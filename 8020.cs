//
// Simple 20/80 bracket limit strategy with intrabar triggering using a 1-tick series.
//
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
	public class Strategy8020 : Strategy
	{
		private const string LongSignal = "Long8020";
		private const string ShortSignal = "Short8020";
		private const string LongStopSignal = "LongStop8020";
		private const string LongTargetSignal = "LongTarget8020";
		private const string ShortStopSignal = "ShortStop8020";
		private const string ShortTargetSignal = "ShortTarget8020";

		private Order longEntryOrder;
		private Order shortEntryOrder;
		private Order longStopOrder;
		private Order longTargetOrder;
		private Order shortStopOrder;
		private Order shortTargetOrder;
		private bool ordersPlaced;
		private int activeBase;
		private int lastOrdersPlacedLogBase;
		private bool settingsLogged;
		private int tradesInRange;
		private bool tradeCompletedSinceLastTrigger;
		private bool sessionClosed;
		private bool inSkipWindow;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name = "8020";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 2;
				EntryHandling = EntryHandling.AllEntries;
				StopTargetHandling = StopTargetHandling.PerEntryExecution;
				IsUnmanaged = true;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				BarsRequiredToTrade = 1;

				TriggerValue = 30;
				TakeProfitPoints = 10;
				StopLossPoints = 10;
				MaxTradesPerRange = 1;

				SessionStart = new TimeSpan(9, 30, 0);
				SessionEnd = new TimeSpan(16, 0, 0);
				NoTradesAfter = new TimeSpan(15, 30, 0);
				SkipStart = TimeSpan.Zero;
				SkipEnd = TimeSpan.Zero;
				CloseAtSessionEnd = true;
				ForceCloseAtSkipStart = true;
				SessionBrush = Brushes.Gold;
				SkipBrush = Brushes.DarkSlateGray;
				NoTradesAfterBrush = Brushes.Red;
			}
			else if (State == State.Configure)
			{
				// Ensure unmanaged mode for SubmitOrderUnmanaged
				IsUnmanaged = true;
				// Add a 1-tick series for intrabar triggering (SampleIntrabarBacktest style)
				AddDataSeries(Data.BarsPeriodType.Tick, 1);
				TraceOrders = DebugEnabled;
				LogDebug($"Config: EntryHandling={EntryHandling} StopTargetHandling={StopTargetHandling}");
			}
			else if (State == State.DataLoaded)
			{
				activeBase = int.MinValue;
				lastOrdersPlacedLogBase = int.MinValue;
				tradesInRange = 0;
				tradeCompletedSinceLastTrigger = false;
				sessionClosed = false;
				inSkipWindow = false;
				LogDebug($"Init: EntryHandling={EntryHandling} StopTargetHandling={StopTargetHandling}");
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 0)
			{
				DrawSessionAndSkip();
				return;
			}
			if (BarsInProgress != 1)
				return;

			if (CurrentBars[1] < 2)
				return;

			if (ordersPlaced && !IsWorking(longEntryOrder) && !IsWorking(shortEntryOrder) && Position.MarketPosition == MarketPosition.Flat)
				ordersPlaced = false;

			if (!settingsLogged)
			{
				LogDebug($"Runtime settings: EntryHandling={EntryHandling} EntriesPerDirection={EntriesPerDirection} StopTargetHandling={StopTargetHandling} IsUnmanaged={IsUnmanaged}");
				settingsLogged = true;
			}

			if (!TimeInSession(Time[0]))
				return;

			bool nowInSkip = TimeInSkip(Time[0]);
			if (nowInSkip && !inSkipWindow)
			{
				inSkipWindow = true;
				LogDebug("Entered skip window.");
				if (ForceCloseAtSkipStart)
					FlattenAll("SkipWindow");
			}
			else if (!nowInSkip && inSkipWindow)
			{
				inSkipWindow = false;
				LogDebug("Exited skip window.");
			}

			if (nowInSkip)
				return;

			if (CrossedSessionEnd(Time[1], Time[0]) && !sessionClosed)
			{
				sessionClosed = true;
				LogDebug("Session end crossed.");
				if (CloseAtSessionEnd)
					FlattenAll("SessionEnd");
				return;
			}
			if (TimeInSession(Time[0]) && sessionClosed)
				sessionClosed = false;

			if (CrossedNoTradesAfter(Time[1], Time[0]))
			{
				LogDebug("NoTradesAfter crossed. Canceling entry orders.");
				CancelEntryOrders();
			}

			if (TimeInNoTradesAfter(Time[0]))
				return;

			double lastPrice = Closes[1][0];
			double prevPrice = Closes[1][1];

			double base100 = Math.Floor(lastPrice / 100.0) * 100.0;
			int baseKey = (int)base100;
			double low = base100 + 20.0;
			double high = base100 + 80.0;

			// Only evaluate triggers inside the 20-80 range.
			if (lastPrice < low || lastPrice > high)
				return;

			double triggerPrice = base100 + 20.0 + TriggerValue;
			bool hitTrigger =
				(lastPrice == triggerPrice) ||
				(prevPrice < triggerPrice && lastPrice > triggerPrice) ||
				(prevPrice > triggerPrice && lastPrice < triggerPrice);

			if (hitTrigger && baseKey != activeBase)
			{
				ResetForNewBase(baseKey);
			}

			bool canRearmInRange = tradesInRange < MaxTradesPerRange;
			if (hitTrigger && tradeCompletedSinceLastTrigger && baseKey == activeBase && canRearmInRange)
			{
				LogDebug($"Trigger hit after trade complete. Rearming in same range (trade {tradesInRange + 1}/{MaxTradesPerRange}).");
				CancelAllWorkingOrders();
				ordersPlaced = false;
				tradeCompletedSinceLastTrigger = false;
			}

			if (!ordersPlaced && hitTrigger && baseKey == activeBase && canRearmInRange)
			{
				double longEntry = Instrument.MasterInstrument.RoundToTickSize(low);
				double shortEntry = Instrument.MasterInstrument.RoundToTickSize(high);

				double longStop = Instrument.MasterInstrument.RoundToTickSize(longEntry - StopLossPoints);
				double longTarget = Instrument.MasterInstrument.RoundToTickSize(longEntry + TakeProfitPoints);
				double shortStop = Instrument.MasterInstrument.RoundToTickSize(shortEntry + StopLossPoints);
				double shortTarget = Instrument.MasterInstrument.RoundToTickSize(shortEntry - TakeProfitPoints);

				LogDebug($"Trigger hit. Sending orders. LongEntry={longEntry:0.00} LongSL={longStop:0.00} LongTP={longTarget:0.00} ShortEntry={shortEntry:0.00} ShortSL={shortStop:0.00} ShortTP={shortTarget:0.00}");

				longEntryOrder = SubmitOrderUnmanaged(
					1, OrderAction.Buy, OrderType.Limit, DefaultQuantity, longEntry, 0, null, LongSignal);
				shortEntryOrder = SubmitOrderUnmanaged(
					1, OrderAction.SellShort, OrderType.Limit, DefaultQuantity, shortEntry, 0, null, ShortSignal);

				ordersPlaced = true;
				activeBase = baseKey;
			}
			else if (hitTrigger && ordersPlaced)
			{
				if (baseKey != lastOrdersPlacedLogBase)
				{
					LogDebug($"Trigger hit but orders already placed. ActiveBase={activeBase} BaseKey={baseKey}");
					lastOrdersPlacedLogBase = baseKey;
				}
			}
		}

		protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
			double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
		{
			if (order == null)
				return;

			if (order.Name == LongSignal)
				longEntryOrder = order;
			else if (order.Name == ShortSignal)
				shortEntryOrder = order;
			else if (order.Name == LongStopSignal)
				longStopOrder = order;
			else if (order.Name == LongTargetSignal)
				longTargetOrder = order;
			else if (order.Name == ShortStopSignal)
				shortStopOrder = order;
			else if (order.Name == ShortTargetSignal)
				shortTargetOrder = order;

			LogDebug(
				$"OrderUpdate {order.Name} State={orderState} Qty={quantity} Filled={filled} AvgFill={averageFillPrice:0.00} " +
				$"Limit={limitPrice:0.00} Stop={stopPrice:0.00} Error={error} Comment={comment}",
				time);
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
			MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (execution?.Order == null)
				return;

			LogDebug(
				$"Execution {execution.Order.Name} State={execution.Order.OrderState} Qty={quantity} Price={price:0.00} " +
				$"MP={marketPosition} ExecId={executionId}",
				time);

			if (execution.Order.Name == LongSignal && execution.Order.OrderState == OrderState.Filled)
			{
				double entryPrice = execution.Order.AverageFillPrice;
				double stopPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice - StopLossPoints);
				double targetPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TakeProfitPoints);
				string oco = Guid.NewGuid().ToString("N");
				longStopOrder = SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.StopMarket, quantity, 0, stopPrice, oco, LongStopSignal);
				longTargetOrder = SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Limit, quantity, targetPrice, 0, oco, LongTargetSignal);
				LogDebug($"Long exits submitted. Stop={stopPrice:0.00} Target={targetPrice:0.00} OCO={oco}");
			}
			else if (execution.Order.Name == ShortSignal && execution.Order.OrderState == OrderState.Filled)
			{
				double entryPrice = execution.Order.AverageFillPrice;
				double stopPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice + StopLossPoints);
				double targetPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TakeProfitPoints);
				string oco = Guid.NewGuid().ToString("N");
				shortStopOrder = SubmitOrderUnmanaged(1, OrderAction.BuyToCover, OrderType.StopMarket, quantity, 0, stopPrice, oco, ShortStopSignal);
				shortTargetOrder = SubmitOrderUnmanaged(1, OrderAction.BuyToCover, OrderType.Limit, quantity, targetPrice, 0, oco, ShortTargetSignal);
				LogDebug($"Short exits submitted. Stop={stopPrice:0.00} Target={targetPrice:0.00} OCO={oco}");
			}
			else if (execution.Order.Name == LongStopSignal || execution.Order.Name == LongTargetSignal)
			{
				CancelIfWorking(longStopOrder);
				CancelIfWorking(longTargetOrder);
				RecordCompletedTrade();
			}
			else if (execution.Order.Name == ShortStopSignal || execution.Order.Name == ShortTargetSignal)
			{
				CancelIfWorking(shortStopOrder);
				CancelIfWorking(shortTargetOrder);
				RecordCompletedTrade();
			}
		}

		private void CancelIfWorking(Order order)
		{
			if (IsWorking(order))
				CancelOrder(order);
		}

		private void CancelAllWorkingOrders()
		{
			CancelIfWorking(longEntryOrder);
			CancelIfWorking(shortEntryOrder);
			CancelIfWorking(longStopOrder);
			CancelIfWorking(longTargetOrder);
			CancelIfWorking(shortStopOrder);
			CancelIfWorking(shortTargetOrder);
		}

		private void CancelEntryOrders()
		{
			CancelIfWorking(longEntryOrder);
			CancelIfWorking(shortEntryOrder);
		}

		private void FlattenAll(string reason)
		{
			CancelAllWorkingOrders();

			if (Position.MarketPosition == MarketPosition.Long && Position.Quantity > 0)
			{
				SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Market, Position.Quantity, 0, 0, null, $"FlattenLong_{reason}");
				LogDebug($"Flatten long due to {reason}.");
			}
			else if (Position.MarketPosition == MarketPosition.Short && Position.Quantity > 0)
			{
				SubmitOrderUnmanaged(1, OrderAction.BuyToCover, OrderType.Market, Position.Quantity, 0, 0, null, $"FlattenShort_{reason}");
				LogDebug($"Flatten short due to {reason}.");
			}
		}

		private void ResetForNewBase(int baseKey)
		{
			if (baseKey == activeBase)
				return;

			CancelAllWorkingOrders();
			activeBase = baseKey;
			ordersPlaced = false;
			tradesInRange = 0;
			tradeCompletedSinceLastTrigger = false;
			LogDebug($"New range detected. Base={baseKey} Reset trades in range.");
		}

		private void RecordCompletedTrade()
		{
			if (tradeCompletedSinceLastTrigger)
				return;

			tradesInRange++;
			tradeCompletedSinceLastTrigger = true;
			LogDebug($"Trade completed. TradesInRange={tradesInRange}/{MaxTradesPerRange} Base={activeBase}");
		}

		private bool IsWorking(Order order)
		{
			return order != null && (order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted);
		}

		private bool TimeInSession(DateTime time)
		{
			if (SessionStart == SessionEnd)
				return true;
			return IsTimeInRange(time.TimeOfDay, SessionStart, SessionEnd);
		}

		private bool TimeInSkip(DateTime time)
		{
			if (SkipStart == SkipEnd)
				return false;
			return IsTimeInRange(time.TimeOfDay, SkipStart, SkipEnd);
		}

		private bool TimeInNoTradesAfter(DateTime time)
		{
			if (NoTradesAfter == SessionEnd)
				return false;

			TimeSpan now = time.TimeOfDay;
			if (SessionStart < SessionEnd)
				return now >= NoTradesAfter && now < SessionEnd;
			return now >= NoTradesAfter || now < SessionEnd;
		}

		private bool CrossedSessionEnd(DateTime prev, DateTime now)
		{
			return IsTimeInRange(prev.TimeOfDay, SessionStart, SessionEnd)
			       && !IsTimeInRange(now.TimeOfDay, SessionStart, SessionEnd);
		}

		private bool CrossedNoTradesAfter(DateTime prev, DateTime now)
		{
			return !TimeInNoTradesAfter(prev) && TimeInNoTradesAfter(now);
		}

		private bool IsTimeInRange(TimeSpan now, TimeSpan start, TimeSpan end)
		{
			if (start < end)
				return now >= start && now < end;
			return now >= start || now < end;
		}

		private void DrawSessionAndSkip()
		{
			if (CurrentBar < 1)
				return;

			if (SessionStart != SessionEnd)
			{
				DateTime barTime = Time[0];
				DateTime sessionStartTime = barTime.Date + SessionStart;
				DateTime sessionEndTime = barTime.Date + SessionEnd;
				if (SessionStart > SessionEnd)
					sessionEndTime = sessionEndTime.AddDays(1);

				string sessionTag = $"8020_SessionFill_{sessionStartTime:yyyyMMdd_HHmm}";
				if (DrawObjects[sessionTag] == null)
				{
					Draw.Rectangle(this, sessionTag, false,
						sessionStartTime, 0,
						sessionEndTime, 30000,
						Brushes.Transparent, SessionBrush ?? Brushes.Gold, 20).ZOrder = -1;
				}

				if (NoTradesAfter != SessionEnd)
				{
					DateTime noTradesAfterTime = barTime.Date + NoTradesAfter;
					if (SessionStart > SessionEnd && NoTradesAfter < SessionStart)
						noTradesAfterTime = noTradesAfterTime.AddDays(1);
					Draw.VerticalLine(this, $"8020_NoTradesAfter_{sessionStartTime:yyyyMMdd_HHmm}", noTradesAfterTime,
						NoTradesAfterBrush ?? Brushes.Red, DashStyleHelper.Dash, 2);
				}
			}

			if (SkipStart != SkipEnd)
				DrawSkipWindow(SkipStart, SkipEnd);
		}

		private void DrawSkipWindow(TimeSpan start, TimeSpan end)
		{
			DateTime barTime = Time[0];
			DateTime windowStart = barTime.Date + start;
			DateTime windowEnd = barTime.Date + end;
			if (start > end)
				windowEnd = windowEnd.AddDays(1);

			string tag = $"8020_Skip_{windowStart:yyyyMMdd_HHmm}";
			if (DrawObjects[tag] == null)
			{
				Draw.Rectangle(this, tag, false,
					windowStart, 0,
					windowEnd, 30000,
					Brushes.Transparent, SkipBrush ?? Brushes.DarkSlateGray, 25).ZOrder = -1;
			}
		}

		private void LogDebug(string message, DateTime? time = null)
		{
			if (!DebugEnabled)
				return;

			DateTime stamp = time ?? (CurrentBar >= 0 ? Time[0] : Core.Globals.Now);
			Print($"{stamp} - {message}");
		}

		#region Properties
		[Range(1, 80), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "TriggerValue", GroupName = "NinjaScriptParameters", Order = 0)]
		public int TriggerValue { get; set; }

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "TakeProfitPoints", GroupName = "NinjaScriptParameters", Order = 1)]
		public int TakeProfitPoints { get; set; }

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "StopLossPoints", GroupName = "NinjaScriptParameters", Order = 2)]
		public int StopLossPoints { get; set; }

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "MaxTradesPerRange", GroupName = "NinjaScriptParameters", Order = 3)]
		public int MaxTradesPerRange { get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SessionStart", GroupName = "Session", Order = 0)]
		public TimeSpan SessionStart { get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SessionEnd", GroupName = "Session", Order = 1)]
		public TimeSpan SessionEnd { get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NoTradesAfter", GroupName = "Session", Order = 2)]
		public TimeSpan NoTradesAfter { get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SkipStart", GroupName = "Session", Order = 3)]
		public TimeSpan SkipStart { get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SkipEnd", GroupName = "Session", Order = 4)]
		public TimeSpan SkipEnd { get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "CloseAtSessionEnd", GroupName = "Session", Order = 5)]
		public bool CloseAtSessionEnd { get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "ForceCloseAtSkipStart", GroupName = "Session", Order = 6)]
		public bool ForceCloseAtSkipStart { get; set; }

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SessionBrush", GroupName = "Session", Order = 7)]
		public Brush SessionBrush { get; set; }

		[Browsable(false)]
		public string SessionBrushSerialize
		{
			get { return Serialize.BrushToString(SessionBrush); }
			set { SessionBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "SkipBrush", GroupName = "Session", Order = 8)]
		public Brush SkipBrush { get; set; }

		[Browsable(false)]
		public string SkipBrushSerialize
		{
			get { return Serialize.BrushToString(SkipBrush); }
			set { SkipBrush = Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NoTradesAfterBrush", GroupName = "Session", Order = 9)]
		public Brush NoTradesAfterBrush { get; set; }

		[Browsable(false)]
		public string NoTradesAfterBrushSerialize
		{
			get { return Serialize.BrushToString(NoTradesAfterBrush); }
			set { NoTradesAfterBrush = Serialize.StringToBrush(value); }
		}

		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "DebugEnabled", GroupName = "NinjaScriptParameters", Order = 4)]
		public bool DebugEnabled { get; set; }
		#endregion
	}
}
