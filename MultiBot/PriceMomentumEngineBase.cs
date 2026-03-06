#region Using declarations
using System;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public abstract class PriceMomentumEngineBase : IMultiBotEngine
    {
        private double previousClose = double.NaN;

        public string Name { get; private set; }
        public int BarsInProgress { get; private set; }
        public bool IsEnabled { get; private set; }

        protected PriceMomentumEngineBase(string name, int barsInProgress, bool isEnabled)
        {
            Name = name;
            BarsInProgress = barsInProgress;
            IsEnabled = isEnabled;
        }

        public EngineSignal OnBarUpdate(MultiBotContext context)
        {
            if (!IsEnabled || context.CurrentBar < 2)
            {
                previousClose = context.Close;
                return EngineSignal.None;
            }

            EngineSignal signal = EngineSignal.None;

            if (!double.IsNaN(previousClose))
            {
                if (context.Close > previousClose && context.Position == MarketPosition.Flat)
                    signal = new EngineSignal(EngineSignalAction.EnterLong, "Momentum up");
                else if (context.Close < previousClose && context.Position == MarketPosition.Long)
                    signal = new EngineSignal(EngineSignalAction.ExitLong, "Momentum down");
            }

            previousClose = context.Close;
            return signal;
        }
    }
}
