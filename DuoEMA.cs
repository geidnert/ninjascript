#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class DuoEMA : Strategy
    {
        public enum InitialStopMode
        {
            BodyPercent,
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

        private Order longEntryOrder;
        private Order shortEntryOrder;
        private int lastLoggedBar = -1;

        private bool asiaSessionClosed;
        private bool londonSessionClosed;
        private bool newYorkSessionClosed;

        private bool sessionInitialized;
        private SessionSlot activeSession = SessionSlot.None;

        private EMA emaAsia;
        private EMA emaLondon;
        private EMA emaNewYork;
        private EMA activeEma;

        private int activeEmaPeriod;
        private int activeContracts;
        private bool activeUseMarketEntry;
        private double activeSignalBodyThresholdPercent;
        private InitialStopMode activeEntryStopMode;
        private double activeEntryStopBodyPercent;
        private double activeExitCrossPoints;
        private double activeFlipBodyThresholdPercent;
        private FlipStopMode activeFlipStopSetting;
        private bool activeEnableFlipLogic;
        private bool activeFlattenOnTwoReverseCandles;
        private int activeMaxSequentialLosses;
        private int activeMaxSequentialWins;
        private double activeMinEntryBodySize;

        private int asiaSequentialLosses;
        private int londonSequentialLosses;
        private int newYorkSequentialLosses;

        private int asiaSequentialWins;
        private int londonSequentialWins;
        private int newYorkSequentialWins;

        private bool wasInPosition;
        private SessionSlot lastTradeSession = SessionSlot.None;
        private int processedTradeCount;

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

                UseAsiaSession = true;
                AsiaSessionStart = new TimeSpan(19, 0, 0);
                AsiaSessionEnd = new TimeSpan(1, 30, 0);
                AsiaNoTradesAfter = new TimeSpan(1, 0, 0);
                AsiaEmaPeriod = 21;
                AsiaContracts = 1;
                AsiaUseMarketEntry = false;
                AsiaSignalBodyThresholdPercent = 50.0;
                AsiaEntryStopMode = InitialStopMode.BodyPercent;
                AsiaEntryStopBodyPercent = 50.0;
                AsiaExitCrossPoints = 0.0;
                AsiaFlipBodyThresholdPercent = 50.0;
                AsiaFlipStopSetting = FlipStopMode.WickExtreme;
                AsiaEnableFlipLogic = true;
                AsiaFlattenOnTwoReverseCandles = false;
                AsiaMaxSequentialLosses = 0;
                AsiaMaxSequentialWins = 0;
                AsiaMinEntryBodySize = 0.0;

                UseLondonSession = true;
                LondonSessionStart = new TimeSpan(1, 30, 0);
                LondonSessionEnd = new TimeSpan(5, 30, 0);
                LondonNoTradesAfter = new TimeSpan(5, 0, 0);
                LondonEmaPeriod = 21;
                LondonContracts = 1;
                LondonUseMarketEntry = false;
                LondonSignalBodyThresholdPercent = 50.0;
                LondonEntryStopMode = InitialStopMode.BodyPercent;
                LondonEntryStopBodyPercent = 50.0;
                LondonExitCrossPoints = 0.0;
                LondonFlipBodyThresholdPercent = 50.0;
                LondonFlipStopSetting = FlipStopMode.WickExtreme;
                LondonEnableFlipLogic = true;
                LondonFlattenOnTwoReverseCandles = false;
                LondonMaxSequentialLosses = 0;
                LondonMaxSequentialWins = 0;
                LondonMinEntryBodySize = 0.0;

                UseNewYorkSession = true;
                NewYorkSessionStart = new TimeSpan(9, 40, 0);
                NewYorkSessionEnd = new TimeSpan(15, 0, 0);
                NewYorkNoTradesAfter = new TimeSpan(14, 30, 0);
                NewYorkEmaPeriod = 21;
                NewYorkContracts = 1;
                NewYorkUseMarketEntry = false;
                NewYorkSignalBodyThresholdPercent = 50.0;
                NewYorkEntryStopMode = InitialStopMode.BodyPercent;
                NewYorkEntryStopBodyPercent = 50.0;
                NewYorkExitCrossPoints = 0.0;
                NewYorkFlipBodyThresholdPercent = 50.0;
                NewYorkFlipStopSetting = FlipStopMode.WickExtreme;
                NewYorkEnableFlipLogic = true;
                NewYorkFlattenOnTwoReverseCandles = false;
                NewYorkMaxSequentialLosses = 0;
                NewYorkMaxSequentialWins = 0;
                NewYorkMinEntryBodySize = 0.0;

                CloseAtSessionEnd = true;
                SessionBrush = Brushes.Gold;

                UseNewsSkip = true;
                NewsBlockMinutes = 2;

                DebugLogging = false;
            }
            else if (State == State.DataLoaded)
            {
                emaAsia = EMA(AsiaEmaPeriod);
                emaLondon = EMA(LondonEmaPeriod);
                emaNewYork = EMA(NewYorkEmaPeriod);

                AddChartIndicator(emaAsia);
                if (LondonEmaPeriod != AsiaEmaPeriod)
                    AddChartIndicator(emaLondon);
                if (NewYorkEmaPeriod != AsiaEmaPeriod && NewYorkEmaPeriod != LondonEmaPeriod)
                    AddChartIndicator(emaNewYork);

                asiaSessionClosed = false;
                londonSessionClosed = false;
                newYorkSessionClosed = false;

                sessionInitialized = false;
                activeSession = GetFirstConfiguredSession();
                ApplyInputsForSession(activeSession);
                wasInPosition = false;
                lastTradeSession = SessionSlot.None;
                processedTradeCount = SystemPerformance.AllTrades.Count;

                EnsureNewsDatesInitialized();

                LogDebug(
                    string.Format(
                        "DataLoaded | ActiveSession={0} EMA={1} Contracts={2} MarketEntry={3} Body%={4:0.##} ExitCross={5:0.##} EntryStop={6} EntryBodySL%={7:0.##} FlipStop={8} FlipBody%={9:0.##}",
                        FormatSessionLabel(activeSession),
                        activeEmaPeriod,
                        activeContracts,
                        activeUseMarketEntry,
                        activeSignalBodyThresholdPercent,
                        activeExitCrossPoints,
                        activeEntryStopMode,
                        activeEntryStopBodyPercent,
                        activeFlipStopSetting,
                        activeFlipBodyThresholdPercent));
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(1, GetMaxConfiguredEmaPeriod()))
                return;

            DrawSessionBackgrounds();
            DrawNewsWindows(Time[0]);

            ProcessSessionTransitions(SessionSlot.Asia);
            ProcessSessionTransitions(SessionSlot.London);
            ProcessSessionTransitions(SessionSlot.NewYork);

            UpdateActiveSession(Time[0]);
            UpdateSequentialCountersOnTradeClose();

            bool inNewsSkipNow = TimeInNewsSkip(Time[0]);
            bool inNewsSkipPrev = CurrentBar > 0 && TimeInNewsSkip(Time[1]);
            if (!inNewsSkipPrev && inNewsSkipNow)
            {
                CancelWorkingEntryOrders();
                LogDebug("Entered news block: canceling working entries.");
            }

            bool inActiveSessionNow = activeSession != SessionSlot.None;
            bool inNoTradesNow = inActiveSessionNow && TimeInNoTradesAfter(activeSession, Time[0]);
            bool sessionTradeBlocked = inActiveSessionNow && SessionSequentialLimitReached(activeSession);
            bool canTradeNow = inActiveSessionNow && !inNoTradesNow && !inNewsSkipNow && !sessionTradeBlocked;

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

            if (sessionTradeBlocked)
                CancelWorkingEntryOrders();

            if (activeEma == null || CurrentBar < activeEmaPeriod)
                return;

            double emaValue = activeEma[0];
            double emaLimitPrice = Instrument.MasterInstrument.RoundToTickSize(emaValue);

            double bodySize = Math.Abs(Close[0] - Open[0]);
            bool bullish = Close[0] > Open[0];
            bool bearish = Close[0] < Open[0];
            double bodyAbovePercent = GetBodyPercentAboveEma(Open[0], Close[0], emaValue);
            double bodyBelowPercent = GetBodyPercentBelowEma(Open[0], Close[0], emaValue);

            bool bodySizePasses = bodySize >= activeMinEntryBodySize;
            bool longSignal = bullish && bodySizePasses && bodyAbovePercent >= activeSignalBodyThresholdPercent;
            bool shortSignal = bearish && bodySizePasses && bodyBelowPercent >= activeSignalBodyThresholdPercent;

            if (DebugLogging && CurrentBar != lastLoggedBar)
            {
                lastLoggedBar = CurrentBar;
                LogDebug(
                    string.Format(
                        "BarSummary | session={0} pos={1} o={2:0.00} h={3:0.00} l={4:0.00} c={5:0.00} ema={6:0.00} body={7:0.00} above%={8:0.0} below%={9:0.0} longSig={10} shortSig={11} canTrade={12} noTrades={13} news={14} seqBlocked={15} minBody={16:0.00} lossSeq={17} winSeq={18}",
                        FormatSessionLabel(activeSession),
                        Position.MarketPosition,
                        Open[0],
                        High[0],
                        Low[0],
                        Close[0],
                        emaValue,
                        bodySize,
                        bodyAbovePercent,
                        bodyBelowPercent,
                        longSignal,
                        shortSignal,
                        canTradeNow,
                        inNoTradesNow,
                        inNewsSkipNow,
                        sessionTradeBlocked,
                        activeMinEntryBodySize,
                        GetSequentialLosses(activeSession),
                        GetSequentialWins(activeSession)));
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (activeFlattenOnTwoReverseCandles && CurrentBar > 0 && IsBearishBar(0) && IsBearishBar(1))
                {
                    ExitLong("TwoCandleReverse", "LongEntry");
                    LogDebug("Flatten LONG | two consecutive bearish candles detected.");
                    return;
                }

                if (Close[0] <= emaValue - activeExitCrossPoints)
                {
                    bool shouldFlip = activeEnableFlipLogic && canTradeNow && bodyBelowPercent >= activeFlipBodyThresholdPercent;
                    if (shouldFlip)
                    {
                        CancelOrderIfActive(longEntryOrder, "FlipToShort");
                        CancelOrderIfActive(shortEntryOrder, "FlipToShort");

                        double stopPrice = BuildFlipShortStopPrice(Close[0], Open[0], High[0]);
                        SetStopLoss("ShortEntry", CalculationMode.Price, stopPrice, false);
                        EnterShort(activeContracts, "ShortEntry");

                        LogDebug(string.Format("Flip LONG->SHORT | close={0:0.00} ema={1:0.00} below%={2:0.0} stop={3:0.00}", Close[0], emaValue, bodyBelowPercent, stopPrice));
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
                if (activeFlattenOnTwoReverseCandles && CurrentBar > 0 && IsBullishBar(0) && IsBullishBar(1))
                {
                    ExitShort("TwoCandleReverse", "ShortEntry");
                    LogDebug("Flatten SHORT | two consecutive bullish candles detected.");
                    return;
                }

                if (Close[0] >= emaValue + activeExitCrossPoints)
                {
                    bool shouldFlip = activeEnableFlipLogic && canTradeNow && bodyAbovePercent >= activeFlipBodyThresholdPercent;
                    if (shouldFlip)
                    {
                        CancelOrderIfActive(longEntryOrder, "FlipToLong");
                        CancelOrderIfActive(shortEntryOrder, "FlipToLong");

                        double stopPrice = BuildFlipLongStopPrice(Close[0], Open[0], Low[0]);
                        SetStopLoss("LongEntry", CalculationMode.Price, stopPrice, false);
                        EnterLong(activeContracts, "LongEntry");

                        LogDebug(string.Format("Flip SHORT->LONG | close={0:0.00} ema={1:0.00} above%={2:0.0} stop={3:0.00}", Close[0], emaValue, bodyAbovePercent, stopPrice));
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
                return;

            bool longOrderActive = IsOrderActive(longEntryOrder);
            bool shortOrderActive = IsOrderActive(shortEntryOrder);

            if (longSignal)
            {
                CancelOrderIfActive(shortEntryOrder, "OppositeLongSignal");

                if (!longOrderActive)
                {
                    double entryPrice = activeUseMarketEntry ? Close[0] : emaLimitPrice;
                    double stopPrice = BuildLongEntryStopPrice(entryPrice, Open[0], Close[0], Low[0]);

                    SetStopLoss("LongEntry", CalculationMode.Price, stopPrice, false);
                    if (activeUseMarketEntry)
                        EnterLong(activeContracts, "LongEntry");
                    else
                        EnterLongLimit(0, true, activeContracts, emaLimitPrice, "LongEntry");

                    LogDebug(
                        activeUseMarketEntry
                            ? string.Format("Place LONG market | session={0} stop={1:0.00}", FormatSessionLabel(activeSession), stopPrice)
                            : string.Format("Place LONG limit @ EMA={0:0.00} | session={1} stop={2:0.00}", emaLimitPrice, FormatSessionLabel(activeSession), stopPrice));
                }
            }
            else if (shortSignal)
            {
                CancelOrderIfActive(longEntryOrder, "OppositeShortSignal");

                if (!shortOrderActive)
                {
                    double entryPrice = activeUseMarketEntry ? Close[0] : emaLimitPrice;
                    double stopPrice = BuildShortEntryStopPrice(entryPrice, Open[0], Close[0], High[0]);

                    SetStopLoss("ShortEntry", CalculationMode.Price, stopPrice, false);
                    if (activeUseMarketEntry)
                        EnterShort(activeContracts, "ShortEntry");
                    else
                        EnterShortLimit(0, true, activeContracts, emaLimitPrice, "ShortEntry");

                    LogDebug(
                        activeUseMarketEntry
                            ? string.Format("Place SHORT market | session={0} stop={1:0.00}", FormatSessionLabel(activeSession), stopPrice)
                            : string.Format("Place SHORT limit @ EMA={0:0.00} | session={1} stop={2:0.00}", emaLimitPrice, FormatSessionLabel(activeSession), stopPrice));
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

            if (DebugLogging)
            {
                LogDebug(
                    string.Format(
                        "OrderUpdate | name={0} state={1} qty={2} filled={3} avg={4:0.00} limit={5:0.00} stop={6:0.00} error={7} comment={8}",
                        order.Name,
                        orderState,
                        quantity,
                        filled,
                        averageFillPrice,
                        limitPrice,
                        stopPrice,
                        error,
                        comment));
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution?.Order == null)
                return;

            if (!DebugLogging)
                return;

            LogDebug(
                string.Format(
                    "Execution | order={0} state={1} qty={2} price={3:0.00} posAfter={4} execId={5}",
                    execution.Order.Name,
                    execution.Order.OrderState,
                    quantity,
                    price,
                    marketPosition,
                    executionId));
        }

        private void UpdateActiveSession(DateTime time)
        {
            SessionSlot desired = DetermineSessionForTime(time);
            if (!sessionInitialized || desired != activeSession)
            {
                activeSession = desired;
                if (activeSession != SessionSlot.None)
                    ApplyInputsForSession(activeSession);

                sessionInitialized = true;
                LogDebug(string.Format("Active session switched to {0}", FormatSessionLabel(activeSession)));
            }
        }

        private SessionSlot DetermineSessionForTime(DateTime time)
        {
            TimeSpan now = time.TimeOfDay;

            if (IsSessionConfigured(SessionSlot.Asia)
                && TryGetSessionWindow(SessionSlot.Asia, out TimeSpan asiaStart, out TimeSpan asiaEnd)
                && IsTimeInRange(now, asiaStart, asiaEnd))
                return SessionSlot.Asia;

            if (IsSessionConfigured(SessionSlot.London)
                && TryGetSessionWindow(SessionSlot.London, out TimeSpan londonStart, out TimeSpan londonEnd)
                && IsTimeInRange(now, londonStart, londonEnd))
                return SessionSlot.London;

            if (IsSessionConfigured(SessionSlot.NewYork)
                && TryGetSessionWindow(SessionSlot.NewYork, out TimeSpan nyStart, out TimeSpan nyEnd)
                && IsTimeInRange(now, nyStart, nyEnd))
                return SessionSlot.NewYork;

            return SessionSlot.None;
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
                    activeEmaPeriod = AsiaEmaPeriod;
                    activeContracts = AsiaContracts;
                    activeUseMarketEntry = AsiaUseMarketEntry;
                    activeSignalBodyThresholdPercent = AsiaSignalBodyThresholdPercent;
                    activeEntryStopMode = AsiaEntryStopMode;
                    activeEntryStopBodyPercent = AsiaEntryStopBodyPercent;
                    activeExitCrossPoints = AsiaExitCrossPoints;
                    activeFlipBodyThresholdPercent = AsiaFlipBodyThresholdPercent;
                    activeFlipStopSetting = AsiaFlipStopSetting;
                    activeEnableFlipLogic = AsiaEnableFlipLogic;
                    activeFlattenOnTwoReverseCandles = AsiaFlattenOnTwoReverseCandles;
                    activeMaxSequentialLosses = AsiaMaxSequentialLosses;
                    activeMaxSequentialWins = AsiaMaxSequentialWins;
                    activeMinEntryBodySize = AsiaMinEntryBodySize;
                    break;

                case SessionSlot.London:
                    activeEma = emaLondon;
                    activeEmaPeriod = LondonEmaPeriod;
                    activeContracts = LondonContracts;
                    activeUseMarketEntry = LondonUseMarketEntry;
                    activeSignalBodyThresholdPercent = LondonSignalBodyThresholdPercent;
                    activeEntryStopMode = LondonEntryStopMode;
                    activeEntryStopBodyPercent = LondonEntryStopBodyPercent;
                    activeExitCrossPoints = LondonExitCrossPoints;
                    activeFlipBodyThresholdPercent = LondonFlipBodyThresholdPercent;
                    activeFlipStopSetting = LondonFlipStopSetting;
                    activeEnableFlipLogic = LondonEnableFlipLogic;
                    activeFlattenOnTwoReverseCandles = LondonFlattenOnTwoReverseCandles;
                    activeMaxSequentialLosses = LondonMaxSequentialLosses;
                    activeMaxSequentialWins = LondonMaxSequentialWins;
                    activeMinEntryBodySize = LondonMinEntryBodySize;
                    break;

                case SessionSlot.NewYork:
                    activeEma = emaNewYork;
                    activeEmaPeriod = NewYorkEmaPeriod;
                    activeContracts = NewYorkContracts;
                    activeUseMarketEntry = NewYorkUseMarketEntry;
                    activeSignalBodyThresholdPercent = NewYorkSignalBodyThresholdPercent;
                    activeEntryStopMode = NewYorkEntryStopMode;
                    activeEntryStopBodyPercent = NewYorkEntryStopBodyPercent;
                    activeExitCrossPoints = NewYorkExitCrossPoints;
                    activeFlipBodyThresholdPercent = NewYorkFlipBodyThresholdPercent;
                    activeFlipStopSetting = NewYorkFlipStopSetting;
                    activeEnableFlipLogic = NewYorkEnableFlipLogic;
                    activeFlattenOnTwoReverseCandles = NewYorkFlattenOnTwoReverseCandles;
                    activeMaxSequentialLosses = NewYorkMaxSequentialLosses;
                    activeMaxSequentialWins = NewYorkMaxSequentialWins;
                    activeMinEntryBodySize = NewYorkMinEntryBodySize;
                    break;

                default:
                    activeEma = null;
                    activeEmaPeriod = 0;
                    activeContracts = 0;
                    activeUseMarketEntry = false;
                    activeSignalBodyThresholdPercent = 100.0;
                    activeEntryStopMode = InitialStopMode.BodyPercent;
                    activeEntryStopBodyPercent = 50.0;
                    activeExitCrossPoints = 0.0;
                    activeFlipBodyThresholdPercent = 100.0;
                    activeFlipStopSetting = FlipStopMode.WickExtreme;
                    activeEnableFlipLogic = false;
                    activeFlattenOnTwoReverseCandles = false;
                    activeMaxSequentialLosses = 0;
                    activeMaxSequentialWins = 0;
                    activeMinEntryBodySize = 0.0;
                    break;
            }
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
                ResetSequentialCounters(slot);
                LogDebug(string.Format("{0} session start.", FormatSessionLabel(slot)));
            }

            if (inPrev && !inNow && !GetSessionClosed(slot))
            {
                if (CloseAtSessionEnd)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong("SessionEnd", "LongEntry");
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort("SessionEnd", "ShortEntry");
                }

                CancelWorkingEntryOrders();
                SetSessionClosed(slot, true);
                LogDebug(string.Format("{0} session end: flatten/cancel.", FormatSessionLabel(slot)));
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

        private bool IsBullishBar(int barsAgo)
        {
            return Close[barsAgo] > Open[barsAgo];
        }

        private bool IsBearishBar(int barsAgo)
        {
            return Close[barsAgo] < Open[barsAgo];
        }

        private bool SessionSequentialLimitReached(SessionSlot slot)
        {
            if (slot == SessionSlot.None)
                return true;

            int losses = GetSequentialLosses(slot);
            int wins = GetSequentialWins(slot);
            int maxLosses = GetMaxSequentialLossesLimit(slot);
            int maxWins = GetMaxSequentialWinsLimit(slot);

            if (maxLosses > 0 && losses >= maxLosses)
                return true;

            if (maxWins > 0 && wins >= maxWins)
                return true;

            return false;
        }

        private void UpdateSequentialCountersOnTradeClose()
        {
            bool inPositionNow = Position.MarketPosition != MarketPosition.Flat;
            if (inPositionNow && !wasInPosition)
                lastTradeSession = activeSession != SessionSlot.None ? activeSession : DetermineSessionForTime(Time[0]);

            int allTradesCount = SystemPerformance.AllTrades.Count;
            if (allTradesCount > processedTradeCount)
            {
                for (int i = processedTradeCount; i < allTradesCount; i++)
                {
                    Trade closedTrade = SystemPerformance.AllTrades[i];
                    SessionSlot tradeSession = lastTradeSession != SessionSlot.None
                        ? lastTradeSession
                        : (activeSession != SessionSlot.None ? activeSession : DetermineSessionForTime(Time[0]));

                    double tradeProfit = closedTrade.ProfitCurrency;
                    if (tradeProfit < 0)
                    {
                        IncrementSequentialLoss(tradeSession);
                        LogDebug(string.Format("Closed trade LOSS | session={0} pnl={1:0.00} seqLoss={2} seqWin={3}",
                            FormatSessionLabel(tradeSession), tradeProfit, GetSequentialLosses(tradeSession), GetSequentialWins(tradeSession)));
                    }
                    else if (tradeProfit > 0)
                    {
                        IncrementSequentialWin(tradeSession);
                        LogDebug(string.Format("Closed trade WIN | session={0} pnl={1:0.00} seqLoss={2} seqWin={3}",
                            FormatSessionLabel(tradeSession), tradeProfit, GetSequentialLosses(tradeSession), GetSequentialWins(tradeSession)));
                    }
                    else
                    {
                        LogDebug(string.Format("Closed trade BREAKEVEN | session={0} pnl={1:0.00} seqLoss={2} seqWin={3}",
                            FormatSessionLabel(tradeSession), tradeProfit, GetSequentialLosses(tradeSession), GetSequentialWins(tradeSession)));
                    }
                }

                processedTradeCount = allTradesCount;
            }

            if (!inPositionNow)
                lastTradeSession = SessionSlot.None;

            wasInPosition = inPositionNow;
        }

        private int GetSequentialLosses(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    return asiaSequentialLosses;
                case SessionSlot.London:
                    return londonSequentialLosses;
                case SessionSlot.NewYork:
                    return newYorkSequentialLosses;
                default:
                    return 0;
            }
        }

        private int GetSequentialWins(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    return asiaSequentialWins;
                case SessionSlot.London:
                    return londonSequentialWins;
                case SessionSlot.NewYork:
                    return newYorkSequentialWins;
                default:
                    return 0;
            }
        }

        private void IncrementSequentialLoss(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    asiaSequentialLosses++;
                    asiaSequentialWins = 0;
                    break;
                case SessionSlot.London:
                    londonSequentialLosses++;
                    londonSequentialWins = 0;
                    break;
                case SessionSlot.NewYork:
                    newYorkSequentialLosses++;
                    newYorkSequentialWins = 0;
                    break;
            }
        }

        private void IncrementSequentialWin(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    asiaSequentialWins++;
                    asiaSequentialLosses = 0;
                    break;
                case SessionSlot.London:
                    londonSequentialWins++;
                    londonSequentialLosses = 0;
                    break;
                case SessionSlot.NewYork:
                    newYorkSequentialWins++;
                    newYorkSequentialLosses = 0;
                    break;
            }
        }

        private void ResetSequentialCounters(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    asiaSequentialLosses = 0;
                    asiaSequentialWins = 0;
                    break;
                case SessionSlot.London:
                    londonSequentialLosses = 0;
                    londonSequentialWins = 0;
                    break;
                case SessionSlot.NewYork:
                    newYorkSequentialLosses = 0;
                    newYorkSequentialWins = 0;
                    break;
            }
        }

        private int GetMaxSequentialLossesLimit(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    return AsiaMaxSequentialLosses;
                case SessionSlot.London:
                    return LondonMaxSequentialLosses;
                case SessionSlot.NewYork:
                    return NewYorkMaxSequentialLosses;
                default:
                    return 0;
            }
        }

        private int GetMaxSequentialWinsLimit(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    return AsiaMaxSequentialWins;
                case SessionSlot.London:
                    return LondonMaxSequentialWins;
                case SessionSlot.NewYork:
                    return NewYorkMaxSequentialWins;
                default:
                    return 0;
            }
        }

        private double BuildLongEntryStopPrice(double entryPrice, double candleOpen, double candleClose, double candleLow)
        {
            double body = Math.Abs(candleClose - candleOpen);
            double raw;

            switch (activeEntryStopMode)
            {
                case InitialStopMode.CandleOpen:
                    raw = candleOpen;
                    break;
                case InitialStopMode.WickExtreme:
                    raw = candleLow;
                    break;
                default:
                    raw = candleClose - (body * (activeEntryStopBodyPercent / 100.0));
                    break;
            }

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);
            return rounded;
        }

        private double BuildShortEntryStopPrice(double entryPrice, double candleOpen, double candleClose, double candleHigh)
        {
            double body = Math.Abs(candleClose - candleOpen);
            double raw;

            switch (activeEntryStopMode)
            {
                case InitialStopMode.CandleOpen:
                    raw = candleOpen;
                    break;
                case InitialStopMode.WickExtreme:
                    raw = candleHigh;
                    break;
                default:
                    raw = candleClose + (body * (activeEntryStopBodyPercent / 100.0));
                    break;
            }

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }

        private double BuildFlipShortStopPrice(double entryPrice, double candleOpen, double candleHigh)
        {
            double raw = activeFlipStopSetting == FlipStopMode.CandleOpen ? candleOpen : candleHigh;
            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }

        private double BuildFlipLongStopPrice(double entryPrice, double candleOpen, double candleLow)
        {
            double raw = activeFlipStopSetting == FlipStopMode.CandleOpen ? candleOpen : candleLow;
            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);
            return rounded;
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
            if (!TryGetSessionWindow(slot, out start, out end))
                return false;

            return IsTimeInRange(time.TimeOfDay, start, end);
        }

        private bool TimeInNoTradesAfter(SessionSlot slot, DateTime time)
        {
            TimeSpan start;
            TimeSpan end;
            TimeSpan noTradesAfter;
            if (!TryGetSessionWindow(slot, out start, out end, out noTradesAfter))
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
            TimeSpan noTradesAfter;
            return TryGetSessionWindow(slot, out start, out end, out noTradesAfter);
        }

        private bool TryGetSessionWindow(SessionSlot slot, out TimeSpan start, out TimeSpan end, out TimeSpan noTradesAfter)
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
            }

            return false;
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
            if (!TryGetSessionWindow(slot, out start, out end))
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
            if (!TryGetSessionWindow(slot, out start, out end, out noTradesAfter))
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
            if (!TryGetSessionWindow(slot, out start, out end))
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
                var lineBrush = new SolidColorBrush(Color.FromArgb(90, 255, 0, 0));
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

        [NinjaScriptProperty]
        [Display(Name = "Use Asia Session", Description = "Enable or disable trading rules during the Asia session window.", GroupName = "04. Asia Time", Order = 0)]
        public bool UseAsiaSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "Asia session start time (chart time).", GroupName = "04. Asia Time", Order = 1)]
        public TimeSpan AsiaSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "Asia session end time (chart time).", GroupName = "04. Asia Time", Order = 2)]
        public TimeSpan AsiaSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", Description = "Blocks new entries after this time until the Asia session ends.", GroupName = "04. Asia Time", Order = 3)]
        public TimeSpan AsiaNoTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA lookback period used for Asia entry/exit logic.", GroupName = "05. Asia Parameters", Order = 0)]
        public int AsiaEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order size for Asia entries.", GroupName = "05. Asia Parameters", Order = 1)]
        public int AsiaContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Market Entry", Description = "If true, enters at market. If false, places entry limit orders at EMA.", GroupName = "05. Asia Parameters", Order = 2)]
        public bool AsiaUseMarketEntry { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Signal Body % Over/Under EMA", Description = "Minimum percent of candle body that must be above/below EMA to trigger a signal.", GroupName = "05. Asia Parameters", Order = 3)]
        public double AsiaSignalBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Stop Mode", Description = "How initial stop is calculated for non-flip entries.", GroupName = "05. Asia Parameters", Order = 4)]
        public InitialStopMode AsiaEntryStopMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Entry Stop Body %", Description = "Used only when Entry Stop Mode is BodyPercent.", GroupName = "05. Asia Parameters", Order = 5)]
        public double AsiaEntryStopBodyPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Extra points beyond EMA required before exit/flip evaluation. 0 = EMA touch/cross.", GroupName = "05. Asia Parameters", Order = 6)]
        public double AsiaExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Flip Body % Threshold", Description = "Minimum opposite-side body percent required to flip instead of just exit.", GroupName = "05. Asia Parameters", Order = 7)]
        public double AsiaFlipBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flip Stop Mode", Description = "Stop placement mode used when a flip entry is taken.", GroupName = "05. Asia Parameters", Order = 8)]
        public FlipStopMode AsiaFlipStopSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Flip Logic", Description = "If false, opposite EMA cross conditions exit only and never reverse position.", GroupName = "05. Asia Parameters", Order = 9)]
        public bool AsiaEnableFlipLogic { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten On 2 Reverse 5m Candles", Description = "If true, open position is flattened after 2 consecutive candles against the trade.", GroupName = "05. Asia Parameters", Order = 10)]
        public bool AsiaFlattenOnTwoReverseCandles { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Sequential Losses", Description = "Max consecutive losing trades allowed in this session before new entries are blocked. 0 = disabled.", GroupName = "05. Asia Parameters", Order = 11)]
        public int AsiaMaxSequentialLosses { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Sequential Wins", Description = "Max consecutive winning trades allowed in this session before new entries are blocked. 0 = disabled.", GroupName = "05. Asia Parameters", Order = 12)]
        public int AsiaMaxSequentialWins { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min Entry Candle Body Size", Description = "Minimum absolute candle body size required to allow a new entry.", GroupName = "05. Asia Parameters", Order = 13)]
        public double AsiaMinEntryBodySize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use London Session", Description = "Enable or disable trading rules during the London session window.", GroupName = "06. London Time", Order = 0)]
        public bool UseLondonSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "London session start time (chart time).", GroupName = "06. London Time", Order = 1)]
        public TimeSpan LondonSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "London session end time (chart time).", GroupName = "06. London Time", Order = 2)]
        public TimeSpan LondonSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", Description = "Blocks new entries after this time until the London session ends.", GroupName = "06. London Time", Order = 3)]
        public TimeSpan LondonNoTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA lookback period used for London entry/exit logic.", GroupName = "07. London Parameters", Order = 0)]
        public int LondonEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order size for London entries.", GroupName = "07. London Parameters", Order = 1)]
        public int LondonContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Market Entry", Description = "If true, enters at market. If false, places entry limit orders at EMA.", GroupName = "07. London Parameters", Order = 2)]
        public bool LondonUseMarketEntry { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Signal Body % Over/Under EMA", Description = "Minimum percent of candle body that must be above/below EMA to trigger a signal.", GroupName = "07. London Parameters", Order = 3)]
        public double LondonSignalBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Stop Mode", Description = "How initial stop is calculated for non-flip entries.", GroupName = "07. London Parameters", Order = 4)]
        public InitialStopMode LondonEntryStopMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Entry Stop Body %", Description = "Used only when Entry Stop Mode is BodyPercent.", GroupName = "07. London Parameters", Order = 5)]
        public double LondonEntryStopBodyPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Extra points beyond EMA required before exit/flip evaluation. 0 = EMA touch/cross.", GroupName = "07. London Parameters", Order = 6)]
        public double LondonExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Flip Body % Threshold", Description = "Minimum opposite-side body percent required to flip instead of just exit.", GroupName = "07. London Parameters", Order = 7)]
        public double LondonFlipBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flip Stop Mode", Description = "Stop placement mode used when a flip entry is taken.", GroupName = "07. London Parameters", Order = 8)]
        public FlipStopMode LondonFlipStopSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Flip Logic", Description = "If false, opposite EMA cross conditions exit only and never reverse position.", GroupName = "07. London Parameters", Order = 9)]
        public bool LondonEnableFlipLogic { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten On 2 Reverse 5m Candles", Description = "If true, open position is flattened after 2 consecutive candles against the trade.", GroupName = "07. London Parameters", Order = 10)]
        public bool LondonFlattenOnTwoReverseCandles { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Sequential Losses", Description = "Max consecutive losing trades allowed in this session before new entries are blocked. 0 = disabled.", GroupName = "07. London Parameters", Order = 11)]
        public int LondonMaxSequentialLosses { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Sequential Wins", Description = "Max consecutive winning trades allowed in this session before new entries are blocked. 0 = disabled.", GroupName = "07. London Parameters", Order = 12)]
        public int LondonMaxSequentialWins { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min Entry Candle Body Size", Description = "Minimum absolute candle body size required to allow a new entry.", GroupName = "07. London Parameters", Order = 13)]
        public double LondonMinEntryBodySize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use New York Session", Description = "Enable or disable trading rules during the New York session window.", GroupName = "08. New York Time", Order = 0)]
        public bool UseNewYorkSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "New York session start time (chart time).", GroupName = "08. New York Time", Order = 1)]
        public TimeSpan NewYorkSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "New York session end time (chart time).", GroupName = "08. New York Time", Order = 2)]
        public TimeSpan NewYorkSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", Description = "Blocks new entries after this time until the New York session ends.", GroupName = "08. New York Time", Order = 3)]
        public TimeSpan NewYorkNoTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA lookback period used for New York entry/exit logic.", GroupName = "09. New York Parameters", Order = 0)]
        public int NewYorkEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Order size for New York entries.", GroupName = "09. New York Parameters", Order = 1)]
        public int NewYorkContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Market Entry", Description = "If true, enters at market. If false, places entry limit orders at EMA.", GroupName = "09. New York Parameters", Order = 2)]
        public bool NewYorkUseMarketEntry { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Signal Body % Over/Under EMA", Description = "Minimum percent of candle body that must be above/below EMA to trigger a signal.", GroupName = "09. New York Parameters", Order = 3)]
        public double NewYorkSignalBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Stop Mode", Description = "How initial stop is calculated for non-flip entries.", GroupName = "09. New York Parameters", Order = 4)]
        public InitialStopMode NewYorkEntryStopMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Entry Stop Body %", Description = "Used only when Entry Stop Mode is BodyPercent.", GroupName = "09. New York Parameters", Order = 5)]
        public double NewYorkEntryStopBodyPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Extra points beyond EMA required before exit/flip evaluation. 0 = EMA touch/cross.", GroupName = "09. New York Parameters", Order = 6)]
        public double NewYorkExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Flip Body % Threshold", Description = "Minimum opposite-side body percent required to flip instead of just exit.", GroupName = "09. New York Parameters", Order = 7)]
        public double NewYorkFlipBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flip Stop Mode", Description = "Stop placement mode used when a flip entry is taken.", GroupName = "09. New York Parameters", Order = 8)]
        public FlipStopMode NewYorkFlipStopSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Flip Logic", Description = "If false, opposite EMA cross conditions exit only and never reverse position.", GroupName = "09. New York Parameters", Order = 9)]
        public bool NewYorkEnableFlipLogic { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flatten On 2 Reverse 5m Candles", Description = "If true, open position is flattened after 2 consecutive candles against the trade.", GroupName = "09. New York Parameters", Order = 10)]
        public bool NewYorkFlattenOnTwoReverseCandles { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Sequential Losses", Description = "Max consecutive losing trades allowed in this session before new entries are blocked. 0 = disabled.", GroupName = "09. New York Parameters", Order = 11)]
        public int NewYorkMaxSequentialLosses { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Sequential Wins", Description = "Max consecutive winning trades allowed in this session before new entries are blocked. 0 = disabled.", GroupName = "09. New York Parameters", Order = 12)]
        public int NewYorkMaxSequentialWins { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Min Entry Candle Body Size", Description = "Minimum absolute candle body size required to allow a new entry.", GroupName = "09. New York Parameters", Order = 13)]
        public double NewYorkMinEntryBodySize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close At Session End", Description = "If true, flatten positions and cancel entries at each session end.", GroupName = "10. Sessions", Order = 0)]
        public bool CloseAtSessionEnd { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Session Fill", Description = "Background fill color for active session windows.", GroupName = "10. Sessions", Order = 1)]
        public Brush SessionBrush { get; set; }

        [Browsable(false)]
        public string SessionBrushSerializable
        {
            get { return Serialize.BrushToString(SessionBrush); }
            set { SessionBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Description = "Block entries around scheduled 08:30 and 14:00 news events.", GroupName = "11. News", Order = 0)]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Block Minutes", Description = "Blocks this many minutes before and after each news timestamp.", GroupName = "11. News", Order = 1)]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Debug Logging", Description = "Print detailed decision and order logs to the Output window.", GroupName = "12. Debug", Order = 0)]
        public bool DebugLogging { get; set; }
    }
}
