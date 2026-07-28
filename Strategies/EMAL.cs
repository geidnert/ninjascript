#region Using declarations
using System;
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

        // Session boundaries in minutes-of-day, New York time. Asia wraps midnight.
        // 17:00-18:30 is the maintenance/session-gate halt and belongs to no session.
        // NY-anchored boundaries. Globex reopen and the US cash session never drift, because
        // CME (Chicago) and New York share the same DST dates.
        private const int AsiaStartMinute = 18 * 60 + 30;   // 18:30 ET
        private const int UsStartMinute = 9 * 60 + 30;  // 09:30 ET, cash open
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
                Description = "One-minute EMA direction strategy with market or passive bid/ask limit entries. "
                    + "Take-profit distance is fixed, ATR-scaled, or EMA-slope-scaled.";
                Name = "EMAL";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                BarsRequiredToTrade = 1;

                EmaPeriod = 6;
                MinimumEmaSlopePoints = 0.75;
                TakeProfitPoints = 4.0;
                StopLossPoints = 13.0;
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
                MaxAccountBalance = 0.0;

                // Daily caps in POINTS of realised P&L. 0 = disabled.
                MaxDailyProfitPoints = 0.0;
                MaxDailyLossPoints = 0.0;

                // Dynamic take-profit, OFF by default. A multiplier of 0 disables scaling and
                // falls back to the fixed TakeProfitPoints, so defaults reproduce the original
                // behaviour exactly regardless of TakeProfitMode.
                TakeProfitMode = EMALTakeProfitMode.Fixed;
                AtrPeriod = 14;
                AtrTakeProfitMultiplier = 0.0;
                SlopeTakeProfitMultiplier = 0.0;
                MinimumTakeProfitPoints = 1.0;
                MaximumTakeProfitPoints = 30.0;

                // Adaptive stop, OFF by default. Multiplier 0 disables scaling and falls back
                // to the fixed StopLossPoints, so defaults reproduce current behaviour.
                StopLossMode = EMALStopLossMode.Fixed;
                StopAtrMultiplier = 0.0;
                StopTpMultiplier = 0.0;
                MinimumStopLossPoints = 2.0;
                MaximumStopLossPoints = 60.0;

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
                // Points reproduces current behaviour exactly. AtrRatio reinterprets the same
                // three slope values as multiples of ATR instead of absolute points.
                SlopeThresholdMode = EMALSlopeThresholdMode.Points;
                MinimumSlopePoints = 0.10;
                MaximumSlopePoints = 25.0;

                AsiaMinimumSlope = 0.75;
                EuropeMinimumSlope = 0.75;
                UsMinimumSlope = 0.75;


                // Blackout around the 08:30 US data release. HHmm, inclusive both ends.
                BlockNewsWindow = true;
                FlattenAtNewsBlock = true;
                NewsBlockStartTime = 828;
                NewsBlockEndTime = 832;

                ShowInfoPanel = true;
                EnableFeatureLog = true;
                FeatureLogPath = @"C:\Users\Administrator\Documents\EMAL_features.csv";
            }
            else if (State == State.DataLoaded)
            {
                ValidateChart();
                maxAccountBalanceLimitReached = false;

                SetupTimeZones();

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

        // 0 = Asia, 1 = Europe, 2 = US cash, -1 = maintenance halt (no session).
        // Takes the RAW platform time so each boundary can be judged in its own zone.
        private int GetSessionIndex(DateTime platformTime)
        {
            DateTime ny = ConvertToZone(platformTime, easternZone);
            int nyMinute = ny.Hour * 60 + ny.Minute;

            // 17:00-18:30 ET is the maintenance/session-gate halt.
            if (nyMinute >= UsEndMinute && nyMinute < AsiaStartMinute)
                return -1;

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
                default: return Math.Abs(MinimumEmaSlopePoints);
            }
        }

        // The entry gate, in whichever units SlopeThresholdMode selects.
        //
        // Points  - the configured value is an absolute points-per-minute threshold. When
        //           volatility rises, EMA slope rises with it, so a fixed threshold becomes
        //           easier to clear and trade count inflates in exactly the regimes where the
        //           marginal setups are weakest.
        // AtrRatio- the configured value is a RATIO of ATR. The effective points threshold
        //           scales with volatility, holding selectivity roughly constant across
        //           regimes and stabilising trade count.
        //
        // Measured on the Apr-Jul 2026 sample: Test-half volatility ran 1.34x Train, and the
        // fixed gate admitted 47% more trades per day (287 -> 421) at 39% lower gross per
        // trade. slope/ATR is the direct fix for that mechanism.
        private double GetRequiredSlope(DateTime platformTime)
        {
            double configured = GetConfiguredSlope(platformTime);

            if (SlopeThresholdMode != EMALSlopeThresholdMode.AtrRatio)
                return configured;

            double atrValue = atr[1];

            // A non-positive or unformed ATR would disable the gate entirely; fall back.
            if (atrValue <= 0.0 || double.IsNaN(atrValue))
                return configured;

            double points = configured * atrValue;
            double floor = Math.Min(MinimumSlopePoints, MaximumSlopePoints);
            double ceiling = Math.Max(MinimumSlopePoints, MaximumSlopePoints);

            return Math.Min(ceiling, Math.Max(floor, points));
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
            Print(string.Format("  per-session         : {0}   slope mode: {1}",
                UsePerSessionSettings, SlopeThresholdMode));
            Print(string.Format("      Asia   1830-03  : {0}  slope {1}", AsiaEnabled, AsiaMinimumSlope));
            Print(string.Format("      Europe 03-0930  : {0}  slope {1}", EuropeEnabled, EuropeMinimumSlope));
            Print(string.Format("      US     0930-17  : {0}  slope {1}", UsEnabled, UsMinimumSlope));
            Print(string.Format("  bars blocked        : {0}  (session gate)", blockedBarCount));
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
            EvaluateEntryCancellation();
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

            if (dailyProfitLimitReached) return "daily profit cap";
            if (dailyLossLimitReached) return "daily loss cap";
            if (maxAccountBalanceLimitReached) return "balance cap";

            return "allow";
        }

        private List<KeyValuePair<string, string>> BuildInfoLines()
        {
            DateTime raw = GetBarOpenRaw();
            int session = GetSessionIndex(raw);
            string instrument = Instrument != null && Instrument.MasterInstrument != null
                ? Instrument.FullName
                : "-";

            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(
                    string.Format("EMAL v{0}", GetAddOnVersion()),
                    string.Empty),
                new KeyValuePair<string, string>("Contracts:", Contracts.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Contract:", instrument),
                new KeyValuePair<string, string>("Slope:", GetRequiredSlope(raw).ToString("0.##", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("EMA:", EmaPeriod.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("TP:", TakeProfitPoints.ToString("0.##", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("SL:", StopLossPoints.ToString("0.##", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Trade:", GetTradeGateState()),
                new KeyValuePair<string, string>("Session:", SessionName(session)),
                new KeyValuePair<string, string>(InfoFooter, string.Empty)
            };
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

            return true;
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

            // Wrong chart period or instrument: stay loaded, submit nothing.
            if (configurationBlocked)
                return;

            // Evaluate on every tick so unrealized profit can flatten an open position
            // immediately instead of waiting for the next one-minute bar.
            if (IsAccountBalanceBlocked())
                return;

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

            // Snapshot the target at signal time so a requeued entry cannot pick up a
            // different ATR/slope reading than the bar that generated the signal.
            queuedTakeProfitPoints = ComputeTakeProfitPoints(completedEmaSlope);
            queuedStopLossPoints = ComputeStopLossPoints(queuedTakeProfitPoints);

            // Reference for the "price ran without us" rule: where price was when the
            // signal fired, not where the limit was placed.
            queuedSignalPrice = Close[0];

            if (EntryOrderType == EMALEntryOrderType.Limit)
                queuedLimitPrice = GetPassiveLimitPrice(direction);
        }

        private double ComputeTakeProfitPoints(double completedEmaSlope)
        {
            double points;

            switch (TakeProfitMode)
            {
                // A multiplier of zero (or less) means "disabled" -> use the fixed distance.
                case EMALTakeProfitMode.Atr:
                    if (AtrTakeProfitMultiplier <= 0.0)
                        return TakeProfitPoints;
                    points = AtrTakeProfitMultiplier * atr[1];
                    break;

                case EMALTakeProfitMode.Slope:
                    if (SlopeTakeProfitMultiplier <= 0.0)
                        return TakeProfitPoints;
                    points = SlopeTakeProfitMultiplier * Math.Abs(completedEmaSlope);
                    break;

                default:
                    return TakeProfitPoints;
            }

            // Clamp so an extreme ATR/slope reading cannot produce an absurd target.
            double floor = Math.Min(MinimumTakeProfitPoints, MaximumTakeProfitPoints);
            double ceiling = Math.Max(MinimumTakeProfitPoints, MaximumTakeProfitPoints);

            return Math.Min(ceiling, Math.Max(floor, points));
        }

        // Adaptive stop distance. Same convention as the take-profit modes: a multiplier of
        // zero disables scaling and falls back to the fixed StopLossPoints, so the defaults
        // reproduce current behaviour regardless of which mode is selected.
        //
        // TpMultiple is the mode that pins risk:reward, and therefore the breakeven win rate,
        // permanently. If the target is already ATR-scaled the stop inherits that adaptation
        // rather than scaling the same quantity twice.
        private double ComputeStopLossPoints(double takeProfitPoints)
        {
            double points;

            switch (StopLossMode)
            {
                case EMALStopLossMode.Atr:
                    if (StopAtrMultiplier <= 0.0)
                        return StopLossPoints;
                    points = StopAtrMultiplier * atr[1];
                    break;

                case EMALStopLossMode.TpMultiple:
                    if (StopTpMultiplier <= 0.0)
                        return StopLossPoints;
                    points = StopTpMultiplier * takeProfitPoints;
                    break;

                default:
                    return StopLossPoints;
            }

            double floor = Math.Min(MinimumStopLossPoints, MaximumStopLossPoints);
            double ceiling = Math.Max(MinimumStopLossPoints, MaximumStopLossPoints);

            return Math.Min(ceiling, Math.Max(floor, points));
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
                "{0} | {1} {2}{3} | mode={4} | target={5:F2} pts stop={6:F2} pts | atr={7:F2}",
                Time[0],
                direction > 0 ? "LONG" : "SHORT",
                EntryOrderType,
                EntryOrderType == EMALEntryOrderType.Limit
                    ? string.Format("@{0}={1:F2}", LimitPriceReference, limitPrice)
                    : string.Empty,
                TakeProfitMode,
                takeProfit,
                StopLossPoints,
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
        private void ValidateChart()
        {
            configurationBlocked = false;

            if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 1)
            {
                configurationBlocked = true;
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
                    BeginProtectionTracking(orderName, TakeProfitPoints, StopLossPoints, 0.0);

                entryFillValue += price * executionQuantity;
                entryFilledQuantity += executionQuantity;
                openEntryPrice = entryFillValue / entryFilledQuantity;
                openEntryDirection = orderName == LongEntrySignal ? 1 : -1;

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

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Account Balance", Description = "When account net liquidation, including unrealized P&L, reaches this value, pending entries are cancelled, open positions are flattened, and new entries remain blocked. 0 disables.", GroupName = "Risk", Order = 0)]
        public double MaxAccountBalance { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Daily Profit (points)", Description = "Once realised profit for the trading day reaches this many points, no further entries are taken until the next day. Any open position is left to its stop and target. Resets at 18:00 ET (CME trading day). 0 disables.", GroupName = "Risk", Order = 1)]
        public double MaxDailyProfitPoints { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Daily Loss (points)", Description = "Enter as a POSITIVE number. Once realised loss for the trading day reaches this many points, no further entries are taken until the next day. Any open position is left to its stop and target. Resets at 18:00 ET. 0 disables.", GroupName = "Risk", Order = 2)]
        public double MaxDailyLossPoints { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "EMA Period", Description = "EMA period evaluated on the one-minute chart.", GroupName = "Parameters", Order = 0)]
        public int EmaPeriod { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Minimum EMA Slope (points/minute)", Description = "Minimum completed-bar EMA change required in the trade direction.", GroupName = "Parameters", Order = 1)]
        public double MinimumEmaSlopePoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Take Profit (points)", Description = "Profit-target distance from the actual fill.", GroupName = "Parameters", Order = 2)]
        public double TakeProfitPoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Stop Loss (points)", Description = "Stop-loss distance from the actual fill.", GroupName = "Parameters", Order = 3)]
        public double StopLossPoints { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Contracts", Description = "Number of contracts per entry.", GroupName = "Parameters", Order = 4)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Order Type", Description = "Market, or limit at the price set by Limit Price Reference.", GroupName = "Parameters", Order = 5)]
        public EMALEntryOrderType EntryOrderType { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Limit Price Reference", Description = "Where a limit entry is placed. BidAsk = passive, bid for longs and ask for shorts. Open = this bar's open. Close = previous bar's close. Ignored when Entry Order Type is Market.", GroupName = "Parameters", Order = 6)]
        public EMALLimitPriceReference LimitPriceReference { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bracket Anchor", Description = "Fill: stop and target are measured from the actual fill (original behaviour). Reference: measured from the UNOFFSET limit price, so an offset entry keeps the exact barrier prices the un-offset trade would have had - identical outcome, offset is pure gain. Only matters when Limit Offset is non-zero.", GroupName = "Limit Offset", Order = 0)]
        public EMALBracketAnchor BracketAnchor { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Limit Offset Mode", Description = "Global uses one offset everywhere. PerSession uses the three session values below. Start with Global.", GroupName = "Limit Offset", Order = 1)]
        public EMALLimitOffsetMode LimitOffsetMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Limit Offset (points)", Description = "Shifts the limit entry in your favour: below the reference for longs, above for shorts. Positive = more passive, better price, fewer fills. Negative chases the move. 0 = off.", GroupName = "Limit Offset", Order = 2)]
        public double LimitOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 3)]
        public double AsiaLimitOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Europe Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 4)]
        public double EuropeLimitOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US Limit Offset", Description = "Used only when Limit Offset Mode is PerSession.", GroupName = "Limit Offset", Order = 5)]
        public double UsLimitOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit Mode", Description = "Fixed uses Take Profit (points). Atr and Slope scale the target; each is disabled when its multiplier is 0.", GroupName = "Take Profit Scaling", Order = 0)]
        public EMALTakeProfitMode TakeProfitMode { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR Period", Description = "ATR period used when Take Profit Mode is Atr.", GroupName = "Take Profit Scaling", Order = 1)]
        public int AtrPeriod { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR TP Multiplier", Description = "Target = multiplier x ATR. 0 disables scaling and uses the fixed Take Profit instead.", GroupName = "Take Profit Scaling", Order = 2)]
        public double AtrTakeProfitMultiplier { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Slope TP Multiplier", Description = "Target = multiplier x |EMA slope|. 0 disables scaling and uses the fixed Take Profit instead.", GroupName = "Take Profit Scaling", Order = 3)]
        public double SlopeTakeProfitMultiplier { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Min Take Profit (points)", Description = "Floor applied to a scaled target.", GroupName = "Take Profit Scaling", Order = 4)]
        public double MinimumTakeProfitPoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Take Profit (points)", Description = "Ceiling applied to a scaled target.", GroupName = "Take Profit Scaling", Order = 5)]
        public double MaximumTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Mode", Description = "Fixed uses Stop Loss (points). Atr scales by ATR. TpMultiple sets the stop as a multiple of the actual take profit, which pins risk:reward and therefore the breakeven win rate. Each is disabled when its multiplier is 0.", GroupName = "Stop Loss Scaling", Order = 0)]
        public EMALStopLossMode StopLossMode { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Stop ATR Multiplier", Description = "Stop = multiplier x ATR. 0 disables scaling and uses the fixed Stop Loss instead.", GroupName = "Stop Loss Scaling", Order = 1)]
        public double StopAtrMultiplier { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Stop TP Multiplier", Description = "Stop = multiplier x the actual take profit. Breakeven win rate becomes k/(1+k), independent of volatility. 0 disables scaling.", GroupName = "Stop Loss Scaling", Order = 2)]
        public double StopTpMultiplier { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Min Stop Loss (points)", Description = "Floor applied to a scaled stop. A guardrail, not a tuning knob.", GroupName = "Stop Loss Scaling", Order = 3)]
        public double MinimumStopLossPoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Stop Loss (points)", Description = "Ceiling applied to a scaled stop, so an ATR spike cannot produce an absurd risk. A guardrail, not a tuning knob.", GroupName = "Stop Loss Scaling", Order = 4)]
        public double MaximumStopLossPoints { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Limit Timeout (seconds)", Description = "Cancel an unfilled limit entry after this many seconds. 0 disables; the order then lives until the next bar opens.", GroupName = "Limit Entry Cancellation", Order = 0)]
        public double LimitOrderTimeoutSeconds { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Cancel If Moved (points)", Description = "Cancel an unfilled limit entry once price has travelled this far in the signal direction without us. 0 disables.", GroupName = "Limit Entry Cancellation", Order = 1)]
        public double CancelIfMovedPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Per-Session Settings", Description = "Enable the three-session split (Asia 18:30-03:00, Europe 03:00-09:30, US 09:30-17:00). When off, the global Minimum EMA Slope applies to every hour.", GroupName = "Sessions", Order = 0)]
        public bool UsePerSessionSettings { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Slope Threshold Mode", Description = "Points: slope values are absolute points/minute (current behaviour). AtrRatio: the same values become multiples of ATR, so the gate tightens automatically when volatility rises and trade count stays stable across regimes.", GroupName = "Sessions", Order = 7)]
        public EMALSlopeThresholdMode SlopeThresholdMode { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Min Slope (points)", Description = "Floor on the effective points threshold when Slope Threshold Mode is AtrRatio. Guardrail only.", GroupName = "Sessions", Order = 8)]
        public double MinimumSlopePoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Max Slope (points)", Description = "Ceiling on the effective points threshold when Slope Threshold Mode is AtrRatio, so an ATR spike cannot switch the strategy off entirely. Guardrail only.", GroupName = "Sessions", Order = 9)]
        public double MaximumSlopePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia 18:30-03:00 Enabled", GroupName = "Sessions", Order = 1)]
        public bool AsiaEnabled { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Asia Min Slope", Description = "Slope threshold for the Asia session.", GroupName = "Sessions", Order = 2)]
        public double AsiaMinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Europe 03:00-09:30 Enabled", GroupName = "Sessions", Order = 3)]
        public bool EuropeEnabled { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Europe Min Slope", Description = "Slope threshold for the Europe session.", GroupName = "Sessions", Order = 4)]
        public double EuropeMinimumSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "US 09:30-17:00 Enabled", GroupName = "Sessions", Order = 5)]
        public bool UsEnabled { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "US Min Slope", Description = "Slope threshold for the US cash session.", GroupName = "Sessions", Order = 6)]
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
        [Display(Name = "Show Info Panel", Description = "Draw the strategy status panel in the lower-left of the chart.", GroupName = "Logging", Order = 2)]
        public bool ShowInfoPanel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Logging", Description = "Write one CSV row per completed trade containing the entry context: slope, ATR, TP/ATR, the previous three bars OHLCV, fill delay and outcome. Turn off to disable all file writing.", GroupName = "Logging", Order = 0)]
        public bool EnableFeatureLog { get; set; }

        [Display(Name = "Log File Path", Description = "Full path to the CSV. Leave blank to auto-name a timestamped file in Documents. Appends if the file already exists. Ignored when Logging is off.", GroupName = "Logging", Order = 1)]
        public string FeatureLogPath { get; set; }
    }

    public enum EMALEntryOrderType
    {
        Market,
        Limit
    }

    public enum EMALTakeProfitMode
    {
        Fixed,
        Atr,
        Slope
    }

    public enum EMALSlopeThresholdMode
    {
        Points,
        AtrRatio
    }

    public enum EMALStopLossMode
    {
        Fixed,
        Atr,
        TpMultiple
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
