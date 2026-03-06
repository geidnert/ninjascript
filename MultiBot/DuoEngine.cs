namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public sealed class DuoEngine : PriceMomentumEngineBase
    {
        public DuoEngine(int barsInProgress, bool isEnabled)
            : base("DUO", barsInProgress, isEnabled)
        {
        }
    }
}
