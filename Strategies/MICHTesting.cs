#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Text;
using System.Text.RegularExpressions;
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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class MICHTesting : Strategy
    {
        private const string StrategySignalPrefix = "MICH";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";
        private const string HeartbeatStrategyName = "MICHTesting";

        public MICHTesting()
        {
            VendorLicense(1235);
        }
        
        #region Private Variables

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


        // ── NY-A indicators ──
        private SMA nyALongSmaHigh;
        private SMA nyALongSmaLow;
        private EMA nyALongEmaHigh;
        private EMA nyALongEmaLow;
        private WMA nyALongWmaFilter;
        private ADX nyALongAdxIndicator;
        private Swing nyALongSwing;
        private EMA nyALongEarlyExitEma;
        private ADX nyALongEarlyExitAdx;
        private ATR nyALongAtr;
        private SMA nyAShortSmaHigh;
        private SMA nyAShortSmaLow;
        private EMA nyAShortEmaHigh;
        private EMA nyAShortEmaLow;
        private WMA nyAShortWmaFilter;
        private ADX nyAShortAdxIndicator;
        private Swing nyAShortSwing;
        private EMA nyAShortEarlyExitEma;
        private ADX nyAShortEarlyExitAdx;
        private ATR nyAShortAtr;

        // ── NY-B indicators ──
        private SMA nyBLongSmaHigh;
        private SMA nyBLongSmaLow;
        private EMA nyBLongEmaHigh;
        private EMA nyBLongEmaLow;
        private WMA nyBLongWmaFilter;
        private ADX nyBLongAdxIndicator;
        private Swing nyBLongSwing;
        private EMA nyBLongEarlyExitEma;
        private ADX nyBLongEarlyExitAdx;
        private ATR nyBLongAtr;
        private SMA nyBShortSmaHigh;
        private SMA nyBShortSmaLow;
        private EMA nyBShortEmaHigh;
        private EMA nyBShortEmaLow;
        private WMA nyBShortWmaFilter;
        private ADX nyBShortAdxIndicator;
        private Swing nyBShortSwing;
        private EMA nyBShortEarlyExitEma;
        private ADX nyBShortEarlyExitAdx;
        private ATR nyBShortAtr;

        // ── NY-C indicators ──
        private SMA nyCLongSmaHigh;
        private SMA nyCLongSmaLow;
        private EMA nyCLongEmaHigh;
        private EMA nyCLongEmaLow;
        private WMA nyCLongWmaFilter;
        private ADX nyCLongAdxIndicator;
        private Swing nyCLongSwing;
        private EMA nyCLongEarlyExitEma;
        private ADX nyCLongEarlyExitAdx;
        private ATR nyCLongAtr;
        private SMA nyCShortSmaHigh;
        private SMA nyCShortSmaLow;
        private EMA nyCShortEmaHigh;
        private EMA nyCShortEmaLow;
        private WMA nyCShortWmaFilter;
        private ADX nyCShortAdxIndicator;
        private Swing nyCShortSwing;
        private EMA nyCShortEarlyExitEma;
        private ADX nyCShortEarlyExitAdx;
        private ATR nyCShortAtr;


        // ── EU-A indicators ──
        private SMA euALongSmaHigh;
        private SMA euALongSmaLow;
        private EMA euALongEmaHigh;
        private EMA euALongEmaLow;
        private WMA euALongWmaFilter;
        private ADX euALongAdxIndicator;
        private Swing euALongSwing;
        private EMA euALongEarlyExitEma;
        private ADX euALongEarlyExitAdx;
        private ATR euALongAtr;
        private SMA euAShortSmaHigh;
        private SMA euAShortSmaLow;
        private EMA euAShortEmaHigh;
        private EMA euAShortEmaLow;
        private WMA euAShortWmaFilter;
        private ADX euAShortAdxIndicator;
        private Swing euAShortSwing;
        private EMA euAShortEarlyExitEma;
        private ADX euAShortEarlyExitAdx;
        private ATR euAShortAtr;

        // ── EU-B indicators ──
        private SMA euBLongSmaHigh;
        private SMA euBLongSmaLow;
        private EMA euBLongEmaHigh;
        private EMA euBLongEmaLow;
        private WMA euBLongWmaFilter;
        private ADX euBLongAdxIndicator;
        private Swing euBLongSwing;
        private EMA euBLongEarlyExitEma;
        private ADX euBLongEarlyExitAdx;
        private ATR euBLongAtr;
        private SMA euBShortSmaHigh;
        private SMA euBShortSmaLow;
        private EMA euBShortEmaHigh;
        private EMA euBShortEmaLow;
        private WMA euBShortWmaFilter;
        private ADX euBShortAdxIndicator;
        private Swing euBShortSwing;
        private EMA euBShortEarlyExitEma;
        private ADX euBShortEarlyExitAdx;
        private ATR euBShortAtr;

        // ── EU-C indicators ──
        private SMA euCLongSmaHigh;
        private SMA euCLongSmaLow;
        private EMA euCLongEmaHigh;
        private EMA euCLongEmaLow;
        private WMA euCLongWmaFilter;
        private ADX euCLongAdxIndicator;
        private Swing euCLongSwing;
        private EMA euCLongEarlyExitEma;
        private ADX euCLongEarlyExitAdx;
        private ATR euCLongAtr;
        private SMA euCShortSmaHigh;
        private SMA euCShortSmaLow;
        private EMA euCShortEmaHigh;
        private EMA euCShortEmaLow;
        private WMA euCShortWmaFilter;
        private ADX euCShortAdxIndicator;
        private Swing euCShortSwing;
        private EMA euCShortEarlyExitEma;
        private ADX euCShortEarlyExitAdx;
        private ATR euCShortAtr;


        // ── AS-A indicators ──
        private SMA asALongSmaHigh;
        private SMA asALongSmaLow;
        private EMA asALongEmaHigh;
        private EMA asALongEmaLow;
        private WMA asALongWmaFilter;
        private ADX asALongAdxIndicator;
        private Swing asALongSwing;
        private EMA asALongEarlyExitEma;
        private ADX asALongEarlyExitAdx;
        private ATR asALongAtr;
        private SMA asAShortSmaHigh;
        private SMA asAShortSmaLow;
        private EMA asAShortEmaHigh;
        private EMA asAShortEmaLow;
        private WMA asAShortWmaFilter;
        private ADX asAShortAdxIndicator;
        private Swing asAShortSwing;
        private EMA asAShortEarlyExitEma;
        private ADX asAShortEarlyExitAdx;
        private ATR asAShortAtr;

        // ── AS-B indicators ──
        private SMA asBLongSmaHigh;
        private SMA asBLongSmaLow;
        private EMA asBLongEmaHigh;
        private EMA asBLongEmaLow;
        private WMA asBLongWmaFilter;
        private ADX asBLongAdxIndicator;
        private Swing asBLongSwing;
        private EMA asBLongEarlyExitEma;
        private ADX asBLongEarlyExitAdx;
        private ATR asBLongAtr;
        private SMA asBShortSmaHigh;
        private SMA asBShortSmaLow;
        private EMA asBShortEmaHigh;
        private EMA asBShortEmaLow;
        private WMA asBShortWmaFilter;
        private ADX asBShortAdxIndicator;
        private Swing asBShortSwing;
        private EMA asBShortEarlyExitEma;
        private ADX asBShortEarlyExitAdx;
        private ATR asBShortAtr;

        // ── AS-C indicators ──
        private SMA asCLongSmaHigh;
        private SMA asCLongSmaLow;
        private EMA asCLongEmaHigh;
        private EMA asCLongEmaLow;
        private WMA asCLongWmaFilter;
        private ADX asCLongAdxIndicator;
        private Swing asCLongSwing;
        private EMA asCLongEarlyExitEma;
        private ADX asCLongEarlyExitAdx;
        private ATR asCLongAtr;
        private SMA asCShortSmaHigh;
        private SMA asCShortSmaLow;
        private EMA asCShortEmaHigh;
        private EMA asCShortEmaLow;
        private WMA asCShortWmaFilter;
        private ADX asCShortAdxIndicator;
        private Swing asCShortSwing;
        private EMA asCShortEarlyExitEma;
        private ADX asCShortEarlyExitAdx;
        private ATR asCShortAtr;


        // ── Ny session state ──
        private int nySessionTradeCount;
        private int nySessionWinCount;
        private int nySessionLossCount;
        private double nySessionPnLTicks;
        private bool nySessionLimitsReached;
        private int nyLastTradeDirection;
        private bool nyLastTradeWasLoss;
        private bool nyWasInNoTradesAfterWindow;

        // ── Eu session state ──
        private int euSessionTradeCount;
        private int euSessionWinCount;
        private int euSessionLossCount;
        private double euSessionPnLTicks;
        private bool euSessionLimitsReached;
        private int euLastTradeDirection;
        private bool euLastTradeWasLoss;
        private bool euWasInNoTradesAfterWindow;

        // ── As session state ──
        private int asSessionTradeCount;
        private int asSessionWinCount;
        private int asSessionLossCount;
        private double asSessionPnLTicks;
        private bool asSessionLimitsReached;
        private int asLastTradeDirection;
        private bool asLastTradeWasLoss;
        private bool asWasInNoTradesAfterWindow;

        // ── Active session tracking ──
        private int activeSessionId;  // 0 = none, 1 = NY, 2 = EU, 3 = Asia
        private int lastInfoSessionId;
        private DateTime currentSessionDate;

        // ────────────────────────────────────────────────────────
        //  v1 — 9 SUB-SESSION STATE (independent per sub-session)
        //  Mapping:  1=NY-A, 2=NY-B, 3=NY-C,
        //            4=EU-A, 5=EU-B, 6=EU-C,
        //            7=AS-A, 8=AS-B, 9=AS-C
        // ────────────────────────────────────────────────────────
        private const int SubSessionCount = 9;
        private readonly int[] subTradeCount        = new int[SubSessionCount + 1];
        private readonly int[] subWinCount          = new int[SubSessionCount + 1];
        private readonly int[] subLossCount         = new int[SubSessionCount + 1];
        private readonly double[] subPnLTicks       = new double[SubSessionCount + 1];
        private readonly bool[] subLimitsReached    = new bool[SubSessionCount + 1];
        private readonly int[] subLastTradeDirection = new int[SubSessionCount + 1];
        private readonly bool[] subLastTradeWasLoss  = new bool[SubSessionCount + 1];
        private readonly bool[] subWasInNoTradesAfterWindow = new bool[SubSessionCount + 1];

        // ── Trade state (shared — only one trade at a time) ──
        private int tradeDirection;
        private double signalCandleRange;
        private double priorCandleHigh;
        private double priorCandleLow;
        private double originalStopPrice;
        private double tradeEntryPrice;
        private bool hasActivePosition;
        private int barsSinceEntry;
        private bool breakEvenApplied;
        private DateTime tradeEntryTime;
        private double opposingBarBenchmark;
        private bool opposingBarBenchmarkSet;
        private bool priceOffsetTrailActive;
        private double priceOffsetTrailDistance;
        // ── R-Multiple trail state ──
        private double rMultipleSlSize;       // Captured initial SL distance at entry
        private double rMultipleBestPrice;    // High watermark (long) / Low watermark (short)
        private bool   rMultipleActivated;    // True once profit >= ActivationPct of SL size
        private bool   rMultipleLocked;       // True once profit >= LockPct of SL size
        private bool entryBarSlApplied;
        private MarketPosition prevMarketPosition;
        private Order entryOrder;
        private Order stopOrder;
        private Order targetOrder;
        private bool forceFlattenInProgress;
        private bool forceFlattenOrderSubmitted;
        private string forceFlattenReason;
        private double tradePeakAdx;
        private double entryAtr;            // ATR value captured at entry bar close

        // ── Time-window transition tracking ──
        private bool wasInNewsSkipWindow;

        // ── News skip ──
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

        // ── Info box overlay ──
        private Border infoBoxContainer;
        private StackPanel infoBoxRowsPanel;
        private bool legacyInfoDrawingsCleared;
        private static readonly Brush InfoHeaderFooterGradientBrush = CreateFrozenVerticalGradientBrush(
            Color.FromArgb(240, 0x2A, 0x2F, 0x45),
            Color.FromArgb(240, 0x1E, 0x23, 0x36),
            Color.FromArgb(240, 0x14, 0x18, 0x28));
        private static readonly Brush InfoBodyOddBrush = CreateFrozenBrush(240, 0x0F, 0x0F, 0x17);
        private static readonly Brush InfoBodyEvenBrush = CreateFrozenBrush(240, 0x11, 0x11, 0x18);
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(255, 0xFF, 0x8C, 0x00);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);
        private static readonly Brush PassedNewsRowBrush = CreateFrozenBrush(30, 211, 211, 211);
        private static readonly FontFamily InfoEmojiFontFamily = new FontFamily("Segoe UI Emoji");
        private static readonly FontFamily InfoSymbolFontFamily = new FontFamily("Segoe UI Symbol");
        private static readonly HashSet<string> InfoEmojiTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "✔", "✅", "❌", "✖", "⛔", "⬜", "🕒", "🚫"
        };
        private static readonly HashSet<string> InfoSymbolTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "■", "□", "●", "○", "▲", "▼", "◆", "◇"
        };
        private bool isConfiguredTimeframeValid = true;
        private bool isConfiguredInstrumentValid = true;
        private bool timeframePopupShown;
        private bool instrumentPopupShown;
        private bool maxAccountLimitHit;
        private StrategyHeartbeatReporter heartbeatReporter;
        private string projectXSessionToken;
        private DateTime projectXTokenAcquiredUtc = Core.Globals.MinDate;
        private List<ProjectXAccountInfo> projectXAccounts;
        private readonly Dictionary<string, long> projectXLastOrderIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private string projectXResolvedContractId;
        private string projectXResolvedInstrumentKey = string.Empty;
        private bool suppressProjectXNextExecutionExitWebhook;
        private double projectXLastSyncedStopPrice;
        private double projectXLastSyncedTargetPrice;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                     = @"MICHTesting — NY / EU / Asia with ORBO-style trail (bestPrice watermark, activation threshold, trail lock).";
                Name                            = "MICHTesting";
                Calculate                       = Calculate.OnBarClose;
                EntriesPerDirection             = 1;
                EntryHandling                   = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy    = false;
                ExitOnSessionCloseSeconds       = 30;
                IsFillLimitOnTouch              = false;
                MaximumBarsLookBack             = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution             = OrderFillResolution.Standard;
                Slippage                        = 0;
                StartBehavior                   = StartBehavior.WaitUntilFlat;
                TimeInForce                     = TimeInForce.Gtc;
                TraceOrders                     = true;
                RealtimeErrorHandling           = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling              = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade             = 20;
                IsInstantiatedOnEachOptimizationIteration = true;

                // ─── General ───
                RequireEntryConfirmation        = false;
                SessionStartTime                = DateTime.Parse("18:00", System.Globalization.CultureInfo.InvariantCulture);
                UseNewsSkip                     = false;
                NewsBlockMinutes                = 1;
                FlattenOnBlockedWindowTransition = false;
                DebugLogging                    = true;
                MaxAccountBalance               = 0.0;
                UseWebhooks                     = false;
                WebhookProviderType             = WebhookProvider.TradersPost;
                WebhookUrl                      = string.Empty;
                WebhookTickerOverride           = string.Empty;
                ProjectXApiBaseUrl              = "https://api.topstepx.com";
                ProjectXTradeAllAccounts        = false;
                ProjectXUsername                = string.Empty;
                ProjectXApiKey                  = string.Empty;
                ProjectXAccountId               = string.Empty;
                ProjectXContractId              = string.Empty;


                
                // ════════════════════════════════════════
                //  9 SUB-SESSION WINDOW DEFAULTS (v1)
                // ════════════════════════════════════════
                NyAEnable                      = true;
                NyATradeWindowStart            = DateTime.Parse("09:45", System.Globalization.CultureInfo.InvariantCulture);
                NyAEnableNoNewTradesAfter      = true;
                NyANoNewTradesAfter            = DateTime.Parse("11:35", System.Globalization.CultureInfo.InvariantCulture);
                NyAEnableForcedClose           = false;
                NyAForcedCloseTime             = DateTime.Parse("11:55", System.Globalization.CultureInfo.InvariantCulture);

                NyBEnable                      = true;
                NyBTradeWindowStart            = DateTime.Parse("11:30", System.Globalization.CultureInfo.InvariantCulture);
                NyBEnableNoNewTradesAfter      = true;
                NyBNoNewTradesAfter            = DateTime.Parse("14:00", System.Globalization.CultureInfo.InvariantCulture);
                NyBEnableForcedClose           = false;
                NyBForcedCloseTime             = DateTime.Parse("14:25", System.Globalization.CultureInfo.InvariantCulture);

                NyCEnable                      = true;
                NyCTradeWindowStart            = DateTime.Parse("14:00", System.Globalization.CultureInfo.InvariantCulture);
                NyCEnableNoNewTradesAfter      = true;
                NyCNoNewTradesAfter            = DateTime.Parse("15:00", System.Globalization.CultureInfo.InvariantCulture);
                NyCEnableForcedClose           = true;
                NyCForcedCloseTime             = DateTime.Parse("15:55", System.Globalization.CultureInfo.InvariantCulture);

                EuAEnable                      = true;
                EuATradeWindowStart            = DateTime.Parse("02:05", System.Globalization.CultureInfo.InvariantCulture);
                EuAEnableNoNewTradesAfter      = true;
                EuANoNewTradesAfter            = DateTime.Parse("03:00", System.Globalization.CultureInfo.InvariantCulture);
                EuAEnableForcedClose           = true;
                EuAForcedCloseTime             = DateTime.Parse("03:50", System.Globalization.CultureInfo.InvariantCulture);

                EuBEnable                      = true;
                EuBTradeWindowStart            = DateTime.Parse("03:00", System.Globalization.CultureInfo.InvariantCulture);
                EuBEnableNoNewTradesAfter      = true;
                EuBNoNewTradesAfter            = DateTime.Parse("05:00", System.Globalization.CultureInfo.InvariantCulture);
                EuBEnableForcedClose           = true;
                EuBForcedCloseTime             = DateTime.Parse("05:05", System.Globalization.CultureInfo.InvariantCulture);

                EuCEnable                      = true;
                EuCTradeWindowStart            = DateTime.Parse("05:00", System.Globalization.CultureInfo.InvariantCulture);
                EuCEnableNoNewTradesAfter      = true;
                EuCNoNewTradesAfter            = DateTime.Parse("08:00", System.Globalization.CultureInfo.InvariantCulture);
                EuCEnableForcedClose           = true;
                EuCForcedCloseTime             = DateTime.Parse("08:25", System.Globalization.CultureInfo.InvariantCulture);

                AsAEnable                      = true;
                AsATradeWindowStart            = DateTime.Parse("18:30", System.Globalization.CultureInfo.InvariantCulture);
                AsAEnableNoNewTradesAfter      = true;
                AsANoNewTradesAfter            = DateTime.Parse("20:00", System.Globalization.CultureInfo.InvariantCulture);
                AsAEnableForcedClose           = false;
                AsAForcedCloseTime             = DateTime.Parse("20:00", System.Globalization.CultureInfo.InvariantCulture);

                AsBEnable                      = true;
                AsBTradeWindowStart            = DateTime.Parse("20:00", System.Globalization.CultureInfo.InvariantCulture);
                AsBEnableNoNewTradesAfter      = true;
                AsBNoNewTradesAfter            = DateTime.Parse("23:59", System.Globalization.CultureInfo.InvariantCulture);
                AsBEnableForcedClose           = true;
                AsBForcedCloseTime             = DateTime.Parse("00:59", System.Globalization.CultureInfo.InvariantCulture);

                AsCEnable                      = true;
                AsCTradeWindowStart            = DateTime.Parse("00:05", System.Globalization.CultureInfo.InvariantCulture);
                AsCEnableNoNewTradesAfter      = true;
                AsCNoNewTradesAfter            = DateTime.Parse("01:15", System.Globalization.CultureInfo.InvariantCulture);
                AsCEnableForcedClose           = true;
                AsCForcedCloseTime             = DateTime.Parse("02:00", System.Globalization.CultureInfo.InvariantCulture);

                // ════════════════════════════════════════
                //  NEW YORK SESSION DEFAULTS
                // ════════════════════════════════════════
                NyEnable                                       = true;
                NyAMaxTradesPerSession = 3;
                NyBMaxTradesPerSession = 3;
                NyCMaxTradesPerSession = 3;
                NyAMaxLossesPerSession = 2;
                NyBMaxLossesPerSession = 2;
                NyCMaxLossesPerSession = 2;
                NyAMaxSessionProfitTicks = 1075;
                NyBMaxSessionProfitTicks = 1075;
                NyCMaxSessionProfitTicks = 1075;
                NyAMaxSessionLossTicks = 160;
                NyBMaxSessionLossTicks = 160;
                NyCMaxSessionLossTicks = 160;
                NyAContracts = 1;
                NyBContracts = 1;
                NyCContracts = 1;
                NyAEnableLongTrades = true;
                NyBEnableLongTrades = true;
                NyCEnableLongTrades = true;
                NyAEnableShortTrades = true;
                NyBEnableShortTrades = true;
                NyCEnableShortTrades = true;
                NyALongMaPeriod = 58;
                NyBLongMaPeriod = 21;
                NyCLongMaPeriod = 54;
                NyALongEmaPeriod = 46;
                NyBLongEmaPeriod = 24;
                NyCLongEmaPeriod = 21;
                NyALongMaType = MAMode.Both;
                NyBLongMaType = MAMode.Both;
                NyCLongMaType = MAMode.Both;
                NyALongMaxTakeProfitTicks = 1179;
                NyBLongMaxTakeProfitTicks = 1179;
                NyCLongMaxTakeProfitTicks = 1179;
                NyALongCandleMultiplier = 4.07;
                NyBLongCandleMultiplier = 4.07;
                NyCLongCandleMultiplier = 4.07;
                NyALongAtrPeriod = 14;
                NyBLongAtrPeriod = 14;
                NyCLongAtrPeriod = 14;
                NyALongAtrThresholdTicks = 100;
                NyBLongAtrThresholdTicks = 100;
                NyCLongAtrThresholdTicks = 100;
                NyALongLowAtrCandleMultiplier = 5.13;
                NyBLongLowAtrCandleMultiplier = 4.05;
                NyCLongLowAtrCandleMultiplier = 3.39;
                NyALongHighAtrCandleMultiplier = 5.13;
                NyBLongHighAtrCandleMultiplier = 4.35;
                NyCLongHighAtrCandleMultiplier = 4.07;
                NyALongSlExtraTicks = 41;
                NyBLongSlExtraTicks = 35;
                NyCLongSlExtraTicks = 48;
                NyALongTrailOffsetTicks = 34;
                NyBLongTrailOffsetTicks = 31;
                NyCLongTrailOffsetTicks = 13;
                NyALongTrailDelayBars = 4;
                NyBLongTrailDelayBars = 1;
                NyCLongTrailDelayBars = 6;
                NyALongMaxSlTicks = 330;
                NyBLongMaxSlTicks = 180;
                NyCLongMaxSlTicks = 181;
                NyALongMaxSlToTpRatio = 0.5;
                NyBLongMaxSlToTpRatio = 0.53;
                NyCLongMaxSlToTpRatio = 0.45;
                NyALongUsePriorCandleSl = true;
                NyBLongUsePriorCandleSl = true;
                NyCLongUsePriorCandleSl = false;
                NyALongSlAtMa = false;
                NyBLongSlAtMa = false;
                NyCLongSlAtMa = false;
                NyALongMoveSlToEntryBar = false;
                NyBLongMoveSlToEntryBar = false;
                NyCLongMoveSlToEntryBar = false;
                NyALongTrailCandleOffset = 19;
                NyBLongTrailCandleOffset = 19;
                NyCLongTrailCandleOffset = 0;
                NyALongEnableBreakeven = true;
                NyBLongEnableBreakeven = true;
                NyCLongEnableBreakeven = true;
                NyALongBreakevenTriggerTicks = 382;
                NyBLongBreakevenTriggerTicks = 374;
                NyCLongBreakevenTriggerTicks = 340;
                NyALongBreakevenCandlePct = 50.0;
                NyBLongBreakevenCandlePct = 50.0;
                NyCLongBreakevenCandlePct = 50.0;
                NyALongBreakevenOffsetTicks = 8;
                NyBLongBreakevenOffsetTicks = 6;
                NyCLongBreakevenOffsetTicks = 35;
                NyALongMaxBarsInTrade = 28;
                NyBLongMaxBarsInTrade = 28;
                NyCLongMaxBarsInTrade = 15;
                NyALongEnablePriceOffsetTrail = true;
                NyBLongEnablePriceOffsetTrail = true;
                NyCLongEnablePriceOffsetTrail = true;
                NyALongPriceOffsetReductionTicks = 26;
                NyBLongPriceOffsetReductionTicks = 20;
                NyCLongPriceOffsetReductionTicks = 6;
                NyALongEnableRMultipleTrail = true;
                NyBLongEnableRMultipleTrail = false;
                NyCLongEnableRMultipleTrail = true;
                NyALongRMultipleActivationPct = 162.0;
                NyBLongRMultipleActivationPct = 200.0;
                NyCLongRMultipleActivationPct = 173.0;
                NyALongRMultipleTrailPct = 200.0;
                NyBLongRMultipleTrailPct = 143.0;
                NyCLongRMultipleTrailPct = 67.0;
                NyALongRMultipleLockPct = 0.0;
                NyBLongRMultipleLockPct = 0.0;
                NyCLongRMultipleLockPct = 10.0;
                NyALongMAEMaxTicks = 300;
                NyBLongMAEMaxTicks = 180;
                NyCLongMAEMaxTicks = 116;
                NyALongRequireDirectionFlip = true;
                NyBLongRequireDirectionFlip = true;
                NyCLongRequireDirectionFlip = true;
                NyALongAllowSameDirectionAfterLoss = true;
                NyBLongAllowSameDirectionAfterLoss = true;
                NyCLongAllowSameDirectionAfterLoss = true;
                NyALongRequireSmaSlope = false;
                NyBLongRequireSmaSlope = false;
                NyCLongRequireSmaSlope = false;
                NyALongSmaSlopeLookback = 1;
                NyBLongSmaSlopeLookback = 4;
                NyCLongSmaSlopeLookback = 7;
                NyALongEnableWmaFilter = true;
                NyBLongEnableWmaFilter = true;
                NyCLongEnableWmaFilter = true;
                NyALongWmaPeriod = 152;
                NyBLongWmaPeriod = 196;
                NyCLongWmaPeriod = 119;
                NyALongMinBodyPct = 60.0;
                NyBLongMinBodyPct = 58.0;
                NyCLongMinBodyPct = 61.0;
                NyALongTrendConfirmBars = 13;
                NyBLongTrendConfirmBars = 16;
                NyCLongTrendConfirmBars = 12;
                NyALongEnableAdxFilter = true;
                NyBLongEnableAdxFilter = true;
                NyCLongEnableAdxFilter = true;
                NyALongAdxPeriod = 12;
                NyBLongAdxPeriod = 21;
                NyCLongAdxPeriod = 20;
                NyALongAdxMinLevel = 15;
                NyBLongAdxMinLevel = 15;
                NyCLongAdxMinLevel = 15;
                NyALongUseEmaEarlyExit = false;
                NyBLongUseEmaEarlyExit = true;
                NyCLongUseEmaEarlyExit = true;
                NyALongEmaEarlyExitPeriod = 50;
                NyBLongEmaEarlyExitPeriod = 25;
                NyCLongEmaEarlyExitPeriod = 27;
                NyALongUseAdxEarlyExit = true;
                NyBLongUseAdxEarlyExit = true;
                NyCLongUseAdxEarlyExit = true;
                NyALongAdxEarlyExitPeriod = 19;
                NyBLongAdxEarlyExitPeriod = 15;
                NyCLongAdxEarlyExitPeriod = 12;
                NyALongAdxEarlyExitMin = 15.0;
                NyBLongAdxEarlyExitMin = 13.0;
                NyCLongAdxEarlyExitMin = 19.0;
                NyALongUseAdxDrawdownExit = true;
                NyBLongUseAdxDrawdownExit = true;
                NyCLongUseAdxDrawdownExit = true;
                NyALongAdxDrawdownFromPeak = 16.0;
                NyBLongAdxDrawdownFromPeak = 9.0;
                NyCLongAdxDrawdownFromPeak = 12.0;
                NyAShortMaPeriod = 37;
                NyBShortMaPeriod = 40;
                NyCShortMaPeriod = 22;
                NyAShortEmaPeriod = 30;
                NyBShortEmaPeriod = 30;
                NyCShortEmaPeriod = 23;
                NyAShortMaType = MAMode.SMA;
                NyBShortMaType = MAMode.SMA;
                NyCShortMaType = MAMode.SMA;
                NyAShortMaxTakeProfitTicks = 1179;
                NyBShortMaxTakeProfitTicks = 1179;
                NyCShortMaxTakeProfitTicks = 1179;
                NyAShortCandleMultiplier = 4.3;
                NyBShortCandleMultiplier = 4.3;
                NyCShortCandleMultiplier = 4.3;
                NyAShortAtrPeriod = 14;
                NyBShortAtrPeriod = 14;
                NyCShortAtrPeriod = 14;
                NyAShortAtrThresholdTicks = 100;
                NyBShortAtrThresholdTicks = 100;
                NyCShortAtrThresholdTicks = 100;
                NyAShortLowAtrCandleMultiplier = 4.24;
                NyBShortLowAtrCandleMultiplier = 4.91;
                NyCShortLowAtrCandleMultiplier = 3.41;
                NyAShortHighAtrCandleMultiplier = 2.89;
                NyBShortHighAtrCandleMultiplier = 3.0;
                NyCShortHighAtrCandleMultiplier = 1.99;
                NyAShortSlExtraTicks = 42;
                NyBShortSlExtraTicks = 16;
                NyCShortSlExtraTicks = 9;
                NyAShortTrailOffsetTicks = 43;
                NyBShortTrailOffsetTicks = 37;
                NyCShortTrailOffsetTicks = 6;
                NyAShortTrailDelayBars = 1;
                NyBShortTrailDelayBars = 3;
                NyCShortTrailDelayBars = 2;
                NyAShortMaxSlTicks = 327;
                NyBShortMaxSlTicks = 244;
                NyCShortMaxSlTicks = 130;
                NyAShortMaxSlToTpRatio = 0.5;
                NyBShortMaxSlToTpRatio = 0.55;
                NyCShortMaxSlToTpRatio = 0.42;
                NyAShortUsePriorCandleSl = true;
                NyBShortUsePriorCandleSl = true;
                NyCShortUsePriorCandleSl = true;
                NyAShortSlAtMa = true;
                NyBShortSlAtMa = false;
                NyCShortSlAtMa = false;
                NyAShortMoveSlToEntryBar = false;
                NyBShortMoveSlToEntryBar = false;
                NyCShortMoveSlToEntryBar = true;
                NyAShortTrailCandleOffset = 0;
                NyBShortTrailCandleOffset = 13;
                NyCShortTrailCandleOffset = 14;
                NyAShortEnableBreakeven = true;
                NyBShortEnableBreakeven = true;
                NyCShortEnableBreakeven = true;
                NyAShortBreakevenTriggerTicks = 355;
                NyBShortBreakevenTriggerTicks = 355;
                NyCShortBreakevenTriggerTicks = 300;
                NyAShortBreakevenOffsetTicks = 3;
                NyBShortBreakevenOffsetTicks = 53;
                NyCShortBreakevenOffsetTicks = 20;
                NyAShortMaxBarsInTrade = 23;
                NyBShortMaxBarsInTrade = 25;
                NyCShortMaxBarsInTrade = 22;
                NyAShortEnablePriceOffsetTrail = false;
                NyBShortEnablePriceOffsetTrail = false;
                NyCShortEnablePriceOffsetTrail = true;
                NyAShortPriceOffsetReductionTicks = 0;
                NyBShortPriceOffsetReductionTicks = 0;
                NyCShortPriceOffsetReductionTicks = 12;
                NyAShortEnableRMultipleTrail = true;
                NyBShortEnableRMultipleTrail = false;
                NyCShortEnableRMultipleTrail = true;
                NyAShortRMultipleActivationPct = 289.0;
                NyBShortRMultipleActivationPct = 50.0;
                NyCShortRMultipleActivationPct = 240.0;
                NyAShortRMultipleTrailPct = 153.0;
                NyBShortRMultipleTrailPct = 75.0;
                NyCShortRMultipleTrailPct = 20.0;
                NyAShortRMultipleLockPct = 10.0;
                NyBShortRMultipleLockPct = 0.0;
                NyCShortRMultipleLockPct = 0.0;
                NyAShortMAEMaxTicks = 302;
                NyBShortMAEMaxTicks = 240;
                NyCShortMAEMaxTicks = 165;
                NyAShortRequireDirectionFlip = true;
                NyBShortRequireDirectionFlip = true;
                NyCShortRequireDirectionFlip = true;
                NyAShortAllowSameDirectionAfterLoss = true;
                NyBShortAllowSameDirectionAfterLoss = true;
                NyCShortAllowSameDirectionAfterLoss = true;
                NyAShortRequireSmaSlope = false;
                NyBShortRequireSmaSlope = false;
                NyCShortRequireSmaSlope = false;
                NyAShortSmaSlopeLookback = 3;
                NyBShortSmaSlopeLookback = 3;
                NyCShortSmaSlopeLookback = 1;
                NyAShortEnableWmaFilter = true;
                NyBShortEnableWmaFilter = true;
                NyCShortEnableWmaFilter = true;
                NyAShortWmaPeriod = 75;
                NyBShortWmaPeriod = 76;
                NyCShortWmaPeriod = 66;
                NyAShortMinBodyPct = 5.0;
                NyBShortMinBodyPct = 18.0;
                NyCShortMinBodyPct = 5.0;
                NyAShortTrendConfirmBars = 13;
                NyBShortTrendConfirmBars = 13;
                NyCShortTrendConfirmBars = 13;
                NyAShortEnableAdxFilter = true;
                NyBShortEnableAdxFilter = true;
                NyCShortEnableAdxFilter = true;
                NyAShortAdxPeriod = 11;
                NyBShortAdxPeriod = 11;
                NyCShortAdxPeriod = 14;
                NyAShortAdxMinLevel = 17;
                NyBShortAdxMinLevel = 17;
                NyCShortAdxMinLevel = 17;
                NyAShortUseEmaEarlyExit = false;
                NyBShortUseEmaEarlyExit = false;
                NyCShortUseEmaEarlyExit = false;
                NyAShortEmaEarlyExitPeriod = 40;
                NyBShortEmaEarlyExitPeriod = 45;
                NyCShortEmaEarlyExitPeriod = 40;
                NyAShortUseAdxEarlyExit = true;
                NyBShortUseAdxEarlyExit = true;
                NyCShortUseAdxEarlyExit = true;
                NyAShortAdxEarlyExitPeriod = 16;
                NyBShortAdxEarlyExitPeriod = 20;
                NyCShortAdxEarlyExitPeriod = 20;
                NyAShortAdxEarlyExitMin = 15.0;
                NyBShortAdxEarlyExitMin = 13.0;
                NyCShortAdxEarlyExitMin = 17.0;
                NyAShortUseAdxDrawdownExit = true;
                NyBShortUseAdxDrawdownExit = true;
                NyCShortUseAdxDrawdownExit = true;
                NyAShortAdxDrawdownFromPeak = 9.0;
                NyBShortAdxDrawdownFromPeak = 9.0;
                NyCShortAdxDrawdownFromPeak = 9.0;

                // ════════════════════════════════════════
                //  EUROPE SESSION DEFAULTS
                // ════════════════════════════════════════
                EuEnable                                       = true;
                EuAMaxTradesPerSession = 3;
                EuBMaxTradesPerSession = 3;
                EuCMaxTradesPerSession = 3;
                EuAMaxLossesPerSession = 2;
                EuBMaxLossesPerSession = 2;
                EuCMaxLossesPerSession = 2;
                EuAMaxSessionProfitTicks = 1075;
                EuBMaxSessionProfitTicks = 1075;
                EuCMaxSessionProfitTicks = 1075;
                EuAMaxSessionLossTicks = 160;
                EuBMaxSessionLossTicks = 160;
                EuCMaxSessionLossTicks = 160;
                EuAContracts = 1;
                EuBContracts = 1;
                EuCContracts = 1;
                EuAEnableLongTrades = true;
                EuBEnableLongTrades = true;
                EuCEnableLongTrades = true;
                EuAEnableShortTrades = true;
                EuBEnableShortTrades = true;
                EuCEnableShortTrades = true;
                EuALongMaPeriod = 82;
                EuBLongMaPeriod = 76;
                EuCLongMaPeriod = 37;
                EuALongEmaPeriod = 39;
                EuBLongEmaPeriod = 48;
                EuCLongEmaPeriod = 24;
                EuALongMaType = MAMode.Both;
                EuBLongMaType = MAMode.Both;
                EuCLongMaType = MAMode.Both;
                EuALongMaxTakeProfitTicks = 890;
                EuBLongMaxTakeProfitTicks = 890;
                EuCLongMaxTakeProfitTicks = 890;
                EuALongCandleMultiplier = 3.34;
                EuBLongCandleMultiplier = 3.34;
                EuCLongCandleMultiplier = 3.34;
                EuALongAtrPeriod = 14;
                EuBLongAtrPeriod = 14;
                EuCLongAtrPeriod = 14;
                EuALongAtrThresholdTicks = 57;
                EuBLongAtrThresholdTicks = 52;
                EuCLongAtrThresholdTicks = 57;
                EuALongLowAtrCandleMultiplier = 2.24;
                EuBLongLowAtrCandleMultiplier = 2.57;
                EuCLongLowAtrCandleMultiplier = 2.15;
                EuALongHighAtrCandleMultiplier = 5.91;
                EuBLongHighAtrCandleMultiplier = 4.15;
                EuCLongHighAtrCandleMultiplier = 2.47;
                EuALongSlExtraTicks = 26;
                EuBLongSlExtraTicks = 42;
                EuCLongSlExtraTicks = 44;
                EuALongTrailOffsetTicks = 36;
                EuBLongTrailOffsetTicks = 12;
                EuCLongTrailOffsetTicks = 13;
                EuALongTrailDelayBars = 3;
                EuBLongTrailDelayBars = 7;
                EuCLongTrailDelayBars = 3;
                EuALongMaxSlTicks = 225;
                EuBLongMaxSlTicks = 265;
                EuCLongMaxSlTicks = 167;
                EuALongMaxSlToTpRatio = 0.49;
                EuBLongMaxSlToTpRatio = 0.47;
                EuCLongMaxSlToTpRatio = 0.47;
                EuALongUsePriorCandleSl = true;
                EuBLongUsePriorCandleSl = true;
                EuCLongUsePriorCandleSl = true;
                EuALongSlAtMa = false;
                EuBLongSlAtMa = false;
                EuCLongSlAtMa = false;
                EuALongMoveSlToEntryBar = true;
                EuBLongMoveSlToEntryBar = true;
                EuCLongMoveSlToEntryBar = true;
                EuALongTrailCandleOffset = 17;
                EuBLongTrailCandleOffset = 17;
                EuCLongTrailCandleOffset = 12;
                EuALongEnableBreakeven = true;
                EuBLongEnableBreakeven = true;
                EuCLongEnableBreakeven = true;
                EuALongBreakevenTriggerTicks = 138;
                EuBLongBreakevenTriggerTicks = 166;
                EuCLongBreakevenTriggerTicks = 117;
                EuALongBreakevenCandlePct = 50.0;
                EuBLongBreakevenCandlePct = 50.0;
                EuCLongBreakevenCandlePct = 50.0;
                EuALongBreakevenOffsetTicks = 63;
                EuBLongBreakevenOffsetTicks = 17;
                EuCLongBreakevenOffsetTicks = 42;
                EuALongMaxBarsInTrade = 21;
                EuBLongMaxBarsInTrade = 18;
                EuCLongMaxBarsInTrade = 27;
                EuALongEnablePriceOffsetTrail = true;
                EuBLongEnablePriceOffsetTrail = true;
                EuCLongEnablePriceOffsetTrail = true;
                EuALongPriceOffsetReductionTicks = 98;
                EuBLongPriceOffsetReductionTicks = 92;
                EuCLongPriceOffsetReductionTicks = 51;
                EuALongEnableRMultipleTrail = true;
                EuBLongEnableRMultipleTrail = true;
                EuCLongEnableRMultipleTrail = true;
                EuALongRMultipleActivationPct = 77.0;
                EuBLongRMultipleActivationPct = 70.0;
                EuCLongRMultipleActivationPct = 79.0;
                EuALongRMultipleTrailPct = 57.0;
                EuBLongRMultipleTrailPct = 56.0;
                EuCLongRMultipleTrailPct = 59.0;
                EuALongRMultipleLockPct = 0.0;
                EuBLongRMultipleLockPct = 89.0;
                EuCLongRMultipleLockPct = 0.0;
                EuALongMAEMaxTicks = 204;
                EuBLongMAEMaxTicks = 215;
                EuCLongMAEMaxTicks = 149;
                EuALongRequireDirectionFlip = true;
                EuBLongRequireDirectionFlip = true;
                EuCLongRequireDirectionFlip = true;
                EuALongAllowSameDirectionAfterLoss = true;
                EuBLongAllowSameDirectionAfterLoss = true;
                EuCLongAllowSameDirectionAfterLoss = false;
                EuALongRequireSmaSlope = false;
                EuBLongRequireSmaSlope = false;
                EuCLongRequireSmaSlope = false;
                EuALongSmaSlopeLookback = 2;
                EuBLongSmaSlopeLookback = 1;
                EuCLongSmaSlopeLookback = 11;
                EuALongEnableWmaFilter = true;
                EuBLongEnableWmaFilter = true;
                EuCLongEnableWmaFilter = true;
                EuALongWmaPeriod = 87;
                EuBLongWmaPeriod = 56;
                EuCLongWmaPeriod = 73;
                EuALongMinBodyPct = 54.0;
                EuBLongMinBodyPct = 57.0;
                EuCLongMinBodyPct = 58.0;
                EuALongTrendConfirmBars = 10;
                EuBLongTrendConfirmBars = 14;
                EuCLongTrendConfirmBars = 16;
                EuALongEnableAdxFilter = true;
                EuBLongEnableAdxFilter = true;
                EuCLongEnableAdxFilter = true;
                EuALongAdxPeriod = 13;
                EuBLongAdxPeriod = 10;
                EuCLongAdxPeriod = 12;
                EuALongAdxMinLevel = 13;
                EuBLongAdxMinLevel = 13;
                EuCLongAdxMinLevel = 13;
                EuALongUseEmaEarlyExit = true;
                EuBLongUseEmaEarlyExit = true;
                EuCLongUseEmaEarlyExit = true;
                EuALongEmaEarlyExitPeriod = 66;
                EuBLongEmaEarlyExitPeriod = 78;
                EuCLongEmaEarlyExitPeriod = 20;
                EuALongUseAdxEarlyExit = true;
                EuBLongUseAdxEarlyExit = true;
                EuCLongUseAdxEarlyExit = true;
                EuALongAdxEarlyExitPeriod = 20;
                EuBLongAdxEarlyExitPeriod = 20;
                EuCLongAdxEarlyExitPeriod = 16;
                EuALongAdxEarlyExitMin = 15.0;
                EuBLongAdxEarlyExitMin = 15.0;
                EuCLongAdxEarlyExitMin = 15.0;
                EuALongUseAdxDrawdownExit = true;
                EuBLongUseAdxDrawdownExit = true;
                EuCLongUseAdxDrawdownExit = true;
                EuALongAdxDrawdownFromPeak = 7.0;
                EuBLongAdxDrawdownFromPeak = 6.0;
                EuCLongAdxDrawdownFromPeak = 15.0;
                EuAShortMaPeriod = 96;
                EuBShortMaPeriod = 50;
                EuCShortMaPeriod = 33;
                EuAShortEmaPeriod = 88;
                EuBShortEmaPeriod = 91;
                EuCShortEmaPeriod = 65;
                EuAShortMaType = MAMode.EMA;
                EuBShortMaType = MAMode.Both;
                EuCShortMaType = MAMode.Both;
                EuAShortMaxTakeProfitTicks = 1000;
                EuBShortMaxTakeProfitTicks = 1000;
                EuCShortMaxTakeProfitTicks = 1000;
                EuAShortCandleMultiplier = 4.22;
                EuBShortCandleMultiplier = 4.22;
                EuCShortCandleMultiplier = 4.22;
                EuAShortAtrPeriod = 14;
                EuBShortAtrPeriod = 14;
                EuCShortAtrPeriod = 14;
                EuAShortAtrThresholdTicks = 60;
                EuBShortAtrThresholdTicks = 60;
                EuCShortAtrThresholdTicks = 60;
                EuAShortLowAtrCandleMultiplier = 4.37;
                EuBShortLowAtrCandleMultiplier = 3.32;
                EuCShortLowAtrCandleMultiplier = 4.12;
                EuAShortHighAtrCandleMultiplier = 4.82;
                EuBShortHighAtrCandleMultiplier = 4.21;
                EuCShortHighAtrCandleMultiplier = 4.6;
                EuAShortSlExtraTicks = 43;
                EuBShortSlExtraTicks = 42;
                EuCShortSlExtraTicks = 67;
                EuAShortTrailOffsetTicks = 42;
                EuBShortTrailOffsetTicks = 37;
                EuCShortTrailOffsetTicks = 53;
                EuAShortTrailDelayBars = 2;
                EuBShortTrailDelayBars = 6;
                EuCShortTrailDelayBars = 1;
                EuAShortMaxSlTicks = 326;
                EuBShortMaxSlTicks = 326;
                EuCShortMaxSlTicks = 321;
                EuAShortMaxSlToTpRatio = 0.48;
                EuBShortMaxSlToTpRatio = 0.48;
                EuCShortMaxSlToTpRatio = 0.48;
                EuAShortUsePriorCandleSl = true;
                EuBShortUsePriorCandleSl = true;
                EuCShortUsePriorCandleSl = true;
                EuAShortSlAtMa = false;
                EuBShortSlAtMa = false;
                EuCShortSlAtMa = false;
                EuAShortMoveSlToEntryBar = false;
                EuBShortMoveSlToEntryBar = false;
                EuCShortMoveSlToEntryBar = false;
                EuAShortTrailCandleOffset = 8;
                EuBShortTrailCandleOffset = 14;
                EuCShortTrailCandleOffset = 8;
                EuAShortEnableBreakeven = true;
                EuBShortEnableBreakeven = true;
                EuCShortEnableBreakeven = true;
                EuAShortBreakevenTriggerTicks = 285;
                EuBShortBreakevenTriggerTicks = 257;
                EuCShortBreakevenTriggerTicks = 285;
                EuAShortBreakevenOffsetTicks = 27;
                EuBShortBreakevenOffsetTicks = 3;
                EuCShortBreakevenOffsetTicks = 31;
                EuAShortMaxBarsInTrade = 22;
                EuBShortMaxBarsInTrade = 24;
                EuCShortMaxBarsInTrade = 22;
                EuAShortEnablePriceOffsetTrail = true;
                EuBShortEnablePriceOffsetTrail = false;
                EuCShortEnablePriceOffsetTrail = false;
                EuAShortPriceOffsetReductionTicks = 30;
                EuBShortPriceOffsetReductionTicks = 0;
                EuCShortPriceOffsetReductionTicks = 0;
                EuAShortEnableRMultipleTrail = false;
                EuBShortEnableRMultipleTrail = false;
                EuCShortEnableRMultipleTrail = false;
                EuAShortRMultipleActivationPct = 77.0;
                EuBShortRMultipleActivationPct = 250.0;
                EuCShortRMultipleActivationPct = 55.0;
                EuAShortRMultipleTrailPct = 100.0;
                EuBShortRMultipleTrailPct = 140.0;
                EuCShortRMultipleTrailPct = 95.0;
                EuAShortRMultipleLockPct = 0.0;
                EuBShortRMultipleLockPct = 10.0;
                EuCShortRMultipleLockPct = 0.0;
                EuAShortMAEMaxTicks = 158;
                EuBShortMAEMaxTicks = 188;
                EuCShortMAEMaxTicks = 216;
                EuAShortRequireDirectionFlip = true;
                EuBShortRequireDirectionFlip = true;
                EuCShortRequireDirectionFlip = true;
                EuAShortAllowSameDirectionAfterLoss = true;
                EuBShortAllowSameDirectionAfterLoss = true;
                EuCShortAllowSameDirectionAfterLoss = true;
                EuAShortRequireSmaSlope = false;
                EuBShortRequireSmaSlope = false;
                EuCShortRequireSmaSlope = false;
                EuAShortSmaSlopeLookback = 3;
                EuBShortSmaSlopeLookback = 16;
                EuCShortSmaSlopeLookback = 3;
                EuAShortEnableWmaFilter = true;
                EuBShortEnableWmaFilter = true;
                EuCShortEnableWmaFilter = true;
                EuAShortWmaPeriod = 119;
                EuBShortWmaPeriod = 120;
                EuCShortWmaPeriod = 118;
                EuAShortMinBodyPct = 32.0;
                EuBShortMinBodyPct = 48.0;
                EuCShortMinBodyPct = 38.0;
                EuAShortTrendConfirmBars = 13;
                EuBShortTrendConfirmBars = 5;
                EuCShortTrendConfirmBars = 10;
                EuAShortEnableAdxFilter = true;
                EuBShortEnableAdxFilter = true;
                EuCShortEnableAdxFilter = true;
                EuAShortAdxPeriod = 5;
                EuBShortAdxPeriod = 5;
                EuCShortAdxPeriod = 5;
                EuAShortAdxMinLevel = 24;
                EuBShortAdxMinLevel = 22;
                EuCShortAdxMinLevel = 21;
                EuAShortUseEmaEarlyExit = true;
                EuBShortUseEmaEarlyExit = false;
                EuCShortUseEmaEarlyExit = false;
                EuAShortEmaEarlyExitPeriod = 63;
                EuBShortEmaEarlyExitPeriod = 50;
                EuCShortEmaEarlyExitPeriod = 50;
                EuAShortUseAdxEarlyExit = true;
                EuBShortUseAdxEarlyExit = true;
                EuCShortUseAdxEarlyExit = true;
                EuAShortAdxEarlyExitPeriod = 13;
                EuBShortAdxEarlyExitPeriod = 25;
                EuCShortAdxEarlyExitPeriod = 13;
                EuAShortAdxEarlyExitMin = 11.0;
                EuBShortAdxEarlyExitMin = 6.0;
                EuCShortAdxEarlyExitMin = 11.0;
                EuAShortUseAdxDrawdownExit = true;
                EuBShortUseAdxDrawdownExit = true;
                EuCShortUseAdxDrawdownExit = true;
                EuAShortAdxDrawdownFromPeak = 10.0;
                EuBShortAdxDrawdownFromPeak = 14.0;
                EuCShortAdxDrawdownFromPeak = 7.0;

                // ════════════════════════════════════════
                //  ASIA SESSION DEFAULTS
                // ════════════════════════════════════════
                AsEnable                                       = true;
                AsAMaxTradesPerSession = 2;
                AsBMaxTradesPerSession = 2;
                AsCMaxTradesPerSession = 2;
                AsAMaxLossesPerSession = 1;
                AsBMaxLossesPerSession = 1;
                AsCMaxLossesPerSession = 1;
                AsAMaxSessionProfitTicks = 500;
                AsBMaxSessionProfitTicks = 500;
                AsCMaxSessionProfitTicks = 500;
                AsAMaxSessionLossTicks = 200;
                AsBMaxSessionLossTicks = 200;
                AsCMaxSessionLossTicks = 200;
                AsAContracts = 1;
                AsBContracts = 1;
                AsCContracts = 1;
                AsAEnableLongTrades = true;
                AsBEnableLongTrades = true;
                AsCEnableLongTrades = true;
                AsAEnableShortTrades = true;
                AsBEnableShortTrades = true;
                AsCEnableShortTrades = true;
                AsALongMaPeriod = 66;
                AsBLongMaPeriod = 64;
                AsCLongMaPeriod = 56;
                AsALongEmaPeriod = 23;
                AsBLongEmaPeriod = 34;
                AsCLongEmaPeriod = 11;
                AsALongMaType = MAMode.Both;
                AsBLongMaType = MAMode.Both;
                AsCLongMaType = MAMode.Both;
                AsALongMaxTakeProfitTicks = 600;
                AsBLongMaxTakeProfitTicks = 600;
                AsCLongMaxTakeProfitTicks = 600;
                AsALongCandleMultiplier = 2.65;
                AsBLongCandleMultiplier = 2.65;
                AsCLongCandleMultiplier = 2.65;
                AsALongAtrPeriod = 14;
                AsBLongAtrPeriod = 14;
                AsCLongAtrPeriod = 14;
                AsALongAtrThresholdTicks = 50;
                AsBLongAtrThresholdTicks = 50;
                AsCLongAtrThresholdTicks = 50;
                AsALongLowAtrCandleMultiplier = 2.6;
                AsBLongLowAtrCandleMultiplier = 2.47;
                AsCLongLowAtrCandleMultiplier = 1.97;
                AsALongHighAtrCandleMultiplier = 4.44;
                AsBLongHighAtrCandleMultiplier = 2.1;
                AsCLongHighAtrCandleMultiplier = 1.67;
                AsALongSlExtraTicks = 40;
                AsBLongSlExtraTicks = 41;
                AsCLongSlExtraTicks = 64;
                AsALongTrailOffsetTicks = 0;
                AsBLongTrailOffsetTicks = 0;
                AsCLongTrailOffsetTicks = 0;
                AsALongTrailDelayBars = 2;
                AsBLongTrailDelayBars = 2;
                AsCLongTrailDelayBars = 1;
                AsALongMaxSlTicks = 277;
                AsBLongMaxSlTicks = 220;
                AsCLongMaxSlTicks = 155;
                AsALongMaxSlToTpRatio = 0.51;
                AsBLongMaxSlToTpRatio = 0.5;
                AsCLongMaxSlToTpRatio = 0.37;
                AsALongUsePriorCandleSl = false;
                AsBLongUsePriorCandleSl = true;
                AsCLongUsePriorCandleSl = true;
                AsALongSlAtMa = true;
                AsBLongSlAtMa = true;
                AsCLongSlAtMa = true;
                AsALongMoveSlToEntryBar = false;
                AsBLongMoveSlToEntryBar = true;
                AsCLongMoveSlToEntryBar = true;
                AsALongTrailCandleOffset = 17;
                AsBLongTrailCandleOffset = 15;
                AsCLongTrailCandleOffset = 1;
                AsALongEnableBreakeven = true;
                AsBLongEnableBreakeven = true;
                AsCLongEnableBreakeven = true;
                AsALongBreakevenTriggerTicks = 103;
                AsBLongBreakevenTriggerTicks = 91;
                AsCLongBreakevenTriggerTicks = 72;
                AsALongBreakevenCandlePct = 63.0;
                AsBLongBreakevenCandlePct = 63.0;
                AsCLongBreakevenCandlePct = 63.0;
                AsALongBreakevenOffsetTicks = 43;
                AsBLongBreakevenOffsetTicks = 34;
                AsCLongBreakevenOffsetTicks = 70;
                AsALongMaxBarsInTrade = 29;
                AsBLongMaxBarsInTrade = 27;
                AsCLongMaxBarsInTrade = 30;
                AsALongEnablePriceOffsetTrail = true;
                AsBLongEnablePriceOffsetTrail = false;
                AsCLongEnablePriceOffsetTrail = true;
                AsALongPriceOffsetReductionTicks = 48;
                AsBLongPriceOffsetReductionTicks = 6;
                AsCLongPriceOffsetReductionTicks = 12;
                AsALongEnableRMultipleTrail = true;
                AsBLongEnableRMultipleTrail = true;
                AsCLongEnableRMultipleTrail = true;
                AsALongRMultipleActivationPct = 39.0;
                AsBLongRMultipleActivationPct = 49.0;
                AsCLongRMultipleActivationPct = 16.0;
                AsALongRMultipleTrailPct = 58.0;
                AsBLongRMultipleTrailPct = 55.0;
                AsCLongRMultipleTrailPct = 75.0;
                AsALongRMultipleLockPct = 0.0;
                AsBLongRMultipleLockPct = 0.0;
                AsCLongRMultipleLockPct = 0.0;
                AsALongMAEMaxTicks = 0;
                AsBLongMAEMaxTicks = 0;
                AsCLongMAEMaxTicks = 0;
                AsALongRequireDirectionFlip = true;
                AsBLongRequireDirectionFlip = true;
                AsCLongRequireDirectionFlip = true;
                AsALongAllowSameDirectionAfterLoss = true;
                AsBLongAllowSameDirectionAfterLoss = true;
                AsCLongAllowSameDirectionAfterLoss = true;
                AsALongRequireSmaSlope = true;
                AsBLongRequireSmaSlope = false;
                AsCLongRequireSmaSlope = true;
                AsALongSmaSlopeLookback = 1;
                AsBLongSmaSlopeLookback = 2;
                AsCLongSmaSlopeLookback = 3;
                AsALongEnableWmaFilter = true;
                AsBLongEnableWmaFilter = true;
                AsCLongEnableWmaFilter = true;
                AsALongWmaPeriod = 22;
                AsBLongWmaPeriod = 37;
                AsCLongWmaPeriod = 42;
                AsALongMinBodyPct = 51.0;
                AsBLongMinBodyPct = 46.0;
                AsCLongMinBodyPct = 35.0;
                AsALongTrendConfirmBars = 5;
                AsBLongTrendConfirmBars = 4;
                AsCLongTrendConfirmBars = 6;
                AsALongEnableAdxFilter = true;
                AsBLongEnableAdxFilter = true;
                AsCLongEnableAdxFilter = true;
                AsALongAdxPeriod = 16;
                AsBLongAdxPeriod = 12;
                AsCLongAdxPeriod = 16;
                AsALongAdxMinLevel = 16;
                AsBLongAdxMinLevel = 19;
                AsCLongAdxMinLevel = 15;
                AsALongUseEmaEarlyExit = true;
                AsBLongUseEmaEarlyExit = true;
                AsCLongUseEmaEarlyExit = true;
                AsALongEmaEarlyExitPeriod = 21;
                AsBLongEmaEarlyExitPeriod = 27;
                AsCLongEmaEarlyExitPeriod = 24;
                AsALongUseAdxEarlyExit = true;
                AsBLongUseAdxEarlyExit = true;
                AsCLongUseAdxEarlyExit = true;
                AsALongAdxEarlyExitPeriod = 17;
                AsBLongAdxEarlyExitPeriod = 16;
                AsCLongAdxEarlyExitPeriod = 17;
                AsALongAdxEarlyExitMin = 19.0;
                AsBLongAdxEarlyExitMin = 19.0;
                AsCLongAdxEarlyExitMin = 19.0;
                AsALongUseAdxDrawdownExit = true;
                AsBLongUseAdxDrawdownExit = false;
                AsCLongUseAdxDrawdownExit = true;
                AsALongAdxDrawdownFromPeak = 12.0;
                AsBLongAdxDrawdownFromPeak = 10.0;
                AsCLongAdxDrawdownFromPeak = 12.0;
                AsAShortMaPeriod = 47;
                AsBShortMaPeriod = 50;
                AsCShortMaPeriod = 49;
                AsAShortEmaPeriod = 90;
                AsBShortEmaPeriod = 85;
                AsCShortEmaPeriod = 95;
                AsAShortMaType = MAMode.Both;
                AsBShortMaType = MAMode.Both;
                AsCShortMaType = MAMode.Both;
                AsAShortMaxTakeProfitTicks = 1000;
                AsBShortMaxTakeProfitTicks = 1000;
                AsCShortMaxTakeProfitTicks = 1000;
                AsAShortCandleMultiplier = 4.43;
                AsBShortCandleMultiplier = 4.43;
                AsCShortCandleMultiplier = 4.43;
                AsAShortAtrPeriod = 14;
                AsBShortAtrPeriod = 14;
                AsCShortAtrPeriod = 14;
                AsAShortAtrThresholdTicks = 47;
                AsBShortAtrThresholdTicks = 55;
                AsCShortAtrThresholdTicks = 59;
                AsAShortLowAtrCandleMultiplier = 5.2;
                AsBShortLowAtrCandleMultiplier = 5.0;
                AsCShortLowAtrCandleMultiplier = 4.38;
                AsAShortHighAtrCandleMultiplier = 4.47;
                AsBShortHighAtrCandleMultiplier = 5.1;
                AsCShortHighAtrCandleMultiplier = 4.21;
                AsAShortSlExtraTicks = 38;
                AsBShortSlExtraTicks = 42;
                AsCShortSlExtraTicks = 49;
                AsAShortTrailOffsetTicks = 5;
                AsBShortTrailOffsetTicks = 17;
                AsCShortTrailOffsetTicks = 2;
                AsAShortTrailDelayBars = 22;
                AsBShortTrailDelayBars = 15;
                AsCShortTrailDelayBars = 20;
                AsAShortMaxSlTicks = 183;
                AsBShortMaxSlTicks = 160;
                AsCShortMaxSlTicks = 150;
                AsAShortMaxSlToTpRatio = 0.43;
                AsBShortMaxSlToTpRatio = 0.44;
                AsCShortMaxSlToTpRatio = 0.48;
                AsAShortUsePriorCandleSl = true;
                AsBShortUsePriorCandleSl = true;
                AsCShortUsePriorCandleSl = true;
                AsAShortSlAtMa = true;
                AsBShortSlAtMa = true;
                AsCShortSlAtMa = true;
                AsAShortMoveSlToEntryBar = true;
                AsBShortMoveSlToEntryBar = true;
                AsCShortMoveSlToEntryBar = true;
                AsAShortTrailCandleOffset = 2;
                AsBShortTrailCandleOffset = 2;
                AsCShortTrailCandleOffset = 2;
                AsAShortEnableBreakeven = true;
                AsBShortEnableBreakeven = true;
                AsCShortEnableBreakeven = false;
                AsAShortBreakevenTriggerTicks = 143;
                AsBShortBreakevenTriggerTicks = 134;
                AsCShortBreakevenTriggerTicks = 125;
                AsAShortBreakevenOffsetTicks = 23;
                AsBShortBreakevenOffsetTicks = 34;
                AsCShortBreakevenOffsetTicks = 15;
                AsAShortMaxBarsInTrade = 42;
                AsBShortMaxBarsInTrade = 28;
                AsCShortMaxBarsInTrade = 20;
                AsAShortEnablePriceOffsetTrail = true;
                AsBShortEnablePriceOffsetTrail = true;
                AsCShortEnablePriceOffsetTrail = true;
                AsAShortPriceOffsetReductionTicks = 38;
                AsBShortPriceOffsetReductionTicks = 49;
                AsCShortPriceOffsetReductionTicks = 37;
                AsAShortEnableRMultipleTrail = true;
                AsBShortEnableRMultipleTrail = false;
                AsCShortEnableRMultipleTrail = true;
                AsAShortRMultipleActivationPct = 104.0;
                AsBShortRMultipleActivationPct = 106.0;
                AsCShortRMultipleActivationPct = 120.0;
                AsAShortRMultipleTrailPct = 33.0;
                AsBShortRMultipleTrailPct = 10.0;
                AsCShortRMultipleTrailPct = 31.0;
                AsAShortRMultipleLockPct = 0.0;
                AsBShortRMultipleLockPct = 0.0;
                AsCShortRMultipleLockPct = 0.0;
                AsAShortMAEMaxTicks = 0;
                AsBShortMAEMaxTicks = 0;
                AsCShortMAEMaxTicks = 0;
                AsAShortRequireDirectionFlip = false;
                AsBShortRequireDirectionFlip = false;
                AsCShortRequireDirectionFlip = true;
                AsAShortAllowSameDirectionAfterLoss = true;
                AsBShortAllowSameDirectionAfterLoss = true;
                AsCShortAllowSameDirectionAfterLoss = true;
                AsAShortRequireSmaSlope = false;
                AsBShortRequireSmaSlope = false;
                AsCShortRequireSmaSlope = false;
                AsAShortSmaSlopeLookback = 5;
                AsBShortSmaSlopeLookback = 3;
                AsCShortSmaSlopeLookback = 3;
                AsAShortEnableWmaFilter = true;
                AsBShortEnableWmaFilter = true;
                AsCShortEnableWmaFilter = true;
                AsAShortWmaPeriod = 16;
                AsBShortWmaPeriod = 26;
                AsCShortWmaPeriod = 41;
                AsAShortMinBodyPct = 42.0;
                AsBShortMinBodyPct = 37.0;
                AsCShortMinBodyPct = 44.0;
                AsAShortTrendConfirmBars = 19;
                AsBShortTrendConfirmBars = 18;
                AsCShortTrendConfirmBars = 19;
                AsAShortEnableAdxFilter = true;
                AsBShortEnableAdxFilter = true;
                AsCShortEnableAdxFilter = true;
                AsAShortAdxPeriod = 5;
                AsBShortAdxPeriod = 5;
                AsCShortAdxPeriod = 5;
                AsAShortAdxMinLevel = 20;
                AsBShortAdxMinLevel = 23;
                AsCShortAdxMinLevel = 23;
                AsAShortUseEmaEarlyExit = false;
                AsBShortUseEmaEarlyExit = false;
                AsCShortUseEmaEarlyExit = false;
                AsAShortEmaEarlyExitPeriod = 11;
                AsBShortEmaEarlyExitPeriod = 11;
                AsCShortEmaEarlyExitPeriod = 11;
                AsAShortUseAdxEarlyExit = true;
                AsBShortUseAdxEarlyExit = true;
                AsCShortUseAdxEarlyExit = true;
                AsAShortAdxEarlyExitPeriod = 14;
                AsBShortAdxEarlyExitPeriod = 13;
                AsCShortAdxEarlyExitPeriod = 15;
                AsAShortAdxEarlyExitMin = 16.0;
                AsBShortAdxEarlyExitMin = 16.0;
                AsCShortAdxEarlyExitMin = 15.0;
                AsAShortUseAdxDrawdownExit = true;
                AsBShortUseAdxDrawdownExit = true;
                AsCShortUseAdxDrawdownExit = true;
                AsAShortAdxDrawdownFromPeak = 6.0;
                AsBShortAdxDrawdownFromPeak = 6.0;
                AsCShortAdxDrawdownFromPeak = 7.0;
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                if (NyEnable && NyAEnable)
                {
                // ── NY-A indicators ──
                nyALongSmaHigh                             = SMA(High,  NyALongMaPeriod);
                nyALongSmaLow                              = SMA(Low,   NyALongMaPeriod);
                nyALongEmaHigh                             = EMA(High,  NyALongEmaPeriod);
                nyALongEmaLow                              = EMA(Low,   NyALongEmaPeriod);
                nyALongWmaFilter                           = WMA(Close, NyALongWmaPeriod);
                nyALongAdxIndicator                        = ADX(NyALongAdxPeriod);
                nyALongSwing                               = Swing(1);
                nyALongEarlyExitEma                        = EMA(NyALongEmaEarlyExitPeriod);
                nyALongEarlyExitAdx                        = ADX(NyALongAdxEarlyExitPeriod);
                nyALongAtr                                 = ATR(NyALongAtrPeriod);
                nyAShortSmaHigh                            = SMA(High,  NyAShortMaPeriod);
                nyAShortSmaLow                             = SMA(Low,   NyAShortMaPeriod);
                nyAShortEmaHigh                            = EMA(High,  NyAShortEmaPeriod);
                nyAShortEmaLow                             = EMA(Low,   NyAShortEmaPeriod);
                nyAShortWmaFilter                          = WMA(Close, NyAShortWmaPeriod);
                nyAShortAdxIndicator                       = ADX(NyAShortAdxPeriod);
                nyAShortSwing                              = Swing(1);
                nyAShortEarlyExitEma                       = EMA(NyAShortEmaEarlyExitPeriod);
                nyAShortEarlyExitAdx                       = ADX(NyAShortAdxEarlyExitPeriod);
                nyAShortAtr                                = ATR(NyAShortAtrPeriod);
                }

                if (NyEnable && NyBEnable)
                {
                // ── NY-B indicators ──
                nyBLongSmaHigh                             = SMA(High,  NyBLongMaPeriod);
                nyBLongSmaLow                              = SMA(Low,   NyBLongMaPeriod);
                nyBLongEmaHigh                             = EMA(High,  NyBLongEmaPeriod);
                nyBLongEmaLow                              = EMA(Low,   NyBLongEmaPeriod);
                nyBLongWmaFilter                           = WMA(Close, NyBLongWmaPeriod);
                nyBLongAdxIndicator                        = ADX(NyBLongAdxPeriod);
                nyBLongSwing                               = Swing(1);
                nyBLongEarlyExitEma                        = EMA(NyBLongEmaEarlyExitPeriod);
                nyBLongEarlyExitAdx                        = ADX(NyBLongAdxEarlyExitPeriod);
                nyBLongAtr                                 = ATR(NyBLongAtrPeriod);
                nyBShortSmaHigh                            = SMA(High,  NyBShortMaPeriod);
                nyBShortSmaLow                             = SMA(Low,   NyBShortMaPeriod);
                nyBShortEmaHigh                            = EMA(High,  NyBShortEmaPeriod);
                nyBShortEmaLow                             = EMA(Low,   NyBShortEmaPeriod);
                nyBShortWmaFilter                          = WMA(Close, NyBShortWmaPeriod);
                nyBShortAdxIndicator                       = ADX(NyBShortAdxPeriod);
                nyBShortSwing                              = Swing(1);
                nyBShortEarlyExitEma                       = EMA(NyBShortEmaEarlyExitPeriod);
                nyBShortEarlyExitAdx                       = ADX(NyBShortAdxEarlyExitPeriod);
                nyBShortAtr                                = ATR(NyBShortAtrPeriod);
                }

                if (NyEnable && NyCEnable)
                {
                // ── NY-C indicators ──
                nyCLongSmaHigh                             = SMA(High,  NyCLongMaPeriod);
                nyCLongSmaLow                              = SMA(Low,   NyCLongMaPeriod);
                nyCLongEmaHigh                             = EMA(High,  NyCLongEmaPeriod);
                nyCLongEmaLow                              = EMA(Low,   NyCLongEmaPeriod);
                nyCLongWmaFilter                           = WMA(Close, NyCLongWmaPeriod);
                nyCLongAdxIndicator                        = ADX(NyCLongAdxPeriod);
                nyCLongSwing                               = Swing(1);
                nyCLongEarlyExitEma                        = EMA(NyCLongEmaEarlyExitPeriod);
                nyCLongEarlyExitAdx                        = ADX(NyCLongAdxEarlyExitPeriod);
                nyCLongAtr                                 = ATR(NyCLongAtrPeriod);
                nyCShortSmaHigh                            = SMA(High,  NyCShortMaPeriod);
                nyCShortSmaLow                             = SMA(Low,   NyCShortMaPeriod);
                nyCShortEmaHigh                            = EMA(High,  NyCShortEmaPeriod);
                nyCShortEmaLow                             = EMA(Low,   NyCShortEmaPeriod);
                nyCShortWmaFilter                          = WMA(Close, NyCShortWmaPeriod);
                nyCShortAdxIndicator                       = ADX(NyCShortAdxPeriod);
                nyCShortSwing                              = Swing(1);
                nyCShortEarlyExitEma                       = EMA(NyCShortEmaEarlyExitPeriod);
                nyCShortEarlyExitAdx                       = ADX(NyCShortAdxEarlyExitPeriod);
                nyCShortAtr                                = ATR(NyCShortAtrPeriod);
                }

                if (EuEnable && EuAEnable)
                {
                // ── EU-A indicators ──
                euALongSmaHigh                             = SMA(High,  EuALongMaPeriod);
                euALongSmaLow                              = SMA(Low,   EuALongMaPeriod);
                euALongEmaHigh                             = EMA(High,  EuALongEmaPeriod);
                euALongEmaLow                              = EMA(Low,   EuALongEmaPeriod);
                euALongWmaFilter                           = WMA(Close, EuALongWmaPeriod);
                euALongAdxIndicator                        = ADX(EuALongAdxPeriod);
                euALongSwing                               = Swing(1);
                euALongEarlyExitEma                        = EMA(EuALongEmaEarlyExitPeriod);
                euALongEarlyExitAdx                        = ADX(EuALongAdxEarlyExitPeriod);
                euALongAtr                                 = ATR(EuALongAtrPeriod);
                euAShortSmaHigh                            = SMA(High,  EuAShortMaPeriod);
                euAShortSmaLow                             = SMA(Low,   EuAShortMaPeriod);
                euAShortEmaHigh                            = EMA(High,  EuAShortEmaPeriod);
                euAShortEmaLow                             = EMA(Low,   EuAShortEmaPeriod);
                euAShortWmaFilter                          = WMA(Close, EuAShortWmaPeriod);
                euAShortAdxIndicator                       = ADX(EuAShortAdxPeriod);
                euAShortSwing                              = Swing(1);
                euAShortEarlyExitEma                       = EMA(EuAShortEmaEarlyExitPeriod);
                euAShortEarlyExitAdx                       = ADX(EuAShortAdxEarlyExitPeriod);
                euAShortAtr                                = ATR(EuAShortAtrPeriod);
                }

                if (EuEnable && EuBEnable)
                {
                // ── EU-B indicators ──
                euBLongSmaHigh                             = SMA(High,  EuBLongMaPeriod);
                euBLongSmaLow                              = SMA(Low,   EuBLongMaPeriod);
                euBLongEmaHigh                             = EMA(High,  EuBLongEmaPeriod);
                euBLongEmaLow                              = EMA(Low,   EuBLongEmaPeriod);
                euBLongWmaFilter                           = WMA(Close, EuBLongWmaPeriod);
                euBLongAdxIndicator                        = ADX(EuBLongAdxPeriod);
                euBLongSwing                               = Swing(1);
                euBLongEarlyExitEma                        = EMA(EuBLongEmaEarlyExitPeriod);
                euBLongEarlyExitAdx                        = ADX(EuBLongAdxEarlyExitPeriod);
                euBLongAtr                                 = ATR(EuBLongAtrPeriod);
                euBShortSmaHigh                            = SMA(High,  EuBShortMaPeriod);
                euBShortSmaLow                             = SMA(Low,   EuBShortMaPeriod);
                euBShortEmaHigh                            = EMA(High,  EuBShortEmaPeriod);
                euBShortEmaLow                             = EMA(Low,   EuBShortEmaPeriod);
                euBShortWmaFilter                          = WMA(Close, EuBShortWmaPeriod);
                euBShortAdxIndicator                       = ADX(EuBShortAdxPeriod);
                euBShortSwing                              = Swing(1);
                euBShortEarlyExitEma                       = EMA(EuBShortEmaEarlyExitPeriod);
                euBShortEarlyExitAdx                       = ADX(EuBShortAdxEarlyExitPeriod);
                euBShortAtr                                = ATR(EuBShortAtrPeriod);
                }

                if (EuEnable && EuCEnable)
                {
                // ── EU-C indicators ──
                euCLongSmaHigh                             = SMA(High,  EuCLongMaPeriod);
                euCLongSmaLow                              = SMA(Low,   EuCLongMaPeriod);
                euCLongEmaHigh                             = EMA(High,  EuCLongEmaPeriod);
                euCLongEmaLow                              = EMA(Low,   EuCLongEmaPeriod);
                euCLongWmaFilter                           = WMA(Close, EuCLongWmaPeriod);
                euCLongAdxIndicator                        = ADX(EuCLongAdxPeriod);
                euCLongSwing                               = Swing(1);
                euCLongEarlyExitEma                        = EMA(EuCLongEmaEarlyExitPeriod);
                euCLongEarlyExitAdx                        = ADX(EuCLongAdxEarlyExitPeriod);
                euCLongAtr                                 = ATR(EuCLongAtrPeriod);
                euCShortSmaHigh                            = SMA(High,  EuCShortMaPeriod);
                euCShortSmaLow                             = SMA(Low,   EuCShortMaPeriod);
                euCShortEmaHigh                            = EMA(High,  EuCShortEmaPeriod);
                euCShortEmaLow                             = EMA(Low,   EuCShortEmaPeriod);
                euCShortWmaFilter                          = WMA(Close, EuCShortWmaPeriod);
                euCShortAdxIndicator                       = ADX(EuCShortAdxPeriod);
                euCShortSwing                              = Swing(1);
                euCShortEarlyExitEma                       = EMA(EuCShortEmaEarlyExitPeriod);
                euCShortEarlyExitAdx                       = ADX(EuCShortAdxEarlyExitPeriod);
                euCShortAtr                                = ATR(EuCShortAtrPeriod);
                }

                if (AsEnable && AsAEnable)
                {
                // ── AS-A indicators ──
                asALongSmaHigh                             = SMA(High,  AsALongMaPeriod);
                asALongSmaLow                              = SMA(Low,   AsALongMaPeriod);
                asALongEmaHigh                             = EMA(High,  AsALongEmaPeriod);
                asALongEmaLow                              = EMA(Low,   AsALongEmaPeriod);
                asALongWmaFilter                           = WMA(Close, AsALongWmaPeriod);
                asALongAdxIndicator                        = ADX(AsALongAdxPeriod);
                asALongSwing                               = Swing(1);
                asALongEarlyExitEma                        = EMA(AsALongEmaEarlyExitPeriod);
                asALongEarlyExitAdx                        = ADX(AsALongAdxEarlyExitPeriod);
                asALongAtr                                 = ATR(AsALongAtrPeriod);
                asAShortSmaHigh                            = SMA(High,  AsAShortMaPeriod);
                asAShortSmaLow                             = SMA(Low,   AsAShortMaPeriod);
                asAShortEmaHigh                            = EMA(High,  AsAShortEmaPeriod);
                asAShortEmaLow                             = EMA(Low,   AsAShortEmaPeriod);
                asAShortWmaFilter                          = WMA(Close, AsAShortWmaPeriod);
                asAShortAdxIndicator                       = ADX(AsAShortAdxPeriod);
                asAShortSwing                              = Swing(1);
                asAShortEarlyExitEma                       = EMA(AsAShortEmaEarlyExitPeriod);
                asAShortEarlyExitAdx                       = ADX(AsAShortAdxEarlyExitPeriod);
                asAShortAtr                                = ATR(AsAShortAtrPeriod);
                }

                if (AsEnable && AsBEnable)
                {
                // ── AS-B indicators ──
                asBLongSmaHigh                             = SMA(High,  AsBLongMaPeriod);
                asBLongSmaLow                              = SMA(Low,   AsBLongMaPeriod);
                asBLongEmaHigh                             = EMA(High,  AsBLongEmaPeriod);
                asBLongEmaLow                              = EMA(Low,   AsBLongEmaPeriod);
                asBLongWmaFilter                           = WMA(Close, AsBLongWmaPeriod);
                asBLongAdxIndicator                        = ADX(AsBLongAdxPeriod);
                asBLongSwing                               = Swing(1);
                asBLongEarlyExitEma                        = EMA(AsBLongEmaEarlyExitPeriod);
                asBLongEarlyExitAdx                        = ADX(AsBLongAdxEarlyExitPeriod);
                asBLongAtr                                 = ATR(AsBLongAtrPeriod);
                asBShortSmaHigh                            = SMA(High,  AsBShortMaPeriod);
                asBShortSmaLow                             = SMA(Low,   AsBShortMaPeriod);
                asBShortEmaHigh                            = EMA(High,  AsBShortEmaPeriod);
                asBShortEmaLow                             = EMA(Low,   AsBShortEmaPeriod);
                asBShortWmaFilter                          = WMA(Close, AsBShortWmaPeriod);
                asBShortAdxIndicator                       = ADX(AsBShortAdxPeriod);
                asBShortSwing                              = Swing(1);
                asBShortEarlyExitEma                       = EMA(AsBShortEmaEarlyExitPeriod);
                asBShortEarlyExitAdx                       = ADX(AsBShortAdxEarlyExitPeriod);
                asBShortAtr                                = ATR(AsBShortAtrPeriod);
                }

                if (AsEnable && AsCEnable)
                {
                // ── AS-C indicators ──
                asCLongSmaHigh                             = SMA(High,  AsCLongMaPeriod);
                asCLongSmaLow                              = SMA(Low,   AsCLongMaPeriod);
                asCLongEmaHigh                             = EMA(High,  AsCLongEmaPeriod);
                asCLongEmaLow                              = EMA(Low,   AsCLongEmaPeriod);
                asCLongWmaFilter                           = WMA(Close, AsCLongWmaPeriod);
                asCLongAdxIndicator                        = ADX(AsCLongAdxPeriod);
                asCLongSwing                               = Swing(1);
                asCLongEarlyExitEma                        = EMA(AsCLongEmaEarlyExitPeriod);
                asCLongEarlyExitAdx                        = ADX(AsCLongAdxEarlyExitPeriod);
                asCLongAtr                                 = ATR(AsCLongAtrPeriod);
                asCShortSmaHigh                            = SMA(High,  AsCShortMaPeriod);
                asCShortSmaLow                             = SMA(Low,   AsCShortMaPeriod);
                asCShortEmaHigh                            = EMA(High,  AsCShortEmaPeriod);
                asCShortEmaLow                             = EMA(Low,   AsCShortEmaPeriod);
                asCShortWmaFilter                          = WMA(Close, AsCShortWmaPeriod);
                asCShortAdxIndicator                       = ADX(AsCShortAdxPeriod);
                asCShortSwing                              = Swing(1);
                asCShortEarlyExitEma                       = EMA(AsCShortEmaEarlyExitPeriod);
                asCShortEarlyExitAdx                       = ADX(AsCShortAdxEarlyExitPeriod);
                asCShortAtr                                = ATR(AsCShortAtrPeriod);
                }


                // Chart display — NY-A Long MAs (primary/first active NY sub-session)
                if (NyEnable && NyAEnable)
                {
                    if (NyALongMaType == MAMode.SMA || NyALongMaType == MAMode.Both)
                    {
                        nyALongSmaHigh.Plots[0].Brush = Brushes.RoyalBlue;
                        nyALongSmaLow.Plots[0].Brush  = Brushes.MediumOrchid;
                        AddChartIndicator(nyALongSmaHigh);
                        AddChartIndicator(nyALongSmaLow);
                    }
                    if (NyALongMaType == MAMode.EMA || NyALongMaType == MAMode.Both)
                    {
                        nyALongEmaHigh.Plots[0].Brush = Brushes.DodgerBlue;
                        nyALongEmaLow.Plots[0].Brush  = Brushes.Orchid;
                        AddChartIndicator(nyALongEmaHigh);
                        AddChartIndicator(nyALongEmaLow);
                    }
                    if (NyALongEnableWmaFilter)
                    {
                        nyALongWmaFilter.Plots[0].Brush = Brushes.Gold;
                        AddChartIndicator(nyALongWmaFilter);
                    }
                }

                EnsureNewsDatesInitialized();
                ValidateRequiredPrimaryTimeframe(5);
                ValidateRequiredPrimaryInstrument();
                heartbeatReporter = new StrategyHeartbeatReporter(
                    HeartbeatStrategyName,
                    System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv"));
                projectXSessionToken = null;
                projectXTokenAcquiredUtc = Core.Globals.MinDate;
                projectXAccounts = null;
                projectXLastOrderIds.Clear();
                projectXResolvedContractId = null;
                projectXResolvedInstrumentKey = string.Empty;
                suppressProjectXNextExecutionExitWebhook = false;
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
            }
            else if (State == State.Transition)
            {
                ResetAll();
            }
            else if (State == State.Realtime)
            {
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
                projectXSessionToken = null;
                projectXTokenAcquiredUtc = Core.Globals.MinDate;
                projectXAccounts = null;
                projectXLastOrderIds.Clear();
                projectXResolvedContractId = null;
                projectXResolvedInstrumentKey = string.Empty;
                suppressProjectXNextExecutionExitWebhook = false;
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
                DisposeInfoBoxOverlay();
            }
        }


        // ════════════════════════════════════════════════════════
        //  SESSION DISPATCH HELPERS  (v1 — 9 sub-sessions, FULL INDEPENDENCE)
        // ════════════════════════════════════════════════════════

        // Map sub-session id to parent-group id (1=NY, 2=EU, 3=AS). Used for info-box/label grouping only.
        private int ParentGroupOf(int sid)
        {
            if (sid >= 1 && sid <= 3) return 1;
            if (sid >= 4 && sid <= 6) return 2;
            if (sid >= 7 && sid <= 9) return 3;
            return 0;
        }

        // -- Indicator access by sub-session id (1..9) --
        private SMA S_LongSmaHigh(int sid)
        {
            switch (sid)
            {
                case 1: return nyALongSmaHigh;
                case 2: return nyBLongSmaHigh;
                case 3: return nyCLongSmaHigh;
                case 4: return euALongSmaHigh;
                case 5: return euBLongSmaHigh;
                case 6: return euCLongSmaHigh;
                case 7: return asALongSmaHigh;
                case 8: return asBLongSmaHigh;
                case 9: return asCLongSmaHigh;
            }
            return null;
        }

        private SMA S_LongSmaLow(int sid)
        {
            switch (sid)
            {
                case 1: return nyALongSmaLow;
                case 2: return nyBLongSmaLow;
                case 3: return nyCLongSmaLow;
                case 4: return euALongSmaLow;
                case 5: return euBLongSmaLow;
                case 6: return euCLongSmaLow;
                case 7: return asALongSmaLow;
                case 8: return asBLongSmaLow;
                case 9: return asCLongSmaLow;
            }
            return null;
        }

        private EMA S_LongEmaHigh(int sid)
        {
            switch (sid)
            {
                case 1: return nyALongEmaHigh;
                case 2: return nyBLongEmaHigh;
                case 3: return nyCLongEmaHigh;
                case 4: return euALongEmaHigh;
                case 5: return euBLongEmaHigh;
                case 6: return euCLongEmaHigh;
                case 7: return asALongEmaHigh;
                case 8: return asBLongEmaHigh;
                case 9: return asCLongEmaHigh;
            }
            return null;
        }

        private EMA S_LongEmaLow(int sid)
        {
            switch (sid)
            {
                case 1: return nyALongEmaLow;
                case 2: return nyBLongEmaLow;
                case 3: return nyCLongEmaLow;
                case 4: return euALongEmaLow;
                case 5: return euBLongEmaLow;
                case 6: return euCLongEmaLow;
                case 7: return asALongEmaLow;
                case 8: return asBLongEmaLow;
                case 9: return asCLongEmaLow;
            }
            return null;
        }

        private WMA S_LongWmaFilter(int sid)
        {
            switch (sid)
            {
                case 1: return nyALongWmaFilter;
                case 2: return nyBLongWmaFilter;
                case 3: return nyCLongWmaFilter;
                case 4: return euALongWmaFilter;
                case 5: return euBLongWmaFilter;
                case 6: return euCLongWmaFilter;
                case 7: return asALongWmaFilter;
                case 8: return asBLongWmaFilter;
                case 9: return asCLongWmaFilter;
            }
            return null;
        }

        private ADX S_LongAdxIndicator(int sid)
        {
            switch (sid)
            {
                case 1: return nyALongAdxIndicator;
                case 2: return nyBLongAdxIndicator;
                case 3: return nyCLongAdxIndicator;
                case 4: return euALongAdxIndicator;
                case 5: return euBLongAdxIndicator;
                case 6: return euCLongAdxIndicator;
                case 7: return asALongAdxIndicator;
                case 8: return asBLongAdxIndicator;
                case 9: return asCLongAdxIndicator;
            }
            return null;
        }

        private Swing S_LongSwing(int sid)
        {
            switch (sid)
            {
                case 1: return nyALongSwing;
                case 2: return nyBLongSwing;
                case 3: return nyCLongSwing;
                case 4: return euALongSwing;
                case 5: return euBLongSwing;
                case 6: return euCLongSwing;
                case 7: return asALongSwing;
                case 8: return asBLongSwing;
                case 9: return asCLongSwing;
            }
            return null;
        }

        private EMA S_LongEarlyExitEma(int sid)
        {
            switch (sid)
            {
                case 1: return nyALongEarlyExitEma;
                case 2: return nyBLongEarlyExitEma;
                case 3: return nyCLongEarlyExitEma;
                case 4: return euALongEarlyExitEma;
                case 5: return euBLongEarlyExitEma;
                case 6: return euCLongEarlyExitEma;
                case 7: return asALongEarlyExitEma;
                case 8: return asBLongEarlyExitEma;
                case 9: return asCLongEarlyExitEma;
            }
            return null;
        }

        private ADX S_LongEarlyExitAdx(int sid)
        {
            switch (sid)
            {
                case 1: return nyALongEarlyExitAdx;
                case 2: return nyBLongEarlyExitAdx;
                case 3: return nyCLongEarlyExitAdx;
                case 4: return euALongEarlyExitAdx;
                case 5: return euBLongEarlyExitAdx;
                case 6: return euCLongEarlyExitAdx;
                case 7: return asALongEarlyExitAdx;
                case 8: return asBLongEarlyExitAdx;
                case 9: return asCLongEarlyExitAdx;
            }
            return null;
        }

        private SMA S_ShortSmaHigh(int sid)
        {
            switch (sid)
            {
                case 1: return nyAShortSmaHigh;
                case 2: return nyBShortSmaHigh;
                case 3: return nyCShortSmaHigh;
                case 4: return euAShortSmaHigh;
                case 5: return euBShortSmaHigh;
                case 6: return euCShortSmaHigh;
                case 7: return asAShortSmaHigh;
                case 8: return asBShortSmaHigh;
                case 9: return asCShortSmaHigh;
            }
            return null;
        }

        private SMA S_ShortSmaLow(int sid)
        {
            switch (sid)
            {
                case 1: return nyAShortSmaLow;
                case 2: return nyBShortSmaLow;
                case 3: return nyCShortSmaLow;
                case 4: return euAShortSmaLow;
                case 5: return euBShortSmaLow;
                case 6: return euCShortSmaLow;
                case 7: return asAShortSmaLow;
                case 8: return asBShortSmaLow;
                case 9: return asCShortSmaLow;
            }
            return null;
        }

        private EMA S_ShortEmaHigh(int sid)
        {
            switch (sid)
            {
                case 1: return nyAShortEmaHigh;
                case 2: return nyBShortEmaHigh;
                case 3: return nyCShortEmaHigh;
                case 4: return euAShortEmaHigh;
                case 5: return euBShortEmaHigh;
                case 6: return euCShortEmaHigh;
                case 7: return asAShortEmaHigh;
                case 8: return asBShortEmaHigh;
                case 9: return asCShortEmaHigh;
            }
            return null;
        }

        private EMA S_ShortEmaLow(int sid)
        {
            switch (sid)
            {
                case 1: return nyAShortEmaLow;
                case 2: return nyBShortEmaLow;
                case 3: return nyCShortEmaLow;
                case 4: return euAShortEmaLow;
                case 5: return euBShortEmaLow;
                case 6: return euCShortEmaLow;
                case 7: return asAShortEmaLow;
                case 8: return asBShortEmaLow;
                case 9: return asCShortEmaLow;
            }
            return null;
        }

        private WMA S_ShortWmaFilter(int sid)
        {
            switch (sid)
            {
                case 1: return nyAShortWmaFilter;
                case 2: return nyBShortWmaFilter;
                case 3: return nyCShortWmaFilter;
                case 4: return euAShortWmaFilter;
                case 5: return euBShortWmaFilter;
                case 6: return euCShortWmaFilter;
                case 7: return asAShortWmaFilter;
                case 8: return asBShortWmaFilter;
                case 9: return asCShortWmaFilter;
            }
            return null;
        }

        private ADX S_ShortAdxIndicator(int sid)
        {
            switch (sid)
            {
                case 1: return nyAShortAdxIndicator;
                case 2: return nyBShortAdxIndicator;
                case 3: return nyCShortAdxIndicator;
                case 4: return euAShortAdxIndicator;
                case 5: return euBShortAdxIndicator;
                case 6: return euCShortAdxIndicator;
                case 7: return asAShortAdxIndicator;
                case 8: return asBShortAdxIndicator;
                case 9: return asCShortAdxIndicator;
            }
            return null;
        }

        private Swing S_ShortSwing(int sid)
        {
            switch (sid)
            {
                case 1: return nyAShortSwing;
                case 2: return nyBShortSwing;
                case 3: return nyCShortSwing;
                case 4: return euAShortSwing;
                case 5: return euBShortSwing;
                case 6: return euCShortSwing;
                case 7: return asAShortSwing;
                case 8: return asBShortSwing;
                case 9: return asCShortSwing;
            }
            return null;
        }

        private EMA S_ShortEarlyExitEma(int sid)
        {
            switch (sid)
            {
                case 1: return nyAShortEarlyExitEma;
                case 2: return nyBShortEarlyExitEma;
                case 3: return nyCShortEarlyExitEma;
                case 4: return euAShortEarlyExitEma;
                case 5: return euBShortEarlyExitEma;
                case 6: return euCShortEarlyExitEma;
                case 7: return asAShortEarlyExitEma;
                case 8: return asBShortEarlyExitEma;
                case 9: return asCShortEarlyExitEma;
            }
            return null;
        }

        private ADX S_ShortEarlyExitAdx(int sid)
        {
            switch (sid)
            {
                case 1: return nyAShortEarlyExitAdx;
                case 2: return nyBShortEarlyExitAdx;
                case 3: return nyCShortEarlyExitAdx;
                case 4: return euAShortEarlyExitAdx;
                case 5: return euBShortEarlyExitAdx;
                case 6: return euCShortEarlyExitAdx;
                case 7: return asAShortEarlyExitAdx;
                case 8: return asBShortEarlyExitAdx;
                case 9: return asCShortEarlyExitAdx;
            }
            return null;
        }

        private ATR S_Atr(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return nyALongAtr;
                    case 2: return nyBLongAtr;
                    case 3: return nyCLongAtr;
                    case 4: return euALongAtr;
                    case 5: return euBLongAtr;
                    case 6: return euCLongAtr;
                    case 7: return asALongAtr;
                    case 8: return asBLongAtr;
                    case 9: return asCLongAtr;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return nyAShortAtr;
                    case 2: return nyBShortAtr;
                    case 3: return nyCShortAtr;
                    case 4: return euAShortAtr;
                    case 5: return euBShortAtr;
                    case 6: return euCShortAtr;
                    case 7: return asAShortAtr;
                    case 8: return asBShortAtr;
                    case 9: return asCShortAtr;
                }
            }
            return null;
        }

        // -- Session-level parameter dispatch (PER-SUB-SESSION) --
        private int S_Contracts(int sid)
        {
            switch (sid)
            {
                case 1: return NyAContracts;
                case 2: return NyBContracts;
                case 3: return NyCContracts;
                case 4: return EuAContracts;
                case 5: return EuBContracts;
                case 6: return EuCContracts;
                case 7: return AsAContracts;
                case 8: return AsBContracts;
                case 9: return AsCContracts;
            }
            return default(int);
        }

        private bool S_EnableLongTrades(int sid)
        {
            switch (sid)
            {
                case 1: return NyAEnableLongTrades;
                case 2: return NyBEnableLongTrades;
                case 3: return NyCEnableLongTrades;
                case 4: return EuAEnableLongTrades;
                case 5: return EuBEnableLongTrades;
                case 6: return EuCEnableLongTrades;
                case 7: return AsAEnableLongTrades;
                case 8: return AsBEnableLongTrades;
                case 9: return AsCEnableLongTrades;
            }
            return default(bool);
        }

        private bool S_EnableShortTrades(int sid)
        {
            switch (sid)
            {
                case 1: return NyAEnableShortTrades;
                case 2: return NyBEnableShortTrades;
                case 3: return NyCEnableShortTrades;
                case 4: return EuAEnableShortTrades;
                case 5: return EuBEnableShortTrades;
                case 6: return EuCEnableShortTrades;
                case 7: return AsAEnableShortTrades;
                case 8: return AsBEnableShortTrades;
                case 9: return AsCEnableShortTrades;
            }
            return default(bool);
        }

        private int S_MaxTradesPerSession(int sid)
        {
            switch (sid)
            {
                case 1: return NyAMaxTradesPerSession;
                case 2: return NyBMaxTradesPerSession;
                case 3: return NyCMaxTradesPerSession;
                case 4: return EuAMaxTradesPerSession;
                case 5: return EuBMaxTradesPerSession;
                case 6: return EuCMaxTradesPerSession;
                case 7: return AsAMaxTradesPerSession;
                case 8: return AsBMaxTradesPerSession;
                case 9: return AsCMaxTradesPerSession;
            }
            return default(int);
        }

        private int S_MaxWinsPerSession(int sid)
        {
            switch (sid)
            {
                case 1: return 0;
                case 2: return 0;
                case 3: return 0;
                case 4: return 0;
                case 5: return 0;
                case 6: return 0;
                case 7: return 0;
                case 8: return 0;
                case 9: return 0;
            }
            return default(int);
        }

        private int S_MaxLossesPerSession(int sid)
        {
            switch (sid)
            {
                case 1: return NyAMaxLossesPerSession;
                case 2: return NyBMaxLossesPerSession;
                case 3: return NyCMaxLossesPerSession;
                case 4: return EuAMaxLossesPerSession;
                case 5: return EuBMaxLossesPerSession;
                case 6: return EuCMaxLossesPerSession;
                case 7: return AsAMaxLossesPerSession;
                case 8: return AsBMaxLossesPerSession;
                case 9: return AsCMaxLossesPerSession;
            }
            return default(int);
        }

        private int S_MaxSessionProfitTicks(int sid)
        {
            switch (sid)
            {
                case 1: return NyAMaxSessionProfitTicks;
                case 2: return NyBMaxSessionProfitTicks;
                case 3: return NyCMaxSessionProfitTicks;
                case 4: return EuAMaxSessionProfitTicks;
                case 5: return EuBMaxSessionProfitTicks;
                case 6: return EuCMaxSessionProfitTicks;
                case 7: return AsAMaxSessionProfitTicks;
                case 8: return AsBMaxSessionProfitTicks;
                case 9: return AsCMaxSessionProfitTicks;
            }
            return default(int);
        }

        private int S_MaxSessionLossTicks(int sid)
        {
            switch (sid)
            {
                case 1: return NyAMaxSessionLossTicks;
                case 2: return NyBMaxSessionLossTicks;
                case 3: return NyCMaxSessionLossTicks;
                case 4: return EuAMaxSessionLossTicks;
                case 5: return EuBMaxSessionLossTicks;
                case 6: return EuCMaxSessionLossTicks;
                case 7: return AsAMaxSessionLossTicks;
                case 8: return AsBMaxSessionLossTicks;
                case 9: return AsCMaxSessionLossTicks;
            }
            return default(int);
        }

        private DateTime S_TradeWindowStart(int sid)
        {
            switch (sid)
            {
                case 1: return NyATradeWindowStart;
                case 2: return NyBTradeWindowStart;
                case 3: return NyCTradeWindowStart;
                case 4: return EuATradeWindowStart;
                case 5: return EuBTradeWindowStart;
                case 6: return EuCTradeWindowStart;
                case 7: return AsATradeWindowStart;
                case 8: return AsBTradeWindowStart;
                case 9: return AsCTradeWindowStart;
            }
            return default(DateTime);
        }

        private DateTime S_NoNewTradesAfter(int sid)
        {
            switch (sid)
            {
                case 1: return NyANoNewTradesAfter;
                case 2: return NyBNoNewTradesAfter;
                case 3: return NyCNoNewTradesAfter;
                case 4: return EuANoNewTradesAfter;
                case 5: return EuBNoNewTradesAfter;
                case 6: return EuCNoNewTradesAfter;
                case 7: return AsANoNewTradesAfter;
                case 8: return AsBNoNewTradesAfter;
                case 9: return AsCNoNewTradesAfter;
            }
            return default(DateTime);
        }

        private DateTime S_ForcedCloseTime(int sid)
        {
            switch (sid)
            {
                case 1: return NyAForcedCloseTime;
                case 2: return NyBForcedCloseTime;
                case 3: return NyCForcedCloseTime;
                case 4: return EuAForcedCloseTime;
                case 5: return EuBForcedCloseTime;
                case 6: return EuCForcedCloseTime;
                case 7: return AsAForcedCloseTime;
                case 8: return AsBForcedCloseTime;
                case 9: return AsCForcedCloseTime;
            }
            return default(DateTime);
        }

        // -- Sub-session on/off and per-sub-session feature toggles --
        private bool S_SubEnable(int sid)
        {
            switch (sid)
            {
                case 1: return NyAEnable;
                case 2: return NyBEnable;
                case 3: return NyCEnable;
                case 4: return EuAEnable;
                case 5: return EuBEnable;
                case 6: return EuCEnable;
                case 7: return AsAEnable;
                case 8: return AsBEnable;
                case 9: return AsCEnable;
            }
            return default(bool);
        }

        private bool S_EnableForcedClose(int sid)
        {
            switch (sid)
            {
                case 1: return NyAEnableForcedClose;
                case 2: return NyBEnableForcedClose;
                case 3: return NyCEnableForcedClose;
                case 4: return EuAEnableForcedClose;
                case 5: return EuBEnableForcedClose;
                case 6: return EuCEnableForcedClose;
                case 7: return AsAEnableForcedClose;
                case 8: return AsBEnableForcedClose;
                case 9: return AsCEnableForcedClose;
            }
            return default(bool);
        }

        private bool S_EnableNoNewTradesAfter(int sid)
        {
            switch (sid)
            {
                case 1: return NyAEnableNoNewTradesAfter;
                case 2: return NyBEnableNoNewTradesAfter;
                case 3: return NyCEnableNoNewTradesAfter;
                case 4: return EuAEnableNoNewTradesAfter;
                case 5: return EuBEnableNoNewTradesAfter;
                case 6: return EuCEnableNoNewTradesAfter;
                case 7: return AsAEnableNoNewTradesAfter;
                case 8: return AsBEnableNoNewTradesAfter;
                case 9: return AsCEnableNoNewTradesAfter;
            }
            return default(bool);
        }

        private bool S_ParentEnable(int sid)
        {
            int p = ParentGroupOf(sid);
            return p == 1 ? NyEnable : p == 2 ? EuEnable : p == 3 ? AsEnable : false;
        }
        // A sub-session is active only if BOTH parent master AND sub toggle are ON
        private bool S_Active(int sid) { return S_ParentEnable(sid) && S_SubEnable(sid); }

        // -- SD_* parameter dispatch (PER-SUB-SESSION, direction-aware) --
        private int SD_MaPeriod(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongMaPeriod;
                    case 2: return NyBLongMaPeriod;
                    case 3: return NyCLongMaPeriod;
                    case 4: return EuALongMaPeriod;
                    case 5: return EuBLongMaPeriod;
                    case 6: return EuCLongMaPeriod;
                    case 7: return AsALongMaPeriod;
                    case 8: return AsBLongMaPeriod;
                    case 9: return AsCLongMaPeriod;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortMaPeriod;
                    case 2: return NyBShortMaPeriod;
                    case 3: return NyCShortMaPeriod;
                    case 4: return EuAShortMaPeriod;
                    case 5: return EuBShortMaPeriod;
                    case 6: return EuCShortMaPeriod;
                    case 7: return AsAShortMaPeriod;
                    case 8: return AsBShortMaPeriod;
                    case 9: return AsCShortMaPeriod;
                }
            }
            return default(int);
        }

        private int SD_EmaPeriod(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongEmaPeriod;
                    case 2: return NyBLongEmaPeriod;
                    case 3: return NyCLongEmaPeriod;
                    case 4: return EuALongEmaPeriod;
                    case 5: return EuBLongEmaPeriod;
                    case 6: return EuCLongEmaPeriod;
                    case 7: return AsALongEmaPeriod;
                    case 8: return AsBLongEmaPeriod;
                    case 9: return AsCLongEmaPeriod;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortEmaPeriod;
                    case 2: return NyBShortEmaPeriod;
                    case 3: return NyCShortEmaPeriod;
                    case 4: return EuAShortEmaPeriod;
                    case 5: return EuBShortEmaPeriod;
                    case 6: return EuCShortEmaPeriod;
                    case 7: return AsAShortEmaPeriod;
                    case 8: return AsBShortEmaPeriod;
                    case 9: return AsCShortEmaPeriod;
                }
            }
            return default(int);
        }

        private MAMode SD_MaType(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongMaType;
                    case 2: return NyBLongMaType;
                    case 3: return NyCLongMaType;
                    case 4: return EuALongMaType;
                    case 5: return EuBLongMaType;
                    case 6: return EuCLongMaType;
                    case 7: return AsALongMaType;
                    case 8: return AsBLongMaType;
                    case 9: return AsCLongMaType;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortMaType;
                    case 2: return NyBShortMaType;
                    case 3: return NyCShortMaType;
                    case 4: return EuAShortMaType;
                    case 5: return EuBShortMaType;
                    case 6: return EuCShortMaType;
                    case 7: return AsAShortMaType;
                    case 8: return AsBShortMaType;
                    case 9: return AsCShortMaType;
                }
            }
            return default(MAMode);
        }

        private MichalEntryMode SD_EntryMode(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return MichalEntryMode.Market;
                    case 2: return MichalEntryMode.Market;
                    case 3: return MichalEntryMode.Market;
                    case 4: return MichalEntryMode.Market;
                    case 5: return MichalEntryMode.Market;
                    case 6: return MichalEntryMode.Market;
                    case 7: return MichalEntryMode.Market;
                    case 8: return MichalEntryMode.Market;
                    case 9: return MichalEntryMode.Market;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return MichalEntryMode.Market;
                    case 2: return MichalEntryMode.Market;
                    case 3: return MichalEntryMode.Market;
                    case 4: return MichalEntryMode.Market;
                    case 5: return MichalEntryMode.Market;
                    case 6: return MichalEntryMode.Market;
                    case 7: return MichalEntryMode.Market;
                    case 8: return MichalEntryMode.Market;
                    case 9: return MichalEntryMode.Market;
                }
            }
            return default(MichalEntryMode);
        }

        private int SD_LimitOffsetTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return 1;
                    case 2: return 1;
                    case 3: return 1;
                    case 4: return 1;
                    case 5: return 1;
                    case 6: return 1;
                    case 7: return 1;
                    case 8: return 1;
                    case 9: return 1;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return 1;
                    case 2: return 1;
                    case 3: return 1;
                    case 4: return 1;
                    case 5: return 1;
                    case 6: return 1;
                    case 7: return 1;
                    case 8: return 1;
                    case 9: return 1;
                }
            }
            return default(int);
        }

        private double SD_LimitRetracementPct(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return 1.0;
                    case 2: return 1.0;
                    case 3: return 1.0;
                    case 4: return 6.0;
                    case 5: return 6.0;
                    case 6: return 6.0;
                    case 7: return 6.0;
                    case 8: return 6.0;
                    case 9: return 6.0;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return 1.0;
                    case 2: return 1.0;
                    case 3: return 1.0;
                    case 4: return 1.0;
                    case 5: return 1.0;
                    case 6: return 1.0;
                    case 7: return 1.0;
                    case 8: return 1.0;
                    case 9: return 1.0;
                }
            }
            return default(double);
        }

        private MichalTPMode SD_TakeProfitMode(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return MichalTPMode.CandleMultiple;
                    case 2: return MichalTPMode.CandleMultiple;
                    case 3: return MichalTPMode.CandleMultiple;
                    case 4: return MichalTPMode.CandleMultiple;
                    case 5: return MichalTPMode.CandleMultiple;
                    case 6: return MichalTPMode.CandleMultiple;
                    case 7: return MichalTPMode.CandleMultiple;
                    case 8: return MichalTPMode.CandleMultiple;
                    case 9: return MichalTPMode.CandleMultiple;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return MichalTPMode.CandleMultiple;
                    case 2: return MichalTPMode.CandleMultiple;
                    case 3: return MichalTPMode.CandleMultiple;
                    case 4: return MichalTPMode.CandleMultiple;
                    case 5: return MichalTPMode.CandleMultiple;
                    case 6: return MichalTPMode.CandleMultiple;
                    case 7: return MichalTPMode.CandleMultiple;
                    case 8: return MichalTPMode.CandleMultiple;
                    case 9: return MichalTPMode.CandleMultiple;
                }
            }
            return default(MichalTPMode);
        }

        private int SD_TakeProfitTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return 310;
                    case 2: return 310;
                    case 3: return 310;
                    case 4: return 180;
                    case 5: return 180;
                    case 6: return 180;
                    case 7: return 180;
                    case 8: return 180;
                    case 9: return 180;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return 310;
                    case 2: return 310;
                    case 3: return 310;
                    case 4: return 310;
                    case 5: return 310;
                    case 6: return 310;
                    case 7: return 310;
                    case 8: return 310;
                    case 9: return 310;
                }
            }
            return default(int);
        }

        private int SD_MaxTakeProfitTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongMaxTakeProfitTicks;
                    case 2: return NyBLongMaxTakeProfitTicks;
                    case 3: return NyCLongMaxTakeProfitTicks;
                    case 4: return EuALongMaxTakeProfitTicks;
                    case 5: return EuBLongMaxTakeProfitTicks;
                    case 6: return EuCLongMaxTakeProfitTicks;
                    case 7: return AsALongMaxTakeProfitTicks;
                    case 8: return AsBLongMaxTakeProfitTicks;
                    case 9: return AsCLongMaxTakeProfitTicks;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortMaxTakeProfitTicks;
                    case 2: return NyBShortMaxTakeProfitTicks;
                    case 3: return NyCShortMaxTakeProfitTicks;
                    case 4: return EuAShortMaxTakeProfitTicks;
                    case 5: return EuBShortMaxTakeProfitTicks;
                    case 6: return EuCShortMaxTakeProfitTicks;
                    case 7: return AsAShortMaxTakeProfitTicks;
                    case 8: return AsBShortMaxTakeProfitTicks;
                    case 9: return AsCShortMaxTakeProfitTicks;
                }
            }
            return default(int);
        }

        private int SD_SwingStrength(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return 1;
                    case 2: return 1;
                    case 3: return 1;
                    case 4: return 1;
                    case 5: return 1;
                    case 6: return 1;
                    case 7: return 1;
                    case 8: return 1;
                    case 9: return 1;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return 1;
                    case 2: return 1;
                    case 3: return 1;
                    case 4: return 1;
                    case 5: return 1;
                    case 6: return 1;
                    case 7: return 1;
                    case 8: return 1;
                    case 9: return 1;
                }
            }
            return default(int);
        }

        private double SD_CandleMultiplier(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongCandleMultiplier;
                    case 2: return NyBLongCandleMultiplier;
                    case 3: return NyCLongCandleMultiplier;
                    case 4: return EuALongCandleMultiplier;
                    case 5: return EuBLongCandleMultiplier;
                    case 6: return EuCLongCandleMultiplier;
                    case 7: return AsALongCandleMultiplier;
                    case 8: return AsBLongCandleMultiplier;
                    case 9: return AsCLongCandleMultiplier;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortCandleMultiplier;
                    case 2: return NyBShortCandleMultiplier;
                    case 3: return NyCShortCandleMultiplier;
                    case 4: return EuAShortCandleMultiplier;
                    case 5: return EuBShortCandleMultiplier;
                    case 6: return EuCShortCandleMultiplier;
                    case 7: return AsAShortCandleMultiplier;
                    case 8: return AsBShortCandleMultiplier;
                    case 9: return AsCShortCandleMultiplier;
                }
            }
            return default(double);
        }

        private int SD_AtrPeriod(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongAtrPeriod;
                    case 2: return NyBLongAtrPeriod;
                    case 3: return NyCLongAtrPeriod;
                    case 4: return EuALongAtrPeriod;
                    case 5: return EuBLongAtrPeriod;
                    case 6: return EuCLongAtrPeriod;
                    case 7: return AsALongAtrPeriod;
                    case 8: return AsBLongAtrPeriod;
                    case 9: return AsCLongAtrPeriod;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortAtrPeriod;
                    case 2: return NyBShortAtrPeriod;
                    case 3: return NyCShortAtrPeriod;
                    case 4: return EuAShortAtrPeriod;
                    case 5: return EuBShortAtrPeriod;
                    case 6: return EuCShortAtrPeriod;
                    case 7: return AsAShortAtrPeriod;
                    case 8: return AsBShortAtrPeriod;
                    case 9: return AsCShortAtrPeriod;
                }
            }
            return default(int);
        }

        private int SD_AtrThresholdTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongAtrThresholdTicks;
                    case 2: return NyBLongAtrThresholdTicks;
                    case 3: return NyCLongAtrThresholdTicks;
                    case 4: return EuALongAtrThresholdTicks;
                    case 5: return EuBLongAtrThresholdTicks;
                    case 6: return EuCLongAtrThresholdTicks;
                    case 7: return AsALongAtrThresholdTicks;
                    case 8: return AsBLongAtrThresholdTicks;
                    case 9: return AsCLongAtrThresholdTicks;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortAtrThresholdTicks;
                    case 2: return NyBShortAtrThresholdTicks;
                    case 3: return NyCShortAtrThresholdTicks;
                    case 4: return EuAShortAtrThresholdTicks;
                    case 5: return EuBShortAtrThresholdTicks;
                    case 6: return EuCShortAtrThresholdTicks;
                    case 7: return AsAShortAtrThresholdTicks;
                    case 8: return AsBShortAtrThresholdTicks;
                    case 9: return AsCShortAtrThresholdTicks;
                }
            }
            return default(int);
        }

        private double SD_LowAtrCandleMultiplier(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongLowAtrCandleMultiplier;
                    case 2: return NyBLongLowAtrCandleMultiplier;
                    case 3: return NyCLongLowAtrCandleMultiplier;
                    case 4: return EuALongLowAtrCandleMultiplier;
                    case 5: return EuBLongLowAtrCandleMultiplier;
                    case 6: return EuCLongLowAtrCandleMultiplier;
                    case 7: return AsALongLowAtrCandleMultiplier;
                    case 8: return AsBLongLowAtrCandleMultiplier;
                    case 9: return AsCLongLowAtrCandleMultiplier;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortLowAtrCandleMultiplier;
                    case 2: return NyBShortLowAtrCandleMultiplier;
                    case 3: return NyCShortLowAtrCandleMultiplier;
                    case 4: return EuAShortLowAtrCandleMultiplier;
                    case 5: return EuBShortLowAtrCandleMultiplier;
                    case 6: return EuCShortLowAtrCandleMultiplier;
                    case 7: return AsAShortLowAtrCandleMultiplier;
                    case 8: return AsBShortLowAtrCandleMultiplier;
                    case 9: return AsCShortLowAtrCandleMultiplier;
                }
            }
            return default(double);
        }

        private double SD_HighAtrCandleMultiplier(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongHighAtrCandleMultiplier;
                    case 2: return NyBLongHighAtrCandleMultiplier;
                    case 3: return NyCLongHighAtrCandleMultiplier;
                    case 4: return EuALongHighAtrCandleMultiplier;
                    case 5: return EuBLongHighAtrCandleMultiplier;
                    case 6: return EuCLongHighAtrCandleMultiplier;
                    case 7: return AsALongHighAtrCandleMultiplier;
                    case 8: return AsBLongHighAtrCandleMultiplier;
                    case 9: return AsCLongHighAtrCandleMultiplier;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortHighAtrCandleMultiplier;
                    case 2: return NyBShortHighAtrCandleMultiplier;
                    case 3: return NyCShortHighAtrCandleMultiplier;
                    case 4: return EuAShortHighAtrCandleMultiplier;
                    case 5: return EuBShortHighAtrCandleMultiplier;
                    case 6: return EuCShortHighAtrCandleMultiplier;
                    case 7: return AsAShortHighAtrCandleMultiplier;
                    case 8: return AsBShortHighAtrCandleMultiplier;
                    case 9: return AsCShortHighAtrCandleMultiplier;
                }
            }
            return default(double);
        }

        private int SD_SlExtraTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongSlExtraTicks;
                    case 2: return NyBLongSlExtraTicks;
                    case 3: return NyCLongSlExtraTicks;
                    case 4: return EuALongSlExtraTicks;
                    case 5: return EuBLongSlExtraTicks;
                    case 6: return EuCLongSlExtraTicks;
                    case 7: return AsALongSlExtraTicks;
                    case 8: return AsBLongSlExtraTicks;
                    case 9: return AsCLongSlExtraTicks;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortSlExtraTicks;
                    case 2: return NyBShortSlExtraTicks;
                    case 3: return NyCShortSlExtraTicks;
                    case 4: return EuAShortSlExtraTicks;
                    case 5: return EuBShortSlExtraTicks;
                    case 6: return EuCShortSlExtraTicks;
                    case 7: return AsAShortSlExtraTicks;
                    case 8: return AsBShortSlExtraTicks;
                    case 9: return AsCShortSlExtraTicks;
                }
            }
            return default(int);
        }

        private int SD_TrailOffsetTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongTrailOffsetTicks;
                    case 2: return NyBLongTrailOffsetTicks;
                    case 3: return NyCLongTrailOffsetTicks;
                    case 4: return EuALongTrailOffsetTicks;
                    case 5: return EuBLongTrailOffsetTicks;
                    case 6: return EuCLongTrailOffsetTicks;
                    case 7: return AsALongTrailOffsetTicks;
                    case 8: return AsBLongTrailOffsetTicks;
                    case 9: return AsCLongTrailOffsetTicks;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortTrailOffsetTicks;
                    case 2: return NyBShortTrailOffsetTicks;
                    case 3: return NyCShortTrailOffsetTicks;
                    case 4: return EuAShortTrailOffsetTicks;
                    case 5: return EuBShortTrailOffsetTicks;
                    case 6: return EuCShortTrailOffsetTicks;
                    case 7: return AsAShortTrailOffsetTicks;
                    case 8: return AsBShortTrailOffsetTicks;
                    case 9: return AsCShortTrailOffsetTicks;
                }
            }
            return default(int);
        }

        private int SD_TrailDelayBars(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongTrailDelayBars;
                    case 2: return NyBLongTrailDelayBars;
                    case 3: return NyCLongTrailDelayBars;
                    case 4: return EuALongTrailDelayBars;
                    case 5: return EuBLongTrailDelayBars;
                    case 6: return EuCLongTrailDelayBars;
                    case 7: return AsALongTrailDelayBars;
                    case 8: return AsBLongTrailDelayBars;
                    case 9: return AsCLongTrailDelayBars;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortTrailDelayBars;
                    case 2: return NyBShortTrailDelayBars;
                    case 3: return NyCShortTrailDelayBars;
                    case 4: return EuAShortTrailDelayBars;
                    case 5: return EuBShortTrailDelayBars;
                    case 6: return EuCShortTrailDelayBars;
                    case 7: return AsAShortTrailDelayBars;
                    case 8: return AsBShortTrailDelayBars;
                    case 9: return AsCShortTrailDelayBars;
                }
            }
            return default(int);
        }

        private int SD_MaxSlTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongMaxSlTicks;
                    case 2: return NyBLongMaxSlTicks;
                    case 3: return NyCLongMaxSlTicks;
                    case 4: return EuALongMaxSlTicks;
                    case 5: return EuBLongMaxSlTicks;
                    case 6: return EuCLongMaxSlTicks;
                    case 7: return AsALongMaxSlTicks;
                    case 8: return AsBLongMaxSlTicks;
                    case 9: return AsCLongMaxSlTicks;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortMaxSlTicks;
                    case 2: return NyBShortMaxSlTicks;
                    case 3: return NyCShortMaxSlTicks;
                    case 4: return EuAShortMaxSlTicks;
                    case 5: return EuBShortMaxSlTicks;
                    case 6: return EuCShortMaxSlTicks;
                    case 7: return AsAShortMaxSlTicks;
                    case 8: return AsBShortMaxSlTicks;
                    case 9: return AsCShortMaxSlTicks;
                }
            }
            return default(int);
        }

        private double SD_MaxSlToTpRatio(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongMaxSlToTpRatio;
                    case 2: return NyBLongMaxSlToTpRatio;
                    case 3: return NyCLongMaxSlToTpRatio;
                    case 4: return EuALongMaxSlToTpRatio;
                    case 5: return EuBLongMaxSlToTpRatio;
                    case 6: return EuCLongMaxSlToTpRatio;
                    case 7: return AsALongMaxSlToTpRatio;
                    case 8: return AsBLongMaxSlToTpRatio;
                    case 9: return AsCLongMaxSlToTpRatio;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortMaxSlToTpRatio;
                    case 2: return NyBShortMaxSlToTpRatio;
                    case 3: return NyCShortMaxSlToTpRatio;
                    case 4: return EuAShortMaxSlToTpRatio;
                    case 5: return EuBShortMaxSlToTpRatio;
                    case 6: return EuCShortMaxSlToTpRatio;
                    case 7: return AsAShortMaxSlToTpRatio;
                    case 8: return AsBShortMaxSlToTpRatio;
                    case 9: return AsCShortMaxSlToTpRatio;
                }
            }
            return default(double);
        }

        private bool SD_UsePriorCandleSl(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongUsePriorCandleSl;
                    case 2: return NyBLongUsePriorCandleSl;
                    case 3: return NyCLongUsePriorCandleSl;
                    case 4: return EuALongUsePriorCandleSl;
                    case 5: return EuBLongUsePriorCandleSl;
                    case 6: return EuCLongUsePriorCandleSl;
                    case 7: return AsALongUsePriorCandleSl;
                    case 8: return AsBLongUsePriorCandleSl;
                    case 9: return AsCLongUsePriorCandleSl;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortUsePriorCandleSl;
                    case 2: return NyBShortUsePriorCandleSl;
                    case 3: return NyCShortUsePriorCandleSl;
                    case 4: return EuAShortUsePriorCandleSl;
                    case 5: return EuBShortUsePriorCandleSl;
                    case 6: return EuCShortUsePriorCandleSl;
                    case 7: return AsAShortUsePriorCandleSl;
                    case 8: return AsBShortUsePriorCandleSl;
                    case 9: return AsCShortUsePriorCandleSl;
                }
            }
            return default(bool);
        }

        private bool SD_SlAtMa(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongSlAtMa;
                    case 2: return NyBLongSlAtMa;
                    case 3: return NyCLongSlAtMa;
                    case 4: return EuALongSlAtMa;
                    case 5: return EuBLongSlAtMa;
                    case 6: return EuCLongSlAtMa;
                    case 7: return AsALongSlAtMa;
                    case 8: return AsBLongSlAtMa;
                    case 9: return AsCLongSlAtMa;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortSlAtMa;
                    case 2: return NyBShortSlAtMa;
                    case 3: return NyCShortSlAtMa;
                    case 4: return EuAShortSlAtMa;
                    case 5: return EuBShortSlAtMa;
                    case 6: return EuCShortSlAtMa;
                    case 7: return AsAShortSlAtMa;
                    case 8: return AsBShortSlAtMa;
                    case 9: return AsCShortSlAtMa;
                }
            }
            return default(bool);
        }

        private bool SD_MoveSlToEntryBar(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongMoveSlToEntryBar;
                    case 2: return NyBLongMoveSlToEntryBar;
                    case 3: return NyCLongMoveSlToEntryBar;
                    case 4: return EuALongMoveSlToEntryBar;
                    case 5: return EuBLongMoveSlToEntryBar;
                    case 6: return EuCLongMoveSlToEntryBar;
                    case 7: return AsALongMoveSlToEntryBar;
                    case 8: return AsBLongMoveSlToEntryBar;
                    case 9: return AsCLongMoveSlToEntryBar;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortMoveSlToEntryBar;
                    case 2: return NyBShortMoveSlToEntryBar;
                    case 3: return NyCShortMoveSlToEntryBar;
                    case 4: return EuAShortMoveSlToEntryBar;
                    case 5: return EuBShortMoveSlToEntryBar;
                    case 6: return EuCShortMoveSlToEntryBar;
                    case 7: return AsAShortMoveSlToEntryBar;
                    case 8: return AsBShortMoveSlToEntryBar;
                    case 9: return AsCShortMoveSlToEntryBar;
                }
            }
            return default(bool);
        }

        private int SD_TrailCandleOffset(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongTrailCandleOffset;
                    case 2: return NyBLongTrailCandleOffset;
                    case 3: return NyCLongTrailCandleOffset;
                    case 4: return EuALongTrailCandleOffset;
                    case 5: return EuBLongTrailCandleOffset;
                    case 6: return EuCLongTrailCandleOffset;
                    case 7: return AsALongTrailCandleOffset;
                    case 8: return AsBLongTrailCandleOffset;
                    case 9: return AsCLongTrailCandleOffset;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortTrailCandleOffset;
                    case 2: return NyBShortTrailCandleOffset;
                    case 3: return NyCShortTrailCandleOffset;
                    case 4: return EuAShortTrailCandleOffset;
                    case 5: return EuBShortTrailCandleOffset;
                    case 6: return EuCShortTrailCandleOffset;
                    case 7: return AsAShortTrailCandleOffset;
                    case 8: return AsBShortTrailCandleOffset;
                    case 9: return AsCShortTrailCandleOffset;
                }
            }
            return default(int);
        }

        private bool SD_EnableBreakeven(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongEnableBreakeven;
                    case 2: return NyBLongEnableBreakeven;
                    case 3: return NyCLongEnableBreakeven;
                    case 4: return EuALongEnableBreakeven;
                    case 5: return EuBLongEnableBreakeven;
                    case 6: return EuCLongEnableBreakeven;
                    case 7: return AsALongEnableBreakeven;
                    case 8: return AsBLongEnableBreakeven;
                    case 9: return AsCLongEnableBreakeven;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortEnableBreakeven;
                    case 2: return NyBShortEnableBreakeven;
                    case 3: return NyCShortEnableBreakeven;
                    case 4: return EuAShortEnableBreakeven;
                    case 5: return EuBShortEnableBreakeven;
                    case 6: return EuCShortEnableBreakeven;
                    case 7: return AsAShortEnableBreakeven;
                    case 8: return AsBShortEnableBreakeven;
                    case 9: return AsCShortEnableBreakeven;
                }
            }
            return default(bool);
        }

        private BEMode2 SD_BreakevenMode(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return BEMode2.FixedTicks;
                    case 2: return BEMode2.FixedTicks;
                    case 3: return BEMode2.FixedTicks;
                    case 4: return BEMode2.FixedTicks;
                    case 5: return BEMode2.FixedTicks;
                    case 6: return BEMode2.FixedTicks;
                    case 7: return BEMode2.FixedTicks;
                    case 8: return BEMode2.FixedTicks;
                    case 9: return BEMode2.FixedTicks;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return BEMode2.FixedTicks;
                    case 2: return BEMode2.FixedTicks;
                    case 3: return BEMode2.FixedTicks;
                    case 4: return BEMode2.FixedTicks;
                    case 5: return BEMode2.FixedTicks;
                    case 6: return BEMode2.FixedTicks;
                    case 7: return BEMode2.FixedTicks;
                    case 8: return BEMode2.FixedTicks;
                    case 9: return BEMode2.FixedTicks;
                }
            }
            return default(BEMode2);
        }

        private int SD_BreakevenTriggerTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongBreakevenTriggerTicks;
                    case 2: return NyBLongBreakevenTriggerTicks;
                    case 3: return NyCLongBreakevenTriggerTicks;
                    case 4: return EuALongBreakevenTriggerTicks;
                    case 5: return EuBLongBreakevenTriggerTicks;
                    case 6: return EuCLongBreakevenTriggerTicks;
                    case 7: return AsALongBreakevenTriggerTicks;
                    case 8: return AsBLongBreakevenTriggerTicks;
                    case 9: return AsCLongBreakevenTriggerTicks;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortBreakevenTriggerTicks;
                    case 2: return NyBShortBreakevenTriggerTicks;
                    case 3: return NyCShortBreakevenTriggerTicks;
                    case 4: return EuAShortBreakevenTriggerTicks;
                    case 5: return EuBShortBreakevenTriggerTicks;
                    case 6: return EuCShortBreakevenTriggerTicks;
                    case 7: return AsAShortBreakevenTriggerTicks;
                    case 8: return AsBShortBreakevenTriggerTicks;
                    case 9: return AsCShortBreakevenTriggerTicks;
                }
            }
            return default(int);
        }

        private double SD_BreakevenCandlePct(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongBreakevenCandlePct;
                    case 2: return NyBLongBreakevenCandlePct;
                    case 3: return NyCLongBreakevenCandlePct;
                    case 4: return EuALongBreakevenCandlePct;
                    case 5: return EuBLongBreakevenCandlePct;
                    case 6: return EuCLongBreakevenCandlePct;
                    case 7: return AsALongBreakevenCandlePct;
                    case 8: return AsBLongBreakevenCandlePct;
                    case 9: return AsCLongBreakevenCandlePct;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return 50.0;
                    case 2: return 50.0;
                    case 3: return 50.0;
                    case 4: return 50.0;
                    case 5: return 50.0;
                    case 6: return 50.0;
                    case 7: return 50.0;
                    case 8: return 50.0;
                    case 9: return 50.0;
                }
            }
            return default(double);
        }

        private int SD_BreakevenOffsetTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongBreakevenOffsetTicks;
                    case 2: return NyBLongBreakevenOffsetTicks;
                    case 3: return NyCLongBreakevenOffsetTicks;
                    case 4: return EuALongBreakevenOffsetTicks;
                    case 5: return EuBLongBreakevenOffsetTicks;
                    case 6: return EuCLongBreakevenOffsetTicks;
                    case 7: return AsALongBreakevenOffsetTicks;
                    case 8: return AsBLongBreakevenOffsetTicks;
                    case 9: return AsCLongBreakevenOffsetTicks;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortBreakevenOffsetTicks;
                    case 2: return NyBShortBreakevenOffsetTicks;
                    case 3: return NyCShortBreakevenOffsetTicks;
                    case 4: return EuAShortBreakevenOffsetTicks;
                    case 5: return EuBShortBreakevenOffsetTicks;
                    case 6: return EuCShortBreakevenOffsetTicks;
                    case 7: return AsAShortBreakevenOffsetTicks;
                    case 8: return AsBShortBreakevenOffsetTicks;
                    case 9: return AsCShortBreakevenOffsetTicks;
                }
            }
            return default(int);
        }

        private int SD_MaxBarsInTrade(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongMaxBarsInTrade;
                    case 2: return NyBLongMaxBarsInTrade;
                    case 3: return NyCLongMaxBarsInTrade;
                    case 4: return EuALongMaxBarsInTrade;
                    case 5: return EuBLongMaxBarsInTrade;
                    case 6: return EuCLongMaxBarsInTrade;
                    case 7: return AsALongMaxBarsInTrade;
                    case 8: return AsBLongMaxBarsInTrade;
                    case 9: return AsCLongMaxBarsInTrade;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortMaxBarsInTrade;
                    case 2: return NyBShortMaxBarsInTrade;
                    case 3: return NyCShortMaxBarsInTrade;
                    case 4: return EuAShortMaxBarsInTrade;
                    case 5: return EuBShortMaxBarsInTrade;
                    case 6: return EuCShortMaxBarsInTrade;
                    case 7: return AsAShortMaxBarsInTrade;
                    case 8: return AsBShortMaxBarsInTrade;
                    case 9: return AsCShortMaxBarsInTrade;
                }
            }
            return default(int);
        }

        private bool SD_EnableOpposingBarExit(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return false;
                    case 2: return false;
                    case 3: return false;
                    case 4: return false;
                    case 5: return false;
                    case 6: return false;
                    case 7: return false;
                    case 8: return false;
                    case 9: return false;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return false;
                    case 2: return false;
                    case 3: return false;
                    case 4: return false;
                    case 5: return false;
                    case 6: return false;
                    case 7: return false;
                    case 8: return false;
                    case 9: return false;
                }
            }
            return default(bool);
        }

        private bool SD_EnableEngulfingExit(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return false;
                    case 2: return false;
                    case 3: return false;
                    case 4: return false;
                    case 5: return false;
                    case 6: return false;
                    case 7: return false;
                    case 8: return false;
                    case 9: return false;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return false;
                    case 2: return false;
                    case 3: return false;
                    case 4: return false;
                    case 5: return false;
                    case 6: return false;
                    case 7: return false;
                    case 8: return false;
                    case 9: return false;
                }
            }
            return default(bool);
        }

        private bool SD_EnablePriceOffsetTrail(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongEnablePriceOffsetTrail;
                    case 2: return NyBLongEnablePriceOffsetTrail;
                    case 3: return NyCLongEnablePriceOffsetTrail;
                    case 4: return EuALongEnablePriceOffsetTrail;
                    case 5: return EuBLongEnablePriceOffsetTrail;
                    case 6: return EuCLongEnablePriceOffsetTrail;
                    case 7: return AsALongEnablePriceOffsetTrail;
                    case 8: return AsBLongEnablePriceOffsetTrail;
                    case 9: return AsCLongEnablePriceOffsetTrail;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortEnablePriceOffsetTrail;
                    case 2: return NyBShortEnablePriceOffsetTrail;
                    case 3: return NyCShortEnablePriceOffsetTrail;
                    case 4: return EuAShortEnablePriceOffsetTrail;
                    case 5: return EuBShortEnablePriceOffsetTrail;
                    case 6: return EuCShortEnablePriceOffsetTrail;
                    case 7: return AsAShortEnablePriceOffsetTrail;
                    case 8: return AsBShortEnablePriceOffsetTrail;
                    case 9: return AsCShortEnablePriceOffsetTrail;
                }
            }
            return default(bool);
        }

        private int SD_PriceOffsetReductionTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongPriceOffsetReductionTicks;
                    case 2: return NyBLongPriceOffsetReductionTicks;
                    case 3: return NyCLongPriceOffsetReductionTicks;
                    case 4: return EuALongPriceOffsetReductionTicks;
                    case 5: return EuBLongPriceOffsetReductionTicks;
                    case 6: return EuCLongPriceOffsetReductionTicks;
                    case 7: return AsALongPriceOffsetReductionTicks;
                    case 8: return AsBLongPriceOffsetReductionTicks;
                    case 9: return AsCLongPriceOffsetReductionTicks;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortPriceOffsetReductionTicks;
                    case 2: return NyBShortPriceOffsetReductionTicks;
                    case 3: return NyCShortPriceOffsetReductionTicks;
                    case 4: return EuAShortPriceOffsetReductionTicks;
                    case 5: return EuBShortPriceOffsetReductionTicks;
                    case 6: return EuCShortPriceOffsetReductionTicks;
                    case 7: return AsAShortPriceOffsetReductionTicks;
                    case 8: return AsBShortPriceOffsetReductionTicks;
                    case 9: return AsCShortPriceOffsetReductionTicks;
                }
            }
            return default(int);
        }

        private bool SD_EnableRMultipleTrail(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongEnableRMultipleTrail;
                    case 2: return NyBLongEnableRMultipleTrail;
                    case 3: return NyCLongEnableRMultipleTrail;
                    case 4: return EuALongEnableRMultipleTrail;
                    case 5: return EuBLongEnableRMultipleTrail;
                    case 6: return EuCLongEnableRMultipleTrail;
                    case 7: return AsALongEnableRMultipleTrail;
                    case 8: return AsBLongEnableRMultipleTrail;
                    case 9: return AsCLongEnableRMultipleTrail;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortEnableRMultipleTrail;
                    case 2: return NyBShortEnableRMultipleTrail;
                    case 3: return NyCShortEnableRMultipleTrail;
                    case 4: return EuAShortEnableRMultipleTrail;
                    case 5: return EuBShortEnableRMultipleTrail;
                    case 6: return EuCShortEnableRMultipleTrail;
                    case 7: return AsAShortEnableRMultipleTrail;
                    case 8: return AsBShortEnableRMultipleTrail;
                    case 9: return AsCShortEnableRMultipleTrail;
                }
            }
            return default(bool);
        }

        private double SD_RMultipleActivationPct(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongRMultipleActivationPct;
                    case 2: return NyBLongRMultipleActivationPct;
                    case 3: return NyCLongRMultipleActivationPct;
                    case 4: return EuALongRMultipleActivationPct;
                    case 5: return EuBLongRMultipleActivationPct;
                    case 6: return EuCLongRMultipleActivationPct;
                    case 7: return AsALongRMultipleActivationPct;
                    case 8: return AsBLongRMultipleActivationPct;
                    case 9: return AsCLongRMultipleActivationPct;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortRMultipleActivationPct;
                    case 2: return NyBShortRMultipleActivationPct;
                    case 3: return NyCShortRMultipleActivationPct;
                    case 4: return EuAShortRMultipleActivationPct;
                    case 5: return EuBShortRMultipleActivationPct;
                    case 6: return EuCShortRMultipleActivationPct;
                    case 7: return AsAShortRMultipleActivationPct;
                    case 8: return AsBShortRMultipleActivationPct;
                    case 9: return AsCShortRMultipleActivationPct;
                }
            }
            return default(double);
        }

        private int SD_MAEMaxTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongMAEMaxTicks;
                    case 2: return NyBLongMAEMaxTicks;
                    case 3: return NyCLongMAEMaxTicks;
                    case 4: return EuALongMAEMaxTicks;
                    case 5: return EuBLongMAEMaxTicks;
                    case 6: return EuCLongMAEMaxTicks;
                    case 7: return AsALongMAEMaxTicks;
                    case 8: return AsBLongMAEMaxTicks;
                    case 9: return AsCLongMAEMaxTicks;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortMAEMaxTicks;
                    case 2: return NyBShortMAEMaxTicks;
                    case 3: return NyCShortMAEMaxTicks;
                    case 4: return EuAShortMAEMaxTicks;
                    case 5: return EuBShortMAEMaxTicks;
                    case 6: return EuCShortMAEMaxTicks;
                    case 7: return AsAShortMAEMaxTicks;
                    case 8: return AsBShortMAEMaxTicks;
                    case 9: return AsCShortMAEMaxTicks;
                }
            }
            return default(int);
        }

        private double SD_RMultipleTrailPct(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongRMultipleTrailPct;
                    case 2: return NyBLongRMultipleTrailPct;
                    case 3: return NyCLongRMultipleTrailPct;
                    case 4: return EuALongRMultipleTrailPct;
                    case 5: return EuBLongRMultipleTrailPct;
                    case 6: return EuCLongRMultipleTrailPct;
                    case 7: return AsALongRMultipleTrailPct;
                    case 8: return AsBLongRMultipleTrailPct;
                    case 9: return AsCLongRMultipleTrailPct;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortRMultipleTrailPct;
                    case 2: return NyBShortRMultipleTrailPct;
                    case 3: return NyCShortRMultipleTrailPct;
                    case 4: return EuAShortRMultipleTrailPct;
                    case 5: return EuBShortRMultipleTrailPct;
                    case 6: return EuCShortRMultipleTrailPct;
                    case 7: return AsAShortRMultipleTrailPct;
                    case 8: return AsBShortRMultipleTrailPct;
                    case 9: return AsCShortRMultipleTrailPct;
                }
            }
            return default(double);
        }

        private double SD_RMultipleLockPct(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongRMultipleLockPct;
                    case 2: return NyBLongRMultipleLockPct;
                    case 3: return NyCLongRMultipleLockPct;
                    case 4: return EuALongRMultipleLockPct;
                    case 5: return EuBLongRMultipleLockPct;
                    case 6: return EuCLongRMultipleLockPct;
                    case 7: return AsALongRMultipleLockPct;
                    case 8: return AsBLongRMultipleLockPct;
                    case 9: return AsCLongRMultipleLockPct;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortRMultipleLockPct;
                    case 2: return NyBShortRMultipleLockPct;
                    case 3: return NyCShortRMultipleLockPct;
                    case 4: return EuAShortRMultipleLockPct;
                    case 5: return EuBShortRMultipleLockPct;
                    case 6: return EuCShortRMultipleLockPct;
                    case 7: return AsAShortRMultipleLockPct;
                    case 8: return AsBShortRMultipleLockPct;
                    case 9: return AsCShortRMultipleLockPct;
                }
            }
            return default(double);
        }

        private bool SD_RequireDirectionFlip(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongRequireDirectionFlip;
                    case 2: return NyBLongRequireDirectionFlip;
                    case 3: return NyCLongRequireDirectionFlip;
                    case 4: return EuALongRequireDirectionFlip;
                    case 5: return EuBLongRequireDirectionFlip;
                    case 6: return EuCLongRequireDirectionFlip;
                    case 7: return AsALongRequireDirectionFlip;
                    case 8: return AsBLongRequireDirectionFlip;
                    case 9: return AsCLongRequireDirectionFlip;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortRequireDirectionFlip;
                    case 2: return NyBShortRequireDirectionFlip;
                    case 3: return NyCShortRequireDirectionFlip;
                    case 4: return EuAShortRequireDirectionFlip;
                    case 5: return EuBShortRequireDirectionFlip;
                    case 6: return EuCShortRequireDirectionFlip;
                    case 7: return AsAShortRequireDirectionFlip;
                    case 8: return AsBShortRequireDirectionFlip;
                    case 9: return AsCShortRequireDirectionFlip;
                }
            }
            return default(bool);
        }

        private bool SD_AllowSameDirectionAfterLoss(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongAllowSameDirectionAfterLoss;
                    case 2: return NyBLongAllowSameDirectionAfterLoss;
                    case 3: return NyCLongAllowSameDirectionAfterLoss;
                    case 4: return EuALongAllowSameDirectionAfterLoss;
                    case 5: return EuBLongAllowSameDirectionAfterLoss;
                    case 6: return EuCLongAllowSameDirectionAfterLoss;
                    case 7: return AsALongAllowSameDirectionAfterLoss;
                    case 8: return AsBLongAllowSameDirectionAfterLoss;
                    case 9: return AsCLongAllowSameDirectionAfterLoss;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortAllowSameDirectionAfterLoss;
                    case 2: return NyBShortAllowSameDirectionAfterLoss;
                    case 3: return NyCShortAllowSameDirectionAfterLoss;
                    case 4: return EuAShortAllowSameDirectionAfterLoss;
                    case 5: return EuBShortAllowSameDirectionAfterLoss;
                    case 6: return EuCShortAllowSameDirectionAfterLoss;
                    case 7: return AsAShortAllowSameDirectionAfterLoss;
                    case 8: return AsBShortAllowSameDirectionAfterLoss;
                    case 9: return AsCShortAllowSameDirectionAfterLoss;
                }
            }
            return default(bool);
        }

        private int SD_MaxDistanceFromSmaTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return 0;
                    case 2: return 0;
                    case 3: return 0;
                    case 4: return 0;
                    case 5: return 0;
                    case 6: return 0;
                    case 7: return 0;
                    case 8: return 0;
                    case 9: return 0;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return 0;
                    case 2: return 0;
                    case 3: return 0;
                    case 4: return 0;
                    case 5: return 0;
                    case 6: return 0;
                    case 7: return 0;
                    case 8: return 0;
                    case 9: return 0;
                }
            }
            return default(int);
        }

        private bool SD_RequireSmaSlope(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongRequireSmaSlope;
                    case 2: return NyBLongRequireSmaSlope;
                    case 3: return NyCLongRequireSmaSlope;
                    case 4: return EuALongRequireSmaSlope;
                    case 5: return EuBLongRequireSmaSlope;
                    case 6: return EuCLongRequireSmaSlope;
                    case 7: return AsALongRequireSmaSlope;
                    case 8: return AsBLongRequireSmaSlope;
                    case 9: return AsCLongRequireSmaSlope;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortRequireSmaSlope;
                    case 2: return NyBShortRequireSmaSlope;
                    case 3: return NyCShortRequireSmaSlope;
                    case 4: return EuAShortRequireSmaSlope;
                    case 5: return EuBShortRequireSmaSlope;
                    case 6: return EuCShortRequireSmaSlope;
                    case 7: return AsAShortRequireSmaSlope;
                    case 8: return AsBShortRequireSmaSlope;
                    case 9: return AsCShortRequireSmaSlope;
                }
            }
            return default(bool);
        }

        private int SD_SmaSlopeLookback(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongSmaSlopeLookback;
                    case 2: return NyBLongSmaSlopeLookback;
                    case 3: return NyCLongSmaSlopeLookback;
                    case 4: return EuALongSmaSlopeLookback;
                    case 5: return EuBLongSmaSlopeLookback;
                    case 6: return EuCLongSmaSlopeLookback;
                    case 7: return AsALongSmaSlopeLookback;
                    case 8: return AsBLongSmaSlopeLookback;
                    case 9: return AsCLongSmaSlopeLookback;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortSmaSlopeLookback;
                    case 2: return NyBShortSmaSlopeLookback;
                    case 3: return NyCShortSmaSlopeLookback;
                    case 4: return EuAShortSmaSlopeLookback;
                    case 5: return EuBShortSmaSlopeLookback;
                    case 6: return EuCShortSmaSlopeLookback;
                    case 7: return AsAShortSmaSlopeLookback;
                    case 8: return AsBShortSmaSlopeLookback;
                    case 9: return AsCShortSmaSlopeLookback;
                }
            }
            return default(int);
        }

        private bool SD_EnableWmaFilter(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongEnableWmaFilter;
                    case 2: return NyBLongEnableWmaFilter;
                    case 3: return NyCLongEnableWmaFilter;
                    case 4: return EuALongEnableWmaFilter;
                    case 5: return EuBLongEnableWmaFilter;
                    case 6: return EuCLongEnableWmaFilter;
                    case 7: return AsALongEnableWmaFilter;
                    case 8: return AsBLongEnableWmaFilter;
                    case 9: return AsCLongEnableWmaFilter;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortEnableWmaFilter;
                    case 2: return NyBShortEnableWmaFilter;
                    case 3: return NyCShortEnableWmaFilter;
                    case 4: return EuAShortEnableWmaFilter;
                    case 5: return EuBShortEnableWmaFilter;
                    case 6: return EuCShortEnableWmaFilter;
                    case 7: return AsAShortEnableWmaFilter;
                    case 8: return AsBShortEnableWmaFilter;
                    case 9: return AsCShortEnableWmaFilter;
                }
            }
            return default(bool);
        }

        private int SD_WmaPeriod(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongWmaPeriod;
                    case 2: return NyBLongWmaPeriod;
                    case 3: return NyCLongWmaPeriod;
                    case 4: return EuALongWmaPeriod;
                    case 5: return EuBLongWmaPeriod;
                    case 6: return EuCLongWmaPeriod;
                    case 7: return AsALongWmaPeriod;
                    case 8: return AsBLongWmaPeriod;
                    case 9: return AsCLongWmaPeriod;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortWmaPeriod;
                    case 2: return NyBShortWmaPeriod;
                    case 3: return NyCShortWmaPeriod;
                    case 4: return EuAShortWmaPeriod;
                    case 5: return EuBShortWmaPeriod;
                    case 6: return EuCShortWmaPeriod;
                    case 7: return AsAShortWmaPeriod;
                    case 8: return AsBShortWmaPeriod;
                    case 9: return AsCShortWmaPeriod;
                }
            }
            return default(int);
        }

        private int SD_MaxSignalCandleTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return 0;
                    case 2: return 0;
                    case 3: return 0;
                    case 4: return 0;
                    case 5: return 0;
                    case 6: return 0;
                    case 7: return 0;
                    case 8: return 0;
                    case 9: return 0;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return 0;
                    case 2: return 0;
                    case 3: return 0;
                    case 4: return 0;
                    case 5: return 0;
                    case 6: return 0;
                    case 7: return 0;
                    case 8: return 0;
                    case 9: return 0;
                }
            }
            return default(int);
        }

        private double SD_MinBodyPct(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongMinBodyPct;
                    case 2: return NyBLongMinBodyPct;
                    case 3: return NyCLongMinBodyPct;
                    case 4: return EuALongMinBodyPct;
                    case 5: return EuBLongMinBodyPct;
                    case 6: return EuCLongMinBodyPct;
                    case 7: return AsALongMinBodyPct;
                    case 8: return AsBLongMinBodyPct;
                    case 9: return AsCLongMinBodyPct;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortMinBodyPct;
                    case 2: return NyBShortMinBodyPct;
                    case 3: return NyCShortMinBodyPct;
                    case 4: return EuAShortMinBodyPct;
                    case 5: return EuBShortMinBodyPct;
                    case 6: return EuCShortMinBodyPct;
                    case 7: return AsAShortMinBodyPct;
                    case 8: return AsBShortMinBodyPct;
                    case 9: return AsCShortMinBodyPct;
                }
            }
            return default(double);
        }

        private int SD_TrendConfirmBars(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongTrendConfirmBars;
                    case 2: return NyBLongTrendConfirmBars;
                    case 3: return NyCLongTrendConfirmBars;
                    case 4: return EuALongTrendConfirmBars;
                    case 5: return EuBLongTrendConfirmBars;
                    case 6: return EuCLongTrendConfirmBars;
                    case 7: return AsALongTrendConfirmBars;
                    case 8: return AsBLongTrendConfirmBars;
                    case 9: return AsCLongTrendConfirmBars;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortTrendConfirmBars;
                    case 2: return NyBShortTrendConfirmBars;
                    case 3: return NyCShortTrendConfirmBars;
                    case 4: return EuAShortTrendConfirmBars;
                    case 5: return EuBShortTrendConfirmBars;
                    case 6: return EuCShortTrendConfirmBars;
                    case 7: return AsAShortTrendConfirmBars;
                    case 8: return AsBShortTrendConfirmBars;
                    case 9: return AsCShortTrendConfirmBars;
                }
            }
            return default(int);
        }

        private bool SD_EnableAdxFilter(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongEnableAdxFilter;
                    case 2: return NyBLongEnableAdxFilter;
                    case 3: return NyCLongEnableAdxFilter;
                    case 4: return EuALongEnableAdxFilter;
                    case 5: return EuBLongEnableAdxFilter;
                    case 6: return EuCLongEnableAdxFilter;
                    case 7: return AsALongEnableAdxFilter;
                    case 8: return AsBLongEnableAdxFilter;
                    case 9: return AsCLongEnableAdxFilter;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortEnableAdxFilter;
                    case 2: return NyBShortEnableAdxFilter;
                    case 3: return NyCShortEnableAdxFilter;
                    case 4: return EuAShortEnableAdxFilter;
                    case 5: return EuBShortEnableAdxFilter;
                    case 6: return EuCShortEnableAdxFilter;
                    case 7: return AsAShortEnableAdxFilter;
                    case 8: return AsBShortEnableAdxFilter;
                    case 9: return AsCShortEnableAdxFilter;
                }
            }
            return default(bool);
        }

        private int SD_AdxPeriod(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongAdxPeriod;
                    case 2: return NyBLongAdxPeriod;
                    case 3: return NyCLongAdxPeriod;
                    case 4: return EuALongAdxPeriod;
                    case 5: return EuBLongAdxPeriod;
                    case 6: return EuCLongAdxPeriod;
                    case 7: return AsALongAdxPeriod;
                    case 8: return AsBLongAdxPeriod;
                    case 9: return AsCLongAdxPeriod;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortAdxPeriod;
                    case 2: return NyBShortAdxPeriod;
                    case 3: return NyCShortAdxPeriod;
                    case 4: return EuAShortAdxPeriod;
                    case 5: return EuBShortAdxPeriod;
                    case 6: return EuCShortAdxPeriod;
                    case 7: return AsAShortAdxPeriod;
                    case 8: return AsBShortAdxPeriod;
                    case 9: return AsCShortAdxPeriod;
                }
            }
            return default(int);
        }

        private int SD_AdxMinLevel(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongAdxMinLevel;
                    case 2: return NyBLongAdxMinLevel;
                    case 3: return NyCLongAdxMinLevel;
                    case 4: return EuALongAdxMinLevel;
                    case 5: return EuBLongAdxMinLevel;
                    case 6: return EuCLongAdxMinLevel;
                    case 7: return AsALongAdxMinLevel;
                    case 8: return AsBLongAdxMinLevel;
                    case 9: return AsCLongAdxMinLevel;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortAdxMinLevel;
                    case 2: return NyBShortAdxMinLevel;
                    case 3: return NyCShortAdxMinLevel;
                    case 4: return EuAShortAdxMinLevel;
                    case 5: return EuBShortAdxMinLevel;
                    case 6: return EuCShortAdxMinLevel;
                    case 7: return AsAShortAdxMinLevel;
                    case 8: return AsBShortAdxMinLevel;
                    case 9: return AsCShortAdxMinLevel;
                }
            }
            return default(int);
        }

        private bool SD_UseEmaEarlyExit(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongUseEmaEarlyExit;
                    case 2: return NyBLongUseEmaEarlyExit;
                    case 3: return NyCLongUseEmaEarlyExit;
                    case 4: return EuALongUseEmaEarlyExit;
                    case 5: return EuBLongUseEmaEarlyExit;
                    case 6: return EuCLongUseEmaEarlyExit;
                    case 7: return AsALongUseEmaEarlyExit;
                    case 8: return AsBLongUseEmaEarlyExit;
                    case 9: return AsCLongUseEmaEarlyExit;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortUseEmaEarlyExit;
                    case 2: return NyBShortUseEmaEarlyExit;
                    case 3: return NyCShortUseEmaEarlyExit;
                    case 4: return EuAShortUseEmaEarlyExit;
                    case 5: return EuBShortUseEmaEarlyExit;
                    case 6: return EuCShortUseEmaEarlyExit;
                    case 7: return AsAShortUseEmaEarlyExit;
                    case 8: return AsBShortUseEmaEarlyExit;
                    case 9: return AsCShortUseEmaEarlyExit;
                }
            }
            return default(bool);
        }

        private int SD_EmaEarlyExitPeriod(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongEmaEarlyExitPeriod;
                    case 2: return NyBLongEmaEarlyExitPeriod;
                    case 3: return NyCLongEmaEarlyExitPeriod;
                    case 4: return EuALongEmaEarlyExitPeriod;
                    case 5: return EuBLongEmaEarlyExitPeriod;
                    case 6: return EuCLongEmaEarlyExitPeriod;
                    case 7: return AsALongEmaEarlyExitPeriod;
                    case 8: return AsBLongEmaEarlyExitPeriod;
                    case 9: return AsCLongEmaEarlyExitPeriod;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortEmaEarlyExitPeriod;
                    case 2: return NyBShortEmaEarlyExitPeriod;
                    case 3: return NyCShortEmaEarlyExitPeriod;
                    case 4: return EuAShortEmaEarlyExitPeriod;
                    case 5: return EuBShortEmaEarlyExitPeriod;
                    case 6: return EuCShortEmaEarlyExitPeriod;
                    case 7: return AsAShortEmaEarlyExitPeriod;
                    case 8: return AsBShortEmaEarlyExitPeriod;
                    case 9: return AsCShortEmaEarlyExitPeriod;
                }
            }
            return default(int);
        }

        private int SD_EmaEarlyExitOffsetTicks(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return 0;
                    case 2: return 0;
                    case 3: return 0;
                    case 4: return 0;
                    case 5: return 0;
                    case 6: return 0;
                    case 7: return 0;
                    case 8: return 0;
                    case 9: return 0;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return 0;
                    case 2: return 0;
                    case 3: return 0;
                    case 4: return 0;
                    case 5: return 0;
                    case 6: return 0;
                    case 7: return 0;
                    case 8: return 0;
                    case 9: return 0;
                }
            }
            return default(int);
        }

        private bool SD_UseAdxEarlyExit(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongUseAdxEarlyExit;
                    case 2: return NyBLongUseAdxEarlyExit;
                    case 3: return NyCLongUseAdxEarlyExit;
                    case 4: return EuALongUseAdxEarlyExit;
                    case 5: return EuBLongUseAdxEarlyExit;
                    case 6: return EuCLongUseAdxEarlyExit;
                    case 7: return AsALongUseAdxEarlyExit;
                    case 8: return AsBLongUseAdxEarlyExit;
                    case 9: return AsCLongUseAdxEarlyExit;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortUseAdxEarlyExit;
                    case 2: return NyBShortUseAdxEarlyExit;
                    case 3: return NyCShortUseAdxEarlyExit;
                    case 4: return EuAShortUseAdxEarlyExit;
                    case 5: return EuBShortUseAdxEarlyExit;
                    case 6: return EuCShortUseAdxEarlyExit;
                    case 7: return AsAShortUseAdxEarlyExit;
                    case 8: return AsBShortUseAdxEarlyExit;
                    case 9: return AsCShortUseAdxEarlyExit;
                }
            }
            return default(bool);
        }

        private int SD_AdxEarlyExitPeriod(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongAdxEarlyExitPeriod;
                    case 2: return NyBLongAdxEarlyExitPeriod;
                    case 3: return NyCLongAdxEarlyExitPeriod;
                    case 4: return EuALongAdxEarlyExitPeriod;
                    case 5: return EuBLongAdxEarlyExitPeriod;
                    case 6: return EuCLongAdxEarlyExitPeriod;
                    case 7: return AsALongAdxEarlyExitPeriod;
                    case 8: return AsBLongAdxEarlyExitPeriod;
                    case 9: return AsCLongAdxEarlyExitPeriod;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortAdxEarlyExitPeriod;
                    case 2: return NyBShortAdxEarlyExitPeriod;
                    case 3: return NyCShortAdxEarlyExitPeriod;
                    case 4: return EuAShortAdxEarlyExitPeriod;
                    case 5: return EuBShortAdxEarlyExitPeriod;
                    case 6: return EuCShortAdxEarlyExitPeriod;
                    case 7: return AsAShortAdxEarlyExitPeriod;
                    case 8: return AsBShortAdxEarlyExitPeriod;
                    case 9: return AsCShortAdxEarlyExitPeriod;
                }
            }
            return default(int);
        }

        private double SD_AdxEarlyExitMin(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongAdxEarlyExitMin;
                    case 2: return NyBLongAdxEarlyExitMin;
                    case 3: return NyCLongAdxEarlyExitMin;
                    case 4: return EuALongAdxEarlyExitMin;
                    case 5: return EuBLongAdxEarlyExitMin;
                    case 6: return EuCLongAdxEarlyExitMin;
                    case 7: return AsALongAdxEarlyExitMin;
                    case 8: return AsBLongAdxEarlyExitMin;
                    case 9: return AsCLongAdxEarlyExitMin;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortAdxEarlyExitMin;
                    case 2: return NyBShortAdxEarlyExitMin;
                    case 3: return NyCShortAdxEarlyExitMin;
                    case 4: return EuAShortAdxEarlyExitMin;
                    case 5: return EuBShortAdxEarlyExitMin;
                    case 6: return EuCShortAdxEarlyExitMin;
                    case 7: return AsAShortAdxEarlyExitMin;
                    case 8: return AsBShortAdxEarlyExitMin;
                    case 9: return AsCShortAdxEarlyExitMin;
                }
            }
            return default(double);
        }

        private bool SD_UseAdxDrawdownExit(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongUseAdxDrawdownExit;
                    case 2: return NyBLongUseAdxDrawdownExit;
                    case 3: return NyCLongUseAdxDrawdownExit;
                    case 4: return EuALongUseAdxDrawdownExit;
                    case 5: return EuBLongUseAdxDrawdownExit;
                    case 6: return EuCLongUseAdxDrawdownExit;
                    case 7: return AsALongUseAdxDrawdownExit;
                    case 8: return AsBLongUseAdxDrawdownExit;
                    case 9: return AsCLongUseAdxDrawdownExit;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortUseAdxDrawdownExit;
                    case 2: return NyBShortUseAdxDrawdownExit;
                    case 3: return NyCShortUseAdxDrawdownExit;
                    case 4: return EuAShortUseAdxDrawdownExit;
                    case 5: return EuBShortUseAdxDrawdownExit;
                    case 6: return EuCShortUseAdxDrawdownExit;
                    case 7: return AsAShortUseAdxDrawdownExit;
                    case 8: return AsBShortUseAdxDrawdownExit;
                    case 9: return AsCShortUseAdxDrawdownExit;
                }
            }
            return default(bool);
        }

        private double SD_AdxDrawdownFromPeak(int sid, int dir)
        {
            if (dir == 1)
            {
                switch (sid)
                {
                    case 1: return NyALongAdxDrawdownFromPeak;
                    case 2: return NyBLongAdxDrawdownFromPeak;
                    case 3: return NyCLongAdxDrawdownFromPeak;
                    case 4: return EuALongAdxDrawdownFromPeak;
                    case 5: return EuBLongAdxDrawdownFromPeak;
                    case 6: return EuCLongAdxDrawdownFromPeak;
                    case 7: return AsALongAdxDrawdownFromPeak;
                    case 8: return AsBLongAdxDrawdownFromPeak;
                    case 9: return AsCLongAdxDrawdownFromPeak;
                }
            }
            else
            {
                switch (sid)
                {
                    case 1: return NyAShortAdxDrawdownFromPeak;
                    case 2: return NyBShortAdxDrawdownFromPeak;
                    case 3: return NyCShortAdxDrawdownFromPeak;
                    case 4: return EuAShortAdxDrawdownFromPeak;
                    case 5: return EuBShortAdxDrawdownFromPeak;
                    case 6: return EuCShortAdxDrawdownFromPeak;
                    case 7: return AsAShortAdxDrawdownFromPeak;
                    case 8: return AsBShortAdxDrawdownFromPeak;
                    case 9: return AsCShortAdxDrawdownFromPeak;
                }
            }
            return default(double);
        }

        // -- Session state access/mutators (PER-SUB-SESSION, 1..9 arrays) --
        private int S_GetTradeCount(int sid) { return (sid >= 1 && sid <= 9) ? subTradeCount[sid] : 0; }
        private int S_GetWinCount(int sid)   { return (sid >= 1 && sid <= 9) ? subWinCount[sid] : 0; }
        private int S_GetLossCount(int sid)  { return (sid >= 1 && sid <= 9) ? subLossCount[sid] : 0; }
        private double S_GetPnLTicks(int sid){ return (sid >= 1 && sid <= 9) ? subPnLTicks[sid] : 0.0; }
        private bool S_GetLimitsReached(int sid){ return (sid >= 1 && sid <= 9) ? subLimitsReached[sid] : false; }
        private int S_GetLastTradeDirection(int sid){ return (sid >= 1 && sid <= 9) ? subLastTradeDirection[sid] : 0; }
        private bool S_GetLastTradeWasLoss(int sid){ return (sid >= 1 && sid <= 9) ? subLastTradeWasLoss[sid] : false; }

        private void S_SetLimitsReached(int sid, bool val) { if (sid >= 1 && sid <= 9) subLimitsReached[sid] = val; }
        private void S_AddTradeResult(int sid, double pnlTicks)
        {
            if (sid < 1 || sid > 9) return;
            subTradeCount[sid]++;
            subPnLTicks[sid] += pnlTicks;
            if (pnlTicks > 0) subWinCount[sid]++; else subLossCount[sid]++;
            subLastTradeDirection[sid] = tradeDirection;
            subLastTradeWasLoss[sid] = pnlTicks <= 0;
        }

        private string S_Label(int sid)
        {
            switch (sid)
            {
                case 1: return "NY-A";  case 2: return "NY-B";  case 3: return "NY-C";
                case 4: return "EU-A";  case 5: return "EU-B";  case 6: return "EU-C";
                case 7: return "AS-A";  case 8: return "AS-B";  case 9: return "AS-C";
            }
            return "??";
        }


        #region MA Helpers — Session-dispatched

        private double GetLongUpperMA(int sid, int barsAgo)
        {
            MAMode mt = SD_MaType(sid, 1);
            switch (mt)
            {
                case MAMode.SMA:  return S_LongSmaHigh(sid)[barsAgo];
                case MAMode.EMA:  return S_LongEmaHigh(sid)[barsAgo];
                case MAMode.Both: return Math.Max(S_LongSmaHigh(sid)[barsAgo], S_LongEmaHigh(sid)[barsAgo]);
                default:          return S_LongSmaHigh(sid)[barsAgo];
            }
        }
        private double GetLongLowerMA(int sid, int barsAgo)
        {
            MAMode mt = SD_MaType(sid, 1);
            switch (mt)
            {
                case MAMode.SMA:  return S_LongSmaLow(sid)[barsAgo];
                case MAMode.EMA:  return S_LongEmaLow(sid)[barsAgo];
                case MAMode.Both: return Math.Min(S_LongSmaLow(sid)[barsAgo], S_LongEmaLow(sid)[barsAgo]);
                default:          return S_LongSmaLow(sid)[barsAgo];
            }
        }
        private double GetLongUpperMAForDistance(int sid)
        {
            MAMode mt = SD_MaType(sid, 1);
            switch (mt)
            {
                case MAMode.EMA:  return S_LongEmaHigh(sid)[0];
                case MAMode.Both: return Math.Max(S_LongSmaHigh(sid)[0], S_LongEmaHigh(sid)[0]);
                default:          return S_LongSmaHigh(sid)[0];
            }
        }
        private double GetLongLowerMAForTrail(int sid)
        {
            MAMode mt = SD_MaType(sid, 1);
            switch (mt)
            {
                case MAMode.EMA:  return S_LongEmaLow(sid)[0];
                case MAMode.Both: return Math.Min(S_LongSmaLow(sid)[0], S_LongEmaLow(sid)[0]);
                default:          return S_LongSmaLow(sid)[0];
            }
        }
        private bool IsLongSlopeRising(int sid)
        {
            int lookback = SD_SmaSlopeLookback(sid, 1);
            if (CurrentBar <= lookback) return false;
            return GetLongUpperMA(sid, 0) > GetLongUpperMA(sid, lookback) && GetLongLowerMA(sid, 0) > GetLongLowerMA(sid, lookback);
        }

        private double GetShortUpperMA(int sid, int barsAgo)
        {
            MAMode mt = SD_MaType(sid, -1);
            switch (mt)
            {
                case MAMode.SMA:  return S_ShortSmaHigh(sid)[barsAgo];
                case MAMode.EMA:  return S_ShortEmaHigh(sid)[barsAgo];
                case MAMode.Both: return Math.Max(S_ShortSmaHigh(sid)[barsAgo], S_ShortEmaHigh(sid)[barsAgo]);
                default:          return S_ShortSmaHigh(sid)[barsAgo];
            }
        }
        private double GetShortLowerMA(int sid, int barsAgo)
        {
            MAMode mt = SD_MaType(sid, -1);
            switch (mt)
            {
                case MAMode.SMA:  return S_ShortSmaLow(sid)[barsAgo];
                case MAMode.EMA:  return S_ShortEmaLow(sid)[barsAgo];
                case MAMode.Both: return Math.Min(S_ShortSmaLow(sid)[barsAgo], S_ShortEmaLow(sid)[barsAgo]);
                default:          return S_ShortSmaLow(sid)[barsAgo];
            }
        }
        private double GetShortLowerMAForDistance(int sid)
        {
            MAMode mt = SD_MaType(sid, -1);
            switch (mt)
            {
                case MAMode.EMA:  return S_ShortEmaLow(sid)[0];
                case MAMode.Both: return Math.Min(S_ShortSmaLow(sid)[0], S_ShortEmaLow(sid)[0]);
                default:          return S_ShortSmaLow(sid)[0];
            }
        }
        private double GetShortUpperMAForTrail(int sid)
        {
            MAMode mt = SD_MaType(sid, -1);
            switch (mt)
            {
                case MAMode.EMA:  return S_ShortEmaHigh(sid)[0];
                case MAMode.Both: return Math.Max(S_ShortSmaHigh(sid)[0], S_ShortEmaHigh(sid)[0]);
                default:          return S_ShortSmaHigh(sid)[0];
            }
        }
        private bool IsShortSlopeFalling(int sid)
        {
            int lookback = SD_SmaSlopeLookback(sid, -1);
            if (CurrentBar <= lookback) return false;
            return GetShortUpperMA(sid, 0) < GetShortUpperMA(sid, lookback) && GetShortLowerMA(sid, 0) < GetShortLowerMA(sid, lookback);
        }

        // Direction-based (uses activeSessionId + tradeDirection)
        private double GetUpperMAForTrail()  => tradeDirection == 1 ? GetLongUpperMAForDistance(activeSessionId) : GetShortUpperMAForTrail(activeSessionId);
        private double GetLowerMAForTrail()  => tradeDirection == 1 ? GetLongLowerMAForTrail(activeSessionId)   : GetShortLowerMAForDistance(activeSessionId);
        private double GetUpperMA(int b)     => tradeDirection == 1 ? GetLongUpperMA(activeSessionId, b)        : GetShortUpperMA(activeSessionId, b);
        private double GetLowerMA(int b)     => tradeDirection == 1 ? GetLongLowerMA(activeSessionId, b)        : GetShortLowerMA(activeSessionId, b);

        #endregion


        #region Session Determination

        /// <summary>Determine which sub-session (if any) is eligible for entries at the given bar time.</summary>
        private int DetermineEntrySession()
        {
            // Check each active sub-session (1..9). Sub-sessions don't overlap so at most one matches.
            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (S_Active(sid) && !subLimitsReached[sid] && IsInSessionTradeWindow(sid))
                    return sid;
            }
            return 0;
        }

        private bool IsInSessionTradeWindow(int sid)
        {
            double barSM   = ToSessionMinutes(Time[0].TimeOfDay);
            double startSM = ToSessionMinutes(S_TradeWindowStart(sid).TimeOfDay);

            if (barSM < startSM) return false;

            // No-new-trades-after boundary: if toggle ON, use configured time; if OFF, use forced close as the outer bound
            double endSM = S_EnableNoNewTradesAfter(sid)
                ? ToSessionMinutes(S_NoNewTradesAfter(sid).TimeOfDay)
                : ToSessionMinutes(S_ForcedCloseTime(sid).TimeOfDay);
            if (barSM >= endSM) return false;

            if (IsInNewsSkipWindow(Time[0])) return false;
            return true;
        }

        private bool IsInAnyForcedCloseWindow()
        {
            double barSM = ToSessionMinutes(Time[0].TimeOfDay);
            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (!S_Active(sid) || !S_EnableForcedClose(sid)) continue;
                double closeSM = ToSessionMinutes(S_ForcedCloseTime(sid).TimeOfDay);
                if (barSM >= closeSM && barSM < closeSM + 10) return true;
            }
            return false;
        }

        private bool IsInSessionForcedClose(int sid)
        {
            if (!S_EnableForcedClose(sid)) return false;
            double barSM   = ToSessionMinutes(Time[0].TimeOfDay);
            double closeSM = ToSessionMinutes(S_ForcedCloseTime(sid).TimeOfDay);
            return barSM >= closeSM;
        }

        #endregion


        protected override void OnBarUpdate()
        {
            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
            {
                CancelWorkingEntryOrder();
                if (Position.MarketPosition != MarketPosition.Flat)
                    FlattenAndCancel("InvalidConfiguration");
                return;
            }

            // Compute minBars from all enabled sessions
            int minBars = 20;
            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (!S_Active(sid)) continue;
                minBars = Math.Max(minBars, Math.Max(SD_MaPeriod(sid, 1) + 2,  SD_MaPeriod(sid, -1) + 2));
                minBars = Math.Max(minBars, Math.Max(SD_EmaPeriod(sid, 1) + 2, SD_EmaPeriod(sid, -1) + 2));
                minBars = Math.Max(minBars, Math.Max(SD_SmaSlopeLookback(sid, 1) + 1, SD_SmaSlopeLookback(sid, -1) + 1));
                minBars = Math.Max(minBars, Math.Max(SD_WmaPeriod(sid, 1) + 1,  SD_WmaPeriod(sid, -1) + 1));
                minBars = Math.Max(minBars, Math.Max(SD_AdxPeriod(sid, 1) + 2,  SD_AdxPeriod(sid, -1) + 2));
                minBars = Math.Max(minBars, Math.Max(SD_TrendConfirmBars(sid, 1) + 1, SD_TrendConfirmBars(sid, -1) + 1));
            }
            if (CurrentBar < minBars) return;

            // ─── Session Reset ───
            CheckSessionReset();

            // ─── Chart Drawings ───
            DrawSessionTimeWindows();
            UpdateInfoBox();

            // ─── Blocked window transitions ───
            HandleTimeWindowTransitions();

            if (forceFlattenInProgress)
            {
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    if (hasActivePosition)
                        HandlePositionClosed();
                    else
                    {
                        forceFlattenInProgress = false;
                        forceFlattenOrderSubmitted = false;
                        forceFlattenReason = null;
                    }
                }
                else
                {
                    TrySubmitForceFlattenExit();
                }
                prevMarketPosition = Position.MarketPosition;
                return;
            }

            if (IsAccountBalanceBlocked())
            {
                prevMarketPosition = Position.MarketPosition;
                return;
            }

            // ─── Forced Close (check active session) ───
            if (activeSessionId > 0 && Position.MarketPosition != MarketPosition.Flat && IsInSessionForcedClose(activeSessionId))
            {
                FlattenAndCancel("Forced close");
                prevMarketPosition = Position.MarketPosition;
                return;
            }

            // ─── Position Change Detection ───
            if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat)
                HandlePositionClosed();

            if (Position.MarketPosition != MarketPosition.Flat && !hasActivePosition && tradeEntryPrice > 0)
                hasActivePosition = true;

            // ─── Session Limits (active session) ───
            if (activeSessionId > 0 && S_GetLimitsReached(activeSessionId) && Position.MarketPosition == MarketPosition.Flat)
            {
                prevMarketPosition = Position.MarketPosition;
                return;
            }

            // ─── In-Position Management ───
            if (Position.MarketPosition != MarketPosition.Flat && hasActivePosition)
            {
                barsSinceEntry++;
                int sid = activeSessionId;

                int maxBars = SD_MaxBarsInTrade(sid, tradeDirection);
                if (maxBars > 0 && barsSinceEntry >= maxBars)
                {
                    Print(string.Format("{0} | [{1}] MAX TIME EXIT: {2} bars (limit={3})", Time[0], S_Label(sid), barsSinceEntry, maxBars));
                    FlattenAndCancel("Max time in trade");
                    if (Position.MarketPosition == MarketPosition.Flat && hasActivePosition)
                        HandlePositionClosed();
                }

                bool engulfEnabled = SD_EnableEngulfingExit(sid, tradeDirection);
                if (engulfEnabled && hasActivePosition && Position.MarketPosition != MarketPosition.Flat)
                    CheckEngulfingExit();

                bool oppBarEnabled = SD_EnableOpposingBarExit(sid, tradeDirection);
                if (oppBarEnabled && hasActivePosition && Position.MarketPosition != MarketPosition.Flat)
                    CheckOpposingBarExit();

                if (hasActivePosition && Position.MarketPosition != MarketPosition.Flat)
                {
                    ManageBreakeven();
                    ManageEntryBarSl();
                    ManageTrailingStop();
                    ManageCandleLagTrail();
                    ManagePriceOffsetTrail();
                    ManageRMultipleTrail();
                }

                if (hasActivePosition && Position.MarketPosition != MarketPosition.Flat)
                    CheckEarlyExits();

                if (Position.MarketPosition == MarketPosition.Flat && hasActivePosition)
                {
                    Print(string.Format("{0} | Position went FLAT during bar (SL/TP filled)", Time[0]));
                    HandlePositionClosed();
                }
            }

            // ─── Entry Signal ───
            if (Position.MarketPosition == MarketPosition.Flat && !hasActivePosition)
            {
                int entrySid = DetermineEntrySession();
                if (entrySid > 0)
                    CheckForEntrySignal(entrySid);
            }

            prevMarketPosition = Position.MarketPosition;
        }


        #region Position Change Detection

        private void HandlePositionClosed()
        {
            int sid = activeSessionId;
            if (sid == 0) sid = 1; // fallback

            if (SystemPerformance != null && SystemPerformance.AllTrades.Count > 0)
            {
                Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                double pnlTicks = lastTrade.ProfitTicks;
                S_AddTradeResult(sid, pnlTicks);
                Print(string.Format("{0} | [{1}] CLOSED {2} | Dir={3} | PnL={4:F1}t",
                    Time[0], S_Label(sid), pnlTicks > 0 ? "PROFIT" : "LOSS",
                    tradeDirection == 1 ? "LONG" : "SHORT", pnlTicks));
            }
            else
            {
                double pnlTicks = tradeDirection == 1
                    ? (Close[0] - tradeEntryPrice) / TickSize
                    : (tradeEntryPrice - Close[0]) / TickSize;
                S_AddTradeResult(sid, pnlTicks);
            }

            hasActivePosition           = false;
            tradeDirection              = 0;
            signalCandleRange           = 0;
            priorCandleHigh             = 0;
            priorCandleLow              = 0;
            originalStopPrice           = 0;
            tradeEntryPrice             = 0;
            barsSinceEntry              = 0;
            breakEvenApplied            = false;
            opposingBarBenchmark        = 0;
            opposingBarBenchmarkSet     = false;
            priceOffsetTrailActive      = false;
            priceOffsetTrailDistance     = 0;
            rMultipleSlSize              = 0;
            rMultipleBestPrice           = 0;
            rMultipleActivated           = false;
            rMultipleLocked              = false;
            entryBarSlApplied           = false;
            entryOrder                  = null;
            stopOrder                   = null;
            targetOrder                 = null;
            forceFlattenInProgress      = false;
            forceFlattenOrderSubmitted  = false;
            forceFlattenReason          = null;
            tradePeakAdx                = 0.0;
            entryAtr                    = 0;
            projectXLastSyncedStopPrice = 0.0;
            projectXLastSyncedTargetPrice = 0.0;

            CheckSessionLimitsInternal(sid);
            activeSessionId = 0;
        }

        #endregion


        private string GetEntrySignalName(int direction) { return direction == 1 ? LongEntrySignal : ShortEntrySignal; }
        private string BuildExitSignalName(string reason) { return StrategySignalPrefix + reason; }


        #region Entry Fill

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order == null) return;
            string orderName = execution.Order.Name;
            int executionQty = Math.Abs(quantity);
            int entryOrderQty = execution.Order.Quantity > 0 ? execution.Order.Quantity : Math.Max(1, executionQty);

            if ((orderName == LongEntrySignal || orderName == ShortEntrySignal)
                && execution.Order.OrderState == OrderState.Filled
                && !hasActivePosition)
            {
                int sid = activeSessionId;
                tradeEntryPrice             = execution.Price;
                hasActivePosition           = true;
                barsSinceEntry              = 0;
                breakEvenApplied            = false;
                tradeEntryTime              = time;
                opposingBarBenchmark        = 0;
                opposingBarBenchmarkSet     = false;
                priceOffsetTrailActive      = false;
                priceOffsetTrailDistance     = 0;
                entryBarSlApplied           = false;

                ADX earlyAdx = tradeDirection == 1 ? S_LongEarlyExitAdx(sid) : S_ShortEarlyExitAdx(sid);
                int earlyAdxPeriod = SD_AdxEarlyExitPeriod(sid, tradeDirection);
                tradePeakAdx = (earlyAdx != null && CurrentBar >= earlyAdxPeriod) ? earlyAdx[0] : 0.0;

                // ── Capture ATR at entry bar close (must be before CalculateTakeProfit) ──
                ATR entryAtrInd = S_Atr(sid, tradeDirection);
                entryAtr = (entryAtrInd != null && CurrentBar >= SD_AtrPeriod(sid, tradeDirection)) ? entryAtrInd[0] : 0;

                int contracts = S_Contracts(sid);
                string entryName = GetEntrySignalName(tradeDirection);
                double tpPrice = CalculateTakeProfit(tradeEntryPrice);
                double slPrice = CalculateStopLoss();

                if (tradeDirection == 1)
                {
                    if (tpPrice <= tradeEntryPrice) tpPrice = tradeEntryPrice + SD_TakeProfitTicks(sid, 1) * TickSize;
                    if (slPrice >= tradeEntryPrice) slPrice = tradeEntryPrice - 4 * TickSize;
                    slPrice = ClampStopToMAE(slPrice, true);
                    targetOrder = ExitLongLimit(0, true, contracts, tpPrice, BuildExitSignalName("TP"), entryName);
                    stopOrder = ExitLongStopMarket(0, true, contracts, slPrice, BuildExitSignalName("SL"), entryName);
                }
                else
                {
                    if (tpPrice >= tradeEntryPrice) tpPrice = tradeEntryPrice - SD_TakeProfitTicks(sid, -1) * TickSize;
                    if (slPrice <= tradeEntryPrice) slPrice = tradeEntryPrice + 4 * TickSize;
                    slPrice = ClampStopToMAE(slPrice, false);
                    targetOrder = ExitShortLimit(0, true, contracts, tpPrice, BuildExitSignalName("TP"), entryName);
                    stopOrder = ExitShortStopMarket(0, true, contracts, slPrice, BuildExitSignalName("SL"), entryName);
                }

                originalStopPrice = slPrice;
                string entryWebhookAction;
                bool isMarketEntry;
                if (TryGetEntryWebhookAction(execution, out entryWebhookAction, out isMarketEntry))
                {
                    bool entryWebhookSent = SendWebhook(entryWebhookAction, tradeEntryPrice, tpPrice, slPrice, isMarketEntry, entryOrderQty);
                    if (WebhookProviderType == WebhookProvider.ProjectX && entryWebhookSent)
                    {
                        projectXLastSyncedStopPrice = RoundToInstrumentTick(slPrice);
                        projectXLastSyncedTargetPrice = RoundToInstrumentTick(tpPrice);
                    }
                }

                // ── R-Multiple trail initialisation ──
                rMultipleSlSize      = Math.Abs(tradeEntryPrice - slPrice);
                rMultipleBestPrice   = tradeEntryPrice;
                rMultipleActivated   = false;
                rMultipleLocked      = false;
                Print(string.Format("{0} | [{1}] FILLED {2} @ {3:F2} | TP={4:F2} | SL={5:F2}", time, S_Label(sid), tradeDirection == 1 ? "LONG" : "SHORT", tradeEntryPrice, tpPrice, slPrice));
            }

            if (marketPosition == MarketPosition.Flat)
            {
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
            }

            if (ShouldSendExitWebhook(execution, orderName, marketPosition))
            {
                if (WebhookProviderType == WebhookProvider.ProjectX && suppressProjectXNextExecutionExitWebhook)
                {
                    suppressProjectXNextExecutionExitWebhook = false;
                    LogDebug(string.Format(
                        "ProjectX execution exit suppressed | order={0} qty={1} posAfter={2}",
                        orderName,
                        executionQty,
                        marketPosition));
                }
                else
                {
                    SendWebhook("exit", 0, 0, 0, true, executionQty);
                }
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            string targetSignalName = BuildExitSignalName("TP");
            string stopSignalName = BuildExitSignalName("SL");

            if (order.Name == targetSignalName)
            {
                if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected || orderState == OrderState.Filled)
                    targetOrder = null;
                else
                    targetOrder = order;
            }
            else if (order.Name == stopSignalName)
            {
                if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected || orderState == OrderState.Filled)
                    stopOrder = null;
                else
                    stopOrder = order;
            }

            TrySyncProjectXProtectiveOrder(order, limitPrice, stopPrice, orderState);

            if (entryOrder != null && order == entryOrder
                && (orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
            {
                SendWebhook("cancel");
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
                entryOrder = null;
                hasActivePosition = false;
                Print(string.Format("{0} | Entry {1}: {2}", time, orderState, nativeError));
            }

            if (forceFlattenInProgress
                && Position.MarketPosition != MarketPosition.Flat
                && (orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
                TrySubmitForceFlattenExit();
        }

        #endregion


        #region Early Exit Checks

        private void CheckEarlyExits()
        {
            if (tradeEntryPrice == 0) return;
            int sid = activeSessionId;
            int contracts = S_Contracts(sid);

            bool inLoss = tradeDirection == 1 ? Close[0] < tradeEntryPrice : Close[0] > tradeEntryPrice;

            bool useEma        = SD_UseEmaEarlyExit(sid, tradeDirection);
            EMA earlyEma       = tradeDirection == 1 ? S_LongEarlyExitEma(sid) : S_ShortEarlyExitEma(sid);
            int emaPeriod      = SD_EmaEarlyExitPeriod(sid, tradeDirection);
            int emaOffsetTicks = SD_EmaEarlyExitOffsetTicks(sid, tradeDirection);

            bool useAdx        = SD_UseAdxEarlyExit(sid, tradeDirection);
            bool useAdxDd      = SD_UseAdxDrawdownExit(sid, tradeDirection);
            ADX earlyAdx       = tradeDirection == 1 ? S_LongEarlyExitAdx(sid) : S_ShortEarlyExitAdx(sid);
            int adxPeriod      = SD_AdxEarlyExitPeriod(sid, tradeDirection);
            double adxMin      = SD_AdxEarlyExitMin(sid, tradeDirection);
            double adxDdPeak   = SD_AdxDrawdownFromPeak(sid, tradeDirection);

            if (useEma && earlyEma != null && CurrentBar >= emaPeriod)
            {
                double ema = earlyEma[0];
                double offset = emaOffsetTicks * TickSize;
                if (tradeDirection == 1 && inLoss && Close[0] <= ema - offset)
                {
                    Print(string.Format("{0} | [{1}] EMA EARLY EXIT (Long)", Time[0], S_Label(sid)));
                    FlattenAndCancel("EMA Early Exit");
                    if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat) HandlePositionClosed();
                    return;
                }
                if (tradeDirection == -1 && inLoss && Close[0] >= ema + offset)
                {
                    Print(string.Format("{0} | [{1}] EMA EARLY EXIT (Short)", Time[0], S_Label(sid)));
                    FlattenAndCancel("EMA Early Exit");
                    if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat) HandlePositionClosed();
                    return;
                }
            }

            if ((useAdx || useAdxDd) && earlyAdx != null && CurrentBar >= adxPeriod)
            {
                double adx = earlyAdx[0];
                if (adx > tradePeakAdx) tradePeakAdx = adx;

                if (useAdx && inLoss && adxMin > 0.0 && adx < adxMin)
                {
                    Print(string.Format("{0} | [{1}] ADX EARLY EXIT: ADX={2:F1} < {3:F1}", Time[0], S_Label(sid), adx, adxMin));
                    FlattenAndCancel("ADX Early Exit");
                    if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat) HandlePositionClosed();
                    return;
                }
                if (useAdxDd && inLoss && tradePeakAdx > 0.0 && (tradePeakAdx - adx) >= adxDdPeak)
                {
                    Print(string.Format("{0} | [{1}] ADX DRAWDOWN EXIT: ADX={2:F1} dropped {3:F1} from peak={4:F1}", Time[0], S_Label(sid), adx, tradePeakAdx - adx, tradePeakAdx));
                    FlattenAndCancel("ADX Drawdown Exit");
                    if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat) HandlePositionClosed();
                    return;
                }
            }
        }

        #endregion


        #region Webhooks

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

        private bool IsProjectXProtectiveOrderActiveState(OrderState orderState)
        {
            return orderState == OrderState.Submitted
                || orderState == OrderState.Accepted
                || orderState == OrderState.Working
                || orderState == OrderState.PartFilled;
        }

        private double RoundToInstrumentTick(double price)
        {
            return Instrument != null && Instrument.MasterInstrument != null
                ? Instrument.MasterInstrument.RoundToTickSize(price)
                : price;
        }

        private bool ArePricesEquivalent(double left, double right)
        {
            if (left <= 0.0 || right <= 0.0)
                return false;

            return Math.Abs(left - right) <= TickSize * 0.5;
        }

        private void TrySyncProjectXProtectiveOrder(Order order, double limitPrice, double stopPrice, OrderState orderState)
        {
            if (State != State.Realtime || WebhookProviderType != WebhookProvider.ProjectX)
                return;
            if (order == null || Position.MarketPosition == MarketPosition.Flat)
                return;
            if (!IsProjectXProtectiveOrderActiveState(orderState))
                return;

            string orderName = order.Name ?? string.Empty;
            string targetSignalName = BuildExitSignalName("TP");
            string stopSignalName = BuildExitSignalName("SL");

            if (string.Equals(orderName, stopSignalName, StringComparison.Ordinal))
            {
                double actualStopPrice = stopPrice > 0.0 ? RoundToInstrumentTick(stopPrice) : RoundToInstrumentTick(order.StopPrice);
                if (actualStopPrice <= 0.0 || ArePricesEquivalent(projectXLastSyncedStopPrice, actualStopPrice))
                    return;

                if (SyncProjectXProtectivePrice(actualStopPrice, true))
                    projectXLastSyncedStopPrice = actualStopPrice;
            }
            else if (string.Equals(orderName, targetSignalName, StringComparison.Ordinal))
            {
                double actualTargetPrice = limitPrice > 0.0 ? RoundToInstrumentTick(limitPrice) : RoundToInstrumentTick(order.LimitPrice);
                if (actualTargetPrice <= 0.0 || ArePricesEquivalent(projectXLastSyncedTargetPrice, actualTargetPrice))
                    return;

                if (SyncProjectXProtectivePrice(actualTargetPrice, false))
                    projectXLastSyncedTargetPrice = actualTargetPrice;
            }
        }

        private bool SyncProjectXProtectivePrice(double price, bool isStopOrder)
        {
            if (price <= 0.0)
                return false;
            if (!EnsureProjectXSession())
                return false;

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetProjectXTargets(out targetAccounts, out contractId))
                return false;

            bool modifiedAny = false;
            foreach (var account in targetAccounts)
            {
                try
                {
                    if (ProjectXModifyProtectiveOrders(account.Id, contractId, price, isStopOrder))
                        modifiedAny = true;
                }
                catch (Exception ex)
                {
                    LogDebug(string.Format(
                        "ProjectX protective sync error | kind={0} accountId={1} contractId={2} price={3:0.00} error={4}",
                        isStopOrder ? "stop" : "target",
                        account.Id,
                        contractId,
                        price,
                        ex.Message));
                }
            }

            return modifiedAny;
        }

        private bool ProjectXModifyProtectiveOrders(int accountId, string contractId, double price, bool isStopOrder)
        {
            string searchJson = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string searchResponse = ProjectXPost("/api/Order/searchOpen", searchJson, true);
            bool success;
            if (TryGetJsonBool(searchResponse, "success", out success) && !success)
                return false;

            int expectedSide = Position.MarketPosition == MarketPosition.Long ? 1 : 0;
            int expectedType = isStopOrder ? 4 : 1;
            bool modifiedAny = false;

            foreach (var order in ExtractProjectXOrders(searchResponse))
            {
                object contractObj;
                if (!order.TryGetValue("contractId", out contractObj))
                    continue;
                if (!string.Equals(contractObj != null ? contractObj.ToString() : string.Empty, contractId, StringComparison.OrdinalIgnoreCase))
                    continue;

                object typeObj;
                int type;
                if (!order.TryGetValue("type", out typeObj) || !TryConvertToInt(typeObj, out type) || type != expectedType)
                    continue;

                object sideObj;
                int side;
                if (!order.TryGetValue("side", out sideObj) || !TryConvertToInt(sideObj, out side) || side != expectedSide)
                    continue;

                object idObj;
                long orderId;
                if (!order.TryGetValue("id", out idObj) || !TryConvertToLong(idObj, out orderId) || orderId <= 0)
                    continue;

                object sizeObj;
                int size;
                if (!order.TryGetValue("size", out sizeObj) || !TryConvertToInt(sizeObj, out size) || size <= 0)
                    size = Math.Max(1, Position.Quantity);

                object existingPriceObj;
                double existingPrice;
                if (isStopOrder)
                {
                    if (order.TryGetValue("stopPrice", out existingPriceObj)
                        && existingPriceObj != null
                        && double.TryParse(existingPriceObj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out existingPrice)
                        && ArePricesEquivalent(RoundToInstrumentTick(existingPrice), price))
                        continue;
                }
                else
                {
                    if (order.TryGetValue("limitPrice", out existingPriceObj)
                        && existingPriceObj != null
                        && double.TryParse(existingPriceObj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out existingPrice)
                        && ArePricesEquivalent(RoundToInstrumentTick(existingPrice), price))
                        continue;
                }

                string response = ProjectXModifyOrder(accountId, orderId, size, isStopOrder ? (double?)null : price, isStopOrder ? (double?)price : null);
                if (!string.IsNullOrWhiteSpace(response))
                    modifiedAny = true;
            }

            if (!modifiedAny)
            {
                LogDebug(string.Format(
                    "ProjectX protective sync skipped | kind={0} accountId={1} contractId={2} price={3:0.00} reason=no-open-order",
                    isStopOrder ? "stop" : "target",
                    accountId,
                    contractId,
                    price));
            }

            return modifiedAny;
        }

        private string ProjectXModifyOrder(int accountId, long orderId, int size, double? limitPrice, double? stopPrice)
        {
            string json = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"orderId\":{1},\"size\":{2},\"limitPrice\":{3},\"stopPrice\":{4},\"trailPrice\":null}}",
                accountId,
                orderId,
                size > 0 ? size.ToString(CultureInfo.InvariantCulture) : "null",
                limitPrice.HasValue ? limitPrice.Value.ToString(CultureInfo.InvariantCulture) : "null",
                stopPrice.HasValue ? stopPrice.Value.ToString(CultureInfo.InvariantCulture) : "null");
            return ProjectXPost("/api/Order/modify", json, true);
        }

        private bool ShouldSendExitWebhook(Execution execution, string orderName, MarketPosition marketPosition)
        {
            if (execution == null || execution.Order == null)
                return false;

            if (orderName == LongEntrySignal || orderName == ShortEntrySignal)
                return false;

            string fromEntry = execution.Order.FromEntrySignal ?? string.Empty;
            if (fromEntry == LongEntrySignal || fromEntry == ShortEntrySignal)
                return true;

            string normalized = orderName ?? string.Empty;
            if (normalized.Length == 0)
                return marketPosition == MarketPosition.Flat;

            return normalized.StartsWith(StrategySignalPrefix, StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Stop loss", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Profit target", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Exit on session close", StringComparison.OrdinalIgnoreCase)
                || marketPosition == MarketPosition.Flat;
        }

        private bool TryGetEntryWebhookAction(Execution execution, out string action, out bool isMarketEntry)
        {
            action = null;
            isMarketEntry = false;

            if (execution == null || execution.Order == null)
                return false;

            string orderName = execution.Order.Name ?? string.Empty;
            if (orderName != LongEntrySignal && orderName != ShortEntrySignal)
                return false;

            action = orderName == LongEntrySignal ? "buy" : "sell";
            OrderType orderType = execution.Order.OrderType;
            isMarketEntry = orderType == OrderType.Market || orderType == OrderType.StopMarket;
            return true;
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
                int orderQtyForProvider = quantityOverride > 0 ? quantityOverride : GetDefaultWebhookQuantity();
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
                int orderQty = quantityOverride > 0 ? quantityOverride : GetDefaultWebhookQuantity();
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
                return false;

            List<ProjectXAccountInfo> accounts;
            if (!TryLoadProjectXAccounts(out accounts))
                return false;

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

        private int GetDefaultWebhookQuantity()
        {
            if (activeSessionId >= 1 && activeSessionId <= SubSessionCount)
                return Math.Max(1, S_Contracts(activeSessionId));

            if (Position.Quantity > 0)
                return Math.Max(1, Position.Quantity);

            return Math.Max(1, NyAContracts);
        }

        #endregion


        #region Time Window Helpers

        private double ToSessionMinutes(TimeSpan timeOfDay)
        {
            double sessionStartMin = SessionStartTime.TimeOfDay.TotalMinutes;
            double minutes = timeOfDay.TotalMinutes - sessionStartMin;
            if (minutes < 0) minutes += 1440.0;
            return minutes;
        }

        private bool IsInNewsSkipWindow(DateTime time)
        {
            if (!UseNewsSkip) return false;
            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime newsTime = NewsDates[i];
                if (newsTime.Date != time.Date) continue;
                DateTime windowStart = newsTime.AddMinutes(-NewsBlockMinutes);
                DateTime windowEnd = newsTime.AddMinutes(NewsBlockMinutes);
                if (time >= windowStart && time < windowEnd) return true;
            }
            return false;
        }


        private void ValidateRequiredPrimaryTimeframe(int requiredMinutes)
        {
            bool isMinuteSeries = BarsPeriod != null && BarsPeriod.BarsPeriodType == NinjaTrader.Data.BarsPeriodType.Minute;
            bool timeframeMatches = isMinuteSeries && BarsPeriod.Value == requiredMinutes;
            isConfiguredTimeframeValid = timeframeMatches;
            if (timeframeMatches) return;
            string actualTimeframe = BarsPeriod == null ? "Unknown" : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", BarsPeriod.Value, BarsPeriod.BarsPeriodType);
            string message = string.Format(CultureInfo.InvariantCulture, "{0} must run on a {1}-minute chart. Current chart is {2}.", Name, requiredMinutes, actualTimeframe);
            Print("Timeframe validation failed | " + message);
            if (!timeframePopupShown) { timeframePopupShown = true; try { if (System.Windows.Application.Current != null) System.Windows.Application.Current.Dispatcher.Invoke(() => { System.Windows.MessageBox.Show(message, "Invalid Timeframe", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); }); } catch (Exception ex) { Print("Failed to show popup: " + ex.Message); } }
        }

        private void ValidateRequiredPrimaryInstrument()
        {
            string instrumentName = Instrument != null && Instrument.MasterInstrument != null ? (Instrument.MasterInstrument.Name ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;
            bool instrumentMatches = instrumentName == "NQ" || instrumentName == "MNQ";
            isConfiguredInstrumentValid = instrumentMatches;
            if (instrumentMatches) return;
            string actualInstrument = string.IsNullOrWhiteSpace(instrumentName) ? "Unknown" : instrumentName;
            string message = string.Format(CultureInfo.InvariantCulture, "{0} must run on NQ or MNQ. Current instrument is {1}.", Name, actualInstrument);
            Print("Instrument validation failed | " + message);
            if (!instrumentPopupShown) { instrumentPopupShown = true; try { if (System.Windows.Application.Current != null) System.Windows.Application.Current.Dispatcher.Invoke(() => { System.Windows.MessageBox.Show(message, "Invalid Instrument", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning); }); } catch (Exception ex) { Print("Failed to show popup: " + ex.Message); } }
        }

        private bool ShowEntryConfirmation(string orderType, double price, int quantity)
        {
            if (State != State.Realtime) return true;
            bool result = false;
            if (System.Windows.Application.Current == null) return false;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                string message = string.Format(CultureInfo.InvariantCulture, "Confirm {0} entry?\nPrice: {1:F2}\nQuantity: {2}", orderType, price, quantity);
                System.Windows.MessageBoxResult res = System.Windows.MessageBox.Show(message, "Entry Confirmation", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                result = res == System.Windows.MessageBoxResult.Yes;
            });
            return result;
        }

        #endregion


        #region Entry Signal Detection

        private void CheckForEntrySignal(int sid)
        {
            if (S_GetLimitsReached(sid)) return;

            double currentClose = Close[0];
            double currentOpen  = Open[0];
            double currentHigh  = High[0];
            double currentLow   = Low[0];
            double prevClose    = Close[1];
            double prevOpen     = Open[1];
            double prevHigh     = High[1];
            double prevLow      = Low[1];

            bool isBullish = currentClose > currentOpen;
            bool isBearish = currentClose < currentOpen;

            if (S_EnableLongTrades(sid) && isBullish && IsDirectionAllowed(sid, 1))
                TryLongSignal(sid, currentClose, currentOpen, currentHigh, currentLow, prevClose, prevOpen, prevHigh, prevLow);
            else if (S_EnableShortTrades(sid) && isBearish && IsDirectionAllowed(sid, -1))
                TryShortSignal(sid, currentClose, currentOpen, currentHigh, currentLow, prevClose, prevOpen, prevHigh, prevLow);
        }

        private void TryLongSignal(int sid, double currentClose, double currentOpen, double currentHigh, double currentLow,
                                    double prevClose, double prevOpen, double prevHigh, double prevLow)
        {
            double upperCurr = GetLongUpperMA(sid, 0);
            double lowerCurr = GetLongLowerMA(sid, 0);
            double upperPrev = GetLongUpperMA(sid, 1);
            double lowerPrev = GetLongLowerMA(sid, 1);

            if (!(currentClose > upperCurr && currentClose > lowerCurr
               && currentClose > upperPrev && currentClose > lowerPrev
               && currentOpen  > lowerCurr
               && currentClose > prevOpen  && currentClose > prevClose))
                return;

            if (SD_MaxSignalCandleTicks(sid, 1) > 0)
            {
                double rangeTicks = (currentHigh - currentLow) / TickSize;
                if (rangeTicks > SD_MaxSignalCandleTicks(sid, 1)) return;
            }
            double candleRange = currentHigh - currentLow;
            if (SD_MinBodyPct(sid, 1) > 0 && candleRange > 0)
            {
                double bodyPct = (Math.Abs(currentClose - currentOpen) / candleRange) * 100.0;
                if (bodyPct < SD_MinBodyPct(sid, 1)) return;
            }
            if (SD_MaxDistanceFromSmaTicks(sid, 1) > 0)
            {
                double distTicks = (currentClose - GetLongUpperMAForDistance(sid)) / TickSize;
                if (distTicks > SD_MaxDistanceFromSmaTicks(sid, 1)) return;
            }
            if (SD_RequireSmaSlope(sid, 1) && !IsLongSlopeRising(sid)) return;
            if (SD_EnableWmaFilter(sid, 1) && currentClose <= S_LongWmaFilter(sid)[0]) return;
            if (SD_TrendConfirmBars(sid, 1) > 0 && currentClose <= Open[SD_TrendConfirmBars(sid, 1)]) return;
            if (SD_EnableAdxFilter(sid, 1) && S_LongAdxIndicator(sid)[0] < SD_AdxMinLevel(sid, 1)) return;

            SubmitEntry(sid, 1, currentClose, currentHigh, currentLow, prevHigh, prevLow);
        }

        private void TryShortSignal(int sid, double currentClose, double currentOpen, double currentHigh, double currentLow,
                                     double prevClose, double prevOpen, double prevHigh, double prevLow)
        {
            double upperCurr = GetShortUpperMA(sid, 0);
            double lowerCurr = GetShortLowerMA(sid, 0);
            double upperPrev = GetShortUpperMA(sid, 1);
            double lowerPrev = GetShortLowerMA(sid, 1);

            if (!(currentClose < upperCurr && currentClose < lowerCurr
               && currentClose < upperPrev && currentClose < lowerPrev
               && currentOpen  < upperCurr
               && currentClose < prevOpen  && currentClose < prevClose))
                return;

            if (SD_MaxSignalCandleTicks(sid, -1) > 0)
            {
                double rangeTicks = (currentHigh - currentLow) / TickSize;
                if (rangeTicks > SD_MaxSignalCandleTicks(sid, -1)) return;
            }
            double candleRange = currentHigh - currentLow;
            if (SD_MinBodyPct(sid, -1) > 0 && candleRange > 0)
            {
                double bodyPct = (Math.Abs(currentClose - currentOpen) / candleRange) * 100.0;
                if (bodyPct < SD_MinBodyPct(sid, -1)) return;
            }
            if (SD_MaxDistanceFromSmaTicks(sid, -1) > 0)
            {
                double distTicks = (GetShortLowerMAForDistance(sid) - currentClose) / TickSize;
                if (distTicks > SD_MaxDistanceFromSmaTicks(sid, -1)) return;
            }
            if (SD_RequireSmaSlope(sid, -1) && !IsShortSlopeFalling(sid)) return;
            if (SD_EnableWmaFilter(sid, -1) && currentClose >= S_ShortWmaFilter(sid)[0]) return;
            if (SD_TrendConfirmBars(sid, -1) > 0 && currentClose >= Open[SD_TrendConfirmBars(sid, -1)]) return;
            if (SD_EnableAdxFilter(sid, -1) && S_ShortAdxIndicator(sid)[0] < SD_AdxMinLevel(sid, -1)) return;

            SubmitEntry(sid, -1, currentClose, currentHigh, currentLow, prevHigh, prevLow);
        }

        private bool IsDirectionAllowed(int sid, int direction)
        {
            int lastDir = S_GetLastTradeDirection(sid);
            if (lastDir == 0) return true;
            bool requireFlip       = SD_RequireDirectionFlip(sid, direction);
            bool allowSameAfterLoss = SD_AllowSameDirectionAfterLoss(sid, direction);
            if (!requireFlip) return true;
            if (lastDir == direction)
            {
                if (allowSameAfterLoss && S_GetLastTradeWasLoss(sid)) return true;
                return false;
            }
            return true;
        }

        private void SubmitEntry(int sid, int direction, double closePrice, double candleHigh, double candleLow, double prevHigh, double prevLow)
        {
            double candleRange = candleHigh - candleLow;
            int    slExtraTicks    = SD_SlExtraTicks(sid, direction);
            int    maxSlTicks      = SD_MaxSlTicks(sid, direction);
            double maxSlToTpRatio  = SD_MaxSlToTpRatio(sid, direction);

            double slPrice = direction == 1
                ? prevLow  - slExtraTicks * TickSize
                : prevHigh + slExtraTicks * TickSize;
            double slDistanceTicks = Math.Abs(closePrice - slPrice) / TickSize;

            if (maxSlTicks > 0 && slDistanceTicks > maxSlTicks) return;
            if (maxSlToTpRatio > 0)
            {
                double tpDistanceTicks = CalculateTPDistanceTicks(sid, direction, candleRange);
                if (tpDistanceTicks > 0 && slDistanceTicks / tpDistanceTicks > maxSlToTpRatio) return;
            }

            activeSessionId   = sid;
            tradeDirection    = direction;
            signalCandleRange = candleRange;
            priorCandleHigh   = prevHigh;
            priorCandleLow    = prevLow;

            int contracts = S_Contracts(sid);
            string entryName = GetEntrySignalName(direction);
            MichalEntryMode entryMode = SD_EntryMode(sid, direction);
            int limitOffsetTicks = SD_LimitOffsetTicks(sid, direction);
            double limitRetracePct = SD_LimitRetracementPct(sid, direction);
            bool useMarketEntry = entryMode == MichalEntryMode.Market;
            double plannedEntryPrice = closePrice;

            if (entryMode == MichalEntryMode.LimitOffset)
            {
                plannedEntryPrice = direction == 1 ? closePrice - limitOffsetTicks * TickSize : closePrice + limitOffsetTicks * TickSize;
            }
            else if (entryMode == MichalEntryMode.LimitRetracement)
            {
                double retrace = signalCandleRange * (limitRetracePct / 100.0);
                plannedEntryPrice = direction == 1 ? closePrice - retrace : closePrice + retrace;
            }

            if (RequireEntryConfirmation && !ShowEntryConfirmation(direction == 1 ? "Long" : "Short", plannedEntryPrice, contracts))
                return;

            double plannedTpPrice = CalculateTakeProfit(plannedEntryPrice);
            double plannedSlPrice = CalculateStopLoss();

            if (direction == 1)
            {
                if (plannedTpPrice <= plannedEntryPrice)
                    plannedTpPrice = plannedEntryPrice + SD_TakeProfitTicks(sid, 1) * TickSize;
                if (plannedSlPrice >= plannedEntryPrice)
                    plannedSlPrice = plannedEntryPrice - 4 * TickSize;
                plannedSlPrice = ClampStopToMAEForEntryPrice(plannedSlPrice, true, plannedEntryPrice);
            }
            else
            {
                if (plannedTpPrice >= plannedEntryPrice)
                    plannedTpPrice = plannedEntryPrice - SD_TakeProfitTicks(sid, -1) * TickSize;
                if (plannedSlPrice <= plannedEntryPrice)
                    plannedSlPrice = plannedEntryPrice + 4 * TickSize;
                plannedSlPrice = ClampStopToMAEForEntryPrice(plannedSlPrice, false, plannedEntryPrice);
            }

            if (entryMode == MichalEntryMode.Market)
                entryOrder = direction == 1 ? EnterLong(contracts, entryName) : EnterShort(contracts, entryName);
            else
                entryOrder = direction == 1
                    ? EnterLongLimit(0, true, contracts, plannedEntryPrice, entryName)
                    : EnterShortLimit(0, true, contracts, plannedEntryPrice, entryName);

            Print(string.Format("{0} | [{1}] SIGNAL {2} | Close={3:F2}", Time[0], S_Label(sid), direction == 1 ? "LONG" : "SHORT", closePrice));
        }

        #endregion


        #region Take Profit

        private double GetAtrAwareCandleMultiplier(int sid, int dir)
        {
            int    atrThresholdTicks = SD_AtrThresholdTicks(sid, dir);
            double lowMult           = SD_LowAtrCandleMultiplier(sid, dir);
            double highMult          = SD_HighAtrCandleMultiplier(sid, dir);
            // If threshold is 0 or ATR was not captured, fall back to standard multiplier
            if (atrThresholdTicks <= 0 || entryAtr <= 0)
                return SD_CandleMultiplier(sid, dir);
            double atrInTicks = entryAtr / TickSize;
            return atrInTicks < atrThresholdTicks ? lowMult : highMult;
        }

        private double CalculateTPDistanceTicks(int sid, int direction, double candleRange)
        {
            MichalTPMode mode = SD_TakeProfitMode(sid, direction);
            int tpTix = SD_TakeProfitTicks(sid, direction);
            int maxTp = SD_MaxTakeProfitTicks(sid, direction);
            double mult = GetAtrAwareCandleMultiplier(sid, direction);
            double tpTicks;
            switch (mode)
            {
                case MichalTPMode.CandleMultiple: tpTicks = (candleRange * mult) / TickSize; break;
                default: tpTicks = tpTix; break;
            }
            if (maxTp > 0 && tpTicks > maxTp) tpTicks = maxTp;
            return tpTicks;
        }

        private double CalculateTakeProfit(double fillPrice)
        {
            int sid = activeSessionId;
            MichalTPMode mode = SD_TakeProfitMode(sid, tradeDirection);
            int tpTix = SD_TakeProfitTicks(sid, tradeDirection);
            int maxTp = SD_MaxTakeProfitTicks(sid, tradeDirection);
            double mult = GetAtrAwareCandleMultiplier(sid, tradeDirection);
            double tpDistance;
            switch (mode)
            {
                case MichalTPMode.FixedTicks:     tpDistance = tpTix * TickSize; break;
                case MichalTPMode.SwingPoint:     return GetSwingTarget(fillPrice);
                case MichalTPMode.CandleMultiple: tpDistance = signalCandleRange * mult; break;
                default:                          tpDistance = tpTix * TickSize; break;
            }
            if (maxTp > 0)
            {
                double maxTpDist = maxTp * TickSize;
                if (tpDistance > maxTpDist) tpDistance = maxTpDist;
            }
            return tradeDirection == 1 ? fillPrice + tpDistance : fillPrice - tpDistance;
        }

        private double GetSwingTarget(double fillPrice)
        {
            int sid = activeSessionId;
            Swing sw = tradeDirection == 1 ? S_LongSwing(sid) : S_ShortSwing(sid);
            int fallbackTicks = SD_TakeProfitTicks(sid, tradeDirection);
            int swingStr = SD_SwingStrength(sid, tradeDirection);
            if (tradeDirection == 1)
            {
                for (int i = 0; i < CurrentBar - swingStr; i++)
                {
                    double swHigh = sw.SwingHigh[i];
                    if (swHigh > 0 && swHigh > fillPrice) return swHigh;
                }
                return fillPrice + fallbackTicks * TickSize;
            }
            else
            {
                for (int i = 0; i < CurrentBar - swingStr; i++)
                {
                    double swLow = sw.SwingLow[i];
                    if (swLow > 0 && swLow < fillPrice) return swLow;
                }
                return fillPrice - fallbackTicks * TickSize;
            }
        }

        #endregion


        #region Stop Loss, Breakeven, and Trailing

        private double CalculateStopLoss()
        {
            int sid = activeSessionId;
            bool slAtMa = SD_SlAtMa(sid, tradeDirection);
            bool usePriorCandle = SD_UsePriorCandleSl(sid, tradeDirection);
            int slExtraTicks = SD_SlExtraTicks(sid, tradeDirection);
            double slPrice;
            if (slAtMa)
            {
                slPrice = tradeDirection == 1
                    ? GetLongLowerMA(sid, 0) - slExtraTicks * TickSize
                    : GetShortUpperMA(sid, 0) + slExtraTicks * TickSize;
            }
            else if (usePriorCandle)
            {
                double twoBarsLow  = CurrentBar >= 2 ? Low[2]  : priorCandleLow;
                double twoBarsHigh = CurrentBar >= 2 ? High[2] : priorCandleHigh;
                slPrice = tradeDirection == 1
                    ? Math.Min(priorCandleLow, twoBarsLow)   - slExtraTicks * TickSize
                    : Math.Max(priorCandleHigh, twoBarsHigh) + slExtraTicks * TickSize;
            }
            else
            {
                slPrice = tradeDirection == 1
                    ? priorCandleLow  - slExtraTicks * TickSize
                    : priorCandleHigh + slExtraTicks * TickSize;
            }
            return slPrice;
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

        private double ClampStopToMAE(double proposedStop, bool isLong)
        {
            return ClampStopToMAEForEntryPrice(proposedStop, isLong, tradeEntryPrice);
        }

        private double ClampStopToMAEForEntryPrice(double proposedStop, bool isLong, double referenceEntryPrice)
        {
            if (referenceEntryPrice == 0) return proposedStop;
            int maeMaxTicks = SD_MAEMaxTicks(activeSessionId, tradeDirection);
            if (maeMaxTicks <= 0) return proposedStop;
            double maeLimit = maeMaxTicks * TickSize;
            if (isLong)
            {
                // For longs: stop can't be lower than entry - MAEMaxTicks
                double maeFloor = Instrument.MasterInstrument.RoundToTickSize(referenceEntryPrice - maeLimit);
                if (proposedStop < maeFloor)
                {
                    Print(string.Format("{0} | [{1}] MAE CLAMP LONG: {2:F2} -> {3:F2} ({4}t floor)",
                        Time[0], S_Label(activeSessionId), proposedStop, maeFloor, maeMaxTicks));
                    return maeFloor;
                }
            }
            else
            {
                // For shorts: stop can't be higher than entry + MAEMaxTicks
                double maeCeiling = Instrument.MasterInstrument.RoundToTickSize(referenceEntryPrice + maeLimit);
                if (proposedStop > maeCeiling)
                {
                    Print(string.Format("{0} | [{1}] MAE CLAMP SHORT: {2:F2} -> {3:F2} ({4}t ceiling)",
                        Time[0], S_Label(activeSessionId), proposedStop, maeCeiling, maeMaxTicks));
                    return maeCeiling;
                }
            }
            return proposedStop;
        }

        private void ManageBreakeven()
        {
            int sid = activeSessionId;
            int contracts = S_Contracts(sid);
            bool enabled = SD_EnableBreakeven(sid, tradeDirection);
            BEMode2 mode = SD_BreakevenMode(sid, tradeDirection);
            int triggerTicks = SD_BreakevenTriggerTicks(sid, tradeDirection);
            double candlePct = SD_BreakevenCandlePct(sid, tradeDirection);
            int offsetTicks = SD_BreakevenOffsetTicks(sid, tradeDirection);
            if (!enabled || breakEvenApplied || tradeEntryPrice == 0) return;
            double triggerDistance = mode == BEMode2.FixedTicks ? triggerTicks * TickSize : signalCandleRange * (candlePct / 100.0);
            bool triggered = false;
            if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
                triggered = High[0] >= tradeEntryPrice + triggerDistance;
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
                triggered = Low[0] <= tradeEntryPrice - triggerDistance;
            if (triggered)
            {
                if (tradeDirection == 1)
                {
                    double bePrice = Instrument.MasterInstrument.RoundToTickSize(tradeEntryPrice + offsetTicks * TickSize);
                    bePrice = ClampStopToMAE(bePrice, true);
                    if (bePrice > originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long, bePrice))
                    {
                        ExitLongStopMarket(0, true, contracts, bePrice, BuildExitSignalName("SL"), LongEntrySignal);
                        originalStopPrice = bePrice;
                        breakEvenApplied = true;
                    }
                }
                else
                {
                    double bePrice = Instrument.MasterInstrument.RoundToTickSize(tradeEntryPrice - offsetTicks * TickSize);
                    bePrice = ClampStopToMAE(bePrice, false);
                    if (bePrice < originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short, bePrice))
                    {
                        ExitShortStopMarket(0, true, contracts, bePrice, BuildExitSignalName("SL"), ShortEntrySignal);
                        originalStopPrice = bePrice;
                        breakEvenApplied = true;
                    }
                }
            }
        }

        private void ManageTrailingStop()
        {
            int sid = activeSessionId;
            int contracts = S_Contracts(sid);
            int trailDelayBars = SD_TrailDelayBars(sid, tradeDirection);
            int trailOffsetTicks = SD_TrailOffsetTicks(sid, tradeDirection);
            if (barsSinceEntry < trailDelayBars) return;
            if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                double newStop = GetLongLowerMAForTrail(sid) - trailOffsetTicks * TickSize;
                newStop = Instrument.MasterInstrument.RoundToTickSize(newStop);
                newStop = ClampStopToMAE(newStop, true);
                if (newStop > originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long, newStop))
                {
                    ExitLongStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), LongEntrySignal);
                    originalStopPrice = newStop;
                }
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                double newStop = GetShortUpperMAForTrail(sid) + trailOffsetTicks * TickSize;
                newStop = Instrument.MasterInstrument.RoundToTickSize(newStop);
                newStop = ClampStopToMAE(newStop, false);
                if (newStop < originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short, newStop))
                {
                    ExitShortStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), ShortEntrySignal);
                    originalStopPrice = newStop;
                }
            }
        }

        private void ManageEntryBarSl()
        {
            int sid = activeSessionId;
            int contracts = S_Contracts(sid);
            bool moveSlEnabled = SD_MoveSlToEntryBar(sid, tradeDirection);
            int slExtraTicks = SD_SlExtraTicks(sid, tradeDirection);
            if (!moveSlEnabled || entryBarSlApplied || barsSinceEntry != 1) return;
            entryBarSlApplied = true;
            bool entryBarBullish = Close[1] > Open[1];
            bool entryBarBearish = Close[1] < Open[1];
            if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long && entryBarBullish)
            {
                double newSl = Instrument.MasterInstrument.RoundToTickSize(Low[1] - slExtraTicks * TickSize);
                newSl = ClampStopToMAE(newSl, true);
                if (newSl > originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long, newSl))
                { ExitLongStopMarket(0, true, contracts, newSl, BuildExitSignalName("SL"), LongEntrySignal); originalStopPrice = newSl; }
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short && entryBarBearish)
            {
                double newSl = Instrument.MasterInstrument.RoundToTickSize(High[1] + slExtraTicks * TickSize);
                newSl = ClampStopToMAE(newSl, false);
                if (newSl < originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short, newSl))
                { ExitShortStopMarket(0, true, contracts, newSl, BuildExitSignalName("SL"), ShortEntrySignal); originalStopPrice = newSl; }
            }
        }

        private void ManageCandleLagTrail()
        {
            int sid = activeSessionId;
            int contracts = S_Contracts(sid);
            int trailCandleOffset = SD_TrailCandleOffset(sid, tradeDirection);
            int trailDelayBars = SD_TrailDelayBars(sid, tradeDirection);
            int slExtraTicks = SD_SlExtraTicks(sid, tradeDirection);
            if (trailCandleOffset <= 0 || barsSinceEntry < trailCandleOffset + trailDelayBars) return;
            if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                double newStop = Instrument.MasterInstrument.RoundToTickSize(Low[trailCandleOffset] - slExtraTicks * TickSize);
                newStop = ClampStopToMAE(newStop, true);
                if (newStop > originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long, newStop))
                {
                    ExitLongStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), LongEntrySignal);
                    originalStopPrice = newStop;
                }
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                double newStop = Instrument.MasterInstrument.RoundToTickSize(High[trailCandleOffset] + slExtraTicks * TickSize);
                newStop = ClampStopToMAE(newStop, false);
                if (newStop < originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short, newStop))
                {
                    ExitShortStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), ShortEntrySignal);
                    originalStopPrice = newStop;
                }
            }
        }

        #endregion


        #region Engulfing Bar Exit

        private void CheckEngulfingExit()
        {
            int contracts = S_Contracts(activeSessionId);
            double currentOpen  = Open[0]; double currentClose = Close[0];
            double prevOpen     = Open[1]; double prevClose    = Close[1];
            double prevBodyHigh = Math.Max(prevOpen, prevClose);
            double prevBodyLow  = Math.Min(prevOpen, prevClose);
            if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                if (currentClose > currentOpen && currentClose > prevBodyHigh && currentOpen <= prevBodyLow)
                    ExitShort(Math.Max(1, Position.Quantity), BuildExitSignalName("EngulfExit"), ShortEntrySignal);
            }
            else if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                if (currentClose < currentOpen && currentClose < prevBodyLow && currentOpen >= prevBodyHigh)
                    ExitLong(Math.Max(1, Position.Quantity), BuildExitSignalName("EngulfExit"), LongEntrySignal);
            }
        }

        #endregion


        #region Price-Offset Trailing

        private void ManagePriceOffsetTrail()
        {
            int sid = activeSessionId;
            int contracts = S_Contracts(sid);
            bool enabled = SD_EnablePriceOffsetTrail(sid, tradeDirection);
            int reductionTicks = SD_PriceOffsetReductionTicks(sid, tradeDirection);
            if (!enabled || tradeEntryPrice == 0) return;
            if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                double lowerMA = GetLongLowerMAForTrail(sid);
                if (!priceOffsetTrailActive && lowerMA > originalStopPrice)
                {
                    priceOffsetTrailDistance = Close[0] - originalStopPrice - (reductionTicks * TickSize);
                    if (priceOffsetTrailDistance < TickSize) priceOffsetTrailDistance = TickSize;
                    priceOffsetTrailActive = true;
                }
                if (priceOffsetTrailActive)
                {
                    double newStop = Instrument.MasterInstrument.RoundToTickSize(Close[0] - priceOffsetTrailDistance);
                    newStop = ClampStopToMAE(newStop, true);
                    if (newStop > originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long, newStop))
                    {
                        ExitLongStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), LongEntrySignal);
                        originalStopPrice = newStop;
                    }
                }
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                double upperMA = GetShortUpperMAForTrail(sid);
                if (!priceOffsetTrailActive && upperMA < originalStopPrice)
                {
                    priceOffsetTrailDistance = originalStopPrice - Close[0] - (reductionTicks * TickSize);
                    if (priceOffsetTrailDistance < TickSize) priceOffsetTrailDistance = TickSize;
                    priceOffsetTrailActive = true;
                }
                if (priceOffsetTrailActive)
                {
                    double newStop = Instrument.MasterInstrument.RoundToTickSize(Close[0] + priceOffsetTrailDistance);
                    newStop = ClampStopToMAE(newStop, false);
                    if (newStop < originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short, newStop))
                    {
                        ExitShortStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), ShortEntrySignal);
                        originalStopPrice = newStop;
                    }
                }
            }
        }

        private void ManageRMultipleTrail()
        {
            int sid = activeSessionId;
            int contracts = S_Contracts(sid);
            bool enabled = SD_EnableRMultipleTrail(sid, tradeDirection);
            if (!enabled || tradeEntryPrice == 0 || rMultipleSlSize <= 0) return;

            bool isLong  = tradeDirection == 1  && Position.MarketPosition == MarketPosition.Long;
            bool isShort = tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short;
            if (!isLong && !isShort) return;

            // Update High/Low watermark
            if (isLong)
            {
                if (High[0] > rMultipleBestPrice) rMultipleBestPrice = High[0];
            }
            else
            {
                if (rMultipleBestPrice <= 0 || Low[0] < rMultipleBestPrice) rMultipleBestPrice = Low[0];
            }

            double profit = isLong ? rMultipleBestPrice - tradeEntryPrice
                                   : tradeEntryPrice - rMultipleBestPrice;

            // B) Activation: wait until profit >= ActivationPct % of initial SL size
            double activationPct = SD_RMultipleActivationPct(sid, tradeDirection);
            if (!rMultipleActivated)
            {
                if (activationPct > 0 && profit < rMultipleSlSize * (activationPct / 100.0)) return;
                rMultipleActivated = true;
            }

            // C) Lock: once profit >= LockPct % of initial SL size, freeze the stop
            double lockPct = SD_RMultipleLockPct(sid, tradeDirection);
            if (lockPct > 0 && profit >= rMultipleSlSize * (lockPct / 100.0))
            {
                rMultipleLocked = true;
                return;
            }
            if (rMultipleLocked) return;

            // Trail: stop trails at trailPct % of SL size behind bestPrice
            double trailPct = SD_RMultipleTrailPct(sid, tradeDirection);
            if (trailPct <= 0) return;
            double trailDist = rMultipleSlSize * (trailPct / 100.0);

            if (isLong)
            {
                double newStop = Instrument.MasterInstrument.RoundToTickSize(rMultipleBestPrice - trailDist);
                newStop = ClampStopToMAE(newStop, true);
                if (newStop > originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long, newStop))
                {
                    ExitLongStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), LongEntrySignal);
                    originalStopPrice = newStop;
                }
            }
            else
            {
                double newStop = Instrument.MasterInstrument.RoundToTickSize(rMultipleBestPrice + trailDist);
                newStop = ClampStopToMAE(newStop, false);
                if (newStop < originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short, newStop))
                {
                    ExitShortStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), ShortEntrySignal);
                    originalStopPrice = newStop;
                }
            }
        }

        #endregion


        #region Opposing Bar Exit

        private void CheckOpposingBarExit()
        {
            bool isBullish = Close[0] > Open[0];
            bool isBearish = Close[0] < Open[0];
            if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                if (isBullish)
                {
                    if (!opposingBarBenchmarkSet) { opposingBarBenchmark = Close[0]; opposingBarBenchmarkSet = true; }
                    else if (Close[0] > opposingBarBenchmark)
                        ExitShort(Math.Max(1, Position.Quantity), BuildExitSignalName("OppBarExit"), ShortEntrySignal);
                }
            }
            else if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                if (isBearish)
                {
                    if (!opposingBarBenchmarkSet) { opposingBarBenchmark = Close[0]; opposingBarBenchmarkSet = true; }
                    else if (Close[0] < opposingBarBenchmark)
                        ExitLong(Math.Max(1, Position.Quantity), BuildExitSignalName("OppBarExit"), LongEntrySignal);
                }
            }
        }

        #endregion


        #region Session Management

        private void CheckSessionReset()
        {
            TimeSpan barTOD = Time[0].TimeOfDay;
            TimeSpan sessionStartTOD = SessionStartTime.TimeOfDay;
            DateTime sessionDate = barTOD >= sessionStartTOD ? Time[0].Date : Time[0].Date.AddDays(-1);
            if (sessionDate != currentSessionDate)
            {
                if (Position.MarketPosition != MarketPosition.Flat) FlattenAndCancel("Session rollover");
                currentSessionDate = sessionDate;
                ResetAll();
                Print(string.Format("{0} | ═══ NEW SESSION ═══ {1:yyyy-MM-dd}", Time[0], sessionDate));
            }
        }

        private void ResetAll()
        {
            // Legacy per-parent state (retained but no longer the source of truth)
            nySessionTradeCount = 0; nySessionWinCount = 0; nySessionLossCount = 0; nySessionPnLTicks = 0; nySessionLimitsReached = false; nyLastTradeDirection = 0; nyLastTradeWasLoss = false; nyWasInNoTradesAfterWindow = false;
            euSessionTradeCount = 0; euSessionWinCount = 0; euSessionLossCount = 0; euSessionPnLTicks = 0; euSessionLimitsReached = false; euLastTradeDirection = 0; euLastTradeWasLoss = false; euWasInNoTradesAfterWindow = false;
            asSessionTradeCount = 0; asSessionWinCount = 0; asSessionLossCount = 0; asSessionPnLTicks = 0; asSessionLimitsReached = false; asLastTradeDirection = 0; asLastTradeWasLoss = false; asWasInNoTradesAfterWindow = false;

            // v1 — reset per-sub-session state
            for (int i = 1; i <= SubSessionCount; i++)
            {
                subTradeCount[i] = 0;
                subWinCount[i] = 0;
                subLossCount[i] = 0;
                subPnLTicks[i] = 0.0;
                subLimitsReached[i] = false;
                subLastTradeDirection[i] = 0;
                subLastTradeWasLoss[i] = false;
                subWasInNoTradesAfterWindow[i] = false;
            }

            activeSessionId = 0;
            tradeDirection = 0; signalCandleRange = 0; priorCandleHigh = 0; priorCandleLow = 0;
            originalStopPrice = 0; tradeEntryPrice = 0; hasActivePosition = false;
            barsSinceEntry = 0; breakEvenApplied = false; opposingBarBenchmark = 0; opposingBarBenchmarkSet = false;
            priceOffsetTrailActive = false; priceOffsetTrailDistance = 0; entryBarSlApplied = false;
            entryOrder = null; prevMarketPosition = MarketPosition.Flat; tradePeakAdx = 0.0;
            wasInNewsSkipWindow = false;
        }

        private void CheckSessionLimitsInternal(int sid)
        {
            if (S_GetLimitsReached(sid)) return;
            string reason = "";
            int tc = S_GetTradeCount(sid); int wc = S_GetWinCount(sid); int lc = S_GetLossCount(sid); double pnl = S_GetPnLTicks(sid);
            if (tc >= S_MaxTradesPerSession(sid)) reason = string.Format("Max trades ({0})", S_MaxTradesPerSession(sid));
            else if (S_MaxWinsPerSession(sid) > 0 && wc >= S_MaxWinsPerSession(sid)) reason = string.Format("Max wins ({0})", S_MaxWinsPerSession(sid));
            else if (S_MaxLossesPerSession(sid) > 0 && lc >= S_MaxLossesPerSession(sid)) reason = string.Format("Max losses ({0})", S_MaxLossesPerSession(sid));
            else if (pnl >= S_MaxSessionProfitTicks(sid)) reason = string.Format("Max profit ({0:F1}t)", pnl);
            else if (pnl <= -S_MaxSessionLossTicks(sid)) reason = string.Format("Max loss ({0:F1}t)", pnl);
            if (!string.IsNullOrEmpty(reason))
            {
                S_SetLimitsReached(sid, true);
                Print(string.Format("{0} | [{1}] *** SESSION LIMIT: {2} ***", Time[0], S_Label(sid), reason));
            }
        }

        private void HandleTimeWindowTransitions()
        {
            // Per-sub-session transitions (1..9)
            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (!S_Active(sid)) continue;

                bool inNoTrades = S_EnableNoNewTradesAfter(sid)
                    && ToSessionMinutes(Time[0].TimeOfDay) >= ToSessionMinutes(S_NoNewTradesAfter(sid).TimeOfDay);

                bool wasNoTrades = subWasInNoTradesAfterWindow[sid];

                if (FlattenOnBlockedWindowTransition && activeSessionId == sid)
                {
                    if (!wasNoTrades && inNoTrades) FlattenAndCancel("NoTradesAfter");
                }

                subWasInNoTradesAfterWindow[sid] = inNoTrades;
            }

            // News skip (global)
            bool inNewsSkip = IsInNewsSkipWindow(Time[0]);
            if (FlattenOnBlockedWindowTransition && !wasInNewsSkipWindow && inNewsSkip)
                FlattenAndCancel("NewsSkip");
            wasInNewsSkipWindow = inNewsSkip;
        }

        private void CancelWorkingEntryOrder()
        {
            if (entryOrder == null) return;
            try
            {
                if (entryOrder.OrderState == OrderState.Working || entryOrder.OrderState == OrderState.Accepted || entryOrder.OrderState == OrderState.Submitted || entryOrder.OrderState == OrderState.PartFilled)
                    CancelOrder(entryOrder);
            }
            catch { }
        }

        private bool IsOrderCancelable(Order order)
        {
            if (order == null) return false;
            return order.OrderState == OrderState.Working
                || order.OrderState == OrderState.Accepted
                || order.OrderState == OrderState.Submitted
                || order.OrderState == OrderState.PartFilled;
        }

        private void CancelWorkingProtectionOrders()
        {
            try
            {
                if (IsOrderCancelable(targetOrder))
                    CancelOrder(targetOrder);
            }
            catch { }

            try
            {
                if (IsOrderCancelable(stopOrder))
                    CancelOrder(stopOrder);
            }
            catch { }
        }

        private void TrySubmitForceFlattenExit()
        {
            if (!forceFlattenInProgress || forceFlattenOrderSubmitted) return;
            if (Position.MarketPosition == MarketPosition.Flat) return;
            if (IsOrderCancelable(targetOrder) || IsOrderCancelable(stopOrder)) return;

            forceFlattenOrderSubmitted = true;
            string reason = string.IsNullOrWhiteSpace(forceFlattenReason) ? "ForcedExit" : forceFlattenReason;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                ExitLong(Math.Max(1, Position.Quantity), BuildExitSignalName("ForcedExit"), LongEntrySignal);
                Print(string.Format("{0} | {1}: Close LONG", Time[0], reason));
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                ExitShort(Math.Max(1, Position.Quantity), BuildExitSignalName("ForcedExit"), ShortEntrySignal);
                Print(string.Format("{0} | {1}: Close SHORT", Time[0], reason));
            }
        }

        private void FlattenAndCancel(string reason)
        {
            CancelWorkingEntryOrder();
            if (Position.MarketPosition == MarketPosition.Flat) return;

            if (!forceFlattenInProgress)
            {
                forceFlattenInProgress = true;
                forceFlattenOrderSubmitted = false;
                forceFlattenReason = reason;
            }

            CancelWorkingProtectionOrders();
            TrySubmitForceFlattenExit();
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
                if (DebugLogging)
                    Print(string.Format(CultureInfo.InvariantCulture,
                        "Max account balance reached | netLiq={0:0.00} target={1:0.00}",
                        balance,
                        MaxAccountBalance));
            }

            if (!maxAccountLimitHit)
                return false;

            FlattenAndCancel("MaxBalance");
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

        #endregion


        #region Drawing Helpers

        private void DrawSessionTimeWindows()
        {
            if (CurrentBar < 1) return;
            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (!S_Active(sid)) continue;
                DrawSessionBackground(Time[0], sid);
            }
            DrawNewsWindows(Time[0]);
        }

        private void DrawSessionBackground(DateTime barTime, int sid)
        {
            DateTime sessionStart = barTime.Date + S_TradeWindowStart(sid).TimeOfDay;
            DateTime sessionEnd = barTime.Date + S_ForcedCloseTime(sid).TimeOfDay;
            if (S_ForcedCloseTime(sid).TimeOfDay <= S_TradeWindowStart(sid).TimeOfDay)
                sessionEnd = sessionEnd.AddDays(1);
            if (sessionEnd <= sessionStart) return;
            int p = ParentGroupOf(sid);
            Brush fillBrush = p == 1 ? Brushes.Gold : p == 2 ? Brushes.CornflowerBlue : Brushes.MediumSeaGreen;
            string rectTag = string.Format("MICH_{0}Fill_{1:yyyyMMdd_HHmm}", S_Label(sid), sessionStart);
            if (DrawObjects[rectTag] != null) return;
            Draw.Rectangle(this, rectTag, false, sessionStart, 0, sessionEnd, 30000, Brushes.Transparent, fillBrush, 10).ZOrder = -1;
        }

        private void DrawNewsWindows(DateTime barTime)
        {
            if (!UseNewsSkip) return;
            for (int i = 0; i < NewsDates.Count; i++)
            {
                DateTime newsTime = NewsDates[i];
                if (newsTime.Date != barTime.Date) continue;
                DateTime windowStart = newsTime.AddMinutes(-NewsBlockMinutes);
                DateTime windowEnd = newsTime.AddMinutes(NewsBlockMinutes);
                var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));
                var lineBrush = new SolidColorBrush(Color.FromArgb(20, 30, 144, 255));
                try { if (areaBrush.CanFreeze) areaBrush.Freeze(); if (lineBrush.CanFreeze) lineBrush.Freeze(); } catch { }
                string tagBase = string.Format("MICH_News_{0:yyyyMMdd_HHmm}", newsTime);
                if (DrawObjects[tagBase + "_Rect"] != null) continue;
                Draw.Rectangle(this, tagBase + "_Rect", false, windowStart, 0, windowEnd, 30000, lineBrush, areaBrush, 2).ZOrder = -1;
            }
        }

        #endregion


        #region Info Box

        private enum InfoValueRunKind { Default, Emoji, Symbol }

        private void UpdateInfoBox()
        {
            if (State != State.Realtime && State != State.Historical) return;
            if (ChartControl == null || ChartControl.Dispatcher == null) return;
            var lines = BuildInfoLines();
            if (!legacyInfoDrawingsCleared) { RemoveLegacyInfoBoxDrawings(); legacyInfoDrawingsCleared = true; }
            ChartControl.Dispatcher.InvokeAsync(() => RenderInfoBoxOverlay(lines));
        }

        private void RenderInfoBoxOverlay(List<(string label, string value, Brush labelBrush, Brush valueBrush)> lines)
        {
            if (!EnsureInfoBoxOverlay() || infoBoxRowsPanel == null) return;
            infoBoxRowsPanel.Children.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                bool isHeader = i == 0; bool isFooter = i == lines.Count - 1;
                var rowBorder = new Border { Background = (isHeader || isFooter) ? InfoHeaderFooterGradientBrush : (i % 2 == 0 ? InfoBodyEvenBrush : InfoBodyOddBrush), Padding = new Thickness(6, 2, 6, 2) };
                var text = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = isHeader ? 15 : 14, FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal, TextAlignment = (isHeader || isFooter) ? TextAlignment.Center : TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
                TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
                string label = lines[i].label ?? string.Empty; string value = lines[i].value ?? string.Empty;
                string normalizedValue = NormalizeInfoValueToken(value);
                bool valueUsesEmojiRendering = ClassifyInfoValueRunKind(normalizedValue) == InfoValueRunKind.Emoji;
                TextOptions.SetTextRenderingMode(text, valueUsesEmojiRendering ? TextRenderingMode.Grayscale : TextRenderingMode.ClearType);
                text.Inlines.Add(new Run(label) { Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : InfoLabelBrush });
                if (!string.IsNullOrEmpty(value))
                {
                    text.Inlines.Add(new Run(" ") { Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : InfoLabelBrush });
                    Brush stateValueBrush = lines[i].valueBrush; if (stateValueBrush == null || stateValueBrush == Brushes.Transparent) stateValueBrush = lines[i].labelBrush; if (stateValueBrush == null || stateValueBrush == Brushes.Transparent) stateValueBrush = InfoValueBrush;
                    text.Inlines.Add(BuildInfoValueRun(normalizedValue, stateValueBrush));
                }
                rowBorder.Child = text; infoBoxRowsPanel.Children.Add(rowBorder);
            }
        }

        private Run BuildInfoValueRun(string value, Brush stateValueBrush)
        {
            string safeValue = value ?? string.Empty; string normalizedValue = NormalizeInfoValueToken(safeValue);
            switch (ClassifyInfoValueRunKind(normalizedValue))
            {
                case InfoValueRunKind.Emoji: var emojiRun = new Run(normalizedValue) { FontFamily = InfoEmojiFontFamily, Foreground = stateValueBrush }; TextOptions.SetTextRenderingMode(emojiRun, TextRenderingMode.Grayscale); return emojiRun;
                case InfoValueRunKind.Symbol: return new Run(normalizedValue) { FontFamily = InfoSymbolFontFamily, Foreground = stateValueBrush };
                default: return new Run(normalizedValue) { Foreground = stateValueBrush };
            }
        }
        private string NormalizeInfoValueToken(string value) { if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty; string token = value.Trim(); if (token == "○" || token == "◯" || token == "⚪") return "⛔"; return value; }
        private InfoValueRunKind ClassifyInfoValueRunKind(string value) { if (string.IsNullOrWhiteSpace(value)) return InfoValueRunKind.Default; string token = value.Trim(); if (InfoEmojiTokens.Contains(token) || ContainsEmojiCodePoint(token)) return InfoValueRunKind.Emoji; if (InfoSymbolTokens.Contains(token)) return InfoValueRunKind.Symbol; return InfoValueRunKind.Default; }
        private bool ContainsEmojiCodePoint(string text) { for (int i = 0; i < text.Length; i++) { int cp = text[i]; if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { cp = char.ConvertToUtf32(text[i], text[i + 1]); i++; } if ((cp >= 0x1F300 && cp <= 0x1FAFF) || (cp >= 0x2600 && cp <= 0x27BF)) return true; } return false; }

        private bool EnsureInfoBoxOverlay()
        {
            if (ChartControl == null) return false;
            if (infoBoxContainer != null && infoBoxRowsPanel != null) return true;
            var host = ChartControl.Parent as System.Windows.Controls.Panel; if (host == null) return false;
            infoBoxRowsPanel = new StackPanel { Orientation = Orientation.Vertical };
            infoBoxContainer = new Border { Child = infoBoxRowsPanel, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 8, 8, 37), Background = Brushes.Transparent };
            host.Children.Add(infoBoxContainer); System.Windows.Controls.Panel.SetZIndex(infoBoxContainer, int.MaxValue); return true;
        }
        private void DisposeInfoBoxOverlay() { try { if (ChartControl == null || ChartControl.Dispatcher == null) { infoBoxRowsPanel = null; infoBoxContainer = null; return; } ChartControl.Dispatcher.InvokeAsync(() => { if (infoBoxContainer != null) { var parent = infoBoxContainer.Parent as System.Windows.Controls.Panel; if (parent != null) parent.Children.Remove(infoBoxContainer); } infoBoxRowsPanel = null; infoBoxContainer = null; }); } catch { infoBoxRowsPanel = null; infoBoxContainer = null; } }
        private void RemoveLegacyInfoBoxDrawings() { RemoveDrawObject("Info"); RemoveDrawObject("myStatusLabel_bg"); for (int i = 0; i < 64; i++) { RemoveDrawObject(string.Format("myStatusLabel_bg_{0}", i)); RemoveDrawObject(string.Format("myStatusLabel_label_{0}", i)); RemoveDrawObject(string.Format("myStatusLabel_val_{0}", i)); } }

        private bool IsInSessionDisplayWindow(int sid, DateTime time)
        {
            double barSM = ToSessionMinutes(time.TimeOfDay);
            double startSM = ToSessionMinutes(S_TradeWindowStart(sid).TimeOfDay);
            double endSM = ToSessionMinutes(S_ForcedCloseTime(sid).TimeOfDay);
            return barSM >= startSM && barSM < endSM;
        }

        private int DetermineDisplaySession(DateTime time)
        {
            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (S_Active(sid) && IsInSessionDisplayWindow(sid, time))
                    return sid;
            }
            return 0;
        }

        private int GetDefaultEnabledSessionId()
        {
            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (S_Active(sid)) return sid;
            }
            return 0;
        }

        private int GetInfoSessionId()
        {
            if (activeSessionId > 0)
            {
                lastInfoSessionId = activeSessionId;
                return activeSessionId;
            }

            int displaySessionId = DetermineDisplaySession(Time[0]);
            if (displaySessionId > 0)
            {
                lastInfoSessionId = displaySessionId;
                return displaySessionId;
            }

            if (lastInfoSessionId > 0)
                return lastInfoSessionId;

            lastInfoSessionId = GetDefaultEnabledSessionId();
            return lastInfoSessionId;
        }

        private string FormatInfoSessionLabel(int sid)
        {
            switch (sid)
            {
                case 1: return "New York A";  case 2: return "New York B";  case 3: return "New York C";
                case 4: return "London A";    case 5: return "London B";    case 6: return "London C";
                case 7: return "Asia A";      case 8: return "Asia B";      case 9: return "Asia C";
                default: return "Off";
            }
        }

        private string BuildContractsInfoText(int sid)
        {
            if (sid >= 1 && sid <= SubSessionCount)
                return S_Contracts(sid).ToString(CultureInfo.InvariantCulture);

            if (Position.Quantity > 0)
                return Position.Quantity.ToString(CultureInfo.InvariantCulture);

            int defaultSid = GetDefaultEnabledSessionId();
            return defaultSid >= 1 && defaultSid <= SubSessionCount
                ? S_Contracts(defaultSid).ToString(CultureInfo.InvariantCulture)
                : "Off";
        }

        private List<(string label, string value, Brush labelBrush, Brush valueBrush)> BuildInfoLines()
        {
            var lines = new List<(string label, string value, Brush labelBrush, Brush valueBrush)>();
            int infoSessionId = GetInfoSessionId();
            lines.Add((string.Format("MICH v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(("Contracts:", BuildContractsInfoText(infoSessionId), Brushes.LightGray, InfoValueBrush));

            if (!UseNewsSkip)
            {
                lines.Add(("News:", "Disabled", Brushes.LightGray, InfoValueBrush));
            }
            else
            {
                List<DateTime> weekNews = GetCurrentWeekNews(Time[0]);
                if (weekNews.Count == 0) lines.Add(("News:", "🚫", Brushes.LightGray, Brushes.IndianRed));
                else
                {
                    for (int i = 0; i < weekNews.Count; i++)
                    {
                        DateTime newsTime = weekNews[i];
                        bool blockPassed = Time[0] > newsTime.AddMinutes(NewsBlockMinutes);
                        string value = newsTime.ToString("ddd", CultureInfo.InvariantCulture) + " " + newsTime.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
                        Brush newsBrush = blockPassed ? PassedNewsRowBrush : Brushes.LightGray;
                        lines.Add(("News:", value, newsBrush, newsBrush));
                    }
                }
            }
            lines.Add(("Session:", FormatInfoSessionLabel(infoSessionId), Brushes.LightGray, InfoValueBrush));
            lines.Add(("AutoEdge Systems™", string.Empty, InfoLabelBrush, Brushes.Transparent));
            return lines;
        }

        private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b) { var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b)); try { if (brush.CanFreeze) brush.Freeze(); } catch { } return brush; }
        private static Brush CreateFrozenVerticalGradientBrush(Color top, Color mid, Color bottom) { var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0.0), EndPoint = new Point(0.5, 1.0) }; brush.GradientStops.Add(new GradientStop(top, 0.0)); brush.GradientStops.Add(new GradientStop(mid, 0.5)); brush.GradientStops.Add(new GradientStop(bottom, 1.0)); try { if (brush.CanFreeze) brush.Freeze(); } catch { } return brush; }
        private string GetAddOnVersion() { Assembly assembly = GetType().Assembly; Version version = assembly.GetName().Version; return version != null ? version.ToString() : "0.0.0.0"; }

        #endregion


        #region News Helpers

        private void EnsureNewsDatesInitialized() { if (newsDatesInitialized) return; NewsDates.Clear(); LoadHardcodedNewsDates(); NewsDates.Sort(); newsDatesInitialized = true; }
        private void LoadHardcodedNewsDates()
        {
            if (string.IsNullOrWhiteSpace(NewsDatesRaw)) return;
            string[] entries = NewsDatesRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++) { DateTime parsed; if (!DateTime.TryParse(entries[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed)) continue; if (!NewsDates.Contains(parsed)) NewsDates.Add(parsed); }
        }
        private List<DateTime> GetCurrentWeekNews(DateTime time)
        {
            EnsureNewsDatesInitialized(); var weekNews = new List<DateTime>(); DateTime weekStart = GetWeekStart(time.Date); DateTime weekEnd = weekStart.AddDays(7);
            for (int i = 0; i < NewsDates.Count; i++) { DateTime candidate = NewsDates[i]; if (candidate >= weekStart && candidate < weekEnd) weekNews.Add(candidate); }
            weekNews.Sort(); return weekNews;
        }
        private DateTime GetWeekStart(DateTime date) { DayOfWeek firstDayOfWeek = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek; int diff = (7 + (date.DayOfWeek - firstDayOfWeek)) % 7; return date.AddDays(-diff).Date; }

        #endregion


        #region Properties

        // ═══════════════════════════════════════
        //  GENERAL
        // ═══════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Require Entry Confirmation", Order = 1, GroupName = "000. General")]
        public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Session Start Time", Order = 2, GroupName = "000. General")]
        public DateTime SessionStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Order = 3, GroupName = "000. General")]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "News Block Minutes", Order = 4, GroupName = "000. General")]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten On Blocked Window", Order = 5, GroupName = "000. General")]
        public bool FlattenOnBlockedWindowTransition { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Debug Logging", Order = 6, GroupName = "000. General")]
        public bool DebugLogging { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Account Balance", Description = "When net liquidation reaches or exceeds this value, entries are blocked and open positions are flattened. 0 disables.", Order = 7, GroupName = "000. General")]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Webhooks", Description = "Enable outbound order webhooks.", Order = 0, GroupName = "200. Webhooks")]
        public bool UseWebhooks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Provider", Description = "Select webhook target: TradersPost or ProjectX.", Order = 1, GroupName = "200. Webhooks")]
        public WebhookProvider WebhookProviderType { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL", Description = "HTTP endpoint for order webhooks. Leave empty to disable.", Order = 2, GroupName = "200. Webhooks")]
        public string WebhookUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Ticker Override", Description = "Optional TradersPost ticker/instrument name override. Leave empty to use the chart instrument automatically.", Order = 3, GroupName = "200. Webhooks")]
        public string WebhookTickerOverride { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Base URL", Description = "ProjectX gateway base URL. Leave the default ProjectX gateway URL or paste your firm-specific endpoint.", Order = 3, GroupName = "200. Webhooks")]
        public string ProjectXApiBaseUrl { get; set; }

        [Browsable(false)]
        public bool ProjectXTradeAllAccounts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Username", Description = "ProjectX login username for direct ProjectX order routing.", Order = 5, GroupName = "200. Webhooks")]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX API Key", Description = "ProjectX API key used together with the ProjectX username.", Order = 6, GroupName = "200. Webhooks")]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Accounts", Description = "Comma-separated ProjectX account ids or exact account names.", Order = 7, GroupName = "200. Webhooks")]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Contract ID", Description = "Hidden optional override for support/debug use only.", Order = 8, GroupName = "200. Webhooks")]
        public string ProjectXContractId { get; set; }


        // ═══════════════════════════════════════
        //  NEW YORK SESSION
        // ═══════════════════════════════════════


        [NinjaScriptProperty]
        [Display(Name = "Enable New York Session", Order = 1, GroupName = "001. NY Master")]
        public bool NyEnable { get; set; }


        // ═══════════════════════════════════════
        //  EUROPE SESSION
        // ═══════════════════════════════════════


        [NinjaScriptProperty]
        [Display(Name = "Enable Europe Session", Order = 1, GroupName = "002. EU Master")]
        public bool EuEnable { get; set; }


        // ═══════════════════════════════════════
        //  ASIA SESSION
        // ═══════════════════════════════════════


        [NinjaScriptProperty]
        [Display(Name = "Enable Asia Session", Order = 1, GroupName = "003. AS Master")]
        public bool AsEnable { get; set; }


        
        [NinjaScriptProperty]
        [Display(Name = "Enable NY-A", Order = 100, GroupName = "010. NY-A: Session")]
        public bool NyAEnable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable No-New-Trades-After", Order = 101, GroupName = "010. NY-A: Session")]
        public bool NyAEnableNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Forced Close", Order = 102, GroupName = "010. NY-A: Session")]
        public bool NyAEnableForcedClose { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Enable NY-B", Order = 100, GroupName = "027. NY-B: Session")]
        public bool NyBEnable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable No-New-Trades-After", Order = 101, GroupName = "027. NY-B: Session")]
        public bool NyBEnableNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Forced Close", Order = 102, GroupName = "027. NY-B: Session")]
        public bool NyBEnableForcedClose { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Enable NY-C", Order = 100, GroupName = "044. NY-C: Session")]
        public bool NyCEnable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable No-New-Trades-After", Order = 101, GroupName = "044. NY-C: Session")]
        public bool NyCEnableNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Forced Close", Order = 102, GroupName = "044. NY-C: Session")]
        public bool NyCEnableForcedClose { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Enable EU-A", Order = 100, GroupName = "061. EU-A: Session")]
        public bool EuAEnable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable No-New-Trades-After", Order = 101, GroupName = "061. EU-A: Session")]
        public bool EuAEnableNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Forced Close", Order = 102, GroupName = "061. EU-A: Session")]
        public bool EuAEnableForcedClose { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Enable EU-B", Order = 100, GroupName = "078. EU-B: Session")]
        public bool EuBEnable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable No-New-Trades-After", Order = 101, GroupName = "078. EU-B: Session")]
        public bool EuBEnableNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Forced Close", Order = 102, GroupName = "078. EU-B: Session")]
        public bool EuBEnableForcedClose { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Enable EU-C", Order = 100, GroupName = "095. EU-C: Session")]
        public bool EuCEnable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable No-New-Trades-After", Order = 101, GroupName = "095. EU-C: Session")]
        public bool EuCEnableNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Forced Close", Order = 102, GroupName = "095. EU-C: Session")]
        public bool EuCEnableForcedClose { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Enable AS-A", Order = 100, GroupName = "112. AS-A: Session")]
        public bool AsAEnable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable No-New-Trades-After", Order = 101, GroupName = "112. AS-A: Session")]
        public bool AsAEnableNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Forced Close", Order = 102, GroupName = "112. AS-A: Session")]
        public bool AsAEnableForcedClose { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Enable AS-B", Order = 100, GroupName = "129. AS-B: Session")]
        public bool AsBEnable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable No-New-Trades-After", Order = 101, GroupName = "129. AS-B: Session")]
        public bool AsBEnableNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Forced Close", Order = 102, GroupName = "129. AS-B: Session")]
        public bool AsBEnableForcedClose { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Enable AS-C", Order = 100, GroupName = "146. AS-C: Session")]
        public bool AsCEnable { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable No-New-Trades-After", Order = 101, GroupName = "146. AS-C: Session")]
        public bool AsCEnableNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Forced Close", Order = 102, GroupName = "146. AS-C: Session")]
        public bool AsCEnableForcedClose { get; set; }


        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "010. NY-A: Session")]
        public int NyAContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "010. NY-A: Session")]
        public bool NyAEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "010. NY-A: Session")]
        public bool NyAEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "010. NY-A: Session")]
        public DateTime NyATradeWindowStart { get; set; }



        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "010. NY-A: Session")]
        public DateTime NyANoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "010. NY-A: Session")]
        public DateTime NyAForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "010. NY-A: Session")]
        public int NyAMaxTradesPerSession { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "010. NY-A: Session")]
        public int NyAMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "010. NY-A: Session")]
        public int NyAMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "010. NY-A: Session")]
        public int NyAMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "011. NY-A: Long: MA Settings")]
        public int NyALongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "011. NY-A: Long: MA Settings")]
        public int NyALongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "011. NY-A: Long: MA Settings")]
        public MAMode NyALongMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "013. NY-A: Long: Take Profit")]
        public int NyALongMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "013. NY-A: Long: Take Profit")]
        public double NyALongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "013. NY-A: Long: Take Profit")]
        public int NyALongAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "013. NY-A: Long: Take Profit")]
        public int NyALongAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "013. NY-A: Long: Take Profit")]
        public double NyALongLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "013. NY-A: Long: Take Profit")]
        public double NyALongHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "014. NY-A: Long: Stop Loss")]
        public int NyALongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "014. NY-A: Long: Stop Loss")]
        public int NyALongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "014. NY-A: Long: Stop Loss")]
        public int NyALongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "014. NY-A: Long: Stop Loss")]
        public int NyALongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "014. NY-A: Long: Stop Loss")]
        public double NyALongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "014. NY-A: Long: Stop Loss")]
        public bool NyALongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "014. NY-A: Long: Stop Loss")]
        public bool NyALongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "014. NY-A: Long: Stop Loss")]
        public bool NyALongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "014. NY-A: Long: Stop Loss")]
        public int NyALongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "015. NY-A: Long: Breakeven")]
        public bool NyALongEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "015. NY-A: Long: Breakeven")]
        public int NyALongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "015. NY-A: Long: Breakeven")]
        public double NyALongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "015. NY-A: Long: Breakeven")]
        public int NyALongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "016. NY-A: Long: Trade Management")]
        public int NyALongMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "016. NY-A: Long: Trade Management")]
        public bool NyALongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "016. NY-A: Long: Trade Management")]
        public int NyALongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "016. NY-A: Long: Trade Management")]
        public bool NyALongEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "016. NY-A: Long: Trade Management")]
        public double NyALongRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "016. NY-A: Long: Trade Management")]
        public double NyALongRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "016. NY-A: Long: Trade Management")]
        public double NyALongRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "016. NY-A: Long: Trade Management")]
        public int NyALongMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "017. NY-A: Long: Entry Filters")]
        public bool NyALongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "017. NY-A: Long: Entry Filters")]
        public bool NyALongAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "017. NY-A: Long: Entry Filters")]
        public bool NyALongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "017. NY-A: Long: Entry Filters")]
        public int NyALongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "017. NY-A: Long: Entry Filters")]
        public bool NyALongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "017. NY-A: Long: Entry Filters")]
        public int NyALongWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "017. NY-A: Long: Entry Filters")]
        public double NyALongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "017. NY-A: Long: Entry Filters")]
        public int NyALongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "017. NY-A: Long: Entry Filters")]
        public bool NyALongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "017. NY-A: Long: Entry Filters")]
        public int NyALongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "017. NY-A: Long: Entry Filters")]
        public int NyALongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "018. NY-A: Long: Early Exit Filters")]
        public bool NyALongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "018. NY-A: Long: Early Exit Filters")]
        public int NyALongEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "018. NY-A: Long: Early Exit Filters")]
        public bool NyALongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "018. NY-A: Long: Early Exit Filters")]
        public int NyALongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "018. NY-A: Long: Early Exit Filters")]
        public double NyALongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "018. NY-A: Long: Early Exit Filters")]
        public bool NyALongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "018. NY-A: Long: Early Exit Filters")]
        public double NyALongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "019. NY-A: Short: MA Settings")]
        public int NyAShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "019. NY-A: Short: MA Settings")]
        public int NyAShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "019. NY-A: Short: MA Settings")]
        public MAMode NyAShortMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "021. NY-A: Short: Take Profit")]
        public int NyAShortMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "021. NY-A: Short: Take Profit")]
        public double NyAShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "021. NY-A: Short: Take Profit")]
        public int NyAShortAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "021. NY-A: Short: Take Profit")]
        public int NyAShortAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "021. NY-A: Short: Take Profit")]
        public double NyAShortLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "021. NY-A: Short: Take Profit")]
        public double NyAShortHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "022. NY-A: Short: Stop Loss")]
        public int NyAShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "022. NY-A: Short: Stop Loss")]
        public int NyAShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "022. NY-A: Short: Stop Loss")]
        public int NyAShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "022. NY-A: Short: Stop Loss")]
        public int NyAShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "022. NY-A: Short: Stop Loss")]
        public double NyAShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "022. NY-A: Short: Stop Loss")]
        public bool NyAShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "022. NY-A: Short: Stop Loss")]
        public bool NyAShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "022. NY-A: Short: Stop Loss")]
        public bool NyAShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "022. NY-A: Short: Stop Loss")]
        public int NyAShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "023. NY-A: Short: Breakeven")]
        public bool NyAShortEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "023. NY-A: Short: Breakeven")]
        public int NyAShortBreakevenTriggerTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "023. NY-A: Short: Breakeven")]
        public int NyAShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "024. NY-A: Short: Trade Management")]
        public int NyAShortMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "024. NY-A: Short: Trade Management")]
        public bool NyAShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "024. NY-A: Short: Trade Management")]
        public int NyAShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "024. NY-A: Short: Trade Management")]
        public bool NyAShortEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "024. NY-A: Short: Trade Management")]
        public double NyAShortRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "024. NY-A: Short: Trade Management")]
        public double NyAShortRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "024. NY-A: Short: Trade Management")]
        public double NyAShortRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "024. NY-A: Short: Trade Management")]
        public int NyAShortMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "025. NY-A: Short: Entry Filters")]
        public bool NyAShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "025. NY-A: Short: Entry Filters")]
        public bool NyAShortAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "025. NY-A: Short: Entry Filters")]
        public bool NyAShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "025. NY-A: Short: Entry Filters")]
        public int NyAShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "025. NY-A: Short: Entry Filters")]
        public bool NyAShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "025. NY-A: Short: Entry Filters")]
        public int NyAShortWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "025. NY-A: Short: Entry Filters")]
        public double NyAShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "025. NY-A: Short: Entry Filters")]
        public int NyAShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "025. NY-A: Short: Entry Filters")]
        public bool NyAShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "025. NY-A: Short: Entry Filters")]
        public int NyAShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "025. NY-A: Short: Entry Filters")]
        public int NyAShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "026. NY-A: Short: Early Exit Filters")]
        public bool NyAShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "026. NY-A: Short: Early Exit Filters")]
        public int NyAShortEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "026. NY-A: Short: Early Exit Filters")]
        public bool NyAShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "026. NY-A: Short: Early Exit Filters")]
        public int NyAShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "026. NY-A: Short: Early Exit Filters")]
        public double NyAShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "026. NY-A: Short: Early Exit Filters")]
        public bool NyAShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "026. NY-A: Short: Early Exit Filters")]
        public double NyAShortAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "027. NY-B: Session")]
        public int NyBContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "027. NY-B: Session")]
        public bool NyBEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "027. NY-B: Session")]
        public bool NyBEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "027. NY-B: Session")]
        public DateTime NyBTradeWindowStart { get; set; }



        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "027. NY-B: Session")]
        public DateTime NyBNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "027. NY-B: Session")]
        public DateTime NyBForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "027. NY-B: Session")]
        public int NyBMaxTradesPerSession { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "027. NY-B: Session")]
        public int NyBMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "027. NY-B: Session")]
        public int NyBMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "027. NY-B: Session")]
        public int NyBMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "028. NY-B: Long: MA Settings")]
        public int NyBLongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "028. NY-B: Long: MA Settings")]
        public int NyBLongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "028. NY-B: Long: MA Settings")]
        public MAMode NyBLongMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "030. NY-B: Long: Take Profit")]
        public int NyBLongMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "030. NY-B: Long: Take Profit")]
        public double NyBLongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "030. NY-B: Long: Take Profit")]
        public int NyBLongAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "030. NY-B: Long: Take Profit")]
        public int NyBLongAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "030. NY-B: Long: Take Profit")]
        public double NyBLongLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "030. NY-B: Long: Take Profit")]
        public double NyBLongHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "031. NY-B: Long: Stop Loss")]
        public int NyBLongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "031. NY-B: Long: Stop Loss")]
        public int NyBLongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "031. NY-B: Long: Stop Loss")]
        public int NyBLongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "031. NY-B: Long: Stop Loss")]
        public int NyBLongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "031. NY-B: Long: Stop Loss")]
        public double NyBLongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "031. NY-B: Long: Stop Loss")]
        public bool NyBLongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "031. NY-B: Long: Stop Loss")]
        public bool NyBLongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "031. NY-B: Long: Stop Loss")]
        public bool NyBLongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "031. NY-B: Long: Stop Loss")]
        public int NyBLongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "032. NY-B: Long: Breakeven")]
        public bool NyBLongEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "032. NY-B: Long: Breakeven")]
        public int NyBLongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "032. NY-B: Long: Breakeven")]
        public double NyBLongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "032. NY-B: Long: Breakeven")]
        public int NyBLongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "033. NY-B: Long: Trade Management")]
        public int NyBLongMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "033. NY-B: Long: Trade Management")]
        public bool NyBLongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "033. NY-B: Long: Trade Management")]
        public int NyBLongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "033. NY-B: Long: Trade Management")]
        public bool NyBLongEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "033. NY-B: Long: Trade Management")]
        public double NyBLongRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "033. NY-B: Long: Trade Management")]
        public double NyBLongRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "033. NY-B: Long: Trade Management")]
        public double NyBLongRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "033. NY-B: Long: Trade Management")]
        public int NyBLongMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "034. NY-B: Long: Entry Filters")]
        public bool NyBLongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "034. NY-B: Long: Entry Filters")]
        public bool NyBLongAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "034. NY-B: Long: Entry Filters")]
        public bool NyBLongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "034. NY-B: Long: Entry Filters")]
        public int NyBLongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "034. NY-B: Long: Entry Filters")]
        public bool NyBLongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "034. NY-B: Long: Entry Filters")]
        public int NyBLongWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "034. NY-B: Long: Entry Filters")]
        public double NyBLongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "034. NY-B: Long: Entry Filters")]
        public int NyBLongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "034. NY-B: Long: Entry Filters")]
        public bool NyBLongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "034. NY-B: Long: Entry Filters")]
        public int NyBLongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "034. NY-B: Long: Entry Filters")]
        public int NyBLongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "035. NY-B: Long: Early Exit Filters")]
        public bool NyBLongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "035. NY-B: Long: Early Exit Filters")]
        public int NyBLongEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "035. NY-B: Long: Early Exit Filters")]
        public bool NyBLongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "035. NY-B: Long: Early Exit Filters")]
        public int NyBLongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "035. NY-B: Long: Early Exit Filters")]
        public double NyBLongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "035. NY-B: Long: Early Exit Filters")]
        public bool NyBLongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "035. NY-B: Long: Early Exit Filters")]
        public double NyBLongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "036. NY-B: Short: MA Settings")]
        public int NyBShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "036. NY-B: Short: MA Settings")]
        public int NyBShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "036. NY-B: Short: MA Settings")]
        public MAMode NyBShortMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "038. NY-B: Short: Take Profit")]
        public int NyBShortMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "038. NY-B: Short: Take Profit")]
        public double NyBShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "038. NY-B: Short: Take Profit")]
        public int NyBShortAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "038. NY-B: Short: Take Profit")]
        public int NyBShortAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "038. NY-B: Short: Take Profit")]
        public double NyBShortLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "038. NY-B: Short: Take Profit")]
        public double NyBShortHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "039. NY-B: Short: Stop Loss")]
        public int NyBShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "039. NY-B: Short: Stop Loss")]
        public int NyBShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "039. NY-B: Short: Stop Loss")]
        public int NyBShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "039. NY-B: Short: Stop Loss")]
        public int NyBShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "039. NY-B: Short: Stop Loss")]
        public double NyBShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "039. NY-B: Short: Stop Loss")]
        public bool NyBShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "039. NY-B: Short: Stop Loss")]
        public bool NyBShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "039. NY-B: Short: Stop Loss")]
        public bool NyBShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "039. NY-B: Short: Stop Loss")]
        public int NyBShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "040. NY-B: Short: Breakeven")]
        public bool NyBShortEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "040. NY-B: Short: Breakeven")]
        public int NyBShortBreakevenTriggerTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "040. NY-B: Short: Breakeven")]
        public int NyBShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "041. NY-B: Short: Trade Management")]
        public int NyBShortMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "041. NY-B: Short: Trade Management")]
        public bool NyBShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "041. NY-B: Short: Trade Management")]
        public int NyBShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "041. NY-B: Short: Trade Management")]
        public bool NyBShortEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "041. NY-B: Short: Trade Management")]
        public double NyBShortRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "041. NY-B: Short: Trade Management")]
        public double NyBShortRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "041. NY-B: Short: Trade Management")]
        public double NyBShortRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "041. NY-B: Short: Trade Management")]
        public int NyBShortMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "042. NY-B: Short: Entry Filters")]
        public bool NyBShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "042. NY-B: Short: Entry Filters")]
        public bool NyBShortAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "042. NY-B: Short: Entry Filters")]
        public bool NyBShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "042. NY-B: Short: Entry Filters")]
        public int NyBShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "042. NY-B: Short: Entry Filters")]
        public bool NyBShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "042. NY-B: Short: Entry Filters")]
        public int NyBShortWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "042. NY-B: Short: Entry Filters")]
        public double NyBShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "042. NY-B: Short: Entry Filters")]
        public int NyBShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "042. NY-B: Short: Entry Filters")]
        public bool NyBShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "042. NY-B: Short: Entry Filters")]
        public int NyBShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "042. NY-B: Short: Entry Filters")]
        public int NyBShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "043. NY-B: Short: Early Exit Filters")]
        public bool NyBShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "043. NY-B: Short: Early Exit Filters")]
        public int NyBShortEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "043. NY-B: Short: Early Exit Filters")]
        public bool NyBShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "043. NY-B: Short: Early Exit Filters")]
        public int NyBShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "043. NY-B: Short: Early Exit Filters")]
        public double NyBShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "043. NY-B: Short: Early Exit Filters")]
        public bool NyBShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "043. NY-B: Short: Early Exit Filters")]
        public double NyBShortAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "044. NY-C: Session")]
        public int NyCContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "044. NY-C: Session")]
        public bool NyCEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "044. NY-C: Session")]
        public bool NyCEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "044. NY-C: Session")]
        public DateTime NyCTradeWindowStart { get; set; }



        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "044. NY-C: Session")]
        public DateTime NyCNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "044. NY-C: Session")]
        public DateTime NyCForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "044. NY-C: Session")]
        public int NyCMaxTradesPerSession { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "044. NY-C: Session")]
        public int NyCMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "044. NY-C: Session")]
        public int NyCMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "044. NY-C: Session")]
        public int NyCMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "045. NY-C: Long: MA Settings")]
        public int NyCLongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "045. NY-C: Long: MA Settings")]
        public int NyCLongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "045. NY-C: Long: MA Settings")]
        public MAMode NyCLongMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "047. NY-C: Long: Take Profit")]
        public int NyCLongMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "047. NY-C: Long: Take Profit")]
        public double NyCLongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "047. NY-C: Long: Take Profit")]
        public int NyCLongAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "047. NY-C: Long: Take Profit")]
        public int NyCLongAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "047. NY-C: Long: Take Profit")]
        public double NyCLongLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "047. NY-C: Long: Take Profit")]
        public double NyCLongHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "048. NY-C: Long: Stop Loss")]
        public int NyCLongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "048. NY-C: Long: Stop Loss")]
        public int NyCLongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "048. NY-C: Long: Stop Loss")]
        public int NyCLongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "048. NY-C: Long: Stop Loss")]
        public int NyCLongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "048. NY-C: Long: Stop Loss")]
        public double NyCLongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "048. NY-C: Long: Stop Loss")]
        public bool NyCLongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "048. NY-C: Long: Stop Loss")]
        public bool NyCLongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "048. NY-C: Long: Stop Loss")]
        public bool NyCLongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "048. NY-C: Long: Stop Loss")]
        public int NyCLongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "049. NY-C: Long: Breakeven")]
        public bool NyCLongEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "049. NY-C: Long: Breakeven")]
        public int NyCLongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "049. NY-C: Long: Breakeven")]
        public double NyCLongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "049. NY-C: Long: Breakeven")]
        public int NyCLongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "050. NY-C: Long: Trade Management")]
        public int NyCLongMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "050. NY-C: Long: Trade Management")]
        public bool NyCLongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "050. NY-C: Long: Trade Management")]
        public int NyCLongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "050. NY-C: Long: Trade Management")]
        public bool NyCLongEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "050. NY-C: Long: Trade Management")]
        public double NyCLongRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "050. NY-C: Long: Trade Management")]
        public double NyCLongRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "050. NY-C: Long: Trade Management")]
        public double NyCLongRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "050. NY-C: Long: Trade Management")]
        public int NyCLongMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "051. NY-C: Long: Entry Filters")]
        public bool NyCLongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "051. NY-C: Long: Entry Filters")]
        public bool NyCLongAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "051. NY-C: Long: Entry Filters")]
        public bool NyCLongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "051. NY-C: Long: Entry Filters")]
        public int NyCLongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "051. NY-C: Long: Entry Filters")]
        public bool NyCLongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "051. NY-C: Long: Entry Filters")]
        public int NyCLongWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "051. NY-C: Long: Entry Filters")]
        public double NyCLongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "051. NY-C: Long: Entry Filters")]
        public int NyCLongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "051. NY-C: Long: Entry Filters")]
        public bool NyCLongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "051. NY-C: Long: Entry Filters")]
        public int NyCLongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "051. NY-C: Long: Entry Filters")]
        public int NyCLongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "052. NY-C: Long: Early Exit Filters")]
        public bool NyCLongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "052. NY-C: Long: Early Exit Filters")]
        public int NyCLongEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "052. NY-C: Long: Early Exit Filters")]
        public bool NyCLongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "052. NY-C: Long: Early Exit Filters")]
        public int NyCLongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "052. NY-C: Long: Early Exit Filters")]
        public double NyCLongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "052. NY-C: Long: Early Exit Filters")]
        public bool NyCLongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "052. NY-C: Long: Early Exit Filters")]
        public double NyCLongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "053. NY-C: Short: MA Settings")]
        public int NyCShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "053. NY-C: Short: MA Settings")]
        public int NyCShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "053. NY-C: Short: MA Settings")]
        public MAMode NyCShortMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "055. NY-C: Short: Take Profit")]
        public int NyCShortMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "055. NY-C: Short: Take Profit")]
        public double NyCShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "055. NY-C: Short: Take Profit")]
        public int NyCShortAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "055. NY-C: Short: Take Profit")]
        public int NyCShortAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "055. NY-C: Short: Take Profit")]
        public double NyCShortLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "055. NY-C: Short: Take Profit")]
        public double NyCShortHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "056. NY-C: Short: Stop Loss")]
        public int NyCShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "056. NY-C: Short: Stop Loss")]
        public int NyCShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "056. NY-C: Short: Stop Loss")]
        public int NyCShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "056. NY-C: Short: Stop Loss")]
        public int NyCShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "056. NY-C: Short: Stop Loss")]
        public double NyCShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "056. NY-C: Short: Stop Loss")]
        public bool NyCShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "056. NY-C: Short: Stop Loss")]
        public bool NyCShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "056. NY-C: Short: Stop Loss")]
        public bool NyCShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "056. NY-C: Short: Stop Loss")]
        public int NyCShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "057. NY-C: Short: Breakeven")]
        public bool NyCShortEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "057. NY-C: Short: Breakeven")]
        public int NyCShortBreakevenTriggerTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "057. NY-C: Short: Breakeven")]
        public int NyCShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "058. NY-C: Short: Trade Management")]
        public int NyCShortMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "058. NY-C: Short: Trade Management")]
        public bool NyCShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "058. NY-C: Short: Trade Management")]
        public int NyCShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "058. NY-C: Short: Trade Management")]
        public bool NyCShortEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "058. NY-C: Short: Trade Management")]
        public double NyCShortRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "058. NY-C: Short: Trade Management")]
        public double NyCShortRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "058. NY-C: Short: Trade Management")]
        public double NyCShortRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "058. NY-C: Short: Trade Management")]
        public int NyCShortMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "059. NY-C: Short: Entry Filters")]
        public bool NyCShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "059. NY-C: Short: Entry Filters")]
        public bool NyCShortAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "059. NY-C: Short: Entry Filters")]
        public bool NyCShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "059. NY-C: Short: Entry Filters")]
        public int NyCShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "059. NY-C: Short: Entry Filters")]
        public bool NyCShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "059. NY-C: Short: Entry Filters")]
        public int NyCShortWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "059. NY-C: Short: Entry Filters")]
        public double NyCShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "059. NY-C: Short: Entry Filters")]
        public int NyCShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "059. NY-C: Short: Entry Filters")]
        public bool NyCShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "059. NY-C: Short: Entry Filters")]
        public int NyCShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "059. NY-C: Short: Entry Filters")]
        public int NyCShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "060. NY-C: Short: Early Exit Filters")]
        public bool NyCShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "060. NY-C: Short: Early Exit Filters")]
        public int NyCShortEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "060. NY-C: Short: Early Exit Filters")]
        public bool NyCShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "060. NY-C: Short: Early Exit Filters")]
        public int NyCShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "060. NY-C: Short: Early Exit Filters")]
        public double NyCShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "060. NY-C: Short: Early Exit Filters")]
        public bool NyCShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "060. NY-C: Short: Early Exit Filters")]
        public double NyCShortAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "061. EU-A: Session")]
        public int EuAContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "061. EU-A: Session")]
        public bool EuAEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "061. EU-A: Session")]
        public bool EuAEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "061. EU-A: Session")]
        public DateTime EuATradeWindowStart { get; set; }



        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "061. EU-A: Session")]
        public DateTime EuANoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "061. EU-A: Session")]
        public DateTime EuAForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "061. EU-A: Session")]
        public int EuAMaxTradesPerSession { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "061. EU-A: Session")]
        public int EuAMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "061. EU-A: Session")]
        public int EuAMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "061. EU-A: Session")]
        public int EuAMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "062. EU-A: Long: MA Settings")]
        public int EuALongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "062. EU-A: Long: MA Settings")]
        public int EuALongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "062. EU-A: Long: MA Settings")]
        public MAMode EuALongMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "064. EU-A: Long: Take Profit")]
        public int EuALongMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "064. EU-A: Long: Take Profit")]
        public double EuALongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "064. EU-A: Long: Take Profit")]
        public int EuALongAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "064. EU-A: Long: Take Profit")]
        public int EuALongAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "064. EU-A: Long: Take Profit")]
        public double EuALongLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "064. EU-A: Long: Take Profit")]
        public double EuALongHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "065. EU-A: Long: Stop Loss")]
        public int EuALongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "065. EU-A: Long: Stop Loss")]
        public int EuALongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "065. EU-A: Long: Stop Loss")]
        public int EuALongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "065. EU-A: Long: Stop Loss")]
        public int EuALongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "065. EU-A: Long: Stop Loss")]
        public double EuALongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "065. EU-A: Long: Stop Loss")]
        public bool EuALongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "065. EU-A: Long: Stop Loss")]
        public bool EuALongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "065. EU-A: Long: Stop Loss")]
        public bool EuALongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "065. EU-A: Long: Stop Loss")]
        public int EuALongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "066. EU-A: Long: Breakeven")]
        public bool EuALongEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "066. EU-A: Long: Breakeven")]
        public int EuALongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "066. EU-A: Long: Breakeven")]
        public double EuALongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "066. EU-A: Long: Breakeven")]
        public int EuALongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "067. EU-A: Long: Trade Management")]
        public int EuALongMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "067. EU-A: Long: Trade Management")]
        public bool EuALongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "067. EU-A: Long: Trade Management")]
        public int EuALongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "067. EU-A: Long: Trade Management")]
        public bool EuALongEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "067. EU-A: Long: Trade Management")]
        public double EuALongRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "067. EU-A: Long: Trade Management")]
        public double EuALongRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "067. EU-A: Long: Trade Management")]
        public double EuALongRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "067. EU-A: Long: Trade Management")]
        public int EuALongMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "068. EU-A: Long: Entry Filters")]
        public bool EuALongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "068. EU-A: Long: Entry Filters")]
        public bool EuALongAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "068. EU-A: Long: Entry Filters")]
        public bool EuALongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "068. EU-A: Long: Entry Filters")]
        public int EuALongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "068. EU-A: Long: Entry Filters")]
        public bool EuALongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "068. EU-A: Long: Entry Filters")]
        public int EuALongWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "068. EU-A: Long: Entry Filters")]
        public double EuALongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "068. EU-A: Long: Entry Filters")]
        public int EuALongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "068. EU-A: Long: Entry Filters")]
        public bool EuALongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "068. EU-A: Long: Entry Filters")]
        public int EuALongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "068. EU-A: Long: Entry Filters")]
        public int EuALongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "069. EU-A: Long: Early Exit Filters")]
        public bool EuALongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "069. EU-A: Long: Early Exit Filters")]
        public int EuALongEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "069. EU-A: Long: Early Exit Filters")]
        public bool EuALongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "069. EU-A: Long: Early Exit Filters")]
        public int EuALongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "069. EU-A: Long: Early Exit Filters")]
        public double EuALongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "069. EU-A: Long: Early Exit Filters")]
        public bool EuALongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "069. EU-A: Long: Early Exit Filters")]
        public double EuALongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "070. EU-A: Short: MA Settings")]
        public int EuAShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "070. EU-A: Short: MA Settings")]
        public int EuAShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "070. EU-A: Short: MA Settings")]
        public MAMode EuAShortMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "072. EU-A: Short: Take Profit")]
        public int EuAShortMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "072. EU-A: Short: Take Profit")]
        public double EuAShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "072. EU-A: Short: Take Profit")]
        public int EuAShortAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "072. EU-A: Short: Take Profit")]
        public int EuAShortAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "072. EU-A: Short: Take Profit")]
        public double EuAShortLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "072. EU-A: Short: Take Profit")]
        public double EuAShortHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "073. EU-A: Short: Stop Loss")]
        public int EuAShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "073. EU-A: Short: Stop Loss")]
        public int EuAShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "073. EU-A: Short: Stop Loss")]
        public int EuAShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "073. EU-A: Short: Stop Loss")]
        public int EuAShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "073. EU-A: Short: Stop Loss")]
        public double EuAShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "073. EU-A: Short: Stop Loss")]
        public bool EuAShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "073. EU-A: Short: Stop Loss")]
        public bool EuAShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "073. EU-A: Short: Stop Loss")]
        public bool EuAShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "073. EU-A: Short: Stop Loss")]
        public int EuAShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "074. EU-A: Short: Breakeven")]
        public bool EuAShortEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "074. EU-A: Short: Breakeven")]
        public int EuAShortBreakevenTriggerTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "074. EU-A: Short: Breakeven")]
        public int EuAShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "075. EU-A: Short: Trade Management")]
        public int EuAShortMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "075. EU-A: Short: Trade Management")]
        public bool EuAShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "075. EU-A: Short: Trade Management")]
        public int EuAShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "075. EU-A: Short: Trade Management")]
        public bool EuAShortEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "075. EU-A: Short: Trade Management")]
        public double EuAShortRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "075. EU-A: Short: Trade Management")]
        public double EuAShortRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "075. EU-A: Short: Trade Management")]
        public double EuAShortRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "075. EU-A: Short: Trade Management")]
        public int EuAShortMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "076. EU-A: Short: Entry Filters")]
        public bool EuAShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "076. EU-A: Short: Entry Filters")]
        public bool EuAShortAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "076. EU-A: Short: Entry Filters")]
        public bool EuAShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "076. EU-A: Short: Entry Filters")]
        public int EuAShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "076. EU-A: Short: Entry Filters")]
        public bool EuAShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "076. EU-A: Short: Entry Filters")]
        public int EuAShortWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "076. EU-A: Short: Entry Filters")]
        public double EuAShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "076. EU-A: Short: Entry Filters")]
        public int EuAShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "076. EU-A: Short: Entry Filters")]
        public bool EuAShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "076. EU-A: Short: Entry Filters")]
        public int EuAShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "076. EU-A: Short: Entry Filters")]
        public int EuAShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "077. EU-A: Short: Early Exit Filters")]
        public bool EuAShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "077. EU-A: Short: Early Exit Filters")]
        public int EuAShortEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "077. EU-A: Short: Early Exit Filters")]
        public bool EuAShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "077. EU-A: Short: Early Exit Filters")]
        public int EuAShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "077. EU-A: Short: Early Exit Filters")]
        public double EuAShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "077. EU-A: Short: Early Exit Filters")]
        public bool EuAShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "077. EU-A: Short: Early Exit Filters")]
        public double EuAShortAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "078. EU-B: Session")]
        public int EuBContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "078. EU-B: Session")]
        public bool EuBEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "078. EU-B: Session")]
        public bool EuBEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "078. EU-B: Session")]
        public DateTime EuBTradeWindowStart { get; set; }



        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "078. EU-B: Session")]
        public DateTime EuBNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "078. EU-B: Session")]
        public DateTime EuBForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "078. EU-B: Session")]
        public int EuBMaxTradesPerSession { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "078. EU-B: Session")]
        public int EuBMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "078. EU-B: Session")]
        public int EuBMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "078. EU-B: Session")]
        public int EuBMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "079. EU-B: Long: MA Settings")]
        public int EuBLongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "079. EU-B: Long: MA Settings")]
        public int EuBLongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "079. EU-B: Long: MA Settings")]
        public MAMode EuBLongMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "081. EU-B: Long: Take Profit")]
        public int EuBLongMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "081. EU-B: Long: Take Profit")]
        public double EuBLongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "081. EU-B: Long: Take Profit")]
        public int EuBLongAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "081. EU-B: Long: Take Profit")]
        public int EuBLongAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "081. EU-B: Long: Take Profit")]
        public double EuBLongLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "081. EU-B: Long: Take Profit")]
        public double EuBLongHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "082. EU-B: Long: Stop Loss")]
        public int EuBLongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "082. EU-B: Long: Stop Loss")]
        public int EuBLongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "082. EU-B: Long: Stop Loss")]
        public int EuBLongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "082. EU-B: Long: Stop Loss")]
        public int EuBLongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "082. EU-B: Long: Stop Loss")]
        public double EuBLongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "082. EU-B: Long: Stop Loss")]
        public bool EuBLongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "082. EU-B: Long: Stop Loss")]
        public bool EuBLongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "082. EU-B: Long: Stop Loss")]
        public bool EuBLongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "082. EU-B: Long: Stop Loss")]
        public int EuBLongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "083. EU-B: Long: Breakeven")]
        public bool EuBLongEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "083. EU-B: Long: Breakeven")]
        public int EuBLongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "083. EU-B: Long: Breakeven")]
        public double EuBLongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "083. EU-B: Long: Breakeven")]
        public int EuBLongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "084. EU-B: Long: Trade Management")]
        public int EuBLongMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "084. EU-B: Long: Trade Management")]
        public bool EuBLongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "084. EU-B: Long: Trade Management")]
        public int EuBLongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "084. EU-B: Long: Trade Management")]
        public bool EuBLongEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "084. EU-B: Long: Trade Management")]
        public double EuBLongRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "084. EU-B: Long: Trade Management")]
        public double EuBLongRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "084. EU-B: Long: Trade Management")]
        public double EuBLongRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "084. EU-B: Long: Trade Management")]
        public int EuBLongMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "085. EU-B: Long: Entry Filters")]
        public bool EuBLongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "085. EU-B: Long: Entry Filters")]
        public bool EuBLongAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "085. EU-B: Long: Entry Filters")]
        public bool EuBLongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "085. EU-B: Long: Entry Filters")]
        public int EuBLongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "085. EU-B: Long: Entry Filters")]
        public bool EuBLongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "085. EU-B: Long: Entry Filters")]
        public int EuBLongWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "085. EU-B: Long: Entry Filters")]
        public double EuBLongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "085. EU-B: Long: Entry Filters")]
        public int EuBLongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "085. EU-B: Long: Entry Filters")]
        public bool EuBLongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "085. EU-B: Long: Entry Filters")]
        public int EuBLongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "085. EU-B: Long: Entry Filters")]
        public int EuBLongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "086. EU-B: Long: Early Exit Filters")]
        public bool EuBLongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "086. EU-B: Long: Early Exit Filters")]
        public int EuBLongEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "086. EU-B: Long: Early Exit Filters")]
        public bool EuBLongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "086. EU-B: Long: Early Exit Filters")]
        public int EuBLongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "086. EU-B: Long: Early Exit Filters")]
        public double EuBLongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "086. EU-B: Long: Early Exit Filters")]
        public bool EuBLongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "086. EU-B: Long: Early Exit Filters")]
        public double EuBLongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "087. EU-B: Short: MA Settings")]
        public int EuBShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "087. EU-B: Short: MA Settings")]
        public int EuBShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "087. EU-B: Short: MA Settings")]
        public MAMode EuBShortMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "089. EU-B: Short: Take Profit")]
        public int EuBShortMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "089. EU-B: Short: Take Profit")]
        public double EuBShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "089. EU-B: Short: Take Profit")]
        public int EuBShortAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "089. EU-B: Short: Take Profit")]
        public int EuBShortAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "089. EU-B: Short: Take Profit")]
        public double EuBShortLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "089. EU-B: Short: Take Profit")]
        public double EuBShortHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "090. EU-B: Short: Stop Loss")]
        public int EuBShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "090. EU-B: Short: Stop Loss")]
        public int EuBShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "090. EU-B: Short: Stop Loss")]
        public int EuBShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "090. EU-B: Short: Stop Loss")]
        public int EuBShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "090. EU-B: Short: Stop Loss")]
        public double EuBShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "090. EU-B: Short: Stop Loss")]
        public bool EuBShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "090. EU-B: Short: Stop Loss")]
        public bool EuBShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "090. EU-B: Short: Stop Loss")]
        public bool EuBShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "090. EU-B: Short: Stop Loss")]
        public int EuBShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "091. EU-B: Short: Breakeven")]
        public bool EuBShortEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "091. EU-B: Short: Breakeven")]
        public int EuBShortBreakevenTriggerTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "091. EU-B: Short: Breakeven")]
        public int EuBShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "092. EU-B: Short: Trade Management")]
        public int EuBShortMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "092. EU-B: Short: Trade Management")]
        public bool EuBShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "092. EU-B: Short: Trade Management")]
        public int EuBShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "092. EU-B: Short: Trade Management")]
        public bool EuBShortEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "092. EU-B: Short: Trade Management")]
        public double EuBShortRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "092. EU-B: Short: Trade Management")]
        public double EuBShortRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "092. EU-B: Short: Trade Management")]
        public double EuBShortRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "092. EU-B: Short: Trade Management")]
        public int EuBShortMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "093. EU-B: Short: Entry Filters")]
        public bool EuBShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "093. EU-B: Short: Entry Filters")]
        public bool EuBShortAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "093. EU-B: Short: Entry Filters")]
        public bool EuBShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "093. EU-B: Short: Entry Filters")]
        public int EuBShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "093. EU-B: Short: Entry Filters")]
        public bool EuBShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "093. EU-B: Short: Entry Filters")]
        public int EuBShortWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "093. EU-B: Short: Entry Filters")]
        public double EuBShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "093. EU-B: Short: Entry Filters")]
        public int EuBShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "093. EU-B: Short: Entry Filters")]
        public bool EuBShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "093. EU-B: Short: Entry Filters")]
        public int EuBShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "093. EU-B: Short: Entry Filters")]
        public int EuBShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "094. EU-B: Short: Early Exit Filters")]
        public bool EuBShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "094. EU-B: Short: Early Exit Filters")]
        public int EuBShortEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "094. EU-B: Short: Early Exit Filters")]
        public bool EuBShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "094. EU-B: Short: Early Exit Filters")]
        public int EuBShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "094. EU-B: Short: Early Exit Filters")]
        public double EuBShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "094. EU-B: Short: Early Exit Filters")]
        public bool EuBShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "094. EU-B: Short: Early Exit Filters")]
        public double EuBShortAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "095. EU-C: Session")]
        public int EuCContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "095. EU-C: Session")]
        public bool EuCEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "095. EU-C: Session")]
        public bool EuCEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "095. EU-C: Session")]
        public DateTime EuCTradeWindowStart { get; set; }



        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "095. EU-C: Session")]
        public DateTime EuCNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "095. EU-C: Session")]
        public DateTime EuCForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "095. EU-C: Session")]
        public int EuCMaxTradesPerSession { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "095. EU-C: Session")]
        public int EuCMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "095. EU-C: Session")]
        public int EuCMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "095. EU-C: Session")]
        public int EuCMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "096. EU-C: Long: MA Settings")]
        public int EuCLongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "096. EU-C: Long: MA Settings")]
        public int EuCLongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "096. EU-C: Long: MA Settings")]
        public MAMode EuCLongMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "098. EU-C: Long: Take Profit")]
        public int EuCLongMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "098. EU-C: Long: Take Profit")]
        public double EuCLongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "098. EU-C: Long: Take Profit")]
        public int EuCLongAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "098. EU-C: Long: Take Profit")]
        public int EuCLongAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "098. EU-C: Long: Take Profit")]
        public double EuCLongLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "098. EU-C: Long: Take Profit")]
        public double EuCLongHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "099. EU-C: Long: Stop Loss")]
        public int EuCLongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "099. EU-C: Long: Stop Loss")]
        public int EuCLongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "099. EU-C: Long: Stop Loss")]
        public int EuCLongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "099. EU-C: Long: Stop Loss")]
        public int EuCLongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "099. EU-C: Long: Stop Loss")]
        public double EuCLongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "099. EU-C: Long: Stop Loss")]
        public bool EuCLongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "099. EU-C: Long: Stop Loss")]
        public bool EuCLongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "099. EU-C: Long: Stop Loss")]
        public bool EuCLongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "099. EU-C: Long: Stop Loss")]
        public int EuCLongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "100. EU-C: Long: Breakeven")]
        public bool EuCLongEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "100. EU-C: Long: Breakeven")]
        public int EuCLongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "100. EU-C: Long: Breakeven")]
        public double EuCLongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "100. EU-C: Long: Breakeven")]
        public int EuCLongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "101. EU-C: Long: Trade Management")]
        public int EuCLongMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "101. EU-C: Long: Trade Management")]
        public bool EuCLongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "101. EU-C: Long: Trade Management")]
        public int EuCLongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "101. EU-C: Long: Trade Management")]
        public bool EuCLongEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "101. EU-C: Long: Trade Management")]
        public double EuCLongRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "101. EU-C: Long: Trade Management")]
        public double EuCLongRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "101. EU-C: Long: Trade Management")]
        public double EuCLongRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "101. EU-C: Long: Trade Management")]
        public int EuCLongMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "102. EU-C: Long: Entry Filters")]
        public bool EuCLongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "102. EU-C: Long: Entry Filters")]
        public bool EuCLongAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "102. EU-C: Long: Entry Filters")]
        public bool EuCLongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "102. EU-C: Long: Entry Filters")]
        public int EuCLongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "102. EU-C: Long: Entry Filters")]
        public bool EuCLongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "102. EU-C: Long: Entry Filters")]
        public int EuCLongWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "102. EU-C: Long: Entry Filters")]
        public double EuCLongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "102. EU-C: Long: Entry Filters")]
        public int EuCLongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "102. EU-C: Long: Entry Filters")]
        public bool EuCLongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "102. EU-C: Long: Entry Filters")]
        public int EuCLongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "102. EU-C: Long: Entry Filters")]
        public int EuCLongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "103. EU-C: Long: Early Exit Filters")]
        public bool EuCLongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "103. EU-C: Long: Early Exit Filters")]
        public int EuCLongEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "103. EU-C: Long: Early Exit Filters")]
        public bool EuCLongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "103. EU-C: Long: Early Exit Filters")]
        public int EuCLongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "103. EU-C: Long: Early Exit Filters")]
        public double EuCLongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "103. EU-C: Long: Early Exit Filters")]
        public bool EuCLongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "103. EU-C: Long: Early Exit Filters")]
        public double EuCLongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "104. EU-C: Short: MA Settings")]
        public int EuCShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "104. EU-C: Short: MA Settings")]
        public int EuCShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "104. EU-C: Short: MA Settings")]
        public MAMode EuCShortMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "106. EU-C: Short: Take Profit")]
        public int EuCShortMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "106. EU-C: Short: Take Profit")]
        public double EuCShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "106. EU-C: Short: Take Profit")]
        public int EuCShortAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "106. EU-C: Short: Take Profit")]
        public int EuCShortAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "106. EU-C: Short: Take Profit")]
        public double EuCShortLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "106. EU-C: Short: Take Profit")]
        public double EuCShortHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "107. EU-C: Short: Stop Loss")]
        public int EuCShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "107. EU-C: Short: Stop Loss")]
        public int EuCShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "107. EU-C: Short: Stop Loss")]
        public int EuCShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "107. EU-C: Short: Stop Loss")]
        public int EuCShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "107. EU-C: Short: Stop Loss")]
        public double EuCShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "107. EU-C: Short: Stop Loss")]
        public bool EuCShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "107. EU-C: Short: Stop Loss")]
        public bool EuCShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "107. EU-C: Short: Stop Loss")]
        public bool EuCShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "107. EU-C: Short: Stop Loss")]
        public int EuCShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "108. EU-C: Short: Breakeven")]
        public bool EuCShortEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "108. EU-C: Short: Breakeven")]
        public int EuCShortBreakevenTriggerTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "108. EU-C: Short: Breakeven")]
        public int EuCShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "109. EU-C: Short: Trade Management")]
        public int EuCShortMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "109. EU-C: Short: Trade Management")]
        public bool EuCShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "109. EU-C: Short: Trade Management")]
        public int EuCShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "109. EU-C: Short: Trade Management")]
        public bool EuCShortEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "109. EU-C: Short: Trade Management")]
        public double EuCShortRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "109. EU-C: Short: Trade Management")]
        public double EuCShortRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "109. EU-C: Short: Trade Management")]
        public double EuCShortRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "109. EU-C: Short: Trade Management")]
        public int EuCShortMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "110. EU-C: Short: Entry Filters")]
        public bool EuCShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "110. EU-C: Short: Entry Filters")]
        public bool EuCShortAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "110. EU-C: Short: Entry Filters")]
        public bool EuCShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "110. EU-C: Short: Entry Filters")]
        public int EuCShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "110. EU-C: Short: Entry Filters")]
        public bool EuCShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "110. EU-C: Short: Entry Filters")]
        public int EuCShortWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "110. EU-C: Short: Entry Filters")]
        public double EuCShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "110. EU-C: Short: Entry Filters")]
        public int EuCShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "110. EU-C: Short: Entry Filters")]
        public bool EuCShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "110. EU-C: Short: Entry Filters")]
        public int EuCShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "110. EU-C: Short: Entry Filters")]
        public int EuCShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "111. EU-C: Short: Early Exit Filters")]
        public bool EuCShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "111. EU-C: Short: Early Exit Filters")]
        public int EuCShortEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "111. EU-C: Short: Early Exit Filters")]
        public bool EuCShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "111. EU-C: Short: Early Exit Filters")]
        public int EuCShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "111. EU-C: Short: Early Exit Filters")]
        public double EuCShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "111. EU-C: Short: Early Exit Filters")]
        public bool EuCShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "111. EU-C: Short: Early Exit Filters")]
        public double EuCShortAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "112. AS-A: Session")]
        public int AsAContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "112. AS-A: Session")]
        public bool AsAEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "112. AS-A: Session")]
        public bool AsAEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "112. AS-A: Session")]
        public DateTime AsATradeWindowStart { get; set; }



        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "112. AS-A: Session")]
        public DateTime AsANoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "112. AS-A: Session")]
        public DateTime AsAForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "112. AS-A: Session")]
        public int AsAMaxTradesPerSession { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "112. AS-A: Session")]
        public int AsAMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "112. AS-A: Session")]
        public int AsAMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "112. AS-A: Session")]
        public int AsAMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "113. AS-A: Long: MA Settings")]
        public int AsALongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "113. AS-A: Long: MA Settings")]
        public int AsALongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "113. AS-A: Long: MA Settings")]
        public MAMode AsALongMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "115. AS-A: Long: Take Profit")]
        public int AsALongMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "115. AS-A: Long: Take Profit")]
        public double AsALongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "115. AS-A: Long: Take Profit")]
        public int AsALongAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "115. AS-A: Long: Take Profit")]
        public int AsALongAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "115. AS-A: Long: Take Profit")]
        public double AsALongLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "115. AS-A: Long: Take Profit")]
        public double AsALongHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "116. AS-A: Long: Stop Loss")]
        public int AsALongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "116. AS-A: Long: Stop Loss")]
        public int AsALongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "116. AS-A: Long: Stop Loss")]
        public int AsALongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "116. AS-A: Long: Stop Loss")]
        public int AsALongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "116. AS-A: Long: Stop Loss")]
        public double AsALongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "116. AS-A: Long: Stop Loss")]
        public bool AsALongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "116. AS-A: Long: Stop Loss")]
        public bool AsALongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "116. AS-A: Long: Stop Loss")]
        public bool AsALongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "116. AS-A: Long: Stop Loss")]
        public int AsALongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "117. AS-A: Long: Breakeven")]
        public bool AsALongEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "117. AS-A: Long: Breakeven")]
        public int AsALongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "117. AS-A: Long: Breakeven")]
        public double AsALongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "117. AS-A: Long: Breakeven")]
        public int AsALongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "118. AS-A: Long: Trade Management")]
        public int AsALongMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "118. AS-A: Long: Trade Management")]
        public bool AsALongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "118. AS-A: Long: Trade Management")]
        public int AsALongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "118. AS-A: Long: Trade Management")]
        public bool AsALongEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "118. AS-A: Long: Trade Management")]
        public double AsALongRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "118. AS-A: Long: Trade Management")]
        public double AsALongRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "118. AS-A: Long: Trade Management")]
        public double AsALongRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "118. AS-A: Long: Trade Management")]
        public int AsALongMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "119. AS-A: Long: Entry Filters")]
        public bool AsALongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "119. AS-A: Long: Entry Filters")]
        public bool AsALongAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "119. AS-A: Long: Entry Filters")]
        public bool AsALongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "119. AS-A: Long: Entry Filters")]
        public int AsALongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "119. AS-A: Long: Entry Filters")]
        public bool AsALongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "119. AS-A: Long: Entry Filters")]
        public int AsALongWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "119. AS-A: Long: Entry Filters")]
        public double AsALongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "119. AS-A: Long: Entry Filters")]
        public int AsALongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "119. AS-A: Long: Entry Filters")]
        public bool AsALongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "119. AS-A: Long: Entry Filters")]
        public int AsALongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "119. AS-A: Long: Entry Filters")]
        public int AsALongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "120. AS-A: Long: Early Exit Filters")]
        public bool AsALongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "120. AS-A: Long: Early Exit Filters")]
        public int AsALongEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "120. AS-A: Long: Early Exit Filters")]
        public bool AsALongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "120. AS-A: Long: Early Exit Filters")]
        public int AsALongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "120. AS-A: Long: Early Exit Filters")]
        public double AsALongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "120. AS-A: Long: Early Exit Filters")]
        public bool AsALongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "120. AS-A: Long: Early Exit Filters")]
        public double AsALongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "121. AS-A: Short: MA Settings")]
        public int AsAShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "121. AS-A: Short: MA Settings")]
        public int AsAShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "121. AS-A: Short: MA Settings")]
        public MAMode AsAShortMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "123. AS-A: Short: Take Profit")]
        public int AsAShortMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "123. AS-A: Short: Take Profit")]
        public double AsAShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "123. AS-A: Short: Take Profit")]
        public int AsAShortAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "123. AS-A: Short: Take Profit")]
        public int AsAShortAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "123. AS-A: Short: Take Profit")]
        public double AsAShortLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "123. AS-A: Short: Take Profit")]
        public double AsAShortHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "124. AS-A: Short: Stop Loss")]
        public int AsAShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "124. AS-A: Short: Stop Loss")]
        public int AsAShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "124. AS-A: Short: Stop Loss")]
        public int AsAShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "124. AS-A: Short: Stop Loss")]
        public int AsAShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "124. AS-A: Short: Stop Loss")]
        public double AsAShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "124. AS-A: Short: Stop Loss")]
        public bool AsAShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "124. AS-A: Short: Stop Loss")]
        public bool AsAShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "124. AS-A: Short: Stop Loss")]
        public bool AsAShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "124. AS-A: Short: Stop Loss")]
        public int AsAShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "125. AS-A: Short: Breakeven")]
        public bool AsAShortEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "125. AS-A: Short: Breakeven")]
        public int AsAShortBreakevenTriggerTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "125. AS-A: Short: Breakeven")]
        public int AsAShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "126. AS-A: Short: Trade Management")]
        public int AsAShortMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "126. AS-A: Short: Trade Management")]
        public bool AsAShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "126. AS-A: Short: Trade Management")]
        public int AsAShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "126. AS-A: Short: Trade Management")]
        public bool AsAShortEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "126. AS-A: Short: Trade Management")]
        public double AsAShortRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "126. AS-A: Short: Trade Management")]
        public double AsAShortRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "126. AS-A: Short: Trade Management")]
        public double AsAShortRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "126. AS-A: Short: Trade Management")]
        public int AsAShortMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "127. AS-A: Short: Entry Filters")]
        public bool AsAShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "127. AS-A: Short: Entry Filters")]
        public bool AsAShortAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "127. AS-A: Short: Entry Filters")]
        public bool AsAShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "127. AS-A: Short: Entry Filters")]
        public int AsAShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "127. AS-A: Short: Entry Filters")]
        public bool AsAShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "127. AS-A: Short: Entry Filters")]
        public int AsAShortWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "127. AS-A: Short: Entry Filters")]
        public double AsAShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "127. AS-A: Short: Entry Filters")]
        public int AsAShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "127. AS-A: Short: Entry Filters")]
        public bool AsAShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "127. AS-A: Short: Entry Filters")]
        public int AsAShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "127. AS-A: Short: Entry Filters")]
        public int AsAShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "128. AS-A: Short: Early Exit Filters")]
        public bool AsAShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "128. AS-A: Short: Early Exit Filters")]
        public int AsAShortEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "128. AS-A: Short: Early Exit Filters")]
        public bool AsAShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "128. AS-A: Short: Early Exit Filters")]
        public int AsAShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "128. AS-A: Short: Early Exit Filters")]
        public double AsAShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "128. AS-A: Short: Early Exit Filters")]
        public bool AsAShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "128. AS-A: Short: Early Exit Filters")]
        public double AsAShortAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "129. AS-B: Session")]
        public int AsBContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "129. AS-B: Session")]
        public bool AsBEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "129. AS-B: Session")]
        public bool AsBEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "129. AS-B: Session")]
        public DateTime AsBTradeWindowStart { get; set; }



        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "129. AS-B: Session")]
        public DateTime AsBNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "129. AS-B: Session")]
        public DateTime AsBForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "129. AS-B: Session")]
        public int AsBMaxTradesPerSession { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "129. AS-B: Session")]
        public int AsBMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "129. AS-B: Session")]
        public int AsBMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "129. AS-B: Session")]
        public int AsBMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "130. AS-B: Long: MA Settings")]
        public int AsBLongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "130. AS-B: Long: MA Settings")]
        public int AsBLongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "130. AS-B: Long: MA Settings")]
        public MAMode AsBLongMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "132. AS-B: Long: Take Profit")]
        public int AsBLongMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "132. AS-B: Long: Take Profit")]
        public double AsBLongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "132. AS-B: Long: Take Profit")]
        public int AsBLongAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "132. AS-B: Long: Take Profit")]
        public int AsBLongAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "132. AS-B: Long: Take Profit")]
        public double AsBLongLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "132. AS-B: Long: Take Profit")]
        public double AsBLongHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "133. AS-B: Long: Stop Loss")]
        public int AsBLongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "133. AS-B: Long: Stop Loss")]
        public int AsBLongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "133. AS-B: Long: Stop Loss")]
        public int AsBLongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "133. AS-B: Long: Stop Loss")]
        public int AsBLongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "133. AS-B: Long: Stop Loss")]
        public double AsBLongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "133. AS-B: Long: Stop Loss")]
        public bool AsBLongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "133. AS-B: Long: Stop Loss")]
        public bool AsBLongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "133. AS-B: Long: Stop Loss")]
        public bool AsBLongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "133. AS-B: Long: Stop Loss")]
        public int AsBLongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "134. AS-B: Long: Breakeven")]
        public bool AsBLongEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "134. AS-B: Long: Breakeven")]
        public int AsBLongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "134. AS-B: Long: Breakeven")]
        public double AsBLongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "134. AS-B: Long: Breakeven")]
        public int AsBLongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "135. AS-B: Long: Trade Management")]
        public int AsBLongMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "135. AS-B: Long: Trade Management")]
        public bool AsBLongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "135. AS-B: Long: Trade Management")]
        public int AsBLongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "135. AS-B: Long: Trade Management")]
        public bool AsBLongEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "135. AS-B: Long: Trade Management")]
        public double AsBLongRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "135. AS-B: Long: Trade Management")]
        public double AsBLongRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "135. AS-B: Long: Trade Management")]
        public double AsBLongRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "135. AS-B: Long: Trade Management")]
        public int AsBLongMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "136. AS-B: Long: Entry Filters")]
        public bool AsBLongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "136. AS-B: Long: Entry Filters")]
        public bool AsBLongAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "136. AS-B: Long: Entry Filters")]
        public bool AsBLongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "136. AS-B: Long: Entry Filters")]
        public int AsBLongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "136. AS-B: Long: Entry Filters")]
        public bool AsBLongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "136. AS-B: Long: Entry Filters")]
        public int AsBLongWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "136. AS-B: Long: Entry Filters")]
        public double AsBLongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "136. AS-B: Long: Entry Filters")]
        public int AsBLongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "136. AS-B: Long: Entry Filters")]
        public bool AsBLongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "136. AS-B: Long: Entry Filters")]
        public int AsBLongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "136. AS-B: Long: Entry Filters")]
        public int AsBLongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "137. AS-B: Long: Early Exit Filters")]
        public bool AsBLongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "137. AS-B: Long: Early Exit Filters")]
        public int AsBLongEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "137. AS-B: Long: Early Exit Filters")]
        public bool AsBLongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "137. AS-B: Long: Early Exit Filters")]
        public int AsBLongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "137. AS-B: Long: Early Exit Filters")]
        public double AsBLongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "137. AS-B: Long: Early Exit Filters")]
        public bool AsBLongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "137. AS-B: Long: Early Exit Filters")]
        public double AsBLongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "138. AS-B: Short: MA Settings")]
        public int AsBShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "138. AS-B: Short: MA Settings")]
        public int AsBShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "138. AS-B: Short: MA Settings")]
        public MAMode AsBShortMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "140. AS-B: Short: Take Profit")]
        public int AsBShortMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "140. AS-B: Short: Take Profit")]
        public double AsBShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "140. AS-B: Short: Take Profit")]
        public int AsBShortAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "140. AS-B: Short: Take Profit")]
        public int AsBShortAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "140. AS-B: Short: Take Profit")]
        public double AsBShortLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "140. AS-B: Short: Take Profit")]
        public double AsBShortHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "141. AS-B: Short: Stop Loss")]
        public int AsBShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "141. AS-B: Short: Stop Loss")]
        public int AsBShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "141. AS-B: Short: Stop Loss")]
        public int AsBShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "141. AS-B: Short: Stop Loss")]
        public int AsBShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "141. AS-B: Short: Stop Loss")]
        public double AsBShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "141. AS-B: Short: Stop Loss")]
        public bool AsBShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "141. AS-B: Short: Stop Loss")]
        public bool AsBShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "141. AS-B: Short: Stop Loss")]
        public bool AsBShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "141. AS-B: Short: Stop Loss")]
        public int AsBShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "142. AS-B: Short: Breakeven")]
        public bool AsBShortEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "142. AS-B: Short: Breakeven")]
        public int AsBShortBreakevenTriggerTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "142. AS-B: Short: Breakeven")]
        public int AsBShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "143. AS-B: Short: Trade Management")]
        public int AsBShortMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "143. AS-B: Short: Trade Management")]
        public bool AsBShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "143. AS-B: Short: Trade Management")]
        public int AsBShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "143. AS-B: Short: Trade Management")]
        public bool AsBShortEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "143. AS-B: Short: Trade Management")]
        public double AsBShortRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "143. AS-B: Short: Trade Management")]
        public double AsBShortRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "143. AS-B: Short: Trade Management")]
        public double AsBShortRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "143. AS-B: Short: Trade Management")]
        public int AsBShortMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "144. AS-B: Short: Entry Filters")]
        public bool AsBShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "144. AS-B: Short: Entry Filters")]
        public bool AsBShortAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "144. AS-B: Short: Entry Filters")]
        public bool AsBShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "144. AS-B: Short: Entry Filters")]
        public int AsBShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "144. AS-B: Short: Entry Filters")]
        public bool AsBShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "144. AS-B: Short: Entry Filters")]
        public int AsBShortWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "144. AS-B: Short: Entry Filters")]
        public double AsBShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "144. AS-B: Short: Entry Filters")]
        public int AsBShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "144. AS-B: Short: Entry Filters")]
        public bool AsBShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "144. AS-B: Short: Entry Filters")]
        public int AsBShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "144. AS-B: Short: Entry Filters")]
        public int AsBShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "145. AS-B: Short: Early Exit Filters")]
        public bool AsBShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "145. AS-B: Short: Early Exit Filters")]
        public int AsBShortEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "145. AS-B: Short: Early Exit Filters")]
        public bool AsBShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "145. AS-B: Short: Early Exit Filters")]
        public int AsBShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "145. AS-B: Short: Early Exit Filters")]
        public double AsBShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "145. AS-B: Short: Early Exit Filters")]
        public bool AsBShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "145. AS-B: Short: Early Exit Filters")]
        public double AsBShortAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "146. AS-C: Session")]
        public int AsCContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "146. AS-C: Session")]
        public bool AsCEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "146. AS-C: Session")]
        public bool AsCEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "146. AS-C: Session")]
        public DateTime AsCTradeWindowStart { get; set; }



        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "146. AS-C: Session")]
        public DateTime AsCNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "146. AS-C: Session")]
        public DateTime AsCForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "146. AS-C: Session")]
        public int AsCMaxTradesPerSession { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "146. AS-C: Session")]
        public int AsCMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "146. AS-C: Session")]
        public int AsCMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "146. AS-C: Session")]
        public int AsCMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "147. AS-C: Long: MA Settings")]
        public int AsCLongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "147. AS-C: Long: MA Settings")]
        public int AsCLongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "147. AS-C: Long: MA Settings")]
        public MAMode AsCLongMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "149. AS-C: Long: Take Profit")]
        public int AsCLongMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "149. AS-C: Long: Take Profit")]
        public double AsCLongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "149. AS-C: Long: Take Profit")]
        public int AsCLongAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "149. AS-C: Long: Take Profit")]
        public int AsCLongAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "149. AS-C: Long: Take Profit")]
        public double AsCLongLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "149. AS-C: Long: Take Profit")]
        public double AsCLongHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "150. AS-C: Long: Stop Loss")]
        public int AsCLongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "150. AS-C: Long: Stop Loss")]
        public int AsCLongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "150. AS-C: Long: Stop Loss")]
        public int AsCLongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "150. AS-C: Long: Stop Loss")]
        public int AsCLongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "150. AS-C: Long: Stop Loss")]
        public double AsCLongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "150. AS-C: Long: Stop Loss")]
        public bool AsCLongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "150. AS-C: Long: Stop Loss")]
        public bool AsCLongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "150. AS-C: Long: Stop Loss")]
        public bool AsCLongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "150. AS-C: Long: Stop Loss")]
        public int AsCLongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "151. AS-C: Long: Breakeven")]
        public bool AsCLongEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "151. AS-C: Long: Breakeven")]
        public int AsCLongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "151. AS-C: Long: Breakeven")]
        public double AsCLongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "151. AS-C: Long: Breakeven")]
        public int AsCLongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "152. AS-C: Long: Trade Management")]
        public int AsCLongMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "152. AS-C: Long: Trade Management")]
        public bool AsCLongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "152. AS-C: Long: Trade Management")]
        public int AsCLongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "152. AS-C: Long: Trade Management")]
        public bool AsCLongEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "152. AS-C: Long: Trade Management")]
        public double AsCLongRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "152. AS-C: Long: Trade Management")]
        public double AsCLongRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "152. AS-C: Long: Trade Management")]
        public double AsCLongRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "152. AS-C: Long: Trade Management")]
        public int AsCLongMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "153. AS-C: Long: Entry Filters")]
        public bool AsCLongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "153. AS-C: Long: Entry Filters")]
        public bool AsCLongAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "153. AS-C: Long: Entry Filters")]
        public bool AsCLongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "153. AS-C: Long: Entry Filters")]
        public int AsCLongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "153. AS-C: Long: Entry Filters")]
        public bool AsCLongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "153. AS-C: Long: Entry Filters")]
        public int AsCLongWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "153. AS-C: Long: Entry Filters")]
        public double AsCLongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "153. AS-C: Long: Entry Filters")]
        public int AsCLongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "153. AS-C: Long: Entry Filters")]
        public bool AsCLongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "153. AS-C: Long: Entry Filters")]
        public int AsCLongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "153. AS-C: Long: Entry Filters")]
        public int AsCLongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "154. AS-C: Long: Early Exit Filters")]
        public bool AsCLongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "154. AS-C: Long: Early Exit Filters")]
        public int AsCLongEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "154. AS-C: Long: Early Exit Filters")]
        public bool AsCLongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "154. AS-C: Long: Early Exit Filters")]
        public int AsCLongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "154. AS-C: Long: Early Exit Filters")]
        public double AsCLongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "154. AS-C: Long: Early Exit Filters")]
        public bool AsCLongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "154. AS-C: Long: Early Exit Filters")]
        public double AsCLongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "155. AS-C: Short: MA Settings")]
        public int AsCShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "155. AS-C: Short: MA Settings")]
        public int AsCShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "155. AS-C: Short: MA Settings")]
        public MAMode AsCShortMaType { get; set; }






        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "157. AS-C: Short: Take Profit")]
        public int AsCShortMaxTakeProfitTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "157. AS-C: Short: Take Profit")]
        public double AsCShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ATR Period", Order = 6, GroupName = "157. AS-C: Short: Take Profit")]
        public int AsCShortAtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "ATR Threshold (Ticks, 0=off)", Order = 7, GroupName = "157. AS-C: Short: Take Profit")]
        public int AsCShortAtrThresholdTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Low ATR Candle Multiplier", Order = 8, GroupName = "157. AS-C: Short: Take Profit")]
        public double AsCShortLowAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "High ATR Candle Multiplier", Order = 9, GroupName = "157. AS-C: Short: Take Profit")]
        public double AsCShortHighAtrCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "158. AS-C: Short: Stop Loss")]
        public int AsCShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "158. AS-C: Short: Stop Loss")]
        public int AsCShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "158. AS-C: Short: Stop Loss")]
        public int AsCShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "158. AS-C: Short: Stop Loss")]
        public int AsCShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "158. AS-C: Short: Stop Loss")]
        public double AsCShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "158. AS-C: Short: Stop Loss")]
        public bool AsCShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "158. AS-C: Short: Stop Loss")]
        public bool AsCShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "158. AS-C: Short: Stop Loss")]
        public bool AsCShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "158. AS-C: Short: Stop Loss")]
        public int AsCShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "159. AS-C: Short: Breakeven")]
        public bool AsCShortEnableBreakeven { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "159. AS-C: Short: Breakeven")]
        public int AsCShortBreakevenTriggerTicks { get; set; }


        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "159. AS-C: Short: Breakeven")]
        public int AsCShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "160. AS-C: Short: Trade Management")]
        public int AsCShortMaxBarsInTrade { get; set; }



        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "160. AS-C: Short: Trade Management")]
        public bool AsCShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "160. AS-C: Short: Trade Management")]
        public int AsCShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable R-Multiple Trail", Order = 6, GroupName = "160. AS-C: Short: Trade Management")]
        public bool AsCShortEnableRMultipleTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "R-Trail Activation (%R, 0=off)", Order = 7, GroupName = "160. AS-C: Short: Trade Management")]
        public double AsCShortRMultipleActivationPct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "R-Trail Distance (%R)", Order = 8, GroupName = "160. AS-C: Short: Trade Management")]
        public double AsCShortRMultipleTrailPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "R-Trail Lock (%R, 0=off)", Order = 9, GroupName = "160. AS-C: Short: Trade Management")]
        public double AsCShortRMultipleLockPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "MAE Max Ticks (0=off)", Order = 11, GroupName = "160. AS-C: Short: Trade Management")]
        public int AsCShortMAEMaxTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "161. AS-C: Short: Entry Filters")]
        public bool AsCShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "161. AS-C: Short: Entry Filters")]
        public bool AsCShortAllowSameDirectionAfterLoss { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "161. AS-C: Short: Entry Filters")]
        public bool AsCShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "161. AS-C: Short: Entry Filters")]
        public int AsCShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "161. AS-C: Short: Entry Filters")]
        public bool AsCShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "161. AS-C: Short: Entry Filters")]
        public int AsCShortWmaPeriod { get; set; }


        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "161. AS-C: Short: Entry Filters")]
        public double AsCShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "161. AS-C: Short: Entry Filters")]
        public int AsCShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "161. AS-C: Short: Entry Filters")]
        public bool AsCShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "161. AS-C: Short: Entry Filters")]
        public int AsCShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "161. AS-C: Short: Entry Filters")]
        public int AsCShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "162. AS-C: Short: Early Exit Filters")]
        public bool AsCShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "162. AS-C: Short: Early Exit Filters")]
        public int AsCShortEmaEarlyExitPeriod { get; set; }


        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "162. AS-C: Short: Early Exit Filters")]
        public bool AsCShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "162. AS-C: Short: Early Exit Filters")]
        public int AsCShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "162. AS-C: Short: Early Exit Filters")]
        public double AsCShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "162. AS-C: Short: Early Exit Filters")]
        public bool AsCShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "162. AS-C: Short: Early Exit Filters")]
        public double AsCShortAdxDrawdownFromPeak { get; set; }

        public enum MAMode { SMA, EMA, Both }
        public enum MichalEntryMode { Market, LimitOffset, LimitRetracement }
        public enum MichalTPMode { FixedTicks, SwingPoint, CandleMultiple }
        public enum BEMode2 { FixedTicks, CandlePercent }
        public enum WebhookProvider { TradersPost, ProjectX }

        #endregion
    }
}
