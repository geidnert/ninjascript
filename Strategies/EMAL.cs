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
        private const string SessionEndExitSignal = StrategySignalPrefix + "SessionEnd";
        private const string BreakExitSignal = StrategySignalPrefix + "Break";

        private EMA ema;
        private Order entryOrder;
        private Order terminalExitOrder;
        private int queuedDirection;
        private double queuedLimitPrice;
        private int queuedEntryBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "One-minute EMA direction strategy with market or passive bid/ask limit entries, a daily session, and a flattening break.";
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
                TakeProfitPoints = 4.0;
                StopLossPoints = 10.0;
                Contracts = 1;
                EntryOrderType = EMALEntryOrderType.Market;
                SessionStart = TimeSpan.Zero;
                SessionStop = new TimeSpan(23, 59, 59);
                BreakStart = new TimeSpan(9, 0, 0);
                BreakStop = new TimeSpan(11, 0, 0);
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
            if (BarsInProgress != 0)
                return;

            DateTime gateTime = GetTimeGateTime(Time[0]);
            bool inSession = SessionStart != SessionStop
                && IsTimeInRange(gateTime.TimeOfDay, SessionStart, SessionStop);
            bool inBreak = BreakStart != BreakStop
                && IsBreakTime(gateTime.TimeOfDay, BreakStart, BreakStop);

            if (!inSession || inBreak)
            {
                ClearQueuedEntry();
                CancelEntryOrderIfActive();
                TrySubmitTerminalExit(inBreak ? BreakExitSignal : SessionEndExitSignal);
                return;
            }

            if (!IsFirstTickOfBar)
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

            if (longSignal)
                QueueEntry(1);
            else if (shortSignal)
                QueueEntry(-1);

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
                || IsOrderActive(entryOrder)
                || IsOrderActive(terminalExitOrder))
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

                if (orderState == OrderState.Filled)
                {
                    ClearQueuedEntry();
                }
                else if (orderState == OrderState.Cancelled)
                {
                    TrySubmitQueuedEntry();
                }
                else
                {
                    ClearQueuedEntry();
                    PrintOrderRejection(time, order, error, comment);
                }
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

    public enum EMALEntryOrderType
    {
        Market,
        Limit
    }
}
