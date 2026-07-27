#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
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

        private EMA ema;
        private ATR atr;
        private Order entryOrder;
        private Order protectiveStopOrder;
        private Order profitTargetOrder;
        private int queuedDirection;
        private double queuedLimitPrice;
        private double queuedTakeProfitPoints;
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
        private double entryFillValue;
        private int entryFilledQuantity;
        private double desiredProtectionTargetPrice;
        private int desiredProtectionQuantity;
        private bool terminalExitPending;

        // Account-level profit guard. Once net liquidation reaches the configured
        // ceiling, the latch remains set for the lifetime of this strategy instance.
        private bool maxAccountBalanceLimitReached;

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
        private int pendingDirection;
        private int pendingEntryBar;
        private int loggedRowCount;

        // Session boundaries in minutes-of-day, New York time. Asia wraps midnight.
        // 17:00-18:00 is the CME maintenance halt and belongs to no session.
        // NY-anchored boundaries. Globex reopen and the US cash session never drift, because
        // CME (Chicago) and New York share the same DST dates.
        private const int AsiaStartMinute = 18 * 60;    // 18:00 ET, Globex reopen
        private const int UsStartMinute = 9 * 60 + 30;  // 09:30 ET, cash open
        private const int UsEndMinute = 17 * 60;        // 17:00 ET, cash close

        // London-anchored boundary. Europe begins at 08:00 London, which tracks UK DST and so
        // lands on 03:00 ET most of the year and 04:00 ET during the ~4 misaligned weeks.
        // Inside the overnight band (NY 18:00 -> 09:30) London time only ever spans roughly
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
            + "Session,Direction,SignalPrice,Ema,Slope,SlopePrev,SlopeAccel,"
            + "DistToEma,Atr,TpPoints,SlPoints,TpOverAtr,"
            + "Bar1Open,Bar1High,Bar1Low,Bar1Close,Bar1Volume,"
            + "Bar2Open,Bar2High,Bar2Low,Bar2Close,Bar2Volume,"
            + "Bar3Open,Bar3High,Bar3Low,Bar3Close,Bar3Volume,"
            + "AvgVolume20,FillPrice,FillDelaySec,"
            + "ExitTime,ExitPrice,ExitReason,ProfitPoints,IsWin,BarsHeld";

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
                MaxAccountBalance = 0.0;

                // Dynamic take-profit, OFF by default. A multiplier of 0 disables scaling and
                // falls back to the fixed TakeProfitPoints, so defaults reproduce the original
                // behaviour exactly regardless of TakeProfitMode.
                TakeProfitMode = EMALTakeProfitMode.Fixed;
                AtrPeriod = 14;
                AtrTakeProfitMultiplier = 0.0;
                SlopeTakeProfitMultiplier = 0.0;
                MinimumTakeProfitPoints = 1.0;
                MaximumTakeProfitPoints = 30.0;

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
                AsiaMinimumSlope = 0.75;
                EuropeMinimumSlope = 0.75;
                UsMinimumSlope = 0.75;

                // Sunday entry windows, 18:05 - 24:00 New York time in 30-minute blocks.
                // OFF by default so the full Sunday-open-to-Friday-close run is unrestricted.
                RestrictToSundayWindows = false;
                Sunday1800 = true;
                Sunday1830 = true;
                Sunday1900 = true;
                Sunday1930 = true;
                Sunday2000 = true;
                Sunday2030 = true;
                Sunday2100 = true;
                Sunday2130 = true;
                Sunday2200 = true;
                Sunday2230 = true;
                Sunday2300 = true;
                Sunday2330 = true;

                // Blackout around the 08:30 US data release. HHmm, inclusive both ends.
                BlockNewsWindow = true;
                FlattenAtNewsBlock = true;
                NewsBlockStartTime = 828;
                NewsBlockEndTime = 832;

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

            // 17:00-18:00 ET is the CME maintenance halt.
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
        private double GetRequiredSlope(DateTime platformTime)
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
        private void CaptureEntryFeatures(int direction, double signalPrice, double takeProfit)
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
                N(signalPrice),
                N(ema[1]),
                N(slope),
                N(slopePrev),
                N(slope - slopePrev),
                N((signalPrice - ema[1]) * direction),
                N(atrValue),
                N(takeProfit),
                N(StopLossPoints),
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
                (CurrentBar - pendingEntryBar).ToString(CultureInfo.InvariantCulture)
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
            Print(string.Format("      Asia   18-03    : {0}  slope {1}", AsiaEnabled, AsiaMinimumSlope));
            Print(string.Format("      Europe 03-0930  : {0}  slope {1}", EuropeEnabled, EuropeMinimumSlope));
            Print(string.Format("      US     0930-17  : {0}  slope {1}", UsEnabled, UsMinimumSlope));
            Print(string.Format("  sunday filter       : {0}  (bars blocked: {1})",
                RestrictToSundayWindows, blockedBarCount));
            Print(string.Format("  enabled blocks      : {0}", EnabledBlocksSummary()));
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

            EvaluateEntryCancellation();
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

        // Grid origin for the 30-minute blocks. The first block is clipped by
        // SundayWindowStart, so 18:00-18:30 is really 18:05-18:30.
        private static readonly TimeSpan SundayGridOrigin = new TimeSpan(18, 0, 0);
        private static readonly TimeSpan SundayWindowStart = new TimeSpan(18, 5, 0);

        private bool IsEntryWindowOpen()
        {
            DateTime barOpenRaw = GetBarOpenRaw();
            DateTime barOpen = ConvertToEastern(barOpenRaw);

            // News blackout is checked first and applies on every day, independent of the
            // Sunday filter.
            if (IsNewsBlackout(barOpen))
            {
                newsBlockedBarCount++;
                return false;
            }

            // Session gate. Independent of the Sunday filter; both must pass.
            if (UsePerSessionSettings)
            {
                int session = GetSessionIndex(barOpenRaw);

                if (session < 0 || !IsSessionEnabled(session))
                    return false;
            }

            if (!RestrictToSundayWindows)
                return true;

            if (barOpen.DayOfWeek != DayOfWeek.Sunday)
                return false;

            return IsHalfHourEnabled(barOpen.TimeOfDay);
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

        // Maps a time of day onto one of the twelve 30-minute blocks between 18:00 and 24:00.
        // Blocks are half-open: [start, start+30min), so 18:30 belongs to the 18:30 block.
        private bool IsHalfHourEnabled(TimeSpan timeOfDay)
        {
            // Nothing before 18:05, even though the block grid is anchored at 18:00.
            if (timeOfDay < SundayWindowStart)
                return false;

            int block = (int)((timeOfDay - SundayGridOrigin).TotalMinutes / 30.0);

            switch (block)
            {
                case 0: return Sunday1800;
                case 1: return Sunday1830;
                case 2: return Sunday1900;
                case 3: return Sunday1930;
                case 4: return Sunday2000;
                case 5: return Sunday2030;
                case 6: return Sunday2100;
                case 7: return Sunday2130;
                case 8: return Sunday2200;
                case 9: return Sunday2230;
                case 10: return Sunday2300;
                case 11: return Sunday2330;
                default: return false;
            }
        }

        private string EnabledBlocksSummary()
        {
            string[] labels =
            {
                "18:05", "18:30", "19:00", "19:30", "20:00", "20:30",
                "21:00", "21:30", "22:00", "22:30", "23:00", "23:30"
            };

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            for (int i = 0; i < labels.Length; i++)
            {
                // Probe inside each block; block 0 must be probed at 18:05, not 18:00.
                TimeSpan probe = SundayGridOrigin + TimeSpan.FromMinutes(30 * i);

                if (probe < SundayWindowStart)
                    probe = SundayWindowStart;

                if (!IsHalfHourEnabled(probe))
                    continue;

                if (sb.Length > 0)
                    sb.Append(", ");

                sb.Append(labels[i]);
            }

            return sb.Length == 0 ? "(none)" : sb.ToString();
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            // Evaluate on every tick so unrealized profit can flatten an open position
            // immediately instead of waiting for the next one-minute bar.
            if (IsAccountBalanceBlocked())
                return;

            if (!IsFirstTickOfBar)
                return;

            ClearQueuedEntry();

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

        private void TrySubmitQueuedEntry()
        {
            // OnOrderUpdate can re-enter this method after an asynchronous cancellation.
            // Recheck the account latch here so no queued entry can escape the main gate.
            if (IsAccountBalanceBlocked())
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
            double signalPrice = queuedSignalPrice;
            ClearQueuedEntry();

            // Context the cancellation rules need while the order is live.
            activeEntryDirection = direction;
            activeEntryReferencePrice = signalPrice;
            activeEntrySubmitTime = lastTickTime != DateTime.MinValue ? lastTickTime : Time[0];
            entryCancelPending = false;

            string entrySignal = direction > 0 ? LongEntrySignal : ShortEntrySignal;

            BeginProtectionTracking(entrySignal, takeProfit);

            CaptureEntryFeatures(direction, signalPrice, takeProfit);

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

            return Instrument.MasterInstrument.RoundToTickSize(price);
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

        private void BeginProtectionTracking(string entrySignal, double takeProfitPoints)
        {
            protectedEntrySignal = entrySignal ?? string.Empty;
            activeTakeProfitPoints = Math.Max(TickSize, takeProfitPoints);
            entryFillValue = 0.0;
            entryFilledQuantity = 0;
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
            queuedTakeProfitPoints = 0.0;
            queuedSignalPrice = 0.0;
            queuedEntryBar = -1;
        }

        private void ValidateChart()
        {
            if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 1)
                throw new InvalidOperationException("EMAL must be applied to a 1-minute chart.");

            string instrumentName = Instrument == null || Instrument.MasterInstrument == null
                ? string.Empty
                : Instrument.MasterInstrument.Name;

            if (!string.Equals(instrumentName, "NQ", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(instrumentName, "MNQ", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("EMAL supports NQ and MNQ only.");
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
                    BeginProtectionTracking(orderName, TakeProfitPoints);

                entryFillValue += price * executionQuantity;
                entryFilledQuantity += executionQuantity;

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

            double stopDistance = Math.Max(TickSize, StopLossPoints);
            double targetDistance = Math.Max(TickSize, activeTakeProfitPoints);
            double stopPrice = positionDirection == MarketPosition.Long
                ? averageEntryPrice - stopDistance
                : averageEntryPrice + stopDistance;
            double targetPrice = positionDirection == MarketPosition.Long
                ? averageEntryPrice + targetDistance
                : averageEntryPrice - targetDistance;

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

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Limit Timeout (seconds)", Description = "Cancel an unfilled limit entry after this many seconds. 0 disables; the order then lives until the next bar opens.", GroupName = "Limit Entry Cancellation", Order = 0)]
        public double LimitOrderTimeoutSeconds { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Cancel If Moved (points)", Description = "Cancel an unfilled limit entry once price has travelled this far in the signal direction without us. 0 disables.", GroupName = "Limit Entry Cancellation", Order = 1)]
        public double CancelIfMovedPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Per-Session Settings", Description = "Enable the three-session split (Asia 18:00-03:00, Europe 03:00-09:30, US 09:30-17:00). When off, the global Minimum EMA Slope applies to every hour.", GroupName = "Sessions", Order = 0)]
        public bool UsePerSessionSettings { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia 18:00-03:00 Enabled", GroupName = "Sessions", Order = 1)]
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
        [Display(Name = "Restrict To Sunday Windows", Description = "Master switch. When on, new entries are allowed only on Sunday, from 18:05, and only inside the enabled blocks below. Exits are never affected.", GroupName = "Sunday Entry Windows", Order = 0)]
        public bool RestrictToSundayWindows { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "18:05 - 18:30", GroupName = "Sunday Entry Windows", Order = 1)]
        public bool Sunday1800 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "18:30 - 19:00", GroupName = "Sunday Entry Windows", Order = 2)]
        public bool Sunday1830 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "19:00 - 19:30", GroupName = "Sunday Entry Windows", Order = 3)]
        public bool Sunday1900 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "19:30 - 20:00", GroupName = "Sunday Entry Windows", Order = 4)]
        public bool Sunday1930 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "20:00 - 20:30", GroupName = "Sunday Entry Windows", Order = 5)]
        public bool Sunday2000 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "20:30 - 21:00", GroupName = "Sunday Entry Windows", Order = 6)]
        public bool Sunday2030 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "21:00 - 21:30", GroupName = "Sunday Entry Windows", Order = 7)]
        public bool Sunday2100 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "21:30 - 22:00", GroupName = "Sunday Entry Windows", Order = 8)]
        public bool Sunday2130 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "22:00 - 22:30", GroupName = "Sunday Entry Windows", Order = 9)]
        public bool Sunday2200 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "22:30 - 23:00", GroupName = "Sunday Entry Windows", Order = 10)]
        public bool Sunday2230 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "23:00 - 23:30", GroupName = "Sunday Entry Windows", Order = 11)]
        public bool Sunday2300 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "23:30 - 24:00", GroupName = "Sunday Entry Windows", Order = 12)]
        public bool Sunday2330 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Block News Window", Description = "Block new entries across a fixed daily time range, applied on every day regardless of the Sunday filter.", GroupName = "News Blackout", Order = 0)]
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
        [Display(Name = "Enable Feature Log", Description = "Write one CSV row per completed trade containing the entry context: slope, ATR, TP/ATR, the previous three bars OHLCV, fill delay and outcome.", GroupName = "Feature Log", Order = 0)]
        public bool EnableFeatureLog { get; set; }

        [Display(Name = "Feature Log Path", Description = "Full path to the CSV. Leave blank to auto-name a timestamped file in Documents. Appends if the file already exists.", GroupName = "Feature Log", Order = 1)]
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

    public enum EMALLimitPriceReference
    {
        BidAsk,
        Open,
        Close
    }
}
