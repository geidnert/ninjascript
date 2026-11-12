#region Using declarations
using System;
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
        private const string ProfitTargetOrderName = "Profit target";
        private const string StopLossOrderName = "Stop loss";

        private EMA fastEma;
        private EMA slowEma;
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

        private bool UsesEmaExit => EarlyExitLevel == EmaCrossReversalExitLevel.FastEma
            || EarlyExitLevel == EmaCrossReversalExitLevel.SlowEma;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EmaCrossReversal";
                Description = "Research-only EMA crossover reversal with optional EMA exits, fixed point stop/target and independent entry-price protection. Chart-selected minute/second NQ/MNQ bars, EMA9/26, no session-close exit.";
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

                BarsRequiredToTrade = SlowEmaPeriod;
                Calculate = UsesEmaExit || UseEntryPriceProtection
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
                if ((UseEntryPriceProtection || (UsesEmaExit
                    && ExitConfirmation == EmaCrossReversalExitConfirmation.Touch))
                    && IsHistoricalSimulation() && !Bars.IsTickReplay)
                    throw new InvalidOperationException("EmaCrossReversal requires Tick Replay for historical touch exits; use Market Replay Playback for fill verification.");

                fastEma = EMA(Close, FastEmaPeriod);
                slowEma = EMA(Close, SlowEmaPeriod);
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
            }
            else if (State == State.Realtime)
            {
                realtimeStartBar = CurrentBar;
                lastProcessedSignalBar = -1;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            fastEma.Update();
            slowEma.Update();

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

                entryPending = true;
                string entrySignal = relationship > 0 ? LongEntrySignal : ShortEntrySignal;
                if (EarlyExitLevel == EmaCrossReversalExitLevel.FixedStop)
                    SetStopLoss(entrySignal, CalculationMode.Ticks, StopLossPoints / TickSize, false);
                if (TakeProfitPoints > 0)
                    SetProfitTarget(entrySignal, CalculationMode.Ticks, TakeProfitPoints / TickSize);
                // A managed opposite entry closes the old side and opens Contracts on the new side.
                if (relationship > 0)
                    EnterLong(Contracts, entrySignal);
                else
                    EnterShort(Contracts, entrySignal);
                return;
            }

            if (UsesEmaExit
                && ExitConfirmation == EmaCrossReversalExitConfirmation.CloseBeyond
                && Time[barsAgo] > activeEntryFillTime)
                EvaluatePriceExit(Close[barsAgo], ExitLevelPrice(barsAgo), ref earlyExitArmed, false, true);
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
                && (State != State.Realtime || barIndex > realtimeStartBar);
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

            earlyExitPending = true;
            if (side == MarketPosition.Long)
                ExitLong(LongEarlyExitSignal, LongEntrySignal);
            else
                ExitShort(ShortEarlyExitSignal, ShortEntrySignal);
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
                if (order.Name == LongEarlyExitSignal || order.Name == ShortEarlyExitSignal)
                    earlyExitPending = false;
                Print(string.Format("EmaCrossReversal|ORDER_CANCELLED|time={0:O}|signal={1}|filled={2}",
                    time, order.Name, filled));
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

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
            else if ((signal == LongEarlyExitSignal || signal == ShortEarlyExitSignal
                || IsManagedProtectionOrder(execution.Order))
                && (marketPosition == MarketPosition.Flat || Position.MarketPosition == MarketPosition.Flat))
            {
                earlyExitPending = false;
                earlyExitArmed = false;
                entryPriceExitArmed = false;
                activeEntrySide = MarketPosition.Flat;
                activeEntryFillTime = DateTime.MinValue;
                lastEarlyExitFillTime = time;
            }
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
    }
}
