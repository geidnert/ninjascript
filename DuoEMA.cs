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
            Asia,
            London,
            NewYork
        }

        private EMA ema;
        private Order longEntryOrder;
        private Order shortEntryOrder;
        private int lastLoggedBar = -1;

        private bool asiaSessionClosed;
        private bool londonSessionClosed;
        private bool newYorkSessionClosed;

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

                EmaPeriod = 21;
                Contracts = 1;
                UseMarketEntry = false;
                SignalBodyThresholdPercent = 50.0;
                ExitCrossPoints = 0.0;
                EntryStopMode = InitialStopMode.BodyPercent;
                EntryStopBodyPercent = 50.0;
                FlipStopSetting = FlipStopMode.WickExtreme;
                FlipBodyThresholdPercent = 50.0;

                UseAsiaSession = true;
                AsiaSessionStart = new TimeSpan(19, 0, 0);
                AsiaSessionEnd = new TimeSpan(1, 30, 0);
                AsiaNoTradesAfter = new TimeSpan(1, 0, 0);

                UseLondonSession = true;
                LondonSessionStart = new TimeSpan(1, 30, 0);
                LondonSessionEnd = new TimeSpan(5, 30, 0);
                LondonNoTradesAfter = new TimeSpan(5, 0, 0);

                UseNewYorkSession = true;
                NewYorkSessionStart = new TimeSpan(9, 40, 0);
                NewYorkSessionEnd = new TimeSpan(15, 0, 0);
                NewYorkNoTradesAfter = new TimeSpan(14, 30, 0);

                CloseAtSessionEnd = true;
                SessionBrush = Brushes.Gold;

                UseNewsSkip = true;
                NewsBlockMinutes = 2;

                DebugLogging = false;
            }
            else if (State == State.DataLoaded)
            {
                ema = EMA(EmaPeriod);
                AddChartIndicator(ema);

                asiaSessionClosed = false;
                londonSessionClosed = false;
                newYorkSessionClosed = false;

                EnsureNewsDatesInitialized();

                LogDebug(
                    string.Format(
                        "DataLoaded | EMA={0} Contracts={1} MarketEntry={2} SignalBody%={3:0.##} ExitCross={4:0.##} EntryStop={5} EntryBodySL%={6:0.##} FlipStop={7} FlipBody%={8:0.##}",
                        EmaPeriod,
                        Contracts,
                        UseMarketEntry,
                        SignalBodyThresholdPercent,
                        ExitCrossPoints,
                        EntryStopMode,
                        EntryStopBodyPercent,
                        FlipStopSetting,
                        FlipBodyThresholdPercent));
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(1, EmaPeriod))
                return;

            DrawSessionBackgrounds();
            DrawNewsWindows(Time[0]);

            ProcessSessionTransitions(SessionSlot.Asia);
            ProcessSessionTransitions(SessionSlot.London);
            ProcessSessionTransitions(SessionSlot.NewYork);

            bool inNewsSkipNow = TimeInNewsSkip(Time[0]);
            bool inNewsSkipPrev = CurrentBar > 0 && TimeInNewsSkip(Time[1]);
            if (!inNewsSkipPrev && inNewsSkipNow)
            {
                CancelWorkingEntryOrders();
                LogDebug("Entered news block: canceling working entries.");
            }

            bool inAnySessionNow = TimeInAnyEnabledSession(Time[0]);
            bool inNoTradesNow = TimeInNoTradesAfterAnyEnabledSession(Time[0]);
            bool canTradeNow = inAnySessionNow && !inNoTradesNow && !inNewsSkipNow;

            if (CurrentBar > 0)
            {
                bool crossedNoTrades = !TimeInNoTradesAfterAnyEnabledSession(Time[1]) && inNoTradesNow;
                if (crossedNoTrades)
                {
                    CancelWorkingEntryOrders();
                    LogDebug("NoTradesAfter crossed: canceling working entries.");
                }
            }

            if (!inAnySessionNow)
                CancelWorkingEntryOrders();

            if (inNoTradesNow)
                CancelWorkingEntryOrders();

            double emaValue = ema[0];
            double emaLimitPrice = Instrument.MasterInstrument.RoundToTickSize(emaValue);

            double bodySize = Math.Abs(Close[0] - Open[0]);
            bool bullish = Close[0] > Open[0];
            bool bearish = Close[0] < Open[0];
            double bodyAbovePercent = GetBodyPercentAboveEma(Open[0], Close[0], emaValue);
            double bodyBelowPercent = GetBodyPercentBelowEma(Open[0], Close[0], emaValue);

            bool longSignal = bullish && bodySize > 0 && bodyAbovePercent >= SignalBodyThresholdPercent;
            bool shortSignal = bearish && bodySize > 0 && bodyBelowPercent >= SignalBodyThresholdPercent;

            if (DebugLogging && CurrentBar != lastLoggedBar)
            {
                lastLoggedBar = CurrentBar;
                LogDebug(
                    string.Format(
                        "BarSummary | pos={0} o={1:0.00} h={2:0.00} l={3:0.00} c={4:0.00} ema={5:0.00} body={6:0.00} above%={7:0.0} below%={8:0.0} longSig={9} shortSig={10} canTrade={11} inSession={12} noTrades={13} news={14}",
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
                        inAnySessionNow,
                        inNoTradesNow,
                        inNewsSkipNow));
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (Close[0] <= emaValue - ExitCrossPoints)
                {
                    bool shouldFlip = canTradeNow && bodyBelowPercent >= FlipBodyThresholdPercent;
                    if (shouldFlip)
                    {
                        CancelOrderIfActive(longEntryOrder, "FlipToShort");
                        CancelOrderIfActive(shortEntryOrder, "FlipToShort");

                        double stopPrice = BuildFlipShortStopPrice(Close[0], Open[0], High[0]);
                        SetStopLoss("ShortEntry", CalculationMode.Price, stopPrice, false);
                        EnterShort(Contracts, "ShortEntry");

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
                if (Close[0] >= emaValue + ExitCrossPoints)
                {
                    bool shouldFlip = canTradeNow && bodyAbovePercent >= FlipBodyThresholdPercent;
                    if (shouldFlip)
                    {
                        CancelOrderIfActive(longEntryOrder, "FlipToLong");
                        CancelOrderIfActive(shortEntryOrder, "FlipToLong");

                        double stopPrice = BuildFlipLongStopPrice(Close[0], Open[0], Low[0]);
                        SetStopLoss("LongEntry", CalculationMode.Price, stopPrice, false);
                        EnterLong(Contracts, "LongEntry");

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
                    double entryPrice = UseMarketEntry ? Close[0] : emaLimitPrice;
                    double stopPrice = BuildLongEntryStopPrice(entryPrice, Open[0], Close[0], Low[0]);

                    SetStopLoss("LongEntry", CalculationMode.Price, stopPrice, false);
                    if (UseMarketEntry)
                        EnterLong(Contracts, "LongEntry");
                    else
                        EnterLongLimit(0, true, Contracts, emaLimitPrice, "LongEntry");

                    LogDebug(
                        UseMarketEntry
                            ? string.Format("Place LONG market | stop={0:0.00}", stopPrice)
                            : string.Format("Place LONG limit @ EMA={0:0.00} | stop={1:0.00}", emaLimitPrice, stopPrice));
                }
            }
            else if (shortSignal)
            {
                CancelOrderIfActive(longEntryOrder, "OppositeShortSignal");

                if (!shortOrderActive)
                {
                    double entryPrice = UseMarketEntry ? Close[0] : emaLimitPrice;
                    double stopPrice = BuildShortEntryStopPrice(entryPrice, Open[0], Close[0], High[0]);

                    SetStopLoss("ShortEntry", CalculationMode.Price, stopPrice, false);
                    if (UseMarketEntry)
                        EnterShort(Contracts, "ShortEntry");
                    else
                        EnterShortLimit(0, true, Contracts, emaLimitPrice, "ShortEntry");

                    LogDebug(
                        UseMarketEntry
                            ? string.Format("Place SHORT market | stop={0:0.00}", stopPrice)
                            : string.Format("Place SHORT limit @ EMA={0:0.00} | stop={1:0.00}", emaLimitPrice, stopPrice));
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
            if (!DebugLogging || execution?.Order == null)
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

        private void ProcessSessionTransitions(SessionSlot slot)
        {
            bool inNow = TimeInSession(slot, Time[0]);
            bool inPrev = CurrentBar > 0 ? TimeInSession(slot, Time[1]) : inNow;

            if (inNow && !inPrev)
            {
                SetSessionClosed(slot, false);
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

        private double BuildLongEntryStopPrice(double entryPrice, double candleOpen, double candleClose, double candleLow)
        {
            double body = Math.Abs(candleClose - candleOpen);
            double raw;

            switch (EntryStopMode)
            {
                case InitialStopMode.CandleOpen:
                    raw = candleOpen;
                    break;
                case InitialStopMode.WickExtreme:
                    raw = candleLow;
                    break;
                default:
                    raw = candleClose - (body * (EntryStopBodyPercent / 100.0));
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

            switch (EntryStopMode)
            {
                case InitialStopMode.CandleOpen:
                    raw = candleOpen;
                    break;
                case InitialStopMode.WickExtreme:
                    raw = candleHigh;
                    break;
                default:
                    raw = candleClose + (body * (EntryStopBodyPercent / 100.0));
                    break;
            }

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }

        private double BuildFlipShortStopPrice(double entryPrice, double candleOpen, double candleHigh)
        {
            double raw = FlipStopSetting == FlipStopMode.CandleOpen ? candleOpen : candleHigh;
            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }

        private double BuildFlipLongStopPrice(double entryPrice, double candleOpen, double candleLow)
        {
            double raw = FlipStopSetting == FlipStopMode.CandleOpen ? candleOpen : candleLow;
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

        private bool TimeInAnyEnabledSession(DateTime time)
        {
            return TimeInSession(SessionSlot.Asia, time)
                || TimeInSession(SessionSlot.London, time)
                || TimeInSession(SessionSlot.NewYork, time);
        }

        private bool TimeInNoTradesAfterAnyEnabledSession(DateTime time)
        {
            return TimeInNoTradesAfter(SessionSlot.Asia, time)
                || TimeInNoTradesAfter(SessionSlot.London, time)
                || TimeInNoTradesAfter(SessionSlot.NewYork, time);
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
                    return slot.ToString();
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
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", GroupName = "01. Core", Order = 0)]
        public int EmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "01. Core", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Market Entry", Description = "If true, use market entries. If false, place limit entries at EMA.", GroupName = "01. Core", Order = 2)]
        public bool UseMarketEntry { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Signal Body % Over/Under EMA", Description = "Required percent of the single candle body beyond EMA for entry.", GroupName = "02. Entry", Order = 0)]
        public double SignalBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Stop Mode", Description = "Initial stop mode for non-flip entries.", GroupName = "02. Entry", Order = 1)]
        public InitialStopMode EntryStopMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Entry Stop Body %", Description = "Used when Entry Stop Mode = BodyPercent.", GroupName = "02. Entry", Order = 2)]
        public double EntryStopBodyPercent { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Exit long when close <= EMA - points. Exit short when close >= EMA + points.", GroupName = "03. Exit", Order = 0)]
        public double ExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "Flip Body % Threshold", Description = "Body percent beyond EMA required on the exit candle to flip immediately.", GroupName = "03. Exit", Order = 1)]
        public double FlipBodyThresholdPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Flip Stop Mode", Description = "Initial stop mode for immediate flips.", GroupName = "03. Exit", Order = 2)]
        public FlipStopMode FlipStopSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Asia Session", GroupName = "04. Asia Time", Order = 0)]
        public bool UseAsiaSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", GroupName = "04. Asia Time", Order = 1)]
        public TimeSpan AsiaSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", GroupName = "04. Asia Time", Order = 2)]
        public TimeSpan AsiaSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", GroupName = "04. Asia Time", Order = 3)]
        public TimeSpan AsiaNoTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use London Session", GroupName = "05. London Time", Order = 0)]
        public bool UseLondonSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", GroupName = "05. London Time", Order = 1)]
        public TimeSpan LondonSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", GroupName = "05. London Time", Order = 2)]
        public TimeSpan LondonSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", GroupName = "05. London Time", Order = 3)]
        public TimeSpan LondonNoTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use New York Session", GroupName = "06. New York Time", Order = 0)]
        public bool UseNewYorkSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", GroupName = "06. New York Time", Order = 1)]
        public TimeSpan NewYorkSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", GroupName = "06. New York Time", Order = 2)]
        public TimeSpan NewYorkSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", GroupName = "06. New York Time", Order = 3)]
        public TimeSpan NewYorkNoTradesAfter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close At Session End", Description = "If true, flatten positions and cancel entries at each session end.", GroupName = "07. Sessions", Order = 0)]
        public bool CloseAtSessionEnd { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Session Fill", Description = "Background fill color for active session windows.", GroupName = "07. Sessions", Order = 1)]
        public Brush SessionBrush { get; set; }

        [Browsable(false)]
        public string SessionBrushSerializable
        {
            get { return Serialize.BrushToString(SessionBrush); }
            set { SessionBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Description = "Block entries around scheduled 08:30 and 14:00 news events.", GroupName = "08. News", Order = 0)]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Block Minutes", Description = "Blocks this many minutes before and after each news timestamp.", GroupName = "08. News", Order = 1)]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Debug Logging", GroupName = "09. Debug", Order = 0)]
        public bool DebugLogging { get; set; }
    }
}
