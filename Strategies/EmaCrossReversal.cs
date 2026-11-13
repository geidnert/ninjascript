#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public enum EmaCrossReversalExitLevel
    {
        Off,
        FastEma,
        SlowEma,
        FixedStop
    }

    public enum EmaCrossReversalExitConfirmation
    {
        Touch,
        CloseBeyond
    }

    public class EmaCrossReversal : Strategy
    {
        private const string LongEntrySignal = "EmaCrossReversalLong";
        private const string ShortEntrySignal = "EmaCrossReversalShort";
        private const string LongEarlyExitSignal = "EmaCrossReversalEarlyExitLong";
        private const string ShortEarlyExitSignal = "EmaCrossReversalEarlyExitShort";
        private const string LongSlopeExitSignal = "EmaCrossReversalSlopeExitLong";
        private const string ShortSlopeExitSignal = "EmaCrossReversalSlopeExitShort";
        private const string LongDailyExitSignal = "EmaCrossReversalDailyProfitLong";
        private const string ShortDailyExitSignal = "EmaCrossReversalDailyProfitShort";
        private const string ProfitTargetOrderName = "Profit target";
        private const string StopLossOrderName = "Stop loss";
        private const int SlopeLookback = 3;
        private const int SlopeAtrPeriod = 14;

        private EMA fastEma;
        private EMA slowEma;
        private ATR slopeAtr;
        private int previousRelationship;
        private int realtimeStartBar = -1;
        private int lastProcessedSignalBar = -1;
        private bool entryPending;
        private bool earlyExitPending;
        private bool earlyExitArmed;
        private bool entryPriceExitArmed;
        private bool skipFirstExitObservation;
        private bool orderFaulted;
        private string activeEntryOrderId;
        private int entryFilledQuantity;
        private MarketPosition activeEntrySide = MarketPosition.Flat;
        private DateTime activeEntryFillTime = DateTime.MinValue;
        private DateTime lastEarlyExitFillTime = DateTime.MinValue;
        private readonly List<DailyOrderProgress> dailyOrders = new List<DailyOrderProgress>();
        private TimeZoneInfo platformZone;
        private TimeZoneInfo easternZone;
        private DateTime dailyDay = DateTime.MinValue;
        private DateTime dailyMarketTime = DateTime.MinValue;
        private double dailyRealized;
        private double dailyAverage;
        private int dailyQuantity;
        private double dailyBid;
        private double dailyAsk;
        private bool dailyProfitReached;
        private bool dailyLiquidating;
        private bool dailyMissingBookReported;
        private bool dailyPositionWaitReported;
        private bool entrySubmissionInProgress;
        private bool dailyExitSubmitting;
        private string awaitingExitSignal;

        private sealed class DailyOrderProgress
        {
            public Order Order;
            public bool Entry;
            public bool ExitFence;
            public bool Terminal;
            public bool CancelRequested;
            public int ReportedFilled;
            public int Executed;
        }

        private bool UsesEmaExit => EarlyExitLevel == EmaCrossReversalExitLevel.FastEma
            || EarlyExitLevel == EmaCrossReversalExitLevel.SlowEma;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EmaCrossReversal";
                Description = "Research-only EMA crossover reversal with optional ATR-normalized slow-EMA slope filter, EMA exits, fixed point stop/target and entry-price protection. Chart-selected minute/second NQ/MNQ bars, no session-close exit.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                StopTargetHandling = StopTargetHandling.ByStrategyPosition;
                StartBehavior = StartBehavior.WaitUntilFlat;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = true;
                BarsRequiredToTrade = 26;
                FastEmaPeriod = 9;
                SlowEmaPeriod = 26;
                Contracts = 1;
                EarlyExitLevel = EmaCrossReversalExitLevel.Off;
                ExitConfirmation = EmaCrossReversalExitConfirmation.CloseBeyond;
                UseEntryPriceProtection = false;
                StopLossPoints = 0;
                TakeProfitPoints = 0;
                MinimumSlowEmaSlopeAtr = 0;
                MaxDailyProfit = 0;
            }
            else if (State == State.Configure)
            {
                if (FastEmaPeriod < 1 || SlowEmaPeriod <= FastEmaPeriod)
                    throw new InvalidOperationException("The fast EMA period must be positive and smaller than the slow EMA period.");
                if (Contracts < 1 || Contracts > 20)
                    throw new InvalidOperationException("Contracts must be between 1 and 20.");
                if (!Enum.IsDefined(typeof(EmaCrossReversalExitLevel), EarlyExitLevel)
                    || !Enum.IsDefined(typeof(EmaCrossReversalExitConfirmation), ExitConfirmation))
                    throw new InvalidOperationException("EmaCrossReversal requires valid early-exit settings.");
                if (double.IsNaN(StopLossPoints) || double.IsInfinity(StopLossPoints) || StopLossPoints < 0
                    || double.IsNaN(TakeProfitPoints) || double.IsInfinity(TakeProfitPoints) || TakeProfitPoints < 0)
                    throw new InvalidOperationException("EmaCrossReversal requires finite, non-negative stop/target points.");
                if (EarlyExitLevel == EmaCrossReversalExitLevel.FixedStop && StopLossPoints <= 0)
                    throw new InvalidOperationException("EmaCrossReversal requires positive Stop loss points in FixedStop mode.");
                if (double.IsNaN(MinimumSlowEmaSlopeAtr) || double.IsInfinity(MinimumSlowEmaSlopeAtr) || MinimumSlowEmaSlopeAtr < 0)
                    throw new InvalidOperationException("EmaCrossReversal requires a finite, non-negative minimum slow EMA slope.");
                if (double.IsNaN(MaxDailyProfit) || double.IsInfinity(MaxDailyProfit) || MaxDailyProfit < 0)
                    throw new InvalidOperationException("EmaCrossReversal requires a finite, non-negative daily profit cap.");

                BarsRequiredToTrade = SlowEmaPeriod;
                Calculate = UsesEmaExit || UseEntryPriceProtection || MaxDailyProfit > 0
                    ? Calculate.OnEachTick : Calculate.OnBarClose;
            }
            else if (State == State.DataLoaded)
            {
                if ((BarsPeriod.BarsPeriodType != BarsPeriodType.Minute
                    && BarsPeriod.BarsPeriodType != BarsPeriodType.Second) || BarsPeriod.Value < 1)
                    throw new InvalidOperationException("EmaCrossReversal requires an ordinary minute or second chart with a positive interval.");

                string root = Instrument.MasterInstrument.Name;
                if (!string.Equals(root, "NQ", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(root, "MNQ", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("EmaCrossReversal requires NQ or MNQ.");
                if (Math.Abs(StopLossPoints / TickSize - Math.Round(StopLossPoints / TickSize)) > 1e-8
                    || Math.Abs(TakeProfitPoints / TickSize - Math.Round(TakeProfitPoints / TickSize)) > 1e-8)
                    throw new InvalidOperationException("EmaCrossReversal requires stop/target points in whole instrument ticks.");
                if ((MaxDailyProfit > 0 || UseEntryPriceProtection || (UsesEmaExit
                    && ExitConfirmation == EmaCrossReversalExitConfirmation.Touch))
                    && IsHistoricalSimulation() && !Bars.IsTickReplay)
                    throw new InvalidOperationException("EmaCrossReversal requires Tick Replay for historical touch exits or daily-profit protection; use Market Replay Playback for fill verification.");

                if (MaxDailyProfit > 0)
                {
                    platformZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
                    easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                    if (platformZone == null || Instrument.MasterInstrument.PointValue <= 0
                        || double.IsNaN(Instrument.MasterInstrument.PointValue) || double.IsInfinity(Instrument.MasterInstrument.PointValue))
                        throw new InvalidOperationException("EmaCrossReversal daily-profit protection requires a platform time zone and positive instrument point value.");
                }

                fastEma = EMA(Close, FastEmaPeriod);
                slowEma = EMA(Close, SlowEmaPeriod);
                slopeAtr = MinimumSlowEmaSlopeAtr > 0 ? ATR(SlopeAtrPeriod) : null;
                fastEma.Plots[0].Brush = Brushes.DodgerBlue;
                slowEma.Plots[0].Brush = Brushes.Orange;
                AddChartIndicator(fastEma);
                AddChartIndicator(slowEma);
                previousRelationship = 0;
                realtimeStartBar = -1;
                lastProcessedSignalBar = -1;
                entryPending = false;
                earlyExitPending = false;
                earlyExitArmed = false;
                entryPriceExitArmed = false;
                skipFirstExitObservation = false;
                orderFaulted = false;
                activeEntryOrderId = null;
                entryFilledQuantity = 0;
                activeEntrySide = MarketPosition.Flat;
                activeEntryFillTime = DateTime.MinValue;
                lastEarlyExitFillTime = DateTime.MinValue;
                dailyOrders.Clear();
                dailyDay = DateTime.MinValue;
                dailyMarketTime = DateTime.MinValue;
                dailyRealized = 0;
                dailyAverage = 0;
                dailyQuantity = 0;
                dailyBid = dailyAsk = 0;
                dailyProfitReached = dailyLiquidating = false;
                dailyMissingBookReported = dailyPositionWaitReported = false;
                entrySubmissionInProgress = dailyExitSubmitting = false;
                awaitingExitSignal = null;
            }
            else if (State == State.Realtime)
            {
                realtimeStartBar = CurrentBar;
                lastProcessedSignalBar = -1;
                if (MaxDailyProfit > 0)
                {
                    dailyMarketTime = DateTime.MinValue;
                    dailyBid = dailyAsk = 0;
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            fastEma.Update();
            slowEma.Update();
            if (slopeAtr != null)
                slopeAtr.Update();

            bool tickCallbacks = Calculate == Calculate.OnEachTick
                && (State == State.Realtime || Bars.IsTickReplay);
            if (!tickCallbacks || IsFirstTickOfBar)
                ProcessCompletedBar(tickCallbacks ? 1 : 0);

            if ((!UsesEmaExit && !UseEntryPriceProtection)
                || !CanTrade(CurrentBar))
                return;

            if (skipFirstExitObservation)
            {
                skipFirstExitObservation = false;
                return;
            }

            if (UseEntryPriceProtection)
                EvaluatePriceExit(Close[0], Position.AveragePrice, ref entryPriceExitArmed, true, true);

            if (UsesEmaExit)
            {
                bool touch = ExitConfirmation == EmaCrossReversalExitConfirmation.Touch;
                EvaluatePriceExit(Close[0], ExitLevelPrice(0), ref earlyExitArmed, touch, touch);
            }
        }

        private void ProcessCompletedBar(int barsAgo)
        {
            int signalBar = CurrentBar - barsAgo;
            if (signalBar < 0 || signalBar <= lastProcessedSignalBar)
                return;

            lastProcessedSignalBar = signalBar;
            int relationship = fastEma[barsAgo] > slowEma[barsAgo] ? 1
                : fastEma[barsAgo] < slowEma[barsAgo] ? -1 : 0;
            int priorRelationship = previousRelationship;

            // Equality does not reverse a trade or forget the side before the touch.
            if (relationship != 0)
                previousRelationship = relationship;

            if (!CanTrade(signalBar) || entryPending || earlyExitPending || orderFaulted)
                return;

            bool crossed = priorRelationship != 0 && relationship != 0
                && relationship != priorRelationship;
            if (crossed)
            {
                // Consume stale/pending crosses instead of entering later on mere alignment.
                if (Position.MarketPosition == MarketPosition.Flat
                    && Time[barsAgo] <= lastEarlyExitFillTime)
                    return;
                if (Position.MarketPosition != MarketPosition.Flat
                    && Time[barsAgo] <= activeEntryFillTime)
                    return;

                MarketPosition targetSide = relationship > 0 ? MarketPosition.Long : MarketPosition.Short;
                if (Position.MarketPosition == targetSide)
                    return;
                if (!SlopeAllowsEntry(relationship, barsAgo))
                {
                    if (Position.MarketPosition != MarketPosition.Flat)
                        RequestMarketExit(true);
                    return;
                }

                entryPending = true;
                string entrySignal = relationship > 0 ? LongEntrySignal : ShortEntrySignal;
                if (EarlyExitLevel == EmaCrossReversalExitLevel.FixedStop)
                    SetStopLoss(entrySignal, CalculationMode.Ticks, StopLossPoints / TickSize, false);
                if (TakeProfitPoints > 0)
                    SetProfitTarget(entrySignal, CalculationMode.Ticks, TakeProfitPoints / TickSize);
                // A managed opposite entry closes the old side and opens Contracts on the new side.
                entrySubmissionInProgress = true;
                try
                {
                    if (relationship > 0)
                        EnterLong(Contracts, entrySignal);
                    else
                        EnterShort(Contracts, entrySignal);
                }
                finally
                {
                    entrySubmissionInProgress = false;
                }
                if (MaxDailyProfit > 0)
                    FinishDailyLiquidation();
                return;
            }

            if (UsesEmaExit
                && ExitConfirmation == EmaCrossReversalExitConfirmation.CloseBeyond
                && Time[barsAgo] > activeEntryFillTime)
                EvaluatePriceExit(Close[barsAgo], ExitLevelPrice(barsAgo), ref earlyExitArmed, false, true);
        }

        private bool SlopeAllowsEntry(int direction, int barsAgo)
        {
            if (MinimumSlowEmaSlopeAtr == 0)
                return true;
            if (CurrentBar - barsAgo < SlopeLookback)
                return false;

            double atr = slopeAtr[barsAgo];
            return atr > 0 && direction * (slowEma[barsAgo] - slowEma[barsAgo + SlopeLookback])
                / (SlopeLookback * atr) >= MinimumSlowEmaSlopeAtr;
        }

        private bool IsHistoricalSimulation()
        {
            return Account == null
                || string.Equals(Account.Name, "Backtest", StringComparison.OrdinalIgnoreCase);
        }

        private bool CanTrade(int barIndex)
        {
            return barIndex >= BarsRequiredToTrade
                && (State != State.Historical || IsHistoricalSimulation())
                && (State != State.Realtime || barIndex > realtimeStartBar)
                && (MaxDailyProfit == 0 || (dailyMarketTime != DateTime.MinValue && DailyBookReady()
                    && !dailyProfitReached && !dailyLiquidating));
        }

        private double ExitLevelPrice(int barsAgo)
        {
            switch (EarlyExitLevel)
            {
                case EmaCrossReversalExitLevel.FastEma:
                    return fastEma[barsAgo];
                case EmaCrossReversalExitLevel.SlowEma:
                    return slowEma[barsAgo];
                default:
                    throw new InvalidOperationException("EmaCrossReversal requires an enabled exit level.");
            }
        }

        private void EvaluatePriceExit(double price, double level, ref bool armed, bool touch, bool allowExit)
        {
            if (entryPending || earlyExitPending || orderFaulted)
                return;

            MarketPosition side = Position.MarketPosition;
            if (side == MarketPosition.Flat)
            {
                armed = false;
                return;
            }
            if (side != activeEntrySide)
                return;

            if (!armed)
            {
                armed = side == MarketPosition.Long ? price > level : price < level;
                return;
            }

            bool reached = touch
                ? (side == MarketPosition.Long ? price <= level : price >= level)
                : (side == MarketPosition.Long ? price < level : price > level);
            if (!allowExit || !reached)
                return;

            RequestMarketExit(false);
        }

        private void RequestMarketExit(bool rejectedSlope)
        {
            MarketPosition side = Position.MarketPosition;
            if (side == MarketPosition.Flat || entryPending || earlyExitPending || orderFaulted)
                return;

            earlyExitPending = true;
            string signal = side == MarketPosition.Long
                ? (rejectedSlope ? LongSlopeExitSignal : LongEarlyExitSignal)
                : (rejectedSlope ? ShortSlopeExitSignal : ShortEarlyExitSignal);
            if (MaxDailyProfit > 0)
                awaitingExitSignal = signal;
            if (side == MarketPosition.Long)
                ExitLong(signal, LongEntrySignal);
            else
                ExitShort(signal, ShortEntrySignal);
        }

        private bool IsStrategyFlattenSignal(string signal)
        {
            return signal == LongEarlyExitSignal || signal == ShortEarlyExitSignal
                || signal == LongSlopeExitSignal || signal == ShortSlopeExitSignal
                || signal == LongDailyExitSignal || signal == ShortDailyExitSignal;
        }

        private bool IsManagedProtectionOrder(Order order)
        {
            return (order.Name == ProfitTargetOrderName || order.Name == StopLossOrderName)
                && (order.FromEntrySignal == LongEntrySignal || order.FromEntrySignal == ShortEntrySignal);
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            if (MaxDailyProfit > 0)
            {
                DailyOrderProgress progress = DailyProgress(order);
                progress.ReportedFilled = Math.Max(progress.ReportedFilled, filled);
                progress.Terminal = orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected;
                if (IsManagedProtectionOrder(order) && (orderState == OrderState.PartFilled || orderState == OrderState.Filled))
                    progress.ExitFence = true;
                if (order.Name == awaitingExitSignal)
                    awaitingExitSignal = null;
                if (error != ErrorCode.NoError)
                {
                    orderFaulted = true;
                    Print(string.Format("EmaCrossReversal|DAILY_ORDER_ERROR|time={0:O}|signal={1}|error={2}|comment={3}",
                        time, order.Name, error, comment));
                }
            }
            if (IsManagedProtectionOrder(order)
                && (orderState == OrderState.PartFilled || orderState == OrderState.Filled))
                earlyExitPending = true;

            if (orderState == OrderState.Rejected)
            {
                orderFaulted = true;
                Print(string.Format("EmaCrossReversal|ORDER_REJECTED|time={0:O}|signal={1}|error={2}|comment={3}",
                    time, order.Name, error, comment));
            }
            else if (orderState == OrderState.Cancelled)
            {
                if (order.Name == LongEntrySignal || order.Name == ShortEntrySignal)
                    entryPending = false;
                if (IsStrategyFlattenSignal(order.Name))
                    earlyExitPending = false;
                Print(string.Format("EmaCrossReversal|ORDER_CANCELLED|time={0:O}|signal={1}|filled={2}",
                    time, order.Name, filled));
            }
            if (MaxDailyProfit > 0)
                FinishDailyLiquidation();
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (MaxDailyProfit > 0)
            {
                DailyProgress(execution.Order).Executed += quantity;
                RecordDailyExecution(execution.Order.OrderAction, price, quantity, time);
            }
            string signal = execution.Order.Name;
            if (signal == LongEntrySignal || signal == ShortEntrySignal)
            {
                if (activeEntryOrderId != orderId)
                {
                    activeEntryOrderId = orderId;
                    entryFilledQuantity = 0;
                    earlyExitArmed = false;
                    entryPriceExitArmed = false;
                }

                activeEntrySide = signal == LongEntrySignal ? MarketPosition.Long : MarketPosition.Short;
                entryFilledQuantity += quantity;
                entryPending = entryFilledQuantity < execution.Order.Quantity;
                activeEntryFillTime = time;
                skipFirstExitObservation = true;
                if (UsesEmaExit
                    && !earlyExitArmed
                    && Position.MarketPosition == activeEntrySide)
                {
                    fastEma.Update();
                    slowEma.Update();
                    double level = ExitLevelPrice(0);
                    earlyExitArmed = activeEntrySide == MarketPosition.Long ? price > level : price < level;
                }
            }
            else if ((IsStrategyFlattenSignal(signal) || IsManagedProtectionOrder(execution.Order))
                && (marketPosition == MarketPosition.Flat || Position.MarketPosition == MarketPosition.Flat))
            {
                earlyExitPending = false;
                earlyExitArmed = false;
                entryPriceExitArmed = false;
                activeEntrySide = MarketPosition.Flat;
                activeEntryFillTime = DateTime.MinValue;
                lastEarlyExitFillTime = time;
            }
            if (MaxDailyProfit > 0)
            {
                CheckDailyProfit();
                FinishDailyLiquidation();
            }
        }

        protected override void OnMarketData(MarketDataEventArgs update)
        {
            if (MaxDailyProfit == 0 || (State == State.Historical && !IsHistoricalSimulation()))
                return;
            if (update.MarketDataType != MarketDataType.Bid && update.MarketDataType != MarketDataType.Ask
                && update.MarketDataType != MarketDataType.Last)
                return;

            AdvanceDailyDay(update.Time);
            if (update.MarketDataType == MarketDataType.Bid)
                dailyBid = update.Price;
            else if (update.MarketDataType == MarketDataType.Ask)
                dailyAsk = update.Price;
            else
            {
                dailyBid = update.Bid;
                dailyAsk = update.Ask;
            }
            if (!DailyBookReady())
            {
                if (!dailyMissingBookReported)
                    Print("EmaCrossReversal|DAILY_BOOK_MISSING|daily-profit marking and new entries wait for valid bid/ask; native protection remains working.");
                dailyMissingBookReported = true;
            }
            else if (dailyMissingBookReported)
            {
                dailyMissingBookReported = false;
                Print("EmaCrossReversal|DAILY_BOOK_RESTORED");
            }
            CheckDailyProfit();
            FinishDailyLiquidation();
        }

        private DateTime AdvanceDailyDay(DateTime time)
        {
            DateTime et = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), platformZone, easternZone);
            DateTime key = et.AddHours(6).Date;
            if (dailyMarketTime == DateTime.MinValue || time > dailyMarketTime)
                dailyMarketTime = time;
            if (key > dailyDay)
            {
                dailyDay = key;
                dailyRealized = 0;
                dailyProfitReached = false;
                Print(string.Format("EmaCrossReversal|DAILY_RESET|time={0:O}|day={1:yyyy-MM-dd}|cleanupPending={2}", time, key, dailyLiquidating));
            }
            return key;
        }

        private bool DailyBookReady()
        {
            return dailyBid > 0 && dailyAsk >= dailyBid && !double.IsInfinity(dailyBid)
                && !double.IsInfinity(dailyAsk) && !double.IsNaN(dailyBid) && !double.IsNaN(dailyAsk);
        }

        private DailyOrderProgress DailyProgress(Order order)
        {
            for (int i = 0; i < dailyOrders.Count; i++)
                if (dailyOrders[i].Order == order || (!string.IsNullOrEmpty(order.OrderId)
                    && dailyOrders[i].Order.OrderId == order.OrderId))
                {
                    dailyOrders[i].Order = order;
                    return dailyOrders[i];
                }
            DailyOrderProgress progress = new DailyOrderProgress
            {
                Order = order,
                Entry = order.Name == LongEntrySignal || order.Name == ShortEntrySignal,
                ExitFence = IsStrategyFlattenSignal(order.Name) || order.Name == "Close position"
            };
            dailyOrders.Add(progress);
            return progress;
        }

        private void RecordDailyExecution(OrderAction action, double price, int quantity, DateTime time)
        {
            DateTime key = AdvanceDailyDay(time);
            int change = (action == OrderAction.Buy || action == OrderAction.BuyToCover) ? quantity : -quantity;
            int closed = dailyQuantity * change < 0 ? Math.Min(Math.Abs(dailyQuantity), quantity) : 0;
            if (key == dailyDay)
                dailyRealized += Math.Sign(dailyQuantity) * (price - dailyAverage) * closed * Instrument.MasterInstrument.PointValue;
            else
                Print(string.Format("EmaCrossReversal|DAILY_LATE_EXECUTION|time={0:O}|day={1:yyyy-MM-dd}|currentDay={2:yyyy-MM-dd}", time, key, dailyDay));

            int next = dailyQuantity + change;
            if (next == 0)
                dailyAverage = 0;
            else if (dailyQuantity == 0 || Math.Sign(next) != Math.Sign(dailyQuantity))
                dailyAverage = price;
            else if (closed == 0)
                dailyAverage = (dailyAverage * Math.Abs(dailyQuantity) + price * quantity) / Math.Abs(next);
            dailyQuantity = next;
        }

        private void CheckDailyProfit()
        {
            if (dailyProfitReached || orderFaulted)
                return;
            if (dailyQuantity != 0 && !DailyBookReady())
                return;
            double open = dailyQuantity == 0 ? 0 : dailyQuantity
                * ((dailyQuantity > 0 ? dailyBid : dailyAsk) - dailyAverage) * Instrument.MasterInstrument.PointValue;
            if (dailyRealized + open < MaxDailyProfit)
                return;
            dailyProfitReached = dailyLiquidating = true;
            Print(string.Format("EmaCrossReversal|DAILY_PROFIT_REACHED|day={0:yyyy-MM-dd}|realized={1:F2}|open={2:F2}|cap={3:F2}|no entries until18:00ET",
                dailyDay, dailyRealized, open, MaxDailyProfit));
        }

        private void FinishDailyLiquidation()
        {
            if (!dailyLiquidating || orderFaulted || dailyExitSubmitting)
                return;
            bool unsettled = entrySubmissionInProgress;
            for (int i = 0; i < dailyOrders.Count; i++)
            {
                DailyOrderProgress progress = dailyOrders[i];
                if (!progress.Entry)
                    continue;
                if (!progress.Terminal)
                {
                    unsettled = true;
                    if (!progress.CancelRequested && (progress.Order.OrderState == OrderState.Submitted
                        || progress.Order.OrderState == OrderState.Accepted || progress.Order.OrderState == OrderState.Working
                        || progress.Order.OrderState == OrderState.PartFilled))
                    {
                        progress.CancelRequested = true;
                        try { CancelOrder(progress.Order); }
                        catch (InvalidOperationException ex)
                        {
                            orderFaulted = true;
                            Print("EmaCrossReversal|DAILY_CANCEL_ERROR|" + ex.Message + "|CHECK STRATEGY POSITION");
                            return;
                        }
                    }
                }
                if (progress.Executed < progress.ReportedFilled)
                    unsettled = true;
            }
            if (unsettled || awaitingExitSignal != null)
                return;
            entryPending = false;
            for (int i = 0; i < dailyOrders.Count; i++)
                if (dailyOrders[i].ExitFence && (!dailyOrders[i].Terminal
                    || dailyOrders[i].Executed < dailyOrders[i].ReportedFilled))
                    return;
            earlyExitPending = false;
            if (dailyQuantity == 0 && Position.MarketPosition == MarketPosition.Flat)
            {
                if (!dailyProfitReached)
                    dailyLiquidating = false;
                return;
            }
            MarketPosition side = dailyQuantity > 0 ? MarketPosition.Long : MarketPosition.Short;
            if (dailyQuantity == 0 || Position.MarketPosition != side || Position.Quantity != Math.Abs(dailyQuantity))
            {
                if (!dailyPositionWaitReported)
                    Print("EmaCrossReversal|DAILY_POSITION_WAIT|waiting for position/execution settlement before flattening");
                dailyPositionWaitReported = true;
                return;
            }
            dailyPositionWaitReported = false;
            earlyExitPending = true;
            awaitingExitSignal = side == MarketPosition.Long ? LongDailyExitSignal : ShortDailyExitSignal;
            dailyExitSubmitting = true;
            try
            {
                if (side == MarketPosition.Long)
                    ExitLong(LongDailyExitSignal, LongEntrySignal);
                else
                    ExitShort(ShortDailyExitSignal, ShortEntrySignal);
            }
            finally { dailyExitSubmitting = false; }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Fast EMA period", Order = 1, GroupName = "Parameters")]
        public int FastEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "Slow EMA period", Order = 2, GroupName = "Parameters")]
        public int SlowEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Contracts", Order = 3, GroupName = "Parameters")]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Minimum slow EMA slope / ATR", Description = "0 disables. Directional slow-EMA change over 3 completed bars, divided by 3 x ATR14. Research candidate: 0.01 on 10-minute EMA34/124. A rejected opposite cross closes without reversing; wait for a fresh qualifying cross.", Order = 1, GroupName = "Entry filter")]
        public double MinimumSlowEmaSlopeAtr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry-price protection", Description = "Independent intrabar exit on return to actual average entry after a favorable observed move. Can run alongside either EMA exit. Market fills and fees can still produce a loss.", Order = 1, GroupName = "Optional early exit")]
        public bool UseEntryPriceProtection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Early-exit / stop mode", Description = "Choose Off, a fast/slow EMA exit, or FixedStop with Stop loss points. The fixed stop replaces the EMA exit. Entry-price protection remains independent.", Order = 2, GroupName = "Optional early exit")]
        public EmaCrossReversalExitLevel EarlyExitLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EMA exit confirmation", Description = "EMA exits only: Touch exits intrabar at/through the EMA; CloseBeyond requires a completed candle strictly beyond it. Entry-price protection always uses intrabar touch. Wait for a new EMA crossover after either exit.", Order = 3, GroupName = "Optional early exit")]
        public EmaCrossReversalExitConfirmation ExitConfirmation { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Stop loss points", Description = "Fixed price-point distance from average entry, used only in FixedStop mode. Must be positive in that mode and a whole number of instrument ticks.", Order = 1, GroupName = "Fixed TP / SL")]
        public double StopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Take profit points", Description = "0 disables the target. Positive points enable a managed profit target with any exit mode. After a target or stop fill, wait for a new completed EMA crossover.", Order = 2, GroupName = "Fixed TP / SL")]
        public double TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Max daily profit ($)", Description = "0 disables. This strategy run's realized gross since18:00 New York plus its own open profit at exit-side bid/ask, before commissions. Reaching the cap cancels its pending entries, closes its position at market and blocks entries until18:00 ET or restart. Fill slippage can finish below the cap.", Order = 1, GroupName = "Daily risk")]
        public double MaxDailyProfit { get; set; }
    }
}
