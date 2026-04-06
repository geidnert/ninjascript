#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class HUGOTesting : Strategy
    {
        public HUGOTesting()
        {
            VendorLicense(1346);
        }
        private const string StrategySignalPrefix = "HUGOTesting";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";
        private const string HeartbeatStrategyName = "HUGOTesting";
        private const int RequiredPrimaryTimeframeMinutes = 15;

        #region Private Variables
        // EMA instances — one per bucket (may share same length but kept separate for flexibility)
        private EMA emaL1, emaL2, emaS1, emaS2;
        // We also need a "detection" EMA to spot crossovers — use the shortest of the four
        // Actually, crossover detection needs a consistent EMA. We'll use all four and check per-direction.
        // For long signals: we need to detect Close crosses above EMA. Which EMA? 
        // The crossover is direction-specific, so longs use the long EMAs and shorts use the short EMAs.
        // But we have TWO long EMAs (L1, L2) — the bucket is determined AFTER the crossover.
        // Solution: detect crossover using BOTH long EMAs — if close crosses above EITHER, it's a long signal.
        // Then measure candle size to pick bucket.
        // HOWEVER: the original design uses a single EMA for crossover detection.
        // With per-bucket EMAs, we need a strategy: use the LONGER of the two for crossover detection
        // per direction, or require crossover on the SPECIFIC bucket's EMA.
        // Simplest correct approach: detect crossover on EACH bucket's EMA independently.
        // A long signal fires for L1 if close crosses above emaL1, and for L2 if close crosses above emaL2.
        // Then candle size determines which bucket actually takes the trade.
        // If both L1 and L2 EMAs are crossed on the same bar, the candle size picks the right one.
        // This is the cleanest approach.

        // Signal tracking
        private bool longSignalActive;
        private bool shortSignalActive;
        private int signalBar;
        private double crossoverCandleHigh;
        private double crossoverCandleLow;
        private double signalEmaValue;

        // Filter wait tracking
        private bool pendingLongFilterWait;
        private bool pendingShortFilterWait;
        private int filterWaitStartBar;
        private string pendingFilterBucket; // which bucket is waiting

        // Order tracking
        private Order entryOrder;
        private string managedEntrySignal = string.Empty;
        private int entrySignalSequence;

        // Global Daily P&L tracking
        private double globalDailyPnL;
        private DateTime lastTradeDate;
        private DateTime currentSessionDate;   // v1.10: session-anchored date (handles midnight crossing)
        private bool globalDailyLossLimitHit;
        private bool globalDailyProfitLimitHit;
        private int globalDailyTradeCount;

        // Per-bucket daily P&L tracking
        private double dailyPnL_L1, dailyPnL_L2, dailyPnL_S1, dailyPnL_S2;
        private int dailyTradeCount_L1, dailyTradeCount_L2, dailyTradeCount_S1, dailyTradeCount_S2;
        private bool dailyLossHit_L1, dailyLossHit_L2, dailyLossHit_S1, dailyLossHit_S2;
        private bool dailyProfitHit_L1, dailyProfitHit_L2, dailyProfitHit_S1, dailyProfitHit_S2;

        // Position tracking
        private double entryPrice;
        private double currentStopPrice;
        private double currentTargetPrice;
        private bool breakEvenMoved;
        private int currentTradeDirection;
        private string activeBucket; // which bucket owns the current position

        // Pending direction
        private int pendingDirection;

        // Direction flip tracking (global)
        private int lastTradeDirection;
        private double lastTradePnL;

        // v1.03: Price-Trailing Stop state
        private double bestPriceSinceEntry;
        private bool trailActivated;
        private bool trailLocked;
        private double initialStopDistance;
        private double initialTPDistance;

        // v1.06: Max Bars in Trade
        private int entryBar;

        // v1.06: SL to Entry on EMA Cross
        private bool slMovedToEntryOnEMA;

        // v1.06: Skip Time Window
        private bool inSkipWindow;
        private bool inNewsSkipWindow;
        private bool maxAccountLimitHit;
        private bool isConfiguredTimeframeValid = true;
        private bool isConfiguredInstrumentValid = true;
        private bool timeframePopupShown;
        private bool instrumentPopupShown;
        // v1.07: WMA / SMA / ADX indicators — one per bucket
        private WMA wmaL1, wmaL2, wmaS1, wmaS2;
        private SMA smaL1, smaL2, smaS1, smaS2;
        private ADX adxL1, adxL2, adxS1, adxS2;
        private StrategyHeartbeatReporter heartbeatReporter;

        // Info box overlay
        private Border infoBoxContainer;
        private StackPanel infoBoxRowsPanel;
        private static readonly Brush InfoHeaderFooterGradientBrush = CreateFrozenVerticalGradientBrush(
            Color.FromArgb(240, 0x2A, 0x2F, 0x45),
            Color.FromArgb(240, 0x1E, 0x23, 0x36),
            Color.FromArgb(240, 0x14, 0x18, 0x28));
        private static readonly Brush InfoBodyOddBrush = CreateFrozenBrush(240, 0x0F, 0x0F, 0x17);
        private static readonly Brush InfoBodyEvenBrush = CreateFrozenBrush(240, 0x11, 0x11, 0x18);
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(255, 0x8B, 0x45, 0x13);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);
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


        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  COMMON PARAMETERS
        // ════════════════════════════════════════════════════════════════════════

        #region Common Parameters

        // ── Time Settings ─────────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Session Start Time", Description = "Midnight-crossing anchor. Resets daily counters at this time. Set to the true start of the trading session (e.g. 18:00 for NQ).", Order = 1, GroupName = "0. Common - Time Settings")]
        public string SessionStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Window Start", Description = "Earliest time at which new entries are allowed. Can be later than Session Start Time.", Order = 2, GroupName = "0. Common - Time Settings")]
        public string TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Last Entry Time", Order = 3, GroupName = "0. Common - Time Settings")]
        public string LastEntryTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Force Close Time", Order = 4, GroupName = "0. Common - Time Settings")]
        public string ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End Time", Order = 5, GroupName = "0. Common - Time Settings")]
        public string SessionEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Skip Time Window", Order = 6, GroupName = "0. Common - Time Settings")]
        public bool EnableSkipTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Time Start", Order = 7, GroupName = "0. Common - Time Settings")]
        public string SkipTimeStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Time End", Order = 8, GroupName = "0. Common - Time Settings")]
        public string SkipTimeEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten Position at Skip Start", Order = 9, GroupName = "0. Common - Time Settings")]
        public bool FlattenAtSkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Order = 10, GroupName = "0. Common - Time Settings")]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "News Time", Order = 11, GroupName = "0. Common - Time Settings")]
        [Browsable(false)]
        public string NewsTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Block Minutes", Order = 12, GroupName = "0. Common - Time Settings")]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten Position at News Start", Order = 13, GroupName = "0. Common - Time Settings")]
        public bool FlattenAtNewsStart { get; set; }

        // ── Contract Size ─────────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contract Quantity", Order = 14, GroupName = "0. Common - Time Settings")]
        public int ContractQuantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Confirmation", Order = 1, GroupName = "0. Common - Execution")]
        public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL", Order = 1, GroupName = "0. Common - Webhooks")]
        public string WebhookUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Ticker Override", Order = 2, GroupName = "0. Common - Webhooks")]
        public string WebhookTickerOverride { get; set; }

        // ── Global Risk Management ────────────────────────────────────────────
        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Account Balance", Description = "When net liquidation reaches or exceeds this value, entries are blocked and open positions are flattened. 0 disables.", Order = 0, GroupName = "0. Common - Global Risk")]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Global Max Daily Loss", Order = 1, GroupName = "0. Common - Global Risk")]
        public bool EnableGlobalMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Global Max Daily Loss (Points)", Order = 2, GroupName = "0. Common - Global Risk")]
        public double GlobalMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Global Max Daily Profit", Order = 3, GroupName = "0. Common - Global Risk")]
        public bool EnableGlobalMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Global Max Daily Profit (Points)", Order = 4, GroupName = "0. Common - Global Risk")]
        public double GlobalMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Global Max Trades Per Day", Order = 5, GroupName = "0. Common - Global Risk")]
        public bool EnableGlobalMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Global Max Trades Per Day", Order = 6, GroupName = "0. Common - Global Risk")]
        public int GlobalMaxTradesPerDay { get; set; }

        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  L1: LONG — SMALL CANDLE
        // ════════════════════════════════════════════════════════════════════════

        #region L1 Parameters

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable", Description = "Enable Long Small Candle bucket", Order = 0, GroupName = "L1.0 Bucket Settings")]
        public bool L1_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Min Candle Range (Points)", Description = "Minimum signal candle range for this bucket", Order = 1, GroupName = "L1.0 Bucket Settings")]
        public double L1_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Max Candle Range (Points)", Description = "Maximum signal candle range for this bucket (0 = no max)", Order = 2, GroupName = "L1.0 Bucket Settings")]
        public double L1_MaxCandleRange { get; set; }

        // EMA
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "L1 EMA Length", Order = 1, GroupName = "L1.1 EMA Settings")]
        public int L1_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable EMA Slope Filter", Order = 2, GroupName = "L1.1 EMA Settings")]
        public bool L1_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 EMA Slope Mode", Order = 3, GroupName = "L1.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum L1_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "L1 EMA Slope Bars", Order = 4, GroupName = "L1.1 EMA Settings")]
        public int L1_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 EMA Min Slope %", Order = 5, GroupName = "L1.1 EMA Settings")]
        public double L1_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty][Display(Name = "L1 Enable WMA Filter", Order = 1, GroupName = "L1.1b WMA Filter")]
        public bool L1_EnableWMAFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L1 WMA Length", Order = 2, GroupName = "L1.1b WMA Filter")]
        public int L1_WMALength { get; set; }

        [NinjaScriptProperty][Display(Name = "L1 Enable SMA Filter", Order = 1, GroupName = "L1.1c SMA Filter")]
        public bool L1_EnableSMAFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L1 SMA Length", Order = 2, GroupName = "L1.1c SMA Filter")]
        public int L1_SMALength { get; set; }

        [NinjaScriptProperty][Display(Name = "L1 Enable ADX Filter", Order = 1, GroupName = "L1.1d ADX Filter")]
        public bool L1_EnableADXFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L1 ADX Period", Order = 2, GroupName = "L1.1d ADX Filter")]
        public int L1_ADXPeriod { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L1 ADX Min Value", Order = 3, GroupName = "L1.1d ADX Filter")]
        public double L1_ADXMin { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L1 ADX Max Value", Order = 4, GroupName = "L1.1d ADX Filter")]
        public double L1_ADXMax { get; set; }

        // Entry
        [NinjaScriptProperty]
        [Display(Name = "L1 Entry Type", Order = 1, GroupName = "L1.2 Entry Settings")]
        public HugoTesting_EntryTypeEnum L1_EntryType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Pullback Offset (Points)", Order = 2, GroupName = "L1.2 Entry Settings")]
        public double L1_PullbackOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "L1 Max Bars to Wait", Order = 3, GroupName = "L1.2 Entry Settings")]
        public int L1_MaxBarsToWait { get; set; }

        // Stop Loss
        [NinjaScriptProperty]
        [Display(Name = "L1 Stop Loss Type", Order = 1, GroupName = "L1.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum L1_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Fixed Stop Loss (Points)", Order = 2, GroupName = "L1.3 Stop Loss")]
        public double L1_FixedStopLoss { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "L1 SL Candle Multiplier", Order = 2, GroupName = "L1.3 Stop Loss")]
        public double L1_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Stop Loss Offset (Points)", Order = 3, GroupName = "L1.3 Stop Loss")]
        public double L1_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Maximum Stop Loss (Points)", Order = 4, GroupName = "L1.3 Stop Loss")]
        public double L1_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Max Bars In Trade", Order = 5, GroupName = "L1.3 Stop Loss")]
        public bool L1_EnableMaxBarsInTrade { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "L1 Max Bars In Trade", Order = 6, GroupName = "L1.3 Stop Loss")]
        public int L1_MaxBarsInTrade { get; set; }

        // Take Profit
        [NinjaScriptProperty]
        [Display(Name = "L1 Take Profit Type", Order = 1, GroupName = "L1.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum L1_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "L1 Risk Reward Ratio", Order = 2, GroupName = "L1.4 Take Profit")]
        public double L1_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Fixed Take Profit (Points)", Order = 3, GroupName = "L1.4 Take Profit")]
        public double L1_FixedTakeProfit { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "L1 TP Candle Multiplier", Order = 4, GroupName = "L1.4 Take Profit")]
        public double L1_TPCandleMultiplier { get; set; }

        // Break Even
        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Break Even", Order = 1, GroupName = "L1.5 Break Even")]
        public bool L1_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Break Even Trigger Type", Order = 2, GroupName = "L1.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum L1_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Break Even Trigger Value", Order = 3, GroupName = "L1.5 Break Even")]
        public double L1_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Break Even Offset (Points)", Order = 4, GroupName = "L1.5 Break Even")]
        public double L1_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable SL to Entry on EMA Cross", Order = 5, GroupName = "L1.5 Break Even")]
        public bool L1_EnableSLToEntryOnEMA { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 SL to Entry Offset (Points)", Order = 6, GroupName = "L1.5 Break Even")]
        public double L1_SLToEntryOffset { get; set; }

        // Trailing Stop
        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Price Trail Stop", Order = 1, GroupName = "L1.6 Trailing Stop")]
        public bool L1_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Trail Distance Mode", Order = 2, GroupName = "L1.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum L1_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "L1 Trail Distance Value", Order = 3, GroupName = "L1.6 Trailing Stop")]
        public double L1_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Trail Activation Mode", Order = 4, GroupName = "L1.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum L1_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Trail Activation Value", Order = 5, GroupName = "L1.6 Trailing Stop")]
        public double L1_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "L1 Trail Step (Ticks)", Order = 6, GroupName = "L1.6 Trailing Stop")]
        public int L1_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Trail Lock", Order = 7, GroupName = "L1.6 Trailing Stop")]
        public bool L1_EnableTrailLock { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Trail Lock Value", Order = 8, GroupName = "L1.6 Trailing Stop")]
        public double L1_TrailLockValue { get; set; }

        // Signal Quality Filters
        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Candle Size Filter", Order = 1, GroupName = "L1.7 Signal Filters")]
        public bool L1_EnableCandleSizeFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Min Candle Size (Points)", Order = 2, GroupName = "L1.7 Signal Filters")]
        public double L1_MinCandleSize { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Max Candle Size (Points)", Order = 3, GroupName = "L1.7 Signal Filters")]
        public double L1_MaxCandleSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Body % Filter", Order = 4, GroupName = "L1.7 Signal Filters")]
        public bool L1_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "L1 Min Body % of Range", Order = 5, GroupName = "L1.7 Signal Filters")]
        public double L1_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Close Distance Filter", Order = 6, GroupName = "L1.7 Signal Filters")]
        public bool L1_EnableCloseDistanceFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Min Close Distance (Points)", Order = 7, GroupName = "L1.7 Signal Filters")]
        public double L1_MinCloseDistance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Filter Wait Bars", Order = 8, GroupName = "L1.7 Signal Filters")]
        public bool L1_EnableFilterWaitBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "L1 Filter Wait Max Bars", Order = 9, GroupName = "L1.7 Signal Filters")]
        public int L1_FilterWaitMaxBars { get; set; }

        // Direction Flip
        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Direction Flip", Order = 1, GroupName = "L1.8 Direction Flip")]
        public bool L1_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Same Direction On Loss", Order = 2, GroupName = "L1.8 Direction Flip")]
        public bool L1_EnableSameDirectionOnLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Loss Threshold (Points)", Order = 3, GroupName = "L1.8 Direction Flip")]
        public double L1_SameDirectionLossThreshold { get; set; }

        // Per-bucket Risk Management
        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Max Daily Loss", Order = 1, GroupName = "L1.9 Risk Management")]
        public bool L1_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Max Daily Loss (Points)", Order = 2, GroupName = "L1.9 Risk Management")]
        public double L1_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Max Daily Profit", Order = 3, GroupName = "L1.9 Risk Management")]
        public bool L1_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L1 Max Daily Profit (Points)", Order = 4, GroupName = "L1.9 Risk Management")]
        public double L1_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "L1 Enable Max Trades Per Day", Order = 5, GroupName = "L1.9 Risk Management")]
        public bool L1_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "L1 Max Trades Per Day", Order = 6, GroupName = "L1.9 Risk Management")]
        public int L1_MaxTradesPerDay { get; set; }

        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  L2: LONG — LARGE CANDLE
        // ════════════════════════════════════════════════════════════════════════

        #region L2 Parameters

        [NinjaScriptProperty]
        [Display(Name = "L2 Enable", Description = "Enable Long Large Candle bucket", Order = 0, GroupName = "L2.0 Bucket Settings")]
        public bool L2_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L2 Min Candle Range (Points)", Order = 1, GroupName = "L2.0 Bucket Settings")]
        public double L2_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "L2 Max Candle Range (Points)", Description = "0 = no max", Order = 2, GroupName = "L2.0 Bucket Settings")]
        public double L2_MaxCandleRange { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L2 EMA Length", Order = 1, GroupName = "L2.1 EMA Settings")]
        public int L2_EmaLength { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Enable EMA Slope Filter", Order = 2, GroupName = "L2.1 EMA Settings")]
        public bool L2_EnableEmaSlope { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 EMA Slope Mode", Order = 3, GroupName = "L2.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum L2_EmaSlopeMode { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L2 EMA Slope Bars", Order = 4, GroupName = "L2.1 EMA Settings")]
        public int L2_EmaSlopeBars { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 EMA Min Slope %", Order = 5, GroupName = "L2.1 EMA Settings")]
        public double L2_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Enable WMA Filter", Order = 1, GroupName = "L2.1b WMA Filter")]
        public bool L2_EnableWMAFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L2 WMA Length", Order = 2, GroupName = "L2.1b WMA Filter")]
        public int L2_WMALength { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Enable SMA Filter", Order = 1, GroupName = "L2.1c SMA Filter")]
        public bool L2_EnableSMAFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L2 SMA Length", Order = 2, GroupName = "L2.1c SMA Filter")]
        public int L2_SMALength { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Enable ADX Filter", Order = 1, GroupName = "L2.1d ADX Filter")]
        public bool L2_EnableADXFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L2 ADX Period", Order = 2, GroupName = "L2.1d ADX Filter")]
        public int L2_ADXPeriod { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 ADX Min Value", Order = 3, GroupName = "L2.1d ADX Filter")]
        public double L2_ADXMin { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 ADX Max Value", Order = 4, GroupName = "L2.1d ADX Filter")]
        public double L2_ADXMax { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Entry Type", Order = 1, GroupName = "L2.2 Entry Settings")]
        public HugoTesting_EntryTypeEnum L2_EntryType { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Pullback Offset (Points)", Order = 2, GroupName = "L2.2 Entry Settings")]
        public double L2_PullbackOffset { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "L2 Max Bars to Wait", Order = 3, GroupName = "L2.2 Entry Settings")]
        public int L2_MaxBarsToWait { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Stop Loss Type", Order = 1, GroupName = "L2.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum L2_StopLossType { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Fixed Stop Loss (Points)", Order = 2, GroupName = "L2.3 Stop Loss")]
        public double L2_FixedStopLoss { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "L2 SL Candle Multiplier", Order = 2, GroupName = "L2.3 Stop Loss")]
        public double L2_SLCandleMultiplier { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Stop Loss Offset (Points)", Order = 3, GroupName = "L2.3 Stop Loss")]
        public double L2_StopLossOffset { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Maximum Stop Loss (Points)", Order = 4, GroupName = "L2.3 Stop Loss")]
        public double L2_MaxStopLoss { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Enable Max Bars In Trade", Order = 5, GroupName = "L2.3 Stop Loss")]
        public bool L2_EnableMaxBarsInTrade { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L2 Max Bars In Trade", Order = 6, GroupName = "L2.3 Stop Loss")]
        public int L2_MaxBarsInTrade { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Take Profit Type", Order = 1, GroupName = "L2.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum L2_TakeProfitType { get; set; }
        [NinjaScriptProperty][Range(0.1, double.MaxValue)][Display(Name = "L2 Risk Reward Ratio", Order = 2, GroupName = "L2.4 Take Profit")]
        public double L2_RiskRewardRatio { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Fixed Take Profit (Points)", Order = 3, GroupName = "L2.4 Take Profit")]
        public double L2_FixedTakeProfit { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "L2 TP Candle Multiplier", Order = 4, GroupName = "L2.4 Take Profit")]
        public double L2_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Enable Break Even", Order = 1, GroupName = "L2.5 Break Even")]
        public bool L2_EnableBreakEven { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Break Even Trigger Type", Order = 2, GroupName = "L2.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum L2_BreakEvenTriggerType { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Break Even Trigger Value", Order = 3, GroupName = "L2.5 Break Even")]
        public double L2_BreakEvenTriggerValue { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Break Even Offset (Points)", Order = 4, GroupName = "L2.5 Break Even")]
        public double L2_BreakEvenOffset { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Enable SL to Entry on EMA Cross", Order = 5, GroupName = "L2.5 Break Even")]
        public bool L2_EnableSLToEntryOnEMA { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 SL to Entry Offset (Points)", Order = 6, GroupName = "L2.5 Break Even")]
        public double L2_SLToEntryOffset { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Enable Price Trail Stop", Order = 1, GroupName = "L2.6 Trailing Stop")]
        public bool L2_EnablePriceTrail { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Trail Distance Mode", Order = 2, GroupName = "L2.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum L2_TrailDistanceMode { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "L2 Trail Distance Value", Order = 3, GroupName = "L2.6 Trailing Stop")]
        public double L2_TrailDistanceValue { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Trail Activation Mode", Order = 4, GroupName = "L2.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum L2_TrailActivationMode { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Trail Activation Value", Order = 5, GroupName = "L2.6 Trailing Stop")]
        public double L2_TrailActivationValue { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "L2 Trail Step (Ticks)", Order = 6, GroupName = "L2.6 Trailing Stop")]
        public int L2_TrailStepTicks { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Enable Trail Lock", Order = 7, GroupName = "L2.6 Trailing Stop")]
        public bool L2_EnableTrailLock { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Trail Lock Value", Order = 8, GroupName = "L2.6 Trailing Stop")]
        public double L2_TrailLockValue { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Enable Candle Size Filter", Order = 1, GroupName = "L2.7 Signal Filters")]
        public bool L2_EnableCandleSizeFilter { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Min Candle Size (Points)", Order = 2, GroupName = "L2.7 Signal Filters")]
        public double L2_MinCandleSize { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Max Candle Size (Points)", Order = 3, GroupName = "L2.7 Signal Filters")]
        public double L2_MaxCandleSize { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Enable Body % Filter", Order = 4, GroupName = "L2.7 Signal Filters")]
        public bool L2_EnableBodyPctFilter { get; set; }
        [NinjaScriptProperty][Range(0, 100)][Display(Name = "L2 Min Body % of Range", Order = 5, GroupName = "L2.7 Signal Filters")]
        public double L2_MinBodyPct { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Enable Close Distance Filter", Order = 6, GroupName = "L2.7 Signal Filters")]
        public bool L2_EnableCloseDistanceFilter { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Min Close Distance (Points)", Order = 7, GroupName = "L2.7 Signal Filters")]
        public double L2_MinCloseDistance { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Enable Filter Wait Bars", Order = 8, GroupName = "L2.7 Signal Filters")]
        public bool L2_EnableFilterWaitBars { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L2 Filter Wait Max Bars", Order = 9, GroupName = "L2.7 Signal Filters")]
        public int L2_FilterWaitMaxBars { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Enable Direction Flip", Order = 1, GroupName = "L2.8 Direction Flip")]
        public bool L2_EnableDirectionFlip { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Enable Same Direction On Loss", Order = 2, GroupName = "L2.8 Direction Flip")]
        public bool L2_EnableSameDirectionOnLoss { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Loss Threshold (Points)", Order = 3, GroupName = "L2.8 Direction Flip")]
        public double L2_SameDirectionLossThreshold { get; set; }

        [NinjaScriptProperty][Display(Name = "L2 Enable Max Daily Loss", Order = 1, GroupName = "L2.9 Risk Management")]
        public bool L2_EnableMaxDailyLoss { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Max Daily Loss (Points)", Order = 2, GroupName = "L2.9 Risk Management")]
        public double L2_MaxDailyLoss { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Enable Max Daily Profit", Order = 3, GroupName = "L2.9 Risk Management")]
        public bool L2_EnableMaxDailyProfit { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "L2 Max Daily Profit (Points)", Order = 4, GroupName = "L2.9 Risk Management")]
        public double L2_MaxDailyProfit { get; set; }
        [NinjaScriptProperty][Display(Name = "L2 Enable Max Trades Per Day", Order = 5, GroupName = "L2.9 Risk Management")]
        public bool L2_EnableMaxTradesPerDay { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "L2 Max Trades Per Day", Order = 6, GroupName = "L2.9 Risk Management")]
        public int L2_MaxTradesPerDay { get; set; }

        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  S1: SHORT — SMALL CANDLE
        // ════════════════════════════════════════════════════════════════════════

        #region S1 Parameters

        [NinjaScriptProperty]
        [Display(Name = "S1 Enable", Description = "Enable Short Small Candle bucket", Order = 0, GroupName = "S1.0 Bucket Settings")]
        public bool S1_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 Min Candle Range (Points)", Order = 1, GroupName = "S1.0 Bucket Settings")]
        public double S1_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 Max Candle Range (Points)", Description = "0 = no max", Order = 2, GroupName = "S1.0 Bucket Settings")]
        public double S1_MaxCandleRange { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S1 EMA Length", Order = 1, GroupName = "S1.1 EMA Settings")]
        public int S1_EmaLength { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Enable EMA Slope Filter", Order = 2, GroupName = "S1.1 EMA Settings")]
        public bool S1_EnableEmaSlope { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 EMA Slope Mode", Order = 3, GroupName = "S1.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum S1_EmaSlopeMode { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S1 EMA Slope Bars", Order = 4, GroupName = "S1.1 EMA Settings")]
        public int S1_EmaSlopeBars { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 EMA Min Slope %", Order = 5, GroupName = "S1.1 EMA Settings")]
        public double S1_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Enable WMA Filter", Order = 1, GroupName = "S1.1b WMA Filter")]
        public bool S1_EnableWMAFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S1 WMA Length", Order = 2, GroupName = "S1.1b WMA Filter")]
        public int S1_WMALength { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Enable SMA Filter", Order = 1, GroupName = "S1.1c SMA Filter")]
        public bool S1_EnableSMAFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S1 SMA Length", Order = 2, GroupName = "S1.1c SMA Filter")]
        public int S1_SMALength { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Enable ADX Filter", Order = 1, GroupName = "S1.1d ADX Filter")]
        public bool S1_EnableADXFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S1 ADX Period", Order = 2, GroupName = "S1.1d ADX Filter")]
        public int S1_ADXPeriod { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 ADX Min Value", Order = 3, GroupName = "S1.1d ADX Filter")]
        public double S1_ADXMin { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 ADX Max Value", Order = 4, GroupName = "S1.1d ADX Filter")]
        public double S1_ADXMax { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Entry Type", Order = 1, GroupName = "S1.2 Entry Settings")]
        public HugoTesting_EntryTypeEnum S1_EntryType { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Pullback Offset (Points)", Order = 2, GroupName = "S1.2 Entry Settings")]
        public double S1_PullbackOffset { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "S1 Max Bars to Wait", Order = 3, GroupName = "S1.2 Entry Settings")]
        public int S1_MaxBarsToWait { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Stop Loss Type", Order = 1, GroupName = "S1.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum S1_StopLossType { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Fixed Stop Loss (Points)", Order = 2, GroupName = "S1.3 Stop Loss")]
        public double S1_FixedStopLoss { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "S1 SL Candle Multiplier", Order = 2, GroupName = "S1.3 Stop Loss")]
        public double S1_SLCandleMultiplier { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Stop Loss Offset (Points)", Order = 3, GroupName = "S1.3 Stop Loss")]
        public double S1_StopLossOffset { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Maximum Stop Loss (Points)", Order = 4, GroupName = "S1.3 Stop Loss")]
        public double S1_MaxStopLoss { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Enable Max Bars In Trade", Order = 5, GroupName = "S1.3 Stop Loss")]
        public bool S1_EnableMaxBarsInTrade { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S1 Max Bars In Trade", Order = 6, GroupName = "S1.3 Stop Loss")]
        public int S1_MaxBarsInTrade { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Take Profit Type", Order = 1, GroupName = "S1.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum S1_TakeProfitType { get; set; }
        [NinjaScriptProperty][Range(0.1, double.MaxValue)][Display(Name = "S1 Risk Reward Ratio", Order = 2, GroupName = "S1.4 Take Profit")]
        public double S1_RiskRewardRatio { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Fixed Take Profit (Points)", Order = 3, GroupName = "S1.4 Take Profit")]
        public double S1_FixedTakeProfit { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "S1 TP Candle Multiplier", Order = 4, GroupName = "S1.4 Take Profit")]
        public double S1_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Enable Break Even", Order = 1, GroupName = "S1.5 Break Even")]
        public bool S1_EnableBreakEven { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Break Even Trigger Type", Order = 2, GroupName = "S1.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum S1_BreakEvenTriggerType { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Break Even Trigger Value", Order = 3, GroupName = "S1.5 Break Even")]
        public double S1_BreakEvenTriggerValue { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Break Even Offset (Points)", Order = 4, GroupName = "S1.5 Break Even")]
        public double S1_BreakEvenOffset { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Enable SL to Entry on EMA Cross", Order = 5, GroupName = "S1.5 Break Even")]
        public bool S1_EnableSLToEntryOnEMA { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 SL to Entry Offset (Points)", Order = 6, GroupName = "S1.5 Break Even")]
        public double S1_SLToEntryOffset { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Enable Price Trail Stop", Order = 1, GroupName = "S1.6 Trailing Stop")]
        public bool S1_EnablePriceTrail { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Trail Distance Mode", Order = 2, GroupName = "S1.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum S1_TrailDistanceMode { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "S1 Trail Distance Value", Order = 3, GroupName = "S1.6 Trailing Stop")]
        public double S1_TrailDistanceValue { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Trail Activation Mode", Order = 4, GroupName = "S1.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum S1_TrailActivationMode { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Trail Activation Value", Order = 5, GroupName = "S1.6 Trailing Stop")]
        public double S1_TrailActivationValue { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "S1 Trail Step (Ticks)", Order = 6, GroupName = "S1.6 Trailing Stop")]
        public int S1_TrailStepTicks { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Enable Trail Lock", Order = 7, GroupName = "S1.6 Trailing Stop")]
        public bool S1_EnableTrailLock { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Trail Lock Value", Order = 8, GroupName = "S1.6 Trailing Stop")]
        public double S1_TrailLockValue { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Enable Candle Size Filter", Order = 1, GroupName = "S1.7 Signal Filters")]
        public bool S1_EnableCandleSizeFilter { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Min Candle Size (Points)", Order = 2, GroupName = "S1.7 Signal Filters")]
        public double S1_MinCandleSize { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Max Candle Size (Points)", Order = 3, GroupName = "S1.7 Signal Filters")]
        public double S1_MaxCandleSize { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Enable Body % Filter", Order = 4, GroupName = "S1.7 Signal Filters")]
        public bool S1_EnableBodyPctFilter { get; set; }
        [NinjaScriptProperty][Range(0, 100)][Display(Name = "S1 Min Body % of Range", Order = 5, GroupName = "S1.7 Signal Filters")]
        public double S1_MinBodyPct { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Enable Close Distance Filter", Order = 6, GroupName = "S1.7 Signal Filters")]
        public bool S1_EnableCloseDistanceFilter { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Min Close Distance (Points)", Order = 7, GroupName = "S1.7 Signal Filters")]
        public double S1_MinCloseDistance { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Enable Filter Wait Bars", Order = 8, GroupName = "S1.7 Signal Filters")]
        public bool S1_EnableFilterWaitBars { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S1 Filter Wait Max Bars", Order = 9, GroupName = "S1.7 Signal Filters")]
        public int S1_FilterWaitMaxBars { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Enable Direction Flip", Order = 1, GroupName = "S1.8 Direction Flip")]
        public bool S1_EnableDirectionFlip { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Enable Same Direction On Loss", Order = 2, GroupName = "S1.8 Direction Flip")]
        public bool S1_EnableSameDirectionOnLoss { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Loss Threshold (Points)", Order = 3, GroupName = "S1.8 Direction Flip")]
        public double S1_SameDirectionLossThreshold { get; set; }

        [NinjaScriptProperty][Display(Name = "S1 Enable Max Daily Loss", Order = 1, GroupName = "S1.9 Risk Management")]
        public bool S1_EnableMaxDailyLoss { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Max Daily Loss (Points)", Order = 2, GroupName = "S1.9 Risk Management")]
        public double S1_MaxDailyLoss { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Enable Max Daily Profit", Order = 3, GroupName = "S1.9 Risk Management")]
        public bool S1_EnableMaxDailyProfit { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S1 Max Daily Profit (Points)", Order = 4, GroupName = "S1.9 Risk Management")]
        public double S1_MaxDailyProfit { get; set; }
        [NinjaScriptProperty][Display(Name = "S1 Enable Max Trades Per Day", Order = 5, GroupName = "S1.9 Risk Management")]
        public bool S1_EnableMaxTradesPerDay { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S1 Max Trades Per Day", Order = 6, GroupName = "S1.9 Risk Management")]
        public int S1_MaxTradesPerDay { get; set; }

        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  S2: SHORT — LARGE CANDLE
        // ════════════════════════════════════════════════════════════════════════

        #region S2 Parameters

        [NinjaScriptProperty]
        [Display(Name = "S2 Enable", Description = "Enable Short Large Candle bucket", Order = 0, GroupName = "S2.0 Bucket Settings")]
        public bool S2_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 Min Candle Range (Points)", Order = 1, GroupName = "S2.0 Bucket Settings")]
        public double S2_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 Max Candle Range (Points)", Description = "0 = no max", Order = 2, GroupName = "S2.0 Bucket Settings")]
        public double S2_MaxCandleRange { get; set; }

        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S2 EMA Length", Order = 1, GroupName = "S2.1 EMA Settings")]
        public int S2_EmaLength { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Enable EMA Slope Filter", Order = 2, GroupName = "S2.1 EMA Settings")]
        public bool S2_EnableEmaSlope { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 EMA Slope Mode", Order = 3, GroupName = "S2.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum S2_EmaSlopeMode { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S2 EMA Slope Bars", Order = 4, GroupName = "S2.1 EMA Settings")]
        public int S2_EmaSlopeBars { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 EMA Min Slope %", Order = 5, GroupName = "S2.1 EMA Settings")]
        public double S2_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Enable WMA Filter", Order = 1, GroupName = "S2.1b WMA Filter")]
        public bool S2_EnableWMAFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S2 WMA Length", Order = 2, GroupName = "S2.1b WMA Filter")]
        public int S2_WMALength { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Enable SMA Filter", Order = 1, GroupName = "S2.1c SMA Filter")]
        public bool S2_EnableSMAFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S2 SMA Length", Order = 2, GroupName = "S2.1c SMA Filter")]
        public int S2_SMALength { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Enable ADX Filter", Order = 1, GroupName = "S2.1d ADX Filter")]
        public bool S2_EnableADXFilter { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S2 ADX Period", Order = 2, GroupName = "S2.1d ADX Filter")]
        public int S2_ADXPeriod { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 ADX Min Value", Order = 3, GroupName = "S2.1d ADX Filter")]
        public double S2_ADXMin { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 ADX Max Value", Order = 4, GroupName = "S2.1d ADX Filter")]
        public double S2_ADXMax { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Entry Type", Order = 1, GroupName = "S2.2 Entry Settings")]
        public HugoTesting_EntryTypeEnum S2_EntryType { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Pullback Offset (Points)", Order = 2, GroupName = "S2.2 Entry Settings")]
        public double S2_PullbackOffset { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "S2 Max Bars to Wait", Order = 3, GroupName = "S2.2 Entry Settings")]
        public int S2_MaxBarsToWait { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Stop Loss Type", Order = 1, GroupName = "S2.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum S2_StopLossType { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Fixed Stop Loss (Points)", Order = 2, GroupName = "S2.3 Stop Loss")]
        public double S2_FixedStopLoss { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "S2 SL Candle Multiplier", Order = 2, GroupName = "S2.3 Stop Loss")]
        public double S2_SLCandleMultiplier { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Stop Loss Offset (Points)", Order = 3, GroupName = "S2.3 Stop Loss")]
        public double S2_StopLossOffset { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Maximum Stop Loss (Points)", Order = 4, GroupName = "S2.3 Stop Loss")]
        public double S2_MaxStopLoss { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Enable Max Bars In Trade", Order = 5, GroupName = "S2.3 Stop Loss")]
        public bool S2_EnableMaxBarsInTrade { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S2 Max Bars In Trade", Order = 6, GroupName = "S2.3 Stop Loss")]
        public int S2_MaxBarsInTrade { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Take Profit Type", Order = 1, GroupName = "S2.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum S2_TakeProfitType { get; set; }
        [NinjaScriptProperty][Range(0.1, double.MaxValue)][Display(Name = "S2 Risk Reward Ratio", Order = 2, GroupName = "S2.4 Take Profit")]
        public double S2_RiskRewardRatio { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Fixed Take Profit (Points)", Order = 3, GroupName = "S2.4 Take Profit")]
        public double S2_FixedTakeProfit { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "S2 TP Candle Multiplier", Order = 4, GroupName = "S2.4 Take Profit")]
        public double S2_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Enable Break Even", Order = 1, GroupName = "S2.5 Break Even")]
        public bool S2_EnableBreakEven { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Break Even Trigger Type", Order = 2, GroupName = "S2.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum S2_BreakEvenTriggerType { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Break Even Trigger Value", Order = 3, GroupName = "S2.5 Break Even")]
        public double S2_BreakEvenTriggerValue { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Break Even Offset (Points)", Order = 4, GroupName = "S2.5 Break Even")]
        public double S2_BreakEvenOffset { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Enable SL to Entry on EMA Cross", Order = 5, GroupName = "S2.5 Break Even")]
        public bool S2_EnableSLToEntryOnEMA { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 SL to Entry Offset (Points)", Order = 6, GroupName = "S2.5 Break Even")]
        public double S2_SLToEntryOffset { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Enable Price Trail Stop", Order = 1, GroupName = "S2.6 Trailing Stop")]
        public bool S2_EnablePriceTrail { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Trail Distance Mode", Order = 2, GroupName = "S2.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum S2_TrailDistanceMode { get; set; }
        [NinjaScriptProperty][Range(0.01, double.MaxValue)][Display(Name = "S2 Trail Distance Value", Order = 3, GroupName = "S2.6 Trailing Stop")]
        public double S2_TrailDistanceValue { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Trail Activation Mode", Order = 4, GroupName = "S2.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum S2_TrailActivationMode { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Trail Activation Value", Order = 5, GroupName = "S2.6 Trailing Stop")]
        public double S2_TrailActivationValue { get; set; }
        [NinjaScriptProperty][Range(0, int.MaxValue)][Display(Name = "S2 Trail Step (Ticks)", Order = 6, GroupName = "S2.6 Trailing Stop")]
        public int S2_TrailStepTicks { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Enable Trail Lock", Order = 7, GroupName = "S2.6 Trailing Stop")]
        public bool S2_EnableTrailLock { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Trail Lock Value", Order = 8, GroupName = "S2.6 Trailing Stop")]
        public double S2_TrailLockValue { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Enable Candle Size Filter", Order = 1, GroupName = "S2.7 Signal Filters")]
        public bool S2_EnableCandleSizeFilter { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Min Candle Size (Points)", Order = 2, GroupName = "S2.7 Signal Filters")]
        public double S2_MinCandleSize { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Max Candle Size (Points)", Order = 3, GroupName = "S2.7 Signal Filters")]
        public double S2_MaxCandleSize { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Enable Body % Filter", Order = 4, GroupName = "S2.7 Signal Filters")]
        public bool S2_EnableBodyPctFilter { get; set; }
        [NinjaScriptProperty][Range(0, 100)][Display(Name = "S2 Min Body % of Range", Order = 5, GroupName = "S2.7 Signal Filters")]
        public double S2_MinBodyPct { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Enable Close Distance Filter", Order = 6, GroupName = "S2.7 Signal Filters")]
        public bool S2_EnableCloseDistanceFilter { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Min Close Distance (Points)", Order = 7, GroupName = "S2.7 Signal Filters")]
        public double S2_MinCloseDistance { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Enable Filter Wait Bars", Order = 8, GroupName = "S2.7 Signal Filters")]
        public bool S2_EnableFilterWaitBars { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S2 Filter Wait Max Bars", Order = 9, GroupName = "S2.7 Signal Filters")]
        public int S2_FilterWaitMaxBars { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Enable Direction Flip", Order = 1, GroupName = "S2.8 Direction Flip")]
        public bool S2_EnableDirectionFlip { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Enable Same Direction On Loss", Order = 2, GroupName = "S2.8 Direction Flip")]
        public bool S2_EnableSameDirectionOnLoss { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Loss Threshold (Points)", Order = 3, GroupName = "S2.8 Direction Flip")]
        public double S2_SameDirectionLossThreshold { get; set; }

        [NinjaScriptProperty][Display(Name = "S2 Enable Max Daily Loss", Order = 1, GroupName = "S2.9 Risk Management")]
        public bool S2_EnableMaxDailyLoss { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Max Daily Loss (Points)", Order = 2, GroupName = "S2.9 Risk Management")]
        public double S2_MaxDailyLoss { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Enable Max Daily Profit", Order = 3, GroupName = "S2.9 Risk Management")]
        public bool S2_EnableMaxDailyProfit { get; set; }
        [NinjaScriptProperty][Range(0, double.MaxValue)][Display(Name = "S2 Max Daily Profit (Points)", Order = 4, GroupName = "S2.9 Risk Management")]
        public double S2_MaxDailyProfit { get; set; }
        [NinjaScriptProperty][Display(Name = "S2 Enable Max Trades Per Day", Order = 5, GroupName = "S2.9 Risk Management")]
        public bool S2_EnableMaxTradesPerDay { get; set; }
        [NinjaScriptProperty][Range(1, int.MaxValue)][Display(Name = "S2 Max Trades Per Day", Order = 6, GroupName = "S2.9 Risk Management")]
        public int S2_MaxTradesPerDay { get; set; }

        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  BUCKET PARAMETER ACCESSOR — Reads correct parameters based on activeBucket
        // ════════════════════════════════════════════════════════════════════════

        #region Bucket Accessor Helper

        private EMA GetEma(string bucket)
        {
            switch (bucket) { case "L1": return emaL1; case "L2": return emaL2; case "S1": return emaS1; case "S2": return emaS2; default: return emaL1; }
        }

        private bool B_EnableEmaSlope(string b) { switch(b){case "L1":return L1_EnableEmaSlope;case "L2":return L2_EnableEmaSlope;case "S1":return S1_EnableEmaSlope;case "S2":return S2_EnableEmaSlope;default:return false;} }
        private HugoTesting_EmaSlopeModeEnum B_EmaSlopeMode(string b) { switch(b){case "L1":return L1_EmaSlopeMode;case "L2":return L2_EmaSlopeMode;case "S1":return S1_EmaSlopeMode;case "S2":return S2_EmaSlopeMode;default:return HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;} }
        private int B_EmaSlopeBars(string b) { switch(b){case "L1":return L1_EmaSlopeBars;case "L2":return L2_EmaSlopeBars;case "S1":return S1_EmaSlopeBars;case "S2":return S2_EmaSlopeBars;default:return 49;} }
        private double B_EmaSlopeMinPct(string b) { switch(b){case "L1":return L1_EmaSlopeMinPct;case "L2":return L2_EmaSlopeMinPct;case "S1":return S1_EmaSlopeMinPct;case "S2":return S2_EmaSlopeMinPct;default:return 0;} }

        private HugoTesting_EntryTypeEnum B_EntryType(string b) { switch(b){case "L1":return L1_EntryType;case "L2":return L2_EntryType;case "S1":return S1_EntryType;case "S2":return S2_EntryType;default:return HugoTesting_EntryTypeEnum.Immediate;} }
        private double B_PullbackOffset(string b) { switch(b){case "L1":return L1_PullbackOffset;case "L2":return L2_PullbackOffset;case "S1":return S1_PullbackOffset;case "S2":return S2_PullbackOffset;default:return 3;} }
        private int B_MaxBarsToWait(string b) { switch(b){case "L1":return L1_MaxBarsToWait;case "L2":return L2_MaxBarsToWait;case "S1":return S1_MaxBarsToWait;case "S2":return S2_MaxBarsToWait;default:return 0;} }

        private HugoTesting_StopLossTypeEnum B_StopLossType(string b) { switch(b){case "L1":return L1_StopLossType;case "L2":return L2_StopLossType;case "S1":return S1_StopLossType;case "S2":return S2_StopLossType;default:return HugoTesting_StopLossTypeEnum.Fixed;} }
        private double B_FixedStopLoss(string b) { switch(b){case "L1":return L1_FixedStopLoss;case "L2":return L2_FixedStopLoss;case "S1":return S1_FixedStopLoss;case "S2":return S2_FixedStopLoss;default:return 57;} }
        private double B_StopLossOffset(string b) { switch(b){case "L1":return L1_StopLossOffset;case "L2":return L2_StopLossOffset;case "S1":return S1_StopLossOffset;case "S2":return S2_StopLossOffset;default:return 56;} }
        private double B_MaxStopLoss(string b) { switch(b){case "L1":return L1_MaxStopLoss;case "L2":return L2_MaxStopLoss;case "S1":return S1_MaxStopLoss;case "S2":return S2_MaxStopLoss;default:return 50;} }

        private HugoTesting_TakeProfitTypeEnum B_TakeProfitType(string b) { switch(b){case "L1":return L1_TakeProfitType;case "L2":return L2_TakeProfitType;case "S1":return S1_TakeProfitType;case "S2":return S2_TakeProfitType;default:return HugoTesting_TakeProfitTypeEnum.RiskReward;} }
        private double B_RiskRewardRatio(string b) { switch(b){case "L1":return L1_RiskRewardRatio;case "L2":return L2_RiskRewardRatio;case "S1":return S1_RiskRewardRatio;case "S2":return S2_RiskRewardRatio;default:return 3.42;} }
        private double B_FixedTakeProfit(string b) { switch(b){case "L1":return L1_FixedTakeProfit;case "L2":return L2_FixedTakeProfit;case "S1":return S1_FixedTakeProfit;case "S2":return S2_FixedTakeProfit;default:return 100;} }
        private double B_SLCandleMultiplier(string b) { switch(b){case "L1":return L1_SLCandleMultiplier;case "L2":return L2_SLCandleMultiplier;case "S1":return S1_SLCandleMultiplier;case "S2":return S2_SLCandleMultiplier;default:return 1.0;} }
        private double B_TPCandleMultiplier(string b) { switch(b){case "L1":return L1_TPCandleMultiplier;case "L2":return L2_TPCandleMultiplier;case "S1":return S1_TPCandleMultiplier;case "S2":return S2_TPCandleMultiplier;default:return 1.0;} }

        private bool B_EnableBreakEven(string b) { switch(b){case "L1":return L1_EnableBreakEven;case "L2":return L2_EnableBreakEven;case "S1":return S1_EnableBreakEven;case "S2":return S2_EnableBreakEven;default:return false;} }
        private HugoTesting_BreakEvenTriggerEnum B_BreakEvenTriggerType(string b) { switch(b){case "L1":return L1_BreakEvenTriggerType;case "L2":return L2_BreakEvenTriggerType;case "S1":return S1_BreakEvenTriggerType;case "S2":return S2_BreakEvenTriggerType;default:return HugoTesting_BreakEvenTriggerEnum.RiskMultiple;} }
        private double B_BreakEvenTriggerValue(string b) { switch(b){case "L1":return L1_BreakEvenTriggerValue;case "L2":return L2_BreakEvenTriggerValue;case "S1":return S1_BreakEvenTriggerValue;case "S2":return S2_BreakEvenTriggerValue;default:return 3.29;} }
        private double B_BreakEvenOffset(string b) { switch(b){case "L1":return L1_BreakEvenOffset;case "L2":return L2_BreakEvenOffset;case "S1":return S1_BreakEvenOffset;case "S2":return S2_BreakEvenOffset;default:return 2;} }

        private bool B_EnablePriceTrail(string b) { switch(b){case "L1":return L1_EnablePriceTrail;case "L2":return L2_EnablePriceTrail;case "S1":return S1_EnablePriceTrail;case "S2":return S2_EnablePriceTrail;default:return false;} }
        private HugoTesting_TrailDistanceModeEnum B_TrailDistanceMode(string b) { switch(b){case "L1":return L1_TrailDistanceMode;case "L2":return L2_TrailDistanceMode;case "S1":return S1_TrailDistanceMode;case "S2":return S2_TrailDistanceMode;default:return HugoTesting_TrailDistanceModeEnum.FixedPoints;} }
        private double B_TrailDistanceValue(string b) { switch(b){case "L1":return L1_TrailDistanceValue;case "L2":return L2_TrailDistanceValue;case "S1":return S1_TrailDistanceValue;case "S2":return S2_TrailDistanceValue;default:return 20;} }
        private HugoTesting_TrailActivationModeEnum B_TrailActivationMode(string b) { switch(b){case "L1":return L1_TrailActivationMode;case "L2":return L2_TrailActivationMode;case "S1":return S1_TrailActivationMode;case "S2":return S2_TrailActivationMode;default:return HugoTesting_TrailActivationModeEnum.FixedPoints;} }
        private double B_TrailActivationValue(string b) { switch(b){case "L1":return L1_TrailActivationValue;case "L2":return L2_TrailActivationValue;case "S1":return S1_TrailActivationValue;case "S2":return S2_TrailActivationValue;default:return 30;} }
        private int B_TrailStepTicks(string b) { switch(b){case "L1":return L1_TrailStepTicks;case "L2":return L2_TrailStepTicks;case "S1":return S1_TrailStepTicks;case "S2":return S2_TrailStepTicks;default:return 0;} }
        private bool B_EnableTrailLock(string b) { switch(b){case "L1":return L1_EnableTrailLock;case "L2":return L2_EnableTrailLock;case "S1":return S1_EnableTrailLock;case "S2":return S2_EnableTrailLock;default:return false;} }
        private double B_TrailLockValue(string b) { switch(b){case "L1":return L1_TrailLockValue;case "L2":return L2_TrailLockValue;case "S1":return S1_TrailLockValue;case "S2":return S2_TrailLockValue;default:return 0;} }

        private bool B_EnableCandleSizeFilter(string b) { switch(b){case "L1":return L1_EnableCandleSizeFilter;case "L2":return L2_EnableCandleSizeFilter;case "S1":return S1_EnableCandleSizeFilter;case "S2":return S2_EnableCandleSizeFilter;default:return false;} }
        private double B_MinCandleSize(string b) { switch(b){case "L1":return L1_MinCandleSize;case "L2":return L2_MinCandleSize;case "S1":return S1_MinCandleSize;case "S2":return S2_MinCandleSize;default:return 10;} }
        private double B_MaxCandleSize(string b) { switch(b){case "L1":return L1_MaxCandleSize;case "L2":return L2_MaxCandleSize;case "S1":return S1_MaxCandleSize;case "S2":return S2_MaxCandleSize;default:return 0;} }
        private bool B_EnableBodyPctFilter(string b) { switch(b){case "L1":return L1_EnableBodyPctFilter;case "L2":return L2_EnableBodyPctFilter;case "S1":return S1_EnableBodyPctFilter;case "S2":return S2_EnableBodyPctFilter;default:return false;} }
        private double B_MinBodyPct(string b) { switch(b){case "L1":return L1_MinBodyPct;case "L2":return L2_MinBodyPct;case "S1":return S1_MinBodyPct;case "S2":return S2_MinBodyPct;default:return 33;} }
        private bool B_EnableCloseDistanceFilter(string b) { switch(b){case "L1":return L1_EnableCloseDistanceFilter;case "L2":return L2_EnableCloseDistanceFilter;case "S1":return S1_EnableCloseDistanceFilter;case "S2":return S2_EnableCloseDistanceFilter;default:return false;} }
        private double B_MinCloseDistance(string b) { switch(b){case "L1":return L1_MinCloseDistance;case "L2":return L2_MinCloseDistance;case "S1":return S1_MinCloseDistance;case "S2":return S2_MinCloseDistance;default:return 5;} }
        private bool B_EnableFilterWaitBars(string b) { switch(b){case "L1":return L1_EnableFilterWaitBars;case "L2":return L2_EnableFilterWaitBars;case "S1":return S1_EnableFilterWaitBars;case "S2":return S2_EnableFilterWaitBars;default:return false;} }
        private int B_FilterWaitMaxBars(string b) { switch(b){case "L1":return L1_FilterWaitMaxBars;case "L2":return L2_FilterWaitMaxBars;case "S1":return S1_FilterWaitMaxBars;case "S2":return S2_FilterWaitMaxBars;default:return 3;} }

        private bool B_EnableDirectionFlip(string b) { switch(b){case "L1":return L1_EnableDirectionFlip;case "L2":return L2_EnableDirectionFlip;case "S1":return S1_EnableDirectionFlip;case "S2":return S2_EnableDirectionFlip;default:return false;} }
        private bool B_EnableSameDirectionOnLoss(string b) { switch(b){case "L1":return L1_EnableSameDirectionOnLoss;case "L2":return L2_EnableSameDirectionOnLoss;case "S1":return S1_EnableSameDirectionOnLoss;case "S2":return S2_EnableSameDirectionOnLoss;default:return false;} }
        private double B_SameDirectionLossThreshold(string b) { switch(b){case "L1":return L1_SameDirectionLossThreshold;case "L2":return L2_SameDirectionLossThreshold;case "S1":return S1_SameDirectionLossThreshold;case "S2":return S2_SameDirectionLossThreshold;default:return 10;} }

        private bool B_EnableMaxDailyLoss(string b) { switch(b){case "L1":return L1_EnableMaxDailyLoss;case "L2":return L2_EnableMaxDailyLoss;case "S1":return S1_EnableMaxDailyLoss;case "S2":return S2_EnableMaxDailyLoss;default:return false;} }
        private double B_MaxDailyLoss(string b) { switch(b){case "L1":return L1_MaxDailyLoss;case "L2":return L2_MaxDailyLoss;case "S1":return S1_MaxDailyLoss;case "S2":return S2_MaxDailyLoss;default:return 307;} }
        private bool B_EnableMaxDailyProfit(string b) { switch(b){case "L1":return L1_EnableMaxDailyProfit;case "L2":return L2_EnableMaxDailyProfit;case "S1":return S1_EnableMaxDailyProfit;case "S2":return S2_EnableMaxDailyProfit;default:return false;} }
        private double B_MaxDailyProfit(string b) { switch(b){case "L1":return L1_MaxDailyProfit;case "L2":return L2_MaxDailyProfit;case "S1":return S1_MaxDailyProfit;case "S2":return S2_MaxDailyProfit;default:return 186;} }
        private bool B_EnableMaxTradesPerDay(string b) { switch(b){case "L1":return L1_EnableMaxTradesPerDay;case "L2":return L2_EnableMaxTradesPerDay;case "S1":return S1_EnableMaxTradesPerDay;case "S2":return S2_EnableMaxTradesPerDay;default:return false;} }
        private int B_MaxTradesPerDay(string b) { switch(b){case "L1":return L1_MaxTradesPerDay;case "L2":return L2_MaxTradesPerDay;case "S1":return S1_MaxTradesPerDay;case "S2":return S2_MaxTradesPerDay;default:return 6;} }

        // v1.06: Max Bars In Trade accessors
        private bool B_EnableMaxBarsInTrade(string b) { switch(b){case "L1":return L1_EnableMaxBarsInTrade;case "L2":return L2_EnableMaxBarsInTrade;case "S1":return S1_EnableMaxBarsInTrade;case "S2":return S2_EnableMaxBarsInTrade;default:return false;} }
        private int B_MaxBarsInTrade(string b) { switch(b){case "L1":return L1_MaxBarsInTrade;case "L2":return L2_MaxBarsInTrade;case "S1":return S1_MaxBarsInTrade;case "S2":return S2_MaxBarsInTrade;default:return 20;} }

        // v1.06: SL to Entry on EMA accessors
        private bool B_EnableSLToEntryOnEMA(string b) { switch(b){case "L1":return L1_EnableSLToEntryOnEMA;case "L2":return L2_EnableSLToEntryOnEMA;case "S1":return S1_EnableSLToEntryOnEMA;case "S2":return S2_EnableSLToEntryOnEMA;default:return false;} }
        private double B_SLToEntryOffset(string b) { switch(b){case "L1":return L1_SLToEntryOffset;case "L2":return L2_SLToEntryOffset;case "S1":return S1_SLToEntryOffset;case "S2":return S2_SLToEntryOffset;default:return 0;} }
        // v1.07: WMA / SMA / ADX filter accessors
        private WMA GetWma(string b) { switch(b){case "L1":return wmaL1;case "L2":return wmaL2;case "S1":return wmaS1;case "S2":return wmaS2;default:return wmaL1;} }
        private SMA GetSma(string b) { switch(b){case "L1":return smaL1;case "L2":return smaL2;case "S1":return smaS1;case "S2":return smaS2;default:return smaL1;} }
        private ADX GetAdx(string b) { switch(b){case "L1":return adxL1;case "L2":return adxL2;case "S1":return adxS1;case "S2":return adxS2;default:return adxL1;} }

        private bool B_EnableWMAFilter(string b) { switch(b){case "L1":return L1_EnableWMAFilter;case "L2":return L2_EnableWMAFilter;case "S1":return S1_EnableWMAFilter;case "S2":return S2_EnableWMAFilter;default:return false;} }
        private int B_WMALength(string b) { switch(b){case "L1":return L1_WMALength;case "L2":return L2_WMALength;case "S1":return S1_WMALength;case "S2":return S2_WMALength;default:return 20;} }
        private bool B_EnableSMAFilter(string b) { switch(b){case "L1":return L1_EnableSMAFilter;case "L2":return L2_EnableSMAFilter;case "S1":return S1_EnableSMAFilter;case "S2":return S2_EnableSMAFilter;default:return false;} }
        private int B_SMALength(string b) { switch(b){case "L1":return L1_SMALength;case "L2":return L2_SMALength;case "S1":return S1_SMALength;case "S2":return S2_SMALength;default:return 20;} }
        private bool B_EnableADXFilter(string b) { switch(b){case "L1":return L1_EnableADXFilter;case "L2":return L2_EnableADXFilter;case "S1":return S1_EnableADXFilter;case "S2":return S2_EnableADXFilter;default:return false;} }
        private int B_ADXPeriod(string b) { switch(b){case "L1":return L1_ADXPeriod;case "L2":return L2_ADXPeriod;case "S1":return S1_ADXPeriod;case "S2":return S2_ADXPeriod;default:return 14;} }
        private double B_ADXMin(string b) { switch(b){case "L1":return L1_ADXMin;case "L2":return L2_ADXMin;case "S1":return S1_ADXMin;case "S2":return S2_ADXMin;default:return 20;} }
        private double B_ADXMax(string b) { switch(b){case "L1":return L1_ADXMax;case "L2":return L2_ADXMax;case "S1":return S1_ADXMax;case "S2":return S2_ADXMax;default:return 100;} }


        // Per-bucket P&L accessors
        private double GetBucketDailyPnL(string b) { switch(b){case "L1":return dailyPnL_L1;case "L2":return dailyPnL_L2;case "S1":return dailyPnL_S1;case "S2":return dailyPnL_S2;default:return 0;} }
        private void AddBucketDailyPnL(string b, double v) { switch(b){case "L1":dailyPnL_L1+=v;break;case "L2":dailyPnL_L2+=v;break;case "S1":dailyPnL_S1+=v;break;case "S2":dailyPnL_S2+=v;break;} }
        private int GetBucketTradeCount(string b) { switch(b){case "L1":return dailyTradeCount_L1;case "L2":return dailyTradeCount_L2;case "S1":return dailyTradeCount_S1;case "S2":return dailyTradeCount_S2;default:return 0;} }
        private void IncBucketTradeCount(string b) { switch(b){case "L1":dailyTradeCount_L1++;break;case "L2":dailyTradeCount_L2++;break;case "S1":dailyTradeCount_S1++;break;case "S2":dailyTradeCount_S2++;break;} }
        private bool GetBucketLossHit(string b) { switch(b){case "L1":return dailyLossHit_L1;case "L2":return dailyLossHit_L2;case "S1":return dailyLossHit_S1;case "S2":return dailyLossHit_S2;default:return false;} }
        private void SetBucketLossHit(string b, bool v) { switch(b){case "L1":dailyLossHit_L1=v;break;case "L2":dailyLossHit_L2=v;break;case "S1":dailyLossHit_S1=v;break;case "S2":dailyLossHit_S2=v;break;} }
        private bool GetBucketProfitHit(string b) { switch(b){case "L1":return dailyProfitHit_L1;case "L2":return dailyProfitHit_L2;case "S1":return dailyProfitHit_S1;case "S2":return dailyProfitHit_S2;default:return false;} }
        private void SetBucketProfitHit(string b, bool v) { switch(b){case "L1":dailyProfitHit_L1=v;break;case "L2":dailyProfitHit_L2=v;break;case "S1":dailyProfitHit_S1=v;break;case "S2":dailyProfitHit_S2=v;break;} }
        private bool IsBucketEnabled(string b) { switch(b){case "L1":return L1_Enable;case "L2":return L2_Enable;case "S1":return S1_Enable;case "S2":return S2_Enable;default:return false;} }

        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  BUCKET ROUTING — Determines which bucket a signal belongs to
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Given a direction (1=long, -1=short) and candle range, find the matching bucket.
        /// Returns null if no bucket matches or matching bucket is disabled/halted.
        /// </summary>
        private string ResolveBucket(int direction, double candleRange)
        {
            string[] candidates = direction == 1 ? new[] { "L1", "L2" } : new[] { "S1", "S2" };

            foreach (string b in candidates)
            {
                if (!IsBucketEnabled(b)) continue;

                double minR = 0, maxR = 0;
                switch (b)
                {
                    case "L1": minR = L1_MinCandleRange; maxR = L1_MaxCandleRange; break;
                    case "L2": minR = L2_MinCandleRange; maxR = L2_MaxCandleRange; break;
                    case "S1": minR = S1_MinCandleRange; maxR = S1_MaxCandleRange; break;
                    case "S2": minR = S2_MinCandleRange; maxR = S2_MaxCandleRange; break;
                }

                if (candleRange < minR) continue;
                if (maxR > 0 && candleRange > maxR) continue;

                // Bucket range matches — check if bucket is halted
                if (GetBucketLossHit(b) || GetBucketProfitHit(b)) continue;
                if (B_EnableMaxTradesPerDay(b) && GetBucketTradeCount(b) >= B_MaxTradesPerDay(b)) continue;

                return b;
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  OnStateChange
        // ════════════════════════════════════════════════════════════════════════

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "HUGOTesting";
                Name        = "HUGOTesting";
                Calculate                               = Calculate.OnBarClose;
                EntriesPerDirection                     = 1;
                EntryHandling                           = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy            = false;
                ExitOnSessionCloseSeconds               = 30;
                IsFillLimitOnTouch                      = false;
                MaximumBarsLookBack                     = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution                     = OrderFillResolution.Standard;
                Slippage                                = 0;
                StartBehavior                           = StartBehavior.WaitUntilFlat;
                TimeInForce                             = TimeInForce.Gtc;
                TraceOrders                             = false;
                RealtimeErrorHandling                   = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling                      = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade                     = 20;
                IsInstantiatedOnEachOptimizationIteration = true;

                // ── Common defaults ──────────────────────────────────────────
                SessionStartTime        = "18:00";
                TradeWindowStart        = "03:50";
                LastEntryTime           = "13:15";
                ForceCloseTime          = "15:00";
                SessionEndTime          = "17:00";
                ContractQuantity        = 1;
                RequireEntryConfirmation = false;
                WebhookUrl              = string.Empty;
                WebhookTickerOverride   = string.Empty;
                MaxAccountBalance       = 0.0;

                EnableSkipTime          = true;
                SkipTimeStart           = "08:25";
                SkipTimeEnd             = "08:30";
                FlattenAtSkipStart      = false;
                UseNewsSkip             = false;
                NewsBlockMinutes        = 1;
                FlattenAtNewsStart      = false;

                EnableGlobalMaxDailyLoss    = true;
                GlobalMaxDailyLoss          = 307;
                EnableGlobalMaxDailyProfit  = true;
                GlobalMaxDailyProfit        = 186;
                EnableGlobalMaxTradesPerDay = true;
                GlobalMaxTradesPerDay       = 7;

                // ── L1: Long Small Candle ────────────────────────────────────
                L1_Enable               = true;
                L1_MinCandleRange       = 0;
                L1_MaxCandleRange       = 50;

                L1_EmaLength            = 48;
                L1_EnableEmaSlope       = true;
                L1_EmaSlopeMode         = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                L1_EmaSlopeBars         = 53;
                L1_EmaSlopeMinPct       = 0.069;

                L1_EnableWMAFilter      = false;
                L1_WMALength            = 20;
                L1_EnableSMAFilter      = false;
                L1_SMALength            = 50;
                L1_EnableADXFilter      = false;
                L1_ADXPeriod            = 14;
                L1_ADXMin               = 20;
                L1_ADXMax               = 100;

                L1_EntryType            = HugoTesting_EntryTypeEnum.Immediate;
                L1_PullbackOffset       = 3;
                L1_MaxBarsToWait        = 0;

                L1_StopLossType         = HugoTesting_StopLossTypeEnum.Fixed;
                L1_FixedStopLoss        = 59;
                L1_SLCandleMultiplier    = 1.0;
                L1_StopLossOffset       = 22;
                L1_MaxStopLoss          = 49.25;
                L1_EnableMaxBarsInTrade = false;
                L1_MaxBarsInTrade       = 20;

                L1_TakeProfitType       = HugoTesting_TakeProfitTypeEnum.RiskReward;
                L1_RiskRewardRatio      = 3.67;
                L1_FixedTakeProfit      = 203;
                L1_TPCandleMultiplier    = 1.0;

                L1_EnableBreakEven          = false;
                L1_BreakEvenTriggerType     = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                L1_BreakEvenTriggerValue    = 2.67;
                L1_BreakEvenOffset          = 11;
                L1_EnableSLToEntryOnEMA     = false;
                L1_SLToEntryOffset          = 0;

                L1_EnablePriceTrail         = false;
                L1_TrailDistanceMode        = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                L1_TrailDistanceValue       = 55;
                L1_TrailActivationMode      = HugoTesting_TrailActivationModeEnum.FixedPoints;
                L1_TrailActivationValue     = 150;
                L1_TrailStepTicks           = 0;
                L1_EnableTrailLock          = false;
                L1_TrailLockValue           = 0;

                L1_EnableCandleSizeFilter   = false;
                L1_MinCandleSize            = 10;
                L1_MaxCandleSize            = 0;
                L1_EnableBodyPctFilter      = true;
                L1_MinBodyPct               = 32;
                L1_EnableCloseDistanceFilter = false;
                L1_MinCloseDistance          = 5;
                L1_EnableFilterWaitBars     = false;
                L1_FilterWaitMaxBars        = 3;

                L1_EnableDirectionFlip         = false;
                L1_EnableSameDirectionOnLoss   = false;
                L1_SameDirectionLossThreshold  = 10;

                L1_EnableMaxDailyLoss       = true;
                L1_MaxDailyLoss             = 203;
                L1_EnableMaxDailyProfit     = true;
                L1_MaxDailyProfit           = 157;
                L1_EnableMaxTradesPerDay    = false;
                L1_MaxTradesPerDay          = 2;

                // ── L2: Long Large Candle (same defaults, different range) ───
                L2_Enable               = true;
                L2_MinCandleRange       = 50;
                L2_MaxCandleRange       = 0;

                L2_EmaLength            = 47;
                L2_EnableEmaSlope       = true;
                L2_EmaSlopeMode         = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                L2_EmaSlopeBars         = 27;
                L2_EmaSlopeMinPct       = 0.055;

                L2_EnableWMAFilter      = true;
                L2_WMALength            = 70;
                L2_EnableSMAFilter      = false;
                L2_SMALength            = 49;
                L2_EnableADXFilter      = false;
                L2_ADXPeriod            = 3;
                L2_ADXMin               = 14;
                L2_ADXMax               = 100;

                L2_EntryType            = HugoTesting_EntryTypeEnum.Immediate;
                L2_PullbackOffset       = 3;
                L2_MaxBarsToWait        = 0;

                L2_StopLossType         = HugoTesting_StopLossTypeEnum.Fixed;
                L2_FixedStopLoss        = 56.75;
                L2_SLCandleMultiplier    = 1.0;
                L2_StopLossOffset       = 56;
                L2_MaxStopLoss          = 50;
                L2_EnableMaxBarsInTrade = false;
                L2_MaxBarsInTrade       = 20;

                L2_TakeProfitType       = HugoTesting_TakeProfitTypeEnum.RiskReward;
                L2_RiskRewardRatio      = 3.42;
                L2_FixedTakeProfit      = 100;
                L2_TPCandleMultiplier    = 1.0;

                L2_EnableBreakEven          = true;
                L2_BreakEvenTriggerType     = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                L2_BreakEvenTriggerValue    = 1.88;
                L2_BreakEvenOffset          = 2;
                L2_EnableSLToEntryOnEMA     = false;
                L2_SLToEntryOffset          = 0;

                L2_EnablePriceTrail         = false;
                L2_TrailDistanceMode        = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                L2_TrailDistanceValue       = 25;
                L2_TrailActivationMode      = HugoTesting_TrailActivationModeEnum.FixedPoints;
                L2_TrailActivationValue     = 192;
                L2_TrailStepTicks           = 0;
                L2_EnableTrailLock          = false;
                L2_TrailLockValue           = 0;

                L2_EnableCandleSizeFilter   = false;
                L2_MinCandleSize            = 10;
                L2_MaxCandleSize            = 0;
                L2_EnableBodyPctFilter      = true;
                L2_MinBodyPct               = 31;
                L2_EnableCloseDistanceFilter = false;
                L2_MinCloseDistance          = 5;
                L2_EnableFilterWaitBars     = false;
                L2_FilterWaitMaxBars        = 3;

                L2_EnableDirectionFlip         = true;
                L2_EnableSameDirectionOnLoss   = false;
                L2_SameDirectionLossThreshold  = 1;

                L2_EnableMaxDailyLoss       = true;
                L2_MaxDailyLoss             = 207;
                L2_EnableMaxDailyProfit     = true;
                L2_MaxDailyProfit           = 186;
                L2_EnableMaxTradesPerDay    = false;
                L2_MaxTradesPerDay          = 6;

                // ── S1: Short Small Candle ───────────────────────────────────
                S1_Enable               = true;
                S1_MinCandleRange       = 0;
                S1_MaxCandleRange       = 50;

                S1_EmaLength            = 48;
                S1_EnableEmaSlope       = true;
                S1_EmaSlopeMode         = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                S1_EmaSlopeBars         = 45;
                S1_EmaSlopeMinPct       = 0.057;

                S1_EnableWMAFilter      = true;
                S1_WMALength            = 14;
                S1_EnableSMAFilter      = false;
                S1_SMALength            = 10;
                S1_EnableADXFilter      = false;
                S1_ADXPeriod            = 5;
                S1_ADXMin               = 20;
                S1_ADXMax               = 100;

                S1_EntryType            = HugoTesting_EntryTypeEnum.Immediate;
                S1_PullbackOffset       = 3;
                S1_MaxBarsToWait        = 0;

                S1_StopLossType         = HugoTesting_StopLossTypeEnum.Fixed;
                S1_FixedStopLoss        = 61.5;
                S1_SLCandleMultiplier    = 1.0;
                S1_StopLossOffset       = 56;
                S1_MaxStopLoss          = 50;
                S1_EnableMaxBarsInTrade = false;
                S1_MaxBarsInTrade       = 20;

                S1_TakeProfitType       = HugoTesting_TakeProfitTypeEnum.RiskReward;
                S1_RiskRewardRatio      = 3.42;
                S1_FixedTakeProfit      = 100;
                S1_TPCandleMultiplier    = 1.0;

                S1_EnableBreakEven          = true;
                S1_BreakEvenTriggerType     = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                S1_BreakEvenTriggerValue    = 1.05;
                S1_BreakEvenOffset          = 2;
                S1_EnableSLToEntryOnEMA     = false;
                S1_SLToEntryOffset          = 0;

                S1_EnablePriceTrail         = false;
                S1_TrailDistanceMode        = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                S1_TrailDistanceValue       = 20;
                S1_TrailActivationMode      = HugoTesting_TrailActivationModeEnum.FixedPoints;
                S1_TrailActivationValue     = 147;
                S1_TrailStepTicks           = 0;
                S1_EnableTrailLock          = false;
                S1_TrailLockValue           = 0;

                S1_EnableCandleSizeFilter   = false;
                S1_MinCandleSize            = 10;
                S1_MaxCandleSize            = 0;
                S1_EnableBodyPctFilter      = true;
                S1_MinBodyPct               = 33;
                S1_EnableCloseDistanceFilter = false;
                S1_MinCloseDistance          = 5;
                S1_EnableFilterWaitBars     = false;
                S1_FilterWaitMaxBars        = 3;

                S1_EnableDirectionFlip         = false;
                S1_EnableSameDirectionOnLoss   = false;
                S1_SameDirectionLossThreshold  = 10;

                S1_EnableMaxDailyLoss       = true;
                S1_MaxDailyLoss             = 155;
                S1_EnableMaxDailyProfit     = true;
                S1_MaxDailyProfit           = 187;
                S1_EnableMaxTradesPerDay    = false;
                S1_MaxTradesPerDay          = 6;

                // ── S2: Short Large Candle ───────────────────────────────────
                S2_Enable               = true;
                S2_MinCandleRange       = 50;
                S2_MaxCandleRange       = 193;

                S2_EmaLength            = 46;
                S2_EnableEmaSlope       = true;
                S2_EmaSlopeMode         = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                S2_EmaSlopeBars         = 46;
                S2_EmaSlopeMinPct       = 0.06;

                S2_EnableWMAFilter      = false;
                S2_WMALength            = 20;
                S2_EnableSMAFilter      = false;
                S2_SMALength            = 50;
                S2_EnableADXFilter      = false;
                S2_ADXPeriod            = 14;
                S2_ADXMin               = 20;
                S2_ADXMax               = 100;

                S2_EntryType            = HugoTesting_EntryTypeEnum.Immediate;
                S2_PullbackOffset       = 3;
                S2_MaxBarsToWait        = 0;

                S2_StopLossType         = HugoTesting_StopLossTypeEnum.Fixed;
                S2_FixedStopLoss        = 61;
                S2_SLCandleMultiplier    = 1.0;
                S2_StopLossOffset       = 56;
                S2_MaxStopLoss          = 50;
                S2_EnableMaxBarsInTrade = false;
                S2_MaxBarsInTrade       = 20;

                S2_TakeProfitType       = HugoTesting_TakeProfitTypeEnum.RiskReward;
                S2_RiskRewardRatio      = 3.35;
                S2_FixedTakeProfit      = 100;
                S2_TPCandleMultiplier    = 1.0;

                S2_EnableBreakEven          = true;
                S2_BreakEvenTriggerType     = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                S2_BreakEvenTriggerValue    = 3.27;
                S2_BreakEvenOffset          = 2;
                S2_EnableSLToEntryOnEMA     = false;
                S2_SLToEntryOffset          = 0;

                S2_EnablePriceTrail         = false;
                S2_TrailDistanceMode        = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                S2_TrailDistanceValue       = 60;
                S2_TrailActivationMode      = HugoTesting_TrailActivationModeEnum.FixedPoints;
                S2_TrailActivationValue     = 196;
                S2_TrailStepTicks           = 0;
                S2_EnableTrailLock          = false;
                S2_TrailLockValue           = 0;

                S2_EnableCandleSizeFilter   = false;
                S2_MinCandleSize            = 10;
                S2_MaxCandleSize            = 0;
                S2_EnableBodyPctFilter      = true;
                S2_MinBodyPct               = 31;
                S2_EnableCloseDistanceFilter = false;
                S2_MinCloseDistance          = 5;
                S2_EnableFilterWaitBars     = false;
                S2_FilterWaitMaxBars        = 3;

                S2_EnableDirectionFlip         = false;
                S2_EnableSameDirectionOnLoss   = false;
                S2_SameDirectionLossThreshold  = 10;

                S2_EnableMaxDailyLoss       = true;
                S2_MaxDailyLoss             = 105;
                S2_EnableMaxDailyProfit     = true;
                S2_MaxDailyProfit           = 131;
                S2_EnableMaxTradesPerDay    = true;
                S2_MaxTradesPerDay          = 2;
            }
            else if (State == State.Configure)
            {
                longSignalActive        = false;
                shortSignalActive       = false;
                signalBar               = 0;
                globalDailyPnL          = 0;
                lastTradeDate           = DateTime.MinValue;
                globalDailyLossLimitHit = false;
                globalDailyProfitLimitHit = false;
                globalDailyTradeCount   = 0;
                pendingDirection        = 0;
                breakEvenMoved          = false;
                currentTradeDirection   = 0;
                currentTargetPrice      = 0;
                lastTradeDirection      = 0;
                lastTradePnL            = 0;
                pendingLongFilterWait   = false;
                pendingShortFilterWait  = false;
                filterWaitStartBar      = 0;
                pendingFilterBucket     = null;
                activeBucket            = null;
                bestPriceSinceEntry     = 0;
                trailActivated          = false;
                trailLocked             = false;
                initialStopDistance     = 0;
                initialTPDistance       = 0;
                entryBar                = 0;
                slMovedToEntryOnEMA     = false;
                inSkipWindow            = false;
                inNewsSkipWindow        = false;

                // Reset per-bucket P&L
                dailyPnL_L1 = dailyPnL_L2 = dailyPnL_S1 = dailyPnL_S2 = 0;
                dailyTradeCount_L1 = dailyTradeCount_L2 = dailyTradeCount_S1 = dailyTradeCount_S2 = 0;
                dailyLossHit_L1 = dailyLossHit_L2 = dailyLossHit_S1 = dailyLossHit_S2 = false;
                dailyProfitHit_L1 = dailyProfitHit_L2 = dailyProfitHit_S1 = dailyProfitHit_S2 = false;
                managedEntrySignal      = string.Empty;
                entrySignalSequence     = 0;
                maxAccountLimitHit      = false;
                isConfiguredTimeframeValid = true;
                isConfiguredInstrumentValid = true;
                timeframePopupShown     = false;
                instrumentPopupShown    = false;
            }
            else if (State == State.DataLoaded)
            {
                ValidateRequiredPrimaryTimeframe(RequiredPrimaryTimeframeMinutes);
                ValidateRequiredPrimaryInstrument();
                EnsureNewsDatesInitialized();

                emaL1 = EMA(L1_EmaLength);
                wmaL1 = WMA(L1_WMALength);
                wmaL2 = WMA(L2_WMALength);
                wmaS1 = WMA(S1_WMALength);
                wmaS2 = WMA(S2_WMALength);

                smaL1 = SMA(L1_SMALength);
                smaL2 = SMA(L2_SMALength);
                smaS1 = SMA(S1_SMALength);
                smaS2 = SMA(S2_SMALength);

                adxL1 = ADX(L1_ADXPeriod);
                adxL2 = ADX(L2_ADXPeriod);
                adxS1 = ADX(S1_ADXPeriod);
                adxS2 = ADX(S2_ADXPeriod);

                emaL2 = EMA(L2_EmaLength);
                emaS1 = EMA(S1_EmaLength);
                emaS2 = EMA(S2_EmaLength);

                // Add distinct EMAs to chart (avoid duplicates if same length)
                AddChartIndicator(emaL1);
                if (L2_EmaLength != L1_EmaLength) AddChartIndicator(emaL2);
                if (S1_EmaLength != L1_EmaLength && S1_EmaLength != L2_EmaLength) AddChartIndicator(emaS1);
                if (S2_EmaLength != L1_EmaLength && S2_EmaLength != L2_EmaLength && S2_EmaLength != S1_EmaLength) AddChartIndicator(emaS2);

                heartbeatReporter = new StrategyHeartbeatReporter(
                    HeartbeatStrategyName,
                    System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv"));
            }
            else if (State == State.Realtime)
            {
                if (heartbeatReporter != null)
                    heartbeatReporter.Start();
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

        private string GetEntrySignalPrefix(int direction)
        {
            return direction == 1 ? LongEntrySignal : ShortEntrySignal;
        }

        private string BuildManagedEntrySignal(int direction)
        {
            return GetEntrySignalPrefix(direction);
        }

        private string BuildExitSignalName(string reason)
        {
            return StrategySignalPrefix + (reason ?? string.Empty);
        }

        private bool IsLongEntryOrderName(string orderName)
        {
            return !string.IsNullOrWhiteSpace(orderName)
                && string.Equals(orderName, LongEntrySignal, StringComparison.Ordinal);
        }

        private bool IsShortEntryOrderName(string orderName)
        {
            return !string.IsNullOrWhiteSpace(orderName)
                && string.Equals(orderName, ShortEntrySignal, StringComparison.Ordinal);
        }

        private string GetOpenLongEntrySignal()
        {
            return LongEntrySignal;
        }

        private string GetOpenShortEntrySignal()
        {
            return ShortEntrySignal;
        }

        private string GetActiveEntrySignal()
        {
            if (Position.MarketPosition == MarketPosition.Long)
                return GetOpenLongEntrySignal();
            if (Position.MarketPosition == MarketPosition.Short)
                return GetOpenShortEntrySignal();
            if (currentTradeDirection > 0)
                return GetOpenLongEntrySignal();
            if (currentTradeDirection < 0)
                return GetOpenShortEntrySignal();
            if (!string.IsNullOrWhiteSpace(activeBucket))
                return activeBucket.StartsWith("L", StringComparison.Ordinal) ? GetOpenLongEntrySignal() : GetOpenShortEntrySignal();

            return managedEntrySignal ?? string.Empty;
        }

        private void ConfigureInitialProtectiveOrders(string entrySignal, double stopLoss, double takeProfit)
        {
            SetStopLoss(CalculationMode.Price, stopLoss);
            if (takeProfit > 0)
                SetProfitTarget(CalculationMode.Price, takeProfit);
        }

        private void ExitAllPositions(string reason)
        {
            string exitSignal = BuildExitSignalName(reason);
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (string.Equals(reason, "ForceClose", StringComparison.Ordinal))
                    ExitLong(Math.Max(1, Position.Quantity), exitSignal, GetOpenLongEntrySignal());
                else
                    ExitLong(exitSignal);
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (string.Equals(reason, "ForceClose", StringComparison.Ordinal))
                    ExitShort(Math.Max(1, Position.Quantity), exitSignal, GetOpenShortEntrySignal());
                else
                    ExitShort(exitSignal);
            }
        }

        private bool IsAccountBalanceBlocked()
        {
            if (MaxAccountBalance <= 0.0)
                return false;

            double balance;
            if (!TryGetCurrentNetLiquidation(out balance))
                return false;

            if (balance >= MaxAccountBalance && !maxAccountLimitHit)
            {
                maxAccountLimitHit = true;
                Print(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} - Max account balance reached | netLiq={1:0.00} target={2:0.00}",
                    Time[0],
                    balance,
                    MaxAccountBalance));
            }

            if (!maxAccountLimitHit)
                return false;

            CancelAllOrders();
            ExitAllPositions("MaxAccountBalance");
            return true;
        }

        private bool TryGetCurrentNetLiquidation(out double netLiquidation)
        {
            netLiquidation = 0.0;
            if (Account == null)
                return false;

            try
            {
                netLiquidation = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                if (netLiquidation > 0.0)
                    return true;

                double realizedCash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                double unrealized = Position.MarketPosition != MarketPosition.Flat
                    ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0])
                    : 0.0;
                netLiquidation = realizedCash + unrealized;
                return realizedCash > 0.0 || Position.MarketPosition != MarketPosition.Flat;
            }
            catch
            {
                return false;
            }
        }

        private void ValidateRequiredPrimaryTimeframe(int requiredMinutes)
        {
            bool isMinuteSeries = BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute;
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
            Print("Timeframe validation failed | " + message);
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
            catch
            {
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
            Print("Instrument validation failed | " + message);
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
            catch
            {
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  OnBarUpdate
        // ════════════════════════════════════════════════════════════════════════

        // v1.10: Convert a time-of-day to session-relative minutes.
        // Returns how many minutes after the session start the given time is.
        // Wraps negative values by adding 1440 so that times before session start
        // (which in a midnight-crossing session means they are near end of session
        // the following day) are correctly placed after session start.
        // Example: SessionStart=18:00 → 18:00→0, 00:00→360, 14:30→1230, 17:59→1439.
        private double ToSessionMinutes(TimeSpan timeOfDay)
        {
            double sessionStartMin = TimeSpan.Parse(SessionStartTime).TotalMinutes;
            double minutes = timeOfDay.TotalMinutes - sessionStartMin;
            if (minutes < 0) minutes += 1440.0;
            return minutes;
        }

        private DateTime GetSessionAnchorDate(DateTime barTime)
        {
            TimeSpan sessionStartTOD = TimeSpan.Parse(SessionStartTime);
            return barTime.TimeOfDay >= sessionStartTOD ? barTime.Date : barTime.Date.AddDays(-1);
        }

        private DateTime GetSessionDateTime(DateTime barTime, TimeSpan timeOfDay)
        {
            DateTime sessionStart = GetSessionAnchorDate(barTime).Date + TimeSpan.Parse(SessionStartTime);
            return sessionStart.AddMinutes(ToSessionMinutes(timeOfDay));
        }

        private bool TryGetConfiguredWindow(DateTime barTime, string startTimeText, string endTimeText, out DateTime windowStart, out DateTime windowEnd)
        {
            windowStart = DateTime.MinValue;
            windowEnd = DateTime.MinValue;

            TimeSpan startTime = TimeSpan.Parse(startTimeText);
            TimeSpan endTime = TimeSpan.Parse(endTimeText);

            windowStart = GetSessionDateTime(barTime, startTime);
            windowEnd = GetSessionDateTime(barTime, endTime);
            return windowEnd > windowStart;
        }

        private bool IsSkipWindowConfigured()
        {
            return EnableSkipTime && TimeSpan.Parse(SkipTimeStart) != TimeSpan.Parse(SkipTimeEnd);
        }

        private bool IsInSkipWindow(DateTime barTime)
        {
            DateTime windowStart;
            DateTime windowEnd;
            return TryGetSkipWindow(barTime, out windowStart, out windowEnd)
                && barTime >= windowStart
                && barTime < windowEnd;
        }

        private bool TryGetSkipWindow(DateTime barTime, out DateTime windowStart, out DateTime windowEnd)
        {
            if (!IsSkipWindowConfigured())
            {
                windowStart = DateTime.MinValue;
                windowEnd = DateTime.MinValue;
                return false;
            }

            return TryGetConfiguredWindow(barTime, SkipTimeStart, SkipTimeEnd, out windowStart, out windowEnd);
        }

        private bool IsNewsWindowConfigured()
        {
            return UseNewsSkip && NewsBlockMinutes > 0 && NewsDates.Count > 0;
        }

        private bool IsInNewsSkipWindow(DateTime barTime)
        {
            DateTime windowStart;
            DateTime windowEnd;
            return TryGetNewsWindow(barTime, out windowStart, out windowEnd)
                && barTime >= windowStart
                && barTime < windowEnd;
        }

        private bool TryGetNewsWindow(DateTime barTime, out DateTime windowStart, out DateTime windowEnd)
        {
            windowStart = DateTime.MinValue;
            windowEnd = DateTime.MinValue;

            if (!IsNewsWindowConfigured())
                return false;

            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime newsTime = NewsDates[i];
                if (newsTime.Date != barTime.Date)
                    continue;

                DateTime candidateWindowStart = newsTime.AddMinutes(-NewsBlockMinutes);
                DateTime candidateWindowEnd = newsTime.AddMinutes(NewsBlockMinutes);
                if (barTime < candidateWindowStart || barTime >= candidateWindowEnd)
                    continue;

                windowStart = candidateWindowStart;
                windowEnd = candidateWindowEnd;
                return true;
            }

            return false;
        }

        private void DrawSessionTimeWindows()
        {
            if (CurrentBar < 1)
                return;

            DrawSessionBackground(Time[0]);
            DrawForceCloseLine(Time[0]);
            DrawSkipWindow(Time[0]);
            DrawNewsWindow(Time[0]);
        }

        private void DrawSessionBackground(DateTime barTime)
        {
            DateTime sessionStart;
            DateTime sessionEnd;
            if (!TryGetConfiguredWindow(barTime, TradeWindowStart, SessionEndTime, out sessionStart, out sessionEnd))
                return;

            string rectTag = string.Format("Hugo_SessionFill_{0:yyyyMMdd_HHmm}", sessionStart);
            if (DrawObjects[rectTag] != null)
                return;

            Draw.Rectangle(
                this,
                rectTag,
                false,
                sessionStart,
                0,
                sessionEnd,
                30000,
                Brushes.Transparent,
                Brushes.Gold,
                10).ZOrder = -1;
        }

        private void DrawSkipWindow(DateTime barTime)
        {
            DateTime windowStart;
            DateTime windowEnd;
            if (!TryGetSkipWindow(barTime, out windowStart, out windowEnd))
                return;

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

            string tagBase = string.Format("Hugo_Skip_{0:yyyyMMdd_HHmm}", windowStart);
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

        private void DrawForceCloseLine(DateTime barTime)
        {
            DateTime lineTime = GetSessionDateTime(barTime, TimeSpan.Parse(ForceCloseTime));
            string tag = string.Format("Hugo_ForceClose_{0:yyyyMMdd_HHmm}", lineTime);
            Draw.VerticalLine(this, tag, lineTime, Brushes.Red, DashStyleHelper.DashDot, 2);
        }

        private void DrawNewsWindow(DateTime barTime)
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

                int startBarsAgo = Bars.GetBar(windowStart);
                int endBarsAgo = Bars.GetBar(windowEnd);
                if (startBarsAgo < 0 || endBarsAgo < 0)
                    continue;

                var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));
                var lineBrush = new SolidColorBrush(Color.FromArgb(20, 30, 144, 255));
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

                string tagBase = string.Format("Hugo_News_{0:yyyyMMdd_HHmm}", newsTime);
                if (DrawObjects[tagBase + "_Rect"] != null)
                    continue;

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

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            DrawSessionTimeWindows();
            UpdateInfoBox();

            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
            {
                CancelAllOrders();
                ExitAllPositions("InvalidConfiguration");
                return;
            }

            if (IsAccountBalanceBlocked())
                return;

            if (CurrentBar < BarsRequiredToTrade) return;

            // ── Session reset (midnight-crossing safe) ───────────────────────
            // Anchor the "trading day" to the session start time so a session that
            // begins at 18:00 and runs to 17:00 the next calendar day is treated as
            // a single session, not split at midnight.
            TimeSpan barTOD         = Time[0].TimeOfDay;
            DateTime sessionDate    = GetSessionAnchorDate(Time[0]);

            if (sessionDate != currentSessionDate)
            {
                globalDailyPnL          = 0;
                lastTradeDate           = Time[0].Date;
                currentSessionDate      = sessionDate;
                globalDailyLossLimitHit = false;
                globalDailyProfitLimitHit = false;
                globalDailyTradeCount   = 0;
                lastTradeDirection      = 0;
                lastTradePnL            = 0;
                pendingLongFilterWait   = false;
                pendingShortFilterWait  = false;
                pendingFilterBucket     = null;
                inSkipWindow            = false;
                inNewsSkipWindow        = false;

                dailyPnL_L1 = dailyPnL_L2 = dailyPnL_S1 = dailyPnL_S2 = 0;
                dailyTradeCount_L1 = dailyTradeCount_L2 = dailyTradeCount_S1 = dailyTradeCount_S2 = 0;
                dailyLossHit_L1 = dailyLossHit_L2 = dailyLossHit_S1 = dailyLossHit_S2 = false;
                dailyProfitHit_L1 = dailyProfitHit_L2 = dailyProfitHit_S1 = dailyProfitHit_S2 = false;
            }

            // ── Time guards (midnight-crossing safe) ─────────────────────────
            // Convert all times to session-relative minutes so that comparisons work
            // correctly even when the session spans midnight (e.g. 18:00 – 17:00).
            // ToSessionMinutes(t) returns how many minutes after session-start t is.
            // A session that starts at 18:00 maps: 18:00→0, 23:59→359, 00:00→360,
            // 14:30→1230, 15:50→1310, 17:00→1380. Simple >= / < then works.
            double barSM             = ToSessionMinutes(barTOD);
            double tradeWindowStartSM = ToSessionMinutes(TimeSpan.Parse(TradeWindowStart));
            double lastEntrySM       = ToSessionMinutes(TimeSpan.Parse(LastEntryTime));
            double forceCloseSM      = ToSessionMinutes(TimeSpan.Parse(ForceCloseTime));
            double sessionEndSM      = ToSessionMinutes(TimeSpan.Parse(SessionEndTime));

            bool isWithinSession   = barSM >= tradeWindowStartSM && barSM < sessionEndSM;
            bool canEnterNewTrades = barSM >= tradeWindowStartSM && barSM <= lastEntrySM;
            // ForceClose: fire from forceCloseSM onwards (no upper bound).
            // Re-issues exit every bar until position is flat.
            // When flat, still return early to block new entries after ForceClose time.
            bool shouldForceClose = barSM >= forceCloseSM;

            if (shouldForceClose)
            {
                CancelAllOrders();
                ResetSignals();
                ExitAllPositions("ForceClose");
                return;
            }

            // ── Skip Time Window ─────────────────────────────────────────────
            if (IsSkipWindowConfigured())
            {
                bool nowInSkip = IsInSkipWindow(Time[0]);

                if (nowInSkip && !inSkipWindow)
                {
                    // Entering skip window
                    inSkipWindow = true;
                    Print(Time[0] + " - Skip window started (" + SkipTimeStart + " - " + SkipTimeEnd + "). No new entries.");
                    if (FlattenAtSkipStart)
                    {
                        CancelAllOrders();
                        ResetSignals();
                        ExitAllPositions("SkipWindow");
                        Print(Time[0] + " - Flatten at skip start: position and orders cleared.");
                    }
                    else
                    {
                        CancelPendingEntryWork();
                    }
                }
                else if (!nowInSkip && inSkipWindow)
                {
                    inSkipWindow = false;
                    Print(Time[0] + " - Skip window ended. Resuming normal trading.");
                }

                // Manage existing position during skip window normally, just block new entries
                if (inSkipWindow && Position.MarketPosition == MarketPosition.Flat)
                    return;
            }

            if (IsNewsWindowConfigured())
            {
                bool nowInNewsSkip = IsInNewsSkipWindow(Time[0]);

                if (nowInNewsSkip && !inNewsSkipWindow)
                {
                    inNewsSkipWindow = true;
                    DateTime newsWindowStart;
                    DateTime newsWindowEnd;
                    string newsLabel = TryGetNewsWindow(Time[0], out newsWindowStart, out newsWindowEnd)
                        ? newsWindowStart.AddMinutes(NewsBlockMinutes).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                        : "scheduled news";
                    Print(Time[0] + " - News skip window started around " + newsLabel + ". No new entries.");
                    if (FlattenAtNewsStart)
                    {
                        CancelAllOrders();
                        ResetSignals();
                        ExitAllPositions("NewsSkip");
                        Print(Time[0] + " - Flatten at news start: position and orders cleared.");
                    }
                    else
                    {
                        CancelPendingEntryWork();
                    }
                }
                else if (!nowInNewsSkip && inNewsSkipWindow)
                {
                    inNewsSkipWindow = false;
                    Print(Time[0] + " - News skip window ended. Resuming normal trading.");
                }

                if (inNewsSkipWindow && Position.MarketPosition == MarketPosition.Flat)
                    return;
            }

            if (!canEnterNewTrades)
            {
                if (entryOrder != null) { CancelOrder(entryOrder); ResetSignals(); }
                pendingLongFilterWait  = false;
                pendingShortFilterWait = false;
                pendingFilterBucket    = null;
            }

            // ── Global Daily P&L guards ──────────────────────────────────────
            double unrealizedPnL = 0;
            if (Position.MarketPosition == MarketPosition.Long)
                unrealizedPnL = (Close[0] - Position.AveragePrice) * Position.Quantity;
            else if (Position.MarketPosition == MarketPosition.Short)
                unrealizedPnL = (Position.AveragePrice - Close[0]) * Position.Quantity;

            double totalGlobalPnL = globalDailyPnL + unrealizedPnL;

            if (EnableGlobalMaxDailyLoss && totalGlobalPnL <= -GlobalMaxDailyLoss)
            {
                globalDailyLossLimitHit = true;
                CancelAllOrders(); ResetSignals();
                ExitAllPositions("GlobalMaxLoss");
                Print(Time[0] + " - GLOBAL Max daily LOSS limit hit: " + totalGlobalPnL + " pts. Trading halted.");
                return;
            }

            if (EnableGlobalMaxDailyProfit && totalGlobalPnL >= GlobalMaxDailyProfit)
            {
                globalDailyProfitLimitHit = true;
                CancelAllOrders(); ResetSignals();
                ExitAllPositions("GlobalMaxProfit");
                Print(Time[0] + " - GLOBAL Max daily PROFIT limit hit: " + totalGlobalPnL + " pts. Trading halted.");
                return;
            }

            if (globalDailyLossLimitHit || globalDailyProfitLimitHit || !isWithinSession)
                return;

            if (EnableGlobalMaxTradesPerDay && globalDailyTradeCount >= GlobalMaxTradesPerDay
                && Position.MarketPosition == MarketPosition.Flat)
                return;

            // ── Per-bucket P&L guard for active position ─────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && activeBucket != null)
            {
                double bucketTotalPnL = GetBucketDailyPnL(activeBucket) + unrealizedPnL;

                if (B_EnableMaxDailyLoss(activeBucket) && bucketTotalPnL <= -B_MaxDailyLoss(activeBucket))
                {
                    SetBucketLossHit(activeBucket, true);
                    CancelAllOrders(); ResetSignals();
                    ExitAllPositions(activeBucket + "_MaxLoss");
                    Print(Time[0] + " - " + activeBucket + " Max daily LOSS limit hit: " + bucketTotalPnL + " pts.");
                    return;
                }

                if (B_EnableMaxDailyProfit(activeBucket) && bucketTotalPnL >= B_MaxDailyProfit(activeBucket))
                {
                    SetBucketProfitHit(activeBucket, true);
                    CancelAllOrders(); ResetSignals();
                    ExitAllPositions(activeBucket + "_MaxProfit");
                    Print(Time[0] + " - " + activeBucket + " Max daily PROFIT limit hit: " + bucketTotalPnL + " pts.");
                    return;
                }
            }

            // ── EMA crossover detection (per-bucket EMAs) ────────────────────
            // For longs: check if Close crosses above EITHER L1 or L2 EMA
            // For shorts: check if Close crosses below EITHER S1 or S2 EMA
            bool crossAboveL1 = L1_Enable && CurrentBar >= L1_EmaLength && Close[0] > emaL1[0] && Close[1] <= emaL1[1];
            bool crossAboveL2 = L2_Enable && CurrentBar >= L2_EmaLength && Close[0] > emaL2[0] && Close[1] <= emaL2[1];
            bool crossBelowS1 = S1_Enable && CurrentBar >= S1_EmaLength && Close[0] < emaS1[0] && Close[1] >= emaS1[1];
            bool crossBelowS2 = S2_Enable && CurrentBar >= S2_EmaLength && Close[0] < emaS2[0] && Close[1] >= emaS2[1];

            bool anyLongCross  = crossAboveL1 || crossAboveL2;
            bool anyShortCross = crossBelowS1 || crossBelowS2;

            // ── Manage open position ─────────────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && activeBucket != null)
            {
                EMA activeEma = GetEma(activeBucket);

                // v1.06: Max Bars In Trade
                if (B_EnableMaxBarsInTrade(activeBucket) && entryBar > 0
                    && (CurrentBar - entryBar) >= B_MaxBarsInTrade(activeBucket))
                {
                    Print(Time[0] + " - " + activeBucket + " Max bars in trade reached ("
                        + (CurrentBar - entryBar) + " bars). Exiting.");
                    ExitAllPositions("MaxBarsExit");
                    return;
                }

                CheckBreakEven();

                // v1.06: SL to Entry on EMA Cross (per-bucket, one-way ratchet)
                CheckSLToEntryOnEMA(activeEma);

                if (B_StopLossType(activeBucket) == HugoTesting_StopLossTypeEnum.EMATrailing)
                    UpdateEMATrailingStop(activeEma);

                ManagePriceTrailingStop();

                if (B_TakeProfitType(activeBucket) == HugoTesting_TakeProfitTypeEnum.EMACross)
                {
                    if (Position.MarketPosition == MarketPosition.Long && Close[0] < activeEma[0] && Close[1] >= activeEma[1])
                    { ExitLong(BuildExitSignalName("EMACrossExit")); return; }
                    if (Position.MarketPosition == MarketPosition.Short && Close[0] > activeEma[0] && Close[1] <= activeEma[1])
                    { ExitShort(BuildExitSignalName("EMACrossExit")); return; }
                }

                // Opposite signal exit: use the active bucket's EMA
                if (Position.MarketPosition == MarketPosition.Long && Close[0] < activeEma[0] && Close[1] >= activeEma[1])
                    ExitLong(BuildExitSignalName("OppositeSignal"));
                else if (Position.MarketPosition == MarketPosition.Short && Close[0] > activeEma[0] && Close[1] <= activeEma[1])
                    ExitShort(BuildExitSignalName("OppositeSignal"));
            }

            // ── Cancel pending limit order on opposite signal ────────────────
            if (entryOrder != null)
            {
                if ((pendingDirection == 1 && anyShortCross) || (pendingDirection == -1 && anyLongCross))
                {
                    CancelOrder(entryOrder);
                    ResetSignals();
                }
            }

            // Cancel filter wait on opposite signal
            if (pendingLongFilterWait  && anyShortCross) { pendingLongFilterWait  = false; pendingFilterBucket = null; }
            if (pendingShortFilterWait && anyLongCross)  { pendingShortFilterWait = false; pendingFilterBucket = null; }

            // ── Pullback wait: expire on MaxBarsToWait ───────────────────────
            if (entryOrder != null && activeBucket != null)
            {
                int maxWait = B_MaxBarsToWait(activeBucket);
                if (maxWait > 0 && CurrentBar - signalBar >= maxWait)
                {
                    CancelOrder(entryOrder);
                    ResetSignals();
                }
            }

            // ── Only process entries when flat and no pending order ───────────
            if (Position.MarketPosition != MarketPosition.Flat || entryOrder != null || !canEnterNewTrades)
                return;

            if (EnableGlobalMaxTradesPerDay && globalDailyTradeCount >= GlobalMaxTradesPerDay)
                return;

            // ── Filter-wait resolution ───────────────────────────────────────
            if (pendingLongFilterWait && pendingFilterBucket != null)
            {
                if (CurrentBar - filterWaitStartBar >= B_FilterWaitMaxBars(pendingFilterBucket))
                {
                    pendingLongFilterWait = false;
                    pendingFilterBucket   = null;
                }
                else if (PassesSignalQualityFilters(true, pendingFilterBucket))
                {
                    string bucket = pendingFilterBucket;
                    pendingLongFilterWait  = false;
                    pendingFilterBucket    = null;
                    longSignalActive       = true;
                    signalBar              = CurrentBar;
                    crossoverCandleHigh    = High[0];
                    crossoverCandleLow     = Low[0];
                    signalEmaValue         = GetEma(bucket)[0];
                    pendingDirection       = 1;
                    activeBucket           = bucket;
                    ProcessLongEntry();
                    return;
                }
                return;
            }

            if (pendingShortFilterWait && pendingFilterBucket != null)
            {
                if (CurrentBar - filterWaitStartBar >= B_FilterWaitMaxBars(pendingFilterBucket))
                {
                    pendingShortFilterWait = false;
                    pendingFilterBucket    = null;
                }
                else if (PassesSignalQualityFilters(false, pendingFilterBucket))
                {
                    string bucket = pendingFilterBucket;
                    pendingShortFilterWait = false;
                    pendingFilterBucket    = null;
                    shortSignalActive      = true;
                    signalBar              = CurrentBar;
                    crossoverCandleHigh    = High[0];
                    crossoverCandleLow     = Low[0];
                    signalEmaValue         = GetEma(bucket)[0];
                    pendingDirection       = -1;
                    activeBucket           = bucket;
                    ProcessShortEntry();
                    return;
                }
                return;
            }

            // ── New signal detection ─────────────────────────────────────────
            double candleRange = High[0] - Low[0];

            // Block new entries during skip window
            if ((IsSkipWindowConfigured() && inSkipWindow) || (IsNewsWindowConfigured() && inNewsSkipWindow)) return;

            if (anyLongCross)
            {
                string bucket = ResolveBucket(1, candleRange);
                if (bucket == null)
                {
                    Print(Time[0] + " - Long signal: candle range " + candleRange.ToString("F2") + " pts — no matching bucket. Skipped.");
                    return;
                }

                if (!IsDirectionAllowed(1, bucket)) return;
                if (!PassesEmaSlopeFilter(true, bucket)) return;
                if (!PassesWMAFilter(true, bucket)) return;
                if (!PassesSMAFilter(true, bucket)) return;
                if (!PassesADXFilter(bucket)) return;

                crossoverCandleHigh = High[0];
                crossoverCandleLow  = Low[0];
                signalEmaValue      = GetEma(bucket)[0];

                if (!PassesSignalQualityFilters(true, bucket))
                {
                    if (B_EnableFilterWaitBars(bucket))
                    {
                        pendingLongFilterWait = true;
                        pendingFilterBucket   = bucket;
                        filterWaitStartBar    = CurrentBar;
                        Print(Time[0] + " - " + bucket + " Long signal: candle failed quality filters. Waiting up to " + B_FilterWaitMaxBars(bucket) + " bars.");
                    }
                    return;
                }

                longSignalActive  = true;
                shortSignalActive = false;
                signalBar         = CurrentBar;
                pendingDirection  = 1;
                activeBucket      = bucket;
                Print(Time[0] + " - " + bucket + " Long signal. Candle range: " + candleRange.ToString("F2") + " pts.");
                ProcessLongEntry();
            }
            else if (anyShortCross)
            {
                string bucket = ResolveBucket(-1, candleRange);
                if (bucket == null)
                {
                    Print(Time[0] + " - Short signal: candle range " + candleRange.ToString("F2") + " pts — no matching bucket. Skipped.");
                    return;
                }

                if (!IsDirectionAllowed(-1, bucket)) return;
                if (!PassesEmaSlopeFilter(false, bucket)) return;
                if (!PassesWMAFilter(false, bucket)) return;
                if (!PassesSMAFilter(false, bucket)) return;
                if (!PassesADXFilter(bucket)) return;

                crossoverCandleHigh = High[0];
                crossoverCandleLow  = Low[0];
                signalEmaValue      = GetEma(bucket)[0];

                if (!PassesSignalQualityFilters(false, bucket))
                {
                    if (B_EnableFilterWaitBars(bucket))
                    {
                        pendingShortFilterWait = true;
                        pendingFilterBucket    = bucket;
                        filterWaitStartBar     = CurrentBar;
                        Print(Time[0] + " - " + bucket + " Short signal: candle failed quality filters. Waiting up to " + B_FilterWaitMaxBars(bucket) + " bars.");
                    }
                    return;
                }

                shortSignalActive = true;
                longSignalActive  = false;
                signalBar         = CurrentBar;
                pendingDirection  = -1;
                activeBucket      = bucket;
                Print(Time[0] + " - " + bucket + " Short signal. Candle range: " + candleRange.ToString("F2") + " pts.");
                ProcessShortEntry();
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Price Trailing Stop — Uses active bucket's parameters
        // ════════════════════════════════════════════════════════════════════════

        private void ManagePriceTrailingStop()
        {
            if (activeBucket == null) return;
            if (!B_EnablePriceTrail(activeBucket)) return;
            if (Position.MarketPosition == MarketPosition.Flat) return;
            if (trailLocked) return;

            bool isLong = Position.MarketPosition == MarketPosition.Long;

            if (isLong)
                bestPriceSinceEntry = Math.Max(bestPriceSinceEntry, High[0]);
            else
                bestPriceSinceEntry = Math.Min(bestPriceSinceEntry, Low[0]);

            double profit = isLong
                ? bestPriceSinceEntry - entryPrice
                : entryPrice - bestPriceSinceEntry;

            // Activation check
            if (!trailActivated)
            {
                double activationThreshold = GetTrailThreshold(B_TrailActivationValue(activeBucket));
                if (B_TrailActivationValue(activeBucket) <= 0 || profit >= activationThreshold)
                {
                    trailActivated = true;
                    Print(Time[0] + " - " + activeBucket + " Price trail ACTIVATED. Profit: " + profit.ToString("F2") + " pts.");
                }
                else return;
            }

            // Lock check
            if (B_EnableTrailLock(activeBucket) && B_TrailLockValue(activeBucket) > 0)
            {
                double lockThreshold = GetTrailThreshold(B_TrailLockValue(activeBucket));
                if (profit >= lockThreshold)
                {
                    trailLocked = true;
                    Print(Time[0] + " - " + activeBucket + " Price trail LOCKED. Stop frozen at: " + currentStopPrice.ToString("F2"));
                    return;
                }
            }

            // Trail distance
            double trailDist = B_TrailDistanceMode(activeBucket) == HugoTesting_TrailDistanceModeEnum.FixedPoints
                ? B_TrailDistanceValue(activeBucket)
                : (initialStopDistance > 0
                    ? initialStopDistance * B_TrailDistanceValue(activeBucket) / 100.0
                    : B_TrailDistanceValue(activeBucket));

            if (trailDist <= 0) return;

            double newStop = isLong
                ? Instrument.MasterInstrument.RoundToTickSize(bestPriceSinceEntry - trailDist)
                : Instrument.MasterInstrument.RoundToTickSize(bestPriceSinceEntry + trailDist);

            // Step filter
            if (B_TrailStepTicks(activeBucket) > 0)
            {
                double stepDist = B_TrailStepTicks(activeBucket) * TickSize;
                double moveDist = isLong ? newStop - currentStopPrice : currentStopPrice - newStop;
                if (moveDist < stepDist) return;
            }

            bool improves = isLong ? newStop > currentStopPrice : newStop < currentStopPrice;
            if (!improves) return;

            if (!TryUpdateProtectiveStop(Position.MarketPosition, newStop, "Trail stop"))
                return;

            Print(Time[0] + " - " + activeBucket + " Trail stop -> " + currentStopPrice.ToString("F2")
                + " | Best: " + bestPriceSinceEntry.ToString("F2")
                + " | Profit: " + profit.ToString("F2") + " pts");
        }

        private double GetTrailThreshold(double value)
        {
            if (activeBucket == null) return value;
            if (B_TrailActivationMode(activeBucket) == HugoTesting_TrailActivationModeEnum.PctOfRRTarget && initialTPDistance > 0)
                return initialTPDistance * value / 100.0;
            return value;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Filter Methods — Now bucket-aware
        // ════════════════════════════════════════════════════════════════════════

        private bool IsDirectionAllowed(int signalDirection, string bucket)
        {
            if (!B_EnableDirectionFlip(bucket))                                                    return true;
            if (lastTradeDirection == 0)                                                           return true;
            if (signalDirection != lastTradeDirection)                                             return true;
            if (B_EnableSameDirectionOnLoss(bucket) && lastTradePnL < -B_SameDirectionLossThreshold(bucket)) return true;
            return false;
        }

        private bool PassesEmaSlopeFilter(bool isLong, string bucket)
        {
            if (!B_EnableEmaSlope(bucket)) return true;
            int slopeBars = B_EmaSlopeBars(bucket);
            if (CurrentBar < slopeBars) return false;

            EMA e = GetEma(bucket);
            double emaNow  = e[0];
            double emaThen = e[slopeBars];
            if (emaThen == 0) return false;

            double slopePct = (emaNow - emaThen) / emaThen * 100.0;

            switch (B_EmaSlopeMode(bucket))
            {
                case HugoTesting_EmaSlopeModeEnum.DirectionEnforced:
                    return isLong ? slopePct >= B_EmaSlopeMinPct(bucket) : slopePct <= -B_EmaSlopeMinPct(bucket);
                case HugoTesting_EmaSlopeModeEnum.MagnitudeOnly:
                    return Math.Abs(slopePct) >= B_EmaSlopeMinPct(bucket);
                default:
                    return true;
            }
        }

        private bool PassesSignalQualityFilters(bool isLong, string bucket)
        {
            double candleRange = High[0] - Low[0];
            double bodySize    = Math.Abs(Close[0] - Open[0]);

            if (B_EnableCandleSizeFilter(bucket))
            {
                if (candleRange < B_MinCandleSize(bucket)) return false;
                if (B_MaxCandleSize(bucket) > 0 && candleRange > B_MaxCandleSize(bucket)) return false;
            }

            if (B_EnableBodyPctFilter(bucket))
            {
                if (candleRange <= 0) return false;
                if ((bodySize / candleRange) * 100.0 < B_MinBodyPct(bucket)) return false;
            }

            if (B_EnableCloseDistanceFilter(bucket))
            {
                EMA e = GetEma(bucket);
                double closeDist = isLong ? Close[0] - e[0] : e[0] - Close[0];
                if (closeDist < B_MinCloseDistance(bucket)) return false;
            }

            return true;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Entry Processing — Uses active bucket's parameters
        // ════════════════════════════════════════════════════════════════════════

        private void ProcessLongEntry()
        {
            string b = activeBucket;
            EMA e    = GetEma(b);

            currentTradeDirection = 1;
            double stopLoss   = CalculateStopLoss(true, b, e);
            double takeProfit = CalculateTakeProfitFromEntry(true, Close[0], stopLoss, b);

            if (B_MaxStopLoss(b) > 0)
            {
                double entryEst = B_EntryType(b) == HugoTesting_EntryTypeEnum.Immediate ? Close[0] : e[0] - B_PullbackOffset(b);
                double slDist   = entryEst - stopLoss;
                if (slDist > B_MaxStopLoss(b)) stopLoss = entryEst - B_MaxStopLoss(b);
            }

            if (B_EntryType(b) == HugoTesting_EntryTypeEnum.Immediate)
            {
                double targetPrice = B_TakeProfitType(b) != HugoTesting_TakeProfitTypeEnum.EMACross ? takeProfit : 0;
                if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", Close[0], ContractQuantity))
                {
                    Print(Time[0] + " - Long entry confirmation declined.");
                    ResetSignals();
                    activeBucket = null;
                    currentTradeDirection = 0;
                    currentTargetPrice = 0;
                    return;
                }

                entryPrice          = Close[0];
                currentStopPrice    = stopLoss;
                currentTargetPrice  = targetPrice;
                breakEvenMoved      = false;
                entryBar            = CurrentBar;
                slMovedToEntryOnEMA = false;

                initialStopDistance = entryPrice - stopLoss;
                initialTPDistance   = B_TakeProfitType(b) != HugoTesting_TakeProfitTypeEnum.EMACross
                    ? takeProfit - entryPrice : 0;
                ResetTrailState();

                managedEntrySignal = BuildManagedEntrySignal(1);
                ConfigureInitialProtectiveOrders(managedEntrySignal, stopLoss, currentTargetPrice);
                EnterLong(ContractQuantity, managedEntrySignal);
            }
            else
            {
                double limitPrice = e[0] - B_PullbackOffset(b);
                if (!CanPlaceLongLimitSafely(limitPrice, "Long pullback"))
                {
                    ResetSignals();
                    activeBucket = null;
                    currentTradeDirection = 0;
                    currentTargetPrice = 0;
                    return;
                }

                if (B_StopLossType(b) == HugoTesting_StopLossTypeEnum.Fixed)
                    stopLoss = limitPrice - B_FixedStopLoss(b);
                else if (B_StopLossType(b) == HugoTesting_StopLossTypeEnum.EMAFixed || B_StopLossType(b) == HugoTesting_StopLossTypeEnum.EMATrailing)
                    stopLoss = e[0] - B_StopLossOffset(b);

                if (B_MaxStopLoss(b) > 0)
                {
                    double slDist = limitPrice - stopLoss;
                    if (slDist > B_MaxStopLoss(b)) stopLoss = limitPrice - B_MaxStopLoss(b);
                }

                if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", limitPrice, ContractQuantity))
                {
                    Print(Time[0] + " - Long entry confirmation declined.");
                    ResetSignals();
                    activeBucket = null;
                    currentTradeDirection = 0;
                    currentTargetPrice = 0;
                    return;
                }

                currentStopPrice    = stopLoss;
                entryPrice          = limitPrice;
                breakEvenMoved      = false;
                entryBar            = CurrentBar;
                slMovedToEntryOnEMA = false;

                currentTargetPrice  = 0;
                if (B_TakeProfitType(b) != HugoTesting_TakeProfitTypeEnum.EMACross)
                {
                    takeProfit = CalculateTakeProfitFromEntry(true, limitPrice, stopLoss, b);
                    currentTargetPrice = takeProfit;
                }

                initialStopDistance = limitPrice - stopLoss;
                initialTPDistance   = B_TakeProfitType(b) != HugoTesting_TakeProfitTypeEnum.EMACross
                    ? takeProfit - limitPrice : 0;
                ResetTrailState();

                managedEntrySignal = BuildManagedEntrySignal(1);
                ConfigureInitialProtectiveOrders(managedEntrySignal, stopLoss, currentTargetPrice);
                entryOrder = EnterLongLimit(ContractQuantity, limitPrice, managedEntrySignal);
            }
        }

        private void ProcessShortEntry()
        {
            string b = activeBucket;
            EMA e    = GetEma(b);

            currentTradeDirection = -1;
            double stopLoss   = CalculateStopLoss(false, b, e);
            double takeProfit = CalculateTakeProfitFromEntry(false, Close[0], stopLoss, b);

            if (B_MaxStopLoss(b) > 0)
            {
                double entryEst = B_EntryType(b) == HugoTesting_EntryTypeEnum.Immediate ? Close[0] : e[0] + B_PullbackOffset(b);
                double slDist   = stopLoss - entryEst;
                if (slDist > B_MaxStopLoss(b)) stopLoss = entryEst + B_MaxStopLoss(b);
            }

            if (B_EntryType(b) == HugoTesting_EntryTypeEnum.Immediate)
            {
                double targetPrice = B_TakeProfitType(b) != HugoTesting_TakeProfitTypeEnum.EMACross ? takeProfit : 0;
                if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", Close[0], ContractQuantity))
                {
                    Print(Time[0] + " - Short entry confirmation declined.");
                    ResetSignals();
                    activeBucket = null;
                    currentTradeDirection = 0;
                    currentTargetPrice = 0;
                    return;
                }

                entryPrice          = Close[0];
                currentStopPrice    = stopLoss;
                currentTargetPrice  = targetPrice;
                breakEvenMoved      = false;
                entryBar            = CurrentBar;
                slMovedToEntryOnEMA = false;

                initialStopDistance = stopLoss - entryPrice;
                initialTPDistance   = B_TakeProfitType(b) != HugoTesting_TakeProfitTypeEnum.EMACross
                    ? entryPrice - takeProfit : 0;
                ResetTrailState();

                managedEntrySignal = BuildManagedEntrySignal(-1);
                ConfigureInitialProtectiveOrders(managedEntrySignal, stopLoss, currentTargetPrice);
                EnterShort(ContractQuantity, managedEntrySignal);
            }
            else
            {
                double limitPrice = e[0] + B_PullbackOffset(b);
                if (!CanPlaceShortLimitSafely(limitPrice, "Short pullback"))
                {
                    ResetSignals();
                    activeBucket = null;
                    currentTradeDirection = 0;
                    currentTargetPrice = 0;
                    return;
                }

                if (B_StopLossType(b) == HugoTesting_StopLossTypeEnum.Fixed)
                    stopLoss = limitPrice + B_FixedStopLoss(b);
                else if (B_StopLossType(b) == HugoTesting_StopLossTypeEnum.EMAFixed || B_StopLossType(b) == HugoTesting_StopLossTypeEnum.EMATrailing)
                    stopLoss = e[0] + B_StopLossOffset(b);

                if (B_MaxStopLoss(b) > 0)
                {
                    double slDist = stopLoss - limitPrice;
                    if (slDist > B_MaxStopLoss(b)) stopLoss = limitPrice + B_MaxStopLoss(b);
                }

                if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", limitPrice, ContractQuantity))
                {
                    Print(Time[0] + " - Short entry confirmation declined.");
                    ResetSignals();
                    activeBucket = null;
                    currentTradeDirection = 0;
                    currentTargetPrice = 0;
                    return;
                }

                currentStopPrice    = stopLoss;
                entryPrice          = limitPrice;
                breakEvenMoved      = false;
                entryBar            = CurrentBar;
                slMovedToEntryOnEMA = false;

                currentTargetPrice  = 0;
                if (B_TakeProfitType(b) != HugoTesting_TakeProfitTypeEnum.EMACross)
                {
                    takeProfit = CalculateTakeProfitFromEntry(false, limitPrice, stopLoss, b);
                    currentTargetPrice = takeProfit;
                }

                initialStopDistance = stopLoss - limitPrice;
                initialTPDistance   = B_TakeProfitType(b) != HugoTesting_TakeProfitTypeEnum.EMACross
                    ? limitPrice - takeProfit : 0;
                ResetTrailState();

                managedEntrySignal = BuildManagedEntrySignal(-1);
                ConfigureInitialProtectiveOrders(managedEntrySignal, stopLoss, currentTargetPrice);
                entryOrder = EnterShortLimit(ContractQuantity, limitPrice, managedEntrySignal);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Stop Loss / Take Profit Calculations — Bucket-aware
        // ════════════════════════════════════════════════════════════════════════

        private double CalculateStopLoss(bool isLong, string b, EMA e)
        {
            switch (B_StopLossType(b))
            {
                case HugoTesting_StopLossTypeEnum.Fixed:
                    return isLong ? Close[0] - B_FixedStopLoss(b) : Close[0] + B_FixedStopLoss(b);
                case HugoTesting_StopLossTypeEnum.CandleHighLow:
                    return isLong ? crossoverCandleLow - B_StopLossOffset(b) : crossoverCandleHigh + B_StopLossOffset(b);
                case HugoTesting_StopLossTypeEnum.EMAFixed:
                case HugoTesting_StopLossTypeEnum.EMATrailing:
                    return isLong ? e[0] - B_StopLossOffset(b) : e[0] + B_StopLossOffset(b);
                case HugoTesting_StopLossTypeEnum.CandleMultiple:
                {
                    double candleRange = crossoverCandleHigh - crossoverCandleLow;
                    double slDist = candleRange * B_SLCandleMultiplier(b);
                    return isLong ? Close[0] - slDist : Close[0] + slDist;
                }
                default:
                    return isLong ? Close[0] - B_FixedStopLoss(b) : Close[0] + B_FixedStopLoss(b);
            }
        }

        private double CalculateTakeProfitFromEntry(bool isLong, double entry, double stopLoss, string b)
        {
            double risk = isLong ? entry - stopLoss : stopLoss - entry;
            switch (B_TakeProfitType(b))
            {
                case HugoTesting_TakeProfitTypeEnum.RiskReward:
                    return isLong ? entry + risk * B_RiskRewardRatio(b) : entry - risk * B_RiskRewardRatio(b);
                case HugoTesting_TakeProfitTypeEnum.Fixed:
                    return isLong ? entry + B_FixedTakeProfit(b) : entry - B_FixedTakeProfit(b);
                case HugoTesting_TakeProfitTypeEnum.EMACross:
                    return isLong ? entry + 1000 : entry - 1000;
                case HugoTesting_TakeProfitTypeEnum.CandleMultiple:
                {
                    double candleRange = crossoverCandleHigh - crossoverCandleLow;
                    return isLong ? entry + candleRange * B_TPCandleMultiplier(b)
                                 : entry - candleRange * B_TPCandleMultiplier(b);
                }
                default:
                    return isLong ? entry + B_FixedTakeProfit(b) : entry - B_FixedTakeProfit(b);
            }
        }


        // ════════════════════════════════════════════════════════════════════════
        //  v1.07: Entry Filters — WMA, SMA, ADX
        // ════════════════════════════════════════════════════════════════════════

        private bool PassesWMAFilter(bool isLong, string bucket)
        {
            if (!B_EnableWMAFilter(bucket)) return true;
            WMA w = GetWma(bucket);
            return isLong ? Close[0] > w[0] : Close[0] < w[0];
        }

        private bool PassesSMAFilter(bool isLong, string bucket)
        {
            if (!B_EnableSMAFilter(bucket)) return true;
            SMA s = GetSma(bucket);
            return isLong ? Close[0] > s[0] : Close[0] < s[0];
        }

        private bool PassesADXFilter(string bucket)
        {
            if (!B_EnableADXFilter(bucket)) return true;
            double val = GetAdx(bucket)[0];
            return val >= B_ADXMin(bucket) && val <= B_ADXMax(bucket);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Break-Even & EMA Trailing Stop — Bucket-aware
        // ════════════════════════════════════════════════════════════════════════

        private void CheckBreakEven()
        {
            if (activeBucket == null) return;
            if (!B_EnableBreakEven(activeBucket) || breakEvenMoved || Position.MarketPosition == MarketPosition.Flat)
                return;

            double profit = 0, risk = 0;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                profit = Close[0] - Position.AveragePrice;
                risk   = Position.AveragePrice - currentStopPrice;
            }
            else
            {
                profit = Position.AveragePrice - Close[0];
                risk   = currentStopPrice - Position.AveragePrice;
            }

            bool trigger = B_BreakEvenTriggerType(activeBucket) == HugoTesting_BreakEvenTriggerEnum.Points
                ? profit >= B_BreakEvenTriggerValue(activeBucket)
                : risk > 0 && (profit / risk) >= B_BreakEvenTriggerValue(activeBucket);

            if (!trigger) return;

            double newStop = Position.MarketPosition == MarketPosition.Long
                ? Position.AveragePrice + B_BreakEvenOffset(activeBucket)
                : Position.AveragePrice - B_BreakEvenOffset(activeBucket);

            if (!TryUpdateProtectiveStop(Position.MarketPosition, newStop, "Break Even"))
                return;

            breakEvenMoved = true;
            Print(Time[0] + " - " + activeBucket + " Break Even triggered. New stop: " + currentStopPrice);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  v1.06: SL to Entry on EMA Cross — Bucket-aware, one-way ratchet
        // ════════════════════════════════════════════════════════════════════════

        private void CheckSLToEntryOnEMA(EMA e)
        {
            if (activeBucket == null) return;
            if (!B_EnableSLToEntryOnEMA(activeBucket)) return;
            if (slMovedToEntryOnEMA) return;  // already fired once, do not move back
            if (Position.MarketPosition == MarketPosition.Flat) return;

            bool isLong  = Position.MarketPosition == MarketPosition.Long;
            bool emaCrossedPrice = isLong
                ? e[0] > Close[0] && e[1] <= Close[1]   // EMA crossed above price (bearish for long)
                : e[0] < Close[0] && e[1] >= Close[1];  // EMA crossed below price (bullish for short)

            if (!emaCrossedPrice) return;

            double offset   = B_SLToEntryOffset(activeBucket);
            double newStop  = isLong
                ? entryPrice + offset
                : entryPrice - offset;

            // Only move stop if it improves (protects capital)
            bool improves = isLong ? newStop > currentStopPrice : newStop < currentStopPrice;
            if (!improves) return;

            if (!TryUpdateProtectiveStop(Position.MarketPosition, newStop, "SL to entry on EMA cross"))
                return;

            slMovedToEntryOnEMA = true;
            Print(Time[0] + " - " + activeBucket + " SL moved to entry on EMA cross. New stop: "
                + currentStopPrice.ToString("F2") + " (offset: " + offset + " pts)");
        }

        private void UpdateEMATrailingStop(EMA e)
        {
            if (breakEvenMoved) return;
            if (activeBucket == null) return;

            double newStop;
            if (Position.MarketPosition == MarketPosition.Long)
            {
                newStop = e[0] - B_StopLossOffset(activeBucket);
                if (newStop > currentStopPrice)
                    TryUpdateProtectiveStop(Position.MarketPosition, newStop, "EMA trail");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                newStop = e[0] + B_StopLossOffset(activeBucket);
                if (newStop < currentStopPrice)
                    TryUpdateProtectiveStop(Position.MarketPosition, newStop, "EMA trail");
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════════════

        private void ResetTrailState()
        {
            bestPriceSinceEntry = entryPrice;
            trailActivated      = false;
            trailLocked         = false;
        }

        private void CancelPendingEntryWork()
        {
            if (entryOrder != null)
            {
                CancelOrder(entryOrder);
                entryOrder = null;
            }

            pendingLongFilterWait  = false;
            pendingShortFilterWait = false;
            pendingFilterBucket    = null;
        }

        private double GetReferencePriceForProtectiveStop(MarketPosition positionDirection)
        {
            if (positionDirection == MarketPosition.Long)
            {
                double bid = GetCurrentBid();
                if (bid > 0)
                    return bid;
            }
            else if (positionDirection == MarketPosition.Short)
            {
                double ask = GetCurrentAsk();
                if (ask > 0)
                    return ask;
            }

            return Close[0];
        }

        private bool CanAmendProtectiveStopForCurrentMarket(MarketPosition positionDirection, double proposedStopPrice)
        {
            if (double.IsNaN(proposedStopPrice) || double.IsInfinity(proposedStopPrice) || proposedStopPrice <= 0)
                return false;

            double referencePrice = GetReferencePriceForProtectiveStop(positionDirection);
            if (double.IsNaN(referencePrice) || double.IsInfinity(referencePrice) || referencePrice <= 0)
                return true;

            if (positionDirection == MarketPosition.Long)
                return proposedStopPrice < referencePrice;
            if (positionDirection == MarketPosition.Short)
                return proposedStopPrice > referencePrice;

            return true;
        }

        private bool TryUpdateProtectiveStop(MarketPosition positionDirection, double proposedStopPrice, string context)
        {
            double roundedStop = Instrument.MasterInstrument.RoundToTickSize(proposedStopPrice);
            if (State == State.Realtime && !CanAmendProtectiveStopForCurrentMarket(positionDirection, roundedStop))
            {
                Print(Time[0] + " - " + (activeBucket ?? "?") + " " + context + " skipped: proposed stop "
                    + roundedStop.ToString("F2") + " is on the wrong side of market for " + positionDirection + ".");
                return false;
            }

            SetStopLoss(CalculationMode.Price, roundedStop);
            currentStopPrice = roundedStop;
            return true;
        }

        private bool CanPlaceLongLimitSafely(double limitPrice, string context)
        {
            if (State != State.Realtime)
                return true;

            double referencePrice = GetCurrentAsk();
            if (referencePrice <= 0 || double.IsNaN(referencePrice) || double.IsInfinity(referencePrice))
                referencePrice = Close[0];

            if (referencePrice <= limitPrice)
            {
                Print(Time[0] + " - " + (activeBucket ?? "?") + " " + context + " skipped: price "
                    + referencePrice.ToString("F2") + " already <= limit " + limitPrice.ToString("F2") + ".");
                return false;
            }

            return true;
        }

        private bool CanPlaceShortLimitSafely(double limitPrice, string context)
        {
            if (State != State.Realtime)
                return true;

            double referencePrice = GetCurrentBid();
            if (referencePrice <= 0 || double.IsNaN(referencePrice) || double.IsInfinity(referencePrice))
                referencePrice = Close[0];

            if (referencePrice >= limitPrice)
            {
                Print(Time[0] + " - " + (activeBucket ?? "?") + " " + context + " skipped: price "
                    + referencePrice.ToString("F2") + " already >= limit " + limitPrice.ToString("F2") + ".");
                return false;
            }

            return true;
        }

        private void CancelAllOrders()
        {
            if (entryOrder != null) { CancelOrder(entryOrder); entryOrder = null; }
        }

        private void ResetSignals()
        {
            longSignalActive       = false;
            shortSignalActive      = false;
            pendingDirection       = 0;
            entryOrder             = null;
            pendingLongFilterWait  = false;
            pendingShortFilterWait = false;
            pendingFilterBucket    = null;
        }

        private bool ShowEntryConfirmation(string orderType, double price, int quantity)
        {
            if (State != State.Realtime)
                return true;

            bool result = false;
            if (System.Windows.Application.Current == null)
                return false;

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                string message = string.Format(CultureInfo.InvariantCulture,
                    "Confirm {0} entry?\nPrice: {1:F2}\nQuantity: {2}",
                    orderType,
                    price,
                    quantity);
                MessageBoxResult response = System.Windows.MessageBox.Show(
                    message,
                    "Entry Confirmation",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);
                result = response == System.Windows.MessageBoxResult.Yes;
            });

            return result;
        }

        private bool TryGetEntryWebhookAction(Execution execution, out string action, out bool isMarketEntry)
        {
            action = null;
            isMarketEntry = false;

            if (execution == null || execution.Order == null)
                return false;

            string orderName = execution.Order.Name ?? string.Empty;
            if (!IsLongEntryOrderName(orderName) && !IsShortEntryOrderName(orderName))
                return false;

            action = IsLongEntryOrderName(orderName) ? "buy" : "sell";
            OrderType orderType = execution.Order.OrderType;
            isMarketEntry = orderType == OrderType.Market || orderType == OrderType.StopMarket;
            return true;
        }

        private bool ShouldSendExitWebhook(Execution execution, string orderName, MarketPosition marketPosition)
        {
            if (execution == null || execution.Order == null)
                return false;

            if (IsLongEntryOrderName(orderName) || IsShortEntryOrderName(orderName))
                return false;

            string fromEntry = execution.Order.FromEntrySignal ?? string.Empty;
            if (IsLongEntryOrderName(fromEntry) || IsShortEntryOrderName(fromEntry))
                return true;

            return marketPosition == MarketPosition.Flat;
        }

        private int GetDefaultWebhookQuantity()
        {
            return Math.Max(1, ContractQuantity);
        }

        private void SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime)
                return;
            if (string.IsNullOrWhiteSpace(WebhookUrl))
                return;

            try
            {
                int orderQty = quantityOverride > 0 ? quantityOverride : GetDefaultWebhookQuantity();
                string ticker = !string.IsNullOrWhiteSpace(WebhookTickerOverride)
                    ? WebhookTickerOverride.Trim()
                    : (Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "UNKNOWN");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
                string json = string.Empty;
                string action = (eventType ?? string.Empty).ToLowerInvariant();

                if (action == "buy" || action == "sell")
                {
                    string takeProfitPart = takeProfit > 0
                        ? string.Format(CultureInfo.InvariantCulture, ",\"takeProfit\":{{\"limitPrice\":{0}}}", takeProfit)
                        : string.Empty;
                    string stopLossPart = stopLoss > 0
                        ? string.Format(CultureInfo.InvariantCulture, ",\"stopLoss\":{{\"type\":\"stop\",\"stopPrice\":{0}}}", stopLoss)
                        : string.Empty;

                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"{1}\",\"orderType\":\"{2}\",\"quantityType\":\"fixed_quantity\",\"quantity\":{3},\"signalPrice\":{4},\"time\":\"{5}\"{6}{7}}}",
                        ticker,
                        action,
                        isMarketEntry ? "market" : "limit",
                        orderQty,
                        entryPrice,
                        timestamp,
                        takeProfitPart,
                        stopLossPart);
                }
                else if (action == "exit")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"exit\",\"orderType\":\"market\",\"quantityType\":\"fixed_quantity\",\"quantity\":{1},\"cancel\":true,\"time\":\"{2}\"}}",
                        ticker,
                        orderQty,
                        timestamp);
                }
                else if (action == "cancel")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"cancel\",\"time\":\"{1}\"}}",
                        ticker,
                        timestamp);
                }

                if (string.IsNullOrWhiteSpace(json))
                    return;

                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.UploadString(WebhookUrl, "POST", json);
                }
            }
            catch
            {
            }
        }

        private void UpdateInfoBox()
        {
            if (State != State.Realtime && State != State.Historical)
                return;
            if (ChartControl == null || ChartControl.Dispatcher == null)
                return;

            var lines = BuildInfoLines();
            ChartControl.Dispatcher.InvokeAsync(() => RenderInfoBoxOverlay(lines));
        }

        private void RenderInfoBoxOverlay(List<(string label, string value, Brush labelBrush, Brush valueBrush)> lines)
        {
            if (!EnsureInfoBoxOverlay() || infoBoxRowsPanel == null)
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

                string label = lines[i].label ?? string.Empty;
                string value = lines[i].value ?? string.Empty;

                text.Inlines.Add(new Run(label)
                {
                    Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : (lines[i].labelBrush ?? InfoLabelBrush)
                });

                if (!string.IsNullOrEmpty(value))
                {
                    Brush valueBrush = lines[i].valueBrush ?? InfoValueBrush;
                    text.Inlines.Add(new Run(" ")
                    {
                        Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : (lines[i].labelBrush ?? InfoLabelBrush)
                    });
                    text.Inlines.Add(new Run(value) { Foreground = valueBrush });
                }

                rowBorder.Child = text;
                infoBoxRowsPanel.Children.Add(rowBorder);
            }
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

            infoBoxRowsPanel = new StackPanel { Orientation = Orientation.Vertical };
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

        private List<(string label, string value, Brush labelBrush, Brush valueBrush)> BuildInfoLines()
        {
            var lines = new List<(string label, string value, Brush labelBrush, Brush valueBrush)>();
            lines.Add((string.Format("HUGO v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(("Contracts:", ContractQuantity.ToString(CultureInfo.InvariantCulture), Brushes.LightGray, InfoValueBrush));
            if (!UseNewsSkip)
            {
                lines.Add(("News:", "Disabled", Brushes.LightGray, InfoValueBrush));
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
                        string value = newsTime.ToString("ddd", CultureInfo.InvariantCulture) + " " + newsTime.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
                        Brush newsBrush = blockPassed ? PassedNewsRowBrush : InfoValueBrush;
                        lines.Add(("News:", value, newsBrush, newsBrush));
                    }
                }
            }
            lines.Add(("Current Session:", GetCurrentSessionInfoText(Time[0]), Brushes.LightGray, InfoValueBrush));
            lines.Add(("AutoEdge Systems™", string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            return lines;
        }

        private void EnsureNewsDatesInitialized()
        {
            if (newsDatesInitialized)
                return;

            NewsDates.Clear();
            LoadHardcodedNewsDates();
            NewsDates.Sort();
            newsDatesInitialized = true;
        }

        private void LoadHardcodedNewsDates()
        {
            if (string.IsNullOrWhiteSpace(NewsDatesRaw))
                return;

            string[] entries = NewsDatesRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                DateTime parsed;
                if (!DateTime.TryParse(entries[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
                    continue;
                if (!NewsDates.Contains(parsed))
                    NewsDates.Add(parsed);
            }
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

        private string GetCurrentSessionInfoText(DateTime time)
        {
            TimeSpan now = time.TimeOfDay;
            TimeSpan asiaStart = new TimeSpan(19, 0, 0);
            TimeSpan londonStart = new TimeSpan(2, 7, 0);
            TimeSpan newYorkStart = new TimeSpan(9, 42, 0);

            if (now >= asiaStart || now < londonStart)
                return "Asia";
            if (now >= londonStart && now < newYorkStart)
                return "London";
            return "New York";
        }

        private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            try
            {
                if (brush.CanFreeze)
                    brush.Freeze();
            }
            catch
            {
            }

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
            catch
            {
            }

            return brush;
        }

        private string GetAddOnVersion()
        {
            Assembly assembly = GetType().Assembly;
            Version version = assembly.GetName().Version;
            return version != null ? version.ToString() : "0.0.0.0";
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Order / Execution Callbacks
        // ════════════════════════════════════════════════════════════════════════

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null)
                return;

            if (IsLongEntryOrderName(order.Name) || IsShortEntryOrderName(order.Name))
            {
                if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                {
                    SendWebhook("cancel");
                    entryOrder = null;
                    ResetSignals();
                    activeBucket = null;
                    currentTradeDirection = 0;
                    currentTargetPrice = 0;
                    if (string.Equals(order.Name ?? string.Empty, managedEntrySignal ?? string.Empty, StringComparison.Ordinal))
                        managedEntrySignal = string.Empty;
                }
                else if (orderState == OrderState.Filled)
                {
                    entryOrder  = null;
                    managedEntrySignal = order.Name ?? managedEntrySignal;
                    entryPrice  = averageFillPrice;
                    bestPriceSinceEntry = averageFillPrice;
                    if (filled < quantity)
                        Print(Time[0] + " - Partial fill: " + filled + " of " + quantity);
                }
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            string entryAction;
            bool isMarketEntry;
            if (TryGetEntryWebhookAction(execution, out entryAction, out isMarketEntry))
            {
                managedEntrySignal = execution.Order.Name ?? managedEntrySignal;
                SendWebhook(entryAction, price, currentTargetPrice, currentStopPrice, isMarketEntry, quantity);
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            double tradePnL = 0;
            if (SystemPerformance != null && SystemPerformance.AllTrades.Count > 0)
                tradePnL = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1].ProfitPoints;

            // Update global P&L
            globalDailyPnL        += tradePnL;
            globalDailyTradeCount += 1;

            // Update per-bucket P&L
            if (activeBucket != null)
            {
                AddBucketDailyPnL(activeBucket, tradePnL);
                IncBucketTradeCount(activeBucket);
            }

            // Update global direction flip state
            lastTradeDirection = currentTradeDirection;
            lastTradePnL       = tradePnL;

            Print(Time[0] + " - " + (activeBucket ?? "?") + " Trade #" + globalDailyTradeCount
                + " closed. Direction: " + (lastTradeDirection == 1 ? "Long" : "Short")
                + ". P&L: " + tradePnL + " pts. Global Daily P&L: " + globalDailyPnL + " pts."
                + (activeBucket != null ? " Bucket P&L: " + GetBucketDailyPnL(activeBucket) + " pts." : ""));

            if (ShouldSendExitWebhook(execution, execution.Order.Name ?? string.Empty, marketPosition))
                SendWebhook("exit", 0, 0, 0, true, quantity);

            ResetSignals();
            breakEvenMoved      = false;
            slMovedToEntryOnEMA = false;
            entryBar            = 0;
            ResetTrailState();
            initialStopDistance = 0;
            initialTPDistance   = 0;
            currentTargetPrice  = 0;
            activeBucket        = null;
            managedEntrySignal  = string.Empty;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Enums (unchanged from v1.03)
    // ════════════════════════════════════════════════════════════════════════
    public enum HugoTesting_EntryTypeEnum        { Immediate, WaitForPullback }
    public enum HugoTesting_StopLossTypeEnum     { Fixed, CandleHighLow, EMAFixed, EMATrailing, CandleMultiple }
    public enum HugoTesting_TakeProfitTypeEnum   { RiskReward, Fixed, EMACross, CandleMultiple }
    public enum HugoTesting_BreakEvenTriggerEnum { Points, RiskMultiple }
    public enum HugoTesting_EmaSlopeModeEnum     { DirectionEnforced, MagnitudeOnly }
    public enum HugoTesting_TrailDistanceModeEnum   { FixedPoints, PctOfInitialSL }
    public enum HugoTesting_TrailActivationModeEnum { FixedPoints, PctOfRRTarget }
}
