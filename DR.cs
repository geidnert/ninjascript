#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class DR : Strategy
    {
        #region User Inputs
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

        // DR counter for unique tags
        private int drCounter;

        // Flag to track if we have an active DR
        private bool hasActiveDR;
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
            // Wait for enough bars for swing detection
            if (CurrentBar < SwingStrength * 2 + LegSwingLookback)
                return;

            // If we don't have an active DR, try to create the initial one
            if (!hasActiveDR)
            {
                DebugPrint(string.Format("No active DR. Attempting to create initial DR. Close={0:F2}", Close[0]));
                TryCreateInitialDR();
                return;
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

                int currentBreakoutDirection = 0;
                if (bullishBreakout)
                    currentBreakoutDirection = 1;
                else if (bearishBreakout)
                    currentBreakoutDirection = -1;

                if (currentBreakoutDirection != 0)
                {
                    bool isContinuation = currentBreakoutDirection == lastBreakoutDirection;

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

                    lastBreakoutDirection = currentBreakoutDirection;
                }
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
            DebugPrint(string.Format("\n\n=== DR #{0} CREATED === Low={1:F2}, High={2:F2}, Mid={3:F2}, Height={4:F2}, StartBar={5}, Tags: {6}, {7}",
                drCounter, currentDrLow, currentDrHigh, currentDrMid, drHeight, currentDrStartBar, currentDrBoxTag, currentDrMidLineTag));

            // Draw the new DR
            DrawCurrentDR();
        }

        private void ExtendCurrentDR()
        {
            // Update the end bar to current bar
            currentDrEndBar = CurrentBar;

            // Redraw to extend to the right
            DrawCurrentDR();
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

            Print(string.Format("[{0:yyyy-MM-dd HH:mm:ss}] DR: {1}", Time[0], message));
        }
        #endregion
    }
}
