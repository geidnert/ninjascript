#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class DuoEMA : Strategy
    {
        public DuoEMA()
        {
            VendorLicense(337);
        }

        public enum InitialStopMode
        {
            CandleOpen,
            WickExtreme
        }

        public enum FlipStopMode
        {
            CandleOpen,
            WickExtreme
        }

        private enum SessionSlot
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

        public enum ExitMode
        {
            EmaOnly,
            OppositeCandlesOnly,
            Both
        }

        private Order longEntryOrder;
        private Order shortEntryOrder;
        private int missingLongEntryOrderBars;
        private int missingShortEntryOrderBars;

        private bool asiaSessionClosed;
        private bool londonSessionClosed;
        private bool newYorkSessionClosed;

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

        private EMA emaAsia;
        private EMA emaLondon;
        private EMA emaNewYork;
        private EMA activeEma;
        private DM adxAsia;
        private DM adxLondon;
        private DM adxNewYork;
        private DM activeAdx;

        private int activeEmaPeriod;
        private int activeContracts;
        private double activeSignalBodyThresholdPercent;
        private InitialStopMode activeEntryStopMode;
        private double activeExitCrossPoints;
        private double activeTakeProfitPoints;
        private double activeFlipBodyThresholdPercent;
        private FlipStopMode activeFlipStopSetting;
        private double activeMinEntryBodySize;
        private bool activeRequireEmaTouch;
        private int activeAdxPeriod;
        private double activeAdxThreshold;
        private double activeAdxMaxThreshold;
        private double activeDiMinSpread;
        private double activeContractDoublerStopThresholdPoints;
        private double activeEmaMinSlopePointsPerBar;
        private double activeAdxMinSlopePoints;
        private double activeAdxPeakDrawdownExitUnits;
        private double activeAdxAbsoluteExitLevel;
        private double activeProfitPeakDrawdownExitPoints;
        private double activeStopPaddingPoints;

        private double pendingLongStopForWebhook;
        private double pendingShortStopForWebhook;
        private double currentTradePeakAdx;
        private MarketPosition trackedAdxPeakPosition = MarketPosition.Flat;
        private double currentTradePeakProfitPoints;
        private MarketPosition trackedProfitPeakPosition = MarketPosition.Flat;
        private string projectXSessionToken;
        private DateTime projectXTokenAcquiredUtc = Core.Globals.MinDate;
        private int? projectXLastOrderId;
        private string projectXLastOrderContractId;
        private bool accountBalanceLimitReached;
        private int accountBalanceLimitReachedBar = -1;
        private TimeZoneInfo targetTimeZone;
        private TimeZoneInfo londonTimeZone;

        private static readonly string NewsDatesRaw =
@"2025-01-10,08:30
2025-01-14,08:30
2025-01-15,08:30
2025-02-07,08:30
2025-02-12,08:30
2025-03-07,08:30
2025-03-12,08:30
2025-04-04,08:30
2025-04-10,08:30
2025-05-02,08:30
2025-05-13,08:30
2025-06-06,08:30
2025-06-11,08:30
2025-07-03,08:30
2025-07-15,08:30
2025-08-01,08:30
2025-08-12,08:30
2025-09-04,08:30
2025-09-11,08:30
2025-11-20,08:30
2025-12-03,08:30
2026-01-09,08:30
2026-01-13,08:30
2026-02-06,08:30
2026-02-10,08:30
2026-02-11,08:30
2026-03-05,08:30
2026-03-11,08:30
2026-04-03,08:30
2026-04-10,08:30
2026-05-07,08:30
2026-05-12,08:30
2026-06-05,08:30
2026-06-10,08:30
2026-07-02,08:30
2026-07-14,08:30
2026-08-06,08:30
2026-08-12,08:30
2026-09-03,08:30
2026-09-11,08:30
2026-10-02,08:30
2026-10-14,08:30
2026-11-05,08:30
2026-11-10,08:30
2026-12-04,08:30
2025-01-28,14:00
2025-03-19,14:00
2025-05-07,14:00
2025-06-18,14:00
2025-07-30,14:00
2025-09-17,14:00
2025-10-29,14:00
2025-12-10,14:00
2026-01-28,14:00
2026-03-18,14:00
2026-04-29,14:00
2026-06-17,14:00
2026-07-29,14:00
2026-09-15,14:00
2026-10-27,14:00
2026-12-09,14:00";

        private static readonly List<DateTime> NewsDates = new List<DateTime>();
        private static bool newsDatesInitialized;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DuoEMA";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = false;
                IsInstantiatedOnEachOptimizationIteration = false;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;

                UseAsiaSession = true;
                AsiaSessionStart = new TimeSpan(18, 30, 0);
                AsiaSessionEnd = new TimeSpan(2, 00, 0);
                AsiaNoTradesAfter = new TimeSpan(2, 00, 0);
                AsiaEmaPeriod = 21;
                AsiaContracts = 1;
                AsiaSignalBodyThresholdPercent = 0;
                AsiaRequireEmaTouch = false;
                AsiaAdxPeriod = 14;
                AsiaAdxThreshold = 0.6;
                AsiaAdxMaxThreshold = 36.9;
                AsiaAdxMinSlopePoints = 1.6;
                AsiaAdxPeakDrawdownExitUnits = 13.8;
                AsiaProfitPeakDrawdownExitPoints = 0.0;
                AsiaEmaMinSlopePointsPerBar = 0.0;
                AsiaEntryStopMode = InitialStopMode.WickExtreme;
                AsiaStopPaddingPoints = 13.2;
                AsiaExitCrossPoints = 3.8;
                AsiaTakeProfitPoints = 71;
                AsiaFlipBodyThresholdPercent = 0.0;
                AsiaFlipStopSetting = FlipStopMode.CandleOpen;
                AsiaMinEntryBodySize = 0.0;
                AsiaContractDoublerStopThresholdPoints = 0.0;
                AsiaDiMinSpread = 0.0;
                AsiaAdxAbsoluteExitLevel = 56;

                UseLondonSession = false;
                LondonSessionStart = new TimeSpan(3, 00, 0);
                LondonSessionEnd = new TimeSpan(7, 30, 0);
                LondonNoTradesAfter = new TimeSpan(7, 30, 0);
                AutoShiftLondon = true;
                LondonEmaPeriod = 21;
                LondonContracts = 1;
                LondonSignalBodyThresholdPercent = 0.0;
                LondonRequireEmaTouch = false;
                LondonAdxPeriod = 14;
                LondonAdxThreshold = 0.0;
                LondonAdxMaxThreshold = 0.0;
                LondonAdxMinSlopePoints = 1.5;
                LondonAdxPeakDrawdownExitUnits = 1.2;
                LondonProfitPeakDrawdownExitPoints = 0.0;
                LondonEmaMinSlopePointsPerBar = 0.0;
                LondonEntryStopMode = InitialStopMode.WickExtreme;
                LondonStopPaddingPoints = 24;
                LondonExitCrossPoints = 6.5;
                LondonTakeProfitPoints = 0.0;
                LondonFlipBodyThresholdPercent = 0;
                LondonFlipStopSetting = FlipStopMode.CandleOpen;
                LondonMinEntryBodySize = 0.0;
                LondonContractDoublerStopThresholdPoints = 0.0;
                LondonDiMinSpread = 0.0;
                LondonAdxAbsoluteExitLevel = 0.0;

                UseNewYorkSession = true;
                NewYorkSessionStart = new TimeSpan(9, 40, 0);
                NewYorkSessionEnd = new TimeSpan(15, 00, 0);
                NewYorkNoTradesAfter = new TimeSpan(15, 00, 0);
                AutoShiftNewYork = false;
                NewYorkEmaPeriod = 21;
                NewYorkContracts = 1;
                NewYorkSignalBodyThresholdPercent = 0;
                NewYorkRequireEmaTouch = false;
                NewYorkAdxPeriod = 14;
                NewYorkAdxThreshold = 14;
                NewYorkAdxMaxThreshold = 47.1;
                NewYorkAdxMinSlopePoints = 1.5;
                NewYorkAdxPeakDrawdownExitUnits = 16.5;
                NewYorkProfitPeakDrawdownExitPoints = 0.0;
                NewYorkEmaMinSlopePointsPerBar = 0.0;
                NewYorkEntryStopMode = InitialStopMode.WickExtreme;
                NewYorkStopPaddingPoints = 16.75;
                NewYorkExitCrossPoints = 2.75;
                NewYorkTakeProfitPoints = 68;
                NewYorkFlipBodyThresholdPercent = 0.0;
                NewYorkFlipStopSetting = FlipStopMode.CandleOpen;
                NewYorkMinEntryBodySize = 0.0;
                NewYorkContractDoublerStopThresholdPoints = 0.0;
                NewYorkDiMinSpread = 0.0;
                NewYorkAdxAbsoluteExitLevel = 60;

                CloseAtSessionEnd = false;
                SessionBrush = Brushes.Gold;
                ShowNoTradesAfterLine = true;
                ShowEmaOnChart = true;
                ShowAdxOnChart = true;
                ShowAdxThresholdLines = true;

                UseNewsSkip = true;
                NewsBlockMinutes = 1;

                WebhookUrl = string.Empty;
                WebhookProviderType = WebhookProvider.TradersPost;
                ProjectXApiBaseUrl = "https://gateway-api-demo.s2f.projectx.com";
                ProjectXUsername = string.Empty;
                ProjectXApiKey = string.Empty;
                ProjectXAccountId = string.Empty;
                ProjectXContractId = string.Empty;
                MaxAccountBalance = 0.0;
                MaxEntryDistanceFromEmaPoints = 0.0;
                RequireEntryConfirmation = false;
                OppositeCandleExitCount = 0;
                PositionExitMode = ExitMode.EmaOnly;

                DebugLogging = false;
            }
            else if (State == State.DataLoaded)
            {
                emaAsia = EMA(AsiaEmaPeriod);
                emaLondon = EMA(LondonEmaPeriod);
                emaNewYork = EMA(NewYorkEmaPeriod);
                adxAsia = DM(AsiaAdxPeriod);
                adxLondon = DM(LondonAdxPeriod);
                adxNewYork = DM(NewYorkAdxPeriod);
                UpdateAdxReferenceLines(adxAsia, AsiaAdxThreshold, AsiaAdxMaxThreshold);
                UpdateAdxReferenceLines(adxLondon, LondonAdxThreshold, LondonAdxMaxThreshold);
                UpdateAdxReferenceLines(adxNewYork, NewYorkAdxThreshold, NewYorkAdxMaxThreshold);

                if (ShowEmaOnChart)
                {
                    AddChartIndicator(emaAsia);
                    AddChartIndicator(emaLondon);
                    AddChartIndicator(emaNewYork);
                }

                AddChartIndicator(adxAsia);
                AddChartIndicator(adxLondon);
                AddChartIndicator(adxNewYork);

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
                ApplyInputsForSession(activeSession);
                UpdateEmaPlotVisibility();
                UpdateAdxPlotVisibility();
                pendingLongStopForWebhook = 0.0;
                pendingShortStopForWebhook = 0.0;
                currentTradePeakAdx = 0.0;
                trackedAdxPeakPosition = MarketPosition.Flat;
                currentTradePeakProfitPoints = 0.0;
                trackedProfitPeakPosition = MarketPosition.Flat;
                projectXSessionToken = null;
                projectXTokenAcquiredUtc = Core.Globals.MinDate;
                projectXLastOrderId = null;
                projectXLastOrderContractId = null;
                accountBalanceLimitReached = false;
                accountBalanceLimitReachedBar = -1;

                EnsureNewsDatesInitialized();

                LogDebug(
                    string.Format(
                        "DataLoaded | ActiveSession={0} EMA={1} ADX={2}/{3:0.##} Contracts={4} Body%={5:0.##} TouchEMA={6} ExitCross={7:0.##} EntryStop={8} FlipStop={9} FlipBody%={10:0.##}",
                        FormatSessionLabel(activeSession),
                        activeEmaPeriod,
                        activeAdxPeriod,
                        activeAdxThreshold,
                        activeContracts,
                        activeSignalBodyThresholdPercent,
                        activeRequireEmaTouch,
                        activeExitCrossPoints,
                        activeEntryStopMode,
                        activeFlipStopSetting,
                        activeFlipBodyThresholdPercent));
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(1, Math.Max(GetMaxConfiguredEmaPeriod(), GetMaxConfiguredAdxPeriod())))
                return;

            DrawSessionBackgrounds();
            DrawNewsWindows(Time[0]);
            UpdateTradeLines();
            UpdateInfo();

            ProcessSessionTransitions(SessionSlot.Asia);
            ProcessSessionTransitions(SessionSlot.London);
            ProcessSessionTransitions(SessionSlot.NewYork);
            ReconcileTrackedEntryOrders();

            UpdateActiveSession(Time[0]);
            UpdateEmaPlotVisibility();
            UpdateAdxPlotVisibility();

            bool inNewsSkipNow = TimeInNewsSkip(Time[0]);
            bool inNewsSkipPrev = CurrentBar > 0 && TimeInNewsSkip(Time[1]);
            if (!inNewsSkipPrev && inNewsSkipNow)
            {
                CancelWorkingEntryOrders();
                LogDebug("Entered news block: canceling working entries.");
            }

            bool inActiveSessionNow = activeSession != SessionSlot.None && TimeInSession(activeSession, Time[0]);
            bool inNoTradesNow = inActiveSessionNow && TimeInNoTradesAfter(activeSession, Time[0]);
            bool accountBlocked = IsAccountBalanceBlocked();
            double adxValue = activeAdx != null ? activeAdx[0] : 0.0;
            UpdateAdxPeakTracker(adxValue);
            UpdateProfitPeakTracker(Close[0]);
            double adxSlope = GetAdxSlopePoints();
            bool adxMinPass = !inActiveSessionNow || activeAdxThreshold <= 0.0 || adxValue >= activeAdxThreshold;
            bool adxMaxPass = !inActiveSessionNow || activeAdxMaxThreshold <= 0.0 || adxValue <= activeAdxMaxThreshold;
            bool adxThresholdPass = adxMinPass && adxMaxPass;
            bool adxSlopePass = !inActiveSessionNow || activeAdxMinSlopePoints <= 0.0 || adxSlope >= activeAdxMinSlopePoints;
            bool adxPass = adxThresholdPass && adxSlopePass;
            bool canTradeNow = inActiveSessionNow && !inNoTradesNow && !inNewsSkipNow && !accountBlocked && adxPass;

            if (CurrentBar > 0)
            {
                SessionSlot prevSession = DetermineSessionForTime(Time[1]);
                bool inNoTradesPrev = prevSession != SessionSlot.None && TimeInNoTradesAfter(prevSession, Time[1]);
                if (!inNoTradesPrev && inNoTradesNow)
                {
                    CancelWorkingEntryOrders();
                    LogDebug("NoTradesAfter crossed: canceling working entries.");
                }
            }

            if (!inActiveSessionNow)
                CancelWorkingEntryOrders();

            if (inNoTradesNow)
                CancelWorkingEntryOrders();

            if (activeEma == null || CurrentBar < activeEmaPeriod)
                return;

            double emaValue = activeEma[0];
            bool emaTouched = CandleTouchesEma(High[0], Low[0], emaValue);

            double bodySize = Math.Abs(Close[0] - Open[0]);
            bool bullish = Close[0] > Open[0];
            bool bearish = Close[0] < Open[0];
            double bodyAbovePercent = GetBodyPercentAboveEma(Open[0], Close[0], emaValue);
            double bodyBelowPercent = GetBodyPercentBelowEma(Open[0], Close[0], emaValue);
            double emaDistancePoints = Math.Abs(Close[0] - emaValue);
            double diPlus;
            double diMinus;
            bool hasDiValues = TryGetAdxDirectionalValues(out diPlus, out diMinus);
            double diSpread = hasDiValues ? Math.Abs(diPlus - diMinus) : 0.0;
            bool diFilterEnabled = activeDiMinSpread > 0.0;
            bool diLongPass = !diFilterEnabled || (hasDiValues && diPlus > diMinus && diSpread >= activeDiMinSpread);
            bool diShortPass = !diFilterEnabled || (hasDiValues && diMinus > diPlus && diSpread >= activeDiMinSpread);
            bool emaSlopeLongPass = EmaSlopePassesLong();
            bool emaSlopeShortPass = EmaSlopePassesShort();

            bool bodySizePasses = bodySize >= activeMinEntryBodySize;
            bool touchPasses = !activeRequireEmaTouch || emaTouched;
            bool distancePasses = MaxEntryDistanceFromEmaPoints <= 0.0 || emaDistancePoints <= MaxEntryDistanceFromEmaPoints;
            bool longEmaSidePass = Close[0] > emaValue;
            bool shortEmaSidePass = Close[0] < emaValue;
            bool longPreDiSignal = bullish && bodySizePasses && touchPasses && emaSlopeLongPass && longEmaSidePass && bodyAbovePercent >= activeSignalBodyThresholdPercent;
            bool shortPreDiSignal = bearish && bodySizePasses && touchPasses && emaSlopeShortPass && shortEmaSidePass && bodyBelowPercent >= activeSignalBodyThresholdPercent;
            bool longBaseSignal = longPreDiSignal && diLongPass;
            bool shortBaseSignal = shortPreDiSignal && diShortPass;
            bool longSignal = longBaseSignal && distancePasses;
            bool shortSignal = shortBaseSignal && distancePasses;

            if (DebugLogging && canTradeNow && !distancePasses && (longBaseSignal || shortBaseSignal))
            {
                LogDebug(string.Format(
                    "Setup blocked | reason=EmaDistance side={0} distancePts={1:0.00} maxPts={2:0.00} session={3}",
                    longBaseSignal ? "Long" : "Short",
                    emaDistancePoints,
                    MaxEntryDistanceFromEmaPoints,
                    FormatSessionLabel(activeSession)));
            }

            if (DebugLogging && diFilterEnabled && !hasDiValues && (longPreDiSignal || shortPreDiSignal))
                LogDebug(string.Format("Setup blocked | reason=DiUnavailable session={0} diMinSpread={1:0.00}", FormatSessionLabel(activeSession), activeDiMinSpread));

            if (DebugLogging && diFilterEnabled && hasDiValues && ((longPreDiSignal && !diLongPass) || (shortPreDiSignal && !diShortPass)))
            {
                string blockedSide = longPreDiSignal && !diLongPass ? "Long" : "Short";
                LogDebug(string.Format(
                    "Setup blocked | reason=DiFilter side={0} +DI={1:0.00} -DI={2:0.00} spread={3:0.00} minSpread={4:0.00} session={5}",
                    blockedSide,
                    diPlus,
                    diMinus,
                    diSpread,
                    activeDiMinSpread,
                    FormatSessionLabel(activeSession)));
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (activeAdxAbsoluteExitLevel > 0.0 && adxValue >= activeAdxAbsoluteExitLevel)
                {
                    ExitLong("AdxLevelExit", "LongEntry");
                    LogDebug(string.Format("Exit LONG | reason=AdxLevel adx={0:0.00} threshold={1:0.00}",
                        adxValue, activeAdxAbsoluteExitLevel));
                    return;
                }

                double adxDrawdown;
                if (ShouldExitOnAdxDrawdown(adxValue, out adxDrawdown))
                {
                    ExitLong("AdxDrawdownExit", "LongEntry");
                    LogDebug(string.Format("Exit LONG | reason=AdxDrawdown adx={0:0.00} peak={1:0.00} drawdown={2:0.00} threshold={3:0.00}",
                        adxValue, currentTradePeakAdx, adxDrawdown, activeAdxPeakDrawdownExitUnits));
                    return;
                }

                double profitDrawdown;
                double currentProfitPoints;
                if (ShouldExitOnProfitPeakDrawdown(out profitDrawdown, out currentProfitPoints))
                {
                    ExitLong("ProfitDrawdownExit", "LongEntry");
                    LogDebug(string.Format("Exit LONG | reason=ProfitDrawdown currentPts={0:0.00} peakPts={1:0.00} drawdownPts={2:0.00} thresholdPts={3:0.00}",
                        currentProfitPoints, currentTradePeakProfitPoints, profitDrawdown, activeProfitPeakDrawdownExitPoints));
                    return;
                }

                if (activeTakeProfitPoints > 0.0 && Close[0] >= Position.AveragePrice + activeTakeProfitPoints)
                {
                    ExitLong("TakeProfitExit", "LongEntry");
                    LogDebug(string.Format("Exit LONG | reason=TakeProfit close={0:0.00} avg={1:0.00} tpPts={2:0.00}",
                        Close[0], Position.AveragePrice, activeTakeProfitPoints));
                    return;
                }

                bool useOppositeExit = PositionExitMode == ExitMode.Both || PositionExitMode == ExitMode.OppositeCandlesOnly;
                bool useEmaExit = PositionExitMode == ExitMode.Both || PositionExitMode == ExitMode.EmaOnly;

                if (useOppositeExit && ShouldExitOnOppositeCandles(MarketPosition.Long))
                {
                    ExitLong("OppositeCandleExit", "LongEntry");
                    LogDebug(string.Format("Exit LONG | reason=OppositeCandles count={0}", OppositeCandleExitCount));
                    return;
                }

                if (useEmaExit && Close[0] <= emaValue - activeExitCrossPoints)
                {
                    if (DebugLogging && canTradeNow && !distancePasses && EmaSlopePassesShort() && bodyBelowPercent >= activeFlipBodyThresholdPercent)
                    {
                        LogDebug(string.Format(
                            "Flip blocked | reason=EmaDistance side=Short distancePts={0:0.00} maxPts={1:0.00} session={2}",
                            emaDistancePoints,
                            MaxEntryDistanceFromEmaPoints,
                            FormatSessionLabel(activeSession)));
                    }

                    bool shouldFlip = canTradeNow && distancePasses && EmaSlopePassesShort() && bodyBelowPercent >= activeFlipBodyThresholdPercent;
                    if (shouldFlip)
                    {
                        CancelOrderIfActive(longEntryOrder, "FlipToShort");
                        CancelOrderIfActive(shortEntryOrder, "FlipToShort");

                        double stopPrice = BuildFlipShortStopPrice(Close[0], Open[0], High[0]);
                        int qty = GetEntryQuantity(Close[0], stopPrice);
                        if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", Close[0], qty))
                        {
                            LogDebug("Entry confirmation declined | Flip LONG->SHORT.");
                            return;
                        }
                        pendingShortStopForWebhook = stopPrice;
                        SetStopLossByDistanceTicks("ShortEntry", Close[0], stopPrice);
                        SetProfitTargetByDistanceTicks("ShortEntry", activeTakeProfitPoints);
                        SendWebhook("sell", Close[0], Close[0], stopPrice, true, qty);
                        StartTradeLines(Close[0], stopPrice, activeTakeProfitPoints > 0.0 ? Close[0] - activeTakeProfitPoints : 0.0, activeTakeProfitPoints > 0.0);
                        EnterShort(qty, "ShortEntry");

                        LogDebug(string.Format("Flip LONG->SHORT | close={0:0.00} ema={1:0.00} below%={2:0.0} stop={3:0.00} qty={4} {5}", Close[0], emaValue, bodyBelowPercent, stopPrice, qty, FormatDiForLog()));
                    }
                    else
                    {
                        ExitLong("ExitLong", "LongEntry");
                        LogDebug(string.Format("Exit LONG | close={0:0.00} ema={1:0.00} below%={2:0.0}", Close[0], emaValue, bodyBelowPercent));
                    }
                }

                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (activeAdxAbsoluteExitLevel > 0.0 && adxValue >= activeAdxAbsoluteExitLevel)
                {
                    ExitShort("AdxLevelExit", "ShortEntry");
                    LogDebug(string.Format("Exit SHORT | reason=AdxLevel adx={0:0.00} threshold={1:0.00}",
                        adxValue, activeAdxAbsoluteExitLevel));
                    return;
                }

                double adxDrawdown;
                if (ShouldExitOnAdxDrawdown(adxValue, out adxDrawdown))
                {
                    ExitShort("AdxDrawdownExit", "ShortEntry");
                    LogDebug(string.Format("Exit SHORT | reason=AdxDrawdown adx={0:0.00} peak={1:0.00} drawdown={2:0.00} threshold={3:0.00}",
                        adxValue, currentTradePeakAdx, adxDrawdown, activeAdxPeakDrawdownExitUnits));
                    return;
                }

                double profitDrawdown;
                double currentProfitPoints;
                if (ShouldExitOnProfitPeakDrawdown(out profitDrawdown, out currentProfitPoints))
                {
                    ExitShort("ProfitDrawdownExit", "ShortEntry");
                    LogDebug(string.Format("Exit SHORT | reason=ProfitDrawdown currentPts={0:0.00} peakPts={1:0.00} drawdownPts={2:0.00} thresholdPts={3:0.00}",
                        currentProfitPoints, currentTradePeakProfitPoints, profitDrawdown, activeProfitPeakDrawdownExitPoints));
                    return;
                }

                if (activeTakeProfitPoints > 0.0 && Close[0] <= Position.AveragePrice - activeTakeProfitPoints)
                {
                    ExitShort("TakeProfitExit", "ShortEntry");
                    LogDebug(string.Format("Exit SHORT | reason=TakeProfit close={0:0.00} avg={1:0.00} tpPts={2:0.00}",
                        Close[0], Position.AveragePrice, activeTakeProfitPoints));
                    return;
                }

                bool useOppositeExit = PositionExitMode == ExitMode.Both || PositionExitMode == ExitMode.OppositeCandlesOnly;
                bool useEmaExit = PositionExitMode == ExitMode.Both || PositionExitMode == ExitMode.EmaOnly;

                if (useOppositeExit && ShouldExitOnOppositeCandles(MarketPosition.Short))
                {
                    ExitShort("OppositeCandleExit", "ShortEntry");
                    LogDebug(string.Format("Exit SHORT | reason=OppositeCandles count={0}", OppositeCandleExitCount));
                    return;
                }

                if (useEmaExit && Close[0] >= emaValue + activeExitCrossPoints)
                {
                    if (DebugLogging && canTradeNow && !distancePasses && EmaSlopePassesLong() && bodyAbovePercent >= activeFlipBodyThresholdPercent)
                    {
                        LogDebug(string.Format(
                            "Flip blocked | reason=EmaDistance side=Long distancePts={0:0.00} maxPts={1:0.00} session={2}",
                            emaDistancePoints,
                            MaxEntryDistanceFromEmaPoints,
                            FormatSessionLabel(activeSession)));
                    }

                    bool shouldFlip = canTradeNow && distancePasses && EmaSlopePassesLong() && bodyAbovePercent >= activeFlipBodyThresholdPercent;
                    if (shouldFlip)
                    {
                        CancelOrderIfActive(longEntryOrder, "FlipToLong");
                        CancelOrderIfActive(shortEntryOrder, "FlipToLong");

                        double stopPrice = BuildFlipLongStopPrice(Close[0], Open[0], Low[0]);
                        int qty = GetEntryQuantity(Close[0], stopPrice);
                        if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", Close[0], qty))
                        {
                            LogDebug("Entry confirmation declined | Flip SHORT->LONG.");
                            return;
                        }
                        pendingLongStopForWebhook = stopPrice;
                        SetStopLossByDistanceTicks("LongEntry", Close[0], stopPrice);
                        SetProfitTargetByDistanceTicks("LongEntry", activeTakeProfitPoints);
                        SendWebhook("buy", Close[0], Close[0], stopPrice, true, qty);
                        StartTradeLines(Close[0], stopPrice, activeTakeProfitPoints > 0.0 ? Close[0] + activeTakeProfitPoints : 0.0, activeTakeProfitPoints > 0.0);
                        EnterLong(qty, "LongEntry");

                        LogDebug(string.Format("Flip SHORT->LONG | close={0:0.00} ema={1:0.00} above%={2:0.0} stop={3:0.00} qty={4} {5}", Close[0], emaValue, bodyAbovePercent, stopPrice, qty, FormatDiForLog()));
                    }
                    else
                    {
                        ExitShort("ExitShort", "ShortEntry");
                        LogDebug(string.Format("Exit SHORT | close={0:0.00} ema={1:0.00} above%={2:0.0}", Close[0], emaValue, bodyAbovePercent));
                    }
                }

                return;
            }

            if (!canTradeNow)
            {
                return;
            }

            bool longOrderActive = IsOrderActive(longEntryOrder);
            bool shortOrderActive = IsOrderActive(shortEntryOrder);

            if (longSignal)
            {
                BeginTradeAttempt("Long");
                LogDebug(string.Format("Setup ready | side=Long session={0} close={1:0.00} ema={2:0.00}", FormatSessionLabel(activeSession), Close[0], emaValue));

                CancelOrderIfActive(shortEntryOrder, "OppositeLongSignal");

                if (!longOrderActive)
                {
                    double entryPrice = Close[0];
                    double stopPrice = BuildLongEntryStopPrice(entryPrice, Open[0], Close[0], Low[0]);
                    int qty = GetEntryQuantity(entryPrice, stopPrice);
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", entryPrice, qty))
                    {
                        LogDebug("Entry confirmation declined | LONG.");
                        EndTradeAttempt("confirmation-declined");
                        return;
                    }

                    pendingLongStopForWebhook = stopPrice;
                    SetStopLossByDistanceTicks("LongEntry", entryPrice, stopPrice);
                    SetProfitTargetByDistanceTicks("LongEntry", activeTakeProfitPoints);
                    SendWebhook("buy", entryPrice, entryPrice, stopPrice, true, qty);
                    StartTradeLines(entryPrice, stopPrice, activeTakeProfitPoints > 0.0 ? entryPrice + activeTakeProfitPoints : 0.0, activeTakeProfitPoints > 0.0);
                    EnterLong(qty, "LongEntry");
                    LogDebug(string.Format("Place LONG market | session={0} stop={1:0.00} qty={2} {3}", FormatSessionLabel(activeSession), stopPrice, qty, FormatDiForLog()));
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

                if (!shortOrderActive)
                {
                    double entryPrice = Close[0];
                    double stopPrice = BuildShortEntryStopPrice(entryPrice, Open[0], Close[0], High[0]);
                    int qty = GetEntryQuantity(entryPrice, stopPrice);
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", entryPrice, qty))
                    {
                        LogDebug("Entry confirmation declined | SHORT.");
                        EndTradeAttempt("confirmation-declined");
                        return;
                    }

                    pendingShortStopForWebhook = stopPrice;
                    SetStopLossByDistanceTicks("ShortEntry", entryPrice, stopPrice);
                    SetProfitTargetByDistanceTicks("ShortEntry", activeTakeProfitPoints);
                    SendWebhook("sell", entryPrice, entryPrice, stopPrice, true, qty);
                    StartTradeLines(entryPrice, stopPrice, activeTakeProfitPoints > 0.0 ? entryPrice - activeTakeProfitPoints : 0.0, activeTakeProfitPoints > 0.0);
                    EnterShort(qty, "ShortEntry");
                    LogDebug(string.Format("Place SHORT market | session={0} stop={1:0.00} qty={2} {3}", FormatSessionLabel(activeSession), stopPrice, qty, FormatDiForLog()));
                }
                else if (DebugLogging)
                {
                    LogDebug(string.Format("SHORT signal skipped | reason=shortOrderActive tracked={0}", FormatOrderRef(shortEntryOrder)));
                    EndTradeAttempt("entry-order-active");
                }
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;

            if (order.Name == "LongEntry")
                longEntryOrder = order;
            else if (order.Name == "ShortEntry")
                shortEntryOrder = order;

            if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected || orderState == OrderState.Filled)
            {
                if (order == longEntryOrder)
                    longEntryOrder = null;
                else if (order == shortEntryOrder)
                    shortEntryOrder = null;
            }

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

            bool isEntryOrder = order.Name == "LongEntry" || order.Name == "ShortEntry";
            if (isEntryOrder && orderState != OrderState.Filled &&
                (orderState == OrderState.Cancelled || orderState == OrderState.Rejected) &&
                Position.MarketPosition == MarketPosition.Flat)
            {
                if (tradeLinesActive)
                    FinalizeTradeLines();
                EndTradeAttempt("entry-" + orderState);
            }

            if (orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                UpdateInfo();
        }

        private void HandleOrderRejected(Order order, ErrorCode error, string comment)
        {
            string name = order != null ? (order.Name ?? string.Empty) : string.Empty;
            string state = order != null ? order.OrderState.ToString() : "Unknown";
            Print(string.Format("{0} | DuoEMA | bar={1} | Order rejected guard | name={2} state={3} error={4} comment={5}",
                Time[0], CurrentBar, name, state, error, comment ?? string.Empty));

            if (name == "LongEntry" || name == "ShortEntry")
            {
                CancelWorkingEntryOrders();
                EndTradeAttempt("entry-rejected");
                return;
            }

            if (name == "Stop loss" || name == "Profit target")
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("ProtectiveReject", "LongEntry");
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("ProtectiveReject", "ShortEntry");
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
            double fillPrice = Instrument.MasterInstrument.RoundToTickSize(price);

            if (orderName == "LongEntry" || orderName == "ShortEntry")
            {
                SessionSlot entrySession = activeSession != SessionSlot.None
                    ? activeSession
                    : DetermineSessionForTime(time);
                if (entrySession != SessionSlot.None)
                {
                    lockedTradeSession = entrySession;
                    LogDebug(string.Format("Session lock set to {0} on {1} fill.", FormatSessionLabel(lockedTradeSession), orderName));
                }
            }
            else if (marketPosition == MarketPosition.Flat && lockedTradeSession != SessionSlot.None)
            {
                LogDebug(string.Format("Session lock released from {0} after flat execution ({1}).", FormatSessionLabel(lockedTradeSession), orderName));
                lockedTradeSession = SessionSlot.None;
            }

            if (ShouldSendExitWebhook(execution, orderName, marketPosition))
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

            if (orderName != "LongEntry" && orderName != "ShortEntry")
            {
                if (tradeLinesActive)
                    FinalizeTradeLines();
                EndTradeAttempt("exit-" + orderName);
            }

            UpdateInfo();
        }

        private bool IsEntryOrderName(string orderName)
        {
            return orderName == "LongEntry" || orderName == "ShortEntry";
        }

        private bool ShouldSendExitWebhook(Execution execution, string orderName, MarketPosition marketPosition)
        {
            if (execution?.Order == null)
                return false;

            if (IsEntryOrderName(orderName))
                return false;

            string fromEntry = execution.Order.FromEntrySignal ?? string.Empty;
            if (fromEntry == "LongEntry" || fromEntry == "ShortEntry")
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
                || normalized.Equals("AdxDrawdownExit", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("AdxLevelExit", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("TakeProfitExit", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("ProfitDrawdownExit", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("OppositeCandleExit", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("ProtectiveReject", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("TwoCandleReverse", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return marketPosition == MarketPosition.Flat;
        }

        private void StartTradeLines(double entryPrice, double stopPrice, double takeProfitPrice, bool hasTakeProfit)
        {
            if (tradeLinesActive)
                FinalizeTradeLines();

            tradeLineTagPrefix = string.Format("DuoEMA_TradeLine_{0}_{1}_", ++tradeLineTagCounter, CurrentBar);
            tradeLinesActive = true;
            tradeLineHasTp = hasTakeProfit;
            tradeLineSignalBar = Math.Max(0, CurrentBar - 1);
            tradeLineExitBar = -1;
            tradeLineEntryPrice = Instrument.MasterInstrument.RoundToTickSize(entryPrice);
            tradeLineSlPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            tradeLineTpPrice = hasTakeProfit ? Instrument.MasterInstrument.RoundToTickSize(takeProfitPrice) : 0.0;

            DrawTradeLinesAtBarsAgo(1, 0);
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
        }

        private void FinalizeTradeLines()
        {
            if (!tradeLinesActive)
                return;

            tradeLineExitBar = CurrentBar;
            UpdateTradeLines();
            tradeLinesActive = false;
            ClearTradeLineState();
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

        private void DrawTradeLinesAtBarsAgo(int startBarsAgo, int endBarsAgo)
        {
            if (string.IsNullOrEmpty(tradeLineTagPrefix))
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

        private SessionSlot DetermineSessionForTime(DateTime time)
        {
            TimeSpan now = time.TimeOfDay;
            TimeSpan asiaStart = TimeSpan.Zero;
            TimeSpan asiaEnd = TimeSpan.Zero;
            TimeSpan londonStart = TimeSpan.Zero;
            TimeSpan londonEnd = TimeSpan.Zero;
            TimeSpan nyStart = TimeSpan.Zero;
            TimeSpan nyEnd = TimeSpan.Zero;

            bool asiaConfigured = IsSessionConfigured(SessionSlot.Asia)
                && TryGetSessionWindow(SessionSlot.Asia, time, out asiaStart, out asiaEnd);
            bool londonConfigured = IsSessionConfigured(SessionSlot.London)
                && TryGetSessionWindow(SessionSlot.London, time, out londonStart, out londonEnd);
            bool newYorkConfigured = IsSessionConfigured(SessionSlot.NewYork)
                && TryGetSessionWindow(SessionSlot.NewYork, time, out nyStart, out nyEnd);

            if (asiaConfigured && IsTimeInRange(now, asiaStart, asiaEnd))
                return SessionSlot.Asia;
            if (londonConfigured && IsTimeInRange(now, londonStart, londonEnd))
                return SessionSlot.London;
            if (newYorkConfigured && IsTimeInRange(now, nyStart, nyEnd))
                return SessionSlot.NewYork;

            if (!asiaConfigured && !londonConfigured && !newYorkConfigured)
                return SessionSlot.None;

            DateTime nextAsiaStart = DateTime.MaxValue;
            DateTime nextLondonStart = DateTime.MaxValue;
            DateTime nextNewYorkStart = DateTime.MaxValue;

            if (asiaConfigured)
            {
                nextAsiaStart = time.Date + asiaStart;
                if (nextAsiaStart <= time)
                    nextAsiaStart = nextAsiaStart.AddDays(1);
            }

            if (londonConfigured)
            {
                nextLondonStart = time.Date + londonStart;
                if (nextLondonStart <= time)
                    nextLondonStart = nextLondonStart.AddDays(1);
            }

            if (newYorkConfigured)
            {
                nextNewYorkStart = time.Date + nyStart;
                if (nextNewYorkStart <= time)
                    nextNewYorkStart = nextNewYorkStart.AddDays(1);
            }

            if (nextAsiaStart <= nextLondonStart && nextAsiaStart <= nextNewYorkStart)
                return SessionSlot.Asia;
            if (nextLondonStart <= nextNewYorkStart)
                return SessionSlot.London;
            return SessionSlot.NewYork;
        }

        private SessionSlot GetFirstConfiguredSession()
        {
            if (IsSessionConfigured(SessionSlot.Asia))
                return SessionSlot.Asia;
            if (IsSessionConfigured(SessionSlot.London))
                return SessionSlot.London;
            if (IsSessionConfigured(SessionSlot.NewYork))
                return SessionSlot.NewYork;
            return SessionSlot.None;
        }

        private bool IsSessionConfigured(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    return UseAsiaSession && AsiaSessionStart != AsiaSessionEnd;
                case SessionSlot.London:
                    return UseLondonSession && LondonSessionStart != LondonSessionEnd;
                case SessionSlot.NewYork:
                    return UseNewYorkSession && NewYorkSessionStart != NewYorkSessionEnd;
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
                    activeDiMinSpread = AsiaDiMinSpread;
                    activeAdxMinSlopePoints = AsiaAdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = AsiaAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = AsiaAdxAbsoluteExitLevel;
                    activeProfitPeakDrawdownExitPoints = AsiaProfitPeakDrawdownExitPoints;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = AsiaContracts;
                    activeSignalBodyThresholdPercent = AsiaSignalBodyThresholdPercent;
                    activeEmaMinSlopePointsPerBar = AsiaEmaMinSlopePointsPerBar;
                    activeEntryStopMode = AsiaEntryStopMode;
                    activeStopPaddingPoints = AsiaStopPaddingPoints;
                    activeExitCrossPoints = AsiaExitCrossPoints;
                    activeTakeProfitPoints = AsiaTakeProfitPoints;
                    activeFlipBodyThresholdPercent = AsiaFlipBodyThresholdPercent;
                    activeFlipStopSetting = AsiaFlipStopSetting;
                    activeMinEntryBodySize = AsiaMinEntryBodySize;
                    activeRequireEmaTouch = AsiaRequireEmaTouch;
                    activeContractDoublerStopThresholdPoints = AsiaContractDoublerStopThresholdPoints;
                    break;

                case SessionSlot.London:
                    activeEma = emaLondon;
                    activeAdx = adxLondon;
                    activeEmaPeriod = LondonEmaPeriod;
                    activeAdxPeriod = LondonAdxPeriod;
                    activeAdxThreshold = LondonAdxThreshold;
                    activeAdxMaxThreshold = LondonAdxMaxThreshold;
                    activeDiMinSpread = LondonDiMinSpread;
                    activeAdxMinSlopePoints = LondonAdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = LondonAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = LondonAdxAbsoluteExitLevel;
                    activeProfitPeakDrawdownExitPoints = LondonProfitPeakDrawdownExitPoints;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = LondonContracts;
                    activeSignalBodyThresholdPercent = LondonSignalBodyThresholdPercent;
                    activeEmaMinSlopePointsPerBar = LondonEmaMinSlopePointsPerBar;
                    activeEntryStopMode = LondonEntryStopMode;
                    activeStopPaddingPoints = LondonStopPaddingPoints;
                    activeExitCrossPoints = LondonExitCrossPoints;
                    activeTakeProfitPoints = LondonTakeProfitPoints;
                    activeFlipBodyThresholdPercent = LondonFlipBodyThresholdPercent;
                    activeFlipStopSetting = LondonFlipStopSetting;
                    activeMinEntryBodySize = LondonMinEntryBodySize;
                    activeRequireEmaTouch = LondonRequireEmaTouch;
                    activeContractDoublerStopThresholdPoints = LondonContractDoublerStopThresholdPoints;
                    break;

                case SessionSlot.NewYork:
                    activeEma = emaNewYork;
                    activeAdx = adxNewYork;
                    activeEmaPeriod = NewYorkEmaPeriod;
                    activeAdxPeriod = NewYorkAdxPeriod;
                    activeAdxThreshold = NewYorkAdxThreshold;
                    activeAdxMaxThreshold = NewYorkAdxMaxThreshold;
                    activeDiMinSpread = NewYorkDiMinSpread;
                    activeAdxMinSlopePoints = NewYorkAdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = NewYorkAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = NewYorkAdxAbsoluteExitLevel;
                    activeProfitPeakDrawdownExitPoints = NewYorkProfitPeakDrawdownExitPoints;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = NewYorkContracts;
                    activeSignalBodyThresholdPercent = NewYorkSignalBodyThresholdPercent;
                    activeEmaMinSlopePointsPerBar = NewYorkEmaMinSlopePointsPerBar;
                    activeEntryStopMode = NewYorkEntryStopMode;
                    activeStopPaddingPoints = NewYorkStopPaddingPoints;
                    activeExitCrossPoints = NewYorkExitCrossPoints;
                    activeTakeProfitPoints = NewYorkTakeProfitPoints;
                    activeFlipBodyThresholdPercent = NewYorkFlipBodyThresholdPercent;
                    activeFlipStopSetting = NewYorkFlipStopSetting;
                    activeMinEntryBodySize = NewYorkMinEntryBodySize;
                    activeRequireEmaTouch = NewYorkRequireEmaTouch;
                    activeContractDoublerStopThresholdPoints = NewYorkContractDoublerStopThresholdPoints;
                    break;

                default:
                    activeEma = null;
                    activeAdx = null;
                    activeEmaPeriod = 0;
                    activeAdxPeriod = 0;
                    activeAdxThreshold = 0.0;
                    activeAdxMaxThreshold = 0.0;
                    activeDiMinSpread = 0.0;
                    activeAdxMinSlopePoints = 0.0;
                    activeAdxPeakDrawdownExitUnits = 0.0;
                    activeAdxAbsoluteExitLevel = 0.0;
                    activeProfitPeakDrawdownExitPoints = 0.0;
                    activeContracts = 0;
                    activeSignalBodyThresholdPercent = 100.0;
                    activeEmaMinSlopePointsPerBar = 0.0;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeStopPaddingPoints = 0.0;
                    activeExitCrossPoints = 0.0;
                    activeTakeProfitPoints = 0.0;
                    activeFlipBodyThresholdPercent = 100.0;
                    activeFlipStopSetting = FlipStopMode.WickExtreme;
                    activeMinEntryBodySize = 0.0;
                    activeRequireEmaTouch = false;
                    activeContractDoublerStopThresholdPoints = 0.0;
                    break;
            }
        }

        private void UpdateEmaPlotVisibility()
        {
            if (!ShowEmaOnChart)
            {
                SetEmaVisible(emaAsia, false);
                SetEmaVisible(emaLondon, false);
                SetEmaVisible(emaNewYork, false);
                return;
            }

            bool showAsia = activeSession == SessionSlot.Asia;
            bool showLondon = activeSession == SessionSlot.London;
            bool showNewYork = activeSession == SessionSlot.NewYork;

            // NinjaTrader may return shared indicator instances when periods match.
            // If two session EMA fields point to the same instance, visibility must be
            // resolved across all aliases before applying the brush.
            bool visAsia = showAsia
                || (ReferenceEquals(emaAsia, emaLondon) && showLondon)
                || (ReferenceEquals(emaAsia, emaNewYork) && showNewYork);
            bool visLondon = showLondon
                || (ReferenceEquals(emaLondon, emaAsia) && showAsia)
                || (ReferenceEquals(emaLondon, emaNewYork) && showNewYork);
            bool visNewYork = showNewYork
                || (ReferenceEquals(emaNewYork, emaAsia) && showAsia)
                || (ReferenceEquals(emaNewYork, emaLondon) && showLondon);

            SetEmaVisible(emaAsia, visAsia);
            SetEmaVisible(emaLondon, visLondon);
            SetEmaVisible(emaNewYork, visNewYork);
        }

        private void UpdateAdxPlotVisibility()
        {
            bool asiaForceDi = UseAsiaSession && AsiaDiMinSpread > 0.0;
            bool londonForceDi = UseLondonSession && LondonDiMinSpread > 0.0;
            bool newYorkForceDi = UseNewYorkSession && NewYorkDiMinSpread > 0.0;

            bool forceAsia = asiaForceDi
                || (ReferenceEquals(adxAsia, adxLondon) && londonForceDi)
                || (ReferenceEquals(adxAsia, adxNewYork) && newYorkForceDi);
            bool forceLondon = londonForceDi
                || (ReferenceEquals(adxLondon, adxAsia) && asiaForceDi)
                || (ReferenceEquals(adxLondon, adxNewYork) && newYorkForceDi);
            bool forceNewYork = newYorkForceDi
                || (ReferenceEquals(adxNewYork, adxAsia) && asiaForceDi)
                || (ReferenceEquals(adxNewYork, adxLondon) && londonForceDi);

            SetAdxVisible(adxAsia, ShowAdxOnChart, forceAsia);
            SetAdxVisible(adxLondon, ShowAdxOnChart, forceLondon);
            SetAdxVisible(adxNewYork, ShowAdxOnChart, forceNewYork);
        }

        private void SetEmaVisible(EMA ema, bool visible)
        {
            if (ema == null || ema.Plots == null || ema.Plots.Length == 0)
                return;

            ema.Plots[0].Brush = visible ? Brushes.Gold : Brushes.Transparent;
        }

        private void SetAdxVisible(DM adx, bool showAdx, bool forceShowDi)
        {
            if (adx == null || adx.Plots == null || adx.Plots.Length == 0)
                return;

            Brush adxBrush = showAdx ? Brushes.DodgerBlue : Brushes.Transparent;
            Brush diPlusBrush = (showAdx || forceShowDi) ? Brushes.LimeGreen : Brushes.Transparent;
            Brush diMinusBrush = (showAdx || forceShowDi) ? Brushes.OrangeRed : Brushes.Transparent;

            adx.Plots[0].Brush = adxBrush;
            if (adx.Plots.Length > 1)
                adx.Plots[1].Brush = diPlusBrush;
            if (adx.Plots.Length > 2)
                adx.Plots[2].Brush = diMinusBrush;
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
                "DuoEMA_ADX_Min_" + adxTagSuffix,
                drawMin ? minThreshold : 0.0,
                drawMin ? Brushes.LimeGreen : Brushes.Transparent,
                DashStyleHelper.Solid,
                2);

            Draw.HorizontalLine(
                adx,
                "DuoEMA_ADX_Max_" + adxTagSuffix,
                drawMax ? maxThreshold : 0.0,
                drawMax ? Brushes.OrangeRed : Brushes.Transparent,
                DashStyleHelper.Dash,
                2);
        }

        private int GetMaxConfiguredEmaPeriod()
        {
            return Math.Max(AsiaEmaPeriod, Math.Max(LondonEmaPeriod, NewYorkEmaPeriod));
        }

        private void ProcessSessionTransitions(SessionSlot slot)
        {
            bool inNow = TimeInSession(slot, Time[0]);
            bool inPrev = CurrentBar > 0 ? TimeInSession(slot, Time[1]) : inNow;

            if (inNow && !inPrev)
            {
                SetSessionClosed(slot, false);
                LogDebug(string.Format("{0} session start.", FormatSessionLabel(slot)));
                if (activeSession == slot)
                    LogSessionActivation("start");
            }

            if (inPrev && !inNow && !GetSessionClosed(slot))
            {
                if (CloseAtSessionEnd)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("SessionEnd");
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort("SessionEnd");
                }

                CancelWorkingEntryOrders();
                SetSessionClosed(slot, true);
                string sessionEndAction = CloseAtSessionEnd ? "flatten/cancel" : "cancel-only";
                LogDebug(string.Format("{0} session end: {1}. closeAtSessionEnd={2}", FormatSessionLabel(slot), sessionEndAction, CloseAtSessionEnd));
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

        private void ReconcileTrackedEntryOrders()
        {
            ReconcileTrackedEntryOrder(ref longEntryOrder, "LongEntry", ref missingLongEntryOrderBars);
            ReconcileTrackedEntryOrder(ref shortEntryOrder, "ShortEntry", ref missingShortEntryOrderBars);
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

        private bool IsBullishBar(int barsAgo)
        {
            return Close[barsAgo] > Open[barsAgo];
        }

        private bool IsBearishBar(int barsAgo)
        {
            return Close[barsAgo] < Open[barsAgo];
        }

        private bool ShouldExitOnOppositeCandles(MarketPosition position)
        {
            if (OppositeCandleExitCount <= 0)
                return false;

            int required = OppositeCandleExitCount;
            if (CurrentBar < required - 1)
                return false;

            for (int i = 0; i < required; i++)
            {
                bool opposite = position == MarketPosition.Long
                    ? IsBearishBar(i)
                    : IsBullishBar(i);

                if (!opposite)
                    return false;
            }

            return true;
        }

        private double BuildLongEntryStopPrice(double entryPrice, double candleOpen, double candleClose, double candleLow)
        {
            double raw = activeEntryStopMode == InitialStopMode.CandleOpen ? candleOpen : candleLow;
            raw -= activeStopPaddingPoints;

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);
            return rounded;
        }

        private double BuildShortEntryStopPrice(double entryPrice, double candleOpen, double candleClose, double candleHigh)
        {
            double raw = activeEntryStopMode == InitialStopMode.CandleOpen ? candleOpen : candleHigh;
            raw += activeStopPaddingPoints;

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }

        private double BuildFlipShortStopPrice(double entryPrice, double candleOpen, double candleHigh)
        {
            double raw = activeFlipStopSetting == FlipStopMode.CandleOpen ? candleOpen : candleHigh;
            raw += activeStopPaddingPoints;
            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }

        private double BuildFlipLongStopPrice(double entryPrice, double candleOpen, double candleLow)
        {
            double raw = activeFlipStopSetting == FlipStopMode.CandleOpen ? candleOpen : candleLow;
            raw -= activeStopPaddingPoints;
            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);
            return rounded;
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

        private double GetAdxSlopePoints()
        {
            if (activeAdx == null || CurrentBar < 1)
                return 0.0;

            return activeAdx[0] - activeAdx[1];
        }

        private bool TryGetAdxDirectionalValues(out double diPlus, out double diMinus)
        {
            diPlus = double.NaN;
            diMinus = double.NaN;

            if (activeAdx == null)
                return false;

            if (TryGetIndicatorSeriesValue(activeAdx, new[] { "DiPlus", "DIPlus", "PlusDI", "DmiPlus", "DMIPlus" }, out diPlus) &&
                TryGetIndicatorSeriesValue(activeAdx, new[] { "DiMinus", "DIMinus", "MinusDI", "DmiMinus", "DMIMinus" }, out diMinus))
                return true;

            try
            {
                if (activeAdx.Values != null && activeAdx.Values.Length >= 3)
                {
                    diPlus = activeAdx.Values[1][0];
                    diMinus = activeAdx.Values[2][0];
                    if (!double.IsNaN(diPlus) && !double.IsNaN(diMinus))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetIndicatorSeriesValue(object indicator, string[] propertyNames, out double value)
        {
            value = double.NaN;
            if (indicator == null || propertyNames == null)
                return false;

            for (int i = 0; i < propertyNames.Length; i++)
            {
                string propertyName = propertyNames[i];
                if (string.IsNullOrWhiteSpace(propertyName))
                    continue;

                try
                {
                    PropertyInfo prop = indicator.GetType().GetProperty(
                        propertyName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                    if (prop == null)
                        continue;

                    object raw = prop.GetValue(indicator, null);
                    if (raw == null)
                        continue;

                    if (raw is ISeries<double>)
                    {
                        value = ((ISeries<double>)raw)[0];
                        return !double.IsNaN(value);
                    }

                    if (raw is double)
                    {
                        value = (double)raw;
                        return !double.IsNaN(value);
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private string FormatDiForLog()
        {
            double diPlus;
            double diMinus;
            if (!TryGetAdxDirectionalValues(out diPlus, out diMinus))
                return "+DI=n/a -DI=n/a spread=n/a";

            return string.Format(CultureInfo.InvariantCulture,
                "+DI={0:0.00} -DI={1:0.00} spread={2:0.00}",
                diPlus,
                diMinus,
                Math.Abs(diPlus - diMinus));
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

        private double GetUnrealizedProfitPoints(double marketPrice)
        {
            if (Position.MarketPosition == MarketPosition.Flat)
                return 0.0;

            if (Position.MarketPosition == MarketPosition.Long)
                return marketPrice - Position.AveragePrice;

            if (Position.MarketPosition == MarketPosition.Short)
                return Position.AveragePrice - marketPrice;

            return 0.0;
        }

        private void UpdateProfitPeakTracker(double marketPrice)
        {
            MarketPosition currentPos = Position.MarketPosition;
            if (currentPos == MarketPosition.Flat)
            {
                trackedProfitPeakPosition = MarketPosition.Flat;
                currentTradePeakProfitPoints = 0.0;
                return;
            }

            double currentProfitPoints = GetUnrealizedProfitPoints(marketPrice);
            if (trackedProfitPeakPosition != currentPos)
            {
                trackedProfitPeakPosition = currentPos;
                currentTradePeakProfitPoints = currentProfitPoints;
                return;
            }

            if (currentProfitPoints > currentTradePeakProfitPoints)
                currentTradePeakProfitPoints = currentProfitPoints;
        }

        private bool ShouldExitOnProfitPeakDrawdown(out double drawdownPoints, out double currentProfitPoints)
        {
            drawdownPoints = 0.0;
            currentProfitPoints = 0.0;
            if (activeProfitPeakDrawdownExitPoints <= 0.0)
                return false;

            if (Position.MarketPosition == MarketPosition.Flat)
                return false;

            if (trackedProfitPeakPosition != Position.MarketPosition)
                return false;

            currentProfitPoints = GetUnrealizedProfitPoints(Close[0]);
            drawdownPoints = currentTradePeakProfitPoints - currentProfitPoints;
            return drawdownPoints >= activeProfitPeakDrawdownExitPoints;
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

        private bool TimeInNoTradesAfter(SessionSlot slot, DateTime time)
        {
            TimeSpan start;
            TimeSpan end;
            TimeSpan noTradesAfter;
            if (!TryGetSessionWindow(slot, time, out start, out end, out noTradesAfter))
                return false;

            TimeSpan now = time.TimeOfDay;

            if (start < end)
                return now >= noTradesAfter && now < end;

            if (noTradesAfter >= start)
                return now >= noTradesAfter || now < end;

            return now >= noTradesAfter && now < end;
        }

        private bool TryGetSessionWindow(SessionSlot slot, out TimeSpan start, out TimeSpan end)
        {
            return TryGetSessionWindow(slot, Time[0], out start, out end);
        }

        private bool TryGetSessionWindow(SessionSlot slot, out TimeSpan start, out TimeSpan end, out TimeSpan noTradesAfter)
        {
            return TryGetSessionWindow(slot, Time[0], out start, out end, out noTradesAfter);
        }

        private bool TryGetSessionWindow(SessionSlot slot, DateTime referenceTime, out TimeSpan start, out TimeSpan end)
        {
            TimeSpan noTradesAfter;
            return TryGetSessionWindow(slot, referenceTime, out start, out end, out noTradesAfter);
        }

        private bool TryGetSessionWindow(SessionSlot slot, DateTime referenceTime, out TimeSpan start, out TimeSpan end, out TimeSpan noTradesAfter)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;
            noTradesAfter = TimeSpan.Zero;

            if (!TryGetSessionBaseWindow(slot, out start, out end, out noTradesAfter))
                return false;

            if (ShouldAutoShiftSession(slot))
            {
                TimeSpan shift = GetLondonSessionShiftForDate(referenceTime.Date);
                start = ShiftTime(start, shift);
                end = ShiftTime(end, shift);
                noTradesAfter = ShiftTime(noTradesAfter, shift);
            }

            return true;
        }

        private bool TryGetSessionBaseWindow(SessionSlot slot, out TimeSpan start, out TimeSpan end, out TimeSpan noTradesAfter)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;
            noTradesAfter = TimeSpan.Zero;

            switch (slot)
            {
                case SessionSlot.Asia:
                    if (!UseAsiaSession || AsiaSessionStart == AsiaSessionEnd)
                        return false;
                    start = AsiaSessionStart;
                    end = AsiaSessionEnd;
                    noTradesAfter = AsiaNoTradesAfter;
                    return true;

                case SessionSlot.London:
                    if (!UseLondonSession || LondonSessionStart == LondonSessionEnd)
                        return false;
                    start = LondonSessionStart;
                    end = LondonSessionEnd;
                    noTradesAfter = LondonNoTradesAfter;
                    return true;

                case SessionSlot.NewYork:
                    if (!UseNewYorkSession || NewYorkSessionStart == NewYorkSessionEnd)
                        return false;
                    start = NewYorkSessionStart;
                    end = NewYorkSessionEnd;
                    noTradesAfter = NewYorkNoTradesAfter;
                    return true;

                default:
                    return false;
            }
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

        private DateTime GetNoTradesAfterTime(SessionSlot slot, DateTime sessionStartTime)
        {
            TimeSpan start;
            TimeSpan end;
            TimeSpan noTradesAfter;
            if (!TryGetSessionWindow(slot, sessionStartTime, out start, out end, out noTradesAfter))
                return Core.Globals.MinDate;

            DateTime noTradesTime = sessionStartTime.Date + noTradesAfter;
            if (start > end && noTradesAfter < start)
                noTradesTime = noTradesTime.AddDays(1);
            return noTradesTime;
        }

        private void DrawSessionBackgrounds()
        {
            if (CurrentBar < 1)
                return;

            DrawSessionBackground(SessionSlot.Asia, "DuoEMA_Asia", SessionBrush ?? Brushes.LightSkyBlue);
            DrawSessionBackground(SessionSlot.London, "DuoEMA_London", SessionBrush ?? Brushes.LightSkyBlue);
            DrawSessionBackground(SessionSlot.NewYork, "DuoEMA_NewYork", SessionBrush ?? Brushes.LightSkyBlue);
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

            if (ShowNoTradesAfterLine)
            {
                DateTime noTradesAfterTime = GetNoTradesAfterTime(slot, sessionStart);
                var lineBrush = new SolidColorBrush(Color.FromArgb(70, 255, 0, 0));
                try
                {
                    if (lineBrush.CanFreeze)
                        lineBrush.Freeze();
                }
                catch { }

                Draw.VerticalLine(this,
                    string.Format("{0}_NoTradesAfter_{1:yyyyMMdd_HHmm}", tagPrefix, sessionStart),
                    noTradesAfterTime,
                    lineBrush,
                    DashStyleHelper.Solid,
                    2);
            }
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

                string tagBase = string.Format("DuoEMA_News_{0:yyyyMMdd_HHmm}", newsTime);

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

            if (string.IsNullOrWhiteSpace(NewsDatesRaw))
            {
                newsDatesInitialized = true;
                return;
            }

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
                if (t == new TimeSpan(8, 30, 0) || t == new TimeSpan(14, 0, 0))
                    NewsDates.Add(parsed);
            }

            newsDatesInitialized = true;
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
                case SessionSlot.London:
                    return londonSessionClosed;
                case SessionSlot.NewYork:
                    return newYorkSessionClosed;
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
                case SessionSlot.London:
                    londonSessionClosed = value;
                    break;
                case SessionSlot.NewYork:
                    newYorkSessionClosed = value;
                    break;
            }
        }

        private string FormatSessionLabel(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    return "Asia";
                case SessionSlot.London:
                    return "London";
                case SessionSlot.NewYork:
                    return "New York";
                default:
                    return "None";
            }
        }

        private void LogSessionActivation(string reason)
        {
            TimeSpan start;
            TimeSpan end;
            TimeSpan noTradesAfter;
            if (!TryGetSessionWindow(activeSession, Time[0], out start, out end, out noTradesAfter))
                return;

            bool inNow = TimeInSession(activeSession, Time[0]);
            bool noTradesNow = TimeInNoTradesAfter(activeSession, Time[0]);
            bool autoShift = ShouldAutoShiftSession(activeSession);

            if (autoShift)
            {
                TimeSpan baseStart;
                TimeSpan baseEnd;
                TimeSpan baseNoTradesAfter;
                if (TryGetSessionBaseWindow(activeSession, out baseStart, out baseEnd, out baseNoTradesAfter))
                {
                    TimeSpan shift = GetLondonSessionShiftForDate(Time[0].Date);
                    DateTime utcSample = new DateTime(Time[0].Year, Time[0].Month, Time[0].Day, 12, 0, 0, DateTimeKind.Utc);
                    DateTime utcRef = new DateTime(Time[0].Year, 1, 15, 12, 0, 0, DateTimeKind.Utc);
                    TimeZoneInfo londonTz = GetLondonTimeZone();
                    TimeZoneInfo sessionTz = GetTargetTimeZone();
                    TimeSpan londonOffsetRef = londonTz.GetUtcOffset(utcRef);
                    TimeSpan londonOffsetSample = londonTz.GetUtcOffset(utcSample);
                    TimeSpan targetOffsetRef = sessionTz.GetUtcOffset(utcRef);
                    TimeSpan targetOffsetSample = sessionTz.GetUtcOffset(utcSample);

                    LogDebug(string.Format(
                        "SessionShift ({0}) | session={1} date={2:yyyy-MM-dd} tz={3} londonRef={4:hh\\:mm} londonNow={5:hh\\:mm} targetRef={6:hh\\:mm} targetNow={7:hh\\:mm} shift={8}m base={9:hh\\:mm}-{10:hh\\:mm} nta={11:hh\\:mm} shifted={12:hh\\:mm}-{13:hh\\:mm} nta={14:hh\\:mm}",
                        reason,
                        FormatSessionLabel(activeSession),
                        Time[0].Date,
                        sessionTz.Id,
                        londonOffsetRef,
                        londonOffsetSample,
                        targetOffsetRef,
                        targetOffsetSample,
                        shift.TotalMinutes,
                        baseStart,
                        baseEnd,
                        baseNoTradesAfter,
                        start,
                        end,
                        noTradesAfter));
                }
            }

            LogDebug(string.Format(
                "SessionConfig ({0}) | session={1} inSessionNow={2} noTradesNow={3} closeAtSessionEnd={4} autoShift={5} start={6:hh\\:mm} end={7:hh\\:mm} noTradesAfter={8:hh\\:mm} ema={9} emaSlopeMin={10:0.000} adxMin={11:0.##} adxMax={12:0.##} diMinSpread={13:0.##} adxSlopeMin={14:0.##} adxPeakDd={15:0.##} adxAbsExit={16:0.##} profitPeakDdPts={17:0.##} tpPts={18:0.##} contracts={19} body%={20:0.##} touchEMA={21} minBody={22:0.##} exitCross={23:0.##} flipBody%={24:0.##} flipStop={25} entryStop={26} slPad={27:0.##} doublerPts={28:0.##}",
                reason,
                FormatSessionLabel(activeSession),
                inNow,
                noTradesNow,
                CloseAtSessionEnd,
                autoShift,
                start,
                end,
                noTradesAfter,
                activeEmaPeriod,
                activeEmaMinSlopePointsPerBar,
                activeAdxThreshold,
                activeAdxMaxThreshold,
                activeDiMinSpread,
                activeAdxMinSlopePoints,
                activeAdxPeakDrawdownExitUnits,
                activeAdxAbsoluteExitLevel,
                activeProfitPeakDrawdownExitPoints,
                activeTakeProfitPoints,
                activeContracts,
                activeSignalBodyThresholdPercent,
                activeRequireEmaTouch,
                activeMinEntryBodySize,
                activeExitCrossPoints,
                activeFlipBodyThresholdPercent,
                activeFlipStopSetting,
                activeEntryStopMode,
                activeStopPaddingPoints,
                activeContractDoublerStopThresholdPoints));

            int adxPlotCount = activeAdx != null && activeAdx.Plots != null ? activeAdx.Plots.Length : 0;
            int adxValueCount = activeAdx != null && activeAdx.Values != null ? activeAdx.Values.Length : 0;
            string adxType = activeAdx != null ? activeAdx.GetType().Name : "null";
            bool diForceOn = activeDiMinSpread > 0.0;
            LogDebug(string.Format(
                "AdxVisuals ({0}) | session={1} type={2} showAdx={3} showThresholds={4} diForce={5} diMinSpread={6:0.##} plots={7} values={8}",
                reason,
                FormatSessionLabel(activeSession),
                adxType,
                ShowAdxOnChart,
                ShowAdxThresholdLines,
                diForceOn,
                activeDiMinSpread,
                adxPlotCount,
                adxValueCount));
        }

        private bool ShouldAutoShiftSession(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.London:
                    return AutoShiftLondon;
                case SessionSlot.NewYork:
                    return AutoShiftNewYork;
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

        private void LogDebug(string message)
        {
            if (!DebugLogging)
                return;

            if (Bars == null || CurrentBar < 0)
            {
                Print(string.Format("DuoEMA | {0}", message));
                return;
            }

            Print(string.Format("{0} | DuoEMA | bar={1} | {2}", Time[0], CurrentBar, message));
        }

        public void UpdateInfo()
        {
            UpdateInfoText();
        }

        public void UpdateInfoText()
        {
            var lines = BuildInfoLines();
            var font = new SimpleFont("Consolas", 14);

            int maxLabel = lines.Max(l => l.label.Length);
            int maxValue = Math.Max(1, lines.Max(l => l.value.Length));

            string valuePlaceholder = new string('0', maxValue);
            var bgLines = lines
                .Select(l => l.label.PadRight(maxLabel + 1) + valuePlaceholder)
                .ToArray();

            string bgText = string.Join(Environment.NewLine, bgLines);

            Draw.TextFixed(
                owner: this,
                tag: "myStatusLabel_bg",
                text: bgText,
                textPosition: TextPosition.BottomLeft,
                textBrush: Brushes.Transparent,
                font: font,
                outlineBrush: null,
                areaBrush: Brushes.Black,
                areaOpacity: 85);

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

            string spacesBeforeValue = new string(' ', maxLabel + 1);
            for (int i = 0; i < lines.Count; i++)
            {
                string tag = string.Format("myStatusLabel_val_{0}", i);

                var overlayLines = new string[lines.Count];
                for (int j = 0; j < lines.Count; j++)
                    overlayLines[j] = j == i ? spacesBeforeValue + lines[i].value : string.Empty;

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

            string tpLine;
            string slLine;
            GetPnLLines(out tpLine, out slLine);

            lines.Add(("TP:        ", tpLine, Brushes.LimeGreen));
            lines.Add(("SL:        ", slLine, Brushes.IndianRed));
            lines.Add(("Contracts: ", string.Format(CultureInfo.InvariantCulture, "{0}", activeContracts), Brushes.LightGray));
            lines.Add((FormatSessionLabel(activeSession), string.Empty, Brushes.LightGray));
            lines.Add((string.Format("v{0}", GetAddOnVersion()), string.Empty, Brushes.LightGray));

            return lines;
        }

        private void GetPnLLines(out string tpLine, out string slLine)
        {
            bool hasPosition = Position.MarketPosition != MarketPosition.Flat;
            bool hasLongOrder = IsOrderActive(longEntryOrder);
            bool hasShortOrder = IsOrderActive(shortEntryOrder);
            bool hasTrackedTrade = tradeLinesActive || tradeLineEntryPrice > 0.0;

            if (!hasPosition && !hasLongOrder && !hasShortOrder && !hasTrackedTrade)
            {
                tpLine = "$0";
                slLine = "$0";
                return;
            }

            double entry = 0.0;
            double tp = 0.0;
            double sl = 0.0;

            if (tradeLineEntryPrice > 0.0)
            {
                entry = tradeLineEntryPrice;
                sl = tradeLineSlPrice;
                tp = tradeLineHasTp ? tradeLineTpPrice : 0.0;
            }
            else if (hasPosition)
            {
                entry = Position.AveragePrice;
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    sl = pendingLongStopForWebhook;
                    tp = activeTakeProfitPoints > 0.0 ? entry + activeTakeProfitPoints : 0.0;
                }
                else
                {
                    sl = pendingShortStopForWebhook;
                    tp = activeTakeProfitPoints > 0.0 ? entry - activeTakeProfitPoints : 0.0;
                }
            }

            if (entry <= 0.0 || sl <= 0.0)
            {
                tpLine = "$0";
                slLine = "$0";
                return;
            }

            double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
            if (Instrument.MasterInstrument.Name == "MNQ")
                tickValue = 0.50;
            else if (Instrument.MasterInstrument.Name == "NQ")
                tickValue = 5.00;

            double slTicks = Math.Abs(entry - sl) / TickSize;
            double slDollars = slTicks * tickValue * Math.Max(1, activeContracts);
            slLine = string.Format(CultureInfo.InvariantCulture, "${0:0}", slDollars);

            if (tp > 0.0)
            {
                double tpTicks = Math.Abs(tp - entry) / TickSize;
                double tpDollars = tpTicks * tickValue * Math.Max(1, activeContracts);
                tpLine = string.Format(CultureInfo.InvariantCulture, "${0:0}", tpDollars);
            }
            else
            {
                tpLine = "$0";
            }
        }

        private string GetAddOnVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Version version = assembly.GetName().Version;
            return version != null ? version.ToString() : "0.0.0.0";
        }

        private int GetMaxConfiguredAdxPeriod()
        {
            return Math.Max(AsiaAdxPeriod, Math.Max(LondonAdxPeriod, NewYorkAdxPeriod));
        }

        private bool CandleTouchesEma(double candleHigh, double candleLow, double emaValue)
        {
            return candleLow <= emaValue && candleHigh >= emaValue;
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

        private int GetEntryQuantity(double entryPrice, double stopPrice)
        {
            int baseQty = Math.Max(1, activeContracts);
            if (activeContractDoublerStopThresholdPoints <= 0.0)
                return baseQty;

            double stopDistancePoints = Math.Abs(entryPrice - stopPrice);
            if (stopDistancePoints > 0.0 && stopDistancePoints < activeContractDoublerStopThresholdPoints)
                return baseQty * 2;

            return baseQty;
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
                    ExitLong("MaxAccountBalance", "LongEntry");
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("MaxAccountBalance", "ShortEntry");

                LogDebug(string.Format("Account balance target reached | cash={0:0.00} target={1:0.00} trading paused.", balance, MaxAccountBalance));
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
                cashValue = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                return cashValue > 0.0;
            }
            catch
            {
                return false;
            }
        }

        private void SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime)
                return;

            if (WebhookProviderType == WebhookProvider.ProjectX)
            {
                int orderQtyForProvider = quantityOverride > 0 ? quantityOverride : Math.Max(1, activeContracts);
                SendProjectX(eventType, entryPrice, takeProfit, stopLoss, isMarketEntry, orderQtyForProvider);
                return;
            }

            if (string.IsNullOrWhiteSpace(WebhookUrl))
                return;

            try
            {
                int orderQty = quantityOverride > 0 ? quantityOverride : Math.Max(1, activeContracts);
                string ticker = Instrument != null ? Instrument.MasterInstrument.Name : "UNKNOWN";
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
                    return;

                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.UploadString(WebhookUrl, "POST", json);
                }
            }
            catch (Exception ex)
            {
                LogDebug(string.Format("Webhook error: {0}", ex.Message));
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
                switch (eventType.ToLowerInvariant())
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
            catch (Exception ex)
            {
                LogDebug(string.Format("ProjectX error: {0}", ex.Message));
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

        [NinjaScriptProperty]
        [Display(Name = "Use Asia Session", Description = "Enable trading logic during the Asia time window.", GroupName = "Asia", Order = 0)]
        public bool UseAsiaSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "Asia session start time in chart time zone.", GroupName = "Asia", Order = 1)]
        public TimeSpan AsiaSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "Asia session end time in chart time zone.", GroupName = "Asia", Order = 2)]
        public TimeSpan AsiaSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", Description = "No new entries after this time until the session ends.", GroupName = "Asia", Order = 3)]
        public TimeSpan AsiaNoTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Asia entry and exit logic.", GroupName = "Asia", Order = 4)]
        public int AsiaEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for Asia entries.", GroupName = "Asia", Order = 5)]
        public int AsiaContracts { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Signal Body % Over/Under EMA", Description = "Minimum candle body percent above EMA for longs or below EMA for shorts.", GroupName = "Asia", Order = 6)]
        public double AsiaSignalBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Candle Touch EMA", Description = "If enabled, the full candle range (wick/body) must touch or cross the EMA for an entry signal.", GroupName = "Asia", Order = 7)]
        public bool AsiaRequireEmaTouch { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Asia trend filter.", GroupName = "Asia", Order = 8)]
        public int AsiaAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Asia entries are allowed only when ADX is greater than or equal to this value.", GroupName = "Asia", Order = 9)]
        public double AsiaAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Asia entries are allowed only when ADX is less than or equal to this value.", GroupName = "Asia", Order = 10)]
        public double AsiaAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "DI Min Spread", Description = "0 disables DI filter. If > 0, longs require +DI > -DI with spread >= this value, shorts require -DI > +DI with spread >= this value.", GroupName = "Asia", Order = 90)]
        public double AsiaDiMinSpread { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Min Slope (Points)", Description = "Minimum positive ADX slope per bar required for entries. 0 disables slope filter.", GroupName = "Asia", Order = 11)]
        public double AsiaAdxMinSlopePoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "Asia", Order = 12)]
        public double AsiaAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "Asia", Order = 90)]
        public double AsiaAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Profit Peak Drawdown Exit (Pts)", Description = "0 disables. Track best unrealized profit (points) per trade and exit after this many points of pullback from that peak.", GroupName = "Asia", Order = 13)]
        public double AsiaProfitPeakDrawdownExitPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope magnitude per bar required for entries. 0 disables slope filter.", GroupName = "Asia", Order = 14)]
        public double AsiaEmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Stop Mode", Description = "How the initial stop is positioned for a new entry.", GroupName = "Asia", Order = 15)]
        public InitialStopMode AsiaEntryStopMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Additional stop padding in points beyond the selected stop anchor (wick/open).", GroupName = "Asia", Order = 16)]
        public double AsiaStopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "Asia", Order = 17)]
        public double AsiaExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "Asia", Order = 18)]
        public double AsiaTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Flip Body % Threshold", Description = "Minimum opposite-side body percent needed to reverse position instead of exit only.", GroupName = "Asia", Order = 19)]
        public double AsiaFlipBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flip Stop Mode", Description = "Stop placement mode used for flip entries.", GroupName = "Asia", Order = 20)]
        public FlipStopMode AsiaFlipStopSetting { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min Entry Candle Body Size", Description = "Minimum absolute body size required for a new entry candle.", GroupName = "Asia", Order = 21)]
        public double AsiaMinEntryBodySize { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Contract Doubler SL Threshold", Description = "If initial stop distance is below this point value, entry size is doubled.", GroupName = "Asia", Order = 22)]
        public double AsiaContractDoublerStopThresholdPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use London Session", Description = "Enable trading logic during the London time window.", GroupName = "London", Order = 0)]
        public bool UseLondonSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "London session start time in chart time zone.", GroupName = "London", Order = 1)]
        public TimeSpan LondonSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "London session end time in chart time zone.", GroupName = "London", Order = 2)]
        public TimeSpan LondonSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", Description = "No new entries after this time until the session ends.", GroupName = "London", Order = 3)]
        public TimeSpan LondonNoTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Auto Shift", Description = "Apply London DST auto-shift for this session window.", GroupName = "London", Order = 4)]
        public bool AutoShiftLondon { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by London entry and exit logic.", GroupName = "London", Order = 5)]
        public int LondonEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for London entries.", GroupName = "London", Order = 5)]
        public int LondonContracts { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Signal Body % Over/Under EMA", Description = "Minimum candle body percent above EMA for longs or below EMA for shorts.", GroupName = "London", Order = 6)]
        public double LondonSignalBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Candle Touch EMA", Description = "If enabled, the full candle range (wick/body) must touch or cross the EMA for an entry signal.", GroupName = "London", Order = 7)]
        public bool LondonRequireEmaTouch { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the London trend filter.", GroupName = "London", Order = 8)]
        public int LondonAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. London entries are allowed only when ADX is greater than or equal to this value.", GroupName = "London", Order = 9)]
        public double LondonAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. London entries are allowed only when ADX is less than or equal to this value.", GroupName = "London", Order = 10)]
        public double LondonAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "DI Min Spread", Description = "0 disables DI filter. If > 0, longs require +DI > -DI with spread >= this value, shorts require -DI > +DI with spread >= this value.", GroupName = "London", Order = 90)]
        public double LondonDiMinSpread { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Min Slope (Points)", Description = "Minimum positive ADX slope per bar required for entries. 0 disables slope filter.", GroupName = "London", Order = 11)]
        public double LondonAdxMinSlopePoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "London", Order = 12)]
        public double LondonAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "London", Order = 90)]
        public double LondonAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Profit Peak Drawdown Exit (Pts)", Description = "0 disables. Track best unrealized profit (points) per trade and exit after this many points of pullback from that peak.", GroupName = "London", Order = 13)]
        public double LondonProfitPeakDrawdownExitPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope magnitude per bar required for entries. 0 disables slope filter.", GroupName = "London", Order = 14)]
        public double LondonEmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Stop Mode", Description = "How the initial stop is positioned for a new entry.", GroupName = "London", Order = 15)]
        public InitialStopMode LondonEntryStopMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Additional stop padding in points beyond the selected stop anchor (wick/open).", GroupName = "London", Order = 16)]
        public double LondonStopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "London", Order = 17)]
        public double LondonExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "London", Order = 18)]
        public double LondonTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Flip Body % Threshold", Description = "Minimum opposite-side body percent needed to reverse position instead of exit only.", GroupName = "London", Order = 19)]
        public double LondonFlipBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flip Stop Mode", Description = "Stop placement mode used for flip entries.", GroupName = "London", Order = 20)]
        public FlipStopMode LondonFlipStopSetting { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min Entry Candle Body Size", Description = "Minimum absolute body size required for a new entry candle.", GroupName = "London", Order = 21)]
        public double LondonMinEntryBodySize { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Contract Doubler SL Threshold", Description = "If initial stop distance is below this point value, entry size is doubled.", GroupName = "London", Order = 22)]
        public double LondonContractDoublerStopThresholdPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use New York Session", Description = "Enable trading logic during the New York time window.", GroupName = "New York", Order = 0)]
        public bool UseNewYorkSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "New York session start time in chart time zone.", GroupName = "New York", Order = 1)]
        public TimeSpan NewYorkSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "New York session end time in chart time zone.", GroupName = "New York", Order = 2)]
        public TimeSpan NewYorkSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", Description = "No new entries after this time until the session ends.", GroupName = "New York", Order = 3)]
        public TimeSpan NewYorkNoTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Auto Shift", Description = "Apply London DST auto-shift for this session window.", GroupName = "New York", Order = 4)]
        public bool AutoShiftNewYork { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by New York entry and exit logic.", GroupName = "New York", Order = 5)]
        public int NewYorkEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for New York entries.", GroupName = "New York", Order = 5)]
        public int NewYorkContracts { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Signal Body % Over/Under EMA", Description = "Minimum candle body percent above EMA for longs or below EMA for shorts.", GroupName = "New York", Order = 6)]
        public double NewYorkSignalBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require Candle Touch EMA", Description = "If enabled, the full candle range (wick/body) must touch or cross the EMA for an entry signal.", GroupName = "New York", Order = 7)]
        public bool NewYorkRequireEmaTouch { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the New York trend filter.", GroupName = "New York", Order = 8)]
        public int NewYorkAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. New York entries are allowed only when ADX is greater than or equal to this value.", GroupName = "New York", Order = 9)]
        public double NewYorkAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. New York entries are allowed only when ADX is less than or equal to this value.", GroupName = "New York", Order = 10)]
        public double NewYorkAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "DI Min Spread", Description = "0 disables DI filter. If > 0, longs require +DI > -DI with spread >= this value, shorts require -DI > +DI with spread >= this value.", GroupName = "New York", Order = 90)]
        public double NewYorkDiMinSpread { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Min Slope (Points)", Description = "Minimum positive ADX slope per bar required for entries. 0 disables slope filter.", GroupName = "New York", Order = 11)]
        public double NewYorkAdxMinSlopePoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "New York", Order = 12)]
        public double NewYorkAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "New York", Order = 90)]
        public double NewYorkAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Profit Peak Drawdown Exit (Pts)", Description = "0 disables. Track best unrealized profit (points) per trade and exit after this many points of pullback from that peak.", GroupName = "New York", Order = 13)]
        public double NewYorkProfitPeakDrawdownExitPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope magnitude per bar required for entries. 0 disables slope filter.", GroupName = "New York", Order = 14)]
        public double NewYorkEmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Stop Mode", Description = "How the initial stop is positioned for a new entry.", GroupName = "New York", Order = 15)]
        public InitialStopMode NewYorkEntryStopMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Additional stop padding in points beyond the selected stop anchor (wick/open).", GroupName = "New York", Order = 16)]
        public double NewYorkStopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "New York", Order = 17)]
        public double NewYorkExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "New York", Order = 18)]
        public double NewYorkTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Flip Body % Threshold", Description = "Minimum opposite-side body percent needed to reverse position instead of exit only.", GroupName = "New York", Order = 19)]
        public double NewYorkFlipBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flip Stop Mode", Description = "Stop placement mode used for flip entries.", GroupName = "New York", Order = 20)]
        public FlipStopMode NewYorkFlipStopSetting { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min Entry Candle Body Size", Description = "Minimum absolute body size required for a new entry candle.", GroupName = "New York", Order = 21)]
        public double NewYorkMinEntryBodySize { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Contract Doubler SL Threshold", Description = "If initial stop distance is below this point value, entry size is doubled.", GroupName = "New York", Order = 22)]
        public double NewYorkContractDoublerStopThresholdPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close At Session End", Description = "If true, flatten positions and cancel entries at each configured session end.", GroupName = "10. Sessions", Order = 0)]
        public bool CloseAtSessionEnd { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Session Fill", Description = "Background color used to highlight configured session windows.", GroupName = "10. Sessions", Order = 1)]
        public Brush SessionBrush { get; set; }

        [Browsable(false)]
        public string SessionBrushSerializable
        {
            get { return Serialize.BrushToString(SessionBrush); }
            set { SessionBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show No-Trades Line", Description = "Draw a red vertical line at each session no-trades-after time.", GroupName = "10. Sessions", Order = 2)]
        public bool ShowNoTradesAfterLine { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show EMA On Chart", Description = "Show/hide EMA indicators on chart.", GroupName = "10. Sessions", Order = 3)]
        public bool ShowEmaOnChart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show ADX On Chart", Description = "Show/hide ADX indicators on chart.", GroupName = "10. Sessions", Order = 4)]
        public bool ShowAdxOnChart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show ADX Threshold Lines", Description = "Show/hide ADX min/max threshold reference lines on chart.", GroupName = "10. Sessions", Order = 5)]
        public bool ShowAdxThresholdLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Description = "Block entries inside the configured minutes before and after listed news events.", GroupName = "11. News", Order = 0)]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Block Minutes", Description = "Minutes blocked before and after each news timestamp.", GroupName = "11. News", Order = 1)]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL", Description = "HTTP endpoint for TradersPost order webhooks. Leave empty to disable TradersPost webhooks.", GroupName = "12. Webhooks", Order = 0)]
        public string WebhookUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Provider", Description = "Select webhook target: TradersPost or ProjectX.", GroupName = "12. Webhooks", Order = 1)]
        public WebhookProvider WebhookProviderType { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX API Base URL", Description = "ProjectX gateway base URL.", GroupName = "12. Webhooks", Order = 2)]
        public string ProjectXApiBaseUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Username", Description = "ProjectX login username.", GroupName = "12. Webhooks", Order = 3)]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX API Key", Description = "ProjectX login key.", GroupName = "12. Webhooks", Order = 4)]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Account ID", Description = "ProjectX account id used for order routing.", GroupName = "12. Webhooks", Order = 5)]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Contract ID", Description = "ProjectX contract id (for example CON.F.US.DA6.M25).", GroupName = "12. Webhooks", Order = 6)]
        public string ProjectXContractId { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Account Balance", Description = "When cash value reaches or exceeds this value, entries are blocked and position is flattened. 0 disables.", GroupName = "13. Risk", Order = 0)]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Entry Distance From EMA (Points)", Description = "0 disables. Block long/short entries and flips when close price is farther than this many points from the active EMA.", GroupName = "13. Risk", Order = 1)]
        public double MaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Confirmation", Description = "Show a Yes/No confirmation popup before each new long/short entry (including flips).", GroupName = "13. Risk", Order = 2)]
        public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit Mode", Description = "Choose EMA exits only, opposite-candle exits only, or both.", GroupName = "13. Risk", Order = 3)]
        public ExitMode PositionExitMode { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Opposite Candle Exit Count", Description = "0 disables. If set to N, close an open position after N consecutive opposite candles.", GroupName = "13. Risk", Order = 4)]
        public int OppositeCandleExitCount { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Debug Logging", Description = "Print concise decision, order, and execution diagnostics to Output.", GroupName = "14. Debug", Order = 0)]
        public bool DebugLogging { get; set; }
    }
}
