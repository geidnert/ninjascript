#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class AnalyzerDuoStateExporter : Strategy
    {
        private enum SessionSlot
        {
            None,
            Asia,
            Asia2,
            Asia3,
            London,
            London2,
            London3,
            NewYork,
            NewYork2,
            NewYork3,
        }

        private sealed class SessionConfig
        {
            public SessionSlot Slot { get; set; }
            public string Label { get; set; }
            public bool Enabled { get; set; }
            public TimeSpan Start { get; set; }
            public TimeSpan End { get; set; }
            public bool AutoShift { get; set; }
            public bool BlockSundayTrades { get; set; }
            public TimeSpan SkipStart { get; set; }
            public TimeSpan SkipEnd { get; set; }
            public int EmaPeriod { get; set; }
            public double MinimumAdx { get; set; }
            public double MaximumAdx { get; set; }
            public double MinimumAdxSlope { get; set; }
            public double MinimumAtr { get; set; }
            public double MaxEntryDistanceFromEmaPoints { get; set; }
            public bool AllowLong { get; set; }
            public bool AllowShort { get; set; }
        }

        private static readonly SessionSlot[] ConfigurableSessionSlots =
        {
            SessionSlot.Asia,
            SessionSlot.Asia2,
            SessionSlot.Asia3,
            SessionSlot.London,
            SessionSlot.London2,
            SessionSlot.London3,
            SessionSlot.NewYork,
            SessionSlot.NewYork2,
            SessionSlot.NewYork3,
        };

        private readonly Dictionary<SessionSlot, SessionConfig> sessionConfigs = new Dictionary<SessionSlot, SessionConfig>();
        private StreamWriter writer;
        private string tempFilePath = string.Empty;
        private string finalFilePath = string.Empty;
        private string metadataFilePath = string.Empty;
        private DateTime firstBarTime = DateTime.MinValue;
        private DateTime lastBarTime = DateTime.MinValue;
        private int rowsWritten;
        private TimeZoneInfo targetTimeZone;
        private TimeZoneInfo londonTimeZone;

        private EMA ema16;
        private EMA ema21;
        private DM dm14;
        private ATR atr14;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "AnalyzerDuoStateExporter";
                Description = "Exports DUO-relevant per-bar analyzer state for parity debugging.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = false;
                BarsRequiredToTrade = 21;
                IsInstantiatedOnEachOptimizationIteration = false;

                ExportRoot = string.Empty;
                ExportFileName = string.Empty;
                OverwriteExisting = true;
                WriteMetadataFile = true;
            }
            else if (State == State.DataLoaded)
            {
                InitializeSessionConfigs();

                ema16 = EMA(16);
                ema21 = EMA(21);
                dm14 = DM(14);
                atr14 = ATR(14);

                PrepareOutputFiles();
            }
            else if (State == State.Terminated)
            {
                FinalizeOutputFiles();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || writer == null)
            {
                return;
            }

            if (CurrentBar < BarsRequiredToTrade)
            {
                return;
            }

            var barTime = Time[0];
            if (firstBarTime == DateTime.MinValue)
            {
                firstBarTime = barTime;
            }

            lastBarTime = barTime;

            var activeSlot = DetermineSessionForTime(barTime);
            SessionConfig config;
            var hasActiveSession = sessionConfigs.TryGetValue(activeSlot, out config);

            var emaValue = hasActiveSession && config.EmaPeriod == 16 ? ema16[0] : ema21[0];
            var emaSlope = hasActiveSession && config.EmaPeriod == 16 ? ema16[0] - ema16[1] : ema21[0] - ema21[1];
            var adxValue = dm14[0];
            var adxSlope = dm14[0] - dm14[1];
            var atrValue = atr14[0];

            var bullish = Close[0] > Open[0];
            var bearish = Close[0] < Open[0];
            var bodyAbovePercent = GetBodyPercentAboveEma(Open[0], Close[0], emaValue);
            var bodyBelowPercent = GetBodyPercentBelowEma(Open[0], Close[0], emaValue);

            var inSession = hasActiveSession && TimeInSession(activeSlot, barTime);
            var inNySkip = hasActiveSession && IsNewYorkFamily(activeSlot) && IsSkipWindowActive(config, barTime);
            var asiaSundayBlocked = hasActiveSession && IsAsiaFamily(activeSlot) && config.BlockSundayTrades && barTime.DayOfWeek == DayOfWeek.Sunday;
            var adxMinPass = hasActiveSession && adxValue >= config.MinimumAdx;
            var adxMaxPass = !hasActiveSession || config.MaximumAdx <= 0.0 || adxValue <= config.MaximumAdx;
            var adxSlopePass = hasActiveSession && adxSlope >= config.MinimumAdxSlope;
            var atrPass = !hasActiveSession || config.MinimumAtr <= 0.0 || atrValue >= config.MinimumAtr;
            var distancePoints = Math.Abs(Close[0] - emaValue);
            var distancePass = !hasActiveSession || config.MaxEntryDistanceFromEmaPoints <= 0.0 || distancePoints <= config.MaxEntryDistanceFromEmaPoints;
            var longSignalRaw = bullish && bodyAbovePercent > 0.0;
            var shortSignalRaw = bearish && bodyBelowPercent > 0.0;
            var longSignal = hasActiveSession && longSignalRaw && config.AllowLong;
            var shortSignal = hasActiveSession && shortSignalRaw && config.AllowShort;
            var canTradeNow = hasActiveSession && inSession && !inNySkip && !asiaSundayBlocked && adxMinPass && adxMaxPass && adxSlopePass && atrPass;

            writer.Write(barTime.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(activeSlot.ToString());
            writer.Write(';');
            writer.Write(Open[0].ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(High[0].ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(Low[0].ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(Close[0].ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(emaValue.ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(emaSlope.ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(adxValue.ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(adxSlope.ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(atrValue.ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(bodyAbovePercent.ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(bodyBelowPercent.ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(distancePoints.ToString("0.########", CultureInfo.InvariantCulture));
            writer.Write(';');
            writer.Write(BoolToken(inSession));
            writer.Write(';');
            writer.Write(BoolToken(inNySkip));
            writer.Write(';');
            writer.Write(BoolToken(asiaSundayBlocked));
            writer.Write(';');
            writer.Write(BoolToken(adxMinPass));
            writer.Write(';');
            writer.Write(BoolToken(adxMaxPass));
            writer.Write(';');
            writer.Write(BoolToken(adxSlopePass));
            writer.Write(';');
            writer.Write(BoolToken(atrPass));
            writer.Write(';');
            writer.Write(BoolToken(distancePass));
            writer.Write(';');
            writer.Write(BoolToken(longSignalRaw));
            writer.Write(';');
            writer.Write(BoolToken(shortSignalRaw));
            writer.Write(';');
            writer.Write(BoolToken(longSignal));
            writer.Write(';');
            writer.Write(BoolToken(shortSignal));
            writer.Write(';');
            writer.Write(BoolToken(canTradeNow));
            writer.Write(';');
            writer.Write(hasActiveSession ? config.MinimumAdx.ToString("0.########", CultureInfo.InvariantCulture) : string.Empty);
            writer.Write(';');
            writer.Write(hasActiveSession ? config.MaximumAdx.ToString("0.########", CultureInfo.InvariantCulture) : string.Empty);
            writer.Write(';');
            writer.Write(hasActiveSession ? config.MinimumAdxSlope.ToString("0.########", CultureInfo.InvariantCulture) : string.Empty);
            writer.Write(';');
            writer.Write(hasActiveSession ? config.MinimumAtr.ToString("0.########", CultureInfo.InvariantCulture) : string.Empty);
            writer.Write(';');
            writer.Write(hasActiveSession ? config.MaxEntryDistanceFromEmaPoints.ToString("0.########", CultureInfo.InvariantCulture) : string.Empty);
            writer.Write(';');
            writer.Write(Volume[0].ToString(CultureInfo.InvariantCulture));
            writer.WriteLine();

            rowsWritten++;
        }

        private void InitializeSessionConfigs()
        {
            sessionConfigs.Clear();

            sessionConfigs[SessionSlot.Asia] = new SessionConfig { Slot = SessionSlot.Asia, Label = "Asia 1", Enabled = true, Start = new TimeSpan(18, 30, 0), End = new TimeSpan(20, 0, 0), AutoShift = false, BlockSundayTrades = false, SkipStart = TimeSpan.Zero, SkipEnd = TimeSpan.Zero, EmaPeriod = 21, MinimumAdx = 18.73, MaximumAdx = 46.8, MinimumAdxSlope = 1.14, MinimumAtr = 7.7, MaxEntryDistanceFromEmaPoints = 9.0, AllowLong = true, AllowShort = true };
            sessionConfigs[SessionSlot.Asia2] = new SessionConfig { Slot = SessionSlot.Asia2, Label = "Asia 2", Enabled = true, Start = new TimeSpan(20, 0, 0), End = new TimeSpan(23, 59, 0), AutoShift = false, BlockSundayTrades = false, SkipStart = TimeSpan.Zero, SkipEnd = TimeSpan.Zero, EmaPeriod = 21, MinimumAdx = 21.3, MaximumAdx = 53.0, MinimumAdxSlope = 1.15, MinimumAtr = 5.1, MaxEntryDistanceFromEmaPoints = 0.0, AllowLong = true, AllowShort = true };
            sessionConfigs[SessionSlot.Asia3] = new SessionConfig { Slot = SessionSlot.Asia3, Label = "Asia 3", Enabled = true, Start = new TimeSpan(0, 0, 0), End = new TimeSpan(2, 0, 0), AutoShift = false, BlockSundayTrades = false, SkipStart = TimeSpan.Zero, SkipEnd = TimeSpan.Zero, EmaPeriod = 21, MinimumAdx = 19.4, MaximumAdx = 64.4, MinimumAdxSlope = 1.3, MinimumAtr = 4.0, MaxEntryDistanceFromEmaPoints = 18.0, AllowLong = true, AllowShort = true };
            sessionConfigs[SessionSlot.London] = new SessionConfig { Slot = SessionSlot.London, Label = "London 1", Enabled = true, Start = new TimeSpan(1, 45, 0), End = new TimeSpan(3, 0, 0), AutoShift = true, BlockSundayTrades = false, SkipStart = TimeSpan.Zero, SkipEnd = TimeSpan.Zero, EmaPeriod = 21, MinimumAdx = 17.9, MaximumAdx = 38.25, MinimumAdxSlope = 0.99, MinimumAtr = 7.6, MaxEntryDistanceFromEmaPoints = 0.0, AllowLong = true, AllowShort = true };
            sessionConfigs[SessionSlot.London2] = new SessionConfig { Slot = SessionSlot.London2, Label = "London 2", Enabled = true, Start = new TimeSpan(3, 0, 0), End = new TimeSpan(5, 0, 0), AutoShift = true, BlockSundayTrades = false, SkipStart = TimeSpan.Zero, SkipEnd = TimeSpan.Zero, EmaPeriod = 21, MinimumAdx = 24.5, MaximumAdx = 33.8, MinimumAdxSlope = 0.94, MinimumAtr = 7.8, MaxEntryDistanceFromEmaPoints = 9.0, AllowLong = true, AllowShort = true };
            sessionConfigs[SessionSlot.London3] = new SessionConfig { Slot = SessionSlot.London3, Label = "London 3", Enabled = true, Start = new TimeSpan(5, 0, 0), End = new TimeSpan(8, 55, 0), AutoShift = true, BlockSundayTrades = false, SkipStart = TimeSpan.Zero, SkipEnd = TimeSpan.Zero, EmaPeriod = 21, MinimumAdx = 38.7, MaximumAdx = 42.9, MinimumAdxSlope = 1.0, MinimumAtr = 12.0, MaxEntryDistanceFromEmaPoints = 9.5, AllowLong = true, AllowShort = true };
            sessionConfigs[SessionSlot.NewYork] = new SessionConfig { Slot = SessionSlot.NewYork, Label = "New York 1", Enabled = true, Start = new TimeSpan(9, 35, 0), End = new TimeSpan(11, 30, 0), AutoShift = false, BlockSundayTrades = false, SkipStart = TimeSpan.Zero, SkipEnd = TimeSpan.Zero, EmaPeriod = 16, MinimumAdx = 16.1, MaximumAdx = 65.5, MinimumAdxSlope = 1.63, MinimumAtr = 9.8, MaxEntryDistanceFromEmaPoints = 39.0, AllowLong = true, AllowShort = true };
            sessionConfigs[SessionSlot.NewYork2] = new SessionConfig { Slot = SessionSlot.NewYork2, Label = "New York 2", Enabled = true, Start = new TimeSpan(11, 30, 0), End = new TimeSpan(14, 0, 0), AutoShift = false, BlockSundayTrades = false, SkipStart = new TimeSpan(12, 0, 0), SkipEnd = new TimeSpan(12, 20, 0), EmaPeriod = 16, MinimumAdx = 22.9, MaximumAdx = 47.0, MinimumAdxSlope = 1.56, MinimumAtr = 17.0, MaxEntryDistanceFromEmaPoints = 40.5, AllowLong = true, AllowShort = true };
            sessionConfigs[SessionSlot.NewYork3] = new SessionConfig { Slot = SessionSlot.NewYork3, Label = "New York 3", Enabled = true, Start = new TimeSpan(14, 0, 0), End = new TimeSpan(17, 0, 0), AutoShift = false, BlockSundayTrades = false, SkipStart = TimeSpan.Zero, SkipEnd = TimeSpan.Zero, EmaPeriod = 21, MinimumAdx = 18.5, MaximumAdx = 42.0, MinimumAdxSlope = 1.48, MinimumAtr = 0.0, MaxEntryDistanceFromEmaPoints = 0.0, AllowLong = true, AllowShort = true };
        }

        private SessionSlot DetermineSessionForTime(DateTime time)
        {
            var nextSlot = SessionSlot.None;
            var nextStart = DateTime.MaxValue;
            var hasConfiguredSession = false;

            foreach (var slot in ConfigurableSessionSlots)
            {
                TimeSpan start;
                TimeSpan end;
                if (!TryGetSessionWindow(slot, time, out start, out end))
                {
                    continue;
                }

                hasConfiguredSession = true;
                if (IsTimeInRange(time.TimeOfDay, start, end))
                {
                    return slot;
                }

                var candidateStart = time.Date + start;
                if (candidateStart <= time)
                {
                    candidateStart = candidateStart.AddDays(1);
                }

                if (candidateStart < nextStart)
                {
                    nextStart = candidateStart;
                    nextSlot = slot;
                }
            }

            return hasConfiguredSession ? nextSlot : SessionSlot.None;
        }

        private bool TimeInSession(SessionSlot slot, DateTime time)
        {
            TimeSpan start;
            TimeSpan end;
            return TryGetSessionWindow(slot, time, out start, out end) && IsTimeInRange(time.TimeOfDay, start, end);
        }

        private bool TryGetSessionWindow(SessionSlot slot, DateTime referenceTime, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            SessionConfig config;
            if (!sessionConfigs.TryGetValue(slot, out config) || !config.Enabled || config.Start == config.End)
            {
                return false;
            }

            start = config.Start;
            end = config.End;

            if (config.AutoShift && IsLondonFamily(slot))
            {
                var shift = GetLondonSessionShiftForDate(referenceTime.Date);
                start = ShiftTime(start, shift);
                end = ShiftTime(end, shift);
            }

            return true;
        }

        private bool IsSkipWindowActive(SessionConfig config, DateTime time)
        {
            if (config.SkipStart == config.SkipEnd)
            {
                return false;
            }

            return IsTimeInRange(time.TimeOfDay, config.SkipStart, config.SkipEnd);
        }

        private TimeSpan GetLondonSessionShiftForDate(DateTime date)
        {
            var utcSample = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);
            var utcRef = new DateTime(date.Year, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            var londonTz = GetLondonTimeZone();
            var sessionTz = GetTargetTimeZone();

            var baseline = londonTz.GetUtcOffset(utcRef) - sessionTz.GetUtcOffset(utcRef);
            var actual = londonTz.GetUtcOffset(utcSample) - sessionTz.GetUtcOffset(utcSample);
            return baseline - actual;
        }

        private TimeZoneInfo GetTargetTimeZone()
        {
            if (targetTimeZone != null)
            {
                return targetTimeZone;
            }

            try
            {
                var bars = Bars;
                if (bars != null)
                {
                    var timeZoneProp = bars.GetType().GetProperty("TimeZoneInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (timeZoneProp != null && typeof(TimeZoneInfo).IsAssignableFrom(timeZoneProp.PropertyType))
                    {
                        targetTimeZone = (TimeZoneInfo)timeZoneProp.GetValue(bars, null);
                    }
                }

                if (targetTimeZone == null)
                {
                    targetTimeZone = Bars != null && Bars.TradingHours != null ? Bars.TradingHours.TimeZoneInfo : null;
                }
            }
            catch
            {
                targetTimeZone = null;
            }

            if (targetTimeZone == null)
            {
                targetTimeZone = TimeZoneInfo.Local;
            }

            return targetTimeZone;
        }

        private TimeZoneInfo GetLondonTimeZone()
        {
            if (londonTimeZone != null)
            {
                return londonTimeZone;
            }

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

        private static bool IsAsiaFamily(SessionSlot slot)
        {
            return slot == SessionSlot.Asia || slot == SessionSlot.Asia2 || slot == SessionSlot.Asia3;
        }

        private static bool IsLondonFamily(SessionSlot slot)
        {
            return slot == SessionSlot.London || slot == SessionSlot.London2 || slot == SessionSlot.London3;
        }

        private static bool IsNewYorkFamily(SessionSlot slot)
        {
            return slot == SessionSlot.NewYork || slot == SessionSlot.NewYork2 || slot == SessionSlot.NewYork3;
        }

        private static bool IsTimeInRange(TimeSpan now, TimeSpan start, TimeSpan end)
        {
            if (start < end)
            {
                return now >= start && now < end;
            }

            return now >= start || now < end;
        }

        private static TimeSpan ShiftTime(TimeSpan baseTime, TimeSpan shift)
        {
            var ticks = (baseTime.Ticks + shift.Ticks) % TimeSpan.TicksPerDay;
            if (ticks < 0)
            {
                ticks += TimeSpan.TicksPerDay;
            }

            return new TimeSpan(ticks);
        }

        private static string BoolToken(bool value)
        {
            return value ? "1" : "0";
        }

        private static double GetBodyPercentAboveEma(double open, double close, double emaValue)
        {
            var bodyTop = Math.Max(open, close);
            var bodyBottom = Math.Min(open, close);
            var body = bodyTop - bodyBottom;
            if (body <= 0)
            {
                return 0;
            }

            double above;
            if (emaValue <= bodyBottom)
            {
                above = body;
            }
            else if (emaValue >= bodyTop)
            {
                above = 0;
            }
            else
            {
                above = bodyTop - emaValue;
            }

            return above / body * 100.0;
        }

        private static double GetBodyPercentBelowEma(double open, double close, double emaValue)
        {
            var bodyTop = Math.Max(open, close);
            var bodyBottom = Math.Min(open, close);
            var body = bodyTop - bodyBottom;
            if (body <= 0)
            {
                return 0;
            }

            double below;
            if (emaValue >= bodyTop)
            {
                below = body;
            }
            else if (emaValue <= bodyBottom)
            {
                below = 0;
            }
            else
            {
                below = emaValue - bodyBottom;
            }

            return below / body * 100.0;
        }

        private void PrepareOutputFiles()
        {
            var root = ResolveExportRoot();
            var contractDirectory = Path.Combine(root, SanitizePathComponent(Instrument.FullName));
            Directory.CreateDirectory(contractDirectory);

            var stem = ResolveFileStem();
            finalFilePath = Path.Combine(contractDirectory, stem + ".csv");
            metadataFilePath = Path.Combine(contractDirectory, stem + ".meta.txt");
            tempFilePath = Path.Combine(contractDirectory, stem + ".tmp");

            if (OverwriteExisting)
            {
                TryDelete(finalFilePath);
                TryDelete(metadataFilePath);
                TryDelete(tempFilePath);
            }
            else if (File.Exists(finalFilePath))
            {
                throw new InvalidOperationException("AnalyzerDuoStateExporter output file already exists: " + finalFilePath);
            }

            writer = new StreamWriter(
                new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(false));

            writer.WriteLine("timestamp;session;open;high;low;close;ema;emaSlope;adx;adxSlope;atr;bodyAbovePct;bodyBelowPct;distancePts;inSession;inNySkip;asiaSundayBlocked;adxMinPass;adxMaxPass;adxSlopePass;atrPass;distancePass;longSignalRaw;shortSignalRaw;longSignal;shortSignal;canTrade;minAdx;maxAdx;minAdxSlope;minAtr;maxDist;volume");
        }

        private void FinalizeOutputFiles()
        {
            if (writer == null)
            {
                return;
            }

            try
            {
                writer.Flush();
                writer.Dispose();
                writer = null;

                if (rowsWritten == 0)
                {
                    TryDelete(tempFilePath);
                    return;
                }

                File.Move(tempFilePath, finalFilePath);
                if (WriteMetadataFile)
                {
                    File.WriteAllText(metadataFilePath, BuildMetadata(), new UTF8Encoding(false));
                }

                Print("AnalyzerDuoStateExporter wrote " + rowsWritten + " rows to " + finalFilePath);
            }
            catch (Exception ex)
            {
                Print("AnalyzerDuoStateExporter failed to finalize export: " + ex);
                throw;
            }
        }

        private string ResolveExportRoot()
        {
            return string.IsNullOrWhiteSpace(ExportRoot)
                ? Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "db", "analyzer-duo-state")
                : ExportRoot.Trim();
        }

        private string ResolveFileStem()
        {
            if (!string.IsNullOrWhiteSpace(ExportFileName))
            {
                return Path.GetFileNameWithoutExtension(ExportFileName.Trim());
            }

            var periodType = BarsPeriod.BarsPeriodType.ToString();
            var periodValue = BarsPeriod.Value.ToString(CultureInfo.InvariantCulture);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture, "{0}-{1}-{2}", SanitizePathComponent(periodType + periodValue), SanitizePathComponent(Bars != null && Bars.TradingHours != null ? Bars.TradingHours.Name : "UnknownHours"), timestamp);
        }

        private string BuildMetadata()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Name=" + Name);
            builder.AppendLine("Instrument=" + Instrument.FullName);
            builder.AppendLine("BarsPeriodType=" + BarsPeriod.BarsPeriodType);
            builder.AppendLine("BarsPeriodValue=" + BarsPeriod.Value.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("TradingHours=" + (Bars != null && Bars.TradingHours != null ? Bars.TradingHours.Name : string.Empty));
            builder.AppendLine("RowsWritten=" + rowsWritten.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("FirstBarTime=" + (firstBarTime == DateTime.MinValue ? string.Empty : firstBarTime.ToString("O", CultureInfo.InvariantCulture)));
            builder.AppendLine("LastBarTime=" + (lastBarTime == DateTime.MinValue ? string.Empty : lastBarTime.ToString("O", CultureInfo.InvariantCulture)));
            return builder.ToString();
        }

        private static string SanitizePathComponent(string value)
        {
            var sanitized = value;
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalid, '_');
            }

            return sanitized.Replace(':', '_');
        }

        private static void TryDelete(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        [NinjaScriptProperty]
        [Display(Name = "Export Root", Order = 1, GroupName = "Export")]
        public string ExportRoot { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Export File Name", Order = 2, GroupName = "Export")]
        public string ExportFileName { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Overwrite Existing", Order = 3, GroupName = "Export")]
        public bool OverwriteExisting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Write Metadata File", Order = 4, GroupName = "Export")]
        public bool WriteMetadataFile { get; set; }
    }
}
