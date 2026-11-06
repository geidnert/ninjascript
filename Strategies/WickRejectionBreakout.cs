#region Using declarations
using System;
using System.Collections.Generic;
#if !WICK_CLOSE_SAFETY_TEST
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endif
#endregion

// WickRejectionBreakout: trades a break in the direction away from a qualifying
// rejection wick, but only when that break happens during the immediately next bar.
namespace NinjaTrader.NinjaScript.Strategies
{
#if !WICK_CLOSE_SAFETY_TEST
    public class WickRejectionBreakout : Strategy
    {
        public enum ConfirmationLevel { Body, OppositeWick }
        public enum StopLossMode { FixedPoints, RejectionWick }
        public enum TradeDirectionFilter { Both, LongsOnly, ShortsOnly }
        public enum PriorSessionProfileMode { Disabled, PocDirectional, OutsideValueArea, ReversalAtValueEdge }

        private const string StrategySignalPrefix = "WickRejectionBreakout";
        // Keep the broker/chart labels as readable as manual Chart Trader orders.
        // The entry signal remains the managed-order link for its stop and target.
        private const string LongEntrySignal = "Long";
        private const string ShortEntrySignal = "Short";
        private const string FlattenExitSignal = "Exit";
        private const string ProfitTargetExitOrderName = "Profit target";
        private const string StopLossExitOrderName = "Stop loss";

        private int pendingSetupBar = -1;
        private bool pendingLong;
        private double pendingTriggerPrice;
        private double pendingRejectionWickPrice;
        // Display-only state used while a broker/Playback chart replays its
        // pre-existing historical bars. It is never a NinjaTrader position.
        private bool historicalVisualTradeOpen;
        private bool historicalVisualTradeLong;
        private double historicalVisualStopPrice;
        private double historicalVisualTargetPrice;
        private bool configuredInstrumentIsValid = true;
        private bool configurationWarningShown;
        // Time-slot values are deliberately anchored to New York time, not to
        // NinjaTrader's configurable chart/display timezone.  The Windows
        // Eastern zone contains both EST and EDT rules.
        private TimeZoneInfo platformZone;
        private TimeZoneInfo easternZone;
        private DateTime activeProfileSessionDate = DateTime.MinValue;
        private Dictionary<double, long> activeSessionVolumeAtPrice;
        private SessionProfile previousSessionProfile;
        private bool maxDailyProfitLimitReached;
        private bool maxDailyLossLimitReached;
        private double maxDailyProfitStartBalance = double.NaN;
        private double maxDailyLossStartBalance = double.NaN;
        private DateTime maxDailyProfitDate = DateTime.MinValue;
        private DateTime maxDailyLossDate = DateTime.MinValue;
        private Brush tradingWindowOpenBrush;
        private Brush tradingWindowBlockedBrush;
        private RSI rsiEarlyExit;
        private bool rsiEarlyExitArmed;
        private bool rsiEarlyExitEntryPending;
        private bool rsiEarlyExitEntryLong;
        private int rsiEarlyExitEntryBar = -1;
        private bool propFirmEndOfDayExitSubmitted;
        private readonly object exitSafetyLock = new object();
        private WickExitSafetyCoordinator exitSafety = new WickExitSafetyCoordinator();
        private readonly List<SafetyOrderReference> safetyOrders = new List<SafetyOrderReference>();
        private System.Threading.Timer exitSafetyTimer;
        private bool exitSafetyReady;
        private bool exitSafetyStopped;
        private bool exitSafetyQueued;
        private bool exitSafetyProcessing;
        private int nextSafetyOrderToken;
        private long lastSafetyVersion = -1;
        private int lastSafetyStrategyPosition;
        private int? lastSafetyAccountPosition;
        private DateTime safetyProgressUtc = DateTime.UtcNow;
        private DateTime safetyWarningUtc = DateTime.MinValue;
        private bool safetyAccountReadWarning;
        private bool previousRedTimeBlock;
        private bool redTimeBlockCloseActive;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Enters on the next bar when price breaks away from a candle with a qualifying rejection wick.";
                Name = "WickRejectionBreakout";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                BarsRequiredToTrade = 2;
                // Do not rely on NinjaTrader's chart-session-close exit. Its
                // timing follows the chart template and, as the live TakeProfit
                // liquidation showed, can be later than the account's own
                // cutoff. The New York-time prop-firm safety path below owns
                // the block and flatten instead.
                IsExitOnSessionCloseStrategy = false;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;

                Quantity = 1;
                MinimumWickPoints = 2.0;
                // Full NT8 Playback on the shared 2026-06-29--2026-09-16
                // period improved net from $67,785 at 0.5 points to $73,080
                // at 3 points, with materially fewer stop losses.
                MinimumBodyPoints = 3.0;
                MaximumOppositeWickPoints = 0.25;
                BreakoutLevel = ConfirmationLevel.Body;
                // The all-hours TP11/SL70/RSI run with a two-bar slow-negative
                // exit is the best validated 5-minute Playback configuration.
                // Time-window exclusions did not generalize to the earlier
                // June--September validation period.
                UseSkipTimeSlot1 = false;
                SkipTimeSlot1Start = "20:00:00";
                SkipTimeSlot1End = "00:59:59";
                UseSkipTimeSlot2 = false;
                SkipTimeSlot2Start = "07:00:00";
                SkipTimeSlot2End = "09:59:59";
                // Research-only drawdown candidate. Keep disabled until NT8
                // Playback confirms it against the 3-point body baseline.
                UseSkipTimeSlot3 = false;
                SkipTimeSlot3Start = "12:00:00";
                SkipTimeSlot3End = "15:59:59";
                // TakeProfit liquidated a live NQ position at 16:55 New York
                // time. Stop seeking entries ten minutes beforehand and submit
                // our own market exit one minute beforehand, rather than letting
                // the prop firm flatten the account underneath managed orders.
                PropFirmEntryCutoffTime = "16:45:00";
                PropFirmForceFlatTime = "16:54:00";
                PropFirmSafetyEndTime = "17:00:00";
                ProfitTargetPoints = 11.0;
                StopLossPoints = 70.0;
                StopMode = StopLossMode.FixedPoints;
                MaxDailyProfit = 0.0;
                MaxDailyLoss = 0.0;
                UseRsiEarlyExit = true;
                RsiEarlyExitPeriod = 23;
                RsiEarlyExitUpper = 58.5;
                RsiEarlyExitLower = 41.5;
                // NT8 Playback validated this two-bar slow-and-negative exit
                // with the 5-minute TP11 configuration.
                UseNegativeTimeExit = true;
                NegativeTimeExitBars = 2;
                // 2-minute research candidate. Disabled preserves the proven
                // 5-minute TP11/two-bar-exit baseline.
                UseNoFollowThroughExit = false;
                NoFollowThroughBars = 4;
                MinimumFollowThroughPoints = 4.0;
                DrawSignals = true;
                DrawHistoricalSignals = true;
                ShowTradingWindowBackground = true;
                EnableDiagnosticLogging = true;

                // Research candidates only. They remain disabled until an
                // independent NT8 Playback comparison confirms the raw-tick
                // profile construction and the managed-order fills.
                TradeDirection = TradeDirectionFilter.Both;
                UsePriorLegFilter = false;
                PriorLegBars = 3;
                PriorLegMustOpposeEntry = true;
                PriorSessionProfileFilter = PriorSessionProfileMode.Disabled;
                PriorSessionValueAreaPercent = 80;
            }
            else if (State == State.DataLoaded)
            {
                SetupTimeZones();
                configuredInstrumentIsValid = ValidateRequiredPrimaryInstrument() && ValidatePointSettings() && ValidateTimeSettings();
                tradingWindowOpenBrush = CreateFrozenBrush(24, 0, 128, 0);
                tradingWindowBlockedBrush = CreateFrozenBrush(30, 178, 34, 34);
                rsiEarlyExit = RSI(RsiEarlyExitPeriod, 1);
                previousRedTimeBlock = false;
                redTimeBlockCloseActive = false;
                lock (exitSafetyLock)
                {
                    if (IsHistoricalTradeSimulationContext())
                    {
                        exitSafety = new WickExitSafetyCoordinator();
                        safetyOrders.Clear();
                        nextSafetyOrderToken = 0;
                    }
                    exitSafetyStopped = false;
                    exitSafetyQueued = false;
                    exitSafetyProcessing = false;
                    lastSafetyVersion = -1;
                    safetyProgressUtc = DateTime.UtcNow;
                }
                exitSafetyTimer = new System.Threading.Timer(
                    state => QueueExitSafetyProcessing(), null,
                    System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                exitSafetyReady = true;
            }
            else if (State == State.Realtime)
            {
                // A setup must never survive the historical-to-realtime handoff.
                // In particular, enabling this strategy mid-bar must wait for the
                // next genuinely live bar instead of acting on an already formed
                // historical bar.
                ClearPendingSetup();
                ClearHistoricalVisualTrade();
                ClearRsiEarlyExit();
                TransitionSafetyOrdersToRealtime();
            }
            else if (State == State.Terminated)
            {
                System.Threading.Timer timer;
                bool unfinished;
                lock (exitSafetyLock)
                {
                    exitSafetyStopped = true;
                    unfinished = exitSafety.Closing || exitSafety.OwnedPosition != 0 || exitSafety.ActiveOrders != 0;
                    timer = exitSafetyTimer;
                    exitSafetyTimer = null;
                }
                if (timer != null)
                    timer.Dispose();
                if (unfinished)
                    Print(StrategySignalPrefix + "|SAFE_CLOSE_STOPPED_PENDING|Check the account and working orders manually. "
                        + "Disabling does not prove that broker orders are cancelled.");
            }

        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            if (e.MarketDataType != MarketDataType.Last || e.Volume <= 0)
                return;

            ProcessExitSafety(false);
            DateTime easternTime = ConvertToEastern(e.Time);
            if (EvaluatePropFirmEndOfDaySafety(easternTime))
                return;

            DateTime sessionDate = easternTime.TimeOfDay >= new TimeSpan(18, 0, 0)
                ? easternTime.Date
                : easternTime.Date.AddDays(-1);

            if (activeProfileSessionDate != sessionDate)
            {
                if (activeSessionVolumeAtPrice != null && activeSessionVolumeAtPrice.Count > 0)
                {
                    previousSessionProfile = BuildSessionProfile(activeSessionVolumeAtPrice, PriorSessionValueAreaPercent);
                    Log(string.Format("PROFILE_READY|session={0:yyyy-MM-dd}|poc={1:F2}|val={2:F2}|vah={3:F2}|area={4}%",
                        activeProfileSessionDate, previousSessionProfile.Poc, previousSessionProfile.Val,
                        previousSessionProfile.Vah, PriorSessionValueAreaPercent));
                }

                activeProfileSessionDate = sessionDate;
                activeSessionVolumeAtPrice = new Dictionary<double, long>();
            }

            long existing;
            activeSessionVolumeAtPrice.TryGetValue(e.Price, out existing);
            activeSessionVolumeAtPrice[e.Price] = existing + e.Volume;
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice, OrderState orderState,
            DateTime time, ErrorCode error, string comment)
        {
            Order realtimeOrder = TranslateSafetyOrder(order);
            if (realtimeOrder == null)
                return;

            lock (exitSafetyLock)
            {
                SafetyOrderReference tracked = GetSafetyOrder(realtimeOrder);
                exitSafety.TrackOrder(tracked.Token, GetSafetyOrderKind(realtimeOrder), GetSafetyOrderState(orderState),
                    quantity, filled, IsSafetyCancelAllowed(orderState, realtimeOrder.OrderType));
                if (error != ErrorCode.NoError && orderState != OrderState.Filled
                    && orderState != OrderState.Cancelled && orderState != OrderState.Rejected)
                    exitSafety.Fail("Broker order action failed: " + error + ". Check the account and orders manually.");
            }
            if (orderState == OrderState.Rejected || error != ErrorCode.NoError)
                Print(string.Format(CultureInfo.InvariantCulture,
                    "{0}|{1}|time={2:O}|name={3}|fromEntry={4}|type={5}|id={6}|state={7}|error={8}|comment={9}",
                    StrategySignalPrefix, orderState == OrderState.Rejected ? "ORDER_REJECTED" : "SAFE_CLOSE_ORDER_ERROR",
                    time, realtimeOrder.Name, realtimeOrder.FromEntrySignal, realtimeOrder.OrderType,
                    realtimeOrder.OrderId, orderState, error, comment));
            else
                Log(string.Format("ORDER_STATE|name={0}|id={1}|state={2}|quantity={3}|filled={4}",
                    realtimeOrder.Name, realtimeOrder.OrderId, orderState, quantity, filled), time);
            QueueExitSafetyProcessing();
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            QueueExitSafetyProcessing();
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0)
                return;

            UpdateTradingWindowBackground();

            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Historical chart bars cannot submit managed orders for broker, Sim
            // and Playback instances. Calculate.OnEachTick would otherwise
            // evaluate their complete range as though it were available intrabar,
            // create a historical fill, and leave the strategy internally long
            // when it is enabled mid-session. We may still draw visual signal
            // markers, which are deliberately not simulated fills. Strategy
            // Analyzer deliberately retains its Backtest historical fills.
            if (State == State.Historical && !IsHistoricalTradeSimulationContext())
            {
                DrawHistoricalSignalMarkers();
                return;
            }

            bool redTimeBlock = IsSkippedTime(Time[0]);
            if (redTimeBlock && !previousRedTimeBlock)
            {
                redTimeBlockCloseActive = true;
                ExitAllPositions("TimeBlock");
                CancelRedTimeBlockOrders();
            }
            previousRedTimeBlock = redTimeBlock;

            if (ProcessExitSafety(false))
                return;

            if (!configuredInstrumentIsValid)
            {
                ExitAllPositions("InvalidConfiguration");
                ShowConfigurationWarningOnce();
                return;
            }

            if (EvaluateRsiEarlyExit())
                return;
            if (EvaluateNoFollowThroughExit())
                return;
            if (EvaluateNegativeTimeExit())
                return;

            if (IsAccountDailyProfitBlocked() || IsAccountDailyLossBlocked())
                return;

            if (redTimeBlock)
            {
                ClearPendingSetup();
                return;
            }

            // A setup is live only during the bar after the rejection candle.
            // At the first tick of each new bar, the prior setup expires and the
            // completed prior bar becomes the sole candidate for the new setup.
            // Historical processing invokes this once per completed bar; do not
            // rely on IsFirstTickOfBar being populated there.
            if (State == State.Historical || IsFirstTickOfBar)
            {
                // The previous setup was valid but its one permitted
                // confirmation bar ended without reaching its trigger. This
                // diagnostic makes a missing entry distinguishable from an
                // ignored setup or rejected order.
                if (pendingSetupBar == CurrentBar - 2)
                    Log(string.Format("SETUP_EXPIRED|reason=trigger_not_reached|direction={0}|trigger={1:F2}",
                        pendingLong ? "Long" : "Short", pendingTriggerPrice));
                ClearPendingSetup();
                CreateSetupFromPriorBar();
            }

            if (pendingSetupBar == CurrentBar - 1 && Position.MarketPosition == MarketPosition.Flat && CanEnterAfterSafety())
                EvaluatePendingSetup();
        }

        private void CreateSetupFromPriorBar()
        {
            double bodyHigh = Math.Max(Open[1], Close[1]);
            double bodyLow = Math.Min(Open[1], Close[1]);
            double bodySize = Math.Abs(Close[1] - Open[1]);
            double upperWick = High[1] - bodyHigh;
            double lowerWick = bodyLow - Low[1];
            bool upperQualifies = upperWick >= MinimumWickPoints;
            bool lowerQualifies = lowerWick >= MinimumWickPoints;

            if (bodySize < MinimumBodyPoints)
            {
                Log(string.Format("SETUP_SKIPPED|reason=body_too_small|body={0:F2}|minimum={1:F2}", bodySize, MinimumBodyPoints));
                return;
            }

            if (!upperQualifies && !lowerQualifies)
                return;

            // A candle can have two long wicks.  Use its stronger rejection;
            // an exact tie is directionally ambiguous and is ignored.
            if (upperQualifies && lowerQualifies && Math.Abs(upperWick - lowerWick) < TickSize / 2.0)
            {
                Log("SETUP_SKIPPED|reason=equal_qualifying_wicks");
                return;
            }

            pendingLong = lowerQualifies && (!upperQualifies || lowerWick > upperWick);
            if (!AllowsTradeDirection(pendingLong))
            {
                Log("SETUP_SKIPPED|reason=trade_direction_filter");
                return;
            }
            if (!AllowsPriorLeg(pendingLong))
            {
                Log("SETUP_SKIPPED|reason=prior_leg_filter");
                return;
            }
            if (!AllowsPriorSessionProfile(pendingLong))
            {
                Log("SETUP_SKIPPED|reason=prior_session_profile_filter");
                return;
            }
            double oppositeWick = pendingLong ? upperWick : lowerWick;
            if (MaximumOppositeWickPoints > 0 && oppositeWick > MaximumOppositeWickPoints)
            {
                Log(string.Format("SETUP_SKIPPED|reason=opposite_wick_too_large|oppositeWick={0:F2}|maximum={1:F2}",
                    oppositeWick, MaximumOppositeWickPoints));
                return;
            }

            pendingSetupBar = CurrentBar - 1;
            pendingRejectionWickPrice = pendingLong ? Low[1] : High[1];
            if (pendingLong)
                pendingTriggerPrice = BreakoutLevel == ConfirmationLevel.Body ? bodyHigh : High[1];
            else
                pendingTriggerPrice = BreakoutLevel == ConfirmationLevel.Body ? bodyLow : Low[1];

            Log(string.Format("SETUP|direction={0}|setupBar={1}|lowerWick={2:F2}|upperWick={3:F2}|trigger={4:F2}|level={5}",
                pendingLong ? "Long" : "Short", pendingSetupBar, lowerWick, upperWick, pendingTriggerPrice, BreakoutLevel));
        }

        private void EvaluatePendingSetup()
        {
            if (IsSkippedTime(Time[0]))
            {
                if (State == State.Historical || IsFirstTickOfBar)
                    Log(string.Format("SIGNAL_SKIPPED|reason=time_block|direction={0}|trigger={1:F2}",
                        pendingLong ? "Long" : "Short", pendingTriggerPrice));
                return;
            }

            bool broken = pendingLong ? High[0] >= pendingTriggerPrice : Low[0] <= pendingTriggerPrice;
            if (!broken)
                return;

            lock (exitSafetyLock)
            {
                if (!exitSafety.CanEnter)
                    return;
                exitSafety.BeginEntry();
            }
            string signal = pendingLong ? LongEntrySignal : ShortEntrySignal;
            if (StopMode == StopLossMode.FixedPoints)
                SetStopLoss(signal, CalculationMode.Ticks, PointsToTicks(StopLossPoints), false);
            else
                SetStopLoss(signal, CalculationMode.Price, pendingRejectionWickPrice, false);
            SetProfitTarget(signal, CalculationMode.Ticks, PointsToTicks(ProfitTargetPoints));

            if (DrawSignals)
                DrawEntrySignalMarker(StrategySignalPrefix + "Entry_" + CurrentBar + "_" + (pendingLong ? "Long" : "Short"));

            Log(string.Format("SIGNAL|direction={0}|price={1:F2}|trigger={2:F2}|stopMode={3}|stop={4:F2}|targetPoints={5:F2}|action=Enter{0}",
                pendingLong ? "Long" : "Short", Close[0], pendingTriggerPrice, StopMode,
                StopMode == StopLossMode.FixedPoints ? StopLossPoints : pendingRejectionWickPrice, ProfitTargetPoints));
            // Store the arm decision before the managed market order is
            // submitted. OnExecutionUpdate applies it only if that exact
            // Long/Short entry actually fills.
            PrepareRsiEarlyExitForEntry(pendingLong);

            if (pendingLong)
                EnterLong(Quantity, LongEntrySignal);
            else
                EnterShort(Quantity, ShortEntrySignal);

            ClearPendingSetup();
        }

        private void PrepareRsiEarlyExitForEntry(bool enteringLong)
        {
            ClearRsiEarlyExit();
            if (!UseRsiEarlyExit || rsiEarlyExit == null)
                return;

            // This is a give-back control, not an entry filter. It is armed
            // only after a filled entry when the already completed rejection
            // bar's RSI was on the favourable side of 50.
            rsiEarlyExitEntryPending = true;
            rsiEarlyExitEntryLong = enteringLong;
            rsiEarlyExitArmed = enteringLong ? rsiEarlyExit[1] > 50.0 : rsiEarlyExit[1] < 50.0;
            rsiEarlyExitEntryBar = CurrentBar;
        }

        private bool EvaluateRsiEarlyExit()
        {
            if (!UseRsiEarlyExit || rsiEarlyExit == null)
                return false;

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                // A managed market entry can be awaiting its execution
                // callback for this same tick. Keep the prepared arm state
                // until that callback confirms (or a later setup replaces it).
                if (rsiEarlyExitEntryPending)
                    return false;
                ClearRsiEarlyExit();
                return false;
            }

            // Calculate.OnEachTick must wait for the first tick of the next
            // bar so the prior RSI value is final. Historical Analyzer bars
            // are already complete at OnBarUpdate, so use their current value.
            if (State != State.Historical && !IsFirstTickOfBar)
                return false;

            int completedBarsAgo = State == State.Historical ? 0 : 1;
            if (!rsiEarlyExitArmed || CurrentBar - completedBarsAgo <= rsiEarlyExitEntryBar)
                return false;

            double value = rsiEarlyExit[completedBarsAgo];
            bool exitLong = Position.MarketPosition == MarketPosition.Long && value <= RsiEarlyExitLower;
            bool exitShort = Position.MarketPosition == MarketPosition.Short && value >= RsiEarlyExitUpper;
            if (!exitLong && !exitShort)
                return false;

            Log(string.Format("RSI_EXIT|direction={0}|rsi={1:F2}|period={2}|lower={3:F2}|upper={4:F2}",
                exitLong ? "Long" : "Short", value, RsiEarlyExitPeriod, RsiEarlyExitLower, RsiEarlyExitUpper));
            ClearRsiEarlyExit();
            ExitAllPositions("RsiExit");
            return true;
        }

        private void ClearRsiEarlyExit()
        {
            rsiEarlyExitArmed = false;
            rsiEarlyExitEntryPending = false;
            rsiEarlyExitEntryLong = false;
            rsiEarlyExitEntryBar = -1;
        }

        private bool EvaluateNegativeTimeExit()
        {
            if (!UseNegativeTimeExit || Position.MarketPosition == MarketPosition.Flat)
                return false;

            // Use only a completed bar. On a live OnEachTick strategy this is
            // the first tick of the next bar; in the Analyzer the current bar
            // is already complete.
            if (State != State.Historical && !IsFirstTickOfBar)
                return false;

            int completedBarsAgo = State == State.Historical ? 0 : 1;
            string entrySignal = Position.MarketPosition == MarketPosition.Long ? LongEntrySignal : ShortEntrySignal;
            int barsSinceEntry = BarsSinceEntryExecution(0, entrySignal, 0);
            if (barsSinceEntry < NegativeTimeExitBars)
                return false;

            double completedClose = Close[completedBarsAgo];
            bool stillNegative = Position.MarketPosition == MarketPosition.Long
                ? completedClose < Position.AveragePrice
                : completedClose > Position.AveragePrice;
            if (!stillNegative)
                return false;

            Log(string.Format("NEGATIVE_TIME_EXIT|direction={0}|completedBars={1}|close={2:F2}|entry={3:F2}",
                Position.MarketPosition, barsSinceEntry, completedClose, Position.AveragePrice));
            ExitAllPositions("NegativeTimeExit");
            return true;
        }

        private bool EvaluateNoFollowThroughExit()
        {
            if (!UseNoFollowThroughExit || Position.MarketPosition == MarketPosition.Flat)
                return false;
            if (State != State.Historical && !IsFirstTickOfBar)
                return false;

            string entrySignal = Position.MarketPosition == MarketPosition.Long ? LongEntrySignal : ShortEntrySignal;
            int barsSinceEntry = BarsSinceEntryExecution(0, entrySignal, 0);
            // The bar in which an intrabar entry occurred is not evidence of
            // follow-through. Wait until the requested number of subsequent
            // completed chart bars is available.
            if (barsSinceEntry <= NoFollowThroughBars)
                return false;

            int completedBarsAgo = State == State.Historical ? 0 : 1;
            double maximumFavourablePoints = 0.0;
            for (int barsAgo = completedBarsAgo; barsAgo < barsSinceEntry; barsAgo++)
            {
                double favourablePoints = Position.MarketPosition == MarketPosition.Long
                    ? High[barsAgo] - Position.AveragePrice
                    : Position.AveragePrice - Low[barsAgo];
                if (favourablePoints > maximumFavourablePoints)
                    maximumFavourablePoints = favourablePoints;
            }

            if (maximumFavourablePoints >= MinimumFollowThroughPoints)
                return false;

            Log(string.Format("NO_FOLLOW_THROUGH_EXIT|direction={0}|completedBars={1}|mfe={2:F2}|required={3:F2}",
                Position.MarketPosition, NoFollowThroughBars, maximumFavourablePoints, MinimumFollowThroughPoints));
            ExitAllPositions("NoFollowThroughExit");
            return true;
        }

        // Chart reload/re-enable display only. A 5-minute historical OHLC bar
        // cannot reproduce the intrabar order/fill sequence of a real trade, so
        // this path never calls EnterLong/EnterShort, never stages brackets, and
        // never contributes to Strategy Performance. It redraws only the same
        // qualifying signal marker on the bar where the trigger was reached.
        private void DrawHistoricalSignalMarkers()
        {
            if (!DrawHistoricalSignals || !configuredInstrumentIsValid)
                return;

            // Resolve an older visual entry before considering a new signal on
            // this completed bar. Its bracket is intentionally display-only.
            if (historicalVisualTradeOpen)
            {
                ResolveHistoricalVisualExit();
                if (historicalVisualTradeOpen)
                    return;
            }

            // Historical processing invokes this once per completed bar; do not
            // rely on IsFirstTickOfBar being populated there.
            if (State == State.Historical || IsFirstTickOfBar)
            {
                ClearPendingSetup();
                CreateSetupFromPriorBar();
            }

            if (pendingSetupBar != CurrentBar - 1 || IsSkippedTime(Time[0]))
                return;

            bool broken = pendingLong ? High[0] >= pendingTriggerPrice : Low[0] <= pendingTriggerPrice;
            if (!broken)
                return;

            DrawEntrySignalMarker(StrategySignalPrefix + "HistoricalSignal_" + CurrentBar + "_" + (pendingLong ? "Long" : "Short"));
            OpenHistoricalVisualTrade();
            ClearPendingSetup();
        }

        private void OpenHistoricalVisualTrade()
        {
            // A historical 5-minute OHLC bar cannot report the live market-fill
            // price, so use the first qualifying breakout level as a transparent
            // visual estimate. This state must never be used to submit orders.
            historicalVisualTradeOpen = true;
            historicalVisualTradeLong = pendingLong;
            historicalVisualTargetPrice = pendingLong
                ? pendingTriggerPrice + ProfitTargetPoints
                : pendingTriggerPrice - ProfitTargetPoints;
            historicalVisualStopPrice = StopMode == StopLossMode.FixedPoints
                ? (pendingLong ? pendingTriggerPrice - StopLossPoints : pendingTriggerPrice + StopLossPoints)
                : pendingRejectionWickPrice;
        }

        private void ResolveHistoricalVisualExit()
        {
            bool hitTarget = historicalVisualTradeLong
                ? High[0] >= historicalVisualTargetPrice
                : Low[0] <= historicalVisualTargetPrice;
            bool hitStop = historicalVisualTradeLong
                ? Low[0] <= historicalVisualStopPrice
                : High[0] >= historicalVisualStopPrice;

            if (!hitTarget && !hitStop)
                return;

            bool targetWins;
            if (hitTarget && hitStop)
            {
                // The intrabar sequence is unknowable from an OHLC bar. Use a
                // documented conventional path: bullish bars visit low before
                // high; bearish bars visit high before low. Tick Replay/live
                // execution markers remain the authoritative source.
                bool bullishBar = Close[0] >= Open[0];
                targetWins = historicalVisualTradeLong ? !bullishBar : bullishBar;
            }
            else
            {
                targetWins = hitTarget;
            }

            string tag = StrategySignalPrefix + "_Historical_" + (targetWins ? "TP_" : "SL_") + CurrentBar;
            if (targetWins)
                DrawExitMarker(tag, "◆ TP", 0, historicalVisualTargetPrice, Brushes.Gold);
            else
                DrawExitMarker(tag, "■ SL", 0, historicalVisualStopPrice, Brushes.Red);

            ClearHistoricalVisualTrade();
        }

        private void ClearHistoricalVisualTrade()
        {
            historicalVisualTradeOpen = false;
            historicalVisualStopPrice = 0.0;
            historicalVisualTargetPrice = 0.0;
        }

        private void DrawEntrySignalMarker(string tag)
        {
            if (pendingLong)
                Draw.ArrowUp(this, tag, false, 0, Low[0] - 2 * TickSize, Brushes.LimeGreen);
            else
                Draw.ArrowDown(this, tag, false, 0, High[0] + 2 * TickSize, Brushes.IndianRed);
        }

        private void DrawExitMarker(string tag, string text, int barsAgo, double price, Brush brush)
        {
            Draw.Text(this, tag, false, text, barsAgo, price, 0, brush,
                new SimpleFont("Segoe UI", 20) { Bold = true }, TextAlignment.Center,
                Brushes.Transparent, Brushes.Transparent, 0);
        }

        private void DrawExitMarker(string tag, string text, DateTime time, double price, Brush brush)
        {
            Draw.Text(this, tag, false, text, time, price, 0, brush,
                new SimpleFont("Segoe UI", 20) { Bold = true }, TextAlignment.Center,
                Brushes.Transparent, Brushes.Transparent, 0);
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null)
                return;

            Order ownedOrder = TranslateSafetyOrder(execution.Order);
            if (ownedOrder == null)
            {
                FailExitSafety("Execution has no usable strategy order; check the account manually.");
                return;
            }
            lock (exitSafetyLock)
            {
                SafetyOrderReference tracked = GetSafetyOrder(ownedOrder);
                exitSafety.RecordExecution(tracked.Token, executionId, Math.Abs(quantity), SafetyOrderSide(ownedOrder.OrderAction));
            }
            QueueExitSafetyProcessing();

            string name = execution.Name ?? string.Empty;
            bool filledExpectedLong = rsiEarlyExitEntryPending
                && rsiEarlyExitEntryLong
                && string.Equals(name, LongEntrySignal, StringComparison.OrdinalIgnoreCase);
            bool filledExpectedShort = rsiEarlyExitEntryPending
                && !rsiEarlyExitEntryLong
                && string.Equals(name, ShortEntrySignal, StringComparison.OrdinalIgnoreCase);
            if (filledExpectedLong || filledExpectedShort)
            {
                rsiEarlyExitEntryPending = false;
                rsiEarlyExitEntryBar = CurrentBar;
            }

            if (!DrawSignals)
                return;

            // These are execution markers, not inferred candle results. A gold
            // TP symbol means the managed profit target actually filled; a red
            // SL symbol means the managed stop loss actually filled.
            string uniqueId = string.IsNullOrEmpty(executionId)
                ? (string.IsNullOrEmpty(orderId) ? time.Ticks.ToString(CultureInfo.InvariantCulture) : orderId)
                : executionId;

            if (string.Equals(name, ProfitTargetExitOrderName, StringComparison.OrdinalIgnoreCase))
            {
                DrawExitMarker(StrategySignalPrefix + "_TP_" + uniqueId, "◆ TP", time, price, Brushes.Gold);
                Log(string.Format("EXIT|type=TP|price={0:F2}|quantity={1}", price, Math.Abs(quantity)));
            }
            else if (string.Equals(name, StopLossExitOrderName, StringComparison.OrdinalIgnoreCase))
            {
                DrawExitMarker(StrategySignalPrefix + "_SL_" + uniqueId, "■ SL", time, price, Brushes.Red);
                Log(string.Format("EXIT|type=SL|price={0:F2}|quantity={1}", price, Math.Abs(quantity)));
            }
        }

        private void ClearPendingSetup()
        {
            pendingSetupBar = -1;
            pendingTriggerPrice = 0;
            pendingRejectionWickPrice = 0;
        }

        // Mirrors EMAL's CME trading-day risk caps. The session begins at
        // 18:00 ET, and the first eligible tick's account net liquidation is
        // the baseline. This intentionally includes other account activity
        // and unrealized P&L, exactly as EMAL does.
        private DateTime GetTradingDay(DateTime easternTime)
        {
            return easternTime.Hour >= 18
                ? easternTime.Date.AddDays(1)
                : easternTime.Date;
        }

        private bool IsAccountDailyProfitBlocked()
        {
            if (MaxDailyProfit <= 0.0)
            {
                maxDailyProfitLimitReached = false;
                maxDailyProfitStartBalance = double.NaN;
                maxDailyProfitDate = DateTime.MinValue;
                return false;
            }

            DateTime tradingDay = GetTradingDay(ConvertToEastern(Time[0]));
            if (maxDailyProfitDate != tradingDay)
            {
                maxDailyProfitDate = tradingDay;
                maxDailyProfitLimitReached = false;
                maxDailyProfitStartBalance = double.NaN;
            }
            if (maxDailyProfitLimitReached)
                return true;

            double netLiquidation;
            if (!TryGetCurrentNetLiquidation(out netLiquidation))
                return false;
            if (double.IsNaN(maxDailyProfitStartBalance))
            {
                maxDailyProfitStartBalance = netLiquidation;
                return false;
            }

            double dailyProfit = netLiquidation - maxDailyProfitStartBalance;
            if (dailyProfit < MaxDailyProfit)
                return false;

            maxDailyProfitLimitReached = true;
            ClearPendingSetup();
            ExitAllPositions("MaxDailyProfit");
            Print(string.Format("{0} | max daily profit reached | startNetLiq={1:F2} netLiq={2:F2} profit={3:F2} target={4:F2} | entries blocked for {5:yyyy-MM-dd}; wait for SAFE_CLOSE_COMPLETE before disabling",
                Time[0], maxDailyProfitStartBalance, netLiquidation, dailyProfit, MaxDailyProfit, tradingDay));
            return true;
        }

        private bool IsAccountDailyLossBlocked()
        {
            if (MaxDailyLoss <= 0.0)
            {
                maxDailyLossLimitReached = false;
                maxDailyLossStartBalance = double.NaN;
                maxDailyLossDate = DateTime.MinValue;
                return false;
            }

            DateTime tradingDay = GetTradingDay(ConvertToEastern(Time[0]));
            if (maxDailyLossDate != tradingDay)
            {
                maxDailyLossDate = tradingDay;
                maxDailyLossLimitReached = false;
                maxDailyLossStartBalance = double.NaN;
            }
            if (maxDailyLossLimitReached)
                return true;

            double netLiquidation;
            if (!TryGetCurrentNetLiquidation(out netLiquidation))
                return false;
            if (double.IsNaN(maxDailyLossStartBalance))
            {
                maxDailyLossStartBalance = netLiquidation;
                return false;
            }

            double dailyPnl = netLiquidation - maxDailyLossStartBalance;
            if (dailyPnl > -MaxDailyLoss)
                return false;

            maxDailyLossLimitReached = true;
            ClearPendingSetup();
            ExitAllPositions("MaxDailyLoss");
            Print(string.Format("{0} | max daily loss reached | startNetLiq={1:F2} netLiq={2:F2} pnl={3:F2} limit=-{4:F2} | entries blocked for {5:yyyy-MM-dd}; wait for SAFE_CLOSE_COMPLETE before disabling",
                Time[0], maxDailyLossStartBalance, netLiquidation, dailyPnl, MaxDailyLoss, tradingDay));
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
                if (netLiquidation > 0.0 && !double.IsNaN(netLiquidation) && !double.IsInfinity(netLiquidation))
                    return true;

                double cash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                double unrealized = Position.MarketPosition != MarketPosition.Flat
                    ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0])
                    : 0.0;
                netLiquidation = cash + unrealized;
                return (cash > 0.0 || Position.MarketPosition != MarketPosition.Flat)
                    && !double.IsNaN(netLiquidation) && !double.IsInfinity(netLiquidation);
            }
            catch
            {
                return false;
            }
        }

        private bool IsHistoricalTradeSimulationContext()
        {
            // Strategy Analyzer uses NinjaTrader's synthetic Backtest account.
            // A live broker account, Sim101, or Playback account must only warm
            // up on historical bars; its first tradable setup is created live.
            return Account == null
                || string.Equals(Account.Name, "Backtest", StringComparison.OrdinalIgnoreCase);
        }

        private bool AllowsTradeDirection(bool isLong)
        {
            return TradeDirection == TradeDirectionFilter.Both
                || (TradeDirection == TradeDirectionFilter.LongsOnly && isLong)
                || (TradeDirection == TradeDirectionFilter.ShortsOnly && !isLong);
        }

        private bool AllowsPriorLeg(bool isLong)
        {
            if (!UsePriorLegFilter)
                return true;
            if (CurrentBar < PriorLegBars + 2)
                return false;

            double newestClose = Close[2];
            // Mirror the research definition: compare the last completed bar
            // before the rejection candle (Close[2]) with the close one bar
            // before the N-bar lookback begins.
            double oldestClose = Close[PriorLegBars + 2];
            int priorDirection = newestClose > oldestClose ? 1 : newestClose < oldestClose ? -1 : 0;
            int entryDirection = isLong ? 1 : -1;
            return PriorLegMustOpposeEntry
                ? priorDirection == -entryDirection
                : priorDirection == entryDirection;
        }

        private bool AllowsPriorSessionProfile(bool isLong)
        {
            if (PriorSessionProfileFilter == PriorSessionProfileMode.Disabled)
                return true;
            if (previousSessionProfile == null)
                return false;

            double price = Close[1];
            if (PriorSessionProfileFilter == PriorSessionProfileMode.PocDirectional)
                return isLong ? price >= previousSessionProfile.Poc : price <= previousSessionProfile.Poc;
            if (PriorSessionProfileFilter == PriorSessionProfileMode.OutsideValueArea)
                return isLong ? price >= previousSessionProfile.Vah : price <= previousSessionProfile.Val;
            return isLong ? price <= previousSessionProfile.Val : price >= previousSessionProfile.Vah;
        }

        private SessionProfile BuildSessionProfile(Dictionary<double, long> volumeAtPrice, int percentage)
        {
            var prices = new List<double>(volumeAtPrice.Keys);
            prices.Sort();
            double poc = prices[0];
            long pocVolume = volumeAtPrice[poc];
            long total = 0;
            foreach (KeyValuePair<double, long> level in volumeAtPrice)
            {
                total += level.Value;
                if (level.Value > pocVolume || (level.Value == pocVolume && level.Key < poc))
                {
                    poc = level.Key;
                    pocVolume = level.Value;
                }
            }

            int low = prices.IndexOf(poc);
            int high = low;
            long included = pocVolume;
            while (included * 100L < total * percentage && (low > 0 || high < prices.Count - 1))
            {
                long below = low > 0 ? volumeAtPrice[prices[low - 1]] : -1;
                long above = high < prices.Count - 1 ? volumeAtPrice[prices[high + 1]] : -1;
                if (above > below)
                {
                    high++;
                    included += above;
                }
                else
                {
                    low--;
                    included += below;
                }
            }
            return new SessionProfile(poc, prices[low], prices[high]);
        }

        private int PointsToTicks(double points)
        {
            return (int)Math.Round(points / TickSize, MidpointRounding.AwayFromZero);
        }

        private bool ValidatePointSettings()
        {
            return TickSize > 0
                && IsWholeTickAmount(MinimumWickPoints)
                && IsWholeTickAmountOrZero(MinimumBodyPoints)
                && IsWholeTickAmountOrZero(MaximumOppositeWickPoints)
                && IsWholeTickAmount(ProfitTargetPoints)
                && IsWholeTickAmount(StopLossPoints)
                && PriorLegBars >= 1
                && (!UseRsiEarlyExit || (RsiEarlyExitPeriod >= 2
                    && RsiEarlyExitLower >= 0.0
                    && RsiEarlyExitLower < 50.0
                    && RsiEarlyExitUpper > 50.0
                    && RsiEarlyExitUpper <= 100.0))
                && (!UseNegativeTimeExit || NegativeTimeExitBars >= 1)
                && (!UseNoFollowThroughExit || (NoFollowThroughBars >= 1
                    && IsWholeTickAmount(MinimumFollowThroughPoints)))
                && PriorSessionValueAreaPercent >= 50
                && PriorSessionValueAreaPercent <= 95;
        }

        private bool IsSkippedTime(DateTime time)
        {
            // Time[0] uses NinjaTrader's General Options display timezone.
            // Convert it for every bar so the configured slots continue to
            // match New York during the Sweden/US DST mismatch weeks too.
            int currentTime = ToTime(ConvertToEastern(time));
            return IsPropFirmEndOfDayEntryBlocked(currentTime)
                || (UseSkipTimeSlot1 && IsTimeInSlot(currentTime, ParseTime(SkipTimeSlot1Start), ParseTime(SkipTimeSlot1End)))
                || (UseSkipTimeSlot2 && IsTimeInSlot(currentTime, ParseTime(SkipTimeSlot2Start), ParseTime(SkipTimeSlot2End)))
                || (UseSkipTimeSlot3 && IsTimeInSlot(currentTime, ParseTime(SkipTimeSlot3Start), ParseTime(SkipTimeSlot3End)));
        }

        private bool IsPropFirmEndOfDayEntryBlocked(int currentTime)
        {
            return IsTimeInSlot(currentTime, ParseTime(PropFirmEntryCutoffTime), ParseTime(PropFirmSafetyEndTime));
        }

        private bool EvaluatePropFirmEndOfDaySafety(DateTime easternTime)
        {
            int currentTime = ToTime(easternTime);
            int cutoffTime = ParseTime(PropFirmEntryCutoffTime);
            int forceFlatTime = ParseTime(PropFirmForceFlatTime);
            int safetyEndTime = ParseTime(PropFirmSafetyEndTime);
            bool inSafetyWindow = IsTimeInSlot(currentTime, cutoffTime, safetyEndTime);
            if (!inSafetyWindow)
            {
                propFirmEndOfDayExitSubmitted = false;
                return false;
            }

            if (currentTime < forceFlatTime || propFirmEndOfDayExitSubmitted || Position.MarketPosition == MarketPosition.Flat)
                return false;

            propFirmEndOfDayExitSubmitted = true;
            Log(string.Format("PROP_FIRM_EOD_EXIT|time={0:HH:mm:ss}|forceFlat={1}|position={2}",
                easternTime, PropFirmForceFlatTime, Position.MarketPosition));
            ExitAllPositions("PropFirmEndOfDay");
            return true;
        }

        // BackBrushes colors each individual bar, rather than repainting the
        // whole chart panel. This makes the historical skip windows visible
        // after reload as well as showing the state of the current live bar.
        private void UpdateTradingWindowBackground()
        {
            if (!ShowTradingWindowBackground)
            {
                BackBrushes[0] = null;
                return;
            }

            // This intentionally represents the time schedule only. Green
            // means this bar is eligible for a new entry in time; it does not
            // promise a setup because the wick, risk, daily-cap, and research
            // filters still apply. Red marks one of the enabled skip windows.
            bool isBlocked = !configuredInstrumentIsValid || IsSkippedTime(Time[0]);
            BackBrushes[0] = isBlocked ? tradingWindowBlockedBrush : tradingWindowOpenBrush;
        }

        private Brush CreateFrozenBrush(byte alpha, byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        private void SetupTimeZones()
        {
            try
            {
                platformZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
                easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                Print(StrategySignalPrefix + "|TIMEZONE|platform="
                    + (platformZone == null ? "unknown" : platformZone.Id)
                    + "|slots=NewYork");
            }
            catch (Exception ex)
            {
                // Keep the strategy usable if NinjaTrader cannot supply a
                // timezone, but make the reduced guarantee explicit.
                platformZone = null;
                easternZone = null;
                Print(StrategySignalPrefix + "|TIMEZONE_WARNING|using_platform_time|" + ex.Message);
            }
        }

        private DateTime ConvertToEastern(DateTime platformTime)
        {
            if (easternZone == null || platformZone == null || platformZone.Equals(easternZone))
                return platformTime;

            try
            {
                return TimeZoneInfo.ConvertTime(
                    DateTime.SpecifyKind(platformTime, DateTimeKind.Unspecified),
                    platformZone,
                    easternZone);
            }
            catch (Exception)
            {
                return platformTime;
            }
        }

        private bool ValidateTimeSettings()
        {
            int ignored;
            return (!UseSkipTimeSlot1 || (TryParseTime(SkipTimeSlot1Start, out ignored) && TryParseTime(SkipTimeSlot1End, out ignored)))
                && (!UseSkipTimeSlot2 || (TryParseTime(SkipTimeSlot2Start, out ignored) && TryParseTime(SkipTimeSlot2End, out ignored)))
                && (!UseSkipTimeSlot3 || (TryParseTime(SkipTimeSlot3Start, out ignored) && TryParseTime(SkipTimeSlot3End, out ignored)))
                && TryParseTime(PropFirmEntryCutoffTime, out ignored)
                && TryParseTime(PropFirmForceFlatTime, out ignored)
                && TryParseTime(PropFirmSafetyEndTime, out ignored);
        }

        private int ParseTime(string text)
        {
            int value;
            if (!TryParseTime(text, out value))
                throw new ArgumentException("Skip times must use HH:mm:ss.");
            return value;
        }

        private bool TryParseTime(string text, out int value)
        {
            TimeSpan time;
            if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out time) || time < TimeSpan.Zero || time >= TimeSpan.FromDays(1))
            {
                value = 0;
                return false;
            }
            value = time.Hours * 10000 + time.Minutes * 100 + time.Seconds;
            return true;
        }

        private bool IsTimeInSlot(int currentTime, int startTime, int endTime)
        {
            return startTime <= endTime
                ? currentTime >= startTime && currentTime <= endTime
                : currentTime >= startTime || currentTime <= endTime;
        }

        private bool IsWholeTickAmount(double points)
        {
            return points > 0 && Math.Abs(points / TickSize - Math.Round(points / TickSize)) < 0.000001;
        }

        private bool IsWholeTickAmountOrZero(double points)
        {
            return points >= 0 && Math.Abs(points / TickSize - Math.Round(points / TickSize)) < 0.000001;
        }

        private bool ValidateRequiredPrimaryInstrument()
        {
            string name = Instrument == null || Instrument.MasterInstrument == null ? string.Empty : Instrument.MasterInstrument.Name;
            return string.Equals(name, "NQ", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "MNQ", StringComparison.OrdinalIgnoreCase);
        }

        private void ShowConfigurationWarningOnce()
        {
            if (configurationWarningShown)
                return;

            configurationWarningShown = true;
            Print("WickRejectionBreakout is disabled: it requires an NQ or MNQ chart, whole-tick point settings, and valid HH:mm:ss skip times.");
        }

        private void ExitAllPositions(string reason)
        {
            ClearPendingSetup();
            lock (exitSafetyLock)
                exitSafety.RequestClose(reason);
            Log("SAFE_CLOSE_REQUEST|reason=" + reason);
            ProcessExitSafety(true);
        }

        private bool CanEnterAfterSafety()
        {
            lock (exitSafetyLock)
                return exitSafety.CanEnter;
        }

        private void CancelRedTimeBlockOrders()
        {
            List<Order> owned = new List<Order>();
            lock (exitSafetyLock)
                foreach (SafetyOrderReference tracked in safetyOrders)
                    if (tracked.Reference != null && !tracked.Reference.IsBacktestOrder
                        && IsSafetyCancelAllowed(tracked.Reference.OrderState, tracked.Reference.OrderType))
                        owned.Add(tracked.Reference);
            if (State != State.Realtime || owned.Count == 0)
                return;
            try
            {
                if (Account == null)
                    throw new InvalidOperationException("No account for red-zone order cancellation.");
                Log("TIME_BLOCK_CANCEL|ownedOrders=" + owned.Count);
                Account.Cancel(owned);
            }
            catch (Exception ex)
            {
                FailExitSafety("Red-zone cancellation failed: " + ex.Message);
            }
        }

        private void QueueExitSafetyProcessing()
        {
            lock (exitSafetyLock)
            {
                if (!exitSafetyReady || exitSafetyStopped || exitSafetyQueued)
                    return;
                exitSafetyQueued = true;
            }
            try
            {
                TriggerCustomEvent(state =>
                {
                    lock (exitSafetyLock)
                        exitSafetyQueued = false;
                    ProcessExitSafety(true);
                }, null);
            }
            catch (Exception ex)
            {
                lock (exitSafetyLock)
                    exitSafetyQueued = false;
                FailExitSafety("Cannot schedule order reconciliation: " + ex.Message);
            }
        }

        private bool ProcessExitSafety(bool force)
        {
            lock (exitSafetyLock)
            {
                if (!exitSafetyReady || exitSafetyStopped)
                    return false;
                if (exitSafetyProcessing)
                    return exitSafety.Closing || exitSafety.Fault != null;
                if (!force && !exitSafety.Closing && exitSafety.Fault == null && !exitSafety.HasActiveExits)
                    return false;
                exitSafetyProcessing = true;
            }
            try
            {
                int strategyPosition = SignedPosition(Position.MarketPosition, Position.Quantity);
                lock (exitSafetyLock)
                    if (!exitSafety.Closing && exitSafety.Fault == null && strategyPosition != 0)
                        return false;
                int? accountPosition = GetSafetyAccountPosition(strategyPosition);
                WickExitAction action;
                bool closing;
                string reason;
                lock (exitSafetyLock)
                {
                    if (!force && lastSafetyVersion == exitSafety.Version
                        && lastSafetyStrategyPosition == strategyPosition && lastSafetyAccountPosition == accountPosition)
                        return exitSafety.Closing || exitSafety.Fault != null;
                    if (lastSafetyVersion != exitSafety.Version)
                        safetyProgressUtc = DateTime.UtcNow;
                    lastSafetyVersion = exitSafety.Version;
                    lastSafetyStrategyPosition = strategyPosition;
                    lastSafetyAccountPosition = accountPosition;
                    action = exitSafety.Next(strategyPosition, accountPosition);
                    closing = exitSafety.Closing;
                    reason = exitSafety.Reason;
                    if (exitSafetyTimer != null)
                        exitSafetyTimer.Change(closing ? 100 : System.Threading.Timeout.Infinite,
                            closing ? 100 : System.Threading.Timeout.Infinite);
                }

                if (action.Kind == WickExitActionKind.Cancel)
                {
                    Order target = null;
                    lock (exitSafetyLock)
                        foreach (SafetyOrderReference tracked in safetyOrders)
                            if (tracked.Token == action.OrderToken)
                                target = tracked.Reference;
                    if (target == null || (State == State.Realtime && target.IsBacktestOrder))
                        throw new InvalidOperationException("Cancellation has no realtime strategy order.");
                    if (!IsSafetyCancelAllowed(target.OrderState, target.OrderType))
                    {
                        lock (exitSafetyLock)
                            exitSafety.DeferCancellation(action.OrderToken);
                    }
                    else
                    {
                        Log("SAFE_CLOSE_CANCEL|reason=" + reason + "|id=" + target.OrderId + "|name=" + target.Name);
                        if (redTimeBlockCloseActive && State == State.Realtime)
                        {
                            if (Account == null)
                                throw new InvalidOperationException("No account for red-zone order cancellation.");
                            Account.Cancel(new[] { target });
                        }
                        else
                            CancelOrder(target);
                        QueueExitSafetyProcessing();
                    }
                }
                else if (action.Kind == WickExitActionKind.MarketExit)
                {
                    int remaining = Math.Abs(action.SignedQuantity);
                    Log("SAFE_CLOSE_MARKET|reason=" + reason + "|side="
                        + (action.SignedQuantity > 0 ? "Long" : "Short") + "|quantity=" + remaining);
                    // This reservation is reached only after all brackets and their fills have settled.
                    if (action.SignedQuantity > 0)
                        ExitLong(0, remaining, FlattenExitSignal, string.Empty);
                    else
                        ExitShort(0, remaining, FlattenExitSignal, string.Empty);
                }
                else if (action.Kind == WickExitActionKind.Complete)
                {
                    redTimeBlockCloseActive = false;
                    Log("SAFE_CLOSE_COMPLETE|reason=" + reason + "|ownedOrdersTerminal=true|strategyFlat=true");
                }

                if (closing && DateTime.UtcNow - safetyProgressUtc >= TimeSpan.FromSeconds(5)
                    && DateTime.UtcNow - safetyWarningUtc >= TimeSpan.FromSeconds(5))
                {
                    safetyWarningUtc = DateTime.UtcNow;
                    string status;
                    lock (exitSafetyLock)
                        status = "reason=" + reason + "|owned=" + exitSafety.OwnedPosition
                            + "|strategy=" + strategyPosition + "|account=" + (accountPosition.HasValue ? accountPosition.ToString() : "unavailable")
                            + "|activeOrders=" + exitSafety.ActiveOrders + "|fault=" + (exitSafety.Fault ?? "none");
                    Print(StrategySignalPrefix + "|SAFE_CLOSE_WAIT|Check account/orders if this persists|" + status);
                }
                lock (exitSafetyLock)
                    return exitSafety.Closing || exitSafety.Fault != null;
            }
            catch (Exception ex)
            {
                FailExitSafety("Order reconciliation failed: " + ex.Message);
                return true;
            }
            finally
            {
                lock (exitSafetyLock)
                    exitSafetyProcessing = false;
            }
        }

        private void FailExitSafety(string message)
        {
            lock (exitSafetyLock)
                exitSafety.Fail(message);
            Print(StrategySignalPrefix + "|SAFE_CLOSE_ERROR|" + message);
        }

        private int? GetSafetyAccountPosition(int strategyPosition)
        {
            if (State == State.Historical && IsHistoricalTradeSimulationContext())
                return strategyPosition;
            if (Account == null || Instrument == null)
                return null;
            try
            {
                lock (Account.Positions)
                    foreach (Position position in Account.Positions)
                        if (position.Instrument != null && string.Equals(position.Instrument.FullName,
                            Instrument.FullName, StringComparison.OrdinalIgnoreCase))
                        {
                            safetyAccountReadWarning = false;
                            return SignedPosition(position.MarketPosition, position.Quantity);
                        }
                safetyAccountReadWarning = false;
                return 0;
            }
            catch (Exception ex)
            {
                if (!safetyAccountReadWarning)
                    Print(StrategySignalPrefix + "|SAFE_CLOSE_ACCOUNT_UNAVAILABLE|Waiting without a market exit|" + ex.Message);
                safetyAccountReadWarning = true;
                return null;
            }
        }

        private Order TranslateSafetyOrder(Order order)
        {
            if (order == null || State != State.Realtime || !order.IsBacktestOrder)
                return order;
            Order realtime = GetRealtimeOrder(order);
            lock (exitSafetyLock)
                foreach (SafetyOrderReference tracked in safetyOrders)
                    if (object.ReferenceEquals(tracked.Reference, order))
                    {
                        tracked.Reference = realtime;
                        tracked.OrderId = realtime == null ? string.Empty : realtime.OrderId;
                        if (realtime == null)
                            exitSafety.RemoveHistoricalOrder(tracked.Token);
                    }
            return realtime;
        }

        private void TransitionSafetyOrdersToRealtime()
        {
            List<Order> historical = new List<Order>();
            lock (exitSafetyLock)
                foreach (SafetyOrderReference tracked in safetyOrders)
                    if (tracked.Reference != null && tracked.Reference.IsBacktestOrder)
                        historical.Add(tracked.Reference);
            foreach (Order order in historical)
                TranslateSafetyOrder(order);
        }

        private SafetyOrderReference GetSafetyOrder(Order order)
        {
            foreach (SafetyOrderReference tracked in safetyOrders)
                if (object.ReferenceEquals(tracked.Reference, order)
                    || (!string.IsNullOrEmpty(order.OrderId) && tracked.OrderId == order.OrderId))
                {
                    tracked.Reference = order;
                    tracked.OrderId = order.OrderId;
                    return tracked;
                }
            SafetyOrderReference created = new SafetyOrderReference
            {
                Token = ++nextSafetyOrderToken, Reference = order, OrderId = order.OrderId
            };
            safetyOrders.Add(created);
            exitSafety.TrackOrder(created.Token, GetSafetyOrderKind(order), WickExitOrderState.Waiting, 0, 0, false);
            return created;
        }

        private static WickExitOrderKind GetSafetyOrderKind(Order order)
        {
            if (order.Name == ProfitTargetExitOrderName) return WickExitOrderKind.Target;
            if (order.Name == StopLossExitOrderName) return WickExitOrderKind.Stop;
            if (order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort)
                return WickExitOrderKind.Entry;
            return order.OrderType == OrderType.Market ? WickExitOrderKind.MarketExit : WickExitOrderKind.OtherExit;
        }

        private static WickExitOrderState GetSafetyOrderState(OrderState state)
        {
            if (state == OrderState.Filled) return WickExitOrderState.Filled;
            if (state == OrderState.Cancelled) return WickExitOrderState.Cancelled;
            if (state == OrderState.Rejected) return WickExitOrderState.Rejected;
            if (state == OrderState.CancelPending || state == OrderState.CancelSubmitted) return WickExitOrderState.Cancelling;
            if (state == OrderState.ChangePending || state == OrderState.ChangeSubmitted) return WickExitOrderState.Changing;
            if (state == OrderState.Accepted || state == OrderState.Working || state == OrderState.PartFilled)
                return WickExitOrderState.Working;
            return WickExitOrderState.Waiting;
        }

        private static bool IsSafetyCancelAllowed(OrderState state, OrderType type)
        {
            return type != OrderType.Market
                && (state == OrderState.Accepted || state == OrderState.Working || state == OrderState.PartFilled);
        }

        private static int SignedPosition(MarketPosition side, int quantity)
        {
            return side == MarketPosition.Long ? Math.Abs(quantity) : side == MarketPosition.Short ? -Math.Abs(quantity) : 0;
        }

        private static int SafetyOrderSide(OrderAction action)
        {
            return action == OrderAction.Buy || action == OrderAction.BuyToCover ? 1 : -1;
        }

        private sealed class SafetyOrderReference
        {
            internal int Token;
            internal string OrderId;
            internal Order Reference;
        }

        private void Log(string message)
        {
            DateTime timestamp = DateTime.Now;
            try
            {
                if (CurrentBar >= 0)
                    timestamp = Time[0];
            }
            catch (Exception)
            {
                // A diagnostic must never interfere with order management.
            }
            Log(message, timestamp);
        }

        private void Log(string message, DateTime timestamp)
        {
            // Do not flood the Output window when re-enabling a chart strategy
            // redraws visual history. Analyzer diagnostics remain available.
            if (EnableDiagnosticLogging && (State != State.Historical || IsHistoricalTradeSimulationContext()))
            {
                DateTime easternTime = ConvertToEastern(timestamp);
                Print(StrategySignalPrefix + "|timeET="
                    + easternTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + "|" + message);
            }
        }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", GroupName = "Parameters", Order = 0)]
        public int Quantity { get; set; }

        [NinjaScriptProperty]
        [Range(0.000001, 10000)]
        [Display(Name = "Minimum wick points", GroupName = "Setup", Order = 10)]
        public double MinimumWickPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Minimum body points", GroupName = "Setup", Order = 11)]
        public double MinimumBodyPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Maximum opposite wick points", Description = "0 disables this filter. A positive value rejects a setup when the wick opposite the rejection direction is larger.", GroupName = "Setup", Order = 12)]
        public double MaximumOppositeWickPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Breakout level", GroupName = "Setup", Order = 13)]
        public ConfirmationLevel BreakoutLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade direction", Description = "Research candidate. Both leaves the original long/short behavior unchanged.", GroupName = "Research filters", Order = 10)]
        public TradeDirectionFilter TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use prior leg filter", Description = "Research candidate. Requires the close-to-close leg before the rejection candle to agree with or oppose the proposed entry direction.", GroupName = "Research filters", Order = 11)]
        public bool UsePriorLegFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 120)]
        [Display(Name = "Prior leg bars", GroupName = "Research filters", Order = 12)]
        public int PriorLegBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prior leg must oppose entry", GroupName = "Research filters", Order = 13)]
        public bool PriorLegMustOpposeEntry { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prior session profile filter", Description = "Requires raw Last trade data. Builds POC and value area from the completed prior 18:00--17:00 New York futures session. Disabled preserves original behavior.", GroupName = "Research filters", Order = 14)]
        public PriorSessionProfileMode PriorSessionProfileFilter { get; set; }

        [NinjaScriptProperty]
        [Range(50, 95)]
        [Display(Name = "Prior session value area percent", Description = "Used by Outside value area and Reversal at value edge modes. The current research candidate is 80.", GroupName = "Research filters", Order = 15)]
        public int PriorSessionValueAreaPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use skip time slot 1", GroupName = "Time filter", Order = 14)]
        public bool UseSkipTimeSlot1 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip time slot 1 start", Description = "New York time in HH:mm:ss. Automatically follows EST/EDT. The default 20:00:00--00:59:59 slot crosses midnight.", GroupName = "Time filter", Order = 15)]
        public string SkipTimeSlot1Start { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip time slot 1 end", Description = "New York time in HH:mm:ss. Automatically follows EST/EDT.", GroupName = "Time filter", Order = 16)]
        public string SkipTimeSlot1End { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use skip time slot 2", GroupName = "Time filter", Order = 17)]
        public bool UseSkipTimeSlot2 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip time slot 2 start", Description = "New York time in HH:mm:ss. Automatically follows EST/EDT.", GroupName = "Time filter", Order = 18)]
        public string SkipTimeSlot2Start { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip time slot 2 end", Description = "New York time in HH:mm:ss. Automatically follows EST/EDT.", GroupName = "Time filter", Order = 19)]
        public string SkipTimeSlot2End { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use skip time slot 3", Description = "Optional research candidate. Default off until NT8 Playback is compared.", GroupName = "Time filter", Order = 20)]
        public bool UseSkipTimeSlot3 { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip time slot 3 start", Description = "New York time in HH:mm:ss. Automatically follows EST/EDT.", GroupName = "Time filter", Order = 21)]
        public string SkipTimeSlot3Start { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip time slot 3 end", Description = "New York time in HH:mm:ss. Automatically follows EST/EDT.", GroupName = "Time filter", Order = 22)]
        public string SkipTimeSlot3End { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prop-firm entry cutoff", Description = "New York time in HH:mm:ss. End-of-day safety is mandatory: no new entry may be submitted at or after this time.", GroupName = "Time filter", Order = 23)]
        public string PropFirmEntryCutoffTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prop-firm force-flat time", Description = "New York time in HH:mm:ss. End-of-day safety is mandatory: on the first live Last tick at or after this time, the strategy exits its own open position before the prop firm can liquidate it.", GroupName = "Time filter", Order = 24)]
        public string PropFirmForceFlatTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Prop-firm safety end", Description = "New York time in HH:mm:ss. The mandatory protected no-entry window ends here. Keep this after the prop firm's liquidation time.", GroupName = "Time filter", Order = 25)]
        public string PropFirmSafetyEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(0.000001, 10000)]
        [Display(Name = "Profit target points", GroupName = "Risk", Order = 30)]
        public double ProfitTargetPoints { get; set; }

        [NinjaScriptProperty]
        [Range(0.000001, 10000)]
        [Display(Name = "Stop loss points", Description = "Used only when Stop loss mode is Fixed points.", GroupName = "Risk", Order = 31)]
        public double StopLossPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop loss mode", Description = "Fixed points uses the configured stop distance. Rejection wick places the stop at the qualifying lower wick for longs or upper wick for shorts.", GroupName = "Risk", Order = 32)]
        public StopLossMode StopMode { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max daily profit", Description = "Maximum account profit in dollars from the first observed net liquidation of each 18:00 ET CME trading day. Unrealized P&L counts. Reaching it flattens and blocks this strategy until the next 18:00 ET reset. 0 disables.", GroupName = "Risk", Order = 33)]
        public double MaxDailyProfit { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max daily loss", Description = "Maximum account loss in dollars from the first observed net liquidation of each 18:00 ET CME trading day. Unrealized P&L counts. Reaching it flattens and blocks this strategy until the next 18:00 ET reset. 0 disables.", GroupName = "Risk", Order = 34)]
        public double MaxDailyLoss { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use RSI early exit", Description = "After a trade begins with the completed rejection bar's RSI on the favourable side of 50, exits at the next available tick after a later completed bar closes through the configured RSI threshold.", GroupName = "Risk", Order = 35)]
        public bool UseRsiEarlyExit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 200)]
        [Display(Name = "RSI early-exit period", Description = "TraderPro research candidate: 23. RSI smoothing is fixed at 1 to match the research calculation.", GroupName = "Risk", Order = 36)]
        public int RsiEarlyExitPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(50.000001, 100.0)]
        [Display(Name = "RSI early-exit upper", Description = "Exits an armed short when a completed RSI value reaches or exceeds this level. Research candidate: 58.5.", GroupName = "Risk", Order = 37)]
        public double RsiEarlyExitUpper { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 49.999999)]
        [Display(Name = "RSI early-exit lower", Description = "Exits an armed long when a completed RSI value reaches or falls below this level. Research candidate: 41.5.", GroupName = "Risk", Order = 38)]
        public double RsiEarlyExitLower { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use slow negative exit", Description = "Research A/B: exits only when this many completed chart bars have elapsed since entry and the completed close remains adverse to the entry price. Disabled preserves the baseline.", GroupName = "Risk", Order = 39)]
        public bool UseNegativeTimeExit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Slow negative exit bars", Description = "Completed chart bars to wait before the slow-negative exit can act. On a 5-minute chart, 2 means 10 minutes.", GroupName = "Risk", Order = 40)]
        public int NegativeTimeExitBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use no-follow-through exit", Description = "Research A/B: exits when the requested number of completed post-entry bars fail to reach the required favourable excursion. Disabled preserves the baseline.", GroupName = "Risk", Order = 41)]
        public bool UseNoFollowThroughExit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "No-follow-through bars", Description = "Completed bars after the entry bar allowed to demonstrate follow-through. 4 is the 2-minute research candidate.", GroupName = "Risk", Order = 42)]
        public int NoFollowThroughBars { get; set; }

        [NinjaScriptProperty]
        [Range(0.000001, 10000)]
        [Display(Name = "Minimum follow-through points", Description = "Minimum favourable excursion required within the completed no-follow-through window. 4 is the 2-minute research candidate.", GroupName = "Risk", Order = 43)]
        public double MinimumFollowThroughPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show trading-window background", Description = "Light green marks times when the strategy may seek a new entry. Light red marks an enabled skip-time break. Colors show the time schedule only; other filters can still reject a setup.", GroupName = "Visual", Order = 30)]
        public bool ShowTradingWindowBackground { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw entry arrows", GroupName = "Visual", Order = 31)]
        public bool DrawSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw historical signal arrows", Description = "Redraws qualifying past signals and estimated TP/SL outcome markers after a chart strategy is enabled. They are visual only, not reconstructed orders, fills, or Strategy Performance trades. Live execution markers are authoritative.", GroupName = "Visual", Order = 32)]
        public bool DrawHistoricalSignals { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable diagnostic logging", GroupName = "Visual", Order = 33)]
        public bool EnableDiagnosticLogging { get; set; }

        private sealed class SessionProfile
        {
            public SessionProfile(double poc, double val, double vah)
            {
                Poc = poc;
                Val = val;
                Vah = vah;
            }

            public double Poc { get; private set; }
            public double Val { get; private set; }
            public double Vah { get; private set; }
        }
    }
#endif

    internal enum WickExitOrderKind { Entry, Target, Stop, MarketExit, OtherExit }
    internal enum WickExitOrderState { Waiting, Working, Changing, Cancelling, Filled, Cancelled, Rejected }
    internal enum WickExitActionKind { None, Cancel, MarketExit, Complete }

    internal sealed class WickExitAction
    {
        internal WickExitActionKind Kind;
        internal int OrderToken;
        internal int SignedQuantity;
    }

    internal sealed class WickExitSafetyCoordinator
    {
        private sealed class TrackedOrder
        {
            internal int Epoch;
            internal WickExitOrderKind Kind;
            internal WickExitOrderState State;
            internal int Quantity;
            internal int ReportedFilled;
            internal int Executed;
            internal bool CanCancel;
            internal bool CancelRequested;
        }

        private readonly Dictionary<int, TrackedOrder> orders = new Dictionary<int, TrackedOrder>();
        private readonly HashSet<string> executionIds = new HashSet<string>(StringComparer.Ordinal);
        private int epoch;
        private bool targetSeen;
        private bool stopSeen;
        private bool entryReserved;
        private bool marketReserved;
        private bool entryFilledThisEpoch;

        internal int OwnedPosition { get; private set; }
        internal int ActiveOrders { get; private set; }
        internal bool HasActiveExits { get; private set; }
        internal bool HasUnprocessedFills { get; private set; }
        internal bool Closing { get; private set; }
        internal string Reason { get; private set; }
        internal string Fault { get; private set; }
        internal long Version { get; private set; }
        internal bool CanEnter
        {
            get { return !Closing && Fault == null && !entryReserved && OwnedPosition == 0
                    && ActiveOrders == 0 && !HasUnprocessedFills
                    && (!entryFilledThisEpoch || (targetSeen && stopSeen)); }
        }

        internal void BeginEntry()
        {
            if (!CanEnter)
                throw new InvalidOperationException("Prior strategy orders have not settled.");
            epoch++;
            targetSeen = stopSeen = false;
            entryReserved = true;
            marketReserved = false;
            entryFilledThisEpoch = false;
            Version++;
        }

        internal void RequestClose(string reason)
        {
            if (Closing)
                return;
            Closing = true;
            Reason = reason;
            marketReserved = false;
            Version++;
        }

        internal void Fail(string reason)
        {
            Fault = reason;
            RequestClose(reason);
            Version++;
        }

        internal void TrackOrder(int token, WickExitOrderKind kind, WickExitOrderState state,
            int quantity, int filled, bool canCancel)
        {
            TrackedOrder tracked;
            if (!orders.TryGetValue(token, out tracked))
            {
                tracked = new TrackedOrder { Epoch = epoch, Kind = kind };
                orders.Add(token, tracked);
            }
            tracked.Kind = kind;
            bool staleTerminalRegression = IsTerminal(tracked.State) && !IsTerminal(state)
                && filled <= tracked.ReportedFilled && quantity <= tracked.Quantity;
            if (!staleTerminalRegression)
                tracked.State = state;
            tracked.Quantity = Math.Max(tracked.Quantity, quantity);
            tracked.ReportedFilled = Math.Max(tracked.ReportedFilled, filled);
            tracked.CanCancel = canCancel;
            if (kind == WickExitOrderKind.Entry)
                entryReserved = false;
            if (tracked.Epoch == epoch)
            {
                targetSeen |= kind == WickExitOrderKind.Target;
                stopSeen |= kind == WickExitOrderKind.Stop;
            }
            if (kind == WickExitOrderKind.MarketExit
                && (state == WickExitOrderState.Cancelled || state == WickExitOrderState.Rejected))
                Fail("Market close was cancelled or rejected; manual review required.");
            Version++;
            RefreshFlags();
        }

        internal void RemoveHistoricalOrder(int token)
        {
            orders.Remove(token);
            Version++;
            RefreshFlags();
        }

        internal void DeferCancellation(int token)
        {
            TrackedOrder tracked = orders[token];
            tracked.CancelRequested = false;
            tracked.CanCancel = false;
            Version++;
        }

        internal void RecordExecution(int token, string executionId, int quantity, int side)
        {
            if (quantity <= 0 || (side != 1 && side != -1))
                throw new ArgumentOutOfRangeException("quantity", "Invalid execution quantity/direction.");
            TrackedOrder tracked;
            if (!orders.TryGetValue(token, out tracked))
                throw new InvalidOperationException("Execution has no owned order.");
            if (string.IsNullOrEmpty(executionId))
                Fail("Execution has no unique id; manual review required.");
            else if (!executionIds.Add(executionId))
                return;
            int before = OwnedPosition;
            if (tracked.Kind == WickExitOrderKind.Entry && tracked.Epoch == epoch)
                entryFilledThisEpoch = true;
            tracked.Executed += quantity;
            tracked.ReportedFilled = Math.Max(tracked.ReportedFilled, tracked.Executed);
            if (tracked.Quantity > 0 && tracked.Executed >= tracked.Quantity)
                tracked.State = WickExitOrderState.Filled;
            OwnedPosition += side * quantity;
            if (tracked.Kind != WickExitOrderKind.Entry
                && ((before == 0 && OwnedPosition != 0) || before * (long)OwnedPosition < 0))
            {
                RequestClose("UnexpectedExitFill");
                marketReserved = false;
            }
            Version++;
            RefreshFlags();
        }

        internal WickExitAction Next(int strategyPosition, int? accountPosition)
        {
            if (!Closing && strategyPosition == 0 && OwnedPosition == 0
                && (HasActiveExits || (entryFilledThisEpoch && (!targetSeen || !stopSeen)))
                && !entryReserved && !HasActive(WickExitOrderKind.Entry) && !HasUnprocessedFills)
                RequestClose("ResidualExitOrders");
            if (!Closing)
                return None();

            WickExitAction cancel = CancelFirst(WickExitOrderKind.Entry);
            if (cancel != null) return cancel;
            if (entryReserved || HasActive(WickExitOrderKind.Entry))
                return None();

            cancel = CancelFirst(WickExitOrderKind.Target, WickExitOrderKind.OtherExit);
            if (cancel != null) return cancel;
            if (HasActive(WickExitOrderKind.Target, WickExitOrderKind.OtherExit) || HasUnprocessedFills)
                return None();

            if (strategyPosition != OwnedPosition)
                return None();
            if (OwnedPosition == 0)
            {
                if (entryFilledThisEpoch && (!targetSeen || !stopSeen))
                    return None();
                cancel = CancelFirst(WickExitOrderKind.Stop);
                if (cancel != null) return cancel;
                if (ActiveOrders != 0)
                    return None();
                Closing = false;
                marketReserved = false;
                Version++;
                return new WickExitAction { Kind = WickExitActionKind.Complete };
            }

            bool accountAllowsExit = accountPosition.HasValue
                && Math.Sign(accountPosition.Value) == Math.Sign(OwnedPosition)
                && Math.Abs((long)accountPosition.Value) >= Math.Abs((long)OwnedPosition);
            if (!accountAllowsExit)
            {
                // Our stale exits must not open a new account position after an external flatten.
                if (!accountPosition.HasValue)
                    return None();
                cancel = CancelFirst(WickExitOrderKind.Stop);
                return cancel ?? None();
            }
            if (!targetSeen || !stopSeen)
                return None();
            cancel = CancelFirst(WickExitOrderKind.Stop);
            if (cancel != null) return cancel;
            if (ActiveOrders != 0 || HasUnprocessedFills || marketReserved || Fault != null)
                return None();
            marketReserved = true;
            Version++;
            return new WickExitAction { Kind = WickExitActionKind.MarketExit, SignedQuantity = OwnedPosition };
        }

        private WickExitAction CancelFirst(params WickExitOrderKind[] kinds)
        {
            foreach (KeyValuePair<int, TrackedOrder> pair in orders)
            {
                TrackedOrder order = pair.Value;
                if (Array.IndexOf(kinds, order.Kind) < 0 || IsTerminal(order.State)
                    || !order.CanCancel || order.CancelRequested)
                    continue;
                order.CancelRequested = true;
                Version++;
                return new WickExitAction { Kind = WickExitActionKind.Cancel, OrderToken = pair.Key };
            }
            return null;
        }

        private bool HasActive(params WickExitOrderKind[] kinds)
        {
            foreach (TrackedOrder order in orders.Values)
                if (!IsTerminal(order.State) && Array.IndexOf(kinds, order.Kind) >= 0)
                    return true;
            return false;
        }

        private void RefreshFlags()
        {
            ActiveOrders = 0;
            HasActiveExits = false;
            HasUnprocessedFills = false;
            foreach (TrackedOrder order in orders.Values)
            {
                if (!IsTerminal(order.State))
                {
                    ActiveOrders++;
                    HasActiveExits |= order.Kind != WickExitOrderKind.Entry;
                }
                HasUnprocessedFills |= order.ReportedFilled > order.Executed;
            }
        }

        private static bool IsTerminal(WickExitOrderState state)
        {
            return state == WickExitOrderState.Filled || state == WickExitOrderState.Cancelled
                || state == WickExitOrderState.Rejected;
        }

        private static WickExitAction None()
        {
            return new WickExitAction { Kind = WickExitActionKind.None };
        }
    }

}
