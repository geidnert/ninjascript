#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
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
    public class MICH : Strategy
    {
        private const string StrategySignalPrefix = "MICH";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";
        private const string HeartbeatStrategyName = "MICH";
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

        public MICH()
        {
            VendorLicense(1235);
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

        //  Session IDs:  1=NY-A  2=NY-B  3=NY-C
        //                4=EU-A  5=EU-B  6=EU-C
        //                7=AS-A  8=AS-B  9=AS-C
        private const int SubSessionCount = 9;

        // ── Per-session state arrays (index 1..9) ─────────────────────────────
        private readonly int[]    subTradeCount               = new int[SubSessionCount + 1];
        private readonly int[]    subWinCount                 = new int[SubSessionCount + 1];
        private readonly int[]    subLossCount                = new int[SubSessionCount + 1];
        private readonly double[] subPnLTicks                 = new double[SubSessionCount + 1];
        private readonly bool[]   subLimitsReached            = new bool[SubSessionCount + 1];
        private readonly int[]    subLastTradeDirection       = new int[SubSessionCount + 1];
        private readonly bool[]   subLastTradeWasLoss         = new bool[SubSessionCount + 1];
        private readonly bool[]   subWasInNoTradesAfterWindow = new bool[SubSessionCount + 1];

        // ── Per-session indicator arrays (index 1..9) ─────────────────────────
        private readonly SMA[]   smaHighL = new SMA[SubSessionCount + 1];
        private readonly SMA[]   smaLowL  = new SMA[SubSessionCount + 1];
        private readonly SMA[]   smaHighS = new SMA[SubSessionCount + 1];
        private readonly SMA[]   smaLowS  = new SMA[SubSessionCount + 1];
        private readonly EMA[]   emaHighL = new EMA[SubSessionCount + 1];
        private readonly EMA[]   emaLowL  = new EMA[SubSessionCount + 1];
        private readonly EMA[]   emaHighS = new EMA[SubSessionCount + 1];
        private readonly EMA[]   emaLowS  = new EMA[SubSessionCount + 1];
        private readonly WMA[]   wmaLong  = new WMA[SubSessionCount + 1];
        private readonly WMA[]   wmaShort = new WMA[SubSessionCount + 1];
        private readonly ADX[]   adxLong  = new ADX[SubSessionCount + 1];
        private readonly ADX[]   adxShort = new ADX[SubSessionCount + 1];
        private readonly Swing[] swingInd = new Swing[SubSessionCount + 1];


        // ── Session / trade state ─────────────────────────────────────────────
        private int            activeSessionId;
        private DateTime       currentSessionDate;
        private int            tradeDirection;
        private double         signalCandleRange;
        private int            lastInfoSessionId;
        private double         priorCandleHigh;
        private double         priorCandleLow;
        private double         originalStopPrice;
        private double         tradeEntryPrice;
        private bool           hasActivePosition;
        private int            barsSinceEntry;
        private bool           breakEvenApplied;
        private bool           entryBarSlApplied;
        private bool           priceOffsetTrailActive;
        private double         priceOffsetTrailDistance;
        private bool           opposingBarBenchmarkSet;
        private double         opposingBarBenchmark;
        private bool           wasInNewsSkipWindow;
        private bool           maxAccountLimitHit;
        private bool           isConfiguredTimeframeValid = true;
        private bool           isConfiguredInstrumentValid = true;
        private bool           timeframePopupShown;
        private bool           instrumentPopupShown;
        private MarketPosition prevMarketPosition;
        private Order          entryOrder;
        private Order          stopOrder;
        private Order          targetOrder;
        private bool           forceFlattenInProgress;
        private bool           forceFlattenOrderSubmitted;
        private string         forceFlattenReason;
        private StrategyHeartbeatReporter heartbeatReporter;
        private string projectXSessionToken;
        private DateTime projectXTokenAcquiredUtc;
        private List<ProjectXAccountInfo> projectXAccounts;
        private readonly Dictionary<string, long> projectXLastOrderIds = new Dictionary<string, long>();
        private string projectXResolvedContractId;
        private string projectXResolvedInstrumentKey;
        private double projectXLastSyncedStopPrice;
        private double projectXLastSyncedTargetPrice;

        // =====================================================================
        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description  = "Michal MS v1.11 — 9-session multi-session strategy with MichalV106 entry logic.";
                Name         = "MICH";
                Calculate    = Calculate.OnBarClose;
                EntriesPerDirection   = 1;
                EntryHandling         = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds    = 30;
                IsFillLimitOnTouch           = false;
                MaximumBarsLookBack          = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution          = OrderFillResolution.Standard;
                Slippage                     = 0;
                StartBehavior                = StartBehavior.ImmediatelySubmitSynchronizeAccount;
                TimeInForce                  = TimeInForce.Gtc;
                TraceOrders                  = true;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling           = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade          = 20;
                IsInstantiatedOnEachOptimizationIteration = true;

                SessionStartTime = DateTime.Parse("18:00", System.Globalization.CultureInfo.InvariantCulture);
                RequireEntryConfirmation = false;
                MaxAccountBalance = 0.0;
                UseNewsSkip = false;
                NewsBlockMinutes = 1;
                FlattenOnBlockedWindowTransition = false;
                DebugLogging = false;
                UseWebhooks = false;
                WebhookProviderType = WebhookProvider.TradersPost;
                WebhookUrl = string.Empty;
                WebhookTickerOverride = string.Empty;
                ProjectXApiBaseUrl = "https://api.topstepx.com";
                ProjectXTradeAllAccounts = false;
                ProjectXUsername = string.Empty;
                ProjectXApiKey = string.Empty;
                ProjectXAccountId = string.Empty;
                ProjectXContractId = string.Empty;
                NyEnable = true;  EuEnable = true;  AsEnable = true;
                NyAEngulfingExitAfterBars=0; NyAEngulfingExitPaddingTicks=10;
                NyBEngulfingExitAfterBars=13; NyBEngulfingExitPaddingTicks=16;
                NyCEngulfingExitAfterBars=19; NyCEngulfingExitPaddingTicks=5;
                EuAEngulfingExitAfterBars=9; EuAEngulfingExitPaddingTicks=8;
                EuBEngulfingExitAfterBars=13; EuBEngulfingExitPaddingTicks=6;
                EuCEngulfingExitAfterBars=0; EuCEngulfingExitPaddingTicks=5;
                AsAEngulfingExitAfterBars=0; AsAEngulfingExitPaddingTicks=10;
                AsBEngulfingExitAfterBars=17; AsBEngulfingExitPaddingTicks=26;
                AsCEngulfingExitAfterBars=18; AsCEngulfingExitPaddingTicks=5;

                // ── Global MA settings ──────────────────────────────────────
                GlobalMaPeriod=50; GlobalEmaPeriod=50; GlobalMaType=MAMode.Both;
                // ── Global Contracts ─────────────────────────────────────────
                GlobalContracts=1;

                // ── Global MA Exit Filter (0 = disabled) ─────────────────────

                // ── NY-A ─────────────────────────────────────────────────────
                NyAEnable = true;
                NyATradeWindowStart       = DateTime.Parse("09:45", System.Globalization.CultureInfo.InvariantCulture);
                NyAEnableNoNewTradesAfter = true;
                NyANoNewTradesAfter       = DateTime.Parse("11:35", System.Globalization.CultureInfo.InvariantCulture);
                NyAEnableForcedClose      = false;
                NyAForcedCloseTime        = DateTime.Parse("11:55", System.Globalization.CultureInfo.InvariantCulture);
                NyAMaxSessionProfitTicks=1350; NyAMaxSessionLossTicks=160;
                NyAMaxTradesPerSession=2; NyAMaxLossesPerSession=2;
                // Long
                NyALongCandleMultiplier=4.23; NyALongMaxTakeProfitTicks=1120;
                NyALongSlExtraTicks=41; NyALongMaxSlTicks=330; NyALongMaxSlToTpRatio=0.58;
                NyALongUsePriorCandleSl=true; NyALongSlAtMa=false; NyALongMoveSlToEntryBar=false;
                NyALongTrailOffsetTicks=0; NyALongTrailDelayBars=3; NyALongTrailCandleOffset=23;
                NyALongEnableBreakeven=true; NyALongBreakevenTriggerTicks=318; NyALongBreakevenOffsetTicks=10;
                NyALongMaxBarsInTrade=56;
                NyALongEnablePriceOffsetTrail=true; NyALongPriceOffsetReductionTicks=17;
                NyARequireDirectionFlip=true; NyAAllowSameDirectionAfterLoss=true;
                NyALongEnableWmaFilter=true; NyALongWmaPeriod=143;
                NyALongMinBodyPct=60.0;
                NyALongTrendConfirmBars=13; NyALongEnableAdxFilter=true; NyALongAdxPeriod=7; NyALongAdxMinLevel=12;
                // Short
                NyAShortCandleMultiplier=4.24; NyAShortMaxTakeProfitTicks=1179;
                NyAShortSlExtraTicks=38; NyAShortMaxSlTicks=323; NyAShortMaxSlToTpRatio=0.5;
                NyAShortUsePriorCandleSl=true; NyAShortSlAtMa=false; NyAShortMoveSlToEntryBar=false;
                NyAShortTrailOffsetTicks=36; NyAShortTrailDelayBars=17; NyAShortTrailCandleOffset=15;
                NyAShortEnableBreakeven=true; NyAShortBreakevenTriggerTicks=334; NyAShortBreakevenOffsetTicks=230;
                NyAShortMaxBarsInTrade=35;
                NyAShortEnablePriceOffsetTrail=false; NyAShortPriceOffsetReductionTicks=42;
                NyAShortEnableWmaFilter=true; NyAShortWmaPeriod=47;
                NyAShortMinBodyPct=6.0;
                NyAShortTrendConfirmBars=11; NyAShortEnableAdxFilter=true; NyAShortAdxPeriod=11; NyAShortAdxMinLevel=17;

                // ── NY-B ─────────────────────────────────────────────────────
                NyBEnable = true;
                NyBTradeWindowStart       = DateTime.Parse("11:30", System.Globalization.CultureInfo.InvariantCulture);
                NyBEnableNoNewTradesAfter = true;
                NyBNoNewTradesAfter       = DateTime.Parse("14:00", System.Globalization.CultureInfo.InvariantCulture);
                NyBEnableForcedClose      = false;
                NyBForcedCloseTime        = DateTime.Parse("14:25", System.Globalization.CultureInfo.InvariantCulture);
                NyBMaxSessionProfitTicks=1075; NyBMaxSessionLossTicks=160;
                NyBMaxTradesPerSession=3; NyBMaxLossesPerSession=2;
                // Long
                NyBLongCandleMultiplier=5.19; NyBLongMaxTakeProfitTicks=543;
                NyBLongSlExtraTicks=35; NyBLongMaxSlTicks=186; NyBLongMaxSlToTpRatio=0.46;
                NyBLongUsePriorCandleSl=true; NyBLongSlAtMa=false; NyBLongMoveSlToEntryBar=false;
                NyBLongTrailOffsetTicks=0; NyBLongTrailDelayBars=1; NyBLongTrailCandleOffset=0;
                NyBLongEnableBreakeven=true; NyBLongBreakevenTriggerTicks=142; NyBLongBreakevenOffsetTicks=40;
                NyBLongMaxBarsInTrade=12;
                NyBLongEnablePriceOffsetTrail=true; NyBLongPriceOffsetReductionTicks=18;
                NyBRequireDirectionFlip=true; NyBAllowSameDirectionAfterLoss=true;
                NyBLongEnableWmaFilter=true; NyBLongWmaPeriod=195;
                NyBLongMinBodyPct=43.0;
                NyBLongTrendConfirmBars=18; NyBLongEnableAdxFilter=true; NyBLongAdxPeriod=25; NyBLongAdxMinLevel=13;
                // Short
                NyBShortCandleMultiplier=4.8; NyBShortMaxTakeProfitTicks=590;
                NyBShortSlExtraTicks=16; NyBShortMaxSlTicks=245; NyBShortMaxSlToTpRatio=0.45;
                NyBShortUsePriorCandleSl=true; NyBShortSlAtMa=false; NyBShortMoveSlToEntryBar=false;
                NyBShortTrailOffsetTicks=69; NyBShortTrailDelayBars=2; NyBShortTrailCandleOffset=11;
                NyBShortEnableBreakeven=true; NyBShortBreakevenTriggerTicks=353; NyBShortBreakevenOffsetTicks=86;
                NyBShortMaxBarsInTrade=27;
                NyBShortEnablePriceOffsetTrail=false; NyBShortPriceOffsetReductionTicks=103;
                NyBShortEnableWmaFilter=true; NyBShortWmaPeriod=100;
                NyBShortMinBodyPct=26.0;
                NyBShortTrendConfirmBars=13; NyBShortEnableAdxFilter=true; NyBShortAdxPeriod=18; NyBShortAdxMinLevel=15;

                // ── NY-C ─────────────────────────────────────────────────────
                NyCEnable = true;
                NyCTradeWindowStart       = DateTime.Parse("14:00", System.Globalization.CultureInfo.InvariantCulture);
                NyCEnableNoNewTradesAfter = true;
                NyCNoNewTradesAfter       = DateTime.Parse("14:55", System.Globalization.CultureInfo.InvariantCulture);
                NyCEnableForcedClose      = true;
                NyCForcedCloseTime        = DateTime.Parse("16:00", System.Globalization.CultureInfo.InvariantCulture);
                NyCMaxSessionProfitTicks=1075; NyCMaxSessionLossTicks=160;
                NyCMaxTradesPerSession=3; NyCMaxLossesPerSession=2;
                // Long
                NyCLongCandleMultiplier=4.24; NyCLongMaxTakeProfitTicks=625;
                NyCLongSlExtraTicks=31; NyCLongMaxSlTicks=245; NyCLongMaxSlToTpRatio=0.42;
                NyCLongUsePriorCandleSl=false; NyCLongSlAtMa=false; NyCLongMoveSlToEntryBar=true;
                NyCLongTrailOffsetTicks=1; NyCLongTrailDelayBars=1; NyCLongTrailCandleOffset=0;
                NyCLongEnableBreakeven=true; NyCLongBreakevenTriggerTicks=270; NyCLongBreakevenOffsetTicks=31;
                NyCLongMaxBarsInTrade=18;
                NyCLongEnablePriceOffsetTrail=true; NyCLongPriceOffsetReductionTicks=69;
                NyCRequireDirectionFlip=true; NyCAllowSameDirectionAfterLoss=true;
                NyCLongEnableWmaFilter=true; NyCLongWmaPeriod=117;
                NyCLongMinBodyPct=61.0;
                NyCLongTrendConfirmBars=13; NyCLongEnableAdxFilter=true; NyCLongAdxPeriod=19; NyCLongAdxMinLevel=15;
                // Short
                NyCShortCandleMultiplier=4.18; NyCShortMaxTakeProfitTicks=519;
                NyCShortSlExtraTicks=9; NyCShortMaxSlTicks=123; NyCShortMaxSlToTpRatio=0.42;
                NyCShortUsePriorCandleSl=true; NyCShortSlAtMa=true; NyCShortMoveSlToEntryBar=true;
                NyCShortTrailOffsetTicks=86; NyCShortTrailDelayBars=3; NyCShortTrailCandleOffset=0;
                NyCShortEnableBreakeven=true; NyCShortBreakevenTriggerTicks=207; NyCShortBreakevenOffsetTicks=100;
                NyCShortMaxBarsInTrade=22;
                NyCShortEnablePriceOffsetTrail=true; NyCShortPriceOffsetReductionTicks=7;
                NyCShortEnableWmaFilter=true; NyCShortWmaPeriod=20;
                NyCShortMinBodyPct=5.0;
                NyCShortTrendConfirmBars=9; NyCShortEnableAdxFilter=true; NyCShortAdxPeriod=11; NyCShortAdxMinLevel=20;

                // ── EU-A ─────────────────────────────────────────────────────
                EuAEnable = true;
                EuATradeWindowStart       = DateTime.Parse("02:05", System.Globalization.CultureInfo.InvariantCulture);
                EuAEnableNoNewTradesAfter = true;
                EuANoNewTradesAfter       = DateTime.Parse("03:00", System.Globalization.CultureInfo.InvariantCulture);
                EuAEnableForcedClose      = true;
                EuAForcedCloseTime        = DateTime.Parse("03:55", System.Globalization.CultureInfo.InvariantCulture);
                EuAMaxSessionProfitTicks=1075; EuAMaxSessionLossTicks=160;
                EuAMaxTradesPerSession=2; EuAMaxLossesPerSession=2;
                // Long
                EuALongCandleMultiplier=3.31; EuALongMaxTakeProfitTicks=426;
                EuALongSlExtraTicks=23; EuALongMaxSlTicks=220; EuALongMaxSlToTpRatio=0.49;
                EuALongUsePriorCandleSl=false; EuALongSlAtMa=false; EuALongMoveSlToEntryBar=true;
                EuALongTrailOffsetTicks=1; EuALongTrailDelayBars=3; EuALongTrailCandleOffset=1;
                EuALongEnableBreakeven=true; EuALongBreakevenTriggerTicks=135; EuALongBreakevenOffsetTicks=87;
                EuALongMaxBarsInTrade=19;
                EuALongEnablePriceOffsetTrail=true; EuALongPriceOffsetReductionTicks=100;
                EuARequireDirectionFlip=true; EuAAllowSameDirectionAfterLoss=true;
                EuALongEnableWmaFilter=true; EuALongWmaPeriod=100;
                EuALongMinBodyPct=54.0;
                EuALongTrendConfirmBars=10; EuALongEnableAdxFilter=true; EuALongAdxPeriod=16; EuALongAdxMinLevel=14;
                // Short
                EuAShortCandleMultiplier=4.39; EuAShortMaxTakeProfitTicks=1000;
                EuAShortSlExtraTicks=43; EuAShortMaxSlTicks=326; EuAShortMaxSlToTpRatio=0.45;
                EuAShortUsePriorCandleSl=true; EuAShortSlAtMa=false; EuAShortMoveSlToEntryBar=false;
                EuAShortTrailOffsetTicks=0; EuAShortTrailDelayBars=3; EuAShortTrailCandleOffset=0;
                EuAShortEnableBreakeven=true; EuAShortBreakevenTriggerTicks=150; EuAShortBreakevenOffsetTicks=36;
                EuAShortMaxBarsInTrade=22;
                EuAShortEnablePriceOffsetTrail=true; EuAShortPriceOffsetReductionTicks=30;
                EuAShortEnableWmaFilter=true; EuAShortWmaPeriod=122;
                EuAShortMinBodyPct=33.0;
                EuAShortTrendConfirmBars=12; EuAShortEnableAdxFilter=true; EuAShortAdxPeriod=5; EuAShortAdxMinLevel=11;

                // ── EU-B ─────────────────────────────────────────────────────
                EuBEnable = true;
                EuBTradeWindowStart       = DateTime.Parse("03:00", System.Globalization.CultureInfo.InvariantCulture);
                EuBEnableNoNewTradesAfter = true;
                EuBNoNewTradesAfter       = DateTime.Parse("05:00", System.Globalization.CultureInfo.InvariantCulture);
                EuBEnableForcedClose      = true;
                EuBForcedCloseTime        = DateTime.Parse("05:05", System.Globalization.CultureInfo.InvariantCulture);
                EuBMaxSessionProfitTicks=1075; EuBMaxSessionLossTicks=160;
                EuBMaxTradesPerSession=3; EuBMaxLossesPerSession=2;
                // Long
                EuBLongCandleMultiplier=3.33; EuBLongMaxTakeProfitTicks=635;
                EuBLongSlExtraTicks=42; EuBLongMaxSlTicks=265; EuBLongMaxSlToTpRatio=0.47;
                EuBLongUsePriorCandleSl=true; EuBLongSlAtMa=true; EuBLongMoveSlToEntryBar=true;
                EuBLongTrailOffsetTicks=0; EuBLongTrailDelayBars=1; EuBLongTrailCandleOffset=6;
                EuBLongEnableBreakeven=true; EuBLongBreakevenTriggerTicks=134; EuBLongBreakevenOffsetTicks=12;
                EuBLongMaxBarsInTrade=19;
                EuBLongEnablePriceOffsetTrail=true; EuBLongPriceOffsetReductionTicks=112;
                EuBRequireDirectionFlip=true; EuBAllowSameDirectionAfterLoss=true;
                EuBLongEnableWmaFilter=true; EuBLongWmaPeriod=30;
                EuBLongMinBodyPct=59.0;
                EuBLongTrendConfirmBars=20; EuBLongEnableAdxFilter=true; EuBLongAdxPeriod=12; EuBLongAdxMinLevel=11;
                // Short
                EuBShortCandleMultiplier=4.11; EuBShortMaxTakeProfitTicks=395;
                EuBShortSlExtraTicks=42; EuBShortMaxSlTicks=190; EuBShortMaxSlToTpRatio=0.48;
                EuBShortUsePriorCandleSl=true; EuBShortSlAtMa=true; EuBShortMoveSlToEntryBar=false;
                EuBShortTrailOffsetTicks=38; EuBShortTrailDelayBars=6; EuBShortTrailCandleOffset=0;
                EuBShortEnableBreakeven=true; EuBShortBreakevenTriggerTicks=200; EuBShortBreakevenOffsetTicks=72;
                EuBShortMaxBarsInTrade=20;
                EuBShortEnablePriceOffsetTrail=false; EuBShortPriceOffsetReductionTicks=8;
                EuBShortEnableWmaFilter=true; EuBShortWmaPeriod=137;
                EuBShortMinBodyPct=47.0;
                EuBShortTrendConfirmBars=6; EuBShortEnableAdxFilter=true; EuBShortAdxPeriod=5; EuBShortAdxMinLevel=22;

                // ── EU-C ─────────────────────────────────────────────────────
                EuCEnable = true;
                EuCTradeWindowStart       = DateTime.Parse("05:00", System.Globalization.CultureInfo.InvariantCulture);
                EuCEnableNoNewTradesAfter = true;
                EuCNoNewTradesAfter       = DateTime.Parse("07:45", System.Globalization.CultureInfo.InvariantCulture);
                EuCEnableForcedClose      = true;
                EuCForcedCloseTime        = DateTime.Parse("08:25", System.Globalization.CultureInfo.InvariantCulture);
                EuCMaxSessionProfitTicks=1075; EuCMaxSessionLossTicks=160;
                EuCMaxTradesPerSession=1; EuCMaxLossesPerSession=1;
                // Long
                EuCLongCandleMultiplier=3.41; EuCLongMaxTakeProfitTicks=370;
                EuCLongSlExtraTicks=43; EuCLongMaxSlTicks=166; EuCLongMaxSlToTpRatio=0.47;
                EuCLongUsePriorCandleSl=true; EuCLongSlAtMa=false; EuCLongMoveSlToEntryBar=true;
                EuCLongTrailOffsetTicks=0; EuCLongTrailDelayBars=2; EuCLongTrailCandleOffset=9;
                EuCLongEnableBreakeven=true; EuCLongBreakevenTriggerTicks=115; EuCLongBreakevenOffsetTicks=42;
                EuCLongMaxBarsInTrade=27;
                EuCLongEnablePriceOffsetTrail=false; EuCLongPriceOffsetReductionTicks=7;
                EuCRequireDirectionFlip=true; EuCAllowSameDirectionAfterLoss=false;
                EuCLongEnableWmaFilter=true; EuCLongWmaPeriod=20;
                EuCLongMinBodyPct=57.0;
                EuCLongTrendConfirmBars=16; EuCLongEnableAdxFilter=true; EuCLongAdxPeriod=20; EuCLongAdxMinLevel=11;
                // Short
                EuCShortCandleMultiplier=3.72; EuCShortMaxTakeProfitTicks=670;
                EuCShortSlExtraTicks=69; EuCShortMaxSlTicks=251; EuCShortMaxSlToTpRatio=0.48;
                EuCShortUsePriorCandleSl=true; EuCShortSlAtMa=false; EuCShortMoveSlToEntryBar=false;
                EuCShortTrailOffsetTicks=80; EuCShortTrailDelayBars=1; EuCShortTrailCandleOffset=8;
                EuCShortEnableBreakeven=true; EuCShortBreakevenTriggerTicks=243; EuCShortBreakevenOffsetTicks=47;
                EuCShortMaxBarsInTrade=34;
                EuCShortEnablePriceOffsetTrail=false; EuCShortPriceOffsetReductionTicks=2;
                EuCShortEnableWmaFilter=true; EuCShortWmaPeriod=109;
                EuCShortMinBodyPct=36.0;
                EuCShortTrendConfirmBars=10; EuCShortEnableAdxFilter=true; EuCShortAdxPeriod=5; EuCShortAdxMinLevel=22;

                // ── AS-A ─────────────────────────────────────────────────────
                AsAEnable = true;
                AsATradeWindowStart       = DateTime.Parse("18:30", System.Globalization.CultureInfo.InvariantCulture);
                AsAEnableNoNewTradesAfter = true;
                AsANoNewTradesAfter       = DateTime.Parse("20:00", System.Globalization.CultureInfo.InvariantCulture);
                AsAEnableForcedClose      = false;
                AsAForcedCloseTime        = DateTime.Parse("20:00", System.Globalization.CultureInfo.InvariantCulture);
                AsAMaxSessionProfitTicks=500; AsAMaxSessionLossTicks=200;
                AsAMaxTradesPerSession=2; AsAMaxLossesPerSession=1;
                // Long
                AsALongCandleMultiplier=3.6; AsALongMaxTakeProfitTicks=760;
                AsALongSlExtraTicks=41; AsALongMaxSlTicks=285; AsALongMaxSlToTpRatio=0.49;
                AsALongUsePriorCandleSl=true; AsALongSlAtMa=true; AsALongMoveSlToEntryBar=false;
                AsALongTrailOffsetTicks=53; AsALongTrailDelayBars=12; AsALongTrailCandleOffset=16;
                AsALongEnableBreakeven=true; AsALongBreakevenTriggerTicks=113; AsALongBreakevenOffsetTicks=64;
                AsALongMaxBarsInTrade=31;
                AsALongEnablePriceOffsetTrail=true; AsALongPriceOffsetReductionTicks=112;
                AsARequireDirectionFlip=false; AsAAllowSameDirectionAfterLoss=true;
                AsALongEnableWmaFilter=true; AsALongWmaPeriod=20;
                AsALongMinBodyPct=47.0;
                AsALongTrendConfirmBars=6; AsALongEnableAdxFilter=true; AsALongAdxPeriod=19; AsALongAdxMinLevel=16;
                // Short
                AsAShortCandleMultiplier=3.94; AsAShortMaxTakeProfitTicks=830;
                AsAShortSlExtraTicks=25; AsAShortMaxSlTicks=173; AsAShortMaxSlToTpRatio=0.42;
                AsAShortUsePriorCandleSl=true; AsAShortSlAtMa=true; AsAShortMoveSlToEntryBar=true;
                AsAShortTrailOffsetTicks=3; AsAShortTrailDelayBars=40; AsAShortTrailCandleOffset=6;
                AsAShortEnableBreakeven=true; AsAShortBreakevenTriggerTicks=141; AsAShortBreakevenOffsetTicks=23;
                AsAShortMaxBarsInTrade=42;
                AsAShortEnablePriceOffsetTrail=true; AsAShortPriceOffsetReductionTicks=30;
                AsAShortEnableWmaFilter=true; AsAShortWmaPeriod=20;
                AsAShortMinBodyPct=40.0;
                AsAShortTrendConfirmBars=20; AsAShortEnableAdxFilter=true; AsAShortAdxPeriod=14; AsAShortAdxMinLevel=18;

                // ── AS-B ─────────────────────────────────────────────────────
                AsBEnable = true;
                AsBTradeWindowStart       = DateTime.Parse("20:00", System.Globalization.CultureInfo.InvariantCulture);
                AsBEnableNoNewTradesAfter = true;
                AsBNoNewTradesAfter       = DateTime.Parse("00:00", System.Globalization.CultureInfo.InvariantCulture);
                AsBEnableForcedClose      = true;
                AsBForcedCloseTime        = DateTime.Parse("01:15", System.Globalization.CultureInfo.InvariantCulture);
                AsBMaxSessionProfitTicks=500; AsBMaxSessionLossTicks=200;
                AsBMaxTradesPerSession=2; AsBMaxLossesPerSession=1;
                // Long
                AsBLongCandleMultiplier=2.91; AsBLongMaxTakeProfitTicks=430;
                AsBLongSlExtraTicks=41; AsBLongMaxSlTicks=217; AsBLongMaxSlToTpRatio=0.5;
                AsBLongUsePriorCandleSl=true; AsBLongSlAtMa=true; AsBLongMoveSlToEntryBar=false;
                AsBLongTrailOffsetTicks=21; AsBLongTrailDelayBars=7; AsBLongTrailCandleOffset=14;
                AsBLongEnableBreakeven=true; AsBLongBreakevenTriggerTicks=115; AsBLongBreakevenOffsetTicks=50;
                AsBLongMaxBarsInTrade=26;
                AsBLongEnablePriceOffsetTrail=true; AsBLongPriceOffsetReductionTicks=65;
                AsBRequireDirectionFlip=true; AsBAllowSameDirectionAfterLoss=true;
                AsBLongEnableWmaFilter=true; AsBLongWmaPeriod=41;
                AsBLongMinBodyPct=27.0;
                AsBLongTrendConfirmBars=4; AsBLongEnableAdxFilter=true; AsBLongAdxPeriod=12; AsBLongAdxMinLevel=20;
                // Short
                AsBShortCandleMultiplier=4.9; AsBShortMaxTakeProfitTicks=511;
                AsBShortSlExtraTicks=44; AsBShortMaxSlTicks=163; AsBShortMaxSlToTpRatio=0.39;
                AsBShortUsePriorCandleSl=true; AsBShortSlAtMa=true; AsBShortMoveSlToEntryBar=true;
                AsBShortTrailOffsetTicks=1; AsBShortTrailDelayBars=14; AsBShortTrailCandleOffset=1;
                AsBShortEnableBreakeven=true; AsBShortBreakevenTriggerTicks=140; AsBShortBreakevenOffsetTicks=20;
                AsBShortMaxBarsInTrade=28;
                AsBShortEnablePriceOffsetTrail=true; AsBShortPriceOffsetReductionTicks=40;
                AsBShortEnableWmaFilter=true; AsBShortWmaPeriod=27;
                AsBShortMinBodyPct=31.0;
                AsBShortTrendConfirmBars=18; AsBShortEnableAdxFilter=true; AsBShortAdxPeriod=5; AsBShortAdxMinLevel=23;

                // ── AS-C ─────────────────────────────────────────────────────
                AsCEnable = true;
                AsCTradeWindowStart       = DateTime.Parse("00:05", System.Globalization.CultureInfo.InvariantCulture);
                AsCEnableNoNewTradesAfter = true;
                AsCNoNewTradesAfter       = DateTime.Parse("01:15", System.Globalization.CultureInfo.InvariantCulture);
                AsCEnableForcedClose      = true;
                AsCForcedCloseTime        = DateTime.Parse("02:20", System.Globalization.CultureInfo.InvariantCulture);
                AsCMaxSessionProfitTicks=500; AsCMaxSessionLossTicks=200;
                AsCMaxTradesPerSession=1; AsCMaxLossesPerSession=1;
                // Long
                AsCLongCandleMultiplier=2.36; AsCLongMaxTakeProfitTicks=300;
                AsCLongSlExtraTicks=5; AsCLongMaxSlTicks=155; AsCLongMaxSlToTpRatio=0.36;
                AsCLongUsePriorCandleSl=true; AsCLongSlAtMa=true; AsCLongMoveSlToEntryBar=true;
                AsCLongTrailOffsetTicks=4; AsCLongTrailDelayBars=5; AsCLongTrailCandleOffset=1;
                AsCLongEnableBreakeven=true; AsCLongBreakevenTriggerTicks=180; AsCLongBreakevenOffsetTicks=12;
                AsCLongMaxBarsInTrade=41;
                AsCLongEnablePriceOffsetTrail=true; AsCLongPriceOffsetReductionTicks=37;
                AsCRequireDirectionFlip=true; AsCAllowSameDirectionAfterLoss=true;
                AsCLongEnableWmaFilter=true; AsCLongWmaPeriod=20;
                AsCLongMinBodyPct=24.0;
                AsCLongTrendConfirmBars=7; AsCLongEnableAdxFilter=true; AsCLongAdxPeriod=14; AsCLongAdxMinLevel=12;
                // Short
                AsCShortCandleMultiplier=4.74; AsCShortMaxTakeProfitTicks=425;
                AsCShortSlExtraTicks=49; AsCShortMaxSlTicks=160; AsCShortMaxSlToTpRatio=0.48;
                AsCShortUsePriorCandleSl=true; AsCShortSlAtMa=true; AsCShortMoveSlToEntryBar=true;
                AsCShortTrailOffsetTicks=21; AsCShortTrailDelayBars=15; AsCShortTrailCandleOffset=2;
                AsCShortEnableBreakeven=true; AsCShortBreakevenTriggerTicks=255; AsCShortBreakevenOffsetTicks=75;
                AsCShortMaxBarsInTrade=20;
                AsCShortEnablePriceOffsetTrail=false; AsCShortPriceOffsetReductionTicks=93;
                AsCShortEnableWmaFilter=true; AsCShortWmaPeriod=100;
                AsCShortMinBodyPct=41.0;
                AsCShortTrendConfirmBars=18; AsCShortEnableAdxFilter=true; AsCShortAdxPeriod=22; AsCShortAdxMinLevel=10;
            }
            else if (State == State.DataLoaded)
            {
                for (int sid = 1; sid <= SubSessionCount; sid++)
                {
                    if (!S_Active(sid)) continue;
                    smaHighL[sid] = SMA(High, SD_MaPeriod(sid, 1));
                    smaLowL[sid]  = SMA(Low,  SD_MaPeriod(sid, 1));
                    smaHighS[sid] = SMA(High, SD_MaPeriod(sid,-1));
                    smaLowS[sid]  = SMA(Low,  SD_MaPeriod(sid,-1));
                    emaHighL[sid] = EMA(High, SD_EmaPeriod(sid, 1));
                    emaLowL[sid]  = EMA(Low,  SD_EmaPeriod(sid, 1));
                    emaHighS[sid] = EMA(High, SD_EmaPeriod(sid,-1));
                    emaLowS[sid]  = EMA(Low,  SD_EmaPeriod(sid,-1));
                    wmaLong[sid]  = WMA(Close, SD_WmaPeriod(sid,  1));
                    wmaShort[sid] = WMA(Close, SD_WmaPeriod(sid, -1));
                    adxLong[sid]  = ADX(SD_AdxPeriod(sid,  1));
                    adxShort[sid] = ADX(SD_AdxPeriod(sid, -1));
                    swingInd[sid] = Swing(Math.Max(SD_SwingStrength(sid,1), SD_SwingStrength(sid,-1)));
                }

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
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
            }
            else if (State == State.Transition) { ResetAll(); }
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
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
                DisposeInfoBoxOverlay();
            }
        }

        #endregion

        // =====================================================================
        #region Session Accessor Methods  S_() and SD_()

        private bool S_ParentEnable(int sid) { int p=(sid<=3)?1:(sid<=6)?2:3; return p==1?NyEnable:p==2?EuEnable:AsEnable; }
        private bool S_SubEnable(int sid) { switch(sid){case 1:return NyAEnable;case 2:return NyBEnable;case 3:return NyCEnable;case 4:return EuAEnable;case 5:return EuBEnable;case 6:return EuCEnable;case 7:return AsAEnable;case 8:return AsBEnable;case 9:return AsCEnable;}return false; }
        private bool S_Active(int sid) { return S_ParentEnable(sid) && S_SubEnable(sid); }
        private string S_Label(int sid) { switch(sid){case 1:return"NY-A";case 2:return"NY-B";case 3:return"NY-C";case 4:return"EU-A";case 5:return"EU-B";case 6:return"EU-C";case 7:return"AS-A";case 8:return"AS-B";case 9:return"AS-C";}return"???"; }
        private string GetRegion(int sid) { if(sid<=3)return"NY";if(sid<=6)return"EU";return"AS"; }
        private DateTime S_TradeWindowStart(int sid) { switch(sid){case 1:return NyATradeWindowStart;case 2:return NyBTradeWindowStart;case 3:return NyCTradeWindowStart;case 4:return EuATradeWindowStart;case 5:return EuBTradeWindowStart;case 6:return EuCTradeWindowStart;case 7:return AsATradeWindowStart;case 8:return AsBTradeWindowStart;case 9:return AsCTradeWindowStart;}return default(DateTime); }
        private DateTime S_NoNewTradesAfter(int sid) { switch(sid){case 1:return NyANoNewTradesAfter;case 2:return NyBNoNewTradesAfter;case 3:return NyCNoNewTradesAfter;case 4:return EuANoNewTradesAfter;case 5:return EuBNoNewTradesAfter;case 6:return EuCNoNewTradesAfter;case 7:return AsANoNewTradesAfter;case 8:return AsBNoNewTradesAfter;case 9:return AsCNoNewTradesAfter;}return default(DateTime); }
        private DateTime S_ForcedCloseTime(int sid) { switch(sid){case 1:return NyAForcedCloseTime;case 2:return NyBForcedCloseTime;case 3:return NyCForcedCloseTime;case 4:return EuAForcedCloseTime;case 5:return EuBForcedCloseTime;case 6:return EuCForcedCloseTime;case 7:return AsAForcedCloseTime;case 8:return AsBForcedCloseTime;case 9:return AsCForcedCloseTime;}return default(DateTime); }
        private bool S_EnableNoNewTradesAfter(int sid) { switch(sid){case 1:return NyAEnableNoNewTradesAfter;case 2:return NyBEnableNoNewTradesAfter;case 3:return NyCEnableNoNewTradesAfter;case 4:return EuAEnableNoNewTradesAfter;case 5:return EuBEnableNoNewTradesAfter;case 6:return EuCEnableNoNewTradesAfter;case 7:return AsAEnableNoNewTradesAfter;case 8:return AsBEnableNoNewTradesAfter;case 9:return AsCEnableNoNewTradesAfter;}return false; }
        private bool S_EnableForcedClose(int sid) { switch(sid){case 1:return NyAEnableForcedClose;case 2:return NyBEnableForcedClose;case 3:return NyCEnableForcedClose;case 4:return EuAEnableForcedClose;case 5:return EuBEnableForcedClose;case 6:return EuCEnableForcedClose;case 7:return AsAEnableForcedClose;case 8:return AsBEnableForcedClose;case 9:return AsCEnableForcedClose;}return false; }
        private int  S_Contracts(int sid) { return GlobalContracts; }
        private int  S_MaxTradesPerSession(int sid) { switch(sid){case 1:return NyAMaxTradesPerSession;case 2:return NyBMaxTradesPerSession;case 3:return NyCMaxTradesPerSession;case 4:return EuAMaxTradesPerSession;case 5:return EuBMaxTradesPerSession;case 6:return EuCMaxTradesPerSession;case 7:return AsAMaxTradesPerSession;case 8:return AsBMaxTradesPerSession;case 9:return AsCMaxTradesPerSession;}return 3; }
        private int  S_MaxLossesPerSession(int sid) { switch(sid){case 1:return NyAMaxLossesPerSession;case 2:return NyBMaxLossesPerSession;case 3:return NyCMaxLossesPerSession;case 4:return EuAMaxLossesPerSession;case 5:return EuBMaxLossesPerSession;case 6:return EuCMaxLossesPerSession;case 7:return AsAMaxLossesPerSession;case 8:return AsBMaxLossesPerSession;case 9:return AsCMaxLossesPerSession;}return 2; }
        private int  S_MaxSessionProfitTicks(int sid) { switch(sid){case 1:return NyAMaxSessionProfitTicks;case 2:return NyBMaxSessionProfitTicks;case 3:return NyCMaxSessionProfitTicks;case 4:return EuAMaxSessionProfitTicks;case 5:return EuBMaxSessionProfitTicks;case 6:return EuCMaxSessionProfitTicks;case 7:return AsAMaxSessionProfitTicks;case 8:return AsBMaxSessionProfitTicks;case 9:return AsCMaxSessionProfitTicks;}return 1075; }
        private int  S_MaxSessionLossTicks(int sid) { switch(sid){case 1:return NyAMaxSessionLossTicks;case 2:return NyBMaxSessionLossTicks;case 3:return NyCMaxSessionLossTicks;case 4:return EuAMaxSessionLossTicks;case 5:return EuBMaxSessionLossTicks;case 6:return EuCMaxSessionLossTicks;case 7:return AsAMaxSessionLossTicks;case 8:return AsBMaxSessionLossTicks;case 9:return AsCMaxSessionLossTicks;}return 160; }
        private bool S_EnableLongTrades(int sid)  { return true; }
        private bool S_EnableShortTrades(int sid) { return true; }
        private bool   S_GetLimitsReached(int sid) { return subLimitsReached[sid]; }
        private void   S_SetLimitsReached(int sid, bool v) { subLimitsReached[sid]=v; }
        private int    S_GetTradeCount(int sid)  { return subTradeCount[sid]; }
        private int    S_GetLossCount(int sid)   { return subLossCount[sid]; }
        private double S_GetPnLTicks(int sid)    { return subPnLTicks[sid]; }
        private int S_EngulfingExitAfterBars(int sid)    { switch(sid){case 1:return NyAEngulfingExitAfterBars;case 2:return NyBEngulfingExitAfterBars;case 3:return NyCEngulfingExitAfterBars;case 4:return EuAEngulfingExitAfterBars;case 5:return EuBEngulfingExitAfterBars;case 6:return EuCEngulfingExitAfterBars;case 7:return AsAEngulfingExitAfterBars;case 8:return AsBEngulfingExitAfterBars;case 9:return AsCEngulfingExitAfterBars;}return 0; }
        private int S_EngulfingExitPaddingTicks(int sid) { switch(sid){case 1:return NyAEngulfingExitPaddingTicks;case 2:return NyBEngulfingExitPaddingTicks;case 3:return NyCEngulfingExitPaddingTicks;case 4:return EuAEngulfingExitPaddingTicks;case 5:return EuBEngulfingExitPaddingTicks;case 6:return EuCEngulfingExitPaddingTicks;case 7:return AsAEngulfingExitPaddingTicks;case 8:return AsBEngulfingExitPaddingTicks;case 9:return AsCEngulfingExitPaddingTicks;}return 0; }

        // ── SD_ per-session per-direction entry parameter dispatch ─────────────
        private int    SD_MaPeriod(int sid,int d)          { return GlobalMaPeriod; }
        private int    SD_EmaPeriod(int sid,int d)         { return GlobalEmaPeriod; }
        private MAMode SD_MaType(int sid,int d)            { return GlobalMaType; }
        private double SD_CandleMultiplier(int sid,int d)  { if(d==1)switch(sid){case 1:return NyALongCandleMultiplier;case 2:return NyBLongCandleMultiplier;case 3:return NyCLongCandleMultiplier;case 4:return EuALongCandleMultiplier;case 5:return EuBLongCandleMultiplier;case 6:return EuCLongCandleMultiplier;case 7:return AsALongCandleMultiplier;case 8:return AsBLongCandleMultiplier;case 9:return AsCLongCandleMultiplier;} else switch(sid){case 1:return NyAShortCandleMultiplier;case 2:return NyBShortCandleMultiplier;case 3:return NyCShortCandleMultiplier;case 4:return EuAShortCandleMultiplier;case 5:return EuBShortCandleMultiplier;case 6:return EuCShortCandleMultiplier;case 7:return AsAShortCandleMultiplier;case 8:return AsBShortCandleMultiplier;case 9:return AsCShortCandleMultiplier;}return 4.3; }
        private int    SD_MaxTakeProfitTicks(int sid,int d){ if(d==1)switch(sid){case 1:return NyALongMaxTakeProfitTicks;case 2:return NyBLongMaxTakeProfitTicks;case 3:return NyCLongMaxTakeProfitTicks;case 4:return EuALongMaxTakeProfitTicks;case 5:return EuBLongMaxTakeProfitTicks;case 6:return EuCLongMaxTakeProfitTicks;case 7:return AsALongMaxTakeProfitTicks;case 8:return AsBLongMaxTakeProfitTicks;case 9:return AsCLongMaxTakeProfitTicks;} else switch(sid){case 1:return NyAShortMaxTakeProfitTicks;case 2:return NyBShortMaxTakeProfitTicks;case 3:return NyCShortMaxTakeProfitTicks;case 4:return EuAShortMaxTakeProfitTicks;case 5:return EuBShortMaxTakeProfitTicks;case 6:return EuCShortMaxTakeProfitTicks;case 7:return AsAShortMaxTakeProfitTicks;case 8:return AsBShortMaxTakeProfitTicks;case 9:return AsCShortMaxTakeProfitTicks;}return 1179; }
        private int    SD_SlExtraTicks(int sid,int d)      { if(d==1)switch(sid){case 1:return NyALongSlExtraTicks;case 2:return NyBLongSlExtraTicks;case 3:return NyCLongSlExtraTicks;case 4:return EuALongSlExtraTicks;case 5:return EuBLongSlExtraTicks;case 6:return EuCLongSlExtraTicks;case 7:return AsALongSlExtraTicks;case 8:return AsBLongSlExtraTicks;case 9:return AsCLongSlExtraTicks;} else switch(sid){case 1:return NyAShortSlExtraTicks;case 2:return NyBShortSlExtraTicks;case 3:return NyCShortSlExtraTicks;case 4:return EuAShortSlExtraTicks;case 5:return EuBShortSlExtraTicks;case 6:return EuCShortSlExtraTicks;case 7:return AsAShortSlExtraTicks;case 8:return AsBShortSlExtraTicks;case 9:return AsCShortSlExtraTicks;}return 42; }
        private int    SD_MaxSlTicks(int sid,int d)        { if(d==1)switch(sid){case 1:return NyALongMaxSlTicks;case 2:return NyBLongMaxSlTicks;case 3:return NyCLongMaxSlTicks;case 4:return EuALongMaxSlTicks;case 5:return EuBLongMaxSlTicks;case 6:return EuCLongMaxSlTicks;case 7:return AsALongMaxSlTicks;case 8:return AsBLongMaxSlTicks;case 9:return AsCLongMaxSlTicks;} else switch(sid){case 1:return NyAShortMaxSlTicks;case 2:return NyBShortMaxSlTicks;case 3:return NyCShortMaxSlTicks;case 4:return EuAShortMaxSlTicks;case 5:return EuBShortMaxSlTicks;case 6:return EuCShortMaxSlTicks;case 7:return AsAShortMaxSlTicks;case 8:return AsBShortMaxSlTicks;case 9:return AsCShortMaxSlTicks;}return 331; }
        private double SD_MaxSlToTpRatio(int sid,int d)    { if(d==1)switch(sid){case 1:return NyALongMaxSlToTpRatio;case 2:return NyBLongMaxSlToTpRatio;case 3:return NyCLongMaxSlToTpRatio;case 4:return EuALongMaxSlToTpRatio;case 5:return EuBLongMaxSlToTpRatio;case 6:return EuCLongMaxSlToTpRatio;case 7:return AsALongMaxSlToTpRatio;case 8:return AsBLongMaxSlToTpRatio;case 9:return AsCLongMaxSlToTpRatio;} else switch(sid){case 1:return NyAShortMaxSlToTpRatio;case 2:return NyBShortMaxSlToTpRatio;case 3:return NyCShortMaxSlToTpRatio;case 4:return EuAShortMaxSlToTpRatio;case 5:return EuBShortMaxSlToTpRatio;case 6:return EuCShortMaxSlToTpRatio;case 7:return AsAShortMaxSlToTpRatio;case 8:return AsBShortMaxSlToTpRatio;case 9:return AsCShortMaxSlToTpRatio;}return 0.48; }
        private bool   SD_UsePriorCandleSl(int sid,int d) { if(d==1)switch(sid){case 1:return NyALongUsePriorCandleSl;case 2:return NyBLongUsePriorCandleSl;case 3:return NyCLongUsePriorCandleSl;case 4:return EuALongUsePriorCandleSl;case 5:return EuBLongUsePriorCandleSl;case 6:return EuCLongUsePriorCandleSl;case 7:return AsALongUsePriorCandleSl;case 8:return AsBLongUsePriorCandleSl;case 9:return AsCLongUsePriorCandleSl;} else switch(sid){case 1:return NyAShortUsePriorCandleSl;case 2:return NyBShortUsePriorCandleSl;case 3:return NyCShortUsePriorCandleSl;case 4:return EuAShortUsePriorCandleSl;case 5:return EuBShortUsePriorCandleSl;case 6:return EuCShortUsePriorCandleSl;case 7:return AsAShortUsePriorCandleSl;case 8:return AsBShortUsePriorCandleSl;case 9:return AsCShortUsePriorCandleSl;}return true; }
        private bool   SD_SlAtMa(int sid,int d)            { if(d==1)switch(sid){case 1:return NyALongSlAtMa;case 2:return NyBLongSlAtMa;case 3:return NyCLongSlAtMa;case 4:return EuALongSlAtMa;case 5:return EuBLongSlAtMa;case 6:return EuCLongSlAtMa;case 7:return AsALongSlAtMa;case 8:return AsBLongSlAtMa;case 9:return AsCLongSlAtMa;} else switch(sid){case 1:return NyAShortSlAtMa;case 2:return NyBShortSlAtMa;case 3:return NyCShortSlAtMa;case 4:return EuAShortSlAtMa;case 5:return EuBShortSlAtMa;case 6:return EuCShortSlAtMa;case 7:return AsAShortSlAtMa;case 8:return AsBShortSlAtMa;case 9:return AsCShortSlAtMa;}return false; }
        private bool   SD_MoveSlToEntryBar(int sid,int d)  { if(d==1)switch(sid){case 1:return NyALongMoveSlToEntryBar;case 2:return NyBLongMoveSlToEntryBar;case 3:return NyCLongMoveSlToEntryBar;case 4:return EuALongMoveSlToEntryBar;case 5:return EuBLongMoveSlToEntryBar;case 6:return EuCLongMoveSlToEntryBar;case 7:return AsALongMoveSlToEntryBar;case 8:return AsBLongMoveSlToEntryBar;case 9:return AsCLongMoveSlToEntryBar;} else switch(sid){case 1:return NyAShortMoveSlToEntryBar;case 2:return NyBShortMoveSlToEntryBar;case 3:return NyCShortMoveSlToEntryBar;case 4:return EuAShortMoveSlToEntryBar;case 5:return EuBShortMoveSlToEntryBar;case 6:return EuCShortMoveSlToEntryBar;case 7:return AsAShortMoveSlToEntryBar;case 8:return AsBShortMoveSlToEntryBar;case 9:return AsCShortMoveSlToEntryBar;}return false; }
        private int    SD_TrailOffsetTicks(int sid,int d)  { if(d==1)switch(sid){case 1:return NyALongTrailOffsetTicks;case 2:return NyBLongTrailOffsetTicks;case 3:return NyCLongTrailOffsetTicks;case 4:return EuALongTrailOffsetTicks;case 5:return EuBLongTrailOffsetTicks;case 6:return EuCLongTrailOffsetTicks;case 7:return AsALongTrailOffsetTicks;case 8:return AsBLongTrailOffsetTicks;case 9:return AsCLongTrailOffsetTicks;} else switch(sid){case 1:return NyAShortTrailOffsetTicks;case 2:return NyBShortTrailOffsetTicks;case 3:return NyCShortTrailOffsetTicks;case 4:return EuAShortTrailOffsetTicks;case 5:return EuBShortTrailOffsetTicks;case 6:return EuCShortTrailOffsetTicks;case 7:return AsAShortTrailOffsetTicks;case 8:return AsBShortTrailOffsetTicks;case 9:return AsCShortTrailOffsetTicks;}return 41; }
        private int    SD_TrailDelayBars(int sid,int d)    { if(d==1)switch(sid){case 1:return NyALongTrailDelayBars;case 2:return NyBLongTrailDelayBars;case 3:return NyCLongTrailDelayBars;case 4:return EuALongTrailDelayBars;case 5:return EuBLongTrailDelayBars;case 6:return EuCLongTrailDelayBars;case 7:return AsALongTrailDelayBars;case 8:return AsBLongTrailDelayBars;case 9:return AsCLongTrailDelayBars;} else switch(sid){case 1:return NyAShortTrailDelayBars;case 2:return NyBShortTrailDelayBars;case 3:return NyCShortTrailDelayBars;case 4:return EuAShortTrailDelayBars;case 5:return EuBShortTrailDelayBars;case 6:return EuCShortTrailDelayBars;case 7:return AsAShortTrailDelayBars;case 8:return AsBShortTrailDelayBars;case 9:return AsCShortTrailDelayBars;}return 2; }
        private int    SD_TrailCandleOffset(int sid,int d) { if(d==1)switch(sid){case 1:return NyALongTrailCandleOffset;case 2:return NyBLongTrailCandleOffset;case 3:return NyCLongTrailCandleOffset;case 4:return EuALongTrailCandleOffset;case 5:return EuBLongTrailCandleOffset;case 6:return EuCLongTrailCandleOffset;case 7:return AsALongTrailCandleOffset;case 8:return AsBLongTrailCandleOffset;case 9:return AsCLongTrailCandleOffset;} else switch(sid){case 1:return NyAShortTrailCandleOffset;case 2:return NyBShortTrailCandleOffset;case 3:return NyCShortTrailCandleOffset;case 4:return EuAShortTrailCandleOffset;case 5:return EuBShortTrailCandleOffset;case 6:return EuCShortTrailCandleOffset;case 7:return AsAShortTrailCandleOffset;case 8:return AsBShortTrailCandleOffset;case 9:return AsCShortTrailCandleOffset;}return 0; }
        private bool   SD_EnableBreakeven(int sid,int d)   { if(d==1)switch(sid){case 1:return NyALongEnableBreakeven;case 2:return NyBLongEnableBreakeven;case 3:return NyCLongEnableBreakeven;case 4:return EuALongEnableBreakeven;case 5:return EuBLongEnableBreakeven;case 6:return EuCLongEnableBreakeven;case 7:return AsALongEnableBreakeven;case 8:return AsBLongEnableBreakeven;case 9:return AsCLongEnableBreakeven;} else switch(sid){case 1:return NyAShortEnableBreakeven;case 2:return NyBShortEnableBreakeven;case 3:return NyCShortEnableBreakeven;case 4:return EuAShortEnableBreakeven;case 5:return EuBShortEnableBreakeven;case 6:return EuCShortEnableBreakeven;case 7:return AsAShortEnableBreakeven;case 8:return AsBShortEnableBreakeven;case 9:return AsCShortEnableBreakeven;}return true; }
        private BEMode2 SD_BreakevenMode(int sid,int d)    { return BEMode2.FixedTicks; }
        private int    SD_BreakevenTriggerTicks(int sid,int d) { if(d==1)switch(sid){case 1:return NyALongBreakevenTriggerTicks;case 2:return NyBLongBreakevenTriggerTicks;case 3:return NyCLongBreakevenTriggerTicks;case 4:return EuALongBreakevenTriggerTicks;case 5:return EuBLongBreakevenTriggerTicks;case 6:return EuCLongBreakevenTriggerTicks;case 7:return AsALongBreakevenTriggerTicks;case 8:return AsBLongBreakevenTriggerTicks;case 9:return AsCLongBreakevenTriggerTicks;} else switch(sid){case 1:return NyAShortBreakevenTriggerTicks;case 2:return NyBShortBreakevenTriggerTicks;case 3:return NyCShortBreakevenTriggerTicks;case 4:return EuAShortBreakevenTriggerTicks;case 5:return EuBShortBreakevenTriggerTicks;case 6:return EuCShortBreakevenTriggerTicks;case 7:return AsAShortBreakevenTriggerTicks;case 8:return AsBShortBreakevenTriggerTicks;case 9:return AsCShortBreakevenTriggerTicks;}return 357; }
        private double SD_BreakevenCandlePct(int sid,int d){ return 50.0; }
        private int    SD_BreakevenOffsetTicks(int sid,int d) { if(d==1)switch(sid){case 1:return NyALongBreakevenOffsetTicks;case 2:return NyBLongBreakevenOffsetTicks;case 3:return NyCLongBreakevenOffsetTicks;case 4:return EuALongBreakevenOffsetTicks;case 5:return EuBLongBreakevenOffsetTicks;case 6:return EuCLongBreakevenOffsetTicks;case 7:return AsALongBreakevenOffsetTicks;case 8:return AsBLongBreakevenOffsetTicks;case 9:return AsCLongBreakevenOffsetTicks;} else switch(sid){case 1:return NyAShortBreakevenOffsetTicks;case 2:return NyBShortBreakevenOffsetTicks;case 3:return NyCShortBreakevenOffsetTicks;case 4:return EuAShortBreakevenOffsetTicks;case 5:return EuBShortBreakevenOffsetTicks;case 6:return EuCShortBreakevenOffsetTicks;case 7:return AsAShortBreakevenOffsetTicks;case 8:return AsBShortBreakevenOffsetTicks;case 9:return AsCShortBreakevenOffsetTicks;}return 3; }
        private int    SD_MaxBarsInTrade(int sid,int d)    { if(d==1)switch(sid){case 1:return NyALongMaxBarsInTrade;case 2:return NyBLongMaxBarsInTrade;case 3:return NyCLongMaxBarsInTrade;case 4:return EuALongMaxBarsInTrade;case 5:return EuBLongMaxBarsInTrade;case 6:return EuCLongMaxBarsInTrade;case 7:return AsALongMaxBarsInTrade;case 8:return AsBLongMaxBarsInTrade;case 9:return AsCLongMaxBarsInTrade;} else switch(sid){case 1:return NyAShortMaxBarsInTrade;case 2:return NyBShortMaxBarsInTrade;case 3:return NyCShortMaxBarsInTrade;case 4:return EuAShortMaxBarsInTrade;case 5:return EuBShortMaxBarsInTrade;case 6:return EuCShortMaxBarsInTrade;case 7:return AsAShortMaxBarsInTrade;case 8:return AsBShortMaxBarsInTrade;case 9:return AsCShortMaxBarsInTrade;}return 39; }
        private bool   SD_EnablePriceOffsetTrail(int sid,int d) { if(d==1)switch(sid){case 1:return NyALongEnablePriceOffsetTrail;case 2:return NyBLongEnablePriceOffsetTrail;case 3:return NyCLongEnablePriceOffsetTrail;case 4:return EuALongEnablePriceOffsetTrail;case 5:return EuBLongEnablePriceOffsetTrail;case 6:return EuCLongEnablePriceOffsetTrail;case 7:return AsALongEnablePriceOffsetTrail;case 8:return AsBLongEnablePriceOffsetTrail;case 9:return AsCLongEnablePriceOffsetTrail;} else switch(sid){case 1:return NyAShortEnablePriceOffsetTrail;case 2:return NyBShortEnablePriceOffsetTrail;case 3:return NyCShortEnablePriceOffsetTrail;case 4:return EuAShortEnablePriceOffsetTrail;case 5:return EuBShortEnablePriceOffsetTrail;case 6:return EuCShortEnablePriceOffsetTrail;case 7:return AsAShortEnablePriceOffsetTrail;case 8:return AsBShortEnablePriceOffsetTrail;case 9:return AsCShortEnablePriceOffsetTrail;}return false; }
        private int    SD_PriceOffsetReductionTicks(int sid,int d) { if(d==1)switch(sid){case 1:return NyALongPriceOffsetReductionTicks;case 2:return NyBLongPriceOffsetReductionTicks;case 3:return NyCLongPriceOffsetReductionTicks;case 4:return EuALongPriceOffsetReductionTicks;case 5:return EuBLongPriceOffsetReductionTicks;case 6:return EuCLongPriceOffsetReductionTicks;case 7:return AsALongPriceOffsetReductionTicks;case 8:return AsBLongPriceOffsetReductionTicks;case 9:return AsCLongPriceOffsetReductionTicks;} else switch(sid){case 1:return NyAShortPriceOffsetReductionTicks;case 2:return NyBShortPriceOffsetReductionTicks;case 3:return NyCShortPriceOffsetReductionTicks;case 4:return EuAShortPriceOffsetReductionTicks;case 5:return EuBShortPriceOffsetReductionTicks;case 6:return EuCShortPriceOffsetReductionTicks;case 7:return AsAShortPriceOffsetReductionTicks;case 8:return AsBShortPriceOffsetReductionTicks;case 9:return AsCShortPriceOffsetReductionTicks;}return 0; }
        private bool   SD_EnableOpposingBarExit(int sid,int d) { return false; }
        private bool   SD_EnableEngulfingExit(int sid,int d) { return false; } // replaced by EngulfingExitAfterBars
        private bool   SD_RequireDirectionFlip(int sid,int d) { switch(sid){case 1:return NyARequireDirectionFlip;case 2:return NyBRequireDirectionFlip;case 3:return NyCRequireDirectionFlip;case 4:return EuARequireDirectionFlip;case 5:return EuBRequireDirectionFlip;case 6:return EuCRequireDirectionFlip;case 7:return AsARequireDirectionFlip;case 8:return AsBRequireDirectionFlip;case 9:return AsCRequireDirectionFlip;}return true; }
        private bool   SD_AllowSameDirectionAfterLoss(int sid,int d) { switch(sid){case 1:return NyAAllowSameDirectionAfterLoss;case 2:return NyBAllowSameDirectionAfterLoss;case 3:return NyCAllowSameDirectionAfterLoss;case 4:return EuAAllowSameDirectionAfterLoss;case 5:return EuBAllowSameDirectionAfterLoss;case 6:return EuCAllowSameDirectionAfterLoss;case 7:return AsAAllowSameDirectionAfterLoss;case 8:return AsBAllowSameDirectionAfterLoss;case 9:return AsCAllowSameDirectionAfterLoss;}return true; }
        private bool   SD_RequireMaSlope(int sid,int d)    { return false; }
        private int    SD_MaSlopeLookback(int sid,int d)   { return 1; }
        private bool   SD_EnableWmaFilter(int sid,int d)   { if(d==1)switch(sid){case 1:return NyALongEnableWmaFilter;case 2:return NyBLongEnableWmaFilter;case 3:return NyCLongEnableWmaFilter;case 4:return EuALongEnableWmaFilter;case 5:return EuBLongEnableWmaFilter;case 6:return EuCLongEnableWmaFilter;case 7:return AsALongEnableWmaFilter;case 8:return AsBLongEnableWmaFilter;case 9:return AsCLongEnableWmaFilter;} else switch(sid){case 1:return NyAShortEnableWmaFilter;case 2:return NyBShortEnableWmaFilter;case 3:return NyCShortEnableWmaFilter;case 4:return EuAShortEnableWmaFilter;case 5:return EuBShortEnableWmaFilter;case 6:return EuCShortEnableWmaFilter;case 7:return AsAShortEnableWmaFilter;case 8:return AsBShortEnableWmaFilter;case 9:return AsCShortEnableWmaFilter;}return true; }
        private int    SD_WmaPeriod(int sid,int d)         { if(d==1)switch(sid){case 1:return NyALongWmaPeriod;case 2:return NyBLongWmaPeriod;case 3:return NyCLongWmaPeriod;case 4:return EuALongWmaPeriod;case 5:return EuBLongWmaPeriod;case 6:return EuCLongWmaPeriod;case 7:return AsALongWmaPeriod;case 8:return AsBLongWmaPeriod;case 9:return AsCLongWmaPeriod;} else switch(sid){case 1:return NyAShortWmaPeriod;case 2:return NyBShortWmaPeriod;case 3:return NyCShortWmaPeriod;case 4:return EuAShortWmaPeriod;case 5:return EuBShortWmaPeriod;case 6:return EuCShortWmaPeriod;case 7:return AsAShortWmaPeriod;case 8:return AsBShortWmaPeriod;case 9:return AsCShortWmaPeriod;}return 82; }
        private int    SD_MaxSignalCandleTicks(int sid,int d) { return 0; }
        private double SD_MinBodyPct(int sid,int d)        { if(d==1)switch(sid){case 1:return NyALongMinBodyPct;case 2:return NyBLongMinBodyPct;case 3:return NyCLongMinBodyPct;case 4:return EuALongMinBodyPct;case 5:return EuBLongMinBodyPct;case 6:return EuCLongMinBodyPct;case 7:return AsALongMinBodyPct;case 8:return AsBLongMinBodyPct;case 9:return AsCLongMinBodyPct;} else switch(sid){case 1:return NyAShortMinBodyPct;case 2:return NyBShortMinBodyPct;case 3:return NyCShortMinBodyPct;case 4:return EuAShortMinBodyPct;case 5:return EuBShortMinBodyPct;case 6:return EuCShortMinBodyPct;case 7:return AsAShortMinBodyPct;case 8:return AsBShortMinBodyPct;case 9:return AsCShortMinBodyPct;}return 0.0; }
        private int    SD_TrendConfirmBars(int sid,int d)  { if(d==1)switch(sid){case 1:return NyALongTrendConfirmBars;case 2:return NyBLongTrendConfirmBars;case 3:return NyCLongTrendConfirmBars;case 4:return EuALongTrendConfirmBars;case 5:return EuBLongTrendConfirmBars;case 6:return EuCLongTrendConfirmBars;case 7:return AsALongTrendConfirmBars;case 8:return AsBLongTrendConfirmBars;case 9:return AsCLongTrendConfirmBars;} else switch(sid){case 1:return NyAShortTrendConfirmBars;case 2:return NyBShortTrendConfirmBars;case 3:return NyCShortTrendConfirmBars;case 4:return EuAShortTrendConfirmBars;case 5:return EuBShortTrendConfirmBars;case 6:return EuCShortTrendConfirmBars;case 7:return AsAShortTrendConfirmBars;case 8:return AsBShortTrendConfirmBars;case 9:return AsCShortTrendConfirmBars;}return 13; }
        private int    SD_MaxDistanceFromMaTicks(int sid,int d) { return 0; }
        private bool   SD_EnableAdxFilter(int sid,int d)   { if(d==1)switch(sid){case 1:return NyALongEnableAdxFilter;case 2:return NyBLongEnableAdxFilter;case 3:return NyCLongEnableAdxFilter;case 4:return EuALongEnableAdxFilter;case 5:return EuBLongEnableAdxFilter;case 6:return EuCLongEnableAdxFilter;case 7:return AsALongEnableAdxFilter;case 8:return AsBLongEnableAdxFilter;case 9:return AsCLongEnableAdxFilter;} else switch(sid){case 1:return NyAShortEnableAdxFilter;case 2:return NyBShortEnableAdxFilter;case 3:return NyCShortEnableAdxFilter;case 4:return EuAShortEnableAdxFilter;case 5:return EuBShortEnableAdxFilter;case 6:return EuCShortEnableAdxFilter;case 7:return AsAShortEnableAdxFilter;case 8:return AsBShortEnableAdxFilter;case 9:return AsCShortEnableAdxFilter;}return true; }
        private int    SD_AdxPeriod(int sid,int d)         { if(d==1)switch(sid){case 1:return NyALongAdxPeriod;case 2:return NyBLongAdxPeriod;case 3:return NyCLongAdxPeriod;case 4:return EuALongAdxPeriod;case 5:return EuBLongAdxPeriod;case 6:return EuCLongAdxPeriod;case 7:return AsALongAdxPeriod;case 8:return AsBLongAdxPeriod;case 9:return AsCLongAdxPeriod;} else switch(sid){case 1:return NyAShortAdxPeriod;case 2:return NyBShortAdxPeriod;case 3:return NyCShortAdxPeriod;case 4:return EuAShortAdxPeriod;case 5:return EuBShortAdxPeriod;case 6:return EuCShortAdxPeriod;case 7:return AsAShortAdxPeriod;case 8:return AsBShortAdxPeriod;case 9:return AsCShortAdxPeriod;}return 3; }
        private int    SD_AdxMinLevel(int sid,int d)       { if(d==1)switch(sid){case 1:return NyALongAdxMinLevel;case 2:return NyBLongAdxMinLevel;case 3:return NyCLongAdxMinLevel;case 4:return EuALongAdxMinLevel;case 5:return EuBLongAdxMinLevel;case 6:return EuCLongAdxMinLevel;case 7:return AsALongAdxMinLevel;case 8:return AsBLongAdxMinLevel;case 9:return AsCLongAdxMinLevel;} else switch(sid){case 1:return NyAShortAdxMinLevel;case 2:return NyBShortAdxMinLevel;case 3:return NyCShortAdxMinLevel;case 4:return EuAShortAdxMinLevel;case 5:return EuBShortAdxMinLevel;case 6:return EuCShortAdxMinLevel;case 7:return AsAShortAdxMinLevel;case 8:return AsBShortAdxMinLevel;case 9:return AsCShortAdxMinLevel;}return 15; }

        private MichalEntryMode SD_EntryMode(int sid,int d) { return MichalEntryMode.Market; } // V1.00: Market only for now, per-session override in v1.01
        private int    SD_LimitOffsetTicks(int sid,int d)    { return 1; } // V1.00: default placeholder
        private double SD_LimitRetracementPct(int sid,int d) { return 1.0; } // V1.00: default placeholder
        private MichalTPMode SD_TakeProfitMode(int sid,int d) { return MichalTPMode.CandleMultiple; } // V1.00: CandleMultiple as V106 default
        private int    SD_TakeProfitTicks(int sid,int d)     { return 310; } // V1.00: V106 default fallback
        private int    SD_SwingStrength(int sid,int d)       { return 1; } // V1.00: V106 default

        #endregion

        // =====================================================================
        #region MA Helpers (per session + direction)

        private double GetUpperMA(int sid, int dir, int barsAgo)
        {
            SMA sh = dir==1 ? smaHighL[sid] : smaHighS[sid];
            EMA eh = dir==1 ? emaHighL[sid] : emaHighS[sid];
            switch (SD_MaType(sid,dir))
            {
                case MAMode.SMA:  return sh[barsAgo];
                case MAMode.EMA:  return eh[barsAgo];
                case MAMode.Both: return Math.Max(sh[barsAgo], eh[barsAgo]);
                default:          return sh[barsAgo];
            }
        }
        private double GetLowerMA(int sid, int dir, int barsAgo)
        {
            SMA sl = dir==1 ? smaLowL[sid] : smaLowS[sid];
            EMA el = dir==1 ? emaLowL[sid] : emaLowS[sid];
            switch (SD_MaType(sid,dir))
            {
                case MAMode.SMA:  return sl[barsAgo];
                case MAMode.EMA:  return el[barsAgo];
                case MAMode.Both: return Math.Min(sl[barsAgo], el[barsAgo]);
                default:          return sl[barsAgo];
            }
        }
        // For trailing stops — long uses lower MA, short uses upper MA
        private double GetLowerMAForTrail(int sid, int dir)
        {
            SMA sl = dir==1 ? smaLowL[sid] : smaLowS[sid];
            EMA el = dir==1 ? emaLowL[sid] : emaLowS[sid];
            switch (SD_MaType(sid,dir))
            {
                case MAMode.SMA:  return sl[0];
                case MAMode.EMA:  return el[0];
                case MAMode.Both: return Math.Max(sl[0], el[0]);
                default:          return sl[0];
            }
        }
        private double GetUpperMAForTrail(int sid, int dir)
        {
            SMA sh = dir==1 ? smaHighL[sid] : smaHighS[sid];
            EMA eh = dir==1 ? emaHighL[sid] : emaHighS[sid];
            switch (SD_MaType(sid,dir))
            {
                case MAMode.SMA:  return sh[0];
                case MAMode.EMA:  return eh[0];
                case MAMode.Both: return Math.Min(sh[0], eh[0]);
                default:          return sh[0];
            }
        }
        private bool IsSlopeRising(int sid, int dir, int lb)
        {
            SMA sh=dir==1?smaHighL[sid]:smaHighS[sid]; SMA sl=dir==1?smaLowL[sid]:smaLowS[sid];
            EMA eh=dir==1?emaHighL[sid]:emaHighS[sid]; EMA el=dir==1?emaLowL[sid]:emaLowS[sid];
            bool s = sh[0]>sh[lb] && sl[0]>sl[lb];
            bool e = eh[0]>eh[lb] && el[0]>el[lb];
            switch(SD_MaType(sid,dir)){ case MAMode.SMA:return s; case MAMode.EMA:return e; case MAMode.Both:return s&&e; default:return s; }
        }
        private bool IsSlopeFalling(int sid, int dir, int lb)
        {
            SMA sh=dir==1?smaHighL[sid]:smaHighS[sid]; SMA sl=dir==1?smaLowL[sid]:smaLowS[sid];
            EMA eh=dir==1?emaHighL[sid]:emaHighS[sid]; EMA el=dir==1?emaLowL[sid]:emaLowS[sid];
            bool s = sh[0]<sh[lb] && sl[0]<sl[lb];
            bool e = eh[0]<eh[lb] && el[0]<el[lb];
            switch(SD_MaType(sid,dir)){ case MAMode.SMA:return s; case MAMode.EMA:return e; case MAMode.Both:return s&&e; default:return s; }
        }

        #endregion

        // =====================================================================
        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
            {
                CancelWorkingEntryOrder();
                if (Position.MarketPosition != MarketPosition.Flat)
                    FlattenAndCancel("InvalidConfiguration");
                return;
            }

            // Minimum bars check across all active sessions
            int minBars = 20;
            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (!S_Active(sid)) continue;
                minBars = Math.Max(minBars, SD_MaPeriod(sid,1)+2);
                minBars = Math.Max(minBars, SD_MaPeriod(sid,-1)+2);
                minBars = Math.Max(minBars, SD_EmaPeriod(sid,1)+2);
                minBars = Math.Max(minBars, SD_EmaPeriod(sid,-1)+2);
                minBars = Math.Max(minBars, SD_WmaPeriod(sid,1)+1);
                minBars = Math.Max(minBars, SD_WmaPeriod(sid,-1)+1);
                minBars = Math.Max(minBars, SD_AdxPeriod(sid,1)+2);
                minBars = Math.Max(minBars, SD_AdxPeriod(sid,-1)+2);
                minBars = Math.Max(minBars, SD_TrendConfirmBars(sid,1)+1);
                minBars = Math.Max(minBars, SD_TrendConfirmBars(sid,-1)+1);
                minBars = Math.Max(minBars, SD_MaSlopeLookback(sid,1)+1);
                minBars = Math.Max(minBars, SD_MaSlopeLookback(sid,-1)+1);
            }
            if (CurrentBar < minBars) return;

            CheckSessionReset();
            DrawSessionTimeWindows();
            UpdateInfoBox();

            if (FlattenOnBlockedWindowTransition)
                HandleTimeWindowTransitions();

            if (forceFlattenInProgress)
            {
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    if (hasActivePosition)
                        HandlePositionClosed(activeSessionId);
                    else
                        ClearForceFlattenState();
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

            // Forced close for active session
            if (activeSessionId > 0 && S_EnableForcedClose(activeSessionId) && IsInSessionForcedClose(activeSessionId))
            {
                FlattenAndCancel("ForcedClose");
                if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat)
                    HandlePositionClosed(activeSessionId);
                prevMarketPosition = Position.MarketPosition;
                return;
            }

            // Detect position closed externally (TP/SL hit)
            if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat)
                HandlePositionClosed(activeSessionId);

            if (Position.MarketPosition != MarketPosition.Flat && !hasActivePosition && tradeEntryPrice > 0)
                hasActivePosition = true;

            // Trade management while in position
            if (activeSessionId > 0 && hasActivePosition && Position.MarketPosition != MarketPosition.Flat)
            {
                barsSinceEntry++;
                int d = tradeDirection; int sid = activeSessionId;
                int maxBars = SD_MaxBarsInTrade(sid, d);
                if (maxBars > 0 && barsSinceEntry >= maxBars)
                {
                    FlattenAndCancel("MaxBarsInTrade");
                    if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat)
                        HandlePositionClosed(sid);
                }
                if (hasActivePosition && Position.MarketPosition != MarketPosition.Flat)
                {
                    CheckEngulfingExit(sid); // timed engulfing: activated after EngulfingExitAfterBars bars
                    if (SD_EnableOpposingBarExit(sid,d)) CheckOpposingBarExit(sid);
                }
                if (hasActivePosition && Position.MarketPosition != MarketPosition.Flat)
                {
                    ManageBreakeven(sid);
                    ManageEntryBarSl(sid);
                    ManageTrailingStop(sid);
                    ManageCandleLagTrail(sid);
                    ManagePriceOffsetTrail(sid);
                }
                if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat)
                    HandlePositionClosed(sid);
            }

            // Entry check when flat
            if (Position.MarketPosition == MarketPosition.Flat && !hasActivePosition)
            {
                int entrySid = DetermineEntrySession();
                if (entrySid > 0) CheckForEntrySignal(entrySid);
            }

            prevMarketPosition = Position.MarketPosition;
        }

        #endregion

        // =====================================================================
        #region Session Detection

        private double ToSessionMinutes(TimeSpan t)
        {
            double m = t.TotalMinutes - SessionStartTime.TimeOfDay.TotalMinutes;
            if (m < 0) m += 1440;
            return m;
        }
        private int DetermineEntrySession()
        {
            if (IsInNewsSkipWindow(Time[0])) return 0;

            for (int sid = 1; sid <= SubSessionCount; sid++)
                if (S_Active(sid) && !subLimitsReached[sid] && IsInSessionTradeWindow(sid)) return sid;
            return 0;
        }
        private bool IsInSessionTradeWindow(int sid)
        {
            double barSM   = ToSessionMinutes(Time[0].TimeOfDay);
            double startSM = ToSessionMinutes(S_TradeWindowStart(sid).TimeOfDay);
            if (barSM < startSM) return false;
            double endSM = S_EnableNoNewTradesAfter(sid)
                ? ToSessionMinutes(S_NoNewTradesAfter(sid).TimeOfDay)
                : ToSessionMinutes(S_ForcedCloseTime(sid).TimeOfDay);
            return barSM < endSM;
        }
        private bool IsInSessionForcedClose(int sid)
        {
            if (!S_EnableForcedClose(sid)) return false;
            double barSM   = ToSessionMinutes(Time[0].TimeOfDay);
            double closeSM = ToSessionMinutes(S_ForcedCloseTime(sid).TimeOfDay);
            return barSM >= closeSM;
        }

        private bool IsInNewsSkipWindow(DateTime time)
        {
            if (!UseNewsSkip) return false;
            EnsureNewsDatesInitialized();

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

        #endregion

        // =====================================================================
        #region Entry Signal

        private void CheckForEntrySignal(int sid)
        {
            double cC=Close[0],cO=Open[0],cH=High[0],cL=Low[0];
            double pC=Close[1],pO=Open[1],pH=High[1],pL=Low[1];
            // Long MAs
            double uCL=GetUpperMA(sid,1,0), lCL=GetLowerMA(sid,1,0);
            double uPL=GetUpperMA(sid,1,1), lPL=GetLowerMA(sid,1,1);
            // Short MAs
            double uCS=GetUpperMA(sid,-1,0), lCS=GetLowerMA(sid,-1,0);
            double uPS=GetUpperMA(sid,-1,1), lPS=GetLowerMA(sid,-1,1);
            bool bull=cC>cO, bear=cC<cO;

            // LONG signal: bull candle, close above both MAs current+prior bars, open above lower MA, confirms vs prior bar
            if (S_EnableLongTrades(sid) && bull
                && cC>uCL && cC>lCL && cC>uPL && cC>lPL
                && cO>lCL && cC>pO && cC>pC
                && IsDirectionAllowed(sid,1))
            {
                if (PassesFilters(sid,1,cC,cH,cL))
                    SubmitEntry(sid,1,cC,cH,cL,pH,pL);
            }
            // SHORT signal: bear candle, close below both MAs current+prior bars, open below upper MA, confirms vs prior bar
            else if (S_EnableShortTrades(sid) && bear
                && cC<uCS && cC<lCS && cC<uPS && cC<lPS
                && cO<uCS && cC<pO && cC<pC
                && IsDirectionAllowed(sid,-1))
            {
                if (PassesFilters(sid,-1,cC,cH,cL))
                    SubmitEntry(sid,-1,cC,cH,cL,pH,pL);
            }
        }

        private bool IsDirectionAllowed(int sid, int dir)
        {
            int lastDir = subLastTradeDirection[sid];
            if (lastDir == 0) return true;
            if (!SD_RequireDirectionFlip(sid,dir)) return true;
            if (lastDir == dir)
            {
                if (SD_AllowSameDirectionAfterLoss(sid,dir) && subLastTradeWasLoss[sid]) return true;
                return false;
            }
            return true;
        }

        private bool PassesFilters(int sid, int dir, double close, double high, double low)
        {
            double range = high - low;
            double body  = Math.Abs(close - Open[0]);
            string lbl   = S_Label(sid) + (dir==1?" LONG":" SHORT");

            int maxCandle = SD_MaxSignalCandleTicks(sid,dir);
            if (maxCandle > 0 && range/TickSize > maxCandle)
            { Print(string.Format("{0} | {1} REJECT: CandleSize {2:F0}t>{3}", Time[0],lbl,range/TickSize,maxCandle)); return false; }

            double minBody = SD_MinBodyPct(sid,dir);
            if (minBody > 0 && range > 0 && (body/range)*100.0 < minBody)
            { Print(string.Format("{0} | {1} REJECT: Body% {2:F1}<{3:F1}", Time[0],lbl,(body/range)*100.0,minBody)); return false; }

            int maxDist = SD_MaxDistanceFromMaTicks(sid,dir);
            if (maxDist > 0)
            {
                double dist = dir==1
                    ? (close - GetUpperMA(sid,dir,0))/TickSize
                    : (GetLowerMA(sid,dir,0) - close)/TickSize;
                if (dist > maxDist)
                { Print(string.Format("{0} | {1} REJECT: MADist {2:F1}t>{3}", Time[0],lbl,dist,maxDist)); return false; }
            }

            if (SD_RequireMaSlope(sid,dir))
            {
                int lb = SD_MaSlopeLookback(sid,dir);
                if (dir== 1 && !IsSlopeRising(sid,dir,lb))  { Print(string.Format("{0} | {1} REJECT: MA not rising", Time[0],lbl));  return false; }
                if (dir==-1 && !IsSlopeFalling(sid,dir,lb)) { Print(string.Format("{0} | {1} REJECT: MA not falling", Time[0],lbl)); return false; }
            }

            if (SD_EnableWmaFilter(sid,dir))
            {
                WMA w = dir==1 ? wmaLong[sid] : wmaShort[sid];
                if (dir== 1 && close <= w[0]) { Print(string.Format("{0} | {1} REJECT: price<=WMA", Time[0],lbl)); return false; }
                if (dir==-1 && close >= w[0]) { Print(string.Format("{0} | {1} REJECT: price>=WMA", Time[0],lbl)); return false; }
            }

            int tcb = SD_TrendConfirmBars(sid,dir);
            if (tcb > 0)
            {
                if (dir== 1 && close <= Open[tcb]) { Print(string.Format("{0} | {1} REJECT: no uptrend {2}b", Time[0],lbl,tcb));   return false; }
                if (dir==-1 && close >= Open[tcb]) { Print(string.Format("{0} | {1} REJECT: no downtrend {2}b", Time[0],lbl,tcb)); return false; }
            }

            if (SD_EnableAdxFilter(sid,dir))
            {
                ADX a = dir==1 ? adxLong[sid] : adxShort[sid];
                if (a[0] < SD_AdxMinLevel(sid,dir))
                { Print(string.Format("{0} | {1} REJECT: ADX {2:F1}<{3}", Time[0],lbl,a[0],SD_AdxMinLevel(sid,dir))); return false; }
            }

            return true;
        }

        private void SubmitEntry(int sid, int dir, double close, double cH, double cL, double pH, double pL)
        {
            double range  = cH - cL;
            double slP    = dir==1 ? pL - SD_SlExtraTicks(sid,dir)*TickSize : pH + SD_SlExtraTicks(sid,dir)*TickSize;
            double slDist = Math.Abs(close - slP)/TickSize;

            int maxSl = SD_MaxSlTicks(sid,dir);
            if (maxSl > 0 && slDist > maxSl)
            { Print(string.Format("{0} | [{1}] {2} REJECT: SL {3:F0}t>{4}", Time[0],S_Label(sid),dir==1?"LONG":"SHORT",slDist,maxSl)); return; }

            double maxRatio = SD_MaxSlToTpRatio(sid,dir);
            if (maxRatio > 0)
            {
                double tpDist = CalculateTPDistanceTicks(sid,dir,range);
                if (tpDist > 0 && slDist/tpDist > maxRatio)
                { Print(string.Format("{0} | [{1}] {2} REJECT: SL:TP {3:F2}>{4:F2}", Time[0],S_Label(sid),dir==1?"LONG":"SHORT",slDist/tpDist,maxRatio)); return; }
            }

            int qty = Math.Max(1, S_Contracts(sid));
            string name = GetEntrySignalName(dir);
            MichalEntryMode mode = SD_EntryMode(sid,dir);
            double plannedEntryPrice = close;
            if (mode == MichalEntryMode.LimitOffset)
                plannedEntryPrice = dir==1 ? close - SD_LimitOffsetTicks(sid,dir)*TickSize : close + SD_LimitOffsetTicks(sid,dir)*TickSize;
            else if (mode == MichalEntryMode.LimitRetracement)
                plannedEntryPrice = dir==1 ? close - range * (SD_LimitRetracementPct(sid,dir)/100.0) : close + range * (SD_LimitRetracementPct(sid,dir)/100.0);

            if (RequireEntryConfirmation && !ShowEntryConfirmation(dir==1 ? "Long" : "Short", plannedEntryPrice, qty))
                return;

            activeSessionId   = sid;
            tradeDirection    = dir;
            signalCandleRange = range;
            priorCandleHigh   = pH;
            priorCandleLow    = pL;

            if (mode == MichalEntryMode.LimitOffset)
            {
                double lp = dir==1 ? close - SD_LimitOffsetTicks(sid,dir)*TickSize : close + SD_LimitOffsetTicks(sid,dir)*TickSize;
                entryOrder = dir==1 ? EnterLongLimit(0,true,qty,lp,name) : EnterShortLimit(0,true,qty,lp,name);
            }
            else if (mode == MichalEntryMode.LimitRetracement)
            {
                double ret = range * (SD_LimitRetracementPct(sid,dir)/100.0);
                double lp = dir==1 ? close - ret : close + ret;
                entryOrder = dir==1 ? EnterLongLimit(0,true,qty,lp,name) : EnterShortLimit(0,true,qty,lp,name);
            }
            else
            {
                entryOrder = dir==1 ? EnterLong(qty,name) : EnterShort(qty,name);
            }
            Print(string.Format("{0} | [{1}] SIGNAL {2} @ {3:F2}", Time[0],S_Label(sid),dir==1?"LONG":"SHORT",close));
        }

        #endregion

        // =====================================================================
        #region OnExecutionUpdate / OnOrderUpdate

        protected override void OnExecutionUpdate(Execution exec, string execId, double price,
            int qty, MarketPosition mp, string orderId, DateTime time)
        {
            if (exec.Order == null) return;
            string n = exec.Order.Name ?? string.Empty;
            if ((n==LongEntrySignal||n==ShortEntrySignal) && exec.Order.OrderState==OrderState.Filled && !hasActivePosition)
            {
                tradeEntryPrice=exec.Price; hasActivePosition=true;
                barsSinceEntry=0; breakEvenApplied=false; entryBarSlApplied=false;
                priceOffsetTrailActive=false; priceOffsetTrailDistance=0;
                opposingBarBenchmark=0; opposingBarBenchmarkSet=false;

                int sid=activeSessionId; int d=tradeDirection;
                string eName = GetEntrySignalName(d);
                double tp = CalculateTakeProfit(sid,d,tradeEntryPrice);
                double sl = CalculateStopLoss(sid,d);

                if (d==1)  { if(tp<=tradeEntryPrice) tp=tradeEntryPrice+SD_TakeProfitTicks(sid,d)*TickSize; if(sl>=tradeEntryPrice) sl=tradeEntryPrice-4*TickSize; }
                else       { if(tp>=tradeEntryPrice) tp=tradeEntryPrice-SD_TakeProfitTicks(sid,d)*TickSize; if(sl<=tradeEntryPrice) sl=tradeEntryPrice+4*TickSize; }
                originalStopPrice=sl;

                if (d==1) { targetOrder = ExitLongLimit(0,true,Position.Quantity,tp,BuildExitSignalName("TP"),eName); stopOrder = ExitLongStopMarket(0,true,Position.Quantity,sl,BuildExitSignalName("SL"),eName); }
                else      { targetOrder = ExitShortLimit(0,true,Position.Quantity,tp,BuildExitSignalName("TP"),eName); stopOrder = ExitShortStopMarket(0,true,Position.Quantity,sl,BuildExitSignalName("SL"),eName); }
                Print(string.Format("{0} | [{1}] FILLED {2} @ {3:F2} | TP={4:F2} SL={5:F2}",
                    time,S_Label(sid),d==1?"LONG":"SHORT",tradeEntryPrice,tp,sl));

                string entryWebhookAction;
                bool isMarketEntry;
                if (TryGetEntryWebhookAction(exec, out entryWebhookAction, out isMarketEntry))
                {
                    bool entryWebhookSent = SendWebhook(entryWebhookAction, tradeEntryPrice, tp, sl, isMarketEntry, qty);
                    if (WebhookProviderType == WebhookProvider.ProjectX && entryWebhookSent)
                    {
                        projectXLastSyncedStopPrice = RoundToInstrumentTick(sl);
                        projectXLastSyncedTargetPrice = RoundToInstrumentTick(tp);
                    }
                }
            }

            if (mp == MarketPosition.Flat)
            {
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
            }

            if (ShouldSendExitWebhook(exec, n, mp))
                SendWebhook("exit", 0, 0, 0, true, qty);
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int qty, int filled, double avgFill, OrderState state, DateTime time, ErrorCode err, string nativeErr)
        {
            string orderName = order != null ? (order.Name ?? string.Empty) : string.Empty;
            if (orderName == BuildExitSignalName("TP"))
                targetOrder = order;
            else if (orderName == BuildExitSignalName("SL"))
                stopOrder = order;

            TrySyncProjectXProtectiveOrder(order, limitPrice, stopPrice, state);

            if (entryOrder!=null && order==entryOrder && (state==OrderState.Cancelled||state==OrderState.Rejected))
            {
                SendWebhook("cancel");
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
                entryOrder=null; hasActivePosition=false; activeSessionId=0;
            }

            if (forceFlattenInProgress
                && Position.MarketPosition != MarketPosition.Flat
                && (state==OrderState.Cancelled||state==OrderState.Rejected))
                TrySubmitForceFlattenExit();
        }

        #endregion

        // =====================================================================
        #region TP / SL Calculation

        private double CalculateTakeProfit(int sid, int dir, double fill)
        {
            double dist;
            switch (SD_TakeProfitMode(sid,dir))
            {
                case MichalTPMode.FixedTicks:     dist = SD_TakeProfitTicks(sid,dir)*TickSize; break;
                case MichalTPMode.SwingPoint:     return GetSwingTarget(sid,fill,dir);
                case MichalTPMode.CandleMultiple: dist = signalCandleRange * SD_CandleMultiplier(sid,dir); break;
                default:                          dist = SD_TakeProfitTicks(sid,dir)*TickSize; break;
            }
            int maxTp = SD_MaxTakeProfitTicks(sid,dir);
            if (maxTp>0 && dist > maxTp*TickSize) dist = maxTp*TickSize;
            return dir==1 ? fill+dist : fill-dist;
        }

        private double CalculateTPDistanceTicks(int sid, int dir, double range)
        {
            double t;
            switch (SD_TakeProfitMode(sid,dir))
            {
                case MichalTPMode.CandleMultiple: t = (range * SD_CandleMultiplier(sid,dir))/TickSize; break;
                default:                          t = SD_TakeProfitTicks(sid,dir); break;
            }
            int maxTp = SD_MaxTakeProfitTicks(sid,dir);
            if (maxTp>0 && t>maxTp) t=maxTp;
            return t;
        }

        private double GetSwingTarget(int sid, double fill, int dir)
        {
            int ss = SD_SwingStrength(sid,dir);
            if (dir==1) { for (int i=0;i<CurrentBar-ss;i++) { double v=swingInd[sid].SwingHigh[i]; if(v>0&&v>fill) return v; } }
            else        { for (int i=0;i<CurrentBar-ss;i++) { double v=swingInd[sid].SwingLow[i];  if(v>0&&v<fill) return v; } }
            return dir==1 ? fill+SD_TakeProfitTicks(sid,dir)*TickSize : fill-SD_TakeProfitTicks(sid,dir)*TickSize;
        }
        private double CalculateStopLoss(int sid, int dir)
        {
            int extra = SD_SlExtraTicks(sid,dir);
            if (SD_SlAtMa(sid,dir))
                return dir==1 ? GetLowerMA(sid,dir,0)-extra*TickSize : GetUpperMA(sid,dir,0)+extra*TickSize;
            if (SD_UsePriorCandleSl(sid,dir) && CurrentBar>=2)
            {
                if (dir==1) return Math.Min(priorCandleLow,  Low[2]) -extra*TickSize;
                else        return Math.Max(priorCandleHigh, High[2])+extra*TickSize;
            }
            return dir==1 ? priorCandleLow-extra*TickSize : priorCandleHigh+extra*TickSize;
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

        #endregion

        // =====================================================================
        #region Trade Management

        private void ManageBreakeven(int sid)
        {
            int d=tradeDirection;
            if (!SD_EnableBreakeven(sid,d)||breakEvenApplied||tradeEntryPrice==0) return;
            double trig = SD_BreakevenMode(sid,d)==BEMode2.FixedTicks
                ? SD_BreakevenTriggerTicks(sid,d)*TickSize
                : signalCandleRange*(SD_BreakevenCandlePct(sid,d)/100.0);
            bool hit=(d==1&&Position.MarketPosition==MarketPosition.Long&&High[0]>=tradeEntryPrice+trig)
                   ||(d==-1&&Position.MarketPosition==MarketPosition.Short&&Low[0]<=tradeEntryPrice-trig);
            if (!hit) return;
            double be = d==1
                ? tradeEntryPrice+SD_BreakevenOffsetTicks(sid,d)*TickSize
                : tradeEntryPrice-SD_BreakevenOffsetTicks(sid,d)*TickSize;
            be=Instrument.MasterInstrument.RoundToTickSize(be);
            string eName=GetEntrySignalName(d);
            if (d==1 &&be>originalStopPrice&&CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long,be)) { ExitLongStopMarket(0,true,Position.Quantity,be,BuildExitSignalName("SL"),eName);  originalStopPrice=be; breakEvenApplied=true; }
            if (d==-1&&be<originalStopPrice&&CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short,be)) { ExitShortStopMarket(0,true,Position.Quantity,be,BuildExitSignalName("SL"),eName); originalStopPrice=be; breakEvenApplied=true; }
        }

        private void ManageEntryBarSl(int sid)
        {
            int d=tradeDirection;
            if (!SD_MoveSlToEntryBar(sid,d)||entryBarSlApplied||barsSinceEntry!=1) return;
            entryBarSlApplied=true;
            string eName=GetEntrySignalName(d);
            if (d==1&&Position.MarketPosition==MarketPosition.Long&&Close[1]>Open[1])
            {
                double ns=Instrument.MasterInstrument.RoundToTickSize(Low[1]-SD_SlExtraTicks(sid,d)*TickSize);
                if (ns>originalStopPrice&&CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long,ns)) { ExitLongStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
            }
            if (d==-1&&Position.MarketPosition==MarketPosition.Short&&Close[1]<Open[1])
            {
                double ns=Instrument.MasterInstrument.RoundToTickSize(High[1]+SD_SlExtraTicks(sid,d)*TickSize);
                if (ns<originalStopPrice&&CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short,ns)) { ExitShortStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
            }
        }

        private void ManageTrailingStop(int sid)
        {
            int d=tradeDirection;
            if (barsSinceEntry < SD_TrailDelayBars(sid,d)) return;
            string eName=GetEntrySignalName(d);
            if (d==1&&Position.MarketPosition==MarketPosition.Long)
            {
                double ns=Instrument.MasterInstrument.RoundToTickSize(GetLowerMAForTrail(sid,d)-SD_TrailOffsetTicks(sid,d)*TickSize);
                if (ns>originalStopPrice&&CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long,ns)) { ExitLongStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
            }
            else if (d==-1&&Position.MarketPosition==MarketPosition.Short)
            {
                double ns=Instrument.MasterInstrument.RoundToTickSize(GetUpperMAForTrail(sid,d)+SD_TrailOffsetTicks(sid,d)*TickSize);
                if (ns<originalStopPrice&&CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short,ns)) { ExitShortStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
            }
        }

        private void ManageCandleLagTrail(int sid)
        {
            int d=tradeDirection; int offset=SD_TrailCandleOffset(sid,d);
            if (offset<=0||barsSinceEntry<offset+SD_TrailDelayBars(sid,d)) return;
            string eName=GetEntrySignalName(d);
            if (d==1&&Position.MarketPosition==MarketPosition.Long)
            {
                double ns=Instrument.MasterInstrument.RoundToTickSize(Low[offset]-SD_SlExtraTicks(sid,d)*TickSize);
                if (ns>originalStopPrice&&CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long,ns)) { ExitLongStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
            }
            else if (d==-1&&Position.MarketPosition==MarketPosition.Short)
            {
                double ns=Instrument.MasterInstrument.RoundToTickSize(High[offset]+SD_SlExtraTicks(sid,d)*TickSize);
                if (ns<originalStopPrice&&CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short,ns)) { ExitShortStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
            }
        }

        private void ManagePriceOffsetTrail(int sid)
        {
            int d=tradeDirection;
            if (!SD_EnablePriceOffsetTrail(sid,d)||tradeEntryPrice==0) return;
            string eName=GetEntrySignalName(d);
            if (d==1&&Position.MarketPosition==MarketPosition.Long)
            {
                if (!priceOffsetTrailActive&&GetLowerMAForTrail(sid,d)>originalStopPrice)
                {
                    priceOffsetTrailDistance=Close[0]-originalStopPrice-SD_PriceOffsetReductionTicks(sid,d)*TickSize;
                    if (priceOffsetTrailDistance<TickSize) priceOffsetTrailDistance=TickSize;
                    priceOffsetTrailActive=true;
                }
                if (priceOffsetTrailActive)
                {
                    double ns=Instrument.MasterInstrument.RoundToTickSize(Close[0]-priceOffsetTrailDistance);
                    if (ns>originalStopPrice&&CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long,ns)) { ExitLongStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
                }
            }
            else if (d==-1&&Position.MarketPosition==MarketPosition.Short)
            {
                if (!priceOffsetTrailActive&&GetUpperMAForTrail(sid,d)<originalStopPrice)
                {
                    priceOffsetTrailDistance=originalStopPrice-Close[0]-SD_PriceOffsetReductionTicks(sid,d)*TickSize;
                    if (priceOffsetTrailDistance<TickSize) priceOffsetTrailDistance=TickSize;
                    priceOffsetTrailActive=true;
                }
                if (priceOffsetTrailActive)
                {
                    double ns=Instrument.MasterInstrument.RoundToTickSize(Close[0]+priceOffsetTrailDistance);
                    if (ns<originalStopPrice&&CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short,ns)) { ExitShortStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
                }
            }
        }

        #endregion

        // =====================================================================
        #region Engulfing / Opposing Bar Exits

        private void CheckEngulfingExit(int sid)
        {
            int afterBars = S_EngulfingExitAfterBars(sid);
            if (afterBars <= 0 || barsSinceEntry < afterBars) return;

            double padding = S_EngulfingExitPaddingTicks(sid) * TickSize;
            int d = tradeDirection;

            // SHORT trade: close if current bullish candle closes above High[1] + padding
            if (d == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                bool bullish = Close[0] > Open[0];
                if (bullish && Close[0] > High[1] + padding)
                {
                    FlattenAndCancel("EngulfingExit");
                    Print(string.Format("{0} | [{1}] ENGULFING EXIT SHORT: Close={2:F2} > High[1]={3:F2} + pad={4:F2}",
                        Time[0], S_Label(sid), Close[0], High[1], padding));
                }
            }
            // LONG trade: close if current bearish candle closes below Low[1] - padding
            else if (d == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                bool bearish = Close[0] < Open[0];
                if (bearish && Close[0] < Low[1] - padding)
                {
                    FlattenAndCancel("EngulfingExit");
                    Print(string.Format("{0} | [{1}] ENGULFING EXIT LONG: Close={2:F2} < Low[1]={3:F2} - pad={4:F2}",
                        Time[0], S_Label(sid), Close[0], Low[1], padding));
                }
            }
        }
        private void CheckOpposingBarExit(int sid)
        {
            bool bull=Close[0]>Open[0], bear=Close[0]<Open[0];
            int d=tradeDirection;
            if (d==-1&&Position.MarketPosition==MarketPosition.Short&&bull)
            {
                if (!opposingBarBenchmarkSet) { opposingBarBenchmark=Close[0]; opposingBarBenchmarkSet=true; }
                else if (Close[0]>opposingBarBenchmark) ExitShort(Position.Quantity,BuildExitSignalName("OppBarExit"),ShortEntrySignal);
            }
            if (d== 1&&Position.MarketPosition==MarketPosition.Long&&bear)
            {
                if (!opposingBarBenchmarkSet) { opposingBarBenchmark=Close[0]; opposingBarBenchmarkSet=true; }
                else if (Close[0]<opposingBarBenchmark) ExitLong(Position.Quantity,BuildExitSignalName("OppBarExit"),LongEntrySignal);
            }
        }

        #endregion

        // =====================================================================
        #region Session Management

        private void CheckSessionReset()
        {
            TimeSpan barTOD   = Time[0].TimeOfDay;
            DateTime sessDate = barTOD >= SessionStartTime.TimeOfDay ? Time[0].Date : Time[0].Date.AddDays(-1);
            if (sessDate != currentSessionDate)
            {
                if (Position.MarketPosition != MarketPosition.Flat) FlattenAndCancel("SessionRollover");
                currentSessionDate = sessDate;
                ResetAll();
                Print(string.Format("{0} | ═══ NEW SESSION ═══ {1:yyyy-MM-dd}", Time[0], sessDate));
            }
        }

        private void ResetAll()
        {
            for (int i=1;i<=SubSessionCount;i++)
            {
                subTradeCount[i]=0; subWinCount[i]=0; subLossCount[i]=0;
                subPnLTicks[i]=0.0; subLimitsReached[i]=false;
                subLastTradeDirection[i]=0; subLastTradeWasLoss[i]=false;
                subWasInNoTradesAfterWindow[i]=false;
            }
            activeSessionId=0; tradeDirection=0; signalCandleRange=0;
            priorCandleHigh=0; priorCandleLow=0; originalStopPrice=0;
            tradeEntryPrice=0; hasActivePosition=false; barsSinceEntry=0;
            breakEvenApplied=false; entryBarSlApplied=false;
            priceOffsetTrailActive=false; priceOffsetTrailDistance=0;
            opposingBarBenchmark=0; opposingBarBenchmarkSet=false;
            wasInNewsSkipWindow=false;
            entryOrder=null; stopOrder=null; targetOrder=null;
            projectXLastSyncedStopPrice = 0.0;
            projectXLastSyncedTargetPrice = 0.0;
            ClearForceFlattenState();
            prevMarketPosition=MarketPosition.Flat;
        }

        private void HandlePositionClosed(int sid)
        {
            if (sid < 1 || sid > SubSessionCount) { hasActivePosition=false; activeSessionId=0; return; }
            double pnl=0;
            if (SystemPerformance!=null && SystemPerformance.AllTrades.Count>0)
                pnl=SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count-1].ProfitTicks;
            else
                pnl=tradeDirection==1?(Close[0]-tradeEntryPrice)/TickSize:(tradeEntryPrice-Close[0])/TickSize;

            subPnLTicks[sid]+=pnl; subTradeCount[sid]++;
            subLastTradeDirection[sid]=tradeDirection;
            if (pnl>0){subWinCount[sid]++;subLastTradeWasLoss[sid]=false;}
            else      {subLossCount[sid]++;subLastTradeWasLoss[sid]=true;}

            Print(string.Format("{0} | [{1}] CLOSED {2} PnL={3:F1}t W={4} L={5}",
                Time[0],S_Label(sid),pnl>0?"PROFIT":"LOSS",pnl,subWinCount[sid],subLossCount[sid]));
            CheckSessionLimitsInternal(sid);

            hasActivePosition=false; tradeDirection=0; signalCandleRange=0;
            priorCandleHigh=0; priorCandleLow=0; originalStopPrice=0;
            tradeEntryPrice=0; barsSinceEntry=0; breakEvenApplied=false;
            entryBarSlApplied=false; priceOffsetTrailActive=false; priceOffsetTrailDistance=0;
            opposingBarBenchmark=0; opposingBarBenchmarkSet=false;
            entryOrder=null; stopOrder=null; targetOrder=null;
            projectXLastSyncedStopPrice = 0.0;
            projectXLastSyncedTargetPrice = 0.0;
            ClearForceFlattenState();
            activeSessionId=0;
        }

        private void CheckSessionLimitsInternal(int sid)
        {
            if (S_GetLimitsReached(sid)) return;
            string reason="";
            if (S_GetTradeCount(sid) >= S_MaxTradesPerSession(sid))                           reason=string.Format("MaxTrades({0})",S_MaxTradesPerSession(sid));
            else if (S_MaxLossesPerSession(sid)>0 && S_GetLossCount(sid)>=S_MaxLossesPerSession(sid)) reason=string.Format("MaxLosses({0})",S_MaxLossesPerSession(sid));
            else if (S_GetPnLTicks(sid) >= S_MaxSessionProfitTicks(sid))                      reason=string.Format("MaxProfit({0:F0}t)",S_GetPnLTicks(sid));
            else if (S_GetPnLTicks(sid) <= -S_MaxSessionLossTicks(sid))                       reason=string.Format("MaxLoss({0:F0}t)",S_GetPnLTicks(sid));
            if (!string.IsNullOrEmpty(reason))
            { S_SetLimitsReached(sid,true); Print(string.Format("{0} | [{1}] *** LIMIT: {2} ***",Time[0],S_Label(sid),reason)); }
        }
        private void FlattenAndCancel(string reason)
        {
            CancelWorkingEntryOrder();
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                ClearForceFlattenState();
                return;
            }

            if (!forceFlattenInProgress)
            {
                forceFlattenInProgress = true;
                forceFlattenOrderSubmitted = false;
                forceFlattenReason = reason;
            }

            CancelWorkingProtectionOrders();
            TrySubmitForceFlattenExit();
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

        private void ClearForceFlattenState()
        {
            forceFlattenInProgress = false;
            forceFlattenOrderSubmitted = false;
            forceFlattenReason = null;
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

        private void HandleTimeWindowTransitions()
        {
            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (!S_Active(sid)) continue;

                bool inNoTrades = S_EnableNoNewTradesAfter(sid)
                    && ToSessionMinutes(Time[0].TimeOfDay) >= ToSessionMinutes(S_NoNewTradesAfter(sid).TimeOfDay);
                bool wasNoTrades = subWasInNoTradesAfterWindow[sid];

                if (FlattenOnBlockedWindowTransition && activeSessionId == sid && !wasNoTrades && inNoTrades)
                    FlattenAndCancel("NoTradesAfter");

                subWasInNoTradesAfterWindow[sid] = inNoTrades;
            }

            bool inNewsSkip = IsInNewsSkipWindow(Time[0]);
            if (FlattenOnBlockedWindowTransition && !wasInNewsSkipWindow && inNewsSkip)
                FlattenAndCancel("NewsSkip");
            wasInNewsSkipWindow = inNewsSkip;
        }

        private void EnsureNewsDatesInitialized()
        {
            if (newsDatesInitialized) return;

            NewsDates.Clear();
            if (!string.IsNullOrWhiteSpace(NewsDatesRaw))
            {
                string[] entries = NewsDatesRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < entries.Length; i++)
                {
                    DateTime parsed;
                    if (!DateTime.TryParse(
                        entries[i],
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeLocal,
                        out parsed))
                        continue;

                    if (!NewsDates.Contains(parsed))
                        NewsDates.Add(parsed);
                }
            }

            NewsDates.Sort();
            newsDatesInitialized = true;
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
            if (!UseWebhooks || State != State.Realtime || WebhookProviderType != WebhookProvider.ProjectX)
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

        private bool SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (!UseWebhooks || State != State.Realtime)
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
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture);
                string action = (eventType ?? string.Empty).ToLowerInvariant();
                string json = string.Empty;

                if (action == "buy" || action == "sell")
                {
                    json = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"{1}\",\"orderType\":\"{2}\",\"quantityType\":\"fixed_quantity\",\"quantity\":{3},\"signalPrice\":{4},\"time\":\"{5}\",\"takeProfit\":{{\"limitPrice\":{6}}},\"stopLoss\":{{\"type\":\"stop\",\"stopPrice\":{7}}}}}",
                        JsonEscape(ticker),
                        action,
                        isMarketEntry ? "market" : "limit",
                        orderQty,
                        entryPrice,
                        JsonEscape(now),
                        takeProfit,
                        stopLoss);
                }
                else if (action == "exit")
                {
                    json = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"exit\",\"orderType\":\"market\",\"quantityType\":\"fixed_quantity\",\"quantity\":{1},\"cancel\":true,\"time\":\"{2}\"}}",
                        JsonEscape(ticker),
                        orderQty,
                        JsonEscape(now));
                }
                else if (action == "cancel")
                {
                    json = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"cancel\",\"time\":\"{1}\"}}",
                        JsonEscape(ticker),
                        JsonEscape(now));
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
                LogDebug(string.Format("Webhook error | event={0} error={1}", eventType, ex.Message));
                return false;
            }
        }

        private int GetDefaultWebhookQuantity()
        {
            if (Position != null && Position.MarketPosition != MarketPosition.Flat && Math.Abs(Position.Quantity) > 0)
                return Math.Abs(Position.Quantity);

            if (activeSessionId > 0)
                return Math.Max(1, S_Contracts(activeSessionId));

            return Math.Max(1, GlobalContracts);
        }

        private string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
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

        private void LogProjectX(string message)
        {
            LogProjectXStatus(message);
        }

        private void RunProjectXStartupPreflight()
        {
            if (!UseWebhooks || WebhookProviderType != WebhookProvider.ProjectX)
                return;

            LogProjectXDiscovery(string.Format(
                "ProjectX startup preflight begin | instrument={0} accounts={1} baseUrl={2}",
                GetProjectXInstrumentKey(),
                ProjectXTradeAllAccounts ? "<all>" : (ProjectXAccountId ?? string.Empty),
                ProjectXApiBaseUrl ?? string.Empty));

            if (!EnsureProjectXSession())
            {
                LogProjectXDiscovery("ProjectX startup preflight failed | stage=auth");
                return;
            }

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetProjectXTargets(out targetAccounts, out contractId))
            {
                LogProjectXDiscovery("ProjectX startup preflight failed | stage=targets");
                return;
            }

            LogProjectXStatus(string.Format(
                "ProjectX webhook targets | count={0} contractId={1}",
                targetAccounts.Count,
                contractId ?? string.Empty));
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
                contractId));
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
                FormatProjectXAccountsForLog(targetAccounts),
                contractId));

            bool sentAny = false;
            string action = (eventType ?? string.Empty).ToLowerInvariant();
            foreach (var account in targetAccounts)
            {
                try
                {
                    if (action == "buy" || action == "sell")
                    {
                        if (ProjectXPrepareForEntry(account.Id, contractId))
                        {
                            string response = ProjectXPlaceOrder(action, account.Id, contractId, entryPrice, takeProfit, stopLoss, isMarketEntry, quantity);
                            sentAny = sentAny || !string.IsNullOrWhiteSpace(response);
                        }
                    }
                    else if (action == "exit")
                    {
                        ProjectXFlattenPosition(account.Id, contractId);
                        sentAny = true;
                    }
                    else if (action == "cancel")
                    {
                        ProjectXCancelOrders(account.Id, contractId);
                        sentAny = true;
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

            return sentAny;
        }

        private bool EnsureProjectXSession()
        {
            if (string.IsNullOrWhiteSpace(ProjectXApiBaseUrl))
            {
                LogProjectX("ProjectX login failed | reason=empty-base-url");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(projectXSessionToken) &&
                (DateTime.UtcNow - projectXTokenAcquiredUtc).TotalHours < 23)
                return true;

            if (string.IsNullOrWhiteSpace(ProjectXUsername) || string.IsNullOrWhiteSpace(ProjectXApiKey))
            {
                LogProjectX("ProjectX login failed | reason=missing-credentials");
                return false;
            }

            string loginJson = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"userName\":\"{0}\",\"apiKey\":\"{1}\"}}",
                JsonEscape(ProjectXUsername),
                JsonEscape(ProjectXApiKey));

            string response = ProjectXPost("/api/Auth/loginKey", loginJson, false, true);
            if (string.IsNullOrWhiteSpace(response))
            {
                LogProjectX("ProjectX login failed | reason=empty-response");
                return false;
            }

            string token;
            if (!TryGetJsonString(response, "token", out token))
            {
                LogProjectX("ProjectX login failed | reason=missing-token");
                return false;
            }

            projectXSessionToken = token;
            projectXTokenAcquiredUtc = DateTime.UtcNow;
            projectXAccounts = null;
            projectXLastOrderIds.Clear();
            projectXResolvedContractId = null;
            projectXResolvedInstrumentKey = string.Empty;
            LogProjectX("ProjectX login succeeded");
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

            if (ProjectXTradeAllAccounts)
            {
                targetAccounts = accounts.Where(a => a.CanTrade).ToList();
                if (targetAccounts.Count == 0)
                    LogProjectX("ProjectX account selection failed | reason=no-tradable-accounts");
                return targetAccounts.Count > 0;
            }

            var selectors = ParseProjectXAccountSelectors(ProjectXAccountId);
            if (selectors.Count == 0)
            {
                LogProjectX("ProjectX account selection failed | reason=no-selection");
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
                    if (matchedIds.Add(match.Id))
                        matchedAccounts.Add(match);
            }

            if (unmatchedSelectors.Count > 0)
            {
                LogProjectXDiscovery(string.Format(
                    "ProjectX account selection unmatched | selectors={0}",
                    string.Join(", ", unmatchedSelectors.ToArray())));
            }

            targetAccounts = matchedAccounts;
            if (targetAccounts.Count == 0)
                LogProjectX("ProjectX account selection failed | reason=no-matching-tradable-accounts");
            return targetAccounts.Count > 0;
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
                LogProjectX("ProjectX warning | no webhooks will be sent because no ProjectX accounts were found.");
                LogProjectXDiscovery("ProjectX account load failed | reason=empty-response");
                accounts = null;
                return false;
            }

            accounts = ExtractProjectXAccounts(response).ToList();
            projectXAccounts = accounts.Count > 0 ? accounts : null;

            LogProjectX("ProjectX accounts found | count=" + accounts.Count);
            if (accounts.Count == 0)
            {
                LogProjectX("ProjectX warning | no webhooks will be sent because no ProjectX accounts were found.");
                return false;
            }

            foreach (var account in accounts)
            {
                LogProjectX(string.Format(
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

            if (!string.IsNullOrWhiteSpace(ProjectXContractId))
            {
                contractId = ProjectXContractId.Trim();
                projectXResolvedInstrumentKey = GetProjectXInstrumentKey();
                LogProjectXDiscovery(string.Format(
                    "ProjectX contract override | instrument={0} contractId={1}",
                    projectXResolvedInstrumentKey,
                    contractId));
                return true;
            }

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
                    string.IsNullOrWhiteSpace(desiredSuffix) ? "<none>" : desiredSuffix,
                    contracts != null ? contracts.Count : 0));
                return false;
            }

            contractId = selected.Id;
            projectXResolvedContractId = contractId;
            projectXResolvedInstrumentKey = instrumentKey;
            LogProjectXDiscovery(string.Format(
                "ProjectX contract resolved | instrument={0} root={1} desiredSuffix={2} contractId={3} name={4} active={5}",
                instrumentKey,
                root,
                string.IsNullOrWhiteSpace(desiredSuffix) ? "<none>" : desiredSuffix,
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
                string.IsNullOrWhiteSpace(desiredSuffix) ? "<none>" : desiredSuffix));
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
                JsonEscape(searchText));
            string response = ProjectXPost("/api/Contract/search", requestJson, true);
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
            return activeMatches.Count > 0 ? activeMatches[0] : contracts[0];
        }

        private bool DoesProjectXContractMatchRoot(ProjectXContractInfo contract, string root)
        {
            if (contract == null || string.IsNullOrWhiteSpace(root))
                return false;

            string expectedSymbolId = "F.US." + root;
            return (!string.IsNullOrWhiteSpace(contract.SymbolId) && string.Equals(contract.SymbolId, expectedSymbolId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(contract.Id) && contract.Id.IndexOf(".US." + root + ".", StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrWhiteSpace(contract.Name) && contract.Name.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        private string GetProjectXInstrumentKey()
        {
            if (Instrument == null)
                return string.Empty;

            string fullName = Instrument.FullName ?? string.Empty;
            return !string.IsNullOrWhiteSpace(fullName) ? fullName.Trim().ToUpperInvariant() : GetProjectXInstrumentRoot();
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
                .Select(a => string.Format(CultureInfo.InvariantCulture, "{0}:{1}", a.Id, a.Name ?? string.Empty))
                .ToArray();
            return items.Length > 0 ? string.Join(", ", items) : "<none>";
        }

        private string GetProjectXOrderKey(int accountId, string contractId)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}", accountId, contractId ?? string.Empty);
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

        private string ProjectXPlaceOrder(string side, int accountId, string contractId, double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            int orderSide = side.Equals("buy", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int orderType = isMarketEntry ? 2 : 1;
            int normalizedQuantity = Math.Max(1, quantity);
            double entry = Instrument != null && Instrument.MasterInstrument != null
                ? Instrument.MasterInstrument.RoundToTickSize(entryPrice)
                : entryPrice;
            bool isLong = orderSide == 0;
            int tpTicks = NormalizeProjectXBracketTicks(PriceToTicks(takeProfit - entry), 4, isLong ? 1 : -1);
            int slTicks = NormalizeProjectXBracketTicks(PriceToTicks(stopLoss - entry), 1, isLong ? -1 : 1);

            string limitPart = isMarketEntry
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, ",\"limitPrice\":{0}", entry);

            string json = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\",\"type\":{2},\"side\":{3},\"size\":{4}{5},\"takeProfitBracket\":{{\"quantity\":{6},\"type\":1,\"ticks\":{7}}},\"stopLossBracket\":{{\"quantity\":{6},\"type\":4,\"ticks\":{8}}}}}",
                accountId,
                JsonEscape(contractId),
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
                LogDebug(string.Format(
                    "ProjectX flatten warning | stage=cancel-clear accountId={0} contractId={1}",
                    accountId,
                    contractId));

            int positionSize;
            if (TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize) && positionSize != 0)
            {
                ProjectXClosePosition(accountId, contractId);
                if (!WaitForProjectXFlat(accountId, contractId, 4000))
                    LogDebug(string.Format(
                        "ProjectX flatten warning | stage=flat accountId={0} contractId={1} positionSize={2}",
                        accountId,
                        contractId,
                        positionSize));
            }

            ProjectXCancelOrders(accountId, contractId);
            if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
                LogDebug(string.Format(
                    "ProjectX flatten warning | stage=post-close-cancel accountId={0} contractId={1}",
                    accountId,
                    contractId));
        }

        private string ProjectXClosePosition(int accountId, string contractId)
        {
            string json = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\"}}",
                accountId,
                JsonEscape(contractId));
            return ProjectXPost("/api/Position/closeContract", json, true);
        }

        private void ProjectXCancelOrders(int accountId, string contractId)
        {
            foreach (long orderId in GetProjectXOpenOrderIds(accountId, contractId))
            {
                string cancelJson = string.Format(
                    CultureInfo.InvariantCulture,
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
                if (order.TryGetValue("id", out idObj) && TryConvertToLong(idObj, out id) && id > 0)
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

            Dictionary<string, object> data;
            try
            {
                data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
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

            Dictionary<string, object> data;
            try
            {
                data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
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

        private IEnumerable<Dictionary<string, object>> ExtractProjectXOrders(string json)
        {
            return ExtractProjectXDictionaryArray(json, "orders");
        }

        private IEnumerable<Dictionary<string, object>> ExtractProjectXPositions(string json)
        {
            return ExtractProjectXDictionaryArray(json, "positions");
        }

        private IEnumerable<Dictionary<string, object>> ExtractProjectXDictionaryArray(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json))
                yield break;

            Dictionary<string, object> data;
            try
            {
                data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                yield break;
            }

            object raw;
            if (data == null || !data.TryGetValue(key, out raw) || raw == null)
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
                    System.Globalization.CultureInfo.InvariantCulture,
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

        private string GetEntrySignalName(int direction) { return direction == 1 ? LongEntrySignal : ShortEntrySignal; }
        private string BuildExitSignalName(string reason) { return StrategySignalPrefix + reason; }

        private void ValidateRequiredPrimaryTimeframe(int requiredMinutes)
        {
            bool isMinuteSeries = BarsPeriod != null && BarsPeriod.BarsPeriodType == NinjaTrader.Data.BarsPeriodType.Minute;
            bool timeframeMatches = isMinuteSeries && BarsPeriod.Value == requiredMinutes;
            isConfiguredTimeframeValid = timeframeMatches;
            if (timeframeMatches) return;

            string actualTimeframe = BarsPeriod == null
                ? "Unknown"
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", BarsPeriod.Value, BarsPeriod.BarsPeriodType);
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "{0} must run on a {1}-minute chart. Current chart is {2}.",
                Name,
                requiredMinutes,
                actualTimeframe);

            Print("Timeframe validation failed | " + message);
            if (timeframePopupShown) return;

            timeframePopupShown = true;
            try
            {
                if (System.Windows.Application.Current != null)
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
                Print("Failed to show timeframe popup: " + ex.Message);
            }
        }

        private void ValidateRequiredPrimaryInstrument()
        {
            string instrumentName = Instrument != null && Instrument.MasterInstrument != null
                ? (Instrument.MasterInstrument.Name ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
            bool instrumentMatches = instrumentName == "NQ" || instrumentName == "MNQ";
            isConfiguredInstrumentValid = instrumentMatches;
            if (instrumentMatches) return;

            string actualInstrument = string.IsNullOrWhiteSpace(instrumentName) ? "Unknown" : instrumentName;
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "{0} must run on NQ or MNQ. Current instrument is {1}.",
                Name,
                actualInstrument);

            Print("Instrument validation failed | " + message);
            if (instrumentPopupShown) return;

            instrumentPopupShown = true;
            try
            {
                if (System.Windows.Application.Current != null)
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
                Print("Failed to show instrument popup: " + ex.Message);
            }
        }

        private bool ShowEntryConfirmation(string orderType, double price, int quantity)
        {
            if (State != State.Realtime) return true;
            if (System.Windows.Application.Current == null) return false;

            bool confirmed = false;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                string message = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Confirm {0} entry?\nPrice: {1:F2}\nQuantity: {2}",
                    orderType,
                    price,
                    quantity);

                confirmed = System.Windows.MessageBox.Show(
                    message,
                    "Entry Confirmation",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
            });

            return confirmed;
        }

        #region Drawing Helpers

        private void DrawSessionTimeWindows()
        {
            if (CurrentBar < 1) return;

            DateTime anchorDate = GetTradingSessionAnchorDate(Time[0]);
            RemoveRawSessionBackgroundDrawings(anchorDate);

            List<Tuple<int, DateTime, DateTime>> windows = BuildResolvedSessionDrawWindows(anchorDate);
            for (int i = 0; i < windows.Count; i++)
            {
                Tuple<int, DateTime, DateTime> window = windows[i];
                DrawSessionBackground(window.Item1, window.Item2, window.Item3);
            }
            DrawNewsWindows(Time[0]);
        }

        private DateTime GetTradingSessionAnchorDate(DateTime time)
        {
            TimeSpan sessionStart = SessionStartTime.TimeOfDay;
            return time.TimeOfDay >= sessionStart ? time.Date : time.Date.AddDays(-1);
        }

        private DateTime ResolveTradingSessionTime(DateTime anchorDate, TimeSpan timeOfDay)
        {
            DateTime resolved = anchorDate + timeOfDay;
            if (timeOfDay < SessionStartTime.TimeOfDay)
                resolved = resolved.AddDays(1);
            return resolved;
        }

        private DateTime GetSessionEntryWindowStart(DateTime anchorDate, int sid)
        {
            return ResolveTradingSessionTime(anchorDate, S_TradeWindowStart(sid).TimeOfDay);
        }

        private DateTime GetSessionEntryWindowEnd(DateTime anchorDate, int sid)
        {
            TimeSpan endTime = S_EnableNoNewTradesAfter(sid)
                ? S_NoNewTradesAfter(sid).TimeOfDay
                : S_ForcedCloseTime(sid).TimeOfDay;
            DateTime sessionStart = GetSessionEntryWindowStart(anchorDate, sid);
            DateTime sessionEnd = ResolveTradingSessionTime(anchorDate, endTime);
            if (sessionEnd <= sessionStart)
                sessionEnd = sessionEnd.AddDays(1);
            return sessionEnd;
        }

        private List<Tuple<int, DateTime, DateTime>> BuildResolvedSessionDrawWindows(DateTime anchorDate)
        {
            var rawWindows = new List<Tuple<int, DateTime, DateTime>>();
            var boundaries = new List<DateTime>();

            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (!S_Active(sid)) continue;

                DateTime sessionStart = GetSessionEntryWindowStart(anchorDate, sid);
                DateTime sessionEnd = GetSessionEntryWindowEnd(anchorDate, sid);
                if (sessionEnd <= sessionStart) continue;

                rawWindows.Add(Tuple.Create(sid, sessionStart, sessionEnd));
                boundaries.Add(sessionStart);
                boundaries.Add(sessionEnd);
            }

            boundaries.Sort();

            var uniqueBoundaries = new List<DateTime>();
            for (int i = 0; i < boundaries.Count; i++)
            {
                if (uniqueBoundaries.Count == 0 || boundaries[i] != uniqueBoundaries[uniqueBoundaries.Count - 1])
                    uniqueBoundaries.Add(boundaries[i]);
            }

            var resolvedWindows = new List<Tuple<int, DateTime, DateTime>>();
            for (int i = 0; i < uniqueBoundaries.Count - 1; i++)
            {
                DateTime segmentStart = uniqueBoundaries[i];
                DateTime segmentEnd = uniqueBoundaries[i + 1];
                if (segmentEnd <= segmentStart) continue;

                DateTime midpoint = segmentStart + TimeSpan.FromTicks((segmentEnd - segmentStart).Ticks / 2);
                int selectedSid = 0;
                for (int w = 0; w < rawWindows.Count; w++)
                {
                    Tuple<int, DateTime, DateTime> rawWindow = rawWindows[w];
                    if (midpoint >= rawWindow.Item2 && midpoint < rawWindow.Item3)
                    {
                        selectedSid = rawWindow.Item1;
                        break;
                    }
                }

                if (selectedSid == 0) continue;

                if (resolvedWindows.Count > 0)
                {
                    Tuple<int, DateTime, DateTime> previous = resolvedWindows[resolvedWindows.Count - 1];
                    if (previous.Item1 == selectedSid && previous.Item3 == segmentStart)
                    {
                        resolvedWindows[resolvedWindows.Count - 1] = Tuple.Create(selectedSid, previous.Item2, segmentEnd);
                        continue;
                    }
                }

                resolvedWindows.Add(Tuple.Create(selectedSid, segmentStart, segmentEnd));
            }

            return resolvedWindows;
        }

        private void RemoveRawSessionBackgroundDrawings(DateTime anchorDate)
        {
            for (int sid = 1; sid <= SubSessionCount; sid++)
            {
                if (!S_Active(sid)) continue;

                DateTime sessionStart = GetSessionEntryWindowStart(anchorDate, sid);
                string rawTag = string.Format("MICH_{0}Fill_{1:yyyyMMdd_HHmm}", S_Label(sid), sessionStart);
                RemoveDrawObject(rawTag);
            }
        }

        private void DrawSessionBackground(int sid, DateTime sessionStart, DateTime sessionEnd)
        {
            if (sessionEnd <= sessionStart) return;

            int parentGroup = ParentGroupOf(sid);
            Brush fillBrush = parentGroup == 1 ? Brushes.Gold : parentGroup == 2 ? Brushes.CornflowerBlue : Brushes.MediumSeaGreen;
            string rectTag = string.Format("MICH_{0}Fill_{1:yyyyMMdd_HHmm}_{2:yyyyMMdd_HHmm}", S_Label(sid), sessionStart, sessionEnd);
            if (DrawObjects[rectTag] != null) return;
            Draw.Rectangle(this, rectTag, false, sessionStart, 0, sessionEnd, 30000, Brushes.Transparent, fillBrush, 10).ZOrder = -1;
        }

        private void DrawNewsWindows(DateTime barTime)
        {
            if (!UseNewsSkip) return;
            EnsureNewsDatesInitialized();
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
            if (!legacyInfoDrawingsCleared)
            {
                RemoveLegacyInfoBoxDrawings();
                legacyInfoDrawingsCleared = true;
            }
            ChartControl.Dispatcher.InvokeAsync(() => RenderInfoBoxOverlay(lines));
        }

        private void RenderInfoBoxOverlay(List<Tuple<string, string, Brush, Brush>> lines)
        {
            if (!EnsureInfoBoxOverlay() || infoBoxRowsPanel == null) return;
            infoBoxRowsPanel.Children.Clear();
            for (int i = 0; i < lines.Count; i++)
            {
                bool isHeader = i == 0;
                bool isFooter = i == lines.Count - 1;
                var rowBorder = new Border
                {
                    Background = (isHeader || isFooter) ? InfoHeaderFooterGradientBrush : (i % 2 == 0 ? InfoBodyEvenBrush : InfoBodyOddBrush),
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
                string label = lines[i].Item1 ?? string.Empty;
                string value = lines[i].Item2 ?? string.Empty;
                string normalizedValue = NormalizeInfoValueToken(value);
                bool valueUsesEmojiRendering = ClassifyInfoValueRunKind(normalizedValue) == InfoValueRunKind.Emoji;
                TextOptions.SetTextRenderingMode(text, valueUsesEmojiRendering ? TextRenderingMode.Grayscale : TextRenderingMode.ClearType);
                text.Inlines.Add(new Run(label) { Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : InfoLabelBrush });
                if (!string.IsNullOrEmpty(value))
                {
                    text.Inlines.Add(new Run(" ") { Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : InfoLabelBrush });
                    Brush stateValueBrush = lines[i].Item4;
                    if (stateValueBrush == null || stateValueBrush == Brushes.Transparent) stateValueBrush = lines[i].Item3;
                    if (stateValueBrush == null || stateValueBrush == Brushes.Transparent) stateValueBrush = InfoValueBrush;
                    text.Inlines.Add(BuildInfoValueRun(normalizedValue, stateValueBrush));
                }
                rowBorder.Child = text;
                infoBoxRowsPanel.Children.Add(rowBorder);
            }
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
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            string token = value.Trim();
            if (token == "○" || token == "◯" || token == "⚪") return "⛔";
            return value;
        }

        private InfoValueRunKind ClassifyInfoValueRunKind(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return InfoValueRunKind.Default;
            string token = value.Trim();
            if (InfoEmojiTokens.Contains(token) || ContainsEmojiCodePoint(token)) return InfoValueRunKind.Emoji;
            if (InfoSymbolTokens.Contains(token)) return InfoValueRunKind.Symbol;
            return InfoValueRunKind.Default;
        }

        private bool ContainsEmojiCodePoint(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                if ((cp >= 0x1F300 && cp <= 0x1FAFF) || (cp >= 0x2600 && cp <= 0x27BF)) return true;
            }
            return false;
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
                        if (parent != null) parent.Children.Remove(infoBoxContainer);
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
            RemoveDrawObject("Info");
            RemoveDrawObject("myStatusLabel_bg");
            for (int i = 0; i < 64; i++)
            {
                RemoveDrawObject(string.Format("myStatusLabel_bg_{0}", i));
                RemoveDrawObject(string.Format("myStatusLabel_label_{0}", i));
                RemoveDrawObject(string.Format("myStatusLabel_val_{0}", i));
            }
        }

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

        private int ParentGroupOf(int sid)
        {
            return sid <= 3 ? 1 : sid <= 6 ? 2 : 3;
        }

        private string FormatInfoSessionLabel(int sid)
        {
            switch (sid)
            {
                case 1: return "New York A";
                case 2: return "New York B";
                case 3: return "New York C";
                case 4: return "London A";
                case 5: return "London B";
                case 6: return "London C";
                case 7: return "Asia A";
                case 8: return "Asia B";
                case 9: return "Asia C";
                default: return "Off";
            }
        }

        private string BuildContractsInfoText(int sid)
        {
            if (sid >= 1 && sid <= SubSessionCount)
                return S_Contracts(sid).ToString(CultureInfo.InvariantCulture);

            if (Position != null && Position.Quantity > 0)
                return Position.Quantity.ToString(CultureInfo.InvariantCulture);

            int defaultSid = GetDefaultEnabledSessionId();
            return defaultSid >= 1 && defaultSid <= SubSessionCount
                ? S_Contracts(defaultSid).ToString(CultureInfo.InvariantCulture)
                : "Off";
        }

        private List<Tuple<string, string, Brush, Brush>> BuildInfoLines()
        {
            var lines = new List<Tuple<string, string, Brush, Brush>>();
            int infoSessionId = GetInfoSessionId();
            lines.Add(InfoLine(string.Format("MICH v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(InfoLine("Contracts:", BuildContractsInfoText(infoSessionId), Brushes.LightGray, InfoValueBrush));

            if (!UseNewsSkip)
            {
                lines.Add(InfoLine("News:", "Disabled", Brushes.LightGray, InfoValueBrush));
            }
            else
            {
                List<DateTime> weekNews = GetCurrentWeekNews(Time[0]);
                if (weekNews.Count == 0)
                {
                    lines.Add(InfoLine("News:", "🚫", Brushes.LightGray, Brushes.IndianRed));
                }
                else
                {
                    for (int i = 0; i < weekNews.Count; i++)
                    {
                        DateTime newsTime = weekNews[i];
                        bool blockPassed = Time[0] > newsTime.AddMinutes(NewsBlockMinutes);
                        string value = newsTime.ToString("ddd", CultureInfo.InvariantCulture) + " " + newsTime.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
                        Brush newsBrush = blockPassed ? PassedNewsRowBrush : Brushes.LightGray;
                        lines.Add(InfoLine("News:", value, newsBrush, newsBrush));
                    }
                }
            }

            lines.Add(InfoLine("Session:", FormatInfoSessionLabel(infoSessionId), Brushes.LightGray, InfoValueBrush));
            lines.Add(InfoLine("AutoEdge Systems™", string.Empty, InfoLabelBrush, Brushes.Transparent));
            return lines;
        }

        private Tuple<string, string, Brush, Brush> InfoLine(string label, string value, Brush labelBrush, Brush valueBrush)
        {
            return new Tuple<string, string, Brush, Brush>(label, value, labelBrush, valueBrush);
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

        private string GetAddOnVersion()
        {
            Assembly assembly = GetType().Assembly;
            Version version = assembly.GetName().Version;
            return version != null ? version.ToString() : "0.0.0.0";
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

        #endregion

        private void CancelWorkingEntryOrder()
        {
            if (!IsOrderCancelable(entryOrder)) return;
            try
            {
                CancelOrder(entryOrder);
            }
            catch { }
        }

        #endregion

        // =====================================================================
        #region Properties

        // ── Global ─────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Session Start Time",Order=1,GroupName="0. Global")] public DateTime SessionStartTime {get;set;}
        [NinjaScriptProperty][Display(Name="Require Entry Confirmation",Order=2,GroupName="0. Global")] public bool RequireEntryConfirmation {get;set;}
        [NinjaScriptProperty][Display(Name="NY Enable (Parent)",Order=3,GroupName="0. Global")] public bool NyEnable {get;set;}
        [NinjaScriptProperty][Display(Name="EU Enable (Parent)",Order=4,GroupName="0. Global")] public bool EuEnable {get;set;}
        [NinjaScriptProperty][Display(Name="AS Enable (Parent)",Order=5,GroupName="0. Global")] public bool AsEnable {get;set;}
        [NinjaScriptProperty][Range(0.0,double.MaxValue)][Display(Name="Max Account Balance",Description="When net liquidation reaches or exceeds this value, entries are blocked and open positions are flattened. 0 disables.",Order=6,GroupName="0. Global")] public double MaxAccountBalance {get;set;}
        [NinjaScriptProperty][Display(Name="Use News Skip",Order=7,GroupName="0. Global")] public bool UseNewsSkip {get;set;}
        [NinjaScriptProperty][Range(0,60)][Display(Name="News Block Minutes",Order=8,GroupName="0. Global")] public int NewsBlockMinutes {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Flatten On Blocked Window",Description="If enabled, flatten when entering a no-new-trades or news-block window.",Order=9,GroupName="0. Global")] public bool FlattenOnBlockedWindowTransition {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Debug Logging",Order=10,GroupName="0. Global")] public bool DebugLogging {get;set;}

        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Webhooks",Description="Enable outbound order webhooks.",Order=0,GroupName="12. Webhooks")] public bool UseWebhooks {get;set;}
        [NinjaScriptProperty][Display(Name="Webhook Provider",Description="Select webhook target: TradersPost or ProjectX.",Order=1,GroupName="12. Webhooks")] public WebhookProvider WebhookProviderType {get;set;}
        [NinjaScriptProperty][Display(Name="TradersPost Webhook URL",Description="HTTP endpoint for TradersPost order webhooks.",Order=2,GroupName="12. Webhooks")] public string WebhookUrl {get;set;}
        [NinjaScriptProperty][Display(Name="Webhook Ticker Override",Description="Optional TradersPost ticker override. Leave empty to use the chart instrument.",Order=3,GroupName="12. Webhooks")] public string WebhookTickerOverride {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="ProjectX API Base URL",Description="ProjectX gateway base URL.",Order=4,GroupName="12. Webhooks")] public string ProjectXApiBaseUrl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="ProjectX Trade All Accounts",Description="If enabled, route ProjectX orders to every tradable account returned by ProjectX.",Order=5,GroupName="12. Webhooks")] public bool ProjectXTradeAllAccounts {get;set;}
        [NinjaScriptProperty][Display(Name="ProjectX Username",Description="ProjectX login username.",Order=6,GroupName="12. Webhooks")] public string ProjectXUsername {get;set;}
        [NinjaScriptProperty][Display(Name="ProjectX API Key",Description="ProjectX API key.",Order=7,GroupName="12. Webhooks")] public string ProjectXApiKey {get;set;}
        [NinjaScriptProperty][Display(Name="ProjectX Accounts",Description="Comma-separated ProjectX account ids or exact account names.",Order=8,GroupName="12. Webhooks")] public string ProjectXAccountId {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="ProjectX Contract ID",Description="Optional direct contract id override.",Order=9,GroupName="12. Webhooks")] public string ProjectXContractId {get;set;}

        // ── Global MA (entry) ─────────────────────────────────────────────────
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="MA Period",  Order=10,GroupName="0. Global")] public int    GlobalMaPeriod  {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="EMA Period", Order=11,GroupName="0. Global")] public int    GlobalEmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="MA Type",              Order=12,GroupName="0. Global")] public MAMode GlobalMaType   {get;set;}
        // ── Global Contracts ─────────────────────────────────────────────────
        [NinjaScriptProperty][Range(1,10)][Display(Name="Contracts",Order=13,GroupName="0. Global")] public int GlobalContracts {get;set;}



        // ── NY-A Session ────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="1a. NY-A Session")] public bool NyAEnable {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="1a. NY-A Session")] public DateTime NyATradeWindowStart {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable No-New-Trades After",Order=3,GroupName="1a. NY-A Session")] public bool NyAEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="1a. NY-A Session")] public DateTime NyANoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Forced Close",Order=5,GroupName="1a. NY-A Session")] public bool NyAEnableForcedClose {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="1a. NY-A Session")] public DateTime NyAForcedCloseTime {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="1a. NY-A Session")] public int NyAMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="1a. NY-A Session")] public int NyAMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Browsable(false)][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="1a. NY-A Session")] public int NyAMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="1a. NY-A Session")] public int NyAMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Require Direction Flip",Order=15,GroupName="1a. NY-A Session")] public bool NyARequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="1a. NY-A Session")] public bool NyAAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="1a. NY-A Session")] public int NyAEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="1a. NY-A Session")] public int NyAEngulfingExitPaddingTicks {get;set;}
        // NY-A Long
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="1b. NY-A Long")] public double NyALongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="1b. NY-A Long")] public int NyALongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="1b. NY-A Long")] public int NyALongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="1b. NY-A Long")] public int NyALongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="1b. NY-A Long")] public double NyALongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="1b. NY-A Long")] public bool NyALongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="1b. NY-A Long")] public bool NyALongSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="1b. NY-A Long")] public bool NyALongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="1b. NY-A Long")] public int NyALongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="1b. NY-A Long")] public int NyALongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="1b. NY-A Long")] public int NyALongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="1b. NY-A Long")] public bool NyALongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="1b. NY-A Long")] public int NyALongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="1b. NY-A Long")] public int NyALongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="1b. NY-A Long")] public int NyALongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="1b. NY-A Long")] public bool NyALongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="1b. NY-A Long")] public int NyALongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="1b. NY-A Long")] public bool NyALongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="1b. NY-A Long")] public int NyALongWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="1b. NY-A Long")] public double NyALongMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="1b. NY-A Long")] public int NyALongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="1b. NY-A Long")] public bool NyALongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="1b. NY-A Long")] public int NyALongAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="1b. NY-A Long")] public int NyALongAdxMinLevel {get;set;}
        // NY-A Short
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="1c. NY-A Short")] public double NyAShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="1c. NY-A Short")] public int NyAShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="1c. NY-A Short")] public int NyAShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="1c. NY-A Short")] public int NyAShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="1c. NY-A Short")] public double NyAShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="1c. NY-A Short")] public bool NyAShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="1c. NY-A Short")] public bool NyAShortSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="1c. NY-A Short")] public bool NyAShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="1c. NY-A Short")] public int NyAShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="1c. NY-A Short")] public int NyAShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="1c. NY-A Short")] public int NyAShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="1c. NY-A Short")] public bool NyAShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="1c. NY-A Short")] public int NyAShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="1c. NY-A Short")] public int NyAShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="1c. NY-A Short")] public int NyAShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="1c. NY-A Short")] public bool NyAShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="1c. NY-A Short")] public int NyAShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="1c. NY-A Short")] public bool NyAShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="1c. NY-A Short")] public int NyAShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="1c. NY-A Short")] public double NyAShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="1c. NY-A Short")] public int NyAShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="1c. NY-A Short")] public bool NyAShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="1c. NY-A Short")] public int NyAShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="1c. NY-A Short")] public int NyAShortAdxMinLevel {get;set;}

        // ── NY-B ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="2a. NY-B Session")] public bool NyBEnable {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="2a. NY-B Session")] public DateTime NyBTradeWindowStart {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable No-New-Trades After",Order=3,GroupName="2a. NY-B Session")] public bool NyBEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="2a. NY-B Session")] public DateTime NyBNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Forced Close",Order=5,GroupName="2a. NY-B Session")] public bool NyBEnableForcedClose {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="2a. NY-B Session")] public DateTime NyBForcedCloseTime {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="2a. NY-B Session")] public int NyBMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="2a. NY-B Session")] public int NyBMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Browsable(false)][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="2a. NY-B Session")] public int NyBMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="2a. NY-B Session")] public int NyBMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Require Direction Flip",Order=15,GroupName="2a. NY-B Session")] public bool NyBRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="2a. NY-B Session")] public bool NyBAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="2a. NY-B Session")] public int NyBEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="2a. NY-B Session")] public int NyBEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="2b. NY-B Long")] public double NyBLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="2b. NY-B Long")] public int NyBLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="2b. NY-B Long")] public int NyBLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="2b. NY-B Long")] public int NyBLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="2b. NY-B Long")] public double NyBLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="2b. NY-B Long")] public bool NyBLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="2b. NY-B Long")] public bool NyBLongSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="2b. NY-B Long")] public bool NyBLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="2b. NY-B Long")] public int NyBLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="2b. NY-B Long")] public int NyBLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="2b. NY-B Long")] public int NyBLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="2b. NY-B Long")] public bool NyBLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="2b. NY-B Long")] public int NyBLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="2b. NY-B Long")] public int NyBLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="2b. NY-B Long")] public int NyBLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="2b. NY-B Long")] public bool NyBLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="2b. NY-B Long")] public int NyBLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="2b. NY-B Long")] public bool NyBLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="2b. NY-B Long")] public int NyBLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="2b. NY-B Long")] public double NyBLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="2b. NY-B Long")] public int NyBLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="2b. NY-B Long")] public bool NyBLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="2b. NY-B Long")] public int NyBLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="2b. NY-B Long")] public int NyBLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="2c. NY-B Short")] public double NyBShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="2c. NY-B Short")] public int NyBShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="2c. NY-B Short")] public int NyBShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="2c. NY-B Short")] public int NyBShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="2c. NY-B Short")] public double NyBShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="2c. NY-B Short")] public bool NyBShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="2c. NY-B Short")] public bool NyBShortSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="2c. NY-B Short")] public bool NyBShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="2c. NY-B Short")] public int NyBShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="2c. NY-B Short")] public int NyBShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="2c. NY-B Short")] public int NyBShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="2c. NY-B Short")] public bool NyBShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="2c. NY-B Short")] public int NyBShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="2c. NY-B Short")] public int NyBShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="2c. NY-B Short")] public int NyBShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="2c. NY-B Short")] public bool NyBShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="2c. NY-B Short")] public int NyBShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="2c. NY-B Short")] public bool NyBShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="2c. NY-B Short")] public int NyBShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="2c. NY-B Short")] public double NyBShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="2c. NY-B Short")] public int NyBShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="2c. NY-B Short")] public bool NyBShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="2c. NY-B Short")] public int NyBShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="2c. NY-B Short")] public int NyBShortAdxMinLevel {get;set;}
        // ── NY-C ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="3a. NY-C Session")] public bool NyCEnable {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="3a. NY-C Session")] public DateTime NyCTradeWindowStart {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable No-New-Trades After",Order=3,GroupName="3a. NY-C Session")] public bool NyCEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="3a. NY-C Session")] public DateTime NyCNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Forced Close",Order=5,GroupName="3a. NY-C Session")] public bool NyCEnableForcedClose {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="3a. NY-C Session")] public DateTime NyCForcedCloseTime {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="3a. NY-C Session")] public int NyCMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="3a. NY-C Session")] public int NyCMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Browsable(false)][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="3a. NY-C Session")] public int NyCMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="3a. NY-C Session")] public int NyCMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Require Direction Flip",Order=15,GroupName="3a. NY-C Session")] public bool NyCRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="3a. NY-C Session")] public bool NyCAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="3a. NY-C Session")] public int NyCEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="3a. NY-C Session")] public int NyCEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="3b. NY-C Long")] public double NyCLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="3b. NY-C Long")] public int NyCLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="3b. NY-C Long")] public int NyCLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="3b. NY-C Long")] public int NyCLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="3b. NY-C Long")] public double NyCLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="3b. NY-C Long")] public bool NyCLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="3b. NY-C Long")] public bool NyCLongSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="3b. NY-C Long")] public bool NyCLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="3b. NY-C Long")] public int NyCLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="3b. NY-C Long")] public int NyCLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="3b. NY-C Long")] public int NyCLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="3b. NY-C Long")] public bool NyCLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="3b. NY-C Long")] public int NyCLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="3b. NY-C Long")] public int NyCLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="3b. NY-C Long")] public int NyCLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="3b. NY-C Long")] public bool NyCLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="3b. NY-C Long")] public int NyCLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="3b. NY-C Long")] public bool NyCLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="3b. NY-C Long")] public int NyCLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="3b. NY-C Long")] public double NyCLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="3b. NY-C Long")] public int NyCLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="3b. NY-C Long")] public bool NyCLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="3b. NY-C Long")] public int NyCLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="3b. NY-C Long")] public int NyCLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="3c. NY-C Short")] public double NyCShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="3c. NY-C Short")] public int NyCShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="3c. NY-C Short")] public int NyCShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="3c. NY-C Short")] public int NyCShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="3c. NY-C Short")] public double NyCShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="3c. NY-C Short")] public bool NyCShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="3c. NY-C Short")] public bool NyCShortSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="3c. NY-C Short")] public bool NyCShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="3c. NY-C Short")] public int NyCShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="3c. NY-C Short")] public int NyCShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="3c. NY-C Short")] public int NyCShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="3c. NY-C Short")] public bool NyCShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="3c. NY-C Short")] public int NyCShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="3c. NY-C Short")] public int NyCShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="3c. NY-C Short")] public int NyCShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="3c. NY-C Short")] public bool NyCShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="3c. NY-C Short")] public int NyCShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="3c. NY-C Short")] public bool NyCShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="3c. NY-C Short")] public int NyCShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="3c. NY-C Short")] public double NyCShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="3c. NY-C Short")] public int NyCShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="3c. NY-C Short")] public bool NyCShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="3c. NY-C Short")] public int NyCShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="3c. NY-C Short")] public int NyCShortAdxMinLevel {get;set;}
        // ── EU-A ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="4a. EU-A Session")] public bool EuAEnable {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="4a. EU-A Session")] public DateTime EuATradeWindowStart {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable No-New-Trades After",Order=3,GroupName="4a. EU-A Session")] public bool EuAEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="4a. EU-A Session")] public DateTime EuANoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Forced Close",Order=5,GroupName="4a. EU-A Session")] public bool EuAEnableForcedClose {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="4a. EU-A Session")] public DateTime EuAForcedCloseTime {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="4a. EU-A Session")] public int EuAMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="4a. EU-A Session")] public int EuAMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Browsable(false)][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="4a. EU-A Session")] public int EuAMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="4a. EU-A Session")] public int EuAMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Require Direction Flip",Order=15,GroupName="4a. EU-A Session")] public bool EuARequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="4a. EU-A Session")] public bool EuAAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="4a. EU-A Session")] public int EuAEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="4a. EU-A Session")] public int EuAEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="4b. EU-A Long")] public double EuALongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="4b. EU-A Long")] public int EuALongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="4b. EU-A Long")] public int EuALongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="4b. EU-A Long")] public int EuALongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="4b. EU-A Long")] public double EuALongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="4b. EU-A Long")] public bool EuALongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="4b. EU-A Long")] public bool EuALongSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="4b. EU-A Long")] public bool EuALongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="4b. EU-A Long")] public int EuALongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="4b. EU-A Long")] public int EuALongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="4b. EU-A Long")] public int EuALongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="4b. EU-A Long")] public bool EuALongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="4b. EU-A Long")] public int EuALongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="4b. EU-A Long")] public int EuALongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="4b. EU-A Long")] public int EuALongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="4b. EU-A Long")] public bool EuALongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="4b. EU-A Long")] public int EuALongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="4b. EU-A Long")] public bool EuALongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="4b. EU-A Long")] public int EuALongWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="4b. EU-A Long")] public double EuALongMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="4b. EU-A Long")] public int EuALongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="4b. EU-A Long")] public bool EuALongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="4b. EU-A Long")] public int EuALongAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="4b. EU-A Long")] public int EuALongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="4c. EU-A Short")] public double EuAShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="4c. EU-A Short")] public int EuAShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="4c. EU-A Short")] public int EuAShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="4c. EU-A Short")] public int EuAShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="4c. EU-A Short")] public double EuAShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="4c. EU-A Short")] public bool EuAShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="4c. EU-A Short")] public bool EuAShortSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="4c. EU-A Short")] public bool EuAShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="4c. EU-A Short")] public int EuAShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="4c. EU-A Short")] public int EuAShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="4c. EU-A Short")] public int EuAShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="4c. EU-A Short")] public bool EuAShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="4c. EU-A Short")] public int EuAShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="4c. EU-A Short")] public int EuAShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="4c. EU-A Short")] public int EuAShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="4c. EU-A Short")] public bool EuAShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="4c. EU-A Short")] public int EuAShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="4c. EU-A Short")] public bool EuAShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="4c. EU-A Short")] public int EuAShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="4c. EU-A Short")] public double EuAShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="4c. EU-A Short")] public int EuAShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="4c. EU-A Short")] public bool EuAShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="4c. EU-A Short")] public int EuAShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="4c. EU-A Short")] public int EuAShortAdxMinLevel {get;set;}
        // ── EU-B ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="5a. EU-B Session")] public bool EuBEnable {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="5a. EU-B Session")] public DateTime EuBTradeWindowStart {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable No-New-Trades After",Order=3,GroupName="5a. EU-B Session")] public bool EuBEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="5a. EU-B Session")] public DateTime EuBNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Forced Close",Order=5,GroupName="5a. EU-B Session")] public bool EuBEnableForcedClose {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="5a. EU-B Session")] public DateTime EuBForcedCloseTime {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="5a. EU-B Session")] public int EuBMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="5a. EU-B Session")] public int EuBMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Browsable(false)][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="5a. EU-B Session")] public int EuBMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="5a. EU-B Session")] public int EuBMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Require Direction Flip",Order=15,GroupName="5a. EU-B Session")] public bool EuBRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="5a. EU-B Session")] public bool EuBAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="5a. EU-B Session")] public int EuBEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="5a. EU-B Session")] public int EuBEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="5b. EU-B Long")] public double EuBLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="5b. EU-B Long")] public int EuBLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="5b. EU-B Long")] public int EuBLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="5b. EU-B Long")] public int EuBLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="5b. EU-B Long")] public double EuBLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="5b. EU-B Long")] public bool EuBLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="5b. EU-B Long")] public bool EuBLongSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="5b. EU-B Long")] public bool EuBLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="5b. EU-B Long")] public int EuBLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="5b. EU-B Long")] public int EuBLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="5b. EU-B Long")] public int EuBLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="5b. EU-B Long")] public bool EuBLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="5b. EU-B Long")] public int EuBLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="5b. EU-B Long")] public int EuBLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="5b. EU-B Long")] public int EuBLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="5b. EU-B Long")] public bool EuBLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="5b. EU-B Long")] public int EuBLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="5b. EU-B Long")] public bool EuBLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="5b. EU-B Long")] public int EuBLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="5b. EU-B Long")] public double EuBLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="5b. EU-B Long")] public int EuBLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="5b. EU-B Long")] public bool EuBLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="5b. EU-B Long")] public int EuBLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="5b. EU-B Long")] public int EuBLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="5c. EU-B Short")] public double EuBShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="5c. EU-B Short")] public int EuBShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="5c. EU-B Short")] public int EuBShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="5c. EU-B Short")] public int EuBShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="5c. EU-B Short")] public double EuBShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="5c. EU-B Short")] public bool EuBShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="5c. EU-B Short")] public bool EuBShortSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="5c. EU-B Short")] public bool EuBShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="5c. EU-B Short")] public int EuBShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="5c. EU-B Short")] public int EuBShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="5c. EU-B Short")] public int EuBShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="5c. EU-B Short")] public bool EuBShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="5c. EU-B Short")] public int EuBShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="5c. EU-B Short")] public int EuBShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="5c. EU-B Short")] public int EuBShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="5c. EU-B Short")] public bool EuBShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="5c. EU-B Short")] public int EuBShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="5c. EU-B Short")] public bool EuBShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="5c. EU-B Short")] public int EuBShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="5c. EU-B Short")] public double EuBShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="5c. EU-B Short")] public int EuBShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="5c. EU-B Short")] public bool EuBShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="5c. EU-B Short")] public int EuBShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="5c. EU-B Short")] public int EuBShortAdxMinLevel {get;set;}
        // ── EU-C ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="6a. EU-C Session")] public bool EuCEnable {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="6a. EU-C Session")] public DateTime EuCTradeWindowStart {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable No-New-Trades After",Order=3,GroupName="6a. EU-C Session")] public bool EuCEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="6a. EU-C Session")] public DateTime EuCNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Forced Close",Order=5,GroupName="6a. EU-C Session")] public bool EuCEnableForcedClose {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="6a. EU-C Session")] public DateTime EuCForcedCloseTime {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="6a. EU-C Session")] public int EuCMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="6a. EU-C Session")] public int EuCMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Browsable(false)][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="6a. EU-C Session")] public int EuCMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="6a. EU-C Session")] public int EuCMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Require Direction Flip",Order=15,GroupName="6a. EU-C Session")] public bool EuCRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="6a. EU-C Session")] public bool EuCAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="6a. EU-C Session")] public int EuCEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="6a. EU-C Session")] public int EuCEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="6b. EU-C Long")] public double EuCLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="6b. EU-C Long")] public int EuCLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="6b. EU-C Long")] public int EuCLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="6b. EU-C Long")] public int EuCLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="6b. EU-C Long")] public double EuCLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="6b. EU-C Long")] public bool EuCLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="6b. EU-C Long")] public bool EuCLongSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="6b. EU-C Long")] public bool EuCLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="6b. EU-C Long")] public int EuCLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="6b. EU-C Long")] public int EuCLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="6b. EU-C Long")] public int EuCLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="6b. EU-C Long")] public bool EuCLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="6b. EU-C Long")] public int EuCLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="6b. EU-C Long")] public int EuCLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="6b. EU-C Long")] public int EuCLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="6b. EU-C Long")] public bool EuCLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="6b. EU-C Long")] public int EuCLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="6b. EU-C Long")] public bool EuCLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="6b. EU-C Long")] public int EuCLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="6b. EU-C Long")] public double EuCLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="6b. EU-C Long")] public int EuCLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="6b. EU-C Long")] public bool EuCLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="6b. EU-C Long")] public int EuCLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="6b. EU-C Long")] public int EuCLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="6c. EU-C Short")] public double EuCShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="6c. EU-C Short")] public int EuCShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="6c. EU-C Short")] public int EuCShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="6c. EU-C Short")] public int EuCShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="6c. EU-C Short")] public double EuCShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="6c. EU-C Short")] public bool EuCShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="6c. EU-C Short")] public bool EuCShortSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="6c. EU-C Short")] public bool EuCShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="6c. EU-C Short")] public int EuCShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="6c. EU-C Short")] public int EuCShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="6c. EU-C Short")] public int EuCShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="6c. EU-C Short")] public bool EuCShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="6c. EU-C Short")] public int EuCShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="6c. EU-C Short")] public int EuCShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="6c. EU-C Short")] public int EuCShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="6c. EU-C Short")] public bool EuCShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="6c. EU-C Short")] public int EuCShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="6c. EU-C Short")] public bool EuCShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="6c. EU-C Short")] public int EuCShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="6c. EU-C Short")] public double EuCShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="6c. EU-C Short")] public int EuCShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="6c. EU-C Short")] public bool EuCShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="6c. EU-C Short")] public int EuCShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="6c. EU-C Short")] public int EuCShortAdxMinLevel {get;set;}
        // ── AS-A ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="7a. AS-A Session")] public bool AsAEnable {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="7a. AS-A Session")] public DateTime AsATradeWindowStart {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable No-New-Trades After",Order=3,GroupName="7a. AS-A Session")] public bool AsAEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="7a. AS-A Session")] public DateTime AsANoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Forced Close",Order=5,GroupName="7a. AS-A Session")] public bool AsAEnableForcedClose {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="7a. AS-A Session")] public DateTime AsAForcedCloseTime {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="7a. AS-A Session")] public int AsAMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="7a. AS-A Session")] public int AsAMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Browsable(false)][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="7a. AS-A Session")] public int AsAMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="7a. AS-A Session")] public int AsAMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Require Direction Flip",Order=15,GroupName="7a. AS-A Session")] public bool AsARequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="7a. AS-A Session")] public bool AsAAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="7a. AS-A Session")] public int AsAEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="7a. AS-A Session")] public int AsAEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="7b. AS-A Long")] public double AsALongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="7b. AS-A Long")] public int AsALongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="7b. AS-A Long")] public int AsALongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="7b. AS-A Long")] public int AsALongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="7b. AS-A Long")] public double AsALongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="7b. AS-A Long")] public bool AsALongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="7b. AS-A Long")] public bool AsALongSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="7b. AS-A Long")] public bool AsALongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="7b. AS-A Long")] public int AsALongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="7b. AS-A Long")] public int AsALongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="7b. AS-A Long")] public int AsALongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="7b. AS-A Long")] public bool AsALongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="7b. AS-A Long")] public int AsALongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="7b. AS-A Long")] public int AsALongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="7b. AS-A Long")] public int AsALongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="7b. AS-A Long")] public bool AsALongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="7b. AS-A Long")] public int AsALongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="7b. AS-A Long")] public bool AsALongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="7b. AS-A Long")] public int AsALongWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="7b. AS-A Long")] public double AsALongMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="7b. AS-A Long")] public int AsALongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="7b. AS-A Long")] public bool AsALongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="7b. AS-A Long")] public int AsALongAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="7b. AS-A Long")] public int AsALongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="7c. AS-A Short")] public double AsAShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="7c. AS-A Short")] public int AsAShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="7c. AS-A Short")] public int AsAShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="7c. AS-A Short")] public int AsAShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="7c. AS-A Short")] public double AsAShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="7c. AS-A Short")] public bool AsAShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="7c. AS-A Short")] public bool AsAShortSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="7c. AS-A Short")] public bool AsAShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="7c. AS-A Short")] public int AsAShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="7c. AS-A Short")] public int AsAShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="7c. AS-A Short")] public int AsAShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="7c. AS-A Short")] public bool AsAShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="7c. AS-A Short")] public int AsAShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="7c. AS-A Short")] public int AsAShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="7c. AS-A Short")] public int AsAShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="7c. AS-A Short")] public bool AsAShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="7c. AS-A Short")] public int AsAShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="7c. AS-A Short")] public bool AsAShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="7c. AS-A Short")] public int AsAShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="7c. AS-A Short")] public double AsAShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="7c. AS-A Short")] public int AsAShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="7c. AS-A Short")] public bool AsAShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="7c. AS-A Short")] public int AsAShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="7c. AS-A Short")] public int AsAShortAdxMinLevel {get;set;}
        // ── AS-B ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="8a. AS-B Session")] public bool AsBEnable {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="8a. AS-B Session")] public DateTime AsBTradeWindowStart {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable No-New-Trades After",Order=3,GroupName="8a. AS-B Session")] public bool AsBEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="8a. AS-B Session")] public DateTime AsBNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Forced Close",Order=5,GroupName="8a. AS-B Session")] public bool AsBEnableForcedClose {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="8a. AS-B Session")] public DateTime AsBForcedCloseTime {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="8a. AS-B Session")] public int AsBMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="8a. AS-B Session")] public int AsBMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Browsable(false)][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="8a. AS-B Session")] public int AsBMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="8a. AS-B Session")] public int AsBMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Require Direction Flip",Order=15,GroupName="8a. AS-B Session")] public bool AsBRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="8a. AS-B Session")] public bool AsBAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="8a. AS-B Session")] public int AsBEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="8a. AS-B Session")] public int AsBEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="8b. AS-B Long")] public double AsBLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="8b. AS-B Long")] public int AsBLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="8b. AS-B Long")] public int AsBLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="8b. AS-B Long")] public int AsBLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="8b. AS-B Long")] public double AsBLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="8b. AS-B Long")] public bool AsBLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="8b. AS-B Long")] public bool AsBLongSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="8b. AS-B Long")] public bool AsBLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="8b. AS-B Long")] public int AsBLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="8b. AS-B Long")] public int AsBLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="8b. AS-B Long")] public int AsBLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="8b. AS-B Long")] public bool AsBLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="8b. AS-B Long")] public int AsBLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="8b. AS-B Long")] public int AsBLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="8b. AS-B Long")] public int AsBLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="8b. AS-B Long")] public bool AsBLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="8b. AS-B Long")] public int AsBLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="8b. AS-B Long")] public bool AsBLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="8b. AS-B Long")] public int AsBLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="8b. AS-B Long")] public double AsBLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="8b. AS-B Long")] public int AsBLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="8b. AS-B Long")] public bool AsBLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="8b. AS-B Long")] public int AsBLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="8b. AS-B Long")] public int AsBLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="8c. AS-B Short")] public double AsBShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="8c. AS-B Short")] public int AsBShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="8c. AS-B Short")] public int AsBShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="8c. AS-B Short")] public int AsBShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="8c. AS-B Short")] public double AsBShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="8c. AS-B Short")] public bool AsBShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="8c. AS-B Short")] public bool AsBShortSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="8c. AS-B Short")] public bool AsBShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="8c. AS-B Short")] public int AsBShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="8c. AS-B Short")] public int AsBShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="8c. AS-B Short")] public int AsBShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="8c. AS-B Short")] public bool AsBShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="8c. AS-B Short")] public int AsBShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="8c. AS-B Short")] public int AsBShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="8c. AS-B Short")] public int AsBShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="8c. AS-B Short")] public bool AsBShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="8c. AS-B Short")] public int AsBShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="8c. AS-B Short")] public bool AsBShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="8c. AS-B Short")] public int AsBShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="8c. AS-B Short")] public double AsBShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="8c. AS-B Short")] public int AsBShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="8c. AS-B Short")] public bool AsBShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="8c. AS-B Short")] public int AsBShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="8c. AS-B Short")] public int AsBShortAdxMinLevel {get;set;}
        // ── AS-C ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="9a. AS-C Session")] public bool AsCEnable {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="9a. AS-C Session")] public DateTime AsCTradeWindowStart {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable No-New-Trades After",Order=3,GroupName="9a. AS-C Session")] public bool AsCEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="9a. AS-C Session")] public DateTime AsCNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Forced Close",Order=5,GroupName="9a. AS-C Session")] public bool AsCEnableForcedClose {get;set;}
        [NinjaScriptProperty][Browsable(false)][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="9a. AS-C Session")] public DateTime AsCForcedCloseTime {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="9a. AS-C Session")] public int AsCMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="9a. AS-C Session")] public int AsCMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Browsable(false)][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="9a. AS-C Session")] public int AsCMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="9a. AS-C Session")] public int AsCMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Require Direction Flip",Order=15,GroupName="9a. AS-C Session")] public bool AsCRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="9a. AS-C Session")] public bool AsCAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="9a. AS-C Session")] public int AsCEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="9a. AS-C Session")] public int AsCEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="9b. AS-C Long")] public double AsCLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="9b. AS-C Long")] public int AsCLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="9b. AS-C Long")] public int AsCLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="9b. AS-C Long")] public int AsCLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="9b. AS-C Long")] public double AsCLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="9b. AS-C Long")] public bool AsCLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="9b. AS-C Long")] public bool AsCLongSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="9b. AS-C Long")] public bool AsCLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="9b. AS-C Long")] public int AsCLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="9b. AS-C Long")] public int AsCLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="9b. AS-C Long")] public int AsCLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="9b. AS-C Long")] public bool AsCLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="9b. AS-C Long")] public int AsCLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="9b. AS-C Long")] public int AsCLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="9b. AS-C Long")] public int AsCLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="9b. AS-C Long")] public bool AsCLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="9b. AS-C Long")] public int AsCLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="9b. AS-C Long")] public bool AsCLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="9b. AS-C Long")] public int AsCLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="9b. AS-C Long")] public double AsCLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="9b. AS-C Long")] public int AsCLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="9b. AS-C Long")] public bool AsCLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="9b. AS-C Long")] public int AsCLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="9b. AS-C Long")] public int AsCLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="9c. AS-C Short")] public double AsCShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="9c. AS-C Short")] public int AsCShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="9c. AS-C Short")] public int AsCShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="9c. AS-C Short")] public int AsCShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="9c. AS-C Short")] public double AsCShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Use Prior Candle SL",Order=9,GroupName="9c. AS-C Short")] public bool AsCShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="SL At MA",Order=10,GroupName="9c. AS-C Short")] public bool AsCShortSlAtMa {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Move SL To Entry Bar",Order=11,GroupName="9c. AS-C Short")] public bool AsCShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="9c. AS-C Short")] public int AsCShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="9c. AS-C Short")] public int AsCShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="9c. AS-C Short")] public int AsCShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable Breakeven",Order=15,GroupName="9c. AS-C Short")] public bool AsCShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="9c. AS-C Short")] public int AsCShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="9c. AS-C Short")] public int AsCShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="9c. AS-C Short")] public int AsCShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="9c. AS-C Short")] public bool AsCShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="9c. AS-C Short")] public int AsCShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable WMA Filter",Order=29,GroupName="9c. AS-C Short")] public bool AsCShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="9c. AS-C Short")] public int AsCShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="9c. AS-C Short")] public double AsCShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="9c. AS-C Short")] public int AsCShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Browsable(false)][Display(Name="Enable ADX Filter",Order=35,GroupName="9c. AS-C Short")] public bool AsCShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="9c. AS-C Short")] public int AsCShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Browsable(false)][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="9c. AS-C Short")] public int AsCShortAdxMinLevel {get;set;}
        #endregion
    }

    #region Enums
    public enum MAMode           { SMA, EMA, Both }
    public enum MichalEntryMode  { Market, LimitOffset, LimitRetracement }
    public enum MichalTPMode     { FixedTicks, SwingPoint, CandleMultiple }
    public enum BEMode2          { FixedTicks, CandlePercent }
    public enum WebhookProvider  { TradersPost, ProjectX }
    #endregion
}
