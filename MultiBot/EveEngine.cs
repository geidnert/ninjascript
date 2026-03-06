namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public sealed class EveEngine : PriceMomentumEngineBase
    {
        public EveEngine(int barsInProgress, bool isEnabled)
            : base("EVE", barsInProgress, isEnabled)
        {
        }
    }
}
