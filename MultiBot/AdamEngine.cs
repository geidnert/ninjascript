namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public sealed class AdamEngine : PriceMomentumEngineBase
    {
        public AdamEngine(int barsInProgress, bool isEnabled)
            : base("ADAM", barsInProgress, isEnabled)
        {
        }
    }
}
