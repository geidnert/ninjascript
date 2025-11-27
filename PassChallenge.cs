using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.ComponentModel;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;


namespace NinjaTrader.NinjaScript.Strategies
{
    public class PassChallenge : Strategy
    {
		[NinjaScriptProperty]
        [Display(Name = "Hour", Description = "Hour of entry", Order = 1, GroupName = "Time Management")]
        public int EntryHour { get; set; }

		[NinjaScriptProperty]
        [Display(Name = "Minute", Description = "Minute of entry", Order = 2, GroupName = "Time Management")]
        public int EntryMinute { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Contracts", Description = "Number of contracts to take", Order = 1, GroupName = "Trade Management")]
        public int NumberOfContracts { get; set; }

        // [Display(Name = "TP points", Description = "Number of points for TP", Order = 3, GroupName = "Trade Management")]
        internal int ProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dollar target", Description = "How many dollars do you need, add a little more to be sure", Order = 4, GroupName = "Trade Management")]
        public int DollarTarget { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dollar stop", Description = "Risk per trade in dollars (behind entry)", Order = 5, GroupName = "Trade Management")]
        public int DollarStop { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trailing", Description = "Trail stop in profit", Order = 6, GroupName = "Trade Management")]
        public bool Trailing { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Long trade", Description = "A Long trade will take place ich checked otherwise a Short", Order = 4, GroupName = "Trade Management")]
        public bool LongTrade { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Limit Order", Description = "Place limit order instead of market", Order = 7, GroupName = "Trade Management")]
        public bool LimitOrder { get; set; }

        private bool ordersPlaced = false;
		private string displayText = "Waiting...";
        private double trailingStopDistance = double.NaN;
        private double currentStopPrice = double.NaN;
        private double highSinceEntry = double.NaN;
        private double lowSinceEntry = double.NaN;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "PassChallenge";
                Calculate = Calculate.OnEachTick;
                IsUnmanaged = false;
                IsInstantiatedOnEachOptimizationIteration = false;

                NumberOfContracts = 4;
                EntryHour = 9;
                EntryMinute = 29;
                ProfitPoints = 0;
                DollarTarget = 3050;
                DollarStop = 1000;
                Trailing = false;
                LongTrade = true;
            }
        }

        protected override void OnBarUpdate()
        {
			UpdateInfoText(GetDebugText());
            ManageTrailingStop();
            
			if (ordersPlaced || State != State.Realtime)
                return;

            DateTime now = Times[0][0];

            if (now.Hour == EntryHour &&
                now.Minute == EntryMinute + 1)
            {
                double ask = GetCurrentAsk();
                double bid = GetCurrentBid();
                double tick = TickSize;

                Print($"Bid: {bid}, Ask: {ask}, TickSize: {tick}");

                double dollarPerPoint = GetDollarPerPoint();
                double profitPoints = ProfitPoints;
                double stopPoints = double.NaN;
                bool useStop = DollarStop > 0;
                if (!useStop)
                {
                    trailingStopDistance = double.NaN;
                    currentStopPrice = double.NaN;
                }

                int numberOfContracts = NumberOfContracts;//GetSafeContractCount();
                Print($"Number of contracts to trade: {numberOfContracts}");

                if (profitPoints == 0 && DollarTarget > 0)
                {
                    profitPoints = DollarTarget / (dollarPerPoint * numberOfContracts);
                    Print($"Calculated profit points from dollar target: {profitPoints}");
                }

                if (useStop)
                {
                    stopPoints = DollarStop / (dollarPerPoint * numberOfContracts);
                    Print($"Calculated stop points from dollar stop: {stopPoints}");
                    trailingStopDistance = stopPoints;
                }

                double longEntry = Instrument.MasterInstrument.RoundToTickSize(bid);
                double shortEntry = Instrument.MasterInstrument.RoundToTickSize(ask);

                Print($"Calculated longEntry: {longEntry}, shortEntry: {shortEntry}");

                if (LongTrade && !double.IsNaN(longEntry))
                {
                    double target = Instrument.MasterInstrument.RoundToTickSize(longEntry + profitPoints);
                    Print($"Submitting Long Entry at {longEntry} with target {target}");
                    SetProfitTarget("LongEntry", CalculationMode.Price, target);
                    if (useStop && !double.IsNaN(stopPoints))
                    {
                        double stop = Instrument.MasterInstrument.RoundToTickSize(longEntry - stopPoints);
                        SetStopLoss("LongEntry", CalculationMode.Price, stop, false);
                        currentStopPrice = stop;
                        highSinceEntry = longEntry;
                        Print($"Long stop set at {stop}");
                    }
					
					if (LimitOrder)
                    	EnterLongLimit(0, true, numberOfContracts, longEntry, "LongEntry");
					else
						EnterLong(numberOfContracts, "LongEntry");

                    Print("Long order submitted at: " + now.ToString("HH:mm:ss"));
                }

                if (!LongTrade && !double.IsNaN(shortEntry))
                {
                    double target = Instrument.MasterInstrument.RoundToTickSize(shortEntry - profitPoints);
                    Print($"Submitting Short Entry at {shortEntry} with target {target}");
                    SetProfitTarget("ShortEntry", CalculationMode.Price, target);
                    if (useStop && !double.IsNaN(stopPoints))
                    {
                        double stop = Instrument.MasterInstrument.RoundToTickSize(shortEntry + stopPoints);
                        SetStopLoss("ShortEntry", CalculationMode.Price, stop, false);
                        currentStopPrice = stop;
                        lowSinceEntry = shortEntry;
                        Print($"Short stop set at {stop}");
                    }
					if (LimitOrder)
                    	EnterShortLimit(0, true, numberOfContracts, shortEntry, "ShortEntry");
					else
						EnterShort(numberOfContracts, "ShortEntry");
                    
					Print("Short order submitted at: " + now.ToString("HH:mm:ss"));
                }

                if (LongTrade && double.IsNaN(longEntry))
                    Print("No long order submitted due to invalid entry price.");

                if (!LongTrade && double.IsNaN(shortEntry))
                    Print("No short order submitted due to invalid entry price.");

                ordersPlaced = true;
            }
        }

		string GetAddOnVersion()
		{
			var assembly = Assembly.GetExecutingAssembly();
			Version version = assembly.GetName().Version;
			return version.ToString();
		}

		private string GetDebugText() => "Entry time: " + EntryHour + ":" + EntryMinute +  
										"\nContract: " + NumberOfContracts + 
										"\nDirection: " + (LongTrade ? "Long" : "Short") +
										"\nOrder Type: " + (LimitOrder ? "Limit" : "Market") +
										"\nDollar target: " + DollarTarget + 
                                        "\nDollar stop: " + DollarStop +
                                        "\nTrailing: " + Trailing +
										"\nPassChallenge v" + GetAddOnVersion();

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
											double averageFillPrice, OrderState orderState, DateTime time,
											ErrorCode error, string comment)
		{
			Print($"[OnOrderUpdate] {time:HH:mm:ss} | Name: {order.Name}, State: {orderState}, Limit: {limitPrice}, Fill: {filled}, AvgPrice: {averageFillPrice}");
		}


        private double GetDollarPerPoint()
        {
            if (Instrument.FullName.StartsWith("MNQ"))
                return 2.0;
            else if (Instrument.FullName.StartsWith("NQ"))
                return 20.0;
            else
                return 1.0; // fallback/default
        }

        private int GetSafeContractCount()
        {
            double maxEquity = 50000;
            double maxDrawdown = 2500;
            double marginPerContract = 17600;

            double cash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
            double unrealized = Account.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);
            double netEquity = cash + unrealized;

            double equityUsed = maxEquity - netEquity;
            double remainingDrawdown = maxDrawdown - equityUsed;

            Print($"Account Cash: {cash}, Unrealized: {unrealized}, NetEquity: {netEquity}");
            Print($"Equity used: {equityUsed}, Remaining Drawdown: {remainingDrawdown}");

            if (remainingDrawdown <= 0)
                return 0;

            int contracts = (int)(remainingDrawdown / marginPerContract);
            return Math.Min(contracts, 10);
        }

		public void UpdateInfoText(string newText)
        {
            displayText = newText;

            Draw.TextFixed(owner: this, tag: "myStatusLabel", text: displayText, textPosition: TextPosition.BottomRight,
						textBrush: Brushes.DarkGray, font: new SimpleFont("Segoe UI", 9), outlineBrush: null,
						areaBrush: Brushes.Gray, areaOpacity: 20);
        }

        private void ManageTrailingStop()
        {
            if (!Trailing || Position.MarketPosition == MarketPosition.Flat || double.IsNaN(trailingStopDistance) || trailingStopDistance <= 0)
                return;

            // Seed anchors with average price if not set yet
            if (double.IsNaN(highSinceEntry))
                highSinceEntry = Position.AveragePrice;
            if (double.IsNaN(lowSinceEntry))
                lowSinceEntry = Position.AveragePrice;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                highSinceEntry = Math.Max(highSinceEntry, Close[0]);
                double newStop = Instrument.MasterInstrument.RoundToTickSize(highSinceEntry - trailingStopDistance);
                if (double.IsNaN(currentStopPrice) || newStop > currentStopPrice)
                {
                    currentStopPrice = newStop;
                    SetStopLoss("LongEntry", CalculationMode.Price, currentStopPrice, false);
                    Print($"[TRAIL] Long stop moved to {currentStopPrice:F2} (highSinceEntry={highSinceEntry:F2})");
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                lowSinceEntry = Math.Min(lowSinceEntry, Close[0]);
                double newStop = Instrument.MasterInstrument.RoundToTickSize(lowSinceEntry + trailingStopDistance);
                if (double.IsNaN(currentStopPrice) || newStop < currentStopPrice)
                {
                    currentStopPrice = newStop;
                    SetStopLoss("ShortEntry", CalculationMode.Price, currentStopPrice, false);
                    Print($"[TRAIL] Short stop moved to {currentStopPrice:F2} (lowSinceEntry={lowSinceEntry:F2})");
                }
            }
        }
    }
}
