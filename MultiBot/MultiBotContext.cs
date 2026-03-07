#region Using declarations
using System;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public sealed class MultiBotContext
    {
        public DateTime Time { get; private set; }
        public int BarsInProgress { get; private set; }
        public int CurrentBar { get; private set; }
        public double Open { get; private set; }
        public double High { get; private set; }
        public double Low { get; private set; }
        public double Close { get; private set; }
        public MarketPosition Position { get; private set; }
        public int PositionQuantity { get; private set; }

        public MultiBotContext(
            DateTime time,
            int barsInProgress,
            int currentBar,
            double open,
            double high,
            double low,
            double close,
            MarketPosition position,
            int positionQuantity)
        {
            Time = time;
            BarsInProgress = barsInProgress;
            CurrentBar = currentBar;
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Position = position;
            PositionQuantity = positionQuantity;
        }
    }
}
