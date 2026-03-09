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

		[NinjaScriptProperty]
		[Display(Name = "Debug", Description = "Enable verbose logging", Order = 1, GroupName = "Debugging")]
		public bool Debug { get; set; }

        private bool ordersPlaced = false;
		private string displayText = "Waiting...";
        private double trailingStopDistance = double.NaN;
        private double currentStopPrice = double.NaN;
        private double highSinceEntry = double.NaN;
        private double lowSinceEntry = double.NaN;
		private DateTime lastSessionDate = DateTime.MinValue;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "PassChallenge";
                Calculate = Calculate.OnEachTick;
                IsUnmanaged = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                BarsRequiredToTrade = 1;

                NumberOfContracts = 4;
                EntryHour = 9;
                EntryMinute = 29;
                ProfitPoints = 0;
                DollarTarget = 3050;
                DollarStop = 2500;
                Trailing = false;
                LongTrade = true;
				Debug = false;
            }
            else if (State == State.Configure)
            {
                // Add 1 tick series so historical/backtest runs use intrabar data for fills and trailing logic
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.Realtime)
            {
                // Reset runtime state when transitioning from historical to real-time/playback
                ordersPlaced = false;
                trailingStopDistance = double.NaN;
                currentStopPrice = double.NaN;
                highSinceEntry = double.NaN;
                lowSinceEntry = double.NaN;
				lastSessionDate = DateTime.MinValue;
            }
        }

        protected override void OnBarUpdate()
        {
            // Ensure both series have data before proceeding
            if (CurrentBars[0] < BarsRequiredToTrade || CurrentBars[1] < BarsRequiredToTrade)
                return;

			ResetSessionState(Times[BarsInProgress][0].Date);

            // Tick series drives intrabar management
            if (BarsInProgress == 1)
            {
                ManageTrailingStop();
                return;
            }

            // Only run trade logic on the primary series
            if (BarsInProgress != 0)
                return;

			UpdateInfoText(GetDebugText());

            if (ordersPlaced)
                return;

            DateTime now = Times[0][0];
            int entryMinuteTarget = EntryMinute + 1;
            int entryHourTarget = EntryHour;
            if (entryMinuteTarget >= 60)
            {
                entryMinuteTarget -= 60;
                entryHourTarget = (entryHourTarget + 1) % 24;
            }

            if (now.Hour == entryHourTarget &&
                now.Minute == entryMinuteTarget)
            {
                double ask = GetAskPrice();
                double bid = GetBidPrice();
                double tick = TickSize;

                Log($"[{State}] Bid: {bid}, Ask: {ask}, TickSize: {tick}");

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
                Log($"Number of contracts to trade: {numberOfContracts}");

                if (profitPoints == 0 && DollarTarget > 0)
                {
                    profitPoints = DollarTarget / (dollarPerPoint * numberOfContracts);
                    Log($"Calculated profit points from dollar target: {profitPoints}");
                }

                if (useStop)
                {
                    stopPoints = DollarStop / (dollarPerPoint * numberOfContracts);
                    Log($"Calculated stop points from dollar stop: {stopPoints}");
                    trailingStopDistance = stopPoints;
                }

                double longEntry = Instrument.MasterInstrument.RoundToTickSize(bid);
                double shortEntry = Instrument.MasterInstrument.RoundToTickSize(ask);

                Log($"Calculated longEntry: {longEntry}, shortEntry: {shortEntry}");

                if (LongTrade && !double.IsNaN(longEntry))
                {
                    double target = Instrument.MasterInstrument.RoundToTickSize(longEntry + profitPoints);
                    Log($"Submitting Long Entry at {longEntry} with target {target}");
                    SetProfitTarget("LongEntry", CalculationMode.Price, target);
                    if (useStop && !double.IsNaN(stopPoints))
                    {
                        double stop = Instrument.MasterInstrument.RoundToTickSize(longEntry - stopPoints);
                        SetStopLoss("LongEntry", CalculationMode.Price, stop, false);
                        currentStopPrice = stop;
                        highSinceEntry = longEntry;
                        Log($"Long stop set at {stop}");
                    }
					
					if (LimitOrder)
                    	EnterLongLimit(1, true, numberOfContracts, longEntry, "LongEntry");
					else
						EnterLong(1, numberOfContracts, "LongEntry");

                    Log("Long order submitted at: " + now.ToString("HH:mm:ss"));
                }

                if (!LongTrade && !double.IsNaN(shortEntry))
                {
                    double target = Instrument.MasterInstrument.RoundToTickSize(shortEntry - profitPoints);
                    Log($"Submitting Short Entry at {shortEntry} with target {target}");
                    SetProfitTarget("ShortEntry", CalculationMode.Price, target);
                    if (useStop && !double.IsNaN(stopPoints))
                    {
                        double stop = Instrument.MasterInstrument.RoundToTickSize(shortEntry + stopPoints);
                        SetStopLoss("ShortEntry", CalculationMode.Price, stop, false);
                        currentStopPrice = stop;
                        lowSinceEntry = shortEntry;
                        Log($"Short stop set at {stop}");
                    }
					if (LimitOrder)
                    	EnterShortLimit(1, true, numberOfContracts, shortEntry, "ShortEntry");
					else
						EnterShort(1, numberOfContracts, "ShortEntry");
                    
					Log("Short order submitted at: " + now.ToString("HH:mm:ss"));
                }

                if (LongTrade && double.IsNaN(longEntry))
                    Log("No long order submitted due to invalid entry price.");

                if (!LongTrade && double.IsNaN(shortEntry))
                    Log("No short order submitted due to invalid entry price.");

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
			Log($"[OnOrderUpdate] {time:HH:mm:ss} | Name: {order.Name}, State: {orderState}, Limit: {limitPrice}, Fill: {filled}, AvgPrice: {averageFillPrice}");
		}


        private double GetBidPrice()
        {
            double price = GetCurrentBid();
            if (price <= 0 || double.IsNaN(price))
                price = Closes[1][0];
            if (price <= 0 || double.IsNaN(price))
                price = Closes[0][0];
            return price;
        }

        private double GetAskPrice()
        {
            double price = GetCurrentAsk();
            if (price <= 0 || double.IsNaN(price))
                price = Closes[1][0];
            if (price <= 0 || double.IsNaN(price))
                price = Closes[0][0];
            return price;
        }

        private double GetDollarPerPoint()
        {
            return Instrument.MasterInstrument.PointValue;
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

            Log($"Account Cash: {cash}, Unrealized: {unrealized}, NetEquity: {netEquity}");
            Log($"Equity used: {equityUsed}, Remaining Drawdown: {remainingDrawdown}");

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
                    Log($"[TRAIL] Long stop moved to {currentStopPrice:F2} (highSinceEntry={highSinceEntry:F2})");
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
                    Log($"[TRAIL] Short stop moved to {currentStopPrice:F2} (lowSinceEntry={lowSinceEntry:F2})");
                }
            }
        }

		private void ResetSessionState(DateTime currentSessionDate)
		{
			if (currentSessionDate == lastSessionDate)
				return;

			ordersPlaced = false;
			trailingStopDistance = double.NaN;
			currentStopPrice = double.NaN;
			highSinceEntry = double.NaN;
			lowSinceEntry = double.NaN;
			lastSessionDate = currentSessionDate;

			Log($"Session reset for {currentSessionDate:yyyy-MM-dd}");
		}

		private void Log(string message)
		{
			if (Debug)
				Print(message);
		}
    }
}
