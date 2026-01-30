#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class Sweeper : Strategy
    {
        public Sweeper()
        {
            VendorLicense(1005);
        }
       
        #region User Inputs
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Leg Swing Lookback", Description = "Max bars to look back for the internal swing that defines new DRs on breakouts.", GroupName = "01. DR Parameters", Order = 0)]
        public int LegSwingLookback { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Swing Strength", Description = "Bars on each side required for a swing high/low in DR detection.", GroupName = "01. DR Parameters", Order = 1)]
        public int SwingStrength { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min DR Size (points)", Description = "Minimum DR height in points required to consider it tradable.", GroupName = "01. DR Parameters", Order = 2)]
        public double MinDrSizePoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 50.0)]
        [Display(Name = "Mid Red Zone % (each side)", Description = "Percent of DR height to mark above and below the midline as the red no-trade zone.", GroupName = "01. DR Parameters", Order = 3)]
        public double MidRedZonePercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DR Bars Period Type", Description = "Timeframe unit for DR calculation (e.g., Minute).", GroupName = "01. DR Parameters", Order = 4)]
        public BarsPeriodType DrBarsPeriodType { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "DR Bars Period Value", Description = "Timeframe value for DR calculation (e.g., 60 for 1H when Period Type = Minute).",GroupName = "01. DR Parameters", Order = 5)]
        public int DrBarsPeriodValue { get; set; }

        [XmlIgnore]
        [Display(Name = "DR Box Brush", Description = "Fill color for DR boxes.", GroupName = "02. Visual Settings", Order = 0)]
        public Brush DrBoxBrush { get; set; }

        [Browsable(false)]
        public string DrBoxBrushSerializable
        {
            get { return Serialize.BrushToString(DrBoxBrush); }
            set { DrBoxBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "DR Outline Brush", Description = "Outline color for DR boxes.", GroupName = "02. Visual Settings", Order = 1)]
        public Brush DrOutlineBrush { get; set; }

        [Browsable(false)]
        public string DrOutlineBrushSerializable
        {
            get { return Serialize.BrushToString(DrOutlineBrush); }
            set { DrOutlineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "DR Mid-Line Brush", Description = "Color for the DR midline.", GroupName = "02. Visual Settings", Order = 2)]
        public Brush DrMidLineBrush { get; set; }

        [Browsable(false)]
        public string DrMidLineBrushSerializable
        {
            get { return Serialize.BrushToString(DrMidLineBrush); }
            set { DrMidLineBrush = Serialize.StringToBrush(value); }
        }

        [Range(0, 100)]
        [Display(Name = "Box Opacity", Description = "Transparency of DR box fill (0-100).", GroupName = "02. Visual Settings", Order = 3)]
        public int BoxOpacity { get; set; }

        [Range(1, 5)]
        [Display(Name = "Line Width", Description = "Width of DR lines.", GroupName = "02. Visual Settings", Order = 4)]
        public int LineWidth { get; set; }

        [Range(0, 100)]
        [Display(Name = "Line Opacity", Description = "Transparency of DR lines (0-100).", GroupName = "02. Visual Settings", Order = 5)]
        public int LineOpacity { get; set; }

        [XmlIgnore]
        [Display(Name = "Sweep Line Brush", Description = "Color for swept 1H level lines.", GroupName = "02. Visual Settings", Order = 7)]
        public Brush SweepLineBrush { get; set; }

        [Browsable(false)]
        public string SweepLineBrushSerializable
        {
            get { return Serialize.BrushToString(SweepLineBrush); }
            set { SweepLineBrush = Serialize.StringToBrush(value); }
        }

        [Range(1, 5)]
        [Display(Name = "Sweep Line Width", Description = "Width of swept 1H level lines.", GroupName = "02. Visual Settings", Order = 8)]
        public int SweepLineWidth { get; set; }

        [XmlIgnore]
        [Display(Name = "Swing Line Brush", Description = "Color for swing high/low liquidity lines.", GroupName = "02. Visual Settings", Order = 9)]
        public Brush SwingLineBrush { get; set; }

        [Browsable(false)]
        public string SwingLineBrushSerializable
        {
            get { return Serialize.BrushToString(SwingLineBrush); }
            set { SwingLineBrush = Serialize.StringToBrush(value); }
        }

        [Range(1, 1000)]
        [Display(Name = "Swing Draw Bars", Description = "Number of DR bars to keep swing lines visible.", GroupName = "02. Visual Settings", Order = 10)]
        public int SwingDrawBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Logging", Description = "Enable detailed logging to the Output window for debugging.", GroupName = "03. Debug", Order = 0)]
        public bool DebugLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Historical DRs", Description = "If false, only show the current active DR.", GroupName = "02. Visual Settings", Order = 6)]
        public bool ShowHistoricalDRs { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "Session start time (chart time).", GroupName = "06. Session Time", Order = 0)]
        public TimeSpan SessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "Session end time (chart time).", GroupName = "06. Session Time", Order = 1)]
        public TimeSpan SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", Description = "No new entries after this time until session end (existing trades may still run).", GroupName = "06. Session Time", Order = 2)]
        public TimeSpan NoTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close At Session End", Description = "If true, flatten open positions and cancel orders at session end.", GroupName = "06. Session Time", Order = 3)]
        public bool CloseAtSessionEnd { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Session Fill", Description = "Background color used to highlight the session window.", GroupName = "06. Session Time", Order = 4)]
        public Brush SessionBrush { get; set; }

        [Browsable(false)]
        public string SessionBrushSerializable
        {
            get { return Serialize.BrushToString(SessionBrush); }
            set { SessionBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start", Description = "Start of skip window (chart time).", GroupName = "07. Skip Time", Order = 0)]
        public TimeSpan SkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End", Description = "End of skip window (chart time).", GroupName = "07. Skip Time", Order = 1)]
        public TimeSpan SkipEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Force Close at Skip Start", Description = "If true, flatten and cancel as soon as skip window begins.", GroupName = "07. Skip Time", Order = 2)]
        public bool ForceCloseAtSkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip Filter", Description = "Skip trading during listed news windows.", GroupName = "07. Skip Time",Order = 3)]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Skip Minutes Before", Description = "Minutes before the news time to start skipping.", GroupName = "07. Skip Time", Order = 4)]
        public int NewsSkipMinutesBefore { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Skip Minutes After", Description = "Minutes after the news time to end skipping.", GroupName = "07. Skip Time", Order = 5)]
        public int NewsSkipMinutesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Invalidated FVGs", Description = "If false, remove FVGs once invalidated.", GroupName = "04. FVG", Order = 0)]
        public bool ShowInvalidatedFvgs { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min FVG Size (points)", Description = "Minimum FVG size in points to draw/track.", GroupName = "04. FVG", Order = 1)]
        public double MinFvgSizePoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max FVG Size (points)", Description = "Maximum FVG size in points to draw/track (0 disables).", GroupName = "04. FVG", Order = 2)]
        public double MaxFvgSizePoints { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "FVG Opacity", Description = "Opacity of active FVG rectangles (0-100).", GroupName = "04. FVG", Order = 3)]
        public int FvgOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Invalidated FVG Opacity", Description = "Opacity of invalidated FVG rectangles (0-100).", GroupName = "04. FVG", Order = 4)]
        public int InvalidatedFvgOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 365)]
        [Display(Name = "FVG Draw Limit (days)", Description = "Number of days of FVGs to keep on chart.", GroupName = "04. FVG", Order = 5)]
        public int FvgDrawLimitDays { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Only Show FVGs Inside DR", Description = "If true, only draw FVGs fully within the active DR range.", GroupName = "04. FVG", Order = 6)]
        public bool OnlyShowFvgsInsideDr { get; set; }

        [XmlIgnore]
        [Display(Name = "FVG Fill Brush", Description = "Fill color for active FVG rectangles.", GroupName = "04. FVG",Order = 7)]
        public Brush FvgFillBrush { get; set; }

        [Browsable(false)]
        public string FvgFillBrushSerializable
        {
            get { return Serialize.BrushToString(FvgFillBrush); }
            set { FvgFillBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Invalidated FVG Fill Brush", Description = "Fill color for invalidated FVG rectangles.", GroupName = "04. FVG", Order = 8)]
        public Brush InvalidatedFvgFillBrush { get; set; }

        [Browsable(false)]
        public string InvalidatedFvgFillBrushSerializable
        {
            get { return Serialize.BrushToString(InvalidatedFvgFillBrush); }
            set { InvalidatedFvgFillBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "Sweep Lookback (bars)", Description = "Lookback window on DR timeframe for unswept levels.", GroupName = "05. Entries", Order = 0)]
        public int SweepLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % of DR", Description = "Take-profit level as a percent of DR height (directional).", GroupName = "05. Entries", Order = 1)]
        public double TpPercentOfDr { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Trailing Stop", Description = "If true, trail SL after price touches DR midline (2-bar trailing).", GroupName = "05. Entries", Order = 2)]
        public bool EnableTrailingStop { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit On EMA Flat", Description = "If true, flatten when EMA slope crosses threshold (long: <= -threshold, short: >= +threshold).", GroupName = "05. Entries", Order = 3)]
        public bool ExitOnEmaFlat { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "EMA Period", Description = "EMA period used for flat-slope exit and chart plot.", GroupName = "05. Entries", Order = 4)]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "EMA Slope Threshold (points)", Description = "Minimum EMA slope magnitude to trigger exit. 0 = any non-positive/ non-negative slope.", GroupName = "05. Entries", Order = 5)]
        public double EmaSlopeThreshold { get; set; }

        public enum EntryMode
        {
            Fvg,
            Duo
        }

        public enum SweepConfirmMode
        {
            RequireClose,
            SkipClose
        }

        public enum SweepSource
        {
            CandleHighLow,
            SwingHighLow
        }

        [NinjaScriptProperty]
        [Display(Name = "Entry Mode", Description = "Select entry trigger: FVG or Duo (2-candle).",GroupName = "05. Entries", Order = 6)]
        public EntryMode EntryModeSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sweep Confirmation", Description = "Require 1H close back through swept level before arming setup.", GroupName = "05. Entries", Order = 7)]
        public SweepConfirmMode SweepConfirmModeSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Sweep Source", Description = "Choose candle high/low sweeps or swing high/low sweeps.", GroupName = "05. Entries", Order = 7)]
        public SweepSource SweepSourceSetting { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Sweep Swing Strength", Description = "Bars on each side required for swing high/low used by sweep.", GroupName = "05. Entries", Order = 7)]
        public int SweepSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Reverse Swing Strength (5m)", Description = "Swing strength for reverse SL using recent 5m swing high/low.", GroupName = "05. Entries", Order = 7)]
        public int ReverseSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reverse On Opposite FVG", Description = "If true, reverse position when an opposite-direction 5m FVG prints.", GroupName = "05. Entries", Order = 8)]
        public bool ReverseOnOppositeFvg { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Limit Entries", Description = "If true, place limit entries at the FVG edge (or % within the FVG) instead of market.", GroupName = "05. Entries", Order = 9)]
        public bool UseLimitEntries { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Entry %", Description = "FVG: 0% = near edge (short: lower, long: upper), 100% = far edge. Duo: % into combined 2-candle body.", GroupName = "05. Entries", Order = 10)]
        public double LimitEntryPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Duo Min Combined Body (points)", Description = "Minimum combined body size of 2 same-direction 5m candles.", GroupName = "05. Entries", Order = 11)]
        public double DuoMinCombinedBodyPoints { get; set; }
        #endregion

        #region State Variables
        private class FvgBox
        {
            public string Tag;
            public int StartBarIndex;
            public int EndBarIndex;
            public double Upper;
            public double Lower;
            public bool IsBullish;
            public bool IsActive;
            public DateTime SessionDate;
        }

        private class SweepLine
        {
            public string Tag;
            public int StartBarIndex;
            public int EndBarIndex;
            public DateTime StartTime;
            public DateTime EndTime;
            public double Price;
            public bool IsActive;
            public bool IsHigh;
        }

        private class SwingLine
        {
            public string Tag;
            public int StartBarIndex;
            public DateTime StartTime;
            public DateTime EndTime;
            public double Price;
            public bool IsActive;
            public bool IsHigh;
        }

        private enum SetupDirection
        {
            Long,
            Short
        }

        private double currentDrHigh;
        private double currentDrLow;
        private double currentDrMid;
        private int currentDrStartBar;
        private int currentDrEndBar;
        private DateTime currentDrStartTime;
        private DateTime currentDrEndTime;
        private string currentDrBoxTag;
        private string currentDrRedZoneTag;
        private string currentDrMidLineTag;
        private string currentDrTopLineTag;
        private string currentDrBottomLineTag;
        private int lastBreakoutDirection;
        private bool inBreakoutStrike;
        private int drCounter;
        private bool hasActiveDR;
        private bool currentDrTradable;
        private int drSeriesIndex;

        private List<FvgBox> activeFvgs;
        private List<SweepLine> sweepLines;
        private List<SwingLine> swingLines;
        private int sweepLineCounter;
        private bool pendingSweepValid;
        private int pendingSweepStartBarIndex;
        private DateTime pendingSweepStartTime;
        private double pendingSweepPrice;
        private bool pendingSweepIsHigh;
        private bool pendingSweepDrawRequested;
        private DateTime pendingSweepFillTime;
        private int fvgCounter;

        private bool sweepSetupActive;
        private SetupDirection sweepSetupDirection;
        private DateTime sweepSetupStartTime;
        private DateTime sweepSetupEndTime;
        private double sweepSetupStopPrice;
        private bool sessionClosed;
        private string activeEntrySignal;
        private double activeEntryPrice;
        private double activeStopPrice;
        private double activeTpPrice;
        private bool trailingActive;
        private EMA exitEma;
        private int lastReverseBar;
        private static readonly string NewsDatesRaw =
@"2025-01-10,08:30
2025-01-14,08:30
2025-01-15,08:30
2025-02-07,08:30
2025-02-12,08:30
2025-03-07,08:30
2025-03-12,08:30
2025-04-04,08:30
2025-04-10,08:30
2025-05-02,08:30
2025-05-13,08:30
2025-06-06,08:30
2025-06-11,08:30
2025-07-03,08:30
2025-07-15,08:30
2025-08-01,08:30
2025-08-12,08:30
2025-09-04,08:30
2025-09-11,08:30
2025-11-20,08:30
2025-12-03,08:30
2026-01-09,08:30
2026-01-13,08:30
2026-02-06,08:30
2026-02-10,08:30
2026-02-11,08:30
2026-03-05,08:30
2026-03-11,08:30
2026-04-03,08:30
2026-04-10,08:30
2026-05-07,08:30
2026-05-12,08:30
2026-06-05,08:30
2026-06-10,08:30
2026-07-02,08:30
2026-07-14,08:30
2026-08-06,08:30
2026-08-12,08:30
2026-09-03,08:30
2026-09-11,08:30
2026-10-02,08:30
2026-10-14,08:30
2026-11-05,08:30
2026-11-10,08:30
2026-12-04,08:30
2026-01-07,10:00
2026-02-03,10:00
2026-02-18,10:00
2026-03-10,10:00
2026-04-30,10:00
2026-05-28,10:00
2025-01-28,14:00
2025-03-19,14:00
2025-05-07,14:00
2025-06-18,14:00
2025-07-30,14:00
2025-09-17,14:00
2025-10-29,14:00
2025-12-10,14:00
2026-01-28,14:00
2026-03-18,14:00
2026-04-29,14:00
2026-06-17,14:00
2026-07-29,14:00
2026-09-15,14:00
2026-10-27,14:00
2026-12-09,14:00";
        private static readonly List<DateTime> NewsDates = new List<DateTime>();
        private static bool newsDatesInitialized;
        #endregion

        #region NinjaScript Lifecycle
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "H1/M5 DR visualizer (draws dealing ranges only).";
                Name = "Sweeper";
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
                MidRedZonePercent = 12;
                DrBarsPeriodType = BarsPeriodType.Minute;
                DrBarsPeriodValue = 60;
                BoxOpacity = 10;
                LineWidth = 1;
                LineOpacity = 30;
                SweepLineBrush = Brushes.DimGray;
                SweepLineWidth = 1;
                SwingLineBrush = Brushes.DimGray;
                SwingDrawBars = 300;
                DebugLogging = false;
                ShowHistoricalDRs = true;

                SessionStart = new TimeSpan(9, 30, 0);
                SessionEnd = new TimeSpan(16, 0, 0);
                NoTradesAfter = new TimeSpan(15, 30, 0);
                CloseAtSessionEnd = true;
                SessionBrush = Brushes.Gold;
                SkipStart = new TimeSpan(0, 0, 0);
                SkipEnd = new TimeSpan(0, 0, 0);
                ForceCloseAtSkipStart = true;
                UseNewsSkip = true;
                NewsSkipMinutesBefore = 0;
                NewsSkipMinutesAfter = 5;

                ShowInvalidatedFvgs = true;
                MinFvgSizePoints = 0;
                MaxFvgSizePoints = 0;
                FvgOpacity = 20;
                InvalidatedFvgOpacity = 10;
                FvgDrawLimitDays = 5;
                OnlyShowFvgsInsideDr = false;

                FvgFillBrush = Brushes.DarkCyan;
                InvalidatedFvgFillBrush = Brushes.IndianRed;

                SweepLookbackBars = 50;
                TpPercentOfDr = 79;
                EnableTrailingStop = false;
                ExitOnEmaFlat = false;
                EmaPeriod = 5;
                EmaSlopeThreshold = 0;
                EntryModeSetting = EntryMode.Fvg;
                SweepConfirmModeSetting = SweepConfirmMode.RequireClose;
                SweepSourceSetting = SweepSource.CandleHighLow;
                SweepSwingStrength = 1;
                ReverseSwingStrength = 1;
                ReverseOnOppositeFvg = false;
                UseLimitEntries = false;
                LimitEntryPercent = 0;
                DuoMinCombinedBodyPoints = 0;

                DrBoxBrush = Brushes.DodgerBlue;
                DrOutlineBrush = Brushes.DodgerBlue;
                DrMidLineBrush = Brushes.White;
            }
            else if (State == State.Configure)
            {
                if (BarsPeriod.BarsPeriodType == DrBarsPeriodType && BarsPeriod.Value == DrBarsPeriodValue)
                {
                    drSeriesIndex = 0;
                }
                else
                {
                    AddDataSeries(DrBarsPeriodType, DrBarsPeriodValue);
                    drSeriesIndex = 1;
                }
            }
            else if (State == State.DataLoaded)
            {
                if (ExitOnEmaFlat)
                {
                    exitEma = EMA(EmaPeriod);
                    AddChartIndicator(exitEma);
                }

                drCounter = 0;
                hasActiveDR = false;
                currentDrHigh = 0;
                currentDrLow = 0;
                currentDrMid = 0;
                currentDrStartBar = -1;
                currentDrEndBar = -1;
                currentDrStartTime = Core.Globals.MinDate;
                currentDrEndTime = Core.Globals.MinDate;
                currentDrBoxTag = string.Empty;
                currentDrRedZoneTag = string.Empty;
                currentDrMidLineTag = string.Empty;
                currentDrTopLineTag = string.Empty;
                currentDrBottomLineTag = string.Empty;
                lastBreakoutDirection = 0;
                inBreakoutStrike = false;
                currentDrTradable = true;

                activeFvgs = new List<FvgBox>();
                fvgCounter = 0;
                sweepLines = new List<SweepLine>();
                swingLines = new List<SwingLine>();
                sweepLineCounter = 0;
                pendingSweepValid = false;
                pendingSweepStartBarIndex = -1;
                pendingSweepStartTime = Core.Globals.MinDate;
                pendingSweepPrice = 0;
                pendingSweepIsHigh = false;
                pendingSweepDrawRequested = false;
                pendingSweepFillTime = Core.Globals.MinDate;
                sweepSetupActive = false;
                sweepSetupDirection = SetupDirection.Long;
                sweepSetupStartTime = Core.Globals.MinDate;
                sweepSetupEndTime = Core.Globals.MinDate;
                sweepSetupStopPrice = 0;
                sessionClosed = false;
                activeEntrySignal = string.Empty;
                activeEntryPrice = 0;
                activeStopPrice = 0;
                activeTpPrice = 0;
                trailingActive = false;
                lastReverseBar = -1;

                if (DrBoxBrush != null && DrBoxBrush.CanFreeze)
                    DrBoxBrush.Freeze();
                if (DrOutlineBrush != null && DrOutlineBrush.CanFreeze)
                    DrOutlineBrush.Freeze();
                if (DrMidLineBrush != null && DrMidLineBrush.CanFreeze)
                    DrMidLineBrush.Freeze();
                if (SweepLineBrush != null && SweepLineBrush.CanFreeze)
                    SweepLineBrush.Freeze();
                if (SwingLineBrush != null && SwingLineBrush.CanFreeze)
                    SwingLineBrush.Freeze();
                if (FvgFillBrush != null && FvgFillBrush.CanFreeze)
                    FvgFillBrush.Freeze();
                if (InvalidatedFvgFillBrush != null && InvalidatedFvgFillBrush.CanFreeze)
                    InvalidatedFvgFillBrush.Freeze();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 0)
            {
                DrawSessionBackground();

                if (CurrentBar < 1)
                    return;

                if (pendingSweepDrawRequested)
                {
                    if (SweepSourceSetting == SweepSource.CandleHighLow)
                        DrawPendingSweepLine(pendingSweepFillTime);
                    else
                        pendingSweepValid = false;
                    pendingSweepDrawRequested = false;
                }

                bool crossedSkipWindow =
                    (!TimeInSkip(Time[1]) && TimeInSkip(Time[0])) ||
                    (TimeInSkip(Time[1]) && !TimeInSkip(Time[0]));

                if (crossedSkipWindow)
                {
                    if (TimeInSkip(Time[0]))
                    {
                        LogDebug("⛔ Entered skip window");
                        if (ForceCloseAtSkipStart)
                        {
                            if (Position.MarketPosition == MarketPosition.Long)
                                ExitLong("SkipWindow", activeEntrySignal);
                            else if (Position.MarketPosition == MarketPosition.Short)
                                ExitShort("SkipWindow",activeEntrySignal);
                            CancelAllOrders();
                        }
                    }
                    else
                    {
                        LogDebug("✅ Exited skip window");
                    }
                }

                bool crossedNewsWindow =
                    (!TimeInNewsSkip(Time[1]) && TimeInNewsSkip(Time[0])) ||
                    (TimeInNewsSkip(Time[1]) && !TimeInNewsSkip(Time[0]));

                if (crossedNewsWindow)
                {
                    if (TimeInNewsSkip(Time[0]))
                    {
                        LogDebug("⛔ Entered news window");
                        if (ForceCloseAtSkipStart)
                        {
                            if (Position.MarketPosition == MarketPosition.Long)
                                ExitLong("NewsWindow", activeEntrySignal);
                            else if (Position.MarketPosition == MarketPosition.Short)
                                ExitShort("NewsWindow", activeEntrySignal);
                            CancelAllOrders();
                        }
                    }
                    else
                    {
                        LogDebug("✅ Exited news window");
                    }
                }

                if (TimeInSkip(Time[0]))
                {
                    if (ForceCloseAtSkipStart && Position.MarketPosition != MarketPosition.Flat)
                    {
                        ExitLong("SkipWindow", activeEntrySignal);
                        ExitShort("SkipWindow", activeEntrySignal);
                        CancelAllOrders();
                    }
                    sweepSetupActive = false;
                    ClearPendingSweepLine();
                    return;
                }

                if (TimeInNewsSkip(Time[0]))
                {
                    if (ForceCloseAtSkipStart && Position.MarketPosition != MarketPosition.Flat)
                    {
                        ExitLong("NewsWindow", activeEntrySignal);
                        ExitShort("NewsWindow", activeEntrySignal);
                        CancelAllOrders();
                    }
                    sweepSetupActive = false;
                    ClearPendingSweepLine();
                    return;
                }

                if (ExitOnEmaFlat && Position.MarketPosition != MarketPosition.Flat && CurrentBar >= EmaPeriod)
                {
                    double emaNow = exitEma[0];
                    double emaPrev = exitEma[1];
                    double emaSlope = emaNow - emaPrev;
                    double threshold = EmaSlopeThreshold;
                    bool exitLong = Position.MarketPosition == MarketPosition.Long && emaSlope <= -threshold;
                    bool exitShort = Position.MarketPosition == MarketPosition.Short && emaSlope >= threshold;
                    if (exitLong || exitShort)
                    {
                        LogDebug(string.Format("EMA slope ({0}) crossed threshold — flattening position. slope={1} threshold={2}", EmaPeriod, emaSlope, threshold), true);
                        if (exitLong)
                            ExitLong("EMAFlatExit", activeEntrySignal);
                        else if (exitShort)
                            ExitShort("EMAFlatExit", activeEntrySignal);

                        CancelAllOrders();
                    }
                }

                if (Position.MarketPosition == MarketPosition.Flat && !string.IsNullOrEmpty(activeEntrySignal))
                {
                    activeEntrySignal = string.Empty;
                    activeEntryPrice = 0;
                    activeStopPrice = 0;
                    activeTpPrice = 0;
                    trailingActive = false;
                }

                bool inSessionNow = TimeInSession(Time[0]);
                bool inSessionPrev = CurrentBar > 0 ? TimeInSession(Time[1]) : inSessionNow;

                if (inSessionNow && !inSessionPrev)
                {
                    sessionClosed = false;
                    LogDebug("Session start.");
                }

                if (inSessionPrev && !inSessionNow && !sessionClosed)
                {
                    if (CloseAtSessionEnd)
                    {
                        if (Position.MarketPosition == MarketPosition.Long)
                            ExitLong();
                        else if (Position.MarketPosition == MarketPosition.Short)
                            ExitShort();
                    }

                    CancelAllOrders();
                    sweepSetupActive = false;
                    ClearPendingSweepLine();
                    sessionClosed = true;
                    LogDebug("Session end — flatten/cancel.");
                }

                bool crossedNoTrades = CurrentBar > 0 && !TimeInNoTradesAfter(Time[1]) && TimeInNoTradesAfter(Time[0]);

                if (crossedNoTrades)
                {
                    CancelAllOrders();
                    sweepSetupActive = false;
                    ClearPendingSweepLine();
                    LogDebug("NoTradesAfter crossed — canceling orders.");
                }

                if (!inSessionNow || TimeInNoTradesAfter(Time[0]))
                {
                    // Keep drawing FVGs/DRs but block new setups/entries.
                    if (sweepSetupActive && TimeInNoTradesAfter(Time[0]))
                    {
                        sweepSetupActive = false;
                        ClearPendingSweepLine();
                    }
                }

                if (sweepSetupActive && Time[0] > sweepSetupEndTime)
                {
                    LogDebug("Sweep setup expired.");
                    sweepSetupActive = false;
                    ClearPendingSweepLine();
                }

                if (CurrentBar >= 2)
                    UpdateFvgs();

                if (SweepSourceSetting == SweepSource.SwingHighLow)
                {
                    ClearSweepLines();
                    UpdateSwingLiquidityPrimary();

                    if (SweepConfirmModeSetting == SweepConfirmMode.RequireClose
                        && TimeInSession(Time[0])
                        && !TimeInNoTradesAfter(Time[0]))
                    {
                        EvaluateSweepSetup();
                    }
                    else if (SweepConfirmModeSetting == SweepConfirmMode.SkipClose)
                    {
                        EvaluateSweepSetupSkipClose();
                    }

                    UpdateSwingLinesDraw();
                }
                else
                {
                    ClearSwingLines();
                    UpdateSweepLinesDraw();

                    if (SweepConfirmModeSetting == SweepConfirmMode.SkipClose)
                        EvaluateSweepSetupSkipClose();
                }

                if (drSeriesIndex != 0)
                {
                    if (hasActiveDR)
                        DrawCurrentDR(Times[0][0]);
                    if (EnableTrailingStop)
                        UpdateTrailingStop();
                    if (EntryModeSetting == EntryMode.Duo)
                        EvaluateDuoEntry();
                    return;
                }
            }

            if (BarsInProgress != drSeriesIndex)
                return;

            if (CurrentBars[drSeriesIndex] < SwingStrength * 2 + LegSwingLookback)
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

            if (SweepConfirmModeSetting == SweepConfirmMode.RequireClose
                && SweepSourceSetting != SweepSource.SwingHighLow
                && TimeInSession(Times[drSeriesIndex][0])
                && !TimeInNoTradesAfter(Times[drSeriesIndex][0]))
            {
                EvaluateSweepSetup();
            }
        }
        #endregion

        #region FVG Logic
        private void UpdateFvgs()
        {
            if (FvgDrawLimitDays <= 0)
            {
                if (activeFvgs.Count > 0)
                {
                    for (int i = 0; i < activeFvgs.Count; i++)
                        RemoveDrawObject(activeFvgs[i].Tag);
                    activeFvgs.Clear();
                }
                return;
            }

            PruneFvgs(FvgDrawLimitDays);
            UpdateActiveFvgs();
            DetectNewFvg();
        }

        private void PruneFvgs(int drawLimitDays)
        {
            if (drawLimitDays <= 0)
                return;

            DateTime cutoffDate = Time[0].Date.AddDays(-(drawLimitDays - 1));
            for (int i = activeFvgs.Count - 1; i >= 0; i--)
            {
                FvgBox fvg = activeFvgs[i];
                if (fvg.SessionDate < cutoffDate)
                {
                    RemoveDrawObject(fvg.Tag);
                    activeFvgs.RemoveAt(i);
                }
            }
        }

        private void UpdateActiveFvgs()
        {
            for (int i = 0; i < activeFvgs.Count; i++)
            {
                FvgBox fvg = activeFvgs[i];
                if (!fvg.IsActive)
                    continue;

                bool invalidated = fvg.IsBullish
                    ? (Close[0] < fvg.Lower && Close[0] < Open[0])
                    : (Close[0] > fvg.Upper && Close[0] > Open[0]);

                fvg.EndBarIndex = CurrentBar;

                int startBarsAgo = CurrentBar - fvg.StartBarIndex;
                int endBarsAgo = CurrentBar - fvg.EndBarIndex;
                if (startBarsAgo < 0)
                    startBarsAgo = 0;
                if (endBarsAgo < 0)
                    endBarsAgo = 0;

                if (!ShouldDrawFvg(fvg.Lower, fvg.Upper))
                {
                    RemoveDrawObject(fvg.Tag);
                    continue;
                }

                Draw.Rectangle(
                    this,
                    fvg.Tag,
                    false,
                    startBarsAgo,
                    fvg.Lower,
                    endBarsAgo,
                    fvg.Upper,
                    Brushes.Transparent,
                    FvgFillBrush,
                    FvgOpacity
                );

                if (invalidated)
                {
                    fvg.IsActive = false;
                    if (!ShowInvalidatedFvgs)
                    {
                        RemoveDrawObject(fvg.Tag);
                    }
                    else
                    {
                        if (ShouldDrawFvg(fvg.Lower, fvg.Upper))
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
                                InvalidatedFvgFillBrush,
                                InvalidatedFvgOpacity
                            );
                        }
                    }
                }
            }
        }

        private void DetectNewFvg()
        {
            bool bullishFvg = Low[0] > High[2];
            bool bearishFvg = High[0] < Low[2];

            if (!bullishFvg && !bearishFvg)
                return;

            if (sweepSetupActive)
            {
                LogDebug(string.Format(
                    "FVG detected {0}. Lower={1:F2} Upper={2:F2}",
                    bullishFvg ? "bullish" : "bearish",
                    bullishFvg ? High[2] : High[0],
                    bullishFvg ? Low[0] : Low[2]));
            }

            FvgBox fvg = new FvgBox();
            fvg.IsBullish = bullishFvg;
            fvg.Lower = bullishFvg ? High[2] : High[0];
            fvg.Upper = bullishFvg ? Low[0] : Low[2];
            fvg.StartBarIndex = CurrentBar - 2;
            fvg.EndBarIndex = CurrentBar;
            fvg.IsActive = true;
            fvg.SessionDate = Time[0].Date;
            fvg.Tag = string.Format("Sweeper_FVG_{0}_{1:yyyyMMdd_HHmmss}", fvgCounter++, Time[0]);

            double fvgSizePoints = Math.Abs(fvg.Upper - fvg.Lower);
            if (MinFvgSizePoints > 0 && fvgSizePoints < MinFvgSizePoints)
                return;
            if (MaxFvgSizePoints > 0 && fvgSizePoints > MaxFvgSizePoints)
                return;
            if (!ShouldDrawFvg(fvg.Lower, fvg.Upper))
                return;

            activeFvgs.Add(fvg);

            Draw.Rectangle(
                this,
                fvg.Tag,
                false,
                2,
                fvg.Lower,
                0,
                fvg.Upper,
                Brushes.Transparent,
                FvgFillBrush,
                FvgOpacity
            );

            TryReverseOnOppositeFvg(bullishFvg, bearishFvg, fvg.Lower, fvg.Upper);
            TryEnterFromFvgSignal(bullishFvg, bearishFvg, fvg.Lower, fvg.Upper);
        }

        private bool ShouldDrawFvg(double fvgLower, double fvgUpper)
        {
            if (!OnlyShowFvgsInsideDr)
                return true;
            if (!hasActiveDR)
                return false;
            return fvgLower >= currentDrLow && fvgUpper <= currentDrHigh;
        }

        private void TryEnterFromFvgSignal(bool bullishFvg, bool bearishFvg, double fvgLower, double fvgUpper)
        {
            if (EntryModeSetting != EntryMode.Fvg)
                return;
            if (!sweepSetupActive)
                return;
            if (Position.MarketPosition != MarketPosition.Flat)
                return;
            if (Time[0] <= sweepSetupStartTime || Time[0] > sweepSetupEndTime)
                return;
            if (!TimeInSession(Time[0]) || TimeInNoTradesAfter(Time[0]))
                return;

            bool matchesDirection = sweepSetupDirection == SetupDirection.Long ? bullishFvg : bearishFvg;
            if (!matchesDirection)
                return;
            if (!IsFvgInTradeZone(sweepSetupDirection, fvgLower, fvgUpper))
            {
                LogDebug(string.Format(
                    "FVG rejected (red zone overlap). Dir={0} FVG[{1:F2}-{2:F2}] DR[{3:F2}-{4:F2}] Mid={5:F2}",
                    sweepSetupDirection == SetupDirection.Long ? "LONG" : "SHORT",
                    fvgLower,
                    fvgUpper,
                    currentDrLow,
                    currentDrHigh,
                    currentDrMid));
                return;
            }

            double drHeight = currentDrHigh - currentDrLow;
            if (drHeight <= 0)
                return;

            double tp = sweepSetupDirection == SetupDirection.Long
                ? currentDrLow + drHeight * (TpPercentOfDr / 100.0)
                : currentDrHigh - drHeight * (TpPercentOfDr / 100.0);

            double entryPrice = UseLimitEntries
                ? GetLimitEntryPrice(sweepSetupDirection, fvgLower, fvgUpper)
                : Close[0];

            double stop = sweepSetupStopPrice;
            if (SweepSourceSetting == SweepSource.SwingHighLow)
                stop = bullishFvg ? Low[2] : High[2];

            string signal = sweepSetupDirection == SetupDirection.Long ? "Sweeper_Long" : "Sweeper_Short";

            LogDebug(string.Format(
                "FVG trigger {0}. TP={1:F2} SL={2:F2} (FVG {3})",
                sweepSetupDirection == SetupDirection.Long ? "LONG" : "SHORT",
                tp,
                stop,
                sweepSetupDirection == SetupDirection.Long ? "bullish" : "bearish"));

            activeEntrySignal = signal;
            activeEntryPrice = entryPrice;
            activeStopPrice = stop;
            activeTpPrice = tp;
            trailingActive = false;

            SetStopLoss(signal, CalculationMode.Price, stop, false);
            SetProfitTarget(signal, CalculationMode.Price, tp);

            if (sweepSetupDirection == SetupDirection.Long)
            {
                if (UseLimitEntries)
                    EnterLongLimit(activeEntryPrice, signal);
                else
                    EnterLong(signal);
            }
            else
            {
                if (UseLimitEntries)
                    EnterShortLimit(activeEntryPrice, signal);
                else
                    EnterShort(signal);
            }

            sweepSetupActive = false;
        }

        private void TryReverseOnOppositeFvg(bool bullishFvg, bool bearishFvg, double fvgLower, double fvgUpper)
        {
            if (EntryModeSetting != EntryMode.Fvg)
                return;
            if (!ReverseOnOppositeFvg)
                return;
            if (Position.MarketPosition == MarketPosition.Flat)
                return;
            if (CurrentBar == lastReverseBar)
                return;
            if (!TimeInSession(Time[0]) || TimeInNoTradesAfter(Time[0]))
                return;

            bool shouldReverse = Position.MarketPosition == MarketPosition.Long ? bearishFvg : bullishFvg;
            if (!shouldReverse)
                return;
            SetupDirection newDirection = Position.MarketPosition == MarketPosition.Long
                ? SetupDirection.Short
                : SetupDirection.Long;
            if (!IsFvgInTradeZone(newDirection, fvgLower, fvgUpper))
            {
                LogDebug(string.Format(
                    "Reverse rejected (red zone overlap). Dir={0} FVG[{1:F2}-{2:F2}] DR[{3:F2}-{4:F2}] Mid={5:F2}",
                    newDirection == SetupDirection.Long ? "LONG" : "SHORT",
                    fvgLower,
                    fvgUpper,
                    currentDrLow,
                    currentDrHigh,
                    currentDrMid));
                return;
            }

            double swingPrice;
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (!TryFindRecentSwingHigh(ReverseSwingStrength, out swingPrice))
                    return;
            }
            else
            {
                if (!TryFindRecentSwingLow(ReverseSwingStrength, out swingPrice))
                    return;
            }

            double drHeight = currentDrHigh - currentDrLow;
            if (drHeight <= 0)
                return;

            double tp = newDirection == SetupDirection.Long
                ? currentDrLow + drHeight * (TpPercentOfDr / 100.0)
                : currentDrHigh - drHeight * (TpPercentOfDr / 100.0);

            double stop = swingPrice;
            string signal = newDirection == SetupDirection.Long ? "Sweeper_RevLong" : "Sweeper_RevShort";

            LogDebug(string.Format(
                "Reverse on opposite FVG. NewDir={0} TP={1:F2} SL={2:F2}",
                newDirection == SetupDirection.Long ? "LONG" : "SHORT",
                tp,
                stop), true);

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong("ReverseExit", activeEntrySignal);
            else
                ExitShort("ReverseExit", activeEntrySignal);

            SetStopLoss(signal, CalculationMode.Price, stop, false);
            SetProfitTarget(signal, CalculationMode.Price, tp);

            if (newDirection == SetupDirection.Long)
            {
                double entry = UseLimitEntries ? GetLimitEntryPrice(newDirection, fvgLower, fvgUpper) : Close[0];
                if (UseLimitEntries)
                    EnterLongLimit(entry, signal);
                else
                    EnterLong(signal);
                activeEntryPrice = entry;
            }
            else
            {
                double entry = UseLimitEntries ? GetLimitEntryPrice(newDirection, fvgLower, fvgUpper) : Close[0];
                if (UseLimitEntries)
                    EnterShortLimit(entry, signal);
                else
                    EnterShort(signal);
                activeEntryPrice = entry;
            }

            activeEntrySignal = signal;
            activeStopPrice = stop;
            activeTpPrice = tp;
            trailingActive = false;
            lastReverseBar = CurrentBar;
        }
        #endregion

        #region Sweep Entry Logic
        private void SetPendingSweepLine(bool isHigh, double price, DateTime sweepStartTime)
        {
            int startBarIndex = Bars.GetBar(sweepStartTime);
            if (startBarIndex < 0)
                return;

            pendingSweepValid = true;
            pendingSweepStartBarIndex = startBarIndex;
            pendingSweepStartTime = sweepStartTime;
            pendingSweepPrice = price;
            pendingSweepIsHigh = isHigh;
        }

        private void DrawPendingSweepLine(DateTime fillTime)
        {
            if (!pendingSweepValid)
                return;

            string tag = string.Format(
                "Sweeper_Sweep_{0}_{1:yyyyMMdd_HHmmss}_{2}",
                pendingSweepIsHigh ? "H" : "L",
                fillTime,
                sweepLineCounter++);

            SweepLine line = new SweepLine
            {
                Tag = tag,
                Price = pendingSweepPrice,
                StartBarIndex = pendingSweepStartBarIndex,
                EndBarIndex = CurrentBar,
                StartTime = pendingSweepStartTime,
                EndTime = fillTime,
                IsActive = false,
                IsHigh = pendingSweepIsHigh
            };

            Draw.Line(
                this,
                line.Tag,
                false,
                line.StartTime,
                line.Price,
                line.EndTime,
                line.Price,
                SweepLineBrush,
                DashStyleHelper.Solid,
                SweepLineWidth
            );

            if (DebugLogging)
                LogDebug(string.Format("Sweep line draw. Price={0:F2} Start={1:yyyy-MM-dd HH:mm} End={2:yyyy-MM-dd HH:mm}", line.Price, line.StartTime, line.EndTime));

            sweepLines.Add(line);
            pendingSweepValid = false;
        }

        private void UpdateSweepLinesDraw()
        {
            if (sweepLines == null || sweepLines.Count == 0)
                return;

            for (int i = 0; i < sweepLines.Count; i++)
            {
                SweepLine line = sweepLines[i];
                if (line == null || !line.IsActive)
                    continue;

                Draw.Line(
                    this,
                    line.Tag,
                    false,
                    line.StartTime,
                    line.Price,
                    line.EndTime,
                    line.Price,
                    SweepLineBrush,
                    DashStyleHelper.Solid,
                    SweepLineWidth
                );
            }
        }

        private void ClearSweepLines()
        {
            if (sweepLines == null || sweepLines.Count == 0)
                return;

            for (int i = 0; i < sweepLines.Count; i++)
            {
                if (!string.IsNullOrEmpty(sweepLines[i].Tag))
                    RemoveDrawObject(sweepLines[i].Tag);
            }
            sweepLines.Clear();
        }

        private void UpdateSwingLinesDraw()
        {
            if (swingLines == null || swingLines.Count == 0)
                return;

            for (int i = 0; i < swingLines.Count; i++)
            {
                SwingLine line = swingLines[i];
                if (line == null || !line.IsActive)
                    continue;

                bool hit = line.IsHigh ? High[0] >= line.Price : Low[0] <= line.Price;
                line.EndTime = Time[0];

                Draw.Line(
                    this,
                    line.Tag,
                    false,
                    line.StartTime,
                    line.Price,
                    line.EndTime,
                    line.Price,
                    SwingLineBrush,
                    DashStyleHelper.Solid,
                    1
                );

                if (hit)
                {
                    if (DebugLogging)
                    {
                        LogDebug(string.Format(
                            "Swing line hit {0} price={1:F2} time={2:yyyy-MM-dd HH:mm}",
                            line.IsHigh ? "HIGH" : "LOW",
                            line.Price,
                            Time[0]));
                    }
                    line.IsActive = false;
                }
            }
        }

        private void ClearSwingLines()
        {
            if (swingLines == null || swingLines.Count == 0)
                return;

            for (int i = 0; i < swingLines.Count; i++)
            {
                if (!string.IsNullOrEmpty(swingLines[i].Tag))
                    RemoveDrawObject(swingLines[i].Tag);
            }
            swingLines.Clear();
        }

        private void UpdateSwingLiquidityPrimary()
        {
            if (SweepSourceSetting != SweepSource.SwingHighLow)
                return;

            if (SwingDrawBars <= 0)
            {
                ClearSwingLines();
                return;
            }

            if (CurrentBar < SweepSwingStrength * 2)
                return;

            int pivotBarsAgo = SweepSwingStrength;
            if (IsSwingHighAt(pivotBarsAgo, SweepSwingStrength))
            {
                double price = High[pivotBarsAgo];
                if (!WasSwingLevelSwept(pivotBarsAgo, price, true))
                    AddSwingLine(true, pivotBarsAgo, price);
            }
            if (IsSwingLowAt(pivotBarsAgo, SweepSwingStrength))
            {
                double price = Low[pivotBarsAgo];
                if (!WasSwingLevelSwept(pivotBarsAgo, price, false))
                    AddSwingLine(false, pivotBarsAgo, price);
            }

            PruneSwingLines();
        }

        private void AddSwingLine(bool isHigh, int barsAgo, double price)
        {
            int startBarIndex = CurrentBar - barsAgo;
            DateTime startTime = Time[barsAgo];
            string tag = string.Format("Sweeper_Swing_{0}_{1:yyyyMMdd_HHmmss}", isHigh ? "H" : "L", startTime);

            SwingLine line = new SwingLine
            {
                Tag = tag,
                Price = price,
                StartBarIndex = startBarIndex,
                StartTime = startTime,
                EndTime = startTime,
                IsActive = true,
                IsHigh = isHigh
            };

            swingLines.Add(line);
            if (DebugLogging)
            {
                LogDebug(string.Format(
                    "Swing line created {0} price={1:F2} time={2:yyyy-MM-dd HH:mm} bar={3}",
                    isHigh ? "HIGH" : "LOW",
                    price,
                    startTime,
                    startBarIndex));
            }

        }

        private void PruneSwingLines()
        {
            int cutoffIndex = CurrentBar - SwingDrawBars;
            for (int i = swingLines.Count - 1; i >= 0; i--)
            {
                SwingLine line = swingLines[i];
                if (line.StartBarIndex < cutoffIndex)
                {
                    if (!string.IsNullOrEmpty(line.Tag))
                        RemoveDrawObject(line.Tag);
                    swingLines.RemoveAt(i);
                }
            }
        }
        private void ClearPendingSweepLine()
        {
            pendingSweepValid = false;
            pendingSweepStartTime = Core.Globals.MinDate;
            pendingSweepDrawRequested = false;
            pendingSweepFillTime = Core.Globals.MinDate;
        }

        private void EvaluateSweepSetup()
        {
            if (!hasActiveDR)
                return;
            if (Position.MarketPosition != MarketPosition.Flat)
                return;
            if (CurrentBar < 2)
                return;

            double close = Close[0];
            if (!IsInsideDr(close) || !IsOutsideRedZone(close))
                return;

            double redLow, redHigh;
            GetRedZone(out redLow, out redHigh);

            int sweptIndex;
            double sweptPrice;
            bool requireSweepClose = SweepConfirmModeSetting == SweepConfirmMode.RequireClose;

            if (SweepSourceSetting == SweepSource.SwingHighLow)
            {
                SwingLine swingLine;
                if (TryFindActiveSwingHigh(out swingLine))
                {
                    sweptPrice = swingLine.Price;
                    bool wickValid = High[0] <= currentDrHigh && High[0] >= redHigh;
                    bool sweepHit = High[0] > sweptPrice && wickValid;
                    bool closeConfirm = close < sweptPrice;
                    if (sweepHit && (!requireSweepClose || closeConfirm))
                    {
                        LogDebug(string.Format(
                            "Sweep SHORT armed. SweptHigh={0:F2} CurrHigh={1:F2} Close={2:F2}",
                            sweptPrice, High[0], close), true);
                        double stop;
                        if (!TryGetClosestSwingStop(SetupDirection.Short, Close[0], out stop))
                            stop = sweptPrice + TickSize;
                        LogDebug(string.Format(
                            "Pending sweep line set (SHORT). Price={0:F2} StartTime={1:yyyy-MM-dd HH:mm}",
                            sweptPrice, swingLine.StartTime));
                        if (sweepSetupActive)
                            ClearPendingSweepLine();
                        SetPendingSweepLine(true, sweptPrice, swingLine.StartTime);
                        ArmSweepSetupSwing(SetupDirection.Short, stop);
                        swingLine.IsActive = false;
                        return;
                    }
                }

                if (TryFindActiveSwingLow(out swingLine))
                {
                    sweptPrice = swingLine.Price;
                    bool wickValid = Low[0] >= currentDrLow && Low[0] <= redLow;
                    bool sweepHit = Low[0] < sweptPrice && wickValid;
                    bool closeConfirm = close > sweptPrice;
                    if (sweepHit && (!requireSweepClose || closeConfirm))
                    {
                        LogDebug(string.Format(
                            "Sweep LONG armed. SweptLow={0:F2} CurrLow={1:F2} Close={2:F2}",
                            sweptPrice, Low[0], close), true);
                        double stop;
                        if (!TryGetClosestSwingStop(SetupDirection.Long, Close[0], out stop))
                            stop = sweptPrice - TickSize;
                        LogDebug(string.Format(
                            "Pending sweep line set (LONG). Price={0:F2} StartTime={1:yyyy-MM-dd HH:mm}",
                            sweptPrice, swingLine.StartTime));
                        if (sweepSetupActive)
                            ClearPendingSweepLine();
                        SetPendingSweepLine(false, sweptPrice, swingLine.StartTime);
                        ArmSweepSetupSwing(SetupDirection.Long, stop);
                        swingLine.IsActive = false;
                    }
                }

                return;
            }

            if (TryFindUnsweptHigh(out sweptIndex, out sweptPrice))
            {
                bool wickValid = High[0] <= currentDrHigh && High[0] >= redHigh;
                bool sweepHit = High[0] > sweptPrice && wickValid;
                bool closeConfirm = close < sweptPrice;
                if (sweepHit && (!requireSweepClose || closeConfirm))
                {
                    LogDebug(string.Format(
                        "Sweep SHORT armed. SweptHigh={0:F2} CurrHigh={1:F2} Close={2:F2}",
                        sweptPrice, High[0], close), true);
                    double stop = requireSweepClose ? Highs[drSeriesIndex][0] : High[sweptIndex];
                    LogDebug(string.Format(
                        "Pending sweep line set (SHORT). Price={0:F2} StartTime={1:yyyy-MM-dd HH:mm}",
                        sweptPrice, Times[drSeriesIndex][sweptIndex]));
                    SetPendingSweepLine(true, sweptPrice, Times[drSeriesIndex][sweptIndex]);
                    ArmSweepSetup(SetupDirection.Short, stop);
                    return;
                }
            }

            if (TryFindUnsweptLow(out sweptIndex, out sweptPrice))
            {
                bool wickValid = Low[0] >= currentDrLow && Low[0] <= redLow;
                bool sweepHit = Low[0] < sweptPrice && wickValid;
                bool closeConfirm = close > sweptPrice;
                if (sweepHit && (!requireSweepClose || closeConfirm))
                {
                    LogDebug(string.Format(
                        "Sweep LONG armed. SweptLow={0:F2} CurrLow={1:F2} Close={2:F2}",
                        sweptPrice, Low[0], close), true);
                    double stop = requireSweepClose ? Lows[drSeriesIndex][0] : Low[sweptIndex];
                    LogDebug(string.Format(
                        "Pending sweep line set (LONG). Price={0:F2} StartTime={1:yyyy-MM-dd HH:mm}",
                        sweptPrice, Times[drSeriesIndex][sweptIndex]));
                    SetPendingSweepLine(false, sweptPrice, Times[drSeriesIndex][sweptIndex]);
                    ArmSweepSetup(SetupDirection.Long, stop);
                }
            }
        }

        private void EvaluateSweepSetupSkipClose()
        {
            if (SweepConfirmModeSetting != SweepConfirmMode.SkipClose)
                return;
            if (!hasActiveDR)
                return;
            if (Position.MarketPosition != MarketPosition.Flat)
                return;
            if (sweepSetupActive && SweepSourceSetting != SweepSource.SwingHighLow)
                return;
            if (!TimeInSession(Time[0]) || TimeInNoTradesAfter(Time[0]))
                return;

            double close = Close[0];
            if (!IsInsideDr(close) || !IsOutsideRedZone(close))
                return;

            double redLow, redHigh;
            GetRedZone(out redLow, out redHigh);

            int sweptIndex;
            double sweptPrice;

            if (SweepSourceSetting == SweepSource.SwingHighLow)
            {
                SwingLine swingLine;
                if (TryFindActiveSwingHigh(out swingLine))
                {
                    sweptPrice = swingLine.Price;
                    double seriesHigh = High[0];
                    bool wickValid = seriesHigh <= currentDrHigh && seriesHigh >= redHigh;
                    if (seriesHigh > sweptPrice && wickValid)
                    {
                        LogDebug(string.Format(
                            "Sweep SHORT armed (skip close). SweptHigh={0:F2} CurrHigh={1:F2} Close={2:F2}",
                            sweptPrice, seriesHigh, close), true);
                        LogDebug(string.Format(
                            "Pending sweep line set (SHORT). Price={0:F2} StartTime={1:yyyy-MM-dd HH:mm}",
                            sweptPrice, swingLine.StartTime));
                        if (sweepSetupActive)
                            ClearPendingSweepLine();
                        SetPendingSweepLine(true, sweptPrice, swingLine.StartTime);
                        double stop;
                        if (!TryGetClosestSwingStop(SetupDirection.Short, Close[0], out stop))
                            stop = sweptPrice + TickSize;
                        ArmSweepSetupSwing(SetupDirection.Short, stop);
                        swingLine.IsActive = false;
                        return;
                    }
                }

                if (TryFindActiveSwingLow(out swingLine))
                {
                    sweptPrice = swingLine.Price;
                    double seriesLow = Low[0];
                    bool wickValid = seriesLow >= currentDrLow && seriesLow <= redLow;
                    if (seriesLow < sweptPrice && wickValid)
                    {
                        LogDebug(string.Format(
                            "Sweep LONG armed (skip close). SweptLow={0:F2} CurrLow={1:F2} Close={2:F2}",
                            sweptPrice, seriesLow, close), true);
                        LogDebug(string.Format(
                            "Pending sweep line set (LONG). Price={0:F2} StartTime={1:yyyy-MM-dd HH:mm}",
                            sweptPrice, swingLine.StartTime));
                        if (sweepSetupActive)
                            ClearPendingSweepLine();
                        SetPendingSweepLine(false, sweptPrice, swingLine.StartTime);
                        double stop;
                        if (!TryGetClosestSwingStop(SetupDirection.Long, Close[0], out stop))
                            stop = sweptPrice - TickSize;
                        ArmSweepSetupSwing(SetupDirection.Long, stop);
                        swingLine.IsActive = false;
                    }
                }

                return;
            }

            if (CurrentBars[drSeriesIndex] < 2)
                return;
            if (!TimeInSession(Times[drSeriesIndex][0]) || TimeInNoTradesAfter(Times[drSeriesIndex][0]))
                return;

            close = Closes[drSeriesIndex][0];
            if (!IsInsideDr(close) || !IsOutsideRedZone(close))
                return;

            if (TryFindUnsweptHighOnSeries(drSeriesIndex, out sweptIndex, out sweptPrice))
            {
                double seriesHigh = Highs[drSeriesIndex][0];
                bool wickValid = seriesHigh <= currentDrHigh && seriesHigh >= redHigh;
                if (seriesHigh > sweptPrice && wickValid)
                {
                    LogDebug(string.Format(
                        "Sweep SHORT armed (skip close). SweptHigh={0:F2} CurrHigh={1:F2} Close={2:F2}",
                        sweptPrice, seriesHigh, close), true);
                    LogDebug(string.Format(
                        "Pending sweep line set (SHORT). Price={0:F2} StartTime={1:yyyy-MM-dd HH:mm}",
                        sweptPrice, Times[drSeriesIndex][sweptIndex]));
                    SetPendingSweepLine(true, sweptPrice, Times[drSeriesIndex][sweptIndex]);
                    ArmSweepSetup(SetupDirection.Short, Highs[drSeriesIndex][sweptIndex]);
                    return;
                }
            }

            if (TryFindUnsweptLowOnSeries(drSeriesIndex, out sweptIndex, out sweptPrice))
            {
                double seriesLow = Lows[drSeriesIndex][0];
                bool wickValid = seriesLow >= currentDrLow && seriesLow <= redLow;
                if (seriesLow < sweptPrice && wickValid)
                {
                    LogDebug(string.Format(
                        "Sweep LONG armed (skip close). SweptLow={0:F2} CurrLow={1:F2} Close={2:F2}",
                        sweptPrice, seriesLow, close), true);
                    LogDebug(string.Format(
                        "Pending sweep line set (LONG). Price={0:F2} StartTime={1:yyyy-MM-dd HH:mm}",
                        sweptPrice, Times[drSeriesIndex][sweptIndex]));
                    SetPendingSweepLine(false, sweptPrice, Times[drSeriesIndex][sweptIndex]);
                    ArmSweepSetup(SetupDirection.Long, Lows[drSeriesIndex][sweptIndex]);
                }
            }
        }

        private void ArmSweepSetup(SetupDirection direction)
        {
            double stop = direction == SetupDirection.Long
                ? Lows[drSeriesIndex][0]
                : Highs[drSeriesIndex][0];
            ArmSweepSetup(direction, stop);
        }

        private void ArmSweepSetup(SetupDirection direction, double stopPrice)
        {
            sweepSetupActive = true;
            sweepSetupDirection = direction;
            sweepSetupStartTime = Times[drSeriesIndex][0];
            sweepSetupEndTime = sweepSetupStartTime.AddMinutes(GetDrPeriodMinutes());
            sweepSetupStopPrice = stopPrice;
        }

        private void ArmSweepSetupSwing(SetupDirection direction, double stopPrice)
        {
            sweepSetupActive = true;
            sweepSetupDirection = direction;
            sweepSetupStartTime = Time[0];
            sweepSetupEndTime = sweepSetupStartTime.AddMinutes(GetDrPeriodMinutes());
            sweepSetupStopPrice = stopPrice;
        }

        private bool TryGetClosestSwingStop(SetupDirection direction, double referencePrice, out double stopPrice)
        {
            stopPrice = 0;
            if (swingLines == null || swingLines.Count == 0)
                return false;

            bool wantHigh = direction == SetupDirection.Short;
            double best = 0;
            bool found = false;

            for (int i = 0; i < swingLines.Count; i++)
            {
                SwingLine line = swingLines[i];
                if (line == null || !line.IsActive)
                    continue;
                if (line.IsHigh != wantHigh)
                    continue;

                double price = line.Price;
                if (DebugLogging)
                {
                    LogDebug(string.Format(
                        "Swing stop candidate dir={0} price={1:F2} ref={2:F2} time={3:yyyy-MM-dd HH:mm}",
                        direction,
                        price,
                        referencePrice,
                        line.StartTime));
                }
                if (direction == SetupDirection.Long)
                {
                    if (price >= referencePrice)
                        continue;
                    // Use the closest active swing low below the reference.
                    if (!found || price > best)
                    {
                        best = price;
                        found = true;
                    }
                }
                else
                {
                    if (price <= referencePrice)
                        continue;
                    // Use the closest active swing high above the reference.
                    if (!found || price < best)
                    {
                        best = price;
                        found = true;
                    }
                }
            }

            if (!found)
                return false;

            stopPrice = best + (direction == SetupDirection.Short ? TickSize : -TickSize);
            if (DebugLogging)
            {
                LogDebug(string.Format(
                    "Swing stop selected dir={0} stop={1:F2} ref={2:F2}",
                    direction,
                    stopPrice,
                    referencePrice));
            }
            return true;
        }


        private bool WasSwingLevelSwept(int pivotBarsAgo, double price, bool isHigh)
        {
            if (pivotBarsAgo <= 0)
                return false;

            for (int barsAgo = 0; barsAgo < pivotBarsAgo; barsAgo++)
            {
                if (isHigh)
                {
                    if (High[barsAgo] >= price)
                        return true;
                }
                else
                {
                    if (Low[barsAgo] <= price)
                        return true;
                }
            }

            return false;
        }


        private int GetDrPeriodMinutes()
        {
            switch (DrBarsPeriodType)
            {
                case BarsPeriodType.Minute:
                    return DrBarsPeriodValue;
                case BarsPeriodType.Day:
                    return DrBarsPeriodValue * 1440;
                case BarsPeriodType.Week:
                    return DrBarsPeriodValue * 10080;
                case BarsPeriodType.Month:
                    return DrBarsPeriodValue * 43200;
                default:
                    return 60;
            }
        }

        private bool TryFindUnsweptHigh(out int barsAgo, out double high)
        {
            barsAgo = -1;
            high = 0;

            int maxLookback = Math.Min(SweepLookbackBars, CurrentBar - 1);
            for (int i = 1; i <= maxLookback; i++)
            {
                double candidate = High[i];
                bool swept = false;
                for (int j = 1; j < i; j++)
                {
                    if (High[j] > candidate)
                    {
                        swept = true;
                        break;
                    }
                }
                if (swept)
                    continue;

                barsAgo = i;
                high = candidate;
                return true;
            }

            return false;
        }

        private bool TryFindActiveSwingHigh(out SwingLine swingLine)
        {
            swingLine = null;
            if (swingLines == null || swingLines.Count == 0)
                return false;

            int currentIndex = CurrentBar;
            int cutoffIndex = currentIndex - SweepLookbackBars;

            for (int i = swingLines.Count - 1; i >= 0; i--)
            {
                SwingLine line = swingLines[i];
                if (line == null || !line.IsActive || !line.IsHigh)
                    continue;
                if (SweepLookbackBars > 0 && line.StartBarIndex < cutoffIndex)
                    continue;

                swingLine = line;
                return true;
            }

            return false;
        }

        private bool TryFindUnsweptHighOnSeries(int seriesIndex, out int barsAgo, out double high)
        {
            barsAgo = -1;
            high = 0;

            int maxLookback = Math.Min(SweepLookbackBars, CurrentBars[seriesIndex] - 1);
            for (int i = 1; i <= maxLookback; i++)
            {
                double candidate = Highs[seriesIndex][i];
                bool swept = false;
                for (int j = 1; j < i; j++)
                {
                    if (Highs[seriesIndex][j] > candidate)
                    {
                        swept = true;
                        break;
                    }
                }
                if (swept)
                    continue;

                barsAgo = i;
                high = candidate;
                return true;
            }

            return false;
        }

        private bool TryFindUnsweptLow(out int barsAgo, out double low)
        {
            barsAgo = -1;
            low = 0;

            int maxLookback = Math.Min(SweepLookbackBars, CurrentBar - 1);
            for (int i = 1; i <= maxLookback; i++)
            {
                double candidate = Low[i];
                bool swept = false;
                for (int j = 1; j < i; j++)
                {
                    if (Low[j] < candidate)
                    {
                        swept = true;
                        break;
                    }
                }
                if (swept)
                    continue;

                barsAgo = i;
                low = candidate;
                return true;
            }

            return false;
        }

        private bool TryFindUnsweptLowOnSeries(int seriesIndex, out int barsAgo, out double low)
        {
            barsAgo = -1;
            low = 0;

            int maxLookback = Math.Min(SweepLookbackBars, CurrentBars[seriesIndex] - 1);
            for (int i = 1; i <= maxLookback; i++)
            {
                double candidate = Lows[seriesIndex][i];
                bool swept = false;
                for (int j = 1; j < i; j++)
                {
                    if (Lows[seriesIndex][j] < candidate)
                    {
                        swept = true;
                        break;
                    }
                }
                if (swept)
                    continue;

                barsAgo = i;
                low = candidate;
                return true;
            }

            return false;
        }

        private bool TryFindActiveSwingLow(out SwingLine swingLine)
        {
            swingLine = null;
            if (swingLines == null || swingLines.Count == 0)
                return false;

            int currentIndex = CurrentBar;
            int cutoffIndex = currentIndex - SweepLookbackBars;

            for (int i = swingLines.Count - 1; i >= 0; i--)
            {
                SwingLine line = swingLines[i];
                if (line == null || !line.IsActive || line.IsHigh)
                    continue;
                if (SweepLookbackBars > 0 && line.StartBarIndex < cutoffIndex)
                    continue;

                swingLine = line;
                return true;
            }

            return false;
        }

        private bool IsInsideDr(double price)
        {
            return price >= currentDrLow && price <= currentDrHigh;
        }

        private bool IsOutsideRedZone(double price)
        {
            if (MidRedZonePercent <= 0)
                return true;

            double redLow, redHigh;
            GetRedZone(out redLow, out redHigh);
            return price <= redLow || price >= redHigh;
        }

        private void GetRedZone(out double redLow, out double redHigh)
        {
            double drHeight = currentDrHigh - currentDrLow;
            double halfZone = drHeight * (MidRedZonePercent / 100.0);
            redLow = currentDrMid - halfZone;
            redHigh = currentDrMid + halfZone;
        }

        private void EvaluateDuoEntry()
        {
            if (EntryModeSetting != EntryMode.Duo)
                return;
            if (!hasActiveDR)
                return;
            if (Position.MarketPosition != MarketPosition.Flat)
                return;
            if (!sweepSetupActive)
                return;
            if (Time[0] <= sweepSetupStartTime || Time[0] > sweepSetupEndTime)
                return;
            if (!TimeInSession(Time[0]) || TimeInNoTradesAfter(Time[0]))
                return;
            if (CurrentBar < 2)
                return;

            bool c0Bull = Close[0] > Open[0];
            bool c1Bull = Close[1] > Open[1];
            bool c0Bear = Close[0] < Open[0];
            bool c1Bear = Close[1] < Open[1];

            if (sweepSetupDirection == SetupDirection.Long && !(c0Bull && c1Bull))
                return;
            if (sweepSetupDirection == SetupDirection.Short && !(c0Bear && c1Bear))
                return;

            double bodyHigh = Math.Max(Math.Max(Open[0], Close[0]), Math.Max(Open[1], Close[1]));
            double bodyLow = Math.Min(Math.Min(Open[0], Close[0]), Math.Min(Open[1], Close[1]));
            double combinedBody = Math.Abs(bodyHigh - bodyLow);

            if (DuoMinCombinedBodyPoints > 0 && combinedBody < DuoMinCombinedBodyPoints * TickSize)
                return;

            if (!IsFvgInTradeZone(sweepSetupDirection, bodyLow, bodyHigh))
                return;

            SetupDirection direction = sweepSetupDirection;
            double entry = UseLimitEntries ? GetDuoEntryPrice(direction, bodyLow, bodyHigh) : Close[0];
            if (UseLimitEntries)
            {
                if (direction == SetupDirection.Long && entry > Close[0])
                {
                    LogDebug(string.Format("Duo entry skipped (limit above price). Entry={0:F2} Close={1:F2}", entry, Close[0]));
                    return;
                }
                if (direction == SetupDirection.Short && entry < Close[0])
                {
                    LogDebug(string.Format("Duo entry skipped (limit below price). Entry={0:F2} Close={1:F2}", entry, Close[0]));
                    return;
                }
            }

            double drHeight = currentDrHigh - currentDrLow;
            if (drHeight <= 0)
                return;

            double tp = direction == SetupDirection.Long
                ? currentDrLow + drHeight * (TpPercentOfDr / 100.0)
                : currentDrHigh - drHeight * (TpPercentOfDr / 100.0);

            double wickHigh = Math.Max(High[0], High[1]);
            double wickLow = Math.Min(Low[0], Low[1]);
            double stop = direction == SetupDirection.Long ? wickLow : wickHigh;
            if (direction == SetupDirection.Long && stop >= entry)
                stop = entry - TickSize;
            else if (direction == SetupDirection.Short && stop <= entry)
                stop = entry + TickSize;
            string signal = direction == SetupDirection.Long ? "Sweeper_DuoLong" : "Sweeper_DuoShort";

            LogDebug(string.Format(
                "Duo trigger {0}. Entry={1:F2} TP={2:F2} SL={3:F2} Body[{4:F2}-{5:F2}]",
                direction == SetupDirection.Long ? "LONG" :"SHORT",
                entry,
                tp,
                stop,
                bodyLow,
                bodyHigh), true);

            activeEntrySignal = signal;
            activeEntryPrice = entry;
            activeStopPrice = stop;
            activeTpPrice = tp;
            trailingActive = false;

            SetStopLoss(signal, CalculationMode.Price, stop, false);
            SetProfitTarget(signal, CalculationMode.Price, tp);

            if (direction == SetupDirection.Long)
            {
                if (UseLimitEntries)
                    EnterLongLimit(entry, signal);
                else
                    EnterLong(signal);
            }
            else
            {
                if (UseLimitEntries)
                    EnterShortLimit(entry, signal);
                else
                    EnterShort(signal);
            }

            sweepSetupActive = false;
        }

        private double GetDuoEntryPrice(SetupDirection direction, double bodyLow, double bodyHigh)
        {
            double range = Math.Abs(bodyHigh - bodyLow);
            double pct = Math.Max(0, Math.Min(100, LimitEntryPercent)) / 100.0;
            if (direction == SetupDirection.Short)
                return bodyHigh - range * pct;
            return bodyLow + range * pct;
        }

        private bool IsFvgInTradeZone(SetupDirection direction, double fvgLower, double fvgUpper)
        {
            if (!hasActiveDR)
                return false;

            double redLow, redHigh;
            GetRedZone(out redLow, out redHigh);

            if (direction == SetupDirection.Short)
                return fvgUpper >= redHigh && fvgLower <= currentDrHigh;

            return fvgLower <= redLow && fvgUpper >= currentDrLow;
        }

        private double GetLimitEntryPrice(SetupDirection direction, double fvgLower, double fvgUpper)
        {
            double range = Math.Abs(fvgUpper - fvgLower);
            double pct = Math.Max(0, Math.Min(100, LimitEntryPercent)) / 100.0;
            if (direction == SetupDirection.Short)
                return fvgLower + range * pct;
            return fvgUpper - range * pct;
        }

        private bool TryFindRecentSwingHigh(int strength, out double swingHigh)
        {
            swingHigh = 0;
            if (CurrentBar < strength * 2 + 1)
                return false;

            int maxLookback = Math.Min(50, CurrentBar - strength);
            for (int barsAgo = 1; barsAgo <= maxLookback; barsAgo++)
            {
                if (IsSwingHighAt(barsAgo, strength))
                {
                    swingHigh = High[barsAgo];
                    return true;
                }
            }

            return false;
        }

        private bool TryFindRecentSwingLow(int strength, out double swingLow)
        {
            swingLow = 0;
            if (CurrentBar < strength * 2 + 1)
                return false;

            int maxLookback = Math.Min(50, CurrentBar - strength);
            for (int barsAgo = 1; barsAgo <= maxLookback; barsAgo++)
            {
                if (IsSwingLowAt(barsAgo, strength))
                {
                    swingLow = Low[barsAgo];
                    return true;
                }
            }

            return false;
        }

        private bool IsSwingHighAt(int barsAgo, int strength)
        {
            if (barsAgo < strength || CurrentBar - barsAgo < strength)
                return false;

            double pivot = High[barsAgo];
            for (int i = 1; i <= strength; i++)
            {
                if (pivot <= High[barsAgo - i] || pivot <= High[barsAgo + i])
                    return false;
            }
            return true;
        }

        private bool IsSwingLowAt(int barsAgo, int strength)
        {
            if (barsAgo < strength || CurrentBar - barsAgo < strength)
                return false;

            double pivot = Low[barsAgo];
            for (int i = 1; i <= strength; i++)
            {
                if (pivot >= Low[barsAgo - i] || pivot >= Low[barsAgo + i])
                    return false;
            }
            return true;
        }
        #endregion

        #region Trailing Stop
        private void UpdateTrailingStop()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
                return;
            if (CurrentBar < 2)
                return;
            if (string.IsNullOrEmpty(activeEntrySignal))
                return;

            if (!trailingActive)
            {
                bool triggerHit = Position.MarketPosition == MarketPosition.Long
                    ? High[0] >= currentDrMid
                    : Low[0] <= currentDrMid;
                if (triggerHit)
                {
                    trailingActive = true;
                    LogDebug(string.Format("Trailing activated at DR mid {0:F2}.", currentDrMid));
                }
            }

            if (!trailingActive)
                return;

            double newStop = Position.MarketPosition == MarketPosition.Long ? Low[1] : High[1];
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (newStop > activeStopPrice && newStop < Close[0])
                {
                    activeStopPrice = newStop;
                    SetStopLoss(activeEntrySignal, CalculationMode.Price, newStop, false);
                    LogDebug(string.Format("Trailing SL moved to {0:F2}", newStop));
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (newStop < activeStopPrice && newStop > Close[0])
                {
                    activeStopPrice = newStop;
                    SetStopLoss(activeEntrySignal, CalculationMode.Price, newStop, false);
                    LogDebug(string.Format("Trailing SL moved to {0:F2}", newStop));
                }
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
            currentDrStartTime = Times[drSeriesIndex][CurrentBar - currentDrStartBar];
            currentDrEndTime = Times[drSeriesIndex][0];

            drCounter++;
            currentDrBoxTag = "DRBox_" + drCounter;
            currentDrRedZoneTag = "DRRedZone_" + drCounter;
            currentDrMidLineTag = "DRMid_" + drCounter;
            currentDrTopLineTag = "DRTop_" + drCounter;
            currentDrBottomLineTag = "DRBot_" + drCounter;

            hasActiveDR = true;

            if (!ShowHistoricalDRs)
                ClearPreviousDrDrawings();

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
            currentDrEndTime = Times[drSeriesIndex][0];
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
            DrawCurrentDR(currentDrEndTime);
        }

        private void DrawCurrentDR(DateTime endTime)
        {
            if (!hasActiveDR)
                return;

            DateTime startTime = currentDrStartTime;
            if (startTime == Core.Globals.MinDate)
                startTime = Times[drSeriesIndex][0];

            Draw.Rectangle(
                this,
                currentDrBoxTag,
                false,
                startTime,
                currentDrLow,
                endTime,
                currentDrHigh,
                Brushes.Transparent,
                DrBoxBrush,
                BoxOpacity
            );

            DrawRedZone(startTime, endTime);

            Brush midLineBrush = GetLineBrush(DrMidLineBrush);
            Draw.Line(
                this,
                currentDrMidLineTag,
                false,
                startTime,
                currentDrMid,
                endTime,
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
                startTime,
                currentDrHigh,
                endTime,
                currentDrHigh,
                outlineLineBrush,
                DashStyleHelper.Solid,
                LineWidth
            );

            Draw.Line(
                this,
                currentDrBottomLineTag,
                false,
                startTime,
                currentDrLow,
                endTime,
                currentDrLow,
                outlineLineBrush,
                DashStyleHelper.Solid,
                LineWidth
            );
        }

        private void DrawRedZone(DateTime startTime, DateTime endTime)
        {
            if (MidRedZonePercent <= 0)
                return;

            double drHeight = currentDrHigh - currentDrLow;
            if (drHeight <= 0)
                return;

            double halfZone = drHeight * (MidRedZonePercent / 100.0);
            double zoneHigh = currentDrMid + halfZone;
            double zoneLow = currentDrMid - halfZone;

            Draw.Rectangle(
                this,
                currentDrRedZoneTag,
                false,
                startTime,
                zoneLow,
                endTime,
                zoneHigh,
                Brushes.Transparent,
                Brushes.Red,
                BoxOpacity
            );
        }

        private void ClearPreviousDrDrawings()
        {
            if (drCounter <= 1)
                return;

            int previousId = drCounter - 1;
            RemoveDrawObject("DRBox_" + previousId);
            RemoveDrawObject("DRRedZone_" + previousId);
            RemoveDrawObject("DRMid_" + previousId);
            RemoveDrawObject("DRTop_" + previousId);
            RemoveDrawObject("DRBot_" + previousId);
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
            LogDebug(message);
        }

        private void LogDebug(string message, bool newBlock = false)
        {
            if (!DebugLogging || string.IsNullOrEmpty(message))
                return;
            if (newBlock)
                Print(string.Empty);
            Print(string.Format("{0} - {1}", Time[0], message));
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.OrderState == OrderState.Filled && pendingSweepValid)
            {
                if (execution.Order.OrderAction == OrderAction.Buy || execution.Order.OrderAction == OrderAction.SellShort)
                {
                    LogDebug(string.Format(
                        "Entry fill detected. Action={0} OrderName={1} Time={2:yyyy-MM-dd HH:mm}",
                        execution.Order.OrderAction,
                        execution.Order.Name,
                        time));
                    pendingSweepFillTime = time;
                    pendingSweepDrawRequested = true;
                }
            }

            if (!DebugLogging)
                return;

            Print(string.Format(
                "{0} - Execution {1} {2} qty={3} price={4:F2} pos={5} order={6}",
                time,
                execution.Order.OrderState,
                execution.Order.Name,
                quantity,
                price,
                marketPosition,
                execution.Order.OrderId));
        }

        private void CancelAllOrders()
        {
            if (Account == null)
                return;

            foreach (Order order in Account.Orders)
            {
                if (order == null)
                    continue;
                if (order.Instrument != Instrument)
                    continue;

                if (order.OrderState == OrderState.Working ||
                    order.OrderState == OrderState.Submitted ||
                    order.OrderState == OrderState.Accepted ||
                    order.OrderState == OrderState.ChangePending)
                {
                    CancelOrder(order);
                }
            }
        }

        private void DrawSessionBackground()
        {
            if (CurrentBar < 1)
                return;

            DateTime barTime = Time[0];
            DateTime sessionStartTime = GetSessionStartTime(barTime);
            DateTime sessionEndTime = SessionStart > SessionEnd
                ? sessionStartTime.AddDays(1).Date + SessionEnd
                : sessionStartTime.Date + SessionEnd;

            string rectTag = "Sweeper_SessionFill_" + sessionStartTime.ToString("yyyyMMdd");
            if (DrawObjects[rectTag] == null)
            {
                Draw.Rectangle(
                    this,
                    rectTag,
                    false,
                    sessionStartTime,
                    0,
                    sessionEndTime,
                    30000,
                    Brushes.Transparent,
                    SessionBrush ?? Brushes.DarkSlateGray,
                    10
                ).ZOrder = -1;
            }

            var noTradesBrush = new SolidColorBrush(Color.FromArgb(70, 255, 0, 0));
            try
            {
                if (noTradesBrush.CanFreeze)
                    noTradesBrush.Freeze();
            }
            catch { }

            DateTime noTradesAfterTime = GetNoTradesAfterTime(sessionStartTime);

            Draw.VerticalLine(this, $"NoTradesAfter_{sessionStartTime:yyyyMMdd}", noTradesAfterTime, noTradesBrush,
                DashStyleHelper.Solid, 2);

            DrawSkipWindow("Skip", SkipStart, SkipEnd);
            DrawNewsWindows(barTime);
        }

        private void DrawNewsWindows(DateTime barTime)
        {
            if (!UseNewsSkip)
                return;

            EnsureNewsDatesInitialized();

            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime newsTime = NewsDates[i];
                if (newsTime.Date != barTime.Date)
                    continue;

                DateTime windowStart = newsTime.AddMinutes(-NewsSkipMinutesBefore);
                DateTime windowEnd = newsTime.AddMinutes(NewsSkipMinutesAfter);

                var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));
                var lineBrush = new SolidColorBrush(Color.FromArgb(90, 255, 0, 0));
                try
                {
                    if (areaBrush.CanFreeze)
                        areaBrush.Freeze();
                    if (lineBrush.CanFreeze)
                        lineBrush.Freeze();
                }
                catch { }

                string rectTag = $"Sweeper_News_Rect_{newsTime:yyyyMMdd_HHmm}";
                Draw.Rectangle(
                    this,
                    rectTag,
                    false,
                    windowStart,
                    0,
                    windowEnd,
                    30000,
                    lineBrush,
                    areaBrush,
                    2
                ).ZOrder = -1;

                string startTag = $"Sweeper_News_Start_{newsTime:yyyyMMdd_HHmm}";
                Draw.VerticalLine(this, startTag, windowStart, lineBrush, DashStyleHelper.DashDot, 2);

                string endTag = $"Sweeper_News_End_{newsTime:yyyyMMdd_HHmm}";
                Draw.VerticalLine(this, endTag, windowEnd, lineBrush, DashStyleHelper.DashDot, 2);
            }
        }

        private void DrawSkipWindow(string tagPrefix, TimeSpan start, TimeSpan end)
        {
            if (start == TimeSpan.Zero && end == TimeSpan.Zero)
                return;

            DateTime barTime = Time[0];
            DateTime windowStart = barTime.Date + start;
            DateTime windowEnd = barTime.Date + end;
            if (start > end)
                windowEnd = windowEnd.AddDays(1);

            var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));
            var lineBrush = new SolidColorBrush(Color.FromArgb(90, 255, 0, 0));
            try
            {
                if (areaBrush.CanFreeze)
                    areaBrush.Freeze();
                if (lineBrush.CanFreeze)
                    lineBrush.Freeze();
            }
            catch { }

            string rectTag = $"Sweeper_{tagPrefix}_Rect_{windowStart:yyyyMMdd}";
            Draw.Rectangle(
                this,
                rectTag,
                false,
                windowStart,
                0,
                windowEnd,
                30000,
                lineBrush,
                areaBrush,
                2
            ).ZOrder = -1;

            string startTag = $"Sweeper_{tagPrefix}_Start_{windowStart:yyyyMMdd}";
            Draw.VerticalLine(this, startTag, windowStart, lineBrush, DashStyleHelper.DashDot, 2);

            string endTag = $"Sweeper_{tagPrefix}_End_{windowEnd:yyyyMMdd}";
            Draw.VerticalLine(this, endTag, windowEnd, lineBrush, DashStyleHelper.DashDot, 2);
        }

        private DateTime GetSessionStartTime(DateTime barTime)
        {
            if (SessionStart <= SessionEnd)
                return barTime.Date + SessionStart;

            // Overnight session: if we're after midnight but before session end, start is previous day.
            if (barTime.TimeOfDay < SessionEnd)
                return barTime.Date.AddDays(-1) + SessionStart;

            return barTime.Date + SessionStart;
        }

        private DateTime GetNoTradesAfterTime(DateTime sessionStartTime)
        {
            DateTime noTradesAfterTime = sessionStartTime.Date + NoTradesAfter;
            if (SessionStart > SessionEnd && NoTradesAfter < SessionStart)
                noTradesAfterTime = noTradesAfterTime.AddDays(1);
            return noTradesAfterTime;
        }

        private bool TimeInSession(DateTime time)
        {
            TimeSpan now = time.TimeOfDay;

            if (SessionStart < SessionEnd)
                return now >= SessionStart && now < SessionEnd;

            return now >= SessionStart || now < SessionEnd;
        }

        private bool TimeInNoTradesAfter(DateTime time)
        {
            TimeSpan now = time.TimeOfDay;

            if (SessionStart < SessionEnd)
                return now >= NoTradesAfter && now < SessionEnd;

            // Overnight session
            if (NoTradesAfter >= SessionStart)
                return now >= NoTradesAfter || now < SessionEnd;

            return now >= NoTradesAfter && now < SessionEnd;
        }

        private bool TimeInSkip(DateTime time)
        {
            if (SkipStart == TimeSpan.Zero && SkipEnd == TimeSpan.Zero)
                return false;

            TimeSpan now = time.TimeOfDay;
            if (SkipStart < SkipEnd)
                return now >= SkipStart && now <= SkipEnd;
            return now >= SkipStart || now <= SkipEnd;
        }

        private void EnsureNewsDatesInitialized()
        {
            if (newsDatesInitialized)
                return;

            if (string.IsNullOrWhiteSpace(NewsDatesRaw))
            {
                newsDatesInitialized = true;
                return;
            }

            string[] entries = NewsDatesRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                string trimmed = entries[i].Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                DateTime parsed;
                if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd,HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
                {
                    NewsDates.Add(parsed);
                }
                else
                {
                    LogDebug(string.Format("Invalid news date entry: {0}", trimmed));
                }
            }

            newsDatesInitialized = true;
        }

        private bool TimeInNewsSkip(DateTime time)
        {
            if (!UseNewsSkip)
                return false;

            EnsureNewsDatesInitialized();

            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime newsTime = NewsDates[i];
                if (newsTime.Date != time.Date)
                    continue;

                DateTime windowStart = newsTime.AddMinutes(-NewsSkipMinutesBefore);
                DateTime windowEnd = newsTime.AddMinutes(NewsSkipMinutesAfter);
                if (time >= windowStart && time <= windowEnd)
                    return true;
            }

            return false;
        }
    }
}
