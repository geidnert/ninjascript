#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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
        private const string StrategySignalPrefix = "EMAL";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";
        private const string StopExitSignal = StrategySignalPrefix + "Stop";
        private const string TargetExitSignal = StrategySignalPrefix + "Target";
        private const string TerminalExitSignalPrefix = StrategySignalPrefix + "Exit";
        private const string NewsExitSignal = StrategySignalPrefix + "NewsFlat";

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
        private ATR atr;
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
        private double us0920Tp, us0920Sl, us0955Tp, us0955Sl;
        private double entryFillValue;
        private int entryFilledQuantity;
        private double desiredProtectionTargetPrice;
        private int desiredProtectionQuantity;
        private bool terminalExitPending;

        // Account-level profit guard. Once net liquidation reaches the configured
        // ceiling, the latch remains set for the lifetime of this strategy instance.
        private bool maxAccountBalanceLimitReached;

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
        // Two special US windows carve the morning into their own sessions (Steve, 2026-07-29),
        // each a distinct stronger regime with its own settings. Europe always ends at 09:20
        // (its 09:20-09:30 tail goes to US 09:20-09:50). 09:50-09:55 is a hard no-trade block.
        private const int Us0920StartMinute = 9 * 60 + 20; // 09:20 ET, US 09:20-09:50 opens
        private const int Us0920EndMinute = 9 * 60 + 50;   // 09:50 ET
        private const int BlockStartMinute = 9 * 60 + 50;  // 09:50-09:55 ET, hard no-trade block
        private const int BlockEndMinute = 9 * 60 + 55;    // 09:55 ET
        private const int Us0955StartMinute = 9 * 60 + 55; // 09:55 ET, US 09:55-10:30 opens
        private const int Us0955EndMinute = 10 * 60 + 30;  // 10:30 ET
        private const int UsStartMinute = 10 * 60 + 30; // 10:30 ET, US proper begins
        private const int UsEndMinute = 17 * 60;        // 17:00 ET, cash close

        // London-anchored boundary. Europe begins at 08:00 London, which tracks UK DST and so
        // lands on 03:00 ET most of the year and 04:00 ET during the ~4 misaligned weeks.
        // Inside the overnight band (NY 18:30 -> 09:30) London time only ever spans roughly
        // 22:00 -> 14:30, so the range [08:00, 15:00) uniquely identifies the European portion.
        private const int LondonOpenMinute = 8 * 60;    // 08:00 London
        private const int LondonBandEndMinute = 15 * 60;

        // All time rules are evaluated in New York time regardless of how NinjaTrader's
        // display timezone is configured. TimeZoneInfo carries the full DST rule set, so
        // the spring and autumn shifts are handled automatically - no seasonal code.
        private TimeZoneInfo platformZone;
        private TimeZoneInfo easternZone;
        private TimeZoneInfo londonZone;
        private TimeZoneInfo tokyoZone;

        private const string FeatureHeader =
            "EntryTimeET,EntryTimeUTC,EntryTimeLondon,EntryTimeTokyo,DayOfWeek,HHmm,HHmmLondon,HHmmTokyo,"
            + "Session,Direction,EntryMode,LimitRef,SignalPrice,Ema,Slope,SlopePrev,SlopeAccel,ReqSlope,SlopeOverAtr,LimitOffset,"
            + "DistToEma,Atr,TpPoints,SlPoints,TpOverAtr,"
            + "Bar1Open,Bar1High,Bar1Low,Bar1Close,Bar1Volume,"
            + "Bar2Open,Bar2High,Bar2Low,Bar2Close,Bar2Volume,"
            + "Bar3Open,Bar3High,Bar3Low,Bar3Close,Bar3Volume,"
            + "AvgVolume20,FillPrice,FillDelaySec,"
            + "ExitTime,ExitPrice,ExitReason,ProfitPoints,IsWin,BarsHeld,MaePoints,MfePoints";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "EMA direction strategy for NQ with market or passive bid/ask limit entries "
                    + "and fixed take-profit and stop-loss brackets. Select the Time Frame (M1/M5/M15) and "
                    + "apply the strategy to a chart of the matching bar period.";
                Name = "EMAL";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                BarsRequiredToTrade = 1;

                // Timeframe. M1 is the tuned live config; the internally-managed trading
                // parameters below are re-affirmed per timeframe in ApplyTimeFrameSettings().
                TimeFrame = EMALTimeFrame.M1;
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
                EuropeLimitOffset = 0.0;
                UsLimitOffset = 0.0;
                Us0920LimitOffset = 0.0;
                Us0955LimitOffset = 0.0;
                MaxAccountBalance = 0.0;

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

                // ATR is retained for the feature log only; all TP/slope scaling removed in v14.
                AtrPeriod = 14;


                // Limit-entry cancellation, OFF by default (0 = disabled). With both off the
                // only rule is the original one: cancel at the open of the next bar.
                LimitOrderTimeoutSeconds = 0.0;
                CancelIfMovedPoints = 0.0;

                // Per-session settings. Defaults reproduce current behaviour exactly: all three
                // sessions on, all thresholds equal to the global MinimumEmaSlopePoints.
                // TP, SL and entry type stay GLOBAL by design - three independent copies of an
                // interacting triple is where overfitting lives.
                UsePerSessionSettings = true;
                AsiaEnabled = true;
                EuropeEnabled = true;
                UsEnabled = true;
                Us0920Enabled = true;
                Us0955Enabled = true;
                // Per-window bracket presets (Steve, 2026-07-30). Window 1 defaults to TP5/SL18
                // (best net/maxDD of its four options; no TP4 option there). Window 2 defaults to
                // TP4/SL18 = current behaviour. ResolveWindowPresets() applies them in DataLoaded.
                Us0920Setting = EMALUs0920Setting.TP5_SL18_Slope2_75;
                Us0955Setting = EMALUs0955Setting.TP4_SL18_Slope2_75;

                AsiaMinimumSlope = 3.0;
                EuropeMinimumSlope = 0.5;
                UsMinimumSlope = 2.75;
                Us0920MinimumSlope = 2.75;   // overwritten by ResolveWindowPresets from the Setting popup
                Us0955MinimumSlope = 2.75;


                // Blackout around the 08:30 US data release. HHmm, inclusive both ends.
                BlockNewsWindow = true;
                FlattenAtNewsBlock = true;
                NewsBlockStartTime = 828;
                NewsBlockEndTime = 832;

                UseBucketFilter = true;
                ShowInfoPanel = true;
                EnableFeatureLog = true;
                FeatureLogPath = @"C:\Users\Administrator\Documents\EMAL_features.csv";
                EnablePathLog = false;   // research-only; never on for live trading
                PathLogPath = string.Empty;
            }
            else if (State == State.DataLoaded)
            {
                ApplyTimeFrameSettings();
                ResolveWindowPresets();   // window Setting popups -> per-window TP/SL/slope
                ValidateChart();
                maxAccountBalanceLimitReached = false;

                SetupTimeZones();

                pathRecorders = new List<PathRecorder>();
                pathLogHeaderWritten = false;
                pathLogFailureCount = 0;

                ema = EMA(EmaPeriod);
                atr = ATR(AtrPeriod);
                AddChartIndicator(ema);
            }
            else if (State == State.Realtime)
            {
                TransitionTrackedOrderReferencesToRealtime();
            }
            else if (State == State.Terminated)
            {
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
                londonZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
                tokyoZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

                Print(string.Format(
                    "EMAL: platform={0} | NY+London anchored boundaries active.",
                    platformZone == null ? "unknown" : platformZone.Id));
            }
            catch (Exception ex)
            {
                platformZone = null;
                easternZone = null;
                londonZone = null;
                tokyoZone = null;
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

        // 0 = Asia, 1 = Europe, 2 = US cash (10:30-17:00), 3 = US 09:20-09:50,
        // 5 = US 09:55-10:30, -1 = maintenance halt OR the 09:50-09:55 no-trade block.
        // All ET (NY) windows are checked before the London-anchored Europe band, which
        // caps Europe at 09:20 (its 09:20-09:30 tail is claimed by US 09:20-09:50).
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

            // Special US windows, checked before US proper AND before Europe.
            if (nyMinute >= Us0920StartMinute && nyMinute < Us0920EndMinute)
                return 3;

            if (nyMinute >= Us0955StartMinute && nyMinute < Us0955EndMinute)
                return 5;

            if (nyMinute >= UsStartMinute && nyMinute < UsEndMinute)
                return 2;

            // Overnight band. Europe begins on London's clock, not New York's, so this
            // boundary follows UK DST automatically.
            DateTime london = ConvertToZone(platformTime, londonZone);
            int londonMinute = london.Hour * 60 + london.Minute;

            if (londonMinute >= LondonOpenMinute && londonMinute < LondonBandEndMinute)
                return 1;

            return 0;
        }

        private static string SessionName(int index)
        {
            switch (index)
            {
                case 0: return "Asia";
                case 1: return "Europe";
                case 2: return "US";
                case 3: return "US 09:20-09:50";
                case 5: return "US 09:55-10:30";
                default: return "Halt";
            }
        }

        private bool IsSessionEnabled(int index)
        {
            switch (index)
            {
                case 0: return AsiaEnabled;
                case 1: return EuropeEnabled;
                case 2: return UsEnabled;
                case 3: return Us0920Enabled;
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
                case 1: return Math.Abs(EuropeMinimumSlope);
                case 2: return Math.Abs(UsMinimumSlope);
                case 3: return Math.Abs(Us0920MinimumSlope);
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
                case 3: return us0920Tp;
                case 5: return us0955Tp;
                default: return TakeProfitPoints;
            }
        }

        private double GetConfiguredStopLoss()
        {
            switch (GetSessionIndex(GetBarOpenRaw()))
            {
                case 3: return us0920Sl;
                case 5: return us0955Sl;
                default: return StopLossPoints;
            }
        }

        // Resolves each window's Setting popup into its TP / SL / slope. The slope is written
        // back into the per-window Us*MinimumSlope so GetConfiguredSlope keeps working unchanged.
        private void ResolveWindowPresets()
        {
            switch (Us0920Setting)
            {
                case EMALUs0920Setting.TP2_SL10_Slope2_75: us0920Tp = 2; us0920Sl = 10; Us0920MinimumSlope = 2.75; break;
                case EMALUs0920Setting.TP4_SL18_Slope2_75: us0920Tp = 4; us0920Sl = 18; Us0920MinimumSlope = 2.75; break;
                case EMALUs0920Setting.TP2_SL14_Slope3_0:  us0920Tp = 2; us0920Sl = 14; Us0920MinimumSlope = 3.0;  break;
                case EMALUs0920Setting.TP2_SL14_Slope2_75: us0920Tp = 2; us0920Sl = 14; Us0920MinimumSlope = 2.75; break;
                default: /* TP5_SL18_Slope2_75 */          us0920Tp = 5; us0920Sl = 18; Us0920MinimumSlope = 2.75; break;
            }
            switch (Us0955Setting)
            {
                case EMALUs0955Setting.TP3_SL16_Slope2_75: us0955Tp = 3; us0955Sl = 16; Us0955MinimumSlope = 2.75; break;
                case EMALUs0955Setting.TP3_SL18_Slope2_75: us0955Tp = 3; us0955Sl = 18; Us0955MinimumSlope = 2.75; break;
                case EMALUs0955Setting.TP2_SL18_Slope2_75: us0955Tp = 2; us0955Sl = 18; Us0955MinimumSlope = 2.75; break;
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
            DateTime barOpenLondon = ConvertToZone(barOpenRaw, londonZone);
            DateTime barOpenTokyo = ConvertToZone(barOpenRaw, tokyoZone);
            double slope = ema[1] - ema[2];
            double slopePrev = ema[2] - ema[3];
            double atrValue = atr[1];

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
                barOpenLondon.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                barOpenTokyo.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                barOpen.DayOfWeek.ToString(),
                (barOpen.Hour * 100 + barOpen.Minute).ToString(CultureInfo.InvariantCulture),
                (barOpenLondon.Hour * 100 + barOpenLondon.Minute).ToString(CultureInfo.InvariantCulture),
                (barOpenTokyo.Hour * 100 + barOpenTokyo.Minute).ToString(CultureInfo.InvariantCulture),
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
                N(atrValue > 0.0 ? Math.Abs(slope) / atrValue : 0.0),
                N(EntryOrderType == EMALEntryOrderType.Limit ? GetLimitOffsetPoints() : 0.0),
                N((signalPrice - ema[1]) * direction),
                N(atrValue),
                N(takeProfit),
                N(stopLoss),
                N(atrValue > 0.0 ? takeProfit / atrValue : 0.0),
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
            Print(string.Format("      Europe   03-0930   : {0}  slope {1}", EuropeEnabled, EuropeMinimumSlope));
            Print(string.Format("      US 0920-0950       : {0}  slope {1}", Us0920Enabled, Us0920MinimumSlope));
            Print(string.Format("      (block 0950-0955, no trade)"));
            Print(string.Format("      US 0955-1030       : {0}  slope {1}", Us0955Enabled, Us0955MinimumSlope));
            Print(string.Format("      US       1030-17   : {0}  slope {1}", UsEnabled, UsMinimumSlope));
            Print(string.Format("  bars blocked        : {0}  (session gate)", blockedBarCount));
            Print(string.Format("  bucket filter       : {0}  (bars blocked: {1})",
                UseBucketFilter, bucketBlockedBarCount));
            Print(string.Format("  time frame / parity : {0} / {1}  (bars blocked: {2})",
                TimeFrame, TradeParity, parityBlockedBarCount));
            Print(string.Format("  time stop           : {0}s  onlyWhenLosing={1} thresh={2}  (fired: {3})",
                TimeStopSeconds, TimeStopOnlyWhenLosing, TimeStopLossPoints, timeStopCount));
            Print(string.Format("  news blackout       : {0}  {1:0000}-{2:0000}  (bars blocked: {3})",
                BlockNewsWindow, NewsBlockStartTime, NewsBlockEndTime, newsBlockedBarCount));
            Print(string.Format("  news flatten        : {0}  (positions closed: {1})",
                FlattenAtNewsBlock, newsFlattenCount));
            Print(string.Format("  timeout / moved     : {0}s / {1} pts (0 = off)",
                LimitOrderTimeoutSeconds, CancelIfMovedPoints));
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

            if (IsNewsBlackout(ConvertToEastern(raw)))
                return "news blackout";

            if (UsePerSessionSettings)
            {
                int s = GetSessionIndex(raw);

                if (s < 0 || !IsSessionEnabled(s))
                    return "session gate";
            }

            // Mirror IsEntryWindowOpen's bucket gate: an out-of-list 30-minute bucket
            // blocks entry on time alone. Without this the panel read "allow" during a
            // bucket-blocked window even though no trade could fire.
            if (!IsBucketAllowed(ConvertToEastern(raw)))
                return "time block";

            // Mirror IsEntryWindowOpen's even/odd candle filter.
            if (!IsParityAllowed(ConvertToEastern(raw)))
                return TradeParity == EMALTradeParity.Even ? "odd bar (want even)" : "even bar (want odd)";

            if (dailyProfitLimitReached) return "daily profit cap";
            if (dailyLossLimitReached) return "daily loss cap";
            if (maxAccountBalanceLimitReached) return "balance cap";

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

            if (CurrentBar < Math.Max(Math.Max(EmaPeriod, AtrPeriod), 20) + 2)
                return "Warmup in progress";

            // Limit entries need ticks; if none have ever arrived in real time the strategy
            // silently never fills. Surfaces the "enable Tick Replay" cause.
            if (State == State.Realtime && EntryOrderType == EMALEntryOrderType.Limit && !sawMarketData)
                return "ERROR: no ticks (enable Tick Replay)";

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

        private bool IsEntryWindowOpen()
        {
            DateTime barOpenRaw = GetBarOpenRaw();
            DateTime barOpen = ConvertToEastern(barOpenRaw);

            // News blackout applies on every day, independent of the session gate.
            if (IsNewsBlackout(barOpen))
            {
                newsBlockedBarCount++;
                return false;
            }

            if (UsePerSessionSettings)
            {
                int session = GetSessionIndex(barOpenRaw);

                if (session < 0 || !IsSessionEnabled(session))
                    return false;
            }

            if (!IsBucketAllowed(barOpen))
            {
                bucketBlockedBarCount++;
                return false;
            }

            if (!IsParityAllowed(barOpen))
            {
                parityBlockedBarCount++;
                return false;
            }

            return true;
        }

        // Even/Odd candle filter. Candles are indexed from the top of the hour by the
        // Time Frame length: index = minute-of-hour / bars (1/5/15). Even = index 0,2,4...
        // Odd = index 1,3,5... Both disables the filter. 60 is divisible by 1/5/15, so the
        // pattern resets cleanly at every hour. Minute-of-hour is timezone-invariant across
        // whole-hour offsets, but eastern bar-open is passed for consistency with the gates.
        private bool IsParityAllowed(DateTime barOpenEastern)
        {
            if (TradeParity == EMALTradeParity.Both)
                return true;

            int index = barOpenEastern.Minute / ExpectedBarMinutes();
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
            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            int quantity = Position.Quantity;

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(quantity, NewsExitSignal, LongEntrySignal);
            else
                ExitShort(quantity, NewsExitSignal, ShortEntrySignal);

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
            if (IsAccountBalanceBlocked())
            {
                // v16 fix: still draw the info panel (showing "balance cap") before bailing.
                // Previously this early return skipped UpdateInfoText() below, so setting a
                // non-zero Max Account Balance made the panel vanish entirely.
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
            if (CurrentBar < Math.Max(Math.Max(EmaPeriod, AtrPeriod), 20) + 2)
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
                "{0} | {1} {2}{3} | target={4:F2} pts stop={5:F2} pts | atr={6:F2}",
                Time[0],
                direction > 0 ? "LONG" : "SHORT",
                EntryOrderType,
                EntryOrderType == EMALEntryOrderType.Limit
                    ? string.Format("@{0}={1:F2}", LimitPriceReference, limitPrice)
                    : string.Empty,
                takeProfit,
                stopLoss,
                atr[1]));

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
                case 1: return EuropeLimitOffset;
                case 2: return UsLimitOffset;
                case 3: return Us0920LimitOffset;
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
        // Minutes-per-bar the selected Time Frame expects. The strategy trades the chart's
        // own bar series (no internal AddDataSeries), so the chart period must match.
        private int ExpectedBarMinutes()
        {
            switch (TimeFrame)
            {
                case EMALTimeFrame.M5: return 5;
                case EMALTimeFrame.M15: return 15;
                default: return 1;
            }
        }

        // Loads the internally-managed trading parameters for the selected Time Frame.
        // All of these are hidden from the user; the Time Frame popup is the only control.
        //
        // M1 is the tuned live config (EMA 9 / Asia 3.0 / Europe 0.5 / US 2.75 / TP 4 / SL 18,
        // Tuning Brief line 414). M5 and M15 are NOT yet tuned - they reuse the M1 values as
        // placeholders. TODO: replace the M5/M15 blocks once the higher-timeframe sweeps are done.
        private void ApplyTimeFrameSettings()
        {
            switch (TimeFrame)
            {
                case EMALTimeFrame.M5:
                    // TODO(5m tuning): placeholder = M1 tuned set.
                    EmaPeriod = 9;
                    AsiaMinimumSlope = 3.0;
                    EuropeMinimumSlope = 0.5;
                    UsMinimumSlope = 2.75;
                    Us0920MinimumSlope = 2.75;   // seeded from US; tune separately
                    Us0955MinimumSlope = 2.75;   // seeded from US; tune separately
                    TakeProfitPoints = 4.0;
                    StopLossPoints = 18.0;
                    break;

                case EMALTimeFrame.M15:
                    // TODO(15m tuning): placeholder = M1 tuned set.
                    EmaPeriod = 9;
                    AsiaMinimumSlope = 3.0;
                    EuropeMinimumSlope = 0.5;
                    UsMinimumSlope = 2.75;
                    Us0920MinimumSlope = 2.75;   // seeded from US; tune separately
                    Us0955MinimumSlope = 2.75;   // seeded from US; tune separately
                    TakeProfitPoints = 4.0;
                    StopLossPoints = 18.0;
                    break;

                default: // M1 - tuned
                    EmaPeriod = 9;
                    AsiaMinimumSlope = 3.0;
                    EuropeMinimumSlope = 0.5;
                    UsMinimumSlope = 2.75;
                    Us0920MinimumSlope = 2.75;   // seeded from US; tune separately
                    Us0955MinimumSlope = 2.75;   // seeded from US; tune separately
                    TakeProfitPoints = 4.0;
                    StopLossPoints = 18.0;
                    break;
            }
        }

        private void ValidateChart()
        {
            configurationBlocked = false;
            configurationBlockReason = string.Empty;

            int expected = ExpectedBarMinutes();
            if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != expected)
            {
                configurationBlocked = true;
                configurationBlockReason = string.Format("needs a {0}-min chart (is {1} {2})",
                    expected, BarsPeriod.Value, BarsPeriod.BarsPeriodType);
                Print("EMAL DISABLED: Time Frame " + TimeFrame + " requires a " + expected
                    + "-minute chart. Current series is "
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

                return;
            }

            if (IsTerminalExitOrderName(orderName))
            {
                if (orderState == OrderState.Rejected)
                {
                    Print(string.Format(
                        "{0} | CRITICAL: terminal exit {1} rejected | error={2} comment={3}",
                        time,
                        orderName,
                        error,
                        comment ?? string.Empty));
                }

                return;
            }

            if (orderName == NewsExitSignal)
            {
                if (orderState == OrderState.Rejected)
                {
                    Print(string.Format(
                        "{0} | news flatten rejected | error={1} comment={2} | retrying emergency exit",
                        time,
                        error,
                        comment ?? string.Empty));
                    TrySubmitTerminalExit("NewsReject", order.FromEntrySignal);
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
                TrySubmitQueuedEntry();
            }
            else if (orderState == OrderState.Rejected)
            {
                entryOrder = null;
                ClearActiveEntryContext();
                ClearQueuedEntry();
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

                if (maxAccountBalanceLimitReached)
                {
                    TrySubmitTerminalExit("MaxAccountBalance", orderName);
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

                RecordRealizedPoints(price, quantity, positionIsFlat);

                if (EnableFeatureLog && positionIsFlat)
                    CaptureExitAndWrite(price, time, orderName);

                CancelRemainingEntryAfterExit();

                if (positionIsFlat)
                    ResetProtectionTracking();
            }
        }

        private void SubmitOrUpdateProtection(MarketPosition positionDirection, double averageEntryPrice,
            int protectedQuantity, DateTime time)
        {
            if (terminalExitPending
                || positionDirection == MarketPosition.Flat
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
                ChangeOrder(protectiveStopOrder, protectedQuantity, 0.0, stopPrice);
            }
            else
            {
                protectiveStopOrder = positionDirection == MarketPosition.Long
                    ? ExitLongStopMarket(0, true, protectedQuantity, stopPrice, StopExitSignal, protectedEntrySignal)
                    : ExitShortStopMarket(0, true, protectedQuantity, stopPrice, StopExitSignal, protectedEntrySignal);
            }

            // A rejection can callback synchronously from the submission above. Do not submit
            // its OCO sibling with an identifier NinjaTrader has already retired.
            if (terminalExitPending
                || Position.MarketPosition == MarketPosition.Flat)
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

                ChangeOrder(
                    profitTargetOrder,
                    desiredProtectionQuantity,
                    desiredProtectionTargetPrice,
                    0.0);
            }
            else
            {
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
            if (terminalExitPending)
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

            if (positionDirection == MarketPosition.Long)
                ExitLong(exitSignal, fromEntrySignal);
            else
                ExitShort(exitSignal, fromEntrySignal);
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
            CancelOrder(entryOrder);
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
        }

        [NinjaScriptProperty]
        [Display(Name = "Time Frame", Description = "Candle timeframe the strategy is tuned for. Apply the strategy to a chart of the MATCHING bar period: M1 = 1-minute, M5 = 5-minute, M15 = 15-minute. Selecting a timeframe loads its internally-managed parameter set. M5 and M15 are NOT yet tuned - they currently reuse the 1-minute values as placeholders.", GroupName = "Time Frame", Order = 0)]
        public EMALTimeFrame TimeFrame { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Parity", Description = "Reduce trade count by trading only alternate candles. Candles are indexed from the top of the hour by the Time Frame length (index = minute-of-hour / bars). Even = index 0,2,4... (e.g. on 5m: :00 :10 :20 :30); Odd = index 1,3,5... (e.g. on 5m: :05 :15 :25); Both = every candle (current behaviour). On 1m this is simply even vs odd minute.", GroupName = "Time Frame", Order = 1)]
        public EMALTradeParity TradeParity { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Account Balance", Description = "When account net liquidation, including unrealized P&L, reaches this value, pending entries are cancelled, open positions are flattened, and new entries remain blocked. 0 disables.", GroupName = "Risk", Order = 0)]
        public double MaxAccountBalance { get; set; }

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
        [Display(Name = "Europe Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 4)]
        public double EuropeLimitOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 5)]
        public double UsLimitOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:20-09:50 Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 6)]
        public double Us0920LimitOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:55-10:30 Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 7)]
        public double Us0955LimitOffset { get; set; }


        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ATR Period", Description = "ATR period used when Take Profit Mode is Atr.", GroupName = "Parameters", Order = 7)]
        public int AtrPeriod { get; set; }










        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Limit Timeout (seconds)", Description = "Cancel an unfilled limit entry after this many seconds. 0 disables; the order then lives until the next bar opens.", GroupName = "Limit Entry Cancellation", Order = 0)]
        public double LimitOrderTimeoutSeconds { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Cancel If Moved (points)", Description = "Cancel an unfilled limit entry once price has travelled this far in the signal direction without us. 0 disables.", GroupName = "Limit Entry Cancellation", Order = 1)]
        public double CancelIfMovedPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Per-Session Settings", Description = "Enable the per-session split (Asia 18:30-03:00, Europe 03:00-09:20, US 09:20-09:50, US 09:55-10:30, US 10:30-17:00). When off, the global Minimum EMA Slope applies to every hour.", GroupName = "Sessions", Order = 0)]
        public bool UsePerSessionSettings { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Bucket Filter", Description = "Restrict entries to the 33 approved 30-minute windows derived from 78 sessions of playback. The window list is hardcoded and not user-editable. Risk-reduction setting: it lowers drawdown and also lowers gross profit.", GroupName = "Sessions", Order = 10)]
        public bool UseBucketFilter { get; set; }




        [NinjaScriptProperty]
        [Display(Name = "Asia 18:30-03:00 Enabled", GroupName = "Sessions", Order = 1)]
        public bool AsiaEnabled { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Asia Min Slope", Description = "Slope threshold for the Asia session.", GroupName = "Sessions", Order = 2)]
        public double AsiaMinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Europe 03:00-09:20 Enabled", GroupName = "Sessions", Order = 3)]
        public bool EuropeEnabled { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Europe Min Slope", Description = "Slope threshold for the Europe session.", GroupName = "Sessions", Order = 4)]
        public double EuropeMinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 10:30-17:00 Enabled", GroupName = "Sessions", Order = 5)]
        public bool UsEnabled { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US Min Slope", Description = "Slope threshold for the US cash session (10:30-17:00).", GroupName = "Sessions", Order = 6)]
        public double UsMinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 09:20-09:50 Enabled", GroupName = "Sessions", Order = 7)]
        public bool Us0920Enabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 09:20-09:50 Setting", Description = "Preset TP/SL/slope for the US 09:20-09:50 window. Member name reads TP_SL_Slope (e.g. TP5_SL18_Slope2_75 = TP 5, SL 18, slope 2.75).", GroupName = "Sessions", Order = 8)]
        public EMALUs0920Setting Us0920Setting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:20-09:50 Min Slope", Description = "Driven by the US 09:20-09:50 Setting preset; not user-editable.", GroupName = "Sessions", Order = 21)]
        public double Us0920MinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 09:55-10:30 Enabled", GroupName = "Sessions", Order = 9)]
        public bool Us0955Enabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 09:55-10:30 Setting", Description = "Preset TP/SL/slope for the US 09:55-10:30 window. Member name reads TP_SL_Slope (e.g. TP4_SL18_Slope2_75 = TP 4, SL 18, slope 2.75).", GroupName = "Sessions", Order = 10)]
        public EMALUs0955Setting Us0955Setting { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "US 09:55-10:30 Min Slope", Description = "Driven by the US 09:55-10:30 Setting preset; not user-editable.", GroupName = "Sessions", Order = 22)]
        public double Us0955MinimumSlope { get; set; }














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
        [Display(Name = "Logging", Description = "Write one CSV row per completed trade containing the entry context: slope, ATR, TP/ATR, the previous three bars OHLCV, fill delay and outcome. Turn off to disable all file writing.", GroupName = "Logging", Order = 0)]
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

    public enum EMALTimeFrame
    {
        M1,
        M5,
        M15
    }

    public enum EMALTradeParity
    {
        Both,
        Even,
        Odd
    }

    // Per-window bracket presets (Steve, 2026-07-30). Member names read TP / SL / Slope
    // (e.g. TP2_SL10_Slope2_75 = TP 2, SL 10, slope 2.75). NinjaTrader shows the member name.
    public enum EMALUs0920Setting
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
        TP4_SL18_Slope2_75
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
