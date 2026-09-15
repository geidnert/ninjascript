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

// =====================================================================================
// EMA921 - AutoEdge Systems(TM)
// NQ/MNQ, minute charts (built for 1m, also runs on 5m or any other minute period).
//
// Forked from EMAL 1070 (2026-09-11). Every EMAL order-safety mechanism is carried over
// unchanged in behaviour: naked-position audit, MissingStop/MissingTarget, protective-reject
// flatten, stop-side gap latch, terminal-exit cancel-then-confirm with its 30s breaker and
// retry ladder, target-touch watchdog, working-entry cancel on disable, shared order-rate
// guard, LiquidationOnly latch, ProjectX mirroring, and the feature / research / tick CSV
// logs. Comments tagged EMAL-xxxx are inherited from EMAL and describe that code's history.
// Deliberately NOT carried over: EMAL's target-side gap latch (cancel a working entry once
// its planned TP has been passed) - EMA921 never cancels a working entry for market reasons.
//
// EMA921 trade rules (longs shown, shorts are the mirror image):
//   1. Sequence: count consecutive bullish candles whose body is >= Min Body. A candle whose
//      body is under Min Body is INVISIBLE - it neither counts nor resets the run. A bearish
//      candle with a qualifying body resets the bullish run (and starts a bearish one).
//   2. Once the run reaches Sequence Candles the setup is ARMED long. It then waits for the
//      completed-bar slope of the entry EMA (ema[1] - ema[2], the EMAL calculation) to reach
//      Min Slope. Slope is only required for this first entry of a sequence.
//   3. Limit buy at Entry EMA + Entry Padding.
//   4. Stop at SL EMA - SL Padding.
//   5. Target = entry + RR x (entry - stop), never less than Min TP Distance from entry.
//   6. Every bar close, until filled, the limit / planned stop / planned target move to the
//      new EMA values. Nothing cancels a working entry for market reasons - only the session
//      gate, the risk caps, the SL-distance range check and the order-safety paths can.
//   7. After the fill the stop follows the SL EMA every bar (both directions, capped at Max
//      SL from entry) and the target is re-derived from it with the RR multiple.
//   8. Target hit -> at the next bar close a new limit is placed with the same rules (no
//      sequence and no slope required). This chain continues until a stop is hit.
//   9. Stop hit -> everything resets; a brand-new sequence (step 1) is required.
// The stop distance at entry must lie between Min SL and Max SL; outside that range the
// limit is not placed (or is pulled) and the setup keeps waiting.
// =====================================================================================
namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class EMA921 : Strategy
    {
        private const string StrategySignalPrefix = "EMA921";
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
            public string EventType;                       // "buy"/"sell"/"exit"/"cancel"/"modify-entry"; null for a protection-sync item
            public string EntrySide;                       // EMA921 "modify-entry" only: "buy"/"sell" for the fallback re-place
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

        // EMA921 orange scheme (Steve, 2026-09-11): same layout as EMAL's panel, warm palette.
        private static readonly Brush InfoHeaderFooterGradientBrush = CreateFrozenVerticalGradientBrush(
            Color.FromArgb(240, 0x5A, 0x2E, 0x08),
            Color.FromArgb(240, 0x3E, 0x1F, 0x05),
            Color.FromArgb(240, 0x26, 0x13, 0x03));
        private static readonly Brush InfoBodyOddBrush = CreateFrozenBrush(240, 0x17, 0x10, 0x0A);
        private static readonly Brush InfoBodyEvenBrush = CreateFrozenBrush(240, 0x1C, 0x13, 0x0B);
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(255, 0xFF, 0x9F, 0x1C);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xD9, 0xA4, 0x6C);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xFF, 0xF1, 0xE0);
        private static readonly Brush InfoStatusTextBrush = CreateFrozenBrush(255, 0xFF, 0x33, 0x33);

        // EMA921-1002: the old fixed EntryEmaBrush/StopEmaBrush pair was dropped, since up to
        // five distinct entry periods and four distinct stop periods could be computed at once.
        // EMA921-1004: styling is back, but on the strategy's own "Active Entry/Stop EMA" plots
        // (SetDefaults, brushes set there directly) rather than on the individual cached
        // EMA(Close, period) indicator objects, which are no longer charted at all - see
        // BuildEmaSeriesCache() and UpdateLevelPlots().

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

        private EMA ema;        // entry EMA for the CURRENT bar's active session - reassigned every
                                 // bar by ResolveActiveSessionValues(), not fixed at DataLoaded
        private EMA slEma;      // stop EMA for the CURRENT bar's active session, same as above

        // EMA921-1002: per-session cascade-tuned presets. One EMA(Close, period) instance is
        // pre-created per DISTINCT period actually used by any session (Disabled or the session's
        // one cascade-tuned preset)
        // during DataLoaded - never created dynamically mid-run - and looked up here every bar.
        private Dictionary<int, EMA> emaSeriesByPeriod;
        private int warmupMaxPeriod;   // max entry/stop period across every session; see GetWarmupBars
        private const int FallbackEntryPeriod = 7;   // used only when GetSessionIndex returns
        private const int FallbackStopPeriod = 21;   // -1 (outside every window) - inert, see ResolveActiveSessionValues

        // EMA921-1002 (code-review fix, blocking): the session an OPEN POSITION's parameters
        // are LATCHED to, for the life of that position (see IsTradeLifecycleActive - only a
        // position latches; a bare arm, a working limit, and a pending TP chain do not). Without
        // this, a position that survives a session boundary (e.g. the US 9:36-10:30 -> Midday
        // handoff at 10:30) would have its stop/target silently re-derived from a DIFFERENT
        // session's EMA period/padding/caps mid-trade - a combination no session was ever tuned
        // with, and one that can fire a false stop-hit exit. See ResolveActiveSessionValues().
        private const int UnresolvedSessionIndex = -2;   // sentinel GetSessionIndex() can never
                                                          // return - distinct from -1, which IS a
                                                          // legitimate "outside every window"
                                                          // result and must not keep re-triggering
                                                          // the first-resolution branch once latched
        private int activeTradeSessionIndex = UnresolvedSessionIndex;

        // Per-session resolved values (EMA921-1002), populated once in DataLoaded by
        // ResolveSessionPresets() and copied into the shared EntryEmaPeriod/EntryPaddingPoints/etc
        // properties every bar by ResolveActiveSessionValues() - every downstream formula keeps
        // reading those same property names unchanged. "Disabled" fields are inert placeholders
        // (that session can never arm - see IsSessionEnabled) but must still be real, safe values
        // since ResolveActiveSessionValues() runs unconditionally, before the enable check.
        private int asiaEntryEma, asiaStopEma, asiaSeq;
        private double asiaEntryPad, asiaStopPad, asiaSlope, asiaRr, asiaBody, asiaMinSl, asiaMaxSl, asiaMinTp;
        private int europeEntryEma, europeStopEma, europeSeq;
        private double europeEntryPad, europeStopPad, europeSlope, europeRr, europeBody, europeMinSl, europeMaxSl, europeMinTp;
        private int preMarketEntryEma, preMarketStopEma, preMarketSeq;
        private double preMarketEntryPad, preMarketStopPad, preMarketSlope, preMarketRr, preMarketBody, preMarketMinSl, preMarketMaxSl, preMarketMinTp;
        private int us0936EntryEma, us0936StopEma, us0936Seq;
        private double us0936EntryPad, us0936StopPad, us0936Slope, us0936Rr, us0936Body, us0936MinSl, us0936MaxSl, us0936MinTp;
        private int usMiddayEntryEma, usMiddayStopEma, usMiddaySeq;
        private double usMiddayEntryPad, usMiddayStopPad, usMiddaySlope, usMiddayRr, usMiddayBody, usMiddayMinSl, usMiddayMaxSl, usMiddayMinTp;

        // ---- EMA921 sequence / setup state ----
        // Candle runs are counted on COMPLETED bars only, once per bar (guarded by
        // lastSequenceBarProcessed), including during historical warmup so the counts are
        // already correct when the strategy goes live. A candle whose body is under Min Body is
        // invisible: it neither extends nor resets either run (Steve, 2026-09-11).
        private int bullRun;
        private int bearRun;
        private int lastSequenceBarProcessed = -1;
        // +1/-1 once a run has reached Sequence Candles; 0 = nothing armed. Stays armed until the
        // limit fills (Steve: "if price reverses then limit would definitely get filled"). An
        // opposite completed run replaces it ONLY while no entry order is working yet.
        private int setupDirection;
        // Set the first time the slope gate passes for the armed setup; never re-checked for
        // that setup afterwards (so an SL-range pause does not re-impose the slope gate).
        private bool setupSlopeConfirmed;
        // +1/-1 after a take-profit: re-entry mode. The next bar close places a new limit with
        // the same EMA/padding/RR rules but WITHOUT the sequence or slope gates. Ends on a stop.
        private int chainDirection;
        private int chainTradeNumber;
        // Per-trade plan, recomputed every bar close from the COMPLETED-bar EMA values. Every
        // number here derives from the shared chart data, never from this account's own fill,
        // so every instance on the feed computes identical levels (same principle as EMAL-1042).
        private int planDirection;
        private double planLimitPrice;
        private double planStopPrice;
        private double planTargetPrice;
        private string planEntryKind = string.Empty;   // "Setup" or "ReEntry"
        // Post-fill bracket. The anchor is the first entry fill price; on a normal limit fill that
        // is the plan's limit price, identical on every account (see InitializeBracketFromPlan).
        private double bracketAnchorPrice;
        private int bracketDirection;
        private double desiredProtectionStopPrice;
        private bool positionOpenTracked;
        private int entrySubmittedBar = -1;
        // Diagnostics for the summary / info panel.
        private string setupStatusText = "-";
        private int setupArmCount;
        private int reEntryCount;
        private int rangeSkipCount;
        private int trailCrossExitCount;
        private int tpOutcomeCount;
        private int slOutcomeCount;
        private int otherOutcomeCount;

        private Order entryOrder;
        private Order protectiveStopOrder;
        private Order profitTargetOrder;
        private int queuedDirection;
        private double queuedLimitPrice;
        private double queuedStopPrice;
        private double queuedTargetPrice;
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
        private double gapLatchStopPrice;
        // EMA921: STOP side only. EMAL's target-side latch (cancel a working entry once its
        // planned TP has traded, flatten post-fill if it filled anyway) is deliberately not
        // ported - Steve, 2026-09-11: nothing cancels an unfilled EMA921 limit for market
        // reasons. The stop-side flag still turns a fill into an already-breached stop into an
        // immediate GapStop flatten at the moment protection is first attached.
        // EMA921: the stop level is re-armed every bar close while the entry is working,
        // because the planned stop moves with the SL EMA (see RefreshPlanLevels).
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
        private int blockedBarCount;
        // EMAL-1051: counts bars blocked by the unconditional 08:28-08:32 news-release block.
        private int newsBlockedMinuteBarCount;
        // EMAL-1051 (second change, 2026-08-22): counts bars blocked by the unconditional
        // 16:55-17:00 pre-close block. See IsPreCloseMinute's comment.
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

        // Session boundaries in minutes-of-day, New York time - mirrored 1:1 from EMAL 1070
        // (Steve, 2026-09-11: "trade the same session periods as EMAL, including the europe
        // session ... adopt their naming conventions"). Constant names are EMAL's own
        // (Us0928... is EMAL's historical name for the 09:36-09:55 window). Deliberate gaps,
        // same as EMAL: 06:30-08:00, 09:28-09:36, and the 17:00-18:00 CME maintenance halt.
        // EMA921-1002: the original two windows (09:36-09:55, 09:55-10:30) are MERGED into one
        // continuous 09:36-10:30 window - the full-cascade tuning campaign found both
        // individually too thin to tune reliably (P1 alone never converged in 3 passes; see
        // CASCADE_CHECKPOINT.md "Follow-up 2 RESULTS") and recommended merging. Session index 3
        // now covers the whole span; index 5 is retired. Constant names kept as "Us0928" (EMAL's
        // own historical name for this slot) for continuity with EMAL's session-index numbering.
        private const int Us0928StartMinute = 9 * 60 + 36; // 09:36 ET, US 09:36-10:30 opens
        private const int Us0928EndMinute = 10 * 60 + 30;  // 10:30 ET (exclusive)

        // All time rules are evaluated in New York time regardless of how NinjaTrader's
        // display timezone is configured. TimeZoneInfo carries the full DST rule set, so
        // the spring and autumn shifts are handled automatically - no seasonal code.
        private TimeZoneInfo platformZone;
        private TimeZoneInfo easternZone;

        // One row per completed trade. The entry fragment is captured when the limit is FIRST
        // submitted (signal context), the fill fragment at the fill, the exit fragment at flat.
        private const string FeatureHeader =
            "EntryTimeET,EntryTimeUTC,DayOfWeek,HHmm,"
            + "Session,Direction,EntryKind,ChainTrade,SeqRun,SignalPrice,EmaEntry,EmaStop,Slope,SlopePrev,SlopeAccel,ReqSlope,"
            + "LimitPrice,PlanStop,PlanTarget,DistToEma,TpPoints,SlPoints,"
            + "Bar1Open,Bar1High,Bar1Low,Bar1Close,Bar1Volume,"
            + "Bar2Open,Bar2High,Bar2Low,Bar2Close,Bar2Volume,"
            + "Bar3Open,Bar3High,Bar3Low,Bar3Close,Bar3Volume,"
            + "AvgVolume20,FillPrice,FillDelaySec,BarsWorking,FillStop,FillTarget,"
            + "ExitTime,ExitPrice,ExitReason,ProfitPoints,IsWin,BarsHeld,MaePoints,MfePoints,FinalStop,FinalTarget";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "EMA921: N-candle momentum sequence + entry-EMA slope gate, passive limit at the "
                    + "entry EMA, stop at the stop EMA, RR-derived target, bracket trails the EMAs every bar. "
                    + "NQ/MNQ, minute charts. AutoEdge Systems.";
                Name = "EMA921";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                // Same as EMAL: NinjaTrader-native session-close flatten, governed by the CHART'S
                // Trading Hours Template - a second, independent mechanism alongside EOD Force
                // Close. Confirm the template before relying on it for the overnight sessions.
                IsExitOnSessionCloseStrategy = true;
                IsInstantiatedOnEachOptimizationIteration = false;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                // EMA921-1003 (fix): NinjaTrader only allows BarsRequiredToTrade to be set from
                // State.SetDefaults ("cannot be set from this state" at DataLoaded, caught the
                // hard way against a live NT8 instance) - so this MUST be a fixed constant here,
                // not computed at runtime from the resolved presets. It still needs to cover the
                // max entry/stop period across ALL five sessions' presets (not just one fixed
                // pair, now that each session can use a different period) - GetWarmupBars() /
                // warmupMaxPeriod document and runtime-verify that max via the per-bar warmup
                // gate check (CurrentBar < GetWarmupBars()), but this literal is the actual
                // authoritative value NinjaTrader uses to decide how many bars to preload.
                // Currently 23 = the largest stop-EMA period among all five presets (21, on
                // Pre-Market and US 9:36-10:30) + 2. If any preset's period ever changes, update
                // this literal to match Max(all entry/stop periods, 20) + 2, or NT8 may start
                // calling OnBarUpdate before every session's indicator is fully warmed.
                BarsRequiredToTrade = 23;

                Version = EMA921Version.version_1005;   // bump on every new cut; see enum comment

                // ---- EMA921 rules (Steve, 2026-09-11; re-tuned 2026-09-12 per
                // EMA921_Tuning_Plan.md Phase 4/5). SUPERSEDED 2026-09-14 (EMA921-1002): these
                // ten fields are no longer independently user-editable. Every session now picks
                // its own preset via a single "Setting" popup, EMAL style (see the C. Sessions
                // group below) - Disabled first, one value-encoded preset per session (EMA921-1005:
                // renamed from the generic "CascadeTuned" label to spell out the actual values in
                // the dropdown itself, EMAL style), found by the full-cascade tuning campaign
                // (CASCADE_CHECKPOINT.md) on the entire research
                // store, selected purely on that session's own Net/MaxIDD. Values below are just
                // the inert pre-DataLoaded default; ResolveSessionPresets() (called from
                // DataLoaded) and ResolveActiveSessionValues() (called once per bar from the
                // top of OnBarUpdate) overwrite them per the LATCHED active session before they
                // are ever read by signal logic - see ResolveActiveSessionValues()'s own
                // comment for what "active" means once a trade is in progress. ----
                EntryEmaPeriod = 7;
                StopEmaPeriod = 21;
                EntryPaddingPoints = 0.5;
                StopPaddingPoints = 1.0;
                MinimumSlopePoints = 2.75;
                RewardMultiple = 1.0;
                SequenceCandles = 4;
                MinimumBodyPoints = 1.0;
                MinimumStopPoints = 5.0;
                MaximumStopPoints = 30.0;
                MinimumTargetPoints = 2.0;
                Contracts = 1;

                // Sessions (EMA921-1002): five popups, Disabled first - the original six windows
                // minus the 9:36-9:55 / 9:55-10:30 split, which the cascade found individually
                // too thin to tune reliably (P1 alone never converged) and recommended merging
                // into one continuous 9:36-10:30 window instead. See EMA921-1002-changelog.txt
                // (the per-session preset architecture), EMA921-1003-changelog.txt (a NinjaTrader
                // state-timing error found on first load), and EMA921-1004-changelog.txt (this
                // cut - chart clutter fix, cosmetic only).
                AsiaSetting = EMA921AsiaSetting.Disabled;
                EuropeSetting = EMA921EuropeSetting.Disabled;
                PreMarketSetting = EMA921PreMarketSetting.Disabled;
                Us0936Setting = EMA921Us0936Setting.Disabled;
                USMiddaySetting = EMA921USMiddaySetting.Disabled;
                EODForceCloseTime = new TimeSpan(16, 55, 0);   // see comment on the property below
                MultiContractProtectionFix = EMA921MultiContractProtectionFix.Off;

                MaxAccountBalance = 0.0;
                MaxDailyProfit = 0.0;
                EnableTargetTouchWatchdog = true;   // see comment on the property below
                TargetTouchGraceMs = 400;
                TouchDetectionMode = EMA921TouchDetectionMode.QuoteOrLast;
                EnableNakedPositionAudit = true;   // EMAL-1067: safety net, ON by default
                NakedPositionGraceSeconds = 10;    // EMAL-1067: see the property comment
                OrderActionLimitPerHour = 4000;    // EMAL-1070 value; see the property's comment

                ProjectXApiBaseUrl = "https://api.topstepx.com";
                ProjectXTradeAllAccounts = false;
                ProjectXUsername = string.Empty;
                ProjectXApiKey = string.Empty;
                ProjectXAccountId = string.Empty;
                ProjectXContractId = string.Empty;

                EnableFeatureLog = false;
                FeatureLogPath = string.Empty;   // blank -> version-named auto-path, see ResolveFeatureLogPath
                EnablePathLog = false;   // research-only; never on for live trading
                PathLogPath = string.Empty;
                EnableExecutionDiagnostics = false;

                EnableTickLogging = false;   // diagnostic OFF by default; only for divergence hunting
                TickLogTag = string.Empty;
                TickLogFolder = string.Empty;   // blank -> NinjaTrader.Core.Globals.UserDataDir\ticklogs

                // Level plots (EMA921): the working limit / planned-or-live stop / planned-or-live
                // target, drawn as per-bar hash marks only while a plan or position exists.
                AddPlot(new Stroke(Brushes.Gold, DashStyleHelper.Solid, 2), PlotStyle.Hash, "Entry Limit");
                AddPlot(new Stroke(Brushes.Red, DashStyleHelper.Solid, 2), PlotStyle.Hash, "Stop Loss");
                AddPlot(new Stroke(Brushes.LimeGreen, DashStyleHelper.Solid, 2), PlotStyle.Hash, "Take Profit");

                // EMA921-1004 (Steve, 2026-09-14): with five sessions now each potentially using
                // a different entry/stop EMA period, 1002/1003 charted every distinct cached
                // period permanently via BuildEmaSeriesCache()'s AddChartIndicator calls - up to
                // 9 lines at once, most of them inactive on any given bar. These two plots
                // replace that entirely: one continuous line each, painted across the FULL chart
                // history (see UpdateActiveEmaPlots(), called from OnBarUpdate every tick, before
                // the historical-bars early return - a first draft of this fix put the write
                // inside UpdateLevelPlots() instead, which is never reached on a historical bar
                // outside the Strategy Analyzer, so the lines only appeared from the realtime
                // transition onward; caught by code review before this was sent), fed from
                // whichever EMA the LATCHED active session (see ResolveActiveSessionValues) is
                // actually using right now. The line's underlying data source switches seamlessly
                // at each session boundary, so the chart always shows exactly the EMA the
                // strategy is reading, never more.
                AddPlot(new Stroke(Brushes.Orange, DashStyleHelper.Solid, 2), PlotStyle.Line, "Active Entry EMA");
                AddPlot(new Stroke(Brushes.DarkOrange, DashStyleHelper.Solid, 2), PlotStyle.Line, "Active Stop EMA");
            }
            else if (State == State.DataLoaded)
            {
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

                // Fresh sequence state on every (re-)enable.
                bullRun = 0;
                bearRun = 0;
                lastSequenceBarProcessed = -1;
                ClearSetupState("start");
                activeTradeSessionIndex = UnresolvedSessionIndex;   // EMA921-1002: idle, free to latch on first bar

                // EMA921-1002: each session can now use a different entry/stop EMA period, so
                // there is no longer one fixed pair of indicator instances bound once here.
                // ResolveSessionPresets() reads every session's Setting popup; BuildEmaSeriesCache()
                // then pre-creates one EMA(Close, period) instance per DISTINCT period actually
                // needed (across all five sessions, Disabled or the session's cascade-tuned preset) - never created
                // dynamically mid-run. ema/slEma are re-pointed into this cache once per bar by
                // ResolveActiveSessionValues() (called from the top of OnBarUpdate), so they
                // reference the LATCHED active session's own series (see that method's own
                // comment for what "active" means once a trade is in progress). EMA921-1004: none
                // of the cached series are individually charted any more - only the LATCHED one
                // is, via the "Active Entry/Stop EMA" plots (UpdateActiveEmaPlots()), so the chart
                // never shows more than the two EMAs actually driving the strategy right now.
                ResolveSessionPresets();
                ValidateSessionPresets();   // real Min/Max SL sanity check - see its own comment
                BuildEmaSeriesCache();
                // EMA921-1003 (fix): NinjaTrader rejects BarsRequiredToTrade assignments outside
                // State.SetDefaults ("'BarsRequiredToTrade' cannot be set from this state" -
                // caught against a live NT8 instance). BarsRequiredToTrade is set as a fixed
                // literal in SetDefaults instead (see that comment for the derivation and the
                // note to update it if any preset's period ever changes); GetWarmupBars()/
                // warmupMaxPeriod are computed here purely for the runtime warmup gate check
                // (CurrentBar < GetWarmupBars(), used elsewhere), not to drive this property.
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
                    "EMA921: platform={0} | NY-anchored boundaries active.",
                    platformZone == null ? "unknown" : platformZone.Id));
            }
            catch (Exception ex)
            {
                platformZone = null;
                easternZone = null;
                Print("EMA921: timezone setup failed, using platform time as-is. " + ex.Message);
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

        // Session indices kept identical to EMAL's so logs from the two strategies line up:
        // 3 = US 09:36-10:30 (EMA921-1002: merged from the original 09:36-09:55 / 09:55-10:30
        // split), 10 = Asia 18:00-03:00, 11 = Europe 03:00-06:30, 12 = US Pre-Market 08:32-09:28,
        // 13 = US Midday 10:30-17:00. Index 5 is retired.
        // -1 = outside every window. Asia crosses midnight; IsInMinuteWindow handles that.
        private const int AsiaStartMinute = 18 * 60;         // 18:00 ET
        private const int AsiaStopMinute = 3 * 60;           // 03:00 ET (wraps past midnight)
        private const int EuropeStartMinute = 3 * 60;        // 03:00 ET
        private const int EuropeStopMinute = 6 * 60 + 30;    // 06:30 ET
        private const int PreMarketStartMinute = 8 * 60 + 32; // 08:32 ET. CHANGED 2026-09-12
        // (Steve, Round 3 tuning): the derive-segment simulator found the whole edge of a
        // candidate Pre-Market config lived in the 14-58 minutes AFTER the 08:28-08:32 news
        // block, with the 08:00-08:32/08:44 stretch flat-to-losing under that candidate. Moving
        // the session's own start to right after the existing unconditional news block avoids
        // re-trading that stretch. Was 08:00 ET.
        private const int PreMarketStopMinute = 9 * 60 + 28; // 09:28 ET (EMAL: deliberately not tied to Us0928StartMinute)
        private const int USMiddayStartMinute = Us0928EndMinute;   // 10:30 ET
        private const int USMiddayStopMinute = 17 * 60;      // 17:00 ET, CME daily maintenance halt

        // EOD Force Close (EMAL-1051): standing, unconditional block on new entries AND flatten of
        // any open position from EODForceCloseTime up to the 17:00 ET CME halt. The clock time is
        // user-editable because prop firms differ (Topstep: flat by 16:00 ET); the 17:00 upper
        // bound is a market fact, not a preference. EODForceCloseTime >= 17:00 disables it.
        private bool IsPreCloseMinute(int minuteOfDay)
        {
            int eodForceCloseMinute = (int)EODForceCloseTime.TotalMinutes;
            return minuteOfDay >= eodForceCloseMinute && minuteOfDay < USMiddayStopMinute;
        }

        private int GetSessionIndex(DateTime platformTime)
        {
            DateTime ny = ConvertToZone(platformTime, easternZone);
            return GetSessionIndexForMinute(ny.Hour * 60 + ny.Minute);
        }

        private static int GetSessionIndexForMinute(int nyMinute)
        {
            if (nyMinute >= Us0928StartMinute && nyMinute < Us0928EndMinute)
                return 3;
            if (IsInMinuteWindow(nyMinute, AsiaStartMinute, AsiaStopMinute))
                return 10;
            if (IsInMinuteWindow(nyMinute, EuropeStartMinute, EuropeStopMinute))
                return 11;
            if (IsInMinuteWindow(nyMinute, PreMarketStartMinute, PreMarketStopMinute))
                return 12;
            if (IsInMinuteWindow(nyMinute, USMiddayStartMinute, USMiddayStopMinute))
                return 13;
            return -1;
        }

        // Stop is exclusive; a window with start > stop crosses midnight (EMAL-1051).
        private static bool IsInMinuteWindow(int nyMinute, int startMinute, int stopMinute)
        {
            if (startMinute <= stopMinute)
                return nyMinute >= startMinute && nyMinute < stopMinute;
            return nyMinute >= startMinute || nyMinute < stopMinute;
        }

        // EMAL's session labels; index 3 covers the EMA921-1002 merged 9:36-10:30 window.
        private static string SessionName(int index)
        {
            switch (index)
            {
                case 3: return "9:36-10:30";
                case 10: return "18:00-3:00";
                case 11: return "3:00-6:30";
                case 12: return "8:32-9:28";
                case 13: return "10:30-17:00";
                default: return "Halt";
            }
        }

        private bool IsSessionEnabled(int index)
        {
            switch (index)
            {
                case 3: return Us0936Setting != EMA921Us0936Setting.Disabled;
                case 10: return AsiaSetting != EMA921AsiaSetting.Disabled;
                case 11: return EuropeSetting != EMA921EuropeSetting.Disabled;
                case 12: return PreMarketSetting != EMA921PreMarketSetting.Disabled;
                case 13: return USMiddaySetting != EMA921USMiddaySetting.Disabled;
                default: return false;
            }
        }

        // Per-session slope threshold (EMA921-1002). Same unit as EMAL: points of entry-EMA
        // change over the last completed bar (ema[1] - ema[2]). MinimumSlopePoints is refreshed
        // every bar by ResolveActiveSessionValues() to the CURRENT bar's active session, so this
        // keeps working unchanged.
        private double GetRequiredSlope()
        {
            return Math.Abs(MinimumSlopePoints);
        }

        // ================================================================================
        // EMA921-1002: per-session cascade-tuned presets
        // ================================================================================
        // Two-step resolution, EMAL's own pattern scaled up from 3 fields (TP/SL/slope) to 11
        // (entry EMA, stop EMA, entry pad, stop pad, slope, RR, sequence candles, min body, min
        // SL, max SL, min TP): (1) ResolveSessionPresets(), called once from DataLoaded, reads
        // each session's Setting popup and populates that session's private backing fields
        // (asiaEntryEma, asiaEntryPad, ... one set per session). (2) ResolveActiveSessionValues(),
        // called once per completed bar from the top of OnBarUpdate (before UpdateSequenceRuns()
        // and ProcessBarClose() - see that call site's own comment for why the ordering matters),
        // copies the LATCHED active session's backing fields into the shared
        // EntryEmaPeriod/EntryPaddingPoints/etc properties (and re-points the shared ema/slEma
        // indicator references) - every downstream formula already reads those same
        // property/field names and needs no further changes.

        private void ResolveSessionPresets()
        {
            switch (AsiaSetting)
            {
                case EMA921AsiaSetting.S1_EMA9_Pad0_5_Slope2_25_Body1_0_RR2_0_Seq2_StopEMA14_StopPad0_0_MinSL3_MaxSL30:
                    asiaEntryEma = 9; asiaStopEma = 14; asiaEntryPad = 0.5; asiaStopPad = 0.0;
                    asiaSlope = 2.25; asiaRr = 2.0; asiaSeq = 2; asiaBody = 1.0;
                    asiaMinSl = 3.0; asiaMaxSl = 30.0; asiaMinTp = 4.0;
                    break;
                default:   // Disabled - window is off; values are inert, see IsSessionEnabled
                    asiaEntryEma = 9; asiaStopEma = 14; asiaEntryPad = 0.5; asiaStopPad = 0.0;
                    asiaSlope = 2.25; asiaRr = 2.0; asiaSeq = 2; asiaBody = 1.0;
                    asiaMinSl = 3.0; asiaMaxSl = 30.0; asiaMinTp = 4.0;
                    break;
            }
            switch (EuropeSetting)
            {
                case EMA921EuropeSetting.S1_EMA6_Pad2_0_Slope1_5_Body0_25_RR2_0_Seq4_StopEMA17_StopPad0_75_MinSL5_MaxSL20:
                    europeEntryEma = 6; europeStopEma = 17; europeEntryPad = 2.0; europeStopPad = 0.75;
                    europeSlope = 1.5; europeRr = 2.0; europeSeq = 4; europeBody = 0.25;
                    europeMinSl = 5.0; europeMaxSl = 20.0; europeMinTp = 4.0;
                    break;
                default:
                    europeEntryEma = 6; europeStopEma = 17; europeEntryPad = 2.0; europeStopPad = 0.75;
                    europeSlope = 1.5; europeRr = 2.0; europeSeq = 4; europeBody = 0.25;
                    europeMinSl = 5.0; europeMaxSl = 20.0; europeMinTp = 4.0;
                    break;
            }
            switch (PreMarketSetting)
            {
                case EMA921PreMarketSetting.S1_EMA5_Pad0_5_Slope1_5_Body0_5_RR1_0_Seq4_StopEMA21_StopPad0_25_MinSL7_MaxSL30:
                    preMarketEntryEma = 5; preMarketStopEma = 21; preMarketEntryPad = 0.5; preMarketStopPad = 0.25;
                    preMarketSlope = 1.5; preMarketRr = 1.0; preMarketSeq = 4; preMarketBody = 0.5;
                    preMarketMinSl = 7.0; preMarketMaxSl = 30.0; preMarketMinTp = 4.0;
                    break;
                default:
                    preMarketEntryEma = 5; preMarketStopEma = 21; preMarketEntryPad = 0.5; preMarketStopPad = 0.25;
                    preMarketSlope = 1.5; preMarketRr = 1.0; preMarketSeq = 4; preMarketBody = 0.5;
                    preMarketMinSl = 7.0; preMarketMaxSl = 30.0; preMarketMinTp = 4.0;
                    break;
            }
            switch (Us0936Setting)
            {
                case EMA921Us0936Setting.S1_EMA4_Pad3_0_Slope3_0_Body2_0_RR2_0_Seq4_StopEMA21_StopPad0_0_MinSL5_MaxSL30:
                    us0936EntryEma = 4; us0936StopEma = 21; us0936EntryPad = 3.0; us0936StopPad = 0.0;
                    us0936Slope = 3.0; us0936Rr = 2.0; us0936Seq = 4; us0936Body = 2.0;
                    us0936MinSl = 5.0; us0936MaxSl = 30.0; us0936MinTp = 4.0;
                    break;
                default:
                    us0936EntryEma = 4; us0936StopEma = 21; us0936EntryPad = 3.0; us0936StopPad = 0.0;
                    us0936Slope = 3.0; us0936Rr = 2.0; us0936Seq = 4; us0936Body = 2.0;
                    us0936MinSl = 5.0; us0936MaxSl = 30.0; us0936MinTp = 4.0;
                    break;
            }
            switch (USMiddaySetting)
            {
                case EMA921USMiddaySetting.S1_EMA7_Pad0_25_Slope3_5_Body2_0_RR2_0_Seq2_StopEMA18_StopPad2_0_MinSL5_MaxSL20:
                    usMiddayEntryEma = 7; usMiddayStopEma = 18; usMiddayEntryPad = 0.25; usMiddayStopPad = 2.0;
                    usMiddaySlope = 3.5; usMiddayRr = 2.0; usMiddaySeq = 2; usMiddayBody = 2.0;
                    usMiddayMinSl = 5.0; usMiddayMaxSl = 20.0; usMiddayMinTp = 4.0;
                    break;
                default:
                    usMiddayEntryEma = 7; usMiddayStopEma = 18; usMiddayEntryPad = 0.25; usMiddayStopPad = 2.0;
                    usMiddaySlope = 3.5; usMiddayRr = 2.0; usMiddaySeq = 2; usMiddayBody = 2.0;
                    usMiddayMinSl = 5.0; usMiddayMaxSl = 20.0; usMiddayMinTp = 4.0;
                    break;
            }
        }

        // EMA921-1002 (code-review fix): the real Min/Max SL sanity check, replacing the one in
        // ValidateChart() that became vacuous once these values moved per-session (see that
        // method's comment). Called from DataLoaded right after ResolveSessionPresets(), so it
        // checks the ACTUAL five resolved presets, not the inert SetDefaults placeholder.
        private void ValidateSessionPresets()
        {
            var pairs = new[]
            {
                new { Name = "Asia", Min = asiaMinSl, Max = asiaMaxSl },
                new { Name = "Europe", Min = europeMinSl, Max = europeMaxSl },
                new { Name = "US Pre-Market", Min = preMarketMinSl, Max = preMarketMaxSl },
                new { Name = "US 9:36-10:30", Min = us0936MinSl, Max = us0936MaxSl },
                new { Name = "US Midday", Min = usMiddayMinSl, Max = usMiddayMaxSl },
            };
            foreach (var p in pairs)
            {
                if (p.Max < p.Min)
                {
                    configurationBlocked = true;
                    configurationBlockReason = p.Name + " Max SL is below Min SL";
                    Print("EMA921 DISABLED: " + p.Name + " Max SL (" + p.Max + ") is below Min SL ("
                        + p.Min + "). No orders will be submitted.");
                }
            }
        }

        // Every distinct entry/stop EMA period across all five sessions, called once from
        // DataLoaded after ResolveSessionPresets(). Pre-creates one EMA(Close, period) indicator
        // per distinct value needed - NEVER created dynamically mid-run - and records the max
        // for GetWarmupBars(). EMA921-1004: no longer charts every one of them (that was the
        // clutter of up to 9 permanent lines) - see the "Active Entry/Stop EMA" plots added in
        // SetDefaults and UpdateActiveEmaPlots() instead, which show only whichever one is
        // actually in use on the current bar.
        private void BuildEmaSeriesCache()
        {
            emaSeriesByPeriod = new Dictionary<int, EMA>();
            int[] periods = new[]
            {
                asiaEntryEma, asiaStopEma, europeEntryEma, europeStopEma,
                preMarketEntryEma, preMarketStopEma, us0936EntryEma, us0936StopEma,
                usMiddayEntryEma, usMiddayStopEma,
                FallbackEntryPeriod, FallbackStopPeriod   // guarantee the "outside every
                                                           // window" fallback in ResolveActiveSessionValues is always cached
            };
            warmupMaxPeriod = 20;
            foreach (int period in periods)
            {
                warmupMaxPeriod = Math.Max(warmupMaxPeriod, period);
                if (emaSeriesByPeriod.ContainsKey(period))
                    continue;
                EMA series = EMA(Close, period);
                emaSeriesByPeriod[period] = series;
            }

            // EMA921-1002 (code-review fix, blocking): ema/slEma are otherwise null from
            // DataLoaded until the first ProcessBarClose() call resolves them, but
            // OnBarUpdate() dereferences both (ema.Update()/slEma.Update()) on every tick,
            // including the first - before ProcessBarClose ever runs. Seed them here with the
            // guaranteed-cached fallback periods so there is never a null-reference window.
            // ResolveActiveSessionValues() re-points them correctly once the first bar closes.
            ema = emaSeriesByPeriod[FallbackEntryPeriod];
            slEma = emaSeriesByPeriod[FallbackStopPeriod];
        }

        // EMA921-1002 (code-review fix, blocking): true only while a POSITION IS OPEN - the one
        // state that genuinely must not be re-based, since an open position's protective stop
        // and target are live orders in the market. Nothing else latches:
        //   - a bare armed setup (setupDirection != 0, no order yet) does not latch - a 2nd
        //     review pass found including it let a brand NEW trade start in a brand NEW session
        //     while still using the PREVIOUS session's parameters, since an arm can sit for many
        //     bars before an order is ever placed;
        //   - a pending TP chain (chainDirection != 0, flat, waiting for its next re-entry) does
        //     not latch - a 3rd pass found the identical hazard there;
        //   - a WORKING ENTRY LIMIT does not latch either - a 4th pass found the file actively
        //     re-prices a working limit every bar (MoveWorkingEntry -> ChangeOrder), so a limit
        //     surviving a session boundary would keep being re-priced off the OLD session's EMA/
        //     padding and could fill and get bracketed entirely under the wrong session's stop
        //     caps. An unfilled entry has nothing to protect, so cancelling it on a genuine
        //     session change is not an exit and carries no naked-position risk - see
        //     ResolveActiveSessionValues(), which cancels it alongside clearing the setup/chain.
        private bool IsTradeLifecycleActive()
        {
            return Position.MarketPosition != MarketPosition.Flat;
        }

        // Copies the ACTIVE session's resolved preset into the shared properties every
        // downstream formula already reads, and re-points ema/slEma to that session's indicator
        // series. Called once per completed bar, from the top of OnBarUpdate (before
        // UpdateSequenceRuns() and ProcessBarClose()). "Active session" is LATCHED for the life
        // of an OPEN POSITION ONLY (see IsTradeLifecycleActive) - only re-resolved from the
        // current bar's session while flat - so an open position never has its stop/target basis
        // silently swapped out from under it by a session boundary, while a bare arm, a working
        // limit, or a pending chain are all released and rebuilt fresh under the new session.
        private void ResolveActiveSessionValues()
        {
            int currentSessIdx = GetSessionIndex(GetBarOpenRaw());
            if (!IsTradeLifecycleActive())
            {
                if (activeTradeSessionIndex != currentSessIdx)
                {
                    // The session genuinely changed while flat. Any bare arm and/or pending
                    // TP-chain re-entry was decided/armed under the OLD session's
                    // SequenceCandles/MinimumBodyPoints and must not carry into a fresh window -
                    // ClearSetupState resets setupDirection, chainDirection and chainTradeNumber
                    // together. A still-working entry limit (built off the OLD session's EMA/
                    // padding, and actively re-priced bar to bar - see IsTradeLifecycleActive's
                    // comment) is cancelled outright, same as the existing window-closed/
                    // risk-cap cancel paths elsewhere in this file - it has no fill to protect,
                    // so this is not an exit and needs no terminal-exit handling.
                    if (setupDirection != 0 || chainDirection != 0)
                        ClearSetupState("session changed");
                    if (IsOrderActive(entryOrder))
                        CancelEntryOrderIfActive("session changed");

                    // A third code-review pass found the candle-run counters (bullRun/bearRun)
                    // were NEVER reset on a session change, only the threshold used to READ
                    // them - so a run built under one session's body filter/SequenceCandles
                    // could immediately satisfy the NEXT session's (looser) requirement the
                    // instant its window opens, including across the un-gapped Asia->Europe and
                    // US 9:36-10:30->Midday handoffs and the short 09:28-09:36 gap. Resetting
                    // to 0 is the conservative direction: it can only make the live strategy
                    // arm LESS eagerly at a session open than perfect per-session isolation
                    // would, never arm on contaminated cross-session data.
                    bullRun = 0;
                    bearRun = 0;

                    activeTradeSessionIndex = currentSessIdx;
                }
            }
            else if (activeTradeSessionIndex == UnresolvedSessionIndex)
            {
                // First-ever resolution for this position/lifecycle ONLY - fifth code-review
                // pass found using "< 0" here was wrong, because -1 (GetSessionIndex's own
                // "outside every window" value) is a legitimate ALREADY-LATCHED result: if the
                // very first resolution happened to land in a gap, "< 0" kept re-triggering this
                // branch every subsequent bar until a real session opened, silently re-basing an
                // ALREADY-OPEN position's stop/target the instant it did - exactly what this
                // whole latch exists to prevent. UnresolvedSessionIndex is a sentinel no
                // GetSessionIndex() call can ever produce, so once set (even to -1) it never
                // re-triggers again for the life of this lifecycle.
                activeTradeSessionIndex = currentSessIdx;
            }

            int sessIdx = activeTradeSessionIndex;
            int entryPeriod, stopPeriod;
            switch (sessIdx)
            {
                case 3:
                    entryPeriod = us0936EntryEma; stopPeriod = us0936StopEma;
                    EntryPaddingPoints = us0936EntryPad; StopPaddingPoints = us0936StopPad;
                    MinimumSlopePoints = us0936Slope; RewardMultiple = us0936Rr;
                    SequenceCandles = us0936Seq; MinimumBodyPoints = us0936Body;
                    MinimumStopPoints = us0936MinSl; MaximumStopPoints = us0936MaxSl;
                    MinimumTargetPoints = us0936MinTp;
                    break;
                case 10:
                    entryPeriod = asiaEntryEma; stopPeriod = asiaStopEma;
                    EntryPaddingPoints = asiaEntryPad; StopPaddingPoints = asiaStopPad;
                    MinimumSlopePoints = asiaSlope; RewardMultiple = asiaRr;
                    SequenceCandles = asiaSeq; MinimumBodyPoints = asiaBody;
                    MinimumStopPoints = asiaMinSl; MaximumStopPoints = asiaMaxSl;
                    MinimumTargetPoints = asiaMinTp;
                    break;
                case 11:
                    entryPeriod = europeEntryEma; stopPeriod = europeStopEma;
                    EntryPaddingPoints = europeEntryPad; StopPaddingPoints = europeStopPad;
                    MinimumSlopePoints = europeSlope; RewardMultiple = europeRr;
                    SequenceCandles = europeSeq; MinimumBodyPoints = europeBody;
                    MinimumStopPoints = europeMinSl; MaximumStopPoints = europeMaxSl;
                    MinimumTargetPoints = europeMinTp;
                    break;
                case 12:
                    entryPeriod = preMarketEntryEma; stopPeriod = preMarketStopEma;
                    EntryPaddingPoints = preMarketEntryPad; StopPaddingPoints = preMarketStopPad;
                    MinimumSlopePoints = preMarketSlope; RewardMultiple = preMarketRr;
                    SequenceCandles = preMarketSeq; MinimumBodyPoints = preMarketBody;
                    MinimumStopPoints = preMarketMinSl; MaximumStopPoints = preMarketMaxSl;
                    MinimumTargetPoints = preMarketMinTp;
                    break;
                case 13:
                    entryPeriod = usMiddayEntryEma; stopPeriod = usMiddayStopEma;
                    EntryPaddingPoints = usMiddayEntryPad; StopPaddingPoints = usMiddayStopPad;
                    MinimumSlopePoints = usMiddaySlope; RewardMultiple = usMiddayRr;
                    SequenceCandles = usMiddaySeq; MinimumBodyPoints = usMiddayBody;
                    MinimumStopPoints = usMiddayMinSl; MaximumStopPoints = usMiddayMaxSl;
                    MinimumTargetPoints = usMiddayMinTp;
                    break;
                default:
                    // Outside every window (the 06:30-08:32 and 17:00-18:00 gaps). No entry can
                    // ever fire here (IsEntryWindowOpen blocks it), but UpdateSequenceRuns()
                    // still counts candle runs through every gap unconditionally - a second
                    // code-review pass found that leaving these nine fields un-set let the body
                    // threshold silently carry over from whatever REAL session last ran, so a
                    // gap-window run could already satisfy the NEXT session's SequenceCandles
                    // the instant that window opens, using a stale threshold. Explicit, fixed,
                    // documented fallback values instead of carryover from an arbitrary session.
                    entryPeriod = FallbackEntryPeriod; stopPeriod = FallbackStopPeriod;
                    EntryPaddingPoints = 0.5; StopPaddingPoints = 1.0;
                    MinimumSlopePoints = 2.75; RewardMultiple = 1.0;
                    SequenceCandles = 4; MinimumBodyPoints = 1.0;
                    MinimumStopPoints = 5.0; MaximumStopPoints = 30.0;
                    MinimumTargetPoints = 4.0;
                    break;
            }
            EntryEmaPeriod = entryPeriod;
            StopEmaPeriod = stopPeriod;
            ema = emaSeriesByPeriod[entryPeriod];
            slEma = emaSeriesByPeriod[stopPeriod];
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
                string.Format("EMA921_v{0}_log_{1}.csv", ResolveVersionNumberString(),
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

        // Captured when the limit is FIRST submitted for a trade, while the signal bar's context
        // is still current. The limit then usually moves for several bars before it fills; the
        // fill fragment records how many (BarsWorking) and the bracket actually attached.
        private void CaptureEntryFeatures(int direction, double signalPrice, double limitPrice,
            double stopPrice, double targetPrice)
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
                planEntryKind,
                chainTradeNumber.ToString(CultureInfo.InvariantCulture),
                (direction > 0 ? bullRun : bearRun).ToString(CultureInfo.InvariantCulture),
                N(signalPrice),
                N(ema[1]),
                N(slEma[1]),
                N(slope),
                N(slopePrev),
                N(slope - slopePrev),
                N(GetRequiredSlope()),
                N(limitPrice),
                N(stopPrice),
                N(targetPrice),
                N((signalPrice - ema[1]) * direction),
                N(Math.Abs(targetPrice - limitPrice)),
                N(Math.Abs(limitPrice - stopPrice)),
                N(Open[1]), N(High[1]), N(Low[1]), N(Close[1]), N(Volume[1]),
                N(Open[2]), N(High[2]), N(Low[2]), N(Close[2]), N(Volume[2]),
                N(Open[3]), N(High[3]), N(Low[3]), N(Close[3]), N(Volume[3]),
                N(avgVol)
            });
        }

        // EMAL-1051: StartPathRecorder is called directly from the entry-fill site, not from
        // here, so Research Log works independently of Log.
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
                N(delay < 0.0 ? 0.0 : delay),
                (entrySubmittedBar >= 0 ? CurrentBar - entrySubmittedBar : 0).ToString(CultureInfo.InvariantCulture),
                N(planStopPrice),
                N(planTargetPrice)
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
                exitTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                N(exitPrice),
                exitReason ?? string.Empty,
                N(profitPoints),
                profitPoints > 0.0 ? "1" : "0",
                (CurrentBar - pendingEntryBar).ToString(CultureInfo.InvariantCulture),
                N(tradeMaePoints),
                N(tradeMfePoints),
                N(desiredProtectionStopPrice),
                N(desiredProtectionTargetPrice)
            }));

            pendingEntryFeatures = null;
            pendingFillFeatures = null;
        }

        private void PrintFillRateSummary()
        {
            if (signalCount == 0)
                return;

            Print("================ EMA921 fill rate ================");
            // EMA921-1002: these ten fields now vary per bar with the active session, so this
            // line only reflects whichever session was active on the LAST bar processed - not a
            // strategy-wide constant anymore. Per-session values are in ResolveSessionPresets().
            Print(string.Format("  rules (last active) : seq {0} candles, body>={1}, slope>={2}, EMA {3}/{4}, pad {5}/{6}, RR {7}, SL {8}-{9}, minTP {10}",
                SequenceCandles, MinimumBodyPoints, MinimumSlopePoints, EntryEmaPeriod, StopEmaPeriod,
                EntryPaddingPoints, StopPaddingPoints, RewardMultiple, MinimumStopPoints, MaximumStopPoints, MinimumTargetPoints));
            Print(string.Format("  sessions            : Asia={0} Europe={1} PreMkt={2} 9:36-10:30={3} Midday={4}",
                AsiaSetting, EuropeSetting, PreMarketSetting, Us0936Setting, USMiddaySetting));
            Print(string.Format("  bars blocked        : {0}  (session gate)", blockedBarCount));
            Print(string.Format("  8:28-8:32 news block : always on  bars blocked: {0}", newsBlockedMinuteBarCount));
            Print("  8:29 news flatten : always on");
            Print("  9:29 cash-open force close : always on");
            Print(string.Format("  EOD Force Close ({0:hh\\:mm}-17:00) block/flatten : always on  bars blocked: {1}", EODForceCloseTime, preCloseBlockedMinuteBarCount));
            Print(string.Format("  order rate guard    : always on / {0} actions (entries blocked: {1})",
                OrderActionLimitPerHour, rateGuardBlockedEntryCount));
            Print(string.Format("  sequences armed     : {0}", setupArmCount));
            Print(string.Format("  entries submitted   : {0}  (re-entries after TP: {1})", signalCount, reEntryCount));
            Print(string.Format("  filled              : {0}  ({1:F1}%)",
                filledCount, 100.0 * filledCount / signalCount));
            Print(string.Format("  entry cancels       : {0}  (all reasons)", cancelBarEndCount));
            Print(string.Format("  SL-range skips      : {0}  (bars where the stop distance was outside Min/Max SL)", rangeSkipCount));
            Print(string.Format("  outcomes            : TP {0} / SL {1} / other {2}  (trail-cross exits: {3})",
                tpOutcomeCount, slOutcomeCount, otherOutcomeCount, trailCrossExitCount));
            Print("==================================================");
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

                if (TouchDetectionMode != EMA921TouchDetectionMode.QuoteOrLast)
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
                resolvedPathLogPath = Path.Combine(dir, string.Format("EMA921_v{0}_research_log_{1}.csv",
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
                Print("EMA921 path log write failed: " + ex.Message);
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

            // Same verdict IsEntryWindowOpen acts on (EMA921: minute-by-minute over the bar).
            string windowBlock = GetEntryWindowBlockReason();
            if (windowBlock != null)
                return windowBlock;

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

            if (CurrentBar < GetWarmupBars())
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
                new KeyValuePair<string, string>(string.Format("EMA921 v{0}", ResolveVersionNumberString()), string.Empty)
            };

            string status = GetStatusLine();
            if (!string.IsNullOrEmpty(status))
            {
                statusLineIndex = lines.Count;
                lines.Add(new KeyValuePair<string, string>(status, string.Empty));
            }

            lines.Add(new KeyValuePair<string, string>("Instrument:", instrument));
            lines.Add(new KeyValuePair<string, string>("Contracts:", Contracts.ToString(CultureInfo.InvariantCulture)));
            lines.Add(new KeyValuePair<string, string>("EMA:", string.Format(CultureInfo.InvariantCulture,
                "{0} entry / {1} stop", EntryEmaPeriod, StopEmaPeriod)));

            // Same completed-bar calc the entry gate uses (ema[1] - ema[2]) - EMAL's Slope row.
            double currentSlope = CurrentBar >= 2 ? ema[1] - ema[2] : double.NaN;
            double requiredSlopePanel = GetRequiredSlope();
            string currentSlopeText = double.IsNaN(currentSlope)
                ? "n/a"
                : currentSlope.ToString("0.##", CultureInfo.InvariantCulture);
            if (!double.IsNaN(currentSlope))
                slopeValid = Math.Abs(currentSlope) >= requiredSlopePanel;
            slopeLineIndex = lines.Count;
            lines.Add(new KeyValuePair<string, string>("Slope:",
                string.Format("{0} ({1})", requiredSlopePanel.ToString("0.##", CultureInfo.InvariantCulture), currentSlopeText)));

            string sequenceText;
            if (bullRun > 0)
                sequenceText = string.Format(CultureInfo.InvariantCulture, "Bull {0}/{1}", bullRun, SequenceCandles);
            else if (bearRun > 0)
                sequenceText = string.Format(CultureInfo.InvariantCulture, "Bear {0}/{1}", bearRun, SequenceCandles);
            else
                sequenceText = string.Format(CultureInfo.InvariantCulture, "0/{0}", SequenceCandles);
            lines.Add(new KeyValuePair<string, string>("Sequence:", sequenceText));
            lines.Add(new KeyValuePair<string, string>("Setup:", setupStatusText ?? "-"));

            bool inPosition = Position.MarketPosition != MarketPosition.Flat && bracketAnchorPrice > 0.0;
            double panelEntry = inPosition ? bracketAnchorPrice : planLimitPrice;
            double panelStop = inPosition ? desiredProtectionStopPrice : planStopPrice;
            double panelTarget = inPosition ? desiredProtectionTargetPrice : planTargetPrice;
            bool havePlan = panelEntry > 0.0 && panelStop > 0.0 && panelTarget > 0.0;
            lines.Add(new KeyValuePair<string, string>(inPosition ? "Entry (filled):" : "Entry:",
                havePlan ? panelEntry.ToString("0.00", CultureInfo.InvariantCulture) : "n/a"));
            lines.Add(new KeyValuePair<string, string>("SL:", havePlan
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.00} ({1:0.##} pts)", panelStop, Math.Abs(panelEntry - panelStop))
                : "n/a"));
            lines.Add(new KeyValuePair<string, string>("TP:", havePlan
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.00} ({1:0.##} pts)", panelTarget, Math.Abs(panelTarget - panelEntry))
                : "n/a"));
            lines.Add(new KeyValuePair<string, string>("RR / SL range:", string.Format(CultureInfo.InvariantCulture,
                "{0:0.##} / {1:0.##}-{2:0.##}", RewardMultiple, MinimumStopPoints, MaximumStopPoints)));

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
            bool sessionBlocked = session < 0 || !IsSessionEnabled(session);
            if (sessionBlocked)
                blockedValueLineIndices.Add(lines.Count);
            string sessionZoneAbbrev = GetEasternZoneAbbreviation(ConvertToEastern(raw));
            lines.Add(new KeyValuePair<string, string>("Session:", string.Format("{0} {1}{2}", sessionName, sessionZoneAbbrev,
                session >= 0 && !IsSessionEnabled(session) ? " (off)" : string.Empty)));
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

        // ---------------- standing time rules (all from EMAL) ----------------
        // Minute-of-day, New York time. On a 1-minute chart every check below is exactly EMAL's.

        // EMAL-1051: unconditional 08:28-08:32 ET entry block around the 08:30 news slot.
        private static bool IsNewsReleaseBlockedMinute(int minuteOfDay)
        {
            return minuteOfDay >= 8 * 60 + 28 && minuteOfDay < 8 * 60 + 32;
        }

        // EMAL-1061: unconditional flatten of any open position at 08:29 ET.
        private static bool IsNewsFlattenMinute(int minuteOfDay)
        {
            return minuteOfDay == 8 * 60 + 29;
        }

        // EMAL-1061: unconditional flatten of any open position at 09:29 ET (cash-open spike).
        private static bool IsCashOpenForceCloseMinute(int minuteOfDay)
        {
            return minuteOfDay == 9 * 60 + 29;
        }

        // Platform time the flatten rules evaluate against: the latest Last tick when it belongs
        // to the forming bar (realtime, or historical with Tick Replay), otherwise the bar open.
        private DateTime GetDecisionTimeRaw(out bool fromTick)
        {
            DateTime barOpen = GetBarOpenRaw();
            fromTick = lastTickTime != DateTime.MinValue && lastTickTime >= barOpen && lastTickTime <= Time[0];
            return fromTick ? lastTickTime : barOpen;
        }

        private static int MinuteOfDay(DateTime easternTime)
        {
            return easternTime.Hour * 60 + easternTime.Minute;
        }

        private int GetBarSpanMinutes()
        {
            return Math.Max(1, Math.Min(BarsPeriod.Value, 24 * 60));
        }

        // EMA921: EMAL evaluates its entry blocks on the 1-minute bar's open. A limit placed at a
        // longer bar's close stays working for that whole bar, so a bar is only tradable if EVERY
        // minute it covers is tradable (a 5m bar opening 08:25 is blocked by the 08:28 news
        // block, a 5m bar opening 09:25 by the 09:28-09:36 gap). Identical to EMAL on 1m.
        // Returns null when the window is open. reasonCode: 1 news, 2 EOD close, 3 session gate.
        private string GetEntryWindowBlockReason(out int reasonCode)
        {
            reasonCode = 0;
            int startMinute = MinuteOfDay(ConvertToEastern(GetBarOpenRaw()));
            int span = GetBarSpanMinutes();
            for (int k = 0; k < span; k++)
            {
                int m = (startMinute + k) % (24 * 60);
                if (IsNewsReleaseBlockedMinute(m))
                {
                    reasonCode = 1;
                    return "news block";
                }
                if (IsPreCloseMinute(m))
                {
                    reasonCode = 2;
                    return "EOD close";
                }
                int s = GetSessionIndexForMinute(m);
                if (s < 0 || !IsSessionEnabled(s))
                {
                    reasonCode = 3;
                    return "session gate";
                }
            }
            return null;
        }

        private string GetEntryWindowBlockReason()
        {
            int reasonCode;
            return GetEntryWindowBlockReason(out reasonCode);
        }

        private bool IsEntryWindowOpen()
        {
            int reasonCode;
            if (GetEntryWindowBlockReason(out reasonCode) == null)
                return true;

            if (reasonCode == 1)
                newsBlockedMinuteBarCount++;
            else if (reasonCode == 2)
                preCloseBlockedMinuteBarCount++;
            else
                blockedBarCount++;
            return false;
        }

        // The three standing flattens (EOD Force Close, 08:29 news, 09:29 cash open). EMAL checks
        // them on the first tick of the 1-minute bar; EMA921 checks every tick against the tick's
        // own clock, which is the same thing on 1m and still lands on the right minute on 5m.
        // Without tick data (historical, no Tick Replay) the whole bar span is checked instead.
        private string GetForcedFlattenReason()
        {
            bool fromTick;
            DateTime decisionEt = ConvertToEastern(GetDecisionTimeRaw(out fromTick));
            int startMinute = MinuteOfDay(decisionEt);
            int span = fromTick ? 1 : GetBarSpanMinutes();
            for (int k = 0; k < span; k++)
            {
                int m = (startMinute + k) % (24 * 60);
                if (IsPreCloseMinute(m))
                    return "PreCloseFlatten";
                if (IsNewsFlattenMinute(m))
                    return "NewsBlockFlatten";
                if (IsCashOpenForceCloseMinute(m))
                    return "CashOpenFlatten";
            }
            return null;
        }

        // EMA921-1002: EntryEmaPeriod/StopEmaPeriod now vary per bar with the active session
        // (ResolveActiveSessionValues), so warmup must use the max across EVERY session's
        // periods (warmupMaxPeriod, computed once in DataLoaded by BuildEmaSeriesCache), not
        // just whichever session happens to be active on the current bar - otherwise a session
        // with a longer period than today's could get used before its own EMA is fully warmed.
        private int GetWarmupBars()
        {
            return warmupMaxPeriod + 2;
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            // EMAL-1067: a naked position must be caught even when the strategy would otherwise
            // return early below, so this runs first, on every tick.
            AuditNakedPosition();

            bool firstTickOfBar = IsFirstTickOfBar;

            // EMA921-1002 (code-review fix): up to nine distinct EMA period instances can now
            // be cached (five sessions' entry+stop periods, deduplicated), not just the two the
            // active session happens to be using this bar - ALL of them need Update() every
            // tick, or a series a session switch is about to re-point ema/slEma to would be
            // stale on its first bar of use (the exact hazard the old single-pair Update() call
            // was written to prevent - see the DataLoaded/BuildEmaSeriesCache comments).
            foreach (EMA series in emaSeriesByPeriod.Values)
                series.Update();

            // EMA921-1002 (code-review fix, blocking): must run BEFORE UpdateSequenceRuns(),
            // not inside ProcessBarClose() (where it used to live) - UpdateSequenceRuns() reads
            // MinimumBodyPoints, and candle-run bookkeeping happens on EVERY completed bar,
            // in and out of every window, unconditionally. Resolving the active session only
            // once ProcessBarClose() ran meant every bar's run was counted against the
            // PREVIOUS bar's resolved body threshold - so whether a session's candle runs even
            // arm correctly depended on which OTHER session had been active moments earlier.
            if (firstTickOfBar)
                ResolveActiveSessionValues();

            // EMA921-1004 (code-review fix): must run BEFORE the historical-bars return just
            // below, not from inside UpdateLevelPlots() (which is never reached on a historical
            // bar outside the Strategy Analyzer - the three level plots SHOULD stay gated that
            // way, since they only mean anything while a plan/position exists). The
            // two Active EMA lines are meant to show the same continuous history the old
            // AddChartIndicator-based lines did; unconditional/every-tick here (not gated on
            // firstTickOfBar) preserves the same live intra-bar movement on the rightmost point
            // those lines always had.
            UpdateActiveEmaPlots();

            // Candle runs are price facts - counted on every completed bar, warmup included, so
            // the counts are already right the moment the strategy goes live.
            if (firstTickOfBar)
                UpdateSequenceRuns();

            if (State == State.Historical && !IsHistoricalTradeSimulationContext())
                return;

            if (configurationBlocked)
            {
                if (firstTickOfBar)
                    UpdateInfoText();
                UpdateLevelPlots();
                return;
            }

            bool accountRiskEnabled = MaxAccountBalance > 0.0 || MaxDailyProfit > 0.0;
            if (accountRiskEnabled && (IsAccountBalanceBlocked() || IsAccountDailyProfitBlocked()))
            {
                if (firstTickOfBar)
                {
                    ClearSetupState("risk cap");
                    UpdateInfoText();
                }
                UpdateLevelPlots();
                return;
            }

            // EMAL-1051/1059/1061: standing flattens. TrySubmitTerminalExit is idempotent while
            // an exit is already in flight, so evaluating this on every tick is safe.
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                string flattenReason = GetForcedFlattenReason();
                if (flattenReason != null)
                    TrySubmitTerminalExit(flattenReason, protectedEntrySignal);
            }

            if (!firstTickOfBar)
            {
                UpdateLevelPlots();
                return;
            }

            ClearQueuedEntry();
            ProcessBarClose();
            UpdateLevelPlots();
            UpdateInfoText();
        }

        // ---------------- EMA921 rules ----------------

        // Runs once per COMPLETED bar (bar [1] on the first tick of the next bar). A candle
        // whose body is under Min Body is invisible: it neither counts nor resets (Steve,
        // 2026-09-11). A zero-body candle is never directional, even with Min Body = 0.
        private void UpdateSequenceRuns()
        {
            if (CurrentBar < 1)
                return;

            int completedBar = CurrentBar - 1;
            if (completedBar <= lastSequenceBarProcessed)
                return;
            lastSequenceBarProcessed = completedBar;

            double body = Close[1] - Open[1];
            double minBody = Math.Max(0.0, MinimumBodyPoints) - 1e-9;

            if (body > 0.0 && body >= minBody)
            {
                bullRun++;
                bearRun = 0;
            }
            else if (body < 0.0 && -body >= minBody)
            {
                bearRun++;
                bullRun = 0;
            }
        }

        // The whole per-bar decision, on the first tick of each new bar (= the close of the
        // previous one). All levels come from completed-bar EMA values, so they are fixed for
        // the whole bar and identical on every account running on the same feed.
        private void ProcessBarClose()
        {
            // EMA921-1002 (code-review fix): ResolveActiveSessionValues() now runs earlier, at
            // the top of OnBarUpdate() before UpdateSequenceRuns() - see that call site's
            // comment for why. Values are already current for this bar by the time we get here.

            // A position that went flat through a path no exit handler recognises (manual or
            // broker-side flatten, NinjaTrader's own session-close exit) still ends the chain.
            if (positionOpenTracked && Position.MarketPosition == MarketPosition.Flat)
                RecordTradeOutcome("UntrackedFlat");

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                // Never a second entry while a position is open (EMAL behaviour).
                CancelEntryOrderIfActive("position-open");
                UpdateTrailingBracket();
                return;
            }

            if (CurrentBar < GetWarmupBars())
            {
                CancelEntryOrderIfActive("warmup");
                setupStatusText = "warmup";
                return;
            }

            // Standing time rules and disabled sessions pull any working limit and end the setup
            // and any TP chain; a new sequence is needed once the window reopens (the candle
            // runs keep counting throughout, so a run still in progress re-arms immediately).
            if (!IsEntryWindowOpen())
            {
                CancelEntryOrderIfActive("window-closed");
                if (setupDirection != 0 || chainDirection != 0)
                    ClearSetupState("window closed");
                ClearPlan();
                setupStatusText = "window closed";
                return;
            }

            // 1. Arm from the candle runs. Only while nothing is working and no TP chain is
            //    active: a working limit is never replaced (Steve: nothing cancels it), and a
            //    chain only ends on a stop.
            if (chainDirection == 0 && !IsOrderActive(entryOrder))
            {
                int runDirection = bullRun >= SequenceCandles ? 1 : (bearRun >= SequenceCandles ? -1 : 0);
                if (runDirection != 0 && runDirection != setupDirection)
                {
                    setupDirection = runDirection;
                    setupSlopeConfirmed = false;
                    chainTradeNumber = 1;
                    setupArmCount++;
                    Print(string.Format(CultureInfo.InvariantCulture,
                        "{0} | EMA921 SEQUENCE ARMED | {1} | run={2} (need {3}, body>={4})",
                        Time[0], runDirection > 0 ? "LONG" : "SHORT",
                        runDirection > 0 ? bullRun : bearRun, SequenceCandles, MinimumBodyPoints));
                }
            }

            int direction = chainDirection != 0 ? chainDirection : setupDirection;
            string side = direction > 0 ? "Long" : "Short";
            if (direction == 0)
            {
                ClearPlan();
                setupStatusText = "waiting for sequence";
                return;
            }

            // 2. Slope gate - first entry of a sequence only, never for TP re-entries.
            if (chainDirection == 0 && !setupSlopeConfirmed)
            {
                double slope = ema[1] - ema[2];
                double requiredSlope = GetRequiredSlope();
                bool slopeOk = direction > 0
                    ? slope > 0.0 && slope >= requiredSlope
                    : slope < 0.0 && slope <= -requiredSlope;
                if (!slopeOk)
                {
                    ClearPlan();
                    setupStatusText = side + " armed, waiting for slope";
                    return;
                }
                setupSlopeConfirmed = true;
            }

            // 3. Levels from the completed bar.
            double limitPrice, stopPrice, targetPrice, stopDistance;
            ComputePlan(direction, out limitPrice, out stopPrice, out targetPrice, out stopDistance);
            planDirection = direction;
            planLimitPrice = limitPrice;
            planStopPrice = stopPrice;
            planTargetPrice = targetPrice;
            planEntryKind = chainDirection != 0 ? "ReEntry" : "Setup";

            // Min/Max SL: outside the range the trade is not allowed - no limit is placed and a
            // working one is pulled; the setup / chain keeps waiting for the range to come back.
            double tolerance = TickSize / 2.0;
            if (stopDistance < TickSize
                || stopDistance < MinimumStopPoints - tolerance
                || stopDistance > MaximumStopPoints + tolerance)
            {
                rangeSkipCount++;
                setupStatusText = string.Format(CultureInfo.InvariantCulture,
                    "{0} {1}: SL {2:0.##} pts out of range", side, chainDirection != 0 ? "re-entry" : "setup", stopDistance);
                CancelEntryOrderIfActive("sl-range");
                return;
            }

            if (IsOrderActive(entryOrder))
            {
                MoveWorkingEntry(direction, limitPrice, stopPrice, targetPrice);
                setupStatusText = string.Format(CultureInfo.InvariantCulture, "{0} limit working{1}",
                    side, chainDirection != 0 ? " (re-entry #" + chainTradeNumber + ")" : string.Empty);
                return;
            }

            // 4. Submit.
            int submittedBefore = signalCount;
            QueueEntry(direction, limitPrice, stopPrice, targetPrice);
            TrySubmitQueuedEntry();
            setupStatusText = signalCount != submittedBefore
                ? string.Format(CultureInfo.InvariantCulture, "{0} limit placed{1}",
                    side, chainDirection != 0 ? " (re-entry #" + chainTradeNumber + ")" : string.Empty)
                : side + " entry blocked (" + GetTradeGateState() + ")";
        }

        // Limit at entry EMA +/- Entry Padding (positive = toward the trade, i.e. ABOVE the EMA
        // for a long so a shallow pullback that does not quite reach the EMA still fills), stop
        // at stop EMA -/+ SL Padding, target from the RR multiple.
        private void ComputePlan(int direction, out double limitPrice, out double stopPrice,
            out double targetPrice, out double stopDistance)
        {
            double entryEma = ema[1];
            double stopEma = slEma[1];
            limitPrice = Instrument.MasterInstrument.RoundToTickSize(
                direction > 0 ? entryEma + EntryPaddingPoints : entryEma - EntryPaddingPoints);
            stopPrice = Instrument.MasterInstrument.RoundToTickSize(
                direction > 0 ? stopEma - StopPaddingPoints : stopEma + StopPaddingPoints);
            stopDistance = (limitPrice - stopPrice) * direction;
            targetPrice = ComputeTargetPrice(direction, limitPrice, stopPrice);
        }

        // TP = anchor + RR x (anchor - stop), never closer than Min TP Distance to the anchor
        // (Steve, 2026-09-11). Once the stop has trailed past the anchor the risk term is <= 0
        // and the floor takes over.
        private double ComputeTargetPrice(int direction, double anchorPrice, double stopPrice)
        {
            double risk = (anchorPrice - stopPrice) * direction;
            double reward = Math.Max(RewardMultiple * risk, Math.Max(MinimumTargetPoints, TickSize));
            return Instrument.MasterInstrument.RoundToTickSize(anchorPrice + direction * reward);
        }

        // Moves the working entry to this bar's plan. ChangeOrder amends in place, so the order
        // is never absent from the book the way a cancel/replace would leave it.
        private void MoveWorkingEntry(int direction, double limitPrice, double stopPrice, double targetPrice)
        {
            if (entryOrder == null
                || entryCancelPending
                || IsOrderInFlight(entryOrder)
                || IsHistoricalOrderAwaitingRealtimeTransition(entryOrder))
            {
                return;
            }

            // Gap latch / touch watchdog follow the plan (they are plan-derived by design).
            ArmGapLatch(direction, stopPrice, targetPrice);

            if (Math.Abs(entryOrder.LimitPrice - limitPrice) < TickSize / 2.0)
                return;

            if (IsExecutionDiagnosticsActive())
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | EMA921 ENTRY TRACE | move limit {1:F2} -> {2:F2} | stop={3:F2} target={4:F2}",
                    Time[0], entryOrder.LimitPrice, limitPrice, stopPrice, targetPrice));
            }

            RecordNtOrderAction("change-entry");
            ChangeOrder(entryOrder, entryOrder.Quantity, limitPrice, 0.0);
            SyncProjectXEntryMove(direction, limitPrice, targetPrice, stopPrice);
        }

        // Post-fill, once per bar close: stop follows the stop EMA (both directions - Steve,
        // 2026-09-11), capped at Max SL from the anchor. TARGET IS FROZEN AT FILL (Steve,
        // 2026-09-11, revised) - only the stop trails. Earlier draft re-derived the target from
        // the CURRENT (trailing) stop every bar via ComputeTargetPrice, so as the stop closed in
        // on the anchor the target ratcheted DOWN toward the MinimumTargetPoints floor regardless
        // of RewardMultiple - the resting target was rarely the real exit and RewardMultiple's
        // effect on outcomes was mostly washed out by the floor-collapse dynamic. Steve confirmed
        // this was not intended: the target must stay fixed at RewardMultiple x the risk measured
        // AT FILL (already computed once in InitializeBracketFromPlan), and only the stop moves.
        private void UpdateTrailingBracket()
        {
            // Any exit already in flight (including a touch-watchdog conversion that has fired)
            // owns the position now - re-submitting a leg here could race it.
            if (terminalExitPending || terminalExitCancelPending
                || targetTouchWatchdogCancelPending || targetTouchWatchdogFired
                || IsTerminalExitRetryWaiting())
            {
                return;
            }

            int direction = bracketDirection;
            double anchor = bracketAnchorPrice;
            if (direction == 0 || anchor <= 0.0 || CurrentBar < 1)
                return;

            MarketPosition positionDirection = Position.MarketPosition;
            if ((direction > 0) != (positionDirection == MarketPosition.Long))
                return;

            // A protective leg still in flight (just submitted, or a previous change not yet
            // acknowledged) is not amended again; the next bar close catches up.
            if (IsOrderInFlight(protectiveStopOrder) || IsOrderInFlight(profitTargetOrder))
                return;

            double stopPrice = Instrument.MasterInstrument.RoundToTickSize(
                direction > 0 ? slEma[1] - StopPaddingPoints : slEma[1] + StopPaddingPoints);
            double widestStop = anchor - direction * MaximumStopPoints;
            if ((anchor - stopPrice) * direction > MaximumStopPoints)
                stopPrice = Instrument.MasterInstrument.RoundToTickSize(widestStop);
            // FROZEN at fill by InitializeBracketFromPlan - do NOT recompute from the live stop.
            // See the method comment above.
            double targetPrice = desiredProtectionTargetPrice;

            // A sell stop at/above the bid (buy stop at/below the ask) cannot be placed - price
            // has already crossed the new stop level, which IS the stop being hit. Exit now via
            // the cancel-then-confirm path rather than letting the broker reject the change.
            double market = GetProtectiveReferencePrice(positionDirection);
            bool stopCrossed = direction > 0 ? stopPrice >= market : stopPrice <= market;
            if (stopCrossed)
            {
                trailCrossExitCount++;
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | EMA921 TRAIL | new stop {1:F2} already crossed (market {2:F2}) - exiting",
                    Time[0], stopPrice, market));
                TrySubmitTerminalExit("TrailStop", protectedEntrySignal);
                return;
            }

            desiredProtectionStopPrice = stopPrice;
            // desiredProtectionTargetPrice deliberately NOT reassigned - it stays at its
            // fill-time value for the life of the trade.
            // Keep the stop-side latch on the LIVE stop, so EMAL's recovery path (which checks it
            // before re-attaching protection) judges against the level actually in force. Passing
            // the (unchanged) target keeps plannedTargetTouchLevel consistent; it is a no-op
            // write since the target never moves post-fill.
            if (gapLatchArmed)
                ArmGapLatch(direction, stopPrice, targetPrice);

            int quantity = desiredProtectionQuantity > 0 ? desiredProtectionQuantity : Math.Abs(Position.Quantity);
            SubmitOrUpdateProtection(positionDirection, anchor, quantity, Time[0], false);
        }

        // First execution of an entry: freeze the anchor at the FILL price (Steve, 2026-09-11,
        // option (a)) and attach the planned stop. The limit price is plan-derived and identical
        // on every account, so on a normal limit fill every account gets the same anchor and
        // therefore the same TP; only a gap-through fill (better than the limit) differs, and
        // then the real fill is the right anchor anyway. The stop distance is clamped into
        // [Min SL, Max SL] here, which only matters when a fill raced a bar-close cancel/move.
        private void InitializeBracketFromPlan(int direction, double fillPrice)
        {
            double anchor = fillPrice > 0.0 ? fillPrice : planLimitPrice;
            double stopPrice = planDirection == direction ? planStopPrice : 0.0;

            if (stopPrice <= 0.0 || (anchor - stopPrice) * direction <= 0.0)
            {
                // Recovery: no usable plan (state lost across a reconnect, or the plan was cleared
                // in a cancel/fill race). Rebuild from the stop EMA, same clamp as the trail.
                stopPrice = CurrentBar >= 1
                    ? (direction > 0 ? slEma[1] - StopPaddingPoints : slEma[1] + StopPaddingPoints)
                    : 0.0;
                if (stopPrice <= 0.0 || (anchor - stopPrice) * direction < MinimumStopPoints)
                    stopPrice = anchor - direction * MinimumStopPoints;
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | EMA921 BRACKET RECOVERY | no plan for this fill - stop rebuilt at {1:F2}", Time[0], stopPrice));
            }
            if ((anchor - stopPrice) * direction > MaximumStopPoints)
                stopPrice = anchor - direction * MaximumStopPoints;
            if ((anchor - stopPrice) * direction < MinimumStopPoints)
                stopPrice = anchor - direction * MinimumStopPoints;

            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            bracketDirection = direction;
            bracketAnchorPrice = anchor;
            desiredProtectionStopPrice = stopPrice;
            desiredProtectionTargetPrice = ComputeTargetPrice(direction, anchor, stopPrice);
            plannedTargetTouchLevel = desiredProtectionTargetPrice;
            positionOpenTracked = true;
        }

        // Called with the position FLAT, from the exit handler (or ProcessBarClose for an
        // untracked flat). Rule 8: a target starts / continues the re-entry chain. Rule 9: a stop
        // resets everything including the candle runs, so a brand-new sequence is required.
        // Any other exit (forced flattens, safety exits, manual) ends the chain and the setup
        // but leaves the candle runs alone.
        private void RecordTradeOutcome(string exitOrderName)
        {
            int direction = bracketDirection != 0 ? bracketDirection : openEntryDirection;
            string name = exitOrderName ?? string.Empty;
            bool isTarget = name == TargetExitSignal || name == TargetTouchExitSignal;
            bool isStop = name == StopExitSignal
                || name == TerminalExitSignalPrefix + "GapStop"
                || name == TerminalExitSignalPrefix + "TrailStop";

            positionOpenTracked = false;
            setupDirection = 0;
            setupSlopeConfirmed = false;
            ClearPlan();

            if (isTarget && direction != 0)
            {
                tpOutcomeCount++;
                chainDirection = direction;
                chainTradeNumber = Math.Max(1, chainTradeNumber) + 1;
                setupStatusText = (direction > 0 ? "Long" : "Short") + " TP - re-entry next bar";
            }
            else if (isStop)
            {
                slOutcomeCount++;
                chainDirection = 0;
                chainTradeNumber = 0;
                bullRun = 0;
                bearRun = 0;
                setupStatusText = "stopped - waiting for new sequence";
            }
            else
            {
                otherOutcomeCount++;
                chainDirection = 0;
                chainTradeNumber = 0;
                setupStatusText = "exit (" + name + ") - waiting for sequence";
            }

            Print(string.Format(CultureInfo.InvariantCulture,
                "{0} | EMA921 OUTCOME | exit={1} -> {2}",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0], name,
                isTarget ? "TP, re-entry chain " + (direction > 0 ? "LONG" : "SHORT")
                    : (isStop ? "SL, full reset" : "chain ended")));

            ResetBracketState();
        }

        private void ResetBracketState()
        {
            bracketDirection = 0;
            bracketAnchorPrice = 0.0;
            desiredProtectionStopPrice = 0.0;
        }

        private void ClearPlan()
        {
            planDirection = 0;
            planLimitPrice = 0.0;
            planStopPrice = 0.0;
            planTargetPrice = 0.0;
            planEntryKind = string.Empty;
        }

        private void ClearSetupState(string reason)
        {
            setupDirection = 0;
            setupSlopeConfirmed = false;
            chainDirection = 0;
            chainTradeNumber = 0;
            ClearPlan();
            setupStatusText = string.IsNullOrEmpty(reason) ? "-" : reason;
        }

        // Entry limit / stop / target as per-bar hash marks: the plan while a limit is working,
        // the live bracket while a position is open, nothing otherwise.
        private void UpdateLevelPlots()
        {
            if (CurrentBar < 0)
                return;

            bool inPosition = Position.MarketPosition != MarketPosition.Flat && bracketAnchorPrice > 0.0;
            bool working = !inPosition && IsOrderActive(entryOrder) && planLimitPrice > 0.0;
            double entryLevel = inPosition ? bracketAnchorPrice : (working ? planLimitPrice : 0.0);
            double stopLevel = inPosition ? desiredProtectionStopPrice : (working ? planStopPrice : 0.0);
            double targetLevel = inPosition ? desiredProtectionTargetPrice : (working ? planTargetPrice : 0.0);

            if (entryLevel > 0.0) Values[0][0] = entryLevel; else Values[0].Reset();
            if (stopLevel > 0.0) Values[1][0] = stopLevel; else Values[1].Reset();
            if (targetLevel > 0.0) Values[2][0] = targetLevel; else Values[2].Reset();
        }

        // EMA921-1004: the two continuous "Active Entry/Stop EMA" lines - always drawn, not
        // gated on a plan/position existing like the three plots above (UpdateLevelPlots), and
        // called from a point in OnBarUpdate BEFORE the historical-bars early return (unlike
        // UpdateLevelPlots, which is deliberately never reached on a historical bar outside the
        // Strategy Analyzer) so these lines paint across the full loaded chart history, the same
        // as the old per-period AddChartIndicator lines did - only now there is exactly one of each, always reflecting
        // whichever session is LATCHED active right now (see ResolveActiveSessionValues), instead
        // of up to nine lines shown permanently regardless of which one was actually in use.
        // ema/slEma are guaranteed non-null and already Update()-called for this tick by the time
        // this runs - seeded in BuildEmaSeriesCache (DataLoaded, before any OnBarUpdate call) and
        // refreshed every tick by the series.Update() loop just above this call site.
        private void UpdateActiveEmaPlots()
        {
            if (CurrentBar < 0)
                return;

            Values[3][0] = ema[0];
            Values[4][0] = slEma[0];
        }

        private void QueueEntry(int direction, double limitPrice, double stopPrice, double targetPrice)
        {
            queuedDirection = direction;
            queuedEntryBar = CurrentBar;
            queuedSignalTimestamp = IsExecutionDiagnosticsActive()
                ? Stopwatch.GetTimestamp()
                : 0L;
            queuedSignalUtc = queuedSignalTimestamp > 0L ? DateTime.UtcNow : DateTime.MinValue;

            queuedSignalPrice = Close[0];
            queuedLimitPrice = limitPrice;
            queuedStopPrice = stopPrice;
            queuedTargetPrice = targetPrice;
        }

        private void TrySubmitQueuedEntry()
        {
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
            double stopPrice = queuedStopPrice;
            double targetPrice = queuedTargetPrice;
            double signalPrice = queuedSignalPrice;
            long signalTimestamp = queuedSignalTimestamp;
            DateTime signalUtc = queuedSignalUtc;
            ClearQueuedEntry();

            entryCancelPending = false;
            signalCount++;
            if (chainDirection != 0)
                reEntryCount++;
            entrySubmittedBar = CurrentBar;

            string entrySignal = direction > 0 ? LongEntrySignal : ShortEntrySignal;

            BeginProtectionTracking(entrySignal, direction, limitPrice, stopPrice, targetPrice);

            CaptureEntryFeatures(direction, signalPrice, limitPrice, stopPrice, targetPrice);

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

            // EMAL-1041: ProjectX enqueue strictly after the NT submission call.
            SendPlannedProjectXEntry(direction, limitPrice, targetPrice, stopPrice);

            Print(string.Format(CultureInfo.InvariantCulture,
                "{0} | EMA921 ENTRY | {1} {2} limit={3:F2} stop={4:F2} target={5:F2} | SL {6:0.##} pts, TP {7:0.##} pts",
                Time[0], planEntryKind, direction > 0 ? "LONG" : "SHORT", limitPrice, stopPrice, targetPrice,
                Math.Abs(limitPrice - stopPrice), Math.Abs(targetPrice - limitPrice)));

            if (diagnosticsActive)
            {
                long submitReturnedTimestamp = Stopwatch.GetTimestamp();
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | EMA921 EXECUTION | account={1} signal={2} limit={3:F2} tp={4:F2} sl={5:F2} "
                    + "signalUtc={6:O} submitStartUtc={7:O} signalToSubmitStartMs={8:F3} submitCallMs={9:F3}",
                    Time[0],
                    Account != null ? Account.Name : "-",
                    entrySignal,
                    limitPrice,
                    targetPrice,
                    stopPrice,
                    entryLatencySignalUtc,
                    entryLatencySubmitStartUtc,
                    ElapsedMilliseconds(entryLatencySignalTimestamp, entryLatencySubmitStartTimestamp),
                    ElapsedMilliseconds(entryLatencySubmitStartTimestamp, submitReturnedTimestamp)));
            }
        }

        // EMAL-1044: reason is diagnostic-only (feeds entryCancelReason / LogEntryOrderTransition).
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
                    "{0} | EMA921 ENTRY TRACE | cancel requested | reason={1} orderName={2} limit={3:F2} bid={4:F2} ask={5:F2}",
                    lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                    entryCancelReason,
                    entryOrder != null ? entryOrder.Name : "-",
                    entryOrder != null ? entryOrder.LimitPrice : 0.0,
                    GetCurrentBid(),
                    GetCurrentAsk()));
            }
            RecordNtOrderAction("cancel-entry-" + entryCancelReason);
            CancelOrder(entryOrder);
        }

        private void ClearActiveEntryContext()
        {
            entryCancelPending = false;
        }

        private void BeginProtectionTracking(string entrySignal, int direction, double limitPrice,
            double stopPrice, double targetPrice)
        {
            protectedEntrySignal = entrySignal ?? string.Empty;
            entryFillValue = 0.0;
            entryFilledQuantity = 0;
            desiredProtectionTargetPrice = 0.0;
            desiredProtectionQuantity = 0;
            ResetBracketState();
            protectiveStopOrder = null;
            profitTargetOrder = null;
            unprotectedSinceUtc = DateTime.MinValue;   // EMAL-1068 finding 5
            terminalExitPending = false;
            stopReconcileAttempts = 0;
            targetReconcileAttempts = 0;
            ResetGapLatchTracking();
            ArmGapLatch(direction, stopPrice, targetPrice);
        }

        // EMAL-1037 pattern, EMA921 levels: armed from the PLAN (shared chart data) before the
        // order exists, re-armed every bar close while the entry is working because the planned
        // stop moves with the stop EMA. Stop side only - see the gapStopBreached field comment.
        // Also latches the target-touch watchdog's shared level (EMAL-1042).
        private void ArmGapLatch(int direction, double stopPrice, double targetPrice)
        {
            if (direction == 0 || stopPrice <= 0.0)
            {
                gapLatchArmed = false;
                plannedTargetTouchLevel = 0.0;
                if (!plannedTargetTouchLevelFallbackLogged)
                {
                    plannedTargetTouchLevelFallbackLogged = true;
                    Print("EMA921 TARGET TOUCH WATCHDOG: no planned levels at arming time - "
                        + "falling back to the working target price for this trade.");
                }
                return;
            }

            double roundedStop = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            if (!gapLatchArmed || gapLatchDirection != direction || Math.Abs(roundedStop - gapLatchStopPrice) >= TickSize / 2.0)
                gapStopBreached = false;   // a breach of an older, different level is meaningless now

            gapLatchDirection = direction;
            gapLatchStopPrice = roundedStop;
            gapLatchArmed = true;
            plannedTargetTouchLevel = targetPrice > 0.0
                ? Instrument.MasterInstrument.RoundToTickSize(targetPrice)
                : 0.0;
        }

        // Runs on every Last tick (and Bid/Ask under QuoteOrLast) while armed. Stop side only.
        // try/catch is required: RealtimeErrorHandling.IgnoreAllErrors would otherwise hide a
        // fault and leave a stale latch.
        private void EvaluateGapLatch(MarketDataType source, double price, DateTime tickTime)
        {
            if (!gapLatchArmed || price <= 0.0)
                return;

            // Long's reference side is Bid, Short's is Ask (EMAL convention); Last always counts.
            if (source == MarketDataType.Bid && gapLatchDirection <= 0)
                return;
            if (source == MarketDataType.Ask && gapLatchDirection >= 0)
                return;

            try
            {
                bool breached = gapLatchDirection > 0
                    ? price <= gapLatchStopPrice
                    : price >= gapLatchStopPrice;
                if (!gapStopBreached && breached)
                {
                    gapStopBreached = true;
                    if (IsExecutionDiagnosticsActive())
                    {
                        Print(string.Format(CultureInfo.InvariantCulture,
                            "{0} | EMA921 GAP BREACH DETECTED | side={1} level=stop source={2} stop={3:F2} price={4:F2}",
                            tickTime, gapLatchDirection > 0 ? "Long" : "Short", source, gapLatchStopPrice, price));
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
                        "{0} | EMA921 TARGET TOUCH WATCHDOG DISABLED | Contracts={1} Position.Quantity={2} - "
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
                        "{0} | EMA921 TARGET TOUCH WATCHDOG | first touch | source={1} plannedTargetTouchLevel={2:F2} "
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
                "{0} | EMA921 TARGET TOUCH WATCHDOG | price traded through target and limit unfilled after {1}ms "
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
                "{0} | EMA921 TARGET TOUCH WATCHDOG | target cancel confirmed unfilled - exiting at market | side={1} entry={2}",
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

        private static bool IsOrderInFlight(Order order)
        {
            return order != null
                && (order.OrderState == OrderState.Submitted
                    || order.OrderState == OrderState.ChangePending
                    || order.OrderState == OrderState.ChangeSubmitted
                    || order.OrderState == OrderState.CancelPending
                    || order.OrderState == OrderState.CancelSubmitted);
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
            queuedStopPrice = 0.0;
            queuedTargetPrice = 0.0;
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
                "{0} | EMA921 EXECUTION | account={1} signal={2} firstOrderState={3} "
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
                "{0} | EMA921 ENTRY TRACE | seq={1} state={2} orderName={3} limit={4:F2} bid={5:F2} "
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
                    "{0} | EMA921 GAP RACE | entry filled before cancel confirmed | cancelReason={1} fill={2:F2}",
                    time, entryCancelReason, averageFillPrice));
            }
            else if (orderState == OrderState.Cancelled && cancelWasPending)
            {
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} | EMA921 ENTRY TRACE | cancel confirmed | reason={1}",
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
                "{0} | EMA921 EXECUTION | account={1} signal={2} firstFill "
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

            // The strategy trades the chart's own bar series (no internal AddDataSeries). Any
            // MINUTE period works (built for 1m, also used on 5m - Steve, 2026-09-11); the time
            // rules need minute bars because GetBarOpenRaw derives the bar open from the period.
            if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value < 1)
            {
                configurationBlocked = true;
                configurationBlockReason = string.Format("needs a minute chart (is {0} {1})",
                    BarsPeriod.Value, BarsPeriod.BarsPeriodType);
                Print("EMA921 DISABLED: requires a minute chart (1m or 5m). Current series is "
                    + BarsPeriod.Value + " " + BarsPeriod.BarsPeriodType
                    + ". No orders will be submitted.");
            }

            // EMA921-1002 (code-review fix): the old "settings sanity" check here read
            // MaximumStopPoints/MinimumStopPoints directly, which was correct when those were
            // fixed global values but is now vacuous - ValidateChart() runs BEFORE
            // ResolveSessionPresets() populates real per-session values, so this always saw the
            // inert SetDefaults placeholder (5.0/30.0, always valid) regardless of what any
            // session is actually configured to trade on. See ValidateSessionPresets(), called
            // separately AFTER ResolveSessionPresets() in DataLoaded, for the real check.

            string instrumentName = Instrument == null || Instrument.MasterInstrument == null
                ? string.Empty
                : Instrument.MasterInstrument.Name;

            if (!string.Equals(instrumentName, "NQ", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(instrumentName, "MNQ", StringComparison.OrdinalIgnoreCase))
            {
                configurationBlocked = true;
                configurationBlockReason = string.Format("NQ/MNQ only (is '{0}')", instrumentName);
                Print("EMA921 DISABLED: supports NQ and MNQ only. Current instrument is '"
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
                                "{0} | EMA921 TARGET TOUCH WATCHDOG | target filled before cancel confirmed - no market exit sent",
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
                                    "{0} | EMA921 EXIT TRACE | protective order filled during "
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
                        "{0} | EMA921 TARGET TOUCH WATCHDOG | market exit rejected | error={1} comment={2} | "
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

                // EMA921: the setup / chain step that produced this entry is consumed; what
                // happens next is decided by how the trade exits (RecordTradeOutcome).
                setupDirection = 0;
                setupSlopeConfirmed = false;
                chainDirection = 0;
                setupStatusText = (openEntryDirection > 0 ? "Long" : "Short") + " in trade";
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
                    bool planMatches = planDirection == recoveredDirection && planStopPrice > 0.0;
                    BeginProtectionTracking(orderName, recoveredDirection, price,
                        planMatches ? planStopPrice : 0.0, planMatches ? planTargetPrice : 0.0);
                }

                // EMA921: first execution of this entry freezes the anchor and the planned
                // bracket (partial fills of a multi-lot entry keep the same anchor).
                if (entryFilledQuantity == 0 || bracketAnchorPrice <= 0.0)
                    InitializeBracketFromPlan(orderName == LongEntrySignal ? 1 : -1, price);

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

                // EMA921 rules 8/9 - must run before ResetProtectionTracking clears the bracket.
                if (positionIsFlat)
                    RecordTradeOutcome(orderName);

                CancelRemainingEntryAfterExit();

                if (positionIsFlat)
                    ResetProtectionTracking();
                else if (IsTerminalExitOrderName(orderName))
                    ScheduleTerminalExitRetry("ResidualPosition", execution.Order.FromEntrySignal, false);
            }
        }

        // EMA921: the bracket PRICES come from the EMA plan (InitializeBracketFromPlan at the fill,
        // UpdateTrailingBracket every bar after) rather than EMAL's fixed distances from the
        // fill. Everything else - stop-first staging, the target only after the stop is
        // accepted, MissingStop / MissingTarget, the stop-side gap check - is EMAL's code.
        // checkGapLatch=false for the per-bar trail: the latch answers "was the stop level
        // crossed before protection first attached", which is only meaningful at the fill (and
        // in EMAL's recovery path); mid-trade the working stop order itself handles a cross.
        private void SubmitOrUpdateProtection(MarketPosition positionDirection, double averageEntryPrice,
            int protectedQuantity, DateTime time, bool checkGapLatch = true)
        {
            if (positionDirection == MarketPosition.Flat
                || protectedQuantity <= 0)
            {
                return;
            }

            int direction = positionDirection == MarketPosition.Long ? 1 : -1;
            if (bracketDirection != direction
                || bracketAnchorPrice <= 0.0
                || desiredProtectionStopPrice <= 0.0
                || desiredProtectionTargetPrice <= 0.0)
            {
                // Recovery: a position exists without a bracket (state lost). Rebuild one.
                InitializeBracketFromPlan(direction, averageEntryPrice);
            }

            double stopPrice = Instrument.MasterInstrument.RoundToTickSize(desiredProtectionStopPrice);
            double targetPrice = Instrument.MasterInstrument.RoundToTickSize(desiredProtectionTargetPrice);
            desiredProtectionTargetPrice = targetPrice;
            desiredProtectionQuantity = protectedQuantity;

            if (State == State.Realtime && checkGapLatch)
            {
                // EMAL-1037: the gap decision is read from the tick-driven latch armed from the
                // plan, identical for every instance on the feed regardless of callback timing.
                double marketPrice = GetProtectiveReferencePrice(positionDirection);
                if (gapStopBreached)
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
            if (MultiContractProtectionFix == EMA921MultiContractProtectionFix.Off)
                return;
            if (MultiContractProtectionFix == EMA921MultiContractProtectionFix.Auto && Position.Quantity <= 1)
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
                        "{0} | EMA921 NAKED AUDIT | fault clock CLEAR after {1:F2}s (no flatten) "
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
                    "{0} | EMA921 NAKED AUDIT | fault clock START | stopActive={1} targetActive={2} "
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
                "{0} | EMA921 NAKED AUDIT | protection INCOMPLETE for {1}s - FLATTENING | "
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
                "{0} | EMA921 EXIT TRACE | terminal exit requested | reason={1} position={2} qty={3} "
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
                Print("EMA921 CRITICAL: could not cancel working entry during termination: " + ex.Message);
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
                Print("EMA921 CRITICAL: ProjectX mirror could not be verified flat during strategy termination.");
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
            ResetBracketState();
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
            gapLatchStopPrice = 0.0;
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
                ? (object)("EMA921-account:" + (Account.Name ?? string.Empty))
                : (object)"EMA921-no-account";
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
                "{0} | CRITICAL ORDER RATE LIMIT | all EMA921 entries on this connection blocked until {1:HH:mm:ss} UTC | protection/exits still allowed | {2}",
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
                        "{0} | EMA921-1069 | clearing stale terminal-exit cancel window observed while FLAT "
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
                Name = "EMA921-ProjectX-" + orderRateInstanceId
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
        // EMA921: takes the planned target / stop as PRICES (the EMA plan), not EMAL's fixed
        // point distances. ProjectXPlaceOrder converts them to bracket ticks from the entry.
        private void SendPlannedProjectXEntry(int direction, double plannedEntryPrice,
            double plannedTargetPrice, double plannedStopPrice)
        {
            if (State != State.Realtime || !IsProjectXConfigured())
                return;

            double entry = Instrument.MasterInstrument.RoundToTickSize(plannedEntryPrice);
            double target = Instrument.MasterInstrument.RoundToTickSize(plannedTargetPrice);
            double stop = Instrument.MasterInstrument.RoundToTickSize(plannedStopPrice);

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

        // EMA921: the NT entry limit moves every bar close; the ProjectX mirror follows. Modify
        // the mirrored entry's limit in place (worker: "modify-entry"). If the mirror was never
        // placed, place it now. If the modify is refused (typically: the mirror order is no
        // longer open), the worker falls back to EMAL's normal cancel/flatten-then-place path,
        // which re-aligns ProjectX to NT (NT is the master: its entry is still unfilled).
        // After a successful modify the ProjectX bracket ticks are stale (they were sized for the
        // original plan), so the last-synced protection prices are cleared - the first NT stop /
        // target callback after the fill then always pushes the real levels across.
        private void SyncProjectXEntryMove(int direction, double plannedEntryPrice,
            double plannedTargetPrice, double plannedStopPrice)
        {
            if (State != State.Realtime || !IsProjectXConfigured())
                return;

            bool mirrorActive;
            lock (projectXStateLock)
                mirrorActive = projectXEntryMirrorActive;

            if (!mirrorActive)
            {
                SendPlannedProjectXEntry(direction, plannedEntryPrice, plannedTargetPrice, plannedStopPrice);
                return;
            }

            double entry = Instrument.MasterInstrument.RoundToTickSize(plannedEntryPrice);
            double target = Instrument.MasterInstrument.RoundToTickSize(plannedTargetPrice);
            double stop = Instrument.MasterInstrument.RoundToTickSize(plannedStopPrice);

            string instrumentRoot, instrumentKey;
            DateTime instrumentExpiry;
            bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey,
                out instrumentExpiry, out hasInstrumentExpiry);

            var item = new ProjectXWorkItem
            {
                EventType = "modify-entry",
                EntrySide = direction > 0 ? "buy" : "sell",
                EntryPrice = entry,
                TakeProfit = target,
                StopLoss = stop,
                IsMarketEntry = false,
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
                            projectXLastSyncedStopPrice = 0.0;
                            projectXLastSyncedTargetPrice = 0.0;
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

                        case "modify-entry":
                            // EMA921: move the mirrored entry limit; fall back to a fresh
                            // prepare+place (EMAL's buy/sell path) if the modify is refused.
                            if (ProjectXModifyEntryOrder(account.Id, contractId, item.EntryPrice, item.Quantity)
                                || (ProjectXPrepareForEntry(account.Id, contractId)
                                    && ProjectXPlaceOrder(item.EntrySide, account.Id, contractId, item.EntryPrice,
                                        item.TakeProfit, item.StopLoss, false, item.Quantity,
                                        item.TickSizeSnapshot)))
                            {
                                sentAny = true;
                            }
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

        // EMA921: amend the mirrored entry's limit price in place. Returns false when there is
        // no known open entry order for this account or ProjectX refuses the modify.
        private bool ProjectXModifyEntryOrder(int accountId, string contractId, double entryPrice, int quantity)
        {
            string key = GetProjectXOrderKey(accountId, contractId);
            long orderId;
            if (!projectXLastOrderIds.TryGetValue(key, out orderId) || orderId <= 0)
                return false;

            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"orderId\":{1},\"size\":{2},\"limitPrice\":{3},\"stopPrice\":null,\"trailPrice\":null}}",
                accountId, orderId, Math.Max(1, quantity), FormatProjectXPriceRaw(entryPrice));
            string response = ProjectXPost("/api/Order/modify", json, true);
            bool success;
            if (string.IsNullOrWhiteSpace(response))
                return false;
            if (TryGetJsonBool(response, "success", out success) && !success)
            {
                ProjectXLog(string.Format(
                    "ProjectX entry modify refused | account={0} order={1} price={2} - re-placing",
                    accountId, orderId, FormatProjectXPriceRaw(entryPrice)));
                projectXLastOrderIds.Remove(key);
                return false;
            }
            return true;
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
            Print(string.Format("{0} | EMA921 | {1}",
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

        // ================================================================================
        // A. Version
        // ================================================================================

        // Version stamp (EMAL convention). GroupName sorts first; purely informational, never
        // read by strategy logic.
        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Version", Description = "Which EMA921 cut is installed. First entry is the current version number (selected by default); second entry is the IST date it was last modified. Never changes behavior.", GroupName = "A. Version", Order = 0)]
        public EMA921Version Version { get; set; }

        // ================================================================================
        // B. EMA921 rules (Steve, 2026-09-11). SUPERSEDED 2026-09-14 (EMA921-1002): all ten
        // fields below are now driven entirely by each session's own Setting popup (group C) -
        // not independently user-editable. Hidden from the property grid (Browsable(false)),
        // same convention EMAL uses for its own preset-driven fields. Kept as real properties
        // (not renamed) purely so every downstream formula that already reads them by name
        // keeps working unchanged - see ResolveActiveSessionValues().
        // ================================================================================

        [Range(1, 500), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry EMA Period", Description = "Driven by the active session's Setting preset; not user-editable.", GroupName = "B. EMA921 Rules", Order = 0)]
        public int EntryEmaPeriod { get; set; }

        [Range(1, 500), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "SL EMA Period", Description = "Driven by the active session's Setting preset; not user-editable.", GroupName = "B. EMA921 Rules", Order = 1)]
        public int StopEmaPeriod { get; set; }

        [Range(-100.0, 100.0), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Padding (points)", Description = "Driven by the active session's Setting preset; not user-editable.", GroupName = "B. EMA921 Rules", Order = 2)]
        public double EntryPaddingPoints { get; set; }

        [Range(0.0, 100.0), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "SL Padding (points)", Description = "Driven by the active session's Setting preset; not user-editable.", GroupName = "B. EMA921 Rules", Order = 3)]
        public double StopPaddingPoints { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Min Slope", Description = "Driven by the active session's Setting preset; not user-editable.", GroupName = "B. EMA921 Rules", Order = 4)]
        public double MinimumSlopePoints { get; set; }

        [Range(0.1, 50.0), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Reward Multiple (RR)", Description = "Driven by the active session's Setting preset; not user-editable.", GroupName = "B. EMA921 Rules", Order = 5)]
        public double RewardMultiple { get; set; }

        [Range(1, 100), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Sequence Candles", Description = "Driven by the active session's Setting preset; not user-editable.", GroupName = "B. EMA921 Rules", Order = 6)]
        public int SequenceCandles { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Min Body (points)", Description = "Driven by the active session's Setting preset; not user-editable.", GroupName = "B. EMA921 Rules", Order = 7)]
        public double MinimumBodyPoints { get; set; }

        [Range(0.25, 1000.0), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Min SL (points)", Description = "Driven by the active session's Setting preset; not user-editable.", GroupName = "B. EMA921 Rules", Order = 8)]
        public double MinimumStopPoints { get; set; }

        [Range(0.25, 1000.0), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Max SL (points)", Description = "Driven by the active session's Setting preset; not user-editable.", GroupName = "B. EMA921 Rules", Order = 9)]
        public double MaximumStopPoints { get; set; }

        [Range(0.25, 1000.0), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Min TP Distance (points)", Description = "Driven by the active session's Setting preset; not user-editable. Every current preset uses 4.0 (the cascade tuning campaign's fixed floor, not independently swept).", GroupName = "B. EMA921 Rules", Order = 10)]
        public double MinimumTargetPoints { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Contracts", Description = "Number of contracts per entry.", GroupName = "B. EMA921 Rules", Order = 11)]
        public int Contracts { get; set; }

        // ================================================================================
        // C. Sessions (EMA921-1002; option naming corrected EMA921-1005). Five popups, EMAL's
        // exact style: "Disabled" first and selected by default, one preset per session found by
        // the full-cascade tuning campaign (CASCADE_CHECKPOINT.md) on the ENTIRE research store
        // (no derive/holdout split, no significance testing) - selected purely on that session's
        // own Net/MaxIDD. EMA921-1005: the preset's dropdown option name itself now spells out
        // every tuned value (e.g. "S1_EMA9_Pad0.5_Slope2.25_..."), matching EMAL's convention,
        // instead of the generic "CascadeTuned" label 1002-1004 shipped.
        // The original 09:36-09:55 / 09:55-10:30 split is MERGED into one 09:36-10:30 popup: the
        // cascade found both individually too thin to tune reliably (P1 alone never converged in
        // 3 passes) and recommended merging - see CASCADE_CHECKPOINT.md "Follow-up 2 RESULTS".
        // None of this has been sent to Andreas, compiled, or Playback-tested before this cut -
        // engine-derived research only, same standing caveat as every other tuning result in this
        // project. Leave every session on Disabled until Andreas confirms this compiles clean and
        // you have run it in Playback yourself.
        // ================================================================================

        [NinjaScriptProperty]
        [Display(Name = "Asia 18:00-3:00 Setting", Description = "EMA9_Pad0.5_Slope2.25_Body1.0_RR2.0_Seq2_StopEMA14_StopPad0.0_MinSL3_MaxSL30 - WR39.4% Net$51,270 AvgDaily$427.25 MaxIDD$1,955 Net/IDD26.23 (best of all five sessions; full-cascade result, did not fully converge in 3 passes but n grew across passes, not an overfitting signature - see CASCADE_CHECKPOINT.md)", GroupName = "C. Sessions", Order = 0)]
        public EMA921AsiaSetting AsiaSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Europe 3:00-6:30 Setting", Description = "EMA6_Pad2.0_Slope1.5_Body0.25_RR2.0_Seq4_StopEMA17_StopPad0.75_MinSL5_MaxSL20 - WR35.7% Net$27,294 AvgDaily$227.45 MaxIDD$2,364 Net/IDD11.55 (weakest of the five sessions; converged cleanly)", GroupName = "C. Sessions", Order = 1)]
        public EMA921EuropeSetting EuropeSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US Pre-Market 8:32-9:28 Setting", Description = "EMA5_Pad0.5_Slope1.5_Body0.5_RR1.0_Seq4_StopEMA21_StopPad0.25_MinSL7_MaxSL30 - WR58.3% Net$23,948 AvgDaily$199.56 MaxIDD$1,069 Net/IDD22.40 (converged cleanly)\n\nCHANGED 2026-09-12: window itself now starts 08:32 (was 08:00), right after the existing 8:28-8:32 news-release block instead of re-trading the pre-block stretch.\n\nBlocked times 8:28-8:32 (unconditional news-release block, applies regardless of session; now entirely before this window opens). Any open position force-closed at 8:29 and again at 9:29 (both unconditional, apply regardless of session).", GroupName = "C. Sessions", Order = 2)]
        public EMA921PreMarketSetting PreMarketSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 9:36-10:30 Setting", Description = "EMA4_Pad3.0_Slope3.0_Body2.0_RR2.0_Seq4_StopEMA21_StopPad0.0_MinSL5_MaxSL30 - WR48.4% Net$32,838 AvgDaily$273.65 MaxIDD$2,240 Net/IDD14.66 (converged cleanly; MERGED window - see the C. Sessions header comment above. Replaces the old separate 09:36-09:55 and 09:55-10:30 popups)\n\n09:28-09:36 is never traded (same as EMAL).", GroupName = "C. Sessions", Order = 3)]
        public EMA921Us0936Setting Us0936Setting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US Midday 10:30-17:00 Setting", Description = "EMA7_Pad0.25_Slope3.5_Body2.0_RR2.0_Seq2_StopEMA18_StopPad2.0_MinSL5_MaxSL20 - WR36.6% Net$62,011 AvgDaily$516.76 MaxIDD$3,771 Net/IDD16.44 (converged cleanly; highest raw MaxIDD of the five sessions - Steve, if this is too high for 1 NQ, an earlier 12:30 session close was explored: same config truncated gives Net/IDD18.65 but -26% total profit, see CASCADE_CHECKPOINT.md \"Follow-up 1 RESULTS\" - not implemented as a separate preset here)\n\nBlocked from EOD Force Close to 17:00.", GroupName = "C. Sessions", Order = 4)]
        public EMA921USMiddaySetting USMiddaySetting { get; set; }

        // EMAL-1051: see IsPreCloseMinute. Default 16:55 ET; lower it for prop firms with a
        // stricter requirement (e.g. Topstep enforces flat by 16:00 ET).
        [NinjaScriptProperty]
        [Display(Name = "EOD Force Close", Description = "All entries are blocked and any open position is force-flattened at market from this time until the 17:00 ET CME daily maintenance halt. Default 16:55 (5 minutes ahead of the halt). Lower it for prop firms with a stricter flat-by requirement, e.g. Topstep enforces 16:00 ET.", GroupName = "C. Sessions", Order = 6)]
        public TimeSpan EODForceCloseTime { get; set; }

        // EMAL-1046: see ReconcileMultiContractProtection's inertness guard.
        [NinjaScriptProperty]
        [Display(Name = "Multi-Contract Protection Fix", Description = "Off (default): no effect. Auto: reconciles the stop/target order quantity to Position.Quantity whenever they differ, but ONLY once position quantity exceeds 1. On: same reconciliation at any quantity (testing). Fixes a partial-fill resize race where Tradovate can reject a resize submitted before the protective order reaches Working state.", GroupName = "C. Sessions", Order = 7)]
        public EMA921MultiContractProtectionFix MultiContractProtectionFix { get; set; }

        // ================================================================================
        // D. Risk
        // ================================================================================

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Account Balance", Description = "When account net liquidation, including unrealized P&L, reaches this value, pending entries are cancelled, open positions are flattened, and new entries remain blocked. 0 disables.", GroupName = "D. Risk", Order = 0)]
        public double MaxAccountBalance { get; set; }

        // EMAL: eval-account compliance facility. Resets at 18:00 ET (CME trading day).
        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Daily Profit", Description = "Maximum daily account profit in currency, measured from the first tick's net liquidation each trading day (resets 18:00 ET). Reaching it cancels pending entries, flattens open positions, and blocks new entries for the rest of that trading day. 0 disables.", GroupName = "D. Risk", Order = 1)]
        public double MaxDailyProfit { get; set; }

        // EMAL-1070: Tradovate/Apex provider ceiling is 5000 actions per hour, enforced BY IP;
        // reaching it can leave open positions unmodifiable for up to an hour. The local guard
        // keeps well clear of that. OPERATIONAL RULE: divide by the number of Tradovate
        // connections on this IP. NOTE for EMA921: the counter is per STRATEGY CLASS and per
        // connection, so EMA921 and EMAL on the same connection count separately while sharing
        // one provider budget - size both settings together. EMA921 also spends more actions
        // per trade than EMAL (entry and bracket are amended every bar).
        [Range(NewTradeActionReserve, 5000), NinjaScriptProperty]
        [Display(Name = "Order Actions / Hour", Description = "Conservative local EMA921 action ceiling per NT8 connection (shared across every EMA921 instance on it - EMAL keeps its own separate count). Default 4000 leaves headroom below Tradovate's 5000-per-hour provider limit. Exceeding the PROVIDER limit blocks all entries for a 60-minute cool-down AND can leave you unable to modify or exit open positions, which Tradovate does NOT auto-flatten - so keep a wide margin, and lower this when EMAL or other bots share the connection.", GroupName = "D. Risk", Order = 2)]
        public int OrderActionLimitPerHour { get; set; }

        // EMAL: default ON, hidden. Live incident 2026-08-14 (queue-position asymmetry on a
        // passive target). 1-contract only - see EvaluateTargetTouchWatchdog's scope guard.
        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Target Touch Watchdog", Description = "When ON, if price trades at or beyond the working target's limit price but the target hasn't filled within Target Touch Grace (ms), cancels the target and exits at market (signal EMA921TouchExit) once the cancel confirms unfilled.", GroupName = "D. Risk", Order = 5)]
        public bool EnableTargetTouchWatchdog { get; set; }

        [Range(100, 5000), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Target Touch Grace (ms)", Description = "How long the working target may sit unfilled after price first trades at or beyond its limit price before the watchdog cancels it and exits at market. Default 400.", GroupName = "D. Risk", Order = 6)]
        public int TargetTouchGraceMs { get; set; }

        // EMAL-1045: hidden, fixed at QuoteOrLast.
        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Touch Detection Mode", Description = "QuoteOrLast (default): the target-touch watchdog and gap latch trigger on either a Last trade AT the level or the relevant quote (Bid for Long, Ask for Short) reaching it. LastOnly: Last-trade detection only.", GroupName = "D. Risk", Order = 7)]
        public EMA921TouchDetectionMode TouchDetectionMode { get; set; }

        // EMAL-1067/1068: recurring naked-position audit.
        [NinjaScriptProperty]
        [Display(Name = "Naked Position Audit", Description = "Recurring safety net (from EMAL-1068): if an OPEN position is missing EITHER its working stop OR its working target continuously for the grace period below, flatten it. Catches protection that was accepted and later vanished (broker cancel, connection blip, order pulled), which the fill-time MissingStop/MissingTarget checks cannot see. Leave ON.", GroupName = "D. Risk", Order = 10)]
        public bool EnableNakedPositionAudit { get; set; }

        [Range(3, 300), NinjaScriptProperty]
        [Display(Name = "Naked Position Grace (seconds)", Description = "How long protection must be CONTINUOUSLY incomplete before the audit flattens. The clock starts when the fault is first observed and resets the moment both legs are seen working again. Must exceed a normal broker acknowledgement. Realtime only. Default 10.", GroupName = "D. Risk", Order = 11)]
        public int NakedPositionGraceSeconds { get; set; }

        // ================================================================================
        // E. ProjectX API (EMAL mirroring, unchanged apart from EMA921's moving entry)
        // ================================================================================

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Base URL", GroupName = "E. ProjectX API", Order = 3)]
        public string ProjectXApiBaseUrl { get; set; }

        [Browsable(false)]
        public bool ProjectXTradeAllAccounts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Username", Description = "ProjectX login username for direct routing.", GroupName = "E. ProjectX API", Order = 5)]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "API", Description = "ProjectX API key used with the username.", GroupName = "E. ProjectX API", Order = 6)]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Accounts", Description = "Comma-separated ProjectX account IDs or exact account names.", GroupName = "E. ProjectX API", Order = 7)]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Contract ID", Description = "Hidden optional contract override for support/debug use.", GroupName = "E. ProjectX API", Order = 8)]
        public string ProjectXContractId { get; set; }

        // ================================================================================
        // F. Logging
        // ================================================================================

        [NinjaScriptProperty]
        [Display(Name = "Log", Description = "Write one CSV row per completed trade: entry kind (Setup / ReEntry), sequence run, slope, both EMAs, planned limit/stop/target, the previous three bars OHLCV, fill delay, bars the limit was working, the bracket at fill and at exit, and the outcome.", GroupName = "F. Logging", Order = 0)]
        public bool EnableFeatureLog { get; set; }

        [Browsable(false)]
        [Display(Name = "Log File Path", Description = "Full path to the CSV. Leave blank to auto-name EMA921_v{version}_log_{yyyy-MM-dd hh-mm tt}.csv in Documents, stamped at file-creation time. Appends if the file already exists. Ignored when Log is off.", GroupName = "F. Logging", Order = 1)]
        public string FeatureLogPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Research Log", Description = "Research-only, leave OFF for live trading. Records per fill the first-touch time to a 0.25pt grid (+/-30pt, 1800s horizon), tracked past the exit, so alternative exits can be reconstructed offline.", GroupName = "F. Logging", Order = 3)]
        public bool EnablePathLog { get; set; }

        [Browsable(false)]
        [Display(Name = "Path Log File", Description = "Full path to the research log CSV. Blank auto-names EMA921_v{version}_research_log_{yyyy-MM-dd hh-mm tt}.csv in Documents, stamped at file-creation time.", GroupName = "F. Logging", Order = 4)]
        public string PathLogPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Execution Diagnostics", Description = "Per-entry timing for signal-to-submit, first order state and first fill callback, plus the full entry-order transition trace (including every per-bar limit move) and gap-breach detail. Leave OFF for live accounts.", GroupName = "F. Logging", Order = 5)]
        public bool EnableExecutionDiagnostics { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tick Logging", Description = "Diagnostic only, OFF by default - enable only when hunting a cross-box divergence. Realtime-only. Records the exact Last/Bid/Ask tick stream this instance computes on, so two boxes' files can be diffed after a divergent trade. Never affects trading.", GroupName = "F. Logging", Order = 6)]
        public bool EnableTickLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Tick Log Tag", Description = "Written into the tick log filename so files from different boxes/instances don't collide, e.g. \"box227\". Leave blank on a single-box setup.", GroupName = "F. Logging", Order = 7)]
        public string TickLogTag { get; set; }

        [Browsable(false)]
        [Display(Name = "Tick Log Folder", Description = "Folder for tick log CSVs, one file per instrument per day per tag. Created if missing. Leave blank for NinjaTrader's user data folder \\ticklogs.", GroupName = "F. Logging", Order = 8)]
        public string TickLogFolder { get; set; }
    }

    // Version stamp (EMAL convention). Purely informational, no effect on behavior. Bump the
    // first member's number on every new cut, and rename the second member's date to today
    // (IST) on every edit, even within the same cut.
    public enum EMA921Version
    {
        version_1005,
        modified_2026_09_15
    }

    // EMAL-1045: LastOnly = Last-trade-only detection; QuoteOrLast (default) also triggers on
    // the relevant quote reaching the level.
    public enum EMA921TouchDetectionMode
    {
        LastOnly,
        QuoteOrLast
    }

    // EMAL-1046: Off = no effect; Auto only acts once Position.Quantity > 1; On = any quantity.
    public enum EMA921MultiContractProtectionFix
    {
        Off,
        On,
        Auto
    }

    // EMA921-1002: per-session presets, EMAL style - Disabled first (and default), one
    // preset per session, named after its own tuned values (EMA921-1005, was the generic
    // "CascadeTuned" label in 1002-1004). See the C. Sessions property group's Description
    // tooltips (or CASCADE_CHECKPOINT.md) for the full parameter set and stats behind each.
    public enum EMA921AsiaSetting
    {
        Disabled,
        S1_EMA9_Pad0_5_Slope2_25_Body1_0_RR2_0_Seq2_StopEMA14_StopPad0_0_MinSL3_MaxSL30
    }

    public enum EMA921EuropeSetting
    {
        Disabled,
        S1_EMA6_Pad2_0_Slope1_5_Body0_25_RR2_0_Seq4_StopEMA17_StopPad0_75_MinSL5_MaxSL20
    }

    public enum EMA921PreMarketSetting
    {
        Disabled,
        S1_EMA5_Pad0_5_Slope1_5_Body0_5_RR1_0_Seq4_StopEMA21_StopPad0_25_MinSL7_MaxSL30
    }

    // Merged 09:36-10:30 window (EMA921-1002) - replaces the old separate Us0936/Us0955 enums.
    public enum EMA921Us0936Setting
    {
        Disabled,
        S1_EMA4_Pad3_0_Slope3_0_Body2_0_RR2_0_Seq4_StopEMA21_StopPad0_0_MinSL5_MaxSL30
    }

    public enum EMA921USMiddaySetting
    {
        Disabled,
        S1_EMA7_Pad0_25_Slope3_5_Body2_0_RR2_0_Seq2_StopEMA18_StopPad2_0_MinSL5_MaxSL20
    }
}
