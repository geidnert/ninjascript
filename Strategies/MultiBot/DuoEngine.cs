#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public enum DuoDirectionMode
    {
        Both,
        LongOnly,
        ShortOnly
    }

    public sealed class DuoEngine : IMultiBotEngine
    {
        private readonly Queue<double> emaSeedWindow = new Queue<double>();
        private double ema = double.NaN;
        private bool emaReady;

        private bool hasPrevOhlc;
        private double prevHigh;
        private double prevLow;
        private double prevClose;

        private int adxTrSeedCount;
        private int adxDxSeedCount;
        private double trSeedSum;
        private double pdmSeedSum;
        private double mdmSeedSum;
        private double smoothedTr;
        private double smoothedPdm;
        private double smoothedMdm;
        private double dxSeedSum;
        private double adx;
        private double prevAdx;
        private bool adxReady;

        public string Name { get { return "DUO"; } }
        public int BarsInProgress { get; private set; }
        public bool IsEnabled { get; private set; }

        public int EmaPeriod { get; private set; }
        public double ExitCrossPoints { get; private set; }
        public DuoDirectionMode TradeDirection { get; private set; }

        public bool UseSessionFilter { get; private set; }
        public bool UseNewYorkSkip { get; private set; }
        public bool AsiaBlockSundayTrades { get; private set; }
        public TimeSpan NewYorkSkipStart { get; private set; }
        public TimeSpan NewYorkSkipEnd { get; private set; }

        public bool UseAdxFilter { get; private set; }
        public int AdxPeriod { get; private set; }
        public double AdxMin { get; private set; }
        public double AdxMax { get; private set; }
        public double AdxMinSlopePoints { get; private set; }

        public TimeSpan AsiaSessionStart { get; private set; }
        public TimeSpan AsiaSessionEnd { get; private set; }
        public TimeSpan NewYorkSessionStart { get; private set; }
        public TimeSpan NewYorkSessionEnd { get; private set; }

        public DuoEngine(
            int barsInProgress,
            bool isEnabled,
            int emaPeriod,
            double exitCrossPoints,
            DuoDirectionMode tradeDirection,
            bool useSessionFilter,
            bool useNewYorkSkip,
            TimeSpan newYorkSkipStart,
            TimeSpan newYorkSkipEnd,
            bool useAdxFilter,
            int adxPeriod,
            double adxMin,
            double adxMax,
            double adxMinSlopePoints,
            bool asiaBlockSundayTrades,
            TimeSpan asiaSessionStart,
            TimeSpan asiaSessionEnd,
            TimeSpan newYorkSessionStart,
            TimeSpan newYorkSessionEnd)
        {
            BarsInProgress = barsInProgress;
            IsEnabled = isEnabled;
            EmaPeriod = Math.Max(1, emaPeriod);
            ExitCrossPoints = Math.Max(0.0, exitCrossPoints);
            TradeDirection = tradeDirection;

            UseSessionFilter = useSessionFilter;
            UseNewYorkSkip = useNewYorkSkip;
            AsiaBlockSundayTrades = asiaBlockSundayTrades;
            NewYorkSkipStart = newYorkSkipStart;
            NewYorkSkipEnd = newYorkSkipEnd;

            UseAdxFilter = useAdxFilter;
            AdxPeriod = Math.Max(2, adxPeriod);
            AdxMin = Math.Max(0.0, adxMin);
            AdxMax = Math.Max(0.0, adxMax);
            AdxMinSlopePoints = Math.Max(0.0, adxMinSlopePoints);

            AsiaSessionStart = asiaSessionStart;
            AsiaSessionEnd = asiaSessionEnd;
            NewYorkSessionStart = newYorkSessionStart;
            NewYorkSessionEnd = newYorkSessionEnd;
        }

        public IList<TradeIntent> OnBarUpdate(MultiBotContext context)
        {
            List<TradeIntent> intents = new List<TradeIntent>();

            if (!IsEnabled || context.CurrentBar < 1)
            {
                UpdateOhlcMemory(context);
                return intents;
            }

            UpdateEma(context.Close);
            UpdateAdx(context.High, context.Low, context.Close);
            if (!emaReady)
            {
                UpdateOhlcMemory(context);
                return intents;
            }

            bool inAsiaSession = IsWithinSession(context.Time.TimeOfDay, AsiaSessionStart, AsiaSessionEnd);
            bool inNewYorkSession = IsWithinSession(context.Time.TimeOfDay, NewYorkSessionStart, NewYorkSessionEnd);
            bool inAnySession = inAsiaSession || inNewYorkSession;

            bool inSessionPass = !UseSessionFilter || inAnySession;
            bool nySkipPass = !UseNewYorkSkip || !inNewYorkSession || !IsWithinSession(context.Time.TimeOfDay, NewYorkSkipStart, NewYorkSkipEnd);
            bool asiaSundayPass = !AsiaBlockSundayTrades || !inAsiaSession || context.Time.DayOfWeek != DayOfWeek.Sunday;

            bool adxMinPass = !UseAdxFilter || AdxMin <= 0.0 || (adxReady && adx >= AdxMin);
            bool adxMaxPass = !UseAdxFilter || AdxMax <= 0.0 || (adxReady && adx <= AdxMax);
            double adxSlope = adxReady ? adx - prevAdx : 0.0;
            bool adxSlopePass = !UseAdxFilter || AdxMinSlopePoints <= 0.0 || (adxReady && adxSlope >= AdxMinSlopePoints);
            bool adxPass = !UseAdxFilter || (adxMinPass && adxMaxPass && adxSlopePass);

            bool canTradeNow = inSessionPass && nySkipPass && asiaSundayPass && adxPass;

            bool allowLong = TradeDirection != DuoDirectionMode.ShortOnly;
            bool allowShort = TradeDirection != DuoDirectionMode.LongOnly;

            bool bullish = context.Close > context.Open;
            bool bearish = context.Close < context.Open;
            double bodyAbovePercent = GetBodyPercentAboveEma(context.Open, context.Close, ema);
            double bodyBelowPercent = GetBodyPercentBelowEma(context.Open, context.Close, ema);
            bool longSignal = allowLong && bullish && bodyAbovePercent > 0.0;
            bool shortSignal = allowShort && bearish && bodyBelowPercent > 0.0;

            if (context.Position == MarketPosition.Long && context.Close <= ema - ExitCrossPoints)
                intents.Add(new TradeIntent(Name, TradeIntentAction.ExitLong, "DUO EMA cross-down exit"));

            if (context.Position == MarketPosition.Short && context.Close >= ema + ExitCrossPoints)
                intents.Add(new TradeIntent(Name, TradeIntentAction.ExitShort, "DUO EMA cross-up exit"));

            if (context.Position == MarketPosition.Flat && canTradeNow && longSignal)
                intents.Add(new TradeIntent(Name, TradeIntentAction.EnterLong, "DUO setup long"));

            if (context.Position == MarketPosition.Flat && canTradeNow && shortSignal)
                intents.Add(new TradeIntent(Name, TradeIntentAction.EnterShort, "DUO setup short"));

            UpdateOhlcMemory(context);
            return intents;
        }

        private void UpdateEma(double close)
        {
            if (!emaReady)
            {
                emaSeedWindow.Enqueue(close);
                if (emaSeedWindow.Count < EmaPeriod)
                    return;

                if (emaSeedWindow.Count > EmaPeriod)
                    emaSeedWindow.Dequeue();

                double sum = 0.0;
                foreach (double v in emaSeedWindow)
                    sum += v;
                ema = sum / EmaPeriod;
                emaReady = true;
                return;
            }

            double k = 2.0 / (EmaPeriod + 1.0);
            ema = close * k + ema * (1.0 - k);
        }

        private void UpdateAdx(double high, double low, double close)
        {
            if (!hasPrevOhlc)
                return;

            double tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            double upMove = high - prevHigh;
            double downMove = prevLow - low;
            double pdm = upMove > downMove && upMove > 0 ? upMove : 0.0;
            double mdm = downMove > upMove && downMove > 0 ? downMove : 0.0;

            if (adxTrSeedCount < AdxPeriod)
            {
                trSeedSum += tr;
                pdmSeedSum += pdm;
                mdmSeedSum += mdm;
                adxTrSeedCount++;

                if (adxTrSeedCount == AdxPeriod)
                {
                    smoothedTr = trSeedSum;
                    smoothedPdm = pdmSeedSum;
                    smoothedMdm = mdmSeedSum;
                }
                return;
            }

            smoothedTr = smoothedTr - (smoothedTr / AdxPeriod) + tr;
            smoothedPdm = smoothedPdm - (smoothedPdm / AdxPeriod) + pdm;
            smoothedMdm = smoothedMdm - (smoothedMdm / AdxPeriod) + mdm;

            if (smoothedTr <= 0.0)
                return;

            double pdi = 100.0 * (smoothedPdm / smoothedTr);
            double mdi = 100.0 * (smoothedMdm / smoothedTr);
            double denom = pdi + mdi;
            double dx = denom <= 0.0 ? 0.0 : 100.0 * Math.Abs(pdi - mdi) / denom;

            prevAdx = adx;
            if (!adxReady)
            {
                dxSeedSum += dx;
                adxDxSeedCount++;
                if (adxDxSeedCount >= AdxPeriod)
                {
                    adx = dxSeedSum / AdxPeriod;
                    adxReady = true;
                }
                return;
            }

            adx = ((adx * (AdxPeriod - 1)) + dx) / AdxPeriod;
        }

        private static double GetBodyPercentAboveEma(double open, double close, double emaValue)
        {
            double bodyHigh = Math.Max(open, close);
            double bodyLow = Math.Min(open, close);
            double bodySize = bodyHigh - bodyLow;
            if (bodySize <= 0.0 || bodyHigh <= emaValue)
                return 0.0;

            double above = bodyHigh - Math.Max(bodyLow, emaValue);
            if (above <= 0.0)
                return 0.0;

            return (above / bodySize) * 100.0;
        }

        private static double GetBodyPercentBelowEma(double open, double close, double emaValue)
        {
            double bodyHigh = Math.Max(open, close);
            double bodyLow = Math.Min(open, close);
            double bodySize = bodyHigh - bodyLow;
            if (bodySize <= 0.0 || bodyLow >= emaValue)
                return 0.0;

            double below = Math.Min(bodyHigh, emaValue) - bodyLow;
            if (below <= 0.0)
                return 0.0;

            return (below / bodySize) * 100.0;
        }

        private static bool IsWithinSession(TimeSpan now, TimeSpan start, TimeSpan end)
        {
            if (start == end)
                return false;

            if (start < end)
                return now >= start && now < end;

            return now >= start || now < end;
        }

        private void UpdateOhlcMemory(MultiBotContext context)
        {
            prevHigh = context.High;
            prevLow = context.Low;
            prevClose = context.Close;
            hasPrevOhlc = true;
        }
    }
}
