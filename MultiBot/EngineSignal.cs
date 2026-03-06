#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public enum EngineSignalAction
    {
        None,
        EnterLong,
        EnterShort,
        ExitLong,
        ExitShort
    }

    public sealed class EngineSignal
    {
        public static readonly EngineSignal None = new EngineSignal(EngineSignalAction.None, string.Empty);

        public EngineSignalAction Action { get; private set; }
        public string Reason { get; private set; }

        public EngineSignal(EngineSignalAction action, string reason)
        {
            Action = action;
            Reason = reason ?? string.Empty;
        }
    }
}
