using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public sealed class EveEngine : IMultiBotEngine
    {
        public string Name { get { return "EVE"; } }
        public int BarsInProgress { get; private set; }
        public bool IsEnabled { get; private set; }

        public EveEngine(int barsInProgress, bool isEnabled)
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
