#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public enum DREntryMethod
    {
        Method1_PullbackLimit,
        Method2_OppositeCandle
    }

    public class DR : Strategy
    {
        #region User Inputs
        [NinjaScriptProperty]
        [Display(Name = "Entry Method", GroupName = "01. DR Parameters", Order = 0)]
        public DREntryMethod EntryMethod { get; set; }

        // Min DR size in points (height / TickSize) – skip trading too small DRs
        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min DR Size (points)", GroupName = "01. DR Parameters", Order = 3)]
        public double MinDrSizePoints { get; set; }

        // Global minimum Risk/Reward filter (applied at least to Method 2 and safe to reuse for Method 1)
        [NinjaScriptProperty]
        [Range(1.0, 10.0)]
        [Display(Name = "Min Risk/Reward", GroupName = "01. DR Parameters", Order = 4)]
        public double MinRiskReward { get; set; }

        // Stop loss distance in % of DR height for Method 1
        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "M1 SL % of DR", GroupName = "01. DR Parameters", Order = 5)]
        public double M1StopPercentOfDr { get; set; }

        // Take profit level in % of DR height for Method 1 (50% ~ mid)
        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "M1 TP % of DR", GroupName = "01. DR Parameters", Order = 6)]
        public double M1TpPercentOfDr { get; set; }

        // Price level % of DR that must be touched before looking for opposite candle
        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "M2 Price Touch %", GroupName = "01. DR Parameters", Order = 7)]
        public double M2PriceTouchPercent { get; set; }

        // Take profit level in % of DR height for Method 2 (usually 50-62%)
        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "M2 TP % of DR", GroupName = "01. DR Parameters", Order = 8)]
        public double M2TpPercentOfDr { get; set; }

        // [NinjaScriptProperty]
        // [Range(1, 50)]
        // [Display(Name = "Leg Swing Lookback", Description = "Maximum bars to look back for leg low/high around breakout", GroupName = "01. DR Parameters", Order = 1)]
        internal int LegSwingLookback { get; set; }

        // [NinjaScriptProperty]
        // [Range(1, 10)]
        // [Display(Name = "Swing Strength", Description = "Bars on each side for swing high/low detection", GroupName = "01. DR Parameters", Order = 2)]
        internal int SwingStrength { get; set; }

        [XmlIgnore]
        [Display(Name = "DR Box Brush", Description = "Fill color for DR boxes", GroupName = "02. Visual Settings", Order = 1)]
        public Brush DrBoxBrush { get; set; }

        [Browsable(false)]
        public string DrBoxBrushSerializable
        {
            get { return Serialize.BrushToString(DrBoxBrush); }
            set { DrBoxBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "DR Outline Brush", Description = "Outline color for DR boxes", GroupName = "02. Visual Settings", Order = 2)]
        public Brush DrOutlineBrush { get; set; }

        [Browsable(false)]
        public string DrOutlineBrushSerializable
        {
            get { return Serialize.BrushToString(DrOutlineBrush); }
            set { DrOutlineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "DR Mid-Line Brush", Description = "Color for mid-line", GroupName = "02. Visual Settings", Order = 3)]
        public Brush DrMidLineBrush { get; set; }

        [Browsable(false)]
        public string DrMidLineBrushSerializable
        {
            get { return Serialize.BrushToString(DrMidLineBrush); }
            set { DrMidLineBrush = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "Box Opacity", Description = "Transparency of DR box fill (0-100)", GroupName = "02. Visual Settings", Order = 4)]
        public int BoxOpacity { get; set; }

        [Range(1, 5)]
        [Display(Name = "Line Width", Description = "Width of mid-line", GroupName = "02. Visual Settings", Order = 5)]
        public int LineWidth { get; set; }

        [Range(0, 100)]
        [Display(Name = "Line Opacity", Description = "Transparency of DR lines (0-100)", GroupName = "02. Visual Settings", Order = 6)]
        public int LineOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Debug Logging", Description = "Enable detailed debug logging to Output window", GroupName = "03. Debug", Order = 1)]
        public bool DebugLogging { get; set; }
        #endregion

        #region State Variables
        // Current DR state
        private double currentDrHigh;
        private double currentDrLow;
        private double currentDrMid;
        private int currentDrStartBar;
        private int currentDrEndBar;
        private string currentDrBoxTag;
        private string currentDrMidLineTag;
        private string currentDrTopLineTag;
        private string currentDrBottomLineTag;
        // -1 = last breakout was bearish, +1 = bullish, 0 = none yet
        private int lastBreakoutDirection;
        // True while we are in a breakout "strike" where wicks can extend the DR
        private bool inBreakoutStrike;

        // DR counter for unique tags
        private int drCounter;

        // Flag to track if we have an active DR
        private bool hasActiveDR;

        // Trade & DR direction
        private int currentDrDirection;   // +1 bullish DR (breakout up), -1 bearish DR (breakout down)
        private bool currentDrTradable;   // DR is large enough to trade (>= MinDrSizePoints)
        private int lastTradedDrId;       // to allow at most one trade per DR

        // Pending signal state for intrabar execution (used by both methods)
        private bool pendingLongSignal;
        private bool pendingShortSignal;
        private double pendingEntryPrice;
        private double pendingStopPrice;
        private double pendingTargetPrice;

        // Method 2 specific
        private bool m2PriceTouched;     // true once price has touched the M2PriceTouchPercent level
        #endregion

        #region NinjaScript Lifecycle
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "DR Strategy - Draws Dealing Range boxes with mid-lines (no trading)";
                Name = "DR";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Default parameter values
                EntryMethod = DREntryMethod.Method1_PullbackLimit;
                MinDrSizePoints = 4;
                MinRiskReward = 1.0;
                M1StopPercentOfDr = 10;
                M1TpPercentOfDr = 50;
                M2PriceTouchPercent = 88;
                M2TpPercentOfDr = 55;
                LegSwingLookback = 10;
                SwingStrength = 1;
                BoxOpacity = 10;
                LineWidth = 1;
                LineOpacity = 30;
                DebugLogging = false;

                // Default colors
                DrBoxBrush = Brushes.DodgerBlue;
                DrOutlineBrush = Brushes.DodgerBlue;
                DrMidLineBrush = Brushes.White;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                // Initialize state
                drCounter = 0;
                hasActiveDR = false;
                currentDrHigh = 0;
                currentDrLow = 0;
                currentDrMid = 0;
                currentDrStartBar = -1;
                currentDrEndBar = -1;
                currentDrBoxTag = string.Empty;
                currentDrMidLineTag = string.Empty;
                currentDrTopLineTag = string.Empty;
                currentDrBottomLineTag = string.Empty;
                lastBreakoutDirection = 0;
                inBreakoutStrike = false;
                currentDrDirection = 0;
                currentDrTradable = true;
                lastTradedDrId = -1;
                pendingLongSignal = false;
                pendingShortSignal = false;
                pendingEntryPrice = 0;
                pendingStopPrice = 0;
                pendingTargetPrice = 0;
                m2PriceTouched = false;

                // Freeze brushes for performance
                if (DrBoxBrush != null && DrBoxBrush.CanFreeze)
                    DrBoxBrush.Freeze();
                if (DrOutlineBrush != null && DrOutlineBrush.CanFreeze)
                    DrOutlineBrush.Freeze();
                if (DrMidLineBrush != null && DrMidLineBrush.CanFreeze)
                    DrMidLineBrush.Freeze();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 0)
            {
                if (CurrentBars[0] < SwingStrength * 2 + LegSwingLookback || CurrentBars[1] < 1)
                    return;

                if (!IsFirstTickOfBar)
                    return;

                // If we don't have an active DR, try to create the initial one
                if (!hasActiveDR)
                {
                    DebugPrint(string.Format("No active DR. Attempting to create initial DR. Close={0:F2}", Close[0]));
                    TryCreateInitialDR();
                    return;
                }

                if (hasActiveDR && inBreakoutStrike)
                {
                    if (lastBreakoutDirection == 1)
                    {
                        if (High[0] > currentDrHigh)
                        {
                            currentDrHigh = High[0];
                            currentDrMid = (currentDrHigh + currentDrLow) / 2.0;
                            ExtendCurrentDR();
                            DebugPrint("Bullish strike wick extension. New DR High=" + currentDrHigh);
                        }
                        else
                        {
                            inBreakoutStrike = false;
                            DebugPrint("Bullish strike ended (no new high). Waiting for next close outside for new DR.");
                        }
                    }
                    else if (lastBreakoutDirection == -1)
                    {
                        if (Low[0] < currentDrLow)
                        {
                            currentDrLow = Low[0];
                            currentDrMid = (currentDrHigh + currentDrLow) / 2.0;
                            ExtendCurrentDR();
                            DebugPrint("Bearish strike wick extension. New DR Low=" + currentDrLow);
                        }
                        else
                        {
                            inBreakoutStrike = false;
                            DebugPrint("Bearish strike ended (no new low). Waiting for next close outside for new DR.");
                        }
                    }
                }

                // Check if current bar is still inside the DR
                bool insideDR = Close[0] >= currentDrLow && Close[0] <= currentDrHigh;

                if (insideDR)
                {
                    // Extend the current DR to the right
                    // DebugPrint(string.Format("Price inside DR range. Extending DR box. Close={0:F2}, DR Low={1:F2}, DR High={2:F2}",
                    //     Close[0], currentDrLow, currentDrHigh));
                    ExtendCurrentDR();
                }
                else
                {
                    // Breakout detected - finalize current DR and create new one
                    bool bullishBreakout = Close[0] > currentDrHigh;
                    bool bearishBreakout = Close[0] < currentDrLow;

                    int previousBreakoutDirection = lastBreakoutDirection;
                    int currentBreakoutDirection = 0;
                    if (bullishBreakout)
                        currentBreakoutDirection = 1;
                    else if (bearishBreakout)
                        currentBreakoutDirection = -1;

                    if (currentBreakoutDirection != 0)
                    {
                        bool isContinuation = currentBreakoutDirection == lastBreakoutDirection && inBreakoutStrike;

                        if (previousBreakoutDirection != 0 && currentBreakoutDirection != previousBreakoutDirection)
                            ClearPendingSignals(true);

                        lastBreakoutDirection = currentBreakoutDirection;

                        if (currentBreakoutDirection == 1)
                        {
                            DebugPrint(string.Format("BULLISH BREAKOUT detected! Close={0:F2} > DR High={1:F2}", Close[0], currentDrHigh));
                            CreateNewDRFromBullishBreakout(isContinuation);
                        }
                        else if (currentBreakoutDirection == -1)
                        {
                            DebugPrint(string.Format("BEARISH BREAKOUT detected! Close={0:F2} < DR Low={1:F2}", Close[0], currentDrLow));
                            CreateNewDRFromBearishBreakout(isContinuation);
                        }

                        inBreakoutStrike = true;
                    }
                }

                ProcessMethod2Signals();
            }
            else if (BarsInProgress == 1)
            {
                if (CurrentBars[0] < SwingStrength * 2 + LegSwingLookback || CurrentBars[1] < 1)
                    return;

                SubmitPendingOrders();
            }
        }
        #endregion

        #region DR Creation Logic
        private void TryCreateInitialDR()
        {
            // Find the most recent swing high and swing low
            double swingHigh = double.MinValue;
            double swingLow = double.MaxValue;
            int swingHighBar = -1;
            int swingLowBar = -1;

            // Look back to find swings
            for (int i = SwingStrength; i < Math.Min(CurrentBar, 100); i++)
            {
                if (IsSwingHigh(i))
                {
                    swingHigh = High[i];
                    swingHighBar = CurrentBar - i;
                    DebugPrint(string.Format("Found initial swing HIGH at bar {0}, price={1:F2}", swingHighBar, swingHigh));
                    break;
                }
            }

            for (int i = SwingStrength; i < Math.Min(CurrentBar, 100); i++)
            {
                if (IsSwingLow(i))
                {
                    swingLow = Low[i];
                    swingLowBar = CurrentBar - i;
                    DebugPrint(string.Format("Found initial swing LOW at bar {0}, price={1:F2}", swingLowBar, swingLow));
                    break;
                }
            }

            // Need both swings to create initial DR
            if (swingHighBar < 0 || swingLowBar < 0)
            {
                DebugPrint("Cannot create initial DR - missing swing high or swing low");
                return;
            }

            // Create initial DR from these swings
            int startBar = Math.Min(swingHighBar, swingLowBar);
            DebugPrint(string.Format("Creating INITIAL DR from swings. StartBar={0}", startBar));
            CreateNewDR(swingLow, swingHigh, startBar);
            lastBreakoutDirection = 0;
            inBreakoutStrike = false;
        }

        private void CreateNewDRFromBullishBreakout(bool isContinuation)
        {
            // Bullish breakout: Close[0] > currentDrHigh
            // Find the leg low that led to this breakout

            DebugPrint(string.Format("Starting bullish breakout DR creation. Looking back {0} bars.", LegSwingLookback));

            double epsilon = TickSize * 0.5;
            double legLow = double.NaN;
            int legLowIndex = -1;

            // Find the closest internal swing low within the lookback
            for (int i = 1; i <= Math.Min(LegSwingLookback, CurrentBar); i++)
            {
                if (IsSwingLow(i))
                {
                    legLow = Low[i];
                    legLowIndex = i;
                    break; // first (closest) swing low wins
                }
            }

            if (legLowIndex == -1)
            {
                if (isContinuation)
                {
                    DebugPrint("No internal swing LOW found (continuation). Extending current DR with breakout wick high.");
                    currentDrHigh = Math.Max(currentDrHigh, High[0]);
                    currentDrMid = (currentDrHigh + currentDrLow) / 2.0;
                    ExtendCurrentDR();
                    return;
                }
                else
                {
                    double newDrLow = Low[0];
                    double newDrHigh = High[0];
                    int startBar = CurrentBar;
                    DebugPrint(string.Format("No swing LOW found on reversal. Creating NEW DR from breakout candle only. Low={0:F2}, High={1:F2}, StartBar={2}",
                        newDrLow, newDrHigh, startBar));
                    CreateNewDR(newDrLow, newDrHigh, startBar);
                    return;
                }
            }

            bool isNewInternalLegLow = legLow > currentDrLow + epsilon;

            if (isContinuation)
            {
                if (!isNewInternalLegLow)
                {
                    DebugPrint("Closest swing LOW is at or below current DR low (continuation). Extending current DR with breakout wick high.");
                    currentDrHigh = Math.Max(currentDrHigh, High[0]);
                    currentDrMid = (currentDrHigh + currentDrLow) / 2.0;
                    ExtendCurrentDR();
                    return;
                }
            }

            double finalDrLow = legLow;
            double finalDrHigh = High[0];
            int lowBarIndex = CurrentBar - legLowIndex;

            DebugPrint(string.Format("Creating NEW DR from bullish breakout. Low={0:F2}, High={1:F2}, StartBar={2} (isContinuation={3})",
                finalDrLow, finalDrHigh, lowBarIndex, isContinuation));
            CreateNewDR(finalDrLow, finalDrHigh, lowBarIndex);
        }

        private void CreateNewDRFromBearishBreakout(bool isContinuation)
        {
            // Bearish breakout: Close[0] < currentDrLow
            // Find the leg high that led to this breakout

            DebugPrint(string.Format("Starting bearish breakout DR creation. Looking back {0} bars.", LegSwingLookback));

            double epsilon = TickSize * 0.5;
            double legHigh = double.NaN;
            int legHighIndex = -1;

            // Find the closest internal swing high within the lookback
            for (int i = 1; i <= Math.Min(LegSwingLookback, CurrentBar); i++)
            {
                if (IsSwingHigh(i))
                {
                    legHigh = High[i];
                    legHighIndex = i;
                    break; // first (closest) swing high wins
                }
            }

            if (legHighIndex == -1)
            {
                if (isContinuation)
                {
                    DebugPrint("No internal swing HIGH found (continuation). Extending current DR with breakout wick low.");
                    currentDrLow = Math.Min(currentDrLow, Low[0]);
                    currentDrMid = (currentDrHigh + currentDrLow) / 2.0;
                    ExtendCurrentDR();
                    return;
                }
                else
                {
                    double newDrHigh = High[0];
                    double newDrLow = Low[0];
                    int startBar = CurrentBar;
                    DebugPrint(string.Format("No swing HIGH found on reversal. Creating NEW DR from breakout candle only. Low={0:F2}, High={1:F2}, StartBar={2}",
                        newDrLow, newDrHigh, startBar));
                    CreateNewDR(newDrLow, newDrHigh, startBar);
                    return;
                }
            }

            bool isNewInternalLegHigh = legHigh < currentDrHigh - epsilon;

            if (isContinuation)
            {
                if (!isNewInternalLegHigh)
                {
                    DebugPrint("Closest swing HIGH is at or above current DR high (continuation). Extending current DR with breakout wick low.");
                    currentDrLow = Math.Min(currentDrLow, Low[0]);
                    currentDrMid = (currentDrHigh + currentDrLow) / 2.0;
                    ExtendCurrentDR();
                    return;
                }
            }

            double finalDrHigh = legHigh;
            double finalDrLow = Low[0];
            int highBarIndex = CurrentBar - legHighIndex;

            DebugPrint(string.Format("Creating NEW DR from bearish breakout. Low={0:F2}, High={1:F2}, StartBar={2} (isContinuation={3})",
                finalDrLow, finalDrHigh, highBarIndex, isContinuation));
            CreateNewDR(finalDrLow, finalDrHigh, highBarIndex);
        }

        private void CreateNewDR(double drLow, double drHigh, int startBar)
        {
            // Validate DR dimensions
            if (drHigh <= drLow)
            {
                DebugPrint(string.Format("ERROR: Cannot create DR - invalid dimensions! High={0:F2} <= Low={1:F2}", drHigh, drLow));
                return;
            }

            // Store new DR parameters
            currentDrLow = drLow;
            currentDrHigh = drHigh;
            currentDrMid = (drHigh + drLow) / 2.0;
            currentDrStartBar = startBar;
            currentDrEndBar = CurrentBar;

            // Generate unique tags for this DR
            drCounter++;
            currentDrBoxTag = "DRBox_" + drCounter;
            currentDrMidLineTag = "DRMid_" + drCounter;
            currentDrTopLineTag = "DRTop_" + drCounter;
            currentDrBottomLineTag = "DRBot_" + drCounter;

            hasActiveDR = true;

            double drHeight = drHigh - drLow;
            double drSizePoints = drHeight / TickSize;
            currentDrDirection = lastBreakoutDirection;
            currentDrTradable = drSizePoints >= MinDrSizePoints;
            lastTradedDrId = -1;
            ClearPendingSignals(true);

            // Separator line before logging the new DR creation
            DebugPrint(string.Empty);
            DebugPrint(string.Format("=== DR #{0} CREATED === Low={1:F2}, High={2:F2}, Mid={3:F2}, Height={4:F2}, StartBar={5}, Tags: {6}, {7}",
                drCounter, currentDrLow, currentDrHigh, currentDrMid, drHeight, currentDrStartBar, currentDrBoxTag, currentDrMidLineTag));

            // Draw the new DR
            DrawCurrentDR();

            EvaluateMethod1Setup();
        }

        private void ExtendCurrentDR()
        {
            // Update the end bar to current bar
            currentDrEndBar = CurrentBar;
            currentDrTradable = (currentDrHigh - currentDrLow) / TickSize >= MinDrSizePoints;

            // Redraw to extend to the right
            DrawCurrentDR();
        }
        #endregion

        #region Trading Logic
        private void EvaluateMethod1Setup()
        {
            if (EntryMethod != DREntryMethod.Method1_PullbackLimit)
                return;

            if (!currentDrTradable || currentDrDirection == 0 || lastTradedDrId == drCounter)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double drHeight = currentDrHigh - currentDrLow;
            double entry = 0;
            double stop = 0;
            double tp = 0;

            if (currentDrDirection == -1)
            {
                entry = currentDrHigh;
                stop = entry + drHeight * (M1StopPercentOfDr / 100.0);
                tp = currentDrLow + drHeight * (M1TpPercentOfDr / 100.0);
            }
            else if (currentDrDirection == 1)
            {
                entry = currentDrLow;
                stop = entry - drHeight * (M1StopPercentOfDr / 100.0);
                tp = currentDrLow + drHeight * (M1TpPercentOfDr / 100.0);
            }
            else
            {
                return;
            }

            double risk = currentDrDirection == 1 ? entry - stop : stop - entry;
            double reward = currentDrDirection == 1 ? tp - entry : entry - tp;

            if (risk <= 0 || reward <= 0 || reward / risk < MinRiskReward)
                return;

            ClearPendingSignals(false);

            if (currentDrDirection == 1)
            {
                pendingLongSignal = true;
            }
            else if (currentDrDirection == -1)
            {
                pendingShortSignal = true;
            }

            pendingEntryPrice = entry;
            pendingStopPrice = stop;
            pendingTargetPrice = tp;
            lastTradedDrId = drCounter;
        }

        private void ProcessMethod2Signals()
        {
            if (EntryMethod != DREntryMethod.Method2_OppositeCandle)
                return;

            if (!hasActiveDR || !currentDrTradable || currentDrDirection == 0)
                return;

            if (Position.MarketPosition != MarketPosition.Flat || lastTradedDrId == drCounter)
                return;

            double drHeight = currentDrHigh - currentDrLow;
            if (drHeight <= 0)
                return;

            if (currentDrDirection == -1)
            {
                double touchLine = currentDrHigh - drHeight * (M2PriceTouchPercent / 100.0);
                if (!m2PriceTouched && High[0] >= touchLine)
                    m2PriceTouched = true;

                bool oppositeBullishCandle = Close[0] > Open[0];
                if (m2PriceTouched && oppositeBullishCandle)
                {
                    double entry = Close[0];
                    double stop = currentDrLow;
                    double tp = currentDrLow + drHeight * (M2TpPercentOfDr / 100.0);
                    double risk = entry - stop;
                    double reward = tp - entry;

                    if (risk > 0 && reward > 0 && reward / risk >= MinRiskReward)
                    {
                        ClearPendingSignals(false);
                        pendingLongSignal = true;
                        pendingEntryPrice = entry;
                        pendingStopPrice = stop;
                        pendingTargetPrice = tp;
                        lastTradedDrId = drCounter;
                        m2PriceTouched = false;
                    }
                }
            }
            else if (currentDrDirection == 1)
            {
                double touchLine = currentDrLow + drHeight * (M2PriceTouchPercent / 100.0);
                if (!m2PriceTouched && Low[0] <= touchLine)
                    m2PriceTouched = true;

                bool oppositeBearishCandle = Close[0] < Open[0];
                if (m2PriceTouched && oppositeBearishCandle)
                {
                    double entry = Close[0];
                    double stop = currentDrHigh;
                    double tp = currentDrHigh - drHeight * (M2TpPercentOfDr / 100.0);
                    double risk = stop - entry;
                    double reward = entry - tp;

                    if (risk > 0 && reward > 0 && reward / risk >= MinRiskReward)
                    {
                        ClearPendingSignals(false);
                        pendingShortSignal = true;
                        pendingEntryPrice = entry;
                        pendingStopPrice = stop;
                        pendingTargetPrice = tp;
                        lastTradedDrId = drCounter;
                        m2PriceTouched = false;
                    }
                }
            }
        }

        private void SubmitPendingOrders()
        {
            if (!pendingLongSignal && !pendingShortSignal)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (pendingEntryPrice <= 0 || pendingStopPrice <= 0 || pendingTargetPrice <= 0)
            {
                ClearPendingSignals(false);
                return;
            }

            string longSignalName = EntryMethod == DREntryMethod.Method1_PullbackLimit ? "DR_M1_Long" : "DR_M2_Long";
            string shortSignalName = EntryMethod == DREntryMethod.Method1_PullbackLimit ? "DR_M1_Short" : "DR_M2_Short";

            if (pendingLongSignal)
            {
                EnterLongLimit(0, true, DefaultQuantity, pendingEntryPrice, longSignalName);
                SetStopLoss(longSignalName, CalculationMode.Price, pendingStopPrice, false);
                SetProfitTarget(longSignalName, CalculationMode.Price, pendingTargetPrice);
            }
            else if (pendingShortSignal)
            {
                EnterShortLimit(0, true, DefaultQuantity, pendingEntryPrice, shortSignalName);
                SetStopLoss(shortSignalName, CalculationMode.Price, pendingStopPrice, false);
                SetProfitTarget(shortSignalName, CalculationMode.Price, pendingTargetPrice);
            }

            ClearPendingSignals(false);
        }

        private void ClearPendingSignals(bool resetTouch)
        {
            pendingLongSignal = false;
            pendingShortSignal = false;
            pendingEntryPrice = 0;
            pendingStopPrice = 0;
            pendingTargetPrice = 0;

            if (resetTouch)
                m2PriceTouched = false;
        }
        #endregion

        #region Swing Detection
        private bool IsSwingHigh(int barsAgo)
        {
            if (barsAgo < SwingStrength || CurrentBar - barsAgo < SwingStrength)
                return false;

            double pivot = High[barsAgo];

            for (int i = 1; i <= SwingStrength; i++)
            {
                if (pivot <= High[barsAgo - i] || pivot <= High[barsAgo + i])
                    return false;
            }

            return true;
        }

        private bool IsSwingLow(int barsAgo)
        {
            if (barsAgo < SwingStrength || CurrentBar - barsAgo < SwingStrength)
                return false;

            double pivot = Low[barsAgo];

            for (int i = 1; i <= SwingStrength; i++)
            {
                if (pivot >= Low[barsAgo - i] || pivot >= Low[barsAgo + i])
                    return false;
            }

            return true;
        }
        #endregion

        #region Drawing Methods
        private void DrawCurrentDR()
        {
            if (!hasActiveDR)
                return;

            // Calculate barsAgo values for drawing
            int startBarsAgo = CurrentBar - currentDrStartBar;
            int endBarsAgo = CurrentBar - currentDrEndBar;

            // Ensure valid range
            if (startBarsAgo < 0)
                startBarsAgo = 0;
            if (endBarsAgo < 0)
                endBarsAgo = 0;

            // Draw the DR box (rectangle)
            Draw.Rectangle(
                this,
                currentDrBoxTag,
                false,
                startBarsAgo,
                currentDrLow,
                endBarsAgo,
                currentDrHigh,
                Brushes.Transparent,
                DrBoxBrush,
                BoxOpacity
            );

            // Draw the mid-line
            Brush midLineBrush = GetLineBrush(DrMidLineBrush);
            Draw.Line(
                this,
                currentDrMidLineTag,
                false,
                startBarsAgo,
                currentDrMid,
                endBarsAgo,
                currentDrMid,
                midLineBrush,
                DashStyleHelper.Solid,
                LineWidth
            );

            // Draw top and bottom borders only
            Brush outlineLineBrush = GetLineBrush(DrOutlineBrush);
            Draw.Line(
                this,
                currentDrTopLineTag,
                false,
                startBarsAgo,
                currentDrHigh,
                endBarsAgo,
                currentDrHigh,
                outlineLineBrush,
                DashStyleHelper.Solid,
                LineWidth
            );

            Draw.Line(
                this,
                currentDrBottomLineTag,
                false,
                startBarsAgo,
                currentDrLow,
                endBarsAgo,
                currentDrLow,
                outlineLineBrush,
                DashStyleHelper.Solid,
                LineWidth
            );
        }
        #endregion

        private Brush GetLineBrush(Brush baseBrush)
        {
            if (baseBrush == null)
                return Brushes.Transparent;

            try
            {
                Brush clone = baseBrush.Clone();
                clone.Opacity = Math.Max(0, Math.Min(100, LineOpacity)) / 100.0;
                if (clone.CanFreeze)
                    clone.Freeze();
                return clone;
            }
            catch
            {
                // Fallback to transparent if cloning fails
                return Brushes.Transparent;
            }
        }

        #region Debug Logging
        private void DebugPrint(string message)
        {
            if (!DebugLogging)
                return;

            if (string.IsNullOrWhiteSpace(message))
            {
                // Print a blank line as a visual separator when no message is provided
                Print(string.Empty);
                return;
            }

            Print(string.Format("[{0:yyyy-MM-dd HH:mm:ss}] - {1}", Time[0], message));
        }
        #endregion
    }
}
