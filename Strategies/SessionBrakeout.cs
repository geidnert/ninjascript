#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// SessionBrakeout: trades confirmed breakouts from balanced Asia, London, and NY AM ranges.
// All session times are New York time, independent of NinjaTrader's display time zone.
namespace NinjaTrader.NinjaScript.Strategies
{
    public enum SessionBrakeoutBracketMode
    {
        SessionRangeRewardRisk,
        FixedPoints
    }

    public class SessionBrakeout : Strategy
    {
        private const string StrategySignalPrefix = "SessionBrakeout";
        // Keep chart and broker-facing entry labels neutral. The strategy still
        // links its bracket orders to the matching direction internally.
        private const string LongEntrySignal = "Long";
        private const string ShortEntrySignal = "Short";
        private const string MaxBalanceExitSignal = StrategySignalPrefix + "MaxBalance";
        private const string MaxDailyProfitExitSignal = StrategySignalPrefix + "MaxDailyProfit";
        private const string InvalidConfigurationExitSignal = StrategySignalPrefix + "InvalidConfiguration";
        private const string EmaExitSignal = StrategySignalPrefix + "EmaCloseExit";

        private enum SessionKind { None = -1, Asia, London, NyAm, NyPm }

        private sealed class SessionRange
        {
            public SessionKind Kind;
            // NinjaTrader drawing timestamps must stay in the chart/platform zone.
            public DateTime DrawStartTime;
            public DateTime StartTime;
            public DateTime EndTime;
            public double High = double.MinValue;
            public double Low = double.MaxValue;
            public double Open;
            public double Close;
            public readonly List<double> Closes = new List<double>();
            public bool Qualified;
            public bool Armed;
            public bool TradeTaken;
            public bool Finalized;
            public bool RangeLevelTouched;
            public bool OppositeRetryUsed;
            public int RequiredEntryDirection;

            public double Size { get { return High - Low; } }
        }

        private TimeZoneInfo platformZone;
        private TimeZoneInfo easternZone;
        private SessionRange collectingRange;
        private SessionRange asiaRange;
        private SessionRange londonRange;
        private SessionRange nyAmRange;
        private SessionRange nyPmRange;
        private SessionKind liveVisualSession;
        private DateTime liveVisualSessionStart = DateTime.MinValue;
        private double liveVisualHigh = double.MinValue;
        private double liveVisualLow = double.MaxValue;
        private bool isConfiguredTimeframeValid = true;
        private bool isConfiguredInstrumentValid = true;
        private bool configurationWarningShown;
        private bool maxAccountLimitHit;
        private bool dailyProfitLimitHit;
        private DateTime dailyProfitDate = DateTime.MinValue;
        private double dailyProfitStartBalance;
        private EMA entryEma;
        private bool isConfiguredSettingsValid = true;
        private SessionRange activeTradeRange;
        private bool activeTradeWasLong;
        private bool activeEntryFilled;
        private DateTime activeEntryFillTime;
        private Order activeEntryOrder;
        private bool entryOrderPending;
        private bool emaExitPending;
        private int activeEntryExecutedQuantity;
        private int activeExitExecutedQuantity;
        private readonly HashSet<string> handledExecutions = new HashSet<string>();
        private string completedExitName;
        private OrderState completedExitState;
        private DateTime completedExitTime;
        private bool completedExitWasFlat;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Trades 5-minute, confirmed breakouts from balanced Asia, London, and NY AM ranges. Session boundaries are New York time.";
                Name = "SessionBrakeout";
                // Entries are intentionally evaluated only from completed 5-minute
                // bars.  This gives Playback and historical processing the exact
                // same decision path; OnEachTick can otherwise process a playback
                // bar differently from the historical reconstruction.
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                BarsRequiredToTrade = 20;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;

                Quantity = 1;
                RewardRiskMultiple = 1.75;
                BracketMode = SessionBrakeoutBracketMode.SessionRangeRewardRisk;
                FixedProfitTargetPoints = 20.0;
                FixedStopLossPoints = 10.0;
                UseRangeQualification = true;
                MinimumSessionRangePoints = 8.0;
                MaximumSessionRangePoints = 300.0;
                MaximumNetMovePercent = 60.0;
                CentralRangeBandPercent = 60.0;
                MinimumCentralClosePercent = 70.0;
                RequireEntryConfirmation = false;
                MaxAccountBalance = 0.0;
                MaxDailyProfit = 0.0;
                DrawSessionRanges = true;
                ShowInfoBox = true;
                EnableAsiaTrades = false;
                EnableLondonTrades = true;
                EnableNyAmTrades = false;
                EnableNyPmTrades = false;
                AsiaStartHHmm = 1800;
                AsiaEndHHmm = 300;
                LondonStartHHmm = 300;
                LondonEndHHmm = 930;
                NyAmStartHHmm = 930;
                NyAmEndHHmm = 1300;
                NyPmStartHHmm = 1300;
                NyPmEndHHmm = 1800;
                UseEmaFilter = false;
                EmaPeriod = 50;
                UseEmaCloseExit = false;
                AllowOppositeRetryAfterStop = false;
            }
            else if (State == State.DataLoaded)
            {
                SetupTimeZones();
                isConfiguredTimeframeValid = ValidateRequiredPrimaryTimeframe();
                isConfiguredInstrumentValid = ValidateRequiredPrimaryInstrument();
                isConfiguredSettingsValid = ValidateSessionSettings() && EmaPeriod >= 1 && ValidateBracketSettings();
                if ((UseEmaFilter || UseEmaCloseExit) && isConfiguredSettingsValid)
                {
                    entryEma = EMA(EmaPeriod);
                    AddChartIndicator(entryEma);
                }
            }
            else if (State == State.Realtime && activeEntryOrder != null && activeEntryOrder.IsBacktestOrder)
            {
                activeEntryOrder = GetRealtimeOrder(activeEntryOrder);
                if (activeEntryOrder == null)
                    entryOrderPending = false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade)
                return;

            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid || !isConfiguredSettingsValid)
            {
                ExitAllPositions(InvalidConfigurationExitSignal);
                ShowConfigurationWarningOnce();
                return;
            }

            RefreshDailyProfitBaseline();
            if (CheckAccountGuards())
                return;
            bool emaExitRequested = EvaluateEmaCloseExit();

            // Under OnBarClose, Time[0]/OHLC[0] are the newly completed bar.  Use
            // that same bar in both historical calculation and Playback.
            DateTime easternTime = ConvertToEastern(Time[0]);
            SessionKind currentSession = GetSessionKind(easternTime);
            AppendCompletedBar(0);
            UpdateLiveSessionDrawing(currentSession);
            UpdateInfoBox(currentSession, easternTime);

            if (CurrentBar == 0)
                return;

            if (!emaExitRequested && !emaExitPending)
                EvaluateArmedBreakouts(currentSession, true, Close[0]);
        }

        private void AppendCompletedBar(int barsAgo)
        {
            DateTime completedBarEasternTime = ConvertToEastern(Time[barsAgo]);
            SessionKind completedSession = GetSessionKind(completedBarEasternTime);
            if (completedSession == SessionKind.None)
            {
                if (collectingRange != null)
                {
                    FinalizeRange(collectingRange);
                    collectingRange = null;
                }
                return;
            }
            DateTime sessionStart = GetSessionStart(completedBarEasternTime, completedSession);
            if (collectingRange == null || collectingRange.Kind != completedSession || collectingRange.StartTime != sessionStart)
            {
                if (collectingRange != null)
                    FinalizeRange(collectingRange);
                collectingRange = new SessionRange
                {
                    Kind = completedSession,
                    StartTime = sessionStart,
                    DrawStartTime = Time[barsAgo]
                };
            }

            if (collectingRange.Closes.Count == 0)
                collectingRange.Open = Open[barsAgo];
            collectingRange.High = Math.Max(collectingRange.High, High[barsAgo]);
            collectingRange.Low = Math.Min(collectingRange.Low, Low[barsAgo]);
            collectingRange.Close = Close[barsAgo];
            collectingRange.Closes.Add(Close[barsAgo]);
        }

        private void UpdateLiveSessionDrawing(SessionKind currentSession)
        {
            if (currentSession == SessionKind.None)
            {
                liveVisualSessionStart = DateTime.MinValue;
                return;
            }
            if (liveVisualSessionStart == DateTime.MinValue || liveVisualSession != currentSession)
            {
                liveVisualSession = currentSession;
                liveVisualSessionStart = Time[0];
                liveVisualHigh = High[0];
                liveVisualLow = Low[0];
            }
            else
            {
                liveVisualHigh = Math.Max(liveVisualHigh, High[0]);
                liveVisualLow = Math.Min(liveVisualLow, Low[0]);
            }

            if (!DrawSessionRanges || !ShouldDrawSourceRange(currentSession))
                return;

            string key = currentSession + "_" + GetSessionStart(ConvertToEastern(Time[0]), currentSession).ToString("yyyyMMddHHmm");
            Brush brush = GetRangeBrush(currentSession);
            Draw.Line(this, StrategySignalPrefix + "High_" + key, false,
                liveVisualSessionStart, liveVisualHigh, Time[0], liveVisualHigh, brush, DashStyleHelper.Solid, 2);
            Draw.Line(this, StrategySignalPrefix + "Low_" + key, false,
                liveVisualSessionStart, liveVisualLow, Time[0], liveVisualLow, brush, DashStyleHelper.Solid, 2);
            DrawSourceSessionBox(collectingRange, Time[0]);
        }

        private void FinalizeRange(SessionRange range)
        {
            if (range == null || range.Finalized || range.Closes.Count == 0)
                return;

            range.EndTime = GetSessionEnd(range.StartTime, range.Kind);
            range.Finalized = true;
            range.Qualified = IsBalancedRange(range);
            range.Armed = range.Qualified;
            // Keep the source high/low visible across its full immediate target
            // session. Example: Asia's levels span all of London when London is
            // the enabled target, even if London breaks them early.
            DrawRange(range, ConvertToPlatform(GetTargetSessionEnd(range.Kind, range.EndTime)), true);

            if (range.Kind == SessionKind.Asia)
                asiaRange = range;
            else if (range.Kind == SessionKind.London)
                londonRange = range;
            else if (range.Kind == SessionKind.NyAm)
                nyAmRange = range;
            else if (range.Kind == SessionKind.NyPm)
                nyPmRange = range;

            Print(string.Format("SessionBrakeout|RANGE|{0}|{1:yyyy-MM-dd HH:mm}|size={2:F2}|netMove={3:F1}%|central={4:F1}%|qualified={5}|qualificationEnabled={6}",
                range.Kind, range.StartTime, range.Size, GetNetMovePercent(range), GetCentralClosePercent(range), range.Qualified, UseRangeQualification));
        }

        private bool IsBalancedRange(SessionRange range)
        {
            if (range.Closes.Count == 0)
                return false;
            if (!UseRangeQualification)
                return true;
            if (range.Size < MinimumSessionRangePoints || range.Size > MaximumSessionRangePoints)
                return false;
            return GetNetMovePercent(range) <= MaximumNetMovePercent
                && GetCentralClosePercent(range) >= MinimumCentralClosePercent;
        }

        private double GetNetMovePercent(SessionRange range)
        {
            if (range.Size <= 0.0)
                return 100.0;
            return Math.Abs(range.Close - range.Open) / range.Size * 100.0;
        }

        private double GetCentralClosePercent(SessionRange range)
        {
            if (range.Size <= 0.0 || range.Closes.Count == 0)
                return 0.0;
            double bandFraction = Math.Max(0.0, Math.Min(100.0, CentralRangeBandPercent)) / 100.0;
            double outerFraction = (1.0 - bandFraction) / 2.0;
            double lower = range.Low + range.Size * outerFraction;
            double upper = range.High - range.Size * outerFraction;
            int count = 0;
            foreach (double close in range.Closes)
                if (close >= lower && close <= upper)
                    count++;
            return count * 100.0 / range.Closes.Count;
        }

        private void EvaluateArmedBreakouts(SessionKind currentSession, bool completedBarBelongsToTarget, double completedClose)
        {
            if (currentSession == SessionKind.None)
                return;
            SessionRange candidate = GetCandidateForTarget(currentSession);

            if (candidate == null || !completedBarBelongsToTarget)
                return;
            DateTime targetStart = GetSessionStart(ConvertToEastern(Time[0]), currentSession);
            if (candidate.StartTime != GetSessionStart(targetStart, candidate.Kind) || candidate.EndTime > targetStart)
                return;

            // A wick through a level is not a breakout. Only the final close of
            // this five-minute target-session bar can trigger an entry.
            if (!candidate.RangeLevelTouched
                && (completedClose > candidate.High || completedClose < candidate.Low))
            {
                candidate.RangeLevelTouched = true;
                Print(string.Format("SessionBrakeout|BREAKOUT_CHECK|target={0}|source={1}|close={2:F2}|high={3:F2}|low={4:F2}|qualified={5}|enabled={6}|armed={7}|position={8}",
                    GetSessionDisplayName(currentSession), GetSessionDisplayName(candidate.Kind), completedClose,
                    candidate.High, candidate.Low, candidate.Qualified, IsTradeEnabled(currentSession),
                    candidate.Armed, Position.MarketPosition));
            }

            if (!candidate.Qualified)
                return;

            if (Position.MarketPosition != MarketPosition.Flat || entryOrderPending || emaExitPending)
                return;

            if (!IsTradeEnabled(currentSession))
                return;

            if (!candidate.Armed || candidate.TradeTaken)
                return;

            if (completedClose > candidate.High && candidate.RequiredEntryDirection >= 0)
                SubmitBreakout(true, candidate, completedClose);
            else if (completedClose < candidate.Low && candidate.RequiredEntryDirection <= 0)
                SubmitBreakout(false, candidate, completedClose);
        }

        private void SubmitBreakout(bool isLong, SessionRange range, double breakoutClose)
        {
            if (!AllowsEmaEntry(isLong, breakoutClose))
                return;
            int stopTicks;
            int targetTicks;
            if (BracketMode == SessionBrakeoutBracketMode.FixedPoints)
            {
                if (!TryGetFixedTicks(FixedStopLossPoints, out stopTicks) || !TryGetFixedTicks(FixedProfitTargetPoints, out targetTicks))
                {
                    Print("SessionBrakeout|INVALID_BRACKETS|Fixed TP/SL must be positive, finite whole-tick distances.");
                    return;
                }
            }
            else
            {
                stopTicks = Math.Max(1, (int)Math.Ceiling((range.Size * 0.5) / TickSize));
                targetTicks = Math.Max(1, (int)Math.Round(stopTicks * RewardRiskMultiple, MidpointRounding.AwayFromZero));
            }
            string signal = isLong ? LongEntrySignal : ShortEntrySignal;
            if (!ConfirmEntry(isLong ? "Buy breakout" : "Sell-short breakout", breakoutClose, range))
                return;

            SetStopLoss(signal, CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget(signal, CalculationMode.Ticks, targetTicks);
            range.TradeTaken = true;
            range.Armed = false;
            activeTradeRange = range;
            activeTradeWasLong = isLong;
            activeEntryFilled = false;
            activeEntryFillTime = DateTime.MinValue;
            activeEntryOrder = null;
            entryOrderPending = true;
            emaExitPending = false;
            activeEntryExecutedQuantity = 0;
            activeExitExecutedQuantity = 0;
            completedExitName = null;
            completedExitWasFlat = false;
            Print(string.Format("SessionBrakeout|BREAKOUT|{0}|source={1}|close={2:F2}|range={3:F2}|stopTicks={4}|targetTicks={5}|mode={6}",
                isLong ? "Long" : "Short", range.Kind, breakoutClose, range.Size, stopTicks, targetTicks, BracketMode));
            if (isLong)
                EnterLong(Quantity, LongEntrySignal);
            else
                EnterShort(Quantity, ShortEntrySignal);
        }

        private bool EvaluateEmaCloseExit()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                emaExitPending = false;
                return false;
            }
            if (!UseEmaCloseExit || entryEma == null || CurrentBar + 1 < EmaPeriod || entryOrderPending)
                return false;
            if (emaExitPending)
                return true;
            double value = entryEma[0];
            if (double.IsNaN(value) || double.IsInfinity(value))
                return false;
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            if (isLong ? Close[0] > value : Close[0] < value)
                return false;
            emaExitPending = true;
            Print(string.Format("SessionBrakeout|EMA_EXIT|direction={0}|close={1:F2}|ema={2:F2}|period={3}",
                isLong ? "Long" : "Short", Close[0], value, EmaPeriod));
            if (isLong)
                ExitLong(EmaExitSignal, LongEntrySignal);
            else
                ExitShort(EmaExitSignal, ShortEntrySignal);
            return true;
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time,
            ErrorCode error, string nativeError)
        {
            if (order == null || activeTradeRange == null)
                return;
            if (State == State.Realtime && order.IsBacktestOrder)
            {
                Order realtimeOrder = GetRealtimeOrder(order);
                if (ReferenceEquals(activeEntryOrder, order))
                    activeEntryOrder = realtimeOrder;
                if (realtimeOrder == null)
                {
                    if (order.Name == (activeTradeWasLong ? LongEntrySignal : ShortEntrySignal))
                        entryOrderPending = false;
                    return;
                }
                order = realtimeOrder;
            }
            string entrySignal = activeTradeWasLong ? LongEntrySignal : ShortEntrySignal;
            if (order.Name == entrySignal && entryOrderPending
                && (activeEntryOrder == null || ReferenceEquals(activeEntryOrder, order)
                    || (!string.IsNullOrEmpty(order.OrderId) && activeEntryOrder.OrderId == order.OrderId)))
            {
                activeEntryOrder = order;
                if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                {
                    entryOrderPending = filled > activeEntryExecutedQuantity;
                    if (!activeEntryFilled && filled == 0)
                        activeTradeRange = null;
                    Print("SessionBrakeout|ENTRY_TERMINAL|" + orderState + "|" + nativeError);
                    CompleteTrackedExit();
                }
            }
            else if (order.Name == EmaExitSignal && order.FromEntrySignal == entrySignal
                && time >= activeEntryFillTime && (orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
            {
                emaExitPending = false;
                Print("SessionBrakeout|EMA_EXIT_TERMINAL|" + orderState + "|retry=next_completed_bar|" + nativeError);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price,
            int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null || activeTradeRange == null)
                return;
            if (!string.IsNullOrEmpty(executionId) && !handledExecutions.Add(executionId))
                return;
            Order order = execution.Order;
            string entrySignal = activeTradeWasLong ? LongEntrySignal : ShortEntrySignal;
            if (order.Name == entrySignal)
            {
                if (activeEntryOrder != null && !ReferenceEquals(activeEntryOrder, order)
                    && (string.IsNullOrEmpty(order.OrderId) || activeEntryOrder.OrderId != order.OrderId))
                    return;
                activeEntryOrder = order;
                if (!activeEntryFilled)
                    activeEntryFillTime = time;
                activeEntryFilled = true;
                activeEntryExecutedQuantity += quantity;
                entryOrderPending = activeEntryExecutedQuantity < order.Filled
                    || (order.OrderState != OrderState.Filled && order.OrderState != OrderState.Cancelled && order.OrderState != OrderState.Rejected);
                CompleteTrackedExit();
                return;
            }
            if (!activeEntryFilled || order.FromEntrySignal != entrySignal || time < activeEntryFillTime)
                return;
            activeExitExecutedQuantity += quantity;
            completedExitName = order.Name;
            completedExitState = order.OrderState;
            completedExitTime = time;
            completedExitWasFlat = marketPosition == MarketPosition.Flat;
            CompleteTrackedExit();
        }

        private void CompleteTrackedExit()
        {
            if (activeTradeRange == null || completedExitName == null || !completedExitWasFlat
                || entryOrderPending || activeExitExecutedQuantity != activeEntryExecutedQuantity)
                return;

            bool stopped = completedExitName == "Stop loss" && completedExitState == OrderState.Filled;
            DateTime time = completedExitTime;
            SessionRange range = activeTradeRange;
            bool wasLong = activeTradeWasLong;
            activeTradeRange = null;
            activeEntryOrder = null;
            activeEntryFilled = false;
            emaExitPending = false;
            completedExitName = null;
            if (!AllowOppositeRetryAfterStop || !stopped || range.OppositeRetryUsed)
                return;
            DateTime easternTime = ConvertToEastern(time);
            SessionKind target = GetTargetSessionKind(range.Kind);
            if (GetSessionKind(easternTime) != target
                || range.StartTime != GetSessionStart(GetSessionStart(easternTime, target), range.Kind))
                return;
            range.OppositeRetryUsed = true;
            range.RequiredEntryDirection = wasLong ? -1 : 1;
            range.TradeTaken = false;
            range.Armed = true;
            Print(string.Format("SessionBrakeout|OPPOSITE_RETRY_ARMED|source={0}|direction={1}",
                range.Kind, wasLong ? "Short" : "Long"));
        }

        private bool ValidateBracketSettings()
        {
            if (BracketMode == SessionBrakeoutBracketMode.SessionRangeRewardRisk)
                return true;
            int ticks;
            return BracketMode == SessionBrakeoutBracketMode.FixedPoints
                && TryGetFixedTicks(FixedStopLossPoints, out ticks)
                && TryGetFixedTicks(FixedProfitTargetPoints, out ticks);
        }

        private bool TryGetFixedTicks(double points, out int ticks)
        {
            ticks = 0;
            if (double.IsNaN(points) || double.IsInfinity(points) || points <= 0.0 || TickSize <= 0.0)
                return false;
            double exact = points / TickSize;
            double rounded = Math.Round(exact, MidpointRounding.AwayFromZero);
            if (double.IsNaN(rounded) || double.IsInfinity(rounded) || rounded < 1.0 || rounded > int.MaxValue
                || Math.Abs(exact - rounded) > 1e-8)
                return false;
            ticks = (int)rounded;
            return true;
        }

        private bool AllowsEmaEntry(bool isLong, double breakoutClose)
        {
            if (!UseEmaFilter)
                return true;
            if (entryEma == null || CurrentBar + 1 < EmaPeriod)
                return false;
            double value = entryEma[0];
            bool allowed = !double.IsNaN(value) && !double.IsInfinity(value)
                && (isLong ? breakoutClose > value : breakoutClose < value);
            if (!allowed)
                Print(string.Format("SessionBrakeout|EMA_BLOCKED|direction={0}|close={1:F2}|ema={2:F2}|period={3}",
                    isLong ? "Long" : "Short", breakoutClose, value, EmaPeriod));
            return allowed;
        }

        private bool ConfirmEntry(string orderType, double price, SessionRange range)
        {
            if (!RequireEntryConfirmation)
                return true;
            return MessageBox.Show(string.Format("{0} {1} contract(s) at {2:F2}? Source: {3}, range: {4:F2} points.",
                    orderType, Quantity, price, range.Kind, range.Size), Name,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        private void DrawRange(SessionRange range, DateTime endTime, bool isFinal)
        {
            if (!DrawSessionRanges || range == null || range.High == double.MinValue || !ShouldDrawSourceRange(range.Kind))
                return;
            string key = range.Kind + "_" + range.StartTime.ToString("yyyyMMddHHmm");
            Brush brush = GetRangeBrush(range.Kind);
            if (isFinal)
            {
                Brush fillBrush = range.Qualified ? Brushes.LimeGreen : Brushes.IndianRed;
                Draw.Rectangle(this, StrategySignalPrefix + "Area_" + key, false,
                    range.DrawStartTime, range.High, endTime, range.Low,
                    Brushes.Transparent, fillBrush, 14);
            }
            Draw.Line(this, StrategySignalPrefix + "High_" + key, false, range.DrawStartTime, range.High, endTime, range.High,
                brush, DashStyleHelper.Solid, 2);
            Draw.Line(this, StrategySignalPrefix + "Low_" + key, false, range.DrawStartTime, range.Low, endTime, range.Low,
                brush, DashStyleHelper.Solid, 2);
            if (isFinal)
            {
                DrawSourceSessionBox(range, ConvertToPlatform(range.EndTime));
                DrawRangeStatusLabel(range, endTime, key);
            }
        }

        private void DrawSourceSessionBox(SessionRange range, DateTime endTime)
        {
            string key = StrategySignalPrefix + "SourceBox_" + range.Kind + "_" + range.StartTime.ToString("yyyyMMddHHmm");
            DateTime startTime = ConvertToPlatform(range.StartTime);
            Draw.Line(this, key + "_Top", false, startTime, range.High, endTime, range.High,
                Brushes.LightGray, DashStyleHelper.Dot, 1);
            Draw.Line(this, key + "_Bottom", false, startTime, range.Low, endTime, range.Low,
                Brushes.LightGray, DashStyleHelper.Dot, 1);
            Draw.Line(this, key + "_Start", false, startTime, range.High, startTime, range.Low,
                Brushes.LightGray, DashStyleHelper.Dot, 1);
            Draw.Line(this, key + "_End", false, endTime, range.High, endTime, range.Low,
                Brushes.LightGray, DashStyleHelper.Dot, 1);
        }

        private void DrawRangeStatusLabel(SessionRange range, DateTime endTime, string key)
        {
            long midpointTicks = range.DrawStartTime.Ticks + (endTime.Ticks - range.DrawStartTime.Ticks) / 2;
            DateTime midpoint = new DateTime(midpointTicks);
            string target = GetSessionDisplayName(GetTargetSessionKind(range.Kind));
            string status = !UseRangeQualification ? "Range qualification disabled" : range.Qualified
                ? string.Format("Accepted | net {0:F1}% | inside band {1:F1}%",
                    GetNetMovePercent(range), GetCentralClosePercent(range))
                : "Rejected: " + GetRejectionReason(range);
            string text = string.Format("{0} → {1}\n{2}", GetSessionDisplayName(range.Kind), target, status);
            Brush statusBrush = range.Qualified ? Brushes.LightGreen : Brushes.LightCoral;
            Draw.Text(this, StrategySignalPrefix + "Status_" + key, false, text, midpoint,
                range.High + TickSize * 4, 0, statusBrush, new SimpleFont("Segoe UI", 12),
                TextAlignment.Center, Brushes.Transparent, Brushes.Transparent, 0);
        }

        private static Brush GetRangeBrush(SessionKind kind)
        {
            if (kind == SessionKind.Asia)
                return Brushes.MediumSeaGreen;
            if (kind == SessionKind.London)
                return Brushes.DodgerBlue;
            if (kind == SessionKind.NyAm)
                return Brushes.DarkOrange;
            return Brushes.MediumPurple;
        }

        private SessionRange GetCandidateForTarget(SessionKind targetSession)
        {
            if (targetSession == SessionKind.Asia)
                return nyPmRange;
            if (targetSession == SessionKind.London)
                return asiaRange;
            if (targetSession == SessionKind.NyAm)
                return londonRange;
            return nyAmRange;
        }

        // The panel reports only a finalized source range whose immediate target
        // is enabled. This keeps a London-only chart locked to the last Asia
        // range, rather than showing the unrelated London -> NY AM candidate.
        private SessionRange GetLatestSelectedSourceRange()
        {
            SessionRange latest = null;
            SessionRange[] ranges = { asiaRange, londonRange, nyAmRange, nyPmRange };
            foreach (SessionRange range in ranges)
            {
                if (range == null || !range.Finalized || !ShouldDrawSourceRange(range.Kind))
                    continue;
                if (latest == null || range.EndTime > latest.EndTime)
                    latest = range;
            }
            return latest;
        }

        private void UpdateInfoBox(SessionKind currentSession, DateTime easternTime)
        {
            if (!ShowInfoBox)
            {
                RemoveDrawObject(StrategySignalPrefix + "InfoBox");
                return;
            }

            SessionRange candidate = GetLatestSelectedSourceRange();
            SessionKind targetSession = candidate == null ? currentSession : GetTargetSessionKind(candidate.Kind);
            string rangeText = candidate == null
                ? "Waiting for selected source range"
                : string.Format("{0} {1:F2} pts", GetSessionDisplayName(candidate.Kind), candidate.Size);
            string tradeText;
            if (candidate == null)
            {
                tradeText = "Waiting for source range";
            }
            else if (!candidate.Qualified)
            {
                tradeText = "Rejected: " + GetRejectionReason(candidate);
            }
            else if (!IsTradeEnabled(targetSession))
            {
                tradeText = "Target session disabled";
            }
            else if (candidate.TradeTaken)
            {
                tradeText = "Breakout traded";
            }
            else
            {
                tradeText = candidate.RequiredEntryDirection == 0 ? "Armed: close outside range"
                    : candidate.RequiredEntryDirection > 0 ? "Armed: opposite long retry" : "Armed: opposite short retry";
            }

            string text = string.Format(
                "SessionBrakeout\nContracts: {0}\nSource range: {1}\nTrade: {2}\nNews: Disabled\nTarget session: {3} ET\nAs of: {4:yyyy-MM-dd HH:mm} ET\n{5}",
                Quantity, rangeText, currentSession == SessionKind.None ? "Outside configured sessions" : tradeText,
                GetSessionDisplayName(targetSession), easternTime, GetEnabledSessionSummary());
            Brush statusBrush = candidate == null
                ? Brushes.LightSteelBlue
                : candidate.Qualified ? Brushes.LightGreen : Brushes.LightCoral;
            Draw.TextFixed(this, StrategySignalPrefix + "InfoBox", text, TextPosition.BottomLeft,
                statusBrush, new SimpleFont("Segoe UI", 14), Brushes.DimGray, Brushes.Black, 55);
        }

        private string GetRejectionReason(SessionRange range)
        {
            if (range.Size < MinimumSessionRangePoints)
                return string.Format("range {0:F2} < {1:F2}", range.Size, MinimumSessionRangePoints);
            if (range.Size > MaximumSessionRangePoints)
                return string.Format("range {0:F2} > {1:F2}", range.Size, MaximumSessionRangePoints);
            double netMove = GetNetMovePercent(range);
            if (netMove > MaximumNetMovePercent)
                return string.Format("net move {0:F1}% > {1:F1}%", netMove, MaximumNetMovePercent);
            return string.Format("inside band {0:F1}% < {1:F1}%",
                GetCentralClosePercent(range), MinimumCentralClosePercent);
        }

        private static string GetSessionDisplayName(SessionKind session)
        {
            if (session == SessionKind.None)
                return "None";
            if (session == SessionKind.NyAm)
                return "NY AM";
            if (session == SessionKind.NyPm)
                return "NY PM";
            return session.ToString();
        }

        private string GetEnabledSessionSummary()
        {
            var enabled = new List<string>();
            if (EnableAsiaTrades) enabled.Add("Asia");
            if (EnableLondonTrades) enabled.Add("London");
            if (EnableNyAmTrades) enabled.Add("NY AM");
            if (EnableNyPmTrades) enabled.Add("NY PM");
            return "Enabled: " + (enabled.Count == 0 ? "None" : string.Join(", ", enabled));
        }

        // A source range is visible only when its immediate successor is enabled
        // for trades. This keeps the chart focused on levels that can actually
        // produce a trade under the selected session configuration.
        private bool ShouldDrawSourceRange(SessionKind sourceSession)
        {
            if (sourceSession == SessionKind.NyPm)
                return EnableAsiaTrades;
            if (sourceSession == SessionKind.Asia)
                return EnableLondonTrades;
            if (sourceSession == SessionKind.London)
                return EnableNyAmTrades;
            return EnableNyPmTrades;
        }

        private SessionKind GetSessionKind(DateTime easternTime)
        {
            int minute = easternTime.Hour * 60 + easternTime.Minute;
            for (int kind = 0; kind < 4; kind++)
            {
                SessionKind session = (SessionKind)kind;
                if (IsInMinuteWindow(minute, GetStartMinute(session), GetEndMinute(session)))
                    return session;
            }
            return SessionKind.None;
        }

        private static SessionKind GetTargetSessionKind(SessionKind sourceSession)
        {
            if (sourceSession == SessionKind.NyPm)
                return SessionKind.Asia;
            if (sourceSession == SessionKind.Asia)
                return SessionKind.London;
            if (sourceSession == SessionKind.London)
                return SessionKind.NyAm;
            return SessionKind.NyPm;
        }

        private DateTime GetSessionStart(DateTime easternTime, SessionKind kind)
        {
            int start = GetStartMinute(kind);
            DateTime startTime = easternTime.Date.AddMinutes(start);
            if (startTime > easternTime)
                startTime = startTime.AddDays(-1);
            return startTime;
        }

        private DateTime GetSessionEnd(DateTime start, SessionKind kind)
        {
            int duration = (GetEndMinute(kind) - GetStartMinute(kind) + 1440) % 1440;
            return start.AddMinutes(duration);
        }

        private DateTime GetTargetSessionEnd(SessionKind source, DateTime targetStart)
        {
            SessionKind target = GetTargetSessionKind(source);
            DateTime start = targetStart.Date.AddMinutes(GetStartMinute(target));
            if (start < targetStart)
                start = start.AddDays(1);
            return GetSessionEnd(start, target);
        }

        private int GetStartMinute(SessionKind kind)
        {
            return HhmmToMinute(kind == SessionKind.Asia ? AsiaStartHHmm
                : kind == SessionKind.London ? LondonStartHHmm
                : kind == SessionKind.NyAm ? NyAmStartHHmm : NyPmStartHHmm);
        }

        private int GetEndMinute(SessionKind kind)
        {
            return HhmmToMinute(kind == SessionKind.Asia ? AsiaEndHHmm
                : kind == SessionKind.London ? LondonEndHHmm
                : kind == SessionKind.NyAm ? NyAmEndHHmm : NyPmEndHHmm);
        }

        private static int HhmmToMinute(int hhmm)
        {
            return hhmm / 100 * 60 + hhmm % 100;
        }

        private bool ValidateSessionSettings()
        {
            int[] values = { AsiaStartHHmm, AsiaEndHHmm, LondonStartHHmm, LondonEndHHmm,
                NyAmStartHHmm, NyAmEndHHmm, NyPmStartHHmm, NyPmEndHHmm };
            foreach (int value in values)
                if (value < 0 || value > 2359 || value % 100 >= 60)
                    return false;
            int origin = GetStartMinute(SessionKind.Asia);
            int previousEnd = 0;
            for (int kind = 0; kind < 4; kind++)
            {
                SessionKind session = (SessionKind)kind;
                int start = GetStartMinute(session);
                int offset = (start - origin + 1440) % 1440;
                int duration = (GetEndMinute(session) - start + 1440) % 1440;
                if (duration == 0 || offset < previousEnd || offset + duration > 1440)
                    return false;
                previousEnd = offset + duration;
            }
            return true;
        }

        private bool IsTradeEnabled(SessionKind targetSession)
        {
            if (targetSession == SessionKind.Asia)
                return EnableAsiaTrades;
            if (targetSession == SessionKind.London)
                return EnableLondonTrades;
            if (targetSession == SessionKind.NyAm)
                return EnableNyAmTrades;
            return EnableNyPmTrades;
        }

        private static bool IsInMinuteWindow(int minute, int start, int end)
        {
            return start <= end ? minute >= start && minute < end : minute >= start || minute < end;
        }

        private void SetupTimeZones()
        {
            try
            {
                platformZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
                easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                Print(string.Format("SessionBrakeout|TIMEZONE|platform={0}|sessions=NY time", platformZone == null ? "unknown" : platformZone.Id));
            }
            catch (Exception ex)
            {
                platformZone = null;
                easternZone = null;
                Print("SessionBrakeout|TIMEZONE_ERROR|" + ex.Message);
            }
        }

        private DateTime ConvertToEastern(DateTime platformTime)
        {
            if (platformZone == null || easternZone == null || platformZone.Equals(easternZone))
                return platformTime;
            try
            {
                return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(platformTime, DateTimeKind.Unspecified), platformZone, easternZone);
            }
            catch
            {
                return platformTime;
            }
        }

        private DateTime ConvertToPlatform(DateTime easternTime)
        {
            if (platformZone == null || easternZone == null || platformZone.Equals(easternZone))
                return easternTime;
            try
            {
                return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(easternTime, DateTimeKind.Unspecified), easternZone, platformZone);
            }
            catch
            {
                return easternTime;
            }
        }

        private bool ValidateRequiredPrimaryTimeframe()
        {
            return BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == 5;
        }

        private bool ValidateRequiredPrimaryInstrument()
        {
            string name = Instrument == null || Instrument.MasterInstrument == null ? string.Empty : Instrument.MasterInstrument.Name;
            return string.Equals(name, "NQ", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "MNQ", StringComparison.OrdinalIgnoreCase);
        }

        private void ShowConfigurationWarningOnce()
        {
            if (configurationWarningShown)
                return;
            configurationWarningShown = true;
            Print(string.Format("SessionBrakeout is disabled: it requires a 5-minute NQ or MNQ chart, a positive EMA period, valid whole-tick fixed TP/SL when selected, and valid HHmm windows in Asia -> London -> NY AM -> NY PM order without overlaps (current: {0} {1}).",
                BarsPeriod == null ? "unknown" : BarsPeriod.ToString(), Instrument == null ? "unknown" : Instrument.FullName));
        }

        private void ExitAllPositions(string reason)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(reason, LongEntrySignal);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(reason, ShortEntrySignal);
        }

        private void RefreshDailyProfitBaseline()
        {
            if (MaxDailyProfit <= 0.0)
            {
                dailyProfitLimitHit = false;
                dailyProfitDate = DateTime.MinValue;
                return;
            }
            if (dailyProfitDate.Date == Time[0].Date)
                return;
            dailyProfitDate = Time[0].Date;
            dailyProfitStartBalance = GetNetLiquidation();
            dailyProfitLimitHit = false;
        }

        private bool CheckAccountGuards()
        {
            double netLiquidation = GetNetLiquidation();
            if (MaxAccountBalance > 0.0 && (maxAccountLimitHit || netLiquidation >= MaxAccountBalance))
            {
                maxAccountLimitHit = true;
                ExitAllPositions(MaxBalanceExitSignal);
                return true;
            }
            if (MaxDailyProfit > 0.0 && (dailyProfitLimitHit || netLiquidation - dailyProfitStartBalance >= MaxDailyProfit))
            {
                dailyProfitLimitHit = true;
                ExitAllPositions(MaxDailyProfitExitSignal);
                return true;
            }
            return false;
        }

        private double GetNetLiquidation()
        {
            if (Account == null)
                return 0.0;
            try
            {
                return Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
            }
            catch
            {
                return Account.Get(AccountItem.CashValue, Currency.UsDollar)
                    + Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
            }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "Parameters", Order = 0)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 20.0)]
        [Display(Name = "Reward/risk multiple", GroupName = "Parameters", Order = 1)]
        public double RewardRiskMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bracket mode", Description = "SessionRangeRewardRisk uses half the source range for SL and the reward/risk multiple for TP. FixedPoints uses the two independent point inputs.", GroupName = "Risk", Order = 0)]
        public SessionBrakeoutBracketMode BracketMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, double.MaxValue)]
        [Display(Name = "Fixed take profit (points)", Description = "Used only in FixedPoints mode. Distance from actual entry fill; must be a whole number of instrument ticks.", GroupName = "Risk", Order = 1)]
        public double FixedProfitTargetPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, double.MaxValue)]
        [Display(Name = "Fixed stop loss (points)", Description = "Used only in FixedPoints mode. Distance from actual entry fill, not a guaranteed maximum filled loss.", GroupName = "Risk", Order = 2)]
        public double FixedStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use range qualification", Description = "When off, bypass minimum/maximum source range, net-move and central-close tests. Session, EMA, breakout and risk rules still apply.", GroupName = "Range qualification", Order = 9)]
        public bool UseRangeQualification { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, double.MaxValue)]
        [Display(Name = "Minimum session range points", GroupName = "Range qualification", Order = 10)]
        public double MinimumSessionRangePoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, double.MaxValue)]
        [Display(Name = "Maximum session range points", GroupName = "Range qualification", Order = 11)]
        public double MaximumSessionRangePoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Maximum start-to-end move %", GroupName = "Range qualification", Order = 12)]
        public double MaximumNetMovePercent { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 100.0)]
        [Display(Name = "Middle range band width %", GroupName = "Range qualification", Order = 13)]
        public double CentralRangeBandPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Minimum closes inside middle band %", GroupName = "Range qualification", Order = 14)]
        public double MinimumCentralClosePercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw session ranges", GroupName = "Visuals", Order = 20)]
        public bool DrawSessionRanges { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show info box", GroupName = "Visuals", Order = 21)]
        public bool ShowInfoBox { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Asia trades", GroupName = "Trade sessions", Order = 30)]
        public bool EnableAsiaTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable London trades", GroupName = "Trade sessions", Order = 31)]
        public bool EnableLondonTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable NY AM trades", GroupName = "Trade sessions", Order = 32)]
        public bool EnableNyAmTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable NY PM trades", GroupName = "Trade sessions", Order = 33)]
        public bool EnableNyPmTrades { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Asia start (HHmm)", Description = "New York time. 1800 means 18:00. Start inclusive; overnight sessions allowed.", GroupName = "Session times (New York)", Order = 0)]
        public int AsiaStartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "Asia end (HHmm)", Description = "End exclusive. Gaps are allowed; windows must not overlap.", GroupName = "Session times (New York)", Order = 1)]
        public int AsiaEndHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "London start (HHmm)", GroupName = "Session times (New York)", Order = 2)]
        public int LondonStartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "London end (HHmm)", GroupName = "Session times (New York)", Order = 3)]
        public int LondonEndHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "NY AM start (HHmm)", GroupName = "Session times (New York)", Order = 4)]
        public int NyAmStartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "NY AM end (HHmm)", GroupName = "Session times (New York)", Order = 5)]
        public int NyAmEndHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "NY PM start (HHmm)", GroupName = "Session times (New York)", Order = 6)]
        public int NyPmStartHHmm { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2359)]
        [Display(Name = "NY PM end (HHmm)", GroupName = "Session times (New York)", Order = 7)]
        public int NyPmEndHHmm { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA filter", Description = "Only long when the completed breakout close is strictly above EMA; only short when strictly below. No exits are added.", GroupName = "EMA filter", Order = 0)]
        public bool UseEmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA period", Description = "EMA of chart closes. Requires at least this many chart bars before entries; disabled by default.", GroupName = "EMA filter", Order = 1)]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit on EMA close", Description = "Exit an open long when a completed bar closes at/below EMA, or short at/above EMA. Uses EMA period even if the entry filter is off. Market request, not a guaranteed EMA fill.", GroupName = "EMA filter", Order = 2)]
        public bool UseEmaCloseExit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow one opposite retry after SL", Description = "After a fully closed stop-loss trade, permit one opposite-direction breakout from the same source range during its immediate target session. EMA/normal breakout rules still apply. TP/EMA exits do not rearm.", GroupName = "Trade sessions", Order = 34)]
        public bool AllowOppositeRetryAfterStop { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require entry confirmation", GroupName = "Safety", Order = 30)]
        public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max account balance", GroupName = "Safety", Order = 31)]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max daily profit", GroupName = "Safety", Order = 32)]
        public double MaxDailyProfit { get; set; }
    }
}
