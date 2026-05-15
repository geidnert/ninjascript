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
    public class EMACTesting : Strategy
    {
        private const string StrategySignalPrefix = "EMACTesting";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";

        private EMA ema;
        private Order longEntryOrder;
        private Order shortEntryOrder;

        private bool longNegativeSlopeSeen;
        private bool longCloseBelowSeen;
        private bool longSetupActive;
        private double lastLongEntryPrice;
        private double lastLongStopPrice;
        private double lastLongTargetPrice;

        private bool shortPositiveSlopeSeen;
        private bool shortCloseAboveSeen;
        private bool shortSetupActive;
        private double lastShortEntryPrice;
        private double lastShortStopPrice;
        private double lastShortTargetPrice;
        private int currentPositionEntryBar = -1;

        public EMACTesting()
        {
            VendorLicense(337);
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "EMAC testing strategy: EMA slope, close through the EMA, then moving limit at the EMA line.";
                Name = "EMACTesting";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                IsInstantiatedOnEachOptimizationIteration = false;
                IsFillLimitOnTouch = false;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;

                EmaPeriod = 21;
                EntryEmaMinSlopePoints = 0.0;
                EntryMinRunFromEmaPoints = 0.0;
                LimitOffsetPoints = -1.0;
                Contracts = 1;
                StopLossPoints = 4.0;
                TakeProfitType = EMACTesting_TakeProfitType.FixedPoints;
                FixedTakeProfitPoints = 20.0;
                PreviousCandleOffsetPoints = 0.0;
                PivotStrength = 3;
                PivotOffsetPoints = 0.0;
                SlopeTargetMultiplier = 1.0;
                CandleReversalExitBars = 5;
                CandleReversalCloseBeyondPoints = 0.0;
                CandleReversalMinBodyPoints = 0.75;
                AsiaSessionStart = new TimeSpan(18, 30, 0);
                AsiaSessionEnd = new TimeSpan(2, 0, 0);
                LondonSessionStart = new TimeSpan(1, 0, 0);
                LondonSessionEnd = new TimeSpan(8, 0, 0);
                NewYorkSessionStart = new TimeSpan(9, 30, 0);
                NewYorkSessionEnd = new TimeSpan(16, 0, 0);
                CloseAtSessionEnd = false;
            }
            else if (State == State.DataLoaded)
            {
                ema = EMA(EmaPeriod);
                AddChartIndicator(ema);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < Math.Max(EmaPeriod + 1, PivotStrength * 2 + 1))
                return;

            double emaValue = ema[0];
            double emaSlopePoints = GetEmaSlopePoints();

            TryExitCandleReversal();

            if (!IsWithinAnySession(Time[0]))
            {
                ResetLongSequence();
                ResetShortSequence();

                if (CloseAtSessionEnd && Position.MarketPosition == MarketPosition.Long)
                    ExitLong("SessionExit", LongEntrySignal);
                else if (CloseAtSessionEnd && Position.MarketPosition == MarketPosition.Short)
                    ExitShort("SessionExit", ShortEntrySignal);

                return;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (longEntryOrder == null && !shortSetupActive)
                    WatchForLongSequence(emaValue, emaSlopePoints);

                if (shortEntryOrder == null && !longSetupActive)
                    WatchForShortSequence(emaValue, emaSlopePoints);
            }

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (longSetupActive && Close[0] < emaValue)
            {
                ResetLongSequence();
                return;
            }

            if (shortSetupActive && Close[0] > emaValue)
            {
                ResetShortSequence();
                return;
            }

            if (longSetupActive && EntryEmaSlopePasses(true, emaSlopePoints) && EntryMinRunFromEmaPasses(true, emaValue))
                SubmitOrUpdateLimit(true, emaValue, emaSlopePoints);
            else if (shortSetupActive && EntryEmaSlopePasses(false, emaSlopePoints) && EntryMinRunFromEmaPasses(false, emaValue))
                SubmitOrUpdateLimit(false, emaValue, emaSlopePoints);
        }

        private void WatchForLongSequence(double emaValue, double emaSlopePoints)
        {
            if (emaSlopePoints <= -Math.Abs(EntryEmaMinSlopePoints))
                longNegativeSlopeSeen = true;

            if (longNegativeSlopeSeen && Close[0] < emaValue)
                longCloseBelowSeen = true;

            if (!longCloseBelowSeen || Close[0] <= emaValue)
                return;

            ResetShortSequence();
            longSetupActive = true;
        }

        private void WatchForShortSequence(double emaValue, double emaSlopePoints)
        {
            if (emaSlopePoints >= Math.Abs(EntryEmaMinSlopePoints))
                shortPositiveSlopeSeen = true;

            if (shortPositiveSlopeSeen && Close[0] > emaValue)
                shortCloseAboveSeen = true;

            if (!shortCloseAboveSeen || Close[0] >= emaValue)
                return;

            ResetLongSequence();
            shortSetupActive = true;
        }

        private void SubmitOrUpdateLimit(bool isLong, double emaValue, double emaSlopePoints)
        {
            string entrySignal = isLong ? LongEntrySignal : ShortEntrySignal;
            Order entryOrder = isLong ? longEntryOrder : shortEntryOrder;
            double entryPrice = isLong
                ? Instrument.MasterInstrument.RoundToTickSize(emaValue + LimitOffsetPoints)
                : Instrument.MasterInstrument.RoundToTickSize(emaValue - LimitOffsetPoints);
            double stopPrice = isLong
                ? Instrument.MasterInstrument.RoundToTickSize(entryPrice - StopLossPoints)
                : Instrument.MasterInstrument.RoundToTickSize(entryPrice + StopLossPoints);
            double targetPrice;

            if (!TryBuildTargetPrice(isLong, entryPrice, emaSlopePoints, out targetPrice))
                return;

            SetStopLoss(entrySignal, CalculationMode.Price, stopPrice, false);
            if (TakeProfitType != EMACTesting_TakeProfitType.None)
                SetProfitTarget(entrySignal, CalculationMode.Price, targetPrice);

            if (IsOrderActive(entryOrder))
            {
                if (SubmittedPricesMatch(isLong, entryPrice, stopPrice, targetPrice))
                    return;

                ChangeOrder(entryOrder, entryOrder.Quantity, entryPrice, 0);
            }
            else if (isLong)
            {
                CancelOrderIfActive(shortEntryOrder);
                longEntryOrder = EnterLongLimit(0, true, Contracts, entryPrice, LongEntrySignal);
            }
            else
            {
                CancelOrderIfActive(longEntryOrder);
                shortEntryOrder = EnterShortLimit(0, true, Contracts, entryPrice, ShortEntrySignal);
            }

            StoreSubmittedPrices(isLong, entryPrice, stopPrice, targetPrice);
        }

        private bool TryBuildTargetPrice(bool isLong, double entryPrice, double emaSlopePoints, out double targetPrice)
        {
            targetPrice = 0.0;

            switch (TakeProfitType)
            {
                case EMACTesting_TakeProfitType.None:
                    return true;

                case EMACTesting_TakeProfitType.FixedPoints:
                    targetPrice = isLong
                        ? entryPrice + FixedTakeProfitPoints
                        : entryPrice - FixedTakeProfitPoints;
                    break;

                case EMACTesting_TakeProfitType.PreviousCandle:
                    targetPrice = isLong
                        ? High[1] + PreviousCandleOffsetPoints
                        : Low[1] - PreviousCandleOffsetPoints;
                    break;

                case EMACTesting_TakeProfitType.PreviousPivot:
                    if (!TryGetPreviousPivot(isLong, out targetPrice))
                        return false;
                    targetPrice += isLong ? PivotOffsetPoints : -PivotOffsetPoints;
                    break;

                case EMACTesting_TakeProfitType.SlopeCalculated:
                    double targetDistance = Math.Abs(emaSlopePoints) * SlopeTargetMultiplier;
                    targetPrice = isLong
                        ? entryPrice + targetDistance
                        : entryPrice - targetDistance;
                    break;
            }

            targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);
            return isLong ? targetPrice > entryPrice : targetPrice < entryPrice;
        }

        private bool TryGetPreviousPivot(bool isLong, out double pivotPrice)
        {
            pivotPrice = 0.0;
            int strength = Math.Max(1, PivotStrength);

            for (int barsAgo = strength; barsAgo <= CurrentBar - strength; barsAgo++)
            {
                double candidate = isLong ? High[barsAgo] : Low[barsAgo];
                bool isPivot = true;

                for (int offset = 1; offset <= strength; offset++)
                {
                    if (isLong)
                    {
                        if (High[barsAgo - offset] >= candidate || High[barsAgo + offset] > candidate)
                        {
                            isPivot = false;
                            break;
                        }
                    }
                    else
                    {
                        if (Low[barsAgo - offset] <= candidate || Low[barsAgo + offset] < candidate)
                        {
                            isPivot = false;
                            break;
                        }
                    }
                }

                if (isPivot)
                {
                    pivotPrice = candidate;
                    return true;
                }
            }

            return false;
        }

        private double GetEmaSlopePoints()
        {
            if (CurrentBar < 1 || ema == null)
                return 0.0;

            return ema[0] - ema[1];
        }

        private bool EntryEmaSlopePasses(bool isLong, double slopePoints)
        {
            double requiredSlope = Math.Abs(EntryEmaMinSlopePoints);

            return isLong
                ? slopePoints >= requiredSlope
                : slopePoints <= -requiredSlope;
        }

        private bool EntryMinRunFromEmaPasses(bool isLong, double emaValue)
        {
            double requiredDistance = Math.Abs(EntryMinRunFromEmaPoints);
            if (requiredDistance <= 0.0)
                return true;

            return isLong
                ? Close[0] >= emaValue + requiredDistance
                : Close[0] <= emaValue - requiredDistance;
        }

        private bool TryExitCandleReversal()
        {
            if (CandleReversalExitBars <= 0 || Position.MarketPosition == MarketPosition.Flat || currentPositionEntryBar < 0)
                return false;

            int barsHeld = CurrentBar - currentPositionEntryBar;
            if (barsHeld < CandleReversalExitBars)
                return false;

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Close[0] <= Open[0])
                    return false;

                double referenceHigh;
                if (!TryGetMostRecentBearishCandleHighSinceEntry(out referenceHigh))
                    return false;

                if (Close[0] <= referenceHigh + CandleReversalCloseBeyondPoints)
                    return false;

                ExitShort("CandleReversalExit", ShortEntrySignal);
                return true;
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (Close[0] >= Open[0])
                    return false;

                double referenceLow;
                if (!TryGetMostRecentBullishCandleLowSinceEntry(out referenceLow))
                    return false;

                if (Close[0] >= referenceLow - CandleReversalCloseBeyondPoints)
                    return false;

                ExitLong("CandleReversalExit", LongEntrySignal);
                return true;
            }

            return false;
        }

        private bool TryGetMostRecentBearishCandleHighSinceEntry(out double high)
        {
            high = 0.0;
            int maxBarsAgo = CurrentBar - Math.Max(0, currentPositionEntryBar);

            for (int i = 1; i <= maxBarsAgo; i++)
            {
                if (Close[i] < Open[i] && IsCandleBodyLargeEnough(i, CandleReversalMinBodyPoints))
                {
                    high = High[i];
                    return true;
                }
            }

            return false;
        }

        private bool TryGetMostRecentBullishCandleLowSinceEntry(out double low)
        {
            low = 0.0;
            int maxBarsAgo = CurrentBar - Math.Max(0, currentPositionEntryBar);

            for (int i = 1; i <= maxBarsAgo; i++)
            {
                if (Close[i] > Open[i] && IsCandleBodyLargeEnough(i, CandleReversalMinBodyPoints))
                {
                    low = Low[i];
                    return true;
                }
            }

            return false;
        }

        private bool IsCandleBodyLargeEnough(int barsAgo, double minBodyPoints)
        {
            return Math.Abs(Close[barsAgo] - Open[barsAgo]) >= minBodyPoints;
        }

        private bool IsWithinAnySession(DateTime time)
        {
            TimeSpan current = time.TimeOfDay;

            return IsTimeInRange(current, AsiaSessionStart, AsiaSessionEnd)
                || IsTimeInRange(current, LondonSessionStart, LondonSessionEnd)
                || IsTimeInRange(current, NewYorkSessionStart, NewYorkSessionEnd);
        }

        private bool IsTimeInRange(TimeSpan current, TimeSpan start, TimeSpan end)
        {
            if (start == end)
                return false;

            if (start < end)
                return current >= start && current < end;

            return current >= start || current < end;
        }

        private static bool IsOrderActive(Order order)
        {
            return order != null
                && (order.OrderState == OrderState.Accepted
                    || order.OrderState == OrderState.Working
                    || order.OrderState == OrderState.Submitted
                    || order.OrderState == OrderState.PartFilled);
        }

        private void CancelOrderIfActive(Order order)
        {
            if (IsOrderActive(order))
                CancelOrder(order);
        }

        private bool SubmittedPricesMatch(bool isLong, double entryPrice, double stopPrice, double targetPrice)
        {
            return isLong
                ? PricesAreEqual(entryPrice, lastLongEntryPrice)
                    && PricesAreEqual(stopPrice, lastLongStopPrice)
                    && PricesAreEqual(targetPrice, lastLongTargetPrice)
                : PricesAreEqual(entryPrice, lastShortEntryPrice)
                    && PricesAreEqual(stopPrice, lastShortStopPrice)
                    && PricesAreEqual(targetPrice, lastShortTargetPrice);
        }

        private bool PricesAreEqual(double left, double right)
        {
            return Math.Abs(left - right) < TickSize * 0.5;
        }

        private void StoreSubmittedPrices(bool isLong, double entryPrice, double stopPrice, double targetPrice)
        {
            if (isLong)
            {
                lastLongEntryPrice = entryPrice;
                lastLongStopPrice = stopPrice;
                lastLongTargetPrice = targetPrice;
            }
            else
            {
                lastShortEntryPrice = entryPrice;
                lastShortStopPrice = stopPrice;
                lastShortTargetPrice = targetPrice;
            }
        }

        private void ResetLongSequence()
        {
            CancelOrderIfActive(longEntryOrder);

            longNegativeSlopeSeen = false;
            longCloseBelowSeen = false;
            longSetupActive = false;
            lastLongEntryPrice = 0.0;
            lastLongStopPrice = 0.0;
            lastLongTargetPrice = 0.0;
        }

        private void ResetShortSequence()
        {
            CancelOrderIfActive(shortEntryOrder);

            shortPositiveSlopeSeen = false;
            shortCloseAboveSeen = false;
            shortSetupActive = false;
            lastShortEntryPrice = 0.0;
            lastShortStopPrice = 0.0;
            lastShortTargetPrice = 0.0;
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            bool isLongEntry = order.Name == LongEntrySignal;
            bool isShortEntry = order.Name == ShortEntrySignal;
            if (!isLongEntry && !isShortEntry)
                return;

            if (isLongEntry)
                longEntryOrder = order;
            else
                shortEntryOrder = order;

            if (orderState == OrderState.Filled)
            {
                if (isLongEntry)
                {
                    longSetupActive = false;
                    longEntryOrder = null;
                }
                else
                {
                    shortSetupActive = false;
                    shortEntryOrder = null;
                }

                currentPositionEntryBar = CurrentBar;
                longNegativeSlopeSeen = false;
                longCloseBelowSeen = false;
                shortPositiveSlopeSeen = false;
                shortCloseAboveSeen = false;
            }
            else if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
            {
                if (isLongEntry)
                    longEntryOrder = null;
                else
                    shortEntryOrder = null;
            }

            if (orderState == OrderState.Rejected)
                Print(string.Format("{0} | {1} | {2} entry rejected | error={3} comment={4}",
                    time, StrategySignalPrefix, isLongEntry ? "long" : "short", error, comment ?? string.Empty));
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity,
            MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat)
            {
                longEntryOrder = null;
                shortEntryOrder = null;
                currentPositionEntryBar = -1;
            }
        }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "EMA Period", Description = "EMA line used for slope, close sequence, and limit placement.", GroupName = "Parameters", Order = 0)]
        public int EmaPeriod { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Entry EMA Min Slope Points", Description = "DUOrc-style EMA[0] - EMA[1] slope required before the moving limit starts.", GroupName = "Parameters", Order = 1)]
        public double EntryEmaMinSlopePoints { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Entry Min Run From EMA Points", Description = "0 disables. After the EMA cross/reclaim, price must close this many points beyond the EMA before the moving limit starts.", GroupName = "Parameters", Order = 2)]
        public double EntryMinRunFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Limit Offset Points", Description = "Signed EMA offset. -1 means 1 point below EMA for longs and 1 point above EMA for shorts.", GroupName = "Parameters", Order = 3)]
        public double LimitOffsetPoints { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Contracts", Description = "Order quantity per entry.", GroupName = "Parameters", Order = 4)]
        public int Contracts { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Stop Loss Points", Description = "Fixed stop distance from the limit entry.", GroupName = "Parameters", Order = 5)]
        public double StopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit Type", Description = "Previous candle high/low, previous pivot high/low, or a target calculated from EMA slope.", GroupName = "Take Profit", Order = 0)]
        public EMACTesting_TakeProfitType TakeProfitType { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Fixed Take Profit Points", Description = "Fixed TP distance used when Take Profit Type is FixedPoints.", GroupName = "Take Profit", Order = 1)]
        public double FixedTakeProfitPoints { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Previous Candle Offset Points", Description = "Offset added past the previous candle high/low target.", GroupName = "Take Profit", Order = 2)]
        public double PreviousCandleOffsetPoints { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Pivot Strength", Description = "Bars on each side used to confirm the previous pivot high/low.", GroupName = "Take Profit", Order = 3)]
        public int PivotStrength { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Pivot Offset Points", Description = "Offset added past the previous pivot high/low target.", GroupName = "Take Profit", Order = 4)]
        public double PivotOffsetPoints { get; set; }

        [Range(0.01, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Slope Target Multiplier", Description = "Points of target distance per point of EMA slope when using slope-calculated TP.", GroupName = "Take Profit", Order = 5)]
        public double SlopeTargetMultiplier { get; set; }

        [Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Candle Reversal Exit Bars", Description = "Bars held before DUOrc-style reverse-candle exit can trigger. 0 disables.", GroupName = "Candle Reversal Exit", Order = 0)]
        public int CandleReversalExitBars { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "Close-beyond distance past the reference candle high/low.", GroupName = "Candle Reversal Exit", Order = 1)]
        public double CandleReversalCloseBeyondPoints { get; set; }

        [Range(0.0, double.MaxValue), NinjaScriptProperty]
        [Display(Name = "Candle Reversal Min Body Points", Description = "Minimum body size for the reference opposite candle.", GroupName = "Candle Reversal Exit", Order = 2)]
        public double CandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia Session Start", Description = "Asia trading window start.", GroupName = "Session Times", Order = 0)]
        public TimeSpan AsiaSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Asia Session End", Description = "Asia trading window end.", GroupName = "Session Times", Order = 1)]
        public TimeSpan AsiaSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London Session Start", Description = "London trading window start.", GroupName = "Session Times", Order = 2)]
        public TimeSpan LondonSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London Session End", Description = "London trading window end.", GroupName = "Session Times", Order = 3)]
        public TimeSpan LondonSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York Session Start", Description = "New York trading window start.", GroupName = "Session Times", Order = 4)]
        public TimeSpan NewYorkSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York Session End", Description = "New York trading window end.", GroupName = "Session Times", Order = 5)]
        public TimeSpan NewYorkSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close At Session End", Description = "Flatten open positions outside all configured sessions.", GroupName = "Session Times", Order = 6)]
        public bool CloseAtSessionEnd { get; set; }
    }

    public enum EMACTesting_TakeProfitType
    {
        None,
        FixedPoints,
        PreviousCandle,
        PreviousPivot,
        SlopeCalculated
    }
}
