// ============================================================================
//  Lepp Open Version 1  –  NinjaTrader 8
//  Version : 1.001
//  Timeframe: 30-Second bars  |  Asset: NQ Futures
//
//  SETUP INSTRUCTIONS
//  ─────────────────────────────────────────────────────────────────────────
//  1. Copy this file to:
//       [NT8 Documents]\NinjaTrader 8\bin\Custom\Strategies\
//  2. In NinjaTrader: New → NinjaScript Editor → Compile (F5)
//  3. Add strategy to a 30-second NQ chart using the RTH session template
//     (09:30–16:15 ET).  Using RTH data is strongly recommended so that
//     IsFirstBarOfSession aligns with 09:30.
//  4. Configure parameters in the strategy dialog.
//
//  KEY DESIGN NOTES
//  ─────────────────────────────────────────────────────────────────────────
//  • OR bar  : The single 30-second bar whose OPEN is 09:30:00 ET.
//              NinjaTrader 8 stamps time-based bars with their CLOSE time,
//              so the OR bar has Time[0] == 09:30:30.
//  • Drawing : All session lines (OR / Target / Trigger / SL) are drawn as
//              bounded Draw.Line objects from 09:30 to SessionEnd, so they
//              do NOT bleed across sessions.
//  • OCO     : SetStopLoss / SetProfitTarget are applied inside OnBarUpdate
//              (never OnExecutionUpdate) via a pending-flag pattern, which is
//              the reliable NT8 approach.
//  • Re-entry: After a stop-out the relevant side re-arms automatically,
//              subject to MaxTradesPerDay and NoNewTradesAfter.
// ============================================================================

#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// ── Enums must live in the same namespace but outside the class ─────────────
namespace NinjaTrader.NinjaScript.Strategies
{
    public enum NQOREntryMethod
    {
        [Description("i)  Market order on trigger")]   MarketOnTrigger  = 0,
        [Description("ii) Retracement limit order")]   RetracementLimit = 1,
        [Description("iii) FVG limit order")]          FVGLimit         = 2
    }

    public enum NQORFVGEntry
    {
        [Description("Top of FVG (first touch)")]  Top    = 0,
        [Description("Midpoint of FVG")]           Mid    = 1,
        [Description("Bottom of FVG (deepest)")]   Bottom = 2
    }

    public enum NQORStopMethod
    {
        [Description("i)   Fixed points")]     FixedPoints  = 0,
        [Description("ii)  % of range")]       PercentRange = 1,
        [Description("iii) Last swing point")] SwingPoint   = 2,
        [Description("iv)  FVG candle")]       FVGCandle    = 3
    }

    public enum NQORBEMethod
    {
        [Description("Fixed points from entry")] FixedPoints  = 0,
        [Description("% of OR range")]           PercentOR    = 1
    }

    // =========================================================================
    public class LeppOpen : Strategy
    {
        public LeppOpen()
        {
            VendorLicense(1252);
        }

        // ── Opening Range ─────────────────────────────────────────────────────
        private double   _orHigh, _orLow, _orRange;
        private bool     _orSet;
        private DateTime _orDate;

        // ── Calculated levels ─────────────────────────────────────────────────
        private double _longTarget,  _shortTarget;
        private double _longTrigger, _shortTrigger;

        // ── Long side state machine ───────────────────────────────────────────
        private bool  _longBreakout;
        private bool  _longTriggerHit;
        private bool  _longEntryArmed;
        private bool  _longInTrade;
        private bool  _longFVGPending;      // waiting for an FVG to form
        private Order _longLimitOrder;

        // ── Short side state machine ──────────────────────────────────────────
        private bool  _shortBreakout;
        private bool  _shortTriggerHit;
        private bool  _shortEntryArmed;
        private bool  _shortInTrade;
        private bool  _shortFVGPending;
        private Order _shortLimitOrder;

        // ── FVG storage ───────────────────────────────────────────────────────
        private bool   _longFVGFound,  _shortFVGFound;
        private double _longFVGBot,    _longFVGTop,    _longFVGMid,    _longFVGCandleLow;
        private double _shortFVGBot,   _shortFVGTop,   _shortFVGMid,   _shortFVGCandleHigh;

        // ── Pending OCO bracket (applied in OnBarUpdate, never OnExecution) ───
        private bool   _applyLongOCO,  _applyShortOCO;
        private double _pendingLongSL,  _pendingShortSL;
        private double _pendingLongTP,  _pendingShortTP;

        // ── Re-arm flags (set by OnPositionUpdate, consumed in OnBarUpdate) ───
        private bool _longRearmNeeded, _shortRearmNeeded;

        // ── Session bookkeeping ───────────────────────────────────────────────
        private int      _tradesToday;
        private bool     _forceFlatDone;
        private DateTime _lastResetDate = DateTime.MinValue;
        private int      _drawSeq;          // unique suffix for draw objects

        // ── Win / Loss counters ───────────────────────────────────────────────
        // _lastWinDirection: 0=no win yet this session, 1=last win was Long, 2=last win was Short
        private int  _profitableTradesToday;
        private int  _losingTradesToday;
        private int  _lastWinDirection;

        // ── Break-even tracking ───────────────────────────────────────────────
        // Entry prices stored on fill; BE flag set once SL is moved
        private double _longEntryPrice,  _shortEntryPrice;
        private bool   _longBEMoved,     _shortBEMoved;
        private double _longBEPrice,     _shortBEPrice;   // computed BE stop level
        // Pending BE SL update (applied in OnBarUpdate like initial OCO)
        private bool   _applyLongBE,     _applyShortBE;
        private double _pendingLongBESL, _pendingShortBESL;

        // ── Swing indicator reference ─────────────────────────────────────────
        private NinjaTrader.NinjaScript.Indicators.Swing _swingInd;

        // =====================================================================
        //  STATE CHANGE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "LeppOpen";
                Description = "LeppOpen– NQ 30-second OR strategy – configurable entry/exit methods.";
                Calculate   = Calculate.OnBarClose;

                // Allow enough entries for re-entry; MaxTradesPerDay is the real gate
                EntriesPerDirection = 10;
                EntryHandling       = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;

                // ── Opening Range ──────────────────────────────────────────
                ORMultiplier  = 5.01;
                TriggerOffset = 16.0;
                MinORSize     = 7.0;
                MaxORSize     = 150.0;

                // ── Trade Management ───────────────────────────────────────
                Contracts           = 5;
                MaxTradesPerDay     = 3;
                MaxProfitableTrades = 2;
                MaxLosingTrades     = 2;
                AlternatingEnabled  = false;

                // ── Entry ──────────────────────────────────────────────────
                EntryMethod = NQOREntryMethod.RetracementLimit;
                RetracePct  = 70.18;
                MinFVGSize  = 21.0;
                FVGEntry    = NQORFVGEntry.Bottom;

                // ── Stop Loss ──────────────────────────────────────────────
                StopMethod    = NQORStopMethod.PercentRange;
                FixedSLPts    = 44.0;
                SLRangePct    = 84.77;
                SwingStrength = 5;

                // ── Break-Even ─────────────────────────────────────────────
                BEEnabled      = true;
                BEMethod       = NQORBEMethod.FixedPoints;
                BETriggerPts   = 25.0;
                BETriggerPct   = 21.0;
                BEOffsetPts    = 1.5;

                // ── Time Filters ───────────────────────────────────────────
                NoNewTradesHour = 14;
                NoNewTradesMin  = 21;
                ForceFlatHour   = 15;
                ForceFlatMin    = 48;
                SessionEndHour  = 17;
                SessionEndMin   = 0;
            }
            else if (State == State.DataLoaded)
            {
                _swingInd = Swing(SwingStrength);
            }
        }

        // =====================================================================
        //  MAIN BAR UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (CurrentBar < 3)      return;

            // ── A. Daily reset ────────────────────────────────────────────────
            if (Time[0].Date != _lastResetDate)
            {
                _lastResetDate = Time[0].Date;
                ResetSession();
            }

            // ── B. Apply pending OCO brackets (MUST be in OnBarUpdate) ────────
            //        These are queued from OnExecutionUpdate and processed here.
            if (_applyLongOCO)
            {
                SetStopLoss("LongEntry",  CalculationMode.Price, _pendingLongSL,  false);
                SetProfitTarget("LongEntry",  CalculationMode.Price, _pendingLongTP);
                _applyLongOCO = false;
            }
            if (_applyShortOCO)
            {
                SetStopLoss("ShortEntry", CalculationMode.Price, _pendingShortSL, false);
                SetProfitTarget("ShortEntry", CalculationMode.Price, _pendingShortTP);
                _applyShortOCO = false;
            }

            // ── B2. Apply pending Break-Even SL updates ───────────────────────
            if (_applyLongBE)
            {
                SetStopLoss("LongEntry", CalculationMode.Price, _pendingLongBESL, false);
                _applyLongBE = false;
                Print(Time[0] + " | ✦ Long SL moved to BE @ " + _pendingLongBESL);
            }
            if (_applyShortBE)
            {
                SetStopLoss("ShortEntry", CalculationMode.Price, _pendingShortBESL, false);
                _applyShortBE = false;
                Print(Time[0] + " | ✦ Short SL moved to BE @ " + _pendingShortBESL);
            }

            // ── C. Force flatten at configured time ───────────────────────────
            var tod      = Time[0].TimeOfDay;
            var flatTime = new TimeSpan(ForceFlatHour, ForceFlatMin, 0);
            if (tod >= flatTime && !_forceFlatDone)
            {
                FlattenEverything();
                _forceFlatDone = true;
                return;
            }
            if (_forceFlatDone) return;

            // ── D. Capture OR candle ──────────────────────────────────────────
            //        NT8 stamps 30s bars with their CLOSE time.
            //        The bar that OPENS at 09:30:00 CLOSES at 09:30:30.
            if (!_orSet)
            {
                if (tod == new TimeSpan(9, 30, 30))
                {
                    double range = High[0] - Low[0];

                    // OR size filter
                    if (range < MinORSize || range > MaxORSize)
                    {
                        Print(Time[0] + " | OR size " + range + " pts outside ["
                              + MinORSize + "–" + MaxORSize + "]. Session skipped.");
                        _forceFlatDone = true;   // treat as no-trade session
                        return;
                    }

                    _orHigh  = High[0];
                    _orLow   = Low[0];
                    _orRange = range;
                    _orSet   = true;
                    _orDate  = Time[0].Date;

                    _longTarget   = _orHigh + ORMultiplier * _orRange;
                    _shortTarget  = _orLow  - ORMultiplier * _orRange;
                    _longTrigger  = _longTarget  - TriggerOffset;
                    _shortTrigger = _shortTarget + TriggerOffset;

                    DrawSessionLines();

                    Print(Time[0] + " | ═══ OR captured ═══"
                          + "  H=" + _orHigh + "  L=" + _orLow + "  Range=" + _orRange
                          + " | LongTarget="  + _longTarget  + "  LongTrigger="  + _longTrigger
                          + " | ShortTarget=" + _shortTarget + "  ShortTrigger=" + _shortTrigger);
                }
                return;   // Nothing more until OR is confirmed
            }

            // ── E. Trading permission gate ────────────────────────────────────
            var noNewTime = new TimeSpan(NoNewTradesHour, NoNewTradesMin, 0);
            bool canTrade = tod < noNewTime
                         && _tradesToday         < MaxTradesPerDay
                         && _profitableTradesToday < MaxProfitableTrades
                         && _losingTradesToday     < MaxLosingTrades;

            // ── F. OR breakout detection ──────────────────────────────────────
            if (!_longBreakout  && High[0] > _orHigh) _longBreakout  = true;
            if (!_shortBreakout && Low[0]  < _orLow)  _shortBreakout = true;

            // ── G. FVG scan ───────────────────────────────────────────────────
            //   Runs while:  breakout has occurred
            //   AND:         trigger NOT yet hit  (collecting "last FVG before trigger")
            //              OR pending arm (collecting new FVG after stop-out / re-arm)
            //   Checks the standard 3-candle FVG pattern evaluated on bar close:
            //     Bullish FVG:  Low[0]  > High[2]  →  gap = [High[2] , Low[0] ]
            //     Bearish FVG:  High[0] < Low[2]   →  gap = [High[0] , Low[2] ]
            if (EntryMethod == NQOREntryMethod.FVGLimit)
            {
                // ── Long (bullish) FVG ──
                bool longFVGScanActive = _longBreakout && !_longInTrade
                                      && (!_longTriggerHit || _longFVGPending);
                if (longFVGScanActive)
                {
                    double gap = Low[0] - High[2];
                    if (gap >= MinFVGSize)
                    {
                        _longFVGBot       = High[2];
                        _longFVGTop       = Low[0];
                        _longFVGMid       = (_longFVGBot + _longFVGTop) * 0.5;
                        _longFVGCandleLow = Low[1];   // middle candle low → FVG SL anchor
                        _longFVGFound     = true;
                    }
                }

                // ── Short (bearish) FVG ──
                bool shortFVGScanActive = _shortBreakout && !_shortInTrade
                                       && (!_shortTriggerHit || _shortFVGPending);
                if (shortFVGScanActive)
                {
                    double gap = Low[2] - High[0];
                    if (gap >= MinFVGSize)
                    {
                        _shortFVGTop        = Low[2];
                        _shortFVGBot        = High[0];
                        _shortFVGMid        = (_shortFVGBot + _shortFVGTop) * 0.5;
                        _shortFVGCandleHigh = High[1];  // middle candle high → FVG SL anchor
                        _shortFVGFound      = true;
                    }
                }
            }

            // ── G2. Break-Even monitoring ─────────────────────────────────────
            //        Check each bar while in trade; move SL to BE once price
            //        has moved the configured trigger distance from entry.
            if (BEEnabled)
            {
                // Long BE
                if (_longInTrade && !_longBEMoved && _longEntryPrice > 0)
                {
                    double beTriggerDist = (BEMethod == NQORBEMethod.FixedPoints)
                                        ? BETriggerPts
                                        : _orRange * (BETriggerPct / 100.0);
                    if (High[0] >= _longEntryPrice + beTriggerDist)
                    {
                        _longBEMoved        = true;
                        _pendingLongBESL    = _longEntryPrice + BEOffsetPts;
                        _applyLongBE        = true;
                        _longBEPrice        = _pendingLongBESL;

                        // Redraw SL line at new BE level
                        DateTime beT1 = Time[0];
                        DateTime beT2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
                        Draw.Line(this, "LongBE_" + (++_drawSeq), false,
                                  beT1, _longBEPrice, beT2, _longBEPrice,
                                  Brushes.Lime, DashStyleHelper.Dash, 2);
                    }
                }

                // Short BE
                if (_shortInTrade && !_shortBEMoved && _shortEntryPrice > 0)
                {
                    double beTriggerDist = (BEMethod == NQORBEMethod.FixedPoints)
                                        ? BETriggerPts
                                        : _orRange * (BETriggerPct / 100.0);
                    if (Low[0] <= _shortEntryPrice - beTriggerDist)
                    {
                        _shortBEMoved       = true;
                        _pendingShortBESL   = _shortEntryPrice - BEOffsetPts;
                        _applyShortBE       = true;
                        _shortBEPrice       = _pendingShortBESL;

                        DateTime beT1 = Time[0];
                        DateTime beT2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
                        Draw.Line(this, "ShortBE_" + (++_drawSeq), false,
                                  beT1, _shortBEPrice, beT2, _shortBEPrice,
                                  Brushes.Lime, DashStyleHelper.Dash, 2);
                    }
                }
            }

            // ── H. Trigger line crossed (first time) ──────────────────────────
            if (_longBreakout && !_longTriggerHit && High[0] >= _longTrigger)
            {
                _longTriggerHit = true;
                Print(Time[0] + " | Long trigger crossed @ " + _longTrigger);
                if (canTrade && !_longInTrade && !_shortInTrade)
                    TryArmLong();
            }

            if (_shortBreakout && !_shortTriggerHit && Low[0] <= _shortTrigger)
            {
                _shortTriggerHit = true;
                Print(Time[0] + " | Short trigger crossed @ " + _shortTrigger);
                if (canTrade && !_shortInTrade && !_longInTrade)
                    TryArmShort();
            }

            // ── I. FVG pending arm (polls each bar after trigger until FVG found)
            if (_longFVGPending && !_longEntryArmed && !_longInTrade && canTrade && !_shortInTrade)
            {
                if (_longFVGFound)
                {
                    _longFVGPending = false;
                    PlaceLongFVGOrder();
                }
            }
            if (_shortFVGPending && !_shortEntryArmed && !_shortInTrade && canTrade && !_longInTrade)
            {
                if (_shortFVGFound)
                {
                    _shortFVGPending = false;
                    PlaceShortFVGOrder();
                }
            }

            // ── J. Re-arm after stop-out (flag set by OnPositionUpdate) ──────
            if (_longRearmNeeded && canTrade && !_longInTrade && !_longEntryArmed && !_shortInTrade)
            {
                _longRearmNeeded = false;
                TryArmLong();
            }
            if (_shortRearmNeeded && canTrade && !_shortInTrade && !_shortEntryArmed && !_longInTrade)
            {
                _shortRearmNeeded = false;
                TryArmShort();
            }

            // ── K. Cancel limit orders if price already reached target ────────
            if (_longEntryArmed && _longLimitOrder != null && High[0] >= _longTarget)
            {
                TryCancelOrder(_longLimitOrder);
                _longEntryArmed = false;
                _longFVGPending = false;
                _longTriggerHit = false;   // reset so no further re-arms this session
                Print(Time[0] + " | Long limit cancelled – target already reached");
            }
            if (_shortEntryArmed && _shortLimitOrder != null && Low[0] <= _shortTarget)
            {
                TryCancelOrder(_shortLimitOrder);
                _shortEntryArmed = false;
                _shortFVGPending = false;
                _shortTriggerHit = false;
                Print(Time[0] + " | Short limit cancelled – target already reached");
            }
        }

        // =====================================================================
        //  ARMING HELPERS
        // =====================================================================

        /// <summary>Arms the long entry according to the selected entry method.</summary>
        private void TryArmLong()
        {
            if (_longEntryArmed)               return;
            if (_tradesToday >= MaxTradesPerDay) return;
            if (_profitableTradesToday >= MaxProfitableTrades) return;
            if (_losingTradesToday     >= MaxLosingTrades)     return;

            // Alternating: block long if last WIN was also long
            if (AlternatingEnabled && _lastWinDirection == 1)
            {
                Print(Time[0] + " | Long blocked – alternating rule (last win was Long)");
                return;
            }

            switch (EntryMethod)
            {
                // ── i) Market order ──────────────────────────────────────────
                case NQOREntryMethod.MarketOnTrigger:
                    _pendingLongSL = CalcLongSL(Close[0]);
                    _pendingLongTP = _longTarget;
                    _applyLongOCO  = true;
                    EnterLong(Contracts, "LongEntry");
                    _longEntryArmed = true;
                    Print(Time[0] + " | Long MARKET order submitted");
                    break;

                // ── ii) Retracement limit ─────────────────────────────────────
                case NQOREntryMethod.RetracementLimit:
                {
                    // Entry = Target - Retracement% × (Target - OR High)
                    double retDist  = (_longTarget - _orHigh) * (RetracePct / 100.0);
                    double limitPx  = _longTarget - retDist;
                    _longLimitOrder = EnterLongLimit(0, true, Contracts, limitPx, "LongEntry");
                    _longEntryArmed = true;
                    Print(Time[0] + " | Long RETRACEMENT limit @ " + limitPx
                          + "  (" + RetracePct + "% of " + (_longTarget - _orHigh) + " pts)");
                    break;
                }

                // ── iii) FVG limit ────────────────────────────────────────────
                case NQOREntryMethod.FVGLimit:
                    if (_longFVGFound)
                        PlaceLongFVGOrder();
                    else
                    {
                        _longFVGPending = true;
                        Print(Time[0] + " | Long FVG armed – scanning for FVG...");
                    }
                    break;
            }
        }

        /// <summary>Arms the short entry according to the selected entry method.</summary>
        private void TryArmShort()
        {
            if (_shortEntryArmed)              return;
            if (_tradesToday >= MaxTradesPerDay) return;
            if (_profitableTradesToday >= MaxProfitableTrades) return;
            if (_losingTradesToday     >= MaxLosingTrades)     return;

            // Alternating: block short if last WIN was also short
            if (AlternatingEnabled && _lastWinDirection == 2)
            {
                Print(Time[0] + " | Short blocked – alternating rule (last win was Short)");
                return;
            }

            switch (EntryMethod)
            {
                // ── i) Market order ──────────────────────────────────────────
                case NQOREntryMethod.MarketOnTrigger:
                    _pendingShortSL = CalcShortSL(Close[0]);
                    _pendingShortTP = _shortTarget;
                    _applyShortOCO  = true;
                    EnterShort(Contracts, "ShortEntry");
                    _shortEntryArmed = true;
                    Print(Time[0] + " | Short MARKET order submitted");
                    break;

                // ── ii) Retracement limit ─────────────────────────────────────
                case NQOREntryMethod.RetracementLimit:
                {
                    // Entry = Target + Retracement% × (OR Low - Target)
                    double retDist   = (_orLow - _shortTarget) * (RetracePct / 100.0);
                    double limitPx   = _shortTarget + retDist;
                    _shortLimitOrder = EnterShortLimit(0, true, Contracts, limitPx, "ShortEntry");
                    _shortEntryArmed = true;
                    Print(Time[0] + " | Short RETRACEMENT limit @ " + limitPx
                          + "  (" + RetracePct + "% of " + (_orLow - _shortTarget) + " pts)");
                    break;
                }

                // ── iii) FVG limit ────────────────────────────────────────────
                case NQOREntryMethod.FVGLimit:
                    if (_shortFVGFound)
                        PlaceShortFVGOrder();
                    else
                    {
                        _shortFVGPending = true;
                        Print(Time[0] + " | Short FVG armed – scanning for FVG...");
                    }
                    break;
            }
        }

        private void PlaceLongFVGOrder()
        {
            double px       = FVGEntryPrice(_longFVGBot, _longFVGTop, _longFVGMid, true);
            _longLimitOrder = EnterLongLimit(0, true, Contracts, px, "LongEntry");
            _longEntryArmed = true;
            Print(Time[0] + " | Long FVG LIMIT @ " + px
                  + "  (gap: " + _longFVGBot + "–" + _longFVGTop + ")");
        }

        private void PlaceShortFVGOrder()
        {
            double px        = FVGEntryPrice(_shortFVGBot, _shortFVGTop, _shortFVGMid, false);
            _shortLimitOrder = EnterShortLimit(0, true, Contracts, px, "ShortEntry");
            _shortEntryArmed = true;
            Print(Time[0] + " | Short FVG LIMIT @ " + px
                  + "  (gap: " + _shortFVGBot + "–" + _shortFVGTop + ")");
        }

        /// <summary>
        /// Returns the limit price to use within an FVG based on user preference.
        ///   Long  – Top = first touch (most likely fill), Bottom = deepest retracement
        ///   Short – Bottom = first touch, Top = deepest retracement
        /// </summary>
        private double FVGEntryPrice(double bot, double top, double mid, bool isLong)
        {
            switch (FVGEntry)
            {
                case NQORFVGEntry.Top:    return isLong ? top : bot;
                case NQORFVGEntry.Bottom: return isLong ? bot : top;
                default:                 return mid;
            }
        }

        // =====================================================================
        //  ORDER & EXECUTION CALLBACKS
        // =====================================================================

        /// <summary>Keeps order references fresh for cancellation checks.</summary>
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int qty, int filled, double avgFill, OrderState state,
            DateTime time, ErrorCode err, string comment)
        {
            if (order.Name == "LongEntry")  _longLimitOrder  = order;
            if (order.Name == "ShortEntry") _shortLimitOrder = order;
        }

        /// <summary>
        /// Fires when any order is (partially) filled.
        /// Only processes FULL fills of our named entry orders.
        /// Queues the OCO bracket for application in the next OnBarUpdate cycle.
        /// </summary>
        protected override void OnExecutionUpdate(Execution exec, string execId, double price,
            int qty, MarketPosition mp, string orderId, DateTime time)
        {
            if (exec.Order.OrderState != OrderState.Filled) return;

            // ── Long entry filled ─────────────────────────────────────────────
            if (exec.Order.Name == "LongEntry")
            {
                _longInTrade     = true;
                _longEntryArmed  = false;
                _longLimitOrder  = null;
                _longEntryPrice  = price;
                _longBEMoved     = false;
                _tradesToday++;

                double sl = CalcLongSL(price);
                _pendingLongSL = sl;
                _pendingLongTP = _longTarget;
                _applyLongOCO  = true;

                // Draw red dashed SL line – bounded to this session only
                {
                    DateTime slT1 = time;
                    DateTime slT2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
                    Draw.Line(this, "LongSL_" + (++_drawSeq), false,
                              slT1, sl, slT2, sl, Brushes.Red, DashStyleHelper.Dash, 2);
                }

                Print(time + " | ► LONG FILLED @ " + price
                      + "  SL=" + sl + "  TP=" + _longTarget
                      + "  [Trade " + _tradesToday + "/" + MaxTradesPerDay + "]");
            }

            // ── Short entry filled ────────────────────────────────────────────
            else if (exec.Order.Name == "ShortEntry")
            {
                _shortInTrade    = true;
                _shortEntryArmed = false;
                _shortLimitOrder = null;
                _shortEntryPrice = price;
                _shortBEMoved    = false;
                _tradesToday++;

                double sl = CalcShortSL(price);
                _pendingShortSL = sl;
                _pendingShortTP = _shortTarget;
                _applyShortOCO  = true;

                // Draw red dashed SL line – bounded to this session only
                {
                    DateTime slT1 = time;
                    DateTime slT2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
                    Draw.Line(this, "ShortSL_" + (++_drawSeq), false,
                              slT1, sl, slT2, sl, Brushes.Red, DashStyleHelper.Dash, 2);
                }

                Print(time + " | ► SHORT FILLED @ " + price
                      + "  SL=" + sl + "  TP=" + _shortTarget
                      + "  [Trade " + _tradesToday + "/" + MaxTradesPerDay + "]");
            }
        }

        /// <summary>
        /// Fires when position size changes.
        /// Classifies the closed trade as Win / Loss / Scratch, updates all
        /// session counters and sets alternating-direction state.
        /// </summary>
        protected override void OnPositionUpdate(Position pos, double avgPx,
            int qty, MarketPosition mp)
        {
            if (mp != MarketPosition.Flat) return;

            // ── Long closed ───────────────────────────────────────────────────
            if (_longInTrade)
            {
                _longInTrade  = false;
                _longFVGFound = false;

                // Classify outcome using the last closed trade's P&L
                // SystemPerformance.AllTrades is updated before OnPositionUpdate fires.
                double pnl = 0;
                if (SystemPerformance.AllTrades.Count > 0)
                    pnl = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1].ProfitCurrency;

                bool isScratch = _longBEMoved
                              && Math.Abs(pnl) < TickSize * Instrument.MasterInstrument.PointValue * Contracts * 2;

                if (isScratch)
                {
                    Print(Time[0] + " | Long closed – SCRATCH (BE stop)");
                    // Scratch does not count toward win or loss tally
                }
                else if (pnl > 0)
                {
                    _profitableTradesToday++;
                    _lastWinDirection = 1;   // 1 = last win was Long
                    Print(Time[0] + " | Long closed – WIN  (#" + _profitableTradesToday + " profitable today)");
                }
                else
                {
                    _losingTradesToday++;
                    Print(Time[0] + " | Long closed – LOSS (#" + _losingTradesToday + " losses today)");
                    // After a loss, same direction is allowed → do NOT update _lastWinDirection
                }

                // Reset BE state for this side
                _longBEMoved    = false;
                _longEntryPrice = 0;

                // Re-arm only if session limits not exhausted
                bool canRearm = _longTriggerHit
                             && !_forceFlatDone
                             && _tradesToday         < MaxTradesPerDay
                             && _profitableTradesToday < MaxProfitableTrades
                             && _losingTradesToday     < MaxLosingTrades
                             && !(AlternatingEnabled && _lastWinDirection == 1);

                _longRearmNeeded = canRearm;
                Print(Time[0] + " | Long re-arm=" + _longRearmNeeded);
            }

            // ── Short closed ──────────────────────────────────────────────────
            if (_shortInTrade)
            {
                _shortInTrade  = false;
                _shortFVGFound = false;

                double pnl = 0;
                if (SystemPerformance.AllTrades.Count > 0)
                    pnl = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1].ProfitCurrency;

                bool isScratch = _shortBEMoved
                              && Math.Abs(pnl) < TickSize * Instrument.MasterInstrument.PointValue * Contracts * 2;

                if (isScratch)
                {
                    Print(Time[0] + " | Short closed – SCRATCH (BE stop)");
                }
                else if (pnl > 0)
                {
                    _profitableTradesToday++;
                    _lastWinDirection = 2;   // 2 = last win was Short
                    Print(Time[0] + " | Short closed – WIN  (#" + _profitableTradesToday + " profitable today)");
                }
                else
                {
                    _losingTradesToday++;
                    Print(Time[0] + " | Short closed – LOSS (#" + _losingTradesToday + " losses today)");
                }

                _shortBEMoved    = false;
                _shortEntryPrice = 0;

                bool canRearm = _shortTriggerHit
                             && !_forceFlatDone
                             && _tradesToday          < MaxTradesPerDay
                             && _profitableTradesToday < MaxProfitableTrades
                             && _losingTradesToday      < MaxLosingTrades
                             && !(AlternatingEnabled && _lastWinDirection == 2);

                _shortRearmNeeded = canRearm;
                Print(Time[0] + " | Short re-arm=" + _shortRearmNeeded);
            }
        }

        // =====================================================================
        //  STOP LOSS CALCULATORS
        // =====================================================================

        private double CalcLongSL(double fillPrice)
        {
            switch (StopMethod)
            {
                // i)   Fixed points below entry
                case NQORStopMethod.FixedPoints:
                    return fillPrice - FixedSLPts;

                // ii)  % of (Target – OR High) below entry
                case NQORStopMethod.PercentRange:
                    return fillPrice - (_longTarget - _orHigh) * (SLRangePct / 100.0);

                // iii) One tick below the most recent swing low
                case NQORStopMethod.SwingPoint:
                {
                    double swLow = LastSwingLow(150);
                    return swLow > 0 ? swLow - TickSize : fillPrice - FixedSLPts;
                }

                // iv)  One tick below the middle candle of the FVG that triggered entry
                case NQORStopMethod.FVGCandle:
                    return _longFVGFound
                        ? _longFVGCandleLow - TickSize
                        : fillPrice - FixedSLPts;   // fallback to fixed

                default:
                    return fillPrice - FixedSLPts;
            }
        }

        private double CalcShortSL(double fillPrice)
        {
            switch (StopMethod)
            {
                // i)   Fixed points above entry
                case NQORStopMethod.FixedPoints:
                    return fillPrice + FixedSLPts;

                // ii)  % of (OR Low – Target) above entry
                case NQORStopMethod.PercentRange:
                    return fillPrice + (_orLow - _shortTarget) * (SLRangePct / 100.0);

                // iii) One tick above the most recent swing high
                case NQORStopMethod.SwingPoint:
                {
                    double swHigh = LastSwingHigh(150);
                    return swHigh > 0 ? swHigh + TickSize : fillPrice + FixedSLPts;
                }

                // iv)  One tick above the middle candle of the FVG that triggered entry
                case NQORStopMethod.FVGCandle:
                    return _shortFVGFound
                        ? _shortFVGCandleHigh + TickSize
                        : fillPrice + FixedSLPts;

                default:
                    return fillPrice + FixedSLPts;
            }
        }

        // =====================================================================
        //  SWING INDICATOR HELPERS
        // =====================================================================

        private double LastSwingLow(int lookbackBars)
        {
            int n = Math.Min(lookbackBars, CurrentBar);
            for (int i = 0; i < n; i++)
            {
                double v = _swingInd.SwingLow[i];
                if (v > 0 && !double.IsNaN(v)) return v;
            }
            return 0;
        }

        private double LastSwingHigh(int lookbackBars)
        {
            int n = Math.Min(lookbackBars, CurrentBar);
            for (int i = 0; i < n; i++)
            {
                double v = _swingInd.SwingHigh[i];
                if (v > 0 && !double.IsNaN(v)) return v;
            }
            return 0;
        }

        // =====================================================================
        //  DRAWING
        // =====================================================================

        /// <summary>
        /// Draws all six session lines (OR H/L, Targets, Triggers).
        /// Lines are bounded from 09:30 to SessionEnd so they don't
        /// bleed across sessions or overnight.
        /// </summary>
        private void DrawSessionLines()
        {
            // Both anchors built from the calendar date of the OR day
            DateTime t1 = _orDate.Add(new TimeSpan(9, 30, 0));
            DateTime t2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));

            // ── Opening Range – Magenta solid ─────────────────────────────────
            Draw.Line(this, "OR_H",  false, t1, _orHigh,        t2, _orHigh,        Brushes.Magenta,    DashStyleHelper.Solid, 2);
            Draw.Line(this, "OR_L",  false, t1, _orLow,         t2, _orLow,         Brushes.Magenta,    DashStyleHelper.Solid, 2);

            // ── Targets – DodgerBlue solid ────────────────────────────────────
            Draw.Line(this, "LT",    false, t1, _longTarget,     t2, _longTarget,     Brushes.DodgerBlue, DashStyleHelper.Solid, 2);
            Draw.Line(this, "ST",    false, t1, _shortTarget,    t2, _shortTarget,    Brushes.DodgerBlue, DashStyleHelper.Solid, 2);

            // ── Triggers – Orange dashed ──────────────────────────────────────
            Draw.Line(this, "LTr",   false, t1, _longTrigger,    t2, _longTrigger,    Brushes.Orange,     DashStyleHelper.Dash,  2);
            Draw.Line(this, "STr",   false, t1, _shortTrigger,   t2, _shortTrigger,   Brushes.Orange,     DashStyleHelper.Dash,  2);
        }

        // =====================================================================
        //  FORCE FLATTEN
        // =====================================================================
        private void FlattenEverything()
        {
            TryCancelOrder(_longLimitOrder);
            TryCancelOrder(_shortLimitOrder);

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong("ForceFlatL", "LongEntry");
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort("ForceFlatS", "ShortEntry");

            Print(Time[0] + " | ★ Force flatten executed.");
        }

        private void TryCancelOrder(Order o)
        {
            if (o == null) return;
            if (o.OrderState == OrderState.Working  ||
                o.OrderState == OrderState.Accepted)
                CancelOrder(o);
        }

        // =====================================================================
        //  SESSION RESET
        // =====================================================================
        private void ResetSession()
        {
            // Opening Range
            _orHigh = _orLow = _orRange = 0;
            _orSet  = false;

            // Levels
            _longTarget = _shortTarget = 0;
            _longTrigger = _shortTrigger = 0;

            // Long state
            _longBreakout   = false;
            _longTriggerHit = false;
            _longEntryArmed = false;
            _longInTrade    = false;
            _longFVGPending = false;
            _longLimitOrder = null;

            // Short state
            _shortBreakout   = false;
            _shortTriggerHit = false;
            _shortEntryArmed = false;
            _shortInTrade    = false;
            _shortFVGPending = false;
            _shortLimitOrder = null;

            // FVG data
            _longFVGFound = _shortFVGFound = false;
            _longFVGBot = _longFVGTop = _longFVGMid = _longFVGCandleLow    = 0;
            _shortFVGBot = _shortFVGTop = _shortFVGMid = _shortFVGCandleHigh = 0;

            // Pending OCO
            _applyLongOCO  = _applyShortOCO  = false;
            _pendingLongSL = _pendingShortSL = 0;
            _pendingLongTP = _pendingShortTP = 0;

            // Re-arm
            _longRearmNeeded = _shortRearmNeeded = false;

            // Break-even
            _longEntryPrice  = _shortEntryPrice  = 0;
            _longBEMoved     = _shortBEMoved     = false;
            _longBEPrice     = _shortBEPrice     = 0;
            _applyLongBE     = _applyShortBE     = false;
            _pendingLongBESL = _pendingShortBESL = 0;

            // Session counters
            _tradesToday           = 0;
            _profitableTradesToday = 0;
            _losingTradesToday     = 0;
            _lastWinDirection      = 0;   // reset alternating state each session
            _forceFlatDone         = false;
        }

        // =====================================================================
        //  PROPERTIES  –  NinjaTrader parameter UI
        // =====================================================================
        #region Properties

        // ── Group 1 : Opening Range ──────────────────────────────────────────

        [NinjaScriptProperty]
        [Range(0.5, 20.0)]
        [Display(Name = "OR Multiplier",
                 Description = "Target = OR High/Low ± Multiplier × OR Range",
                 GroupName = "1 - Opening Range", Order = 1)]
        public double ORMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Trigger Offset (pts)",
                 Description = "Distance from Target to Trigger line",
                 GroupName = "1 - Opening Range", Order = 2)]
        public double TriggerOffset { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Min OR Size (pts)",
                 Description = "Skip session if OR range is smaller than this",
                 GroupName = "1 - Opening Range", Order = 3)]
        public double MinORSize { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max OR Size (pts)",
                 Description = "Skip session if OR range is larger than this",
                 GroupName = "1 - Opening Range", Order = 4)]
        public double MaxORSize { get; set; }

        // ── Group 2 : Trade Management ───────────────────────────────────────

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts",
                 Description = "Number of contracts per trade",
                 GroupName = "2 - Trade Management", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Trades Per Day",
                 Description = "Maximum entries per session (both sides combined)",
                 GroupName = "2 - Trade Management", Order = 2)]
        public int MaxTradesPerDay { get; set; }

        // ── Group 3 : Entry Method ───────────────────────────────────────────

        [NinjaScriptProperty]
        [Display(Name = "Entry Method",
                 Description = "How to enter after trigger is crossed",
                 GroupName = "3 - Entry Method", Order = 1)]
        public NQOREntryMethod EntryMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "Retracement % (method ii)",
                 Description = "Enter at Target minus X% of (Target - OR High/Low)",
                 GroupName = "3 - Entry Method", Order = 2)]
        public double RetracePct { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Min FVG Size pts (method iii)",
                 Description = "Only use FVGs at least this many points wide",
                 GroupName = "3 - Entry Method", Order = 3)]
        public double MinFVGSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG Entry Point (method iii)",
                 Description = "Where within the FVG to place the limit order",
                 GroupName = "3 - Entry Method", Order = 4)]
        public NQORFVGEntry FVGEntry { get; set; }

        // ── Group 4 : Stop Loss ──────────────────────────────────────────────

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Method",
                 Description = "How to calculate the stop loss level",
                 GroupName = "4 - Stop Loss", Order = 1)]
        public NQORStopMethod StopMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Fixed SL Points (method i)",
                 Description = "Fixed distance in points from entry to stop",
                 GroupName = "4 - Stop Loss", Order = 2)]
        public double FixedSLPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "SL % of Range (method ii)",
                 Description = "Stop = Entry ± X% × (Target - OR High/Low)",
                 GroupName = "4 - Stop Loss", Order = 3)]
        public double SLRangePct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Swing Strength (method iii)",
                 Description = "Bar lookback strength for NinjaTrader Swing indicator",
                 GroupName = "4 - Stop Loss", Order = 4)]
        public int SwingStrength { get; set; }

        // ── Group 5 : Time Filters ───────────────────────────────────────────

        [NinjaScriptProperty]
        [Range(9, 17)]
        [Display(Name = "No New Trades After – Hour (EST)",
                 GroupName = "5 - Time Filters", Order = 1)]
        public int NoNewTradesHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "No New Trades After – Minute",
                 GroupName = "5 - Time Filters", Order = 2)]
        public int NoNewTradesMin { get; set; }

        [NinjaScriptProperty]
        [Range(9, 18)]
        [Display(Name = "Force Flatten – Hour (EST)",
                 GroupName = "5 - Time Filters", Order = 3)]
        public int ForceFlatHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Force Flatten – Minute",
                 GroupName = "5 - Time Filters", Order = 4)]
        public int ForceFlatMin { get; set; }

        [NinjaScriptProperty]
        [Range(9, 23)]
        [Display(Name = "Session Lines End – Hour (EST)",
                 Description = "Visual end time of OR / Target / Trigger lines",
                 GroupName = "5 - Time Filters", Order = 5)]
        public int SessionEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Session Lines End – Minute",
                 GroupName = "5 - Time Filters", Order = 6)]
        public int SessionEndMin { get; set; }

        // ── Group 6 : Break-Even ─────────────────────────────────────────────

        [NinjaScriptProperty]
        [Display(Name = "Break-Even Enabled",
                 Description = "Move SL to entry + offset once price moves the trigger distance",
                 GroupName = "6 - Break-Even", Order = 1)]
        public bool BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Method",
                 Description = "Measure trigger distance in fixed pts or % of OR range",
                 GroupName = "6 - Break-Even", Order = 2)]
        public NQORBEMethod BEMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "BE Trigger – Fixed Pts",
                 Description = "Price must move this many pts from entry to trigger BE (FixedPoints mode)",
                 GroupName = "6 - Break-Even", Order = 3)]
        public double BETriggerPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "BE Trigger – % of OR Range",
                 Description = "Price must move X% of OR range from entry to trigger BE (PercentOR mode)",
                 GroupName = "6 - Break-Even", Order = 4)]
        public double BETriggerPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "BE Offset Pts",
                 Description = "SL is placed this many pts ABOVE entry for longs, BELOW for shorts",
                 GroupName = "6 - Break-Even", Order = 5)]
        public double BEOffsetPts { get; set; }

        // ── Group 7 : Session Trade Filters ──────────────────────────────────

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Profitable Trades / Session",
                 Description = "Stop taking new entries after this many winning trades",
                 GroupName = "7 - Session Filters", Order = 1)]
        public int MaxProfitableTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Losing Trades / Session",
                 Description = "Stop taking new entries after this many losing trades",
                 GroupName = "7 - Session Filters", Order = 2)]
        public int MaxLosingTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alternating Direction",
                 Description = "After a WIN, only allow the opposite direction next entry",
                 GroupName = "7 - Session Filters", Order = 3)]
        public bool AlternatingEnabled { get; set; }

        #endregion
    }
}
