using System;

namespace TraderResearch.OrderFlow
{
    public enum ValueAreaEntryMode { ReversalOnly, ExpansionOnly, Both }

    public sealed class ValueAreaExpansionState
    {
        private long lastBar = -1;
        private int side;
        private int count;
        private bool consumed;

        public int OutsideSide { get { return side; } }
        public int OutsideCloses { get { return count; } }

        public void Reset()
        {
            lastBar = -1;
            ResetRun();
        }

        public int Observe(long bar, decimal close, decimal val, decimal vah, int minimumCloses)
        {
            if (bar < 0 || minimumCloses < 1)
                throw new ArgumentOutOfRangeException("bar/minimumCloses");
            if (val > vah)
                throw new ArgumentException("VAL must not exceed VAH.");
            if (bar == lastBar)
                return 0;
            if (bar < lastBar)
                throw new InvalidOperationException("Expansion bars must be chronological.");
            if (lastBar >= 0 && bar != lastBar + 1)
                ResetRun();
            lastBar = bar;

            int nextSide = close > vah ? 1 : close < val ? -1 : 0;
            if (nextSide == 0)
            {
                ResetRun();
                return 0;
            }
            if (nextSide != side)
            {
                ResetRun();
                side = nextSide;
            }
            if (count < minimumCloses)
                count++;
            if (count < minimumCloses || consumed)
                return 0;
            consumed = true;
            return side;
        }

        private void ResetRun()
        {
            side = 0;
            count = 0;
            consumed = false;
        }
    }
}
