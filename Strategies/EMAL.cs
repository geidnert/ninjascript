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
    public class EMAL : Strategy
    {
        private const string StrategySignalPrefix = "EMAL";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";

        private EMA ema;
        private Order entryOrder;
        private int queuedDirection;
        private double queuedLimitPrice;
        private int queuedEntryBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "One-minute EMA direction strategy with market or passive bid/ask limit entries. "
                    + "ReverseSignals inverts entry direction; combined with swapped TP/SL distances this is the "
                    + "exact per-trade mirror of the original logic.";
                Name = "EMAL";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 1;

                EmaPeriod = 6;
                MinimumEmaSlopePoints = 0.25;
                // Mirror of the original 3 TP / 10 SL configuration.
                TakeProfitPoints = 10.0;
                StopLossPoints = 3.0;
                Contracts = 1;
                EntryOrderType = EMALEntryOrderType.Market;
                ReverseSignals = true;
            }
            else if (State == State.DataLoaded)
            {
                ValidateChart();

                ema = EMA(EmaPeriod);
                AddChartIndicator(ema);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || !IsFirstTickOfBar)
                return;

            ClearQueuedEntry();

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                CancelEntryOrderIfActive();
                return;
            }

            if (CurrentBar < EmaPeriod + 2)
            {
                CancelEntryOrderIfActive();
                return;
            }

            double currentPrice = Close[0];
            double completedEma = ema[1];
            double completedEmaSlope = ema[1] - ema[2];
            double requiredSlope = Math.Abs(MinimumEmaSlopePoints);

            bool longSignal = currentPrice > completedEma
                && completedEmaSlope > 0.0
                && completedEmaSlope >= requiredSlope;
            bool shortSignal = currentPrice < completedEma
                && completedEmaSlope < 0.0
                && completedEmaSlope <= -requiredSlope;

            // ReverseSignals inverts the entry direction: a long signal is taken as a short and
            // vice versa. Combined with swapped TakeProfitPoints/StopLossPoints, each trade exits
            // on exactly the barrier the original logic would have exited on.
            int signalDirection = longSignal ? 1 : (shortSignal ? -1 : 0);

            if (signalDirection != 0)
                QueueEntry(ReverseSignals ? -signalDirection : signalDirection);

            if (IsOrderActive(entryOrder))
            {
                CancelOrder(entryOrder);
                return;
            }

            TrySubmitQueuedEntry();
        }

        private void QueueEntry(int direction)
        {
            queuedDirection = direction;
            queuedEntryBar = CurrentBar;

            if (EntryOrderType == EMALEntryOrderType.Limit)
                queuedLimitPrice = GetPassiveLimitPrice(direction);
        }

        private void TrySubmitQueuedEntry()
        {
            if (queuedDirection == 0
                || queuedEntryBar != CurrentBar
                || Position.MarketPosition != MarketPosition.Flat
                || IsOrderActive(entryOrder))
            {
                return;
            }

            int direction = queuedDirection;
            double limitPrice = queuedLimitPrice;
            ClearQueuedEntry();

            string entrySignal = direction > 0 ? LongEntrySignal : ShortEntrySignal;
            SetProfitTarget(entrySignal, CalculationMode.Ticks, PointsToTicks(TakeProfitPoints));
            SetStopLoss(entrySignal, CalculationMode.Ticks, PointsToTicks(StopLossPoints), false);

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
            double price = direction > 0 ? GetCurrentBid() : GetCurrentAsk();

            if (price <= 0.0 || double.IsNaN(price))
                price = Close[0];

            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        private int PointsToTicks(double points)
        {
            return Math.Max(1, (int)Math.Round(
                Math.Abs(points) / TickSize,
                MidpointRounding.AwayFromZero));
        }

        private void CancelEntryOrderIfActive()
        {
            if (IsOrderActive(entryOrder))
                CancelOrder(entryOrder);
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
            if (order == null
                || (order.Name != LongEntrySignal && order.Name != ShortEntrySignal))
            {
                return;
            }

            if (orderState != OrderState.Cancelled
                && orderState != OrderState.Filled
                && orderState != OrderState.Rejected)
            {
                entryOrder = order;
            }
            else if (orderState == OrderState.Filled)
            {
                entryOrder = null;
                ClearQueuedEntry();
            }
            else if (orderState == OrderState.Cancelled)
            {
                entryOrder = null;
                TrySubmitQueuedEntry();
            }
            else if (orderState == OrderState.Rejected)
            {
                entryOrder = null;
                ClearQueuedEntry();
                Print(string.Format(
                    "{0} | {1} entry rejected | error={2} comment={3}",
                    time,
                    order.Name,
                    error,
                    comment ?? string.Empty));
            }
        }

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
        [Display(Name = "Entry Order Type", Description = "Market, or passive limit at bid for longs and ask for shorts.", GroupName = "Parameters", Order = 5)]
        public EMALEntryOrderType EntryOrderType { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reverse Signals", Description = "Invert entry direction: take shorts on long signals and longs on short signals.", GroupName = "Parameters", Order = 6)]
        public bool ReverseSignals { get; set; }
    }

    public enum EMALEntryOrderType
    {
        Market,
        Limit
    }
}
