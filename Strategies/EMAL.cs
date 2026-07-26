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
        private ATR atr;
        private Order entryOrder;
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
                BarsRequiredToTrade = 1;

                EmaPeriod = 6;
                MinimumEmaSlopePoints = 0.75;
                TakeProfitPoints = 4.0;
                StopLossPoints = 13.0;
                Contracts = 1;
                EntryOrderType = EMALEntryOrderType.Limit;

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

                // Sunday entry windows, 18:00 - 24:00 New York time in 30-minute blocks.
                // All enabled by default: the master switch alone gives "Sunday evening only".
                RestrictToSundayWindows = true;
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
            }
            else if (State == State.DataLoaded)
            {
                ValidateChart();

                ema = EMA(EmaPeriod);
                atr = ATR(AtrPeriod);
                AddChartIndicator(ema);
            }
            else if (State == State.Realtime)
            {
                TransitionEntryOrderReferenceToRealtime();
            }
            else if (State == State.Terminated)
            {
                PrintFillRateSummary();
            }
        }

        private void PrintFillRateSummary()
        {
            if (signalCount == 0)
                return;

            int cancelled = cancelTimeoutCount + cancelMovedCount + cancelBarEndCount;

            Print("================ EMAL fill rate ================");
            Print(string.Format("  entry type          : {0}", EntryOrderType));
            Print(string.Format("  sunday filter       : {0}  (bars blocked: {1})",
                RestrictToSundayWindows, blockedBarCount));
            Print(string.Format("  enabled blocks      : {0}", EnabledBlocksSummary()));
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
                || IsHistoricalOrderAwaitingRealtimeTransition(entryOrder)
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

        private static readonly TimeSpan SundayWindowStart = new TimeSpan(18, 0, 0);

        private bool IsEntryWindowOpen()
        {
            if (!RestrictToSundayWindows)
                return true;

            // NinjaTrader stamps a bar with its CLOSING time, so the in-progress bar reads one
            // period ahead. Entries fire at the bar's open, so test against that instead or the
            // first minute of every window would be lost.
            DateTime barOpen = Time[0].AddMinutes(-BarsPeriod.Value);

            if (barOpen.DayOfWeek != DayOfWeek.Sunday)
                return false;

            return IsHalfHourEnabled(barOpen.TimeOfDay);
        }

        // Maps a time of day onto one of the twelve 30-minute blocks between 18:00 and 24:00.
        // Blocks are half-open: [start, start+30min), so 18:30 belongs to the 18:30 block.
        private bool IsHalfHourEnabled(TimeSpan timeOfDay)
        {
            if (timeOfDay < SundayWindowStart)
                return false;

            int block = (int)((timeOfDay - SundayWindowStart).TotalMinutes / 30.0);

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
                "18:00", "18:30", "19:00", "19:30", "20:00", "20:30",
                "21:00", "21:30", "22:00", "22:30", "23:00", "23:30"
            };

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            for (int i = 0; i < labels.Length; i++)
            {
                if (!IsHalfHourEnabled(SundayWindowStart + TimeSpan.FromMinutes(30 * i)))
                    continue;

                if (sb.Length > 0)
                    sb.Append(", ");

                sb.Append(labels[i]);
            }

            return sb.Length == 0 ? "(none)" : sb.ToString();
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

            if (CurrentBar < Math.Max(EmaPeriod, AtrPeriod) + 2)
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
            double requiredSlope = Math.Abs(MinimumEmaSlopePoints);

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

            SetProfitTarget(entrySignal, CalculationMode.Ticks, PointsToTicks(takeProfit));
            SetStopLoss(entrySignal, CalculationMode.Ticks, PointsToTicks(StopLossPoints), false);

            Print(string.Format(
                "{0} | {1} {2} | mode={3} | target={4:F2} pts stop={5:F2} pts | atr={6:F2}",
                Time[0],
                direction > 0 ? "LONG" : "SHORT",
                EntryOrderType,
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

        private void TransitionEntryOrderReferenceToRealtime()
        {
            if (State != State.Realtime
                || entryOrder == null
                || !entryOrder.IsBacktestOrder)
            {
                return;
            }

            entryOrder = GetRealtimeOrder(entryOrder);
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
            // Convert the stored reference before any realtime cancel can use it.
            TransitionEntryOrderReferenceToRealtime();

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
                filledCount++;
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
        [Display(Name = "Restrict To Sunday Windows", Description = "Master switch. When on, new entries are allowed only on Sunday and only inside the enabled 30-minute blocks below. Exits are never affected.", GroupName = "Sunday Entry Windows", Order = 0)]
        public bool RestrictToSundayWindows { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "18:00 - 18:30", GroupName = "Sunday Entry Windows", Order = 1)]
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
}
