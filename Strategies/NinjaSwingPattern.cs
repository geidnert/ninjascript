using System;

namespace Trader.Indicators
{
    public static class NinjaSwingPattern
    {
        public static bool IsConfirmed(int candidateBarsAgo, int strength, bool high, Func<int, decimal> priceBarsAgo)
        {
            if (strength < 1 || candidateBarsAgo < strength)
                throw new ArgumentOutOfRangeException("candidateBarsAgo/strength");
            if (priceBarsAgo == null)
                throw new ArgumentNullException("priceBarsAgo");
            decimal candidate = priceBarsAgo(candidateBarsAgo);
            for (int offset = 1; offset <= strength; offset++)
            {
                decimal older = priceBarsAgo(candidateBarsAgo + offset);
                decimal newer = priceBarsAgo(candidateBarsAgo - offset);
                if (high ? older > candidate || newer >= candidate : older < candidate || newer <= candidate)
                    return false;
            }
            return true;
        }

        public static bool TryFindBeforeRejection(int strength, int lookbackBars, int availableBarsAgo,
            bool high, Func<int, decimal> priceBarsAgo, out decimal level, out int pivotBarsAgo)
        {
            if (strength < 1 || lookbackBars < 1 || availableBarsAgo < 0)
                throw new ArgumentOutOfRangeException("strength/lookbackBars/availableBarsAgo");
            if (priceBarsAgo == null)
                throw new ArgumentNullException("priceBarsAgo");
            level = 0m;
            pivotBarsAgo = -1;
            int maximum = (int)Math.Min((long)lookbackBars + 1, (long)availableBarsAgo - strength);
            // Bar 1 is the rejection candle; bar 0 is its live confirmation bar.
            for (int candidate = strength + 2; candidate <= maximum; candidate++)
            {
                if (!IsConfirmed(candidate, strength, high, priceBarsAgo))
                    continue;
                level = priceBarsAgo(candidate);
                pivotBarsAgo = candidate;
                return true;
            }
            return false;
        }

        public static bool WithinDistance(decimal rejectionExtreme, decimal level, decimal maximumDistance)
        {
            if (maximumDistance < 0m)
                throw new ArgumentOutOfRangeException("maximumDistance");
            return Math.Abs(rejectionExtreme - level) <= maximumDistance;
        }
    }
}
