#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
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
        private const string NewsExitSignal = StrategySignalPrefix + "NewsFlat";

        public enum WebhookProvider
        {
            TradersPost,
            ProjectX
        }

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
        private string webhookUrl = string.Empty;
        private string webhookTickerOverride = string.Empty;
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
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(255, 0x00, 0xFF, 0x00);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);

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
        private double queuedBracketReference;
        private double queuedTakeProfitPoints;
        private double queuedStopLossPoints;
        private double queuedSignalPrice;
        private int queuedEntryBar = -1;

        // Live entry-order context, used by the cancellation rules.
        private int activeEntryDirection;
        private double activeEntryReferencePrice;
        private DateTime activeEntrySubmitTime = DateTime.MinValue;
        private bool entryCancelPending;

        // Execution-driven protection. Stops are validated against the live market before
        // submission so a replay gap cannot place a buy stop below market or a sell stop
        // above market. The target is submitted only after the stop is accepted, preventing
        // reuse of an OCO identifier belonging to a rejected order pair.
        private string protectedEntrySignal = string.Empty;
        private double activeTakeProfitPoints;
        private double activeStopLossPoints;
        private double activeBracketReference;
        // Per-window bracket presets, resolved from the Setting popups in DataLoaded.
        private double us0928Tp, us0928Sl, us0955Tp, us0955Sl;
        private double entryFillValue;
        private int entryFilledQuantity;
        private double desiredProtectionTargetPrice;
        private int desiredProtectionQuantity;
        private bool terminalExitPending;

        // Account-level profit guard. Once net liquidation reaches the configured
        // ceiling, the latch remains set for the lifetime of this strategy instance.
        private bool maxAccountBalanceLimitReached;

        // Daily account-currency profit guard. The baseline is the first available net
        // liquidation value on each primary-bar calendar date, so unrealized P&L counts.
        private bool maxDailyProfitLimitReached;
        private double maxDailyProfitStartBalance = double.NaN;
        private DateTime maxDailyProfitDate = DateTime.MinValue;

        // Daily profit cap, measured in POINTS of realised P&L. Tracked independently of the
        // feature log so it works whether logging is on or off. The "day" is the CME trading
        // day, 18:00 ET to 17:00 ET, matching the session model used everywhere else.
        private DateTime currentTradingDay = DateTime.MinValue;
        private double dailyRealizedPoints;
        private bool dailyProfitLimitReached;
        private bool dailyLossLimitReached;
        private double openEntryPrice;
        private int openEntryDirection;
        private double openExitValue;
        private int openExitQuantity;

        // Set by ValidateChart when the strategy is on the wrong series or instrument.
        private bool configurationBlocked;
        private string configurationBlockReason = string.Empty;

        // Tick clock. Time[0] returns the in-progress bar's close stamp, so it cannot be
        // used for elapsed-seconds math. OnMarketData supplies the real tick timestamp.
        private DateTime lastTickTime = DateTime.MinValue;
        private double lastTickPrice;
        private bool sawMarketData;

        // Fill-rate accounting. Filled trades are the only thing the performance report
        // shows, so signals that never became trades have to be counted here.
        private int signalCount;
        private int filledCount;
        private int cancelTimeoutCount;
        private int cancelMovedCount;
        private int cancelBarEndCount;
        private int blockedBarCount;
        private int newsBlockedBarCount;
        private int bucketBlockedBarCount;
        private int parityBlockedBarCount;
        private int rolloverBlockedBarCount;
        private int minuteFilterBlockedBarCount;
        private DateTime rolloverStart = DateTime.MinValue;
        private bool rolloverStartValid;

        // Time stop ("horizontal filter"). Fill time is tracked outside the feature-log gate
        // so the rule works with logging off.
        private DateTime openEntryTime = DateTime.MinValue;
        private int timeStopCount;
        private int newsFlattenCount;

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

        private sealed class PathRecorder
        {
            public DateTime FillTime;      // platform time at fill
            public double FillPrice;
            public int Direction;          // +1 long, -1 short
            public string Session;
            public double[] FavTouch;      // elapsed sec first-touch per level, NaN = never
            public double[] AdvTouch;
        }

        // Session boundaries in minutes-of-day, New York time. Asia wraps midnight.
        // 17:00-18:30 is the maintenance/session-gate halt and belongs to no session.
        // NY-anchored boundaries. Globex reopen and the US cash session never drift, because
        // CME (Chicago) and New York share the same DST dates.
        private const int AsiaStartMinute = 18 * 60 + 30;   // 18:30 ET
        // US 09:28-09:50 window (Steve, 2026-08-02: start moved to 09:28, the real researched
        // start - see below). 09:50-09:55 is a hard no-trade block.
        private const int Us0928StartMinute = 9 * 60 + 28; // 09:28 ET, US 09:28-09:50 opens
        private const int Us0928EndMinute = 9 * 60 + 50;   // 09:50 ET
        private const int BlockStartMinute = 9 * 60 + 50;  // 09:50-09:55 ET, hard no-trade block
        private const int BlockEndMinute = 9 * 60 + 55;    // 09:55 ET
        private const int Us0955StartMinute = 9 * 60 + 55; // 09:55 ET, US 09:55-10:30 opens
        private const int Us0955EndMinute = 10 * 60 + 30;  // 10:30 ET
        private const int UsStartMinute = 10 * 60 + 30; // 10:30 ET, US proper begins
        private const int UsEndMinute = 17 * 60;        // 17:00 ET, cash close

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
                Name = GetVersionedStrategyName("EMAL");
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                BarsRequiredToTrade = 1;

                TradeParity = EMALTradeParity.Both;   // trade every candle by default

                EmaPeriod = 9;
                MinimumEmaSlopePoints = 0.75;   // global fallback; unused while per-session is on
                TakeProfitPoints = 4.0;
                StopLossPoints = 18.0;
                Contracts = 1;
                EntryOrderType = EMALEntryOrderType.Limit;
                LimitPriceReference = EMALLimitPriceReference.BidAsk;

                // Entry offset, OFF by default (0 = no offset, current behaviour).
                // Start in Global mode: one number, measurable. PerSession spends three
                // parameters on an effect that has not yet been measured even once.
                BracketAnchor = EMALBracketAnchor.Fill;
                LimitOffsetMode = EMALLimitOffsetMode.Global;
                LimitOffsetPoints = 0.0;
                AsiaLimitOffset = 0.0;
                UsLimitOffset = 0.0;
                Us0928LimitOffset = 0.0;
                Us0955LimitOffset = 0.0;
                MaxAccountBalance = 0.0;
                MaxDailyProfit = 0.0;
                EnableOrderRateGuard = true;
                OrderActionLimitPerHour = 1100;

                WebhookUrl = string.Empty;
                WebhookTickerOverride = string.Empty;
                WebhookProviderType = WebhookProvider.TradersPost;
                ProjectXApiBaseUrl = "https://api.topstepx.com";
                ProjectXTradeAllAccounts = false;
                ProjectXUsername = string.Empty;
                ProjectXApiKey = string.Empty;
                ProjectXAccountId = string.Empty;
                ProjectXContractId = string.Empty;
                // Contract-rollover block (Steve, 2026-07-31): off until a roll date is set;
                // when set, blocks the first 3 sessions of the new contract.
                RolloverBlockStart = string.Empty;
                RolloverBlockSessions = 3;

                // Daily caps in POINTS of realised P&L. 0 = disabled.
                // BOTH TESTED AND REJECTED on 78 sessions - days that go down recover
                // (down 80 pts -> rest of day averages +267) and days that go up keep going
                // (up 300 pts -> rest of day averages +134). Leave at 0.
                MaxDailyProfitPoints = 0.0;
                MaxDailyLossPoints = 0.0;

                // Time stop / horizontal filter. 0 = disabled.
                TimeStopSeconds = 0.0;
                TimeStopOnlyWhenLosing = true;
                TimeStopLossPoints = 0.0;


                // Limit-entry cancellation, OFF by default (0 = disabled). With both off the
                // only rule is the original one: cancel at the open of the next bar.
                LimitOrderTimeoutSeconds = 0.0;
                CancelIfMovedPoints = 0.0;

                // Per-session settings. Defaults reproduce current behaviour exactly: all three
                // sessions on, all thresholds equal to the global MinimumEmaSlopePoints.
                // TP, SL and entry type stay GLOBAL by design - three independent copies of an
                // interacting triple is where overfitting lives.
                UsePerSessionSettings = true;
                // Default to the two US morning windows only (Steve, 2026-07-31); Asia and the
                // US 10:30-17:00 session are OFF by default and enabled per user choice.
                AsiaEnabled = false;
                UsEnabled = false;
                Us0928Enabled = true;
                Us0955Enabled = true;
                // Per-window bracket presets (Steve, 2026-07-30). Window 1 defaults to TP5/SL18
                // (best net/maxDD of its four options; no TP4 option there). Window 2 defaults to
                // TP4/SL18 = current behaviour. ResolveWindowPresets() applies them in DataLoaded.
                Us0928Setting = EMALUs0928Setting.TP5_SL18_Slope2_75;
                Us0955Setting = EMALUs0955Setting.TP4_SL18_Slope2_75;

                // Free-tune escape hatch for the two NY windows (Steve, 2026-08-03). OFF by
                // default - live behavior is byte-for-byte unchanged from EMAL-23. When on,
                // ResolveWindowPresets() stops resolving Us0928Setting/Us0955Setting into
                // TP/SL/slope and reads these four fields directly instead, and it stops
                // overwriting Us0928MinimumSlope/Us0955MinimumSlope from the preset - closing
                // the "slope field is inert while a preset is selected" trap. Defaults below
                // reproduce the current TP5_SL18_Slope2_75 / TP4_SL18_Slope2_75 presets exactly,
                // so flipping TuneUsWindowsFree on with no other changes is a no-op.
                TuneUsWindowsFree = false;
                Us0928TakeProfitPoints = 5.0;
                Us0928StopLossPoints = 18.0;
                Us0955TakeProfitPoints = 4.0;
                Us0955StopLossPoints = 18.0;

                AsiaMinimumSlope = 3.0;
                UsMinimumSlope = 2.75;
                Us0928MinimumSlope = 2.75;   // overwritten by ResolveWindowPresets from the Setting popup, unless TuneUsWindowsFree
                Us0955MinimumSlope = 2.75;

                // Blackout around the 08:30 US data release. HHmm, inclusive both ends.
                BlockNewsWindow = true;
                FlattenAtNewsBlock = true;
                NewsBlockStartTime = 828;
                NewsBlockEndTime = 832;

                UseBucketFilter = true;

                // Minute-of-5 filter (Steve, 2026-08-01). Master switch defaults ON, with 1a/1e
                // enabled and 1b/1c/1d off - Steve's typical usage. NOTE: this is a real
                // behavior change from pre-filter EMAL versions for any FRESH instance (a new
                // chart/strategy add), since it now trades only 2 of every 5 minutes out of the
                // box instead of all 5 - not a no-op default like every other filter in this
                // file. One shared setting for whichever sessions/windows are enabled -
                // deliberately not per-window (see EMAL-18-changelog.txt).
                EnableMinuteFilter = true;
                TradeMinute1a = true;
                TradeMinute1b = false;
                TradeMinute1c = false;
                TradeMinute1d = false;
                TradeMinute1e = true;

                ShowInfoPanel = true;
                EnableFeatureLog = false;   // logging OFF by default (Steve, 2026-07-31)
                FeatureLogPath = @"C:\Users\Administrator\Documents\EMAL_features.csv";
                EnablePathLog = false;   // research-only; never on for live trading
                PathLogPath = string.Empty;
            }
            else if (State == State.DataLoaded)
            {
                ResolveWindowPresets();   // window Setting popups -> per-window TP/SL/slope
                rolloverStartValid = !string.IsNullOrWhiteSpace(RolloverBlockStart)
                    && DateTime.TryParseExact(RolloverBlockStart.Trim(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out rolloverStart);
                ValidateChart();
                maxAccountBalanceLimitReached = false;
                maxDailyProfitLimitReached = false;
                maxDailyProfitStartBalance = double.NaN;
                maxDailyProfitDate = DateTime.MinValue;

                SetupTimeZones();

                pathRecorders = new List<PathRecorder>();
                pathLogHeaderWritten = false;
                pathLogFailureCount = 0;

                ema = EMA(EmaPeriod);
                AddChartIndicator(ema);
            }
            else if (State == State.Realtime)
            {
                TransitionTrackedOrderReferencesToRealtime();
                RunProjectXStartupPreflight();
            }
            else if (State == State.Terminated)
            {
                CancelWorkingEntryOnTermination();
                FlattenProjectXOrphanOnTermination();
                ReleaseOrderRateReservation();
                FlushAllPathRecorders();
                PrintFillRateSummary();
                CloseFeatureLog();
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

        private DateTime GetBarOpenEastern()
        {
            return ConvertToEastern(GetBarOpenRaw());
        }

        // 0 = Asia (also the default fallback for the overnight band now that Europe is gone -
        // AsiaEnabled is off by default so this is inert unless Steve turns Asia on), 2 = US cash
        // (10:30-17:00), 3 = US 09:28-09:50, 5 = US 09:55-10:30, -1 = maintenance halt OR the
        // 09:50-09:55 no-trade block. Europe removed entirely (Steve, 2026-08-02: "I never want
        // to use this bot on London").
        private int GetSessionIndex(DateTime platformTime)
        {
            DateTime ny = ConvertToZone(platformTime, easternZone);
            int nyMinute = ny.Hour * 60 + ny.Minute;

            // 17:00-18:30 ET maintenance/session-gate halt.
            if (nyMinute >= UsEndMinute && nyMinute < AsiaStartMinute)
                return -1;

            // 09:50-09:55 ET hard no-trade block (returns "no session" so entries are gated).
            if (nyMinute >= BlockStartMinute && nyMinute < BlockEndMinute)
                return -1;

            // Special US windows, checked before US proper.
            if (nyMinute >= Us0928StartMinute && nyMinute < Us0928EndMinute)
                return 3;

            if (nyMinute >= Us0955StartMinute && nyMinute < Us0955EndMinute)
                return 5;

            if (nyMinute >= UsStartMinute && nyMinute < UsEndMinute)
                return 2;

            return 0;
        }

        private static string SessionName(int index)
        {
            switch (index)
            {
                case 0: return "Asia";
                case 2: return "US";
                case 3: return "US 09:28-09:50";
                case 5: return "US 09:55-10:30";
                default: return "Halt";
            }
        }

        private bool IsSessionEnabled(int index)
        {
            switch (index)
            {
                case 0: return AsiaEnabled;
                case 2: return UsEnabled;
                case 3: return Us0928Enabled;
                case 5: return Us0955Enabled;
                default: return false;
            }
        }

        // Per-session slope threshold. Falls back to the global value when per-session
        // settings are off, so the two cannot disagree silently.
        private double GetConfiguredSlope(DateTime platformTime)
        {
            if (!UsePerSessionSettings)
                return Math.Abs(MinimumEmaSlopePoints);

            switch (GetSessionIndex(platformTime))
            {
                case 0: return Math.Abs(AsiaMinimumSlope);
                case 2: return Math.Abs(UsMinimumSlope);
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

        // Per-window brackets (Steve, 2026-07-30). The two special US sessions each pick a
        // preset (TP/SL/slope) via a popup; those windows use their own TP/SL, every other
        // session uses the global TakeProfitPoints/StopLossPoints. Resolved once in DataLoaded.
        private double GetConfiguredTakeProfit()
        {
            switch (GetSessionIndex(GetBarOpenRaw()))
            {
                case 3: return us0928Tp;
                case 5: return us0955Tp;
                default: return TakeProfitPoints;
            }
        }

        private double GetConfiguredStopLoss()
        {
            switch (GetSessionIndex(GetBarOpenRaw()))
            {
                case 3: return us0928Sl;
                case 5: return us0955Sl;
                default: return StopLossPoints;
            }
        }

        // Resolves each window's Setting popup into its TP / SL / slope. The slope is written
        // back into the per-window Us*MinimumSlope so GetConfiguredSlope keeps working unchanged.
        //
        // TuneUsWindowsFree (Steve, 2026-08-03): when on, skip preset resolution entirely and
        // read TP/SL straight from Us0928TakeProfitPoints/Us0928StopLossPoints (and the 0955
        // pair) - and, critically, do NOT touch Us0928MinimumSlope/Us0955MinimumSlope here, so
        // whatever value the tuner set on those fields stands. In preset mode those two fields
        // are the ones ResolveWindowPresets overwrites every DataLoaded, which is why sweeping
        // them while a preset is selected is a no-op - closed for the free-tune path only.
        private void ResolveWindowPresets()
        {
            if (TuneUsWindowsFree)
            {
                us0928Tp = Us0928TakeProfitPoints;
                us0928Sl = Us0928StopLossPoints;
                us0955Tp = Us0955TakeProfitPoints;
                us0955Sl = Us0955StopLossPoints;
                return;
            }

            switch (Us0928Setting)
            {
                case EMALUs0928Setting.TP2_SL10_Slope2_75: us0928Tp = 2; us0928Sl = 10; Us0928MinimumSlope = 2.75; break;
                case EMALUs0928Setting.TP4_SL18_Slope2_75: us0928Tp = 4; us0928Sl = 18; Us0928MinimumSlope = 2.75; break;
                case EMALUs0928Setting.TP2_SL14_Slope3_0:  us0928Tp = 2; us0928Sl = 14; Us0928MinimumSlope = 3.0;  break;
                case EMALUs0928Setting.TP2_SL14_Slope2_75: us0928Tp = 2; us0928Sl = 14; Us0928MinimumSlope = 2.75; break;
                default: /* TP5_SL18_Slope2_75 */          us0928Tp = 5; us0928Sl = 18; Us0928MinimumSlope = 2.75; break;
            }
            switch (Us0955Setting)
            {
                case EMALUs0955Setting.TP3_SL16_Slope2_75: us0955Tp = 3; us0955Sl = 16; Us0955MinimumSlope = 2.75; break;
                case EMALUs0955Setting.TP3_SL18_Slope2_75: us0955Tp = 3; us0955Sl = 18; Us0955MinimumSlope = 2.75; break;
                case EMALUs0955Setting.TP2_SL18_Slope2_75: us0955Tp = 2; us0955Sl = 18; Us0955MinimumSlope = 2.75; break;
                case EMALUs0955Setting.TP4_SL20_Slope2_50: us0955Tp = 4; us0955Sl = 20; Us0955MinimumSlope = 2.50; break;
                default: /* TP4_SL18_Slope2_75 */          us0955Tp = 4; us0955Sl = 18; Us0955MinimumSlope = 2.75; break;
            }
        }

        private static string N(double v)
        {
            return v.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private string ResolveFeatureLogPath()
        {
            if (!string.IsNullOrEmpty(FeatureLogPath))
                return FeatureLogPath;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                string.Format("EMAL_features_{0:yyyyMMdd_HHmmss}.csv", DateTime.Now));
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

                // Stamped per row so a log file that has accumulated more than one run stays
                // trivially separable. Reconstructing that split from fill signatures is slow
                // and lossy on the unpaired rows.
                EntryOrderType.ToString(),
                EntryOrderType == EMALEntryOrderType.Limit
                    ? LimitPriceReference.ToString()
                    : "n/a",

                N(signalPrice),
                N(ema[1]),
                N(slope),
                N(slopePrev),
                N(slope - slopePrev),
                N(GetRequiredSlope(barOpenRaw)),
                N(EntryOrderType == EMALEntryOrderType.Limit ? GetLimitOffsetPoints() : 0.0),
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

            int cancelled = cancelTimeoutCount + cancelMovedCount + cancelBarEndCount;

            Print("================ EMAL fill rate ================");
            Print(string.Format("  entry type          : {0}", EntryOrderType));
            Print(string.Format("  per-session         : {0}", UsePerSessionSettings));
            Print(string.Format("      Asia     1830-03   : {0}  slope {1}", AsiaEnabled, AsiaMinimumSlope));
            Print(string.Format("      US 0928-0950       : {0}  slope {1}", Us0928Enabled, Us0928MinimumSlope));
            Print(string.Format("      (block 0950-0955, no trade)"));
            Print(string.Format("      US 0955-1030       : {0}  slope {1}", Us0955Enabled, Us0955MinimumSlope));
            Print(string.Format("      US       1030-17   : {0}  slope {1}", UsEnabled, UsMinimumSlope));
            Print(string.Format("  bars blocked        : {0}  (session gate)", blockedBarCount));
            Print(string.Format("  bucket filter       : {0}  (bars blocked: {1})",
                UseBucketFilter, bucketBlockedBarCount));
            Print(string.Format("  minute filter       : {0}  1a={1} 1b={2} 1c={3} 1d={4} 1e={5}  (bars blocked: {6})",
                EnableMinuteFilter, TradeMinute1a, TradeMinute1b, TradeMinute1c, TradeMinute1d, TradeMinute1e,
                minuteFilterBlockedBarCount));
            Print(string.Format("  trade parity        : {0}  (bars blocked: {1})",
                TradeParity, parityBlockedBarCount));
            Print(string.Format("  time stop           : {0}s  onlyWhenLosing={1} thresh={2}  (fired: {3})",
                TimeStopSeconds, TimeStopOnlyWhenLosing, TimeStopLossPoints, timeStopCount));
            Print(string.Format("  news blackout       : {0}  {1:0000}-{2:0000}  (bars blocked: {3})",
                BlockNewsWindow, NewsBlockStartTime, NewsBlockEndTime, newsBlockedBarCount));
            Print(string.Format("  news flatten        : {0}  (positions closed: {1})",
                FlattenAtNewsBlock, newsFlattenCount));
            Print(string.Format("  timeout / moved     : {0}s / {1} pts (0 = off)",
                LimitOrderTimeoutSeconds, CancelIfMovedPoints));
            Print(string.Format("  order rate guard    : {0} / {1} actions (entries blocked: {2})",
                EnableOrderRateGuard, OrderActionLimitPerHour, rateGuardBlockedEntryCount));
            Print(string.Format("  signals generated   : {0}", signalCount));
            Print(string.Format("  filled              : {0}  ({1:F1}%)",
                filledCount, 100.0 * filledCount / signalCount));
            Print(string.Format("  cancelled           : {0}  ({1:F1}%)",
                cancelled, 100.0 * cancelled / signalCount));
            Print(string.Format("      by timeout      : {0}", cancelTimeoutCount));
            Print(string.Format("      by price moved  : {0}", cancelMovedCount));
            Print(string.Format("      at bar end      : {0}", cancelBarEndCount));
            if (EntryOrderType == EMALEntryOrderType.Limit && !sawMarketData)
            {
                Print("  WARNING: no market data ticks were received, so the timeout and");
                Print("           moved-price rules never ran. Enable Tick Replay.");
            }
            Print("===============================================");
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last)
                return;

            sawMarketData = true;
            lastTickTime = e.Time;
            lastTickPrice = e.Price;

            TrackExcursion();
            UpdatePathRecorders();
            EvaluateProjectXOrphanRecovery();
            EvaluateTerminalExitRecovery();
            EvaluateTimeStop();
            EvaluateEntryCancellation();
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
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            return Path.Combine(dir, "EMAL_path_log.csv");
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

        private void EvaluateEntryCancellation()
        {
            if (EntryOrderType != EMALEntryOrderType.Limit
                || entryCancelPending
                || !IsOrderActive(entryOrder)
                || activeEntryDirection == 0)
            {
                return;
            }

            string reason = null;

            // 1. Time in force. The order has rested longer than we are willing to wait.
            if (LimitOrderTimeoutSeconds > 0.0
                && activeEntrySubmitTime != DateTime.MinValue
                && (lastTickTime - activeEntrySubmitTime).TotalSeconds >= LimitOrderTimeoutSeconds)
            {
                reason = "timeout";
                cancelTimeoutCount++;
            }

            // 2. Price ran in the signal direction without us. The move we wanted has already
            //    happened; filling on a pullback would be a different trade than the signal.
            else if (CancelIfMovedPoints > 0.0
                && (lastTickPrice - activeEntryReferencePrice) * activeEntryDirection >= CancelIfMovedPoints)
            {
                reason = "moved";
                cancelMovedCount++;
            }

            if (reason == null)
                return;

            // Latch before cancelling: CancelOrder is asynchronous and this method runs on
            // every tick, so without this we would re-issue the cancel until it confirms.
            entryCancelPending = true;
            RecordNtOrderAction("cancel-entry-" + reason);
            CancelOrder(entryOrder);

            Print(string.Format("{0} | cancel {1} | {2} | ref={3:F2} last={4:F2} held={5:F1}s",
                lastTickTime,
                activeEntryDirection > 0 ? "LONG" : "SHORT",
                reason,
                activeEntryReferencePrice,
                lastTickPrice,
                (lastTickTime - activeEntrySubmitTime).TotalSeconds));
        }

        // ---------------- chart info panel ----------------

        // Mirrors IsEntryWindowOpen's decision so the panel reports the same verdict the
        // strategy acts on, rather than a second implementation that could drift.
        private string GetTradeGateState()
        {
            if (configurationBlocked)
                return "disabled";

            DateTime raw = GetBarOpenRaw();

            if (IsRolloverBlocked(ConvertToEastern(raw)))
                return "rollover block";

            if (IsNewsBlackout(ConvertToEastern(raw)))
                return "news blackout";

            int s = GetSessionIndex(raw);

            if (UsePerSessionSettings)
            {
                if (s < 0 || !IsSessionEnabled(s))
                    return "session gate";
            }

            // Mirror IsEntryWindowOpen's bucket gate: an out-of-list 30-minute bucket
            // blocks entry on time alone. Without this the panel read "allow" during a
            // bucket-blocked window even though no trade could fire.
            if (!IsBucketAllowed(ConvertToEastern(raw)))
                return "time block";

            // Mirror IsEntryWindowOpen's minute-of-5 filter (Steve, 2026-08-01).
            if (!IsMinuteAllowed(ConvertToEastern(raw)))
                return "minute block (" + MinutePositionLabel(ConvertToEastern(raw)) + ")";

            // Mirror IsEntryWindowOpen's even/odd candle filter.
            if (!IsParityAllowed(ConvertToEastern(raw)))
                return TradeParity == EMALTradeParity.Even ? "odd bar (want even)" : "even bar (want odd)";

            if (maxDailyProfitLimitReached) return "daily profit cap";
            if (dailyProfitLimitReached) return "daily points cap";
            if (dailyLossLimitReached) return "daily loss cap";
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
        // null when everything is fine (the line is then omitted). Placed above "Contracts:"
        // so the panel always tells the user WHY it is or isn't trading (Steve, 2026-07-30).
        private string GetStatusLine()
        {
            if (configurationBlocked)
                return "ERROR: " + configurationBlockReason;

            if (CurrentBar < Math.Max(EmaPeriod, 20) + 2)
                return "Warmup in progress";

            // Limit entries need ticks; if none have ever arrived in real time the strategy
            // silently never fills. Surfaces the "enable Tick Replay" cause.
            if (State == State.Realtime && EntryOrderType == EMALEntryOrderType.Limit && !sawMarketData)
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

        private List<KeyValuePair<string, string>> BuildInfoLines()
        {
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
                lines.Add(new KeyValuePair<string, string>(status, string.Empty));

            lines.Add(new KeyValuePair<string, string>("Contracts:", Contracts.ToString(CultureInfo.InvariantCulture)));
            lines.Add(new KeyValuePair<string, string>("Contract:", instrument));
            lines.Add(new KeyValuePair<string, string>("Slope:", GetRequiredSlope(raw).ToString("0.##", CultureInfo.InvariantCulture)));
            lines.Add(new KeyValuePair<string, string>("EMA:", EmaPeriod.ToString(CultureInfo.InvariantCulture)));
            lines.Add(new KeyValuePair<string, string>("TP:", GetConfiguredTakeProfit().ToString("0.##", CultureInfo.InvariantCulture)));
            lines.Add(new KeyValuePair<string, string>("SL:", GetConfiguredStopLoss().ToString("0.##", CultureInfo.InvariantCulture)));
            lines.Add(new KeyValuePair<string, string>("Trade:", GetTradeGateState()));
            int projectedActions;
            int actionLimit;
            DateTime providerBlockedUntilUtc;
            if (TryGetOrderRateStatus(out projectedActions, out actionLimit, out providerBlockedUntilUtc))
                lines.Add(new KeyValuePair<string, string>("API:", string.Format("{0}/{1}", projectedActions, actionLimit)));
            else
                lines.Add(new KeyValuePair<string, string>("API:", "Off"));
            lines.Add(new KeyValuePair<string, string>("Session:", SessionName(session)));
            lines.Add(new KeyValuePair<string, string>(InfoFooter, string.Empty));
            return lines;
        }

        private void UpdateInfoText()
        {
            if (!ShowInfoPanel || ChartControl == null || ChartControl.Dispatcher == null)
                return;

            if (State != State.Realtime && State != State.Historical)
                return;

            var lines = BuildInfoLines();
            ChartControl.Dispatcher.InvokeAsync(() => RenderInfoBoxOverlay(lines));
        }

        private void RenderInfoBoxOverlay(List<KeyValuePair<string, string>> lines)
        {
            if (!EnsureInfoBoxOverlay() || infoBoxRowsPanel == null)
                return;

            infoBoxRowsPanel.Children.Clear();

            for (int i = 0; i < lines.Count; i++)
            {
                bool edge = i == 0 || i == lines.Count - 1;

                var text = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = edge ? 15 : 14,
                    FontWeight = edge ? FontWeights.SemiBold : FontWeights.Normal,
                    TextAlignment = edge ? TextAlignment.Center : TextAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);

                text.Inlines.Add(new Run(lines[i].Key)
                {
                    Foreground = edge ? InfoHeaderTextBrush : InfoLabelBrush
                });

                if (!string.IsNullOrEmpty(lines[i].Value))
                {
                    text.Inlines.Add(new Run(" ") { Foreground = InfoLabelBrush });
                    text.Inlines.Add(new Run(lines[i].Value) { Foreground = InfoValueBrush });
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

        private string GetVersionedStrategyName(string baseName)
        {
            return baseName + GetAddOnVersion().Replace(".", string.Empty);
        }

        // Allowed 30-minute entry buckets, indexed from 00:00 ET in 30-minute steps
        // (0 = 00:00, 1 = 00:30, ... 47 = 23:30). Hardcoded by design - this is a
        // research result, not a user setting.
        //
        // Derived from 78 sessions of NinjaTrader playback, Apr 26 - Jul 24 2026
        // (28,325 trades), keeping buckets whose trade-level t-statistic exceeds 0.5.
        // Keeps 33 of 46 populated buckets and 72% of trades.
        //
        // Blocked: 02:30 03:00 03:30 04:00 04:30 05:30 06:00 07:30 14:30 16:30 18:00
        //          22:00 23:00
        // Eight of the thirteen fall in 02:30-06:00, the thin-liquidity overnight
        // stretch that has been the weakest region in every cut of the data.
        //
        // Out-of-sample evidence (interleaved split): 85% of profit retained, max
        // intraday drawdown halved, net/maxIDD 12.9 -> 21.9. The sequential split
        // showed a larger profit cost, so this is a RISK-REDUCTION setting, not a
        // profit-maximising one. See EMAL_Analysis_Plan.md 0c.
        private static readonly bool[] AllowedBuckets = BuildAllowedBuckets(new[]
        {
            0, 1, 2, 3, 4, 10, 13, 14, 16, 17, 18, 19, 20, 21, 22, 23, 24,
            25, 26, 27, 28, 30, 31, 32, 37, 38, 39, 40, 41, 42, 43, 45, 47
        });

        private static bool[] BuildAllowedBuckets(int[] allowed)
        {
            var map = new bool[48];

            for (int i = 0; i < allowed.Length; i++)
            {
                if (allowed[i] >= 0 && allowed[i] < 48)
                    map[allowed[i]] = true;
            }

            return map;
        }

        private bool IsBucketAllowed(DateTime easternTime)
        {
            if (!UseBucketFilter)
                return true;

            int bucket = (easternTime.Hour * 60 + easternTime.Minute) / 30;

            return bucket >= 0 && bucket < AllowedBuckets.Length && AllowedBuckets[bucket];
        }

        // Minute-of-5 filter (Steve, 2026-08-01). Applies to the underlying 1-minute signal
        // regardless of which session/window is enabled - one shared setting, not per-window.
        // Position is the bar-open minute's place within its nominal 5-minute grouping: 0=1a
        // (1st minute), 1=1b, 2=1c, 3=1d, 4=1e.
        private string MinutePositionLabel(DateTime easternTime)
        {
            switch (easternTime.Minute % 5)
            {
                case 0: return "1a";
                case 1: return "1b";
                case 2: return "1c";
                case 3: return "1d";
                default: return "1e";
            }
        }

        private bool IsMinuteAllowed(DateTime easternTime)
        {
            if (!EnableMinuteFilter)
                return true;

            switch (easternTime.Minute % 5)
            {
                case 0: return TradeMinute1a;
                case 1: return TradeMinute1b;
                case 2: return TradeMinute1c;
                case 3: return TradeMinute1d;
                default: return TradeMinute1e;
            }
        }

        // Contract-rollover block (Steve, 2026-07-31). Blocks entries for the first
        // RolloverBlockSessions trading dates on/after Rollover Block Start. Trading dates are
        // calendar dates that are not Saturday (NQ trades Sun evening -> Fri). The user sets the
        // new contract's first session date each quarter (blank or Sessions<=0 = off).
        private bool IsRolloverBlocked(DateTime easternBarOpen)
        {
            if (RolloverBlockSessions <= 0 || !rolloverStartValid)
                return false;

            DateTime d = easternBarOpen.Date;
            if (d < rolloverStart)
                return false;
            if ((d - rolloverStart).Days > RolloverBlockSessions + 2)
                return false;   // well past the window; cheap early-out for the loop below

            int count = 0;
            for (DateTime x = rolloverStart; x <= d; x = x.AddDays(1))
                if (x.DayOfWeek != DayOfWeek.Saturday)
                    count++;

            return count <= RolloverBlockSessions;
        }

        private bool IsEntryWindowOpen()
        {
            DateTime barOpenRaw = GetBarOpenRaw();
            DateTime barOpen = ConvertToEastern(barOpenRaw);

            // Contract-rollover block: skip the first N sessions of a new contract.
            if (IsRolloverBlocked(barOpen))
            {
                rolloverBlockedBarCount++;
                return false;
            }

            // News blackout applies on every day, independent of the session gate.
            if (IsNewsBlackout(barOpen))
            {
                newsBlockedBarCount++;
                return false;
            }

            int session = GetSessionIndex(barOpenRaw);

            if (UsePerSessionSettings)
            {
                if (session < 0 || !IsSessionEnabled(session))
                    return false;
            }

            if (!IsBucketAllowed(barOpen))
            {
                bucketBlockedBarCount++;
                return false;
            }

            if (!IsMinuteAllowed(barOpen))
            {
                minuteFilterBlockedBarCount++;
                return false;
            }

            if (!IsParityAllowed(barOpen))
            {
                parityBlockedBarCount++;
                return false;
            }

            return true;
        }

        // Even/Odd candle filter. Candles are indexed from the top of the hour by minute-of-hour.
        // Even = index 0,2,4... Odd = index 1,3,5... Both disables the filter. Minute-of-hour is
        // timezone-invariant across whole-hour offsets, but eastern bar-open is passed for
        // consistency with the gates.
        private bool IsParityAllowed(DateTime barOpenEastern)
        {
            if (TradeParity == EMALTradeParity.Both)
                return true;

            int index = barOpenEastern.Minute;
            bool isEven = (index % 2) == 0;

            return TradeParity == EMALTradeParity.Even ? isEven : !isEven;
        }

        // Time stop / "horizontal filter".
        //
        // A trade that has not resolved quickly is disproportionately heading for the stop.
        // Measured on 78 sessions of playback, among trades still open at N seconds the
        // eventual win rate falls well below the 81.8% breakeven:
        //     10s -> 74.6%   15s -> 72.0%   30s -> 67.6%   60s -> 63.4%
        // Winners resolve in a median 10s; losers take 84s.
        //
        // Fires only when the trade is UNDERWATER by at least TimeStopLossPoints. Trades that
        // are grinding toward target are left alone: among winners still open at 60s, mean
        // adverse excursion was 9.32 pts, so a bare sign test would cut many eventual winners.
        //
        // Runs on every Last tick. Exits at market through the normal terminal-exit path so
        // the protective bracket is cancelled with it.
        private void EvaluateTimeStop()
        {
            if (TimeStopSeconds <= 0.0
                || terminalExitPending
                || IsTerminalExitRetryWaiting()
                || openEntryDirection == 0
                || openEntryPrice <= 0.0
                || openEntryTime == DateTime.MinValue
                || lastTickTime == DateTime.MinValue
                || Position.MarketPosition == MarketPosition.Flat)
            {
                return;
            }

            double heldSeconds = (lastTickTime - openEntryTime).TotalSeconds;

            if (heldSeconds < TimeStopSeconds)
                return;

            double unrealized = (lastTickPrice - openEntryPrice) * openEntryDirection;

            if (TimeStopOnlyWhenLosing && unrealized > -Math.Abs(TimeStopLossPoints))
                return;

            timeStopCount++;

            Print(string.Format(
                "{0} | TIME STOP | held {1:F1}s unrealized {2:F2} pts | {3}",
                lastTickTime, heldSeconds, unrealized,
                openEntryDirection > 0 ? "LONG" : "SHORT"));

            TrySubmitTerminalExit("TimeStop", protectedEntrySignal);
        }

        private void FlattenForNews()
        {
            if (Position.MarketPosition == MarketPosition.Flat
                || terminalExitPending
                || IsTerminalExitRetryWaiting())
                return;

            int quantity = Position.Quantity;
            terminalExitPending = true;

            RecordNtOrderAction("news-exit");

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(quantity, NewsExitSignal, LongEntrySignal);
            else
                ExitShort(quantity, NewsExitSignal, ShortEntrySignal);

            SendExplicitProjectXExit("news");

            newsFlattenCount++;

            Print(string.Format("{0} | NEWS FLAT | closing {1} {2} @ market",
                Time[0],
                quantity,
                Position.MarketPosition));
        }

        // Blackout around scheduled economic releases. Times are HHmm (0828 = 08:28) and the
        // range is INCLUSIVE at both ends, so 0828-0832 blocks five one-minute bars:
        // 08:28, 08:29, 08:30, 08:31 and 08:32. Does not support ranges crossing midnight.
        private bool IsNewsBlackout(DateTime barOpen)
        {
            if (!BlockNewsWindow)
                return false;

            int barTime = barOpen.Hour * 100 + barOpen.Minute;
            int start = Math.Min(NewsBlockStartTime, NewsBlockEndTime);
            int end = Math.Max(NewsBlockStartTime, NewsBlockEndTime);

            return barTime >= start && barTime <= end;
        }



        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            // Wrong chart period or instrument: stay loaded, submit nothing - but still draw
            // the panel so the top status line shows the user WHY it is disabled.
            if (configurationBlocked)
            {
                if (IsFirstTickOfBar)
                    UpdateInfoText();
                return;
            }

            // Evaluate on every tick so unrealized profit can flatten an open position
            // immediately instead of waiting for the next one-minute bar.
            if (IsAccountBalanceBlocked() || IsAccountDailyProfitBlocked())
            {
                // Keep drawing the info panel after either account-level latch is hit.
                if (IsFirstTickOfBar)
                    UpdateInfoText();
                return;
            }

            if (!IsFirstTickOfBar)
                return;

            ClearQueuedEntry();

            // Roll the daily accumulator before any gate reads it.
            RollTradingDayIfNeeded();

            UpdateInfoText();

            if (IsDailyProfitBlocked() || IsDailyLossBlocked())
                return;

            // Force-flat for the news blackout. This is the ONLY place the strategy closes a
            // position itself; every other exit is left to the stop and target. Runs before the
            // flat-check below so it can act on an open position.
            if (FlattenAtNewsBlock && IsNewsBlackout(GetBarOpenEastern()))
                FlattenForNews();

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                CancelEntryOrderIfActive();
                return;
            }

            // 20 covers the AvgVolume20 lookback used by the feature log.
            if (CurrentBar < Math.Max(EmaPeriod, 20) + 2)
            {
                CancelEntryOrderIfActive();
                return;
            }

            // Entry-time gate only. Open positions are untouched: their stop and target were
            // registered at entry and continue to manage the exit outside the window.
            if (!IsEntryWindowOpen())
            {
                blockedBarCount++;
                CancelEntryOrderIfActive();
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
                CancelEntryOrderIfActive();
                return;
            }

            TrySubmitQueuedEntry();
        }

        private void QueueEntry(int direction, double completedEmaSlope)
        {
            queuedDirection = direction;
            queuedEntryBar = CurrentBar;
            signalCount++;

            // All bracket scaling (TP Atr/Slope 3.6, SL Atr 3.7) was tested and REJECTED:
            // varying the bracket per-trade destroys edge. Fixed distances only. The snapshot
            // plumbing is retained so an open trade keeps the values it entered with.
            // Per-window brackets: the two special US sessions use their preset TP/SL; all
            // other sessions use the global values. Snapshotted here so the trade keeps them.
            queuedTakeProfitPoints = GetConfiguredTakeProfit();
            queuedStopLossPoints = GetConfiguredStopLoss();

            // Reference for the "price ran without us" rule: where price was when the
            // signal fired, not where the limit was placed.
            queuedSignalPrice = Close[0];

            if (EntryOrderType == EMALEntryOrderType.Limit)
                queuedLimitPrice = GetPassiveLimitPrice(direction);
        }

        private void TrySubmitQueuedEntry()
        {
            // OnOrderUpdate can re-enter this method after an asynchronous cancellation.
            // Recheck the account latch here so no queued entry can escape the main gate.
            if (configurationBlocked
                || IsAccountBalanceBlocked()
                || IsAccountDailyProfitBlocked()
                || IsDailyProfitBlocked()
                || IsDailyLossBlocked())
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
            double stopLoss = queuedStopLossPoints > 0.0 ? queuedStopLossPoints : StopLossPoints;
            double signalPrice = queuedSignalPrice;
            double bracketReference = queuedBracketReference;
            ClearQueuedEntry();

            // Context the cancellation rules need while the order is live.
            activeEntryDirection = direction;
            activeEntryReferencePrice = signalPrice;
            activeEntrySubmitTime = lastTickTime != DateTime.MinValue ? lastTickTime : Time[0];
            entryCancelPending = false;

            string entrySignal = direction > 0 ? LongEntrySignal : ShortEntrySignal;

            BeginProtectionTracking(entrySignal, takeProfit, stopLoss, bracketReference);

            CaptureEntryFeatures(direction, signalPrice, takeProfit, stopLoss);

            Print(string.Format(
                "{0} | {1} {2}{3} | target={4:F2} pts stop={5:F2} pts",
                Time[0],
                direction > 0 ? "LONG" : "SHORT",
                EntryOrderType,
                EntryOrderType == EMALEntryOrderType.Limit
                    ? string.Format("@{0}={1:F2}", LimitPriceReference, limitPrice)
                    : string.Empty,
                takeProfit,
                stopLoss));

            SendPlannedProjectXEntry(
                direction,
                EntryOrderType == EMALEntryOrderType.Limit ? limitPrice : GetProjectXMarketReferencePrice(direction),
                bracketReference,
                takeProfit,
                stopLoss);

            RecordNtOrderAction("entry");

            if (direction > 0)
            {
                if (EntryOrderType == EMALEntryOrderType.Market)
                    EnterLong(0, Contracts, LongEntrySignal);
                else
                    EnterLongLimit(0, true, Contracts, limitPrice, LongEntrySignal);
            }
            else
            {
                if (EntryOrderType == EMALEntryOrderType.Market)
                    EnterShort(0, Contracts, ShortEntrySignal);
                else
                    EnterShortLimit(0, true, Contracts, limitPrice, ShortEntrySignal);
            }
        }

        private double GetPassiveLimitPrice(int direction)
        {
            double price;

            switch (LimitPriceReference)
            {
                // This runs on the first tick of the bar, where Close[0] IS the bar's open.
                case EMALLimitPriceReference.Open:
                    price = Close[0];
                    break;

                // Previous completed bar's close.
                case EMALLimitPriceReference.Close:
                    price = Close[1];
                    break;

                // Passive: bid for longs, ask for shorts. Original behaviour.
                default:
                    price = direction > 0 ? GetCurrentBid() : GetCurrentAsk();
                    break;
            }

            if (price <= 0.0 || double.IsNaN(price))
                price = Close[0];

            // The UNOFFSET reference. When BracketAnchor is Reference the stop and target are
            // measured from here rather than from the fill, so an offset entry keeps the exact
            // barrier prices the un-offset trade would have had - and therefore the same outcome.
            queuedBracketReference = Instrument.MasterInstrument.RoundToTickSize(price);

            // Offset is applied in the FAVOURABLE direction: below the reference for longs,
            // above it for shorts. Positive = more passive (better price, lower fill rate).
            // Negative chases into the move (worse price, higher fill rate).
            price -= GetLimitOffsetPoints() * direction;

            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        private double GetLimitOffsetPoints()
        {
            if (LimitOffsetMode != EMALLimitOffsetMode.PerSession)
                return LimitOffsetPoints;

            switch (GetSessionIndex(GetBarOpenRaw()))
            {
                case 0: return AsiaLimitOffset;
                case 2: return UsLimitOffset;
                case 3: return Us0928LimitOffset;
                case 5: return Us0955LimitOffset;
                default: return LimitOffsetPoints;
            }
        }

        private void CancelEntryOrderIfActive()
        {
            if (!IsOrderActive(entryOrder)
                || entryCancelPending
                || IsHistoricalOrderAwaitingRealtimeTransition(entryOrder))
            {
                return;
            }

            cancelBarEndCount++;
            entryCancelPending = true;
            RecordNtOrderAction("cancel-entry-bar-end");
            CancelOrder(entryOrder);
        }

        private void ClearActiveEntryContext()
        {
            activeEntryDirection = 0;
            activeEntryReferencePrice = 0.0;
            activeEntrySubmitTime = DateTime.MinValue;
            entryCancelPending = false;
        }

        private void BeginProtectionTracking(string entrySignal, double takeProfitPoints, double stopLossPoints,
            double bracketReference)
        {
            protectedEntrySignal = entrySignal ?? string.Empty;
            activeTakeProfitPoints = Math.Max(TickSize, takeProfitPoints);
            activeStopLossPoints = Math.Max(TickSize, stopLossPoints);
            activeBracketReference = bracketReference;
            entryFillValue = 0.0;
            entryFilledQuantity = 0;
            openExitValue = 0.0;
            openExitQuantity = 0;
            desiredProtectionTargetPrice = 0.0;
            desiredProtectionQuantity = 0;
            protectiveStopOrder = null;
            profitTargetOrder = null;
            terminalExitPending = false;
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
            queuedBracketReference = 0.0;
            queuedTakeProfitPoints = 0.0;
            queuedStopLossPoints = 0.0;
            queuedSignalPrice = 0.0;
            queuedEntryBar = -1;
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

            if (orderName == NewsExitSignal)
            {
                if (orderState == OrderState.Rejected || orderState == OrderState.Cancelled)
                {
                    Print(string.Format(
                        "{0} | news flatten rejected | error={1} comment={2} | retrying emergency exit",
                        time,
                        error,
                        comment ?? string.Empty));
                    ScheduleTerminalExitRetry("NewsReject", order.FromEntrySignal, rateLimitedRejection);
                }

                return;
            }

            if (orderName != LongEntrySignal && orderName != ShortEntrySignal)
                return;

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
                if (Position.MarketPosition == MarketPosition.Flat)
                    ReleaseOrderRateReservation();
                TrySubmitQueuedEntry();
            }
            else if (orderState == OrderState.Rejected)
            {
                entryOrder = null;
                ClearActiveEntryContext();
                ClearQueuedEntry();
                CancelProjectXEntryMirror(true);
                ReleaseOrderRateReservation();
                Print(string.Format(
                    "{0} | {1} entry rejected | error={2} comment={3}",
                    time,
                    order.Name,
                    error,
                    comment ?? string.Empty));
            }
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
                    BeginProtectionTracking(orderName, GetConfiguredTakeProfit(), GetConfiguredStopLoss(), 0.0);

                entryFillValue += price * executionQuantity;
                entryFilledQuantity += executionQuantity;
                openEntryPrice = entryFillValue / entryFilledQuantity;
                openEntryDirection = orderName == LongEntrySignal ? 1 : -1;

                // First fill starts the time-stop clock; later partials do not restart it.
                if (openEntryTime == DateTime.MinValue)
                    openEntryTime = time;

                if (maxAccountBalanceLimitReached || maxDailyProfitLimitReached)
                {
                    TrySubmitTerminalExit(
                        maxAccountBalanceLimitReached ? "MaxAccountBalance" : "MaxDailyProfit",
                        orderName);
                    return;
                }

                double averageEntryPrice = entryFillValue / entryFilledQuantity;
                SubmitOrUpdateProtection(
                    orderName == LongEntrySignal ? MarketPosition.Long : MarketPosition.Short,
                    averageEntryPrice,
                    entryFilledQuantity,
                    time);
                return;
            }

            if (orderName == StopExitSignal
                || orderName == TargetExitSignal
                || orderName == NewsExitSignal
                || IsTerminalExitOrderName(orderName))
            {
                bool positionIsFlat = marketPosition == MarketPosition.Flat
                    || Position.MarketPosition == MarketPosition.Flat;

                if (positionIsFlat && projectXEntryMirrorActive)
                {
                    if (suppressProjectXNextExecutionExit)
                    {
                        suppressProjectXNextExecutionExit = false;
                        projectXEntryMirrorActive = false;
                    }
                    else if (SendWebhook("exit", 0.0, 0.0, 0.0, true, Math.Abs(quantity)))
                    {
                        projectXEntryMirrorActive = false;
                    }
                    else
                    {
                        projectXOrphanRecoveryCount++;
                        projectXOrphanRecoveryDueUtc = DateTime.UtcNow.AddSeconds(5);
                    }

                    if (!projectXEntryMirrorActive)
                    {
                        projectXLastSyncedStopPrice = 0.0;
                        projectXLastSyncedTargetPrice = 0.0;
                        projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                        projectXOrphanRecoveryCount = 0;
                    }
                }

                RecordRealizedPoints(price, quantity, positionIsFlat);

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
                activeStopLossPoints > 0.0 ? activeStopLossPoints : StopLossPoints);
            double targetDistance = Math.Max(TickSize, activeTakeProfitPoints);

            // Fill      - barriers measured from the actual fill (original behaviour).
            // Reference - barriers measured from the UNOFFSET limit price, so an offset entry
            //             lands on exactly the barrier prices the un-offset trade would have
            //             had. The trade resolves identically and the offset is pure gain.
            //             Falls back to the fill if no reference was captured (market entries).
            double anchor = BracketAnchor == EMALBracketAnchor.Reference && activeBracketReference > 0.0
                ? activeBracketReference
                : averageEntryPrice;

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
                double marketPrice = GetProtectiveReferencePrice(positionDirection);

                if (marketPrice > 0.0)
                {
                    bool stopAlreadyBreached = positionDirection == MarketPosition.Long
                        ? stopPrice >= marketPrice
                        : stopPrice <= marketPrice;
                    bool targetAlreadyReached = positionDirection == MarketPosition.Long
                        ? targetPrice <= marketPrice
                        : targetPrice >= marketPrice;

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

                CancelProjectXEntryMirror(Position.MarketPosition == MarketPosition.Flat);
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
            if (!projectXEntryMirrorActive
                || WebhookProviderType != WebhookProvider.ProjectX
                || Position.MarketPosition != MarketPosition.Flat)
            {
                return;
            }

            if (!SendWebhook("exit"))
                Print("EMAL CRITICAL: ProjectX mirror could not be verified flat during strategy termination.");
            else
                projectXEntryMirrorActive = false;
        }

        // CME trading day: 18:00 ET starts the NEXT day's session, so anything at or after
        // 18:00 belongs to tomorrow. Keeps an overnight session in one bucket.
        private DateTime GetTradingDay(DateTime easternTime)
        {
            return easternTime.Hour >= 18
                ? easternTime.Date.AddDays(1)
                : easternTime.Date;
        }

        private void RollTradingDayIfNeeded()
        {
            DateTime day = GetTradingDay(ConvertToEastern(GetBarOpenRaw()));

            if (day == currentTradingDay)
                return;

            if (currentTradingDay != DateTime.MinValue
                && (MaxDailyProfitPoints > 0.0 || MaxDailyLossPoints > 0.0))
            {
                Print(string.Format("{0:yyyy-MM-dd} | day closed | realised {1:F2} pts | profit cap {2} | loss cap {3}",
                    currentTradingDay, dailyRealizedPoints,
                    dailyProfitLimitReached ? "HIT" : "-",
                    dailyLossLimitReached ? "HIT" : "-"));
            }

            currentTradingDay = day;
            dailyRealizedPoints = 0.0;
            dailyProfitLimitReached = false;
            dailyLossLimitReached = false;
        }

        // Daily loss cap. MaxDailyLossPoints is entered as a POSITIVE number of points.
        // Blocks new entries only; an open position is left to its stop and target.
        private bool IsDailyLossBlocked()
        {
            if (MaxDailyLossPoints <= 0.0)
                return false;

            if (dailyLossLimitReached)
                return true;

            if (dailyRealizedPoints > -Math.Abs(MaxDailyLossPoints))
                return false;

            dailyLossLimitReached = true;
            ClearQueuedEntry();
            CancelEntryOrderIfActive();

            Print(string.Format(
                "{0:yyyy-MM-dd} | DAILY LOSS LIMIT | realised {1:F2} pts <= -{2:F2} | no new entries today",
                currentTradingDay, dailyRealizedPoints, Math.Abs(MaxDailyLossPoints)));

            return true;
        }

        // Blocks NEW entries only. An open position is left to its stop and target, exactly
        // like the session filter.
        private bool IsDailyProfitBlocked()
        {
            if (MaxDailyProfitPoints <= 0.0)
                return false;

            if (dailyProfitLimitReached)
                return true;

            if (dailyRealizedPoints < MaxDailyProfitPoints)
                return false;

            dailyProfitLimitReached = true;
            ClearQueuedEntry();
            CancelEntryOrderIfActive();

            Print(string.Format(
                "{0:yyyy-MM-dd} | DAILY PROFIT LIMIT | realised {1:F2} pts >= {2:F2} | no new entries today",
                currentTradingDay, dailyRealizedPoints, MaxDailyProfitPoints));

            return true;
        }

        private void RecordRealizedPoints(double exitPrice, int exitQuantity, bool positionIsFlat)
        {
            if (openEntryDirection == 0 || openEntryPrice <= 0.0)
            {
                if (positionIsFlat)
                {
                    openExitValue = 0.0;
                    openExitQuantity = 0;
                }

                return;
            }

            int filledQuantity = Math.Abs(exitQuantity);

            if (filledQuantity > 0)
            {
                openExitValue += exitPrice * filledQuantity;
                openExitQuantity += filledQuantity;
            }

            if (!positionIsFlat)
                return;

            if (openExitQuantity > 0)
            {
                double averageExitPrice = openExitValue / openExitQuantity;
                dailyRealizedPoints += (averageExitPrice - openEntryPrice) * openEntryDirection;
            }

            openEntryDirection = 0;
            openEntryPrice = 0.0;
            openEntryTime = DateTime.MinValue;
            openExitValue = 0.0;
            openExitQuantity = 0;
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

            Print(string.Format(
                "{0} | max account balance reached | netLiq={1:F2} target={2:F2} | trading stopped",
                lastTickTime != DateTime.MinValue ? lastTickTime : Time[0],
                netLiquidation,
                MaxAccountBalance));

            return true;
        }

        private bool IsAccountDailyProfitBlocked()
        {
            if (MaxDailyProfit <= 0.0)
            {
                maxDailyProfitLimitReached = false;
                maxDailyProfitStartBalance = double.NaN;
                maxDailyProfitDate = DateTime.MinValue;
                return false;
            }

            DateTime currentDate = Time[0].Date;
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
            activeBracketReference = 0.0;
            entryFillValue = 0.0;
            entryFilledQuantity = 0;
            desiredProtectionTargetPrice = 0.0;
            desiredProtectionQuantity = 0;
            protectiveStopOrder = null;
            profitTargetOrder = null;
            terminalExitPending = false;
            ClearTerminalExitRetry();
            ReleaseOrderRateReservation();
        }

        private bool IsLiveOrderRateGuardActive()
        {
            return EnableOrderRateGuard
                && State == State.Realtime
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

        private double GetProjectXMarketReferencePrice(int direction)
        {
            double price = direction > 0 ? GetCurrentAsk() : GetCurrentBid();
            if (price <= 0.0 || double.IsNaN(price) || double.IsInfinity(price))
                price = lastTickPrice;
            if ((price <= 0.0 || double.IsNaN(price) || double.IsInfinity(price)) && CurrentBar >= 0)
                price = Close[0];
            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        private void SendPlannedProjectXEntry(int direction, double plannedEntryPrice,
            double bracketReference, double takeProfitPoints, double stopLossPoints)
        {
            if (State != State.Realtime || WebhookProviderType != WebhookProvider.ProjectX)
                return;

            double entry = Instrument.MasterInstrument.RoundToTickSize(plannedEntryPrice);
            double anchor = BracketAnchor == EMALBracketAnchor.Reference && bracketReference > 0.0
                ? Instrument.MasterInstrument.RoundToTickSize(bracketReference)
                : entry;
            double target = Instrument.MasterInstrument.RoundToTickSize(
                direction > 0 ? anchor + takeProfitPoints : anchor - takeProfitPoints);
            double stop = Instrument.MasterInstrument.RoundToTickSize(
                direction > 0 ? anchor - stopLossPoints : anchor + stopLossPoints);

            bool hadUnresolvedMirror = projectXEntryMirrorActive;
            bool sent = SendWebhook(
                direction > 0 ? "buy" : "sell",
                entry,
                target,
                stop,
                EntryOrderType == EMALEntryOrderType.Market,
                Contracts);

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

        private void CancelProjectXEntryMirror(bool flattenIfOrphaned)
        {
            if (!projectXEntryMirrorActive || WebhookProviderType != WebhookProvider.ProjectX)
                return;

            if (flattenIfOrphaned)
            {
                if (SendWebhook("exit"))
                {
                    projectXEntryMirrorActive = false;
                    projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                    projectXOrphanRecoveryCount = 0;
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
            else
            {
                SendWebhook("cancel");
            }

            if (!projectXEntryMirrorActive || !flattenIfOrphaned)
            {
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
            }
        }

        private void EvaluateProjectXOrphanRecovery()
        {
            if (!projectXEntryMirrorActive
                || WebhookProviderType != WebhookProvider.ProjectX
                || Position.MarketPosition != MarketPosition.Flat
                || IsOrderActive(entryOrder)
                || projectXOrphanRecoveryDueUtc == DateTime.MinValue
                || DateTime.UtcNow < projectXOrphanRecoveryDueUtc)
            {
                return;
            }

            if (SendWebhook("exit"))
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

        private void SendExplicitProjectXExit(string reason)
        {
            if (!projectXEntryMirrorActive || WebhookProviderType != WebhookProvider.ProjectX)
                return;

            bool sent = SendWebhook("exit", 0.0, 0.0, 0.0, true, Math.Abs(Position.Quantity));
            if (sent)
            {
                suppressProjectXNextExecutionExit = true;
                Print(string.Format("{0} | ProjectX explicit exit sent | reason={1}",
                    lastTickTime != DateTime.MinValue ? lastTickTime : Time[0], reason));
            }
        }

        private bool SendWebhook(string eventType, double entryPrice = 0.0, double takeProfit = 0.0,
            double stopLoss = 0.0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime && State != State.Terminated)
                return false;

            int quantity = quantityOverride > 0 ? quantityOverride : Math.Max(1, Contracts);
            if (WebhookProviderType == WebhookProvider.ProjectX)
                return SendProjectX(eventType, entryPrice, takeProfit, stopLoss, isMarketEntry, quantity);

            if (string.IsNullOrWhiteSpace(WebhookUrl))
                return false;

            try
            {
                string ticker = !string.IsNullOrWhiteSpace(WebhookTickerOverride)
                    ? WebhookTickerOverride.Trim()
                    : (Instrument != null && Instrument.MasterInstrument != null
                        ? Instrument.MasterInstrument.Name
                        : "UNKNOWN");
                string action = (eventType ?? string.Empty).ToLowerInvariant();
                string json;
                if (action == "buy" || action == "sell")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"{1}\",\"orderType\":\"{2}\",\"quantityType\":\"fixed_quantity\",\"quantity\":{3},\"signalPrice\":{4},\"takeProfit\":{{\"limitPrice\":{5}}},\"stopLoss\":{{\"type\":\"stop\",\"stopPrice\":{6}}}}}",
                        JsonEscape(ticker), action, isMarketEntry ? "market" : "limit", quantity,
                        FormatProjectXPrice(entryPrice), FormatProjectXPrice(takeProfit), FormatProjectXPrice(stopLoss));
                }
                else
                {
                    string tpAction = action == "cancel" ? "cancel" : "exit";
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"{1}\"}}",
                        JsonEscape(ticker), tpAction);
                }

                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.UploadString(WebhookUrl, "POST", json);
                }
                return true;
            }
            catch (Exception ex)
            {
                Print("EMAL webhook error: " + ex.Message);
                return false;
            }
        }

        private bool SendProjectX(string eventType, double entryPrice, double takeProfit,
            double stopLoss, bool isMarketEntry, int quantity)
        {
            if (!EnsureProjectXSession())
                return false;

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetProjectXTargets(out targetAccounts, out contractId))
                return false;

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
                                && ProjectXPlaceOrder(eventType, account.Id, contractId, entryPrice,
                                    takeProfit, stopLoss, isMarketEntry, quantity))
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

        private void RunProjectXStartupPreflight()
        {
            if (WebhookProviderType != WebhookProvider.ProjectX)
                return;

            ProjectXLog(string.Format(
                "ProjectX startup preflight begin | instrument={0} selectors={1}",
                GetProjectXInstrumentKey(), ProjectXAccountId ?? string.Empty));

            if (!EnsureProjectXSession())
            {
                ProjectXLog("ProjectX startup preflight failed | stage=auth");
                return;
            }

            List<ProjectXAccountInfo> targets;
            string contractId;
            if (!TryGetProjectXTargets(out targets, out contractId))
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
            if (string.IsNullOrWhiteSpace(ProjectXApiBaseUrl)
                || string.IsNullOrWhiteSpace(ProjectXUsername)
                || string.IsNullOrWhiteSpace(ProjectXApiKey))
            {
                ProjectXLog("ProjectX login failed | missing base URL or credentials");
                return false;
            }

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

        private bool TryGetProjectXTargets(out List<ProjectXAccountInfo> targetAccounts, out string contractId)
        {
            targetAccounts = null;
            contractId = null;
            if (!TryResolveProjectXContractId(out contractId))
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

        private bool TryResolveProjectXContractId(out string contractId)
        {
            contractId = null;
            if (!string.IsNullOrWhiteSpace(ProjectXContractId))
            {
                contractId = ProjectXContractId.Trim();
                return true;
            }

            string instrumentKey = GetProjectXInstrumentKey();
            if (!string.IsNullOrWhiteSpace(projectXResolvedContractId)
                && string.Equals(projectXResolvedInstrumentKey, instrumentKey, StringComparison.OrdinalIgnoreCase))
            {
                contractId = projectXResolvedContractId;
                return true;
            }

            string root = GetProjectXInstrumentRoot();
            if (string.IsNullOrWhiteSpace(root))
                return false;

            DateTime expiry;
            string suffix = TryGetInstrumentExpiry(out expiry) || TryParseInstrumentExpiryFromFullName(out expiry)
                ? GetProjectXFuturesMonthCode(expiry.Month) + expiry.ToString("yy", CultureInfo.InvariantCulture)
                : string.Empty;

            List<ProjectXContractInfo> contracts;
            if (!TrySearchProjectXContracts(root, suffix, out contracts))
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

        private void SyncProjectXProtectionUpdate(ProjectXProtectionOrderKind kind, double price, string reason)
        {
            if (State != State.Realtime
                || WebhookProviderType != WebhookProvider.ProjectX
                || !projectXEntryMirrorActive
                || Position.MarketPosition == MarketPosition.Flat)
            {
                return;
            }

            price = Instrument.MasterInstrument.RoundToTickSize(price);
            if (price <= 0.0 || double.IsNaN(price) || double.IsInfinity(price))
                return;

            double lastPrice = kind == ProjectXProtectionOrderKind.StopLoss
                ? projectXLastSyncedStopPrice
                : projectXLastSyncedTargetPrice;
            if (lastPrice > 0.0 && Math.Abs(lastPrice - price) < TickSize / 2.0)
                return;

            if (!EnsureProjectXSession())
                return;

            List<ProjectXAccountInfo> targets;
            string contractId;
            if (!TryGetProjectXTargets(out targets, out contractId))
                return;

            bool modifiedAny = false;
            int expectedSide = Position.MarketPosition == MarketPosition.Long ? 1 : 0;
            foreach (ProjectXAccountInfo account in targets)
            {
                Dictionary<string, object> order = SelectProjectXProtectionOrder(
                    account.Id, contractId, kind, expectedSide);
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
                    size = Math.Max(1, Math.Abs(Position.Quantity));

                string response = ProjectXModifyProtectionOrder(account.Id, orderId, size, kind, price);
                bool success;
                if (!TryGetJsonBool(response, "success", out success) || success)
                    modifiedAny = true;
                else
                    ProjectXLog(string.Format(
                        "ProjectX protection sync failed | account={0} order={1} kind={2} price={3:0.00} reason={4}",
                        account.Id, orderId, kind, price, reason));
            }

            if (modifiedAny)
            {
                if (kind == ProjectXProtectionOrderKind.StopLoss)
                    projectXLastSyncedStopPrice = price;
                else
                    projectXLastSyncedTargetPrice = price;
            }
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
            string limit = kind == ProjectXProtectionOrderKind.TakeProfit ? FormatProjectXPrice(price) : "null";
            string stop = kind == ProjectXProtectionOrderKind.StopLoss ? FormatProjectXPrice(price) : "null";
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"orderId\":{1},\"size\":{2},\"limitPrice\":{3},\"stopPrice\":{4},\"trailPrice\":null}}",
                accountId, orderId, Math.Max(1, size), limit, stop);
            return ProjectXPost("/api/Order/modify", json, true);
        }

        private bool ProjectXPlaceOrder(string side, int accountId, string contractId,
            double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            int orderSide = string.Equals(side, "buy", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int orderType = isMarketEntry ? 2 : 1;
            int normalizedQuantity = Math.Max(1, quantity);
            double entry = Instrument.MasterInstrument.RoundToTickSize(entryPrice);
            bool isLong = orderSide == 0;
            int tpTicks = NormalizeProjectXBracketTicks(
                PriceToTicks(takeProfit - entry), 4, isLong ? 1 : -1);
            int slTicks = NormalizeProjectXBracketTicks(
                PriceToTicks(stopLoss - entry), 1, isLong ? -1 : 1);
            string limitPart = isMarketEntry
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, ",\"limitPrice\":{0}", FormatProjectXPrice(entry));
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

        private string FormatProjectXPrice(double price)
        {
            return Instrument.MasterInstrument.RoundToTickSize(price)
                .ToString("0.########", CultureInfo.InvariantCulture);
        }

        private int PriceToTicks(double distance)
        {
            return TickSize > 0.0
                ? (int)Math.Round(distance / TickSize, MidpointRounding.AwayFromZero)
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
            if (WebhookProviderType == WebhookProvider.ProjectX)
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

        [NinjaScriptProperty]
        [Display(Name = "Trade Parity", Description = "Reduce trade count by trading only alternate candles. Even = even-numbered minute; Odd = odd-numbered minute; Both = every candle (current behaviour).", GroupName = "Time Frame", Order = 1)]
        public EMALTradeParity TradeParity { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Account Balance", Description = "When account net liquidation, including unrealized P&L, reaches this value, pending entries are cancelled, open positions are flattened, and new entries remain blocked. 0 disables.", GroupName = "Risk", Order = 0)]
        public double MaxAccountBalance { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Daily Profit", Description = "Maximum daily account profit in currency, measured from the first primary bar's net liquidation for each calendar date. Reaching it cancels pending entries, flattens open positions, and blocks new entries for the rest of that date. 0 disables.", GroupName = "Risk", Order = 1)]
        public double MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Order Rate Guard", Description = "Share a rolling EMAL order-action budget across all strategy instances on the same NT8 connection. Blocks new entries only; protection and exits are never blocked.", GroupName = "Risk", Order = 2)]
        public bool EnableOrderRateGuard { get; set; }

        [Range(NewTradeActionReserve, 5000), NinjaScriptProperty]
        [Display(Name = "Order Actions / Hour", Description = "Conservative local EMAL action ceiling per NT8 connection. Default 1100 leaves headroom below Tradovate's observed 1500-request provider limit.", GroupName = "Risk", Order = 3)]
        public int OrderActionLimitPerHour { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL", Description = "Optional TradersPost endpoint. Leave empty when using ProjectX.", GroupName = "ProjectX / Webhooks", Order = 0)]
        public string WebhookUrl
        {
            get { return webhookUrl ?? string.Empty; }
            set { webhookUrl = value ?? string.Empty; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Ticker Override", Description = "Optional TradersPost ticker override. Leave empty to use the chart instrument.", GroupName = "ProjectX / Webhooks", Order = 1)]
        public string WebhookTickerOverride
        {
            get { return webhookTickerOverride ?? string.Empty; }
            set { webhookTickerOverride = value ?? string.Empty; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Provider", Description = "Select TradersPost or direct ProjectX order routing.", GroupName = "ProjectX / Webhooks", Order = 2)]
        public WebhookProvider WebhookProviderType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Base URL", GroupName = "ProjectX / Webhooks", Order = 3)]
        public string ProjectXApiBaseUrl { get; set; }

        [Browsable(false)]
        public bool ProjectXTradeAllAccounts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Username", Description = "ProjectX login username for direct routing.", GroupName = "ProjectX / Webhooks", Order = 5)]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX API Key", Description = "ProjectX API key used with the username.", GroupName = "ProjectX / Webhooks", Order = 6)]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Accounts", Description = "Comma-separated ProjectX account IDs or exact account names.", GroupName = "ProjectX / Webhooks", Order = 7)]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Contract ID", Description = "Hidden optional contract override for support/debug use.", GroupName = "ProjectX / Webhooks", Order = 8)]
        public string ProjectXContractId { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Rollover Block Start (yyyy-MM-dd)", Description = "First session date of a new contract, e.g. 2026-09-14. The strategy blocks ALL entries for the first N trading days (Rollover Block Sessions) starting here. Blank = off. Update it each quarter at the roll.", GroupName = "Rollover", Order = 0)]
        public string RolloverBlockStart { get; set; }

        [Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Rollover Block Sessions", Description = "How many trading days (Sun-Fri; Saturday skipped) to block from Rollover Block Start. 0 = off.", GroupName = "Rollover", Order = 1)]
        public int RolloverBlockSessions { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Max Daily Profit (points)", Description = "Once realised profit for the trading day reaches this many points, no further entries are taken until the next day. Any open position is left to its stop and target. Resets at 18:00 ET (CME trading day). 0 disables.", GroupName = "Risk", Order = 1)]
        public double MaxDailyProfitPoints { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Max Daily Loss (points)", Description = "Enter as a POSITIVE number. Once realised loss for the trading day reaches this many points, no further entries are taken until the next day. Any open position is left to its stop and target. Resets at 18:00 ET. 0 disables.", GroupName = "Risk", Order = 2)]
        public double MaxDailyLossPoints { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Time Stop (seconds)", Description = "Close a trade that has been open this long without resolving. Winners resolve in a median 10s; a trade still open at 15s wins only 72% against an 81.8% breakeven. 0 disables.", GroupName = "Time Stop", Order = 0)]
        public double TimeStopSeconds { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Only When Losing", Description = "Fire the time stop only when the trade is underwater. Leave ON: trades grinding toward target are left alone.", GroupName = "Time Stop", Order = 1)]
        public bool TimeStopOnlyWhenLosing { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Time Stop Loss (points)", Description = "How far underwater before the time stop fires. 0 = any loss at all. Worth raising: among winners still open at 60s, mean adverse excursion was 9.32 points, so a bare sign test cuts eventual winners.", GroupName = "Time Stop", Order = 2)]
        public double TimeStopLossPoints { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "EMA Period", Description = "EMA period evaluated on the one-minute chart.", GroupName = "Parameters", Order = 0)]
        public int EmaPeriod { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Minimum EMA Slope (points/minute)", Description = "Minimum completed-bar EMA change required in the trade direction.", GroupName = "Parameters", Order = 1)]
        public double MinimumEmaSlopePoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Take Profit (points)", Description = "Profit-target distance from the actual fill.", GroupName = "Parameters", Order = 2)]
        public double TakeProfitPoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop Loss (points)", Description = "Stop-loss distance from the actual fill.", GroupName = "Parameters", Order = 3)]
        public double StopLossPoints { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Contracts", Description = "Number of contracts per entry.", GroupName = "Parameters", Order = 4)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Order Type", Description = "Market, or limit at the price set by Limit Price Reference.", GroupName = "Parameters", Order = 5)]
        public EMALEntryOrderType EntryOrderType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Limit Price Reference", Description = "Where a limit entry is placed. BidAsk = passive, bid for longs and ask for shorts. Open = this bar's open. Close = previous bar's close. Ignored when Entry Order Type is Market.", GroupName = "Parameters", Order = 6)]
        public EMALLimitPriceReference LimitPriceReference { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bracket Anchor", Description = "Fill: stop and target are measured from the actual fill (original behaviour). Reference: measured from the UNOFFSET limit price, so an offset entry keeps the exact barrier prices the un-offset trade would have had - identical outcome, offset is pure gain. Only matters when Limit Offset is non-zero.", GroupName = "Limit Offset", Order = 0)]
        public EMALBracketAnchor BracketAnchor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Limit Offset Mode", Description = "Global uses one offset everywhere. PerSession uses the per-session values (hidden). Start with Global.", GroupName = "Limit Offset", Order = 1)]
        public EMALLimitOffsetMode LimitOffsetMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Limit Offset (points)", Description = "Shifts the limit entry in your favour: below the reference for longs, above for shorts. Positive = more passive, better price, fewer fills. Negative chases the move. 0 = off.", GroupName = "Limit Offset", Order = 2)]
        public double LimitOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Asia Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 3)]
        public double AsiaLimitOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 5)]
        public double UsLimitOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:28-09:50 Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 6)]
        public double Us0928LimitOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:55-10:30 Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 7)]
        public double Us0955LimitOffset { get; set; }











        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Limit Timeout (seconds)", Description = "Cancel an unfilled limit entry after this many seconds. 0 disables; the order then lives until the next bar opens.", GroupName = "Limit Entry Cancellation", Order = 0)]
        public double LimitOrderTimeoutSeconds { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Cancel If Moved (points)", Description = "Cancel an unfilled limit entry once price has travelled this far in the signal direction without us. 0 disables.", GroupName = "Limit Entry Cancellation", Order = 1)]
        public double CancelIfMovedPoints { get; set; }

        // ================================================================================
        // Advanced (Steve, 2026-08-01, EMAL-21) - moved out of Sessions 1m into their own
        // section, even though both remain hidden. Both apply regardless of Time Frame
        // (unchanged behavior) - not 1m-specific despite where they used to live.
        // ================================================================================

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Per-Session Settings", Description = "Enable the per-session split (Asia 18:30-03:00, US 09:28-09:50, US 09:55-10:30, US 10:30-17:00). When off, the global Minimum EMA Slope applies to every hour.", GroupName = "Advanced", Order = 0)]
        public bool UsePerSessionSettings { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Bucket Filter", Description = "Restrict entries to the 33 approved 30-minute windows derived from 78 sessions of playback. The window list is hardcoded and not user-editable. Risk-reduction setting: it lowers drawdown and also lowers gross profit.", GroupName = "Advanced", Order = 1)]
        public bool UseBucketFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Tune US Windows Free", Description = "Research-only escape hatch (EMAL-24, Steve 2026-08-03). When on, ResolveWindowPresets() ignores Us0928Setting/Us0955Setting and reads TP/SL directly from Us0928TakeProfitPoints/Us0928StopLossPoints/Us0955TakeProfitPoints/Us0955StopLossPoints, and stops overwriting Us0928MinimumSlope/Us0955MinimumSlope - making all six fields freely tunable instead of preset-locked. OFF by default; live behavior unchanged.", GroupName = "Advanced", Order = 2)]
        public bool TuneUsWindowsFree { get; set; }

        // ================================================================================
        // Sessions 1m (Steve, 2026-08-01, EMAL-21) - reorganized so the two US windows this
        // strategy actually runs come first, then the minute filter, with Asia/US-cash (rarely
        // touched) hidden at the bottom. Europe removed entirely 2026-08-02 (Steve: "I never
        // want to use this bot on London").
        // ================================================================================

        [NinjaScriptProperty]
        [Display(Name = "US 09:28-09:50 Enabled", GroupName = "Sessions 1m", Order = 0)]
        public bool Us0928Enabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 09:28-09:50 Setting", Description = "Preset TP/SL/slope for the US 09:28-09:50 window. Member name reads TP_SL_Slope (e.g. TP5_SL18_Slope2_75 = TP 5, SL 18, slope 2.75).", GroupName = "Sessions 1m", Order = 1)]
        public EMALUs0928Setting Us0928Setting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:28-09:50 Min Slope", Description = "Driven by the US 09:28-09:50 Setting preset; not user-editable.", GroupName = "Sessions 1m", Order = 2)]
        public double Us0928MinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 09:55-10:30 Enabled", GroupName = "Sessions 1m", Order = 3)]
        public bool Us0955Enabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 09:55-10:30 Setting", Description = "Preset TP/SL/slope for the US 09:55-10:30 window. Member name reads TP_SL_Slope (e.g. TP4_SL18_Slope2_75 = TP 4, SL 18, slope 2.75).", GroupName = "Sessions 1m", Order = 4)]
        public EMALUs0955Setting Us0955Setting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:55-10:30 Min Slope", Description = "Driven by the US 09:55-10:30 Setting preset; not user-editable.", GroupName = "Sessions 1m", Order = 5)]
        public double Us0955MinimumSlope { get; set; }

        // Free TP/SL fields (EMAL-24, Steve 2026-08-03). Inert unless TuneUsWindowsFree is on -
        // see ResolveWindowPresets(). Hidden from the live UI like the preset/slope fields above;
        // reachable through the CLI tuner via their parameter ids.
        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:28-09:50 Take Profit (free)", Description = "Only used when Tune US Windows Free is on; otherwise driven by the US 09:28-09:50 Setting preset.", GroupName = "Sessions 1m", Order = 6)]
        public double Us0928TakeProfitPoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:28-09:50 Stop Loss (free)", Description = "Only used when Tune US Windows Free is on; otherwise driven by the US 09:28-09:50 Setting preset.", GroupName = "Sessions 1m", Order = 7)]
        public double Us0928StopLossPoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:55-10:30 Take Profit (free)", Description = "Only used when Tune US Windows Free is on; otherwise driven by the US 09:55-10:30 Setting preset.", GroupName = "Sessions 1m", Order = 8)]
        public double Us0955TakeProfitPoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:55-10:30 Stop Loss (free)", Description = "Only used when Tune US Windows Free is on; otherwise driven by the US 09:55-10:30 Setting preset.", GroupName = "Sessions 1m", Order = 9)]
        public double Us0955StopLossPoints { get; set; }

        // Minute-of-5 filter (Steve, 2026-08-01). One shared setting applied to whichever
        // sessions/windows are enabled - deliberately not per-window (Us0928/Us0955 or otherwise).
        // Only meaningful while the strategy evaluates 1-minute bars; see IsMinuteAllowed().
        [NinjaScriptProperty]
        [Display(Name = "Enable Minute Filter", Description = "Master switch. When off, all five minute positions trade (current behavior). When on, only the positions checked below are allowed to enter, across every enabled session/window.", GroupName = "Sessions 1m", Order = 6)]
        public bool EnableMinuteFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Minute 1a", Description = "Allow entries on the 1st minute of each 5-minute grouping (bar-open minute % 5 == 0).", GroupName = "Sessions 1m", Order = 7)]
        public bool TradeMinute1a { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Minute 1b", Description = "Allow entries on the 2nd minute of each 5-minute grouping (bar-open minute % 5 == 1).", GroupName = "Sessions 1m", Order = 8)]
        public bool TradeMinute1b { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Minute 1c", Description = "Allow entries on the 3rd minute of each 5-minute grouping (bar-open minute % 5 == 2).", GroupName = "Sessions 1m", Order = 9)]
        public bool TradeMinute1c { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Minute 1d", Description = "Allow entries on the 4th minute of each 5-minute grouping (bar-open minute % 5 == 3).", GroupName = "Sessions 1m", Order = 10)]
        public bool TradeMinute1d { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Minute 1e", Description = "Allow entries on the 5th minute of each 5-minute grouping (bar-open minute % 5 == 4).", GroupName = "Sessions 1m", Order = 11)]
        public bool TradeMinute1e { get; set; }

        // Rarely touched - hidden at the bottom of Sessions 1m rather than removed (Steve,
        // 2026-08-01, EMAL-21).
        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Asia 18:30-03:00 Enabled", GroupName = "Sessions 1m", Order = 22)]
        public bool AsiaEnabled { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Asia Min Slope", Description = "Slope threshold for the Asia session.", GroupName = "Sessions 1m", Order = 23)]
        public double AsiaMinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 10:30-17:00 Enabled", GroupName = "Sessions 1m", Order = 26)]
        public bool UsEnabled { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US Min Slope", Description = "Slope threshold for the US cash session (10:30-17:00).", GroupName = "Sessions 1m", Order = 27)]
        public double UsMinimumSlope { get; set; }








        [NinjaScriptProperty]
        [Display(Name = "Block News Window", Description = "Block new entries across a fixed daily time range, applied every day independently of the session gate.", GroupName = "News Blackout", Order = 0)]
        public bool BlockNewsWindow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten At News Block", Description = "Also close any open position at market when the blackout begins. This is the only rule that force-exits a trade; all other exits are left to the stop and target.", GroupName = "News Blackout", Order = 1)]
        public bool FlattenAtNewsBlock { get; set; }

        [Range(0, 2359), NinjaScriptProperty]
        [Display(Name = "News Block Start (HHmm)", Description = "Start of the blackout as HHmm. 828 = 08:28. Inclusive.", GroupName = "News Blackout", Order = 2)]
        public int NewsBlockStartTime { get; set; }

        [Range(0, 2359), NinjaScriptProperty]
        [Display(Name = "News Block End (HHmm)", Description = "End of the blackout as HHmm. 832 = 08:32. Inclusive, so the 08:32 bar is also blocked.", GroupName = "News Blackout", Order = 3)]
        public int NewsBlockEndTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Show Info Panel", Description = "Draw the strategy status panel in the lower-left of the chart.", GroupName = "Logging", Order = 2)]
        public bool ShowInfoPanel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Logging", Description = "Write one CSV row per completed trade containing the entry context: slope, the previous three bars OHLCV, fill delay and outcome. Turn off to disable all file writing.", GroupName = "Logging", Order = 0)]
        public bool EnableFeatureLog { get; set; }

        [Display(Name = "Log File Path", Description = "Full path to the CSV. Leave blank to auto-name a timestamped file in Documents. Appends if the file already exists. Ignored when Logging is off.", GroupName = "Logging", Order = 1)]
        public string FeatureLogPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Research Path Log", Description = "Research-only, leave OFF for live trading. Records per fill the first-touch time to a 0.5pt grid (+/-30pt, 300s horizon), tracked past the TP/SL exit, so any TP/SL can be reconstructed offline.", GroupName = "Logging", Order = 3)]
        public bool EnablePathLog { get; set; }

        [Browsable(false)]
        [Display(Name = "Path Log File", Description = "Full path to the research path-log CSV. Blank auto-names EMAL_path_log.csv in Documents.", GroupName = "Logging", Order = 4)]
        public string PathLogPath { get; set; }
    }

    public enum EMALTradeParity
    {
        Both,
        Even,
        Odd
    }

    // Per-window bracket presets (Steve, 2026-07-30). Member names read TP / SL / Slope
    // (e.g. TP2_SL10_Slope2_75 = TP 2, SL 10, slope 2.75). NinjaTrader shows the member name.
    public enum EMALUs0928Setting
    {
        TP2_SL10_Slope2_75,
        TP4_SL18_Slope2_75,
        TP2_SL14_Slope3_0,
        TP2_SL14_Slope2_75,
        TP5_SL18_Slope2_75
    }

    public enum EMALUs0955Setting
    {
        TP3_SL16_Slope2_75,
        TP3_SL18_Slope2_75,
        TP2_SL18_Slope2_75,
        TP4_SL18_Slope2_75,
        TP4_SL20_Slope2_50
    }

    public enum EMALEntryOrderType
    {
        Market,
        Limit
    }




    public enum EMALBracketAnchor
    {
        Fill,
        Reference
    }

    public enum EMALLimitOffsetMode
    {
        Global,
        PerSession
    }

    public enum EMALLimitPriceReference
    {
        BidAsk,
        Open,
        Close
    }
}
