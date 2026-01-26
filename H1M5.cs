#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class H1M5 : Strategy
    {
        #region User Inputs
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Leg Swing Lookback", GroupName = "01. DR Parameters", Order = 0)]
        public int LegSwingLookback { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Swing Strength", GroupName = "01. DR Parameters", Order = 1)]
        public int SwingStrength { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min DR Size (points)", GroupName = "01. DR Parameters", Order = 2)]
        public double MinDrSizePoints { get; set; }

        [XmlIgnore]
        [Display(Name = "DR Box Brush", Description = "Fill color for DR boxes", GroupName = "02. Visual Settings", Order = 0)]
        public Brush DrBoxBrush { get; set; }

        [Browsable(false)]
        public string DrBoxBrushSerializable
        {
            get { return Serialize.BrushToString(DrBoxBrush); }
            set { DrBoxBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "DR Outline Brush", Description = "Outline color for DR boxes", GroupName = "02. Visual Settings", Order = 1)]
        public Brush DrOutlineBrush { get; set; }

        [Browsable(false)]
        public string DrOutlineBrushSerializable
        {
            get { return Serialize.BrushToString(DrOutlineBrush); }
            set { DrOutlineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "DR Mid-Line Brush", Description = "Color for mid-line", GroupName = "02. Visual Settings", Order = 2)]
        public Brush DrMidLineBrush { get; set; }

        [Browsable(false)]
        public string DrMidLineBrushSerializable
        {
            get { return Serialize.BrushToString(DrMidLineBrush); }
            set { DrMidLineBrush = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "Box Opacity", Description = "Transparency of DR box fill (0-100)", GroupName = "02. Visual Settings", Order = 3)]
        public int BoxOpacity { get; set; }

        [Range(1, 5)]
        [Display(Name = "Line Width", Description = "Width of mid-line", GroupName = "02. Visual Settings", Order = 4)]
        public int LineWidth { get; set; }

        [Range(0, 100)]
        [Display(Name = "Line Opacity", Description = "Transparency of DR lines (0-100)", GroupName = "02. Visual Settings", Order = 5)]
        public int LineOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Debug Logging", Description = "Enable detailed debug logging to Output window", GroupName = "03. Debug", Order = 0)]
        public bool DebugLogging { get; set; }
        #endregion

        #region State Variables
        private double currentDrHigh;
        private double currentDrLow;
        private double currentDrMid;
        private int currentDrStartBar;
        private int currentDrEndBar;
        private string currentDrBoxTag;
        private string currentDrMidLineTag;
        private string currentDrTopLineTag;
        private string currentDrBottomLineTag;
        private int lastBreakoutDirection;
        private bool inBreakoutStrike;
        private int drCounter;
        private bool hasActiveDR;
        private bool currentDrTradable;
        #endregion

        #region NinjaScript Lifecycle
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "H1/M5 DR visualizer (draws dealing ranges only).";
                Name = "H1M5";
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

                LegSwingLookback = 10;
                SwingStrength = 1;
                MinDrSizePoints = 4;
                BoxOpacity = 10;
                LineWidth = 1;
                LineOpacity = 30;
                DebugLogging = false;

                DrBoxBrush = Brushes.DodgerBlue;
                DrOutlineBrush = Brushes.DodgerBlue;
                DrMidLineBrush = Brushes.White;
            }
            else if (State == State.Configure)
            {
                // Add the lower timeframe series (M5) for future intrabar logic.
                AddDataSeries(BarsPeriodType.Minute, 5);
            }
            else if (State == State.DataLoaded)
            {
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
                currentDrTradable = true;

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

                bool insideDR = Close[0] >= currentDrLow && Close[0] <= currentDrHigh;
                if (insideDR)
                {
                    ExtendCurrentDR();
                }
                else
                {
                    bool bullishBreakout = Close[0] > currentDrHigh;
                    bool bearishBreakout = Close[0] < currentDrLow;

                    int currentBreakoutDirection = 0;
                    if (bullishBreakout)
                        currentBreakoutDirection = 1;
                    else if (bearishBreakout)
                        currentBreakoutDirection = -1;

                    if (currentBreakoutDirection != 0)
                    {
                        bool isContinuation = currentBreakoutDirection == lastBreakoutDirection && inBreakoutStrike;
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
            }
            else if (BarsInProgress == 1)
            {
                // Reserved for future M5 intrabar logic (SampleIntrabarBacktest style).
                return;
            }
        }
        #endregion

        #region DR Creation Logic
        private void TryCreateInitialDR()
        {
            double swingHigh = double.MinValue;
            double swingLow = double.MaxValue;
            int swingHighBar = -1;
            int swingLowBar = -1;

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

            if (swingHighBar < 0 || swingLowBar < 0)
            {
                DebugPrint("Cannot create initial DR - missing swing high or swing low");
                return;
            }

            int startBar = Math.Min(swingHighBar, swingLowBar);
            DebugPrint(string.Format("Creating INITIAL DR from swings. StartBar={0}", startBar));
            CreateNewDR(swingLow, swingHigh, startBar);
            lastBreakoutDirection = 0;
            inBreakoutStrike = false;
        }

        private void CreateNewDRFromBullishBreakout(bool isContinuation)
        {
            DebugPrint(string.Format("Starting bullish breakout DR creation. Looking back {0} bars.", LegSwingLookback));

            double epsilon = TickSize * 0.5;
            double legLow = double.NaN;
            int legLowIndex = -1;

            for (int i = 1; i <= Math.Min(LegSwingLookback, CurrentBar); i++)
            {
                if (IsSwingLow(i))
                {
                    legLow = Low[i];
                    legLowIndex = i;
                    break;
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

            if (isContinuation && !isNewInternalLegLow)
            {
                DebugPrint("Closest swing LOW is at or below current DR low (continuation). Extending current DR with breakout wick high.");
                currentDrHigh = Math.Max(currentDrHigh, High[0]);
                currentDrMid = (currentDrHigh + currentDrLow) / 2.0;
                ExtendCurrentDR();
                return;
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
            DebugPrint(string.Format("Starting bearish breakout DR creation. Looking back {0} bars.", LegSwingLookback));

            double epsilon = TickSize * 0.5;
            double legHigh = double.NaN;
            int legHighIndex = -1;

            for (int i = 1; i <= Math.Min(LegSwingLookback, CurrentBar); i++)
            {
                if (IsSwingHigh(i))
                {
                    legHigh = High[i];
                    legHighIndex = i;
                    break;
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

            if (isContinuation && !isNewInternalLegHigh)
            {
                DebugPrint("Closest swing HIGH is at or above current DR high (continuation). Extending current DR with breakout wick low.");
                currentDrLow = Math.Min(currentDrLow, Low[0]);
                currentDrMid = (currentDrHigh + currentDrLow) / 2.0;
                ExtendCurrentDR();
                return;
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
            if (drHigh <= drLow)
            {
                DebugPrint(string.Format("ERROR: Cannot create DR - invalid dimensions! High={0:F2} <= Low={1:F2}", drHigh, drLow));
                return;
            }

            currentDrLow = drLow;
            currentDrHigh = drHigh;
            currentDrMid = (drHigh + drLow) / 2.0;
            currentDrStartBar = startBar;
            currentDrEndBar = CurrentBar;

            drCounter++;
            currentDrBoxTag = "DRBox_" + drCounter;
            currentDrMidLineTag = "DRMid_" + drCounter;
            currentDrTopLineTag = "DRTop_" + drCounter;
            currentDrBottomLineTag = "DRBot_" + drCounter;

            hasActiveDR = true;

            double drHeight = drHigh - drLow;
            double drSizePoints = drHeight / TickSize;
            currentDrTradable = drSizePoints >= MinDrSizePoints;

            DebugPrint(string.Empty);
            DebugPrint(string.Format("=== DR #{0} CREATED === Low={1:F2}, High={2:F2}, Mid={3:F2}, Height={4:F2}, StartBar={5}, Tags: {6}, {7}",
                drCounter, currentDrLow, currentDrHigh, currentDrMid, drHeight, currentDrStartBar, currentDrBoxTag, currentDrMidLineTag));
            DebugPrint(string.Format("DR #{0} tradable={1} (height={2:F2}, sizePts={3:F2}, minRequired={4:F2})",
                drCounter, currentDrTradable, drHeight, drSizePoints, MinDrSizePoints));

            DrawCurrentDR();
        }

        private void ExtendCurrentDR()
        {
            currentDrEndBar = CurrentBar;
            double drHeight = currentDrHigh - currentDrLow;
            double drSizePoints = drHeight / TickSize;
            currentDrTradable = drSizePoints >= MinDrSizePoints;
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

        #region Drawing
        private void DrawCurrentDR()
        {
            if (!hasActiveDR)
                return;

            int startBarsAgo = CurrentBar - currentDrStartBar;
            int endBarsAgo = CurrentBar - currentDrEndBar;

            if (startBarsAgo < 0)
                startBarsAgo = 0;
            if (endBarsAgo < 0)
                endBarsAgo = 0;

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
                return Brushes.Transparent;
            }
        }
        #endregion

        private void DebugPrint(string message)
        {
            if (DebugLogging && !string.IsNullOrEmpty(message))
                Print(message);
        }
    }
}
