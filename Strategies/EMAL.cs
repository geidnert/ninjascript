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
        // EMAL-1070: set on EVERY target breach regardless of EnableGapTargetLatch, so the
        // one-shot and the A/B counter still work with the latch off. gapTargetBreached itself
        // is set ONLY when the latch is enabled, because it is what drives both the pre-fill
        // cancel and SubmitOrUpdateProtection's post-fill flatten.
        private bool gapTargetBreachObserved;
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
        // EMAL-1051: same pattern, one pair per new session, resolved from that session's own
        // Setting popup in ResolveWindowPresets.
        private double asiaTp, asiaSl, europeTp, europeSl, preMarketTp, preMarketSl, usMiddayTp, usMiddaySl;
        private double entryFillValue;
        private int entryFilledQuantity;
        private double desiredProtectionTargetPrice;
        private int desiredProtectionQuantity;
        private bool terminalExitPending;

        // EMAL-1062 (Steve, 2026-09-04): terminal-exit cancel-then-confirm state, same pattern
        // already proven by the target-touch watchdog's own targetTouchWatchdogCancelPending -
        // see TrySubmitTerminalExit's comment for the root-cause incident this fixes. Per-trade
        // state, reset in ResetProtectionTracking alongside the watchdog's own fields.
        private bool terminalExitCancelPending;
        // EMAL-1069 (2026-09-09): when the cancel-then-confirm window OPENED. The window is a
        // DELIBERATE naked interval - EMAL-1062 cancels both protective orders and waits for
        // confirmations before firing the market exit, so from CancelOrder until that exit
        // fills the position has no protection. Before 1062 a failed terminal exit left the
        // position fully protected; after it, a LOST CANCEL CONFIRMATION latches
        // terminalExitCancelPending true FOREVER - there was no timeout anywhere in the file.
        // Stuck, it silently no-ops EVERY emergency exit through TrySubmitTerminalExit's first
        // guard: GapStop, GapTarget, MissingStop, MissingTarget, ProtectiveReject,
        // PreCloseFlatten, NewsBlockFlatten, CashOpenFlatten, MaxAccountBalance,
        // MaxDailyProfit. MissingStop no-op means a position that failed to get a stop has
        // nothing left to flatten it. Identified by the 2026-09-09 code audit as the leading
        // candidate for the naked positions Steve still sees post-1062.
        private DateTime terminalExitCancelPendingSinceUtc = DateTime.MinValue;
        // Generous: a cancel confirmation is normally sub-second. This is a stuck-state
        // breaker, not a latency budget - it must never fire on a healthy round trip.
        private const int TerminalExitCancelTimeoutSeconds = 30;
        // After retry exhaustion we stop attempting EXITS but must keep restoring PROTECTION.
        private const int TerminalExitExhaustedRestoreSeconds = 300;
        private string terminalExitCancelReason = string.Empty;
        private string terminalExitCancelEntrySignal = string.Empty;
        private MarketPosition terminalExitCancelDirection = MarketPosition.Flat;
        private bool terminalExitStopCancelDone;
        private bool terminalExitTargetCancelDone;
        private bool terminalExitStopFilled;
        private bool terminalExitTargetFilled;

        // Multi-contract protection reconciliation (Steve, 2026-08-18). Off by default; Auto
        // only does anything once Position.Quantity > 1 - the reconciliation method's own
        // inertness guard is what enforces that, not these fields. Per-trade state, reset in
        // both BeginProtectionTracking and ResetProtectionTracking alongside the other
        // protection fields above so a retry count never survives into the next trade.
        // EMAL-1067 (Steve, 2026-09-09): RECURRING naked-position audit. The existing
        // MissingStop/MissingTarget checks are EVENT-DRIVEN - they live inside
        // SubmitOrUpdateProtection, which is only reached from the entry-fill path in
        // OnExecutionUpdate and from the terminal-exit retry. They therefore cover
        // "protection failed to attach at fill" but NOT "protection attached, was accepted,
        // and later went away" (broker-side cancel, connection blip, order pulled). In that
        // second case nothing re-checks and the position sits unprotected until some other
        // event happens to fire. Steve reported seeing exactly that. This audit closes it.
        //
        // Anchored on when protection was first observed INCOMPLETE, and reset the moment it
        // is observed complete again. NOT anchored on position-open: that was the first
        // draft and it was wrong twice over - it could not debounce a transient (30 minutes
        // into a healthy position the grace has long elapsed, so ANY momentary gap would fire
        // instantly), and it tied the clock to the wrong event. NOTE, corrected 2026-09-09:
        // the EMAL-1046 multi-contract resize was cited as that transient and it is NOT one -
        // ReconcileMultiContractProtection and SubmitOrUpdateProtection both use ChangeOrder,
        // an IN-PLACE amend, so the Order reference survives, IsOrderActive stays true, and no
        // protection gap opens. The debounce is still required for genuine transients; only
        // the example was wrong. Anchoring on the fault itself both debounces and
        // still covers "protection never attached at all", since that is simply a fault
        // observed on the first sighting.
        private DateTime unprotectedSinceUtc = DateTime.MinValue;
        private int nakedAuditFirings;

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
        // EMAL-1070 (2026-09-10): cancelBarEndCount counts EVERY entry cancel regardless of
        // reason - one unconditional ++ - so the summary's total and its per-reason breakdown
        // must not be read as a partition. This breaks out the gap-latch (EMAL-1041) share.
        // WHY: `EMAL GAP BREACH DETECTED` and `ENTRY TRACE | cancel requested` are BOTH behind
        // IsExecutionDiagnosticsActive(), so the live cancel rate was observable only on the
        // single diagnostics-enabled account - 6 cancels against 34 signals on 2026-09-09, too
        // thin to score TNVQZ's P-58-3 with. Putting the count in the ALWAYS-ON summary gives
        // it from every account without enabling execution diagnostics fleet-wide.
        // NOTE the two are not the same event: GAP BREACH DETECTED fires whenever the latch
        // condition is met (26 times that day), while a cancel is only logged when an entry
        // order is still active to cancel - CancelEntryOrderIfActive early-returns otherwise.
        private int gapBreachCancelCount;
        // EMAL-1070 (2026-09-10): counts target breaches that occurred while a live working
        // entry existed - i.e. exactly what the latch would have acted on - and counts them
        // WHETHER OR NOT the latch is enabled. That makes it the live A/B's cohort measurement:
        // with the latch OFF it is the suppressed cohort; with it ON it should track
        // gapBreachCancelCount. It is the direct measurement of the size the tick
        // reconstruction could only estimate, and over-stated ~2x (P-58-3).
        private int gapLatchTargetBreachCount;
        private int blockedBarCount;

        // EMAL-1073 (Steve, 2026-09-19): the P1 5-minute opening-range gate + continuous
        // directional bias (fields, UpdateUs0928OpeningRangeState, IsUs0928OpeningRangeConditionMet,
        // and their two call sites) was REMOVED entirely after emal-analyst tested every variant
        // of the underlying hypothesis (daily gate, gate+bias, per-signal 5-min block, per-signal
        // 15-min block, on both ZQMFH and real-fill MXQFL data) and rejected all of them - see
        // emal-work/EMAL_Analysis_Plan.md §62/§63/§65. The core claim ("trades while price sits
        // inside the opening range are meaningfully worse") was tested directly and did not
        // survive: the effect reverses sign under interleaved-half testing and flips entirely
        // backwards at the 15-minute range. Opening-range/bias mechanisms are 0-for-11 in this
        // project's history as of this cut.
        // EMAL-1050: counts bars blocked by the 09:43 block (Block0943 - originally also covered
        // 09:31-09:35, and the file also had a separate always-on 09:30 hard block; both retired
        // 2026-08-28 when the US 09:36-09:55 window's start moved to 09:36, since no session opens
        // before then any more and neither block had anything left to protect).
        private int additionalBlockedMinuteBarCount;
        // EMAL-1051: counts bars blocked by the unconditional 08:28-08:32 news-release block.
        private int newsBlockedMinuteBarCount;
        // EMAL-1051 (second change, 2026-08-22): counts bars blocked by the unconditional
        // 16:55-17:00 pre-close block. See IsPreCloseWindow's comment.
        private int preCloseBlockedMinuteBarCount;

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
        // playback. Grid: 0.25-pt (1 NQ tick) steps to 30 pts each side; 300s horizon. Changed
        // from 0.5 (2 ticks) to 0.25 (2026-08-22, Steve) so TP/SL can be reconstructed at 1-tick
        // granularity - every downstream consumer (array sizing, CSV header column names, the
        // per-tick touch-check loop below) derives from this constant and PathLogLevels, so
        // this is the only line that needed to change. Doubles the research-log CSV's column
        // count (124 -> 244) and the per-tick loop's iteration count, both harmless: the log is
        // research-only (never runs live) and still just a few MB per run.
        private const double PathLogStepPoints = 0.25;
        private const double PathLogMaxPoints = 30.0;
        // Raised 300 -> 1800 (2026-08-22, Steve): the SL-sweep question for Asia/Europe/US
        // Pre-Market (whether a tighter stop than each session's current SL20 improves
        // net/maxDD) turned out to be genuinely undecided by the 300s-horizon reconstruction -
        // roughly 400 trades across those three sessions go adverse far enough to matter but
        // never touch either level within 300s, so their true outcome is unknown and the
        // SL-sweep's conclusion flips sign depending on how they're valued (see the chat record
        // for the full analysis). 1800s (30 min) should resolve nearly all of that censored
        // cohort directly from a fresh Playback capture, since EMAL's median trade duration is
        // well under 2 minutes even in its widest-bracket sessions (Europe/Pre-Market). Same
        // per-tick loop, same array sizing - only how long each PathRecorder stays tracked
        // before flushing changes, so concurrent open recorders grow proportionally with trade
        // frequency, not with grid resolution; harmless at EMAL's actual trade rate.
        private const double PathLogHorizonSeconds = 1800.0;
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
        // EMAL-1065 (Steve, 2026-09-07): the 09:50-09:54 gap was retested properly on EMAL-1064
        // (capture PXHRV, 2,659 trades, full four-halves methodology) and the two arms came back
        // a WASH - independently audited by emal-analyst (Analysis_Plan §48.5), no measured
        // basis for either choice. Steve decided to close the gap anyway, matching EMAL-1064's
        // shipped default - a judgment call, not a data-backed finding (TUNING-HANDOFF.md,
        // 2026-09-07 entries; Analysis_Plan §48.6). P1 now runs straight through to 09:55 with
        // no gap, under its own bracket/slope, same as P1's normal course.
        // Constant names (Us0928.../Us0955...) still identify "the first window" / "the second
        // window" throughout the source.
        // NY-anchored boundaries. Globex reopen and the US cash session never drift, because
        // CME (Chicago) and New York share the same DST dates.
        // US 09:36-09:55 window (Steve, 2026-08-02: start moved to 09:28, the real researched
        // start - see below). CHANGED 2026-08-28 (Steve): moved again to 09:36. The 09:28-09:35
        // span no longer needs the 09:30 hard block (removed - see the old IsHardBlockedMinute
        // history in EMAL-1053.cs and earlier) or the 09:31-09:35 portion of the old
        // Block0931To0935 toggle (now Block0943, 09:43-only - see IsAdditionalBlockedMinute),
        // since no session opens before 09:36 any more. See PreMarketStopMinute below, now a
        // separate literal deliberately NOT tied to this constant, so Pre-Market's own window
        // (still ending 09:28) does not silently extend when this one moves.
        private const int Us0928StartMinute = 9 * 60 + 36; // 09:36 ET, US 09:36-09:55 opens
        private const int Us0928EndMinute = 9 * 60 + 55;   // 09:55 ET (exclusive) - EMAL-1065:
                                                             // moved from 09:50, closing the old
                                                             // 09:50-09:54 gap (see comment above)
        private const int Us0955StartMinute = 9 * 60 + 55; // 09:55 ET, US 09:55-10:30 opens -
                                                             // now equal to Us0928EndMinute above,
                                                             // no gap and no overlap
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
                // EMAL-1075 (Steve, 2026-09-22): changed from "EMAL" per Steve's explicit request
                // to stop that literal string reaching the broker/account order report's Text/Name
                // column. See this file's changelog for the full investigation - short version:
                // every managed order call in this file (EnterLongLimit/EnterShort/ExitLong/
                // ExitShort) passes NT8 an internal signalName built from StrategySignalPrefix
                // ("EMALLong", "EMALStop", etc, always WITH a suffix), and the ProjectX REST order
                // JSON (ProjectXPlaceOrder) carries no name/tag/text field at all - neither matches
                // the reported symptom (bare "EMAL", no suffix, every order type). This Name
                // property is the one remaining plausible source: it's the strategy's own
                // registered display name, and NinjaTrader's native broker order-routing (the
                // Tradovate connection, not ProjectX) is documented to use it when constructing
                // outbound order tags for MANAGED orders - which is architecture inside NT8's
                // closed-source connection adapter, invisible to this file, so this could NOT be
                // verified by static reading alone.
                //
                // Set to "v1075", NOT left blank - code review caught that blank is the WEAKER of
                // the two options for the actual test this exists to run. If the broker report
                // shows "v1075", Name is confirmed to be the channel and Steve owns the lever from
                // here. If it still shows "EMAL", Name is DEFINITIVELY ruled out (no NT8 fallback
                // can turn a non-blank, non-"EMAL" string into "EMAL") and the real source is
                // outside this file entirely. A blank Name can't discriminate those two cases: if
                // "EMAL" still showed up, there'd be no way to tell whether NT8 quietly fell back
                // to the class name (also "EMAL") on a blank Name, or whether Name was never the
                // channel at all - and NEITHER interpretation would be provable from this file.
                //
                // ALSO: blanking Name would have blanked the strategy's own identity everywhere
                // NT8 itself displays it - the Strategies dialog, Control Center's Strategies tab,
                // Chart Trader's strategy label, saved templates - all of which key off Name per
                // NT8 convention. "v1075" keeps a real, if generic, identity there instead of
                // erasing it. NOTE this is SEPARATE from NT8's own native per-instance "Label"
                // field (Setup group, between Calculate and Maximum bars look back) that Steve
                // already uses for his own tracking (e.g. "Funded Group MW", showing in the
                // Strategies tab's Strategy column) - that field is native NT8 UI, not anything
                // in this source file, unaffected by this change, and Steve wants to keep using
                // it as-is. This Name change targets a DIFFERENT, separate field: the broker/
                // account report's own Text column, which Steve confirmed shows something
                // distinct from that native Strategy column.
                //
                // TEST THIS WITH A LIVE/SIM ORDER before trusting it - flagged explicitly per
                // Steve's own instruction not to claim a fix that only appears to work.
                Name = "v1075";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                // EMAL-1051 (sixth change, 2026-08-22, Steve): defaulted ON. Note this is a
                // NinjaTrader-native flatten, governed by the CHART'S Trading Hours Template, not
                // by EMAL's own session windows or the new EODForceCloseTime property above - it
                // is a second, independent flatten mechanism, not a replacement for either. If the
                // assigned template's session-end time doesn't line up with 17:00 ET, this can
                // fire at a different time than EODForceCloseTime, and if the template treats
                // Asia/Europe's overnight hours as outside its "session," this could flatten those
                // positions early. Confirm the chart's Trading Hours Template before relying on
                // this for any overnight session.
                IsExitOnSessionCloseStrategy = true;
                IsInstantiatedOnEachOptimizationIteration = false;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                // EMAL-1051 (seventh change, 2026-08-22, Steve): raised from 1 to 22 to match
                // OnBarUpdate's own internal warmup guard exactly (Math.Max(EmaPeriod, 20) + 2 =
                // Math.Max(9, 20) + 2 = 22 at today's defaults) - NinjaTrader itself now withholds
                // OnBarUpdate calls until that many bars exist, instead of calling in from bar 1
                // and relying solely on the internal check to no-op each time. Does not change
                // trading behavior - the internal check was, and remains, the actual gate. Tied
                // to today's EmaPeriod default: if EmaPeriod is ever raised above 20, the internal
                // check's own threshold moves with it automatically, but this hardcoded 22 would
                // not - same situation as before this change, just shifted, and EmaPeriod is
                // Browsable(false) so not something a user is expected to touch.
                BarsRequiredToTrade = 22;

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

                Version = EMALVersion.version_1077;   // bump on every new cut; see enum comment

                EmaPeriod = 9;
                MinimumEmaSlopePoints = 0.75;   // fallback for a minute outside both tracked windows
                Contracts = 1;

                MaxAccountBalance = 0.0;
                MaxDailyProfit = 0.0;
                EnableTargetTouchWatchdog = true;   // see comment on the property below
                TargetTouchGraceMs = 400;
                TouchDetectionMode = EMALTouchDetectionMode.QuoteOrLast;
                EnableNakedPositionAudit = true;   // EMAL-1067: safety net, ON by default
                NakedPositionGraceSeconds = 10;    // EMAL-1067: see the property comment
                MultiContractProtectionFix = EMALMultiContractProtectionFix.Off;   // see comment on the property below
                Block0943 = true;   // ALWAYS ON, hidden; see comment on the property below

                // EMAL-1051 (corrected 2026-08-22, Steve): all four new sessions default to
                // Disabled, same as the original two sessions (Us0928Setting/Us0955Setting
                // below) - a fresh instance takes zero trades on any of the six sessions until a
                // preset is explicitly chosen. *MinimumSlope defaults are overwritten by
                // ResolveWindowPresets from each session's own Setting popup once a non-Disabled
                // preset is selected, same as the original two sessions' pattern.
                AsiaSetting = EMALAsiaSetting.Disabled;
                AsiaMinimumSlope = 2.75;

                EuropeSetting = EMALEuropeSetting.Disabled;
                EuropeMinimumSlope = 2.75;

                PreMarketSetting = EMALPreMarketSetting.Disabled;
                PreMarketMinimumSlope = 2.75;

                USMiddaySetting = EMALUSMiddaySetting.Disabled;
                USMiddayMinimumSlope = 2.75;
                EODForceCloseTime = new TimeSpan(16, 55, 0);   // see comment on the property below
                EnableGapTargetLatch = true;   // EMAL-1073 (Steve, 2026-09-18): the 2026-09-10/
                    // 09-11 live A/B this was shipped OFF for has already run and closed out -
                    // restored to TRUE (the 1069 safety behaviour) as the new standing default.
                OrderActionLimitPerHour = 4000;   // EMAL-1070: was 1100; see the property's comment

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

                Us0928MinimumSlope = 5.25;   // overwritten by ResolveWindowPresets from the Setting popup
                Us0955MinimumSlope = 3.50;

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

        // 3 = US 09:28-09:55, 5 = US 09:55-10:30 (both UNCHANGED, untouched, never tuned - see
        // EMAL-1051-changelog.txt). EMAL-1051 adds four MORE sessions, found and validated from
        // the EMAL-1049 QNRVX full-day research log (Apr26-Aug21): 10 = Asia, 11 = Europe,
        // 12 = US Pre-Market, 13 = US Midday. These four use the SAME preset-popup mechanism as
        // the original two (a single "Setting" enum per session, "Disabled" first, TP/SL/slope
        // bundled into the chosen preset - no separate editable TP/SL/slope fields, no exposed
        // start/stop time properties either; those are fixed consts below, same as the original
        // two sessions' Us0928StartMinute/Us0928EndMinute). Asia's window (18:00-03:00) crosses
        // midnight; IsInMinuteWindow below handles that with plain minute-of-day int arithmetic,
        // matching this file's existing style rather than introducing TimeSpan comparisons.
        // -1 = outside every window.
        private const int AsiaStartMinute = 18 * 60;         // 18:00 ET
        private const int AsiaStopMinute = 3 * 60;           // 03:00 ET (wraps past midnight)
        private const int EuropeStartMinute = 3 * 60;        // 03:00 ET
        private const int EuropeStopMinute = 6 * 60 + 30;    // 06:30 ET
        private const int PreMarketStartMinute = 8 * 60;     // 08:00 ET
        private const int PreMarketStopMinute = 9 * 60 + 28; // 09:28 ET - CHANGED 2026-08-28: deliberately a
            // separate literal now, NOT "= Us0928StartMinute" any more. That equality held only while the
            // next session opened immediately at 09:28; now it opens at 09:36, and this constant must stay at
            // 09:28 regardless, leaving an intentional 09:28-09:36 no-trade gap between the two sessions
            // (see Us0928StartMinute's own comment above).
        private const int USMiddayStartMinute = Us0955EndMinute;   // 10:30 ET - butts directly against the existing session, no gap, no overlap
        private const int USMiddayStopMinute = 17 * 60;      // 17:00 ET, immediately before the CME daily maintenance break

        // EMAL-1051 (second change, 2026-08-22, Steve): standing, UNCONDITIONAL block on new
        // entries AND flatten of any open position from the configurable EOD Force Close time
        // up to the 17:00 ET CME daily maintenance halt - no toggle to turn the FEATURE off, but
        // the exact clock time is user-editable (see EODForceCloseTime property below), because
        // different prop firms enforce different cutoffs (Topstep requires flat by 16:00 ET;
        // Steve's own default is 16:55, 5 minutes ahead of the halt). Motivated by the
        // daily-reopen gap-risk analysis (chat record, EMAL_Analysis_Plan.md): of trades entered
        // 16:00-17:00, a real share were still open when the exchange halted, riding an
        // unmonitored hour with no working stop able to react before the 18:00 reopen. This
        // closes that exposure structurally (flatten before the halt) rather than trusting TP/SL
        // to resolve in time. Only US Midday can realistically still be open this late (every
        // other session has already closed or not yet opened by 16:55), but the check is
        // intentionally unconditional on session identity, same reasoning as the news block: a
        // standing risk rule, not something scoped to one session's tuning. The 17:00 upper bound
        // is NOT user-editable - it is a real CME market fact (the daily halt itself), not a risk
        // preference - only how far ahead of it to start closing is configurable. Setting
        // EODForceCloseTime at or after 17:00 leaves an empty window, i.e. disables the feature;
        // this is accepted, not guarded against, same as any other property that can be set to a
        // no-op value.
        private bool IsPreCloseWindow(DateTime easternTime)
        {
            int minuteOfDay = easternTime.Hour * 60 + easternTime.Minute;
            int eodForceCloseMinute = (int)EODForceCloseTime.TotalMinutes;
            return minuteOfDay >= eodForceCloseMinute && minuteOfDay < USMiddayStopMinute;
        }

        private int GetSessionIndex(DateTime platformTime)
        {
            DateTime ny = ConvertToZone(platformTime, easternZone);
            int nyMinute = ny.Hour * 60 + ny.Minute;

            if (nyMinute >= Us0928StartMinute && nyMinute < Us0928EndMinute)
                return 3;

            if (nyMinute >= Us0955StartMinute && nyMinute < Us0955EndMinute)
                return 5;

            if (AsiaSetting != EMALAsiaSetting.Disabled && IsInMinuteWindow(nyMinute, AsiaStartMinute, AsiaStopMinute))
                return 10;

            if (EuropeSetting != EMALEuropeSetting.Disabled && IsInMinuteWindow(nyMinute, EuropeStartMinute, EuropeStopMinute))
                return 11;

            if (PreMarketSetting != EMALPreMarketSetting.Disabled && IsInMinuteWindow(nyMinute, PreMarketStartMinute, PreMarketStopMinute))
                return 12;

            if (USMiddaySetting != EMALUSMiddaySetting.Disabled && IsInMinuteWindow(nyMinute, USMiddayStartMinute, USMiddayStopMinute))
                return 13;

            return -1;
        }

        // EMAL-1051: shared by the four new sessions only - the original two use their own plain
        // `nyMinute >= Start && nyMinute < End` comparison above, untouched, since neither of
        // their windows crosses midnight. Handles a window that DOES cross midnight (start >
        // stop, e.g. Asia's 18:00-03:00 -> 1080 > 180) by splitting into "from start to end of
        // day" OR "from start of day to stop". A same-day window (start <= stop) is the ordinary
        // case. Stop is exclusive either way, matching the original two sessions' convention.
        private static bool IsInMinuteWindow(int nyMinute, int startMinute, int stopMinute)
        {
            if (startMinute <= stopMinute)
                return nyMinute >= startMinute && nyMinute < stopMinute;
            return nyMinute >= startMinute || nyMinute < stopMinute;
        }

        private static string SessionName(int index)
        {
            switch (index)
            {
                case 3: return "9:36-9:55";
                case 5: return "9:55-10:30";
                case 10: return "18:00-3:00";
                case 11: return "3:00-6:30";
                case 12: return "8:00-9:28";
                case 13: return "10:30-17:00";
                default: return "Halt";
            }
        }

        private bool IsSessionEnabled(int index)
        {
            switch (index)
            {
                case 3: return Us0928Setting != EMALUs0928Setting.Disabled;
                case 5: return Us0955Setting != EMALUs0955Setting.Disabled;
                case 10: return AsiaSetting != EMALAsiaSetting.Disabled;
                case 11: return EuropeSetting != EMALEuropeSetting.Disabled;
                case 12: return PreMarketSetting != EMALPreMarketSetting.Disabled;
                case 13: return USMiddaySetting != EMALUSMiddaySetting.Disabled;
                default: return false;
            }
        }

        // EMAL-1077: per-session slope-calculation WINDOW, the companion to the per-session
        // threshold below. `ema[1] - ema[N]` where N is this value: N=2 is the one-bar slope every
        // cut through EMAL-1076 hardcoded, N=3 spans two bar intervals. N is the INDEX of the older
        // EMA bar, not the interval count - the span is N-1 intervals. P1 and P2 run N=3 as of this
        // cut (Analysis_Plan §72.10/§75); every other session stays at the shipped N=2.
        private int us0928SlopeWindow = DefaultSlopeWindowBars;
        private int us0955SlopeWindow = DefaultSlopeWindowBars;
        private int asiaSlopeWindow = DefaultSlopeWindowBars;
        private int europeSlopeWindow = DefaultSlopeWindowBars;
        private int preMarketSlopeWindow = DefaultSlopeWindowBars;
        private int usMiddaySlopeWindow = DefaultSlopeWindowBars;

        // The shipped one-bar slope. Also the fallback for any minute outside every tracked window.
        private const int DefaultSlopeWindowBars = 2;

        // Warmup must cover the WIDEST window any session could ask for, because warmup is global
        // while the window is per-session - sizing it to the active session would under-warm the
        // bar on which a wider-window session first opens.
        private int MaxConfiguredSlopeWindow
        {
            get
            {
                int m = DefaultSlopeWindowBars;
                if (us0928SlopeWindow > m) m = us0928SlopeWindow;
                if (us0955SlopeWindow > m) m = us0955SlopeWindow;
                if (asiaSlopeWindow > m) m = asiaSlopeWindow;
                if (europeSlopeWindow > m) m = europeSlopeWindow;
                if (preMarketSlopeWindow > m) m = preMarketSlopeWindow;
                if (usMiddaySlopeWindow > m) m = usMiddaySlopeWindow;
                return m;
            }
        }

        // Mirrors GetConfiguredSlope's session dispatch exactly - same cases, same fallback - so the
        // threshold and the window a bar is judged by can never come from different sessions.
        private int GetConfiguredSlopeWindow(DateTime platformTime)
        {
            switch (GetSessionIndex(platformTime))
            {
                case 3: return us0928SlopeWindow;
                case 5: return us0955SlopeWindow;
                case 10: return asiaSlopeWindow;
                case 11: return europeSlopeWindow;
                case 12: return preMarketSlopeWindow;
                case 13: return usMiddaySlopeWindow;
                default: return DefaultSlopeWindowBars;
            }
        }

        // Signed completed-bar slope for the session that owns `platformTime`, guarding the bar
        // count so a wide window can never read past the start of the series.
        private double GetSessionSlope(DateTime platformTime)
        {
            int n = GetConfiguredSlopeWindow(platformTime);
            return CurrentBar >= n ? ema[1] - ema[n] : double.NaN;
        }

        // Per-session slope threshold. Falls back to the global value for a minute outside
        // every tracked window.
        private double GetConfiguredSlope(DateTime platformTime)
        {
            switch (GetSessionIndex(platformTime))
            {
                case 3: return Math.Abs(Us0928MinimumSlope);
                case 5: return Math.Abs(Us0955MinimumSlope);
                case 10: return Math.Abs(AsiaMinimumSlope);
                case 11: return Math.Abs(EuropeMinimumSlope);
                case 12: return Math.Abs(PreMarketMinimumSlope);
                case 13: return Math.Abs(USMiddayMinimumSlope);
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
        // every session preset always specifies a real SL - this exists only so a bug produces a
        // safe, known-sane stop instead of a zero-distance one.
        private const double DefaultSafetyStopLossPoints = 18.0;

        // Per-session brackets (Steve, 2026-07-30; extended to four more sessions 2026-08-22).
        // Every session (all six) picks a preset (TP/SL/slope) via a single popup - no separate
        // editable TP/SL/slope fields anywhere. Outside every window there is nothing to
        // configure, so these return NaN and callers (currently only the info panel) must handle
        // that as "n/a".
        private double GetConfiguredTakeProfit()
        {
            switch (GetSessionIndex(GetBarOpenRaw()))
            {
                case 3: return us0928Tp;
                case 5: return us0955Tp;
                case 10: return asiaTp;
                case 11: return europeTp;
                case 12: return preMarketTp;
                case 13: return usMiddayTp;
                default: return double.NaN;
            }
        }

        private double GetConfiguredStopLoss()
        {
            switch (GetSessionIndex(GetBarOpenRaw()))
            {
                case 3: return us0928Sl;
                case 5: return us0955Sl;
                case 10: return asiaSl;
                case 11: return europeSl;
                case 12: return preMarketSl;
                case 13: return usMiddaySl;
                default: return double.NaN;
            }
        }

        // Resolves each session's Setting popup into its TP / SL / slope. The slope is written
        // back into the per-session *MinimumSlope property so GetConfiguredSlope keeps working
        // unchanged. Each of the four new sessions has exactly one real preset (found on the
        // QNRVX full-day scan) plus Disabled - unlike the original two sessions, which offer a
        // short list of alternatives, these were only validated at one bracket each so far.
        private void ResolveWindowPresets()
        {
            switch (Us0928Setting)
            {
                case EMALUs0928Setting.Disabled:                     us0928Tp = 5; us0928Sl = 18; Us0928MinimumSlope = 5.25; us0928SlopeWindow = 3; break;   // window is off; values are inert, see IsSessionEnabled
                case EMALUs0928Setting.S1_TP4_SL18_Slope5_25_Win3:   us0928Tp = 4; us0928Sl = 18; Us0928MinimumSlope = 5.25; us0928SlopeWindow = 3; break;
                // Legacy names from saved templates all resolve to the single live preset - AND the
                // property is rewritten to that member. Without the rewrite the popup would keep
                // displaying the legacy name while the strategy ran the new gate, which would make
                // the changelog's own install check ("confirm both popups read the new preset")
                // report a FALSE NEGATIVE on a correct install. Also makes the next workspace save clean.
                case EMALUs0928Setting.S1_TP4_SL18_Slope2_75:
                case EMALUs0928Setting.S2_TP3_SL18_Slope2_75:
                    Print(string.Format("EMAL CONFIG REMAP | US 09:36-09:55 legacy preset '{0}' from a saved template -> S1_TP4_SL18_Slope5_25_Win3 (slope 5.25, 3-bar window)", Us0928Setting));
                    Us0928Setting = EMALUs0928Setting.S1_TP4_SL18_Slope5_25_Win3;
                    goto default;
                default: /* S1_TP4_SL18_Slope5_25_Win3 */            us0928Tp = 4; us0928Sl = 18; Us0928MinimumSlope = 5.25; us0928SlopeWindow = 3; break;
            }
            switch (Us0955Setting)
            {
                case EMALUs0955Setting.Disabled:                       us0955Tp = 3.75; us0955Sl = 18; Us0955MinimumSlope = 3.50; us0955SlopeWindow = 3; break;   // window is off; values are inert, see IsSessionEnabled
                case EMALUs0955Setting.S1_TP3_75_SL18_Slope3_50_Win3:  us0955Tp = 3.75; us0955Sl = 18; Us0955MinimumSlope = 3.50; us0955SlopeWindow = 3; break;
                // Legacy names resolve to the live preset AND rewrite the property - see the
                // US 09:36-09:55 block above for why the rewrite matters. S4_TP3_75_SL18_Slope2_75
                // is the one Steve actually runs today, so this is the path his install will take.
                case EMALUs0955Setting.S1_TP4_SL18_Slope2_75:
                case EMALUs0955Setting.S2_TP3_SL18_Slope2_75:
                case EMALUs0955Setting.S3_TP3_SL16_Slope2_75:
                case EMALUs0955Setting.S4_TP3_75_SL18_Slope2_75:
                    Print(string.Format("EMAL CONFIG REMAP | US 09:55-10:30 legacy preset '{0}' from a saved template -> S1_TP3_75_SL18_Slope3_50_Win3 (slope 3.50, 3-bar window)", Us0955Setting));
                    Us0955Setting = EMALUs0955Setting.S1_TP3_75_SL18_Slope3_50_Win3;
                    goto default;
                default: /* S1_TP3_75_SL18_Slope3_50_Win3 */           us0955Tp = 3.75; us0955Sl = 18; Us0955MinimumSlope = 3.50; us0955SlopeWindow = 3; break;
            }
            switch (AsiaSetting)
            {
                case EMALAsiaSetting.Disabled:                        asiaTp = 4;   asiaSl = 20; AsiaMinimumSlope = 2.75; asiaSlopeWindow = DefaultSlopeWindowBars; break;   // window is off; values are inert, see IsSessionEnabled
                case EMALAsiaSetting.TP4_SL20_Slope2_75:         asiaTp = 4;   asiaSl = 20; AsiaMinimumSlope = 2.75; asiaSlopeWindow = DefaultSlopeWindowBars; break;
                default: /* TP4_SL20_Slope2_75 */                asiaTp = 4;   asiaSl = 20; AsiaMinimumSlope = 2.75; asiaSlopeWindow = DefaultSlopeWindowBars; break;
            }
            switch (EuropeSetting)
            {
                case EMALEuropeSetting.Disabled:                      europeTp = 8.5; europeSl = 20; EuropeMinimumSlope = 2.75; europeSlopeWindow = DefaultSlopeWindowBars; break;
                case EMALEuropeSetting.TP8_5_SL20_Slope2_75:   europeTp = 8.5; europeSl = 20; EuropeMinimumSlope = 2.75; europeSlopeWindow = DefaultSlopeWindowBars; break;
                default:                                              europeTp = 8.5; europeSl = 20; EuropeMinimumSlope = 2.75; europeSlopeWindow = DefaultSlopeWindowBars; break;
            }
            switch (PreMarketSetting)
            {
                case EMALPreMarketSetting.Disabled:                       preMarketTp = 11; preMarketSl = 20; PreMarketMinimumSlope = 2.75; preMarketSlopeWindow = DefaultSlopeWindowBars; break;
                case EMALPreMarketSetting.TP11_SL20_Slope2_75:  preMarketTp = 11; preMarketSl = 20; PreMarketMinimumSlope = 2.75; preMarketSlopeWindow = DefaultSlopeWindowBars; break;
                default:                                                  preMarketTp = 11; preMarketSl = 20; PreMarketMinimumSlope = 2.75; preMarketSlopeWindow = DefaultSlopeWindowBars; break;
            }
            switch (USMiddaySetting)
            {
                case EMALUSMiddaySetting.Disabled:                      usMiddayTp = 3; usMiddaySl = 13; USMiddayMinimumSlope = 2.75; usMiddaySlopeWindow = DefaultSlopeWindowBars; break;
                case EMALUSMiddaySetting.TP3_SL13_Slope2_75:   usMiddayTp = 3; usMiddaySl = 13; USMiddayMinimumSlope = 2.75; usMiddaySlopeWindow = DefaultSlopeWindowBars; break;
                default:                                                usMiddayTp = 3; usMiddaySl = 13; USMiddayMinimumSlope = 2.75; usMiddaySlopeWindow = DefaultSlopeWindowBars; break;
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
            int slopeWindow = GetConfiguredSlopeWindow(barOpenRaw);
            double slope = ema[1] - ema[slopeWindow];
            // slopePrev shifts the WHOLE window back one bar, so it stays a like-for-like "was the
            // trend already this steep one bar ago" check at any window size (same convention the
            // RVKTM research branch used, verified there at N=2/3/4).
            double slopePrev = ema[2] - ema[1 + slopeWindow];

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

        // EMAL-1051 (Steve, 2026-08-21): StartPathRecorder's call moved OUT of this method - see
        // the OnOrderUpdate entry-fill call site, which now calls it directly and unconditionally
        // (StartPathRecorder has its own correct EnablePathLog gate). Previously it lived here,
        // which meant Research Log (EnablePathLog) silently never recorded anything unless Log
        // (EnableFeatureLog) was ALSO on - the two toggles are meant to be independent (Research
        // Log's own description says nothing about depending on Feature Log), and this method
        // returns immediately above when EnableFeatureLog is off, so StartPathRecorder was never
        // reached. Found 2026-08-21 when Steve had Research Log on, Log off, ran a full-day
        // Playback for an hour (~1 month of simulated trades), and no research-log file was ever
        // created - not a settings mistake, a real defect in this coupling.
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
                // EMAL-1070: ".fff" added 2026-09-10. Whole-second ExitTime made trade HOLD TIME
                // unmeasurable - computed holds came out NEGATIVE (min -2.48s) against a
                // sub-second fill clock (EntryTimeET + FillDelaySec), which left TNVQZ's
                // prediction P-58-2 untestable. Logging format only: no logic, no order path.
                exitTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
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
            Print(string.Format("      US 0928-0955       : {0}  slope {1}", Us0928Setting, Us0928MinimumSlope));
            Print(string.Format("      US 0955-1030       : {0}  slope {1}", Us0955Setting, Us0955MinimumSlope));
            Print(string.Format("  bars blocked        : {0}  (session gate)", blockedBarCount));
            Print(string.Format("  9:43 block          : enabled={0}  bars blocked: {1}", Block0943, additionalBlockedMinuteBarCount));
            Print(string.Format("  8:28-8:32 news block : always on  bars blocked: {0}", newsBlockedMinuteBarCount));
            Print("  8:29 news flatten : always on");
            Print("  9:29 cash-open force close : always on");
            Print(string.Format("  EOD Force Close ({0:hh\\:mm}-17:00) block/flatten : always on  bars blocked: {1}", EODForceCloseTime, preCloseBlockedMinuteBarCount));
            Print(string.Format("      Asia 18:00-3:00     : {0}  slope {1}", AsiaSetting, AsiaMinimumSlope));
            Print(string.Format("      Europe 3:00-6:30    : {0}  slope {1}", EuropeSetting, EuropeMinimumSlope));
            Print(string.Format("      US Pre-Mkt 8:00-9:28: {0}  slope {1}", PreMarketSetting, PreMarketMinimumSlope));
            Print(string.Format("      US Midday 10:30-17:00: {0}  slope {1}", USMiddaySetting, USMiddayMinimumSlope));
            Print(string.Format("  order rate guard    : always on / {0} actions (entries blocked: {1})",
                OrderActionLimitPerHour, rateGuardBlockedEntryCount));
            Print(string.Format("  signals generated   : {0}", signalCount));
            Print(string.Format("  filled              : {0}  ({1:F1}%)",
                filledCount, 100.0 * filledCount / signalCount));
            Print(string.Format("  cancelled           : {0}  ({1:F1}%)",
                cancelled, 100.0 * cancelled / signalCount));
            // EMAL-1070: relabelled from "at bar end". cancelBarEndCount is incremented on
            // EVERY cancel reason, so with the gap-latch line below it the two sub-lines would
            // read as a partition that does not sum. See the field comment.
            Print(string.Format("      total (all reasons): {0}", cancelBarEndCount));
            Print(string.Format("      gap-latch cancels: {0}  ({1:F1}% of signals)  [target latch {2}]",
                gapBreachCancelCount, 100.0 * gapBreachCancelCount / signalCount,
                EnableGapTargetLatch ? "ON" : "OFF"));
            Print(string.Format("      gap-latch target breaches w/ live entry: {0}  ({1:F1}% of signals)",
                gapLatchTargetBreachCount,
                100.0 * gapLatchTargetBreachCount / signalCount));
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

            // EMAL-1069: ALSO enter when a cancel-then-confirm window is open, not only when a
            // retry is already scheduled. The stuck-latch case this breaker exists for happens
            // on a FIRST-attempt exit, where terminalExitRetryReason is still empty - so the
            // original guard alone made the breaker unreachable in precisely the scenario it
            // was written for. (Caught before release; the method's own internal guards still
            // short-circuit every healthy path immediately.)
            if (!string.IsNullOrWhiteSpace(terminalExitRetryReason) || terminalExitCancelPending)
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

            if (CurrentBar < Math.Max(EmaPeriod, 20) + MaxConfiguredSlopeWindow + 1)
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
            // (ema[1] - ema[N] for this session's window), signed - unlike the threshold, which
            // is always positive.
            double currentSlope = GetSessionSlope(raw);
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

        // RETIRED 2026-08-28 (Steve): the always-on 09:30 hard block (formerly IsHardBlockedMinute,
        // Steve 2026-08-08, kept on discretion per Analysis_Plan.md §12.16 despite testing negative
        // on drawdown grounds) is deleted outright, not just disabled. It only ever fired inside the
        // US 09:28-09:50 window, and that window now opens at 09:36 - 09:30 is structurally
        // unreachable by any session (Pre-Market still stops at 09:28, nothing opens again until
        // 09:36), so the block had nothing left to protect. See Us0928StartMinute's comment.

        // EMAL-1050 (Steve, 2026-08-21; extended same day to add 09:43). CHANGED 2026-08-28 (Steve):
        // the 09:31-09:35 portion is deleted for the same reason as the 09:30 hard block above - that
        // span is structurally unreachable now that the window opens at 09:36, not 09:28. Renamed
        // Block0931To0935 -> Block0943 to match: this toggle now covers ONLY the 09:43 minute, still
        // always-on/hidden per the 2026-08-22 change. Does NOT touch 09:28/09:29 (now pre-session,
        // never reachable), 09:30-09:35 (ditto), 09:36-09:42, or 09:44 onward.
        private bool IsAdditionalBlockedMinute(DateTime easternTime)
        {
            if (!Block0943 || easternTime.Hour != 9)
                return false;

            return easternTime.Minute == 43;
        }

        // EMAL-1051 (Steve, 2026-08-22): standing, UNCONDITIONAL block on 08:28-08:32 ET - no
        // toggle, matching the 09:30 hard block's own always-on treatment, not
        // IsAdditionalBlockedMinute's gated pattern. Steve's stated reason: avoids the 08:30 ET
        // high-impact economic news release slot (CPI, jobs report, etc.), and he wants it
        // blocked "even when there is not news" - a standing risk rule, not something to be
        // discovered or overridden by what the QNRVX data showed for that window. Applies
        // regardless of which session (if any) is active, same placement as the other two
        // unconditional/gated blocks below.
        // EMAL-1059 (Steve, 2026-08-31): this window's ENTRY-BLOCKING behavior is unchanged, but
        // see IsNewsFlattenMinute below for the separate flatten this block gained the same day -
        // an already-open position is no longer left to ride its own TP/SL through the 08:30
        // spike untouched.
        private bool IsNewsReleaseBlockedMinute(DateTime easternTime)
        {
            return easternTime.Hour == 8 && easternTime.Minute >= 28 && easternTime.Minute < 32;
        }

        // EMAL-1061 (Steve, 2026-09-03): standing, UNCONDITIONAL flatten-only window at 08:29 ET -
        // deliberately DECOUPLED from IsNewsReleaseBlockedMinute's 08:28-08:31 entry-blocking
        // window (which is unchanged and still governs new entries), same pattern as
        // IsCashOpenForceCloseMinute below. CHANGED from firing at 08:28 (this window's original
        // open) to its own dedicated 08:29 check, per Steve: still comfortably ahead of the 08:30
        // news slot, but gives an open trade one more minute of its own TP/SL before being forced
        // out, same reasoning already applied to the 09:29 cash-open flatten.
        private bool IsNewsFlattenMinute(DateTime easternTime)
        {
            return easternTime.Hour == 8 && easternTime.Minute == 29;
        }

        // EMAL-1061 (Steve, 2026-09-03): standing, UNCONDITIONAL flatten-only window at 09:29 ET -
        // no entry-blocking companion needed, since no session is enterable in the 09:28-09:36 gap
        // between Pre-Market's stop and the US 09:36-09:55 window's start (see PreMarketStopMinute/
        // Us0928StartMinute above). Steve's stated reason: any position still open going into the
        // 09:30 cash-open volatility spike should be closed first, regardless of which session
        // opened it - same category of standing risk rule as the 08:28 news block and EOD Force
        // Close, not scoped to Pre-Market specifically (Pre-Market is simply the only session whose
        // trades can realistically still be open this close to 09:30, since every prior session has
        // already fully closed and no new position exists yet from a session that hasn't opened).
        // A single minute is sufficient: OnBarUpdate's flatten fires an immediate market exit the
        // instant the 09:29 bar opens, well before 09:30:00.
        private bool IsCashOpenForceCloseMinute(DateTime easternTime)
        {
            return easternTime.Hour == 9 && easternTime.Minute == 29;
        }

        private bool IsEntryWindowOpen()
        {
            DateTime barOpenRaw = GetBarOpenRaw();
            DateTime barOpen = ConvertToEastern(barOpenRaw);

            // EMAL-1050: checked first, before the session/window gate below - "no matter what
            // the settings are" placement, gated on its own toggle (Block0943).
            if (IsAdditionalBlockedMinute(barOpen))
            {
                additionalBlockedMinuteBarCount++;
                return false;
            }

            // EMAL-1051: unconditional, same placement as the two blocks above - see
            // IsNewsReleaseBlockedMinute's comment.
            if (IsNewsReleaseBlockedMinute(barOpen))
            {
                newsBlockedMinuteBarCount++;
                return false;
            }

            // EMAL-1051 (second change, 2026-08-22): unconditional, same placement as the blocks
            // above - see IsPreCloseWindow's comment. A fresh entry this late would just have to
            // be flattened again minutes later by the check in OnBarUpdate below, so it is blocked
            // here rather than opened and immediately closed.
            if (IsPreCloseWindow(barOpen))
            {
                preCloseBlockedMinuteBarCount++;
                return false;
            }

            // Window gate is unconditional (Steve, 2026-08-06): entries are confined to whichever
            // sessions are enabled - originally just the two US windows, now up to six with
            // EMAL-1051's four additions.
            int session = GetSessionIndex(barOpenRaw);
            if (session < 0 || !IsSessionEnabled(session))
                return false;

            return true;
        }



        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            // EMAL-1067: FIRST thing after the series guard, deliberately ahead of every
            // strategy gate below (warmup, window-closed, blocked-minute, position-open).
            // A naked position must be caught even when the strategy would otherwise return
            // early - e.g. outside a session window, which is exactly when an unattended
            // position is most dangerous and least likely to be noticed.
            AuditNakedPosition();

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
                // EMAL-1051 (second change, 2026-08-22): flatten any position still open in the
                // 5 minutes before the 17:00 ET CME daily halt - see IsPreCloseWindow's comment.
                // Checked before the ordinary "position open, do nothing but cancel a stray entry
                // order" path below, since this is the one case where an open position DOES need
                // to be acted on directly rather than left to its own stop/target.
                if (IsPreCloseWindow(ConvertToEastern(GetBarOpenRaw())))
                {
                    TrySubmitTerminalExit("PreCloseFlatten", protectedEntrySignal);
                    return;
                }

                // EMAL-1059 (Steve, 2026-08-31): flatten any position still open ahead of the
                // 08:30 news slot, same treatment as PreCloseFlatten above - before this cut, the
                // 08:28-08:31 news block only gated NEW entries, and a position opened earlier
                // just rode its own TP/SL through the 08:30 spike with no forced exit.
                // CHANGED 2026-09-03 (EMAL-1061, Steve): now checks the dedicated
                // IsNewsFlattenMinute (08:29) instead of the entry-block's own
                // IsNewsReleaseBlockedMinute (08:28-08:31) - decoupling the flatten from the
                // wider entry-block window gives an already-open trade one more minute of its own
                // TP/SL before being forced out, same reasoning as the 09:29 cash-open flatten
                // below. Still comfortably ahead of 08:30:00. If TrySubmitTerminalExit's first
                // attempt doesn't confirm flat, EvaluateTerminalExitRecovery retries on its own
                // real-time backoff schedule (2s/5s/15s/... up to 300s) - independent of this
                // window's width, so narrowing it to a single minute does not weaken the retry.
                if (IsNewsFlattenMinute(ConvertToEastern(GetBarOpenRaw())))
                {
                    TrySubmitTerminalExit("NewsBlockFlatten", protectedEntrySignal);
                    return;
                }

                // EMAL-1061 (Steve, 2026-09-03): flatten any position still open at 09:29 ET, to
                // avoid riding into the 09:30 cash-open volatility spike. Same mechanism/
                // placement as the two flattens above - see IsCashOpenForceCloseMinute's comment
                // for why this is unconditional rather than scoped to Pre-Market.
                if (IsCashOpenForceCloseMinute(ConvertToEastern(GetBarOpenRaw())))
                {
                    TrySubmitTerminalExit("CashOpenFlatten", protectedEntrySignal);
                    return;
                }

                CancelEntryOrderIfActive("position-open");
                return;
            }

            // 20 covers the AvgVolume20 lookback used by the feature log. The slope term is the
            // WIDEST configured window, not this bar's session - see MaxConfiguredSlopeWindow.
            if (CurrentBar < Math.Max(EmaPeriod, 20) + MaxConfiguredSlopeWindow + 1)
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
            double completedEmaSlope = GetSessionSlope(GetBarOpenRaw());
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
            if (string.Equals(reason, "gap-breach-target", StringComparison.Ordinal))
                gapBreachCancelCount++;   // EMAL-1070: see the field comment
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
            unprotectedSinceUtc = DateTime.MinValue;   // EMAL-1068 finding 5: reset with the rest of the per-trade protection state, not only via the audit's own Flat branch
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
            gapTargetBreachObserved = false;   // EMAL-1070
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
        // interacts with a favorable move and this is the normal, common case.
        // EMAL-1070 CORRECTION: gapStopBreached still feeds SubmitOrUpdateProtection's post-fill
        // decision unchanged, but gapTargetBreached now does so ONLY when EnableGapTargetLatch is
        // true - with the latch off it is never set and the target-side post-fill backstop does
        // not exist. The stop-side backstop remains for the rare race either way.
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
                    if (!gapTargetBreachObserved && price >= gapLatchTargetPrice)
                    {
                        // EMAL-1070: OBSERVATION is unconditional and drives only the counter,
                        // so the live A/B measures its own cohort whether the latch is on or off.
                        gapTargetBreachObserved = true;
                        if (IsOrderActive(entryOrder) && !entryCancelPending)
                            gapLatchTargetBreachCount++;

                        // EMAL-1070: the TARGET-side latch is gated AS A WHOLE - the flag, the
                        // pre-fill cancel, and (via gapTargetBreached) the post-fill "target
                        // crossed before protection | flattening" branch in
                        // SubmitOrUpdateProtection. **Gating only the cancel was the first
                        // draft and was WRONG**: every suppressed entry would still have filled
                        // and then been market-flattened for a guaranteed scratch, which is the
                        // opposite of the arm WBGQF measured (EMAL-1066 gates the FLAG - see
                        // 1066:2691/2715 - which is why its cohort got a normal bracket and ran
                        // to target). The STOP side above is deliberately NOT gated: a fill into
                        // an already-breached stop is the genuinely dangerous case and keeps its
                        // emergency flatten.
                        if (EnableGapTargetLatch)
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
                    if (!gapTargetBreachObserved && price <= gapLatchTargetPrice)
                    {
                        // EMAL-1070: OBSERVATION is unconditional and drives only the counter,
                        // so the live A/B measures its own cohort whether the latch is on or off.
                        gapTargetBreachObserved = true;
                        if (IsOrderActive(entryOrder) && !entryCancelPending)
                            gapLatchTargetBreachCount++;

                        // EMAL-1070: the TARGET-side latch is gated AS A WHOLE - the flag, the
                        // pre-fill cancel, and (via gapTargetBreached) the post-fill "target
                        // crossed before protection | flattening" branch in
                        // SubmitOrUpdateProtection. **Gating only the cancel was the first
                        // draft and was WRONG**: every suppressed entry would still have filled
                        // and then been market-flattened for a guaranteed scratch, which is the
                        // opposite of the arm WBGQF measured (EMAL-1066 gates the FLAG - see
                        // 1066:2691/2715 - which is why its cohort got a normal bracket and ran
                        // to target). The STOP side above is deliberately NOT gated: a fill into
                        // an already-breached stop is the genuinely dangerous case and keeps its
                        // emergency flatten.
                        if (EnableGapTargetLatch)
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

                // EMAL-1062: terminal-exit cancel confirmation - same placement/reasoning as the
                // watchdog block above (before the generic Rejected branch, so a cancel that
                // comes back Rejected doesn't ALSO trigger a second terminal exit). Both the stop
                // and the target may need to confirm (whichever were active when
                // TrySubmitTerminalExit ran) before the market exit is allowed to fire.
                if (terminalExitCancelPending
                    && (orderName == StopExitSignal || orderName == TargetExitSignal))
                {
                    bool cancelTerminalState = orderState == OrderState.Cancelled
                        || orderState == OrderState.Filled
                        || orderState == OrderState.Rejected;
                    if (cancelTerminalState)
                    {
                        if (orderName == StopExitSignal)
                        {
                            terminalExitStopCancelDone = true;
                            if (filled > 0)
                                terminalExitStopFilled = true;
                        }
                        else
                        {
                            terminalExitTargetCancelDone = true;
                            if (filled > 0)
                                terminalExitTargetFilled = true;
                        }

                        if (terminalExitStopCancelDone && terminalExitTargetCancelDone)
                        {
                            bool eitherFilled = terminalExitStopFilled || terminalExitTargetFilled;
                            string pendingReason = terminalExitCancelReason;
                            MarketPosition pendingDirection = terminalExitCancelDirection;
                            string pendingEntrySignal = terminalExitCancelEntrySignal;
                            terminalExitCancelPending = false;
                            terminalExitCancelPendingSinceUtc = DateTime.MinValue;   // EMAL-1069

                            if (eitherFilled)
                            {
                                // A resting protective order won the race - the position is
                                // already flattening (or flat) via its own normal fill path.
                                // Submitting the market exit too would be the exact double-fire
                                // this cut exists to prevent.
                                Print(string.Format(
                                    "{0} | EMAL EXIT TRACE | protective order filled during "
                                    + "terminal-exit cancel race - no market exit sent | reason={1} "
                                    + "stopFilled={2} targetFilled={3}",
                                    time, pendingReason, terminalExitStopFilled, terminalExitTargetFilled));
                            }
                            else
                            {
                                SubmitTerminalExitMarketOrder(pendingReason, pendingDirection,
                                    pendingEntrySignal);
                            }
                        }
                        return;
                    }
                    // Not yet terminal - fall through and wait for the next callback, same as
                    // the watchdog block above.
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

                // EMAL-1051: called unconditionally, same reasoning as openEntryDirection above -
                // Research Log (EnablePathLog) must work independently of Log (EnableFeatureLog).
                // StartPathRecorder has its own correct EnablePathLog/direction/price guard, so
                // this is a no-op whenever Research Log is off, regardless of Feature Log's state.
                StartPathRecorder(averageFillPrice, openEntryDirection, time);

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

        // EMAL-1067/1068: runs from OnBarUpdate on every tick (Calculate.OnEachTick) - cheap,
        // every early-out is a field read. FLATTENS WHEN EITHER LEG IS MISSING - stop OR
        // target - once the fault has PERSISTED for NakedPositionGraceSeconds.
        //
        // Both legs are required because EMAL's edge IS the bracket geometry: breakeven is
        // 81.8% at TP4/SL18, so a position that has lost its target can no longer take the
        // +TP and can only exit at the full stop or an EOD/session flatten. Its favourable
        // outcome is gone while the unfavourable one remains - that is not "capped and safe",
        // it is a position with only downside left. This also matches the file's own
        // event-driven MissingTarget, which flattens. (An earlier draft flattened only on a
        // missing stop, on a "capped, not endangered" argument; Steve overruled it 2026-09-09
        // and was right.)
        //
        // Routes through TrySubmitTerminalExit, never a bare ExitLong/ExitShort: protective
        // orders in this file are NEVER OCO-linked, so a market exit racing a live protective
        // order is exactly how the 2026-09-03 naked positions were created. When both legs
        // are already gone that method fires immediately (nothing to race); when only one is
        // missing it cancels the survivor first and fires on confirmation.
        //
        // SCOPE LIMIT, stated so this is not mistaken for full coverage: this reads Position
        // (the STRATEGY's position), not PositionAccount. With StartBehavior WaitUntilFlat
        // that is correct on disable/re-enable - an adopted account position is not flattened.
        // But an account position that has drifted OUT of the strategy's own tracking is
        // invisible here, and "position/order-state desync" is one of the two candidate root
        // causes of the defect this audit mitigates. It may not cover the case it is aimed at.
        private void AuditNakedPosition()
        {
            if (!EnableNakedPositionAudit)
                return;

            // EMAL-1068: REALTIME ONLY. This is a wall-clock timer (DateTime.UtcNow); running
            // it over historically-simulated bars would make output machine-speed dependent -
            // a GC pause or a loaded machine could change the trade book from identical
            // inputs. Harmless on a live/Playback warmup (no orders, Position is Flat, the
            // audit no-ops) but NOT harmless in a Strategy Analyzer backtest, where
            // IsHistoricalTradeSimulationContext() is true and orders do flow. The mitigation
            // is only meaningful in real time anyway.
            if (State != State.Realtime)
            {
                unprotectedSinceUtc = DateTime.MinValue;
                return;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                unprotectedSinceUtc = DateTime.MinValue;
                return;
            }

            // An exit is already in flight - do not stack another.
            if (terminalExitPending || terminalExitCancelPending
                || targetTouchWatchdogCancelPending || IsTerminalExitRetryWaiting())
            {
                return;
            }

            bool stopActive = IsOrderActive(protectiveStopOrder);
            bool targetActive = IsOrderActive(profitTargetOrder);

            // FULLY protected = BOTH a working stop AND a working target. A stop alone is not
            // enough (Steve, 2026-09-09, and he is right): EMAL's edge IS the bracket geometry
            // - breakeven is 81.8% at TP4/SL18 - so a position that has lost its target can no
            // longer take the +TP and can only exit at the full stop or at an EOD/session
            // flatten. Its favourable outcome has been deleted while the unfavourable one
            // remains. That is not "capped and safe", it is a position with only downside
            // left. It also matches the file's own long-standing MissingTarget behaviour,
            // which flattens.
            if (stopActive && targetActive)
            {
                // EMAL-1068: log the CLEAR with elapsed duration - this is the measurement.
                if (unprotectedSinceUtc != DateTime.MinValue)
                {
                    Print(string.Format(CultureInfo.InvariantCulture,
                        "{0} | EMAL NAKED AUDIT | fault clock CLEAR after {1:F2}s (no flatten) "
                        + "| position={2} qty={3}",
                        lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                        (DateTime.UtcNow - unprotectedSinceUtc).TotalSeconds,
                        Position.MarketPosition, Position.Quantity));
                }
                unprotectedSinceUtc = DateTime.MinValue;
                return;
            }

            // First observation of the fault - start the clock, decide nothing yet. This is
            // what debounces a genuine momentary gap. (The EMAL-1046 multi-contract resize was
            // originally cited here as that transient; it is NOT one - it uses ChangeOrder, an
            // in-place amend, so no gap opens. Corrected 2026-09-09.)
            if (unprotectedSinceUtc == DateTime.MinValue)
            {
                unprotectedSinceUtc = DateTime.UtcNow;
                // EMAL-1068: log the clock START, not just the firing. Without this a run with
                // zero firings cannot distinguish "worst fault lasted 0.2s, 50x margin" from
                // "worst fault lasted 9.8s and nearly fired" - so it could not validate the
                // grace period at all. With it, the start/clear pair yields the observed
                // fault-duration distribution, which is the only thing that can set the grace
                // on evidence rather than judgement.
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | EMAL NAKED AUDIT | fault clock START | stopActive={1} targetActive={2} "
                    + "position={3} qty={4} grace={5}s",
                    lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                    stopActive, targetActive, Position.MarketPosition, Position.Quantity,
                    NakedPositionGraceSeconds));
                return;
            }

            // The fault must PERSIST for the whole grace window, uninterrupted.
            if ((DateTime.UtcNow - unprotectedSinceUtc).TotalSeconds < NakedPositionGraceSeconds)
                return;

            nakedAuditFirings++;
            Print(string.Format(CultureInfo.InvariantCulture,
                "{0} | EMAL NAKED AUDIT | protection INCOMPLETE for {1}s - FLATTENING | "
                + "stopActive={2} targetActive={3} position={4} qty={5} entry={6:F2} firing#{7}",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                NakedPositionGraceSeconds, stopActive, targetActive,
                Position.MarketPosition, Position.Quantity,
                openEntryPrice > 0.0 ? openEntryPrice : Position.AveragePrice,
                nakedAuditFirings));

            TrySubmitTerminalExit("NakedPositionAudit", protectedEntrySignal);
        }

        // EMAL-1062 (Steve, 2026-09-04): ROOT-CAUSE FIX for the 2026-09-03 live-account naked-
        // position incident (three accounts, three separate firings: 18:00:00 EMALExitGapTarget,
        // 16:55:00 EMALExitPreCloseFlatten, 11:04 an unnamed order alongside a legitimate entry -
        // all the same underlying defect). Before this cut, every reason string routed through
        // here (GapStop, GapTarget, PreCloseFlatten, NewsBlockFlatten, CashOpenFlatten,
        // MaxAccountBalance, MaxDailyProfit, MissingStop, MissingTarget, ProtectiveReject)
        // submitted its market ExitLong/ExitShort IMMEDIATELY, while the position's own resting
        // protectiveStopOrder and profitTargetOrder were STILL WORKING at the broker (this file
        // has never OCO-linked them - confirmed by grep, zero hits for "Oco" anywhere in this
        // source). If the market order and a resting protective order both filled - the market
        // order flattening the position, then the resting order filling moments later against an
        // already-flat book - NinjaTrader's managed engine has nothing left to net against and
        // opens a BRAND NEW position in the opposite direction, with no stop/target attached
        // (SubmitOrUpdateProtection only arms protection from the normal entry-fill path, which
        // this new position never went through). This is the exact race the target-touch
        // watchdog's own SubmitTargetTouchMarketExit comment already names: "a limit can fill
        // between the cancel request and the broker processing it, and firing both would flip
        // the position." That fix was applied to the watchdog's one call site in EMAL-1045 and
        // never generalized to this method's nine. This cut generalizes it: cancel any resting
        // protective order FIRST, wait for CONFIRMED terminal state on each (via OnOrderUpdate,
        // mirroring the watchdog's own targetTouchWatchdogCancelPending handling), and only THEN
        // submit the market exit - and only if neither resting order filled during the race. If
        // nothing is resting (already cancelled/never armed), the market exit still fires
        // immediately, same as before.
        private void TrySubmitTerminalExit(string reason, string entrySignal)
        {
            if (terminalExitPending || terminalExitCancelPending
                || targetTouchWatchdogCancelPending || IsTerminalExitRetryWaiting())
            {
                return;
            }

            MarketPosition positionDirection = Position.MarketPosition;
            if (positionDirection == MarketPosition.Flat)
                return;

            string fromEntrySignal = string.IsNullOrEmpty(entrySignal)
                ? protectedEntrySignal
                : entrySignal;

            bool stopActive = IsOrderActive(protectiveStopOrder);
            bool targetActive = IsOrderActive(profitTargetOrder);

            Print(string.Format(
                "{0} | EMAL EXIT TRACE | terminal exit requested | reason={1} position={2} qty={3} "
                + "entry={4} stopActive={5} targetActive={6}",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                reason ?? string.Empty, positionDirection, Position.Quantity, fromEntrySignal,
                stopActive, targetActive));

            if (!stopActive && !targetActive)
            {
                // Nothing resting to race against - safe to fire immediately.
                SubmitTerminalExitMarketOrder(reason, positionDirection, fromEntrySignal);
                return;
            }

            terminalExitCancelPending = true;
            terminalExitCancelPendingSinceUtc = DateTime.UtcNow;   // EMAL-1069: starts the breaker
            terminalExitCancelReason = reason ?? string.Empty;
            terminalExitCancelEntrySignal = fromEntrySignal;
            terminalExitCancelDirection = positionDirection;
            terminalExitStopCancelDone = !stopActive;
            terminalExitTargetCancelDone = !targetActive;
            terminalExitStopFilled = false;
            terminalExitTargetFilled = false;

            RecordNtOrderAction("terminal-exit-cancel-protective-" + (reason ?? string.Empty));
            if (stopActive)
                CancelOrder(protectiveStopOrder);
            if (targetActive)
                CancelOrder(profitTargetOrder);
        }

        // Called only once BOTH resting protective orders (whichever were active) have reached a
        // CONFIRMED terminal OrderState - see the OnOrderUpdate hook that drives
        // terminalExitCancelPending. Mirrors SubmitTargetTouchMarketExit's own placement and
        // reasoning exactly.
        private void SubmitTerminalExitMarketOrder(string reason, MarketPosition positionDirection,
            string fromEntrySignal)
        {
            terminalExitPending = true;
            CancelRemainingEntryAfterExit();

            string exitSignal = TerminalExitSignalPrefix + reason;

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
            unprotectedSinceUtc = DateTime.MinValue;   // EMAL-1068 finding 5: reset with the rest of the per-trade protection state, not only via the audit's own Flat branch
            terminalExitPending = false;
            // EMAL-1062: per-trade state, must not survive into the next trade, same reasoning
            // as ResetGapLatchTracking's own comment on the watchdog fields below.
            terminalExitCancelPending = false;
            terminalExitCancelPendingSinceUtc = DateTime.MinValue;   // EMAL-1069
            terminalExitCancelReason = string.Empty;
            terminalExitCancelEntrySignal = string.Empty;
            terminalExitCancelDirection = MarketPosition.Flat;
            terminalExitStopCancelDone = false;
            terminalExitTargetCancelDone = false;
            terminalExitStopFilled = false;
            terminalExitTargetFilled = false;
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
            gapTargetBreachObserved = false;   // EMAL-1070
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

        // EMAL-1070: the "1500 requests" literal was the SECOND site sized against the refuted
        // 1500 figure, and the more consequential one - this method is the ONLY trigger for
        // MarkProviderRateLimit, i.e. the only thing that arms the one-hour entry cooldown the
        // raised ceiling's safety margin depends on. If Tradovate's rejection text quotes the
        // real number ("Exceeded 5000 requests per hour") and happens not to contain
        // "rate limit" or "too many requests", detection would fail silently and EMAL would
        // keep submitting entries into a rejecting endpoint. Both numeric literals are kept:
        // they are independent belt-and-braces matches, and the 1500 one costs nothing if the
        // provider never emits it. ANY FUTURE CHANGE TO THE PROVIDER LIMIT MUST UPDATE THIS
        // LIST as well as the default and the hover text - all three are the same fact.
        private bool IsProviderRateLimitRejection(string comment)
        {
            string text = comment ?? string.Empty;
            return text.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("5000 requests", StringComparison.OrdinalIgnoreCase) >= 0
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
                // EMAL-1069: was DateTime.MaxValue, which made IsTerminalExitRetryWaiting()
                // true FOREVER - and that gate sits BEFORE the SubmitOrUpdateProtection restore
                // in EvaluateTerminalExitRecovery, so exhaustion permanently prevented
                // protection from ever being re-armed, not merely further exit attempts.
                //
                // BEHAVIOURAL CHANGE, STATED EXPLICITLY (do not describe this as "exits stay
                // stopped" - that is false). TrySubmitTerminalExit's own guard checks
                // terminalExitPending / terminalExitCancelPending / targetTouchWatchdogCancelPending
                // / IsTerminalExitRetryWaiting() - it does NOT check terminalExitRetryCount. So
                // once the due time lands in the past, ALL terminal-exit call sites reopen for
                // that tick, not just the one inside EvaluateTerminalExitRecovery (whose own
                // retryCount check at the bottom of this method guards only itself). Under 1068
                // exhaustion killed every emergency exit permanently; under 1069 they become
                // reachable roughly once per TerminalExitExhaustedRestoreSeconds.
                //
                // That is judged the safer state - 1068's alternative was a position that could
                // never be exited by any mechanism, ever - and every such exit still routes
                // through cancel-then-confirm, so no naked-position path is added. But it IS a
                // change on a safety-critical path and Andreas must review it as such.
                terminalExitRetryDueUtc = DateTime.UtcNow.AddSeconds(TerminalExitExhaustedRestoreSeconds);
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
                // EMAL-1069: also drop a still-open cancel-then-confirm window when we observe
                // FLAT. ResetProtectionTracking is the only other place this clears, and it has
                // exactly one caller which fires only when the position goes flat via one of
                // four recognised order names (EMALStop/EMALTarget/EMALTouchExit/EMALExit*).
                // NT8's OWN session-close flatten (IsExitOnSessionCloseStrategy is a shipped
                // default, governed by the CHART'S Trading Hours Template, not EMAL's sessions)
                // uses none of those names - nor does a manual or broker-side flatten. So the
                // latch survived into the NEXT trade and silently no-opped every emergency exit
                // there, including MissingStop. The 2026-09-09 audit identified this as a
                // second, independent route to the same stuck latch, and as the one that best
                // explains why the fault is intermittent and self-clearing. Clearing per-trade
                // state while FLAT cannot endanger an open position - there isn't one.
                if (terminalExitCancelPending)
                {
                    Print(string.Format(CultureInfo.InvariantCulture,
                        "{0} | EMAL-1069 | clearing stale terminal-exit cancel window observed while FLAT "
                        + "| reason={1} - position closed by a path that does not reach ResetProtectionTracking",
                        lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                        terminalExitCancelReason));
                }
                terminalExitCancelPending = false;
                terminalExitCancelPendingSinceUtc = DateTime.MinValue;
                terminalExitStopCancelDone = false;
                terminalExitTargetCancelDone = false;
                terminalExitStopFilled = false;
                terminalExitTargetFilled = false;

                ClearTerminalExitRetry();
                return;
            }

            // ---- EMAL-1069 BREAKER: bound the cancel-then-confirm window --------------------
            // Deliberately BEFORE the terminalExitRetryReason guard below: on a first-attempt
            // exit that reason is still empty, so a stuck cancel would never be reached by any
            // code path at all. That is the permanent-latch case.
            //
            // On timeout we do NOT fire a market exit. At least one cancel confirmation never
            // arrived, so we cannot know whether that protective order is genuinely cancelled
            // or still working at the broker - and firing a market order against a live
            // protective order with no OCO is exactly the 2026-09-03 mechanism. Instead we
            // clear the latch and hand over to ScheduleTerminalExitRetry, the existing tested
            // path.
            //
            // WHAT THIS ACTUALLY ACHIEVES - stated precisely, because "it restores protection"
            // is NOT true in the common case. The stuck state IS "CancelOrder was sent and the
            // confirming OnOrderUpdate never arrived", so the local Order object never left
            // Working/CancelPending and IsOrderActive still reports TRUE. SubmitOrUpdateProtection
            // therefore takes its ChangeOrder branch with unchanged price and quantity, which is
            // a no-op. Control then reaches TrySubmitTerminalExit, which sees the leg still
            // active, re-latches, and re-issues CancelOrder on the same stuck order.
            //
            // So the real effect is: a PERMANENT latch becomes a BOUNDED ladder of at most 8
            // re-cancel attempts (2/5/15/30/60/120/300/300s, each preceded by a 30s stuck
            // window, ~18 minutes total) plus a CRITICAL log line naming the condition.
            // Protection is genuinely re-armed only in the PARTIAL subcase - one leg confirmed
            // terminal, the other lost - where IsOrderActive is false for that leg and a fresh
            // order is submitted safely against a terminal reference.
            //
            // Resubmitting a protective order against an apparently-LIVE one is deliberately
            // refused: that risks two working stops and is the wrong trade-off when the broker
            // state is unknown.
            if (terminalExitCancelPending
                && terminalExitCancelPendingSinceUtc != DateTime.MinValue
                && Position.MarketPosition != MarketPosition.Flat
                && (DateTime.UtcNow - terminalExitCancelPendingSinceUtc).TotalSeconds
                       >= TerminalExitCancelTimeoutSeconds)
            {
                string stuckReason = terminalExitCancelReason;
                string stuckEntrySignal = terminalExitCancelEntrySignal;
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | CRITICAL: terminal-exit cancel confirmation NOT received in {1}s - "
                    + "breaking stuck latch and restoring protection | reason={2} stopDone={3} "
                    + "targetDone={4} stopActive={5} targetActive={6} position={7} qty={8}",
                    lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                    TerminalExitCancelTimeoutSeconds, stuckReason,
                    terminalExitStopCancelDone, terminalExitTargetCancelDone,
                    IsOrderActive(protectiveStopOrder), IsOrderActive(profitTargetOrder),
                    Position.MarketPosition, Position.Quantity));

                terminalExitCancelPending = false;
                terminalExitCancelPendingSinceUtc = DateTime.MinValue;
                terminalExitStopCancelDone = false;
                terminalExitTargetCancelDone = false;
                terminalExitStopFilled = false;
                terminalExitTargetFilled = false;

                ScheduleTerminalExitRetry(stuckReason, stuckEntrySignal, false);
                return;
            }
            // ---- end EMAL-1069 breaker ------------------------------------------------------

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
            else if (terminalExitRetryCount > MaxTerminalExitRetries)
            {
                // EMAL-1069: exits are exhausted and stay exhausted - but keep waking so the
                // protection restore above continues to run. Without this the due time would
                // stay in the past and the restore would re-run on every tick.
                terminalExitRetryDueUtc = DateTime.UtcNow.AddSeconds(TerminalExitExhaustedRestoreSeconds);
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
        // EMAL-1070 (2026-09-10, Steve): default raised 1100 -> 4000. The old default and its
        // hover text were sized against an "observed 1500-request" Tradovate limit; the actual
        // Apex/Tradovate ceiling is 5000 PER HOUR (rolling, 60-minute cool-down), so 1100 was
        // throttling EMAL ~4.5x below the real constraint. 4000 keeps a 1000-request margin.
        //
        // MEASURED, not assumed (NT8 Output, 2026-09-09, 14 concurrent accounts, one Tradovate
        // connection): the shared counter reached 1102 by 10:17:59 and blocked 93 entries
        // across 17 instances still on the 1100 default, while 4 instances already set to 3000
        // blocked NONE. Adding the blocked entries back gives ~1660 actions/hour for 14
        // accounts, ~119 per account. Scaled to 20 accounts that is ~2370/hour - so at 4000
        // the guard is a genuine anomaly backstop, not a working limit. Zero provider-side
        // rejections were observed.
        //
        // WHY THE MARGIN BELOW 5000 IS THE SAFETY-CRITICAL PART, and why this must NOT be
        // raised further: Apex and Tradovate both state that reaching the PROVIDER limit does
        // NOT flatten open positions, and that while limited you may be unable to place,
        // modify, cancel or exit an order or position - for up to a 60-minute cool-down. That
        // is an externally-imposed naked-position hazard EMAL cannot defend against, because
        // SubmitOrUpdateProtection's own calls are what get rejected. The two failure modes
        // are therefore NOT symmetrical:
        //   LOCAL ceiling  -> blocks only the marginal ENTRY, bumps rateGuardBlockedEntryCount.
        //   PROVIDER limit -> MarkProviderRateLimit blocks every EMAL entry on the connection
        //                     for a FULL HOUR (DateTime.UtcNow.AddHours(1)), AND the broker may
        //                     refuse the protective/exit orders EMAL needs to stay safe.
        // The local guard exists to keep EMAL away from that cliff. Range still caps at 5000;
        // a user who types 5000 has zero headroom by construction. Note also that Tradovate
        // enforces the limit BY IP, and additionally at minute and second resolution - EMAL
        // has no brake at those resolutions, and this hourly ceiling does not create or
        // mitigate that exposure, which scales with ACCOUNT COUNT, not with this value.
        //
        // **OPERATIONAL RULE - DIVIDE THIS SETTING BY THE NUMBER OF TRADOVATE CONNECTIONS ON
        // THIS IP.** GetOrderRateGuardKey() keys the shared counter on Account.Connection, but
        // Tradovate enforces the 5000/hour BY IP. Two NT8 connections from one machine are two
        // independent local counters spending one shared provider budget: at 4000 that is 8000
        // against 5000, i.e. over the cliff with the guard never firing. The 1100 default was
        // accidentally safe under this (2 x 1100 = 2200); 4000 is not.
        //
        // ---------------------------------------------------------------------------------
        // The block below documents a DIFFERENT property (EnableGapTargetLatch, declared after
        // OrderActionLimitPerHour). Everything above this line describes Order Actions / Hour.
        // ---------------------------------------------------------------------------------
        // EMAL-1070 (2026-09-10, Steve's explicit decision): the EMAL-1041 pre-fill entry-cancel
        // gap latch becomes user-controllable and SHIPS OFF, so it can be A/B tested live.
        //
        // **THIS IS A SAFETY MECHANISM AND IT NOW DEFAULTS TO DISABLED. READ BEFORE CHANGING.**
        // 1041 made this mandatory because of a fill-then-immediate-loss pattern found in the
        // 2026-08-13 LIVE review: a still-working entry whose target the market has already
        // passed can fill at a price the move has left, leaving only the stop ahead of it.
        //
        // WHY IT SHIPS OFF ANYWAY: WBGQF (Analysis_Plan §59) measured the cancelled cohort as
        // the BEST in the book on every metric - net, win rate and drawdown all improve
        // monotonically as the latch is loosened, with no interior optimum and the maximum at
        // "do not cancel". Steve's decision, taken 2026-09-10 with the counter-argument stated:
        // the cohort wins 98-99% of the time, which is the signature of an ENGINE FILL ARTIFACT
        // rather than an edge, and TNVQZ has already shown the engine's exit fills are fictional
        // (0 of 43 funded target exits filled through live, against ~4 expected). The entry leg
        // is untested and this live A/B is how it gets tested.
        //
        // SCOPE - CORRECTED 2026-09-10 after review; an earlier draft of this comment said the
        // opposite and was wrong. With this FALSE, gapTargetBreached is NEVER SET, so BOTH the
        // pre-fill entry cancel AND SubmitOrUpdateProtection's post-fill "target crossed before
        // protection | flattening" branch are inert. **The post-fill TARGET backstop is GONE,
        // not unchanged.** That is deliberate and is the whole point: gating only the cancel
        // left every suppressed entry filling and then being market-flattened for a guaranteed
        // scratch, which is not the arm WBGQF measured (EMAL-1066 gates the FLAG - 1066:2691/2715).
        //
        // THE STOP SIDE IS UNAFFECTED. gapStopBreached is still set unconditionally and
        // TrySubmitTerminalExit("GapStop") still fires. That is the genuinely dangerous case -
        // a fill into an already-breached stop triggers immediately and can fill well beyond the
        // 18 points sized for. The target side is benign by comparison: the position gets a
        // NORMAL stop+target bracket and is bounded by its ordinary stop.
        //
        // KNOWN CONSEQUENCE, not covered by WBGQF: plannedTargetTouchLevel is limit +/- TP and
        // has ALREADY been crossed at fill time for this whole cohort, so EvaluateTargetTouchWatchdog
        // latches immediately and, if the (marketable) target limit has not filled inside
        // TargetTouchGraceMs, cancels it and routes to SubmitTargetTouchMarketExit. In 1069 this
        // cohort could never reach that path. It is NOT a naked-position path (the stop is still
        // working and the exit fires from the target's confirmed terminal-cancel callback), but it
        // is the path with the open JVKTX <=0 touch-exit defect, and its LIVE rate is unknown -
        // Playback fills first-touch-wins and will essentially never trigger it.
        //
        // SET IT BACK TO TRUE TO RESTORE EMAL-1069 BEHAVIOUR - no rebuild needed.
        [NinjaScriptProperty]
        [Display(Name = "Gap Latch: Target Side (1041)", Description = "TRUE = 1069 behaviour: cancel a still-working entry the moment the market passes its planned target, and flatten post-fill if it filled anyway. FALSE = take the trade with a NORMAL stop+target bracket. The STOP-side gap protection is unaffected either way. EMAL-1073: ships TRUE (1069 behaviour restored as the standing default) - the 2026-09-10/09-11 live A/B this shipped FALSE for has already run and closed out. Set FALSE only to re-run that A/B intentionally.", GroupName = "C. Risk", Order = 12)]
        public bool EnableGapTargetLatch { get; set; }

        [Range(NewTradeActionReserve, 5000), NinjaScriptProperty]
        [Display(Name = "Order Actions / Hour", Description = "Conservative local EMAL action ceiling per NT8 connection (shared across every strategy instance on it). Default 4000 leaves headroom below Tradovate's 5000-per-hour provider limit. Exceeding the PROVIDER limit blocks all entries for a 60-minute cool-down AND can leave you unable to modify or exit open positions, which Tradovate does NOT auto-flatten - so keep a wide margin and never set this at or near 5000.", GroupName = "C. Risk", Order = 3)]
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
        // EMAL-1052 (Steve, 2026-08-23): hidden from the property grid. Default stays true
        // (unchanged) - visibility only, no behavior change.
        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Target Touch Watchdog", Description = "When ON, if price trades at or beyond the working target's limit price but the target hasn't filled within Target Touch Grace (ms), cancels the target and exits at market (signal EMALTouchExit) once the cancel confirms unfilled. Never fires a second exit if the target fills during or after the grace window - the cancel confirmation is always awaited first. OFF reverts to pure-limit target behavior, no code-level difference from a prior cut.", GroupName = "C. Risk", Order = 5)]
        public bool EnableTargetTouchWatchdog { get; set; }

        // EMAL-1052 (Steve, 2026-08-23): hidden from the property grid. Default stays 400
        // (unchanged) - visibility only, no behavior change.
        [Range(100, 5000), NinjaScriptProperty]
        [Browsable(false)]
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
        // EMAL-1052 (Steve, 2026-08-23): hidden from the property grid, fixed at QuoteOrLast.
        // Default unchanged - visibility only, no behavior change.
        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Touch Detection Mode", Description = "QuoteOrLast (default): the target-touch watchdog and gap latch trigger on either a Last trade AT the level or the relevant quote (Bid for Long, Ask for Short) reaching it - catches a fleeting one-tick touch even with no Last print exactly there. LastOnly: Last-trade detection only, byte-identical to the prior cut, for A/B comparison or rollback.", GroupName = "C. Risk", Order = 7)]
        public EMALTouchDetectionMode TouchDetectionMode { get; set; }

        // ================================================================================
        // EMAL-1067 (Steve, 2026-09-09) - RECURRING NAKED-POSITION AUDIT
        // ================================================================================

        [NinjaScriptProperty]
        [Display(Name = "Naked Position Audit", Description = "EMAL-1068. Recurring safety net: if an OPEN position is missing EITHER its working stop OR its working target continuously for the grace period below, flatten it. Closes the gap left by the existing MissingStop/MissingTarget checks, which only run when protection is first submitted and therefore cannot catch an order that was accepted and later vanished (broker cancel, connection blip, order pulled). BOTH legs are required because EMAL's edge is the bracket geometry: a position that has lost its target can no longer take the +TP and can only exit at the full stop or an EOD flatten, i.e. its favourable outcome is gone while the unfavourable one remains. Leave ON.", GroupName = "C. Risk", Order = 10)]
        public bool EnableNakedPositionAudit { get; set; }

        [Range(3, 300), NinjaScriptProperty]
        [Display(Name = "Naked Position Grace (seconds)", Description = "EMAL-1068. How long protection must be CONTINUOUSLY incomplete before the audit flattens. The clock starts when the fault is first OBSERVED and resets the moment both legs are seen working again, so a genuine momentary gap is debounced rather than acted on, while protection that never attached at all still fires (that is simply a fault seen on first sighting). Must exceed a normal broker acknowledgement. 10s is a JUDGEMENT CALL, not a measurement - the fault clock START/CLEAR lines in the Output window record the observed fault-duration distribution, and the default should be reset to a large multiple of the observed maximum once that data exists. Realtime only.", GroupName = "C. Risk", Order = 11)]
        public int NakedPositionGraceSeconds { get; set; }

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
        [Display(Name = "Multi-Contract Protection Fix", Description = "Off (default): no effect, byte-identical to the prior cut on every account. Auto: reconciles the stop/target order quantity to Position.Quantity whenever they differ, but ONLY once position quantity exceeds 1 - single-contract accounts are unaffected even when this is set. On: same reconciliation, active at any quantity (for testing; harmless no-op at qty 1). Fixes a partial-fill resize race where Tradovate can reject a resize submitted before the protective order reaches Working state.", GroupName = "B. Sessions", Order = 1)]
        public EMALMultiContractProtectionFix MultiContractProtectionFix { get; set; }

        // EMAL-1050 (Steve, 2026-08-21; extended same day to also cover 09:43): originally an
        // OFF-by-default optional toggle covering 09:31-09:35 AND 09:43. CHANGED 2026-08-22
        // (Steve): defaults ON and hidden from the property grid - same "hardcode to enabled,
        // remove the user option" treatment this file already gives the order rate guard and the
        // gap-breach entry cancellation. CHANGED AGAIN 2026-08-28 (Steve): the US 09:36-09:55
        // window's start moved to 09:36, making 09:31-09:35 structurally unreachable by any
        // session (nothing opens before 09:36 any more) - that portion is deleted from
        // IsAdditionalBlockedMinute, and this property is renamed Block0931To0935 -> Block0943
        // to match; it now covers ONLY 09:43. The always-on 09:30 hard block that used to sit
        // above this one in IsEntryWindowOpen is deleted outright for the same reason.
        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Block 9:43", Description = "Blocks entries during the 09:43 ET minute. Always on, hidden - see the field comment above.", GroupName = "C. Risk", Order = 9)]
        public bool Block0943 { get; set; }

        // ================================================================================
        // EMAL-1051 (Steve, 2026-08-22): four new sessions, found and validated from the
        // EMAL-1049 QNRVX full-day research log (26,620 trades, Apr26-Aug21) via offline
        // path-log reconstruction, four-halves validated (sequential + interleaved), on
        // live-vs-Playback-adjusted numbers (measured stop-slippage and touch-exit-shortfall
        // biases applied before selection - not raw Playback-optimistic numbers). Each is a
        // single "Setting" popup, "Disabled" first, exactly like the original two US
        // sessions' Us0928Setting/Us0955Setting - Steve's explicit instruction: 6 popups
        // total (2 original + 4 new), no separate editable TP/SL/slope fields, no exposed
        // start/stop time properties either (those are fixed consts, same as the original
        // two sessions' Us0928StartMinute/Us0928EndMinute). The original two sessions are
        // completely UNCHANGED and were never tuned as part of this work. Slope is fixed at
        // 2.75 for all four new sessions, baked into each one's only real preset, same as
        // how the original two sessions bake their slope into their presets.
        // Two gaps found in the data and deliberately left uncovered by any session:
        // 06:30-08:00 ET (a real, decisive failure in the QNRVX scan, not a marginal one) and
        // 17:00-18:00 ET (the CME daily maintenance break - no trade data exists there at all).
        // Full detail, including the four-halves numbers and the judgment calls behind each
        // window's exact boundaries: EMAL-1051-changelog.txt.
        // ================================================================================

        [NinjaScriptProperty]
        [Display(Name = "Asia 18:00-3:00 Setting", Description = "TP4_SL20_Slope2_75 WR86.17% PF1.200 Net$89,088 MaxDD$3,815 Net/DD23.35\n\nPlayback-reconstruction, CVYFX Apr26-Aug21, 1-tick grid / 1800s horizon, live-vs-Playback adjusted.", GroupName = "B. Sessions", Order = 2)]
        public EMALAsiaSetting AsiaSetting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Asia Min Slope", Description = "Driven by the Asia Setting preset; not user-editable.", GroupName = "B. Sessions", Order = 3)]
        public double AsiaMinimumSlope { get; set; }

        // EMAL-1052 (Steve, 2026-08-23): hidden from the property grid. Default stays Disabled
        // (unchanged) - this is a visibility change only, not a behavior change. Motivated by
        // the CVYFX SL-sweep (Analysis_Plan §31): Europe's shipped bracket has no statistically
        // established edge net of commission (day-level t=0.40, vs 2.91-9.18 for every other
        // session) and the worst Net/MaxDD of any session (1.52). Not deleted - the property,
        // its enum, and ResolveWindowPresets' case for it are all untouched, so it can be made
        // visible again with a one-line revert if the edge question is ever resolved.
        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Europe 3:00-6:30 Setting", Description = "TP8_5_SL20_Slope2_75 WR71.73% PF1.045 Net$12,838 MaxDD$3,252 Net/DD3.95 - notably wider TP than the NY sessions' TP4/TP3 (smoothness originally validated on the QNRVX scan). Hidden 2026-08-23 (EMAL-1052): net of the standard $3.10/trade commission this edge is not statistically distinguishable from zero (day-level t=0.40) and carries the worst Net/MaxDD of any session - see Analysis_Plan §31.\n\nPlayback-reconstruction, CVYFX Apr26-Aug21, 1-tick grid / 1800s horizon, live-vs-Playback adjusted.", GroupName = "B. Sessions", Order = 7)]
        public EMALEuropeSetting EuropeSetting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Europe Min Slope", Description = "Driven by the Europe Setting preset; not user-editable.", GroupName = "B. Sessions", Order = 8)]
        public double EuropeMinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US Pre-Market 8:00-9:28 Setting", Description = "TP11_SL20_Slope2_75 WR69.96% PF1.244 Net$37,645 MaxDD$2,842 Net/DD13.25\n\nBlocked times 8:28-8:32 (unconditional news-release block, applies regardless of session). Any open position force-closed at 8:29 and again at 9:29 (both unconditional, apply regardless of session).\n\nPlayback-reconstruction, CVYFX Apr26-Aug21, 1-tick grid / 1800s horizon, live-vs-Playback adjusted.", GroupName = "B. Sessions", Order = 13)]
        public EMALPreMarketSetting PreMarketSetting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US Pre-Market Min Slope", Description = "Driven by the US Pre-Market Setting preset; not user-editable.", GroupName = "B. Sessions", Order = 14)]
        public double PreMarketMinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US Midday 10:30-17:00 Setting", Description = "TP3_SL13_Slope2_75 WR86.39% PF1.385 Net$136,872 MaxDD$2,831 Net/DD48.35\n\nBlocked times 16:55-17:00.\n\nPlayback-reconstruction, CVYFX Apr26-Aug21, 1-tick grid / 1800s horizon, live-vs-Playback adjusted.", GroupName = "B. Sessions", Order = 23)]
        public EMALUSMiddaySetting USMiddaySetting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US Midday Min Slope", Description = "Driven by the US Midday Setting preset; not user-editable.", GroupName = "B. Sessions", Order = 24)]
        public double USMiddayMinimumSlope { get; set; }

        // EMAL-1051 (fifth change, 2026-08-22, Steve): last setting in the group, below all six
        // sessions. See IsPreCloseWindow's comment for the full rationale. Default 16:55 ET (5
        // minutes ahead of the 17:00 CME halt); lower it for prop firms with a stricter
        // requirement (e.g. Topstep enforces flat by 16:00 ET).
        [NinjaScriptProperty]
        [Display(Name = "EOD Force Close", Description = "All entries are blocked and any open position is force-flattened at market from this time until the 17:00 ET CME daily maintenance halt. Default 16:55 (5 minutes ahead of the halt). Lower it for prop firms with a stricter flat-by requirement, e.g. Topstep enforces 16:00 ET.", GroupName = "B. Sessions", Order = 29)]
        public TimeSpan EODForceCloseTime { get; set; }

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
        [TypeConverter(typeof(EMALLivePresetConverter))]
        [Display(Name = "US 9:36-9:55 Setting", Description = "S1_TP4_SL18_Slope5_25_Win3, RR4.50 - the only preset; S2_TP3_SL18 was removed in EMAL-1077.\nWR87.87% PF1.613 Net$28,727 MaxDD$1,906 Net/DD15.07 AvgDaily$273.59, 1072 trades\nvs the EMAL-1076 shipped S1 (Slope2.75, 1-bar window) measured on the SAME capture: WR87.21% PF1.513 Net$26,139 MaxDD$2,060 Net/DD12.69 AvgDaily$248.94, 1102 trades - so +$2,588 net and -$154 MaxDD.\n\nSLOPE CALCULATION - CHANGED IN EMAL-1077. This session now judges the EMA slope over a TWO-BAR window, ema[1]-ema[3], instead of the one-bar ema[1]-ema[2] every cut through EMAL-1076 used. Win3 in the preset name is that window: N is the INDEX of the older EMA bar, so N=3 spans 2 bar intervals and the shipped N=2 spans 1. The threshold moves with it - 5.25 over two bars is NOT comparable to 2.75 over one, and reading it as a big tightening is the easy mistake. Only P1 and P2 use N=3; every other session stays at N=2.\n\nSource: RVKTM S=3 real-fill NT8 Playback capture, Apr27-Sep18 2026, 105 days, live brackets (Analysis_Plan §72.10/§75). NOT the CVYFX/MXQFL captures earlier presets quoted - do not compare absolute Net/MaxDD across captures.\n\nHONEST CAVEAT, read before trusting the numbers: this threshold was chosen from a 320-cell sweep and is NOT statistically distinguishable from the old setting. The nearest cell that was formally graded (S=3 @ 4.50 applied to BOTH sessions, not this per-session pair) showed funded dNet +$1,815 over 105 days, 95% CI [-$7,458, +$11,403], i.e. +$17.28/day at t=0.395 - so demonstrating it is real would need roughly 16 years of data (§75.6). No null was ever run on THIS per-session pairing specifically, which carries more post-hoc search than the graded cell, not less. Adopted 2026-09-25 as Steve's judgment call on the drawdown reduction - the same kind of call as the S4 adoption on P2 - not as a demonstrated edge.\n\nBlocked time 9:43 (always on).", GroupName = "B. Sessions", Order = 19)]
        public EMALUs0928Setting Us0928Setting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:36-09:55 Min Slope", Description = "Driven by the US 09:36-09:55 Setting preset; not user-editable. EMAL-1077: 5.25, measured over a TWO-bar window (ema[1]-ema[3]), so it is not comparable to the 2.75 one-bar threshold used through EMAL-1076.", GroupName = "B. Sessions", Order = 20)]
        public double Us0928MinimumSlope { get; set; }

        [NinjaScriptProperty]
        [TypeConverter(typeof(EMALLivePresetConverter))]
        [Display(Name = "US 9:55-10:30 Setting", Description = "S1_TP3_75_SL18_Slope3_50_Win3, RR4.80 - the only preset; S1_TP4/S2_TP3/S3_TP3_SL16 and the old S4 were removed in EMAL-1077. Brackets are unchanged from the S4 you were running.\nWR88.89% PF1.674 Net$57,118 MaxDD$1,769 Net/DD32.29 AvgDaily$543.98, 2125 trades\nvs the EMAL-1076 shipped S4 (TP3.75/SL18, Slope2.75, 1-bar window) measured on the SAME capture: WR89.38% PF1.764 Net$54,539 MaxDD$1,842 Net/DD29.60 AvgDaily$519.42, 1873 trades - so +$2,579 net and -$73 MaxDD. Note it nets more by taking 252 MORE trades, not better ones: $/trade falls 29.12 -> 26.88 and PF 1.764 -> 1.674.\n\nSLOPE CALCULATION - CHANGED IN EMAL-1077. This session now judges the EMA slope over a TWO-BAR window, ema[1]-ema[3], instead of the one-bar ema[1]-ema[2] every cut through EMAL-1076 used. Win3 in the preset name is that window: N is the INDEX of the older EMA bar, so N=3 spans 2 bar intervals and the shipped N=2 spans 1. A threshold of 3.50 over two bars is NOT comparable to 2.75 over one. Only P1 and P2 use N=3; every other session stays at N=2.\n\nSource: RVKTM S=3 real-fill NT8 Playback capture, Apr27-Sep18 2026, 105 days, live brackets (Analysis_Plan §72.10/§75). NOT the CVYFX/MXQFL captures earlier presets quoted.\n\nHONEST CAVEAT: chosen from a 320-cell sweep and NOT statistically distinguishable from the old setting (§75.3 - neither cell beats the other under a symmetric null). P2 is also the session where the evidence is THINNEST: the +$2,579 is 58 better days against 39 worse, median day +$71.90, but the top 3 days alone contribute +$2,660 - 103% of the total - and the other 102 days net -$81. Drop 2026-06-03 and the gain falls to +$1,505. Day-block bootstrap P(dNet>0) 74.7%, 95% CI [-$4,580, +$10,281]. §75.5's paired test on the ratio reads lower still (46.8%) and calls P2 'the pivot, and it says no'. Adopted 2026-09-25 as Steve's judgment call with all of that on the table, not as a demonstrated edge.\n\nFUNDED BOOK, P1 S1 + P2 S1 together: Net$85,844 MaxDD$1,948 Net/DD44.06 AvgDaily$817.56, 3197 trades, vs shipped Net$80,677 MaxDD$2,426 Net/DD33.26 AvgDaily$768.36, 2975 trades.", GroupName = "B. Sessions", Order = 21)]
        public EMALUs0955Setting Us0955Setting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:55-10:30 Min Slope", Description = "Driven by the US 09:55-10:30 Setting preset; not user-editable. EMAL-1077: 3.50, measured over a TWO-bar window (ema[1]-ema[3]), so it is not comparable to the 2.75 one-bar threshold used through EMAL-1076.", GroupName = "B. Sessions", Order = 22)]
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
        version_1077,
        modified_2026_09_25
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

    // EMAL-1073 (Steve, 2026-09-19): S3 (P1) REMOVED - EMAL-1071 added it based on a cascade
    // sweep later found to have scored candidates on a censoring-bug-corrupted Net/MaxIDD
    // objective (see emal-work/EMAL_Analysis_Plan.md §64). Corrected, full-book, real-fill
    // re-run found S3 net -$5,618 vs shipped, 0/4 halves - not an improvement, a loss. Reverted
    // to the pre-1071 two-preset list. See EMAL-1071-changelog.txt for S3's original (now
    // superseded) rationale.
    // EMAL-1077. Shows only the members NOT marked [Browsable(false)] in the property-grid
    // dropdown, while EnumConverter's inherited ConvertFrom still parses every member by name -
    // so legacy preset names in saved templates deserialize, but cannot be newly selected.
    public class EMALLivePresetConverter : EnumConverter
    {
        public EMALLivePresetConverter(Type type) : base(type) { }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            List<object> visible = new List<object>();
            foreach (object value in Enum.GetValues(EnumType))
            {
                FieldInfo field = EnumType.GetField(value.ToString());
                if (field == null)
                    continue;
                BrowsableAttribute[] hidden = (BrowsableAttribute[])field.GetCustomAttributes(typeof(BrowsableAttribute), false);
                if (hidden.Length > 0 && !hidden[0].Browsable)
                    continue;
                visible.Add(value);
            }
            return new StandardValuesCollection(visible);
        }

        // TRUE, deliberately: the visible list is exhaustive for SELECTION, so the grid renders a
        // picker rather than an editable combo. It does not affect ConvertFrom, so legacy names in
        // saved templates still parse - and NT8's XmlSerializer bypasses this converter entirely
        // anyway. Returning false would let an operator type free text into a live-config property
        // and hand a FormatException to the grid.
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) { return true; }
    }

    public enum EMALUs0928Setting
    {
        Disabled,

        // LEGACY ALIASES - EMAL-1077. Hidden from the dropdown by EMALLivePresetConverter, but
        // still PARSEABLE, which is the whole point: NT8 persists this property by member NAME
        // into workspace XML and chart/Strategy-Analyzer templates. Steve has saved templates
        // carrying these names. Delete them and NT8 cannot resolve the name on load, leaves the
        // property at its SetDefaults value - Disabled - and the session SILENTLY TAKES NO TRADES
        // on a chart that looks completely normal. ResolveWindowPresets maps them to the live
        // preset, so an old template loads as the live config rather than as nothing.
        // NOTE this means an eval account whose template said S2_TP3_SL18 (RR6.00) now runs the
        // funded-legal RR4.50 preset instead - a real behavior change, deliberate, see changelog.
        [Browsable(false)] S1_TP4_SL18_Slope2_75,
        [Browsable(false)] S2_TP3_SL18_Slope2_75,

        // The live preset is appended LAST so every legacy member keeps the ordinal it had in
        // EMAL-1076. Name-based XML does not care, but any path that ever persists or compares this
        // enum as an int would silently remap old values if the new member were inserted at 1.
        S1_TP4_SL18_Slope5_25_Win3
    }

    // HISTORICAL (EMAL-1073, superseded by EMAL-1077 - P2 now has ONE live preset plus hidden
    // legacy aliases; the S4 described below is now one of those aliases, not a selectable option).
    // EMAL-1073 (Steve, 2026-09-19): S4-S7 (P2) REMOVED - same §64 finding as P1's S3 above.
    // Corrected re-run: S4 -$15,400, S5 -$11,722, S6 -$6,485, S7 -$8,911 vs shipped, all 0/4
    // halves. Reverted to the pre-1071 three-preset list. See EMAL-1071-changelog.txt for
    // S4-S7's original (now superseded) rationale.
    // EMAL-1073 (Steve, 2026-09-19): S4_TP3_75_SL18_Slope2_75 ADDED - funded-legal (RR4.80 <=
    // 5.00) alternative to S1 that trades ~1% of net profit for ~30% lower max drawdown.
    // Full emal-analyst adoption-gate validation on the MXQFL real-fill capture (105 days) -
    // see Analysis_Plan §67. Not a discovered edge, a deliberate risk/reward choice: rejected
    // under the "beats the field" adoption bar but adopted here on Steve's explicit instruction
    // as a genuine, reproducible bracket-geometry trade-off, not noise.
    public enum EMALUs0955Setting
    {
        Disabled,

        // LEGACY ALIASES - see the EMALUs0928Setting comment above for why these must not be
        // deleted. S4_TP3_75_SL18_Slope2_75 is the one Steve actually runs live today, so it is
        // the alias that matters most on install.
        [Browsable(false)] S1_TP4_SL18_Slope2_75,
        [Browsable(false)] S2_TP3_SL18_Slope2_75,
        [Browsable(false)] S3_TP3_SL16_Slope2_75,
        [Browsable(false)] S4_TP3_75_SL18_Slope2_75,

        // Appended last to preserve every legacy ordinal - see EMALUs0928Setting.
        S1_TP3_75_SL18_Slope3_50_Win3
    }

    // EMAL-1051: four new sessions' presets, same shape as the two above - Disabled first,
    // one real preset each (found and validated on the EMAL-1049 QNRVX full-day scan).
    public enum EMALAsiaSetting
    {
        Disabled,
        TP4_SL20_Slope2_75
    }

    public enum EMALEuropeSetting
    {
        Disabled,
        TP8_5_SL20_Slope2_75
    }

    public enum EMALPreMarketSetting
    {
        Disabled,
        TP11_SL20_Slope2_75
    }

    public enum EMALUSMiddaySetting
    {
        Disabled,
        TP3_SL13_Slope2_75
    }

}
