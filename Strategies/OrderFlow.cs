#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
using TraderResearch.OrderFlow;
#endregion

// OrderFlow is intentionally a chart-first strategy.  It builds the completed
// CME ETH session profile from actual 1-tick Last-trade volume, then looks for
// a break outside VAH/VAL followed by a qualified reclaim, or optional
// consecutive-close acceptance outside value in the expansion direction.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class OrderFlow : Strategy
    {
        public enum TakeProfitMode { FixedPoints, OppositeValueArea, PointOfControl, PreviousSwingWick }
        public enum StopLossMode { FixedPoints, SignalBarOppositeWick }

        private const string StrategySignalPrefix = "OrderFlow";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";
        private const string InvalidConfigurationExitSignal = StrategySignalPrefix + "InvalidConfiguration";
        private const string MaxBalanceExitSignal = StrategySignalPrefix + "MaxBalance";
        private const string MaxDailyProfitExitSignal = StrategySignalPrefix + "MaxDailyProfit";
        private const int SwingStrength = 2;
        private static readonly Brush InactiveSessionBrush = CreateFrozenBrush(72, 170, 170, 170);

        private sealed class SessionProfile
        {
            public DateTime SessionDateEastern;
            public Dictionary<double, long> VolumeAtPrice;
            public double Poc;
            public double Val;
            public double Vah;
            public long MaximumVolume;
        }

        private TimeZoneInfo platformZone;
        private TimeZoneInfo easternZone;
        private DateTime collectingSessionDateEastern = DateTime.MinValue;
        private Dictionary<double, long> collectingVolumeAtPrice;
        private SessionProfile previousSessionProfile;
        private int longBreakoutBar = -1;
        private int shortBreakoutBar = -1;
        private double longBreakoutBaselineVolume;
        private double shortBreakoutBaselineVolume;
        private double longBreakoutPeakVolume;
        private double shortBreakoutPeakVolume;
        private bool longVolumeFaded;
        private bool shortVolumeFaded;
        private bool configurationWarningShown;
        private bool isConfiguredTimeframeValid = true;
        private bool isConfiguredInstrumentValid = true;
        private bool maxAccountLimitHit;
        private bool dailyProfitLimitHit;
        private DateTime dailyProfitDate = DateTime.MinValue;
        private double dailyProfitStartBalance;
        private readonly ValueAreaExpansionState expansionState = new ValueAreaExpansionState();

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Previous CME ETH session volume profile with reversal or optional expansion entries. Uses actual 1-tick Last-trade volume.";
                Name = "OrderFlow";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                BarsRequiredToTrade = 20;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;

                Contracts = 1;
                ValueAreaPercent = 70;
                ReturnToValueAreaBars = 100;
                BaselineVolumeLookbackBars = 20;
                StrongVolumeMultiplier = 2.00;
                EnableEngulfingReclaimEntry = true;
                EntryModeSetting = ValueAreaEntryMode.ReversalOnly;
                ExpansionClosesOutsideValue = 3;
                ExpansionProfitTargetPoints = 40.0;
                ExpansionStopLossPoints = 20.0;
                ProfileWidthMinutes = 180;
                DrawProfile = true;
                DrawSignalMarkers = true;
                EnableDiagnosticLogging = true;
                EnableOrderSubmission = false;
                StopLossPoints = 20.0;
                ProfitTargetPoints = 40.0;
                TakeProfitModeSetting = TakeProfitMode.FixedPoints;
                StopLossModeSetting = StopLossMode.FixedPoints;
                SwingWickLookbackBars = 20;
                MaxAccountBalance = 0.0;
                MaxDailyProfit = 0.0;
            }
            else if (State == State.Configure)
            {
                // The secondary series supplies the volume-at-price data.  This
                // deliberately avoids estimating a profile from 15-minute OHLC.
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                expansionState.Reset();
                SetupTimeZones();
                isConfiguredTimeframeValid = ValidateRequiredPrimaryTimeframe();
                isConfiguredInstrumentValid = ValidateRequiredPrimaryInstrument();
                AddChartIndicator(VOL());
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                CollectLastTradeVolume();
                return;
            }

            if (BarsInProgress != 0 || CurrentBar < BarsRequiredToTrade)
                return;

            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid || !IsEntryConfigurationValid())
            {
                ExitAllPositions(InvalidConfigurationExitSignal);
                ShowConfigurationWarningOnce();
                return;
            }

            RefreshDailyProfitBaseline();
            if (CheckAccountGuards())
                return;

            if (previousSessionProfile == null)
            {
                expansionState.Reset();
                return;
            }

            DrawPreviousSessionProfile();
            if (EntryModeSetting != ValueAreaEntryMode.ReversalOnly)
                EvaluateExpansions();
            else
                expansionState.Reset();
            if (EntryModeSetting != ValueAreaEntryMode.ExpansionOnly)
            {
                ExpireStaleBreakouts();
                EvaluateReclaims();
                RecordNewBreakouts();
            }
            else
                ClearBreakouts();
        }

        private void CollectLastTradeVolume()
        {
            if (CurrentBars[1] < 0 || Volumes[1][0] <= 0)
                return;

            DateTime easternTime = ConvertToEastern(Times[1][0]);
            DateTime sessionDate = GetSessionDate(easternTime);
            if (collectingSessionDateEastern != sessionDate)
            {
                if (collectingVolumeAtPrice != null && collectingVolumeAtPrice.Count > 0)
                {
                    // The prior profile was active until this new ETH session
                    // began.  Retain it as subdued context before promoting
                    // the newly completed profile to the active colors.
                    if (previousSessionProfile != null)
                        DrawSessionProfile(previousSessionProfile, ConvertFromEastern(sessionDate.AddHours(18)), false);
                    previousSessionProfile = BuildSessionProfile(collectingSessionDateEastern, collectingVolumeAtPrice);
                    Print(string.Format(CultureInfo.InvariantCulture,
                        "OrderFlow|PROFILE_READY|session={0:yyyy-MM-dd}|POC={1:F2}|VAL={2:F2}|VAH={3:F2}|area={4}%",
                        previousSessionProfile.SessionDateEastern, previousSessionProfile.Poc,
                        previousSessionProfile.Val, previousSessionProfile.Vah, ValueAreaPercent));
                }

                collectingSessionDateEastern = sessionDate;
                collectingVolumeAtPrice = new Dictionary<double, long>();
                ClearBreakouts();
                expansionState.Reset();
            }

            double price = RoundToTick(Closes[1][0]);
            long existingVolume;
            collectingVolumeAtPrice.TryGetValue(price, out existingVolume);
            collectingVolumeAtPrice[price] = existingVolume + (long)Volumes[1][0];
        }

        private SessionProfile BuildSessionProfile(DateTime sessionDate, Dictionary<double, long> volumeAtPrice)
        {
            var prices = new List<double>(volumeAtPrice.Keys);
            prices.Sort();
            int pocIndex = 0;
            long totalVolume = 0;
            long maximumVolume = 0;
            for (int index = 0; index < prices.Count; index++)
            {
                long levelVolume = volumeAtPrice[prices[index]];
                totalVolume += levelVolume;
                if (levelVolume > maximumVolume || (levelVolume == maximumVolume && prices[index] < prices[pocIndex]))
                {
                    pocIndex = index;
                    maximumVolume = levelVolume;
                }
            }

            int lowIndex = pocIndex;
            int highIndex = pocIndex;
            long includedVolume = maximumVolume;
            while (includedVolume * 100L < totalVolume * ValueAreaPercent && (lowIndex > 0 || highIndex < prices.Count - 1))
            {
                long below = lowIndex > 0 ? volumeAtPrice[prices[lowIndex - 1]] : -1;
                long above = highIndex < prices.Count - 1 ? volumeAtPrice[prices[highIndex + 1]] : -1;
                if (above > below)
                {
                    highIndex++;
                    includedVolume += above;
                }
                else
                {
                    lowIndex--;
                    includedVolume += below;
                }
            }

            return new SessionProfile
            {
                SessionDateEastern = sessionDate,
                VolumeAtPrice = new Dictionary<double, long>(volumeAtPrice),
                Poc = prices[pocIndex],
                Val = prices[lowIndex],
                Vah = prices[highIndex],
                MaximumVolume = maximumVolume
            };
        }

        private void DrawPreviousSessionProfile()
        {
            if (previousSessionProfile == null)
                return;
            DrawSessionProfile(previousSessionProfile, Time[0], true);
        }

        private void DrawSessionProfile(SessionProfile profile, DateTime lineEnd, bool isActive)
        {
            if (!DrawProfile || profile == null || profile.MaximumVolume <= 0)
                return;

            string sessionTag = profile.SessionDateEastern.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            DateTime profileStart = ConvertFromEastern(profile.SessionDateEastern.AddHours(18));
            foreach (KeyValuePair<double, long> level in profile.VolumeAtPrice)
            {
                double width = ProfileWidthMinutes * ((double)level.Value / profile.MaximumVolume);
                DateTime profileEnd = profileStart.AddMinutes(Math.Max(1.0, width));
                Brush brush = !isActive ? InactiveSessionBrush
                    : (level.Key == profile.Poc
                        ? Brushes.Gold
                        : (level.Key >= profile.Val && level.Key <= profile.Vah ? Brushes.Teal : Brushes.DimGray));
                Draw.Line(this, StrategySignalPrefix + "Profile_" + sessionTag + "_" + level.Key.ToString("F2", CultureInfo.InvariantCulture),
                    false, profileStart, level.Key, profileEnd, level.Key, brush, DashStyleHelper.Solid, 2);
            }

            DateTime lineStart = profileStart;
            Brush vahBrush = isActive ? Brushes.MediumTurquoise : InactiveSessionBrush;
            Brush valBrush = isActive ? Brushes.IndianRed : InactiveSessionBrush;
            Brush pocBrush = isActive ? Brushes.Gold : InactiveSessionBrush;
            Draw.Line(this, StrategySignalPrefix + "VAH_" + sessionTag, false, lineStart, profile.Vah, lineEnd, profile.Vah, vahBrush, DashStyleHelper.Dash, isActive ? 2 : 1);
            Draw.Line(this, StrategySignalPrefix + "VAL_" + sessionTag, false, lineStart, profile.Val, lineEnd, profile.Val, valBrush, DashStyleHelper.Dash, isActive ? 2 : 1);
            Draw.Line(this, StrategySignalPrefix + "POC_" + sessionTag, false, lineStart, profile.Poc, lineEnd, profile.Poc, pocBrush, DashStyleHelper.Dot, 1);
            Draw.Text(this, StrategySignalPrefix + "VAHLabel_" + sessionTag, "VAH", 0, profile.Vah + 2 * TickSize, vahBrush);
            Draw.Text(this, StrategySignalPrefix + "VALLabel_" + sessionTag, "VAL", 0, profile.Val - 2 * TickSize, valBrush);
        }

        private static Brush CreateFrozenBrush(byte alpha, byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        private void RecordNewBreakouts()
        {
            if (Close[0] < previousSessionProfile.Val)
            {
                // A fresh lower break supersedes any older upper-break setup.
                if (longBreakoutBar < 0)
                {
                    ClearShortBreakout();
                    ArmBreakout(true);
                }
                UpdateBreakoutVolume(true);
            }
            else if (Close[0] > previousSessionProfile.Vah)
            {
                // A fresh upper break supersedes any older lower-break setup.
                if (shortBreakoutBar < 0)
                {
                    ClearLongBreakout();
                    ArmBreakout(false);
                }
                UpdateBreakoutVolume(false);
            }
        }

        private void EvaluateReclaims()
        {
            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            bool longReturnedToValue = longBreakoutBar >= 0 && CurrentBar > longBreakoutBar
                && Close[0] >= previousSessionProfile.Val && Close[0] <= previousSessionProfile.Vah;
            bool shortReturnedToValue = shortBreakoutBar >= 0 && CurrentBar > shortBreakoutBar
                && Close[0] <= previousSessionProfile.Vah && Close[0] >= previousSessionProfile.Val;

            if (longReturnedToValue)
            {
                bool qualifies = (EnableEngulfingReclaimEntry && IsBullishEngulfing()) || (longVolumeFaded && IsStrongVolume(longBreakoutBaselineVolume));
                LogReturn("Long", longVolumeFaded, longBreakoutBaselineVolume, qualifies);
                if (qualifies)
                    SubmitSignal(true);
                else
                    ClearLongBreakout();
                return;
            }

            if (shortReturnedToValue)
            {
                bool qualifies = (EnableEngulfingReclaimEntry && IsBearishEngulfing()) || (shortVolumeFaded && IsStrongVolume(shortBreakoutBaselineVolume));
                LogReturn("Short", shortVolumeFaded, shortBreakoutBaselineVolume, qualifies);
                if (qualifies)
                    SubmitSignal(false);
                else
                    ClearShortBreakout();
            }
        }

        private void ArmBreakout(bool isLong)
        {
            double baseline = GetBaselineVolume();
            if (baseline <= 0)
                return;

            if (isLong)
            {
                longBreakoutBar = CurrentBar;
                longBreakoutBaselineVolume = baseline;
                longBreakoutPeakVolume = Volume[0];
                longVolumeFaded = false;
            }
            else
            {
                shortBreakoutBar = CurrentBar;
                shortBreakoutBaselineVolume = baseline;
                shortBreakoutPeakVolume = Volume[0];
                shortVolumeFaded = false;
            }

            Log(string.Format(CultureInfo.InvariantCulture,
                "ARM|direction={0}|bar={1}|close={2:F2}|volume={3}|baseline={4:F0}",
                isLong ? "Long" : "Short", CurrentBar, Close[0], Volume[0], baseline));
        }

        private void UpdateBreakoutVolume(bool isLong)
        {
            if (isLong)
            {
                if (Volume[0] < longBreakoutPeakVolume)
                {
                    if (!longVolumeFaded)
                        Log(string.Format(CultureInfo.InvariantCulture,
                            "FADE|direction=Long|bar={0}|volume={1}|peak={2}", CurrentBar, Volume[0], longBreakoutPeakVolume));
                    longVolumeFaded = true;
                }
                longBreakoutPeakVolume = Math.Max(longBreakoutPeakVolume, Volume[0]);
            }
            else
            {
                if (Volume[0] < shortBreakoutPeakVolume)
                {
                    if (!shortVolumeFaded)
                        Log(string.Format(CultureInfo.InvariantCulture,
                            "FADE|direction=Short|bar={0}|volume={1}|peak={2}", CurrentBar, Volume[0], shortBreakoutPeakVolume));
                    shortVolumeFaded = true;
                }
                shortBreakoutPeakVolume = Math.Max(shortBreakoutPeakVolume, Volume[0]);
            }
        }

        private bool IsBullishEngulfing()
        {
            return CurrentBar > 0 && Close[0] > Open[0] && Close[1] < Open[1]
                && Open[0] <= Close[1] && Close[0] >= Open[1];
        }

        private bool IsBearishEngulfing()
        {
            return CurrentBar > 0 && Close[0] < Open[0] && Close[1] > Open[1]
                && Open[0] >= Close[1] && Close[0] <= Open[1];
        }

        private void EvaluateExpansions()
        {
            DateTime easternBarStart = ConvertToEastern(Time[0]).AddMinutes(-15);
            if (GetSessionDate(easternBarStart) != collectingSessionDateEastern)
            {
                expansionState.Reset();
                return;
            }
            DateTime utcClose = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(Time[0], DateTimeKind.Unspecified), platformZone);
            long barSlot = utcClose.Ticks / TimeSpan.FromMinutes(15).Ticks;
            int direction = expansionState.Observe(barSlot, (decimal)Close[0],
                (decimal)previousSessionProfile.Val, (decimal)previousSessionProfile.Vah,
                ExpansionClosesOutsideValue);
            if (expansionState.OutsideSide != 0)
                Log(string.Format(CultureInfo.InvariantCulture,
                    "EXPANSION|direction={0}|bar={1}|closes={2}|minimum={3}|close={4:F2}|VAL={5:F2}|VAH={6:F2}",
                    expansionState.OutsideSide > 0 ? "Long" : "Short", CurrentBar,
                    expansionState.OutsideCloses, ExpansionClosesOutsideValue, Close[0],
                    previousSessionProfile.Val, previousSessionProfile.Vah));
            if (direction == 0)
                return;
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                Log("EXPANSION_VETO|reason=position-not-flat; confirmed signal consumed, not queued");
                return;
            }
            SubmitSignal(direction > 0, true);
        }

        private void SubmitSignal(bool isLong, bool isExpansion = false)
        {
            string direction = isLong ? "Long" : "Short";
            if (DrawSignalMarkers)
            {
                string tag = StrategySignalPrefix + (isExpansion ? "Expansion_" : "Reclaim_") + CurrentBar + "_" + direction;
                if (isLong)
                    Draw.ArrowUp(this, tag, false, 0, Low[0] - 2 * TickSize, Brushes.LimeGreen);
                else
                    Draw.ArrowDown(this, tag, false, 0, High[0] + 2 * TickSize, Brushes.IndianRed);
            }

            if (isExpansion)
                Print(string.Format(CultureInfo.InvariantCulture,
                    "OrderFlow|SIGNAL|entry=Expansion|direction={0}|close={1:F2}|closes={2}|TP={3:F2}|SL={4:F2}|mode={5}",
                    direction, Close[0], expansionState.OutsideCloses,
                    ExpansionProfitTargetPoints, ExpansionStopLossPoints,
                    EnableOrderSubmission ? "orders-enabled" : "visual-only"));
            else
                Print(string.Format(CultureInfo.InvariantCulture,
                    "OrderFlow|SIGNAL|direction={0}|close={1:F2}|volume={2}|mode={3}",
                    direction, Close[0], Volume[0], EnableOrderSubmission ? "orders-enabled" : "visual-only"));
            ClearBreakouts();
            if (!EnableOrderSubmission)
                return;

            string entrySignal = isLong ? LongEntrySignal : ShortEntrySignal;
            if (isExpansion)
            {
                SetStopLoss(entrySignal, CalculationMode.Ticks, PointsToTicks(ExpansionStopLossPoints), false);
                SetProfitTarget(entrySignal, CalculationMode.Ticks, PointsToTicks(ExpansionProfitTargetPoints));
            }
            else
                ConfigureBrackets(isLong, entrySignal);
            if (isLong)
                EnterLong(Contracts, LongEntrySignal);
            else
                EnterShort(Contracts, ShortEntrySignal);
        }

        private void ConfigureBrackets(bool isLong, string entrySignal)
        {
            if (StopLossModeSetting == StopLossMode.SignalBarOppositeWick)
            {
                // The signal bar is the reclaim bar. Its opposite wick is a
                // natural structural invalidation point for this setup.
                double stopPrice = isLong ? Low[0] : High[0];
                SetStopLoss(entrySignal, CalculationMode.Price, stopPrice, false);
            }
            else
            {
                SetStopLoss(entrySignal, CalculationMode.Ticks, PointsToTicks(StopLossPoints), false);
            }

            double targetPrice;
            if (TakeProfitModeSetting == TakeProfitMode.OppositeValueArea)
            {
                targetPrice = isLong ? previousSessionProfile.Vah : previousSessionProfile.Val;
                SetProfitTarget(entrySignal, CalculationMode.Price, targetPrice);
                return;
            }

            if (TakeProfitModeSetting == TakeProfitMode.PointOfControl)
            {
                SetProfitTarget(entrySignal, CalculationMode.Price, previousSessionProfile.Poc);
                return;
            }

            if (TakeProfitModeSetting == TakeProfitMode.PreviousSwingWick
                && TryGetPreviousSwingWick(isLong, out targetPrice))
            {
                SetProfitTarget(entrySignal, CalculationMode.Price, targetPrice);
                return;
            }

            // A swing wick must be in the profitable direction. If no
            // confirmed wick is available, retain a valid protective target.
            if (TakeProfitModeSetting == TakeProfitMode.PreviousSwingWick)
                Log("TARGET_FALLBACK|reason=no_prior_favorable_swing_wick");
            SetProfitTarget(entrySignal, CalculationMode.Ticks, PointsToTicks(ProfitTargetPoints));
        }

        private bool TryGetPreviousSwingWick(bool isLong, out double targetPrice)
        {
            int maximumBarsAgo = Math.Min(SwingWickLookbackBars, CurrentBar - SwingStrength);
            for (int barsAgo = SwingStrength + 1; barsAgo <= maximumBarsAgo; barsAgo++)
            {
                if (isLong && IsConfirmedSwingHigh(barsAgo) && High[barsAgo] > Close[0] + TickSize)
                {
                    targetPrice = High[barsAgo];
                    return true;
                }
                if (!isLong && IsConfirmedSwingLow(barsAgo) && Low[barsAgo] < Close[0] - TickSize)
                {
                    targetPrice = Low[barsAgo];
                    return true;
                }
            }
            targetPrice = 0;
            return false;
        }

        private bool IsConfirmedSwingHigh(int barsAgo)
        {
            for (int offset = 1; offset <= SwingStrength; offset++)
            {
                if (High[barsAgo] <= High[barsAgo - offset] || High[barsAgo] < High[barsAgo + offset])
                    return false;
            }
            return true;
        }

        private bool IsConfirmedSwingLow(int barsAgo)
        {
            for (int offset = 1; offset <= SwingStrength; offset++)
            {
                if (Low[barsAgo] >= Low[barsAgo - offset] || Low[barsAgo] > Low[barsAgo + offset])
                    return false;
            }
            return true;
        }

        private void ExpireStaleBreakouts()
        {
            if (longBreakoutBar >= 0 && CurrentBar - longBreakoutBar > ReturnToValueAreaBars)
                ClearLongBreakout();
            if (shortBreakoutBar >= 0 && CurrentBar - shortBreakoutBar > ReturnToValueAreaBars)
                ClearShortBreakout();
        }

        private void ClearBreakouts()
        {
            ClearLongBreakout();
            ClearShortBreakout();
        }

        private void ClearLongBreakout()
        {
            longBreakoutBar = -1;
            longBreakoutBaselineVolume = 0;
            longBreakoutPeakVolume = 0;
            longVolumeFaded = false;
        }

        private void ClearShortBreakout()
        {
            shortBreakoutBar = -1;
            shortBreakoutBaselineVolume = 0;
            shortBreakoutPeakVolume = 0;
            shortVolumeFaded = false;
        }

        private double GetBaselineVolume()
        {
            if (CurrentBar < BaselineVolumeLookbackBars)
                return 0;
            var volumes = new List<double>();
            for (int barsAgo = 1; barsAgo <= BaselineVolumeLookbackBars; barsAgo++)
                volumes.Add(Volume[barsAgo]);
            volumes.Sort();
            int middle = volumes.Count / 2;
            return volumes.Count % 2 == 0
                ? (volumes[middle - 1] + volumes[middle]) / 2.0
                : volumes[middle];
        }

        private bool IsStrongVolume(double baselineVolume)
        {
            if (baselineVolume <= 0)
                return false;
            return Volume[0] >= baselineVolume * StrongVolumeMultiplier;
        }

        private void LogReturn(string direction, bool volumeFaded, double baselineVolume, bool qualifies)
        {
            Log(string.Format(CultureInfo.InvariantCulture,
                "RETURN|direction={0}|bar={1}|volume={2}|baseline={3:F0}|required={4:F0}|faded={5}|qualified={6}",
                direction, CurrentBar, Volume[0], baselineVolume, baselineVolume * StrongVolumeMultiplier,
                volumeFaded, qualifies));
        }

        private void Log(string message)
        {
            if (EnableDiagnosticLogging)
                Print(StrategySignalPrefix + "|" + message);
        }

        private DateTime GetSessionDate(DateTime easternTime)
        {
            return easternTime.TimeOfDay >= TimeSpan.FromHours(18) ? easternTime.Date : easternTime.Date.AddDays(-1);
        }

        private void SetupTimeZones()
        {
            platformZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
            easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }

        private DateTime ConvertToEastern(DateTime platformTime)
        {
            if (platformZone == null || easternZone == null || platformZone.Equals(easternZone))
                return platformTime;
            return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(platformTime, DateTimeKind.Unspecified), platformZone, easternZone);
        }

        private DateTime ConvertFromEastern(DateTime easternTime)
        {
            if (platformZone == null || easternZone == null || platformZone.Equals(easternZone))
                return easternTime;
            return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(easternTime, DateTimeKind.Unspecified), easternZone, platformZone);
        }

        private bool ValidateRequiredPrimaryTimeframe()
        {
            return BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == 15;
        }

        private bool ValidateRequiredPrimaryInstrument()
        {
            string name = Instrument == null || Instrument.MasterInstrument == null ? string.Empty : Instrument.MasterInstrument.Name;
            return string.Equals(name, "NQ", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "MNQ", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsEntryConfigurationValid()
        {
            if (!Enum.IsDefined(typeof(ValueAreaEntryMode), EntryModeSetting))
                return false;
            if (EntryModeSetting == ValueAreaEntryMode.ReversalOnly)
                return true;
            return ExpansionClosesOutsideValue >= 1
                && !double.IsNaN(ExpansionProfitTargetPoints) && !double.IsInfinity(ExpansionProfitTargetPoints)
                && !double.IsNaN(ExpansionStopLossPoints) && !double.IsInfinity(ExpansionStopLossPoints)
                && ExpansionProfitTargetPoints >= TickSize && ExpansionProfitTargetPoints <= 1000
                && ExpansionStopLossPoints >= TickSize && ExpansionStopLossPoints <= 1000;
        }

        private void ShowConfigurationWarningOnce()
        {
            if (configurationWarningShown)
                return;
            configurationWarningShown = true;
            Print(!isConfiguredTimeframeValid || !isConfiguredInstrumentValid
                ? "OrderFlow is disabled: use an NQ or MNQ 15-minute chart."
                : "OrderFlow is disabled: choose a valid entry mode, at least one expansion close, and positive expansion TP/SL points.");
        }

        private int PointsToTicks(double points)
        {
            return (int)Math.Round(points / TickSize, MidpointRounding.AwayFromZero);
        }

        private double RoundToTick(double price)
        {
            return Math.Round(price / TickSize, MidpointRounding.AwayFromZero) * TickSize;
        }

        private void RefreshDailyProfitBaseline()
        {
            if (MaxDailyProfit <= 0)
            {
                dailyProfitLimitHit = false;
                dailyProfitDate = DateTime.MinValue;
                return;
            }
            if (dailyProfitDate == Time[0].Date)
                return;
            dailyProfitDate = Time[0].Date;
            dailyProfitStartBalance = GetNetLiquidation();
            dailyProfitLimitHit = false;
        }

        private bool CheckAccountGuards()
        {
            double balance = GetNetLiquidation();
            if (MaxAccountBalance > 0 && balance >= MaxAccountBalance)
            {
                maxAccountLimitHit = true;
                ExitAllPositions(MaxBalanceExitSignal);
            }
            if (MaxDailyProfit > 0 && !dailyProfitLimitHit && balance - dailyProfitStartBalance >= MaxDailyProfit)
            {
                dailyProfitLimitHit = true;
                ExitAllPositions(MaxDailyProfitExitSignal);
            }
            return maxAccountLimitHit || dailyProfitLimitHit;
        }

        private double GetNetLiquidation()
        {
            try
            {
                return Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
            }
            catch (Exception)
            {
                return Account.Get(AccountItem.CashValue, Currency.UsDollar)
                    + Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
            }
        }

        private void ExitAllPositions(string exitSignal)
        {
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(exitSignal, LongEntrySignal);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(exitSignal, ShortEntrySignal);
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "Orders", Order = 0)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable order submission", Description = "Off by default for chart validation. Turn on only after visual and Playback review.", GroupName = "Orders", Order = 1)]
        public bool EnableOrderSubmission { get; set; }

        [NinjaScriptProperty]
        [Range(50, 95)]
        [Display(Name = "Value area percent", GroupName = "Profile", Order = 10)]
        public int ValueAreaPercent { get; set; }

        [NinjaScriptProperty]
        [Range(15, 720)]
        [Display(Name = "Profile width minutes", Description = "Maximum horizontal width of the drawn prior-session profile.", GroupName = "Profile", Order = 11)]
        public int ProfileWidthMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw profile", GroupName = "Profile", Order = 12)]
        public bool DrawProfile { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry style", Description = "ReversalOnly preserves reclaim entries. ExpansionOnly follows consecutive closes outside value. Both allows either while flat.", GroupName = "Signal", Order = 19)]
        public ValueAreaEntryMode EntryModeSetting { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Return to value area within bars", GroupName = "Signal", Order = 20)]
        public int ReturnToValueAreaBars { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Baseline volume bars", Description = "Bars used to calculate the robust median volume baseline before a breakout.", GroupName = "Signal", Order = 21)]
        public int BaselineVolumeLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 10.0)]
        [Display(Name = "Strong volume multiplier", Description = "The reclaim bar volume must be at least this multiple of the preceding volume average.", GroupName = "Signal", Order = 22)]
        public double StrongVolumeMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable engulfing reclaim entry", Description = "Allows a bullish or bearish body engulfing candle that closes back inside the value area to enter without volume confirmation.", GroupName = "Signal", Order = 23)]
        public bool EnableEngulfingReclaimEntry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw signal markers", GroupName = "Signal", Order = 24)]
        public bool DrawSignalMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable diagnostic logging", Description = "Writes breakout, fade, and return qualification details to NinjaScript Output.", GroupName = "Signal", Order = 25)]
        public bool EnableDiagnosticLogging { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Minimum consecutive outside closes", Description = "Includes the first close beyond VAH/VAL. An inside or boundary close resets the run. One expansion signal per continuous directional run.", GroupName = "Expansion", Order = 0)]
        public int ExpansionClosesOutsideValue { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 1000)]
        [Display(Name = "Expansion profit target points", Description = "Fixed points from actual fill; independent of the reversal target mode.", GroupName = "Expansion", Order = 1)]
        public double ExpansionProfitTargetPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 1000)]
        [Display(Name = "Expansion stop loss points", Description = "Fixed points from actual fill; independent of the reversal stop mode.", GroupName = "Expansion", Order = 2)]
        public double ExpansionStopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 1000)]
        [Display(Name = "Stop loss points", GroupName = "Orders", Order = 30)]
        public double StopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.25, 1000)]
        [Display(Name = "Profit target points", GroupName = "Orders", Order = 31)]
        public double ProfitTargetPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop loss mode", Description = "Signal bar opposite wick uses the reclaim candle's low for longs or high for shorts.", GroupName = "Orders", Order = 32)]
        public StopLossMode StopLossModeSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profit target mode", Description = "Opposite value area uses VAH for longs and VAL for shorts. Point of control uses the prior profile POC. Previous swing wick uses the latest confirmed favorable wick.", GroupName = "Orders", Order = 33)]
        public TakeProfitMode TakeProfitModeSetting { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Previous swing wick lookback bars", Description = "Search depth for a previous favorable swing wick target.", GroupName = "Orders", Order = 34)]
        public int SwingWickLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max account balance", GroupName = "Safety", Order = 40)]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max daily profit", GroupName = "Safety", Order = 41)]
        public double MaxDailyProfit { get; set; }
    }
}
