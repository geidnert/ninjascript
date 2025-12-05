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
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Leg Swing Lookback", Description = "Maximum bars to look back for leg low/high around breakout", GroupName = "01. DR Parameters", Order = 1)]
        public int LegSwingLookback { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Swing Strength", Description = "Bars on each side for swing high/low detection", GroupName = "01. DR Parameters", Order = 2)]
        public int SwingStrength { get; set; }

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
                SwingStrength = 2;
                BoxOpacity = 15;
                LineWidth = 1;
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
                DebugPrint(string.Format("Price inside DR range. Extending DR box. Close={0:F2}, DR Low={1:F2}, DR High={2:F2}",
                    Close[0], currentDrLow, currentDrHigh));
                ExtendCurrentDR();
            }
            else
            {
                // Breakout detected - finalize current DR and create new one
                bool bullishBreakout = Close[0] > currentDrHigh;
                bool bearishBreakout = Close[0] < currentDrLow;

                if (bullishBreakout)
                {
                    DebugPrint(string.Format("BULLISH BREAKOUT detected! Close={0:F2} > DR High={1:F2}", Close[0], currentDrHigh));
                    CreateNewDRFromBullishBreakout();
                }
                else if (bearishBreakout)
                {
                    DebugPrint(string.Format("BEARISH BREAKOUT detected! Close={0:F2} < DR Low={1:F2}", Close[0], currentDrLow));
                    CreateNewDRFromBearishBreakout();
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

        private void CreateNewDRFromBullishBreakout()
        {
            // Bullish breakout: Close[0] > currentDrHigh
            // Find the leg low that led to this breakout

            DebugPrint(string.Format("Starting bullish breakout DR creation. Looking back {0} bars.", LegSwingLookback));

            // Step 1: Find the leg low within the last LegSwingLookback bars
            double legLow = double.MaxValue;
            int legLowIndex = 0;

            for (int i = 0; i <= Math.Min(LegSwingLookback, CurrentBar); i++)
            {
                if (Low[i] < legLow)
                {
                    legLow = Low[i];
                    legLowIndex = i;
                }
            }

            double newDrLow = legLow;
            int lowBarIndex = CurrentBar - legLowIndex;

            DebugPrint(string.Format("Found leg LOW: price={0:F2}, at {1} bars ago (bar index {2})",
                newDrLow, legLowIndex, lowBarIndex));

            // Step 2: Find the first swing high between legLowIndex and current bar
            double newDrHigh = double.MinValue;
            bool foundSwingHigh = false;

            for (int i = legLowIndex; i >= SwingStrength; i--)
            {
                if (IsSwingHigh(i))
                {
                    newDrHigh = High[i];
                    foundSwingHigh = true;
                    DebugPrint(string.Format("Found swing HIGH in leg: price={0:F2}, at {1} bars ago", newDrHigh, i));
                    break;
                }
            }

            // If no swing high found, use the highest high in the range
            if (!foundSwingHigh)
            {
                for (int i = legLowIndex; i >= 0; i--)
                {
                    if (High[i] > newDrHigh)
                        newDrHigh = High[i];
                }
                DebugPrint(string.Format("No swing high found, using highest HIGH in range: price={0:F2}", newDrHigh));
            }

            // Create the new DR
            DebugPrint(string.Format("Creating NEW DR from bullish breakout. Low={0:F2}, High={1:F2}, StartBar={2}",
                newDrLow, newDrHigh, lowBarIndex));
            CreateNewDR(newDrLow, newDrHigh, lowBarIndex);
        }

        private void CreateNewDRFromBearishBreakout()
        {
            // Bearish breakout: Close[0] < currentDrLow
            // Find the leg high that led to this breakout

            DebugPrint(string.Format("Starting bearish breakout DR creation. Looking back {0} bars.", LegSwingLookback));

            // Step 1: Find the leg high within the last LegSwingLookback bars
            double legHigh = double.MinValue;
            int legHighIndex = 0;

            for (int i = 0; i <= Math.Min(LegSwingLookback, CurrentBar); i++)
            {
                if (High[i] > legHigh)
                {
                    legHigh = High[i];
                    legHighIndex = i;
                }
            }

            double newDrHigh = legHigh;
            int highBarIndex = CurrentBar - legHighIndex;

            DebugPrint(string.Format("Found leg HIGH: price={0:F2}, at {1} bars ago (bar index {2})",
                newDrHigh, legHighIndex, highBarIndex));

            // Step 2: Find the first swing low between legHighIndex and current bar
            double newDrLow = double.MaxValue;
            bool foundSwingLow = false;

            for (int i = legHighIndex; i >= SwingStrength; i--)
            {
                if (IsSwingLow(i))
                {
                    newDrLow = Low[i];
                    foundSwingLow = true;
                    DebugPrint(string.Format("Found swing LOW in leg: price={0:F2}, at {1} bars ago", newDrLow, i));
                    break;
                }
            }

            // If no swing low found, use the lowest low in the range
            if (!foundSwingLow)
            {
                for (int i = legHighIndex; i >= 0; i--)
                {
                    if (Low[i] < newDrLow)
                        newDrLow = Low[i];
                }
                DebugPrint(string.Format("No swing low found, using lowest LOW in range: price={0:F2}", newDrLow));
            }

            // Create the new DR
            DebugPrint(string.Format("Creating NEW DR from bearish breakout. Low={0:F2}, High={1:F2}, StartBar={2}",
                newDrLow, newDrHigh, highBarIndex));
            CreateNewDR(newDrLow, newDrHigh, highBarIndex);
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
            DebugPrint(string.Format("=== DR #{0} CREATED === Low={1:F2}, High={2:F2}, Mid={3:F2}, Height={4:F2}, StartBar={5}, Tags: {6}, {7}",
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
            Draw.Line(
                this,
                currentDrMidLineTag,
                false,
                startBarsAgo,
                currentDrMid,
                endBarsAgo,
                currentDrMid,
                DrMidLineBrush,
                DashStyleHelper.Solid,
                LineWidth
            );

            // Draw top and bottom borders only
            Draw.Line(
                this,
                currentDrTopLineTag,
                false,
                startBarsAgo,
                currentDrHigh,
                endBarsAgo,
                currentDrHigh,
                DrOutlineBrush,
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
                DrOutlineBrush,
                DashStyleHelper.Solid,
                LineWidth
            );
        }
        #endregion

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
