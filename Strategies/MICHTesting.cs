#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows;
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

        public MICHTesting()
        {
            VendorLicense(1235);
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
        private MarketPosition prevMarketPosition;
        private Order          entryOrder;
        private Order          stopOrder;
        private Order          targetOrder;
        private bool           forceFlattenInProgress;
        private bool           forceFlattenOrderSubmitted;
        private string         forceFlattenReason;
        private StrategyHeartbeatReporter heartbeatReporter;

        // =====================================================================
        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description  = "Michal MS v1.11 — 9-session multi-session strategy with MichalV106 entry logic.";
                Name         = "MICHTesting";
                Calculate    = Calculate.OnBarClose;
                EntriesPerDirection   = 1;
                EntryHandling         = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds    = 30;
                IsFillLimitOnTouch           = false;
                MaximumBarsLookBack          = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution          = OrderFillResolution.Standard;
                Slippage                     = 0;
                StartBehavior                = StartBehavior.WaitUntilFlat;
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

                heartbeatReporter = new StrategyHeartbeatReporter(
                    HeartbeatStrategyName,
                    System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv"));
            }
            else if (State == State.Transition) { ResetAll(); }
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
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int qty, int filled, double avgFill, OrderState state, DateTime time, ErrorCode err, string nativeErr)
        {
            string orderName = order != null ? (order.Name ?? string.Empty) : string.Empty;
            if (orderName == BuildExitSignalName("TP"))
                targetOrder = order;
            else if (orderName == BuildExitSignalName("SL"))
                stopOrder = order;

            if (entryOrder!=null && order==entryOrder && (state==OrderState.Cancelled||state==OrderState.Rejected))
            { entryOrder=null; hasActivePosition=false; activeSessionId=0; }

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
            if (d==1 &&be>originalStopPrice&&be<Close[0]) { ExitLongStopMarket(0,true,Position.Quantity,be,BuildExitSignalName("SL"),eName);  originalStopPrice=be; breakEvenApplied=true; }
            if (d==-1&&be<originalStopPrice&&be>Close[0]) { ExitShortStopMarket(0,true,Position.Quantity,be,BuildExitSignalName("SL"),eName); originalStopPrice=be; breakEvenApplied=true; }
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
                if (ns>originalStopPrice&&ns<Close[0]) { ExitLongStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
            }
            if (d==-1&&Position.MarketPosition==MarketPosition.Short&&Close[1]<Open[1])
            {
                double ns=Instrument.MasterInstrument.RoundToTickSize(High[1]+SD_SlExtraTicks(sid,d)*TickSize);
                if (ns<originalStopPrice&&ns>Close[0]) { ExitShortStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
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
                if (ns>originalStopPrice&&ns<Close[0]) { ExitLongStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
            }
            else if (d==-1&&Position.MarketPosition==MarketPosition.Short)
            {
                double ns=Instrument.MasterInstrument.RoundToTickSize(GetUpperMAForTrail(sid,d)+SD_TrailOffsetTicks(sid,d)*TickSize);
                if (ns<originalStopPrice&&ns>Close[0]) { ExitShortStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
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
                if (ns>originalStopPrice&&ns<Close[0]) { ExitLongStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
            }
            else if (d==-1&&Position.MarketPosition==MarketPosition.Short)
            {
                double ns=Instrument.MasterInstrument.RoundToTickSize(High[offset]+SD_SlExtraTicks(sid,d)*TickSize);
                if (ns<originalStopPrice&&ns>Close[0]) { ExitShortStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
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
                    if (ns>originalStopPrice&&ns<Close[0]) { ExitLongStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
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
                    if (ns<originalStopPrice&&ns>Close[0]) { ExitShortStopMarket(0,true,Position.Quantity,ns,BuildExitSignalName("SL"),eName); originalStopPrice=ns; }
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
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Session Start Time",Order=1,GroupName="0. Global")] public DateTime SessionStartTime {get;set;}
        [NinjaScriptProperty][Display(Name="Require Entry Confirmation",Order=2,GroupName="0. Global")] public bool RequireEntryConfirmation {get;set;}
        [NinjaScriptProperty][Display(Name="NY Enable (Parent)",Order=3,GroupName="0. Global")] public bool NyEnable {get;set;}
        [NinjaScriptProperty][Display(Name="EU Enable (Parent)",Order=4,GroupName="0. Global")] public bool EuEnable {get;set;}
        [NinjaScriptProperty][Display(Name="AS Enable (Parent)",Order=5,GroupName="0. Global")] public bool AsEnable {get;set;}
        [NinjaScriptProperty][Range(0.0,double.MaxValue)][Display(Name="Max Account Balance",Description="When net liquidation reaches or exceeds this value, entries are blocked and open positions are flattened. 0 disables.",Order=6,GroupName="0. Global")] public double MaxAccountBalance {get;set;}
        [NinjaScriptProperty][Display(Name="Use News Skip",Order=7,GroupName="0. Global")] public bool UseNewsSkip {get;set;}
        [NinjaScriptProperty][Range(0,60)][Display(Name="News Block Minutes",Order=8,GroupName="0. Global")] public int NewsBlockMinutes {get;set;}
        [NinjaScriptProperty][Display(Name="Flatten On Blocked Window",Description="If enabled, flatten when entering a no-new-trades or news-block window.",Order=9,GroupName="0. Global")] public bool FlattenOnBlockedWindowTransition {get;set;}

        // ── Global MA (entry) ─────────────────────────────────────────────────
        [NinjaScriptProperty][Range(1,500)][Display(Name="MA Period",  Order=10,GroupName="0. Global")] public int    GlobalMaPeriod  {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="EMA Period", Order=11,GroupName="0. Global")] public int    GlobalEmaPeriod {get;set;}
        [NinjaScriptProperty][Display(Name="MA Type",              Order=12,GroupName="0. Global")] public MAMode GlobalMaType   {get;set;}
        // ── Global Contracts ─────────────────────────────────────────────────
        [NinjaScriptProperty][Range(1,10)][Display(Name="Contracts",Order=13,GroupName="0. Global")] public int GlobalContracts {get;set;}



        // ── NY-A Session ────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="1a. NY-A Session")] public bool NyAEnable {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="1a. NY-A Session")] public DateTime NyATradeWindowStart {get;set;}
        [NinjaScriptProperty][Display(Name="Enable No-New-Trades After",Order=3,GroupName="1a. NY-A Session")] public bool NyAEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="1a. NY-A Session")] public DateTime NyANoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Forced Close",Order=5,GroupName="1a. NY-A Session")] public bool NyAEnableForcedClose {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="1a. NY-A Session")] public DateTime NyAForcedCloseTime {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="1a. NY-A Session")] public int NyAMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="1a. NY-A Session")] public int NyAMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="1a. NY-A Session")] public int NyAMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="1a. NY-A Session")] public int NyAMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Display(Name="Require Direction Flip",Order=15,GroupName="1a. NY-A Session")] public bool NyARequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="1a. NY-A Session")] public bool NyAAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="1a. NY-A Session")] public int NyAEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="1a. NY-A Session")] public int NyAEngulfingExitPaddingTicks {get;set;}
        // NY-A Long
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="1b. NY-A Long")] public double NyALongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="1b. NY-A Long")] public int NyALongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="1b. NY-A Long")] public int NyALongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="1b. NY-A Long")] public int NyALongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="1b. NY-A Long")] public double NyALongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="1b. NY-A Long")] public bool NyALongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="1b. NY-A Long")] public bool NyALongSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="1b. NY-A Long")] public bool NyALongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="1b. NY-A Long")] public int NyALongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="1b. NY-A Long")] public int NyALongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="1b. NY-A Long")] public int NyALongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="1b. NY-A Long")] public bool NyALongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="1b. NY-A Long")] public int NyALongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="1b. NY-A Long")] public int NyALongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="1b. NY-A Long")] public int NyALongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="1b. NY-A Long")] public bool NyALongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="1b. NY-A Long")] public int NyALongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="1b. NY-A Long")] public bool NyALongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="1b. NY-A Long")] public int NyALongWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="1b. NY-A Long")] public double NyALongMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="1b. NY-A Long")] public int NyALongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="1b. NY-A Long")] public bool NyALongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="1b. NY-A Long")] public int NyALongAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="1b. NY-A Long")] public int NyALongAdxMinLevel {get;set;}
        // NY-A Short
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="1c. NY-A Short")] public double NyAShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="1c. NY-A Short")] public int NyAShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="1c. NY-A Short")] public int NyAShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="1c. NY-A Short")] public int NyAShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="1c. NY-A Short")] public double NyAShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="1c. NY-A Short")] public bool NyAShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="1c. NY-A Short")] public bool NyAShortSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="1c. NY-A Short")] public bool NyAShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="1c. NY-A Short")] public int NyAShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="1c. NY-A Short")] public int NyAShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="1c. NY-A Short")] public int NyAShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="1c. NY-A Short")] public bool NyAShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="1c. NY-A Short")] public int NyAShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="1c. NY-A Short")] public int NyAShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="1c. NY-A Short")] public int NyAShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="1c. NY-A Short")] public bool NyAShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="1c. NY-A Short")] public int NyAShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="1c. NY-A Short")] public bool NyAShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="1c. NY-A Short")] public int NyAShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="1c. NY-A Short")] public double NyAShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="1c. NY-A Short")] public int NyAShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="1c. NY-A Short")] public bool NyAShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="1c. NY-A Short")] public int NyAShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="1c. NY-A Short")] public int NyAShortAdxMinLevel {get;set;}

        // ── NY-B ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="2a. NY-B Session")] public bool NyBEnable {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="2a. NY-B Session")] public DateTime NyBTradeWindowStart {get;set;}
        [NinjaScriptProperty][Display(Name="Enable No-New-Trades After",Order=3,GroupName="2a. NY-B Session")] public bool NyBEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="2a. NY-B Session")] public DateTime NyBNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Forced Close",Order=5,GroupName="2a. NY-B Session")] public bool NyBEnableForcedClose {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="2a. NY-B Session")] public DateTime NyBForcedCloseTime {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="2a. NY-B Session")] public int NyBMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="2a. NY-B Session")] public int NyBMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="2a. NY-B Session")] public int NyBMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="2a. NY-B Session")] public int NyBMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Display(Name="Require Direction Flip",Order=15,GroupName="2a. NY-B Session")] public bool NyBRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="2a. NY-B Session")] public bool NyBAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="2a. NY-B Session")] public int NyBEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="2a. NY-B Session")] public int NyBEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="2b. NY-B Long")] public double NyBLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="2b. NY-B Long")] public int NyBLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="2b. NY-B Long")] public int NyBLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="2b. NY-B Long")] public int NyBLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="2b. NY-B Long")] public double NyBLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="2b. NY-B Long")] public bool NyBLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="2b. NY-B Long")] public bool NyBLongSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="2b. NY-B Long")] public bool NyBLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="2b. NY-B Long")] public int NyBLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="2b. NY-B Long")] public int NyBLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="2b. NY-B Long")] public int NyBLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="2b. NY-B Long")] public bool NyBLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="2b. NY-B Long")] public int NyBLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="2b. NY-B Long")] public int NyBLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="2b. NY-B Long")] public int NyBLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="2b. NY-B Long")] public bool NyBLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="2b. NY-B Long")] public int NyBLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="2b. NY-B Long")] public bool NyBLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="2b. NY-B Long")] public int NyBLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="2b. NY-B Long")] public double NyBLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="2b. NY-B Long")] public int NyBLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="2b. NY-B Long")] public bool NyBLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="2b. NY-B Long")] public int NyBLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="2b. NY-B Long")] public int NyBLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="2c. NY-B Short")] public double NyBShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="2c. NY-B Short")] public int NyBShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="2c. NY-B Short")] public int NyBShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="2c. NY-B Short")] public int NyBShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="2c. NY-B Short")] public double NyBShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="2c. NY-B Short")] public bool NyBShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="2c. NY-B Short")] public bool NyBShortSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="2c. NY-B Short")] public bool NyBShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="2c. NY-B Short")] public int NyBShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="2c. NY-B Short")] public int NyBShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="2c. NY-B Short")] public int NyBShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="2c. NY-B Short")] public bool NyBShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="2c. NY-B Short")] public int NyBShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="2c. NY-B Short")] public int NyBShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="2c. NY-B Short")] public int NyBShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="2c. NY-B Short")] public bool NyBShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="2c. NY-B Short")] public int NyBShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="2c. NY-B Short")] public bool NyBShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="2c. NY-B Short")] public int NyBShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="2c. NY-B Short")] public double NyBShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="2c. NY-B Short")] public int NyBShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="2c. NY-B Short")] public bool NyBShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="2c. NY-B Short")] public int NyBShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="2c. NY-B Short")] public int NyBShortAdxMinLevel {get;set;}
        // ── NY-C ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="3a. NY-C Session")] public bool NyCEnable {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="3a. NY-C Session")] public DateTime NyCTradeWindowStart {get;set;}
        [NinjaScriptProperty][Display(Name="Enable No-New-Trades After",Order=3,GroupName="3a. NY-C Session")] public bool NyCEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="3a. NY-C Session")] public DateTime NyCNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Forced Close",Order=5,GroupName="3a. NY-C Session")] public bool NyCEnableForcedClose {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="3a. NY-C Session")] public DateTime NyCForcedCloseTime {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="3a. NY-C Session")] public int NyCMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="3a. NY-C Session")] public int NyCMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="3a. NY-C Session")] public int NyCMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="3a. NY-C Session")] public int NyCMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Display(Name="Require Direction Flip",Order=15,GroupName="3a. NY-C Session")] public bool NyCRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="3a. NY-C Session")] public bool NyCAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="3a. NY-C Session")] public int NyCEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="3a. NY-C Session")] public int NyCEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="3b. NY-C Long")] public double NyCLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="3b. NY-C Long")] public int NyCLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="3b. NY-C Long")] public int NyCLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="3b. NY-C Long")] public int NyCLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="3b. NY-C Long")] public double NyCLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="3b. NY-C Long")] public bool NyCLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="3b. NY-C Long")] public bool NyCLongSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="3b. NY-C Long")] public bool NyCLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="3b. NY-C Long")] public int NyCLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="3b. NY-C Long")] public int NyCLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="3b. NY-C Long")] public int NyCLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="3b. NY-C Long")] public bool NyCLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="3b. NY-C Long")] public int NyCLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="3b. NY-C Long")] public int NyCLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="3b. NY-C Long")] public int NyCLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="3b. NY-C Long")] public bool NyCLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="3b. NY-C Long")] public int NyCLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="3b. NY-C Long")] public bool NyCLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="3b. NY-C Long")] public int NyCLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="3b. NY-C Long")] public double NyCLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="3b. NY-C Long")] public int NyCLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="3b. NY-C Long")] public bool NyCLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="3b. NY-C Long")] public int NyCLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="3b. NY-C Long")] public int NyCLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="3c. NY-C Short")] public double NyCShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="3c. NY-C Short")] public int NyCShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="3c. NY-C Short")] public int NyCShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="3c. NY-C Short")] public int NyCShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="3c. NY-C Short")] public double NyCShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="3c. NY-C Short")] public bool NyCShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="3c. NY-C Short")] public bool NyCShortSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="3c. NY-C Short")] public bool NyCShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="3c. NY-C Short")] public int NyCShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="3c. NY-C Short")] public int NyCShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="3c. NY-C Short")] public int NyCShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="3c. NY-C Short")] public bool NyCShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="3c. NY-C Short")] public int NyCShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="3c. NY-C Short")] public int NyCShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="3c. NY-C Short")] public int NyCShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="3c. NY-C Short")] public bool NyCShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="3c. NY-C Short")] public int NyCShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="3c. NY-C Short")] public bool NyCShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="3c. NY-C Short")] public int NyCShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="3c. NY-C Short")] public double NyCShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="3c. NY-C Short")] public int NyCShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="3c. NY-C Short")] public bool NyCShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="3c. NY-C Short")] public int NyCShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="3c. NY-C Short")] public int NyCShortAdxMinLevel {get;set;}
        // ── EU-A ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="4a. EU-A Session")] public bool EuAEnable {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="4a. EU-A Session")] public DateTime EuATradeWindowStart {get;set;}
        [NinjaScriptProperty][Display(Name="Enable No-New-Trades After",Order=3,GroupName="4a. EU-A Session")] public bool EuAEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="4a. EU-A Session")] public DateTime EuANoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Forced Close",Order=5,GroupName="4a. EU-A Session")] public bool EuAEnableForcedClose {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="4a. EU-A Session")] public DateTime EuAForcedCloseTime {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="4a. EU-A Session")] public int EuAMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="4a. EU-A Session")] public int EuAMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="4a. EU-A Session")] public int EuAMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="4a. EU-A Session")] public int EuAMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Display(Name="Require Direction Flip",Order=15,GroupName="4a. EU-A Session")] public bool EuARequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="4a. EU-A Session")] public bool EuAAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="4a. EU-A Session")] public int EuAEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="4a. EU-A Session")] public int EuAEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="4b. EU-A Long")] public double EuALongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="4b. EU-A Long")] public int EuALongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="4b. EU-A Long")] public int EuALongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="4b. EU-A Long")] public int EuALongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="4b. EU-A Long")] public double EuALongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="4b. EU-A Long")] public bool EuALongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="4b. EU-A Long")] public bool EuALongSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="4b. EU-A Long")] public bool EuALongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="4b. EU-A Long")] public int EuALongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="4b. EU-A Long")] public int EuALongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="4b. EU-A Long")] public int EuALongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="4b. EU-A Long")] public bool EuALongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="4b. EU-A Long")] public int EuALongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="4b. EU-A Long")] public int EuALongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="4b. EU-A Long")] public int EuALongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="4b. EU-A Long")] public bool EuALongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="4b. EU-A Long")] public int EuALongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="4b. EU-A Long")] public bool EuALongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="4b. EU-A Long")] public int EuALongWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="4b. EU-A Long")] public double EuALongMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="4b. EU-A Long")] public int EuALongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="4b. EU-A Long")] public bool EuALongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="4b. EU-A Long")] public int EuALongAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="4b. EU-A Long")] public int EuALongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="4c. EU-A Short")] public double EuAShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="4c. EU-A Short")] public int EuAShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="4c. EU-A Short")] public int EuAShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="4c. EU-A Short")] public int EuAShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="4c. EU-A Short")] public double EuAShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="4c. EU-A Short")] public bool EuAShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="4c. EU-A Short")] public bool EuAShortSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="4c. EU-A Short")] public bool EuAShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="4c. EU-A Short")] public int EuAShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="4c. EU-A Short")] public int EuAShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="4c. EU-A Short")] public int EuAShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="4c. EU-A Short")] public bool EuAShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="4c. EU-A Short")] public int EuAShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="4c. EU-A Short")] public int EuAShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="4c. EU-A Short")] public int EuAShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="4c. EU-A Short")] public bool EuAShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="4c. EU-A Short")] public int EuAShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="4c. EU-A Short")] public bool EuAShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="4c. EU-A Short")] public int EuAShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="4c. EU-A Short")] public double EuAShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="4c. EU-A Short")] public int EuAShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="4c. EU-A Short")] public bool EuAShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="4c. EU-A Short")] public int EuAShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="4c. EU-A Short")] public int EuAShortAdxMinLevel {get;set;}
        // ── EU-B ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="5a. EU-B Session")] public bool EuBEnable {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="5a. EU-B Session")] public DateTime EuBTradeWindowStart {get;set;}
        [NinjaScriptProperty][Display(Name="Enable No-New-Trades After",Order=3,GroupName="5a. EU-B Session")] public bool EuBEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="5a. EU-B Session")] public DateTime EuBNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Forced Close",Order=5,GroupName="5a. EU-B Session")] public bool EuBEnableForcedClose {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="5a. EU-B Session")] public DateTime EuBForcedCloseTime {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="5a. EU-B Session")] public int EuBMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="5a. EU-B Session")] public int EuBMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="5a. EU-B Session")] public int EuBMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="5a. EU-B Session")] public int EuBMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Display(Name="Require Direction Flip",Order=15,GroupName="5a. EU-B Session")] public bool EuBRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="5a. EU-B Session")] public bool EuBAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="5a. EU-B Session")] public int EuBEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="5a. EU-B Session")] public int EuBEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="5b. EU-B Long")] public double EuBLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="5b. EU-B Long")] public int EuBLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="5b. EU-B Long")] public int EuBLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="5b. EU-B Long")] public int EuBLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="5b. EU-B Long")] public double EuBLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="5b. EU-B Long")] public bool EuBLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="5b. EU-B Long")] public bool EuBLongSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="5b. EU-B Long")] public bool EuBLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="5b. EU-B Long")] public int EuBLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="5b. EU-B Long")] public int EuBLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="5b. EU-B Long")] public int EuBLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="5b. EU-B Long")] public bool EuBLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="5b. EU-B Long")] public int EuBLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="5b. EU-B Long")] public int EuBLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="5b. EU-B Long")] public int EuBLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="5b. EU-B Long")] public bool EuBLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="5b. EU-B Long")] public int EuBLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="5b. EU-B Long")] public bool EuBLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="5b. EU-B Long")] public int EuBLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="5b. EU-B Long")] public double EuBLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="5b. EU-B Long")] public int EuBLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="5b. EU-B Long")] public bool EuBLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="5b. EU-B Long")] public int EuBLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="5b. EU-B Long")] public int EuBLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="5c. EU-B Short")] public double EuBShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="5c. EU-B Short")] public int EuBShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="5c. EU-B Short")] public int EuBShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="5c. EU-B Short")] public int EuBShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="5c. EU-B Short")] public double EuBShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="5c. EU-B Short")] public bool EuBShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="5c. EU-B Short")] public bool EuBShortSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="5c. EU-B Short")] public bool EuBShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="5c. EU-B Short")] public int EuBShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="5c. EU-B Short")] public int EuBShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="5c. EU-B Short")] public int EuBShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="5c. EU-B Short")] public bool EuBShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="5c. EU-B Short")] public int EuBShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="5c. EU-B Short")] public int EuBShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="5c. EU-B Short")] public int EuBShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="5c. EU-B Short")] public bool EuBShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="5c. EU-B Short")] public int EuBShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="5c. EU-B Short")] public bool EuBShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="5c. EU-B Short")] public int EuBShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="5c. EU-B Short")] public double EuBShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="5c. EU-B Short")] public int EuBShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="5c. EU-B Short")] public bool EuBShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="5c. EU-B Short")] public int EuBShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="5c. EU-B Short")] public int EuBShortAdxMinLevel {get;set;}
        // ── EU-C ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="6a. EU-C Session")] public bool EuCEnable {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="6a. EU-C Session")] public DateTime EuCTradeWindowStart {get;set;}
        [NinjaScriptProperty][Display(Name="Enable No-New-Trades After",Order=3,GroupName="6a. EU-C Session")] public bool EuCEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="6a. EU-C Session")] public DateTime EuCNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Forced Close",Order=5,GroupName="6a. EU-C Session")] public bool EuCEnableForcedClose {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="6a. EU-C Session")] public DateTime EuCForcedCloseTime {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="6a. EU-C Session")] public int EuCMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="6a. EU-C Session")] public int EuCMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="6a. EU-C Session")] public int EuCMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="6a. EU-C Session")] public int EuCMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Display(Name="Require Direction Flip",Order=15,GroupName="6a. EU-C Session")] public bool EuCRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="6a. EU-C Session")] public bool EuCAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="6a. EU-C Session")] public int EuCEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="6a. EU-C Session")] public int EuCEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="6b. EU-C Long")] public double EuCLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="6b. EU-C Long")] public int EuCLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="6b. EU-C Long")] public int EuCLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="6b. EU-C Long")] public int EuCLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="6b. EU-C Long")] public double EuCLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="6b. EU-C Long")] public bool EuCLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="6b. EU-C Long")] public bool EuCLongSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="6b. EU-C Long")] public bool EuCLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="6b. EU-C Long")] public int EuCLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="6b. EU-C Long")] public int EuCLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="6b. EU-C Long")] public int EuCLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="6b. EU-C Long")] public bool EuCLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="6b. EU-C Long")] public int EuCLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="6b. EU-C Long")] public int EuCLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="6b. EU-C Long")] public int EuCLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="6b. EU-C Long")] public bool EuCLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="6b. EU-C Long")] public int EuCLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="6b. EU-C Long")] public bool EuCLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="6b. EU-C Long")] public int EuCLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="6b. EU-C Long")] public double EuCLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="6b. EU-C Long")] public int EuCLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="6b. EU-C Long")] public bool EuCLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="6b. EU-C Long")] public int EuCLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="6b. EU-C Long")] public int EuCLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="6c. EU-C Short")] public double EuCShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="6c. EU-C Short")] public int EuCShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="6c. EU-C Short")] public int EuCShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="6c. EU-C Short")] public int EuCShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="6c. EU-C Short")] public double EuCShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="6c. EU-C Short")] public bool EuCShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="6c. EU-C Short")] public bool EuCShortSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="6c. EU-C Short")] public bool EuCShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="6c. EU-C Short")] public int EuCShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="6c. EU-C Short")] public int EuCShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="6c. EU-C Short")] public int EuCShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="6c. EU-C Short")] public bool EuCShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="6c. EU-C Short")] public int EuCShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="6c. EU-C Short")] public int EuCShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="6c. EU-C Short")] public int EuCShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="6c. EU-C Short")] public bool EuCShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="6c. EU-C Short")] public int EuCShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="6c. EU-C Short")] public bool EuCShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="6c. EU-C Short")] public int EuCShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="6c. EU-C Short")] public double EuCShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="6c. EU-C Short")] public int EuCShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="6c. EU-C Short")] public bool EuCShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="6c. EU-C Short")] public int EuCShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="6c. EU-C Short")] public int EuCShortAdxMinLevel {get;set;}
        // ── AS-A ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="7a. AS-A Session")] public bool AsAEnable {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="7a. AS-A Session")] public DateTime AsATradeWindowStart {get;set;}
        [NinjaScriptProperty][Display(Name="Enable No-New-Trades After",Order=3,GroupName="7a. AS-A Session")] public bool AsAEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="7a. AS-A Session")] public DateTime AsANoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Forced Close",Order=5,GroupName="7a. AS-A Session")] public bool AsAEnableForcedClose {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="7a. AS-A Session")] public DateTime AsAForcedCloseTime {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="7a. AS-A Session")] public int AsAMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="7a. AS-A Session")] public int AsAMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="7a. AS-A Session")] public int AsAMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="7a. AS-A Session")] public int AsAMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Display(Name="Require Direction Flip",Order=15,GroupName="7a. AS-A Session")] public bool AsARequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="7a. AS-A Session")] public bool AsAAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="7a. AS-A Session")] public int AsAEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="7a. AS-A Session")] public int AsAEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="7b. AS-A Long")] public double AsALongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="7b. AS-A Long")] public int AsALongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="7b. AS-A Long")] public int AsALongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="7b. AS-A Long")] public int AsALongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="7b. AS-A Long")] public double AsALongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="7b. AS-A Long")] public bool AsALongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="7b. AS-A Long")] public bool AsALongSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="7b. AS-A Long")] public bool AsALongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="7b. AS-A Long")] public int AsALongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="7b. AS-A Long")] public int AsALongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="7b. AS-A Long")] public int AsALongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="7b. AS-A Long")] public bool AsALongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="7b. AS-A Long")] public int AsALongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="7b. AS-A Long")] public int AsALongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="7b. AS-A Long")] public int AsALongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="7b. AS-A Long")] public bool AsALongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="7b. AS-A Long")] public int AsALongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="7b. AS-A Long")] public bool AsALongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="7b. AS-A Long")] public int AsALongWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="7b. AS-A Long")] public double AsALongMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="7b. AS-A Long")] public int AsALongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="7b. AS-A Long")] public bool AsALongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="7b. AS-A Long")] public int AsALongAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="7b. AS-A Long")] public int AsALongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="7c. AS-A Short")] public double AsAShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="7c. AS-A Short")] public int AsAShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="7c. AS-A Short")] public int AsAShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="7c. AS-A Short")] public int AsAShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="7c. AS-A Short")] public double AsAShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="7c. AS-A Short")] public bool AsAShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="7c. AS-A Short")] public bool AsAShortSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="7c. AS-A Short")] public bool AsAShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="7c. AS-A Short")] public int AsAShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="7c. AS-A Short")] public int AsAShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="7c. AS-A Short")] public int AsAShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="7c. AS-A Short")] public bool AsAShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="7c. AS-A Short")] public int AsAShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="7c. AS-A Short")] public int AsAShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="7c. AS-A Short")] public int AsAShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="7c. AS-A Short")] public bool AsAShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="7c. AS-A Short")] public int AsAShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="7c. AS-A Short")] public bool AsAShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="7c. AS-A Short")] public int AsAShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="7c. AS-A Short")] public double AsAShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="7c. AS-A Short")] public int AsAShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="7c. AS-A Short")] public bool AsAShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="7c. AS-A Short")] public int AsAShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="7c. AS-A Short")] public int AsAShortAdxMinLevel {get;set;}
        // ── AS-B ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="8a. AS-B Session")] public bool AsBEnable {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="8a. AS-B Session")] public DateTime AsBTradeWindowStart {get;set;}
        [NinjaScriptProperty][Display(Name="Enable No-New-Trades After",Order=3,GroupName="8a. AS-B Session")] public bool AsBEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="8a. AS-B Session")] public DateTime AsBNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Forced Close",Order=5,GroupName="8a. AS-B Session")] public bool AsBEnableForcedClose {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="8a. AS-B Session")] public DateTime AsBForcedCloseTime {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="8a. AS-B Session")] public int AsBMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="8a. AS-B Session")] public int AsBMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="8a. AS-B Session")] public int AsBMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="8a. AS-B Session")] public int AsBMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Display(Name="Require Direction Flip",Order=15,GroupName="8a. AS-B Session")] public bool AsBRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="8a. AS-B Session")] public bool AsBAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="8a. AS-B Session")] public int AsBEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="8a. AS-B Session")] public int AsBEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="8b. AS-B Long")] public double AsBLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="8b. AS-B Long")] public int AsBLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="8b. AS-B Long")] public int AsBLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="8b. AS-B Long")] public int AsBLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="8b. AS-B Long")] public double AsBLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="8b. AS-B Long")] public bool AsBLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="8b. AS-B Long")] public bool AsBLongSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="8b. AS-B Long")] public bool AsBLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="8b. AS-B Long")] public int AsBLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="8b. AS-B Long")] public int AsBLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="8b. AS-B Long")] public int AsBLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="8b. AS-B Long")] public bool AsBLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="8b. AS-B Long")] public int AsBLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="8b. AS-B Long")] public int AsBLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="8b. AS-B Long")] public int AsBLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="8b. AS-B Long")] public bool AsBLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="8b. AS-B Long")] public int AsBLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="8b. AS-B Long")] public bool AsBLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="8b. AS-B Long")] public int AsBLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="8b. AS-B Long")] public double AsBLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="8b. AS-B Long")] public int AsBLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="8b. AS-B Long")] public bool AsBLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="8b. AS-B Long")] public int AsBLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="8b. AS-B Long")] public int AsBLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="8c. AS-B Short")] public double AsBShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="8c. AS-B Short")] public int AsBShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="8c. AS-B Short")] public int AsBShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="8c. AS-B Short")] public int AsBShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="8c. AS-B Short")] public double AsBShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="8c. AS-B Short")] public bool AsBShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="8c. AS-B Short")] public bool AsBShortSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="8c. AS-B Short")] public bool AsBShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="8c. AS-B Short")] public int AsBShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="8c. AS-B Short")] public int AsBShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="8c. AS-B Short")] public int AsBShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="8c. AS-B Short")] public bool AsBShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="8c. AS-B Short")] public int AsBShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="8c. AS-B Short")] public int AsBShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="8c. AS-B Short")] public int AsBShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="8c. AS-B Short")] public bool AsBShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="8c. AS-B Short")] public int AsBShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="8c. AS-B Short")] public bool AsBShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="8c. AS-B Short")] public int AsBShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="8c. AS-B Short")] public double AsBShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="8c. AS-B Short")] public int AsBShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="8c. AS-B Short")] public bool AsBShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="8c. AS-B Short")] public int AsBShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="8c. AS-B Short")] public int AsBShortAdxMinLevel {get;set;}
        // ── AS-C ────────────────────────────────────────────────────────────────
        [NinjaScriptProperty][Display(Name="Enable",Order=1,GroupName="9a. AS-C Session")] public bool AsCEnable {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Trade Window Start",Order=2,GroupName="9a. AS-C Session")] public DateTime AsCTradeWindowStart {get;set;}
        [NinjaScriptProperty][Display(Name="Enable No-New-Trades After",Order=3,GroupName="9a. AS-C Session")] public bool AsCEnableNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="No New Trades After",Order=4,GroupName="9a. AS-C Session")] public DateTime AsCNoNewTradesAfter {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Forced Close",Order=5,GroupName="9a. AS-C Session")] public bool AsCEnableForcedClose {get;set;}
        [NinjaScriptProperty][PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")][Display(Name="Forced Close Time",Order=6,GroupName="9a. AS-C Session")] public DateTime AsCForcedCloseTime {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Profit Ticks",Order=10,GroupName="9a. AS-C Session")] public int AsCMaxSessionProfitTicks {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="Max Loss Ticks",Order=11,GroupName="9a. AS-C Session")] public int AsCMaxSessionLossTicks {get;set;}
                [NinjaScriptProperty][Range(1,20)][Display(Name="Max Trades",Order=8,GroupName="9a. AS-C Session")] public int AsCMaxTradesPerSession {get;set;}
        [NinjaScriptProperty][Range(0,20)][Display(Name="Max Losses",Order=9,GroupName="9a. AS-C Session")] public int AsCMaxLossesPerSession {get;set;}
        [NinjaScriptProperty][Display(Name="Require Direction Flip",Order=15,GroupName="9a. AS-C Session")] public bool AsCRequireDirectionFlip {get;set;}
        [NinjaScriptProperty][Display(Name="Allow Same Dir After Loss",Order=16,GroupName="9a. AS-C Session")] public bool AsCAllowSameDirectionAfterLoss {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Engulfing Exit After Bars",Order=17,GroupName="9a. AS-C Session")] public int AsCEngulfingExitAfterBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Engulfing Exit Padding Ticks",Order=18,GroupName="9a. AS-C Session")] public int AsCEngulfingExitPaddingTicks {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="9b. AS-C Long")] public double AsCLongCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="9b. AS-C Long")] public int AsCLongMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="9b. AS-C Long")] public int AsCLongSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="9b. AS-C Long")] public int AsCLongMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="9b. AS-C Long")] public double AsCLongMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="9b. AS-C Long")] public bool AsCLongUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="9b. AS-C Long")] public bool AsCLongSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="9b. AS-C Long")] public bool AsCLongMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="9b. AS-C Long")] public int AsCLongTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="9b. AS-C Long")] public int AsCLongTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="9b. AS-C Long")] public int AsCLongTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="9b. AS-C Long")] public bool AsCLongEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="9b. AS-C Long")] public int AsCLongBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="9b. AS-C Long")] public int AsCLongBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="9b. AS-C Long")] public int AsCLongMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="9b. AS-C Long")] public bool AsCLongEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="9b. AS-C Long")] public int AsCLongPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="9b. AS-C Long")] public bool AsCLongEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="9b. AS-C Long")] public int AsCLongWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="9b. AS-C Long")] public double AsCLongMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="9b. AS-C Long")] public int AsCLongTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="9b. AS-C Long")] public bool AsCLongEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="9b. AS-C Long")] public int AsCLongAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="9b. AS-C Long")] public int AsCLongAdxMinLevel {get;set;}
        [NinjaScriptProperty][Range(0.01,100)][Display(Name="Candle Multiplier",Order=4,GroupName="9c. AS-C Short")] public double AsCShortCandleMultiplier {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max TP Ticks",Order=5,GroupName="9c. AS-C Short")] public int AsCShortMaxTakeProfitTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="SL Extra Ticks",Order=6,GroupName="9c. AS-C Short")] public int AsCShortSlExtraTicks {get;set;}
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Max SL Ticks",Order=7,GroupName="9c. AS-C Short")] public int AsCShortMaxSlTicks {get;set;}
        [NinjaScriptProperty][Range(0,10)][Display(Name="Max SL:TP Ratio",Order=8,GroupName="9c. AS-C Short")] public double AsCShortMaxSlToTpRatio {get;set;}
        [NinjaScriptProperty][Display(Name="Use Prior Candle SL",Order=9,GroupName="9c. AS-C Short")] public bool AsCShortUsePriorCandleSl {get;set;}
        [NinjaScriptProperty][Display(Name="SL At MA",Order=10,GroupName="9c. AS-C Short")] public bool AsCShortSlAtMa {get;set;}
        [NinjaScriptProperty][Display(Name="Move SL To Entry Bar",Order=11,GroupName="9c. AS-C Short")] public bool AsCShortMoveSlToEntryBar {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="Trail Offset Ticks",Order=12,GroupName="9c. AS-C Short")] public int AsCShortTrailOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Trail Delay Bars",Order=13,GroupName="9c. AS-C Short")] public int AsCShortTrailDelayBars {get;set;}
        [NinjaScriptProperty][Range(0,50)][Display(Name="Trail Candle Offset",Order=14,GroupName="9c. AS-C Short")] public int AsCShortTrailCandleOffset {get;set;}
        [NinjaScriptProperty][Display(Name="Enable Breakeven",Order=15,GroupName="9c. AS-C Short")] public bool AsCShortEnableBreakeven {get;set;}
        [NinjaScriptProperty][Range(1,9999)][Display(Name="BE Trigger Ticks",Order=17,GroupName="9c. AS-C Short")] public int AsCShortBreakevenTriggerTicks {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="BE Offset Ticks",Order=19,GroupName="9c. AS-C Short")] public int AsCShortBreakevenOffsetTicks {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Max Bars In Trade",Order=20,GroupName="9c. AS-C Short")] public int AsCShortMaxBarsInTrade {get;set;}
        [NinjaScriptProperty][Display(Name="Enable PriceOffset Trail",Order=21,GroupName="9c. AS-C Short")] public bool AsCShortEnablePriceOffsetTrail {get;set;}
        [NinjaScriptProperty][Range(0,999)][Display(Name="PriceOffset Reduction",Order=22,GroupName="9c. AS-C Short")] public int AsCShortPriceOffsetReductionTicks {get;set;}
        [NinjaScriptProperty][Display(Name="Enable WMA Filter",Order=29,GroupName="9c. AS-C Short")] public bool AsCShortEnableWmaFilter {get;set;}
        [NinjaScriptProperty][Range(1,500)][Display(Name="WMA Period",Order=30,GroupName="9c. AS-C Short")] public int AsCShortWmaPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Min Body %",Order=32,GroupName="9c. AS-C Short")] public double AsCShortMinBodyPct {get;set;}
        [NinjaScriptProperty][Range(0,200)][Display(Name="Trend Confirm Bars",Order=33,GroupName="9c. AS-C Short")] public int AsCShortTrendConfirmBars {get;set;}
        [NinjaScriptProperty][Display(Name="Enable ADX Filter",Order=35,GroupName="9c. AS-C Short")] public bool AsCShortEnableAdxFilter {get;set;}
        [NinjaScriptProperty][Range(1,100)][Display(Name="ADX Period",Order=36,GroupName="9c. AS-C Short")] public int AsCShortAdxPeriod {get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="ADX Min Level",Order=37,GroupName="9c. AS-C Short")] public int AsCShortAdxMinLevel {get;set;}
        #endregion
    }

    #region Enums
    public enum MAMode           { SMA, EMA, Both }
    public enum MichalEntryMode  { Market, LimitOffset, LimitRetracement }
    public enum MichalTPMode     { FixedTicks, SwingPoint, CandleMultiple }
    public enum BEMode2          { FixedTicks, CandlePercent }
    #endregion
}
