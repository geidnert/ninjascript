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
        private double tradeLineEntryPrice;
        private double tradeLineStopPrice;
        private double tradeLineTargetPrice;
        private bool hasTradeLevelLines;
        private string tradeEntryTag;
        private string tradeStopTag;
        private string tradeTargetTag;

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
                tradeLineEntryPrice = 0;
                tradeLineStopPrice = 0;
                tradeLineTargetPrice = 0;
                hasTradeLevelLines = false;
                tradeEntryTag = string.Empty;
                tradeStopTag = string.Empty;
                tradeTargetTag = string.Empty;
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
                            ClearPendingSignals(true, "breakout direction flipped", true);

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
                UpdatePreviewTradeLinesIfNonePending();
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
            // Keep lines visible through DR creation; they will be updated immediately below
            ClearPendingSignals(true, "new DR created", false);
            UpdatePreviewTradeLinesIfNonePending();

            // Separator line before logging the new DR creation
            DebugPrint(string.Empty);
            DebugPrint(string.Format("=== DR #{0} CREATED === Low={1:F2}, High={2:F2}, Mid={3:F2}, Height={4:F2}, StartBar={5}, Tags: {6}, {7}",
                drCounter, currentDrLow, currentDrHigh, currentDrMid, drHeight, currentDrStartBar, currentDrBoxTag, currentDrMidLineTag));
            DebugPrint(string.Format("DR #{0} tradable={1} (height={2:F2}, sizePts={3:F2}, minRequired={4:F2})",
                drCounter, currentDrTradable, drHeight, drSizePoints, MinDrSizePoints));

            // Draw the new DR
            DrawCurrentDR();

            EvaluateMethod1Setup();
        }

        private void ExtendCurrentDR()
        {
            // Update the end bar to current bar
            currentDrEndBar = CurrentBar;
            bool wasTradable = currentDrTradable;
            double drHeight = currentDrHigh - currentDrLow;
            double drSizePoints = drHeight / TickSize;
            currentDrTradable = (currentDrHigh - currentDrLow) / TickSize >= MinDrSizePoints;

            if (wasTradable != currentDrTradable)
            {
                DebugPrint(string.Format("DR tradable state changed → {0} (height={1:F2}, sizePts={2:F2}, minRequired={3:F2})",
                    currentDrTradable, drHeight, drSizePoints, MinDrSizePoints));
            }

            // Redraw to extend to the right
            DrawCurrentDR();
            UpdateTradeLevelLinePositions(); // refresh trade level lines to new DR width
            UpdatePreviewTradeLinesIfNonePending();

            // Keep Method 1 targets synced to the current DR dimensions
            UpdateMethod1TargetsForExtendedDr();
        }
        #endregion

        #region Trading Logic
        private bool TryCalculateMethod1Prices(out double entry, out double stop, out double tp)
        {
            entry = stop = tp = 0;

            if (currentDrDirection == 0)
                return false;

            double drHeight = currentDrHigh - currentDrLow;
            if (drHeight <= 0)
                return false;

            double slFrac = M1StopPercentOfDr / 100.0;
            double tpFrac = M1TpPercentOfDr / 100.0;

            if (currentDrDirection == 1)
            {
                // Bullish DR → long from the low
                entry = currentDrLow;
                stop = entry - drHeight * slFrac;          // SL below DR
                tp = currentDrLow + drHeight * tpFrac;     // TP inside DR from low up
            }
            else // currentDrDirection == -1
            {
                // Bearish DR → short from the high
                entry = currentDrHigh;
                stop = entry + drHeight * slFrac;          // SL above DR
                tp = currentDrHigh - drHeight * tpFrac;    // TP inside DR from high down
            }

            return true;
        }

        private void EvaluateMethod1Setup()
        {
            if (EntryMethod != DREntryMethod.Method1_PullbackLimit)
                return;

            if (!currentDrTradable || currentDrDirection == 0 || lastTradedDrId == drCounter)
            {
                DebugPrint(string.Format("M1 skip: tradable={0}, dir={1}, lastTradedDrId={2}, drId={3}",
                    currentDrTradable, currentDrDirection, lastTradedDrId, drCounter));
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                DebugPrint(string.Format("M1 skip: position not flat (pos={0})", Position.MarketPosition));
                return;
            }

            double entry, stop, tp;
            if (!TryCalculateMethod1Prices(out entry, out stop, out tp))
            {
                DebugPrint("M1 skip: failed to calculate prices");
                return;
            }

            double risk = Math.Abs(entry - stop);
            double reward = Math.Abs(tp - entry);
            double rr = risk > 0 ? reward / risk : 0;

            if (risk <= 0 || reward <= 0 || reward / risk < MinRiskReward)
            {
                DebugPrint(string.Format("M1 skip: risk/reward invalid (entry={0:F2}, stop={1:F2}, tp={2:F2}, risk={3:F2}, reward={4:F2}, rr={5:F2}, minRR={6:F2})",
                    entry, stop, tp, risk, reward, rr, MinRiskReward));
                return;
            }

            ClearPendingSignals(false, "prepare M1 pending", true);

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

            DebugPrint(string.Format("M1 pending {0}: entry={1:F2}, stop={2:F2}, tp={3:F2}, risk={4:F2}, reward={5:F2}, rr={6:F2}, drId={7}",
                currentDrDirection == 1 ? "LONG" : "SHORT",
                entry, stop, tp, risk, reward, rr, drCounter));

            UpdateTradeLevelLines(entry, stop, tp);
        }

        private void UpdateMethod1TargetsForExtendedDr()
        {
            // Only relevant when Method 1 is selected
            if (EntryMethod != DREntryMethod.Method1_PullbackLimit)
                return;

            // Need a valid, tradable DR for which Method 1 has been prepared
            if (!hasActiveDR || !currentDrTradable || currentDrDirection == 0)
                return;

            if (lastTradedDrId != drCounter)
                return; // no Method 1 trade planned for this DR

            double entry, stop, tp;
            if (!TryCalculateMethod1Prices(out entry, out stop, out tp))
                return;

            // Keep pending signal levels in sync with the DR size
            if (pendingLongSignal || pendingShortSignal)
            {
                pendingEntryPrice = entry;
                pendingStopPrice = stop;
                pendingTargetPrice = tp;
            }

            // Also update the managed targets for any current/future positions
            const string longSignalName = "DR_M1_Long";
            const string shortSignalName = "DR_M1_Short";

            if (currentDrDirection == 1)
            {
                SetStopLoss(longSignalName, CalculationMode.Price, stop, false);
                SetProfitTarget(longSignalName, CalculationMode.Price, tp);
            }
            else if (currentDrDirection == -1)
            {
                SetStopLoss(shortSignalName, CalculationMode.Price, stop, false);
                SetProfitTarget(shortSignalName, CalculationMode.Price, tp);
            }

            DebugPrint(string.Format(
                "M1 DR extended → updated prices: entry={0:F2}, stop={1:F2}, tp={2:F2}, drLow={3:F2}, drHigh={4:F2}",
                entry, stop, tp, currentDrLow, currentDrHigh));

            // Keep visual levels in sync
            UpdateTradeLevelLines(entry, stop, tp);
        }

        private void ProcessMethod2Signals()
        {
            if (EntryMethod != DREntryMethod.Method2_OppositeCandle)
                return;

            if (!hasActiveDR || !currentDrTradable || currentDrDirection == 0)
            {
                DebugPrint(string.Format("M2 skip: invalid DR state (hasActiveDR={0}, tradable={1}, dir={2}, drId={3})",
                    hasActiveDR, currentDrTradable, currentDrDirection, drCounter));
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat || lastTradedDrId == drCounter)
            {
                DebugPrint(string.Format("M2 skip: position not flat or already traded (pos={0}, lastTradedDrId={1}, drId={2})",
                    Position.MarketPosition, lastTradedDrId, drCounter));
                return;
            }

            double drHeight = currentDrHigh - currentDrLow;
            if (drHeight <= 0)
            {
                DebugPrint(string.Format("M2 skip: invalid DR height (drHeight={0:F2})", drHeight));
                return;
            }

            // -------------------------------------------------------------
            // Bullish DR (breakout up) -> long in direction of breakout
            // -------------------------------------------------------------
            if (currentDrDirection == 1)
            {
                // % measured from DR high down (0% = high, 100% = low)
                double touchLine = currentDrHigh - drHeight * (M2PriceTouchPercent / 100.0);

                // Step 2: price must reach the 88–100% discount zone
                if (!m2PriceTouched && Low[0] <= touchLine)
                {
                    m2PriceTouched = true;
                    DebugPrint(string.Format("M2 touch (bullish DR): touchLine={0:F2}, Low={1:F2}", touchLine, Low[0]));
                }

                // Step 3: after touch, wait for opposite candle (bullish) and enter on its close
                if (m2PriceTouched && Close[0] > Open[0])
                {
                    double entry = Close[0];
                    double stop  = currentDrLow;                                   // SL at low (100% line)
                    double tp    = currentDrHigh - drHeight * (M2TpPercentOfDr / 100.0); // TP around 50–62%

                    // Align to valid price increments to avoid broker rejections
                    entry = Instrument.MasterInstrument.RoundToTickSize(entry);
                    stop  = Instrument.MasterInstrument.RoundToTickSize(stop);
                    tp    = Instrument.MasterInstrument.RoundToTickSize(tp);

                    double risk   = entry - stop;
                    double reward = tp - entry;
                    double rr     = risk > 0 ? reward / risk : 0;

                    if (risk <= 0 || reward <= 0)
                    {
                        DebugPrint(string.Format("M2 skip (bullish): invalid risk/reward (entry={0:F2}, stop={1:F2}, tp={2:F2}, risk={3:F2}, reward={4:F2})",
                            entry, stop, tp, risk, reward));
                        return;
                    }

                    if (rr < MinRiskReward)
                    {
                        DebugPrint(string.Format("M2 skip (bullish): RR {0:F2} < min {1:F2} (entry={2:F2}, stop={3:F2}, tp={4:F2})",
                            rr, MinRiskReward, entry, stop, tp));
                        return;
                    }

                    if (risk > 0 && reward > 0 && rr >= MinRiskReward)
                    {
                        ClearPendingSignals(false, "prepare M2 pending long", true);
                        pendingLongSignal  = true;
                        pendingEntryPrice  = entry;
                        pendingStopPrice   = stop;
                        pendingTargetPrice = tp;
                        lastTradedDrId     = drCounter;
                        m2PriceTouched     = false;

                        DebugPrint(string.Format("M2 pending LONG: entry={0:F2}, stop={1:F2}, tp={2:F2}, risk={3:F2}, reward={4:F2}, rr={5:F2}, drId={6}",
                            entry, stop, tp, risk, reward, rr, drCounter));

                        UpdateTradeLevelLines(entry, stop, tp);
                    }
                }
            }
            // -------------------------------------------------------------
            // Bearish DR (breakout down) -> short in direction of breakout
            // -------------------------------------------------------------
            else if (currentDrDirection == -1)
            {
                // % measured from DR low up (0% = low, 100% = high)
                double touchLine = currentDrLow + drHeight * (M2PriceTouchPercent / 100.0);

                // Step 2: price must reach the 88–100% premium zone
                if (!m2PriceTouched && High[0] >= touchLine)
                {
                    m2PriceTouched = true;
                    DebugPrint(string.Format("M2 touch (bearish DR): touchLine={0:F2}, High={1:F2}", touchLine, High[0]));
                }

                // Step 3: after touch, wait for opposite candle (bearish) and enter on its close
                if (m2PriceTouched && Close[0] < Open[0])
                {
                    double entry = Close[0];
                    double stop  = currentDrHigh;                                  // SL at high (100% line)
                    double tp    = currentDrLow + drHeight * (M2TpPercentOfDr / 100.0);

                    // Align to valid price increments to avoid broker rejections
                    entry = Instrument.MasterInstrument.RoundToTickSize(entry);
                    stop  = Instrument.MasterInstrument.RoundToTickSize(stop);
                    tp    = Instrument.MasterInstrument.RoundToTickSize(tp);

                    double risk   = stop - entry;
                    double reward = entry - tp;
                    double rr     = risk > 0 ? reward / risk : 0;

                    if (risk <= 0 || reward <= 0)
                    {
                        DebugPrint(string.Format("M2 skip (bearish): invalid risk/reward (entry={0:F2}, stop={1:F2}, tp={2:F2}, risk={3:F2}, reward={4:F2})",
                            entry, stop, tp, risk, reward));
                        return;
                    }

                    if (rr < MinRiskReward)
                    {
                        DebugPrint(string.Format("M2 skip (bearish): RR {0:F2} < min {1:F2} (entry={2:F2}, stop={3:F2}, tp={4:F2})",
                            rr, MinRiskReward, entry, stop, tp));
                        return;
                    }

                    if (risk > 0 && reward > 0 && rr >= MinRiskReward)
                    {
                        ClearPendingSignals(false, "prepare M2 pending short", true);
                        pendingShortSignal = true;
                        pendingEntryPrice  = entry;
                        pendingStopPrice   = stop;
                        pendingTargetPrice = tp;
                        lastTradedDrId     = drCounter;
                        m2PriceTouched     = false;

                        DebugPrint(string.Format("M2 pending SHORT: entry={0:F2}, stop={1:F2}, tp={2:F2}, risk={3:F2}, reward={4:F2}, rr={5:F2}, drId={6}",
                            entry, stop, tp, risk, reward, rr, drCounter));

                        UpdateTradeLevelLines(entry, stop, tp);
                    }
                }
            }
        }

        private void SubmitPendingOrders()
        {
            if (!pendingLongSignal && !pendingShortSignal)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                DebugPrint(string.Format("SubmitPendingOrders skip: position not flat (pos={0})", Position.MarketPosition));
                return;
            }

            if (pendingEntryPrice <= 0 || pendingStopPrice <= 0 || pendingTargetPrice <= 0)
            {
                ClearPendingSignals(false, "pending prices invalid (<= 0)", true);
                return;
            }

            string longSignalName = EntryMethod == DREntryMethod.Method1_PullbackLimit ? "DR_M1_Long" : "DR_M2_Long";
            string shortSignalName = EntryMethod == DREntryMethod.Method1_PullbackLimit ? "DR_M1_Short" : "DR_M2_Short";

            // Extra safety: ensure prices are on valid ticks before sending orders
            pendingEntryPrice  = Instrument.MasterInstrument.RoundToTickSize(pendingEntryPrice);
            pendingStopPrice   = Instrument.MasterInstrument.RoundToTickSize(pendingStopPrice);
            pendingTargetPrice = Instrument.MasterInstrument.RoundToTickSize(pendingTargetPrice);

            bool useMarketOrdersForMethod2 = EntryMethod == DREntryMethod.Method2_OppositeCandle;
            string orderType = useMarketOrdersForMethod2 ? "MARKET" : "LIMIT";

            if (pendingLongSignal)
            {
                // Set targets before entry to avoid broker-side rejections
                SetStopLoss(longSignalName, CalculationMode.Price, pendingStopPrice, false);
                SetProfitTarget(longSignalName, CalculationMode.Price, pendingTargetPrice);

                DebugPrint(string.Format("Submitting LONG {0}: entry={1:F2}, stop={2:F2}, tp={3:F2}, drId={4}",
                    orderType, pendingEntryPrice, pendingStopPrice, pendingTargetPrice, drCounter));

                if (useMarketOrdersForMethod2)
                    EnterLong(DefaultQuantity, longSignalName);
                else
                    EnterLongLimit(0, true, DefaultQuantity, pendingEntryPrice, longSignalName);
            }
            else if (pendingShortSignal)
            {
                SetStopLoss(shortSignalName, CalculationMode.Price, pendingStopPrice, false);
                SetProfitTarget(shortSignalName, CalculationMode.Price, pendingTargetPrice);

                DebugPrint(string.Format("Submitting SHORT {0}: entry={1:F2}, stop={2:F2}, tp={3:F2}, drId={4}",
                    orderType, pendingEntryPrice, pendingStopPrice, pendingTargetPrice, drCounter));

                if (useMarketOrdersForMethod2)
                    EnterShort(DefaultQuantity, shortSignalName);
                else
                    EnterShortLimit(0, true, DefaultQuantity, pendingEntryPrice, shortSignalName);
            }

            ClearPendingSignals(false, "orders submitted");
        }

        private void ClearPendingSignals(bool resetTouch, string reason = null, bool clearLines = false)
        {
            if (!string.IsNullOrEmpty(reason))
                DebugPrint("Clearing pending signals: " + reason);

            pendingLongSignal = false;
            pendingShortSignal = false;
            pendingEntryPrice = 0;
            pendingStopPrice = 0;
            pendingTargetPrice = 0;

            if (resetTouch)
                m2PriceTouched = false;

            if (clearLines)
                RemoveTradeLevelLines();
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

        #region Trade Level Lines
        private void UpdateTradeLevelLines(double entry, double stop, double tp)
        {
            if (!hasActiveDR)
                return;

            tradeLineEntryPrice = entry;
            tradeLineStopPrice = stop;
            tradeLineTargetPrice = tp;
            hasTradeLevelLines = true;

            tradeEntryTag = string.Format("DR_{0}_EntryLine", drCounter);
            tradeStopTag = string.Format("DR_{0}_StopLine", drCounter);
            tradeTargetTag = string.Format("DR_{0}_TargetLine", drCounter);

            UpdateTradeLevelLinePositions();
        }

        private void UpdateTradeLevelLinePositions()
        {
            if (!hasTradeLevelLines || !hasActiveDR)
                return;

            int startBarsAgo = CurrentBar - currentDrStartBar;
            int endBarsAgo = CurrentBar - currentDrEndBar;

            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            Brush entryBrush = GetLineBrush(Brushes.RoyalBlue);
            Brush targetBrush = GetLineBrush(Brushes.LimeGreen);

            // Slightly transparent red for stops, and thicker lines for all levels
            SolidColorBrush stopBrush = new SolidColorBrush(Colors.Red) { Opacity = 0.6 };
            try
            {
                if (stopBrush.CanFreeze)
                    stopBrush.Freeze();
            }
            catch { }

            const int levelLineWidth = 2;
            const int stopLineWidth = 2;
            const DashStyleHelper levelDash = DashStyleHelper.DashDotDot;

            if (tradeLineEntryPrice > 0)
            {
                Draw.Line(
                    this,
                    tradeEntryTag,
                    false,
                    startBarsAgo,
                    tradeLineEntryPrice,
                    endBarsAgo,
                    tradeLineEntryPrice,
                    entryBrush,
                    levelDash,
                    levelLineWidth
                );
            }

            if (tradeLineStopPrice > 0)
            {
                Draw.Line(
                    this,
                    tradeStopTag,
                    false,
                    startBarsAgo,
                    tradeLineStopPrice,
                    endBarsAgo,
                    tradeLineStopPrice,
                    stopBrush,
                    levelDash,
                    stopLineWidth
                );
            }

            if (tradeLineTargetPrice > 0)
            {
                Draw.Line(
                    this,
                    tradeTargetTag,
                    false,
                    startBarsAgo,
                    tradeLineTargetPrice,
                    endBarsAgo,
                    tradeLineTargetPrice,
                    targetBrush,
                    levelDash,
                    levelLineWidth
                );
            }
        }

        private void RemoveTradeLevelLines()
        {
            if (!hasTradeLevelLines)
                return;

            if (!string.IsNullOrEmpty(tradeEntryTag))
                RemoveDrawObject(tradeEntryTag);
            if (!string.IsNullOrEmpty(tradeStopTag))
                RemoveDrawObject(tradeStopTag);
            if (!string.IsNullOrEmpty(tradeTargetTag))
                RemoveDrawObject(tradeTargetTag);

            hasTradeLevelLines = false;
            tradeLineEntryPrice = tradeLineStopPrice = tradeLineTargetPrice = 0;
            tradeEntryTag = tradeStopTag = tradeTargetTag = string.Empty;
        }

        private void UpdatePreviewTradeLinesIfNonePending()
        {
            if (pendingLongSignal || pendingShortSignal)
                return;

            if (!hasActiveDR || !currentDrTradable || currentDrDirection == 0)
                return;

            double drHeight = currentDrHigh - currentDrLow;
            if (drHeight <= 0)
                return;

            double entry = 0, stop = 0, tp = 0;
            bool hasLevels = false;

            if (EntryMethod == DREntryMethod.Method1_PullbackLimit)
            {
                double e, s, t;
                if (!TryCalculateMethod1Prices(out e, out s, out t))
                    return;

                double risk = Math.Abs(e - s);
                double reward = Math.Abs(t - e);
                double rr = risk > 0 ? reward / risk : 0;
                if (risk <= 0 || reward <= 0 || rr < MinRiskReward)
                    return;

                entry = Instrument.MasterInstrument.RoundToTickSize(e);
                stop  = Instrument.MasterInstrument.RoundToTickSize(s);
                tp    = Instrument.MasterInstrument.RoundToTickSize(t);
                hasLevels = true;
            }
            else if (EntryMethod == DREntryMethod.Method2_OppositeCandle)
            {
                if (currentDrDirection == 1)
                {
                    // Use touch line as a visual proxy for the future entry (actual entry is the close of the bullish candle)
                    entry = currentDrHigh - drHeight * (M2PriceTouchPercent / 100.0);
                    stop  = currentDrLow;
                    tp    = currentDrHigh - drHeight * (M2TpPercentOfDr / 100.0);
                }
                else if (currentDrDirection == -1)
                {
                    entry = currentDrLow + drHeight * (M2PriceTouchPercent / 100.0);
                    stop  = currentDrHigh;
                    tp    = currentDrLow + drHeight * (M2TpPercentOfDr / 100.0);
                }

                entry = Instrument.MasterInstrument.RoundToTickSize(entry);
                stop  = Instrument.MasterInstrument.RoundToTickSize(stop);
                tp    = Instrument.MasterInstrument.RoundToTickSize(tp);

                double risk = Math.Abs(entry - stop);
                double reward = Math.Abs(tp - entry);
                double rr = risk > 0 ? reward / risk : 0;
                if (risk <= 0 || reward <= 0 || rr < MinRiskReward)
                    return;

                hasLevels = true;
            }

            if (hasLevels)
                UpdateTradeLevelLines(entry, stop, tp);
        }
        #endregion

        #region Order/Execution Logging
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (!DebugLogging || order == null)
                return;

            string msg = string.Format("OrderUpdate: name={0}, fromEntry={1}, action={2}, type={3}, state={4}, qty={5}, filled={6}, avgFill={7:F2}, limit={8:F2}, stop={9:F2}, oco={10}",
                order.Name, order.FromEntrySignal, order.OrderAction, order.OrderType, orderState, quantity, filled, averageFillPrice, limitPrice, stopPrice, order.Oco);

            if (error != ErrorCode.NoError || !string.IsNullOrWhiteSpace(nativeError))
                msg += string.Format(", error={0}, native={1}", error, nativeError);

            DebugPrint(msg);
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (!DebugLogging || execution == null || execution.Order == null)
                return;

            string orderName = execution.Order.Name;
            string fromEntry = execution.Order.FromEntrySignal;
            string exitReason = string.Empty;

            if (!string.IsNullOrEmpty(orderName))
            {
                if (orderName.IndexOf("profit target", StringComparison.OrdinalIgnoreCase) >= 0)
                    exitReason = "TP filled";
                else if (orderName.IndexOf("stop loss", StringComparison.OrdinalIgnoreCase) >= 0)
                    exitReason = "SL filled";
            }

            int filledQty = execution.Order != null ? execution.Order.Filled : 0;

            DebugPrint(string.Format("Execution: order={0}, fromEntry={1}, action={2}, type={3}, qty={4}, price={5:F2}, pos={6}, filled={7}, avgFill={8:F2}{9}",
                orderName,
                fromEntry,
                execution.Order.OrderAction,
                execution.Order.OrderType,
                quantity,
                price,
                marketPosition,
                filledQty,
                execution.Order.AverageFillPrice,
                string.IsNullOrEmpty(exitReason) ? string.Empty : ", reason=" + exitReason));
        }
        #endregion

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
