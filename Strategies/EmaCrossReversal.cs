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
        EntryPrice
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

        private EMA fastEma;
        private EMA slowEma;
        private int previousRelationship;
        private int realtimeStartBar = -1;
        private int lastProcessedSignalBar = -1;
        private bool entryPending;
        private bool earlyExitPending;
        private bool earlyExitArmed;
        private bool skipFirstExitObservation;
        private bool orderFaulted;
        private string activeEntryOrderId;
        private int entryFilledQuantity;
        private MarketPosition activeEntrySide = MarketPosition.Flat;
        private DateTime activeEntryFillTime = DateTime.MinValue;
        private DateTime lastEarlyExitFillTime = DateTime.MinValue;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EmaCrossReversal";
                Description = "Research-only EMA crossover reversal with optional EMA or entry-price exits. Five-minute NQ/MNQ, EMA9/26, no stop, target, or session-close exit.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
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

                BarsRequiredToTrade = SlowEmaPeriod;
                Calculate = EarlyExitLevel == EmaCrossReversalExitLevel.Off
                    ? Calculate.OnBarClose : Calculate.OnEachTick;
            }
            else if (State == State.DataLoaded)
            {
                if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 5)
                    throw new InvalidOperationException("EmaCrossReversal requires an ordinary five-minute chart.");

                string root = Instrument.MasterInstrument.Name;
                if (!string.Equals(root, "NQ", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(root, "MNQ", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("EmaCrossReversal requires NQ or MNQ.");
                if (EarlyExitLevel != EmaCrossReversalExitLevel.Off
                    && ExitConfirmation == EmaCrossReversalExitConfirmation.Touch
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

            if (EarlyExitLevel == EmaCrossReversalExitLevel.Off
                || !CanTrade(CurrentBar))
                return;

            if (skipFirstExitObservation)
            {
                skipFirstExitObservation = false;
                return;
            }

            EvaluatePriceExit(Close[0], ExitLevelPrice(0),
                ExitConfirmation == EmaCrossReversalExitConfirmation.Touch);
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
                // A managed opposite entry closes the old side and opens Contracts on the new side.
                if (relationship > 0)
                    EnterLong(Contracts, LongEntrySignal);
                else
                    EnterShort(Contracts, ShortEntrySignal);
                return;
            }

            if (EarlyExitLevel != EmaCrossReversalExitLevel.Off
                && ExitConfirmation == EmaCrossReversalExitConfirmation.CloseBeyond
                && Time[barsAgo] > activeEntryFillTime)
                EvaluatePriceExit(Close[barsAgo], ExitLevelPrice(barsAgo), true);
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
                case EmaCrossReversalExitLevel.EntryPrice:
                    return Position.AveragePrice;
                default:
                    throw new InvalidOperationException("EmaCrossReversal requires an enabled exit level.");
            }
        }

        private void EvaluatePriceExit(double price, double level, bool allowExit)
        {
            if (entryPending || earlyExitPending || orderFaulted)
                return;

            MarketPosition side = Position.MarketPosition;
            if (side == MarketPosition.Flat)
            {
                earlyExitArmed = false;
                return;
            }
            if (side != activeEntrySide)
                return;

            if (!earlyExitArmed)
            {
                earlyExitArmed = side == MarketPosition.Long ? price > level : price < level;
                return;
            }

            bool reached = ExitConfirmation == EmaCrossReversalExitConfirmation.Touch
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

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

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
                    activeEntrySide = signal == LongEntrySignal ? MarketPosition.Long : MarketPosition.Short;
                    earlyExitArmed = false;
                }

                entryFilledQuantity += quantity;
                entryPending = entryFilledQuantity < execution.Order.Quantity;
                activeEntryFillTime = time;
                skipFirstExitObservation = true;
                if (EarlyExitLevel != EmaCrossReversalExitLevel.Off
                    && EarlyExitLevel != EmaCrossReversalExitLevel.EntryPrice && !earlyExitArmed
                    && Position.MarketPosition == activeEntrySide)
                {
                    fastEma.Update();
                    slowEma.Update();
                    double level = ExitLevelPrice(0);
                    earlyExitArmed = activeEntrySide == MarketPosition.Long ? price > level : price < level;
                }
            }
            else if ((signal == LongEarlyExitSignal || signal == ShortEarlyExitSignal)
                && (marketPosition == MarketPosition.Flat || Position.MarketPosition == MarketPosition.Flat))
            {
                earlyExitPending = false;
                earlyExitArmed = false;
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
        [Display(Name = "Early-exit level", Description = "Off preserves crossover-only exits. Choose the configured fast/slow EMA or actual average entry price. EntryPrice arms only after a favorable observed move.", Order = 1, GroupName = "Optional early exit")]
        public EmaCrossReversalExitLevel EarlyExitLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit confirmation", Description = "Touch exits intrabar at/through the level; CloseBeyond requires a completed candle strictly beyond it. Wait for a new EMA crossover after an exit.", Order = 2, GroupName = "Optional early exit")]
        public EmaCrossReversalExitConfirmation ExitConfirmation { get; set; }
    }
}
