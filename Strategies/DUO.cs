#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class DUO : Strategy
    {
        private const string HeartbeatStrategyName = "DUO";

        public DUO()
        {
            VendorLicense(337);
        }

        public enum InitialStopMode
        {
            WickExtreme
        }

        public enum SessionTradeDirection
        {
            Both,
            LongOnly,
            ShortOnly
        }

        public enum TakeProfitStopMode
        {
            PercentMove,
            AtrTrail
        }

        private sealed class TradeLineSnapshot
        {
            public int StartBar;
            public int EndBar;
            public double EntryPrice;
            public double StopPrice;
            public double TakeProfitPrice;
            public double TakeProfitTriggerPrice;
            public bool HasTakeProfit;
            public bool HasTakeProfitTrigger;
        }

        // Commercial (closed list) option: uncomment these converters and the [TypeConverter(...)] lines
        // on the momentum properties to switch back from free input to dropdown presets.
        // private sealed class AsiaAdxSlopeDropdownConverter : System.ComponentModel.DoubleConverter
        // {
        //     private static readonly double[] Presets = new double[]
        //     {
        //         1.15, 1.16, 1.17, 1.18, 1.19, 1.20, 1.21, 1.22, 1.23, 1.24, 1.25
        //     };
        //
        //     public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        //     {
        //         return true;
        //     }
        //
        //     public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        //     {
        //         return true;
        //     }
        //
        //     public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        //     {
        //         return new TypeConverter.StandardValuesCollection(Presets);
        //     }
        // }
        //
        // private sealed class NewYorkAdxSlopeDropdownConverter : System.ComponentModel.DoubleConverter
        // {
        //     private static readonly double[] Presets = new double[]
        //     {
        //         1.58, 1.59, 1.60, 1.61, 1.62, 1.63, 1.64, 1.65, 1.66, 1.67, 1.68
        //     };
        //
        //     public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        //     {
        //         return true;
        //     }
        //
        //     public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        //     {
        //         return true;
        //     }
        //
        //     public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        //     {
        //         return new TypeConverter.StandardValuesCollection(Presets);
        //     }
        // }

        private enum SessionSlot
        {
            None,
            Asia,
            Asia2,
            Asia3,
            London,
            London2,
            London3,
            NewYork,
            NewYork2,
            NewYork3
        }

        private enum SessionFamily
        {
            None,
            Asia,
            London,
            NewYork
        }

        public enum WebhookProvider
        {
            TradersPost,
            ProjectX
        }

        private sealed class ProjectXAccountInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool CanTrade { get; set; }
            public bool IsVisible { get; set; }
        }

        private sealed class ProjectXContractInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string SymbolId { get; set; }
            public bool ActiveContract { get; set; }
        }

        private Order longEntryOrder;
        private Order shortEntryOrder;
        private int missingLongEntryOrderBars;
        private int missingShortEntryOrderBars;
        private string webhookUrl = string.Empty;
        private string webhookTickerOverride = string.Empty;
        private double asiaAdxMinSlopePoints;
        private double asia2AdxMinSlopePoints;
        private double asia3AdxMinSlopePoints;
        private double londonAdxMinSlopePoints;
        private double london2AdxMinSlopePoints;
        private double london3AdxMinSlopePoints;
        private double newYorkAdxMinSlopePoints;
        private double newYork2AdxMinSlopePoints;
        private double newYork3AdxMinSlopePoints;
        private TimeZoneInfo targetTimeZone;
        private TimeZoneInfo londonTimeZone;
        private StrategyHeartbeatReporter heartbeatReporter;

        private bool asiaSessionClosed;
        private bool asia2SessionClosed;
        private bool asia3SessionClosed;
        private bool londonSessionClosed;
        private bool london2SessionClosed;
        private bool london3SessionClosed;
        private bool newYorkSessionClosed;
        private bool newYork2SessionClosed;
        private bool newYork3SessionClosed;

        private bool sessionInitialized;
        private SessionSlot activeSession = SessionSlot.None;
        private SessionSlot lockedTradeSession = SessionSlot.None;
        private int lastDeferredSessionLogBar = -1;
        private bool tradeAttemptOpen;
        private int tradeAttemptId;
        private string tradeAttemptSide = string.Empty;
        private int tradeLineTagCounter;
        private string tradeLineTagPrefix = string.Empty;
        private bool tradeLinesActive;
        private bool tradeLineHasTp;
        private bool tradeLineHasTpTrigger;
        private int tradeLineSignalBar = -1;
        private int tradeLineExitBar = -1;
        private double tradeLineEntryPrice;
        private double tradeLineTpPrice;
        private double tradeLineTpTriggerPrice;
        private double tradeLineSlPrice;
        private readonly List<TradeLineSnapshot> historicalTradeLines = new List<TradeLineSnapshot>();

        private EMA emaAsia;
        private EMA emaAsia2;
        private EMA emaAsia3;
        private EMA emaLondon;
        private EMA emaLondon2;
        private EMA emaLondon3;
        private EMA emaNewYork;
        private EMA emaNewYork2;
        private EMA emaNewYork3;
        private EMA activeEma;
        private ATR takeProfitAtr;
        private DUOAtrVisual atrVisual;
        private DM adxAsia;
        private DM adxAsia2;
        private DM adxAsia3;
        private DM adxLondon;
        private DM adxLondon2;
        private DM adxLondon3;
        private DM adxNewYork;
        private DM adxNewYork2;
        private DM adxNewYork3;
        private DM activeAdx;

        private int activeEmaPeriod;
        private int activeContracts;
        private SessionTradeDirection activeTradeDirection = SessionTradeDirection.Both;
        private InitialStopMode activeEntryStopMode;
        private double activeEmaMinSlopePointsPerBar;
        private double activeMaxEntryDistanceFromEmaPoints;
        private double activeExitCrossPoints;
        private double activeFlipEmaCrossPoints;
        private double activeTakeProfitPoints;
        private double activeMinimumAtrForEntry;
        private int activeAdxPeriod;
        private double activeAdxThreshold;
        private double activeFlipAdxThreshold;
        private double activeAdxMaxThreshold;
        private double activeAdxMinSlopePoints;
        private double activeAdxPeakDrawdownExitUnits;
        private double activeAdxAbsoluteExitLevel;
        private double activeStopPaddingPoints;
        private double activeHvSlPaddingPoints;
        private TimeSpan activeHvSlStartTime;
        private TimeSpan activeHvSlEndTime;
        private double activeEntryOffsetPoints;
        private bool activeEnableFlipBreakEven;
        private double activeFlipBreakEvenTriggerPoints;
        private double activeFlipTakeProfitPoints;
        private double activeTakeProfitPercentTriggerPercent;
        private TakeProfitStopMode activeTakeProfitStopMode = TakeProfitStopMode.PercentMove;
        private double activeTakeProfitAtrTrailMultiplier;
        private double activeTakeProfitPercentStopMovePercent;
        private bool activeRequireMinAdxForFlips;
        private bool activeEnableAdxDdRiskMode;
        private double activeAdxDdRiskModeStopLossPoints;
        private double activeAdxDdRiskModeTakeProfitPoints;
        private int activeHorizontalExitBars;
        private double activeMaxStopLossPoints;

        private double pendingLongStopForWebhook;
        private double pendingShortStopForWebhook;
        private double currentTradePeakAdx;
        private MarketPosition trackedAdxPeakPosition = MarketPosition.Flat;
        private string projectXSessionToken;
        private DateTime projectXTokenAcquiredUtc = Core.Globals.MinDate;
        private List<ProjectXAccountInfo> projectXAccounts;
        private readonly Dictionary<string, long> projectXLastOrderIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private string projectXResolvedContractId;
        private string projectXResolvedInstrumentKey = string.Empty;
        private bool accountBalanceLimitReached;
        private int accountBalanceLimitReachedBar = -1;
        private int asiaTradesThisSession;
        private int londonTradesThisSession;
        private int newYorkTradesThisSession;
        private string currentPositionEntrySignal = string.Empty;
        private bool currentPositionIsFlipEntry;
        private bool pendingLongEntryIsFlip;
        private bool pendingShortEntryIsFlip;
        private bool suppressProjectXNextExecutionExitWebhook;
        private bool flipBreakEvenActivated;
        private bool takeProfitStopTriggered;
        private double initialStopPrice;
        private double currentStopPrice;
        private bool adxDdRiskModeApplied;
        private int currentPositionEntryBar = -1;
        private Order activeStopLossOrder;
        private Order activeProfitTargetOrder;
        private Order activeExitOrder;
        private DateTime protectionAuditGraceUntilUtc = Core.Globals.MinDate;
        private bool isConfiguredTimeframeValid = true;
        private bool isConfiguredInstrumentValid = true;
        private bool timeframePopupShown;
        private bool instrumentPopupShown;
        private const double FlipBodyThresholdPercent = 0.0;
        private const int TakeProfitAtrPeriod = 14;
        private const string LongEntrySignal = "DUOLong";
        private const string ShortEntrySignal = "DUOShort";
        private const string LongFlipEntrySignal = "DUOLong";
        private const string ShortFlipEntrySignal = "DUOShort";
        private static readonly SessionSlot[] ConfigurableSessionSlots = new[]
        {
            SessionSlot.Asia,
            SessionSlot.Asia2,
            SessionSlot.Asia3,
            SessionSlot.London,
            SessionSlot.London2,
            SessionSlot.London3,
            SessionSlot.NewYork,
            SessionSlot.NewYork2,
            SessionSlot.NewYork3
        };
        private static readonly Brush PassedNewsRowBrush = CreateFrozenBrush(30, 211, 211, 211);
        private static readonly string NewsDatesRaw =
@"2025-01-02,08:30
2025-01-08,08:30
2025-01-08,14:00
2025-01-10,08:30
2025-01-14,08:30
2025-01-15,08:30
2025-01-16,08:30
2025-01-23,08:30
2025-01-29,14:00
2025-01-30,08:30
2025-01-31,08:30
2025-02-06,08:30
2025-02-07,08:30
2025-02-12,08:30
2025-02-13,08:30
2025-02-14,08:30
2025-02-19,14:00
2025-02-20,08:30
2025-02-27,08:30
2025-02-28,08:30
2025-03-06,08:30
2025-03-07,08:30
2025-03-12,08:30
2025-03-13,08:30
2025-03-17,08:30
2025-03-19,14:00
2025-03-20,08:30
2025-03-27,08:30
2025-03-28,08:30
2025-04-03,08:30
2025-04-04,08:30
2025-04-09,14:00
2025-04-10,08:30
2025-04-11,08:30
2025-04-16,08:30
2025-04-17,08:30
2025-04-24,08:30
2025-04-30,08:30
2025-05-01,08:30
2025-05-02,08:30
2025-05-07,14:00
2025-05-08,08:30
2025-05-13,08:30
2025-05-15,08:30
2025-05-22,08:30
2025-05-28,14:00
2025-05-29,08:30
2025-05-30,08:30
2025-06-05,08:30
2025-06-06,08:30
2025-06-11,08:30
2025-06-12,08:30
2025-06-17,08:30
2025-06-18,08:30
2025-06-18,14:00
2025-06-26,08:30
2025-06-27,08:30
2025-07-03,08:30
2025-07-09,14:00
2025-07-10,08:30
2025-07-15,08:30
2025-07-16,08:30
2025-07-17,08:30
2025-07-24,08:30
2025-07-30,08:30
2025-07-30,14:00
2025-07-31,08:30
2025-08-01,08:30
2025-08-07,08:30
2025-08-12,08:30
2025-08-14,08:30
2025-08-15,08:30
2025-08-20,14:00
2025-08-21,08:30
2025-08-28,08:30
2025-08-29,08:30
2025-09-04,08:30
2025-09-05,08:30
2025-09-10,08:30
2025-09-11,08:30
2025-09-16,08:30
2025-09-17,14:00
2025-09-18,08:30
2025-09-25,08:30
2025-09-26,08:30
2025-10-08,14:00
2025-10-24,08:30
2025-10-29,14:00
2025-11-19,14:00
2025-11-20,08:30
2025-11-25,08:30
2025-11-26,08:30
2025-12-04,08:30
2025-12-10,08:30
2025-12-10,14:00
2025-12-11,08:30
2025-12-16,08:30
2025-12-18,08:30
2025-12-23,08:30
2025-12-24,08:30
2025-12-30,14:00
2025-12-31,08:30
2026-01-08,08:30
2026-01-09,08:30
2026-01-13,08:30
2026-01-14,08:30
2026-01-15,08:30
2026-01-21,08:30
2026-01-22,08:30
2026-01-28,14:00
2026-01-29,08:30
2026-01-30,08:30
2026-02-05,08:30
2026-02-10,08:30
2026-02-11,08:30
2026-02-12,08:30
2026-02-13,08:30
2026-02-18,14:00
2026-02-19,08:30
2026-02-20,08:30
2026-02-26,08:30
2026-02-27,08:30
2026-03-05,08:30
2026-03-06,08:30
2026-03-11,08:30
2026-03-12,08:30
2026-03-13,08:30
2026-03-16,08:30
2026-03-18,08:30
2026-03-18,14:00
2026-03-19,08:30
2026-03-26,08:30
2026-04-02,08:30
2026-04-03,08:30
2026-04-08,14:00
2026-04-09,08:30
2026-04-10,08:30
2026-04-14,08:30
2026-04-16,08:30
2026-04-23,08:30
2026-04-29,14:00
2026-04-30,08:30
2026-05-07,08:30
2026-05-08,08:30
2026-05-12,08:30
2026-05-13,08:30
2026-05-14,08:30
2026-05-20,14:00
2026-05-21,08:30
2026-05-28,08:30
2026-06-04,08:30
2026-06-05,08:30
2026-06-10,08:30
2026-06-11,08:30
2026-06-17,08:30
2026-06-17,14:00
2026-06-18,08:30
2026-06-25,08:30
2026-07-02,08:30
2026-07-08,14:00
2026-07-09,08:30
2026-07-14,08:30
2026-07-15,08:30
2026-07-16,08:30
2026-07-23,08:30
2026-07-29,14:00
2026-07-30,08:30
2026-07-31,08:30
2026-08-06,08:30
2026-08-07,08:30
2026-08-12,08:30
2026-08-13,08:30
2026-08-14,08:30
2026-08-19,14:00
2026-08-20,08:30
2026-08-26,08:30
2026-08-27,08:30
2026-09-03,08:30
2026-09-04,08:30
2026-09-10,08:30
2026-09-11,08:30
2026-09-16,08:30
2026-09-16,14:00
2026-09-17,08:30
2026-09-24,08:30
2026-09-30,08:30
2026-10-01,08:30
2026-10-02,08:30
2026-10-07,14:00
2026-10-08,08:30
2026-10-14,08:30
2026-10-15,08:30
2026-10-22,08:30
2026-10-28,14:00
2026-10-29,08:30
2026-10-30,08:30
2026-11-05,08:30
2026-11-06,08:30
2026-11-10,08:30
2026-11-12,08:30
2026-11-13,08:30
2026-11-17,08:30
2026-11-18,14:00
2026-11-19,08:30
2026-11-25,08:30
2026-12-03,08:30
2026-12-04,08:30
2026-12-09,14:00
2026-12-10,08:30
2026-12-15,08:30
2026-12-16,08:30
2026-12-17,08:30
2026-12-23,08:30
2026-12-24,08:30
2026-12-30,14:00
2026-12-31,08:30";

        private static readonly List<DateTime> NewsDates = new List<DateTime>();
        private static bool newsDatesInitialized;
        private Border infoBoxContainer;
        private StackPanel infoBoxRowsPanel;
        private bool legacyInfoDrawingsCleared;
        private bool strategyAccountDesyncDetected;
        private static readonly Brush InfoHeaderFooterGradientBrush = CreateFrozenVerticalGradientBrush(
            Color.FromArgb(240, 0x2A, 0x2F, 0x45),
            Color.FromArgb(240, 0x1E, 0x23, 0x36),
            Color.FromArgb(240, 0x14, 0x18, 0x28));
        private static readonly Brush InfoBodyOddBrush = CreateFrozenBrush(240, 0x0F, 0x0F, 0x17);
        private static readonly Brush InfoBodyEvenBrush = CreateFrozenBrush(240, 0x11, 0x11, 0x18);
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(255, 0x00, 0xFF, 0x00);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DUO2";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                IsInstantiatedOnEachOptimizationIteration = false;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;

                UseAsiaSession = true;
                AsiaSessionStart = new TimeSpan(18, 30, 0);
                AsiaSessionEnd = new TimeSpan(20, 00, 0);
                AsiaBlockSundayTrades = false;
                AsiaEmaPeriod = 21;
                AsiaContracts = 1;
                AsiaTradeDirection = SessionTradeDirection.Both;
                AsiaFlipAdxThreshold = 29.1;
                AsiaEmaMinSlopePointsPerBar = 0.6;
                AsiaMaxEntryDistanceFromEmaPoints = 9.0;
                AsiaAdxPeriod = 14;
                AsiaAdxThreshold = 18.73;
                AsiaAdxMaxThreshold = 46.8;
                AsiaAdxMinSlopePoints = 1.14;
                AsiaAdxPeakDrawdownExitUnits = 13.6;
                AsiaAdxAbsoluteExitLevel = 58.8;
                AsiaStopPaddingPoints = 22.0;
                AsiaExitCrossPoints = 3.0;
                AsiaFlipEmaCrossPoints = 0.0;
                AsiaMaxStopLossPoints = 212.0;
                AsiaTakeProfitPoints = 94.0;
                AsiaAtrMinimum = 7.7;
                AsiaEntryOffsetPoints = 0.0;
                AsiaEnableFlipBreakEven = true;
                AsiaFlipBreakEvenTriggerPoints = 18.0;
                AsiaFlipTakeProfitPoints = 98.5;
                AsiaTakeProfitPercentTriggerPercent = 76.5;
                AsiaTakeProfitStopMode = TakeProfitStopMode.PercentMove;
                AsiaTakeProfitAtrTrailMultiplier = 1.0;
                AsiaTakeProfitPercentStopMovePercent = 32.25;
                AsiaRequireMinAdxForFlips = true;
                AsiaEnableAdxDdRiskMode = true;
                AsiaAdxDdRiskModeStopLossPoints = 15.25;
                AsiaAdxDdRiskModeTakeProfitPoints = 80.75;
                AsiaHorizontalExitBars = 70;

                UseAsia2Session = true;
                Asia2SessionStart = new TimeSpan(20, 00, 0);
                Asia2SessionEnd = new TimeSpan(23, 59, 0);
                Asia2BlockSundayTrades = false;
                Asia2EmaPeriod = 21;
                Asia2Contracts = 1;
                Asia2TradeDirection = SessionTradeDirection.Both;
                Asia2FlipAdxThreshold = 23.2;
                Asia2EmaMinSlopePointsPerBar = 0.55;
                Asia2MaxEntryDistanceFromEmaPoints = 0.0;
                Asia2AdxPeriod = 14;
                Asia2AdxThreshold = 21.3;
                Asia2AdxMaxThreshold = 53.0;
                Asia2AdxMinSlopePoints = 1.15;
                Asia2AdxPeakDrawdownExitUnits = 15.2;
                Asia2AdxAbsoluteExitLevel = 58.9;
                Asia2StopPaddingPoints = 29.1;
                Asia2ExitCrossPoints = 3.5;
                Asia2FlipEmaCrossPoints = 0.0;
                Asia2MaxStopLossPoints = 235.0;
                Asia2TakeProfitPoints = 83.5;
                Asia2AtrMinimum = 5.1;
                Asia2EntryOffsetPoints = 1.25;
                Asia2EnableFlipBreakEven = true;
                Asia2FlipBreakEvenTriggerPoints = 38.25;
                Asia2FlipTakeProfitPoints = 80.75;
                Asia2TakeProfitPercentTriggerPercent = 73.75;
                Asia2TakeProfitStopMode = TakeProfitStopMode.PercentMove;
                Asia2TakeProfitAtrTrailMultiplier = 1.0;
                Asia2TakeProfitPercentStopMovePercent = 51.25;
                Asia2RequireMinAdxForFlips = true;
                Asia2EnableAdxDdRiskMode = true;
                Asia2AdxDdRiskModeStopLossPoints = 6.25;
                Asia2AdxDdRiskModeTakeProfitPoints = 44.0;
                Asia2HorizontalExitBars = 29;

                UseAsia3Session = true;
                Asia3SessionStart = TimeSpan.Zero;
                Asia3SessionEnd = new TimeSpan(2, 00, 0);
                Asia3BlockSundayTrades = false;
                Asia3EmaPeriod = 21;
                Asia3Contracts = 1;
                Asia3TradeDirection = SessionTradeDirection.Both;
                Asia3FlipAdxThreshold = 29.2;
                Asia3EmaMinSlopePointsPerBar = 0.66;
                Asia3MaxEntryDistanceFromEmaPoints = 18.0;
                Asia3AdxPeriod = 14;
                Asia3AdxThreshold = 19.4;
                Asia3AdxMaxThreshold = 64.4;
                Asia3AdxMinSlopePoints = 1.3;
                Asia3AdxPeakDrawdownExitUnits = 13.9;
                Asia3AdxAbsoluteExitLevel = 63.2;
                Asia3StopPaddingPoints = 22.25;
                Asia3ExitCrossPoints = 3.75;
                Asia3FlipEmaCrossPoints = 0.0;
                Asia3MaxStopLossPoints = 143.0;
                Asia3TakeProfitPoints = 108.75;
                Asia3AtrMinimum = 4.0;
                Asia3EntryOffsetPoints = 0.0;
                Asia3EnableFlipBreakEven = true;
                Asia3FlipBreakEvenTriggerPoints = 0.0;
                Asia3FlipTakeProfitPoints = 130.75;
                Asia3TakeProfitPercentTriggerPercent = 81.0;
                Asia3TakeProfitStopMode = TakeProfitStopMode.PercentMove;
                Asia3TakeProfitAtrTrailMultiplier = 1.0;
                Asia3TakeProfitPercentStopMovePercent = 69.0;
                Asia3RequireMinAdxForFlips = true;
                Asia3EnableAdxDdRiskMode = true;
                Asia3AdxDdRiskModeStopLossPoints = 4.25;
                Asia3AdxDdRiskModeTakeProfitPoints = 31.0;
                Asia3HorizontalExitBars = 63;

                UseLondonSession = true;
                LondonSessionStart = new TimeSpan(1, 45, 0);
                LondonSessionEnd = new TimeSpan(3, 00, 0);
                AutoShiftLondon = true;
                LondonEmaPeriod = 21;
                LondonContracts = 1;
                LondonTradeDirection = SessionTradeDirection.Both;
                LondonFlipAdxThreshold = 25.4;
                LondonEmaMinSlopePointsPerBar = 0.82;
                LondonMaxEntryDistanceFromEmaPoints = 0.0;
                LondonAdxPeriod = 14;
                LondonAdxThreshold = 17.9;
                LondonAdxMaxThreshold = 38.25;
                LondonAdxMinSlopePoints = 0.99;
                LondonAdxPeakDrawdownExitUnits = 11.3;
                LondonAdxAbsoluteExitLevel = 50.9;
                LondonStopPaddingPoints = 16.5;
                LondonExitCrossPoints = 17.75;
                LondonFlipEmaCrossPoints = 0.0;
                LondonMaxStopLossPoints = 117.0;
                LondonTakeProfitPoints = 119.0;
                LondonAtrMinimum = 7.6;
                LondonEntryOffsetPoints = 0.0;
                LondonEnableFlipBreakEven = true;
                LondonFlipBreakEvenTriggerPoints = 0.0;
                LondonFlipTakeProfitPoints = 155.0;
                LondonTakeProfitPercentTriggerPercent = 46.25;
                LondonTakeProfitStopMode = TakeProfitStopMode.PercentMove;
                LondonTakeProfitAtrTrailMultiplier = 0.0;
                LondonTakeProfitPercentStopMovePercent = 29.5;
                LondonRequireMinAdxForFlips = false;
                LondonEnableAdxDdRiskMode = true;
                LondonAdxDdRiskModeStopLossPoints = 0.75;
                LondonAdxDdRiskModeTakeProfitPoints = 10.0;
                LondonHorizontalExitBars = 54;

                UseLondon2Session = true;
                London2SessionStart = new TimeSpan(3, 00, 0);
                London2SessionEnd = new TimeSpan(5, 00, 0);
                AutoShiftLondon2 = true;
                London2EmaPeriod = 21;
                London2Contracts = 1;
                London2TradeDirection = SessionTradeDirection.Both;
                London2FlipAdxThreshold = 25.0;
                London2EmaMinSlopePointsPerBar = 0.0;
                London2MaxEntryDistanceFromEmaPoints = 9.0;
                London2AdxPeriod = 14;
                London2AdxThreshold = 24.5;
                London2AdxMaxThreshold = 33.8;
                London2AdxMinSlopePoints = 0.94;
                London2AdxPeakDrawdownExitUnits = 11.22;
                London2AdxAbsoluteExitLevel = 51.2;
                London2StopPaddingPoints = 24.25;
                London2ExitCrossPoints = 3.0;
                London2FlipEmaCrossPoints = 6.25;
                London2MaxStopLossPoints = 163.0;
                London2TakeProfitPoints = 116.5;
                London2AtrMinimum = 7.8;
                London2EntryOffsetPoints = 2.75;
                London2EnableFlipBreakEven = true;
                London2FlipBreakEvenTriggerPoints = 19.0;
                London2FlipTakeProfitPoints = 122.0;
                London2TakeProfitPercentTriggerPercent = 55.0;
                London2TakeProfitStopMode = TakeProfitStopMode.PercentMove;
                London2TakeProfitAtrTrailMultiplier = 0.0;
                London2TakeProfitPercentStopMovePercent = 35.5;
                London2RequireMinAdxForFlips = true;
                London2EnableAdxDdRiskMode = true;
                London2AdxDdRiskModeStopLossPoints = 0.25;
                London2AdxDdRiskModeTakeProfitPoints = 87.5;
                London2HorizontalExitBars = 58;

                UseLondon3Session = true;
                London3SessionStart = new TimeSpan(5, 00, 0);
                London3SessionEnd = new TimeSpan(8, 55, 0);
                London3FlatByTime = "09:00:00";
                AutoShiftLondon3 = true;
                London3EmaPeriod = 21;
                London3Contracts = 1;
                London3TradeDirection = SessionTradeDirection.Both;
                London3FlipAdxThreshold = 0.0;
                London3EmaMinSlopePointsPerBar = 0.0;
                London3MaxEntryDistanceFromEmaPoints = 9.5;
                London3AdxPeriod = 14;
                London3AdxThreshold = 38.7;
                London3AdxMaxThreshold = 42.9;
                London3AdxMinSlopePoints = 1.0;
                London3AdxPeakDrawdownExitUnits = 6.8;
                London3AdxAbsoluteExitLevel = 51.2;
                London3StopPaddingPoints = 28.0;
                London3ExitCrossPoints = 2.25;
                London3FlipEmaCrossPoints = 3.5;
                London3MaxStopLossPoints = 140.0;
                London3TakeProfitPoints = 113.0;
                London3AtrMinimum = 12.0;
                London3EntryOffsetPoints = 10.75;
                London3EnableFlipBreakEven = true;
                London3FlipBreakEvenTriggerPoints = 26.5;
                London3FlipTakeProfitPoints = 151.0;
                London3TakeProfitPercentTriggerPercent = 54.5;
                London3TakeProfitStopMode = TakeProfitStopMode.PercentMove;
                London3TakeProfitAtrTrailMultiplier = 0.0;
                London3TakeProfitPercentStopMovePercent = 20.25;
                London3RequireMinAdxForFlips = true;
                London3EnableAdxDdRiskMode = true;
                London3AdxDdRiskModeStopLossPoints = 0.0;
                London3AdxDdRiskModeTakeProfitPoints = 56.75;
                London3HorizontalExitBars = 43;

                UseNewYorkSession = true;
                NewYorkSessionStart = new TimeSpan(9, 35, 0);
                NewYorkSessionEnd = new TimeSpan(11, 30, 0);
                NewYorkSkipStart = TimeSpan.Zero;
                NewYorkSkipEnd = TimeSpan.Zero;
                NewYorkEmaPeriod = 16;
                NewYorkContracts = 1;
                NewYorkTradeDirection = SessionTradeDirection.Both;
                NewYorkFlipAdxThreshold = 17.9;
                NewYorkEmaMinSlopePointsPerBar = 0.8;
                NewYorkMaxEntryDistanceFromEmaPoints = 39.0;
                NewYorkAdxPeriod = 14;
                NewYorkAdxThreshold = 16.1;
                NewYorkAdxMaxThreshold = 65.5;
                NewYorkAdxMinSlopePoints = 1.63;
                NewYorkAdxPeakDrawdownExitUnits = 15.25;
                NewYorkAdxAbsoluteExitLevel = 69.5;
                NewYorkStopPaddingPoints = 44.25;
                NewYorkExitCrossPoints = 2.75;
                NewYorkFlipEmaCrossPoints = 6.25;
                NewYorkMaxStopLossPoints = 287.5;
                NewYorkTakeProfitPoints = 127.25;
                NewYorkAtrMinimum = 9.8;
                NewYorkHvSlPaddingPoints = 34.5;
                NewYorkHvSlStartTime = new TimeSpan(9, 35, 0);
                NewYorkHvSlEndTime = new TimeSpan(10, 00, 0);
                NewYorkEntryOffsetPoints = 0.0;
                NewYorkEnableFlipBreakEven = true;
                NewYorkFlipBreakEvenTriggerPoints = 70.0;
                NewYorkFlipTakeProfitPoints = 146.5;
                NewYorkTakeProfitPercentTriggerPercent = 86.0;
                NewYorkTakeProfitStopMode = TakeProfitStopMode.PercentMove;
                NewYorkTakeProfitAtrTrailMultiplier = 1.0;
                NewYorkTakeProfitPercentStopMovePercent = 49.0;
                NewYorkRequireMinAdxForFlips = true;
                NewYorkEnableAdxDdRiskMode = true;
                NewYorkAdxDdRiskModeStopLossPoints = 35.0;
                NewYorkAdxDdRiskModeTakeProfitPoints = 72.5;
                NewYorkHorizontalExitBars = 36;

                UseNewYork2Session = true;
                NewYork2SessionStart = new TimeSpan(11, 30, 0);
                NewYork2SessionEnd = new TimeSpan(14, 00, 0);
                NewYork2SkipStart = new TimeSpan(12, 00, 0);
                NewYork2SkipEnd = new TimeSpan(12, 20, 0);
                NewYork2EmaPeriod = 16;
                NewYork2Contracts = 1;
                NewYork2TradeDirection = SessionTradeDirection.Both;
                NewYork2FlipAdxThreshold = 10.0;
                NewYork2EmaMinSlopePointsPerBar = 1.4;
                NewYork2MaxEntryDistanceFromEmaPoints = 40.5;
                NewYork2AdxPeriod = 14;
                NewYork2AdxThreshold = 22.9;
                NewYork2AdxMaxThreshold = 47.0;
                NewYork2AdxMinSlopePoints = 1.56;
                NewYork2AdxPeakDrawdownExitUnits = 8.8;
                NewYork2AdxAbsoluteExitLevel = 54.0;
                NewYork2StopPaddingPoints = 32.75;
                NewYork2ExitCrossPoints = 11.25;
                NewYork2FlipEmaCrossPoints = 0.0;
                NewYork2MaxStopLossPoints = 227.75;
                NewYork2TakeProfitPoints = 127.25;
                NewYork2AtrMinimum = 17.0;
                NewYork2HvSlPaddingPoints = 0.0;
                NewYork2HvSlStartTime = TimeSpan.Zero;
                NewYork2HvSlEndTime = TimeSpan.Zero;
                NewYork2EntryOffsetPoints = 0.0;
                NewYork2EnableFlipBreakEven = true;
                NewYork2FlipBreakEvenTriggerPoints = 42.0;
                NewYork2FlipTakeProfitPoints = 69.25;
                NewYork2TakeProfitPercentTriggerPercent = 78.0;
                NewYork2TakeProfitStopMode = TakeProfitStopMode.PercentMove;
                NewYork2TakeProfitAtrTrailMultiplier = 1.0;
                NewYork2TakeProfitPercentStopMovePercent = 28.0;
                NewYork2RequireMinAdxForFlips = false;
                NewYork2EnableAdxDdRiskMode = true;
                NewYork2AdxDdRiskModeStopLossPoints = 43.0;
                NewYork2AdxDdRiskModeTakeProfitPoints = 53.5;
                NewYork2HorizontalExitBars = 35;

                UseNewYork3Session = true;
                NewYork3SessionStart = new TimeSpan(14, 00, 0);
                NewYork3SessionEnd = new TimeSpan(17, 00, 0);
                NewYork3SkipStart = TimeSpan.Zero;
                NewYork3SkipEnd = TimeSpan.Zero;
                NewYork3EmaPeriod = 21;
                NewYork3Contracts = 1;
                NewYork3TradeDirection = SessionTradeDirection.Both;
                NewYork3FlipAdxThreshold = 0.0;
                NewYork3EmaMinSlopePointsPerBar = 1.2;
                NewYork3MaxEntryDistanceFromEmaPoints = 0.0;
                NewYork3AdxPeriod = 14;
                NewYork3AdxThreshold = 18.5;
                NewYork3AdxMaxThreshold = 42.0;
                NewYork3AdxMinSlopePoints = 1.48;
                NewYork3AdxPeakDrawdownExitUnits = 7.3;
                NewYork3AdxAbsoluteExitLevel = 55.0;
                NewYork3StopPaddingPoints = 42.5;
                NewYork3ExitCrossPoints = 13.25;
                NewYork3FlipEmaCrossPoints = 0.0;
                NewYork3MaxStopLossPoints = 264.75;
                NewYork3TakeProfitPoints = 153.0;
                NewYork3AtrMinimum = 0.0;
                NewYork3HvSlPaddingPoints = 0.0;
                NewYork3HvSlStartTime = TimeSpan.Zero;
                NewYork3HvSlEndTime = TimeSpan.Zero;
                NewYork3EntryOffsetPoints = 17.75;
                NewYork3EnableFlipBreakEven = false;
                NewYork3FlipBreakEvenTriggerPoints = 0.0;
                NewYork3FlipTakeProfitPoints = 158.0;
                NewYork3TakeProfitPercentTriggerPercent = 46.0;
                NewYork3TakeProfitStopMode = TakeProfitStopMode.PercentMove;
                NewYork3TakeProfitAtrTrailMultiplier = 1.0;
                NewYork3TakeProfitPercentStopMovePercent = 40.0;
                NewYork3RequireMinAdxForFlips = false;
                NewYork3EnableAdxDdRiskMode = true;
                NewYork3AdxDdRiskModeStopLossPoints = 11.0;
                NewYork3AdxDdRiskModeTakeProfitPoints = 30.0;
                NewYork3HorizontalExitBars = 27;

                CloseAtSessionEnd = false;
                ForceCloseTime = "16:55:00";
                AsiaSessionBrush = Brushes.DarkCyan;
                LondonSessionBrush = Brushes.MediumSeaGreen;
                NewYorkSessionBrush = Brushes.Gold;
                ShowEmaOnChart = false;
                ShowAdxOnChart = false;
                ShowAdxThresholdLines = false;
                ShowAtrOnChart = false;
                ShowAtrThresholdLines = false;

                UseNewsSkip = true;
                NewsBlockMinutes = 1;

                WebhookUrl = string.Empty;
                WebhookTickerOverride = string.Empty;
                WebhookProviderType = WebhookProvider.TradersPost;
                ProjectXApiBaseUrl = "https://api.topstepx.com";
                ProjectXTradeAllAccounts = false;
                ProjectXUsername = string.Empty;
                ProjectXApiKey = string.Empty;
                ProjectXAccountId = string.Empty;
                ProjectXContractId = string.Empty;
                MaxAccountBalance = 0.0;
                RequireEntryConfirmation = false;

                DebugLogging = false;
            }
            else if (State == State.DataLoaded)
            {
                ValidateRequiredPrimaryTimeframe(5);
                ValidateRequiredPrimaryInstrument();

                emaAsia = EMA(AsiaEmaPeriod);
                emaAsia2 = EMA(Asia2EmaPeriod);
                emaAsia3 = EMA(Asia3EmaPeriod);
                emaLondon = EMA(LondonEmaPeriod);
                emaLondon2 = EMA(London2EmaPeriod);
                emaLondon3 = EMA(London3EmaPeriod);
                emaNewYork = EMA(NewYorkEmaPeriod);
                emaNewYork2 = EMA(NewYork2EmaPeriod);
                emaNewYork3 = EMA(NewYork3EmaPeriod);
                takeProfitAtr = ATR(TakeProfitAtrPeriod);
                atrVisual = DUOAtrVisual(TakeProfitAtrPeriod);
                adxAsia = DM(AsiaAdxPeriod);
                adxAsia2 = DM(Asia2AdxPeriod);
                adxAsia3 = DM(Asia3AdxPeriod);
                adxLondon = DM(LondonAdxPeriod);
                adxLondon2 = DM(London2AdxPeriod);
                adxLondon3 = DM(London3AdxPeriod);
                adxNewYork = DM(NewYorkAdxPeriod);
                adxNewYork2 = DM(NewYork2AdxPeriod);
                adxNewYork3 = DM(NewYork3AdxPeriod);
                UpdateAdxReferenceLines(adxAsia, AsiaAdxThreshold, AsiaAdxMaxThreshold);
                UpdateAdxReferenceLines(adxAsia2, Asia2AdxThreshold, Asia2AdxMaxThreshold);
                UpdateAdxReferenceLines(adxAsia3, Asia3AdxThreshold, Asia3AdxMaxThreshold);
                UpdateAdxReferenceLines(adxLondon, LondonAdxThreshold, LondonAdxMaxThreshold);
                UpdateAdxReferenceLines(adxLondon2, London2AdxThreshold, London2AdxMaxThreshold);
                UpdateAdxReferenceLines(adxLondon3, London3AdxThreshold, London3AdxMaxThreshold);
                UpdateAdxReferenceLines(adxNewYork, NewYorkAdxThreshold, NewYorkAdxMaxThreshold);
                UpdateAdxReferenceLines(adxNewYork2, NewYork2AdxThreshold, NewYork2AdxMaxThreshold);
                UpdateAdxReferenceLines(adxNewYork3, NewYork3AdxThreshold, NewYork3AdxMaxThreshold);

                if (ShowEmaOnChart)
                {
                    AddChartIndicator(emaAsia);
                    AddChartIndicator(emaAsia2);
                    AddChartIndicator(emaAsia3);
                    AddChartIndicator(emaLondon);
                    AddChartIndicator(emaLondon2);
                    AddChartIndicator(emaLondon3);
                    AddChartIndicator(emaNewYork);
                    AddChartIndicator(emaNewYork2);
                    AddChartIndicator(emaNewYork3);
                }

                if (ShowAdxOnChart)
                {
                    AddChartIndicator(adxAsia);
                    AddChartIndicator(adxAsia2);
                    AddChartIndicator(adxAsia3);
                    AddChartIndicator(adxLondon);
                    AddChartIndicator(adxLondon2);
                    AddChartIndicator(adxLondon3);
                    AddChartIndicator(adxNewYork);
                    AddChartIndicator(adxNewYork2);
                    AddChartIndicator(adxNewYork3);
                }

                if (ShowAtrOnChart || ShowAtrThresholdLines)
                    AddChartIndicator(atrVisual);

                sessionInitialized = false;
                activeSession = GetFirstConfiguredSession();
                lockedTradeSession = SessionSlot.None;
                lastDeferredSessionLogBar = -1;
                tradeAttemptOpen = false;
                tradeAttemptId = 0;
                tradeAttemptSide = string.Empty;
                tradeLineTagCounter = 0;
                tradeLineTagPrefix = string.Empty;
                tradeLinesActive = false;
                tradeLineHasTp = false;
                tradeLineHasTpTrigger = false;
                tradeLineSignalBar = -1;
                tradeLineExitBar = -1;
                tradeLineEntryPrice = 0.0;
                tradeLineTpPrice = 0.0;
                tradeLineTpTriggerPrice = 0.0;
                tradeLineSlPrice = 0.0;
                historicalTradeLines.Clear();
                ApplyInputsForSession(activeSession);
                UpdateEmaPlotVisibility();
                UpdateAdxPlotVisibility();
                UpdateAtrPlotVisibility();
                targetTimeZone = null;
                londonTimeZone = null;
                pendingLongStopForWebhook = 0.0;
                pendingShortStopForWebhook = 0.0;
                currentTradePeakAdx = 0.0;
                trackedAdxPeakPosition = MarketPosition.Flat;
                flipBreakEvenActivated = false;
                takeProfitStopTriggered = false;
                initialStopPrice = 0.0;
                currentStopPrice = 0.0;
                adxDdRiskModeApplied = false;
                currentPositionEntryBar = -1;
                asiaSessionClosed = false;
                asia2SessionClosed = false;
                asia3SessionClosed = false;
                londonSessionClosed = false;
                london2SessionClosed = false;
                london3SessionClosed = false;
                newYorkSessionClosed = false;
                newYork2SessionClosed = false;
                newYork3SessionClosed = false;
                projectXSessionToken = null;
                projectXTokenAcquiredUtc = Core.Globals.MinDate;
                projectXAccounts = null;
                projectXLastOrderIds.Clear();
                projectXResolvedContractId = null;
                projectXResolvedInstrumentKey = string.Empty;
                accountBalanceLimitReached = false;
                accountBalanceLimitReachedBar = -1;
                asiaTradesThisSession = 0;
                londonTradesThisSession = 0;
                newYorkTradesThisSession = 0;
                strategyAccountDesyncDetected = false;

                EnsureNewsDatesInitialized();
                heartbeatReporter = new StrategyHeartbeatReporter(
                    HeartbeatStrategyName,
                    System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv"));

                LogDebug(
                    string.Format(
                        "DataLoaded | ActiveSession={0} EMA={1} ADX={2}/{3:0.##} Contracts={4} ExitCross={5:0.##} FlipCross={6:0.##} EntryStop={7}",
                        FormatSessionLabel(activeSession),
                        activeEmaPeriod,
                        activeAdxPeriod,
                        activeAdxThreshold,
                        activeContracts,
                        activeExitCrossPoints,
                        GetEffectiveFlipEmaCrossPoints(),
                        activeEntryStopMode));
            }
            else if (State == State.Realtime)
            {
                if (heartbeatReporter != null)
                    heartbeatReporter.Start();

                RunProjectXStartupPreflight();
                CheckAndHandleStrategyAccountDesync("realtime-start");
            }
            else if (State == State.Terminated)
            {
                if (heartbeatReporter != null)
                {
                    heartbeatReporter.Dispose();
                    heartbeatReporter = null;
                }

                DisposeInfoBoxOverlay();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CheckAndHandleStrategyAccountDesync("bar"))
            {
                UpdateInfo();
                return;
            }

            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
            {
                CancelWorkingEntryOrders();
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(BuildExitSignalName("InvalidConfiguration"), GetOpenLongEntrySignal());
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(BuildExitSignalName("InvalidConfiguration"), GetOpenShortEntrySignal());
                return;
            }

            if (CurrentBar < Math.Max(1, Math.Max(GetMaxConfiguredEmaPeriod(), GetMaxConfiguredAdxPeriod())))
                return;

            DrawSessionBackgrounds();
            DrawNewsWindows(Time[0]);
            UpdateTradeLines();
            UpdateInfo();

            foreach (SessionSlot slot in ConfigurableSessionSlots)
                ProcessSessionTransitions(slot);
            ReconcileTrackedEntryOrders();
            ReconcileTrackedProtectiveOrders();
            SyncTradeLinesToLivePositionAndOrders();

            UpdateActiveSession(Time[0]);
            UpdateEmaPlotVisibility();
            UpdateAdxPlotVisibility();
            UpdateAtrPlotVisibility();

            if (AuditPositionProtection("bar-watchdog"))
                return;

            if (IsForceCloseTimeReached(Time[0]))
            {
                CancelWorkingEntryOrders();
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(BuildExitSignalName("ForceClose"), GetOpenLongEntrySignal());
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(BuildExitSignalName("ForceClose"), GetOpenShortEntrySignal());
                return;
            }

            bool inNewsSkipNow = TimeInNewsSkip(Time[0]);
            bool inNewsSkipPrev = CurrentBar > 0 && TimeInNewsSkip(Time[1]);
            if (!inNewsSkipPrev && inNewsSkipNow)
            {
                CancelWorkingEntryOrders();
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(BuildExitSignalName("NewsSkip"), GetOpenLongEntrySignal());
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(BuildExitSignalName("NewsSkip"), GetOpenShortEntrySignal());
                LogDebug("Entered news block: canceling working entries and flattening open position.");
            }

            bool inActiveSessionNow = activeSession != SessionSlot.None && TimeInSession(activeSession, Time[0]);
            bool inNySkipNow = IsNewYorkFamily(activeSession) && IsNewYorkSkipTime(activeSession, Time[0]);
            bool isAsiaSundayBlockedNow = IsAsiaSundayBlocked(activeSession, Time[0]);
            bool accountBlocked = IsAccountBalanceBlocked();
            double adxValue = activeAdx != null ? activeAdx[0] : 0.0;
            double atrValue = GetCurrentAtrValue();
            UpdateAdxPeakTracker(adxValue);
            double adxSlope = GetAdxSlopePoints();
            bool adxMinPass = !inActiveSessionNow || activeAdxThreshold <= 0.0 || adxValue >= activeAdxThreshold;
            bool adxMaxPass = !inActiveSessionNow || activeAdxMaxThreshold <= 0.0 || adxValue <= activeAdxMaxThreshold;
            bool adxThresholdPass = adxMinPass && adxMaxPass;
            bool adxSlopePass = !inActiveSessionNow || activeAdxMinSlopePoints <= 0.0 || adxSlope >= activeAdxMinSlopePoints;
            bool atrMinPass = !inActiveSessionNow || activeMinimumAtrForEntry <= 0.0 || atrValue >= activeMinimumAtrForEntry;
            bool adxPass = adxThresholdPass && adxSlopePass;
            bool canTradeNow = inActiveSessionNow && !inNewsSkipNow && !inNySkipNow && !isAsiaSundayBlockedNow && !accountBlocked && adxPass && atrMinPass;
            bool flipAdxMinPass = !activeRequireMinAdxForFlips || !inActiveSessionNow || activeFlipAdxThreshold <= 0.0 || adxValue >= activeFlipAdxThreshold;
            bool canFlipNow = inActiveSessionNow && !inNewsSkipNow && !inNySkipNow && !isAsiaSundayBlockedNow && !accountBlocked && flipAdxMinPass && atrMinPass;
            bool allowLong = activeTradeDirection != SessionTradeDirection.ShortOnly;
            bool allowShort = activeTradeDirection != SessionTradeDirection.LongOnly;

            if (!inActiveSessionNow)
                CancelWorkingEntryOrders();

            if (inNySkipNow)
                CancelWorkingEntryOrders();

            if (isAsiaSundayBlockedNow)
                CancelWorkingEntryOrders();

            if (activeEma == null || CurrentBar < activeEmaPeriod)
                return;

            if (IsLondon3FlatByTimeReached(Time[0]))
            {
                CancelWorkingEntryOrders();
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(BuildExitSignalName("London3FlatByTime"), GetOpenLongEntrySignal());
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(BuildExitSignalName("London3FlatByTime"), GetOpenShortEntrySignal());

                LogDebug(string.Format(
                    "London3 flat-by-time reached | now={0:HH:mm} configured={1} side={2}",
                    Time[0],
                    string.IsNullOrWhiteSpace(London3FlatByTime) ? "Off" : London3FlatByTime,
                    Position.MarketPosition));
                return;
            }

            double emaValue = activeEma[0];
            bool bullish = Close[0] > Open[0];
            bool bearish = Close[0] < Open[0];
            double bodyAbovePercent = GetBodyPercentAboveEma(Open[0], Close[0], emaValue);
            double bodyBelowPercent = GetBodyPercentBelowEma(Open[0], Close[0], emaValue);
            double emaDistancePoints = Math.Abs(Close[0] - emaValue);
            bool distancePasses = activeMaxEntryDistanceFromEmaPoints <= 0.0 || emaDistancePoints <= activeMaxEntryDistanceFromEmaPoints;
            bool emaSlopeLongPass = EmaSlopePassesLong();
            bool emaSlopeShortPass = EmaSlopePassesShort();
            bool longSignalRaw = bullish && bodyAbovePercent > 0.0;
            bool shortSignalRaw = bearish && bodyBelowPercent > 0.0;
            bool longSignal = longSignalRaw && allowLong;
            bool shortSignal = shortSignalRaw && allowShort;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                TryApplyFlipBreakEvenStop();
                TryManageTakeProfitTriggeredStop();

                if (activeAdxAbsoluteExitLevel > 0.0 && adxValue >= activeAdxAbsoluteExitLevel)
                {
                    ExitLong(BuildExitSignalName("AdxLevelExit"), GetOpenLongEntrySignal());
                    LogDebug(string.Format("Exit LONG | reason=AdxLevel adx={0:0.00} threshold={1:0.00}",
                        adxValue, activeAdxAbsoluteExitLevel));
                    return;
                }

                double adxDrawdown;
                if (ShouldExitOnAdxDrawdown(adxValue, out adxDrawdown))
                {
                    if (activeEnableAdxDdRiskMode)
                    {
                        TryApplyAdxDdRiskMode(adxValue, adxDrawdown);
                    }
                    else
                    {
                        ExitLong(BuildExitSignalName("AdxDrawdownExit"), GetOpenLongEntrySignal());
                        LogDebug(string.Format("Exit LONG | reason=AdxDrawdown adx={0:0.00} peak={1:0.00} drawdown={2:0.00} threshold={3:0.00}",
                            adxValue, currentTradePeakAdx, adxDrawdown, activeAdxPeakDrawdownExitUnits));
                        return;
                    }
                }

                double activePositionTakeProfitPoints = GetActivePositionTakeProfitPoints();
                if (activePositionTakeProfitPoints > 0.0 && Close[0] >= Position.AveragePrice + activePositionTakeProfitPoints)
                {
                    ExitLong(BuildExitSignalName("TakeProfitExit"), GetOpenLongEntrySignal());
                    LogDebug(string.Format("Exit LONG | reason=TakeProfit close={0:0.00} avg={1:0.00} tpPts={2:0.00} source={3}",
                        Close[0], Position.AveragePrice, activePositionTakeProfitPoints, adxDdRiskModeApplied && activeAdxDdRiskModeTakeProfitPoints > 0.0 ? "AdxDdRiskMode" : "Session"));
                    return;
                }

                if (TryExitStaleTrade())
                    return;

                if (Close[0] <= emaValue - activeExitCrossPoints)
                {
                    double effectiveFlipEmaCrossPoints = GetEffectiveFlipEmaCrossPoints();
                    if (DebugLogging && canTradeNow && !distancePasses && emaSlopeShortPass && bodyBelowPercent >= FlipBodyThresholdPercent)
                    {
                        LogDebug(string.Format(
                            "Flip blocked | reason=EmaDistance side=Short distancePts={0:0.00} maxPts={1:0.00} session={2}",
                            emaDistancePoints,
                            activeMaxEntryDistanceFromEmaPoints,
                            FormatSessionLabel(activeSession)));
                    }

                    bool flipCanTradePass = canFlipNow;
                    bool flipAtrPass = atrMinPass;
                    bool flipDirectionPass = allowShort;
                    bool flipDistancePass = distancePasses;
                    bool flipSlopePass = emaSlopeShortPass;
                    bool flipBodyPass = bodyBelowPercent >= FlipBodyThresholdPercent;
                    double flipEntryPrice = GetEntryPriceForDirection(Close[0], false, 0.0);
                    double flipStopPrice = BuildFlipShortStopPrice(flipEntryPrice, emaValue, Time[0]);
                    double flipStopLossPoints = GetPlannedStopLossPoints(flipEntryPrice, flipStopPrice);
                    bool flipMaxStopPass = IsWithinMaxStopLossPoints(flipStopLossPoints);
                    bool flipCrossPass = Close[0] <= emaValue - effectiveFlipEmaCrossPoints;
                    bool shouldFlip = flipCanTradePass && flipDirectionPass && flipDistancePass && flipSlopePass && flipBodyPass && flipCrossPass && flipMaxStopPass;
                    if (shouldFlip)
                    {
                        CancelOrderIfActive(longEntryOrder, "FlipToShort");
                        CancelOrderIfActive(shortEntryOrder, "FlipToShort");

                        bool useMarketEntry = true;
                        double entryPrice = flipEntryPrice;
                        double stopPrice = flipStopPrice;
                        int qty = GetEntryQuantity();
                        if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", entryPrice, qty))
                        {
                            LogDebug("Entry confirmation declined | Flip LONG->SHORT.");
                            return;
                        }
                        double flipTakeProfitPoints = GetConfiguredEntryTakeProfitPoints(true);
                        double takeProfitPrice = GetWebhookTakeProfitPrice(entryPrice, flipTakeProfitPoints, false);
                        pendingShortStopForWebhook = stopPrice;
                        pendingShortEntryIsFlip = true;
                        SetStopLossByDistanceTicks(ShortFlipEntrySignal, entryPrice, stopPrice);
                        SetProfitTargetByDistanceTicks(ShortFlipEntrySignal, flipTakeProfitPoints);
                        SendFlipWebhooks("sell", entryPrice, takeProfitPrice, stopPrice, useMarketEntry, qty, MarketPosition.Long);
                        StartTradeLines(entryPrice, stopPrice, flipTakeProfitPoints > 0.0 ? entryPrice - flipTakeProfitPoints : 0.0, flipTakeProfitPoints > 0.0);
                        SubmitShortEntryOrder(qty, entryPrice, useMarketEntry, ShortFlipEntrySignal);

                        LogDebug(string.Format(
                            "Flip LONG->SHORT | close={0:0.00} entry={1:0.00} type={2} ema={3:0.00} below%={4:0.0} flipCrossPts={5:0.##} stop={6:0.00} stopTicks={7} qty={8}",
                            Close[0],
                            entryPrice,
                            useMarketEntry ? "market" : "limit",
                            emaValue,
                            bodyBelowPercent,
                            effectiveFlipEmaCrossPoints,
                            stopPrice,
                            PriceToTicks(Math.Abs(entryPrice - stopPrice)),
                            qty));
                    }
                    else
                    {
                        if (!flipMaxStopPass)
                        {
                            LogDebug(string.Format(
                                "Flip blocked | reason=MaxSL side=Short slPts={0:0.00} maxSlPts={1:0.00} session={2}",
                                flipStopLossPoints,
                                activeMaxStopLossPoints,
                                FormatSessionLabel(activeSession)));
                        }
                        if (DebugLogging)
                        {
                            LogDebug(string.Format(
                                "Flip skipped | side=Short canTrade={0} adxMinForFlipPass={1} atrMinPass={2} directionPass={3} distancePass={4} emaSlopePass={5} bodyPass={6} crossPass={7} maxStopPass={8} below%={9:0.0} minBody%={10:0.0} flipCrossPts={11:0.##} flipSlPts={12:0.00} maxSlPts={13:0.00}",
                                flipCanTradePass,
                                flipAdxMinPass,
                                flipAtrPass,
                                flipDirectionPass,
                                flipDistancePass,
                                flipSlopePass,
                                flipBodyPass,
                                flipCrossPass,
                                flipMaxStopPass,
                                bodyBelowPercent,
                                FlipBodyThresholdPercent,
                                effectiveFlipEmaCrossPoints,
                                flipStopLossPoints,
                                activeMaxStopLossPoints));
                        }
                        ExitLong(BuildExitSignalName("EmaExitLong"), GetOpenLongEntrySignal());
                        LogDebug(string.Format("Exit LONG | close={0:0.00} ema={1:0.00} below%={2:0.0}", Close[0], emaValue, bodyBelowPercent));
                    }
                }

                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                TryApplyFlipBreakEvenStop();
                TryManageTakeProfitTriggeredStop();

                if (activeAdxAbsoluteExitLevel > 0.0 && adxValue >= activeAdxAbsoluteExitLevel)
                {
                    ExitShort(BuildExitSignalName("AdxLevelExit"), GetOpenShortEntrySignal());
                    LogDebug(string.Format("Exit SHORT | reason=AdxLevel adx={0:0.00} threshold={1:0.00}",
                        adxValue, activeAdxAbsoluteExitLevel));
                    return;
                }

                double adxDrawdown;
                if (ShouldExitOnAdxDrawdown(adxValue, out adxDrawdown))
                {
                    if (activeEnableAdxDdRiskMode)
                    {
                        TryApplyAdxDdRiskMode(adxValue, adxDrawdown);
                    }
                    else
                    {
                        ExitShort(BuildExitSignalName("AdxDrawdownExit"), GetOpenShortEntrySignal());
                        LogDebug(string.Format("Exit SHORT | reason=AdxDrawdown adx={0:0.00} peak={1:0.00} drawdown={2:0.00} threshold={3:0.00}",
                            adxValue, currentTradePeakAdx, adxDrawdown, activeAdxPeakDrawdownExitUnits));
                        return;
                    }
                }

                double activePositionTakeProfitPoints = GetActivePositionTakeProfitPoints();
                if (activePositionTakeProfitPoints > 0.0 && Close[0] <= Position.AveragePrice - activePositionTakeProfitPoints)
                {
                    ExitShort(BuildExitSignalName("TakeProfitExit"), GetOpenShortEntrySignal());
                    LogDebug(string.Format("Exit SHORT | reason=TakeProfit close={0:0.00} avg={1:0.00} tpPts={2:0.00} source={3}",
                        Close[0], Position.AveragePrice, activePositionTakeProfitPoints, adxDdRiskModeApplied && activeAdxDdRiskModeTakeProfitPoints > 0.0 ? "AdxDdRiskMode" : "Session"));
                    return;
                }

                if (TryExitStaleTrade())
                    return;

                if (Close[0] >= emaValue + activeExitCrossPoints)
                {
                    double effectiveFlipEmaCrossPoints = GetEffectiveFlipEmaCrossPoints();
                    if (DebugLogging && canTradeNow && !distancePasses && emaSlopeLongPass && bodyAbovePercent >= FlipBodyThresholdPercent)
                    {
                        LogDebug(string.Format(
                            "Flip blocked | reason=EmaDistance side=Long distancePts={0:0.00} maxPts={1:0.00} session={2}",
                            emaDistancePoints,
                            activeMaxEntryDistanceFromEmaPoints,
                            FormatSessionLabel(activeSession)));
                    }

                    bool flipCanTradePass = canFlipNow;
                    bool flipAtrPass = atrMinPass;
                    bool flipDirectionPass = allowLong;
                    bool flipDistancePass = distancePasses;
                    bool flipSlopePass = emaSlopeLongPass;
                    bool flipBodyPass = bodyAbovePercent >= FlipBodyThresholdPercent;
                    double flipEntryPrice = GetEntryPriceForDirection(Close[0], true, 0.0);
                    double flipStopPrice = BuildFlipLongStopPrice(flipEntryPrice, emaValue, Time[0]);
                    double flipStopLossPoints = GetPlannedStopLossPoints(flipEntryPrice, flipStopPrice);
                    bool flipMaxStopPass = IsWithinMaxStopLossPoints(flipStopLossPoints);
                    bool flipCrossPass = Close[0] >= emaValue + effectiveFlipEmaCrossPoints;
                    bool shouldFlip = flipCanTradePass && flipDirectionPass && flipDistancePass && flipSlopePass && flipBodyPass && flipCrossPass && flipMaxStopPass;
                    if (shouldFlip)
                    {
                        CancelOrderIfActive(longEntryOrder, "FlipToLong");
                        CancelOrderIfActive(shortEntryOrder, "FlipToLong");

                        bool useMarketEntry = true;
                        double entryPrice = flipEntryPrice;
                        double stopPrice = flipStopPrice;
                        int qty = GetEntryQuantity();
                        if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", entryPrice, qty))
                        {
                            LogDebug("Entry confirmation declined | Flip SHORT->LONG.");
                            return;
                        }
                        double flipTakeProfitPoints = GetConfiguredEntryTakeProfitPoints(true);
                        double takeProfitPrice = GetWebhookTakeProfitPrice(entryPrice, flipTakeProfitPoints, true);
                        pendingLongStopForWebhook = stopPrice;
                        pendingLongEntryIsFlip = true;
                        SetStopLossByDistanceTicks(LongFlipEntrySignal, entryPrice, stopPrice);
                        SetProfitTargetByDistanceTicks(LongFlipEntrySignal, flipTakeProfitPoints);
                        SendFlipWebhooks("buy", entryPrice, takeProfitPrice, stopPrice, useMarketEntry, qty, MarketPosition.Short);
                        StartTradeLines(entryPrice, stopPrice, flipTakeProfitPoints > 0.0 ? entryPrice + flipTakeProfitPoints : 0.0, flipTakeProfitPoints > 0.0);
                        SubmitLongEntryOrder(qty, entryPrice, useMarketEntry, LongFlipEntrySignal);

                        LogDebug(string.Format(
                            "Flip SHORT->LONG | close={0:0.00} entry={1:0.00} type={2} ema={3:0.00} above%={4:0.0} flipCrossPts={5:0.##} stop={6:0.00} stopTicks={7} qty={8}",
                            Close[0],
                            entryPrice,
                            useMarketEntry ? "market" : "limit",
                            emaValue,
                            bodyAbovePercent,
                            effectiveFlipEmaCrossPoints,
                            stopPrice,
                            PriceToTicks(Math.Abs(entryPrice - stopPrice)),
                            qty));
                    }
                    else
                    {
                        if (!flipMaxStopPass)
                        {
                            LogDebug(string.Format(
                                "Flip blocked | reason=MaxSL side=Long slPts={0:0.00} maxSlPts={1:0.00} session={2}",
                                flipStopLossPoints,
                                activeMaxStopLossPoints,
                                FormatSessionLabel(activeSession)));
                        }
                        if (DebugLogging)
                        {
                            LogDebug(string.Format(
                                "Flip skipped | side=Long canTrade={0} adxMinForFlipPass={1} atrMinPass={2} directionPass={3} distancePass={4} emaSlopePass={5} bodyPass={6} crossPass={7} maxStopPass={8} above%={9:0.0} minBody%={10:0.0} flipCrossPts={11:0.##} flipSlPts={12:0.00} maxSlPts={13:0.00}",
                                flipCanTradePass,
                                flipAdxMinPass,
                                flipAtrPass,
                                flipDirectionPass,
                                flipDistancePass,
                                flipSlopePass,
                                flipBodyPass,
                                flipCrossPass,
                                flipMaxStopPass,
                                bodyAbovePercent,
                                FlipBodyThresholdPercent,
                                effectiveFlipEmaCrossPoints,
                                flipStopLossPoints,
                                activeMaxStopLossPoints));
                        }
                        ExitShort(BuildExitSignalName("EmaExitShort"), GetOpenShortEntrySignal());
                        LogDebug(string.Format("Exit SHORT | close={0:0.00} ema={1:0.00} above%={2:0.0}", Close[0], emaValue, bodyAbovePercent));
                    }
                }

                return;
            }

            if (DebugLogging && Position.MarketPosition == MarketPosition.Flat)
            {
                if (longSignalRaw && !allowLong)
                {
                    LogDebug(string.Format(
                        "Setup blocked | side=Long close={0:0.00} ema={1:0.00} reasons=DirectionBlocked mode={2}",
                        Close[0],
                        emaValue,
                        activeTradeDirection));
                }
                else if (shortSignalRaw && !allowShort)
                {
                    LogDebug(string.Format(
                        "Setup blocked | side=Short close={0:0.00} ema={1:0.00} reasons=DirectionBlocked mode={2}",
                        Close[0],
                        emaValue,
                        activeTradeDirection));
                }
            }

            if (!canTradeNow)
            {
                if (DebugLogging && Position.MarketPosition == MarketPosition.Flat && (longSignal || shortSignal))
                {
                    string setupSide = longSignal ? "Long" : "Short";
                    var reasons = new List<string>();

                    if (!inActiveSessionNow)
                        reasons.Add(string.Format("OutOfSession active={0}", FormatSessionLabel(activeSession)));
                    if (inNewsSkipNow)
                        reasons.Add(string.Format("NewsSkip minutes={0}", NewsBlockMinutes));
                    if (inNySkipNow)
                    {
                        TimeSpan skipStart;
                        TimeSpan skipEnd;
                        if (TryGetNewYorkSkipWindow(activeSession, out skipStart, out skipEnd))
                            reasons.Add(string.Format("NewYorkSkip {0:hh\\:mm}-{1:hh\\:mm}", skipStart, skipEnd));
                        else
                            reasons.Add("NewYorkSkip");
                    }
                    if (isAsiaSundayBlockedNow)
                        reasons.Add("AsiaSundayBlock");
                    if (accountBlocked)
                        reasons.Add(string.Format("AccountBlocked maxBalance={0:0.##}", MaxAccountBalance));
                    if (!adxMinPass)
                        reasons.Add(string.Format("AdxBelowMin adx={0:0.00} min={1:0.00}", adxValue, activeAdxThreshold));
                    if (!adxMaxPass)
                        reasons.Add(string.Format("AdxAboveMax adx={0:0.00} max={1:0.00}", adxValue, activeAdxMaxThreshold));
                    if (!adxSlopePass)
                        reasons.Add(string.Format("AdxSlopeBelowMin slope={0:0.00} min={1:0.00}", adxSlope, activeAdxMinSlopePoints));
                    if (!atrMinPass)
                        reasons.Add(string.Format("AtrBelowMin atr={0:0.00} min={1:0.00}", atrValue, activeMinimumAtrForEntry));
                    if (reasons.Count == 0)
                        reasons.Add("UnknownGate");

                    LogDebug(string.Format(
                        "Setup blocked | side={0} close={1:0.00} ema={2:0.00} reasons={3}",
                        setupSide,
                        Close[0],
                        emaValue,
                        string.Join(" | ", reasons)));
                }
                return;
            }

            if (longSignal)
            {
                BeginTradeAttempt("Long");
                LogDebug(string.Format("Setup ready | side=Long session={0} close={1:0.00} ema={2:0.00}", FormatSessionLabel(activeSession), Close[0], emaValue));

                CancelOrderIfActive(shortEntryOrder, "OppositeLongSignal");
                bool longOrderActive = IsOrderActive(longEntryOrder);

                if (!longOrderActive)
                {
                    bool useMarketEntry = activeEntryOffsetPoints <= 0.0;
                    double entryPrice = GetEntryPriceForDirection(Close[0], true, activeEntryOffsetPoints);
                    double stopPrice = BuildLongEntryStopPrice(entryPrice, emaValue, Time[0]);
                    double stopLossPoints = GetPlannedStopLossPoints(entryPrice, stopPrice);
                    if (!IsWithinMaxStopLossPoints(stopLossPoints))
                    {
                        LogDebug(string.Format(
                            "Entry blocked | reason=MaxSL side=Long session={0} entry={1:0.00} stop={2:0.00} slPts={3:0.00} maxSlPts={4:0.00}",
                            FormatSessionLabel(activeSession),
                            entryPrice,
                            stopPrice,
                            stopLossPoints,
                            activeMaxStopLossPoints));
                        EndTradeAttempt("max-stop");
                        return;
                    }
                    int qty = GetEntryQuantity();
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", entryPrice, qty))
                    {
                        LogDebug("Entry confirmation declined | LONG.");
                        EndTradeAttempt("confirmation-declined");
                        return;
                    }

                    double takeProfitPoints = GetConfiguredEntryTakeProfitPoints(false);
                    double takeProfitPrice = GetWebhookTakeProfitPrice(entryPrice, takeProfitPoints, true);
                    pendingLongStopForWebhook = stopPrice;
                    pendingLongEntryIsFlip = false;
                    SetStopLossByDistanceTicks(LongEntrySignal, entryPrice, stopPrice);
                    SetProfitTargetByDistanceTicks(LongEntrySignal, takeProfitPoints);
                    SendWebhook("buy", entryPrice, takeProfitPrice, stopPrice, useMarketEntry, qty);
                    StartTradeLines(entryPrice, stopPrice, takeProfitPoints > 0.0 ? entryPrice + takeProfitPoints : 0.0, takeProfitPoints > 0.0);
                    SubmitLongEntryOrder(qty, entryPrice, useMarketEntry, LongEntrySignal);
                    LogDebug(string.Format("Place LONG {0} | session={1} entry={2:0.00} stop={3:0.00} qty={4}", useMarketEntry ? "market" : "limit", FormatSessionLabel(activeSession), entryPrice, stopPrice, qty));
                }
                else if (DebugLogging)
                {
                    LogDebug(string.Format("LONG signal skipped | reason=longOrderActive tracked={0}", FormatOrderRef(longEntryOrder)));
                    EndTradeAttempt("entry-order-active");
                }
            }
            else if (shortSignal)
            {
                BeginTradeAttempt("Short");
                LogDebug(string.Format("Setup ready | side=Short session={0} close={1:0.00} ema={2:0.00}", FormatSessionLabel(activeSession), Close[0], emaValue));

                CancelOrderIfActive(longEntryOrder, "OppositeShortSignal");
                bool shortOrderActive = IsOrderActive(shortEntryOrder);

                if (!shortOrderActive)
                {
                    bool useMarketEntry = activeEntryOffsetPoints <= 0.0;
                    double entryPrice = GetEntryPriceForDirection(Close[0], false, activeEntryOffsetPoints);
                    double stopPrice = BuildShortEntryStopPrice(entryPrice, emaValue, Time[0]);
                    double stopLossPoints = GetPlannedStopLossPoints(entryPrice, stopPrice);
                    if (!IsWithinMaxStopLossPoints(stopLossPoints))
                    {
                        LogDebug(string.Format(
                            "Entry blocked | reason=MaxSL side=Short session={0} entry={1:0.00} stop={2:0.00} slPts={3:0.00} maxSlPts={4:0.00}",
                            FormatSessionLabel(activeSession),
                            entryPrice,
                            stopPrice,
                            stopLossPoints,
                            activeMaxStopLossPoints));
                        EndTradeAttempt("max-stop");
                        return;
                    }
                    int qty = GetEntryQuantity();
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", entryPrice, qty))
                    {
                        LogDebug("Entry confirmation declined | SHORT.");
                        EndTradeAttempt("confirmation-declined");
                        return;
                    }

                    double takeProfitPoints = GetConfiguredEntryTakeProfitPoints(false);
                    double takeProfitPrice = GetWebhookTakeProfitPrice(entryPrice, takeProfitPoints, false);
                    pendingShortStopForWebhook = stopPrice;
                    pendingShortEntryIsFlip = false;
                    SetStopLossByDistanceTicks(ShortEntrySignal, entryPrice, stopPrice);
                    SetProfitTargetByDistanceTicks(ShortEntrySignal, takeProfitPoints);
                    SendWebhook("sell", entryPrice, takeProfitPrice, stopPrice, useMarketEntry, qty);
                    StartTradeLines(entryPrice, stopPrice, takeProfitPoints > 0.0 ? entryPrice - takeProfitPoints : 0.0, takeProfitPoints > 0.0);
                    SubmitShortEntryOrder(qty, entryPrice, useMarketEntry, ShortEntrySignal);
                    LogDebug(string.Format("Place SHORT {0} | session={1} entry={2:0.00} stop={3:0.00} qty={4}", useMarketEntry ? "market" : "limit", FormatSessionLabel(activeSession), entryPrice, stopPrice, qty));
                }
                else if (DebugLogging)
                {
                    LogDebug(string.Format("SHORT signal skipped | reason=shortOrderActive tracked={0}", FormatOrderRef(shortEntryOrder)));
                    EndTradeAttempt("entry-order-active");
                }
            }
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate == null || marketDataUpdate.MarketDataType != MarketDataType.Last)
                return;

            if (State != State.Realtime || Position.MarketPosition == MarketPosition.Flat)
                return;

            if (CheckAndHandleStrategyAccountDesync("tick-watchdog"))
                return;

            AuditPositionProtection("tick-watchdog");
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            if (IsLongEntryOrderName(order.Name))
                longEntryOrder = order;
            else if (IsShortEntryOrderName(order.Name))
                shortEntryOrder = order;

            if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected || orderState == OrderState.Filled)
            {
                if (order == longEntryOrder)
                {
                    if (orderState != OrderState.Filled)
                        pendingLongEntryIsFlip = false;
                    longEntryOrder = null;
                }
                else if (order == shortEntryOrder)
                {
                    if (orderState != OrderState.Filled)
                        pendingShortEntryIsFlip = false;
                    shortEntryOrder = null;
                }
            }

            TrackProtectiveAndExitOrders(order, orderState);
            SyncTradeLinesToProtectiveOrder(order, limitPrice, stopPrice);
            SyncTradeLinesToFilledEntryOrder(order, orderState, averageFillPrice);

            if (orderState == OrderState.Rejected)
                HandleOrderRejected(order, error, comment);

            bool isImportantState = orderState == OrderState.Filled
                || orderState == OrderState.Cancelled
                || orderState == OrderState.Rejected;
            bool shouldLogOrderState = DebugLogging && isImportantState;

            if (shouldLogOrderState)
            {
                string currentOrderId = order.OrderId ?? string.Empty;
                LogDebug(
                    string.Format(
                        "OrderUpdate | name={0} id={1} state={2} qty={3} filled={4} avg={5:0.00} limit={6:0.00} stop={7:0.00} error={8} comment={9} trackedLong={10} trackedShort={11}",
                        order.Name,
                        currentOrderId,
                        orderState,
                        quantity,
                        filled,
                        averageFillPrice,
                        limitPrice,
                        stopPrice,
                        error,
                        comment,
                        FormatOrderRef(longEntryOrder),
                        FormatOrderRef(shortEntryOrder)));
            }

            bool isEntryOrder = IsEntryOrderName(order.Name);
            if (isEntryOrder && orderState != OrderState.Filled &&
                (orderState == OrderState.Cancelled || orderState == OrderState.Rejected) &&
                Position.MarketPosition == MarketPosition.Flat)
            {
                if (tradeLinesActive)
                    FinalizeTradeLines();
                EndTradeAttempt("entry-" + orderState);
            }

            if (orderState == OrderState.Cancelled && IsProtectiveOrderName(order.Name))
                AuditPositionProtection("protective-cancelled");

            if (orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                UpdateInfo();
        }

        private void HandleOrderRejected(Order order, ErrorCode error, string comment)
        {
            string name = order != null ? (order.Name ?? string.Empty) : string.Empty;
            string state = order != null ? order.OrderState.ToString() : "Unknown";
            Print(string.Format("{0} | {6} | bar={1} | Order rejected guard | name={2} state={3} error={4} comment={5}",
                Time[0], CurrentBar, name, state, error, comment ?? string.Empty, HeartbeatStrategyName));

            if (IsEntryOrderName(name))
            {
                CancelWorkingEntryOrders();
                EndTradeAttempt("entry-rejected");
                return;
            }

            if (IsProtectiveOrderName(name))
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(BuildExitSignalName("ProtectiveReject"), GetOpenLongEntrySignal());
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(BuildExitSignalName("ProtectiveReject"), GetOpenShortEntrySignal());
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution?.Order == null)
                return;

            if (execution.Order.OrderState != OrderState.Filled)
                return;

            string orderName = execution.Order.Name ?? string.Empty;
            double effectiveFillPrice = execution.Order.AverageFillPrice > 0.0
                ? execution.Order.AverageFillPrice
                : price;
            double fillPrice = Instrument.MasterInstrument.RoundToTickSize(effectiveFillPrice);
            bool terminalExitExecution = IsTerminalExitExecution(orderName);

            if (IsEntryOrderName(orderName))
            {
                currentPositionEntrySignal = orderName;
                currentPositionIsFlipEntry = IsLongEntryOrderName(orderName) ? pendingLongEntryIsFlip : pendingShortEntryIsFlip;
                pendingLongEntryIsFlip = false;
                pendingShortEntryIsFlip = false;
                activeStopLossOrder = null;
                activeProfitTargetOrder = null;
                activeExitOrder = null;
                flipBreakEvenActivated = false;
                takeProfitStopTriggered = false;
                initialStopPrice = marketPosition == MarketPosition.Long
                    ? pendingLongStopForWebhook
                    : marketPosition == MarketPosition.Short
                        ? pendingShortStopForWebhook
                        : 0.0;
                initialStopPrice = Instrument.MasterInstrument.RoundToTickSize(initialStopPrice);
                if (initialStopPrice <= 0.0 && tradeLineSlPrice > 0.0)
                    initialStopPrice = Instrument.MasterInstrument.RoundToTickSize(tradeLineSlPrice);
                initialStopPrice = BuildFilledStopPrice(marketPosition, fillPrice, initialStopPrice);
                currentStopPrice = initialStopPrice;
                ReanchorTradeLinesToEntryFill(marketPosition, fillPrice, currentPositionIsFlipEntry, initialStopPrice);
                adxDdRiskModeApplied = false;
                currentPositionEntryBar = CurrentBar;
                SessionSlot entrySession = activeSession != SessionSlot.None
                    ? activeSession
                    : DetermineSessionForTime(time);
                if (entrySession != SessionSlot.None)
                {
                    lockedTradeSession = entrySession;
                    SetTradesThisSession(entrySession, GetTradesThisSession(entrySession) + 1);
                    LogDebug(string.Format("Session lock set to {0} on {1} fill.", FormatSessionLabel(lockedTradeSession), orderName));
                    LogDebug(string.Format(
                        "Trade count | session={0} taken={1}",
                        FormatSessionLabel(entrySession),
                        GetTradesThisSession(entrySession)));
                }

                ArmProtectionAuditGracePeriod("entry-fill");
            }
            else if (terminalExitExecution)
            {
                ReleasePositionTrackingAfterTerminalExit(time, orderName);
            }
            else if (marketPosition == MarketPosition.Flat && lockedTradeSession != SessionSlot.None)
            {
                LogDebug(string.Format("Session lock released from {0} after flat execution ({1}).", FormatSessionLabel(lockedTradeSession), orderName));
                lockedTradeSession = SessionSlot.None;
                ResetPositionTrackingState();
            }
            else if (marketPosition == MarketPosition.Flat)
            {
                ResetPositionTrackingState();
            }

            bool isManagedFlipCloseExecution = string.Equals(orderName, "Close position", StringComparison.OrdinalIgnoreCase)
                && suppressProjectXNextExecutionExitWebhook;
            if (WebhookProviderType == WebhookProvider.ProjectX && isManagedFlipCloseExecution)
            {
                suppressProjectXNextExecutionExitWebhook = false;
                LogDebug(string.Format(
                    "ProjectX flip close execution consumed suppression | order={0} qty={1} posAfter={2}",
                    orderName,
                    quantity,
                    marketPosition));
            }

            bool shouldSendExitWebhook = ShouldSendExitWebhook(execution, orderName, marketPosition);
            if (shouldSendExitWebhook)
            {
                if (WebhookProviderType == WebhookProvider.ProjectX && suppressProjectXNextExecutionExitWebhook)
                {
                    suppressProjectXNextExecutionExitWebhook = false;
                    LogDebug(string.Format(
                        "ProjectX execution exit suppressed | order={0} qty={1} posAfter={2}",
                        orderName,
                        quantity,
                        marketPosition));
                }
                else
                {
                    SendWebhook("exit", 0, 0, 0, true, quantity);
                }
            }

            if (DebugLogging)
            {
                LogDebug(
                    string.Format(
                        "Execution | order={0} qty={1} price={2:0.00} posAfter={3} execId={4}",
                        orderName,
                        quantity,
                        fillPrice,
                        marketPosition,
                        executionId));
            }

            if (!IsEntryOrderName(orderName))
            {
                if (tradeLinesActive && ShouldFinalizeTradeLinesOnExecution(orderName))
                    FinalizeTradeLines();
                EndTradeAttempt("exit-" + orderName);
            }

            UpdateInfo();
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (!UseCustomTradeLineRendering() || RenderTarget == null || chartControl == null || chartScale == null || ChartBars == null)
                return;

            bool hasActiveSegment = ShouldRenderActiveTradeLinesCustom();
            bool hasHistoricalSegments = historicalTradeLines.Count > 0;
            if (!hasActiveSegment && !hasHistoricalSegments)
                return;

            var oldAntialiasMode = RenderTarget.AntialiasMode;
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;

            using (var entryBrush = Brushes.Gold.ToDxBrush(RenderTarget))
            using (var stopBrush = Brushes.Red.ToDxBrush(RenderTarget))
            using (var targetBrush = Brushes.LimeGreen.ToDxBrush(RenderTarget))
            {
                foreach (TradeLineSnapshot snapshot in historicalTradeLines)
                {
                    DrawRenderedTradeLineSet(
                        chartControl,
                        chartScale,
                        snapshot.StartBar,
                        snapshot.EndBar,
                        snapshot.EntryPrice,
                        snapshot.StopPrice,
                        snapshot.HasTakeProfit,
                        snapshot.TakeProfitPrice,
                        snapshot.HasTakeProfitTrigger,
                        snapshot.TakeProfitTriggerPrice,
                        entryBrush,
                        stopBrush,
                        targetBrush);
                }

                if (hasActiveSegment)
                {
                    DrawRenderedTradeLineSet(
                        chartControl,
                        chartScale,
                        tradeLineSignalBar,
                        CurrentBar,
                        tradeLineEntryPrice,
                        tradeLineSlPrice,
                        tradeLineHasTp,
                        tradeLineTpPrice,
                        tradeLineHasTpTrigger,
                        tradeLineTpTriggerPrice,
                        entryBrush,
                        stopBrush,
                        targetBrush);
                }
            }

            RenderTarget.AntialiasMode = oldAntialiasMode;
        }

        private bool IsEntryOrderName(string orderName)
        {
            return IsLongEntryOrderName(orderName) || IsShortEntryOrderName(orderName);
        }

        private bool IsTerminalExitExecution(string orderName)
        {
            if (IsEntryOrderName(orderName))
                return false;

            if (!string.Equals(orderName, "Close position", StringComparison.OrdinalIgnoreCase))
                return true;

            return !IsFlipTransitionActive();
        }

        private bool IsFlipTransitionActive()
        {
            return pendingLongEntryIsFlip || pendingShortEntryIsFlip || currentPositionIsFlipEntry;
        }

        private void ResetPositionTrackingState()
        {
            currentPositionEntrySignal = string.Empty;
            currentPositionIsFlipEntry = false;
            flipBreakEvenActivated = false;
            takeProfitStopTriggered = false;
            initialStopPrice = 0.0;
            currentStopPrice = 0.0;
            adxDdRiskModeApplied = false;
            currentPositionEntryBar = -1;
            ClearProtectionAuditState();
        }

        private void ReleasePositionTrackingAfterTerminalExit(DateTime time, string orderName)
        {
            if (lockedTradeSession != SessionSlot.None)
            {
                LogDebug(string.Format(
                    "Session lock released from {0} after terminal exit execution ({1}).",
                    FormatSessionLabel(lockedTradeSession),
                    orderName));
                lockedTradeSession = SessionSlot.None;
            }

            ResetPositionTrackingState();

            SessionSlot desired = DetermineSessionForTime(time);
            if (!sessionInitialized || desired != activeSession)
            {
                activeSession = desired;
                if (activeSession != SessionSlot.None)
                {
                    ApplyInputsForSession(activeSession);
                    LogSessionActivation("switch");
                }

                sessionInitialized = true;
                LogDebug(string.Format(
                    "Active session switched to {0} after terminal exit execution ({1}).",
                    FormatSessionLabel(activeSession),
                    orderName));
            }
        }

        private bool ShouldFinalizeTradeLinesOnExecution(string orderName)
        {
            if (!tradeLinesActive)
                return false;

            // During a managed flip, NinjaTrader emits a synthetic "Close position" execution
            // for the old leg. The new trade lines have already been started for the incoming leg,
            // so finalizing them here would erase the flip's fresh line set before its entry fill arrives.
            return IsTerminalExitExecution(orderName);
        }

        private double GetEffectiveFlipEmaCrossPoints()
        {
            return activeFlipEmaCrossPoints > 0.0
                ? activeFlipEmaCrossPoints
                : activeExitCrossPoints;
        }

        private double GetPlannedStopLossPoints(double entryPrice, double stopPrice)
        {
            return Instrument.MasterInstrument.RoundToTickSize(Math.Abs(entryPrice - stopPrice));
        }

        private bool IsWithinMaxStopLossPoints(double stopLossPoints)
        {
            return activeMaxStopLossPoints <= 0.0
                || stopLossPoints <= activeMaxStopLossPoints + TickSize * 0.5;
        }

        private bool IsLongEntryOrderName(string orderName)
        {
            return string.Equals(orderName, LongEntrySignal, StringComparison.Ordinal)
                || string.Equals(orderName, LongFlipEntrySignal, StringComparison.Ordinal);
        }

        private bool IsShortEntryOrderName(string orderName)
        {
            return string.Equals(orderName, ShortEntrySignal, StringComparison.Ordinal)
                || string.Equals(orderName, ShortFlipEntrySignal, StringComparison.Ordinal);
        }

        private string GetOpenLongEntrySignal()
        {
            return IsLongEntryOrderName(currentPositionEntrySignal) ? currentPositionEntrySignal : LongEntrySignal;
        }

        private string GetOpenShortEntrySignal()
        {
            return IsShortEntryOrderName(currentPositionEntrySignal) ? currentPositionEntrySignal : ShortEntrySignal;
        }

        private string BuildExitSignalName(string reason)
        {
            return "DUO" + reason;
        }

        private void TryApplyFlipBreakEvenStop()
        {
            if (!activeEnableFlipBreakEven || activeFlipBreakEvenTriggerPoints <= 0.0 || flipBreakEvenActivated)
                return;

            if (!currentPositionIsFlipEntry || Position.MarketPosition == MarketPosition.Flat)
                return;

            double averagePrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice);
            double closePrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            string entrySignal = Position.MarketPosition == MarketPosition.Long
                ? GetOpenLongEntrySignal()
                : GetOpenShortEntrySignal();

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (closePrice < averagePrice + activeFlipBreakEvenTriggerPoints)
                    return;
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (closePrice > averagePrice - activeFlipBreakEvenTriggerPoints)
                    return;
            }
            else
            {
                return;
            }

            bool stopValid = IsManagedStopPriceValid(averagePrice, closePrice);
            bool stopApplied = stopValid && ApplyManagedStop(entrySignal, averagePrice);
            flipBreakEvenActivated = true;

            LogDebug(string.Format(
                "Flip BE armed | signal={0} triggerPts={1:0.00} stop={2:0.00} close={3:0.00} stopValid={4} stopApplied={5}",
                entrySignal,
                activeFlipBreakEvenTriggerPoints,
                averagePrice,
                closePrice,
                stopValid,
                stopApplied));
        }

        private void TryManageTakeProfitTriggeredStop()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            double activePositionTakeProfitPoints = GetActivePositionTakeProfitPoints();
            if (activeTakeProfitPercentTriggerPercent <= 0.0 || activePositionTakeProfitPoints <= 0.0)
                return;

            double averagePrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice);
            double closePrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            string entrySignal = Position.MarketPosition == MarketPosition.Long
                ? GetOpenLongEntrySignal()
                : GetOpenShortEntrySignal();
            double triggerPoints = activePositionTakeProfitPoints * (activeTakeProfitPercentTriggerPercent / 100.0);

            if (!takeProfitStopTriggered)
            {
                bool triggerReached = Position.MarketPosition == MarketPosition.Long
                    ? closePrice >= averagePrice + triggerPoints
                    : closePrice <= averagePrice - triggerPoints;
                if (!triggerReached)
                    return;

                takeProfitStopTriggered = true;
                LogDebug(string.Format(
                    "TP stop trigger armed | signal={0} mode={1} triggerPct={2:0.##} triggerPts={3:0.00} avg={4:0.00} close={5:0.00} tpPts={6:0.00}",
                    entrySignal,
                    activeTakeProfitStopMode,
                    activeTakeProfitPercentTriggerPercent,
                    triggerPoints,
                    averagePrice,
                    closePrice,
                    activePositionTakeProfitPoints));
            }

            if (activeTakeProfitStopMode == TakeProfitStopMode.AtrTrail)
            {
                TryApplyTakeProfitAtrTrailStop(entrySignal, closePrice);
                return;
            }

            double stopMovePoints = activePositionTakeProfitPoints * (activeTakeProfitPercentStopMovePercent / 100.0);
            double stopPrice = Position.MarketPosition == MarketPosition.Long
                ? averagePrice + stopMovePoints
                : averagePrice - stopMovePoints;
            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

            if (!IsManagedStopPriceValid(stopPrice, closePrice))
                return;

            bool stopApplied = ApplyManagedStop(entrySignal, stopPrice);
            if (stopApplied)
            {
                LogDebug(string.Format(
                    "TP percent stop moved | signal={0} triggerPct={1:0.##} stopPct={2:0.##} stop={3:0.00} avg={4:0.00} close={5:0.00}",
                    entrySignal,
                    activeTakeProfitPercentTriggerPercent,
                    activeTakeProfitPercentStopMovePercent,
                    stopPrice,
                    averagePrice,
                    closePrice));
            }
        }

        private void TryApplyTakeProfitAtrTrailStop(string entrySignal, double closePrice)
        {
            if (takeProfitAtr == null || activeTakeProfitAtrTrailMultiplier <= 0.0)
                return;

            double atrValue = Instrument.MasterInstrument.RoundToTickSize(takeProfitAtr[0]);
            if (atrValue <= 0.0)
                return;

            double trailDistance = atrValue * activeTakeProfitAtrTrailMultiplier;
            double stopPrice = Position.MarketPosition == MarketPosition.Long
                ? closePrice - trailDistance
                : closePrice + trailDistance;
            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

            if (!IsManagedStopPriceValid(stopPrice, closePrice))
                return;

            bool stopApplied = ApplyManagedStop(entrySignal, stopPrice);
            if (stopApplied)
            {
                LogDebug(string.Format(
                    "TP ATR trail moved | signal={0} atrPeriod={1} atr={2:0.00} multiplier={3:0.##} stop={4:0.00} close={5:0.00}",
                    entrySignal,
                    TakeProfitAtrPeriod,
                    atrValue,
                    activeTakeProfitAtrTrailMultiplier,
                    stopPrice,
                    closePrice));
            }
        }

        private bool ApplyManagedStop(string entrySignal, double stopPrice)
        {
            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            if (!ShouldTightenManagedStop(stopPrice))
                return false;

            SetStopLoss(entrySignal, CalculationMode.Price, stopPrice, false);
            currentStopPrice = stopPrice;
            UpdateTradeLineStopPrice(stopPrice);
            return true;
        }

        private bool ShouldTightenManagedStop(double stopPrice)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                return currentStopPrice <= 0.0 || stopPrice > currentStopPrice + TickSize * 0.5;

            if (Position.MarketPosition == MarketPosition.Short)
                return currentStopPrice <= 0.0 || stopPrice < currentStopPrice - TickSize * 0.5;

            return false;
        }

        private bool IsManagedStopPriceValid(double stopPrice, double closePrice)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                return stopPrice <= closePrice - TickSize;

            if (Position.MarketPosition == MarketPosition.Short)
                return stopPrice >= closePrice + TickSize;

            return false;
        }

        private bool HasManagedStopTightened()
        {
            if (Position.MarketPosition == MarketPosition.Flat || initialStopPrice <= 0.0 || currentStopPrice <= 0.0)
                return false;

            if (Position.MarketPosition == MarketPosition.Long)
                return currentStopPrice > initialStopPrice + TickSize * 0.5;

            if (Position.MarketPosition == MarketPosition.Short)
                return currentStopPrice < initialStopPrice - TickSize * 0.5;

            return false;
        }

        private bool TryExitStaleTrade()
        {
            if (activeHorizontalExitBars <= 0 || Position.MarketPosition == MarketPosition.Flat || currentPositionEntryBar < 0)
                return false;

            int barsHeld = CurrentBar - currentPositionEntryBar;
            if (barsHeld < activeHorizontalExitBars)
                return false;

            double averagePrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice);
            double closePrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(BuildExitSignalName("HorizontalExit"), GetOpenLongEntrySignal());
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(BuildExitSignalName("HorizontalExit"), GetOpenShortEntrySignal());
            else
                return false;

            LogDebug(string.Format(
                "Exit {0} | reason=Horizontal barsHeld={1} threshold={2} avg={3:0.00} close={4:0.00} entryBar={5}",
                Position.MarketPosition,
                barsHeld,
                activeHorizontalExitBars,
                averagePrice,
                closePrice,
                currentPositionEntryBar));

            return true;
        }

        private double GetEffectiveFlipTakeProfitPoints()
        {
            return activeFlipTakeProfitPoints > 0.0 ? activeFlipTakeProfitPoints : activeTakeProfitPoints;
        }

        private double GetCurrentAtrValue()
        {
            if (takeProfitAtr == null)
                return 0.0;

            double atrValue = takeProfitAtr[0];
            if (double.IsNaN(atrValue) || double.IsInfinity(atrValue) || atrValue <= 0.0)
                return 0.0;

            return atrValue;
        }

        private double GetConfiguredEntryTakeProfitPoints(bool isFlipEntry)
        {
            return isFlipEntry
                ? GetEffectiveFlipTakeProfitPoints()
                : activeTakeProfitPoints;
        }

        private double GetActivePositionTakeProfitPoints()
        {
            if (adxDdRiskModeApplied && activeAdxDdRiskModeTakeProfitPoints > 0.0)
                return activeAdxDdRiskModeTakeProfitPoints;

            return GetConfiguredEntryTakeProfitPoints(currentPositionIsFlipEntry);
        }

        private void TryApplyAdxDdRiskMode(double adxValue, double adxDrawdown)
        {
            if (adxDdRiskModeApplied || Position.MarketPosition == MarketPosition.Flat)
                return;

            string entrySignal = Position.MarketPosition == MarketPosition.Long
                ? GetOpenLongEntrySignal()
                : GetOpenShortEntrySignal();
            double averagePrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice);
            double closePrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            double tickSize = TickSize;

            bool slApplied = false;
            bool tpApplied = false;
            bool slSkippedInvalid = false;
            bool tpSkippedInvalid = false;

            if (activeAdxDdRiskModeStopLossPoints > 0.0 && !HasManagedStopTightened())
            {
                double stopPrice = Position.MarketPosition == MarketPosition.Long
                    ? averagePrice - activeAdxDdRiskModeStopLossPoints
                    : averagePrice + activeAdxDdRiskModeStopLossPoints;
                stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

                bool stopValid = Position.MarketPosition == MarketPosition.Long
                    ? stopPrice <= closePrice - tickSize
                    : stopPrice >= closePrice + tickSize;

                if (stopValid)
                {
                    slApplied = ApplyManagedStop(entrySignal, stopPrice);
                }
                else
                {
                    slSkippedInvalid = true;
                }
            }

            if (activeAdxDdRiskModeTakeProfitPoints > 0.0)
            {
                double targetPrice = Position.MarketPosition == MarketPosition.Long
                    ? averagePrice + activeAdxDdRiskModeTakeProfitPoints
                    : averagePrice - activeAdxDdRiskModeTakeProfitPoints;
                targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);

                bool targetValid = Position.MarketPosition == MarketPosition.Long
                    ? targetPrice >= closePrice + tickSize
                    : targetPrice <= closePrice - tickSize;

                if (targetValid)
                {
                    int targetTicks = PriceToTicks(activeAdxDdRiskModeTakeProfitPoints);
                    if (targetTicks < 1)
                        targetTicks = 1;
                    SetProfitTarget(entrySignal, CalculationMode.Ticks, targetTicks);
                    UpdateTradeLineTakeProfitPrice(targetPrice);
                    tpApplied = true;
                }
                else
                {
                    tpSkippedInvalid = true;
                }
            }

            adxDdRiskModeApplied = true;

            LogDebug(string.Format(
                "ADX DD risk mode armed | side={0} adx={1:0.00} peak={2:0.00} drawdown={3:0.00} threshold={4:0.00} close={5:0.00} slPts={6:0.00} slApplied={7} slSkippedInvalid={8} tpPts={9:0.00} tpApplied={10} tpSkippedInvalid={11} stopTightened={12}",
                Position.MarketPosition,
                adxValue,
                currentTradePeakAdx,
                adxDrawdown,
                activeAdxPeakDrawdownExitUnits,
                closePrice,
                activeAdxDdRiskModeStopLossPoints,
                slApplied,
                slSkippedInvalid,
                activeAdxDdRiskModeTakeProfitPoints,
                tpApplied,
                tpSkippedInvalid,
                HasManagedStopTightened()));
        }

        private bool ShouldSendExitWebhook(Execution execution, string orderName, MarketPosition marketPosition)
        {
            if (execution?.Order == null)
                return false;

            if (IsEntryOrderName(orderName))
                return false;

            string fromEntry = execution.Order.FromEntrySignal ?? string.Empty;
            if (IsEntryOrderName(fromEntry))
                return true;

            string normalized = orderName ?? string.Empty;
            if (normalized.Length == 0)
                return marketPosition == MarketPosition.Flat;

            if (normalized.Equals("Stop loss", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Profit target", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Exit on session close", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("SessionEnd", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("ExitLong", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("ExitShort", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("EmaExitLong", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("EmaExitShort", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("AdxDrawdownExit", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("AdxLevelExit", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("TakeProfitExit", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("ProtectiveReject", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("TwoCandleReverse", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("DUO", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return marketPosition == MarketPosition.Flat;
        }

        private void StartTradeLines(double entryPrice, double stopPrice, double takeProfitPrice, bool hasTakeProfit)
        {
            if (tradeLinesActive)
                FinalizeTradeLines();

            tradeLineTagPrefix = string.Format("DUO_TradeLine_{0}_{1}_", ++tradeLineTagCounter, CurrentBar);
            tradeLinesActive = true;
            tradeLineSignalBar = Math.Max(0, CurrentBar - 1);
            tradeLineExitBar = -1;
            tradeLineEntryPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice);
            tradeLineSlPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            SetTradeLineTakeProfitState(takeProfitPrice, hasTakeProfit);

            DrawTradeLinesAtBarsAgo(1, 0);
            RequestTradeLineRender();
        }

        private void UpdateTradeLines()
        {
            if (!tradeLinesActive || tradeLineSignalBar < 0)
                return;

            int startBarsAgo = Math.Max(0, CurrentBar - tradeLineSignalBar);
            int endBarsAgo = tradeLineExitBar >= 0
                ? Math.Max(0, CurrentBar - tradeLineExitBar)
                : 0;

            DrawTradeLinesAtBarsAgo(startBarsAgo, endBarsAgo);

            if (ShouldRenderActiveTradeLinesCustom())
                RequestTradeLineRender();
        }

        private void FinalizeTradeLines()
        {
            if (!tradeLinesActive)
                return;

            tradeLineExitBar = CurrentBar;
            historicalTradeLines.Add(new TradeLineSnapshot
            {
                StartBar = tradeLineSignalBar,
                EndBar = tradeLineExitBar,
                EntryPrice = tradeLineEntryPrice,
                StopPrice = tradeLineSlPrice,
                HasTakeProfit = tradeLineHasTp,
                TakeProfitPrice = tradeLineTpPrice,
                HasTakeProfitTrigger = tradeLineHasTpTrigger,
                TakeProfitTriggerPrice = tradeLineTpTriggerPrice
            });
            UpdateTradeLines();
            tradeLinesActive = false;
            ClearTradeLineState();
            RequestTradeLineRender();
        }

        private void ClearTradeLineState()
        {
            tradeLineHasTp = false;
            tradeLineHasTpTrigger = false;
            tradeLineSignalBar = -1;
            tradeLineExitBar = -1;
            tradeLineEntryPrice = 0.0;
            tradeLineTpPrice = 0.0;
            tradeLineTpTriggerPrice = 0.0;
            tradeLineSlPrice = 0.0;
        }

        private void UpdateTradeLineStopPrice(double stopPrice)
        {
            if (!tradeLinesActive)
                return;

            tradeLineSlPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            UpdateTradeLines();
        }

        private void UpdateTradeLineTakeProfitPrice(double takeProfitPrice)
        {
            if (!tradeLinesActive)
                return;

            SetTradeLineTakeProfitState(takeProfitPrice, takeProfitPrice > 0.0);
            UpdateTradeLines();
        }

        private void SyncTradeLinesToProtectiveOrder(Order order, double limitPrice, double stopPrice)
        {
            if (!tradeLinesActive || order == null || !IsOrderActive(order))
                return;

            string orderName = order.Name ?? string.Empty;
            if (orderName.Equals("Stop loss", StringComparison.OrdinalIgnoreCase))
            {
                double actualStopPrice = GetWorkingOrderStopPrice(order, stopPrice);
                if (actualStopPrice > 0.0)
                {
                    currentStopPrice = actualStopPrice;
                    UpdateTradeLineStopPrice(actualStopPrice);
                }
            }
            else if (orderName.Equals("Profit target", StringComparison.OrdinalIgnoreCase))
            {
                double actualTargetPrice = GetWorkingOrderLimitPrice(order, limitPrice);
                if (actualTargetPrice > 0.0)
                    UpdateTradeLineTakeProfitPrice(actualTargetPrice);
            }
        }

        private void SyncTradeLinesToFilledEntryOrder(Order order, OrderState orderState, double averageFillPrice)
        {
            if (!tradeLinesActive || order == null || orderState != OrderState.Filled || !IsEntryOrderName(order.Name))
                return;

            double actualFillPrice = averageFillPrice > 0.0
                ? Instrument.MasterInstrument.RoundToTickSize(averageFillPrice)
                : 0.0;
            if (actualFillPrice <= 0.0)
                return;

            bool isLongEntry = IsLongEntryOrderName(order.Name);
            bool isFlipEntry = isLongEntry ? pendingLongEntryIsFlip : pendingShortEntryIsFlip;
            double plannedStopPrice = isLongEntry ? pendingLongStopForWebhook : pendingShortStopForWebhook;
            double actualStopPrice = BuildFilledStopPrice(
                isLongEntry ? MarketPosition.Long : MarketPosition.Short,
                actualFillPrice,
                plannedStopPrice);

            ReanchorTradeLinesToEntryFill(
                isLongEntry ? MarketPosition.Long : MarketPosition.Short,
                actualFillPrice,
                isFlipEntry,
                actualStopPrice);
        }

        private void SyncTradeLinesToLivePositionAndOrders()
        {
            if (!tradeLinesActive || Position.MarketPosition == MarketPosition.Flat)
                return;

            bool changed = false;

            double actualEntryPrice = Instrument.MasterInstrument.RoundToTickSize(Position.AveragePrice);
            if (actualEntryPrice > 0.0 && Math.Abs(actualEntryPrice - tradeLineEntryPrice) > TickSize * 0.5)
            {
                tradeLineEntryPrice = actualEntryPrice;
                changed = true;
            }

            if (IsOrderActive(activeStopLossOrder))
            {
                double actualStopPrice = GetWorkingOrderStopPrice(activeStopLossOrder, 0.0);
                if (actualStopPrice > 0.0 && Math.Abs(actualStopPrice - tradeLineSlPrice) > TickSize * 0.5)
                {
                    tradeLineSlPrice = actualStopPrice;
                    currentStopPrice = actualStopPrice;
                    changed = true;
                }
            }

            if (IsOrderActive(activeProfitTargetOrder))
            {
                double actualTargetPrice = GetWorkingOrderLimitPrice(activeProfitTargetOrder, 0.0);
                if (actualTargetPrice > 0.0 && (!tradeLineHasTp || Math.Abs(actualTargetPrice - tradeLineTpPrice) > TickSize * 0.5 || changed))
                {
                    SetTradeLineTakeProfitState(actualTargetPrice, true);
                    changed = true;
                }
            }

            if (changed)
                UpdateTradeLines();
        }

        private double GetWorkingOrderStopPrice(Order order, double fallbackStopPrice)
        {
            double rawStopPrice = order != null && order.StopPrice > 0.0
                ? order.StopPrice
                : fallbackStopPrice;
            return rawStopPrice > 0.0
                ? Instrument.MasterInstrument.RoundToTickSize(rawStopPrice)
                : 0.0;
        }

        private double GetWorkingOrderLimitPrice(Order order, double fallbackLimitPrice)
        {
            double rawLimitPrice = order != null && order.LimitPrice > 0.0
                ? order.LimitPrice
                : fallbackLimitPrice;
            return rawLimitPrice > 0.0
                ? Instrument.MasterInstrument.RoundToTickSize(rawLimitPrice)
                : 0.0;
        }

        private double BuildFilledStopPrice(MarketPosition marketPosition, double fillPrice, double plannedStopPrice)
        {
            if (marketPosition == MarketPosition.Flat || plannedStopPrice <= 0.0)
                return 0.0;

            double referenceEntryPrice = tradeLineEntryPrice > 0.0
                ? tradeLineEntryPrice
                : fillPrice;
            int stopTicks = PriceToTicks(Math.Abs(referenceEntryPrice - plannedStopPrice));
            if (stopTicks < 1)
                stopTicks = 1;

            double stopPrice = marketPosition == MarketPosition.Long
                ? fillPrice - stopTicks * TickSize
                : fillPrice + stopTicks * TickSize;
            return Instrument.MasterInstrument.RoundToTickSize(stopPrice);
        }

        private void ReanchorTradeLinesToEntryFill(MarketPosition marketPosition, double fillPrice, bool isFlipEntry, double stopPrice)
        {
            if (!tradeLinesActive || marketPosition == MarketPosition.Flat)
                return;

            tradeLineEntryPrice = Instrument.MasterInstrument.RoundToTickSize(fillPrice);
            tradeLineSlPrice = stopPrice > 0.0
                ? Instrument.MasterInstrument.RoundToTickSize(stopPrice)
                : 0.0;

            double takeProfitPoints = GetConfiguredEntryTakeProfitPoints(isFlipEntry);
            if (takeProfitPoints > 0.0)
            {
                int targetTicks = PriceToTicks(takeProfitPoints);
                if (targetTicks < 1)
                    targetTicks = 1;

                double takeProfitPrice = marketPosition == MarketPosition.Long
                    ? fillPrice + targetTicks * TickSize
                    : fillPrice - targetTicks * TickSize;
                SetTradeLineTakeProfitState(takeProfitPrice, true);
            }
            else
            {
                SetTradeLineTakeProfitState(0.0, false);
            }

            UpdateTradeLines();
        }

        private void SetTradeLineTakeProfitState(double takeProfitPrice, bool hasTakeProfit)
        {
            tradeLineHasTp = hasTakeProfit;
            tradeLineTpPrice = hasTakeProfit
                ? Instrument.MasterInstrument.RoundToTickSize(takeProfitPrice)
                : 0.0;
            tradeLineHasTpTrigger = false;
            tradeLineTpTriggerPrice = 0.0;

            if (!hasTakeProfit || activeTakeProfitPercentTriggerPercent <= 0.0)
                return;

            double tpDistance = tradeLineTpPrice - tradeLineEntryPrice;
            if (Math.Abs(tpDistance) < TickSize * 0.5)
                return;

            double triggerPrice = tradeLineEntryPrice + tpDistance * (activeTakeProfitPercentTriggerPercent / 100.0);
            tradeLineTpTriggerPrice = Instrument.MasterInstrument.RoundToTickSize(triggerPrice);
            tradeLineHasTpTrigger = true;
        }

        private void DrawTradeLinesAtBarsAgo(int startBarsAgo, int endBarsAgo)
        {
            if (string.IsNullOrEmpty(tradeLineTagPrefix))
                return;

            if (UseCustomTradeLineRendering())
                return;

            Draw.Line(this, tradeLineTagPrefix + "Entry", false,
                startBarsAgo, tradeLineEntryPrice,
                endBarsAgo, tradeLineEntryPrice,
                Brushes.Gold, DashStyleHelper.Solid, 2);

            Draw.Line(this, tradeLineTagPrefix + "SL", false,
                startBarsAgo, tradeLineSlPrice,
                endBarsAgo, tradeLineSlPrice,
                Brushes.Red, DashStyleHelper.Solid, 2);

            if (tradeLineHasTp)
            {
                Draw.Line(this, tradeLineTagPrefix + "TP", false,
                    startBarsAgo, tradeLineTpPrice,
                    endBarsAgo, tradeLineTpPrice,
                    Brushes.LimeGreen, DashStyleHelper.Solid, 2);
            }

            if (tradeLineHasTpTrigger)
            {
                Draw.Line(this, tradeLineTagPrefix + "TPTrigger", false,
                    startBarsAgo, tradeLineTpTriggerPrice,
                    endBarsAgo, tradeLineTpTriggerPrice,
                    Brushes.LimeGreen, DashStyleHelper.Dot, 2);
            }
        }

        private bool ShouldRenderActiveTradeLinesCustom()
        {
            return tradeLinesActive && tradeLineSignalBar >= 0 && tradeLineExitBar < 0;
        }

        private bool UseCustomTradeLineRendering()
        {
            return true;
        }

        private void RequestTradeLineRender()
        {
            if (ChartControl == null)
                return;

            if (ChartControl.Dispatcher == null || ChartControl.Dispatcher.CheckAccess())
            {
                ChartControl.InvalidateVisual();
                return;
            }

            ChartControl.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ChartControl != null)
                    ChartControl.InvalidateVisual();
            }));
        }

        private float GetSnappedTradeLineY(ChartScale chartScale, double price)
        {
            float priceY = (float)chartScale.GetYByValue(price);
            return (float)Math.Round(priceY);
        }

        private void DrawRenderedTradeLineSet(
            ChartControl chartControl,
            ChartScale chartScale,
            int startBarIndex,
            int endBarIndex,
            double entryPrice,
            double stopPrice,
            bool hasTakeProfit,
            double takeProfitPrice,
            bool hasTakeProfitTrigger,
            double takeProfitTriggerPrice,
            SharpDX.Direct2D1.Brush entryBrush,
            SharpDX.Direct2D1.Brush stopBrush,
            SharpDX.Direct2D1.Brush targetBrush)
        {
            int visibleStartBarIndex = Math.Max(ChartBars.FromIndex, startBarIndex);
            int visibleEndBarIndex = Math.Min(ChartBars.ToIndex, endBarIndex);
            if (visibleEndBarIndex < visibleStartBarIndex)
                return;

            float startX = chartControl.GetXByBarIndex(ChartBars, visibleStartBarIndex);
            float endX = chartControl.GetXByBarIndex(ChartBars, visibleEndBarIndex);
            float panelLeft = ChartPanel != null ? ChartPanel.X : startX;
            float panelRight = ChartPanel != null ? ChartPanel.X + ChartPanel.W : endX;
            startX = Math.Max(startX, panelLeft);
            endX = Math.Min(endX, panelRight);
            if (endX <= startX)
                return;

            DrawPixelSnappedHorizontalLine(chartScale, startX, endX, entryPrice, entryBrush, 2f);
            DrawPixelSnappedHorizontalLine(chartScale, startX, endX, stopPrice, stopBrush, 2f);

            if (hasTakeProfit)
                DrawPixelSnappedHorizontalLine(chartScale, startX, endX, takeProfitPrice, targetBrush, 2f);

            if (hasTakeProfitTrigger)
                DrawPixelSnappedDottedHorizontalLine(chartScale, startX, endX, takeProfitTriggerPrice, targetBrush, 2f);
        }

        private void DrawPixelSnappedHorizontalLine(ChartScale chartScale, float startX, float endX, double price, SharpDX.Direct2D1.Brush brush, float width)
        {
            if (price <= 0.0 || endX <= startX)
                return;

            float y = GetSnappedTradeLineY(chartScale, price);
            RenderTarget.DrawLine(new SharpDX.Vector2(startX, y), new SharpDX.Vector2(endX, y), brush, width);
        }

        private void DrawPixelSnappedDottedHorizontalLine(ChartScale chartScale, float startX, float endX, double price, SharpDX.Direct2D1.Brush brush, float width)
        {
            if (price <= 0.0 || endX <= startX)
                return;

            float y = GetSnappedTradeLineY(chartScale, price);
            const float dashLength = 4f;
            const float gapLength = 4f;

            for (float x = startX; x < endX; x += dashLength + gapLength)
            {
                float segmentEndX = Math.Min(x + dashLength, endX);
                RenderTarget.DrawLine(new SharpDX.Vector2(x, y), new SharpDX.Vector2(segmentEndX, y), brush, width);
            }
        }

        private void UpdateActiveSession(DateTime time)
        {
            SessionSlot desired = DetermineSessionForTime(time);
            bool inPosition = Position.MarketPosition != MarketPosition.Flat;

            if (inPosition)
            {
                if (lockedTradeSession == SessionSlot.None)
                {
                    SessionSlot inferredLock = activeSession != SessionSlot.None
                        ? activeSession
                        : desired;
                    if (inferredLock != SessionSlot.None)
                    {
                        lockedTradeSession = inferredLock;
                        LogDebug(string.Format("Session lock inferred as {0} while position is open.", FormatSessionLabel(lockedTradeSession)));
                    }
                }

                if (lockedTradeSession != SessionSlot.None && activeSession != lockedTradeSession)
                {
                    activeSession = lockedTradeSession;
                    ApplyInputsForSession(activeSession);
                    LogSessionActivation("lock");
                    LogDebug(string.Format("Active session held at {0} while position is open.", FormatSessionLabel(activeSession)));
                }

                sessionInitialized = true;
                if (DebugLogging && desired != activeSession && CurrentBar != lastDeferredSessionLogBar)
                {
                    lastDeferredSessionLogBar = CurrentBar;
                    LogDebug(string.Format(
                        "Session switch deferred | current={0} desired={1} pos={2}",
                        FormatSessionLabel(activeSession),
                        FormatSessionLabel(desired),
                        Position.MarketPosition));
                }
                return;
            }

            if (lockedTradeSession != SessionSlot.None)
            {
                LogDebug(string.Format("Session lock cleared while flat (was {0}).", FormatSessionLabel(lockedTradeSession)));
                lockedTradeSession = SessionSlot.None;
            }

            if (!sessionInitialized || desired != activeSession)
            {
                activeSession = desired;
                if (activeSession != SessionSlot.None)
                {
                    ApplyInputsForSession(activeSession);
                    LogSessionActivation("switch");
                }

                sessionInitialized = true;
                LogDebug(string.Format("Active session switched to {0}", FormatSessionLabel(activeSession)));
            }
        }

        private static SessionFamily GetSessionFamily(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                case SessionSlot.Asia2:
                case SessionSlot.Asia3:
                    return SessionFamily.Asia;

                case SessionSlot.London:
                case SessionSlot.London2:
                case SessionSlot.London3:
                    return SessionFamily.London;

                case SessionSlot.NewYork:
                case SessionSlot.NewYork2:
                case SessionSlot.NewYork3:
                    return SessionFamily.NewYork;

                default:
                    return SessionFamily.None;
            }
        }

        private static bool IsAsiaFamily(SessionSlot slot)
        {
            return GetSessionFamily(slot) == SessionFamily.Asia;
        }

        private static bool IsLondonFamily(SessionSlot slot)
        {
            return GetSessionFamily(slot) == SessionFamily.London;
        }

        private static bool IsNewYorkFamily(SessionSlot slot)
        {
            return GetSessionFamily(slot) == SessionFamily.NewYork;
        }

        private SessionSlot DetermineSessionForTime(DateTime time)
        {
            TimeSpan now = time.TimeOfDay;
            SessionSlot nextSlot = SessionSlot.None;
            DateTime nextStart = DateTime.MaxValue;
            bool hasConfiguredSession = false;

            foreach (SessionSlot slot in ConfigurableSessionSlots)
            {
                TimeSpan start;
                TimeSpan end;
                if (!IsSessionConfigured(slot) || !TryGetSessionWindow(slot, time, out start, out end))
                    continue;

                hasConfiguredSession = true;
                if (IsTimeInRange(now, start, end))
                    return slot;

                DateTime candidateStart = time.Date + start;
                if (candidateStart <= time)
                    candidateStart = candidateStart.AddDays(1);

                if (candidateStart < nextStart)
                {
                    nextStart = candidateStart;
                    nextSlot = slot;
                }
            }

            return hasConfiguredSession ? nextSlot : SessionSlot.None;
        }

        private SessionSlot GetFirstConfiguredSession()
        {
            foreach (SessionSlot slot in ConfigurableSessionSlots)
            {
                if (IsSessionConfigured(slot))
                    return slot;
            }

            return SessionSlot.None;
        }

        private bool IsSessionConfigured(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    return UseAsiaSession && AsiaSessionStart != AsiaSessionEnd;
                case SessionSlot.Asia2:
                    return UseAsia2Session && Asia2SessionStart != Asia2SessionEnd;
                case SessionSlot.Asia3:
                    return UseAsia3Session && Asia3SessionStart != Asia3SessionEnd;
                case SessionSlot.London:
                    return UseLondonSession && LondonSessionStart != LondonSessionEnd;
                case SessionSlot.London2:
                    return UseLondon2Session && London2SessionStart != London2SessionEnd;
                case SessionSlot.London3:
                    return UseLondon3Session && London3SessionStart != London3SessionEnd;
                case SessionSlot.NewYork:
                    return UseNewYorkSession && NewYorkSessionStart != NewYorkSessionEnd;
                case SessionSlot.NewYork2:
                    return UseNewYork2Session && NewYork2SessionStart != NewYork2SessionEnd;
                case SessionSlot.NewYork3:
                    return UseNewYork3Session && NewYork3SessionStart != NewYork3SessionEnd;
                default:
                    return false;
            }
        }

        private void ApplyInputsForSession(SessionSlot session)
        {
            switch (session)
            {
                case SessionSlot.Asia:
                    activeEma = emaAsia;
                    activeAdx = adxAsia;
                    activeEmaPeriod = AsiaEmaPeriod;
                    activeAdxPeriod = AsiaAdxPeriod;
                    activeAdxThreshold = AsiaAdxThreshold;
                    activeFlipAdxThreshold = AsiaFlipAdxThreshold;
                    activeAdxMaxThreshold = AsiaAdxMaxThreshold;
                    activeAdxMinSlopePoints = AsiaAdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = AsiaAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = AsiaAdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = AsiaContracts;
                    activeTradeDirection = AsiaTradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = AsiaEmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = AsiaMaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = AsiaStopPaddingPoints;
                    activeExitCrossPoints = AsiaExitCrossPoints;
                    activeFlipEmaCrossPoints = AsiaFlipEmaCrossPoints;
                    activeMaxStopLossPoints = AsiaMaxStopLossPoints;
                    activeTakeProfitPoints = AsiaTakeProfitPoints;
                    activeMinimumAtrForEntry = AsiaAtrMinimum;
                    activeHvSlPaddingPoints = 0.0;
                    activeHvSlStartTime = TimeSpan.Zero;
                    activeHvSlEndTime = TimeSpan.Zero;
                    activeEntryOffsetPoints = AsiaEntryOffsetPoints;
                    activeEnableFlipBreakEven = AsiaEnableFlipBreakEven;
                    activeFlipBreakEvenTriggerPoints = AsiaFlipBreakEvenTriggerPoints;
                    activeFlipTakeProfitPoints = AsiaFlipTakeProfitPoints;
                    activeTakeProfitPercentTriggerPercent = AsiaTakeProfitPercentTriggerPercent;
                    activeTakeProfitStopMode = AsiaTakeProfitStopMode;
                    activeTakeProfitAtrTrailMultiplier = AsiaTakeProfitAtrTrailMultiplier;
                    activeTakeProfitPercentStopMovePercent = AsiaTakeProfitPercentStopMovePercent;
                    activeRequireMinAdxForFlips = AsiaRequireMinAdxForFlips;
                    activeEnableAdxDdRiskMode = AsiaEnableAdxDdRiskMode;
                    activeAdxDdRiskModeStopLossPoints = AsiaAdxDdRiskModeStopLossPoints;
                    activeAdxDdRiskModeTakeProfitPoints = AsiaAdxDdRiskModeTakeProfitPoints;
                    activeHorizontalExitBars = AsiaHorizontalExitBars;
                    break;

                case SessionSlot.Asia2:
                    activeEma = emaAsia2;
                    activeAdx = adxAsia2;
                    activeEmaPeriod = Asia2EmaPeriod;
                    activeAdxPeriod = Asia2AdxPeriod;
                    activeAdxThreshold = Asia2AdxThreshold;
                    activeFlipAdxThreshold = Asia2FlipAdxThreshold;
                    activeAdxMaxThreshold = Asia2AdxMaxThreshold;
                    activeAdxMinSlopePoints = Asia2AdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = Asia2AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = Asia2AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = Asia2Contracts;
                    activeTradeDirection = Asia2TradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = Asia2EmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = Asia2MaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = Asia2StopPaddingPoints;
                    activeExitCrossPoints = Asia2ExitCrossPoints;
                    activeFlipEmaCrossPoints = Asia2FlipEmaCrossPoints;
                    activeMaxStopLossPoints = Asia2MaxStopLossPoints;
                    activeTakeProfitPoints = Asia2TakeProfitPoints;
                    activeMinimumAtrForEntry = Asia2AtrMinimum;
                    activeHvSlPaddingPoints = 0.0;
                    activeHvSlStartTime = TimeSpan.Zero;
                    activeHvSlEndTime = TimeSpan.Zero;
                    activeEntryOffsetPoints = Asia2EntryOffsetPoints;
                    activeEnableFlipBreakEven = Asia2EnableFlipBreakEven;
                    activeFlipBreakEvenTriggerPoints = Asia2FlipBreakEvenTriggerPoints;
                    activeFlipTakeProfitPoints = Asia2FlipTakeProfitPoints;
                    activeTakeProfitPercentTriggerPercent = Asia2TakeProfitPercentTriggerPercent;
                    activeTakeProfitStopMode = Asia2TakeProfitStopMode;
                    activeTakeProfitAtrTrailMultiplier = Asia2TakeProfitAtrTrailMultiplier;
                    activeTakeProfitPercentStopMovePercent = Asia2TakeProfitPercentStopMovePercent;
                    activeRequireMinAdxForFlips = Asia2RequireMinAdxForFlips;
                    activeEnableAdxDdRiskMode = Asia2EnableAdxDdRiskMode;
                    activeAdxDdRiskModeStopLossPoints = Asia2AdxDdRiskModeStopLossPoints;
                    activeAdxDdRiskModeTakeProfitPoints = Asia2AdxDdRiskModeTakeProfitPoints;
                    activeHorizontalExitBars = Asia2HorizontalExitBars;
                    break;

                case SessionSlot.Asia3:
                    activeEma = emaAsia3;
                    activeAdx = adxAsia3;
                    activeEmaPeriod = Asia3EmaPeriod;
                    activeAdxPeriod = Asia3AdxPeriod;
                    activeAdxThreshold = Asia3AdxThreshold;
                    activeFlipAdxThreshold = Asia3FlipAdxThreshold;
                    activeAdxMaxThreshold = Asia3AdxMaxThreshold;
                    activeAdxMinSlopePoints = Asia3AdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = Asia3AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = Asia3AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = Asia3Contracts;
                    activeTradeDirection = Asia3TradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = Asia3EmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = Asia3MaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = Asia3StopPaddingPoints;
                    activeExitCrossPoints = Asia3ExitCrossPoints;
                    activeFlipEmaCrossPoints = Asia3FlipEmaCrossPoints;
                    activeMaxStopLossPoints = Asia3MaxStopLossPoints;
                    activeTakeProfitPoints = Asia3TakeProfitPoints;
                    activeMinimumAtrForEntry = Asia3AtrMinimum;
                    activeHvSlPaddingPoints = 0.0;
                    activeHvSlStartTime = TimeSpan.Zero;
                    activeHvSlEndTime = TimeSpan.Zero;
                    activeEntryOffsetPoints = Asia3EntryOffsetPoints;
                    activeEnableFlipBreakEven = Asia3EnableFlipBreakEven;
                    activeFlipBreakEvenTriggerPoints = Asia3FlipBreakEvenTriggerPoints;
                    activeFlipTakeProfitPoints = Asia3FlipTakeProfitPoints;
                    activeTakeProfitPercentTriggerPercent = Asia3TakeProfitPercentTriggerPercent;
                    activeTakeProfitStopMode = Asia3TakeProfitStopMode;
                    activeTakeProfitAtrTrailMultiplier = Asia3TakeProfitAtrTrailMultiplier;
                    activeTakeProfitPercentStopMovePercent = Asia3TakeProfitPercentStopMovePercent;
                    activeRequireMinAdxForFlips = Asia3RequireMinAdxForFlips;
                    activeEnableAdxDdRiskMode = Asia3EnableAdxDdRiskMode;
                    activeAdxDdRiskModeStopLossPoints = Asia3AdxDdRiskModeStopLossPoints;
                    activeAdxDdRiskModeTakeProfitPoints = Asia3AdxDdRiskModeTakeProfitPoints;
                    activeHorizontalExitBars = Asia3HorizontalExitBars;
                    break;

                case SessionSlot.London:
                    activeEma = emaLondon;
                    activeAdx = adxLondon;
                    activeEmaPeriod = LondonEmaPeriod;
                    activeAdxPeriod = LondonAdxPeriod;
                    activeAdxThreshold = LondonAdxThreshold;
                    activeFlipAdxThreshold = LondonFlipAdxThreshold;
                    activeAdxMaxThreshold = LondonAdxMaxThreshold;
                    activeAdxMinSlopePoints = LondonAdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = LondonAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = LondonAdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = LondonContracts;
                    activeTradeDirection = LondonTradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = LondonEmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = LondonMaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = LondonStopPaddingPoints;
                    activeExitCrossPoints = LondonExitCrossPoints;
                    activeFlipEmaCrossPoints = LondonFlipEmaCrossPoints;
                    activeMaxStopLossPoints = LondonMaxStopLossPoints;
                    activeTakeProfitPoints = LondonTakeProfitPoints;
                    activeMinimumAtrForEntry = LondonAtrMinimum;
                    activeHvSlPaddingPoints = 0.0;
                    activeHvSlStartTime = TimeSpan.Zero;
                    activeHvSlEndTime = TimeSpan.Zero;
                    activeEntryOffsetPoints = LondonEntryOffsetPoints;
                    activeEnableFlipBreakEven = LondonEnableFlipBreakEven;
                    activeFlipBreakEvenTriggerPoints = LondonFlipBreakEvenTriggerPoints;
                    activeFlipTakeProfitPoints = LondonFlipTakeProfitPoints;
                    activeTakeProfitPercentTriggerPercent = LondonTakeProfitPercentTriggerPercent;
                    activeTakeProfitStopMode = LondonTakeProfitStopMode;
                    activeTakeProfitAtrTrailMultiplier = LondonTakeProfitAtrTrailMultiplier;
                    activeTakeProfitPercentStopMovePercent = LondonTakeProfitPercentStopMovePercent;
                    activeRequireMinAdxForFlips = LondonRequireMinAdxForFlips;
                    activeEnableAdxDdRiskMode = LondonEnableAdxDdRiskMode;
                    activeAdxDdRiskModeStopLossPoints = LondonAdxDdRiskModeStopLossPoints;
                    activeAdxDdRiskModeTakeProfitPoints = LondonAdxDdRiskModeTakeProfitPoints;
                    activeHorizontalExitBars = LondonHorizontalExitBars;
                    break;

                case SessionSlot.London2:
                    activeEma = emaLondon2;
                    activeAdx = adxLondon2;
                    activeEmaPeriod = London2EmaPeriod;
                    activeAdxPeriod = London2AdxPeriod;
                    activeAdxThreshold = London2AdxThreshold;
                    activeFlipAdxThreshold = London2FlipAdxThreshold;
                    activeAdxMaxThreshold = London2AdxMaxThreshold;
                    activeAdxMinSlopePoints = London2AdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = London2AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = London2AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = London2Contracts;
                    activeTradeDirection = London2TradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = London2EmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = London2MaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = London2StopPaddingPoints;
                    activeExitCrossPoints = London2ExitCrossPoints;
                    activeFlipEmaCrossPoints = London2FlipEmaCrossPoints;
                    activeMaxStopLossPoints = London2MaxStopLossPoints;
                    activeTakeProfitPoints = London2TakeProfitPoints;
                    activeMinimumAtrForEntry = London2AtrMinimum;
                    activeHvSlPaddingPoints = 0.0;
                    activeHvSlStartTime = TimeSpan.Zero;
                    activeHvSlEndTime = TimeSpan.Zero;
                    activeEntryOffsetPoints = London2EntryOffsetPoints;
                    activeEnableFlipBreakEven = London2EnableFlipBreakEven;
                    activeFlipBreakEvenTriggerPoints = London2FlipBreakEvenTriggerPoints;
                    activeFlipTakeProfitPoints = London2FlipTakeProfitPoints;
                    activeTakeProfitPercentTriggerPercent = London2TakeProfitPercentTriggerPercent;
                    activeTakeProfitStopMode = London2TakeProfitStopMode;
                    activeTakeProfitAtrTrailMultiplier = London2TakeProfitAtrTrailMultiplier;
                    activeTakeProfitPercentStopMovePercent = London2TakeProfitPercentStopMovePercent;
                    activeRequireMinAdxForFlips = London2RequireMinAdxForFlips;
                    activeEnableAdxDdRiskMode = London2EnableAdxDdRiskMode;
                    activeAdxDdRiskModeStopLossPoints = London2AdxDdRiskModeStopLossPoints;
                    activeAdxDdRiskModeTakeProfitPoints = London2AdxDdRiskModeTakeProfitPoints;
                    activeHorizontalExitBars = London2HorizontalExitBars;
                    break;

                case SessionSlot.London3:
                    activeEma = emaLondon3;
                    activeAdx = adxLondon3;
                    activeEmaPeriod = London3EmaPeriod;
                    activeAdxPeriod = London3AdxPeriod;
                    activeAdxThreshold = London3AdxThreshold;
                    activeFlipAdxThreshold = London3FlipAdxThreshold;
                    activeAdxMaxThreshold = London3AdxMaxThreshold;
                    activeAdxMinSlopePoints = London3AdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = London3AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = London3AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = London3Contracts;
                    activeTradeDirection = London3TradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = London3EmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = London3MaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = London3StopPaddingPoints;
                    activeExitCrossPoints = London3ExitCrossPoints;
                    activeFlipEmaCrossPoints = London3FlipEmaCrossPoints;
                    activeMaxStopLossPoints = London3MaxStopLossPoints;
                    activeTakeProfitPoints = London3TakeProfitPoints;
                    activeMinimumAtrForEntry = London3AtrMinimum;
                    activeHvSlPaddingPoints = 0.0;
                    activeHvSlStartTime = TimeSpan.Zero;
                    activeHvSlEndTime = TimeSpan.Zero;
                    activeEntryOffsetPoints = London3EntryOffsetPoints;
                    activeEnableFlipBreakEven = London3EnableFlipBreakEven;
                    activeFlipBreakEvenTriggerPoints = London3FlipBreakEvenTriggerPoints;
                    activeFlipTakeProfitPoints = London3FlipTakeProfitPoints;
                    activeTakeProfitPercentTriggerPercent = London3TakeProfitPercentTriggerPercent;
                    activeTakeProfitStopMode = London3TakeProfitStopMode;
                    activeTakeProfitAtrTrailMultiplier = London3TakeProfitAtrTrailMultiplier;
                    activeTakeProfitPercentStopMovePercent = London3TakeProfitPercentStopMovePercent;
                    activeRequireMinAdxForFlips = London3RequireMinAdxForFlips;
                    activeEnableAdxDdRiskMode = London3EnableAdxDdRiskMode;
                    activeAdxDdRiskModeStopLossPoints = London3AdxDdRiskModeStopLossPoints;
                    activeAdxDdRiskModeTakeProfitPoints = London3AdxDdRiskModeTakeProfitPoints;
                    activeHorizontalExitBars = London3HorizontalExitBars;
                    break;

                case SessionSlot.NewYork:
                    activeEma = emaNewYork;
                    activeAdx = adxNewYork;
                    activeEmaPeriod = NewYorkEmaPeriod;
                    activeAdxPeriod = NewYorkAdxPeriod;
                    activeAdxThreshold = NewYorkAdxThreshold;
                    activeFlipAdxThreshold = NewYorkFlipAdxThreshold;
                    activeAdxMaxThreshold = NewYorkAdxMaxThreshold;
                    activeAdxMinSlopePoints = NewYorkAdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = NewYorkAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = NewYorkAdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = NewYorkContracts;
                    activeTradeDirection = NewYorkTradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = NewYorkEmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = NewYorkMaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = NewYorkStopPaddingPoints;
                    activeExitCrossPoints = NewYorkExitCrossPoints;
                    activeFlipEmaCrossPoints = NewYorkFlipEmaCrossPoints;
                    activeMaxStopLossPoints = NewYorkMaxStopLossPoints;
                    activeTakeProfitPoints = NewYorkTakeProfitPoints;
                    activeMinimumAtrForEntry = NewYorkAtrMinimum;
                    activeHvSlPaddingPoints = NewYorkHvSlPaddingPoints;
                    activeHvSlStartTime = NewYorkHvSlStartTime;
                    activeHvSlEndTime = NewYorkHvSlEndTime;
                    activeEntryOffsetPoints = NewYorkEntryOffsetPoints;
                    activeEnableFlipBreakEven = NewYorkEnableFlipBreakEven;
                    activeFlipBreakEvenTriggerPoints = NewYorkFlipBreakEvenTriggerPoints;
                    activeFlipTakeProfitPoints = NewYorkFlipTakeProfitPoints;
                    activeTakeProfitPercentTriggerPercent = NewYorkTakeProfitPercentTriggerPercent;
                    activeTakeProfitStopMode = NewYorkTakeProfitStopMode;
                    activeTakeProfitAtrTrailMultiplier = NewYorkTakeProfitAtrTrailMultiplier;
                    activeTakeProfitPercentStopMovePercent = NewYorkTakeProfitPercentStopMovePercent;
                    activeRequireMinAdxForFlips = NewYorkRequireMinAdxForFlips;
                    activeEnableAdxDdRiskMode = NewYorkEnableAdxDdRiskMode;
                    activeAdxDdRiskModeStopLossPoints = NewYorkAdxDdRiskModeStopLossPoints;
                    activeAdxDdRiskModeTakeProfitPoints = NewYorkAdxDdRiskModeTakeProfitPoints;
                    activeHorizontalExitBars = NewYorkHorizontalExitBars;
                    break;

                case SessionSlot.NewYork2:
                    activeEma = emaNewYork2;
                    activeAdx = adxNewYork2;
                    activeEmaPeriod = NewYork2EmaPeriod;
                    activeAdxPeriod = NewYork2AdxPeriod;
                    activeAdxThreshold = NewYork2AdxThreshold;
                    activeFlipAdxThreshold = NewYork2FlipAdxThreshold;
                    activeAdxMaxThreshold = NewYork2AdxMaxThreshold;
                    activeAdxMinSlopePoints = NewYork2AdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = NewYork2AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = NewYork2AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = NewYork2Contracts;
                    activeTradeDirection = NewYork2TradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = NewYork2EmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = NewYork2MaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = NewYork2StopPaddingPoints;
                    activeExitCrossPoints = NewYork2ExitCrossPoints;
                    activeFlipEmaCrossPoints = NewYork2FlipEmaCrossPoints;
                    activeMaxStopLossPoints = NewYork2MaxStopLossPoints;
                    activeTakeProfitPoints = NewYork2TakeProfitPoints;
                    activeMinimumAtrForEntry = NewYork2AtrMinimum;
                    activeHvSlPaddingPoints = NewYork2HvSlPaddingPoints;
                    activeHvSlStartTime = NewYork2HvSlStartTime;
                    activeHvSlEndTime = NewYork2HvSlEndTime;
                    activeEntryOffsetPoints = NewYork2EntryOffsetPoints;
                    activeEnableFlipBreakEven = NewYork2EnableFlipBreakEven;
                    activeFlipBreakEvenTriggerPoints = NewYork2FlipBreakEvenTriggerPoints;
                    activeFlipTakeProfitPoints = NewYork2FlipTakeProfitPoints;
                    activeTakeProfitPercentTriggerPercent = NewYork2TakeProfitPercentTriggerPercent;
                    activeTakeProfitStopMode = NewYork2TakeProfitStopMode;
                    activeTakeProfitAtrTrailMultiplier = NewYork2TakeProfitAtrTrailMultiplier;
                    activeTakeProfitPercentStopMovePercent = NewYork2TakeProfitPercentStopMovePercent;
                    activeRequireMinAdxForFlips = NewYork2RequireMinAdxForFlips;
                    activeEnableAdxDdRiskMode = NewYork2EnableAdxDdRiskMode;
                    activeAdxDdRiskModeStopLossPoints = NewYork2AdxDdRiskModeStopLossPoints;
                    activeAdxDdRiskModeTakeProfitPoints = NewYork2AdxDdRiskModeTakeProfitPoints;
                    activeHorizontalExitBars = NewYork2HorizontalExitBars;
                    break;

                case SessionSlot.NewYork3:
                    activeEma = emaNewYork3;
                    activeAdx = adxNewYork3;
                    activeEmaPeriod = NewYork3EmaPeriod;
                    activeAdxPeriod = NewYork3AdxPeriod;
                    activeAdxThreshold = NewYork3AdxThreshold;
                    activeFlipAdxThreshold = NewYork3FlipAdxThreshold;
                    activeAdxMaxThreshold = NewYork3AdxMaxThreshold;
                    activeAdxMinSlopePoints = NewYork3AdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = NewYork3AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = NewYork3AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = NewYork3Contracts;
                    activeTradeDirection = NewYork3TradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = NewYork3EmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = NewYork3MaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = NewYork3StopPaddingPoints;
                    activeExitCrossPoints = NewYork3ExitCrossPoints;
                    activeFlipEmaCrossPoints = NewYork3FlipEmaCrossPoints;
                    activeMaxStopLossPoints = NewYork3MaxStopLossPoints;
                    activeTakeProfitPoints = NewYork3TakeProfitPoints;
                    activeMinimumAtrForEntry = NewYork3AtrMinimum;
                    activeHvSlPaddingPoints = NewYork3HvSlPaddingPoints;
                    activeHvSlStartTime = NewYork3HvSlStartTime;
                    activeHvSlEndTime = NewYork3HvSlEndTime;
                    activeEntryOffsetPoints = NewYork3EntryOffsetPoints;
                    activeEnableFlipBreakEven = NewYork3EnableFlipBreakEven;
                    activeFlipBreakEvenTriggerPoints = NewYork3FlipBreakEvenTriggerPoints;
                    activeFlipTakeProfitPoints = NewYork3FlipTakeProfitPoints;
                    activeTakeProfitPercentTriggerPercent = NewYork3TakeProfitPercentTriggerPercent;
                    activeTakeProfitStopMode = NewYork3TakeProfitStopMode;
                    activeTakeProfitAtrTrailMultiplier = NewYork3TakeProfitAtrTrailMultiplier;
                    activeTakeProfitPercentStopMovePercent = NewYork3TakeProfitPercentStopMovePercent;
                    activeRequireMinAdxForFlips = NewYork3RequireMinAdxForFlips;
                    activeEnableAdxDdRiskMode = NewYork3EnableAdxDdRiskMode;
                    activeAdxDdRiskModeStopLossPoints = NewYork3AdxDdRiskModeStopLossPoints;
                    activeAdxDdRiskModeTakeProfitPoints = NewYork3AdxDdRiskModeTakeProfitPoints;
                    activeHorizontalExitBars = NewYork3HorizontalExitBars;
                    break;

                default:
                    activeEma = null;
                    activeAdx = null;
                    activeEmaPeriod = 0;
                    activeAdxPeriod = 0;
                    activeAdxThreshold = 0.0;
                    activeFlipAdxThreshold = 0.0;
                    activeAdxMaxThreshold = 0.0;
                    activeAdxMinSlopePoints = 0.0;
                    activeAdxPeakDrawdownExitUnits = 0.0;
                    activeAdxAbsoluteExitLevel = 0.0;
                    activeContracts = 0;
                    activeTradeDirection = SessionTradeDirection.Both;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = 0.0;
                    activeMaxEntryDistanceFromEmaPoints = 0.0;
                    activeStopPaddingPoints = 0.0;
                    activeExitCrossPoints = 0.0;
                    activeFlipEmaCrossPoints = 0.0;
                    activeMaxStopLossPoints = 0.0;
                    activeTakeProfitPoints = 0.0;
                    activeMinimumAtrForEntry = 0.0;
                    activeHvSlPaddingPoints = 0.0;
                    activeHvSlStartTime = TimeSpan.Zero;
                    activeHvSlEndTime = TimeSpan.Zero;
                    activeEntryOffsetPoints = 0.0;
                    activeEnableFlipBreakEven = false;
                    activeFlipBreakEvenTriggerPoints = 0.0;
                    activeFlipTakeProfitPoints = 0.0;
                    activeTakeProfitPercentTriggerPercent = 0.0;
                    activeTakeProfitStopMode = TakeProfitStopMode.PercentMove;
                    activeTakeProfitAtrTrailMultiplier = 0.0;
                    activeTakeProfitPercentStopMovePercent = 0.0;
                    activeRequireMinAdxForFlips = false;
                    activeEnableAdxDdRiskMode = false;
                    activeAdxDdRiskModeStopLossPoints = 0.0;
                    activeAdxDdRiskModeTakeProfitPoints = 0.0;
                    activeHorizontalExitBars = 0;
                    break;
            }
        }

        private void UpdateEmaPlotVisibility()
        {
            if (!ShowEmaOnChart)
            {
                SetEmaVisible(emaAsia, false);
                SetEmaVisible(emaAsia2, false);
                SetEmaVisible(emaAsia3, false);
                SetEmaVisible(emaLondon, false);
                SetEmaVisible(emaLondon2, false);
                SetEmaVisible(emaLondon3, false);
                SetEmaVisible(emaNewYork, false);
                SetEmaVisible(emaNewYork2, false);
                SetEmaVisible(emaNewYork3, false);
                return;
            }

            SetEmaVisible(emaAsia, ShouldShowEmaInstance(emaAsia));
            SetEmaVisible(emaAsia2, ShouldShowEmaInstance(emaAsia2));
            SetEmaVisible(emaAsia3, ShouldShowEmaInstance(emaAsia3));
            SetEmaVisible(emaLondon, ShouldShowEmaInstance(emaLondon));
            SetEmaVisible(emaLondon2, ShouldShowEmaInstance(emaLondon2));
            SetEmaVisible(emaLondon3, ShouldShowEmaInstance(emaLondon3));
            SetEmaVisible(emaNewYork, ShouldShowEmaInstance(emaNewYork));
            SetEmaVisible(emaNewYork2, ShouldShowEmaInstance(emaNewYork2));
            SetEmaVisible(emaNewYork3, ShouldShowEmaInstance(emaNewYork3));
        }

        private void UpdateAdxPlotVisibility()
        {
            SetAdxVisible(adxAsia, ShowAdxOnChart);
            SetAdxVisible(adxAsia2, ShowAdxOnChart);
            SetAdxVisible(adxAsia3, ShowAdxOnChart);
            SetAdxVisible(adxLondon, ShowAdxOnChart);
            SetAdxVisible(adxLondon2, ShowAdxOnChart);
            SetAdxVisible(adxLondon3, ShowAdxOnChart);
            SetAdxVisible(adxNewYork, ShowAdxOnChart);
            SetAdxVisible(adxNewYork2, ShowAdxOnChart);
            SetAdxVisible(adxNewYork3, ShowAdxOnChart);
        }

        private void UpdateAtrPlotVisibility()
        {
            if (atrVisual == null)
                return;

            atrVisual.ShowAtrLine = ShowAtrOnChart;
            atrVisual.ShowThresholdLine = ShowAtrThresholdLines;
            atrVisual.Threshold = activeMinimumAtrForEntry;
        }

        private bool ShouldShowEmaInstance(EMA ema)
        {
            if (ema == null)
                return false;

            return (activeSession == SessionSlot.Asia && ReferenceEquals(ema, emaAsia))
                || (activeSession == SessionSlot.Asia2 && ReferenceEquals(ema, emaAsia2))
                || (activeSession == SessionSlot.Asia3 && ReferenceEquals(ema, emaAsia3))
                || (activeSession == SessionSlot.London && ReferenceEquals(ema, emaLondon))
                || (activeSession == SessionSlot.London2 && ReferenceEquals(ema, emaLondon2))
                || (activeSession == SessionSlot.London3 && ReferenceEquals(ema, emaLondon3))
                || (activeSession == SessionSlot.NewYork && ReferenceEquals(ema, emaNewYork))
                || (activeSession == SessionSlot.NewYork2 && ReferenceEquals(ema, emaNewYork2))
                || (activeSession == SessionSlot.NewYork3 && ReferenceEquals(ema, emaNewYork3));
        }

        private void SetEmaVisible(EMA ema, bool visible)
        {
            if (ema == null || ema.Plots == null || ema.Plots.Length == 0)
                return;

            ema.Plots[0].Brush = visible ? Brushes.Gold : Brushes.Transparent;
        }

        private void SetAdxVisible(DM adx, bool showAdx)
        {
            if (adx == null || adx.Plots == null || adx.Plots.Length == 0)
                return;

            Brush adxBrush = showAdx ? Brushes.DodgerBlue : Brushes.Transparent;

            adx.Plots[0].Brush = adxBrush;
            if (adx.Plots.Length > 1)
                adx.Plots[1].Brush = Brushes.Transparent;
            if (adx.Plots.Length > 2)
                adx.Plots[2].Brush = Brushes.Transparent;
        }

        private void SetAtrVisible(ATR atr, bool showAtr)
        {
            if (atr == null || atr.Plots == null || atr.Plots.Length == 0)
                return;

            atr.Plots[0].Brush = showAtr ? Brushes.DeepSkyBlue : Brushes.Transparent;
        }

        private void UpdateAdxReferenceLines(DM adx, double minThreshold, double maxThreshold)
        {
            if (adx == null || adx.Lines == null || adx.Lines.Length == 0)
                return;

            bool showLines = ShowAdxThresholdLines && ShowAdxOnChart;

            // ADX line 0 is the primary threshold line (min).
            bool showMin = showLines && minThreshold > 0.0;
            adx.Lines[0].Value = showMin ? minThreshold : double.NaN;
            adx.Lines[0].Brush = showMin ? Brushes.LimeGreen : Brushes.Transparent;

            // If a second line exists, use it for max threshold.
            if (adx.Lines.Length > 1)
            {
                bool showMax = showLines && maxThreshold > 0.0;
                adx.Lines[1].Value = showMax ? maxThreshold : double.NaN;
                adx.Lines[1].Brush = showMax ? Brushes.OrangeRed : Brushes.Transparent;
            }

            // Draw explicit guide lines on the ADX panel so min/max are visible even when
            // the built-in ADX indicator exposes only one threshold line slot.
            string adxTagSuffix = adx.GetHashCode().ToString(CultureInfo.InvariantCulture);
            bool drawMin = showLines && minThreshold > 0.0;
            bool drawMax = showLines && maxThreshold > 0.0;

            Draw.HorizontalLine(
                adx,
                "DUO_ADX_Min_" + adxTagSuffix,
                drawMin ? minThreshold : 0.0,
                drawMin ? Brushes.LimeGreen : Brushes.Transparent,
                DashStyleHelper.Solid,
                2);

            Draw.HorizontalLine(
                adx,
                "DUO_ADX_Max_" + adxTagSuffix,
                drawMax ? maxThreshold : 0.0,
                drawMax ? Brushes.OrangeRed : Brushes.Transparent,
                DashStyleHelper.Dash,
                2);
        }

        private int GetMaxConfiguredEmaPeriod()
        {
            return new[]
            {
                AsiaEmaPeriod,
                Asia2EmaPeriod,
                Asia3EmaPeriod,
                LondonEmaPeriod,
                London2EmaPeriod,
                London3EmaPeriod,
                NewYorkEmaPeriod,
                NewYork2EmaPeriod,
                NewYork3EmaPeriod
            }.Max();
        }

        private bool IsFamilyActive(SessionFamily family, DateTime time)
        {
            if (family == SessionFamily.None)
                return false;

            foreach (SessionSlot slot in ConfigurableSessionSlots)
            {
                if (GetSessionFamily(slot) == family && TimeInSession(slot, time))
                    return true;
            }

            return false;
        }

        private void ProcessSessionTransitions(SessionSlot slot)
        {
            bool inNow = TimeInSession(slot, Time[0]);
            bool inPrev = CurrentBar > 0 ? TimeInSession(slot, Time[1]) : inNow;
            SessionFamily family = GetSessionFamily(slot);
            bool familyActivePrev = CurrentBar > 0 && IsFamilyActive(family, Time[1]);

            if (inNow && !inPrev)
            {
                SetSessionClosed(slot, false);
                LogDebug(string.Format("{0} session start.", FormatSessionLabel(slot)));
                if (!familyActivePrev)
                {
                    SetTradesThisSession(slot, 0);
                    string resetLabel = family == SessionFamily.None
                        ? FormatSessionLabel(slot)
                        : GetFamilyAnchorSlotLabel(family);
                    LogDebug(string.Format("{0} trade counter reset.", resetLabel));
                }
                if (activeSession == slot)
                    LogSessionActivation("start");
            }

            if (inPrev && !inNow && !GetSessionClosed(slot))
            {
                if (CloseAtSessionEnd)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(BuildExitSignalName("SessionEnd"));
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort(BuildExitSignalName("SessionEnd"));
                }

                CancelWorkingEntryOrders();
                SetSessionClosed(slot, true);
                string sessionEndAction = CloseAtSessionEnd ? "flatten/cancel" : "cancel-only";
                LogDebug(string.Format("{0} session end: {1}. closeAtSessionEnd={2}", FormatSessionLabel(slot), sessionEndAction, CloseAtSessionEnd));
            }
        }

        private string GetFamilyAnchorSlotLabel(SessionFamily family)
        {
            switch (family)
            {
                case SessionFamily.Asia:
                    return "Asia";
                case SessionFamily.London:
                    return "London";
                case SessionFamily.NewYork:
                    return "New York";
                default:
                    return "None";
            }
        }

        private bool IsForceCloseTimeReached(DateTime barTime)
        {
            DateTime forceCloseDateTime;
            if (!TryGetForceCloseDateTime(barTime, out forceCloseDateTime))
                return false;

            return barTime >= forceCloseDateTime;
        }

        private bool TryGetForceCloseDateTime(DateTime referenceTime, out DateTime forceCloseDateTime)
        {
            forceCloseDateTime = Core.Globals.MinDate;

            TimeSpan forceCloseTimeOfDay;
            if (!TryParseConfiguredForceCloseTime(out forceCloseTimeOfDay))
                return false;

            DateTime anchorStart;
            if (!TryGetTradingDayAnchorStart(referenceTime, out anchorStart))
                return false;

            forceCloseDateTime = forceCloseTimeOfDay <= anchorStart.TimeOfDay
                ? anchorStart.Date.AddDays(1) + forceCloseTimeOfDay
                : anchorStart.Date + forceCloseTimeOfDay;
            return true;
        }

        private bool IsLondon3FlatByTimeReached(DateTime barTime)
        {
            if (Position.MarketPosition == MarketPosition.Flat)
                return false;

            if (lockedTradeSession != SessionSlot.London3)
                return false;

            TimeSpan flatByTime;
            if (!TryParseLondon3FlatByTime(out flatByTime))
                return false;

            return barTime.TimeOfDay >= flatByTime;
        }

        private bool CheckAndHandleStrategyAccountDesync(string reason)
        {
            if (State != State.Realtime)
                return false;

            if (strategyAccountDesyncDetected)
                return true;

            MarketPosition accountMarketPosition;
            int accountQuantity;
            if (!TryGetAccountPositionSnapshot(out accountMarketPosition, out accountQuantity))
                return false;

            MarketPosition strategyMarketPosition = Position.MarketPosition;
            int strategyQuantity = Math.Abs(Position.Quantity);

            bool marketPositionMatches = strategyMarketPosition == accountMarketPosition;
            bool quantityMatches = strategyQuantity == accountQuantity;
            if (marketPositionMatches && quantityMatches)
                return false;

            strategyAccountDesyncDetected = true;
            CancelWorkingEntryOrders();

            LogMessage(string.Format(
                "STRATEGY/ACCOUNT DESYNC DETECTED | reason={0} instrument={1} strategyPos={2} strategyQty={3} accountPos={4} accountQty={5} | Trading is now blocked. Manually disable and re-enable the strategy after verifying the account position.",
                reason,
                Instrument != null ? Instrument.FullName : "Unknown",
                strategyMarketPosition,
                strategyQuantity,
                accountMarketPosition,
                accountQuantity), true);
            return true;
        }

        private bool TryGetAccountPositionSnapshot(out MarketPosition accountMarketPosition, out int accountQuantity)
        {
            accountMarketPosition = MarketPosition.Flat;
            accountQuantity = 0;

            if (Account == null || Instrument == null)
                return false;

            try
            {
                foreach (var pos in Account.Positions)
                {
                    if (pos == null || pos.Instrument == null)
                        continue;

                    if (!string.Equals(pos.Instrument.FullName, Instrument.FullName, StringComparison.Ordinal))
                        continue;

                    accountMarketPosition = pos.MarketPosition;
                    accountQuantity = Math.Abs(pos.Quantity);
                    return true;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseConfiguredForceCloseTime(out TimeSpan forceCloseTime)
        {
            forceCloseTime = TimeSpan.Zero;

            string configured = ForceCloseTime;
            if (string.IsNullOrWhiteSpace(configured))
                return false;

            return TimeSpan.TryParse(configured, CultureInfo.InvariantCulture, out forceCloseTime)
                || TimeSpan.TryParse(configured, out forceCloseTime);
        }

        private bool TryParseLondon3FlatByTime(out TimeSpan flatByTime)
        {
            flatByTime = TimeSpan.Zero;

            string configured = London3FlatByTime;
            if (string.IsNullOrWhiteSpace(configured))
                return false;

            return TimeSpan.TryParse(configured, CultureInfo.InvariantCulture, out flatByTime)
                || TimeSpan.TryParse(configured, out flatByTime);
        }

        private bool TryGetTradingDayAnchorSlot(out SessionSlot slot)
        {
            foreach (SessionSlot configuredSlot in ConfigurableSessionSlots)
            {
                if (IsSessionConfigured(configuredSlot))
                {
                    slot = configuredSlot;
                    return true;
                }
            }

            slot = SessionSlot.None;
            return false;
        }

        private bool TryGetTradingDayAnchorStart(DateTime referenceTime, out DateTime anchorStart)
        {
            anchorStart = Core.Globals.MinDate;

            SessionSlot anchorSlot;
            if (!TryGetTradingDayAnchorSlot(out anchorSlot))
                return false;

            TimeSpan currentStart;
            TimeSpan currentEnd;
            if (!TryGetSessionWindow(anchorSlot, referenceTime, out currentStart, out currentEnd))
                return false;

            if (referenceTime.TimeOfDay >= currentStart)
            {
                anchorStart = referenceTime.Date + currentStart;
                return true;
            }

            TimeSpan previousStart;
            TimeSpan previousEnd;
            DateTime previousReference = referenceTime.AddDays(-1);
            if (!TryGetSessionWindow(anchorSlot, previousReference, out previousStart, out previousEnd))
                return false;

            anchorStart = previousReference.Date + previousStart;
            return true;
        }

        private int GetTradesThisSession(SessionSlot slot)
        {
            switch (GetSessionFamily(slot))
            {
                case SessionFamily.Asia:
                    return asiaTradesThisSession;
                case SessionFamily.London:
                    return londonTradesThisSession;
                case SessionFamily.NewYork:
                    return newYorkTradesThisSession;
                default:
                    return 0;
            }
        }

        private void SetTradesThisSession(SessionSlot slot, int value)
        {
            int normalized = Math.Max(0, value);
            switch (GetSessionFamily(slot))
            {
                case SessionFamily.Asia:
                    asiaTradesThisSession = normalized;
                    break;
                case SessionFamily.London:
                    londonTradesThisSession = normalized;
                    break;
                case SessionFamily.NewYork:
                    newYorkTradesThisSession = normalized;
                    break;
            }
        }

        private void CancelWorkingEntryOrders()
        {
            CancelOrderIfActive(longEntryOrder, "CancelWorkingEntries");
            CancelOrderIfActive(shortEntryOrder, "CancelWorkingEntries");
        }

        private void CancelOrderIfActive(Order order, string reason)
        {
            if (!IsOrderActive(order))
                return;

            LogDebug(string.Format("CancelOrder | name={0} reason={1} state={2}", order.Name, reason, order.OrderState));
            CancelOrder(order);
            SendWebhook("cancel");
        }

        private bool IsOrderActive(Order order)
        {
            return order != null &&
                (order.OrderState == OrderState.Working ||
                 order.OrderState == OrderState.Submitted ||
                 order.OrderState == OrderState.Accepted ||
                 order.OrderState == OrderState.ChangePending ||
                 order.OrderState == OrderState.PartFilled);
        }

        private void TrackProtectiveAndExitOrders(Order order, OrderState orderState)
        {
            string orderName = order != null ? (order.Name ?? string.Empty) : string.Empty;

            if (IsProtectiveOrderName(orderName))
            {
                bool isActive = IsOrderActive(order);

                if (IsStopLossOrderName(orderName))
                {
                    if (isActive)
                        activeStopLossOrder = order;
                    else if (MatchesTrackedOrder(activeStopLossOrder, order))
                        activeStopLossOrder = null;
                }
                else if (IsProfitTargetOrderName(orderName))
                {
                    if (isActive)
                        activeProfitTargetOrder = order;
                    else if (MatchesTrackedOrder(activeProfitTargetOrder, order))
                        activeProfitTargetOrder = null;
                }

                if (orderState == OrderState.Filled)
                    ArmProtectionAuditGracePeriod("protective-filled", 2000);
            }

            if (IsStrategyExitOrderName(orderName))
            {
                if (IsOrderActive(order))
                {
                    activeExitOrder = order;
                    ArmProtectionAuditGracePeriod("exit-active", 2000);
                }
                else
                {
                    if (MatchesTrackedOrder(activeExitOrder, order))
                        activeExitOrder = null;

                    ArmProtectionAuditGracePeriod("exit-terminal", 2000);
                }
            }
        }

        private bool AuditPositionProtection(string reason)
        {
            if (CheckAndHandleStrategyAccountDesync("protection-" + reason))
                return false;

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearProtectionAuditState();
                return false;
            }

            ReconcileTrackedProtectiveOrders();

            if (DateTime.UtcNow < protectionAuditGraceUntilUtc)
                return false;

            if (IsOrderActive(activeExitOrder))
                return false;

            double takeProfitPoints = GetActivePositionTakeProfitPoints();
            bool requiresTarget = takeProfitPoints > 0.0;
            bool hasStop = IsOrderActive(activeStopLossOrder);
            bool hasTarget = !requiresTarget || IsOrderActive(activeProfitTargetOrder);

            if (hasStop && hasTarget)
                return false;

            double restoredStopPrice;
            double restoredTakeProfitPoints;
            if (TryRestorePositionProtection(out restoredStopPrice, out restoredTakeProfitPoints))
            {
                ArmProtectionAuditGracePeriod("restore-" + reason);
                LogDebug(string.Format(
                    "Protection guard | action=restore reason={0} side={1} hasStop={2} hasTarget={3} stop={4:0.00} tpPts={5:0.00}",
                    reason,
                    Position.MarketPosition,
                    hasStop,
                    hasTarget,
                    restoredStopPrice,
                    restoredTakeProfitPoints));
                return true;
            }

            string entrySignal = Position.MarketPosition == MarketPosition.Long
                ? GetOpenLongEntrySignal()
                : GetOpenShortEntrySignal();

            LogDebug(string.Format(
                "Protection guard | action=flatten reason={0} side={1} hasStop={2} hasTarget={3}",
                reason,
                Position.MarketPosition,
                hasStop,
                hasTarget));

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(BuildExitSignalName("ProtectionGuard"), entrySignal);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(BuildExitSignalName("ProtectionGuard"), entrySignal);

            ArmProtectionAuditGracePeriod("flatten-" + reason, 2000);
            return true;
        }

        private bool TryRestorePositionProtection(out double stopPrice, out double takeProfitPoints)
        {
            stopPrice = 0.0;
            takeProfitPoints = 0.0;

            if (Position.MarketPosition == MarketPosition.Flat)
                return false;

            string entrySignal = Position.MarketPosition == MarketPosition.Long
                ? GetOpenLongEntrySignal()
                : GetOpenShortEntrySignal();
            if (string.IsNullOrEmpty(entrySignal))
                return false;

            double closePrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            double desiredStopPrice = currentStopPrice > 0.0 ? currentStopPrice : initialStopPrice;
            desiredStopPrice = Instrument.MasterInstrument.RoundToTickSize(desiredStopPrice);

            if (desiredStopPrice <= 0.0 || !IsManagedStopPriceValid(desiredStopPrice, closePrice))
                return false;

            SetStopLoss(entrySignal, CalculationMode.Price, desiredStopPrice, false);
            currentStopPrice = desiredStopPrice;
            UpdateTradeLineStopPrice(desiredStopPrice);
            activeStopLossOrder = null;
            stopPrice = desiredStopPrice;

            takeProfitPoints = GetActivePositionTakeProfitPoints();
            if (takeProfitPoints > 0.0)
            {
                int targetTicks = PriceToTicks(takeProfitPoints);
                if (targetTicks < 1)
                    targetTicks = 1;
                SetProfitTarget(entrySignal, CalculationMode.Ticks, targetTicks);
                activeProfitTargetOrder = null;
            }

            return true;
        }

        private void ReconcileTrackedProtectiveOrders()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearProtectionAuditState();
                return;
            }

            if (Account == null)
                return;

            Order foundStop = null;
            Order foundTarget = null;
            Order foundExit = null;
            string entrySignal = Position.MarketPosition == MarketPosition.Long
                ? GetOpenLongEntrySignal()
                : GetOpenShortEntrySignal();

            try
            {
                foreach (Order accountOrder in Account.Orders)
                {
                    if (!IsOrderActive(accountOrder) || !IsOrderForThisInstrument(accountOrder))
                        continue;

                    string orderName = accountOrder.Name ?? string.Empty;
                    string fromEntrySignal = accountOrder.FromEntrySignal ?? string.Empty;
                    bool signalMatches = fromEntrySignal.Length == 0
                        || entrySignal.Length == 0
                        || string.Equals(fromEntrySignal, entrySignal, StringComparison.Ordinal);

                    if (!signalMatches)
                        continue;

                    if (IsStopLossOrderName(orderName))
                        foundStop = accountOrder;
                    else if (IsProfitTargetOrderName(orderName))
                        foundTarget = accountOrder;
                    else if (IsStrategyExitOrderName(orderName))
                        foundExit = accountOrder;
                }
            }
            catch
            {
                return;
            }

            activeStopLossOrder = foundStop;
            activeProfitTargetOrder = foundTarget;
            activeExitOrder = foundExit;
        }

        private bool IsStopLossOrderName(string orderName)
        {
            return !string.IsNullOrEmpty(orderName)
                && orderName.Equals("Stop loss", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsProfitTargetOrderName(string orderName)
        {
            return !string.IsNullOrEmpty(orderName)
                && orderName.Equals("Profit target", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsProtectiveOrderName(string orderName)
        {
            return IsStopLossOrderName(orderName) || IsProfitTargetOrderName(orderName);
        }

        private bool IsStrategyExitOrderName(string orderName)
        {
            return !string.IsNullOrEmpty(orderName)
                && orderName.StartsWith("DUO", StringComparison.OrdinalIgnoreCase)
                && !IsEntryOrderName(orderName);
        }

        private bool MatchesTrackedOrder(Order trackedOrder, Order order)
        {
            if (trackedOrder == null || order == null)
                return false;

            string trackedId = trackedOrder.OrderId ?? string.Empty;
            string orderId = order.OrderId ?? string.Empty;
            return string.Equals(trackedId, orderId, StringComparison.Ordinal);
        }

        private bool IsOrderForThisInstrument(Order order)
        {
            return order != null
                && order.Instrument != null
                && Instrument != null
                && string.Equals(order.Instrument.FullName, Instrument.FullName, StringComparison.Ordinal);
        }

        private void ArmProtectionAuditGracePeriod(string reason, int milliseconds = 3000)
        {
            DateTime candidateUtc = DateTime.UtcNow.AddMilliseconds(milliseconds);
            if (candidateUtc > protectionAuditGraceUntilUtc)
                protectionAuditGraceUntilUtc = candidateUtc;

            if (DebugLogging)
                LogDebug(string.Format("Protection grace armed | reason={0} ms={1}", reason, milliseconds));
        }

        private void ClearProtectionAuditState()
        {
            activeStopLossOrder = null;
            activeProfitTargetOrder = null;
            activeExitOrder = null;
            protectionAuditGraceUntilUtc = Core.Globals.MinDate;
        }

        private void ReconcileTrackedEntryOrders()
        {
            ReconcileTrackedEntryOrder(ref longEntryOrder, LongEntrySignal, ref missingLongEntryOrderBars);
            ReconcileTrackedEntryOrder(ref shortEntryOrder, ShortEntrySignal, ref missingShortEntryOrderBars);
        }

        private void ReconcileTrackedEntryOrder(ref Order trackedOrder, string expectedName, ref int missingBars)
        {
            if (!IsOrderActive(trackedOrder))
            {
                missingBars = 0;
                return;
            }

            if (Account == null)
            {
                missingBars = 0;
                return;
            }

            string trackedId = trackedOrder.OrderId ?? string.Empty;
            bool foundActive = false;

            try
            {
                foreach (Order accountOrder in Account.Orders)
                {
                    if (accountOrder == null)
                        continue;

                    string accountOrderId = accountOrder.OrderId ?? string.Empty;
                    if (trackedId.Length > 0 && string.Equals(accountOrderId, trackedId, StringComparison.Ordinal))
                    {
                        foundActive = IsOrderActive(accountOrder);
                        break;
                    }
                }
            }
            catch
            {
                missingBars = 0;
                return;
            }

            if (foundActive)
            {
                missingBars = 0;
                return;
            }

            missingBars++;

            if (missingBars >= 2)
            {
                if (DebugLogging)
                {
                    LogDebug(string.Format(
                        "Clearing stale tracked order | expected={0} tracked={1} missingBars={2}",
                        expectedName,
                        FormatOrderRef(trackedOrder),
                        missingBars));
                }

                trackedOrder = null;
                missingBars = 0;
            }
        }

        private string FormatOrderRef(Order order)
        {
            if (order == null)
                return "null";

            string name = order.Name ?? string.Empty;
            string id = order.OrderId ?? string.Empty;
            return string.Format("{0}:{1}:{2}", name, id, order.OrderState);
        }

        private double BuildLongEntryStopPrice(double entryPrice, double emaValue, DateTime time)
        {
            double raw = emaValue - GetActiveLongStopPaddingPoints(time);

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);
            return rounded;
        }

        private double BuildShortEntryStopPrice(double entryPrice, double emaValue, DateTime time)
        {
            double raw = emaValue + GetActiveShortStopPaddingPoints(time);

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }

        private double BuildFlipShortStopPrice(double entryPrice, double emaValue, DateTime time)
        {
            double raw = emaValue + GetActiveShortStopPaddingPoints(time);
            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }

        private double BuildFlipLongStopPrice(double entryPrice, double emaValue, DateTime time)
        {
            double raw = emaValue - GetActiveLongStopPaddingPoints(time);
            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);
            return rounded;
        }

        private double GetEntryPriceForDirection(double signalClose, bool isLong, double offsetPoints)
        {
            if (offsetPoints <= 0.0)
                return Instrument.MasterInstrument.RoundToTickSize(signalClose);

            double raw = isLong
                ? signalClose - offsetPoints
                : signalClose + offsetPoints;
            return Instrument.MasterInstrument.RoundToTickSize(raw);
        }

        private void SubmitLongEntryOrder(int quantity, double entryPrice, bool isMarketEntry, string signalName)
        {
            if (isMarketEntry)
                EnterLong(quantity, signalName);
            else
                EnterLongLimit(0, true, quantity, entryPrice, signalName);
        }

        private void SubmitShortEntryOrder(int quantity, double entryPrice, bool isMarketEntry, string signalName)
        {
            if (isMarketEntry)
                EnterShort(quantity, signalName);
            else
                EnterShortLimit(0, true, quantity, entryPrice, signalName);
        }

        private double GetActiveLongStopPaddingPoints(DateTime time)
        {
            return IsHighVolatilitySlWindow(time) ? activeHvSlPaddingPoints : activeStopPaddingPoints;
        }

        private double GetActiveShortStopPaddingPoints(DateTime time)
        {
            return IsHighVolatilitySlWindow(time) ? activeHvSlPaddingPoints : activeStopPaddingPoints;
        }

        private bool IsHighVolatilitySlWindow(DateTime time)
        {
            if (activeHvSlPaddingPoints <= 0.0)
                return false;

            if (activeHvSlStartTime == activeHvSlEndTime)
                return false;

            return IsTimeInRange(time.TimeOfDay, activeHvSlStartTime, activeHvSlEndTime);
        }

        private double GetAdxSlopePoints()
        {
            if (activeAdx == null || CurrentBar < 1)
                return 0.0;

            return activeAdx[0] - activeAdx[1];
        }

        private double GetEmaSlopePoints()
        {
            if (activeEma == null || CurrentBar < 1)
                return 0.0;

            return activeEma[0] - activeEma[1];
        }

        private double GetEmaSlopePointsPerBar()
        {
            return GetEmaSlopePoints();
        }

        private bool EmaSlopePassesLong()
        {
            if (activeEmaMinSlopePointsPerBar <= 0.0)
                return true;

            return GetEmaSlopePointsPerBar() >= activeEmaMinSlopePointsPerBar;
        }

        private bool EmaSlopePassesShort()
        {
            if (activeEmaMinSlopePointsPerBar <= 0.0)
                return true;

            return GetEmaSlopePointsPerBar() <= -activeEmaMinSlopePointsPerBar;
        }

        private void UpdateAdxPeakTracker(double adxValue)
        {
            MarketPosition currentPos = Position.MarketPosition;
            if (currentPos == MarketPosition.Flat)
            {
                trackedAdxPeakPosition = MarketPosition.Flat;
                currentTradePeakAdx = 0.0;
                return;
            }

            if (trackedAdxPeakPosition != currentPos)
            {
                trackedAdxPeakPosition = currentPos;
                currentTradePeakAdx = adxValue;
                return;
            }

            if (adxValue > currentTradePeakAdx)
                currentTradePeakAdx = adxValue;
        }

        private bool ShouldExitOnAdxDrawdown(double adxValue, out double drawdown)
        {
            drawdown = 0.0;
            if (activeAdxPeakDrawdownExitUnits <= 0.0)
                return false;

            if (Position.MarketPosition == MarketPosition.Flat)
                return false;

            if (trackedAdxPeakPosition != Position.MarketPosition)
                return false;

            drawdown = currentTradePeakAdx - adxValue;
            return drawdown >= activeAdxPeakDrawdownExitUnits;
        }


        private double GetBodyPercentAboveEma(double open, double close, double emaValue)
        {
            double bodyTop = Math.Max(open, close);
            double bodyBottom = Math.Min(open, close);
            double body = bodyTop - bodyBottom;
            if (body <= 0)
                return 0;

            double above;
            if (emaValue <= bodyBottom)
                above = body;
            else if (emaValue >= bodyTop)
                above = 0;
            else
                above = bodyTop - emaValue;

            return (above / body) * 100.0;
        }

        private double GetBodyPercentBelowEma(double open, double close, double emaValue)
        {
            double bodyTop = Math.Max(open, close);
            double bodyBottom = Math.Min(open, close);
            double body = bodyTop - bodyBottom;
            if (body <= 0)
                return 0;

            double below;
            if (emaValue >= bodyTop)
                below = body;
            else if (emaValue <= bodyBottom)
                below = 0;
            else
                below = emaValue - bodyBottom;

            return (below / body) * 100.0;
        }

        private bool TimeInSession(SessionSlot slot, DateTime time)
        {
            TimeSpan start;
            TimeSpan end;
            if (!TryGetSessionWindow(slot, time, out start, out end))
                return false;

            return IsTimeInRange(time.TimeOfDay, start, end);
        }

        private bool TryGetSessionWindow(SessionSlot slot, out TimeSpan start, out TimeSpan end)
        {
            return TryGetSessionWindow(slot, Time[0], out start, out end);
        }

        private bool TryGetSessionWindow(SessionSlot slot, DateTime referenceTime, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            switch (slot)
            {
                case SessionSlot.Asia:
                    if (!UseAsiaSession || AsiaSessionStart == AsiaSessionEnd)
                        return false;
                    start = AsiaSessionStart;
                    end = AsiaSessionEnd;
                    return true;

                case SessionSlot.Asia2:
                    if (!UseAsia2Session || Asia2SessionStart == Asia2SessionEnd)
                        return false;
                    start = Asia2SessionStart;
                    end = Asia2SessionEnd;
                    return true;

                case SessionSlot.Asia3:
                    if (!UseAsia3Session || Asia3SessionStart == Asia3SessionEnd)
                        return false;
                    start = Asia3SessionStart;
                    end = Asia3SessionEnd;
                    return true;

                case SessionSlot.London:
                    if (!UseLondonSession || LondonSessionStart == LondonSessionEnd)
                        return false;
                    start = LondonSessionStart;
                    end = LondonSessionEnd;
                    if (ShouldAutoShiftSession(slot))
                    {
                        TimeSpan shift = GetLondonSessionShiftForDate(referenceTime.Date);
                        start = ShiftTime(start, shift);
                        end = ShiftTime(end, shift);
                    }
                    return true;

                case SessionSlot.London2:
                    if (!UseLondon2Session || London2SessionStart == London2SessionEnd)
                        return false;
                    start = London2SessionStart;
                    end = London2SessionEnd;
                    if (ShouldAutoShiftSession(slot))
                    {
                        TimeSpan shift = GetLondonSessionShiftForDate(referenceTime.Date);
                        start = ShiftTime(start, shift);
                        end = ShiftTime(end, shift);
                    }
                    return true;

                case SessionSlot.London3:
                    if (!UseLondon3Session || London3SessionStart == London3SessionEnd)
                        return false;
                    start = London3SessionStart;
                    end = London3SessionEnd;
                    if (ShouldAutoShiftSession(slot))
                    {
                        TimeSpan shift = GetLondonSessionShiftForDate(referenceTime.Date);
                        start = ShiftTime(start, shift);
                        end = ShiftTime(end, shift);
                    }
                    return true;

                case SessionSlot.NewYork:
                    if (!UseNewYorkSession || NewYorkSessionStart == NewYorkSessionEnd)
                        return false;
                    start = NewYorkSessionStart;
                    end = NewYorkSessionEnd;
                    return true;

                case SessionSlot.NewYork2:
                    if (!UseNewYork2Session || NewYork2SessionStart == NewYork2SessionEnd)
                        return false;
                    start = NewYork2SessionStart;
                    end = NewYork2SessionEnd;
                    return true;

                case SessionSlot.NewYork3:
                    if (!UseNewYork3Session || NewYork3SessionStart == NewYork3SessionEnd)
                        return false;
                    start = NewYork3SessionStart;
                    end = NewYork3SessionEnd;
                    return true;

                default:
                    return false;
            }
        }

        private bool ShouldAutoShiftSession(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.London:
                    return AutoShiftLondon;
                case SessionSlot.London2:
                    return AutoShiftLondon2;
                case SessionSlot.London3:
                    return AutoShiftLondon3;
                default:
                    return false;
            }
        }

        private TimeSpan GetLondonSessionShiftForDate(DateTime date)
        {
            DateTime utcSample = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);
            DateTime utcRef = new DateTime(date.Year, 1, 15, 12, 0, 0, DateTimeKind.Utc);

            TimeZoneInfo londonTz = GetLondonTimeZone();
            TimeZoneInfo sessionTz = GetTargetTimeZone();

            TimeSpan baseline = londonTz.GetUtcOffset(utcRef) - sessionTz.GetUtcOffset(utcRef);
            TimeSpan actual = londonTz.GetUtcOffset(utcSample) - sessionTz.GetUtcOffset(utcSample);
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
                var bars = Bars;
                if (bars != null)
                {
                    var timeZoneProp = bars.GetType().GetProperty(
                        "TimeZoneInfo",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (timeZoneProp != null && typeof(TimeZoneInfo).IsAssignableFrom(timeZoneProp.PropertyType))
                        targetTimeZone = (TimeZoneInfo)timeZoneProp.GetValue(bars, null);
                }

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

        private TimeZoneInfo GetLondonTimeZone()
        {
            if (londonTimeZone != null)
                return londonTimeZone;

            try
            {
                londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            }
            catch
            {
                try
                {
                    londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
                }
                catch
                {
                    londonTimeZone = TimeZoneInfo.Utc;
                }
            }

            return londonTimeZone;
        }

        private bool IsTimeInRange(TimeSpan now, TimeSpan start, TimeSpan end)
        {
            if (start < end)
                return now >= start && now < end;

            return now >= start || now < end;
        }

        private DateTime GetSessionStartTime(SessionSlot slot, DateTime barTime)
        {
            TimeSpan start;
            TimeSpan end;
            if (!TryGetSessionWindow(slot, barTime, out start, out end))
                return Core.Globals.MinDate;

            if (start <= end)
                return barTime.Date + start;

            if (barTime.TimeOfDay < end)
                return barTime.Date.AddDays(-1) + start;

            return barTime.Date + start;
        }

        private void DrawSessionBackgrounds()
        {
            if (CurrentBar < 1)
                return;

            foreach (SessionSlot slot in ConfigurableSessionSlots)
                DrawSessionBackground(slot, "DUO_" + FormatSessionLabel(slot), GetSessionFillBrush(slot));

            foreach (SessionSlot slot in ConfigurableSessionSlots.Where(IsNewYorkFamily))
                DrawNewYorkSkipWindow(slot, Time[0]);
        }

        private void DrawSessionBackground(SessionSlot slot, string tagPrefix, Brush fillBrush)
        {
            TimeSpan start;
            TimeSpan end;
            if (!TryGetSessionWindow(slot, Time[0], out start, out end))
                return;

            DateTime sessionStart = GetSessionStartTime(slot, Time[0]);
            if (sessionStart == Core.Globals.MinDate)
                return;

            DateTime sessionEnd = start > end
                ? sessionStart.AddDays(1).Date + end
                : sessionStart.Date + end;

            string rectTag = string.Format("{0}_SessionFill_{1:yyyyMMdd_HHmm}", tagPrefix, sessionStart);
            if (DrawObjects[rectTag] == null)
            {
                Draw.Rectangle(
                    this,
                    rectTag,
                    false,
                    sessionStart,
                    0,
                    sessionEnd,
                    30000,
                    Brushes.Transparent,
                    fillBrush,
                    10).ZOrder = -1;
            }
        }

        private Brush GetSessionFillBrush(SessionSlot slot)
        {
            switch (GetSessionFamily(slot))
            {
                case SessionFamily.Asia:
                    return AsiaSessionBrush ?? Brushes.LightSkyBlue;
                case SessionFamily.London:
                    return LondonSessionBrush ?? Brushes.LightSkyBlue;
                case SessionFamily.NewYork:
                    return NewYorkSessionBrush ?? Brushes.LightSkyBlue;
                default:
                    return Brushes.LightSkyBlue;
            }
        }

        private bool IsAsiaSundayBlocked(SessionSlot slot, DateTime time)
        {
            if (!IsAsiaFamily(slot) || time.DayOfWeek != DayOfWeek.Sunday)
                return false;

            switch (slot)
            {
                case SessionSlot.Asia:
                    return AsiaBlockSundayTrades;
                case SessionSlot.Asia2:
                    return Asia2BlockSundayTrades;
                case SessionSlot.Asia3:
                    return Asia3BlockSundayTrades;
                default:
                    return false;
            }
        }

        private bool TryGetNewYorkSkipWindow(SessionSlot slot, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            switch (slot)
            {
                case SessionSlot.NewYork:
                    if (!UseNewYorkSession || NewYorkSkipStart == TimeSpan.Zero || NewYorkSkipEnd == TimeSpan.Zero)
                        return false;
                    start = NewYorkSkipStart;
                    end = NewYorkSkipEnd;
                    return true;

                case SessionSlot.NewYork2:
                    if (!UseNewYork2Session || NewYork2SkipStart == TimeSpan.Zero || NewYork2SkipEnd == TimeSpan.Zero)
                        return false;
                    start = NewYork2SkipStart;
                    end = NewYork2SkipEnd;
                    return true;

                case SessionSlot.NewYork3:
                    if (!UseNewYork3Session || NewYork3SkipStart == TimeSpan.Zero || NewYork3SkipEnd == TimeSpan.Zero)
                        return false;
                    start = NewYork3SkipStart;
                    end = NewYork3SkipEnd;
                    return true;

                default:
                    return false;
            }
        }

        private bool IsNewYorkSkipTime(SessionSlot slot, DateTime time)
        {
            if (!IsNewYorkFamily(slot))
                return false;

            TimeSpan skipStart;
            TimeSpan skipEnd;
            if (!TryGetNewYorkSkipWindow(slot, out skipStart, out skipEnd))
                return false;

            TimeSpan now = time.TimeOfDay;
            if (skipStart < skipEnd)
                return now >= skipStart && now <= skipEnd;

            return now >= skipStart || now <= skipEnd;
        }

        private void DrawNewYorkSkipWindow(SessionSlot slot, DateTime barTime)
        {
            TimeSpan skipStart;
            TimeSpan skipEnd;
            if (!TryGetNewYorkSkipWindow(slot, out skipStart, out skipEnd))
                return;

            DateTime windowStart = barTime.Date + skipStart;
            DateTime windowEnd = barTime.Date + skipEnd;
            if (skipStart > skipEnd)
                windowEnd = windowEnd.AddDays(1);

            int startBarsAgo = Bars.GetBar(windowStart);
            int endBarsAgo = Bars.GetBar(windowEnd);
            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));
            var lineBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
            try
            {
                if (areaBrush.CanFreeze)
                    areaBrush.Freeze();
                if (lineBrush.CanFreeze)
                    lineBrush.Freeze();
            }
            catch
            {
            }

            string tagBase = string.Format("DUO_{0}_Skip_{1:yyyyMMdd_HHmm}", FormatSessionLabel(slot), windowStart);
            Draw.Rectangle(
                this,
                tagBase + "_Rect",
                false,
                windowStart,
                0,
                windowEnd,
                30000,
                lineBrush,
                areaBrush,
                2).ZOrder = -1;

            Draw.VerticalLine(this, tagBase + "_Start", windowStart, lineBrush, DashStyleHelper.DashDot, 2);
            Draw.VerticalLine(this, tagBase + "_End", windowEnd, lineBrush, DashStyleHelper.DashDot, 2);
        }

        private void DrawNewsWindows(DateTime barTime)
        {
            if (!UseNewsSkip)
                return;

            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime newsTime = NewsDates[i];
                if (newsTime.Date != barTime.Date)
                    continue;

                DateTime windowStart = newsTime.AddMinutes(-NewsBlockMinutes);
                DateTime windowEnd = newsTime.AddMinutes(NewsBlockMinutes);

                var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));
                var lineBrush = new SolidColorBrush(Color.FromArgb(20, 30, 144, 255));
                try
                {
                    if (areaBrush.CanFreeze)
                        areaBrush.Freeze();
                    if (lineBrush.CanFreeze)
                        lineBrush.Freeze();
                }
                catch { }

                string tagBase = string.Format("DUO_News_{0:yyyyMMdd_HHmm}", newsTime);

                Draw.Rectangle(
                    this,
                    tagBase + "_Rect",
                    false,
                    windowStart,
                    0,
                    windowEnd,
                    30000,
                    lineBrush,
                    areaBrush,
                    2).ZOrder = -1;

                Draw.VerticalLine(this, tagBase + "_Start", windowStart, lineBrush, DashStyleHelper.DashDot, 2);
                Draw.VerticalLine(this, tagBase + "_End", windowEnd, lineBrush, DashStyleHelper.DashDot, 2);
            }
        }

        private void EnsureNewsDatesInitialized()
        {
            if (newsDatesInitialized)
                return;

            NewsDates.Clear();
            LoadHardcodedNewsDates();

            NewsDates.Sort();
            newsDatesInitialized = true;
            AppendNewsFetchLog(string.Format(
                "Refresh complete | source=hardcoded count={0} events=[{1}]",
                NewsDates.Count,
                FormatNewsDatesForLog(NewsDates)));
        }

        private void LoadHardcodedNewsDates()
        {
            if (string.IsNullOrWhiteSpace(NewsDatesRaw))
                return;

            string[] entries = NewsDatesRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                string trimmed = entries[i].Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                DateTime parsed;
                if (!DateTime.TryParseExact(trimmed, "yyyy-MM-dd,HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
                {
                    LogDebug(string.Format("Invalid news date entry: {0}", trimmed));
                    continue;
                }

                TimeSpan t = parsed.TimeOfDay;
                if ((t == new TimeSpan(8, 30, 0) || t == new TimeSpan(14, 0, 0)) && !NewsDates.Contains(parsed))
                    NewsDates.Add(parsed);
            }
        }

        private static string FormatNewsDatesForLog(List<DateTime> dates)
        {
            if (dates == null || dates.Count == 0)
                return string.Empty;

            return string.Join(", ", dates.Select(d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        }

        private void AppendNewsFetchLog(string message)
        {
            LogDebug(string.Format("NewsFetch | {0}", message ?? string.Empty));
        }

        private bool TimeInNewsSkip(DateTime time)
        {
            if (!UseNewsSkip)
                return false;

            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime newsTime = NewsDates[i];
                if (newsTime.Date != time.Date)
                    continue;

                DateTime windowStart = newsTime.AddMinutes(-NewsBlockMinutes);
                DateTime windowEnd = newsTime.AddMinutes(NewsBlockMinutes);
                if (time >= windowStart && time <= windowEnd)
                    return true;
            }

            return false;
        }

        private bool GetSessionClosed(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    return asiaSessionClosed;
                case SessionSlot.Asia2:
                    return asia2SessionClosed;
                case SessionSlot.Asia3:
                    return asia3SessionClosed;
                case SessionSlot.London:
                    return londonSessionClosed;
                case SessionSlot.London2:
                    return london2SessionClosed;
                case SessionSlot.London3:
                    return london3SessionClosed;
                case SessionSlot.NewYork:
                    return newYorkSessionClosed;
                case SessionSlot.NewYork2:
                    return newYork2SessionClosed;
                case SessionSlot.NewYork3:
                    return newYork3SessionClosed;
                default:
                    return false;
            }
        }

        private void SetSessionClosed(SessionSlot slot, bool value)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    asiaSessionClosed = value;
                    break;
                case SessionSlot.Asia2:
                    asia2SessionClosed = value;
                    break;
                case SessionSlot.Asia3:
                    asia3SessionClosed = value;
                    break;
                case SessionSlot.London:
                    londonSessionClosed = value;
                    break;
                case SessionSlot.London2:
                    london2SessionClosed = value;
                    break;
                case SessionSlot.London3:
                    london3SessionClosed = value;
                    break;
                case SessionSlot.NewYork:
                    newYorkSessionClosed = value;
                    break;
                case SessionSlot.NewYork2:
                    newYork2SessionClosed = value;
                    break;
                case SessionSlot.NewYork3:
                    newYork3SessionClosed = value;
                    break;
            }
        }

        private string FormatSessionLabel(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    return "Asia1";
                case SessionSlot.Asia2:
                    return "Asia2";
                case SessionSlot.Asia3:
                    return "Asia3";
                case SessionSlot.London:
                    return "London1";
                case SessionSlot.London2:
                    return "London2";
                case SessionSlot.London3:
                    return "London3";
                case SessionSlot.NewYork:
                    return "NewYork1";
                case SessionSlot.NewYork2:
                    return "NewYork2";
                case SessionSlot.NewYork3:
                    return "NewYork3";
                default:
                    return "None";
            }
        }

        private void LogSessionActivation(string reason)
        {
            TimeSpan start;
            TimeSpan end;
            if (!TryGetSessionWindow(activeSession, Time[0], out start, out end))
                return;

            bool inNow = TimeInSession(activeSession, Time[0]);

            LogDebug(string.Format(
                "SessionConfig ({0}) | session={1} inSessionNow={2} closeAtSessionEnd={3} forceClose={4} start={5:hh\\:mm} end={6:hh\\:mm} ema={7} adxMin={8:0.##} adxMax={9:0.##} adxSlopeMin={10:0.##} adxPeakDd={11:0.##} adxAbsExit={12:0.##} tpPts={13:0.##} contracts={14} exitCross={15:0.##} flipCross={16:0.##} entryStop={17} slPad={18:0.##} hvSlPad={19:0.##} hvWindow={20:hh\\:mm}-{21:hh\\:mm} entryOffset={22:0.##} flipBe={23}/{24:0.##} flipTp={25:0.##} tpPct={26:0.##} mode={27} atrMult={28:0.##} stopPct={29:0.##} adxFlipMin={30} adxDdRiskMode={31} adxDdRiskSlPts={32:0.##} adxDdRiskTpPts={33:0.##} horizontal={34} atrMin={35:0.##}",
                reason,
                FormatSessionLabel(activeSession),
                inNow,
                CloseAtSessionEnd,
                string.IsNullOrWhiteSpace(ForceCloseTime) ? "Off" : ForceCloseTime,
                start,
                end,
                activeEmaPeriod,
                activeAdxThreshold,
                activeAdxMaxThreshold,
                activeAdxMinSlopePoints,
                activeAdxPeakDrawdownExitUnits,
                activeAdxAbsoluteExitLevel,
                activeTakeProfitPoints,
                activeContracts,
                activeExitCrossPoints,
                GetEffectiveFlipEmaCrossPoints(),
                activeEntryStopMode,
                activeStopPaddingPoints,
                activeHvSlPaddingPoints,
                activeHvSlStartTime,
                activeHvSlEndTime,
                activeEntryOffsetPoints,
                activeEnableFlipBreakEven,
                activeFlipBreakEvenTriggerPoints,
                activeFlipTakeProfitPoints,
                activeTakeProfitPercentTriggerPercent,
                activeTakeProfitStopMode,
                activeTakeProfitAtrTrailMultiplier,
                activeTakeProfitPercentStopMovePercent,
                activeRequireMinAdxForFlips,
                activeEnableAdxDdRiskMode,
                activeAdxDdRiskModeStopLossPoints,
                activeAdxDdRiskModeTakeProfitPoints,
                activeHorizontalExitBars,
                activeMinimumAtrForEntry));

            int adxPlotCount = activeAdx != null && activeAdx.Plots != null ? activeAdx.Plots.Length : 0;
            int adxValueCount = activeAdx != null && activeAdx.Values != null ? activeAdx.Values.Length : 0;
            string adxType = activeAdx != null ? activeAdx.GetType().Name : "null";
            LogDebug(string.Format(
                "AdxVisuals ({0}) | session={1} type={2} showAdx={3} showThresholds={4} plots={5} values={6}",
                reason,
                FormatSessionLabel(activeSession),
                adxType,
                ShowAdxOnChart,
                ShowAdxThresholdLines,
                adxPlotCount,
                adxValueCount));

            int atrPlotCount = atrVisual != null && atrVisual.Plots != null ? atrVisual.Plots.Length : 0;
            string atrType = atrVisual != null ? atrVisual.GetType().Name : "null";
            LogDebug(string.Format(
                "AtrVisuals ({0}) | session={1} type={2} showAtr={3} showThresholds={4} plots={5} threshold={6:0.##}",
                reason,
                FormatSessionLabel(activeSession),
                atrType,
                ShowAtrOnChart,
                ShowAtrThresholdLines,
                atrPlotCount,
                activeMinimumAtrForEntry));
        }

        private void BeginTradeAttempt(string side)
        {
            if (!DebugLogging)
                return;

            if (tradeAttemptOpen && string.Equals(tradeAttemptSide, side, StringComparison.Ordinal))
                return;

            if (tradeAttemptOpen)
                EndTradeAttempt("interrupted");

            tradeAttemptOpen = true;
            tradeAttemptId++;
            tradeAttemptSide = side;
            LogDebug(string.Format("Attempt #{0} start | side={1} session={2}", tradeAttemptId, side, FormatSessionLabel(activeSession)));
        }

        private void EndTradeAttempt(string reason)
        {
            if (!DebugLogging || !tradeAttemptOpen)
                return;

            LogDebug(string.Format("Attempt #{0} end | reason={1}", tradeAttemptId, reason));
            Print(string.Empty);
            tradeAttemptOpen = false;
            tradeAttemptSide = string.Empty;
        }

        private void LogMessage(string message, bool forceLog)
        {
            if (!forceLog && !DebugLogging)
                return;
            if (Bars == null || CurrentBar < 0)
            {
                Print(string.Format("{0} | {1}", HeartbeatStrategyName, message));
                return;
            }

            Print(string.Format("{0} | {3} | bar={1} | {2}", Time[0], CurrentBar, message, HeartbeatStrategyName));
        }

        private void LogDebug(string message)
        {
            LogMessage(message, false);
        }

        private void LogProjectXDiscovery(string message)
        {
            if (WebhookProviderType != WebhookProvider.ProjectX)
                return;

            LogDebug(message);
        }

        private void LogProjectXStatus(string message)
        {
            if (WebhookProviderType != WebhookProvider.ProjectX)
                return;

            LogMessage(message, true);
        }

        public void UpdateInfo()
        {
            if (ChartControl == null)
                return;
            UpdateInfoText();
        }

        public void UpdateInfoText()
        {
            if (State != State.Realtime && State != State.Historical)
                return;

            if (ChartControl == null || ChartControl.Dispatcher == null)
                return;

            var lines = BuildInfoLines();
            if (!legacyInfoDrawingsCleared)
            {
                RemoveLegacyInfoBoxDrawings();
                legacyInfoDrawingsCleared = true;
            }

            ChartControl.Dispatcher.InvokeAsync(() => RenderInfoBoxOverlay(lines));
        }

        private void RenderInfoBoxOverlay(List<(string label, string value, Brush labelBrush, Brush valueBrush)> lines)
        {
            if (!EnsureInfoBoxOverlay())
                return;

            if (infoBoxRowsPanel == null)
                return;

            infoBoxRowsPanel.Children.Clear();

            for (int i = 0; i < lines.Count; i++)
            {
                bool isHeader = i == 0;
                bool isFooter = i == lines.Count - 1;
                var rowBorder = new Border
                {
                    Background = (isHeader || isFooter)
                        ? InfoHeaderFooterGradientBrush
                        : (i % 2 == 0 ? InfoBodyEvenBrush : InfoBodyOddBrush),
                    Padding = new Thickness(6, 2, 6, 2)
                };

                var text = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = isHeader ? 15 : 14,
                    FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                    TextAlignment = (isHeader || isFooter) ? TextAlignment.Center : TextAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);

                string rawLabel = lines[i].label ?? string.Empty;
                string value = lines[i].value ?? string.Empty;
                string label = rawLabel;

                if (string.IsNullOrEmpty(value) && rawLabel.StartsWith("News:", StringComparison.Ordinal))
                {
                    label = "News:";
                    value = rawLabel.Substring("News:".Length).TrimStart();
                }
                string normalizedValue = NormalizeInfoValueToken(value);
                bool valueUsesEmojiRendering = ClassifyInfoValueRunKind(normalizedValue) == InfoValueRunKind.Emoji;
                TextOptions.SetTextRenderingMode(text, valueUsesEmojiRendering ? TextRenderingMode.Grayscale : TextRenderingMode.ClearType);

                text.Inlines.Add(new Run(label) { Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : InfoLabelBrush });
                if (!string.IsNullOrEmpty(value))
                {
                    text.Inlines.Add(new Run(" ") { Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : InfoLabelBrush });

                    Brush stateValueBrush = lines[i].valueBrush;
                    if (stateValueBrush == null || stateValueBrush == Brushes.Transparent)
                        stateValueBrush = lines[i].labelBrush;
                    if (stateValueBrush == null || stateValueBrush == Brushes.Transparent)
                        stateValueBrush = InfoValueBrush;

                    var valueRun = BuildInfoValueRun(normalizedValue, stateValueBrush);
                    text.Inlines.Add(valueRun);
                }

                rowBorder.Child = text;
                infoBoxRowsPanel.Children.Add(rowBorder);
            }
        }

        private static readonly FontFamily InfoEmojiFontFamily = new FontFamily("Segoe UI Emoji");
        private static readonly FontFamily InfoSymbolFontFamily = new FontFamily("Segoe UI Symbol");

        private static readonly HashSet<string> InfoEmojiTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "✔", "✔️", "✅", "❌", "✖", "⛔", "⛔️", "🚫", "⬜", "🕒"
        };

        private static readonly HashSet<string> InfoSymbolTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "■", "□", "●", "○", "▲", "▼", "◆", "◇"
        };

        private enum InfoValueRunKind
        {
            Default,
            Emoji,
            Symbol
        }

        private Run BuildInfoValueRun(string value, Brush stateValueBrush)
        {
            string safeValue = value ?? string.Empty;
            string normalizedValue = NormalizeInfoValueToken(safeValue);
            switch (ClassifyInfoValueRunKind(normalizedValue))
            {
                case InfoValueRunKind.Emoji:
                    var emojiRun = new Run(normalizedValue) { FontFamily = InfoEmojiFontFamily, Foreground = stateValueBrush };
                    TextOptions.SetTextRenderingMode(emojiRun, TextRenderingMode.Grayscale);
                    return emojiRun;
                case InfoValueRunKind.Symbol:
                    return new Run(normalizedValue) { FontFamily = InfoSymbolFontFamily, Foreground = stateValueBrush };
                default:
                    return new Run(normalizedValue) { Foreground = stateValueBrush };
            }
        }

        private string NormalizeInfoValueToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value ?? string.Empty;

            string token = value.Trim();
            if (token == "○" || token == "◯" || token == "⚪")
                return "🚫";

            return value;
        }

        private InfoValueRunKind ClassifyInfoValueRunKind(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return InfoValueRunKind.Default;

            string token = value.Trim();
            if (InfoEmojiTokens.Contains(token) || ContainsEmojiCodePoint(token))
                return InfoValueRunKind.Emoji;
            if (InfoSymbolTokens.Contains(token))
                return InfoValueRunKind.Symbol;
            return InfoValueRunKind.Default;
        }

        private bool ContainsEmojiCodePoint(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                int codePoint = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }

                if ((codePoint >= 0x1F300 && codePoint <= 0x1FAFF) ||
                    (codePoint >= 0x2600 && codePoint <= 0x27BF))
                {
                    return true;
                }
            }

            return false;
        }

        private bool EnsureInfoBoxOverlay()
        {
            if (ChartControl == null)
                return false;

            if (infoBoxContainer != null && infoBoxRowsPanel != null)
                return true;

            var host = ChartControl.Parent as System.Windows.Controls.Panel;
            if (host == null)
                return false;

            infoBoxRowsPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            infoBoxContainer = new Border
            {
                Child = infoBoxRowsPanel,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(5, 8, 8, 37),
                Background = Brushes.Transparent
            };

            host.Children.Add(infoBoxContainer);
            System.Windows.Controls.Panel.SetZIndex(infoBoxContainer, int.MaxValue);
            return true;
        }

        private void DisposeInfoBoxOverlay()
        {
            try
            {
                if (ChartControl == null || ChartControl.Dispatcher == null)
                {
                    infoBoxRowsPanel = null;
                    infoBoxContainer = null;
                    return;
                }

                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    if (infoBoxContainer != null)
                    {
                        var parent = infoBoxContainer.Parent as System.Windows.Controls.Panel;
                        if (parent != null)
                            parent.Children.Remove(infoBoxContainer);
                    }

                    infoBoxRowsPanel = null;
                    infoBoxContainer = null;
                });
            }
            catch
            {
                infoBoxRowsPanel = null;
                infoBoxContainer = null;
            }
        }

        private void RemoveLegacyInfoBoxDrawings()
        {
            RemoveDrawObject("myStatusLabel_bg");
            RemoveDrawObject("myStatusLabel_bg_top");
            RemoveDrawObject("myStatusLabel_bg_bottom");
            for (int i = 0; i < 64; i++)
            {
                RemoveDrawObject(string.Format("myStatusLabel_bg_{0}", i));
                RemoveDrawObject(string.Format("myStatusLabel_label_{0}", i));
                RemoveDrawObject(string.Format("myStatusLabel_val_{0}", i));
            }
        }

        private List<(string label, string value, Brush labelBrush, Brush valueBrush)> BuildInfoLines()
        {
            var lines = new List<(string label, string value, Brush labelBrush, Brush valueBrush)>();
            string contractsText = Math.Max(1, activeContracts).ToString(CultureInfo.InvariantCulture);
            double adxValue = activeAdx != null ? activeAdx[0] : 0.0;
            double adxSlope = GetAdxSlopePoints();
            bool adxMinEnabled = activeAdxThreshold > 0.0;
            bool adxMaxEnabled = activeAdxMaxThreshold > 0.0;
            bool slopeEnabled = activeAdxMinSlopePoints > 0.0;
            bool aboveMin = !adxMinEnabled || adxValue >= activeAdxThreshold;
            bool belowMin = adxMinEnabled && adxValue < activeAdxThreshold;
            bool overMax = adxMaxEnabled && adxValue > activeAdxMaxThreshold;
            bool slopeValid = !slopeEnabled || adxSlope >= activeAdxMinSlopePoints;

            string paState;
            Brush paBrush;
            if (overMax)
            {
                paState = "Peaking";
                paBrush = Brushes.OrangeRed;
            }
            else if (belowMin)
            {
                paState = "Weak";
                paBrush = Brushes.IndianRed;
            }
            else if (aboveMin && slopeValid)
            {
                paState = "Trending";
                paBrush = Brushes.LimeGreen;
            }
            else
            {
                paState = "Ranging";
                paBrush = Brushes.Gold;
            }

            lines.Add((string.Format("DUO v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(("Contracts:", contractsText, Brushes.LightGray, Brushes.LightGray));
            lines.Add(("PA:", paState, Brushes.LightGray, paBrush));
            string configuredMomentumText = activeAdxMinSlopePoints > 0.0
                ? activeAdxMinSlopePoints.ToString("0.00", CultureInfo.InvariantCulture)
                : "Off";
            string currentMomentumText = adxSlope.ToString("0.00", CultureInfo.InvariantCulture);
            lines.Add(("Mom:", configuredMomentumText + "/" + currentMomentumText, Brushes.LightGray, Brushes.LightGray));
            if (!UseNewsSkip)
            {
                lines.Add(("News:", "Disabled", Brushes.LightGray, Brushes.LightGray));
            }
            else
            {
                List<DateTime> weekNews = GetCurrentWeekNews(Time[0]);
                if (weekNews.Count == 0)
                {
                    lines.Add(("News:", "🚫", Brushes.LightGray, Brushes.IndianRed));
                }
                else
                {
                    for (int i = 0; i < weekNews.Count; i++)
                    {
                        DateTime newsTime = weekNews[i];
                        bool blockPassed = Time[0] > newsTime.AddMinutes(NewsBlockMinutes);
                        string dayPart = newsTime.ToString("ddd", CultureInfo.InvariantCulture);
                        string timePart = newsTime.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
                        string label = "News: " + dayPart + " " + timePart;
                        Brush labelBrush = blockPassed ? PassedNewsRowBrush : Brushes.LightGray;
                        lines.Add((label, string.Empty, labelBrush, Brushes.Transparent));
                    }
                }
            }

            lines.Add(("Session:", FormatSessionLabel(activeSession), Brushes.LightGray, Brushes.LightGray));
            lines.Add(("AutoEdge Systems™", string.Empty, InfoLabelBrush, Brushes.Transparent));

            return lines;
        }

        private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            try
            {
                if (brush.CanFreeze)
                    brush.Freeze();
            }
            catch { }
            return brush;
        }

        private static Brush CreateFrozenVerticalGradientBrush(Color top, Color mid, Color bottom)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0.0),
                EndPoint = new Point(0.5, 1.0)
            };
            brush.GradientStops.Add(new GradientStop(top, 0.0));
            brush.GradientStops.Add(new GradientStop(mid, 0.5));
            brush.GradientStops.Add(new GradientStop(bottom, 1.0));
            try
            {
                if (brush.CanFreeze)
                    brush.Freeze();
            }
            catch { }
            return brush;
        }

        private List<DateTime> GetCurrentWeekNews(DateTime time)
        {
            EnsureNewsDatesInitialized();

            var weekNews = new List<DateTime>();
            DateTime weekStart = GetWeekStart(time.Date);
            DateTime weekEnd = weekStart.AddDays(7);
            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime candidate = NewsDates[i];
                if (candidate >= weekStart && candidate < weekEnd)
                    weekNews.Add(candidate);
            }

            weekNews.Sort();
            return weekNews;
        }

        private DateTime GetWeekStart(DateTime date)
        {
            DayOfWeek firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int diff = (7 + (date.DayOfWeek - firstDayOfWeek)) % 7;
            return date.AddDays(-diff).Date;
        }

        private string GetAddOnVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Version version = assembly.GetName().Version;
            return version != null ? version.ToString() : "0.0.0.0";
        }

        private int GetMaxConfiguredAdxPeriod()
        {
            return new[]
            {
                AsiaAdxPeriod,
                Asia2AdxPeriod,
                Asia3AdxPeriod,
                LondonAdxPeriod,
                London2AdxPeriod,
                London3AdxPeriod,
                NewYorkAdxPeriod,
                NewYork2AdxPeriod,
                NewYork3AdxPeriod
            }.Max();
        }

        private void ValidateRequiredPrimaryTimeframe(int requiredMinutes)
        {
            bool isMinuteSeries = BarsPeriod != null && BarsPeriod.BarsPeriodType == NinjaTrader.Data.BarsPeriodType.Minute;
            bool timeframeMatches = isMinuteSeries && BarsPeriod.Value == requiredMinutes;
            isConfiguredTimeframeValid = timeframeMatches;
            if (timeframeMatches)
                return;

            string actualTimeframe = BarsPeriod == null
                ? "Unknown"
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", BarsPeriod.Value, BarsPeriod.BarsPeriodType);
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "{0} must run on a {1}-minute chart. Current chart is {2}. Trading is disabled until timeframe is corrected.",
                Name,
                requiredMinutes,
                actualTimeframe);
            LogDebug("Timeframe validation failed | " + message);
            ShowTimeframeValidationPopup(message);
        }

        private void ShowTimeframeValidationPopup(string message)
        {
            if (timeframePopupShown)
                return;

            timeframePopupShown = true;
            if (System.Windows.Application.Current == null)
                return;

            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () =>
                    {
                        System.Windows.MessageBox.Show(
                            message,
                            "Invalid Timeframe",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    });
            }
            catch (Exception ex)
            {
                LogDebug("Failed to show timeframe popup: " + ex.Message);
            }
        }

        private void ValidateRequiredPrimaryInstrument()
        {
            string instrumentName = Instrument != null && Instrument.MasterInstrument != null
                ? (Instrument.MasterInstrument.Name ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
            bool instrumentMatches = instrumentName == "NQ" || instrumentName == "MNQ";
            isConfiguredInstrumentValid = instrumentMatches;
            if (instrumentMatches)
                return;

            string actualInstrument = string.IsNullOrWhiteSpace(instrumentName) ? "Unknown" : instrumentName;
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "{0} must run on NQ or MNQ. Current instrument is {1}. Trading is disabled until instrument is corrected.",
                Name,
                actualInstrument);
            LogDebug("Instrument validation failed | " + message);
            ShowInstrumentValidationPopup(message);
        }

        private void ShowInstrumentValidationPopup(string message)
        {
            if (instrumentPopupShown)
                return;

            instrumentPopupShown = true;
            if (System.Windows.Application.Current == null)
                return;

            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () =>
                    {
                        System.Windows.MessageBox.Show(
                            message,
                            "Invalid Instrument",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    });
            }
            catch (Exception ex)
            {
                LogDebug("Failed to show instrument popup: " + ex.Message);
            }
        }

        private bool ShowEntryConfirmation(string orderType, double price, int quantity)
        {
            bool result = false;
            if (System.Windows.Application.Current == null)
                return false;

            System.Windows.Application.Current.Dispatcher.Invoke(
                () =>
                {
                    var message = string.Format(CultureInfo.InvariantCulture, "Confirm {0} entry\nPrice: {1}\nQty: {2}", orderType, price, quantity);
                    var res =
                        System.Windows.MessageBox.Show(message, "Entry Confirmation", System.Windows.MessageBoxButton.YesNo,
                                                    System.Windows.MessageBoxImage.Question);

                    result = (res == System.Windows.MessageBoxResult.Yes);
                });

            return result;
        }

        private int GetEntryQuantity()
        {
            int baseQty = Math.Max(1, activeContracts);
            return baseQty;
        }

        private double GetWebhookTakeProfitPrice(double entryPrice, double takeProfitPoints, bool isLong)
        {
            if (takeProfitPoints <= 0.0)
                return entryPrice;

            double takeProfitPrice = isLong
                ? entryPrice + takeProfitPoints
                : entryPrice - takeProfitPoints;
            return Instrument.MasterInstrument.RoundToTickSize(takeProfitPrice);
        }

        private void SetStopLossByDistanceTicks(string fromEntrySignal, double referenceEntryPrice, double plannedStopPrice)
        {
            int stopTicks = PriceToTicks(Math.Abs(referenceEntryPrice - plannedStopPrice));
            if (stopTicks < 1)
                stopTicks = 1;
            SetStopLoss(fromEntrySignal, CalculationMode.Ticks, stopTicks, false);
        }

        private void SetProfitTargetByDistanceTicks(string fromEntrySignal, double takeProfitPoints)
        {
            if (takeProfitPoints <= 0.0)
                return;

            int targetTicks = PriceToTicks(takeProfitPoints);
            if (targetTicks < 1)
                targetTicks = 1;
            SetProfitTarget(fromEntrySignal, CalculationMode.Ticks, targetTicks);
        }

        private bool IsAccountBalanceBlocked()
        {
            if (MaxAccountBalance <= 0.0)
                return false;

            if (accountBalanceLimitReached)
                return true;

            double balance;
            if (!TryGetCurrentCashValue(out balance))
                return false;

            if (balance < MaxAccountBalance)
                return false;

            if (!accountBalanceLimitReached || accountBalanceLimitReachedBar != CurrentBar)
            {
                accountBalanceLimitReached = true;
                accountBalanceLimitReachedBar = CurrentBar;
                CancelWorkingEntryOrders();
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(BuildExitSignalName("MaxAccountBalance"), GetOpenLongEntrySignal());
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(BuildExitSignalName("MaxAccountBalance"), GetOpenShortEntrySignal());

                LogDebug(string.Format("Account balance target reached | netLiq={0:0.00} target={1:0.00} trading paused.", balance, MaxAccountBalance));
            }

            return true;
        }

        private bool TryGetCurrentCashValue(out double cashValue)
        {
            cashValue = 0.0;
            if (Account == null)
                return false;

            try
            {
                cashValue = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                if (cashValue > 0.0)
                    return true;

                double realizedCash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                double unrealized = Position.MarketPosition != MarketPosition.Flat
                    ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0])
                    : 0.0;
                cashValue = realizedCash + unrealized;
                return realizedCash > 0.0 || Position.MarketPosition != MarketPosition.Flat;
            }
            catch
            {
                return false;
            }
        }

        private void SendFlipWebhooks(string entryEventType, double entryPrice, double takeProfitPrice, double stopPrice, bool isMarketEntry, int entryQuantity, MarketPosition expectedCurrentPosition)
        {
            int positionQty = Math.Abs(Position.Quantity);
            bool shouldExitFirst = Position.MarketPosition == expectedCurrentPosition && positionQty > 0;

            if (shouldExitFirst)
            {
                LogDebug(string.Format(
                    "Flip webhook sequence | step=exit side={0} qty={1}",
                    expectedCurrentPosition,
                    positionQty));
                bool exitWebhookSent = SendWebhook("exit", 0, 0, 0, true, positionQty);
                if (WebhookProviderType == WebhookProvider.ProjectX)
                    suppressProjectXNextExecutionExitWebhook = exitWebhookSent;
            }

            int webhookEntryQty = Math.Max(1, entryQuantity);
            LogDebug(string.Format(
                "Flip webhook sequence | step=entry action={0} entryQty={1} market={2} entry={3:0.00} stop={4:0.00}",
                entryEventType,
                webhookEntryQty,
                isMarketEntry,
                entryPrice,
                stopPrice));
            SendWebhook(entryEventType, entryPrice, takeProfitPrice, stopPrice, isMarketEntry, webhookEntryQty);
        }

        private bool SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime)
                return false;

            if (WebhookProviderType == WebhookProvider.ProjectX)
            {
                int orderQtyForProvider = quantityOverride > 0 ? quantityOverride : Math.Max(1, activeContracts);
                LogDebug(string.Format(
                    "Webhook attempt | provider=ProjectX event={0} qty={1} market={2} entry={3:0.00} tp={4:0.00} sl={5:0.00}",
                    eventType,
                    orderQtyForProvider,
                    isMarketEntry,
                    entryPrice,
                    takeProfit,
                    stopLoss));
                return SendProjectX(eventType, entryPrice, takeProfit, stopLoss, isMarketEntry, orderQtyForProvider);
            }

            if (string.IsNullOrWhiteSpace(WebhookUrl))
            {
                LogDebug(string.Format("Webhook skipped | provider=TradersPost event={0} reason=empty-url", eventType));
                return false;
            }

            try
            {
                int orderQty = quantityOverride > 0 ? quantityOverride : Math.Max(1, activeContracts);
                string ticker = !string.IsNullOrWhiteSpace(WebhookTickerOverride)
                    ? WebhookTickerOverride.Trim()
                    : (Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "UNKNOWN");
                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
                string json = string.Empty;
                string action = eventType.ToLowerInvariant();

                if (action == "buy" || action == "sell")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"{1}\",\"orderType\":\"{2}\",\"quantityType\":\"fixed_quantity\",\"quantity\":{3},\"signalPrice\":{4},\"time\":\"{5}\",\"takeProfit\":{{\"limitPrice\":{6}}},\"stopLoss\":{{\"type\":\"stop\",\"stopPrice\":{7}}}}}",
                        ticker,
                        action,
                        isMarketEntry ? "market" : "limit",
                        orderQty,
                        entryPrice,
                        time,
                        takeProfit,
                        stopLoss);
                }
                else if (action == "exit")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"exit\",\"orderType\":\"market\",\"quantityType\":\"fixed_quantity\",\"quantity\":{1},\"cancel\":true,\"time\":\"{2}\"}}",
                        ticker,
                        orderQty,
                        time);
                }
                else if (action == "cancel")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"cancel\",\"time\":\"{1}\"}}",
                        ticker,
                        time);
                }

                if (string.IsNullOrWhiteSpace(json))
                {
                    LogDebug(string.Format(
                        "Webhook skipped | provider=TradersPost event={0} reason=empty-payload qty={1}",
                        eventType,
                        orderQty));
                    return false;
                }

                LogDebug(string.Format(
                    "Webhook attempt | provider=TradersPost event={0} action={1} qty={2} market={3} url={4}",
                    eventType,
                    action,
                    orderQty,
                    isMarketEntry,
                    WebhookUrl));
                LogDebug(string.Format("Webhook payload | {0}", json));

                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.UploadString(WebhookUrl, "POST", json);
                }

                LogDebug(string.Format("Webhook sent | provider=TradersPost event={0} action={1} qty={2}", eventType, action, orderQty));
                return true;
            }
            catch (Exception ex)
            {
                LogDebug(string.Format("Webhook error: {0}", ex.Message));
                return false;
            }
        }

        private bool SendProjectX(string eventType, double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            if (!EnsureProjectXSession())
            {
                LogDebug(string.Format("Webhook skipped | provider=ProjectX event={0} reason=auth-unavailable", eventType));
                return false;
            }

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetProjectXTargets(out targetAccounts, out contractId))
            {
                LogDebug(string.Format("Webhook skipped | provider=ProjectX event={0} reason=account-selection-or-contract-unavailable", eventType));
                return false;
            }

            LogDebug(string.Format(
                "ProjectX targets | event={0} accounts={1} contractId={2}",
                eventType,
                string.Join(", ", targetAccounts.Select(a => string.Format(CultureInfo.InvariantCulture, "{0}:{1}", a.Id, a.Name ?? string.Empty)).ToArray()),
                contractId));

            foreach (var account in targetAccounts)
            {
                try
                {
                    switch (eventType.ToLowerInvariant())
                    {
                        case "buy":
                        case "sell":
                            if (ProjectXPrepareForEntry(account.Id, contractId))
                                ProjectXPlaceOrder(eventType, account.Id, contractId, entryPrice, takeProfit, stopLoss, isMarketEntry, quantity);
                            break;
                        case "exit":
                            ProjectXFlattenPosition(account.Id, contractId);
                            break;
                        case "cancel":
                            ProjectXCancelOrders(account.Id, contractId);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LogDebug(string.Format(
                        "ProjectX account error | event={0} accountId={1} accountName={2} error={3}",
                        eventType,
                        account.Id,
                        account.Name ?? string.Empty,
                        ex.Message));
                }
            }

            return targetAccounts.Count > 0;
        }

        private void RunProjectXStartupPreflight()
        {
            if (WebhookProviderType != WebhookProvider.ProjectX)
            {
                LogDebug(string.Format(
                    "ProjectX startup preflight skipped | provider={0}",
                    WebhookProviderType));
                return;
            }

            string instrumentKey = GetProjectXInstrumentKey();
            string selectors = string.Join(", ", ParseProjectXAccountSelectors(ProjectXAccountId).ToArray());
            LogProjectXDiscovery(string.Format(
                "ProjectX startup preflight begin | instrument={0} selectors={1} baseUrl={2}",
                string.IsNullOrWhiteSpace(instrumentKey) ? "<unknown>" : instrumentKey,
                string.IsNullOrWhiteSpace(selectors) ? "<none>" : selectors,
                string.IsNullOrWhiteSpace(ProjectXApiBaseUrl) ? "<empty>" : ProjectXApiBaseUrl));

            if (!EnsureProjectXSession())
            {
                LogProjectXDiscovery("ProjectX startup preflight failed | stage=auth");
                return;
            }

            List<ProjectXAccountInfo> accounts;
            if (!TryLoadProjectXAccounts(out accounts))
            {
                LogProjectXDiscovery("ProjectX startup preflight failed | stage=accounts");
                return;
            }

            string contractId;
            if (!TryResolveProjectXContractId(out contractId))
            {
                LogProjectXDiscovery("ProjectX startup preflight failed | stage=contract");
                return;
            }

            List<ProjectXAccountInfo> targetAccounts;
            string targetContractId;
            if (!TryGetProjectXTargets(out targetAccounts, out targetContractId))
            {
                LogProjectXDiscovery("ProjectX startup preflight failed | stage=targets");
                return;
            }

            LogProjectXStatus(string.Format(
                "ProjectX webhook targets | count={0} contractId={1}",
                targetAccounts.Count,
                targetContractId ?? string.Empty));
            foreach (var account in targetAccounts)
            {
                LogProjectXStatus(string.Format(
                    "ProjectX target account | id={0} name={1}",
                    account.Id,
                    account.Name ?? string.Empty));
            }

            LogProjectXDiscovery(string.Format(
                "ProjectX startup preflight ready | accounts={0} contractId={1}",
                FormatProjectXAccountsForLog(targetAccounts),
                targetContractId));
        }

        private bool EnsureProjectXSession()
        {
            if (string.IsNullOrWhiteSpace(ProjectXApiBaseUrl))
            {
                LogProjectXStatus("ProjectX login failed | reason=empty-base-url");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(projectXSessionToken) &&
                (DateTime.UtcNow - projectXTokenAcquiredUtc).TotalHours < 23)
                return true;

            if (string.IsNullOrWhiteSpace(ProjectXUsername) || string.IsNullOrWhiteSpace(ProjectXApiKey))
            {
                LogProjectXStatus("ProjectX login failed | reason=missing-credentials");
                return false;
            }

            string loginJson = string.Format(CultureInfo.InvariantCulture,
                "{{\"userName\":\"{0}\",\"apiKey\":\"{1}\"}}",
                ProjectXUsername,
                ProjectXApiKey);

            string response = ProjectXPost("/api/Auth/loginKey", loginJson, false, true);
            if (string.IsNullOrWhiteSpace(response))
            {
                LogProjectXStatus("ProjectX login failed | reason=empty-response");
                return false;
            }

            string token;
            if (!TryGetJsonString(response, "token", out token))
            {
                LogProjectXStatus("ProjectX login failed | reason=missing-token");
                return false;
            }

            projectXSessionToken = token;
            projectXTokenAcquiredUtc = DateTime.UtcNow;
            projectXAccounts = null;
            projectXLastOrderIds.Clear();
            projectXResolvedContractId = null;
            projectXResolvedInstrumentKey = string.Empty;
            LogProjectXStatus("ProjectX login succeeded");
            return true;
        }

        private bool TryGetProjectXTargets(out List<ProjectXAccountInfo> targetAccounts, out string contractId)
        {
            targetAccounts = null;
            contractId = null;

            if (!TryResolveProjectXContractId(out contractId))
            {
                return false;
            }

            List<ProjectXAccountInfo> accounts;
            if (!TryLoadProjectXAccounts(out accounts))
            {
                return false;
            }

            var selectors = ParseProjectXAccountSelectors(ProjectXAccountId);
            if (selectors.Count == 0)
            {
                LogProjectXStatus("ProjectX warning | no webhooks will be sent because ProjectX Accounts is empty.");
                LogProjectXDiscovery("ProjectX account selection failed | reason=no-selection");
                return false;
            }

            var matchedAccounts = new List<ProjectXAccountInfo>();
            var matchedIds = new HashSet<int>();
            var unmatchedSelectors = new List<string>();

            foreach (string selector in selectors)
            {
                int accountId;
                List<ProjectXAccountInfo> matches = int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out accountId)
                    ? accounts.Where(a => a.CanTrade && a.Id == accountId).ToList()
                    : accounts.Where(a => a.CanTrade && string.Equals(a.Name ?? string.Empty, selector, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matches.Count == 0)
                {
                    unmatchedSelectors.Add(selector);
                    continue;
                }

                foreach (var match in matches)
                {
                    if (matchedIds.Add(match.Id))
                        matchedAccounts.Add(match);
                }
            }

            if (unmatchedSelectors.Count > 0)
            {
                LogProjectXDiscovery(string.Format(
                    "ProjectX account selection unmatched | selectors={0}",
                    string.Join(", ", unmatchedSelectors.ToArray())));
            }

            if (matchedAccounts.Count == 0)
            {
                LogProjectXDiscovery("ProjectX account selection failed | reason=no-matching-tradable-accounts");
                return false;
            }

            targetAccounts = matchedAccounts;
            return true;
        }

        private bool TryLoadProjectXAccounts(out List<ProjectXAccountInfo> accounts)
        {
            if (projectXAccounts != null && projectXAccounts.Count > 0)
            {
                accounts = projectXAccounts;
                return true;
            }

            string response = ProjectXPost("/api/Account/search", "{\"onlyActiveAccounts\":true}", true, true);
            if (string.IsNullOrWhiteSpace(response))
            {
                LogProjectXStatus("ProjectX warning | no webhooks will be sent because no ProjectX accounts were found.");
                LogProjectXDiscovery("ProjectX account load failed | reason=empty-response");
                accounts = null;
                return false;
            }

            accounts = ExtractProjectXAccounts(response).ToList();
            projectXAccounts = accounts.Count > 0 ? accounts : null;

            LogProjectXStatus(string.Format("ProjectX accounts found | count={0}", accounts.Count));
            if (accounts.Count == 0)
            {
                LogProjectXStatus("ProjectX warning | no webhooks will be sent because no ProjectX accounts were found.");
                return false;
            }

            foreach (var account in accounts)
            {
                LogProjectXStatus(string.Format(
                    "ProjectX account | id={0} name={1} canTrade={2} isVisible={3}",
                    account.Id,
                    account.Name ?? string.Empty,
                    account.CanTrade,
                    account.IsVisible));
            }

            return true;
        }

        private bool TryResolveProjectXContractId(out string contractId)
        {
            contractId = null;

            string instrumentKey = GetProjectXInstrumentKey();
            if (!string.IsNullOrWhiteSpace(projectXResolvedContractId) &&
                string.Equals(projectXResolvedInstrumentKey, instrumentKey, StringComparison.OrdinalIgnoreCase))
            {
                contractId = projectXResolvedContractId;
                return true;
            }

            string root = GetProjectXInstrumentRoot();
            if (string.IsNullOrWhiteSpace(root))
            {
                LogProjectXDiscovery("ProjectX contract resolve failed | reason=empty-instrument-root");
                return false;
            }

            DateTime expiry;
            string desiredSuffix = TryGetInstrumentExpiry(out expiry) || TryParseInstrumentExpiryFromFullName(out expiry)
                ? GetProjectXFuturesMonthCode(expiry.Month) + expiry.ToString("yy", CultureInfo.InvariantCulture)
                : string.Empty;

            List<ProjectXContractInfo> contracts;
            if (!TrySearchProjectXContracts(root, desiredSuffix, out contracts))
                return false;

            ProjectXContractInfo selected = SelectProjectXContract(root, desiredSuffix, contracts);
            if (selected == null || string.IsNullOrWhiteSpace(selected.Id))
            {
                LogProjectXDiscovery(string.Format(
                    "ProjectX contract resolve failed | root={0} desiredSuffix={1} candidates={2}",
                    root,
                    string.IsNullOrWhiteSpace(desiredSuffix) ? "active" : desiredSuffix,
                    string.Join(", ", contracts.Select(c => c.Id ?? string.Empty).ToArray())));
                return false;
            }

            contractId = selected.Id;
            projectXResolvedContractId = contractId;
            projectXResolvedInstrumentKey = instrumentKey;
            LogProjectXDiscovery(string.Format(
                "ProjectX contract resolved | instrument={0} root={1} desiredSuffix={2} contractId={3} name={4} active={5}",
                instrumentKey,
                root,
                string.IsNullOrWhiteSpace(desiredSuffix) ? "active" : desiredSuffix,
                selected.Id,
                selected.Name ?? string.Empty,
                selected.ActiveContract));
            return true;
        }

        private bool TrySearchProjectXContracts(string root, string desiredSuffix, out List<ProjectXContractInfo> contracts)
        {
            contracts = null;

            string primarySearchText = !string.IsNullOrWhiteSpace(desiredSuffix) ? root + desiredSuffix : root;
            if (TrySearchProjectXContractsByText(primarySearchText, root, out contracts) && contracts.Count > 0)
                return true;

            if (!string.Equals(primarySearchText, root, StringComparison.OrdinalIgnoreCase) &&
                TrySearchProjectXContractsByText(root, root, out contracts) && contracts.Count > 0)
                return true;

            LogProjectXDiscovery(string.Format(
                "ProjectX contract search failed | root={0} desiredSuffix={1}",
                root,
                string.IsNullOrWhiteSpace(desiredSuffix) ? "active" : desiredSuffix));
            return false;
        }

        private bool TrySearchProjectXContractsByText(string searchText, string root, out List<ProjectXContractInfo> contracts)
        {
            if (TrySearchProjectXContractsByText(searchText, root, true, out contracts) && contracts.Count > 0)
                return true;

            if (TrySearchProjectXContractsByText(searchText, root, false, out contracts) && contracts.Count > 0)
                return true;

            contracts = new List<ProjectXContractInfo>();
            return false;
        }

        private bool TrySearchProjectXContractsByText(string searchText, string root, bool live, out List<ProjectXContractInfo> contracts)
        {
            string requestJson = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"live\":{0},\"searchText\":\"{1}\"}}",
                live ? "true" : "false",
                searchText);
            string response = ProjectXPost("/api/Contract/search", requestJson, true, true);
            contracts = ExtractProjectXContracts(response)
                .Where(c => DoesProjectXContractMatchRoot(c, root))
                .ToList();

            LogProjectXDiscovery(string.Format(
                "ProjectX contract search | searchText={0} live={1} matches={2}",
                searchText,
                live,
                contracts.Count));
            return !string.IsNullOrWhiteSpace(response);
        }

        private ProjectXContractInfo SelectProjectXContract(string root, string desiredSuffix, List<ProjectXContractInfo> contracts)
        {
            if (contracts == null || contracts.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(desiredSuffix))
            {
                var exactMatches = contracts
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id) &&
                        c.Id.EndsWith("." + desiredSuffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (exactMatches.Count > 0)
                    return exactMatches.FirstOrDefault(c => c.ActiveContract) ?? exactMatches[0];
            }

            var activeMatches = contracts.Where(c => c.ActiveContract).ToList();
            if (activeMatches.Count > 0)
                return activeMatches[0];

            return contracts[0];
        }

        private bool DoesProjectXContractMatchRoot(ProjectXContractInfo contract, string root)
        {
            if (contract == null || string.IsNullOrWhiteSpace(root))
                return false;

            string expectedSymbolId = "F.US." + root;
            if (!string.IsNullOrWhiteSpace(contract.SymbolId) &&
                string.Equals(contract.SymbolId, expectedSymbolId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(contract.Id) &&
                contract.Id.IndexOf(".US." + root + ".", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrWhiteSpace(contract.Name) &&
                contract.Name.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private string GetProjectXInstrumentKey()
        {
            if (Instrument == null)
                return string.Empty;

            string fullName = Instrument.FullName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(fullName))
                return fullName.Trim().ToUpperInvariant();

            return GetProjectXInstrumentRoot();
        }

        private string GetProjectXInstrumentRoot()
        {
            return Instrument != null && Instrument.MasterInstrument != null
                ? (Instrument.MasterInstrument.Name ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
        }

        private bool TryGetInstrumentExpiry(out DateTime expiry)
        {
            expiry = Core.Globals.MinDate;
            if (Instrument == null)
                return false;

            try
            {
                PropertyInfo property = Instrument.GetType().GetProperty("Expiry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (property == null)
                    return false;

                object raw = property.GetValue(Instrument, null);
                if (!(raw is DateTime))
                    return false;

                DateTime dt = (DateTime)raw;
                if (dt.Year < 2000)
                    return false;

                expiry = dt;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseInstrumentExpiryFromFullName(out DateTime expiry)
        {
            expiry = Core.Globals.MinDate;
            string fullName = Instrument != null ? (Instrument.FullName ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;
            if (string.IsNullOrWhiteSpace(fullName))
                return false;

            Match match = Regex.Match(fullName, @"\b(?<month>\d{1,2})[-/](?<year>\d{2,4})\b");
            if (!match.Success)
                return false;

            int month;
            int year;
            if (!int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out month) ||
                !int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
                return false;

            if (year < 100)
                year += 2000;
            if (month < 1 || month > 12 || year < 2000)
                return false;

            expiry = new DateTime(year, month, 1);
            return true;
        }

        private string GetProjectXFuturesMonthCode(int month)
        {
            switch (month)
            {
                case 1: return "F";
                case 2: return "G";
                case 3: return "H";
                case 4: return "J";
                case 5: return "K";
                case 6: return "M";
                case 7: return "N";
                case 8: return "Q";
                case 9: return "U";
                case 10: return "V";
                case 11: return "X";
                case 12: return "Z";
                default: return string.Empty;
            }
        }

        private List<string> ParseProjectXAccountSelectors(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string FormatProjectXAccountsForLog(IEnumerable<ProjectXAccountInfo> accounts)
        {
            if (accounts == null)
                return "<none>";

            var items = accounts
                .Select(a => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1}",
                    a.Id,
                    a.Name ?? string.Empty))
                .ToArray();
            return items.Length > 0 ? string.Join(", ", items) : "<none>";
        }

        private string GetProjectXOrderKey(int accountId, string contractId)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}", accountId, contractId ?? string.Empty);
        }

        private string ProjectXPlaceOrder(string side, int accountId, string contractId, double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            int orderSide = side.Equals("buy", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int orderType = isMarketEntry ? 2 : 1;
            int normalizedQuantity = Math.Max(1, quantity);
            double entry = Instrument.MasterInstrument.RoundToTickSize(entryPrice);
            bool isLong = orderSide == 0;
            int tpTicks = NormalizeProjectXBracketTicks(
                PriceToTicks(takeProfit - entry),
                4,
                isLong ? 1 : -1);
            int slTicks = NormalizeProjectXBracketTicks(
                PriceToTicks(stopLoss - entry),
                1,
                isLong ? -1 : 1);

            string limitPart = isMarketEntry
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, ",\"limitPrice\":{0}", entry);

            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\",\"type\":{2},\"side\":{3},\"size\":{4}{5},\"takeProfitBracket\":{{\"quantity\":{6},\"type\":1,\"ticks\":{7}}},\"stopLossBracket\":{{\"quantity\":{6},\"type\":4,\"ticks\":{8}}}}}",
                accountId,
                contractId,
                orderType,
                orderSide,
                normalizedQuantity,
                limitPart,
                normalizedQuantity,
                tpTicks,
                slTicks);

            string response = ProjectXPost("/api/Order/place", json, true);
            long orderId;
            if (TryGetJsonLong(response, "orderId", out orderId))
                projectXLastOrderIds[GetProjectXOrderKey(accountId, contractId)] = orderId;

            return response;
        }

        private int NormalizeProjectXBracketTicks(int rawTicks, int minAbsTicks, int zeroTickDirection)
        {
            int direction = rawTicks == 0 ? Math.Sign(zeroTickDirection) : Math.Sign(rawTicks);
            int absTicks = Math.Abs(rawTicks);
            if (absTicks < minAbsTicks)
                absTicks = minAbsTicks;
            return direction * absTicks;
        }

        private bool ProjectXPrepareForEntry(int accountId, string contractId)
        {
            ProjectXCancelOrders(accountId, contractId);

            if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
            {
                LogDebug(string.Format(
                    "ProjectX prepare failed | stage=cancel-clear accountId={0} contractId={1}",
                    accountId,
                    contractId));
                return false;
            }

            int positionSize;
            if (TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize) && positionSize != 0)
            {
                ProjectXClosePosition(accountId, contractId);

                if (!WaitForProjectXFlat(accountId, contractId, 4000))
                {
                    LogDebug(string.Format(
                        "ProjectX prepare failed | stage=flat accountId={0} contractId={1} positionSize={2}",
                        accountId,
                        contractId,
                        positionSize));
                    return false;
                }

                ProjectXCancelOrders(accountId, contractId);
                if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
                {
                    LogDebug(string.Format(
                        "ProjectX prepare failed | stage=post-close-cancel accountId={0} contractId={1}",
                        accountId,
                        contractId));
                    return false;
                }
            }

            return true;
        }

        private void ProjectXFlattenPosition(int accountId, string contractId)
        {
            ProjectXCancelOrders(accountId, contractId);
            if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
            {
                LogDebug(string.Format(
                    "ProjectX flatten warning | stage=cancel-clear accountId={0} contractId={1}",
                    accountId,
                    contractId));
            }

            int positionSize;
            if (TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize) && positionSize != 0)
            {
                ProjectXClosePosition(accountId, contractId);
                if (!WaitForProjectXFlat(accountId, contractId, 4000))
                {
                    LogDebug(string.Format(
                        "ProjectX flatten warning | stage=flat accountId={0} contractId={1} positionSize={2}",
                        accountId,
                        contractId,
                        positionSize));
                }
            }

            ProjectXCancelOrders(accountId, contractId);
            if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
            {
                LogDebug(string.Format(
                    "ProjectX flatten warning | stage=post-close-cancel accountId={0} contractId={1}",
                    accountId,
                    contractId));
            }
        }

        private string ProjectXClosePosition(int accountId, string contractId)
        {
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\"}}",
                accountId,
                contractId);
            return ProjectXPost("/api/Position/closeContract", json, true);
        }

        private void ProjectXCancelOrders(int accountId, string contractId)
        {
            foreach (long orderId in GetProjectXOpenOrderIds(accountId, contractId))
            {
                string cancelJson = string.Format(CultureInfo.InvariantCulture,
                    "{{\"accountId\":{0},\"orderId\":{1}}}",
                    accountId,
                    orderId);
                ProjectXPost("/api/Order/cancel", cancelJson, true);
            }

            projectXLastOrderIds.Remove(GetProjectXOrderKey(accountId, contractId));
        }

        private bool WaitForProjectXFlat(int accountId, string contractId, int timeoutMs)
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow <= deadlineUtc)
            {
                int positionSize;
                if (TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize) && positionSize == 0)
                    return true;

                System.Threading.Thread.Sleep(150);
            }

            return false;
        }

        private bool WaitForProjectXOrdersCleared(int accountId, string contractId, int timeoutMs)
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow <= deadlineUtc)
            {
                if (GetProjectXOpenOrderIds(accountId, contractId).Count == 0)
                    return true;

                System.Threading.Thread.Sleep(150);
            }

            return false;
        }

        private List<long> GetProjectXOpenOrderIds(int accountId, string contractId)
        {
            var orderIds = new List<long>();
            string searchJson = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string searchResponse = ProjectXPost("/api/Order/searchOpen", searchJson, true);
            bool success;
            if (TryGetJsonBool(searchResponse, "success", out success) && !success)
                return orderIds;

            foreach (var order in ExtractProjectXOrders(searchResponse))
            {
                object contractObj;
                if (!order.TryGetValue("contractId", out contractObj))
                    continue;
                if (!string.Equals(contractObj != null ? contractObj.ToString() : string.Empty, contractId, StringComparison.OrdinalIgnoreCase))
                    continue;

                object idObj;
                long id;
                if (!order.TryGetValue("id", out idObj) || !TryConvertToLong(idObj, out id) || id <= 0)
                    continue;

                orderIds.Add(id);
            }

            return orderIds;
        }

        private bool TryGetProjectXOpenPositionSize(int accountId, string contractId, out int signedSize)
        {
            signedSize = 0;
            string searchJson = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string searchResponse = ProjectXPost("/api/Position/searchOpen", searchJson, true);
            bool success;
            if (TryGetJsonBool(searchResponse, "success", out success) && !success)
                return false;

            foreach (var position in ExtractProjectXPositions(searchResponse))
            {
                object contractObj;
                if (!position.TryGetValue("contractId", out contractObj))
                    continue;
                if (!string.Equals(contractObj != null ? contractObj.ToString() : string.Empty, contractId, StringComparison.OrdinalIgnoreCase))
                    continue;

                object typeObj;
                object sizeObj;
                int type;
                int size;
                if (!position.TryGetValue("type", out typeObj) || !TryConvertToInt(typeObj, out type))
                    continue;
                if (!position.TryGetValue("size", out sizeObj) || !TryConvertToInt(sizeObj, out size) || size <= 0)
                    continue;

                signedSize += type == 2 ? -size : size;
            }

            return true;
        }

        private string ProjectXPost(string path, string json, bool requiresAuth)
        {
            return ProjectXPost(path, json, requiresAuth, false);
        }

        private string ProjectXPost(string path, string json, bool requiresAuth, bool alwaysLog)
        {
            string baseUrl = ProjectXApiBaseUrl != null ? ProjectXApiBaseUrl.TrimEnd('/') : string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            if (alwaysLog)
                LogProjectXDiscovery(string.Format(
                    "ProjectX request | url={0}{1} auth={2} payload={3}",
                    baseUrl,
                    path,
                    requiresAuth,
                    SanitizeProjectXJsonForLog(json)));
            else
                LogDebug(string.Format(
                    "ProjectX request | url={0}{1} auth={2} payload={3}",
                    baseUrl,
                    path,
                    requiresAuth,
                    SanitizeProjectXJsonForLog(json)));

            try
            {
                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    if (requiresAuth && !string.IsNullOrWhiteSpace(projectXSessionToken))
                        client.Headers[System.Net.HttpRequestHeader.Authorization] = "Bearer " + projectXSessionToken;

                    string response = client.UploadString(baseUrl + path, "POST", json);
                    if (alwaysLog)
                        LogProjectXDiscovery(string.Format(
                            "ProjectX response | url={0}{1} body={2}",
                            baseUrl,
                            path,
                            SanitizeProjectXJsonForLog(response)));
                    else
                        LogDebug(string.Format(
                            "ProjectX response | url={0}{1} body={2}",
                            baseUrl,
                            path,
                            SanitizeProjectXJsonForLog(response)));
                    return response;
                }
            }
            catch (System.Net.WebException ex)
            {
                string errorBody = ReadWebExceptionResponse(ex);
                if (alwaysLog)
                    LogProjectXDiscovery(string.Format(
                        "ProjectX request failed | url={0}{1} error={2} body={3}",
                        baseUrl,
                        path,
                        ex.Message,
                        SanitizeProjectXJsonForLog(errorBody)));
                else
                    LogDebug(string.Format(
                        "ProjectX request failed | url={0}{1} error={2} body={3}",
                        baseUrl,
                        path,
                        ex.Message,
                        SanitizeProjectXJsonForLog(errorBody)));
                return errorBody;
            }
            catch (Exception ex)
            {
                if (alwaysLog)
                    LogProjectXDiscovery(string.Format("ProjectX request failed | url={0}{1} error={2}", baseUrl, path, ex.Message));
                else
                    LogDebug(string.Format("ProjectX request failed | url={0}{1} error={2}", baseUrl, path, ex.Message));
                return null;
            }
        }

        private string ReadWebExceptionResponse(System.Net.WebException ex)
        {
            if (ex == null || ex.Response == null)
                return null;

            try
            {
                using (var stream = ex.Response.GetResponseStream())
                {
                    if (stream == null)
                        return null;
                    using (var reader = new System.IO.StreamReader(stream))
                        return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private string SanitizeProjectXJsonForLog(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            string sanitized = json;
            sanitized = RedactProjectXJsonValue(sanitized, "apiKey");
            sanitized = RedactProjectXJsonValue(sanitized, "loginKey");
            sanitized = RedactProjectXJsonValue(sanitized, "token");
            sanitized = RedactProjectXJsonValue(sanitized, "newToken");
            return sanitized;
        }

        private string RedactProjectXJsonValue(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return json ?? string.Empty;

            return Regex.Replace(
                json,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"[^\"]*\"",
                "\"" + key + "\":\"***\"");
        }

        private int PriceToTicks(double priceDistance)
        {
            if (TickSize <= 0.0)
                return 0;
            return (int)Math.Round(priceDistance / TickSize, MidpointRounding.AwayFromZero);
        }

        private bool TryGetJsonString(string json, string key, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);
                object raw;
                if (data == null || !data.TryGetValue(key, out raw) || raw == null)
                    return false;
                value = raw.ToString();
                return !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetJsonInt(string json, string key, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);
                object raw;
                if (data == null || !data.TryGetValue(key, out raw) || raw == null)
                    return false;
                return int.TryParse(raw.ToString(), out value);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetJsonLong(string json, string key, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);
                object raw;
                if (data == null || !data.TryGetValue(key, out raw) || raw == null)
                    return false;
                return TryConvertToLong(raw, out value);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetJsonBool(string json, string key, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);
                object raw;
                if (data == null || !data.TryGetValue(key, out raw) || raw == null)
                    return false;
                return TryConvertToBool(raw, out value);
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<ProjectXAccountInfo> ExtractProjectXAccounts(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                yield break;

            var serializer = new JavaScriptSerializer();
            Dictionary<string, object> data;
            try
            {
                data = serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                yield break;
            }

            object raw;
            if (data == null || !data.TryGetValue("accounts", out raw) || raw == null)
                yield break;

            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null)
                    continue;

                object idObj;
                int id;
                if (!dict.TryGetValue("id", out idObj) || !TryConvertToInt(idObj, out id) || id <= 0)
                    continue;

                object nameObj;
                object canTradeObj;
                object isVisibleObj;
                bool canTrade;
                bool isVisible;

                dict.TryGetValue("name", out nameObj);
                dict.TryGetValue("canTrade", out canTradeObj);
                dict.TryGetValue("isVisible", out isVisibleObj);
                TryConvertToBool(canTradeObj, out canTrade);
                TryConvertToBool(isVisibleObj, out isVisible);

                yield return new ProjectXAccountInfo
                {
                    Id = id,
                    Name = nameObj != null ? nameObj.ToString() : string.Empty,
                    CanTrade = canTrade,
                    IsVisible = isVisible
                };
            }
        }

        private IEnumerable<ProjectXContractInfo> ExtractProjectXContracts(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                yield break;

            var serializer = new JavaScriptSerializer();
            Dictionary<string, object> data;
            try
            {
                data = serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                yield break;
            }

            object raw;
            if (data == null || !data.TryGetValue("contracts", out raw) || raw == null)
                yield break;

            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null)
                    continue;

                object idObj;
                if (!dict.TryGetValue("id", out idObj) || idObj == null)
                    continue;

                object nameObj;
                object descriptionObj;
                object symbolIdObj;
                object activeObj;
                bool activeContract;

                dict.TryGetValue("name", out nameObj);
                dict.TryGetValue("description", out descriptionObj);
                dict.TryGetValue("symbolId", out symbolIdObj);
                dict.TryGetValue("activeContract", out activeObj);
                TryConvertToBool(activeObj, out activeContract);

                yield return new ProjectXContractInfo
                {
                    Id = idObj.ToString(),
                    Name = nameObj != null ? nameObj.ToString() : string.Empty,
                    Description = descriptionObj != null ? descriptionObj.ToString() : string.Empty,
                    SymbolId = symbolIdObj != null ? symbolIdObj.ToString() : string.Empty,
                    ActiveContract = activeContract
                };
            }
        }

        private bool TryConvertToInt(object raw, out int value)
        {
            value = 0;
            if (raw == null)
                return false;

            if (raw is int)
            {
                value = (int)raw;
                return true;
            }

            if (raw is long)
            {
                long longValue = (long)raw;
                if (longValue < int.MinValue || longValue > int.MaxValue)
                    return false;
                value = (int)longValue;
                return true;
            }

            if (raw is decimal)
            {
                value = (int)(decimal)raw;
                return true;
            }

            if (raw is double)
            {
                value = (int)(double)raw;
                return true;
            }

            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToLong(object raw, out long value)
        {
            value = 0;
            if (raw == null)
                return false;

            if (raw is int)
            {
                value = (int)raw;
                return true;
            }

            if (raw is long)
            {
                value = (long)raw;
                return true;
            }

            if (raw is decimal)
            {
                decimal decimalValue = (decimal)raw;
                if (decimalValue < long.MinValue || decimalValue > long.MaxValue)
                    return false;
                value = (long)decimalValue;
                return true;
            }

            if (raw is double)
            {
                double doubleValue = (double)raw;
                if (doubleValue < long.MinValue || doubleValue > long.MaxValue)
                    return false;
                value = (long)doubleValue;
                return true;
            }

            return long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToBool(object raw, out bool value)
        {
            value = false;
            if (raw == null)
                return false;

            if (raw is bool)
            {
                value = (bool)raw;
                return true;
            }

            return bool.TryParse(raw.ToString(), out value);
        }

        private IEnumerable<Dictionary<string, object>> ExtractProjectXOrders(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                yield break;

            var serializer = new JavaScriptSerializer();
            Dictionary<string, object> data;
            try
            {
                data = serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                yield break;
            }

            object raw;
            if (data == null || !data.TryGetValue("orders", out raw) || raw == null)
                yield break;

            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var dict = item as Dictionary<string, object>;
                if (dict != null)
                    yield return dict;
            }
        }

        private IEnumerable<Dictionary<string, object>> ExtractProjectXPositions(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                yield break;

            var serializer = new JavaScriptSerializer();
            Dictionary<string, object> data;
            try
            {
                data = serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                yield break;
            }

            object raw;
            if (data == null || !data.TryGetValue("positions", out raw) || raw == null)
                yield break;

            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var dict = item as Dictionary<string, object>;
                if (dict != null)
                    yield return dict;
            }
        }

        [NinjaScriptProperty]
        [Display(Name = "Asia 1 Session(18:30-20:00)", Description = "Enable trading logic during the Asia 1 time window.", GroupName = "Asia 1", Order = 0)]
        public bool UseAsiaSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "Asia 1 session start time in chart time zone.", GroupName = "Asia 1", Order = 1)]
        public TimeSpan AsiaSessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "Asia 1 session end time in chart time zone.", GroupName = "Asia 1", Order = 2)]
        public TimeSpan AsiaSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Block Sunday Trades", Description = "If enabled, block new Asia 1 entries/flips on Sundays.", GroupName = "Asia 1", Order = 3)]
        public bool AsiaBlockSundayTrades { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Asia 1 entry and exit logic.", GroupName = "Asia 1", Order = 4)]
        public int AsiaEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for Asia 1 entries.", GroupName = "Asia 1", Order = 5)]
        public int AsiaContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trade Direction", Description = "Select whether Asia 1 can take long, short, or both directions.", GroupName = "Asia 1", Order = 6)]
        public SessionTradeDirection AsiaTradeDirection { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Asia 1 trend filter.", GroupName = "Asia 1", Order = 10)]
        public int AsiaAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Asia 1 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "Asia 1", Order = 11)]
        public double AsiaAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, Asia 1 flips are allowed only when ADX is greater than or equal to this value.", GroupName = "Asia 1", Order = 7)]
        public double AsiaFlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Asia 1 entries are allowed only when ADX is less than or equal to this value.", GroupName = "Asia 1", Order = 12)]
        public double AsiaAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        // [TypeConverter(typeof(AsiaAdxSlopeDropdownConverter))]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "Asia 1", Order = 13)]
        public double AsiaAdxMinSlopePoints
        {
            get { return asiaAdxMinSlopePoints; }
            set { asiaAdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "Asia 1", Order = 14)]
        public double AsiaAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "Asia 1", Order = 8)]
        public double AsiaEmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "Asia 1", Order = 9)]
        public double AsiaMaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "Asia 1", Order = 15)]
        public double AsiaAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "Asia 1", Order = 16)]
        public double AsiaStopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "Asia 1", Order = 17)]
        public double AsiaExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "Asia 1", Order = 18)]
        public double AsiaTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Offset Points", Description = "0 = market entry at signal close. Positive value = limit entry offset from signal close (long: close-offset, short: close+offset).", GroupName = "Asia 1", Order = 22)]
        public double AsiaEntryOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Flip BE Trigger", Description = "If enabled, flip entries can move stop loss to break-even after the configured profit threshold is reached.", GroupName = "Asia 1", Order = 23)]
        public bool AsiaEnableFlipBreakEven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip BE Trigger (Points)", Description = "Only used for flip entries when Enable Flip BE Trigger is on. At this unrealized profit in points, stop loss moves to break-even.", GroupName = "Asia 1", Order = 24)]
        public double AsiaFlipBreakEvenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip TP (Points)", Description = "0 uses the session take profit. Greater than 0 overrides take profit only for flip entries.", GroupName = "Asia 1", Order = 25)]
        public double AsiaFlipTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Exit Cross Points", Description = "0 uses Exit Cross Points. When an open trade reaches the normal exit cross, require this many points beyond EMA before reversing to the opposite side.", GroupName = "Asia 1", Order = 26)]
        public double AsiaFlipEmaCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Trigger", Description = "Percent of the active take-profit distance required before the stop move arms. Example: 75 = trigger after price reaches 75% of TP distance.", GroupName = "Asia 1", Order = 27)]
        public double AsiaTakeProfitPercentTriggerPercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "TP % Stop Mode", Description = "After TP % Trigger is reached, either move the stop to TP % Stop Move once or trail by ATR(14) x multiplier.", GroupName = "Asia 1", Order = 28)]
        public TakeProfitStopMode AsiaTakeProfitStopMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "TP % ATR Trail Multiplier", Description = "Only used when TP % Stop Mode is AtrTrail. Trail stop at current close minus/plus ATR(14) x this multiplier.", GroupName = "Asia 1", Order = 29)]
        public double AsiaTakeProfitAtrTrailMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Stop Move", Description = "Only used when TP % Stop Mode is PercentMove. Move stop to this percent of the active take-profit distance from entry. Example: 50 = halfway from entry to TP, 0 = break-even.", GroupName = "Asia 1", Order = 30)]
        public double AsiaTakeProfitPercentStopMovePercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ADX Require Min For Flips (FLIP)", Description = "If enabled, flips are blocked while ADX is below the active session minimum ADX threshold line.", GroupName = "Asia 1", Order = 31)]
        public bool AsiaRequireMinAdxForFlips { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX DD Risk Mode", Description = "If enabled, ADX peak drawdown trigger arms a defensive bracket instead of immediate ADX drawdown exit.", GroupName = "Asia 1", Order = 32)]
        public bool AsiaEnableAdxDdRiskMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk SL (Points)", Description = "0 disables stop adjustment. When ADX DD risk mode arms, set stop to avg entry minus/plus this many points.", GroupName = "Asia 1", Order = 33)]
        public double AsiaAdxDdRiskModeStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk TP (Points)", Description = "0 disables target adjustment. When ADX DD risk mode arms, set take profit distance to this many points from avg entry.", GroupName = "Asia 1", Order = 34)]
        public double AsiaAdxDdRiskModeTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Horizontal Exit Bars", Description = "0 disables. Close an open trade once it has been held for this many closed 5-minute bars since entry.", GroupName = "Asia 1", Order = 35)]
        public int AsiaHorizontalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max SL Points", Description = "0 disables. If the planned entry-to-stop distance is greater than this value, the trade is not placed.", GroupName = "Asia 1", Order = 36)]
        public double AsiaMaxStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new Asia 1 entries and flips while ATR(14) is below this value.", GroupName = "Asia 1", Order = 37)]
        public double AsiaAtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Asia 2 Session(20:00-23:59)", Description = "Enable trading logic during the Asia 2 time window.", GroupName = "Asia 2", Order = 0)]
        public bool UseAsia2Session { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "Asia 2 session start time in chart time zone.", GroupName = "Asia 2", Order = 1)]
        public TimeSpan Asia2SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "Asia 2 session end time in chart time zone.", GroupName = "Asia 2", Order = 2)]
        public TimeSpan Asia2SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Block Sunday Trades", Description = "If enabled, block new Asia 2 entries/flips on Sundays.", GroupName = "Asia 2", Order = 3)]
        public bool Asia2BlockSundayTrades { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Asia 2 entry and exit logic.", GroupName = "Asia 2", Order = 4)]
        public int Asia2EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for Asia 2 entries.", GroupName = "Asia 2", Order = 5)]
        public int Asia2Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trade Direction", Description = "Select whether Asia 2 can take long, short, or both directions.", GroupName = "Asia 2", Order = 6)]
        public SessionTradeDirection Asia2TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Asia 2 trend filter.", GroupName = "Asia 2", Order = 10)]
        public int Asia2AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Asia 2 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "Asia 2", Order = 11)]
        public double Asia2AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, Asia 2 flips are allowed only when ADX is greater than or equal to this value.", GroupName = "Asia 2", Order = 7)]
        public double Asia2FlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Asia 2 entries are allowed only when ADX is less than or equal to this value.", GroupName = "Asia 2", Order = 12)]
        public double Asia2AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        // [TypeConverter(typeof(Asia2AdxSlopeDropdownConverter))]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "Asia 2", Order = 13)]
        public double Asia2AdxMinSlopePoints
        {
            get { return asia2AdxMinSlopePoints; }
            set { asia2AdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "Asia 2", Order = 14)]
        public double Asia2AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "Asia 2", Order = 8)]
        public double Asia2EmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "Asia 2", Order = 9)]
        public double Asia2MaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "Asia 2", Order = 15)]
        public double Asia2AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "Asia 2", Order = 16)]
        public double Asia2StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "Asia 2", Order = 17)]
        public double Asia2ExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "Asia 2", Order = 18)]
        public double Asia2TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Offset Points", Description = "0 = market entry at signal close. Positive value = limit entry offset from signal close (long: close-offset, short: close+offset).", GroupName = "Asia 2", Order = 22)]
        public double Asia2EntryOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Flip BE Trigger", Description = "If enabled, flip entries can move stop loss to break-even after the configured profit threshold is reached.", GroupName = "Asia 2", Order = 23)]
        public bool Asia2EnableFlipBreakEven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip BE Trigger (Points)", Description = "Only used for flip entries when Enable Flip BE Trigger is on. At this unrealized profit in points, stop loss moves to break-even.", GroupName = "Asia 2", Order = 24)]
        public double Asia2FlipBreakEvenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip TP (Points)", Description = "0 uses the session take profit. Greater than 0 overrides take profit only for flip entries.", GroupName = "Asia 2", Order = 25)]
        public double Asia2FlipTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Exit Cross Points", Description = "0 uses Exit Cross Points. When an open trade reaches the normal exit cross, require this many points beyond EMA before reversing to the opposite side.", GroupName = "Asia 2", Order = 26)]
        public double Asia2FlipEmaCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Trigger", Description = "Percent of the active take-profit distance required before the stop move arms. Example: 75 = trigger after price reaches 75% of TP distance.", GroupName = "Asia 2", Order = 27)]
        public double Asia2TakeProfitPercentTriggerPercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "TP % Stop Mode", Description = "After TP % Trigger is reached, either move the stop to TP % Stop Move once or trail by ATR(14) x multiplier.", GroupName = "Asia 2", Order = 28)]
        public TakeProfitStopMode Asia2TakeProfitStopMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "TP % ATR Trail Multiplier", Description = "Only used when TP % Stop Mode is AtrTrail. Trail stop at current close minus/plus ATR(14) x this multiplier.", GroupName = "Asia 2", Order = 29)]
        public double Asia2TakeProfitAtrTrailMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Stop Move", Description = "Only used when TP % Stop Mode is PercentMove. Move stop to this percent of the active take-profit distance from entry. Example: 50 = halfway from entry to TP, 0 = break-even.", GroupName = "Asia 2", Order = 30)]
        public double Asia2TakeProfitPercentStopMovePercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ADX Require Min For Flips (FLIP)", Description = "If enabled, flips are blocked while ADX is below the active session minimum ADX threshold line.", GroupName = "Asia 2", Order = 31)]
        public bool Asia2RequireMinAdxForFlips { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX DD Risk Mode", Description = "If enabled, ADX peak drawdown trigger arms a defensive bracket instead of immediate ADX drawdown exit.", GroupName = "Asia 2", Order = 32)]
        public bool Asia2EnableAdxDdRiskMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk SL (Points)", Description = "0 disables stop adjustment. When ADX DD risk mode arms, set stop to avg entry minus/plus this many points.", GroupName = "Asia 2", Order = 33)]
        public double Asia2AdxDdRiskModeStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk TP (Points)", Description = "0 disables target adjustment. When ADX DD risk mode arms, set take profit distance to this many points from avg entry.", GroupName = "Asia 2", Order = 34)]
        public double Asia2AdxDdRiskModeTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Horizontal Exit Bars", Description = "0 disables. Close an open trade once it has been held for this many closed 5-minute bars since entry.", GroupName = "Asia 2", Order = 35)]
        public int Asia2HorizontalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max SL Points", Description = "0 disables. If the planned entry-to-stop distance is greater than this value, the trade is not placed.", GroupName = "Asia 2", Order = 36)]
        public double Asia2MaxStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new Asia 2 entries and flips while ATR(14) is below this value.", GroupName = "Asia 2", Order = 37)]
        public double Asia2AtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Asia 3 Session(00:00-02:00)", Description = "Enable trading logic during the Asia 3 time window.", GroupName = "Asia 3", Order = 0)]
        public bool UseAsia3Session { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "Asia 3 session start time in chart time zone.", GroupName = "Asia 3", Order = 1)]
        public TimeSpan Asia3SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "Asia 3 session end time in chart time zone.", GroupName = "Asia 3", Order = 2)]
        public TimeSpan Asia3SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Block Sunday Trades", Description = "If enabled, block new Asia 3 entries/flips on Sundays.", GroupName = "Asia 3", Order = 3)]
        public bool Asia3BlockSundayTrades { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Asia 3 entry and exit logic.", GroupName = "Asia 3", Order = 4)]
        public int Asia3EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for Asia 3 entries.", GroupName = "Asia 3", Order = 5)]
        public int Asia3Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trade Direction", Description = "Select whether Asia 3 can take long, short, or both directions.", GroupName = "Asia 3", Order = 6)]
        public SessionTradeDirection Asia3TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Asia 3 trend filter.", GroupName = "Asia 3", Order = 10)]
        public int Asia3AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Asia 3 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "Asia 3", Order = 11)]
        public double Asia3AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, Asia 3 flips are allowed only when ADX is greater than or equal to this value.", GroupName = "Asia 3", Order = 7)]
        public double Asia3FlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Asia 3 entries are allowed only when ADX is less than or equal to this value.", GroupName = "Asia 3", Order = 12)]
        public double Asia3AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        // [TypeConverter(typeof(Asia3AdxSlopeDropdownConverter))]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "Asia 3", Order = 13)]
        public double Asia3AdxMinSlopePoints
        {
            get { return asia3AdxMinSlopePoints; }
            set { asia3AdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "Asia 3", Order = 14)]
        public double Asia3AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "Asia 3", Order = 8)]
        public double Asia3EmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "Asia 3", Order = 9)]
        public double Asia3MaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "Asia 3", Order = 15)]
        public double Asia3AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "Asia 3", Order = 16)]
        public double Asia3StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "Asia 3", Order = 17)]
        public double Asia3ExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "Asia 3", Order = 18)]
        public double Asia3TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Offset Points", Description = "0 = market entry at signal close. Positive value = limit entry offset from signal close (long: close-offset, short: close+offset).", GroupName = "Asia 3", Order = 22)]
        public double Asia3EntryOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Flip BE Trigger", Description = "If enabled, flip entries can move stop loss to break-even after the configured profit threshold is reached.", GroupName = "Asia 3", Order = 23)]
        public bool Asia3EnableFlipBreakEven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip BE Trigger (Points)", Description = "Only used for flip entries when Enable Flip BE Trigger is on. At this unrealized profit in points, stop loss moves to break-even.", GroupName = "Asia 3", Order = 24)]
        public double Asia3FlipBreakEvenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip TP (Points)", Description = "0 uses the session take profit. Greater than 0 overrides take profit only for flip entries.", GroupName = "Asia 3", Order = 25)]
        public double Asia3FlipTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Exit Cross Points", Description = "0 uses Exit Cross Points. When an open trade reaches the normal exit cross, require this many points beyond EMA before reversing to the opposite side.", GroupName = "Asia 3", Order = 26)]
        public double Asia3FlipEmaCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Trigger", Description = "Percent of the active take-profit distance required before the stop move arms. Example: 75 = trigger after price reaches 75% of TP distance.", GroupName = "Asia 3", Order = 27)]
        public double Asia3TakeProfitPercentTriggerPercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "TP % Stop Mode", Description = "After TP % Trigger is reached, either move the stop to TP % Stop Move once or trail by ATR(14) x multiplier.", GroupName = "Asia 3", Order = 28)]
        public TakeProfitStopMode Asia3TakeProfitStopMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "TP % ATR Trail Multiplier", Description = "Only used when TP % Stop Mode is AtrTrail. Trail stop at current close minus/plus ATR(14) x this multiplier.", GroupName = "Asia 3", Order = 29)]
        public double Asia3TakeProfitAtrTrailMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Stop Move", Description = "Only used when TP % Stop Mode is PercentMove. Move stop to this percent of the active take-profit distance from entry. Example: 50 = halfway from entry to TP, 0 = break-even.", GroupName = "Asia 3", Order = 30)]
        public double Asia3TakeProfitPercentStopMovePercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ADX Require Min For Flips (FLIP)", Description = "If enabled, flips are blocked while ADX is below the active session minimum ADX threshold line.", GroupName = "Asia 3", Order = 31)]
        public bool Asia3RequireMinAdxForFlips { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX DD Risk Mode", Description = "If enabled, ADX peak drawdown trigger arms a defensive bracket instead of immediate ADX drawdown exit.", GroupName = "Asia 3", Order = 32)]
        public bool Asia3EnableAdxDdRiskMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk SL (Points)", Description = "0 disables stop adjustment. When ADX DD risk mode arms, set stop to avg entry minus/plus this many points.", GroupName = "Asia 3", Order = 33)]
        public double Asia3AdxDdRiskModeStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk TP (Points)", Description = "0 disables target adjustment. When ADX DD risk mode arms, set take profit distance to this many points from avg entry.", GroupName = "Asia 3", Order = 34)]
        public double Asia3AdxDdRiskModeTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Horizontal Exit Bars", Description = "0 disables. Close an open trade once it has been held for this many closed 5-minute bars since entry.", GroupName = "Asia 3", Order = 35)]
        public int Asia3HorizontalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max SL Points", Description = "0 disables. If the planned entry-to-stop distance is greater than this value, the trade is not placed.", GroupName = "Asia 3", Order = 36)]
        public double Asia3MaxStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new Asia 3 entries and flips while ATR(14) is below this value.", GroupName = "Asia 3", Order = 37)]
        public double Asia3AtrMinimum { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "London 1 Session(01:45-03:00)", Description = "Enable trading logic during the London 1 time window.", GroupName = "London 1", Order = 0)]
        public bool UseLondonSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "London 1 session start time in chart time zone.", GroupName = "London 1", Order = 1)]
        public TimeSpan LondonSessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "London 1 session end time in chart time zone.", GroupName = "London 1", Order = 2)]
        public TimeSpan LondonSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Auto Shift", Description = "Apply London 1 DST auto-shift for this session window.", GroupName = "London 1", Order = 3)]
        public bool AutoShiftLondon { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by London 1 entry and exit logic.", GroupName = "London 1", Order = 4)]
        public int LondonEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for London 1 entries.", GroupName = "London 1", Order = 5)]
        public int LondonContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trade Direction", Description = "Select whether London 1 can take long, short, or both directions.", GroupName = "London 1", Order = 6)]
        public SessionTradeDirection LondonTradeDirection { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, London 1 flips are allowed only when ADX is greater than or equal to this value.", GroupName = "London 1", Order = 7)]
        public double LondonFlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "London 1", Order = 8)]
        public double LondonEmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "London 1", Order = 9)]
        public double LondonMaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the London 1 trend filter.", GroupName = "London 1", Order = 10)]
        public int LondonAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. London 1 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "London 1", Order = 11)]
        public double LondonAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. London 1 entries are allowed only when ADX is less than or equal to this value.", GroupName = "London 1", Order = 12)]
        public double LondonAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "London 1", Order = 13)]
        public double LondonAdxMinSlopePoints
        {
            get { return londonAdxMinSlopePoints; }
            set { londonAdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "London 1", Order = 14)]
        public double LondonAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "London 1", Order = 15)]
        public double LondonAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "London 1", Order = 16)]
        public double LondonStopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "London 1", Order = 17)]
        public double LondonExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "London 1", Order = 18)]
        public double LondonTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Offset Points", Description = "0 = market entry at signal close. Positive value = limit entry offset from signal close (long: close-offset, short: close+offset).", GroupName = "London 1", Order = 22)]
        public double LondonEntryOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Flip BE Trigger", Description = "If enabled, flip entries can move stop loss to break-even after the configured profit threshold is reached.", GroupName = "London 1", Order = 23)]
        public bool LondonEnableFlipBreakEven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip BE Trigger (Points)", Description = "Only used for flip entries when Enable Flip BE Trigger is on. At this unrealized profit in points, stop loss moves to break-even.", GroupName = "London 1", Order = 24)]
        public double LondonFlipBreakEvenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip TP (Points)", Description = "0 uses the session take profit. Greater than 0 overrides take profit only for flip entries.", GroupName = "London 1", Order = 25)]
        public double LondonFlipTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Exit Cross Points", Description = "0 uses Exit Cross Points. When an open trade reaches the normal exit cross, require this many points beyond EMA before reversing to the opposite side.", GroupName = "London 1", Order = 26)]
        public double LondonFlipEmaCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Trigger", Description = "Percent of the active take-profit distance required before the stop move arms. Example: 75 = trigger after price reaches 75% of TP distance.", GroupName = "London 1", Order = 27)]
        public double LondonTakeProfitPercentTriggerPercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "TP % Stop Mode", Description = "After TP % Trigger is reached, either move the stop to TP % Stop Move once or trail by ATR(14) x multiplier.", GroupName = "London 1", Order = 28)]
        public TakeProfitStopMode LondonTakeProfitStopMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "TP % ATR Trail Multiplier", Description = "Only used when TP % Stop Mode is AtrTrail. Trail stop at current close minus/plus ATR(14) x this multiplier.", GroupName = "London 1", Order = 29)]
        public double LondonTakeProfitAtrTrailMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Stop Move", Description = "Only used when TP % Stop Mode is PercentMove. Move stop to this percent of the active take-profit distance from entry. Example: 50 = halfway from entry to TP, 0 = break-even.", GroupName = "London 1", Order = 30)]
        public double LondonTakeProfitPercentStopMovePercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ADX Require Min For Flips (FLIP)", Description = "If enabled, flips are blocked while ADX is below the active session minimum ADX threshold line.", GroupName = "London 1", Order = 31)]
        public bool LondonRequireMinAdxForFlips { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX DD Risk Mode", Description = "If enabled, ADX peak drawdown trigger arms a defensive bracket instead of immediate ADX drawdown exit.", GroupName = "London 1", Order = 32)]
        public bool LondonEnableAdxDdRiskMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk SL (Points)", Description = "0 disables stop adjustment. When ADX DD risk mode arms, set stop to avg entry minus/plus this many points.", GroupName = "London 1", Order = 33)]
        public double LondonAdxDdRiskModeStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk TP (Points)", Description = "0 disables target adjustment. When ADX DD risk mode arms, set take profit distance to this many points from avg entry.", GroupName = "London 1", Order = 34)]
        public double LondonAdxDdRiskModeTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Horizontal Exit Bars", Description = "0 disables. Close an open trade once it has been held for this many closed 5-minute bars since entry.", GroupName = "London 1", Order = 35)]
        public int LondonHorizontalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max SL Points", Description = "0 disables. If the planned entry-to-stop distance is greater than this value, the trade is not placed.", GroupName = "London 1", Order = 36)]
        public double LondonMaxStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new London 1 entries and flips while ATR(14) is below this value.", GroupName = "London 1", Order = 37)]
        public double LondonAtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "London 2 Session(03:00-05:00)", Description = "Enable trading logic during the London 2 time window.", GroupName = "London 2", Order = 0)]
        public bool UseLondon2Session { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "London 2 session start time in chart time zone.", GroupName = "London 2", Order = 1)]
        public TimeSpan London2SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "London 2 session end time in chart time zone.", GroupName = "London 2", Order = 2)]
        public TimeSpan London2SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Auto Shift", Description = "Apply London 2 DST auto-shift for this session window.", GroupName = "London 2", Order = 3)]
        public bool AutoShiftLondon2 { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by London 2 entry and exit logic.", GroupName = "London 2", Order = 4)]
        public int London2EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for London 2 entries.", GroupName = "London 2", Order = 5)]
        public int London2Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trade Direction", Description = "Select whether London 2 can take long, short, or both directions.", GroupName = "London 2", Order = 6)]
        public SessionTradeDirection London2TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, London 2 flips are allowed only when ADX is greater than or equal to this value.", GroupName = "London 2", Order = 7)]
        public double London2FlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "London 2", Order = 8)]
        public double London2EmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "London 2", Order = 9)]
        public double London2MaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the London 2 trend filter.", GroupName = "London 2", Order = 10)]
        public int London2AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. London 2 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "London 2", Order = 11)]
        public double London2AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. London 2 entries are allowed only when ADX is less than or equal to this value.", GroupName = "London 2", Order = 12)]
        public double London2AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "London 2", Order = 13)]
        public double London2AdxMinSlopePoints
        {
            get { return london2AdxMinSlopePoints; }
            set { london2AdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "London 2", Order = 14)]
        public double London2AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "London 2", Order = 15)]
        public double London2AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "London 2", Order = 16)]
        public double London2StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "London 2", Order = 17)]
        public double London2ExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "London 2", Order = 18)]
        public double London2TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Offset Points", Description = "0 = market entry at signal close. Positive value = limit entry offset from signal close (long: close-offset, short: close+offset).", GroupName = "London 2", Order = 22)]
        public double London2EntryOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Flip BE Trigger", Description = "If enabled, flip entries can move stop loss to break-even after the configured profit threshold is reached.", GroupName = "London 2", Order = 23)]
        public bool London2EnableFlipBreakEven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip BE Trigger (Points)", Description = "Only used for flip entries when Enable Flip BE Trigger is on. At this unrealized profit in points, stop loss moves to break-even.", GroupName = "London 2", Order = 24)]
        public double London2FlipBreakEvenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip TP (Points)", Description = "0 uses the session take profit. Greater than 0 overrides take profit only for flip entries.", GroupName = "London 2", Order = 25)]
        public double London2FlipTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Exit Cross Points", Description = "0 uses Exit Cross Points. When an open trade reaches the normal exit cross, require this many points beyond EMA before reversing to the opposite side.", GroupName = "London 2", Order = 26)]
        public double London2FlipEmaCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Trigger", Description = "Percent of the active take-profit distance required before the stop move arms. Example: 75 = trigger after price reaches 75% of TP distance.", GroupName = "London 2", Order = 27)]
        public double London2TakeProfitPercentTriggerPercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "TP % Stop Mode", Description = "After TP % Trigger is reached, either move the stop to TP % Stop Move once or trail by ATR(14) x multiplier.", GroupName = "London 2", Order = 28)]
        public TakeProfitStopMode London2TakeProfitStopMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "TP % ATR Trail Multiplier", Description = "Only used when TP % Stop Mode is AtrTrail. Trail stop at current close minus/plus ATR(14) x this multiplier.", GroupName = "London 2", Order = 29)]
        public double London2TakeProfitAtrTrailMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Stop Move", Description = "Only used when TP % Stop Mode is PercentMove. Move stop to this percent of the active take-profit distance from entry. Example: 50 = halfway from entry to TP, 0 = break-even.", GroupName = "London 2", Order = 30)]
        public double London2TakeProfitPercentStopMovePercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ADX Require Min For Flips (FLIP)", Description = "If enabled, flips are blocked while ADX is below the active session minimum ADX threshold line.", GroupName = "London 2", Order = 31)]
        public bool London2RequireMinAdxForFlips { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX DD Risk Mode", Description = "If enabled, ADX peak drawdown trigger arms a defensive bracket instead of immediate ADX drawdown exit.", GroupName = "London 2", Order = 32)]
        public bool London2EnableAdxDdRiskMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk SL (Points)", Description = "0 disables stop adjustment. When ADX DD risk mode arms, set stop to avg entry minus/plus this many points.", GroupName = "London 2", Order = 33)]
        public double London2AdxDdRiskModeStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk TP (Points)", Description = "0 disables target adjustment. When ADX DD risk mode arms, set take profit distance to this many points from avg entry.", GroupName = "London 2", Order = 34)]
        public double London2AdxDdRiskModeTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Horizontal Exit Bars", Description = "0 disables. Close an open trade once it has been held for this many closed 5-minute bars since entry.", GroupName = "London 2", Order = 35)]
        public int London2HorizontalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max SL Points", Description = "0 disables. If the planned entry-to-stop distance is greater than this value, the trade is not placed.", GroupName = "London 2", Order = 36)]
        public double London2MaxStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new London 2 entries and flips while ATR(14) is below this value.", GroupName = "London 2", Order = 37)]
        public double London2AtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "London 3 Session(05:00-08:55)", Description = "Enable trading logic during the London 3 time window.", GroupName = "London 3", Order = 0)]
        public bool UseLondon3Session { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "London 3 session start time in chart time zone.", GroupName = "London 3", Order = 1)]
        public TimeSpan London3SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "London 3 session end time in chart time zone.", GroupName = "London 3", Order = 2)]
        public TimeSpan London3SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Auto Shift", Description = "Apply London 3 DST auto-shift for this session window.", GroupName = "London 3", Order = 3)]
        public bool AutoShiftLondon3 { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Flat By Time", Description = "Optional fixed chart time to flatten any open London 3 trade. Leave blank to disable. This does not shift with Auto Shift.", GroupName = "London 3", Order = 4)]
        public string London3FlatByTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by London 3 entry and exit logic.", GroupName = "London 3", Order = 4)]
        public int London3EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for London 3 entries.", GroupName = "London 3", Order = 5)]
        public int London3Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trade Direction", Description = "Select whether London 3 can take long, short, or both directions.", GroupName = "London 3", Order = 6)]
        public SessionTradeDirection London3TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, London 3 flips are allowed only when ADX is greater than or equal to this value.", GroupName = "London 3", Order = 7)]
        public double London3FlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "London 3", Order = 8)]
        public double London3EmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "London 3", Order = 9)]
        public double London3MaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the London 3 trend filter.", GroupName = "London 3", Order = 10)]
        public int London3AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. London 3 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "London 3", Order = 11)]
        public double London3AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. London 3 entries are allowed only when ADX is less than or equal to this value.", GroupName = "London 3", Order = 12)]
        public double London3AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "London 3", Order = 13)]
        public double London3AdxMinSlopePoints
        {
            get { return london3AdxMinSlopePoints; }
            set { london3AdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "London 3", Order = 14)]
        public double London3AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "London 3", Order = 15)]
        public double London3AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "London 3", Order = 16)]
        public double London3StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "London 3", Order = 17)]
        public double London3ExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "London 3", Order = 18)]
        public double London3TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Offset Points", Description = "0 = market entry at signal close. Positive value = limit entry offset from signal close (long: close-offset, short: close+offset).", GroupName = "London 3", Order = 22)]
        public double London3EntryOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Flip BE Trigger", Description = "If enabled, flip entries can move stop loss to break-even after the configured profit threshold is reached.", GroupName = "London 3", Order = 23)]
        public bool London3EnableFlipBreakEven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip BE Trigger (Points)", Description = "Only used for flip entries when Enable Flip BE Trigger is on. At this unrealized profit in points, stop loss moves to break-even.", GroupName = "London 3", Order = 24)]
        public double London3FlipBreakEvenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip TP (Points)", Description = "0 uses the session take profit. Greater than 0 overrides take profit only for flip entries.", GroupName = "London 3", Order = 25)]
        public double London3FlipTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Exit Cross Points", Description = "0 uses Exit Cross Points. When an open trade reaches the normal exit cross, require this many points beyond EMA before reversing to the opposite side.", GroupName = "London 3", Order = 26)]
        public double London3FlipEmaCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Trigger", Description = "Percent of the active take-profit distance required before the stop move arms. Example: 75 = trigger after price reaches 75% of TP distance.", GroupName = "London 3", Order = 27)]
        public double London3TakeProfitPercentTriggerPercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "TP % Stop Mode", Description = "After TP % Trigger is reached, either move the stop to TP % Stop Move once or trail by ATR(14) x multiplier.", GroupName = "London 3", Order = 28)]
        public TakeProfitStopMode London3TakeProfitStopMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "TP % ATR Trail Multiplier", Description = "Only used when TP % Stop Mode is AtrTrail. Trail stop at current close minus/plus ATR(14) x this multiplier.", GroupName = "London 3", Order = 29)]
        public double London3TakeProfitAtrTrailMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Stop Move", Description = "Only used when TP % Stop Mode is PercentMove. Move stop to this percent of the active take-profit distance from entry. Example: 50 = halfway from entry to TP, 0 = break-even.", GroupName = "London 3", Order = 30)]
        public double London3TakeProfitPercentStopMovePercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ADX Require Min For Flips (FLIP)", Description = "If enabled, flips are blocked while ADX is below the active session minimum ADX threshold line.", GroupName = "London 3", Order = 31)]
        public bool London3RequireMinAdxForFlips { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX DD Risk Mode", Description = "If enabled, ADX peak drawdown trigger arms a defensive bracket instead of immediate ADX drawdown exit.", GroupName = "London 3", Order = 32)]
        public bool London3EnableAdxDdRiskMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk SL (Points)", Description = "0 disables stop adjustment. When ADX DD risk mode arms, set stop to avg entry minus/plus this many points.", GroupName = "London 3", Order = 33)]
        public double London3AdxDdRiskModeStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk TP (Points)", Description = "0 disables target adjustment. When ADX DD risk mode arms, set take profit distance to this many points from avg entry.", GroupName = "London 3", Order = 34)]
        public double London3AdxDdRiskModeTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Horizontal Exit Bars", Description = "0 disables. Close an open trade once it has been held for this many closed 5-minute bars since entry.", GroupName = "London 3", Order = 35)]
        public int London3HorizontalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max SL Points", Description = "0 disables. If the planned entry-to-stop distance is greater than this value, the trade is not placed.", GroupName = "London 3", Order = 36)]
        public double London3MaxStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new London 3 entries and flips while ATR(14) is below this value.", GroupName = "London 3", Order = 37)]
        public double London3AtrMinimum { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "New York 1 Session(09:35-11:30)", Description = "Enable trading logic during the New York 1 time window.", GroupName = "New York 1", Order = 0)]
        public bool UseNewYorkSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "New York 1 session start time in chart time zone.", GroupName = "New York 1", Order = 1)]
        public TimeSpan NewYorkSessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "New York 1 session end time in chart time zone.", GroupName = "New York 1", Order = 2)]
        public TimeSpan NewYorkSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Skip Start", Description = "Start of New York 1 skip window.", GroupName = "New York 1", Order = 3)]
        public TimeSpan NewYorkSkipStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Skip End", Description = "End of New York 1 skip window.", GroupName = "New York 1", Order = 4)]
        public TimeSpan NewYorkSkipEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by New York 1 entry and exit logic.", GroupName = "New York 1", Order = 5)]
        public int NewYorkEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for New York 1 entries.", GroupName = "New York 1", Order = 6)]
        public int NewYorkContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trade Direction", Description = "Select whether New York 1 can take long, short, or both directions.", GroupName = "New York 1", Order = 7)]
        public SessionTradeDirection NewYorkTradeDirection { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the New York 1 trend filter.", GroupName = "New York 1", Order = 11)]
        public int NewYorkAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. New York 1 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "New York 1", Order = 12)]
        public double NewYorkAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, New York 1 flips are allowed only when ADX is greater than or equal to this value.", GroupName = "New York 1", Order = 8)]
        public double NewYorkFlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. New York 1 entries are allowed only when ADX is less than or equal to this value.", GroupName = "New York 1", Order = 13)]
        public double NewYorkAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        // [TypeConverter(typeof(NewYorkAdxSlopeDropdownConverter))]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "New York 1", Order = 14)]
        public double NewYorkAdxMinSlopePoints
        {
            get { return newYorkAdxMinSlopePoints; }
            set { newYorkAdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "New York 1", Order = 15)]
        public double NewYorkAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "New York 1", Order = 9)]
        public double NewYorkEmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "New York 1", Order = 10)]
        public double NewYorkMaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "New York 1", Order = 16)]
        public double NewYorkAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "New York 1", Order = 17)]
        public double NewYorkStopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "New York 1", Order = 18)]
        public double NewYorkExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "New York 1", Order = 19)]
        public double NewYorkTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "HV SL Padding Points", Description = "High-volatility stop distance in points from EMA on the opposite side. 0 disables HV stop logic.", GroupName = "New York 1", Order = 20)]
        public double NewYorkHvSlPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "HV SL Start Time", Description = "Start time for using HV SL Padding Points.", GroupName = "New York 1", Order = 21)]
        public TimeSpan NewYorkHvSlStartTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "HV SL End Time", Description = "End time for using HV SL Padding Points.", GroupName = "New York 1", Order = 22)]
        public TimeSpan NewYorkHvSlEndTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Offset Points", Description = "0 = market entry at signal close. Positive value = limit entry offset from signal close (long: close-offset, short: close+offset).", GroupName = "New York 1", Order = 23)]
        public double NewYorkEntryOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Flip BE Trigger", Description = "If enabled, flip entries can move stop loss to break-even after the configured profit threshold is reached.", GroupName = "New York 1", Order = 24)]
        public bool NewYorkEnableFlipBreakEven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip BE Trigger (Points)", Description = "Only used for flip entries when Enable Flip BE Trigger is on. At this unrealized profit in points, stop loss moves to break-even.", GroupName = "New York 1", Order = 25)]
        public double NewYorkFlipBreakEvenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip TP (Points)", Description = "0 uses the session take profit. Greater than 0 overrides take profit only for flip entries.", GroupName = "New York 1", Order = 26)]
        public double NewYorkFlipTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Exit Cross Points", Description = "0 uses Exit Cross Points. When an open trade reaches the normal exit cross, require this many points beyond EMA before reversing to the opposite side.", GroupName = "New York 1", Order = 27)]
        public double NewYorkFlipEmaCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Trigger", Description = "Percent of the active take-profit distance required before the stop move arms. Example: 75 = trigger after price reaches 75% of TP distance.", GroupName = "New York 1", Order = 28)]
        public double NewYorkTakeProfitPercentTriggerPercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "TP % Stop Mode", Description = "After TP % Trigger is reached, either move the stop to TP % Stop Move once or trail by ATR(14) x multiplier.", GroupName = "New York 1", Order = 29)]
        public TakeProfitStopMode NewYorkTakeProfitStopMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "TP % ATR Trail Multiplier", Description = "Only used when TP % Stop Mode is AtrTrail. Trail stop at current close minus/plus ATR(14) x this multiplier.", GroupName = "New York 1", Order = 30)]
        public double NewYorkTakeProfitAtrTrailMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Stop Move", Description = "Only used when TP % Stop Mode is PercentMove. Move stop to this percent of the active take-profit distance from entry. Example: 50 = halfway from entry to TP, 0 = break-even.", GroupName = "New York 1", Order = 31)]
        public double NewYorkTakeProfitPercentStopMovePercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ADX Require Min For Flips (FLIP)", Description = "If enabled, flips are blocked while ADX is below the active session minimum ADX threshold line.", GroupName = "New York 1", Order = 32)]
        public bool NewYorkRequireMinAdxForFlips { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX DD Risk Mode", Description = "If enabled, ADX peak drawdown trigger arms a defensive bracket instead of immediate ADX drawdown exit.", GroupName = "New York 1", Order = 33)]
        public bool NewYorkEnableAdxDdRiskMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk SL (Points)", Description = "0 disables stop adjustment. When ADX DD risk mode arms, set stop to avg entry minus/plus this many points.", GroupName = "New York 1", Order = 34)]
        public double NewYorkAdxDdRiskModeStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk TP (Points)", Description = "0 disables target adjustment. When ADX DD risk mode arms, set take profit distance to this many points from avg entry.", GroupName = "New York 1", Order = 35)]
        public double NewYorkAdxDdRiskModeTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Horizontal Exit Bars", Description = "0 disables. Close an open trade once it has been held for this many closed 5-minute bars since entry.", GroupName = "New York 1", Order = 36)]
        public int NewYorkHorizontalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max SL Points", Description = "0 disables. If the planned entry-to-stop distance is greater than this value, the trade is not placed.", GroupName = "New York 1", Order = 37)]
        public double NewYorkMaxStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new New York 1 entries and flips while ATR(14) is below this value.", GroupName = "New York 1", Order = 38)]
        public double NewYorkAtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "New York 2 Session(11:30-14:00)", Description = "Enable trading logic during the New York 2 time window.", GroupName = "New York 2", Order = 0)]
        public bool UseNewYork2Session { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "New York 2 session start time in chart time zone.", GroupName = "New York 2", Order = 1)]
        public TimeSpan NewYork2SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "New York 2 session end time in chart time zone.", GroupName = "New York 2", Order = 2)]
        public TimeSpan NewYork2SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Skip Start", Description = "Start of New York 2 skip window.", GroupName = "New York 2", Order = 3)]
        public TimeSpan NewYork2SkipStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Skip End", Description = "End of New York 2 skip window.", GroupName = "New York 2", Order = 4)]
        public TimeSpan NewYork2SkipEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by New York 2 entry and exit logic.", GroupName = "New York 2", Order = 5)]
        public int NewYork2EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for New York 2 entries.", GroupName = "New York 2", Order = 6)]
        public int NewYork2Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trade Direction", Description = "Select whether New York 2 can take long, short, or both directions.", GroupName = "New York 2", Order = 7)]
        public SessionTradeDirection NewYork2TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the New York 2 trend filter.", GroupName = "New York 2", Order = 11)]
        public int NewYork2AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. New York 2 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "New York 2", Order = 12)]
        public double NewYork2AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, New York 2 flips are allowed only when ADX is greater than or equal to this value.", GroupName = "New York 2", Order = 8)]
        public double NewYork2FlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. New York 2 entries are allowed only when ADX is less than or equal to this value.", GroupName = "New York 2", Order = 13)]
        public double NewYork2AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        // [TypeConverter(typeof(NewYork2AdxSlopeDropdownConverter))]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "New York 2", Order = 14)]
        public double NewYork2AdxMinSlopePoints
        {
            get { return newYork2AdxMinSlopePoints; }
            set { newYork2AdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "New York 2", Order = 15)]
        public double NewYork2AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "New York 2", Order = 9)]
        public double NewYork2EmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "New York 2", Order = 10)]
        public double NewYork2MaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "New York 2", Order = 16)]
        public double NewYork2AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "New York 2", Order = 17)]
        public double NewYork2StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "New York 2", Order = 18)]
        public double NewYork2ExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "New York 2", Order = 19)]
        public double NewYork2TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "HV SL Padding Points", Description = "High-volatility stop distance in points from EMA on the opposite side. 0 disables HV stop logic.", GroupName = "New York 2", Order = 20)]
        public double NewYork2HvSlPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "HV SL Start Time", Description = "Start time for using HV SL Padding Points.", GroupName = "New York 2", Order = 21)]
        public TimeSpan NewYork2HvSlStartTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "HV SL End Time", Description = "End time for using HV SL Padding Points.", GroupName = "New York 2", Order = 22)]
        public TimeSpan NewYork2HvSlEndTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Offset Points", Description = "0 = market entry at signal close. Positive value = limit entry offset from signal close (long: close-offset, short: close+offset).", GroupName = "New York 2", Order = 23)]
        public double NewYork2EntryOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Flip BE Trigger", Description = "If enabled, flip entries can move stop loss to break-even after the configured profit threshold is reached.", GroupName = "New York 2", Order = 24)]
        public bool NewYork2EnableFlipBreakEven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip BE Trigger (Points)", Description = "Only used for flip entries when Enable Flip BE Trigger is on. At this unrealized profit in points, stop loss moves to break-even.", GroupName = "New York 2", Order = 25)]
        public double NewYork2FlipBreakEvenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip TP (Points)", Description = "0 uses the session take profit. Greater than 0 overrides take profit only for flip entries.", GroupName = "New York 2", Order = 26)]
        public double NewYork2FlipTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Exit Cross Points", Description = "0 uses Exit Cross Points. When an open trade reaches the normal exit cross, require this many points beyond EMA before reversing to the opposite side.", GroupName = "New York 2", Order = 27)]
        public double NewYork2FlipEmaCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Trigger", Description = "Percent of the active take-profit distance required before the stop move arms. Example: 75 = trigger after price reaches 75% of TP distance.", GroupName = "New York 2", Order = 28)]
        public double NewYork2TakeProfitPercentTriggerPercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "TP % Stop Mode", Description = "After TP % Trigger is reached, either move the stop to TP % Stop Move once or trail by ATR(14) x multiplier.", GroupName = "New York 2", Order = 29)]
        public TakeProfitStopMode NewYork2TakeProfitStopMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "TP % ATR Trail Multiplier", Description = "Only used when TP % Stop Mode is AtrTrail. Trail stop at current close minus/plus ATR(14) x this multiplier.", GroupName = "New York 2", Order = 30)]
        public double NewYork2TakeProfitAtrTrailMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Stop Move", Description = "Only used when TP % Stop Mode is PercentMove. Move stop to this percent of the active take-profit distance from entry. Example: 50 = halfway from entry to TP, 0 = break-even.", GroupName = "New York 2", Order = 31)]
        public double NewYork2TakeProfitPercentStopMovePercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ADX Require Min For Flips (FLIP)", Description = "If enabled, flips are blocked while ADX is below the active session minimum ADX threshold line.", GroupName = "New York 2", Order = 32)]
        public bool NewYork2RequireMinAdxForFlips { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX DD Risk Mode", Description = "If enabled, ADX peak drawdown trigger arms a defensive bracket instead of immediate ADX drawdown exit.", GroupName = "New York 2", Order = 33)]
        public bool NewYork2EnableAdxDdRiskMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk SL (Points)", Description = "0 disables stop adjustment. When ADX DD risk mode arms, set stop to avg entry minus/plus this many points.", GroupName = "New York 2", Order = 34)]
        public double NewYork2AdxDdRiskModeStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk TP (Points)", Description = "0 disables target adjustment. When ADX DD risk mode arms, set take profit distance to this many points from avg entry.", GroupName = "New York 2", Order = 35)]
        public double NewYork2AdxDdRiskModeTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Horizontal Exit Bars", Description = "0 disables. Close an open trade once it has been held for this many closed 5-minute bars since entry.", GroupName = "New York 2", Order = 36)]
        public int NewYork2HorizontalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max SL Points", Description = "0 disables. If the planned entry-to-stop distance is greater than this value, the trade is not placed.", GroupName = "New York 2", Order = 37)]
        public double NewYork2MaxStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new New York 2 entries and flips while ATR(14) is below this value.", GroupName = "New York 2", Order = 38)]
        public double NewYork2AtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "New York 3 Session(14:00-17:00)", Description = "Enable trading logic during the New York 3 time window.", GroupName = "New York 3", Order = 0)]
        public bool UseNewYork3Session { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "New York 3 session start time in chart time zone.", GroupName = "New York 3", Order = 1)]
        public TimeSpan NewYork3SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "New York 3 session end time in chart time zone.", GroupName = "New York 3", Order = 2)]
        public TimeSpan NewYork3SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Skip Start", Description = "Start of New York 3 skip window.", GroupName = "New York 3", Order = 3)]
        public TimeSpan NewYork3SkipStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Skip End", Description = "End of New York 3 skip window.", GroupName = "New York 3", Order = 4)]
        public TimeSpan NewYork3SkipEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by New York 3 entry and exit logic.", GroupName = "New York 3", Order = 5)]
        public int NewYork3EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for New York 3 entries.", GroupName = "New York 3", Order = 6)]
        public int NewYork3Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trade Direction", Description = "Select whether New York 3 can take long, short, or both directions.", GroupName = "New York 3", Order = 7)]
        public SessionTradeDirection NewYork3TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the New York 3 trend filter.", GroupName = "New York 3", Order = 11)]
        public int NewYork3AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. New York 3 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "New York 3", Order = 12)]
        public double NewYork3AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, New York 3 flips are allowed only when ADX is greater than or equal to this value.", GroupName = "New York 3", Order = 8)]
        public double NewYork3FlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. New York 3 entries are allowed only when ADX is less than or equal to this value.", GroupName = "New York 3", Order = 13)]
        public double NewYork3AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        // [TypeConverter(typeof(NewYork3AdxSlopeDropdownConverter))]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "New York 3", Order = 14)]
        public double NewYork3AdxMinSlopePoints
        {
            get { return newYork3AdxMinSlopePoints; }
            set { newYork3AdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "New York 3", Order = 15)]
        public double NewYork3AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "New York 3", Order = 9)]
        public double NewYork3EmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "New York 3", Order = 10)]
        public double NewYork3MaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "New York 3", Order = 16)]
        public double NewYork3AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "New York 3", Order = 17)]
        public double NewYork3StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "New York 3", Order = 18)]
        public double NewYork3ExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "New York 3", Order = 19)]
        public double NewYork3TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "HV SL Padding Points", Description = "High-volatility stop distance in points from EMA on the opposite side. 0 disables HV stop logic.", GroupName = "New York 3", Order = 20)]
        public double NewYork3HvSlPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "HV SL Start Time", Description = "Start time for using HV SL Padding Points.", GroupName = "New York 3", Order = 21)]
        public TimeSpan NewYork3HvSlStartTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "HV SL End Time", Description = "End time for using HV SL Padding Points.", GroupName = "New York 3", Order = 22)]
        public TimeSpan NewYork3HvSlEndTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Offset Points", Description = "0 = market entry at signal close. Positive value = limit entry offset from signal close (long: close-offset, short: close+offset).", GroupName = "New York 3", Order = 23)]
        public double NewYork3EntryOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Flip BE Trigger", Description = "If enabled, flip entries can move stop loss to break-even after the configured profit threshold is reached.", GroupName = "New York 3", Order = 24)]
        public bool NewYork3EnableFlipBreakEven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip BE Trigger (Points)", Description = "Only used for flip entries when Enable Flip BE Trigger is on. At this unrealized profit in points, stop loss moves to break-even.", GroupName = "New York 3", Order = 25)]
        public double NewYork3FlipBreakEvenTriggerPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Flip TP (Points)", Description = "0 uses the session take profit. Greater than 0 overrides take profit only for flip entries.", GroupName = "New York 3", Order = 26)]
        public double NewYork3FlipTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Exit Cross Points", Description = "0 uses Exit Cross Points. When an open trade reaches the normal exit cross, require this many points beyond EMA before reversing to the opposite side.", GroupName = "New York 3", Order = 27)]
        public double NewYork3FlipEmaCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Trigger", Description = "Percent of the active take-profit distance required before the stop move arms. Example: 75 = trigger after price reaches 75% of TP distance.", GroupName = "New York 3", Order = 28)]
        public double NewYork3TakeProfitPercentTriggerPercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "TP % Stop Mode", Description = "After TP % Trigger is reached, either move the stop to TP % Stop Move once or trail by ATR(14) x multiplier.", GroupName = "New York 3", Order = 29)]
        public TakeProfitStopMode NewYork3TakeProfitStopMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "TP % ATR Trail Multiplier", Description = "Only used when TP % Stop Mode is AtrTrail. Trail stop at current close minus/plus ATR(14) x this multiplier.", GroupName = "New York 3", Order = 30)]
        public double NewYork3TakeProfitAtrTrailMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "TP % Stop Move", Description = "Only used when TP % Stop Mode is PercentMove. Move stop to this percent of the active take-profit distance from entry. Example: 50 = halfway from entry to TP, 0 = break-even.", GroupName = "New York 3", Order = 31)]
        public double NewYork3TakeProfitPercentStopMovePercent { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ADX Require Min For Flips (FLIP)", Description = "If enabled, flips are blocked while ADX is below the active session minimum ADX threshold line.", GroupName = "New York 3", Order = 32)]
        public bool NewYork3RequireMinAdxForFlips { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX DD Risk Mode", Description = "If enabled, ADX peak drawdown trigger arms a defensive bracket instead of immediate ADX drawdown exit.", GroupName = "New York 3", Order = 33)]
        public bool NewYork3EnableAdxDdRiskMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk SL (Points)", Description = "0 disables stop adjustment. When ADX DD risk mode arms, set stop to avg entry minus/plus this many points.", GroupName = "New York 3", Order = 34)]
        public double NewYork3AdxDdRiskModeStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX DD Risk TP (Points)", Description = "0 disables target adjustment. When ADX DD risk mode arms, set take profit distance to this many points from avg entry.", GroupName = "New York 3", Order = 35)]
        public double NewYork3AdxDdRiskModeTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Horizontal Exit Bars", Description = "0 disables. Close an open trade once it has been held for this many closed 5-minute bars since entry.", GroupName = "New York 3", Order = 36)]
        public int NewYork3HorizontalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max SL Points", Description = "0 disables. If the planned entry-to-stop distance is greater than this value, the trade is not placed.", GroupName = "New York 3", Order = 37)]
        public double NewYork3MaxStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new New York 3 entries and flips while ATR(14) is below this value.", GroupName = "New York 3", Order = 38)]
        public double NewYork3AtrMinimum { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Close At Session End", Description = "If true, flatten positions and cancel entries at each configured session end.", GroupName = "10. Sessions", Order = 0)]
        public bool CloseAtSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Force Close Time", Description = "Optional. Leave empty to disable. Enter as HH:mm:ss using the 5-minute bar timestamp, for example 04:55:00 to flatten on the 05:00 bar close. After this time, cancel working entries, flatten any open position, and block new trades for the rest of the trading day.", GroupName = "13. Risk", Order = 1)]
        public string ForceCloseTime { get; set; }

        // [NinjaScriptProperty]
        // [XmlIgnore]
        // [Display(Name = "Asia Session Fill", Description = "Background color used to highlight Asia session windows.", GroupName = "10. Sessions", Order = 2)]
        internal Brush AsiaSessionBrush { get; set; }

        [Browsable(false)]
        public string AsiaSessionBrushSerializable
        {
            get { return Serialize.BrushToString(AsiaSessionBrush); }
            set { AsiaSessionBrush = Serialize.StringToBrush(value); }
        }

        // [NinjaScriptProperty]
        // [XmlIgnore]
        // [Display(Name = "London Session Fill", Description = "Background color used to highlight London session windows.", GroupName = "10. Sessions", Order = 3)]
        internal Brush LondonSessionBrush { get; set; }

        [Browsable(false)]
        public string LondonSessionBrushSerializable
        {
            get { return Serialize.BrushToString(LondonSessionBrush); }
            set { LondonSessionBrush = Serialize.StringToBrush(value); }
        }

        // [NinjaScriptProperty]
        // [XmlIgnore]
        // [Display(Name = "New York Session Fill", Description = "Background color used to highlight New York session windows.", GroupName = "10. Sessions", Order = 4)]
        internal Brush NewYorkSessionBrush { get; set; }

        [Browsable(false)]
        public string NewYorkSessionBrushSerializable
        {
            get { return Serialize.BrushToString(NewYorkSessionBrush); }
            set { NewYorkSessionBrush = Serialize.StringToBrush(value); }
        }

        // [NinjaScriptProperty]
        // [Display(Name = "Show EMA On Chart", Description = "Show/hide EMA indicators on chart.", GroupName = "10. Sessions", Order = 5)]
        internal bool ShowEmaOnChart { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "ADX Show On Chart", Description = "Show/hide ADX indicators on chart.", GroupName = "10. Sessions", Order = 6)]
        internal bool ShowAdxOnChart { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "ADX Show Threshold Lines", Description = "Show/hide ADX min/max threshold reference lines on chart.", GroupName = "10. Sessions", Order = 7)]
        internal bool ShowAdxThresholdLines { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Show ATR On Chart", Description = "Show/hide ATR(14) indicator on chart.", GroupName = "10. Sessions", Order = 8)]
        internal bool ShowAtrOnChart { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "ATR Show Threshold Line", Description = "Show/hide ATR min threshold reference line on chart.", GroupName = "10. Sessions", Order = 9)]
        internal bool ShowAtrThresholdLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Description = "Block entries inside the configured minutes before and after listed news events.", GroupName = "11. News", Order = 0)]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Block Minutes", Description = "Minutes blocked before and after each news timestamp.", GroupName = "11. News", Order = 1)]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL", Description = "HTTP endpoint for TradersPost order webhooks. Leave empty to disable TradersPost webhooks.", GroupName = "12. Webhooks", Order = 0)]
        public string WebhookUrl
        {
            get { return webhookUrl ?? string.Empty; }
            set { webhookUrl = value ?? string.Empty; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Ticker Override", Description = "Optional TradersPost ticker/instrument name override. Leave empty to use the chart instrument automatically.", GroupName = "12. Webhooks", Order = 1)]
        public string WebhookTickerOverride
        {
            get { return webhookTickerOverride ?? string.Empty; }
            set { webhookTickerOverride = value ?? string.Empty; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Provider", Description = "Select webhook target: TradersPost or ProjectX.", GroupName = "12. Webhooks", Order = 2)]
        public WebhookProvider WebhookProviderType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Base URL", Description = "ProjectX gateway base URL. Leave the default ProjectX gateway URL or paste your firm-specific endpoint.", GroupName = "12. Webhooks", Order = 3)]
        public string ProjectXApiBaseUrl { get; set; }

        [Browsable(false)]
        public bool ProjectXTradeAllAccounts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Username", Description = "ProjectX login username for direct ProjectX order routing.", GroupName = "12. Webhooks", Order = 5)]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX API Key", Description = "ProjectX API key used together with the ProjectX username.", GroupName = "12. Webhooks", Order = 6)]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Accounts", Description = "Comma-separated ProjectX account ids or exact account names.", GroupName = "12. Webhooks", Order = 7)]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Contract ID", Description = "Hidden optional override for support/debug use only.", GroupName = "12. Webhooks", Order = 8)]
        public string ProjectXContractId { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Account Balance", Description = "When net liquidation reaches or exceeds this value, entries are blocked and open positions are flattened. 0 disables.", GroupName = "13. Risk", Order = 0)]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Confirmation", Description = "Show a Yes/No confirmation popup before each new long/short entry (including flips).", GroupName = "13. Risk", Order = 2)]
        public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Debug Logging", Description = "Print concise decision, order, and execution diagnostics to Output.", GroupName = "14. Debug", Order = 0)]
        public bool DebugLogging { get; set; }
    }
}
