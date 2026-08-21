#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class EMAL : Strategy
    {
        public EMAL()
        {
            VendorLicense(1980);
        }
        private const string StrategySignalPrefix = "EMAL";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";
        private const string StopExitSignal = StrategySignalPrefix + "Stop";
        private const string TargetExitSignal = StrategySignalPrefix + "Target";
        private const string TerminalExitSignalPrefix = StrategySignalPrefix + "Exit";
        // Deliberately NOT under TerminalExitSignalPrefix ("EMALExit...") - the touch watchdog's
        // market exit is a distinct, non-retrying path (see EvaluateTargetTouchWatchdog /
        // SubmitTargetTouchMarketExit) and must not be swept into IsTerminalExitOrderName's
        // retry-loop handling, which is unrelated to why this fires.
        private const string TargetTouchExitSignal = StrategySignalPrefix + "TouchExit";

        private enum ProjectXProtectionOrderKind
        {
            StopLoss,
            TakeProfit
        }

        private sealed class ProjectXAccountInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool CanTrade { get; set; }
            public bool IsVisible { get; set; }
        }

        private sealed class ProjectXContractInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string SymbolId { get; set; }
            public bool ActiveContract { get; set; }
        }

        // The Tradovate/Apex quota is shared by every account on the same connection/user.
        // These counters therefore live across EMAL instances, keyed by the NT connection
        // object rather than by account. They intentionally count only order actions EMAL
        // itself requests; manual orders, other strategies and other VPS processes are not
        // visible here, which is why the default ceiling leaves a large safety margin.
        private sealed class SharedOrderRateState
        {
            public readonly Queue<DateTime> ActionsUtc = new Queue<DateTime>();
            public readonly Dictionary<string, int> Reservations = new Dictionary<string, int>();
            public DateTime ProviderBlockedUntilUtc = DateTime.MinValue;
            public string ProviderBlockReason = string.Empty;
        }

        private static readonly object OrderRateGuardSync = new object();
        private static readonly Dictionary<object, SharedOrderRateState> OrderRateStates =
            new Dictionary<object, SharedOrderRateState>();
        private const int NewTradeActionReserve = 6;
        private readonly string orderRateInstanceId = Guid.NewGuid().ToString("N");
        private int rateGuardBlockedEntryCount;

        // ProjectX session and mirror state. The actual entry/exit signal names remain stable
        // EMAL-prefixed names; the assembly version is used only for the strategy display name.
        private string projectXSessionToken = string.Empty;
        private DateTime projectXTokenAcquiredUtc = DateTime.MinValue;
        private List<ProjectXAccountInfo> projectXAccounts;
        private string projectXResolvedContractId = string.Empty;
        private string projectXResolvedInstrumentKey = string.Empty;
        private readonly Dictionary<string, long> projectXLastOrderIds = new Dictionary<string, long>();
        private double projectXLastSyncedStopPrice;
        private double projectXLastSyncedTargetPrice;
        private bool projectXEntryMirrorActive;
        private bool suppressProjectXNextExecutionExit;
        private DateTime projectXOrphanRecoveryDueUtc = DateTime.MinValue;
        private int projectXOrphanRecoveryCount;

        // EMAL-1041: async ProjectX/webhook dispatch. Every ProjectX HTTP round trip used to run
        // synchronously on the strategy thread; measured in live logs, that delayed real order
        // submission (EnterLongLimit/EnterShortLimit) by a median 0.49-0.50s on instances with
        // ProjectX configured, and widened the fill->protection window inside OnExecutionUpdate
        // that the gap-latch work (EMAL-1037/1038) exists to protect. All ProjectX/webhook HTTP
        // now runs on a single dedicated per-instance worker thread, fed by a FIFO queue; the
        // strategy thread only ever enqueues a plain-value work item and returns immediately.
        //
        // Threading contract (READ THIS before touching any ProjectX method):
        //   - The six "mirror state" fields the strategy thread still reads for gating decisions
        //     (projectXEntryMirrorActive, projectXLastSyncedStopPrice/TargetPrice,
        //     suppressProjectXNextExecutionExit, projectXOrphanRecoveryDueUtc/Count) are guarded
        //     by projectXStateLock. EVERY read and write of these six fields, on either thread,
        //     must go through the lock. The worker mutates them itself, from each work item's
        //     OnComplete callback, after the HTTP call resolves - this project chose "guard the
        //     fields with a lock and keep the mutation in the worker" over marshaling results
        //     back to the strategy thread, since NT8 strategies have no cheap "run on next
        //     strategy event" primitive to marshal onto.
        //   - The other ProjectX fields (session token, cached accounts/contract, last order ids)
        //     need NO lock: RunProjectXStartupPreflight touches them synchronously on the
        //     strategy thread once, BEFORE the worker thread is started (see State.Realtime); from
        //     that point on, only the worker thread ever touches them, and it processes the FIFO
        //     queue one item at a time, so there is never more than one thread in this group at
        //     once. Do not call EnsureProjectXSession/TryLoadProjectXAccounts/
        //     TryResolveProjectXContractId/ProjectXPlaceOrder/ProjectXCancelOrders/
        //     ProjectXCancelEntryOrder from the strategy thread after the worker has started, or
        //     this guarantee breaks.
        //   - The worker must NEVER touch NT objects/methods (Instrument, Position, Account,
        //     Order, Time[0]/Close[0], TickSize). Anything instrument- or position-derived a
        //     worker-side method needs (instrument root/key/expiry, TickSize, position side) is
        //     captured as a plain value on the strategy thread at enqueue time and carried on the
        //     ProjectXWorkItem. Print()/ProjectXLog() are the one documented exception - NT8's
        //     Print() is safe from any thread.
        //   - Termination (State.Terminated) is the one place ProjectX HTTP still runs
        //     synchronously: the worker is stopped and drained (bounded wait) first, then
        //     CancelWorkingEntryOnTermination/FlattenProjectXOrphanOnTermination call the same
        //     execution methods directly on the strategy thread, bypassing the queue entirely.
        //     This is safe specifically because the worker has already been joined by that point
        //     - no concurrent access - and keeps FlattenProjectXOrphanOnTermination's existing
        //     bounded-timeout flatten-verification behavior intact.
        private sealed class ProjectXWorkItem
        {
            public string EventType;                       // "buy"/"sell"/"exit"/"cancel"; null for a protection-sync item
            public double EntryPrice;
            public double TakeProfit;
            public double StopLoss;
            public bool IsMarketEntry;
            public int Quantity;
            public ProjectXProtectionOrderKind? ProtectionKind;   // set only for a protection-sync item
            public double ProtectionPrice;
            public string ProtectionReason;
            public int ProtectionExpectedSide;             // 1 = long, 0 = short; captured from Position before enqueue
            public int ProtectionFallbackSize;              // captured Math.Abs(Position.Quantity) fallback, before enqueue
            public string InstrumentRoot;
            public string InstrumentKey;
            public DateTime InstrumentExpiry;
            public bool HasInstrumentExpiry;
            public double TickSizeSnapshot;
            public Action<bool, string> OnComplete;          // (success, rawResponse) - runs on the worker thread
        }

        private readonly object projectXStateLock = new object();
        private System.Collections.Concurrent.BlockingCollection<ProjectXWorkItem> projectXQueue;
        private System.Threading.Thread projectXWorkerThread;

        // Emergency-exit recovery. A rejected market exit must release its latch; otherwise a
        // later partial entry fill can leave the existing stop sized for only part of the position.
        private string terminalExitRetryReason = string.Empty;
        private string terminalExitRetryEntrySignal = string.Empty;
        private DateTime terminalExitRetryDueUtc = DateTime.MinValue;
        private int terminalExitRetryCount;
        private bool terminalExitRetryExhaustedLogged;
        private const int MaxTerminalExitRetries = 8;

        // ---- chart info panel (WPF overlay on ChartControl's parent, not SharpDX) ----
        private const string InfoFooter = "AutoEdge Systems™";
        private Border infoBoxContainer;
        private StackPanel infoBoxRowsPanel;

        private static readonly Brush InfoHeaderFooterGradientBrush = CreateFrozenVerticalGradientBrush(
            Color.FromArgb(240, 0x2A, 0x2F, 0x45),
            Color.FromArgb(240, 0x1E, 0x23, 0x36),
            Color.FromArgb(240, 0x14, 0x18, 0x28));
        private static readonly Brush InfoBodyOddBrush = CreateFrozenBrush(240, 0x0F, 0x0F, 0x17);
        private static readonly Brush InfoBodyEvenBrush = CreateFrozenBrush(240, 0x11, 0x11, 0x18);
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(255, 0xFF, 0xD7, 0x00);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);
        private static readonly Brush InfoStatusTextBrush = CreateFrozenBrush(255, 0xFF, 0x33, 0x33);

        private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            try { if (brush.CanFreeze) brush.Freeze(); }
            catch { }
            return brush;
        }

        private static Brush CreateFrozenVerticalGradientBrush(Color top, Color mid, Color bottom)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0.0),
                EndPoint = new Point(0.5, 1.0)
            };
            brush.GradientStops.Add(new GradientStop(top, 0.0));
            brush.GradientStops.Add(new GradientStop(mid, 0.5));
            brush.GradientStops.Add(new GradientStop(bottom, 1.0));
            try { if (brush.CanFreeze) brush.Freeze(); }
            catch { }
            return brush;
        }

        private EMA ema;
        private Order entryOrder;
        private Order protectiveStopOrder;
        private Order profitTargetOrder;
        private int queuedDirection;
        private double queuedLimitPrice;
        private double queuedTakeProfitPoints;
        private double queuedStopLossPoints;
        private double queuedSignalPrice;
        private int queuedEntryBar = -1;
        private long queuedSignalTimestamp;
        private DateTime queuedSignalUtc = DateTime.MinValue;

        private bool entryCancelPending;
        // EMAL-1044: reason recorded at the moment a cancel is requested (set in
        // CancelEntryOrderIfActive), read back when the confirmation/fill callback lands so
        // the transition trace can tag a gap-breach cancel distinctly from a routine
        // bar-boundary/window/warmup cancel. Diagnostic-only - never read by any live decision.
        private string entryCancelReason = string.Empty;

        // Optional live latency instrumentation. Stopwatch is monotonic and is touched only
        // while Execution Diagnostics is enabled, so the normal live path pays no timing or
        // formatting cost. Diagnostic prints are deliberately emitted only after the relevant
        // NT order/protection method has run.
        private long entryLatencySignalTimestamp;
        private long entryLatencySubmitStartTimestamp;
        private DateTime entryLatencySignalUtc = DateTime.MinValue;
        private DateTime entryLatencySubmitStartUtc = DateTime.MinValue;
        private bool entryLatencyOrderStateLogged;
        private bool entryLatencyExecutionLogged;
        // EMAL-1044: monotonic counter for the full entry-order transition trace (see
        // LogEntryOrderTransition), gated on the same EnableExecutionDiagnostics toggle as the
        // existing latency instrumentation above. Not reset per trade - a running sequence
        // across the whole instance lifetime makes it trivial to spot a dropped/reordered
        // callback when reading the Output log linearly.
        private long entryOrderTransitionSequence;

        // Execution-driven protection. Stops are validated against the live market before
        // submission so a replay gap cannot place a buy stop below market or a sell stop
        // above market. The target is submitted only after the stop is accepted, preventing
        // reuse of an OCO identifier belonging to a rejected order pair.
        private string protectedEntrySignal = string.Empty;
        private double activeTakeProfitPoints;
        private double activeStopLossPoints;
        // Gap-exit latch (EMAL-1037, 2026-08-12). Armed from the signal-tick's known limit
        // price the instant BeginProtectionTracking is called - before order submission, so
        // every instance on the shared feed arms from the identical price/level pair. Updated
        // on every OnMarketData tick from that point forward, so the gap decision in
        // SubmitOrUpdateProtection becomes "did any tick since the signal cross the level",
        // not "what does a live GetCurrentBid/Ask query return right now". This removes the
        // dependency on when each account's own broker fill-confirmation callback happens to
        // land, which was the actual race (see EMAL-1037-changelog.txt).
        private bool gapLatchArmed;
        private int gapLatchDirection;
        private double gapLatchTargetPrice;
        private double gapLatchStopPrice;
        private bool gapTargetBreached;
        private bool gapStopBreached;
        // Target touch-then-convert watchdog (2026-08-14, Codex/Steve, live incident): five
        // accounts had identical working EMALTarget limits at the same price; price traded at
        // the level, only two filled, the other three rode a 20-point reversal into the stop.
        // Sibling to the gap latch above, same tick-driven pattern, but post-fill: watches
        // plannedTargetTouchLevel (the shared planned target level every instance on the feed
        // arms from - see the field comment below) for a touch while the target is still
        // working, and converts to a market exit if it doesn't fill within TargetTouchGraceMs.
        // See EvaluateTargetTouchWatchdog. One-shot per trade; all reset in
        // ResetGapLatchTracking.
        private DateTime targetTouchedUtc = DateTime.MinValue;
        private bool targetTouchWatchdogFired;
        private bool targetTouchWatchdogCancelPending;
        // EMAL-1045: which market data channel produced the first touch (Last/Bid/Ask) under
        // TouchDetectionMode.QuoteOrLast - diagnostic only, printed at conversion time so an
        // export shows whether quote-based detection is what caught a given touch. Meaningless
        // until targetTouchedUtc is set; reset alongside it.
        private MarketDataType targetTouchSource = MarketDataType.Last;
        // Deliberately NOT reset per-trade - "logging once" per the design means once per
        // strategy instance, not once per trade, so this assumption-violated warning doesn't
        // spam every trade once it has fired.
        private bool targetTouchWatchdogScopeGuardLogged;
        // 2026-08-15 (EMAL-1042, Steve/Codex): the watchdog originally compared ticks against
        // desiredProtectionTargetPrice, the actual working target order's price - which is
        // anchored to THIS account's own fill (BeginProtectionTracking/SubmitOrUpdateProtection
        // derive it from the fill, not the plan). Fills differ by a tick or two across accounts
        // on the same signal, so their touch levels differed too, and on a touch-only high some
        // accounts latched while others didn't - reproducing the exact cross-account divergence
        // this watchdog exists to remove (live example: one box 2-of-5 targets filled, sibling
        // box 0-of-6, same signal). Fix: latch the PLANNED target level - the planned entry
        // limit price plus/minus TakeProfit points, same signal-tick data ArmGapLatch already
        // captures - instead of any fill-dependent value. Every instance on the shared feed
        // computes the identical number, because it derives from the plan, not the fill. Armed
        // in ArmGapLatch alongside the gap latch levels; must NEVER be recomputed later from
        // Position.AveragePrice, desiredProtectionTargetPrice, or any other fill-dependent
        // value. 0.0 means "not armed" - EvaluateTargetTouchWatchdog falls back to the old
        // fill-anchored desiredProtectionTargetPrice behavior for that trade in that case (e.g.
        // a future market-entry mode with no planned limit price), and
        // plannedTargetTouchLevelFallbackLogged (instance-lifetime, not reset per trade, same
        // pattern as targetTouchWatchdogScopeGuardLogged above) makes sure that fallback prints
        // once, not every trade. See EMAL-1042-changelog.txt for the full uniformity-over-
        // optionality tradeoff this creates: an account whose real fill sits beyond the shared
        // planned level may now convert a tick before ITS OWN limit price traded - intended,
        // not a bug.
        private double plannedTargetTouchLevel;
        private bool plannedTargetTouchLevelFallbackLogged;
        // Per-window bracket presets, resolved from the Setting popups in DataLoaded.
        private double us0928Tp, us0928Sl, us0955Tp, us0955Sl;
        private double entryFillValue;
        private int entryFilledQuantity;
        private double desiredProtectionTargetPrice;
        private int desiredProtectionQuantity;
        private bool terminalExitPending;

        // Multi-contract protection reconciliation (Steve, 2026-08-18). Off by default; Auto
        // only does anything once Position.Quantity > 1 - the reconciliation method's own
        // inertness guard is what enforces that, not these fields. Per-trade state, reset in
        // both BeginProtectionTracking and ResetProtectionTracking alongside the other
        // protection fields above so a retry count never survives into the next trade.
        private int stopReconcileAttempts;
        private int targetReconcileAttempts;
        private const int MaxReconcileAttempts = 3;

        // Account-level profit guard. Once net liquidation reaches the configured
        // ceiling, the latch remains set for the lifetime of this strategy instance.
        private bool maxAccountBalanceLimitReached;

        // Max Daily Profit (Steve, 2026-08-07): re-added from the pre-1024 implementation,
        // where it was DONE/NEGATIVE as a performance-tuning lever (EMAL_Tuning_Brief.md row
        // 3.5 - rest-of-day continuation after crossing any threshold is +134 to +201 pts, so
        // capping it costs net for no drawdown benefit). That verdict is about the strategy's
        // own edge and stands unchanged. This is being restored for a different purpose
        // entirely: a compliance facility so EMAL can be run on prop-firm eval accounts that
        // impose their own max-daily-profit rule, independent of whether the cap helps or
        // hurts EMAL's own numbers. Default 0 (off). Resets at 18:00 ET (CME trading day /
        // market close), same boundary as the separate (still-removed) points-based Max Daily
        // Profit/Loss variants used - Steve confirmed eval-account rules reset with the
        // session, not the calendar date, so this differs from the pre-1024 dollar
        // implementation, which reset at midnight (Time[0].Date). See GetTradingDay().
        private bool maxDailyProfitLimitReached;
        private double maxDailyProfitStartBalance = double.NaN;
        private DateTime maxDailyProfitDate = DateTime.MinValue;

        private double openEntryPrice;
        private int openEntryDirection;

        // Set by ValidateChart when the strategy is on the wrong series or instrument.
        private bool configurationBlocked;
        private string configurationBlockReason = string.Empty;

        // Tick clock. Time[0] returns the in-progress bar's close stamp, so it cannot be
        // used for elapsed-seconds math. OnMarketData supplies the real tick timestamp.
        private DateTime lastTickTime = DateTime.MinValue;
        private double lastTickPrice;
        private bool sawMarketData;
        // EMAL-1045: latest quote, updated on every Bid/Ask OnMarketData callback regardless of
        // TouchDetectionMode (so a live mode switch never starts from stale zeros). Only read by
        // EvaluateGapLatch/EvaluateTargetTouchWatchdog when TouchDetectionMode is QuoteOrLast.
        // Reset to 0 in ResetGapLatchTracking so a stale quote from the previous trade can never
        // be evaluated against a freshly armed latch before a new quote tick arrives.
        private double lastBidPrice;
        private double lastAskPrice;

        // Fill-rate accounting. Filled trades are the only thing the performance report
        // shows, so signals that never became trades have to be counted here.
        private int signalCount;
        private int filledCount;
        private int cancelBarEndCount;
        private int blockedBarCount;
        private int hardBlockedMinuteBarCount;
        // EMAL-1050: counts bars blocked by the new optional 09:31-09:35 & 9:43 block, separate
        // from hardBlockedMinuteBarCount (the always-on 09:30 block) so the fill-rate summary
        // can report them independently.
        private int additionalBlockedMinuteBarCount;

        // Feature logging. The entry-side fragment is built when the order is submitted, the
        // fill fragment when it fills, and the row is written when the position closes.
        private StreamWriter featureWriter;
        private string pendingEntryFeatures;
        private string pendingFillFeatures;
        private DateTime pendingSubmitTime;
        private double pendingSignalPrice;
        private double pendingFillPrice;
        private double tradeMaePoints;
        private double tradeMfePoints;
        private int pendingDirection;
        private int pendingEntryBar;
        private int loggedRowCount;

        // ---- Per-tick diagnostic logger (Steve, 2026-08-18) ----
        // Captures the exact tick stream (Last/Bid/Ask) the strategy computes on, so two boxes
        // can be diffed after a divergent trade. Replaces relying on NT8's own Market Replay/
        // Historical recording, which proved unreliable (recorded the wrong instrument / cached
        // backfill instead of the live RTH window). Pure observer: never touches signal logic,
        // the watchdog, the gap latch, protection, or orders - read-only in OnMarketData, one
        // extra branch, no effect on anything else. OFF by default; only turned on when hunting
        // a divergence. Realtime-only by construction (LogTick checks State itself), so a
        // Playback/Historical/Analyzer run with this on writes nothing - the diagnostic is for
        // live/paper boxes only, where a Market Replay recording isn't available or trustworthy.
        private StreamWriter tickLogWriter;
        private string resolvedTickLogPath;
        private readonly List<string> tickLogBuffer = new List<string>();
        // MinValue = "no flush yet this instance"; set to UtcNow on the first buffered row so
        // the interval clock starts from first activity, not from a stale prior value.
        private DateTime tickLogLastFlushUtc = DateTime.MinValue;
        private bool tickLogDisabledAfterError;
        private const int TickLogFlushRowCount = 500;
        private const double TickLogFlushIntervalSeconds = 5.0;
        private const string TickLogHeader = "Timestamp,MarketDataType,Price,Volume,Instrument";

        // ---- Research path log (Steve, 2026-07-29) ----
        // For each fill, record the first-touch elapsed seconds to a grid of favourable /
        // adverse price levels, tracked PAST the TP/SL exit up to a horizon. That lets any
        // TP and any SL (wider OR tighter) be reconstructed offline with correct first-touch
        // ordering - a stopgap for tuning while the r45 CLI is out of parity. OFF by default;
        // it never affects live trading and only runs when EnablePathLog is set for a research
        // playback. Grid: 0.5-pt steps to 30 pts each side; 300s horizon.
        private const double PathLogStepPoints = 0.5;
        private const double PathLogMaxPoints = 30.0;
        private const double PathLogHorizonSeconds = 300.0;
        private static readonly int PathLogLevels = (int)(PathLogMaxPoints / PathLogStepPoints);
        private List<PathRecorder> pathRecorders;
        private bool pathLogHeaderWritten;
        private int pathLogFailureCount;
        // Caches ResolvePathLogPath()'s auto-generated path (with its creation-time
        // timestamp) so the timestamp is fixed at first resolution instead of advancing on
        // every WritePathRow call. Only used when PathLogPath is blank (auto-name mode).
        private string resolvedPathLogPath;

        private sealed class PathRecorder
        {
            public DateTime FillTime;      // platform time at fill
            public double FillPrice;
            public int Direction;          // +1 long, -1 short
            public string Session;
            public double[] FavTouch;      // elapsed sec first-touch per level, NaN = never
            public double[] AdvTouch;
        }

        // Session boundaries in minutes-of-day, New York time. Asia and the US 10:30-17:00 cash
        // session were removed entirely (Steve, 2026-08-06) - this strategy now trades only the
        // two NY morning windows below.
        // Boundary moved 2026-08-08 (Steve), then REVERTED same day after the gap was actually
        // measured. EMAL-1031 briefly made the windows CONTIGUOUS (09:28-09:59 / 10:00-10:30,
        // no gap) on Steve's market-structure judgment, explicitly NOT four-halves validated at
        // the time. Steve then unblocked the gap specifically so it could be measured on the
        // current engine (it previously carried zero data - the old boundary made it
        // structurally empty, so the historical "57-minute block screen" never actually tested
        // it). Measured 2026-08-08 on real trade data for the first time: 09:50-09:54 clears the
        // project's 1.22x selectivity bar in ALL FOUR halves (1.32x-1.94x) and reduces maxDD in
        // ALL FOUR halves (12%-56%), for a small and mixed net effect (+$695/-$156/-$128/+$667
        // across the four folds) - exactly the "cuts drawdown, costs some net" shape Steve has
        // said is acceptable for this project. This corroborates an independent 2026-07-29
        // finding on real NT8 Playback data (five-min-bucket-stats.csv, different single global
        // bracket) that the old 09:50 bucket ran weaker than its neighbors (83.33% WR vs
        // ~88.5-88.6% either side). Reverted back to the original gapped boundary accordingly.
        // Constant names (Us0928.../Us0955...) still identify "the first window" / "the second
        // window" throughout the source.
        // NY-anchored boundaries. Globex reopen and the US cash session never drift, because
        // CME (Chicago) and New York share the same DST dates.
        // US 09:28-09:50 window (Steve, 2026-08-02: start moved to 09:28, the real researched
        // start - see below).
        private const int Us0928StartMinute = 9 * 60 + 28; // 09:28 ET, US 09:28-09:50 opens
        private const int Us0928EndMinute = 9 * 60 + 50;   // 09:50 ET (exclusive)
        private const int Us0955StartMinute = 9 * 60 + 55; // 09:55 ET, US 09:55-10:30 opens - the
                                                             // 09:50-09:54 gap between the windows
                                                             // is a deliberate, measured no-trade
                                                             // block (see comment block above)
        private const int Us0955EndMinute = 10 * 60 + 30;  // 10:30 ET

        // 09:28 is a real researched boundary (Steve, 2026-08-01), from the per-minute scan of
        // NT8 Playback ground truth (results/EMAL-5m-position-scan-apr24-jul24.md and the
        // in-chat 09:20-09:39 per-minute breakdown): 09:28 held up as strong on BOTH date halves
        // at reasonable sample size. (09:29 was also excluded here same-day, then Steve reversed
        // that - 09:29 trades normally again; see EMAL-18-changelog.txt section 6.) Originally
        // this was a separate entry-timing gate layered on top of a session that still started
        // at 09:20 (needed then to avoid the window's early minutes falling through into the
        // Europe session - see EMAL-21-changelog.txt / EMAL-22-changelog.txt for that history).
        // Europe was removed entirely 2026-08-02 (Steve: "I never want to use this bot on
        // London"), which was the only reason the session boundary itself couldn't just be
        // 09:28 - the gate and the boundary are now the same thing, so the separate gate
        // (IsUs0928EarlyShapeAllowed) was deleted and Us0928StartMinute is now the one and only
        // boundary GetSessionIndex uses for this window.

        // All time rules are evaluated in New York time regardless of how NinjaTrader's
        // display timezone is configured. TimeZoneInfo carries the full DST rule set, so
        // the spring and autumn shifts are handled automatically - no seasonal code.
        private TimeZoneInfo platformZone;
        private TimeZoneInfo easternZone;

        private const string FeatureHeader =
            "EntryTimeET,EntryTimeUTC,DayOfWeek,HHmm,"
            + "Session,Direction,EntryMode,LimitRef,SignalPrice,Ema,Slope,SlopePrev,SlopeAccel,ReqSlope,LimitOffset,"
            + "DistToEma,TpPoints,SlPoints,"
            + "Bar1Open,Bar1High,Bar1Low,Bar1Close,Bar1Volume,"
            + "Bar2Open,Bar2High,Bar2Low,Bar2Close,Bar2Volume,"
            + "Bar3Open,Bar3High,Bar3Low,Bar3Close,Bar3Volume,"
            + "AvgVolume20,FillPrice,FillDelaySec,"
            + "ExitTime,ExitPrice,ExitReason,ProfitPoints,IsWin,BarsHeld,MaePoints,MfePoints";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "EMA direction strategy for NQ (1-minute bars) with market or passive "
                    + "bid/ask limit entries and fixed take-profit and stop-loss brackets.";
                Name = "EMAL";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                BarsRequiredToTrade = 1;

                // Aug 13 live incident (3-lot Apex account): a manual broker-side flatten left a
                // resting entry limit orphaned when the instance was disabled, and
                // StartBehavior=WaitUntilFlat then refused to re-enable ("Unable to cancel out
                // live orders. Strategy was not started."). Disabling now cancels the resting
                // entry so it can't be orphaned at the broker.
                // PARKED 2026-08-14: CS0103, neither name is a real Strategy member in this NT8
                // SDK version (confirmed against every other real NT8 source in this repo - zero
                // hits anywhere). Commented out rather than guessed at a replacement, to avoid
                // shipping an unverified change to live order-disable behavior. Ask Andreas for
                // the correct mechanism (or confirmation this is a platform-level NinjaTrader
                // setting, not a scriptable property) before re-enabling these two lines.
                // CancelEntriesOnStrategyDisable = true;
                // CancelExitsOnStrategyDisable = false;

                Version = EMALVersion.version_1050;   // bump on every new cut; see enum comment

                EmaPeriod = 9;
                MinimumEmaSlopePoints = 0.75;   // fallback for a minute outside both tracked windows
                Contracts = 1;

                MaxAccountBalance = 0.0;
                MaxDailyProfit = 0.0;
                EnableTargetTouchWatchdog = true;   // see comment on the property below
                TargetTouchGraceMs = 400;
                TouchDetectionMode = EMALTouchDetectionMode.QuoteOrLast;   // see comment on the property below
                MultiContractProtectionFix = EMALMultiContractProtectionFix.Off;   // see comment on the property below
                Block0931To0935 = false;   // OFF by default; see comment on the property below
                OrderActionLimitPerHour = 1100;

                ProjectXApiBaseUrl = "https://api.topstepx.com";
                ProjectXTradeAllAccounts = false;
                ProjectXUsername = string.Empty;
                ProjectXApiKey = string.Empty;
                ProjectXAccountId = string.Empty;
                ProjectXContractId = string.Empty;

                // Only the two US morning windows exist now (Steve, 2026-08-06); Asia and the
                // US 10:30-17:00 session were removed entirely, not just defaulted off.
                // Per-window bracket presets (Steve, 2026-07-30). Window 2 defaults to
                // TP4/SL18 = current behaviour. ResolveWindowPresets() applies them in DataLoaded.
                // Selecting "Disabled" on either Setting popup turns that window off (2026-08-06);
                // there is no separate Enabled toggle anymore, see IsSessionEnabled.
                // Window 1 default changed 2026-08-08 (Steve): TP5/SL18/slope2.75 -> slope3.50.
                // Adopted per EMAL_Analysis_Plan.md §8.10 - NT8-confirmed on the risk-over-profit
                // tie-breaker (drawdown down ~9-10%, net down ~3%, both directions replicated
                // independently on two NT8 halves and on the r56 engine's four-halves split).
                // Not a dominant win - §8.10 flags it explicitly as a preference call, not a
                // clean improvement, and closes 09:28 to further sweeping on this bracket.
                // Both windows default to Disabled as of 2026-08-09 (Steve): the strategy no
                // longer opts a fresh instance into live trading on either window by default -
                // a preset must be chosen explicitly. This is a real behavior change, not a
                // UI-only default; an instance left at defaults now takes zero trades.
                Us0928Setting = EMALUs0928Setting.Disabled;
                Us0955Setting = EMALUs0955Setting.Disabled;

                Us0928MinimumSlope = 2.75;   // overwritten by ResolveWindowPresets from the Setting popup
                Us0955MinimumSlope = 2.75;

                EnableFeatureLog = false;   // logging OFF by default (Steve, 2026-07-31)
                FeatureLogPath = string.Empty;   // blank -> version-named auto-path, see ResolveFeatureLogPath
                EnablePathLog = false;   // research-only; never on for live trading
                PathLogPath = string.Empty;
                EnableExecutionDiagnostics = false;

                EnableTickLogging = false;   // diagnostic OFF by default; only for divergence hunting
                TickLogTag = string.Empty;
                TickLogFolder = string.Empty;   // blank -> NinjaTrader.Core.Globals.UserDataDir\ticklogs
            }
            else if (State == State.DataLoaded)
            {
                ResolveWindowPresets();   // window Setting popups -> per-window TP/SL/slope
                ValidateChart();
                maxAccountBalanceLimitReached = false;
                maxDailyProfitLimitReached = false;
                maxDailyProfitStartBalance = double.NaN;
                maxDailyProfitDate = DateTime.MinValue;

                SetupTimeZones();

                pathRecorders = new List<PathRecorder>();
                pathLogHeaderWritten = false;
                pathLogFailureCount = 0;

                // Fresh instance (or a re-enable) starts the tick logger clean: unresolved path,
                // no error latch carried over, empty buffer, flush clock reset.
                resolvedTickLogPath = null;
                tickLogDisabledAfterError = false;
                tickLogBuffer.Clear();
                tickLogLastFlushUtc = DateTime.MinValue;

                // Bind explicitly to the primary Close series. Combined with the Update()
                // call in OnBarUpdate, this prevents a re-enabled Playback/chart instance
                // from asking a lagging hosted EMA for unavailable bars.
                ema = EMA(Close, EmaPeriod);
                AddChartIndicator(ema);
            }
            else if (State == State.Realtime)
            {
                TransitionTrackedOrderReferencesToRealtime();
                // Preflight runs first, strategy thread only, and warms the session/account/
                // contract cache; the worker starts only after it completes, so those cache
                // fields never see the strategy and worker threads touch them at the same time
                // (see the threading-contract comment above the ProjectXWorkItem class).
                RunProjectXStartupPreflight();
                StartProjectXWorker();

                // Draw the panel the moment the strategy goes live, instead of waiting for
                // OnBarUpdate's first tick of a bar - historical warmup skips OnBarUpdate's
                // panel draw entirely (see the State.Historical check there), so without this
                // a strategy enabled mid-bar could sit with no visible panel for up to a full
                // bar period. (Steve, 2026-08-06 - reported panel "waits for a candle to print".)
                UpdateInfoText();
            }
            else if (State == State.Terminated)
            {
                // Stop and drain the worker BEFORE the two termination methods below run their
                // own direct synchronous sends - otherwise those methods could read stale mirror
                // state while the worker is still mid-flight on an earlier item.
                StopProjectXWorker();
                CancelWorkingEntryOnTermination();
                FlattenProjectXOrphanOnTermination();
                ReleaseOrderRateReservation();
                FlushAllPathRecorders();
                PrintFillRateSummary();
                CloseFeatureLog();
                CloseTickLog();
                DisposeInfoBoxOverlay();
            }
        }

        private void SetupTimeZones()
        {
            try
            {
                // NinjaTrader returns bar times in the General Options display timezone.
                platformZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
                easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

                Print(string.Format(
                    "EMAL: platform={0} | NY-anchored boundaries active.",
                    platformZone == null ? "unknown" : platformZone.Id));
            }
            catch (Exception ex)
            {
                platformZone = null;
                easternZone = null;
                Print("EMAL: timezone setup failed, using platform time as-is. " + ex.Message);
            }
        }

        private bool IsHistoricalTradeSimulationContext()
        {
            // Analyzer uses the synthetic Backtest account and must retain historical fills.
            // Playback and broker-account instances use historical bars for indicator warmup
            // only, preventing historical managed orders from suppressing realtime entries.
            return Account == null
                || string.Equals(Account.Name, "Backtest", StringComparison.OrdinalIgnoreCase);
        }

        private DateTime ConvertToZone(DateTime platformTime, TimeZoneInfo target)
        {
            if (target == null || platformZone == null || platformZone.Equals(target))
                return platformTime;

            try
            {
                return TimeZoneInfo.ConvertTime(
                    DateTime.SpecifyKind(platformTime, DateTimeKind.Unspecified),
                    platformZone,
                    target);
            }
            catch (Exception)
            {
                return platformTime;
            }
        }

        // "Eastern Standard Time" is the Windows ID for the whole zone, EST and EDT alike.
        // ConvertTime applies the correct offset for that specific date automatically.
        private DateTime ConvertToEastern(DateTime platformTime)
        {
            if (easternZone == null || platformZone == null || platformZone.Equals(easternZone))
                return platformTime;

            try
            {
                return TimeZoneInfo.ConvertTime(
                    DateTime.SpecifyKind(platformTime, DateTimeKind.Unspecified),
                    platformZone,
                    easternZone);
            }
            catch (Exception)
            {
                return platformTime;
            }
        }

        // ET/EST for the info panel's Session: row (Steve, 2026-08-09; confirmed the exact
        // pair he wants 2026-08-09: "ET" while daylight saving is in effect, "EST" during
        // standard time - not the EDT/EST pair). easternZone covers both offsets under one
        // Windows ID (see ConvertToEastern's comment) - this asks it which one applies to the
        // specific bar's date, so a backtest crossing a DST transition shows the correct label
        // for each bar rather than whatever's true today. Falls back to "ET" if the zone
        // lookup itself ever failed (see DataLoaded) - same label as the daylight case, since
        // at that point the actual offset is unknown anyway.
        private string GetEasternZoneAbbreviation(DateTime easternTime)
        {
            if (easternZone == null)
                return "ET";

            return easternZone.IsDaylightSavingTime(easternTime) ? "ET" : "EST";
        }

        private DateTime ConvertToUtc(DateTime platformTime)
        {
            if (platformZone == null)
                return platformTime;

            try
            {
                return TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(platformTime, DateTimeKind.Unspecified),
                    platformZone);
            }
            catch (Exception)
            {
                return platformTime;
            }
        }

        // Bar OPEN in New York time. NinjaTrader stamps bars with their CLOSING time, so the
        // in-progress bar reads one period ahead; entries fire at the open.
        private DateTime GetBarOpenRaw()
        {
            return Time[0].AddMinutes(-BarsPeriod.Value);
        }

        // 3 = US 09:28-09:50, 5 = US 09:55-10:30, -1 = outside both windows (the two windows
        // are contiguous, no gap between them - see the boundary comment above).
        private int GetSessionIndex(DateTime platformTime)
        {
            DateTime ny = ConvertToZone(platformTime, easternZone);
            int nyMinute = ny.Hour * 60 + ny.Minute;

            if (nyMinute >= Us0928StartMinute && nyMinute < Us0928EndMinute)
                return 3;

            if (nyMinute >= Us0955StartMinute && nyMinute < Us0955EndMinute)
                return 5;

            return -1;
        }

        private static string SessionName(int index)
        {
            switch (index)
            {
                case 3: return "9:28-9:50";
                case 5: return "9:55-10:30";
                default: return "Halt";
            }
        }

        private bool IsSessionEnabled(int index)
        {
            switch (index)
            {
                case 3: return Us0928Setting != EMALUs0928Setting.Disabled;
                case 5: return Us0955Setting != EMALUs0955Setting.Disabled;
                default: return false;
            }
        }

        // Per-session slope threshold. Falls back to the global value for a minute outside
        // both tracked windows.
        private double GetConfiguredSlope(DateTime platformTime)
        {
            switch (GetSessionIndex(platformTime))
            {
                case 3: return Math.Abs(Us0928MinimumSlope);
                case 5: return Math.Abs(Us0955MinimumSlope);
                default: return Math.Abs(MinimumEmaSlopePoints);
            }
        }

        // The entry gate. AtrRatio scaling was tested and REJECTED (brief v2.3 3.4): at matched
        // selectivity Points won on both expectancy and drawdown. Mode removed in v14.
        private double GetRequiredSlope(DateTime platformTime)
        {
            return GetConfiguredSlope(platformTime);
        }

        // Fallback stop distance if a computed stop is ever unexpectedly zero (Steve, 2026-08-06,
        // replacing the removed global StopLossPoints property). Should never actually engage -
        // both window presets always specify a real SL - this exists only so a bug produces a
        // safe, known-sane stop instead of a zero-distance one.
        private const double DefaultSafetyStopLossPoints = 18.0;

        // Per-window brackets (Steve, 2026-07-30). The two US windows each pick a preset
        // (TP/SL/slope) via a popup. There is no global TP/SL anymore (removed 2026-08-06, see
        // EMAL-1023-changelog.txt) - outside both windows there is nothing to configure, so these
        // return NaN and callers (currently only the info panel) must handle that as "n/a".
        private double GetConfiguredTakeProfit()
        {
            switch (GetSessionIndex(GetBarOpenRaw()))
            {
                case 3: return us0928Tp;
                case 5: return us0955Tp;
                default: return double.NaN;
            }
        }

        private double GetConfiguredStopLoss()
        {
            switch (GetSessionIndex(GetBarOpenRaw()))
            {
                case 3: return us0928Sl;
                case 5: return us0955Sl;
                default: return double.NaN;
            }
        }

        // Resolves each window's Setting popup into its TP / SL / slope. The slope is written
        // back into the per-window Us*MinimumSlope so GetConfiguredSlope keeps working unchanged.
        private void ResolveWindowPresets()
        {
            switch (Us0928Setting)
            {
                case EMALUs0928Setting.Disabled:                   us0928Tp = 5; us0928Sl = 18; Us0928MinimumSlope = 2.75; break;   // window is off; values are inert, see IsSessionEnabled
                case EMALUs0928Setting.P1_ENG_TP4_SL18_Slope2_75:  us0928Tp = 4; us0928Sl = 18; Us0928MinimumSlope = 2.75; break;
                case EMALUs0928Setting.P2_ENG_TP3_SL18_Slope2_75:  us0928Tp = 3; us0928Sl = 18; Us0928MinimumSlope = 2.75; break;
                default: /* TP4_SL18_Slope2_75 */          us0928Tp = 4; us0928Sl = 18; Us0928MinimumSlope = 2.75; break;
            }
            switch (Us0955Setting)
            {
                case EMALUs0955Setting.Disabled:                   us0955Tp = 4; us0955Sl = 18; Us0955MinimumSlope = 2.75; break;   // window is off; values are inert, see IsSessionEnabled
                case EMALUs0955Setting.P1_ENG_TP4_SL18_Slope2_75:  us0955Tp = 4; us0955Sl = 18; Us0955MinimumSlope = 2.75; break;
                case EMALUs0955Setting.P2_ENG_TP3_SL18_Slope2_75:  us0955Tp = 3; us0955Sl = 18; Us0955MinimumSlope = 2.75; break;
                case EMALUs0955Setting.P3_ENG_TP3_SL16_Slope2_75:  us0955Tp = 3; us0955Sl = 16; Us0955MinimumSlope = 2.75; break;
                default: /* TP4_SL18_Slope2_75 */          us0955Tp = 4; us0955Sl = 18; Us0955MinimumSlope = 2.75; break;
            }
        }

        private static string N(double v)
        {
            return v.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private string ResolveVersionNumberString()
        {
            const string prefix = "version_";
            string name = Version.ToString();
            return name.StartsWith(prefix, StringComparison.Ordinal)
                ? name.Substring(prefix.Length)
                : name;
        }

        private string ResolveFeatureLogPath()
        {
            if (!string.IsNullOrEmpty(FeatureLogPath))
                return FeatureLogPath;

            // Timestamped at first call only - this method only runs once per instance
            // (guarded by featureWriter == null in WriteFeatureRow below), so the stamp is
            // fixed at file-creation time, not recomputed on every row.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                string.Format("EMAL_v{0}_log_{1}.csv", ResolveVersionNumberString(),
                    DateTime.Now.ToString("yyyy-MM-dd hh-mm tt", CultureInfo.InvariantCulture)));
        }

        private void WriteFeatureRow(string row)
        {
            try
            {
                if (featureWriter == null)
                {
                    string path = ResolveFeatureLogPath();
                    bool isNew = !File.Exists(path);

                    featureWriter = new StreamWriter(path, true);

                    if (isNew)
                        featureWriter.WriteLine(FeatureHeader);

                    Print("Feature log -> " + path);
                }

                featureWriter.WriteLine(row);
                loggedRowCount++;
            }
            catch (Exception ex)
            {
                Print("Feature log write failed: " + ex.Message);
                EnableFeatureLog = false;
            }
        }

        private void CloseFeatureLog()
        {
            if (featureWriter == null)
                return;

            try
            {
                featureWriter.Flush();
                featureWriter.Close();
                Print(string.Format("Feature log closed, {0} rows written.", loggedRowCount));
            }
            catch (Exception ex)
            {
                Print("Feature log close failed: " + ex.Message);
            }

            featureWriter = null;
        }

        // Captured at order submission, while the signal bar's context is still current.
        private void CaptureEntryFeatures(int direction, double signalPrice, double takeProfit, double stopLoss)
        {
            if (!EnableFeatureLog)
                return;

            DateTime barOpenRaw = GetBarOpenRaw();
            DateTime barOpen = ConvertToEastern(barOpenRaw);
            DateTime barOpenUtc = ConvertToUtc(barOpenRaw);
            double slope = ema[1] - ema[2];
            double slopePrev = ema[2] - ema[3];

            double avgVol = 0.0;
            for (int i = 1; i <= 20; i++)
                avgVol += Volume[i];
            avgVol /= 20.0;

            pendingSubmitTime = lastTickTime != DateTime.MinValue ? lastTickTime : Time[0];
            pendingSignalPrice = signalPrice;
            pendingDirection = direction;

            pendingEntryFeatures = string.Join(",", new string[]
            {
                barOpen.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                barOpenUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                barOpen.DayOfWeek.ToString(),
                (barOpen.Hour * 100 + barOpen.Minute).ToString(CultureInfo.InvariantCulture),
                SessionName(GetSessionIndex(barOpenRaw)),
                direction.ToString(CultureInfo.InvariantCulture),

                // EntryMode/LimitRef columns kept for CSV schema stability; both are now
                // always constant since Entry Order Type is fixed to Limit(BidAsk) (2026-08-06).
                "Limit",
                "BidAsk",

                N(signalPrice),
                N(ema[1]),
                N(slope),
                N(slopePrev),
                N(slope - slopePrev),
                N(GetRequiredSlope(barOpenRaw)),
                N(0.0), // LimitOffset column kept for CSV schema stability; offset removed 2026-08-06, always 0
                N((signalPrice - ema[1]) * direction),
                N(takeProfit),
                N(stopLoss),
                N(Open[1]), N(High[1]), N(Low[1]), N(Close[1]), N(Volume[1]),
                N(Open[2]), N(High[2]), N(Low[2]), N(Close[2]), N(Volume[2]),
                N(Open[3]), N(High[3]), N(Low[3]), N(Close[3]), N(Volume[3]),
                N(avgVol)
            });
        }

        private void CaptureFillFeatures(double fillPrice, DateTime fillTime)
        {
            if (!EnableFeatureLog || pendingEntryFeatures == null)
                return;

            double delay = pendingSubmitTime == DateTime.MinValue
                ? 0.0
                : (fillTime - pendingSubmitTime).TotalSeconds;

            // Kept as a field: Position.AveragePrice is already reset by the time the exit fires.
            pendingFillPrice = fillPrice;
            pendingEntryBar = CurrentBar;

            // Excursion tracking starts at the fill and runs until the exit. Both are stored
            // as positive point distances, matching NinjaTrader's MAE/MFE convention.
            tradeMaePoints = 0.0;
            tradeMfePoints = 0.0;

            StartPathRecorder(fillPrice, pendingDirection, fillTime);

            pendingFillFeatures = string.Join(",", new string[]
            {
                N(fillPrice),
                N(delay < 0.0 ? 0.0 : delay)
            });
        }

        private void CaptureExitAndWrite(double exitPrice, DateTime exitTime, string exitReason)
        {
            if (!EnableFeatureLog || pendingEntryFeatures == null || pendingFillFeatures == null)
            {
                pendingEntryFeatures = null;
                pendingFillFeatures = null;
                return;
            }

            double profitPoints = (exitPrice - pendingFillPrice) * pendingDirection;

            WriteFeatureRow(string.Join(",", new string[]
            {
                pendingEntryFeatures,
                pendingFillFeatures,
                exitTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                N(exitPrice),
                exitReason ?? string.Empty,
                N(profitPoints),
                profitPoints > 0.0 ? "1" : "0",
                (CurrentBar - pendingEntryBar).ToString(CultureInfo.InvariantCulture),
                N(tradeMaePoints),
                N(tradeMfePoints)
            }));

            pendingEntryFeatures = null;
            pendingFillFeatures = null;
        }

        private void PrintFillRateSummary()
        {
            if (signalCount == 0)
                return;

            int cancelled = cancelBarEndCount;

            Print("================ EMAL fill rate ================");
            Print(string.Format("      US 0928-0950       : {0}  slope {1}", Us0928Setting, Us0928MinimumSlope));
            Print(string.Format("      (block 0950-0955, no trade)"));
            Print(string.Format("      US 0955-1030       : {0}  slope {1}", Us0955Setting, Us0955MinimumSlope));
            Print(string.Format("  bars blocked        : {0}  (session gate)", blockedBarCount));
            Print(string.Format("  9:30 hard block     : bars blocked: {0}", hardBlockedMinuteBarCount));
            Print(string.Format("  9:31-9:35 & 9:43 block : enabled={0}  bars blocked: {1}", Block0931To0935, additionalBlockedMinuteBarCount));
            Print(string.Format("  order rate guard    : always on / {0} actions (entries blocked: {1})",
                OrderActionLimitPerHour, rateGuardBlockedEntryCount));
            Print(string.Format("  signals generated   : {0}", signalCount));
            Print(string.Format("  filled              : {0}  ({1:F1}%)",
                filledCount, 100.0 * filledCount / signalCount));
            Print(string.Format("  cancelled           : {0}  ({1:F1}%)",
                cancelled, 100.0 * cancelled / signalCount));
            Print(string.Format("      at bar end      : {0}", cancelBarEndCount));
            Print("===============================================");
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // EMAL-1046: pure observer, first thing in the method, ahead of every dispatch
            // branch below - never returns early, never mutates anything the rest of this
            // method reads, so its presence cannot change signal/order behavior whether
            // EnableTickLogging is true or false. See the field-block comment above for scope.
            if (EnableTickLogging)
                LogTick(e);

            // EMAL-1045: Bid/Ask ticks are handled separately from the Last-tick path below -
            // they never touch sawMarketData/lastTickTime/lastTickPrice (that clock stays
            // Last-only, unchanged) and only feed the new quote-based touch/breach detection,
            // itself gated on TouchDetectionMode inside HandleQuoteTick.
            if (e.MarketDataType == MarketDataType.Bid || e.MarketDataType == MarketDataType.Ask)
            {
                HandleQuoteTick(e);
                return;
            }

            if (e.MarketDataType != MarketDataType.Last)
                return;

            sawMarketData = true;
            lastTickTime = e.Time;
            lastTickPrice = e.Price;

            // Keep the always-on tick clock above, but do not repeatedly enter five helper
            // methods when their feature/state is inactive. With many instances on one NQ/MNQ
            // feed this is the normal flat-state path for almost every tick.
            if (gapLatchArmed)
                EvaluateGapLatch(MarketDataType.Last, e.Price, e.Time);

            if (EnableTargetTouchWatchdog && !targetTouchWatchdogFired
                && Position.MarketPosition != MarketPosition.Flat)
            {
                EvaluateTargetTouchWatchdog(MarketDataType.Last, e.Price, e.Time);
            }

            if (EnableFeatureLog && pendingFillFeatures != null && pendingDirection != 0
                && pendingFillPrice > 0.0)
            {
                TrackExcursion();
            }

            if (EnablePathLog && pathRecorders != null && pathRecorders.Count > 0)
                UpdatePathRecorders();

            bool projectXOrphanCheckDue;
            lock (projectXStateLock)
                projectXOrphanCheckDue = projectXEntryMirrorActive && projectXOrphanRecoveryDueUtc != DateTime.MinValue;
            if (projectXOrphanCheckDue)
                EvaluateProjectXOrphanRecovery();

            if (!string.IsNullOrWhiteSpace(terminalExitRetryReason))
                EvaluateTerminalExitRecovery();
        }

        // EMAL-1045: Bid/Ask dispatch. Stores the latest quote unconditionally (so a live mode
        // switch never starts from stale zeros), then - only under TouchDetectionMode.QuoteOrLast
        // - feeds the same two evaluators the Last path already uses. RealtimeErrorHandling is
        // IgnoreAllErrors, so this is wrapped explicitly rather than letting a fault vanish
        // silently (see class-level remarks on that setting).
        private void HandleQuoteTick(MarketDataEventArgs e)
        {
            try
            {
                if (e.MarketDataType == MarketDataType.Bid)
                    lastBidPrice = e.Price;
                else if (e.MarketDataType == MarketDataType.Ask)
                    lastAskPrice = e.Price;
                else
                    return;

                if (TouchDetectionMode != EMALTouchDetectionMode.QuoteOrLast)
                    return;

                if (gapLatchArmed)
                    EvaluateGapLatch(e.MarketDataType, e.Price, e.Time);

                if (EnableTargetTouchWatchdog && !targetTouchWatchdogFired
                    && Position.MarketPosition != MarketPosition.Flat)
                {
                    EvaluateTargetTouchWatchdog(e.MarketDataType, e.Price, e.Time);
                }
            }
            catch (Exception ex)
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | QUOTE HANDLING ERROR | type={1} price={2:F2} | {3}",
                    e.Time, e.MarketDataType, e.Price, ex.Message));
            }
        }

        // ---- Per-tick diagnostic logger ----
        // Called once per OnMarketData invocation, for Last/Bid/Ask only (see the field-block
        // comment for scope/rationale). Buffers rows in memory; never writes to disk per tick -
        // FlushTickLog is only called from here when a threshold is crossed, and from
        // CloseTickLog at State.Terminated. Any failure disables further logging for the rest
        // of this instance's life rather than retrying or throwing - RealtimeErrorHandling is
        // IgnoreAllErrors for the strategy as a whole, so this diagnostic path is not allowed to
        // rely on that; it catches its own faults explicitly.
        private void LogTick(MarketDataEventArgs e)
        {
            if (State != State.Realtime || tickLogDisabledAfterError)
                return;

            string typeLabel;
            if (e.MarketDataType == MarketDataType.Last) typeLabel = "Last";
            else if (e.MarketDataType == MarketDataType.Bid) typeLabel = "Bid";
            else if (e.MarketDataType == MarketDataType.Ask) typeLabel = "Ask";
            else return;   // this diagnostic only cares about the three trade/quote types above

            try
            {
                DateTime stamp = e.Time == DateTime.MinValue ? DateTime.Now : e.Time;
                string instrumentName = Instrument == null ? string.Empty : Instrument.FullName;

                tickLogBuffer.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2:F2},{3},{4}",
                    stamp.ToString("yyyyMMdd HHmmss.fff", CultureInfo.InvariantCulture),
                    typeLabel, e.Price, e.Volume, instrumentName));

                if (tickLogLastFlushUtc == DateTime.MinValue)
                    tickLogLastFlushUtc = DateTime.UtcNow;   // start the interval clock from first activity

                bool rowThresholdHit = tickLogBuffer.Count >= TickLogFlushRowCount;
                bool timeThresholdHit = (DateTime.UtcNow - tickLogLastFlushUtc).TotalSeconds >= TickLogFlushIntervalSeconds;

                if (rowThresholdHit || timeThresholdHit)
                    FlushTickLog();
            }
            catch (Exception ex)
            {
                Print("Tick log error, disabling further tick logging: " + ex.Message);
                tickLogDisabledAfterError = true;
                tickLogBuffer.Clear();
            }
        }

        private string ResolveTickLogFolder()
        {
            string folder = TickLogFolder;
            if (string.IsNullOrWhiteSpace(folder))
                folder = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "ticklogs");

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            return folder;
        }

        // Timestamped-by-day, tagged, resolved once per instance (guarded by
        // resolvedTickLogPath == null, reset in State.DataLoaded) so a mid-session flush never
        // recomputes the date and silently starts writing to a different file.
        private string ResolveTickLogPath()
        {
            if (resolvedTickLogPath != null)
                return resolvedTickLogPath;

            string folder = ResolveTickLogFolder();
            string instrumentName = Instrument == null ? "UNKNOWN" : Instrument.FullName;

            resolvedTickLogPath = Path.Combine(folder, string.Format(CultureInfo.InvariantCulture,
                "{0}_{1}_{2}.csv",
                SanitizeForFileName(instrumentName),
                SanitizeForFileName(TickLogTag),
                DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));

            return resolvedTickLogPath;
        }

        // Instrument full names contain spaces/slashes (e.g. "NQ 12-26"); the tag is
        // user-typed. Replace anything that isn't legal in a filename rather than reject it.
        private static string SanitizeForFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '-' : c);
            return sb.ToString();
        }

        // Opens the StreamWriter once (append mode - a restart mid-session continues the same
        // day's file rather than truncating it) and reuses it across flushes; never opened or
        // closed per row. Writes the header only when the file didn't already exist.
        private void FlushTickLog()
        {
            if (tickLogBuffer.Count == 0)
            {
                tickLogLastFlushUtc = DateTime.UtcNow;
                return;
            }

            try
            {
                if (tickLogWriter == null)
                {
                    string path = ResolveTickLogPath();
                    bool isNew = !File.Exists(path);

                    tickLogWriter = new StreamWriter(path, true);

                    if (isNew)
                        tickLogWriter.WriteLine(TickLogHeader);

                    Print("Tick log -> " + path);
                }

                for (int i = 0; i < tickLogBuffer.Count; i++)
                    tickLogWriter.WriteLine(tickLogBuffer[i]);

                tickLogWriter.Flush();
                tickLogBuffer.Clear();
                tickLogLastFlushUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Print("Tick log flush failed, disabling further tick logging: " + ex.Message);
                tickLogDisabledAfterError = true;
                tickLogBuffer.Clear();
            }
        }

        // Called from State.Terminated so a partially-filled buffer at shutdown isn't lost.
        private void CloseTickLog()
        {
            if (tickLogBuffer.Count > 0)
                FlushTickLog();

            if (tickLogWriter == null)
                return;

            try
            {
                tickLogWriter.Flush();
                tickLogWriter.Close();
            }
            catch (Exception ex)
            {
                Print("Tick log close failed: " + ex.Message);
            }

            tickLogWriter = null;
        }

        // ---- Research path log ----
        private void StartPathRecorder(double fillPrice, int direction, DateTime fillTime)
        {
            if (!EnablePathLog || direction == 0 || fillPrice <= 0.0)
                return;
            if (pathRecorders == null)
                pathRecorders = new List<PathRecorder>();

            var fav = new double[PathLogLevels];
            var adv = new double[PathLogLevels];
            for (int k = 0; k < PathLogLevels; k++) { fav[k] = double.NaN; adv[k] = double.NaN; }

            pathRecorders.Add(new PathRecorder
            {
                FillTime = fillTime,
                FillPrice = fillPrice,
                Direction = direction,
                Session = SessionName(GetSessionIndex(GetBarOpenRaw())),
                FavTouch = fav,
                AdvTouch = adv
            });
        }

        // Runs on every Last tick. Records first-touch times PAST the position's exit, so a
        // wider bracket can be reconstructed, then flushes at the horizon.
        private void UpdatePathRecorders()
        {
            if (!EnablePathLog || pathRecorders == null || pathRecorders.Count == 0)
                return;

            for (int i = pathRecorders.Count - 1; i >= 0; i--)
            {
                PathRecorder r = pathRecorders[i];
                double elapsed = (lastTickTime - r.FillTime).TotalSeconds;
                double fav = (lastTickPrice - r.FillPrice) * r.Direction;
                double adv = -fav;

                for (int k = 0; k < PathLogLevels; k++)
                {
                    double level = (k + 1) * PathLogStepPoints;
                    if (double.IsNaN(r.FavTouch[k]) && fav >= level) r.FavTouch[k] = elapsed;
                    if (double.IsNaN(r.AdvTouch[k]) && adv >= level) r.AdvTouch[k] = elapsed;
                }

                if (elapsed >= PathLogHorizonSeconds)
                {
                    FlushPathRecorder(r);
                    pathRecorders.RemoveAt(i);
                }
            }
        }

        private void FlushAllPathRecorders()
        {
            if (pathRecorders == null)
                return;
            foreach (PathRecorder r in pathRecorders)
                FlushPathRecorder(r);
            pathRecorders.Clear();
        }

        private void FlushPathRecorder(PathRecorder r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(ConvertToEastern(r.FillTime).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.Append(',').Append(r.Session);
            sb.Append(',').Append(r.Direction.ToString(CultureInfo.InvariantCulture));
            sb.Append(',').Append(N(r.FillPrice));
            for (int k = 0; k < PathLogLevels; k++)
                sb.Append(',').Append(double.IsNaN(r.FavTouch[k]) ? string.Empty : r.FavTouch[k].ToString("0.###", CultureInfo.InvariantCulture));
            for (int k = 0; k < PathLogLevels; k++)
                sb.Append(',').Append(double.IsNaN(r.AdvTouch[k]) ? string.Empty : r.AdvTouch[k].ToString("0.###", CultureInfo.InvariantCulture));
            WritePathRow(sb.ToString());
        }

        private string ResolvePathLogPath()
        {
            if (!string.IsNullOrEmpty(PathLogPath))
                return PathLogPath;

            // Unlike ResolveFeatureLogPath, this method runs on every WritePathRow call, not
            // just once - so the timestamp must be cached at first resolution, not
            // recomputed per row (which would fragment the log across a new file per row).
            if (resolvedPathLogPath == null)
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                resolvedPathLogPath = Path.Combine(dir, string.Format("EMAL_v{0}_research_log_{1}.csv",
                    ResolveVersionNumberString(),
                    DateTime.Now.ToString("yyyy-MM-dd hh-mm tt", CultureInfo.InvariantCulture)));
            }
            return resolvedPathLogPath;
        }

        private void WritePathRow(string row)
        {
            if (pathLogFailureCount > 5)
                return;
            try
            {
                string path = ResolvePathLogPath();
                if (!pathLogHeaderWritten)
                {
                    var h = new System.Text.StringBuilder("FillTimeET,Session,Direction,FillPrice");
                    for (int k = 0; k < PathLogLevels; k++)
                        h.Append(",fav").Append(((k + 1) * PathLogStepPoints).ToString("0.#", CultureInfo.InvariantCulture));
                    for (int k = 0; k < PathLogLevels; k++)
                        h.Append(",adv").Append(((k + 1) * PathLogStepPoints).ToString("0.#", CultureInfo.InvariantCulture));
                    if (!File.Exists(path))
                        File.AppendAllText(path, h.ToString() + Environment.NewLine);
                    pathLogHeaderWritten = true;
                }
                File.AppendAllText(path, row + Environment.NewLine);
            }
            catch (Exception ex)
            {
                pathLogFailureCount++;
                Print("EMAL path log write failed: " + ex.Message);
            }
        }

        // Runs on every Last tick while a filled position is open. MAE/MFE are measured from
        // the actual fill price, so they answer "how far did price move before this resolved" -
        // which is the input to sizing a Limit Offset.
        private void TrackExcursion()
        {
            if (!EnableFeatureLog
                || pendingFillFeatures == null
                || pendingDirection == 0
                || pendingFillPrice <= 0.0)
            {
                return;
            }

            double excursion = (lastTickPrice - pendingFillPrice) * pendingDirection;

            if (-excursion > tradeMaePoints)
                tradeMaePoints = -excursion;

            if (excursion > tradeMfePoints)
                tradeMfePoints = excursion;
        }

        // ---------------- chart info panel ----------------

        // Mirrors IsEntryWindowOpen's decision so the panel reports the same verdict the
        // strategy acts on, rather than a second implementation that could drift.
        private string GetTradeGateState()
        {
            if (configurationBlocked)
                return "disabled";

            DateTime raw = GetBarOpenRaw();

            if (IsHardBlockedMinute(ConvertToEastern(raw)))
                return "9:30 block";

            // Window gate is unconditional now, see IsEntryWindowOpen (Steve, 2026-08-06).
            int s = GetSessionIndex(raw);
            if (s < 0 || !IsSessionEnabled(s))
                return "session gate";

            if (maxDailyProfitLimitReached) return "daily profit cap";
            if (maxAccountBalanceLimitReached) return "balance cap";

            int projectedActions;
            int actionLimit;
            DateTime providerBlockedUntilUtc;
            if (TryGetOrderRateStatus(out projectedActions, out actionLimit, out providerBlockedUntilUtc))
            {
                if (providerBlockedUntilUtc > DateTime.UtcNow)
                    return "API cooldown";
                if (projectedActions + NewTradeActionReserve > actionLimit)
                    return "API guard";
            }

            return "allow";
        }

        // A single top-of-panel status line, shown only when something needs the user's
        // attention: a hard error that stops the strategy, or warmup in progress. Returns
        // null when everything is fine (the line is then omitted). Placed above "Instrument:"
        // so the panel always tells the user WHY it is or isn't trading (Steve, 2026-07-30).
        private string GetStatusLine()
        {
            if (configurationBlocked)
                return "ERROR: " + configurationBlockReason;

            if (CurrentBar < Math.Max(EmaPeriod, 20) + 2)
                return "Warmup in progress";

            // Limit entries need ticks; if none have ever arrived in real time the strategy
            // silently never fills. Surfaces the "enable Tick Replay" cause.
            if (State == State.Realtime && !sawMarketData)
                return "ERROR: no ticks (enable Tick Replay)";

            if (State == State.Realtime
                && Position.MarketPosition != MarketPosition.Flat
                && !IsOrderActive(protectiveStopOrder)
                && !terminalExitPending)
            {
                return "CRITICAL: position has no confirmed stop";
            }

            int projectedActions;
            int actionLimit;
            DateTime providerBlockedUntilUtc;
            if (TryGetOrderRateStatus(out projectedActions, out actionLimit, out providerBlockedUntilUtc)
                && providerBlockedUntilUtc > DateTime.UtcNow)
            {
                return string.Format("API cooldown to {0:HH:mm} UTC", providerBlockedUntilUtc);
            }

            return null;
        }

        // statusLineIndex (Steve, 2026-08-07): index of the status/error row within the
        // returned list, or -1 if none this bar - so the renderer can color just that row
        // red without guessing from position or an empty Value (header/footer also have an
        // empty Value, so that alone isn't a safe way to identify this row).
        // slopeLineIndex/slopeValid (Steve, 2026-08-07): the Slope: row's validity glyph is
        // rendered as a separate colored Run in RenderInfoBoxOverlay (see InfoSlopeValidBrush/
        // InfoSlopeInvalidBrush) rather than baked into the Value string, since it needs its
        // own color independent of the normal value text. slopeValid is null when there's no
        // glyph to show yet (current slope still "n/a").
        // blockedValueLineIndices (Steve, 2026-08-07): row indices whose Value text (only the
        // text after the label/colon - never the label itself) should render in the same red
        // used for the status row, because that row's condition is currently blocking trading:
        // Trade: (any disabled reason), Trade Minute: (current minute disabled), Max Account
        // Balance: (cap reached), API Guard: (rate-limited or in cooldown), Session: (outside
        // both trading windows, i.e. "Halt").
        private List<KeyValuePair<string, string>> BuildInfoLines(out int statusLineIndex, out int slopeLineIndex, out bool? slopeValid, out List<int> blockedValueLineIndices)
        {
            statusLineIndex = -1;
            slopeLineIndex = -1;
            slopeValid = null;
            blockedValueLineIndices = new List<int>();
            DateTime raw = GetBarOpenRaw();
            int session = GetSessionIndex(raw);
            string instrument = Instrument != null && Instrument.MasterInstrument != null
                ? Instrument.FullName
                : "-";

            var lines = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(string.Format("EMAL v{0}", GetAddOnVersion()), string.Empty)
            };

            string status = GetStatusLine();
            if (!string.IsNullOrEmpty(status))
            {
                statusLineIndex = lines.Count;
                lines.Add(new KeyValuePair<string, string>(status, string.Empty));
            }

            lines.Add(new KeyValuePair<string, string>("Instrument:", instrument));
            lines.Add(new KeyValuePair<string, string>("Contracts:", Contracts.ToString(CultureInfo.InvariantCulture)));
            // Current slope shown alongside the required threshold so it's easy to monitor
            // how close the live EMA slope is to triggering a signal (Steve, 2026-08-06).
            // Same completed-bar calc OnBarUpdate uses for the actual entry decision
            // (ema[1] - ema[2]), signed - unlike the threshold, which is always positive.
            double currentSlope = CurrentBar >= 2 ? ema[1] - ema[2] : double.NaN;
            double requiredSlopePanel = GetRequiredSlope(raw);
            string currentSlopeText = double.IsNaN(currentSlope)
                ? "n/a"
                : currentSlope.ToString("0.##", CultureInfo.InvariantCulture);
            // Validity glyph (Steve, 2026-08-07): compares slope MAGNITUDE only, matching the
            // slope half of OnBarUpdate's signal test (completedEmaSlope >= requiredSlope for a
            // long, <= -requiredSlope for a short - both reduce to abs(slope) >= threshold).
            // Direction (price vs EMA) is a separate condition this indicator doesn't cover -
            // it answers "is the slope steep enough right now", not "would a trade fire".
            // No glyph while current slope is n/a (not enough bars yet) - neither state fits.
            if (!double.IsNaN(currentSlope))
                slopeValid = Math.Abs(currentSlope) >= requiredSlopePanel;
            lines.Add(new KeyValuePair<string, string>("EMA Period:", EmaPeriod.ToString(CultureInfo.InvariantCulture)));
            slopeLineIndex = lines.Count;
            lines.Add(new KeyValuePair<string, string>("Slope:",
                string.Format("{0} ({1})", requiredSlopePanel.ToString("0.##", CultureInfo.InvariantCulture), currentSlopeText)));
            double panelTakeProfit = GetConfiguredTakeProfit();
            double panelStopLoss = GetConfiguredStopLoss();
            lines.Add(new KeyValuePair<string, string>("TP:", double.IsNaN(panelTakeProfit) ? "n/a" : panelTakeProfit.ToString("0.##", CultureInfo.InvariantCulture)));
            lines.Add(new KeyValuePair<string, string>("SL:", double.IsNaN(panelStopLoss) ? "n/a" : panelStopLoss.ToString("0.##", CultureInfo.InvariantCulture)));
            string tradeGateState = GetTradeGateState();
            if (tradeGateState != "allow")
                blockedValueLineIndices.Add(lines.Count);
            lines.Add(new KeyValuePair<string, string>("Trade:", tradeGateState));

            if (maxAccountBalanceLimitReached)
                blockedValueLineIndices.Add(lines.Count);
            lines.Add(new KeyValuePair<string, string>("Max Account Balance:",
                MaxAccountBalance > 0.0 ? "$" + MaxAccountBalance.ToString("0.##", CultureInfo.InvariantCulture) : "Off"));

            if (maxDailyProfitLimitReached)
                blockedValueLineIndices.Add(lines.Count);
            lines.Add(new KeyValuePair<string, string>("Max Daily Profit:",
                MaxDailyProfit > 0.0 ? "$" + MaxDailyProfit.ToString("0.##", CultureInfo.InvariantCulture) : "Off"));

            int projectedActions;
            int actionLimit;
            DateTime providerBlockedUntilUtc;
            if (TryGetOrderRateStatus(out projectedActions, out actionLimit, out providerBlockedUntilUtc))
            {
                if (providerBlockedUntilUtc > DateTime.UtcNow || projectedActions + NewTradeActionReserve > actionLimit)
                    blockedValueLineIndices.Add(lines.Count);
                lines.Add(new KeyValuePair<string, string>("API Guard:", string.Format("{0}/{1}", projectedActions, actionLimit)));
            }
            else
            {
                lines.Add(new KeyValuePair<string, string>("API Guard:", "Off"));
            }

            string sessionName = SessionName(session);
            if (sessionName == "Halt")
                blockedValueLineIndices.Add(lines.Count);
            string sessionZoneAbbrev = GetEasternZoneAbbreviation(ConvertToEastern(raw));
            lines.Add(new KeyValuePair<string, string>("Session:", string.Format("{0} {1}", sessionName, sessionZoneAbbrev)));
            lines.Add(new KeyValuePair<string, string>(InfoFooter, string.Empty));
            return lines;
        }

        private void UpdateInfoText()
        {
            if (ChartControl == null || ChartControl.Dispatcher == null)
                return;

            if (State != State.Realtime && State != State.Historical)
                return;

            int statusLineIndex;
            int slopeLineIndex;
            bool? slopeValid;
            List<int> blockedValueLineIndices;
            var lines = BuildInfoLines(out statusLineIndex, out slopeLineIndex, out slopeValid, out blockedValueLineIndices);
            ChartControl.Dispatcher.InvokeAsync(() => RenderInfoBoxOverlay(lines, statusLineIndex, slopeLineIndex, slopeValid, blockedValueLineIndices));
        }

        private void RenderInfoBoxOverlay(List<KeyValuePair<string, string>> lines, int statusLineIndex, int slopeLineIndex, bool? slopeValid, List<int> blockedValueLineIndices)
        {
            if (!EnsureInfoBoxOverlay() || infoBoxRowsPanel == null)
                return;

            infoBoxRowsPanel.Children.Clear();

            for (int i = 0; i < lines.Count; i++)
            {
                bool edge = i == 0 || i == lines.Count - 1;
                bool isStatusRow = i == statusLineIndex;
                bool isEmojiRow = i == slopeLineIndex && slopeValid.HasValue;

                var text = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = edge ? 15 : 14,
                    FontWeight = edge || isStatusRow ? FontWeights.SemiBold : FontWeights.Normal,
                    TextAlignment = edge ? TextAlignment.Center : TextAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                // Every row stays on Display (crisp small text, matches every other row) - see
                // the emoji Run below for the actual color-glyph fix, corrected 2026-08-09
                // against DUO-21.cs's BuildInfoValueRun, which renders ✅/⛔️ correctly in
                // production. The previous fix here (Steve, 2026-08-07) switched this whole
                // row to TextFormattingMode.Ideal on the theory that Display's legacy
                // GDI-compatible glyph path can't render color-layer (COLR/CPAL) emoji - that
                // was a plausible but never-confirmed diagnosis, and it had the side effect of
                // rendering the Slope: row's label/value text through a different formatting
                // path than every other row. DUO proves the actual fix doesn't touch
                // TextFormattingMode at all.
                TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
                // BUG FIX (Steve, 2026-08-10): the 2026-08-09 fix set TextRenderingMode.Grayscale
                // on the emoji Run below but never on this TextBlock, so the row still rendered
                // through WPF's default ClearType path - which is what actually blocks
                // multi-layer COLR/CPAL color glyphs, producing the monochrome fallback dingbat
                // seen live in NT8 (confirmed via screenshot, v1.0.3.3). DUO-21.cs's actual working
                // code sets TextRenderingMode on the TextBlock too (BuildInfoRows, not just
                // BuildInfoValueRun) - Grayscale only for the row carrying the emoji, ClearType
                // (the existing default, made explicit) for every other row so nothing else
                // changes. This was the missing half of the port; TextFormattingMode is still
                // untouched, matching the comment above.
                TextOptions.SetTextRenderingMode(text, isEmojiRow ? TextRenderingMode.Grayscale : TextRenderingMode.ClearType);

                text.Inlines.Add(new Run(lines[i].Key)
                {
                    Foreground = isStatusRow ? InfoStatusTextBrush : (edge ? InfoHeaderTextBrush : InfoLabelBrush)
                });

                if (!string.IsNullOrEmpty(lines[i].Value))
                {
                    text.Inlines.Add(new Run(" ") { Foreground = InfoLabelBrush });
                    text.Inlines.Add(new Run(lines[i].Value)
                    {
                        Foreground = blockedValueLineIndices.Contains(i) ? InfoStatusTextBrush : InfoValueBrush
                    });
                }

                if (i == slopeLineIndex && slopeValid.HasValue)
                {
                    text.Inlines.Add(new Run(" ") { Foreground = InfoLabelBrush });
                    // Segoe UI Emoji font family + TextRenderingMode.Grayscale on this Run is
                    // the actual working fix (2026-08-09, matched against DUO-21.cs's
                    // BuildInfoValueRun/InfoEmojiTokens path, confirmed correct in production
                    // there) - not TextFormattingMode.Ideal, which the previous cut used on
                    // the whole row instead. ClearType (the default rendering mode) is what
                    // was producing the fallback dingbat; Grayscale is what DUO uses for every
                    // token it classifies as emoji.
                    var slopeGlyphRun = new Run(slopeValid.Value ? "✅" : "⛔️")
                    {
                        FontFamily = new FontFamily("Segoe UI Emoji")
                    };
                    TextOptions.SetTextRenderingMode(slopeGlyphRun, TextRenderingMode.Grayscale);
                    text.Inlines.Add(slopeGlyphRun);
                }

                infoBoxRowsPanel.Children.Add(new Border
                {
                    Background = edge
                        ? InfoHeaderFooterGradientBrush
                        : (i % 2 == 0 ? InfoBodyEvenBrush : InfoBodyOddBrush),
                    Padding = new Thickness(6, 2, 6, 2),
                    Child = text
                });
            }
        }

        private bool EnsureInfoBoxOverlay()
        {
            if (ChartControl == null)
                return false;

            if (infoBoxContainer != null && infoBoxRowsPanel != null)
                return true;

            var host = ChartControl.Parent as System.Windows.Controls.Panel;

            if (host == null)
                return false;

            infoBoxRowsPanel = new StackPanel { Orientation = Orientation.Vertical };

            infoBoxContainer = new Border
            {
                Child = infoBoxRowsPanel,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(5, 8, 8, 37),
                Background = Brushes.Transparent
            };

            host.Children.Add(infoBoxContainer);
            System.Windows.Controls.Panel.SetZIndex(infoBoxContainer, int.MaxValue);
            return true;
        }

        private void DisposeInfoBoxOverlay()
        {
            try
            {
                if (ChartControl == null || ChartControl.Dispatcher == null)
                {
                    infoBoxRowsPanel = null;
                    infoBoxContainer = null;
                    return;
                }

                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    if (infoBoxContainer != null)
                    {
                        var parent = infoBoxContainer.Parent as System.Windows.Controls.Panel;

                        if (parent != null)
                            parent.Children.Remove(infoBoxContainer);
                    }

                    infoBoxRowsPanel = null;
                    infoBoxContainer = null;
                });
            }
            catch
            {
                infoBoxRowsPanel = null;
                infoBoxContainer = null;
            }
        }

        private string GetAddOnVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Version version = assembly.GetName().Version;
            return version != null ? version.ToString() : "0.0.0.0";
        }

        // Hard block on the 09:30 ET minute (Steve, 2026-08-08): unconditional, independent of
        // session enable/disable or any other setting. TESTED
        // AND FAILED on drawdown grounds, kept on discretion (Analysis_Plan.md §12.16): as a
        // standalone pre-registered hypothesis (waiving the 57-minute multiplicity correction),
        // 09:30's bad win rate is real (n=29, WR 72.41%, PF 0.721, net -$814.90, permutation
        // p=0.011) - but blocking it makes the worst drawdown WORSE in 2 of 4 four-halves folds,
        // a composition-matched random deletion beats its drawdown improvement ~30% of the time,
        // the bad stretch was May-June only (Jul/Aug profitable, a held-out fortnight went 6-for-6),
        // and the neighboring minutes (09:29, 09:31-09:33) are all strong - contradicting the
        // cash-open-volatility mechanism's own prediction of a dangerous neighborhood, not one
        // isolated bar. Kept anyway on Steve's discretion: cheap (~1% of the book, ~$11/session,
        // deletes losing not winning trades), NOT because it is a validated drawdown reduction.
        // Only ever fires within the US 09:28-09:50 window (09:30 doesn't occur in the 09:55-10:30
        // window or in any other session), but checked unconditionally rather than gated behind
        // the session index, matching Steve's "no matter what the settings are" instruction.
        private bool IsHardBlockedMinute(DateTime easternTime)
        {
            return easternTime.Hour == 9 && easternTime.Minute == 30;
        }

        // EMAL-1050 (Steve, 2026-08-21; extended same day to add 09:43): optional additional
        // block on 09:31-09:35 inclusive AND 09:43, both gated on the single Block0931To0935
        // toggle - OFF by default. Independent of, and evaluated after, the always-on 09:30
        // hard block above - the two are separate mechanisms and this one is user-toggleable
        // where the 09:30 one is not. Does NOT touch 09:28/09:29, 09:36-09:42, or 09:44 onward -
        // those minutes are unaffected whether this toggle is on or off, per spec. With this
        // enabled, 09:36 becomes the first minute a new entry can fire following the 09:28
        // window's open (09:30 always, 09:31-09:35 via this toggle, both blocked in between),
        // and 09:43 is blocked as a separate standalone minute later in the same window. Note
        // for context (Analysis_Plan §12.16): the 09:30 hard-block research found 09:29 and
        // 09:31-09:33 specifically strong/profitable in that same study, in NT8 Playback - this
        // toggle responds to a live-vs-Playback divergence Steve has observed (see project
        // memory `live-open-erratic-minutes.md`), not a contradiction of that finding.
        private bool IsAdditionalBlockedMinute(DateTime easternTime)
        {
            if (!Block0931To0935 || easternTime.Hour != 9)
                return false;

            int minute = easternTime.Minute;
            return (minute >= 31 && minute <= 35) || minute == 43;
        }

        private bool IsEntryWindowOpen()
        {
            DateTime barOpenRaw = GetBarOpenRaw();
            DateTime barOpen = ConvertToEastern(barOpenRaw);

            // Checked first, unconditionally - see IsHardBlockedMinute's comment.
            if (IsHardBlockedMinute(barOpen))
            {
                hardBlockedMinuteBarCount++;
                return false;
            }

            // EMAL-1050: checked next, before the session/window gate below - same "no matter
            // what the settings are" placement as the 09:30 block, just gated on its own toggle
            // instead of being unconditional.
            if (IsAdditionalBlockedMinute(barOpen))
            {
                additionalBlockedMinuteBarCount++;
                return false;
            }

            // Window gate is unconditional (Steve, 2026-08-06): the two US windows are the only
            // sessions that exist, so entries are confined to them.
            int session = GetSessionIndex(barOpenRaw);
            if (session < 0 || !IsSessionEnabled(session))
                return false;

            return true;
        }



        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            bool firstTickOfBar = IsFirstTickOfBar;

            // Keep the hosted EMA synchronized before any strategy gate can return. Update() is
            // a no-op when the EMA is already current, so this is cheap.
            //
            // REVERTED to unconditional (EMAL-1040, 2026-08-14): EMAL-1038 gated this to only
            // State.Historical or firstTickOfBar, to skip supposedly-redundant work on
            // mid-bar live ticks. That reopened the exact out-of-range bug this comment used to
            // warn about: NT8 Playback (QSJAW Part B) threw "Indicator 'EMA': ... accessing a
            // series [barsAgo] with a value of 5 when there are only 4 bars on the chart" - the
            // hosted EMA's internal bar tracking fell behind the primary series between
            // first-ticks (most likely across a data gap/multi-bar catch-up) and the gap was
            // never observed until Update() ran again. The thing the gating optimized away was
            // already a no-op most of the time per the original comment above, so there was no
            // real performance problem to trade this correctness risk against. Does not affect
            // any decision EMAL ever made: signal logic only reads completed EMA values on the
            // first tick of a new bar, and Update() ran at that exact moment either way - see
            // EMAL-VERSION-STATUS.md's EMAL-1038.cs entry for the full analysis of why prior
            // Playback results (QSJAW Part A) are not affected by this bug.
            ema.Update();

            // Historical bars warm Playback/live instances only. Strategy Analyzer retains
            // the complete historical order/fill path through the Backtest account.
            if (State == State.Historical && !IsHistoricalTradeSimulationContext())
                return;

            // Wrong chart period or instrument: stay loaded, submit nothing - but still draw
            // the panel so the top status line shows the user WHY it is disabled.
            if (configurationBlocked)
            {
                if (firstTickOfBar)
                    UpdateInfoText();
                return;
            }

            // Evaluate on every tick so unrealized profit can flatten an open position
            // immediately instead of waiting for the next one-minute bar. When both guards
            // are disabled (their default), bypass the guard methods entirely.
            bool accountRiskEnabled = MaxAccountBalance > 0.0 || MaxDailyProfit > 0.0;
            if (accountRiskEnabled && (IsAccountBalanceBlocked() || IsAccountDailyProfitBlocked()))
            {
                // Keep drawing the info panel after the account-level latch is hit.
                if (firstTickOfBar)
                    UpdateInfoText();
                return;
            }

            if (!firstTickOfBar)
                return;

            ClearQueuedEntry();

            UpdateInfoText();

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                CancelEntryOrderIfActive("position-open");
                return;
            }

            // 20 covers the AvgVolume20 lookback used by the feature log.
            if (CurrentBar < Math.Max(EmaPeriod, 20) + 2)
            {
                CancelEntryOrderIfActive("warmup");
                return;
            }

            // Entry-time gate only. Open positions are untouched: their stop and target were
            // registered at entry and continue to manage the exit outside the window.
            if (!IsEntryWindowOpen())
            {
                blockedBarCount++;
                CancelEntryOrderIfActive("window-closed");
                return;
            }

            double currentPrice = Close[0];
            double completedEma = ema[1];
            double completedEmaSlope = ema[1] - ema[2];
            double requiredSlope = GetRequiredSlope(GetBarOpenRaw());

            bool longSignal = currentPrice > completedEma
                && completedEmaSlope > 0.0
                && completedEmaSlope >= requiredSlope;
            bool shortSignal = currentPrice < completedEma
                && completedEmaSlope < 0.0
                && completedEmaSlope <= -requiredSlope;

            int signalDirection = longSignal ? 1 : (shortSignal ? -1 : 0);

            if (signalDirection != 0)
                QueueEntry(signalDirection, completedEmaSlope);

            if (IsOrderActive(entryOrder))
            {
                CancelEntryOrderIfActive("signal-replace");
                return;
            }

            TrySubmitQueuedEntry();
        }

        private void QueueEntry(int direction, double completedEmaSlope)
        {
            queuedDirection = direction;
            queuedEntryBar = CurrentBar;
            queuedSignalTimestamp = IsExecutionDiagnosticsActive()
                ? Stopwatch.GetTimestamp()
                : 0L;
            queuedSignalUtc = queuedSignalTimestamp > 0L ? DateTime.UtcNow : DateTime.MinValue;
            signalCount++;

            // All bracket scaling (TP Atr/Slope 3.6, SL Atr 3.7) was tested and REJECTED:
            // varying the bracket per-trade destroys edge. Fixed distances only. The snapshot
            // plumbing is retained so an open trade keeps the values it entered with.
            // Per-window brackets: the two special US sessions use their preset TP/SL; all
            // other sessions use the global values. Snapshotted here so the trade keeps them.
            queuedTakeProfitPoints = GetConfiguredTakeProfit();
            queuedStopLossPoints = GetConfiguredStopLoss();

            // Where price was when the signal fired, not where the limit was placed.
            queuedSignalPrice = Close[0];
            queuedLimitPrice = GetPassiveLimitPrice(direction);
        }

        private void TrySubmitQueuedEntry()
        {
            // OnOrderUpdate can re-enter this method after an asynchronous cancellation.
            // Recheck the account latch here so no queued entry can escape the main gate.
            if (configurationBlocked
                || ((MaxAccountBalance > 0.0 || MaxDailyProfit > 0.0)
                    && (IsAccountBalanceBlocked() || IsAccountDailyProfitBlocked())))
            {
                ClearQueuedEntry();
                return;
            }

            if (queuedDirection == 0
                || queuedEntryBar != CurrentBar
                || Position.MarketPosition != MarketPosition.Flat
                || IsOrderActive(entryOrder))
            {
                return;
            }

            string rateBlockReason;
            if (!TryReserveNewTradeActions(out rateBlockReason))
            {
                rateGuardBlockedEntryCount++;
                ClearQueuedEntry();
                Print(string.Format(
                    "{0} | ORDER RATE GUARD | entry blocked | {1}",
                    lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                    rateBlockReason));
                return;
            }

            int direction = queuedDirection;
            double limitPrice = queuedLimitPrice;
            double takeProfit = queuedTakeProfitPoints;
            double stopLoss = queuedStopLossPoints > 0.0 ? queuedStopLossPoints : DefaultSafetyStopLossPoints;
            double signalPrice = queuedSignalPrice;
            long signalTimestamp = queuedSignalTimestamp;
            DateTime signalUtc = queuedSignalUtc;
            ClearQueuedEntry();

            entryCancelPending = false;

            string entrySignal = direction > 0 ? LongEntrySignal : ShortEntrySignal;

            BeginProtectionTracking(entrySignal, direction, limitPrice, takeProfit, stopLoss);

            CaptureEntryFeatures(direction, signalPrice, takeProfit, stopLoss);

            RecordNtOrderAction("entry");

            bool diagnosticsActive = IsExecutionDiagnosticsActive();
            entryLatencySignalTimestamp = diagnosticsActive ? signalTimestamp : 0L;
            entryLatencySubmitStartTimestamp = diagnosticsActive ? Stopwatch.GetTimestamp() : 0L;
            entryLatencySignalUtc = diagnosticsActive ? signalUtc : DateTime.MinValue;
            entryLatencySubmitStartUtc = diagnosticsActive ? DateTime.UtcNow : DateTime.MinValue;
            entryLatencyOrderStateLogged = false;
            entryLatencyExecutionLogged = false;

            if (direction > 0)
                EnterLongLimit(0, true, Contracts, limitPrice, LongEntrySignal);
            else
                EnterShortLimit(0, true, Contracts, limitPrice, ShortEntrySignal);

            // EMAL-1041: moved from before EnterLongLimit/EnterShortLimit, same reasoning as the
            // diagnostics block below it (EMAL-1038) - measured in live logs, the old ordering
            // delayed real order submission a median 0.49-0.50s on ProjectX-configured instances.
            // The enqueue itself is now non-blocking either way (see SendPlannedProjectXEntry),
            // but keeping it strictly after the NT submission call matches the same rationale and
            // keeps every non-trading side effect grouped together, post-submission.
            SendPlannedProjectXEntry(direction, limitPrice, takeProfit, stopLoss);

            // Formatting/Print used to run before EnterLongLimit/EnterShortLimit. With many
            // same-instrument instances that serialized diagnostic work ahead of later
            // accounts. Keep it optional and strictly after the NT submission call.
            if (diagnosticsActive)
            {
                long submitReturnedTimestamp = Stopwatch.GetTimestamp();
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | EMAL EXECUTION | account={1} signal={2} limit={3:F2} tp={4:F2} sl={5:F2} "
                    + "signalUtc={6:O} submitStartUtc={7:O} signalToSubmitStartMs={8:F3} submitCallMs={9:F3}",
                    Time[0],
                    Account != null ? Account.Name : "-",
                    entrySignal,
                    limitPrice,
                    takeProfit,
                    stopLoss,
                    entryLatencySignalUtc,
                    entryLatencySubmitStartUtc,
                    ElapsedMilliseconds(entryLatencySignalTimestamp, entryLatencySubmitStartTimestamp),
                    ElapsedMilliseconds(entryLatencySubmitStartTimestamp, submitReturnedTimestamp)));
            }
        }

        // Passive: bid for longs, ask for shorts. Entry Order Type is fixed to Limit(BidAsk)
        // (2026-08-06) - Market entries and the Open/Close limit references were removed.
        private double GetPassiveLimitPrice(int direction)
        {
            double price = direction > 0 ? GetCurrentBid() : GetCurrentAsk();

            if (price <= 0.0 || double.IsNaN(price))
                price = Close[0];

            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        // EMAL-1044: reason is diagnostic-only (feeds entryCancelReason / LogEntryOrderTransition
        // below), defaults to "unspecified" for any call site not updated to pass one. No live
        // decision reads this string; CancelOrder(entryOrder) below is unchanged from EMAL-1043.
        private void CancelEntryOrderIfActive(string reason = "unspecified")
        {
            if (!IsOrderActive(entryOrder)
                || entryCancelPending
                || IsHistoricalOrderAwaitingRealtimeTransition(entryOrder))
            {
                return;
            }

            cancelBarEndCount++;
            entryCancelPending = true;
            entryCancelReason = reason ?? "unspecified";
            if (IsExecutionDiagnosticsActive())
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | EMAL ENTRY TRACE | cancel requested | reason={1} orderName={2} limit={3:F2} bid={4:F2} ask={5:F2}",
                    lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                    entryCancelReason,
                    entryOrder != null ? entryOrder.Name : "-",
                    entryOrder != null ? entryOrder.LimitPrice : 0.0,
                    GetCurrentBid(),
                    GetCurrentAsk()));
            }
            RecordNtOrderAction("cancel-entry-bar-end");
            CancelOrder(entryOrder);
        }

        private void ClearActiveEntryContext()
        {
            entryCancelPending = false;
        }

        private void BeginProtectionTracking(string entrySignal, int direction, double limitPrice,
            double takeProfitPoints, double stopLossPoints)
        {
            protectedEntrySignal = entrySignal ?? string.Empty;
            activeTakeProfitPoints = Math.Max(TickSize, takeProfitPoints);
            activeStopLossPoints = Math.Max(TickSize, stopLossPoints);
            entryFillValue = 0.0;
            entryFilledQuantity = 0;
            desiredProtectionTargetPrice = 0.0;
            desiredProtectionQuantity = 0;
            protectiveStopOrder = null;
            profitTargetOrder = null;
            terminalExitPending = false;
            stopReconcileAttempts = 0;
            targetReconcileAttempts = 0;
            ArmGapLatch(direction, limitPrice, activeTakeProfitPoints, activeStopLossPoints);
        }

        // Armed once, from the same signal-tick data every instance on the feed shares -
        // before the order exists, so it cannot depend on that account's own submission or
        // fill-confirmation timing. See the field comments above and EMAL-1037-changelog.txt.
        private void ArmGapLatch(int direction, double limitPrice, double takeProfitPoints, double stopLossPoints)
        {
            if (direction == 0 || limitPrice <= 0.0)
            {
                gapLatchArmed = false;
                // No planned limit price for this trade (e.g. a future market-entry code path) -
                // the target touch watchdog falls back to its old fill-anchored behavior. Log
                // once per instance lifetime, not once per trade, so this doesn't spam once it's
                // fired for an instance that's always in this mode.
                plannedTargetTouchLevel = 0.0;
                if (!plannedTargetTouchLevelFallbackLogged)
                {
                    plannedTargetTouchLevelFallbackLogged = true;
                    Print("EMAL TARGET TOUCH WATCHDOG: no planned limit price at arming time - "
                        + "falling back to fill-anchored touch level for this and future trades on this instance.");
                }
                return;
            }

            double targetPrice = direction > 0
                ? limitPrice + takeProfitPoints
                : limitPrice - takeProfitPoints;
            double stopPrice = direction > 0
                ? limitPrice - stopLossPoints
                : limitPrice + stopLossPoints;

            gapLatchDirection = direction;
            gapLatchTargetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);
            gapLatchStopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            gapTargetBreached = false;
            gapStopBreached = false;
            gapLatchArmed = true;

            // Target touch watchdog's shared planned level - same signal-tick limit/TakeProfit
            // data as gapLatchTargetPrice above, latched here (not recomputed later) so every
            // instance on the feed arms from the identical number. See the field comment.
            plannedTargetTouchLevel = gapLatchTargetPrice;
        }

        // Runs on every Last tick from the moment the latch is armed. try/catch is required:
        // RealtimeErrorHandling.IgnoreAllErrors (see DataLoaded) means an unhandled exception
        // here would fail silently and the strategy would keep trading with a stale latch.
        //
        // Aug 13-14 live-trade review (Codex): a resting entry limit was filling on a
        // retracement AFTER price had already run through its planned TAKE PROFIT, then
        // immediately market-exiting at a loss. Fix: the moment the target level is crossed,
        // proactively cancel the still-working entry order here (reusing the existing
        // bar-boundary cancel helper, now also called from tick level) instead of letting it
        // fill and relying on the post-fill gap latch to flatten.
        // Stop-side deliberately NOT wired to cancel (Steve, 2026-08-14): a passive resting
        // entry sits at the top of the book on the stop side, so price reaching the stop level
        // before the entry fills isn't a real scenario on liquid ES/NQ outside a sub-second
        // not-yet-acknowledged race - unlike the target side, where the order simply never
        // interacts with a favorable move and this is the normal, common case. gapTargetBreached/
        // gapStopBreached both still feed SubmitOrUpdateProtection's post-fill decision
        // unchanged - that path remains the emergency backstop for the rare race either way.
        // EMAL-1045: source is Last, Bid, or Ask - Bid/Ask only arrive here when
        // TouchDetectionMode is QuoteOrLast (gated in HandleQuoteTick). tickTime is the tick's
        // own timestamp (not the possibly-stale lastTickTime global, which only advances on Last
        // ticks) so a quote-triggered diagnostic print carries an accurate time.
        private void EvaluateGapLatch(MarketDataType source, double price, DateTime tickTime)
        {
            if (!gapLatchArmed || price <= 0.0)
                return;

            // Quote-side filtering: a Long position's target is a sell limit resting ABOVE
            // entry (ExitLongLimit - confirmed against SubmitOrUpdateProfitTarget below), which
            // fills as the market trades UP into it, so Bid is the side that matters; a Short's
            // target is a buy limit resting BELOW entry (ExitShortLimit), which fills as the
            // market trades DOWN into it, so Ask is the side that matters. This mirrors the
            // existing Long-uses-Bid/Short-uses-Ask convention already used elsewhere in this
            // file (GetPassiveLimitPrice, GetProtectiveReferencePrice) and is applied uniformly
            // to the stop-side flag below too, for the same reference-price reason, not because
            // the stop order itself is quoted on that side. Last is always relevant regardless
            // of direction; the "wrong" quote side for this direction carries no new information
            // and is skipped rather than mismatched.
            if (source == MarketDataType.Bid && gapLatchDirection <= 0)
                return;
            if (source == MarketDataType.Ask && gapLatchDirection >= 0)
                return;

            try
            {
                if (gapLatchDirection > 0)
                {
                    // Stop-side breach-before-fill is not a real scenario for a passive resting
                    // entry: the order sits at the top of the book on that side, so price cannot
                    // walk past it and keep going without filling it first (on liquid ES/NQ,
                    // outside a sub-second not-yet-acknowledged race the existing post-fill gap
                    // latch already covers). Flag still tracked below for that post-fill path -
                    // only the pre-fill cancel trigger was removed, per Steve, 2026-08-14.
                    if (!gapStopBreached && price <= gapLatchStopPrice)
                    {
                        gapStopBreached = true;
                        if (IsExecutionDiagnosticsActive())
                        {
                            Print(string.Format(CultureInfo.InvariantCulture,
                                "{0} | EMAL GAP BREACH DETECTED | side=Long level=stop source={1} stop={2:F2} price={3:F2}",
                                tickTime, source, gapLatchStopPrice, price));
                        }
                    }
                    if (!gapTargetBreached && price >= gapLatchTargetPrice)
                    {
                        gapTargetBreached = true;
                        if (IsExecutionDiagnosticsActive())
                        {
                            Print(string.Format(CultureInfo.InvariantCulture,
                                "{0} | EMAL GAP BREACH DETECTED | side=Long level=target source={1} target={2:F2} price={3:F2}",
                                tickTime, source, gapLatchTargetPrice, price));
                        }
                        CancelEntryOrderIfActive("gap-breach-target");
                    }
                }
                else
                {
                    if (!gapStopBreached && price >= gapLatchStopPrice)
                    {
                        gapStopBreached = true;
                        if (IsExecutionDiagnosticsActive())
                        {
                            Print(string.Format(CultureInfo.InvariantCulture,
                                "{0} | EMAL GAP BREACH DETECTED | side=Short level=stop source={1} stop={2:F2} price={3:F2}",
                                tickTime, source, gapLatchStopPrice, price));
                        }
                    }
                    if (!gapTargetBreached && price <= gapLatchTargetPrice)
                    {
                        gapTargetBreached = true;
                        if (IsExecutionDiagnosticsActive())
                        {
                            Print(string.Format(CultureInfo.InvariantCulture,
                                "{0} | EMAL GAP BREACH DETECTED | side=Short level=target source={1} target={2:F2} price={3:F2}",
                                tickTime, source, gapLatchTargetPrice, price));
                        }
                        CancelEntryOrderIfActive("gap-breach-target");
                    }
                }
            }
            catch (Exception ex)
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | GAP LATCH ERROR | source={1} | {2}", tickTime, source, ex.Message));
            }
        }

        // Post-fill sibling of EvaluateGapLatch, same tick-driven pattern - no timers/threads,
        // just comparing the current tick against a latched timestamp. Reads
        // plannedTargetTouchLevel (the SHARED planned target level, latched at arming time from
        // the same signal-tick data every instance on the feed sees - see the field comment),
        // not desiredProtectionTargetPrice (this account's own fill-anchored working target
        // price) and not a recomputed level. EMAL-1042: this is the anchor change from
        // EMAL-1041 - see EMAL-1042-changelog.txt for why. Falls back to
        // desiredProtectionTargetPrice only when plannedTargetTouchLevel is unset (0.0), i.e.
        // ArmGapLatch had no planned limit price for this trade. Guarded from the caller
        // (OnMarketData) on EnableTargetTouchWatchdog, !targetTouchWatchdogFired, and
        // position-not-flat, so this only runs when it could possibly still do something.
        // EMAL-1045: source is Last, Bid, or Ask - Bid/Ask only arrive here when
        // TouchDetectionMode is QuoteOrLast (gated in HandleQuoteTick).
        private void EvaluateTargetTouchWatchdog(MarketDataType source, double price, DateTime tickTime)
        {
            if (price <= 0.0
                || terminalExitPending
                || !IsOrderActive(profitTargetOrder))
            {
                return;
            }

            // Scope guard: this strategy is 1-contract only by design. If that assumption is
            // ever violated, disable the watchdog inertly rather than build partial-fill
            // quantity reconciliation it was explicitly asked not to have. Logged once per
            // instance (see the field comment), not once per trade.
            if (Contracts != 1 || Math.Abs(Position.Quantity) > 1)
            {
                if (!targetTouchWatchdogScopeGuardLogged)
                {
                    targetTouchWatchdogScopeGuardLogged = true;
                    Print(string.Format(
                        "{0} | EMAL TARGET TOUCH WATCHDOG DISABLED | Contracts={1} Position.Quantity={2} - "
                        + "only supports the 1-contract case, feature is inert until this instance is reconfigured",
                        tickTime, Contracts, Position.Quantity));
                }
                return;
            }

            bool isLong = Position.MarketPosition == MarketPosition.Long;

            // Quote-side filtering - same Long-uses-Bid/Short-uses-Ask convention as
            // EvaluateGapLatch (see its comment for the order-object confirmation). Last is
            // always relevant; the "wrong" quote side for this position carries no new
            // information about whether the resting target has been reached and is skipped.
            if (source == MarketDataType.Bid && !isLong)
                return;
            if (source == MarketDataType.Ask && isLong)
                return;

            // plannedTargetTouchLevel is the shared touch level; desiredProtectionTargetPrice
            // (this account's own working target price) is retained here only as the fallback
            // source and for diagnostics - never as the primary comparison. See field comments.
            double ownWorkingTargetPrice = desiredProtectionTargetPrice;
            double touchLevel = plannedTargetTouchLevel > 0.0 ? plannedTargetTouchLevel : ownWorkingTargetPrice;
            if (touchLevel <= 0.0)
                return;

            bool touched = isLong ? price >= touchLevel : price <= touchLevel;

            if (targetTouchedUtc == DateTime.MinValue)
            {
                if (!touched)
                    return;
                targetTouchedUtc = tickTime;
                targetTouchSource = source;
                if (IsExecutionDiagnosticsActive())
                {
                    Print(string.Format(CultureInfo.InvariantCulture,
                        "{0} | EMAL TARGET TOUCH WATCHDOG | first touch | source={1} plannedTargetTouchLevel={2:F2} "
                        + "ownWorkingTargetPrice={3:F2} price={4:F2}",
                        tickTime, source, plannedTargetTouchLevel, ownWorkingTargetPrice, price));
                }
                return;
            }

            // No un-latching if price trades back away from the target: the touch already
            // happened, and the grace period is measured from the first touch, not from
            // continuous presence at the level.
            double elapsedMs = (tickTime - targetTouchedUtc).TotalMilliseconds;
            if (elapsedMs < TargetTouchGraceMs)
                return;

            // Grace elapsed and the target is still working (re-checked via IsOrderActive at the
            // top of this method on every call) - convert. One-shot: set both flags immediately,
            // before the cancel outcome is known, so no later tick can re-enter this method for
            // the same trade (guarded in OnMarketData via targetTouchWatchdogFired).
            targetTouchWatchdogFired = true;
            targetTouchWatchdogCancelPending = true;
            RecordNtOrderAction("touch-watchdog-cancel-target");
            Print(string.Format(
                "{0} | EMAL TARGET TOUCH WATCHDOG | price traded through target and limit unfilled after {1}ms "
                + "- cancelling target for market exit | touchSource={2} plannedTargetTouchLevel={3:F2} "
                + "ownWorkingTargetPrice={4:F2} price={5:F2}",
                tickTime, TargetTouchGraceMs, targetTouchSource, plannedTargetTouchLevel, ownWorkingTargetPrice, price));
            CancelOrder(profitTargetOrder);
        }

        // Called only after the target order's own cancel is CONFIRMED (a terminal OrderState
        // callback), never speculatively - a limit can fill between the cancel request and the
        // broker processing it, and firing both would flip the position. See the OnOrderUpdate
        // hook that calls this.
        private void SubmitTargetTouchMarketExit(string entrySignal)
        {
            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            MarketPosition positionDirection = Position.MarketPosition;
            string fromEntrySignal = string.IsNullOrEmpty(entrySignal) ? protectedEntrySignal : entrySignal;

            RecordNtOrderAction("touch-watchdog-exit");
            Print(string.Format(
                "{0} | EMAL TARGET TOUCH WATCHDOG | target cancel confirmed unfilled - exiting at market | side={1} entry={2}",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0], positionDirection, fromEntrySignal));

            if (positionDirection == MarketPosition.Long)
                ExitLong(TargetTouchExitSignal, fromEntrySignal);
            else
                ExitShort(TargetTouchExitSignal, fromEntrySignal);
        }

        private void TransitionTrackedOrderReferencesToRealtime()
        {
            if (State != State.Realtime)
                return;

            entryOrder = TransitionOrderReferenceToRealtime(entryOrder);
            protectiveStopOrder = TransitionOrderReferenceToRealtime(protectiveStopOrder);
            profitTargetOrder = TransitionOrderReferenceToRealtime(profitTargetOrder);
        }

        private Order TransitionOrderReferenceToRealtime(Order order)
        {
            if (order == null || !order.IsBacktestOrder)
                return order;

            // A null result means NinjaTrader did not create a live counterpart. Store it
            // exactly: retaining the old backtest object would make the entry look active
            // forever and block all real-time/replay signals.
            return GetRealtimeOrder(order);
        }

        private bool IsHistoricalOrderAwaitingRealtimeTransition(Order order)
        {
            return State == State.Realtime
                && order != null
                && order.IsBacktestOrder;
        }

        private static bool IsOrderActive(Order order)
        {
            return order != null
                && order.OrderState != OrderState.Cancelled
                && order.OrderState != OrderState.Filled
                && order.OrderState != OrderState.Rejected;
        }

        private void ClearQueuedEntry()
        {
            queuedDirection = 0;
            queuedLimitPrice = 0.0;
            queuedTakeProfitPoints = 0.0;
            queuedStopLossPoints = 0.0;
            queuedSignalPrice = 0.0;
            queuedEntryBar = -1;
            queuedSignalTimestamp = 0L;
            queuedSignalUtc = DateTime.MinValue;
        }

        private bool IsExecutionDiagnosticsActive()
        {
            return EnableExecutionDiagnostics && State == State.Realtime;
        }

        private static double ElapsedMilliseconds(long startTimestamp, long endTimestamp)
        {
            if (startTimestamp <= 0L || endTimestamp < startTimestamp)
                return 0.0;

            return (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        }

        private void LogFirstEntryOrderState(string orderName, OrderState orderState)
        {
            if (!IsExecutionDiagnosticsActive()
                || entryLatencyOrderStateLogged
                || entryLatencySubmitStartTimestamp <= 0L
                || (orderState != OrderState.Submitted
                    && orderState != OrderState.Accepted
                    && orderState != OrderState.Working))
            {
                return;
            }

            entryLatencyOrderStateLogged = true;
            long stateTimestamp = Stopwatch.GetTimestamp();
            Print(string.Format(CultureInfo.InvariantCulture,
                "{0} | EMAL EXECUTION | account={1} signal={2} firstOrderState={3} "
                + "stateUtc={4:O} signalToStateMs={5:F3} submitStartToStateMs={6:F3}",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                Account != null ? Account.Name : "-",
                orderName,
                orderState,
                DateTime.UtcNow,
                ElapsedMilliseconds(entryLatencySignalTimestamp, stateTimestamp),
                ElapsedMilliseconds(entryLatencySubmitStartTimestamp, stateTimestamp)));
        }

        // EMAL-1044: full entry-order lifecycle trace, every callback (not just the first),
        // gated on the same EnableExecutionDiagnostics toggle as the latency instrumentation
        // above - OFF by default, no live-path change. Added for Playback-only diagnosis of the
        // remaining engine/NT8 order-state-sequencing divergence (see PARITY-NOTES.md); not
        // intended to reach a live account. cancelWasPending/entryCancelReason are snapshotted by
        // the caller before this method runs, since ClearActiveEntryContext (called later in the
        // same OnOrderUpdate branch) resets entryCancelPending.
        private void LogEntryOrderTransition(Order order, OrderState orderState, int filled,
            double averageFillPrice, DateTime time, bool cancelWasPending)
        {
            if (!IsExecutionDiagnosticsActive() || order == null)
                return;

            entryOrderTransitionSequence++;
            Print(string.Format(CultureInfo.InvariantCulture,
                "{0} | EMAL ENTRY TRACE | seq={1} state={2} orderName={3} limit={4:F2} bid={5:F2} "
                + "ask={6:F2} last={7:F2} filled={8} avgFill={9:F2} cancelPending={10} cancelReason={11}",
                time,
                entryOrderTransitionSequence,
                orderState,
                order.Name,
                order.LimitPrice,
                GetCurrentBid(),
                GetCurrentAsk(),
                lastTickPrice,
                filled,
                averageFillPrice,
                cancelWasPending,
                cancelWasPending ? entryCancelReason : "-"));

            if (orderState == OrderState.Filled && cancelWasPending)
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | EMAL GAP RACE | entry filled before cancel confirmed | cancelReason={1} fill={2:F2}",
                    time, entryCancelReason, averageFillPrice));
            }
            else if (orderState == OrderState.Cancelled && cancelWasPending)
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | EMAL ENTRY TRACE | cancel confirmed | reason={1}",
                    time, entryCancelReason));
            }
        }

        private void LogFirstEntryExecution(string orderName)
        {
            if (!IsExecutionDiagnosticsActive()
                || entryLatencyExecutionLogged
                || entryLatencySubmitStartTimestamp <= 0L)
            {
                return;
            }

            entryLatencyExecutionLogged = true;
            long executionTimestamp = Stopwatch.GetTimestamp();
            Print(string.Format(CultureInfo.InvariantCulture,
                "{0} | EMAL EXECUTION | account={1} signal={2} firstFill "
                + "fillCallbackUtc={3:O} signalToFillCallbackMs={4:F3} submitStartToFillCallbackMs={5:F3}",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                Account != null ? Account.Name : "-",
                orderName,
                DateTime.UtcNow,
                ElapsedMilliseconds(entryLatencySignalTimestamp, executionTimestamp),
                ElapsedMilliseconds(entryLatencySubmitStartTimestamp, executionTimestamp)));
        }

        private void ResetEntryLatencyTracking()
        {
            entryLatencySignalTimestamp = 0L;
            entryLatencySubmitStartTimestamp = 0L;
            entryLatencySignalUtc = DateTime.MinValue;
            entryLatencySubmitStartUtc = DateTime.MinValue;
            entryLatencyOrderStateLogged = false;
            entryLatencyExecutionLogged = false;
        }

        // Refuses to trade rather than throwing. An unhandled exception disables the strategy
        // with a message that is easy to miss; this leaves it loaded, logs the reason plainly,
        // and latches configurationBlocked so no entry can ever be submitted.
        private void ValidateChart()
        {
            configurationBlocked = false;
            configurationBlockReason = string.Empty;

            // The strategy trades the chart's own bar series (no internal AddDataSeries), so it
            // must be a 1-minute chart.
            if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 1)
            {
                configurationBlocked = true;
                configurationBlockReason = string.Format("needs a 1-min chart (is {0} {1})",
                    BarsPeriod.Value, BarsPeriod.BarsPeriodType);
                Print("EMAL DISABLED: requires a 1-minute chart. Current series is "
                    + BarsPeriod.Value + " " + BarsPeriod.BarsPeriodType
                    + ". No orders will be submitted.");
            }

            string instrumentName = Instrument == null || Instrument.MasterInstrument == null
                ? string.Empty
                : Instrument.MasterInstrument.Name;

            if (!string.Equals(instrumentName, "NQ", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(instrumentName, "MNQ", StringComparison.OrdinalIgnoreCase))
            {
                configurationBlocked = true;
                configurationBlockReason = string.Format("NQ/MNQ only (is '{0}')", instrumentName);
                Print("EMAL DISABLED: supports NQ and MNQ only. Current instrument is '"
                    + instrumentName + "'. No orders will be submitted.");
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            // NT8 renews working historical orders when the strategy enters realtime.
            // Convert stored references before any realtime cancel/change can use them.
            TransitionTrackedOrderReferencesToRealtime();

            if (order == null)
                return;

            string orderName = order.Name ?? string.Empty;
            bool rateLimitedRejection = orderState == OrderState.Rejected
                && IsProviderRateLimitRejection(comment);
            if (rateLimitedRejection)
                MarkProviderRateLimit(comment);

            if (orderName == StopExitSignal || orderName == TargetExitSignal)
            {
                TrackProtectiveOrder(order, orderState);

                // Target touch watchdog: this callback is the cancel confirmation the watchdog
                // is waiting on. Handle it here, before the generic Rejected branch below, so a
                // cancel that comes back Rejected (order already gone) doesn't ALSO trigger the
                // generic ProtectiveReject terminal exit - this is the one and only place that
                // decides the outcome of a watchdog-initiated cancel.
                if (orderName == TargetExitSignal && targetTouchWatchdogCancelPending)
                {
                    bool watchdogTerminalState = orderState == OrderState.Cancelled
                        || orderState == OrderState.Filled
                        || orderState == OrderState.Rejected;
                    if (watchdogTerminalState)
                    {
                        targetTouchWatchdogCancelPending = false;
                        if (filled > 0)
                        {
                            // The limit won the race - do nothing further. Position is already
                            // flattening (or flat) via the normal TargetExitSignal fill path.
                            Print(string.Format(
                                "{0} | EMAL TARGET TOUCH WATCHDOG | target filled before cancel confirmed - no market exit sent",
                                time));
                        }
                        else
                        {
                            string watchdogEntrySignal = string.IsNullOrEmpty(order.FromEntrySignal)
                                ? protectedEntrySignal
                                : order.FromEntrySignal;
                            SubmitTargetTouchMarketExit(watchdogEntrySignal);
                        }
                        return;
                    }
                    // Not yet terminal (e.g. still Working/PartFilled momentarily after the
                    // cancel request) - fall through to normal handling below and wait for the
                    // next callback.
                }

                if (orderState == OrderState.Rejected)
                {
                    Print(string.Format(
                        "{0} | {1} rejected | error={2} comment={3} | flattening position",
                        time,
                        orderName,
                        error,
                        comment ?? string.Empty));

                    string entrySignal = string.IsNullOrEmpty(order.FromEntrySignal)
                        ? protectedEntrySignal
                        : order.FromEntrySignal;
                    TrySubmitTerminalExit("ProtectiveReject", entrySignal);
                }
                else if (orderName == StopExitSignal
                    && (orderState == OrderState.Accepted || orderState == OrderState.Working))
                {
                    // Stage the OCO sibling only after the stop is valid and accepted.
                    SubmitOrUpdateProfitTarget();
                }

                if (orderState == OrderState.Accepted
                    || orderState == OrderState.Working
                    || orderState == OrderState.PartFilled)
                {
                    if (orderName == StopExitSignal)
                        SyncProjectXProtectionUpdate(ProjectXProtectionOrderKind.StopLoss,
                            stopPrice > 0.0 ? stopPrice : order.StopPrice, "nt8-stop-update");
                    else
                        SyncProjectXProtectionUpdate(ProjectXProtectionOrderKind.TakeProfit,
                            limitPrice > 0.0 ? limitPrice : order.LimitPrice, "nt8-target-update");
                }

                // EMAL-1046: gated no-op at Off/1-lot Auto - see the method's own inertness
                // guard. This is the Working/Accepted transition the fix specifically targets -
                // a resize attempted before the order reaches this state is what gets rejected
                // and dropped today. Fires on either sibling's transition and reconciles both.
                if (orderState == OrderState.Accepted || orderState == OrderState.Working)
                {
                    ReconcileMultiContractProtection(
                        orderName == StopExitSignal ? "stop-working" : "target-working", time);
                }

                return;
            }

            if (orderName == TargetTouchExitSignal)
            {
                // Deliberately no retry loop, per the design: "if the market exit is rejected,
                // log and leave the stop in place rather than retrying in a loop." The
                // protective stop is still working (untouched by any of this), so the position
                // remains protected either way.
                if (orderState == OrderState.Rejected)
                {
                    Print(string.Format(
                        "{0} | EMAL TARGET TOUCH WATCHDOG | market exit rejected | error={1} comment={2} | "
                        + "leaving protective stop in place, not retrying",
                        time, error, comment ?? string.Empty));
                }
                return;
            }

            if (IsTerminalExitOrderName(orderName))
            {
                if (orderState == OrderState.Rejected || orderState == OrderState.Cancelled)
                {
                    Print(string.Format(
                        "{0} | CRITICAL: terminal exit {1} {4} | error={2} comment={3}",
                        time,
                        orderName,
                        error,
                        comment ?? string.Empty,
                        orderState));

                    string reason = orderName.Length > TerminalExitSignalPrefix.Length
                        ? orderName.Substring(TerminalExitSignalPrefix.Length)
                        : "Rejected";
                    ScheduleTerminalExitRetry(reason, order.FromEntrySignal, rateLimitedRejection);
                }

                return;
            }

            if (orderName != LongEntrySignal && orderName != ShortEntrySignal)
                return;

            LogFirstEntryOrderState(orderName, orderState);
            bool entryCancelWasPending = entryCancelPending;
            LogEntryOrderTransition(order, orderState, filled, averageFillPrice, time, entryCancelWasPending);

            if (orderState != OrderState.Cancelled
                && orderState != OrderState.Filled
                && orderState != OrderState.Rejected)
            {
                entryOrder = order;
            }
            else if (orderState == OrderState.Filled)
            {
                entryOrder = null;
                filledCount++;

                // Tracked outside the feature-log gate: the daily cap must work with
                // logging off.
                openEntryPrice = averageFillPrice;
                openEntryDirection = order.Name == LongEntrySignal ? 1 : -1;

                CaptureFillFeatures(averageFillPrice, time);
                ClearActiveEntryContext();
                ClearQueuedEntry();
            }
            else if (orderState == OrderState.Cancelled)
            {
                entryOrder = null;
                ClearActiveEntryContext();
                CancelProjectXEntryMirror(Position.MarketPosition == MarketPosition.Flat);
                if (filled == 0 && Position.MarketPosition == MarketPosition.Flat)
                {
                    ReleaseOrderRateReservation();
                    ResetGapLatchTracking();
                }
                else if (Position.MarketPosition == MarketPosition.Flat)
                {
                    ReleaseOrderRateReservation();
                }
                ResetEntryLatencyTracking();
                TrySubmitQueuedEntry();
            }
            else if (orderState == OrderState.Rejected)
            {
                entryOrder = null;
                ClearActiveEntryContext();
                ClearQueuedEntry();
                CancelProjectXEntryMirror(true);
                ReleaseOrderRateReservation();
                if (filled == 0 && Position.MarketPosition == MarketPosition.Flat)
                    ResetGapLatchTracking();
                ResetEntryLatencyTracking();
                Print(string.Format(
                    "{0} | {1} entry rejected | error={2} comment={3}",
                    time,
                    order.Name,
                    error,
                    comment ?? string.Empty));

                // Aug 13-14 live-trade review (Codex): after a broker LiquidationOnly rejection
                // on one incident's account, EMAL kept submitting further entries, which were
                // rejected again. Reuses the same permanent instance-level latch as the wrong-
                // instrument/max-balance/max-daily-profit disables - manual reset (re-enable the
                // instance) required, no auto-clear.
                if (!configurationBlocked && IsLiquidationOnlyRejection(comment))
                {
                    configurationBlocked = true;
                    configurationBlockReason = "broker LiquidationOnly rejection - instance disabled, manual reset required";
                    Print(string.Format(
                        "{0} | LIQUIDATION-ONLY REJECTION | trading stopped for this instance | comment={1}",
                        time,
                        comment ?? string.Empty));
                }
            }
        }

        private bool IsLiquidationOnlyRejection(string comment)
        {
            string text = comment ?? string.Empty;
            return text.IndexOf("liquidation only", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("liquidationonly", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null)
                return;

            string orderName = execution.Name ?? string.Empty;

            if (orderName == LongEntrySignal || orderName == ShortEntrySignal)
            {
                int executionQuantity = Math.Abs(quantity);
                if (executionQuantity <= 0)
                    return;

                if (!string.Equals(protectedEntrySignal, orderName, StringComparison.Ordinal))
                {
                    // Recovery path: protectedEntrySignal doesn't match, so this fill's
                    // BeginProtectionTracking call at signal/submission time never ran for it
                    // (e.g. state lost across a reconnect). No queued signal-tick limit price
                    // exists to arm the gap latch from here, so fall back to this execution's
                    // actual fill price as the anchor - already-post-fill, so it cannot miss a
                    // gap the way the old live re-query could; it just can't benefit from the
                    // pre-fill conservative margin the normal path gets.
                    int recoveredDirection = orderName == LongEntrySignal ? 1 : -1;
                    BeginProtectionTracking(orderName, recoveredDirection, price,
                        GetConfiguredTakeProfit(), GetConfiguredStopLoss());
                }

                entryFillValue += price * executionQuantity;
                entryFilledQuantity += executionQuantity;
                openEntryPrice = entryFillValue / entryFilledQuantity;
                openEntryDirection = orderName == LongEntrySignal ? 1 : -1;

                if (maxAccountBalanceLimitReached || maxDailyProfitLimitReached)
                {
                    TrySubmitTerminalExit(maxAccountBalanceLimitReached ? "MaxAccountBalance" : "MaxDailyProfit", orderName);
                    LogFirstEntryExecution(orderName);
                    return;
                }

                double averageEntryPrice = entryFillValue / entryFilledQuantity;
                SubmitOrUpdateProtection(
                    orderName == LongEntrySignal ? MarketPosition.Long : MarketPosition.Short,
                    averageEntryPrice,
                    entryFilledQuantity,
                    time);
                // EMAL-1046: gated no-op at Off/1-lot Auto - see the method's own inertness
                // guard. Runs after SubmitOrUpdateProtection, which is unchanged above.
                ReconcileMultiContractProtection("entry-fill", time);
                // Protection/terminal handling always wins the critical path; diagnostics are
                // emitted only after the safety order method has completed.
                LogFirstEntryExecution(orderName);
                return;
            }

            if (orderName == StopExitSignal
                || orderName == TargetExitSignal
                || orderName == TargetTouchExitSignal
                || IsTerminalExitOrderName(orderName))
            {
                bool positionIsFlat = marketPosition == MarketPosition.Flat
                    || Position.MarketPosition == MarketPosition.Flat;

                bool mirrorActiveSnapshot;
                lock (projectXStateLock)
                    mirrorActiveSnapshot = projectXEntryMirrorActive;

                if (positionIsFlat && mirrorActiveSnapshot)
                {
                    bool suppressed;
                    lock (projectXStateLock)
                    {
                        suppressed = suppressProjectXNextExecutionExit;
                        if (suppressed)
                        {
                            suppressProjectXNextExecutionExit = false;
                            projectXEntryMirrorActive = false;
                            projectXLastSyncedStopPrice = 0.0;
                            projectXLastSyncedTargetPrice = 0.0;
                            projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                            projectXOrphanRecoveryCount = 0;
                        }
                    }

                    if (!suppressed)
                    {
                        int quantitySnapshot = Math.Abs(quantity);
                        DispatchProjectXSimpleEvent("exit", quantitySnapshot, false, (sent, response) =>
                        {
                            lock (projectXStateLock)
                            {
                                if (sent)
                                {
                                    projectXEntryMirrorActive = false;
                                    projectXLastSyncedStopPrice = 0.0;
                                    projectXLastSyncedTargetPrice = 0.0;
                                    projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                                    projectXOrphanRecoveryCount = 0;
                                }
                                else
                                {
                                    projectXOrphanRecoveryCount++;
                                    projectXOrphanRecoveryDueUtc = DateTime.UtcNow.AddSeconds(5);
                                }
                            }
                        });
                    }
                }

                ClearOpenPositionStateIfFlat(positionIsFlat);

                if (EnableFeatureLog && positionIsFlat)
                    CaptureExitAndWrite(price, time, orderName);

                CancelRemainingEntryAfterExit();

                if (positionIsFlat)
                    ResetProtectionTracking();
                else if (IsTerminalExitOrderName(orderName))
                    ScheduleTerminalExitRetry("ResidualPosition", execution.Order.FromEntrySignal, false);
            }
        }

        private void SubmitOrUpdateProtection(MarketPosition positionDirection, double averageEntryPrice,
            int protectedQuantity, DateTime time)
        {
            if (positionDirection == MarketPosition.Flat
                || protectedQuantity <= 0)
            {
                return;
            }

            // activeStopLossPoints is snapshotted at entry; fall back to the fixed value if a
            // position somehow exists without protection tracking having been started.
            double stopDistance = Math.Max(TickSize,
                activeStopLossPoints > 0.0 ? activeStopLossPoints : DefaultSafetyStopLossPoints);
            double targetDistance = Math.Max(TickSize, activeTakeProfitPoints);

            // Barriers measured from the actual fill (Limit Offset / Bracket Anchor removed
            // 2026-08-06 - both existed only to support a non-zero entry offset).
            double anchor = averageEntryPrice;

            double stopPrice = positionDirection == MarketPosition.Long
                ? anchor - stopDistance
                : anchor + stopDistance;
            double targetPrice = positionDirection == MarketPosition.Long
                ? anchor + targetDistance
                : anchor - targetDistance;

            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);
            desiredProtectionTargetPrice = targetPrice;
            desiredProtectionQuantity = protectedQuantity;

            if (State == State.Realtime)
            {
                // EMAL-1037: the gap decision is read from the tick-driven latch armed at the
                // signal tick (ArmGapLatch/EvaluateGapLatch), not from a fresh
                // GetProtectiveReferencePrice() snapshot. The old live re-query made the
                // decision a function of wall-clock time - whatever tick happened to be
                // current when this account's own fill-confirmation callback landed - so two
                // instances filled at the identical price could take different exit paths
                // purely on broker round-trip jitter (see EMAL-1037-changelog.txt for the
                // 2026-08-12 incident). The latch instead answers "did any tick since the
                // signal cross the level", which is identical for every instance on the same
                // feed regardless of when each one's callback fires. GetProtectiveReferencePrice
                // is kept below only for the diagnostic print, not for path selection.
                double marketPrice = GetProtectiveReferencePrice(positionDirection);
                bool stopAlreadyBreached = gapStopBreached;
                bool targetAlreadyReached = gapTargetBreached;

                if (stopAlreadyBreached)
                {
                    Print(string.Format(
                        "{0} | {1} stop gap-through | fill={2:F2} stop={3:F2} market={4:F2} | flattening",
                        time,
                        positionDirection,
                        averageEntryPrice,
                        stopPrice,
                        marketPrice));
                    TrySubmitTerminalExit("GapStop", protectedEntrySignal);
                    return;
                }

                if (targetAlreadyReached)
                {
                    Print(string.Format(
                        "{0} | {1} target crossed before protection | fill={2:F2} target={3:F2} market={4:F2} | flattening",
                        time,
                        positionDirection,
                        averageEntryPrice,
                        targetPrice,
                        marketPrice));
                    TrySubmitTerminalExit("GapTarget", protectedEntrySignal);
                    return;
                }
            }

            if (IsOrderActive(protectiveStopOrder))
            {
                bool quantityMatches = protectiveStopOrder.Quantity == protectedQuantity;
                bool priceMatches = Math.Abs(protectiveStopOrder.StopPrice - stopPrice) < TickSize / 2.0;
                if (!quantityMatches || !priceMatches)
                {
                    RecordNtOrderAction("change-stop");
                    ChangeOrder(protectiveStopOrder, protectedQuantity, 0.0, stopPrice);
                }
            }
            else
            {
                if (terminalExitPending)
                    return;

                RecordNtOrderAction("submit-stop");
                protectiveStopOrder = positionDirection == MarketPosition.Long
                    ? ExitLongStopMarket(0, true, protectedQuantity, stopPrice, StopExitSignal, protectedEntrySignal)
                    : ExitShortStopMarket(0, true, protectedQuantity, stopPrice, StopExitSignal, protectedEntrySignal);
            }

            // A rejection can callback synchronously from the submission above. Do not submit
            // its OCO sibling with an identifier NinjaTrader has already retired.
            if (terminalExitPending
                || Position.MarketPosition == MarketPosition.Flat
                || IsTerminalExitRetryWaiting())
            {
                return;
            }

            if (protectiveStopOrder == null
                || protectiveStopOrder.OrderState == OrderState.Rejected)
            {
                TrySubmitTerminalExit("MissingStop", protectedEntrySignal);
                return;
            }

            // The accepted stop can fill before this method returns. In that case the
            // position is already closing and no target sibling should be submitted.
            if (!IsOrderActive(protectiveStopOrder))
                return;

            // Normally the stop's Accepted/Working callback stages the target. This fallback
            // also restores a missing target after a rejected terminal-exit recovery.
            if (!terminalExitPending)
                SubmitOrUpdateProfitTarget();
        }

        private void SubmitOrUpdateProfitTarget()
        {
            if (terminalExitPending
                || Position.MarketPosition == MarketPosition.Flat
                || !IsOrderActive(protectiveStopOrder)
                || desiredProtectionQuantity <= 0
                || desiredProtectionTargetPrice <= 0.0)
            {
                return;
            }

            if (IsOrderActive(profitTargetOrder))
            {
                bool quantityMatches = profitTargetOrder.Quantity == desiredProtectionQuantity;
                bool priceMatches = Math.Abs(
                    profitTargetOrder.LimitPrice - desiredProtectionTargetPrice) < TickSize / 2.0;

                if (quantityMatches && priceMatches)
                    return;

                RecordNtOrderAction("change-target");
                ChangeOrder(
                    profitTargetOrder,
                    desiredProtectionQuantity,
                    desiredProtectionTargetPrice,
                    0.0);
            }
            else
            {
                RecordNtOrderAction("submit-target");
                profitTargetOrder = Position.MarketPosition == MarketPosition.Long
                    ? ExitLongLimit(
                        0,
                        true,
                        desiredProtectionQuantity,
                        desiredProtectionTargetPrice,
                        TargetExitSignal,
                        protectedEntrySignal)
                    : ExitShortLimit(
                        0,
                        true,
                        desiredProtectionQuantity,
                        desiredProtectionTargetPrice,
                        TargetExitSignal,
                        protectedEntrySignal);
            }

            if (!terminalExitPending
                && !IsTerminalExitRetryWaiting()
                && Position.MarketPosition != MarketPosition.Flat
                && (profitTargetOrder == null || profitTargetOrder.OrderState == OrderState.Rejected))
            {
                TrySubmitTerminalExit("MissingTarget", protectedEntrySignal);
            }
        }

        // ---- Multi-contract protection reconciliation (EMAL-1046, Steve, 2026-08-18) ----
        // Fixes an observed multi-lot bug (account 1367, 2026-08-13 and 2026-08-17): on a
        // partial-filled multi-contract entry, a stop/target resize ChangeOrder can reach the
        // broker before the protective order is in Working state, get rejected by Tradovate,
        // and be silently dropped (RealtimeErrorHandling = IgnoreAllErrors covers the strategy
        // as a whole). Result: protection ends up under-sized for the position (e.g. a 3-lot
        // position left with a 1-lot stop). Single-contract accounts never partial-fill this
        // way and are unaffected - confirmed by the inertness guard below, not by assumption.
        //
        // This does NOT modify SubmitOrUpdateProtection/SubmitOrUpdateProfitTarget above (the
        // existing single-fill placement path, unchanged) - it is a separate, gated,
        // additional reconciliation step called after that path from two points: an entry
        // execution fill (OnExecutionUpdate) and a stop/target order's transition to
        // Accepted/Working (OnOrderUpdate). It only ever resizes an existing protective order
        // to match Position.Quantity - it never creates a second stop/target, never changes a
        // price, and never touches the gap latch or the touch watchdog.
        //
        // HARD INERTNESS GUARD: these two lines are the first executable statements in this
        // method. Nothing above them allocates, subscribes, or places/modifies/cancels an
        // order. At Off (the default) or at Auto with a 1-lot position, the method enters and
        // returns immediately - zero effect, on every account, matching the prior cut's
        // behavior exactly.
        private void ReconcileMultiContractProtection(string trigger, DateTime time)
        {
            if (MultiContractProtectionFix == EMALMultiContractProtectionFix.Off)
                return;
            if (MultiContractProtectionFix == EMALMultiContractProtectionFix.Auto && Position.Quantity <= 1)
                return;

            try
            {
                // Position.Quantity is the source of truth for what is actually filled - never
                // resize a protective order above this, even transiently during an in-flight
                // partial fill.
                int positionQuantity = Position.Quantity;
                if (positionQuantity <= 0)
                    return;

                ReconcileProtectiveStopQuantity(positionQuantity, trigger, time);
                ReconcileProtectiveTargetQuantity(positionQuantity, trigger, time);
            }
            catch (Exception ex)
            {
                // Must never propagate into the trading path - this is a diagnostic/safety
                // add-on, not allowed to rely on RealtimeErrorHandling.IgnoreAllErrors to
                // contain a fault here.
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | MULTI-CONTRACT PROTECTION FIX ERROR | trigger={1} | {2}",
                    time, trigger, ex.Message));
            }
        }

        private void ReconcileProtectiveStopQuantity(int positionQuantity, string trigger, DateTime time)
        {
            if (!IsOrderActive(protectiveStopOrder))
                return;   // nothing to resize - order placement itself is unchanged, above

            // Resizing before the order is Working/Accepted is exactly the failure mode being
            // fixed (a resize submitted too early is what Tradovate rejects and drops today) -
            // wait for a real Working/Accepted callback rather than attempting one blind.
            if (protectiveStopOrder.OrderState != OrderState.Working
                && protectiveStopOrder.OrderState != OrderState.Accepted)
            {
                return;
            }

            int resizeQuantity = Math.Min(positionQuantity, Position.Quantity);
            if (resizeQuantity <= 0 || protectiveStopOrder.Quantity == resizeQuantity)
                return;   // already matches - no ChangeOrder, no log line

            if (stopReconcileAttempts >= MaxReconcileAttempts)
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | MULTI-CONTRACT PROTECTION FIX | stop still mismatched after {1} attempts | "
                    + "orderQty={2} positionQty={3} trigger={4}",
                    time, MaxReconcileAttempts, protectiveStopOrder.Quantity, resizeQuantity, trigger));
                return;
            }

            stopReconcileAttempts++;
            Print(string.Format(CultureInfo.InvariantCulture,
                "{0} | MULTI-CONTRACT PROTECTION FIX | resizing stop | orderQty={1} -> {2} | trigger={3} attempt={4}/{5}",
                time, protectiveStopOrder.Quantity, resizeQuantity, trigger, stopReconcileAttempts, MaxReconcileAttempts));
            RecordNtOrderAction("multicontract-resize-stop");
            ChangeOrder(protectiveStopOrder, resizeQuantity, 0.0, protectiveStopOrder.StopPrice);
            // A rejection of this ChangeOrder surfaces through the existing OnOrderUpdate
            // Rejected branch for StopExitSignal/TargetExitSignal orders (prints and flattens
            // via TrySubmitTerminalExit("ProtectiveReject", ...)) - not swallowed, reuses the
            // same visible/safe path every other protective-order rejection already takes.
        }

        private void ReconcileProtectiveTargetQuantity(int positionQuantity, string trigger, DateTime time)
        {
            if (!IsOrderActive(profitTargetOrder))
                return;   // nothing to resize - order placement itself is unchanged, above

            if (profitTargetOrder.OrderState != OrderState.Working
                && profitTargetOrder.OrderState != OrderState.Accepted)
            {
                return;
            }

            int resizeQuantity = Math.Min(positionQuantity, Position.Quantity);
            if (resizeQuantity <= 0 || profitTargetOrder.Quantity == resizeQuantity)
                return;   // already matches - no ChangeOrder, no log line

            if (targetReconcileAttempts >= MaxReconcileAttempts)
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | MULTI-CONTRACT PROTECTION FIX | target still mismatched after {1} attempts | "
                    + "orderQty={2} positionQty={3} trigger={4}",
                    time, MaxReconcileAttempts, profitTargetOrder.Quantity, resizeQuantity, trigger));
                return;
            }

            targetReconcileAttempts++;
            Print(string.Format(CultureInfo.InvariantCulture,
                "{0} | MULTI-CONTRACT PROTECTION FIX | resizing target | orderQty={1} -> {2} | trigger={3} attempt={4}/{5}",
                time, profitTargetOrder.Quantity, resizeQuantity, trigger, targetReconcileAttempts, MaxReconcileAttempts));
            RecordNtOrderAction("multicontract-resize-target");
            ChangeOrder(profitTargetOrder, resizeQuantity, profitTargetOrder.LimitPrice, 0.0);
            // Rejection handling: see the identical comment in ReconcileProtectiveStopQuantity.
        }

        private double GetProtectiveReferencePrice(MarketPosition positionDirection)
        {
            double marketPrice = positionDirection == MarketPosition.Long
                ? GetCurrentBid()
                : GetCurrentAsk();

            if (marketPrice <= 0.0 || double.IsNaN(marketPrice) || double.IsInfinity(marketPrice))
                marketPrice = lastTickPrice;

            if ((marketPrice <= 0.0 || double.IsNaN(marketPrice) || double.IsInfinity(marketPrice))
                && CurrentBar >= 0)
            {
                marketPrice = Close[0];
            }

            return marketPrice;
        }

        private void TrackProtectiveOrder(Order order, OrderState orderState)
        {
            bool terminalState = orderState == OrderState.Cancelled
                || orderState == OrderState.Filled
                || orderState == OrderState.Rejected;

            if (order.Name == StopExitSignal)
                protectiveStopOrder = terminalState ? null : order;
            else if (order.Name == TargetExitSignal)
                profitTargetOrder = terminalState ? null : order;
        }

        private void TrySubmitTerminalExit(string reason, string entrySignal)
        {
            if (terminalExitPending || IsTerminalExitRetryWaiting())
                return;

            MarketPosition positionDirection = Position.MarketPosition;
            if (positionDirection == MarketPosition.Flat)
                return;

            terminalExitPending = true;
            CancelRemainingEntryAfterExit();

            string exitSignal = TerminalExitSignalPrefix + reason;
            string fromEntrySignal = string.IsNullOrEmpty(entrySignal)
                ? protectedEntrySignal
                : entrySignal;

            Print(string.Format(
                "{0} | emergency market exit | reason={1} side={2} entry={3}",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                reason,
                positionDirection,
                fromEntrySignal));

            terminalExitRetryReason = reason ?? string.Empty;
            terminalExitRetryEntrySignal = fromEntrySignal ?? string.Empty;
            RecordNtOrderAction("emergency-exit-" + (reason ?? string.Empty));

            if (positionDirection == MarketPosition.Long)
                ExitLong(exitSignal, fromEntrySignal);
            else
                ExitShort(exitSignal, fromEntrySignal);

            SendExplicitProjectXExit("emergency-" + (reason ?? string.Empty));
        }

        private void CancelRemainingEntryAfterExit()
        {
            if (!IsOrderActive(entryOrder)
                || entryCancelPending
                || IsHistoricalOrderAwaitingRealtimeTransition(entryOrder))
            {
                return;
            }

            entryCancelPending = true;
            RecordNtOrderAction("cancel-entry-after-exit");
            CancelOrder(entryOrder);
        }

        private void CancelWorkingEntryOnTermination()
        {
            if (Account == null
                || string.Equals(Account.Name, "Backtest", StringComparison.OrdinalIgnoreCase)
                || !IsOrderActive(entryOrder)
                || entryOrder.IsBacktestOrder)
                return;

            try
            {
                if (Account != null)
                    Account.Cancel(new[] { entryOrder });

                // synchronousDirect: called from State.Terminated, after the worker has already
                // been stopped/drained (see OnStateChange) - the queue is gone, so this must run
                // directly or it would silently do nothing.
                CancelProjectXEntryMirror(Position.MarketPosition == MarketPosition.Flat, synchronousDirect: true);
                Print(string.Format(
                    "{0} | strategy termination safety | working entry cancellation requested; protective exits left working",
                    lastTickTime != DateTime.MinValue ? lastTickTime : DateTime.Now));
            }
            catch (Exception ex)
            {
                Print("EMAL CRITICAL: could not cancel working entry during termination: " + ex.Message);
            }
        }

        private void FlattenProjectXOrphanOnTermination()
        {
            bool mirrorActive;
            lock (projectXStateLock)
                mirrorActive = projectXEntryMirrorActive;

            if (!mirrorActive
                || Position.MarketPosition != MarketPosition.Flat)
            {
                return;
            }

            // Deliberately still synchronous here (via SendWebhook -> SendProjectXWork directly,
            // bypassing the queue) - called from State.Terminated, after StopProjectXWorker has
            // already stopped/drained the worker, so this is the one place ProjectX HTTP is meant
            // to block: the strategy is already shutting down and the existing bounded-timeout
            // flatten verification (ProjectXFlattenPosition's 4s waits) needs to complete before
            // termination proceeds.
            if (!SendWebhook("exit"))
                Print("EMAL CRITICAL: ProjectX mirror could not be verified flat during strategy termination.");
            else
                lock (projectXStateLock)
                    projectXEntryMirrorActive = false;
        }

        // Clears the open-entry snapshot (openEntryPrice/openEntryDirection, read by the
        // protective-order fallback) once the position is fully flat. Was previously also
        // where daily realised points were accumulated for the two removed daily caps
        // (Steve, 2026-08-06) - the exit-value accumulation that fed that calculation had no
        // other reader, so it was removed along with the caps rather than left computing an
        // unused number.
        private void ClearOpenPositionStateIfFlat(bool positionIsFlat)
        {
            if (!positionIsFlat)
                return;

            openEntryDirection = 0;
            openEntryPrice = 0.0;
        }

        private bool IsAccountBalanceBlocked()
        {
            if (MaxAccountBalance <= 0.0)
                return false;

            if (maxAccountBalanceLimitReached)
                return true;

            double netLiquidation;
            if (!TryGetCurrentNetLiquidation(out netLiquidation)
                || netLiquidation < MaxAccountBalance)
            {
                return false;
            }

            maxAccountBalanceLimitReached = true;
            ClearQueuedEntry();
            CancelRemainingEntryAfterExit();

            if (Position.MarketPosition != MarketPosition.Flat)
                TrySubmitTerminalExit("MaxAccountBalance", protectedEntrySignal);

            // Steve, 2026-08-07: latch the same configurationBlocked flag used for a wrong
            // chart/instrument - OnBarUpdate returns immediately once this is set (see the
            // check near its top), so no further entries are ever evaluated again for the
            // life of this instance. This does NOT touch NT8's own Enabled checkbox/state
            // machine - it stops the strategy's own trading logic from the inside, the same
            // mechanism already used for the other permanent disables. Exit/position
            // management (OnOrderUpdate, OnExecutionUpdate, EvaluateTerminalExitRecovery via
            // OnMarketData) is untouched by this flag, so the terminal exit above still gets
            // retried if it fails.
            configurationBlocked = true;
            configurationBlockReason = "max account balance reached";

            Print(string.Format(
                "{0} | max account balance reached | netLiq={1:F2} target={2:F2} | trading stopped",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                netLiquidation,
                MaxAccountBalance));

            return true;
        }

        // CME trading day: 18:00 ET starts the NEXT day's session, so market-close (not
        // midnight) is the reset boundary. Keeps an overnight session in one bucket.
        private DateTime GetTradingDay(DateTime easternTime)
        {
            return easternTime.Hour >= 18
                ? easternTime.Date.AddDays(1)
                : easternTime.Date;
        }

        // Max Daily Profit (Steve, 2026-08-07, re-added - see the maxDailyProfitLimitReached
        // field comment for why). Deliberately does NOT latch configurationBlocked the way
        // IsAccountBalanceBlocked does - this cap is meant to reset every trading day (below),
        // so permanently disabling the instance would defeat that. Blocks new entries and
        // flattens any open position for the rest of the current trading day only. Resets at
        // 18:00 ET (market close / CME trading day boundary), NOT midnight - Steve confirmed
        // eval-account rules reset with the session, not the calendar date.
        private bool IsAccountDailyProfitBlocked()
        {
            if (MaxDailyProfit <= 0.0)
            {
                maxDailyProfitLimitReached = false;
                maxDailyProfitStartBalance = double.NaN;
                maxDailyProfitDate = DateTime.MinValue;
                return false;
            }

            DateTime currentDate = GetTradingDay(ConvertToEastern(lastTickTime != DateTime.MinValue ? lastTickTime : Time[0]));
            if (maxDailyProfitDate != currentDate)
            {
                maxDailyProfitDate = currentDate;
                maxDailyProfitLimitReached = false;
                maxDailyProfitStartBalance = double.NaN;
            }

            if (maxDailyProfitLimitReached)
                return true;

            double netLiquidation;
            if (!TryGetCurrentNetLiquidation(out netLiquidation))
                return false;

            if (double.IsNaN(maxDailyProfitStartBalance))
            {
                maxDailyProfitStartBalance = netLiquidation;
                return false;
            }

            double dailyProfit = netLiquidation - maxDailyProfitStartBalance;
            if (dailyProfit < MaxDailyProfit)
                return false;

            maxDailyProfitLimitReached = true;
            ClearQueuedEntry();
            CancelRemainingEntryAfterExit();

            if (Position.MarketPosition != MarketPosition.Flat)
                TrySubmitTerminalExit("MaxDailyProfit", protectedEntrySignal);

            Print(string.Format(
                "{0} | max daily profit reached | startNetLiq={1:F2} netLiq={2:F2} profit={3:F2} target={4:F2} | trading stopped for {5:yyyy-MM-dd}",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                maxDailyProfitStartBalance,
                netLiquidation,
                dailyProfit,
                MaxDailyProfit,
                maxDailyProfitDate));

            return true;
        }

        private bool TryGetCurrentNetLiquidation(out double netLiquidation)
        {
            netLiquidation = 0.0;
            if (Account == null)
                return false;

            try
            {
                netLiquidation = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                if (netLiquidation > 0.0
                    && !double.IsNaN(netLiquidation)
                    && !double.IsInfinity(netLiquidation))
                {
                    return true;
                }

                double realizedCash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                double unrealized = Position.MarketPosition != MarketPosition.Flat
                    ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0])
                    : 0.0;

                netLiquidation = realizedCash + unrealized;
                return (realizedCash > 0.0 || Position.MarketPosition != MarketPosition.Flat)
                    && !double.IsNaN(netLiquidation)
                    && !double.IsInfinity(netLiquidation);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTerminalExitOrderName(string orderName)
        {
            return !string.IsNullOrEmpty(orderName)
                && orderName.StartsWith(TerminalExitSignalPrefix, StringComparison.Ordinal);
        }

        private void ResetProtectionTracking()
        {
            protectedEntrySignal = string.Empty;
            activeTakeProfitPoints = 0.0;
            activeStopLossPoints = 0.0;
            entryFillValue = 0.0;
            entryFilledQuantity = 0;
            desiredProtectionTargetPrice = 0.0;
            desiredProtectionQuantity = 0;
            protectiveStopOrder = null;
            profitTargetOrder = null;
            terminalExitPending = false;
            stopReconcileAttempts = 0;
            targetReconcileAttempts = 0;
            ClearTerminalExitRetry();
            ReleaseOrderRateReservation();
            ResetGapLatchTracking();
            ResetEntryLatencyTracking();
        }

        private void ResetGapLatchTracking()
        {
            gapLatchArmed = false;
            gapLatchDirection = 0;
            gapLatchTargetPrice = 0.0;
            gapLatchStopPrice = 0.0;
            gapTargetBreached = false;
            gapStopBreached = false;
            // Target touch watchdog resets alongside the gap latch, per the design - both are
            // per-trade state that must not survive into the next trade. Deliberately excludes
            // targetTouchWatchdogScopeGuardLogged and plannedTargetTouchLevelFallbackLogged,
            // both instance-lifetime "logged once" flags, not per-trade state.
            targetTouchedUtc = DateTime.MinValue;
            targetTouchWatchdogFired = false;
            targetTouchWatchdogCancelPending = false;
            plannedTargetTouchLevel = 0.0;
            // EMAL-1045: per-trade quote state. Zeroing the bid/ask fields means a stale quote
            // from the previous trade can never be evaluated against a freshly armed latch
            // before a fresh Bid/Ask tick arrives (both evaluators already guard on price > 0).
            // targetTouchSource is diagnostic-only and meaningless until targetTouchedUtc is set
            // again, but reset here anyway so a stale value never appears in a print by accident.
            lastBidPrice = 0.0;
            lastAskPrice = 0.0;
            targetTouchSource = MarketDataType.Last;
        }

        private bool IsLiveOrderRateGuardActive()
        {
            return State == State.Realtime
                && !IsPlaybackOrderContext();
        }

        private bool IsPlaybackOrderContext()
        {
            try
            {
                if (Account != null
                    && !string.IsNullOrWhiteSpace(Account.Name)
                    && Account.Name.IndexOf("Playback", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                if (Account != null && Account.Connection != null)
                {
                    string connectionName = Account.Connection.Options != null
                        ? Account.Connection.Options.Name
                        : Account.Connection.ToString();

                    return !string.IsNullOrWhiteSpace(connectionName)
                        && connectionName.IndexOf("Playback", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
            }

            return false;
        }

        private object GetOrderRateGuardKey()
        {
            try
            {
                if (Account != null && Account.Connection != null)
                    return Account.Connection;
            }
            catch
            {
            }

            return Account != null
                ? (object)("EMAL-account:" + (Account.Name ?? string.Empty))
                : (object)"EMAL-no-account";
        }

        private static void PruneOrderRateState(SharedOrderRateState state, DateTime nowUtc)
        {
            DateTime cutoffUtc = nowUtc.AddHours(-1);
            while (state.ActionsUtc.Count > 0 && state.ActionsUtc.Peek() <= cutoffUtc)
                state.ActionsUtc.Dequeue();

            if (state.ProviderBlockedUntilUtc <= nowUtc)
            {
                state.ProviderBlockedUntilUtc = DateTime.MinValue;
                state.ProviderBlockReason = string.Empty;
            }
        }

        private SharedOrderRateState GetOrCreateOrderRateState(object key)
        {
            SharedOrderRateState state;
            if (!OrderRateStates.TryGetValue(key, out state))
            {
                state = new SharedOrderRateState();
                OrderRateStates[key] = state;
            }
            return state;
        }

        private bool TryReserveNewTradeActions(out string blockReason)
        {
            blockReason = string.Empty;
            if (!IsLiveOrderRateGuardActive())
                return true;

            object key = GetOrderRateGuardKey();
            DateTime nowUtc = DateTime.UtcNow;
            lock (OrderRateGuardSync)
            {
                SharedOrderRateState state = GetOrCreateOrderRateState(key);
                PruneOrderRateState(state, nowUtc);

                if (state.ProviderBlockedUntilUtc > nowUtc)
                {
                    blockReason = string.Format(
                        "provider cooldown until {0:HH:mm:ss} UTC",
                        state.ProviderBlockedUntilUtc);
                    return false;
                }

                int existingReservation;
                if (state.Reservations.TryGetValue(orderRateInstanceId, out existingReservation)
                    && existingReservation > 0)
                {
                    return true;
                }

                int reserved = state.Reservations.Values.Sum();
                int projected = state.ActionsUtc.Count + reserved + NewTradeActionReserve;
                int limit = Math.Max(NewTradeActionReserve, OrderActionLimitPerHour);
                if (projected > limit)
                {
                    blockReason = string.Format(
                        "order guard {0}/{1} incl. reserve",
                        projected,
                        limit);
                    return false;
                }

                state.Reservations[orderRateInstanceId] = NewTradeActionReserve;
                return true;
            }
        }

        private void RecordNtOrderAction(string action)
        {
            if (!IsLiveOrderRateGuardActive())
                return;

            object key = GetOrderRateGuardKey();
            DateTime nowUtc = DateTime.UtcNow;
            int used;
            int limit = Math.Max(NewTradeActionReserve, OrderActionLimitPerHour);

            lock (OrderRateGuardSync)
            {
                SharedOrderRateState state = GetOrCreateOrderRateState(key);
                PruneOrderRateState(state, nowUtc);
                state.ActionsUtc.Enqueue(nowUtc);

                int remaining;
                if (state.Reservations.TryGetValue(orderRateInstanceId, out remaining))
                {
                    remaining--;
                    if (remaining > 0)
                        state.Reservations[orderRateInstanceId] = remaining;
                    else
                        state.Reservations.Remove(orderRateInstanceId);
                }

                used = state.ActionsUtc.Count + state.Reservations.Values.Sum();
            }

            if (used >= limit)
            {
                Print(string.Format(
                    "{0} | ORDER RATE GUARD | action={1} projected={2}/{3} | new entries blocked; safety orders remain enabled",
                    lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                    action,
                    used,
                    limit));
            }
        }

        private void ReleaseOrderRateReservation()
        {
            object key = GetOrderRateGuardKey();
            lock (OrderRateGuardSync)
            {
                SharedOrderRateState state;
                if (OrderRateStates.TryGetValue(key, out state))
                    state.Reservations.Remove(orderRateInstanceId);
            }
        }

        private void MarkProviderRateLimit(string comment)
        {
            if (!IsLiveOrderRateGuardActive())
                return;

            object key = GetOrderRateGuardKey();
            DateTime blockedUntilUtc = DateTime.UtcNow.AddHours(1);
            lock (OrderRateGuardSync)
            {
                SharedOrderRateState state = GetOrCreateOrderRateState(key);
                if (blockedUntilUtc > state.ProviderBlockedUntilUtc)
                    state.ProviderBlockedUntilUtc = blockedUntilUtc;
                state.ProviderBlockReason = comment ?? string.Empty;
            }

            Print(string.Format(
                "{0} | CRITICAL ORDER RATE LIMIT | all EMAL entries on this connection blocked until {1:HH:mm:ss} UTC | protection/exits still allowed | {2}",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                blockedUntilUtc,
                comment ?? string.Empty));
        }

        private bool IsProviderRateLimitRejection(string comment)
        {
            string text = comment ?? string.Empty;
            return text.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("1500 requests", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("too many requests", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryGetOrderRateStatus(out int projectedActions, out int limit, out DateTime providerBlockedUntilUtc)
        {
            projectedActions = 0;
            limit = Math.Max(NewTradeActionReserve, OrderActionLimitPerHour);
            providerBlockedUntilUtc = DateTime.MinValue;
            if (!IsLiveOrderRateGuardActive())
                return false;

            object key = GetOrderRateGuardKey();
            lock (OrderRateGuardSync)
            {
                SharedOrderRateState state = GetOrCreateOrderRateState(key);
                PruneOrderRateState(state, DateTime.UtcNow);
                projectedActions = state.ActionsUtc.Count + state.Reservations.Values.Sum();
                providerBlockedUntilUtc = state.ProviderBlockedUntilUtc;
            }
            return true;
        }

        private void ClearTerminalExitRetry()
        {
            terminalExitRetryReason = string.Empty;
            terminalExitRetryEntrySignal = string.Empty;
            terminalExitRetryDueUtc = DateTime.MinValue;
            terminalExitRetryCount = 0;
            terminalExitRetryExhaustedLogged = false;
        }

        private bool IsTerminalExitRetryWaiting()
        {
            return !string.IsNullOrWhiteSpace(terminalExitRetryReason)
                && terminalExitRetryDueUtc > DateTime.UtcNow;
        }

        private void ScheduleTerminalExitRetry(string reason, string entrySignal, bool rateLimited)
        {
            terminalExitPending = false;
            terminalExitRetryReason = string.IsNullOrWhiteSpace(reason) ? "Rejected" : reason;
            terminalExitRetryEntrySignal = string.IsNullOrWhiteSpace(entrySignal)
                ? protectedEntrySignal
                : entrySignal;
            terminalExitRetryCount++;

            if (terminalExitRetryCount > MaxTerminalExitRetries)
            {
                terminalExitRetryDueUtc = DateTime.MaxValue;
                if (!terminalExitRetryExhaustedLogged)
                {
                    terminalExitRetryExhaustedLogged = true;
                    Print(string.Format(
                        "{0} | CRITICAL: emergency exit retry limit reached while position remains {1}; verify account protection manually",
                        lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                        Position.MarketPosition));
                }
                return;
            }

            int[] delays = { 2, 5, 15, 30, 60, 120, 300, 300 };
            int delaySeconds = delays[Math.Min(terminalExitRetryCount - 1, delays.Length - 1)];
            if (rateLimited)
                delaySeconds = Math.Max(delaySeconds, 60);
            terminalExitRetryDueUtc = DateTime.UtcNow.AddSeconds(delaySeconds);

            Print(string.Format(
                "{0} | emergency exit retry scheduled | attempt={1}/{2} due={3:HH:mm:ss} UTC reason={4}",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                terminalExitRetryCount,
                MaxTerminalExitRetries,
                terminalExitRetryDueUtc,
                terminalExitRetryReason));
        }

        private void EvaluateTerminalExitRecovery()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearTerminalExitRetry();
                return;
            }

            if (terminalExitPending || string.IsNullOrWhiteSpace(terminalExitRetryReason))
                return;

            if (DateTime.UtcNow < terminalExitRetryDueUtc)
                return;

            // Restore/resize the stop before another market-exit attempt. This is the critical
            // difference from the old latch: a failed exit can no longer freeze partial-fill
            // protection at an undersized quantity.
            int quantity = Math.Abs(Position.Quantity);
            double averagePrice = openEntryPrice > 0.0 ? openEntryPrice : Position.AveragePrice;
            if (quantity > 0 && averagePrice > 0.0)
                SubmitOrUpdateProtection(Position.MarketPosition, averagePrice, quantity,
                    lastTickTime != DateTime.MinValue ? lastTickTime : Time[0]);

            if (!terminalExitPending && terminalExitRetryCount <= MaxTerminalExitRetries)
            {
                TrySubmitTerminalExit(terminalExitRetryReason, terminalExitRetryEntrySignal);
            }
        }

        // ---- ProjectX async worker lifecycle (EMAL-1041) ----

        private void StartProjectXWorker()
        {
            if (projectXWorkerThread != null)
                return;

            projectXQueue = new System.Collections.Concurrent.BlockingCollection<ProjectXWorkItem>();
            projectXWorkerThread = new System.Threading.Thread(ProjectXWorkerLoop)
            {
                IsBackground = true,
                Name = "EMAL-ProjectX-" + orderRateInstanceId
            };
            projectXWorkerThread.Start();
        }

        // Called once from State.Terminated, BEFORE CancelWorkingEntryOnTermination/
        // FlattenProjectXOrphanOnTermination run their own direct synchronous sends. Stops new
        // items being accepted and waits (bounded) for whatever is already queued to finish in
        // order, so those two termination methods see up-to-date mirror state. If the drain times
        // out, log it and proceed anyway - termination must not hang indefinitely on ProjectX.
        private void StopProjectXWorker()
        {
            if (projectXQueue == null)
                return;

            try
            {
                projectXQueue.CompleteAdding();
                if (projectXWorkerThread != null && !projectXWorkerThread.Join(5000))
                    ProjectXLog("ProjectX worker did not drain within 5s at termination - proceeding anyway");
            }
            catch (Exception ex)
            {
                ProjectXLog("ProjectX worker shutdown error | error=" + ex.Message);
            }
            finally
            {
                projectXWorkerThread = null;
                projectXQueue = null;
            }
        }

        private void EnqueueProjectXWork(ProjectXWorkItem item)
        {
            if (projectXQueue == null || item == null)
                return;
            try
            {
                projectXQueue.Add(item);
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding() already called (mid-shutdown) - drop it; termination's own
                // direct synchronous sends are what handle ProjectX from this point on.
            }
        }

        private void ProjectXWorkerLoop()
        {
            foreach (ProjectXWorkItem item in projectXQueue.GetConsumingEnumerable())
            {
                try
                {
                    ExecuteProjectXWorkItem(item);
                }
                catch (Exception ex)
                {
                    // Must never let one bad item kill the worker - RealtimeErrorHandling=
                    // IgnoreAllErrors means nothing may throw silently anywhere near the strategy
                    // thread, and an exception here would otherwise end the foreach and leave every
                    // later queued item (and everything enqueued after) permanently stuck.
                    try { ProjectXLog("ProjectX worker item failed unexpectedly | error=" + ex.Message); }
                    catch { /* logging itself must never take the worker down either */ }
                }
            }
        }

        // Runs entirely on the worker thread (or, during termination, directly on the strategy
        // thread after the worker has been joined - see StopProjectXWorker). Touches only the
        // item's captured plain values and the worker-only cache fields documented above the
        // ProjectXWorkItem class - no NT object, no NT method, anywhere in this call graph.
        private void ExecuteProjectXWorkItem(ProjectXWorkItem item)
        {
            bool success;
            string response = null;
            if (item.ProtectionKind.HasValue)
            {
                success = ExecuteProjectXProtectionSync(item, out response);
            }
            else
            {
                success = SendProjectXWork(item, out response);
            }

            if (item.OnComplete != null)
            {
                try
                {
                    item.OnComplete(success, response);
                }
                catch (Exception ex)
                {
                    try { ProjectXLog("ProjectX work item OnComplete failed | error=" + ex.Message); }
                    catch { }
                }
            }
        }

        // Strategy-thread only. Captures everything the worker will need about the current
        // instrument as plain values, so no worker-side code ever has to touch Instrument.
        private void CaptureProjectXInstrumentSnapshot(out string root, out string key,
            out DateTime expiry, out bool hasExpiry)
        {
            root = GetProjectXInstrumentRoot();
            key = GetProjectXInstrumentKey();
            hasExpiry = TryGetInstrumentExpiry(out expiry) || TryParseInstrumentExpiryFromFullName(out expiry);
        }

        private bool IsProjectXConfigured()
        {
            return !string.IsNullOrWhiteSpace(ProjectXApiBaseUrl)
                && !string.IsNullOrWhiteSpace(ProjectXUsername)
                && !string.IsNullOrWhiteSpace(ProjectXApiKey)
                && (ProjectXTradeAllAccounts || !string.IsNullOrWhiteSpace(ProjectXAccountId));
        }

        // Prepares and enqueues the ProjectX entry mirror; does not block. Moved to run AFTER
        // EnterLongLimit/EnterShortLimit in TrySubmitQueuedEntry (EMAL-1041) - previously ran
        // before it, which is exactly the delay this whole change exists to remove, but the
        // enqueue itself is cheap either way now; kept post-submission to match the diagnostics
        // ordering already established in EMAL-1038 for the same reason.
        private void SendPlannedProjectXEntry(int direction, double plannedEntryPrice,
            double takeProfitPoints, double stopLossPoints)
        {
            if (State != State.Realtime || !IsProjectXConfigured())
                return;

            double entry = Instrument.MasterInstrument.RoundToTickSize(plannedEntryPrice);
            double anchor = entry;
            double target = Instrument.MasterInstrument.RoundToTickSize(
                direction > 0 ? anchor + takeProfitPoints : anchor - takeProfitPoints);
            double stop = Instrument.MasterInstrument.RoundToTickSize(
                direction > 0 ? anchor - stopLossPoints : anchor + stopLossPoints);

            string instrumentRoot, instrumentKey;
            DateTime instrumentExpiry;
            bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey,
                out instrumentExpiry, out hasInstrumentExpiry);

            bool hadUnresolvedMirror;
            lock (projectXStateLock)
                hadUnresolvedMirror = projectXEntryMirrorActive;

            var item = new ProjectXWorkItem
            {
                EventType = direction > 0 ? "buy" : "sell",
                EntryPrice = entry,
                TakeProfit = target,
                StopLoss = stop,
                IsMarketEntry = false,   // Entry Order Type is fixed to Limit (2026-08-06), never Market
                Quantity = Contracts,
                InstrumentRoot = instrumentRoot,
                InstrumentKey = instrumentKey,
                InstrumentExpiry = instrumentExpiry,
                HasInstrumentExpiry = hasInstrumentExpiry,
                TickSizeSnapshot = TickSize,
                OnComplete = (sent, response) =>
                {
                    lock (projectXStateLock)
                    {
                        if (sent)
                        {
                            projectXEntryMirrorActive = true;
                            suppressProjectXNextExecutionExit = false;
                            projectXLastSyncedStopPrice = stop;
                            projectXLastSyncedTargetPrice = target;
                            projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                            projectXOrphanRecoveryCount = 0;
                        }
                        else if (hadUnresolvedMirror)
                        {
                            projectXEntryMirrorActive = true;
                            projectXOrphanRecoveryCount++;
                            projectXOrphanRecoveryDueUtc = DateTime.UtcNow.AddSeconds(5);
                        }
                    }
                }
            };

            EnqueueProjectXWork(item);
        }

        // synchronousDirect=true is for State.Terminated callers only (see StopProjectXWorker) -
        // the worker has already been joined by then, so calling straight into
        // ExecuteProjectXWorkItem here is safe and deliberately bypasses the queue.
        private void CancelProjectXEntryMirror(bool flattenIfOrphaned, bool synchronousDirect = false)
        {
            bool active;
            lock (projectXStateLock)
                active = projectXEntryMirrorActive;
            if (!active)
                return;

            if (!flattenIfOrphaned)
            {
                // Original behavior: this "cancel" path never branched on the send result, and
                // always cleared the synced prices immediately - neither depends on the HTTP
                // outcome, so both can happen right here regardless of sync/async.
                lock (projectXStateLock)
                {
                    projectXLastSyncedStopPrice = 0.0;
                    projectXLastSyncedTargetPrice = 0.0;
                }
                DispatchProjectXSimpleEvent("cancel", 0, synchronousDirect, null);
                return;
            }

            DispatchProjectXSimpleEvent("exit", 0, synchronousDirect, (sent, response) =>
            {
                lock (projectXStateLock)
                {
                    if (sent)
                    {
                        projectXEntryMirrorActive = false;
                        projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                        projectXOrphanRecoveryCount = 0;
                        projectXLastSyncedStopPrice = 0.0;
                        projectXLastSyncedTargetPrice = 0.0;
                    }
                    else
                    {
                        projectXOrphanRecoveryCount++;
                        projectXOrphanRecoveryDueUtc = DateTime.UtcNow.AddSeconds(
                            Math.Min(60, 5 * projectXOrphanRecoveryCount));
                        ProjectXLog(string.Format(
                            "ProjectX orphan flatten retry scheduled | attempt={0} due={1:HH:mm:ss} UTC",
                            projectXOrphanRecoveryCount, projectXOrphanRecoveryDueUtc));
                    }
                }
            });
        }

        // Shared helper for the "exit"/"cancel" (no entry payload) ProjectX events: builds the
        // work item, captures the instrument snapshot, and either enqueues it (normal trading
        // path) or runs it immediately on the calling thread (termination path only - see
        // synchronousDirect callers). quantityOverride 0 means "use Contracts", same default
        // SendWebhook always used.
        private void DispatchProjectXSimpleEvent(string eventType, int quantityOverride,
            bool synchronousDirect, Action<bool, string> onComplete)
        {
            string instrumentRoot, instrumentKey;
            DateTime instrumentExpiry;
            bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey,
                out instrumentExpiry, out hasInstrumentExpiry);

            var item = new ProjectXWorkItem
            {
                EventType = eventType,
                Quantity = quantityOverride > 0 ? quantityOverride : Contracts,
                IsMarketEntry = false,   // unused for "exit"/"cancel" downstream; matches SendWebhook's own default
                InstrumentRoot = instrumentRoot,
                InstrumentKey = instrumentKey,
                InstrumentExpiry = instrumentExpiry,
                HasInstrumentExpiry = hasInstrumentExpiry,
                TickSizeSnapshot = TickSize,
                OnComplete = onComplete
            };

            if (synchronousDirect)
                ExecuteProjectXWorkItem(item);
            else
                EnqueueProjectXWork(item);
        }

        private void EvaluateProjectXOrphanRecovery()
        {
            bool active;
            DateTime dueUtc;
            lock (projectXStateLock)
            {
                active = projectXEntryMirrorActive;
                dueUtc = projectXOrphanRecoveryDueUtc;
            }

            if (!active
                || Position.MarketPosition != MarketPosition.Flat
                || IsOrderActive(entryOrder)
                || dueUtc == DateTime.MinValue
                || DateTime.UtcNow < dueUtc)
            {
                return;
            }

            DispatchProjectXSimpleEvent("exit", 0, false, (sent, response) =>
            {
                lock (projectXStateLock)
                {
                    if (sent)
                    {
                        projectXEntryMirrorActive = false;
                        projectXLastSyncedStopPrice = 0.0;
                        projectXLastSyncedTargetPrice = 0.0;
                        projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                        projectXOrphanRecoveryCount = 0;
                        ProjectXLog("ProjectX orphan flatten recovery succeeded");
                    }
                    else
                    {
                        projectXOrphanRecoveryCount++;
                        projectXOrphanRecoveryDueUtc = DateTime.UtcNow.AddSeconds(
                            Math.Min(60, 5 * projectXOrphanRecoveryCount));
                    }
                }
            });
        }

        private void SendExplicitProjectXExit(string reason)
        {
            bool active;
            lock (projectXStateLock)
                active = projectXEntryMirrorActive;
            if (!active)
                return;

            int quantitySnapshot = Math.Abs(Position.Quantity);
            DispatchProjectXSimpleEvent("exit", quantitySnapshot, false, (sent, response) =>
            {
                if (!sent)
                    return;
                lock (projectXStateLock)
                    suppressProjectXNextExecutionExit = true;
                // Time[0]/Close[0] are not valid off the strategy thread - unlike ProjectXLog
                // (which already avoids Time[0]), this Print previously used it as a fallback;
                // dropped here since this callback can run on the worker thread.
                try
                {
                    Print(string.Format("{0} | ProjectX explicit exit sent | reason={1}",
                        lastTickTime != DateTime.MinValue ? lastTickTime : DateTime.Now, reason));
                }
                catch { }
            });
        }

        // EMAL-1041: every trading-path ProjectX call site now builds a ProjectXWorkItem itself
        // and either enqueues it or (termination only) executes it directly - see
        // DispatchProjectXSimpleEvent/CancelProjectXEntryMirror's synchronousDirect parameter.
        // The only remaining caller of this method is FlattenProjectXOrphanOnTermination, which
        // is termination-only by design (runs on the strategy thread, after the worker has
        // already been stopped/drained), so the body below still executes directly and
        // synchronously - never through the queue - deliberately, not as an oversight.
        private bool SendWebhook(string eventType, double entryPrice = 0.0, double takeProfit = 0.0,
            double stopLoss = 0.0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime && State != State.Terminated)
                return false;

            if (!IsProjectXConfigured())
                return false;

            int quantity = quantityOverride > 0 ? quantityOverride : Math.Max(1, Contracts);
            string instrumentRoot, instrumentKey;
            DateTime instrumentExpiry;
            bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey,
                out instrumentExpiry, out hasInstrumentExpiry);

            var directItem = new ProjectXWorkItem
            {
                EventType = eventType,
                EntryPrice = entryPrice,
                TakeProfit = takeProfit,
                StopLoss = stopLoss,
                IsMarketEntry = isMarketEntry,
                Quantity = quantity,
                InstrumentRoot = instrumentRoot,
                InstrumentKey = instrumentKey,
                InstrumentExpiry = instrumentExpiry,
                HasInstrumentExpiry = hasInstrumentExpiry,
                TickSizeSnapshot = TickSize
            };
            string projectXResponse;
            return SendProjectXWork(directItem, out projectXResponse);
        }

        // Worker-thread body for an entry/exit/cancel item. Renamed from SendProjectX (EMAL-1041)
        // to make clear this now only ever runs on the worker (or, during termination, directly
        // on the strategy thread after the worker has been joined - see StopProjectXWorker).
        // Touches only item.* and the worker-only session/account/contract cache.
        private bool SendProjectXWork(ProjectXWorkItem item, out string response)
        {
            response = null;
            if (!IsProjectXConfigured())
                return false;

            if (!EnsureProjectXSession())
                return false;

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetProjectXTargets(item.InstrumentRoot, item.InstrumentKey, item.InstrumentExpiry,
                item.HasInstrumentExpiry, out targetAccounts, out contractId))
                return false;

            string eventType = item.EventType;
            bool sentAny = false;
            foreach (ProjectXAccountInfo account in targetAccounts)
            {
                try
                {
                    switch ((eventType ?? string.Empty).ToLowerInvariant())
                    {
                        case "buy":
                        case "sell":
                            if (ProjectXPrepareForEntry(account.Id, contractId)
                                && ProjectXPlaceOrder(eventType, account.Id, contractId, item.EntryPrice,
                                    item.TakeProfit, item.StopLoss, item.IsMarketEntry, item.Quantity,
                                    item.TickSizeSnapshot))
                            {
                                sentAny = true;
                            }
                            break;

                        case "exit":
                            if (ProjectXFlattenPosition(account.Id, contractId))
                                sentAny = true;
                            break;

                        case "cancel":
                            ProjectXCancelEntryOrder(account.Id, contractId);
                            sentAny = true;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    ProjectXLog(string.Format(
                        "ProjectX account error | event={0} accountId={1} name={2} error={3}",
                        eventType, account.Id, account.Name ?? string.Empty, ex.Message));
                }
            }

            return sentAny;
        }

        // Strategy-thread only, called once from State.Realtime, BEFORE the worker thread is
        // started (see OnStateChange) - this is what makes it safe for the session/account/
        // contract cache fields below to need no lock: this call and the worker's later calls
        // never overlap.
        private void RunProjectXStartupPreflight()
        {
            if (!IsProjectXConfigured())
            {
                ProjectXLog("ProjectX inactive | configure Username, API and Accounts to enable");
                return;
            }

            string instrumentRoot, instrumentKey;
            DateTime instrumentExpiry;
            bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey,
                out instrumentExpiry, out hasInstrumentExpiry);

            ProjectXLog(string.Format(
                "ProjectX startup preflight begin | instrument={0} selectors={1}",
                instrumentKey, ProjectXAccountId ?? string.Empty));

            if (!EnsureProjectXSession())
            {
                ProjectXLog("ProjectX startup preflight failed | stage=auth");
                return;
            }

            List<ProjectXAccountInfo> targets;
            string contractId;
            if (!TryGetProjectXTargets(instrumentRoot, instrumentKey, instrumentExpiry, hasInstrumentExpiry,
                out targets, out contractId))
            {
                ProjectXLog("ProjectX startup preflight failed | stage=targets");
                return;
            }

            ProjectXLog(string.Format(
                "ProjectX startup preflight ready | accounts={0} contractId={1}",
                FormatProjectXAccountsForLog(targets), contractId));
        }

        private bool EnsureProjectXSession()
        {
            if (!IsProjectXConfigured())
                return false;

            if (!string.IsNullOrWhiteSpace(projectXSessionToken)
                && (DateTime.UtcNow - projectXTokenAcquiredUtc).TotalHours < 23.0)
            {
                return true;
            }

            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"userName\":\"{0}\",\"apiKey\":\"{1}\"}}",
                JsonEscape(ProjectXUsername), JsonEscape(ProjectXApiKey));
            string response = ProjectXPost("/api/Auth/loginKey", json, false, true);
            string token;
            if (!TryGetJsonString(response, "token", out token))
            {
                ProjectXLog("ProjectX login failed | token missing");
                return false;
            }

            projectXSessionToken = token;
            projectXTokenAcquiredUtc = DateTime.UtcNow;
            projectXAccounts = null;
            projectXResolvedContractId = string.Empty;
            projectXResolvedInstrumentKey = string.Empty;
            projectXLastOrderIds.Clear();
            ProjectXLog("ProjectX login succeeded");
            return true;
        }

        private bool TryGetProjectXTargets(string instrumentRoot, string instrumentKey,
            DateTime instrumentExpiry, bool hasInstrumentExpiry,
            out List<ProjectXAccountInfo> targetAccounts, out string contractId)
        {
            targetAccounts = null;
            contractId = null;
            if (!TryResolveProjectXContractId(instrumentRoot, instrumentKey, instrumentExpiry,
                hasInstrumentExpiry, out contractId))
                return false;

            List<ProjectXAccountInfo> accounts;
            if (!TryLoadProjectXAccounts(out accounts))
                return false;

            if (ProjectXTradeAllAccounts)
            {
                targetAccounts = accounts.Where(a => a.CanTrade).ToList();
                return targetAccounts.Count > 0;
            }

            List<string> selectors = ParseProjectXAccountSelectors(ProjectXAccountId);
            if (selectors.Count == 0)
            {
                ProjectXLog("ProjectX target selection failed | ProjectX Accounts is empty");
                return false;
            }

            var matched = new List<ProjectXAccountInfo>();
            var matchedIds = new HashSet<int>();
            foreach (string selector in selectors)
            {
                int id;
                IEnumerable<ProjectXAccountInfo> candidates = int.TryParse(
                    selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
                    ? accounts.Where(a => a.CanTrade && a.Id == id)
                    : accounts.Where(a => a.CanTrade
                        && string.Equals(a.Name ?? string.Empty, selector, StringComparison.OrdinalIgnoreCase));

                foreach (ProjectXAccountInfo account in candidates)
                {
                    if (matchedIds.Add(account.Id))
                        matched.Add(account);
                }
            }

            targetAccounts = matched;
            if (targetAccounts.Count == 0)
                ProjectXLog("ProjectX target selection failed | no matching tradable accounts");
            return targetAccounts.Count > 0;
        }

        private bool TryLoadProjectXAccounts(out List<ProjectXAccountInfo> accounts)
        {
            if (projectXAccounts != null && projectXAccounts.Count > 0)
            {
                accounts = projectXAccounts;
                return true;
            }

            string response = ProjectXPost("/api/Account/search", "{\"onlyActiveAccounts\":true}", true, true);
            accounts = ExtractProjectXAccounts(response).ToList();
            projectXAccounts = accounts.Count > 0 ? accounts : null;
            ProjectXLog(string.Format("ProjectX accounts found | count={0}", accounts.Count));
            return accounts.Count > 0;
        }

        // instrumentRoot/instrumentKey/instrumentExpiry are captured on the strategy thread
        // (CaptureProjectXInstrumentSnapshot) before this is ever called - no Instrument access
        // here, so this is safe from the worker thread.
        private bool TryResolveProjectXContractId(string instrumentRoot, string instrumentKey,
            DateTime instrumentExpiry, bool hasInstrumentExpiry, out string contractId)
        {
            contractId = null;
            if (!string.IsNullOrWhiteSpace(ProjectXContractId))
            {
                contractId = ProjectXContractId.Trim();
                return true;
            }

            if (!string.IsNullOrWhiteSpace(projectXResolvedContractId)
                && string.Equals(projectXResolvedInstrumentKey, instrumentKey, StringComparison.OrdinalIgnoreCase))
            {
                contractId = projectXResolvedContractId;
                return true;
            }

            if (string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            string suffix = hasInstrumentExpiry
                ? GetProjectXFuturesMonthCode(instrumentExpiry.Month)
                    + instrumentExpiry.ToString("yy", CultureInfo.InvariantCulture)
                : string.Empty;

            List<ProjectXContractInfo> contracts;
            if (!TrySearchProjectXContracts(instrumentRoot, suffix, out contracts))
                return false;

            ProjectXContractInfo selected = SelectProjectXContract(suffix, contracts);
            if (selected == null || string.IsNullOrWhiteSpace(selected.Id))
                return false;

            contractId = selected.Id;
            projectXResolvedContractId = contractId;
            projectXResolvedInstrumentKey = instrumentKey;
            ProjectXLog(string.Format("ProjectX contract resolved | instrument={0} contractId={1}",
                instrumentKey, contractId));
            return true;
        }

        private bool TrySearchProjectXContracts(string root, string suffix, out List<ProjectXContractInfo> contracts)
        {
            string primary = string.IsNullOrWhiteSpace(suffix) ? root : root + suffix;
            if (TrySearchProjectXContractsByText(primary, root, true, out contracts) && contracts.Count > 0)
                return true;
            if (TrySearchProjectXContractsByText(primary, root, false, out contracts) && contracts.Count > 0)
                return true;
            if (!string.Equals(primary, root, StringComparison.OrdinalIgnoreCase))
            {
                if (TrySearchProjectXContractsByText(root, root, true, out contracts) && contracts.Count > 0)
                    return true;
                if (TrySearchProjectXContractsByText(root, root, false, out contracts) && contracts.Count > 0)
                    return true;
            }
            contracts = new List<ProjectXContractInfo>();
            return false;
        }

        private bool TrySearchProjectXContractsByText(string searchText, string root, bool live,
            out List<ProjectXContractInfo> contracts)
        {
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"live\":{0},\"searchText\":\"{1}\"}}",
                live ? "true" : "false", JsonEscape(searchText));
            string response = ProjectXPost("/api/Contract/search", json, true, true);
            contracts = ExtractProjectXContracts(response)
                .Where(c => DoesProjectXContractMatchRoot(c, root))
                .ToList();
            return !string.IsNullOrWhiteSpace(response);
        }

        private ProjectXContractInfo SelectProjectXContract(string suffix, List<ProjectXContractInfo> contracts)
        {
            if (contracts == null || contracts.Count == 0)
                return null;
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                List<ProjectXContractInfo> exact = contracts
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id)
                        && c.Id.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (exact.Count > 0)
                    return exact.FirstOrDefault(c => c.ActiveContract) ?? exact[0];
            }
            return contracts.FirstOrDefault(c => c.ActiveContract) ?? contracts[0];
        }

        private bool DoesProjectXContractMatchRoot(ProjectXContractInfo contract, string root)
        {
            if (contract == null || string.IsNullOrWhiteSpace(root))
                return false;
            return (!string.IsNullOrWhiteSpace(contract.SymbolId)
                    && string.Equals(contract.SymbolId, "F.US." + root, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(contract.Id)
                    && contract.Id.IndexOf(".US." + root + ".", StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrWhiteSpace(contract.Name)
                    && contract.Name.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        private string GetProjectXInstrumentKey()
        {
            return Instrument != null && !string.IsNullOrWhiteSpace(Instrument.FullName)
                ? Instrument.FullName.Trim().ToUpperInvariant()
                : GetProjectXInstrumentRoot();
        }

        private string GetProjectXInstrumentRoot()
        {
            return Instrument != null && Instrument.MasterInstrument != null
                ? (Instrument.MasterInstrument.Name ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
        }

        private bool TryGetInstrumentExpiry(out DateTime expiry)
        {
            expiry = Core.Globals.MinDate;
            if (Instrument == null)
                return false;
            try
            {
                PropertyInfo property = Instrument.GetType().GetProperty(
                    "Expiry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                object raw = property != null ? property.GetValue(Instrument, null) : null;
                if (!(raw is DateTime) || ((DateTime)raw).Year < 2000)
                    return false;
                expiry = (DateTime)raw;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseInstrumentExpiryFromFullName(out DateTime expiry)
        {
            expiry = Core.Globals.MinDate;
            string fullName = Instrument != null ? (Instrument.FullName ?? string.Empty) : string.Empty;
            Match match = Regex.Match(fullName, @"\b(?<month>\d{1,2})[-/](?<year>\d{2,4})\b");
            int month;
            int year;
            if (!match.Success
                || !int.TryParse(match.Groups["month"].Value, out month)
                || !int.TryParse(match.Groups["year"].Value, out year))
                return false;
            if (year < 100)
                year += 2000;
            if (month < 1 || month > 12 || year < 2000)
                return false;
            expiry = new DateTime(year, month, 1);
            return true;
        }

        private string GetProjectXFuturesMonthCode(int month)
        {
            const string codes = " FGHJKMNQUVXZ";
            return month >= 1 && month <= 12 ? codes[month].ToString() : string.Empty;
        }

        private List<string> ParseProjectXAccountSelectors(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string FormatProjectXAccountsForLog(IEnumerable<ProjectXAccountInfo> accounts)
        {
            return accounts == null ? "<none>" : string.Join(", ", accounts.Select(a =>
                string.Format(CultureInfo.InvariantCulture, "{0}:{1}", a.Id, a.Name ?? string.Empty)).ToArray());
        }

        // Strategy-thread only: all the cheap gating/dedup checks stay here (no HTTP, so no
        // reason to touch the queue at all if nothing would actually change), then captures
        // everything the worker needs and enqueues. The actual HTTP work happens in
        // ExecuteProjectXProtectionSync, entirely on the worker thread. This is the call site
        // v1037/1038's gap-latch work exists to keep clear of the fill callback - it runs from
        // inside OnExecutionUpdate, and used to block on HTTP right there.
        private void SyncProjectXProtectionUpdate(ProjectXProtectionOrderKind kind, double price, string reason)
        {
            bool mirrorActive;
            lock (projectXStateLock)
                mirrorActive = projectXEntryMirrorActive;

            if (State != State.Realtime
                || !IsProjectXConfigured()
                || !mirrorActive
                || Position.MarketPosition == MarketPosition.Flat)
            {
                return;
            }

            price = Instrument.MasterInstrument.RoundToTickSize(price);
            if (price <= 0.0 || double.IsNaN(price) || double.IsInfinity(price))
                return;

            double lastPrice;
            lock (projectXStateLock)
            {
                lastPrice = kind == ProjectXProtectionOrderKind.StopLoss
                    ? projectXLastSyncedStopPrice
                    : projectXLastSyncedTargetPrice;
            }
            if (lastPrice > 0.0 && Math.Abs(lastPrice - price) < TickSize / 2.0)
                return;

            string instrumentRoot, instrumentKey;
            DateTime instrumentExpiry;
            bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey,
                out instrumentExpiry, out hasInstrumentExpiry);

            var item = new ProjectXWorkItem
            {
                ProtectionKind = kind,
                ProtectionPrice = price,
                ProtectionReason = reason ?? string.Empty,
                ProtectionExpectedSide = Position.MarketPosition == MarketPosition.Long ? 1 : 0,
                ProtectionFallbackSize = Math.Max(1, Math.Abs(Position.Quantity)),
                InstrumentRoot = instrumentRoot,
                InstrumentKey = instrumentKey,
                InstrumentExpiry = instrumentExpiry,
                HasInstrumentExpiry = hasInstrumentExpiry,
                TickSizeSnapshot = TickSize,
                OnComplete = (modifiedAny, response) =>
                {
                    if (!modifiedAny)
                        return;
                    lock (projectXStateLock)
                    {
                        if (kind == ProjectXProtectionOrderKind.StopLoss)
                            projectXLastSyncedStopPrice = price;
                        else
                            projectXLastSyncedTargetPrice = price;
                    }
                }
            };

            EnqueueProjectXWork(item);
        }

        // Worker-thread body for a protection-sync item. Touches only item.* and the
        // worker-only session/account/contract cache - no NT object, no NT method.
        private bool ExecuteProjectXProtectionSync(ProjectXWorkItem item, out string response)
        {
            response = null;
            if (!EnsureProjectXSession())
                return false;

            List<ProjectXAccountInfo> targets;
            string contractId;
            if (!TryGetProjectXTargets(item.InstrumentRoot, item.InstrumentKey, item.InstrumentExpiry,
                item.HasInstrumentExpiry, out targets, out contractId))
                return false;

            ProjectXProtectionOrderKind kind = item.ProtectionKind.Value;
            bool modifiedAny = false;
            foreach (ProjectXAccountInfo account in targets)
            {
                Dictionary<string, object> order = SelectProjectXProtectionOrder(
                    account.Id, contractId, kind, item.ProtectionExpectedSide);
                if (order == null)
                {
                    ProjectXLog(string.Format(
                        "ProjectX protection sync skipped | account={0} kind={1} reason=no-unique-open-order",
                        account.Id, kind));
                    continue;
                }

                long orderId;
                int size;
                if (!TryGetProjectXOrderLong(order, "id", out orderId) || orderId <= 0)
                    continue;
                if (!TryGetProjectXOrderInt(order, "size", out size) || size <= 0)
                    size = item.ProtectionFallbackSize;

                response = ProjectXModifyProtectionOrder(account.Id, orderId, size, kind, item.ProtectionPrice);
                bool success;
                if (!TryGetJsonBool(response, "success", out success) || success)
                    modifiedAny = true;
                else
                    ProjectXLog(string.Format(
                        "ProjectX protection sync failed | account={0} order={1} kind={2} price={3:0.00} reason={4}",
                        account.Id, orderId, kind, item.ProtectionPrice, item.ProtectionReason));
            }

            return modifiedAny;
        }

        private Dictionary<string, object> SelectProjectXProtectionOrder(int accountId, string contractId,
            ProjectXProtectionOrderKind kind, int expectedSide)
        {
            List<Dictionary<string, object>> matches = GetProjectXOpenOrders(accountId, contractId)
                .Where(o => IsProjectXProtectionOrderMatch(o, kind, expectedSide))
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private bool IsProjectXProtectionOrderMatch(Dictionary<string, object> order,
            ProjectXProtectionOrderKind kind, int expectedSide)
        {
            int side;
            if (TryGetProjectXOrderInt(order, "side", out side) && side != expectedSide)
                return false;

            int type;
            if (TryGetProjectXOrderInt(order, "type", out type))
                return kind == ProjectXProtectionOrderKind.StopLoss ? type == 4 : type == 1;

            double price;
            return kind == ProjectXProtectionOrderKind.StopLoss
                ? TryGetProjectXOrderDouble(order, "stopPrice", out price) && price > 0.0
                : TryGetProjectXOrderDouble(order, "limitPrice", out price) && price > 0.0;
        }

        private string ProjectXModifyProtectionOrder(int accountId, long orderId, int size,
            ProjectXProtectionOrderKind kind, double price)
        {
            string limit = kind == ProjectXProtectionOrderKind.TakeProfit ? FormatProjectXPriceRaw(price) : "null";
            string stop = kind == ProjectXProtectionOrderKind.StopLoss ? FormatProjectXPriceRaw(price) : "null";
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"orderId\":{1},\"size\":{2},\"limitPrice\":{3},\"stopPrice\":{4},\"trailPrice\":null}}",
                accountId, orderId, Math.Max(1, size), limit, stop);
            return ProjectXPost("/api/Order/modify", json, true);
        }

        // entryPrice/takeProfit/stopLoss arrive already tick-rounded from SendPlannedProjectXEntry
        // (strategy thread, before enqueue) - no Instrument access needed or wanted here.
        // tickSize is the enqueue-time TickSize snapshot (ProjectXWorkItem.TickSizeSnapshot).
        private bool ProjectXPlaceOrder(string side, int accountId, string contractId,
            double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity,
            double tickSize)
        {
            int orderSide = string.Equals(side, "buy", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int orderType = isMarketEntry ? 2 : 1;
            int normalizedQuantity = Math.Max(1, quantity);
            double entry = entryPrice;
            bool isLong = orderSide == 0;
            int tpTicks = NormalizeProjectXBracketTicks(
                PriceToTicks(takeProfit - entry, tickSize), 4, isLong ? 1 : -1);
            int slTicks = NormalizeProjectXBracketTicks(
                PriceToTicks(stopLoss - entry, tickSize), 1, isLong ? -1 : 1);
            string limitPart = isMarketEntry
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, ",\"limitPrice\":{0}", FormatProjectXPriceRaw(entry));
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\",\"type\":{2},\"side\":{3},\"size\":{4}{5},\"takeProfitBracket\":{{\"quantity\":{6},\"type\":1,\"ticks\":{7}}},\"stopLossBracket\":{{\"quantity\":{6},\"type\":4,\"ticks\":{8}}}}}",
                accountId, JsonEscape(contractId), orderType, orderSide, normalizedQuantity,
                limitPart, normalizedQuantity, tpTicks, slTicks);
            string response = ProjectXPost("/api/Order/place", json, true);
            bool success;
            if (TryGetJsonBool(response, "success", out success) && !success)
                return false;
            long orderId;
            if (TryGetJsonLong(response, "orderId", out orderId) && orderId > 0)
                projectXLastOrderIds[GetProjectXOrderKey(accountId, contractId)] = orderId;
            return !string.IsNullOrWhiteSpace(response);
        }

        private int NormalizeProjectXBracketTicks(int rawTicks, int minAbsoluteTicks, int zeroDirection)
        {
            int direction = rawTicks == 0 ? Math.Sign(zeroDirection) : Math.Sign(rawTicks);
            return direction * Math.Max(minAbsoluteTicks, Math.Abs(rawTicks));
        }

        private bool ProjectXPrepareForEntry(int accountId, string contractId)
        {
            ProjectXCancelOrders(accountId, contractId);
            if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
                return false;

            int positionSize;
            if (!TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize))
            {
                ProjectXLog(string.Format(
                    "ProjectX prepare failed | account={0} reason=position-query-failed", accountId));
                return false;
            }

            if (positionSize != 0)
            {
                ProjectXClosePosition(accountId, contractId);
                if (!WaitForProjectXFlat(accountId, contractId, 4000))
                    return false;
                ProjectXCancelOrders(accountId, contractId);
                if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
                    return false;
            }
            return true;
        }

        private bool ProjectXFlattenPosition(int accountId, string contractId)
        {
            ProjectXCancelOrders(accountId, contractId);
            if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
                ProjectXLog(string.Format("ProjectX flatten warning | account={0} orders-not-cleared", accountId));

            int positionSize;
            if (!TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize))
            {
                ProjectXLog(string.Format(
                    "ProjectX flatten failed | account={0} reason=position-query-failed", accountId));
                return false;
            }

            if (positionSize != 0)
            {
                ProjectXClosePosition(accountId, contractId);
                if (!WaitForProjectXFlat(accountId, contractId, 4000))
                {
                    ProjectXLog(string.Format("ProjectX flatten warning | account={0} position={1}",
                        accountId, positionSize));
                    return false;
                }
            }

            ProjectXCancelOrders(accountId, contractId);
            return WaitForProjectXOrdersCleared(accountId, contractId, 4000);
        }

        private string ProjectXClosePosition(int accountId, string contractId)
        {
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\"}}",
                accountId, JsonEscape(contractId));
            return ProjectXPost("/api/Position/closeContract", json, true);
        }

        private void ProjectXCancelOrders(int accountId, string contractId)
        {
            foreach (long orderId in GetProjectXOpenOrderIds(accountId, contractId))
            {
                string json = string.Format(CultureInfo.InvariantCulture,
                    "{{\"accountId\":{0},\"orderId\":{1}}}", accountId, orderId);
                ProjectXPost("/api/Order/cancel", json, true);
            }
            projectXLastOrderIds.Remove(GetProjectXOrderKey(accountId, contractId));
        }

        private void ProjectXCancelEntryOrder(int accountId, string contractId)
        {
            string key = GetProjectXOrderKey(accountId, contractId);
            long orderId;
            if (!projectXLastOrderIds.TryGetValue(key, out orderId) || orderId <= 0)
                return;

            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"orderId\":{1}}}", accountId, orderId);
            ProjectXPost("/api/Order/cancel", json, true);
            projectXLastOrderIds.Remove(key);
        }

        private bool WaitForProjectXFlat(int accountId, string contractId, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            do
            {
                int positionSize;
                if (TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize) && positionSize == 0)
                    return true;
                System.Threading.Thread.Sleep(150);
            }
            while (DateTime.UtcNow <= deadline);
            return false;
        }

        private bool WaitForProjectXOrdersCleared(int accountId, string contractId, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            do
            {
                if (GetProjectXOpenOrderIds(accountId, contractId).Count == 0)
                    return true;
                System.Threading.Thread.Sleep(150);
            }
            while (DateTime.UtcNow <= deadline);
            return false;
        }

        private List<long> GetProjectXOpenOrderIds(int accountId, string contractId)
        {
            var ids = new List<long>();
            foreach (Dictionary<string, object> order in GetProjectXOpenOrders(accountId, contractId))
            {
                long id;
                if (TryGetProjectXOrderLong(order, "id", out id) && id > 0)
                    ids.Add(id);
            }
            return ids;
        }

        private List<Dictionary<string, object>> GetProjectXOpenOrders(int accountId, string contractId)
        {
            string json = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string response = ProjectXPost("/api/Order/searchOpen", json, true);
            return ExtractProjectXCollection(response, "orders")
                .Where(o => ProjectXDictionaryValueEquals(o, "contractId", contractId))
                .ToList();
        }

        private bool TryGetProjectXOpenPositionSize(int accountId, string contractId, out int signedSize)
        {
            signedSize = 0;
            string json = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string response = ProjectXPost("/api/Position/searchOpen", json, true);
            bool success;
            if (TryGetJsonBool(response, "success", out success) && !success)
                return false;

            foreach (Dictionary<string, object> position in ExtractProjectXCollection(response, "positions"))
            {
                if (!ProjectXDictionaryValueEquals(position, "contractId", contractId))
                    continue;
                int type;
                int size;
                object rawType;
                object rawSize;
                if (!position.TryGetValue("type", out rawType) || !TryConvertToInt(rawType, out type)
                    || !position.TryGetValue("size", out rawSize) || !TryConvertToInt(rawSize, out size))
                    continue;
                signedSize += type == 2 ? -Math.Abs(size) : Math.Abs(size);
            }
            return true;
        }

        private bool ProjectXDictionaryValueEquals(Dictionary<string, object> data, string key, string expected)
        {
            object raw;
            return data != null && data.TryGetValue(key, out raw)
                && string.Equals(raw != null ? raw.ToString() : string.Empty,
                    expected ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private string GetProjectXOrderKey(int accountId, string contractId)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}", accountId, contractId ?? string.Empty);
        }

        // Worker-safe: every ProjectX caller of this (ProjectXPlaceOrder, ProjectXModifyProtectionOrder)
        // already receives a price rounded on the strategy thread before enqueue - no Instrument
        // access needed or wanted here.
        private string FormatProjectXPriceRaw(double alreadyRoundedPrice)
        {
            return alreadyRoundedPrice.ToString("0.########", CultureInfo.InvariantCulture);
        }

        // tickSize is captured on the strategy thread at enqueue time (ProjectXWorkItem.TickSizeSnapshot)
        // rather than read from the NT TickSize property here, so this is safe on the worker thread.
        private int PriceToTicks(double distance, double tickSize)
        {
            return tickSize > 0.0
                ? (int)Math.Round(distance / tickSize, MidpointRounding.AwayFromZero)
                : 0;
        }

        private string ProjectXPost(string path, string json, bool requiresAuthentication)
        {
            return ProjectXPost(path, json, requiresAuthentication, false);
        }

        private string ProjectXPost(string path, string json, bool requiresAuthentication, bool alwaysLog)
        {
            string baseUrl = (ProjectXApiBaseUrl ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            try
            {
                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    if (requiresAuthentication && !string.IsNullOrWhiteSpace(projectXSessionToken))
                        client.Headers[System.Net.HttpRequestHeader.Authorization] = "Bearer " + projectXSessionToken;
                    string response = client.UploadString(baseUrl + path, "POST", json);
                    if (alwaysLog)
                        ProjectXLog(string.Format("ProjectX response | path={0} body={1}",
                            path, SanitizeProjectXJsonForLog(response)));
                    return response;
                }
            }
            catch (System.Net.WebException ex)
            {
                string body = ReadWebExceptionResponse(ex);
                ProjectXLog(string.Format("ProjectX request failed | path={0} error={1} body={2}",
                    path, ex.Message, SanitizeProjectXJsonForLog(body)));
                return body;
            }
            catch (Exception ex)
            {
                ProjectXLog(string.Format("ProjectX request failed | path={0} error={1}", path, ex.Message));
                return null;
            }
        }

        private string ReadWebExceptionResponse(System.Net.WebException exception)
        {
            try
            {
                if (exception == null || exception.Response == null)
                    return null;
                using (Stream stream = exception.Response.GetResponseStream())
                using (var reader = stream != null ? new StreamReader(stream) : null)
                    return reader != null ? reader.ReadToEnd() : null;
            }
            catch
            {
                return null;
            }
        }

        private void ProjectXLog(string message)
        {
            Print(string.Format("{0} | EMAL | {1}",
                lastTickTime != DateTime.MinValue ? lastTickTime : DateTime.Now,
                message ?? string.Empty));
        }

        private string JsonEscape(string value)
        {
            if (value == null)
                return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private string SanitizeProjectXJsonForLog(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;
            string sanitized = json;
            foreach (string key in new[] { "apiKey", "loginKey", "token", "newToken" })
            {
                sanitized = Regex.Replace(sanitized,
                    "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"[^\"]*\"",
                    "\"" + key + "\":\"***\"");
            }
            return sanitized;
        }

        private bool TryGetJsonString(string json, string key, out string value)
        {
            value = null;
            object raw;
            Dictionary<string, object> data;
            if (!TryDeserializeProjectXObject(json, out data)
                || !data.TryGetValue(key, out raw) || raw == null)
                return false;
            value = raw.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        private bool TryGetJsonLong(string json, string key, out long value)
        {
            value = 0;
            object raw;
            Dictionary<string, object> data;
            return TryDeserializeProjectXObject(json, out data)
                && data.TryGetValue(key, out raw)
                && TryConvertToLong(raw, out value);
        }

        private bool TryGetJsonBool(string json, string key, out bool value)
        {
            value = false;
            object raw;
            Dictionary<string, object> data;
            return TryDeserializeProjectXObject(json, out data)
                && data.TryGetValue(key, out raw)
                && TryConvertToBool(raw, out value);
        }

        private bool TryDeserializeProjectXObject(string json, out Dictionary<string, object> data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                return data != null;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<ProjectXAccountInfo> ExtractProjectXAccounts(string json)
        {
            foreach (Dictionary<string, object> item in ExtractProjectXCollection(json, "accounts"))
            {
                object rawId;
                int id;
                if (!item.TryGetValue("id", out rawId) || !TryConvertToInt(rawId, out id) || id <= 0)
                    continue;

                object rawName;
                object rawCanTrade;
                object rawVisible;
                bool canTrade;
                bool visible;
                item.TryGetValue("name", out rawName);
                item.TryGetValue("canTrade", out rawCanTrade);
                item.TryGetValue("isVisible", out rawVisible);
                TryConvertToBool(rawCanTrade, out canTrade);
                TryConvertToBool(rawVisible, out visible);

                yield return new ProjectXAccountInfo
                {
                    Id = id,
                    Name = rawName != null ? rawName.ToString() : string.Empty,
                    CanTrade = canTrade,
                    IsVisible = visible
                };
            }
        }

        private IEnumerable<ProjectXContractInfo> ExtractProjectXContracts(string json)
        {
            foreach (Dictionary<string, object> item in ExtractProjectXCollection(json, "contracts"))
            {
                object rawId;
                if (!item.TryGetValue("id", out rawId) || rawId == null)
                    continue;
                object rawName;
                object rawSymbol;
                object rawActive;
                bool active;
                item.TryGetValue("name", out rawName);
                item.TryGetValue("symbolId", out rawSymbol);
                item.TryGetValue("activeContract", out rawActive);
                TryConvertToBool(rawActive, out active);

                yield return new ProjectXContractInfo
                {
                    Id = rawId.ToString(),
                    Name = rawName != null ? rawName.ToString() : string.Empty,
                    SymbolId = rawSymbol != null ? rawSymbol.ToString() : string.Empty,
                    ActiveContract = active
                };
            }
        }

        private IEnumerable<Dictionary<string, object>> ExtractProjectXCollection(string json, string key)
        {
            Dictionary<string, object> data;
            if (!TryDeserializeProjectXObject(json, out data))
                yield break;
            object raw;
            if (!data.TryGetValue(key, out raw) || raw == null)
                yield break;
            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;
            foreach (object item in items)
            {
                var dictionary = item as Dictionary<string, object>;
                if (dictionary != null)
                    yield return dictionary;
            }
        }

        private bool TryGetProjectXOrderInt(Dictionary<string, object> order, string key, out int value)
        {
            value = 0;
            object raw;
            return order != null && order.TryGetValue(key, out raw) && TryConvertToInt(raw, out value);
        }

        private bool TryGetProjectXOrderLong(Dictionary<string, object> order, string key, out long value)
        {
            value = 0;
            object raw;
            return order != null && order.TryGetValue(key, out raw) && TryConvertToLong(raw, out value);
        }

        private bool TryGetProjectXOrderDouble(Dictionary<string, object> order, string key, out double value)
        {
            value = 0.0;
            object raw;
            return order != null && order.TryGetValue(key, out raw) && TryConvertToDouble(raw, out value);
        }

        private bool TryConvertToInt(object raw, out int value)
        {
            value = 0;
            if (raw == null)
                return false;
            if (raw is int) { value = (int)raw; return true; }
            if (raw is long && (long)raw >= int.MinValue && (long)raw <= int.MaxValue)
            { value = (int)(long)raw; return true; }
            if (raw is decimal) { value = (int)(decimal)raw; return true; }
            if (raw is double) { value = (int)(double)raw; return true; }
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToLong(object raw, out long value)
        {
            value = 0;
            if (raw == null)
                return false;
            if (raw is int) { value = (int)raw; return true; }
            if (raw is long) { value = (long)raw; return true; }
            if (raw is decimal) { value = (long)(decimal)raw; return true; }
            if (raw is double) { value = (long)(double)raw; return true; }
            return long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToDouble(object raw, out double value)
        {
            value = 0.0;
            if (raw == null)
                return false;
            if (raw is double) { value = (double)raw; return true; }
            if (raw is decimal) { value = (double)(decimal)raw; return true; }
            if (raw is int) { value = (int)raw; return true; }
            if (raw is long) { value = (long)raw; return true; }
            return double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private bool TryConvertToBool(object raw, out bool value)
        {
            value = false;
            if (raw == null)
                return false;
            if (raw is bool) { value = (bool)raw; return true; }
            return bool.TryParse(raw.ToString(), out value);
        }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Account Balance", Description = "When account net liquidation, including unrealized P&L, reaches this value, pending entries are cancelled, open positions are flattened, and new entries remain blocked. 0 disables.", GroupName = "C. Risk", Order = 0)]
        public double MaxAccountBalance { get; set; }

        // Re-added (Steve, 2026-08-07) as an eval-account compliance facility, not a
        // performance-tuning lever - see the maxDailyProfitLimitReached field comment. Default
        // 0/off; resets at 18:00 ET (CME trading day), same boundary as the separate
        // (still-removed) points-based variants - NOT midnight, unlike the pre-1024 dollar
        // implementation this was restored from. See GetTradingDay().
        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Daily Profit", Description = "Maximum daily account profit in currency, measured from the first tick's net liquidation each trading day (resets 18:00 ET). Reaching it cancels pending entries, flattens open positions, and blocks new entries for the rest of that trading day. 0 disables.", GroupName = "C. Risk", Order = 1)]
        public double MaxDailyProfit { get; set; }

        // Order rate guard is always on (2026-08-14, Steve: "hard code to enabled state and
        // remove the user option ... always on", same pattern as Cancel Entry On Gap Breach) -
        // no property, no toggle. See IsLiveOrderRateGuardActive.
        [Range(NewTradeActionReserve, 5000), NinjaScriptProperty]
        [Display(Name = "Order Actions / Hour", Description = "Conservative local EMAL action ceiling per NT8 connection. Default 1100 leaves headroom below Tradovate's observed 1500-request provider limit.", GroupName = "C. Risk", Order = 3)]
        public int OrderActionLimitPerHour { get; set; }

        // EMAL-1041 (2026-08-14, Steve): cancelling a still-working entry the moment its planned
        // TP is crossed avoids the fill-then-immediate-loss pattern Codex found in the Aug 13
        // live review. Was an OFF-by-default toggle (CancelEntryOnGapBreach) from EMAL-1039
        // through the first cut of EMAL-1041, pending a real test; WFYKZ (NT8 Playback, v1040,
        // full range, Part A+B+C, 4/27-8/13/2026, 76 days, 2,220 trades, matching QSJAW's full
        // range exactly) confirmed it eliminates the fill-then-flatten scratch pattern completely
        // (0/2220 exits via the gap latch, vs the same-range baseline's 682/2901) with WR/PF/
        // expectancy up substantially and maxDD DOWN both overall (-$92.40) and in both windows
        // individually (09:28 -$473.90, 09:55 -$41.70) - a clean win with no caveats. Results
        // dramatically improved and this is now hardcoded ON unconditionally (Steve, same day,
        // after seeing the full-range result): no toggle, no way to turn it off, one less
        // failure mode than a user-editable safety behavior. See
        // TraderTunerData/results/wfykz-2026-08-14/README.md and
        // results/qsjaw-2026-08-13-full-range/ for QSJAW, the comparison baseline. The unconditional
        // CancelEntryOrderIfActive() call is in EvaluateGapLatch above, both directions.

        // Default ON (2026-08-14, Steve): live incident, five accounts with identical working
        // EMALTarget limits at the same price, price traded at the level, only two filled, the
        // other three rode a 20-point reversal into the stop - a queue-position asymmetry a
        // passive limit target can't avoid on its own. OFF reverts to pure-limit behavior with
        // no code change, for any single instance that wants it. Scoped to the strategy's
        // 1-contract-only assumption - see EvaluateTargetTouchWatchdog's scope guard, which
        // disables the feature inertly (logs once) rather than reconciling partial fills if that
        // assumption is ever violated.
        [NinjaScriptProperty]
        [Display(Name = "Enable Target Touch Watchdog", Description = "When ON, if price trades at or beyond the working target's limit price but the target hasn't filled within Target Touch Grace (ms), cancels the target and exits at market (signal EMALTouchExit) once the cancel confirms unfilled. Never fires a second exit if the target fills during or after the grace window - the cancel confirmation is always awaited first. OFF reverts to pure-limit target behavior, no code-level difference from a prior cut.", GroupName = "C. Risk", Order = 5)]
        public bool EnableTargetTouchWatchdog { get; set; }

        [Range(100, 5000), NinjaScriptProperty]
        [Display(Name = "Target Touch Grace (ms)", Description = "How long the working target may sit unfilled after price first trades at or beyond its limit price before the watchdog cancels it and exits at market. Measured on live ticks, no timer thread. Default 400.", GroupName = "C. Risk", Order = 6)]
        public int TargetTouchGraceMs { get; set; }

        // EMAL-1045 (2026-08-18, Steve): live incident, Aug 17 09:43 - the target level printed
        // on Last for a fraction of a second then reversed; boxes whose feed processed that one
        // Last tick converted via the watchdog, boxes that didn't rode a 20+ point reversal into
        // the stop, purely on per-feed Last-tick timing/queue position, not a real difference in
        // what the market did. QuoteOrLast keys the same touch/breach test on the resting order's
        // own fillable side (Bid for a Long's target, Ask for a Short's - see EvaluateGapLatch's
        // comment) in addition to Last, so a touch the quote reached is caught even if no Last
        // trade happened to print exactly there. LastOnly reproduces the exact prior-cut
        // behavior for instant A/B comparison or rollback without a recompile. Tradeoff:
        // QuoteOrLast is more sensitive and will convert on some touches LastOnly would have let
        // resolve as a later clean target fill - trading a bit more ~1-tick haircut for
        // cross-box uniformity and fewer rides-to-the-stop. Applies to both the gap latch's
        // breach detection and the target touch watchdog; independent of Enable Target Touch
        // Watchdog for the gap latch half (the gap latch itself has no on/off toggle - see its
        // own comment), but the watchdog half also still requires that toggle ON.
        [NinjaScriptProperty]
        [Display(Name = "Touch Detection Mode", Description = "QuoteOrLast (default): the target-touch watchdog and gap latch trigger on either a Last trade AT the level or the relevant quote (Bid for Long, Ask for Short) reaching it - catches a fleeting one-tick touch even with no Last print exactly there. LastOnly: Last-trade detection only, byte-identical to the prior cut, for A/B comparison or rollback.", GroupName = "C. Risk", Order = 7)]
        public EMALTouchDetectionMode TouchDetectionMode { get; set; }

        // EMAL-1046 (2026-08-18, Steve): fixes an observed multi-lot bug where a partial-filled
        // entry's stop/target resize can reach the broker before the protective order is
        // Working, get rejected, and be silently dropped - leaving protection under-sized for
        // the position (account 1367, 2026-08-13 and 2026-08-17 incidents). Off is byte-
        // identical to the prior cut on every account, including multi-contract ones - the
        // reconciliation method's own first-lines inertness guard is what enforces this, not
        // this property alone. Auto is the recommended live setting for Steve's ~2 multi-
        // contract accounts; the guard means it does nothing at qty 1, so it is safe to leave
        // set even if an account is temporarily trading 1 lot. On exists to test the
        // reconciliation logic itself at qty 1, where it still no-ops harmlessly (quantities
        // already match).
        [NinjaScriptProperty]
        [Display(Name = "Multi-Contract Protection Fix", Description = "Off (default): no effect, byte-identical to the prior cut on every account. Auto: reconciles the stop/target order quantity to Position.Quantity whenever they differ, but ONLY once position quantity exceeds 1 - single-contract accounts are unaffected even when this is set. On: same reconciliation, active at any quantity (for testing; harmless no-op at qty 1). Fixes a partial-fill resize race where Tradovate can reject a resize submitted before the protective order reaches Working state.", GroupName = "C. Risk", Order = 8)]
        public EMALMultiContractProtectionFix MultiContractProtectionFix { get; set; }

        // EMAL-1050 (Steve, 2026-08-21; extended same day to also cover 09:43): OFF by default -
        // byte-identical to EMAL-1046 behavior on every account until explicitly turned on. Does
        // not touch the always-on 09:30 hard block above, and does not affect 09:28/09:29,
        // 09:36-09:42, or 09:44-onward - see IsAdditionalBlockedMinute's comment for the full
        // detail. Last setting in this group.
        [NinjaScriptProperty]
        [Display(Name = "Block 9:31-9:35 & 9:43", Description = "When ON, additionally blocks entries during the 09:31-09:35 ET minutes AND the 09:43 ET minute (09:30 is already always blocked regardless of this setting), so 09:36 becomes the first possible entry minute after 09:28/09:29. Does not affect 09:28/09:29, 09:36-09:42, or any minute from 09:44 onward. OFF by default - byte-identical to the prior cut when off.", GroupName = "C. Risk", Order = 9)]
        public bool Block0931To0935 { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Base URL", GroupName = "D. ProjectX API", Order = 3)]
        public string ProjectXApiBaseUrl { get; set; }

        [Browsable(false)]
        public bool ProjectXTradeAllAccounts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Username", Description = "ProjectX login username for direct routing.", GroupName = "D. ProjectX API", Order = 5)]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "API", Description = "ProjectX API key used with the username.", GroupName = "D. ProjectX API", Order = 6)]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Accounts", Description = "Comma-separated ProjectX account IDs or exact account names.", GroupName = "D. ProjectX API", Order = 7)]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Contract ID", Description = "Hidden optional contract override for support/debug use.", GroupName = "D. ProjectX API", Order = 8)]
        public string ProjectXContractId { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "EMA Period", Description = "EMA period evaluated on the one-minute chart.", GroupName = "Advanced", Order = 2)]
        public int EmaPeriod { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Minimum EMA Slope (points/minute)", Description = "Minimum completed-bar EMA change required in the trade direction.", GroupName = "Advanced", Order = 3)]
        public double MinimumEmaSlopePoints { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Contracts", Description = "Number of contracts per entry.", GroupName = "B. Sessions", Order = 0)]
        public int Contracts { get; set; }

        // Version stamp (Steve, 2026-08-05). GroupName has a leading space so it sorts
        // ahead of every other group, alphabetically, in the NT8 property grid - this must
        // stay the topmost setting. Purely informational; never read by strategy logic.
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Version", Description = "Which EMAL cut is installed. First entry is the current version number (selected by default); second entry is the IST date it was last modified. Never changes behavior.", GroupName = "A. Version", Order = 0)]
        public EMALVersion Version { get; set; }








        // ================================================================================
        // Sessions 1m (Steve, 2026-08-01, EMAL-21; original Asia and US 10:30-17:00 removed
        // entirely 2026-08-06, Europe removed entirely 2026-08-02, Steve: "I never want to use
        // this bot on London") - the two US morning windows below are the only sessions this
        // strategy trades. A single continuous Asia session existed EMAL-1033 through
        // EMAL-1034 (hidden, off by default; re-added 2026-08-09, unhid 2026-08-09) and was
        // removed again 2026-08-10 after both leads tested on it (a Sunday-only slope sweep and
        // an MNQ out-of-sample proxy check on the JPX-open window) came up empty - see
        // EMAL-1035-changelog.txt and Analysis_Plan §16 for the full record. No trace of it
        // remains in this file; if it's ever revisited, EMAL-1033/1034 are the reference cuts.
        // ================================================================================

        [NinjaScriptProperty]
        [Display(Name = "US 09:28-09:50 Setting", Description = "P1 (NT8, WFYKZ Apr27-Aug13, gap-breach ON) WR91.00% PF2.356 Net$41,427 MaxDD$1,649 Net/DD25.13\n\nP2 WR92.01% PF1.797 Net$16,332 MaxDD$1,339 Net/DD12.2", GroupName = "B. Sessions", Order = 1)]
        public EMALUs0928Setting Us0928Setting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:28-09:50 Min Slope", Description = "Driven by the US 09:28-09:50 Setting preset; not user-editable.", GroupName = "B. Sessions", Order = 2)]
        public double Us0928MinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 09:55-10:30 Setting", Description = "P1 WR86.99% PF1.413 Net$20,087 MaxDD$2,162 Net/DD9.29\n\nP2 (NT8, WFYKZ Apr27-Aug13, gap-breach ON) WR93.61% PF2.530 Net$46,606 MaxDD$1,342 Net/DD34.73\n\nP3 WR88.89% PF1.4 Net$14,899 MaxDD$2,053 Net/DD7.26", GroupName = "B. Sessions", Order = 3)]
        public EMALUs0955Setting Us0955Setting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:55-10:30 Min Slope", Description = "Driven by the US 09:55-10:30 Setting preset; not user-editable.", GroupName = "B. Sessions", Order = 4)]
        public double Us0955MinimumSlope { get; set; }




        [NinjaScriptProperty]
        [Display(Name = "Log", Description = "Write one CSV row per completed trade containing the entry context: slope, the previous three bars OHLCV, fill delay and outcome. Turn off to disable all file writing.", GroupName = "F. Logging", Order = 0)]
        public bool EnableFeatureLog { get; set; }

        [Browsable(false)]
        [Display(Name = "Log File Path", Description = "Full path to the CSV. Leave blank to auto-name EMAL_v{version}_log_{yyyy-MM-dd hh-mm tt}.csv in Documents, stamped at file-creation time. Appends if the file already exists. Ignored when Log is off.", GroupName = "F. Logging", Order = 1)]
        public string FeatureLogPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Research Log", Description = "Research-only, leave OFF for live trading. Records per fill the first-touch time to a 0.5pt grid (+/-30pt, 300s horizon), tracked past the TP/SL exit, so any TP/SL can be reconstructed offline.", GroupName = "F. Logging", Order = 3)]
        public bool EnablePathLog { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Execution Diagnostics", Description = "Write per-entry timing for signal-to-submit, first order state, and first fill callback, plus (EMAL-1044) a full entry-order transition trace and gap-breach/cancel-race detail. Leave OFF for lowest live-path overhead and for any live account; enable only for a short latency test or a Playback diagnostic run.", GroupName = "F. Logging", Order = 5)]
        public bool EnableExecutionDiagnostics { get; set; }

        [Browsable(false)]
        [Display(Name = "Path Log File", Description = "Full path to the research log CSV. Blank auto-names EMAL_v{version}_research_log_{yyyy-MM-dd hh-mm tt}.csv in Documents, stamped at file-creation time.", GroupName = "F. Logging", Order = 4)]
        public string PathLogPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tick Logging", Description = "Diagnostic only, OFF by default - enable only when hunting a cross-box divergence. Realtime-only: writes nothing during Playback/Historical/Analyzer. Records the exact Last/Bid/Ask tick stream this instance computes on, buffered and periodically flushed, so two boxes' files can be diffed after a divergent trade. Never affects signal logic, the watchdog, the gap latch, protection, or orders.", GroupName = "F. Logging", Order = 6)]
        public bool EnableTickLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tick Log Tag", Description = "Written into the tick log filename so files from different boxes/instances don't collide, e.g. \"box227\", \"box107\". Leave blank on a single-box setup.", GroupName = "F. Logging", Order = 7)]
        public string TickLogTag { get; set; }

        [Browsable(false)]
        [Display(Name = "Tick Log Folder", Description = "Folder for tick log CSVs, one file per instrument per day per tag, filename {instrument}_{tag}_{yyyyMMdd}.csv. Created if missing. Leave blank for NinjaTrader's user data folder \\ticklogs.", GroupName = "F. Logging", Order = 8)]
        public string TickLogFolder { get; set; }
    }

    // Version stamp (Steve, 2026-08-05). Purely informational, no effect on strategy
    // behavior. Two entries: the current cut number (default-selected) and the IST date
    // it was last modified. Bump the first member's number on every new cut, and rename
    // the second member's date to today (IST) on every edit, even within the same cut.
    public enum EMALVersion
    {
        version_1050,
        modified_2026_08_21
    }

    // EMAL-1045: LastOnly reproduces the prior cut's Last-trade-only detection exactly;
    // QuoteOrLast (default) additionally triggers on the relevant quote reaching the level. See
    // the Touch Detection Mode property comment for the full rationale/tradeoff.
    public enum EMALTouchDetectionMode
    {
        LastOnly,
        QuoteOrLast
    }

    // EMAL-1046: Off is the previous cut's exact behavior on every account. Auto is what
    // Steve runs on his ~2 multi-contract accounts - the reconciliation method's own inertness
    // guard means it only ever does anything once Position.Quantity > 1. On exists for testing
    // the reconciliation logic itself at qty 1, where it still no-ops harmlessly since there's
    // nothing to reconcile (quantities already match at 1).
    public enum EMALMultiContractProtectionFix
    {
        Off,
        On,
        Auto
    }

    public enum EMALUs0928Setting
    {
        Disabled,
        P1_ENG_TP4_SL18_Slope2_75,
        P2_ENG_TP3_SL18_Slope2_75
    }

    public enum EMALUs0955Setting
    {
        Disabled,
        P1_ENG_TP4_SL18_Slope2_75,
        P2_ENG_TP3_SL18_Slope2_75,
        P3_ENG_TP3_SL16_Slope2_75
    }

}
