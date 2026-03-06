namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public sealed class OrboEngine : PriceMomentumEngineBase
    {
        public OrboEngine(int barsInProgress, bool isEnabled)
            : base("ORBO", barsInProgress, isEnabled)
        {
        }
    }
}
