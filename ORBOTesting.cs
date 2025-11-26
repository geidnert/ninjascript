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
public class ORBOTesting : Strategy
{
    public ORBOTesting()
    {
        //VendorLicense(204);
    }

#region Settings
    // [NinjaScriptProperty]
    // [Display(Name = "Instrument", Description = "Select the instrument you want to trade", Order = 0,
    //          GroupName = "A. Config")]
    // internal StrategyPreset PresetSetting { get; set; }

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
    [Display(Name = "Market Entry", Description = "If true, enter at market on bar close trigger instead of placing limit orders", Order = 3,
             GroupName = "A. Config")]
    public bool MarketEntry {
        get; set;
    }

    [NinjaScriptProperty]
    [Display(Name = "Anti Hedge", Description = "Dont take trade in opposite direction to prevent hedging", Order = 4,
             GroupName = "A. Config")]
    public bool AntiHedge {
        get; set;
    }

    [NinjaScriptProperty]
    [Display(
        Name = "Max Account Balance",
        Description =
            "When account reach this amount, ongoing orders and positions will close, no more trades will be taken",
        Order = 5, GroupName = "A. Config")]
    public double MaxAccountBalance {
        get; set;
    }

    [NinjaScriptProperty]
    [Display(Name = "Webhook URL", Description = "Sends POST JSON to this URL on trade signals", Order = 5, GroupName = "A. Config")]
    public string WebhookUrl { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Use Breakout Rearm Delay", Description = "Wait 5 minutes after breakout reset before allowing new entry", Order = 7, GroupName = "A. Config")]
    public bool UseBreakoutRearmDelay { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Range Duration", Order = 1, GroupName = "B. Entry Conditions")]
    public int BiasDuration { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Max Range (pts)", Description = "If session range exceeds this size, block trading for the day (0 = disabled)", Order = 2, GroupName = "B. Entry Conditions")]
    public double MaxRangePoints { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Range Duration (sec)", Description = "Overrides Range Duration when > 0; set to 0 to use Range Duration minutes", Order = 2, GroupName = "B. Entry Conditions")]
    public int RangeDurationSeconds { get; set; }

    [NinjaScriptProperty]
    [Range(1, 100, ErrorMessage = "EntryPercent must be between 1 and 100 ticks")]
    [Display(Name = "Entry %", Description = "Entry price for limit order from 15 min OR", Order = 3, GroupName = "B. Entry Conditions")]
    public double EntryPercent { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "TP %", Description = "Take profit distance", Order = 4, GroupName = "B. Entry Conditions")]
    public double TakeProfitPercent { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Hard SL %", Description = "Hard SL level", Order = 6, GroupName = "B. Entry Conditions")]
    public double HardStopLossPercent { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Min RR % (Market)", Description = "Minimum reward/risk (as percent, e.g. 25 = 0.25 RR) for market entries; 0 disables", Order = 7, GroupName = "B. Entry Conditions")]
    public double MinMarketRRPercent { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Evaluation Interval (sec)", Description = "Time between entry evaluations; default 300 sec (5m). 0 = use 300", Order = 8, GroupName = "B. Entry Conditions")]
    public int EvaluationIntervalSeconds { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Require 5m Close Below Return%", Description = "Require 5-minute candle close for return reset", Order = 9, GroupName = "B. Entry Conditions")]
    public bool RequireCloseBelowReturn { get; set; }

    [NinjaScriptProperty]
    [Range(0, 100, ErrorMessage = "SLBETrigger must be 0–100 percent of range")]
    [Display(
        Name = "SL BE Trigger %",
        Description = "Percent of the session range where BE flatten gets armed (same scale as Entry% / TP%)",
        Order = 10, GroupName = "B. Entry Conditions")]
    public double SLBETrigger { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Max Bars In Trade", Description = "Exit trade after this many bars since entry", 
            Order = 11, GroupName = "B. Entry Conditions")]
    public int MaxBarsInTrade { get; set; }

    [NinjaScriptProperty]
    [Range(0, 200, ErrorMessage = "Cancel Order % must be between 0 and 200")]
    [Display(Name = "Cancel Order %", Description = "Cancel pending entry if price moves this % of session range away", 
            Order = 12, GroupName = "B. Entry Conditions")]
    public double CancelOrderPercent { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Cancel Order After X Bars", 
            Description = "Cancel unfilled entry if it has not filled after this many bars",
            Order = 13, GroupName = "B. Entry Conditions")]
    public int CancelOrderBars { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Max VIX", Description = "0 = disable VIX filter; block trades if VIX > this value", Order = 14, GroupName = "B. Entry Conditions")]
    public double MaxVix { get; set; }


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

    [NinjaScriptProperty]
    [Display(Name = "Info Panel", Order = 1, GroupName = "D. Dev")]
    public bool DebugMode {
        get; set;
    }

    [NinjaScriptProperty]
    [Display(Name = "Deviation", Order = 2, GroupName = "D. Dev")]
    public double VarianceInTicks {
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
    private TimeSpan sessionEnd = new TimeSpan(15, 05, 0);
    private TimeSpan noTradesAfter = new TimeSpan(15, 01, 0);
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
    private bool breakoutRearmPending = false;
    private DateTime breakoutRearmTime = DateTime.MinValue;
    private bool rangeTooWide = false;
    private bool rangeTooWideLogged = false;
    private int cachedPrimaryBarSeconds = 0; // seconds of the drive series
    private const int driverBarsIndex = 1;   // 1-second series driving all logic
    // --- Heartbeat reporting ---
    private string heartbeatFile = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv");
    private System.Timers.Timer heartbeatTimer;
    private DateTime lastHeartbeatWrite = DateTime.MinValue;
    private int heartbeatIntervalSeconds = 10; // send heartbeat every 10 seconds
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
    private DateTime entryOrderTime = DateTime.MinValue;
    private int entryOrderBarPrimary = -1;
    private int entryBarPrimary = -1;
    // --- Trailing stop state (break-even management) ---
    private bool trailCancelPending   = false;
    private double pendingTrailStopPrice = double.NaN;
    private double trailingTarget     = double.NaN;
    private bool beStopMoveRequested  = false;
    private int vixIndex = 2;

#endregion

#region State Management
    protected override void OnStateChange()
    {
        if (State == State.SetDefaults)
            SetDefaults();
        else if (State == State.Configure)
        {
            // Use a consistent 1-second drive series so logic is independent of the chart's timeframe
            AddDataSeries(BarsPeriodType.Second, 1);
            AddDataSeries("^VIX", BarsPeriodType.Minute, 1);
        }
        else if (State == State.Transition)
            isRealTime = false;
        else if (State == State.Realtime)
            isRealTime = true;
        else if (State == State.Historical)
            isStrategyAnalyzer = (Account == null || Account.Name == "Backtest");
        else if (State == State.DataLoaded) {
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
        Name = "ORBOTesting";
        Calculate = Calculate.OnBarClose;
        IsOverlay = true;
        IsInstantiatedOnEachOptimizationIteration = false;
        IsUnmanaged = false;

        // 🟢 Default preset:
        //PresetSetting = StrategyPreset.NQ_MNQ_1;

        NumberOfContracts = 1;
        RequireEntryConfirmation = false;
        MarketEntry = false;
        BiasDuration = 15;
        MaxRangePoints = 0;
        RangeDurationSeconds = 0;
        EvaluationIntervalSeconds = 300;
        EntryPercent = 14.0;
        TakeProfitPercent = 32.4;
        HardStopLossPercent = 47.7;
        MinMarketRRPercent = 0;
        VarianceInTicks = 0;
        MaxAccountBalance = 0;
        MaxBarsInTrade = 0;
        CancelOrderPercent = 0;
        RangeBoxBrush = Brushes.Gold;
        MaxVix = 0;
        RequireCloseBelowReturn = false;
        SLBETrigger = 0;
        WebhookUrl = "";
        UseBreakoutRearmDelay = false;
		
        SkipStart = skipStart;
        SkipEnd = skipEnd;

        DebugMode = false;
        AntiHedge = false;
    }
#endregion

#region OnBarUpdate
    protected override void OnBarUpdate()
    {
        // Drive all logic off the 1-second series so results are timeframe-agnostic
        if (BarsInProgress != driverBarsIndex)
            return;
        if (!DriverSeriesReady(1))
            return;

        int currentBar = CurrentBars[driverBarsIndex];

        if (currentBar != entryBar)
        {
            // === LONG side exits ===
            if (Position.MarketPosition == MarketPosition.Long)
            {
                // --- Stop Loss FIRST for realism ---
                if (DriverLow(0) <= todayLongStoploss)
                {
                    ExitLongLimit(0, true, Position.Quantity, todayLongStoploss, "ManualSL", currentSignalName);
                    DebugPrint($"❌ SL hit manually at {todayLongStoploss}");
                }
                // --- Take Profit SECOND ---
                else if (DriverHigh(0) >= todayLongProfit)
                {
                    ExitLongLimit(0, true, Position.Quantity, todayLongProfit, "ManualTP", currentSignalName);
                    tpWasHit = true;
                    DebugPrint($"✅ TP hit manually at {todayLongProfit}");
                }
            }

            // === SHORT side exits ===
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                // --- Stop Loss FIRST ---
                if (DriverHigh(0) >= todayShortStoploss)
                {
                    ExitShortLimit(0, true, Position.Quantity, todayShortStoploss, "ManualSL", currentSignalName);
                    DebugPrint($"❌ SL hit manually at {todayShortStoploss}");
                }
                // --- Take Profit SECOND ---
                else if (DriverLow(0) <= todayShortProfit)
                {
                    ExitShortLimit(0, true, Position.Quantity, todayShortProfit, "ManualTP", currentSignalName);
                    tpWasHit = true;
                    DebugPrint($"✅ TP hit manually at {todayShortProfit}");
                }
            }
        }

        // ======================================================
        // 🔥 TIME-BASED EXIT: Flatten after X bars in trade
        // ======================================================
        if (MaxBarsInTrade > 0 &&
            Position.MarketPosition != MarketPosition.Flat &&
            entryBarPrimary >= 0 &&
            CurrentBars.Length > 0 &&
            CurrentBars[0] >= entryBarPrimary)
        {
            int barsInTrade = CurrentBars[0] - entryBarPrimary;

            if (barsInTrade >= MaxBarsInTrade)
            {
                DebugPrint($"⏱ Time-based exit after {barsInTrade} bars (limit {MaxBarsInTrade}).");

                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("TimeSL", currentSignalName);
                else
                    ExitShort("TimeSL", currentSignalName);

                SendWebhook("exit");
                return;
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
                mid = DriverClose(0);  

            double distance = Math.Abs(mid - pendingEntryPrice);

            if (distance >= cancelOrderDistanceAbs)
            {
                DebugPrint($"❌ Pending entry canceled — price moved {distance:F2} (limit {cancelOrderDistanceAbs:F2}, {CancelOrderPercent}%)");

                CancelOrder(entryOrder);
                entryOrder = null;
                entryOrderBar = -1;
                entryOrderBarPrimary = -1;
                entryOrderTime = DateTime.MinValue;
                pendingEntryPrice = double.NaN;

                tpWasHit = true;
                hasReturnedOnce = false;

                breakoutActive = false;
                breakoutRearmPending = UseBreakoutRearmDelay;

                if (breakoutRearmPending)
                    breakoutRearmTime = DriverTime(0).AddMinutes(5);

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
            entryOrderBarPrimary >= 0 &&
            CurrentBars.Length > 0 &&
            CurrentBars[0] >= entryOrderBarPrimary)
        {
            int barsWaiting = CurrentBars[0] - entryOrderBarPrimary;

            if (barsWaiting >= CancelOrderBars)
            {
                DebugPrint($"❌ Pending entry canceled — waited {barsWaiting} bars (limit {CancelOrderBars})");

                CancelOrder(entryOrder);
                entryOrder = null;
                entryOrderBar = -1;
                entryOrderBarPrimary = -1;
                entryOrderTime = DateTime.MinValue;
                pendingEntryPrice = double.NaN;

                // Same reset logic as CancelOrderPercent
                tpWasHit = true;
                hasReturnedOnce = false;
                breakoutActive = false;

                breakoutRearmPending = UseBreakoutRearmDelay;
                if (breakoutRearmPending)
                    breakoutRearmTime = DriverTime(0).AddMinutes(5);

                SendWebhook("cancel");
                return;
            }
        }

        ResetDailyStateIfNeeded();
	
		// === Skip window cross detection === 
		if (currentBar < 2) // need at least 2 bars for Time[1], Close[1], etc.
        	return;
		
		if (IsDriverFirstTick)
		{
		    bool crossedSkipWindow =
		        (!TimeInSkip(DriverTime(1)) && TimeInSkip(DriverTime(0)))   // just entered a skip window
		        || (TimeInSkip(DriverTime(1)) && !TimeInSkip(DriverTime(0))); // just exited a skip window
		
		    if (crossedSkipWindow)
		    {
		        if (TimeInSkip(DriverTime(0)))
		        {
		            if (DebugMode)
		               DebugPrint($"{DriverTime(0)} - ⛔ Entered skip window");
		
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
		                DebugPrint($"{DriverTime(0)} - ✅ Exited skip window");
		        }
		    }
		}

		// 🔒 HARD GUARD: absolutely no logic while inside the skip window
		if (TimeInSkip(DriverTime(0)))
		    return;
		
        // Drive logic off the 1-second series
        if (ShouldSkipBarUpdate())
            return;

        if (DebugMode)
            UpdateInfoText(GetDebugText());

        if (ShouldAccountBalanceExit())
            return;

        TimeSpan now = DriverTime(0).TimeOfDay;
        TimeSpan biasStart = SessionStart.Add(TimeSpan.FromMinutes(1));
        TimeSpan biasEnd = biasStart.Add(TimeSpan.FromSeconds(GetRangeDurationSeconds()));
        TimeSpan biasResetWindow = TimeSpan.FromSeconds(GetPrimaryBarSeconds());

        if (now >= biasStart && now < biasStart.Add(biasResetWindow))
        {
            // Reset range tracking at 9:31
            sessionHigh = DriverHigh(0);
            sessionLow = DriverLow(0);
            isInBiasWindow = true;
            hasCapturedRange = false;
        }
        else if (now >= biasStart && now < biasEnd && !hasCapturedRange)
        {
            // Keep tracking session high/low during bias
            sessionHigh = Math.Max(sessionHigh, DriverHigh(0));
            sessionLow = Math.Min(sessionLow, DriverLow(0));
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
        {
            return;
        }
		
		if (SLBETrigger > 0 && Position.MarketPosition != MarketPosition.Flat)
        {
            // Choose a stable intrabar price for comparison
            double bid = GetCurrentBid();
            double ask = GetCurrentAsk();
            double mid = (bid > 0 && ask > 0) ? (bid + ask) / 2.0 : DriverClose(0);

            // --- Arm the BE logic once price crosses the BE trigger line ---
            if (!beTriggerActive)
            {
                if (Position.MarketPosition == MarketPosition.Long && !double.IsNaN(beTriggerLongPrice) && mid >= beTriggerLongPrice)
                {
                    beTriggerActive = true;
                    beStopMoveRequested = false;
                    DebugPrint($"🟢 BE trigger ARMED at {beTriggerLongPrice:F2} ({SLBETrigger:0.#}% of range)");
                }
                else if (Position.MarketPosition == MarketPosition.Short && !double.IsNaN(beTriggerShortPrice) && mid <= beTriggerShortPrice)
                {
                    beTriggerActive = true;
                    beStopMoveRequested = false;
                    DebugPrint($"🟢 BE trigger ARMED at {beTriggerShortPrice:F2} ({SLBETrigger:0.#}% of range)");
                }
            }

            // --- Once armed: flatten if we retrace to BE or worse ---
            if (beTriggerActive && !beFlattenTriggered)
            {
                // Use entry price for pure BE, or keep your PnL<=0 guard — both are fine.
                double entry = lastFilledEntryPrice > 0 ? lastFilledEntryPrice : entryPrice; // fallback

                // Move the hard stop to BE once per activation (Strategy Analyzer safe)
                if (!beStopMoveRequested && entry > 0 && !double.IsNaN(entry))
                {
                    double beStop = Instrument.MasterInstrument.RoundToTickSize(entry);
                    ReplaceStopOrder(beStop);
                }

                bool stopManagingBE =
                    beStopMoveRequested ||
                    trailCancelPending ||
                    (hardStopOrder != null &&
                        (hardStopOrder.OrderState == OrderState.Accepted || hardStopOrder.OrderState == OrderState.Working));

                bool retracedToBE =
                    (Position.MarketPosition == MarketPosition.Long  && mid <= entry) ||
                    (Position.MarketPosition == MarketPosition.Short && mid >= entry);

                if (!stopManagingBE && retracedToBE)
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
                    breakoutRearmTime = DriverTime(0).AddMinutes(5);
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

        if (IsDriverFirstTick)
        {
            if (nextEvaluationTime == DateTime.MinValue)
                nextEvaluationTime = ComputeFirstEvalTime(DriverTime(0).Date);

            if (DriverTime(0) >= nextEvaluationTime)
            {
                TryEntrySignal();
                do
                {
                    nextEvaluationTime = nextEvaluationTime.AddSeconds(GetEvaluationIntervalSeconds());
                }
                while (DriverTime(0) >= nextEvaluationTime);
            }
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
        if (!DriverSeriesReady(24))
            return true;
        if (maxAccountLimitHit)
            return true;
        return false;
    }

    private bool DriverSeriesReady(int barsAgo = 0) =>
        CurrentBars.Length > driverBarsIndex && CurrentBars[driverBarsIndex] > barsAgo;

    private int DriverCurrentBar => DriverSeriesReady() ? CurrentBars[driverBarsIndex] : 0;

    private DateTime DriverTime(int barsAgo = 0) => Times[driverBarsIndex][barsAgo];
    private double DriverClose(int barsAgo = 0) => Closes[driverBarsIndex][barsAgo];
    private double DriverHigh(int barsAgo = 0) => Highs[driverBarsIndex][barsAgo];
    private double DriverLow(int barsAgo = 0) => Lows[driverBarsIndex][barsAgo];
    private bool IsDriverFirstTick => BarsInProgress == driverBarsIndex && IsFirstTickOfBar;

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
                                       ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, DriverClose(0))
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
        if (DriverTime(0).TimeOfDay >= NoTradesAfter && DriverTime(0).TimeOfDay < SessionEnd &&
            Position.MarketPosition == MarketPosition.Flat)
        {
            if (entryOrder != null &&
                entryOrder.OrderState == OrderState.Working &&
                (entryOrder.Name == "Long" || entryOrder.Name == "Short"))
            {
                DebugPrint($"⏰ Canceling managed entry order: {entryOrder.Name} (Strategy Analyzer compatible)");
                CancelOrder(entryOrder);
                entryOrder = null;
                entryOrderBar = -1;
                entryOrderBarPrimary = -1;
                entryOrderTime = DateTime.MinValue;

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
        if (rangeTooWide)
            return;

        // 🚫 VIX filter — disabled if MaxVix == 0
        if (MaxVix > 0 && VixReady())
        {
            double curVix = GetVix(0);

            if (curVix > MaxVix)
            {
                if (DebugMode)
                    DebugPrint($"⛔ Entry blocked: VIX {curVix:F2} > MaxVix {MaxVix}");

                return; // block entries only
            }
        }

        // 💡 Reset breakout state if price is back inside range
        bool noOpenOrders = !HasOpenOrders();
        bool isFlat = Position.MarketPosition == MarketPosition.Flat;

        // 🔄 Expire rearm delay first
        if (UseBreakoutRearmDelay && breakoutRearmPending && DriverTime(0) >= breakoutRearmTime)
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
                breakoutRearmTime = DriverTime(0).AddMinutes(5);
                DebugPrint($"🕒 Rearm delay active — new entries paused until {breakoutRearmTime:HH:mm}.");
            }
        }

        if (IsDriverFirstTick && wickLinesDrawn && !orderPlaced && IsInSession() &&
            DriverTime(0).TimeOfDay < NoTradesAfter)
        {
            double entryCheckClose = DriverClose(1); // Use previous bar close for entry logic
            double minTickDistance = TickSize; // Or make it a setting

            bool isLong = entryCheckClose >= todayLongLimit + minTickDistance;
            bool isShort = entryCheckClose <= todayShortLimit - minTickDistance;

            // Only try if break out happened last candle
            // Potential fix for order being placed directly when price returns to return line
            if (UseBreakoutRearmDelay && breakoutRearmPending && DriverTime(0) < breakoutRearmTime)
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
        // Trailing stop state machine: when an old SL cancel finishes, submit the replacement
        if (trailCancelPending
            && order != null
            && order.Name == "StopLoss"
            && order.Instrument.FullName == Instrument.FullName
            && orderState == OrderState.Cancelled)
        {
            DebugPrint("[TRAIL] Confirmed old SL CANCELLED – submitting new SL now.");

            trailCancelPending = false;

            if (!double.IsNaN(pendingTrailStopPrice) &&
                Position.MarketPosition != MarketPosition.Flat)
            {
                SubmitStopLoss(pendingTrailStopPrice);
            }

            pendingTrailStopPrice = double.NaN;
            // fall through to other handlers
        }

        // Track any working/accepted SL for trailing updates
        if (order != null
            && order.OrderType == OrderType.StopMarket
            && order.Instrument.FullName == Instrument.FullName
            && (order.OrderState == OrderState.Accepted || order.OrderState == OrderState.Working))
        {
            hardStopOrder  = order;
            trailingTarget = order.StopPrice;
            DebugPrint($"[TRAIL] SL working. trailingTarget={trailingTarget:F2}, name={order.Name}, action={order.OrderAction}");
        }

        if (!DriverSeriesReady(20))
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
                lastExitBarAnalyzer = DriverCurrentBar;
        }
    }

    protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
                                              MarketPosition marketPosition, string orderId, DateTime time)
    {
        if (execution.Quantity <= 0 || execution.Order == null)
            return;

        if (execution.Order != null && (execution.Order.Name.Contains("Long") || execution.Order.Name.Contains("Short")))
        {
            bool isLongEntry = execution.Order.Name.Contains("Long");

            entryPrice = execution.Price;
            lastFilledEntryPrice = execution.Price;
            entryBar = DriverCurrentBar; // remember bar index of the fill
            entryBarPrimary = CurrentBars.Length > 0 ? CurrentBars[0] : -1;
            tpWasHit = false;
            beTriggerActive = false;
            beFlattenTriggered = false;
            beStopMoveRequested = false;
            trailCancelPending = false;
            pendingTrailStopPrice = double.NaN;

            lastEntryTime = DriverTime(0);
            lastProtectionTime = DateTime.Now;
			
            // Submit protective SL so trailing moves are represented in Strategy Analyzer
            double stopPx = isLongEntry ? todayLongStoploss : todayShortStoploss;
            if (!double.IsNaN(stopPx))
                SubmitStopLoss(stopPx);
        }

        // Reset state
        if (Position.MarketPosition == MarketPosition.Flat)
        {
            if (isStrategyAnalyzer)
                lastExitBarAnalyzer = DriverCurrentBar;

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
            entryBarPrimary = -1;
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
        {
            var stamp = DriverSeriesReady() ? DriverTime(0) : DateTime.Now;
            LogToOutput2(stamp + " DEBUG: " + message);
        }
    }

    private void LogToOutput2(string message)
    {
        NinjaTrader.Code.Output.Process(message, PrintTo.OutputTab2);
    }

    private void ResetDailyStateIfNeeded()
    {
        if (!DriverSeriesReady())
            return; // Prevent out-of-range access to Times array

        string today = DriverTime(0).ToString("yyyyMMdd");
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
        entryOrderBar = -1;
        entryOrderBarPrimary = -1;
        entryOrderTime = DateTime.MinValue;
        pendingEntryPrice = double.NaN;
        entryBar = -1;
        entryBarPrimary = -1;
        beTriggerLongPrice  = double.NaN;
        beTriggerShortPrice = double.NaN;
        beTriggerActive = false;
        beFlattenTriggered = false;
        tpWasHit = false;
        hasReturnedOnce = false;
        beStopMoveRequested = false;
        trailCancelPending  = false;
        pendingTrailStopPrice = double.NaN;
        trailingTarget      = double.NaN;
        rangeTooWide = false;
        rangeTooWideLogged = false;
        cachedPrimaryBarSeconds = 0;

        if (isStrategyAnalyzer)
            lastExitBarAnalyzer = -1;
    }

    private void PlaceEntryIfTriggered()
    {
        if (rangeTooWide)
            return;

        //SetProfitTarget(CalculationMode.Ticks, 0);
        //SetStopLoss(CalculationMode.Ticks, 0);

        int driverCurrentBar = CurrentBars[driverBarsIndex];

        bool isLong = DriverClose(1) > todayLongLimit;
        double rawLimitPrice = isLong ? todayLongLimit : todayShortLimit;
        double limitPrice = rawLimitPrice; // Start with default
        double marketEntryPrice = double.NaN;

        double takeProfit = isLong ? todayLongProfit : todayShortProfit;
        double stopLoss   = isLong ? todayLongStoploss : todayShortStoploss;
            
        string signalName = isLong ? "Long" : "Short";
        currentSignalName = signalName;

        // ⛔️ Skip if price has re-entered the range — unless we're forcing it
        if (!isStrategyAnalyzer && ((!isLong && DriverClose(0) > todayShortLimit) || (isLong && DriverClose(0) < todayLongLimit)))
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

        //SetProfitTarget(signalName, CalculationMode.Price, takeProfit);
        //SetStopLoss(signalName, CalculationMode.Price, stopLoss, false);
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

            if (MarketEntry)
            {
                // Enter at market on the bar that closes above the entry line
                marketEntryPrice = GetCurrentAsk();
                if (marketEntryPrice <= 0 || double.IsNaN(marketEntryPrice))
                    marketEntryPrice = DriverClose(0);

                // Skip if price already beyond TP
                if (marketEntryPrice >= takeProfit)
                {
                    if (DebugMode)
                        DebugPrint($"⛔ Market entry skipped: price {marketEntryPrice:F2} already beyond TP {takeProfit:F2}.");
                    return;
                }

                // RR filter for market entries
                if (MinMarketRRPercent > 0)
            {
                double risk = marketEntryPrice - stopLoss;
                double reward = takeProfit - marketEntryPrice;
                double rr = (risk != 0) ? (reward / risk) : double.NaN;
                double minRR = MinMarketRRPercent / 100.0;

                if (double.IsNaN(rr) || rr < minRR)
                {
                    if (DebugMode)
                        DebugPrint($"⛔ Market entry blocked: RR {rr:F2} < min {minRR:F2}. Reward={reward:F2}, Risk={risk:F2}");
                    return;
                }
            }

            EnterLong(0, NumberOfContracts, signalName);
            pendingEntryPrice = marketEntryPrice;
            entryOrderBar = driverCurrentBar;
            entryOrderBarPrimary = CurrentBars.Length > 0 ? CurrentBars[0] : -1;
            entryOrderTime = DriverTime(0);
        }
        else
        {
            EnterLongLimit(0, true, NumberOfContracts, limitPrice, signalName);
            pendingEntryPrice = limitPrice;
            entryOrderBar = driverCurrentBar;
            entryOrderBarPrimary = CurrentBars.Length > 0 ? CurrentBars[0] : -1;
            entryOrderTime = DriverTime(0);
        }

            SendWebhook("buy", MarketEntry ? marketEntryPrice : limitPrice, takeProfit, stopLoss);
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

            if (MarketEntry)
            {
                marketEntryPrice = GetCurrentBid();
                if (marketEntryPrice <= 0 || double.IsNaN(marketEntryPrice))
                    marketEntryPrice = DriverClose(0);

                // Skip if price already beyond TP
                if (marketEntryPrice <= takeProfit)
                {
                    if (DebugMode)
                        DebugPrint($"⛔ Market entry skipped: price {marketEntryPrice:F2} already beyond TP {takeProfit:F2}.");
                    return;
                }

                // RR filter for market entries
                if (MinMarketRRPercent > 0)
                {
                    double risk = stopLoss - marketEntryPrice;
                    double reward = marketEntryPrice - takeProfit;
                    double rr = (risk != 0) ? (reward / risk) : double.NaN;
                    double minRR = MinMarketRRPercent / 100.0;

                    if (double.IsNaN(rr) || rr < minRR)
                    {
                        if (DebugMode)
                            DebugPrint($"⛔ Market entry blocked: RR {rr:F2} < min {minRR:F2}. Reward={reward:F2}, Risk={risk:F2}");
                        return;
                    }
                }

                EnterShort(0, NumberOfContracts, signalName);
                pendingEntryPrice = marketEntryPrice;
                entryOrderBar = driverCurrentBar;
                entryOrderBarPrimary = CurrentBars.Length > 0 ? CurrentBars[0] : -1;
                entryOrderTime = DriverTime(0);
            }
            else
            {
                EnterShortLimit(0, true, NumberOfContracts, limitPrice, signalName);
                pendingEntryPrice = limitPrice;
                entryOrderBar = driverCurrentBar;
                entryOrderBarPrimary = CurrentBars.Length > 0 ? CurrentBars[0] : -1;
                entryOrderTime = DriverTime(0);
            }

            SendWebhook("sell", MarketEntry ? marketEntryPrice : limitPrice, takeProfit, stopLoss);
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
            lastExitBarAnalyzer = DriverCurrentBar;
        var act = Position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
        double dummyExitP = Position.MarketPosition == MarketPosition.Long ? GetCurrentBid() : GetCurrentAsk();
        TryExitAll(dummyExitP, "SessionEnd");
    }

    private void SubmitStopLoss(double stopPrice)
    {
        if (Position.MarketPosition == MarketPosition.Flat)
            return;

        int qty = Position.Quantity;
        if (qty <= 0)
            return;

        double roundedStop = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

        if (Position.MarketPosition == MarketPosition.Long)
            hardStopOrder = ExitLongStopMarket(0, true, qty, roundedStop, "StopLoss", currentSignalName);
        else
            hardStopOrder = ExitShortStopMarket(0, true, qty, roundedStop, "StopLoss", currentSignalName);

        trailingTarget = roundedStop;
        DebugPrint($"[TRAIL] Stop submitted at {roundedStop:F2} (qty={qty})");
    }

    private void ReplaceStopOrder(double newSL)
    {
        if (double.IsNaN(newSL))
            return;

        double roundedSL = Instrument.MasterInstrument.RoundToTickSize(newSL);

        if (hardStopOrder == null ||
            hardStopOrder.OrderState == OrderState.Cancelled ||
            hardStopOrder.OrderState == OrderState.Filled ||
            hardStopOrder.OrderState == OrderState.Rejected)
        {
            SubmitStopLoss(roundedSL);
            beStopMoveRequested = true;
            return;
        }

        if (hardStopOrder.OrderState == OrderState.Working ||
            hardStopOrder.OrderState == OrderState.Accepted)
        {
            pendingTrailStopPrice = roundedSL;
            trailCancelPending    = true;
            beStopMoveRequested   = true;

            DebugPrint($"[TRAIL] Requesting cancel of old SL at {hardStopOrder.StopPrice:F2} to move to {roundedSL:F2}");
            CancelOrder(hardStopOrder);
        }
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
        entryOrderBar = -1;
        entryOrderBarPrimary = -1;
        entryOrderTime = DateTime.MinValue;
        pendingEntryPrice = double.NaN;
        if (Position.MarketPosition == MarketPosition.Flat)
        {
            entryBar = -1;
            entryBarPrimary = -1;
        }
        if (hardStopOrder != null)
        {
            CancelOrder(hardStopOrder);
            hardStopOrder = null;
        }
        trailCancelPending    = false;
        pendingTrailStopPrice = double.NaN;
        trailingTarget        = double.NaN;
        beStopMoveRequested   = false;
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
        beStopMoveRequested = false;
        trailCancelPending  = false;
        pendingTrailStopPrice = double.NaN;
        trailingTarget      = double.NaN;
    }

    private bool IsInSession()
    {
        if (!DriverSeriesReady())
            return false;

        TimeSpan now = DriverTime(0).TimeOfDay;
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
        double priceToCheck;

        if (RequireCloseBelowReturn)
        {
            // Close-based mode: bar close confirmation
            priceToCheck = DriverClose(1);
        }
        else
        {
            double bid = GetCurrentBid();
            double ask = GetCurrentAsk();

            // ✅ Defensive filter for live mode
            bool bidAskValid = bid > 0 && ask > 0 && Math.Abs(ask - bid) < (5 * TickSize);
            bool feedStable = DateTime.Now - lastEntryTime > TimeSpan.FromSeconds(1);

            if (!bidAskValid || !feedStable)
            {
                // Fallback to last trade price intrabar instead of bad bid/ask
                priceToCheck = GetCurrentAsk() == 0 ? GetCurrentBid() : DriverClose(0);
            }
            else
            {
                // Live + playback consistent midprice
                priceToCheck = (bid + ask) / 2.0;
            }
        }

        bool hasReturned = false;

        // ✅ Add tolerance to handle small discrepancies between tick/backtest prices
        double tolerance = 2 * TickSize; // adjust to 1 tick if you want stricter matching

        if (lastTradeWasLong)
            hasReturned = priceToCheck <= todayLongLimit - tolerance;
        else
            hasReturned = priceToCheck >= todayShortLimit + tolerance;

        if (hasReturned)
            DebugPrint($"[ResetCheck] Mode={(RequireCloseBelowReturn ? "Close" : "Touch")}, " +
                    $"lastTradeWasLong={lastTradeWasLong}, " +
                    $"PriceChecked={priceToCheck:F2}, " +
                    $"ReturnLimit={(lastTradeWasLong ? todayLongLimit : todayShortLimit):F2}, " +
                    $"Result={hasReturned}");

        return hasReturned;
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
            EntryPercent = 14.0;
            TakeProfitPercent = 32.4;
            HardStopLossPercent = 47.7;
            SLBETrigger = 0;
            MaxBarsInTrade = 90;
            break;

        case StrategyPreset.NQ_MNQ_2:
            EntryPercent = 11.25;
            TakeProfitPercent = 34;
            HardStopLossPercent = 50;
            SLBETrigger = 0;
            MaxBarsInTrade = 90;
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
        if (wickLinesDrawn || DriverTime(0).TimeOfDay < endTime || CurrentBars[driverBarsIndex] < GetMinBarsForRange())
            return;

        DateTime sessionDate = DriverTime(0).Date;
        DateTime windowStart = sessionDate + startTime;
        DateTime windowEnd = sessionDate + endTime;
        DateTime drawTo = sessionDate + SessionEnd;
        if (drawTo <= windowEnd)
            drawTo = windowEnd.AddMinutes(1);

        double rangeHigh = double.MinValue;
        double rangeLow = double.MaxValue;
        int barsCounted = 0;

        for (int barsAgo = 0; barsAgo <= CurrentBars[driverBarsIndex]; barsAgo++)
        {
            DateTime barTime = DriverTime(barsAgo);
            if (barTime.Date != sessionDate)
                break;
            if (barTime < windowStart)
                break;
            if (barTime >= windowStart && barTime < windowEnd)
            {
                rangeHigh = Math.Max(rangeHigh, DriverHigh(barsAgo));
                rangeLow = Math.Min(rangeLow, DriverLow(barsAgo));
                barsCounted++;
            }
        }

        if (barsCounted == 0)
            return;

        sessionHigh = rangeHigh;
        sessionLow = rangeLow;

        var tgH = $"{tagPrefix}_High_{sessionDate:yyyyMMdd}";
        var tgbH = $"{tagPrefix}_bHigh_{sessionDate:yyyyMMdd}";
        var tgmH = $"{tagPrefix}_mHLoss_{sessionDate:yyyyMMdd}";
        var tgmL = $"{tagPrefix}_mLLoss_{sessionDate:yyyyMMdd}";
        var tgL = $"{tagPrefix}_Low_{sessionDate:yyyyMMdd}";
        var tgbL = $"{tagPrefix}_bLow_{sessionDate:yyyyMMdd}";
        var tgPH1 = $"{tagPrefix}_Profit_High_1{sessionDate:yyyyMMdd}";
        var tgPL1 = $"{tagPrefix}_Profit_Low_1{sessionDate:yyyyMMdd}";
        var tgReturnHigh = $"{tagPrefix}_Return_High_1{sessionDate:yyyyMMdd}";
        var tgReturnLow = $"{tagPrefix}_Return_Low_1{sessionDate:yyyyMMdd}";
        var g = new SolidColorBrush(Color.FromArgb(70, 50, 205, 50));
        var y = new SolidColorBrush(Color.FromArgb(70, 255, 255, 0));
        var r = new SolidColorBrush(Color.FromArgb(70, 255, 0, 0));
        var o = new SolidColorBrush(Color.FromArgb(70, 255, 140, 0));
        var gr = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        var lg = new SolidColorBrush(Color.FromArgb(70, 50, 205, 50));

        var lineBrush = (lineColor ?? RangeBoxBrush).Clone();
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

        DateTime drawFrom = windowEnd;

        Draw.Line(this, tgH + "entry", false, drawFrom, todayLongLimit, drawTo, todayLongLimit, y, style, width).ZOrder = -1;
        Draw.Line(this, tgH, false, drawFrom, sessionHigh, drawTo, sessionHigh, lineBrush, style, width).ZOrder = -1;
        Draw.Line(this, tgmH + "maxLoss", false, drawFrom, todayLongStoploss, drawTo, todayLongStoploss, r, DashStyleHelper.Solid, width).ZOrder = -1;
        Draw.Line(this, tgmL + "maxLoss", false, drawFrom, todayShortStoploss, drawTo, todayShortStoploss, r, DashStyleHelper.Solid, width).ZOrder = -1;
        Draw.Line(this, tgL, false, drawFrom, sessionLow, drawTo, sessionLow, lineBrush, style, width).ZOrder = -1;
        Draw.Line(this, tgL + "entry", false, drawFrom, todayShortLimit, drawTo, todayShortLimit, y, style, width).ZOrder = -1;

        // Draw a filled rectangle between the session high and low
        string rectTag = $"{tagPrefix}_RangeBox_{sessionDate:yyyyMMdd}";
        Draw.Rectangle(this, rectTag,
            false,                   // AutoScale = false
            drawFrom,                // Start time
            sessionHigh,             // Upper Y
            drawTo,                  // End time
            sessionLow,              // Lower Y
            Brushes.Transparent,     // Border brush
            RangeBoxBrush,           // Fill brush
            10                       // Opacity (0-255)
        ).ZOrder = -1;               // ✅ Place behind the price bars

        // Take profit lines (PT1)
        double ptHigh = todayLongProfit;
        double ptLow = todayShortProfit;
        Draw.Line(this, $"{tagPrefix}_Profit_High_1{sessionDate:yyyyMMdd}", false, drawFrom, ptHigh, drawTo, ptHigh, lg,
                  style, width).ZOrder = -1;
        Draw.Line(this, $"{tagPrefix}_Profit_Low_1{sessionDate:yyyyMMdd}", false, drawFrom, ptLow, drawTo, ptLow, lg, style,
                  width).ZOrder = -1;

        // --- BE Trigger as % of full range (same scale as Entry% / TP%) ---
        double beOffset = rng * SLBETrigger / 100.0;

        beTriggerLongPrice  = Instrument.MasterInstrument.RoundToTickSize(roundedHigh + beOffset);
        beTriggerShortPrice = Instrument.MasterInstrument.RoundToTickSize(roundedLow  - beOffset);

        // === Break-Even Flatten Trigger Lines ===
        if (SLBETrigger > 0)
        {
            var tpBrush = new SolidColorBrush(Color.FromArgb(50, 50, 205, 50)); // similar to your TP brush
            Draw.Line(this, $"{tagPrefix}_BETrigger_Long_{sessionDate:yyyyMMdd}",
                false, drawFrom, beTriggerLongPrice, drawTo, beTriggerLongPrice, tpBrush, DashStyleHelper.Dot, width).ZOrder = -1;

            Draw.Line(this, $"{tagPrefix}_BETrigger_Short_{sessionDate:yyyyMMdd}",
                false, drawFrom, beTriggerShortPrice, drawTo, beTriggerShortPrice, tpBrush, DashStyleHelper.Dot, width).ZOrder = -1;
        }

        wickLinesDrawn = true;

        // Set first allowed evaluation time dynamically based on settings
        nextEvaluationTime = ComputeFirstEvalTime(sessionDate);

        Draw.VerticalLine(this, $"NoTradesAfter_{sessionDate:yyyyMMdd}", sessionDate + NoTradesAfter, r,
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

    private int GetRangeDurationSeconds()
    {
        int seconds = RangeDurationSeconds > 0 ? RangeDurationSeconds : BiasDuration * 60;
        return Math.Max(1, seconds);
    }

    private int GetEvaluationIntervalSeconds()
    {
        int seconds = EvaluationIntervalSeconds > 0 ? EvaluationIntervalSeconds : 300;
        return Math.Max(1, seconds);
    }

    private int GetPrimaryBarSeconds()
    {
        if (cachedPrimaryBarSeconds > 0)
            return cachedPrimaryBarSeconds;

        int seconds;
        var driverPeriod = BarsArray.Length > driverBarsIndex ? BarsArray[driverBarsIndex].BarsPeriod : BarsPeriod;
        switch (driverPeriod.BarsPeriodType)
        {
        case BarsPeriodType.Second:
            seconds = driverPeriod.Value;
            break;
        case BarsPeriodType.Minute:
            seconds = driverPeriod.Value * 60;
            break;
        default:
            seconds = 60;
            break;
        }

        cachedPrimaryBarSeconds = Math.Max(1, seconds);
        return cachedPrimaryBarSeconds;
    }

    private int GetMinBarsForRange() =>
        Math.Max(1, (int)Math.Ceiling((double)GetRangeDurationSeconds() / GetPrimaryBarSeconds()));

    private DateTime ComputeFirstEvalTime(DateTime sessionDate)
    {
        TimeSpan biasStart = SessionStart.Add(TimeSpan.FromMinutes(1));
        TimeSpan biasEnd   = biasStart.Add(TimeSpan.FromSeconds(GetRangeDurationSeconds()));
        return sessionDate.Add(biasEnd).AddSeconds(GetEvaluationIntervalSeconds());
    }
    
    private void WriteHeartbeat()
    {
        try
        {
            string name = this.Name ?? GetType().Name;
            string line = $"{name},{DateTime.Now:O}";
            List<string> lines = new List<string>();

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
            bool success = false;
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
        if (UseBreakoutRearmDelay && breakoutRearmPending && DriverTime(0) < breakoutRearmTime)
            return false;

        // Otherwise OK (session checks already handled elsewhere)
        return true;
    }

    private bool VixReady(int barsAgo = 0)
    {
        return CurrentBars.Length > vixIndex && CurrentBars[vixIndex] > barsAgo;
    }

    private double GetVix(int barsAgo = 0)
    {
        return Closes[vixIndex][barsAgo];
    }

    #endregion
    }
}
