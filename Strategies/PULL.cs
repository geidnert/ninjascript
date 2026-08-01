#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class PULL : Strategy
    {
        private const string StrategySignalPrefix = "PULL";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";
        private const string SessionEndExitSignal = StrategySignalPrefix + "SessionEnd";
        private const string BreakExitSignal = StrategySignalPrefix + "Break";

        private EMA ema;
        private Order entryOrder;
        private Order terminalExitOrder;
        private bool hasPendingSetup;
        private int pendingDirection;
        private double pendingEntryPrice;
        private double pendingStopPrice;
        private double pendingTargetPrice;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Long/short impulse-candle pullback strategy. An unusually large directional candle creates a limit entry inside its range, with the stop beyond its stop-side extreme and the target beyond its target-side extreme.";
                Name = "PULL";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 1;

                EmaPeriod = 20;
                ComparisonBars = 10;
                MinimumRangeMultiple = 2.0;
                MaximumRangeMultiple = 4.0;
                MinimumBullishBodyPercent = 50.0;
                EntryPercent = 50.0;
                StopPaddingPoints = 0.0;
                TargetPaddingPoints = 0.0;
                Contracts = 1;
                SessionStart = TimeSpan.Zero;
                SessionStop = new TimeSpan(23, 59, 59);
                BreakStart = new TimeSpan(9, 0, 0);
                BreakStop = new TimeSpan(11, 0, 0);
            }
            else if (State == State.DataLoaded)
            {
                ValidateConfiguration();

                ema = EMA(EmaPeriod);
                AddChartIndicator(ema);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            DateTime gateTime = GetTimeGateTime(Time[0]);
            bool inSession = SessionStart != SessionStop
                && IsTimeInRange(gateTime.TimeOfDay, SessionStart, SessionStop);
            bool inBreak = BreakStart != BreakStop
                && IsBreakTime(gateTime.TimeOfDay, BreakStart, BreakStop);

            if (!inSession || inBreak)
            {
                CancelPendingEntry();
                TrySubmitTerminalExit(inBreak ? BreakExitSignal : SessionEndExitSignal);
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                ClearPendingSetup();
                CancelEntryOrderIfActive();
                return;
            }

            bool pendingTargetReached = hasPendingSetup
                && (pendingDirection > 0
                    ? Close[0] >= pendingTargetPrice
                    : Close[0] <= pendingTargetPrice);

            if (pendingTargetReached && IsOrderActive(entryOrder))
            {
                Print(string.Format(
                    "{0} | {1} pending {2} entry cancelled because price reached the impulse target first ({3:F2}).",
                    Time[0],
                    StrategySignalPrefix,
                    pendingDirection > 0 ? "long" : "short",
                    pendingTargetPrice));
                CancelPendingEntry();
                return;
            }

            if (!IsFirstTickOfBar)
                return;

            if (IsOrderActive(entryOrder) || IsOrderActive(terminalExitOrder))
                return;

            int requiredBars = Math.Max(EmaPeriod, ComparisonBars + 1) + 1;
            if (CurrentBar < requiredBars)
                return;

            TryCreateImpulseSetup();
        }

        private void TryCreateImpulseSetup()
        {
            double impulseRange = High[1] - Low[1];
            if (impulseRange <= 0.0)
                return;

            double directionalBodyPercent = Math.Abs(Close[1] - Open[1]) / impulseRange * 100.0;
            bool hasDirectionalBody = directionalBodyPercent >= MinimumBullishBodyPercent;
            bool isLongSetup = Close[1] > Open[1] && Close[1] > ema[1];
            bool isShortSetup = Close[1] < Open[1] && Close[1] < ema[1];

            if (!hasDirectionalBody || (!isLongSetup && !isShortSetup))
                return;

            int direction = isLongSetup ? 1 : -1;

            double averagePriorRange = 0.0;
            for (int barsAgo = 2; barsAgo <= ComparisonBars + 1; barsAgo++)
                averagePriorRange += High[barsAgo] - Low[barsAgo];

            averagePriorRange /= ComparisonBars;
            if (averagePriorRange <= 0.0)
                return;

            double rangeMultiple = impulseRange / averagePriorRange;
            bool passesMinimum = rangeMultiple >= MinimumRangeMultiple;
            bool passesMaximum = MaximumRangeMultiple <= 0.0 || rangeMultiple <= MaximumRangeMultiple;

            if (!passesMinimum || !passesMaximum)
                return;

            double entryPrice = direction > 0
                ? High[1] - impulseRange * EntryPercent / 100.0
                : Low[1] + impulseRange * EntryPercent / 100.0;
            double stopPrice = direction > 0
                ? Low[1] - StopPaddingPoints
                : High[1] + StopPaddingPoints;
            double targetPrice = direction > 0
                ? High[1] + TargetPaddingPoints
                : Low[1] - TargetPaddingPoints;

            entryPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice);
            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);

            bool priceAlreadyBeyondSetup = direction > 0
                ? Close[0] > targetPrice || Close[0] < stopPrice
                : Close[0] < targetPrice || Close[0] > stopPrice;

            if (priceAlreadyBeyondSetup)
                return;

            bool pricesAreOrdered = direction > 0
                ? stopPrice < entryPrice && entryPrice < targetPrice
                : targetPrice < entryPrice && entryPrice < stopPrice;

            if (!pricesAreOrdered)
            {
                Print(string.Format(
                    "{0} | {1} {2} impulse skipped because its stop/entry/target prices are not ordered correctly. Stop={3:F2}, Entry={4:F2}, Target={5:F2}.",
                    Time[0],
                    StrategySignalPrefix,
                    direction > 0 ? "long" : "short",
                    stopPrice,
                    entryPrice,
                    targetPrice));
                return;
            }

            hasPendingSetup = true;
            pendingDirection = direction;
            pendingEntryPrice = entryPrice;
            pendingStopPrice = stopPrice;
            pendingTargetPrice = targetPrice;

            string entrySignal = direction > 0 ? LongEntrySignal : ShortEntrySignal;
            SetStopLoss(entrySignal, CalculationMode.Price, pendingStopPrice, false);
            SetProfitTarget(entrySignal, CalculationMode.Price, pendingTargetPrice);

            Print(string.Format(
                "{0} | {1} {2} impulse detected | range={3:F2}, prior-average={4:F2}, multiple={5:F2}, entry={6:F2}, stop={7:F2}, target={8:F2}.",
                Time[1],
                StrategySignalPrefix,
                direction > 0 ? "long" : "short",
                impulseRange,
                averagePriorRange,
                rangeMultiple,
                pendingEntryPrice,
                pendingStopPrice,
                pendingTargetPrice));

            if (direction > 0)
                EnterLongLimit(0, true, Contracts, pendingEntryPrice, LongEntrySignal);
            else
                EnterShortLimit(0, true, Contracts, pendingEntryPrice, ShortEntrySignal);
        }

        private void CancelPendingEntry()
        {
            ClearPendingSetup();
            CancelEntryOrderIfActive();
        }

        private void CancelEntryOrderIfActive()
        {
            if (IsOrderActive(entryOrder))
                CancelOrder(entryOrder);
        }

        private void TrySubmitTerminalExit(string exitSignal)
        {
            if (Position.MarketPosition == MarketPosition.Flat || IsOrderActive(terminalExitOrder))
                return;

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(0, Position.Quantity, exitSignal, LongEntrySignal);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(0, Position.Quantity, exitSignal, ShortEntrySignal);
        }

        private DateTime GetTimeGateTime(DateTime time)
        {
            if (State == State.Historical
                && BarsPeriod != null
                && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute
                && BarsPeriod.Value > 0)
            {
                return time.AddMinutes(BarsPeriod.Value);
            }

            return time;
        }

        private static bool IsTimeInRange(TimeSpan current, TimeSpan start, TimeSpan stop)
        {
            if (start < stop)
                return current >= start && current < stop;

            return current >= start || current < stop;
        }

        private static bool IsBreakTime(TimeSpan current, TimeSpan start, TimeSpan stop)
        {
            if (start < stop)
                return current >= start && current <= stop;

            return current >= start || current <= stop;
        }

        private static bool IsOrderActive(Order order)
        {
            return order != null
                && order.OrderState != OrderState.Cancelled
                && order.OrderState != OrderState.Filled
                && order.OrderState != OrderState.Rejected;
        }

        private void ClearPendingSetup()
        {
            hasPendingSetup = false;
            pendingDirection = 0;
            pendingEntryPrice = 0.0;
            pendingStopPrice = 0.0;
            pendingTargetPrice = 0.0;
        }

        private void ValidateConfiguration()
        {
            if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value < 1)
                throw new InvalidOperationException("PULL must be applied to a minute chart.");

            string instrumentName = Instrument == null || Instrument.MasterInstrument == null
                ? string.Empty
                : Instrument.MasterInstrument.Name;

            if (!string.Equals(instrumentName, "NQ", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(instrumentName, "MNQ", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("PULL supports NQ and MNQ only.");
            }

            if (MaximumRangeMultiple > 0.0 && MaximumRangeMultiple < MinimumRangeMultiple)
                throw new InvalidOperationException("PULL Maximum Range Multiple must be zero (disabled) or greater than or equal to Minimum Range Multiple.");
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            bool isEntryOrder = order.Name == LongEntrySignal || order.Name == ShortEntrySignal;
            bool isTerminalExitOrder = order.Name == SessionEndExitSignal || order.Name == BreakExitSignal;

            if (isTerminalExitOrder)
            {
                if (orderState != OrderState.Cancelled
                    && orderState != OrderState.Filled
                    && orderState != OrderState.Rejected)
                {
                    terminalExitOrder = order;
                }
                else
                {
                    terminalExitOrder = null;
                }

                if (orderState == OrderState.Rejected)
                    PrintOrderRejection(time, order, error, comment);

                return;
            }

            if (!isEntryOrder)
                return;

            if (orderState != OrderState.Cancelled
                && orderState != OrderState.Filled
                && orderState != OrderState.Rejected)
            {
                entryOrder = order;
            }
            else
            {
                entryOrder = null;
                ClearPendingSetup();

                if (orderState == OrderState.Rejected)
                    PrintOrderRejection(time, order, error, comment);
            }
        }

        private void PrintOrderRejection(DateTime time, Order order, ErrorCode error, string comment)
        {
            Print(string.Format(
                "{0} | {1} order rejected | error={2} comment={3}",
                time,
                order.Name,
                error,
                comment ?? string.Empty));
        }

        [Range(7, 100), NinjaScriptProperty]
        [Display(Name = "EMA Period", Description = "EMA used for directional bias. Long impulses close above it; short impulses close below it.", GroupName = "Parameters", Order = 0)]
        public int EmaPeriod { get; set; }

        [Range(1, 100), NinjaScriptProperty]
        [Display(Name = "Comparison Bars", Description = "Number of candles immediately before the impulse candle used to calculate average range.", GroupName = "Parameters", Order = 1)]
        public int ComparisonBars { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Minimum Range Multiple", Description = "Impulse range must be at least this multiple of the average prior-candle range.", GroupName = "Parameters", Order = 2)]
        public double MinimumRangeMultiple { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Maximum Range Multiple", Description = "Impulse range must not exceed this multiple. Set to 0 to disable the maximum.", GroupName = "Parameters", Order = 3)]
        public double MaximumRangeMultiple { get; set; }

        [Range(0.0, 100.0), NinjaScriptProperty]
        [Display(Name = "Minimum Directional Body (%)", Description = "Minimum bullish or bearish body size as a percentage of the impulse candle's full high-low range.", GroupName = "Parameters", Order = 4)]
        public double MinimumBullishBodyPercent { get; set; }

        [Range(0.0, 100.0), NinjaScriptProperty]
        [Display(Name = "Entry Percent", Description = "Limit location from the directional extreme: 0 = high for longs/low for shorts, 50 = midpoint, 100 = opposite extreme.", GroupName = "Parameters", Order = 5)]
        public double EntryPercent { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Stop Padding (points)", Description = "Extra points beyond the impulse low for longs or high for shorts.", GroupName = "Parameters", Order = 6)]
        public double StopPaddingPoints { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Target Padding (points)", Description = "Extra points beyond the impulse high for longs or low for shorts.", GroupName = "Parameters", Order = 7)]
        public double TargetPaddingPoints { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Contracts", Description = "Number of contracts per entry.", GroupName = "Parameters", Order = 8)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "Daily trading start in chart time.", GroupName = "Schedule", Order = 0)]
        public TimeSpan SessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Stop", Description = "Daily trading stop in chart time. Equal start and stop disables the session.", GroupName = "Schedule", Order = 1)]
        public TimeSpan SessionStop { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break Start", Description = "Daily break start in chart time. Equal start and stop disables the break.", GroupName = "Schedule", Order = 2)]
        public TimeSpan BreakStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break Stop", Description = "Daily break stop in chart time. The break cancels pending entries and closes open positions.", GroupName = "Schedule", Order = 3)]
        public TimeSpan BreakStop { get; set; }
    }
}
