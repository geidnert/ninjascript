using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public sealed class AdamEngine : IMultiBotEngine
    {
        public string Name { get { return "ADAM"; } }
        public int BarsInProgress { get; private set; }
        public bool IsEnabled { get; private set; }

        public AdamEngine(int barsInProgress, bool isEnabled)
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
