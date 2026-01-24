//
// Heiken Ashi EMA wick strategy with intrabar order placement.
//
#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class Heiken : Strategy
    {
        private EMA ema;
        private Swing swing;
        private Series<double> haOpen;
        private Series<double> haClose;
        private Series<double> haHigh;
        private Series<double> haLow;
        private double lastSwingHigh;
        private double lastSwingLow;
        private bool pullbackSeenLong;
        private bool pullbackSeenShort;
        private double longTargetPrice;
        private double shortTargetPrice;
        private bool longTargetSubmitted;
        private bool shortTargetSubmitted;
        private bool sessionClosed;
        private bool sessionInitialized;
        private SessionSlot activeSession = SessionSlot.None;
        private DateTime lastSessionEvalTime = DateTime.MinValue;
        private DateTime effectiveTimesDate = DateTime.MinValue;
        private TimeZoneInfo targetTimeZone;
        private TimeZoneInfo londonTimeZone;
        private bool activeAutoShiftTimes;
        private TimeSpan activeSessionStart;
        private TimeSpan activeSessionEnd;
        private TimeSpan activeNoTradesAfter;
        private TimeSpan activeSkipStart;
        private TimeSpan activeSkipEnd;
        private TimeSpan activeSkip2Start;
        private TimeSpan activeSkip2End;
        private TimeSpan effectiveSessionStart;
        private TimeSpan effectiveSessionEnd;
        private TimeSpan effectiveNoTradesAfter;
        private TimeSpan effectiveSkipStart;
        private TimeSpan effectiveSkipEnd;
        private TimeSpan effectiveSkip2Start;
        private TimeSpan effectiveSkip2End;
        private TimeSpan session1Start = new TimeSpan(1, 30, 0);
        private TimeSpan session1End = new TimeSpan(5, 30, 0);
        private TimeSpan session1NoTradesAfter = new TimeSpan(5, 0, 0);
        private TimeSpan session2Start = new TimeSpan(9, 40, 0);
        private TimeSpan session2End = new TimeSpan(15, 0, 0);
        private TimeSpan session2NoTradesAfter = new TimeSpan(14, 30, 0);

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Heiken";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsInstantiatedOnEachOptimizationIteration = false;

                EmaPeriod = 21;
                Contracts = 1;
                ExitOnFirstOppositeCandle = false;
                SkipEntryOnExitBar = false;
                RequireEmaCrossOnEntry = false;
                PivotStrength = 3;
                UsePivotTarget = true;
                PullbackTicks = 4;
                TargetOffsetTicks = 0;
                ExitOnEmaCloseThrough = true;

                SessionBrush = Brushes.Gold;
                UseSession1 = true;
                UseSession2 = true;
                AutoShiftSession1 = true;
                AutoShiftSession2 = false;
                CloseAtSessionEnd = true;
                ForceCloseAtSkipStart = true;

                London_SessionStart = new TimeSpan(1, 30, 0);
                London_SessionEnd = new TimeSpan(5, 30, 0);
                London_NoTradesAfter = new TimeSpan(5, 0, 0);
                London_SkipStart = new TimeSpan(0, 0, 0);
                London_SkipEnd = new TimeSpan(0, 0, 0);
                London_Skip2Start = new TimeSpan(0, 0, 0);
                London_Skip2End = new TimeSpan(0, 0, 0);

                NewYork_SessionStart = new TimeSpan(9, 40, 0);
                NewYork_SessionEnd = new TimeSpan(15, 0, 0);
                NewYork_NoTradesAfter = new TimeSpan(14, 30, 0);
                NewYork_SkipStart = new TimeSpan(11, 45, 0);
                NewYork_SkipEnd = new TimeSpan(13, 15, 0);
                NewYork_Skip2Start = new TimeSpan(0, 0, 0);
                NewYork_Skip2End = new TimeSpan(0, 0, 0);
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Tick, 1);
                ema = EMA(EmaPeriod);
                swing = Swing(PivotStrength);
                AddChartIndicator(ema);
                ema.Plots[0].Brush = Brushes.DodgerBlue;
            }
            else if (State == State.DataLoaded)
            {
                haOpen = new Series<double>(this);
                haClose = new Series<double>(this);
                haHigh = new Series<double>(this);
                haLow = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            bool hasPrevBar = CurrentBar >= 1;

            EnsureActiveSession(Time[0]);
            EnsureEffectiveTimes(Time[0], false);
            DrawSessionBackground();

            if (Bars.IsFirstBarOfSession && sessionClosed)
            {
                sessionClosed = false;
            }

            bool crossedSkipWindow = hasPrevBar && (
                (!TimeInSkip(Time[1]) && TimeInSkip(Time[0]))
                || (TimeInSkip(Time[1]) && !TimeInSkip(Time[0])));

            if (crossedSkipWindow && TimeInSkip(Time[0]) && ForceCloseAtSkipStart)
            {
                Flatten("SkipWindow");
            }

            bool crossedSessionEnd = hasPrevBar && CrossedSessionEnd(Time[1], Time[0]);
            bool isLastBarOfTradingSession = Bars.IsLastBarOfSession && !sessionClosed;

            if (crossedSessionEnd || isLastBarOfTradingSession)
            {
                if (CloseAtSessionEnd)
                    Flatten("SessionEnd");

                sessionClosed = true;
            }

            bool crossedNoTradesAfter = hasPrevBar
                && activeSession != SessionSlot.None
                && Time[1].TimeOfDay <= effectiveNoTradesAfter
                && Time[0].TimeOfDay > effectiveNoTradesAfter;

            if (crossedNoTradesAfter)
            {
            }

            if (TimeInSkip(Time[0]))
                return;

            if (!TimeInSession(Time[0]))
                return;

            if (TimeInNoTradesAfter(Time[0]))
                return;

            double haCloseValue;
            double haOpenValue;
            double haHighValue;
            double haLowValue;

            if (BarsPeriod.BarsPeriodType == BarsPeriodType.HeikenAshi)
            {
                haOpenValue = Open[0];
                haCloseValue = Close[0];
                haHighValue = High[0];
                haLowValue = Low[0];
            }
            else
            {
                haCloseValue = (Open[0] + High[0] + Low[0] + Close[0]) / 4.0;
                haOpenValue = CurrentBar == 0
                    ? (Open[0] + Close[0]) / 2.0
                    : (haOpen[1] + haClose[1]) / 2.0;
                haHighValue = Math.Max(High[0], Math.Max(haOpenValue, haCloseValue));
                haLowValue = Math.Min(Low[0], Math.Min(haOpenValue, haCloseValue));
            }

            haOpen[0] = haOpenValue;
            haClose[0] = haCloseValue;
            haHigh[0] = haHighValue;
            haLow[0] = haLowValue;

            if (CurrentBar < Math.Max(2, EmaPeriod))
                return;

            double emaValue = ema[0];
            double wickEpsilon = TickSize * 0.25;
            double pullbackBand = PullbackTicks * TickSize;

            bool isBull = haCloseValue > haOpenValue;
            bool isBear = haCloseValue < haOpenValue;
            double haBodyTop = Math.Max(haOpenValue, haCloseValue);
            double haBodyBottom = Math.Min(haOpenValue, haCloseValue);
            bool noLowerWick = (haBodyBottom - haLowValue) <= wickEpsilon;
            bool noUpperWick = (haHighValue - haBodyTop) <= wickEpsilon;
            bool emaUp = ema[0] > ema[1];
            bool emaDown = ema[0] < ema[1];
            bool exitedThisBar = false;

            double swingHigh = swing != null ? swing.SwingHigh[0] : double.NaN;
            if (!double.IsNaN(swingHigh))
                lastSwingHigh = swingHigh;

            double swingLow = swing != null ? swing.SwingLow[0] : double.NaN;
            if (!double.IsNaN(swingLow))
                lastSwingLow = swingLow;

            if (!emaUp)
                pullbackSeenLong = false;
            if (!emaDown)
                pullbackSeenShort = false;

            if (emaUp && (isBear || haLowValue <= emaValue + pullbackBand))
                pullbackSeenLong = true;
            if (emaDown && (isBull || haHighValue >= emaValue - pullbackBand))
                pullbackSeenShort = true;

            bool longTrigger = emaUp
                && pullbackSeenLong
                && isBull
                && noLowerWick
                && haCloseValue >= emaValue;
            bool shortTrigger = emaDown
                && pullbackSeenShort
                && isBear
                && noUpperWick
                && haCloseValue <= emaValue;

            bool exitLongSignal = ExitOnFirstOppositeCandle
                ? isBear
                : (isBear && noUpperWick);
            bool exitShortSignal = ExitOnFirstOppositeCandle
                ? isBull
                : (isBull && noLowerWick);

            if (ExitOnEmaCloseThrough)
            {
                if (haCloseValue < emaValue)
                    exitLongSignal = true;
                if (haCloseValue > emaValue)
                    exitShortSignal = true;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                longTargetSubmitted = false;
                shortTargetSubmitted = false;
                longTargetPrice = 0;
                shortTargetPrice = 0;
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (exitLongSignal)
                {
                    ExitLong(1, "ExitLong", "LongEntry");
                    exitedThisBar = true;
                }

                if (UsePivotTarget && !longTargetSubmitted && lastSwingHigh > 0)
                {
                    longTargetPrice = lastSwingHigh + TargetOffsetTicks * TickSize;
                    if (longTargetPrice > Close[0])
                    {
                        ExitLongLimit(1, true, Position.Quantity, longTargetPrice, "PivotTargetLong", "LongEntry");
                        longTargetSubmitted = true;
                    }
                }
                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (exitShortSignal)
                {
                    ExitShort(1, "ExitShort", "ShortEntry");
                    exitedThisBar = true;
                }

                if (UsePivotTarget && !shortTargetSubmitted && lastSwingLow > 0)
                {
                    shortTargetPrice = lastSwingLow - TargetOffsetTicks * TickSize;
                    if (shortTargetPrice < Close[0])
                    {
                        ExitShortLimit(1, true, Position.Quantity, shortTargetPrice, "PivotTargetShort", "ShortEntry");
                        shortTargetSubmitted = true;
                    }
                }
                return;
            }

            if (SkipEntryOnExitBar && exitedThisBar)
                return;

            if (longTrigger)
            {
                EnterLong(1, Contracts, "LongEntry");
                pullbackSeenLong = false;
            }
            else if (shortTrigger)
            {
                EnterShort(1, Contracts, "ShortEntry");
                pullbackSeenShort = false;
            }
        }

        private void Flatten(string reason)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(1, "Exit_" + reason, "LongEntry");
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(1, "Exit_" + reason, "ShortEntry");
        }

        private bool TimeInSkip(DateTime time)
        {
            EnsureActiveSession(time);
            if (activeSession == SessionSlot.None)
                return false;
            EnsureEffectiveTimes(time, false);
            TimeSpan now = time.TimeOfDay;

            bool inSkip1 = false;
            bool inSkip2 = false;

            if (effectiveSkipStart != TimeSpan.Zero && effectiveSkipEnd != TimeSpan.Zero)
            {
                inSkip1 = (effectiveSkipStart < effectiveSkipEnd)
                    ? (now >= effectiveSkipStart && now <= effectiveSkipEnd)
                    : (now >= effectiveSkipStart || now <= effectiveSkipEnd);
            }

            if (effectiveSkip2Start != TimeSpan.Zero && effectiveSkip2End != TimeSpan.Zero)
            {
                inSkip2 = (effectiveSkip2Start < effectiveSkip2End)
                    ? (now >= effectiveSkip2Start && now <= effectiveSkip2End)
                    : (now >= effectiveSkip2Start || now <= effectiveSkip2End);
            }

            return inSkip1 || inSkip2;
        }

        private bool TimeInSession(DateTime time)
        {
            EnsureActiveSession(time);
            if (activeSession == SessionSlot.None)
                return false;
            EnsureEffectiveTimes(time, false);
            TimeSpan now = time.TimeOfDay;

            if (effectiveSessionStart < effectiveSessionEnd)
                return now >= effectiveSessionStart && now < effectiveSessionEnd;

            return now >= effectiveSessionStart || now < effectiveSessionEnd;
        }

        private bool TimeInNoTradesAfter(DateTime time)
        {
            EnsureActiveSession(time);
            if (activeSession == SessionSlot.None)
                return false;
            EnsureEffectiveTimes(time, false);
            TimeSpan now = time.TimeOfDay;

            if (effectiveSessionStart < effectiveSessionEnd)
                return now >= effectiveNoTradesAfter && now < effectiveSessionEnd;

            return (now >= effectiveNoTradesAfter || now < effectiveSessionEnd);
        }

        private void DrawSessionBackground()
        {
            if (CurrentBar < 1)
                return;

            DateTime barTime = Time[0];
            EnsureActiveSession(barTime);
            if (activeSession == SessionSlot.None)
                return;
            EnsureEffectiveTimes(barTime, false);
            bool isOvernight = effectiveSessionStart > effectiveSessionEnd;

            DateTime sessionStartTime = barTime.Date + effectiveSessionStart;
            DateTime sessionEndTime = isOvernight
                ? sessionStartTime.AddDays(1).Date + effectiveSessionEnd
                : sessionStartTime.Date + effectiveSessionEnd;

            int startBarsAgo = Bars.GetBar(sessionStartTime);
            int endBarsAgo = Bars.GetBar(sessionEndTime);

            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            string tag = $"DUO_SessionFill_{activeSession}_{sessionStartTime:yyyyMMdd_HHmm}";

            if (DrawObjects[tag] == null)
            {
                Draw.Rectangle(
                    this,
                    tag,
                    false,
                    sessionStartTime,
                    0,
                    sessionEndTime,
                    30000,
                    Brushes.Transparent,
                    SessionBrush ?? Brushes.DarkSlateGray,
                    10
                ).ZOrder = -1;
            }

            var r = new SolidColorBrush(Color.FromArgb(70, 255, 0, 0));

            DateTime noTradesAfterTime = Time[0].Date + effectiveNoTradesAfter;
            if (effectiveSessionStart > effectiveSessionEnd && effectiveNoTradesAfter < effectiveSessionStart)
                noTradesAfterTime = noTradesAfterTime.AddDays(1);

            Draw.VerticalLine(this, $"NoTradesAfter_{activeSession}_{sessionStartTime:yyyyMMdd_HHmm}", noTradesAfterTime, r,
                DashStyleHelper.Solid, 2);

            DrawSkipWindow("Skip1", effectiveSkipStart, effectiveSkipEnd);
            DrawSkipWindow("Skip2", effectiveSkip2Start, effectiveSkip2End);
        }

        private void DrawSkipWindow(string tagPrefix, TimeSpan start, TimeSpan end)
        {
            if (start == TimeSpan.Zero || end == TimeSpan.Zero)
                return;

            DateTime barDate = Time[0].Date;
            DateTime windowStart = barDate + start;
            DateTime windowEnd = barDate + end;

            if (start > end)
                windowEnd = windowEnd.AddDays(1);

            int startBarsAgo = Bars.GetBar(windowStart);
            int endBarsAgo = Bars.GetBar(windowEnd);

            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));
            areaBrush.Freeze();
            var lineBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
            lineBrush.Freeze();

            string rectTag = $"DUO_{tagPrefix}_Rect_{windowStart:yyyyMMdd}";
            Draw.Rectangle(
                this,
                rectTag,
                false,
                windowStart,
                0,
                windowEnd,
                30000,
                lineBrush,
                areaBrush,
                2
            ).ZOrder = -1;

            string startTag = $"DUO_{tagPrefix}_Start_{windowStart:yyyyMMdd}";
            Draw.VerticalLine(this, startTag, windowStart, lineBrush, DashStyleHelper.Solid, 2);

            string endTag = $"DUO_{tagPrefix}_End_{windowEnd:yyyyMMdd}";
            Draw.VerticalLine(this, endTag, windowEnd, lineBrush, DashStyleHelper.Solid, 2);
        }


        private void EnsureEffectiveTimes(DateTime barTime, bool log)
        {
            if (activeSession == SessionSlot.None)
            {
                effectiveSessionStart = TimeSpan.Zero;
                effectiveSessionEnd = TimeSpan.Zero;
                effectiveNoTradesAfter = TimeSpan.Zero;
                effectiveSkipStart = TimeSpan.Zero;
                effectiveSkipEnd = TimeSpan.Zero;
                effectiveSkip2Start = TimeSpan.Zero;
                effectiveSkip2End = TimeSpan.Zero;
                return;
            }

            if (!activeAutoShiftTimes)
            {
                effectiveSessionStart = activeSessionStart;
                effectiveSessionEnd = activeSessionEnd;
                effectiveNoTradesAfter = activeNoTradesAfter;
                effectiveSkipStart = activeSkipStart;
                effectiveSkipEnd = activeSkipEnd;
                effectiveSkip2Start = activeSkip2Start;
                effectiveSkip2End = activeSkip2End;
                return;
            }

            DateTime date = barTime.Date;
            if (date == effectiveTimesDate)
                return;

            effectiveTimesDate = date;

            TimeSpan shift = GetLondonSessionShiftForDate(date);
            effectiveSessionStart = ShiftTime(activeSessionStart, shift);
            effectiveSessionEnd = ShiftTime(activeSessionEnd, shift);
            effectiveNoTradesAfter = ShiftTime(activeNoTradesAfter, shift);
            effectiveSkipStart = ShiftTime(activeSkipStart, shift);
            effectiveSkipEnd = ShiftTime(activeSkipEnd, shift);
            effectiveSkip2Start = ShiftTime(activeSkip2Start, shift);
            effectiveSkip2End = ShiftTime(activeSkip2End, shift);
        }

        private TimeSpan GetLondonSessionShiftForDate(DateTime date)
        {
            DateTime utcSample = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);
            DateTime utcRef = new DateTime(date.Year, 1, 15, 12, 0, 0, DateTimeKind.Utc);

            TimeZoneInfo londonTz = GetLondonTimeZone();
            TimeZoneInfo targetTz = GetTargetTimeZone();

            TimeSpan baseline = londonTz.GetUtcOffset(utcRef) - targetTz.GetUtcOffset(utcRef);
            TimeSpan actual = londonTz.GetUtcOffset(utcSample) - targetTz.GetUtcOffset(utcSample);

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


        private void EnsureActiveSession(DateTime time)
        {
            if (time <= lastSessionEvalTime)
                return;

            lastSessionEvalTime = time;
            SessionSlot desired = DetermineSessionForTime(time);

            if (!sessionInitialized || desired != activeSession)
            {
                sessionInitialized = true;
                activeSession = desired;
                if (activeSession != SessionSlot.None)
                    ApplyInputsForSession(activeSession);
            }
        }

        private SessionSlot DetermineSessionForTime(DateTime time)
        {
            GetSessionWindowForSession(SessionSlot.Session1, time.Date, out TimeSpan session1StartLocal, out TimeSpan session1EndLocal);
            GetSessionWindowForSession(SessionSlot.Session2, time.Date, out TimeSpan session2StartLocal, out TimeSpan session2EndLocal);

            bool session1Configured = IsSessionConfigured(SessionSlot.Session1);
            bool session2Configured = IsSessionConfigured(SessionSlot.Session2);

            if (session1Configured && IsTimeInRange(time.TimeOfDay, session1StartLocal, session1EndLocal))
                return SessionSlot.Session1;
            if (session2Configured && IsTimeInRange(time.TimeOfDay, session2StartLocal, session2EndLocal))
                return SessionSlot.Session2;

            if (!session1Configured && !session2Configured)
                return SessionSlot.None;

            DateTime nextSession1Start = DateTime.MaxValue;
            DateTime nextSession2Start = DateTime.MaxValue;

            if (session1Configured)
            {
                nextSession1Start = time.Date + session1StartLocal;
                if (nextSession1Start <= time)
                    nextSession1Start = nextSession1Start.AddDays(1);
            }

            if (session2Configured)
            {
                nextSession2Start = time.Date + session2StartLocal;
                if (nextSession2Start <= time)
                    nextSession2Start = nextSession2Start.AddDays(1);
            }

            return nextSession1Start <= nextSession2Start
                ? SessionSlot.Session1
                : SessionSlot.Session2;
        }

        private bool CrossedSessionEnd(DateTime previousTime, DateTime currentTime)
        {
            return CrossedSessionEndForSession(SessionSlot.Session1, previousTime, currentTime)
                || CrossedSessionEndForSession(SessionSlot.Session2, previousTime, currentTime);
        }

        private bool CrossedSessionEndForSession(SessionSlot session, DateTime previousTime, DateTime currentTime)
        {
            if (!IsSessionConfigured(session))
                return false;

            GetSessionWindowForSession(session, currentTime.Date, out TimeSpan start, out TimeSpan end);
            bool wasInSession = IsTimeInRange(previousTime.TimeOfDay, start, end);
            bool nowInSession = IsTimeInRange(currentTime.TimeOfDay, start, end);
            return wasInSession && !nowInSession;
        }

        private void GetSessionWindowForSession(SessionSlot session, DateTime date, out TimeSpan start, out TimeSpan end)
        {
            bool autoShift;
            switch (session)
            {
                case SessionSlot.Session1:
                    start = London_SessionStart;
                    end = London_SessionEnd;
                    autoShift = AutoShiftSession1;
                    break;
                case SessionSlot.Session2:
                    start = NewYork_SessionStart;
                    end = NewYork_SessionEnd;
                    autoShift = AutoShiftSession2;
                    break;
                default:
                    start = TimeSpan.Zero;
                    end = TimeSpan.Zero;
                    autoShift = false;
                    break;
            }

            if (!autoShift)
                return;

            TimeSpan shift = GetLondonSessionShiftForDate(date);
            start = ShiftTime(start, shift);
            end = ShiftTime(end, shift);
        }

        private bool IsSessionConfigured(SessionSlot session)
        {
            switch (session)
            {
                case SessionSlot.Session1:
                    return UseSession1 && London_SessionStart != London_SessionEnd;
                case SessionSlot.Session2:
                    return UseSession2 && NewYork_SessionStart != NewYork_SessionEnd;
                default:
                    return false;
            }
        }

        private void ApplyInputsForSession(SessionSlot session)
        {
            switch (session)
            {
                case SessionSlot.Session1:
                    activeAutoShiftTimes = AutoShiftSession1;
                    activeSessionStart = London_SessionStart;
                    activeSessionEnd = London_SessionEnd;
                    activeNoTradesAfter = London_NoTradesAfter;
                    activeSkipStart = London_SkipStart;
                    activeSkipEnd = London_SkipEnd;
                    activeSkip2Start = London_Skip2Start;
                    activeSkip2End = London_Skip2End;
                    break;
                case SessionSlot.Session2:
                    activeAutoShiftTimes = AutoShiftSession2;
                    activeSessionStart = NewYork_SessionStart;
                    activeSessionEnd = NewYork_SessionEnd;
                    activeNoTradesAfter = NewYork_NoTradesAfter;
                    activeSkipStart = NewYork_SkipStart;
                    activeSkipEnd = NewYork_SkipEnd;
                    activeSkip2Start = NewYork_Skip2Start;
                    activeSkip2End = NewYork_Skip2End;
                    break;
                default:
                    activeAutoShiftTimes = false;
                    activeSessionStart = TimeSpan.Zero;
                    activeSessionEnd = TimeSpan.Zero;
                    activeNoTradesAfter = TimeSpan.Zero;
                    activeSkipStart = TimeSpan.Zero;
                    activeSkipEnd = TimeSpan.Zero;
                    activeSkip2Start = TimeSpan.Zero;
                    activeSkip2End = TimeSpan.Zero;
                    break;
            }
        }

        private bool IsTimeInRange(TimeSpan time, TimeSpan start, TimeSpan end)
        {
            if (start < end)
                return time >= start && time < end;

            return time >= start || time < end;
        }

        public enum SessionSlot
        {
            None,
            Session1,
            Session2
        }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "EMA Period", GroupName = "Parameters", Order = 0)]
        public int EmaPeriod { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Contracts", GroupName = "Parameters", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit On First Opposite Candle", GroupName = "Parameters", Order = 2)]
        public bool ExitOnFirstOppositeCandle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Entry On Exit Bar", GroupName = "Parameters", Order = 3)]
        public bool SkipEntryOnExitBar { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require EMA Cross On Entry", GroupName = "Parameters", Order = 4)]
        public bool RequireEmaCrossOnEntry { get; set; }

        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Pivot Strength", GroupName = "Parameters", Order = 5)]
        public int PivotStrength { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Pivot Target", GroupName = "Parameters", Order = 6)]
        public bool UsePivotTarget { get; set; }

        [Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Pullback Ticks", GroupName = "Parameters", Order = 7)]
        public int PullbackTicks { get; set; }

        [Range(0, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "Target Offset Ticks", GroupName = "Parameters", Order = 8)]
        public int TargetOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Exit On EMA Close Through", GroupName = "Parameters", Order = 9)]
        public bool ExitOnEmaCloseThrough { get; set; }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Fill", Description = "Color of the session background", Order = 4, GroupName = "Sessions")]
        public Brush SessionBrush { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade London (1:30-5:30)", Description = "Allow trading during London", Order = 1, GroupName = "Sessions")]
        public bool UseSession1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade New York (9:40-15:00)", Description = "Allow trading during New York", Order = 2, GroupName = "Sessions")]
        public bool UseSession2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Auto Shift London", Description = "Apply London DST auto-shift to London times", Order = 3, GroupName = "Sessions")]
        public bool AutoShiftSession1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Auto Shift NewYork", Description = "Apply London DST auto-shift to New York times", Order = 4, GroupName = "Sessions")]
        public bool AutoShiftSession2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close at Session End", Description = "If true, open trades will be closed and working orders canceled at session end", Order = 5, GroupName = "Sessions")]
        public bool CloseAtSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "When session is starting", Order = 1, GroupName = "London Time")]
        public TimeSpan London_SessionStart
        {
            get { return session1Start; }
            set { session1Start = new TimeSpan(value.Hours, value.Minutes, 0); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "When session is ending, all positions and orders will be canceled when this time is passed", Order = 2, GroupName = "London Time")]
        public TimeSpan London_SessionEnd
        {
            get { return session1End; }
            set { session1End = new TimeSpan(value.Hours, value.Minutes, 0); }
        }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", Description = "No more orders is being placed between this time and session end,", Order = 3, GroupName = "London Time")]
        public TimeSpan London_NoTradesAfter
        {
            get { return session1NoTradesAfter; }
            set { session1NoTradesAfter = new TimeSpan(value.Hours, value.Minutes, 0); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "When session is starting", Order = 1, GroupName = "New York Time")]
        public TimeSpan NewYork_SessionStart
        {
            get { return session2Start; }
            set { session2Start = new TimeSpan(value.Hours, value.Minutes, 0); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "When session is ending, all positions and orders will be canceled when this time is passed", Order = 2, GroupName = "New York Time")]
        public TimeSpan NewYork_SessionEnd
        {
            get { return session2End; }
            set { session2End = new TimeSpan(value.Hours, value.Minutes, 0); }
        }

        [NinjaScriptProperty]
        [Display(Name = "No Trades After", Description = "No more orders is being placed between this time and session end,", Order = 3, GroupName = "New York Time")]
        public TimeSpan NewYork_NoTradesAfter
        {
            get { return session2NoTradesAfter; }
            set { session2NoTradesAfter = new TimeSpan(value.Hours, value.Minutes, 0); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start", Description = "Start of skip window", Order = 1, GroupName = "London Skip Times")]
        public TimeSpan London_SkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End", Description = "End of skip window", Order = 2, GroupName = "London Skip Times")]
        public TimeSpan London_SkipEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start 2", Description = "Start of 2nd skip window", Order = 3, GroupName = "London Skip Times")]
        public TimeSpan London_Skip2Start { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End 2", Description = "End of 2nd skip window", Order = 4, GroupName = "London Skip Times")]
        public TimeSpan London_Skip2End { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start", Description = "Start of skip window", Order = 1, GroupName = "New York Skip Times")]
        public TimeSpan NewYork_SkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End", Description = "End of skip window", Order = 2, GroupName = "New York Skip Times")]
        public TimeSpan NewYork_SkipEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start 2", Description = "Start of 2nd skip window", Order = 3, GroupName = "New York Skip Times")]
        public TimeSpan NewYork_Skip2Start { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End 2", Description = "End of 2nd skip window", Order = 4, GroupName = "New York Skip Times")]
        public TimeSpan NewYork_Skip2End { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Force Close at Skip Start", Description = "If true, flatten/cancel as soon as a skip window begins", Order = 5, GroupName = "Skip Times")]
        public bool ForceCloseAtSkipStart { get; set; }

    }
}
