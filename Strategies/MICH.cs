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
    public class MICH : Strategy
    {
        private const string StrategySignalPrefix = "MICH";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";
        private const string HeartbeatStrategyName = "MICH";

        public MICH()
        {
            VendorLicense(1235);
        }
        
        #region Private Variables


        // ── Ny session indicators ──
        private SMA nyLongSmaHigh, nyLongSmaLow;
        private EMA nyLongEmaHigh, nyLongEmaLow;
        private WMA nyLongWmaFilter;
        private ADX nyLongAdxIndicator;
        private Swing nyLongSwing;
        private EMA nyLongEarlyExitEma;
        private ADX nyLongEarlyExitAdx;
        private SMA nyShortSmaHigh, nyShortSmaLow;
        private EMA nyShortEmaHigh, nyShortEmaLow;
        private WMA nyShortWmaFilter;
        private ADX nyShortAdxIndicator;
        private Swing nyShortSwing;
        private EMA nyShortEarlyExitEma;
        private ADX nyShortEarlyExitAdx;

        // ── Eu session indicators ──
        private SMA euLongSmaHigh, euLongSmaLow;
        private EMA euLongEmaHigh, euLongEmaLow;
        private WMA euLongWmaFilter;
        private ADX euLongAdxIndicator;
        private Swing euLongSwing;
        private EMA euLongEarlyExitEma;
        private ADX euLongEarlyExitAdx;
        private SMA euShortSmaHigh, euShortSmaLow;
        private EMA euShortEmaHigh, euShortEmaLow;
        private WMA euShortWmaFilter;
        private ADX euShortAdxIndicator;
        private Swing euShortSwing;
        private EMA euShortEarlyExitEma;
        private ADX euShortEarlyExitAdx;

        // ── As session indicators ──
        private SMA asLongSmaHigh, asLongSmaLow;
        private EMA asLongEmaHigh, asLongEmaLow;
        private WMA asLongWmaFilter;
        private ADX asLongAdxIndicator;
        private Swing asLongSwing;
        private EMA asLongEarlyExitEma;
        private ADX asLongEarlyExitAdx;
        private SMA asShortSmaHigh, asShortSmaLow;
        private EMA asShortEmaHigh, asShortEmaLow;
        private WMA asShortWmaFilter;
        private ADX asShortAdxIndicator;
        private Swing asShortSwing;
        private EMA asShortEarlyExitEma;
        private ADX asShortEarlyExitAdx;

        // ── Ny session state ──
        private int nySessionTradeCount;
        private int nySessionWinCount;
        private int nySessionLossCount;
        private double nySessionPnLTicks;
        private bool nySessionLimitsReached;
        private int nyLastTradeDirection;
        private bool nyLastTradeWasLoss;
        private bool nyWasInNoTradesAfterWindow;
        private bool nyWasInSkipWindow;

        // ── Eu session state ──
        private int euSessionTradeCount;
        private int euSessionWinCount;
        private int euSessionLossCount;
        private double euSessionPnLTicks;
        private bool euSessionLimitsReached;
        private int euLastTradeDirection;
        private bool euLastTradeWasLoss;
        private bool euWasInNoTradesAfterWindow;
        private bool euWasInSkipWindow;

        // ── As session state ──
        private int asSessionTradeCount;
        private int asSessionWinCount;
        private int asSessionLossCount;
        private double asSessionPnLTicks;
        private bool asSessionLimitsReached;
        private int asLastTradeDirection;
        private bool asLastTradeWasLoss;
        private bool asWasInNoTradesAfterWindow;
        private bool asWasInSkipWindow;

        // ── Active session tracking ──
        private int activeSessionId;  // 0 = none, 1 = NY, 2 = EU, 3 = Asia
        private int lastInfoSessionId;
        private DateTime currentSessionDate;

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
        private bool entryBarSlApplied;
        private MarketPosition prevMarketPosition;
        private Order entryOrder;
        private Order stopOrder;
        private Order targetOrder;
        private bool forceFlattenInProgress;
        private bool forceFlattenOrderSubmitted;
        private string forceFlattenReason;
        private double tradePeakAdx;

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
        private int? projectXLastOrderId;
        private string projectXLastOrderContractId;

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                     = @"MICH — NY / EU / Asia in one strategy.";
                Name                            = "MICH";
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
                UseNewsSkip                     = true;
                NewsBlockMinutes                = 1;
                FlattenOnBlockedWindowTransition = true;
                DebugLogging                    = false;
                MaxAccountBalance               = 0.0;
                UseWebhooks                     = false;
                WebhookProviderType             = WebhookProvider.TradersPost;
                WebhookUrl                      = string.Empty;
                WebhookTickerOverride           = string.Empty;
                ProjectXApiBaseUrl              = "https://gateway-api-demo.s2f.projectx.com";
                ProjectXUsername                = string.Empty;
                ProjectXApiKey                  = string.Empty;
                ProjectXAccountId               = string.Empty;
                ProjectXContractId              = string.Empty;


                // ════════════════════════════════════════
                //  NEW YORK SESSION DEFAULTS
                // ════════════════════════════════════════
                NyEnable                                       = true;
                NyTradeWindowStart                                = DateTime.Parse("09:42", System.Globalization.CultureInfo.InvariantCulture);
                NySkipTimeStart                                   = DateTime.Parse("11:45", System.Globalization.CultureInfo.InvariantCulture);
                NySkipTimeEnd                                     = DateTime.Parse("13:05", System.Globalization.CultureInfo.InvariantCulture);
                NyNoNewTradesAfter                                = DateTime.Parse("15:00", System.Globalization.CultureInfo.InvariantCulture);
                NyForcedCloseTime                                 = DateTime.Parse("15:50", System.Globalization.CultureInfo.InvariantCulture);
                NyMaxTradesPerSession                             = 4;
                NyMaxWinsPerSession                               = 0;
                NyMaxLossesPerSession                             = 2;
                NyMaxSessionProfitTicks                           = 1075;
                NyMaxSessionLossTicks                             = 160;
                NyContracts                                       = 1;
                NyEnableLongTrades                                = true;
                NyEnableShortTrades                               = true;
                NyLongMaPeriod                                    = 47;
                NyLongEmaPeriod                                   = 35;
                NyLongMaType                                      = MAMode.Both;
                NyLongEntryMode                                   = MichalEntryMode.Market;
                NyLongLimitOffsetTicks                            = 1;
                NyLongLimitRetracementPct                         = 1.0;
                NyLongTakeProfitMode                              = MichalTPMode.CandleMultiple;
                NyLongTakeProfitTicks                             = 310;
                NyLongMaxTakeProfitTicks                          = 1179;
                NyLongSwingStrength                               = 1;
                NyLongCandleMultiplier                            = 4.3;
                NyLongSlExtraTicks                                = 42;
                NyLongTrailOffsetTicks                            = 41;
                NyLongTrailDelayBars                              = 2;
                NyLongMaxSlTicks                                  = 331;
                NyLongMaxSlToTpRatio                              = 0.47;
                NyLongUsePriorCandleSl                            = true;
                NyLongSlAtMa                                      = false;
                NyLongMoveSlToEntryBar                            = false;
                NyLongTrailCandleOffset                           = 19;
                NyLongEnableBreakeven                             = true;
                NyLongBreakevenMode                               = BEMode2.FixedTicks;
                NyLongBreakevenTriggerTicks                       = 381;
                NyLongBreakevenCandlePct                          = 50.0;
                NyLongBreakevenOffsetTicks                        = 3;
                NyLongMaxBarsInTrade                              = 39;
                NyLongEnableOpposingBarExit                       = false;
                NyLongEnableEngulfingExit                         = false;
                NyLongEnablePriceOffsetTrail                      = true;
                NyLongPriceOffsetReductionTicks                   = 35;
                NyLongRequireDirectionFlip                        = true;
                NyLongAllowSameDirectionAfterLoss                 = true;
                NyLongMaxDistanceFromSmaTicks                     = 0;
                NyLongRequireSmaSlope                             = false;
                NyLongSmaSlopeLookback                            = 3;
                NyLongEnableWmaFilter                             = true;
                NyLongWmaPeriod                                   = 145;
                NyLongMaxSignalCandleTicks                        = 0;
                NyLongMinBodyPct                                  = 60.0;
                NyLongTrendConfirmBars                            = 13;
                NyLongEnableAdxFilter                             = false;
                NyLongAdxPeriod                                   = 11;
                NyLongAdxMinLevel                                 = 15;
                NyLongUseEmaEarlyExit                             = false;
                NyLongEmaEarlyExitPeriod                          = 50;
                NyLongEmaEarlyExitOffsetTicks                     = 0;
                NyLongUseAdxEarlyExit                             = true;
                NyLongAdxEarlyExitPeriod                          = 19;
                NyLongAdxEarlyExitMin                             = 15.0;
                NyLongUseAdxDrawdownExit                          = true;
                NyLongAdxDrawdownFromPeak                         = 15.0;
                NyShortMaPeriod                                   = 43;
                NyShortEmaPeriod                                  = 100;
                NyShortMaType                                     = MAMode.SMA;
                NyShortEntryMode                                  = MichalEntryMode.Market;
                NyShortLimitOffsetTicks                           = 1;
                NyShortLimitRetracementPct                        = 1.0;
                NyShortTakeProfitMode                             = MichalTPMode.CandleMultiple;
                NyShortTakeProfitTicks                            = 310;
                NyShortMaxTakeProfitTicks                         = 1179;
                NyShortSwingStrength                              = 1;
                NyShortCandleMultiplier                           = 4.16;
                NyShortSlExtraTicks                               = 42;
                NyShortTrailOffsetTicks                           = 42;
                NyShortTrailDelayBars                             = 1;
                NyShortMaxSlTicks                                 = 327;
                NyShortMaxSlToTpRatio                             = 0.48;
                NyShortUsePriorCandleSl                           = true;
                NyShortSlAtMa                                     = false;
                NyShortMoveSlToEntryBar                           = false;
                NyShortTrailCandleOffset                          = 19;
                NyShortEnableBreakeven                            = true;
                NyShortBreakevenMode                              = BEMode2.FixedTicks;
                NyShortBreakevenTriggerTicks                      = 357;
                NyShortBreakevenCandlePct                         = 50.0;
                NyShortBreakevenOffsetTicks                       = 3;
                NyShortMaxBarsInTrade                             = 22;
                NyShortEnableOpposingBarExit                      = false;
                NyShortEnableEngulfingExit                        = false;
                NyShortEnablePriceOffsetTrail                     = false;
                NyShortPriceOffsetReductionTicks                  = 0;
                NyShortRequireDirectionFlip                       = true;
                NyShortAllowSameDirectionAfterLoss                = true;
                NyShortMaxDistanceFromSmaTicks                    = 0;
                NyShortRequireSmaSlope                            = false;
                NyShortSmaSlopeLookback                           = 3;
                NyShortEnableWmaFilter                            = true;
                NyShortWmaPeriod                                  = 71;
                NyShortMaxSignalCandleTicks                       = 0;
                NyShortMinBodyPct                                 = 5.0;
                NyShortTrendConfirmBars                           = 13;
                NyShortEnableAdxFilter                            = true;
                NyShortAdxPeriod                                  = 5;
                NyShortAdxMinLevel                                = 20;
                NyShortUseEmaEarlyExit                            = false;
                NyShortEmaEarlyExitPeriod                         = 50;
                NyShortEmaEarlyExitOffsetTicks                    = 0;
                NyShortUseAdxEarlyExit                            = true;
                NyShortAdxEarlyExitPeriod                         = 19;
                NyShortAdxEarlyExitMin                            = 5.0;
                NyShortUseAdxDrawdownExit                         = true;
                NyShortAdxDrawdownFromPeak                        = 9.0;

                // ════════════════════════════════════════
                //  EUROPE SESSION DEFAULTS
                // ════════════════════════════════════════
                EuEnable                                       = true;
                EuTradeWindowStart                                = DateTime.Parse("02:07", System.Globalization.CultureInfo.InvariantCulture);
                EuSkipTimeStart                                   = DateTime.Parse("00:45", System.Globalization.CultureInfo.InvariantCulture);
                EuSkipTimeEnd                                     = DateTime.Parse("00:55", System.Globalization.CultureInfo.InvariantCulture);
                EuNoNewTradesAfter                                = DateTime.Parse("05:35", System.Globalization.CultureInfo.InvariantCulture);
                EuForcedCloseTime                                 = DateTime.Parse("06:40", System.Globalization.CultureInfo.InvariantCulture);
                EuMaxTradesPerSession                             = 2;
                EuMaxWinsPerSession                               = 0;
                EuMaxLossesPerSession                             = 2;
                EuMaxSessionProfitTicks                           = 1075;
                EuMaxSessionLossTicks                             = 160;
                EuContracts                                       = 1;
                EuEnableLongTrades                                = true;
                EuEnableShortTrades                               = true;
                EuLongMaPeriod                                    = 79;
                EuLongEmaPeriod                                   = 43;
                EuLongMaType                                      = MAMode.Both;
                EuLongEntryMode                                   = MichalEntryMode.Market;
                EuLongLimitOffsetTicks                            = 1;
                EuLongLimitRetracementPct                         = 6.0;
                EuLongTakeProfitMode                              = MichalTPMode.CandleMultiple;
                EuLongTakeProfitTicks                             = 180;
                EuLongMaxTakeProfitTicks                          = 890;
                EuLongSwingStrength                               = 1;
                EuLongCandleMultiplier                            = 3.34;
                EuLongSlExtraTicks                                = 42;
                EuLongTrailOffsetTicks                            = 76;
                EuLongTrailDelayBars                              = 0;
                EuLongMaxSlTicks                                  = 306;
                EuLongMaxSlToTpRatio                              = 0.47;
                EuLongUsePriorCandleSl                            = true;
                EuLongSlAtMa                                      = false;
                EuLongMoveSlToEntryBar                            = true;
                EuLongTrailCandleOffset                           = 15;
                EuLongEnableBreakeven                             = true;
                EuLongBreakevenMode                               = BEMode2.FixedTicks;
                EuLongBreakevenTriggerTicks                       = 131;
                EuLongBreakevenCandlePct                          = 50.0;
                EuLongBreakevenOffsetTicks                        = 17;
                EuLongMaxBarsInTrade                              = 25;
                EuLongEnableOpposingBarExit                       = false;
                EuLongEnableEngulfingExit                         = false;
                EuLongEnablePriceOffsetTrail                      = true;
                EuLongPriceOffsetReductionTicks                   = 88;
                EuLongRequireDirectionFlip                        = true;
                EuLongAllowSameDirectionAfterLoss                 = true;
                EuLongMaxDistanceFromSmaTicks                     = 0;
                EuLongRequireSmaSlope                             = false;
                EuLongSmaSlopeLookback                            = 2;
                EuLongEnableWmaFilter                             = true;
                EuLongWmaPeriod                                   = 138;
                EuLongMaxSignalCandleTicks                        = 0;
                EuLongMinBodyPct                                  = 57.0;
                EuLongTrendConfirmBars                            = 14;
                EuLongEnableAdxFilter                             = true;
                EuLongAdxPeriod                                   = 10;
                EuLongAdxMinLevel                                 = 13;
                EuLongUseEmaEarlyExit                             = true;
                EuLongEmaEarlyExitPeriod                          = 82;
                EuLongEmaEarlyExitOffsetTicks                     = 0;
                EuLongUseAdxEarlyExit                             = true;
                EuLongAdxEarlyExitPeriod                          = 20;
                EuLongAdxEarlyExitMin                             = 15.0;
                EuLongUseAdxDrawdownExit                          = true;
                EuLongAdxDrawdownFromPeak                         = 15.0;
                EuShortMaPeriod                                   = 66;
                EuShortEmaPeriod                                  = 92;
                EuShortMaType                                     = MAMode.Both;
                EuShortEntryMode                                  = MichalEntryMode.Market;
                EuShortLimitOffsetTicks                           = 1;
                EuShortLimitRetracementPct                        = 1.0;
                EuShortTakeProfitMode                             = MichalTPMode.CandleMultiple;
                EuShortTakeProfitTicks                            = 310;
                EuShortMaxTakeProfitTicks                         = 1000;
                EuShortSwingStrength                              = 1;
                EuShortCandleMultiplier                           = 4.17;
                EuShortSlExtraTicks                               = 42;
                EuShortTrailOffsetTicks                           = 42;
                EuShortTrailDelayBars                             = 6;
                EuShortMaxSlTicks                                 = 326;
                EuShortMaxSlToTpRatio                             = 0.48;
                EuShortUsePriorCandleSl                           = true;
                EuShortSlAtMa                                     = false;
                EuShortMoveSlToEntryBar                           = false;
                EuShortTrailCandleOffset                          = 8;
                EuShortEnableBreakeven                            = true;
                EuShortBreakevenMode                              = BEMode2.FixedTicks;
                EuShortBreakevenTriggerTicks                      = 339;
                EuShortBreakevenCandlePct                         = 50.0;
                EuShortBreakevenOffsetTicks                       = 27;
                EuShortMaxBarsInTrade                             = 24;
                EuShortEnableOpposingBarExit                      = false;
                EuShortEnableEngulfingExit                        = false;
                EuShortEnablePriceOffsetTrail                     = false;
                EuShortPriceOffsetReductionTicks                  = 0;
                EuShortRequireDirectionFlip                       = true;
                EuShortAllowSameDirectionAfterLoss                = true;
                EuShortMaxDistanceFromSmaTicks                    = 0;
                EuShortRequireSmaSlope                            = false;
                EuShortSmaSlopeLookback                           = 3;
                EuShortEnableWmaFilter                            = true;
                EuShortWmaPeriod                                  = 120;
                EuShortMaxSignalCandleTicks                       = 0;
                EuShortMinBodyPct                                 = 38.0;
                EuShortTrendConfirmBars                           = 13;
                EuShortEnableAdxFilter                            = true;
                EuShortAdxPeriod                                  = 5;
                EuShortAdxMinLevel                                = 24;
                EuShortUseEmaEarlyExit                            = false;
                EuShortEmaEarlyExitPeriod                         = 50;
                EuShortEmaEarlyExitOffsetTicks                    = 0;
                EuShortUseAdxEarlyExit                            = true;
                EuShortAdxEarlyExitPeriod                         = 15;
                EuShortAdxEarlyExitMin                            = 5.0;
                EuShortUseAdxDrawdownExit                         = true;
                EuShortAdxDrawdownFromPeak                        = 7.0;

                // ════════════════════════════════════════
                //  ASIA SESSION DEFAULTS
                // ════════════════════════════════════════
                AsEnable                                       = true;
                AsTradeWindowStart                                = DateTime.Parse("19:00", System.Globalization.CultureInfo.InvariantCulture);
                AsSkipTimeStart                                   = DateTime.Parse("07:45", System.Globalization.CultureInfo.InvariantCulture);
                AsSkipTimeEnd                                     = DateTime.Parse("07:55", System.Globalization.CultureInfo.InvariantCulture);
                AsNoNewTradesAfter                                = DateTime.Parse("23:55", System.Globalization.CultureInfo.InvariantCulture);
                AsForcedCloseTime                                 = DateTime.Parse("01:50", System.Globalization.CultureInfo.InvariantCulture);
                AsMaxTradesPerSession                             = 2;
                AsMaxWinsPerSession                               = 0;
                AsMaxLossesPerSession                             = 1;
                AsMaxSessionProfitTicks                           = 500;
                AsMaxSessionLossTicks                             = 200;
                AsContracts                                       = 1;
                AsEnableLongTrades                                = true;
                AsEnableShortTrades                               = true;
                AsLongMaPeriod                                    = 77;
                AsLongEmaPeriod                                   = 14;
                AsLongMaType                                      = MAMode.Both;
                AsLongEntryMode                                   = MichalEntryMode.Market;
                AsLongLimitOffsetTicks                            = 1;
                AsLongLimitRetracementPct                         = 6.0;
                AsLongTakeProfitMode                              = MichalTPMode.CandleMultiple;
                AsLongTakeProfitTicks                             = 180;
                AsLongMaxTakeProfitTicks                          = 650;
                AsLongSwingStrength                               = 1;
                AsLongCandleMultiplier                            = 2.52;
                AsLongSlExtraTicks                                = 40;
                AsLongTrailOffsetTicks                            = 0;
                AsLongTrailDelayBars                              = 3;
                AsLongMaxSlTicks                                  = 280;
                AsLongMaxSlToTpRatio                              = 0.47;
                AsLongUsePriorCandleSl                            = false;
                AsLongSlAtMa                                      = false;
                AsLongMoveSlToEntryBar                            = true;
                AsLongTrailCandleOffset                           = 15;
                AsLongEnableBreakeven                             = true;
                AsLongBreakevenMode                               = BEMode2.FixedTicks;
                AsLongBreakevenTriggerTicks                       = 141;
                AsLongBreakevenCandlePct                          = 63.0;
                AsLongBreakevenOffsetTicks                        = 30;
                AsLongMaxBarsInTrade                              = 25;
                AsLongEnableOpposingBarExit                       = false;
                AsLongEnableEngulfingExit                         = false;
                AsLongEnablePriceOffsetTrail                      = false;
                AsLongPriceOffsetReductionTicks                   = 88;
                AsLongRequireDirectionFlip                        = true;
                AsLongAllowSameDirectionAfterLoss                 = true;
                AsLongMaxDistanceFromSmaTicks                     = 0;
                AsLongRequireSmaSlope                             = false;
                AsLongSmaSlopeLookback                            = 2;
                AsLongEnableWmaFilter                             = true;
                AsLongWmaPeriod                                   = 55;
                AsLongMaxSignalCandleTicks                        = 0;
                AsLongMinBodyPct                                  = 48.0;
                AsLongTrendConfirmBars                            = 4;
                AsLongEnableAdxFilter                             = true;
                AsLongAdxPeriod                                   = 24;
                AsLongAdxMinLevel                                 = 15;
                AsLongUseEmaEarlyExit                             = true;
                AsLongEmaEarlyExitPeriod                          = 24;
                AsLongEmaEarlyExitOffsetTicks                     = 0;
                AsLongUseAdxEarlyExit                             = true;
                AsLongAdxEarlyExitPeriod                          = 17;
                AsLongAdxEarlyExitMin                             = 19.0;
                AsLongUseAdxDrawdownExit                          = true;
                AsLongAdxDrawdownFromPeak                         = 12.0;
                AsShortMaPeriod                                   = 47;
                AsShortEmaPeriod                                  = 90;
                AsShortMaType                                     = MAMode.Both;
                AsShortEntryMode                                  = MichalEntryMode.Market;
                AsShortLimitOffsetTicks                           = 1;
                AsShortLimitRetracementPct                        = 1.0;
                AsShortTakeProfitMode                             = MichalTPMode.CandleMultiple;
                AsShortTakeProfitTicks                            = 310;
                AsShortMaxTakeProfitTicks                         = 1000;
                AsShortSwingStrength                              = 1;
                AsShortCandleMultiplier                           = 4.06;
                AsShortSlExtraTicks                               = 42;
                AsShortTrailOffsetTicks                           = 14;
                AsShortTrailDelayBars                             = 15;
                AsShortMaxSlTicks                                 = 191;
                AsShortMaxSlToTpRatio                             = 0.48;
                AsShortUsePriorCandleSl                           = true;
                AsShortSlAtMa                                     = true;
                AsShortMoveSlToEntryBar                           = true;
                AsShortTrailCandleOffset                          = 3;
                AsShortEnableBreakeven                            = true;
                AsShortBreakevenMode                              = BEMode2.FixedTicks;
                AsShortBreakevenTriggerTicks                      = 147;
                AsShortBreakevenCandlePct                         = 50.0;
                AsShortBreakevenOffsetTicks                       = 19;
                AsShortMaxBarsInTrade                             = 0;
                AsShortEnableOpposingBarExit                      = false;
                AsShortEnableEngulfingExit                        = false;
                AsShortEnablePriceOffsetTrail                     = true;
                AsShortPriceOffsetReductionTicks                  = 47;
                AsShortRequireDirectionFlip                       = false;
                AsShortAllowSameDirectionAfterLoss                = true;
                AsShortMaxDistanceFromSmaTicks                    = 0;
                AsShortRequireSmaSlope                            = false;
                AsShortSmaSlopeLookback                           = 3;
                AsShortEnableWmaFilter                            = true;
                AsShortWmaPeriod                                  = 20;
                AsShortMaxSignalCandleTicks                       = 0;
                AsShortMinBodyPct                                 = 42.0;
                AsShortTrendConfirmBars                           = 19;
                AsShortEnableAdxFilter                            = true;
                AsShortAdxPeriod                                  = 5;
                AsShortAdxMinLevel                                = 22;
                AsShortUseEmaEarlyExit                            = false;
                AsShortEmaEarlyExitPeriod                         = 50;
                AsShortEmaEarlyExitOffsetTicks                    = 0;
                AsShortUseAdxEarlyExit                            = true;
                AsShortAdxEarlyExitPeriod                         = 14;
                AsShortAdxEarlyExitMin                            = 12.0;
                AsShortUseAdxDrawdownExit                         = true;
                AsShortAdxDrawdownFromPeak                        = 7.0;
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                if (NyEnable)
                {
                // ── Ny indicators ──
                nyLongSmaHigh      = SMA(High,  NyLongMaPeriod);
                nyLongSmaLow       = SMA(Low,   NyLongMaPeriod);
                nyLongEmaHigh      = EMA(High,  NyLongEmaPeriod);
                nyLongEmaLow       = EMA(Low,   NyLongEmaPeriod);
                nyLongWmaFilter    = WMA(Close, NyLongWmaPeriod);
                nyLongAdxIndicator = ADX(NyLongAdxPeriod);
                nyLongSwing        = Swing(NyLongSwingStrength);
                nyLongEarlyExitEma = EMA(NyLongEmaEarlyExitPeriod);
                nyLongEarlyExitAdx = ADX(NyLongAdxEarlyExitPeriod);
                nyShortSmaHigh      = SMA(High,  NyShortMaPeriod);
                nyShortSmaLow       = SMA(Low,   NyShortMaPeriod);
                nyShortEmaHigh      = EMA(High,  NyShortEmaPeriod);
                nyShortEmaLow       = EMA(Low,   NyShortEmaPeriod);
                nyShortWmaFilter    = WMA(Close, NyShortWmaPeriod);
                nyShortAdxIndicator = ADX(NyShortAdxPeriod);
                nyShortSwing        = Swing(NyShortSwingStrength);
                nyShortEarlyExitEma = EMA(NyShortEmaEarlyExitPeriod);
                nyShortEarlyExitAdx = ADX(NyShortAdxEarlyExitPeriod);
                }

                if (EuEnable)
                {
                // ── Eu indicators ──
                euLongSmaHigh      = SMA(High,  EuLongMaPeriod);
                euLongSmaLow       = SMA(Low,   EuLongMaPeriod);
                euLongEmaHigh      = EMA(High,  EuLongEmaPeriod);
                euLongEmaLow       = EMA(Low,   EuLongEmaPeriod);
                euLongWmaFilter    = WMA(Close, EuLongWmaPeriod);
                euLongAdxIndicator = ADX(EuLongAdxPeriod);
                euLongSwing        = Swing(EuLongSwingStrength);
                euLongEarlyExitEma = EMA(EuLongEmaEarlyExitPeriod);
                euLongEarlyExitAdx = ADX(EuLongAdxEarlyExitPeriod);
                euShortSmaHigh      = SMA(High,  EuShortMaPeriod);
                euShortSmaLow       = SMA(Low,   EuShortMaPeriod);
                euShortEmaHigh      = EMA(High,  EuShortEmaPeriod);
                euShortEmaLow       = EMA(Low,   EuShortEmaPeriod);
                euShortWmaFilter    = WMA(Close, EuShortWmaPeriod);
                euShortAdxIndicator = ADX(EuShortAdxPeriod);
                euShortSwing        = Swing(EuShortSwingStrength);
                euShortEarlyExitEma = EMA(EuShortEmaEarlyExitPeriod);
                euShortEarlyExitAdx = ADX(EuShortAdxEarlyExitPeriod);
                }

                if (AsEnable)
                {
                // ── As indicators ──
                asLongSmaHigh      = SMA(High,  AsLongMaPeriod);
                asLongSmaLow       = SMA(Low,   AsLongMaPeriod);
                asLongEmaHigh      = EMA(High,  AsLongEmaPeriod);
                asLongEmaLow       = EMA(Low,   AsLongEmaPeriod);
                asLongWmaFilter    = WMA(Close, AsLongWmaPeriod);
                asLongAdxIndicator = ADX(AsLongAdxPeriod);
                asLongSwing        = Swing(AsLongSwingStrength);
                asLongEarlyExitEma = EMA(AsLongEmaEarlyExitPeriod);
                asLongEarlyExitAdx = ADX(AsLongAdxEarlyExitPeriod);
                asShortSmaHigh      = SMA(High,  AsShortMaPeriod);
                asShortSmaLow       = SMA(Low,   AsShortMaPeriod);
                asShortEmaHigh      = EMA(High,  AsShortEmaPeriod);
                asShortEmaLow       = EMA(Low,   AsShortEmaPeriod);
                asShortWmaFilter    = WMA(Close, AsShortWmaPeriod);
                asShortAdxIndicator = ADX(AsShortAdxPeriod);
                asShortSwing        = Swing(AsShortSwingStrength);
                asShortEarlyExitEma = EMA(AsShortEmaEarlyExitPeriod);
                asShortEarlyExitAdx = ADX(AsShortAdxEarlyExitPeriod);
                }

                // Chart display — NY Long MAs (primary session)
                if (NyEnable)
                {
                    if (NyLongMaType == MAMode.SMA || NyLongMaType == MAMode.Both)
                    {
                        nyLongSmaHigh.Plots[0].Brush = Brushes.RoyalBlue;
                        nyLongSmaLow.Plots[0].Brush  = Brushes.MediumOrchid;
                    }
                    if (NyLongMaType == MAMode.EMA || NyLongMaType == MAMode.Both)
                    {
                        nyLongEmaHigh.Plots[0].Brush = Brushes.DodgerBlue;
                        nyLongEmaLow.Plots[0].Brush  = Brushes.Orchid;
                    }
                    if (NyLongEnableWmaFilter)
                    {
                        nyLongWmaFilter.Plots[0].Brush = Brushes.Gold;
                    }
                }

                EnsureNewsDatesInitialized();
                ValidateRequiredPrimaryTimeframe(5);
                ValidateRequiredPrimaryInstrument();
                heartbeatReporter = new StrategyHeartbeatReporter(
                    HeartbeatStrategyName,
                    System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv"));
            }
            else if (State == State.Transition)
            {
                ResetAll();
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
                projectXSessionToken = null;
                projectXLastOrderId = null;
                projectXLastOrderContractId = null;
                DisposeInfoBoxOverlay();
            }
        }


        // ════════════════════════════════════════════════════════
        //  SESSION DISPATCH HELPERS
        // ════════════════════════════════════════════════════════

        // -- Indicator access by session ID --
        private SMA S_LongSmaHigh(int sid) { return sid == 1 ? nyLongSmaHigh : sid == 2 ? euLongSmaHigh : asLongSmaHigh; }
        private SMA S_LongSmaLow(int sid)  { return sid == 1 ? nyLongSmaLow  : sid == 2 ? euLongSmaLow  : asLongSmaLow;  }
        private EMA S_LongEmaHigh(int sid) { return sid == 1 ? nyLongEmaHigh : sid == 2 ? euLongEmaHigh : asLongEmaHigh; }
        private EMA S_LongEmaLow(int sid)  { return sid == 1 ? nyLongEmaLow  : sid == 2 ? euLongEmaLow  : asLongEmaLow;  }
        private WMA S_LongWmaFilter(int sid) { return sid == 1 ? nyLongWmaFilter : sid == 2 ? euLongWmaFilter : asLongWmaFilter; }
        private ADX S_LongAdxIndicator(int sid) { return sid == 1 ? nyLongAdxIndicator : sid == 2 ? euLongAdxIndicator : asLongAdxIndicator; }
        private Swing S_LongSwing(int sid) { return sid == 1 ? nyLongSwing : sid == 2 ? euLongSwing : asLongSwing; }
        private EMA S_LongEarlyExitEma(int sid) { return sid == 1 ? nyLongEarlyExitEma : sid == 2 ? euLongEarlyExitEma : asLongEarlyExitEma; }
        private ADX S_LongEarlyExitAdx(int sid) { return sid == 1 ? nyLongEarlyExitAdx : sid == 2 ? euLongEarlyExitAdx : asLongEarlyExitAdx; }
        private SMA S_ShortSmaHigh(int sid) { return sid == 1 ? nyShortSmaHigh : sid == 2 ? euShortSmaHigh : asShortSmaHigh; }
        private SMA S_ShortSmaLow(int sid)  { return sid == 1 ? nyShortSmaLow  : sid == 2 ? euShortSmaLow  : asShortSmaLow;  }
        private EMA S_ShortEmaHigh(int sid) { return sid == 1 ? nyShortEmaHigh : sid == 2 ? euShortEmaHigh : asShortEmaHigh; }
        private EMA S_ShortEmaLow(int sid)  { return sid == 1 ? nyShortEmaLow  : sid == 2 ? euShortEmaLow  : asShortEmaLow;  }
        private WMA S_ShortWmaFilter(int sid) { return sid == 1 ? nyShortWmaFilter : sid == 2 ? euShortWmaFilter : asShortWmaFilter; }
        private ADX S_ShortAdxIndicator(int sid) { return sid == 1 ? nyShortAdxIndicator : sid == 2 ? euShortAdxIndicator : asShortAdxIndicator; }
        private Swing S_ShortSwing(int sid) { return sid == 1 ? nyShortSwing : sid == 2 ? euShortSwing : asShortSwing; }
        private EMA S_ShortEarlyExitEma(int sid) { return sid == 1 ? nyShortEarlyExitEma : sid == 2 ? euShortEarlyExitEma : asShortEarlyExitEma; }
        private ADX S_ShortEarlyExitAdx(int sid) { return sid == 1 ? nyShortEarlyExitAdx : sid == 2 ? euShortEarlyExitAdx : asShortEarlyExitAdx; }

        // -- Session-level parameter dispatch --
        private int S_Contracts(int sid) { return sid == 1 ? NyContracts : sid == 2 ? EuContracts : AsContracts; }
        private bool S_EnableLongTrades(int sid) { return sid == 1 ? NyEnableLongTrades : sid == 2 ? EuEnableLongTrades : AsEnableLongTrades; }
        private bool S_EnableShortTrades(int sid) { return sid == 1 ? NyEnableShortTrades : sid == 2 ? EuEnableShortTrades : AsEnableShortTrades; }
        private int S_MaxTradesPerSession(int sid) { return sid == 1 ? NyMaxTradesPerSession : sid == 2 ? EuMaxTradesPerSession : AsMaxTradesPerSession; }
        private int S_MaxWinsPerSession(int sid) { return sid == 1 ? NyMaxWinsPerSession : sid == 2 ? EuMaxWinsPerSession : AsMaxWinsPerSession; }
        private int S_MaxLossesPerSession(int sid) { return sid == 1 ? NyMaxLossesPerSession : sid == 2 ? EuMaxLossesPerSession : AsMaxLossesPerSession; }
        private int S_MaxSessionProfitTicks(int sid) { return sid == 1 ? NyMaxSessionProfitTicks : sid == 2 ? EuMaxSessionProfitTicks : AsMaxSessionProfitTicks; }
        private int S_MaxSessionLossTicks(int sid) { return sid == 1 ? NyMaxSessionLossTicks : sid == 2 ? EuMaxSessionLossTicks : AsMaxSessionLossTicks; }
        private DateTime S_TradeWindowStart(int sid) { return sid == 1 ? NyTradeWindowStart : sid == 2 ? EuTradeWindowStart : AsTradeWindowStart; }
        private DateTime S_SkipTimeStart(int sid) { return sid == 1 ? NySkipTimeStart : sid == 2 ? EuSkipTimeStart : AsSkipTimeStart; }
        private DateTime S_SkipTimeEnd(int sid) { return sid == 1 ? NySkipTimeEnd : sid == 2 ? EuSkipTimeEnd : AsSkipTimeEnd; }
        private DateTime S_NoNewTradesAfter(int sid) { return sid == 1 ? NyNoNewTradesAfter : sid == 2 ? EuNoNewTradesAfter : AsNoNewTradesAfter; }
        private DateTime S_ForcedCloseTime(int sid) { return sid == 1 ? NyForcedCloseTime : sid == 2 ? EuForcedCloseTime : AsForcedCloseTime; }

        // -- Direction+Session parameter dispatch (sd = session+direction) --
        private int SD_MaPeriod(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongMaPeriod : sid == 2 ? EuLongMaPeriod : AsLongMaPeriod) : (sid == 1 ? NyShortMaPeriod : sid == 2 ? EuShortMaPeriod : AsShortMaPeriod); }
        private int SD_EmaPeriod(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongEmaPeriod : sid == 2 ? EuLongEmaPeriod : AsLongEmaPeriod) : (sid == 1 ? NyShortEmaPeriod : sid == 2 ? EuShortEmaPeriod : AsShortEmaPeriod); }
        private MAMode SD_MaType(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongMaType : sid == 2 ? EuLongMaType : AsLongMaType) : (sid == 1 ? NyShortMaType : sid == 2 ? EuShortMaType : AsShortMaType); }
        private MichalEntryMode SD_EntryMode(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongEntryMode : sid == 2 ? EuLongEntryMode : AsLongEntryMode) : (sid == 1 ? NyShortEntryMode : sid == 2 ? EuShortEntryMode : AsShortEntryMode); }
        private int SD_LimitOffsetTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongLimitOffsetTicks : sid == 2 ? EuLongLimitOffsetTicks : AsLongLimitOffsetTicks) : (sid == 1 ? NyShortLimitOffsetTicks : sid == 2 ? EuShortLimitOffsetTicks : AsShortLimitOffsetTicks); }
        private double SD_LimitRetracementPct(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongLimitRetracementPct : sid == 2 ? EuLongLimitRetracementPct : AsLongLimitRetracementPct) : (sid == 1 ? NyShortLimitRetracementPct : sid == 2 ? EuShortLimitRetracementPct : AsShortLimitRetracementPct); }
        private MichalTPMode SD_TakeProfitMode(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongTakeProfitMode : sid == 2 ? EuLongTakeProfitMode : AsLongTakeProfitMode) : (sid == 1 ? NyShortTakeProfitMode : sid == 2 ? EuShortTakeProfitMode : AsShortTakeProfitMode); }
        private int SD_TakeProfitTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongTakeProfitTicks : sid == 2 ? EuLongTakeProfitTicks : AsLongTakeProfitTicks) : (sid == 1 ? NyShortTakeProfitTicks : sid == 2 ? EuShortTakeProfitTicks : AsShortTakeProfitTicks); }
        private int SD_MaxTakeProfitTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongMaxTakeProfitTicks : sid == 2 ? EuLongMaxTakeProfitTicks : AsLongMaxTakeProfitTicks) : (sid == 1 ? NyShortMaxTakeProfitTicks : sid == 2 ? EuShortMaxTakeProfitTicks : AsShortMaxTakeProfitTicks); }
        private int SD_SwingStrength(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongSwingStrength : sid == 2 ? EuLongSwingStrength : AsLongSwingStrength) : (sid == 1 ? NyShortSwingStrength : sid == 2 ? EuShortSwingStrength : AsShortSwingStrength); }
        private double SD_CandleMultiplier(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongCandleMultiplier : sid == 2 ? EuLongCandleMultiplier : AsLongCandleMultiplier) : (sid == 1 ? NyShortCandleMultiplier : sid == 2 ? EuShortCandleMultiplier : AsShortCandleMultiplier); }
        private int SD_SlExtraTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongSlExtraTicks : sid == 2 ? EuLongSlExtraTicks : AsLongSlExtraTicks) : (sid == 1 ? NyShortSlExtraTicks : sid == 2 ? EuShortSlExtraTicks : AsShortSlExtraTicks); }
        private int SD_TrailOffsetTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongTrailOffsetTicks : sid == 2 ? EuLongTrailOffsetTicks : AsLongTrailOffsetTicks) : (sid == 1 ? NyShortTrailOffsetTicks : sid == 2 ? EuShortTrailOffsetTicks : AsShortTrailOffsetTicks); }
        private int SD_TrailDelayBars(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongTrailDelayBars : sid == 2 ? EuLongTrailDelayBars : AsLongTrailDelayBars) : (sid == 1 ? NyShortTrailDelayBars : sid == 2 ? EuShortTrailDelayBars : AsShortTrailDelayBars); }
        private int SD_MaxSlTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongMaxSlTicks : sid == 2 ? EuLongMaxSlTicks : AsLongMaxSlTicks) : (sid == 1 ? NyShortMaxSlTicks : sid == 2 ? EuShortMaxSlTicks : AsShortMaxSlTicks); }
        private double SD_MaxSlToTpRatio(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongMaxSlToTpRatio : sid == 2 ? EuLongMaxSlToTpRatio : AsLongMaxSlToTpRatio) : (sid == 1 ? NyShortMaxSlToTpRatio : sid == 2 ? EuShortMaxSlToTpRatio : AsShortMaxSlToTpRatio); }
        private bool SD_UsePriorCandleSl(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongUsePriorCandleSl : sid == 2 ? EuLongUsePriorCandleSl : AsLongUsePriorCandleSl) : (sid == 1 ? NyShortUsePriorCandleSl : sid == 2 ? EuShortUsePriorCandleSl : AsShortUsePriorCandleSl); }
        private bool SD_SlAtMa(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongSlAtMa : sid == 2 ? EuLongSlAtMa : AsLongSlAtMa) : (sid == 1 ? NyShortSlAtMa : sid == 2 ? EuShortSlAtMa : AsShortSlAtMa); }
        private bool SD_MoveSlToEntryBar(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongMoveSlToEntryBar : sid == 2 ? EuLongMoveSlToEntryBar : AsLongMoveSlToEntryBar) : (sid == 1 ? NyShortMoveSlToEntryBar : sid == 2 ? EuShortMoveSlToEntryBar : AsShortMoveSlToEntryBar); }
        private int SD_TrailCandleOffset(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongTrailCandleOffset : sid == 2 ? EuLongTrailCandleOffset : AsLongTrailCandleOffset) : (sid == 1 ? NyShortTrailCandleOffset : sid == 2 ? EuShortTrailCandleOffset : AsShortTrailCandleOffset); }
        private bool SD_EnableBreakeven(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongEnableBreakeven : sid == 2 ? EuLongEnableBreakeven : AsLongEnableBreakeven) : (sid == 1 ? NyShortEnableBreakeven : sid == 2 ? EuShortEnableBreakeven : AsShortEnableBreakeven); }
        private BEMode2 SD_BreakevenMode(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongBreakevenMode : sid == 2 ? EuLongBreakevenMode : AsLongBreakevenMode) : (sid == 1 ? NyShortBreakevenMode : sid == 2 ? EuShortBreakevenMode : AsShortBreakevenMode); }
        private int SD_BreakevenTriggerTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongBreakevenTriggerTicks : sid == 2 ? EuLongBreakevenTriggerTicks : AsLongBreakevenTriggerTicks) : (sid == 1 ? NyShortBreakevenTriggerTicks : sid == 2 ? EuShortBreakevenTriggerTicks : AsShortBreakevenTriggerTicks); }
        private double SD_BreakevenCandlePct(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongBreakevenCandlePct : sid == 2 ? EuLongBreakevenCandlePct : AsLongBreakevenCandlePct) : (sid == 1 ? NyShortBreakevenCandlePct : sid == 2 ? EuShortBreakevenCandlePct : AsShortBreakevenCandlePct); }
        private int SD_BreakevenOffsetTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongBreakevenOffsetTicks : sid == 2 ? EuLongBreakevenOffsetTicks : AsLongBreakevenOffsetTicks) : (sid == 1 ? NyShortBreakevenOffsetTicks : sid == 2 ? EuShortBreakevenOffsetTicks : AsShortBreakevenOffsetTicks); }
        private int SD_MaxBarsInTrade(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongMaxBarsInTrade : sid == 2 ? EuLongMaxBarsInTrade : AsLongMaxBarsInTrade) : (sid == 1 ? NyShortMaxBarsInTrade : sid == 2 ? EuShortMaxBarsInTrade : AsShortMaxBarsInTrade); }
        private bool SD_EnableOpposingBarExit(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongEnableOpposingBarExit : sid == 2 ? EuLongEnableOpposingBarExit : AsLongEnableOpposingBarExit) : (sid == 1 ? NyShortEnableOpposingBarExit : sid == 2 ? EuShortEnableOpposingBarExit : AsShortEnableOpposingBarExit); }
        private bool SD_EnableEngulfingExit(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongEnableEngulfingExit : sid == 2 ? EuLongEnableEngulfingExit : AsLongEnableEngulfingExit) : (sid == 1 ? NyShortEnableEngulfingExit : sid == 2 ? EuShortEnableEngulfingExit : AsShortEnableEngulfingExit); }
        private bool SD_EnablePriceOffsetTrail(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongEnablePriceOffsetTrail : sid == 2 ? EuLongEnablePriceOffsetTrail : AsLongEnablePriceOffsetTrail) : (sid == 1 ? NyShortEnablePriceOffsetTrail : sid == 2 ? EuShortEnablePriceOffsetTrail : AsShortEnablePriceOffsetTrail); }
        private int SD_PriceOffsetReductionTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongPriceOffsetReductionTicks : sid == 2 ? EuLongPriceOffsetReductionTicks : AsLongPriceOffsetReductionTicks) : (sid == 1 ? NyShortPriceOffsetReductionTicks : sid == 2 ? EuShortPriceOffsetReductionTicks : AsShortPriceOffsetReductionTicks); }
        private bool SD_RequireDirectionFlip(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongRequireDirectionFlip : sid == 2 ? EuLongRequireDirectionFlip : AsLongRequireDirectionFlip) : (sid == 1 ? NyShortRequireDirectionFlip : sid == 2 ? EuShortRequireDirectionFlip : AsShortRequireDirectionFlip); }
        private bool SD_AllowSameDirectionAfterLoss(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongAllowSameDirectionAfterLoss : sid == 2 ? EuLongAllowSameDirectionAfterLoss : AsLongAllowSameDirectionAfterLoss) : (sid == 1 ? NyShortAllowSameDirectionAfterLoss : sid == 2 ? EuShortAllowSameDirectionAfterLoss : AsShortAllowSameDirectionAfterLoss); }
        private int SD_MaxDistanceFromSmaTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongMaxDistanceFromSmaTicks : sid == 2 ? EuLongMaxDistanceFromSmaTicks : AsLongMaxDistanceFromSmaTicks) : (sid == 1 ? NyShortMaxDistanceFromSmaTicks : sid == 2 ? EuShortMaxDistanceFromSmaTicks : AsShortMaxDistanceFromSmaTicks); }
        private bool SD_RequireSmaSlope(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongRequireSmaSlope : sid == 2 ? EuLongRequireSmaSlope : AsLongRequireSmaSlope) : (sid == 1 ? NyShortRequireSmaSlope : sid == 2 ? EuShortRequireSmaSlope : AsShortRequireSmaSlope); }
        private int SD_SmaSlopeLookback(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongSmaSlopeLookback : sid == 2 ? EuLongSmaSlopeLookback : AsLongSmaSlopeLookback) : (sid == 1 ? NyShortSmaSlopeLookback : sid == 2 ? EuShortSmaSlopeLookback : AsShortSmaSlopeLookback); }
        private bool SD_EnableWmaFilter(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongEnableWmaFilter : sid == 2 ? EuLongEnableWmaFilter : AsLongEnableWmaFilter) : (sid == 1 ? NyShortEnableWmaFilter : sid == 2 ? EuShortEnableWmaFilter : AsShortEnableWmaFilter); }
        private int SD_WmaPeriod(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongWmaPeriod : sid == 2 ? EuLongWmaPeriod : AsLongWmaPeriod) : (sid == 1 ? NyShortWmaPeriod : sid == 2 ? EuShortWmaPeriod : AsShortWmaPeriod); }
        private int SD_MaxSignalCandleTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongMaxSignalCandleTicks : sid == 2 ? EuLongMaxSignalCandleTicks : AsLongMaxSignalCandleTicks) : (sid == 1 ? NyShortMaxSignalCandleTicks : sid == 2 ? EuShortMaxSignalCandleTicks : AsShortMaxSignalCandleTicks); }
        private double SD_MinBodyPct(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongMinBodyPct : sid == 2 ? EuLongMinBodyPct : AsLongMinBodyPct) : (sid == 1 ? NyShortMinBodyPct : sid == 2 ? EuShortMinBodyPct : AsShortMinBodyPct); }
        private int SD_TrendConfirmBars(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongTrendConfirmBars : sid == 2 ? EuLongTrendConfirmBars : AsLongTrendConfirmBars) : (sid == 1 ? NyShortTrendConfirmBars : sid == 2 ? EuShortTrendConfirmBars : AsShortTrendConfirmBars); }
        private bool SD_EnableAdxFilter(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongEnableAdxFilter : sid == 2 ? EuLongEnableAdxFilter : AsLongEnableAdxFilter) : (sid == 1 ? NyShortEnableAdxFilter : sid == 2 ? EuShortEnableAdxFilter : AsShortEnableAdxFilter); }
        private int SD_AdxPeriod(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongAdxPeriod : sid == 2 ? EuLongAdxPeriod : AsLongAdxPeriod) : (sid == 1 ? NyShortAdxPeriod : sid == 2 ? EuShortAdxPeriod : AsShortAdxPeriod); }
        private int SD_AdxMinLevel(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongAdxMinLevel : sid == 2 ? EuLongAdxMinLevel : AsLongAdxMinLevel) : (sid == 1 ? NyShortAdxMinLevel : sid == 2 ? EuShortAdxMinLevel : AsShortAdxMinLevel); }
        private bool SD_UseEmaEarlyExit(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongUseEmaEarlyExit : sid == 2 ? EuLongUseEmaEarlyExit : AsLongUseEmaEarlyExit) : (sid == 1 ? NyShortUseEmaEarlyExit : sid == 2 ? EuShortUseEmaEarlyExit : AsShortUseEmaEarlyExit); }
        private int SD_EmaEarlyExitPeriod(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongEmaEarlyExitPeriod : sid == 2 ? EuLongEmaEarlyExitPeriod : AsLongEmaEarlyExitPeriod) : (sid == 1 ? NyShortEmaEarlyExitPeriod : sid == 2 ? EuShortEmaEarlyExitPeriod : AsShortEmaEarlyExitPeriod); }
        private int SD_EmaEarlyExitOffsetTicks(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongEmaEarlyExitOffsetTicks : sid == 2 ? EuLongEmaEarlyExitOffsetTicks : AsLongEmaEarlyExitOffsetTicks) : (sid == 1 ? NyShortEmaEarlyExitOffsetTicks : sid == 2 ? EuShortEmaEarlyExitOffsetTicks : AsShortEmaEarlyExitOffsetTicks); }
        private bool SD_UseAdxEarlyExit(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongUseAdxEarlyExit : sid == 2 ? EuLongUseAdxEarlyExit : AsLongUseAdxEarlyExit) : (sid == 1 ? NyShortUseAdxEarlyExit : sid == 2 ? EuShortUseAdxEarlyExit : AsShortUseAdxEarlyExit); }
        private int SD_AdxEarlyExitPeriod(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongAdxEarlyExitPeriod : sid == 2 ? EuLongAdxEarlyExitPeriod : AsLongAdxEarlyExitPeriod) : (sid == 1 ? NyShortAdxEarlyExitPeriod : sid == 2 ? EuShortAdxEarlyExitPeriod : AsShortAdxEarlyExitPeriod); }
        private double SD_AdxEarlyExitMin(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongAdxEarlyExitMin : sid == 2 ? EuLongAdxEarlyExitMin : AsLongAdxEarlyExitMin) : (sid == 1 ? NyShortAdxEarlyExitMin : sid == 2 ? EuShortAdxEarlyExitMin : AsShortAdxEarlyExitMin); }
        private bool SD_UseAdxDrawdownExit(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongUseAdxDrawdownExit : sid == 2 ? EuLongUseAdxDrawdownExit : AsLongUseAdxDrawdownExit) : (sid == 1 ? NyShortUseAdxDrawdownExit : sid == 2 ? EuShortUseAdxDrawdownExit : AsShortUseAdxDrawdownExit); }
        private double SD_AdxDrawdownFromPeak(int sid, int dir) { return dir == 1 ? (sid == 1 ? NyLongAdxDrawdownFromPeak : sid == 2 ? EuLongAdxDrawdownFromPeak : AsLongAdxDrawdownFromPeak) : (sid == 1 ? NyShortAdxDrawdownFromPeak : sid == 2 ? EuShortAdxDrawdownFromPeak : AsShortAdxDrawdownFromPeak); }

        // -- Session state access/mutators --
        private int S_GetTradeCount(int sid) { return sid == 1 ? nySessionTradeCount : sid == 2 ? euSessionTradeCount : asSessionTradeCount; }
        private int S_GetWinCount(int sid) { return sid == 1 ? nySessionWinCount : sid == 2 ? euSessionWinCount : asSessionWinCount; }
        private int S_GetLossCount(int sid) { return sid == 1 ? nySessionLossCount : sid == 2 ? euSessionLossCount : asSessionLossCount; }
        private double S_GetPnLTicks(int sid) { return sid == 1 ? nySessionPnLTicks : sid == 2 ? euSessionPnLTicks : asSessionPnLTicks; }
        private bool S_GetLimitsReached(int sid) { return sid == 1 ? nySessionLimitsReached : sid == 2 ? euSessionLimitsReached : asSessionLimitsReached; }
        private int S_GetLastTradeDirection(int sid) { return sid == 1 ? nyLastTradeDirection : sid == 2 ? euLastTradeDirection : asLastTradeDirection; }
        private bool S_GetLastTradeWasLoss(int sid) { return sid == 1 ? nyLastTradeWasLoss : sid == 2 ? euLastTradeWasLoss : asLastTradeWasLoss; }

        private void S_SetLimitsReached(int sid, bool val) { if (sid == 1) nySessionLimitsReached = val; else if (sid == 2) euSessionLimitsReached = val; else asSessionLimitsReached = val; }
        private void S_AddTradeResult(int sid, double pnlTicks)
        {
            if (sid == 1) { nySessionTradeCount++; nySessionPnLTicks += pnlTicks; if (pnlTicks > 0) nySessionWinCount++; else nySessionLossCount++; nyLastTradeDirection = tradeDirection; nyLastTradeWasLoss = pnlTicks <= 0; }
            else if (sid == 2) { euSessionTradeCount++; euSessionPnLTicks += pnlTicks; if (pnlTicks > 0) euSessionWinCount++; else euSessionLossCount++; euLastTradeDirection = tradeDirection; euLastTradeWasLoss = pnlTicks <= 0; }
            else { asSessionTradeCount++; asSessionPnLTicks += pnlTicks; if (pnlTicks > 0) asSessionWinCount++; else asSessionLossCount++; asLastTradeDirection = tradeDirection; asLastTradeWasLoss = pnlTicks <= 0; }
        }

        private string S_Label(int sid) { return sid == 1 ? "NY" : sid == 2 ? "EU" : "AS"; }


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

        /// <summary>Determine which session (if any) is eligible for entries at the given bar time.</summary>
        private int DetermineEntrySession()
        {
            // Check each enabled session. Sessions don't overlap so at most one matches.
            if (NyEnable && !nySessionLimitsReached && IsInSessionTradeWindow(1)) return 1;
            if (EuEnable && !euSessionLimitsReached && IsInSessionTradeWindow(2)) return 2;
            if (AsEnable && !asSessionLimitsReached && IsInSessionTradeWindow(3)) return 3;
            return 0;
        }

        private bool IsInSessionTradeWindow(int sid)
        {
            double barSM       = ToSessionMinutes(Time[0].TimeOfDay);
            double startSM     = ToSessionMinutes(S_TradeWindowStart(sid).TimeOfDay);
            double endSM       = ToSessionMinutes(S_NoNewTradesAfter(sid).TimeOfDay);
            double skipStartSM = ToSessionMinutes(S_SkipTimeStart(sid).TimeOfDay);
            double skipEndSM   = ToSessionMinutes(S_SkipTimeEnd(sid).TimeOfDay);

            if (barSM < startSM || barSM >= endSM)  return false;
            bool skipConfigured = S_SkipTimeStart(sid).TimeOfDay != S_SkipTimeEnd(sid).TimeOfDay;
            if (skipConfigured && barSM >= skipStartSM && barSM < skipEndSM) return false;
            if (IsInNewsSkipWindow(Time[0])) return false;
            return true;
        }

        private bool IsInAnyForcedCloseWindow()
        {
            double barSM = ToSessionMinutes(Time[0].TimeOfDay);
            if (NyEnable && barSM >= ToSessionMinutes(NyForcedCloseTime.TimeOfDay) && barSM < ToSessionMinutes(NyForcedCloseTime.TimeOfDay) + 10) return true;
            if (EuEnable && barSM >= ToSessionMinutes(EuForcedCloseTime.TimeOfDay) && barSM < ToSessionMinutes(EuForcedCloseTime.TimeOfDay) + 10) return true;
            if (AsEnable && barSM >= ToSessionMinutes(AsForcedCloseTime.TimeOfDay) && barSM < ToSessionMinutes(AsForcedCloseTime.TimeOfDay) + 10) return true;
            return false;
        }

        private bool IsInSessionForcedClose(int sid)
        {
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
            for (int sid = 1; sid <= 3; sid++)
            {
                bool enabled = sid == 1 ? NyEnable : sid == 2 ? EuEnable : AsEnable;
                if (!enabled) continue;
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
            entryBarSlApplied           = false;
            entryOrder                  = null;
            stopOrder                   = null;
            targetOrder                 = null;
            forceFlattenInProgress      = false;
            forceFlattenOrderSubmitted  = false;
            forceFlattenReason          = null;
            tradePeakAdx                = 0.0;

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

                int contracts = S_Contracts(sid);
                string entryName = GetEntrySignalName(tradeDirection);
                double tpPrice = CalculateTakeProfit(tradeEntryPrice);
                double slPrice = CalculateStopLoss();

                if (tradeDirection == 1)
                {
                    if (tpPrice <= tradeEntryPrice) tpPrice = tradeEntryPrice + SD_TakeProfitTicks(sid, 1) * TickSize;
                    if (slPrice >= tradeEntryPrice) slPrice = tradeEntryPrice - 4 * TickSize;
                    targetOrder = ExitLongLimit(0, true, contracts, tpPrice, BuildExitSignalName("TP"), entryName);
                    stopOrder = ExitLongStopMarket(0, true, contracts, slPrice, BuildExitSignalName("SL"), entryName);
                }
                else
                {
                    if (tpPrice >= tradeEntryPrice) tpPrice = tradeEntryPrice - SD_TakeProfitTicks(sid, -1) * TickSize;
                    if (slPrice <= tradeEntryPrice) slPrice = tradeEntryPrice + 4 * TickSize;
                    targetOrder = ExitShortLimit(0, true, contracts, tpPrice, BuildExitSignalName("TP"), entryName);
                    stopOrder = ExitShortStopMarket(0, true, contracts, slPrice, BuildExitSignalName("SL"), entryName);
                }

                originalStopPrice = slPrice;
                string entryAction;
                bool isMarketEntry;
                if (TryGetEntryWebhookAction(execution, out entryAction, out isMarketEntry))
                    SendWebhook(entryAction, tradeEntryPrice, tpPrice, slPrice, isMarketEntry, executionQty);
                Print(string.Format("{0} | [{1}] FILLED {2} @ {3:F2} | TP={4:F2} | SL={5:F2}", time, S_Label(sid), tradeDirection == 1 ? "LONG" : "SHORT", tradeEntryPrice, tpPrice, slPrice));
            }

            if (ShouldSendExitWebhook(execution, orderName, marketPosition))
                SendWebhook("exit", 0, 0, 0, true, executionQty);
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

            if (entryOrder != null && order == entryOrder
                && (orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
            {
                SendWebhook("cancel");
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

        private void SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (!UseWebhooks || State != State.Realtime)
                return;

            if (WebhookProviderType == WebhookProvider.ProjectX)
            {
                int orderQtyForProvider = quantityOverride > 0 ? quantityOverride : GetDefaultWebhookQuantity();
                SendProjectX(eventType, entryPrice, takeProfit, stopLoss, isMarketEntry, orderQtyForProvider);
                return;
            }

            if (string.IsNullOrWhiteSpace(WebhookUrl))
                return;

            try
            {
                int orderQty = quantityOverride > 0 ? quantityOverride : GetDefaultWebhookQuantity();
                string ticker = !string.IsNullOrWhiteSpace(WebhookTickerOverride)
                    ? WebhookTickerOverride.Trim()
                    : (Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "UNKNOWN");
                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
                string json = string.Empty;
                string action = (eventType ?? string.Empty).ToLowerInvariant();

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

        private void SendProjectX(string eventType, double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            if (!EnsureProjectXSession())
                return;

            int accountId;
            string contractId;
            if (!TryGetProjectXIds(out accountId, out contractId))
                return;

            try
            {
                switch ((eventType ?? string.Empty).ToLowerInvariant())
                {
                    case "buy":
                    case "sell":
                        ProjectXPlaceOrder(eventType, accountId, contractId, entryPrice, takeProfit, stopLoss, isMarketEntry, quantity);
                        break;
                    case "exit":
                        ProjectXClosePosition(accountId, contractId);
                        break;
                    case "cancel":
                        ProjectXCancelOrders(accountId, contractId);
                        break;
                }
            }
            catch
            {
            }
        }

        private bool EnsureProjectXSession()
        {
            if (string.IsNullOrWhiteSpace(ProjectXApiBaseUrl))
                return false;

            if (!string.IsNullOrWhiteSpace(projectXSessionToken) &&
                (DateTime.UtcNow - projectXTokenAcquiredUtc).TotalHours < 23)
                return true;

            if (string.IsNullOrWhiteSpace(ProjectXUsername) || string.IsNullOrWhiteSpace(ProjectXApiKey))
                return false;

            string loginJson = string.Format(CultureInfo.InvariantCulture,
                "{{\"userName\":\"{0}\",\"loginKey\":\"{1}\"}}",
                ProjectXUsername,
                ProjectXApiKey);

            string response = ProjectXPost("/api/Auth/loginKey", loginJson, false);
            if (string.IsNullOrWhiteSpace(response))
                return false;

            string token;
            if (!TryGetJsonString(response, "token", out token))
                return false;

            projectXSessionToken = token;
            projectXTokenAcquiredUtc = DateTime.UtcNow;
            return true;
        }

        private bool TryGetProjectXIds(out int accountId, out string contractId)
        {
            accountId = 0;
            contractId = null;

            if (!int.TryParse(ProjectXAccountId, out accountId) || accountId <= 0)
                return false;
            if (string.IsNullOrWhiteSpace(ProjectXContractId))
                return false;

            contractId = ProjectXContractId.Trim();
            return true;
        }

        private string ProjectXPlaceOrder(string side, int accountId, string contractId, double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            int orderSide = side.Equals("buy", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int orderType = isMarketEntry ? 2 : 1;
            double entry = Instrument.MasterInstrument.RoundToTickSize(entryPrice);
            int tpTicks = Math.Max(1, PriceToTicks(Math.Abs(takeProfit - entry)));
            int slTicks = Math.Max(1, PriceToTicks(Math.Abs(entry - stopLoss)));

            string limitPart = isMarketEntry
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, ",\"limitPrice\":{0}", entry);

            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\",\"type\":{2},\"side\":{3},\"size\":{4}{5},\"takeProfitBracket\":{{\"quantity\":1,\"type\":1,\"ticks\":{6}}},\"stopLossBracket\":{{\"quantity\":1,\"type\":4,\"ticks\":{7}}}}}",
                accountId,
                contractId,
                orderType,
                orderSide,
                Math.Max(1, quantity),
                limitPart,
                tpTicks,
                slTicks);

            string response = ProjectXPost("/api/Order/place", json, true);
            int orderId;
            if (TryGetJsonInt(response, "orderId", out orderId))
            {
                projectXLastOrderId = orderId;
                projectXLastOrderContractId = contractId;
            }

            return response;
        }

        private string ProjectXClosePosition(int accountId, string contractId)
        {
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\"}}",
                accountId,
                contractId);
            return ProjectXPost("/api/Position/closeContract", json, true);
        }

        private string ProjectXCancelOrders(int accountId, string contractId)
        {
            if (projectXLastOrderId.HasValue && string.Equals(projectXLastOrderContractId, contractId, StringComparison.OrdinalIgnoreCase))
            {
                string cancelJson = string.Format(CultureInfo.InvariantCulture,
                    "{{\"accountId\":{0},\"orderId\":{1}}}",
                    accountId,
                    projectXLastOrderId.Value);
                return ProjectXPost("/api/Order/cancel", cancelJson, true);
            }

            string searchJson = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string searchResponse = ProjectXPost("/api/Order/searchOpen", searchJson, true);
            foreach (var order in ExtractProjectXOrders(searchResponse))
            {
                object contractObj;
                if (!order.TryGetValue("contractId", out contractObj))
                    continue;
                if (!string.Equals(contractObj != null ? contractObj.ToString() : string.Empty, contractId, StringComparison.OrdinalIgnoreCase))
                    continue;

                object idObj;
                int id;
                if (!order.TryGetValue("id", out idObj) || !int.TryParse(idObj != null ? idObj.ToString() : string.Empty, out id) || id <= 0)
                    continue;

                string cancelJson = string.Format(CultureInfo.InvariantCulture,
                    "{{\"accountId\":{0},\"orderId\":{1}}}",
                    accountId,
                    id);
                ProjectXPost("/api/Order/cancel", cancelJson, true);
            }

            return searchResponse;
        }

        private string ProjectXPost(string path, string json, bool requiresAuth)
        {
            string baseUrl = ProjectXApiBaseUrl != null ? ProjectXApiBaseUrl.TrimEnd('/') : string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            using (var client = new System.Net.WebClient())
            {
                client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                if (requiresAuth && !string.IsNullOrWhiteSpace(projectXSessionToken))
                    client.Headers[System.Net.HttpRequestHeader.Authorization] = "Bearer " + projectXSessionToken;
                return client.UploadString(baseUrl + path, "POST", json);
            }
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

            var array = raw as object[];
            if (array == null)
                yield break;

            for (int i = 0; i < array.Length; i++)
            {
                var dict = array[i] as Dictionary<string, object>;
                if (dict != null)
                    yield return dict;
            }
        }

        private int GetDefaultWebhookQuantity()
        {
            if (activeSessionId >= 1 && activeSessionId <= 3)
                return Math.Max(1, S_Contracts(activeSessionId));

            if (Position.Quantity > 0)
                return Math.Max(1, Position.Quantity);

            return Math.Max(1, NyContracts);
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

            if (entryMode == MichalEntryMode.Market)
            {
                if (RequireEntryConfirmation && !ShowEntryConfirmation(direction == 1 ? "Long" : "Short", closePrice, contracts)) return;
                entryOrder = direction == 1 ? EnterLong(contracts, entryName) : EnterShort(contracts, entryName);
            }
            else if (entryMode == MichalEntryMode.LimitOffset)
            {
                double limitPrice = direction == 1 ? closePrice - limitOffsetTicks * TickSize : closePrice + limitOffsetTicks * TickSize;
                if (RequireEntryConfirmation && !ShowEntryConfirmation(direction == 1 ? "Long" : "Short", limitPrice, contracts)) return;
                entryOrder = direction == 1 ? EnterLongLimit(0, true, contracts, limitPrice, entryName) : EnterShortLimit(0, true, contracts, limitPrice, entryName);
            }
            else if (entryMode == MichalEntryMode.LimitRetracement)
            {
                double retrace = signalCandleRange * (limitRetracePct / 100.0);
                double limitPrice = direction == 1 ? closePrice - retrace : closePrice + retrace;
                if (RequireEntryConfirmation && !ShowEntryConfirmation(direction == 1 ? "Long" : "Short", limitPrice, contracts)) return;
                entryOrder = direction == 1 ? EnterLongLimit(0, true, contracts, limitPrice, entryName) : EnterShortLimit(0, true, contracts, limitPrice, entryName);
            }

            Print(string.Format("{0} | [{1}] SIGNAL {2} | Close={3:F2}", Time[0], S_Label(sid), direction == 1 ? "LONG" : "SHORT", closePrice));
        }

        #endregion


        #region Take Profit

        private double CalculateTPDistanceTicks(int sid, int direction, double candleRange)
        {
            MichalTPMode mode = SD_TakeProfitMode(sid, direction);
            int tpTix = SD_TakeProfitTicks(sid, direction);
            int maxTp = SD_MaxTakeProfitTicks(sid, direction);
            double mult = SD_CandleMultiplier(sid, direction);
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
            double mult = SD_CandleMultiplier(sid, tradeDirection);
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
                if (newStop > originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long, newStop))
                    ExitLongStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), LongEntrySignal);
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                double newStop = GetShortUpperMAForTrail(sid) + trailOffsetTicks * TickSize;
                newStop = Instrument.MasterInstrument.RoundToTickSize(newStop);
                if (newStop < originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short, newStop))
                    ExitShortStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), ShortEntrySignal);
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
                if (newSl > originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long, newSl))
                { ExitLongStopMarket(0, true, contracts, newSl, BuildExitSignalName("SL"), LongEntrySignal); originalStopPrice = newSl; }
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short && entryBarBearish)
            {
                double newSl = Instrument.MasterInstrument.RoundToTickSize(High[1] + slExtraTicks * TickSize);
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
                if (newStop > originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long, newStop))
                    ExitLongStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), LongEntrySignal);
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                double newStop = Instrument.MasterInstrument.RoundToTickSize(High[trailCandleOffset] + slExtraTicks * TickSize);
                if (newStop < originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short, newStop))
                    ExitShortStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), ShortEntrySignal);
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
                    if (newStop > originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Long, newStop))
                        ExitLongStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), LongEntrySignal);
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
                    if (newStop < originalStopPrice && CanAmendProtectiveStopForCurrentMarket(MarketPosition.Short, newStop))
                        ExitShortStopMarket(0, true, contracts, newStop, BuildExitSignalName("SL"), ShortEntrySignal);
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
            nySessionTradeCount = 0; nySessionWinCount = 0; nySessionLossCount = 0; nySessionPnLTicks = 0; nySessionLimitsReached = false; nyLastTradeDirection = 0; nyLastTradeWasLoss = false; nyWasInNoTradesAfterWindow = false; nyWasInSkipWindow = false;
            euSessionTradeCount = 0; euSessionWinCount = 0; euSessionLossCount = 0; euSessionPnLTicks = 0; euSessionLimitsReached = false; euLastTradeDirection = 0; euLastTradeWasLoss = false; euWasInNoTradesAfterWindow = false; euWasInSkipWindow = false;
            asSessionTradeCount = 0; asSessionWinCount = 0; asSessionLossCount = 0; asSessionPnLTicks = 0; asSessionLimitsReached = false; asLastTradeDirection = 0; asLastTradeWasLoss = false; asWasInNoTradesAfterWindow = false; asWasInSkipWindow = false;

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
            // Per-session transitions
            for (int sid = 1; sid <= 3; sid++)
            {
                bool enabled = sid == 1 ? NyEnable : sid == 2 ? EuEnable : AsEnable;
                if (!enabled) continue;

                bool inNoTrades = ToSessionMinutes(Time[0].TimeOfDay) >= ToSessionMinutes(S_NoNewTradesAfter(sid).TimeOfDay);
                bool skipCfg = S_SkipTimeStart(sid).TimeOfDay != S_SkipTimeEnd(sid).TimeOfDay;
                bool inSkip = false;
                if (skipCfg)
                {
                    double barSM = ToSessionMinutes(Time[0].TimeOfDay);
                    inSkip = barSM >= ToSessionMinutes(S_SkipTimeStart(sid).TimeOfDay) && barSM < ToSessionMinutes(S_SkipTimeEnd(sid).TimeOfDay);
                }

                bool wasNoTrades = sid == 1 ? nyWasInNoTradesAfterWindow : sid == 2 ? euWasInNoTradesAfterWindow : asWasInNoTradesAfterWindow;
                bool wasSkip     = sid == 1 ? nyWasInSkipWindow          : sid == 2 ? euWasInSkipWindow          : asWasInSkipWindow;

                if (FlattenOnBlockedWindowTransition && activeSessionId == sid)
                {
                    if (!wasNoTrades && inNoTrades) FlattenAndCancel("NoTradesAfter");
                    if (!wasSkip && inSkip) FlattenAndCancel("SkipWindow");
                }

                if (sid == 1) { nyWasInNoTradesAfterWindow = inNoTrades; nyWasInSkipWindow = inSkip; }
                else if (sid == 2) { euWasInNoTradesAfterWindow = inNoTrades; euWasInSkipWindow = inSkip; }
                else { asWasInNoTradesAfterWindow = inNoTrades; asWasInSkipWindow = inSkip; }
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
            // Draw trade windows for all enabled sessions
            for (int sid = 1; sid <= 3; sid++)
            {
                bool enabled = sid == 1 ? NyEnable : sid == 2 ? EuEnable : AsEnable;
                if (!enabled) continue;
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
            Brush fillBrush = sid == 1 ? Brushes.Gold : sid == 2 ? Brushes.CornflowerBlue : Brushes.MediumSeaGreen;
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
            if (NyEnable && IsInSessionDisplayWindow(1, time))
                return 1;
            if (EuEnable && IsInSessionDisplayWindow(2, time))
                return 2;
            if (AsEnable && IsInSessionDisplayWindow(3, time))
                return 3;
            return 0;
        }

        private int GetDefaultEnabledSessionId()
        {
            if (NyEnable) return 1;
            if (EuEnable) return 2;
            if (AsEnable) return 3;
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
                case 1: return "New York";
                case 2: return "London";
                case 3: return "Asia";
                default: return "Off";
            }
        }

        private string BuildContractsInfoText(int sid)
        {
            if (sid >= 1 && sid <= 3)
                return S_Contracts(sid).ToString(CultureInfo.InvariantCulture);

            if (Position.Quantity > 0)
                return Position.Quantity.ToString(CultureInfo.InvariantCulture);

            int defaultSid = GetDefaultEnabledSessionId();
            return defaultSid >= 1 && defaultSid <= 3
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
        [Display(Name = "Require Entry Confirmation", Order = 1, GroupName = "00. General")]
        public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Session Start Time", Order = 2, GroupName = "00. General")]
        public DateTime SessionStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Order = 3, GroupName = "00. General")]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "News Block Minutes", Order = 4, GroupName = "00. General")]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Flatten On Blocked Window", Order = 5, GroupName = "00. General")]
        public bool FlattenOnBlockedWindowTransition { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Debug Logging", Order = 6, GroupName = "00. General")]
        public bool DebugLogging { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Account Balance", Description = "When net liquidation reaches or exceeds this value, entries are blocked and open positions are flattened. 0 disables.", Order = 7, GroupName = "00. General")]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Webhooks", Description = "Enable outbound order webhooks.", Order = 0, GroupName = "52. Webhooks")]
        public bool UseWebhooks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Webhook Provider", Description = "Select webhook target: TradersPost or ProjectX.", Order = 1, GroupName = "52. Webhooks")]
        public WebhookProvider WebhookProviderType { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL", Description = "HTTP endpoint for order webhooks. Leave empty to disable.", Order = 2, GroupName = "52. Webhooks")]
        public string WebhookUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Ticker Override", Description = "Optional TradersPost ticker/instrument name override. Leave empty to use the chart instrument automatically.", Order = 3, GroupName = "52. Webhooks")]
        public string WebhookTickerOverride { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Base URL", Description = "ProjectX gateway base URL.", Order = 3, GroupName = "52. Webhooks")]
        public string ProjectXApiBaseUrl { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Username", Description = "ProjectX login username.", Order = 4, GroupName = "52. Webhooks")]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Key", Description = "ProjectX login key.", Order = 5, GroupName = "52. Webhooks")]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Account ID", Description = "ProjectX account id used for order routing.", Order = 6, GroupName = "52. Webhooks")]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Contract ID", Description = "ProjectX contract id (for example CON.F.US.DA6.M25).", Order = 7, GroupName = "52. Webhooks")]
        public string ProjectXContractId { get; set; }


        // ═══════════════════════════════════════
        //  NEW YORK SESSION
        // ═══════════════════════════════════════


        [NinjaScriptProperty]
        [Display(Name = "Enable New York Session", Order = 1, GroupName = "01. Ny: Session")]
        public bool NyEnable { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "01. Ny: Session")]
        public int NyContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "01. Ny: Session")]
        public bool NyEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "01. Ny: Session")]
        public bool NyEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "01. Ny: Session")]
        public DateTime NyTradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Skip Time Start", Order = 6, GroupName = "01. Ny: Session")]
        public DateTime NySkipTimeStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Skip Time End", Order = 7, GroupName = "01. Ny: Session")]
        public DateTime NySkipTimeEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "01. Ny: Session")]
        public DateTime NyNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "01. Ny: Session")]
        public DateTime NyForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "01. Ny: Session")]
        public int NyMaxTradesPerSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Wins Per Session", Description = "0 = unlimited.", Order = 11, GroupName = "01. Ny: Session")]
        public int NyMaxWinsPerSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "01. Ny: Session")]
        public int NyMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "01. Ny: Session")]
        public int NyMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "01. Ny: Session")]
        public int NyMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "02. Ny: Long: MA Settings")]
        public int NyLongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "02. Ny: Long: MA Settings")]
        public int NyLongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "MA Type", Order = 3, GroupName = "02. Ny: Long: MA Settings")]
        public MAMode NyLongMaType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Mode", Order = 1, GroupName = "03. Ny: Long: Entry")]
        public MichalEntryMode NyLongEntryMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Limit Offset (Ticks)", Order = 2, GroupName = "03. Ny: Long: Entry")]
        public int NyLongLimitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "Limit Retracement %", Order = 3, GroupName = "03. Ny: Long: Entry")]
        public double NyLongLimitRetracementPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Take Profit Mode", Order = 1, GroupName = "04. Ny: Long: Take Profit")]
        public MichalTPMode NyLongTakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP Fixed Ticks", Order = 2, GroupName = "04. Ny: Long: Take Profit")]
        public int NyLongTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "04. Ny: Long: Take Profit")]
        public int NyLongMaxTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Swing Strength", Order = 4, GroupName = "04. Ny: Long: Take Profit")]
        public int NyLongSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "04. Ny: Long: Take Profit")]
        public double NyLongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "05. Ny: Long: Stop Loss")]
        public int NyLongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "05. Ny: Long: Stop Loss")]
        public int NyLongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "05. Ny: Long: Stop Loss")]
        public int NyLongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "05. Ny: Long: Stop Loss")]
        public int NyLongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "05. Ny: Long: Stop Loss")]
        public double NyLongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "05. Ny: Long: Stop Loss")]
        public bool NyLongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "SL At MA", Order = 7, GroupName = "05. Ny: Long: Stop Loss")]
        public bool NyLongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "05. Ny: Long: Stop Loss")]
        public bool NyLongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "05. Ny: Long: Stop Loss")]
        public int NyLongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "06. Ny: Long: Breakeven")]
        public bool NyLongEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Breakeven Mode", Order = 2, GroupName = "06. Ny: Long: Breakeven")]
        public BEMode2 NyLongBreakevenMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "06. Ny: Long: Breakeven")]
        public int NyLongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "06. Ny: Long: Breakeven")]
        public double NyLongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "06. Ny: Long: Breakeven")]
        public int NyLongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "07. Ny: Long: Trade Management")]
        public int NyLongMaxBarsInTrade { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Opposing Bar Exit", Order = 2, GroupName = "07. Ny: Long: Trade Management")]
        public bool NyLongEnableOpposingBarExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Engulfing Exit", Order = 3, GroupName = "07. Ny: Long: Trade Management")]
        public bool NyLongEnableEngulfingExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "07. Ny: Long: Trade Management")]
        public bool NyLongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "07. Ny: Long: Trade Management")]
        public int NyLongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "08. Ny: Long: Entry Filters")]
        public bool NyLongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "08. Ny: Long: Entry Filters")]
        public bool NyLongAllowSameDirectionAfterLoss { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 500)]
        [Display(Name = "Max Distance From MA (Ticks)", Description = "0 = disabled.", Order = 3, GroupName = "08. Ny: Long: Entry Filters")]
        public int NyLongMaxDistanceFromSmaTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "08. Ny: Long: Entry Filters")]
        public bool NyLongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "08. Ny: Long: Entry Filters")]
        public int NyLongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "08. Ny: Long: Entry Filters")]
        public bool NyLongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "08. Ny: Long: Entry Filters")]
        public int NyLongWmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Signal Candle (Ticks)", Description = "0 = disabled.", Order = 8, GroupName = "08. Ny: Long: Entry Filters")]
        public int NyLongMaxSignalCandleTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "08. Ny: Long: Entry Filters")]
        public double NyLongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "08. Ny: Long: Entry Filters")]
        public int NyLongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "08. Ny: Long: Entry Filters")]
        public bool NyLongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "08. Ny: Long: Entry Filters")]
        public int NyLongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "08. Ny: Long: Entry Filters")]
        public int NyLongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "09. Ny: Long: Early Exit Filters")]
        public bool NyLongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "09. Ny: Long: Early Exit Filters")]
        public int NyLongEmaEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (Ticks)", Description = "0 = exit on any close beyond EMA.", Order = 3, GroupName = "09. Ny: Long: Early Exit Filters")]
        public int NyLongEmaEarlyExitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "09. Ny: Long: Early Exit Filters")]
        public bool NyLongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "09. Ny: Long: Early Exit Filters")]
        public int NyLongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "09. Ny: Long: Early Exit Filters")]
        public double NyLongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "09. Ny: Long: Early Exit Filters")]
        public bool NyLongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "09. Ny: Long: Early Exit Filters")]
        public double NyLongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "10. Ny: Short: MA Settings")]
        public int NyShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "10. Ny: Short: MA Settings")]
        public int NyShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "MA Type", Order = 3, GroupName = "10. Ny: Short: MA Settings")]
        public MAMode NyShortMaType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Mode", Order = 1, GroupName = "11. Ny: Short: Entry")]
        public MichalEntryMode NyShortEntryMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Limit Offset (Ticks)", Order = 2, GroupName = "11. Ny: Short: Entry")]
        public int NyShortLimitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "Limit Retracement %", Order = 3, GroupName = "11. Ny: Short: Entry")]
        public double NyShortLimitRetracementPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Take Profit Mode", Order = 1, GroupName = "12. Ny: Short: Take Profit")]
        public MichalTPMode NyShortTakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP Fixed Ticks", Order = 2, GroupName = "12. Ny: Short: Take Profit")]
        public int NyShortTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "12. Ny: Short: Take Profit")]
        public int NyShortMaxTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Swing Strength", Order = 4, GroupName = "12. Ny: Short: Take Profit")]
        public int NyShortSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "12. Ny: Short: Take Profit")]
        public double NyShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "13. Ny: Short: Stop Loss")]
        public int NyShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "13. Ny: Short: Stop Loss")]
        public int NyShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "13. Ny: Short: Stop Loss")]
        public int NyShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "13. Ny: Short: Stop Loss")]
        public int NyShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "13. Ny: Short: Stop Loss")]
        public double NyShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "13. Ny: Short: Stop Loss")]
        public bool NyShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "SL At MA", Order = 7, GroupName = "13. Ny: Short: Stop Loss")]
        public bool NyShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "13. Ny: Short: Stop Loss")]
        public bool NyShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "13. Ny: Short: Stop Loss")]
        public int NyShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "14. Ny: Short: Breakeven")]
        public bool NyShortEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Breakeven Mode", Order = 2, GroupName = "14. Ny: Short: Breakeven")]
        public BEMode2 NyShortBreakevenMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "14. Ny: Short: Breakeven")]
        public int NyShortBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "14. Ny: Short: Breakeven")]
        public double NyShortBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "14. Ny: Short: Breakeven")]
        public int NyShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "15. Ny: Short: Trade Management")]
        public int NyShortMaxBarsInTrade { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Opposing Bar Exit", Order = 2, GroupName = "15. Ny: Short: Trade Management")]
        public bool NyShortEnableOpposingBarExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Engulfing Exit", Order = 3, GroupName = "15. Ny: Short: Trade Management")]
        public bool NyShortEnableEngulfingExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "15. Ny: Short: Trade Management")]
        public bool NyShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "15. Ny: Short: Trade Management")]
        public int NyShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "16. Ny: Short: Entry Filters")]
        public bool NyShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "16. Ny: Short: Entry Filters")]
        public bool NyShortAllowSameDirectionAfterLoss { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 500)]
        [Display(Name = "Max Distance From MA (Ticks)", Description = "0 = disabled.", Order = 3, GroupName = "16. Ny: Short: Entry Filters")]
        public int NyShortMaxDistanceFromSmaTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "16. Ny: Short: Entry Filters")]
        public bool NyShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "16. Ny: Short: Entry Filters")]
        public int NyShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "16. Ny: Short: Entry Filters")]
        public bool NyShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "16. Ny: Short: Entry Filters")]
        public int NyShortWmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Signal Candle (Ticks)", Description = "0 = disabled.", Order = 8, GroupName = "16. Ny: Short: Entry Filters")]
        public int NyShortMaxSignalCandleTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "16. Ny: Short: Entry Filters")]
        public double NyShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "16. Ny: Short: Entry Filters")]
        public int NyShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "16. Ny: Short: Entry Filters")]
        public bool NyShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "16. Ny: Short: Entry Filters")]
        public int NyShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "16. Ny: Short: Entry Filters")]
        public int NyShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "17. Ny: Short: Early Exit Filters")]
        public bool NyShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "17. Ny: Short: Early Exit Filters")]
        public int NyShortEmaEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (Ticks)", Description = "0 = exit on any close beyond EMA.", Order = 3, GroupName = "17. Ny: Short: Early Exit Filters")]
        public int NyShortEmaEarlyExitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "17. Ny: Short: Early Exit Filters")]
        public bool NyShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "17. Ny: Short: Early Exit Filters")]
        public int NyShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "17. Ny: Short: Early Exit Filters")]
        public double NyShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "17. Ny: Short: Early Exit Filters")]
        public bool NyShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "17. Ny: Short: Early Exit Filters")]
        public double NyShortAdxDrawdownFromPeak { get; set; }


        // ═══════════════════════════════════════
        //  EUROPE SESSION
        // ═══════════════════════════════════════


        [NinjaScriptProperty]
        [Display(Name = "Enable Europe Session", Order = 1, GroupName = "18. Eu: Session")]
        public bool EuEnable { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "18. Eu: Session")]
        public int EuContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "18. Eu: Session")]
        public bool EuEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "18. Eu: Session")]
        public bool EuEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "18. Eu: Session")]
        public DateTime EuTradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Skip Time Start", Order = 6, GroupName = "18. Eu: Session")]
        public DateTime EuSkipTimeStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Skip Time End", Order = 7, GroupName = "18. Eu: Session")]
        public DateTime EuSkipTimeEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "18. Eu: Session")]
        public DateTime EuNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "18. Eu: Session")]
        public DateTime EuForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "18. Eu: Session")]
        public int EuMaxTradesPerSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Wins Per Session", Description = "0 = unlimited.", Order = 11, GroupName = "18. Eu: Session")]
        public int EuMaxWinsPerSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "18. Eu: Session")]
        public int EuMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "18. Eu: Session")]
        public int EuMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "18. Eu: Session")]
        public int EuMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "19. Eu: Long: MA Settings")]
        public int EuLongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "19. Eu: Long: MA Settings")]
        public int EuLongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "MA Type", Order = 3, GroupName = "19. Eu: Long: MA Settings")]
        public MAMode EuLongMaType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Mode", Order = 1, GroupName = "20. Eu: Long: Entry")]
        public MichalEntryMode EuLongEntryMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Limit Offset (Ticks)", Order = 2, GroupName = "20. Eu: Long: Entry")]
        public int EuLongLimitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "Limit Retracement %", Order = 3, GroupName = "20. Eu: Long: Entry")]
        public double EuLongLimitRetracementPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Take Profit Mode", Order = 1, GroupName = "21. Eu: Long: Take Profit")]
        public MichalTPMode EuLongTakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP Fixed Ticks", Order = 2, GroupName = "21. Eu: Long: Take Profit")]
        public int EuLongTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "21. Eu: Long: Take Profit")]
        public int EuLongMaxTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Swing Strength", Order = 4, GroupName = "21. Eu: Long: Take Profit")]
        public int EuLongSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "21. Eu: Long: Take Profit")]
        public double EuLongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "22. Eu: Long: Stop Loss")]
        public int EuLongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "22. Eu: Long: Stop Loss")]
        public int EuLongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "22. Eu: Long: Stop Loss")]
        public int EuLongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "22. Eu: Long: Stop Loss")]
        public int EuLongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "22. Eu: Long: Stop Loss")]
        public double EuLongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "22. Eu: Long: Stop Loss")]
        public bool EuLongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "SL At MA", Order = 7, GroupName = "22. Eu: Long: Stop Loss")]
        public bool EuLongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "22. Eu: Long: Stop Loss")]
        public bool EuLongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "22. Eu: Long: Stop Loss")]
        public int EuLongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "23. Eu: Long: Breakeven")]
        public bool EuLongEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Breakeven Mode", Order = 2, GroupName = "23. Eu: Long: Breakeven")]
        public BEMode2 EuLongBreakevenMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "23. Eu: Long: Breakeven")]
        public int EuLongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "23. Eu: Long: Breakeven")]
        public double EuLongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "23. Eu: Long: Breakeven")]
        public int EuLongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "24. Eu: Long: Trade Management")]
        public int EuLongMaxBarsInTrade { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Opposing Bar Exit", Order = 2, GroupName = "24. Eu: Long: Trade Management")]
        public bool EuLongEnableOpposingBarExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Engulfing Exit", Order = 3, GroupName = "24. Eu: Long: Trade Management")]
        public bool EuLongEnableEngulfingExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "24. Eu: Long: Trade Management")]
        public bool EuLongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "24. Eu: Long: Trade Management")]
        public int EuLongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "25. Eu: Long: Entry Filters")]
        public bool EuLongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "25. Eu: Long: Entry Filters")]
        public bool EuLongAllowSameDirectionAfterLoss { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 500)]
        [Display(Name = "Max Distance From MA (Ticks)", Description = "0 = disabled.", Order = 3, GroupName = "25. Eu: Long: Entry Filters")]
        public int EuLongMaxDistanceFromSmaTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "25. Eu: Long: Entry Filters")]
        public bool EuLongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "25. Eu: Long: Entry Filters")]
        public int EuLongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "25. Eu: Long: Entry Filters")]
        public bool EuLongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "25. Eu: Long: Entry Filters")]
        public int EuLongWmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Signal Candle (Ticks)", Description = "0 = disabled.", Order = 8, GroupName = "25. Eu: Long: Entry Filters")]
        public int EuLongMaxSignalCandleTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "25. Eu: Long: Entry Filters")]
        public double EuLongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "25. Eu: Long: Entry Filters")]
        public int EuLongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "25. Eu: Long: Entry Filters")]
        public bool EuLongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "25. Eu: Long: Entry Filters")]
        public int EuLongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "25. Eu: Long: Entry Filters")]
        public int EuLongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "26. Eu: Long: Early Exit Filters")]
        public bool EuLongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "26. Eu: Long: Early Exit Filters")]
        public int EuLongEmaEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (Ticks)", Description = "0 = exit on any close beyond EMA.", Order = 3, GroupName = "26. Eu: Long: Early Exit Filters")]
        public int EuLongEmaEarlyExitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "26. Eu: Long: Early Exit Filters")]
        public bool EuLongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "26. Eu: Long: Early Exit Filters")]
        public int EuLongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "26. Eu: Long: Early Exit Filters")]
        public double EuLongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "26. Eu: Long: Early Exit Filters")]
        public bool EuLongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "26. Eu: Long: Early Exit Filters")]
        public double EuLongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "27. Eu: Short: MA Settings")]
        public int EuShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "27. Eu: Short: MA Settings")]
        public int EuShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "MA Type", Order = 3, GroupName = "27. Eu: Short: MA Settings")]
        public MAMode EuShortMaType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Mode", Order = 1, GroupName = "28. Eu: Short: Entry")]
        public MichalEntryMode EuShortEntryMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Limit Offset (Ticks)", Order = 2, GroupName = "28. Eu: Short: Entry")]
        public int EuShortLimitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "Limit Retracement %", Order = 3, GroupName = "28. Eu: Short: Entry")]
        public double EuShortLimitRetracementPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Take Profit Mode", Order = 1, GroupName = "29. Eu: Short: Take Profit")]
        public MichalTPMode EuShortTakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP Fixed Ticks", Order = 2, GroupName = "29. Eu: Short: Take Profit")]
        public int EuShortTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "29. Eu: Short: Take Profit")]
        public int EuShortMaxTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Swing Strength", Order = 4, GroupName = "29. Eu: Short: Take Profit")]
        public int EuShortSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "29. Eu: Short: Take Profit")]
        public double EuShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "30. Eu: Short: Stop Loss")]
        public int EuShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "30. Eu: Short: Stop Loss")]
        public int EuShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "30. Eu: Short: Stop Loss")]
        public int EuShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "30. Eu: Short: Stop Loss")]
        public int EuShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "30. Eu: Short: Stop Loss")]
        public double EuShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "30. Eu: Short: Stop Loss")]
        public bool EuShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "SL At MA", Order = 7, GroupName = "30. Eu: Short: Stop Loss")]
        public bool EuShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "30. Eu: Short: Stop Loss")]
        public bool EuShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "30. Eu: Short: Stop Loss")]
        public int EuShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "31. Eu: Short: Breakeven")]
        public bool EuShortEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Breakeven Mode", Order = 2, GroupName = "31. Eu: Short: Breakeven")]
        public BEMode2 EuShortBreakevenMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "31. Eu: Short: Breakeven")]
        public int EuShortBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "31. Eu: Short: Breakeven")]
        public double EuShortBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "31. Eu: Short: Breakeven")]
        public int EuShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "32. Eu: Short: Trade Management")]
        public int EuShortMaxBarsInTrade { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Opposing Bar Exit", Order = 2, GroupName = "32. Eu: Short: Trade Management")]
        public bool EuShortEnableOpposingBarExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Engulfing Exit", Order = 3, GroupName = "32. Eu: Short: Trade Management")]
        public bool EuShortEnableEngulfingExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "32. Eu: Short: Trade Management")]
        public bool EuShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "32. Eu: Short: Trade Management")]
        public int EuShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "33. Eu: Short: Entry Filters")]
        public bool EuShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "33. Eu: Short: Entry Filters")]
        public bool EuShortAllowSameDirectionAfterLoss { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 500)]
        [Display(Name = "Max Distance From MA (Ticks)", Description = "0 = disabled.", Order = 3, GroupName = "33. Eu: Short: Entry Filters")]
        public int EuShortMaxDistanceFromSmaTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "33. Eu: Short: Entry Filters")]
        public bool EuShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "33. Eu: Short: Entry Filters")]
        public int EuShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "33. Eu: Short: Entry Filters")]
        public bool EuShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "33. Eu: Short: Entry Filters")]
        public int EuShortWmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Signal Candle (Ticks)", Description = "0 = disabled.", Order = 8, GroupName = "33. Eu: Short: Entry Filters")]
        public int EuShortMaxSignalCandleTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "33. Eu: Short: Entry Filters")]
        public double EuShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "33. Eu: Short: Entry Filters")]
        public int EuShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "33. Eu: Short: Entry Filters")]
        public bool EuShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "33. Eu: Short: Entry Filters")]
        public int EuShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "33. Eu: Short: Entry Filters")]
        public int EuShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "34. Eu: Short: Early Exit Filters")]
        public bool EuShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "34. Eu: Short: Early Exit Filters")]
        public int EuShortEmaEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (Ticks)", Description = "0 = exit on any close beyond EMA.", Order = 3, GroupName = "34. Eu: Short: Early Exit Filters")]
        public int EuShortEmaEarlyExitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "34. Eu: Short: Early Exit Filters")]
        public bool EuShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "34. Eu: Short: Early Exit Filters")]
        public int EuShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "34. Eu: Short: Early Exit Filters")]
        public double EuShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "34. Eu: Short: Early Exit Filters")]
        public bool EuShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "34. Eu: Short: Early Exit Filters")]
        public double EuShortAdxDrawdownFromPeak { get; set; }


        // ═══════════════════════════════════════
        //  ASIA SESSION
        // ═══════════════════════════════════════


        [NinjaScriptProperty]
        [Display(Name = "Enable Asia Session", Order = 1, GroupName = "35. As: Session")]
        public bool AsEnable { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries.", Order = 2, GroupName = "35. As: Session")]
        public int AsContracts { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Long Trades", Order = 3, GroupName = "35. As: Session")]
        public bool AsEnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Short Trades", Order = 4, GroupName = "35. As: Session")]
        public bool AsEnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 5, GroupName = "35. As: Session")]
        public DateTime AsTradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Skip Time Start", Order = 6, GroupName = "35. As: Session")]
        public DateTime AsSkipTimeStart { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Skip Time End", Order = 7, GroupName = "35. As: Session")]
        public DateTime AsSkipTimeEnd { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 8, GroupName = "35. As: Session")]
        public DateTime AsNoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 9, GroupName = "35. As: Session")]
        public DateTime AsForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 10, GroupName = "35. As: Session")]
        public int AsMaxTradesPerSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Wins Per Session", Description = "0 = unlimited.", Order = 11, GroupName = "35. As: Session")]
        public int AsMaxWinsPerSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 12, GroupName = "35. As: Session")]
        public int AsMaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 13, GroupName = "35. As: Session")]
        public int AsMaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 14, GroupName = "35. As: Session")]
        public int AsMaxSessionLossTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "36. As: Long: MA Settings")]
        public int AsLongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "36. As: Long: MA Settings")]
        public int AsLongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "MA Type", Order = 3, GroupName = "36. As: Long: MA Settings")]
        public MAMode AsLongMaType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Mode", Order = 1, GroupName = "37. As: Long: Entry")]
        public MichalEntryMode AsLongEntryMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Limit Offset (Ticks)", Order = 2, GroupName = "37. As: Long: Entry")]
        public int AsLongLimitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "Limit Retracement %", Order = 3, GroupName = "37. As: Long: Entry")]
        public double AsLongLimitRetracementPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Take Profit Mode", Order = 1, GroupName = "38. As: Long: Take Profit")]
        public MichalTPMode AsLongTakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP Fixed Ticks", Order = 2, GroupName = "38. As: Long: Take Profit")]
        public int AsLongTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "38. As: Long: Take Profit")]
        public int AsLongMaxTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Swing Strength", Order = 4, GroupName = "38. As: Long: Take Profit")]
        public int AsLongSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "38. As: Long: Take Profit")]
        public double AsLongCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "39. As: Long: Stop Loss")]
        public int AsLongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "39. As: Long: Stop Loss")]
        public int AsLongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "39. As: Long: Stop Loss")]
        public int AsLongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "39. As: Long: Stop Loss")]
        public int AsLongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "39. As: Long: Stop Loss")]
        public double AsLongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "39. As: Long: Stop Loss")]
        public bool AsLongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "SL At MA", Order = 7, GroupName = "39. As: Long: Stop Loss")]
        public bool AsLongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "39. As: Long: Stop Loss")]
        public bool AsLongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "39. As: Long: Stop Loss")]
        public int AsLongTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "40. As: Long: Breakeven")]
        public bool AsLongEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Breakeven Mode", Order = 2, GroupName = "40. As: Long: Breakeven")]
        public BEMode2 AsLongBreakevenMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "40. As: Long: Breakeven")]
        public int AsLongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "40. As: Long: Breakeven")]
        public double AsLongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "40. As: Long: Breakeven")]
        public int AsLongBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "41. As: Long: Trade Management")]
        public int AsLongMaxBarsInTrade { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Opposing Bar Exit", Order = 2, GroupName = "41. As: Long: Trade Management")]
        public bool AsLongEnableOpposingBarExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Engulfing Exit", Order = 3, GroupName = "41. As: Long: Trade Management")]
        public bool AsLongEnableEngulfingExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "41. As: Long: Trade Management")]
        public bool AsLongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "41. As: Long: Trade Management")]
        public int AsLongPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "42. As: Long: Entry Filters")]
        public bool AsLongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "42. As: Long: Entry Filters")]
        public bool AsLongAllowSameDirectionAfterLoss { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 500)]
        [Display(Name = "Max Distance From MA (Ticks)", Description = "0 = disabled.", Order = 3, GroupName = "42. As: Long: Entry Filters")]
        public int AsLongMaxDistanceFromSmaTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "42. As: Long: Entry Filters")]
        public bool AsLongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "42. As: Long: Entry Filters")]
        public int AsLongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "42. As: Long: Entry Filters")]
        public bool AsLongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "42. As: Long: Entry Filters")]
        public int AsLongWmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Signal Candle (Ticks)", Description = "0 = disabled.", Order = 8, GroupName = "42. As: Long: Entry Filters")]
        public int AsLongMaxSignalCandleTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "42. As: Long: Entry Filters")]
        public double AsLongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "42. As: Long: Entry Filters")]
        public int AsLongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "42. As: Long: Entry Filters")]
        public bool AsLongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "42. As: Long: Entry Filters")]
        public int AsLongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "42. As: Long: Entry Filters")]
        public int AsLongAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "43. As: Long: Early Exit Filters")]
        public bool AsLongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "43. As: Long: Early Exit Filters")]
        public int AsLongEmaEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (Ticks)", Description = "0 = exit on any close beyond EMA.", Order = 3, GroupName = "43. As: Long: Early Exit Filters")]
        public int AsLongEmaEarlyExitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "43. As: Long: Early Exit Filters")]
        public bool AsLongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "43. As: Long: Early Exit Filters")]
        public int AsLongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "43. As: Long: Early Exit Filters")]
        public double AsLongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X from peak.", Order = 7, GroupName = "43. As: Long: Early Exit Filters")]
        public bool AsLongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "43. As: Long: Early Exit Filters")]
        public double AsLongAdxDrawdownFromPeak { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "44. As: Short: MA Settings")]
        public int AsShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "44. As: Short: MA Settings")]
        public int AsShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "MA Type", Order = 3, GroupName = "44. As: Short: MA Settings")]
        public MAMode AsShortMaType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Entry Mode", Order = 1, GroupName = "45. As: Short: Entry")]
        public MichalEntryMode AsShortEntryMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Limit Offset (Ticks)", Order = 2, GroupName = "45. As: Short: Entry")]
        public int AsShortLimitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "Limit Retracement %", Order = 3, GroupName = "45. As: Short: Entry")]
        public double AsShortLimitRetracementPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Take Profit Mode", Order = 1, GroupName = "46. As: Short: Take Profit")]
        public MichalTPMode AsShortTakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP Fixed Ticks", Order = 2, GroupName = "46. As: Short: Take Profit")]
        public int AsShortTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "46. As: Short: Take Profit")]
        public int AsShortMaxTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "Swing Strength", Order = 4, GroupName = "46. As: Short: Take Profit")]
        public int AsShortSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "46. As: Short: Take Profit")]
        public double AsShortCandleMultiplier { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "47. As: Short: Stop Loss")]
        public int AsShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "47. As: Short: Stop Loss")]
        public int AsShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "47. As: Short: Stop Loss")]
        public int AsShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "47. As: Short: Stop Loss")]
        public int AsShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "47. As: Short: Stop Loss")]
        public double AsShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "47. As: Short: Stop Loss")]
        public bool AsShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "SL At MA", Order = 7, GroupName = "47. As: Short: Stop Loss")]
        public bool AsShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "47. As: Short: Stop Loss")]
        public bool AsShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "47. As: Short: Stop Loss")]
        public int AsShortTrailCandleOffset { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "48. As: Short: Breakeven")]
        public bool AsShortEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Breakeven Mode", Order = 2, GroupName = "48. As: Short: Breakeven")]
        public BEMode2 AsShortBreakevenMode { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "48. As: Short: Breakeven")]
        public int AsShortBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "48. As: Short: Breakeven")]
        public double AsShortBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "48. As: Short: Breakeven")]
        public int AsShortBreakevenOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "49. As: Short: Trade Management")]
        public int AsShortMaxBarsInTrade { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Opposing Bar Exit", Order = 2, GroupName = "49. As: Short: Trade Management")]
        public bool AsShortEnableOpposingBarExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Engulfing Exit", Order = 3, GroupName = "49. As: Short: Trade Management")]
        public bool AsShortEnableEngulfingExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "49. As: Short: Trade Management")]
        public bool AsShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "49. As: Short: Trade Management")]
        public int AsShortPriceOffsetReductionTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "50. As: Short: Entry Filters")]
        public bool AsShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "50. As: Short: Entry Filters")]
        public bool AsShortAllowSameDirectionAfterLoss { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 500)]
        [Display(Name = "Max Distance From MA (Ticks)", Description = "0 = disabled.", Order = 3, GroupName = "50. As: Short: Entry Filters")]
        public int AsShortMaxDistanceFromSmaTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "50. As: Short: Entry Filters")]
        public bool AsShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "50. As: Short: Entry Filters")]
        public int AsShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "50. As: Short: Entry Filters")]
        public bool AsShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "50. As: Short: Entry Filters")]
        public int AsShortWmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Signal Candle (Ticks)", Description = "0 = disabled.", Order = 8, GroupName = "50. As: Short: Entry Filters")]
        public int AsShortMaxSignalCandleTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "50. As: Short: Entry Filters")]
        public double AsShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "50. As: Short: Entry Filters")]
        public int AsShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "50. As: Short: Entry Filters")]
        public bool AsShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "50. As: Short: Entry Filters")]
        public int AsShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "50. As: Short: Entry Filters")]
        public int AsShortAdxMinLevel { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "51. As: Short: Early Exit Filters")]
        public bool AsShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "51. As: Short: Early Exit Filters")]
        public int AsShortEmaEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (Ticks)", Description = "0 = exit on any close beyond EMA.", Order = 3, GroupName = "51. As: Short: Early Exit Filters")]
        public int AsShortEmaEarlyExitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "51. As: Short: Early Exit Filters")]
        public bool AsShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "51. As: Short: Early Exit Filters")]
        public int AsShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "51. As: Short: Early Exit Filters")]
        public double AsShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X from peak.", Order = 7, GroupName = "51. As: Short: Early Exit Filters")]
        public bool AsShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "51. As: Short: Early Exit Filters")]
        public double AsShortAdxDrawdownFromPeak { get; set; }

        public enum MAMode { SMA, EMA, Both }
        public enum MichalEntryMode { Market, LimitOffset, LimitRetracement }
        public enum MichalTPMode { FixedTicks, SwingPoint, CandleMultiple }
        public enum BEMode2 { FixedTicks, CandlePercent }
        public enum WebhookProvider { TradersPost, ProjectX }

        #endregion
    }

}
