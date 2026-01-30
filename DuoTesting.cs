#region Using declarations
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Windows.Media;
    using System.ComponentModel.DataAnnotations;
    using System.Windows;
    using System.ComponentModel;
    using System.Xml.Serialization;
    using System.IO;
    using System.Globalization;
    using NinjaTrader.Cbi;
    using NinjaTrader.Data;
    using NinjaTrader.Gui;
    using NinjaTrader.Gui.Tools;
    using NinjaTrader.NinjaScript;
    using NinjaTrader.NinjaScript.Indicators;
    using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class DuoTesing : Strategy
    {
        public DuoTesing()
        {
            // VendorLicense(337);
        }

        [NinjaScriptProperty]
		[Display(Name = "Entry Confirmation", Description = "Show popup confirmation before each entry", Order = 2, GroupName = "Config")]
		public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Anti Hedge", Description = "Dont take trade in opposite direction to prevent hedging", Order = 3, GroupName = "Config")]
        public bool AntiHedge {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Webhook URL", Description = "Sends POST JSON to this URL on trade signals", Order = 4, GroupName = "Config")]
        public string WebhookUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reverse On Signal", Description = "If true, flatten current position when a reverse signal is generated, then place the new limit order", Order = 5, GroupName = "Config")]
        public bool ReverseOnSignal { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use iFVG Addon", Description = "Enable iFVG add-on override entries", Order = 6, GroupName = "Config")]
        public bool UseIfvgAddon { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London iFVG Min Size (Points)", Description = "Minimum iFVG size in price units", Order = 1, GroupName = "London iFVG Addon")]
        [Range(0, double.MaxValue)]
        public double London_IfvgMinSizePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London iFVG Max Size (Points)", Description = "Maximum iFVG size in price units (0 = no max)", Order = 2, GroupName = "London iFVG Addon")]
        [Range(0, double.MaxValue)]
        public double London_IfvgMaxSizePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London iFVG Min TP/SL Distance (Points)", Description = "Minimum distance between entry and pivot targets/stops", Order = 3, GroupName = "London iFVG Addon")]
        [Range(0, double.MaxValue)]
        public double London_IfvgMinTpSlDistancePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London iFVG Swing Strength", Description = "Bars on each side required to form a swing pivot", Order = 4, GroupName = "London iFVG Addon")]
        [Range(1, int.MaxValue)]
        public int London_IfvgSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London iFVG Swing Draw Bars", Description = "Number of bars to keep swing sweeps active", Order = 5, GroupName = "London iFVG Addon")]
        [Range(0, int.MaxValue)]
        public int London_IfvgSwingDrawBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London iFVG Max Bars Between Sweep And iFVG", Description = "Maximum bars allowed between the sweep and the iFVG invalidation", Order = 6, GroupName = "London iFVG Addon")]
        [Range(0, int.MaxValue)]
        public int London_IfvgMaxBarsBetweenSweepAndIfvg { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London Use iFVG Break-Even Wick", Description = "Enable break-even wick logic for iFVG", Order = 7, GroupName = "London iFVG Addon")]
        public bool London_IfvgUseBreakEvenWickLine { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London Use iFVG Volume SMA Filter", Description = "Require fast volume SMA to exceed slow SMA by a multiplier", Order = 8, GroupName = "London iFVG Addon")]
        public bool London_IfvgUseVolumeSmaFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London iFVG Volume SMA Fast Period", Description = "Fast period for the volume SMA filter", Order = 9, GroupName = "London iFVG Addon")]
        [Range(1, int.MaxValue)]
        public int London_IfvgVolumeFastSmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London iFVG Volume SMA Slow Period", Description = "Slow period for the volume SMA filter", Order = 10, GroupName = "London iFVG Addon")]
        [Range(1, int.MaxValue)]
        public int London_IfvgVolumeSlowSmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London iFVG Volume SMA Multiplier", Description = "Fast volume SMA must exceed slow SMA times this multiplier", Order = 11, GroupName = "London iFVG Addon")]
        [Range(0, double.MaxValue)]
        public double London_IfvgVolumeSmaMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York iFVG Min Size (Points)", Description = "Minimum iFVG size in price units", Order = 1, GroupName = "New York iFVG Addon")]
        [Range(0, double.MaxValue)]
        public double NewYork_IfvgMinSizePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York iFVG Max Size (Points)", Description = "Maximum iFVG size in price units (0 = no max)", Order = 2, GroupName = "New York iFVG Addon")]
        [Range(0, double.MaxValue)]
        public double NewYork_IfvgMaxSizePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York iFVG Min TP/SL Distance (Points)", Description = "Minimum distance between entry and pivot targets/stops", Order = 3, GroupName = "New York iFVG Addon")]
        [Range(0, double.MaxValue)]
        public double NewYork_IfvgMinTpSlDistancePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York iFVG Swing Strength", Description = "Bars on each side required to form a swing pivot", Order = 4, GroupName = "New York iFVG Addon")]
        [Range(1, int.MaxValue)]
        public int NewYork_IfvgSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York iFVG Swing Draw Bars", Description = "Number of bars to keep swing sweeps active", Order = 5, GroupName = "New York iFVG Addon")]
        [Range(0, int.MaxValue)]
        public int NewYork_IfvgSwingDrawBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York iFVG Max Bars Between Sweep And iFVG", Description = "Maximum bars allowed between the sweep and the iFVG invalidation", Order = 6, GroupName = "New York iFVG Addon")]
        [Range(0, int.MaxValue)]
        public int NewYork_IfvgMaxBarsBetweenSweepAndIfvg { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York Use iFVG Break-Even Wick", Description = "Enable break-even wick logic for iFVG", Order = 7, GroupName = "New York iFVG Addon")]
        public bool NewYork_IfvgUseBreakEvenWickLine { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York Use iFVG Volume SMA Filter", Description = "Require fast volume SMA to exceed slow SMA by a multiplier", Order = 8, GroupName = "New York iFVG Addon")]
        public bool NewYork_IfvgUseVolumeSmaFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York iFVG Volume SMA Fast Period", Description = "Fast period for the volume SMA filter", Order = 9, GroupName = "New York iFVG Addon")]
        [Range(1, int.MaxValue)]
        public int NewYork_IfvgVolumeFastSmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York iFVG Volume SMA Slow Period", Description = "Slow period for the volume SMA filter", Order = 10, GroupName = "New York iFVG Addon")]
        [Range(1, int.MaxValue)]
        public int NewYork_IfvgVolumeSlowSmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York iFVG Volume SMA Multiplier", Description = "Fast volume SMA must exceed slow SMA times this multiplier", Order = 11, GroupName = "New York iFVG Addon")]
        [Range(0, double.MaxValue)]
        public double NewYork_IfvgVolumeSmaMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "iFVG Debug Logging", Description = "Enable iFVG add-on debug logs", Order = 14, GroupName = "iFVG Addon")]
        public bool IfvgDebugLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "iFVG Verbose Logging", Description = "Enable iFVG add-on verbose logs", Order = 15, GroupName = "iFVG Addon")]
        public bool IfvgVerboseDebugLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum 1st+2nd Candle Body", GroupName = "London Parameters", Order = 1)]
        public double London_MinC12Body { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum Candle Body (Points)", Description = "Minimum body size per candle for the two entry candles", GroupName = "London Parameters", Order = 2)]
        [Range(0, double.MaxValue)]
        public double London_MinCandlePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Maximum 1st+2nd Candle Body", GroupName = "London Parameters", Order = 2)]
        public double London_MaxC12Body { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Offset % of 1st+2nd Candle Body", GroupName = "London Parameters", Order = 5)]
        public double London_OffsetPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit % of 1st+2nd Candle Body", GroupName = "London Parameters", Order = 6)]
        public double London_TpPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cancel Order % of 1st+2nd Candle Body", GroupName = "London Parameters", Order = 7)]
        public double London_CancelPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Number of Contracts", GroupName = "London Parameters", Order = 0)]
        [Range(1, int.MaxValue)]
        public int London_Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Deviation %", Description = "Max random deviation applied to entry/TP %", Order = 8, GroupName = "London Parameters")]
        [Range(0, double.MaxValue)]
        public double London_DeviationPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL Padding", Description = "Extra padding added to stop loss (in price units, 0.25 increments)", GroupName = "London Parameters", Order = 9)]
        [Range(0, double.MaxValue)]
        public double London_SLPadding { get; set; }

		[NinjaScriptProperty]
        [Display(Name = "Max SL/TP Ratio %", Description = "Skip trades if SL is more than this % of TP (e.g., 200 = SL can be at most 2x TP)", Order = 10, GroupName = "London Parameters")]
        [Range(0, 1000)]
        public double London_MaxSLTPRatioPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL type", Description = "Select the sl type you want", Order = 11, GroupName = "London Parameters")]
        public SLPreset London_SLPresetSetting { get; set; }

		[NinjaScriptProperty]
        [Display(Name = "SL % of 1st Candle", Description = "0% = High of 1st candle (long), 100% = Low of 1st candle (long)", Order = 12, GroupName = "London Parameters")]
        [Range(0, 100)]
        public double London_SLPercentFirstCandle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max SL (Points)", Description = "Maximum allowed stop loss in points. 0 = Disabled", Order = 13, GroupName = "London Parameters")]
        public double London_MaxSLPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max Session Gain (Points)", Description = "Pause trading for the rest of the London session after this realized gain in points. 0 = disabled.", Order = 14, GroupName = "London Parameters")]
        public double London_MaxSessionGain { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Market Entry", Description = "If true, entries use market orders instead of limit orders for London session", Order = 15, GroupName = "London Parameters")]
        public bool London_UseMarketEntry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "When session is starting", Order = 1, GroupName = "London Time")]
        public TimeSpan London_SessionStart
        {
            get {
                return sessionStart;
            }
            set {
                sessionStart = new TimeSpan(value.Hours, value.Minutes, 0);
            }
        }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "When session is ending, all positions and orders will be canceled when this time is passed", Order = 2, GroupName = "London Time")]
        public TimeSpan London_SessionEnd
        {
            get {
                return sessionEnd;
            }
            set {
                sessionEnd = new TimeSpan(value.Hours, value.Minutes, 0);
            }
        }

		[NinjaScriptProperty]
		[Display(Name = "No Trades After", Description = "No more orders is being placed between this time and session end,", Order = 3, GroupName = "London Time")]
		public TimeSpan London_NoTradesAfter
		{
			get {
				return noTradesAfter;
			}
			set {
				noTradesAfter = new TimeSpan(value.Hours, value.Minutes, 0);
			}
		}

        [NinjaScriptProperty]
        [Display(Name = "Minimum 1st+2nd Candle Body", GroupName = "New York Parameters", Order = 1)]
        public double NewYork_MinC12Body { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum Candle Body (Points)", Description = "Minimum body size per candle for the two entry candles", GroupName = "New York Parameters", Order = 2)]
        [Range(0, double.MaxValue)]
        public double NewYork_MinCandlePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Maximum 1st+2nd Candle Body", GroupName = "New York Parameters", Order = 2)]
        public double NewYork_MaxC12Body { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Offset % of 1st+2nd Candle Body", GroupName = "New York Parameters", Order = 5)]
        public double NewYork_OffsetPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit % of 1st+2nd Candle Body", GroupName = "New York Parameters", Order = 6)]
        public double NewYork_TpPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cancel Order % of 1st+2nd Candle Body", GroupName = "New York Parameters", Order = 7)]
        public double NewYork_CancelPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Number of Contracts", GroupName = "New York Parameters", Order = 0)]
        [Range(1, int.MaxValue)]
        public int NewYork_Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Deviation %", Description = "Max random deviation applied to entry/TP %", Order = 8, GroupName = "New York Parameters")]
        [Range(0, double.MaxValue)]
        public double NewYork_DeviationPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL Padding", Description = "Extra padding added to stop loss (in price units, 0.25 increments)", GroupName = "New York Parameters", Order = 9)]
        [Range(0, double.MaxValue)]
        public double NewYork_SLPadding { get; set; }

		[NinjaScriptProperty]
        [Display(Name = "Max SL/TP Ratio %", Description = "Skip trades if SL is more than this % of TP (e.g., 200 = SL can be at most 2x TP)", Order = 10, GroupName = "New York Parameters")]
        [Range(0, 1000)]
        public double NewYork_MaxSLTPRatioPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL type", Description = "Select the sl type you want", Order = 11, GroupName = "New York Parameters")]
        public SLPreset NewYork_SLPresetSetting { get; set; }

		[NinjaScriptProperty]
        [Display(Name = "SL % of 1st Candle", Description = "0% = High of 1st candle (long), 100% = Low of 1st candle (long)", Order = 12, GroupName = "New York Parameters")]
        [Range(0, 100)]
        public double NewYork_SLPercentFirstCandle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max SL (Points)", Description = "Maximum allowed stop loss in points. 0 = Disabled", Order = 13, GroupName = "New York Parameters")]
        public double NewYork_MaxSLPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max Session Gain (Points)", Description = "Pause trading for the rest of the New York session after this realized gain in points. 0 = disabled.", Order = 14, GroupName = "New York Parameters")]
        public double NewYork_MaxSessionGain { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Market Entry", Description = "If true, entries use market orders instead of limit orders for New York session", Order = 15, GroupName = "New York Parameters")]
        public bool NewYork_UseMarketEntry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten On Max Session Gain", Description = "If true, flatten open positions when max session gain is reached.", Order = 1, GroupName = "Session Limits")]
        public bool FlattenOnMaxSessionGain { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "When session is starting", Order = 1, GroupName = "New York Time")]
        public TimeSpan NewYork_SessionStart
        {
            get {
                return session2SessionStart;
            }
            set {
                session2SessionStart = new TimeSpan(value.Hours, value.Minutes, 0);
            }
        }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "When session is ending, all positions and orders will be canceled when this time is passed", Order = 2, GroupName = "New York Time")]
        public TimeSpan NewYork_SessionEnd
        {
            get {
                return session2SessionEnd;
            }
            set {
                session2SessionEnd = new TimeSpan(value.Hours, value.Minutes, 0);
            }
        }

		[NinjaScriptProperty]
		[Display(Name = "No Trades After", Description = "No more orders is being placed between this time and session end,", Order = 3, GroupName = "New York Time")]
		public TimeSpan NewYork_NoTradesAfter
		{
			get {
				return session2NoTradesAfter;
			}
			set {
				session2NoTradesAfter = new TimeSpan(value.Hours, value.Minutes, 0);
			}
		}

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Fill", Description = "Color of the session background", Order = 4, GroupName = "Sessions")]
        public Brush SessionBrush { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade London (1:30-5:30)", Description = "Allow trading during London", Order = 1, GroupName = "Sessions")]
        public bool UseSession1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade New York (9:40-15:00)", Description = "Allow trading during New York", Order = 2, GroupName = "Sessions")]
        public bool UseSession2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Auto Shift London", Description = "Apply London DST auto-shift to London times", Order = 3, GroupName = "Sessions")]
        public bool AutoShiftSession1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Auto Shift NewYork", Description = "Apply London DST auto-shift to New York times", Order = 4, GroupName = "Sessions")]
        public bool AutoShiftSession2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close at Session End", Description = "If true, open trades will be closed and working orders canceled at session end", Order = 5, GroupName = "Sessions")]
        public bool CloseAtSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start", Description = "Start of skip window", Order = 1, GroupName = "London Skip Times")]
        public TimeSpan London_SkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End", Description = "End of skip window", Order = 2, GroupName = "London Skip Times")]
        public TimeSpan London_SkipEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start 2", Description = "Start of 2nd skip window", Order = 3, GroupName = "London Skip Times")]
        public TimeSpan London_Skip2Start { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End 2", Description = "End of 2nd skip window", Order = 4, GroupName = "London Skip Times")]
        public TimeSpan London_Skip2End { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start", Description = "Start of skip window", Order = 1, GroupName = "New York Skip Times")]
        public TimeSpan NewYork_SkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End", Description = "End of skip window", Order = 2, GroupName = "New York Skip Times")]
        public TimeSpan NewYork_SkipEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start 2", Description = "Start of 2nd skip window", Order = 3, GroupName = "New York Skip Times")]
        public TimeSpan NewYork_Skip2Start { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End 2", Description = "End of 2nd skip window", Order = 4, GroupName = "New York Skip Times")]
        public TimeSpan NewYork_Skip2End { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Force Close at Skip Start", Description = "If true, flatten/cancel as soon as a skip window begins", Order = 5, GroupName = "Skip Times")]
        public bool ForceCloseAtSkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use FOMC Skip Filter", Description = "Skip trading during FOMC window on listed dates", Order = 6, GroupName = "Skip Times")]
        public bool UseFomcSkip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FOMC Skip Start", Description = "Start of FOMC skip window (chart time)", Order = 7, GroupName = "Skip Times")]
        public TimeSpan FomcSkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FOMC Skip End", Description = "End of FOMC skip window (chart time)", Order = 8, GroupName = "Skip Times")]
        public TimeSpan FomcSkipEnd { get; set; }

        // State tracking
        private bool longOrderPlaced = false;
        private bool shortOrderPlaced = false;
        private Order longEntryOrder = null;
        private Order shortEntryOrder = null;
        private TimeSpan sessionStart = new TimeSpan(9, 40, 0);
        private TimeSpan sessionEnd = new TimeSpan(15, 00, 0);  
		private TimeSpan noTradesAfter = new TimeSpan(14, 30, 0);
        private TimeSpan session2SessionStart = new TimeSpan(9, 40, 0);
        private TimeSpan session2SessionEnd = new TimeSpan(15, 00, 0);
        private TimeSpan session2NoTradesAfter = new TimeSpan(14, 30, 0);
        private TimeSpan skipStart = new TimeSpan(00, 00, 0);
        private TimeSpan skipEnd = new TimeSpan(00, 00, 0);
        private TimeSpan skip2Start = new TimeSpan(00, 00, 0);
        private TimeSpan skip2End = new TimeSpan(00, 00, 0); 
        private int skipBarUntil = -1;
        private int lastBarProcessed = -1;
        private double currentLongTP = 0;
        private double currentShortTP = 0;
        private double currentLongEntry = 0;
        private double currentShortEntry = 0;
        private double currentLongSL = 0;
        private double currentShortSL = 0;
        private bool First_Candle_High_Low = true;
        private bool First_Candle_Open = false;
        private bool Second_Candle_High_Low = false;
        private bool Second_Candle_Open= false;
        private double currentLongCancelPrice = 0;
        private double currentShortCancelPrice = 0;
        private Random rng;
        private string displayText = "Waiting...";
        private bool sessionClosed = false;
        private bool debug = true;
        private int longSignalBar = -1;
        private int shortSignalBar = -1;
        private bool longLinesActive = false;
        private bool shortLinesActive = false;
        private int longExitBar = -1;
        private int shortExitBar = -1;
        private SessionSlot activeSession = SessionSlot.None;
        private bool sessionInitialized;
        private DateTime effectiveTimesDate = DateTime.MinValue;
        private TimeSpan effectiveSessionStart;
        private TimeSpan effectiveSessionEnd;
        private TimeSpan effectiveNoTradesAfter;
        private TimeSpan effectiveSkipStart;
        private TimeSpan effectiveSkipEnd;
        private TimeSpan effectiveSkip2Start;
        private TimeSpan effectiveSkip2End;
        private bool activeAutoShiftTimes;
        private double activeMinC12Body;
        private double activeMaxC12Body;
        private double activeMinCandlePoints;
        private double activeOffsetPerc;
        private double activeTpPerc;
        private double activeCancelPerc;
        private double activeDeviationPerc;
        private double activeSLPadding;
        private double activeMaxSLTPRatioPerc;
        private SLPreset activeSLPresetSetting;
        private double activeSLPercentFirstCandle;
        private double activeMaxSLPoints;
        private double activeMaxSessionGain;
        private int activeContracts;
        private TimeSpan activeSessionStart;
        private TimeSpan activeSessionEnd;
        private TimeSpan activeNoTradesAfter;
        private TimeSpan activeSkipStart;
        private TimeSpan activeSkipEnd;
        private TimeSpan activeSkip2Start;
        private TimeSpan activeSkip2End;
        private bool activeUseMarketEntry;
		private TimeZoneInfo targetTimeZone;
		private TimeZoneInfo londonTimeZone;
        private int lineTagCounter = 0;
        private string longLineTagPrefix = "LongLine_0_";
        private string shortLineTagPrefix = "ShortLine_0_";
        private const string FomcDatesRaw = 
            "2025-01-29\n" +
            "2025-03-19\n" +
            "2025-05-07\n" +
            "2025-06-18\n" +
            "2025-07-30\n" +
            "2025-09-17\n" +
            "2025-10-29\n" +
            "2025-12-10\n" +
            "2026-01-28\n" +
            "2026-03-18\n" +
            "2026-04-29\n" +
            "2026-06-17\n" +
            "2026-07-29\n" +
            "2026-09-16\n" +
            "2026-10-28\n" +
            "2026-12-09";
        private HashSet<DateTime> fomcDates;
        private bool fomcDatesInitialized;
        private TimeZoneInfo easternTimeZone;
        // --- Webhook state (send entry only after order Accepted/Working) ---
        private bool pendingLongWebhook;
        private double pendingLongEntry;
        private double pendingLongTP;
        private double pendingLongSL;
        private bool pendingShortWebhook;
        private double pendingShortEntry;
        private double pendingShortTP;
        private double pendingShortSL;
        private string lastLongWebhookOrderId;
        private string lastShortWebhookOrderId;
        private double lastLongWebhookEntry;
        private double lastLongWebhookTP;
        private double lastLongWebhookSL;
        private double lastShortWebhookEntry;
        private double lastShortWebhookTP;
        private double lastShortWebhookSL;
        private int lastCancelWebhookBar = -1;
        private int lastExitWebhookBar = -1;
        private string lastCancelWebhookOrderId;
        private double lastCancelWebhookLimitPrice;
        private string lastExitWebhookExecutionId;

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
        private double sessionStartCumProfit;
        private bool sessionGainLimitReached;
        private List<IfvgBox> ifvgActiveFvgs;
        private List<IfvgBox> ifvgBreakEvenFvgs;
        private List<IfvgSwingLine> ifvgSwingLines;
        private int ifvgCounter;
        private bool ifvgOverrideActive;
        private IfvgDirection? ifvgActiveDirection;
        private double ifvgEntryLower;
        private double ifvgEntryUpper;
        private int ifvgLastEntryBar = -1;
        private SMA ifvgVolumeFastSmaLondon;
        private SMA ifvgVolumeSlowSmaLondon;
        private SMA ifvgVolumeFastSmaNewYork;
        private SMA ifvgVolumeSlowSmaNewYork;
        private SMA ifvgVolumeFastSmaActive;
        private SMA ifvgVolumeSlowSmaActive;
        private IfvgSweepEvent ifvgLastSwingSweep;
        private bool ifvgBreakEvenActive;
        private bool ifvgBreakEvenTriggered;
        private double ifvgBreakEvenPrice;
        private IfvgDirection? ifvgBreakEvenDirection;
        private string ifvgActiveTradeTag;
        private string ifvgLastTradeLabelPrinted;
        private double activeIfvgMinSizePoints;
        private double activeIfvgMaxSizePoints;
        private double activeIfvgMinTpSlDistancePoints;
        private int activeIfvgSwingStrength;
        private int activeIfvgSwingDrawBars;
        private int activeIfvgMaxBarsBetweenSweepAndIfvg;
        private bool activeIfvgUseBreakEvenWickLine;
        private bool activeIfvgUseVolumeSmaFilter;
        private int activeIfvgVolumeFastSmaPeriod;
        private int activeIfvgVolumeSlowSmaPeriod;
        private double activeIfvgVolumeSmaMultiplier;
        private const string IfvgLongSignalName = "iFVG_Long";
        private const string IfvgShortSignalName = "iFVG_Short";

        private class IfvgBox
        {
            public string Tag;
            public int StartBarIndex;
            public int EndBarIndex;
            public int CreatedBarIndex;
            public double Upper;
            public double Lower;
            public bool IsBullish;
            public bool IsActive;
        }

        private class IfvgSwingLine
        {
            public string Tag;
            public double Price;
            public int StartBarIndex;
            public int EndBarIndex;
            public bool IsActive;
            public bool IsHigh;
        }

        private class IfvgSweepEvent
        {
            public IfvgDirection Direction;
            public double Price;
            public int BarIndex;
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DuoTesting";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsInstantiatedOnEachOptimizationIteration = false;

                // London
                London_Contracts     = 2;
                London_MinC12Body  = 13;
                London_MinCandlePoints = 0;
                London_MaxC12Body  = 112.5;
                London_OffsetPerc  = 5.7;
                London_TpPerc      = 31.7;
                London_CancelPerc  = 180.5;
                London_DeviationPerc = 0;
                London_SLPadding = 0;
                London_MaxSLTPRatioPerc = 445;
                London_SLPresetSetting = SLPreset.First_Candle_High_Low;
                London_SLPercentFirstCandle = 100;
                London_MaxSLPoints = 112;
                London_MaxSessionGain = 160;
                London_UseMarketEntry = false;
                London_SessionStart  = new TimeSpan(1, 30, 0);
                London_SessionEnd    = new TimeSpan(5, 30, 0);
                London_NoTradesAfter = new TimeSpan(5, 00, 0);
                London_SkipStart     = new TimeSpan(0, 0, 0);
                London_SkipEnd       = new TimeSpan(0, 0, 0);
                London_Skip2Start     = new TimeSpan(0, 0, 0);
                London_Skip2End       = new TimeSpan(0, 0, 0);
                // New York
                NewYork_Contracts     = 1;
                NewYork_MinC12Body  = 19;
                NewYork_MinCandlePoints = 0.75;
                NewYork_MaxC12Body  = 132;
                NewYork_OffsetPerc  = 0.2;
                NewYork_TpPerc      = 28.3;
                NewYork_CancelPerc  = 114.5;
                NewYork_DeviationPerc = 0;
                NewYork_SLPadding = 0;
                NewYork_MaxSLTPRatioPerc = 479;
                NewYork_SLPresetSetting = SLPreset.First_Candle_High_Low;
                NewYork_SLPercentFirstCandle = 100;
                NewYork_MaxSLPoints = 139;
                NewYork_MaxSessionGain = 145;
                NewYork_UseMarketEntry = false;
                NewYork_SessionStart = new TimeSpan(9, 40, 0);
                NewYork_SessionEnd = new TimeSpan(15, 00, 0);
                NewYork_NoTradesAfter = new TimeSpan(14, 30, 0);
                NewYork_SkipStart = new TimeSpan(11, 45, 0);
                NewYork_SkipEnd = new TimeSpan(13, 15, 0);
                NewYork_Skip2Start = new TimeSpan(0, 0, 0);
                NewYork_Skip2End = new TimeSpan(0, 0, 0);
                // Other defaults
                SessionBrush  = Brushes.Gold;
                CloseAtSessionEnd = true;
                UseSession1 = true;
                UseSession2 = true;
                AutoShiftSession1 = true;
                AutoShiftSession2 = false;
                ForceCloseAtSkipStart = true;
                UseFomcSkip = true;
                FomcSkipStart = new TimeSpan(14, 5, 0);
                FomcSkipEnd = new TimeSpan(14, 10, 0);
                RequireEntryConfirmation = false;
                AntiHedge = false;
                WebhookUrl = "";
                ReverseOnSignal = true;
                FlattenOnMaxSessionGain = false;
                UseIfvgAddon = false;
                London_IfvgMinSizePoints = 10;
                London_IfvgMaxSizePoints = 50;
                London_IfvgMinTpSlDistancePoints = 5;
                London_IfvgSwingStrength = 10;
                London_IfvgSwingDrawBars = 200;
                London_IfvgMaxBarsBetweenSweepAndIfvg = 20;
                London_IfvgUseBreakEvenWickLine = false;
                London_IfvgUseVolumeSmaFilter = false;
                London_IfvgVolumeFastSmaPeriod = 9;
                London_IfvgVolumeSlowSmaPeriod = 21;
                London_IfvgVolumeSmaMultiplier = 1;
                NewYork_IfvgMinSizePoints = 10;
                NewYork_IfvgMaxSizePoints = 50;
                NewYork_IfvgMinTpSlDistancePoints = 5;
                NewYork_IfvgSwingStrength = 10;
                NewYork_IfvgSwingDrawBars = 200;
                NewYork_IfvgMaxBarsBetweenSweepAndIfvg = 20;
                NewYork_IfvgUseBreakEvenWickLine = false;
                NewYork_IfvgUseVolumeSmaFilter = false;
                NewYork_IfvgVolumeFastSmaPeriod = 9;
                NewYork_IfvgVolumeSlowSmaPeriod = 21;
                NewYork_IfvgVolumeSmaMultiplier = 1;
                IfvgDebugLogging = true;
                IfvgVerboseDebugLogging = false;
            }
            else if (State == State.DataLoaded)
            {
                // heartbeatId = BuildHeartbeatId();
                // //--- Heartbeat timer setup ---
                // heartbeatTimer = new System.Timers.Timer(heartbeatIntervalSeconds * 1000);
                // heartbeatTimer.Elapsed += (s, e) => WriteHeartbeat();
                // heartbeatTimer.AutoReset = true;
                // heartbeatTimer.Start();

                ApplyStopLossPreset(London_SLPresetSetting);
                ifvgActiveFvgs = new List<IfvgBox>();
                ifvgBreakEvenFvgs = new List<IfvgBox>();
                ifvgSwingLines = new List<IfvgSwingLine>();
                ifvgCounter = 0;
                ifvgOverrideActive = false;
                ifvgActiveDirection = null;
                ifvgEntryLower = 0;
                ifvgEntryUpper = 0;
                ifvgVolumeFastSmaLondon = SMA(Volume, London_IfvgVolumeFastSmaPeriod);
                ifvgVolumeSlowSmaLondon = SMA(Volume, London_IfvgVolumeSlowSmaPeriod);
                ifvgVolumeFastSmaNewYork = SMA(Volume, NewYork_IfvgVolumeFastSmaPeriod);
                ifvgVolumeSlowSmaNewYork = SMA(Volume, NewYork_IfvgVolumeSlowSmaPeriod);
            }
            else if (State == State.Configure)
            {
                rng = new Random();
            }
            else if (State == State.Terminated)
            {
                //--- Clean up heartbeat timer ---
                // if (heartbeatTimer != null)
                // {
                //     heartbeatTimer.Stop();
                //     heartbeatTimer.Dispose();
                //     heartbeatTimer = null;
                // }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
                return;

			EnsureActiveSession(Time[0]);
			EnsureEffectiveTimes(Time[0], false);
			TimeSpan noTradesAfter = effectiveNoTradesAfter;
			
			// Reset sessionClosed when a new session starts
			if (IsFirstTickOfBar && TimeInSession(Time[0]) && sessionClosed)
			{
			    sessionClosed = false;
                sessionStartCumProfit = GetTotalCumProfitPoints();
                sessionGainLimitReached = false;
			
			    longOrderPlaced = false;
			    shortOrderPlaced = false;
			    longEntryOrder = null;
			    shortEntryOrder = null;
			
			    currentLongEntry = 0;
			    currentShortEntry = 0;
			    currentLongTP = 0;
			    currentShortTP = 0;
                currentLongSL = 0;
			    currentShortSL = 0;
			    currentLongCancelPrice = 0;
			    currentShortCancelPrice = 0;
			
			    skipBarUntil = -1;
			    lastBarProcessed = -1;
                longSignalBar = -1;
                shortSignalBar = -1;
                longLinesActive = false;
                shortLinesActive = false;
                longExitBar = -1;
                shortExitBar = -1;
            
                displayText = "Waiting...";

                if (UseIfvgAddon && ifvgActiveFvgs != null)
                {
                    ifvgActiveFvgs.Clear();
                    if (ifvgBreakEvenFvgs != null)
                        ifvgBreakEvenFvgs.Clear();
                    if (ifvgSwingLines != null)
                        ifvgSwingLines.Clear();
                    ifvgCounter = 0;
                    ifvgOverrideActive = false;
                    ifvgActiveDirection = null;
                    ifvgEntryLower = 0;
                    ifvgEntryUpper = 0;
                    ifvgLastSwingSweep = null;
                    ifvgBreakEvenActive = false;
                    ifvgBreakEvenTriggered = false;
                    ifvgBreakEvenPrice = 0;
                    ifvgBreakEvenDirection = null;
                    ifvgActiveTradeTag = null;
                    ifvgLastTradeLabelPrinted = null;
                }
            
                Print($"{Time[0]} - New session started, state reset.");
				}

            DrawSessionBackground();
            UpdateInfo();

			// === Skip window cross detection ===
			bool crossedSkipWindow =
				(!TimeInSkip(Time[1]) && TimeInSkip(Time[0]))   // just entered a skip window
				|| (TimeInSkip(Time[1]) && !TimeInSkip(Time[0])); // just exited a skip window

			if (crossedSkipWindow)
			{
				if (TimeInSkip(Time[0]))
				{
					// 🔹 We just entered a skip window
                    if (debug)
					    Print($"{Time[0]} - ⛔ Entered skip window");

                    if (ForceCloseAtSkipStart)
                    {
					    if (Position.MarketPosition != MarketPosition.Flat)
					    {
						    Flatten("SkipWindow");
					    }
					    else
					    {
						    CancelOrder(shortEntryOrder);
						    CancelOrder(longEntryOrder);
	                            SendWebhookCancelSafe();
					    }
	                        longLinesActive = false;
	                        shortLinesActive = false;
	                        longSignalBar = -1;
	                        shortSignalBar = -1;
                        longExitBar = -1;
                        shortExitBar = -1;
                    }
				}
				else
				{
					// 🔹 We just exited the skip window
                    if (debug)
					    Print($"{Time[0]} - ✅ Exited skip window");
				}
			}


			// === Session check ===
			bool crossedSessionEnd = CrossedSessionEnd(Time[1], Time[0]);
            
            bool isLastBarOfTradingSession = Bars.IsLastBarOfSession && !sessionClosed;
            
			if (crossedSessionEnd || isLastBarOfTradingSession)
            {
                if (CloseAtSessionEnd)
                {
                    if (Position.MarketPosition != MarketPosition.Flat)
                    {
                        // Case 1: In a position -> flatten (Managed Mode removes TP/SL too)
                        Flatten("SessionEnd");
                    }
                    else
                    {
                        // Case 2: No position but entry orders active -> cancel only entries
                        //CancelEntryOrders("SessionEnd");
							CancelOrder(shortEntryOrder);
							CancelOrder(longEntryOrder);
	                        SendWebhookCancelSafe();
	                    }
	                }
	                else // CloseAtSessionEnd == false
	                {
                    if (Position.MarketPosition == MarketPosition.Flat)
                    {
                        // Case 4: No position -> cancel only entries
                        //CancelEntryOrders("SessionEnd (CloseAtSessionEnd=false)");
							CancelOrder(shortEntryOrder);
							CancelOrder(longEntryOrder);
	                        SendWebhookCancelSafe();
	                    }
	                    // Case 3: In a position -> do nothing, let TP/SL handle it
	                }

                longLinesActive = false;
                shortLinesActive = false;
                longSignalBar = -1;
                shortSignalBar = -1;
                longExitBar = -1;
                shortExitBar = -1;

                sessionClosed = true;
            }

			// === No Trades After check ===
			bool crossedNoTradesAfter = 
				activeSession != SessionSlot.None
				&& (Time[1].TimeOfDay <= noTradesAfter && Time[0].TimeOfDay > noTradesAfter);

			if (crossedNoTradesAfter)
			{
                if (debug)
				    Print($"{Time[0]} - ⛔ NoTradesAfter time crossed — canceling entry orders");
					CancelOrder(shortEntryOrder);
					CancelOrder(longEntryOrder);
	                SendWebhookCancelSafe();
				}

            // 🔒 HARD GUARD: absolutely no logic inside skip windows
            if (TimeInSkip(Time[0]))
                return;

            // 🔒 HARD GUARD: absolutely no new logic/entries outside session
            if (!TimeInSession(Time[0]))
                return;

            // ✅ Always keep preview lines updating while in session
            UpdatePreviewLines();

            if (UseIfvgAddon)
                UpdateIfvgAddon();

            // 🔒 HARD GUARD: no entries after London_NoTradesAfter (but keep lines)
            if (TimeInNoTradesAfter(Time[0])) {
				CancelEntryIfAfterNoTrades();
				return;
			}

            if (HasReachedSessionGainLimit())
            {
                CancelOrder(shortEntryOrder);
                CancelOrder(longEntryOrder);
                SendWebhookCancelSafe();
                return;
            }

            if (UseIfvgAddon && ifvgOverrideActive)
                return;

            // 🔒 HARD GUARD: no signal/order logic while a position is open
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            // === Long Cancel: TP hit before entry === 
            if (longEntryOrder != null && longEntryOrder.OrderState == OrderState.Working)
            {
                if (High[0] >= currentLongCancelPrice && Low[0] > currentLongEntry)
                //if (High[0] >= currentLongTP && Low[0] > currentLongEntry)
                {
	                    if (debug)
	                        Print($"{Time[0]} - 🚫 Long cancel price hit before entry fill. Canceling order.");
	                    CancelOrder(longEntryOrder);
	                    SendWebhookCancelSafe(longEntryOrder);
	                    longOrderPlaced = false;
	                    longEntryOrder = null;

                    int longCancelStartBarsAgo = longSignalBar >= 0 ? CurrentBar - longSignalBar : 1;

                    Draw.Line(this, longLineTagPrefix + "LongEntryLineActive", false,
                        longCancelStartBarsAgo, currentLongEntry,
                        0, currentLongEntry,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, longLineTagPrefix + "LongTPLineActive", false,
                        longCancelStartBarsAgo, currentLongTP,
                        0, currentLongTP,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, longLineTagPrefix + "LongSLLineActive", false,
                        longCancelStartBarsAgo, currentLongSL,
                        0, currentLongSL,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, longLineTagPrefix + "LongCancelLineActive", false,
                        longCancelStartBarsAgo, currentLongCancelPrice,
                        0, currentLongCancelPrice,
                        Brushes.Gray, DashStyleHelper.Dot, 2);

                    longLinesActive = false;
                    longSignalBar = -1;
                    longExitBar = -1;

                    skipBarUntil = CurrentBar;
                    if (debug)
                        Print($"{Time[0]} - ➡️ Skipping signals until bar > {skipBarUntil}");
                }
            }

            // === Short Cancel: TP hit before entry ===
            if (shortEntryOrder != null && shortEntryOrder.OrderState == OrderState.Working)
            {
                if (Low[0] <= currentShortCancelPrice && High[0] < currentShortEntry)
                //if (Low[0] <= currentShortTP && High[0] < currentShortEntry)
                {
	                    if (debug)
	                        Print($"{Time[0]} - 🚫 Short cancel price hit before entry fill. Canceling order.");
	                    CancelOrder(shortEntryOrder);
	                    SendWebhookCancelSafe(shortEntryOrder);
	                    shortOrderPlaced = false;
	                    shortEntryOrder = null;

                    int shortCancelStartBarsAgo = shortSignalBar >= 0 ? CurrentBar - shortSignalBar : 1;

                    Draw.Line(this, shortLineTagPrefix + "ShortEntryLineActive", false,
                        shortCancelStartBarsAgo, currentShortEntry,
                        0, currentShortEntry,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, shortLineTagPrefix + "ShortTPLineActive", false,
                        shortCancelStartBarsAgo, currentShortTP,
                        0, currentShortTP,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, shortLineTagPrefix + "ShortSLLineActive", false,
                        shortCancelStartBarsAgo, currentShortSL,
                        0, currentShortSL,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, shortLineTagPrefix + "ShortCancelLineActive", false,
                        shortCancelStartBarsAgo, currentShortCancelPrice,
                        0, currentShortCancelPrice,
                        Brushes.Gray, DashStyleHelper.Dot, 2);

                    shortLinesActive = false;
                    shortSignalBar = -1;
                    shortExitBar = -1;

                    skipBarUntil = CurrentBar;
                    if (debug)
                        Print($"{Time[0]} - ➡️ Skipping signals until bar > {skipBarUntil}");
                }
            }

            // === Strategy signal logic (on bar close only) ===
            if (Calculate == Calculate.OnBarClose 
                && CurrentBar > skipBarUntil 
                && CurrentBar != lastBarProcessed)
            {
                double c1Open = Open[1];
                double c1Close = Close[1];
                double c1High = High[1];
                double c1Low = Low[1];

                double c2Open = Open[0];
                double c2Close = Close[0];
                double c2High = High[0];
                double c2Low = Low[0];

                bool c1Bull = c1Close > c1Open;
                bool c2Bull = c2Close > c2Open;
                bool c1Bear = c1Close < c1Open;
                bool c2Bear = c2Close < c2Open;

                bool validBull = c1Bull && c2Bull;
                bool validBear = c1Bear && c2Bear;
				bool shouldLog = debug && (validBull || validBear);
                if (!validBull && !validBear)
                    return;

                double c1Body = Math.Abs(c1Close - c1Open);
                double c2Body = Math.Abs(c2Close - c2Open);
                if (activeMinCandlePoints > 0 && (c1Body < activeMinCandlePoints || c2Body < activeMinCandlePoints))
                {
                    Print($"\n{Time[0]} - 🚫 Skipping signals: candle body below min {activeMinCandlePoints:0.00}. C1={c1Body:0.00}, C2={c2Body:0.00}");
                    return;
                }

                double c12Body = Math.Abs(c2Close - c1Open);

                if (c12Body < activeMinC12Body || c12Body > activeMaxC12Body)
                {
                    if (shouldLog)
                        Print($"\n{Time[0]} - 🚫 Skipping signals: c12Body {c12Body:0.00} outside [{activeMinC12Body:0.00}, {activeMaxC12Body:0.00}]");
                    return;
                }

                double longSL = 0;
                double shortSL = 0;

                if (First_Candle_High_Low)
                {
                    longSL = c1Low;
                    shortSL = c1High;
                }
                else if (First_Candle_Open)
                {
                    longSL = c1Open;
                    shortSL = c1Open;
                }
                else if (Second_Candle_High_Low)
                {
                    longSL = c2Low;
                    shortSL = c2High;
                }
                else if (Second_Candle_Open)
                {
                    longSL = c2Open;
                    shortSL = c2Open;
                }
                else
                {
                    // Fallback/default (optional)
                    longSL = c1Open;
                    shortSL = c1Open;
                }

                // Randomize % within London_DeviationPerc
                double randomizedOffsetPerc = activeOffsetPerc + (rng.NextDouble() * 2 - 1) * activeDeviationPerc;
                double randomizedTpPerc     = activeTpPerc     + (rng.NextDouble() * 2 - 1) * activeDeviationPerc;

                // Ensure no negative values
                randomizedOffsetPerc = Math.Max(0, randomizedOffsetPerc);
                randomizedTpPerc     = Math.Max(0, randomizedTpPerc);

                // Apply to calculations
                double offset = c12Body * (randomizedOffsetPerc / 100.0);
                double longEntry = c2Close - offset;
                double shortEntry = c2Close + offset;

                double longTP = longEntry + c12Body * (randomizedTpPerc / 100.0);
                double shortTP = shortEntry - c12Body * (randomizedTpPerc / 100.0);

                double longCancelPrice = longEntry + c12Body * (activeCancelPerc / 100.0);
                double shortCancelPrice = shortEntry - c12Body * (activeCancelPerc / 100.0);
                double longSLPoints = Math.Abs(longEntry - longSL) / TickSize;
                double shortSLPoints = Math.Abs(shortEntry - shortSL) / TickSize;

                longEntry = RoundToTickSize(longEntry);
                shortEntry = RoundToTickSize(shortEntry);
                longTP = RoundToTickSize(longTP);
                shortTP = RoundToTickSize(shortTP);
                longCancelPrice = RoundToTickSize(longCancelPrice);
                shortCancelPrice = RoundToTickSize(shortCancelPrice);
                longSL = RoundToTickSize(longSL);
                shortSL = RoundToTickSize(shortSL);

                if (debug) {
                    Print($"\n{Time[0]} - Candle body sizes:");
                    Print($"  ➤ c12Body = {c12Body:0.00} (Min allowed: {activeMinC12Body:0.00}, Max allowed: {activeMaxC12Body:0.00})");
                    Print($"  ➤ Entry Offset = {offset:0.00}");
                    Print($"  ➤ Long TP = {longTP:0.00}, Short TP = {shortTP:0.00}");
                }
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    if (debug)
                        Print($"{Time[0]} - Flat position, resetting orderPlaced flags.");
                    longOrderPlaced = false;
                    shortOrderPlaced = false;
                }

	                if (validBull && !longOrderPlaced)
	                {
	                    HandleReverseOnSignal(MarketPosition.Long);
	                    if (RequireEntryConfirmation)
	                    {
	                        if (!ShowEntryConfirmation("Long", longEntry, activeContracts))
	                        {
                            if (debug)
                                Print($"User declined Long entry via confirmation dialog.");
                            return;
                        }
                    }

                    // Long RR check
                    if (activeMaxSLTPRatioPerc > 0)  // only enforce if enabled
                    {
                        double longTPPoints2 = Math.Abs(longTP - longEntry) / TickSize;
                        double longSLPoints2 = Math.Abs(longEntry - longSL) / TickSize;
                        if (longTPPoints2 > 0)
                        {
                            double ratioPerc = (longSLPoints2 / longTPPoints2) * 100.0;
                            if (ratioPerc > activeMaxSLTPRatioPerc)
                            {
                                if (debug)
                                    Print($"{Time[0]} - ❌ Skipping LONG trade. SL/TP ratio = {ratioPerc:0.00}% exceeds max {activeMaxSLTPRatioPerc}%");
                                return; // Skip this trade
                            }
                        }
                    }

                    //double paddedLongSL = longSL - London_SLPadding;
					double paddedLongSL = RoundToTickSize(GetSLForLong());
                    double slInPoints = Math.Abs(longEntry - paddedLongSL);

                    if (activeMaxSLPoints > 0 && slInPoints > activeMaxSLPoints)
                    {
                        if (debug)
                            Print($"{Time[0]} - 🚫 Skipping long trade: SL = {slInPoints:0.00} points > MaxSL = {activeMaxSLPoints}");
                        return;
                    }

                    if (debug)
                        Print($"{Time[0]} - 📈 Valid long signal detected. Entry={longEntry:0.00}, SL={paddedLongSL:0.00}, TP={longTP:0.00}");

                    // Anti Hedge
                    string otherSymbol = GetOtherInstrument();
                    if (AntiHedge && (HasOppositePosition(otherSymbol, MarketPosition.Long) || HasOppositeOrder(otherSymbol, MarketPosition.Long)))
                    {
                        // Draw the 3 lines in black when anti-hedge prevents the trade
                        Draw.Line(this, "LongEntryLine_" + CurrentBar, false,
                            1, longEntry, 0, longEntry, Brushes.Black, DashStyleHelper.Solid, 2);

                        Draw.Line(this, "LongTPLine_" + CurrentBar, false,
                            1, longTP, 0, longTP, Brushes.Black, DashStyleHelper.Solid, 2);

                        Draw.Line(this, "LongSLLine_" + CurrentBar, false,
                            1, paddedLongSL, 0, paddedLongSL, Brushes.Black, DashStyleHelper.Solid, 2);
                        
                        Draw.Line(this, "LongCancelLine_" + CurrentBar, false, 1, longCancelPrice, 0, longCancelPrice, Brushes.Black, DashStyleHelper.Dot, 2);
                        
                        if (debug)
                            Print($"SKIP {Instrument.MasterInstrument.Name} LONG, {otherSymbol} is SHORT.");
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

	                    SetStopLoss("LongEntry", CalculationMode.Price, paddedLongSL, false);
	                    SetProfitTarget("LongEntry", CalculationMode.Price, longTP);
	                    QueueEntryWebhookLong(longEntry, longTP, paddedLongSL);
						if (activeUseMarketEntry)
							EnterLong(activeContracts, "LongEntry");
						else
							EnterLongLimit(0, true, activeContracts, longEntry, "LongEntry");

                    SetHedgeLock(instrument, desiredDirection); 

                    currentLongEntry = longEntry;
                    currentLongTP = longTP;
                    currentLongSL = paddedLongSL;
                    currentLongCancelPrice = longCancelPrice;
                    longSignalBar = CurrentBar;
                    longLineTagPrefix = $"LongLine_{++lineTagCounter}_{CurrentBar}_";
                    longLinesActive = true;
                    Draw.Line(this, longLineTagPrefix + "LongEntryLineActive", false,
                        1, currentLongEntry,
                        0, currentLongEntry,
                        Brushes.Gold, DashStyleHelper.Solid, 2);
                    Draw.Line(this, longLineTagPrefix + "LongTPLineActive", false,
                        1, currentLongTP,
                        0, currentLongTP,
                        Brushes.LimeGreen, DashStyleHelper.Solid, 2);
                    Draw.Line(this, longLineTagPrefix + "LongSLLineActive", false,
                        1, currentLongSL,
                        0, currentLongSL,
                        Brushes.Red, DashStyleHelper.Solid, 2);
                    Draw.Line(this, longLineTagPrefix + "LongCancelLineActive", false,
                        1, currentLongCancelPrice,
                        0, currentLongCancelPrice,
                        Brushes.Gray, DashStyleHelper.Dot, 2);
	                    longExitBar = -1;
	                    longOrderPlaced = true;
	                    shortOrderPlaced = false;
	                    UpdateInfo();
	                }
					else if (shouldLog && validBull && longOrderPlaced)
					{
						Print($"{Time[0]} - 🚫 Skipping LONG: long order already placed/working");
					}

	                if (validBear && !shortOrderPlaced)
	                {
	                    HandleReverseOnSignal(MarketPosition.Short);
	                    if (RequireEntryConfirmation)
	                    {
	                        if (!ShowEntryConfirmation("Short", shortEntry, activeContracts))
	                        {
                            if (debug)
                                Print($"User declined Long entry via confirmation dialog.");
                            return;
                        }
                    }

                    // Short RR check
                    if (activeMaxSLTPRatioPerc > 0)  // only enforce if enabled
                    {
                        double shortTPPoints2 = Math.Abs(shortEntry - shortTP) / TickSize;
                        double shortSLPoints2 = Math.Abs(shortSL - shortEntry) / TickSize;
                        if (shortTPPoints2 > 0)
                        {
                            double ratioPerc = (shortSLPoints2 / shortTPPoints2) * 100.0;
                            if (ratioPerc > activeMaxSLTPRatioPerc)
                            {
                                if (debug)
                                    Print($"{Time[0]} - ❌ Skipping SHORT trade. SL/TP ratio = {ratioPerc:0.00}% exceeds max {activeMaxSLTPRatioPerc}%");
                                return; // Skip this trade
                            }
                        }
                    }

                    //double paddedShortSL = shortSL + London_SLPadding;
					double paddedShortSL = RoundToTickSize(GetSLForShort());
                    double slInPoints = Math.Abs(shortEntry - paddedShortSL);

                    if (activeMaxSLPoints > 0 && slInPoints > activeMaxSLPoints)
                    {
                        if (debug)
                            Print($"{Time[0]} - 🚫 Skipping short trade: SL = {slInPoints:0.00} points > MaxSL = {activeMaxSLPoints}");
                        return;
                    }
                    
                    if (debug)
                        Print($"{Time[0]} - 📉 Valid short signal detected. Entry={shortEntry:0.00}, SL={paddedShortSL:0.00}, TP={shortTP:0.00}");

                    // Anti Hedge
                    string otherSymbol = GetOtherInstrument();
                    if (AntiHedge && (HasOppositePosition(otherSymbol, MarketPosition.Short) || HasOppositeOrder(otherSymbol, MarketPosition.Short)))
                    {                        
                        // Draw the 3 lines in black when anti-hedge prevents the trade
                        Draw.Line(this, "ShortEntryLine_" + CurrentBar, false,
                            1, shortEntry, 0, shortEntry, Brushes.Black, DashStyleHelper.Solid, 2);

                        Draw.Line(this, "ShortTPLine_" + CurrentBar, false,
                            1, shortTP, 0, shortTP, Brushes.Black, DashStyleHelper.Solid, 2);

                        Draw.Line(this, "ShortSLLine_" + CurrentBar, false,
                            1, paddedShortSL, 0, paddedShortSL, Brushes.Black, DashStyleHelper.Solid, 2);
                        
                        Draw.Line(this, "ShortCancelLine_" + CurrentBar, false, 1, currentShortCancelPrice, 0, currentShortCancelPrice, Brushes.Black, DashStyleHelper.Dot, 2);
                        
                        if (debug)
                            Print($"SKIP {Instrument.MasterInstrument.Name} SHORT, {otherSymbol} is LONG.");
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

	                    SetStopLoss("ShortEntry", CalculationMode.Price, paddedShortSL, false);
	                    SetProfitTarget("ShortEntry", CalculationMode.Price, shortTP);
	                    QueueEntryWebhookShort(shortEntry, shortTP, paddedShortSL);
						if (activeUseMarketEntry)
							EnterShort(activeContracts, "ShortEntry");
						else
							EnterShortLimit(0, true, activeContracts, shortEntry, "ShortEntry");

                    SetHedgeLock(instrument, desiredDirection);

                    currentShortEntry = shortEntry;
                    currentShortTP = shortTP;
                    currentShortSL = paddedShortSL;
                    currentShortCancelPrice = shortCancelPrice;
                    shortSignalBar = CurrentBar;
                    shortLineTagPrefix = $"ShortLine_{++lineTagCounter}_{CurrentBar}_";
                    shortLinesActive = true;
                    Draw.Line(this, shortLineTagPrefix + "ShortEntryLineActive", false,
                        1, currentShortEntry,
                        0, currentShortEntry,
                        Brushes.Gold, DashStyleHelper.Solid, 2);
                    Draw.Line(this, shortLineTagPrefix + "ShortTPLineActive", false,
                        1, currentShortTP,
                        0, currentShortTP,
                        Brushes.LimeGreen, DashStyleHelper.Solid, 2);
                    Draw.Line(this, shortLineTagPrefix + "ShortSLLineActive", false,
                        1, currentShortSL,
                        0, currentShortSL,
                        Brushes.Red, DashStyleHelper.Solid, 2);
                    Draw.Line(this, shortLineTagPrefix + "ShortCancelLineActive", false,
                        1, currentShortCancelPrice,
                        0, currentShortCancelPrice,
                        Brushes.Gray, DashStyleHelper.Dot, 2);
	                    shortExitBar = -1;
	                    shortOrderPlaced = true;
	                    longOrderPlaced = false;
	                    UpdateInfo();
	                }
					else if (shouldLog && validBear && shortOrderPlaced)
					{
						Print($"{Time[0]} - 🚫 Skipping SHORT: short order already placed/working");
					}
                lastBarProcessed = CurrentBar;

            }
        }

	        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
	                                            double averageFillPrice, OrderState orderState, DateTime time,
	                                            ErrorCode error, string comment)
	        {
	            if (order.Name == "LongEntry")
	                longEntryOrder = order;
	
	            if (order.Name == "ShortEntry")
	                shortEntryOrder = order;

	            if (order.Name == "LongEntry" && pendingLongWebhook)
	            {
	                string orderId = order != null ? (order.OrderId ?? string.Empty) : string.Empty;
                    double resolvedEntryPrice = pendingLongEntry;
                    double orderLimitPrice = order != null ? order.LimitPrice : limitPrice;
                    if (orderLimitPrice > 0)
                        resolvedEntryPrice = RoundToTickSize(orderLimitPrice);
                    double sendEntryPrice = RoundToTickSize(resolvedEntryPrice);
                    double sendTP = RoundToTickSize(pendingLongTP);
                    double sendSL = RoundToTickSize(pendingLongSL);
	                bool isActive = orderState == OrderState.Accepted || orderState == OrderState.Working;
	                bool isNewOrderId = !string.Equals(lastLongWebhookOrderId, orderId, StringComparison.Ordinal);
	                bool payloadChanged =
	                    isNewOrderId ||
	                    !PricesEqual(lastLongWebhookEntry, sendEntryPrice) ||
	                    !PricesEqual(lastLongWebhookTP, sendTP) ||
	                    !PricesEqual(lastLongWebhookSL, sendSL);

	                if (isActive && payloadChanged)
	                {
                        if (!string.IsNullOrEmpty(lastLongWebhookOrderId))
                        {
                            if (debug)
                                Print($"{Time[0]} - 🔁 Webhook updating LONG working order: {FormatPriceInvariant(lastLongWebhookEntry)}/{FormatPriceInvariant(lastLongWebhookTP)}/{FormatPriceInvariant(lastLongWebhookSL)} -> {FormatPriceInvariant(sendEntryPrice)}/{FormatPriceInvariant(sendTP)}/{FormatPriceInvariant(sendSL)}");
                            SendWebhook("cancel");
                        }
                        else if (debug)
                        {
                            Print($"{Time[0]} - 📤 Webhook sending initial LONG order: {FormatPriceInvariant(sendEntryPrice)}/{FormatPriceInvariant(sendTP)}/{FormatPriceInvariant(sendSL)}");
                        }
	                    SendWebhook("buy", sendEntryPrice, sendTP, sendSL, activeUseMarketEntry);
	                    pendingLongWebhook = false;
	                    lastLongWebhookOrderId = orderId;
                        lastLongWebhookEntry = sendEntryPrice;
                        lastLongWebhookTP = sendTP;
                        lastLongWebhookSL = sendSL;
	                }
	                else if (orderState == OrderState.Rejected || orderState == OrderState.Cancelled)
	                {
	                    pendingLongWebhook = false;
                        if (orderState == OrderState.Cancelled)
                        {
                            lastLongWebhookOrderId = null;
                            lastLongWebhookEntry = 0;
                            lastLongWebhookTP = 0;
                            lastLongWebhookSL = 0;
                        }
	                }
	            }

	            if (order.Name == "ShortEntry" && pendingShortWebhook)
	            {
	                string orderId = order != null ? (order.OrderId ?? string.Empty) : string.Empty;
                    double resolvedEntryPrice = pendingShortEntry;
                    double orderLimitPrice = order != null ? order.LimitPrice : limitPrice;
                    if (orderLimitPrice > 0)
                        resolvedEntryPrice = RoundToTickSize(orderLimitPrice);
                    double sendEntryPrice = RoundToTickSize(resolvedEntryPrice);
                    double sendTP = RoundToTickSize(pendingShortTP);
                    double sendSL = RoundToTickSize(pendingShortSL);
	                bool isActive = orderState == OrderState.Accepted || orderState == OrderState.Working;
	                bool isNewOrderId = !string.Equals(lastShortWebhookOrderId, orderId, StringComparison.Ordinal);
	                bool payloadChanged =
	                    isNewOrderId ||
	                    !PricesEqual(lastShortWebhookEntry, sendEntryPrice) ||
	                    !PricesEqual(lastShortWebhookTP, sendTP) ||
	                    !PricesEqual(lastShortWebhookSL, sendSL);

	                if (isActive && payloadChanged)
	                {
                        if (!string.IsNullOrEmpty(lastShortWebhookOrderId))
                        {
                            if (debug)
                                Print($"{Time[0]} - 🔁 Webhook updating SHORT working order: {FormatPriceInvariant(lastShortWebhookEntry)}/{FormatPriceInvariant(lastShortWebhookTP)}/{FormatPriceInvariant(lastShortWebhookSL)} -> {FormatPriceInvariant(sendEntryPrice)}/{FormatPriceInvariant(sendTP)}/{FormatPriceInvariant(sendSL)}");
                            SendWebhook("cancel");
                        }
                        else if (debug)
                        {
                            Print($"{Time[0]} - 📤 Webhook sending initial SHORT order: {FormatPriceInvariant(sendEntryPrice)}/{FormatPriceInvariant(sendTP)}/{FormatPriceInvariant(sendSL)}");
                        }
	                    SendWebhook("sell", sendEntryPrice, sendTP, sendSL, activeUseMarketEntry);
	                    pendingShortWebhook = false;
	                    lastShortWebhookOrderId = orderId;
                        lastShortWebhookEntry = sendEntryPrice;
                        lastShortWebhookTP = sendTP;
                        lastShortWebhookSL = sendSL;
	                }
	                else if (orderState == OrderState.Rejected || orderState == OrderState.Cancelled)
	                {
	                    pendingShortWebhook = false;
                        if (orderState == OrderState.Cancelled)
                        {
                            lastShortWebhookOrderId = null;
                            lastShortWebhookEntry = 0;
                            lastShortWebhookTP = 0;
                            lastShortWebhookSL = 0;
                        }
	                }
	            }
	
	            // 🧠 Only reset on cancellations (not fills)
	            if (order.OrderState == OrderState.Cancelled)
	            {
                bool allOrdersInactive =
                    (longEntryOrder == null || longEntryOrder.OrderState == OrderState.Cancelled || longEntryOrder.OrderState == OrderState.Filled) &&
                    (shortEntryOrder == null || shortEntryOrder.OrderState == OrderState.Cancelled || shortEntryOrder.OrderState == OrderState.Filled);

                // Only reset if we have no open position AND no active orders
                if (allOrdersInactive && Position.MarketPosition == MarketPosition.Flat)
                {
                    if (debug)
                        Print($"{Time[0]} - 🔁 Order canceled and flat — resetting info state");
                    longEntryOrder = null;
                    shortEntryOrder = null;
                    longOrderPlaced = false;
                    shortOrderPlaced = false;
                    currentLongEntry = currentShortEntry = 0;
                    currentLongTP = currentShortTP = 0;
                    currentLongSL = currentShortSL = 0;
                    currentLongCancelPrice = currentShortCancelPrice = 0;

                    UpdateInfo();
                }
            }
            else
            {
                // For all other order updates (Submitted, Working, Filled), just refresh info
                UpdateInfo();
            }
        }

        private bool PricesEqual(double a, double b)
        {
            double tolerance = TickSize > 0 ? TickSize * 0.5 : 1e-10;
            return Math.Abs(a - b) <= tolerance;
        }

        private double RoundToTickSize(double price)
        {
            if (Instrument == null || TickSize <= 0)
                return price;

            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        private string FormatPriceInvariant(double price)
        {
            return price.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private double GetTotalCumProfit()
        {
            if (SystemPerformance == null || SystemPerformance.AllTrades == null || SystemPerformance.AllTrades.TradesPerformance == null)
                return 0;

            return SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
        }

        private double GetTotalCumProfitPoints()
        {
            double currencyProfit = GetTotalCumProfit();
            if (Instrument == null || Instrument.MasterInstrument == null || Instrument.MasterInstrument.PointValue <= 0)
                return currencyProfit;

            return currencyProfit / Instrument.MasterInstrument.PointValue;
        }

        private bool HasReachedSessionGainLimit()
        {
            if (activeSession == SessionSlot.None)
                return false;

            if (activeMaxSessionGain <= 0)
                return false;

            if (sessionGainLimitReached)
            {
                if (FlattenOnMaxSessionGain && Position.MarketPosition != MarketPosition.Flat)
                    Flatten("SessionGainLimit");
                return true;
            }

            double sessionProfit = GetTotalCumProfitPoints() - sessionStartCumProfit;
            if (sessionProfit >= activeMaxSessionGain)
            {
                sessionGainLimitReached = true;
                if (debug)
                    Print($"{Time[0]} - ⛔ Max session gain reached ({sessionProfit:0.00} pts >= {activeMaxSessionGain:0.00} pts). Pausing entries for session.");
                if (FlattenOnMaxSessionGain && Position.MarketPosition != MarketPosition.Flat)
                    Flatten("SessionGainLimit");
                return true;
            }

            return false;
        }

        private void UpdateIfvgAddon()
        {
            if (CurrentBar < 2 || ifvgActiveFvgs == null)
                return;

            UpdateIfvgSwingLiquidity();
            UpdateIfvgActiveFvgs();
            UpdateIfvgBreakEvenFvgs();
            DetectNewIfvg();

            if (!ifvgOverrideActive)
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
                ResetIfvgOverride();
            else
            {
                CheckIfvgExitOnCloseBeyondEntry();
                CheckIfvgBreakEvenTrigger();
            }
        }

        private void ResetIfvgOverride()
        {
            ifvgOverrideActive = false;
            ifvgActiveDirection = null;
            ifvgEntryLower = 0;
            ifvgEntryUpper = 0;
            ifvgBreakEvenActive = false;
            ifvgBreakEvenTriggered = false;
            ifvgBreakEvenPrice = 0;
            ifvgBreakEvenDirection = null;
            ifvgActiveTradeTag = null;
            ifvgLastTradeLabelPrinted = null;
        }

        private void CheckIfvgExitOnCloseBeyondEntry()
        {
            if (!ifvgOverrideActive || ifvgActiveDirection == null)
                return;

            if (ifvgActiveDirection == IfvgDirection.Long && Position.MarketPosition == MarketPosition.Long && Close[0] < ifvgEntryLower)
                ExitLong("iFVGCloseExit", IfvgLongSignalName);
            else if (ifvgActiveDirection == IfvgDirection.Short && Position.MarketPosition == MarketPosition.Short && Close[0] > ifvgEntryUpper)
                ExitShort("iFVGCloseExit", IfvgShortSignalName);
        }

        private void UpdateIfvgActiveFvgs()
        {
            if (ifvgActiveFvgs.Count == 0)
                return;

            for (int i = 0; i < ifvgActiveFvgs.Count; i++)
            {
                IfvgBox fvg = ifvgActiveFvgs[i];
                if (!fvg.IsActive)
                    continue;

                bool invalidated = fvg.IsBullish
                    ? (Close[0] < fvg.Lower && Close[0] < Open[0])
                    : (Close[0] > fvg.Upper && Close[0] > Open[0]);

                fvg.EndBarIndex = CurrentBar;

                if (!invalidated)
                    continue;

                LogIfvgTrade(fvg.Tag, string.Format(
                    "ENTRY ATTEMPT {0} fvgLower={1} fvgUpper={2} close={3}",
                    fvg.IsBullish ? "bullish" : "bearish",
                    fvg.Lower,
                    fvg.Upper,
                    Close[0]), true);

                IfvgDirection direction = fvg.IsBullish ? IfvgDirection.Short : IfvgDirection.Long;
                TryEnterIfvg(direction, fvg.Lower, fvg.Upper, fvg.Tag);
                fvg.IsActive = false;
            }
        }

        private void DetectNewIfvg()
        {
            bool bullishFvg = Low[0] > High[2];
            bool bearishFvg = High[0] < Low[2];

            if (!bullishFvg && !bearishFvg)
                return;

            LogIfvgVerbose(string.Format(
                "FVG detected {0} low0={1} high2={2} high0={3} low2={4}",
                bullishFvg ? "bullish" : "bearish",
                Low[0],
                High[2],
                High[0],
                Low[2]));

            IfvgBox fvg = new IfvgBox
            {
                IsBullish = bullishFvg,
                Lower = bullishFvg ? High[2] : High[0],
                Upper = bullishFvg ? Low[0] : Low[2],
                StartBarIndex = CurrentBar - 2,
                EndBarIndex = CurrentBar,
                CreatedBarIndex = CurrentBar,
                IsActive = true,
                Tag = string.Format("iFVG_{0}_{1:yyyyMMdd_HHmmss}", ifvgCounter++, Time[0])
            };

            if (ifvgBreakEvenFvgs != null)
                ifvgBreakEvenFvgs.Add(fvg);

            double fvgSizePoints = Math.Abs(fvg.Upper - fvg.Lower);
            if (activeIfvgMinSizePoints > 0 && fvgSizePoints < activeIfvgMinSizePoints)
            {
                LogIfvgVerbose(string.Format("FVG rejected: size {0} < min {1}", fvgSizePoints, activeIfvgMinSizePoints));
                return;
            }
            if (activeIfvgMaxSizePoints > 0 && fvgSizePoints > activeIfvgMaxSizePoints)
            {
                LogIfvgVerbose(string.Format("FVG rejected: size {0} > max {1}", fvgSizePoints, activeIfvgMaxSizePoints));
                return;
            }

            ifvgActiveFvgs.Add(fvg);
        }

        private void UpdateIfvgBreakEvenFvgs()
        {
            if (ifvgBreakEvenFvgs == null || ifvgBreakEvenFvgs.Count == 0)
                return;

            for (int i = 0; i < ifvgBreakEvenFvgs.Count; i++)
            {
                IfvgBox fvg = ifvgBreakEvenFvgs[i];
                if (!fvg.IsActive)
                    continue;

                bool invalidated = fvg.IsBullish
                    ? (Close[0] < fvg.Lower && Close[0] < Open[0])
                    : (Close[0] > fvg.Upper && Close[0] > Open[0]);

                if (invalidated)
                    fvg.IsActive = false;
            }
        }

        private void TryEnterIfvg(IfvgDirection direction, double fvgLower, double fvgUpper, string fvgTag)
        {
            if (!UseIfvgAddon)
                return;

            if (ifvgOverrideActive)
            {
                LogIfvgTrade(fvgTag, "BLOCKED (ActivePosition)", false);
                return;
            }

            if (ifvgLastEntryBar == CurrentBar)
            {
                LogIfvgTrade(fvgTag, "BLOCKED (AlreadyEnteredThisBar)", false);
                return;
            }

            if (!TimeInSession(Time[0]))
            {
                LogIfvgTrade(fvgTag, "BLOCKED (OutsideSession)", false);
                return;
            }

            if (TimeInNoTradesAfter(Time[0]))
            {
                LogIfvgTrade(fvgTag, "BLOCKED (NoTradesAfter)", false);
                return;
            }

            if (TimeInSkip(Time[0]))
            {
                LogIfvgTrade(fvgTag, "BLOCKED (SkipWindow)", false);
                return;
            }

            if (HasReachedSessionGainLimit())
                return;

            IfvgSweepEvent sweep = GetEligibleIfvgSweep(direction, fvgTag);
            if (sweep == null)
                return;

            if (!IfvgPassesVolumeSmaFilter(fvgTag))
                return;

            CancelOrder(longEntryOrder);
            CancelOrder(shortEntryOrder);
            SendWebhookCancelSafe();

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong("iFVGFlip");
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort("iFVGFlip");

            longLinesActive = false;
            shortLinesActive = false;
            longSignalBar = -1;
            shortSignalBar = -1;
            longExitBar = -1;
            shortExitBar = -1;

            double entryPrice = Close[0];
            double firstTargetPrice;
            int firstTargetBarsAgo;
            bool hasFirstTarget = direction == IfvgDirection.Long
                ? TryGetNthBullishPivotHighAboveEntry(entryPrice, 1, activeIfvgMinTpSlDistancePoints, out firstTargetPrice, out firstTargetBarsAgo)
                : TryGetNthBearishPivotLowBelowEntry(entryPrice, 1, activeIfvgMinTpSlDistancePoints, out firstTargetPrice, out firstTargetBarsAgo);
            if (!hasFirstTarget)
            {
                LogIfvgTrade(fvgTag, string.Format("BLOCKED (NoTarget: {0} entry={1})", direction, entryPrice), false);
                return;
            }

            double targetPrice = firstTargetPrice;
            int targetBarsAgo = firstTargetBarsAgo;

            if (activeIfvgUseBreakEvenWickLine)
            {
                double minBeGap = activeIfvgMinTpSlDistancePoints;
                bool foundTarget = false;
                for (int occurrence = 2; occurrence <= 10; occurrence++)
                {
                    double candidatePrice;
                    int candidateBarsAgo;
                    bool hasCandidate = direction == IfvgDirection.Long
                        ? TryGetNthBullishPivotHighAboveEntry(entryPrice, occurrence, activeIfvgMinTpSlDistancePoints, out candidatePrice, out candidateBarsAgo)
                        : TryGetNthBearishPivotLowBelowEntry(entryPrice, occurrence, activeIfvgMinTpSlDistancePoints, out candidatePrice, out candidateBarsAgo);
                    if (!hasCandidate)
                        break;

                    bool beyondBe = direction == IfvgDirection.Long
                        ? candidatePrice > firstTargetPrice
                        : candidatePrice < firstTargetPrice;
                    bool farEnough = minBeGap <= 0 || Math.Abs(candidatePrice - firstTargetPrice) >= minBeGap;

                    if (beyondBe && farEnough)
                    {
                        targetPrice = candidatePrice;
                        targetBarsAgo = candidateBarsAgo;
                        if (occurrence > 2)
                            LogIfvgTrade(fvgTag, string.Format("TP adjusted (Pivot {0}) price={1} barsAgo={2}", occurrence, candidatePrice, candidateBarsAgo), false);
                        foundTarget = true;
                        break;
                    }
                }

                if (!foundTarget)
                {
                    LogIfvgTrade(fvgTag, string.Format("BLOCKED (NoTargetBeyondBE: {0} entry={1})", direction, entryPrice), false);
                    return;
                }
            }

            targetPrice = RoundToTickSize(targetPrice);

            double stopPrice = 0;
            int stopBarsAgo = -1;
            bool hasStop = false;
            bool hasAnyStop = false;
            double fallbackStopPrice = 0;
            int fallbackStopBarsAgo = -1;
            int stopOccurrence = 1;
            while (true)
            {
                bool foundStop = direction == IfvgDirection.Long
                    ? TryGetNthBearishPivotLowBelowEntry(entryPrice, stopOccurrence, activeIfvgMinTpSlDistancePoints, out stopPrice, out stopBarsAgo)
                    : TryGetNthBullishPivotHighAboveEntry(entryPrice, stopOccurrence, activeIfvgMinTpSlDistancePoints, out stopPrice, out stopBarsAgo);

                if (!foundStop)
                    break;

                if (!hasAnyStop)
                {
                    hasAnyStop = true;
                    fallbackStopPrice = stopPrice;
                    fallbackStopBarsAgo = stopBarsAgo;
                }

                bool outsideFvg = direction == IfvgDirection.Long ? stopPrice < fvgLower : stopPrice > fvgUpper;
                if (outsideFvg)
                {
                    hasStop = true;
                    break;
                }

                stopOccurrence++;
            }

            if (!hasStop)
            {
                if (hasAnyStop)
                {
                    stopPrice = fallbackStopPrice;
                    stopBarsAgo = fallbackStopBarsAgo;
                    LogIfvgTrade(fvgTag, string.Format(
                        "WARN StopInsideFVG {0} entry={1} stop={2} fvgLower={3} fvgUpper={4}",
                        direction,
                        entryPrice,
                        stopPrice,
                        fvgLower,
                        fvgUpper), false);
                }
                else
                {
                    LogIfvgTrade(fvgTag, string.Format(
                        "BLOCKED (NoStop: {0} entry={1})",
                        direction,
                        entryPrice), false);
                    return;
                }
            }

            double bePrice = firstTargetPrice;
            int beBarsAgo = firstTargetBarsAgo;
            if (activeIfvgUseBreakEvenWickLine)
            {
                double fvgBePrice;
                int fvgBeBarsAgo;
                if (TryGetBreakEvenFromFvg(direction, entryPrice, firstTargetPrice, out fvgBePrice, out fvgBeBarsAgo))
                {
                    bePrice = fvgBePrice;
                    beBarsAgo = fvgBeBarsAgo;
                    LogIfvgTrade(fvgTag, string.Format("BE LINE from FVG price={0} barsAgo={1}", bePrice, beBarsAgo), false);
                }

                ActivateIfvgBreakEvenLine(direction, bePrice);
                if (bePrice == firstTargetPrice)
                    LogIfvgTrade(fvgTag, string.Format("BE LINE price={0} barsAgo={1}", bePrice, beBarsAgo), false);
            }

            stopPrice = RoundToTickSize(stopPrice);

            string signalName = direction == IfvgDirection.Long ? IfvgLongSignalName : IfvgShortSignalName;
            SetStopLoss(signalName, CalculationMode.Price, stopPrice, false);
            SetProfitTarget(signalName, CalculationMode.Price, targetPrice);

            string beInfo = activeIfvgUseBreakEvenWickLine ? string.Format(" be={0}", RoundToTickSize(bePrice)) : string.Empty;
            LogIfvgTrade(fvgTag, string.Format(
                "ENTRY SENT {0} entry={1} stop={2} target={3}{4} stopBarsAgo={5} targetBarsAgo={6} sweepPrice={7} sweepBar={8}",
                direction,
                entryPrice,
                stopPrice,
                targetPrice,
                beInfo,
                stopBarsAgo,
                targetBarsAgo,
                sweep.Price,
                sweep.BarIndex), false);

            ifvgOverrideActive = true;
            ifvgActiveDirection = direction;
            ifvgEntryLower = fvgLower;
            ifvgEntryUpper = fvgUpper;
            ifvgLastEntryBar = CurrentBar;
            ifvgActiveTradeTag = fvgTag;

            if (direction == IfvgDirection.Long)
                EnterLong(activeContracts, signalName);
            else
                EnterShort(activeContracts, signalName);
        }

        private void UpdateIfvgSwingLiquidity()
        {
            if (ifvgSwingLines == null)
                return;

            if (activeIfvgSwingDrawBars <= 0)
            {
                if (ifvgSwingLines.Count > 0)
                    ifvgSwingLines.Clear();
                return;
            }

            if (CurrentBar < activeIfvgSwingStrength * 2)
                return;

            int pivotBarsAgo = activeIfvgSwingStrength;
            if (IsIfvgSwingHigh(pivotBarsAgo))
                AddIfvgSwingLine(true, pivotBarsAgo, High[pivotBarsAgo]);
            if (IsIfvgSwingLow(pivotBarsAgo))
                AddIfvgSwingLine(false, pivotBarsAgo, Low[pivotBarsAgo]);

            UpdateIfvgSwingLines();
            PruneIfvgSwingLines();
        }

        private bool IsIfvgSwingHigh(int barsAgo)
        {
            int span = barsAgo * 2 + 1;
            double max = MAX(High, span)[0];
            return High[barsAgo] >= max;
        }

        private bool IsIfvgSwingLow(int barsAgo)
        {
            int span = barsAgo * 2 + 1;
            double min = MIN(Low, span)[0];
            return Low[barsAgo] <= min;
        }

        private void AddIfvgSwingLine(bool isHigh, int barsAgo, double price)
        {
            int startBarIndex = CurrentBar - barsAgo;
            string tag = string.Format("iFVG_Swing_{0}_{1}", isHigh ? "H" : "L", startBarIndex);

            IfvgSwingLine line = new IfvgSwingLine
            {
                Tag = tag,
                Price = price,
                StartBarIndex = startBarIndex,
                EndBarIndex = CurrentBar,
                IsActive = true,
                IsHigh = isHigh
            };

            ifvgSwingLines.Add(line);
        }

        private void UpdateIfvgSwingLines()
        {
            for (int i = 0; i < ifvgSwingLines.Count; i++)
            {
                IfvgSwingLine line = ifvgSwingLines[i];
                if (!line.IsActive)
                    continue;

                bool hit = line.IsHigh ? High[0] >= line.Price : Low[0] <= line.Price;
                line.EndBarIndex = CurrentBar;

                if (hit)
                {
                    RegisterIfvgSweep(line.IsHigh ? IfvgDirection.Short : IfvgDirection.Long, line.Price);
                    line.IsActive = false;
                }
            }
        }

        private void PruneIfvgSwingLines()
        {
            int cutoffIndex = CurrentBar - activeIfvgSwingDrawBars;
            for (int i = ifvgSwingLines.Count - 1; i >= 0; i--)
            {
                IfvgSwingLine line = ifvgSwingLines[i];
                if (line.StartBarIndex < cutoffIndex)
                    ifvgSwingLines.RemoveAt(i);
            }
        }

        private void RegisterIfvgSweep(IfvgDirection direction, double price)
        {
            IfvgSweepEvent existing = ifvgLastSwingSweep;
            if (existing != null &&
                existing.Direction == direction &&
                existing.Price == price &&
                existing.BarIndex == CurrentBar)
                return;

            IfvgSweepEvent sweep = new IfvgSweepEvent
            {
                Direction = direction,
                Price = price,
                BarIndex = CurrentBar
            };

            ifvgLastSwingSweep = sweep;

            LogIfvgVerbose(string.Format(
                "Sweep registered {0} price={1} bar={2} source=swing",
                direction,
                price,
                CurrentBar));
        }

        private IfvgSweepEvent GetEligibleIfvgSweep(IfvgDirection direction, string fvgTag)
        {
            IfvgSweepEvent best = null;

            if (ifvgLastSwingSweep != null && ifvgLastSwingSweep.Direction == direction)
                best = ifvgLastSwingSweep;

            if (best == null)
            {
                LogIfvgTrade(fvgTag, string.Format("BLOCKED (SweepMissing: {0})", direction), false);
                return null;
            }

            int barsSince = CurrentBar - best.BarIndex;
            if (barsSince < 0 || barsSince > activeIfvgMaxBarsBetweenSweepAndIfvg)
            {
                LogIfvgTrade(fvgTag, string.Format(
                    "BLOCKED (SweepExpired: {0} barsSince={1} max={2})",
                    direction,
                    barsSince,
                    activeIfvgMaxBarsBetweenSweepAndIfvg), false);
                return null;
            }

            LogIfvgTrade(fvgTag, string.Format(
                "Eligible sweep {0} price={1} bar={2} barsSince={3}",
                direction,
                best.Price,
                best.BarIndex,
                barsSince), false);
            return best;
        }

        private bool IfvgPassesVolumeSmaFilter(string fvgTag)
        {
            if (!activeIfvgUseVolumeSmaFilter)
                return true;

            if (ifvgVolumeFastSmaActive == null || ifvgVolumeSlowSmaActive == null)
                return true;

            int requiredBars = Math.Max(activeIfvgVolumeFastSmaPeriod, activeIfvgVolumeSlowSmaPeriod);
            if (CurrentBar < requiredBars - 1)
            {
                LogIfvgTrade(fvgTag, string.Format(
                    "BLOCKED (VolumeSmaWarmup: bars={0} need={1})",
                    CurrentBar + 1,
                    requiredBars), false);
                return false;
            }

            double fast = ifvgVolumeFastSmaActive[0];
            double slow = ifvgVolumeSlowSmaActive[0];
            if (slow <= 0)
            {
                LogIfvgTrade(fvgTag, string.Format(
                    "BLOCKED (VolumeSmaInvalid: fast={0} slow={1})",
                    fast,
                    slow), false);
                return false;
            }

            if (fast <= slow * activeIfvgVolumeSmaMultiplier)
            {
                LogIfvgTrade(fvgTag, string.Format(
                    "BLOCKED (VolumeSma: fast={0} slow={1} mult={2})",
                    fast,
                    slow,
                    activeIfvgVolumeSmaMultiplier), false);
                return false;
            }

            LogIfvgTrade(fvgTag, string.Format(
                "Filter VolumeSma ok fast={0} slow={1} mult={2}",
                fast,
                slow,
                activeIfvgVolumeSmaMultiplier), false);
            return true;
        }

        private void CheckIfvgBreakEvenTrigger()
        {
            if (!activeIfvgUseBreakEvenWickLine || !ifvgBreakEvenActive || ifvgBreakEvenTriggered)
                return;
            if (Position.MarketPosition == MarketPosition.Flat || ifvgBreakEvenDirection == null)
                return;

            double high = High[0];
            double low = Low[0];
            bool hit = ifvgBreakEvenDirection == IfvgDirection.Long
                ? high >= ifvgBreakEvenPrice
                : low <= ifvgBreakEvenPrice;

            if (!hit)
                return;

            ifvgBreakEvenTriggered = true;
            string signalName = ifvgBreakEvenDirection == IfvgDirection.Long ? IfvgLongSignalName : IfvgShortSignalName;
            double entryPrice = Position.AveragePrice;
            SetStopLoss(signalName, CalculationMode.Price, entryPrice, false);
            LogIfvgTrade(ifvgActiveTradeTag, string.Format("BE HIT move SL to entry={0}", entryPrice), true);
        }

        private void ActivateIfvgBreakEvenLine(IfvgDirection direction, double price)
        {
            ifvgBreakEvenActive = true;
            ifvgBreakEvenTriggered = false;
            ifvgBreakEvenPrice = RoundToTickSize(price);
            ifvgBreakEvenDirection = direction;
        }

        private bool TryGetNthBullishPivotHighAboveEntry(double entryPrice, int occurrence, double minDistancePoints, out double price, out int barsAgo)
        {
            price = 0;
            barsAgo = -1;
            int found = 0;

            for (int i = 1; i + 1 <= CurrentBars[0]; i++)
            {
                if (Highs[0][i] <= Highs[0][i - 1] || Highs[0][i] <= Highs[0][i + 1])
                    continue;
                if (Highs[0][i] <= entryPrice)
                    continue;
                if (minDistancePoints > 0 && (Highs[0][i] - entryPrice) < minDistancePoints)
                    continue;

                found++;
                if (found == occurrence)
                {
                    price = Highs[0][i];
                    barsAgo = i;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetNthBearishPivotLowBelowEntry(double entryPrice, int occurrence, double minDistancePoints, out double price, out int barsAgo)
        {
            price = 0;
            barsAgo = -1;
            int found = 0;

            for (int i = 1; i + 1 <= CurrentBars[0]; i++)
            {
                if (Lows[0][i] >= Lows[0][i - 1] || Lows[0][i] >= Lows[0][i + 1])
                    continue;
                if (Lows[0][i] >= entryPrice)
                    continue;
                if (minDistancePoints > 0 && (entryPrice - Lows[0][i]) < minDistancePoints)
                    continue;

                found++;
                if (found == occurrence)
                {
                    price = Lows[0][i];
                    barsAgo = i;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetBreakEvenFromFvg(IfvgDirection direction, double entryPrice, double pivotPrice, out double bePrice, out int beBarsAgo)
        {
            bePrice = 0;
            beBarsAgo = -1;

            if (ifvgBreakEvenFvgs == null || ifvgBreakEvenFvgs.Count == 0)
                return false;

            bool found = false;
            double bestPrice = 0;
            int bestBarsAgo = -1;

            for (int i = 0; i < ifvgBreakEvenFvgs.Count; i++)
            {
                IfvgBox fvg = ifvgBreakEvenFvgs[i];
                if (!fvg.IsActive)
                    continue;

                double candidatePrice = direction == IfvgDirection.Long ? fvg.Lower : fvg.Upper;
                if (direction == IfvgDirection.Long)
                {
                    if (candidatePrice <= entryPrice || candidatePrice >= pivotPrice)
                        continue;
                    if (!found || candidatePrice < bestPrice)
                    {
                        found = true;
                        bestPrice = candidatePrice;
                        bestBarsAgo = CurrentBar - fvg.CreatedBarIndex;
                    }
                }
                else
                {
                    if (candidatePrice >= entryPrice || candidatePrice <= pivotPrice)
                        continue;
                    if (!found || candidatePrice > bestPrice)
                    {
                        found = true;
                        bestPrice = candidatePrice;
                        bestBarsAgo = CurrentBar - fvg.CreatedBarIndex;
                    }
                }
            }

            if (!found)
                return false;

            bePrice = RoundToTickSize(bestPrice);
            beBarsAgo = bestBarsAgo;
            return true;
        }

        private void LogIfvgDebug(string message)
        {
            if (!IfvgDebugLogging)
                return;
            Print(string.Format("{0} - iFVG {1}", Time[0], message));
        }

        private void LogIfvgVerbose(string message)
        {
            if (!IfvgDebugLogging || !IfvgVerboseDebugLogging)
                return;
            Print(string.Format("{0} - iFVG {1}", Time[0], message));
        }

        private void LogIfvgTrade(string tradeTag, string message, bool addSeparator)
        {
            if (!IfvgDebugLogging)
                return;

            string label = FormatIfvgLabel(tradeTag);
            if (!string.IsNullOrEmpty(label) && label != ifvgLastTradeLabelPrinted)
            {
                if (!string.IsNullOrEmpty(ifvgLastTradeLabelPrinted) && addSeparator)
                    Print(string.Empty);
                ifvgLastTradeLabelPrinted = label;
            }

            if (!string.IsNullOrEmpty(label))
                Print(string.Format("{0} - {1} {2}", Time[0], label, message));
            else
                Print(string.Format("{0} - {1}", Time[0], message));
        }

        private string FormatIfvgLabel(string tradeTag)
        {
            if (string.IsNullOrEmpty(tradeTag))
                return string.Empty;

            string[] parts = tradeTag.Split('_');
            if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]))
                return "iFVG T" + parts[1];

            return "iFVG";
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
	            var smallFont = new SimpleFont("Arial", 8) { Bold = true };

	            // ✅ Track TP fills
	            if (execution.Order != null && execution.Order.Name == "Profit target"
	                && execution.Order.OrderState == OrderState.Filled)
	            {
	                double entryPrice = execution.Order.FromEntrySignal == "LongEntry"
	                    ? currentLongEntry
	                    : currentShortEntry;

                double points = Math.Abs(price - entryPrice) / TickSize;
                double pointsInPrice = points * TickSize;

                double yOffset = (execution.Order.FromEntrySignal == "LongEntry")
                    ? price + (2 * TickSize)
                    : price - (2 * TickSize);

                string tag = "TPText_" + CurrentBar + "_" + execution.Order.FromEntrySignal;
                Draw.Text(this, tag, true,
                    $"+{pointsInPrice:0.00} points",
                    0, yOffset, 0,
                    Brushes.Green,
                    new SimpleFont("Arial", 15),
                    TextAlignment.Center,
                    null, null, 0);
            }

            // ✅ Track SL fills
	            if (execution.Order != null && execution.Order.Name == "Stop loss"
	                && execution.Order.OrderState == OrderState.Filled)
	            {
	                double entryPrice = execution.Order.FromEntrySignal == "LongEntry"
	                    ? currentLongEntry
	                    : currentShortEntry;

                double points = Math.Abs(price - entryPrice) / TickSize;
                double pointsInPrice = points * TickSize;

                double yOffset = (execution.Order.FromEntrySignal == "LongEntry")
                    ? price - (2 * TickSize)   // place text below stop for long
                    : price + (2 * TickSize);  // place text above stop for short

                string tag = "SLText_" + CurrentBar + "_" + execution.Order.FromEntrySignal;
                Draw.Text(this, tag, true,
                    $"-{pointsInPrice:0.00} points",
                    0, yOffset, 0,
                    Brushes.Red,
                    new SimpleFont("Arial", 15),
                    TextAlignment.Center,
                    null, null, 0);
            }

            if (execution.Order != null &&
                execution.Order.FromEntrySignal == "LongEntry" &&
                (execution.Order.Name == "Profit target" || execution.Order.Name == "Stop loss") &&
                execution.Order.OrderState == OrderState.Filled)
            {
                int barIndex = Bars.GetBar(time);
                longExitBar = barIndex >= 0 ? barIndex : CurrentBar;
            }

            if (execution.Order != null &&
                execution.Order.FromEntrySignal == "ShortEntry" &&
                (execution.Order.Name == "Profit target" || execution.Order.Name == "Stop loss") &&
                execution.Order.OrderState == OrderState.Filled)
            {
                int barIndex = Bars.GetBar(time);
                shortExitBar = barIndex >= 0 ? barIndex : CurrentBar;
            }

            bool isExitExecution = execution.Order != null &&
                execution.Order.OrderState == OrderState.Filled &&
                (execution.Order.Name == "Profit target" ||
                execution.Order.Name == "Stop loss" ||
                execution.Order.Name.StartsWith("Exit_"));

            if (isExitExecution && Position.MarketPosition == MarketPosition.Flat)
            {
                if (debug)
                    Print($"{Time[0]} - 📤 Exit execution detected ({execution.Order.Name}), sending webhook exit (execId={executionId})");
                SendWebhookExitSafe(executionId);
            }

            // ✅ Reset only after final exit (TP/SL/flatten) when position is truly flat
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                bool hasWorkingOrders =
                    (longEntryOrder != null && longEntryOrder.OrderState == OrderState.Working) ||
                    (shortEntryOrder != null && shortEntryOrder.OrderState == OrderState.Working);

                // 🧠 Reset only if this execution was an exit, not an entry
                if (isExitExecution && !hasWorkingOrders)
                {
                    if (debug)
                        Print($"{Time[0]} - ✅ Position closed, resetting info state");
                    longEntryOrder = null;
                    shortEntryOrder = null;
                    longOrderPlaced = false;
                    shortOrderPlaced = false;

                    UpdateInfo();
                }
                ClearHedgeLock(Instrument.MasterInstrument.Name);
	            }

            if (ifvgOverrideActive && Position.MarketPosition == MarketPosition.Flat)
                ResetIfvgOverride();
	        }

	        private void UpdatePreviewLines()
	        {
	            if (longLinesActive && longSignalBar >= 0)
	            {
                int startBarsAgo = CurrentBar - longSignalBar;
                int endBarsAgo = longExitBar >= 0 ? Math.Max(0, CurrentBar - longExitBar) : 0;

                Draw.Line(this, longLineTagPrefix + "LongEntryLineActive", false,
                    startBarsAgo, currentLongEntry,
                    endBarsAgo, currentLongEntry,
                    Brushes.Gold, DashStyleHelper.Solid, 2);

                Draw.Line(this, longLineTagPrefix + "LongTPLineActive", false,
                    startBarsAgo, currentLongTP,
                    endBarsAgo, currentLongTP,
                    Brushes.LimeGreen, DashStyleHelper.Solid, 2);

                Draw.Line(this, longLineTagPrefix + "LongSLLineActive", false,
                    startBarsAgo, currentLongSL,
                    endBarsAgo, currentLongSL,
                    Brushes.Red, DashStyleHelper.Solid, 2);

                Draw.Line(this, longLineTagPrefix + "LongCancelLineActive", false,
                    startBarsAgo, currentLongCancelPrice,
                    endBarsAgo, currentLongCancelPrice,
                    Brushes.Gray, DashStyleHelper.Dot, 2);
            }

            if (shortLinesActive && shortSignalBar >= 0)
            {
                int startBarsAgo = CurrentBar - shortSignalBar;
                int endBarsAgo = shortExitBar >= 0 ? Math.Max(0, CurrentBar - shortExitBar) : 0;

                Draw.Line(this, shortLineTagPrefix + "ShortEntryLineActive", false,
                    startBarsAgo, currentShortEntry,
                    endBarsAgo, currentShortEntry,
                    Brushes.Gold, DashStyleHelper.Solid, 2);

                Draw.Line(this, shortLineTagPrefix + "ShortTPLineActive", false,
                    startBarsAgo, currentShortTP,
                    endBarsAgo, currentShortTP,
                    Brushes.LimeGreen, DashStyleHelper.Solid, 2);

                Draw.Line(this, shortLineTagPrefix + "ShortSLLineActive", false,
                    startBarsAgo, currentShortSL,
                    endBarsAgo, currentShortSL,
                    Brushes.Red, DashStyleHelper.Solid, 2);

                Draw.Line(this, shortLineTagPrefix + "ShortCancelLineActive", false,
                    startBarsAgo, currentShortCancelPrice,
                    endBarsAgo, currentShortCancelPrice,
                    Brushes.Gray, DashStyleHelper.Dot, 2);
            }
        }

		private void CancelEntryIfAfterNoTrades()
		{
			if (shortEntryOrder != null)
			{
                if (debug)
				    Print($"{Time[0]} - ⏰ Canceling SHORT entry due to NoTradesAfter");
					CancelOrder(shortEntryOrder);
	                SendWebhookCancelSafe();
				}
	
				if (longEntryOrder != null)
				{
	                if (debug)
					    Print($"{Time[0]} - ⏰ Canceling LONG entry due to NoTradesAfter");
					CancelOrder(longEntryOrder);
	                SendWebhookCancelSafe();
				}

			longOrderPlaced = false;
			shortOrderPlaced = false;
			longEntryOrder = null;
			shortEntryOrder = null;
		}

        private void HandleReverseOnSignal(MarketPosition desiredDirection)
        {
            if (!ReverseOnSignal)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (desiredDirection == MarketPosition.Long && IsOrderActive(shortEntryOrder))
                {
                    if (debug)
                        Print($"{Time[0]} - 🔁 Reverse signal while SHORT order working. Canceling SHORT entry.");
                    CancelOrder(shortEntryOrder);
                    shortEntryOrder = null;
                    shortOrderPlaced = false;
                }
                else if (desiredDirection == MarketPosition.Short && IsOrderActive(longEntryOrder))
                {
                    if (debug)
                        Print($"{Time[0]} - 🔁 Reverse signal while LONG order working. Canceling LONG entry.");
                    CancelOrder(longEntryOrder);
                    longEntryOrder = null;
                    longOrderPlaced = false;
                }
            }
        }

        private bool IsOrderActive(Order order)
        {
            return order != null &&
                (order.OrderState == OrderState.Working ||
                order.OrderState == OrderState.Submitted ||
                order.OrderState == OrderState.Accepted ||
                order.OrderState == OrderState.ChangePending);
        }

	        private void Flatten(string reason)
	        {
	            if (Position.MarketPosition == MarketPosition.Long)
	            {
	                if (debug)
	                    Print($"{Time[0]} - Flattening LONG due to {reason}");
	                if (ifvgOverrideActive)
	                    ExitLong("Exit_" + reason);
	                else
	                    ExitLong("Exit_" + reason, "LongEntry");
	                
	                // ✅ Send EXIT webhook for longs
	                SendWebhookExitSafe();
	            }
	            else if (Position.MarketPosition == MarketPosition.Short)
	            {
	                if (debug)
	                    Print($"{Time[0]} - Flattening SHORT due to {reason}");
	                if (ifvgOverrideActive)
	                    ExitShort("Exit_" + reason);
	                else
	                    ExitShort("Exit_" + reason, "ShortEntry");
	                
	                // ✅ Send EXIT webhook for shorts
	                SendWebhookExitSafe();
	            }

            longLinesActive = false;
            shortLinesActive = false;
            longSignalBar = -1;
            shortSignalBar = -1;
            longExitBar = -1;
            shortExitBar = -1;
        }
	
        private bool TimeInSkip(DateTime time)
        {
			EnsureActiveSession(time);
			if (activeSession == SessionSlot.None)
				return false;
			EnsureEffectiveTimes(time, false);
            TimeSpan now = time.TimeOfDay;

            bool inSkip1 = false;
            bool inSkip2 = false;
            bool inFomc = IsFomcSkipTime(time);

            // ✅ Skip1 only active if both are not 00:00:00
            if (effectiveSkipStart != TimeSpan.Zero && effectiveSkipEnd != TimeSpan.Zero)
            {
                inSkip1 = (effectiveSkipStart < effectiveSkipEnd)
                    ? (now >= effectiveSkipStart && now <= effectiveSkipEnd)
                    : (now >= effectiveSkipStart || now <= effectiveSkipEnd); // overnight handling
            }

            // ✅ Skip2 only active if both are not 00:00:00
            if (effectiveSkip2Start != TimeSpan.Zero && effectiveSkip2End != TimeSpan.Zero)
            {
                inSkip2 = (effectiveSkip2Start < effectiveSkip2End)
                    ? (now >= effectiveSkip2Start && now <= effectiveSkip2End)
                    : (now >= effectiveSkip2Start || now <= effectiveSkip2End); // overnight handling
            }

            return inSkip1 || inSkip2 || inFomc;
        }

		private bool TimeInSession(DateTime time)
		{
			EnsureActiveSession(time);
			if (activeSession == SessionSlot.None)
				return false;
			EnsureEffectiveTimes(time, false);
		    TimeSpan now = time.TimeOfDay;
		
		    if (effectiveSessionStart < effectiveSessionEnd)
		        return now >= effectiveSessionStart && now < effectiveSessionEnd;   // strictly less
		    else
		        return now >= effectiveSessionStart || now < effectiveSessionEnd;
		}

		private bool TimeInNoTradesAfter(DateTime time)
		{
			EnsureActiveSession(time);
			if (activeSession == SessionSlot.None)
				return false;
			EnsureEffectiveTimes(time, false);
			TimeSpan now = time.TimeOfDay;

			// If the session doesn’t cross midnight
			if (effectiveSessionStart < effectiveSessionEnd)
				return now >= effectiveNoTradesAfter && now < effectiveSessionEnd;

			// If the session crosses midnight
			return (now >= effectiveNoTradesAfter || now < effectiveSessionEnd);
		}

        private void DrawSessionBackground()
        {
            // Don't try drawing until we have a few bars
            if (CurrentBar < 1)
                return;

            // Find current bar time
            DateTime barTime = Time[0];
			EnsureActiveSession(barTime);
			if (activeSession == SessionSlot.None)
				return;
			EnsureEffectiveTimes(barTime, false);
            bool isOvernight = effectiveSessionStart > effectiveSessionEnd;

            DateTime sessionStartTime = barTime.Date + effectiveSessionStart;
            DateTime sessionEndTime = isOvernight
                ? sessionStartTime.AddDays(1).Date + effectiveSessionEnd
                : sessionStartTime.Date + effectiveSessionEnd;

            // Get the bar indexes (barsAgo) for start and end
            int startBarsAgo = Bars.GetBar(sessionStartTime);
            int endBarsAgo = Bars.GetBar(sessionEndTime);

            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            string tag = $"DUO_SessionFill_{activeSession}_{sessionStartTime:yyyyMMdd_HHmm}";

            if (DrawObjects[tag] == null)
            {
				// ✅ Use very high/low Y values to simulate full chart height
				Draw.Rectangle(
					this,
					tag,
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
			
			var r = new SolidColorBrush(Color.FromArgb(70, 255, 0, 0));

			// Calculate the exact DateTime for the London_NoTradesAfter time (on this chart date)
			//DateTime sessionStartTime = Time[0].Date + London_SessionStart;
			DateTime noTradesAfterTime = Time[0].Date + effectiveNoTradesAfter;

			// Handle overnight sessions (when London_SessionEnd < London_SessionStart)
			if (effectiveSessionStart > effectiveSessionEnd && effectiveNoTradesAfter < effectiveSessionStart)
				noTradesAfterTime = noTradesAfterTime.AddDays(1);

			// Draw the vertical line exactly at London_NoTradesAfter
			Draw.VerticalLine(this, $"NoTradesAfter_{activeSession}_{sessionStartTime:yyyyMMdd_HHmm}", noTradesAfterTime, r,
							DashStyleHelper.Solid, 2);

            DrawSkipWindow("Skip1", effectiveSkipStart, effectiveSkipEnd);
            DrawSkipWindow("Skip2", effectiveSkip2Start, effectiveSkip2End);
            DrawFomcWindow(barTime);
        }

        private void DrawSkipWindow(string tagPrefix, TimeSpan start, TimeSpan end)
        {
            // Only draw when both ends are configured
            if (start == TimeSpan.Zero || end == TimeSpan.Zero)
                return;

            DateTime barDate = Time[0].Date;

            DateTime windowStart = barDate + start;
            DateTime windowEnd = barDate + end;

            if (start > end)
                windowEnd = windowEnd.AddDays(1); // overnight skip window support

            int startBarsAgo = Bars.GetBar(windowStart);
            int endBarsAgo = Bars.GetBar(windowEnd);

            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            // Higher opacity fill, light dash-dot outlines
            var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));   // fill (~78% opaque)
            areaBrush.Freeze();
            var lineBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));    // lines (~35% opaque)
            lineBrush.Freeze();

            string rectTag = $"DUO_{tagPrefix}_Rect_{windowStart:yyyyMMdd}";
            Draw.Rectangle(
                this,
                rectTag,
                false,
                windowStart,
                0,
                windowEnd,
                30000,
                lineBrush,   // outline
                areaBrush,   // fill
                2
            ).ZOrder = -1;

            string startTag = $"DUO_{tagPrefix}_Start_{windowStart:yyyyMMdd}";
            Draw.VerticalLine(this, startTag, windowStart, lineBrush, DashStyleHelper.Solid, 2);

            string endTag = $"DUO_{tagPrefix}_End_{windowEnd:yyyyMMdd}";
            Draw.VerticalLine(this, endTag, windowEnd, lineBrush, DashStyleHelper.Solid, 2);
        }

        private void DrawFomcWindow(DateTime barTime)
        {
            if (!UseFomcSkip)
                return;

            EnsureFomcDatesInitialized();
            if (fomcDates == null || fomcDates.Count == 0)
                return;

            if (FomcSkipStart == FomcSkipEnd)
                return;

            if (!fomcDates.Contains(barTime.Date))
                return;

            DateTime windowStart = barTime.Date + FomcSkipStart;
            DateTime windowEnd = barTime.Date + FomcSkipEnd;
            if (FomcSkipStart > FomcSkipEnd)
                windowEnd = windowEnd.AddDays(1);

            int startBarsAgo = Bars.GetBar(windowStart);
            int endBarsAgo = Bars.GetBar(windowEnd);
            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));   // match regular skip fill
            areaBrush.Freeze();
            var lineBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));      // match regular skip lines
            lineBrush.Freeze();

            string rectTag = $"DUO_FOMC_Rect_{barTime:yyyyMMdd}";
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

            string startTag = $"DUO_FOMC_Start_{barTime:yyyyMMdd}";
            Draw.VerticalLine(this, startTag, windowStart, lineBrush, DashStyleHelper.Solid, 2);

            string endTag = $"DUO_FOMC_End_{barTime:yyyyMMdd}";
            Draw.VerticalLine(this, endTag, windowEnd, lineBrush, DashStyleHelper.Solid, 2);
        }

        private void EnsureEffectiveTimes(DateTime barTime, bool log)
        {
            if (activeSession == SessionSlot.None)
            {
                effectiveSessionStart = TimeSpan.Zero;
                effectiveSessionEnd = TimeSpan.Zero;
                effectiveNoTradesAfter = TimeSpan.Zero;
                effectiveSkipStart = TimeSpan.Zero;
                effectiveSkipEnd = TimeSpan.Zero;
                effectiveSkip2Start = TimeSpan.Zero;
                effectiveSkip2End = TimeSpan.Zero;
                return;
            }

            if (!activeAutoShiftTimes)
            {
                effectiveSessionStart = activeSessionStart;
                effectiveSessionEnd = activeSessionEnd;
                effectiveNoTradesAfter = activeNoTradesAfter;
                effectiveSkipStart = activeSkipStart;
                effectiveSkipEnd = activeSkipEnd;
                effectiveSkip2Start = activeSkip2Start;
                effectiveSkip2End = activeSkip2End;
                return;
            }

            DateTime date = barTime.Date;
            if (date == effectiveTimesDate)
                return;

            effectiveTimesDate = date;

            TimeSpan shift = GetLondonSessionShiftForDate(date);
            effectiveSessionStart = ShiftTime(activeSessionStart, shift);
            effectiveSessionEnd = ShiftTime(activeSessionEnd, shift);
            effectiveNoTradesAfter = ShiftTime(activeNoTradesAfter, shift);
            effectiveSkipStart = ShiftTime(activeSkipStart, shift);
            effectiveSkipEnd = ShiftTime(activeSkipEnd, shift);
            effectiveSkip2Start = ShiftTime(activeSkip2Start, shift);
            effectiveSkip2End = ShiftTime(activeSkip2End, shift);

            if (debug && log)
            {
                Print(
                    $"{date:yyyy-MM-dd} - AutoShiftTimes recompute | " +
                    $"Base SS={activeSessionStart:hh\\:mm} SE={activeSessionEnd:hh\\:mm} NTA={activeNoTradesAfter:hh\\:mm} | " +
                    $"Eff SS={effectiveSessionStart:hh\\:mm} SE={effectiveSessionEnd:hh\\:mm} NTA={effectiveNoTradesAfter:hh\\:mm} | " +
                    $"Shift={shift.TotalHours:0.##}h");
            }
        }

        private TimeSpan GetLondonSessionShiftForDate(DateTime date)
        {
            // Use midday UTC to avoid DST transition hour edge cases.
            DateTime utcSample = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);

            // Compute a dynamic baseline for this year so we only shift during UK/US DST mismatch weeks.
            // Jan 15 is a stable reference day (both typically on standard time).
            DateTime utcRef = new DateTime(date.Year, 1, 15, 12, 0, 0, DateTimeKind.Utc);

            TimeZoneInfo londonTz = GetLondonTimeZone();
            TimeZoneInfo targetTz = GetTargetTimeZone();

            TimeSpan baseline = londonTz.GetUtcOffset(utcRef) - targetTz.GetUtcOffset(utcRef);
            TimeSpan actual = londonTz.GetUtcOffset(utcSample) - targetTz.GetUtcOffset(utcSample);

            return baseline - actual;
        }

        private TimeSpan ShiftTime(TimeSpan baseTime, TimeSpan shift)
        {
            long ticks = (baseTime.Ticks + shift.Ticks) % TimeSpan.TicksPerDay;
            if (ticks < 0)
                ticks += TimeSpan.TicksPerDay;
            return new TimeSpan(ticks);
        }

        private TimeZoneInfo GetTargetTimeZone()
        {
            if (targetTimeZone != null)
                return targetTimeZone;

            try
            {
                // Prefer the Bars/data-series time zone (matches Time[0]) if available.
                // Use reflection to avoid compile-time dependency on specific NinjaTrader members.
                var bars = Bars;
                if (bars != null)
                {
                    var timeZoneProp = bars.GetType().GetProperty(
                        "TimeZoneInfo",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (timeZoneProp != null && typeof(TimeZoneInfo).IsAssignableFrom(timeZoneProp.PropertyType))
                        targetTimeZone = (TimeZoneInfo)timeZoneProp.GetValue(bars, null);
                }

                // Fallback to TradingHours template time zone.
                if (targetTimeZone == null)
                    targetTimeZone = Bars?.TradingHours?.TimeZoneInfo;
            }
            catch
            {
                targetTimeZone = null;
            }

            if (targetTimeZone == null)
                targetTimeZone = TimeZoneInfo.Local;

            return targetTimeZone;
        }

        private TimeZoneInfo GetEasternTimeZone()
        {
            if (easternTimeZone != null)
                return easternTimeZone;

            try
            {
                easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch
            {
                try
                {
                    easternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                }
                catch
                {
                    easternTimeZone = TimeZoneInfo.Utc;
                }
            }

            return easternTimeZone;
        }

        private void EnsureFomcDatesInitialized()
        {
            if (fomcDatesInitialized)
                return;

            fomcDatesInitialized = true;
            fomcDates = new HashSet<DateTime>();

            if (string.IsNullOrWhiteSpace(FomcDatesRaw))
                return;

            string[] entries = FomcDatesRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string entry in entries)
            {
                string trimmed = entry.Trim();
                if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                {
                    fomcDates.Add(date.Date);
                }
                else if (debug)
                {
                    Print($"Invalid FOMC date entry: {trimmed}");
                }
            }
        }

        private bool IsFomcSkipTime(DateTime time)
        {
            if (!UseFomcSkip)
                return false;

            EnsureFomcDatesInitialized();
            if (fomcDates == null || fomcDates.Count == 0)
                return false;

            if (!fomcDates.Contains(time.Date))
                return false;

            TimeSpan now = time.TimeOfDay;
            TimeSpan start = FomcSkipStart;
            TimeSpan end = FomcSkipEnd;

            if (start == end)
                return false;

            return start < end
                ? (now >= start && now < end)
                : (now >= start || now < end);
        }

        private TimeZoneInfo GetLondonTimeZone()
        {
            if (londonTimeZone != null)
                return londonTimeZone;

            try
            {
                // Windows time zone id (NinjaTrader runs on Windows).
                londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            }
            catch
            {
                try
                {
                    // Fallback for environments that support IANA ids.
                    londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
                }
                catch
                {
                    londonTimeZone = TimeZoneInfo.Utc;
                }
            }

            return londonTimeZone;
        }

        private double GetSLForLong()
        {
            if (activeSLPresetSetting == SLPreset.First_Candle_Percent)
            {
                double c1High = High[1];
                double c1Low = Low[1];
                double sl = c1High - (activeSLPercentFirstCandle / 100.0) * (c1High - c1Low);

                if (debug)
                    Print($"{Time[0]} - 📉 Long SL set at {activeSLPercentFirstCandle}% of Candle 1 range: {sl:0.00}");

                return sl - activeSLPadding;
            }

            double baseSL = First_Candle_High_Low ? Low[1] :
                            First_Candle_Open ? Open[1] :
                            Second_Candle_High_Low ? Low[0] :
                            Second_Candle_Open ? Open[0] :
                            Open[1];
            return baseSL - activeSLPadding;
        }

        private double GetSLForShort()
        {
            if (activeSLPresetSetting == SLPreset.First_Candle_Percent)
            {
                double c1High = High[1];
                double c1Low = Low[1];
                double sl = c1Low + (activeSLPercentFirstCandle / 100.0) * (c1High - c1Low);

                if (debug)
                    Print($"{Time[0]} - 📈 Short SL set at {activeSLPercentFirstCandle}% of Candle 1 range: {sl:0.00}");

                return sl + activeSLPadding;
            }

            double baseSL = First_Candle_High_Low ? High[1] :
                            First_Candle_Open ? Open[1] :
                            Second_Candle_High_Low ? High[0] :
                            Second_Candle_Open ? Open[0] :
                            Open[1];
            return baseSL + activeSLPadding;
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

        public void UpdateInfo() {
            UpdateInfoText();
        }

        public void UpdateInfoText()
        {
            var lines = BuildInfoLines();
            var font  = new SimpleFont("Consolas", 14); // monospaced

            int maxLabel = lines.Max(l => l.label.Length);
            int maxValue = Math.Max(1, lines.Max(l => l.value.Length));

            // 1) BACKGROUND BLOCK – uses visible chars but transparent text so
            //    NinjaTrader allocates full width (labels + values).
            string valuePlaceholder = new string('0', maxValue); // dummy width
            var bgLines = lines
                .Select(l => l.label.PadRight(maxLabel + 1) + valuePlaceholder)
                .ToArray();

            string bgText = string.Join(Environment.NewLine, bgLines);

            Draw.TextFixed(
                owner: this,
                tag: "myStatusLabel_bg",
                text: bgText,
                textPosition: TextPosition.BottomLeft,
                textBrush: Brushes.Transparent,  // text invisible
                font: font,
                outlineBrush: null,
                areaBrush: Brushes.Black,        // full background for whole block
                areaOpacity: 85);

            // 2) LABEL BLOCK – labels only, no background
            var labelLines = lines
                .Select(l => l.label)
                .ToArray();

            string labelText = string.Join(Environment.NewLine, labelLines);

            Draw.TextFixed(
                owner: this,
                tag: "myStatusLabel_labels",
                text: labelText,
                textPosition: TextPosition.BottomLeft,
                textBrush: Brushes.LightGray,
                font: font,
                outlineBrush: null,
                areaBrush: null,
                areaOpacity: 0);

            // 3) VALUE OVERLAYS – one block per line, only that line has a value
            string spacesBeforeValue = new string(' ', maxLabel + 1);

            for (int i = 0; i < lines.Count; i++)
            {
                string tag = $"myStatusLabel_val_{i}";

                var overlayLines = new string[lines.Count];
                for (int j = 0; j < lines.Count; j++)
                {
                    overlayLines[j] = (j == i)
                        ? spacesBeforeValue + lines[i].value   // value at value column
                        : string.Empty;                        // blank line
                }

                string overlayText = string.Join(Environment.NewLine, overlayLines);

                Draw.TextFixed(
                    owner: this,
                    tag: tag,
                    text: overlayText,
                    textPosition: TextPosition.BottomLeft,
                    textBrush: lines[i].brush,
                    font: font,
                    outlineBrush: null,
                    areaBrush: null,
                    areaOpacity: 0);
            }
        }

        private List<(string label, string value, Brush brush)> BuildInfoLines()
        {
            var lines = new List<(string label, string value, Brush brush)>();

            string tpLine, slLine;
            GetPnLLines(out tpLine, out slLine);

            lines.Add(("TP:        ", $"{tpLine}", Brushes.LimeGreen));
            lines.Add(("SL:        ", $"{slLine}", Brushes.IndianRed));
            lines.Add(("Contracts: ", $"{activeContracts}", Brushes.LightGray));
            //lines.Add(("Anti Hedge:", AntiHedge ? "✅" : "⛔", AntiHedge ? Brushes.LimeGreen : Brushes.IndianRed));
            lines.Add((FormatSessionLabel(activeSession), string.Empty, Brushes.LightGray));

            var version = $"v{GetAddOnVersion()}";
            lines.Add(($"{version}", string.Empty, Brushes.LightGray));

            return lines;
        }

        private void GetPnLLines(out string tpLine, out string slLine)
        {
            // --- Detect state ---
            bool hasPosition = Position.MarketPosition != MarketPosition.Flat;

            bool hasLongOrder =
                longEntryOrder != null &&
                (longEntryOrder.OrderState == OrderState.Working ||
                longEntryOrder.OrderState == OrderState.Submitted ||
                longEntryOrder.OrderState == OrderState.Accepted ||
                longEntryOrder.OrderState == OrderState.ChangePending);

            bool hasShortOrder =
                shortEntryOrder != null &&
                (shortEntryOrder.OrderState == OrderState.Working ||
                shortEntryOrder.OrderState == OrderState.Submitted ||
                shortEntryOrder.OrderState == OrderState.Accepted ||
                shortEntryOrder.OrderState == OrderState.ChangePending);

            // --- New logic: also consider our own flags ---
            bool hasPendingLong  = longOrderPlaced && !hasPosition;
            bool hasPendingShort = shortOrderPlaced && !hasPosition;

            // Show $0 only if no position and nothing pending
            if (!hasPosition && !hasLongOrder && !hasShortOrder && !hasPendingLong && !hasPendingShort)
            {
                tpLine = "$0";
                slLine = "$0";
                return;
            }

            // --- Tick value per instrument ---
            double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
            if (Instrument.MasterInstrument.Name == "MNQ") tickValue = 0.50;
            else if (Instrument.MasterInstrument.Name == "NQ") tickValue = 5.00;

            double entry = 0, tp = 0, sl = 0;

            // --- Active position ---
            if (hasPosition)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    entry = currentLongEntry;
                    tp = currentLongTP;
                    sl = currentLongSL;
                }
                else
                {
                    entry = currentShortEntry;
                    tp = currentShortTP;
                    sl = currentShortSL;
                }
            }
            // --- Pending long order ---
            else if (hasLongOrder || hasPendingLong)
            {
                entry = currentLongEntry;
                tp = currentLongTP;
                sl = currentLongSL;
            }
            // --- Pending short order ---
            else if (hasShortOrder || hasPendingShort)
            {
                entry = currentShortEntry;
                tp = currentShortTP;
                sl = currentShortSL;
            }

            // Guard: if anything not set, show zero
            if (entry == 0 || tp == 0 || sl == 0)
            {
                tpLine = "$0";
                slLine = "$0";
                return;
            }

            // --- Compute TP/SL distances ---
            double tpTicks = Math.Abs(tp - entry) / TickSize;
            double slTicks = Math.Abs(entry - sl) / TickSize;

            double tpDollars = tpTicks * tickValue * activeContracts;
            double slDollars = slTicks * tickValue * activeContracts;

            tpLine = $"${tpDollars:0}";
            slLine = $"${slDollars:0}";
        }

        string GetAddOnVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Version version = assembly.GetName().Version;
            return version.ToString();
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
                        if (debug)
                            Print("Has Opposite Order");
                        return true;
                    }
                }
            }
            if (debug)
                Print("Has No Opposite Order");
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
                        if (debug)
                            Print("Has Opposite Position");
                        return true;
                    }
                }
            }
            if (debug)
                Print("Has No Opposite Position");
            return false;
        }

        private string BuildHeartbeatId()
        {
            string baseName = Name ?? GetType().Name;
            string instrumentName = Instrument != null ? Instrument.FullName : "UnknownInstrument";
            string accountName = Account != null ? Account.Name : "UnknownAccount";
            string barsInfo = BarsPeriod != null
                ? $"{BarsPeriod.BarsPeriodType}-{BarsPeriod.Value}"
                : "NoBars";

            string configKey = $"{activeContracts}-{activeOffsetPerc}-{activeTpPerc}-{activeCancelPerc}-{activeDeviationPerc}-{activeSLPadding}-{activeSLPresetSetting}-{activeMinCandlePoints}";

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
                if (debug)
                    Print($"⚠️ Heartbeat write error: {ex.Message}");
            }
        }

        private enum IfvgDirection
        {
            Long,
            Short
        }

        public enum SessionSlot
        {
            None,
            Session1,
            Session2
        }

        private DateTime lastSessionEvalTime = DateTime.MinValue;

        private void EnsureActiveSession(DateTime time)
        {
            if (time <= lastSessionEvalTime)
                return;
            lastSessionEvalTime = time;

            SessionSlot desired = DetermineSessionForTime(time);
            if (!sessionInitialized || desired != activeSession)
            {
                if (debug)
                {
                    GetSessionWindowForSession(SessionSlot.Session1, time.Date, out TimeSpan s1Start, out TimeSpan s1End);
                    GetSessionWindowForSession(SessionSlot.Session2, time.Date, out TimeSpan s2Start, out TimeSpan s2End);
                    Print($"{time} - Session switch: desired={desired} | S1={s1Start:hh\\:mm}-{s1End:hh\\:mm} (AutoShift={AutoShiftSession1}) | S2={s2Start:hh\\:mm}-{s2End:hh\\:mm} (AutoShift={AutoShiftSession2})");
                }

                activeSession = desired;
                if (activeSession != SessionSlot.None)
                {
                    ApplyInputsForSession(activeSession);
                    sessionStartCumProfit = GetTotalCumProfitPoints();
                    sessionGainLimitReached = false;
                }
                effectiveTimesDate = DateTime.MinValue;
                sessionInitialized = true;
            }
        }

        private SessionSlot DetermineSessionForTime(DateTime time)
        {
            TimeSpan now = time.TimeOfDay;

            GetSessionWindowForSession(SessionSlot.Session1, time.Date, out TimeSpan session1Start, out TimeSpan session1End);
            GetSessionWindowForSession(SessionSlot.Session2, time.Date, out TimeSpan session2Start, out TimeSpan session2End);

            bool session1Configured = IsSessionConfigured(SessionSlot.Session1);
            bool session2Configured = IsSessionConfigured(SessionSlot.Session2);

            if (session1Configured && IsTimeInRange(now, session1Start, session1End))
                return SessionSlot.Session1;
            if (session2Configured && IsTimeInRange(now, session2Start, session2End))
                return SessionSlot.Session2;

            if (!session1Configured && !session2Configured)
                return SessionSlot.None;

            DateTime nextSession1Start = DateTime.MaxValue;
            DateTime nextSession2Start = DateTime.MaxValue;

            if (session1Configured)
            {
                nextSession1Start = time.Date + session1Start;
                if (nextSession1Start <= time)
                    nextSession1Start = nextSession1Start.AddDays(1);
            }

            if (session2Configured)
            {
                nextSession2Start = time.Date + session2Start;
                if (nextSession2Start <= time)
                    nextSession2Start = nextSession2Start.AddDays(1);
            }

            return nextSession1Start <= nextSession2Start
                ? SessionSlot.Session1
                : SessionSlot.Session2;
        }

        private string FormatSessionLabel(SessionSlot session)
        {
            switch (session)
            {
                case SessionSlot.Session1:
                    return "London";
                case SessionSlot.Session2:
                    return "New York";
                default:
                    return "Outside Sessions";
            }
        }

        private bool IsTimeInRange(TimeSpan now, TimeSpan start, TimeSpan end)
        {
            if (start < end)
                return now >= start && now < end;

            return now >= start || now < end;
        }

        private bool CrossedSessionEnd(DateTime previousTime, DateTime currentTime)
        {
            return CrossedSessionEndForSession(SessionSlot.Session1, previousTime, currentTime)
                || CrossedSessionEndForSession(SessionSlot.Session2, previousTime, currentTime);
        }

        private bool CrossedSessionEndForSession(
            SessionSlot session,
            DateTime previousTime,
            DateTime currentTime)
        {
            if (!IsSessionConfigured(session))
                return false;

            GetSessionWindowForSession(session, currentTime.Date, out TimeSpan start, out TimeSpan end);

            bool wasInSession = IsTimeInRange(previousTime.TimeOfDay, start, end);
            bool nowInSession = IsTimeInRange(currentTime.TimeOfDay, start, end);
            return wasInSession && !nowInSession;
        }

        private void GetSessionWindowForSession(
            SessionSlot session,
            DateTime date,
            out TimeSpan start,
            out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;
            bool autoShift = false;

            switch (session)
            {
                case SessionSlot.Session1:
                    start = London_SessionStart;
                    end = London_SessionEnd;
                    autoShift = AutoShiftSession1;
                    break;
                case SessionSlot.Session2:
                    start = NewYork_SessionStart;
                    end = NewYork_SessionEnd;
                    autoShift = AutoShiftSession2;
                    break;
            }

            if (autoShift)
            {
                TimeSpan shift = GetLondonSessionShiftForDate(date);
                start = ShiftTime(start, shift);
                end = ShiftTime(end, shift);
            }
        }

        private bool IsSessionConfigured(SessionSlot session)
        {
            switch (session)
            {
                case SessionSlot.Session1:
                    return UseSession1 && London_SessionStart != London_SessionEnd;
                case SessionSlot.Session2:
                    return UseSession2 && NewYork_SessionStart != NewYork_SessionEnd;
                default:
                    return false;
            }
        }

        private void ApplyInputsForSession(SessionSlot session)
        {
            switch (session)
            {
                case SessionSlot.Session1:
                    activeAutoShiftTimes = AutoShiftSession1;
                    activeContracts = London_Contracts;
                    activeMinC12Body  = London_MinC12Body;
                    activeMaxC12Body  = London_MaxC12Body;
                    activeMinCandlePoints = London_MinCandlePoints;
                    activeOffsetPerc  = London_OffsetPerc;
                    activeTpPerc      = London_TpPerc;
                    activeCancelPerc  = London_CancelPerc;
                    activeDeviationPerc = London_DeviationPerc;
                    activeSLPadding = London_SLPadding;
                    activeMaxSLTPRatioPerc = London_MaxSLTPRatioPerc;
                    activeSLPresetSetting = London_SLPresetSetting;
                    activeSLPercentFirstCandle = London_SLPercentFirstCandle;
                    activeMaxSLPoints = London_MaxSLPoints;
                    activeMaxSessionGain = London_MaxSessionGain;
                    activeUseMarketEntry = London_UseMarketEntry;

                    activeSessionStart  = London_SessionStart;
                    activeSessionEnd    = London_SessionEnd;
                    activeNoTradesAfter = London_NoTradesAfter;
                    activeSkipStart = London_SkipStart;
                    activeSkipEnd = London_SkipEnd;
                    activeSkip2Start = London_Skip2Start;
                    activeSkip2End = London_Skip2End;
                    activeIfvgMinSizePoints = London_IfvgMinSizePoints;
                    activeIfvgMaxSizePoints = London_IfvgMaxSizePoints;
                    activeIfvgMinTpSlDistancePoints = London_IfvgMinTpSlDistancePoints;
                    activeIfvgSwingStrength = London_IfvgSwingStrength;
                    activeIfvgSwingDrawBars = London_IfvgSwingDrawBars;
                    activeIfvgMaxBarsBetweenSweepAndIfvg = London_IfvgMaxBarsBetweenSweepAndIfvg;
                    activeIfvgUseBreakEvenWickLine = London_IfvgUseBreakEvenWickLine;
                    activeIfvgUseVolumeSmaFilter = London_IfvgUseVolumeSmaFilter;
                    activeIfvgVolumeFastSmaPeriod = London_IfvgVolumeFastSmaPeriod;
                    activeIfvgVolumeSlowSmaPeriod = London_IfvgVolumeSlowSmaPeriod;
                    activeIfvgVolumeSmaMultiplier = London_IfvgVolumeSmaMultiplier;
                    ifvgVolumeFastSmaActive = ifvgVolumeFastSmaLondon;
                    ifvgVolumeSlowSmaActive = ifvgVolumeSlowSmaLondon;
                    break;
                case SessionSlot.Session2:
                    activeAutoShiftTimes = AutoShiftSession2;
                    activeContracts = NewYork_Contracts;
                    activeMinC12Body  = NewYork_MinC12Body;
                    activeMaxC12Body  = NewYork_MaxC12Body;
                    activeMinCandlePoints = NewYork_MinCandlePoints;
                    activeOffsetPerc  = NewYork_OffsetPerc;
                    activeTpPerc      = NewYork_TpPerc;
                    activeCancelPerc  = NewYork_CancelPerc;
                    activeDeviationPerc = NewYork_DeviationPerc;
                    activeSLPadding = NewYork_SLPadding;
                    activeMaxSLTPRatioPerc = NewYork_MaxSLTPRatioPerc;
                    activeSLPresetSetting = NewYork_SLPresetSetting;
                    activeSLPercentFirstCandle = NewYork_SLPercentFirstCandle;
                    activeMaxSLPoints = NewYork_MaxSLPoints;
                    activeMaxSessionGain = NewYork_MaxSessionGain;
                    activeUseMarketEntry = NewYork_UseMarketEntry;

                    activeSessionStart  = NewYork_SessionStart;
                    activeSessionEnd    = NewYork_SessionEnd;
                    activeNoTradesAfter = NewYork_NoTradesAfter;
                    activeSkipStart = NewYork_SkipStart;
                    activeSkipEnd = NewYork_SkipEnd;
                    activeSkip2Start = NewYork_Skip2Start;
                    activeSkip2End = NewYork_Skip2End;
                    activeIfvgMinSizePoints = NewYork_IfvgMinSizePoints;
                    activeIfvgMaxSizePoints = NewYork_IfvgMaxSizePoints;
                    activeIfvgMinTpSlDistancePoints = NewYork_IfvgMinTpSlDistancePoints;
                    activeIfvgSwingStrength = NewYork_IfvgSwingStrength;
                    activeIfvgSwingDrawBars = NewYork_IfvgSwingDrawBars;
                    activeIfvgMaxBarsBetweenSweepAndIfvg = NewYork_IfvgMaxBarsBetweenSweepAndIfvg;
                    activeIfvgUseBreakEvenWickLine = NewYork_IfvgUseBreakEvenWickLine;
                    activeIfvgUseVolumeSmaFilter = NewYork_IfvgUseVolumeSmaFilter;
                    activeIfvgVolumeFastSmaPeriod = NewYork_IfvgVolumeFastSmaPeriod;
                    activeIfvgVolumeSlowSmaPeriod = NewYork_IfvgVolumeSlowSmaPeriod;
                    activeIfvgVolumeSmaMultiplier = NewYork_IfvgVolumeSmaMultiplier;
                    ifvgVolumeFastSmaActive = ifvgVolumeFastSmaNewYork;
                    ifvgVolumeSlowSmaActive = ifvgVolumeSlowSmaNewYork;
                    break;
            }
        }
        private void ApplyStopLossPreset(SLPreset preset)
        {
            switch (preset)
            {
                case SLPreset.First_Candle_High_Low:
                    First_Candle_High_Low = true;
                    First_Candle_Open = false;
                    Second_Candle_High_Low = false;
                    Second_Candle_Open = false;
                    break;
                case SLPreset.First_Candle_Open:
                    First_Candle_High_Low = false;
                    First_Candle_Open = true;
                    Second_Candle_High_Low = false;
                    Second_Candle_Open = false;
                    break;
                case SLPreset.Second_Candle_High_Low:
                    First_Candle_High_Low = false;
                    First_Candle_Open = false;
                    Second_Candle_High_Low = true;
                    Second_Candle_Open = false;
                    break;
                case SLPreset.Second_Candle_Open:
                    First_Candle_High_Low = false;
                    First_Candle_Open = false;
                    Second_Candle_High_Low = false;
                    Second_Candle_Open = true;
                    break;
				case SLPreset.First_Candle_Percent:
                    First_Candle_High_Low = false;
                    First_Candle_Open = false;
                    Second_Candle_High_Low = false;
                    Second_Candle_Open = false;
                    break;
            }

            //Print($"== SL PRESET APPLIED: {preset} ==");
        }

        public enum SLPreset
	    {
	        First_Candle_High_Low,
	        First_Candle_Open,
	        Second_Candle_High_Low,
	        Second_Candle_Open,
			First_Candle_Percent
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

	        private void QueueEntryWebhookLong(double entryPrice, double takeProfit, double stopLoss)
	        {
	            if (Position.MarketPosition != MarketPosition.Flat)
	                return;

	            pendingLongWebhook = true;
	            pendingLongEntry = RoundToTickSize(entryPrice);
	            pendingLongTP = RoundToTickSize(takeProfit);
	            pendingLongSL = RoundToTickSize(stopLoss);
	        }

	        private void QueueEntryWebhookShort(double entryPrice, double takeProfit, double stopLoss)
	        {
	            if (Position.MarketPosition != MarketPosition.Flat)
	                return;

	            pendingShortWebhook = true;
	            pendingShortEntry = RoundToTickSize(entryPrice);
	            pendingShortTP = RoundToTickSize(takeProfit);
	            pendingShortSL = RoundToTickSize(stopLoss);
	        }

	        private void SendWebhookCancelSafe(Order order)
	        {
	            string orderId = order != null ? order.OrderId : null;
	            double limitPrice = 0;
	            if (order != null && order.LimitPrice > 0)
	                limitPrice = RoundToTickSize(order.LimitPrice);
	            SendWebhookCancelSafe(orderId, limitPrice);
	        }

	        private void SendWebhookCancelSafe(string orderId = null, double limitPrice = 0)
	        {
	            if (Position.MarketPosition != MarketPosition.Flat)
	                return;

	            bool sameBar = CurrentBar == lastCancelWebhookBar;
	            bool sameOrder = string.Equals(lastCancelWebhookOrderId ?? string.Empty, orderId ?? string.Empty, StringComparison.Ordinal);
	            bool samePrice = PricesEqual(lastCancelWebhookLimitPrice, limitPrice);
	            if (sameBar && sameOrder && samePrice)
	                return;

	            lastCancelWebhookBar = CurrentBar;
	            lastCancelWebhookOrderId = orderId;
	            lastCancelWebhookLimitPrice = limitPrice;
	            SendWebhook("cancel");
	        }

	        private void SendWebhookExitSafe(string executionId = null)
	        {
	            if (CurrentBar == lastExitWebhookBar)
	            {
	                if (string.IsNullOrEmpty(executionId) ||
	                    string.IsNullOrEmpty(lastExitWebhookExecutionId) ||
	                    string.Equals(lastExitWebhookExecutionId, executionId, StringComparison.Ordinal))
                    {
                        if (debug)
                            Print($"{Time[0]} - ⏭️ Exit webhook suppressed (idempotent guard).");
	                    return;
                    }
	            }

	            lastExitWebhookBar = CurrentBar;
	            lastExitWebhookExecutionId = executionId;
	            if (debug)
	                Print($"{Time[0]} - 🚀 Sending EXIT webhook (execId={executionId ?? "n/a"}).");
	            SendWebhook("exit");
	        }

        private void SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false)
        {
            if (State != State.Realtime)
            return;

            if (string.IsNullOrEmpty(WebhookUrl))
                return;

            try
            {
                string ticker = GetTicker(Instrument);
                double roundedEntry = RoundToTickSize(entryPrice);
                double roundedTP = RoundToTickSize(takeProfit);
                double roundedSL = RoundToTickSize(stopLoss);
                string entryStr = FormatPriceInvariant(roundedEntry);
                string tpStr = FormatPriceInvariant(roundedTP);
                string slStr = FormatPriceInvariant(roundedSL);

                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
                string json = "";
                string payloadLog = "";

                switch (eventType.ToLower())
                {
                    case "buy":
                    case "sell":
                        if (isMarketEntry)
                        {
                            json = $@"
                        {{
                            ""ticker"": ""{ticker}"",
                            ""action"": ""{eventType}"",
                            ""orderType"": ""market"",
                            ""quantityType"": ""fixed_quantity"",
                            ""quantity"": {activeContracts},
                            ""signalPrice"": {entryStr},
                            ""time"": ""{time}"",
                            ""takeProfit"": {{
                                ""limitPrice"": {tpStr}
                            }},
                            ""stopLoss"": {{
                                ""type"": ""stop"",
                                ""stopPrice"": {slStr}
                            }}
                        }}";
                            payloadLog = $"{eventType.ToUpper()} market tp={tpStr} sl={slStr}";
                        }
                        else
                        {
                            json = $@"
                        {{
                            ""ticker"": ""{ticker}"",
                            ""action"": ""{eventType}"",
                            ""orderType"": ""limit"",
                            ""limitPrice"": {entryStr},
                            ""quantityType"": ""fixed_quantity"",
                            ""quantity"": {activeContracts},
                            ""signalPrice"": {entryStr},
                            ""time"": ""{time}"",
                            ""takeProfit"": {{
                                ""limitPrice"": {tpStr}
                            }},
                            ""stopLoss"": {{
                                ""type"": ""stop"",
                                ""stopPrice"": {slStr}
                            }}
                        }}";
                            payloadLog = $"{eventType.ToUpper()} limit={entryStr} tp={tpStr} sl={slStr}";
                        }
                        break;

	                    case "exit":
	                        json = $@"
	                        {{
	                            ""ticker"": ""{ticker}"",
	                            ""action"": ""exit"",
	                            ""orderType"": ""market"",
	                            ""quantityType"": ""fixed_quantity"",
	                            ""quantity"": {activeContracts},
	                            ""cancel"": true,
	                            ""time"": ""{time}""
	                        }}";
	                        payloadLog = "EXIT market";
	                        break;

                    case "cancel":
                        json = $@"
                        {{
                            ""ticker"": ""{ticker}"",
                            ""action"": ""cancel"",
                            ""time"": ""{time}""
                        }}";
                        payloadLog = "CANCEL";
                        break;
                }


                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.UploadString(WebhookUrl, "POST", json);
                }

                if (!string.IsNullOrEmpty(payloadLog))
                    Print($"📤 Webhook payload: {payloadLog} ({ticker})");
                Print($"✅ Webhook sent to TradersPost: {eventType.ToUpper()} for {ticker}");
            }
            catch (Exception ex)
            {
                Print($"⚠️ Webhook error: {ex.Message}");
            }
        }
	    }
	}
