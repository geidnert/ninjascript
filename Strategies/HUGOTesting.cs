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
        private const string StrategySignalPrefix    = "HUGOMulti";
        private const string LongEntrySignalName      = StrategySignalPrefix + "Long";
        private const string ShortEntrySignalName     = StrategySignalPrefix + "Short";
        private const string HeartbeatStrategyName    = "HUGOTesting";
        private const int    RequiredPrimaryTimeframeMinutes = 15;
        #region Private Variables

        private double dailyPnL_Sess1_L1;
        private int    dailyTradeCount_Sess1_L1;
        private bool   dailyLossHit_Sess1_L1;
        private bool   dailyProfitHit_Sess1_L1;
        private double dailyPnL_Sess1_L2;
        private int    dailyTradeCount_Sess1_L2;
        private bool   dailyLossHit_Sess1_L2;
        private bool   dailyProfitHit_Sess1_L2;
        private double dailyPnL_Sess1_S1;
        private int    dailyTradeCount_Sess1_S1;
        private bool   dailyLossHit_Sess1_S1;
        private bool   dailyProfitHit_Sess1_S1;
        private double dailyPnL_Sess1_S2;
        private int    dailyTradeCount_Sess1_S2;
        private bool   dailyLossHit_Sess1_S2;
        private bool   dailyProfitHit_Sess1_S2;
        private double dailyPnL_Sess2_L1;
        private int    dailyTradeCount_Sess2_L1;
        private bool   dailyLossHit_Sess2_L1;
        private bool   dailyProfitHit_Sess2_L1;
        private double dailyPnL_Sess2_L2;
        private int    dailyTradeCount_Sess2_L2;
        private bool   dailyLossHit_Sess2_L2;
        private bool   dailyProfitHit_Sess2_L2;
        private double dailyPnL_Sess2_S1;
        private int    dailyTradeCount_Sess2_S1;
        private bool   dailyLossHit_Sess2_S1;
        private bool   dailyProfitHit_Sess2_S1;
        private double dailyPnL_Sess2_S2;
        private int    dailyTradeCount_Sess2_S2;
        private bool   dailyLossHit_Sess2_S2;
        private bool   dailyProfitHit_Sess2_S2;
        private double dailyPnL_Sess3_L1;
        private int    dailyTradeCount_Sess3_L1;
        private bool   dailyLossHit_Sess3_L1;
        private bool   dailyProfitHit_Sess3_L1;
        private double dailyPnL_Sess3_L2;
        private int    dailyTradeCount_Sess3_L2;
        private bool   dailyLossHit_Sess3_L2;
        private bool   dailyProfitHit_Sess3_L2;
        private double dailyPnL_Sess3_S1;
        private int    dailyTradeCount_Sess3_S1;
        private bool   dailyLossHit_Sess3_S1;
        private bool   dailyProfitHit_Sess3_S1;
        private double dailyPnL_Sess3_S2;
        private int    dailyTradeCount_Sess3_S2;
        private bool   dailyLossHit_Sess3_S2;
        private bool   dailyProfitHit_Sess3_S2;
        private double dailyPnL_Sess4_L1;
        private int    dailyTradeCount_Sess4_L1;
        private bool   dailyLossHit_Sess4_L1;
        private bool   dailyProfitHit_Sess4_L1;
        private double dailyPnL_Sess4_L2;
        private int    dailyTradeCount_Sess4_L2;
        private bool   dailyLossHit_Sess4_L2;
        private bool   dailyProfitHit_Sess4_L2;
        private double dailyPnL_Sess4_S1;
        private int    dailyTradeCount_Sess4_S1;
        private bool   dailyLossHit_Sess4_S1;
        private bool   dailyProfitHit_Sess4_S1;
        private double dailyPnL_Sess4_S2;
        private int    dailyTradeCount_Sess4_S2;
        private bool   dailyLossHit_Sess4_S2;
        private bool   dailyProfitHit_Sess4_S2;

        private EMA emaSess1_L1;
        private EMA emaSess1_L2;
        private EMA emaSess1_S1;
        private EMA emaSess1_S2;
        private EMA emaSess2_L1;
        private EMA emaSess2_L2;
        private EMA emaSess2_S1;
        private EMA emaSess2_S2;
        private EMA emaSess3_L1;
        private EMA emaSess3_L2;
        private EMA emaSess3_S1;
        private EMA emaSess3_S2;
        private EMA emaSess4_L1;
        private EMA emaSess4_L2;
        private EMA emaSess4_S1;
        private EMA emaSess4_S2;
        private WMA wmaSess1_L1;
        private WMA wmaSess1_L2;
        private WMA wmaSess1_S1;
        private WMA wmaSess1_S2;
        private WMA wmaSess2_L1;
        private WMA wmaSess2_L2;
        private WMA wmaSess2_S1;
        private WMA wmaSess2_S2;
        private WMA wmaSess3_L1;
        private WMA wmaSess3_L2;
        private WMA wmaSess3_S1;
        private WMA wmaSess3_S2;
        private WMA wmaSess4_L1;
        private WMA wmaSess4_L2;
        private WMA wmaSess4_S1;
        private WMA wmaSess4_S2;

        private bool   longSignalActive;
        private bool   shortSignalActive;
        private int    signalBar;
        private double crossoverCandleHigh;
        private double crossoverCandleLow;
        private Order  entryOrder;

        private double   globalDailyPnL;
        private DateTime currentSessionDate;
        private bool     globalDailyLossLimitHit;
        private bool     globalDailyProfitLimitHit;
        private int      globalDailyTradeCount;

        private double entryPrice;
        private double currentStopPrice;
        private bool   breakEvenMoved;
        private int    currentTradeDirection;
        private string activeBucket;
        private int    activeSession;
        private int    pendingDirection;

        private int[]    lastTradeDirection = new int[5];
        private double[] lastTradePnL       = new double[5];

        private double bestPriceSinceEntry;
        private bool   trailActivated;
        private bool   trailLocked;
        private double initialStopDistance;
        private double initialTPDistance;

        private bool inSkipWindow;
        private bool inNewsSkipWindow;

        // ── Commercial / AutoEdge additions ──────────────────────────────────
        private bool     maxAccountLimitHit;
        private bool     isConfiguredTimeframeValid  = true;
        private bool     isConfiguredInstrumentValid = true;
        private bool     timeframePopupShown;
        private bool     instrumentPopupShown;

        // Info box overlay
        private Border      infoBoxContainer;
        private StackPanel  infoBoxRowsPanel;
        private static readonly Brush InfoHeaderFooterGradientBrush = CreateFrozenVerticalGradientBrush(
            Color.FromArgb(240, 0x2A, 0x2F, 0x45),
            Color.FromArgb(240, 0x1E, 0x23, 0x36),
            Color.FromArgb(240, 0x14, 0x18, 0x28));
        private static readonly Brush InfoBodyOddBrush     = CreateFrozenBrush(240, 0x0F, 0x0F, 0x17);
        private static readonly Brush InfoBodyEvenBrush    = CreateFrozenBrush(240, 0x11, 0x11, 0x18);
        private static readonly Brush InfoHeaderTextBrush  = CreateFrozenBrush(255, 0x8B, 0x45, 0x13);
        private static readonly Brush InfoLabelBrush       = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush       = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);
        private static readonly Brush PassedNewsRowBrush   = CreateFrozenBrush(30, 211, 211, 211);

        // Heartbeat reporter (TopstepX API)
        private StrategyHeartbeatReporter heartbeatReporter;

        // News dates
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
        private readonly List<DateTime> NewsDates = new List<DateTime>();
        private bool newsDatesInitialized;

        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  COMMON PARAMETERS
        // ════════════════════════════════════════════════════════════════════════

        #region Common Parameters

        [NinjaScriptProperty]
        [Display(Name = "Session Start Time (Daily Reset Anchor)", Description = "CME daily reset anchor - typically 18:00 for NQ.", Order = 1, GroupName = "0. Common - Time Settings")]
        public string SessionStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contract Quantity", Order = 2, GroupName = "0. Common - Time Settings")]
        public int ContractQuantity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Skip Time Window", Order = 3, GroupName = "0. Common - Time Settings")]
        public bool EnableSkipTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Time Start", Order = 4, GroupName = "0. Common - Time Settings")]
        public string SkipTimeStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Time End", Order = 5, GroupName = "0. Common - Time Settings")]
        public string SkipTimeEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten Position at Skip Start", Order = 6, GroupName = "0. Common - Time Settings")]
        public bool FlattenAtSkipStart { get; set; }

        // ── News Skip ────────────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Order = 7, GroupName = "0. Common - Time Settings")]
        public bool UseNewsSkip { get; set; }

        [Display(Name = "News Time", Order = 8, GroupName = "0. Common - Time Settings")]
        [Browsable(false)]
        public string NewsTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Block Minutes", Order = 9, GroupName = "0. Common - Time Settings")]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten Position at News Start", Order = 10, GroupName = "0. Common - Time Settings")]
        public bool FlattenAtNewsStart { get; set; }

        // ── Execution ────────────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "Entry Confirmation", Order = 1, GroupName = "0. Common - Execution")]
        public bool RequireEntryConfirmation { get; set; }

        // ── Webhooks ─────────────────────────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL", Order = 1, GroupName = "0. Common - Webhooks")]
        public string WebhookUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Ticker Override", Order = 2, GroupName = "0. Common - Webhooks")]
        public string WebhookTickerOverride { get; set; }

        // ── Contract Size ────────────────────────────────────────────────────
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

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Account Balance", Description = "Net liquidation ceiling. When reached, entries are blocked and open positions are flattened. 0 = disabled.", Order = 7, GroupName = "0. Common - Global Risk")]
        public double MaxAccountBalance { get; set; }

        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  PER-SESSION TIME SETTINGS + BUCKET PARAMETERS
        // ════════════════════════════════════════════════════════════════════════

        #region Per-Session Time Settings

        [NinjaScriptProperty]
        [Display(Name = "S1 Enable Session", Description = "Enable or disable this entire session.", Order = 1, GroupName = "1. Session 1 - Times")]
        public bool Sess1_Enable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 Trade Window Start", Order = 2, GroupName = "1. Session 1 - Times")]
        public string Sess1_TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 Last Entry Time", Order = 3, GroupName = "1. Session 1 - Times")]
        public string Sess1_LastEntryTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 Enable Force Close", Order = 4, GroupName = "1. Session 1 - Times")]
        public bool Sess1_EnableForceClose { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 Force Close Time", Order = 5, GroupName = "1. Session 1 - Times")]
        public string Sess1_ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 Session End Time", Order = 6, GroupName = "1. Session 1 - Times")]
        public string Sess1_SessionEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 Enable Session", Description = "Enable or disable this entire session.", Order = 1, GroupName = "1. Session 2 - Times")]
        public bool Sess2_Enable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 Trade Window Start", Order = 2, GroupName = "1. Session 2 - Times")]
        public string Sess2_TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 Last Entry Time", Order = 3, GroupName = "1. Session 2 - Times")]
        public string Sess2_LastEntryTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 Enable Force Close", Order = 4, GroupName = "1. Session 2 - Times")]
        public bool Sess2_EnableForceClose { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 Force Close Time", Order = 5, GroupName = "1. Session 2 - Times")]
        public string Sess2_ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 Session End Time", Order = 6, GroupName = "1. Session 2 - Times")]
        public string Sess2_SessionEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 Enable Session", Description = "Enable or disable this entire session.", Order = 1, GroupName = "1. Session 3 - Times")]
        public bool Sess3_Enable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 Trade Window Start", Order = 2, GroupName = "1. Session 3 - Times")]
        public string Sess3_TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 Last Entry Time", Order = 3, GroupName = "1. Session 3 - Times")]
        public string Sess3_LastEntryTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 Enable Force Close", Order = 4, GroupName = "1. Session 3 - Times")]
        public bool Sess3_EnableForceClose { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 Force Close Time", Order = 5, GroupName = "1. Session 3 - Times")]
        public string Sess3_ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 Session End Time", Order = 6, GroupName = "1. Session 3 - Times")]
        public string Sess3_SessionEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 Enable Session", Description = "Enable or disable this entire session.", Order = 1, GroupName = "1. Session 4 - Times")]
        public bool Sess4_Enable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 Trade Window Start", Order = 2, GroupName = "1. Session 4 - Times")]
        public string Sess4_TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 Last Entry Time", Order = 3, GroupName = "1. Session 4 - Times")]
        public string Sess4_LastEntryTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 Enable Force Close", Order = 4, GroupName = "1. Session 4 - Times")]
        public bool Sess4_EnableForceClose { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 Force Close Time", Order = 5, GroupName = "1. Session 4 - Times")]
        public string Sess4_ForceCloseTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 Session End Time", Order = 6, GroupName = "1. Session 4 - Times")]
        public string Sess4_SessionEndTime { get; set; }

        #endregion

        // ════════════ SESSION 1 ════════════
        #region Session 1 Bucket Parameters

        // ── Sess1 L1 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess1.L1.0 Bucket Settings")]
        public bool Sess1_L1_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess1.L1.0 Bucket Settings")]
        public double Sess1_L1_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess1.L1.0 Bucket Settings")]
        public double Sess1_L1_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 L1 EMA Length", Order = 1, GroupName = "Sess1.L1.1 EMA Settings")]
        public int Sess1_L1_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Enable EMA Slope Filter", Order = 2, GroupName = "Sess1.L1.1 EMA Settings")]
        public bool Sess1_L1_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 EMA Slope Mode", Order = 3, GroupName = "Sess1.L1.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess1_L1_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 L1 EMA Slope Bars", Order = 4, GroupName = "Sess1.L1.1 EMA Settings")]
        public int Sess1_L1_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 EMA Min Slope %", Order = 5, GroupName = "Sess1.L1.1 EMA Settings")]
        public double Sess1_L1_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Enable WMA Filter", Order = 1, GroupName = "Sess1.L1.1b WMA Filter")]
        public bool Sess1_L1_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 L1 WMA Length", Order = 2, GroupName = "Sess1.L1.1b WMA Filter")]
        public int Sess1_L1_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Stop Loss Type", Order = 1, GroupName = "Sess1.L1.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess1_L1_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess1.L1.3 Stop Loss")]
        public double Sess1_L1_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 L1 SL Candle Multiplier", Order = 3, GroupName = "Sess1.L1.3 Stop Loss")]
        public double Sess1_L1_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess1.L1.3 Stop Loss")]
        public double Sess1_L1_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess1.L1.3 Stop Loss")]
        public double Sess1_L1_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Take Profit Type", Order = 1, GroupName = "Sess1.L1.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess1_L1_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S1 L1 Risk Reward Ratio", Order = 2, GroupName = "Sess1.L1.4 Take Profit")]
        public double Sess1_L1_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess1.L1.4 Take Profit")]
        public double Sess1_L1_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 L1 TP Candle Multiplier", Order = 4, GroupName = "Sess1.L1.4 Take Profit")]
        public double Sess1_L1_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Enable Break Even", Order = 1, GroupName = "Sess1.L1.5 Break Even")]
        public bool Sess1_L1_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Break Even Trigger Type", Order = 2, GroupName = "Sess1.L1.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess1_L1_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Break Even Trigger Value", Order = 3, GroupName = "Sess1.L1.5 Break Even")]
        public double Sess1_L1_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Break Even Offset (Points)", Order = 4, GroupName = "Sess1.L1.5 Break Even")]
        public double Sess1_L1_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Enable Price Trail Stop", Order = 1, GroupName = "Sess1.L1.6 Trailing Stop")]
        public bool Sess1_L1_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Trail Distance Mode", Order = 2, GroupName = "Sess1.L1.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess1_L1_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 L1 Trail Distance Value", Order = 3, GroupName = "Sess1.L1.6 Trailing Stop")]
        public double Sess1_L1_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Trail Activation Mode", Order = 4, GroupName = "Sess1.L1.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess1_L1_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Trail Activation Value", Order = 5, GroupName = "Sess1.L1.6 Trailing Stop")]
        public double Sess1_L1_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S1 L1 Trail Step (Ticks)", Order = 6, GroupName = "Sess1.L1.6 Trailing Stop")]
        public int Sess1_L1_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Enable Body % Filter", Order = 1, GroupName = "Sess1.L1.7 Signal Filters")]
        public bool Sess1_L1_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S1 L1 Min Body % of Range", Order = 2, GroupName = "Sess1.L1.7 Signal Filters")]
        public double Sess1_L1_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Enable Direction Flip", Order = 1, GroupName = "Sess1.L1.8 Direction Flip")]
        public bool Sess1_L1_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Enable Max Daily Loss", Order = 1, GroupName = "Sess1.L1.9 Risk Management")]
        public bool Sess1_L1_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Max Daily Loss (Points)", Order = 2, GroupName = "Sess1.L1.9 Risk Management")]
        public double Sess1_L1_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Enable Max Daily Profit", Order = 3, GroupName = "Sess1.L1.9 Risk Management")]
        public bool Sess1_L1_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L1 Max Daily Profit (Points)", Order = 4, GroupName = "Sess1.L1.9 Risk Management")]
        public double Sess1_L1_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L1 Enable Max Trades Per Day", Order = 5, GroupName = "Sess1.L1.9 Risk Management")]
        public bool Sess1_L1_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 L1 Max Trades Per Day", Order = 6, GroupName = "Sess1.L1.9 Risk Management")]
        public int Sess1_L1_MaxTradesPerDay { get; set; }

        // ── Sess1 L2 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess1.L2.0 Bucket Settings")]
        public bool Sess1_L2_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess1.L2.0 Bucket Settings")]
        public double Sess1_L2_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess1.L2.0 Bucket Settings")]
        public double Sess1_L2_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 L2 EMA Length", Order = 1, GroupName = "Sess1.L2.1 EMA Settings")]
        public int Sess1_L2_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Enable EMA Slope Filter", Order = 2, GroupName = "Sess1.L2.1 EMA Settings")]
        public bool Sess1_L2_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 EMA Slope Mode", Order = 3, GroupName = "Sess1.L2.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess1_L2_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 L2 EMA Slope Bars", Order = 4, GroupName = "Sess1.L2.1 EMA Settings")]
        public int Sess1_L2_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 EMA Min Slope %", Order = 5, GroupName = "Sess1.L2.1 EMA Settings")]
        public double Sess1_L2_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Enable WMA Filter", Order = 1, GroupName = "Sess1.L2.1b WMA Filter")]
        public bool Sess1_L2_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 L2 WMA Length", Order = 2, GroupName = "Sess1.L2.1b WMA Filter")]
        public int Sess1_L2_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Stop Loss Type", Order = 1, GroupName = "Sess1.L2.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess1_L2_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess1.L2.3 Stop Loss")]
        public double Sess1_L2_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 L2 SL Candle Multiplier", Order = 3, GroupName = "Sess1.L2.3 Stop Loss")]
        public double Sess1_L2_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess1.L2.3 Stop Loss")]
        public double Sess1_L2_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess1.L2.3 Stop Loss")]
        public double Sess1_L2_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Take Profit Type", Order = 1, GroupName = "Sess1.L2.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess1_L2_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S1 L2 Risk Reward Ratio", Order = 2, GroupName = "Sess1.L2.4 Take Profit")]
        public double Sess1_L2_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess1.L2.4 Take Profit")]
        public double Sess1_L2_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 L2 TP Candle Multiplier", Order = 4, GroupName = "Sess1.L2.4 Take Profit")]
        public double Sess1_L2_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Enable Break Even", Order = 1, GroupName = "Sess1.L2.5 Break Even")]
        public bool Sess1_L2_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Break Even Trigger Type", Order = 2, GroupName = "Sess1.L2.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess1_L2_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Break Even Trigger Value", Order = 3, GroupName = "Sess1.L2.5 Break Even")]
        public double Sess1_L2_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Break Even Offset (Points)", Order = 4, GroupName = "Sess1.L2.5 Break Even")]
        public double Sess1_L2_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Enable Price Trail Stop", Order = 1, GroupName = "Sess1.L2.6 Trailing Stop")]
        public bool Sess1_L2_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Trail Distance Mode", Order = 2, GroupName = "Sess1.L2.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess1_L2_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 L2 Trail Distance Value", Order = 3, GroupName = "Sess1.L2.6 Trailing Stop")]
        public double Sess1_L2_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Trail Activation Mode", Order = 4, GroupName = "Sess1.L2.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess1_L2_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Trail Activation Value", Order = 5, GroupName = "Sess1.L2.6 Trailing Stop")]
        public double Sess1_L2_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S1 L2 Trail Step (Ticks)", Order = 6, GroupName = "Sess1.L2.6 Trailing Stop")]
        public int Sess1_L2_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Enable Body % Filter", Order = 1, GroupName = "Sess1.L2.7 Signal Filters")]
        public bool Sess1_L2_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S1 L2 Min Body % of Range", Order = 2, GroupName = "Sess1.L2.7 Signal Filters")]
        public double Sess1_L2_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Enable Direction Flip", Order = 1, GroupName = "Sess1.L2.8 Direction Flip")]
        public bool Sess1_L2_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Enable Max Daily Loss", Order = 1, GroupName = "Sess1.L2.9 Risk Management")]
        public bool Sess1_L2_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Max Daily Loss (Points)", Order = 2, GroupName = "Sess1.L2.9 Risk Management")]
        public double Sess1_L2_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Enable Max Daily Profit", Order = 3, GroupName = "Sess1.L2.9 Risk Management")]
        public bool Sess1_L2_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 L2 Max Daily Profit (Points)", Order = 4, GroupName = "Sess1.L2.9 Risk Management")]
        public double Sess1_L2_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 L2 Enable Max Trades Per Day", Order = 5, GroupName = "Sess1.L2.9 Risk Management")]
        public bool Sess1_L2_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 L2 Max Trades Per Day", Order = 6, GroupName = "Sess1.L2.9 Risk Management")]
        public int Sess1_L2_MaxTradesPerDay { get; set; }

        // ── Sess1 S1 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess1.S1.0 Bucket Settings")]
        public bool Sess1_S1_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess1.S1.0 Bucket Settings")]
        public double Sess1_S1_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess1.S1.0 Bucket Settings")]
        public double Sess1_S1_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 S1 EMA Length", Order = 1, GroupName = "Sess1.S1.1 EMA Settings")]
        public int Sess1_S1_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Enable EMA Slope Filter", Order = 2, GroupName = "Sess1.S1.1 EMA Settings")]
        public bool Sess1_S1_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 EMA Slope Mode", Order = 3, GroupName = "Sess1.S1.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess1_S1_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 S1 EMA Slope Bars", Order = 4, GroupName = "Sess1.S1.1 EMA Settings")]
        public int Sess1_S1_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 EMA Min Slope %", Order = 5, GroupName = "Sess1.S1.1 EMA Settings")]
        public double Sess1_S1_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Enable WMA Filter", Order = 1, GroupName = "Sess1.S1.1b WMA Filter")]
        public bool Sess1_S1_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 S1 WMA Length", Order = 2, GroupName = "Sess1.S1.1b WMA Filter")]
        public int Sess1_S1_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Stop Loss Type", Order = 1, GroupName = "Sess1.S1.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess1_S1_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess1.S1.3 Stop Loss")]
        public double Sess1_S1_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 S1 SL Candle Multiplier", Order = 3, GroupName = "Sess1.S1.3 Stop Loss")]
        public double Sess1_S1_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess1.S1.3 Stop Loss")]
        public double Sess1_S1_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess1.S1.3 Stop Loss")]
        public double Sess1_S1_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Take Profit Type", Order = 1, GroupName = "Sess1.S1.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess1_S1_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S1 S1 Risk Reward Ratio", Order = 2, GroupName = "Sess1.S1.4 Take Profit")]
        public double Sess1_S1_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess1.S1.4 Take Profit")]
        public double Sess1_S1_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 S1 TP Candle Multiplier", Order = 4, GroupName = "Sess1.S1.4 Take Profit")]
        public double Sess1_S1_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Enable Break Even", Order = 1, GroupName = "Sess1.S1.5 Break Even")]
        public bool Sess1_S1_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Break Even Trigger Type", Order = 2, GroupName = "Sess1.S1.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess1_S1_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Break Even Trigger Value", Order = 3, GroupName = "Sess1.S1.5 Break Even")]
        public double Sess1_S1_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Break Even Offset (Points)", Order = 4, GroupName = "Sess1.S1.5 Break Even")]
        public double Sess1_S1_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Enable Price Trail Stop", Order = 1, GroupName = "Sess1.S1.6 Trailing Stop")]
        public bool Sess1_S1_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Trail Distance Mode", Order = 2, GroupName = "Sess1.S1.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess1_S1_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 S1 Trail Distance Value", Order = 3, GroupName = "Sess1.S1.6 Trailing Stop")]
        public double Sess1_S1_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Trail Activation Mode", Order = 4, GroupName = "Sess1.S1.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess1_S1_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Trail Activation Value", Order = 5, GroupName = "Sess1.S1.6 Trailing Stop")]
        public double Sess1_S1_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S1 S1 Trail Step (Ticks)", Order = 6, GroupName = "Sess1.S1.6 Trailing Stop")]
        public int Sess1_S1_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Enable Body % Filter", Order = 1, GroupName = "Sess1.S1.7 Signal Filters")]
        public bool Sess1_S1_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S1 S1 Min Body % of Range", Order = 2, GroupName = "Sess1.S1.7 Signal Filters")]
        public double Sess1_S1_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Enable Direction Flip", Order = 1, GroupName = "Sess1.S1.8 Direction Flip")]
        public bool Sess1_S1_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Enable Max Daily Loss", Order = 1, GroupName = "Sess1.S1.9 Risk Management")]
        public bool Sess1_S1_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Max Daily Loss (Points)", Order = 2, GroupName = "Sess1.S1.9 Risk Management")]
        public double Sess1_S1_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Enable Max Daily Profit", Order = 3, GroupName = "Sess1.S1.9 Risk Management")]
        public bool Sess1_S1_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S1 Max Daily Profit (Points)", Order = 4, GroupName = "Sess1.S1.9 Risk Management")]
        public double Sess1_S1_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S1 Enable Max Trades Per Day", Order = 5, GroupName = "Sess1.S1.9 Risk Management")]
        public bool Sess1_S1_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 S1 Max Trades Per Day", Order = 6, GroupName = "Sess1.S1.9 Risk Management")]
        public int Sess1_S1_MaxTradesPerDay { get; set; }

        // ── Sess1 S2 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess1.S2.0 Bucket Settings")]
        public bool Sess1_S2_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess1.S2.0 Bucket Settings")]
        public double Sess1_S2_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess1.S2.0 Bucket Settings")]
        public double Sess1_S2_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 S2 EMA Length", Order = 1, GroupName = "Sess1.S2.1 EMA Settings")]
        public int Sess1_S2_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Enable EMA Slope Filter", Order = 2, GroupName = "Sess1.S2.1 EMA Settings")]
        public bool Sess1_S2_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 EMA Slope Mode", Order = 3, GroupName = "Sess1.S2.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess1_S2_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 S2 EMA Slope Bars", Order = 4, GroupName = "Sess1.S2.1 EMA Settings")]
        public int Sess1_S2_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 EMA Min Slope %", Order = 5, GroupName = "Sess1.S2.1 EMA Settings")]
        public double Sess1_S2_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Enable WMA Filter", Order = 1, GroupName = "Sess1.S2.1b WMA Filter")]
        public bool Sess1_S2_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 S2 WMA Length", Order = 2, GroupName = "Sess1.S2.1b WMA Filter")]
        public int Sess1_S2_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Stop Loss Type", Order = 1, GroupName = "Sess1.S2.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess1_S2_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess1.S2.3 Stop Loss")]
        public double Sess1_S2_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 S2 SL Candle Multiplier", Order = 3, GroupName = "Sess1.S2.3 Stop Loss")]
        public double Sess1_S2_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess1.S2.3 Stop Loss")]
        public double Sess1_S2_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess1.S2.3 Stop Loss")]
        public double Sess1_S2_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Take Profit Type", Order = 1, GroupName = "Sess1.S2.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess1_S2_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S1 S2 Risk Reward Ratio", Order = 2, GroupName = "Sess1.S2.4 Take Profit")]
        public double Sess1_S2_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess1.S2.4 Take Profit")]
        public double Sess1_S2_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 S2 TP Candle Multiplier", Order = 4, GroupName = "Sess1.S2.4 Take Profit")]
        public double Sess1_S2_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Enable Break Even", Order = 1, GroupName = "Sess1.S2.5 Break Even")]
        public bool Sess1_S2_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Break Even Trigger Type", Order = 2, GroupName = "Sess1.S2.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess1_S2_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Break Even Trigger Value", Order = 3, GroupName = "Sess1.S2.5 Break Even")]
        public double Sess1_S2_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Break Even Offset (Points)", Order = 4, GroupName = "Sess1.S2.5 Break Even")]
        public double Sess1_S2_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Enable Price Trail Stop", Order = 1, GroupName = "Sess1.S2.6 Trailing Stop")]
        public bool Sess1_S2_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Trail Distance Mode", Order = 2, GroupName = "Sess1.S2.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess1_S2_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S1 S2 Trail Distance Value", Order = 3, GroupName = "Sess1.S2.6 Trailing Stop")]
        public double Sess1_S2_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Trail Activation Mode", Order = 4, GroupName = "Sess1.S2.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess1_S2_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Trail Activation Value", Order = 5, GroupName = "Sess1.S2.6 Trailing Stop")]
        public double Sess1_S2_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S1 S2 Trail Step (Ticks)", Order = 6, GroupName = "Sess1.S2.6 Trailing Stop")]
        public int Sess1_S2_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Enable Body % Filter", Order = 1, GroupName = "Sess1.S2.7 Signal Filters")]
        public bool Sess1_S2_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S1 S2 Min Body % of Range", Order = 2, GroupName = "Sess1.S2.7 Signal Filters")]
        public double Sess1_S2_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Enable Direction Flip", Order = 1, GroupName = "Sess1.S2.8 Direction Flip")]
        public bool Sess1_S2_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Enable Max Daily Loss", Order = 1, GroupName = "Sess1.S2.9 Risk Management")]
        public bool Sess1_S2_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Max Daily Loss (Points)", Order = 2, GroupName = "Sess1.S2.9 Risk Management")]
        public double Sess1_S2_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Enable Max Daily Profit", Order = 3, GroupName = "Sess1.S2.9 Risk Management")]
        public bool Sess1_S2_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S1 S2 Max Daily Profit (Points)", Order = 4, GroupName = "Sess1.S2.9 Risk Management")]
        public double Sess1_S2_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S1 S2 Enable Max Trades Per Day", Order = 5, GroupName = "Sess1.S2.9 Risk Management")]
        public bool Sess1_S2_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S1 S2 Max Trades Per Day", Order = 6, GroupName = "Sess1.S2.9 Risk Management")]
        public int Sess1_S2_MaxTradesPerDay { get; set; }

        #endregion

        // ════════════ SESSION 2 ════════════
        #region Session 2 Bucket Parameters

        // ── Sess2 L1 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess2.L1.0 Bucket Settings")]
        public bool Sess2_L1_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess2.L1.0 Bucket Settings")]
        public double Sess2_L1_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess2.L1.0 Bucket Settings")]
        public double Sess2_L1_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 L1 EMA Length", Order = 1, GroupName = "Sess2.L1.1 EMA Settings")]
        public int Sess2_L1_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Enable EMA Slope Filter", Order = 2, GroupName = "Sess2.L1.1 EMA Settings")]
        public bool Sess2_L1_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 EMA Slope Mode", Order = 3, GroupName = "Sess2.L1.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess2_L1_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 L1 EMA Slope Bars", Order = 4, GroupName = "Sess2.L1.1 EMA Settings")]
        public int Sess2_L1_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 EMA Min Slope %", Order = 5, GroupName = "Sess2.L1.1 EMA Settings")]
        public double Sess2_L1_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Enable WMA Filter", Order = 1, GroupName = "Sess2.L1.1b WMA Filter")]
        public bool Sess2_L1_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 L1 WMA Length", Order = 2, GroupName = "Sess2.L1.1b WMA Filter")]
        public int Sess2_L1_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Stop Loss Type", Order = 1, GroupName = "Sess2.L1.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess2_L1_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess2.L1.3 Stop Loss")]
        public double Sess2_L1_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 L1 SL Candle Multiplier", Order = 3, GroupName = "Sess2.L1.3 Stop Loss")]
        public double Sess2_L1_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess2.L1.3 Stop Loss")]
        public double Sess2_L1_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess2.L1.3 Stop Loss")]
        public double Sess2_L1_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Take Profit Type", Order = 1, GroupName = "Sess2.L1.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess2_L1_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S2 L1 Risk Reward Ratio", Order = 2, GroupName = "Sess2.L1.4 Take Profit")]
        public double Sess2_L1_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess2.L1.4 Take Profit")]
        public double Sess2_L1_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 L1 TP Candle Multiplier", Order = 4, GroupName = "Sess2.L1.4 Take Profit")]
        public double Sess2_L1_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Enable Break Even", Order = 1, GroupName = "Sess2.L1.5 Break Even")]
        public bool Sess2_L1_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Break Even Trigger Type", Order = 2, GroupName = "Sess2.L1.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess2_L1_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Break Even Trigger Value", Order = 3, GroupName = "Sess2.L1.5 Break Even")]
        public double Sess2_L1_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Break Even Offset (Points)", Order = 4, GroupName = "Sess2.L1.5 Break Even")]
        public double Sess2_L1_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Enable Price Trail Stop", Order = 1, GroupName = "Sess2.L1.6 Trailing Stop")]
        public bool Sess2_L1_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Trail Distance Mode", Order = 2, GroupName = "Sess2.L1.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess2_L1_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 L1 Trail Distance Value", Order = 3, GroupName = "Sess2.L1.6 Trailing Stop")]
        public double Sess2_L1_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Trail Activation Mode", Order = 4, GroupName = "Sess2.L1.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess2_L1_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Trail Activation Value", Order = 5, GroupName = "Sess2.L1.6 Trailing Stop")]
        public double Sess2_L1_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S2 L1 Trail Step (Ticks)", Order = 6, GroupName = "Sess2.L1.6 Trailing Stop")]
        public int Sess2_L1_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Enable Body % Filter", Order = 1, GroupName = "Sess2.L1.7 Signal Filters")]
        public bool Sess2_L1_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S2 L1 Min Body % of Range", Order = 2, GroupName = "Sess2.L1.7 Signal Filters")]
        public double Sess2_L1_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Enable Direction Flip", Order = 1, GroupName = "Sess2.L1.8 Direction Flip")]
        public bool Sess2_L1_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Enable Max Daily Loss", Order = 1, GroupName = "Sess2.L1.9 Risk Management")]
        public bool Sess2_L1_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Max Daily Loss (Points)", Order = 2, GroupName = "Sess2.L1.9 Risk Management")]
        public double Sess2_L1_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Enable Max Daily Profit", Order = 3, GroupName = "Sess2.L1.9 Risk Management")]
        public bool Sess2_L1_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L1 Max Daily Profit (Points)", Order = 4, GroupName = "Sess2.L1.9 Risk Management")]
        public double Sess2_L1_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L1 Enable Max Trades Per Day", Order = 5, GroupName = "Sess2.L1.9 Risk Management")]
        public bool Sess2_L1_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 L1 Max Trades Per Day", Order = 6, GroupName = "Sess2.L1.9 Risk Management")]
        public int Sess2_L1_MaxTradesPerDay { get; set; }

        // ── Sess2 L2 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess2.L2.0 Bucket Settings")]
        public bool Sess2_L2_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess2.L2.0 Bucket Settings")]
        public double Sess2_L2_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess2.L2.0 Bucket Settings")]
        public double Sess2_L2_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 L2 EMA Length", Order = 1, GroupName = "Sess2.L2.1 EMA Settings")]
        public int Sess2_L2_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Enable EMA Slope Filter", Order = 2, GroupName = "Sess2.L2.1 EMA Settings")]
        public bool Sess2_L2_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 EMA Slope Mode", Order = 3, GroupName = "Sess2.L2.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess2_L2_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 L2 EMA Slope Bars", Order = 4, GroupName = "Sess2.L2.1 EMA Settings")]
        public int Sess2_L2_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 EMA Min Slope %", Order = 5, GroupName = "Sess2.L2.1 EMA Settings")]
        public double Sess2_L2_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Enable WMA Filter", Order = 1, GroupName = "Sess2.L2.1b WMA Filter")]
        public bool Sess2_L2_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 L2 WMA Length", Order = 2, GroupName = "Sess2.L2.1b WMA Filter")]
        public int Sess2_L2_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Stop Loss Type", Order = 1, GroupName = "Sess2.L2.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess2_L2_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess2.L2.3 Stop Loss")]
        public double Sess2_L2_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 L2 SL Candle Multiplier", Order = 3, GroupName = "Sess2.L2.3 Stop Loss")]
        public double Sess2_L2_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess2.L2.3 Stop Loss")]
        public double Sess2_L2_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess2.L2.3 Stop Loss")]
        public double Sess2_L2_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Take Profit Type", Order = 1, GroupName = "Sess2.L2.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess2_L2_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S2 L2 Risk Reward Ratio", Order = 2, GroupName = "Sess2.L2.4 Take Profit")]
        public double Sess2_L2_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess2.L2.4 Take Profit")]
        public double Sess2_L2_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 L2 TP Candle Multiplier", Order = 4, GroupName = "Sess2.L2.4 Take Profit")]
        public double Sess2_L2_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Enable Break Even", Order = 1, GroupName = "Sess2.L2.5 Break Even")]
        public bool Sess2_L2_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Break Even Trigger Type", Order = 2, GroupName = "Sess2.L2.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess2_L2_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Break Even Trigger Value", Order = 3, GroupName = "Sess2.L2.5 Break Even")]
        public double Sess2_L2_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Break Even Offset (Points)", Order = 4, GroupName = "Sess2.L2.5 Break Even")]
        public double Sess2_L2_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Enable Price Trail Stop", Order = 1, GroupName = "Sess2.L2.6 Trailing Stop")]
        public bool Sess2_L2_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Trail Distance Mode", Order = 2, GroupName = "Sess2.L2.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess2_L2_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 L2 Trail Distance Value", Order = 3, GroupName = "Sess2.L2.6 Trailing Stop")]
        public double Sess2_L2_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Trail Activation Mode", Order = 4, GroupName = "Sess2.L2.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess2_L2_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Trail Activation Value", Order = 5, GroupName = "Sess2.L2.6 Trailing Stop")]
        public double Sess2_L2_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S2 L2 Trail Step (Ticks)", Order = 6, GroupName = "Sess2.L2.6 Trailing Stop")]
        public int Sess2_L2_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Enable Body % Filter", Order = 1, GroupName = "Sess2.L2.7 Signal Filters")]
        public bool Sess2_L2_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S2 L2 Min Body % of Range", Order = 2, GroupName = "Sess2.L2.7 Signal Filters")]
        public double Sess2_L2_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Enable Direction Flip", Order = 1, GroupName = "Sess2.L2.8 Direction Flip")]
        public bool Sess2_L2_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Enable Max Daily Loss", Order = 1, GroupName = "Sess2.L2.9 Risk Management")]
        public bool Sess2_L2_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Max Daily Loss (Points)", Order = 2, GroupName = "Sess2.L2.9 Risk Management")]
        public double Sess2_L2_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Enable Max Daily Profit", Order = 3, GroupName = "Sess2.L2.9 Risk Management")]
        public bool Sess2_L2_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 L2 Max Daily Profit (Points)", Order = 4, GroupName = "Sess2.L2.9 Risk Management")]
        public double Sess2_L2_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 L2 Enable Max Trades Per Day", Order = 5, GroupName = "Sess2.L2.9 Risk Management")]
        public bool Sess2_L2_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 L2 Max Trades Per Day", Order = 6, GroupName = "Sess2.L2.9 Risk Management")]
        public int Sess2_L2_MaxTradesPerDay { get; set; }

        // ── Sess2 S1 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess2.S1.0 Bucket Settings")]
        public bool Sess2_S1_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess2.S1.0 Bucket Settings")]
        public double Sess2_S1_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess2.S1.0 Bucket Settings")]
        public double Sess2_S1_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 S1 EMA Length", Order = 1, GroupName = "Sess2.S1.1 EMA Settings")]
        public int Sess2_S1_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Enable EMA Slope Filter", Order = 2, GroupName = "Sess2.S1.1 EMA Settings")]
        public bool Sess2_S1_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 EMA Slope Mode", Order = 3, GroupName = "Sess2.S1.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess2_S1_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 S1 EMA Slope Bars", Order = 4, GroupName = "Sess2.S1.1 EMA Settings")]
        public int Sess2_S1_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 EMA Min Slope %", Order = 5, GroupName = "Sess2.S1.1 EMA Settings")]
        public double Sess2_S1_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Enable WMA Filter", Order = 1, GroupName = "Sess2.S1.1b WMA Filter")]
        public bool Sess2_S1_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 S1 WMA Length", Order = 2, GroupName = "Sess2.S1.1b WMA Filter")]
        public int Sess2_S1_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Stop Loss Type", Order = 1, GroupName = "Sess2.S1.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess2_S1_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess2.S1.3 Stop Loss")]
        public double Sess2_S1_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 S1 SL Candle Multiplier", Order = 3, GroupName = "Sess2.S1.3 Stop Loss")]
        public double Sess2_S1_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess2.S1.3 Stop Loss")]
        public double Sess2_S1_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess2.S1.3 Stop Loss")]
        public double Sess2_S1_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Take Profit Type", Order = 1, GroupName = "Sess2.S1.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess2_S1_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S2 S1 Risk Reward Ratio", Order = 2, GroupName = "Sess2.S1.4 Take Profit")]
        public double Sess2_S1_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess2.S1.4 Take Profit")]
        public double Sess2_S1_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 S1 TP Candle Multiplier", Order = 4, GroupName = "Sess2.S1.4 Take Profit")]
        public double Sess2_S1_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Enable Break Even", Order = 1, GroupName = "Sess2.S1.5 Break Even")]
        public bool Sess2_S1_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Break Even Trigger Type", Order = 2, GroupName = "Sess2.S1.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess2_S1_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Break Even Trigger Value", Order = 3, GroupName = "Sess2.S1.5 Break Even")]
        public double Sess2_S1_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Break Even Offset (Points)", Order = 4, GroupName = "Sess2.S1.5 Break Even")]
        public double Sess2_S1_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Enable Price Trail Stop", Order = 1, GroupName = "Sess2.S1.6 Trailing Stop")]
        public bool Sess2_S1_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Trail Distance Mode", Order = 2, GroupName = "Sess2.S1.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess2_S1_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 S1 Trail Distance Value", Order = 3, GroupName = "Sess2.S1.6 Trailing Stop")]
        public double Sess2_S1_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Trail Activation Mode", Order = 4, GroupName = "Sess2.S1.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess2_S1_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Trail Activation Value", Order = 5, GroupName = "Sess2.S1.6 Trailing Stop")]
        public double Sess2_S1_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S2 S1 Trail Step (Ticks)", Order = 6, GroupName = "Sess2.S1.6 Trailing Stop")]
        public int Sess2_S1_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Enable Body % Filter", Order = 1, GroupName = "Sess2.S1.7 Signal Filters")]
        public bool Sess2_S1_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S2 S1 Min Body % of Range", Order = 2, GroupName = "Sess2.S1.7 Signal Filters")]
        public double Sess2_S1_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Enable Direction Flip", Order = 1, GroupName = "Sess2.S1.8 Direction Flip")]
        public bool Sess2_S1_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Enable Max Daily Loss", Order = 1, GroupName = "Sess2.S1.9 Risk Management")]
        public bool Sess2_S1_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Max Daily Loss (Points)", Order = 2, GroupName = "Sess2.S1.9 Risk Management")]
        public double Sess2_S1_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Enable Max Daily Profit", Order = 3, GroupName = "Sess2.S1.9 Risk Management")]
        public bool Sess2_S1_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S1 Max Daily Profit (Points)", Order = 4, GroupName = "Sess2.S1.9 Risk Management")]
        public double Sess2_S1_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S1 Enable Max Trades Per Day", Order = 5, GroupName = "Sess2.S1.9 Risk Management")]
        public bool Sess2_S1_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 S1 Max Trades Per Day", Order = 6, GroupName = "Sess2.S1.9 Risk Management")]
        public int Sess2_S1_MaxTradesPerDay { get; set; }

        // ── Sess2 S2 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess2.S2.0 Bucket Settings")]
        public bool Sess2_S2_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess2.S2.0 Bucket Settings")]
        public double Sess2_S2_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess2.S2.0 Bucket Settings")]
        public double Sess2_S2_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 S2 EMA Length", Order = 1, GroupName = "Sess2.S2.1 EMA Settings")]
        public int Sess2_S2_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Enable EMA Slope Filter", Order = 2, GroupName = "Sess2.S2.1 EMA Settings")]
        public bool Sess2_S2_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 EMA Slope Mode", Order = 3, GroupName = "Sess2.S2.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess2_S2_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 S2 EMA Slope Bars", Order = 4, GroupName = "Sess2.S2.1 EMA Settings")]
        public int Sess2_S2_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 EMA Min Slope %", Order = 5, GroupName = "Sess2.S2.1 EMA Settings")]
        public double Sess2_S2_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Enable WMA Filter", Order = 1, GroupName = "Sess2.S2.1b WMA Filter")]
        public bool Sess2_S2_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 S2 WMA Length", Order = 2, GroupName = "Sess2.S2.1b WMA Filter")]
        public int Sess2_S2_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Stop Loss Type", Order = 1, GroupName = "Sess2.S2.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess2_S2_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess2.S2.3 Stop Loss")]
        public double Sess2_S2_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 S2 SL Candle Multiplier", Order = 3, GroupName = "Sess2.S2.3 Stop Loss")]
        public double Sess2_S2_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess2.S2.3 Stop Loss")]
        public double Sess2_S2_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess2.S2.3 Stop Loss")]
        public double Sess2_S2_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Take Profit Type", Order = 1, GroupName = "Sess2.S2.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess2_S2_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S2 S2 Risk Reward Ratio", Order = 2, GroupName = "Sess2.S2.4 Take Profit")]
        public double Sess2_S2_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess2.S2.4 Take Profit")]
        public double Sess2_S2_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 S2 TP Candle Multiplier", Order = 4, GroupName = "Sess2.S2.4 Take Profit")]
        public double Sess2_S2_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Enable Break Even", Order = 1, GroupName = "Sess2.S2.5 Break Even")]
        public bool Sess2_S2_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Break Even Trigger Type", Order = 2, GroupName = "Sess2.S2.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess2_S2_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Break Even Trigger Value", Order = 3, GroupName = "Sess2.S2.5 Break Even")]
        public double Sess2_S2_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Break Even Offset (Points)", Order = 4, GroupName = "Sess2.S2.5 Break Even")]
        public double Sess2_S2_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Enable Price Trail Stop", Order = 1, GroupName = "Sess2.S2.6 Trailing Stop")]
        public bool Sess2_S2_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Trail Distance Mode", Order = 2, GroupName = "Sess2.S2.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess2_S2_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S2 S2 Trail Distance Value", Order = 3, GroupName = "Sess2.S2.6 Trailing Stop")]
        public double Sess2_S2_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Trail Activation Mode", Order = 4, GroupName = "Sess2.S2.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess2_S2_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Trail Activation Value", Order = 5, GroupName = "Sess2.S2.6 Trailing Stop")]
        public double Sess2_S2_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S2 S2 Trail Step (Ticks)", Order = 6, GroupName = "Sess2.S2.6 Trailing Stop")]
        public int Sess2_S2_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Enable Body % Filter", Order = 1, GroupName = "Sess2.S2.7 Signal Filters")]
        public bool Sess2_S2_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S2 S2 Min Body % of Range", Order = 2, GroupName = "Sess2.S2.7 Signal Filters")]
        public double Sess2_S2_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Enable Direction Flip", Order = 1, GroupName = "Sess2.S2.8 Direction Flip")]
        public bool Sess2_S2_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Enable Max Daily Loss", Order = 1, GroupName = "Sess2.S2.9 Risk Management")]
        public bool Sess2_S2_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Max Daily Loss (Points)", Order = 2, GroupName = "Sess2.S2.9 Risk Management")]
        public double Sess2_S2_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Enable Max Daily Profit", Order = 3, GroupName = "Sess2.S2.9 Risk Management")]
        public bool Sess2_S2_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S2 S2 Max Daily Profit (Points)", Order = 4, GroupName = "Sess2.S2.9 Risk Management")]
        public double Sess2_S2_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S2 S2 Enable Max Trades Per Day", Order = 5, GroupName = "Sess2.S2.9 Risk Management")]
        public bool Sess2_S2_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S2 S2 Max Trades Per Day", Order = 6, GroupName = "Sess2.S2.9 Risk Management")]
        public int Sess2_S2_MaxTradesPerDay { get; set; }

        #endregion

        // ════════════ SESSION 3 ════════════
        #region Session 3 Bucket Parameters

        // ── Sess3 L1 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess3.L1.0 Bucket Settings")]
        public bool Sess3_L1_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess3.L1.0 Bucket Settings")]
        public double Sess3_L1_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess3.L1.0 Bucket Settings")]
        public double Sess3_L1_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 L1 EMA Length", Order = 1, GroupName = "Sess3.L1.1 EMA Settings")]
        public int Sess3_L1_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Enable EMA Slope Filter", Order = 2, GroupName = "Sess3.L1.1 EMA Settings")]
        public bool Sess3_L1_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 EMA Slope Mode", Order = 3, GroupName = "Sess3.L1.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess3_L1_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 L1 EMA Slope Bars", Order = 4, GroupName = "Sess3.L1.1 EMA Settings")]
        public int Sess3_L1_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 EMA Min Slope %", Order = 5, GroupName = "Sess3.L1.1 EMA Settings")]
        public double Sess3_L1_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Enable WMA Filter", Order = 1, GroupName = "Sess3.L1.1b WMA Filter")]
        public bool Sess3_L1_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 L1 WMA Length", Order = 2, GroupName = "Sess3.L1.1b WMA Filter")]
        public int Sess3_L1_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Stop Loss Type", Order = 1, GroupName = "Sess3.L1.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess3_L1_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess3.L1.3 Stop Loss")]
        public double Sess3_L1_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 L1 SL Candle Multiplier", Order = 3, GroupName = "Sess3.L1.3 Stop Loss")]
        public double Sess3_L1_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess3.L1.3 Stop Loss")]
        public double Sess3_L1_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess3.L1.3 Stop Loss")]
        public double Sess3_L1_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Take Profit Type", Order = 1, GroupName = "Sess3.L1.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess3_L1_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S3 L1 Risk Reward Ratio", Order = 2, GroupName = "Sess3.L1.4 Take Profit")]
        public double Sess3_L1_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess3.L1.4 Take Profit")]
        public double Sess3_L1_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 L1 TP Candle Multiplier", Order = 4, GroupName = "Sess3.L1.4 Take Profit")]
        public double Sess3_L1_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Enable Break Even", Order = 1, GroupName = "Sess3.L1.5 Break Even")]
        public bool Sess3_L1_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Break Even Trigger Type", Order = 2, GroupName = "Sess3.L1.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess3_L1_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Break Even Trigger Value", Order = 3, GroupName = "Sess3.L1.5 Break Even")]
        public double Sess3_L1_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Break Even Offset (Points)", Order = 4, GroupName = "Sess3.L1.5 Break Even")]
        public double Sess3_L1_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Enable Price Trail Stop", Order = 1, GroupName = "Sess3.L1.6 Trailing Stop")]
        public bool Sess3_L1_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Trail Distance Mode", Order = 2, GroupName = "Sess3.L1.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess3_L1_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 L1 Trail Distance Value", Order = 3, GroupName = "Sess3.L1.6 Trailing Stop")]
        public double Sess3_L1_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Trail Activation Mode", Order = 4, GroupName = "Sess3.L1.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess3_L1_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Trail Activation Value", Order = 5, GroupName = "Sess3.L1.6 Trailing Stop")]
        public double Sess3_L1_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S3 L1 Trail Step (Ticks)", Order = 6, GroupName = "Sess3.L1.6 Trailing Stop")]
        public int Sess3_L1_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Enable Body % Filter", Order = 1, GroupName = "Sess3.L1.7 Signal Filters")]
        public bool Sess3_L1_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S3 L1 Min Body % of Range", Order = 2, GroupName = "Sess3.L1.7 Signal Filters")]
        public double Sess3_L1_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Enable Direction Flip", Order = 1, GroupName = "Sess3.L1.8 Direction Flip")]
        public bool Sess3_L1_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Enable Max Daily Loss", Order = 1, GroupName = "Sess3.L1.9 Risk Management")]
        public bool Sess3_L1_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Max Daily Loss (Points)", Order = 2, GroupName = "Sess3.L1.9 Risk Management")]
        public double Sess3_L1_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Enable Max Daily Profit", Order = 3, GroupName = "Sess3.L1.9 Risk Management")]
        public bool Sess3_L1_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L1 Max Daily Profit (Points)", Order = 4, GroupName = "Sess3.L1.9 Risk Management")]
        public double Sess3_L1_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L1 Enable Max Trades Per Day", Order = 5, GroupName = "Sess3.L1.9 Risk Management")]
        public bool Sess3_L1_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 L1 Max Trades Per Day", Order = 6, GroupName = "Sess3.L1.9 Risk Management")]
        public int Sess3_L1_MaxTradesPerDay { get; set; }

        // ── Sess3 L2 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess3.L2.0 Bucket Settings")]
        public bool Sess3_L2_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess3.L2.0 Bucket Settings")]
        public double Sess3_L2_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess3.L2.0 Bucket Settings")]
        public double Sess3_L2_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 L2 EMA Length", Order = 1, GroupName = "Sess3.L2.1 EMA Settings")]
        public int Sess3_L2_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Enable EMA Slope Filter", Order = 2, GroupName = "Sess3.L2.1 EMA Settings")]
        public bool Sess3_L2_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 EMA Slope Mode", Order = 3, GroupName = "Sess3.L2.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess3_L2_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 L2 EMA Slope Bars", Order = 4, GroupName = "Sess3.L2.1 EMA Settings")]
        public int Sess3_L2_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 EMA Min Slope %", Order = 5, GroupName = "Sess3.L2.1 EMA Settings")]
        public double Sess3_L2_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Enable WMA Filter", Order = 1, GroupName = "Sess3.L2.1b WMA Filter")]
        public bool Sess3_L2_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 L2 WMA Length", Order = 2, GroupName = "Sess3.L2.1b WMA Filter")]
        public int Sess3_L2_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Stop Loss Type", Order = 1, GroupName = "Sess3.L2.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess3_L2_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess3.L2.3 Stop Loss")]
        public double Sess3_L2_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 L2 SL Candle Multiplier", Order = 3, GroupName = "Sess3.L2.3 Stop Loss")]
        public double Sess3_L2_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess3.L2.3 Stop Loss")]
        public double Sess3_L2_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess3.L2.3 Stop Loss")]
        public double Sess3_L2_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Take Profit Type", Order = 1, GroupName = "Sess3.L2.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess3_L2_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S3 L2 Risk Reward Ratio", Order = 2, GroupName = "Sess3.L2.4 Take Profit")]
        public double Sess3_L2_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess3.L2.4 Take Profit")]
        public double Sess3_L2_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 L2 TP Candle Multiplier", Order = 4, GroupName = "Sess3.L2.4 Take Profit")]
        public double Sess3_L2_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Enable Break Even", Order = 1, GroupName = "Sess3.L2.5 Break Even")]
        public bool Sess3_L2_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Break Even Trigger Type", Order = 2, GroupName = "Sess3.L2.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess3_L2_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Break Even Trigger Value", Order = 3, GroupName = "Sess3.L2.5 Break Even")]
        public double Sess3_L2_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Break Even Offset (Points)", Order = 4, GroupName = "Sess3.L2.5 Break Even")]
        public double Sess3_L2_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Enable Price Trail Stop", Order = 1, GroupName = "Sess3.L2.6 Trailing Stop")]
        public bool Sess3_L2_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Trail Distance Mode", Order = 2, GroupName = "Sess3.L2.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess3_L2_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 L2 Trail Distance Value", Order = 3, GroupName = "Sess3.L2.6 Trailing Stop")]
        public double Sess3_L2_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Trail Activation Mode", Order = 4, GroupName = "Sess3.L2.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess3_L2_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Trail Activation Value", Order = 5, GroupName = "Sess3.L2.6 Trailing Stop")]
        public double Sess3_L2_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S3 L2 Trail Step (Ticks)", Order = 6, GroupName = "Sess3.L2.6 Trailing Stop")]
        public int Sess3_L2_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Enable Body % Filter", Order = 1, GroupName = "Sess3.L2.7 Signal Filters")]
        public bool Sess3_L2_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S3 L2 Min Body % of Range", Order = 2, GroupName = "Sess3.L2.7 Signal Filters")]
        public double Sess3_L2_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Enable Direction Flip", Order = 1, GroupName = "Sess3.L2.8 Direction Flip")]
        public bool Sess3_L2_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Enable Max Daily Loss", Order = 1, GroupName = "Sess3.L2.9 Risk Management")]
        public bool Sess3_L2_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Max Daily Loss (Points)", Order = 2, GroupName = "Sess3.L2.9 Risk Management")]
        public double Sess3_L2_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Enable Max Daily Profit", Order = 3, GroupName = "Sess3.L2.9 Risk Management")]
        public bool Sess3_L2_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 L2 Max Daily Profit (Points)", Order = 4, GroupName = "Sess3.L2.9 Risk Management")]
        public double Sess3_L2_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 L2 Enable Max Trades Per Day", Order = 5, GroupName = "Sess3.L2.9 Risk Management")]
        public bool Sess3_L2_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 L2 Max Trades Per Day", Order = 6, GroupName = "Sess3.L2.9 Risk Management")]
        public int Sess3_L2_MaxTradesPerDay { get; set; }

        // ── Sess3 S1 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess3.S1.0 Bucket Settings")]
        public bool Sess3_S1_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess3.S1.0 Bucket Settings")]
        public double Sess3_S1_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess3.S1.0 Bucket Settings")]
        public double Sess3_S1_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 S1 EMA Length", Order = 1, GroupName = "Sess3.S1.1 EMA Settings")]
        public int Sess3_S1_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Enable EMA Slope Filter", Order = 2, GroupName = "Sess3.S1.1 EMA Settings")]
        public bool Sess3_S1_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 EMA Slope Mode", Order = 3, GroupName = "Sess3.S1.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess3_S1_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 S1 EMA Slope Bars", Order = 4, GroupName = "Sess3.S1.1 EMA Settings")]
        public int Sess3_S1_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 EMA Min Slope %", Order = 5, GroupName = "Sess3.S1.1 EMA Settings")]
        public double Sess3_S1_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Enable WMA Filter", Order = 1, GroupName = "Sess3.S1.1b WMA Filter")]
        public bool Sess3_S1_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 S1 WMA Length", Order = 2, GroupName = "Sess3.S1.1b WMA Filter")]
        public int Sess3_S1_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Stop Loss Type", Order = 1, GroupName = "Sess3.S1.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess3_S1_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess3.S1.3 Stop Loss")]
        public double Sess3_S1_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 S1 SL Candle Multiplier", Order = 3, GroupName = "Sess3.S1.3 Stop Loss")]
        public double Sess3_S1_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess3.S1.3 Stop Loss")]
        public double Sess3_S1_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess3.S1.3 Stop Loss")]
        public double Sess3_S1_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Take Profit Type", Order = 1, GroupName = "Sess3.S1.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess3_S1_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S3 S1 Risk Reward Ratio", Order = 2, GroupName = "Sess3.S1.4 Take Profit")]
        public double Sess3_S1_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess3.S1.4 Take Profit")]
        public double Sess3_S1_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 S1 TP Candle Multiplier", Order = 4, GroupName = "Sess3.S1.4 Take Profit")]
        public double Sess3_S1_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Enable Break Even", Order = 1, GroupName = "Sess3.S1.5 Break Even")]
        public bool Sess3_S1_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Break Even Trigger Type", Order = 2, GroupName = "Sess3.S1.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess3_S1_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Break Even Trigger Value", Order = 3, GroupName = "Sess3.S1.5 Break Even")]
        public double Sess3_S1_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Break Even Offset (Points)", Order = 4, GroupName = "Sess3.S1.5 Break Even")]
        public double Sess3_S1_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Enable Price Trail Stop", Order = 1, GroupName = "Sess3.S1.6 Trailing Stop")]
        public bool Sess3_S1_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Trail Distance Mode", Order = 2, GroupName = "Sess3.S1.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess3_S1_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 S1 Trail Distance Value", Order = 3, GroupName = "Sess3.S1.6 Trailing Stop")]
        public double Sess3_S1_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Trail Activation Mode", Order = 4, GroupName = "Sess3.S1.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess3_S1_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Trail Activation Value", Order = 5, GroupName = "Sess3.S1.6 Trailing Stop")]
        public double Sess3_S1_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S3 S1 Trail Step (Ticks)", Order = 6, GroupName = "Sess3.S1.6 Trailing Stop")]
        public int Sess3_S1_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Enable Body % Filter", Order = 1, GroupName = "Sess3.S1.7 Signal Filters")]
        public bool Sess3_S1_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S3 S1 Min Body % of Range", Order = 2, GroupName = "Sess3.S1.7 Signal Filters")]
        public double Sess3_S1_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Enable Direction Flip", Order = 1, GroupName = "Sess3.S1.8 Direction Flip")]
        public bool Sess3_S1_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Enable Max Daily Loss", Order = 1, GroupName = "Sess3.S1.9 Risk Management")]
        public bool Sess3_S1_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Max Daily Loss (Points)", Order = 2, GroupName = "Sess3.S1.9 Risk Management")]
        public double Sess3_S1_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Enable Max Daily Profit", Order = 3, GroupName = "Sess3.S1.9 Risk Management")]
        public bool Sess3_S1_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S1 Max Daily Profit (Points)", Order = 4, GroupName = "Sess3.S1.9 Risk Management")]
        public double Sess3_S1_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S1 Enable Max Trades Per Day", Order = 5, GroupName = "Sess3.S1.9 Risk Management")]
        public bool Sess3_S1_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 S1 Max Trades Per Day", Order = 6, GroupName = "Sess3.S1.9 Risk Management")]
        public int Sess3_S1_MaxTradesPerDay { get; set; }

        // ── Sess3 S2 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess3.S2.0 Bucket Settings")]
        public bool Sess3_S2_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess3.S2.0 Bucket Settings")]
        public double Sess3_S2_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess3.S2.0 Bucket Settings")]
        public double Sess3_S2_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 S2 EMA Length", Order = 1, GroupName = "Sess3.S2.1 EMA Settings")]
        public int Sess3_S2_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Enable EMA Slope Filter", Order = 2, GroupName = "Sess3.S2.1 EMA Settings")]
        public bool Sess3_S2_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 EMA Slope Mode", Order = 3, GroupName = "Sess3.S2.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess3_S2_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 S2 EMA Slope Bars", Order = 4, GroupName = "Sess3.S2.1 EMA Settings")]
        public int Sess3_S2_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 EMA Min Slope %", Order = 5, GroupName = "Sess3.S2.1 EMA Settings")]
        public double Sess3_S2_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Enable WMA Filter", Order = 1, GroupName = "Sess3.S2.1b WMA Filter")]
        public bool Sess3_S2_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 S2 WMA Length", Order = 2, GroupName = "Sess3.S2.1b WMA Filter")]
        public int Sess3_S2_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Stop Loss Type", Order = 1, GroupName = "Sess3.S2.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess3_S2_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess3.S2.3 Stop Loss")]
        public double Sess3_S2_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 S2 SL Candle Multiplier", Order = 3, GroupName = "Sess3.S2.3 Stop Loss")]
        public double Sess3_S2_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess3.S2.3 Stop Loss")]
        public double Sess3_S2_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess3.S2.3 Stop Loss")]
        public double Sess3_S2_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Take Profit Type", Order = 1, GroupName = "Sess3.S2.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess3_S2_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S3 S2 Risk Reward Ratio", Order = 2, GroupName = "Sess3.S2.4 Take Profit")]
        public double Sess3_S2_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess3.S2.4 Take Profit")]
        public double Sess3_S2_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 S2 TP Candle Multiplier", Order = 4, GroupName = "Sess3.S2.4 Take Profit")]
        public double Sess3_S2_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Enable Break Even", Order = 1, GroupName = "Sess3.S2.5 Break Even")]
        public bool Sess3_S2_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Break Even Trigger Type", Order = 2, GroupName = "Sess3.S2.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess3_S2_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Break Even Trigger Value", Order = 3, GroupName = "Sess3.S2.5 Break Even")]
        public double Sess3_S2_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Break Even Offset (Points)", Order = 4, GroupName = "Sess3.S2.5 Break Even")]
        public double Sess3_S2_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Enable Price Trail Stop", Order = 1, GroupName = "Sess3.S2.6 Trailing Stop")]
        public bool Sess3_S2_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Trail Distance Mode", Order = 2, GroupName = "Sess3.S2.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess3_S2_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S3 S2 Trail Distance Value", Order = 3, GroupName = "Sess3.S2.6 Trailing Stop")]
        public double Sess3_S2_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Trail Activation Mode", Order = 4, GroupName = "Sess3.S2.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess3_S2_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Trail Activation Value", Order = 5, GroupName = "Sess3.S2.6 Trailing Stop")]
        public double Sess3_S2_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S3 S2 Trail Step (Ticks)", Order = 6, GroupName = "Sess3.S2.6 Trailing Stop")]
        public int Sess3_S2_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Enable Body % Filter", Order = 1, GroupName = "Sess3.S2.7 Signal Filters")]
        public bool Sess3_S2_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S3 S2 Min Body % of Range", Order = 2, GroupName = "Sess3.S2.7 Signal Filters")]
        public double Sess3_S2_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Enable Direction Flip", Order = 1, GroupName = "Sess3.S2.8 Direction Flip")]
        public bool Sess3_S2_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Enable Max Daily Loss", Order = 1, GroupName = "Sess3.S2.9 Risk Management")]
        public bool Sess3_S2_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Max Daily Loss (Points)", Order = 2, GroupName = "Sess3.S2.9 Risk Management")]
        public double Sess3_S2_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Enable Max Daily Profit", Order = 3, GroupName = "Sess3.S2.9 Risk Management")]
        public bool Sess3_S2_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S3 S2 Max Daily Profit (Points)", Order = 4, GroupName = "Sess3.S2.9 Risk Management")]
        public double Sess3_S2_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S3 S2 Enable Max Trades Per Day", Order = 5, GroupName = "Sess3.S2.9 Risk Management")]
        public bool Sess3_S2_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S3 S2 Max Trades Per Day", Order = 6, GroupName = "Sess3.S2.9 Risk Management")]
        public int Sess3_S2_MaxTradesPerDay { get; set; }

        #endregion

        // ════════════ SESSION 4 ════════════
        #region Session 4 Bucket Parameters

        // ── Sess4 L1 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess4.L1.0 Bucket Settings")]
        public bool Sess4_L1_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess4.L1.0 Bucket Settings")]
        public double Sess4_L1_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess4.L1.0 Bucket Settings")]
        public double Sess4_L1_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 L1 EMA Length", Order = 1, GroupName = "Sess4.L1.1 EMA Settings")]
        public int Sess4_L1_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Enable EMA Slope Filter", Order = 2, GroupName = "Sess4.L1.1 EMA Settings")]
        public bool Sess4_L1_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 EMA Slope Mode", Order = 3, GroupName = "Sess4.L1.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess4_L1_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 L1 EMA Slope Bars", Order = 4, GroupName = "Sess4.L1.1 EMA Settings")]
        public int Sess4_L1_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 EMA Min Slope %", Order = 5, GroupName = "Sess4.L1.1 EMA Settings")]
        public double Sess4_L1_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Enable WMA Filter", Order = 1, GroupName = "Sess4.L1.1b WMA Filter")]
        public bool Sess4_L1_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 L1 WMA Length", Order = 2, GroupName = "Sess4.L1.1b WMA Filter")]
        public int Sess4_L1_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Stop Loss Type", Order = 1, GroupName = "Sess4.L1.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess4_L1_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess4.L1.3 Stop Loss")]
        public double Sess4_L1_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 L1 SL Candle Multiplier", Order = 3, GroupName = "Sess4.L1.3 Stop Loss")]
        public double Sess4_L1_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess4.L1.3 Stop Loss")]
        public double Sess4_L1_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess4.L1.3 Stop Loss")]
        public double Sess4_L1_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Take Profit Type", Order = 1, GroupName = "Sess4.L1.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess4_L1_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S4 L1 Risk Reward Ratio", Order = 2, GroupName = "Sess4.L1.4 Take Profit")]
        public double Sess4_L1_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess4.L1.4 Take Profit")]
        public double Sess4_L1_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 L1 TP Candle Multiplier", Order = 4, GroupName = "Sess4.L1.4 Take Profit")]
        public double Sess4_L1_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Enable Break Even", Order = 1, GroupName = "Sess4.L1.5 Break Even")]
        public bool Sess4_L1_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Break Even Trigger Type", Order = 2, GroupName = "Sess4.L1.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess4_L1_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Break Even Trigger Value", Order = 3, GroupName = "Sess4.L1.5 Break Even")]
        public double Sess4_L1_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Break Even Offset (Points)", Order = 4, GroupName = "Sess4.L1.5 Break Even")]
        public double Sess4_L1_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Enable Price Trail Stop", Order = 1, GroupName = "Sess4.L1.6 Trailing Stop")]
        public bool Sess4_L1_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Trail Distance Mode", Order = 2, GroupName = "Sess4.L1.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess4_L1_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 L1 Trail Distance Value", Order = 3, GroupName = "Sess4.L1.6 Trailing Stop")]
        public double Sess4_L1_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Trail Activation Mode", Order = 4, GroupName = "Sess4.L1.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess4_L1_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Trail Activation Value", Order = 5, GroupName = "Sess4.L1.6 Trailing Stop")]
        public double Sess4_L1_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S4 L1 Trail Step (Ticks)", Order = 6, GroupName = "Sess4.L1.6 Trailing Stop")]
        public int Sess4_L1_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Enable Body % Filter", Order = 1, GroupName = "Sess4.L1.7 Signal Filters")]
        public bool Sess4_L1_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S4 L1 Min Body % of Range", Order = 2, GroupName = "Sess4.L1.7 Signal Filters")]
        public double Sess4_L1_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Enable Direction Flip", Order = 1, GroupName = "Sess4.L1.8 Direction Flip")]
        public bool Sess4_L1_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Enable Max Daily Loss", Order = 1, GroupName = "Sess4.L1.9 Risk Management")]
        public bool Sess4_L1_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Max Daily Loss (Points)", Order = 2, GroupName = "Sess4.L1.9 Risk Management")]
        public double Sess4_L1_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Enable Max Daily Profit", Order = 3, GroupName = "Sess4.L1.9 Risk Management")]
        public bool Sess4_L1_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L1 Max Daily Profit (Points)", Order = 4, GroupName = "Sess4.L1.9 Risk Management")]
        public double Sess4_L1_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L1 Enable Max Trades Per Day", Order = 5, GroupName = "Sess4.L1.9 Risk Management")]
        public bool Sess4_L1_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 L1 Max Trades Per Day", Order = 6, GroupName = "Sess4.L1.9 Risk Management")]
        public int Sess4_L1_MaxTradesPerDay { get; set; }

        // ── Sess4 L2 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess4.L2.0 Bucket Settings")]
        public bool Sess4_L2_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess4.L2.0 Bucket Settings")]
        public double Sess4_L2_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess4.L2.0 Bucket Settings")]
        public double Sess4_L2_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 L2 EMA Length", Order = 1, GroupName = "Sess4.L2.1 EMA Settings")]
        public int Sess4_L2_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Enable EMA Slope Filter", Order = 2, GroupName = "Sess4.L2.1 EMA Settings")]
        public bool Sess4_L2_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 EMA Slope Mode", Order = 3, GroupName = "Sess4.L2.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess4_L2_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 L2 EMA Slope Bars", Order = 4, GroupName = "Sess4.L2.1 EMA Settings")]
        public int Sess4_L2_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 EMA Min Slope %", Order = 5, GroupName = "Sess4.L2.1 EMA Settings")]
        public double Sess4_L2_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Enable WMA Filter", Order = 1, GroupName = "Sess4.L2.1b WMA Filter")]
        public bool Sess4_L2_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 L2 WMA Length", Order = 2, GroupName = "Sess4.L2.1b WMA Filter")]
        public int Sess4_L2_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Stop Loss Type", Order = 1, GroupName = "Sess4.L2.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess4_L2_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess4.L2.3 Stop Loss")]
        public double Sess4_L2_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 L2 SL Candle Multiplier", Order = 3, GroupName = "Sess4.L2.3 Stop Loss")]
        public double Sess4_L2_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess4.L2.3 Stop Loss")]
        public double Sess4_L2_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess4.L2.3 Stop Loss")]
        public double Sess4_L2_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Take Profit Type", Order = 1, GroupName = "Sess4.L2.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess4_L2_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S4 L2 Risk Reward Ratio", Order = 2, GroupName = "Sess4.L2.4 Take Profit")]
        public double Sess4_L2_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess4.L2.4 Take Profit")]
        public double Sess4_L2_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 L2 TP Candle Multiplier", Order = 4, GroupName = "Sess4.L2.4 Take Profit")]
        public double Sess4_L2_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Enable Break Even", Order = 1, GroupName = "Sess4.L2.5 Break Even")]
        public bool Sess4_L2_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Break Even Trigger Type", Order = 2, GroupName = "Sess4.L2.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess4_L2_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Break Even Trigger Value", Order = 3, GroupName = "Sess4.L2.5 Break Even")]
        public double Sess4_L2_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Break Even Offset (Points)", Order = 4, GroupName = "Sess4.L2.5 Break Even")]
        public double Sess4_L2_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Enable Price Trail Stop", Order = 1, GroupName = "Sess4.L2.6 Trailing Stop")]
        public bool Sess4_L2_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Trail Distance Mode", Order = 2, GroupName = "Sess4.L2.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess4_L2_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 L2 Trail Distance Value", Order = 3, GroupName = "Sess4.L2.6 Trailing Stop")]
        public double Sess4_L2_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Trail Activation Mode", Order = 4, GroupName = "Sess4.L2.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess4_L2_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Trail Activation Value", Order = 5, GroupName = "Sess4.L2.6 Trailing Stop")]
        public double Sess4_L2_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S4 L2 Trail Step (Ticks)", Order = 6, GroupName = "Sess4.L2.6 Trailing Stop")]
        public int Sess4_L2_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Enable Body % Filter", Order = 1, GroupName = "Sess4.L2.7 Signal Filters")]
        public bool Sess4_L2_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S4 L2 Min Body % of Range", Order = 2, GroupName = "Sess4.L2.7 Signal Filters")]
        public double Sess4_L2_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Enable Direction Flip", Order = 1, GroupName = "Sess4.L2.8 Direction Flip")]
        public bool Sess4_L2_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Enable Max Daily Loss", Order = 1, GroupName = "Sess4.L2.9 Risk Management")]
        public bool Sess4_L2_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Max Daily Loss (Points)", Order = 2, GroupName = "Sess4.L2.9 Risk Management")]
        public double Sess4_L2_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Enable Max Daily Profit", Order = 3, GroupName = "Sess4.L2.9 Risk Management")]
        public bool Sess4_L2_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 L2 Max Daily Profit (Points)", Order = 4, GroupName = "Sess4.L2.9 Risk Management")]
        public double Sess4_L2_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 L2 Enable Max Trades Per Day", Order = 5, GroupName = "Sess4.L2.9 Risk Management")]
        public bool Sess4_L2_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 L2 Max Trades Per Day", Order = 6, GroupName = "Sess4.L2.9 Risk Management")]
        public int Sess4_L2_MaxTradesPerDay { get; set; }

        // ── Sess4 S1 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess4.S1.0 Bucket Settings")]
        public bool Sess4_S1_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess4.S1.0 Bucket Settings")]
        public double Sess4_S1_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess4.S1.0 Bucket Settings")]
        public double Sess4_S1_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 S1 EMA Length", Order = 1, GroupName = "Sess4.S1.1 EMA Settings")]
        public int Sess4_S1_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Enable EMA Slope Filter", Order = 2, GroupName = "Sess4.S1.1 EMA Settings")]
        public bool Sess4_S1_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 EMA Slope Mode", Order = 3, GroupName = "Sess4.S1.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess4_S1_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 S1 EMA Slope Bars", Order = 4, GroupName = "Sess4.S1.1 EMA Settings")]
        public int Sess4_S1_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 EMA Min Slope %", Order = 5, GroupName = "Sess4.S1.1 EMA Settings")]
        public double Sess4_S1_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Enable WMA Filter", Order = 1, GroupName = "Sess4.S1.1b WMA Filter")]
        public bool Sess4_S1_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 S1 WMA Length", Order = 2, GroupName = "Sess4.S1.1b WMA Filter")]
        public int Sess4_S1_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Stop Loss Type", Order = 1, GroupName = "Sess4.S1.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess4_S1_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess4.S1.3 Stop Loss")]
        public double Sess4_S1_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 S1 SL Candle Multiplier", Order = 3, GroupName = "Sess4.S1.3 Stop Loss")]
        public double Sess4_S1_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess4.S1.3 Stop Loss")]
        public double Sess4_S1_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess4.S1.3 Stop Loss")]
        public double Sess4_S1_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Take Profit Type", Order = 1, GroupName = "Sess4.S1.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess4_S1_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S4 S1 Risk Reward Ratio", Order = 2, GroupName = "Sess4.S1.4 Take Profit")]
        public double Sess4_S1_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess4.S1.4 Take Profit")]
        public double Sess4_S1_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 S1 TP Candle Multiplier", Order = 4, GroupName = "Sess4.S1.4 Take Profit")]
        public double Sess4_S1_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Enable Break Even", Order = 1, GroupName = "Sess4.S1.5 Break Even")]
        public bool Sess4_S1_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Break Even Trigger Type", Order = 2, GroupName = "Sess4.S1.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess4_S1_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Break Even Trigger Value", Order = 3, GroupName = "Sess4.S1.5 Break Even")]
        public double Sess4_S1_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Break Even Offset (Points)", Order = 4, GroupName = "Sess4.S1.5 Break Even")]
        public double Sess4_S1_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Enable Price Trail Stop", Order = 1, GroupName = "Sess4.S1.6 Trailing Stop")]
        public bool Sess4_S1_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Trail Distance Mode", Order = 2, GroupName = "Sess4.S1.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess4_S1_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 S1 Trail Distance Value", Order = 3, GroupName = "Sess4.S1.6 Trailing Stop")]
        public double Sess4_S1_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Trail Activation Mode", Order = 4, GroupName = "Sess4.S1.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess4_S1_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Trail Activation Value", Order = 5, GroupName = "Sess4.S1.6 Trailing Stop")]
        public double Sess4_S1_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S4 S1 Trail Step (Ticks)", Order = 6, GroupName = "Sess4.S1.6 Trailing Stop")]
        public int Sess4_S1_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Enable Body % Filter", Order = 1, GroupName = "Sess4.S1.7 Signal Filters")]
        public bool Sess4_S1_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S4 S1 Min Body % of Range", Order = 2, GroupName = "Sess4.S1.7 Signal Filters")]
        public double Sess4_S1_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Enable Direction Flip", Order = 1, GroupName = "Sess4.S1.8 Direction Flip")]
        public bool Sess4_S1_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Enable Max Daily Loss", Order = 1, GroupName = "Sess4.S1.9 Risk Management")]
        public bool Sess4_S1_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Max Daily Loss (Points)", Order = 2, GroupName = "Sess4.S1.9 Risk Management")]
        public double Sess4_S1_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Enable Max Daily Profit", Order = 3, GroupName = "Sess4.S1.9 Risk Management")]
        public bool Sess4_S1_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S1 Max Daily Profit (Points)", Order = 4, GroupName = "Sess4.S1.9 Risk Management")]
        public double Sess4_S1_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S1 Enable Max Trades Per Day", Order = 5, GroupName = "Sess4.S1.9 Risk Management")]
        public bool Sess4_S1_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 S1 Max Trades Per Day", Order = 6, GroupName = "Sess4.S1.9 Risk Management")]
        public int Sess4_S1_MaxTradesPerDay { get; set; }

        // ── Sess4 S2 ─────────────────────────────────────
        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Enable", Description = "Enable this bucket", Order = 1, GroupName = "Sess4.S2.0 Bucket Settings")]
        public bool Sess4_S2_Enable { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Min Candle Range (Points)", Description = "Minimum signal candle range", Order = 2, GroupName = "Sess4.S2.0 Bucket Settings")]
        public double Sess4_S2_MinCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Max Candle Range (Points)", Description = "0 = no max", Order = 3, GroupName = "Sess4.S2.0 Bucket Settings")]
        public double Sess4_S2_MaxCandleRange { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 S2 EMA Length", Order = 1, GroupName = "Sess4.S2.1 EMA Settings")]
        public int Sess4_S2_EmaLength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Enable EMA Slope Filter", Order = 2, GroupName = "Sess4.S2.1 EMA Settings")]
        public bool Sess4_S2_EnableEmaSlope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 EMA Slope Mode", Order = 3, GroupName = "Sess4.S2.1 EMA Settings")]
        public HugoTesting_EmaSlopeModeEnum Sess4_S2_EmaSlopeMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 S2 EMA Slope Bars", Order = 4, GroupName = "Sess4.S2.1 EMA Settings")]
        public int Sess4_S2_EmaSlopeBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 EMA Min Slope %", Order = 5, GroupName = "Sess4.S2.1 EMA Settings")]
        public double Sess4_S2_EmaSlopeMinPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Enable WMA Filter", Order = 1, GroupName = "Sess4.S2.1b WMA Filter")]
        public bool Sess4_S2_EnableWMAFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 S2 WMA Length", Order = 2, GroupName = "Sess4.S2.1b WMA Filter")]
        public int Sess4_S2_WMALength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Stop Loss Type", Order = 1, GroupName = "Sess4.S2.3 Stop Loss")]
        public HugoTesting_StopLossTypeEnum Sess4_S2_StopLossType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Fixed Stop Loss (Points)", Order = 2, GroupName = "Sess4.S2.3 Stop Loss")]
        public double Sess4_S2_FixedStopLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 S2 SL Candle Multiplier", Order = 3, GroupName = "Sess4.S2.3 Stop Loss")]
        public double Sess4_S2_SLCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Stop Loss Offset (Points)", Order = 4, GroupName = "Sess4.S2.3 Stop Loss")]
        public double Sess4_S2_StopLossOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Maximum Stop Loss (Points)", Order = 5, GroupName = "Sess4.S2.3 Stop Loss")]
        public double Sess4_S2_MaxStopLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Take Profit Type", Order = 1, GroupName = "Sess4.S2.4 Take Profit")]
        public HugoTesting_TakeProfitTypeEnum Sess4_S2_TakeProfitType { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "S4 S2 Risk Reward Ratio", Order = 2, GroupName = "Sess4.S2.4 Take Profit")]
        public double Sess4_S2_RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Fixed Take Profit (Points)", Order = 3, GroupName = "Sess4.S2.4 Take Profit")]
        public double Sess4_S2_FixedTakeProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 S2 TP Candle Multiplier", Order = 4, GroupName = "Sess4.S2.4 Take Profit")]
        public double Sess4_S2_TPCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Enable Break Even", Order = 1, GroupName = "Sess4.S2.5 Break Even")]
        public bool Sess4_S2_EnableBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Break Even Trigger Type", Order = 2, GroupName = "Sess4.S2.5 Break Even")]
        public HugoTesting_BreakEvenTriggerEnum Sess4_S2_BreakEvenTriggerType { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Break Even Trigger Value", Order = 3, GroupName = "Sess4.S2.5 Break Even")]
        public double Sess4_S2_BreakEvenTriggerValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Break Even Offset (Points)", Order = 4, GroupName = "Sess4.S2.5 Break Even")]
        public double Sess4_S2_BreakEvenOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Enable Price Trail Stop", Order = 1, GroupName = "Sess4.S2.6 Trailing Stop")]
        public bool Sess4_S2_EnablePriceTrail { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Trail Distance Mode", Order = 2, GroupName = "Sess4.S2.6 Trailing Stop")]
        public HugoTesting_TrailDistanceModeEnum Sess4_S2_TrailDistanceMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "S4 S2 Trail Distance Value", Order = 3, GroupName = "Sess4.S2.6 Trailing Stop")]
        public double Sess4_S2_TrailDistanceValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Trail Activation Mode", Order = 4, GroupName = "Sess4.S2.6 Trailing Stop")]
        public HugoTesting_TrailActivationModeEnum Sess4_S2_TrailActivationMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Trail Activation Value", Order = 5, GroupName = "Sess4.S2.6 Trailing Stop")]
        public double Sess4_S2_TrailActivationValue { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "S4 S2 Trail Step (Ticks)", Order = 6, GroupName = "Sess4.S2.6 Trailing Stop")]
        public int Sess4_S2_TrailStepTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Enable Body % Filter", Order = 1, GroupName = "Sess4.S2.7 Signal Filters")]
        public bool Sess4_S2_EnableBodyPctFilter { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "S4 S2 Min Body % of Range", Order = 2, GroupName = "Sess4.S2.7 Signal Filters")]
        public double Sess4_S2_MinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Enable Direction Flip", Order = 1, GroupName = "Sess4.S2.8 Direction Flip")]
        public bool Sess4_S2_EnableDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Enable Max Daily Loss", Order = 1, GroupName = "Sess4.S2.9 Risk Management")]
        public bool Sess4_S2_EnableMaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Max Daily Loss (Points)", Order = 2, GroupName = "Sess4.S2.9 Risk Management")]
        public double Sess4_S2_MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Enable Max Daily Profit", Order = 3, GroupName = "Sess4.S2.9 Risk Management")]
        public bool Sess4_S2_EnableMaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "S4 S2 Max Daily Profit (Points)", Order = 4, GroupName = "Sess4.S2.9 Risk Management")]
        public double Sess4_S2_MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "S4 S2 Enable Max Trades Per Day", Order = 5, GroupName = "Sess4.S2.9 Risk Management")]
        public bool Sess4_S2_EnableMaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "S4 S2 Max Trades Per Day", Order = 6, GroupName = "Sess4.S2.9 Risk Management")]
        public int Sess4_S2_MaxTradesPerDay { get; set; }

        #endregion


        // ════════════════════════════════════════════════════════════════════════
        //  ACCESSORS
        // ════════════════════════════════════════════════════════════════════════

        #region Accessors

        private EMA GetEma(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return emaSess1_L1;
                case (1, "L2"): return emaSess1_L2;
                case (1, "S1"): return emaSess1_S1;
                case (1, "S2"): return emaSess1_S2;
                case (2, "L1"): return emaSess2_L1;
                case (2, "L2"): return emaSess2_L2;
                case (2, "S1"): return emaSess2_S1;
                case (2, "S2"): return emaSess2_S2;
                case (3, "L1"): return emaSess3_L1;
                case (3, "L2"): return emaSess3_L2;
                case (3, "S1"): return emaSess3_S1;
                case (3, "S2"): return emaSess3_S2;
                case (4, "L1"): return emaSess4_L1;
                case (4, "L2"): return emaSess4_L2;
                case (4, "S1"): return emaSess4_S1;
                case (4, "S2"): return emaSess4_S2;
                default: return emaSess1_L1;
            }
        }

        private WMA GetWma(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return wmaSess1_L1;
                case (1, "L2"): return wmaSess1_L2;
                case (1, "S1"): return wmaSess1_S1;
                case (1, "S2"): return wmaSess1_S2;
                case (2, "L1"): return wmaSess2_L1;
                case (2, "L2"): return wmaSess2_L2;
                case (2, "S1"): return wmaSess2_S1;
                case (2, "S2"): return wmaSess2_S2;
                case (3, "L1"): return wmaSess3_L1;
                case (3, "L2"): return wmaSess3_L2;
                case (3, "S1"): return wmaSess3_S1;
                case (3, "S2"): return wmaSess3_S2;
                case (4, "L1"): return wmaSess4_L1;
                case (4, "L2"): return wmaSess4_L2;
                case (4, "S1"): return wmaSess4_S1;
                case (4, "S2"): return wmaSess4_S2;
                default: return wmaSess1_L1;
            }
        }

        private bool IsBucketEnabled(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_Enable;
                case (1, "L2"): return Sess1_L2_Enable;
                case (1, "S1"): return Sess1_S1_Enable;
                case (1, "S2"): return Sess1_S2_Enable;
                case (2, "L1"): return Sess2_L1_Enable;
                case (2, "L2"): return Sess2_L2_Enable;
                case (2, "S1"): return Sess2_S1_Enable;
                case (2, "S2"): return Sess2_S2_Enable;
                case (3, "L1"): return Sess3_L1_Enable;
                case (3, "L2"): return Sess3_L2_Enable;
                case (3, "S1"): return Sess3_S1_Enable;
                case (3, "S2"): return Sess3_S2_Enable;
                case (4, "L1"): return Sess4_L1_Enable;
                case (4, "L2"): return Sess4_L2_Enable;
                case (4, "S1"): return Sess4_S1_Enable;
                case (4, "S2"): return Sess4_S2_Enable;
                default: return false;
            }
        }

        private bool B_EnableEmaSlope(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EnableEmaSlope;
                case (1, "L2"): return Sess1_L2_EnableEmaSlope;
                case (1, "S1"): return Sess1_S1_EnableEmaSlope;
                case (1, "S2"): return Sess1_S2_EnableEmaSlope;
                case (2, "L1"): return Sess2_L1_EnableEmaSlope;
                case (2, "L2"): return Sess2_L2_EnableEmaSlope;
                case (2, "S1"): return Sess2_S1_EnableEmaSlope;
                case (2, "S2"): return Sess2_S2_EnableEmaSlope;
                case (3, "L1"): return Sess3_L1_EnableEmaSlope;
                case (3, "L2"): return Sess3_L2_EnableEmaSlope;
                case (3, "S1"): return Sess3_S1_EnableEmaSlope;
                case (3, "S2"): return Sess3_S2_EnableEmaSlope;
                case (4, "L1"): return Sess4_L1_EnableEmaSlope;
                case (4, "L2"): return Sess4_L2_EnableEmaSlope;
                case (4, "S1"): return Sess4_S1_EnableEmaSlope;
                case (4, "S2"): return Sess4_S2_EnableEmaSlope;
                default: return false;
            }
        }

        private HugoTesting_EmaSlopeModeEnum B_EmaSlopeMode(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EmaSlopeMode;
                case (1, "L2"): return Sess1_L2_EmaSlopeMode;
                case (1, "S1"): return Sess1_S1_EmaSlopeMode;
                case (1, "S2"): return Sess1_S2_EmaSlopeMode;
                case (2, "L1"): return Sess2_L1_EmaSlopeMode;
                case (2, "L2"): return Sess2_L2_EmaSlopeMode;
                case (2, "S1"): return Sess2_S1_EmaSlopeMode;
                case (2, "S2"): return Sess2_S2_EmaSlopeMode;
                case (3, "L1"): return Sess3_L1_EmaSlopeMode;
                case (3, "L2"): return Sess3_L2_EmaSlopeMode;
                case (3, "S1"): return Sess3_S1_EmaSlopeMode;
                case (3, "S2"): return Sess3_S2_EmaSlopeMode;
                case (4, "L1"): return Sess4_L1_EmaSlopeMode;
                case (4, "L2"): return Sess4_L2_EmaSlopeMode;
                case (4, "S1"): return Sess4_S1_EmaSlopeMode;
                case (4, "S2"): return Sess4_S2_EmaSlopeMode;
                default: return HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
            }
        }

        private int B_EmaSlopeBars(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EmaSlopeBars;
                case (1, "L2"): return Sess1_L2_EmaSlopeBars;
                case (1, "S1"): return Sess1_S1_EmaSlopeBars;
                case (1, "S2"): return Sess1_S2_EmaSlopeBars;
                case (2, "L1"): return Sess2_L1_EmaSlopeBars;
                case (2, "L2"): return Sess2_L2_EmaSlopeBars;
                case (2, "S1"): return Sess2_S1_EmaSlopeBars;
                case (2, "S2"): return Sess2_S2_EmaSlopeBars;
                case (3, "L1"): return Sess3_L1_EmaSlopeBars;
                case (3, "L2"): return Sess3_L2_EmaSlopeBars;
                case (3, "S1"): return Sess3_S1_EmaSlopeBars;
                case (3, "S2"): return Sess3_S2_EmaSlopeBars;
                case (4, "L1"): return Sess4_L1_EmaSlopeBars;
                case (4, "L2"): return Sess4_L2_EmaSlopeBars;
                case (4, "S1"): return Sess4_S1_EmaSlopeBars;
                case (4, "S2"): return Sess4_S2_EmaSlopeBars;
                default: return 49;
            }
        }

        private double B_EmaSlopeMinPct(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EmaSlopeMinPct;
                case (1, "L2"): return Sess1_L2_EmaSlopeMinPct;
                case (1, "S1"): return Sess1_S1_EmaSlopeMinPct;
                case (1, "S2"): return Sess1_S2_EmaSlopeMinPct;
                case (2, "L1"): return Sess2_L1_EmaSlopeMinPct;
                case (2, "L2"): return Sess2_L2_EmaSlopeMinPct;
                case (2, "S1"): return Sess2_S1_EmaSlopeMinPct;
                case (2, "S2"): return Sess2_S2_EmaSlopeMinPct;
                case (3, "L1"): return Sess3_L1_EmaSlopeMinPct;
                case (3, "L2"): return Sess3_L2_EmaSlopeMinPct;
                case (3, "S1"): return Sess3_S1_EmaSlopeMinPct;
                case (3, "S2"): return Sess3_S2_EmaSlopeMinPct;
                case (4, "L1"): return Sess4_L1_EmaSlopeMinPct;
                case (4, "L2"): return Sess4_L2_EmaSlopeMinPct;
                case (4, "S1"): return Sess4_S1_EmaSlopeMinPct;
                case (4, "S2"): return Sess4_S2_EmaSlopeMinPct;
                default: return 0;
            }
        }

        private HugoTesting_StopLossTypeEnum B_StopLossType(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_StopLossType;
                case (1, "L2"): return Sess1_L2_StopLossType;
                case (1, "S1"): return Sess1_S1_StopLossType;
                case (1, "S2"): return Sess1_S2_StopLossType;
                case (2, "L1"): return Sess2_L1_StopLossType;
                case (2, "L2"): return Sess2_L2_StopLossType;
                case (2, "S1"): return Sess2_S1_StopLossType;
                case (2, "S2"): return Sess2_S2_StopLossType;
                case (3, "L1"): return Sess3_L1_StopLossType;
                case (3, "L2"): return Sess3_L2_StopLossType;
                case (3, "S1"): return Sess3_S1_StopLossType;
                case (3, "S2"): return Sess3_S2_StopLossType;
                case (4, "L1"): return Sess4_L1_StopLossType;
                case (4, "L2"): return Sess4_L2_StopLossType;
                case (4, "S1"): return Sess4_S1_StopLossType;
                case (4, "S2"): return Sess4_S2_StopLossType;
                default: return HugoTesting_StopLossTypeEnum.Fixed;
            }
        }

        private double B_FixedStopLoss(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_FixedStopLoss;
                case (1, "L2"): return Sess1_L2_FixedStopLoss;
                case (1, "S1"): return Sess1_S1_FixedStopLoss;
                case (1, "S2"): return Sess1_S2_FixedStopLoss;
                case (2, "L1"): return Sess2_L1_FixedStopLoss;
                case (2, "L2"): return Sess2_L2_FixedStopLoss;
                case (2, "S1"): return Sess2_S1_FixedStopLoss;
                case (2, "S2"): return Sess2_S2_FixedStopLoss;
                case (3, "L1"): return Sess3_L1_FixedStopLoss;
                case (3, "L2"): return Sess3_L2_FixedStopLoss;
                case (3, "S1"): return Sess3_S1_FixedStopLoss;
                case (3, "S2"): return Sess3_S2_FixedStopLoss;
                case (4, "L1"): return Sess4_L1_FixedStopLoss;
                case (4, "L2"): return Sess4_L2_FixedStopLoss;
                case (4, "S1"): return Sess4_S1_FixedStopLoss;
                case (4, "S2"): return Sess4_S2_FixedStopLoss;
                default: return 57;
            }
        }

        private double B_StopLossOffset(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_StopLossOffset;
                case (1, "L2"): return Sess1_L2_StopLossOffset;
                case (1, "S1"): return Sess1_S1_StopLossOffset;
                case (1, "S2"): return Sess1_S2_StopLossOffset;
                case (2, "L1"): return Sess2_L1_StopLossOffset;
                case (2, "L2"): return Sess2_L2_StopLossOffset;
                case (2, "S1"): return Sess2_S1_StopLossOffset;
                case (2, "S2"): return Sess2_S2_StopLossOffset;
                case (3, "L1"): return Sess3_L1_StopLossOffset;
                case (3, "L2"): return Sess3_L2_StopLossOffset;
                case (3, "S1"): return Sess3_S1_StopLossOffset;
                case (3, "S2"): return Sess3_S2_StopLossOffset;
                case (4, "L1"): return Sess4_L1_StopLossOffset;
                case (4, "L2"): return Sess4_L2_StopLossOffset;
                case (4, "S1"): return Sess4_S1_StopLossOffset;
                case (4, "S2"): return Sess4_S2_StopLossOffset;
                default: return 56;
            }
        }

        private double B_MaxStopLoss(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_MaxStopLoss;
                case (1, "L2"): return Sess1_L2_MaxStopLoss;
                case (1, "S1"): return Sess1_S1_MaxStopLoss;
                case (1, "S2"): return Sess1_S2_MaxStopLoss;
                case (2, "L1"): return Sess2_L1_MaxStopLoss;
                case (2, "L2"): return Sess2_L2_MaxStopLoss;
                case (2, "S1"): return Sess2_S1_MaxStopLoss;
                case (2, "S2"): return Sess2_S2_MaxStopLoss;
                case (3, "L1"): return Sess3_L1_MaxStopLoss;
                case (3, "L2"): return Sess3_L2_MaxStopLoss;
                case (3, "S1"): return Sess3_S1_MaxStopLoss;
                case (3, "S2"): return Sess3_S2_MaxStopLoss;
                case (4, "L1"): return Sess4_L1_MaxStopLoss;
                case (4, "L2"): return Sess4_L2_MaxStopLoss;
                case (4, "S1"): return Sess4_S1_MaxStopLoss;
                case (4, "S2"): return Sess4_S2_MaxStopLoss;
                default: return 50;
            }
        }

        private double B_SLCandleMultiplier(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_SLCandleMultiplier;
                case (1, "L2"): return Sess1_L2_SLCandleMultiplier;
                case (1, "S1"): return Sess1_S1_SLCandleMultiplier;
                case (1, "S2"): return Sess1_S2_SLCandleMultiplier;
                case (2, "L1"): return Sess2_L1_SLCandleMultiplier;
                case (2, "L2"): return Sess2_L2_SLCandleMultiplier;
                case (2, "S1"): return Sess2_S1_SLCandleMultiplier;
                case (2, "S2"): return Sess2_S2_SLCandleMultiplier;
                case (3, "L1"): return Sess3_L1_SLCandleMultiplier;
                case (3, "L2"): return Sess3_L2_SLCandleMultiplier;
                case (3, "S1"): return Sess3_S1_SLCandleMultiplier;
                case (3, "S2"): return Sess3_S2_SLCandleMultiplier;
                case (4, "L1"): return Sess4_L1_SLCandleMultiplier;
                case (4, "L2"): return Sess4_L2_SLCandleMultiplier;
                case (4, "S1"): return Sess4_S1_SLCandleMultiplier;
                case (4, "S2"): return Sess4_S2_SLCandleMultiplier;
                default: return 1.0;
            }
        }

        private HugoTesting_TakeProfitTypeEnum B_TakeProfitType(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_TakeProfitType;
                case (1, "L2"): return Sess1_L2_TakeProfitType;
                case (1, "S1"): return Sess1_S1_TakeProfitType;
                case (1, "S2"): return Sess1_S2_TakeProfitType;
                case (2, "L1"): return Sess2_L1_TakeProfitType;
                case (2, "L2"): return Sess2_L2_TakeProfitType;
                case (2, "S1"): return Sess2_S1_TakeProfitType;
                case (2, "S2"): return Sess2_S2_TakeProfitType;
                case (3, "L1"): return Sess3_L1_TakeProfitType;
                case (3, "L2"): return Sess3_L2_TakeProfitType;
                case (3, "S1"): return Sess3_S1_TakeProfitType;
                case (3, "S2"): return Sess3_S2_TakeProfitType;
                case (4, "L1"): return Sess4_L1_TakeProfitType;
                case (4, "L2"): return Sess4_L2_TakeProfitType;
                case (4, "S1"): return Sess4_S1_TakeProfitType;
                case (4, "S2"): return Sess4_S2_TakeProfitType;
                default: return HugoTesting_TakeProfitTypeEnum.RiskReward;
            }
        }

        private double B_RiskRewardRatio(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_RiskRewardRatio;
                case (1, "L2"): return Sess1_L2_RiskRewardRatio;
                case (1, "S1"): return Sess1_S1_RiskRewardRatio;
                case (1, "S2"): return Sess1_S2_RiskRewardRatio;
                case (2, "L1"): return Sess2_L1_RiskRewardRatio;
                case (2, "L2"): return Sess2_L2_RiskRewardRatio;
                case (2, "S1"): return Sess2_S1_RiskRewardRatio;
                case (2, "S2"): return Sess2_S2_RiskRewardRatio;
                case (3, "L1"): return Sess3_L1_RiskRewardRatio;
                case (3, "L2"): return Sess3_L2_RiskRewardRatio;
                case (3, "S1"): return Sess3_S1_RiskRewardRatio;
                case (3, "S2"): return Sess3_S2_RiskRewardRatio;
                case (4, "L1"): return Sess4_L1_RiskRewardRatio;
                case (4, "L2"): return Sess4_L2_RiskRewardRatio;
                case (4, "S1"): return Sess4_S1_RiskRewardRatio;
                case (4, "S2"): return Sess4_S2_RiskRewardRatio;
                default: return 3.42;
            }
        }

        private double B_FixedTakeProfit(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_FixedTakeProfit;
                case (1, "L2"): return Sess1_L2_FixedTakeProfit;
                case (1, "S1"): return Sess1_S1_FixedTakeProfit;
                case (1, "S2"): return Sess1_S2_FixedTakeProfit;
                case (2, "L1"): return Sess2_L1_FixedTakeProfit;
                case (2, "L2"): return Sess2_L2_FixedTakeProfit;
                case (2, "S1"): return Sess2_S1_FixedTakeProfit;
                case (2, "S2"): return Sess2_S2_FixedTakeProfit;
                case (3, "L1"): return Sess3_L1_FixedTakeProfit;
                case (3, "L2"): return Sess3_L2_FixedTakeProfit;
                case (3, "S1"): return Sess3_S1_FixedTakeProfit;
                case (3, "S2"): return Sess3_S2_FixedTakeProfit;
                case (4, "L1"): return Sess4_L1_FixedTakeProfit;
                case (4, "L2"): return Sess4_L2_FixedTakeProfit;
                case (4, "S1"): return Sess4_S1_FixedTakeProfit;
                case (4, "S2"): return Sess4_S2_FixedTakeProfit;
                default: return 100;
            }
        }

        private double B_TPCandleMultiplier(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_TPCandleMultiplier;
                case (1, "L2"): return Sess1_L2_TPCandleMultiplier;
                case (1, "S1"): return Sess1_S1_TPCandleMultiplier;
                case (1, "S2"): return Sess1_S2_TPCandleMultiplier;
                case (2, "L1"): return Sess2_L1_TPCandleMultiplier;
                case (2, "L2"): return Sess2_L2_TPCandleMultiplier;
                case (2, "S1"): return Sess2_S1_TPCandleMultiplier;
                case (2, "S2"): return Sess2_S2_TPCandleMultiplier;
                case (3, "L1"): return Sess3_L1_TPCandleMultiplier;
                case (3, "L2"): return Sess3_L2_TPCandleMultiplier;
                case (3, "S1"): return Sess3_S1_TPCandleMultiplier;
                case (3, "S2"): return Sess3_S2_TPCandleMultiplier;
                case (4, "L1"): return Sess4_L1_TPCandleMultiplier;
                case (4, "L2"): return Sess4_L2_TPCandleMultiplier;
                case (4, "S1"): return Sess4_S1_TPCandleMultiplier;
                case (4, "S2"): return Sess4_S2_TPCandleMultiplier;
                default: return 1.0;
            }
        }

        private bool B_EnableBreakEven(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EnableBreakEven;
                case (1, "L2"): return Sess1_L2_EnableBreakEven;
                case (1, "S1"): return Sess1_S1_EnableBreakEven;
                case (1, "S2"): return Sess1_S2_EnableBreakEven;
                case (2, "L1"): return Sess2_L1_EnableBreakEven;
                case (2, "L2"): return Sess2_L2_EnableBreakEven;
                case (2, "S1"): return Sess2_S1_EnableBreakEven;
                case (2, "S2"): return Sess2_S2_EnableBreakEven;
                case (3, "L1"): return Sess3_L1_EnableBreakEven;
                case (3, "L2"): return Sess3_L2_EnableBreakEven;
                case (3, "S1"): return Sess3_S1_EnableBreakEven;
                case (3, "S2"): return Sess3_S2_EnableBreakEven;
                case (4, "L1"): return Sess4_L1_EnableBreakEven;
                case (4, "L2"): return Sess4_L2_EnableBreakEven;
                case (4, "S1"): return Sess4_S1_EnableBreakEven;
                case (4, "S2"): return Sess4_S2_EnableBreakEven;
                default: return false;
            }
        }

        private HugoTesting_BreakEvenTriggerEnum B_BreakEvenTriggerType(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_BreakEvenTriggerType;
                case (1, "L2"): return Sess1_L2_BreakEvenTriggerType;
                case (1, "S1"): return Sess1_S1_BreakEvenTriggerType;
                case (1, "S2"): return Sess1_S2_BreakEvenTriggerType;
                case (2, "L1"): return Sess2_L1_BreakEvenTriggerType;
                case (2, "L2"): return Sess2_L2_BreakEvenTriggerType;
                case (2, "S1"): return Sess2_S1_BreakEvenTriggerType;
                case (2, "S2"): return Sess2_S2_BreakEvenTriggerType;
                case (3, "L1"): return Sess3_L1_BreakEvenTriggerType;
                case (3, "L2"): return Sess3_L2_BreakEvenTriggerType;
                case (3, "S1"): return Sess3_S1_BreakEvenTriggerType;
                case (3, "S2"): return Sess3_S2_BreakEvenTriggerType;
                case (4, "L1"): return Sess4_L1_BreakEvenTriggerType;
                case (4, "L2"): return Sess4_L2_BreakEvenTriggerType;
                case (4, "S1"): return Sess4_S1_BreakEvenTriggerType;
                case (4, "S2"): return Sess4_S2_BreakEvenTriggerType;
                default: return HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
            }
        }

        private double B_BreakEvenTriggerValue(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_BreakEvenTriggerValue;
                case (1, "L2"): return Sess1_L2_BreakEvenTriggerValue;
                case (1, "S1"): return Sess1_S1_BreakEvenTriggerValue;
                case (1, "S2"): return Sess1_S2_BreakEvenTriggerValue;
                case (2, "L1"): return Sess2_L1_BreakEvenTriggerValue;
                case (2, "L2"): return Sess2_L2_BreakEvenTriggerValue;
                case (2, "S1"): return Sess2_S1_BreakEvenTriggerValue;
                case (2, "S2"): return Sess2_S2_BreakEvenTriggerValue;
                case (3, "L1"): return Sess3_L1_BreakEvenTriggerValue;
                case (3, "L2"): return Sess3_L2_BreakEvenTriggerValue;
                case (3, "S1"): return Sess3_S1_BreakEvenTriggerValue;
                case (3, "S2"): return Sess3_S2_BreakEvenTriggerValue;
                case (4, "L1"): return Sess4_L1_BreakEvenTriggerValue;
                case (4, "L2"): return Sess4_L2_BreakEvenTriggerValue;
                case (4, "S1"): return Sess4_S1_BreakEvenTriggerValue;
                case (4, "S2"): return Sess4_S2_BreakEvenTriggerValue;
                default: return 3.29;
            }
        }

        private double B_BreakEvenOffset(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_BreakEvenOffset;
                case (1, "L2"): return Sess1_L2_BreakEvenOffset;
                case (1, "S1"): return Sess1_S1_BreakEvenOffset;
                case (1, "S2"): return Sess1_S2_BreakEvenOffset;
                case (2, "L1"): return Sess2_L1_BreakEvenOffset;
                case (2, "L2"): return Sess2_L2_BreakEvenOffset;
                case (2, "S1"): return Sess2_S1_BreakEvenOffset;
                case (2, "S2"): return Sess2_S2_BreakEvenOffset;
                case (3, "L1"): return Sess3_L1_BreakEvenOffset;
                case (3, "L2"): return Sess3_L2_BreakEvenOffset;
                case (3, "S1"): return Sess3_S1_BreakEvenOffset;
                case (3, "S2"): return Sess3_S2_BreakEvenOffset;
                case (4, "L1"): return Sess4_L1_BreakEvenOffset;
                case (4, "L2"): return Sess4_L2_BreakEvenOffset;
                case (4, "S1"): return Sess4_S1_BreakEvenOffset;
                case (4, "S2"): return Sess4_S2_BreakEvenOffset;
                default: return 2;
            }
        }

        private bool B_EnablePriceTrail(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EnablePriceTrail;
                case (1, "L2"): return Sess1_L2_EnablePriceTrail;
                case (1, "S1"): return Sess1_S1_EnablePriceTrail;
                case (1, "S2"): return Sess1_S2_EnablePriceTrail;
                case (2, "L1"): return Sess2_L1_EnablePriceTrail;
                case (2, "L2"): return Sess2_L2_EnablePriceTrail;
                case (2, "S1"): return Sess2_S1_EnablePriceTrail;
                case (2, "S2"): return Sess2_S2_EnablePriceTrail;
                case (3, "L1"): return Sess3_L1_EnablePriceTrail;
                case (3, "L2"): return Sess3_L2_EnablePriceTrail;
                case (3, "S1"): return Sess3_S1_EnablePriceTrail;
                case (3, "S2"): return Sess3_S2_EnablePriceTrail;
                case (4, "L1"): return Sess4_L1_EnablePriceTrail;
                case (4, "L2"): return Sess4_L2_EnablePriceTrail;
                case (4, "S1"): return Sess4_S1_EnablePriceTrail;
                case (4, "S2"): return Sess4_S2_EnablePriceTrail;
                default: return false;
            }
        }

        private HugoTesting_TrailDistanceModeEnum B_TrailDistanceMode(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_TrailDistanceMode;
                case (1, "L2"): return Sess1_L2_TrailDistanceMode;
                case (1, "S1"): return Sess1_S1_TrailDistanceMode;
                case (1, "S2"): return Sess1_S2_TrailDistanceMode;
                case (2, "L1"): return Sess2_L1_TrailDistanceMode;
                case (2, "L2"): return Sess2_L2_TrailDistanceMode;
                case (2, "S1"): return Sess2_S1_TrailDistanceMode;
                case (2, "S2"): return Sess2_S2_TrailDistanceMode;
                case (3, "L1"): return Sess3_L1_TrailDistanceMode;
                case (3, "L2"): return Sess3_L2_TrailDistanceMode;
                case (3, "S1"): return Sess3_S1_TrailDistanceMode;
                case (3, "S2"): return Sess3_S2_TrailDistanceMode;
                case (4, "L1"): return Sess4_L1_TrailDistanceMode;
                case (4, "L2"): return Sess4_L2_TrailDistanceMode;
                case (4, "S1"): return Sess4_S1_TrailDistanceMode;
                case (4, "S2"): return Sess4_S2_TrailDistanceMode;
                default: return HugoTesting_TrailDistanceModeEnum.FixedPoints;
            }
        }

        private double B_TrailDistanceValue(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_TrailDistanceValue;
                case (1, "L2"): return Sess1_L2_TrailDistanceValue;
                case (1, "S1"): return Sess1_S1_TrailDistanceValue;
                case (1, "S2"): return Sess1_S2_TrailDistanceValue;
                case (2, "L1"): return Sess2_L1_TrailDistanceValue;
                case (2, "L2"): return Sess2_L2_TrailDistanceValue;
                case (2, "S1"): return Sess2_S1_TrailDistanceValue;
                case (2, "S2"): return Sess2_S2_TrailDistanceValue;
                case (3, "L1"): return Sess3_L1_TrailDistanceValue;
                case (3, "L2"): return Sess3_L2_TrailDistanceValue;
                case (3, "S1"): return Sess3_S1_TrailDistanceValue;
                case (3, "S2"): return Sess3_S2_TrailDistanceValue;
                case (4, "L1"): return Sess4_L1_TrailDistanceValue;
                case (4, "L2"): return Sess4_L2_TrailDistanceValue;
                case (4, "S1"): return Sess4_S1_TrailDistanceValue;
                case (4, "S2"): return Sess4_S2_TrailDistanceValue;
                default: return 20;
            }
        }

        private HugoTesting_TrailActivationModeEnum B_TrailActivationMode(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_TrailActivationMode;
                case (1, "L2"): return Sess1_L2_TrailActivationMode;
                case (1, "S1"): return Sess1_S1_TrailActivationMode;
                case (1, "S2"): return Sess1_S2_TrailActivationMode;
                case (2, "L1"): return Sess2_L1_TrailActivationMode;
                case (2, "L2"): return Sess2_L2_TrailActivationMode;
                case (2, "S1"): return Sess2_S1_TrailActivationMode;
                case (2, "S2"): return Sess2_S2_TrailActivationMode;
                case (3, "L1"): return Sess3_L1_TrailActivationMode;
                case (3, "L2"): return Sess3_L2_TrailActivationMode;
                case (3, "S1"): return Sess3_S1_TrailActivationMode;
                case (3, "S2"): return Sess3_S2_TrailActivationMode;
                case (4, "L1"): return Sess4_L1_TrailActivationMode;
                case (4, "L2"): return Sess4_L2_TrailActivationMode;
                case (4, "S1"): return Sess4_S1_TrailActivationMode;
                case (4, "S2"): return Sess4_S2_TrailActivationMode;
                default: return HugoTesting_TrailActivationModeEnum.FixedPoints;
            }
        }

        private double B_TrailActivationValue(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_TrailActivationValue;
                case (1, "L2"): return Sess1_L2_TrailActivationValue;
                case (1, "S1"): return Sess1_S1_TrailActivationValue;
                case (1, "S2"): return Sess1_S2_TrailActivationValue;
                case (2, "L1"): return Sess2_L1_TrailActivationValue;
                case (2, "L2"): return Sess2_L2_TrailActivationValue;
                case (2, "S1"): return Sess2_S1_TrailActivationValue;
                case (2, "S2"): return Sess2_S2_TrailActivationValue;
                case (3, "L1"): return Sess3_L1_TrailActivationValue;
                case (3, "L2"): return Sess3_L2_TrailActivationValue;
                case (3, "S1"): return Sess3_S1_TrailActivationValue;
                case (3, "S2"): return Sess3_S2_TrailActivationValue;
                case (4, "L1"): return Sess4_L1_TrailActivationValue;
                case (4, "L2"): return Sess4_L2_TrailActivationValue;
                case (4, "S1"): return Sess4_S1_TrailActivationValue;
                case (4, "S2"): return Sess4_S2_TrailActivationValue;
                default: return 30;
            }
        }

        private int B_TrailStepTicks(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_TrailStepTicks;
                case (1, "L2"): return Sess1_L2_TrailStepTicks;
                case (1, "S1"): return Sess1_S1_TrailStepTicks;
                case (1, "S2"): return Sess1_S2_TrailStepTicks;
                case (2, "L1"): return Sess2_L1_TrailStepTicks;
                case (2, "L2"): return Sess2_L2_TrailStepTicks;
                case (2, "S1"): return Sess2_S1_TrailStepTicks;
                case (2, "S2"): return Sess2_S2_TrailStepTicks;
                case (3, "L1"): return Sess3_L1_TrailStepTicks;
                case (3, "L2"): return Sess3_L2_TrailStepTicks;
                case (3, "S1"): return Sess3_S1_TrailStepTicks;
                case (3, "S2"): return Sess3_S2_TrailStepTicks;
                case (4, "L1"): return Sess4_L1_TrailStepTicks;
                case (4, "L2"): return Sess4_L2_TrailStepTicks;
                case (4, "S1"): return Sess4_S1_TrailStepTicks;
                case (4, "S2"): return Sess4_S2_TrailStepTicks;
                default: return 0;
            }
        }

        private bool B_EnableBodyPctFilter(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EnableBodyPctFilter;
                case (1, "L2"): return Sess1_L2_EnableBodyPctFilter;
                case (1, "S1"): return Sess1_S1_EnableBodyPctFilter;
                case (1, "S2"): return Sess1_S2_EnableBodyPctFilter;
                case (2, "L1"): return Sess2_L1_EnableBodyPctFilter;
                case (2, "L2"): return Sess2_L2_EnableBodyPctFilter;
                case (2, "S1"): return Sess2_S1_EnableBodyPctFilter;
                case (2, "S2"): return Sess2_S2_EnableBodyPctFilter;
                case (3, "L1"): return Sess3_L1_EnableBodyPctFilter;
                case (3, "L2"): return Sess3_L2_EnableBodyPctFilter;
                case (3, "S1"): return Sess3_S1_EnableBodyPctFilter;
                case (3, "S2"): return Sess3_S2_EnableBodyPctFilter;
                case (4, "L1"): return Sess4_L1_EnableBodyPctFilter;
                case (4, "L2"): return Sess4_L2_EnableBodyPctFilter;
                case (4, "S1"): return Sess4_S1_EnableBodyPctFilter;
                case (4, "S2"): return Sess4_S2_EnableBodyPctFilter;
                default: return false;
            }
        }

        private double B_MinBodyPct(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_MinBodyPct;
                case (1, "L2"): return Sess1_L2_MinBodyPct;
                case (1, "S1"): return Sess1_S1_MinBodyPct;
                case (1, "S2"): return Sess1_S2_MinBodyPct;
                case (2, "L1"): return Sess2_L1_MinBodyPct;
                case (2, "L2"): return Sess2_L2_MinBodyPct;
                case (2, "S1"): return Sess2_S1_MinBodyPct;
                case (2, "S2"): return Sess2_S2_MinBodyPct;
                case (3, "L1"): return Sess3_L1_MinBodyPct;
                case (3, "L2"): return Sess3_L2_MinBodyPct;
                case (3, "S1"): return Sess3_S1_MinBodyPct;
                case (3, "S2"): return Sess3_S2_MinBodyPct;
                case (4, "L1"): return Sess4_L1_MinBodyPct;
                case (4, "L2"): return Sess4_L2_MinBodyPct;
                case (4, "S1"): return Sess4_S1_MinBodyPct;
                case (4, "S2"): return Sess4_S2_MinBodyPct;
                default: return 33;
            }
        }

        private bool B_EnableDirectionFlip(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EnableDirectionFlip;
                case (1, "L2"): return Sess1_L2_EnableDirectionFlip;
                case (1, "S1"): return Sess1_S1_EnableDirectionFlip;
                case (1, "S2"): return Sess1_S2_EnableDirectionFlip;
                case (2, "L1"): return Sess2_L1_EnableDirectionFlip;
                case (2, "L2"): return Sess2_L2_EnableDirectionFlip;
                case (2, "S1"): return Sess2_S1_EnableDirectionFlip;
                case (2, "S2"): return Sess2_S2_EnableDirectionFlip;
                case (3, "L1"): return Sess3_L1_EnableDirectionFlip;
                case (3, "L2"): return Sess3_L2_EnableDirectionFlip;
                case (3, "S1"): return Sess3_S1_EnableDirectionFlip;
                case (3, "S2"): return Sess3_S2_EnableDirectionFlip;
                case (4, "L1"): return Sess4_L1_EnableDirectionFlip;
                case (4, "L2"): return Sess4_L2_EnableDirectionFlip;
                case (4, "S1"): return Sess4_S1_EnableDirectionFlip;
                case (4, "S2"): return Sess4_S2_EnableDirectionFlip;
                default: return false;
            }
        }

        private bool B_EnableMaxDailyLoss(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EnableMaxDailyLoss;
                case (1, "L2"): return Sess1_L2_EnableMaxDailyLoss;
                case (1, "S1"): return Sess1_S1_EnableMaxDailyLoss;
                case (1, "S2"): return Sess1_S2_EnableMaxDailyLoss;
                case (2, "L1"): return Sess2_L1_EnableMaxDailyLoss;
                case (2, "L2"): return Sess2_L2_EnableMaxDailyLoss;
                case (2, "S1"): return Sess2_S1_EnableMaxDailyLoss;
                case (2, "S2"): return Sess2_S2_EnableMaxDailyLoss;
                case (3, "L1"): return Sess3_L1_EnableMaxDailyLoss;
                case (3, "L2"): return Sess3_L2_EnableMaxDailyLoss;
                case (3, "S1"): return Sess3_S1_EnableMaxDailyLoss;
                case (3, "S2"): return Sess3_S2_EnableMaxDailyLoss;
                case (4, "L1"): return Sess4_L1_EnableMaxDailyLoss;
                case (4, "L2"): return Sess4_L2_EnableMaxDailyLoss;
                case (4, "S1"): return Sess4_S1_EnableMaxDailyLoss;
                case (4, "S2"): return Sess4_S2_EnableMaxDailyLoss;
                default: return false;
            }
        }

        private double B_MaxDailyLoss(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_MaxDailyLoss;
                case (1, "L2"): return Sess1_L2_MaxDailyLoss;
                case (1, "S1"): return Sess1_S1_MaxDailyLoss;
                case (1, "S2"): return Sess1_S2_MaxDailyLoss;
                case (2, "L1"): return Sess2_L1_MaxDailyLoss;
                case (2, "L2"): return Sess2_L2_MaxDailyLoss;
                case (2, "S1"): return Sess2_S1_MaxDailyLoss;
                case (2, "S2"): return Sess2_S2_MaxDailyLoss;
                case (3, "L1"): return Sess3_L1_MaxDailyLoss;
                case (3, "L2"): return Sess3_L2_MaxDailyLoss;
                case (3, "S1"): return Sess3_S1_MaxDailyLoss;
                case (3, "S2"): return Sess3_S2_MaxDailyLoss;
                case (4, "L1"): return Sess4_L1_MaxDailyLoss;
                case (4, "L2"): return Sess4_L2_MaxDailyLoss;
                case (4, "S1"): return Sess4_S1_MaxDailyLoss;
                case (4, "S2"): return Sess4_S2_MaxDailyLoss;
                default: return 307;
            }
        }

        private bool B_EnableMaxDailyProfit(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EnableMaxDailyProfit;
                case (1, "L2"): return Sess1_L2_EnableMaxDailyProfit;
                case (1, "S1"): return Sess1_S1_EnableMaxDailyProfit;
                case (1, "S2"): return Sess1_S2_EnableMaxDailyProfit;
                case (2, "L1"): return Sess2_L1_EnableMaxDailyProfit;
                case (2, "L2"): return Sess2_L2_EnableMaxDailyProfit;
                case (2, "S1"): return Sess2_S1_EnableMaxDailyProfit;
                case (2, "S2"): return Sess2_S2_EnableMaxDailyProfit;
                case (3, "L1"): return Sess3_L1_EnableMaxDailyProfit;
                case (3, "L2"): return Sess3_L2_EnableMaxDailyProfit;
                case (3, "S1"): return Sess3_S1_EnableMaxDailyProfit;
                case (3, "S2"): return Sess3_S2_EnableMaxDailyProfit;
                case (4, "L1"): return Sess4_L1_EnableMaxDailyProfit;
                case (4, "L2"): return Sess4_L2_EnableMaxDailyProfit;
                case (4, "S1"): return Sess4_S1_EnableMaxDailyProfit;
                case (4, "S2"): return Sess4_S2_EnableMaxDailyProfit;
                default: return false;
            }
        }

        private double B_MaxDailyProfit(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_MaxDailyProfit;
                case (1, "L2"): return Sess1_L2_MaxDailyProfit;
                case (1, "S1"): return Sess1_S1_MaxDailyProfit;
                case (1, "S2"): return Sess1_S2_MaxDailyProfit;
                case (2, "L1"): return Sess2_L1_MaxDailyProfit;
                case (2, "L2"): return Sess2_L2_MaxDailyProfit;
                case (2, "S1"): return Sess2_S1_MaxDailyProfit;
                case (2, "S2"): return Sess2_S2_MaxDailyProfit;
                case (3, "L1"): return Sess3_L1_MaxDailyProfit;
                case (3, "L2"): return Sess3_L2_MaxDailyProfit;
                case (3, "S1"): return Sess3_S1_MaxDailyProfit;
                case (3, "S2"): return Sess3_S2_MaxDailyProfit;
                case (4, "L1"): return Sess4_L1_MaxDailyProfit;
                case (4, "L2"): return Sess4_L2_MaxDailyProfit;
                case (4, "S1"): return Sess4_S1_MaxDailyProfit;
                case (4, "S2"): return Sess4_S2_MaxDailyProfit;
                default: return 186;
            }
        }

        private bool B_EnableMaxTradesPerDay(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EnableMaxTradesPerDay;
                case (1, "L2"): return Sess1_L2_EnableMaxTradesPerDay;
                case (1, "S1"): return Sess1_S1_EnableMaxTradesPerDay;
                case (1, "S2"): return Sess1_S2_EnableMaxTradesPerDay;
                case (2, "L1"): return Sess2_L1_EnableMaxTradesPerDay;
                case (2, "L2"): return Sess2_L2_EnableMaxTradesPerDay;
                case (2, "S1"): return Sess2_S1_EnableMaxTradesPerDay;
                case (2, "S2"): return Sess2_S2_EnableMaxTradesPerDay;
                case (3, "L1"): return Sess3_L1_EnableMaxTradesPerDay;
                case (3, "L2"): return Sess3_L2_EnableMaxTradesPerDay;
                case (3, "S1"): return Sess3_S1_EnableMaxTradesPerDay;
                case (3, "S2"): return Sess3_S2_EnableMaxTradesPerDay;
                case (4, "L1"): return Sess4_L1_EnableMaxTradesPerDay;
                case (4, "L2"): return Sess4_L2_EnableMaxTradesPerDay;
                case (4, "S1"): return Sess4_S1_EnableMaxTradesPerDay;
                case (4, "S2"): return Sess4_S2_EnableMaxTradesPerDay;
                default: return false;
            }
        }

        private int B_MaxTradesPerDay(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_MaxTradesPerDay;
                case (1, "L2"): return Sess1_L2_MaxTradesPerDay;
                case (1, "S1"): return Sess1_S1_MaxTradesPerDay;
                case (1, "S2"): return Sess1_S2_MaxTradesPerDay;
                case (2, "L1"): return Sess2_L1_MaxTradesPerDay;
                case (2, "L2"): return Sess2_L2_MaxTradesPerDay;
                case (2, "S1"): return Sess2_S1_MaxTradesPerDay;
                case (2, "S2"): return Sess2_S2_MaxTradesPerDay;
                case (3, "L1"): return Sess3_L1_MaxTradesPerDay;
                case (3, "L2"): return Sess3_L2_MaxTradesPerDay;
                case (3, "S1"): return Sess3_S1_MaxTradesPerDay;
                case (3, "S2"): return Sess3_S2_MaxTradesPerDay;
                case (4, "L1"): return Sess4_L1_MaxTradesPerDay;
                case (4, "L2"): return Sess4_L2_MaxTradesPerDay;
                case (4, "S1"): return Sess4_S1_MaxTradesPerDay;
                case (4, "S2"): return Sess4_S2_MaxTradesPerDay;
                default: return 6;
            }
        }

        private bool B_EnableWMAFilter(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EnableWMAFilter;
                case (1, "L2"): return Sess1_L2_EnableWMAFilter;
                case (1, "S1"): return Sess1_S1_EnableWMAFilter;
                case (1, "S2"): return Sess1_S2_EnableWMAFilter;
                case (2, "L1"): return Sess2_L1_EnableWMAFilter;
                case (2, "L2"): return Sess2_L2_EnableWMAFilter;
                case (2, "S1"): return Sess2_S1_EnableWMAFilter;
                case (2, "S2"): return Sess2_S2_EnableWMAFilter;
                case (3, "L1"): return Sess3_L1_EnableWMAFilter;
                case (3, "L2"): return Sess3_L2_EnableWMAFilter;
                case (3, "S1"): return Sess3_S1_EnableWMAFilter;
                case (3, "S2"): return Sess3_S2_EnableWMAFilter;
                case (4, "L1"): return Sess4_L1_EnableWMAFilter;
                case (4, "L2"): return Sess4_L2_EnableWMAFilter;
                case (4, "S1"): return Sess4_S1_EnableWMAFilter;
                case (4, "S2"): return Sess4_S2_EnableWMAFilter;
                default: return false;
            }
        }

        private int B_WMALength(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_WMALength;
                case (1, "L2"): return Sess1_L2_WMALength;
                case (1, "S1"): return Sess1_S1_WMALength;
                case (1, "S2"): return Sess1_S2_WMALength;
                case (2, "L1"): return Sess2_L1_WMALength;
                case (2, "L2"): return Sess2_L2_WMALength;
                case (2, "S1"): return Sess2_S1_WMALength;
                case (2, "S2"): return Sess2_S2_WMALength;
                case (3, "L1"): return Sess3_L1_WMALength;
                case (3, "L2"): return Sess3_L2_WMALength;
                case (3, "S1"): return Sess3_S1_WMALength;
                case (3, "S2"): return Sess3_S2_WMALength;
                case (4, "L1"): return Sess4_L1_WMALength;
                case (4, "L2"): return Sess4_L2_WMALength;
                case (4, "S1"): return Sess4_S1_WMALength;
                case (4, "S2"): return Sess4_S2_WMALength;
                default: return 20;
            }
        }

        private double B_MinCandleRange(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_MinCandleRange;
                case (1, "L2"): return Sess1_L2_MinCandleRange;
                case (1, "S1"): return Sess1_S1_MinCandleRange;
                case (1, "S2"): return Sess1_S2_MinCandleRange;
                case (2, "L1"): return Sess2_L1_MinCandleRange;
                case (2, "L2"): return Sess2_L2_MinCandleRange;
                case (2, "S1"): return Sess2_S1_MinCandleRange;
                case (2, "S2"): return Sess2_S2_MinCandleRange;
                case (3, "L1"): return Sess3_L1_MinCandleRange;
                case (3, "L2"): return Sess3_L2_MinCandleRange;
                case (3, "S1"): return Sess3_S1_MinCandleRange;
                case (3, "S2"): return Sess3_S2_MinCandleRange;
                case (4, "L1"): return Sess4_L1_MinCandleRange;
                case (4, "L2"): return Sess4_L2_MinCandleRange;
                case (4, "S1"): return Sess4_S1_MinCandleRange;
                case (4, "S2"): return Sess4_S2_MinCandleRange;
                default: return 0;
            }
        }

        private double B_MaxCandleRange(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_MaxCandleRange;
                case (1, "L2"): return Sess1_L2_MaxCandleRange;
                case (1, "S1"): return Sess1_S1_MaxCandleRange;
                case (1, "S2"): return Sess1_S2_MaxCandleRange;
                case (2, "L1"): return Sess2_L1_MaxCandleRange;
                case (2, "L2"): return Sess2_L2_MaxCandleRange;
                case (2, "S1"): return Sess2_S1_MaxCandleRange;
                case (2, "S2"): return Sess2_S2_MaxCandleRange;
                case (3, "L1"): return Sess3_L1_MaxCandleRange;
                case (3, "L2"): return Sess3_L2_MaxCandleRange;
                case (3, "S1"): return Sess3_S1_MaxCandleRange;
                case (3, "S2"): return Sess3_S2_MaxCandleRange;
                case (4, "L1"): return Sess4_L1_MaxCandleRange;
                case (4, "L2"): return Sess4_L2_MaxCandleRange;
                case (4, "S1"): return Sess4_S1_MaxCandleRange;
                case (4, "S2"): return Sess4_S2_MaxCandleRange;
                default: return 0;
            }
        }

        private int B_EmaLength(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return Sess1_L1_EmaLength;
                case (1, "L2"): return Sess1_L2_EmaLength;
                case (1, "S1"): return Sess1_S1_EmaLength;
                case (1, "S2"): return Sess1_S2_EmaLength;
                case (2, "L1"): return Sess2_L1_EmaLength;
                case (2, "L2"): return Sess2_L2_EmaLength;
                case (2, "S1"): return Sess2_S1_EmaLength;
                case (2, "S2"): return Sess2_S2_EmaLength;
                case (3, "L1"): return Sess3_L1_EmaLength;
                case (3, "L2"): return Sess3_L2_EmaLength;
                case (3, "S1"): return Sess3_S1_EmaLength;
                case (3, "S2"): return Sess3_S2_EmaLength;
                case (4, "L1"): return Sess4_L1_EmaLength;
                case (4, "L2"): return Sess4_L2_EmaLength;
                case (4, "S1"): return Sess4_S1_EmaLength;
                case (4, "S2"): return Sess4_S2_EmaLength;
                default: return 1;
            }
        }

        private double GetBucketDailyPnL(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return dailyPnL_Sess1_L1;
                case (1, "L2"): return dailyPnL_Sess1_L2;
                case (1, "S1"): return dailyPnL_Sess1_S1;
                case (1, "S2"): return dailyPnL_Sess1_S2;
                case (2, "L1"): return dailyPnL_Sess2_L1;
                case (2, "L2"): return dailyPnL_Sess2_L2;
                case (2, "S1"): return dailyPnL_Sess2_S1;
                case (2, "S2"): return dailyPnL_Sess2_S2;
                case (3, "L1"): return dailyPnL_Sess3_L1;
                case (3, "L2"): return dailyPnL_Sess3_L2;
                case (3, "S1"): return dailyPnL_Sess3_S1;
                case (3, "S2"): return dailyPnL_Sess3_S2;
                case (4, "L1"): return dailyPnL_Sess4_L1;
                case (4, "L2"): return dailyPnL_Sess4_L2;
                case (4, "S1"): return dailyPnL_Sess4_S1;
                case (4, "S2"): return dailyPnL_Sess4_S2;
                default: return default(double);
            }
        }

        private int GetBucketTradeCount(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return dailyTradeCount_Sess1_L1;
                case (1, "L2"): return dailyTradeCount_Sess1_L2;
                case (1, "S1"): return dailyTradeCount_Sess1_S1;
                case (1, "S2"): return dailyTradeCount_Sess1_S2;
                case (2, "L1"): return dailyTradeCount_Sess2_L1;
                case (2, "L2"): return dailyTradeCount_Sess2_L2;
                case (2, "S1"): return dailyTradeCount_Sess2_S1;
                case (2, "S2"): return dailyTradeCount_Sess2_S2;
                case (3, "L1"): return dailyTradeCount_Sess3_L1;
                case (3, "L2"): return dailyTradeCount_Sess3_L2;
                case (3, "S1"): return dailyTradeCount_Sess3_S1;
                case (3, "S2"): return dailyTradeCount_Sess3_S2;
                case (4, "L1"): return dailyTradeCount_Sess4_L1;
                case (4, "L2"): return dailyTradeCount_Sess4_L2;
                case (4, "S1"): return dailyTradeCount_Sess4_S1;
                case (4, "S2"): return dailyTradeCount_Sess4_S2;
                default: return default(int);
            }
        }

        private bool GetBucketLossHit(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return dailyLossHit_Sess1_L1;
                case (1, "L2"): return dailyLossHit_Sess1_L2;
                case (1, "S1"): return dailyLossHit_Sess1_S1;
                case (1, "S2"): return dailyLossHit_Sess1_S2;
                case (2, "L1"): return dailyLossHit_Sess2_L1;
                case (2, "L2"): return dailyLossHit_Sess2_L2;
                case (2, "S1"): return dailyLossHit_Sess2_S1;
                case (2, "S2"): return dailyLossHit_Sess2_S2;
                case (3, "L1"): return dailyLossHit_Sess3_L1;
                case (3, "L2"): return dailyLossHit_Sess3_L2;
                case (3, "S1"): return dailyLossHit_Sess3_S1;
                case (3, "S2"): return dailyLossHit_Sess3_S2;
                case (4, "L1"): return dailyLossHit_Sess4_L1;
                case (4, "L2"): return dailyLossHit_Sess4_L2;
                case (4, "S1"): return dailyLossHit_Sess4_S1;
                case (4, "S2"): return dailyLossHit_Sess4_S2;
                default: return default(bool);
            }
        }

        private bool GetBucketProfitHit(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): return dailyProfitHit_Sess1_L1;
                case (1, "L2"): return dailyProfitHit_Sess1_L2;
                case (1, "S1"): return dailyProfitHit_Sess1_S1;
                case (1, "S2"): return dailyProfitHit_Sess1_S2;
                case (2, "L1"): return dailyProfitHit_Sess2_L1;
                case (2, "L2"): return dailyProfitHit_Sess2_L2;
                case (2, "S1"): return dailyProfitHit_Sess2_S1;
                case (2, "S2"): return dailyProfitHit_Sess2_S2;
                case (3, "L1"): return dailyProfitHit_Sess3_L1;
                case (3, "L2"): return dailyProfitHit_Sess3_L2;
                case (3, "S1"): return dailyProfitHit_Sess3_S1;
                case (3, "S2"): return dailyProfitHit_Sess3_S2;
                case (4, "L1"): return dailyProfitHit_Sess4_L1;
                case (4, "L2"): return dailyProfitHit_Sess4_L2;
                case (4, "S1"): return dailyProfitHit_Sess4_S1;
                case (4, "S2"): return dailyProfitHit_Sess4_S2;
                default: return default(bool);
            }
        }

        private void AddBucketDailyPnL(int s, string b, double v)
        {
            switch ((s, b))
            {
                case (1, "L1"): dailyPnL_Sess1_L1 += v; break;
                case (1, "L2"): dailyPnL_Sess1_L2 += v; break;
                case (1, "S1"): dailyPnL_Sess1_S1 += v; break;
                case (1, "S2"): dailyPnL_Sess1_S2 += v; break;
                case (2, "L1"): dailyPnL_Sess2_L1 += v; break;
                case (2, "L2"): dailyPnL_Sess2_L2 += v; break;
                case (2, "S1"): dailyPnL_Sess2_S1 += v; break;
                case (2, "S2"): dailyPnL_Sess2_S2 += v; break;
                case (3, "L1"): dailyPnL_Sess3_L1 += v; break;
                case (3, "L2"): dailyPnL_Sess3_L2 += v; break;
                case (3, "S1"): dailyPnL_Sess3_S1 += v; break;
                case (3, "S2"): dailyPnL_Sess3_S2 += v; break;
                case (4, "L1"): dailyPnL_Sess4_L1 += v; break;
                case (4, "L2"): dailyPnL_Sess4_L2 += v; break;
                case (4, "S1"): dailyPnL_Sess4_S1 += v; break;
                case (4, "S2"): dailyPnL_Sess4_S2 += v; break;
            }
        }

        private void IncBucketTradeCount(int s, string b)
        {
            switch ((s, b))
            {
                case (1, "L1"): dailyTradeCount_Sess1_L1++; break;
                case (1, "L2"): dailyTradeCount_Sess1_L2++; break;
                case (1, "S1"): dailyTradeCount_Sess1_S1++; break;
                case (1, "S2"): dailyTradeCount_Sess1_S2++; break;
                case (2, "L1"): dailyTradeCount_Sess2_L1++; break;
                case (2, "L2"): dailyTradeCount_Sess2_L2++; break;
                case (2, "S1"): dailyTradeCount_Sess2_S1++; break;
                case (2, "S2"): dailyTradeCount_Sess2_S2++; break;
                case (3, "L1"): dailyTradeCount_Sess3_L1++; break;
                case (3, "L2"): dailyTradeCount_Sess3_L2++; break;
                case (3, "S1"): dailyTradeCount_Sess3_S1++; break;
                case (3, "S2"): dailyTradeCount_Sess3_S2++; break;
                case (4, "L1"): dailyTradeCount_Sess4_L1++; break;
                case (4, "L2"): dailyTradeCount_Sess4_L2++; break;
                case (4, "S1"): dailyTradeCount_Sess4_S1++; break;
                case (4, "S2"): dailyTradeCount_Sess4_S2++; break;
            }
        }

        private void SetBucketLossHit(int s, string b, bool v)
        {
            switch ((s, b))
            {
                case (1, "L1"): dailyLossHit_Sess1_L1 = v; break;
                case (1, "L2"): dailyLossHit_Sess1_L2 = v; break;
                case (1, "S1"): dailyLossHit_Sess1_S1 = v; break;
                case (1, "S2"): dailyLossHit_Sess1_S2 = v; break;
                case (2, "L1"): dailyLossHit_Sess2_L1 = v; break;
                case (2, "L2"): dailyLossHit_Sess2_L2 = v; break;
                case (2, "S1"): dailyLossHit_Sess2_S1 = v; break;
                case (2, "S2"): dailyLossHit_Sess2_S2 = v; break;
                case (3, "L1"): dailyLossHit_Sess3_L1 = v; break;
                case (3, "L2"): dailyLossHit_Sess3_L2 = v; break;
                case (3, "S1"): dailyLossHit_Sess3_S1 = v; break;
                case (3, "S2"): dailyLossHit_Sess3_S2 = v; break;
                case (4, "L1"): dailyLossHit_Sess4_L1 = v; break;
                case (4, "L2"): dailyLossHit_Sess4_L2 = v; break;
                case (4, "S1"): dailyLossHit_Sess4_S1 = v; break;
                case (4, "S2"): dailyLossHit_Sess4_S2 = v; break;
            }
        }

        private void SetBucketProfitHit(int s, string b, bool v)
        {
            switch ((s, b))
            {
                case (1, "L1"): dailyProfitHit_Sess1_L1 = v; break;
                case (1, "L2"): dailyProfitHit_Sess1_L2 = v; break;
                case (1, "S1"): dailyProfitHit_Sess1_S1 = v; break;
                case (1, "S2"): dailyProfitHit_Sess1_S2 = v; break;
                case (2, "L1"): dailyProfitHit_Sess2_L1 = v; break;
                case (2, "L2"): dailyProfitHit_Sess2_L2 = v; break;
                case (2, "S1"): dailyProfitHit_Sess2_S1 = v; break;
                case (2, "S2"): dailyProfitHit_Sess2_S2 = v; break;
                case (3, "L1"): dailyProfitHit_Sess3_L1 = v; break;
                case (3, "L2"): dailyProfitHit_Sess3_L2 = v; break;
                case (3, "S1"): dailyProfitHit_Sess3_S1 = v; break;
                case (3, "S2"): dailyProfitHit_Sess3_S2 = v; break;
                case (4, "L1"): dailyProfitHit_Sess4_L1 = v; break;
                case (4, "L2"): dailyProfitHit_Sess4_L2 = v; break;
                case (4, "S1"): dailyProfitHit_Sess4_S1 = v; break;
                case (4, "S2"): dailyProfitHit_Sess4_S2 = v; break;
            }
        }

        private bool GetSessEnable(int s)
        {
            switch (s)
            {
                case 1: return Sess1_Enable;
                case 2: return Sess2_Enable;
                case 3: return Sess3_Enable;
                case 4: return Sess4_Enable;
                default: return false;
            }
        }

        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  SESSION ROUTING
        // ════════════════════════════════════════════════════════════════════════

        #region Session Routing

        private double ToSessionMinutes(TimeSpan t)
        {
            double ssm = TimeSpan.Parse(SessionStartTime).TotalMinutes;
            double m   = t.TotalMinutes - ssm;
            if (m < 0) m += 1440.0;
            return m;
        }
        private double SM(string ts) => ToSessionMinutes(TimeSpan.Parse(ts));

        private double GetSessTradeWindowStartSM(int s) { switch(s) { case 1: return SM(Sess1_TradeWindowStart); case 2: return SM(Sess2_TradeWindowStart); case 3: return SM(Sess3_TradeWindowStart); case 4: return SM(Sess4_TradeWindowStart); default: return 0; } }
        private double GetSessLastEntrySM(int s)        { switch(s) { case 1: return SM(Sess1_LastEntryTime);    case 2: return SM(Sess2_LastEntryTime);    case 3: return SM(Sess3_LastEntryTime);    case 4: return SM(Sess4_LastEntryTime);    default: return 0; } }
        private double GetSessForceCloseSM(int s)       { switch(s) { case 1: return SM(Sess1_ForceCloseTime);  case 2: return SM(Sess2_ForceCloseTime);  case 3: return SM(Sess3_ForceCloseTime);  case 4: return SM(Sess4_ForceCloseTime);  default: return 0; } }
        private double GetSessSessionEndSM(int s)       { switch(s) { case 1: return SM(Sess1_SessionEndTime);  case 2: return SM(Sess2_SessionEndTime);  case 3: return SM(Sess3_SessionEndTime);  case 4: return SM(Sess4_SessionEndTime);  default: return 0; } }
        private bool   GetSessEnableForceClose(int s)   { switch(s) { case 1: return Sess1_EnableForceClose;    case 2: return Sess2_EnableForceClose;    case 3: return Sess3_EnableForceClose;    case 4: return Sess4_EnableForceClose;    default: return false; } }

        // Priority: 4 -> 3 -> 2 -> 1 (higher session number wins on overlap)
        private int ResolveActiveSession(double barSM)
        {
            for (int s = 4; s >= 1; s--)
            {
                if (!GetSessEnable(s)) continue;
                double startSM = GetSessTradeWindowStartSM(s);
                double endSM   = GetSessSessionEndSM(s);
                if (startSM == endSM) continue;
                bool inSession = endSM > startSM
                    ? barSM >= startSM && barSM < endSM
                    : barSM >= startSM || barSM < endSM;
                if (inSession) return s;
            }
            return 0;
        }

        private string ResolveBucket(int session, int direction, double candleRange)
        {
            string[] candidates = direction == 1 ? new[] { "L1", "L2" } : new[] { "S1", "S2" };
            foreach (string b in candidates)
            {
                if (!IsBucketEnabled(session, b)) continue;
                if (candleRange < B_MinCandleRange(session, b)) continue;
                double maxR = B_MaxCandleRange(session, b);
                if (maxR > 0 && candleRange > maxR) continue;
                if (GetBucketLossHit(session, b) || GetBucketProfitHit(session, b)) continue;
                if (B_EnableMaxTradesPerDay(session, b) && GetBucketTradeCount(session, b) >= B_MaxTradesPerDay(session, b)) continue;
                return b;
            }
            return null;
        }

        #endregion

        // ════════════════════════════════════════════════════════════════════════
        //  OnStateChange
        // ════════════════════════════════════════════════════════════════════════

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "HUGO Multi v1 - 4-Session EMA Commercial Strategy";
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

                // ── Commercial defaults ──────────────────────────────────────
                RequireEntryConfirmation = false;
                WebhookUrl               = string.Empty;
                WebhookTickerOverride    = string.Empty;
                MaxAccountBalance        = 0.0;
                UseNewsSkip              = false;
                NewsTime                 = string.Empty;
                NewsBlockMinutes         = 1;
                FlattenAtNewsStart       = false;

                SessionStartTime            = "19:15";
                ContractQuantity            = 1;
                EnableSkipTime              = false;
                SkipTimeStart               = "08:15";
                SkipTimeEnd                 = "08:45";
                FlattenAtSkipStart          = false;
                EnableGlobalMaxDailyLoss    = false;
                GlobalMaxDailyLoss          = 230;
                EnableGlobalMaxDailyProfit  = true;
                GlobalMaxDailyProfit        = 330;
                EnableGlobalMaxTradesPerDay = true;
                GlobalMaxTradesPerDay       = 5;

                Sess1_Enable           = true;
                Sess1_TradeWindowStart  = "19:15";
                Sess1_LastEntryTime     = "00:00";
                Sess1_EnableForceClose  = true;
                Sess1_ForceCloseTime    = "03:40";
                Sess1_SessionEndTime    = "17:00";

                Sess2_Enable           = true;
                Sess2_TradeWindowStart  = "00:00";
                Sess2_LastEntryTime     = "03:50";
                Sess2_EnableForceClose  = true;
                Sess2_ForceCloseTime    = "04:00";
                Sess2_SessionEndTime    = "17:00";

                Sess3_Enable           = true;
                Sess3_TradeWindowStart  = "04:00";
                Sess3_LastEntryTime     = "08:15";
                Sess3_EnableForceClose  = true;
                Sess3_ForceCloseTime    = "15:15";
                Sess3_SessionEndTime    = "17:00";

                Sess4_Enable           = true;
                Sess4_TradeWindowStart  = "08:45";
                Sess4_LastEntryTime     = "14:45";
                Sess4_EnableForceClose  = true;
                Sess4_ForceCloseTime    = "15:00";
                Sess4_SessionEndTime    = "17:00";

                // ── Sess1 L1 ──
                Sess1_L1_Enable = true;
                Sess1_L1_MinCandleRange = 0;
                Sess1_L1_MaxCandleRange = 30;
                Sess1_L1_EmaLength = 34;
                Sess1_L1_EnableEmaSlope = true;
                Sess1_L1_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess1_L1_EmaSlopeBars = 53;
                Sess1_L1_EmaSlopeMinPct = 0.131;
                Sess1_L1_EnableWMAFilter = false;
                Sess1_L1_WMALength = 55;
                Sess1_L1_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess1_L1_FixedStopLoss = 72;
                Sess1_L1_SLCandleMultiplier = 1.0;
                Sess1_L1_StopLossOffset = 22;
                Sess1_L1_MaxStopLoss = 72;
                Sess1_L1_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess1_L1_RiskRewardRatio = 3.7;
                Sess1_L1_FixedTakeProfit = 203;
                Sess1_L1_TPCandleMultiplier = 1.0;
                Sess1_L1_EnableBreakEven = false;
                Sess1_L1_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess1_L1_BreakEvenTriggerValue = 1.06;
                Sess1_L1_BreakEvenOffset = 30;
                Sess1_L1_EnablePriceTrail = false;
                Sess1_L1_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess1_L1_TrailDistanceValue = 64;
                Sess1_L1_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess1_L1_TrailActivationValue = 80;
                Sess1_L1_TrailStepTicks = 0;
                Sess1_L1_EnableBodyPctFilter = true;
                Sess1_L1_MinBodyPct = 23;
                Sess1_L1_EnableDirectionFlip = false;
                Sess1_L1_EnableMaxDailyLoss = true;
                Sess1_L1_MaxDailyLoss = 70;
                Sess1_L1_EnableMaxDailyProfit = false;
                Sess1_L1_MaxDailyProfit = 157;
                Sess1_L1_EnableMaxTradesPerDay = true;
                Sess1_L1_MaxTradesPerDay = 1;

                // ── Sess1 L2 ──
                Sess1_L2_Enable = true;
                Sess1_L2_MinCandleRange = 30.25;
                Sess1_L2_MaxCandleRange = 59;
                Sess1_L2_EmaLength = 50;
                Sess1_L2_EnableEmaSlope = true;
                Sess1_L2_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess1_L2_EmaSlopeBars = 21;
                Sess1_L2_EmaSlopeMinPct = 0.08;
                Sess1_L2_EnableWMAFilter = true;
                Sess1_L2_WMALength = 100;
                Sess1_L2_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess1_L2_FixedStopLoss = 56.75;
                Sess1_L2_SLCandleMultiplier = 1.0;
                Sess1_L2_StopLossOffset = 46;
                Sess1_L2_MaxStopLoss = 50;
                Sess1_L2_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess1_L2_RiskRewardRatio = 5.0;
                Sess1_L2_FixedTakeProfit = 100;
                Sess1_L2_TPCandleMultiplier = 1.0;
                Sess1_L2_EnableBreakEven = true;
                Sess1_L2_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess1_L2_BreakEvenTriggerValue = 1.4;
                Sess1_L2_BreakEvenOffset = 2;
                Sess1_L2_EnablePriceTrail = false;
                Sess1_L2_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess1_L2_TrailDistanceValue = 25;
                Sess1_L2_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess1_L2_TrailActivationValue = 191;
                Sess1_L2_TrailStepTicks = 0;
                Sess1_L2_EnableBodyPctFilter = true;
                Sess1_L2_MinBodyPct = 21;
                Sess1_L2_EnableDirectionFlip = true;
                Sess1_L2_EnableMaxDailyLoss = false;
                Sess1_L2_MaxDailyLoss = 41;
                Sess1_L2_EnableMaxDailyProfit = false;
                Sess1_L2_MaxDailyProfit = 100;
                Sess1_L2_EnableMaxTradesPerDay = true;
                Sess1_L2_MaxTradesPerDay = 1;

                // ── Sess1 S1 ──
                Sess1_S1_Enable = true;
                Sess1_S1_MinCandleRange = 0;
                Sess1_S1_MaxCandleRange = 30;
                Sess1_S1_EmaLength = 21;
                Sess1_S1_EnableEmaSlope = true;
                Sess1_S1_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess1_S1_EmaSlopeBars = 45;
                Sess1_S1_EmaSlopeMinPct = 0.005;
                Sess1_S1_EnableWMAFilter = true;
                Sess1_S1_WMALength = 30;
                Sess1_S1_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess1_S1_FixedStopLoss = 70;
                Sess1_S1_SLCandleMultiplier = 1.0;
                Sess1_S1_StopLossOffset = 40;
                Sess1_S1_MaxStopLoss = 50;
                Sess1_S1_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess1_S1_RiskRewardRatio = 4.0;
                Sess1_S1_FixedTakeProfit = 100;
                Sess1_S1_TPCandleMultiplier = 1.0;
                Sess1_S1_EnableBreakEven = true;
                Sess1_S1_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess1_S1_BreakEvenTriggerValue = 2.7;
                Sess1_S1_BreakEvenOffset = 9;
                Sess1_S1_EnablePriceTrail = false;
                Sess1_S1_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess1_S1_TrailDistanceValue = 20;
                Sess1_S1_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess1_S1_TrailActivationValue = 147;
                Sess1_S1_TrailStepTicks = 0;
                Sess1_S1_EnableBodyPctFilter = true;
                Sess1_S1_MinBodyPct = 11;
                Sess1_S1_EnableDirectionFlip = false;
                Sess1_S1_EnableMaxDailyLoss = false;
                Sess1_S1_MaxDailyLoss = 155;
                Sess1_S1_EnableMaxDailyProfit = false;
                Sess1_S1_MaxDailyProfit = 187;
                Sess1_S1_EnableMaxTradesPerDay = true;
                Sess1_S1_MaxTradesPerDay = 2;

                // ── Sess1 S2 ──
                Sess1_S2_Enable = true;
                Sess1_S2_MinCandleRange = 30.25;
                Sess1_S2_MaxCandleRange = 158;
                Sess1_S2_EmaLength = 34;
                Sess1_S2_EnableEmaSlope = true;
                Sess1_S2_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess1_S2_EmaSlopeBars = 46;
                Sess1_S2_EmaSlopeMinPct = 0.01;
                Sess1_S2_EnableWMAFilter = false;
                Sess1_S2_WMALength = 61;
                Sess1_S2_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess1_S2_FixedStopLoss = 49;
                Sess1_S2_SLCandleMultiplier = 1.0;
                Sess1_S2_StopLossOffset = 56;
                Sess1_S2_MaxStopLoss = 46;
                Sess1_S2_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess1_S2_RiskRewardRatio = 5.1;
                Sess1_S2_FixedTakeProfit = 100;
                Sess1_S2_TPCandleMultiplier = 1.0;
                Sess1_S2_EnableBreakEven = true;
                Sess1_S2_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess1_S2_BreakEvenTriggerValue = 2.9;
                Sess1_S2_BreakEvenOffset = 5;
                Sess1_S2_EnablePriceTrail = false;
                Sess1_S2_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess1_S2_TrailDistanceValue = 60;
                Sess1_S2_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess1_S2_TrailActivationValue = 196;
                Sess1_S2_TrailStepTicks = 0;
                Sess1_S2_EnableBodyPctFilter = true;
                Sess1_S2_MinBodyPct = 36;
                Sess1_S2_EnableDirectionFlip = false;
                Sess1_S2_EnableMaxDailyLoss = true;
                Sess1_S2_MaxDailyLoss = 105;
                Sess1_S2_EnableMaxDailyProfit = true;
                Sess1_S2_MaxDailyProfit = 131;
                Sess1_S2_EnableMaxTradesPerDay = true;
                Sess1_S2_MaxTradesPerDay = 2;

                // ── Sess2 L1 ──
                Sess2_L1_Enable = true;
                Sess2_L1_MinCandleRange = 14;
                Sess2_L1_MaxCandleRange = 30;
                Sess2_L1_EmaLength = 67;
                Sess2_L1_EnableEmaSlope = true;
                Sess2_L1_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess2_L1_EmaSlopeBars = 53;
                Sess2_L1_EmaSlopeMinPct = 0.029;
                Sess2_L1_EnableWMAFilter = true;
                Sess2_L1_WMALength = 100;
                Sess2_L1_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess2_L1_FixedStopLoss = 43;
                Sess2_L1_SLCandleMultiplier = 1.0;
                Sess2_L1_StopLossOffset = 22;
                Sess2_L1_MaxStopLoss = 33;
                Sess2_L1_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess2_L1_RiskRewardRatio = 3.74;
                Sess2_L1_FixedTakeProfit = 203;
                Sess2_L1_TPCandleMultiplier = 1.0;
                Sess2_L1_EnableBreakEven = false;
                Sess2_L1_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess2_L1_BreakEvenTriggerValue = 1.2;
                Sess2_L1_BreakEvenOffset = 11;
                Sess2_L1_EnablePriceTrail = false;
                Sess2_L1_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess2_L1_TrailDistanceValue = 55;
                Sess2_L1_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess2_L1_TrailActivationValue = 150;
                Sess2_L1_TrailStepTicks = 0;
                Sess2_L1_EnableBodyPctFilter = true;
                Sess2_L1_MinBodyPct = 40;
                Sess2_L1_EnableDirectionFlip = true;
                Sess2_L1_EnableMaxDailyLoss = false;
                Sess2_L1_MaxDailyLoss = 203;
                Sess2_L1_EnableMaxDailyProfit = false;
                Sess2_L1_MaxDailyProfit = 157;
                Sess2_L1_EnableMaxTradesPerDay = true;
                Sess2_L1_MaxTradesPerDay = 1;

                // ── Sess2 L2 ──
                Sess2_L2_Enable = true;
                Sess2_L2_MinCandleRange = 30.25;
                Sess2_L2_MaxCandleRange = 50;
                Sess2_L2_EmaLength = 50;
                Sess2_L2_EnableEmaSlope = true;
                Sess2_L2_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess2_L2_EmaSlopeBars = 27;
                Sess2_L2_EmaSlopeMinPct = 0.07;
                Sess2_L2_EnableWMAFilter = true;
                Sess2_L2_WMALength = 100;
                Sess2_L2_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess2_L2_FixedStopLoss = 44;
                Sess2_L2_SLCandleMultiplier = 1.0;
                Sess2_L2_StopLossOffset = 56;
                Sess2_L2_MaxStopLoss = 44;
                Sess2_L2_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess2_L2_RiskRewardRatio = 3.56;
                Sess2_L2_FixedTakeProfit = 100;
                Sess2_L2_TPCandleMultiplier = 1.0;
                Sess2_L2_EnableBreakEven = true;
                Sess2_L2_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess2_L2_BreakEvenTriggerValue = 0.93;
                Sess2_L2_BreakEvenOffset = 10;
                Sess2_L2_EnablePriceTrail = false;
                Sess2_L2_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess2_L2_TrailDistanceValue = 25;
                Sess2_L2_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess2_L2_TrailActivationValue = 192;
                Sess2_L2_TrailStepTicks = 0;
                Sess2_L2_EnableBodyPctFilter = true;
                Sess2_L2_MinBodyPct = 20;
                Sess2_L2_EnableDirectionFlip = true;
                Sess2_L2_EnableMaxDailyLoss = true;
                Sess2_L2_MaxDailyLoss = 207;
                Sess2_L2_EnableMaxDailyProfit = true;
                Sess2_L2_MaxDailyProfit = 186;
                Sess2_L2_EnableMaxTradesPerDay = false;
                Sess2_L2_MaxTradesPerDay = 6;

                // ── Sess2 S1 ──
                Sess2_S1_Enable = true;
                Sess2_S1_MinCandleRange = 0;
                Sess2_S1_MaxCandleRange = 50;
                Sess2_S1_EmaLength = 50;
                Sess2_S1_EnableEmaSlope = true;
                Sess2_S1_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess2_S1_EmaSlopeBars = 45;
                Sess2_S1_EmaSlopeMinPct = 0.077;
                Sess2_S1_EnableWMAFilter = true;
                Sess2_S1_WMALength = 100;
                Sess2_S1_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess2_S1_FixedStopLoss = 21.75;
                Sess2_S1_SLCandleMultiplier = 1.0;
                Sess2_S1_StopLossOffset = 56;
                Sess2_S1_MaxStopLoss = 22;
                Sess2_S1_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess2_S1_RiskRewardRatio = 3.12;
                Sess2_S1_FixedTakeProfit = 100;
                Sess2_S1_TPCandleMultiplier = 1.0;
                Sess2_S1_EnableBreakEven = true;
                Sess2_S1_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess2_S1_BreakEvenTriggerValue = 1.04;
                Sess2_S1_BreakEvenOffset = 3;
                Sess2_S1_EnablePriceTrail = false;
                Sess2_S1_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess2_S1_TrailDistanceValue = 20;
                Sess2_S1_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess2_S1_TrailActivationValue = 147;
                Sess2_S1_TrailStepTicks = 0;
                Sess2_S1_EnableBodyPctFilter = true;
                Sess2_S1_MinBodyPct = 28;
                Sess2_S1_EnableDirectionFlip = true;
                Sess2_S1_EnableMaxDailyLoss = true;
                Sess2_S1_MaxDailyLoss = 155;
                Sess2_S1_EnableMaxDailyProfit = true;
                Sess2_S1_MaxDailyProfit = 187;
                Sess2_S1_EnableMaxTradesPerDay = false;
                Sess2_S1_MaxTradesPerDay = 1;

                // ── Sess2 S2 ──
                Sess2_S2_Enable = true;
                Sess2_S2_MinCandleRange = 50;
                Sess2_S2_MaxCandleRange = 34.25;
                Sess2_S2_EmaLength = 46;
                Sess2_S2_EnableEmaSlope = true;
                Sess2_S2_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess2_S2_EmaSlopeBars = 46;
                Sess2_S2_EmaSlopeMinPct = 0.042;
                Sess2_S2_EnableWMAFilter = true;
                Sess2_S2_WMALength = 34;
                Sess2_S2_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess2_S2_FixedStopLoss = 60;
                Sess2_S2_SLCandleMultiplier = 1.0;
                Sess2_S2_StopLossOffset = 56;
                Sess2_S2_MaxStopLoss = 47;
                Sess2_S2_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess2_S2_RiskRewardRatio = 3.4;
                Sess2_S2_FixedTakeProfit = 100;
                Sess2_S2_TPCandleMultiplier = 1.0;
                Sess2_S2_EnableBreakEven = true;
                Sess2_S2_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess2_S2_BreakEvenTriggerValue = 2.43;
                Sess2_S2_BreakEvenOffset = 2;
                Sess2_S2_EnablePriceTrail = false;
                Sess2_S2_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess2_S2_TrailDistanceValue = 60;
                Sess2_S2_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess2_S2_TrailActivationValue = 196;
                Sess2_S2_TrailStepTicks = 0;
                Sess2_S2_EnableBodyPctFilter = true;
                Sess2_S2_MinBodyPct = 23;
                Sess2_S2_EnableDirectionFlip = true;
                Sess2_S2_EnableMaxDailyLoss = true;
                Sess2_S2_MaxDailyLoss = 105;
                Sess2_S2_EnableMaxDailyProfit = true;
                Sess2_S2_MaxDailyProfit = 131;
                Sess2_S2_EnableMaxTradesPerDay = true;
                Sess2_S2_MaxTradesPerDay = 1;

                // ── Sess3 L1 ──
                Sess3_L1_Enable = true;
                Sess3_L1_MinCandleRange = 0;
                Sess3_L1_MaxCandleRange = 50;
                Sess3_L1_EmaLength = 50;
                Sess3_L1_EnableEmaSlope = true;
                Sess3_L1_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess3_L1_EmaSlopeBars = 53;
                Sess3_L1_EmaSlopeMinPct = 0.071;
                Sess3_L1_EnableWMAFilter = true;
                Sess3_L1_WMALength = 20;
                Sess3_L1_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess3_L1_FixedStopLoss = 52.25;
                Sess3_L1_SLCandleMultiplier = 1.0;
                Sess3_L1_StopLossOffset = 22;
                Sess3_L1_MaxStopLoss = 45;
                Sess3_L1_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess3_L1_RiskRewardRatio = 6;
                Sess3_L1_FixedTakeProfit = 203;
                Sess3_L1_TPCandleMultiplier = 1.0;
                Sess3_L1_EnableBreakEven = true;
                Sess3_L1_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess3_L1_BreakEvenTriggerValue = 3.95;
                Sess3_L1_BreakEvenOffset = 20;
                Sess3_L1_EnablePriceTrail = false;
                Sess3_L1_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess3_L1_TrailDistanceValue = 55;
                Sess3_L1_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess3_L1_TrailActivationValue = 150;
                Sess3_L1_TrailStepTicks = 0;
                Sess3_L1_EnableBodyPctFilter = true;
                Sess3_L1_MinBodyPct = 31;
                Sess3_L1_EnableDirectionFlip = false;
                Sess3_L1_EnableMaxDailyLoss = false;
                Sess3_L1_MaxDailyLoss = 203;
                Sess3_L1_EnableMaxDailyProfit = false;
                Sess3_L1_MaxDailyProfit = 150;
                Sess3_L1_EnableMaxTradesPerDay = true;
                Sess3_L1_MaxTradesPerDay = 2;

                // ── Sess3 L2 ──
                Sess3_L2_Enable = true;
                Sess3_L2_MinCandleRange = 50.25;
                Sess3_L2_MaxCandleRange = 0;
                Sess3_L2_EmaLength = 50;
                Sess3_L2_EnableEmaSlope = true;
                Sess3_L2_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess3_L2_EmaSlopeBars = 27;
                Sess3_L2_EmaSlopeMinPct = 0.05;
                Sess3_L2_EnableWMAFilter = true;
                Sess3_L2_WMALength = 50;
                Sess3_L2_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess3_L2_FixedStopLoss = 48;
                Sess3_L2_SLCandleMultiplier = 1.0;
                Sess3_L2_StopLossOffset = 56;
                Sess3_L2_MaxStopLoss = 47.5;
                Sess3_L2_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess3_L2_RiskRewardRatio = 2.21;
                Sess3_L2_FixedTakeProfit = 100;
                Sess3_L2_TPCandleMultiplier = 1.0;
                Sess3_L2_EnableBreakEven = true;
                Sess3_L2_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess3_L2_BreakEvenTriggerValue = 1.45;
                Sess3_L2_BreakEvenOffset = 23;
                Sess3_L2_EnablePriceTrail = false;
                Sess3_L2_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess3_L2_TrailDistanceValue = 25;
                Sess3_L2_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess3_L2_TrailActivationValue = 192;
                Sess3_L2_TrailStepTicks = 0;
                Sess3_L2_EnableBodyPctFilter = true;
                Sess3_L2_MinBodyPct = 20;
                Sess3_L2_EnableDirectionFlip = true;
                Sess3_L2_EnableMaxDailyLoss = false;
                Sess3_L2_MaxDailyLoss = 207;
                Sess3_L2_EnableMaxDailyProfit = false;
                Sess3_L2_MaxDailyProfit = 186;
                Sess3_L2_EnableMaxTradesPerDay = true;
                Sess3_L2_MaxTradesPerDay = 1;

                // ── Sess3 S1 ──
                Sess3_S1_Enable = true;
                Sess3_S1_MinCandleRange = 0;
                Sess3_S1_MaxCandleRange = 50;
                Sess3_S1_EmaLength = 50;
                Sess3_S1_EnableEmaSlope = true;
                Sess3_S1_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess3_S1_EmaSlopeBars = 45;
                Sess3_S1_EmaSlopeMinPct = 0.062;
                Sess3_S1_EnableWMAFilter = true;
                Sess3_S1_WMALength = 56;
                Sess3_S1_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess3_S1_FixedStopLoss = 61.5;
                Sess3_S1_SLCandleMultiplier = 1.0;
                Sess3_S1_StopLossOffset = 56;
                Sess3_S1_MaxStopLoss = 53;
                Sess3_S1_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess3_S1_RiskRewardRatio = 3.42;
                Sess3_S1_FixedTakeProfit = 100;
                Sess3_S1_TPCandleMultiplier = 1.0;
                Sess3_S1_EnableBreakEven = true;
                Sess3_S1_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess3_S1_BreakEvenTriggerValue = 1.05;
                Sess3_S1_BreakEvenOffset = 3;
                Sess3_S1_EnablePriceTrail = false;
                Sess3_S1_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess3_S1_TrailDistanceValue = 20;
                Sess3_S1_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess3_S1_TrailActivationValue = 147;
                Sess3_S1_TrailStepTicks = 0;
                Sess3_S1_EnableBodyPctFilter = true;
                Sess3_S1_MinBodyPct = 28;
                Sess3_S1_EnableDirectionFlip = true;
                Sess3_S1_EnableMaxDailyLoss = false;
                Sess3_S1_MaxDailyLoss = 155;
                Sess3_S1_EnableMaxDailyProfit = true;
                Sess3_S1_MaxDailyProfit = 187;
                Sess3_S1_EnableMaxTradesPerDay = true;
                Sess3_S1_MaxTradesPerDay = 1;

                // ── Sess3 S2 ──
                Sess3_S2_Enable = true;
                Sess3_S2_MinCandleRange = 50.25;
                Sess3_S2_MaxCandleRange = 193;
                Sess3_S2_EmaLength = 46;
                Sess3_S2_EnableEmaSlope = true;
                Sess3_S2_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess3_S2_EmaSlopeBars = 46;
                Sess3_S2_EmaSlopeMinPct = 0.079;
                Sess3_S2_EnableWMAFilter = false;
                Sess3_S2_WMALength = 25;
                Sess3_S2_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess3_S2_FixedStopLoss = 47;
                Sess3_S2_SLCandleMultiplier = 1.0;
                Sess3_S2_StopLossOffset = 56;
                Sess3_S2_MaxStopLoss = 47;
                Sess3_S2_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess3_S2_RiskRewardRatio = 4.24;
                Sess3_S2_FixedTakeProfit = 100;
                Sess3_S2_TPCandleMultiplier = 1.0;
                Sess3_S2_EnableBreakEven = true;
                Sess3_S2_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess3_S2_BreakEvenTriggerValue = 2.92;
                Sess3_S2_BreakEvenOffset = 5;
                Sess3_S2_EnablePriceTrail = false;
                Sess3_S2_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess3_S2_TrailDistanceValue = 60;
                Sess3_S2_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess3_S2_TrailActivationValue = 196;
                Sess3_S2_TrailStepTicks = 0;
                Sess3_S2_EnableBodyPctFilter = true;
                Sess3_S2_MinBodyPct = 44;
                Sess3_S2_EnableDirectionFlip = false;
                Sess3_S2_EnableMaxDailyLoss = false;
                Sess3_S2_MaxDailyLoss = 105;
                Sess3_S2_EnableMaxDailyProfit = true;
                Sess3_S2_MaxDailyProfit = 47;
                Sess3_S2_EnableMaxTradesPerDay = true;
                Sess3_S2_MaxTradesPerDay = 2;

                // ── Sess4 L1 ──
                Sess4_L1_Enable = true;
                Sess4_L1_MinCandleRange = 0;
                Sess4_L1_MaxCandleRange = 50;
                Sess4_L1_EmaLength = 50;
                Sess4_L1_EnableEmaSlope = true;
                Sess4_L1_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess4_L1_EmaSlopeBars = 53;
                Sess4_L1_EmaSlopeMinPct = 0.105;
                Sess4_L1_EnableWMAFilter = true;
                Sess4_L1_WMALength = 65;
                Sess4_L1_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess4_L1_FixedStopLoss = 49;
                Sess4_L1_SLCandleMultiplier = 1.0;
                Sess4_L1_StopLossOffset = 22;
                Sess4_L1_MaxStopLoss = 52;
                Sess4_L1_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess4_L1_RiskRewardRatio = 3.42;
                Sess4_L1_FixedTakeProfit = 203;
                Sess4_L1_TPCandleMultiplier = 1.0;
                Sess4_L1_EnableBreakEven = true;
                Sess4_L1_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess4_L1_BreakEvenTriggerValue = 2.47;
                Sess4_L1_BreakEvenOffset = 4;
                Sess4_L1_EnablePriceTrail = false;
                Sess4_L1_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess4_L1_TrailDistanceValue = 55;
                Sess4_L1_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess4_L1_TrailActivationValue = 150;
                Sess4_L1_TrailStepTicks = 0;
                Sess4_L1_EnableBodyPctFilter = true;
                Sess4_L1_MinBodyPct = 20;
                Sess4_L1_EnableDirectionFlip = true;
                Sess4_L1_EnableMaxDailyLoss = true;
                Sess4_L1_MaxDailyLoss = 203;
                Sess4_L1_EnableMaxDailyProfit = false;
                Sess4_L1_MaxDailyProfit = 150;
                Sess4_L1_EnableMaxTradesPerDay = true;
                Sess4_L1_MaxTradesPerDay = 1;

                // ── Sess4 L2 ──
                Sess4_L2_Enable = true;
                Sess4_L2_MinCandleRange = 50.25;
                Sess4_L2_MaxCandleRange = 0;
                Sess4_L2_EmaLength = 50;
                Sess4_L2_EnableEmaSlope = true;
                Sess4_L2_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess4_L2_EmaSlopeBars = 27;
                Sess4_L2_EmaSlopeMinPct = 0.15;
                Sess4_L2_EnableWMAFilter = true;
                Sess4_L2_WMALength = 100;
                Sess4_L2_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess4_L2_FixedStopLoss = 49.5;
                Sess4_L2_SLCandleMultiplier = 1.0;
                Sess4_L2_StopLossOffset = 56;
                Sess4_L2_MaxStopLoss = 49;
                Sess4_L2_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess4_L2_RiskRewardRatio = 2.7;
                Sess4_L2_FixedTakeProfit = 100;
                Sess4_L2_TPCandleMultiplier = 1.0;
                Sess4_L2_EnableBreakEven = true;
                Sess4_L2_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess4_L2_BreakEvenTriggerValue = 1.41;
                Sess4_L2_BreakEvenOffset = 23;
                Sess4_L2_EnablePriceTrail = false;
                Sess4_L2_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess4_L2_TrailDistanceValue = 25;
                Sess4_L2_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess4_L2_TrailActivationValue = 192;
                Sess4_L2_TrailStepTicks = 0;
                Sess4_L2_EnableBodyPctFilter = true;
                Sess4_L2_MinBodyPct = 40;
                Sess4_L2_EnableDirectionFlip = true;
                Sess4_L2_EnableMaxDailyLoss = false;
                Sess4_L2_MaxDailyLoss = 207;
                Sess4_L2_EnableMaxDailyProfit = false;
                Sess4_L2_MaxDailyProfit = 186;
                Sess4_L2_EnableMaxTradesPerDay = true;
                Sess4_L2_MaxTradesPerDay = 1;

                // ── Sess4 S1 ──
                Sess4_S1_Enable = true;
                Sess4_S1_MinCandleRange = 0;
                Sess4_S1_MaxCandleRange = 50;
                Sess4_S1_EmaLength = 50;
                Sess4_S1_EnableEmaSlope = true;
                Sess4_S1_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess4_S1_EmaSlopeBars = 45;
                Sess4_S1_EmaSlopeMinPct = 0.001;
                Sess4_S1_EnableWMAFilter = true;
                Sess4_S1_WMALength = 67;
                Sess4_S1_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess4_S1_FixedStopLoss = 61.25;
                Sess4_S1_SLCandleMultiplier = 1.0;
                Sess4_S1_StopLossOffset = 56;
                Sess4_S1_MaxStopLoss = 53;
                Sess4_S1_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess4_S1_RiskRewardRatio = 4.39;
                Sess4_S1_FixedTakeProfit = 100;
                Sess4_S1_TPCandleMultiplier = 1.0;
                Sess4_S1_EnableBreakEven = true;
                Sess4_S1_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess4_S1_BreakEvenTriggerValue = 1.2;
                Sess4_S1_BreakEvenOffset = 2;
                Sess4_S1_EnablePriceTrail = false;
                Sess4_S1_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess4_S1_TrailDistanceValue = 20;
                Sess4_S1_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess4_S1_TrailActivationValue = 147;
                Sess4_S1_TrailStepTicks = 0;
                Sess4_S1_EnableBodyPctFilter = true;
                Sess4_S1_MinBodyPct = 26;
                Sess4_S1_EnableDirectionFlip = true;
                Sess4_S1_EnableMaxDailyLoss = true;
                Sess4_S1_MaxDailyLoss = 155;
                Sess4_S1_EnableMaxDailyProfit = true;
                Sess4_S1_MaxDailyProfit = 187;
                Sess4_S1_EnableMaxTradesPerDay = true;
                Sess4_S1_MaxTradesPerDay = 1;

                // ── Sess4 S2 ──
                Sess4_S2_Enable = true;
                Sess4_S2_MinCandleRange = 50.25;
                Sess4_S2_MaxCandleRange = 193;
                Sess4_S2_EmaLength = 46;
                Sess4_S2_EnableEmaSlope = true;
                Sess4_S2_EmaSlopeMode = HugoTesting_EmaSlopeModeEnum.MagnitudeOnly;
                Sess4_S2_EmaSlopeBars = 46;
                Sess4_S2_EmaSlopeMinPct = 0.115;
                Sess4_S2_EnableWMAFilter = true;
                Sess4_S2_WMALength = 35;
                Sess4_S2_StopLossType = HugoTesting_StopLossTypeEnum.Fixed;
                Sess4_S2_FixedStopLoss = 40.75;
                Sess4_S2_SLCandleMultiplier = 1.0;
                Sess4_S2_StopLossOffset = 56;
                Sess4_S2_MaxStopLoss = 41;
                Sess4_S2_TakeProfitType = HugoTesting_TakeProfitTypeEnum.RiskReward;
                Sess4_S2_RiskRewardRatio = 4.24;
                Sess4_S2_FixedTakeProfit = 100;
                Sess4_S2_TPCandleMultiplier = 1.0;
                Sess4_S2_EnableBreakEven = true;
                Sess4_S2_BreakEvenTriggerType = HugoTesting_BreakEvenTriggerEnum.RiskMultiple;
                Sess4_S2_BreakEvenTriggerValue = 2.13;
                Sess4_S2_BreakEvenOffset = 5;
                Sess4_S2_EnablePriceTrail = false;
                Sess4_S2_TrailDistanceMode = HugoTesting_TrailDistanceModeEnum.FixedPoints;
                Sess4_S2_TrailDistanceValue = 60;
                Sess4_S2_TrailActivationMode = HugoTesting_TrailActivationModeEnum.FixedPoints;
                Sess4_S2_TrailActivationValue = 196;
                Sess4_S2_TrailStepTicks = 0;
                Sess4_S2_EnableBodyPctFilter = true;
                Sess4_S2_MinBodyPct = 24;
                Sess4_S2_EnableDirectionFlip = false;
                Sess4_S2_EnableMaxDailyLoss = false;
                Sess4_S2_MaxDailyLoss = 105;
                Sess4_S2_EnableMaxDailyProfit = true;
                Sess4_S2_MaxDailyProfit = 47;
                Sess4_S2_EnableMaxTradesPerDay = true;
                Sess4_S2_MaxTradesPerDay = 2;

            }
            else if (State == State.Configure)
            {
                longSignalActive          = false;
                shortSignalActive         = false;
                globalDailyPnL            = 0;
                currentSessionDate        = DateTime.MinValue;
                globalDailyLossLimitHit   = false;
                globalDailyProfitLimitHit = false;
                globalDailyTradeCount     = 0;
                pendingDirection          = 0;
                breakEvenMoved            = false;
                currentTradeDirection     = 0;
                lastTradeDirection        = new int[5];
                lastTradePnL              = new double[5];
                activeBucket              = null;
                activeSession             = 0;
                inSkipWindow              = false;
                inNewsSkipWindow          = false;
                entryOrder                = null;
                bestPriceSinceEntry       = 0;
                trailActivated            = false;
                trailLocked               = false;
                initialStopDistance       = 0;
                initialTPDistance         = 0;
                maxAccountLimitHit           = false;
                isConfiguredTimeframeValid   = true;
                isConfiguredInstrumentValid  = true;
                timeframePopupShown          = false;
                instrumentPopupShown         = false;
            dailyPnL_Sess1_L1        = 0;
            dailyTradeCount_Sess1_L1 = 0;
            dailyLossHit_Sess1_L1    = false;
            dailyProfitHit_Sess1_L1  = false;
            dailyPnL_Sess1_L2        = 0;
            dailyTradeCount_Sess1_L2 = 0;
            dailyLossHit_Sess1_L2    = false;
            dailyProfitHit_Sess1_L2  = false;
            dailyPnL_Sess1_S1        = 0;
            dailyTradeCount_Sess1_S1 = 0;
            dailyLossHit_Sess1_S1    = false;
            dailyProfitHit_Sess1_S1  = false;
            dailyPnL_Sess1_S2        = 0;
            dailyTradeCount_Sess1_S2 = 0;
            dailyLossHit_Sess1_S2    = false;
            dailyProfitHit_Sess1_S2  = false;
            dailyPnL_Sess2_L1        = 0;
            dailyTradeCount_Sess2_L1 = 0;
            dailyLossHit_Sess2_L1    = false;
            dailyProfitHit_Sess2_L1  = false;
            dailyPnL_Sess2_L2        = 0;
            dailyTradeCount_Sess2_L2 = 0;
            dailyLossHit_Sess2_L2    = false;
            dailyProfitHit_Sess2_L2  = false;
            dailyPnL_Sess2_S1        = 0;
            dailyTradeCount_Sess2_S1 = 0;
            dailyLossHit_Sess2_S1    = false;
            dailyProfitHit_Sess2_S1  = false;
            dailyPnL_Sess2_S2        = 0;
            dailyTradeCount_Sess2_S2 = 0;
            dailyLossHit_Sess2_S2    = false;
            dailyProfitHit_Sess2_S2  = false;
            dailyPnL_Sess3_L1        = 0;
            dailyTradeCount_Sess3_L1 = 0;
            dailyLossHit_Sess3_L1    = false;
            dailyProfitHit_Sess3_L1  = false;
            dailyPnL_Sess3_L2        = 0;
            dailyTradeCount_Sess3_L2 = 0;
            dailyLossHit_Sess3_L2    = false;
            dailyProfitHit_Sess3_L2  = false;
            dailyPnL_Sess3_S1        = 0;
            dailyTradeCount_Sess3_S1 = 0;
            dailyLossHit_Sess3_S1    = false;
            dailyProfitHit_Sess3_S1  = false;
            dailyPnL_Sess3_S2        = 0;
            dailyTradeCount_Sess3_S2 = 0;
            dailyLossHit_Sess3_S2    = false;
            dailyProfitHit_Sess3_S2  = false;
            dailyPnL_Sess4_L1        = 0;
            dailyTradeCount_Sess4_L1 = 0;
            dailyLossHit_Sess4_L1    = false;
            dailyProfitHit_Sess4_L1  = false;
            dailyPnL_Sess4_L2        = 0;
            dailyTradeCount_Sess4_L2 = 0;
            dailyLossHit_Sess4_L2    = false;
            dailyProfitHit_Sess4_L2  = false;
            dailyPnL_Sess4_S1        = 0;
            dailyTradeCount_Sess4_S1 = 0;
            dailyLossHit_Sess4_S1    = false;
            dailyProfitHit_Sess4_S1  = false;
            dailyPnL_Sess4_S2        = 0;
            dailyTradeCount_Sess4_S2 = 0;
            dailyLossHit_Sess4_S2    = false;
            dailyProfitHit_Sess4_S2  = false;
            }
            else if (State == State.DataLoaded)
            {
                emaSess1_L1 = EMA(Sess1_L1_EmaLength);
                wmaSess1_L1 = WMA(Sess1_L1_WMALength);
                emaSess1_L2 = EMA(Sess1_L2_EmaLength);
                wmaSess1_L2 = WMA(Sess1_L2_WMALength);
                emaSess1_S1 = EMA(Sess1_S1_EmaLength);
                wmaSess1_S1 = WMA(Sess1_S1_WMALength);
                emaSess1_S2 = EMA(Sess1_S2_EmaLength);
                wmaSess1_S2 = WMA(Sess1_S2_WMALength);
                emaSess2_L1 = EMA(Sess2_L1_EmaLength);
                wmaSess2_L1 = WMA(Sess2_L1_WMALength);
                emaSess2_L2 = EMA(Sess2_L2_EmaLength);
                wmaSess2_L2 = WMA(Sess2_L2_WMALength);
                emaSess2_S1 = EMA(Sess2_S1_EmaLength);
                wmaSess2_S1 = WMA(Sess2_S1_WMALength);
                emaSess2_S2 = EMA(Sess2_S2_EmaLength);
                wmaSess2_S2 = WMA(Sess2_S2_WMALength);
                emaSess3_L1 = EMA(Sess3_L1_EmaLength);
                wmaSess3_L1 = WMA(Sess3_L1_WMALength);
                emaSess3_L2 = EMA(Sess3_L2_EmaLength);
                wmaSess3_L2 = WMA(Sess3_L2_WMALength);
                emaSess3_S1 = EMA(Sess3_S1_EmaLength);
                wmaSess3_S1 = WMA(Sess3_S1_WMALength);
                emaSess3_S2 = EMA(Sess3_S2_EmaLength);
                wmaSess3_S2 = WMA(Sess3_S2_WMALength);
                emaSess4_L1 = EMA(Sess4_L1_EmaLength);
                wmaSess4_L1 = WMA(Sess4_L1_WMALength);
                emaSess4_L2 = EMA(Sess4_L2_EmaLength);
                wmaSess4_L2 = WMA(Sess4_L2_WMALength);
                emaSess4_S1 = EMA(Sess4_S1_EmaLength);
                wmaSess4_S1 = WMA(Sess4_S1_WMALength);
                emaSess4_S2 = EMA(Sess4_S2_EmaLength);
                wmaSess4_S2 = WMA(Sess4_S2_WMALength);
                AddChartIndicator(emaSess4_L1);

                // ── Commercial: validate TF and instrument ───────────────────
                ValidateRequiredPrimaryTimeframe(RequiredPrimaryTimeframeMinutes);
                ValidateRequiredPrimaryInstrument();
                EnsureNewsDatesInitialized();

                // ── Heartbeat reporter (TopstepX API access) ─────────────────
                if (Category != null)
                {
                    try
                    {
                        heartbeatReporter = new StrategyHeartbeatReporter(
                            HeartbeatStrategyName,
                            System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv"));
                    }
                    catch { heartbeatReporter = null; }
                }
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

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade) return;

            // ── Chart overlays (realtime only) ───────────────────────────────
            if (ChartControl != null)
            {
                DrawSessionTimeWindows();
                UpdateInfoBox();
            }

            // ── Configuration validation guard ───────────────────────────────
            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
            {
                if (entryOrder != null) { CancelOrder(entryOrder); entryOrder = null; }
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong("InvalidConfig");
                if (Position.MarketPosition == MarketPosition.Short) ExitShort("InvalidConfig");
                return;
            }

            // ── Max account balance guard ────────────────────────────────────
            if (IsAccountBalanceBlocked())
                return;

            // ── Daily reset (session-anchored, midnight-crossing safe) ────────
            TimeSpan barTOD          = Time[0].TimeOfDay;
            TimeSpan sessionStartTOD = TimeSpan.Parse(SessionStartTime);
            DateTime sessionDate     = barTOD >= sessionStartTOD ? Time[0].Date : Time[0].Date.AddDays(-1);

            if (sessionDate != currentSessionDate)
            {
                globalDailyPnL            = 0;
                currentSessionDate        = sessionDate;
                globalDailyLossLimitHit   = false;
                globalDailyProfitLimitHit = false;
                globalDailyTradeCount     = 0;
                lastTradeDirection        = new int[5];
                lastTradePnL              = new double[5];
                ResetAllBucketCounters();
            }

            // ── Resolve bar's session ────────────────────────────────────────
            double barSM      = ToSessionMinutes(barTOD);
            int    barSession = ResolveActiveSession(barSM);

            // ── Force Close: fires on the position's OWN session ─────────────
            if (Position.MarketPosition != MarketPosition.Flat && activeSession != 0
                && GetSessEnableForceClose(activeSession))
            {
                double fcSM  = GetSessForceCloseSM(activeSession);
                double endSM = GetSessSessionEndSM(activeSession);
                bool inFC    = fcSM <= endSM ? barSM >= fcSM : barSM >= fcSM || barSM < endSM;
                if (inFC)
                {
                    CancelAllOrders(); ResetSignals();
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(Math.Max(1, Position.Quantity), "ForceClose", LongEntrySignalName);
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort(Math.Max(1, Position.Quantity), "ForceClose", ShortEntrySignalName);
                    return;
                }
            }

            // ── Block new entries past force close or outside session ─────────
            bool newEntriesBlockedByFC = false;
            if (barSession != 0 && GetSessEnableForceClose(barSession))
            {
                double fcSM  = GetSessForceCloseSM(barSession);
                double endSM = GetSessSessionEndSM(barSession);
                newEntriesBlockedByFC = fcSM <= endSM ? barSM >= fcSM && barSM < endSM : barSM >= fcSM || barSM < endSM;
            }

            bool isWithinAnySession = barSession != 0;
            bool canEnterNewTrades  = false;
            if (barSession != 0)
            {
                double tws  = GetSessTradeWindowStartSM(barSession);
                double last = GetSessLastEntrySM(barSession);
                bool inWindow = last >= tws ? barSM >= tws && barSM <= last : barSM >= tws || barSM <= last;
                canEnterNewTrades = inWindow && !newEntriesBlockedByFC;
            }

            if (!canEnterNewTrades && entryOrder != null) { CancelOrder(entryOrder); ResetSignals(); }

            // ── Skip Time Window (global) ────────────────────────────────────
            if (EnableSkipTime)
            {
                double skipStartSM = SM(SkipTimeStart);
                double skipEndSM   = SM(SkipTimeEnd);
                bool nowInSkip = skipEndSM >= skipStartSM
                    ? barSM >= skipStartSM && barSM < skipEndSM
                    : barSM >= skipStartSM || barSM < skipEndSM;

                if (nowInSkip && !inSkipWindow)
                {
                    inSkipWindow = true;
                    Print(Time[0] + " - Skip window started. No new entries.");
                    if (FlattenAtSkipStart)
                    {
                        if (Position.MarketPosition == MarketPosition.Long)  ExitLong("SkipWindowFlat");
                        if (Position.MarketPosition == MarketPosition.Short) ExitShort("SkipWindowFlat");
                        CancelAllOrders(); ResetSignals();
                    }
                    else { if (entryOrder != null) { CancelOrder(entryOrder); entryOrder = null; } }
                }
                else if (!nowInSkip && inSkipWindow)
                {
                    inSkipWindow = false;
                    Print(Time[0] + " - Skip window ended.");
                }
                if (inSkipWindow && Position.MarketPosition == MarketPosition.Flat) return;
            }

            // ── News Skip Window ─────────────────────────────────────────────
            if (IsNewsWindowConfigured())
            {
                bool nowInNewsSkip = IsInNewsSkipWindow(Time[0]);
                if (nowInNewsSkip && !inNewsSkipWindow)
                {
                    inNewsSkipWindow = true;
                    Print(Time[0] + " - News skip window started. No new entries.");
                    if (FlattenAtNewsStart)
                    {
                        if (Position.MarketPosition == MarketPosition.Long)  ExitLong("NewsSkip");
                        if (Position.MarketPosition == MarketPosition.Short) ExitShort("NewsSkip");
                        if (entryOrder != null) { CancelOrder(entryOrder); entryOrder = null; }
                        ResetSignals();
                    }
                    else { if (entryOrder != null) { CancelOrder(entryOrder); entryOrder = null; } }
                }
                else if (!nowInNewsSkip && inNewsSkipWindow)
                {
                    inNewsSkipWindow = false;
                    Print(Time[0] + " - News skip window ended.");
                }
                if (inNewsSkipWindow && Position.MarketPosition == MarketPosition.Flat) return;
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
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong("GlobalMaxLoss");
                if (Position.MarketPosition == MarketPosition.Short) ExitShort("GlobalMaxLoss");
                CancelAllOrders(); ResetSignals();
                Print(Time[0] + " - GLOBAL Max LOSS hit: " + totalGlobalPnL + " pts.");
                return;
            }
            if (EnableGlobalMaxDailyProfit && totalGlobalPnL >= GlobalMaxDailyProfit)
            {
                globalDailyProfitLimitHit = true;
                if (Position.MarketPosition == MarketPosition.Long)  ExitLong("GlobalMaxProfit");
                if (Position.MarketPosition == MarketPosition.Short) ExitShort("GlobalMaxProfit");
                CancelAllOrders(); ResetSignals();
                Print(Time[0] + " - GLOBAL Max PROFIT hit: " + totalGlobalPnL + " pts.");
                return;
            }
            if (globalDailyLossLimitHit || globalDailyProfitLimitHit || !isWithinAnySession) return;
            if (EnableGlobalMaxTradesPerDay && globalDailyTradeCount >= GlobalMaxTradesPerDay
                && Position.MarketPosition == MarketPosition.Flat) return;

            // ── Per-bucket P&L guard ─────────────────────────────────────────
            if (Position.MarketPosition != MarketPosition.Flat && activeBucket != null && activeSession != 0)
            {
                double bucketPnL = GetBucketDailyPnL(activeSession, activeBucket) + unrealizedPnL;
                if (B_EnableMaxDailyLoss(activeSession, activeBucket) && bucketPnL <= -B_MaxDailyLoss(activeSession, activeBucket))
                {
                    SetBucketLossHit(activeSession, activeBucket, true);
                    if (Position.MarketPosition == MarketPosition.Long)  ExitLong("Sess" + activeSession + "_" + activeBucket + "_MaxLoss");
                    if (Position.MarketPosition == MarketPosition.Short) ExitShort("Sess" + activeSession + "_" + activeBucket + "_MaxLoss");
                    CancelAllOrders(); ResetSignals();
                    Print(Time[0] + " - Sess" + activeSession + " " + activeBucket + " Max LOSS hit: " + bucketPnL + " pts.");
                    return;
                }
                if (B_EnableMaxDailyProfit(activeSession, activeBucket) && bucketPnL >= B_MaxDailyProfit(activeSession, activeBucket))
                {
                    SetBucketProfitHit(activeSession, activeBucket, true);
                    if (Position.MarketPosition == MarketPosition.Long)  ExitLong("Sess" + activeSession + "_" + activeBucket + "_MaxProfit");
                    if (Position.MarketPosition == MarketPosition.Short) ExitShort("Sess" + activeSession + "_" + activeBucket + "_MaxProfit");
                    CancelAllOrders(); ResetSignals();
                    Print(Time[0] + " - Sess" + activeSession + " " + activeBucket + " Max PROFIT hit: " + bucketPnL + " pts.");
                    return;
                }
            }

            // ── EMA crossover detection (bar's session indicators) ────────────
            bool anyLongCross = false, anyShortCross = false;
            if (barSession != 0)
            {
                EMA bsL1 = GetEma(barSession, "L1");
                EMA bsL2 = GetEma(barSession, "L2");
                EMA bsS1 = GetEma(barSession, "S1");
                EMA bsS2 = GetEma(barSession, "S2");
                bool crossAboveL1 = IsBucketEnabled(barSession, "L1") && CurrentBar >= B_EmaLength(barSession, "L1") && Close[0] > bsL1[0] && Close[1] <= bsL1[1];
                bool crossAboveL2 = IsBucketEnabled(barSession, "L2") && CurrentBar >= B_EmaLength(barSession, "L2") && Close[0] > bsL2[0] && Close[1] <= bsL2[1];
                bool crossBelowS1 = IsBucketEnabled(barSession, "S1") && CurrentBar >= B_EmaLength(barSession, "S1") && Close[0] < bsS1[0] && Close[1] >= bsS1[1];
                bool crossBelowS2 = IsBucketEnabled(barSession, "S2") && CurrentBar >= B_EmaLength(barSession, "S2") && Close[0] < bsS2[0] && Close[1] >= bsS2[1];
                anyLongCross  = crossAboveL1 || crossAboveL2;
                anyShortCross = crossBelowS1 || crossBelowS2;
            }

            // ── Manage open position (uses position's OWN session) ────────────
            if (Position.MarketPosition != MarketPosition.Flat && activeBucket != null && activeSession != 0)
            {
                EMA activeEma = GetEma(activeSession, activeBucket);

                CheckBreakEven();
                if (B_StopLossType(activeSession, activeBucket) == HugoTesting_StopLossTypeEnum.EMATrailing)
                    UpdateEMATrailingStop(activeEma);
                ManagePriceTrailingStop();

                if (B_TakeProfitType(activeSession, activeBucket) == HugoTesting_TakeProfitTypeEnum.EMACross)
                {
                    if (Position.MarketPosition == MarketPosition.Long && Close[0] < activeEma[0] && Close[1] >= activeEma[1])
                    { ExitLong("EMACrossExit"); return; }
                    if (Position.MarketPosition == MarketPosition.Short && Close[0] > activeEma[0] && Close[1] <= activeEma[1])
                    { ExitShort("EMACrossExit"); return; }
                }

                if (Position.MarketPosition == MarketPosition.Long && Close[0] < activeEma[0] && Close[1] >= activeEma[1])
                    ExitLong("OppositeSignal");
                else if (Position.MarketPosition == MarketPosition.Short && Close[0] > activeEma[0] && Close[1] <= activeEma[1])
                    ExitShort("OppositeSignal");
            }

            // ── Cancel pending limit on opposite signal ───────────────────────
            if (entryOrder != null)
            {
                if ((pendingDirection == 1 && anyShortCross) || (pendingDirection == -1 && anyLongCross))
                { CancelOrder(entryOrder); ResetSignals(); }
            }

            if (Position.MarketPosition != MarketPosition.Flat || entryOrder != null || !canEnterNewTrades) return;
            if (EnableGlobalMaxTradesPerDay && globalDailyTradeCount >= GlobalMaxTradesPerDay) return;
            if (barSession == 0) return;
            if (EnableSkipTime && inSkipWindow) return;
            if (IsNewsWindowConfigured() && inNewsSkipWindow) return;

            // ── New signal detection ─────────────────────────────────────────
            double candleRange = High[0] - Low[0];

            if (anyLongCross)
            {
                string bucket = ResolveBucket(barSession, 1, candleRange);
                if (bucket == null) { Print(Time[0] + " - Sess" + barSession + " Long: no matching bucket."); return; }
                if (!IsDirectionAllowed(1, barSession, bucket)) return;
                if (!PassesEmaSlopeFilter(true, barSession, bucket)) return;
                if (!PassesWMAFilter(true, barSession, bucket)) return;
                if (!PassesBodyPctFilter(candleRange, barSession, bucket)) return;
                crossoverCandleHigh = High[0];
                crossoverCandleLow  = Low[0];
                longSignalActive    = true;
                shortSignalActive   = false;
                signalBar           = CurrentBar;
                pendingDirection    = 1;
                activeBucket        = bucket;
                activeSession       = barSession;
                Print(Time[0] + " - Sess" + barSession + " " + bucket + " Long signal. Range: " + candleRange.ToString("F2") + " pts.");
                ProcessLongEntry();
            }
            else if (anyShortCross)
            {
                string bucket = ResolveBucket(barSession, -1, candleRange);
                if (bucket == null) { Print(Time[0] + " - Sess" + barSession + " Short: no matching bucket."); return; }
                if (!IsDirectionAllowed(-1, barSession, bucket)) return;
                if (!PassesEmaSlopeFilter(false, barSession, bucket)) return;
                if (!PassesWMAFilter(false, barSession, bucket)) return;
                if (!PassesBodyPctFilter(candleRange, barSession, bucket)) return;
                crossoverCandleHigh = High[0];
                crossoverCandleLow  = Low[0];
                shortSignalActive   = true;
                longSignalActive    = false;
                signalBar           = CurrentBar;
                pendingDirection    = -1;
                activeBucket        = bucket;
                activeSession       = barSession;
                Print(Time[0] + " - Sess" + barSession + " " + bucket + " Short signal. Range: " + candleRange.ToString("F2") + " pts.");
                ProcessShortEntry();
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Entry Processing — Immediate only
        // ════════════════════════════════════════════════════════════════════════

        private void ProcessLongEntry()
        {
            int sess = activeSession; string b = activeBucket;
            EMA e = GetEma(sess, b);
            currentTradeDirection = 1;
            double stopLoss   = CalculateStopLoss(true, sess, b, e);
            double takeProfit = CalculateTakeProfit(true, Close[0], stopLoss, sess, b);
            if (B_MaxStopLoss(sess, b) > 0)
            {
                double slDist = Close[0] - stopLoss;
                if (slDist > B_MaxStopLoss(sess, b)) stopLoss = Close[0] - B_MaxStopLoss(sess, b);
            }

            if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", Close[0], ContractQuantity))
            {
                Print(Time[0] + " - Sess" + sess + " " + b + " Long entry confirmation declined.");
                ResetSignals(); activeBucket = null; activeSession = 0; currentTradeDirection = 0;
                return;
            }

            entryPrice          = Close[0];
            currentStopPrice    = stopLoss;
            breakEvenMoved      = false;
            initialStopDistance = entryPrice - stopLoss;
            initialTPDistance   = B_TakeProfitType(sess, b) != HugoTesting_TakeProfitTypeEnum.EMACross ? takeProfit - entryPrice : 0;
            ResetTrailState();
            SetStopLoss(CalculationMode.Price, stopLoss);
            if (B_TakeProfitType(sess, b) != HugoTesting_TakeProfitTypeEnum.EMACross)
                SetProfitTarget(CalculationMode.Price, takeProfit);
            EnterLong(ContractQuantity, LongEntrySignalName);
        }

        private void ProcessShortEntry()
        {
            int sess = activeSession; string b = activeBucket;
            EMA e = GetEma(sess, b);
            currentTradeDirection = -1;
            double stopLoss   = CalculateStopLoss(false, sess, b, e);
            double takeProfit = CalculateTakeProfit(false, Close[0], stopLoss, sess, b);
            if (B_MaxStopLoss(sess, b) > 0)
            {
                double slDist = stopLoss - Close[0];
                if (slDist > B_MaxStopLoss(sess, b)) stopLoss = Close[0] + B_MaxStopLoss(sess, b);
            }

            if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", Close[0], ContractQuantity))
            {
                Print(Time[0] + " - Sess" + sess + " " + b + " Short entry confirmation declined.");
                ResetSignals(); activeBucket = null; activeSession = 0; currentTradeDirection = 0;
                return;
            }

            entryPrice          = Close[0];
            currentStopPrice    = stopLoss;
            breakEvenMoved      = false;
            initialStopDistance = stopLoss - entryPrice;
            initialTPDistance   = B_TakeProfitType(sess, b) != HugoTesting_TakeProfitTypeEnum.EMACross ? entryPrice - takeProfit : 0;
            ResetTrailState();
            SetStopLoss(CalculationMode.Price, stopLoss);
            if (B_TakeProfitType(sess, b) != HugoTesting_TakeProfitTypeEnum.EMACross)
                SetProfitTarget(CalculationMode.Price, takeProfit);
            EnterShort(ContractQuantity, ShortEntrySignalName);
        }

        private double CalculateStopLoss(bool isLong, int sess, string b, EMA e)
        {
            switch (B_StopLossType(sess, b))
            {
                case HugoTesting_StopLossTypeEnum.Fixed:
                    return isLong ? Close[0] - B_FixedStopLoss(sess, b) : Close[0] + B_FixedStopLoss(sess, b);
                case HugoTesting_StopLossTypeEnum.CandleHighLow:
                    return isLong ? crossoverCandleLow - B_StopLossOffset(sess, b) : crossoverCandleHigh + B_StopLossOffset(sess, b);
                case HugoTesting_StopLossTypeEnum.EMAFixed:
                case HugoTesting_StopLossTypeEnum.EMATrailing:
                    return isLong ? e[0] - B_StopLossOffset(sess, b) : e[0] + B_StopLossOffset(sess, b);
                case HugoTesting_StopLossTypeEnum.CandleMultiple:
                {
                    double cr = crossoverCandleHigh - crossoverCandleLow;
                    double dist = cr * B_SLCandleMultiplier(sess, b);
                    return isLong ? Close[0] - dist : Close[0] + dist;
                }
                default:
                    return isLong ? Close[0] - B_FixedStopLoss(sess, b) : Close[0] + B_FixedStopLoss(sess, b);
            }
        }

        private double CalculateTakeProfit(bool isLong, double entry, double stopLoss, int sess, string b)
        {
            double risk = isLong ? entry - stopLoss : stopLoss - entry;
            switch (B_TakeProfitType(sess, b))
            {
                case HugoTesting_TakeProfitTypeEnum.RiskReward:
                    return isLong ? entry + risk * B_RiskRewardRatio(sess, b) : entry - risk * B_RiskRewardRatio(sess, b);
                case HugoTesting_TakeProfitTypeEnum.Fixed:
                    return isLong ? entry + B_FixedTakeProfit(sess, b) : entry - B_FixedTakeProfit(sess, b);
                case HugoTesting_TakeProfitTypeEnum.EMACross:
                    return isLong ? entry + 1000 : entry - 1000;
                case HugoTesting_TakeProfitTypeEnum.CandleMultiple:
                {
                    double cr = crossoverCandleHigh - crossoverCandleLow;
                    return isLong ? entry + cr * B_TPCandleMultiplier(sess, b) : entry - cr * B_TPCandleMultiplier(sess, b);
                }
                default:
                    return isLong ? entry + B_FixedTakeProfit(sess, b) : entry - B_FixedTakeProfit(sess, b);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Filters
        // ════════════════════════════════════════════════════════════════════════

        private bool IsDirectionAllowed(int dir, int sess, string b)
        {
            if (!B_EnableDirectionFlip(sess, b)) return true;
            if (lastTradeDirection[sess] == 0)          return true;
            if (dir != lastTradeDirection[sess])        return true;
            return false;
        }

        private bool PassesEmaSlopeFilter(bool isLong, int sess, string b)
        {
            if (!B_EnableEmaSlope(sess, b)) return true;
            int slopeBars = B_EmaSlopeBars(sess, b);
            if (CurrentBar < slopeBars) return false;
            EMA e = GetEma(sess, b);
            double emaNow = e[0], emaThen = e[slopeBars];
            if (emaThen == 0) return false;
            double slopePct = (emaNow - emaThen) / emaThen * 100.0;
            switch (B_EmaSlopeMode(sess, b))
            {
                case HugoTesting_EmaSlopeModeEnum.DirectionEnforced:
                    return isLong ? slopePct >= B_EmaSlopeMinPct(sess, b) : slopePct <= -B_EmaSlopeMinPct(sess, b);
                case HugoTesting_EmaSlopeModeEnum.MagnitudeOnly:
                    return Math.Abs(slopePct) >= B_EmaSlopeMinPct(sess, b);
                default: return true;
            }
        }

        private bool PassesWMAFilter(bool isLong, int sess, string b)
        {
            if (!B_EnableWMAFilter(sess, b)) return true;
            WMA w = GetWma(sess, b);
            return isLong ? Close[0] > w[0] : Close[0] < w[0];
        }

        private bool PassesBodyPctFilter(double candleRange, int sess, string b)
        {
            if (!B_EnableBodyPctFilter(sess, b)) return true;
            if (candleRange <= 0) return false;
            double bodySize = Math.Abs(Close[0] - Open[0]);
            return (bodySize / candleRange) * 100.0 >= B_MinBodyPct(sess, b);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Break Even
        // ════════════════════════════════════════════════════════════════════════

        private void CheckBreakEven()
        {
            if (activeBucket == null || activeSession == 0) return;
            if (!B_EnableBreakEven(activeSession, activeBucket) || breakEvenMoved || Position.MarketPosition == MarketPosition.Flat) return;
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double profit = isLong ? Close[0] - Position.AveragePrice : Position.AveragePrice - Close[0];
            double risk   = isLong ? Position.AveragePrice - currentStopPrice : currentStopPrice - Position.AveragePrice;
            bool trigger  = B_BreakEvenTriggerType(activeSession, activeBucket) == HugoTesting_BreakEvenTriggerEnum.Points
                ? profit >= B_BreakEvenTriggerValue(activeSession, activeBucket)
                : risk > 0 && (profit / risk) >= B_BreakEvenTriggerValue(activeSession, activeBucket);
            if (!trigger) return;
            double newStop = isLong
                ? Position.AveragePrice + B_BreakEvenOffset(activeSession, activeBucket)
                : Position.AveragePrice - B_BreakEvenOffset(activeSession, activeBucket);
            SetStopLoss(CalculationMode.Price, newStop);
            currentStopPrice = newStop;
            breakEvenMoved   = true;
            Print(Time[0] + " - Sess" + activeSession + " " + activeBucket + " Break Even. New stop: " + newStop);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  EMA Trailing Stop
        // ════════════════════════════════════════════════════════════════════════

        private void UpdateEMATrailingStop(EMA e)
        {
            if (breakEvenMoved || activeBucket == null || activeSession == 0) return;
            if (Position.MarketPosition == MarketPosition.Long)
            {
                double ns = e[0] - B_StopLossOffset(activeSession, activeBucket);
                if (ns > currentStopPrice) { SetStopLoss(CalculationMode.Price, ns); currentStopPrice = ns; }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                double ns = e[0] + B_StopLossOffset(activeSession, activeBucket);
                if (ns < currentStopPrice) { SetStopLoss(CalculationMode.Price, ns); currentStopPrice = ns; }
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Price Trailing Stop
        // ════════════════════════════════════════════════════════════════════════

        private void ManagePriceTrailingStop()
        {
            if (activeBucket == null || activeSession == 0) return;
            if (!B_EnablePriceTrail(activeSession, activeBucket)) return;
            if (Position.MarketPosition == MarketPosition.Flat || trailLocked) return;

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            bestPriceSinceEntry = isLong ? Math.Max(bestPriceSinceEntry, High[0]) : Math.Min(bestPriceSinceEntry, Low[0]);
            double profit = isLong ? bestPriceSinceEntry - entryPrice : entryPrice - bestPriceSinceEntry;

            if (!trailActivated)
            {
                double thresh = B_TrailActivationMode(activeSession, activeBucket) == HugoTesting_TrailActivationModeEnum.PctOfRRTarget && initialTPDistance > 0
                    ? initialTPDistance * B_TrailActivationValue(activeSession, activeBucket) / 100.0
                    : B_TrailActivationValue(activeSession, activeBucket);
                if (B_TrailActivationValue(activeSession, activeBucket) <= 0 || profit >= thresh)
                {
                    trailActivated = true;
                    Print(Time[0] + " - Sess" + activeSession + " " + activeBucket + " Trail ACTIVATED. Profit: " + profit.ToString("F2") + " pts.");
                }
                else return;
            }

            double trailDist = B_TrailDistanceMode(activeSession, activeBucket) == HugoTesting_TrailDistanceModeEnum.FixedPoints
                ? B_TrailDistanceValue(activeSession, activeBucket)
                : initialStopDistance > 0 ? initialStopDistance * B_TrailDistanceValue(activeSession, activeBucket) / 100.0 : B_TrailDistanceValue(activeSession, activeBucket);
            if (trailDist <= 0) return;

            double newStop = isLong
                ? Instrument.MasterInstrument.RoundToTickSize(bestPriceSinceEntry - trailDist)
                : Instrument.MasterInstrument.RoundToTickSize(bestPriceSinceEntry + trailDist);

            if (B_TrailStepTicks(activeSession, activeBucket) > 0)
            {
                double stepDist = B_TrailStepTicks(activeSession, activeBucket) * TickSize;
                double moveDist = isLong ? newStop - currentStopPrice : currentStopPrice - newStop;
                if (moveDist < stepDist) return;
            }

            bool improves = isLong ? newStop > currentStopPrice : newStop < currentStopPrice;
            if (!improves) return;
            SetStopLoss(CalculationMode.Price, newStop);
            currentStopPrice = newStop;
            Print(Time[0] + " - Sess" + activeSession + " " + activeBucket + " Trail -> " + newStop.ToString("F2") + " | Profit: " + profit.ToString("F2") + " pts");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Helpers
        // ════════════════════════════════════════════════════════════════════════

        private void ResetAllBucketCounters()
        {
            dailyPnL_Sess1_L1        = 0;
            dailyTradeCount_Sess1_L1 = 0;
            dailyLossHit_Sess1_L1    = false;
            dailyProfitHit_Sess1_L1  = false;
            dailyPnL_Sess1_L2        = 0;
            dailyTradeCount_Sess1_L2 = 0;
            dailyLossHit_Sess1_L2    = false;
            dailyProfitHit_Sess1_L2  = false;
            dailyPnL_Sess1_S1        = 0;
            dailyTradeCount_Sess1_S1 = 0;
            dailyLossHit_Sess1_S1    = false;
            dailyProfitHit_Sess1_S1  = false;
            dailyPnL_Sess1_S2        = 0;
            dailyTradeCount_Sess1_S2 = 0;
            dailyLossHit_Sess1_S2    = false;
            dailyProfitHit_Sess1_S2  = false;
            dailyPnL_Sess2_L1        = 0;
            dailyTradeCount_Sess2_L1 = 0;
            dailyLossHit_Sess2_L1    = false;
            dailyProfitHit_Sess2_L1  = false;
            dailyPnL_Sess2_L2        = 0;
            dailyTradeCount_Sess2_L2 = 0;
            dailyLossHit_Sess2_L2    = false;
            dailyProfitHit_Sess2_L2  = false;
            dailyPnL_Sess2_S1        = 0;
            dailyTradeCount_Sess2_S1 = 0;
            dailyLossHit_Sess2_S1    = false;
            dailyProfitHit_Sess2_S1  = false;
            dailyPnL_Sess2_S2        = 0;
            dailyTradeCount_Sess2_S2 = 0;
            dailyLossHit_Sess2_S2    = false;
            dailyProfitHit_Sess2_S2  = false;
            dailyPnL_Sess3_L1        = 0;
            dailyTradeCount_Sess3_L1 = 0;
            dailyLossHit_Sess3_L1    = false;
            dailyProfitHit_Sess3_L1  = false;
            dailyPnL_Sess3_L2        = 0;
            dailyTradeCount_Sess3_L2 = 0;
            dailyLossHit_Sess3_L2    = false;
            dailyProfitHit_Sess3_L2  = false;
            dailyPnL_Sess3_S1        = 0;
            dailyTradeCount_Sess3_S1 = 0;
            dailyLossHit_Sess3_S1    = false;
            dailyProfitHit_Sess3_S1  = false;
            dailyPnL_Sess3_S2        = 0;
            dailyTradeCount_Sess3_S2 = 0;
            dailyLossHit_Sess3_S2    = false;
            dailyProfitHit_Sess3_S2  = false;
            dailyPnL_Sess4_L1        = 0;
            dailyTradeCount_Sess4_L1 = 0;
            dailyLossHit_Sess4_L1    = false;
            dailyProfitHit_Sess4_L1  = false;
            dailyPnL_Sess4_L2        = 0;
            dailyTradeCount_Sess4_L2 = 0;
            dailyLossHit_Sess4_L2    = false;
            dailyProfitHit_Sess4_L2  = false;
            dailyPnL_Sess4_S1        = 0;
            dailyTradeCount_Sess4_S1 = 0;
            dailyLossHit_Sess4_S1    = false;
            dailyProfitHit_Sess4_S1  = false;
            dailyPnL_Sess4_S2        = 0;
            dailyTradeCount_Sess4_S2 = 0;
            dailyLossHit_Sess4_S2    = false;
            dailyProfitHit_Sess4_S2  = false;
        }

        private void ResetTrailState()
        {
            bestPriceSinceEntry = entryPrice;
            trailActivated      = false;
            trailLocked         = false;
        }

        private void CancelAllOrders()
        {
            if (entryOrder != null) { CancelOrder(entryOrder); entryOrder = null; }
        }

        private void ResetSignals()
        {
            longSignalActive  = false;
            shortSignalActive = false;
            pendingDirection  = 0;
            entryOrder        = null;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Order / Execution Callbacks
        // ════════════════════════════════════════════════════════════════════════

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null) return;
            if (order.Name == LongEntrySignalName || order.Name == ShortEntrySignalName)
            {
                if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                {
                    SendWebhook("cancel");
                    entryOrder = null; ResetSignals();
                }
                else if (orderState == OrderState.Filled)
                {
                    entryOrder  = null;
                    entryPrice  = averageFillPrice;
                    bestPriceSinceEntry = averageFillPrice;
                }
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null) return;

            // ── Webhook: fire on entry fills ─────────────────────────────────
            string orderName = execution.Order.Name ?? string.Empty;
            if (orderName == LongEntrySignalName || orderName == ShortEntrySignalName)
            {
                string action = orderName == LongEntrySignalName ? "buy" : "sell";
                bool isMarket = execution.Order.OrderType == OrderType.Market || execution.Order.OrderType == OrderType.StopMarket;
                SendWebhook(action, price, 0, currentStopPrice, isMarket, quantity);
                return;
            }

            if (Position.MarketPosition != MarketPosition.Flat) return;

            double tradePnL = 0;
            if (SystemPerformance != null && SystemPerformance.AllTrades.Count > 0)
                tradePnL = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1].ProfitPoints;

            globalDailyPnL        += tradePnL;
            globalDailyTradeCount += 1;
            if (activeBucket != null && activeSession != 0)
            {
                AddBucketDailyPnL(activeSession, activeBucket, tradePnL);
                IncBucketTradeCount(activeSession, activeBucket);
            }
            lastTradeDirection[activeSession] = currentTradeDirection;
            lastTradePnL[activeSession]       = tradePnL;
            Print(Time[0] + " - Sess" + activeSession + " " + (activeBucket ?? "?") + " Trade #" + globalDailyTradeCount
                + " closed. " + (lastTradeDirection[activeSession] == 1 ? "Long" : "Short")
                + ". P&L: " + tradePnL + " pts. Global: " + globalDailyPnL + " pts.");

            // ── Webhook: fire exit ───────────────────────────────────────────
            SendWebhook("exit", 0, 0, 0, true, quantity);

            ResetSignals();
            breakEvenMoved      = false;
            activeBucket        = null;
            activeSession       = 0;
            ResetTrailState();
            initialStopDistance = 0;
            initialTPDistance   = 0;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Commercial: Entry Confirmation
        // ════════════════════════════════════════════════════════════════════════

        private bool ShowEntryConfirmation(string orderType, double price, int quantity)
        {
            if (State != State.Realtime) return true;
            bool result = false;
            if (System.Windows.Application.Current == null) return false;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                string message = string.Format(CultureInfo.InvariantCulture,
                    "Confirm {0} entry?\nPrice: {1:F2}\nQuantity: {2}", orderType, price, quantity);
                MessageBoxResult response = System.Windows.MessageBox.Show(message, "Entry Confirmation",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                result = response == System.Windows.MessageBoxResult.Yes;
            });
            return result;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Commercial: Max Account Balance
        // ════════════════════════════════════════════════════════════════════════

        private bool IsAccountBalanceBlocked()
        {
            if (MaxAccountBalance <= 0.0) return false;
            double balance;
            if (!TryGetCurrentNetLiquidation(out balance)) return false;
            if (balance >= MaxAccountBalance && !maxAccountLimitHit)
            {
                maxAccountLimitHit = true;
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0} - Max account balance reached | netLiq={1:0.00} target={2:0.00}",
                    Time[0], balance, MaxAccountBalance));
            }
            if (!maxAccountLimitHit) return false;
            if (entryOrder != null) { CancelOrder(entryOrder); entryOrder = null; }
            if (Position.MarketPosition == MarketPosition.Long)  ExitLong("MaxAccountBalance");
            if (Position.MarketPosition == MarketPosition.Short) ExitShort("MaxAccountBalance");
            return true;
        }

        private bool TryGetCurrentNetLiquidation(out double netLiquidation)
        {
            netLiquidation = 0.0;
            if (Account == null) return false;
            try
            {
                netLiquidation = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                if (netLiquidation > 0.0) return true;
                double realizedCash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                double unrealized = Position.MarketPosition != MarketPosition.Flat
                    ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]) : 0.0;
                netLiquidation = realizedCash + unrealized;
                return realizedCash > 0.0 || Position.MarketPosition != MarketPosition.Flat;
            }
            catch { return false; }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Commercial: Info Box Overlay
        // ════════════════════════════════════════════════════════════════════════

        private void UpdateInfoBox()
        {
            if (State != State.Realtime && State != State.Historical) return;
            if (ChartControl == null || ChartControl.Dispatcher == null) return;
            var lines = BuildInfoLines();
            ChartControl.Dispatcher.InvokeAsync(() => RenderInfoBoxOverlay(lines));
        }

        private void RenderInfoBoxOverlay(List<(string label, string value, Brush labelBrush, Brush valueBrush)> lines)
        {
            if (!EnsureInfoBoxOverlay() || infoBoxRowsPanel == null) return;
            infoBoxRowsPanel.Children.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                bool isHeader = i == 0;
                bool isFooter = i == lines.Count - 1;
                var rowBorder = new Border
                {
                    Background = (isHeader || isFooter) ? InfoHeaderFooterGradientBrush
                        : (i % 2 == 0 ? InfoBodyEvenBrush : InfoBodyOddBrush),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                var text = new TextBlock
                {
                    FontFamily    = new FontFamily("Segoe UI"),
                    FontSize      = isHeader ? 15 : 14,
                    FontWeight    = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
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
                    Brush vb = lines[i].valueBrush ?? InfoValueBrush;
                    text.Inlines.Add(new Run(" ") { Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : (lines[i].labelBrush ?? InfoLabelBrush) });
                    text.Inlines.Add(new Run(value) { Foreground = vb });
                }
                rowBorder.Child = text;
                infoBoxRowsPanel.Children.Add(rowBorder);
            }
        }

        private bool EnsureInfoBoxOverlay()
        {
            if (ChartControl == null) return false;
            if (infoBoxContainer != null && infoBoxRowsPanel != null) return true;
            var host = ChartControl.Parent as System.Windows.Controls.Panel;
            if (host == null) return false;
            infoBoxRowsPanel = new StackPanel { Orientation = Orientation.Vertical };
            infoBoxContainer = new Border
            {
                Child = infoBoxRowsPanel,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment   = VerticalAlignment.Bottom,
                Margin     = new Thickness(5, 8, 8, 37),
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
                { infoBoxRowsPanel = null; infoBoxContainer = null; return; }
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    if (infoBoxContainer != null)
                    {
                        var parent = infoBoxContainer.Parent as System.Windows.Controls.Panel;
                        if (parent != null) parent.Children.Remove(infoBoxContainer);
                    }
                    infoBoxRowsPanel = null; infoBoxContainer = null;
                });
            }
            catch { infoBoxRowsPanel = null; infoBoxContainer = null; }
        }

        private List<(string label, string value, Brush labelBrush, Brush valueBrush)> BuildInfoLines()
        {
            var lines = new List<(string, string, Brush, Brush)>();
            lines.Add(("HUGO Multi v1", string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(("Contracts:", ContractQuantity.ToString(CultureInfo.InvariantCulture), Brushes.LightGray, InfoValueBrush));
            if (!UseNewsSkip)
            {
                lines.Add(("News:", "Disabled", Brushes.LightGray, InfoValueBrush));
            }
            else
            {
                List<DateTime> weekNews = GetCurrentWeekNews(Time[0]);
                if (weekNews.Count == 0)
                    lines.Add(("News:", "None this week", Brushes.LightGray, Brushes.IndianRed));
                else
                    for (int i = 0; i < weekNews.Count; i++)
                    {
                        DateTime nt = weekNews[i];
                        bool passed = Time[0] > nt.AddMinutes(NewsBlockMinutes);
                        string val  = nt.ToString("ddd", CultureInfo.InvariantCulture) + " " + nt.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
                        Brush nb = passed ? PassedNewsRowBrush : InfoValueBrush;
                        lines.Add(("News:", val, nb, nb));
                    }
            }
            lines.Add(("Session:", GetCurrentSessionInfoText(Time[0]), Brushes.LightGray, InfoValueBrush));
            lines.Add(("AutoEdge Systems™", string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            return lines;
        }

        private string GetCurrentSessionInfoText(DateTime time)
        {
            if (State < State.DataLoaded) return "None";
            double bsm = ToSessionMinutes(time.TimeOfDay);
            int sess = ResolveActiveSession(bsm);
            return sess == 0 ? "None" : string.Format(CultureInfo.InvariantCulture, "Session {0}", sess);
        }

        private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            try { if (brush.CanFreeze) brush.Freeze(); } catch { }
            return brush;
        }

        private static Brush CreateFrozenVerticalGradientBrush(Color top, Color mid, Color bottom)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0.0), EndPoint = new Point(0.5, 1.0) };
            brush.GradientStops.Add(new GradientStop(top, 0.0));
            brush.GradientStops.Add(new GradientStop(mid, 0.5));
            brush.GradientStops.Add(new GradientStop(bottom, 1.0));
            try { if (brush.CanFreeze) brush.Freeze(); } catch { }
            return brush;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Commercial: Chart Drawing (Session windows, skip, news)
        // ════════════════════════════════════════════════════════════════════════

        private void DrawSessionTimeWindows()
        {
            if (CurrentBar < 1) return;
            DrawSessionBackground(Time[0]);
            DrawSkipWindow(Time[0]);
            DrawNewsWindow(Time[0]);
        }

        private void DrawSessionBackground(DateTime barTime)
        {
            for (int s = 1; s <= 4; s++)
            {
                if (!GetSessEnable(s)) continue;
                string twStart = GetSessTradeWindowStartStr(s);
                string sessEnd = GetSessSessionEndStr(s);
                if (string.IsNullOrEmpty(twStart) || string.IsNullOrEmpty(sessEnd)) continue;
                TimeSpan tsS, tsE;
                if (!TimeSpan.TryParse(twStart, out tsS) || !TimeSpan.TryParse(sessEnd, out tsE)) continue;
                double startSM = ToSessionMinutes(tsS);
                double endSM   = ToSessionMinutes(tsE);
                if (startSM == endSM) continue;
                string tag = string.Format("HugoMulti_Sess{0}_{1:yyyyMMdd}", s, barTime.Date);
                if (DrawObjects[tag] != null) continue;
                DateTime ws = barTime.Date + tsS;
                DateTime we = barTime.Date + tsE;
                if (we <= ws) we = we.AddDays(1);
                Draw.Rectangle(this, tag, false, ws, 0, we, 30000, Brushes.Transparent, Brushes.Gold, 10).ZOrder = -1;
            }
        }

        private string GetSessTradeWindowStartStr(int s) { switch(s) { case 1: return Sess1_TradeWindowStart; case 2: return Sess2_TradeWindowStart; case 3: return Sess3_TradeWindowStart; case 4: return Sess4_TradeWindowStart; default: return "00:00"; } }
        private string GetSessSessionEndStr(int s)        { switch(s) { case 1: return Sess1_SessionEndTime;  case 2: return Sess2_SessionEndTime;  case 3: return Sess3_SessionEndTime;  case 4: return Sess4_SessionEndTime;  default: return "17:00"; } }

        private void DrawSkipWindow(DateTime barTime)
        {
            if (!EnableSkipTime || string.IsNullOrEmpty(SkipTimeStart) || string.IsNullOrEmpty(SkipTimeEnd)) return;
            TimeSpan ss, se;
            if (!TimeSpan.TryParse(SkipTimeStart, out ss) || !TimeSpan.TryParse(SkipTimeEnd, out se)) return;
            DateTime ws = barTime.Date + ss;
            DateTime we = barTime.Date + se;
            if (we <= ws) we = we.AddDays(1);
            string tag = string.Format("HugoMulti_Skip_{0:yyyyMMdd_HHmm}", ws);
            if (DrawObjects[tag + "_Rect"] != null) return;
            var areaBrush = new SolidColorBrush(Color.FromArgb(60, 255, 0, 0));
            var lineBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
            try { if (areaBrush.CanFreeze) areaBrush.Freeze(); if (lineBrush.CanFreeze) lineBrush.Freeze(); } catch { }
            Draw.Rectangle(this, tag + "_Rect", false, ws, 0, we, 30000, lineBrush, areaBrush, 2).ZOrder = -1;
            Draw.VerticalLine(this, tag + "_Start", ws, Brushes.Red, DashStyleHelper.DashDot, 2);
            Draw.VerticalLine(this, tag + "_End",   we, Brushes.Red, DashStyleHelper.DashDot, 2);
        }

        private void DrawNewsWindow(DateTime barTime)
        {
            if (!UseNewsSkip) return;
            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime newsTime = NewsDates[i];
                if (newsTime.Date != barTime.Date) continue;
                DateTime ws = newsTime.AddMinutes(-NewsBlockMinutes);
                DateTime we = newsTime.AddMinutes(NewsBlockMinutes);
                string tag = string.Format("HugoMulti_News_{0:yyyyMMdd_HHmm}", newsTime);
                if (DrawObjects[tag + "_Rect"] != null) continue;
                var areaBrush = new SolidColorBrush(Color.FromArgb(60, 255, 165, 0));
                var lineBrush = new SolidColorBrush(Color.FromArgb(20, 30, 144, 255));
                try { if (areaBrush.CanFreeze) areaBrush.Freeze(); if (lineBrush.CanFreeze) lineBrush.Freeze(); } catch { }
                Draw.Rectangle(this, tag + "_Rect", false, ws, 0, we, 30000, lineBrush, areaBrush, 2).ZOrder = -1;
                Draw.VerticalLine(this, tag + "_Start", ws, lineBrush, DashStyleHelper.DashDot, 2);
                Draw.VerticalLine(this, tag + "_End",   we, lineBrush, DashStyleHelper.DashDot, 2);
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Commercial: News Skip Window Logic
        // ════════════════════════════════════════════════════════════════════════

        private bool IsNewsWindowConfigured()
        {
            return UseNewsSkip && NewsBlockMinutes > 0 && NewsDates.Count > 0;
        }

        private bool IsInNewsSkipWindow(DateTime barTime)
        {
            if (!IsNewsWindowConfigured()) return false;
            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime newsTime = NewsDates[i];
                DateTime ws = newsTime.AddMinutes(-NewsBlockMinutes);
                DateTime we = newsTime.AddMinutes(NewsBlockMinutes);
                if (barTime >= ws && barTime < we) return true;
            }
            return false;
        }

        private void EnsureNewsDatesInitialized()
        {
            if (newsDatesInitialized) return;
            NewsDates.Clear();
            LoadHardcodedNewsDates();
            NewsDates.Sort();
            newsDatesInitialized = true;
        }

        private void LoadHardcodedNewsDates()
        {
            if (string.IsNullOrWhiteSpace(NewsDatesRaw)) return;
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
            DayOfWeek firstDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int diff = (7 + (time.DayOfWeek - firstDay)) % 7;
            DateTime weekStart = time.Date.AddDays(-diff);
            DateTime weekEnd   = weekStart.AddDays(7);
            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime c = NewsDates[i];
                if (c >= weekStart && c < weekEnd) weekNews.Add(c);
            }
            weekNews.Sort();
            return weekNews;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Commercial: Webhook (TradersPost)
        // ════════════════════════════════════════════════════════════════════════

        private void SendWebhook(string eventType, double entryPx = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime) return;
            if (string.IsNullOrWhiteSpace(WebhookUrl)) return;
            try
            {
                int orderQty = quantityOverride > 0 ? quantityOverride : Math.Max(1, ContractQuantity);
                string ticker = !string.IsNullOrWhiteSpace(WebhookTickerOverride)
                    ? WebhookTickerOverride.Trim()
                    : (Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "UNKNOWN");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
                string json = string.Empty;
                string action = (eventType ?? string.Empty).ToLowerInvariant();

                if (action == "buy" || action == "sell")
                {
                    string tpPart = takeProfit > 0
                        ? string.Format(CultureInfo.InvariantCulture, ",\"takeProfit\":{{\"limitPrice\":{0}}}", takeProfit)
                        : string.Empty;
                    string slPart = stopLoss > 0
                        ? string.Format(CultureInfo.InvariantCulture, ",\"stopLoss\":{{\"type\":\"stop\",\"stopPrice\":{0}}}", stopLoss)
                        : string.Empty;
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"{1}\",\"orderType\":\"{2}\",\"quantityType\":\"fixed_quantity\",\"quantity\":{3},\"signalPrice\":{4},\"time\":\"{5}\"{6}{7}}}",
                        ticker, action, isMarketEntry ? "market" : "limit", orderQty, entryPx, timestamp, tpPart, slPart);
                }
                else if (action == "exit")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"exit\",\"orderType\":\"market\",\"quantityType\":\"fixed_quantity\",\"quantity\":{1},\"cancel\":true,\"time\":\"{2}\"}}",
                        ticker, orderQty, timestamp);
                }
                else if (action == "cancel")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"cancel\",\"time\":\"{1}\"}}",
                        ticker, timestamp);
                }
                if (string.IsNullOrWhiteSpace(json)) return;
                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.UploadString(WebhookUrl, "POST", json);
                }
            }
            catch { }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Commercial: Timeframe & Instrument Validation (Order Handling Protection)
        // ════════════════════════════════════════════════════════════════════════

        private void ValidateRequiredPrimaryTimeframe(int requiredMinutes)
        {
            bool ok = BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == requiredMinutes;
            isConfiguredTimeframeValid = ok;
            if (ok) return;
            string actual = BarsPeriod == null ? "Unknown"
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", BarsPeriod.Value, BarsPeriod.BarsPeriodType);
            string msg = string.Format(CultureInfo.InvariantCulture,
                "{0} must run on a {1}-minute chart. Current: {2}. Trading disabled until corrected.", Name, requiredMinutes, actual);
            Print("Timeframe validation failed | " + msg);
            ShowValidationPopup(msg, "Invalid Timeframe", ref timeframePopupShown);
        }

        private void ValidateRequiredPrimaryInstrument()
        {
            string inst = Instrument != null && Instrument.MasterInstrument != null
                ? (Instrument.MasterInstrument.Name ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;
            bool ok = inst == "NQ" || inst == "MNQ";
            isConfiguredInstrumentValid = ok;
            if (ok) return;
            string actual = string.IsNullOrWhiteSpace(inst) ? "Unknown" : inst;
            string msg = string.Format(CultureInfo.InvariantCulture,
                "{0} must run on NQ or MNQ. Current instrument: {1}. Trading disabled until corrected.", Name, actual);
            Print("Instrument validation failed | " + msg);
            ShowValidationPopup(msg, "Invalid Instrument", ref instrumentPopupShown);
        }

        private void ShowValidationPopup(string message, string title, ref bool shown)
        {
            if (shown) return;
            shown = true;
            if (ChartControl == null || System.Windows.Application.Current == null) return;
            try
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); }
                    catch { }
                }));
            }
            catch { }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  Commercial: Licensing helper (gets add-on version)
        // ════════════════════════════════════════════════════════════════════════

        private string GetAddOnVersion()
        {
            Assembly assembly = GetType().Assembly;
            Version version = assembly.GetName().Version;
            return version != null ? version.ToString() : "1.0.0.0";
        }

    }   // end class HUGOTesting

    // ════════════════════════════════════════════════════════════════════════
    //  Enums
    // ════════════════════════════════════════════════════════════════════════

    public enum HugoTesting_EntryTypeEnum        { Immediate, WaitForPullback }
    public enum HugoTesting_StopLossTypeEnum     { Fixed, CandleHighLow, EMAFixed, EMATrailing, CandleMultiple }
    public enum HugoTesting_TakeProfitTypeEnum   { RiskReward, Fixed, EMACross, CandleMultiple }
    public enum HugoTesting_BreakEvenTriggerEnum { Points, RiskMultiple }
    public enum HugoTesting_EmaSlopeModeEnum     { DirectionEnforced, MagnitudeOnly }
    public enum HugoTesting_TrailDistanceModeEnum   { FixedPoints, PctOfInitialSL }
    public enum HugoTesting_TrailActivationModeEnum { FixedPoints, PctOfRRTarget }
}
