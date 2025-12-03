using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.ComponentModel;
using System.Xml.Serialization;
using System.IO;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;


namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
public class ORBO : Strategy
{
    public ORBO()
    {
        VendorLicense(204);
    }

#region Settings
    // [NinjaScriptProperty]
    // [Display(Name = "Instrument", Description = "Select the instrument you want to trade", Order = 0,
    //          GroupName = "A. Config")]
    //public StrategyPreset PresetSetting { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Contracts", Description = "Number of contracts to take", Order = 1, GroupName = "A. Config")]
    public int NumberOfContracts {
        get; set;
    }

    [NinjaScriptProperty]
    [Display(Name = "Entry Confirmation", Description = "Show popup confirmation before each entry", Order = 2,
             GroupName = "A. Config")]
    public bool RequireEntryConfirmation {
        get; set;
    }

    [NinjaScriptProperty]
    [Display(Name = "Anti Hedge", Description = "Dont take trade in opposite direction to prevent hedging", Order = 3,
             GroupName = "A. Config")]
    public bool AntiHedge {
        get; set;
    }

    [NinjaScriptProperty]
    [Display(
        Name = "Max Account Balance",
        Description =
            "When account reach this amount, ongoing orders and positions will close, no more trades will be taken",
        Order = 4, GroupName = "A. Config")]
    public double MaxAccountBalance {
        get; set;
    }

    [NinjaScriptProperty]
    [Display(Name = "Webhook URL", Description = "Sends POST JSON to this URL on trade signals", Order = 5, GroupName = "A. Config")]
    public string WebhookUrl { get; set; }

    // [NinjaScriptProperty]
    // [Display(Name = "Use Breakout Rearm Delay", Description = "Wait 5 minutes after breakout reset before allowing new entry", Order = 7, GroupName = "A. Config")]
    internal bool UseBreakoutRearmDelay { get; set; }

    // [NinjaScriptProperty]
    // [Display(Name = "Range Duration", Order = 1, GroupName = "B. Entry Conditions")]
    internal int BiasDuration { get; set; }

    // [NinjaScriptProperty]
    // [Display(Name = "Max Range (pts)", Description = "If session range exceeds this size, block trading for the day (0 = disabled)", Order = 2, GroupName = "B. Entry Conditions")]
    internal double MaxRangePoints { get; set; }

    // [NinjaScriptProperty]
    // [Range(1, 100, ErrorMessage = "EntryPercent must be between 1 and 100 ticks")]
    // [Display(Name = "Entry %", Description = "Entry price for limit order from 15 min OR", Order = 3, GroupName = "B. Entry Conditions")]
    internal double EntryPercent { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "TP %", Description = "Take profit distance", Order = 4, GroupName = "B. Entry Conditions")]
    public double TakeProfitPercent { get; set; }

    // [NinjaScriptProperty]
    // [Display(Name = "Hard SL %", Description = "Hard SL level", Order = 6, GroupName = "B. Entry Conditions")]
    internal double HardStopLossPercent { get; set; }

    // [NinjaScriptProperty]
    // [Display(Name = "Require 5m Close Below Return%", Description = "Require 5-minute candle close for return reset", Order = 9, GroupName = "B. Entry Conditions")]
    internal bool RequireCloseBelowReturn { get; set; }

    // [NinjaScriptProperty]
    // [Range(0, 100, ErrorMessage = "SLBETrigger must be 0–100 percent of range")]
    // [Display(
    //     Name = "SL BE Trigger %",
    //     Description = "Percent of the session range where BE flatten gets armed (same scale as Entry% / TP%)",
    //     Order = 10, GroupName = "B. Entry Conditions")]
    internal double SLBETrigger { get; set; }

    // [NinjaScriptProperty]
    // [Display(Name = "Max Bars In Trade", Description = "Exit trade after this many bars since entry", 
    //         Order = 11, GroupName = "B. Entry Conditions")]
    internal int MaxBarsInTrade { get; set; }

    // [NinjaScriptProperty]
    // [Range(0, 200, ErrorMessage = "Cancel Order % must be between 0 and 200")]
    // [Display(Name = "Cancel Order %", Description = "Cancel pending entry if price moves this % of session range away", 
    //         Order = 12, GroupName = "B. Entry Conditions")]
    internal double CancelOrderPercent { get; set; }

    // [NinjaScriptProperty]
    // [Display(Name = "Cancel Order After X Bars", 
    //         Description = "Cancel unfilled entry if it has not filled after this many bars",
    //         Order = 13, GroupName = "B. Entry Conditions")]
    internal int CancelOrderBars { get; set; }


    [NinjaScriptProperty]
    [Display(Name = "Session Start", Description = "When session is starting", Order = 1,
             GroupName = "C. Session Time")]
    public TimeSpan SessionStart
    {
        get {
            return sessionStart;
        }
        set {
            sessionStart = new TimeSpan(value.Hours, value.Minutes, 0);
        }
    }

    [NinjaScriptProperty]
    [Display(Name = "Session End",
             Description = "When session is ending, all positions and orders will be canceled when this time is passed",
             Order = 2, GroupName = "C. Session Time")]
    public TimeSpan SessionEnd
    {
        get {
            return sessionEnd;
        }
        set {
            sessionEnd = new TimeSpan(value.Hours, value.Minutes, 0);
        }
    }

    [NinjaScriptProperty]
    [Display(Name = "No Trades After",
             Description = "No more orders is being placed between this time and session end,", Order = 3,
             GroupName = "C. Session Time")]
    public TimeSpan NoTradesAfter
    {
        get {
            return noTradesAfter;
        }
        set {
            noTradesAfter = new TimeSpan(value.Hours, value.Minutes, 0);
        }
    }

    [XmlIgnore]
    [NinjaScriptProperty]
    [Display(Name = "Range Box Fill", Description = "Color of the background box between the range", Order = 4, GroupName = "C. Session Time")]
    public Brush RangeBoxBrush { get; set; }

    [Browsable(false)]
    public string RangeBoxBrushSerializable
    {
        get { return Serialize.BrushToString(RangeBoxBrush); }
        set { RangeBoxBrush = Serialize.StringToBrush(value); }
    }
	
    [NinjaScriptProperty]
    [Display(Name = "Skip Start", Description = "Start of skip window", Order = 1, GroupName = "C. Skip Times")]
    public TimeSpan SkipStart { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Skip End", Description = "End of skip window", Order = 2, GroupName = "C. Skip Times")]
    public TimeSpan SkipEnd { get; set; }

    // [NinjaScriptProperty]
    // [Display(Name = "Info Panel", Order = 1, GroupName = "D. Dev")]
    internal bool DebugMode {
        get; set;
    }

    // [NinjaScriptProperty]
    // [Display(Name = "Deviation", Order = 2, GroupName = "D. Dev")]
    internal double VarianceInTicks {
        get; set;
    }
#endregion

#region Variables
    private bool isRealTime = false;
    private bool orderPlaced = false;
    private bool wickLinesDrawn = false;
    private double entryPrice;
    private double sessionHigh, sessionLow;
    private string lastDate = string.Empty;
    private Order entryOrder;
    private Order hardStopOrder;
    private List<Order> profitOrders = new List<Order>();
    private string displayText = "Waiting...";
    private bool isStrategyAnalyzer = false;
    private TimeSpan sessionStart = new TimeSpan(9, 30, 0);
    private TimeSpan sessionEnd = new TimeSpan(16, 06, 0);
    private TimeSpan noTradesAfter = new TimeSpan(15, 26, 0);
    private TimeSpan skipStart = new TimeSpan(00, 00, 0);
    private TimeSpan skipEnd = new TimeSpan(00, 00, 0);
    private static readonly Random Random = new Random();
    private bool maxAccountLimitHit = false;
    private DateTime positionEntryTime = DateTime.MinValue;
    private bool lastTradeWasLong = false;
    private DateTime lastEntryTime = DateTime.MinValue;
    private readonly TimeSpan minTimeBetweenEntries = TimeSpan.FromSeconds(1);
    private int lastExitBarAnalyzer = -1;
    private double todayLongLimit = Double.NaN;
    private double todayShortLimit = Double.NaN;
    private double todayLongProfit = Double.NaN;
    private double todayLongStoploss = Double.NaN;
    private double todayShortProfit = Double.NaN;
    private double todayShortStoploss = Double.NaN;
    private string currentSignalName;
    private DateTime lastProtectionTime = DateTime.MinValue;
    private DateTime nextEvaluationTime = DateTime.MinValue;
    private double startingBalance = 0;
    private bool breakoutActive = false;
    private double lastFilledEntryPrice = 0;
    private bool isInBiasWindow = false;
    private bool hasCapturedRange = false;
    private bool rangeTooWide = false;
    private bool rangeTooWideLogged = false;
    private bool breakoutRearmPending = false;
    private DateTime breakoutRearmTime = DateTime.MinValue;
    // --- Heartbeat reporting ---
    private string heartbeatFile = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv");
    private System.Timers.Timer heartbeatTimer;
    private DateTime lastHeartbeatWrite = DateTime.MinValue;
    private int heartbeatIntervalSeconds = 10; // send heartbeat every 10 seconds
    private static readonly object heartbeatFileLock = new object();
    private string heartbeatId;
    // === Shared Anti-Hedge Lock System ===
    private static readonly object hedgeLockSync = new object();
    private static readonly string hedgeLockFile = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "AntiHedgeLock.csv");
	
	private bool beTriggerActive = false;
	private bool beFlattenTriggered = false;

    private double beTriggerLongPrice  = double.NaN;
    private double beTriggerShortPrice = double.NaN;

    private bool hasReturnedOnce = false;     // becomes true after price has returned inside the zone
    private bool tpWasHit = false;            // true right after TP hit
    private int entryBar = -1;
    private double pendingEntryPrice = double.NaN;
    private double cancelOrderDistanceAbs = 0;
    private int entryOrderBar = -1;

#endregion

#region State Management
    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
            SetDefaults();
        else if (State == State.Transition)
            isRealTime = false;
        else if (State == State.Realtime)
            isRealTime = true;
        else if (State == State.Historical)
            isStrategyAnalyzer = (Account == null || Account.Name == "Backtest");
        else if (State == State.DataLoaded) {
            heartbeatId = BuildHeartbeatId();
            // --- Heartbeat timer setup ---
            heartbeatTimer = new System.Timers.Timer(heartbeatIntervalSeconds * 1000);
            heartbeatTimer.Elapsed += (s, e) => WriteHeartbeat();
            heartbeatTimer.AutoReset = true;
            heartbeatTimer.Start();

            //ApplyPreset(PresetSetting);
        }
        else if (State == State.Terminated)
        {
            // --- Clean up heartbeat timer ---
            if (heartbeatTimer != null)
            {
                heartbeatTimer.Stop();
                heartbeatTimer.Dispose();
                heartbeatTimer = null;
            }
        }
    }

    private void SetDefaults()
    {
        Name = "ORBO";
        Calculate = Calculate.OnEachTick;
        IsOverlay = true;
        IsInstantiatedOnEachOptimizationIteration = false;
        IsUnmanaged = false;

        // 🟢 Default preset:
        //PresetSetting = StrategyPreset.NQ_MNQ_1;

        NumberOfContracts = 1;
        RequireEntryConfirmation = false;
        BiasDuration = 15;
        MaxRangePoints = 200;
        EntryPercent = 13.5;
        TakeProfitPercent = 40;
        HardStopLossPercent = 53;
        CancelOrderBars = 52;
        VarianceInTicks = 0;
        MaxAccountBalance = 0;
        MaxBarsInTrade = 0;
        CancelOrderPercent = 0;
        RangeBoxBrush = Brushes.Gold;
        RequireCloseBelowReturn = false;
        SLBETrigger = 0;
        WebhookUrl = "";
        UseBreakoutRearmDelay = false;
		
        SkipStart = skipStart;
        SkipEnd = skipEnd;

        DebugMode = true;
        AntiHedge = false;
    }
#endregion

#region OnBarUpdate
    protected override void OnBarUpdate()
    {
        ResetDailyStateIfNeeded();
	
        // ======================================================
        // 🔥 TIME-BASED EXIT: Flatten after X bars in trade
        // ======================================================
        if (MaxBarsInTrade > 0 &&
            Position.MarketPosition != MarketPosition.Flat &&
            entryBar >= 0)
        {
            // Safety for tick-based processing
            if (CurrentBar <= entryBar)
                return;

            int barsInTrade = CurrentBar - entryBar;

            if (barsInTrade >= MaxBarsInTrade)
            {
                DebugPrint($"⏱ ORBO: Time-based exit triggered after {barsInTrade} bars (limit {MaxBarsInTrade}).");

                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("TimeSL", currentSignalName);
                else
                    ExitShort("TimeSL", currentSignalName);

                SendWebhook("exit");

                return; // Prevent double exits
            }
        }

        // ======================================================
        // 🚫 CANCEL PENDING ENTRY IF PRICE RUNS AWAY
        // ======================================================
        if (CancelOrderPercent > 0 &&
            cancelOrderDistanceAbs > 0 &&
            entryOrder != null &&
            entryOrder.OrderState == OrderState.Working &&
            Position.MarketPosition == MarketPosition.Flat)
        {
            double bid = GetCurrentBid();
            double ask = GetCurrentAsk();

            double mid;
            if (bid > 0 && ask > 0 && Math.Abs(ask - bid) < 50 * TickSize)
                mid = (bid + ask) / 2.0;
            else
                mid = Close[0];  

            double distance = Math.Abs(mid - pendingEntryPrice);

            if (distance >= cancelOrderDistanceAbs)
            {
                DebugPrint($"❌ Pending entry canceled — price moved {distance:F2} (limit {cancelOrderDistanceAbs:F2}, {CancelOrderPercent}%)");

                CancelOrder(entryOrder);
                entryOrder = null;
                orderPlaced = false;
                entryOrderBar = -1;
                pendingEntryPrice = double.NaN;

                tpWasHit = true;
                hasReturnedOnce = false;

                breakoutActive = false;
                breakoutRearmPending = UseBreakoutRearmDelay;

                if (breakoutRearmPending)
                    breakoutRearmTime = Times[0][0].AddMinutes(5);

                SendWebhook("cancel");
                return;
            }
        }

        //-------------------------------------------------------------
        // 🕒 CANCEL UNFILLED ENTRY IF TOO MANY BARS HAVE PASSED
        //-------------------------------------------------------------
        if (CancelOrderBars > 0 &&
            entryOrder != null &&
            entryOrder.OrderState == OrderState.Working &&
            Position.MarketPosition == MarketPosition.Flat &&
            entryOrderBar >= 0)
        {
            int barsWaiting = CurrentBar - entryOrderBar;

            if (barsWaiting >= CancelOrderBars)
            {
                DebugPrint($"❌ Pending entry canceled — waited {barsWaiting} bars (limit {CancelOrderBars})");

                CancelOrder(entryOrder);
                entryOrder = null;
                orderPlaced = false;
                entryOrderBar = -1;
                pendingEntryPrice = double.NaN;

                // Same reset logic as CancelOrderPercent
                tpWasHit = true;
                hasReturnedOnce = false;
                breakoutActive = false;

                breakoutRearmPending = UseBreakoutRearmDelay;
                if (breakoutRearmPending)
                    breakoutRearmTime = Times[0][0].AddMinutes(5);

                SendWebhook("cancel");
                return;
            }
        }

		// === Skip window cross detection === 
		if (CurrentBar < 2) // need at least 2 bars for Time[1], Close[1], etc.
        	return;
		
		if (IsFirstTickOfBar)
		{
		    bool crossedSkipWindow =
		        (!TimeInSkip(Time[1]) && TimeInSkip(Time[0]))   // just entered a skip window
		        || (TimeInSkip(Time[1]) && !TimeInSkip(Time[0])); // just exited a skip window
		
		    if (crossedSkipWindow)
		    {
		        if (TimeInSkip(Time[0]))
		        {
		            if (DebugMode)
		               DebugPrint($"{Time[0]} - ⛔ Entered skip window");
		
		            if (Position.MarketPosition != MarketPosition.Flat) {
						 if (Position.MarketPosition == MarketPosition.Long)
			                ExitLong("SkipWindow", currentSignalName);
			            else
			                ExitShort("SkipWindow", currentSignalName);
		                //TryExitAll(Close[0], "SkipWindow");
		            } else
		            {
		                CancelAllOrders();
		                SendWebhook("cancel");
		            }
		        }
		        else
		        {
		            if (DebugMode)
		                DebugPrint($"{Time[0]} - ✅ Exited skip window");
		        }
		    }
		}

		// 🔒 HARD GUARD: absolutely no logic while inside the skip window
		if (TimeInSkip(Time[0]))
		    return;
		
        // Main series (BarsInProgress == 0)
        if (ShouldSkipBarUpdate())
            return;

        if (DebugMode)
            UpdateInfoText(GetDebugText());

        if (ShouldAccountBalanceExit())
            return;

        TimeSpan now = Times[0][0].TimeOfDay;
        TimeSpan biasStart = new TimeSpan(SessionStart.Hours, SessionStart.Minutes + 1, 0);
        TimeSpan biasEnd = biasStart.Add(TimeSpan.FromMinutes(BiasDuration));

        if (now >= biasStart && now < biasStart.Add(TimeSpan.FromMinutes(1)))
        {
            // Reset range tracking at 9:31
            sessionHigh = High[0];
            sessionLow = Low[0];
            isInBiasWindow = true;
            hasCapturedRange = false;
        }
        else if (now >= biasStart && now < biasEnd && !hasCapturedRange)
        {
            // Keep tracking session high/low during bias
            sessionHigh = Math.Max(sessionHigh, High[0]);
            sessionLow = Math.Min(sessionLow, Low[0]);
        }
        else if (now >= biasEnd && isInBiasWindow && !hasCapturedRange)
        {
            // At 9:46 (bias window just finished), draw the persistent lines
            DrawSessionWickRangePersistent(biasStart, biasEnd, "WickRange", Brushes.DodgerBlue, DashStyleHelper.Solid,
                                           2);

            hasCapturedRange = true;
            isInBiasWindow = false;
        }

        ExitIfSessionEnded();
        CancelEntryIfAfterNoTrades();
        CancelOrphanOrdersIfSessionOver();

        // 🔁 Breakout reset should always be checked per tick or every 5m based on setting
        bool noOpenOrders = !HasOpenOrders();
        bool isFlat = Position.MarketPosition == MarketPosition.Flat;

        // Block trading on oversized ranges (resets each day)
        if (rangeTooWide && isFlat)
            return;
		
		if (SLBETrigger > 0 && Position.MarketPosition != MarketPosition.Flat)
        {
            // Choose a stable intrabar price for comparison
            double bid = GetCurrentBid();
            double ask = GetCurrentAsk();
            double mid = (bid > 0 && ask > 0) ? (bid + ask) / 2.0 : Close[0];

            // --- Arm the BE logic once price crosses the BE trigger line ---
            if (!beTriggerActive)
            {
                if (Position.MarketPosition == MarketPosition.Long && !double.IsNaN(beTriggerLongPrice) && mid >= beTriggerLongPrice)
                {
                    beTriggerActive = true;
                    DebugPrint($"🟢 BE trigger ARMED at {beTriggerLongPrice:F2} ({SLBETrigger:0.#}% of range)");
                }
                else if (Position.MarketPosition == MarketPosition.Short && !double.IsNaN(beTriggerShortPrice) && mid <= beTriggerShortPrice)
                {
                    beTriggerActive = true;
                    DebugPrint($"🟢 BE trigger ARMED at {beTriggerShortPrice:F2} ({SLBETrigger:0.#}% of range)");
                }
            }

            // --- Once armed: flatten if we retrace to BE or worse ---
            if (beTriggerActive && !beFlattenTriggered)
            {
                // Use entry price for pure BE, or keep your PnL<=0 guard — both are fine.
                double entry = lastFilledEntryPrice > 0 ? lastFilledEntryPrice : entryPrice; // fallback
                bool retracedToBE =
                    (Position.MarketPosition == MarketPosition.Long  && mid <= entry) ||
                    (Position.MarketPosition == MarketPosition.Short && mid >= entry);

                if (retracedToBE)
                {
                    beFlattenTriggered = true;
                    DebugPrint("🔻 Retraced to BE or worse — flattening position.");
                    TryExitAll(mid, "BEFlatten");
                }
            }
        }

        if (breakoutActive && isFlat && noOpenOrders)
        {
            if (HasReturnedToBreakoutResetZone())
            {
                DebugPrint("🔁 Price returned to breakout reset zone. Resetting breakout state.");
                breakoutActive = false;

                if (UseBreakoutRearmDelay)
                {
                    breakoutRearmPending = true;
                    breakoutRearmTime = Times[0][0].AddMinutes(5);
                    DebugPrint($"🕒 Rearm delay active — new entries paused until {breakoutRearmTime:HH:mm}.");
                }
            }
        }

        // --- Detect the first return after a TP hit ---
        if (tpWasHit && !hasReturnedOnce)
        {
            if (HasReturnedToBreakoutResetZone())
            {
                hasReturnedOnce = true;
                DebugPrint("↩️ Price has returned inside breakout zone — rearm ready after close outside.");
            }
        }

        if (IsFirstTickOfBar && Times[0][0] >= nextEvaluationTime)
        {
            TryEntrySignal();
            nextEvaluationTime = nextEvaluationTime.AddMinutes(5);
        }
    }
	
    private bool TimeInSkip(DateTime time)
    {
        TimeSpan now = time.TimeOfDay;

        bool inSkip1 = false;

        // ✅ Skip1 only active if both are not 00:00:00
        if (SkipStart != TimeSpan.Zero && SkipEnd != TimeSpan.Zero)
        {
            inSkip1 = (SkipStart < SkipEnd)
                ? (now >= SkipStart && now <= SkipEnd)
                : (now >= SkipStart || now <= SkipEnd); // overnight handling
        }
		
        return inSkip1;
    }

    private bool ShouldSkipBarUpdate()
    {
        if (BarsInProgress != 0)
            return true;
        if (CurrentBars[0] < 24)
            return true;
        if (maxAccountLimitHit)
            return true;
        return false;
    }

	private string GetDebugText() =>
		GetPnLInfo() +
		"\nContracts: " + NumberOfContracts +
	    "\nAnti Hedge: " + (AntiHedge ? "✅" : "⛔") +
	    // "\nSL to BE: " + (
		//     beTriggerActive ? "✅" :
		//     SLBETrigger > 0 ? "⛔" :
		//     "⛔"
		// ) +
        "\nArmed: " + (IsReadyForNewOrder() ? "✅" : "⛔") +
	    "\nORBO v" + GetAddOnVersion();
	
    private bool ShouldAccountBalanceExit()
    {
        if (MaxAccountBalance <= 0 || maxAccountLimitHit)
            return false;

        double netEquity;

        if (!isStrategyAnalyzer && Account != null)
        {
            // Use live or playback account value (NetLiq includes realized + unrealized)
            netEquity = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
        }
        else
        {
            // Backtest fallback
            double realizedPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            double unrealizedPnL = Position.MarketPosition != MarketPosition.Flat
                                       ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0])
                                       : 0;
            netEquity = startingBalance + realizedPnL + unrealizedPnL;
        }

        if (netEquity >= MaxAccountBalance)
        {
            maxAccountLimitHit = true;
            DebugPrint($"🚨 Account limit hit! NetEquity: {netEquity:F2} >= MaxAccountBalance: {MaxAccountBalance:F2}");
            ExitLivePositionIfNeeded();
            CancelAllOrders();
            CleanupPosition();
            return true;
        }

        return false;
    }

    private void ExitLivePositionIfNeeded()
    {
        if (Position.MarketPosition != MarketPosition.Flat)
        {
            double exitPrice = Position.MarketPosition == MarketPosition.Long ? GetCurrentBid() : GetCurrentAsk();

            TryExitAll(exitPrice, "MaxProfitExit");
            CleanupPosition();
        }
    }

    private void ExitIfSessionEnded()
    {
        if ((!IsInSession()) && (Position.MarketPosition != MarketPosition.Flat))
            ExitAtSessionEnd();
    }

    private void CancelEntryIfAfterNoTrades()
    {
        if (Times[0][0].TimeOfDay >= NoTradesAfter && Times[0][0].TimeOfDay < SessionEnd &&
            Position.MarketPosition == MarketPosition.Flat)
        {
            if (entryOrder != null &&
                entryOrder.OrderState == OrderState.Working &&
                (entryOrder.Name == "Long" || entryOrder.Name == "Short"))
            {
                DebugPrint($"⏰ Canceling managed entry order: {entryOrder.Name} (Strategy Analyzer compatible)");
                CancelOrder(entryOrder);
                entryOrder = null;

                SendWebhook("cancel"); // 🔔 notify cancel of entry
            }
        }
    }

    private void CancelOrphanOrdersIfSessionOver()
    {
        if (!IsInSession() && HasOpenOrders())
            CancelAllOrders();
    }

    private void TryEntrySignal()
    {
        // 💡 Reset breakout state if price is back inside range
        bool noOpenOrders = !HasOpenOrders();
        bool isFlat = Position.MarketPosition == MarketPosition.Flat;

        // 🔄 Expire rearm delay first
        if (UseBreakoutRearmDelay && breakoutRearmPending && Times[0][0] >= breakoutRearmTime)
        {
            breakoutRearmPending = false;
            DebugPrint("✅ Breakout rearm delay expired — new entries allowed.");
        }

        // 💡 Then handle breakout reset
        if (breakoutActive && HasReturnedToBreakoutResetZone() && isFlat && noOpenOrders)
        {
            DebugPrint("🔁 Price returned to breakout reset zone. Resetting breakout state.");
            breakoutActive = false;

            if (UseBreakoutRearmDelay)
            {
                breakoutRearmPending = true;
                breakoutRearmTime = Times[0][0].AddMinutes(5);
                DebugPrint($"🕒 Rearm delay active — new entries paused until {breakoutRearmTime:HH:mm}.");
            }
        }

        if (IsFirstTickOfBar && wickLinesDrawn && !orderPlaced && IsInSession() &&
            Times[0][0].TimeOfDay < NoTradesAfter)
        {
            double entryCheckClose = Close[1]; // Use previous bar close for entry logic
            double minTickDistance = TickSize; // Or make it a setting

            bool isLong = entryCheckClose >= todayLongLimit + minTickDistance;
            bool isShort = entryCheckClose <= todayShortLimit - minTickDistance;

            // Only try if break out happened last candle
            // Potential fix for order being placed directly when price returns to return line
            if (UseBreakoutRearmDelay && breakoutRearmPending && Times[0][0] < breakoutRearmTime)
            {
                DebugPrint($"⏸ Waiting for next 5m close before new entry (until {breakoutRearmTime:HH:mm}).");
                return;
            }

            if (tpWasHit && !hasReturnedOnce)
            {
                DebugPrint("⏸️ TP was hit and price hasn't returned — no new entry allowed yet.");
                return; // skip any new entries
            }

            if (isLong || isShort)
            {
                if (!breakoutActive) {
                    PlaceEntryIfTriggered();
                    breakoutRearmPending = false;
                }
            }
        }
    }

    private void TryExitAll(double exitPrice, string reason)
    {
        if (Position.MarketPosition == MarketPosition.Flat)
            return;

        double marketPrice = Position.MarketPosition == MarketPosition.Long ? GetCurrentBid() : GetCurrentAsk();
        bool exitCondition = (Position.MarketPosition == MarketPosition.Long && marketPrice <= exitPrice) ||
                             (Position.MarketPosition == MarketPosition.Short && marketPrice >= exitPrice);

        if (!exitCondition)
            return;

        DebugPrint(
            $"Exit triggered ({reason})! Side={(Position.MarketPosition == MarketPosition.Long ? "Sell" : "BuyToCover")}, " +
            $"Qty={NumberOfContracts}, MarketPrice={marketPrice}, ExitPrice={exitPrice}, Position={Position.MarketPosition}");

        string name = "";

        if (reason.Equals("LookForClose"))
            name = "SL Body";
        else if (reason.Equals("SessionEnd"))
            name = "SessionExit";
        else if (reason.Equals("MaxProfitExit"))
            name = "MaxProfitExit";

        int qtyToExit = NumberOfContracts;
        if (qtyToExit > 0)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(name, currentSignalName);
            else
                ExitShort(name, currentSignalName);
        }

        // 🔔 Tell TradersPost we’re out
        SendWebhook("exit");
    }
#endregion

#region NinjaTrader Event Routing
    protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
                                          double averageFillPrice, OrderState orderState, DateTime time,
                                          ErrorCode error, string comment)
    {
        if (BarsInProgress == 1 && CurrentBars[1] < 1)
            return;
        if (BarsInProgress == 0 && CurrentBar < 20)
            return;
        if (IsInSession() && !isRealTime && !isStrategyAnalyzer)
            return;

        DebugPrint(
            $"OnOrderUpdate: Order={order?.Name}, State={orderState}, AvgFill={averageFillPrice}, Limit={limitPrice}, Stop={stopPrice}, Qty={quantity}, Filled={filled}, Comment={comment}");

        if (order != null && (order.Name == "Long" || order.Name == "Short"))
        {
            entryOrder = order;
        }

        if (order != null && orderState == OrderState.Filled && order.Name.Contains("Profit target"))
        {
            tpWasHit = true;
            hasReturnedOnce = false;  // must return before new entry allowed
            DebugPrint("💰 TP hit — waiting for price to return to breakout zone before new entry.");

            if (isStrategyAnalyzer)
                lastExitBarAnalyzer = CurrentBar;
        }
    }

    protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
                                              MarketPosition marketPosition, string orderId, DateTime time)
    {
        if (execution.Quantity <= 0 || execution.Order == null)
            return;

        // Entry
        if (execution.Order.Name.Contains("Long") || execution.Order.Name.Contains("Short"))
        {
            entryPrice = execution.Price;
            lastFilledEntryPrice = execution.Price;
            lastEntryTime = Times[0][0];
            lastProtectionTime = DateTime.Now;
            entryBar = CurrentBar;
			
			// reset BE state
    		beTriggerActive = false;
   			beFlattenTriggered = false;
        }

        // Reset state
        if (Position.MarketPosition == MarketPosition.Flat)
        {
            if (isStrategyAnalyzer)
                lastExitBarAnalyzer = CurrentBar;

            DebugPrint(
                $"Execution update: flat after exit. Qty={execution.Quantity}, Price={price}, OrderId={orderId}");
            CleanupPosition();
            ClearHedgeLock(Instrument.MasterInstrument.Name);
			
            // 🟢 Safety: clear TP restriction if exit was not caused by a TP
            if (!tpWasHit)
            {
                hasReturnedOnce = true;  // treat as returned (no restriction)
            }

			beTriggerActive = false;
    		beFlattenTriggered = false;
        }
    }
#endregion

#region Core Helpers &Drawing
    private double GetRandomizedPrice(double basePrice, double varianceInTicks)
    {
        if (VarianceInTicks <= 0)
            return basePrice;

        int steps = (int)(varianceInTicks / TickSize);
        int offsetSteps = Random.Next(-steps, steps + 1);
        double offset = offsetSteps * TickSize;
        return basePrice + offset;
    }

    private bool HasOpenOrders()
    {
        return (entryOrder != null && entryOrder.OrderState == OrderState.Working) ||
               (hardStopOrder != null && hardStopOrder.OrderState == OrderState.Working) ||
               profitOrders.Exists(o => o != null && o.OrderState == OrderState.Working);
    }

    private bool ShowEntryConfirmation(string orderType, double price, int quantity)
    {
        bool result = false;
        if (System.Windows.Application.Current == null) // Defensive: avoid crash in some headless NT environments
            return false;

        System.Windows.Application.Current.Dispatcher.Invoke(
            () =>
            {
                var message = $"Confirm {orderType} entry\nPrice: {price}\nQty: {quantity}";
                var res =
                    System.Windows.MessageBox.Show(message, "Entry Confirmation", System.Windows.MessageBoxButton.YesNo,
                                                   System.Windows.MessageBoxImage.Question);

                result = (res == System.Windows.MessageBoxResult.Yes);
            });

        return result;
    }

    private void printTradeDevider()
    {
        DebugPrint("");
        DebugPrint("---- NEW ENTRY ORDER PLACEMENT ----");
    }

    private void DebugPrint(string message)
    {
        if (DebugMode)
            LogToOutput2(Time[0] + " DEBUG: " + message);
    }

    private void LogToOutput2(string message)
    {
        NinjaTrader.Code.Output.Process(message, PrintTo.OutputTab2);
    }

    private void ResetDailyStateIfNeeded()
    {
        if (CurrentBars[0] < 1)
            return; // Prevent out-of-range access to Times[0][0]

        string today = Times[0][0].ToString("yyyyMMdd");
        if (today == lastDate)
            return;

        lastDate = today;
        orderPlaced = false;
        wickLinesDrawn = false;
        sessionHigh = sessionLow = 0;
        positionEntryTime = DateTime.MinValue;
        lastTradeWasLong = false;
        todayLongLimit = Double.NaN;
        todayShortLimit = Double.NaN;
        //todayLongReturnLimit = Double.NaN;
        //todayShortReturnLimit = Double.NaN;
        todayLongProfit = Double.NaN;
        todayLongStoploss = Double.NaN;
        todayShortProfit = Double.NaN;
        todayShortStoploss = Double.NaN;
        lastProtectionTime = DateTime.MinValue;
        nextEvaluationTime = DateTime.MinValue;
        breakoutActive = false;
        beTriggerLongPrice  = double.NaN;
        beTriggerShortPrice = double.NaN;
        beTriggerActive = false;
        beFlattenTriggered = false;
        tpWasHit = false;
        hasReturnedOnce = false;
        rangeTooWide = false;
        rangeTooWideLogged = false;

        if (isStrategyAnalyzer)
            lastExitBarAnalyzer = -1;
    }

    private void PlaceEntryIfTriggered()
    {
        if (rangeTooWide)
            return;

        SetProfitTarget(CalculationMode.Ticks, 0);
        SetStopLoss(CalculationMode.Ticks, 0);

        bool isLong = Close[1] > todayLongLimit;
        double rawLimitPrice = isLong ? todayLongLimit : todayShortLimit;
        double limitPrice = rawLimitPrice; // Start with default

        double takeProfit = isLong ? todayLongProfit : todayShortProfit;
        double stopLoss   = isLong ? todayLongStoploss : todayShortStoploss;
            
        string signalName = isLong ? "Long" : "Short";
        currentSignalName = signalName;

        // ⛔️ Skip if price has re-entered the range — unless we're forcing it
        if (!isStrategyAnalyzer && ((!isLong && Close[0] > todayShortLimit) || (isLong && Close[0] < todayLongLimit)))
        {
            return;
        }

        printTradeDevider();

        if (RequireEntryConfirmation)
        {
            string directionText = isLong ? "Long" : "Short";
            if (!ShowEntryConfirmation(directionText, limitPrice, NumberOfContracts))
            {
                DebugPrint($"User declined {directionText} entry via confirmation dialog.");
                return;
            }
        }

        if (!breakoutActive)
        {
            breakoutActive = true;
            DebugPrint("📣 New breakout phase started.");
        }

        SetProfitTarget(signalName, CalculationMode.Price, takeProfit);
        SetStopLoss(signalName, CalculationMode.Price, stopLoss, false);
        string otherSymbol = GetOtherInstrument();

        if (isLong) {
            if (AntiHedge && (HasOppositePosition(otherSymbol, MarketPosition.Long) || HasOppositeOrder(otherSymbol, MarketPosition.Long)))
            {
                DebugPrint($"SKIP {Instrument.MasterInstrument.Name} LONG, {otherSymbol} is SHORT.");
                return;
            }

            MarketPosition desiredDirection = MarketPosition.Long;
            string instrument = Instrument.MasterInstrument.Name;
            MarketPosition existing = GetHedgeLock(instrument);

            if (AntiHedge)
            {
                bool conflict =
                    (desiredDirection == MarketPosition.Long && existing == MarketPosition.Short) ||
                    (desiredDirection == MarketPosition.Short && existing == MarketPosition.Long);

                if (conflict)
                {
                    Print($"🛑 AntiHedge active: {instrument} is already {existing}. Skipping {desiredDirection} entry.");
                    return;
                }
            }

            EnterLongLimit(0, true, NumberOfContracts, limitPrice, signalName);
            pendingEntryPrice = limitPrice;
            entryOrderBar = CurrentBar;
            SendWebhook("buy", limitPrice, takeProfit, stopLoss);
            SetHedgeLock(instrument, desiredDirection);
        } else {
            if (AntiHedge && (HasOppositePosition(otherSymbol, MarketPosition.Short) || HasOppositeOrder(otherSymbol, MarketPosition.Short)))
            {
                DebugPrint($"SKIP {Instrument.MasterInstrument.Name} SHORT, {otherSymbol} is LONG.");
                return;
            }

            MarketPosition desiredDirection = MarketPosition.Short;
            string instrument = Instrument.MasterInstrument.Name;
            MarketPosition existing = GetHedgeLock(instrument);

            if (AntiHedge)
            {
                bool conflict =
                    (desiredDirection == MarketPosition.Long && existing == MarketPosition.Short) ||
                    (desiredDirection == MarketPosition.Short && existing == MarketPosition.Long);

                if (conflict)
                {
                    Print($"🛑 AntiHedge active: {instrument} is already {existing}. Skipping {desiredDirection} entry.");
                    return;
                }
            }

            EnterShortLimit(0, true, NumberOfContracts, limitPrice, signalName);
            pendingEntryPrice = limitPrice;
            entryOrderBar = CurrentBar;
            SendWebhook("sell", limitPrice, takeProfit, stopLoss);
            SetHedgeLock(instrument, desiredDirection);
        }

        tpWasHit = false;
        hasReturnedOnce = false;
        breakoutActive = true;
        orderPlaced = true;
        lastTradeWasLong = isLong;

        DebugPrint(
            $"Placed {(isLong ? "LONG" : "SHORT")} order at {limitPrice} with TP={takeProfit} and SL={stopLoss}, Signal={signalName}");
    }

    private bool IsEntryOrder(Order o) =>
        o == entryOrder || o.Name == "Long" || o.Name == "Short" || o.Name == "LongMid" || o.Name == "ShortMid";
    private bool IsProfitOrder(Order o) => o.Name.StartsWith("ProfitLong") || o.Name.StartsWith("ProfitShort");

    private void ExitAtSessionEnd()
    {
        DebugPrint(
            $"Session ended, closing position. MarketPosition={Position.MarketPosition}, Qty={Position.Quantity}");
        if (isStrategyAnalyzer)
            lastExitBarAnalyzer = CurrentBar;
        var act = Position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
        double dummyExitP = Position.MarketPosition == MarketPosition.Long ? GetCurrentBid() : GetCurrentAsk();
        TryExitAll(dummyExitP, "SessionEnd");
    }

    private void CancelAllOrders()
    {
        DebugPrint("CancelAllOrders called. EntryOrder=" + (entryOrder?.Name ?? "null") +
                   ", HardStopOrder=" + (hardStopOrder?.Name ?? "null") + ", ProfitOrders=" + profitOrders.Count);

        bool hadAnyWorking = HasOpenOrders();

        if (entryOrder != null)
        {
            CancelOrder(entryOrder);
            entryOrder = null;
        }
        if (hardStopOrder != null)
        {
            CancelOrder(hardStopOrder);
            hardStopOrder = null;
        }
        foreach (var o in profitOrders)
            CancelOrder(o);
        profitOrders.Clear();
        orderPlaced = false;

        if (hadAnyWorking)
            SendWebhook("cancel"); // 🔔 one cancel for the batch
    }

    private void CleanupPosition()
    {
        DebugPrint("CleanupPosition called. Resetting all order state and flags.");

        CancelAllOrders();
        entryOrder = null;
        hardStopOrder = null;
        profitOrders.Clear();
        orderPlaced = false;
        lastProtectionTime = DateTime.MinValue;
    }

    private bool IsInSession()
    {
        if (CurrentBars[0] < 1)
            return false;

        TimeSpan now = Times[0][0].TimeOfDay;
        TimeSpan start = new TimeSpan(SessionStart.Hours, SessionStart.Minutes + 1, 0);
        TimeSpan end = new TimeSpan(SessionEnd.Hours, SessionEnd.Minutes + 1, 0);
        return now >= start && now < end;
    }

    private static readonly Dictionary<string, string> CrossPairs = new Dictionary<string, string> {
        { "MNQ", "MES" },
        { "MES", "MNQ" },
        { "NQ", "ES" },
        { "ES", "NQ" },
    };

    private string GetOtherInstrument()
    {
        string thisSymbol = Instrument.MasterInstrument.Name.ToUpper();

        if (CrossPairs.ContainsKey(thisSymbol))
            return CrossPairs[thisSymbol];

        throw new Exception("Strategy not on supported instrument!");
    }

    private bool HasOppositeOrder(string targetInstrument, MarketPosition desiredDirection)
    {
        foreach (var order in Account.Orders)
        {
            if (order.Instrument.MasterInstrument.Name.Equals(targetInstrument, StringComparison.OrdinalIgnoreCase) &&
                (order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted))
            {
                if ((desiredDirection == MarketPosition.Long && order.OrderAction == OrderAction.SellShort) ||
                    (desiredDirection == MarketPosition.Short && order.OrderAction == OrderAction.Buy))
                {
                    DebugPrint("Has Opposite Order");
                    return true;
                }
            }
        }
        DebugPrint("Has No Opposite Order");
        return false;
    }

    private bool HasOppositePosition(string targetInstrument, MarketPosition desiredDirection)
    {
        foreach (var pos in Account.Positions)
        {
            if (pos.Instrument.MasterInstrument.Name.Equals(targetInstrument, StringComparison.OrdinalIgnoreCase))
            {
                if ((desiredDirection == MarketPosition.Long && pos.MarketPosition == MarketPosition.Short &&
                     pos.Quantity > 0) ||
                    (desiredDirection == MarketPosition.Short && pos.MarketPosition == MarketPosition.Long &&
                     pos.Quantity > 0))
                {
                    DebugPrint("Has Opposite Position");
                    return true;
                }
            }
        }
        DebugPrint("Has No Opposite Position");
        return false;
    }

    private bool HasReturnedToBreakoutResetZone()
    {
        bool returned = false;

        // 1️⃣ CLOSE-BASED RETURN
        if (RequireCloseBelowReturn)
        {
            // Using the previous CLOSE only
            if (lastTradeWasLong)
                returned = Close[1] <= todayLongLimit;
            else
                returned = Close[1] >= todayShortLimit;
        }

        // 2️⃣ WICK-BASED RETURN — INTRABAR SAFE
        else
        {
            // Intra-bar detection using wick (updates during bar)
            if (lastTradeWasLong)
                returned = Low[0] <= todayLongLimit;
            else
                returned = High[0] >= todayShortLimit;
        }

        if (returned)
        {
            DebugPrint($"[ReturnCheck] Mode={(RequireCloseBelowReturn ? "CLOSE" : "WICK")}, " +
                    $"lastTradeWasLong={lastTradeWasLong}, " +
                    $"PriceChecked={(lastTradeWasLong ? Low[0] : High[0]):F2}, " +
                    $"ReturnLimit={(lastTradeWasLong ? todayLongLimit : todayShortLimit):F2}, " +
                    $"Result=TRUE");
        }

        return returned;
    }

    public enum StrategyPreset
    {
        NQ_MNQ_1,
        NQ_MNQ_2
    }

    private void ApplyPreset(StrategyPreset preset)
    {
        switch (preset)
        {
        case StrategyPreset.NQ_MNQ_1:            
            EntryPercent = 13.5;
            //TakeProfitPercent = 40;
            HardStopLossPercent = 53;
            CancelOrderBars = 52;
            break;

        case StrategyPreset.NQ_MNQ_2:
            EntryPercent = 13.5;
            //TakeProfitPercent = 48.5;            
            HardStopLossPercent = 53;
            CancelOrderBars = 52;
            break;
        }

        //Debug print of selected settings
        // DebugPrint("\n== PRESET APPLIED: " + preset.ToString() + " ==");
        // DebugPrint("Contracts: " + NumberOfContracts);
        // DebugPrint("Entry %: " + EntryPercent);
        // DebugPrint("TP %: " + TakeProfitPercent);
        // DebugPrint("Hard SL %: " + HardStopLossPercent);
        // DebugPrint("Variance ticks: " + VarianceInTicks);
        // DebugPrint("Session Start: " + SessionStart);
        // DebugPrint("Session End: " + SessionEnd);
        // DebugPrint("No Trades After: " + NoTradesAfter);	
    }

    private void DrawSessionWickRangePersistent(TimeSpan startTime, TimeSpan endTime, string tagPrefix, Brush lineColor,
                                                DashStyleHelper style, int width)
    {
        if (wickLinesDrawn || Times[0][0].TimeOfDay < endTime || CurrentBar < BiasDuration)
            return;

        int s = -1, e = -1;
        for (int i = 0; i <= CurrentBar; i++)
        {
            var t = Times[0][i];
            if (t.Date != Times[0][0].Date)
                break;
            var tod = t.TimeOfDay;
            if (tod >= startTime && tod < endTime)
            {
                if (e < 0)
                    e = i;
                s = i;
            }
        }
        if (s < 0 || e < 0 || s <= e)
            return;
        sessionHigh = High[e];
        sessionLow = Low[e];
        for (int i = e + 1; i <= s; i++)
        {
            sessionHigh = Math.Max(sessionHigh, High[i]);
            sessionLow = Math.Min(sessionLow, Low[i]);
        }

        BarsPeriodType barType = BarsPeriod.BarsPeriodType;
        int barValue = BarsPeriod.Value;
        int totalMinutes = (int)(SessionEnd - SessionStart).TotalMinutes - barValue;
        int off = (totalMinutes / barValue);
        var tgH = $"{tagPrefix}_High_{Times[0][0]:yyyyMMdd}";
        var tgbH = $"{tagPrefix}_bHigh_{Times[0][0]:yyyyMMdd}";
        var tgmH = $"{tagPrefix}_mHLoss_{Times[0][0]:yyyyMMdd}";
        var tgmL = $"{tagPrefix}_mLLoss_{Times[0][0]:yyyyMMdd}";
        var tgL = $"{tagPrefix}_Low_{Times[0][0]:yyyyMMdd}";
        var tgbL = $"{tagPrefix}_bLow_{Times[0][0]:yyyyMMdd}";
        var tgPH1 = $"{tagPrefix}_Profit_High_1{Times[0][0]:yyyyMMdd}";
        var tgPL1 = $"{tagPrefix}_Profit_Low_1{Times[0][0]:yyyyMMdd}";
        var tgReturnHigh = $"{tagPrefix}_Return_High_1{Times[0][0]:yyyyMMdd}";
        var tgReturnLow = $"{tagPrefix}_Return_Low_1{Times[0][0]:yyyyMMdd}";
        var g = new SolidColorBrush(Color.FromArgb(70, 50, 205, 50));
        var y = new SolidColorBrush(Color.FromArgb(70, 255, 255, 0));
        var r = new SolidColorBrush(Color.FromArgb(70, 255, 0, 0));
        var o = new SolidColorBrush(Color.FromArgb(70, 255, 140, 0));
        var gr = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        var lg = new SolidColorBrush(Color.FromArgb(70, 50, 205, 50));

        var lineBrush = RangeBoxBrush.Clone();
        lineBrush.Opacity = 0.30;

        // Round session high/low to nearest tick
        double roundedHigh = Instrument.MasterInstrument.RoundToTickSize(sessionHigh);
        double roundedLow  = Instrument.MasterInstrument.RoundToTickSize(sessionLow);
        double rng = roundedHigh - roundedLow;

        if (MaxRangePoints > 0 && rng > MaxRangePoints)
        {
            rangeTooWide = true;
            if (!rangeTooWideLogged && DebugMode)
            {
                DebugPrint($"⛔ Range too wide: {rng:F2} pts > MaxRangePoints {MaxRangePoints:F2}. Trades blocked today.");
                rangeTooWideLogged = true;
            }
            CancelAllOrders();
        }

        double cancelOffset = rng * CancelOrderPercent / 100.0;
        cancelOrderDistanceAbs = Instrument.MasterInstrument.RoundToTickSize(cancelOffset);

        double entryOffset  = rng * EntryPercent / 100.0;
        double tpOffset     = rng * TakeProfitPercent / 100.0;
        double slOffset     = rng * HardStopLossPercent / 100.0;

        todayLongLimit        = Instrument.MasterInstrument.RoundToTickSize(roundedHigh + entryOffset);
        todayShortLimit       = Instrument.MasterInstrument.RoundToTickSize(roundedLow  - entryOffset);

        todayLongProfit       = Instrument.MasterInstrument.RoundToTickSize(GetRandomizedPrice(roundedHigh + tpOffset, VarianceInTicks));
        todayLongStoploss     = Instrument.MasterInstrument.RoundToTickSize(GetRandomizedPrice(roundedHigh - slOffset, VarianceInTicks));
        todayShortProfit      = Instrument.MasterInstrument.RoundToTickSize(GetRandomizedPrice(roundedLow  - tpOffset, VarianceInTicks));
        todayShortStoploss    = Instrument.MasterInstrument.RoundToTickSize(GetRandomizedPrice(roundedLow  + slOffset, VarianceInTicks));

        DebugPrint("\n-------------- New Day Targets --------------");
        DebugPrint($"Session High: {sessionHigh}");
        DebugPrint($"Session Low: {sessionLow}");
        DebugPrint($"Entry High: {todayLongLimit}");
        DebugPrint($"Entry Low: {todayShortLimit}");
        DebugPrint($"Long TP Raw: {sessionHigh + rng * TakeProfitPercent / 100.0} Randomized to {todayLongProfit}");
        DebugPrint($"Long SL Raw: {sessionHigh - rng * HardStopLossPercent / 100.0} Randomized to {todayLongStoploss}");
        DebugPrint($"Short TP Raw: {sessionLow - rng * TakeProfitPercent / 100.0} Randomized to {todayShortProfit}");
        DebugPrint($"Short SL Raw: {sessionLow + rng * HardStopLossPercent / 100.0} Randomized to {todayShortStoploss}");

        Draw.Line(this, tgH + "entry", false, s, todayLongLimit, s - off, todayLongLimit, y, style, width).ZOrder = -1;
        Draw.Line(this, tgH, false, s, sessionHigh, s - off, sessionHigh, lineBrush, style, width).ZOrder = -1;
        Draw.Line(this, tgmH + "maxLoss", false, s, todayLongStoploss, s - off, todayLongStoploss, r, DashStyleHelper.Solid, width).ZOrder = -1;
        Draw.Line(this, tgmL + "maxLoss", false, s, todayShortStoploss, s - off, todayShortStoploss, r, DashStyleHelper.Solid, width).ZOrder = -1;
        Draw.Line(this, tgL, false, s, sessionLow, s - off, sessionLow, lineBrush, style, width).ZOrder = -1;
        Draw.Line(this, tgL + "entry", false, s, todayShortLimit, s - off, todayShortLimit, y, style, width).ZOrder = -1;

        // Draw a filled rectangle between the session high and low
        string rectTag = $"{tagPrefix}_RangeBox_{Times[0][0]:yyyyMMdd}";
        Draw.Rectangle(this, rectTag,
            false,                   // AutoScale = false
            s,                       // Start bar index
            sessionHigh,             // Upper Y
            s - off,                 // End bar index
            sessionLow,              // Lower Y
            Brushes.Transparent,     // Border brush
            RangeBoxBrush,   // Fill brush
            10                       // Opacity (0-255)
        ).ZOrder = -1;               // ✅ Place behind the price bars

        // Take profit lines (PT1)
        double ptHigh = todayLongProfit;
        double ptLow = todayShortProfit;
        Draw.Line(this, $"{tagPrefix}_Profit_High_1{Times[0][0]:yyyyMMdd}", false, s, ptHigh, s - off, ptHigh, lg,
                  style, width).ZOrder = -1;
        Draw.Line(this, $"{tagPrefix}_Profit_Low_1{Times[0][0]:yyyyMMdd}", false, s, ptLow, s - off, ptLow, lg, style,
                  width).ZOrder = -1;

        // --- BE Trigger as % of full range (same scale as Entry% / TP%) ---
        double beOffset = rng * SLBETrigger / 100.0;

        beTriggerLongPrice  = Instrument.MasterInstrument.RoundToTickSize(roundedHigh + beOffset);
        beTriggerShortPrice = Instrument.MasterInstrument.RoundToTickSize(roundedLow  - beOffset);

        // === Break-Even Flatten Trigger Lines ===
        if (SLBETrigger > 0)
        {
            var tpBrush = new SolidColorBrush(Color.FromArgb(50, 50, 205, 50)); // similar to your TP brush
            Draw.Line(this, $"{tagPrefix}_BETrigger_Long_{Times[0][0]:yyyyMMdd}",
                false, s, beTriggerLongPrice, s - off, beTriggerLongPrice, tpBrush, DashStyleHelper.Dot, width).ZOrder = -1;

            Draw.Line(this, $"{tagPrefix}_BETrigger_Short_{Times[0][0]:yyyyMMdd}",
                false, s, beTriggerShortPrice, s - off, beTriggerShortPrice, tpBrush, DashStyleHelper.Dot, width).ZOrder = -1;
        }

        wickLinesDrawn = true;

        // Set first allowed evaluation time at the next 5-minute mark after 9:45
        int biasEndMinute = SessionStart.Minutes + BiasDuration;
        int rounded = ((biasEndMinute + 4) / 5) * 5; // round to next 5-min boundary
        DateTime firstEval = Times[0][0].Date.AddHours(SessionStart.Hours).AddMinutes(rounded + 1);
        nextEvaluationTime = firstEval;

        int noTradesAfter = (int)(NoTradesAfter - SessionStart).TotalMinutes - barValue;
        Draw.VerticalLine(this, $"NoTradesAfter_{Times[0][0]:yyyyMMdd}", s - (noTradesAfter / barValue), r,
                          DashStyleHelper.Solid, 2);
    }

    public void UpdateInfoText(string newText)
    {
        displayText = newText;

        Draw.TextFixed(owner: this, tag: "myStatusLabel", text: displayText, textPosition: TextPosition.BottomLeft,
                       textBrush: Brushes.DarkGray, font: new SimpleFont("Segoe UI", 14), outlineBrush: null,
                       areaBrush: Brushes.Black, areaOpacity: 85);
    }

    string GetAddOnVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        Version version = assembly.GetName().Version;
        return version.ToString();
    }

    private string GetPnLInfo()
    {
        // If contracts not configured, no info
        if (NumberOfContracts <= 0)
            return "TP: $0\nSL: $0";

        // Guard: if values not set yet, show placeholder
        if (double.IsNaN(todayLongLimit) || double.IsNaN(todayLongProfit) || double.IsNaN(todayLongStoploss))
            return "TP: $0\nSL: $0";

        // Tick value based on instrument
        double tickValue;
        if (Instrument.MasterInstrument.Name == "MNQ")
            tickValue = 0.50;
        else if (Instrument.MasterInstrument.Name == "NQ")
            tickValue = 5.00;
        else
            tickValue = Instrument.MasterInstrument.PointValue * TickSize;

        // Use session-wide Long side (always defined)
        double entryPrice = todayLongLimit;
        double targetPrice = todayLongProfit;
        double stopPrice   = todayLongStoploss;

        // Distances in ticks
        double tpTicks = Math.Abs(targetPrice - entryPrice) / TickSize;
        double slTicks = Math.Abs(entryPrice - stopPrice) / TickSize;

        // Dollar amounts
        double tpDollars = tpTicks * tickValue * NumberOfContracts;
        double slDollars = slTicks * tickValue * NumberOfContracts;

        return $"TP: ${tpDollars:0}\nSL: ${slDollars:0}";
    }
    
    private string BuildHeartbeatId()
    {
        string baseName = Name ?? GetType().Name;
        string instrumentName = Instrument != null ? Instrument.FullName : "UnknownInstrument";
        string accountName = Account != null ? Account.Name : "UnknownAccount";
        string barsInfo = BarsPeriod != null
            ? $"{BarsPeriod.BarsPeriodType}-{BarsPeriod.Value}"
            : "NoBars";

        // include key risk params so multiple configs don't collide
        string configKey = $"{EntryPercent}-{TakeProfitPercent}-{HardStopLossPercent}-{NumberOfContracts}";

        string raw = $"{baseName}-{instrumentName}-{barsInfo}-{accountName}-{configKey}";
        return raw.Replace(",", "_").Replace(Environment.NewLine, " ").Trim();
    }
    
    private void WriteHeartbeat()
    {
        try
        {
            string name = heartbeatId ?? this.Name ?? GetType().Name;
            string line = $"{name},{DateTime.Now:O}";
            List<string> lines = new List<string>();

            bool success = false;
            lock (heartbeatFileLock)
            {
                // --- Load existing lines (if any) ---
                if (System.IO.File.Exists(heartbeatFile))
                {
                    for (int i = 0; i < 3; i++) // retry on read conflict
                    {
                        try
                        {
                            lines.AddRange(System.IO.File.ReadAllLines(heartbeatFile));
                            break;
                        }
                        catch (IOException)
                        {
                            System.Threading.Thread.Sleep(100);
                        }
                    }
                }

                // --- Update or add this strategy’s line ---
                bool updated = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith(name + ",", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = line;
                        updated = true;
                        break;
                    }
                }
                if (!updated)
                    lines.Add(line);

                // --- Write back with retry ---
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        System.IO.File.WriteAllLines(heartbeatFile, lines.ToArray());
                        success = true;
                        break;
                    }
                    catch (IOException)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                }
            }

            //if (!success)
            //    Print($"⚠️ Failed to write heartbeat after 3 attempts — file still in use: {heartbeatFile}");
            //else
            //    Print($"💓 Heartbeat written for {name} at {DateTime.UtcNow:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            Print($"⚠️ Heartbeat write error: {ex.Message}");
        }
    }

    private void SetHedgeLock(string instrument, MarketPosition direction)
    {
        lock (hedgeLockSync)
        {
            var lines = File.Exists(hedgeLockFile)
                ? File.ReadAllLines(hedgeLockFile).ToList()
                : new List<string>();

            bool updated = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].StartsWith(instrument + ",", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"{instrument},{direction}";
                    updated = true;
                    break;
                }
            }
            if (!updated)
                lines.Add($"{instrument},{direction}");

            File.WriteAllLines(hedgeLockFile, lines);
        }
    }

    private MarketPosition GetHedgeLock(string instrument)
    {
        lock (hedgeLockSync)
        {
            if (!File.Exists(hedgeLockFile))
                return MarketPosition.Flat;

            foreach (var line in File.ReadAllLines(hedgeLockFile))
            {
                var parts = line.Split(',');
                if (parts.Length == 2 && parts[0].Equals(instrument, StringComparison.OrdinalIgnoreCase))
                {
                    if (Enum.TryParse(parts[1], out MarketPosition pos))
                        return pos;
                }
            }
            return MarketPosition.Flat;
        }
    }

    private void ClearHedgeLock(string instrument)
    {
        lock (hedgeLockSync)
        {
            if (!File.Exists(hedgeLockFile))
                return;

            var lines = File.ReadAllLines(hedgeLockFile).ToList();
            lines.RemoveAll(l => l.StartsWith(instrument + ",", StringComparison.OrdinalIgnoreCase));
            File.WriteAllLines(hedgeLockFile, lines);
        }
    }

    string GetTicker(Instrument instrument)
    {
        // Get the month code letter (F=Jan, G=Feb, H=Mar, etc.)
        string[] monthCodes = { "", "F", "G", "H", "J", "K", "M", "N", "Q", "U", "V", "X", "Z" };
        string monthCode = monthCodes[instrument.Expiry.Month];
        string yearCode = instrument.Expiry.Year.ToString().Substring(2, 2); // or full year if you prefer

        return $"{instrument.MasterInstrument.Name}{monthCode}20{yearCode}";
    }

    private void SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0)
    {
        if (State != State.Realtime)
        return;

        if (string.IsNullOrEmpty(WebhookUrl))
            return;

        try
        {
            string ticker = GetTicker(Instrument);

            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
            string json = "";

            switch (eventType.ToLower())
            {
                case "buy":
                case "sell":
                    json = $@"
                    {{
                        ""ticker"": ""{ticker}"",
                        ""action"": ""{eventType}"",
                        ""orderType"": ""limit"",
                        ""limitPrice"": {entryPrice},
                        ""quantityType"": ""fixed_quantity"",
                        ""quantity"": {NumberOfContracts},
                        ""signalPrice"": {entryPrice},
                        ""time"": ""{time}"",
                        ""takeProfit"": {{
                            ""limitPrice"": {takeProfit}
                        }},
                        ""stopLoss"": {{
                            ""type"": ""stop"",
                            ""stopPrice"": {stopLoss}
                        }}
                    }}";
                    break;

                case "exit":
                    json = $@"
                    {{
                        ""ticker"": ""{ticker}"",
                        ""action"": ""exit"",
                        ""orderType"": ""limit"",
                        ""limitPrice"": {entryPrice},
                        ""quantityType"": ""fixed_quantity"",
                        ""quantity"": {NumberOfContracts},
                        ""cancel"": true,
                        ""signalPrice"": {entryPrice},
                        ""time"": ""{time}""
                    }}";
                    break;

                case "cancel":
                    json = $@"
                    {{
                        ""ticker"": ""{ticker}"",
                        ""action"": ""cancel"",
                        ""time"": ""{time}""
                    }}";
                    break;
            }


            using (var client = new System.Net.WebClient())
            {
                client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                client.UploadString(WebhookUrl, "POST", json);
            }

            Print($"✅ Webhook sent to TradersPost: {eventType.ToUpper()} for {ticker}");
        }
        catch (Exception ex)
        {
            Print($"⚠️ Webhook error: {ex.Message}");
        }
    }

    // ✅ Determines if it's allowed to place a new order right now
    private bool IsReadyForNewOrder()
    {
        // Not OK if TP was hit and price hasn't returned yet
        if (tpWasHit && !hasReturnedOnce)
            return false;

        // Not OK if breakout rearm delay still active
        if (UseBreakoutRearmDelay && breakoutRearmPending && Times[0][0] < breakoutRearmTime)
            return false;

        // Otherwise OK (session checks already handled elsewhere)
        return true;
    }

    #endregion
    }
}
