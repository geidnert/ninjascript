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
    public class DUOrc : Strategy
    {
        private const string HeartbeatStrategyName = "DUOrc";

        public DUOrc()
        {
            VendorLicense(337);
        }

        private sealed class TradeLineSnapshot
        {
            public int StartBar;
            public int EndBar;
            public double EntryPrice;
            public double StopPrice;
            public double TakeProfitPrice;
            public bool HasTakeProfit;
        }

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
            NewYork3,
            NewYork4,
            NewYork5
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

        public enum InitialEntryType
        {
            [Display(Name = "Market Entry")]
            Market,
            [Display(Name = "Limit Entry")]
            EmaLimit
        }

        public enum EntrySystemMode
        {
            Primary,
            Secondary,
            Both
        }

        private enum ProjectXProtectionOrderKind
        {
            StopLoss,
            TakeProfit
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
        private TimeZoneInfo targetTimeZone;
        private TimeZoneInfo londonTimeZone;
        private TimeZoneInfo newYorkTimeZone;
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
        private bool newYork4SessionClosed;
        private bool newYork5SessionClosed;

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
        private int tradeLineSignalBar = -1;
        private int tradeLineExitBar = -1;
        private double tradeLineEntryPrice;
        private double tradeLineTpPrice;
        private double tradeLineSlPrice;
        private readonly List<TradeLineSnapshot> historicalTradeLines = new List<TradeLineSnapshot>();
        private readonly object tradeLineSnapshotsSync = new object();

        private EMA emaAsia;
        private EMA emaAsia2;
        private EMA emaAsia3;
        private EMA emaLondon;
        private EMA emaLondon2;
        private EMA emaLondon3;
        private EMA emaNewYork;
        private EMA emaNewYork2;
        private EMA emaNewYork3;
        private EMA emaNewYork4;
        private EMA emaNewYork5;
        private EMA activeEma;
        private EMA secondaryBiasEma;
        private ATR entryAtr;
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
        private DM adxNewYork4;
        private DM adxNewYork5;
        private DM activeAdx;

        private int activeEmaPeriod;
        private int activeContracts;
        private double activeEntryMinBodyPoints;
        private double activeEntryCloseBeyondEmaPoints;
        private bool activeColorBlindEntry;
        private InitialEntryType activeEntryType;
        private EntrySystemMode activeEntrySystemMode = EntrySystemMode.Primary;
        private bool activeEnableStopOutFlip;
        private int activeSecondaryContracts;
        private double activeSecondaryEntryLimitDistanceFromEmaPoints;
        private double activeSecondaryEntryStopDistanceFromEmaPoints;
        private double activeSecondaryEntryTakeProfitDistanceFromEmaPoints;
        private double activeSecondaryTakeProfitVariancePoints;
        private double activeTakeProfitPoints;
        private double activeMinimumAtrForEntry;
        private int activeAdxPeriod;
        private double activeAdxThreshold;
        private double activeAdxMaxThreshold;
        private double activeAdxPeakDrawdownExitUnits;
        private double activeAdxAbsoluteExitLevel;
        private double activeStopPaddingPoints;
        private bool activeTrailHardStop;
        private int activeCandleReversalExitBars;
        private double activeCandleReversalCloseBeyondPoints;
        private double activeCandleReversalMinBodyPoints;

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
        private bool secondaryLongDoorOpen;
        private bool secondaryShortDoorOpen;
        private SessionSlot secondaryLongDoorSession = SessionSlot.None;
        private SessionSlot secondaryShortDoorSession = SessionSlot.None;
        private int secondaryLongDoorSignalBar = -1;
        private int secondaryShortDoorSignalBar = -1;
        private double secondaryLongDoorTakeProfitVariancePoints;
        private double secondaryShortDoorTakeProfitVariancePoints;
        private bool secondaryLongPositionActive;
        private bool secondaryShortPositionActive;
        private Random secondaryTakeProfitVarianceRandom;
        private double secondaryLongTrailStopPrice;
        private double secondaryShortTrailStopPrice;
        private double secondaryLongTrailDistanceFromEmaPoints;
        private double secondaryShortTrailDistanceFromEmaPoints;
        private double initialStopPrice;
        private double currentStopPrice;
        private int currentPositionEntryBar = -1;
        private Order activeStopLossOrder;
        private Order activeProfitTargetOrder;
        private Order activeExitOrder;
        private DateTime protectionAuditGraceUntilUtc = Core.Globals.MinDate;
        private bool terminalExitPending;
        private string terminalExitPendingReason = string.Empty;
        private DateTime terminalExitPendingSinceUtc = Core.Globals.MinDate;
        private int terminalExitPendingBar = -1;
        private MarketPosition terminalExitPendingSide = MarketPosition.Flat;
        private bool emergencyOverfillFlattenSubmitted;
        private int lastStopOutFlipSubmittedBar = -1;
        private bool missingProtectionWarningPrinted;
        private bool isConfiguredTimeframeValid = true;
        private bool isConfiguredInstrumentValid = true;
        private bool timeframePopupShown;
        private bool instrumentPopupShown;
        private const double VerticalFillLowerPriceBound = -100000000.0;
        private const double VerticalFillUpperPriceBound = 100000000.0;
        private const int EntryAtrPeriod = 14;
        private const int EntryVarianceMinDelaySeconds = 1;
        private const int EntryVarianceMaxDelaySeconds = 10;
        private const string LongEntrySignal = "DUOrcLong";
        private const string ShortEntrySignal = "DUOrcShort";
        private const string LongFlipEntrySignal = "DUOrcFlipLong";
        private const string ShortFlipEntrySignal = "DUOrcFlipShort";
        private const string LongSecondaryEntrySignal = "DUOrcLongSecondary";
        private const string ShortSecondaryEntrySignal = "DUOrcShortSecondary";
        private Random entryVarianceRandom;
        private bool pendingEntryVarianceActive;
        private bool pendingEntryVarianceIsLong;
        private int pendingEntryVarianceQuantity;
        private double pendingEntryVarianceSignalEntryPrice;
        private double pendingEntryVarianceSignalStopPrice;
        private double pendingEntryVarianceTakeProfitPoints;
        private bool pendingEntryVarianceIsMarketEntry;
        private string pendingEntryVarianceSignalName = string.Empty;
        private DateTime pendingEntryVarianceDueUtc = Core.Globals.MinDate;
        private int pendingEntryVarianceSignalBar = -1;
        private int pendingEntryVarianceDelaySeconds;
        // TEMP: Remove this date block after the April 7, 2025 backtest isolation is no longer needed.
        private static readonly DateTime TemporaryBlockedTradingDate = new DateTime(2025, 4, 7);
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
            SessionSlot.NewYork3,
            SessionSlot.NewYork4,
            SessionSlot.NewYork5
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
2026-03-18,08:30
2026-03-18,14:00
2026-03-19,08:30
2026-03-26,08:30
2026-04-01,08:30
2026-04-02,08:30
2026-04-03,08:30
2026-04-08,14:00
2026-04-09,08:30
2026-04-10,08:30
2026-04-14,08:30
2026-04-21,08:30
2026-04-29,14:00
2026-04-30,08:30
2026-05-08,08:30
2026-05-12,08:30
2026-05-13,08:30
2026-05-14,08:30
2026-05-20,14:00
2026-05-28,08:30
2026-06-05,08:30
2026-06-10,08:30
2026-06-11,08:30
2026-06-17,08:30
2026-06-17,14:00
2026-06-25,08:30
2026-07-02,08:30
2026-07-08,14:00
2026-07-14,08:30
2026-07-15,08:30
2026-07-16,08:30
2026-07-29,14:00
2026-07-30,08:30
2026-07-31,08:30
2026-08-07,08:30
2026-08-12,08:30
2026-08-13,08:30
2026-08-14,08:30
2026-08-19,14:00
2026-08-26,08:30
2026-09-04,08:30
2026-09-10,08:30
2026-09-11,08:30
2026-09-16,08:30
2026-09-16,14:00
2026-09-30,08:30
2026-10-02,08:30
2026-10-07,14:00
2026-10-14,08:30
2026-10-15,08:30
2026-10-28,14:00
2026-10-29,08:30
2026-10-30,08:30
2026-11-06,08:30
2026-11-10,08:30
2026-11-13,08:30
2026-11-17,08:30
2026-11-18,14:00
2026-11-25,08:30
2026-12-04,08:30
2026-12-09,14:00
2026-12-10,08:30
2026-12-15,08:30
2026-12-16,08:30
2026-12-23,08:30
2026-12-30,14:00";
        private const string WeeklyNewsJsonUrl = "https://nfs.faireconomy.media/ff_calendar_thisweek.json";
        private const string NewsCacheFilePrefix = "AutoEdge.ff_weekly_news_cache.";
        private const string NewsCacheWeekPrefix = "# week-start-et=";
        private const string NewsCacheUpdatedPrefix = "# updated-utc=";
        private const string NewsTargetCurrency = "USD";
        private const string NewsTargetImpact = "High";
        private static readonly List<DateTime> NewsDates = new List<DateTime>();
        private static readonly object NewsDatesSync = new object();
        private static bool newsDatesInitialized;
        private static bool newsDatesAvailable;
        private static DateTime newsDatesWeekStart = DateTime.MinValue;
        private static string newsDatesSource = "disabled";
        private static DateTime newsFetchBlockedWeekStart = DateTime.MinValue;
        private static DateTime newsFetchBlockedUntilUtc = DateTime.MinValue;
        private static string newsFetchBlockedReason = string.Empty;
        private DateTime lastPrintedNewsWeekStart = DateTime.MinValue;
        private Border infoBoxContainer;
        private StackPanel infoBoxRowsPanel;
        private bool legacyInfoDrawingsCleared;
        private DateTime lastRealtimeInfoRefreshUtc = DateTime.MinValue;
        private bool useRealtimeInfoPreview;
        private DateTime realtimeInfoPreviewBarTime = DateTime.MinValue;
        private double realtimeInfoPreviewHigh = double.NaN;
        private double realtimeInfoPreviewLow = double.NaN;
        private double realtimeInfoPreviewClose = double.NaN;
        private const double RealtimeInfoRefreshSeconds = 1.0;
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
                Name = GetVersionedStrategyName("DUOrc");
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 2;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                IsInstantiatedOnEachOptimizationIteration = false;
                StartBehavior = StartBehavior.ImmediatelySubmitSynchronizeAccount;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;
                AsiaSessionStart = new TimeSpan(18, 40, 0);
                AsiaSessionEnd = new TimeSpan(20, 5, 0);
                AsiaEmaPeriod = 21;
                AsiaContracts = 1;
                AsiaEntryMinBodyPoints = 1;
                AsiaEntryCloseBeyondEmaPoints = 0.29;
                AsiaEntryType = InitialEntryType.Market;
                AsiaEntrySystemMode = EntrySystemMode.Primary;
                AsiaSecondaryContracts = 0;
                AsiaSecondaryEntryLimitDistanceFromEmaPoints = 0;
                AsiaSecondaryEntryStopDistanceFromEmaPoints = 0;
                AsiaSecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                AsiaSecondaryTakeProfitVariancePoints = 0;
                AsiaColorBlindEntry = false;
                AsiaEnableStopOutFlip = true;
                AsiaAdxPeriod = 14;
                AsiaAdxThreshold = 33.0;
                AsiaAdxMaxThreshold = 41.0;
                AsiaAdxPeakDrawdownExitUnits = 2;
                AsiaAdxAbsoluteExitLevel = 56.7;
                AsiaStopPaddingPoints = 18.98;
                AsiaTrailHardStop = true;
                AsiaTakeProfitPoints = 118.3;
                AsiaAtrMinimum = 22.58;
                AsiaCandleReversalExitBars = 7;
                AsiaCandleReversalCloseBeyondPoints = 0.34;
                AsiaCandleReversalMinBodyPoints = 0.17;

                Asia2SessionStart = new TimeSpan(20, 5, 0);
                Asia2SessionEnd = new TimeSpan(23, 30, 0);
                Asia2EmaPeriod = 21;
                Asia2Contracts = 1;
                Asia2EntryMinBodyPoints = 0;
                Asia2EntryCloseBeyondEmaPoints = 0.48;
                Asia2EntryType = InitialEntryType.Market;
                Asia2EntrySystemMode = EntrySystemMode.Primary;
                Asia2SecondaryContracts = 0;
                Asia2SecondaryEntryLimitDistanceFromEmaPoints = 0;
                Asia2SecondaryEntryStopDistanceFromEmaPoints = 0;
                Asia2SecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                Asia2SecondaryTakeProfitVariancePoints = 0;
                Asia2ColorBlindEntry = false;
                Asia2EnableStopOutFlip = false;
                Asia2AdxPeriod = 14;
                Asia2AdxThreshold = 12.0;
                Asia2AdxMaxThreshold = 20.0;
                Asia2AdxPeakDrawdownExitUnits = 2.14;
                Asia2AdxAbsoluteExitLevel = 24.1;
                Asia2StopPaddingPoints = 12.51;
                Asia2TrailHardStop = true;
                Asia2TakeProfitPoints = 63.9;
                Asia2AtrMinimum = 19.84;
                Asia2CandleReversalExitBars = 3;
                Asia2CandleReversalCloseBeyondPoints = 6.26;
                Asia2CandleReversalMinBodyPoints = 0.35;

                Asia3SessionStart = TimeSpan.Zero;
                Asia3SessionEnd = new TimeSpan(3, 5, 0);
                Asia3EmaPeriod = 21;
                Asia3Contracts = 1;
                Asia3EntryMinBodyPoints = 0.5;
                Asia3EntryCloseBeyondEmaPoints = 0.29;
                Asia3EntryType = InitialEntryType.Market;
                Asia3EntrySystemMode = EntrySystemMode.Primary;
                Asia3SecondaryContracts = 0;
                Asia3SecondaryEntryLimitDistanceFromEmaPoints = 0;
                Asia3SecondaryEntryStopDistanceFromEmaPoints = 0;
                Asia3SecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                Asia3SecondaryTakeProfitVariancePoints = 0;
                Asia3ColorBlindEntry = false;
                Asia3EnableStopOutFlip = true;
                Asia3AdxPeriod = 14;
                Asia3AdxThreshold = 27.0;
                Asia3AdxMaxThreshold = 35.0;
                Asia3AdxPeakDrawdownExitUnits = 4.28;
                Asia3AdxAbsoluteExitLevel = 43.2;
                Asia3StopPaddingPoints = 94.04;
                Asia3TrailHardStop = true;
                Asia3TakeProfitPoints = 255.1;
                Asia3AtrMinimum = 22.46;
                Asia3CandleReversalExitBars = 5;
                Asia3CandleReversalCloseBeyondPoints = 0.17;
                Asia3CandleReversalMinBodyPoints = 12.81;

                LondonSessionStart = new TimeSpan(3, 20, 0);
                LondonSessionEnd = new TimeSpan(5, 0, 0);
                AutoShiftLondon = true;
                LondonEmaPeriod = 21;
                LondonContracts = 1;
                LondonEntryMinBodyPoints = 1.5;
                LondonEntryCloseBeyondEmaPoints = 0.29;
                LondonEntryType = InitialEntryType.Market;
                LondonEntrySystemMode = EntrySystemMode.Primary;
                LondonSecondaryContracts = 0;
                LondonSecondaryEntryLimitDistanceFromEmaPoints = 0;
                LondonSecondaryEntryStopDistanceFromEmaPoints = 0;
                LondonSecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                LondonSecondaryTakeProfitVariancePoints = 0;
                LondonColorBlindEntry = false;
                LondonEnableStopOutFlip = true;
                LondonAdxPeriod = 14;
                LondonAdxThreshold = 12.0;
                LondonAdxMaxThreshold = 20.0;
                LondonAdxPeakDrawdownExitUnits = 15.86;
                LondonAdxAbsoluteExitLevel = 53.4;
                LondonStopPaddingPoints = 49.23;
                LondonTrailHardStop = false;
                LondonTakeProfitPoints = 241.6;
                LondonAtrMinimum = 12.62;
                LondonCandleReversalExitBars = 14;
                LondonCandleReversalCloseBeyondPoints = 5.75;
                LondonCandleReversalMinBodyPoints = 2.18;

                London2SessionStart = new TimeSpan(5, 0, 0);
                London2SessionEnd = new TimeSpan(5, 41, 0);
                AutoShiftLondon2 = false;
                London2EmaPeriod = 21;
                London2Contracts = 1;
                London2EntryMinBodyPoints = 1.5;
                London2EntryCloseBeyondEmaPoints = 1.21;
                London2EntryType = InitialEntryType.Market;
                London2EntrySystemMode = EntrySystemMode.Primary;
                London2SecondaryContracts = 0;
                London2SecondaryEntryLimitDistanceFromEmaPoints = 0;
                London2SecondaryEntryStopDistanceFromEmaPoints = 0;
                London2SecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                London2SecondaryTakeProfitVariancePoints = 0;
                London2ColorBlindEntry = false;
                London2EnableStopOutFlip = true;
                London2AdxPeriod = 14;
                London2AdxThreshold = 13.0;
                London2AdxMaxThreshold = 21.0;
                London2AdxPeakDrawdownExitUnits = 9.66;
                London2AdxAbsoluteExitLevel = 55.7;
                London2StopPaddingPoints = 70.09;
                London2TrailHardStop = true;
                London2TakeProfitPoints = 240.5;
                London2AtrMinimum = 3.7;
                London2CandleReversalExitBars = 14;
                London2CandleReversalCloseBeyondPoints = 4.26;
                London2CandleReversalMinBodyPoints = 0.4;

                London3SessionStart = new TimeSpan(6, 15, 0);
                London3SessionEnd = new TimeSpan(6, 48, 0);
                London3FlatByTime = "08:25";
                AutoShiftLondon3 = true;
                London3EmaPeriod = 21;
                London3Contracts = 1;
                London3EntryMinBodyPoints = 0.25;
                London3EntryCloseBeyondEmaPoints = 0.32;
                London3EntryType = InitialEntryType.Market;
                London3EntrySystemMode = EntrySystemMode.Primary;
                London3SecondaryContracts = 0;
                London3SecondaryEntryLimitDistanceFromEmaPoints = 0;
                London3SecondaryEntryStopDistanceFromEmaPoints = 0;
                London3SecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                London3SecondaryTakeProfitVariancePoints = 0;
                London3ColorBlindEntry = false;
                London3EnableStopOutFlip = true;
                London3AdxPeriod = 14;
                London3AdxThreshold = 15.0;
                London3AdxMaxThreshold = 23.0;
                London3AdxPeakDrawdownExitUnits = 8.61;
                London3AdxAbsoluteExitLevel = 30.1;
                London3StopPaddingPoints = 49.11;
                London3TrailHardStop = true;
                London3TakeProfitPoints = 153.4;
                London3AtrMinimum = 12.02;
                London3CandleReversalExitBars = 9;
                London3CandleReversalCloseBeyondPoints = 0.13;
                London3CandleReversalMinBodyPoints = 2.18;

                NewYorkSessionStart = new TimeSpan(9, 35, 0);
                NewYorkSessionEnd = new TimeSpan(10, 0, 0);
                NewYorkEmaPeriod = 21;
                NewYorkContracts = 1;
                NewYorkEntryMinBodyPoints = 3.75;
                NewYorkEntryCloseBeyondEmaPoints = 0.29;
                NewYorkEntryType = InitialEntryType.Market;
                NewYorkEntrySystemMode = EntrySystemMode.Primary;
                NewYorkSecondaryContracts = 0;
                NewYorkSecondaryEntryLimitDistanceFromEmaPoints = 0;
                NewYorkSecondaryEntryStopDistanceFromEmaPoints = 0;
                NewYorkSecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                NewYorkSecondaryTakeProfitVariancePoints = 0;
                NewYorkColorBlindEntry = false;
                NewYorkEnableStopOutFlip = true;
                NewYorkAdxPeriod = 14;
                NewYorkAdxThreshold = 27.0;
                NewYorkAdxMaxThreshold = 37.0;
                NewYorkAdxPeakDrawdownExitUnits = 2;
                NewYorkAdxAbsoluteExitLevel = 40.5;
                NewYorkStopPaddingPoints = 14.12;
                NewYorkTrailHardStop = true;
                NewYorkTakeProfitPoints = 218.7;
                NewYorkAtrMinimum = 1.13;
                NewYorkCandleReversalExitBars = 4;
                NewYorkCandleReversalCloseBeyondPoints = 0.17;
                NewYorkCandleReversalMinBodyPoints = 0.17;

                NewYork2SessionStart = new TimeSpan(10, 5, 0);
                NewYork2SessionEnd = new TimeSpan(11, 0, 0);
                NewYork2EmaPeriod = 21;
                NewYork2Contracts = 1;
                NewYork2EntryMinBodyPoints = 0.5;
                NewYork2EntryCloseBeyondEmaPoints = 0.29;
                NewYork2EntryType = InitialEntryType.Market;
                NewYork2EntrySystemMode = EntrySystemMode.Primary;
                NewYork2SecondaryContracts = 0;
                NewYork2SecondaryEntryLimitDistanceFromEmaPoints = 0;
                NewYork2SecondaryEntryStopDistanceFromEmaPoints = 0;
                NewYork2SecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                NewYork2SecondaryTakeProfitVariancePoints = 0;
                NewYork2ColorBlindEntry = false;
                NewYork2EnableStopOutFlip = true;
                NewYork2AdxPeriod = 14;
                NewYork2AdxThreshold = 13.0;
                NewYork2AdxMaxThreshold = 23.0;
                NewYork2AdxPeakDrawdownExitUnits = 6.21;
                NewYork2AdxAbsoluteExitLevel = 43;
                NewYork2StopPaddingPoints = 43.44;
                NewYork2TrailHardStop = true;
                NewYork2TakeProfitPoints = 198.2;
                NewYork2AtrMinimum = 2.23;
                NewYork2CandleReversalExitBars = 19;
                NewYork2CandleReversalCloseBeyondPoints = 1.75;
                NewYork2CandleReversalMinBodyPoints = 0.28;

                NewYork3SessionStart = new TimeSpan(11, 0, 0);
                NewYork3SessionEnd = new TimeSpan(12, 0, 0);
                NewYork3EmaPeriod = 21;
                NewYork3Contracts = 1;
                NewYork3EntryMinBodyPoints = 0;
                NewYork3EntryCloseBeyondEmaPoints = 0.29;
                NewYork3EntryType = InitialEntryType.Market;
                NewYork3EntrySystemMode = EntrySystemMode.Primary;
                NewYork3SecondaryContracts = 0;
                NewYork3SecondaryEntryLimitDistanceFromEmaPoints = 0;
                NewYork3SecondaryEntryStopDistanceFromEmaPoints = 0;
                NewYork3SecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                NewYork3SecondaryTakeProfitVariancePoints = 0;
                NewYork3ColorBlindEntry = false;
                NewYork3EnableStopOutFlip = false;
                NewYork3AdxPeriod = 14;
                NewYork3AdxThreshold = 12.0;
                NewYork3AdxMaxThreshold = 20.0;
                NewYork3AdxPeakDrawdownExitUnits = 2.24;
                NewYork3AdxAbsoluteExitLevel = 21.9;
                NewYork3StopPaddingPoints = 15.88;
                NewYork3TrailHardStop = true;
                NewYork3TakeProfitPoints = 63.2;
                NewYork3AtrMinimum = 1.67;
                NewYork3CandleReversalExitBars = 4;
                NewYork3CandleReversalCloseBeyondPoints = 0.17;
                NewYork3CandleReversalMinBodyPoints = 0.17;

                NewYork4SessionStart = new TimeSpan(12, 10, 0);
                NewYork4SessionEnd = new TimeSpan(13, 0, 0);
                NewYork4EmaPeriod = 21;
                NewYork4Contracts = 1;
                NewYork4EntryMinBodyPoints = 5.5;
                NewYork4EntryCloseBeyondEmaPoints = 0.29;
                NewYork4EntryType = InitialEntryType.Market;
                NewYork4EntrySystemMode = EntrySystemMode.Primary;
                NewYork4SecondaryContracts = 0;
                NewYork4SecondaryEntryLimitDistanceFromEmaPoints = 0;
                NewYork4SecondaryEntryStopDistanceFromEmaPoints = 0;
                NewYork4SecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                NewYork4SecondaryTakeProfitVariancePoints = 0;
                NewYork4ColorBlindEntry = false;
                NewYork4EnableStopOutFlip = false;
                NewYork4AdxPeriod = 14;
                NewYork4AdxThreshold = 5.0;
                NewYork4AdxMaxThreshold = 20.0;
                NewYork4AdxPeakDrawdownExitUnits = 2;
                NewYork4AdxAbsoluteExitLevel = 30.5;
                NewYork4StopPaddingPoints = 80.86;
                NewYork4TrailHardStop = true;
                NewYork4TakeProfitPoints = 337.1;
                NewYork4AtrMinimum = 49.59;
                NewYork4CandleReversalExitBars = 10;
                NewYork4CandleReversalCloseBeyondPoints = 0.17;
                NewYork4CandleReversalMinBodyPoints = 0.17;

                NewYork5SessionStart = new TimeSpan(13, 35, 0);
                NewYork5SessionEnd = new TimeSpan(15, 5, 0);
                NewYork5EmaPeriod = 21;
                NewYork5Contracts = 1;
                NewYork5EntryMinBodyPoints = 2.75;
                NewYork5EntryCloseBeyondEmaPoints = 0.29;
                NewYork5EntryType = InitialEntryType.Market;
                NewYork5EntrySystemMode = EntrySystemMode.Primary;
                NewYork5SecondaryContracts = 0;
                NewYork5SecondaryEntryLimitDistanceFromEmaPoints = 0;
                NewYork5SecondaryEntryStopDistanceFromEmaPoints = 0;
                NewYork5SecondaryEntryTakeProfitDistanceFromEmaPoints = 0;
                NewYork5SecondaryTakeProfitVariancePoints = 0;
                NewYork5ColorBlindEntry = false;
                NewYork5EnableStopOutFlip = true;
                NewYork5AdxPeriod = 21;
                NewYork5AdxThreshold = 17.0;
                NewYork5AdxMaxThreshold = 25.0;
                NewYork5AdxPeakDrawdownExitUnits = 7.54;
                NewYork5AdxAbsoluteExitLevel = 22.3;
                NewYork5StopPaddingPoints = 95.42;
                NewYork5TrailHardStop = true;
                NewYork5TakeProfitPoints = 184.4;
                NewYork5AtrMinimum = 27.06;
                NewYork5CandleReversalExitBars = 21;
                NewYork5CandleReversalCloseBeyondPoints = 0.17;
                NewYork5CandleReversalMinBodyPoints = 1.26;

                CloseAtSessionEnd = false;
                ForceCloseTime = "16:55:00";
                AsiaSessionBrush = Brushes.DarkCyan;
                LondonSessionBrush = Brushes.MediumSeaGreen;
                NewYorkSessionBrush = Brushes.Gold;
                SecondaryBiasEmaPeriod = 0;
                ShowEmaOnChart = false;
                ShowAdxOnChart = false;
                ShowAdxThresholdLines = false;
                ShowAtrOnChart = false;
                ShowAtrThresholdLines = false;
                UseNewsSkip = false;
                NewsBlockMinutes = 1;

                WebhookUrl = "";
                WebhookTickerOverride = "";
                WebhookProviderType = WebhookProvider.TradersPost;
                ProjectXApiBaseUrl = "https://api.topstepx.com";
                ProjectXTradeAllAccounts = false;
                ProjectXUsername = "";
                ProjectXApiKey = "";
                ProjectXAccountId = "";
                ProjectXContractId = "";
                MaxAccountBalance = 0;
                RequireEntryConfirmation = false;
                SecondaryTrailStop = false;
                EntryVariance = true;
                DebugLogging = false;
            }
            else if (State == State.DataLoaded)
            {
                ValidateRequiredPrimaryTimeframe(5);
                ValidateRequiredPrimaryInstrument();

                ShowEmaOnChart = false;
                ShowAdxOnChart = false;
                ShowAdxThresholdLines = false;
                ShowAtrOnChart = false;
                ShowAtrThresholdLines = false;

                emaAsia = EMA(AsiaEmaPeriod);
                emaAsia2 = EMA(Asia2EmaPeriod);
                emaAsia3 = EMA(Asia3EmaPeriod);
                emaLondon = EMA(LondonEmaPeriod);
                emaLondon2 = EMA(London2EmaPeriod);
                emaLondon3 = EMA(London3EmaPeriod);
                emaNewYork = EMA(NewYorkEmaPeriod);
                emaNewYork2 = EMA(NewYork2EmaPeriod);
                emaNewYork3 = EMA(NewYork3EmaPeriod);
                emaNewYork4 = EMA(NewYork4EmaPeriod);
                emaNewYork5 = EMA(NewYork5EmaPeriod);
                secondaryBiasEma = SecondaryBiasEmaPeriod > 0 ? EMA(SecondaryBiasEmaPeriod) : null;
                entryAtr = ATR(EntryAtrPeriod);
                atrVisual = DUOAtrVisual(EntryAtrPeriod);
                adxAsia = DM(AsiaAdxPeriod);
                adxAsia2 = DM(Asia2AdxPeriod);
                adxAsia3 = DM(Asia3AdxPeriod);
                adxLondon = DM(LondonAdxPeriod);
                adxLondon2 = DM(London2AdxPeriod);
                adxLondon3 = DM(London3AdxPeriod);
                adxNewYork = DM(NewYorkAdxPeriod);
                adxNewYork2 = DM(NewYork2AdxPeriod);
                adxNewYork3 = DM(NewYork3AdxPeriod);
                adxNewYork4 = DM(NewYork4AdxPeriod);
                adxNewYork5 = DM(NewYork5AdxPeriod);
                UpdateAdxReferenceLines(adxAsia, AsiaAdxThreshold, AsiaAdxMaxThreshold);
                UpdateAdxReferenceLines(adxAsia2, Asia2AdxThreshold, Asia2AdxMaxThreshold);
                UpdateAdxReferenceLines(adxAsia3, Asia3AdxThreshold, Asia3AdxMaxThreshold);
                UpdateAdxReferenceLines(adxLondon, LondonAdxThreshold, LondonAdxMaxThreshold);
                UpdateAdxReferenceLines(adxLondon2, London2AdxThreshold, London2AdxMaxThreshold);
                UpdateAdxReferenceLines(adxLondon3, London3AdxThreshold, London3AdxMaxThreshold);
                UpdateAdxReferenceLines(adxNewYork, NewYorkAdxThreshold, NewYorkAdxMaxThreshold);
                UpdateAdxReferenceLines(adxNewYork2, NewYork2AdxThreshold, NewYork2AdxMaxThreshold);
                UpdateAdxReferenceLines(adxNewYork3, NewYork3AdxThreshold, NewYork3AdxMaxThreshold);
                UpdateAdxReferenceLines(adxNewYork4, NewYork4AdxThreshold, NewYork4AdxMaxThreshold);
                UpdateAdxReferenceLines(adxNewYork5, NewYork5AdxThreshold, NewYork5AdxMaxThreshold);

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
                    AddChartIndicator(emaNewYork4);
                    AddChartIndicator(emaNewYork5);
                    if (secondaryBiasEma != null)
                        AddChartIndicator(secondaryBiasEma);
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
                    AddChartIndicator(adxNewYork4);
                    AddChartIndicator(adxNewYork5);
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
                tradeLineSignalBar = -1;
                tradeLineExitBar = -1;
                tradeLineEntryPrice = 0.0;
                tradeLineTpPrice = 0.0;
                tradeLineSlPrice = 0.0;
                entryVarianceRandom = new Random(unchecked(Environment.TickCount ^ GetHashCode()));
                secondaryTakeProfitVarianceRandom = new Random(unchecked(Environment.TickCount ^ GetHashCode() ^ 0x5A17));
                ResetPendingEntryVariance();
                ResetSecondaryEntryState();
                lock (tradeLineSnapshotsSync)
                    historicalTradeLines.Clear();
                ApplyInputsForSession(activeSession);
                UpdateEmaPlotVisibility();
                UpdateAdxPlotVisibility();
                UpdateAtrPlotVisibility();
                targetTimeZone = null;
                londonTimeZone = null;
                newYorkTimeZone = null;
                pendingLongStopForWebhook = 0.0;
                pendingShortStopForWebhook = 0.0;
                currentTradePeakAdx = 0.0;
                trackedAdxPeakPosition = MarketPosition.Flat;
                initialStopPrice = 0.0;
                currentStopPrice = 0.0;
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
                newYork4SessionClosed = false;
                newYork5SessionClosed = false;
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
                lastStopOutFlipSubmittedBar = -1;
                lastPrintedNewsWeekStart = DateTime.MinValue;

                EnsureNewsDatesInitialized(GetNewsReferenceStrategyTime(), true, true);
                heartbeatReporter = new StrategyHeartbeatReporter(
                    HeartbeatStrategyName,
                    System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv"));

                LogDebug(
                    string.Format(
                        "DataLoaded | ActiveSession={0} EMA={1} ADX={2}/{3:0.##} Contracts={4} TP={5:0.##} SLPad={6:0.##} ATRMin={7:0.##}",
                        FormatSessionLabel(activeSession),
                        activeEmaPeriod,
                        activeAdxPeriod,
                        activeAdxThreshold,
                        activeContracts,
                        activeTakeProfitPoints,
                        activeStopPaddingPoints,
                        activeMinimumAtrForEntry));
            }
            else if (State == State.Realtime)
            {
                EnsureNewsDatesInitialized(GetNewsReferenceStrategyTime(), true, true);
                UpdateInfo();

                if (heartbeatReporter != null)
                    heartbeatReporter.Start();

                RunProjectXStartupPreflight();
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
            if (BarsInProgress != 0)
                return;

            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
            {
                CancelWorkingEntryOrders();
                if (Position.MarketPosition == MarketPosition.Long)
                    TrySubmitTerminalExit("InvalidConfiguration");
                else if (Position.MarketPosition == MarketPosition.Short)
                    TrySubmitTerminalExit("InvalidConfiguration");
                return;
            }

            if (CurrentBar < Math.Max(1, Math.Max(GetMaxConfiguredEmaPeriod(), GetMaxConfiguredAdxPeriod())))
                return;

            DrawSessionBackgrounds();
            DrawNewsWindows(Time[0]);
            UpdateTradeLines();

            foreach (SessionSlot slot in ConfigurableSessionSlots)
                ProcessSessionTransitions(slot);
            ReconcileTrackedEntryOrders();
            ReconcileTrackedProtectiveOrders();
            CancelWrongSideProtectiveOrders("bar-watchdog");
            SyncTradeLinesToLivePositionAndOrders();

            if (CheckTerminalExitOverfill("bar-watchdog"))
                return;

            UpdateActiveSession(Time[0]);
            UpdateEmaPlotVisibility();
            UpdateAdxPlotVisibility();
            UpdateAtrPlotVisibility();
            UpdateInfo();

            if (AuditPositionProtection("bar-watchdog"))
                return;

            if (IsTerminalExitInFlight())
                return;

            if (IsForceCloseTimeReached(Time[0]))
            {
                CancelWorkingEntryOrders();
                if (Position.MarketPosition == MarketPosition.Long)
                    TrySubmitTerminalExit("ForceClose");
                else if (Position.MarketPosition == MarketPosition.Short)
                    TrySubmitTerminalExit("ForceClose");
                return;
            }

            if (IsTemporaryBlockedTradingDate(Time[0]))
            {
                CancelWorkingEntryOrders();
                if (Position.MarketPosition == MarketPosition.Long)
                    TrySubmitTerminalExit("TemporaryDateBlock");
                else if (Position.MarketPosition == MarketPosition.Short)
                    TrySubmitTerminalExit("TemporaryDateBlock");
                LogDebug("Temporary date block active | date=2025-04-07");
                return;
            }

            bool inNewsSkipNow = TimeInNewsSkip(Time[0]);
            bool inNewsSkipPrev = CurrentBar > 0 && IsSameNewsWeek(Time[0], Time[1]) && TimeInNewsSkip(Time[1]);
            if (!inNewsSkipPrev && inNewsSkipNow)
            {
                CancelWorkingEntryOrders();
                if (Position.MarketPosition == MarketPosition.Long)
                    TrySubmitTerminalExit("NewsSkip");
                else if (Position.MarketPosition == MarketPosition.Short)
                    TrySubmitTerminalExit("NewsSkip");
                LogDebug("Entered news block: canceling working entries and flattening open position.");
                return;
            }

            bool inActiveSessionNow = activeSession != SessionSlot.None && TimeInSession(activeSession, Time[0]);
            bool accountBlocked = IsAccountBalanceBlocked();
            double adxValue = activeAdx != null ? activeAdx[0] : 0.0;
            double atrValue = GetCurrentAtrValue();
            UpdateAdxPeakTracker(adxValue);
            bool adxMinPass = !inActiveSessionNow || activeAdxThreshold <= 0.0 || adxValue >= activeAdxThreshold;
            bool adxMaxPass = !inActiveSessionNow || activeAdxMaxThreshold <= 0.0 || adxValue <= activeAdxMaxThreshold;
            bool adxThresholdPass = adxMinPass && adxMaxPass;
            bool atrMinPass = !inActiveSessionNow || activeMinimumAtrForEntry <= 0.0 || atrValue >= activeMinimumAtrForEntry;
            bool canTradeNow = inActiveSessionNow && !inNewsSkipNow && !accountBlocked && adxThresholdPass && atrMinPass;
            double secondaryBiasReferencePrice = Close[0];
            double secondaryBiasEmaValue = GetSecondaryBiasEmaValue();
            bool biasAllowLong = IsSecondaryBiasDirectionAllowed(true, secondaryBiasReferencePrice);
            bool biasAllowShort = IsSecondaryBiasDirectionAllowed(false, secondaryBiasReferencePrice);
            bool allowLong = biasAllowLong;
            bool allowShort = biasAllowShort;

            if (!inActiveSessionNow)
                CancelWorkingEntryOrders();

            if (activeEma == null || CurrentBar < activeEmaPeriod)
                return;

            if (IsLondon3FlatByTimeReached(Time[0]))
            {
                CancelWorkingEntryOrders();
                if (Position.MarketPosition == MarketPosition.Long)
                    TrySubmitTerminalExit("London3FlatByTime");
                else if (Position.MarketPosition == MarketPosition.Short)
                    TrySubmitTerminalExit("London3FlatByTime");

                LogDebug(string.Format(
                    "Europe3 flat-by-time reached | now={0:HH:mm} configured={1} side={2}",
                    Time[0],
                    string.IsNullOrWhiteSpace(London3FlatByTime) ? "Off" : London3FlatByTime,
                    Position.MarketPosition));
                return;
            }

            double emaValue = activeEma[0];
            double entryBodyPoints = Math.Abs(Close[0] - Open[0]);
            bool entryBodyPasses = activeEntryMinBodyPoints <= 0.0 || entryBodyPoints >= activeEntryMinBodyPoints;
            bool longSignalBase = LongEntrySignalBase(Open[0], Close[0], emaValue);
            bool shortSignalBase = ShortEntrySignalBase(Open[0], Close[0], emaValue);
            bool longEntryCloseBeyondEmaPasses = EntryCloseBeyondEmaPasses(true, Close[0], emaValue);
            bool shortEntryCloseBeyondEmaPasses = EntryCloseBeyondEmaPasses(false, Close[0], emaValue);
            bool longSignalRaw = longSignalBase && entryBodyPasses && longEntryCloseBeyondEmaPasses;
            bool shortSignalRaw = shortSignalBase && entryBodyPasses && shortEntryCloseBeyondEmaPasses;
            bool longSignal = longSignalRaw && allowLong;
            bool shortSignal = shortSignalRaw && allowShort;

            TryTrailSecondaryStop(emaValue);

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (IsSecondaryTrailOnlyPosition())
                    return;

                if (TrySubmitStopOutFlip(false, canTradeNow, shortSignal, emaValue))
                    return;

                if (activeAdxAbsoluteExitLevel > 0.0 && adxValue >= activeAdxAbsoluteExitLevel)
                {
                    if (TrySubmitTerminalExit("AdxLevelExit"))
                    {
                        LogDebug(string.Format("Exit LONG | reason=AdxLevel adx={0:0.00} threshold={1:0.00}",
                            adxValue, activeAdxAbsoluteExitLevel));
                    }
                    return;
                }

                double adxDrawdown;
                if (ShouldExitOnAdxDrawdown(adxValue, out adxDrawdown))
                {
                    if (TrySubmitTerminalExit("AdxDrawdownExit"))
                    {
                        LogDebug(string.Format("Exit LONG | reason=AdxDrawdown adx={0:0.00} peak={1:0.00} drawdown={2:0.00} threshold={3:0.00}",
                            adxValue, currentTradePeakAdx, adxDrawdown, activeAdxPeakDrawdownExitUnits));
                    }
                    return;
                }

                double activePositionTakeProfitPoints = GetActivePositionTakeProfitPoints();
                if (activePositionTakeProfitPoints > 0.0 && Close[0] >= Position.AveragePrice + activePositionTakeProfitPoints)
                {
                    if (TrySubmitTerminalExit("TakeProfitExit"))
                    {
                        LogDebug(string.Format("Exit LONG | reason=TakeProfit close={0:0.00} avg={1:0.00} tpPts={2:0.00}",
                            Close[0], Position.AveragePrice, activePositionTakeProfitPoints));
                    }
                    return;
                }

                if (TryExitCandleReversal())
                    return;

                TryTrailHardStop(emaValue);

                TryProcessSecondaryEntryDoor(true, canTradeNow, allowLong, longSignalRaw);

                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (IsSecondaryTrailOnlyPosition())
                    return;

                if (TrySubmitStopOutFlip(true, canTradeNow, longSignal, emaValue))
                    return;

                if (activeAdxAbsoluteExitLevel > 0.0 && adxValue >= activeAdxAbsoluteExitLevel)
                {
                    if (TrySubmitTerminalExit("AdxLevelExit"))
                    {
                        LogDebug(string.Format("Exit SHORT | reason=AdxLevel adx={0:0.00} threshold={1:0.00}",
                            adxValue, activeAdxAbsoluteExitLevel));
                    }
                    return;
                }

                double adxDrawdown;
                if (ShouldExitOnAdxDrawdown(adxValue, out adxDrawdown))
                {
                    if (TrySubmitTerminalExit("AdxDrawdownExit"))
                    {
                        LogDebug(string.Format("Exit SHORT | reason=AdxDrawdown adx={0:0.00} peak={1:0.00} drawdown={2:0.00} threshold={3:0.00}",
                            adxValue, currentTradePeakAdx, adxDrawdown, activeAdxPeakDrawdownExitUnits));
                    }
                    return;
                }

                double activePositionTakeProfitPoints = GetActivePositionTakeProfitPoints();
                if (activePositionTakeProfitPoints > 0.0 && Close[0] <= Position.AveragePrice - activePositionTakeProfitPoints)
                {
                    if (TrySubmitTerminalExit("TakeProfitExit"))
                    {
                        LogDebug(string.Format("Exit SHORT | reason=TakeProfit close={0:0.00} avg={1:0.00} tpPts={2:0.00}",
                            Close[0], Position.AveragePrice, activePositionTakeProfitPoints));
                    }
                    return;
                }

                if (TryExitCandleReversal())
                    return;

                TryTrailHardStop(emaValue);

                TryProcessSecondaryEntryDoor(false, canTradeNow, allowShort, shortSignalRaw);

                return;
            }

            if (DebugLogging && Position.MarketPosition == MarketPosition.Flat)
            {
                if (longSignalRaw && !allowLong)
                {
                    LogDebug(string.Format(
                        "Setup blocked | side=Long close={0:0.00} ema={1:0.00} reasons={2}",
                        Close[0],
                        emaValue,
                        GetDirectionalBlockReason(true, biasAllowLong, secondaryBiasReferencePrice, secondaryBiasEmaValue)));
                }
                else if (shortSignalRaw && !allowShort)
                {
                    LogDebug(string.Format(
                        "Setup blocked | side=Short close={0:0.00} ema={1:0.00} reasons={2}",
                        Close[0],
                        emaValue,
                        GetDirectionalBlockReason(false, biasAllowShort, secondaryBiasReferencePrice, secondaryBiasEmaValue)));
                }
            }

            if (!canTradeNow)
            {
                CancelWorkingEntryOrders();

                if (DebugLogging && Position.MarketPosition == MarketPosition.Flat && (longSignal || shortSignal))
                {
                    string setupSide = longSignal ? "Long" : "Short";
                    var reasons = new List<string>();

                    if (!inActiveSessionNow)
                        reasons.Add(string.Format("OutOfSession active={0}", FormatSessionLabel(activeSession)));
                    if (inNewsSkipNow)
                        reasons.Add(string.Format("NewsSkip minutes={0}", NewsBlockMinutes));
                    if (accountBlocked)
                        reasons.Add(string.Format("AccountBlocked maxBalance={0:0.##}", MaxAccountBalance));
                    if (!adxMinPass)
                        reasons.Add(string.Format("AdxBelowMin adx={0:0.00} min={1:0.00}", adxValue, activeAdxThreshold));
                    if (!adxMaxPass)
                        reasons.Add(string.Format("AdxAboveMax adx={0:0.00} max={1:0.00}", adxValue, activeAdxMaxThreshold));
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

            if (!entryBodyPasses)
            {
                CancelWorkingEntryOrders();

                bool bodyBlockedLong = longSignalBase;
                bool bodyBlockedShort = shortSignalBase;
                if (DebugLogging && Position.MarketPosition == MarketPosition.Flat && (bodyBlockedLong || bodyBlockedShort))
                {
                    LogDebug(string.Format(
                        "Setup blocked | side={0} close={1:0.00} ema={2:0.00} reasons=EntryBodyBelowMin body={3:0.00} min={4:0.00}",
                        bodyBlockedLong ? "Long" : "Short",
                        Close[0],
                        emaValue,
                        entryBodyPoints,
                        activeEntryMinBodyPoints));
                }
                return;
            }

            if (activeEntryCloseBeyondEmaPoints > 0.0 && ((longSignalBase && !longEntryCloseBeyondEmaPasses) || (shortSignalBase && !shortEntryCloseBeyondEmaPasses)))
            {
                CancelWorkingEntryOrders();

                bool closeBlockedLong = longSignalBase && !longEntryCloseBeyondEmaPasses;
                double distancePoints = GetEntryCloseBeyondEmaDistancePoints(closeBlockedLong, Close[0], emaValue);
                if (DebugLogging && Position.MarketPosition == MarketPosition.Flat)
                {
                    LogDebug(string.Format(
                        "Setup blocked | side={0} close={1:0.00} ema={2:0.00} reasons=EntryCloseNearEmaPoints distancePoints={3:0.##} minPoints={4:0.##}",
                        closeBlockedLong ? "Long" : "Short",
                        Close[0],
                        emaValue,
                        distancePoints,
                        activeEntryCloseBeyondEmaPoints));
                }
                return;
            }

            if (!longSignal && !shortSignal)
            {
                CancelWorkingEntryOrders();
                return;
            }

            TryProcessSecondaryEntryDoor(true, canTradeNow, allowLong, longSignalRaw);
            TryProcessSecondaryEntryDoor(false, canTradeNow, allowShort, shortSignalRaw);

            if (longSignal)
            {
                bool secondaryEntriesEnabled = AllowsSecondaryEntries();
                bool primaryEntriesEnabled = AllowsPrimaryEntries();

                if (secondaryEntriesEnabled)
                    ArmSecondaryEntryDoor(true);

                LogDebug(string.Format("Setup ready | side=Long session={0} close={1:0.00} ema={2:0.00}", FormatSessionLabel(activeSession), Close[0], emaValue));

                CancelOrderIfActive(shortEntryOrder, "OppositeLongSignal");
                if (!primaryEntriesEnabled)
                {
                    LogDebug("Primary LONG skipped | reason=EntrySystemMode");
                    TryProcessSecondaryEntryDoor(true, canTradeNow, allowLong, longSignalRaw);
                    return;
                }

                BeginTradeAttempt("Long");
                bool longOrderActive = IsOrderActive(longEntryOrder) || IsPendingEntryVarianceFor(true);
                double entryPrice = GetPreparedEntryPrice(true, emaValue);
                double stopPrice = BuildLongEntryStopPrice(entryPrice, emaValue);
                int qty = GetEntryQuantity();
                if (qty <= 0)
                {
                    CancelWorkingEntryOrders();
                    LogDebug("LONG signal skipped | reason=contracts-disabled");
                    EndTradeAttempt("contracts-disabled");
                    return;
                }

                double takeProfitPoints = GetConfiguredEntryTakeProfitPoints();
                double takeProfitPrice = GetWebhookTakeProfitPrice(entryPrice, takeProfitPoints, true);

                if (longOrderActive)
                {
                    if (!HasActiveSecondaryEntryOrder(true) && TryMaintainActiveLimitEntry(true, qty, entryPrice, stopPrice, takeProfitPoints, takeProfitPrice, LongEntrySignal))
                        return;

                    if (DebugLogging)
                    {
                        LogDebug(string.Format("LONG signal skipped | reason=longOrderActive tracked={0}", FormatOrderRef(longEntryOrder)));
                        EndTradeAttempt("entry-order-active");
                    }
                    return;
                }

                if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", entryPrice, qty))
                {
                    LogDebug("Entry confirmation declined | LONG.");
                    EndTradeAttempt("confirmation-declined");
                    return;
                }

                SubmitPreparedInitialEntry(true, qty, entryPrice, stopPrice, takeProfitPoints, takeProfitPrice, LongEntrySignal, IsMarketEntryMode());
                LogDebug(string.Format("Place LONG {0} | session={1} entry={2:0.00} stop={3:0.00} qty={4}", FormatActiveEntryType(), FormatSessionLabel(activeSession), entryPrice, stopPrice, qty));
                TryProcessSecondaryEntryDoor(true, canTradeNow, allowLong, longSignalRaw);
            }
            else if (shortSignal)
            {
                bool secondaryEntriesEnabled = AllowsSecondaryEntries();
                bool primaryEntriesEnabled = AllowsPrimaryEntries();

                if (secondaryEntriesEnabled)
                    ArmSecondaryEntryDoor(false);

                LogDebug(string.Format("Setup ready | side=Short session={0} close={1:0.00} ema={2:0.00}", FormatSessionLabel(activeSession), Close[0], emaValue));

                CancelOrderIfActive(longEntryOrder, "OppositeShortSignal");
                if (!primaryEntriesEnabled)
                {
                    LogDebug("Primary SHORT skipped | reason=EntrySystemMode");
                    TryProcessSecondaryEntryDoor(false, canTradeNow, allowShort, shortSignalRaw);
                    return;
                }

                BeginTradeAttempt("Short");
                bool shortOrderActive = IsOrderActive(shortEntryOrder) || IsPendingEntryVarianceFor(false);
                double entryPrice = GetPreparedEntryPrice(false, emaValue);
                double stopPrice = BuildShortEntryStopPrice(entryPrice, emaValue);
                int qty = GetEntryQuantity();
                if (qty <= 0)
                {
                    CancelWorkingEntryOrders();
                    LogDebug("SHORT signal skipped | reason=contracts-disabled");
                    EndTradeAttempt("contracts-disabled");
                    return;
                }

                double takeProfitPoints = GetConfiguredEntryTakeProfitPoints();
                double takeProfitPrice = GetWebhookTakeProfitPrice(entryPrice, takeProfitPoints, false);

                if (shortOrderActive)
                {
                    if (!HasActiveSecondaryEntryOrder(false) && TryMaintainActiveLimitEntry(false, qty, entryPrice, stopPrice, takeProfitPoints, takeProfitPrice, ShortEntrySignal))
                        return;

                    if (DebugLogging)
                    {
                        LogDebug(string.Format("SHORT signal skipped | reason=shortOrderActive tracked={0}", FormatOrderRef(shortEntryOrder)));
                        EndTradeAttempt("entry-order-active");
                    }
                    return;
                }

                if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", entryPrice, qty))
                {
                    LogDebug("Entry confirmation declined | SHORT.");
                    EndTradeAttempt("confirmation-declined");
                    return;
                }

                SubmitPreparedInitialEntry(false, qty, entryPrice, stopPrice, takeProfitPoints, takeProfitPrice, ShortEntrySignal, IsMarketEntryMode());
                LogDebug(string.Format("Place SHORT {0} | session={1} entry={2:0.00} stop={3:0.00} qty={4}", FormatActiveEntryType(), FormatSessionLabel(activeSession), entryPrice, stopPrice, qty));
                TryProcessSecondaryEntryDoor(false, canTradeNow, allowShort, shortSignalRaw);
            }
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate == null || marketDataUpdate.MarketDataType != MarketDataType.Last)
                return;

            TrackRealtimeInfoPreview(marketDataUpdate.Price);
            RefreshInfoFromRealtimeTick();
            ProcessPendingEntryVariance(marketDataUpdate.Price);

            if (State != State.Realtime || Position.MarketPosition == MarketPosition.Flat)
                return;

            if (CheckTerminalExitOverfill("tick-watchdog"))
                return;

            CancelWrongSideProtectiveOrders("tick-watchdog");

            if (AuditPositionProtection("tick-watchdog"))
                return;
        }

        private void TrackRealtimeInfoPreview(double price)
        {
            if (State != State.Realtime || CurrentBar < 0)
                return;

            DateTime barTime = Time[0];
            if (barTime != realtimeInfoPreviewBarTime || double.IsNaN(realtimeInfoPreviewHigh) || double.IsNaN(realtimeInfoPreviewLow))
            {
                realtimeInfoPreviewBarTime = barTime;
                realtimeInfoPreviewHigh = Math.Max(High[0], price);
                realtimeInfoPreviewLow = Math.Min(Low[0], price);
                realtimeInfoPreviewClose = price;
                return;
            }

            realtimeInfoPreviewHigh = Math.Max(realtimeInfoPreviewHigh, price);
            realtimeInfoPreviewLow = Math.Min(realtimeInfoPreviewLow, price);
            realtimeInfoPreviewClose = price;
        }

        private void RefreshInfoFromRealtimeTick()
        {
            if (State != State.Realtime || ChartControl == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if ((nowUtc - lastRealtimeInfoRefreshUtc).TotalSeconds < RealtimeInfoRefreshSeconds)
                return;

            lastRealtimeInfoRefreshUtc = nowUtc;
            useRealtimeInfoPreview = true;
            try
            {
                UpdateInfo();
            }
            finally
            {
                useRealtimeInfoPreview = false;
            }
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
                    longEntryOrder = null;
                }
                else if (order == shortEntryOrder)
                {
                    shortEntryOrder = null;
                }

                if (orderState != OrderState.Filled && IsSecondaryEntrySignalName(order.Name))
                    MarkSecondaryEntryExited(order.Name);
            }

            TrackProtectiveAndExitOrders(order, orderState);
            SyncTradeLinesToProtectiveOrder(order, limitPrice, stopPrice);
            SyncTradeLinesToFilledEntryOrder(order, orderState, averageFillPrice);
            CancelWrongSideProtectiveOrders("order-update");

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
                (orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
            {
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    if (tradeLinesActive)
                        FinalizeTradeLines();
                    EndTradeAttempt("entry-" + orderState);
                }
                else if (IsSecondaryEntrySignalName(order.Name))
                {
                    EndTradeAttempt("entry-" + orderState);
                }
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
                    TrySubmitTerminalExit("ProtectiveReject");
                else if (Position.MarketPosition == MarketPosition.Short)
                    TrySubmitTerminalExit("ProtectiveReject");
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
            MarketPosition stopOutOriginalSide = GetStopOutOriginalSide(execution);
            bool stopOutFromSecondaryEntry = IsSecondaryEntrySignalName(execution.Order.FromEntrySignal ?? string.Empty);
            bool shouldAttemptStopOutFlip = activeEnableStopOutFlip
                && !(SecondaryTrailStop && stopOutFromSecondaryEntry)
                && IsStopLossOrderName(orderName)
                && Position.MarketPosition == MarketPosition.Flat
                && stopOutOriginalSide != MarketPosition.Flat;

            if (IsEntryOrderName(orderName))
            {
                bool entryIsSecondary = IsSecondaryEntrySignalName(orderName);
                bool secondaryAddOnEntry = entryIsSecondary && Position.Quantity > quantity && !string.IsNullOrEmpty(currentPositionEntrySignal);

                if (!secondaryAddOnEntry)
                    currentPositionEntrySignal = orderName;

                double filledStopPrice = marketPosition == MarketPosition.Long
                    ? pendingLongStopForWebhook
                    : marketPosition == MarketPosition.Short
                        ? pendingShortStopForWebhook
                        : 0.0;
                filledStopPrice = Instrument.MasterInstrument.RoundToTickSize(filledStopPrice);
                if (filledStopPrice <= 0.0 && tradeLineSlPrice > 0.0)
                    filledStopPrice = Instrument.MasterInstrument.RoundToTickSize(tradeLineSlPrice);
                filledStopPrice = BuildFilledStopPrice(marketPosition, fillPrice, filledStopPrice);
                if (entryIsSecondary)
                    MarkSecondaryEntryActive(orderName, filledStopPrice);

                if (secondaryAddOnEntry)
                {
                    ReanchorTradeLinesToEntryFill(marketPosition, fillPrice, filledStopPrice);
                    LogDebug(string.Format(
                        "Secondary add-on filled | signal={0} qty={1} positionQty={2} stop={3:0.00}",
                        orderName,
                        quantity,
                        Position.Quantity,
                        filledStopPrice));
                }
                else
                {
                    activeStopLossOrder = null;
                    activeProfitTargetOrder = null;
                    activeExitOrder = null;
                    initialStopPrice = filledStopPrice;
                    currentStopPrice = initialStopPrice;
                    ReanchorTradeLinesToEntryFill(marketPosition, fillPrice, initialStopPrice);
                    currentPositionEntryBar = CurrentBar;
                }

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

                ArmProtectionAuditGracePeriod("entry-fill", 60000);
                CancelWrongSideProtectiveOrders("entry-fill");
            }
            else if (terminalExitExecution)
            {
                bool protectiveExecution = IsProtectiveOrderName(orderName);
                string fromEntrySignal = execution.Order.FromEntrySignal ?? string.Empty;
                if (!string.IsNullOrEmpty(fromEntrySignal))
                {
                    MarkSecondaryEntryExited(fromEntrySignal);
                    if (marketPosition != MarketPosition.Flat
                        && string.Equals(currentPositionEntrySignal, fromEntrySignal, StringComparison.Ordinal))
                    {
                        if (marketPosition == MarketPosition.Long && secondaryLongPositionActive)
                            currentPositionEntrySignal = LongSecondaryEntrySignal;
                        else if (marketPosition == MarketPosition.Short && secondaryShortPositionActive)
                            currentPositionEntrySignal = ShortSecondaryEntrySignal;
                    }
                }

                if (Position.MarketPosition == MarketPosition.Flat && !IsOrderActive(activeExitOrder))
                {
                    ReleasePositionTrackingAfterTerminalExit(time, orderName);
                }
                else
                {
                    if (protectiveExecution || IsSecondaryTrailOnlyPosition())
                        ClearTerminalExitLock();
                    else if (!CheckTerminalExitOverfill("execution-" + orderName))
                        ArmProtectionAuditGracePeriod("terminal-exit-execution", 2000);
                }
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

            bool shouldSendExitWebhook = ShouldSendExitWebhook(execution, orderName, marketPosition);
            if (shouldSendExitWebhook)
                SendWebhook("exit", 0, 0, 0, true, quantity);

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

            if (shouldAttemptStopOutFlip)
                TrySubmitStopOutExecutionFlip(stopOutOriginalSide, fillPrice);

            CancelWrongSideProtectiveOrders("execution-" + orderName);
            UpdateInfo();
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (!UseCustomTradeLineRendering() || RenderTarget == null || chartControl == null || chartScale == null || ChartBars == null)
                return;

            TradeLineSnapshot[] historicalSnapshots;
            lock (tradeLineSnapshotsSync)
                historicalSnapshots = historicalTradeLines.ToArray();

            bool hasActiveSegment = ShouldRenderActiveTradeLinesCustom();
            bool hasHistoricalSegments = historicalSnapshots.Length > 0;
            if (!hasActiveSegment && !hasHistoricalSegments)
                return;

            var oldAntialiasMode = RenderTarget.AntialiasMode;
            RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.Aliased;

            using (var entryBrush = Brushes.Gold.ToDxBrush(RenderTarget))
            using (var stopBrush = Brushes.Red.ToDxBrush(RenderTarget))
            using (var targetBrush = Brushes.LimeGreen.ToDxBrush(RenderTarget))
            {
                foreach (TradeLineSnapshot snapshot in historicalSnapshots)
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

            return true;
        }
        private void ResetPositionTrackingState()
        {
            currentPositionEntrySignal = string.Empty;
            secondaryLongPositionActive = false;
            secondaryShortPositionActive = false;
            secondaryLongTrailStopPrice = 0.0;
            secondaryShortTrailStopPrice = 0.0;
            secondaryLongTrailDistanceFromEmaPoints = 0.0;
            secondaryShortTrailDistanceFromEmaPoints = 0.0;
            initialStopPrice = 0.0;
            currentStopPrice = 0.0;
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

            return IsTerminalExitExecution(orderName);
        }

        private bool AllowsPrimaryEntries()
        {
            return activeEntrySystemMode == EntrySystemMode.Primary
                || activeEntrySystemMode == EntrySystemMode.Both;
        }

        private bool AllowsSecondaryEntries()
        {
            return activeEntrySystemMode == EntrySystemMode.Secondary
                || activeEntrySystemMode == EntrySystemMode.Both;
        }

        private bool IsSecondaryBiasEnabled()
        {
            return SecondaryBiasEmaPeriod > 0 && secondaryBiasEma != null;
        }

        private double GetSecondaryBiasEmaValue()
        {
            return IsSecondaryBiasEnabled() ? secondaryBiasEma[0] : 0.0;
        }

        private bool IsSecondaryBiasDirectionAllowed(bool isLong, double referencePrice)
        {
            if (!IsSecondaryBiasEnabled())
                return true;

            double biasValue = secondaryBiasEma[0];
            if (biasValue <= 0.0)
                return true;

            return isLong
                ? referencePrice >= biasValue
                : referencePrice <= biasValue;
        }

        private string GetDirectionalBlockReason(bool isLong, bool biasPass, double referencePrice, double biasValue)
        {
            if (!biasPass && IsSecondaryBiasEnabled())
            {
                return string.Format(
                    "SecondaryBias side={0} price={1:0.00} ema={2:0.00}",
                    isLong ? "Long" : "Short",
                    referencePrice,
                    biasValue);
            }

            return "UnknownDirectionGate";
        }

        private bool IsSecondaryEntrySignalName(string orderName)
        {
            return string.Equals(orderName, LongSecondaryEntrySignal, StringComparison.Ordinal)
                || string.Equals(orderName, ShortSecondaryEntrySignal, StringComparison.Ordinal);
        }

        private bool HasSecondaryEntryExposure()
        {
            return secondaryLongPositionActive || secondaryShortPositionActive;
        }

        private void ResetSecondaryEntryState()
        {
            secondaryLongDoorOpen = false;
            secondaryShortDoorOpen = false;
            secondaryLongDoorSession = SessionSlot.None;
            secondaryShortDoorSession = SessionSlot.None;
            secondaryLongDoorSignalBar = -1;
            secondaryShortDoorSignalBar = -1;
            secondaryLongDoorTakeProfitVariancePoints = 0.0;
            secondaryShortDoorTakeProfitVariancePoints = 0.0;
            secondaryLongPositionActive = false;
            secondaryShortPositionActive = false;
            secondaryLongTrailStopPrice = 0.0;
            secondaryShortTrailStopPrice = 0.0;
            secondaryLongTrailDistanceFromEmaPoints = 0.0;
            secondaryShortTrailDistanceFromEmaPoints = 0.0;
        }

        private void CancelSecondaryEntryDoors(string reason)
        {
            if (DebugLogging && (secondaryLongDoorOpen || secondaryShortDoorOpen))
            {
                LogDebug(string.Format(
                    "Secondary door cancelled | reason={0} longDoor={1} shortDoor={2}",
                    reason,
                    secondaryLongDoorOpen,
                    secondaryShortDoorOpen));
            }

            secondaryLongDoorOpen = false;
            secondaryShortDoorOpen = false;
            secondaryLongDoorSession = SessionSlot.None;
            secondaryShortDoorSession = SessionSlot.None;
            secondaryLongDoorSignalBar = -1;
            secondaryShortDoorSignalBar = -1;
            secondaryLongDoorTakeProfitVariancePoints = 0.0;
            secondaryShortDoorTakeProfitVariancePoints = 0.0;
            CancelSecondaryEntryOrderIfActive(true, reason);
            CancelSecondaryEntryOrderIfActive(false, reason);
        }

        private void ArmSecondaryEntryDoor(bool isLong)
        {
            if (!AllowsSecondaryEntries() || activeSession == SessionSlot.None || GetSecondaryEntryQuantity() <= 0)
                return;

            if (isLong)
            {
                bool newDoor = !secondaryLongDoorOpen || secondaryLongDoorSession != activeSession;
                secondaryLongDoorOpen = true;
                secondaryLongDoorSession = activeSession;
                secondaryLongDoorSignalBar = CurrentBar;
                if (newDoor)
                    secondaryLongDoorTakeProfitVariancePoints = GetSecondaryTakeProfitVariancePoints();
                secondaryShortDoorOpen = false;
                secondaryShortDoorSession = SessionSlot.None;
                secondaryShortDoorSignalBar = -1;
                secondaryShortDoorTakeProfitVariancePoints = 0.0;
                CancelSecondaryEntryOrderIfActive(false, "opposite-secondary-door");
            }
            else
            {
                bool newDoor = !secondaryShortDoorOpen || secondaryShortDoorSession != activeSession;
                secondaryShortDoorOpen = true;
                secondaryShortDoorSession = activeSession;
                secondaryShortDoorSignalBar = CurrentBar;
                if (newDoor)
                    secondaryShortDoorTakeProfitVariancePoints = GetSecondaryTakeProfitVariancePoints();
                secondaryLongDoorOpen = false;
                secondaryLongDoorSession = SessionSlot.None;
                secondaryLongDoorSignalBar = -1;
                secondaryLongDoorTakeProfitVariancePoints = 0.0;
                CancelSecondaryEntryOrderIfActive(true, "opposite-secondary-door");
            }

            LogDebug(string.Format(
                "Secondary door armed | side={0} session={1} bar={2} tpVariance={3:0.00}",
                isLong ? "Long" : "Short",
                FormatSessionLabel(activeSession),
                CurrentBar,
                isLong ? secondaryLongDoorTakeProfitVariancePoints : secondaryShortDoorTakeProfitVariancePoints));
        }

        private bool TryProcessSecondaryEntryDoor(
            bool isLong,
            bool canTradeNow,
            bool directionPass,
            bool signalStillValid)
        {
            bool doorOpen = isLong ? secondaryLongDoorOpen : secondaryShortDoorOpen;
            if (!doorOpen)
                return false;

            SessionSlot doorSession = isLong ? secondaryLongDoorSession : secondaryShortDoorSession;
            if (!IsSecondaryDoorStillValid(isLong, doorSession, canTradeNow, directionPass, signalStillValid))
            {
                CancelSecondaryEntryDoors("door-invalid");
                return false;
            }

            if (!CanSubmitSecondaryEntry(isLong))
                return false;

            SubmitOrUpdateSecondaryEntry(isLong);
            return true;
        }

        private bool IsSecondaryDoorStillValid(
            bool isLong,
            SessionSlot doorSession,
            bool canTradeNow,
            bool directionPass,
            bool signalStillValid)
        {
            if (!AllowsSecondaryEntries())
                return false;

            if (GetSecondaryEntryQuantity() <= 0)
                return false;

            if (!canTradeNow || !directionPass || !signalStillValid)
                return false;

            if (activeSession == SessionSlot.None || doorSession == SessionSlot.None || activeSession != doorSession)
                return false;

            if (isLong && Position.MarketPosition == MarketPosition.Short)
                return false;

            if (!isLong && Position.MarketPosition == MarketPosition.Long)
                return false;

            return true;
        }

        private bool HasActiveSecondaryEntryOrder(bool isLong)
        {
            Order order = isLong ? longEntryOrder : shortEntryOrder;
            return IsOrderActive(order) && IsSecondaryEntrySignalName(order.Name);
        }

        private bool CanSubmitSecondaryEntry(bool isLong)
        {
            int secondaryQuantity = GetSecondaryEntryQuantity();
            if (secondaryQuantity <= 0 || activeEma == null || IsTerminalExitInFlight() || IsOrderActive(activeExitOrder))
                return false;

            if (activeSecondaryEntryTakeProfitDistanceFromEmaPoints <= 0.0)
                return false;

            if (activeEntrySystemMode == EntrySystemMode.Secondary && Position.MarketPosition != MarketPosition.Flat)
                return false;

            if (activeEntrySystemMode == EntrySystemMode.Both
                && Position.MarketPosition == (isLong ? MarketPosition.Long : MarketPosition.Short)
                && Position.Quantity >= GetEntryQuantity() + secondaryQuantity)
                return false;

            if (isLong)
            {
                if (secondaryLongPositionActive || Position.MarketPosition == MarketPosition.Short)
                    return false;

                return !IsOrderActive(longEntryOrder) || HasActiveSecondaryEntryOrder(true);
            }

            if (secondaryShortPositionActive || Position.MarketPosition == MarketPosition.Long)
                return false;

            return !IsOrderActive(shortEntryOrder) || HasActiveSecondaryEntryOrder(false);
        }

        private void SubmitOrUpdateSecondaryEntry(bool isLong)
        {
            double emaValue = activeEma[0];
            double entryPrice = BuildSecondaryEntryLimitPrice(isLong, emaValue);
            double stopPrice = BuildSecondaryEntryStopPrice(isLong, emaValue, entryPrice);
            double takeProfitPrice = BuildSecondaryEntryTakeProfitPrice(isLong, emaValue, entryPrice);
            int qty = GetSecondaryEntryQuantity();
            string signalName = isLong ? LongSecondaryEntrySignal : ShortSecondaryEntrySignal;
            bool updating = HasActiveSecondaryEntryOrder(isLong);

            if (!SecondaryEntryPricesAreValid(isLong, entryPrice, stopPrice, takeProfitPrice))
            {
                LogDebug(string.Format(
                    "Secondary skipped | reason=InvalidPrices side={0} ema={1:0.00} entry={2:0.00} stop={3:0.00} target={4:0.00}",
                    isLong ? "Long" : "Short",
                    emaValue,
                    entryPrice,
                    stopPrice,
                    takeProfitPrice));
                return;
            }

            if (!updating && RequireEntryConfirmation && !ShowEntryConfirmation(isLong ? "Long Secondary" : "Short Secondary", entryPrice, qty))
            {
                LogDebug(string.Format("Entry confirmation declined | {0} secondary.", isLong ? "LONG" : "SHORT"));
                return;
            }

            if (!updating)
                BeginTradeAttempt(isLong ? "LongSecondary" : "ShortSecondary");

            if (isLong)
            {
                pendingLongStopForWebhook = stopPrice;
                SetStopLoss(signalName, CalculationMode.Price, stopPrice, false);
                SetProfitTarget(signalName, CalculationMode.Price, takeProfitPrice);
                if (!updating)
                {
                    SendWebhook("buy", entryPrice, takeProfitPrice, stopPrice, false, qty);
                    StartTradeLines(entryPrice, stopPrice, takeProfitPrice, true);
                    SubmitLongEntryOrder(qty, signalName, entryPrice, false);
                }
                else
                {
                    ChangeOrder(longEntryOrder, qty, entryPrice, 0.0);
                }
            }
            else
            {
                pendingShortStopForWebhook = stopPrice;
                SetStopLoss(signalName, CalculationMode.Price, stopPrice, false);
                SetProfitTarget(signalName, CalculationMode.Price, takeProfitPrice);
                if (!updating)
                {
                    SendWebhook("sell", entryPrice, takeProfitPrice, stopPrice, false, qty);
                    StartTradeLines(entryPrice, stopPrice, takeProfitPrice, true);
                    SubmitShortEntryOrder(qty, signalName, entryPrice, false);
                }
                else
                {
                    ChangeOrder(shortEntryOrder, qty, entryPrice, 0.0);
                }
            }

            LogDebug(string.Format(
                "{0} {1} secondary limit | session={2} ema={3:0.00} entry={4:0.00} stop={5:0.00} target={6:0.00} qty={7} tpVariance={8:0.00}",
                updating ? "Move" : "Place",
                isLong ? "LONG" : "SHORT",
                FormatSessionLabel(activeSession),
                emaValue,
                entryPrice,
                stopPrice,
                takeProfitPrice,
                qty,
                isLong ? secondaryLongDoorTakeProfitVariancePoints : secondaryShortDoorTakeProfitVariancePoints));
        }

        private double BuildSecondaryEntryLimitPrice(bool isLong, double emaValue)
        {
            double distance = Math.Max(0.0, activeSecondaryEntryLimitDistanceFromEmaPoints);
            double raw = isLong
                ? emaValue + distance
                : emaValue - distance;

            return Instrument.MasterInstrument.RoundToTickSize(raw);
        }

        private double BuildSecondaryEntryStopPrice(bool isLong, double emaValue, double entryPrice)
        {
            double distance = Math.Max(0.0, activeSecondaryEntryStopDistanceFromEmaPoints);
            double raw = isLong
                ? emaValue - distance
                : emaValue + distance;

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (isLong && rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);
            else if (!isLong && rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);

            return rounded;
        }

        private double BuildSecondaryEntryTakeProfitPrice(bool isLong, double emaValue, double entryPrice)
        {
            double baseDistance = Math.Max(0.0, activeSecondaryEntryTakeProfitDistanceFromEmaPoints);
            double variance = Math.Max(0.0, isLong ? secondaryLongDoorTakeProfitVariancePoints : secondaryShortDoorTakeProfitVariancePoints);
            double raw = isLong
                ? emaValue + baseDistance + variance
                : emaValue - baseDistance - variance;

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (isLong && rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            else if (!isLong && rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);

            return rounded;
        }

        private bool SecondaryEntryPricesAreValid(bool isLong, double entryPrice, double stopPrice, double takeProfitPrice)
        {
            if (entryPrice <= 0.0 || stopPrice <= 0.0 || takeProfitPrice <= 0.0)
                return false;

            if (isLong)
                return stopPrice < entryPrice && takeProfitPrice > entryPrice;

            return stopPrice > entryPrice && takeProfitPrice < entryPrice;
        }

        private double GetSecondaryTakeProfitVariancePoints()
        {
            double maxVariance = Math.Max(0.0, activeSecondaryTakeProfitVariancePoints);
            if (maxVariance <= 0.0)
                return 0.0;

            if (secondaryTakeProfitVarianceRandom == null)
                secondaryTakeProfitVarianceRandom = new Random(unchecked(Environment.TickCount ^ GetHashCode() ^ 0x5A17));

            return Instrument.MasterInstrument.RoundToTickSize(secondaryTakeProfitVarianceRandom.NextDouble() * maxVariance);
        }

        private void CancelSecondaryEntryOrderIfActive(bool isLong, string reason)
        {
            Order order = isLong ? longEntryOrder : shortEntryOrder;
            if (IsOrderActive(order) && IsSecondaryEntrySignalName(order.Name))
                CancelOrderIfActive(order, reason);
        }

        private void MarkSecondaryEntryActive(string orderName, double stopPrice)
        {
            if (string.Equals(orderName, LongSecondaryEntrySignal, StringComparison.Ordinal))
            {
                secondaryLongPositionActive = true;
                secondaryLongTrailStopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
                secondaryLongTrailDistanceFromEmaPoints = Math.Max(0.0, activeSecondaryEntryStopDistanceFromEmaPoints);
            }
            else if (string.Equals(orderName, ShortSecondaryEntrySignal, StringComparison.Ordinal))
            {
                secondaryShortPositionActive = true;
                secondaryShortTrailStopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
                secondaryShortTrailDistanceFromEmaPoints = Math.Max(0.0, activeSecondaryEntryStopDistanceFromEmaPoints);
            }
        }

        private void MarkSecondaryEntryExited(string fromEntrySignal)
        {
            if (string.Equals(fromEntrySignal, LongSecondaryEntrySignal, StringComparison.Ordinal))
            {
                secondaryLongPositionActive = false;
                secondaryLongTrailStopPrice = 0.0;
                secondaryLongTrailDistanceFromEmaPoints = 0.0;
                if (secondaryLongDoorOpen)
                    secondaryLongDoorTakeProfitVariancePoints = GetSecondaryTakeProfitVariancePoints();
            }
            else if (string.Equals(fromEntrySignal, ShortSecondaryEntrySignal, StringComparison.Ordinal))
            {
                secondaryShortPositionActive = false;
                secondaryShortTrailStopPrice = 0.0;
                secondaryShortTrailDistanceFromEmaPoints = 0.0;
                if (secondaryShortDoorOpen)
                    secondaryShortDoorTakeProfitVariancePoints = GetSecondaryTakeProfitVariancePoints();
            }
        }

        private bool IsSecondaryTrailOnlyPosition()
        {
            return SecondaryTrailStop
                && HasSecondaryEntryExposure()
                && IsSecondaryEntrySignalName(currentPositionEntrySignal);
        }

        private bool ShouldPreserveSecondaryTrailForTerminalExit(string reason)
        {
            return SecondaryTrailStop
                && HasSecondaryEntryExposure()
                && !IsMandatoryTerminalExitReason(reason);
        }

        private bool IsMandatoryTerminalExitReason(string reason)
        {
            return string.Equals(reason, "InvalidConfiguration", StringComparison.Ordinal)
                || string.Equals(reason, "ForceClose", StringComparison.Ordinal)
                || string.Equals(reason, "TemporaryDateBlock", StringComparison.Ordinal)
                || string.Equals(reason, "NewsSkip", StringComparison.Ordinal)
                || string.Equals(reason, "London3FlatByTime", StringComparison.Ordinal)
                || string.Equals(reason, "SessionEnd", StringComparison.Ordinal)
                || string.Equals(reason, "MaxAccountBalance", StringComparison.Ordinal)
                || string.Equals(reason, "ProtectiveReject", StringComparison.Ordinal)
                || string.Equals(reason, "EmergencyOverfill", StringComparison.Ordinal);
        }

        private void TryTrailSecondaryStop(double emaValue)
        {
            if (!SecondaryTrailStop || Position.MarketPosition == MarketPosition.Flat || emaValue <= 0.0 || IsTerminalExitInFlight())
                return;

            if (Position.MarketPosition == MarketPosition.Long && secondaryLongPositionActive)
                TryTrailSecondaryStop(true, emaValue);
            else if (Position.MarketPosition == MarketPosition.Short && secondaryShortPositionActive)
                TryTrailSecondaryStop(false, emaValue);
        }

        private void TryTrailSecondaryStop(bool isLong, double emaValue)
        {
            double trailDistance = isLong ? secondaryLongTrailDistanceFromEmaPoints : secondaryShortTrailDistanceFromEmaPoints;
            if (trailDistance <= 0.0)
                trailDistance = Math.Max(0.0, activeSecondaryEntryStopDistanceFromEmaPoints);
            if (trailDistance <= 0.0)
                return;

            double closePrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            double proposedStop = Instrument.MasterInstrument.RoundToTickSize(isLong
                ? emaValue - trailDistance
                : emaValue + trailDistance);

            if (isLong && proposedStop >= closePrice)
                proposedStop = Instrument.MasterInstrument.RoundToTickSize(closePrice - TickSize);
            else if (!isLong && proposedStop <= closePrice)
                proposedStop = Instrument.MasterInstrument.RoundToTickSize(closePrice + TickSize);

            if (proposedStop <= 0.0)
                return;

            double currentTrailStop = isLong ? secondaryLongTrailStopPrice : secondaryShortTrailStopPrice;
            if (currentTrailStop <= 0.0)
                currentTrailStop = proposedStop;

            bool tightens = isLong
                ? proposedStop > currentTrailStop + TickSize * 0.5
                : proposedStop < currentTrailStop - TickSize * 0.5;
            if (!tightens)
                return;

            string signalName = isLong ? LongSecondaryEntrySignal : ShortSecondaryEntrySignal;
            SetStopLoss(signalName, CalculationMode.Price, proposedStop, false);

            if (isLong)
                secondaryLongTrailStopPrice = proposedStop;
            else
                secondaryShortTrailStopPrice = proposedStop;

            LogDebug(string.Format(
                "Secondary trail SL | side={0} ema={1:0.00} distance={2:0.00} stop={3:0.00}",
                isLong ? "Long" : "Short",
                emaValue,
                trailDistance,
                proposedStop));
        }

        private bool IsLongEntryOrderName(string orderName)
        {
            return string.Equals(orderName, LongEntrySignal, StringComparison.Ordinal)
                || string.Equals(orderName, LongFlipEntrySignal, StringComparison.Ordinal)
                || string.Equals(orderName, LongSecondaryEntrySignal, StringComparison.Ordinal);
        }

        private bool IsShortEntryOrderName(string orderName)
        {
            return string.Equals(orderName, ShortEntrySignal, StringComparison.Ordinal)
                || string.Equals(orderName, ShortFlipEntrySignal, StringComparison.Ordinal)
                || string.Equals(orderName, ShortSecondaryEntrySignal, StringComparison.Ordinal);
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
            return "DUOrc" + reason;
        }

        private bool TrySubmitTerminalExit(string reason)
        {
            return TrySubmitTerminalExit(reason, true);
        }

        private bool TrySubmitTerminalExit(string reason, bool useEntrySignal)
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearTerminalExitLock();
                return false;
            }

            if (CheckTerminalExitOverfill("pre-exit-" + reason))
                return false;

            ReconcileTrackedProtectiveOrders();

            if (IsTerminalExitInFlight())
            {
                LogDebug(string.Format(
                    "Terminal exit skipped | reason={0} pendingReason={1} pendingSide={2} activeExit={3}",
                    reason,
                    terminalExitPendingReason,
                    terminalExitPendingSide,
                    FormatOrderRef(activeExitOrder)));
                return false;
            }

            if (IsProtectiveExitLikelyPending())
            {
                ArmProtectionAuditGracePeriod("protective-exit-pending-" + reason, 5000);
                LogDebug(string.Format(
                    "Terminal exit skipped | reason={0} protective order already at/through price side={1} stop={2} target={3}",
                    reason,
                    Position.MarketPosition,
                    FormatOrderRef(activeStopLossOrder),
                    FormatOrderRef(activeProfitTargetOrder)));
                return false;
            }

            MarketPosition exitSide = Position.MarketPosition;
            string exitSignal = BuildExitSignalName(reason);
            bool preserveSecondaryTrail = ShouldPreserveSecondaryTrailForTerminalExit(reason);
            string scopedEntrySignal = exitSide == MarketPosition.Long ? GetOpenLongEntrySignal() : GetOpenShortEntrySignal();
            if (preserveSecondaryTrail && IsSecondaryEntrySignalName(scopedEntrySignal))
            {
                LogDebug(string.Format(
                    "Terminal exit skipped | reason={0} secondary trail position remains managed by SL/TP",
                    reason));
                return false;
            }

            bool exitAllEntries = HasSecondaryEntryExposure() && !preserveSecondaryTrail;
            if (preserveSecondaryTrail)
                useEntrySignal = true;
            MarkTerminalExitPending(reason, exitSide);
            ArmProtectionAuditGracePeriod("terminal-exit-" + reason, 10000);

            if (exitSide == MarketPosition.Long)
            {
                if (useEntrySignal && !exitAllEntries)
                    ExitLong(exitSignal, scopedEntrySignal);
                else
                    ExitLong(exitSignal);
                return true;
            }

            if (exitSide == MarketPosition.Short)
            {
                if (useEntrySignal && !exitAllEntries)
                    ExitShort(exitSignal, scopedEntrySignal);
                else
                    ExitShort(exitSignal);
                return true;
            }

            ClearTerminalExitLock();
            return false;
        }

        private bool IsTerminalExitInFlight()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearTerminalExitLock();
                return false;
            }

            return terminalExitPending || IsOrderActive(activeExitOrder);
        }

        private void MarkTerminalExitPending(string reason, MarketPosition side)
        {
            if (side == MarketPosition.Flat)
                return;

            terminalExitPending = true;
            terminalExitPendingReason = reason ?? string.Empty;
            terminalExitPendingSinceUtc = DateTime.UtcNow;
            terminalExitPendingBar = CurrentBar;
            terminalExitPendingSide = side;
        }

        private void ClearTerminalExitLock()
        {
            terminalExitPending = false;
            terminalExitPendingReason = string.Empty;
            terminalExitPendingSinceUtc = Core.Globals.MinDate;
            terminalExitPendingBar = -1;
            terminalExitPendingSide = MarketPosition.Flat;
            emergencyOverfillFlattenSubmitted = false;
        }

        private bool IsProtectiveExitLikelyPending()
        {
            double closePrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            double threshold = TickSize * 0.5;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (IsOrderActive(activeProfitTargetOrder))
                {
                    double targetPrice = GetWorkingOrderLimitPrice(activeProfitTargetOrder, 0.0);
                    if (targetPrice > 0.0 && closePrice >= targetPrice - threshold)
                        return true;
                }

                if (IsOrderActive(activeStopLossOrder))
                {
                    double stopPrice = GetWorkingOrderStopPrice(activeStopLossOrder, 0.0);
                    if (stopPrice > 0.0 && closePrice <= stopPrice + threshold)
                        return true;
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (IsOrderActive(activeProfitTargetOrder))
                {
                    double targetPrice = GetWorkingOrderLimitPrice(activeProfitTargetOrder, 0.0);
                    if (targetPrice > 0.0 && closePrice <= targetPrice + threshold)
                        return true;
                }

                if (IsOrderActive(activeStopLossOrder))
                {
                    double stopPrice = GetWorkingOrderStopPrice(activeStopLossOrder, 0.0);
                    if (stopPrice > 0.0 && closePrice >= stopPrice - threshold)
                        return true;
                }
            }

            return false;
        }

        private bool CheckTerminalExitOverfill(string reason)
        {
            if (!terminalExitPending)
                return false;

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearTerminalExitLock();
                return false;
            }

            bool overfilled = (terminalExitPendingSide == MarketPosition.Long && Position.MarketPosition == MarketPosition.Short)
                || (terminalExitPendingSide == MarketPosition.Short && Position.MarketPosition == MarketPosition.Long);
            if (!overfilled)
                return false;

            SubmitEmergencyOverfillFlatten(reason);
            return true;
        }

        private void SubmitEmergencyOverfillFlatten(string reason)
        {
            if (emergencyOverfillFlattenSubmitted)
                return;

            emergencyOverfillFlattenSubmitted = true;
            Print(string.Format(
                "{0} | {1} | bar={2} | CRITICAL TERMINAL EXIT OVERFILL | reason={3} originalSide={4} currentPosition={5} pendingReason={6} | Submitting emergency flatten.",
                Time[0],
                HeartbeatStrategyName,
                CurrentBar,
                reason,
                terminalExitPendingSide,
                Position.MarketPosition,
                terminalExitPendingReason));

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(BuildExitSignalName("EmergencyOverfill"));
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(BuildExitSignalName("EmergencyOverfill"));
        }

        private void TryTrailHardStop(double emaValue)
        {
            if (!activeTrailHardStop || Position.MarketPosition == MarketPosition.Flat || activeEma == null || IsTerminalExitInFlight())
                return;

            double closePrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            string entrySignal = Position.MarketPosition == MarketPosition.Long
                ? GetOpenLongEntrySignal()
                : GetOpenShortEntrySignal();
            if (currentStopPrice <= 0.0 && IsOrderActive(activeStopLossOrder))
            {
                double workingStopPrice = GetWorkingOrderStopPrice(activeStopLossOrder, 0.0);
                if (workingStopPrice > 0.0)
                    currentStopPrice = Instrument.MasterInstrument.RoundToTickSize(workingStopPrice);
            }

            if (currentStopPrice <= 0.0)
                return;

            double stopPrice;

            if (Position.MarketPosition == MarketPosition.Long)
                stopPrice = emaValue - activeStopPaddingPoints;
            else if (Position.MarketPosition == MarketPosition.Short)
                stopPrice = emaValue + activeStopPaddingPoints;
            else
                return;

            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            if (!IsManagedStopPriceValid(stopPrice, closePrice))
                return;

            if (ApplyManagedStop(entrySignal, stopPrice, "hard-sl-trail"))
            {
                LogDebug(string.Format(
                    "Hard SL trailed | side={0} signal={1} stop={2:0.00} ema={3:0.00} close={4:0.00} pad={5:0.00}",
                    Position.MarketPosition,
                    entrySignal,
                    stopPrice,
                    emaValue,
                    closePrice,
                    activeStopPaddingPoints));
            }
        }

        private bool ApplyManagedStop(string entrySignal, double stopPrice)
        {
            return ApplyManagedStop(entrySignal, stopPrice, "managed-stop");
        }

        private bool ApplyManagedStop(string entrySignal, double stopPrice, string reason)
        {
            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            if (!ShouldTightenManagedStop(stopPrice))
                return false;

            SetStopLoss(entrySignal, CalculationMode.Price, stopPrice, false);
            currentStopPrice = stopPrice;
            UpdateTradeLineStopPrice(stopPrice);
            SyncProjectXProtectionUpdate(ProjectXProtectionOrderKind.StopLoss, stopPrice, reason);
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

        private bool TryExitCandleReversal()
        {
            if (activeCandleReversalExitBars <= 0 || Position.MarketPosition == MarketPosition.Flat || currentPositionEntryBar < 0)
                return false;

            int barsHeld = CurrentBar - currentPositionEntryBar;
            if (barsHeld < activeCandleReversalExitBars)
                return false;

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (Close[0] <= Open[0])
                    return false;

                double referenceHigh;
                int referenceBarsAgo;
                if (!TryGetMostRecentBearishCandleHighSinceEntry(out referenceHigh, out referenceBarsAgo))
                    return false;

                double triggerPrice = referenceHigh + activeCandleReversalCloseBeyondPoints;
                if (Close[0] <= triggerPrice)
                    return false;

                TrySubmitTerminalExit("CandleReversalExit");
                LogDebug(string.Format(
                    "Exit SHORT | reason=CandleReversal barsHeld={0} threshold={1} close={2:0.00} bearishHigh={3:0.00} trigger={4:0.00} closeBeyond={5:0.##} minBody={6:0.##} refBarsAgo={7}",
                    barsHeld,
                    activeCandleReversalExitBars,
                    Close[0],
                    referenceHigh,
                    triggerPrice,
                    activeCandleReversalCloseBeyondPoints,
                    activeCandleReversalMinBodyPoints,
                    referenceBarsAgo));
                return true;
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (Close[0] >= Open[0])
                    return false;

                double referenceLow;
                int referenceBarsAgo;
                if (!TryGetMostRecentBullishCandleLowSinceEntry(out referenceLow, out referenceBarsAgo))
                    return false;

                double triggerPrice = referenceLow - activeCandleReversalCloseBeyondPoints;
                if (Close[0] >= triggerPrice)
                    return false;

                TrySubmitTerminalExit("CandleReversalExit");
                LogDebug(string.Format(
                    "Exit LONG | reason=CandleReversal barsHeld={0} threshold={1} close={2:0.00} bullishLow={3:0.00} trigger={4:0.00} closeBeyond={5:0.##} minBody={6:0.##} refBarsAgo={7}",
                    barsHeld,
                    activeCandleReversalExitBars,
                    Close[0],
                    referenceLow,
                    triggerPrice,
                    activeCandleReversalCloseBeyondPoints,
                    activeCandleReversalMinBodyPoints,
                    referenceBarsAgo));
                return true;
            }

            return false;
        }

        private bool TryGetMostRecentBearishCandleHighSinceEntry(out double high, out int barsAgo)
        {
            high = 0.0;
            barsAgo = -1;

            int maxBarsAgo = CurrentBar - Math.Max(0, currentPositionEntryBar);
            for (int i = 1; i <= maxBarsAgo; i++)
            {
                if (Close[i] < Open[i] && IsCandleBodyLargeEnough(i, activeCandleReversalMinBodyPoints))
                {
                    high = High[i];
                    barsAgo = i;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetMostRecentBullishCandleLowSinceEntry(out double low, out int barsAgo)
        {
            low = 0.0;
            barsAgo = -1;

            int maxBarsAgo = CurrentBar - Math.Max(0, currentPositionEntryBar);
            for (int i = 1; i <= maxBarsAgo; i++)
            {
                if (Close[i] > Open[i] && IsCandleBodyLargeEnough(i, activeCandleReversalMinBodyPoints))
                {
                    low = Low[i];
                    barsAgo = i;
                    return true;
                }
            }

            return false;
        }

        private bool IsCandleBodyLargeEnough(int barsAgo, double minimumBodyPoints)
        {
            return minimumBodyPoints <= 0.0 || Math.Abs(Close[barsAgo] - Open[barsAgo]) >= minimumBodyPoints;
        }

        private double GetCurrentAtrValue()
        {
            if (entryAtr == null)
                return 0.0;

            double atrValue = entryAtr[0];
            if (double.IsNaN(atrValue) || double.IsInfinity(atrValue) || atrValue <= 0.0)
                return 0.0;

            return atrValue;
        }

        private double GetConfiguredEntryTakeProfitPoints()
        {
            return activeTakeProfitPoints;
        }

        private double GetActivePositionTakeProfitPoints()
        {
            return activeTakeProfitPoints;
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
                || normalized.StartsWith("DUOrc", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return marketPosition == MarketPosition.Flat;
        }

        private void StartTradeLines(double entryPrice, double stopPrice, double takeProfitPrice, bool hasTakeProfit)
        {
            if (tradeLinesActive)
                FinalizeTradeLines();

            tradeLineTagPrefix = string.Format("DUOrc_TradeLine_{0}_{1}_", ++tradeLineTagCounter, CurrentBar);
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
            lock (tradeLineSnapshotsSync)
            {
                historicalTradeLines.Add(new TradeLineSnapshot
                {
                    StartBar = tradeLineSignalBar,
                    EndBar = tradeLineExitBar,
                    EntryPrice = tradeLineEntryPrice,
                    StopPrice = tradeLineSlPrice,
                    HasTakeProfit = tradeLineHasTp,
                    TakeProfitPrice = tradeLineTpPrice
                });
            }
            UpdateTradeLines();
            tradeLinesActive = false;
            ClearTradeLineState();
            RequestTradeLineRender();
        }

        private void ClearTradeLineState()
        {
            tradeLineHasTp = false;
            tradeLineSignalBar = -1;
            tradeLineExitBar = -1;
            tradeLineEntryPrice = 0.0;
            tradeLineTpPrice = 0.0;
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
            double plannedStopPrice = isLongEntry ? pendingLongStopForWebhook : pendingShortStopForWebhook;
            double actualStopPrice = BuildFilledStopPrice(
                isLongEntry ? MarketPosition.Long : MarketPosition.Short,
                actualFillPrice,
                plannedStopPrice);

            ReanchorTradeLinesToEntryFill(
                isLongEntry ? MarketPosition.Long : MarketPosition.Short,
                actualFillPrice,
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

        private void ReanchorTradeLinesToEntryFill(MarketPosition marketPosition, double fillPrice, double stopPrice)
        {
            if (!tradeLinesActive || marketPosition == MarketPosition.Flat)
                return;

            tradeLineEntryPrice = Instrument.MasterInstrument.RoundToTickSize(fillPrice);
            tradeLineSlPrice = stopPrice > 0.0
                ? Instrument.MasterInstrument.RoundToTickSize(stopPrice)
                : 0.0;

            double takeProfitPoints = GetConfiguredEntryTakeProfitPoints();
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
        }

        private void DrawPixelSnappedHorizontalLine(ChartScale chartScale, float startX, float endX, double price, SharpDX.Direct2D1.Brush brush, float width)
        {
            if (price <= 0.0 || endX <= startX)
                return;

            float y = GetSnappedTradeLineY(chartScale, price);
            RenderTarget.DrawLine(new SharpDX.Vector2(startX, y), new SharpDX.Vector2(endX, y), brush, width);
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
                case SessionSlot.NewYork4:
                case SessionSlot.NewYork5:
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
            DateTime gateTime = GetHistoricalTimeGateTime(time);
            TimeSpan now = gateTime.TimeOfDay;
            SessionSlot nextSlot = SessionSlot.None;
            DateTime nextStart = DateTime.MaxValue;
            bool hasConfiguredSession = false;

            foreach (SessionSlot slot in ConfigurableSessionSlots)
            {
                TimeSpan start;
                TimeSpan end;
                if (!IsSessionConfigured(slot) || !TryGetSessionWindow(slot, gateTime, out start, out end))
                    continue;

                hasConfiguredSession = true;
                if (IsTimeInRange(now, start, end))
                    return slot;

                DateTime candidateStart = gateTime.Date + start;
                if (candidateStart <= gateTime)
                    candidateStart = candidateStart.AddDays(1);

                if (candidateStart < nextStart)
                {
                    nextStart = candidateStart;
                    nextSlot = slot;
                }
            }

            return hasConfiguredSession ? nextSlot : SessionSlot.None;
        }

        private SessionSlot DetermineCurrentSessionForTime(DateTime time)
        {
            foreach (SessionSlot slot in ConfigurableSessionSlots)
            {
                if (IsSessionConfigured(slot) && TimeInSession(slot, time))
                    return slot;
            }

            return SessionSlot.None;
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
                    return AsiaContracts > 0 && AsiaSessionStart != AsiaSessionEnd;
                case SessionSlot.Asia2:
                    return Asia2Contracts > 0 && Asia2SessionStart != Asia2SessionEnd;
                case SessionSlot.Asia3:
                    return Asia3Contracts > 0 && Asia3SessionStart != Asia3SessionEnd;
                case SessionSlot.London:
                    return LondonContracts > 0 && LondonSessionStart != LondonSessionEnd;
                case SessionSlot.London2:
                    return London2Contracts > 0 && London2SessionStart != London2SessionEnd;
                case SessionSlot.London3:
                    return London3Contracts > 0 && London3SessionStart != London3SessionEnd;
                case SessionSlot.NewYork:
                    return NewYorkContracts > 0 && NewYorkSessionStart != NewYorkSessionEnd;
                case SessionSlot.NewYork2:
                    return NewYork2Contracts > 0 && NewYork2SessionStart != NewYork2SessionEnd;
                case SessionSlot.NewYork3:
                    return NewYork3Contracts > 0 && NewYork3SessionStart != NewYork3SessionEnd;
                case SessionSlot.NewYork4:
                    return NewYork4Contracts > 0 && NewYork4SessionStart != NewYork4SessionEnd;
                case SessionSlot.NewYork5:
                    return NewYork5Contracts > 0 && NewYork5SessionStart != NewYork5SessionEnd;
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
                    activeAdxMaxThreshold = AsiaAdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = AsiaAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = AsiaAdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = AsiaContracts;
                    activeEntryMinBodyPoints = AsiaEntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = AsiaEntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = AsiaColorBlindEntry;
                    activeEntryType = AsiaEntryType;
                    activeEntrySystemMode = AsiaEntrySystemMode;
                    activeEnableStopOutFlip = AsiaEnableStopOutFlip;
                    activeSecondaryContracts = AsiaSecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = AsiaSecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = AsiaSecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = AsiaSecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = AsiaSecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = AsiaStopPaddingPoints;
                    activeTrailHardStop = AsiaTrailHardStop;
                    activeTakeProfitPoints = AsiaTakeProfitPoints;
                    activeMinimumAtrForEntry = AsiaAtrMinimum;
                    activeCandleReversalExitBars = AsiaCandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = AsiaCandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = AsiaCandleReversalMinBodyPoints;
                    break;

                case SessionSlot.Asia2:
                    activeEma = emaAsia2;
                    activeAdx = adxAsia2;
                    activeEmaPeriod = Asia2EmaPeriod;
                    activeAdxPeriod = Asia2AdxPeriod;
                    activeAdxThreshold = Asia2AdxThreshold;
                    activeAdxMaxThreshold = Asia2AdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = Asia2AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = Asia2AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = Asia2Contracts;
                    activeEntryMinBodyPoints = Asia2EntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = Asia2EntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = Asia2ColorBlindEntry;
                    activeEntryType = Asia2EntryType;
                    activeEntrySystemMode = Asia2EntrySystemMode;
                    activeEnableStopOutFlip = Asia2EnableStopOutFlip;
                    activeSecondaryContracts = Asia2SecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = Asia2SecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = Asia2SecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = Asia2SecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = Asia2SecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = Asia2StopPaddingPoints;
                    activeTrailHardStop = Asia2TrailHardStop;
                    activeTakeProfitPoints = Asia2TakeProfitPoints;
                    activeMinimumAtrForEntry = Asia2AtrMinimum;
                    activeCandleReversalExitBars = Asia2CandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = Asia2CandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = Asia2CandleReversalMinBodyPoints;
                    break;

                case SessionSlot.Asia3:
                    activeEma = emaAsia3;
                    activeAdx = adxAsia3;
                    activeEmaPeriod = Asia3EmaPeriod;
                    activeAdxPeriod = Asia3AdxPeriod;
                    activeAdxThreshold = Asia3AdxThreshold;
                    activeAdxMaxThreshold = Asia3AdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = Asia3AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = Asia3AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = Asia3Contracts;
                    activeEntryMinBodyPoints = Asia3EntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = Asia3EntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = Asia3ColorBlindEntry;
                    activeEntryType = Asia3EntryType;
                    activeEntrySystemMode = Asia3EntrySystemMode;
                    activeEnableStopOutFlip = Asia3EnableStopOutFlip;
                    activeSecondaryContracts = Asia3SecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = Asia3SecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = Asia3SecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = Asia3SecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = Asia3SecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = Asia3StopPaddingPoints;
                    activeTrailHardStop = Asia3TrailHardStop;
                    activeTakeProfitPoints = Asia3TakeProfitPoints;
                    activeMinimumAtrForEntry = Asia3AtrMinimum;
                    activeCandleReversalExitBars = Asia3CandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = Asia3CandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = Asia3CandleReversalMinBodyPoints;
                    break;

                case SessionSlot.London:
                    activeEma = emaLondon;
                    activeAdx = adxLondon;
                    activeEmaPeriod = LondonEmaPeriod;
                    activeAdxPeriod = LondonAdxPeriod;
                    activeAdxThreshold = LondonAdxThreshold;
                    activeAdxMaxThreshold = LondonAdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = LondonAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = LondonAdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = LondonContracts;
                    activeEntryMinBodyPoints = LondonEntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = LondonEntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = LondonColorBlindEntry;
                    activeEntryType = LondonEntryType;
                    activeEntrySystemMode = LondonEntrySystemMode;
                    activeEnableStopOutFlip = LondonEnableStopOutFlip;
                    activeSecondaryContracts = LondonSecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = LondonSecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = LondonSecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = LondonSecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = LondonSecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = LondonStopPaddingPoints;
                    activeTrailHardStop = LondonTrailHardStop;
                    activeTakeProfitPoints = LondonTakeProfitPoints;
                    activeMinimumAtrForEntry = LondonAtrMinimum;
                    activeCandleReversalExitBars = LondonCandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = LondonCandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = LondonCandleReversalMinBodyPoints;
                    break;

                case SessionSlot.London2:
                    activeEma = emaLondon2;
                    activeAdx = adxLondon2;
                    activeEmaPeriod = London2EmaPeriod;
                    activeAdxPeriod = London2AdxPeriod;
                    activeAdxThreshold = London2AdxThreshold;
                    activeAdxMaxThreshold = London2AdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = London2AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = London2AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = London2Contracts;
                    activeEntryMinBodyPoints = London2EntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = London2EntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = London2ColorBlindEntry;
                    activeEntryType = London2EntryType;
                    activeEntrySystemMode = London2EntrySystemMode;
                    activeEnableStopOutFlip = London2EnableStopOutFlip;
                    activeSecondaryContracts = London2SecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = London2SecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = London2SecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = London2SecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = London2SecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = London2StopPaddingPoints;
                    activeTrailHardStop = London2TrailHardStop;
                    activeTakeProfitPoints = London2TakeProfitPoints;
                    activeMinimumAtrForEntry = London2AtrMinimum;
                    activeCandleReversalExitBars = London2CandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = London2CandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = London2CandleReversalMinBodyPoints;
                    break;

                case SessionSlot.London3:
                    activeEma = emaLondon3;
                    activeAdx = adxLondon3;
                    activeEmaPeriod = London3EmaPeriod;
                    activeAdxPeriod = London3AdxPeriod;
                    activeAdxThreshold = London3AdxThreshold;
                    activeAdxMaxThreshold = London3AdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = London3AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = London3AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = London3Contracts;
                    activeEntryMinBodyPoints = London3EntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = London3EntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = London3ColorBlindEntry;
                    activeEntryType = London3EntryType;
                    activeEntrySystemMode = London3EntrySystemMode;
                    activeEnableStopOutFlip = London3EnableStopOutFlip;
                    activeSecondaryContracts = London3SecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = London3SecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = London3SecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = London3SecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = London3SecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = London3StopPaddingPoints;
                    activeTrailHardStop = London3TrailHardStop;
                    activeTakeProfitPoints = London3TakeProfitPoints;
                    activeMinimumAtrForEntry = London3AtrMinimum;
                    activeCandleReversalExitBars = London3CandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = London3CandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = London3CandleReversalMinBodyPoints;
                    break;

                case SessionSlot.NewYork:
                    activeEma = emaNewYork;
                    activeAdx = adxNewYork;
                    activeEmaPeriod = NewYorkEmaPeriod;
                    activeAdxPeriod = NewYorkAdxPeriod;
                    activeAdxThreshold = NewYorkAdxThreshold;
                    activeAdxMaxThreshold = NewYorkAdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = NewYorkAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = NewYorkAdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = NewYorkContracts;
                    activeEntryMinBodyPoints = NewYorkEntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = NewYorkEntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = NewYorkColorBlindEntry;
                    activeEntryType = NewYorkEntryType;
                    activeEntrySystemMode = NewYorkEntrySystemMode;
                    activeEnableStopOutFlip = NewYorkEnableStopOutFlip;
                    activeSecondaryContracts = NewYorkSecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = NewYorkSecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = NewYorkSecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = NewYorkSecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = NewYorkSecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = NewYorkStopPaddingPoints;
                    activeTrailHardStop = NewYorkTrailHardStop;
                    activeTakeProfitPoints = NewYorkTakeProfitPoints;
                    activeMinimumAtrForEntry = NewYorkAtrMinimum;
                    activeCandleReversalExitBars = NewYorkCandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = NewYorkCandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = NewYorkCandleReversalMinBodyPoints;
                    break;

                case SessionSlot.NewYork2:
                    activeEma = emaNewYork2;
                    activeAdx = adxNewYork2;
                    activeEmaPeriod = NewYork2EmaPeriod;
                    activeAdxPeriod = NewYork2AdxPeriod;
                    activeAdxThreshold = NewYork2AdxThreshold;
                    activeAdxMaxThreshold = NewYork2AdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = NewYork2AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = NewYork2AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = NewYork2Contracts;
                    activeEntryMinBodyPoints = NewYork2EntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = NewYork2EntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = NewYork2ColorBlindEntry;
                    activeEntryType = NewYork2EntryType;
                    activeEntrySystemMode = NewYork2EntrySystemMode;
                    activeEnableStopOutFlip = NewYork2EnableStopOutFlip;
                    activeSecondaryContracts = NewYork2SecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = NewYork2SecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = NewYork2SecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = NewYork2SecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = NewYork2SecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = NewYork2StopPaddingPoints;
                    activeTrailHardStop = NewYork2TrailHardStop;
                    activeTakeProfitPoints = NewYork2TakeProfitPoints;
                    activeMinimumAtrForEntry = NewYork2AtrMinimum;
                    activeCandleReversalExitBars = NewYork2CandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = NewYork2CandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = NewYork2CandleReversalMinBodyPoints;
                    break;

                case SessionSlot.NewYork3:
                    activeEma = emaNewYork3;
                    activeAdx = adxNewYork3;
                    activeEmaPeriod = NewYork3EmaPeriod;
                    activeAdxPeriod = NewYork3AdxPeriod;
                    activeAdxThreshold = NewYork3AdxThreshold;
                    activeAdxMaxThreshold = NewYork3AdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = NewYork3AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = NewYork3AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = NewYork3Contracts;
                    activeEntryMinBodyPoints = NewYork3EntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = NewYork3EntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = NewYork3ColorBlindEntry;
                    activeEntryType = NewYork3EntryType;
                    activeEntrySystemMode = NewYork3EntrySystemMode;
                    activeEnableStopOutFlip = NewYork3EnableStopOutFlip;
                    activeSecondaryContracts = NewYork3SecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = NewYork3SecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = NewYork3SecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = NewYork3SecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = NewYork3SecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = NewYork3StopPaddingPoints;
                    activeTrailHardStop = NewYork3TrailHardStop;
                    activeTakeProfitPoints = NewYork3TakeProfitPoints;
                    activeMinimumAtrForEntry = NewYork3AtrMinimum;
                    activeCandleReversalExitBars = NewYork3CandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = NewYork3CandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = NewYork3CandleReversalMinBodyPoints;
                    break;

                case SessionSlot.NewYork4:
                    activeEma = emaNewYork4;
                    activeAdx = adxNewYork4;
                    activeEmaPeriod = NewYork4EmaPeriod;
                    activeAdxPeriod = NewYork4AdxPeriod;
                    activeAdxThreshold = NewYork4AdxThreshold;
                    activeAdxMaxThreshold = NewYork4AdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = NewYork4AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = NewYork4AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = NewYork4Contracts;
                    activeEntryMinBodyPoints = NewYork4EntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = NewYork4EntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = NewYork4ColorBlindEntry;
                    activeEntryType = NewYork4EntryType;
                    activeEntrySystemMode = NewYork4EntrySystemMode;
                    activeEnableStopOutFlip = NewYork4EnableStopOutFlip;
                    activeSecondaryContracts = NewYork4SecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = NewYork4SecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = NewYork4SecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = NewYork4SecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = NewYork4SecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = NewYork4StopPaddingPoints;
                    activeTrailHardStop = NewYork4TrailHardStop;
                    activeTakeProfitPoints = NewYork4TakeProfitPoints;
                    activeMinimumAtrForEntry = NewYork4AtrMinimum;
                    activeCandleReversalExitBars = NewYork4CandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = NewYork4CandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = NewYork4CandleReversalMinBodyPoints;
                    break;

                case SessionSlot.NewYork5:
                    activeEma = emaNewYork5;
                    activeAdx = adxNewYork5;
                    activeEmaPeriod = NewYork5EmaPeriod;
                    activeAdxPeriod = NewYork5AdxPeriod;
                    activeAdxThreshold = NewYork5AdxThreshold;
                    activeAdxMaxThreshold = NewYork5AdxMaxThreshold;
                    activeAdxPeakDrawdownExitUnits = NewYork5AdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = NewYork5AdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = NewYork5Contracts;
                    activeEntryMinBodyPoints = NewYork5EntryMinBodyPoints;
                    activeEntryCloseBeyondEmaPoints = NewYork5EntryCloseBeyondEmaPoints;
                    activeColorBlindEntry = NewYork5ColorBlindEntry;
                    activeEntryType = NewYork5EntryType;
                    activeEntrySystemMode = NewYork5EntrySystemMode;
                    activeEnableStopOutFlip = NewYork5EnableStopOutFlip;
                    activeSecondaryContracts = NewYork5SecondaryContracts;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = NewYork5SecondaryEntryLimitDistanceFromEmaPoints;
                    activeSecondaryEntryStopDistanceFromEmaPoints = NewYork5SecondaryEntryStopDistanceFromEmaPoints;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = NewYork5SecondaryEntryTakeProfitDistanceFromEmaPoints;
                    activeSecondaryTakeProfitVariancePoints = NewYork5SecondaryTakeProfitVariancePoints;
                    activeStopPaddingPoints = NewYork5StopPaddingPoints;
                    activeTrailHardStop = NewYork5TrailHardStop;
                    activeTakeProfitPoints = NewYork5TakeProfitPoints;
                    activeMinimumAtrForEntry = NewYork5AtrMinimum;
                    activeCandleReversalExitBars = NewYork5CandleReversalExitBars;
                    activeCandleReversalCloseBeyondPoints = NewYork5CandleReversalCloseBeyondPoints;
                    activeCandleReversalMinBodyPoints = NewYork5CandleReversalMinBodyPoints;
                    break;

                default:
                    activeEma = null;
                    activeAdx = null;
                    activeEmaPeriod = 0;
                    activeAdxPeriod = 0;
                    activeAdxThreshold = 0.0;
                    activeAdxMaxThreshold = 0.0;
                    activeAdxPeakDrawdownExitUnits = 0.0;
                    activeAdxAbsoluteExitLevel = 0.0;
                    activeContracts = 0;
                    activeEntryMinBodyPoints = 0.0;
                    activeEntryCloseBeyondEmaPoints = 0.0;
                    activeColorBlindEntry = false;
                    activeEntryType = InitialEntryType.Market;
                    activeEntrySystemMode = EntrySystemMode.Primary;
                    activeEnableStopOutFlip = false;
                    activeSecondaryContracts = 0;
                    activeSecondaryEntryLimitDistanceFromEmaPoints = 0.0;
                    activeSecondaryEntryStopDistanceFromEmaPoints = 0.0;
                    activeSecondaryEntryTakeProfitDistanceFromEmaPoints = 0.0;
                    activeSecondaryTakeProfitVariancePoints = 0.0;
                    activeStopPaddingPoints = 0.0;
                    activeTrailHardStop = false;
                    activeTakeProfitPoints = 0.0;
                    activeMinimumAtrForEntry = 0.0;
                    activeCandleReversalExitBars = 0;
                    activeCandleReversalCloseBeyondPoints = 0.0;
                    activeCandleReversalMinBodyPoints = 0.0;
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
                SetEmaVisible(emaNewYork4, false);
                SetEmaVisible(emaNewYork5, false);
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
            SetEmaVisible(emaNewYork4, ShouldShowEmaInstance(emaNewYork4));
            SetEmaVisible(emaNewYork5, ShouldShowEmaInstance(emaNewYork5));
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
            SetAdxVisible(adxNewYork4, ShowAdxOnChart);
            SetAdxVisible(adxNewYork5, ShowAdxOnChart);
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
                || (activeSession == SessionSlot.NewYork3 && ReferenceEquals(ema, emaNewYork3))
                || (activeSession == SessionSlot.NewYork4 && ReferenceEquals(ema, emaNewYork4))
                || (activeSession == SessionSlot.NewYork5 && ReferenceEquals(ema, emaNewYork5));
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
                "DUOrc_ADX_Min_" + adxTagSuffix,
                drawMin ? minThreshold : 0.0,
                drawMin ? Brushes.LimeGreen : Brushes.Transparent,
                DashStyleHelper.Solid,
                2);

            Draw.HorizontalLine(
                adx,
                "DUOrc_ADX_Max_" + adxTagSuffix,
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
                NewYork3EmaPeriod,
                NewYork4EmaPeriod,
                NewYork5EmaPeriod,
                Math.Max(1, SecondaryBiasEmaPeriod)
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
                        TrySubmitTerminalExit("SessionEnd", false);
                    else if (Position.MarketPosition == MarketPosition.Short)
                        TrySubmitTerminalExit("SessionEnd", false);
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
                    return "Europe";
                case SessionFamily.NewYork:
                    return "America";
                default:
                    return "None";
            }
        }

        private bool IsForceCloseTimeReached(DateTime barTime)
        {
            DateTime gateTime = GetHistoricalTimeGateTime(barTime);
            DateTime forceCloseDateTime;
            if (!TryGetForceCloseDateTime(gateTime, out forceCloseDateTime))
                return false;

            return gateTime >= forceCloseDateTime;
        }

        private bool IsTemporaryBlockedTradingDate(DateTime barTime)
        {
            return barTime.Date == TemporaryBlockedTradingDate;
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

            return GetHistoricalTimeGateTime(barTime).TimeOfDay >= flatByTime;
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

        private bool IsPendingEntryVarianceFor(bool isLong)
        {
            return pendingEntryVarianceActive && pendingEntryVarianceIsLong == isLong;
        }

        private bool ShouldUseEntryVariance(bool isMarketEntry, string signalName)
        {
            return EntryVariance
                && State == State.Realtime
                && isMarketEntry
                && Position.MarketPosition == MarketPosition.Flat
                && (string.Equals(signalName, LongEntrySignal, StringComparison.Ordinal) || string.Equals(signalName, ShortEntrySignal, StringComparison.Ordinal));
        }

        private bool TryQueueEntryVariance(bool isLong, int quantity, double entryPrice, double stopPrice, double takeProfitPoints, string signalName, bool isMarketEntry)
        {
            if (!ShouldUseEntryVariance(isMarketEntry, signalName))
                return false;

            if (pendingEntryVarianceActive)
            {
                LogDebug(string.Format(
                    "Entry variance skipped | reason=pending-active side={0} pendingSide={1} delay={2}s",
                    isLong ? "Long" : "Short",
                    pendingEntryVarianceIsLong ? "Long" : "Short",
                    pendingEntryVarianceDelaySeconds));
                return true;
            }

            int delaySeconds = GetEntryVarianceDelaySeconds();
            if (delaySeconds <= 0)
                return false;

            pendingEntryVarianceActive = true;
            pendingEntryVarianceIsLong = isLong;
            pendingEntryVarianceQuantity = quantity;
            pendingEntryVarianceSignalEntryPrice = entryPrice;
            pendingEntryVarianceSignalStopPrice = stopPrice;
            pendingEntryVarianceTakeProfitPoints = takeProfitPoints;
            pendingEntryVarianceIsMarketEntry = isMarketEntry;
            pendingEntryVarianceSignalName = string.IsNullOrWhiteSpace(signalName)
                ? (isLong ? LongEntrySignal : ShortEntrySignal)
                : signalName;
            pendingEntryVarianceSignalBar = CurrentBar;
            pendingEntryVarianceDelaySeconds = delaySeconds;
            pendingEntryVarianceDueUtc = DateTime.UtcNow.AddSeconds(delaySeconds);

            LogDebug(string.Format(
                "Entry variance armed | side={0} delay={1}s signalEntry={2:0.00} signalStop={3:0.00} qty={4}",
                isLong ? "Long" : "Short",
                delaySeconds,
                entryPrice,
                stopPrice,
                quantity));
            return true;
        }

        private int GetEntryVarianceDelaySeconds()
        {
            if (entryVarianceRandom == null)
                entryVarianceRandom = new Random(unchecked(Environment.TickCount ^ GetHashCode()));

            return entryVarianceRandom.Next(EntryVarianceMinDelaySeconds, EntryVarianceMaxDelaySeconds + 1);
        }

        private void ProcessPendingEntryVariance(double lastPrice)
        {
            if (!pendingEntryVarianceActive)
                return;

            if (State != State.Realtime)
            {
                CancelPendingEntryVariance("state-" + State);
                return;
            }

            if (DateTime.UtcNow < pendingEntryVarianceDueUtc)
                return;

            if (!AllowsPrimaryEntries())
            {
                CancelPendingEntryVariance("entry-system-mode");
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                CancelPendingEntryVariance("position-not-flat");
                return;
            }

            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
            {
                CancelPendingEntryVariance("invalid-configuration");
                return;
            }

            if (IsTerminalExitInFlight())
            {
                CancelPendingEntryVariance("terminal-exit-in-flight");
                return;
            }

            if (IsOrderActive(longEntryOrder) || IsOrderActive(shortEntryOrder))
            {
                CancelPendingEntryVariance("entry-order-active");
                return;
            }

            if (activeSession == SessionSlot.None || !TimeInSession(activeSession, Time[0]))
            {
                CancelPendingEntryVariance("out-of-session");
                return;
            }

            if (TimeInNewsSkip(Time[0]))
            {
                CancelPendingEntryVariance("news-skip");
                return;
            }

            if (IsAccountBalanceBlocked())
            {
                CancelPendingEntryVariance("account-blocked");
                return;
            }

            if (IsForceCloseTimeReached(Time[0]))
            {
                CancelPendingEntryVariance("force-close");
                return;
            }

            if (IsTemporaryBlockedTradingDate(Time[0]))
            {
                CancelPendingEntryVariance("temporary-date-block");
                return;
            }

            if (IsLondon3FlatByTimeReached(Time[0]))
            {
                CancelPendingEntryVariance("london3-flat-by-time");
                return;
            }

            if (activeEma == null || CurrentBar < activeEmaPeriod)
                return;

            bool isLong = pendingEntryVarianceIsLong;
            int quantity = pendingEntryVarianceQuantity;
            double entryPrice = GetEntryVarianceMarketEntryPrice(lastPrice);
            if (!IsSecondaryBiasDirectionAllowed(isLong, entryPrice))
            {
                LogDebug(string.Format(
                    "Entry variance cancelled | reason=SecondaryBias side={0} entry={1:0.00} biasEma={2:0.00}",
                    isLong ? "Long" : "Short",
                    entryPrice,
                    GetSecondaryBiasEmaValue()));
                CancelPendingEntryVariance("secondary-bias");
                return;
            }

            double stopPrice = isLong
                ? BuildLongEntryStopPrice(entryPrice, activeEma[0])
                : BuildShortEntryStopPrice(entryPrice, activeEma[0]);
            double takeProfitPoints = pendingEntryVarianceTakeProfitPoints;
            double takeProfitPrice = GetWebhookTakeProfitPrice(entryPrice, takeProfitPoints, isLong);
            string signalName = pendingEntryVarianceSignalName;
            int delaySeconds = pendingEntryVarianceDelaySeconds;
            ResetPendingEntryVariance();
            SubmitPreparedInitialEntry(isLong, quantity, entryPrice, stopPrice, takeProfitPoints, takeProfitPrice, signalName, true, true);
            LogDebug(string.Format(
                "Entry variance submitted | side={0} delay={1}s entry={2:0.00} stop={3:0.00} qty={4}",
                isLong ? "Long" : "Short",
                delaySeconds,
                entryPrice,
                stopPrice,
                quantity));
        }

        private double GetEntryVarianceMarketEntryPrice(double lastPrice)
        {
            double price = lastPrice > 0.0 ? lastPrice : pendingEntryVarianceSignalEntryPrice;
            if (price <= 0.0)
                price = Close[0];
            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        private void CancelPendingEntryVariance(string reason)
        {
            if (!pendingEntryVarianceActive)
                return;

            LogDebug(string.Format(
                "Entry variance cancelled | reason={0} side={1} delay={2}s signalBar={3}",
                reason,
                pendingEntryVarianceIsLong ? "Long" : "Short",
                pendingEntryVarianceDelaySeconds,
                pendingEntryVarianceSignalBar));
            ResetPendingEntryVariance();
            EndTradeAttempt("entry-variance-" + reason);
        }

        private void ResetPendingEntryVariance()
        {
            pendingEntryVarianceActive = false;
            pendingEntryVarianceIsLong = false;
            pendingEntryVarianceQuantity = 0;
            pendingEntryVarianceSignalEntryPrice = 0.0;
            pendingEntryVarianceSignalStopPrice = 0.0;
            pendingEntryVarianceTakeProfitPoints = 0.0;
            pendingEntryVarianceIsMarketEntry = false;
            pendingEntryVarianceSignalName = string.Empty;
            pendingEntryVarianceDueUtc = Core.Globals.MinDate;
            pendingEntryVarianceSignalBar = -1;
            pendingEntryVarianceDelaySeconds = 0;
        }

        private void CancelWorkingEntryOrders()
        {
            CancelPendingEntryVariance("cancel-working-entries");
            CancelSecondaryEntryDoors("cancel-working-entries");
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
                {
                    if (!HasSecondaryEntryExposure())
                        MarkTerminalExitPending("protective-" + orderName, Position.MarketPosition);
                    ArmProtectionAuditGracePeriod("protective-filled", 2000);
                }
            }

            if (IsStrategyExitOrderName(orderName))
            {
                if (IsOrderActive(order))
                {
                    activeExitOrder = order;
                    MarkTerminalExitPending(orderName, Position.MarketPosition);
                    ArmProtectionAuditGracePeriod("exit-active", 2000);
                }
                else
                {
                    if (MatchesTrackedOrder(activeExitOrder, order))
                        activeExitOrder = null;

                    if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                        ClearTerminalExitLock();

                    ArmProtectionAuditGracePeriod("exit-terminal", 2000);
                }
            }
        }

        private bool AuditPositionProtection(string reason)
        {
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
            {
                missingProtectionWarningPrinted = false;
                return false;
            }

            if (!missingProtectionWarningPrinted)
            {
                missingProtectionWarningPrinted = true;
                Print(string.Format(
                    "{0} | {1} | bar={2} | MISSING PROTECTION DETECTED | reason={3} side={4} hasStop={5} hasTarget={6} stop={7} target={8} | No automatic order was sent. Verify the account position manually.",
                    Time[0],
                    HeartbeatStrategyName,
                    CurrentBar,
                    reason,
                    Position.MarketPosition,
                    hasStop,
                    hasTarget,
                    FormatOrderRef(activeStopLossOrder),
                    FormatOrderRef(activeProfitTargetOrder)));
            }

            return false;
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

        private void CancelWrongSideProtectiveOrders(string reason)
        {
            if (Position.MarketPosition == MarketPosition.Flat || Account == null)
                return;

            MarketPosition staleProtectionSide = GetOppositeMarketPosition(Position.MarketPosition);
            if (staleProtectionSide == MarketPosition.Flat)
                return;

            try
            {
                foreach (Order accountOrder in Account.Orders)
                {
                    if (!IsOrderActive(accountOrder) || !IsOrderForThisInstrument(accountOrder) || !IsDuoProtectiveOrder(accountOrder))
                        continue;

                    if (IsProtectiveOrderForPositionSide(accountOrder, staleProtectionSide))
                        CancelLocalOrderIfActive(accountOrder, "StaleProtection-" + reason);
                }
            }
            catch (Exception ex)
            {
                if (DebugLogging)
                    LogDebug(string.Format("Stale protection scan failed | reason={0} error={1}", reason, ex.Message));
            }
        }

        private MarketPosition GetOppositeMarketPosition(MarketPosition side)
        {
            if (side == MarketPosition.Long)
                return MarketPosition.Short;
            if (side == MarketPosition.Short)
                return MarketPosition.Long;
            return MarketPosition.Flat;
        }

        private bool IsProtectiveOrderForPositionSide(Order order, MarketPosition side)
        {
            if (order == null)
                return false;

            if (side == MarketPosition.Long)
                return order.OrderAction == OrderAction.Sell;
            if (side == MarketPosition.Short)
                return order.OrderAction == OrderAction.BuyToCover;

            return false;
        }

        private void CancelLocalOrderIfActive(Order order, string reason)
        {
            if (!IsOrderActive(order))
                return;

            LogDebug(string.Format("CancelLocalOrder | name={0} reason={1} state={2}", order.Name, reason, order.OrderState));
            CancelOrder(order);
        }

        private bool IsDuoProtectiveOrder(Order order)
        {
            if (order == null || !IsProtectiveOrderName(order.Name))
                return false;

            string fromEntrySignal = order.FromEntrySignal ?? string.Empty;
            // Ninja managed stop/target orders can report an empty entry signal, so keep that fallback.
            return fromEntrySignal.Length == 0 || IsEntryOrderName(fromEntrySignal);
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
                && orderName.StartsWith("DUOrc", StringComparison.OrdinalIgnoreCase)
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
            missingProtectionWarningPrinted = false;
            ClearTerminalExitLock();
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

        private double BuildLongEntryStopPrice(double entryPrice, double emaValue)
        {
            double raw = emaValue - activeStopPaddingPoints;

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);
            return rounded;
        }

        private double BuildShortEntryStopPrice(double entryPrice, double emaValue)
        {
            double raw = emaValue + activeStopPaddingPoints;

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }
        private double GetInitialEntryPrice()
        {
            return Instrument.MasterInstrument.RoundToTickSize(Close[0]);
        }

        private bool IsMarketEntryMode()
        {
            return activeEntryType != InitialEntryType.EmaLimit;
        }

        private string FormatActiveEntryType()
        {
            return activeEntryType == InitialEntryType.EmaLimit ? "EMA limit" : "market";
        }

        private double GetPreparedEntryPrice(bool isLong, double emaValue)
        {
            return activeEntryType == InitialEntryType.EmaLimit
                ? GetEmaLimitEntryPrice(isLong, emaValue)
                : GetInitialEntryPrice();
        }

        private double GetEmaLimitEntryPrice(bool isLong, double emaValue)
        {
            double limitPrice = Instrument.MasterInstrument.RoundToTickSize(emaValue);
            double closePrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);

            if (isLong && limitPrice >= closePrice)
                limitPrice = Instrument.MasterInstrument.RoundToTickSize(closePrice - TickSize);
            else if (!isLong && limitPrice <= closePrice)
                limitPrice = Instrument.MasterInstrument.RoundToTickSize(closePrice + TickSize);

            return limitPrice;
        }

        private bool TrySubmitStopOutFlip(bool flipToLong, bool canTradeNow, bool oppositeSignal, double emaValue)
        {
            if (!activeEnableStopOutFlip || !IsStopOutClose(Position.MarketPosition))
                return false;

            return TrySubmitStopOutFlipEntry(
                flipToLong,
                canTradeNow,
                oppositeSignal,
                emaValue,
                GetCurrentStopOutReferencePrice(),
                "close");
        }

        private bool TrySubmitStopOutExecutionFlip(MarketPosition stoppedSide, double stopFillPrice)
        {
            if (stoppedSide == MarketPosition.Flat || lastStopOutFlipSubmittedBar == CurrentBar)
                return false;

            if (CurrentBar < Math.Max(1, Math.Max(GetMaxConfiguredEmaPeriod(), GetMaxConfiguredAdxPeriod())) || activeEma == null || CurrentBar < activeEmaPeriod)
                return false;

            bool flipToLong = stoppedSide == MarketPosition.Short;
            double emaValue = activeEma[0];
            double entryBodyPoints = Math.Abs(Close[0] - Open[0]);
            bool entryBodyPasses = activeEntryMinBodyPoints <= 0.0 || entryBodyPoints >= activeEntryMinBodyPoints;
            bool longSignal = LongEntrySignalBase(Open[0], Close[0], emaValue) && entryBodyPasses && EntryCloseBeyondEmaPasses(true, Close[0], emaValue);
            bool shortSignal = ShortEntrySignalBase(Open[0], Close[0], emaValue) && entryBodyPasses && EntryCloseBeyondEmaPasses(false, Close[0], emaValue);
            bool oppositeSignal = flipToLong ? longSignal : shortSignal;

            bool inNewsSkipNow = TimeInNewsSkip(Time[0]);
            bool inActiveSessionNow = activeSession != SessionSlot.None && TimeInSession(activeSession, Time[0]);
            bool accountBlocked = IsAccountBalanceBlocked();
            bool forceCloseBlocked = IsForceCloseTimeReached(Time[0]);
            bool temporaryDateBlocked = IsTemporaryBlockedTradingDate(Time[0]);
            bool london3FlatBlocked = IsLondon3FlatByTimeReached(Time[0]);
            double adxValue = activeAdx != null ? activeAdx[0] : 0.0;
            double atrValue = GetCurrentAtrValue();
            bool adxMinPass = !inActiveSessionNow || activeAdxThreshold <= 0.0 || adxValue >= activeAdxThreshold;
            bool adxMaxPass = !inActiveSessionNow || activeAdxMaxThreshold <= 0.0 || adxValue <= activeAdxMaxThreshold;
            bool atrMinPass = !inActiveSessionNow || activeMinimumAtrForEntry <= 0.0 || atrValue >= activeMinimumAtrForEntry;
            bool canTradeNow = inActiveSessionNow
                && !inNewsSkipNow
                && !accountBlocked
                && !forceCloseBlocked
                && !temporaryDateBlocked
                && !london3FlatBlocked
                && adxMinPass
                && adxMaxPass
                && atrMinPass;

            return TrySubmitStopOutFlipEntry(
                flipToLong,
                canTradeNow,
                oppositeSignal,
                emaValue,
                stopFillPrice,
                "stop-fill");
        }

        private bool TrySubmitStopOutFlipEntry(bool flipToLong, bool canTradeNow, bool oppositeSignal, double emaValue, double priorStopPrice, string source)
        {
            if (lastStopOutFlipSubmittedBar == CurrentBar)
                return false;

            string side = flipToLong ? "Long" : "Short";
            string direction = flipToLong ? "SHORT->LONG" : "LONG->SHORT";

            if (!canTradeNow || !oppositeSignal)
            {
                if (DebugLogging)
                {
                    LogDebug(string.Format(
                        "Stop-out flip skipped | direction={0} source={1} canTrade={2} oppositeSignal={3} close={4:0.00} stop={5:0.00}",
                        direction,
                        source,
                        canTradeNow,
                        oppositeSignal,
                        Close[0],
                        priorStopPrice));
                }
                return false;
            }

            BeginTradeAttempt(side);

            if (flipToLong)
            {
                CancelOrderIfActive(shortEntryOrder, "StopOutFlipToLong");
                if (IsOrderActive(longEntryOrder))
                {
                    LogDebug(string.Format("Stop-out flip skipped | reason=longOrderActive tracked={0}", FormatOrderRef(longEntryOrder)));
                    EndTradeAttempt("entry-order-active");
                    return false;
                }
            }
            else
            {
                CancelOrderIfActive(longEntryOrder, "StopOutFlipToShort");
                if (IsOrderActive(shortEntryOrder))
                {
                    LogDebug(string.Format("Stop-out flip skipped | reason=shortOrderActive tracked={0}", FormatOrderRef(shortEntryOrder)));
                    EndTradeAttempt("entry-order-active");
                    return false;
                }
            }

            double entryPrice = GetInitialEntryPrice();
            double stopPrice = flipToLong
                ? BuildLongEntryStopPrice(entryPrice, emaValue)
                : BuildShortEntryStopPrice(entryPrice, emaValue);
            int qty = GetEntryQuantity();
            if (qty <= 0)
            {
                LogDebug(string.Format("Stop-out flip skipped | direction={0} reason=contracts-disabled", direction));
                EndTradeAttempt("contracts-disabled");
                return false;
            }

            double takeProfitPoints = GetConfiguredEntryTakeProfitPoints();
            double takeProfitPrice = GetWebhookTakeProfitPrice(entryPrice, takeProfitPoints, flipToLong);

            if (RequireEntryConfirmation && !ShowEntryConfirmation(side, entryPrice, qty))
            {
                LogDebug(string.Format("Entry confirmation declined | Stop-out flip {0}.", direction));
                EndTradeAttempt("confirmation-declined");
                return false;
            }

            SubmitPreparedInitialEntry(
                flipToLong,
                qty,
                entryPrice,
                stopPrice,
                takeProfitPoints,
                takeProfitPrice,
                flipToLong ? LongFlipEntrySignal : ShortFlipEntrySignal);
            lastStopOutFlipSubmittedBar = CurrentBar;
            LogDebug(string.Format(
                "Stop-out flip {0} | source={1} session={2} entry={3:0.00} stop={4:0.00} priorStop={5:0.00} qty={6}",
                direction,
                source,
                FormatSessionLabel(activeSession),
                entryPrice,
                stopPrice,
                priorStopPrice,
                qty));
            return true;
        }

        private MarketPosition GetStopOutOriginalSide(Execution execution)
        {
            if (execution?.Order == null)
                return MarketPosition.Flat;

            string fromEntrySignal = execution.Order.FromEntrySignal ?? string.Empty;
            if (IsLongEntryOrderName(fromEntrySignal))
                return MarketPosition.Long;
            if (IsShortEntryOrderName(fromEntrySignal))
                return MarketPosition.Short;

            if (IsLongEntryOrderName(currentPositionEntrySignal))
                return MarketPosition.Long;
            if (IsShortEntryOrderName(currentPositionEntrySignal))
                return MarketPosition.Short;

            if (execution.Order.OrderAction == OrderAction.Sell)
                return MarketPosition.Long;
            if (execution.Order.OrderAction == OrderAction.BuyToCover)
                return MarketPosition.Short;

            return MarketPosition.Flat;
        }

        private bool IsStopOutClose(MarketPosition side)
        {
            double stopPrice = GetCurrentStopOutReferencePrice();
            if (stopPrice <= 0.0)
                return false;

            double closePrice = Instrument.MasterInstrument.RoundToTickSize(Close[0]);
            double tolerance = TickSize * 0.5;

            if (side == MarketPosition.Long)
                return closePrice <= stopPrice + tolerance;

            if (side == MarketPosition.Short)
                return closePrice >= stopPrice - tolerance;

            return false;
        }

        private double GetCurrentStopOutReferencePrice()
        {
            double stopPrice = IsOrderActive(activeStopLossOrder)
                ? GetWorkingOrderStopPrice(activeStopLossOrder, currentStopPrice)
                : currentStopPrice;

            return stopPrice > 0.0
                ? Instrument.MasterInstrument.RoundToTickSize(stopPrice)
                : 0.0;
        }

        private void SubmitPreparedInitialEntry(bool isLong, int quantity, double entryPrice, double stopPrice, double takeProfitPoints, double takeProfitPrice)
        {
            SubmitPreparedInitialEntry(isLong, quantity, entryPrice, stopPrice, takeProfitPoints, takeProfitPrice, isLong ? LongEntrySignal : ShortEntrySignal, true);
        }

        private void SubmitPreparedInitialEntry(bool isLong, int quantity, double entryPrice, double stopPrice, double takeProfitPoints, double takeProfitPrice, string entrySignal)
        {
            SubmitPreparedInitialEntry(isLong, quantity, entryPrice, stopPrice, takeProfitPoints, takeProfitPrice, entrySignal, true);
        }

        private void SubmitPreparedInitialEntry(bool isLong, int quantity, double entryPrice, double stopPrice, double takeProfitPoints, double takeProfitPrice, string entrySignal, bool isMarketEntry, bool bypassEntryVariance = false)
        {
            string signalName = string.IsNullOrWhiteSpace(entrySignal)
                ? (isLong ? LongEntrySignal : ShortEntrySignal)
                : entrySignal;

            if (!bypassEntryVariance && TryQueueEntryVariance(isLong, quantity, entryPrice, stopPrice, takeProfitPoints, signalName, isMarketEntry))
                return;

            if (isLong)
            {
                pendingLongStopForWebhook = stopPrice;
                SetStopLossByDistanceTicks(signalName, entryPrice, stopPrice);
                SetProfitTargetByDistanceTicks(signalName, takeProfitPoints);
                SendWebhook("buy", entryPrice, takeProfitPrice, stopPrice, isMarketEntry, quantity);
                StartTradeLines(entryPrice, stopPrice, takeProfitPrice, takeProfitPoints > 0.0);
                SubmitLongEntryOrder(quantity, signalName, entryPrice, isMarketEntry);
            }
            else
            {
                pendingShortStopForWebhook = stopPrice;
                SetStopLossByDistanceTicks(signalName, entryPrice, stopPrice);
                SetProfitTargetByDistanceTicks(signalName, takeProfitPoints);
                SendWebhook("sell", entryPrice, takeProfitPrice, stopPrice, isMarketEntry, quantity);
                StartTradeLines(entryPrice, stopPrice, takeProfitPrice, takeProfitPoints > 0.0);
                SubmitShortEntryOrder(quantity, signalName, entryPrice, isMarketEntry);
            }
        }

        private bool TryMaintainActiveLimitEntry(bool isLong, int quantity, double entryPrice, double stopPrice, double takeProfitPoints, double takeProfitPrice, string entrySignal)
        {
            if (activeEntryType != InitialEntryType.EmaLimit)
                return false;

            Order entryOrder = isLong ? longEntryOrder : shortEntryOrder;
            if (!IsOrderActive(entryOrder))
                return false;

            string signalName = string.IsNullOrWhiteSpace(entrySignal)
                ? (isLong ? LongEntrySignal : ShortEntrySignal)
                : entrySignal;
            SetStopLossByDistanceTicks(signalName, entryPrice, stopPrice);
            SetProfitTargetByDistanceTicks(signalName, takeProfitPoints);

            if (entryOrder.OrderState == OrderState.ChangePending)
                return true;

            double workingLimitPrice = GetWorkingOrderLimitPrice(entryOrder, 0.0);
            bool priceChanged = workingLimitPrice <= 0.0 || Math.Abs(workingLimitPrice - entryPrice) > TickSize * 0.5;
            if (!priceChanged)
                return true;

            ChangeOrder(entryOrder, quantity, entryPrice, 0.0);
            UpdatePendingEntryTradeLines(entryPrice, stopPrice, takeProfitPrice, takeProfitPoints > 0.0);
            SendWebhook("cancel");
            SendWebhook(isLong ? "buy" : "sell", entryPrice, takeProfitPrice, stopPrice, false, quantity);
            LogDebug(string.Format(
                "Update {0} EMA limit | session={1} entry={2:0.00} stop={3:0.00} qty={4}",
                isLong ? "LONG" : "SHORT",
                FormatSessionLabel(activeSession),
                entryPrice,
                stopPrice,
                quantity));
            return true;
        }

        private void UpdatePendingEntryTradeLines(double entryPrice, double stopPrice, double takeProfitPrice, bool hasTakeProfit)
        {
            if (!tradeLinesActive)
                return;

            tradeLineEntryPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice);
            tradeLineSlPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            SetTradeLineTakeProfitState(takeProfitPrice, hasTakeProfit);
            UpdateTradeLines();
        }

        private void SubmitLongEntryOrder(int quantity, string signalName, double entryPrice, bool isMarketEntry)
        {
            if (!AllowsPrimaryEntries() && !string.Equals(signalName, LongSecondaryEntrySignal, StringComparison.Ordinal))
            {
                LogDebug(string.Format(
                    "Long entry submit blocked | reason=EntrySystemMode signal={0}",
                    signalName ?? string.Empty));
                return;
            }

            if (isMarketEntry)
                EnterLong(quantity, signalName);
            else
                EnterLongLimit(0, true, quantity, entryPrice, signalName);
        }

        private void SubmitShortEntryOrder(int quantity, string signalName, double entryPrice, bool isMarketEntry)
        {
            if (!AllowsPrimaryEntries() && !string.Equals(signalName, ShortSecondaryEntrySignal, StringComparison.Ordinal))
            {
                LogDebug(string.Format(
                    "Short entry submit blocked | reason=EntrySystemMode signal={0}",
                    signalName ?? string.Empty));
                return;
            }

            if (isMarketEntry)
                EnterShort(quantity, signalName);
            else
                EnterShortLimit(0, true, quantity, entryPrice, signalName);
        }

        private double GetAdxSlopePoints()
        {
            if (activeAdx == null || CurrentBar < 1)
                return 0.0;

            return activeAdx[0] - activeAdx[1];
        }

        private void GetInfoAdxValues(out double adxValue, out double adxSlope)
        {
            adxValue = activeAdx != null ? activeAdx[0] : 0.0;
            adxSlope = GetAdxSlopePoints();

            double previewAdx;
            double previewSlope;
            if (useRealtimeInfoPreview && TryCalculateRealtimeAdxPreview(out previewAdx, out previewSlope))
            {
                adxValue = previewAdx;
                adxSlope = previewSlope;
            }
        }

        private bool TryCalculateRealtimeAdxPreview(out double adxValue, out double adxSlope)
        {
            adxValue = 0.0;
            adxSlope = 0.0;

            if (activeAdx == null || activeAdxPeriod <= 0 || CurrentBar < 1)
                return false;

            int oldestBarsAgo = Math.Min(CurrentBar, Math.Max(200, activeAdxPeriod * 20));
            double sumTr = 0.0;
            double sumDmPlus = 0.0;
            double sumDmMinus = 0.0;
            double calculatedAdx = 50.0;
            double calculatedClosedAdx = 50.0;
            int localIndex = 0;

            for (int barsAgo = oldestBarsAgo; barsAgo >= 0; barsAgo--, localIndex++)
            {
                double high0 = High[barsAgo];
                double low0 = Low[barsAgo];
                if (barsAgo == 0 && realtimeInfoPreviewBarTime == Time[0])
                {
                    if (!double.IsNaN(realtimeInfoPreviewHigh))
                        high0 = Math.Max(high0, realtimeInfoPreviewHigh);
                    if (!double.IsNaN(realtimeInfoPreviewLow))
                        low0 = Math.Min(low0, realtimeInfoPreviewLow);
                }

                double trueRange = high0 - low0;
                double tr = trueRange;
                double dmPlus = 0.0;
                double dmMinus = 0.0;

                if (localIndex > 0)
                {
                    double low1 = Low[barsAgo + 1];
                    double high1 = High[barsAgo + 1];
                    double close1 = Close[barsAgo + 1];

                    tr = Math.Max(Math.Abs(low0 - close1), Math.Max(trueRange, Math.Abs(high0 - close1)));
                    dmPlus = high0 - high1 > low1 - low0 ? Math.Max(high0 - high1, 0.0) : 0.0;
                    dmMinus = low1 - low0 > high0 - high1 ? Math.Max(low1 - low0, 0.0) : 0.0;
                }

                if (localIndex == 0)
                {
                    sumTr = tr;
                    sumDmPlus = dmPlus;
                    sumDmMinus = dmMinus;
                    calculatedAdx = 50.0;
                    continue;
                }

                if (localIndex < activeAdxPeriod)
                {
                    sumTr += tr;
                    sumDmPlus += dmPlus;
                    sumDmMinus += dmMinus;
                }
                else
                {
                    sumTr = sumTr - sumTr / activeAdxPeriod + tr;
                    sumDmPlus = sumDmPlus - sumDmPlus / activeAdxPeriod + dmPlus;
                    sumDmMinus = sumDmMinus - sumDmMinus / activeAdxPeriod + dmMinus;
                }

                double diPlus = 100.0 * (sumTr == 0.0 ? 0.0 : sumDmPlus / sumTr);
                double diMinus = 100.0 * (sumTr == 0.0 ? 0.0 : sumDmMinus / sumTr);
                double diff = Math.Abs(diPlus - diMinus);
                double sum = diPlus + diMinus;
                calculatedAdx = sum == 0.0 ? 50.0 : ((activeAdxPeriod - 1) * calculatedAdx + 100.0 * diff / sum) / activeAdxPeriod;
                if (barsAgo == 1)
                    calculatedClosedAdx = calculatedAdx;
            }

            adxValue = activeAdx[0] + (calculatedAdx - calculatedClosedAdx);
            adxSlope = adxValue - activeAdx[0];
            return !double.IsNaN(adxValue) && !double.IsInfinity(adxValue)
                && !double.IsNaN(adxSlope) && !double.IsInfinity(adxSlope);
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


        private bool LongEntrySignalBase(double openPrice, double closePrice, double emaValue)
        {
            if (activeColorBlindEntry)
                return closePrice > emaValue;

            return closePrice > openPrice && GetBodyPercentAboveEma(openPrice, closePrice, emaValue) > 0.0;
        }

        private bool ShortEntrySignalBase(double openPrice, double closePrice, double emaValue)
        {
            if (activeColorBlindEntry)
                return closePrice < emaValue;

            return closePrice < openPrice && GetBodyPercentBelowEma(openPrice, closePrice, emaValue) > 0.0;
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

        private bool EntryCloseBeyondEmaPasses(bool isLong, double close, double emaValue)
        {
            if (activeEntryCloseBeyondEmaPoints <= 0.0)
                return true;

            double distance = isLong ? close - emaValue : emaValue - close;
            return distance >= activeEntryCloseBeyondEmaPoints;
        }

        private double GetEntryCloseBeyondEmaDistancePoints(bool isLong, double close, double emaValue)
        {
            double distance = isLong ? close - emaValue : emaValue - close;
            return Math.Max(0.0, distance);
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
            DateTime gateTime = GetHistoricalTimeGateTime(time);
            TimeSpan start;
            TimeSpan end;
            if (!TryGetSessionWindow(slot, gateTime, out start, out end))
                return false;

            return IsTimeInRange(gateTime.TimeOfDay, start, end);
        }

        private DateTime GetHistoricalTimeGateTime(DateTime time)
        {
            return ShouldShiftHistoricalTimeGates()
                ? time.AddMinutes(GetHistoricalTimeGateShiftMinutes())
                : time;
        }

        private bool ShouldShiftHistoricalTimeGates()
        {
            return State == State.Historical
                && BarsPeriod != null
                && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute
                && BarsPeriod.Value > 0;
        }

        private int GetHistoricalTimeGateShiftMinutes()
        {
            return BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value > 0
                ? BarsPeriod.Value
                : 5;
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
                    if (AsiaContracts <= 0 || AsiaSessionStart == AsiaSessionEnd)
                        return false;
                    start = AsiaSessionStart;
                    end = AsiaSessionEnd;
                    return true;

                case SessionSlot.Asia2:
                    if (Asia2Contracts <= 0 || Asia2SessionStart == Asia2SessionEnd)
                        return false;
                    start = Asia2SessionStart;
                    end = Asia2SessionEnd;
                    return true;

                case SessionSlot.Asia3:
                    if (Asia3Contracts <= 0 || Asia3SessionStart == Asia3SessionEnd)
                        return false;
                    start = Asia3SessionStart;
                    end = Asia3SessionEnd;
                    return true;

                case SessionSlot.London:
                    if (LondonContracts <= 0 || LondonSessionStart == LondonSessionEnd)
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
                    if (London2Contracts <= 0 || London2SessionStart == London2SessionEnd)
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
                    if (London3Contracts <= 0 || London3SessionStart == London3SessionEnd)
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
                    if (NewYorkContracts <= 0 || NewYorkSessionStart == NewYorkSessionEnd)
                        return false;
                    start = NewYorkSessionStart;
                    end = NewYorkSessionEnd;
                    return true;

                case SessionSlot.NewYork2:
                    if (NewYork2Contracts <= 0 || NewYork2SessionStart == NewYork2SessionEnd)
                        return false;
                    start = NewYork2SessionStart;
                    end = NewYork2SessionEnd;
                    return true;

                case SessionSlot.NewYork3:
                    if (NewYork3Contracts <= 0 || NewYork3SessionStart == NewYork3SessionEnd)
                        return false;
                    start = NewYork3SessionStart;
                    end = NewYork3SessionEnd;
                    return true;

                case SessionSlot.NewYork4:
                    if (NewYork4Contracts <= 0 || NewYork4SessionStart == NewYork4SessionEnd)
                        return false;
                    start = NewYork4SessionStart;
                    end = NewYork4SessionEnd;
                    return true;

                case SessionSlot.NewYork5:
                    if (NewYork5Contracts <= 0 || NewYork5SessionStart == NewYork5SessionEnd)
                        return false;
                    start = NewYork5SessionStart;
                    end = NewYork5SessionEnd;
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

        private TimeZoneInfo GetNewYorkTimeZone()
        {
            if (newYorkTimeZone != null)
                return newYorkTimeZone;

            try
            {
                newYorkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch
            {
                try
                {
                    newYorkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                }
                catch
                {
                    newYorkTimeZone = TimeZoneInfo.Local;
                }
            }

            return newYorkTimeZone;
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
                DrawSessionBackground(slot, "DUOrc_" + FormatSessionLabel(slot), GetSessionFillBrush(slot));
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
                    VerticalFillLowerPriceBound,
                    sessionEnd,
                    VerticalFillUpperPriceBound,
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

        private void DrawNewsWindows(DateTime barTime)
        {
            if (!UseNewsSkip)
                return;

            EnsureNewsDatesInitialized(barTime);
            if (!newsDatesAvailable)
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

                string tagBase = string.Format("DUOrc_News_{0:yyyyMMdd_HHmm}", newsTime);

                Draw.Rectangle(
                    this,
                    tagBase + "_Rect",
                    false,
                    windowStart,
                    VerticalFillLowerPriceBound,
                    windowEnd,
                    VerticalFillUpperPriceBound,
                    lineBrush,
                    areaBrush,
                    2).ZOrder = -1;

                Draw.VerticalLine(this, tagBase + "_Start", windowStart, lineBrush, DashStyleHelper.DashDot, 2);
                Draw.VerticalLine(this, tagBase + "_End", windowEnd, lineBrush, DashStyleHelper.DashDot, 2);
            }
        }

        private void EnsureNewsDatesInitialized(DateTime strategyTime, bool announceIfCurrentWeekAlreadyLoaded = false, bool retryCurrentWeekIfUnavailable = false)
        {
            DateTime weekStartEt = GetWeekStart(GetEtDateForNewsReference(strategyTime));
            lock (NewsDatesSync)
            {
                bool shouldRetryUnavailableCurrentWeek =
                    retryCurrentWeekIfUnavailable &&
                    newsDatesInitialized &&
                    newsDatesWeekStart == weekStartEt &&
                    !newsDatesAvailable &&
                    weekStartEt == GetWeekStart(GetCurrentEtDate()) &&
                    ShouldUseDynamicNewsSource() &&
                    IsNewsFetchAllowed(weekStartEt, out _);

                if (newsDatesInitialized && newsDatesWeekStart == weekStartEt && !shouldRetryUnavailableCurrentWeek)
                {
                    if (announceIfCurrentWeekAlreadyLoaded && ShouldLogNewsWeekSummary(weekStartEt))
                        LogNewsWeekSummary(weekStartEt, "enable");
                    return;
                }

                RefreshNewsDates(weekStartEt);
            }
        }

        private void RefreshNewsDates(DateTime weekStartEt)
        {
            NewsDates.Clear();
            newsDatesInitialized = true;
            newsDatesAvailable = false;
            newsDatesWeekStart = weekStartEt;
            newsDatesSource = "disabled";

            var details = new List<string>();
            List<DateTime> loadedDates;
            string status = string.Empty;
            string cacheStatus = string.Empty;
            string hardcodedStatus = string.Empty;
            DateTime currentWeekEt = GetWeekStart(GetCurrentEtDate());
            bool canFetchLiveWeek = ShouldUseDynamicNewsSource() && weekStartEt == currentWeekEt;
            string fetchGateStatus = string.Empty;
            bool fetchAllowed = canFetchLiveWeek && IsNewsFetchAllowed(weekStartEt, out fetchGateStatus);
            if (fetchAllowed && TryFetchWeeklyNewsDates(weekStartEt, out loadedDates, out status))
            {
                MergeNewsDates(loadedDates);
                newsDatesAvailable = true;
                newsDatesSource = "weekly-json";
                details.Add(status);
                ClearNewsFetchBlock(weekStartEt);

                string cacheWriteStatus;
                TryWriteNewsDatesCache(weekStartEt, loadedDates, out cacheWriteStatus);
                details.Add(cacheWriteStatus);
            }
            else
            {
                details.Add(canFetchLiveWeek
                    ? (!string.IsNullOrWhiteSpace(fetchGateStatus) ? fetchGateStatus : status)
                    : "feed-skip non-current-week-or-disabled");

                List<DateTime> cachedDates;
                if (TryLoadNewsDatesCache(weekStartEt, out cachedDates, out cacheStatus))
                {
                    MergeNewsDates(cachedDates);
                    newsDatesAvailable = true;
                    newsDatesSource = "cache";
                    details.Add(cacheStatus);
                }
                else
                {
                    details.Add(cacheStatus);

                    List<DateTime> hardcodedDates;
                    if (TryLoadHardcodedNewsDates(weekStartEt, out hardcodedDates, out hardcodedStatus))
                    {
                        MergeNewsDates(hardcodedDates);
                        newsDatesAvailable = true;
                        newsDatesSource = "hardcoded";
                        details.Add(hardcodedStatus);
                    }
                    else
                    {
                        details.Add(hardcodedStatus);
                    }
                }
            }

            NewsDates.Sort();
            AppendNewsFetchLog(string.Format(
                CultureInfo.InvariantCulture,
                "Refresh complete | source={0} enabled={1} week={2:yyyy-MM-dd} count={3} events=[{4}] details={5}",
                newsDatesSource,
                newsDatesAvailable,
                weekStartEt,
                NewsDates.Count,
                FormatNewsDatesForLog(NewsDates),
                string.Join(" | ", details.ToArray())));
            if (ShouldLogNewsWeekSummary(weekStartEt))
            {
                LogNewsWeekSummary(weekStartEt, "load");
                if (!newsDatesAvailable)
                    Print(string.Format("Weekly news error: {0} | {1} | {2}", status, cacheStatus, hardcodedStatus));
            }
        }

        private void MergeNewsDates(IEnumerable<DateTime> dates)
        {
            if (dates == null)
                return;

            foreach (DateTime date in dates)
                AddUniqueNewsDate(NewsDates, date);
        }

        private bool TryFetchWeeklyNewsDates(DateTime weekStartEt, out List<DateTime> loadedDates, out string status)
        {
            loadedDates = new List<DateTime>();
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                using (var client = new System.Net.WebClient())
                {
                    client.Encoding = System.Text.Encoding.UTF8;
                    client.Headers[System.Net.HttpRequestHeader.UserAgent] =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
                    client.Headers[System.Net.HttpRequestHeader.Accept] = "application/json,text/plain,*/*";
                    client.Headers[System.Net.HttpRequestHeader.AcceptLanguage] = "en-US,en;q=0.9";

                    string json = client.DownloadString(WeeklyNewsJsonUrl);
                    var serializer = new JavaScriptSerializer();
                    List<Dictionary<string, object>> rows = serializer.Deserialize<List<Dictionary<string, object>>>(json);
                    DateTime weekEndEt = weekStartEt.AddDays(7);

                    if (rows != null)
                    {
                        for (int i = 0; i < rows.Count; i++)
                        {
                            Dictionary<string, object> row = rows[i];
                            DateTime newsDate;
                            if (!TryParseWeeklyNewsDate(row, out newsDate))
                                continue;

                            if (newsDate < weekStartEt || newsDate >= weekEndEt)
                                continue;

                            string currency = GetNewsRowString(row, "country");
                            string impact = GetNewsRowString(row, "impact");
                            if (!string.Equals(currency, NewsTargetCurrency, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(impact, NewsTargetImpact, StringComparison.OrdinalIgnoreCase))
                                continue;

                            AddUniqueNewsDate(loadedDates, newsDate);
                        }
                    }

                    status = string.Format(
                        CultureInfo.InvariantCulture,
                        "feed-ok url={0} rows={1} matches={2}",
                        WeeklyNewsJsonUrl,
                        rows != null ? rows.Count : 0,
                        loadedDates.Count);
                    return true;
                }
            }
            catch (System.Net.WebException ex)
            {
                var response = ex.Response as System.Net.HttpWebResponse;
                if (response != null && (int)response.StatusCode == 429)
                {
                    status = string.Format("feed-error 429 Too Many Requests ({0})", ex.Message);
                    SetNewsFetchBlock(weekStartEt, TimeSpan.FromMinutes(15), status);
                    return false;
                }

                status = string.Format("feed-error {0}", ex.Message);
                SetNewsFetchBlock(weekStartEt, TimeSpan.FromMinutes(2), status);
                return false;
            }
            catch (Exception ex)
            {
                status = string.Format("feed-error {0}", ex.Message);
                SetNewsFetchBlock(weekStartEt, TimeSpan.FromMinutes(2), status);
                return false;
            }
        }

        private bool TryWriteNewsDatesCache(DateTime weekStartEt, List<DateTime> dates, out string status)
        {
            try
            {
                string path = GetNewsCachePath(weekStartEt);
                var lines = new List<string>();
                lines.Add(NewsCacheWeekPrefix + weekStartEt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                lines.Add(NewsCacheUpdatedPrefix + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

                if (dates != null)
                {
                    List<DateTime> orderedDates = dates.OrderBy(d => d).ToList();
                    for (int i = 0; i < orderedDates.Count; i++)
                        lines.Add(orderedDates[i].ToString("yyyy-MM-dd,HH:mm", CultureInfo.InvariantCulture));
                }

                System.IO.File.WriteAllLines(path, lines.ToArray());
                status = string.Format("cache-write-ok file={0}", path);
                return true;
            }
            catch (Exception ex)
            {
                status = string.Format("cache-write-error {0}", ex.Message);
                return false;
            }
        }

        private bool TryLoadNewsDatesCache(DateTime weekStartEt, out List<DateTime> cachedDates, out string status)
        {
            cachedDates = new List<DateTime>();
            try
            {
                string path = GetNewsCachePath(weekStartEt);
                if (!System.IO.File.Exists(path))
                {
                    status = string.Format("cache-miss file-not-found file={0}", path);
                    return false;
                }

                string[] lines = System.IO.File.ReadAllLines(path);
                if (lines == null || lines.Length == 0)
                {
                    status = "cache-miss empty";
                    return false;
                }

                string weekLine = lines.FirstOrDefault(line => line.StartsWith(NewsCacheWeekPrefix, StringComparison.Ordinal));
                if (string.IsNullOrWhiteSpace(weekLine))
                {
                    status = "cache-miss missing-week";
                    return false;
                }

                DateTime cachedWeekStart;
                string weekValue = weekLine.Substring(NewsCacheWeekPrefix.Length).Trim();
                if (!DateTime.TryParseExact(weekValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out cachedWeekStart))
                {
                    status = string.Format("cache-miss invalid-week {0}", weekValue);
                    return false;
                }

                if (cachedWeekStart != weekStartEt)
                {
                    status = string.Format(
                        CultureInfo.InvariantCulture,
                        "cache-stale cached-week={0:yyyy-MM-dd}",
                        cachedWeekStart);
                    return false;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i] != null ? lines[i].Trim() : string.Empty;
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    DateTime parsed;
                    if (!DateTime.TryParseExact(line, "yyyy-MM-dd,HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                        continue;

                    AddUniqueNewsDate(cachedDates, parsed);
                }

                status = string.Format(
                    CultureInfo.InvariantCulture,
                    "cache-ok file={0} matches={1}",
                    path,
                    cachedDates.Count);
                return true;
            }
            catch (Exception ex)
            {
                status = string.Format("cache-error {0}", ex.Message);
                return false;
            }
        }

        private bool TryLoadHardcodedNewsDates(DateTime weekStartEt, out List<DateTime> hardcodedDates, out string status)
        {
            hardcodedDates = new List<DateTime>();
            if (string.IsNullOrWhiteSpace(NewsDatesRaw))
            {
                status = "hardcoded-empty";
                return false;
            }

            DateTime weekEndEt = weekStartEt.AddDays(7);
            string[] entries = NewsDatesRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int parsedCount = 0;
            int invalidCount = 0;
            DateTime firstParsed = DateTime.MaxValue;
            DateTime lastParsed = DateTime.MinValue;

            for (int i = 0; i < entries.Length; i++)
            {
                string trimmed = entries[i] != null ? entries[i].Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                DateTime parsed;
                if (!DateTime.TryParseExact(trimmed, "yyyy-MM-dd,HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    invalidCount++;
                    LogDebug(string.Format("Invalid hardcoded news date entry: {0}", trimmed));
                    continue;
                }

                parsedCount++;
                if (parsed < firstParsed)
                    firstParsed = parsed;
                if (parsed > lastParsed)
                    lastParsed = parsed;

                if (parsed < weekStartEt || parsed >= weekEndEt)
                    continue;

                AddUniqueNewsDate(hardcodedDates, parsed);
            }

            bool inCoverage = parsedCount > 0 &&
                weekStartEt >= GetWeekStart(firstParsed) &&
                weekStartEt <= GetWeekStart(lastParsed);

            status = string.Format(
                CultureInfo.InvariantCulture,
                "hardcoded-{0} parsed={1} matches={2} invalid={3} coverage={4:yyyy-MM-dd}..{5:yyyy-MM-dd}",
                inCoverage ? "ok" : "miss",
                parsedCount,
                hardcodedDates.Count,
                invalidCount,
                parsedCount > 0 ? GetWeekStart(firstParsed) : DateTime.MinValue,
                parsedCount > 0 ? GetWeekStart(lastParsed) : DateTime.MinValue);
            return inCoverage;
        }

        private bool TryParseWeeklyNewsDate(Dictionary<string, object> row, out DateTime newsDate)
        {
            newsDate = DateTime.MinValue;
            string rawDate = GetNewsRowString(row, "date");
            if (string.IsNullOrWhiteSpace(rawDate))
                return false;

            DateTimeOffset timestamp;
            if (!DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
                return false;

            newsDate = timestamp.DateTime;
            return true;
        }

        private static string GetNewsRowString(Dictionary<string, object> row, string key)
        {
            object raw;
            if (row == null || string.IsNullOrWhiteSpace(key) || !row.TryGetValue(key, out raw) || raw == null)
                return string.Empty;

            return raw.ToString().Trim();
        }

        private static void AddUniqueNewsDate(List<DateTime> target, DateTime value)
        {
            if (target == null || !IsTargetNewsTime(value.TimeOfDay) || target.Contains(value))
                return;

            target.Add(value);
        }

        private static bool IsTargetNewsTime(TimeSpan time)
        {
            return time == new TimeSpan(8, 30, 0) || time == new TimeSpan(14, 0, 0);
        }

        private string GetNewsCachePath(DateTime weekStartEt)
        {
            string fileName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1:yyyy-MM-dd}.txt",
                NewsCacheFilePrefix,
                weekStartEt);
            return System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, fileName);
        }

        private bool IsNewsFetchAllowed(DateTime weekStartEt, out string status)
        {
            if (newsFetchBlockedWeekStart == weekStartEt && newsFetchBlockedUntilUtc > DateTime.UtcNow)
            {
                status = string.Format(
                    CultureInfo.InvariantCulture,
                    "feed-skip cooldown-until={0:o} reason={1}",
                    newsFetchBlockedUntilUtc,
                    newsFetchBlockedReason);
                return false;
            }

            status = string.Empty;
            return true;
        }

        private void SetNewsFetchBlock(DateTime weekStartEt, TimeSpan cooldown, string reason)
        {
            newsFetchBlockedWeekStart = weekStartEt;
            newsFetchBlockedUntilUtc = DateTime.UtcNow.Add(cooldown);
            newsFetchBlockedReason = reason ?? string.Empty;
        }

        private void ClearNewsFetchBlock(DateTime weekStartEt)
        {
            if (newsFetchBlockedWeekStart != weekStartEt)
                return;

            newsFetchBlockedWeekStart = DateTime.MinValue;
            newsFetchBlockedUntilUtc = DateTime.MinValue;
            newsFetchBlockedReason = string.Empty;
        }

        private DateTime GetCurrentEtDate()
        {
            DateTime utcNow = DateTime.UtcNow;
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(utcNow, GetNewYorkTimeZone()).Date;
            }
            catch
            {
                return utcNow.Date;
            }
        }

        private DateTime GetEtDateForNewsReference(DateTime strategyTime)
        {
            try
            {
                TimeZoneInfo sourceTimeZone = GetTargetTimeZone();
                DateTime unspecifiedTime = DateTime.SpecifyKind(strategyTime, DateTimeKind.Unspecified);
                DateTime utcTime = TimeZoneInfo.ConvertTimeToUtc(unspecifiedTime, sourceTimeZone);
                return TimeZoneInfo.ConvertTimeFromUtc(utcTime, GetNewYorkTimeZone()).Date;
            }
            catch
            {
                return strategyTime.Date;
            }
        }

        private DateTime GetNewsReferenceStrategyTime()
        {
            DateTime latestLoadedTime;
            if (TryGetLatestLoadedBarTime(out latestLoadedTime))
                return latestLoadedTime;

            return GetCurrentEtDate();
        }

        private bool TryGetLatestLoadedBarTime(out DateTime strategyTime)
        {
            strategyTime = DateTime.MinValue;
            try
            {
                var bars = Bars;
                if (bars == null)
                    return false;

                var barsType = bars.GetType();
                var countProp = barsType.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var getTimeMethod = barsType.GetMethod(
                    "GetTime",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int) },
                    null);

                if (countProp == null || getTimeMethod == null)
                    return false;

                int count = (int)countProp.GetValue(bars, null);
                if (count <= 0)
                    return false;

                object raw = getTimeMethod.Invoke(bars, new object[] { count - 1 });
                if (!(raw is DateTime))
                    return false;

                strategyTime = (DateTime)raw;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ShouldUseDynamicNewsSource()
        {
            return Account != null && !string.Equals(Account.Name, "Backtest", StringComparison.OrdinalIgnoreCase);
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

        private bool ShouldLogNewsWeekSummary(DateTime weekStartEt)
        {
            return weekStartEt == GetWeekStart(GetCurrentEtDate());
        }

        private void LogNewsWeekSummary(DateTime weekStartEt, string reason)
        {
            if (lastPrintedNewsWeekStart == weekStartEt)
                return;

            lastPrintedNewsWeekStart = weekStartEt;

            Print("Weekly news");
            Print("-------------");

            if (!newsDatesAvailable)
            {
                Print("unavailable");
                return;
            }

            if (NewsDates.Count == 0)
            {
                Print("none");
                return;
            }

            for (int i = 0; i < NewsDates.Count; i++)
                Print(NewsDates[i].ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        }

        private bool TimeInNewsSkip(DateTime time)
        {
            if (!UseNewsSkip)
                return false;

            DateTime gateTime = GetHistoricalTimeGateTime(time);
            EnsureNewsDatesInitialized(gateTime);
            if (!newsDatesAvailable)
                return false;

            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime newsTime = NewsDates[i];
                if (newsTime.Date != gateTime.Date)
                    continue;

                DateTime windowStart = newsTime.AddMinutes(-NewsBlockMinutes);
                DateTime windowEnd = newsTime.AddMinutes(NewsBlockMinutes);
                if (gateTime >= windowStart && gateTime <= windowEnd)
                    return true;
            }

            return false;
        }

        private bool IsSameNewsWeek(DateTime first, DateTime second)
        {
            return GetWeekStart(GetEtDateForNewsReference(GetHistoricalTimeGateTime(first))) == GetWeekStart(GetEtDateForNewsReference(GetHistoricalTimeGateTime(second)));
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
                case SessionSlot.NewYork4:
                    return newYork4SessionClosed;
                case SessionSlot.NewYork5:
                    return newYork5SessionClosed;
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
                case SessionSlot.NewYork4:
                    newYork4SessionClosed = value;
                    break;
                case SessionSlot.NewYork5:
                    newYork5SessionClosed = value;
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
                    return "Europe1";
                case SessionSlot.London2:
                    return "Europe2";
                case SessionSlot.London3:
                    return "Europe3";
                case SessionSlot.NewYork:
                    return "America1";
                case SessionSlot.NewYork2:
                    return "America2";
                case SessionSlot.NewYork3:
                    return "America3";
                case SessionSlot.NewYork4:
                    return "America4";
                case SessionSlot.NewYork5:
                    return "America5";
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
                "SessionConfig ({0}) | session={1} inSessionNow={2} closeAtSessionEnd={3} forceClose={4} start={5:hh\\:mm} end={6:hh\\:mm} ema={7} adxMin={8:0.##} adxMax={9:0.##} adxPeakDd={10:0.##} adxAbsExit={11:0.##} tpPts={12:0.##} contracts={13} slPad={14:0.##} trailHardSl={15} entryMinBody={16:0.##} entryCloseEmaPts={17:0.##} candleRev={18}/{19:0.##}/{20:0.##} atrMin={21:0.##}",
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
                activeAdxPeakDrawdownExitUnits,
                activeAdxAbsoluteExitLevel,
                activeTakeProfitPoints,
                activeContracts,
                activeStopPaddingPoints,
                activeTrailHardStop,
                activeEntryMinBodyPoints,
                activeEntryCloseBeyondEmaPoints,
                activeCandleReversalExitBars,
                activeCandleReversalCloseBeyondPoints,
                activeCandleReversalMinBodyPoints,
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

                    if (!TryAddPositionInfoValueRuns(text, normalizedValue))
                    {
                        var valueRun = BuildInfoValueRun(normalizedValue, stateValueBrush);
                        text.Inlines.Add(valueRun);
                    }
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

        private bool TryAddPositionInfoValueRuns(TextBlock text, string value)
        {
            if (text == null)
                return false;

            if (string.Equals(value, "In Long Now", StringComparison.Ordinal))
            {
                text.Inlines.Add(new Run("In ") { Foreground = InfoValueBrush });
                text.Inlines.Add(new Run("Long") { Foreground = Brushes.LimeGreen });
                text.Inlines.Add(new Run(" Now") { Foreground = InfoValueBrush });
                return true;
            }

            if (string.Equals(value, "In Short Now", StringComparison.Ordinal))
            {
                text.Inlines.Add(new Run("In ") { Foreground = InfoValueBrush });
                text.Inlines.Add(new Run("Short") { Foreground = Brushes.IndianRed });
                text.Inlines.Add(new Run(" Now") { Foreground = InfoValueBrush });
                return true;
            }

            return false;
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
            string contractsText = Math.Max(0, activeContracts).ToString(CultureInfo.InvariantCulture);
            double adxValue;
            double adxSlope;
            GetInfoAdxValues(out adxValue, out adxSlope);
            bool adxMinEnabled = activeAdxThreshold > 0.0;
            bool adxMaxEnabled = activeAdxMaxThreshold > 0.0;
            bool belowMin = adxMinEnabled && adxValue < activeAdxThreshold;
            bool overMax = adxMaxEnabled && adxValue > activeAdxMaxThreshold;
            double atrValue = GetCurrentAtrValue();
            bool atrMinEnabled = activeMinimumAtrForEntry > 0.0;
            bool atrBelowMin = atrMinEnabled && atrValue < activeMinimumAtrForEntry;

            string adxState;
            Brush adxBrush;
            if (overMax)
            {
                adxState = FormatInfoThresholdMetric("Max", activeAdxMaxThreshold, adxValue);
                adxBrush = Brushes.OrangeRed;
            }
            else if (belowMin)
            {
                adxState = FormatInfoThresholdMetric("Weak", activeAdxThreshold, adxValue);
                adxBrush = Brushes.IndianRed;
            }
            else
            {
                double normalThreshold = adxMinEnabled ? activeAdxThreshold : (adxMaxEnabled ? activeAdxMaxThreshold : 0.0);
                adxState = FormatInfoThresholdMetric("Normal", normalThreshold, adxValue);
                adxBrush = Brushes.LimeGreen;
            }

            string atrState = atrBelowMin
                ? FormatInfoThresholdMetric("Weak", activeMinimumAtrForEntry, atrValue)
                : FormatInfoThresholdMetric("Normal", activeMinimumAtrForEntry, atrValue);
            Brush atrBrush = atrBelowMin ? Brushes.IndianRed : Brushes.LimeGreen;

            lines.Add((string.Format("DUOrc v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(("Contracts:", contractsText, Brushes.LightGray, Brushes.LightGray));
            lines.Add(("ADX:", adxState, Brushes.LightGray, adxBrush));
            lines.Add(("ATR:", atrState, Brushes.LightGray, atrBrush));
            SessionSlot infoSession = DetermineCurrentSessionForTime(Time[0]);
            var closeSignal = BuildFiveMinuteCloseSignalInfo(infoSession, adxValue, atrValue);
            lines.Add(("5m Close:", closeSignal.value, Brushes.LightGray, closeSignal.brush));
            if (!UseNewsSkip)
            {
                lines.Add(("News:", "Disabled", Brushes.LightGray, Brushes.LightGray));
            }
            else
            {
                EnsureNewsDatesInitialized(Time[0]);
                if (!newsDatesAvailable)
                {
                    lines.Add(("News:", "Disabled", Brushes.LightGray, Brushes.IndianRed));
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
            }

            lines.Add(("Session:", FormatSessionLabel(infoSession), Brushes.LightGray, Brushes.LightGray));
            lines.Add(("AutoEdge Systems™", string.Empty, InfoLabelBrush, Brushes.Transparent));

            return lines;
        }

        private (string value, Brush brush) BuildFiveMinuteCloseSignalInfo(SessionSlot infoSession, double adxValue, double atrValue)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                return ("In Long Now", InfoValueBrush);
            if (Position.MarketPosition == MarketPosition.Short)
                return ("In Short Now", InfoValueBrush);

            if (!CanEvaluateFiveMinuteCloseSignal(infoSession, adxValue, atrValue))
                return ("No Trade", Brushes.IndianRed);

            double closePrice = GetInfoClosePrice();
            double openPrice = Open[0];
            double emaValue = activeEma[0];
            double entryBodyPoints = Math.Abs(closePrice - openPrice);
            if (activeEntryMinBodyPoints > 0.0 && entryBodyPoints < activeEntryMinBodyPoints)
                return ("No Trade", Brushes.IndianRed);

            if (LongEntrySignalBase(openPrice, closePrice, emaValue) && EntryCloseBeyondEmaPasses(true, closePrice, emaValue) && !IsOrderActive(longEntryOrder))
                return ("Long", Brushes.LimeGreen);

            if (ShortEntrySignalBase(openPrice, closePrice, emaValue) && EntryCloseBeyondEmaPasses(false, closePrice, emaValue) && !IsOrderActive(shortEntryOrder))
                return ("Short", Brushes.LimeGreen);

            return ("No Trade", Brushes.IndianRed);
        }

        private bool CanEvaluateFiveMinuteCloseSignal(SessionSlot infoSession, double adxValue, double atrValue)
        {
            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
                return false;

            if (CurrentBar < Math.Max(1, Math.Max(GetMaxConfiguredEmaPeriod(), GetMaxConfiguredAdxPeriod())))
                return false;

            if (infoSession == SessionSlot.None || activeSession != infoSession)
                return false;

            if (IsForceCloseTimeReached(Time[0]) || IsTemporaryBlockedTradingDate(Time[0]) || TimeInNewsSkip(Time[0]))
                return false;

            if (IsAccountBalanceInfoBlocked() || Position.MarketPosition != MarketPosition.Flat)
                return false;

            if (terminalExitPending || IsOrderActive(activeExitOrder) || GetEntryQuantity() <= 0)
                return false;

            if (activeEma == null || CurrentBar < activeEmaPeriod)
                return false;

            if (activeAdxThreshold > 0.0 && adxValue < activeAdxThreshold)
                return false;

            if (activeAdxMaxThreshold > 0.0 && adxValue > activeAdxMaxThreshold)
                return false;

            if (activeMinimumAtrForEntry > 0.0 && atrValue < activeMinimumAtrForEntry)
                return false;

            return true;
        }

        private bool IsAccountBalanceInfoBlocked()
        {
            if (MaxAccountBalance <= 0.0)
                return false;

            if (accountBalanceLimitReached)
                return true;

            double balance;
            return TryGetCurrentCashValue(out balance) && balance >= MaxAccountBalance;
        }

        private double GetInfoClosePrice()
        {
            double closePrice = Close[0];
            if (useRealtimeInfoPreview
                && realtimeInfoPreviewBarTime == Time[0]
                && !double.IsNaN(realtimeInfoPreviewClose)
                && !double.IsInfinity(realtimeInfoPreviewClose))
            {
                closePrice = realtimeInfoPreviewClose;
            }

            return Instrument.MasterInstrument.RoundToTickSize(closePrice);
        }

        private string FormatInfoThresholdMetric(string state, double thresholdValue, double currentValue)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}/{2}",
                state,
                thresholdValue > 0.0 ? FormatInfoMetric(thresholdValue) : "Off",
                FormatInfoMetric(currentValue));
        }

        private string FormatInfoMetric(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
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
            EnsureNewsDatesInitialized(time);

            var weekNews = new List<DateTime>();
            DateTime weekStart = newsDatesWeekStart != DateTime.MinValue ? newsDatesWeekStart : GetWeekStart(time.Date);
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
            int diff = (7 + (date.DayOfWeek - DayOfWeek.Sunday)) % 7;
            return date.AddDays(-diff).Date;
        }

        private string GetAddOnVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Version version = assembly.GetName().Version;
            return version != null ? version.ToString() : "0.0.0.0";
        }

        private string GetVersionedStrategyName(string baseName)
        {
            return baseName + GetAddOnVersion().Replace(".", string.Empty);
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
                NewYork3AdxPeriod,
                NewYork4AdxPeriod,
                NewYork5AdxPeriod
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
            return Math.Max(0, activeContracts);
        }

        private int GetSecondaryEntryQuantity()
        {
            return Math.Max(0, activeSecondaryContracts);
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
                    TrySubmitTerminalExit("MaxAccountBalance");
                else if (Position.MarketPosition == MarketPosition.Short)
                    TrySubmitTerminalExit("MaxAccountBalance");

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

        private bool SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime)
                return false;

            if (WebhookProviderType == WebhookProvider.ProjectX)
            {
                int orderQtyForProvider = quantityOverride > 0 ? quantityOverride : Math.Max(0, activeContracts);
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
                int orderQty = quantityOverride > 0 ? quantityOverride : Math.Max(0, activeContracts);
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

        private void SyncProjectXProtectionUpdate(ProjectXProtectionOrderKind kind, double price, string reason)
        {
            if (State != State.Realtime || WebhookProviderType != WebhookProvider.ProjectX)
                return;

            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            price = Instrument.MasterInstrument.RoundToTickSize(price);
            if (price <= 0.0)
                return;

            if (!EnsureProjectXSession())
            {
                LogDebug(string.Format(
                    "ProjectX protection sync skipped | reason={0} kind={1} cause=auth-unavailable",
                    reason,
                    FormatProjectXProtectionKind(kind)));
                return;
            }

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetProjectXTargets(out targetAccounts, out contractId))
            {
                LogDebug(string.Format(
                    "ProjectX protection sync skipped | reason={0} kind={1} cause=account-selection-or-contract-unavailable",
                    reason,
                    FormatProjectXProtectionKind(kind)));
                return;
            }

            int expectedOrderSide = Position.MarketPosition == MarketPosition.Long ? 1 : 0;
            int fallbackSize = Math.Max(1, Math.Abs(Position.Quantity));

            foreach (var account in targetAccounts)
            {
                try
                {
                    var order = SelectProjectXProtectionOrder(account.Id, contractId, kind, expectedOrderSide);
                    if (order == null)
                    {
                        LogDebug(string.Format(
                            "ProjectX protection sync skipped | reason={0} accountId={1} kind={2} price={3:0.00} cause=no-unique-open-order",
                            reason,
                            account.Id,
                            FormatProjectXProtectionKind(kind),
                            price));
                        continue;
                    }

                    long orderId;
                    if (!TryGetProjectXOrderLong(order, "id", out orderId) || orderId <= 0)
                    {
                        LogDebug(string.Format(
                            "ProjectX protection sync skipped | reason={0} accountId={1} kind={2} price={3:0.00} cause=missing-order-id",
                            reason,
                            account.Id,
                            FormatProjectXProtectionKind(kind),
                            price));
                        continue;
                    }

                    int size;
                    if (!TryGetProjectXOrderInt(order, "size", out size) || size <= 0)
                        size = fallbackSize;

                    string response = ProjectXModifyProtectionOrder(account.Id, orderId, size, kind, price);
                    bool success;
                    if (TryGetJsonBool(response, "success", out success) && !success)
                    {
                        LogDebug(string.Format(
                            "ProjectX protection sync failed | reason={0} accountId={1} orderId={2} kind={3} price={4:0.00}",
                            reason,
                            account.Id,
                            orderId,
                            FormatProjectXProtectionKind(kind),
                            price));
                    }
                    else
                    {
                        LogDebug(string.Format(
                            "ProjectX protection sync sent | reason={0} accountId={1} orderId={2} kind={3} price={4:0.00}",
                            reason,
                            account.Id,
                            orderId,
                            FormatProjectXProtectionKind(kind),
                            price));
                    }
                }
                catch (Exception ex)
                {
                    LogDebug(string.Format(
                        "ProjectX protection sync error | reason={0} accountId={1} kind={2} price={3:0.00} error={4}",
                        reason,
                        account.Id,
                        FormatProjectXProtectionKind(kind),
                        price,
                        ex.Message));
                }
            }
        }

        private Dictionary<string, object> SelectProjectXProtectionOrder(int accountId, string contractId, ProjectXProtectionOrderKind kind, int expectedOrderSide)
        {
            var matches = GetProjectXOpenOrders(accountId, contractId)
                .Where(o => IsProjectXProtectionOrderMatch(o, kind, expectedOrderSide))
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        private bool IsProjectXProtectionOrderMatch(Dictionary<string, object> order, ProjectXProtectionOrderKind kind, int expectedOrderSide)
        {
            if (order == null)
                return false;

            int side;
            if (TryGetProjectXOrderInt(order, "side", out side) && side != expectedOrderSide)
                return false;

            int type;
            if (TryGetProjectXOrderInt(order, "type", out type))
            {
                if (kind == ProjectXProtectionOrderKind.StopLoss)
                    return type == 4;

                if (kind == ProjectXProtectionOrderKind.TakeProfit)
                    return type == 1;
            }

            double price;
            if (kind == ProjectXProtectionOrderKind.StopLoss)
                return TryGetProjectXOrderDouble(order, "stopPrice", out price) && price > 0.0;

            return TryGetProjectXOrderDouble(order, "limitPrice", out price) && price > 0.0;
        }

        private string ProjectXModifyProtectionOrder(int accountId, long orderId, int size, ProjectXProtectionOrderKind kind, double price)
        {
            string limitPrice = kind == ProjectXProtectionOrderKind.TakeProfit
                ? FormatProjectXPrice(price)
                : "null";
            string stopPrice = kind == ProjectXProtectionOrderKind.StopLoss
                ? FormatProjectXPrice(price)
                : "null";

            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"orderId\":{1},\"size\":{2},\"limitPrice\":{3},\"stopPrice\":{4},\"trailPrice\":null}}",
                accountId,
                orderId,
                Math.Max(1, size),
                limitPrice,
                stopPrice);

            return ProjectXPost("/api/Order/modify", json, true);
        }

        private string FormatProjectXPrice(double price)
        {
            return Instrument.MasterInstrument.RoundToTickSize(price).ToString("0.########", CultureInfo.InvariantCulture);
        }

        private string FormatProjectXProtectionKind(ProjectXProtectionOrderKind kind)
        {
            return kind == ProjectXProtectionOrderKind.StopLoss ? "stop-loss" : "take-profit";
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
            foreach (var order in GetProjectXOpenOrders(accountId, contractId))
            {
                long id;
                if (TryGetProjectXOrderLong(order, "id", out id) && id > 0)
                    orderIds.Add(id);
            }

            return orderIds;
        }

        private List<Dictionary<string, object>> GetProjectXOpenOrders(int accountId, string contractId)
        {
            var orders = new List<Dictionary<string, object>>();
            string searchJson = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string searchResponse = ProjectXPost("/api/Order/searchOpen", searchJson, true);
            bool success;
            if (TryGetJsonBool(searchResponse, "success", out success) && !success)
                return orders;

            foreach (var order in ExtractProjectXOrders(searchResponse))
            {
                object contractObj;
                if (!order.TryGetValue("contractId", out contractObj))
                    continue;
                if (!string.Equals(contractObj != null ? contractObj.ToString() : string.Empty, contractId, StringComparison.OrdinalIgnoreCase))
                    continue;

                orders.Add(order);
            }

            return orders;
        }

        private bool TryGetProjectXOrderInt(Dictionary<string, object> order, string key, out int value)
        {
            value = 0;
            object raw;
            return order != null
                && order.TryGetValue(key, out raw)
                && TryConvertToInt(raw, out value);
        }

        private bool TryGetProjectXOrderLong(Dictionary<string, object> order, string key, out long value)
        {
            value = 0;
            object raw;
            return order != null
                && order.TryGetValue(key, out raw)
                && TryConvertToLong(raw, out value);
        }

        private bool TryGetProjectXOrderDouble(Dictionary<string, object> order, string key, out double value)
        {
            value = 0.0;
            object raw;
            return order != null
                && order.TryGetValue(key, out raw)
                && TryConvertToDouble(raw, out value);
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

        private bool TryConvertToDouble(object raw, out double value)
        {
            value = 0.0;
            if (raw == null)
                return false;

            if (raw is double)
            {
                value = (double)raw;
                return true;
            }

            if (raw is decimal)
            {
                value = (double)(decimal)raw;
                return true;
            }

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

            string text = raw.ToString();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
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
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "Asia 1 session start time in chart time zone.", GroupName = "04. Asia 1", Order = 1)]
        public TimeSpan AsiaSessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "Asia 1 session end time in chart time zone.", GroupName = "04. Asia 1", Order = 2)]
        public TimeSpan AsiaSessionEnd { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Asia 1 entry and exit logic.", GroupName = "04. Asia 1", Order = 4)]
        public int AsiaEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for Asia 1 entries. 0 disables this session.", GroupName = "04. Asia 1", Order = 5)]
        public int AsiaContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Asia 1 trend filter.", GroupName = "04. Asia 1", Order = 10)]
        public int AsiaAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Asia 1 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "04. Asia 1", Order = 11)]
        public double AsiaAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Asia 1 entries are allowed only when ADX is less than or equal to this value.", GroupName = "04. Asia 1", Order = 12)]
        public double AsiaAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "04. Asia 1", Order = 14)]
        public double AsiaAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "04. Asia 1", Order = 15)]
        public double AsiaAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "04. Asia 1", Order = 16)]
        public double AsiaStopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "04. Asia 1", Order = 16)]
        public bool AsiaTrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "04. Asia 1", Order = 18)]
        public double AsiaTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "04. Asia 1", Order = 22)]
        public double AsiaEntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "04. Asia 1", Order = 21)]
        public InitialEntryType AsiaEntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "04. Asia 1", Order = 23)]
        public double AsiaEntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "04. Asia 1", Order = 24)]
        public bool AsiaColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "04. Asia 1", Order = 25)]
        public bool AsiaEnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "04. Asia 1", Order = 4)]
        public EntrySystemMode AsiaEntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in Asia 1. 0 disables secondary entries for this session.", GroupName = "04. Asia 1", Order = 39)]
        public int AsiaSecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "04. Asia 1", Order = 28)]
        public double AsiaSecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "04. Asia 1", Order = 29)]
        public double AsiaSecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "04. Asia 1", Order = 30)]
        public double AsiaSecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "04. Asia 1", Order = 31)]
        public double AsiaSecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "04. Asia 1", Order = 36)]
        public int AsiaCandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "04. Asia 1", Order = 37)]
        public double AsiaCandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "04. Asia 1", Order = 38)]
        public double AsiaCandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new Asia 1 entries while ATR(14) is below this value.", GroupName = "04. Asia 1", Order = 40)]
        public double AsiaAtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "Asia 2 session start time in chart time zone.", GroupName = "05. Asia 2", Order = 1)]
        public TimeSpan Asia2SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "Asia 2 session end time in chart time zone.", GroupName = "05. Asia 2", Order = 2)]
        public TimeSpan Asia2SessionEnd { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Asia 2 entry and exit logic.", GroupName = "05. Asia 2", Order = 4)]
        public int Asia2EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for Asia 2 entries. 0 disables this session.", GroupName = "05. Asia 2", Order = 5)]
        public int Asia2Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Asia 2 trend filter.", GroupName = "05. Asia 2", Order = 10)]
        public int Asia2AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Asia 2 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "05. Asia 2", Order = 11)]
        public double Asia2AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Asia 2 entries are allowed only when ADX is less than or equal to this value.", GroupName = "05. Asia 2", Order = 12)]
        public double Asia2AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "05. Asia 2", Order = 14)]
        public double Asia2AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "05. Asia 2", Order = 15)]
        public double Asia2AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "05. Asia 2", Order = 16)]
        public double Asia2StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "05. Asia 2", Order = 16)]
        public bool Asia2TrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "05. Asia 2", Order = 18)]
        public double Asia2TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "05. Asia 2", Order = 22)]
        public double Asia2EntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "05. Asia 2", Order = 21)]
        public InitialEntryType Asia2EntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "05. Asia 2", Order = 23)]
        public double Asia2EntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "05. Asia 2", Order = 24)]
        public bool Asia2ColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "05. Asia 2", Order = 25)]
        public bool Asia2EnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "05. Asia 2", Order = 4)]
        public EntrySystemMode Asia2EntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in Asia 2. 0 disables secondary entries for this session.", GroupName = "05. Asia 2", Order = 39)]
        public int Asia2SecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "05. Asia 2", Order = 28)]
        public double Asia2SecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "05. Asia 2", Order = 29)]
        public double Asia2SecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "05. Asia 2", Order = 30)]
        public double Asia2SecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "05. Asia 2", Order = 31)]
        public double Asia2SecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "05. Asia 2", Order = 36)]
        public int Asia2CandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "05. Asia 2", Order = 37)]
        public double Asia2CandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "05. Asia 2", Order = 38)]
        public double Asia2CandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new Asia 2 entries while ATR(14) is below this value.", GroupName = "05. Asia 2", Order = 40)]
        public double Asia2AtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "Asia 3 session start time in chart time zone.", GroupName = "06. Asia 3", Order = 1)]
        public TimeSpan Asia3SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "Asia 3 session end time in chart time zone.", GroupName = "06. Asia 3", Order = 2)]
        public TimeSpan Asia3SessionEnd { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Asia 3 entry and exit logic.", GroupName = "06. Asia 3", Order = 4)]
        public int Asia3EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for Asia 3 entries. 0 disables this session.", GroupName = "06. Asia 3", Order = 5)]
        public int Asia3Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Asia 3 trend filter.", GroupName = "06. Asia 3", Order = 10)]
        public int Asia3AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Asia 3 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "06. Asia 3", Order = 11)]
        public double Asia3AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Asia 3 entries are allowed only when ADX is less than or equal to this value.", GroupName = "06. Asia 3", Order = 12)]
        public double Asia3AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "06. Asia 3", Order = 14)]
        public double Asia3AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "06. Asia 3", Order = 15)]
        public double Asia3AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "06. Asia 3", Order = 16)]
        public double Asia3StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "06. Asia 3", Order = 16)]
        public bool Asia3TrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "06. Asia 3", Order = 18)]
        public double Asia3TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "06. Asia 3", Order = 22)]
        public double Asia3EntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "06. Asia 3", Order = 21)]
        public InitialEntryType Asia3EntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "06. Asia 3", Order = 23)]
        public double Asia3EntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "06. Asia 3", Order = 24)]
        public bool Asia3ColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "06. Asia 3", Order = 25)]
        public bool Asia3EnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "06. Asia 3", Order = 4)]
        public EntrySystemMode Asia3EntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in Asia 3. 0 disables secondary entries for this session.", GroupName = "06. Asia 3", Order = 39)]
        public int Asia3SecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "06. Asia 3", Order = 28)]
        public double Asia3SecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "06. Asia 3", Order = 29)]
        public double Asia3SecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "06. Asia 3", Order = 30)]
        public double Asia3SecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "06. Asia 3", Order = 31)]
        public double Asia3SecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "06. Asia 3", Order = 36)]
        public int Asia3CandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "06. Asia 3", Order = 37)]
        public double Asia3CandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "06. Asia 3", Order = 38)]
        public double Asia3CandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new Asia 3 entries while ATR(14) is below this value.", GroupName = "06. Asia 3", Order = 40)]
        public double Asia3AtrMinimum { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "Europe 1 session start time in chart time zone.", GroupName = "07. Europe 1", Order = 1)]
        public TimeSpan LondonSessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "Europe 1 session end time in chart time zone.", GroupName = "07. Europe 1", Order = 2)]
        public TimeSpan LondonSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Auto Shift", Description = "Apply Europe 1 DST auto-shift for this session window.", GroupName = "07. Europe 1", Order = 3)]
        public bool AutoShiftLondon { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Europe 1 entry and exit logic.", GroupName = "07. Europe 1", Order = 4)]
        public int LondonEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for Europe 1 entries. 0 disables this session.", GroupName = "07. Europe 1", Order = 5)]
        public int LondonContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Europe 1 trend filter.", GroupName = "07. Europe 1", Order = 10)]
        public int LondonAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Europe 1 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "07. Europe 1", Order = 11)]
        public double LondonAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Europe 1 entries are allowed only when ADX is less than or equal to this value.", GroupName = "07. Europe 1", Order = 12)]
        public double LondonAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "07. Europe 1", Order = 14)]
        public double LondonAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "07. Europe 1", Order = 15)]
        public double LondonAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "07. Europe 1", Order = 16)]
        public double LondonStopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "07. Europe 1", Order = 16)]
        public bool LondonTrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "07. Europe 1", Order = 18)]
        public double LondonTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "07. Europe 1", Order = 22)]
        public double LondonEntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "07. Europe 1", Order = 21)]
        public InitialEntryType LondonEntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "07. Europe 1", Order = 23)]
        public double LondonEntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "07. Europe 1", Order = 24)]
        public bool LondonColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "07. Europe 1", Order = 25)]
        public bool LondonEnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "07. Europe 1", Order = 4)]
        public EntrySystemMode LondonEntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in Europe 1. 0 disables secondary entries for this session.", GroupName = "07. Europe 1", Order = 39)]
        public int LondonSecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "07. Europe 1", Order = 28)]
        public double LondonSecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "07. Europe 1", Order = 29)]
        public double LondonSecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "07. Europe 1", Order = 30)]
        public double LondonSecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "07. Europe 1", Order = 31)]
        public double LondonSecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "07. Europe 1", Order = 36)]
        public int LondonCandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "07. Europe 1", Order = 37)]
        public double LondonCandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "07. Europe 1", Order = 38)]
        public double LondonCandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new Europe 1 entries while ATR(14) is below this value.", GroupName = "07. Europe 1", Order = 40)]
        public double LondonAtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "Europe 2 session start time in chart time zone.", GroupName = "08. Europe 2", Order = 1)]
        public TimeSpan London2SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "Europe 2 session end time in chart time zone.", GroupName = "08. Europe 2", Order = 2)]
        public TimeSpan London2SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Auto Shift", Description = "Apply Europe 2 DST auto-shift for this session window.", GroupName = "08. Europe 2", Order = 3)]
        public bool AutoShiftLondon2 { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Europe 2 entry and exit logic.", GroupName = "08. Europe 2", Order = 4)]
        public int London2EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for Europe 2 entries. 0 disables this session.", GroupName = "08. Europe 2", Order = 5)]
        public int London2Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Europe 2 trend filter.", GroupName = "08. Europe 2", Order = 10)]
        public int London2AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Europe 2 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "08. Europe 2", Order = 11)]
        public double London2AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Europe 2 entries are allowed only when ADX is less than or equal to this value.", GroupName = "08. Europe 2", Order = 12)]
        public double London2AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "08. Europe 2", Order = 14)]
        public double London2AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "08. Europe 2", Order = 15)]
        public double London2AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "08. Europe 2", Order = 16)]
        public double London2StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "08. Europe 2", Order = 16)]
        public bool London2TrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "08. Europe 2", Order = 18)]
        public double London2TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "08. Europe 2", Order = 22)]
        public double London2EntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "08. Europe 2", Order = 21)]
        public InitialEntryType London2EntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "08. Europe 2", Order = 23)]
        public double London2EntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "08. Europe 2", Order = 24)]
        public bool London2ColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "08. Europe 2", Order = 25)]
        public bool London2EnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "08. Europe 2", Order = 4)]
        public EntrySystemMode London2EntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in Europe 2. 0 disables secondary entries for this session.", GroupName = "08. Europe 2", Order = 39)]
        public int London2SecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "08. Europe 2", Order = 28)]
        public double London2SecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "08. Europe 2", Order = 29)]
        public double London2SecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "08. Europe 2", Order = 30)]
        public double London2SecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "08. Europe 2", Order = 31)]
        public double London2SecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "08. Europe 2", Order = 36)]
        public int London2CandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "08. Europe 2", Order = 37)]
        public double London2CandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "08. Europe 2", Order = 38)]
        public double London2CandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new Europe 2 entries while ATR(14) is below this value.", GroupName = "08. Europe 2", Order = 40)]
        public double London2AtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "Europe 3 session start time in chart time zone.", GroupName = "09. Europe 3", Order = 1)]
        public TimeSpan London3SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "Europe 3 session end time in chart time zone.", GroupName = "09. Europe 3", Order = 2)]
        public TimeSpan London3SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Auto Shift", Description = "Apply Europe 3 DST auto-shift for this session window.", GroupName = "09. Europe 3", Order = 3)]
        public bool AutoShiftLondon3 { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Flat By Time", Description = "Optional fixed chart time to flatten any open Europe 3 trade. Leave blank to disable. This does not shift with Auto Shift.", GroupName = "09. Europe 3", Order = 4)]
        public string London3FlatByTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Europe 3 entry and exit logic.", GroupName = "09. Europe 3", Order = 4)]
        public int London3EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for Europe 3 entries. 0 disables this session.", GroupName = "09. Europe 3", Order = 5)]
        public int London3Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Europe 3 trend filter.", GroupName = "09. Europe 3", Order = 10)]
        public int London3AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Europe 3 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "09. Europe 3", Order = 11)]
        public double London3AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Europe 3 entries are allowed only when ADX is less than or equal to this value.", GroupName = "09. Europe 3", Order = 12)]
        public double London3AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "09. Europe 3", Order = 14)]
        public double London3AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "09. Europe 3", Order = 15)]
        public double London3AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "09. Europe 3", Order = 16)]
        public double London3StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "09. Europe 3", Order = 16)]
        public bool London3TrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "09. Europe 3", Order = 18)]
        public double London3TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "09. Europe 3", Order = 22)]
        public double London3EntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "09. Europe 3", Order = 21)]
        public InitialEntryType London3EntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "09. Europe 3", Order = 23)]
        public double London3EntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "09. Europe 3", Order = 24)]
        public bool London3ColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "09. Europe 3", Order = 25)]
        public bool London3EnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "09. Europe 3", Order = 4)]
        public EntrySystemMode London3EntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in Europe 3. 0 disables secondary entries for this session.", GroupName = "09. Europe 3", Order = 39)]
        public int London3SecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "09. Europe 3", Order = 28)]
        public double London3SecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "09. Europe 3", Order = 29)]
        public double London3SecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "09. Europe 3", Order = 30)]
        public double London3SecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "09. Europe 3", Order = 31)]
        public double London3SecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "09. Europe 3", Order = 36)]
        public int London3CandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "09. Europe 3", Order = 37)]
        public double London3CandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "09. Europe 3", Order = 38)]
        public double London3CandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new Europe 3 entries while ATR(14) is below this value.", GroupName = "09. Europe 3", Order = 40)]
        public double London3AtrMinimum { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "America 1 session start time in chart time zone.", GroupName = "10. America 1", Order = 1)]
        public TimeSpan NewYorkSessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "America 1 session end time in chart time zone.", GroupName = "10. America 1", Order = 2)]
        public TimeSpan NewYorkSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by America 1 entry and exit logic.", GroupName = "10. America 1", Order = 5)]
        public int NewYorkEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for America 1 entries. 0 disables this session.", GroupName = "10. America 1", Order = 5)]
        public int NewYorkContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the America 1 trend filter.", GroupName = "10. America 1", Order = 11)]
        public int NewYorkAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. America 1 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "10. America 1", Order = 12)]
        public double NewYorkAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. America 1 entries are allowed only when ADX is less than or equal to this value.", GroupName = "10. America 1", Order = 13)]
        public double NewYorkAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "10. America 1", Order = 15)]
        public double NewYorkAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "10. America 1", Order = 16)]
        public double NewYorkAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "10. America 1", Order = 17)]
        public double NewYorkStopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "10. America 1", Order = 17)]
        public bool NewYorkTrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "10. America 1", Order = 19)]
        public double NewYorkTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "10. America 1", Order = 22)]
        public double NewYorkEntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "10. America 1", Order = 21)]
        public InitialEntryType NewYorkEntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "10. America 1", Order = 23)]
        public double NewYorkEntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "10. America 1", Order = 24)]
        public bool NewYorkColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "10. America 1", Order = 25)]
        public bool NewYorkEnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "10. America 1", Order = 4)]
        public EntrySystemMode NewYorkEntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in America 1. 0 disables secondary entries for this session.", GroupName = "10. America 1", Order = 40)]
        public int NewYorkSecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "10. America 1", Order = 28)]
        public double NewYorkSecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "10. America 1", Order = 29)]
        public double NewYorkSecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "10. America 1", Order = 30)]
        public double NewYorkSecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "10. America 1", Order = 31)]
        public double NewYorkSecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "10. America 1", Order = 37)]
        public int NewYorkCandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "10. America 1", Order = 38)]
        public double NewYorkCandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "10. America 1", Order = 39)]
        public double NewYorkCandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new America 1 entries while ATR(14) is below this value.", GroupName = "10. America 1", Order = 41)]
        public double NewYorkAtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "America 2 session start time in chart time zone.", GroupName = "11. America 2", Order = 1)]
        public TimeSpan NewYork2SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "America 2 session end time in chart time zone.", GroupName = "11. America 2", Order = 2)]
        public TimeSpan NewYork2SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by America 2 entry and exit logic.", GroupName = "11. America 2", Order = 5)]
        public int NewYork2EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for America 2 entries. 0 disables this session.", GroupName = "11. America 2", Order = 5)]
        public int NewYork2Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the America 2 trend filter.", GroupName = "11. America 2", Order = 11)]
        public int NewYork2AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. America 2 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "11. America 2", Order = 12)]
        public double NewYork2AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. America 2 entries are allowed only when ADX is less than or equal to this value.", GroupName = "11. America 2", Order = 13)]
        public double NewYork2AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "11. America 2", Order = 15)]
        public double NewYork2AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "11. America 2", Order = 16)]
        public double NewYork2AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "11. America 2", Order = 17)]
        public double NewYork2StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "11. America 2", Order = 17)]
        public bool NewYork2TrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "11. America 2", Order = 19)]
        public double NewYork2TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "11. America 2", Order = 22)]
        public double NewYork2EntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "11. America 2", Order = 21)]
        public InitialEntryType NewYork2EntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "11. America 2", Order = 23)]
        public double NewYork2EntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "11. America 2", Order = 24)]
        public bool NewYork2ColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "11. America 2", Order = 25)]
        public bool NewYork2EnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "11. America 2", Order = 4)]
        public EntrySystemMode NewYork2EntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in America 2. 0 disables secondary entries for this session.", GroupName = "11. America 2", Order = 40)]
        public int NewYork2SecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "11. America 2", Order = 28)]
        public double NewYork2SecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "11. America 2", Order = 29)]
        public double NewYork2SecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "11. America 2", Order = 30)]
        public double NewYork2SecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "11. America 2", Order = 31)]
        public double NewYork2SecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "11. America 2", Order = 37)]
        public int NewYork2CandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "11. America 2", Order = 38)]
        public double NewYork2CandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "11. America 2", Order = 39)]
        public double NewYork2CandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new America 2 entries while ATR(14) is below this value.", GroupName = "11. America 2", Order = 41)]
        public double NewYork2AtrMinimum { get; set; }


        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "America 3 session start time in chart time zone.", GroupName = "12. America 3", Order = 1)]
        public TimeSpan NewYork3SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "America 3 session end time in chart time zone.", GroupName = "12. America 3", Order = 2)]
        public TimeSpan NewYork3SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by America 3 entry and exit logic.", GroupName = "12. America 3", Order = 5)]
        public int NewYork3EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for America 3 entries. 0 disables this session.", GroupName = "12. America 3", Order = 5)]
        public int NewYork3Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the America 3 trend filter.", GroupName = "12. America 3", Order = 11)]
        public int NewYork3AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. America 3 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "12. America 3", Order = 12)]
        public double NewYork3AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. America 3 entries are allowed only when ADX is less than or equal to this value.", GroupName = "12. America 3", Order = 13)]
        public double NewYork3AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "12. America 3", Order = 15)]
        public double NewYork3AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "12. America 3", Order = 16)]
        public double NewYork3AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "12. America 3", Order = 17)]
        public double NewYork3StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "12. America 3", Order = 17)]
        public bool NewYork3TrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "12. America 3", Order = 19)]
        public double NewYork3TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "12. America 3", Order = 22)]
        public double NewYork3EntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "12. America 3", Order = 21)]
        public InitialEntryType NewYork3EntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "12. America 3", Order = 23)]
        public double NewYork3EntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "12. America 3", Order = 24)]
        public bool NewYork3ColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "12. America 3", Order = 25)]
        public bool NewYork3EnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "12. America 3", Order = 4)]
        public EntrySystemMode NewYork3EntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in America 3. 0 disables secondary entries for this session.", GroupName = "12. America 3", Order = 40)]
        public int NewYork3SecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "12. America 3", Order = 28)]
        public double NewYork3SecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "12. America 3", Order = 29)]
        public double NewYork3SecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "12. America 3", Order = 30)]
        public double NewYork3SecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "12. America 3", Order = 31)]
        public double NewYork3SecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "12. America 3", Order = 37)]
        public int NewYork3CandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "12. America 3", Order = 38)]
        public double NewYork3CandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "12. America 3", Order = 39)]
        public double NewYork3CandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new America 3 entries while ATR(14) is below this value.", GroupName = "12. America 3", Order = 41)]
        public double NewYork3AtrMinimum { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "America 4 session start time in chart time zone.", GroupName = "13. America 4", Order = 1)]
        public TimeSpan NewYork4SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "America 4 session end time in chart time zone.", GroupName = "13. America 4", Order = 2)]
        public TimeSpan NewYork4SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by America 4 entry and exit logic.", GroupName = "13. America 4", Order = 5)]
        public int NewYork4EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for America 4 entries. 0 disables this session.", GroupName = "13. America 4", Order = 5)]
        public int NewYork4Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the America 4 trend filter.", GroupName = "13. America 4", Order = 11)]
        public int NewYork4AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. America 4 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "13. America 4", Order = 12)]
        public double NewYork4AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. America 4 entries are allowed only when ADX is less than or equal to this value.", GroupName = "13. America 4", Order = 13)]
        public double NewYork4AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "13. America 4", Order = 15)]
        public double NewYork4AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "13. America 4", Order = 16)]
        public double NewYork4AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "13. America 4", Order = 17)]
        public double NewYork4StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "13. America 4", Order = 17)]
        public bool NewYork4TrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "13. America 4", Order = 19)]
        public double NewYork4TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "13. America 4", Order = 22)]
        public double NewYork4EntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "13. America 4", Order = 21)]
        public InitialEntryType NewYork4EntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "13. America 4", Order = 23)]
        public double NewYork4EntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "13. America 4", Order = 24)]
        public bool NewYork4ColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "13. America 4", Order = 25)]
        public bool NewYork4EnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "13. America 4", Order = 4)]
        public EntrySystemMode NewYork4EntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in America 4. 0 disables secondary entries for this session.", GroupName = "13. America 4", Order = 40)]
        public int NewYork4SecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "13. America 4", Order = 28)]
        public double NewYork4SecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "13. America 4", Order = 29)]
        public double NewYork4SecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "13. America 4", Order = 30)]
        public double NewYork4SecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "13. America 4", Order = 31)]
        public double NewYork4SecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "13. America 4", Order = 37)]
        public int NewYork4CandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "13. America 4", Order = 38)]
        public double NewYork4CandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "13. America 4", Order = 39)]
        public double NewYork4CandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new America 4 entries while ATR(14) is below this value.", GroupName = "13. America 4", Order = 41)]
        public double NewYork4AtrMinimum { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session Start", Description = "America 5 session start time in chart time zone.", GroupName = "14. America 5", Order = 1)]
        public TimeSpan NewYork5SessionStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Session End", Description = "America 5 session end time in chart time zone.", GroupName = "14. America 5", Order = 2)]
        public TimeSpan NewYork5SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by America 5 entry and exit logic.", GroupName = "14. America 5", Order = 5)]
        public int NewYork5EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Primary Contracts", Description = "Base contracts for America 5 entries. 0 disables this session.", GroupName = "14. America 5", Order = 5)]
        public int NewYork5Contracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the America 5 trend filter.", GroupName = "14. America 5", Order = 11)]
        public int NewYork5AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. America 5 entries are allowed only when ADX is greater than or equal to this value.", GroupName = "14. America 5", Order = 12)]
        public double NewYork5AdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. America 5 entries are allowed only when ADX is less than or equal to this value.", GroupName = "14. America 5", Order = 13)]
        public double NewYork5AdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "14. America 5", Order = 15)]
        public double NewYork5AdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "14. America 5", Order = 16)]
        public double NewYork5AdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Stop distance in points from EMA on the opposite side.", GroupName = "14. America 5", Order = 17)]
        public double NewYork5StopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Trail Hard SL", Description = "If enabled, move the hard stop each bar close using EMA plus SL Padding Points. The stop only tightens.", GroupName = "14. America 5", Order = 17)]
        public bool NewYork5TrailHardStop { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "14. America 5", Order = 19)]
        public double NewYork5TakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Min Body Points", Description = "0 disables. Initial entry signal candle must have at least this body size in points.", GroupName = "14. America 5", Order = 22)]
        public double NewYork5EntryMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Type", Description = "Market enters immediately on a valid signal. EmaLimit places and updates a limit order at EMA21 while normal entry conditions remain valid.", GroupName = "14. America 5", Order = 21)]
        public InitialEntryType NewYork5EntryType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Close Beyond EMA Points", Description = "0 disables. Long entries require the close at least this many points above EMA; short entries require the close at least this many points below EMA.", GroupName = "14. America 5", Order = 23)]
        public double NewYork5EntryCloseBeyondEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Color Blind Entry", Description = "If enabled, long entries only require close above EMA and short entries only require close below EMA. If disabled, entries also require candle color and body crossing EMA.", GroupName = "14. America 5", Order = 24)]
        public bool NewYork5ColorBlindEntry { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Stop-Out Flip", Description = "When enabled, a stop-loss fill can reverse into the opposite direction when the opposite setup still passes normal entry gates.", GroupName = "14. America 5", Order = 25)]
        public bool NewYork5EnableStopOutFlip { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Entry System", Description = "Primary uses the normal DUOrc entry. Secondary opens an EMA-based working limit order after the normal signal. Both enables both systems.", GroupName = "14. America 5", Order = 4)]
        public EntrySystemMode NewYork5EntrySystemMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Contracts", Description = "Contracts used for secondary entries in America 5. 0 disables secondary entries for this session.", GroupName = "14. America 5", Order = 40)]
        public int NewYork5SecondaryContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary Entry From EMA", Description = "Distance in points from EMA for the secondary limit entry. Long entries use EMA plus this value; short entries use EMA minus this value.", GroupName = "14. America 5", Order = 28)]
        public double NewYork5SecondaryEntryLimitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary SL From EMA", Description = "Distance in points from EMA for the secondary stop loss. Long stops use EMA minus this value; short stops use EMA plus this value.", GroupName = "14. America 5", Order = 29)]
        public double NewYork5SecondaryEntryStopDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP From EMA", Description = "Distance in points from EMA for the secondary profit target. Long targets use EMA plus this value; short targets use EMA minus this value.", GroupName = "14. America 5", Order = 30)]
        public double NewYork5SecondaryEntryTakeProfitDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Secondary TP Variance", Description = "Maximum random extra profit-target distance in points. Variance applies only to TP, not the secondary entry or stop.", GroupName = "14. America 5", Order = 31)]
        public double NewYork5SecondaryTakeProfitVariancePoints { get; set; }
        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Candle Reversal Exit Bars", Description = "0 disables. After this many bars held, short exits on bullish close above the most recent bearish candle high; long exits on bearish close below the most recent bullish candle low.", GroupName = "14. America 5", Order = 37)]
        public int NewYork5CandleReversalExitBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Close Beyond Points", Description = "0 uses the candle high/low exactly. Long exits require a close this many points below the reference bullish candle low; short exits require this many points above the reference bearish candle high.", GroupName = "14. America 5", Order = 38)]
        public double NewYork5CandleReversalCloseBeyondPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Candle Reversal Min Body Points", Description = "0 disables. Reference bullish/bearish candles must have at least this body size in points to count for the candle reversal exit.", GroupName = "14. America 5", Order = 39)]
        public double NewYork5CandleReversalMinBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ATR Min Threshold", Description = "0 disables. Block new America 5 entries while ATR(14) is below this value.", GroupName = "14. America 5", Order = 41)]
        public double NewYork5AtrMinimum { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Close At Session End", Description = "If true, flatten positions and cancel entries at each configured session end.", GroupName = "99. Internal", Order = 0)]
        public bool CloseAtSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Force Close Time", Description = "Optional. Leave empty to disable. Enter as HH:mm:ss using the 5-minute bar timestamp, for example 04:55:00 to flatten on the 05:00 bar close. After this time, cancel working entries, flatten any open position, and block new trades for the rest of the trading day.", GroupName = "01. Risk", Order = 1)]
        public string ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [XmlIgnore]
        [Display(Name = "Asia Session Fill", Description = "Background color used to highlight Asia session windows.", GroupName = "99. Internal", Order = 2)]
        public Brush AsiaSessionBrush { get; set; }

        [Browsable(false)]
        public string AsiaSessionBrushSerializable
        {
            get { return Serialize.BrushToString(AsiaSessionBrush); }
            set { AsiaSessionBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [XmlIgnore]
        [Display(Name = "Europe Session Fill", Description = "Background color used to highlight Europe session windows.", GroupName = "99. Internal", Order = 3)]
        public Brush LondonSessionBrush { get; set; }

        [Browsable(false)]
        public string LondonSessionBrushSerializable
        {
            get { return Serialize.BrushToString(LondonSessionBrush); }
            set { LondonSessionBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [XmlIgnore]
        [Display(Name = "America Session Fill", Description = "Background color used to highlight America session windows.", GroupName = "99. Internal", Order = 4)]
        public Brush NewYorkSessionBrush { get; set; }

        [Browsable(false)]
        public string NewYorkSessionBrushSerializable
        {
            get { return Serialize.BrushToString(NewYorkSessionBrush); }
            set { NewYorkSessionBrush = Serialize.StringToBrush(value); }
        }
        internal bool ShowEmaOnChart { get; set; }
        internal bool ShowAdxOnChart { get; set; }
        internal bool ShowAdxThresholdLines { get; set; }
        internal bool ShowAtrOnChart { get; set; }
        internal bool ShowAtrThresholdLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Description = "Block entries inside the configured minutes before and after listed news events.", GroupName = "02. News", Order = 0)]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Block Minutes", Description = "Minutes blocked before and after each news timestamp.", GroupName = "02. News", Order = 1)]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL", Description = "HTTP endpoint for TradersPost order webhooks. Leave empty to disable TradersPost webhooks.", GroupName = "03. Webhooks", Order = 0)]
        public string WebhookUrl
        {
            get { return webhookUrl ?? string.Empty; }
            set { webhookUrl = value ?? string.Empty; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Ticker Override", Description = "Optional TradersPost ticker/instrument name override. Leave empty to use the chart instrument automatically.", GroupName = "03. Webhooks", Order = 1)]
        public string WebhookTickerOverride
        {
            get { return webhookTickerOverride ?? string.Empty; }
            set { webhookTickerOverride = value ?? string.Empty; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Provider", Description = "Select webhook target: TradersPost or ProjectX.", GroupName = "03. Webhooks", Order = 2)]
        public WebhookProvider WebhookProviderType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Base URL", Description = "ProjectX gateway base URL. Leave the default ProjectX gateway URL or paste your firm-specific endpoint.", GroupName = "03. Webhooks", Order = 3)]
        public string ProjectXApiBaseUrl { get; set; }

        [Browsable(false)]
        public bool ProjectXTradeAllAccounts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Username", Description = "ProjectX login username for direct ProjectX order routing.", GroupName = "03. Webhooks", Order = 5)]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX API Key", Description = "ProjectX API key used together with the ProjectX username.", GroupName = "03. Webhooks", Order = 6)]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Accounts", Description = "Comma-separated ProjectX account ids or exact account names.", GroupName = "03. Webhooks", Order = 7)]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Contract ID", Description = "Hidden optional override for support/debug use only.", GroupName = "03. Webhooks", Order = 8)]
        public string ProjectXContractId { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Account Balance", Description = "When net liquidation reaches or exceeds this value, entries are blocked and open positions are flattened. 0 disables.", GroupName = "01. Risk", Order = 0)]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Confirmation", Description = "Show a Yes/No confirmation popup before each new long/short entry.", GroupName = "01. Risk", Order = 2)]
        public bool RequireEntryConfirmation { get; set; }


        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Secondary Bias EMA Period", Description = "0 disables. When enabled, price above this EMA allows only longs; price below this EMA allows only shorts.", GroupName = "01. Risk", Order = 5)]
        public int SecondaryBiasEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Secondary Trail Stop", Description = "If enabled, secondary entries trail their stop every 5-minute close using the original EMA stop distance. The stop only tightens.", GroupName = "01. Risk", Order = 4)]
        public bool SecondaryTrailStop { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Variance", Description = "If enabled, delay qualifying new realtime market entries by a random 1-10 seconds after the 5-minute close.", GroupName = "01. Risk", Order = 3)]
        public bool EntryVariance { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Debug Logging", Description = "Print concise decision, order, and execution diagnostics to Output.", GroupName = "99. Debug", Order = 999)]
        public bool DebugLogging { get; set; }
    }
}
