using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public interface IMultiBotEngine
    {
        string Name { get; }
        int BarsInProgress { get; }
        bool IsEnabled { get; }

        IList<TradeIntent> OnBarUpdate(MultiBotContext context);
    }
}
