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
using System.Xml.Serialization;

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
        Name = "Show Inverted FVGs",
        Description = "If true, keep and color FVGs after they are hit/inverted.",
        Order = 6,
        GroupName = "B. Entry Conditions")]
    public bool ShowInvalidatedFvgs { get; set; }

    [XmlIgnore]
    [Display(
        Name = "FVG Fill Brush",
        Description = "Fill color for active FVG rectangles.",
        Order = 7,
        GroupName = "B. Entry Conditions")]
    public Brush FvgFillBrush { get; set; }

    public string FvgFillBrushSerializable
    {
        get { return Serialize.BrushToString(FvgFillBrush); }
        set { FvgFillBrush = Serialize.StringToBrush(value); }
    }

    [XmlIgnore]
    [Display(
        Name = "Invalidated FVG Fill Brush",
        Description = "Fill color for invalidated FVG rectangles.",
        Order = 8,
        GroupName = "B. Entry Conditions")]
    public Brush InvalidatedFvgFillBrush { get; set; }

    public string InvalidatedFvgFillBrushSerializable
    {
        get { return Serialize.BrushToString(InvalidatedFvgFillBrush); }
        set { InvalidatedFvgFillBrush = Serialize.StringToBrush(value); }
    }

    [NinjaScriptProperty]
    [Display(
        Name = "FVG Opacity",
        Description = "Opacity for active FVGs (0-255).",
        Order = 9,
        GroupName = "B. Entry Conditions")]
    public int FvgOpacity { get; set; }

    [NinjaScriptProperty]
    [Display(
        Name = "Inverted FVG Opacity",
        Description = "Opacity for invalidated FVGs (0-255).",
        Order = 10,
        GroupName = "B. Entry Conditions")]
    public int InvalidatedFvgOpacity { get; set; }

    [NinjaScriptProperty]
    [Display(
        Name = "Use Fixed Take Profit (Points)",
        Description = "If true, TP is a fixed number of points instead of Risk Reward Ratio.",
        Order = 4,
        GroupName = "B. Entry Conditions")]
    public bool UseFixedTakeProfitPoints { get; set; }

    [NinjaScriptProperty]
    [Display(
        Name = "Take Profit Points",
        Description = "Fixed TP distance in points (price units).",
        Order = 5,
        GroupName = "B. Entry Conditions")]
    public double TakeProfitPoints { get; set; }

    [NinjaScriptProperty]
    [Display(
        Name = "Use Fixed Stop Loss (Points)",
        Description = "If true, SL is a fixed number of points instead of FVG-based SL.",
        Order = 6,
        GroupName = "B. Entry Conditions")]
    public bool UseFixedStopLossPoints { get; set; }

    [NinjaScriptProperty]
    [Display(
        Name = "Stop Loss Points",
        Description = "Fixed SL distance in points (price units).",
        Order = 7,
        GroupName = "B. Entry Conditions")]
    public double StopLossPoints { get; set; }

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
    [Display(
        Name = "Tick-Based Range Flatten",
        Description = "If true, flatten any open position immediately when price touches the captured range.",
        Order = 6,
        GroupName = "A. Config")]
    public bool TickBasedRangeFlatten { get; set; }

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
    private double entryPrice = double.NaN;
    private bool invalidBarsPeriod = false;
    private readonly List<FvgBox> activeFvgs = new List<FvgBox>();
    private int fvgCounter = 0;
    // --- Trailing stop state machine ---
    private bool  trailCancelPending   = false;      // true while we wait for old SL to be fully cancelled
    private double pendingTrailStopPrice = double.NaN; // new SL price to submit after cancel
    private double activationWickLow  = double.NaN; // for longs
    private double activationWickHigh = double.NaN; // for shorts



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
        else if (State == State.DataLoaded)
        {
            invalidBarsPeriod =
                BarsPeriod.BarsPeriodType != BarsPeriodType.Minute ||
                BarsPeriod.Value != 5;

            if (invalidBarsPeriod)
            {
                DebugPrint("⚠ FiveMin requires a 5-minute chart. Strategy will skip trading until chart is 5-minute.");
            }

            if (FvgFillBrush != null && FvgFillBrush.CanFreeze)
                FvgFillBrush.Freeze();
            if (InvalidatedFvgFillBrush != null && InvalidatedFvgFillBrush.CanFreeze)
                InvalidatedFvgFillBrush.Freeze();
        }
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
        DebugMode = false;
        EnterOnFvgBreakout = true;
        RiskRewardRatio = 2.0;
        ShowInvalidatedFvgs = true;
        FvgFillBrush = Brushes.DodgerBlue;
        InvalidatedFvgFillBrush = Brushes.Gray;
        FvgOpacity = 70;
        InvalidatedFvgOpacity = 40;
        UseFixedTakeProfitPoints = false;
        TakeProfitPoints = 5.0;
        UseFixedStopLossPoints = false;
        StopLossPoints = 3.0;
        MaxTPsPerDay = 1;
        MaxLossesPerDay = 2;
        Trailing = true;
        TickBasedRangeFlatten = false;
    }
#endregion

#region OnBarUpdate
    protected override void OnBarUpdate()
    {
        ResetDailyStateIfNeeded();

        // Main series (BarsInProgress == 0)
        if (ShouldSkipBarUpdate())
            return;
        if (dailyLimitReached)
            return;

        if (DebugMode)
            UpdateInfoText(GetDebugText());

        if (ShouldAccountBalanceExit())
            return;

        // --- Bias window control ---
        TimeSpan now = Times[0][0].TimeOfDay;

        // Capture the first 5-minute candle (e.g. 9:30–9:35) as the range
        TimeSpan biasStart = new TimeSpan(SessionStart.Hours, SessionStart.Minutes, 0);
        TimeSpan biasEnd = biasStart.Add(TimeSpan.FromMinutes(BiasDuration));

        // Enter bias window
        if (!isInBiasWindow && now >= biasStart && now <= biasEnd)
        {
            sessionHigh = High[0];
            sessionLow  = Low[0];
            isInBiasWindow = true;
            hasCapturedRange = false;
            DebugPrint($"[Bias] Started bias window {biasStart}–{biasEnd}");
        }
        else if (isInBiasWindow && now <= biasEnd)
        {
            // keep updating range
            sessionHigh = Math.Max(sessionHigh, High[0]);
            sessionLow  = Math.Min(sessionLow, Low[0]);
        }
        else if (isInBiasWindow && now > biasEnd && !hasCapturedRange)
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

        if (TickBasedRangeFlatten && ShouldFlattenOnRangeTouch())
            return;

        // ---------------------------------------------
        // STEP 1: Detect FIRST TP touch to activate trailing
        // ---------------------------------------------
        if (Trailing && !trailingActivated && Position.MarketPosition != MarketPosition.Flat)
        {
            // LONG TP touch
            if (Position.MarketPosition == MarketPosition.Long && High[0] >= entryTakeProfitPrice)
            {
                trailingActivated = true;
                activationWickLow = Low[0];
                DebugPrint("[TRAIL] TP touched → Trailing ACTIVATED.");
            }

            // SHORT TP touch
            if (Position.MarketPosition == MarketPosition.Short && Low[0] <= entryTakeProfitPrice)
            {
                trailingActivated = true;
                activationWickHigh = High[0];
                DebugPrint("[TRAIL] TP touched → Trailing ACTIVATED.");
            }
        }

        // Bar-close validation, using wick-reference
        if (IsFirstTickOfBar && trailingActivated)
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (Close[0] < activationWickLow)
                {
                    DebugPrint("❌ TRAIL INVALIDATED - close < activationWickLow. Exiting.");
                    TryExitAll(GetCurrentBid(), "TrailInvalidation");
                    return;
                }
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Close[0] > activationWickHigh)
                {
                    DebugPrint("❌ TRAIL INVALIDATED - close > activationWickHigh. Exiting.");
                    TryExitAll(GetCurrentAsk(), "TrailInvalidation");
                    return;
                }
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
                double newSL = double.NaN;

                if (Position.MarketPosition == MarketPosition.Long)
                    newSL = Instrument.MasterInstrument.RoundToTickSize(Low[1]);

                else if (Position.MarketPosition == MarketPosition.Short)
                    newSL = Instrument.MasterInstrument.RoundToTickSize(High[1]);

                if (!double.IsNaN(newSL))
                {
                    bool canMove =
                        (Position.MarketPosition == MarketPosition.Long && (double.IsNaN(trailingTarget) || newSL > trailingTarget))
                        ||
                        (Position.MarketPosition == MarketPosition.Short && (double.IsNaN(trailingTarget) || newSL < trailingTarget));

                    if (canMove)
                        ReplaceStopOrder(newSL);
                }
            }

			TryEntrySignal(false);  // breakout & engulfing
			//TryClosePosition();
			nextEvaluationTime = nextEvaluationTime.AddMinutes(5);
		}

		// --- Rule 3 retrace runs on *every* tick
		TryEntrySignal(true);

        UpdateActiveFvgs();

        // --- Draw trailing activation + trailing line ---
        if (Trailing && Position.MarketPosition != MarketPosition.Flat)
        {
            // 1. Draw trailing stop line only when trailing is active
            if (trailingActivated)
            {
                Draw.HorizontalLine(this, "trailingLine", trailingTarget, Brushes.Goldenrod);
            }
            else
            {
                // Remove old trailing line when not active yet
                RemoveDrawObject("trailingLine");
            }

            // 2. Draw the activation threshold line (green)
            if (!double.IsNaN(entryTakeProfitPrice))
            {
                Draw.HorizontalLine(this, "trailingActivationLine", entryTakeProfitPrice, Brushes.LimeGreen);
            }
        }
        else
        {
            // Clean up when position is flat
            RemoveDrawObject("trailingLine");
            RemoveDrawObject("trailingActivationLine");
        }
    }

	private bool TryFindFVG_Closed(out double high, out double low)
    {
        high = double.NaN;
        low  = double.NaN;

        if (CurrentBar < 3)
            return false;

        bool bullishFVG = Low[1] > High[3];
        bool bearishFVG = High[1] < Low[3];

        bool body3Inside = Open[3] <= sessionHigh && Close[3] <= sessionHigh && Open[3] >= sessionLow && Close[3] >= sessionLow;
        bool body2Inside = Open[2] <= sessionHigh && Close[2] <= sessionHigh && Open[2] >= sessionLow && Close[2] >= sessionLow;
        bool body1Inside = Open[1] <= sessionHigh && Close[1] <= sessionHigh && Open[1] >= sessionLow && Close[1] >= sessionLow;

        bool insideRange = body3Inside || body2Inside || body1Inside;

        if (bullishFVG && insideRange)
        {
            low  = High[3];
            high = Low[1];
            lastFvgType = FVGType.Bullish;
            fvgFirstCandleHigh = High[3];
            fvgFirstCandleLow  = Low[3];
            AddActiveFvg(low, high, true, CurrentBar - 3, CurrentBar - 1);
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
            AddActiveFvg(low, high, false, CurrentBar - 3, CurrentBar - 1);
            DebugPrint($"[FVG] ✅ Bearish (closed). Zone=[{low:F2},{high:F2}]");
            return true;
        }

        // No log for invalid FVG → reduces spam
        lastFvgType = FVGType.None;
        return false;
    }

    private bool ShouldSkipBarUpdate()
    {
        if (BarsInProgress != 0)
            return true;
        if (invalidBarsPeriod)
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

        // Always reset after a completed entry or invalidation
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

        if (!hasCapturedRange)
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
        else if (reason.Equals("RangeTouch"))
            name = "RangeTouch";

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

    private bool ShouldFlattenOnRangeTouch()
    {
        if (!hasCapturedRange)
            return false;

        if (Position.MarketPosition == MarketPosition.Flat)
            return false;

        double bid = GetCurrentBid();
        double ask = GetCurrentAsk();

        if (Position.MarketPosition == MarketPosition.Long && bid <= sessionHigh)
        {
            DebugPrint($"[Range] Touch detected (long). Bid={bid:F2}, RangeHigh={sessionHigh:F2} → flatten.");
            TryExitAll(sessionHigh, "RangeTouch");
            return true;
        }

        if (Position.MarketPosition == MarketPosition.Short && ask >= sessionLow)
        {
            DebugPrint($"[Range] Touch detected (short). Ask={ask:F2}, RangeLow={sessionLow:F2} → flatten.");
            TryExitAll(sessionLow, "RangeTouch");
            return true;
        }

        return false;
    }

#region NinjaTrader Event Routing
    protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
                                      double averageFillPrice, OrderState orderState, DateTime time,
                                      ErrorCode error, string comment)
    {
        if (order == null)
            return;

        // ============================================================
        // 🔥 TRAILING STOP STATE MACHINE
        // ------------------------------------------------------------
        // When a StopLoss cancel FINISHES for a trailing move, submit
        // the new SL at pendingTrailStopPrice.
        // ============================================================
        if (Trailing
            && trailingActivated
            && trailCancelPending
            && order.Name == "StopLoss"
            && order.Instrument.FullName == Instrument.FullName
            && orderState == OrderState.Cancelled)
        {
            DebugPrint("[TRAIL] Confirmed old SL CANCELLED – submitting new SL now.");

            trailCancelPending = false;

            if (!double.IsNaN(pendingTrailStopPrice) &&
                Position.MarketPosition != MarketPosition.Flat)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    hardStopOrder = ExitLongStopMarket(
                        0, true, NumberOfContracts, pendingTrailStopPrice,
                        "StopLoss", currentSignalName);
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    hardStopOrder = ExitShortStopMarket(
                        0, true, NumberOfContracts, pendingTrailStopPrice,
                        "StopLoss", currentSignalName);
                }

                trailingTarget = pendingTrailStopPrice;
                DebugPrint($"[TRAIL] New SL submitted → {pendingTrailStopPrice:F2}");
            }

            pendingTrailStopPrice = double.NaN;
            // (Do not return — let rest of OnOrderUpdate continue)
        }

        // ============================================================
        // ORIGINAL LOGIC — DO NOT TOUCH
        // ============================================================

        if (Trailing
            && order != null
            && order.OrderType == OrderType.StopMarket               // only real stop orders
            && order.Instrument.FullName == Instrument.FullName       // only this instrument
            && (order.OrderState == OrderState.Accepted || order.OrderState == OrderState.Working))
        {
            hardStopOrder   = order;
            trailingTarget  = order.StopPrice;

            DebugPrint($"[TRAIL] Initial SL working. trailingTarget={trailingTarget:F2}, name={order.Name}, action={order.OrderAction}");
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

        if (order != null && orderState == OrderState.Filled &&
            (order.Name.Contains("Profit target") || IsProfitOrder(order)))
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
            entryPrice = execution.Price;

            double stopPx;
            double takePx;

            double firstHigh = fvgFirstCandleHigh;
            double firstLow  = fvgFirstCandleLow;

            // Fallback if FVG candle data is invalid
            if (double.IsNaN(firstHigh) || double.IsNaN(firstLow))
            {
                firstHigh = High[0];
                firstLow  = Low[0];
                DebugPrint("⚠ Using current bar high/low for SL because FVG reference was invalid.");
            }

            if (execution.Order.Name.Contains("Long"))
            {
                if (UseFixedStopLossPoints && StopLossPoints > 0)
                    stopPx = Instrument.MasterInstrument.RoundToTickSize(entryPrice - StopLossPoints);
                else
                    stopPx = Instrument.MasterInstrument.RoundToTickSize(firstLow - TickSize);
                double risk = entryPrice - stopPx;
                if (UseFixedTakeProfitPoints && TakeProfitPoints > 0)
                    takePx = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TakeProfitPoints);
                else
                    takePx = Instrument.MasterInstrument.RoundToTickSize(entryPrice + (RiskRewardRatio * risk));
            }
            else
            {
                if (UseFixedStopLossPoints && StopLossPoints > 0)
                    stopPx = Instrument.MasterInstrument.RoundToTickSize(entryPrice + StopLossPoints);
                else
                    stopPx = Instrument.MasterInstrument.RoundToTickSize(firstHigh + TickSize);
                double risk = stopPx - entryPrice;
                if (UseFixedTakeProfitPoints && TakeProfitPoints > 0)
                    takePx = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TakeProfitPoints);
                else
                    takePx = Instrument.MasterInstrument.RoundToTickSize(entryPrice - (RiskRewardRatio * risk));
            }

            entryTakeProfitPrice = takePx;

            // Now place stop order
            if (execution.Order.Name.Contains("Long"))
                hardStopOrder = ExitLongStopMarket(0, true, NumberOfContracts, stopPx, "StopLoss", currentSignalName);
            else
                hardStopOrder = ExitShortStopMarket(0, true, NumberOfContracts, stopPx, "StopLoss", currentSignalName);

            // Optional profit target
            if (!double.IsNaN(entryTakeProfitPrice))
            {
                if (execution.Order.Name.Contains("Long"))
                {
                    var tp = ExitLongLimit(0, true, NumberOfContracts, entryTakeProfitPrice, "ProfitLong", currentSignalName);
                    profitOrders.Add(tp);
                }
                else
                {
                    var tp = ExitShortLimit(0, true, NumberOfContracts, entryTakeProfitPrice, "ProfitShort", currentSignalName);
                    profitOrders.Add(tp);
                }
            }

            DebugPrint($"📌 REAL entry={entryPrice:F2}, SL={stopPx:F2}, TP={takePx:F2}, Risk={(Math.Abs(entryPrice - stopPx)):F2}");
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
            // Calculate PnL based on entry and exit prices
            double tradePnL = 0;

            if (lastTradeWasLong)
                tradePnL = (price - entryPrice) * execution.Quantity * Instrument.MasterInstrument.PointValue;
            else
                tradePnL = (entryPrice - price) * execution.Quantity * Instrument.MasterInstrument.PointValue;

            // A trade is a win if exit price is better than entry, regardless of how it exited
            bool wasWin = tradePnL > 0;

            if (wasWin)
            {
                tpCountToday++;
                DebugPrint($"🎯 Win recorded (PnL: ${tradePnL:F2}). Wins Today = {tpCountToday}/{MaxTPsPerDay}");

                if (tpCountToday >= MaxTPsPerDay)
                {
                    dailyLimitReached = true;
                    DebugPrint("⛔ Max wins reached, no more trades today.");
                }
            }
            else
            {
                lossCountToday++;
                DebugPrint($"❌ Loss recorded (PnL: ${tradePnL:F2}). Losses Today = {lossCountToday}/{MaxLossesPerDay}");

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

        // 🔄 reset trailing state
        trailingActivated      = false;
        trailingTarget         = double.NaN;
        pendingTrailStopPrice  = double.NaN;
        trailCancelPending     = false;

        if (isStrategyAnalyzer)
            lastExitBarAnalyzer = -1;
    }

    private void PlaceEntryIfTriggered()
    {
        if (dailyLimitReached)
        {
            DebugPrint("⛔ Max TPs reached — blocking entry.");
            return;
        }
        
        bool isLong  = lastFvgType == FVGType.Bullish;
        bool isShort = lastFvgType == FVGType.Bearish;

        string signalName = isLong ? "Long" : "Short";
        currentSignalName = signalName;

        printTradeDevider();

        // ---- ENTRY CONFIRMATION (optional) ----
        if (RequireEntryConfirmation)
        {
            double previewPrice = Close[0]; // only for UI confirmation, not used for TP/SL
            if (!ShowEntryConfirmation(signalName, previewPrice, NumberOfContracts))
            {
                DebugPrint($"User declined {signalName} entry via confirmation dialog.");
                return;
            }
        }

        // ---- ENTRY ORDER ----
        if (isLong)
        {
            DebugPrint("Submitting LONG entry order...");
            entryOrder = EnterLong(NumberOfContracts, signalName);
        }
        else
        {
            DebugPrint("Submitting SHORT entry order...");
            entryOrder = EnterShort(NumberOfContracts, signalName);
        }

        lastTradeWasLong = isLong;
        orderPlaced = true;

        DebugPrint($"📌 Entry submitted ({signalName}). Waiting for fill to calculate SL/TP...");
    }


    private bool IsEntryOrder(Order o) =>
        o == entryOrder || o.Name == "Long" || o.Name == "Short" || o.Name == "LongMid" || o.Name == "ShortMid";
    private bool IsProfitOrder(Order o) => o.Name.StartsWith("ProfitLong") || o.Name.StartsWith("ProfitShort");

    private void ExitAtSessionEnd()
    {
        // Session end is a "hard exit" – not a trailing move
        trailCancelPending    = false;
        pendingTrailStopPrice = double.NaN;
        
        DebugPrint(
            $"Session ended, closing position. MarketPosition={Position.MarketPosition}, Qty={Position.Quantity}");
        if (isStrategyAnalyzer)
            lastExitBarAnalyzer = CurrentBar;
        var act = Position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
        double dummyExitP = Position.MarketPosition == MarketPosition.Long ? GetCurrentBid() : GetCurrentAsk();
        TryExitAll(dummyExitP, "SessionEnd");
        CancelAllOrders();
    }

    private void UpdateTrailingSL(double newSL)
    {
        if (hardStopOrder == null)
            return;

        // Only modify if order is active
        if (hardStopOrder.OrderState == OrderState.Accepted ||
            hardStopOrder.OrderState == OrderState.Working)
        {
            ChangeOrder(hardStopOrder, hardStopOrder.Quantity, hardStopOrder.LimitPrice, newSL);
            trailingTarget = newSL;
            DebugPrint($"[TRAIL] SL Updated to {newSL}");
        }
    }

    private void ReplaceStopOrder(double newSL)
    {
        if (!Trailing || !trailingActivated)
            return;  // ✅ only trail when logic is active

        // We must have an existing stop order to replace
        if (hardStopOrder == null)
            return;

        // Only cancel if the current SL is active
        if (hardStopOrder.OrderState == OrderState.Working ||
            hardStopOrder.OrderState == OrderState.Accepted)
        {
            pendingTrailStopPrice = newSL;
            trailCancelPending    = true;

            DebugPrint($"[TRAIL] Requesting cancel of old SL at {hardStopOrder.StopPrice:F2} to move to {newSL:F2}");
            CancelOrder(hardStopOrder);
        }
    }

    private void CancelAllOrders()
    {
        DebugPrint("CancelAllOrders called. EntryOrder=" + (entryOrder?.Name ?? "null") +
                   ", HardStopOrder=" + (hardStopOrder?.Name ?? "null") + ", ProfitOrders=" + profitOrders.Count);


        // ❌ Any bulk cancel is *not* a trailing move
        trailCancelPending    = false;
        pendingTrailStopPrice = double.NaN;

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
        // FVGs are now tracked/drawn as rectangles via AddActiveFvg/UpdateActiveFvgs.
    }

    private void DrawFVGZoneProgress()
    {
        // FVGs are now tracked/drawn as rectangles via AddActiveFvg/UpdateActiveFvgs.
    }

    private class FvgBox
    {
        public string Tag;
        public double Lower;
        public double Upper;
        public bool IsBullish;
        public bool IsActive;
        public int StartBarIndex;
        public int EndBarIndex;
        public int CreatedBarIndex;
        public int InvalidatedBarIndex;
        public DateTime SessionDate;
    }

    private void AddActiveFvg(double lower, double upper, bool bullish, int startBarIndex, int endBarIndex)
    {
        var fvg = new FvgBox
        {
            Tag = $"FiveMin_FVG_{fvgCounter++}_{Time[0]:yyyyMMdd_HHmmss}",
            Lower = Math.Min(lower, upper),
            Upper = Math.Max(lower, upper),
            IsBullish = bullish,
            IsActive = true,
            StartBarIndex = Math.Max(0, startBarIndex),
            EndBarIndex = Math.Max(0, endBarIndex),
            CreatedBarIndex = CurrentBar,
            InvalidatedBarIndex = -1,
            SessionDate = Time[0].Date
        };

        activeFvgs.Add(fvg);

        DrawFvgRectangle(fvg, FvgFillBrush ?? Brushes.Transparent, ClampOpacity(FvgOpacity));
    }

    private void UpdateActiveFvgs()
    {
        if (activeFvgs.Count == 0)
            return;

        for (int i = activeFvgs.Count - 1; i >= 0; i--)
        {
            var fvg = activeFvgs[i];
            if (!fvg.IsActive && !ShowInvalidatedFvgs)
                continue;

            if (fvg.IsActive)
                fvg.EndBarIndex = CurrentBar;

            int startBarsAgo = CurrentBar - fvg.StartBarIndex;
            int endBarsAgo = fvg.IsActive ? 0 : Math.Max(0, CurrentBar - fvg.EndBarIndex);
            if (startBarsAgo < 0)
                startBarsAgo = 0;

            if (fvg.IsActive)
            {
                bool allowHitCheck = CurrentBar > fvg.CreatedBarIndex;
                bool invalidated = allowHitCheck && (
                    (fvg.IsBullish && Close[0] < fvg.Lower && Close[0] < Open[0]) ||
                    (!fvg.IsBullish && Close[0] > fvg.Upper && Close[0] > Open[0]));

                if (invalidated)
                {
                    fvg.IsActive = false;
                    fvg.InvalidatedBarIndex = CurrentBar;
                    fvg.EndBarIndex = CurrentBar;

                    if (!ShowInvalidatedFvgs)
                    {
                        RemoveDrawObject(fvg.Tag);
                    }
                    else
                    {
                        int fixedEndBarsAgo = Math.Max(0, CurrentBar - fvg.EndBarIndex);
                        DrawFvgRectangle(
                            fvg,
                            InvalidatedFvgFillBrush ?? Brushes.Transparent,
                            ClampOpacity(InvalidatedFvgOpacity),
                            startBarsAgo,
                            fixedEndBarsAgo);
                    }
                }
                else
                {
                    DrawFvgRectangle(fvg, FvgFillBrush ?? Brushes.Transparent, ClampOpacity(FvgOpacity), startBarsAgo, endBarsAgo);
                }
            }
            else if (ShowInvalidatedFvgs)
            {
                int fixedEndBarsAgo = Math.Max(0, CurrentBar - fvg.EndBarIndex);
                DrawFvgRectangle(fvg, InvalidatedFvgFillBrush ?? Brushes.Transparent, ClampOpacity(InvalidatedFvgOpacity), startBarsAgo, fixedEndBarsAgo);
            }
        }
    }

    private void DrawFvgRectangle(FvgBox fvg, Brush fill, int opacity, int startBarsAgo = 2, int endBarsAgo = 0)
    {
        Draw.Rectangle(
            this,
            fvg.Tag,
            false,
            startBarsAgo,
            fvg.Lower,
            endBarsAgo,
            fvg.Upper,
            Brushes.Transparent,
            fill,
            opacity
        );
    }

    private int ClampOpacity(int opacity)
    {
        if (opacity < 0)
            return 0;
        if (opacity > 255)
            return 255;
        return opacity;
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

        // Reset all trailing-related state in one place
        trailingActivated      = false;
        trailingTarget         = double.NaN;
        entryTakeProfitPrice   = double.NaN;
        entryPrice             = double.NaN;
        pendingTrailStopPrice  = double.NaN;
        trailCancelPending     = false;
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
            if (tod >= startTime && tod <= endTime)
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
