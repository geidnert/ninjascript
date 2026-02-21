#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class DuoTesting : Strategy
    {
        public DuoTesting()
        {
            VendorLicense(337);
        }

        public enum InitialStopMode
        {
            WickExtreme
        }

        public enum SessionTradeDirection
        {
            Both,
            LongOnly,
            ShortOnly
        }

        // Commercial (closed list) option: uncomment these converters and the [TypeConverter(...)] lines
        // on the momentum properties to switch back from free input to dropdown presets.
        // private sealed class AsiaAdxSlopeDropdownConverter : System.ComponentModel.DoubleConverter
        // {
        //     private static readonly double[] Presets = new double[]
        //     {
        //         1.33, 1.34, 1.35, 1.36, 1.37, 1.48, 1.49, 1.50, 1.51, 1.52, 1.53, 1.54, 1.55
        //     };
        //
        //     public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        //     {
        //         return true;
        //     }
        //
        //     public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        //     {
        //         return true;
        //     }
        //
        //     public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        //     {
        //         return new TypeConverter.StandardValuesCollection(Presets);
        //     }
        // }
        //
        // private sealed class NewYorkAdxSlopeDropdownConverter : System.ComponentModel.DoubleConverter
        // {
        //     private static readonly double[] Presets = new double[]
        //     {
        //         1.55, 1.56, 1.57, 1.58, 1.59, 1.60, 1.61, 1.62, 1.63, 1.64, 1.65
        //     };
        //
        //     public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        //     {
        //         return true;
        //     }
        //
        //     public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        //     {
        //         return true;
        //     }
        //
        //     public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        //     {
        //         return new TypeConverter.StandardValuesCollection(Presets);
        //     }
        // }

        private enum SessionSlot
        {
            None,
            Asia,
            NewYork
        }

        public enum WebhookProvider
        {
            TradersPost,
            ProjectX
        }

        private Order longEntryOrder;
        private Order shortEntryOrder;
        private int missingLongEntryOrderBars;
        private int missingShortEntryOrderBars;
        private double asiaAdxMinSlopePoints;
        private double newYorkAdxMinSlopePoints;

        private bool asiaSessionClosed;
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
        private EMA emaNewYork;
        private EMA activeEma;
        private DM adxAsia;
        private DM adxNewYork;
        private DM activeAdx;

        private int activeEmaPeriod;
        private int activeContracts;
        private SessionTradeDirection activeTradeDirection = SessionTradeDirection.Both;
        private InitialStopMode activeEntryStopMode;
        private double activeEmaMinSlopePointsPerBar;
        private double activeMaxEntryDistanceFromEmaPoints;
        private double activeExitCrossPoints;
        private double activeTakeProfitPoints;
        private int activeAdxPeriod;
        private double activeAdxThreshold;
        private double activeFlipAdxThreshold;
        private double activeAdxMaxThreshold;
        private double activeAdxMinSlopePoints;
        private double activeAdxPeakDrawdownExitUnits;
        private double activeAdxAbsoluteExitLevel;
        private double activeStopPaddingPoints;

        private double pendingLongStopForWebhook;
        private double pendingShortStopForWebhook;
        private double currentTradePeakAdx;
        private MarketPosition trackedAdxPeakPosition = MarketPosition.Flat;
        private string projectXSessionToken;
        private DateTime projectXTokenAcquiredUtc = Core.Globals.MinDate;
        private int? projectXLastOrderId;
        private string projectXLastOrderContractId;
        private bool accountBalanceLimitReached;
        private int accountBalanceLimitReachedBar = -1;
        private int asiaTradesThisSession;
        private int newYorkTradesThisSession;
        private string currentPositionEntrySignal = string.Empty;
        private const double FlipBodyThresholdPercent = 0.0;
        private const string LongEntrySignal = "LongEntry";
        private const string ShortEntrySignal = "ShortEntry";
        private const string LongFlipEntrySignal = "LongFlipEntry";
        private const string ShortFlipEntrySignal = "ShortFlipEntry";
        private static readonly Brush PassedNewsRowBrush = CreateFrozenBrush(30, 211, 211, 211);
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
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(210, 0xD3, 0xD3, 0xD3);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DuoTesting2";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                IsInstantiatedOnEachOptimizationIteration = false;
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;

                UseAsiaSession = true;
                AsiaSessionStart = new TimeSpan(18, 30, 0);
                AsiaSessionEnd = new TimeSpan(2, 00, 0);
                AsiaBlockSundayTrades = false;
                AsiaEmaPeriod = 21;
                AsiaContracts = 1;
                AsiaTradeDirection = SessionTradeDirection.Both;
                AsiaFlipAdxThreshold = 19.7;
                AsiaEmaMinSlopePointsPerBar = 0.5;
                AsiaMaxEntryDistanceFromEmaPoints = 21.0;
                AsiaAdxPeriod = 14;
                AsiaAdxThreshold = 19.7;
                AsiaAdxMaxThreshold = 43.6;
                AsiaAdxMinSlopePoints = 1.37;
                AsiaAdxPeakDrawdownExitUnits = 13.0;
                AsiaAdxAbsoluteExitLevel = 57.7;
                AsiaStopPaddingPoints = 63;
                AsiaExitCrossPoints = 3.5;
                AsiaTakeProfitPoints = 93.75;

                UseNewYorkSession = true;
                NewYorkSessionStart = new TimeSpan(9, 40, 0);
                NewYorkSessionEnd = new TimeSpan(13, 30, 0);
                NewYorkSkipStart = new TimeSpan(12, 00, 0);
                NewYorkSkipEnd = new TimeSpan(12, 25, 0);
                NewYorkEmaPeriod = 16;
                NewYorkContracts = 1;
                NewYorkTradeDirection = SessionTradeDirection.Both;
                NewYorkFlipAdxThreshold = 16;
                NewYorkEmaMinSlopePointsPerBar = 0.8;
                NewYorkMaxEntryDistanceFromEmaPoints = 44.0;
                NewYorkAdxPeriod = 14;
                NewYorkAdxThreshold = 16;
                NewYorkAdxMaxThreshold = 58.0;
                NewYorkAdxMinSlopePoints = 1.58;
                NewYorkAdxPeakDrawdownExitUnits = 19.6;
                NewYorkAdxAbsoluteExitLevel = 70.0;
                NewYorkStopPaddingPoints = 54.5;
                NewYorkExitCrossPoints = 1.25;
                NewYorkTakeProfitPoints = 123.75;

                CloseAtSessionEnd = false;
                AsiaSessionBrush = Brushes.DarkCyan;
                NewYorkSessionBrush = Brushes.Gold;
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
                HvSlPaddingPoints = 0.0;
                HvSlStartTime = new TimeSpan(9, 30, 0);
                HvSlEndTime = new TimeSpan(10, 0, 0);
                EntryOffsetPoints = 0.0;
                RequireEntryConfirmation = false;
                RequireMinAdxForFlips = true;

                DebugLogging = false;
            }
            else if (State == State.DataLoaded)
            {
                emaAsia = EMA(AsiaEmaPeriod);
                emaNewYork = EMA(NewYorkEmaPeriod);
                adxAsia = DM(AsiaAdxPeriod);
                adxNewYork = DM(NewYorkAdxPeriod);
                UpdateAdxReferenceLines(adxAsia, AsiaAdxThreshold, AsiaAdxMaxThreshold);
                UpdateAdxReferenceLines(adxNewYork, NewYorkAdxThreshold, NewYorkAdxMaxThreshold);

                if (ShowEmaOnChart)
                {
                    AddChartIndicator(emaAsia);
                    AddChartIndicator(emaNewYork);
                }

                if (ShowAdxOnChart)
                {
                    AddChartIndicator(adxAsia);
                    AddChartIndicator(adxNewYork);
                }

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
                projectXSessionToken = null;
                projectXTokenAcquiredUtc = Core.Globals.MinDate;
                projectXLastOrderId = null;
                projectXLastOrderContractId = null;
                accountBalanceLimitReached = false;
                accountBalanceLimitReachedBar = -1;
                asiaTradesThisSession = 0;
                newYorkTradesThisSession = 0;

                EnsureNewsDatesInitialized();

                LogDebug(
                    string.Format(
                        "DataLoaded | ActiveSession={0} EMA={1} ADX={2}/{3:0.##} Contracts={4} ExitCross={5:0.##} EntryStop={6}",
                        FormatSessionLabel(activeSession),
                        activeEmaPeriod,
                        activeAdxPeriod,
                        activeAdxThreshold,
                        activeContracts,
                        activeExitCrossPoints,
                        activeEntryStopMode));
            }
            else if (State == State.Terminated)
            {
                DisposeInfoBoxOverlay();
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
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("NewsSkip", GetOpenLongEntrySignal());
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("NewsSkip", GetOpenShortEntrySignal());
                LogDebug("Entered news block: canceling working entries and flattening open position.");
            }

            bool inActiveSessionNow = activeSession != SessionSlot.None && TimeInSession(activeSession, Time[0]);
            bool inNySkipNow = activeSession == SessionSlot.NewYork && IsNewYorkSkipTime(Time[0]);
            bool isAsiaSundayBlockedNow = activeSession == SessionSlot.Asia && AsiaBlockSundayTrades && Time[0].DayOfWeek == DayOfWeek.Sunday;
            bool accountBlocked = IsAccountBalanceBlocked();
            double adxValue = activeAdx != null ? activeAdx[0] : 0.0;
            UpdateAdxPeakTracker(adxValue);
            double adxSlope = GetAdxSlopePoints();
            bool adxMinPass = !inActiveSessionNow || activeAdxThreshold <= 0.0 || adxValue >= activeAdxThreshold;
            bool adxMaxPass = !inActiveSessionNow || activeAdxMaxThreshold <= 0.0 || adxValue <= activeAdxMaxThreshold;
            bool adxThresholdPass = adxMinPass && adxMaxPass;
            bool adxSlopePass = !inActiveSessionNow || activeAdxMinSlopePoints <= 0.0 || adxSlope >= activeAdxMinSlopePoints;
            bool adxPass = adxThresholdPass && adxSlopePass;
            bool canTradeNow = inActiveSessionNow && !inNewsSkipNow && !inNySkipNow && !isAsiaSundayBlockedNow && !accountBlocked && adxPass;
            bool flipAdxMinPass = !RequireMinAdxForFlips || !inActiveSessionNow || activeFlipAdxThreshold <= 0.0 || adxValue >= activeFlipAdxThreshold;
            bool canFlipNow = inActiveSessionNow && !inNewsSkipNow && !inNySkipNow && !isAsiaSundayBlockedNow && !accountBlocked && flipAdxMinPass;
            bool allowLong = activeTradeDirection != SessionTradeDirection.ShortOnly;
            bool allowShort = activeTradeDirection != SessionTradeDirection.LongOnly;

            if (!inActiveSessionNow)
                CancelWorkingEntryOrders();

            if (inNySkipNow)
                CancelWorkingEntryOrders();

            if (isAsiaSundayBlockedNow)
                CancelWorkingEntryOrders();

            if (activeEma == null || CurrentBar < activeEmaPeriod)
                return;

            double emaValue = activeEma[0];
            bool bullish = Close[0] > Open[0];
            bool bearish = Close[0] < Open[0];
            double bodyAbovePercent = GetBodyPercentAboveEma(Open[0], Close[0], emaValue);
            double bodyBelowPercent = GetBodyPercentBelowEma(Open[0], Close[0], emaValue);
            double emaDistancePoints = Math.Abs(Close[0] - emaValue);
            bool distancePasses = activeMaxEntryDistanceFromEmaPoints <= 0.0 || emaDistancePoints <= activeMaxEntryDistanceFromEmaPoints;
            bool emaSlopeLongPass = EmaSlopePassesLong();
            bool emaSlopeShortPass = EmaSlopePassesShort();
            bool longSignalRaw = bullish && bodyAbovePercent > 0.0;
            bool shortSignalRaw = bearish && bodyBelowPercent > 0.0;
            bool longSignal = longSignalRaw && allowLong;
            bool shortSignal = shortSignalRaw && allowShort;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (activeAdxAbsoluteExitLevel > 0.0 && adxValue >= activeAdxAbsoluteExitLevel)
                {
                    ExitLong("AdxLevelExit", GetOpenLongEntrySignal());
                    LogDebug(string.Format("Exit LONG | reason=AdxLevel adx={0:0.00} threshold={1:0.00}",
                        adxValue, activeAdxAbsoluteExitLevel));
                    return;
                }

                double adxDrawdown;
                if (ShouldExitOnAdxDrawdown(adxValue, out adxDrawdown))
                {
                    ExitLong("AdxDrawdownExit", GetOpenLongEntrySignal());
                    LogDebug(string.Format("Exit LONG | reason=AdxDrawdown adx={0:0.00} peak={1:0.00} drawdown={2:0.00} threshold={3:0.00}",
                        adxValue, currentTradePeakAdx, adxDrawdown, activeAdxPeakDrawdownExitUnits));
                    return;
                }

                if (activeTakeProfitPoints > 0.0 && Close[0] >= Position.AveragePrice + activeTakeProfitPoints)
                {
                    ExitLong("TakeProfitExit", GetOpenLongEntrySignal());
                    LogDebug(string.Format("Exit LONG | reason=TakeProfit close={0:0.00} avg={1:0.00} tpPts={2:0.00}",
                        Close[0], Position.AveragePrice, activeTakeProfitPoints));
                    return;
                }

                if (Close[0] <= emaValue - activeExitCrossPoints)
                {
                    if (DebugLogging && canTradeNow && !distancePasses && emaSlopeShortPass && bodyBelowPercent >= FlipBodyThresholdPercent)
                    {
                        LogDebug(string.Format(
                            "Flip blocked | reason=EmaDistance side=Short distancePts={0:0.00} maxPts={1:0.00} session={2}",
                            emaDistancePoints,
                            activeMaxEntryDistanceFromEmaPoints,
                            FormatSessionLabel(activeSession)));
                    }

                    bool flipCanTradePass = canFlipNow;
                    bool flipDirectionPass = allowShort;
                    bool flipDistancePass = distancePasses;
                    bool flipSlopePass = emaSlopeShortPass;
                    bool flipBodyPass = bodyBelowPercent >= FlipBodyThresholdPercent;
                    bool shouldFlip = flipCanTradePass && flipDirectionPass && flipDistancePass && flipSlopePass && flipBodyPass;
                    if (shouldFlip)
                    {
                        CancelOrderIfActive(longEntryOrder, "FlipToShort");
                        CancelOrderIfActive(shortEntryOrder, "FlipToShort");

                        bool useMarketEntry = true;
                        double entryPrice = GetEntryPriceForDirection(Close[0], false, 0.0);
                        double stopPrice = BuildFlipShortStopPrice(entryPrice, emaValue, Time[0]);
                        int qty = GetEntryQuantity();
                        if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", entryPrice, qty))
                        {
                            LogDebug("Entry confirmation declined | Flip LONG->SHORT.");
                            return;
                        }
                        pendingShortStopForWebhook = stopPrice;
                        SetStopLossByDistanceTicks(ShortFlipEntrySignal, entryPrice, stopPrice);
                        SetProfitTargetByDistanceTicks(ShortFlipEntrySignal, activeTakeProfitPoints);
                        SendFlipWebhooks("sell", entryPrice, stopPrice, useMarketEntry, qty, MarketPosition.Long);
                        StartTradeLines(entryPrice, stopPrice, activeTakeProfitPoints > 0.0 ? entryPrice - activeTakeProfitPoints : 0.0, activeTakeProfitPoints > 0.0);
                        SubmitShortEntryOrder(qty, entryPrice, useMarketEntry, ShortFlipEntrySignal);

                        LogDebug(string.Format(
                            "Flip LONG->SHORT | close={0:0.00} entry={1:0.00} type={2} ema={3:0.00} below%={4:0.0} stop={5:0.00} stopTicks={6} qty={7}",
                            Close[0],
                            entryPrice,
                            useMarketEntry ? "market" : "limit",
                            emaValue,
                            bodyBelowPercent,
                            stopPrice,
                            PriceToTicks(Math.Abs(entryPrice - stopPrice)),
                            qty));
                    }
                    else
                    {
                        if (DebugLogging)
                        {
                            LogDebug(string.Format(
                                "Flip skipped | side=Short canTrade={0} adxMinForFlipPass={1} directionPass={2} distancePass={3} emaSlopePass={4} bodyPass={5} below%={6:0.0} minBody%={7:0.0}",
                                flipCanTradePass,
                                flipAdxMinPass,
                                flipDirectionPass,
                                flipDistancePass,
                                flipSlopePass,
                                flipBodyPass,
                                bodyBelowPercent,
                                FlipBodyThresholdPercent));
                        }
                        ExitLong("EmaExitLong", GetOpenLongEntrySignal());
                        LogDebug(string.Format("Exit LONG | close={0:0.00} ema={1:0.00} below%={2:0.0}", Close[0], emaValue, bodyBelowPercent));
                    }
                }

                return;
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                if (activeAdxAbsoluteExitLevel > 0.0 && adxValue >= activeAdxAbsoluteExitLevel)
                {
                    ExitShort("AdxLevelExit", GetOpenShortEntrySignal());
                    LogDebug(string.Format("Exit SHORT | reason=AdxLevel adx={0:0.00} threshold={1:0.00}",
                        adxValue, activeAdxAbsoluteExitLevel));
                    return;
                }

                double adxDrawdown;
                if (ShouldExitOnAdxDrawdown(adxValue, out adxDrawdown))
                {
                    ExitShort("AdxDrawdownExit", GetOpenShortEntrySignal());
                    LogDebug(string.Format("Exit SHORT | reason=AdxDrawdown adx={0:0.00} peak={1:0.00} drawdown={2:0.00} threshold={3:0.00}",
                        adxValue, currentTradePeakAdx, adxDrawdown, activeAdxPeakDrawdownExitUnits));
                    return;
                }

                if (activeTakeProfitPoints > 0.0 && Close[0] <= Position.AveragePrice - activeTakeProfitPoints)
                {
                    ExitShort("TakeProfitExit", GetOpenShortEntrySignal());
                    LogDebug(string.Format("Exit SHORT | reason=TakeProfit close={0:0.00} avg={1:0.00} tpPts={2:0.00}",
                        Close[0], Position.AveragePrice, activeTakeProfitPoints));
                    return;
                }

                if (Close[0] >= emaValue + activeExitCrossPoints)
                {
                    if (DebugLogging && canTradeNow && !distancePasses && emaSlopeLongPass && bodyAbovePercent >= FlipBodyThresholdPercent)
                    {
                        LogDebug(string.Format(
                            "Flip blocked | reason=EmaDistance side=Long distancePts={0:0.00} maxPts={1:0.00} session={2}",
                            emaDistancePoints,
                            activeMaxEntryDistanceFromEmaPoints,
                            FormatSessionLabel(activeSession)));
                    }

                    bool flipCanTradePass = canFlipNow;
                    bool flipDirectionPass = allowLong;
                    bool flipDistancePass = distancePasses;
                    bool flipSlopePass = emaSlopeLongPass;
                    bool flipBodyPass = bodyAbovePercent >= FlipBodyThresholdPercent;
                    bool shouldFlip = flipCanTradePass && flipDirectionPass && flipDistancePass && flipSlopePass && flipBodyPass;
                    if (shouldFlip)
                    {
                        CancelOrderIfActive(longEntryOrder, "FlipToLong");
                        CancelOrderIfActive(shortEntryOrder, "FlipToLong");

                        bool useMarketEntry = true;
                        double entryPrice = GetEntryPriceForDirection(Close[0], true, 0.0);
                        double stopPrice = BuildFlipLongStopPrice(entryPrice, emaValue, Time[0]);
                        int qty = GetEntryQuantity();
                        if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", entryPrice, qty))
                        {
                            LogDebug("Entry confirmation declined | Flip SHORT->LONG.");
                            return;
                        }
                        pendingLongStopForWebhook = stopPrice;
                        SetStopLossByDistanceTicks(LongFlipEntrySignal, entryPrice, stopPrice);
                        SetProfitTargetByDistanceTicks(LongFlipEntrySignal, activeTakeProfitPoints);
                        SendFlipWebhooks("buy", entryPrice, stopPrice, useMarketEntry, qty, MarketPosition.Short);
                        StartTradeLines(entryPrice, stopPrice, activeTakeProfitPoints > 0.0 ? entryPrice + activeTakeProfitPoints : 0.0, activeTakeProfitPoints > 0.0);
                        SubmitLongEntryOrder(qty, entryPrice, useMarketEntry, LongFlipEntrySignal);

                        LogDebug(string.Format(
                            "Flip SHORT->LONG | close={0:0.00} entry={1:0.00} type={2} ema={3:0.00} above%={4:0.0} stop={5:0.00} stopTicks={6} qty={7}",
                            Close[0],
                            entryPrice,
                            useMarketEntry ? "market" : "limit",
                            emaValue,
                            bodyAbovePercent,
                            stopPrice,
                            PriceToTicks(Math.Abs(entryPrice - stopPrice)),
                            qty));
                    }
                    else
                    {
                        if (DebugLogging)
                        {
                            LogDebug(string.Format(
                                "Flip skipped | side=Long canTrade={0} adxMinForFlipPass={1} directionPass={2} distancePass={3} emaSlopePass={4} bodyPass={5} above%={6:0.0} minBody%={7:0.0}",
                                flipCanTradePass,
                                flipAdxMinPass,
                                flipDirectionPass,
                                flipDistancePass,
                                flipSlopePass,
                                flipBodyPass,
                                bodyAbovePercent,
                                FlipBodyThresholdPercent));
                        }
                        ExitShort("EmaExitShort", GetOpenShortEntrySignal());
                        LogDebug(string.Format("Exit SHORT | close={0:0.00} ema={1:0.00} above%={2:0.0}", Close[0], emaValue, bodyAbovePercent));
                    }
                }

                return;
            }

            if (DebugLogging && Position.MarketPosition == MarketPosition.Flat)
            {
                if (longSignalRaw && !allowLong)
                {
                    LogDebug(string.Format(
                        "Setup blocked | side=Long close={0:0.00} ema={1:0.00} reasons=DirectionBlocked mode={2}",
                        Close[0],
                        emaValue,
                        activeTradeDirection));
                }
                else if (shortSignalRaw && !allowShort)
                {
                    LogDebug(string.Format(
                        "Setup blocked | side=Short close={0:0.00} ema={1:0.00} reasons=DirectionBlocked mode={2}",
                        Close[0],
                        emaValue,
                        activeTradeDirection));
                }
            }

            if (!canTradeNow)
            {
                if (DebugLogging && Position.MarketPosition == MarketPosition.Flat && (longSignal || shortSignal))
                {
                    string setupSide = longSignal ? "Long" : "Short";
                    var reasons = new List<string>();

                    if (!inActiveSessionNow)
                        reasons.Add(string.Format("OutOfSession active={0}", FormatSessionLabel(activeSession)));
                    if (inNewsSkipNow)
                        reasons.Add(string.Format("NewsSkip minutes={0}", NewsBlockMinutes));
                    if (inNySkipNow)
                        reasons.Add(string.Format("NewYorkSkip {0:hh\\:mm}-{1:hh\\:mm}", NewYorkSkipStart, NewYorkSkipEnd));
                    if (isAsiaSundayBlockedNow)
                        reasons.Add("AsiaSundayBlock");
                    if (accountBlocked)
                        reasons.Add(string.Format("AccountBlocked maxBalance={0:0.##}", MaxAccountBalance));
                    if (!adxMinPass)
                        reasons.Add(string.Format("AdxBelowMin adx={0:0.00} min={1:0.00}", adxValue, activeAdxThreshold));
                    if (!adxMaxPass)
                        reasons.Add(string.Format("AdxAboveMax adx={0:0.00} max={1:0.00}", adxValue, activeAdxMaxThreshold));
                    if (!adxSlopePass)
                        reasons.Add(string.Format("AdxSlopeBelowMin slope={0:0.00} min={1:0.00}", adxSlope, activeAdxMinSlopePoints));
                    if (reasons.Count == 0)
                        reasons.Add("UnknownGate");

                    LogDebug(string.Format(
                        "Setup blocked | side={0} close={1:0.00} ema={2:0.00} reasons={3}",
                        setupSide,
                        Close[0],
                        emaValue,
                        string.Join(" | ", reasons)));
                }
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
                    bool useMarketEntry = EntryOffsetPoints <= 0.0;
                    double entryPrice = GetEntryPriceForDirection(Close[0], true, EntryOffsetPoints);
                    double stopPrice = BuildLongEntryStopPrice(entryPrice, emaValue, Time[0]);
                    int qty = GetEntryQuantity();
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", entryPrice, qty))
                    {
                        LogDebug("Entry confirmation declined | LONG.");
                        EndTradeAttempt("confirmation-declined");
                        return;
                    }

                    pendingLongStopForWebhook = stopPrice;
                    SetStopLossByDistanceTicks(LongEntrySignal, entryPrice, stopPrice);
                    SetProfitTargetByDistanceTicks(LongEntrySignal, activeTakeProfitPoints);
                    SendWebhook("buy", entryPrice, entryPrice, stopPrice, useMarketEntry, qty);
                    StartTradeLines(entryPrice, stopPrice, activeTakeProfitPoints > 0.0 ? entryPrice + activeTakeProfitPoints : 0.0, activeTakeProfitPoints > 0.0);
                    SubmitLongEntryOrder(qty, entryPrice, useMarketEntry, LongEntrySignal);
                    LogDebug(string.Format("Place LONG {0} | session={1} entry={2:0.00} stop={3:0.00} qty={4}", useMarketEntry ? "market" : "limit", FormatSessionLabel(activeSession), entryPrice, stopPrice, qty));
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
                    bool useMarketEntry = EntryOffsetPoints <= 0.0;
                    double entryPrice = GetEntryPriceForDirection(Close[0], false, EntryOffsetPoints);
                    double stopPrice = BuildShortEntryStopPrice(entryPrice, emaValue, Time[0]);
                    int qty = GetEntryQuantity();
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", entryPrice, qty))
                    {
                        LogDebug("Entry confirmation declined | SHORT.");
                        EndTradeAttempt("confirmation-declined");
                        return;
                    }

                    pendingShortStopForWebhook = stopPrice;
                    SetStopLossByDistanceTicks(ShortEntrySignal, entryPrice, stopPrice);
                    SetProfitTargetByDistanceTicks(ShortEntrySignal, activeTakeProfitPoints);
                    SendWebhook("sell", entryPrice, entryPrice, stopPrice, useMarketEntry, qty);
                    StartTradeLines(entryPrice, stopPrice, activeTakeProfitPoints > 0.0 ? entryPrice - activeTakeProfitPoints : 0.0, activeTakeProfitPoints > 0.0);
                    SubmitShortEntryOrder(qty, entryPrice, useMarketEntry, ShortEntrySignal);
                    LogDebug(string.Format("Place SHORT {0} | session={1} entry={2:0.00} stop={3:0.00} qty={4}", useMarketEntry ? "market" : "limit", FormatSessionLabel(activeSession), entryPrice, stopPrice, qty));
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

            if (IsLongEntryOrderName(order.Name))
                longEntryOrder = order;
            else if (IsShortEntryOrderName(order.Name))
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

            bool isEntryOrder = IsEntryOrderName(order.Name);
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
            Print(string.Format("{0} | Duo | bar={1} | Order rejected guard | name={2} state={3} error={4} comment={5}",
                Time[0], CurrentBar, name, state, error, comment ?? string.Empty));

            if (IsEntryOrderName(name))
            {
                CancelWorkingEntryOrders();
                EndTradeAttempt("entry-rejected");
                return;
            }

            if (name == "Stop loss" || name == "Profit target")
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("ProtectiveReject", GetOpenLongEntrySignal());
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("ProtectiveReject", GetOpenShortEntrySignal());
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

            if (IsEntryOrderName(orderName))
            {
                currentPositionEntrySignal = orderName;
                SessionSlot entrySession = activeSession != SessionSlot.None
                    ? activeSession
                    : DetermineSessionForTime(time);
                if (entrySession != SessionSlot.None)
                {
                    lockedTradeSession = entrySession;
                    SetTradesThisSession(entrySession, GetTradesThisSession(entrySession) + 1);
                    LogDebug(string.Format("Session lock set to {0} on {1} fill.", FormatSessionLabel(lockedTradeSession), orderName));
                    LogDebug(string.Format(
                        "Trade count | session={0} taken={1}",
                        FormatSessionLabel(entrySession),
                        GetTradesThisSession(entrySession)));
                }
            }
            else if (marketPosition == MarketPosition.Flat && lockedTradeSession != SessionSlot.None)
            {
                LogDebug(string.Format("Session lock released from {0} after flat execution ({1}).", FormatSessionLabel(lockedTradeSession), orderName));
                lockedTradeSession = SessionSlot.None;
                currentPositionEntrySignal = string.Empty;
            }
            else if (marketPosition == MarketPosition.Flat)
            {
                currentPositionEntrySignal = string.Empty;
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

            if (!IsEntryOrderName(orderName))
            {
                if (tradeLinesActive)
                    FinalizeTradeLines();
                EndTradeAttempt("exit-" + orderName);
            }

            UpdateInfo();
        }

        private bool IsEntryOrderName(string orderName)
        {
            return IsLongEntryOrderName(orderName) || IsShortEntryOrderName(orderName);
        }

        private bool IsLongEntryOrderName(string orderName)
        {
            return string.Equals(orderName, LongEntrySignal, StringComparison.Ordinal)
                || string.Equals(orderName, LongFlipEntrySignal, StringComparison.Ordinal);
        }

        private bool IsShortEntryOrderName(string orderName)
        {
            return string.Equals(orderName, ShortEntrySignal, StringComparison.Ordinal)
                || string.Equals(orderName, ShortFlipEntrySignal, StringComparison.Ordinal);
        }

        private string GetOpenLongEntrySignal()
        {
            return IsLongEntryOrderName(currentPositionEntrySignal) ? currentPositionEntrySignal : LongEntrySignal;
        }

        private string GetOpenShortEntrySignal()
        {
            return IsShortEntryOrderName(currentPositionEntrySignal) ? currentPositionEntrySignal : ShortEntrySignal;
        }

        private bool ShouldSendExitWebhook(Execution execution, string orderName, MarketPosition marketPosition)
        {
            if (execution?.Order == null)
                return false;

            if (IsEntryOrderName(orderName))
                return false;

            string fromEntry = execution.Order.FromEntrySignal ?? string.Empty;
            if (IsEntryOrderName(fromEntry))
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
                || normalized.Equals("EmaExitLong", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("EmaExitShort", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("AdxDrawdownExit", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("AdxLevelExit", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("TakeProfitExit", StringComparison.OrdinalIgnoreCase)
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

            tradeLineTagPrefix = string.Format("Duo_TradeLine_{0}_{1}_", ++tradeLineTagCounter, CurrentBar);
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
            TimeSpan nyStart = TimeSpan.Zero;
            TimeSpan nyEnd = TimeSpan.Zero;

            bool asiaConfigured = IsSessionConfigured(SessionSlot.Asia)
                && TryGetSessionWindow(SessionSlot.Asia, time, out asiaStart, out asiaEnd);
            bool newYorkConfigured = IsSessionConfigured(SessionSlot.NewYork)
                && TryGetSessionWindow(SessionSlot.NewYork, time, out nyStart, out nyEnd);

            if (asiaConfigured && IsTimeInRange(now, asiaStart, asiaEnd))
                return SessionSlot.Asia;
            if (newYorkConfigured && IsTimeInRange(now, nyStart, nyEnd))
                return SessionSlot.NewYork;

            if (!asiaConfigured && !newYorkConfigured)
                return SessionSlot.None;

            DateTime nextAsiaStart = DateTime.MaxValue;
            DateTime nextNewYorkStart = DateTime.MaxValue;

            if (asiaConfigured)
            {
                nextAsiaStart = time.Date + asiaStart;
                if (nextAsiaStart <= time)
                    nextAsiaStart = nextAsiaStart.AddDays(1);
            }

            if (newYorkConfigured)
            {
                nextNewYorkStart = time.Date + nyStart;
                if (nextNewYorkStart <= time)
                    nextNewYorkStart = nextNewYorkStart.AddDays(1);
            }

            if (nextAsiaStart <= nextNewYorkStart)
                return SessionSlot.Asia;
            return SessionSlot.NewYork;
        }

        private SessionSlot GetFirstConfiguredSession()
        {
            if (IsSessionConfigured(SessionSlot.Asia))
                return SessionSlot.Asia;
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
                    activeFlipAdxThreshold = AsiaFlipAdxThreshold;
                    activeAdxMaxThreshold = AsiaAdxMaxThreshold;
                    activeAdxMinSlopePoints = AsiaAdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = AsiaAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = AsiaAdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = AsiaContracts;
                    activeTradeDirection = AsiaTradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = AsiaEmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = AsiaMaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = AsiaStopPaddingPoints;
                    activeExitCrossPoints = AsiaExitCrossPoints;
                    activeTakeProfitPoints = AsiaTakeProfitPoints;
                    break;

                case SessionSlot.NewYork:
                    activeEma = emaNewYork;
                    activeAdx = adxNewYork;
                    activeEmaPeriod = NewYorkEmaPeriod;
                    activeAdxPeriod = NewYorkAdxPeriod;
                    activeAdxThreshold = NewYorkAdxThreshold;
                    activeFlipAdxThreshold = NewYorkFlipAdxThreshold;
                    activeAdxMaxThreshold = NewYorkAdxMaxThreshold;
                    activeAdxMinSlopePoints = NewYorkAdxMinSlopePoints;
                    activeAdxPeakDrawdownExitUnits = NewYorkAdxPeakDrawdownExitUnits;
                    activeAdxAbsoluteExitLevel = NewYorkAdxAbsoluteExitLevel;
                    UpdateAdxReferenceLines(activeAdx, activeAdxThreshold, activeAdxMaxThreshold);
                    activeContracts = NewYorkContracts;
                    activeTradeDirection = NewYorkTradeDirection;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = NewYorkEmaMinSlopePointsPerBar;
                    activeMaxEntryDistanceFromEmaPoints = NewYorkMaxEntryDistanceFromEmaPoints;
                    activeStopPaddingPoints = NewYorkStopPaddingPoints;
                    activeExitCrossPoints = NewYorkExitCrossPoints;
                    activeTakeProfitPoints = NewYorkTakeProfitPoints;
                    break;

                default:
                    activeEma = null;
                    activeAdx = null;
                    activeEmaPeriod = 0;
                    activeAdxPeriod = 0;
                    activeAdxThreshold = 0.0;
                    activeFlipAdxThreshold = 0.0;
                    activeAdxMaxThreshold = 0.0;
                    activeAdxMinSlopePoints = 0.0;
                    activeAdxPeakDrawdownExitUnits = 0.0;
                    activeAdxAbsoluteExitLevel = 0.0;
                    activeContracts = 0;
                    activeTradeDirection = SessionTradeDirection.Both;
                    activeEntryStopMode = InitialStopMode.WickExtreme;
                    activeEmaMinSlopePointsPerBar = 0.0;
                    activeMaxEntryDistanceFromEmaPoints = 0.0;
                    activeStopPaddingPoints = 0.0;
                    activeExitCrossPoints = 0.0;
                    activeTakeProfitPoints = 0.0;
                    break;
            }
        }

        private void UpdateEmaPlotVisibility()
        {
            if (!ShowEmaOnChart)
            {
                SetEmaVisible(emaAsia, false);
                SetEmaVisible(emaNewYork, false);
                return;
            }

            bool showAsia = activeSession == SessionSlot.Asia;
            bool showNewYork = activeSession == SessionSlot.NewYork;

            // NinjaTrader may return shared indicator instances when periods match.
            // If two session EMA fields point to the same instance, visibility must be
            // resolved across all aliases before applying the brush.
            bool visAsia = showAsia
                || (ReferenceEquals(emaAsia, emaNewYork) && showNewYork);
            bool visNewYork = showNewYork
                || (ReferenceEquals(emaNewYork, emaAsia) && showAsia);

            SetEmaVisible(emaAsia, visAsia);
            SetEmaVisible(emaNewYork, visNewYork);
        }

        private void UpdateAdxPlotVisibility()
        {
            SetAdxVisible(adxAsia, ShowAdxOnChart);
            SetAdxVisible(adxNewYork, ShowAdxOnChart);
        }

        private void SetEmaVisible(EMA ema, bool visible)
        {
            if (ema == null || ema.Plots == null || ema.Plots.Length == 0)
                return;

            ema.Plots[0].Brush = visible ? Brushes.Gold : Brushes.Transparent;
        }

        private void SetAdxVisible(DM adx, bool showAdx)
        {
            if (adx == null || adx.Plots == null || adx.Plots.Length == 0)
                return;

            Brush adxBrush = showAdx ? Brushes.DodgerBlue : Brushes.Transparent;

            adx.Plots[0].Brush = adxBrush;
            if (adx.Plots.Length > 1)
                adx.Plots[1].Brush = Brushes.Transparent;
            if (adx.Plots.Length > 2)
                adx.Plots[2].Brush = Brushes.Transparent;
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
                "Duo_ADX_Min_" + adxTagSuffix,
                drawMin ? minThreshold : 0.0,
                drawMin ? Brushes.LimeGreen : Brushes.Transparent,
                DashStyleHelper.Solid,
                2);

            Draw.HorizontalLine(
                adx,
                "Duo_ADX_Max_" + adxTagSuffix,
                drawMax ? maxThreshold : 0.0,
                drawMax ? Brushes.OrangeRed : Brushes.Transparent,
                DashStyleHelper.Dash,
                2);
        }

        private int GetMaxConfiguredEmaPeriod()
        {
            return Math.Max(AsiaEmaPeriod, NewYorkEmaPeriod);
        }

        private void ProcessSessionTransitions(SessionSlot slot)
        {
            bool inNow = TimeInSession(slot, Time[0]);
            bool inPrev = CurrentBar > 0 ? TimeInSession(slot, Time[1]) : inNow;

            if (inNow && !inPrev)
            {
                SetSessionClosed(slot, false);
                SetTradesThisSession(slot, 0);
                LogDebug(string.Format("{0} session start.", FormatSessionLabel(slot)));
                LogDebug(string.Format("{0} trade counter reset.", FormatSessionLabel(slot)));
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

        private int GetTradesThisSession(SessionSlot slot)
        {
            switch (slot)
            {
                case SessionSlot.Asia:
                    return asiaTradesThisSession;
                case SessionSlot.NewYork:
                    return newYorkTradesThisSession;
                default:
                    return 0;
            }
        }

        private void SetTradesThisSession(SessionSlot slot, int value)
        {
            int normalized = Math.Max(0, value);
            switch (slot)
            {
                case SessionSlot.Asia:
                    asiaTradesThisSession = normalized;
                    break;
                case SessionSlot.NewYork:
                    newYorkTradesThisSession = normalized;
                    break;
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
            ReconcileTrackedEntryOrder(ref longEntryOrder, LongEntrySignal, ref missingLongEntryOrderBars);
            ReconcileTrackedEntryOrder(ref shortEntryOrder, ShortEntrySignal, ref missingShortEntryOrderBars);
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

        private double BuildLongEntryStopPrice(double entryPrice, double emaValue, DateTime time)
        {
            double raw = emaValue - GetActiveLongStopPaddingPoints(time);

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);
            return rounded;
        }

        private double BuildShortEntryStopPrice(double entryPrice, double emaValue, DateTime time)
        {
            double raw = emaValue + GetActiveShortStopPaddingPoints(time);

            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }

        private double BuildFlipShortStopPrice(double entryPrice, double emaValue, DateTime time)
        {
            double raw = emaValue + GetActiveShortStopPaddingPoints(time);
            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded <= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice + TickSize);
            return rounded;
        }

        private double BuildFlipLongStopPrice(double entryPrice, double emaValue, DateTime time)
        {
            double raw = emaValue - GetActiveLongStopPaddingPoints(time);
            double rounded = Instrument.MasterInstrument.RoundToTickSize(raw);
            if (rounded >= entryPrice)
                rounded = Instrument.MasterInstrument.RoundToTickSize(entryPrice - TickSize);
            return rounded;
        }

        private double GetEntryPriceForDirection(double signalClose, bool isLong, double offsetPoints)
        {
            if (offsetPoints <= 0.0)
                return Instrument.MasterInstrument.RoundToTickSize(signalClose);

            double raw = isLong
                ? signalClose - offsetPoints
                : signalClose + offsetPoints;
            return Instrument.MasterInstrument.RoundToTickSize(raw);
        }

        private void SubmitLongEntryOrder(int quantity, double entryPrice, bool isMarketEntry, string signalName)
        {
            if (isMarketEntry)
                EnterLong(quantity, signalName);
            else
                EnterLongLimit(0, true, quantity, entryPrice, signalName);
        }

        private void SubmitShortEntryOrder(int quantity, double entryPrice, bool isMarketEntry, string signalName)
        {
            if (isMarketEntry)
                EnterShort(quantity, signalName);
            else
                EnterShortLimit(0, true, quantity, entryPrice, signalName);
        }

        private double GetActiveLongStopPaddingPoints(DateTime time)
        {
            return IsHighVolatilitySlWindow(time) ? HvSlPaddingPoints : activeStopPaddingPoints;
        }

        private double GetActiveShortStopPaddingPoints(DateTime time)
        {
            return IsHighVolatilitySlWindow(time) ? HvSlPaddingPoints : activeStopPaddingPoints;
        }

        private bool IsHighVolatilitySlWindow(DateTime time)
        {
            if (HvSlPaddingPoints <= 0.0)
                return false;

            if (HvSlStartTime == HvSlEndTime)
                return false;

            return IsTimeInRange(time.TimeOfDay, HvSlStartTime, HvSlEndTime);
        }

        private double GetAdxSlopePoints()
        {
            if (activeAdx == null || CurrentBar < 1)
                return 0.0;

            return activeAdx[0] - activeAdx[1];
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

        private bool TryGetSessionWindow(SessionSlot slot, out TimeSpan start, out TimeSpan end)
        {
            return TryGetSessionWindow(slot, Time[0], out start, out end);
        }

        private bool TryGetSessionWindow(SessionSlot slot, DateTime referenceTime, out TimeSpan start, out TimeSpan end)
        {
            start = TimeSpan.Zero;
            end = TimeSpan.Zero;

            switch (slot)
            {
                case SessionSlot.Asia:
                    if (!UseAsiaSession || AsiaSessionStart == AsiaSessionEnd)
                        return false;
                    start = AsiaSessionStart;
                    end = AsiaSessionEnd;
                    return true;

                case SessionSlot.NewYork:
                    if (!UseNewYorkSession || NewYorkSessionStart == NewYorkSessionEnd)
                        return false;
                    start = NewYorkSessionStart;
                    end = NewYorkSessionEnd;
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

        private void DrawSessionBackgrounds()
        {
            if (CurrentBar < 1)
                return;

            DrawSessionBackground(SessionSlot.Asia, "Duo_Asia", AsiaSessionBrush ?? Brushes.LightSkyBlue);
            DrawSessionBackground(SessionSlot.NewYork, "Duo_NewYork", NewYorkSessionBrush ?? Brushes.LightSkyBlue);
            DrawNewYorkSkipWindow(Time[0]);
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
        }

        private bool IsNewYorkSkipTime(DateTime time)
        {
            if (!UseNewYorkSession)
                return false;

            if (NewYorkSkipStart == TimeSpan.Zero || NewYorkSkipEnd == TimeSpan.Zero)
                return false;

            TimeSpan now = time.TimeOfDay;
            if (NewYorkSkipStart < NewYorkSkipEnd)
                return now >= NewYorkSkipStart && now <= NewYorkSkipEnd;

            return now >= NewYorkSkipStart || now <= NewYorkSkipEnd;
        }

        private void DrawNewYorkSkipWindow(DateTime barTime)
        {
            if (!UseNewYorkSession)
                return;

            if (NewYorkSkipStart == TimeSpan.Zero || NewYorkSkipEnd == TimeSpan.Zero)
                return;

            DateTime windowStart = barTime.Date + NewYorkSkipStart;
            DateTime windowEnd = barTime.Date + NewYorkSkipEnd;
            if (NewYorkSkipStart > NewYorkSkipEnd)
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

            string tagBase = string.Format("Duo_NewYorkSkip_{0:yyyyMMdd_HHmm}", windowStart);
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
                catch { }

                string tagBase = string.Format("Duo_News_{0:yyyyMMdd_HHmm}", newsTime);

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

            NewsDates.Clear();
            LoadHardcodedNewsDates();

            NewsDates.Sort();
            newsDatesInitialized = true;
            AppendNewsFetchLog(string.Format(
                "Refresh complete | source=hardcoded count={0} events=[{1}]",
                NewsDates.Count,
                FormatNewsDatesForLog(NewsDates)));
        }

        private void LoadHardcodedNewsDates()
        {
            if (string.IsNullOrWhiteSpace(NewsDatesRaw))
                return;

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
                if ((t == new TimeSpan(8, 30, 0) || t == new TimeSpan(14, 0, 0)) && !NewsDates.Contains(parsed))
                    NewsDates.Add(parsed);
            }
        }

        private static string FormatNewsDatesForLog(List<DateTime> dates)
        {
            if (dates == null || dates.Count == 0)
                return string.Empty;

            return string.Join(", ", dates.Select(d => d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        }

        private void AppendNewsFetchLog(string message)
        {
            LogDebug(string.Format("NewsFetch | {0}", message ?? string.Empty));
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
            if (!TryGetSessionWindow(activeSession, Time[0], out start, out end))
                return;

            bool inNow = TimeInSession(activeSession, Time[0]);

            LogDebug(string.Format(
                "SessionConfig ({0}) | session={1} inSessionNow={2} closeAtSessionEnd={3} start={4:hh\\:mm} end={5:hh\\:mm} ema={6} adxMin={7:0.##} adxMax={8:0.##} adxSlopeMin={9:0.##} adxPeakDd={10:0.##} adxAbsExit={11:0.##} tpPts={12:0.##} contracts={13} exitCross={14:0.##} entryStop={15} slPad={16:0.##}",
                reason,
                FormatSessionLabel(activeSession),
                inNow,
                CloseAtSessionEnd,
                start,
                end,
                activeEmaPeriod,
                activeAdxThreshold,
                activeAdxMaxThreshold,
                activeAdxMinSlopePoints,
                activeAdxPeakDrawdownExitUnits,
                activeAdxAbsoluteExitLevel,
                activeTakeProfitPoints,
                activeContracts,
                activeExitCrossPoints,
                activeEntryStopMode,
                activeStopPaddingPoints));

            int adxPlotCount = activeAdx != null && activeAdx.Plots != null ? activeAdx.Plots.Length : 0;
            int adxValueCount = activeAdx != null && activeAdx.Values != null ? activeAdx.Values.Length : 0;
            string adxType = activeAdx != null ? activeAdx.GetType().Name : "null";
            LogDebug(string.Format(
                "AdxVisuals ({0}) | session={1} type={2} showAdx={3} showThresholds={4} plots={5} values={6}",
                reason,
                FormatSessionLabel(activeSession),
                adxType,
                ShowAdxOnChart,
                ShowAdxThresholdLines,
                adxPlotCount,
                adxValueCount));
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
                Print(string.Format("Duo | {0}", message));
                return;
            }

            Print(string.Format("{0} | Duo | bar={1} | {2}", Time[0], CurrentBar, message));
        }

        public void UpdateInfo()
        {
            if (ChartControl == null)
                return;
            UpdateInfoText();
        }

        public void UpdateInfoText()
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
            if (!EnsureInfoBoxOverlay())
                return;

            if (infoBoxRowsPanel == null)
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
                    FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal
                };
                TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(text, TextRenderingMode.ClearType);

                string rawLabel = lines[i].label ?? string.Empty;
                string value = lines[i].value ?? string.Empty;
                string label = rawLabel;

                if (string.IsNullOrEmpty(value) && rawLabel.StartsWith("News:", StringComparison.Ordinal))
                {
                    label = "News:";
                    value = rawLabel.Substring("News:".Length).TrimStart();
                }

                text.Inlines.Add(new Run(label) { Foreground = isHeader ? InfoHeaderTextBrush : InfoLabelBrush });
                if (!string.IsNullOrEmpty(value))
                {
                    text.Inlines.Add(new Run(" ") { Foreground = isHeader ? InfoHeaderTextBrush : InfoLabelBrush });

                    Brush stateValueBrush = lines[i].valueBrush;
                    if (stateValueBrush == null || stateValueBrush == Brushes.Transparent)
                        stateValueBrush = lines[i].labelBrush;
                    if (stateValueBrush == null || stateValueBrush == Brushes.Transparent)
                        stateValueBrush = InfoValueBrush;

                    text.Inlines.Add(new Run(value) { Foreground = stateValueBrush });
                }

                rowBorder.Child = text;
                infoBoxRowsPanel.Children.Add(rowBorder);
            }
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

            double adxValue = activeAdx != null ? activeAdx[0] : 0.0;
            double adxSlope = GetAdxSlopePoints();
            bool adxMinEnabled = activeAdxThreshold > 0.0;
            bool adxMaxEnabled = activeAdxMaxThreshold > 0.0;
            bool slopeEnabled = activeAdxMinSlopePoints > 0.0;
            bool aboveMin = !adxMinEnabled || adxValue >= activeAdxThreshold;
            bool belowMin = adxMinEnabled && adxValue < activeAdxThreshold;
            bool overMax = adxMaxEnabled && adxValue > activeAdxMaxThreshold;
            bool slopeValid = !slopeEnabled || adxSlope >= activeAdxMinSlopePoints;

            string paState;
            Brush paBrush;
            if (overMax)
            {
                paState = "Peaking";
                paBrush = Brushes.OrangeRed;
            }
            else if (belowMin)
            {
                paState = "Weak";
                paBrush = Brushes.IndianRed;
            }
            else if (aboveMin && slopeValid)
            {
                paState = "Trending";
                paBrush = Brushes.LimeGreen;
            }
            else
            {
                paState = "Ranging";
                paBrush = Brushes.Gold;
            }

            lines.Add((string.Format("Duo v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(("PA:", paState, Brushes.LightGray, paBrush));
            string momentumThresholdText = activeAdxMinSlopePoints > 0.0
                ? activeAdxMinSlopePoints.ToString("0.00", CultureInfo.InvariantCulture)
                : "Off";
            lines.Add(("Mom:", momentumThresholdText, Brushes.LightGray, Brushes.LightGray));
            List<DateTime> weekNews = GetCurrentWeekNews(Time[0]);
            if (weekNews.Count == 0)
            {
                lines.Add(("News:", "⛔", Brushes.LightGray, Brushes.IndianRed));
            }
            else
            {
                for (int i = 0; i < weekNews.Count; i++)
                {
                    DateTime newsTime = weekNews[i];
                    bool blockPassed = Time[0] > newsTime.AddMinutes(NewsBlockMinutes);
                    string dayPart = newsTime.ToString("ddd", CultureInfo.InvariantCulture);
                    string timePart = newsTime.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
                    string label = "News: " + dayPart + " " + timePart;
                    Brush labelBrush = blockPassed ? PassedNewsRowBrush : Brushes.LightGray;
                    lines.Add((label, string.Empty, labelBrush, Brushes.Transparent));
                }
            }

            lines.Add(("Session:", FormatSessionLabel(activeSession), Brushes.LightGray, Brushes.LightGray));
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
            catch { }
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
            catch { }
            return brush;
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

        private string GetAddOnVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Version version = assembly.GetName().Version;
            return version != null ? version.ToString() : "0.0.0.0";
        }

        private int GetMaxConfiguredAdxPeriod()
        {
            return Math.Max(AsiaAdxPeriod, NewYorkAdxPeriod);
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

        private int GetEntryQuantity()
        {
            int baseQty = Math.Max(1, activeContracts);
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
                    ExitLong("MaxAccountBalance", GetOpenLongEntrySignal());
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("MaxAccountBalance", GetOpenShortEntrySignal());

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

        private void SendFlipWebhooks(string entryEventType, double entryPrice, double stopPrice, bool isMarketEntry, int entryQuantity, MarketPosition expectedCurrentPosition)
        {
            int positionQty = Math.Abs(Position.Quantity);
            bool shouldExitFirst = Position.MarketPosition == expectedCurrentPosition && positionQty > 0;

            if (shouldExitFirst)
            {
                LogDebug(string.Format(
                    "Flip webhook sequence | step=exit side={0} qty={1}",
                    expectedCurrentPosition,
                    positionQty));
                SendWebhook("exit", 0, 0, 0, true, positionQty);
            }

            int webhookEntryQty = Math.Max(1, entryQuantity);
            LogDebug(string.Format(
                "Flip webhook sequence | step=entry action={0} entryQty={1} market={2} entry={3:0.00} stop={4:0.00}",
                entryEventType,
                webhookEntryQty,
                isMarketEntry,
                entryPrice,
                stopPrice));
            SendWebhook(entryEventType, entryPrice, entryPrice, stopPrice, isMarketEntry, webhookEntryQty);
        }

        private void SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime)
                return;

            if (WebhookProviderType == WebhookProvider.ProjectX)
            {
                int orderQtyForProvider = quantityOverride > 0 ? quantityOverride : Math.Max(1, activeContracts);
                LogDebug(string.Format(
                    "Webhook attempt | provider=ProjectX event={0} qty={1} market={2} entry={3:0.00} tp={4:0.00} sl={5:0.00}",
                    eventType,
                    orderQtyForProvider,
                    isMarketEntry,
                    entryPrice,
                    takeProfit,
                    stopLoss));
                SendProjectX(eventType, entryPrice, takeProfit, stopLoss, isMarketEntry, orderQtyForProvider);
                return;
            }

            if (string.IsNullOrWhiteSpace(WebhookUrl))
            {
                LogDebug(string.Format("Webhook skipped | provider=TradersPost event={0} reason=empty-url", eventType));
                return;
            }

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
                {
                    LogDebug(string.Format(
                        "Webhook skipped | provider=TradersPost event={0} reason=empty-payload qty={1}",
                        eventType,
                        orderQty));
                    return;
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
        [Display(Name = "Asia Session(18:30-2:00)", Description = "Enable trading logic during the Asia time window.", GroupName = "Asia", Order = 0)]
        public bool UseAsiaSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "Asia session start time in chart time zone.", GroupName = "Asia", Order = 1)]
        public TimeSpan AsiaSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "Asia session end time in chart time zone.", GroupName = "Asia", Order = 2)]
        public TimeSpan AsiaSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Block Sunday Trades", Description = "If enabled, block new Asia entries/flips on Sundays.", GroupName = "Asia", Order = 3)]
        public bool AsiaBlockSundayTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by Asia entry and exit logic.", GroupName = "Asia", Order = 4)]
        public int AsiaEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for Asia entries.", GroupName = "Asia", Order = 5)]
        public int AsiaContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Direction", Description = "Select whether Asia can take long, short, or both directions.", GroupName = "Asia", Order = 6)]
        public SessionTradeDirection AsiaTradeDirection { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the Asia trend filter.", GroupName = "Asia", Order = 10)]
        public int AsiaAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. Asia entries are allowed only when ADX is greater than or equal to this value.", GroupName = "Asia", Order = 11)]
        public double AsiaAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, Asia flips are allowed only when ADX is greater than or equal to this value.", GroupName = "Asia", Order = 7)]
        public double AsiaFlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. Asia entries are allowed only when ADX is less than or equal to this value.", GroupName = "Asia", Order = 12)]
        public double AsiaAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        // [TypeConverter(typeof(AsiaAdxSlopeDropdownConverter))]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "Asia", Order = 13)]
        public double AsiaAdxMinSlopePoints
        {
            get { return asiaAdxMinSlopePoints; }
            set { asiaAdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "Asia", Order = 14)]
        public double AsiaAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "Asia", Order = 8)]
        public double AsiaEmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "Asia", Order = 9)]
        public double AsiaMaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "Asia", Order = 15)]
        public double AsiaAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "Asia", Order = 16)]
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
        [Display(Name = "New York Session(9.40-13:30)", Description = "Enable trading logic during the New York time window.", GroupName = "New York", Order = 0)]
        public bool UseNewYorkSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "New York session start time in chart time zone.", GroupName = "New York", Order = 1)]
        public TimeSpan NewYorkSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session End", Description = "New York session end time in chart time zone.", GroupName = "New York", Order = 2)]
        public TimeSpan NewYorkSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start", Description = "Start of New York skip window.", GroupName = "New York", Order = 3)]
        public TimeSpan NewYorkSkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End", Description = "End of New York skip window.", GroupName = "New York", Order = 4)]
        public TimeSpan NewYorkSkipEnd { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EMA Period", Description = "EMA period used by New York entry and exit logic.", GroupName = "New York", Order = 5)]
        public int NewYorkEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Description = "Base contracts for New York entries.", GroupName = "New York", Order = 6)]
        public int NewYorkContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade Direction", Description = "Select whether New York can take long, short, or both directions.", GroupName = "New York", Order = 7)]
        public SessionTradeDirection NewYorkTradeDirection { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ADX Period", Description = "ADX lookback period for the New York trend filter.", GroupName = "New York", Order = 11)]
        public int NewYorkAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Min Threshold", Description = "0 disables. New York entries are allowed only when ADX is greater than or equal to this value.", GroupName = "New York", Order = 12)]
        public double NewYorkAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "FLIP ADX Min Threshold", Description = "0 disables. When Require Min ADX For Flips is enabled, New York flips are allowed only when ADX is greater than or equal to this value.", GroupName = "New York", Order = 8)]
        public double NewYorkFlipAdxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Max Threshold", Description = "0 disables. New York entries are allowed only when ADX is less than or equal to this value.", GroupName = "New York", Order = 13)]
        public double NewYorkAdxMaxThreshold { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        // [TypeConverter(typeof(NewYorkAdxSlopeDropdownConverter))]
        [Display(Name = "ADX Momentum Threshold", Description = "Momentum Threshold (2 decimals)", GroupName = "New York", Order = 14)]
        public double NewYorkAdxMinSlopePoints
        {
            get { return newYorkAdxMinSlopePoints; }
            set { newYorkAdxMinSlopePoints = Math.Round(value, 2, MidpointRounding.AwayFromZero); }
        }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "ADX Peak Drawdown Exit", Description = "0 disables. While in a trade, track the highest ADX value and flatten when ADX drops by this many units from that peak.", GroupName = "New York", Order = 15)]
        public double NewYorkAdxPeakDrawdownExitUnits { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP EMA Min Slope (Points/Bar)", Description = "Minimum EMA slope required for flip signals. 0 disables.", GroupName = "New York", Order = 9)]
        public double NewYorkEmaMinSlopePointsPerBar { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "FLIP Max Entry Distance From EMA", Description = "0 disables. Block flip entries when close is farther than this many points from EMA.", GroupName = "New York", Order = 10)]
        public double NewYorkMaxEntryDistanceFromEmaPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "ADX Absolute Exit Level", Description = "0 disables. While in a trade, exit immediately when ADX reaches or exceeds this value.", GroupName = "New York", Order = 16)]
        public double NewYorkAdxAbsoluteExitLevel { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "SL Padding Points", Description = "Normal (non-HV) stop distance in points from EMA on the opposite side.", GroupName = "New York", Order = 17)]
        public double NewYorkStopPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Exit Cross Points", Description = "Additional points beyond EMA before evaluating exit/flip. 0 means EMA touch/cross.", GroupName = "New York", Order = 18)]
        public double NewYorkExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Take Profit (Points)", Description = "0 disables. Exit when unrealized profit reaches this many points from average entry price.", GroupName = "New York", Order = 19)]
        public double NewYorkTakeProfitPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close At Session End", Description = "If true, flatten positions and cancel entries at each configured session end.", GroupName = "10. Sessions", Order = 0)]
        public bool CloseAtSessionEnd { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Asia Session Fill", Description = "Background color used to highlight Asia session windows.", GroupName = "10. Sessions", Order = 1)]
        public Brush AsiaSessionBrush { get; set; }

        [Browsable(false)]
        public string AsiaSessionBrushSerializable
        {
            get { return Serialize.BrushToString(AsiaSessionBrush); }
            set { AsiaSessionBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "New York Session Fill", Description = "Background color used to highlight New York session windows.", GroupName = "10. Sessions", Order = 2)]
        public Brush NewYorkSessionBrush { get; set; }

        [Browsable(false)]
        public string NewYorkSessionBrushSerializable
        {
            get { return Serialize.BrushToString(NewYorkSessionBrush); }
            set { NewYorkSessionBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show EMA On Chart", Description = "Show/hide EMA indicators on chart.", GroupName = "10. Sessions", Order = 3)]
        public bool ShowEmaOnChart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ADX Show On Chart", Description = "Show/hide ADX indicators on chart.", GroupName = "10. Sessions", Order = 4)]
        public bool ShowAdxOnChart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ADX Show Threshold Lines", Description = "Show/hide ADX min/max threshold reference lines on chart.", GroupName = "10. Sessions", Order = 5)]
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
        [Display(Name = "HV SL Padding Points", Description = "High-volatility stop distance in points from EMA on the opposite side. 0 disables HV stop logic.", GroupName = "13. Risk", Order = 1)]
        public double HvSlPaddingPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Entry Offset Points", Description = "0 = market entry at signal close. Positive value = limit entry offset from signal close (long: close-offset, short: close+offset).", GroupName = "13. Risk", Order = 2)]
        public double EntryOffsetPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HV SL Start Time", Description = "Start time for using HV SL Padding Points.", GroupName = "13. Risk", Order = 3)]
        public TimeSpan HvSlStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "HV SL End Time", Description = "End time for using HV SL Padding Points.", GroupName = "13. Risk", Order = 4)]
        public TimeSpan HvSlEndTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Confirmation", Description = "Show a Yes/No confirmation popup before each new long/short entry (including flips).", GroupName = "13. Risk", Order = 5)]
        public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ADX Require Min For Flips (FLIP)", Description = "If enabled, flips are blocked while ADX is below the active session minimum ADX threshold line.", GroupName = "13. Risk", Order = 6)]
        public bool RequireMinAdxForFlips { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Debug Logging", Description = "Print concise decision, order, and execution diagnostics to Output.", GroupName = "14. Debug", Order = 0)]
        public bool DebugLogging { get; set; }
    }
}
