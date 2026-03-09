using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public sealed class OrboEngine : IMultiBotEngine
    {
        public string Name { get { return "ORBO"; } }
        public int BarsInProgress { get; private set; }
        public bool IsEnabled { get; private set; }

        public OrboEngine(int barsInProgress, bool isEnabled)
        {
            BarsInProgress = barsInProgress;
            IsEnabled = isEnabled;
        }

        public IList<TradeIntent> OnBarUpdate(MultiBotContext context)
        {
            return new List<TradeIntent>();
        }
    }
}
