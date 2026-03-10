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
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class MICHTesting : Strategy
    {
        public MICHTesting()
        {
            VendorLicense(1235);
        }
        
        #region Private Variables

        // ── Long-side indicators ─────────────────────────────────────────────
        private SMA longSmaHigh;
        private SMA longSmaLow;
        private EMA longEmaHigh;
        private EMA longEmaLow;
        private WMA longWmaFilter;
        private ADX longAdxIndicator;
        private Swing longSwing;
        private EMA longEarlyExitEma;
        private ADX longEarlyExitAdx;

        // ── Short-side indicators ────────────────────────────────────────────
        private SMA shortSmaHigh;
        private SMA shortSmaLow;
        private EMA shortEmaHigh;
        private EMA shortEmaLow;
        private WMA shortWmaFilter;
        private ADX shortAdxIndicator;
        private Swing shortSwing;
        private EMA shortEarlyExitEma;
        private ADX shortEarlyExitAdx;

        // ── Session tracking ─────────────────────────────────────────────────
        private int sessionTradeCount;
        private int sessionWinCount;
        private int sessionLossCount;
        private double sessionPnLTicks;
        private bool sessionLimitsReached;
        private DateTime currentSessionDate;

        // ── Trade state ──────────────────────────────────────────────────────
        private int tradeDirection;         // 1 = long, -1 = short, 0 = flat
        private double signalCandleRange;
        private double priorCandleHigh;
        private double priorCandleLow;
        private double originalStopPrice;
        private double tradeEntryPrice;
        private bool hasActivePosition;

        // ── Direction lock ───────────────────────────────────────────────────
        private int lastTradeDirection;
        private bool lastTradeWasLoss;

        // ── Trailing SL delay ────────────────────────────────────────────────
        private int barsSinceEntry;

        // ── Breakeven tracking ───────────────────────────────────────────────
        private bool breakEvenApplied;

        // ── Max time in trade ────────────────────────────────────────────────
        private DateTime tradeEntryTime;

        // ── Opposing bar exit ────────────────────────────────────────────────
        private double opposingBarBenchmark;
        private bool opposingBarBenchmarkSet;

        // ── Price-offset trailing ────────────────────────────────────────────
        private bool priceOffsetTrailActive;
        private double priceOffsetTrailDistance;

        // ── Entry candle SL ──────────────────────────────────────────────────
        private bool entryBarSlApplied;

        // ── Position change detection ────────────────────────────────────────
        private MarketPosition prevMarketPosition;

        // ── Order tracking ───────────────────────────────────────────────────
        private Order entryOrder;

        // ── ADX peak tracking (early exit) ───────────────────────────────────
        private double tradePeakAdx;

        // ── Time-window transition tracking ──────────────────────────────────
        private bool wasInNoTradesAfterWindow;
        private bool wasInSkipWindow;
        private bool wasInNewsSkipWindow;

        // ── News skip ────────────────────────────────────────────────────────
        private static readonly string NewsDatesRaw =
@"2025-01-08,14:00
2025-01-29,14:00
2025-02-19,14:00
2025-03-19,14:00
2025-04-09,14:00
2025-05-07,14:00
2025-05-28,14:00
2025-06-18,14:00
2025-07-09,14:00
2025-07-30,14:00
2025-08-20,14:00
2025-09-17,14:00
2025-10-08,14:00
2025-10-29,14:00
2025-11-19,14:00
2025-12-10,14:00
2025-12-30,14:00
2026-01-28,14:00
2026-02-18,14:00
2026-03-18,14:00
2026-04-08,14:00
2026-04-29,14:00
2026-05-20,14:00
2026-06-17,14:00
2026-07-08,14:00
2026-07-29,14:00
2026-08-19,14:00
2026-09-16,14:00
2026-10-07,14:00
2026-10-28,14:00
2026-11-18,14:00
2026-12-09,14:00
2026-12-30,14:00";
        private static readonly List<DateTime> NewsDates = new List<DateTime>();
        private static bool newsDatesInitialized;

        // ── Info box overlay ─────────────────────────────────────────────────
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

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                     = @"MICHTesting";
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

                // ─── 0. General ───
                NumberOfContracts             = 1;
                EnableLongTrades                = true;
                EnableShortTrades               = true;
                RequireEntryConfirmation        = false;

                // ─── 1. Common: Session Management ───
                SessionStartTime                = DateTime.Parse("18:00", System.Globalization.CultureInfo.InvariantCulture);
                TradeWindowStart                = DateTime.Parse("09:42", System.Globalization.CultureInfo.InvariantCulture);
                SkipTimeStart                   = DateTime.Parse("11:45", System.Globalization.CultureInfo.InvariantCulture);
                SkipTimeEnd                     = DateTime.Parse("13:05", System.Globalization.CultureInfo.InvariantCulture);
                NoNewTradesAfter                = DateTime.Parse("15:00", System.Globalization.CultureInfo.InvariantCulture);
                ForcedCloseTime                 = DateTime.Parse("15:50", System.Globalization.CultureInfo.InvariantCulture);
                UseNewsSkip                     = true;
                NewsBlockMinutes                = 1;

                // ─── 2. Common: Session Limits ───
                MaxTradesPerSession             = 4;
                MaxWinsPerSession               = 0;
                MaxLossesPerSession             = 2;
                MaxSessionProfitTicks           = 1075;
                MaxSessionLossTicks             = 160;

                // ════════════════════════════════════════
                //  LONG PARAMETERS
                // ════════════════════════════════════════

                // ─── 3. Long: MA Settings ───
                LongMaPeriod                    = 47;
                LongEmaPeriod                   = 35;
                LongMaType                      = MAMode.Both;

                // ─── 4. Long: Entry ───
                LongEntryMode                   = MichalEntryMode.Market;
                LongLimitOffsetTicks            = 1;
                LongLimitRetracementPct         = 1.0;

                // ─── 5. Long: Take Profit ───
                LongTakeProfitMode              = MichalTPMode.CandleMultiple;
                LongTakeProfitTicks             = 310;
                LongMaxTakeProfitTicks          = 1179;
                LongSwingStrength               = 1;
                LongCandleMultiplier            = 4.3;

                // ─── 6. Long: Stop Loss ───
                LongSlExtraTicks                = 42;
                LongTrailOffsetTicks            = 41;
                LongTrailDelayBars              = 2;
                LongMaxSlTicks                  = 331;
                LongMaxSlToTpRatio              = 0.47;
                LongUsePriorCandleSl            = true;
                LongSlAtMa                      = false;
                LongMoveSlToEntryBar            = false;
                LongTrailCandleOffset           = 19;

                // ─── 7. Long: Breakeven ───
                LongEnableBreakeven             = true;
                LongBreakevenMode               = BEMode2.FixedTicks;
                LongBreakevenTriggerTicks       = 381;
                LongBreakevenCandlePct          = 50.0;
                LongBreakevenOffsetTicks        = 3;

                // ─── 8. Long: Trade Management ───
                LongMaxBarsInTrade              = 39;
                LongEnableOpposingBarExit       = false;
                LongEnableEngulfingExit         = false;
                LongEnablePriceOffsetTrail      = true;
                LongPriceOffsetReductionTicks   = 35;

                // ─── 9. Long: Entry Filters ───
                LongRequireDirectionFlip        = true;
                LongAllowSameDirectionAfterLoss = true;
                LongMaxDistanceFromSmaTicks     = 0;
                LongRequireSmaSlope             = false;
                LongSmaSlopeLookback            = 3;
                LongEnableWmaFilter             = true;
                LongWmaPeriod                   = 145;
                LongMaxSignalCandleTicks        = 0;
                LongMinBodyPct                  = 60;
                LongTrendConfirmBars            = 13;
                LongEnableAdxFilter             = false;
                LongAdxPeriod                   = 11;
                LongAdxMinLevel                 = 15;

                // ─── 10. Long: Early Exit Filters ───
                LongUseEmaEarlyExit             = false;
                LongEmaEarlyExitPeriod          = 50;
                LongEmaEarlyExitOffsetTicks     = 0;
                LongUseAdxEarlyExit             = true;
                LongAdxEarlyExitPeriod          = 19;
                LongAdxEarlyExitMin             = 15.0;
                LongUseAdxDrawdownExit          = true;
                LongAdxDrawdownFromPeak         = 15.0;

                // ════════════════════════════════════════
                //  SHORT PARAMETERS
                // ════════════════════════════════════════

                // ─── 11. Short: MA Settings ───
                ShortMaPeriod                   = 43;
                ShortEmaPeriod                  = 100;
                ShortMaType                     = MAMode.SMA;

                // ─── 12. Short: Entry ───
                ShortEntryMode                  = MichalEntryMode.Market;
                ShortLimitOffsetTicks           = 1;
                ShortLimitRetracementPct        = 1.0;

                // ─── 13. Short: Take Profit ───
                ShortTakeProfitMode             = MichalTPMode.CandleMultiple;
                ShortTakeProfitTicks            = 310;
                ShortMaxTakeProfitTicks         = 1179;
                ShortSwingStrength              = 1;
                ShortCandleMultiplier           = 4.16;

                // ─── 14. Short: Stop Loss ───
                ShortSlExtraTicks               = 42;
                ShortTrailOffsetTicks           = 42;
                ShortTrailDelayBars             = 1;
                ShortMaxSlTicks                 = 327;
                ShortMaxSlToTpRatio             = 0.48;
                ShortUsePriorCandleSl           = true;
                ShortSlAtMa                     = false;
                ShortMoveSlToEntryBar           = false;
                ShortTrailCandleOffset          = 19;

                // ─── 15. Short: Breakeven ───
                ShortEnableBreakeven            = true;
                ShortBreakevenMode              = BEMode2.FixedTicks;
                ShortBreakevenTriggerTicks      = 357;
                ShortBreakevenCandlePct         = 50.0;
                ShortBreakevenOffsetTicks       = 3;

                // ─── 16. Short: Trade Management ───
                ShortMaxBarsInTrade             = 22;
                ShortEnableOpposingBarExit      = false;
                ShortEnableEngulfingExit        = false;
                ShortEnablePriceOffsetTrail     = false;
                ShortPriceOffsetReductionTicks  = 0;

                // ─── 17. Short: Entry Filters ───
                ShortRequireDirectionFlip       = true;
                ShortAllowSameDirectionAfterLoss = true;
                ShortMaxDistanceFromSmaTicks    = 0;
                ShortRequireSmaSlope            = false;
                ShortSmaSlopeLookback           = 3;
                ShortEnableWmaFilter            = true;
                ShortWmaPeriod                  = 71;
                ShortMaxSignalCandleTicks       = 0;
                ShortMinBodyPct                 = 5;
                ShortTrendConfirmBars           = 13;
                ShortEnableAdxFilter            = true;
                ShortAdxPeriod                  = 5;
                ShortAdxMinLevel                = 20;

                // ─── 18. Short: Early Exit Filters ───
                ShortUseEmaEarlyExit            = false;
                ShortEmaEarlyExitPeriod         = 50;
                ShortEmaEarlyExitOffsetTicks    = 0;
                ShortUseAdxEarlyExit            = true;
                ShortAdxEarlyExitPeriod         = 19;
                ShortAdxEarlyExitMin            = 5.0;
                ShortUseAdxDrawdownExit         = true;
                ShortAdxDrawdownFromPeak        = 9.0;
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                // Long indicators
                longSmaHigh        = SMA(High,  LongMaPeriod);
                longSmaLow         = SMA(Low,   LongMaPeriod);
                longEmaHigh        = EMA(High,  LongEmaPeriod);
                longEmaLow         = EMA(Low,   LongEmaPeriod);
                longWmaFilter      = WMA(Close, LongWmaPeriod);
                longAdxIndicator   = ADX(LongAdxPeriod);
                longSwing          = Swing(LongSwingStrength);
                longEarlyExitEma   = EMA(LongEmaEarlyExitPeriod);
                longEarlyExitAdx   = ADX(LongAdxEarlyExitPeriod);

                // Short indicators
                shortSmaHigh       = SMA(High,  ShortMaPeriod);
                shortSmaLow        = SMA(Low,   ShortMaPeriod);
                shortEmaHigh       = EMA(High,  ShortEmaPeriod);
                shortEmaLow        = EMA(Low,   ShortEmaPeriod);
                shortWmaFilter     = WMA(Close, ShortWmaPeriod);
                shortAdxIndicator  = ADX(ShortAdxPeriod);
                shortSwing         = Swing(ShortSwingStrength);
                shortEarlyExitEma  = EMA(ShortEmaEarlyExitPeriod);
                shortEarlyExitAdx  = ADX(ShortAdxEarlyExitPeriod);

                // Chart display — Long MAs
                if (LongMaType == MAMode.SMA || LongMaType == MAMode.Both)
                {
                    longSmaHigh.Plots[0].Brush = Brushes.RoyalBlue;
                    longSmaLow.Plots[0].Brush  = Brushes.MediumOrchid;
                    AddChartIndicator(longSmaHigh);
                    AddChartIndicator(longSmaLow);
                }
                if (LongMaType == MAMode.EMA || LongMaType == MAMode.Both)
                {
                    longEmaHigh.Plots[0].Brush = Brushes.DodgerBlue;
                    longEmaLow.Plots[0].Brush  = Brushes.Orchid;
                    AddChartIndicator(longEmaHigh);
                    AddChartIndicator(longEmaLow);
                }
                if (LongEnableWmaFilter)
                {
                    longWmaFilter.Plots[0].Brush = Brushes.Gold;
                    AddChartIndicator(longWmaFilter);
                }

                EnsureNewsDatesInitialized();
                ValidateRequiredPrimaryTimeframe(5);
                ValidateRequiredPrimaryInstrument();
            }
            else if (State == State.Transition)
            {
                ResetAll();
            }
            else if (State == State.Terminated)
            {
                DisposeInfoBoxOverlay();
            }
        }

        #region MA Helpers — Long

        private double GetLongUpperMA(int barsAgo)
        {
            switch (LongMaType)
            {
                case MAMode.SMA:  return longSmaHigh[barsAgo];
                case MAMode.EMA:  return longEmaHigh[barsAgo];
                case MAMode.Both: return Math.Max(longSmaHigh[barsAgo], longEmaHigh[barsAgo]);
                default:          return longSmaHigh[barsAgo];
            }
        }

        private double GetLongLowerMA(int barsAgo)
        {
            switch (LongMaType)
            {
                case MAMode.SMA:  return longSmaLow[barsAgo];
                case MAMode.EMA:  return longEmaLow[barsAgo];
                case MAMode.Both: return Math.Min(longSmaLow[barsAgo], longEmaLow[barsAgo]);
                default:          return longSmaLow[barsAgo];
            }
        }

        // Returns the single MA line used for distance checks on long side
        private double GetLongUpperMAForDistance()
        {
            switch (LongMaType)
            {
                case MAMode.EMA:  return longEmaHigh[0];
                case MAMode.Both: return Math.Max(longSmaHigh[0], longEmaHigh[0]);
                default:          return longSmaHigh[0];
            }
        }

        // Returns the lower MA used for trailing on long side
        private double GetLongLowerMAForTrail()
        {
            switch (LongMaType)
            {
                case MAMode.EMA:  return longEmaLow[0];
                case MAMode.Both: return Math.Min(longSmaLow[0], longEmaLow[0]);
                default:          return longSmaLow[0];
            }
        }

        private bool IsLongSlopeRising()
        {
            int lookback = LongSmaSlopeLookback;
            if (CurrentBar <= lookback) return false;
            return GetLongUpperMA(0) > GetLongUpperMA(lookback) && GetLongLowerMA(0) > GetLongLowerMA(lookback);
        }

        #endregion

        #region MA Helpers — Short

        private double GetShortUpperMA(int barsAgo)
        {
            switch (ShortMaType)
            {
                case MAMode.SMA:  return shortSmaHigh[barsAgo];
                case MAMode.EMA:  return shortEmaHigh[barsAgo];
                case MAMode.Both: return Math.Max(shortSmaHigh[barsAgo], shortEmaHigh[barsAgo]);
                default:          return shortSmaHigh[barsAgo];
            }
        }

        private double GetShortLowerMA(int barsAgo)
        {
            switch (ShortMaType)
            {
                case MAMode.SMA:  return shortSmaLow[barsAgo];
                case MAMode.EMA:  return shortEmaLow[barsAgo];
                case MAMode.Both: return Math.Min(shortSmaLow[barsAgo], shortEmaLow[barsAgo]);
                default:          return shortSmaLow[barsAgo];
            }
        }

        private double GetShortLowerMAForDistance()
        {
            switch (ShortMaType)
            {
                case MAMode.EMA:  return shortEmaLow[0];
                case MAMode.Both: return Math.Min(shortSmaLow[0], shortEmaLow[0]);
                default:          return shortSmaLow[0];
            }
        }

        private double GetShortUpperMAForTrail()
        {
            switch (ShortMaType)
            {
                case MAMode.EMA:  return shortEmaHigh[0];
                case MAMode.Both: return Math.Max(shortSmaHigh[0], shortEmaHigh[0]);
                default:          return shortSmaHigh[0];
            }
        }

        private bool IsShortSlopeFalling()
        {
            int lookback = ShortSmaSlopeLookback;
            if (CurrentBar <= lookback) return false;
            return GetShortUpperMA(0) < GetShortUpperMA(lookback) && GetShortLowerMA(0) < GetShortLowerMA(lookback);
        }

        #endregion

        #region MA Helpers — Direction-based (used during active trade management)

        // These use tradeDirection which is set when a trade is open
        private double GetUpperMAForTrail()  => tradeDirection == 1 ? GetLongUpperMAForDistance() : GetShortUpperMAForTrail();
        private double GetLowerMAForTrail()  => tradeDirection == 1 ? GetLongLowerMAForTrail()    : GetShortLowerMAForDistance();
        private double GetUpperMA(int b)     => tradeDirection == 1 ? GetLongUpperMA(b)           : GetShortUpperMA(b);
        private double GetLowerMA(int b)     => tradeDirection == 1 ? GetLongLowerMA(b)           : GetShortLowerMA(b);

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

            // Compute minBars from all direction-specific periods
            int minBars = Math.Max(Math.Max(LongMaPeriod + 2,  ShortMaPeriod + 2),
                          Math.Max(LongEmaPeriod + 2, ShortEmaPeriod + 2));
            minBars = Math.Max(minBars, Math.Max(LongSmaSlopeLookback + 1, ShortSmaSlopeLookback + 1));
            minBars = Math.Max(minBars, Math.Max(LongWmaPeriod + 1,  ShortWmaPeriod + 1));
            minBars = Math.Max(minBars, Math.Max(LongAdxPeriod + 2,  ShortAdxPeriod + 2));
            minBars = Math.Max(minBars, Math.Max(LongTrendConfirmBars + 1, ShortTrendConfirmBars + 1));
            if (CurrentBar < minBars) return;

            // ─── Session Reset ───
            CheckSessionReset();

            // ─── Chart Drawings ───
            DrawSessionTimeWindows();
            UpdateInfoBox();

            // ─── Blocked window transitions ───
            HandleTimeWindowTransitions();

            // ─── Forced Close ───
            if (IsInForcedCloseWindow())
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

            // ─── Session Limits ───
            if (sessionLimitsReached && Position.MarketPosition == MarketPosition.Flat)
            {
                prevMarketPosition = Position.MarketPosition;
                return;
            }

            // ─── In-Position Management ───
            if (Position.MarketPosition != MarketPosition.Flat && hasActivePosition)
            {
                barsSinceEntry++;

                // Max time in trade (direction-specific)
                int maxBars = tradeDirection == 1 ? LongMaxBarsInTrade : ShortMaxBarsInTrade;
                if (maxBars > 0 && barsSinceEntry >= maxBars)
                {
                    Print(string.Format("{0} | MAX TIME EXIT: {1} bars in trade (limit={2})", Time[0], barsSinceEntry, maxBars));
                    FlattenAndCancel("Max time in trade");
                    if (Position.MarketPosition == MarketPosition.Flat && hasActivePosition)
                        HandlePositionClosed();
                }

                bool engulfEnabled = tradeDirection == 1 ? LongEnableEngulfingExit : ShortEnableEngulfingExit;
                if (engulfEnabled && hasActivePosition && Position.MarketPosition != MarketPosition.Flat)
                    CheckEngulfingExit();

                bool oppBarEnabled = tradeDirection == 1 ? LongEnableOpposingBarExit : ShortEnableOpposingBarExit;
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
            if (Position.MarketPosition == MarketPosition.Flat && !hasActivePosition && !sessionLimitsReached && IsInTradeWindow())
                CheckForEntrySignal();

            prevMarketPosition = Position.MarketPosition;
        }

        #region Position Change Detection

        private void HandlePositionClosed()
        {
            if (SystemPerformance != null && SystemPerformance.AllTrades.Count > 0)
            {
                Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                double pnlTicks = lastTrade.ProfitTicks;
                sessionPnLTicks += pnlTicks;
                sessionTradeCount++;
                lastTradeDirection = tradeDirection;

                if (pnlTicks > 0)
                {
                    lastTradeWasLoss = false;
                    sessionWinCount++;
                    Print(string.Format("{0} | CLOSED PROFIT | Dir={1} | PnL={2:F1}t | SessionPnL={3:F1}t | Trades={4} W={5} L={6}",
                        Time[0], tradeDirection == 1 ? "LONG" : "SHORT", pnlTicks, sessionPnLTicks, sessionTradeCount, sessionWinCount, sessionLossCount));
                }
                else
                {
                    lastTradeWasLoss = true;
                    sessionLossCount++;
                    Print(string.Format("{0} | CLOSED LOSS | Dir={1} | PnL={2:F1}t | SessionPnL={3:F1}t | Trades={4} W={5} L={6}",
                        Time[0], tradeDirection == 1 ? "LONG" : "SHORT", pnlTicks, sessionPnLTicks, sessionTradeCount, sessionWinCount, sessionLossCount));
                }
            }
            else
            {
                double pnlTicks = tradeDirection == 1
                    ? (Close[0] - tradeEntryPrice) / TickSize
                    : (tradeEntryPrice - Close[0]) / TickSize;
                sessionPnLTicks += pnlTicks;
                sessionTradeCount++;
                lastTradeDirection = tradeDirection;
                lastTradeWasLoss = pnlTicks <= 0;
                if (pnlTicks > 0) sessionWinCount++; else sessionLossCount++;
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
            priceOffsetTrailDistance    = 0;
            entryBarSlApplied           = false;
            entryOrder                  = null;
            tradePeakAdx                = 0.0;

            CheckSessionLimitsInternal();
        }

        #endregion

        #region Entry Fill

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order == null) return;
            string orderName = execution.Order.Name;

            if ((orderName == "LongEntry" || orderName == "ShortEntry")
                && execution.Order.OrderState == OrderState.Filled
                && !hasActivePosition)
            {
                tradeEntryPrice             = execution.Price;
                hasActivePosition           = true;
                barsSinceEntry              = 0;
                breakEvenApplied            = false;
                tradeEntryTime              = time;
                opposingBarBenchmark        = 0;
                opposingBarBenchmarkSet     = false;
                priceOffsetTrailActive      = false;
                priceOffsetTrailDistance    = 0;
                entryBarSlApplied           = false;

                // Seed peak ADX for the direction being entered
                ADX earlyAdx = tradeDirection == 1 ? longEarlyExitAdx : shortEarlyExitAdx;
                int earlyAdxPeriod = tradeDirection == 1 ? LongAdxEarlyExitPeriod : ShortAdxEarlyExitPeriod;
                tradePeakAdx = (earlyAdx != null && CurrentBar >= earlyAdxPeriod) ? earlyAdx[0] : 0.0;

                string entryName = tradeDirection == 1 ? "LongEntry" : "ShortEntry";
                double tpPrice = CalculateTakeProfit(tradeEntryPrice);
                double slPrice = CalculateStopLoss();

                if (tradeDirection == 1)
                {
                    if (tpPrice <= tradeEntryPrice) tpPrice = tradeEntryPrice + (tradeDirection == 1 ? LongTakeProfitTicks : ShortTakeProfitTicks) * TickSize;
                    if (slPrice >= tradeEntryPrice) slPrice = tradeEntryPrice - 4 * TickSize;
                    ExitLongLimit(0, true, NumberOfContracts, tpPrice, "TP", entryName);
                    ExitLongStopMarket(0, true, NumberOfContracts, slPrice, "SL", entryName);
                }
                else
                {
                    if (tpPrice >= tradeEntryPrice) tpPrice = tradeEntryPrice - (tradeDirection == 1 ? LongTakeProfitTicks : ShortTakeProfitTicks) * TickSize;
                    if (slPrice <= tradeEntryPrice) slPrice = tradeEntryPrice + 4 * TickSize;
                    ExitShortLimit(0, true, NumberOfContracts, tpPrice, "TP", entryName);
                    ExitShortStopMarket(0, true, NumberOfContracts, slPrice, "SL", entryName);
                }

                originalStopPrice = slPrice;

                WMA wma = tradeDirection == 1 ? longWmaFilter : shortWmaFilter;
                Print(string.Format("{0} | FILLED {1} @ {2:F2} | TP={3:F2} | SL={4:F2} | Range={5:F2}",
                    time, tradeDirection == 1 ? "LONG" : "SHORT", tradeEntryPrice, tpPrice, slPrice, signalCandleRange));
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (entryOrder != null && order == entryOrder
                && (orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
            {
                entryOrder = null;
                hasActivePosition = false;
                Print(string.Format("{0} | Entry {1}: {2}", time, orderState, nativeError));
            }
        }

        #endregion

        #region Early Exit Checks (EMA / ADX)

        private void CheckEarlyExits()
        {
            if (tradeEntryPrice == 0) return;

            bool inLoss = tradeDirection == 1
                ? Close[0] < tradeEntryPrice
                : Close[0] > tradeEntryPrice;

            // Select direction-specific early exit indicators and params
            bool useEma        = tradeDirection == 1 ? LongUseEmaEarlyExit    : ShortUseEmaEarlyExit;
            EMA earlyEma       = tradeDirection == 1 ? longEarlyExitEma        : shortEarlyExitEma;
            int emaPeriod      = tradeDirection == 1 ? LongEmaEarlyExitPeriod  : ShortEmaEarlyExitPeriod;
            int emaOffsetTicks = tradeDirection == 1 ? LongEmaEarlyExitOffsetTicks : ShortEmaEarlyExitOffsetTicks;

            bool useAdx        = tradeDirection == 1 ? LongUseAdxEarlyExit    : ShortUseAdxEarlyExit;
            bool useAdxDd      = tradeDirection == 1 ? LongUseAdxDrawdownExit  : ShortUseAdxDrawdownExit;
            ADX earlyAdx       = tradeDirection == 1 ? longEarlyExitAdx        : shortEarlyExitAdx;
            int adxPeriod      = tradeDirection == 1 ? LongAdxEarlyExitPeriod  : ShortAdxEarlyExitPeriod;
            double adxMin      = tradeDirection == 1 ? LongAdxEarlyExitMin     : ShortAdxEarlyExitMin;
            double adxDdPeak   = tradeDirection == 1 ? LongAdxDrawdownFromPeak : ShortAdxDrawdownFromPeak;

            // ─── EMA Early Exit ───
            if (useEma && earlyEma != null && CurrentBar >= emaPeriod)
            {
                double ema    = earlyEma[0];
                double offset = emaOffsetTicks * TickSize;

                if (tradeDirection == 1 && inLoss && Close[0] <= ema - offset)
                {
                    Print(string.Format("{0} | EMA EARLY EXIT (Long): Close={1:F2} <= EMA({2})={3:F2} - {4}t",
                        Time[0], Close[0], emaPeriod, ema, emaOffsetTicks));
                    FlattenAndCancel("EMA Early Exit");
                    if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat) HandlePositionClosed();
                    return;
                }
                if (tradeDirection == -1 && inLoss && Close[0] >= ema + offset)
                {
                    Print(string.Format("{0} | EMA EARLY EXIT (Short): Close={1:F2} >= EMA({2})={3:F2} + {4}t",
                        Time[0], Close[0], emaPeriod, ema, emaOffsetTicks));
                    FlattenAndCancel("EMA Early Exit");
                    if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat) HandlePositionClosed();
                    return;
                }
            }

            // ─── ADX Early Exit + Drawdown Exit ───
            if ((useAdx || useAdxDd) && earlyAdx != null && CurrentBar >= adxPeriod)
            {
                double adx = earlyAdx[0];
                if (adx > tradePeakAdx) tradePeakAdx = adx;

                if (useAdx && inLoss && adxMin > 0.0 && adx < adxMin)
                {
                    Print(string.Format("{0} | ADX EARLY EXIT: ADX={1:F1} < min={2:F1}", Time[0], adx, adxMin));
                    FlattenAndCancel("ADX Early Exit");
                    if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat) HandlePositionClosed();
                    return;
                }

                if (useAdxDd && inLoss && tradePeakAdx > 0.0 && (tradePeakAdx - adx) >= adxDdPeak)
                {
                    Print(string.Format("{0} | ADX DRAWDOWN EXIT: ADX={1:F1} dropped {2:F1} from peak={3:F1}",
                        Time[0], adx, tradePeakAdx - adx, tradePeakAdx));
                    FlattenAndCancel("ADX Drawdown Exit");
                    if (hasActivePosition && Position.MarketPosition == MarketPosition.Flat) HandlePositionClosed();
                    return;
                }
            }
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

        private bool IsInTradeWindow()
        {
            double barSM       = ToSessionMinutes(Time[0].TimeOfDay);
            double startSM     = ToSessionMinutes(TradeWindowStart.TimeOfDay);
            double endSM       = ToSessionMinutes(NoNewTradesAfter.TimeOfDay);
            double skipStartSM = ToSessionMinutes(SkipTimeStart.TimeOfDay);
            double skipEndSM   = ToSessionMinutes(SkipTimeEnd.TimeOfDay);

            if (barSM < startSM || barSM >= endSM)  return false;
            if (barSM >= skipStartSM && barSM < skipEndSM) return false;
            if (IsInNewsSkipWindow(Time[0])) return false;
            return true;
        }

        private bool IsInForcedCloseWindow()
        {
            double barSM   = ToSessionMinutes(Time[0].TimeOfDay);
            double closeSM = ToSessionMinutes(ForcedCloseTime.TimeOfDay);
            return barSM >= closeSM;
        }

        private bool IsSkipWindowConfigured()
        {
            return SkipTimeStart.TimeOfDay != SkipTimeEnd.TimeOfDay;
        }

        private bool IsInNoTradesAfterWindow(DateTime time)
        {
            return ToSessionMinutes(time.TimeOfDay) >= ToSessionMinutes(NoNewTradesAfter.TimeOfDay);
        }

        private bool IsInSkipWindow(DateTime time)
        {
            if (!IsSkipWindowConfigured())
                return false;

            double barSM = ToSessionMinutes(time.TimeOfDay);
            double skipStartSM = ToSessionMinutes(SkipTimeStart.TimeOfDay);
            double skipEndSM = ToSessionMinutes(SkipTimeEnd.TimeOfDay);
            return barSM >= skipStartSM && barSM < skipEndSM;
        }

        private bool IsInNewsSkipWindow(DateTime time)
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
                if (time >= windowStart && time < windowEnd)
                    return true;
            }

            return false;
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
            catch (Exception ex)
            {
                Print("Failed to show instrument popup: " + ex.Message);
            }
        }

        private bool ShowEntryConfirmation(string orderType, double price, int quantity)
        {
            if (State != State.Realtime)
                return true;

            bool result = false;
            if (System.Windows.Application.Current == null)
                return false;

            System.Windows.Application.Current.Dispatcher.Invoke(
                () =>
                {
                    string message = string.Format(
                        CultureInfo.InvariantCulture,
                        "Confirm {0} entry?\nPrice: {1:F2}\nQuantity: {2}",
                        orderType,
                        price,
                        quantity);

                    System.Windows.MessageBoxResult res =
                        System.Windows.MessageBox.Show(
                            message,
                            "Entry Confirmation",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);
                    result = res == System.Windows.MessageBoxResult.Yes;
                });

            return result;
        }

        #endregion

        #region Entry Signal Detection

        private void CheckForEntrySignal()
        {
            if (sessionLimitsReached) return;

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

            // ─── Try Long ───
            if (EnableLongTrades && isBullish && IsDirectionAllowed(1))
                TryLongSignal(currentClose, currentOpen, currentHigh, currentLow, prevClose, prevOpen, prevHigh, prevLow);

            // ─── Try Short (else: one signal per bar) ───
            else if (EnableShortTrades && isBearish && IsDirectionAllowed(-1))
                TryShortSignal(currentClose, currentOpen, currentHigh, currentLow, prevClose, prevOpen, prevHigh, prevLow);
        }

        private void TryLongSignal(double currentClose, double currentOpen, double currentHigh, double currentLow,
                                    double prevClose, double prevOpen, double prevHigh, double prevLow)
        {
            double upperCurr = GetLongUpperMA(0);
            double lowerCurr = GetLongLowerMA(0);
            double upperPrev = GetLongUpperMA(1);
            double lowerPrev = GetLongLowerMA(1);

            // Core MA breakout condition
            if (!(currentClose > upperCurr && currentClose > lowerCurr
               && currentClose > upperPrev && currentClose > lowerPrev
               && currentOpen  > lowerCurr
               && currentClose > prevOpen  && currentClose > prevClose))
                return;

            // Filter: Max signal candle size
            if (LongMaxSignalCandleTicks > 0)
            {
                double rangeTicks = (currentHigh - currentLow) / TickSize;
                if (rangeTicks > LongMaxSignalCandleTicks)
                {
                    Print(string.Format("{0} | LONG REJECTED: Candle range {1:F1}t > Max {2}t", Time[0], rangeTicks, LongMaxSignalCandleTicks));
                    return;
                }
            }

            // Filter: Min body %
            double candleRange = currentHigh - currentLow;
            if (LongMinBodyPct > 0 && candleRange > 0)
            {
                double bodyPct = (Math.Abs(currentClose - currentOpen) / candleRange) * 100.0;
                if (bodyPct < LongMinBodyPct)
                {
                    Print(string.Format("{0} | LONG REJECTED: Body {1:F1}% < Min {2:F1}%", Time[0], bodyPct, LongMinBodyPct));
                    return;
                }
            }

            // Filter: Max distance from upper MA
            if (LongMaxDistanceFromSmaTicks > 0)
            {
                double distTicks = (currentClose - GetLongUpperMAForDistance()) / TickSize;
                if (distTicks > LongMaxDistanceFromSmaTicks)
                {
                    Print(string.Format("{0} | LONG REJECTED: {1:F1}t from MA_H > {2}", Time[0], distTicks, LongMaxDistanceFromSmaTicks));
                    return;
                }
            }

            // Filter: MA slope rising
            if (LongRequireSmaSlope && !IsLongSlopeRising())
            {
                Print(string.Format("{0} | LONG REJECTED: MA not rising", Time[0]));
                return;
            }

            // Filter: WMA
            if (LongEnableWmaFilter && currentClose <= longWmaFilter[0])
            {
                Print(string.Format("{0} | LONG REJECTED: Close {1:F2} <= WMA {2:F2}", Time[0], currentClose, longWmaFilter[0]));
                return;
            }

            // Filter: Trend confirm bars
            if (LongTrendConfirmBars > 0 && currentClose <= Open[LongTrendConfirmBars])
            {
                Print(string.Format("{0} | LONG REJECTED: No uptrend (Close {1:F2} <= Open[{2}] {3:F2})",
                    Time[0], currentClose, LongTrendConfirmBars, Open[LongTrendConfirmBars]));
                return;
            }

            // Filter: ADX
            if (LongEnableAdxFilter && longAdxIndicator[0] < LongAdxMinLevel)
            {
                Print(string.Format("{0} | LONG REJECTED: ADX {1:F1} < {2}", Time[0], longAdxIndicator[0], LongAdxMinLevel));
                return;
            }

            SubmitEntry(1, currentClose, currentHigh, currentLow, prevHigh, prevLow);
        }

        private void TryShortSignal(double currentClose, double currentOpen, double currentHigh, double currentLow,
                                     double prevClose, double prevOpen, double prevHigh, double prevLow)
        {
            double upperCurr = GetShortUpperMA(0);
            double lowerCurr = GetShortLowerMA(0);
            double upperPrev = GetShortUpperMA(1);
            double lowerPrev = GetShortLowerMA(1);

            // Core MA breakout condition
            if (!(currentClose < upperCurr && currentClose < lowerCurr
               && currentClose < upperPrev && currentClose < lowerPrev
               && currentOpen  < upperCurr
               && currentClose < prevOpen  && currentClose < prevClose))
                return;

            // Filter: Max signal candle size
            if (ShortMaxSignalCandleTicks > 0)
            {
                double rangeTicks = (currentHigh - currentLow) / TickSize;
                if (rangeTicks > ShortMaxSignalCandleTicks)
                {
                    Print(string.Format("{0} | SHORT REJECTED: Candle range {1:F1}t > Max {2}t", Time[0], rangeTicks, ShortMaxSignalCandleTicks));
                    return;
                }
            }

            // Filter: Min body %
            double candleRange = currentHigh - currentLow;
            if (ShortMinBodyPct > 0 && candleRange > 0)
            {
                double bodyPct = (Math.Abs(currentClose - currentOpen) / candleRange) * 100.0;
                if (bodyPct < ShortMinBodyPct)
                {
                    Print(string.Format("{0} | SHORT REJECTED: Body {1:F1}% < Min {2:F1}%", Time[0], bodyPct, ShortMinBodyPct));
                    return;
                }
            }

            // Filter: Max distance from lower MA
            if (ShortMaxDistanceFromSmaTicks > 0)
            {
                double distTicks = (GetShortLowerMAForDistance() - currentClose) / TickSize;
                if (distTicks > ShortMaxDistanceFromSmaTicks)
                {
                    Print(string.Format("{0} | SHORT REJECTED: {1:F1}t from MA_L > {2}", Time[0], distTicks, ShortMaxDistanceFromSmaTicks));
                    return;
                }
            }

            // Filter: MA slope falling
            if (ShortRequireSmaSlope && !IsShortSlopeFalling())
            {
                Print(string.Format("{0} | SHORT REJECTED: MA not falling", Time[0]));
                return;
            }

            // Filter: WMA
            if (ShortEnableWmaFilter && currentClose >= shortWmaFilter[0])
            {
                Print(string.Format("{0} | SHORT REJECTED: Close {1:F2} >= WMA {2:F2}", Time[0], currentClose, shortWmaFilter[0]));
                return;
            }

            // Filter: Trend confirm bars
            if (ShortTrendConfirmBars > 0 && currentClose >= Open[ShortTrendConfirmBars])
            {
                Print(string.Format("{0} | SHORT REJECTED: No downtrend (Close {1:F2} >= Open[{2}] {3:F2})",
                    Time[0], currentClose, ShortTrendConfirmBars, Open[ShortTrendConfirmBars]));
                return;
            }

            // Filter: ADX
            if (ShortEnableAdxFilter && shortAdxIndicator[0] < ShortAdxMinLevel)
            {
                Print(string.Format("{0} | SHORT REJECTED: ADX {1:F1} < {2}", Time[0], shortAdxIndicator[0], ShortAdxMinLevel));
                return;
            }

            SubmitEntry(-1, currentClose, currentHigh, currentLow, prevHigh, prevLow);
        }

        private bool IsDirectionAllowed(int direction)
        {
            if (lastTradeDirection == 0) return true;

            bool requireFlip       = direction == 1 ? LongRequireDirectionFlip        : ShortRequireDirectionFlip;
            bool allowSameAfterLoss = direction == 1 ? LongAllowSameDirectionAfterLoss : ShortAllowSameDirectionAfterLoss;

            if (!requireFlip) return true;

            if (lastTradeDirection == direction)
            {
                if (allowSameAfterLoss && lastTradeWasLoss) return true;
                return false;
            }
            return true;
        }

        private void SubmitEntry(int direction, double closePrice, double candleHigh, double candleLow, double prevHigh, double prevLow)
        {
            double candleRange = candleHigh - candleLow;

            int    slExtraTicks    = direction == 1 ? LongSlExtraTicks    : ShortSlExtraTicks;
            int    maxSlTicks      = direction == 1 ? LongMaxSlTicks       : ShortMaxSlTicks;
            double maxSlToTpRatio  = direction == 1 ? LongMaxSlToTpRatio   : ShortMaxSlToTpRatio;

            // Pre-calculate SL distance for validation
            double slPrice = direction == 1
                ? prevLow  - slExtraTicks * TickSize
                : prevHigh + slExtraTicks * TickSize;

            double slDistanceTicks = Math.Abs(closePrice - slPrice) / TickSize;

            if (maxSlTicks > 0 && slDistanceTicks > maxSlTicks)
            {
                Print(string.Format("{0} | {1} REJECTED: SL {2:F1}t > MaxSL {3}t",
                    Time[0], direction == 1 ? "LONG" : "SHORT", slDistanceTicks, maxSlTicks));
                return;
            }

            if (maxSlToTpRatio > 0)
            {
                double tpDistanceTicks = CalculateTPDistanceTicks(direction, candleRange);
                if (tpDistanceTicks > 0)
                {
                    double ratio = slDistanceTicks / tpDistanceTicks;
                    if (ratio > maxSlToTpRatio)
                    {
                        Print(string.Format("{0} | {1} REJECTED: SL:TP ratio {2:F2} > Max {3:F2} (SL={4:F1}t TP={5:F1}t)",
                            Time[0], direction == 1 ? "LONG" : "SHORT", ratio, maxSlToTpRatio, slDistanceTicks, tpDistanceTicks));
                        return;
                    }
                }
            }

            tradeDirection    = direction;
            signalCandleRange = candleRange;
            priorCandleHigh   = prevHigh;
            priorCandleLow    = prevLow;

            string entryName = direction == 1 ? "LongEntry" : "ShortEntry";

            MichalEntryMode entryMode         = direction == 1 ? LongEntryMode          : ShortEntryMode;
            int             limitOffsetTicks   = direction == 1 ? LongLimitOffsetTicks   : ShortLimitOffsetTicks;
            double          limitRetracePct    = direction == 1 ? LongLimitRetracementPct : ShortLimitRetracementPct;

            if (entryMode == MichalEntryMode.Market)
            {
                if (RequireEntryConfirmation && !ShowEntryConfirmation(direction == 1 ? "Long" : "Short", closePrice, NumberOfContracts))
                {
                    Print(string.Format("{0} | Entry confirmation declined | {1}.", Time[0], direction == 1 ? "LONG" : "SHORT"));
                    return;
                }
                entryOrder = direction == 1 ? EnterLong(NumberOfContracts, entryName) : EnterShort(NumberOfContracts, entryName);
            }
            else if (entryMode == MichalEntryMode.LimitOffset)
            {
                double limitPrice = direction == 1
                    ? closePrice - limitOffsetTicks * TickSize
                    : closePrice + limitOffsetTicks * TickSize;
                if (RequireEntryConfirmation && !ShowEntryConfirmation(direction == 1 ? "Long" : "Short", limitPrice, NumberOfContracts))
                {
                    Print(string.Format("{0} | Entry confirmation declined | {1}.", Time[0], direction == 1 ? "LONG" : "SHORT"));
                    return;
                }
                entryOrder = direction == 1
                    ? EnterLongLimit(0, true, NumberOfContracts, limitPrice, entryName)
                    : EnterShortLimit(0, true, NumberOfContracts, limitPrice, entryName);
            }
            else if (entryMode == MichalEntryMode.LimitRetracement)
            {
                double retrace = signalCandleRange * (limitRetracePct / 100.0);
                double limitPrice = direction == 1 ? closePrice - retrace : closePrice + retrace;
                if (RequireEntryConfirmation && !ShowEntryConfirmation(direction == 1 ? "Long" : "Short", limitPrice, NumberOfContracts))
                {
                    Print(string.Format("{0} | Entry confirmation declined | {1}.", Time[0], direction == 1 ? "LONG" : "SHORT"));
                    return;
                }
                entryOrder = direction == 1
                    ? EnterLongLimit(0, true, NumberOfContracts, limitPrice, entryName)
                    : EnterShortLimit(0, true, NumberOfContracts, limitPrice, entryName);
            }

            WMA wma = direction == 1 ? longWmaFilter : shortWmaFilter;
            Print(string.Format("{0} | SIGNAL {1} | Close={2:F2} | MA_H={3:F2} MA_L={4:F2} | WMA={5:F2} | LastDir={6} WasLoss={7}",
                Time[0], direction == 1 ? "LONG" : "SHORT", closePrice,
                direction == 1 ? GetLongUpperMA(0) : GetShortUpperMA(0),
                direction == 1 ? GetLongLowerMA(0) : GetShortLowerMA(0),
                wma[0], lastTradeDirection, lastTradeWasLoss));
        }

        #endregion

        #region Take Profit

        private double CalculateTPDistanceTicks(int direction, double candleRange)
        {
            MichalTPMode mode   = direction == 1 ? LongTakeProfitMode    : ShortTakeProfitMode;
            int          tpTix  = direction == 1 ? LongTakeProfitTicks   : ShortTakeProfitTicks;
            int          maxTp  = direction == 1 ? LongMaxTakeProfitTicks : ShortMaxTakeProfitTicks;
            double       mult   = direction == 1 ? LongCandleMultiplier  : ShortCandleMultiplier;

            double tpTicks;
            switch (mode)
            {
                case MichalTPMode.CandleMultiple: tpTicks = (candleRange * mult) / TickSize; break;
                case MichalTPMode.FixedTicks:     tpTicks = tpTix; break;
                default:                          tpTicks = tpTix; break;
            }
            if (maxTp > 0 && tpTicks > maxTp) tpTicks = maxTp;
            return tpTicks;
        }

        private double CalculateTakeProfit(double fillPrice)
        {
            MichalTPMode mode   = tradeDirection == 1 ? LongTakeProfitMode    : ShortTakeProfitMode;
            int          tpTix  = tradeDirection == 1 ? LongTakeProfitTicks   : ShortTakeProfitTicks;
            int          maxTp  = tradeDirection == 1 ? LongMaxTakeProfitTicks : ShortMaxTakeProfitTicks;
            double       mult   = tradeDirection == 1 ? LongCandleMultiplier  : ShortCandleMultiplier;

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
                if (tpDistance > maxTpDist)
                {
                    Print(string.Format("{0} | TP CAPPED: {1:F1}t -> {2}t (MaxTP)", Time[0], tpDistance / TickSize, maxTp));
                    tpDistance = maxTpDist;
                }
            }
            return tradeDirection == 1 ? fillPrice + tpDistance : fillPrice - tpDistance;
        }

        private double GetSwingTarget(double fillPrice)
        {
            Swing sw = tradeDirection == 1 ? longSwing : shortSwing;
            int fallbackTicks = tradeDirection == 1 ? LongTakeProfitTicks : ShortTakeProfitTicks;

            if (tradeDirection == 1)
            {
                for (int i = 0; i < CurrentBar - LongSwingStrength; i++)
                {
                    double swHigh = sw.SwingHigh[i];
                    if (swHigh > 0 && swHigh > fillPrice) return swHigh;
                }
                return fillPrice + fallbackTicks * TickSize;
            }
            else
            {
                for (int i = 0; i < CurrentBar - ShortSwingStrength; i++)
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
            bool   slAtMa          = tradeDirection == 1 ? LongSlAtMa          : ShortSlAtMa;
            bool   usePriorCandle  = tradeDirection == 1 ? LongUsePriorCandleSl : ShortUsePriorCandleSl;
            int    slExtraTicks    = tradeDirection == 1 ? LongSlExtraTicks     : ShortSlExtraTicks;

            double slPrice;

            if (slAtMa)
            {
                slPrice = tradeDirection == 1
                    ? GetLongLowerMA(0) - slExtraTicks * TickSize
                    : GetShortUpperMA(0) + slExtraTicks * TickSize;
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

        private void ManageBreakeven()
        {
            bool   enabled      = tradeDirection == 1 ? LongEnableBreakeven        : ShortEnableBreakeven;
            BEMode2 mode        = tradeDirection == 1 ? LongBreakevenMode           : ShortBreakevenMode;
            int    triggerTicks = tradeDirection == 1 ? LongBreakevenTriggerTicks   : ShortBreakevenTriggerTicks;
            double candlePct    = tradeDirection == 1 ? LongBreakevenCandlePct      : ShortBreakevenCandlePct;
            int    offsetTicks  = tradeDirection == 1 ? LongBreakevenOffsetTicks    : ShortBreakevenOffsetTicks;

            if (!enabled || breakEvenApplied || tradeEntryPrice == 0) return;

            double triggerDistance = mode == BEMode2.FixedTicks
                ? triggerTicks * TickSize
                : signalCandleRange * (candlePct / 100.0);

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
                    if (bePrice > originalStopPrice && bePrice < Close[0])
                    {
                        ExitLongStopMarket(0, true, NumberOfContracts, bePrice, "SL", "LongEntry");
                        originalStopPrice = bePrice;
                        breakEvenApplied = true;
                        Print(string.Format("{0} | BREAKEVEN LONG: SL -> {1:F2}", Time[0], bePrice));
                    }
                }
                else
                {
                    double bePrice = Instrument.MasterInstrument.RoundToTickSize(tradeEntryPrice - offsetTicks * TickSize);
                    if (bePrice < originalStopPrice && bePrice > Close[0])
                    {
                        ExitShortStopMarket(0, true, NumberOfContracts, bePrice, "SL", "ShortEntry");
                        originalStopPrice = bePrice;
                        breakEvenApplied = true;
                        Print(string.Format("{0} | BREAKEVEN SHORT: SL -> {1:F2}", Time[0], bePrice));
                    }
                }
            }
        }

        private void ManageTrailingStop()
        {
            int trailDelayBars  = tradeDirection == 1 ? LongTrailDelayBars  : ShortTrailDelayBars;
            int trailOffsetTicks = tradeDirection == 1 ? LongTrailOffsetTicks : ShortTrailOffsetTicks;

            if (barsSinceEntry < trailDelayBars) return;

            if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                double newStop = GetLongLowerMAForTrail() - trailOffsetTicks * TickSize;
                newStop = Instrument.MasterInstrument.RoundToTickSize(newStop);
                if (newStop > originalStopPrice && newStop < Close[0])
                {
                    ExitLongStopMarket(0, true, NumberOfContracts, newStop, "SL", "LongEntry");
                    Print(string.Format("{0} | TRAIL UP -> {1:F2}", Time[0], newStop));
                }
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                double newStop = GetShortUpperMAForTrail() + trailOffsetTicks * TickSize;
                newStop = Instrument.MasterInstrument.RoundToTickSize(newStop);
                if (newStop < originalStopPrice && newStop > Close[0])
                {
                    ExitShortStopMarket(0, true, NumberOfContracts, newStop, "SL", "ShortEntry");
                    Print(string.Format("{0} | TRAIL DOWN -> {1:F2}", Time[0], newStop));
                }
            }
        }

        private void ManageEntryBarSl()
        {
            bool moveSlEnabled = tradeDirection == 1 ? LongMoveSlToEntryBar : ShortMoveSlToEntryBar;
            int  slExtraTicks  = tradeDirection == 1 ? LongSlExtraTicks     : ShortSlExtraTicks;

            if (!moveSlEnabled || entryBarSlApplied || barsSinceEntry != 1) return;
            entryBarSlApplied = true;

            bool entryBarBullish = Close[1] > Open[1];
            bool entryBarBearish = Close[1] < Open[1];

            if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long && entryBarBullish)
            {
                double newSl = Instrument.MasterInstrument.RoundToTickSize(Low[1] - slExtraTicks * TickSize);
                if (newSl > originalStopPrice && newSl < Close[0])
                {
                    ExitLongStopMarket(0, true, NumberOfContracts, newSl, "SL", "LongEntry");
                    originalStopPrice = newSl;
                    Print(string.Format("{0} | ENTRY BAR SL LONG: SL -> {1:F2} (entry bar low={2:F2})", Time[0], newSl, Low[1]));
                }
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short && entryBarBearish)
            {
                double newSl = Instrument.MasterInstrument.RoundToTickSize(High[1] + slExtraTicks * TickSize);
                if (newSl < originalStopPrice && newSl > Close[0])
                {
                    ExitShortStopMarket(0, true, NumberOfContracts, newSl, "SL", "ShortEntry");
                    originalStopPrice = newSl;
                    Print(string.Format("{0} | ENTRY BAR SL SHORT: SL -> {1:F2} (entry bar high={2:F2})", Time[0], newSl, High[1]));
                }
            }
        }

        private void ManageCandleLagTrail()
        {
            int trailCandleOffset = tradeDirection == 1 ? LongTrailCandleOffset : ShortTrailCandleOffset;
            int trailDelayBars    = tradeDirection == 1 ? LongTrailDelayBars    : ShortTrailDelayBars;
            int slExtraTicks      = tradeDirection == 1 ? LongSlExtraTicks      : ShortSlExtraTicks;

            if (trailCandleOffset <= 0 || barsSinceEntry < trailCandleOffset + trailDelayBars) return;

            if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                double newStop = Instrument.MasterInstrument.RoundToTickSize(Low[trailCandleOffset] - slExtraTicks * TickSize);
                if (newStop > originalStopPrice && newStop < Close[0])
                {
                    ExitLongStopMarket(0, true, NumberOfContracts, newStop, "SL", "LongEntry");
                    Print(string.Format("{0} | CANDLE-LAG TRAIL UP -> {1:F2} (Low[{2}]={3:F2})", Time[0], newStop, trailCandleOffset, Low[trailCandleOffset]));
                }
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                double newStop = Instrument.MasterInstrument.RoundToTickSize(High[trailCandleOffset] + slExtraTicks * TickSize);
                if (newStop < originalStopPrice && newStop > Close[0])
                {
                    ExitShortStopMarket(0, true, NumberOfContracts, newStop, "SL", "ShortEntry");
                    Print(string.Format("{0} | CANDLE-LAG TRAIL DOWN -> {1:F2} (High[{2}]={3:F2})", Time[0], newStop, trailCandleOffset, High[trailCandleOffset]));
                }
            }
        }

        #endregion

        #region Engulfing Bar Exit

        private void CheckEngulfingExit()
        {
            double currentOpen  = Open[0];
            double currentClose = Close[0];
            double prevOpen     = Open[1];
            double prevClose    = Close[1];
            double prevBodyHigh = Math.Max(prevOpen, prevClose);
            double prevBodyLow  = Math.Min(prevOpen, prevClose);

            if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                bool isBullish = currentClose > currentOpen;
                if (isBullish && currentClose > prevBodyHigh && currentOpen <= prevBodyLow)
                {
                    Print(string.Format("{0} | ENGULFING EXIT SHORT: Bullish engulfing (O={1:F2} C={2:F2} engulfs {3:F2}-{4:F2})",
                        Time[0], currentOpen, currentClose, prevBodyLow, prevBodyHigh));
                    ExitShort(Math.Max(1, Position.Quantity), "EngulfExit", "ShortEntry");
                }
            }
            else if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                bool isBearish = currentClose < currentOpen;
                if (isBearish && currentClose < prevBodyLow && currentOpen >= prevBodyHigh)
                {
                    Print(string.Format("{0} | ENGULFING EXIT LONG: Bearish engulfing (O={1:F2} C={2:F2} engulfs {3:F2}-{4:F2})",
                        Time[0], currentOpen, currentClose, prevBodyLow, prevBodyHigh));
                    ExitLong(Math.Max(1, Position.Quantity), "EngulfExit", "LongEntry");
                }
            }
        }

        #endregion

        #region Price-Offset Trailing

        private void ManagePriceOffsetTrail()
        {
            bool enabled       = tradeDirection == 1 ? LongEnablePriceOffsetTrail    : ShortEnablePriceOffsetTrail;
            int  reductionTicks = tradeDirection == 1 ? LongPriceOffsetReductionTicks : ShortPriceOffsetReductionTicks;

            if (!enabled || tradeEntryPrice == 0) return;

            if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                double lowerMA = GetLongLowerMAForTrail();
                if (!priceOffsetTrailActive)
                {
                    if (lowerMA > originalStopPrice)
                    {
                        priceOffsetTrailDistance = Close[0] - originalStopPrice - (reductionTicks * TickSize);
                        if (priceOffsetTrailDistance < TickSize) priceOffsetTrailDistance = TickSize;
                        priceOffsetTrailActive = true;
                        Print(string.Format("{0} | PRICE-OFFSET TRAIL ACTIVATED LONG: MA_L={1:F2} > origSL={2:F2} | Offset={3:F1}t",
                            Time[0], lowerMA, originalStopPrice, priceOffsetTrailDistance / TickSize));
                    }
                }
                if (priceOffsetTrailActive)
                {
                    double newStop = Instrument.MasterInstrument.RoundToTickSize(Close[0] - priceOffsetTrailDistance);
                    if (newStop > originalStopPrice && newStop < Close[0])
                    {
                    ExitLongStopMarket(0, true, NumberOfContracts, newStop, "SL", "LongEntry");
                        Print(string.Format("{0} | PRICE-OFFSET TRAIL UP -> {1:F2}", Time[0], newStop));
                    }
                }
            }
            else if (tradeDirection == -1 && Position.MarketPosition == MarketPosition.Short)
            {
                double upperMA = GetShortUpperMAForTrail();
                if (!priceOffsetTrailActive)
                {
                    if (upperMA < originalStopPrice)
                    {
                        priceOffsetTrailDistance = originalStopPrice - Close[0] - (reductionTicks * TickSize);
                        if (priceOffsetTrailDistance < TickSize) priceOffsetTrailDistance = TickSize;
                        priceOffsetTrailActive = true;
                        Print(string.Format("{0} | PRICE-OFFSET TRAIL ACTIVATED SHORT: MA_H={1:F2} < origSL={2:F2} | Offset={3:F1}t",
                            Time[0], upperMA, originalStopPrice, priceOffsetTrailDistance / TickSize));
                    }
                }
                if (priceOffsetTrailActive)
                {
                    double newStop = Instrument.MasterInstrument.RoundToTickSize(Close[0] + priceOffsetTrailDistance);
                    if (newStop < originalStopPrice && newStop > Close[0])
                    {
                    ExitShortStopMarket(0, true, NumberOfContracts, newStop, "SL", "ShortEntry");
                        Print(string.Format("{0} | PRICE-OFFSET TRAIL DOWN -> {1:F2}", Time[0], newStop));
                    }
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
                    if (!opposingBarBenchmarkSet)
                    {
                        opposingBarBenchmark = Close[0];
                        opposingBarBenchmarkSet = true;
                        Print(string.Format("{0} | OPP BAR: First bullish bar, benchmark={1:F2}", Time[0], Close[0]));
                    }
                    else if (Close[0] > opposingBarBenchmark)
                    {
                        Print(string.Format("{0} | OPP BAR EXIT SHORT: Bullish close {1:F2} > benchmark {2:F2}", Time[0], Close[0], opposingBarBenchmark));
                        ExitShort(Math.Max(1, Position.Quantity), "OppBarExit", "ShortEntry");
                    }
                }
            }
            else if (tradeDirection == 1 && Position.MarketPosition == MarketPosition.Long)
            {
                if (isBearish)
                {
                    if (!opposingBarBenchmarkSet)
                    {
                        opposingBarBenchmark = Close[0];
                        opposingBarBenchmarkSet = true;
                        Print(string.Format("{0} | OPP BAR: First bearish bar, benchmark={1:F2}", Time[0], Close[0]));
                    }
                    else if (Close[0] < opposingBarBenchmark)
                    {
                        Print(string.Format("{0} | OPP BAR EXIT LONG: Bearish close {1:F2} < benchmark {2:F2}", Time[0], Close[0], opposingBarBenchmark));
                        ExitLong(Math.Max(1, Position.Quantity), "OppBarExit", "LongEntry");
                    }
                }
            }
        }

        #endregion

        #region Session Management

        private void CheckSessionReset()
        {
            TimeSpan barTOD = Time[0].TimeOfDay;
            TimeSpan sessionStartTOD = SessionStartTime.TimeOfDay;

            DateTime sessionDate = barTOD >= sessionStartTOD
                ? Time[0].Date
                : Time[0].Date.AddDays(-1);

            if (sessionDate != currentSessionDate)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                    FlattenAndCancel("Session rollover");
                currentSessionDate = sessionDate;
                ResetAll();
                Print(string.Format("{0} | ═══ NEW SESSION ═══ {1:yyyy-MM-dd}", Time[0], sessionDate));
            }
        }

        private void ResetAll()
        {
            sessionTradeCount        = 0;
            sessionWinCount          = 0;
            sessionLossCount         = 0;
            sessionPnLTicks          = 0;
            sessionLimitsReached     = false;
            lastTradeDirection       = 0;
            lastTradeWasLoss         = false;
            tradeDirection           = 0;
            signalCandleRange        = 0;
            priorCandleHigh          = 0;
            priorCandleLow           = 0;
            originalStopPrice        = 0;
            tradeEntryPrice          = 0;
            hasActivePosition        = false;
            barsSinceEntry           = 0;
            breakEvenApplied         = false;
            opposingBarBenchmark     = 0;
            opposingBarBenchmarkSet  = false;
            priceOffsetTrailActive   = false;
            priceOffsetTrailDistance = 0;
            entryBarSlApplied        = false;
            entryOrder               = null;
            prevMarketPosition       = MarketPosition.Flat;
            tradePeakAdx             = 0.0;
            wasInNoTradesAfterWindow = false;
            wasInSkipWindow          = false;
            wasInNewsSkipWindow      = false;
        }

        private void CheckSessionLimitsInternal()
        {
            if (sessionLimitsReached) return;
            string reason = "";

            if (sessionTradeCount >= MaxTradesPerSession)
                reason = string.Format("Max trades ({0})", MaxTradesPerSession);
            else if (MaxWinsPerSession > 0 && sessionWinCount >= MaxWinsPerSession)
                reason = string.Format("Max wins ({0})", MaxWinsPerSession);
            else if (MaxLossesPerSession > 0 && sessionLossCount >= MaxLossesPerSession)
                reason = string.Format("Max losses ({0})", MaxLossesPerSession);
            else if (sessionPnLTicks >= MaxSessionProfitTicks)
                reason = string.Format("Max profit ({0:F1}t)", sessionPnLTicks);
            else if (sessionPnLTicks <= -MaxSessionLossTicks)
                reason = string.Format("Max loss ({0:F1}t)", sessionPnLTicks);

            if (!string.IsNullOrEmpty(reason))
            {
                sessionLimitsReached = true;
                Print(string.Format("{0} | *** SESSION LIMIT: {1} | W={2} L={3} ***", Time[0], reason, sessionWinCount, sessionLossCount));
            }
        }

        private void HandleTimeWindowTransitions()
        {
            bool inNoTradesAfterWindow = IsInNoTradesAfterWindow(Time[0]);
            bool inSkipWindow = IsInSkipWindow(Time[0]);
            bool inNewsSkipWindow = IsInNewsSkipWindow(Time[0]);

            if (!wasInNoTradesAfterWindow && inNoTradesAfterWindow)
                FlattenAndCancel("NoTradesAfter");

            if (!wasInSkipWindow && inSkipWindow)
                FlattenAndCancel("SkipWindow");

            if (!wasInNewsSkipWindow && inNewsSkipWindow)
                FlattenAndCancel("NewsSkip");

            wasInNoTradesAfterWindow = inNoTradesAfterWindow;
            wasInSkipWindow = inSkipWindow;
            wasInNewsSkipWindow = inNewsSkipWindow;
        }

        private void CancelWorkingEntryOrder()
        {
            if (entryOrder == null)
                return;

            try
            {
                if (entryOrder.OrderState == OrderState.Working
                    || entryOrder.OrderState == OrderState.Accepted
                    || entryOrder.OrderState == OrderState.Submitted
                    || entryOrder.OrderState == OrderState.PartFilled)
                {
                    CancelOrder(entryOrder);
                }
            }
            catch
            {
            }
        }

        private void FlattenAndCancel(string reason)
        {
            CancelWorkingEntryOrder();

            if (Position.MarketPosition == MarketPosition.Long)
            {
                ExitLong(Math.Max(1, Position.Quantity), "ForcedExit", "LongEntry");
                Print(string.Format("{0} | {1}: Close LONG", Time[0], reason));
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                ExitShort(Math.Max(1, Position.Quantity), "ForcedExit", "ShortEntry");
                Print(string.Format("{0} | {1}: Close SHORT", Time[0], reason));
            }
        }

        #endregion

        #region Drawing Helpers

        private void DrawSessionTimeWindows()
        {
            if (CurrentBar < 1)
                return;

            DrawSessionBackground(Time[0]);
            DrawNoNewTradesLine(Time[0]);
            DrawSkipWindow(Time[0]);
            DrawNewsWindows(Time[0]);
        }

        private void DrawSessionBackground(DateTime barTime)
        {
            DateTime sessionStart = barTime.Date + TradeWindowStart.TimeOfDay;
            DateTime sessionEnd = barTime.Date + ForcedCloseTime.TimeOfDay;
            if (ForcedCloseTime.TimeOfDay <= TradeWindowStart.TimeOfDay)
                sessionEnd = sessionEnd.AddDays(1);
            if (sessionEnd <= sessionStart)
                return;

            string rectTag = string.Format("MICH_SessionFill_{0:yyyyMMdd_HHmm}", sessionStart);
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

        private void DrawNoNewTradesLine(DateTime barTime)
        {
            DateTime lineTime = barTime.Date + NoNewTradesAfter.TimeOfDay;
            string tag = string.Format("MICH_NoNewTradesAfter_{0:yyyyMMdd_HHmm}", lineTime);
            if (DrawObjects[tag] != null)
                return;

            Draw.VerticalLine(this, tag, lineTime, Brushes.Red, DashStyleHelper.DashDot, 2);
        }

        private void DrawSkipWindow(DateTime barTime)
        {
            if (!IsSkipWindowConfigured())
                return;

            DateTime windowStart = barTime.Date + SkipTimeStart.TimeOfDay;
            DateTime windowEnd = barTime.Date + SkipTimeEnd.TimeOfDay;
            if (SkipTimeStart.TimeOfDay > SkipTimeEnd.TimeOfDay)
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

            string tagBase = string.Format("MICH_Skip_{0:yyyyMMdd_HHmm}", windowStart);
            if (DrawObjects[tagBase + "_Rect"] != null)
                return;

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
                catch
                {
                }

                string tagBase = string.Format("MICH_News_{0:yyyyMMdd_HHmm}", newsTime);
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

        #endregion

        #region Info Box

        private enum InfoValueRunKind
        {
            Default,
            Emoji,
            Symbol
        }

        private void UpdateInfoBox()
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
            if (string.IsNullOrWhiteSpace(value))
                return value ?? string.Empty;

            string token = value.Trim();
            if (token == "○" || token == "◯" || token == "⚪")
                return "⛔";

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

                if ((codePoint >= 0x1F300 && codePoint <= 0x1FAFF)
                    || (codePoint >= 0x2600 && codePoint <= 0x27BF))
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
            RemoveDrawObject("Info");
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

            lines.Add((string.Format("MICH v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(("Contracts:", NumberOfContracts.ToString(CultureInfo.InvariantCulture), Brushes.LightGray, Brushes.LightGray));

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
                        string value = dayPart + " " + timePart;
                        Brush newsBrush = blockPassed ? PassedNewsRowBrush : Brushes.LightGray;
                        lines.Add(("News:", value, newsBrush, newsBrush));
                    }
                }
            }

            lines.Add(("Session:", "New York", Brushes.LightGray, Brushes.LightGray));
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

        #endregion

        #region News Helpers

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

                if (parsed.TimeOfDay == new TimeSpan(14, 0, 0) && !NewsDates.Contains(parsed))
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

        #endregion

        #region Properties

        // ═══════════════════════════════════════
        //  0. GENERAL
        // ═══════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order quantity for entries and attached exits.", Order = 1, GroupName = "00. General")]
        public int NumberOfContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Long Trades", Description = "Allow long entries.", Order = 2, GroupName = "00. General")]
        public bool EnableLongTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Short Trades", Description = "Allow short entries.", Order = 3, GroupName = "00. General")]
        public bool EnableShortTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Entry Confirmation", Description = "Show a confirmation prompt before submitting each entry order.", Order = 4, GroupName = "00. General")]
        public bool RequireEntryConfirmation { get; set; }

        // ═══════════════════════════════════════
        //  1. COMMON: SESSION MANAGEMENT
        // ═══════════════════════════════════════

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Session Start Time", Order = 1, GroupName = "01. Common: Session Management")]
        public DateTime SessionStartTime { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Trade Window Start", Order = 2, GroupName = "01. Common: Session Management")]
        public DateTime TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Skip Time Start", Order = 3, GroupName = "01. Common: Session Management")]
        public DateTime SkipTimeStart { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Skip Time End", Order = 4, GroupName = "01. Common: Session Management")]
        public DateTime SkipTimeEnd { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "No New Trades After", Order = 5, GroupName = "01. Common: Session Management")]
        public DateTime NoNewTradesAfter { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Forced Close Time", Order = 6, GroupName = "01. Common: Session Management")]
        public DateTime ForcedCloseTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Description = "Block entries and flatten/cancel on listed news windows.", Order = 7, GroupName = "01. Common: Session Management")]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "News Block Minutes", Description = "Minutes before/after listed news times to block trading.", Order = 8, GroupName = "01. Common: Session Management")]
        public int NewsBlockMinutes { get; set; }

        // ═══════════════════════════════════════
        //  2. COMMON: SESSION LIMITS
        // ═══════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Per Session", Order = 1, GroupName = "02. Common: Session Limits")]
        public int MaxTradesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Wins Per Session", Description = "0 = unlimited.", Order = 2, GroupName = "02. Common: Session Limits")]
        public int MaxWinsPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Losses Per Session", Description = "0 = unlimited.", Order = 3, GroupName = "02. Common: Session Limits")]
        public int MaxLossesPerSession { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 4, GroupName = "02. Common: Session Limits")]
        public int MaxSessionProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 5, GroupName = "02. Common: Session Limits")]
        public int MaxSessionLossTicks { get; set; }

        // ════════════════════════════════════════
        //  3. LONG: MA SETTINGS
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "03. Long: MA Settings")]
        public int LongMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "03. Long: MA Settings")]
        public int LongEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "03. Long: MA Settings")]
        public MAMode LongMaType { get; set; }

        // ════════════════════════════════════════
        //  4. LONG: ENTRY
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Entry Mode", Order = 1, GroupName = "04. Long: Entry")]
        public MichalEntryMode LongEntryMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Limit Offset (Ticks)", Order = 2, GroupName = "04. Long: Entry")]
        public int LongLimitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "Limit Retracement %", Order = 3, GroupName = "04. Long: Entry")]
        public double LongLimitRetracementPct { get; set; }

        // ════════════════════════════════════════
        //  5. LONG: TAKE PROFIT
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Take Profit Mode", Order = 1, GroupName = "05. Long: Take Profit")]
        public MichalTPMode LongTakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP Fixed Ticks", Order = 2, GroupName = "05. Long: Take Profit")]
        public int LongTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "05. Long: Take Profit")]
        public int LongMaxTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Swing Strength", Order = 4, GroupName = "05. Long: Take Profit")]
        public int LongSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "05. Long: Take Profit")]
        public double LongCandleMultiplier { get; set; }

        // ════════════════════════════════════════
        //  6. LONG: STOP LOSS
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "06. Long: Stop Loss")]
        public int LongSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "06. Long: Stop Loss")]
        public int LongTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "06. Long: Stop Loss")]
        public int LongTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "06. Long: Stop Loss")]
        public int LongMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "06. Long: Stop Loss")]
        public double LongMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "06. Long: Stop Loss")]
        public bool LongUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "06. Long: Stop Loss")]
        public bool LongSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "06. Long: Stop Loss")]
        public bool LongMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "06. Long: Stop Loss")]
        public int LongTrailCandleOffset { get; set; }

        // ════════════════════════════════════════
        //  7. LONG: BREAKEVEN
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "07. Long: Breakeven")]
        public bool LongEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Breakeven Mode", Order = 2, GroupName = "07. Long: Breakeven")]
        public BEMode2 LongBreakevenMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "07. Long: Breakeven")]
        public int LongBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "07. Long: Breakeven")]
        public double LongBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "07. Long: Breakeven")]
        public int LongBreakevenOffsetTicks { get; set; }

        // ════════════════════════════════════════
        //  8. LONG: TRADE MANAGEMENT
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "08. Long: Trade Management")]
        public int LongMaxBarsInTrade { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Opposing Bar Exit", Order = 2, GroupName = "08. Long: Trade Management")]
        public bool LongEnableOpposingBarExit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Engulfing Exit", Order = 3, GroupName = "08. Long: Trade Management")]
        public bool LongEnableEngulfingExit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "08. Long: Trade Management")]
        public bool LongEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "08. Long: Trade Management")]
        public int LongPriceOffsetReductionTicks { get; set; }

        // ════════════════════════════════════════
        //  9. LONG: ENTRY FILTERS
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "09. Long: Entry Filters")]
        public bool LongRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "09. Long: Entry Filters")]
        public bool LongAllowSameDirectionAfterLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Distance From MA (Ticks)", Description = "0 = disabled.", Order = 3, GroupName = "09. Long: Entry Filters")]
        public int LongMaxDistanceFromSmaTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "09. Long: Entry Filters")]
        public bool LongRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "09. Long: Entry Filters")]
        public int LongSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "09. Long: Entry Filters")]
        public bool LongEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "09. Long: Entry Filters")]
        public int LongWmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Signal Candle (Ticks)", Description = "0 = disabled.", Order = 8, GroupName = "09. Long: Entry Filters")]
        public int LongMaxSignalCandleTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "09. Long: Entry Filters")]
        public double LongMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "09. Long: Entry Filters")]
        public int LongTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "09. Long: Entry Filters")]
        public bool LongEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "09. Long: Entry Filters")]
        public int LongAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "09. Long: Entry Filters")]
        public int LongAdxMinLevel { get; set; }

        // ════════════════════════════════════════
        //  10. LONG: EARLY EXIT FILTERS
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Long if Close <= EMA - offset.", Order = 1, GroupName = "10. Long: Early Exit Filters")]
        public bool LongUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "10. Long: Early Exit Filters")]
        public int LongEmaEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (Ticks)", Description = "0 = exit on any close beyond EMA.", Order = 3, GroupName = "10. Long: Early Exit Filters")]
        public int LongEmaEarlyExitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Long if ADX drops below minimum.", Order = 4, GroupName = "10. Long: Early Exit Filters")]
        public bool LongUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "10. Long: Early Exit Filters")]
        public int LongAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "10. Long: Early Exit Filters")]
        public double LongAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Long if ADX drops X units from peak.", Order = 7, GroupName = "10. Long: Early Exit Filters")]
        public bool LongUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "10. Long: Early Exit Filters")]
        public double LongAdxDrawdownFromPeak { get; set; }

        // ════════════════════════════════════════
        //  11. SHORT: MA SETTINGS
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA Period", Order = 1, GroupName = "11. Short: MA Settings")]
        public int ShortMaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "11. Short: MA Settings")]
        public int ShortEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MA Type", Order = 3, GroupName = "11. Short: MA Settings")]
        public MAMode ShortMaType { get; set; }

        // ════════════════════════════════════════
        //  12. SHORT: ENTRY
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Entry Mode", Order = 1, GroupName = "12. Short: Entry")]
        public MichalEntryMode ShortEntryMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Limit Offset (Ticks)", Order = 2, GroupName = "12. Short: Entry")]
        public int ShortLimitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "Limit Retracement %", Order = 3, GroupName = "12. Short: Entry")]
        public double ShortLimitRetracementPct { get; set; }

        // ════════════════════════════════════════
        //  13. SHORT: TAKE PROFIT
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Take Profit Mode", Order = 1, GroupName = "13. Short: Take Profit")]
        public MichalTPMode ShortTakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "TP Fixed Ticks", Order = 2, GroupName = "13. Short: Take Profit")]
        public int ShortTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max TP Ticks", Description = "0 = no cap.", Order = 3, GroupName = "13. Short: Take Profit")]
        public int ShortMaxTakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Swing Strength", Order = 4, GroupName = "13. Short: Take Profit")]
        public int ShortSwingStrength { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 50.0)]
        [Display(Name = "Candle Multiplier", Order = 5, GroupName = "13. Short: Take Profit")]
        public double ShortCandleMultiplier { get; set; }

        // ════════════════════════════════════════
        //  14. SHORT: STOP LOSS
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "SL Extra Ticks", Order = 1, GroupName = "14. Short: Stop Loss")]
        public int ShortSlExtraTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Offset Ticks", Order = 2, GroupName = "14. Short: Stop Loss")]
        public int ShortTrailOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Delay Bars", Order = 3, GroupName = "14. Short: Stop Loss")]
        public int ShortTrailDelayBars { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max SL Ticks", Description = "0 = disabled.", Order = 4, GroupName = "14. Short: Stop Loss")]
        public int ShortMaxSlTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 10.0)]
        [Display(Name = "Max SL:TP Ratio", Description = "0 = disabled.", Order = 5, GroupName = "14. Short: Stop Loss")]
        public double ShortMaxSlToTpRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Prior Candle SL", Order = 6, GroupName = "14. Short: Stop Loss")]
        public bool ShortUsePriorCandleSl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL At MA", Order = 7, GroupName = "14. Short: Stop Loss")]
        public bool ShortSlAtMa { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Move SL To Entry Bar", Order = 8, GroupName = "14. Short: Stop Loss")]
        public bool ShortMoveSlToEntryBar { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Trail Candle Offset", Description = "0 = disabled.", Order = 9, GroupName = "14. Short: Stop Loss")]
        public int ShortTrailCandleOffset { get; set; }

        // ════════════════════════════════════════
        //  15. SHORT: BREAKEVEN
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Enable Breakeven", Order = 1, GroupName = "15. Short: Breakeven")]
        public bool ShortEnableBreakeven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Breakeven Mode", Order = 2, GroupName = "15. Short: Breakeven")]
        public BEMode2 ShortBreakevenMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Order = 3, GroupName = "15. Short: Breakeven")]
        public int ShortBreakevenTriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100.0)]
        [Display(Name = "BE Candle %", Order = 4, GroupName = "15. Short: Breakeven")]
        public double ShortBreakevenCandlePct { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "0 = exact entry.", Order = 5, GroupName = "15. Short: Breakeven")]
        public int ShortBreakevenOffsetTicks { get; set; }

        // ════════════════════════════════════════
        //  16. SHORT: TRADE MANAGEMENT
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Bars In Trade", Description = "0 = disabled.", Order = 1, GroupName = "16. Short: Trade Management")]
        public int ShortMaxBarsInTrade { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Opposing Bar Exit", Order = 2, GroupName = "16. Short: Trade Management")]
        public bool ShortEnableOpposingBarExit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Engulfing Exit", Order = 3, GroupName = "16. Short: Trade Management")]
        public bool ShortEnableEngulfingExit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Price-Offset Trail", Order = 4, GroupName = "16. Short: Trade Management")]
        public bool ShortEnablePriceOffsetTrail { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Price-Offset Reduction (Ticks)", Order = 5, GroupName = "16. Short: Trade Management")]
        public int ShortPriceOffsetReductionTicks { get; set; }

        // ════════════════════════════════════════
        //  17. SHORT: ENTRY FILTERS
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Require Direction Flip", Order = 1, GroupName = "17. Short: Entry Filters")]
        public bool ShortRequireDirectionFlip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow Same Dir After Loss", Order = 2, GroupName = "17. Short: Entry Filters")]
        public bool ShortAllowSameDirectionAfterLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Distance From MA (Ticks)", Description = "0 = disabled.", Order = 3, GroupName = "17. Short: Entry Filters")]
        public int ShortMaxDistanceFromSmaTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require MA Slope", Order = 4, GroupName = "17. Short: Entry Filters")]
        public bool ShortRequireSmaSlope { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "MA Slope Lookback", Order = 5, GroupName = "17. Short: Entry Filters")]
        public int ShortSmaSlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable WMA Filter", Order = 6, GroupName = "17. Short: Entry Filters")]
        public bool ShortEnableWmaFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "WMA Period", Order = 7, GroupName = "17. Short: Entry Filters")]
        public int ShortWmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Signal Candle (Ticks)", Description = "0 = disabled.", Order = 8, GroupName = "17. Short: Entry Filters")]
        public int ShortMaxSignalCandleTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Min Body %", Description = "0 = disabled.", Order = 9, GroupName = "17. Short: Entry Filters")]
        public double ShortMinBodyPct { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Trend Confirm Bars", Description = "0 = disabled.", Order = 10, GroupName = "17. Short: Entry Filters")]
        public int ShortTrendConfirmBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable ADX Filter", Order = 11, GroupName = "17. Short: Entry Filters")]
        public bool ShortEnableAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ADX Period", Order = 12, GroupName = "17. Short: Entry Filters")]
        public int ShortAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "ADX Min Level", Order = 13, GroupName = "17. Short: Entry Filters")]
        public int ShortAdxMinLevel { get; set; }

        // ════════════════════════════════════════
        //  18. SHORT: EARLY EXIT FILTERS
        // ════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Use EMA Early Exit", Description = "Exit losing Short if Close >= EMA + offset.", Order = 1, GroupName = "18. Short: Early Exit Filters")]
        public bool ShortUseEmaEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 500)]
        [Display(Name = "EMA Period", Order = 2, GroupName = "18. Short: Early Exit Filters")]
        public int ShortEmaEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "EMA Offset (Ticks)", Description = "0 = exit on any close beyond EMA.", Order = 3, GroupName = "18. Short: Early Exit Filters")]
        public int ShortEmaEarlyExitOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Early Exit", Description = "Exit losing Short if ADX drops below minimum.", Order = 4, GroupName = "18. Short: Early Exit Filters")]
        public bool ShortUseAdxEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "ADX Early Exit Period", Order = 5, GroupName = "18. Short: Early Exit Filters")]
        public int ShortAdxEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Early Exit Min Level", Description = "0 = disabled.", Order = 6, GroupName = "18. Short: Early Exit Filters")]
        public double ShortAdxEarlyExitMin { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use ADX Drawdown Exit", Description = "Exit losing Short if ADX drops X units from peak.", Order = 7, GroupName = "18. Short: Early Exit Filters")]
        public bool ShortUseAdxDrawdownExit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Drawdown From Peak", Order = 8, GroupName = "18. Short: Early Exit Filters")]
        public double ShortAdxDrawdownFromPeak { get; set; }

        #endregion
    }

    #region Enums

    public enum MAMode
    {
        SMA,
        EMA,
        Both
    }

    public enum MichalEntryMode
    {
        Market,
        LimitOffset,
        LimitRetracement
    }

    public enum MichalTPMode
    {
        FixedTicks,
        SwingPoint,
        CandleMultiple
    }

    public enum BEMode2
    {
        FixedTicks,
        CandlePercent
    }

    #endregion
}
