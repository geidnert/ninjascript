#region Using declarations
using System;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public sealed class RiskManager
    {
        private DateTime activeDate = DateTime.MinValue;
        private double dayStartCumProfit = 0;

        public int MaxTotalContracts { get; set; }
        public double MaxDailyLossCurrency { get; set; }

        public RiskManager(int maxTotalContracts, double maxDailyLossCurrency)
        {
            MaxTotalContracts = maxTotalContracts;
            MaxDailyLossCurrency = Math.Abs(maxDailyLossCurrency);
        }

        public void Sync(DateTime time, double cumulativeProfit)
        {
            if (activeDate == DateTime.MinValue || time.Date != activeDate.Date)
            {
                activeDate = time.Date;
                dayStartCumProfit = cumulativeProfit;
            }
        }

        public bool IsDailyLossLocked(double cumulativeProfit)
        {
            double todayPnl = cumulativeProfit - dayStartCumProfit;
            return todayPnl <= -MaxDailyLossCurrency;
        }

        public bool CanEnter(int currentPositionQuantity, int orderQuantity)
        {
            int total = Math.Abs(currentPositionQuantity) + Math.Abs(orderQuantity);
            return total <= Math.Max(1, MaxTotalContracts);
        }
    }
}
