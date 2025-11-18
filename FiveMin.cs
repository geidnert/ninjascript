using System;
using System.Collections.Generic;
using System.Reflection;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;
using System.ComponentModel.DataAnnotations;
using System.Windows;

namespace NinjaTrader.NinjaScript.Strategies
{
public class FiveMin : Strategy
{
    public FiveMin()
    {
        // Old vendor licensing
        //VendorLicense("AutoEdge", "FiveMin", "www.autoedge.io", "support@autoedge.io", null);
    }

#region Settings
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
    [Display(
        Name = "Max Account Balance",
        Description =
            "When account reach this amount, ongoing orders and positions will close, no more trades will be taken",
        Order = 3, GroupName = "A. Config")]
    public double MaxAccountBalance {
        get; set;
    }

    [NinjaScriptProperty]
    [Display(Name = "Trailing", Description = "Enable fixed-step trailing stop", Order = 1, GroupName = "A. Config")]
    public bool Trailing { get; set; }

    [NinjaScriptProperty]
    [Display(Name = "Range Duration", Order = 1, GroupName = "B. Entry Conditions")]
    public int BiasDuration { get; set; }

    [NinjaScriptProperty]
    [Display(
        Name = "Enter On FVG Breakout",
        Description = "If true, enter as soon as an FVG is detected on breakout outside the range, without waiting for retrace + engulfing.",
        Order = 2,
        GroupName = "B. Entry Conditions")]
    public bool EnterOnFvgBreakout { get; set; }

    [NinjaScriptProperty]
    [Display(
        Name = "Risk Reward Ratio",
        Description = "Example: 3 = risk 1 to make 3",
        Order = 3,
        GroupName = "B. Entry Conditions")]
    public double RiskRewardRatio { get; set; }

    [NinjaScriptProperty]
    [Display(
        Name = "Max TPs Per Day",
        Description = "How many take-profit wins are allowed per day before stopping trading.",
        Order = 4,
        GroupName = "A. Config")]
    public int MaxTPsPerDay { get; set; }

    [NinjaScriptProperty]
    [Display(
        Name = "Max Losses Per Day",
        Description = "How many losing trades are allowed per day before stopping all trading.",
        Order = 5,
        GroupName = "A. Config")]
    public int MaxLossesPerDay { get; set; }

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

    [NinjaScriptProperty]
    [Display(Name = "Info Panel", Order = 1, GroupName = "D. Dev")]
    public bool DebugMode {
        get; set;
    }

#endregion

#region Variables
    private bool isRealTime = false;
    private bool orderPlaced = false;
    private double sessionHigh, sessionLow;
    private string lastDate = string.Empty;
    private Order entryOrder;
    private Order hardStopOrder;
    private List<Order> profitOrders = new List<Order>();
    private string displayText = "Waiting...";
    private bool isStrategyAnalyzer = false;
    private TimeSpan sessionStart = new TimeSpan(9, 30, 0);
    private TimeSpan sessionEnd = new TimeSpan(16, 00, 0);
    private TimeSpan noTradesAfter = new TimeSpan(14, 59, 0);
    private bool maxAccountLimitHit = false;
    private DateTime positionEntryTime = DateTime.MinValue;
    private bool lastTradeWasLong = false;
    private DateTime lastEntryTime = DateTime.MinValue;
    private readonly TimeSpan minTimeBetweenEntries = TimeSpan.FromSeconds(1);
    private int lastExitBarAnalyzer = -1;
    private string currentSignalName;
    private DateTime lastProtectionTime = DateTime.MinValue;
    private DateTime nextEvaluationTime = DateTime.MinValue;
    private bool isInBiasWindow = false;
    private bool hasCapturedRange = false;
    private int consecutiveWins = 0;
	private bool rule2Passed = false;
	private bool rule3Passed = false;
	private double fvgHigh = Double.NaN;
	private double fvgLow = Double.NaN;
	// Track FVG direction
	private enum FVGType { None, Bullish, Bearish }
	private FVGType lastFvgType = FVGType.None;
    private double fvgFirstCandleHigh = double.NaN;
    private double fvgFirstCandleLow  = double.NaN;
	// Track sequence progress
	private int rule2Bar = -1;    // bar index when breakout + FVG detected
	private int retraceBar = -1;  // bar index of most recent retrace into FVG

    private double trailingTarget = double.NaN;   // price level to monitor for trailing activation
    private int tpCountToday = 0;
    private int lossCountToday = 0;
    private bool dailyLimitReached = false;

    private bool trailingActivated = false;
    private double entryTakeProfitPrice = double.NaN;

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
        // else if (State == State.DataLoaded)
        //     ApplyPreset(PresetSetting);
    }

    private void SetDefaults()
    {
        Name = "FiveMin";
        Calculate = Calculate.OnEachTick;
        IsOverlay = true;
        IsInstantiatedOnEachOptimizationIteration = false;
        IsUnmanaged = false;
        NumberOfContracts = 1;
        BiasDuration = 5;
        MaxAccountBalance = 0; 
        DebugMode = true;
        EnterOnFvgBreakout = true;
        RiskRewardRatio = 2.0;
        MaxTPsPerDay = 1;
        MaxLossesPerDay = 2;
    }
#endregion

#region OnBarUpdate
    protected override void OnBarUpdate()
    {
        ResetDailyStateIfNeeded();

        // Main series (BarsInProgress == 0)
        if (ShouldSkipBarUpdate())
            return;

        if (DebugMode)
            UpdateInfoText(GetDebugText());

        if (ShouldAccountBalanceExit())
            return;

        // --- Bias window control ---
        TimeSpan now = Times[0][0].TimeOfDay;

        // Align biasStart to the first bar that begins *after* SessionStart
        int barSize = BarsPeriod.Value;                 // e.g. 5 for 5-minute chart
        int startMinute = ((SessionStart.Minutes / barSize) + 1) * barSize;
        TimeSpan biasStart = new TimeSpan(SessionStart.Hours, startMinute, 0);
        TimeSpan biasEnd = biasStart.Add(TimeSpan.FromMinutes(BiasDuration));

        // Enter bias window
        if (!isInBiasWindow && now >= biasStart && now < biasEnd)
        {
            sessionHigh = High[0];
            sessionLow  = Low[0];
            isInBiasWindow = true;
            hasCapturedRange = false;
            DebugPrint($"[Bias] Started bias window {biasStart}–{biasEnd}");
        }
        else if (isInBiasWindow && now < biasEnd)
        {
            // keep updating range
            sessionHigh = Math.Max(sessionHigh, High[0]);
            sessionLow  = Math.Min(sessionLow, Low[0]);
        }
        else if (isInBiasWindow && now >= biasEnd && !hasCapturedRange)
        {
            DrawSessionWickRangePersistent(biasStart, biasEnd, "WickRange", Brushes.DodgerBlue,
                                        DashStyleHelper.Solid, 2);
            hasCapturedRange = true;
            isInBiasWindow = false;
            DebugPrint($"[Bias] Completed range {sessionLow:F2}–{sessionHigh:F2}");
        }

        ExitIfSessionEnded();
        CancelEntryIfAfterNoTrades();
        CancelOrphanOrdersIfSessionOver();

        // ---------------------------------------------
        // STEP 1: Detect FIRST TP touch to activate trailing
        // ---------------------------------------------
        if (Trailing && !trailingActivated && Position.MarketPosition != MarketPosition.Flat)
        {
            // LONG TP touch
            if (Position.MarketPosition == MarketPosition.Long && High[0] >= entryTakeProfitPrice)
            {
                trailingActivated = true;
                DebugPrint("[TRAIL] TP touched → Trailing ACTIVATED.");
            }

            // SHORT TP touch
            if (Position.MarketPosition == MarketPosition.Short && Low[0] <= entryTakeProfitPrice)
            {
                trailingActivated = true;
                DebugPrint("[TRAIL] TP touched → Trailing ACTIVATED.");
            }
        }

        // 🔁 Breakout reset should always be checked per tick or every 5m based on setting
        bool noOpenOrders = !HasOpenOrders();
        bool isFlat = Position.MarketPosition == MarketPosition.Flat;

		// --- Rule 2 + Rule 4 only at first tick of bar (open/close checks)
		if (IsFirstTickOfBar)
		{
            // -------------------------------
            // Continuous Wick Trailing System
            // -------------------------------
            if (Trailing && trailingActivated && Position.MarketPosition != MarketPosition.Flat && IsFirstTickOfBar)
            {
                // LONG TRAILING
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    double newSL = Instrument.MasterInstrument.RoundToTickSize(Low[1]);

                    // SL can ONLY move UP
                    if (double.IsNaN(trailingTarget) || newSL > trailingTarget)
                    {
                        CancelOrder(hardStopOrder);
                        hardStopOrder = ExitLongStopMarket(0, true, Position.Quantity, newSL, "TrailSL", currentSignalName);

                        trailingTarget = newSL;

                        DebugPrint($"[TRAIL] Long trailing SL moved to Low[1] = {newSL:F2}");
                    }
                }

                // SHORT TRAILING
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    double newSL = Instrument.MasterInstrument.RoundToTickSize(High[1]);

                    // SL can ONLY move DOWN
                    if (double.IsNaN(trailingTarget) || newSL < trailingTarget)
                    {
                        CancelOrder(hardStopOrder);
                        hardStopOrder = ExitShortStopMarket(0, true, Position.Quantity, newSL, "TrailSL", currentSignalName);

                        trailingTarget = newSL;

                        DebugPrint($"[TRAIL] Short trailing SL moved to High[1] = {newSL:F2}");
                    }
                }
            }

			TryEntrySignal(false);  // breakout & engulfing
			//TryClosePosition();
			nextEvaluationTime = nextEvaluationTime.AddMinutes(5);
		}

		// --- Rule 3 retrace runs on *every* tick
		TryEntrySignal(true);

        // --- Draw trailing line ---
        if (Trailing && Position.MarketPosition != MarketPosition.Flat && !double.IsNaN(trailingTarget))
        {
            Draw.HorizontalLine(this, "trailingLine", trailingTarget, Brushes.Goldenrod);
        }
    }

	// Use [1],[2],[3] so the 3-bar FVG is confirmed on closed bars
	private bool TryFindFVG_Closed(out double high, out double low)
	{
		high = double.NaN; low = double.NaN;

		if (CurrentBar < 3) { DebugPrint("[FVG] Not enough bars."); return false; }

		// Debug shows closed bars [3],[2],[1]
		DebugPrint($"[FVG] (closed) Bars: " +
			$"[3] O={Open[3]:F2}, H={High[3]:F2}, L={Low[3]:F2}, C={Close[3]:F2} | " +
			$"[2] O={Open[2]:F2}, H={High[2]:F2}, L={Low[2]:F2}, C={Close[2]:F2} | " +
			$"[1] O={Open[1]:F2}, H={High[1]:F2}, L={Low[1]:F2}, C={Close[1]:F2}");

		// 3-bar gap using CLOSED bars
		bool bullishFVG = Low[1] > High[3];
		bool bearishFVG = High[1] < Low[3];

		// At least one full body inside bias range (also CLOSED bars)
		bool body3Inside = Open[3] <= sessionHigh && Close[3] <= sessionHigh && Open[3] >= sessionLow && Close[3] >= sessionLow;
		bool body2Inside = Open[2] <= sessionHigh && Close[2] <= sessionHigh && Open[2] >= sessionLow && Close[2] >= sessionLow;
		bool body1Inside = Open[1] <= sessionHigh && Close[1] <= sessionHigh && Open[1] >= sessionLow && Close[1] >= sessionLow;
		bool insideRange = body3Inside || body2Inside || body1Inside;

		DebugPrint($"[FVG] (closed) InsideRange → [3]={body3Inside}, [2]={body2Inside}, [1]={body1Inside}");

		if (bullishFVG && insideRange)
		{
			low  = High[3];
			high = Low[1];
			lastFvgType = FVGType.Bullish;
			fvgFirstCandleHigh = High[3];
			fvgFirstCandleLow  = Low[3];
			DebugPrint($"[FVG] ✅ Bullish (closed). Zone=[{low:F2},{high:F2}]");
			return true;
		}
		if (bearishFVG && insideRange)
		{
			low  = High[1];
			high = Low[3];
			lastFvgType = FVGType.Bearish;
			fvgFirstCandleHigh = High[3];
			fvgFirstCandleLow  = Low[3];
			DebugPrint($"[FVG] ✅ Bearish (closed). Zone=[{low:F2},{high:F2}]");
			return true;
		}

		lastFvgType = FVGType.None;
		DebugPrint("[FVG] ❌ No valid closed-bar FVG");
		return false;
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

    private string GetDebugText() => "FiveMin v" + GetAddOnVersion();

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
            netEquity = realizedPnL + unrealizedPnL;
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

    private bool SkipManagementForSessionState() => IsInSession() && !isRealTime && !isStrategyAnalyzer;

    private void ExitIfSessionEnded()
    {
        if ((!IsInSession()) && (Position.MarketPosition != MarketPosition.Flat))
            ExitAtSessionEnd();
    }

    private void CancelEntryIfAfterNoTrades()
    {
        if (!IsFirstTickOfBar) return;

        if (Times[0][0].TimeOfDay >= NoTradesAfter && Times[0][0].TimeOfDay < SessionEnd &&
            Position.MarketPosition == MarketPosition.Flat)
        {
            foreach (Order o in Account.Orders)
            {
                if (o != null && (o.Name == "Long" || o.Name == "Short") && o.OrderState == OrderState.Working &&
                    o.Instrument.FullName == Instrument.FullName)
                {
                    DebugPrint($"⏰ Canceling managed entry order: {o.Name}");
                    CancelOrder(o);
                }
            }
        }
    }

    private void CancelOrphanOrdersIfSessionOver()
    {
        if (!IsInSession() && HasOpenOrders())
            CancelAllOrders();
    }

    private void InvalidateSetup(string reason)
    {
        if (rule2Passed || rule3Passed)
            DebugPrint($"[Setup] ❌ Invalidated: {reason}");

        // Prevent Rule 2 from firing again on direct entry
        if (!EnterOnFvgBreakout)
            rule2Passed = false;

        rule3Passed = false;

        lastFvgType = FVGType.None;
        fvgHigh = fvgLow = Double.NaN;
        rule2Bar = -1;
        retraceBar = -1;
    }

	private bool BarOverlapsFVG(int barsAgo = 0)
	{
		if (double.IsNaN(fvgHigh) || double.IsNaN(fvgLow))
			return false;

		double hi = High[barsAgo];
		double lo = Low[barsAgo];
		return lo <= fvgHigh && hi >= fvgLow;
	}

    private void TryEntrySignal(bool isIntrabar)
    {
        if (dailyLimitReached)
            return;
        if (Times[0][0].TimeOfDay >= NoTradesAfter)
            return;

        HandleOppositeSignalReset();

        if (!isIntrabar)
        {
            DetectBreakoutAndFVG();  // Rule 2

            // If we are in "wait for retrace + engulf" mode (old behavior)
            if (!EnterOnFvgBreakout)
            {
                ConfirmEngulfing();  // Rule 4
            }
        }
        else
        {
            // Intrabar retrace (Rule 3) ONLY used in old behavior
            if (!EnterOnFvgBreakout)
            {
                DetectRetrace();     // Rule 3
            }
        }
    }

    private void HandleOppositeSignalReset()
    {
        if (!(rule2Passed || rule3Passed))
            return;

        bool oppositeSignalDetected =
            (lastFvgType == FVGType.Bearish && Close[0] > sessionHigh) ||
            (lastFvgType == FVGType.Bullish && Close[0] < sessionLow);

        if (oppositeSignalDetected)
        {
            DebugPrint($"[Reset] Opposite directional signal detected. "
                    + $"Old={lastFvgType}, Close={Close[0]:F2}, "
                    + $"SessionHigh={sessionHigh:F2}, SessionLow={sessionLow:F2}");
            InvalidateSetup("Opposite direction criteria started");
        }
    }

    private void DetectRetrace()
    {
        if (!rule2Passed || rule3Passed || CurrentBar <= rule2Bar)
            return;

        double priceToCheck = (lastFvgType == FVGType.Bullish) ? GetCurrentBid() : GetCurrentAsk();

        bool intoBull = lastFvgType == FVGType.Bullish && priceToCheck <= fvgHigh && priceToCheck >= fvgLow;
        bool intoBear = lastFvgType == FVGType.Bearish && priceToCheck >= fvgLow && priceToCheck <= fvgHigh;

        if (!(intoBull || intoBear))
            return;

        retraceBar = CurrentBar;
        rule3Passed = true;

        DebugPrint($"✅ Rule 3 intrabar retrace @ {Times[0][0]} Price={priceToCheck:F2} "
                + $"FVG=({fvgLow:F2}-{fvgHigh:F2}), retraceBar={retraceBar}");

        DrawFVGZoneProgress();
    }

    private void ConfirmEngulfing()
    {
        if (!rule3Passed || retraceBar < 0 || CurrentBar <= retraceBar)
            return;

        int retraceBarsAgo = CurrentBar - retraceBar;
        double rOpen  = Open[retraceBarsAgo];
        double rClose = Close[retraceBarsAgo];
        double rHighBody = Math.Max(rOpen, rClose);
        double rLowBody  = Math.Min(rOpen, rClose);
        double engulfClose = Close[0];

        bool bullEngulf = (lastFvgType == FVGType.Bullish) && (engulfClose > rHighBody);
        bool bearEngulf = (lastFvgType == FVGType.Bearish) && (engulfClose < rLowBody);

        DebugPrint($"[Rule4] Engulf check → retraceBar={retraceBar}, "
                + $"body=({rLowBody:F2}-{rHighBody:F2}), "
                + $"EngulfClose={engulfClose:F2}, Bullish={bullEngulf}, Bearish={bearEngulf}");

        if (bullEngulf || bearEngulf)
        {
            DebugPrint("✅ Rule 4 passed → placing entry immediately.");
            PlaceEntryIfTriggered();
            InvalidateSetup("Completed");
        }
    }

    private void DetectBreakoutAndFVG()
    {
        if (rule2Passed)
            return;

        bool breakoutAbove = Close[1] > sessionHigh;
        bool breakoutBelow = Close[1] < sessionLow;

        if (!breakoutAbove && !breakoutBelow)
            return;

        DebugPrint($"[Rule2] Breakout on closed bar {(breakoutAbove ? "ABOVE" : "BELOW")}");

        if (TryFindFVG_Closed(out fvgHigh, out fvgLow))
        {
            if ((breakoutAbove && lastFvgType == FVGType.Bullish) ||
                (breakoutBelow && lastFvgType == FVGType.Bearish))
            {
                rule2Passed = true;
                rule2Bar = CurrentBar - 1;
                retraceBar = -1;
                rule3Passed = false;

                DebugPrint(
                    $"✅ Rule 2 passed (closed). FVG [{fvgLow:F2},{fvgHigh:F2}] Dir={lastFvgType}, rule2Bar={rule2Bar}");

                DrawFVGZone();

                // 🔹 NEW: direct entry mode
                if (EnterOnFvgBreakout)
                {
                    DebugPrint("[Rule2] EnterOnFvgBreakout = true → placing entry immediately on FVG breakout.");
                    PlaceEntryIfTriggered();
                    InvalidateSetup("Direct FVG breakout entry taken");
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
    }
#endregion

#region NinjaTrader Event Routing
    protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
                                          double averageFillPrice, OrderState orderState, DateTime time,
                                          ErrorCode error, string comment)
    {
        if (Trailing && order != null && order.Name == "StopLoss" &&
            (order.OrderState == OrderState.Accepted || order.OrderState == OrderState.Working))
        {
            hardStopOrder = order;                 // <<< REQUIRED
            trailingTarget = order.StopPrice;

            double entryPrice = entryOrder != null && entryOrder.AverageFillPrice > 0
                ? entryOrder.AverageFillPrice
                : Close[0];

            DebugPrint($"[TRAIL] Initial SL working. trailingTarget={trailingTarget:F2}");
        }


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
            lastEntryTime = Times[0][0];
            lastProtectionTime = DateTime.Now;
        }

        // Exit
        if (execution.Order.OrderAction == OrderAction.Sell || execution.Order.OrderAction == OrderAction.BuyToCover)
        {
            if (execution.Order.Name == "SoftSL")
            {
                DebugPrint($"✅ Soft stop exit executed at {price}");
            }
        }

        // Reset state
        if (Position.MarketPosition == MarketPosition.Flat)
        {
            if (isStrategyAnalyzer)
                lastExitBarAnalyzer = CurrentBar;

            DebugPrint(
                $"Execution update: flat after exit. Qty={execution.Quantity}, Price={price}, OrderId={orderId}");
            CleanupPosition();
        }

        if (Position.MarketPosition == MarketPosition.Flat)
        {
            double tradePnL = 0;

            if (execution.Order.OrderAction == OrderAction.Sell && lastTradeWasLong)
                tradePnL = (price - execution.Price) * execution.Quantity * Instrument.MasterInstrument.PointValue;
            else if (execution.Order.OrderAction == OrderAction.BuyToCover && !lastTradeWasLong)
                tradePnL = (execution.Price - price) * execution.Quantity * Instrument.MasterInstrument.PointValue;

            bool wasWin = tradePnL > 0;

            if (wasWin)
            {
                tpCountToday++;
                DebugPrint($"🎯 Take Profit hit. TP Today = {tpCountToday}/{MaxTPsPerDay}");

                if (tpCountToday >= MaxTPsPerDay)
                {
                    dailyLimitReached = true;
                    DebugPrint("⛔ Max TPs reached, no more trades today.");
                }
            }
            else
            {
                lossCountToday++;
                DebugPrint($"❌ Loss recorded. Losses Today = {lossCountToday}/{MaxLossesPerDay}");

                if (lossCountToday >= MaxLossesPerDay)
                {
                    dailyLimitReached = true;
                    DebugPrint("⛔ Max losses reached, no more trades today.");
                }
            }
        }
    }
#endregion

#region Core Helpers &Drawing
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
        sessionHigh = sessionLow = 0;
        positionEntryTime = DateTime.MinValue;
        lastTradeWasLong = false;
        lastProtectionTime = DateTime.MinValue;
        nextEvaluationTime = DateTime.MinValue;
        consecutiveWins = 0;
		rule2Passed = false;
		rule3Passed = false;
		fvgHigh = fvgLow = Double.NaN;
        tpCountToday = 0;
        lossCountToday = 0;
        dailyLimitReached = false;

        if (isStrategyAnalyzer)
            lastExitBarAnalyzer = -1;
    }

    private void PlaceEntryIfTriggered()
    {
        // SetProfitTarget(CalculationMode.Ticks, 0);
        // SetStopLoss(CalculationMode.Ticks, 0);

        bool isLong  = lastFvgType == FVGType.Bullish;
        bool isShort = lastFvgType == FVGType.Bearish;

        string signalName = isLong ? "Long" : "Short";
        currentSignalName = signalName;

        double entryPx = Close[0];  // using bar close at entry
        double stopPx  = double.NaN;
        double takePx  = double.NaN;

        if (isLong)
        {
            // SL = 1 tick below the *first candle low*
            stopPx = Instrument.MasterInstrument.RoundToTickSize(fvgFirstCandleLow - TickSize);
            double risk = entryPx - stopPx;

            takePx = Instrument.MasterInstrument.RoundToTickSize(
                entryPx + (RiskRewardRatio * risk)
            );
        }
        else if (isShort)
        {
            // SL = 1 tick above the *first candle high*
            stopPx = Instrument.MasterInstrument.RoundToTickSize(fvgFirstCandleHigh + TickSize);
            double risk = stopPx - entryPx;

            takePx = Instrument.MasterInstrument.RoundToTickSize(
                entryPx - (RiskRewardRatio * risk)
            );
        }

        entryTakeProfitPrice = takePx;   // <<< REQUIRED FOR TRAILING ACTIVATION

        printTradeDevider();

        if (RequireEntryConfirmation)
        {
            if (!ShowEntryConfirmation(signalName, entryPx, NumberOfContracts))
            {
                DebugPrint($"User declined {signalName} entry via confirmation dialog.");
                return;
            }
        }

        // SetProfitTarget(signalName, CalculationMode.Price, takePx);
        // SetStopLoss(signalName, CalculationMode.Price, stopPx, false);
        double entryStopPrice = stopPx;
        entryTakeProfitPrice = takePx;

        if (isLong)
        {
            EnterLong(NumberOfContracts, signalName);
            ExitLongStopMarket(0, true, NumberOfContracts, entryStopPrice, "StopLoss", signalName);
            if (!Trailing)
            {
                // Only place TP if trailing mode OFF
                ExitLongLimit(0, true, NumberOfContracts, entryTakeProfitPrice, "TakeProfit", signalName);
            }

        }
        else
        {
            EnterShort(NumberOfContracts, signalName);
            ExitShortStopMarket(0, true, NumberOfContracts, entryStopPrice, "StopLoss", signalName);
            if (!Trailing)
            {
                ExitShortLimit(0, true, NumberOfContracts, entryTakeProfitPrice, "TakeProfit", signalName);
            }
        }

        orderPlaced = true;
        lastTradeWasLong = isLong;

        DebugPrint($"📌 Market {signalName} entry={entryPx:F2}, SL={stopPx:F2}, TP={takePx:F2}, Risk={(Math.Abs(entryPx - stopPx)):F2}");
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
        CancelAllOrders();
    }

    private void CancelAllOrders()
    {
        DebugPrint("CancelAllOrders called. EntryOrder=" + (entryOrder?.Name ?? "null") +
                   ", HardStopOrder=" + (hardStopOrder?.Name ?? "null") + ", ProfitOrders=" + profitOrders.Count);

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
    }

    private void DrawFVGZone()
    {
        string tagBase = $"FVG_{lastFvgType}";
        var brush = new SolidColorBrush(Color.FromArgb(70, 50, 205, 50));
        int barsAgoStart = CurrentBar - rule2Bar;

        Draw.Line(this, tagBase + "_high", false, barsAgoStart, fvgHigh, -90, fvgHigh, brush, DashStyleHelper.Solid, 1);
        Draw.Line(this, tagBase + "_low", false, barsAgoStart, fvgLow, -90, fvgLow, brush, DashStyleHelper.Solid, 1);
    }

    private void DrawFVGZoneProgress()
    {
        string tagBase = $"FVG_{lastFvgType}";
        var brush = new SolidColorBrush(Color.FromArgb(70, 50, 205, 50));
        int barsAgoStart = CurrentBar - rule2Bar;
        int barsAgoEnd   = CurrentBar - retraceBar;

        Draw.Line(this, tagBase + "_high", false, barsAgoStart, fvgHigh, barsAgoEnd, fvgHigh, brush, DashStyleHelper.Solid, 1);
        Draw.Line(this, tagBase + "_low", false, barsAgoStart, fvgLow, barsAgoEnd, fvgLow, brush, DashStyleHelper.Solid, 1);
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
        trailingActivated = false;
        entryTakeProfitPrice = double.NaN;
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

    private double RoundOffset(double value)
    {
        return Math.Round(value / TickSize) * TickSize;
    }

    private void DrawSessionWickRangePersistent(TimeSpan startTime, TimeSpan endTime, string tagPrefix, Brush lineColor,
                                                DashStyleHelper style, int width)
    {
        if (Times[0][0].TimeOfDay < endTime || CurrentBar < BiasDuration)
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

        Draw.Line(this, tgH, false, s, sessionHigh, s - off, sessionHigh, gr, style, width);
        Draw.Line(this, tgL, false, s, sessionLow, s - off, sessionLow, gr, style, width);

        // Draw a filled rectangle between the session high and low
        string rectTag = $"{tagPrefix}_RangeBox_{Times[0][0]:yyyyMMdd}";
        Draw.Rectangle(this, rectTag,
            false,                   // AutoScale = false
            s,                       // Start bar index
            sessionHigh,             // Upper Y
            s - off,                 // End bar index
            sessionLow,              // Lower Y
            Brushes.Transparent,     // Border brush
            Brushes.DarkSlateGray,   // Fill brush
            10                       // Opacity (0-255)
        ).ZOrder = -1;               // ✅ Place behind the price bars

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

        Draw.TextFixed(owner: this, tag: "myStatusLabel", text: displayText, textPosition: TextPosition.BottomRight,
                       textBrush: Brushes.DarkGray, font: new SimpleFont("Segoe UI", 9), outlineBrush: null,
                       areaBrush: Brushes.Gray, areaOpacity: 20);
    }

    string GetAddOnVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        Version version = assembly.GetName().Version;
        return version.ToString();
    }

    private double GetStopLossPrice()
    {
        double stopPrice = double.NaN;
        foreach (Order o in Account.Orders)
        {
            if (o != null && o.OrderType == OrderType.StopMarket && o.Instrument.FullName == Instrument.FullName)
            {
                stopPrice = o.StopPrice;
                break;
            }
        }
        return stopPrice;
    }
#endregion
}
}