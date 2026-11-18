#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Web.Script.Serialization;
using System.Xml.Serialization;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;
#endregion

// SELL2525-1000 (file SELL2525-1000.cs, class SELL2525, strategy name SELL2525) - NQ or MNQ opening-bell short scalp. Steve's research, 2026-11-18 (fast15-work/open930, SELL2525_*_DESIGN_LOCKED.md).
//   One short a day. Market sell sent at (target fill 09:30:00.000 + 35 ms) - Delay offset (280 ms) = 09:29:59.755 New York; market buy sent at (entry target + 25,750 ms) - Delay offset = 09:30:25.505.
//   Stop 75 points and take profit 58 points from the actual fill (OCO, placed when the entry fills). Skip the day when the 09:00-09:30 30-minute bar is bullish ("30m bullish bar filter", default ON).
//   "Quote-activity filter": skip the day when quote updates in the last second before the order is sent exceed 1,039 (replay-calibrated, NOT validated on the live feed: default OFF, counts are always logged).
//   Hold 25.75 s was chosen over 25.25 s by Steve for the lower maxIDD / higher Net-to-drawdown in the back-test (B1: Net $19,501, maxIDD $1,424, Net/DD 13.70 vs $19,696 / $1,529 / 12.88); it is a post-hoc pick.
//   Optional Topstep / ProjectX mirror ported from FAST15-1014 (same worker thread and REST calls; market entry with stop and take-profit brackets, flatten at the exit time).
// Unmanaged order mode. Trades only in Realtime (Playback and live). Put it on any NQ or MNQ chart (a 1-minute chart is simplest); it adds its own 1-minute series for the 30-minute bar. All times are New York time.
// Logs (Documents folder): SELL2525-days.csv (one row per day: decision, filters, quote counts), SELL2525-trades.csv (one row per trade: every send/ack/fill time for delay analysis), SELL2525-orders.csv (every order event).
// Playback uses the connection clock for strategy timing; live uses the platform clock. ProjectX network deadlines stay on wall-clock UTC.
namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public enum Sell2525Version
    {
        version_1000,
        modified_2026_11_18
    }

    public class SELL2525 : Strategy
    {
        private const string NameEntry = "S2525-Entry", NameStop = "S2525-Stop1", NameTarget = "S2525-Target1", NameClose = "S2525-Close", NameGuard = "S2525-Guard";
        private const double LateGuardMs = 1500.0;      // if the first tick at or after the send time arrives this much later, the day is skipped (late start / data stall)
        private const int ProtectionWaitMs = 3000;      // fill -> working stop must exist within this time or the position is flattened
        private TimeZoneInfo platformZone, easternZone;
        private bool tradingBlocked;
        private DateTime lastTickTime = DateTime.MinValue;
        private volatile bool projectXTerminating;
        private bool projectXMirrorUsed;
        private bool maxBalanceReached;       // Max Account Balance latch: set once, never reset by the new trading day (lasts until the strategy is restarted, like FAST15)
        private DateTime lastMonitorTime = DateTime.MinValue;
        private DateTime lastOrphanCheckUtc = DateTime.MinValue;

        // ---------------------------------------------------------------- per-day state
        private struct QEv { public long T; public byte Side; public bool PriceChg; }   // Side 0 bid, 1 ask
        private sealed class Day
        {
            public DateTime Et;                                  // the New York date
            public DateTime ExpireL, OpenL, EntryTargetL, EntryDueL, ExitTargetL, ExitDueL;   // platform clock
            public bool Decided, Skipped, EntrySent, ExitSent, Closed, RowWritten, DayRowWritten, OpenSnapDone, NoTriggerNoted, HandNoted;
            public int CorrSellQty; public Order Guard; public DateTime NetOffSince = DateTime.MinValue;
            public int NetShort { get { return FilledQty - ExitFilledQty + CorrSellQty; } }   // own fill count: sells minus buys (positive = short)
            public string Status = "";
            // features
            public double Bar30Open, Bar30Last, TickOpen900, TickLast; public bool Bar30Ok, Bar30Bullish; public string Bar30Why = "";
            public int QSend, QSendBid, QSendAsk, QSendPriceChg, QOpen, QOpenBid, QOpenAsk, QOpenPriceChg;
            public double Ticks100Before, MaxGapMs500;
            // entry
            public DateTime EntryTrigNow, EntryTrigData, EntrySubmitStart, EntrySubmitEnd, EntryAck, FirstFillCb, FirstFillBroker, LastFillCb;
            public double BidSend, AskSend, BidFill, AskFill, DataLagMs;
            public int Qty, FilledQty; public double FillValue, FillPrice;
            public Order Entry, Stop, Target, Close;
            public double StopPx, TpPx; public DateTime StopPlacedStart, StopPlacedEnd, TargetAckTime;
            public string Oco = "";
            // exit
            public DateTime ExitTrigNow, ExitSubmitStart, ExitSubmitEnd, ExitFillCb, ExitFillBroker;
            public double BidExitSend, AskExitSend, ExitFillValue; public int ExitFilledQty; public double ExitPrice;
            public string ExitReason = ""; public int ExitSends; public DateTime LastExitSend;
            public double Ticks100BeforeExit, MaxGapMs500Exit;
            public int FlattenSends; public DateTime LastFlattenSend;
            public string Note = "";
        }
        private Day day;
        private readonly List<QEv> qev = new List<QEv>();
        private double lastBidPx = double.NaN, lastAskPx = double.NaN;

        // ---------------------------------------------------------------- state change
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "SELL2525: NQ or MNQ opening-bell short. Market sell at 09:30:00.035 (sent early by the Delay offset), market buy 25.75 s later, 75-point stop, 58-point target.";
                Name = "SELL2525";
                Version = Sell2525Version.version_1000;
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = false;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.ImmediatelySubmit;
                TimeInForce = TimeInForce.Day;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                ConnectionLossHandling = ConnectionLossHandling.KeepRunning;
                IgnoreOverfill = true;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsUnmanaged = true;

                Use30mFilter = true;
                UseQuoteFilter = false;      // default OFF: research label OFF at live timing (SELL2525_BUILD); Steve can switch it on
                ContractQty = 1;
                DelayOffsetMs = 280;
                MaxAccountBalance = 0;
                EntryOffsetMs = 35;
                HoldMs = 25750;
                StopPoints = 75;
                TpPoints = 58;
                QuoteCutoff = 1038.8;        // 80th percentile of quote updates in the second before the 280 ms send time (replay data, 142 NQ days)
                ProjectXApiBaseUrl = "https://api.topstepx.com";
                ProjectXTradeAllAccounts = false;
                ProjectXUsername = string.Empty;
                ProjectXApiKey = string.Empty;
                ProjectXAccountId = string.Empty;
                ProjectXContractId = string.Empty;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 1);     // BarsArray[1]: the 1-minute series used for the 09:00-09:30 bar
            }
            else if (State == State.DataLoaded)
            {
                try
                {
                    platformZone = NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
                    easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
                }
                catch (Exception) { platformZone = null; easternZone = null; }
                tradingBlocked = false;
                maxBalanceReached = false;
                string root = Instrument == null || Instrument.MasterInstrument == null ? "" : Instrument.MasterInstrument.Name;
                if (root != "NQ" && root != "MNQ")
                {
                    tradingBlocked = true;
                    Print("SELL2525 !!! NO ENTRIES: this strategy runs on NQ or MNQ only. Instrument: " + (Instrument == null ? "none" : Instrument.FullName));
                }
                if (ContractQty < 1 || ContractQty > MaxContracts())
                {
                    tradingBlocked = true;
                    Print("SELL2525 !!! NO ENTRIES: Contract quantity is " + ContractQty + " but this instrument allows 1 to " + MaxContracts() + " (NQ 2, MNQ 9).");
                }
                if (ContractQty > 1 && IsProjectXConfigured())
                {
                    tradingBlocked = true;
                    Print("SELL2525 !!! NO ENTRIES: Contract quantity is above 1 and a ProjectX (Topstep) login is set on this instance; the mirror is 1 contract only. Use 1 here or clear the ProjectX login.");
                }
                if (platformZone == null || easternZone == null)
                {
                    tradingBlocked = true;
                    Print("SELL2525 !!! NO ENTRIES: the New York or platform time zone could not be read, so the order times cannot be trusted.");
                }
                if (Calculate != Calculate.OnEachTick)
                {
                    tradingBlocked = true;
                    Print("SELL2525 !!! NO ENTRIES: Calculate must be On each tick (it is " + Calculate + ").");
                }
                if (BarsArray == null || BarsArray.Length < 2)
                    Print("SELL2525 WARNING: the 1-minute series for the 30-minute bar was not created; the 30m bullish bar filter will fall back to its own tick tracker.");
                try
                {
                    string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("the Documents folder could not be found");
                    daysPath = Path.Combine(dir, "SELL2525-days.csv");
                    tradesPath = Path.Combine(dir, "SELL2525-trades.csv");
                    ordersPath = Path.Combine(dir, "SELL2525-orders.csv");
                    StartLogWriter();
                }
                catch (Exception ex) { daysPath = null; tradesPath = null; ordersPath = null; Print("SELL2525 !!! log folder not available, logging is OFF: " + ex.Message); }
            }
            else if (State == State.Realtime)
            {
                try { RunProjectXStartupPreflight(); StartProjectXWorker(); }
                catch (Exception ex) { Print("SELL2525 !!! ProjectX startup failed: " + ex.Message); }
                Print(string.Format(CultureInfo.InvariantCulture,
                    "SELL2525 settings | version=1000 | maxAccountBalance={12} | instrument={0} | qty={1} | delayOffsetMs={2} | entryTargetMs={3} (after 09:30:00.000) | holdMs={4} | stop={5}pt | tp={6}pt | filter30m={7} | filterQuote={8} cutoff={9:F1} | projectX={10} | blocked={11}",
                    Instrument == null ? "?" : Instrument.FullName, ContractQty, DelayOffsetMs, EntryOffsetMs, HoldMs, StopPoints, TpPoints, Use30mFilter, UseQuoteFilter, QuoteCutoff, IsProjectXConfigured(), tradingBlocked, MaxAccountBalance));
                try
                {
                    DateTime n = StrategyNow;
                    Print("SELL2525 clock | source=" + (Connection.PlaybackConnection != null ? "Playback" : "Live") + " | platform now " + n.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " | wall utc " + DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " | platform zone " + (platformZone == null ? "?" : platformZone.Id) + " | New York now " + Stamp(ToEastern(n)) + " | keep the VPS clock synced (the Delay offset absorbs a constant clock error, not a drifting one).");
                }
                catch (Exception) { }
            }
            else if (State == State.Terminated)
            {
                ProjectXTerminationCleanup();
                try
                {
                    Day d = day;
                    if (d != null && (d.NetShort != 0 || (d.EntrySent && !d.Closed)))
                        Print("SELL2525 !!! strategy stopped while the trade is " + (d.FilledQty > 0 ? "OPEN" : "unfilled") + "; its stop and target may have been canceled by the platform, and any Topstep copy keeps only its own brackets. CHECK THE POSITION AND THE ORDERS IN BOTH ACCOUNTS.");
                }
                catch (Exception) { }
                StopLogWriter();
            }
        }

        private int MaxContracts() { return Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.Name == "MNQ" ? 9 : 2; }
        private int Qty { get { return Math.Max(1, ContractQty); } }

        // ---------------------------------------------------------------- time helpers
        private DateTime StrategyNow
        {
            get
            {
                Connection playback = Connection.PlaybackConnection;
                return playback != null ? playback.Now : NinjaTrader.Core.Globals.Now;
            }
        }

        private DateTime ToEastern(DateTime platformTime)
        {
            if (easternZone == null || platformZone == null || platformZone.Equals(easternZone)) return platformTime;
            try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(platformTime, DateTimeKind.Unspecified), platformZone, easternZone); }
            catch (Exception) { return platformTime; }
        }
        private DateTime ToPlatform(DateTime easternTime)
        {
            if (easternZone == null || platformZone == null || platformZone.Equals(easternZone)) return easternTime;
            try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(easternTime, DateTimeKind.Unspecified), easternZone, platformZone); }
            catch (Exception) { return easternTime; }
        }
        private static string Stamp(DateTime et) { return et.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture); }
        private string StampP(DateTime platformTime) { return platformTime == DateTime.MinValue ? "" : Stamp(ToEastern(platformTime)); }
        private static double Ms(DateTime a, DateTime b) { return (a == DateTime.MinValue || b == DateTime.MinValue) ? double.NaN : (a - b).TotalMilliseconds; }   // a minus b

        private Day NewDay(DateTime nowEt)
        {
            Day d = new Day();
            d.Et = nowEt.Date;
            DateTime open = d.Et.AddHours(9).AddMinutes(30);
            d.OpenL = ToPlatform(open);
            d.EntryTargetL = d.OpenL.AddMilliseconds(EntryOffsetMs);
            d.EntryDueL = d.EntryTargetL.AddMilliseconds(-DelayOffsetMs);
            d.ExitTargetL = d.EntryTargetL.AddMilliseconds(HoldMs);
            d.ExitDueL = d.ExitTargetL.AddMilliseconds(-DelayOffsetMs);
            d.ExpireL = ToPlatform(d.Et.AddDays(1));
            qev.Clear();
            return d;
        }

        // ---------------------------------------------------------------- tick entry points
        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            try { if (BarsInProgress != 0) return; OnTick(marketDataUpdate.MarketDataType, marketDataUpdate.Time, marketDataUpdate.Price); }
            catch (Exception ex) { Print("SELL2525 !!! error in OnMarketData: " + ex.Message + " | CHECK THE POSITION (stops and exits are working orders unless a trade is open with none)."); }
        }

        protected override void OnBarUpdate()
        {
            try { if (BarsInProgress == 0) OnTick(MarketDataType.Last, DateTime.MinValue, 0.0); }
            catch (Exception ex) { Print("SELL2525 !!! error in OnBarUpdate: " + ex.Message + " | CHECK THE POSITION."); }
        }

        private void OnTick(MarketDataType type, DateTime dataTime, double price)
        {
            DateTime now = StrategyNow;
            lastTickTime = now;
            if (State != State.Realtime) return;
            if (day == null || now >= day.ExpireL) day = NewDay(ToEastern(now));
            Day d = day;
            // quote event tracking: only around the open (09:29:30 to 09:31:30)
            bool inWindow = now >= d.OpenL.AddSeconds(-30) && now <= d.OpenL.AddSeconds(90);
            if (type == MarketDataType.Bid || type == MarketDataType.Ask)
            {
                if (inWindow)
                {
                    QEv e = new QEv(); e.T = now.Ticks; e.Side = (byte)(type == MarketDataType.Bid ? 0 : 1);
                    if (type == MarketDataType.Bid) { e.PriceChg = double.IsNaN(lastBidPx) || price != lastBidPx; }
                    else { e.PriceChg = double.IsNaN(lastAskPx) || price != lastAskPx; }
                    qev.Add(e);
                }
                if (type == MarketDataType.Bid) lastBidPx = price; else lastAskPx = price;
            }
            else if (type == MarketDataType.Last && price > 0.0)
            {
                // own 09:00-09:30 tracker: the first and last trade price (used only if the 1-minute series is missing, and logged as a cross-check)
                DateTime n9 = ToPlatform(d.Et.AddHours(9));
                if (now >= n9 && now <= d.EntryDueL)
                {
                    if (d.TickOpen900 == 0.0 && now <= n9.AddSeconds(10)) d.TickOpen900 = price;   // the tracker only counts if the strategy saw the 09:00 open itself
                    if (d.TickOpen900 > 0.0) d.TickLast = price;
                }
            }
            if (!inWindow && now < d.OpenL.AddSeconds(-30)) return;     // nothing to do before 09:29:30
            // 09:30:00.000 snapshot of quote counts (the research window), for calibration
            if (!d.OpenSnapDone && now >= d.OpenL)
            {
                d.OpenSnapDone = true;
                CountQuotes(d.OpenL.Ticks, 10000000L, out d.QOpen, out d.QOpenBid, out d.QOpenAsk, out d.QOpenPriceChg);
            }
            try { CheckAccountBalanceCap(d, now); } catch (Exception ex) { Print("SELL2525 max account balance check error: " + ex.Message); }
            // entry
            if (!d.Decided && now >= d.EntryDueL)
            {
                d.EntryTrigNow = now; d.EntryTrigData = dataTime; d.DataLagMs = dataTime == DateTime.MinValue ? double.NaN : (now - dataTime).TotalMilliseconds;
                DecideAndEnter(d, now);
            }
            // timed exit
            if (d.EntrySent && !d.ExitSent && !d.Closed && now >= d.ExitDueL)
            {
                d.ExitTrigNow = now;
                SendTimedExit(d, now);
            }
            // Protection follows replay speed; network retries below follow real elapsed time.
            DateTime nu = DateTime.UtcNow;
            DateTime monitorTime = Connection.PlaybackConnection != null ? now : nu;
            if ((monitorTime - lastMonitorTime).TotalMilliseconds >= 200.0)
            {
                lastMonitorTime = monitorTime;
                try { Monitor(d, now); } catch (Exception ex) { Print("SELL2525 monitor error: " + ex.Message); }
            }
            if ((nu - lastOrphanCheckUtc).TotalMilliseconds >= 1000.0)
            {
                lastOrphanCheckUtc = nu;
                try { EvaluateProjectXOrphanRecovery(); } catch (Exception ex) { Print("SELL2525 ProjectX orphan check error: " + ex.Message); }
            }
        }

        // Max Account Balance (ported from FAST15): checked every tick on the ACCOUNT'S net liquidation (unrealized included, other strategies on the account count too). At or above the cap:
        // no new entry is placed again, an unfilled entry is canceled and an open trade is closed at market (exit reason "max account balance exit", ProjectX mirror flatten queued).
        // It holds until the strategy is restarted. 0 disables.
        private bool TryGetNetLiquidation(out double netLiquidation)
        {
            netLiquidation = 0.0;
            if (Account == null) return false;
            try
            {
                netLiquidation = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                if (netLiquidation > 0.0 && !double.IsNaN(netLiquidation) && !double.IsInfinity(netLiquidation)) return true;
                double realizedCash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                double unrealized = Position.MarketPosition != MarketPosition.Flat ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]) : 0.0;
                netLiquidation = realizedCash + unrealized;
                return (realizedCash > 0.0 || Position.MarketPosition != MarketPosition.Flat) && !double.IsNaN(netLiquidation) && !double.IsInfinity(netLiquidation);
            }
            catch { return false; }
        }

        private void CheckAccountBalanceCap(Day d, DateTime now)
        {
            if (MaxAccountBalance <= 0 || maxBalanceReached) return;
            double netLiq;
            if (!TryGetNetLiquidation(out netLiq) || netLiq < MaxAccountBalance) return;
            maxBalanceReached = true;
            Print(string.Format(CultureInfo.InvariantCulture, "SELL2525 MAX ACCOUNT BALANCE reached (net liquidation ${0:F2} >= ${1:F2}): no new entries until the strategy is restarted; an unfilled entry is canceled and an open trade is closed at market.", netLiq, MaxAccountBalance));
            if (d != null && d.EntrySent && !d.ExitSent && !d.Closed)
            {
                if (d.ExitReason.Length == 0) d.ExitReason = "max account balance exit";
                d.ExitTrigNow = now;
                SendTimedExit(d, now);
            }
        }

        private bool AnyTradeLive() { Day d = day; return d != null && d.EntrySent && !d.Closed; }

        // events with time in (endTicks - windowTicks, endTicks]
        private void CountQuotes(long endTicks, long windowTicks, out int total, out int bid, out int ask, out int priceChg)
        {
            total = bid = ask = priceChg = 0;
            for (int i = qev.Count - 1; i >= 0; i--)
            {
                QEv e = qev[i];
                if (e.T > endTicks) continue;
                if (e.T <= endTicks - windowTicks) break;
                total++; if (e.Side == 0) bid++; else ask++; if (e.PriceChg) priceChg++;
            }
        }

        private void DensityAround(long endTicks, out double events100, out double maxGapMs500)
        {
            events100 = 0; maxGapMs500 = 0; long prev = endTicks - 5000000L;
            for (int i = 0; i < qev.Count; i++)
            {
                QEv e = qev[i];
                if (e.T <= endTicks - 5000000L) continue;
                if (e.T > endTicks) break;
                if (e.T > endTicks - 1000000L) events100++;
                double g = (e.T - prev) / 10000.0; if (g > maxGapMs500) maxGapMs500 = g; prev = e.T;
            }
            double last = (endTicks - prev) / 10000.0; if (last > maxGapMs500) maxGapMs500 = last;
        }

        // ---------------------------------------------------------------- 30-minute bar (09:00 to 09:30 New York)
        private bool TryBar30(Day d)
        {
            d.Bar30Ok = false; d.Bar30Why = "";
            try
            {
                if (BarsArray != null && BarsArray.Length >= 2 && CurrentBars[1] >= 30)
                {
                    DateTime endEt = ToEastern(Times[1][0]);
                    int sh = -1;   // index of the 1-minute bar that ends 09:30:00 (bar times are END times); 1 if the series already rolled to the 09:31 bar
                    if (endEt.Date == d.Et && endEt.TimeOfDay == new TimeSpan(9, 30, 0)) sh = 0;
                    else if (CurrentBars[1] >= 1 && endEt.Date == d.Et && endEt.TimeOfDay == new TimeSpan(9, 31, 0) && ToEastern(Times[1][1]).TimeOfDay == new TimeSpan(9, 30, 0)) sh = 1;
                    if (sh >= 0)
                    {
                        double open = 0.0;
                        for (int i = sh; i < 45 + sh && i <= CurrentBars[1]; i++)
                        {
                            DateTime e2 = ToEastern(Times[1][i]);
                            if (e2.Date == d.Et && e2.TimeOfDay == new TimeSpan(9, 1, 0)) { open = Opens[1][i]; break; }
                        }
                        if (open > 0.0)
                        {
                            d.Bar30Open = open; d.Bar30Last = Closes[1][sh]; d.Bar30Ok = true; d.Bar30Bullish = d.Bar30Last > d.Bar30Open; d.Bar30Why = "series";
                            return true;
                        }
                        d.Bar30Why = "09:00 bar not found in the 1-minute series";
                    }
                    else d.Bar30Why = "current 1-minute bar ends " + endEt.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + ", not 09:30:00";
                }
                else d.Bar30Why = "1-minute series has too few bars";
            }
            catch (Exception ex) { d.Bar30Why = "series error: " + ex.Message; }
            if (d.TickOpen900 > 0.0 && d.TickLast > 0.0)
            {
                d.Bar30Open = d.TickOpen900; d.Bar30Last = d.TickLast; d.Bar30Ok = true; d.Bar30Bullish = d.Bar30Last > d.Bar30Open; d.Bar30Why += " | tick tracker used";
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- decision and entry
        private void DecideAndEnter(Day d, DateTime now)
        {
            d.Decided = true;
            DensityAround(d.EntryDueL.Ticks, out d.Ticks100Before, out d.MaxGapMs500);
            CountQuotes(d.EntryDueL.Ticks, 10000000L, out d.QSend, out d.QSendBid, out d.QSendAsk, out d.QSendPriceChg);
            bool barOk = TryBar30(d);
            double lateMs = (now - d.EntryDueL).TotalMilliseconds;
            string skip = "";
            if (tradingBlocked) skip = "BLOCKED (see start-up messages)";
            else if (lateMs > LateGuardMs) skip = "LATE (first tick " + lateMs.ToString("F0", CultureInfo.InvariantCulture) + " ms after the send time)";
            else if (maxBalanceReached) skip = "MAX ACCOUNT BALANCE REACHED";
            else if (Position.MarketPosition != MarketPosition.Flat) skip = "POSITION NOT FLAT";
            else if (Use30mFilter && !barOk) skip = "NO 30M DATA (" + d.Bar30Why + ")";
            else if (Use30mFilter && d.Bar30Bullish) skip = "30M BULLISH";
            else if (UseQuoteFilter && d.QSend > QuoteCutoff) skip = "QUOTE ACTIVITY";
            double bid = GetCurrentBid(), ask = GetCurrentAsk();
            if (skip.Length == 0 && (bid <= 0.0 || ask <= 0.0)) skip = "NO BID/ASK";
            if (skip.Length > 0)
            {
                d.Skipped = true; d.Status = "SKIPPED " + skip; d.Closed = true;
                Print("SELL2525 SKIPPED | " + Stamp(d.Et) + " | " + skip + " | bar30 " + d.Bar30Open.ToString("F2", CultureInfo.InvariantCulture) + "->" + d.Bar30Last.ToString("F2", CultureInfo.InvariantCulture) + " | quotes in the second before send " + d.QSend);
                WriteDayRow(d);
                return;
            }
            d.Status = "TRADED"; d.Qty = Qty; d.BidSend = bid; d.AskSend = ask;
            d.EntrySubmitStart = StrategyNow;
            Order o = null;
            try { o = SubmitOrderUnmanaged(0, OrderAction.SellShort, OrderType.Market, d.Qty, 0.0, 0.0, "", NameEntry); }
            catch (Exception ex) { Print("SELL2525 !!! entry submit failed: " + ex.Message); }
            d.EntrySubmitEnd = StrategyNow;
            d.Entry = o; d.EntrySent = true;
            LogOrderSend(NameEntry, "SellShort", "Market", d.Qty, 0.0, 0.0);
            if (o == null) { d.Status = "ENTRY SUBMIT FAILED"; d.Closed = true; WriteDayRow(d); return; }
            Print(string.Format(CultureInfo.InvariantCulture, "SELL2525 ENTRY SENT | {0} | due {1} | sent {2} | +{3:F0} ms late | qty {4} | bid {5:F2} ask {6:F2} | bar30 {7:F2}->{8:F2} | quotes/s {9}",
                Stamp(d.Et), Stamp(ToEastern(d.EntryDueL)), Stamp(ToEastern(d.EntrySubmitStart)), (d.EntrySubmitStart - d.EntryDueL).TotalMilliseconds, d.Qty, bid, ask, d.Bar30Open, d.Bar30Last, d.QSend));
            try { SendPlannedProjectXEntry(-1, bid, TpPoints, StopPoints); }
            catch (Exception ex) { Print("SELL2525 !!! ProjectX entry mirror failed to queue: " + ex.Message); }
            WriteDayRow(d);
        }

        // ---------------------------------------------------------------- timed exit
        private void SendTimedExit(Day d, DateTime now)
        {
            d.ExitSent = true;
            DensityAround(d.ExitDueL.Ticks, out d.Ticks100BeforeExit, out d.MaxGapMs500Exit);
            d.BidExitSend = GetCurrentBid(); d.AskExitSend = GetCurrentAsk();
            int open = d.NetShort > 0 ? d.NetShort : 0;
            if (open <= 0)
            {
                // entry not filled yet: cancel it; a late fill is flattened in OnExecutionUpdate
                d.ExitReason = "no fill at exit time"; d.Closed = true;
                CancelIfLive(d.Entry);
                Print("SELL2525 EXIT TIME REACHED with no open position | entry state " + (d.Entry == null ? "none" : d.Entry.OrderState.ToString()) + " | any late fill is flattened.");
                try { MirrorExitNow(); } catch (Exception ex) { Print("SELL2525 !!! ProjectX exit failed to queue: " + ex.Message + " | CHECK THE TOPSTEP POSITION."); }
                return;
            }
            d.ExitSubmitStart = StrategyNow;
            Order o = null;
            if (OrderInFlight(d.Guard)) { d.ExitSends = 1; d.LastExitSend = now.AddSeconds(-2); Print("SELL2525 exit time reached while a flatten order is working: the exit retry follows."); }
            else
            {
                try { o = SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, open, 0.0, 0.0, "", NameClose); }
                catch (Exception ex) { Print("SELL2525 !!! exit submit failed: " + ex.Message + " | CHECK THE POSITION."); }
                d.ExitSubmitEnd = StrategyNow; d.Close = o; d.ExitSends++; d.LastExitSend = d.ExitSubmitEnd;
                LogOrderSend(NameClose, "BuyToCover", "Market", open, 0.0, 0.0);
            }
            CancelIfLive(d.Entry); CancelIfLive(d.Stop); CancelIfLive(d.Target);   // the entry only matters if it was part filled
            try { MirrorExitNow(); } catch (Exception ex) { Print("SELL2525 !!! ProjectX exit failed to queue: " + ex.Message + " | CHECK THE TOPSTEP POSITION."); }
            Print(string.Format(CultureInfo.InvariantCulture, "SELL2525 EXIT SENT | {0} | due {1} | sent {2} | +{3:F0} ms late | qty {4} | bid {5:F2} ask {6:F2}", Stamp(d.Et), Stamp(ToEastern(d.ExitDueL)), Stamp(ToEastern(d.ExitSubmitStart)), (d.ExitSubmitStart - d.ExitDueL).TotalMilliseconds, open, d.BidExitSend, d.AskExitSend));
        }

        private void CancelIfLive(Order o)
        {
            if (o == null) return;
            try
            {
                OrderState s = o.OrderState;
                if (s == OrderState.Working || s == OrderState.Accepted || s == OrderState.Submitted || s == OrderState.PartFilled || s == OrderState.ChangePending || s == OrderState.Initialized)
                { CancelOrder(o); LogOrderSend(o.Name, "Cancel", o.OrderType.ToString(), o.Quantity, 0.0, 0.0); }
            }
            catch (Exception ex) { Print("SELL2525 cancel error: " + ex.Message); }
        }

        private static bool OrderInFlight(Order o)
        {
            if (o == null) return false;
            OrderState s = o.OrderState;
            return s == OrderState.Initialized || s == OrderState.Submitted || s == OrderState.Accepted || s == OrderState.Working || s == OrderState.PartFilled || s == OrderState.ChangePending;
        }

        private void SubmitFlatten(Day d, string why)
        {
            int net = d.NetShort;   // from the strategy's own fills: positive = short, negative = long
            int open = Math.Abs(net);
            if (open <= 0) return;
            // never while an exit or an earlier flatten is still working: a second order before the first fills would flip the position
            if (OrderInFlight(d.Close) || OrderInFlight(d.Guard)) return;
            if (d.FlattenSends > 0 && (StrategyNow - d.LastFlattenSend).TotalMilliseconds < 3000) return;
            OrderAction a = net > 0 ? OrderAction.BuyToCover : OrderAction.Sell;
            try
            {
                d.Guard = SubmitOrderUnmanaged(0, a, OrderType.Market, open, 0.0, 0.0, "", NameGuard);
                d.FlattenSends++; d.LastFlattenSend = StrategyNow;
                LogOrderSend(NameGuard, a.ToString(), "Market", open, 0.0, 0.0);
                Print("SELL2525 !!! FLATTEN sent (" + why + ") | qty " + open + " | CHECK THE POSITION (disable the strategy before flattening by hand, or it may open a new one).");
                d.Note += (d.Note.Length > 0 ? "; " : "") + "flatten: " + why;
            }
            catch (Exception ex) { Print("SELL2525 !!! flatten submit failed (" + why + "): " + ex.Message + " | CHECK THE POSITION."); }
        }

        // ---------------------------------------------------------------- monitors
        private void Monitor(Day d, DateTime now)
        {
            if (!d.EntrySent) { MaybeNoTrigger(d, now); return; }
            if (d.Closed)
            {
                if (d.FilledQty > 0 && d.NetShort != 0)
                {
                    // a closed trade must leave the account flat: if the strategy's own fills show a leftover position for 1 s, flatten it (never while an order is in flight; up to 6 orders)
                    if (d.NetOffSince == DateTime.MinValue) d.NetOffSince = now;
                    else if ((now - d.NetOffSince).TotalMilliseconds >= 1000.0)
                    {
                        if (d.FlattenSends < 6) SubmitFlatten(d, "position open after the trade was closed");
                        else if (!d.HandNoted) { d.HandNoted = true; Print("SELL2525 !!! POSITION STILL OPEN after 6 flatten orders. FLATTEN IT BY HAND."); }
                    }
                }
                else { d.NetOffSince = DateTime.MinValue; if (!d.RowWritten && d.FilledQty > 0 && d.ExitFilledQty > 0) WriteTradeRow(d); }
                return;
            }
            bool shortOpen = d.NetShort > 0;
            // protection: a working stop must exist ProtectionWaitMs after the first fill
            if (shortOpen && d.FirstFillCb != DateTime.MinValue && !d.ExitSent && (now - d.FirstFillCb).TotalMilliseconds > ProtectionWaitMs)
            {
                bool stopOk = d.Stop != null && (d.Stop.OrderState == OrderState.Working || d.Stop.OrderState == OrderState.Accepted || d.Stop.OrderState == OrderState.Submitted || d.Stop.OrderState == OrderState.PartFilled || d.Stop.OrderState == OrderState.ChangePending);
                if (!stopOk && d.FlattenSends == 0) { SubmitFlatten(d, "protection missing " + ProtectionWaitMs + " ms after the fill"); }
            }
            // exit retry
            if (d.ExitSent && shortOpen && d.ExitSends > 0 && d.ExitSends < 4)
            {
                bool closeLive = d.Close != null && (d.Close.OrderState == OrderState.Working || d.Close.OrderState == OrderState.Accepted || d.Close.OrderState == OrderState.Submitted || d.Close.OrderState == OrderState.PartFilled);
                if (!closeLive && !OrderInFlight(d.Guard) && (now - d.LastExitSend).TotalMilliseconds > 1500)
                {
                    Print("SELL2525 !!! exit order not live and the position is open: retry " + d.ExitSends + " of 3.");
                    int open = d.NetShort; Order o = null;
                    try { o = SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Market, open, 0.0, 0.0, "", NameClose); } catch (Exception ex) { Print("SELL2525 !!! exit retry failed: " + ex.Message); }
                    d.Close = o; d.ExitSends++; d.LastExitSend = StrategyNow; LogOrderSend(NameClose, "BuyToCover", "Market", open, 0.0, 0.0);
                }
            }
            // last resort: still open well after the exit time
            if (d.NetShort != 0 && now > d.ExitDueL.AddSeconds(20) && d.FlattenSends < 3 && (now - d.LastFlattenSend).TotalSeconds > 5)
                SubmitFlatten(d, "position still open 20 s after the exit time");
            if (d.NetShort != 0 && now > d.ExitDueL.AddSeconds(60) && d.FlattenSends >= 3 && !d.NoTriggerNoted)
            { d.NoTriggerNoted = true; Print("SELL2525 !!! POSITION STILL OPEN after 3 flatten orders. FLATTEN IT BY HAND."); }
        }

        private void MaybeNoTrigger(Day d, DateTime now)
        {
            if (!d.Decided && !d.DayRowWritten && now > d.EntryDueL.AddSeconds(30))
            {
                d.Decided = true; d.Status = "NO TRIGGER (no tick at the send time)"; d.Closed = true;
                Print("SELL2525 no entry decision today: no market data arrived at the send time.");
                WriteDayRow(d);
            }
        }

        // ---------------------------------------------------------------- order and execution callbacks
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            try { OnOrderUpdateCore(order, limitPrice, stopPrice, quantity, filled, averageFillPrice, orderState, time, error, nativeError); }
            catch (Exception ex) { Print("SELL2525 !!! error in OnOrderUpdate: " + ex.Message + " | CHECK THE POSITION AND THE ORDERS."); }
        }

        private void OnOrderUpdateCore(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null) return;
            string nm = order.Name ?? "";
            if (!nm.StartsWith("S2525-", StringComparison.Ordinal)) return;
            DateTime cb = StrategyNow;
            LogOrderEvent(order, limitPrice, stopPrice, quantity, filled, averageFillPrice, orderState, time, cb, error, nativeError);
            Day d = day; if (d == null) return;
            if (nm == NameEntry)
            {
                d.Entry = order;
                if ((orderState == OrderState.Accepted || orderState == OrderState.Working) && d.EntryAck == DateTime.MinValue) d.EntryAck = cb;
                if (orderState == OrderState.Cancelled && d.FilledQty == 0 && !d.Closed && State == State.Realtime)
                {
                    d.Status = "ENTRY CANCELED"; d.Closed = true; WriteDayRow(d);
                    Print("SELL2525 !!! ENTRY ORDER CANCELED before it filled | no trade today.");
                    try { MirrorExitNow(); } catch (Exception ex) { Print("SELL2525 !!! ProjectX exit failed to queue: " + ex.Message); }
                }
                if (orderState == OrderState.Rejected)
                {
                    d.Status = "ENTRY REJECTED"; d.Closed = true; d.Note += "entry rejected: " + nativeError;
                    Print("SELL2525 !!! ENTRY ORDER REJECTED | " + nativeError + " | no trade today.");
                    try { MirrorExitNow(); } catch (Exception ex) { Print("SELL2525 !!! ProjectX exit failed to queue: " + ex.Message); }
                }
            }
            else if (nm == NameStop || nm == NameTarget)
            {
                if (nm == NameStop) d.Stop = order; else d.Target = order;
                if (nm == NameTarget && (orderState == OrderState.Accepted || orderState == OrderState.Working) && d.TargetAckTime == DateTime.MinValue) d.TargetAckTime = cb;
                if (nm == NameStop && (orderState == OrderState.Accepted || orderState == OrderState.Working) && d.StopPlacedEnd == DateTime.MinValue) d.StopPlacedEnd = cb;
                // RealtimeErrorHandling = StopCancelClose makes NinjaTrader cancel and close on a rejection by itself: here a warning only (a second flatten could flip the position). A missing STOP is caught by the protection monitor.
                if (orderState == OrderState.Rejected)
                    Print("SELL2525 !!! " + nm + " REJECTED | " + nativeError + " | CHECK THE POSITION AND THE ORDERS.");
                else if (orderState == OrderState.Cancelled && State == State.Realtime && !d.ExitSent && !d.Closed && order.Filled == 0 && !OcoSiblingFilled(d, nm))
                    Print("SELL2525 !!! " + nm + " was canceled while the trade is open and no exit was sent | the protection monitor flattens if the stop is gone.");
            }
            else if (nm == NameGuard) d.Guard = order;
            else if (nm == NameClose)
            {
                d.Close = order;
                if (orderState == OrderState.Rejected) Print("SELL2525 !!! EXIT ORDER REJECTED | " + nativeError + " | a retry follows if the position is still open.");
            }
        }

        private bool OcoSiblingFilled(Day d, string nm)
        {
            Order sib = nm == NameStop ? d.Target : d.Stop;
            return sib != null && sib.OrderState == OrderState.Filled;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            try { OnExecutionUpdateCore(execution, executionId, price, quantity, marketPosition, orderId, time); }
            catch (Exception ex) { Print("SELL2525 !!! error in OnExecutionUpdate: " + ex.Message + " | CHECK THE POSITION: a fill may be open without its stop or exit."); }
        }

        private readonly HashSet<string> seenExecutions = new HashSet<string>();
        private void OnExecutionUpdateCore(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null) return;
            string nm = execution.Order.Name ?? "";
            if (!nm.StartsWith("S2525-", StringComparison.Ordinal)) return;
            if (executionId != null && !seenExecutions.Add(executionId)) return;
            DateTime cb = StrategyNow;
            Day d = day; if (d == null) return;
            LogExecution(nm, executionId, price, quantity, time, cb);
            if (nm == NameEntry)
            {
                d.FilledQty += quantity; d.FillValue += price * quantity; d.FillPrice = d.FillValue / d.FilledQty; d.LastFillCb = cb;
                if (d.FirstFillCb == DateTime.MinValue) { d.FirstFillCb = cb; d.FirstFillBroker = time; d.BidFill = GetCurrentBid(); d.AskFill = GetCurrentAsk(); }
                Print(string.Format(CultureInfo.InvariantCulture, "SELL2525 ENTRY FILLED | {0} | {1} @ {2:F2} | callback {3} | broker time {4} | {5:F0} ms after the send", Stamp(d.Et), quantity, price, StampP(cb), StampP(time), Ms(cb, d.EntrySubmitStart)));
                if (d.Closed || d.ExitSent)
                {
                    SubmitFlatten(d, "entry filled after the exit time or after the trade closed");
                    return;
                }
                if (d.Stop == null) PlaceProtection(d); else UpdateProtectionQuantity(d);
            }
            else if (nm == NameStop || nm == NameTarget || nm == NameClose || nm == NameGuard)
            {
                if (execution.Order.OrderAction == OrderAction.Sell || execution.Order.OrderAction == OrderAction.SellShort)
                    d.CorrSellQty += quantity;      // a guard sell that corrects an accidental long: counted for the net, kept out of the exit price
                else
                {
                    d.ExitFilledQty += quantity;    // own fill counts (not Position, which may lag this callback)
                    d.ExitFillValue += price * quantity; d.ExitPrice = d.ExitFillValue / d.ExitFilledQty; d.ExitFillCb = cb; d.ExitFillBroker = time;
                    if (d.ExitReason.Length == 0 || d.ExitReason == "no fill at exit time")
                        d.ExitReason = nm == NameStop ? "stop" : (nm == NameTarget ? "take profit" : (nm == NameClose ? "timed exit" : "guard flatten"));
                }
                int net = d.NetShort;
                if (net <= 0 && d.FilledQty > 0 && !d.Closed)
                {
                    d.Closed = true;
                    CancelIfLive(d.Stop); CancelIfLive(d.Target);
                    if (!d.ExitSent) { try { MirrorExitNow(); } catch (Exception ex) { Print("SELL2525 !!! ProjectX exit failed to queue: " + ex.Message + " | CHECK THE TOPSTEP POSITION."); } }
                }
                if (net < 0)
                {
                    Print("SELL2525 !!! the position turned LONG (an exit order filled on top of another) | flattening.");
                    SubmitFlatten(d, "position turned long");
                }
                else if (net == 0 && d.ExitFilledQty > 0 && !d.RowWritten)
                {
                    double pts = d.FillPrice - d.ExitPrice;
                    Print(string.Format(CultureInfo.InvariantCulture, "SELL2525 CLOSED | {0} | {1} | entry {2:F2} exit {3:F2} | {4:F2} points | exit callback {5} | held {6:F0} ms", Stamp(d.Et), d.ExitReason, d.FillPrice, d.ExitPrice, pts, StampP(cb), Ms(time, d.FirstFillBroker == DateTime.MinValue ? d.FirstFillCb : d.FirstFillBroker)));
                    WriteTradeRow(d);
                }
            }
        }

        private void PlaceProtection(Day d)
        {
            double stop = Instrument.MasterInstrument.RoundToTickSize(d.FillPrice + StopPoints);
            double tp = Instrument.MasterInstrument.RoundToTickSize(d.FillPrice - TpPoints);
            d.StopPx = stop; d.TpPx = tp; d.Oco = "S2525-" + d.Et.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            d.StopPlacedStart = StrategyNow;
            int q = Math.Max(1, d.NetShort > 0 ? d.NetShort : d.FilledQty);
            try
            {
                d.Stop = SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.StopMarket, q, 0.0, stop, d.Oco, NameStop);
                LogOrderSend(NameStop, "BuyToCover", "StopMarket", q, 0.0, stop);
                d.Target = SubmitOrderUnmanaged(0, OrderAction.BuyToCover, OrderType.Limit, q, tp, 0.0, d.Oco, NameTarget);
                LogOrderSend(NameTarget, "BuyToCover", "Limit", q, tp, 0.0);
            }
            catch (Exception ex)
            {
                Print("SELL2525 !!! protection submit failed: " + ex.Message + " | flattening.");
                SubmitFlatten(d, "protection submit failed");
            }
            if (d.Stop == null) SubmitFlatten(d, "stop order was not created");
            Print(string.Format(CultureInfo.InvariantCulture, "SELL2525 PROTECTION SENT | {0} | stop {1:F2} | target {2:F2} | {3:F0} ms after the fill callback", Stamp(d.Et), stop, tp, Ms(StrategyNow, d.FirstFillCb)));
        }

        private void UpdateProtectionQuantity(Day d)
        {
            try
            {
                int q = d.NetShort > 0 ? d.NetShort : d.FilledQty;
                if (d.Stop != null && (d.Stop.OrderState == OrderState.Working || d.Stop.OrderState == OrderState.Accepted) && d.Stop.Quantity != q) ChangeOrder(d.Stop, q, 0.0, d.StopPx);
                if (d.Target != null && (d.Target.OrderState == OrderState.Working || d.Target.OrderState == OrderState.Accepted) && d.Target.Quantity != q) ChangeOrder(d.Target, q, d.TpPx, 0.0);
            }
            catch (Exception ex) { Print("SELL2525 protection quantity update failed: " + ex.Message); }
        }

        // ---------------------------------------------------------------- Topstep / ProjectX mirror hooks
        private void MirrorExitNow()
        {
            if (State != State.Realtime || !IsProjectXConfigured() || !projectXMirrorUsed) return;
            DispatchProjectXSimpleEvent("exit", MirrorQty, false, (sent, response) =>
            {
                lock (projectXStateLock)
                {
                    if (sent)
                    {
                        projectXEntryMirrorActive = false;
                        projectXLastSyncedStopPrice = 0.0;
                        projectXLastSyncedTargetPrice = 0.0;
                        projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                        projectXOrphanRecoveryCount = 0;
                    }
                    else
                    {
                        projectXEntryMirrorActive = true;
                        projectXOrphanRecoveryCount++;
                        projectXOrphanRecoveryDueUtc = DateTime.UtcNow.AddSeconds(Math.Min(60, 5 * projectXOrphanRecoveryCount));
                    }
                }
            });
        }

        private void ProjectXTerminationCleanup()
        {
            try
            {
                StopProjectXWorker();
                if (!IsProjectXConfigured() || !projectXMirrorUsed) return;
                if (Position.MarketPosition == MarketPosition.Flat) DispatchProjectXSimpleEvent("exit", MirrorQty, true, null);
            }
            catch (Exception ex) { Print("SELL2525 !!! ProjectX termination cleanup failed: " + ex.Message + " | CHECK THE TOPSTEP POSITION."); }
        }

        // Prepares and enqueues the ProjectX entry mirror (a MARKET entry with stop and take-profit brackets measured from the fill); does not block.
        private void SendPlannedProjectXEntry(int direction, double referencePrice, double takeProfitPoints, double stopLossPoints)
        {
            if (State != State.Realtime || !IsProjectXConfigured()) return;
            double entry = Instrument.MasterInstrument.RoundToTickSize(referencePrice);
            double target = Instrument.MasterInstrument.RoundToTickSize(direction > 0 ? entry + takeProfitPoints : entry - takeProfitPoints);
            double stop = Instrument.MasterInstrument.RoundToTickSize(direction > 0 ? entry - stopLossPoints : entry + stopLossPoints);
            string instrumentRoot, instrumentKey; DateTime instrumentExpiry; bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey, out instrumentExpiry, out hasInstrumentExpiry);
            bool hadUnresolvedMirror;
            lock (projectXStateLock) hadUnresolvedMirror = projectXEntryMirrorActive;
            var item = new ProjectXWorkItem
            {
                EventType = direction > 0 ? "buy" : "sell",
                EntryPrice = entry,
                TakeProfit = target,
                StopLoss = stop,
                IsMarketEntry = true,
                Quantity = MirrorQty,
                InstrumentRoot = instrumentRoot,
                InstrumentKey = instrumentKey,
                InstrumentExpiry = instrumentExpiry,
                HasInstrumentExpiry = hasInstrumentExpiry,
                TickSizeSnapshot = TickSize,
                OnComplete = (sent, response) =>
                {
                    lock (projectXStateLock)
                    {
                        if (sent)
                        {
                            projectXEntryMirrorActive = true;
                            suppressProjectXNextExecutionExit = false;
                            projectXLastSyncedStopPrice = stop;
                            projectXLastSyncedTargetPrice = target;
                            projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                            projectXOrphanRecoveryCount = 0;
                        }
                        else if (hadUnresolvedMirror)
                        {
                            projectXEntryMirrorActive = true;
                            projectXOrphanRecoveryCount++;
                            projectXOrphanRecoveryDueUtc = DateTime.UtcNow.AddSeconds(5);
                        }
                    }
                }
            };
            projectXMirrorUsed = true;
            EnqueueProjectXWork(item);
        }

        // ---------------------------------------------------------------- logging
        private sealed class LogJob { public string Path, Header, Rows; }
        private System.Collections.Concurrent.BlockingCollection<LogJob> logQueue;
        private System.Threading.Thread logThread;
        private string daysPath, tradesPath, ordersPath;
        private const string DaysHeader = "date,status,instrument,qty,filter30m,filterQuote,quote_cutoff,bar30_open,bar30_last,bar30_bullish,bar30_source,tick_open_0900,tick_last,quotes_send_window,quotes_send_bid,quotes_send_ask,quotes_send_pricechg,quotes_0930_window,quotes_0930_bid,quotes_0930_ask,quotes_0930_pricechg,quote_events_100ms_before_send,max_gap_ms_500_before_send,entry_due_et,entry_trigger_now_et,entry_late_ms,data_lag_ms_at_trigger,bid_at_send,ask_at_send,delay_offset_ms,entry_offset_ms,hold_ms";
        private const string TradesHeader = "date,instrument,account,qty,delay_offset_ms,entry_target_fill_et,entry_due_send_et,entry_trigger_now_et,entry_trigger_data_et,entry_submit_start_et,entry_submit_end_et,entry_ack_et,entry_fill_callback_et,entry_fill_broker_et,entry_fill_price,bid_at_send,ask_at_send,bid_at_fill,ask_at_fill,send_minus_due_ms,submit_call_ms,ack_minus_send_ms,fill_callback_minus_send_ms,fill_broker_minus_send_ms,fill_broker_minus_target_ms,fill_price_minus_bid_at_send,stop_price,tp_price,stop_sent_et,stop_ack_et,protection_ack_minus_fill_callback_ms,exit_target_fill_et,exit_due_send_et,exit_trigger_now_et,exit_submit_start_et,exit_submit_end_et,exit_fill_callback_et,exit_fill_broker_et,exit_fill_price,bid_at_exit_send,ask_at_exit_send,exit_send_minus_due_ms,exit_fill_callback_minus_send_ms,exit_fill_broker_minus_send_ms,exit_fill_broker_minus_target_ms,exit_reason,hold_actual_broker_ms,gross_points,gross_usd,quote_events_100ms_before_exit_send,max_gap_ms_500_before_exit_send,exit_sends,flatten_sends,status,note";
        private const string OrdersHeader = "wall_et,event,order_name,action,type,state,qty,filled,limit_price,stop_price,avg_fill_price,broker_time_et,error,native_error";

        private void StartLogWriter()
        {
            if (logQueue != null) return;
            logQueue = new System.Collections.Concurrent.BlockingCollection<LogJob>();
            System.Collections.Concurrent.BlockingCollection<LogJob> q = logQueue;
            logThread = new System.Threading.Thread(() =>
            {
                foreach (LogJob j in q.GetConsumingEnumerable())
                {
                    try
                    {
                        bool exists = File.Exists(j.Path);
                        string text = (exists ? "" : j.Header + "\r\n") + j.Rows;
                        File.AppendAllText(j.Path, text);
                    }
                    catch (Exception) { }
                }
            });
            logThread.IsBackground = true;
            logThread.Name = "SELL2525 log writer";
            logThread.Start();
        }

        private void StopLogWriter()
        {
            try
            {
                System.Collections.Concurrent.BlockingCollection<LogJob> q = logQueue;
                if (q == null) return;
                q.CompleteAdding();
                if (logThread != null) logThread.Join(3000);
                logQueue = null; logThread = null;
            }
            catch (Exception) { }
        }

        private void EnqueueLog(string path, string header, string row)
        {
            try
            {
                if (path == null || logQueue == null || logQueue.IsAddingCompleted) return;
                LogJob j = new LogJob(); j.Path = path; j.Header = header; j.Rows = row + "\r\n";
                logQueue.Add(j);
            }
            catch (Exception) { }
        }

        private static string C(string s) { return s == null ? "" : "\"" + s.Replace("\"", "'") + "\""; }
        private static string F(double v, string fmt) { return double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString(fmt, CultureInfo.InvariantCulture); }
        private string AccountName() { try { return Account == null ? "" : Account.Name; } catch (Exception) { return ""; } }

        private void WriteDayRow(Day d)
        {
            if (d.DayRowWritten) return; d.DayRowWritten = true;
            try
            {
                string row = string.Join(",", new string[] {
                    d.Et.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), C(d.Status), Instrument == null ? "" : Instrument.FullName, d.Qty.ToString(CultureInfo.InvariantCulture), Use30mFilter.ToString(), UseQuoteFilter.ToString(), F(QuoteCutoff, "F1"),
                    F(d.Bar30Open, "F2"), F(d.Bar30Last, "F2"), d.Bar30Ok ? d.Bar30Bullish.ToString() : "", C(d.Bar30Why), F(d.TickOpen900, "F2"), F(d.TickLast, "F2"),
                    d.QSend.ToString(CultureInfo.InvariantCulture), d.QSendBid.ToString(CultureInfo.InvariantCulture), d.QSendAsk.ToString(CultureInfo.InvariantCulture), d.QSendPriceChg.ToString(CultureInfo.InvariantCulture),
                    d.QOpen.ToString(CultureInfo.InvariantCulture), d.QOpenBid.ToString(CultureInfo.InvariantCulture), d.QOpenAsk.ToString(CultureInfo.InvariantCulture), d.QOpenPriceChg.ToString(CultureInfo.InvariantCulture),
                    F(d.Ticks100Before, "F0"), F(d.MaxGapMs500, "F1"), Stamp(ToEastern(d.EntryDueL)), StampP(d.EntryTrigNow), F(Ms(d.EntryTrigNow, d.EntryDueL), "F1"), F(d.DataLagMs, "F1"), F(d.BidSend, "F2"), F(d.AskSend, "F2"),
                    DelayOffsetMs.ToString(CultureInfo.InvariantCulture), EntryOffsetMs.ToString(CultureInfo.InvariantCulture), HoldMs.ToString(CultureInfo.InvariantCulture) });
                EnqueueLog(daysPath, DaysHeader, row);
            }
            catch (Exception ex) { Print("SELL2525 day log error: " + ex.Message); }
        }

        private void WriteTradeRow(Day d)
        {
            if (d.RowWritten) return; d.RowWritten = true;
            try
            {
                double pts = d.FilledQty > 0 && d.ExitFilledQty > 0 ? d.FillPrice - d.ExitPrice : 0.0;
                double usd = pts * Instrument.MasterInstrument.PointValue * Math.Max(1, d.FilledQty);
                string[] f = new string[] {
                    d.Et.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Instrument == null ? "" : Instrument.FullName, C(AccountName()), d.Qty.ToString(CultureInfo.InvariantCulture), DelayOffsetMs.ToString(CultureInfo.InvariantCulture),
                    StampP(d.EntryTargetL), StampP(d.EntryDueL), StampP(d.EntryTrigNow), StampP(d.EntryTrigData), StampP(d.EntrySubmitStart), StampP(d.EntrySubmitEnd), StampP(d.EntryAck), StampP(d.FirstFillCb), StampP(d.FirstFillBroker), F(d.FillPrice, "F2"),
                    F(d.BidSend, "F2"), F(d.AskSend, "F2"), F(d.BidFill, "F2"), F(d.AskFill, "F2"),
                    F(Ms(d.EntrySubmitStart, d.EntryDueL), "F1"), F(Ms(d.EntrySubmitEnd, d.EntrySubmitStart), "F1"), F(Ms(d.EntryAck, d.EntrySubmitStart), "F1"), F(Ms(d.FirstFillCb, d.EntrySubmitStart), "F1"), F(Ms(d.FirstFillBroker, d.EntrySubmitStart), "F1"), F(Ms(d.FirstFillBroker, d.EntryTargetL), "F1"),
                    F(d.FillPrice - d.BidSend, "F2"), F(d.StopPx, "F2"), F(d.TpPx, "F2"), StampP(d.StopPlacedStart), StampP(d.StopPlacedEnd), F(Ms(d.StopPlacedEnd, d.FirstFillCb), "F1"),
                    StampP(d.ExitTargetL), StampP(d.ExitDueL), StampP(d.ExitTrigNow), StampP(d.ExitSubmitStart), StampP(d.ExitSubmitEnd), StampP(d.ExitFillCb), StampP(d.ExitFillBroker), F(d.ExitPrice, "F2"), F(d.BidExitSend, "F2"), F(d.AskExitSend, "F2"),
                    F(Ms(d.ExitSubmitStart, d.ExitDueL), "F1"), F(Ms(d.ExitFillCb, d.ExitSubmitStart), "F1"), F(Ms(d.ExitFillBroker, d.ExitSubmitStart), "F1"), F(Ms(d.ExitFillBroker, d.ExitTargetL), "F1"),
                    C(d.ExitReason), F(Ms(d.ExitFillBroker, d.FirstFillBroker), "F0"), F(pts, "F2"), F(usd, "F2"), F(d.Ticks100BeforeExit, "F0"), F(d.MaxGapMs500Exit, "F1"),
                    d.ExitSends.ToString(CultureInfo.InvariantCulture), d.FlattenSends.ToString(CultureInfo.InvariantCulture), C(d.Status), C(d.Note) };
                EnqueueLog(tradesPath, TradesHeader, string.Join(",", f));
            }
            catch (Exception ex) { Print("SELL2525 trade log error: " + ex.Message); }
        }

        private void LogOrderSend(string name, string action, string type, int qty, double limit, double stop)
        {
            try { EnqueueLog(ordersPath, OrdersHeader, string.Join(",", new string[] { Stamp(ToEastern(StrategyNow)), "SEND", name, action, type, "", qty.ToString(CultureInfo.InvariantCulture), "", F(limit, "F2"), F(stop, "F2"), "", "", "", "" })); } catch (Exception) { }
        }

        private void LogOrderEvent(Order o, double limit, double stop, int qty, int filled, double avg, OrderState st, DateTime time, DateTime cb, ErrorCode err, string nativeErr)
        {
            try { EnqueueLog(ordersPath, OrdersHeader, string.Join(",", new string[] { Stamp(ToEastern(cb)), "ORDER", o.Name, o.OrderAction.ToString(), o.OrderType.ToString(), st.ToString(), qty.ToString(CultureInfo.InvariantCulture), filled.ToString(CultureInfo.InvariantCulture), F(limit, "F2"), F(stop, "F2"), F(avg, "F2"), StampP(time), err.ToString(), C(nativeErr) })); } catch (Exception) { }
        }

        private void LogExecution(string name, string id, double price, int qty, DateTime time, DateTime cb)
        {
            try { EnqueueLog(ordersPath, OrdersHeader, string.Join(",", new string[] { Stamp(ToEastern(cb)), "FILL", name, "", "", "Filled", qty.ToString(CultureInfo.InvariantCulture), "", "", "", F(price, "F2"), StampP(time), "", C(id) })); } catch (Exception) { }
        }

        // ================================================================ Topstep / ProjectX mirror (ported from FAST15-1014 lines 156-260 and 2156-3787; its own SendPlannedProjectXEntry is replaced by the market-entry version above)

        // ---- Topstep / ProjectX mirror: ported unchanged from EMAL-1095 (REST orders to ProjectX accounts, worker thread, orphan recovery), then adapted: no profit target, 1 contract, SELL2525 logging. ----
        private enum ProjectXProtectionOrderKind
        {
            StopLoss,
            TakeProfit
        }

        private sealed class ProjectXAccountInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public bool CanTrade { get; set; }
            public bool IsVisible { get; set; }
        }

        private sealed class ProjectXContractInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string SymbolId { get; set; }
            public bool ActiveContract { get; set; }
        }
        // ProjectX session and mirror state. The actual entry/exit signal names remain stable
        // EMAL-prefixed names; the assembly version is used only for the strategy display name.
        private string projectXSessionToken = string.Empty;
        private DateTime projectXTokenAcquiredUtc = DateTime.MinValue;
        private List<ProjectXAccountInfo> projectXAccounts;
        private string projectXResolvedContractId = string.Empty;
        private string projectXResolvedInstrumentKey = string.Empty;
        private readonly Dictionary<string, long> projectXLastOrderIds = new Dictionary<string, long>();
        private readonly object projectXOrderIdLock = new object();   // EMAL-1083: per-account dispatch tasks share projectXLastOrderIds
        private DateTime projectXUnusableLogUtc = DateTime.MinValue;   // EMAL-1083: throttles the 'query unusable' log line (guarded by projectXOrderIdLock)
        private double projectXLastSyncedStopPrice;
        private double projectXLastSyncedTargetPrice;
        private bool projectXEntryMirrorActive;
        private bool suppressProjectXNextExecutionExit;
        private DateTime projectXOrphanRecoveryDueUtc = DateTime.MinValue;
        private int projectXOrphanRecoveryCount;
        // EMAL-1041: async ProjectX/webhook dispatch. Every ProjectX HTTP round trip used to run
        // synchronously on the strategy thread; measured in live logs, that delayed real order
        // submission (EnterLongLimit/EnterShortLimit) by a median 0.49-0.50s on instances with
        // ProjectX configured, and widened the fill->protection window inside OnExecutionUpdate
        // that the gap-latch work (EMAL-1037/1038) exists to protect. All ProjectX/webhook HTTP
        // now runs on a single dedicated per-instance worker thread, fed by a FIFO queue; the
        // strategy thread only ever enqueues a plain-value work item and returns immediately.
        //
        // Threading contract (READ THIS before touching any ProjectX method):
        //   - The six "mirror state" fields the strategy thread still reads for gating decisions
        //     (projectXEntryMirrorActive, projectXLastSyncedStopPrice/TargetPrice,
        //     suppressProjectXNextExecutionExit, projectXOrphanRecoveryDueUtc/Count) are guarded
        //     by projectXStateLock. EVERY read and write of these six fields, on either thread,
        //     must go through the lock. The worker mutates them itself, from each work item's
        //     OnComplete callback, after the HTTP call resolves - this project chose "guard the
        //     fields with a lock and keep the mutation in the worker" over marshaling results
        //     back to the strategy thread, since NT8 strategies have no cheap "run on next
        //     strategy event" primitive to marshal onto.
        //   - The other ProjectX fields (session token, cached accounts/contract, last order ids)
        //     need NO lock: RunProjectXStartupPreflight touches them synchronously on the
        //     strategy thread once, BEFORE the worker thread is started (see State.Realtime); from
        //     that point on, only the worker thread ever touches them, and it processes the FIFO
        //     queue one item at a time, so there is never more than one thread in this group at
        //     once. Do not call EnsureProjectXSession/TryLoadProjectXAccounts/
        //     TryResolveProjectXContractId/ProjectXPlaceOrder/ProjectXCancelOrders/
        //     ProjectXCancelEntryOrder from the strategy thread after the worker has started, or
        //     this guarantee breaks.
        //   - The worker must NEVER touch NT objects/methods (Instrument, Position, Account,
        //     Order, Time[0]/Close[0], TickSize). Anything instrument- or position-derived a
        //     worker-side method needs (instrument root/key/expiry, TickSize, position side) is
        //     captured as a plain value on the strategy thread at enqueue time and carried on the
        //     ProjectXWorkItem. Print()/ProjectXLog() are the one documented exception - NT8's
        //     Print() is safe from any thread.
        //   - Termination (State.Terminated) is the one place ProjectX HTTP still runs
        //     synchronously: the worker is stopped and drained (bounded wait) first, then
        //     CancelWorkingEntryOnTermination/FlattenProjectXOrphanOnTermination call the same
        //     execution methods directly on the strategy thread, bypassing the queue entirely.
        //     This is safe specifically because the worker has already been joined by that point
        //     - no concurrent access - and keeps FlattenProjectXOrphanOnTermination's existing
        //     bounded-timeout flatten-verification behavior intact.
        private sealed class ProjectXWorkItem
        {
            public string EventType;                       // "buy"/"sell"/"exit"/"cancel"; null for a protection-sync item
            public double EntryPrice;
            public double TakeProfit;
            public double StopLoss;
            public bool IsMarketEntry;
            public int Quantity;
            public ProjectXProtectionOrderKind? ProtectionKind;   // set only for a protection-sync item
            public double ProtectionPrice;
            public string ProtectionReason;
            public int ProtectionExpectedSide;             // 1 = long, 0 = short; captured from Position before enqueue
            public int ProtectionFallbackSize;              // captured Math.Abs(Position.Quantity) fallback, before enqueue
            public string InstrumentRoot;
            public string InstrumentKey;
            public DateTime InstrumentExpiry;
            public bool HasInstrumentExpiry;
            public double TickSizeSnapshot;
            public Action<bool, string> OnComplete;          // (success, rawResponse) - runs on the worker thread
            public DateTime EnqueuedUtc;                     // EMAL-1083: stamped by EnqueueProjectXWork; timing log only
            public bool TerminationDirect;                   // EMAL-1083: set only by SendWebhook (State.Terminated direct send) - bounds the per-account wait
        }

        private readonly object projectXStateLock = new object();
        private System.Collections.Concurrent.BlockingCollection<ProjectXWorkItem> projectXQueue;
        private System.Threading.Thread projectXWorkerThread;

        private const int MirrorQty = 1;                              // the Topstep / ProjectX mirror is always 1 contract

        private void StartProjectXWorker()
        {
            if (projectXWorkerThread != null)
                return;

            projectXQueue = new System.Collections.Concurrent.BlockingCollection<ProjectXWorkItem>();
            projectXWorkerThread = new System.Threading.Thread(ProjectXWorkerLoop)
            {
                IsBackground = true,
                Name = "SELL2525-ProjectX-" + Guid.NewGuid().ToString("N")
            };
            projectXWorkerThread.Start();
        }

        // Called once from State.Terminated, BEFORE CancelWorkingEntryOnTermination/
        // FlattenProjectXOrphanOnTermination run their own direct synchronous sends. Stops new
        // items being accepted and waits (bounded) for whatever is already queued to finish in
        // order, so those two termination methods see up-to-date mirror state. If the drain times
        // out, log it and proceed anyway - termination must not hang indefinitely on ProjectX.
        private void StopProjectXWorker()
        {
            if (projectXQueue == null)
                return;

            try
            {
                projectXTerminating = true;   // the worker skips queued entries from here, so none starts after the shutdown flatten
                projectXQueue.CompleteAdding();
                if (projectXWorkerThread != null && !projectXWorkerThread.Join(20000))   // long enough for one in-flight entry (prepare waits up to ~12 s) so it cannot land after the shutdown flatten
                    ProjectXLog("ProjectX worker did not drain within 20s at termination - proceeding anyway");
            }
            catch (Exception ex)
            {
                ProjectXLog("ProjectX worker shutdown error | error=" + ex.Message);
            }
            finally
            {
                projectXWorkerThread = null;
                projectXQueue = null;
            }
        }

        private void EnqueueProjectXWork(ProjectXWorkItem item)
        {
            if (projectXQueue == null || item == null)
                return;
            try
            {
                item.EnqueuedUtc = DateTime.UtcNow;   // EMAL-1083 (timing log only)
                projectXQueue.Add(item);
                if (item.EventType == "buy" || item.EventType == "sell") projectXMirrorUsed = true;   // an entry really reached the queue
            }
            catch (InvalidOperationException)
            {
                // CompleteAdding() already called (mid-shutdown) - drop it; termination's own
                // direct synchronous sends are what handle ProjectX from this point on.
            }
        }

        private void ProjectXWorkerLoop()
        {
            foreach (ProjectXWorkItem item in projectXQueue.GetConsumingEnumerable())
            {
                if (projectXTerminating && (item.EventType == "buy" || item.EventType == "sell")) continue;
                try
                {
                    ExecuteProjectXWorkItem(item);
                }
                catch (Exception ex)
                {
                    // Must never let one bad item kill the worker - RealtimeErrorHandling=
                    // IgnoreAllErrors means nothing may throw silently anywhere near the strategy
                    // thread, and an exception here would otherwise end the foreach and leave every
                    // later queued item (and everything enqueued after) permanently stuck.
                    try { ProjectXLog("ProjectX worker item failed unexpectedly | error=" + ex.Message); }
                    catch { /* logging itself must never take the worker down either */ }
                }
            }
        }

        // Runs entirely on the worker thread (or, during termination, directly on the strategy
        // thread after the worker has been joined - see StopProjectXWorker). Touches only the
        // item's captured plain values and the worker-only cache fields documented above the
        // ProjectXWorkItem class - no NT object, no NT method, anywhere in this call graph.
        private void ExecuteProjectXWorkItem(ProjectXWorkItem item)
        {
            bool success = false;
            string response = null;
            try
            {
                if (item.ProtectionKind.HasValue)
                {
                    success = ExecuteProjectXProtectionSync(item, out response);
                }
                else
                {
                    success = SendProjectXWork(item, out response);
                }
            }
            catch (Exception ex)
            {
                // EMAL-1083: a throw here used to skip OnComplete (the worker loop's catch swallowed it), which would now lose a claimed orphan-recovery retry. Report failure instead.
                success = false;
                try { ProjectXLog("ProjectX work item failed | event=" + item.EventType + " error=" + ex.Message); }
                catch { }
            }

            if (item.OnComplete != null)
            {
                try
                {
                    item.OnComplete(success, response);
                }
                catch (Exception ex)
                {
                    try { ProjectXLog("ProjectX work item OnComplete failed | error=" + ex.Message); }
                    catch { }
                }
            }
        }

        // Strategy-thread only. Captures everything the worker will need about the current
        // instrument as plain values, so no worker-side code ever has to touch Instrument.
        private void CaptureProjectXInstrumentSnapshot(out string root, out string key,
            out DateTime expiry, out bool hasExpiry)
        {
            root = GetProjectXInstrumentRoot();
            key = GetProjectXInstrumentKey();
            hasExpiry = TryGetInstrumentExpiry(out expiry) || TryParseInstrumentExpiryFromFullName(out expiry);
        }

        private bool IsProjectXConfigured()
        {
            return !string.IsNullOrWhiteSpace(ProjectXApiBaseUrl)
                && !string.IsNullOrWhiteSpace(ProjectXUsername)
                && !string.IsNullOrWhiteSpace(ProjectXApiKey)
                && (ProjectXTradeAllAccounts || !string.IsNullOrWhiteSpace(ProjectXAccountId));
        }

        // Prepares and enqueues the ProjectX entry mirror; does not block. Moved to run AFTER
        // EnterLongLimit/EnterShortLimit in TrySubmitQueuedEntry (EMAL-1041) - previously ran
        // before it, which is exactly the delay this whole change exists to remove, but the
        // enqueue itself is cheap either way now; kept post-submission to match the diagnostics
        // ordering already established in EMAL-1038 for the same reason.

        // synchronousDirect=true is for State.Terminated callers only (see StopProjectXWorker) -
        // the worker has already been joined by then, so calling straight into
        // ExecuteProjectXWorkItem here is safe and deliberately bypasses the queue.
        private void CancelProjectXEntryMirror(bool flattenIfOrphaned, bool synchronousDirect = false)
        {
            bool active;
            lock (projectXStateLock)
                active = projectXEntryMirrorActive;
            if (!active)
                return;

            if (!flattenIfOrphaned)
            {
                // Original behavior: this "cancel" path never branched on the send result, and
                // always cleared the synced prices immediately - neither depends on the HTTP
                // outcome, so both can happen right here regardless of sync/async.
                lock (projectXStateLock)
                {
                    projectXLastSyncedStopPrice = 0.0;
                    projectXLastSyncedTargetPrice = 0.0;
                }
                DispatchProjectXSimpleEvent("cancel", 0, synchronousDirect, null);
                return;
            }

            DispatchProjectXSimpleEvent("exit", 0, synchronousDirect, (sent, response) =>
            {
                lock (projectXStateLock)
                {
                    if (sent)
                    {
                        projectXEntryMirrorActive = false;
                        projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                        projectXOrphanRecoveryCount = 0;
                        projectXLastSyncedStopPrice = 0.0;
                        projectXLastSyncedTargetPrice = 0.0;
                    }
                    else
                    {
                        projectXOrphanRecoveryCount++;
                        projectXOrphanRecoveryDueUtc = DateTime.UtcNow.AddSeconds(
                            Math.Min(60, 5 * projectXOrphanRecoveryCount));
                        ProjectXLog(string.Format(
                            "ProjectX orphan flatten retry scheduled | attempt={0} due={1:HH:mm:ss} UTC",
                            projectXOrphanRecoveryCount, projectXOrphanRecoveryDueUtc));
                    }
                }
            });
        }

        // Shared helper for the "exit"/"cancel" (no entry payload) ProjectX events: builds the
        // work item, captures the instrument snapshot, and either enqueues it (normal trading
        // path) or runs it immediately on the calling thread (termination path only - see
        // synchronousDirect callers). quantityOverride 0 means "use Contracts", same default
        // SendWebhook always used.
        private void DispatchProjectXSimpleEvent(string eventType, int quantityOverride,
            bool synchronousDirect, Action<bool, string> onComplete)
        {
            string instrumentRoot, instrumentKey;
            DateTime instrumentExpiry;
            bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey,
                out instrumentExpiry, out hasInstrumentExpiry);

            var item = new ProjectXWorkItem
            {
                EventType = eventType,
                Quantity = quantityOverride > 0 ? quantityOverride : MirrorQty,
                IsMarketEntry = false,   // unused for "exit"/"cancel" downstream; matches SendWebhook's own default
                InstrumentRoot = instrumentRoot,
                InstrumentKey = instrumentKey,
                InstrumentExpiry = instrumentExpiry,
                HasInstrumentExpiry = hasInstrumentExpiry,
                TickSizeSnapshot = TickSize,
                OnComplete = onComplete,
                TerminationDirect = synchronousDirect   // EMAL-1083: the State.Terminated direct path also gets the bounded wait
            };

            if (synchronousDirect)
                ExecuteProjectXWorkItem(item);
            else
                EnqueueProjectXWork(item);
        }

        private void EvaluateProjectXOrphanRecovery()
        {
            bool active;
            DateTime dueUtc;
            lock (projectXStateLock)
            {
                active = projectXEntryMirrorActive;
                dueUtc = projectXOrphanRecoveryDueUtc;
            }

            if (!active
                || Position.MarketPosition != MarketPosition.Flat
                || AnyTradeLive()
                || dueUtc == DateTime.MinValue
                || DateTime.UtcNow < dueUtc)
            {
                return;
            }

            // EMAL-1083 (single-flight): claim this retry BEFORE dispatching, under the lock, so the next tick sees MinValue and does not queue a second exit while this one runs.
            // The completion callback below re-arms the due time on failure and clears it on success; ExecuteProjectXWorkItem guarantees the callback runs even if the send throws.
            lock (projectXStateLock)
            {
                if (projectXOrphanRecoveryDueUtc == DateTime.MinValue || DateTime.UtcNow < projectXOrphanRecoveryDueUtc)
                    return;
                projectXOrphanRecoveryDueUtc = DateTime.MinValue;
            }

            DispatchProjectXSimpleEvent("exit", 0, false, (sent, response) =>
            {
                lock (projectXStateLock)
                {
                    if (sent)
                    {
                        projectXEntryMirrorActive = false;
                        projectXLastSyncedStopPrice = 0.0;
                        projectXLastSyncedTargetPrice = 0.0;
                        projectXOrphanRecoveryDueUtc = DateTime.MinValue;
                        projectXOrphanRecoveryCount = 0;
                        ProjectXLog("ProjectX orphan flatten recovery succeeded");
                    }
                    else
                    {
                        projectXOrphanRecoveryCount++;
                        projectXOrphanRecoveryDueUtc = DateTime.UtcNow.AddSeconds(
                            Math.Min(60, 5 * projectXOrphanRecoveryCount));
                    }
                }
            });
        }

        private void SendExplicitProjectXExit(string reason)
        {
            bool active;
            lock (projectXStateLock)
                active = projectXEntryMirrorActive;
            if (!active)
                return;

            int quantitySnapshot = Math.Abs(Position.Quantity);
            DispatchProjectXSimpleEvent("exit", quantitySnapshot, false, (sent, response) =>
            {
                if (!sent)
                {
                    // EMAL-1083: a failed explicit flatten used to arm NO retry (recovery then depended on a later NT exit order name). Arm the same back-off retry the other exit paths use;
                    // EvaluateProjectXOrphanRecovery fires it once NT is flat, one at a time.
                    lock (projectXStateLock)
                    {
                        if (projectXEntryMirrorActive)
                        {
                            projectXOrphanRecoveryCount++;
                            projectXOrphanRecoveryDueUtc = DateTime.UtcNow.AddSeconds(
                                Math.Min(60, 5 * projectXOrphanRecoveryCount));
                        }
                    }
                    return;
                }
                lock (projectXStateLock)
                    suppressProjectXNextExecutionExit = true;
                // Time[0]/Close[0] are not valid off the strategy thread - unlike ProjectXLog
                // (which already avoids Time[0]), this Print previously used it as a fallback;
                // dropped here since this callback can run on the worker thread.
                try
                {
                    Print(string.Format("{0} | ProjectX explicit exit sent | reason={1}",
                        lastTickTime != DateTime.MinValue ? lastTickTime : DateTime.Now, reason));
                }
                catch { }
            });
        }

        // EMAL-1041: every trading-path ProjectX call site now builds a ProjectXWorkItem itself
        // and either enqueues it or (termination only) executes it directly - see
        // DispatchProjectXSimpleEvent/CancelProjectXEntryMirror's synchronousDirect parameter.
        // The only remaining caller of this method is FlattenProjectXOrphanOnTermination, which
        // is termination-only by design (runs on the strategy thread, after the worker has
        // already been stopped/drained), so the body below still executes directly and
        // synchronously - never through the queue - deliberately, not as an oversight.
        private bool SendWebhook(string eventType, double entryPrice = 0.0, double takeProfit = 0.0,
            double stopLoss = 0.0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime && State != State.Terminated)
                return false;

            if (!IsProjectXConfigured())
                return false;

            int quantity = quantityOverride > 0 ? quantityOverride : MirrorQty;
            string instrumentRoot, instrumentKey;
            DateTime instrumentExpiry;
            bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey,
                out instrumentExpiry, out hasInstrumentExpiry);

            var directItem = new ProjectXWorkItem
            {
                EventType = eventType,
                EntryPrice = entryPrice,
                TakeProfit = takeProfit,
                StopLoss = stopLoss,
                IsMarketEntry = isMarketEntry,
                Quantity = quantity,
                InstrumentRoot = instrumentRoot,
                InstrumentKey = instrumentKey,
                InstrumentExpiry = instrumentExpiry,
                HasInstrumentExpiry = hasInstrumentExpiry,
                TickSizeSnapshot = TickSize,
                TerminationDirect = true   // EMAL-1083
            };
            string projectXResponse;
            return SendProjectXWork(directItem, out projectXResponse);
        }

        // Worker-thread body for an entry/exit/cancel item. Renamed from SendProjectX (EMAL-1041)
        // to make clear this now only ever runs on the worker (or, during termination, directly
        // on the strategy thread after the worker has been joined - see StopProjectXWorker).
        // Touches only item.* and the worker-only session/account/contract cache.
        private bool SendProjectXWork(ProjectXWorkItem item, out string response)
        {
            response = null;
            if (!IsProjectXConfigured())
                return false;

            if (!EnsureProjectXSession())
                return false;

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetProjectXTargets(item.InstrumentRoot, item.InstrumentKey, item.InstrumentExpiry,
                item.HasInstrumentExpiry, out targetAccounts, out contractId))
                return false;

            string eventKey = (item.EventType ?? string.Empty).ToLowerInvariant();
            DateTime dispatchUtc = DateTime.UtcNow;

            // EMAL-1083 (Steve, 2026-10-07): the accounts are now handled IN PARALLEL, one task each, instead of one after another. Before this, the second account's
            // order only started after the first account's cancel/wait/position-check/place (or flatten) had finished, so it reached the exchange later and could
            // miss a limit fill the first account got (10/06: one Topstep account missed a trade the other filled; exits were ~1.2 s apart). Each account's own steps and
            // their order are unchanged. A single configured account runs inline on this thread exactly as before.
            var results = new bool[targetAccounts.Count];
            var tasks = new System.Threading.Tasks.Task[targetAccounts.Count];
            for (int i = 0; i < targetAccounts.Count; i++)
            {
                int index = i;
                ProjectXAccountInfo account = targetAccounts[i];
                Action work = () => { results[index] = SendProjectXToAccount(item, eventKey, account, contractId, dispatchUtc); };
                if (targetAccounts.Count == 1 && !item.TerminationDirect)
                    work();
                else
                    tasks[i] = System.Threading.Tasks.Task.Factory.StartNew(work, System.Threading.Tasks.TaskCreationOptions.LongRunning);
            }
            try
            {
                System.Threading.Tasks.Task[] running = tasks.Where(t => t != null).ToArray();
                if (item.TerminationDirect)
                {
                    // State.Terminated send on the strategy/UI thread: never wait without a limit (an account that has not finished counts as not sent).
                    if (!System.Threading.Tasks.Task.WaitAll(running, 30000))
                        ProjectXLog("ProjectX termination dispatch timed out after 30s | event=" + item.EventType);
                }
                else
                {
                    // Worker path: unlimited on purpose - a limit would let the next queued item for the same account start while this one is still running.
                    System.Threading.Tasks.Task.WaitAll(running);
                }
            }
            catch (Exception ex)
            {
                ProjectXLog("ProjectX dispatch wait failed | error=" + ex.Message);
            }

            bool sentAny = false;
            bool allSent = true;
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i])
                    sentAny = true;
                else
                    allSent = false;
            }
            // EMAL-1083: an EXIT is reported sent only if every account's flatten succeeded, so a partial failure is retried (the accounts already flat just answer success). Entries/cancels keep the any-account rule.
            return eventKey == "exit" ? allSent : sentAny;
        }

        // One account's steps for an entry/exit/cancel event (the body that used to sit inside the foreach in SendProjectXWork), plus a per-account timing line.
        // queuedMs = enqueue -> dispatch start for the whole event; startMs = enqueue -> this account's first step; elapsedMs = this account's own time.
        private bool SendProjectXToAccount(ProjectXWorkItem item, string eventKey, ProjectXAccountInfo account,
            string contractId, DateTime dispatchUtc)
        {
            bool sent = false;
            DateTime startUtc = DateTime.UtcNow;
            try
            {
                switch (eventKey)
                {
                    case "buy":
                    case "sell":
                        if (ProjectXPrepareForEntry(account.Id, contractId)
                            && ProjectXPlaceOrder(item.EventType, account.Id, contractId, item.EntryPrice,
                                item.TakeProfit, item.StopLoss, item.IsMarketEntry, item.Quantity,
                                item.TickSizeSnapshot))
                        {
                            sent = true;
                        }
                        break;

                    case "exit":
                        if (ProjectXFlattenPosition(account.Id, contractId))
                            sent = true;
                        break;

                    case "cancel":
                        ProjectXCancelEntryOrder(account.Id, contractId);
                        sent = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                ProjectXLog(string.Format(
                    "ProjectX account error | event={0} accountId={1} name={2} error={3}",
                    item.EventType, account.Id, account.Name ?? string.Empty, ex.Message));
            }

            try
            {
                DateTime doneUtc = DateTime.UtcNow;
                bool stamped = item.EnqueuedUtc != DateTime.MinValue;
                ProjectXLog(string.Format(CultureInfo.InvariantCulture,
                    "ProjectX dispatch timing | event={0} account={1} name={2} queuedMs={3:F0} startMs={4:F0} elapsedMs={5:F0} ok={6} doneUtc={7:HH:mm:ss.fff}",
                    item.EventType, account.Id, account.Name ?? string.Empty,
                    stamped ? (dispatchUtc - item.EnqueuedUtc).TotalMilliseconds : 0.0,
                    stamped ? (startUtc - item.EnqueuedUtc).TotalMilliseconds : 0.0,
                    (doneUtc - startUtc).TotalMilliseconds, sent, doneUtc));
            }
            catch { /* the timing line must never affect an order */ }
            return sent;
        }

        // Strategy-thread only, called once from State.Realtime, BEFORE the worker thread is
        // started (see OnStateChange) - this is what makes it safe for the session/account/
        // contract cache fields below to need no lock: this call and the worker's later calls
        // never overlap.
        private void RunProjectXStartupPreflight()
        {
            if (!IsProjectXConfigured())
            {
                ProjectXLog("ProjectX inactive | configure Username, API and Accounts to enable");
                return;
            }

            string instrumentRoot, instrumentKey;
            DateTime instrumentExpiry;
            bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey,
                out instrumentExpiry, out hasInstrumentExpiry);

            ProjectXLog(string.Format(
                "ProjectX startup preflight begin | instrument={0} selectors={1}",
                instrumentKey, ProjectXAccountId ?? string.Empty));

            if (!EnsureProjectXSession())
            {
                ProjectXLog("ProjectX startup preflight failed | stage=auth");
                return;
            }

            List<ProjectXAccountInfo> targets;
            string contractId;
            if (!TryGetProjectXTargets(instrumentRoot, instrumentKey, instrumentExpiry, hasInstrumentExpiry,
                out targets, out contractId))
            {
                ProjectXLog("ProjectX startup preflight failed | stage=targets");
                return;
            }

            ProjectXLog(string.Format(
                "ProjectX startup preflight ready | accounts={0} contractId={1}",
                FormatProjectXAccountsForLog(targets), contractId));
        }

        private bool EnsureProjectXSession()
        {
            if (!IsProjectXConfigured())
                return false;

            if (!string.IsNullOrWhiteSpace(projectXSessionToken)
                && (DateTime.UtcNow - projectXTokenAcquiredUtc).TotalHours < 23.0)
            {
                return true;
            }

            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"userName\":\"{0}\",\"apiKey\":\"{1}\"}}",
                JsonEscape(ProjectXUsername), JsonEscape(ProjectXApiKey));
            string response = ProjectXPost("/api/Auth/loginKey", json, false, true);
            string token;
            if (!TryGetJsonString(response, "token", out token))
            {
                ProjectXLog("ProjectX login failed | token missing");
                return false;
            }

            projectXSessionToken = token;
            projectXTokenAcquiredUtc = DateTime.UtcNow;
            projectXAccounts = null;
            projectXResolvedContractId = string.Empty;
            projectXResolvedInstrumentKey = string.Empty;
            lock (projectXOrderIdLock)
                projectXLastOrderIds.Clear();
            ProjectXLog("ProjectX login succeeded");
            return true;
        }

        private bool TryGetProjectXTargets(string instrumentRoot, string instrumentKey,
            DateTime instrumentExpiry, bool hasInstrumentExpiry,
            out List<ProjectXAccountInfo> targetAccounts, out string contractId)
        {
            targetAccounts = null;
            contractId = null;
            if (!TryResolveProjectXContractId(instrumentRoot, instrumentKey, instrumentExpiry,
                hasInstrumentExpiry, out contractId))
                return false;

            List<ProjectXAccountInfo> accounts;
            if (!TryLoadProjectXAccounts(out accounts))
                return false;

            if (ProjectXTradeAllAccounts)
            {
                targetAccounts = accounts.Where(a => a.CanTrade).ToList();
                return targetAccounts.Count > 0;
            }

            List<string> selectors = ParseProjectXAccountSelectors(ProjectXAccountId);
            if (selectors.Count == 0)
            {
                ProjectXLog("ProjectX target selection failed | ProjectX Accounts is empty");
                return false;
            }

            var matched = new List<ProjectXAccountInfo>();
            var matchedIds = new HashSet<int>();
            foreach (string selector in selectors)
            {
                int id;
                IEnumerable<ProjectXAccountInfo> candidates = int.TryParse(
                    selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
                    ? accounts.Where(a => a.CanTrade && a.Id == id)
                    : accounts.Where(a => a.CanTrade
                        && string.Equals(a.Name ?? string.Empty, selector, StringComparison.OrdinalIgnoreCase));

                foreach (ProjectXAccountInfo account in candidates)
                {
                    if (matchedIds.Add(account.Id))
                        matched.Add(account);
                }
            }

            targetAccounts = matched;
            if (targetAccounts.Count == 0)
                ProjectXLog("ProjectX target selection failed | no matching tradable accounts");
            return targetAccounts.Count > 0;
        }

        private bool TryLoadProjectXAccounts(out List<ProjectXAccountInfo> accounts)
        {
            if (projectXAccounts != null && projectXAccounts.Count > 0)
            {
                accounts = projectXAccounts;
                return true;
            }

            string response = ProjectXPost("/api/Account/search", "{\"onlyActiveAccounts\":true}", true, true);
            accounts = ExtractProjectXAccounts(response).ToList();
            projectXAccounts = accounts.Count > 0 ? accounts : null;
            ProjectXLog(string.Format("ProjectX accounts found | count={0}", accounts.Count));
            return accounts.Count > 0;
        }

        // instrumentRoot/instrumentKey/instrumentExpiry are captured on the strategy thread
        // (CaptureProjectXInstrumentSnapshot) before this is ever called - no Instrument access
        // here, so this is safe from the worker thread.
        private bool TryResolveProjectXContractId(string instrumentRoot, string instrumentKey,
            DateTime instrumentExpiry, bool hasInstrumentExpiry, out string contractId)
        {
            contractId = null;
            if (!string.IsNullOrWhiteSpace(ProjectXContractId))
            {
                contractId = ProjectXContractId.Trim();
                return true;
            }

            if (!string.IsNullOrWhiteSpace(projectXResolvedContractId)
                && string.Equals(projectXResolvedInstrumentKey, instrumentKey, StringComparison.OrdinalIgnoreCase))
            {
                contractId = projectXResolvedContractId;
                return true;
            }

            if (string.IsNullOrWhiteSpace(instrumentRoot))
                return false;

            string suffix = hasInstrumentExpiry
                ? GetProjectXFuturesMonthCode(instrumentExpiry.Month)
                    + instrumentExpiry.ToString("yy", CultureInfo.InvariantCulture)
                : string.Empty;

            List<ProjectXContractInfo> contracts;
            if (!TrySearchProjectXContracts(instrumentRoot, suffix, out contracts))
                return false;

            ProjectXContractInfo selected = SelectProjectXContract(suffix, contracts);
            if (selected == null || string.IsNullOrWhiteSpace(selected.Id))
                return false;

            contractId = selected.Id;
            projectXResolvedContractId = contractId;
            projectXResolvedInstrumentKey = instrumentKey;
            ProjectXLog(string.Format("ProjectX contract resolved | instrument={0} contractId={1}",
                instrumentKey, contractId));
            return true;
        }

        private bool TrySearchProjectXContracts(string root, string suffix, out List<ProjectXContractInfo> contracts)
        {
            string primary = string.IsNullOrWhiteSpace(suffix) ? root : root + suffix;
            if (TrySearchProjectXContractsByText(primary, root, true, out contracts) && contracts.Count > 0)
                return true;
            if (TrySearchProjectXContractsByText(primary, root, false, out contracts) && contracts.Count > 0)
                return true;
            if (!string.Equals(primary, root, StringComparison.OrdinalIgnoreCase))
            {
                if (TrySearchProjectXContractsByText(root, root, true, out contracts) && contracts.Count > 0)
                    return true;
                if (TrySearchProjectXContractsByText(root, root, false, out contracts) && contracts.Count > 0)
                    return true;
            }
            contracts = new List<ProjectXContractInfo>();
            return false;
        }

        private bool TrySearchProjectXContractsByText(string searchText, string root, bool live,
            out List<ProjectXContractInfo> contracts)
        {
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"live\":{0},\"searchText\":\"{1}\"}}",
                live ? "true" : "false", JsonEscape(searchText));
            string response = ProjectXPost("/api/Contract/search", json, true, true);
            contracts = ExtractProjectXContracts(response)
                .Where(c => DoesProjectXContractMatchRoot(c, root))
                .ToList();
            return !string.IsNullOrWhiteSpace(response);
        }

        private ProjectXContractInfo SelectProjectXContract(string suffix, List<ProjectXContractInfo> contracts)
        {
            if (contracts == null || contracts.Count == 0)
                return null;
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                List<ProjectXContractInfo> exact = contracts
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id)
                        && c.Id.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (exact.Count > 0)
                    return exact.FirstOrDefault(c => c.ActiveContract) ?? exact[0];
            }
            return contracts.FirstOrDefault(c => c.ActiveContract) ?? contracts[0];
        }

        private bool DoesProjectXContractMatchRoot(ProjectXContractInfo contract, string root)
        {
            if (contract == null || string.IsNullOrWhiteSpace(root))
                return false;
            return (!string.IsNullOrWhiteSpace(contract.SymbolId)
                    && string.Equals(contract.SymbolId, "F.US." + root, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(contract.Id)
                    && contract.Id.IndexOf(".US." + root + ".", StringComparison.OrdinalIgnoreCase) >= 0)
                || (!string.IsNullOrWhiteSpace(contract.Name)
                    && contract.Name.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        private string GetProjectXInstrumentKey()
        {
            return Instrument != null && !string.IsNullOrWhiteSpace(Instrument.FullName)
                ? Instrument.FullName.Trim().ToUpperInvariant()
                : GetProjectXInstrumentRoot();
        }

        private string GetProjectXInstrumentRoot()
        {
            return Instrument != null && Instrument.MasterInstrument != null
                ? (Instrument.MasterInstrument.Name ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
        }

        private bool TryGetInstrumentExpiry(out DateTime expiry)
        {
            expiry = Core.Globals.MinDate;
            if (Instrument == null)
                return false;
            try
            {
                PropertyInfo property = Instrument.GetType().GetProperty(
                    "Expiry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                object raw = property != null ? property.GetValue(Instrument, null) : null;
                if (!(raw is DateTime) || ((DateTime)raw).Year < 2000)
                    return false;
                expiry = (DateTime)raw;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseInstrumentExpiryFromFullName(out DateTime expiry)
        {
            expiry = Core.Globals.MinDate;
            string fullName = Instrument != null ? (Instrument.FullName ?? string.Empty) : string.Empty;
            Match match = Regex.Match(fullName, @"\b(?<month>\d{1,2})[-/](?<year>\d{2,4})\b");
            int month;
            int year;
            if (!match.Success
                || !int.TryParse(match.Groups["month"].Value, out month)
                || !int.TryParse(match.Groups["year"].Value, out year))
                return false;
            if (year < 100)
                year += 2000;
            if (month < 1 || month > 12 || year < 2000)
                return false;
            expiry = new DateTime(year, month, 1);
            return true;
        }

        private string GetProjectXFuturesMonthCode(int month)
        {
            const string codes = " FGHJKMNQUVXZ";
            return month >= 1 && month <= 12 ? codes[month].ToString() : string.Empty;
        }

        private List<string> ParseProjectXAccountSelectors(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string FormatProjectXAccountsForLog(IEnumerable<ProjectXAccountInfo> accounts)
        {
            return accounts == null ? "<none>" : string.Join(", ", accounts.Select(a =>
                string.Format(CultureInfo.InvariantCulture, "{0}:{1}", a.Id, a.Name ?? string.Empty)).ToArray());
        }

        // Strategy-thread only: all the cheap gating/dedup checks stay here (no HTTP, so no
        // reason to touch the queue at all if nothing would actually change), then captures
        // everything the worker needs and enqueues. The actual HTTP work happens in
        // ExecuteProjectXProtectionSync, entirely on the worker thread. This is the call site
        // v1037/1038's gap-latch work exists to keep clear of the fill callback - it runs from
        // inside OnExecutionUpdate, and used to block on HTTP right there.
        private void SyncProjectXProtectionUpdate(ProjectXProtectionOrderKind kind, double price, string reason)
        {
            bool mirrorActive;
            lock (projectXStateLock)
                mirrorActive = projectXEntryMirrorActive;

            if (State != State.Realtime
                || !IsProjectXConfigured()
                || !mirrorActive
                || Position.MarketPosition == MarketPosition.Flat)
            {
                return;
            }

            price = Instrument.MasterInstrument.RoundToTickSize(price);
            if (price <= 0.0 || double.IsNaN(price) || double.IsInfinity(price))
                return;

            double lastPrice;
            lock (projectXStateLock)
            {
                lastPrice = kind == ProjectXProtectionOrderKind.StopLoss
                    ? projectXLastSyncedStopPrice
                    : projectXLastSyncedTargetPrice;
            }
            if (lastPrice > 0.0 && Math.Abs(lastPrice - price) < TickSize / 2.0)
                return;

            string instrumentRoot, instrumentKey;
            DateTime instrumentExpiry;
            bool hasInstrumentExpiry;
            CaptureProjectXInstrumentSnapshot(out instrumentRoot, out instrumentKey,
                out instrumentExpiry, out hasInstrumentExpiry);

            var item = new ProjectXWorkItem
            {
                ProtectionKind = kind,
                ProtectionPrice = price,
                ProtectionReason = reason ?? string.Empty,
                ProtectionExpectedSide = Position.MarketPosition == MarketPosition.Long ? 1 : 0,
                ProtectionFallbackSize = Math.Max(1, Math.Abs(Position.Quantity)),
                InstrumentRoot = instrumentRoot,
                InstrumentKey = instrumentKey,
                InstrumentExpiry = instrumentExpiry,
                HasInstrumentExpiry = hasInstrumentExpiry,
                TickSizeSnapshot = TickSize,
                OnComplete = (modifiedAny, response) =>
                {
                    if (!modifiedAny)
                        return;
                    lock (projectXStateLock)
                    {
                        if (kind == ProjectXProtectionOrderKind.StopLoss)
                            projectXLastSyncedStopPrice = price;
                        else
                            projectXLastSyncedTargetPrice = price;
                    }
                }
            };

            EnqueueProjectXWork(item);
        }

        // Worker-thread body for a protection-sync item. Touches only item.* and the
        // worker-only session/account/contract cache - no NT object, no NT method.
        private bool ExecuteProjectXProtectionSync(ProjectXWorkItem item, out string response)
        {
            response = null;
            if (!EnsureProjectXSession())
                return false;

            List<ProjectXAccountInfo> targets;
            string contractId;
            if (!TryGetProjectXTargets(item.InstrumentRoot, item.InstrumentKey, item.InstrumentExpiry,
                item.HasInstrumentExpiry, out targets, out contractId))
                return false;

            ProjectXProtectionOrderKind kind = item.ProtectionKind.Value;

            // EMAL-1083: accounts are modified in parallel (see SendProjectXWork); each account's own steps are unchanged.
            var modified = new bool[targets.Count];
            var responses = new string[targets.Count];
            var tasks = new System.Threading.Tasks.Task[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                int index = i;
                ProjectXAccountInfo account = targets[i];
                Action work = () => { modified[index] = SyncProjectXProtectionForAccount(item, kind, account, contractId, out responses[index]); };
                if (targets.Count == 1)
                    work();
                else
                    tasks[i] = System.Threading.Tasks.Task.Factory.StartNew(work, System.Threading.Tasks.TaskCreationOptions.LongRunning);
            }
            try
            {
                System.Threading.Tasks.Task.WaitAll(tasks.Where(t => t != null).ToArray());
            }
            catch (Exception ex)
            {
                ProjectXLog("ProjectX protection sync wait failed | error=" + ex.Message);
            }

            bool modifiedAny = false;
            for (int i = 0; i < targets.Count; i++)
            {
                if (modified[i])
                    modifiedAny = true;
                if (responses[i] != null)
                    response = responses[i];
            }
            return modifiedAny;
        }

        // One account's protection-order modify (the body that used to sit inside the foreach in ExecuteProjectXProtectionSync).
        private bool SyncProjectXProtectionForAccount(ProjectXWorkItem item, ProjectXProtectionOrderKind kind,
            ProjectXAccountInfo account, string contractId, out string response)
        {
            response = null;
            try
            {
                Dictionary<string, object> order = SelectProjectXProtectionOrder(
                    account.Id, contractId, kind, item.ProtectionExpectedSide);
                if (order == null)
                {
                    ProjectXLog(string.Format(
                        "ProjectX protection sync skipped | account={0} kind={1} reason=no-unique-open-order",
                        account.Id, kind));
                    return false;
                }

                long orderId;
                int size;
                if (!TryGetProjectXOrderLong(order, "id", out orderId) || orderId <= 0)
                    return false;
                if (!TryGetProjectXOrderInt(order, "size", out size) || size <= 0)
                    size = item.ProtectionFallbackSize;

                response = ProjectXModifyProtectionOrder(account.Id, orderId, size, kind, item.ProtectionPrice);
                bool success;
                if (!TryGetJsonBool(response, "success", out success) || success)
                    return true;
                ProjectXLog(string.Format(
                    "ProjectX protection sync failed | account={0} order={1} kind={2} price={3:0.00} reason={4}",
                    account.Id, orderId, kind, item.ProtectionPrice, item.ProtectionReason));
                return false;
            }
            catch (Exception ex)
            {
                ProjectXLog(string.Format("ProjectX protection sync error | account={0} error={1}", account.Id, ex.Message));
                return false;
            }
        }

        private Dictionary<string, object> SelectProjectXProtectionOrder(int accountId, string contractId,
            ProjectXProtectionOrderKind kind, int expectedSide)
        {
            List<Dictionary<string, object>> matches = GetProjectXOpenOrders(accountId, contractId)
                .Where(o => IsProjectXProtectionOrderMatch(o, kind, expectedSide))
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private bool IsProjectXProtectionOrderMatch(Dictionary<string, object> order,
            ProjectXProtectionOrderKind kind, int expectedSide)
        {
            int side;
            if (TryGetProjectXOrderInt(order, "side", out side) && side != expectedSide)
                return false;

            int type;
            if (TryGetProjectXOrderInt(order, "type", out type))
                return kind == ProjectXProtectionOrderKind.StopLoss ? type == 4 : type == 1;

            double price;
            return kind == ProjectXProtectionOrderKind.StopLoss
                ? TryGetProjectXOrderDouble(order, "stopPrice", out price) && price > 0.0
                : TryGetProjectXOrderDouble(order, "limitPrice", out price) && price > 0.0;
        }

        private string ProjectXModifyProtectionOrder(int accountId, long orderId, int size,
            ProjectXProtectionOrderKind kind, double price)
        {
            string limit = kind == ProjectXProtectionOrderKind.TakeProfit ? FormatProjectXPriceRaw(price) : "null";
            string stop = kind == ProjectXProtectionOrderKind.StopLoss ? FormatProjectXPriceRaw(price) : "null";
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"orderId\":{1},\"size\":{2},\"limitPrice\":{3},\"stopPrice\":{4},\"trailPrice\":null}}",
                accountId, orderId, Math.Max(1, size), limit, stop);
            return ProjectXPost("/api/Order/modify", json, true);
        }

        // entryPrice/takeProfit/stopLoss arrive already tick-rounded from SendPlannedProjectXEntry
        // (strategy thread, before enqueue) - no Instrument access needed or wanted here.
        // tickSize is the enqueue-time TickSize snapshot (ProjectXWorkItem.TickSizeSnapshot).
        private bool ProjectXPlaceOrder(string side, int accountId, string contractId,
            double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity,
            double tickSize)
        {
            int orderSide = string.Equals(side, "buy", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int orderType = isMarketEntry ? 2 : 1;
            int normalizedQuantity = Math.Max(1, quantity);
            double entry = entryPrice;
            bool isLong = orderSide == 0;
            int tpTicks = NormalizeProjectXBracketTicks(
                PriceToTicks(takeProfit - entry, tickSize), 4, isLong ? 1 : -1);
            int slTicks = NormalizeProjectXBracketTicks(
                PriceToTicks(stopLoss - entry, tickSize), 1, isLong ? -1 : 1);
            string limitPart = isMarketEntry
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, ",\"limitPrice\":{0}", FormatProjectXPriceRaw(entry));
            string tpPart = takeProfit > 0.0
                ? string.Format(CultureInfo.InvariantCulture, ",\"takeProfitBracket\":{{\"quantity\":{0},\"type\":1,\"ticks\":{1}}}", normalizedQuantity, tpTicks)
                : string.Empty;   // SELL2525 has no profit target (timed exit), so the ProjectX order carries only the stop bracket
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\",\"type\":{2},\"side\":{3},\"size\":{4}{5}{6},\"stopLossBracket\":{{\"quantity\":{4},\"type\":4,\"ticks\":{7}}}}}",
                accountId, JsonEscape(contractId), orderType, orderSide, normalizedQuantity,
                limitPart, tpPart, slTicks);
            string response = ProjectXPost("/api/Order/place", json, true);
            bool success;
            if (TryGetJsonBool(response, "success", out success) && !success)
                return false;
            long orderId;
            if (TryGetJsonLong(response, "orderId", out orderId) && orderId > 0)
                lock (projectXOrderIdLock)
                    projectXLastOrderIds[GetProjectXOrderKey(accountId, contractId)] = orderId;
            return !string.IsNullOrWhiteSpace(response);
        }

        private int NormalizeProjectXBracketTicks(int rawTicks, int minAbsoluteTicks, int zeroDirection)
        {
            int direction = rawTicks == 0 ? Math.Sign(zeroDirection) : Math.Sign(rawTicks);
            return direction * Math.Max(minAbsoluteTicks, Math.Abs(rawTicks));
        }

        private bool ProjectXPrepareForEntry(int accountId, string contractId)
        {
            ProjectXCancelOrders(accountId, contractId);
            if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
                return false;

            int positionSize;
            if (!TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize))
            {
                ProjectXLog(string.Format(
                    "ProjectX prepare failed | account={0} reason=position-query-failed", accountId));
                return false;
            }

            if (positionSize != 0)
            {
                ProjectXClosePosition(accountId, contractId);
                if (!WaitForProjectXFlat(accountId, contractId, 4000))
                    return false;
                ProjectXCancelOrders(accountId, contractId);
                if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
                    return false;
            }
            return true;
        }

        private bool ProjectXFlattenPosition(int accountId, string contractId)
        {
            ProjectXCancelOrders(accountId, contractId);
            if (!WaitForProjectXOrdersCleared(accountId, contractId, 4000))
                ProjectXLog(string.Format("ProjectX flatten warning | account={0} orders-not-cleared", accountId));

            int positionSize;
            if (!TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize))
            {
                ProjectXLog(string.Format(
                    "ProjectX flatten failed | account={0} reason=position-query-failed", accountId));
                return false;
            }

            if (positionSize != 0)
            {
                ProjectXClosePosition(accountId, contractId);
                if (!WaitForProjectXFlat(accountId, contractId, 4000))
                {
                    ProjectXLog(string.Format("ProjectX flatten warning | account={0} position={1}",
                        accountId, positionSize));
                    return false;
                }
            }

            ProjectXCancelOrders(accountId, contractId);
            return WaitForProjectXOrdersCleared(accountId, contractId, 4000);
        }

        private string ProjectXClosePosition(int accountId, string contractId)
        {
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\"}}",
                accountId, JsonEscape(contractId));
            return ProjectXPost("/api/Position/closeContract", json, true);
        }

        private void ProjectXCancelOrders(int accountId, string contractId)
        {
            List<long> openOrderIds;
            if (!TryGetProjectXOpenOrderIds(accountId, contractId, out openOrderIds))
            {
                ProjectXLog(string.Format("ProjectX cancel skipped | account={0} reason=order-query-unusable", accountId));
                openOrderIds = new List<long>();
            }
            foreach (long orderId in openOrderIds)
            {
                string json = string.Format(CultureInfo.InvariantCulture,
                    "{{\"accountId\":{0},\"orderId\":{1}}}", accountId, orderId);
                ProjectXPost("/api/Order/cancel", json, true);
            }
            lock (projectXOrderIdLock)
                projectXLastOrderIds.Remove(GetProjectXOrderKey(accountId, contractId));
        }

        private void ProjectXCancelEntryOrder(int accountId, string contractId)
        {
            string key = GetProjectXOrderKey(accountId, contractId);
            long orderId;
            lock (projectXOrderIdLock)
            {
                if (!projectXLastOrderIds.TryGetValue(key, out orderId) || orderId <= 0)
                    return;
            }

            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"orderId\":{1}}}", accountId, orderId);
            ProjectXPost("/api/Order/cancel", json, true);
            lock (projectXOrderIdLock)
                projectXLastOrderIds.Remove(key);
        }

        private bool WaitForProjectXFlat(int accountId, string contractId, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            do
            {
                int positionSize;
                bool queryOk = TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize);
                if (queryOk && positionSize == 0)
                    return true;
                System.Threading.Thread.Sleep(queryOk ? 150 : 500);   // EMAL-1083: back off while the query keeps failing
            }
            while (DateTime.UtcNow <= deadline);
            return false;
        }

        private bool WaitForProjectXOrdersCleared(int accountId, string contractId, int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            do
            {
                List<long> openOrderIds;
                bool queryOk = TryGetProjectXOpenOrderIds(accountId, contractId, out openOrderIds);
                if (queryOk && openOrderIds.Count == 0)
                    return true;   // EMAL-1083: only a USABLE empty reply counts as cleared
                System.Threading.Thread.Sleep(queryOk ? 150 : 500);   // EMAL-1083: back off while the query keeps failing
            }
            while (DateTime.UtcNow <= deadline);
            return false;
        }

        // EMAL-1083: returns false when the order query reply is unusable (see IsProjectXQueryResponseUsable) so "query failed" is never mistaken for "no open orders".
        private bool TryGetProjectXOpenOrderIds(int accountId, string contractId, out List<long> ids)
        {
            ids = new List<long>();
            string json = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string response = ProjectXPost("/api/Order/searchOpen", json, true);
            if (!IsProjectXQueryResponseUsable(response, "orders"))
            {
                LogProjectXQueryUnusable("order", accountId);
                return false;
            }
            foreach (Dictionary<string, object> order in ExtractProjectXCollection(response, "orders")
                .Where(o => ProjectXDictionaryValueEquals(o, "contractId", contractId)))
            {
                long id;
                if (TryGetProjectXOrderLong(order, "id", out id) && id > 0)
                    ids.Add(id);
            }
            return true;
        }

        private List<Dictionary<string, object>> GetProjectXOpenOrders(int accountId, string contractId)
        {
            string json = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string response = ProjectXPost("/api/Order/searchOpen", json, true);
            return ExtractProjectXCollection(response, "orders")
                .Where(o => ProjectXDictionaryValueEquals(o, "contractId", contractId))
                .ToList();
        }

        // EMAL-1083: a query reply that is missing, not JSON, success:false, or has neither a success flag nor the expected list is NOT "flat" / "no orders" - it is UNKNOWN, so callers get false.
        // A usable reply has success:true, or (when no success flag is present) the collection key itself, even if the list is empty.
        // EMAL-1083: at most one 'unusable' line per 5 s across all accounts (the wait loops poll repeatedly while a query keeps failing).
        private void LogProjectXQueryUnusable(string what, int accountId)
        {
            lock (projectXOrderIdLock)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - projectXUnusableLogUtc).TotalSeconds < 5.0)
                    return;
                projectXUnusableLogUtc = now;
            }
            ProjectXLog(string.Format("ProjectX {0} query unusable | account={1} - treated as UNKNOWN, not empty/flat (further lines suppressed for 5 s)", what, accountId));
        }

        private bool IsProjectXQueryResponseUsable(string response, string collectionKey)
        {
            Dictionary<string, object> data;
            if (!TryDeserializeProjectXObject(response, out data))
                return false;
            object rawSuccess;
            if (data.TryGetValue("success", out rawSuccess))
            {
                bool ok;
                return TryConvertToBool(rawSuccess, out ok) && ok;
            }
            object rawItems;
            return data.TryGetValue(collectionKey, out rawItems) && rawItems is System.Collections.IEnumerable;
        }

        private bool TryGetProjectXOpenPositionSize(int accountId, string contractId, out int signedSize)
        {
            signedSize = 0;
            string json = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string response = ProjectXPost("/api/Position/searchOpen", json, true);
            if (!IsProjectXQueryResponseUsable(response, "positions"))
            {
                LogProjectXQueryUnusable("position", accountId);
                return false;
            }

            foreach (Dictionary<string, object> position in ExtractProjectXCollection(response, "positions"))
            {
                if (!ProjectXDictionaryValueEquals(position, "contractId", contractId))
                    continue;
                int type;
                int size;
                object rawType;
                object rawSize;
                if (!position.TryGetValue("type", out rawType) || !TryConvertToInt(rawType, out type)
                    || !position.TryGetValue("size", out rawSize) || !TryConvertToInt(rawSize, out size))
                    continue;
                signedSize += type == 2 ? -Math.Abs(size) : Math.Abs(size);
            }
            return true;
        }

        private bool ProjectXDictionaryValueEquals(Dictionary<string, object> data, string key, string expected)
        {
            object raw;
            return data != null && data.TryGetValue(key, out raw)
                && string.Equals(raw != null ? raw.ToString() : string.Empty,
                    expected ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private string GetProjectXOrderKey(int accountId, string contractId)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}", accountId, contractId ?? string.Empty);
        }

        // Worker-safe: every ProjectX caller of this (ProjectXPlaceOrder, ProjectXModifyProtectionOrder)
        // already receives a price rounded on the strategy thread before enqueue - no Instrument
        // access needed or wanted here.
        private string FormatProjectXPriceRaw(double alreadyRoundedPrice)
        {
            return alreadyRoundedPrice.ToString("0.########", CultureInfo.InvariantCulture);
        }

        // tickSize is captured on the strategy thread at enqueue time (ProjectXWorkItem.TickSizeSnapshot)
        // rather than read from the NT TickSize property here, so this is safe on the worker thread.
        private int PriceToTicks(double distance, double tickSize)
        {
            return tickSize > 0.0
                ? (int)Math.Round(distance / tickSize, MidpointRounding.AwayFromZero)
                : 0;
        }

        private string ProjectXPost(string path, string json, bool requiresAuthentication)
        {
            return ProjectXPost(path, json, requiresAuthentication, false);
        }

        private string ProjectXPost(string path, string json, bool requiresAuthentication, bool alwaysLog)
        {
            string baseUrl = (ProjectXApiBaseUrl ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            try
            {
                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    if (requiresAuthentication && !string.IsNullOrWhiteSpace(projectXSessionToken))
                        client.Headers[System.Net.HttpRequestHeader.Authorization] = "Bearer " + projectXSessionToken;
                    string response = client.UploadString(baseUrl + path, "POST", json);
                    if (alwaysLog)
                        ProjectXLog(string.Format("ProjectX response | path={0} body={1}",
                            path, SanitizeProjectXJsonForLog(response)));
                    return response;
                }
            }
            catch (System.Net.WebException ex)
            {
                string body = ReadWebExceptionResponse(ex);
                ProjectXLog(string.Format("ProjectX request failed | path={0} error={1} body={2}",
                    path, ex.Message, SanitizeProjectXJsonForLog(body)));
                return body;
            }
            catch (Exception ex)
            {
                ProjectXLog(string.Format("ProjectX request failed | path={0} error={1}", path, ex.Message));
                return null;
            }
        }

        private string ReadWebExceptionResponse(System.Net.WebException exception)
        {
            try
            {
                if (exception == null || exception.Response == null)
                    return null;
                using (Stream stream = exception.Response.GetResponseStream())
                using (var reader = stream != null ? new StreamReader(stream) : null)
                    return reader != null ? reader.ReadToEnd() : null;
            }
            catch
            {
                return null;
            }
        }

        private void ProjectXLog(string message)
        {
            Print(string.Format("{0} | SELL2525 | {1}",
                lastTickTime != DateTime.MinValue ? lastTickTime : DateTime.Now,
                message ?? string.Empty));
        }

        private string JsonEscape(string value)
        {
            if (value == null)
                return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private string SanitizeProjectXJsonForLog(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;
            string sanitized = json;
            foreach (string key in new[] { "apiKey", "loginKey", "token", "newToken" })
            {
                sanitized = Regex.Replace(sanitized,
                    "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"[^\"]*\"",
                    "\"" + key + "\":\"***\"");
            }
            return sanitized;
        }

        private bool TryGetJsonString(string json, string key, out string value)
        {
            value = null;
            object raw;
            Dictionary<string, object> data;
            if (!TryDeserializeProjectXObject(json, out data)
                || !data.TryGetValue(key, out raw) || raw == null)
                return false;
            value = raw.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        private bool TryGetJsonLong(string json, string key, out long value)
        {
            value = 0;
            object raw;
            Dictionary<string, object> data;
            return TryDeserializeProjectXObject(json, out data)
                && data.TryGetValue(key, out raw)
                && TryConvertToLong(raw, out value);
        }

        private bool TryGetJsonBool(string json, string key, out bool value)
        {
            value = false;
            object raw;
            Dictionary<string, object> data;
            return TryDeserializeProjectXObject(json, out data)
                && data.TryGetValue(key, out raw)
                && TryConvertToBool(raw, out value);
        }

        private bool TryDeserializeProjectXObject(string json, out Dictionary<string, object> data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                return data != null;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<ProjectXAccountInfo> ExtractProjectXAccounts(string json)
        {
            foreach (Dictionary<string, object> item in ExtractProjectXCollection(json, "accounts"))
            {
                object rawId;
                int id;
                if (!item.TryGetValue("id", out rawId) || !TryConvertToInt(rawId, out id) || id <= 0)
                    continue;

                object rawName;
                object rawCanTrade;
                object rawVisible;
                bool canTrade;
                bool visible;
                item.TryGetValue("name", out rawName);
                item.TryGetValue("canTrade", out rawCanTrade);
                item.TryGetValue("isVisible", out rawVisible);
                TryConvertToBool(rawCanTrade, out canTrade);
                TryConvertToBool(rawVisible, out visible);

                yield return new ProjectXAccountInfo
                {
                    Id = id,
                    Name = rawName != null ? rawName.ToString() : string.Empty,
                    CanTrade = canTrade,
                    IsVisible = visible
                };
            }
        }

        private IEnumerable<ProjectXContractInfo> ExtractProjectXContracts(string json)
        {
            foreach (Dictionary<string, object> item in ExtractProjectXCollection(json, "contracts"))
            {
                object rawId;
                if (!item.TryGetValue("id", out rawId) || rawId == null)
                    continue;
                object rawName;
                object rawSymbol;
                object rawActive;
                bool active;
                item.TryGetValue("name", out rawName);
                item.TryGetValue("symbolId", out rawSymbol);
                item.TryGetValue("activeContract", out rawActive);
                TryConvertToBool(rawActive, out active);

                yield return new ProjectXContractInfo
                {
                    Id = rawId.ToString(),
                    Name = rawName != null ? rawName.ToString() : string.Empty,
                    SymbolId = rawSymbol != null ? rawSymbol.ToString() : string.Empty,
                    ActiveContract = active
                };
            }
        }

        private IEnumerable<Dictionary<string, object>> ExtractProjectXCollection(string json, string key)
        {
            Dictionary<string, object> data;
            if (!TryDeserializeProjectXObject(json, out data))
                yield break;
            object raw;
            if (!data.TryGetValue(key, out raw) || raw == null)
                yield break;
            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;
            foreach (object item in items)
            {
                var dictionary = item as Dictionary<string, object>;
                if (dictionary != null)
                    yield return dictionary;
            }
        }

        private bool TryGetProjectXOrderInt(Dictionary<string, object> order, string key, out int value)
        {
            value = 0;
            object raw;
            return order != null && order.TryGetValue(key, out raw) && TryConvertToInt(raw, out value);
        }

        private bool TryGetProjectXOrderLong(Dictionary<string, object> order, string key, out long value)
        {
            value = 0;
            object raw;
            return order != null && order.TryGetValue(key, out raw) && TryConvertToLong(raw, out value);
        }

        private bool TryGetProjectXOrderDouble(Dictionary<string, object> order, string key, out double value)
        {
            value = 0.0;
            object raw;
            return order != null && order.TryGetValue(key, out raw) && TryConvertToDouble(raw, out value);
        }

        private bool TryConvertToInt(object raw, out int value)
        {
            value = 0;
            if (raw == null)
                return false;
            if (raw is int) { value = (int)raw; return true; }
            if (raw is long && (long)raw >= int.MinValue && (long)raw <= int.MaxValue)
            { value = (int)(long)raw; return true; }
            if (raw is decimal) { value = (int)(decimal)raw; return true; }
            if (raw is double) { value = (int)(double)raw; return true; }
            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToLong(object raw, out long value)
        {
            value = 0;
            if (raw == null)
                return false;
            if (raw is int) { value = (int)raw; return true; }
            if (raw is long) { value = (long)raw; return true; }
            if (raw is decimal) { value = (long)(decimal)raw; return true; }
            if (raw is double) { value = (long)(double)raw; return true; }
            return long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToDouble(object raw, out double value)
        {
            value = 0.0;
            if (raw == null)
                return false;
            if (raw is double) { value = (double)raw; return true; }
            if (raw is decimal) { value = (double)(decimal)raw; return true; }
            if (raw is int) { value = (int)raw; return true; }
            if (raw is long) { value = (long)raw; return true; }
            return double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private bool TryConvertToBool(object raw, out bool value)
        {
            value = false;
            if (raw == null)
                return false;
            if (raw is bool) { value = (bool)raw; return true; }
            return bool.TryParse(raw.ToString(), out value);
        }


        // ---------------------------------------------------------------- properties

        // Hidden: which SELL2525 cut is installed. XmlIgnore so a saved template never overrides the installed version.
        [NinjaScriptProperty]
        [XmlIgnore]
        [Browsable(false)]
        [Display(Name = "Version", Description = "Which SELL2525 cut is installed. Never changes behavior.", GroupName = "0. Version", Order = 0)]
        public Sell2525Version Version { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "30m bullish bar filter", Description = "ON: skips the day when the 09:00 to 09:30 New York 30-minute bar is bullish (it closes above where it opened), because the short loses far more often after a rising pre-open. A bar that closes exactly where it opened is traded. OFF: trades every day. Decided from data up to the moment the entry order is sent (09:29:59.755 with the default delay offset). Back-test (142 NQ days): Net $26,710 with maxIDD $2,670 without it vs Net $19,501 with maxIDD $1,424 with it.", GroupName = "1. Filters", Order = 1)]
        public bool Use30mFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Quote-activity filter", Description = "ON: skips the day when the number of bid/ask updates NinjaTrader receives in the last second before the entry order is sent is above the busiest 20% seen in the research (more than 1,039). Very busy quote activity at the open means a fast, jumpy open where the entry fill is less predictable. DEFAULT OFF: the research at the real send time found it does not improve results (Net/DD 12.5 with it vs 14.1 without), and the 1,039 limit comes from recorded replay data and has not been checked against the live feed. The daily counts are always logged so it can be checked later.", GroupName = "1. Filters", Order = 2)]
        public bool UseQuoteFilter { get; set; }

        [NinjaScriptProperty]
        [Range(1, 9)]
        [Display(Name = "Contract quantity", Description = "Contracts per trade. 1 is the researched size. NQ allows 1 to 2, MNQ 1 to 9. The Topstep (ProjectX) mirror always trades 1 contract, so an instance with a ProjectX login must stay at 1. Only NQ and MNQ are accepted.", GroupName = "2. Trade", Order = 1)]
        public int ContractQty { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1500)]
        [Display(Name = "Delay offset (ms)", Description = "How many milliseconds BEFORE the target fill time each order is sent, to cover the time the order takes to reach the exchange. The entry is aimed to fill at 09:30:00.035 and the exit 25.75 seconds later; with 280 both orders are sent 280 ms earlier (09:29:59.755 and 09:30:25.505). Raise it if the logged fill times (SELL2525-trades.csv, fill_broker_minus_target_ms) are consistently late, lower it if they are early. Moves the entry and the exit together.", GroupName = "2. Trade", Order = 2)]
        public int DelayOffsetMs { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max account balance ($)", Description = "0 disables. When the account's net liquidation, including unrealized P&L, reaches this value, an unfilled entry is canceled, an open trade is CLOSED at market and no new entries are placed (use it on eval accounts to stop at the profit target). It holds until the strategy is restarted. It reads the NinjaTrader account this strategy runs on (Sim101 on the Topstep mirror instance, NOT the Topstep account itself), and other strategies or manual positions on that account count toward it.", GroupName = "2. Trade", Order = 3)]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0, 1000)]
        [Display(Name = "Entry target after 09:30:00.000 (ms)", Description = "Target fill time of the entry after the open. Researched value 35.", GroupName = "3. Hidden", Order = 1)]
        public int EntryOffsetMs { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1000, 120000)]
        [Display(Name = "Hold (ms)", Description = "Time from the entry target to the exit target. Researched value 25,750.", GroupName = "3. Hidden", Order = 2)]
        public int HoldMs { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "Stop (points)", Description = "Stop loss from the actual fill. Researched value 75.", GroupName = "3. Hidden", Order = 3)]
        public int StopPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(1, 200)]
        [Display(Name = "Take profit (points)", Description = "Take profit from the actual fill. Researched value 58.", GroupName = "3. Hidden", Order = 4)]
        public int TpPoints { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Range(0.0, 100000.0)]
        [Display(Name = "Quote cutoff", Description = "Quote updates in the last second before the send time above which the Quote-activity filter skips the day. Replay-calibrated 1,038.8.", GroupName = "3. Hidden", Order = 5)]
        public double QuoteCutoff { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Base URL", GroupName = "4. Topstep (ProjectX)", Order = 3)]
        public string ProjectXApiBaseUrl { get; set; }

        [Browsable(false)]
        public bool ProjectXTradeAllAccounts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Username", Description = "ProjectX login username for direct routing.", GroupName = "4. Topstep (ProjectX)", Order = 5)]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "API", Description = "ProjectX API key used with the username.", GroupName = "4. Topstep (ProjectX)", Order = 6)]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Accounts", Description = "Comma-separated ProjectX account IDs or exact account names.", GroupName = "4. Topstep (ProjectX)", Order = 7)]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Contract ID", Description = "Hidden optional contract override for support/debug use.", GroupName = "4. Topstep (ProjectX)", Order = 9)]
        public string ProjectXContractId { get; set; }
    }
}
