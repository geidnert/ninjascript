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

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public enum NQOREntryMethod105
    {
        [Description("i)  Market order on trigger")]   MarketOnTrigger  = 0,
        [Description("ii) Retracement limit order")]   RetracementLimit = 1,
        [Description("iii) FVG limit order")]          FVGLimit         = 2
    }

    public enum NQORFVGEntry105
    {
        [Description("Top of FVG (first touch)")]  Top    = 0,
        [Description("Midpoint of FVG")]           Mid    = 1,
        [Description("Bottom of FVG (deepest)")]   Bottom = 2
    }

    public enum NQORStopMethod105
    {
        [Description("i)   Fixed points")]     FixedPoints  = 0,
        [Description("ii)  % of range")]       PercentRange = 1,
        [Description("iii) Last swing point")] SwingPoint   = 2,
        [Description("iv)  FVG candle")]       FVGCandle    = 3
    }

    public enum NQORBEMethod105
    {
        [Description("Fixed points from entry")] FixedPoints = 0,
        [Description("% of OR range")]           PercentOR   = 1
    }

    // =========================================================================
    public class EVETesting : Strategy
    {
        // ── Opening Range ─────────────────────────────────────────────────────
        private double   _orHigh, _orLow, _orRange;
        private bool     _orSet;
        private DateTime _orDate;

        // Active bucket per direction: 0 = none, 1 = B1, 2 = B2, 3 = B3
        private int  _longActiveBucket, _shortActiveBucket;
        private bool _longORValid, _shortORValid;

        // ── Calculated levels ─────────────────────────────────────────────────
        private double _longTarget,  _shortTarget;
        private double _longTrigger, _shortTrigger;

        // ── Long side state machine ───────────────────────────────────────────
        private bool  _longBreakout;
        private bool  _longTriggerHit;
        private bool  _longEntryArmed;
        private bool  _longInTrade;
        private bool  _longFVGPending;
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

        // ── Pending OCO bracket ───────────────────────────────────────────────
        private bool   _applyLongOCO,  _applyShortOCO;
        private double _pendingLongSL,  _pendingShortSL;
        private double _pendingLongTP,  _pendingShortTP;

        // ── Re-arm flags ──────────────────────────────────────────────────────
        private bool _longRearmNeeded, _shortRearmNeeded;

        // ── Session bookkeeping – separate per direction ──────────────────────
        private int      _longTradesToday,    _shortTradesToday;
        private int      _longProfitableToday, _shortProfitableToday;
        private int      _longLosingToday,     _shortLosingToday;
        private bool     _forceFlatDone;
        private DateTime _lastResetDate = DateTime.MinValue;
        private int      _drawSeq;

        // ── Win / Loss – alternating state (shared) ───────────────────────────
        // _lastWinDirection: 0=no win yet, 1=Long, 2=Short
        private int  _lastWinDirection;

        // ── Break-even tracking ───────────────────────────────────────────────
        private double _longEntryPrice,  _shortEntryPrice;
        private bool   _longBEMoved,     _shortBEMoved;
        private double _longBEPrice,     _shortBEPrice;
        private bool   _applyLongBE,     _applyShortBE;
        private double _pendingLongBESL, _pendingShortBESL;

        // ── Swing indicators – one per bucket per direction ───────────────────
        private NinjaTrader.NinjaScript.Indicators.Swing _longB1SwingInd,  _longB2SwingInd,  _longB3SwingInd;
        private NinjaTrader.NinjaScript.Indicators.Swing _shortB1SwingInd, _shortB2SwingInd, _shortB3SwingInd;

        // =====================================================================
        //  BUCKET PARAMETER HELPERS  (resolve active bucket → correct property)
        // =====================================================================

        // ── Long getters ──────────────────────────────────────────────────────
        private int    GetLongContracts()            { switch (_longActiveBucket) { case 1: return LongB1Contracts;           case 2: return LongB2Contracts;           default: return LongB3Contracts;           } }
        private int    GetLongMaxTradesPerDay()      { switch (_longActiveBucket) { case 1: return LongB1MaxTradesPerDay;     case 2: return LongB2MaxTradesPerDay;     default: return LongB3MaxTradesPerDay;     } }
        private int    GetLongMaxProfitableTrades()  { switch (_longActiveBucket) { case 1: return LongB1MaxProfitableTrades; case 2: return LongB2MaxProfitableTrades; default: return LongB3MaxProfitableTrades; } }
        private int    GetLongMaxLosingTrades()      { switch (_longActiveBucket) { case 1: return LongB1MaxLosingTrades;     case 2: return LongB2MaxLosingTrades;     default: return LongB3MaxLosingTrades;     } }
        private double GetLongORMultiplier()         { switch (_longActiveBucket) { case 1: return LongB1ORMultiplier;        case 2: return LongB2ORMultiplier;        default: return LongB3ORMultiplier;        } }
        private double GetLongTriggerOffset()        { switch (_longActiveBucket) { case 1: return LongB1TriggerOffset;       case 2: return LongB2TriggerOffset;       default: return LongB3TriggerOffset;       } }
        private NQOREntryMethod105 GetLongEntryMethod()  { switch (_longActiveBucket) { case 1: return LongB1EntryMethod;    case 2: return LongB2EntryMethod;    default: return LongB3EntryMethod;    } }
        private double GetLongRetracePct()           { switch (_longActiveBucket) { case 1: return LongB1RetracePct;          case 2: return LongB2RetracePct;          default: return LongB3RetracePct;          } }
        private double GetLongMinFVGSize()           { switch (_longActiveBucket) { case 1: return LongB1MinFVGSize;          case 2: return LongB2MinFVGSize;          default: return LongB3MinFVGSize;          } }
        private NQORFVGEntry105 GetLongFVGEntry()        { switch (_longActiveBucket) { case 1: return LongB1FVGEntry;        case 2: return LongB2FVGEntry;        default: return LongB3FVGEntry;        } }
        private NQORStopMethod105 GetLongStopMethod()    { switch (_longActiveBucket) { case 1: return LongB1StopMethod;      case 2: return LongB2StopMethod;      default: return LongB3StopMethod;      } }
        private double GetLongFixedSLPts()           { switch (_longActiveBucket) { case 1: return LongB1FixedSLPts;          case 2: return LongB2FixedSLPts;          default: return LongB3FixedSLPts;          } }
        private double GetLongSLRangePct()           { switch (_longActiveBucket) { case 1: return LongB1SLRangePct;          case 2: return LongB2SLRangePct;          default: return LongB3SLRangePct;          } }
        private int    GetLongSwingStrength()        { switch (_longActiveBucket) { case 1: return LongB1SwingStrength;       case 2: return LongB2SwingStrength;       default: return LongB3SwingStrength;       } }
        private bool   GetLongBEEnabled()            { switch (_longActiveBucket) { case 1: return LongB1BEEnabled;           case 2: return LongB2BEEnabled;           default: return LongB3BEEnabled;           } }
        private NQORBEMethod105 GetLongBEMethod()        { switch (_longActiveBucket) { case 1: return LongB1BEMethod;        case 2: return LongB2BEMethod;        default: return LongB3BEMethod;        } }
        private double GetLongBETriggerPts()         { switch (_longActiveBucket) { case 1: return LongB1BETriggerPts;        case 2: return LongB2BETriggerPts;        default: return LongB3BETriggerPts;        } }
        private double GetLongBETriggerPct()         { switch (_longActiveBucket) { case 1: return LongB1BETriggerPct;        case 2: return LongB2BETriggerPct;        default: return LongB3BETriggerPct;        } }
        private double GetLongBEOffsetPts()          { switch (_longActiveBucket) { case 1: return LongB1BEOffsetPts;         case 2: return LongB2BEOffsetPts;         default: return LongB3BEOffsetPts;         } }

        // ── Short getters ─────────────────────────────────────────────────────
        private int    GetShortContracts()           { switch (_shortActiveBucket) { case 1: return ShortB1Contracts;           case 2: return ShortB2Contracts;           default: return ShortB3Contracts;           } }
        private int    GetShortMaxTradesPerDay()     { switch (_shortActiveBucket) { case 1: return ShortB1MaxTradesPerDay;     case 2: return ShortB2MaxTradesPerDay;     default: return ShortB3MaxTradesPerDay;     } }
        private int    GetShortMaxProfitableTrades() { switch (_shortActiveBucket) { case 1: return ShortB1MaxProfitableTrades; case 2: return ShortB2MaxProfitableTrades; default: return ShortB3MaxProfitableTrades; } }
        private int    GetShortMaxLosingTrades()     { switch (_shortActiveBucket) { case 1: return ShortB1MaxLosingTrades;     case 2: return ShortB2MaxLosingTrades;     default: return ShortB3MaxLosingTrades;     } }
        private double GetShortORMultiplier()        { switch (_shortActiveBucket) { case 1: return ShortB1ORMultiplier;        case 2: return ShortB2ORMultiplier;        default: return ShortB3ORMultiplier;        } }
        private double GetShortTriggerOffset()       { switch (_shortActiveBucket) { case 1: return ShortB1TriggerOffset;       case 2: return ShortB2TriggerOffset;       default: return ShortB3TriggerOffset;       } }
        private NQOREntryMethod105 GetShortEntryMethod() { switch (_shortActiveBucket) { case 1: return ShortB1EntryMethod;    case 2: return ShortB2EntryMethod;    default: return ShortB3EntryMethod;    } }
        private double GetShortRetracePct()          { switch (_shortActiveBucket) { case 1: return ShortB1RetracePct;          case 2: return ShortB2RetracePct;          default: return ShortB3RetracePct;          } }
        private double GetShortMinFVGSize()          { switch (_shortActiveBucket) { case 1: return ShortB1MinFVGSize;          case 2: return ShortB2MinFVGSize;          default: return ShortB3MinFVGSize;          } }
        private NQORFVGEntry105 GetShortFVGEntry()       { switch (_shortActiveBucket) { case 1: return ShortB1FVGEntry;        case 2: return ShortB2FVGEntry;        default: return ShortB3FVGEntry;        } }
        private NQORStopMethod105 GetShortStopMethod()   { switch (_shortActiveBucket) { case 1: return ShortB1StopMethod;      case 2: return ShortB2StopMethod;      default: return ShortB3StopMethod;      } }
        private double GetShortFixedSLPts()          { switch (_shortActiveBucket) { case 1: return ShortB1FixedSLPts;          case 2: return ShortB2FixedSLPts;          default: return ShortB3FixedSLPts;          } }
        private double GetShortSLRangePct()          { switch (_shortActiveBucket) { case 1: return ShortB1SLRangePct;          case 2: return ShortB2SLRangePct;          default: return ShortB3SLRangePct;          } }
        private int    GetShortSwingStrength()       { switch (_shortActiveBucket) { case 1: return ShortB1SwingStrength;       case 2: return ShortB2SwingStrength;       default: return ShortB3SwingStrength;       } }
        private bool   GetShortBEEnabled()           { switch (_shortActiveBucket) { case 1: return ShortB1BEEnabled;           case 2: return ShortB2BEEnabled;           default: return ShortB3BEEnabled;           } }
        private NQORBEMethod105 GetShortBEMethod()       { switch (_shortActiveBucket) { case 1: return ShortB1BEMethod;        case 2: return ShortB2BEMethod;        default: return ShortB3BEMethod;        } }
        private double GetShortBETriggerPts()        { switch (_shortActiveBucket) { case 1: return ShortB1BETriggerPts;        case 2: return ShortB2BETriggerPts;        default: return ShortB3BETriggerPts;        } }
        private double GetShortBETriggerPct()        { switch (_shortActiveBucket) { case 1: return ShortB1BETriggerPct;        case 2: return ShortB2BETriggerPct;        default: return ShortB3BETriggerPct;        } }
        private double GetShortBEOffsetPts()         { switch (_shortActiveBucket) { case 1: return ShortB1BEOffsetPts;         case 2: return ShortB2BEOffsetPts;         default: return ShortB3BEOffsetPts;         } }

        // ── Swing indicator helpers ───────────────────────────────────────────
        private NinjaTrader.NinjaScript.Indicators.Swing GetLongSwingInd()
        {
            switch (_longActiveBucket)  { case 1: return _longB1SwingInd;  case 2: return _longB2SwingInd;  default: return _longB3SwingInd;  }
        }
        private NinjaTrader.NinjaScript.Indicators.Swing GetShortSwingInd()
        {
            switch (_shortActiveBucket) { case 1: return _shortB1SwingInd; case 2: return _shortB2SwingInd; default: return _shortB3SwingInd; }
        }

        // =====================================================================
        //  STATE CHANGE
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "EVETesting";
                Description = "EVETesting";
                Calculate   = Calculate.OnBarClose;

                EntriesPerDirection = 10;
                EntryHandling       = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;

                // ── Shared: Time Filters ───────────────────────────────────────
                NoNewTradesHour = 14;
                NoNewTradesMin  = 36;
                ForceFlatHour   = 15;
                ForceFlatMin    = 57;
                SessionEndHour  = 17;
                SessionEndMin   = 0;

                // ── Shared: Session Filters ────────────────────────────────────
                AlternatingEnabled = false;

                // ══════════════════════════════════════════════════════════════
                //  LONG BUCKET 1  (Small OR: 7 – 22.75 pts)
                // ══════════════════════════════════════════════════════════════
                LongB1Enabled            = true;
                LongB1MinORSize          = 7.0;
                LongB1MaxORSize          = 22.75;
                LongB1ORMultiplier       = 5.05;
                LongB1TriggerOffset      = 15.0;
                LongB1Contracts          = 5;
                LongB1MaxTradesPerDay    = 1;
                LongB1MaxProfitableTrades = 1;
                LongB1MaxLosingTrades    = 1;
                LongB1EntryMethod        = NQOREntryMethod105.RetracementLimit;
                LongB1RetracePct         = 79.06;
                LongB1MinFVGSize         = 21.0;
                LongB1FVGEntry           = NQORFVGEntry105.Bottom;
                LongB1StopMethod         = NQORStopMethod105.PercentRange;
                LongB1FixedSLPts         = 88.0;
                LongB1SLRangePct         = 58.5;
                LongB1SwingStrength      = 9;
                LongB1BEEnabled          = false;
                LongB1BEMethod           = NQORBEMethod105.FixedPoints;
                LongB1BETriggerPts       = 70.0;
                LongB1BETriggerPct       = 29.0;
                LongB1BEOffsetPts        = 1.5;

                // ══════════════════════════════════════════════════════════════
                //  LONG BUCKET 2  (Medium OR: 23 – 46 pts)
                // ══════════════════════════════════════════════════════════════
                LongB2Enabled            = true;
                LongB2MinORSize          = 23.0;
                LongB2MaxORSize          = 46.0;
                LongB2ORMultiplier       = 4.41;
                LongB2TriggerOffset      = 18.25;
                LongB2Contracts          = 5;
                LongB2MaxTradesPerDay    = 1;
                LongB2MaxProfitableTrades = 1;
                LongB2MaxLosingTrades    = 1;
                LongB2EntryMethod        = NQOREntryMethod105.RetracementLimit;
                LongB2RetracePct         = 74.1;
                LongB2MinFVGSize         = 21.0;
                LongB2FVGEntry           = NQORFVGEntry105.Bottom;
                LongB2StopMethod         = NQORStopMethod105.PercentRange;
                LongB2FixedSLPts         = 101.0;
                LongB2SLRangePct         = 66.0;
                LongB2SwingStrength      = 9;
                LongB2BEEnabled          = false;
                LongB2BEMethod           = NQORBEMethod105.FixedPoints;
                LongB2BETriggerPts       = 63.0;
                LongB2BETriggerPct       = 53.0;
                LongB2BEOffsetPts        = 4.0;

                // ══════════════════════════════════════════════════════════════
                //  LONG BUCKET 3  (Large OR: 46 – 76 pts)
                // ══════════════════════════════════════════════════════════════
                LongB3Enabled            = true;
                LongB3MinORSize          = 46.0;
                LongB3MaxORSize          = 76.0;
                LongB3ORMultiplier       = 4.95;
                LongB3TriggerOffset      = 15.0;
                LongB3Contracts          = 5;
                LongB3MaxTradesPerDay    = 2;
                LongB3MaxProfitableTrades = 2;
                LongB3MaxLosingTrades    = 1;
                LongB3EntryMethod        = NQOREntryMethod105.RetracementLimit;
                LongB3RetracePct         = 76.88;
                LongB3MinFVGSize         = 21.0;
                LongB3FVGEntry           = NQORFVGEntry105.Bottom;
                LongB3StopMethod         = NQORStopMethod105.PercentRange;
                LongB3FixedSLPts         = 101.0;
                LongB3SLRangePct         = 51.02;
                LongB3SwingStrength      = 9;
                LongB3BEEnabled          = true;
                LongB3BEMethod           = NQORBEMethod105.FixedPoints;
                LongB3BETriggerPts       = 73.0;
                LongB3BETriggerPct       = 21.0;
                LongB3BEOffsetPts        = 1.5;

                // ══════════════════════════════════════════════════════════════
                //  SHORT BUCKET 1  (Small OR: 7 – 30 pts)
                // ══════════════════════════════════════════════════════════════
                ShortB1Enabled            = true;
                ShortB1MinORSize          = 7.0;
                ShortB1MaxORSize          = 30.0;
                ShortB1ORMultiplier       = 4.86;
                ShortB1TriggerOffset      = 10.0;
                ShortB1Contracts          = 5;
                ShortB1MaxTradesPerDay    = 2;
                ShortB1MaxProfitableTrades = 2;
                ShortB1MaxLosingTrades    = 2;
                ShortB1EntryMethod        = NQOREntryMethod105.RetracementLimit;
                ShortB1RetracePct         = 70.95;
                ShortB1MinFVGSize         = 21.0;
                ShortB1FVGEntry           = NQORFVGEntry105.Bottom;
                ShortB1StopMethod         = NQORStopMethod105.PercentRange;
                ShortB1FixedSLPts         = 44.0;
                ShortB1SLRangePct         = 86.69;
                ShortB1SwingStrength      = 5;
                ShortB1BEEnabled          = true;
                ShortB1BEMethod           = NQORBEMethod105.FixedPoints;
                ShortB1BETriggerPts       = 40.0;
                ShortB1BETriggerPct       = 21.0;
                ShortB1BEOffsetPts        = 1.5;

                // ══════════════════════════════════════════════════════════════
                //  SHORT BUCKET 2  (Medium OR: 30 – 55 pts)
                // ══════════════════════════════════════════════════════════════
                ShortB2Enabled            = true;
                ShortB2MinORSize          = 30.0;
                ShortB2MaxORSize          = 55.0;
                ShortB2ORMultiplier       = 5.03;
                ShortB2TriggerOffset      = 15.0;
                ShortB2Contracts          = 5;
                ShortB2MaxTradesPerDay    = 3;
                ShortB2MaxProfitableTrades = 2;
                ShortB2MaxLosingTrades    = 2;
                ShortB2EntryMethod        = NQOREntryMethod105.RetracementLimit;
                ShortB2RetracePct         = 70.4;
                ShortB2MinFVGSize         = 21.0;
                ShortB2FVGEntry           = NQORFVGEntry105.Bottom;
                ShortB2StopMethod         = NQORStopMethod105.PercentRange;
                ShortB2FixedSLPts         = 44.0;
                ShortB2SLRangePct         = 83.69;
                ShortB2SwingStrength      = 5;
                ShortB2BEEnabled          = true;
                ShortB2BEMethod           = NQORBEMethod105.FixedPoints;
                ShortB2BETriggerPts       = 37.0;
                ShortB2BETriggerPct       = 56.0;
                ShortB2BEOffsetPts        = 8.0;

                // ══════════════════════════════════════════════════════════════
                //  SHORT BUCKET 3  (Large OR: 55 – 70 pts)
                // ══════════════════════════════════════════════════════════════
                ShortB3Enabled            = true;
                ShortB3MinORSize          = 55.0;
                ShortB3MaxORSize          = 70.0;
                ShortB3ORMultiplier       = 5.49;
                ShortB3TriggerOffset      = 11.0;
                ShortB3Contracts          = 5;
                ShortB3MaxTradesPerDay    = 2;
                ShortB3MaxProfitableTrades = 2;
                ShortB3MaxLosingTrades    = 2;
                ShortB3EntryMethod        = NQOREntryMethod105.RetracementLimit;
                ShortB3RetracePct         = 70.47;
                ShortB3MinFVGSize         = 21.0;
                ShortB3FVGEntry           = NQORFVGEntry105.Bottom;
                ShortB3StopMethod         = NQORStopMethod105.PercentRange;
                ShortB3FixedSLPts         = 44.0;
                ShortB3SLRangePct         = 50.0;
                ShortB3SwingStrength      = 5;
                ShortB3BEEnabled          = false;
                ShortB3BEMethod           = NQORBEMethod105.FixedPoints;
                ShortB3BETriggerPts       = 41.0;
                ShortB3BETriggerPct       = 42.0;
                ShortB3BEOffsetPts        = 1.5;
            }
            else if (State == State.DataLoaded)
            {
                _longB1SwingInd  = Swing(LongB1SwingStrength);
                _longB2SwingInd  = Swing(LongB2SwingStrength);
                _longB3SwingInd  = Swing(LongB3SwingStrength);
                _shortB1SwingInd = Swing(ShortB1SwingStrength);
                _shortB2SwingInd = Swing(ShortB2SwingStrength);
                _shortB3SwingInd = Swing(ShortB3SwingStrength);
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

            // ── B. Apply pending OCO brackets ─────────────────────────────────
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

            // ── D. Capture OR candle and select active bucket ─────────────────
            if (!_orSet)
            {
                if (tod == new TimeSpan(9, 30, 30))
                {
                    double range = High[0] - Low[0];

                    _orHigh  = High[0];
                    _orLow   = Low[0];
                    _orRange = range;
                    _orSet   = true;
                    _orDate  = Time[0].Date;

                    // ── Bucket selection: Long ────────────────────────────────
                    _longActiveBucket = 0;
                    if (LongB1Enabled && range >= LongB1MinORSize && range <  LongB1MaxORSize)
                        _longActiveBucket = 1;
                    else if (LongB2Enabled && range >= LongB2MinORSize && range <  LongB2MaxORSize)
                        _longActiveBucket = 2;
                    else if (LongB3Enabled && range >= LongB3MinORSize && range <= LongB3MaxORSize)
                        _longActiveBucket = 3;

                    _longORValid = (_longActiveBucket > 0);

                    // ── Bucket selection: Short ───────────────────────────────
                    _shortActiveBucket = 0;
                    if (ShortB1Enabled && range >= ShortB1MinORSize && range <  ShortB1MaxORSize)
                        _shortActiveBucket = 1;
                    else if (ShortB2Enabled && range >= ShortB2MinORSize && range <  ShortB2MaxORSize)
                        _shortActiveBucket = 2;
                    else if (ShortB3Enabled && range >= ShortB3MinORSize && range <= ShortB3MaxORSize)
                        _shortActiveBucket = 3;

                    _shortORValid = (_shortActiveBucket > 0);

                    if (!_longORValid && !_shortORValid)
                    {
                        Print(Time[0] + " | OR size " + range + " pts – no matching bucket for either direction. Session skipped.");
                        _forceFlatDone = true;
                        return;
                    }

                    if (!_longORValid)
                        Print(Time[0] + " | OR size " + range + " pts – no matching Long bucket. Long side skipped.");
                    if (!_shortORValid)
                        Print(Time[0] + " | OR size " + range + " pts – no matching Short bucket. Short side skipped.");

                    // ── Compute levels using the active bucket's parameters ────
                    if (_longORValid)
                    {
                        _longTarget  = _orHigh + GetLongORMultiplier()  * _orRange;
                        _longTrigger = _longTarget - GetLongTriggerOffset();
                    }
                    if (_shortORValid)
                    {
                        _shortTarget  = _orLow - GetShortORMultiplier() * _orRange;
                        _shortTrigger = _shortTarget + GetShortTriggerOffset();
                    }

                    DrawSessionLines();

                    Print(Time[0] + " | ═══ OR captured ═══"
                          + "  H=" + _orHigh + "  L=" + _orLow + "  Range=" + _orRange
                          + (_longORValid  ? " | Long  Bucket=" + _longActiveBucket  + "  Target=" + _longTarget  + "  Trigger=" + _longTrigger  : " | Long  skipped")
                          + (_shortORValid ? " | Short Bucket=" + _shortActiveBucket + "  Target=" + _shortTarget + "  Trigger=" + _shortTrigger : " | Short skipped"));
                }
                return;
            }

            // ── E. Trading permission gate (per direction) ────────────────────
            var noNewTime = new TimeSpan(NoNewTradesHour, NoNewTradesMin, 0);
            bool canTradeLong  = _longORValid
                              && tod < noNewTime
                              && _longTradesToday     < GetLongMaxTradesPerDay()
                              && _longProfitableToday < GetLongMaxProfitableTrades()
                              && _longLosingToday     < GetLongMaxLosingTrades();

            bool canTradeShort = _shortORValid
                              && tod < noNewTime
                              && _shortTradesToday     < GetShortMaxTradesPerDay()
                              && _shortProfitableToday < GetShortMaxProfitableTrades()
                              && _shortLosingToday     < GetShortMaxLosingTrades();

            // ── F. OR breakout detection ──────────────────────────────────────
            if (_longORValid  && !_longBreakout  && High[0] > _orHigh) _longBreakout  = true;
            if (_shortORValid && !_shortBreakout && Low[0]  < _orLow)  _shortBreakout = true;

            // ── G. FVG scan ───────────────────────────────────────────────────
            if (_longORValid && GetLongEntryMethod() == NQOREntryMethod105.FVGLimit)
            {
                bool longFVGScanActive = _longBreakout && !_longInTrade
                                      && (!_longTriggerHit || _longFVGPending);
                if (longFVGScanActive)
                {
                    double gap = Low[0] - High[2];
                    if (gap >= GetLongMinFVGSize())
                    {
                        _longFVGBot       = High[2];
                        _longFVGTop       = Low[0];
                        _longFVGMid       = (_longFVGBot + _longFVGTop) * 0.5;
                        _longFVGCandleLow = Low[1];
                        _longFVGFound     = true;
                    }
                }
            }

            if (_shortORValid && GetShortEntryMethod() == NQOREntryMethod105.FVGLimit)
            {
                bool shortFVGScanActive = _shortBreakout && !_shortInTrade
                                       && (!_shortTriggerHit || _shortFVGPending);
                if (shortFVGScanActive)
                {
                    double gap = Low[2] - High[0];
                    if (gap >= GetShortMinFVGSize())
                    {
                        _shortFVGTop        = Low[2];
                        _shortFVGBot        = High[0];
                        _shortFVGMid        = (_shortFVGBot + _shortFVGTop) * 0.5;
                        _shortFVGCandleHigh = High[1];
                        _shortFVGFound      = true;
                    }
                }
            }

            // ── G2. Break-Even monitoring ─────────────────────────────────────
            if (GetLongBEEnabled())
            {
                if (_longInTrade && !_longBEMoved && _longEntryPrice > 0)
                {
                    double beTriggerDist = (GetLongBEMethod() == NQORBEMethod105.FixedPoints)
                                        ? GetLongBETriggerPts()
                                        : _orRange * (GetLongBETriggerPct() / 100.0);
                    if (High[0] >= _longEntryPrice + beTriggerDist)
                    {
                        _longBEMoved     = true;
                        _pendingLongBESL = _longEntryPrice + GetLongBEOffsetPts();
                        _applyLongBE     = true;
                        _longBEPrice     = _pendingLongBESL;

                        DateTime beT1 = Time[0];
                        DateTime beT2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
                        Draw.Line(this, "LongBE_" + (++_drawSeq), false,
                                  beT1, _longBEPrice, beT2, _longBEPrice,
                                  Brushes.Lime, DashStyleHelper.Dash, 2);
                    }
                }
            }

            if (GetShortBEEnabled())
            {
                if (_shortInTrade && !_shortBEMoved && _shortEntryPrice > 0)
                {
                    double beTriggerDist = (GetShortBEMethod() == NQORBEMethod105.FixedPoints)
                                        ? GetShortBETriggerPts()
                                        : _orRange * (GetShortBETriggerPct() / 100.0);
                    if (Low[0] <= _shortEntryPrice - beTriggerDist)
                    {
                        _shortBEMoved     = true;
                        _pendingShortBESL = _shortEntryPrice - GetShortBEOffsetPts();
                        _applyShortBE     = true;
                        _shortBEPrice     = _pendingShortBESL;

                        DateTime beT1 = Time[0];
                        DateTime beT2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
                        Draw.Line(this, "ShortBE_" + (++_drawSeq), false,
                                  beT1, _shortBEPrice, beT2, _shortBEPrice,
                                  Brushes.Lime, DashStyleHelper.Dash, 2);
                    }
                }
            }

            // ── H. Trigger line crossed ───────────────────────────────────────
            if (_longBreakout && !_longTriggerHit && High[0] >= _longTrigger)
            {
                _longTriggerHit = true;
                Print(Time[0] + " | Long trigger crossed @ " + _longTrigger + "  [Bucket " + _longActiveBucket + "]");
                if (canTradeLong && !_longInTrade && !_shortInTrade)
                    TryArmLong();
            }

            if (_shortBreakout && !_shortTriggerHit && Low[0] <= _shortTrigger)
            {
                _shortTriggerHit = true;
                Print(Time[0] + " | Short trigger crossed @ " + _shortTrigger + "  [Bucket " + _shortActiveBucket + "]");
                if (canTradeShort && !_shortInTrade && !_longInTrade)
                    TryArmShort();
            }

            // ── I. FVG pending arm ────────────────────────────────────────────
            if (_longFVGPending && !_longEntryArmed && !_longInTrade && canTradeLong && !_shortInTrade)
            {
                if (_longFVGFound)
                {
                    _longFVGPending = false;
                    PlaceLongFVGOrder();
                }
            }
            if (_shortFVGPending && !_shortEntryArmed && !_shortInTrade && canTradeShort && !_longInTrade)
            {
                if (_shortFVGFound)
                {
                    _shortFVGPending = false;
                    PlaceShortFVGOrder();
                }
            }

            // ── J. Re-arm after stop-out ──────────────────────────────────────
            if (_longRearmNeeded && canTradeLong && !_longInTrade && !_longEntryArmed && !_shortInTrade)
            {
                _longRearmNeeded = false;
                TryArmLong();
            }
            if (_shortRearmNeeded && canTradeShort && !_shortInTrade && !_shortEntryArmed && !_longInTrade)
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
                _longTriggerHit = false;
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

        private void TryArmLong()
        {
            if (_longEntryArmed)                                      return;
            if (_longTradesToday     >= GetLongMaxTradesPerDay())      return;
            if (_longProfitableToday >= GetLongMaxProfitableTrades())  return;
            if (_longLosingToday     >= GetLongMaxLosingTrades())      return;

            if (AlternatingEnabled && _lastWinDirection == 1)
            {
                Print(Time[0] + " | Long blocked – alternating rule (last win was Long)");
                return;
            }

            switch (GetLongEntryMethod())
            {
                case NQOREntryMethod105.MarketOnTrigger:
                    _pendingLongSL = CalcLongSL(Close[0]);
                    _pendingLongTP = _longTarget;
                    _applyLongOCO  = true;
                    EnterLong(GetLongContracts(), "LongEntry");
                    _longEntryArmed = true;
                    Print(Time[0] + " | Long MARKET order submitted  [B" + _longActiveBucket + "]");
                    break;

                case NQOREntryMethod105.RetracementLimit:
                {
                    double retDist  = (_longTarget - _orHigh) * (GetLongRetracePct() / 100.0);
                    double limitPx  = _longTarget - retDist;
                    _longLimitOrder = EnterLongLimit(0, true, GetLongContracts(), limitPx, "LongEntry");
                    _longEntryArmed = true;
                    Print(Time[0] + " | Long RETRACEMENT limit @ " + limitPx
                          + "  (" + GetLongRetracePct() + "% of " + (_longTarget - _orHigh) + " pts)"
                          + "  [B" + _longActiveBucket + "]");
                    break;
                }

                case NQOREntryMethod105.FVGLimit:
                    if (_longFVGFound)
                        PlaceLongFVGOrder();
                    else
                    {
                        _longFVGPending = true;
                        Print(Time[0] + " | Long FVG armed – scanning for FVG...  [B" + _longActiveBucket + "]");
                    }
                    break;
            }
        }

        private void TryArmShort()
        {
            if (_shortEntryArmed)                                       return;
            if (_shortTradesToday     >= GetShortMaxTradesPerDay())      return;
            if (_shortProfitableToday >= GetShortMaxProfitableTrades())  return;
            if (_shortLosingToday     >= GetShortMaxLosingTrades())      return;

            if (AlternatingEnabled && _lastWinDirection == 2)
            {
                Print(Time[0] + " | Short blocked – alternating rule (last win was Short)");
                return;
            }

            switch (GetShortEntryMethod())
            {
                case NQOREntryMethod105.MarketOnTrigger:
                    _pendingShortSL = CalcShortSL(Close[0]);
                    _pendingShortTP = _shortTarget;
                    _applyShortOCO  = true;
                    EnterShort(GetShortContracts(), "ShortEntry");
                    _shortEntryArmed = true;
                    Print(Time[0] + " | Short MARKET order submitted  [B" + _shortActiveBucket + "]");
                    break;

                case NQOREntryMethod105.RetracementLimit:
                {
                    double retDist   = (_orLow - _shortTarget) * (GetShortRetracePct() / 100.0);
                    double limitPx   = _shortTarget + retDist;
                    _shortLimitOrder = EnterShortLimit(0, true, GetShortContracts(), limitPx, "ShortEntry");
                    _shortEntryArmed = true;
                    Print(Time[0] + " | Short RETRACEMENT limit @ " + limitPx
                          + "  (" + GetShortRetracePct() + "% of " + (_orLow - _shortTarget) + " pts)"
                          + "  [B" + _shortActiveBucket + "]");
                    break;
                }

                case NQOREntryMethod105.FVGLimit:
                    if (_shortFVGFound)
                        PlaceShortFVGOrder();
                    else
                    {
                        _shortFVGPending = true;
                        Print(Time[0] + " | Short FVG armed – scanning for FVG...  [B" + _shortActiveBucket + "]");
                    }
                    break;
            }
        }

        private void PlaceLongFVGOrder()
        {
            double px       = LongFVGEntryPrice(_longFVGBot, _longFVGTop, _longFVGMid);
            _longLimitOrder = EnterLongLimit(0, true, GetLongContracts(), px, "LongEntry");
            _longEntryArmed = true;
            Print(Time[0] + " | Long FVG LIMIT @ " + px
                  + "  (gap: " + _longFVGBot + "–" + _longFVGTop + ")"
                  + "  [B" + _longActiveBucket + "]");
        }

        private void PlaceShortFVGOrder()
        {
            double px        = ShortFVGEntryPrice(_shortFVGBot, _shortFVGTop, _shortFVGMid);
            _shortLimitOrder = EnterShortLimit(0, true, GetShortContracts(), px, "ShortEntry");
            _shortEntryArmed = true;
            Print(Time[0] + " | Short FVG LIMIT @ " + px
                  + "  (gap: " + _shortFVGBot + "–" + _shortFVGTop + ")"
                  + "  [B" + _shortActiveBucket + "]");
        }

        private double LongFVGEntryPrice(double bot, double top, double mid)
        {
            switch (GetLongFVGEntry())
            {
                case NQORFVGEntry105.Top:    return top;
                case NQORFVGEntry105.Bottom: return bot;
                default:                    return mid;
            }
        }

        private double ShortFVGEntryPrice(double bot, double top, double mid)
        {
            switch (GetShortFVGEntry())
            {
                case NQORFVGEntry105.Top:    return bot;   // deepest for short
                case NQORFVGEntry105.Bottom: return top;   // first touch for short
                default:                    return mid;
            }
        }

        // =====================================================================
        //  ORDER & EXECUTION CALLBACKS
        // =====================================================================

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int qty, int filled, double avgFill, OrderState state,
            DateTime time, ErrorCode err, string comment)
        {
            if (order.Name == "LongEntry")  _longLimitOrder  = order;
            if (order.Name == "ShortEntry") _shortLimitOrder = order;
        }

        protected override void OnExecutionUpdate(Execution exec, string execId, double price,
            int qty, MarketPosition mp, string orderId, DateTime time)
        {
            if (exec.Order.OrderState != OrderState.Filled) return;

            // ── Long entry filled ─────────────────────────────────────────────
            if (exec.Order.Name == "LongEntry")
            {
                _longInTrade    = true;
                _longEntryArmed = false;
                _longLimitOrder = null;
                _longEntryPrice = price;
                _longBEMoved    = false;
                _longTradesToday++;

                double sl = CalcLongSL(price);
                _pendingLongSL = sl;
                _pendingLongTP = _longTarget;
                _applyLongOCO  = true;

                DateTime slT1 = time;
                DateTime slT2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
                Draw.Line(this, "LongSL_" + (++_drawSeq), false,
                          slT1, sl, slT2, sl, Brushes.Red, DashStyleHelper.Dash, 2);

                Print(time + " | ► LONG FILLED @ " + price
                      + "  SL=" + sl + "  TP=" + _longTarget
                      + "  [B" + _longActiveBucket + "  trade " + _longTradesToday + "/" + GetLongMaxTradesPerDay() + "]");
            }

            // ── Short entry filled ────────────────────────────────────────────
            else if (exec.Order.Name == "ShortEntry")
            {
                _shortInTrade    = true;
                _shortEntryArmed = false;
                _shortLimitOrder = null;
                _shortEntryPrice = price;
                _shortBEMoved    = false;
                _shortTradesToday++;

                double sl = CalcShortSL(price);
                _pendingShortSL = sl;
                _pendingShortTP = _shortTarget;
                _applyShortOCO  = true;

                DateTime slT1 = time;
                DateTime slT2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
                Draw.Line(this, "ShortSL_" + (++_drawSeq), false,
                          slT1, sl, slT2, sl, Brushes.Red, DashStyleHelper.Dash, 2);

                Print(time + " | ► SHORT FILLED @ " + price
                      + "  SL=" + sl + "  TP=" + _shortTarget
                      + "  [B" + _shortActiveBucket + "  trade " + _shortTradesToday + "/" + GetShortMaxTradesPerDay() + "]");
            }
        }

        protected override void OnPositionUpdate(Position pos, double avgPx,
            int qty, MarketPosition mp)
        {
            if (mp != MarketPosition.Flat) return;

            // ── Long closed ───────────────────────────────────────────────────
            if (_longInTrade)
            {
                _longInTrade  = false;
                _longFVGFound = false;

                double pnl = 0;
                if (SystemPerformance.AllTrades.Count > 0)
                    pnl = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1].ProfitCurrency;

                bool isScratch = _longBEMoved
                              && Math.Abs(pnl) < TickSize * Instrument.MasterInstrument.PointValue * GetLongContracts() * 2;

                if (isScratch)
                {
                    Print(Time[0] + " | Long closed – SCRATCH (BE stop)");
                }
                else if (pnl > 0)
                {
                    _longProfitableToday++;
                    _lastWinDirection = 1;
                    Print(Time[0] + " | Long closed – WIN  (#" + _longProfitableToday + " profitable today)");
                }
                else
                {
                    _longLosingToday++;
                    Print(Time[0] + " | Long closed – LOSS (#" + _longLosingToday + " losses today)");
                }

                _longBEMoved    = false;
                _longEntryPrice = 0;

                bool canRearm = _longTriggerHit
                             && !_forceFlatDone
                             && _longTradesToday     < GetLongMaxTradesPerDay()
                             && _longProfitableToday < GetLongMaxProfitableTrades()
                             && _longLosingToday     < GetLongMaxLosingTrades()
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
                              && Math.Abs(pnl) < TickSize * Instrument.MasterInstrument.PointValue * GetShortContracts() * 2;

                if (isScratch)
                {
                    Print(Time[0] + " | Short closed – SCRATCH (BE stop)");
                }
                else if (pnl > 0)
                {
                    _shortProfitableToday++;
                    _lastWinDirection = 2;
                    Print(Time[0] + " | Short closed – WIN  (#" + _shortProfitableToday + " profitable today)");
                }
                else
                {
                    _shortLosingToday++;
                    Print(Time[0] + " | Short closed – LOSS (#" + _shortLosingToday + " losses today)");
                }

                _shortBEMoved    = false;
                _shortEntryPrice = 0;

                bool canRearm = _shortTriggerHit
                             && !_forceFlatDone
                             && _shortTradesToday     < GetShortMaxTradesPerDay()
                             && _shortProfitableToday < GetShortMaxProfitableTrades()
                             && _shortLosingToday     < GetShortMaxLosingTrades()
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
            switch (GetLongStopMethod())
            {
                case NQORStopMethod105.FixedPoints:
                    return fillPrice - GetLongFixedSLPts();

                case NQORStopMethod105.PercentRange:
                    return fillPrice - (_longTarget - _orHigh) * (GetLongSLRangePct() / 100.0);

                case NQORStopMethod105.SwingPoint:
                {
                    double swLow = LastSwingLow(150);
                    return swLow > 0 ? swLow - TickSize : fillPrice - GetLongFixedSLPts();
                }

                case NQORStopMethod105.FVGCandle:
                    return _longFVGFound
                        ? _longFVGCandleLow - TickSize
                        : fillPrice - GetLongFixedSLPts();

                default:
                    return fillPrice - GetLongFixedSLPts();
            }
        }

        private double CalcShortSL(double fillPrice)
        {
            switch (GetShortStopMethod())
            {
                case NQORStopMethod105.FixedPoints:
                    return fillPrice + GetShortFixedSLPts();

                case NQORStopMethod105.PercentRange:
                    return fillPrice + (_orLow - _shortTarget) * (GetShortSLRangePct() / 100.0);

                case NQORStopMethod105.SwingPoint:
                {
                    double swHigh = LastSwingHigh(150);
                    return swHigh > 0 ? swHigh + TickSize : fillPrice + GetShortFixedSLPts();
                }

                case NQORStopMethod105.FVGCandle:
                    return _shortFVGFound
                        ? _shortFVGCandleHigh + TickSize
                        : fillPrice + GetShortFixedSLPts();

                default:
                    return fillPrice + GetShortFixedSLPts();
            }
        }

        // =====================================================================
        //  SWING INDICATOR HELPERS
        // =====================================================================

        private double LastSwingLow(int lookbackBars)
        {
            var ind = GetLongSwingInd();
            if (ind == null) return 0;
            int n = Math.Min(lookbackBars, CurrentBar);
            for (int i = 0; i < n; i++)
            {
                double v = ind.SwingLow[i];
                if (v > 0 && !double.IsNaN(v)) return v;
            }
            return 0;
        }

        private double LastSwingHigh(int lookbackBars)
        {
            var ind = GetShortSwingInd();
            if (ind == null) return 0;
            int n = Math.Min(lookbackBars, CurrentBar);
            for (int i = 0; i < n; i++)
            {
                double v = ind.SwingHigh[i];
                if (v > 0 && !double.IsNaN(v)) return v;
            }
            return 0;
        }

        // =====================================================================
        //  DRAWING
        // =====================================================================

        private void DrawSessionLines()
        {
            DateTime t1 = _orDate.Add(new TimeSpan(9, 30, 0));
            DateTime t2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));

            Draw.Line(this, "OR_H", false, t1, _orHigh, t2, _orHigh, Brushes.Magenta,    DashStyleHelper.Solid, 2);
            Draw.Line(this, "OR_L", false, t1, _orLow,  t2, _orLow,  Brushes.Magenta,    DashStyleHelper.Solid, 2);

            if (_longORValid)
            {
                Draw.Line(this, "LT",  false, t1, _longTarget,  t2, _longTarget,  Brushes.DodgerBlue, DashStyleHelper.Solid, 2);
                Draw.Line(this, "LTr", false, t1, _longTrigger, t2, _longTrigger, Brushes.Orange,     DashStyleHelper.Dash,  2);
            }
            if (_shortORValid)
            {
                Draw.Line(this, "ST",  false, t1, _shortTarget,  t2, _shortTarget,  Brushes.DodgerBlue, DashStyleHelper.Solid, 2);
                Draw.Line(this, "STr", false, t1, _shortTrigger, t2, _shortTrigger, Brushes.Orange,     DashStyleHelper.Dash,  2);
            }
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
            if (o.OrderState == OrderState.Working ||
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
            _orSet             = false;
            _longORValid       = false;
            _shortORValid      = false;
            _longActiveBucket  = 0;
            _shortActiveBucket = 0;

            // Levels
            _longTarget = _shortTarget = 0;
            _longTrigger = _shortTrigger = 0;

            // Long state
            _longBreakout    = false;
            _longTriggerHit  = false;
            _longEntryArmed  = false;
            _longInTrade     = false;
            _longFVGPending  = false;
            _longLimitOrder  = null;

            // Short state
            _shortBreakout   = false;
            _shortTriggerHit = false;
            _shortEntryArmed = false;
            _shortInTrade    = false;
            _shortFVGPending = false;
            _shortLimitOrder = null;

            // FVG data
            _longFVGFound  = _shortFVGFound = false;
            _longFVGBot    = _longFVGTop    = _longFVGMid    = _longFVGCandleLow    = 0;
            _shortFVGBot   = _shortFVGTop   = _shortFVGMid   = _shortFVGCandleHigh  = 0;

            // Pending OCO
            _applyLongOCO  = _applyShortOCO  = false;
            _pendingLongSL = _pendingShortSL  = 0;
            _pendingLongTP = _pendingShortTP  = 0;

            // Re-arm
            _longRearmNeeded = _shortRearmNeeded = false;

            // Break-even
            _longEntryPrice  = _shortEntryPrice  = 0;
            _longBEMoved     = _shortBEMoved     = false;
            _longBEPrice     = _shortBEPrice     = 0;
            _applyLongBE     = _applyShortBE     = false;
            _pendingLongBESL = _pendingShortBESL = 0;

            // Per-direction session counters
            _longTradesToday      = 0;
            _longProfitableToday  = 0;
            _longLosingToday      = 0;
            _shortTradesToday     = 0;
            _shortProfitableToday = 0;
            _shortLosingToday     = 0;

            // Shared
            _lastWinDirection = 0;
            _forceFlatDone    = false;
        }

        // =====================================================================
        //  PROPERTIES  –  NinjaTrader parameter UI
        // =====================================================================
        #region Properties

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 01  –  COMMON : TIME FILTERS
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(9, 17)]
        [Display(Name = "No New Trades After – Hour (EST)",
                 GroupName = "01 - Common: Time Filters", Order = 1)]
        public int NoNewTradesHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "No New Trades After – Minute",
                 GroupName = "01 - Common: Time Filters", Order = 2)]
        public int NoNewTradesMin { get; set; }

        [NinjaScriptProperty]
        [Range(9, 18)]
        [Display(Name = "Force Flatten – Hour (EST)",
                 GroupName = "01 - Common: Time Filters", Order = 3)]
        public int ForceFlatHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Force Flatten – Minute",
                 GroupName = "01 - Common: Time Filters", Order = 4)]
        public int ForceFlatMin { get; set; }

        [NinjaScriptProperty]
        [Range(9, 23)]
        [Display(Name = "Session Lines End – Hour (EST)",
                 Description = "Visual end time of OR / Target / Trigger lines",
                 GroupName = "01 - Common: Time Filters", Order = 5)]
        public int SessionEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Session Lines End – Minute",
                 GroupName = "01 - Common: Time Filters", Order = 6)]
        public int SessionEndMin { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 02  –  COMMON : SESSION FILTERS
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Alternating Direction",
                 Description = "After a WIN on either side, only allow the opposite direction next",
                 GroupName = "02 - Common: Session Filters", Order = 1)]
        public bool AlternatingEnabled { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 03  –  LONG BUCKET 1 : OPENING RANGE
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Enabled",
                 Description = "Enable long trades when OR falls within Bucket 1 size range",
                 GroupName = "03 - Long B1: Opening Range", Order = 1)]
        public bool LongB1Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Min OR Size (pts)",
                 GroupName = "03 - Long B1: Opening Range", Order = 2)]
        public double LongB1MinORSize { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max OR Size (pts)",
                 GroupName = "03 - Long B1: Opening Range", Order = 3)]
        public double LongB1MaxORSize { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 20.0)]
        [Display(Name = "OR Multiplier",
                 Description = "Long Target = OR High + Multiplier × OR Range",
                 GroupName = "03 - Long B1: Opening Range", Order = 4)]
        public double LongB1ORMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Trigger Offset (pts)",
                 Description = "Distance from Long Target to Long Trigger line",
                 GroupName = "03 - Long B1: Opening Range", Order = 5)]
        public double LongB1TriggerOffset { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 04  –  LONG BUCKET 1 : TRADE MANAGEMENT
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts",
                 GroupName = "04 - Long B1: Trade Management", Order = 1)]
        public int LongB1Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Trades Per Day",
                 GroupName = "04 - Long B1: Trade Management", Order = 2)]
        public int LongB1MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Profitable Trades / Session",
                 GroupName = "04 - Long B1: Trade Management", Order = 3)]
        public int LongB1MaxProfitableTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Losing Trades / Session",
                 GroupName = "04 - Long B1: Trade Management", Order = 4)]
        public int LongB1MaxLosingTrades { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 05  –  LONG BUCKET 1 : ENTRY METHOD
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Entry Method",
                 GroupName = "05 - Long B1: Entry Method", Order = 1)]
        public NQOREntryMethod105 LongB1EntryMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "Retracement % (method ii)",
                 Description = "Long limit = Target minus X% of (Target - OR High)",
                 GroupName = "05 - Long B1: Entry Method", Order = 2)]
        public double LongB1RetracePct { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Min FVG Size pts (method iii)",
                 GroupName = "05 - Long B1: Entry Method", Order = 3)]
        public double LongB1MinFVGSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG Entry Point (method iii)",
                 GroupName = "05 - Long B1: Entry Method", Order = 4)]
        public NQORFVGEntry105 LongB1FVGEntry { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 06  –  LONG BUCKET 1 : STOP LOSS
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Method",
                 GroupName = "06 - Long B1: Stop Loss", Order = 1)]
        public NQORStopMethod105 LongB1StopMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Fixed SL Points (method i)",
                 GroupName = "06 - Long B1: Stop Loss", Order = 2)]
        public double LongB1FixedSLPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "SL % of Range (method ii)",
                 Description = "Long SL = Entry - X% × (Target - OR High)",
                 GroupName = "06 - Long B1: Stop Loss", Order = 3)]
        public double LongB1SLRangePct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Swing Strength (method iii)",
                 GroupName = "06 - Long B1: Stop Loss", Order = 4)]
        public int LongB1SwingStrength { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 07  –  LONG BUCKET 1 : BREAK-EVEN
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Break-Even Enabled",
                 GroupName = "07 - Long B1: Break-Even", Order = 1)]
        public bool LongB1BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Method",
                 GroupName = "07 - Long B1: Break-Even", Order = 2)]
        public NQORBEMethod105 LongB1BEMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "BE Trigger – Fixed Pts",
                 GroupName = "07 - Long B1: Break-Even", Order = 3)]
        public double LongB1BETriggerPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "BE Trigger – % of OR Range",
                 GroupName = "07 - Long B1: Break-Even", Order = 4)]
        public double LongB1BETriggerPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "BE Offset Pts",
                 Description = "Long SL placed this many pts ABOVE entry at BE",
                 GroupName = "07 - Long B1: Break-Even", Order = 5)]
        public double LongB1BEOffsetPts { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 08  –  LONG BUCKET 2 : OPENING RANGE
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Enabled",
                 Description = "Enable long trades when OR falls within Bucket 2 size range",
                 GroupName = "08 - Long B2: Opening Range", Order = 1)]
        public bool LongB2Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Min OR Size (pts)",
                 GroupName = "08 - Long B2: Opening Range", Order = 2)]
        public double LongB2MinORSize { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max OR Size (pts)",
                 GroupName = "08 - Long B2: Opening Range", Order = 3)]
        public double LongB2MaxORSize { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 20.0)]
        [Display(Name = "OR Multiplier",
                 Description = "Long Target = OR High + Multiplier × OR Range",
                 GroupName = "08 - Long B2: Opening Range", Order = 4)]
        public double LongB2ORMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Trigger Offset (pts)",
                 GroupName = "08 - Long B2: Opening Range", Order = 5)]
        public double LongB2TriggerOffset { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 09  –  LONG BUCKET 2 : TRADE MANAGEMENT
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts",
                 GroupName = "09 - Long B2: Trade Management", Order = 1)]
        public int LongB2Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Trades Per Day",
                 GroupName = "09 - Long B2: Trade Management", Order = 2)]
        public int LongB2MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Profitable Trades / Session",
                 GroupName = "09 - Long B2: Trade Management", Order = 3)]
        public int LongB2MaxProfitableTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Losing Trades / Session",
                 GroupName = "09 - Long B2: Trade Management", Order = 4)]
        public int LongB2MaxLosingTrades { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 10  –  LONG BUCKET 2 : ENTRY METHOD
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Entry Method",
                 GroupName = "10 - Long B2: Entry Method", Order = 1)]
        public NQOREntryMethod105 LongB2EntryMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "Retracement % (method ii)",
                 Description = "Long limit = Target minus X% of (Target - OR High)",
                 GroupName = "10 - Long B2: Entry Method", Order = 2)]
        public double LongB2RetracePct { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Min FVG Size pts (method iii)",
                 GroupName = "10 - Long B2: Entry Method", Order = 3)]
        public double LongB2MinFVGSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG Entry Point (method iii)",
                 GroupName = "10 - Long B2: Entry Method", Order = 4)]
        public NQORFVGEntry105 LongB2FVGEntry { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 11  –  LONG BUCKET 2 : STOP LOSS
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Method",
                 GroupName = "11 - Long B2: Stop Loss", Order = 1)]
        public NQORStopMethod105 LongB2StopMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Fixed SL Points (method i)",
                 GroupName = "11 - Long B2: Stop Loss", Order = 2)]
        public double LongB2FixedSLPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "SL % of Range (method ii)",
                 Description = "Long SL = Entry - X% × (Target - OR High)",
                 GroupName = "11 - Long B2: Stop Loss", Order = 3)]
        public double LongB2SLRangePct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Swing Strength (method iii)",
                 GroupName = "11 - Long B2: Stop Loss", Order = 4)]
        public int LongB2SwingStrength { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 12  –  LONG BUCKET 2 : BREAK-EVEN
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Break-Even Enabled",
                 GroupName = "12 - Long B2: Break-Even", Order = 1)]
        public bool LongB2BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Method",
                 GroupName = "12 - Long B2: Break-Even", Order = 2)]
        public NQORBEMethod105 LongB2BEMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "BE Trigger – Fixed Pts",
                 GroupName = "12 - Long B2: Break-Even", Order = 3)]
        public double LongB2BETriggerPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "BE Trigger – % of OR Range",
                 GroupName = "12 - Long B2: Break-Even", Order = 4)]
        public double LongB2BETriggerPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "BE Offset Pts",
                 Description = "Long SL placed this many pts ABOVE entry at BE",
                 GroupName = "12 - Long B2: Break-Even", Order = 5)]
        public double LongB2BEOffsetPts { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 13  –  LONG BUCKET 3 : OPENING RANGE
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Enabled",
                 Description = "Enable long trades when OR falls within Bucket 3 size range",
                 GroupName = "13 - Long B3: Opening Range", Order = 1)]
        public bool LongB3Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Min OR Size (pts)",
                 GroupName = "13 - Long B3: Opening Range", Order = 2)]
        public double LongB3MinORSize { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max OR Size (pts)",
                 GroupName = "13 - Long B3: Opening Range", Order = 3)]
        public double LongB3MaxORSize { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 20.0)]
        [Display(Name = "OR Multiplier",
                 Description = "Long Target = OR High + Multiplier × OR Range",
                 GroupName = "13 - Long B3: Opening Range", Order = 4)]
        public double LongB3ORMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Trigger Offset (pts)",
                 GroupName = "13 - Long B3: Opening Range", Order = 5)]
        public double LongB3TriggerOffset { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 14  –  LONG BUCKET 3 : TRADE MANAGEMENT
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts",
                 GroupName = "14 - Long B3: Trade Management", Order = 1)]
        public int LongB3Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Trades Per Day",
                 GroupName = "14 - Long B3: Trade Management", Order = 2)]
        public int LongB3MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Profitable Trades / Session",
                 GroupName = "14 - Long B3: Trade Management", Order = 3)]
        public int LongB3MaxProfitableTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Losing Trades / Session",
                 GroupName = "14 - Long B3: Trade Management", Order = 4)]
        public int LongB3MaxLosingTrades { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 15  –  LONG BUCKET 3 : ENTRY METHOD
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Entry Method",
                 GroupName = "15 - Long B3: Entry Method", Order = 1)]
        public NQOREntryMethod105 LongB3EntryMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "Retracement % (method ii)",
                 Description = "Long limit = Target minus X% of (Target - OR High)",
                 GroupName = "15 - Long B3: Entry Method", Order = 2)]
        public double LongB3RetracePct { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Min FVG Size pts (method iii)",
                 GroupName = "15 - Long B3: Entry Method", Order = 3)]
        public double LongB3MinFVGSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG Entry Point (method iii)",
                 GroupName = "15 - Long B3: Entry Method", Order = 4)]
        public NQORFVGEntry105 LongB3FVGEntry { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 16  –  LONG BUCKET 3 : STOP LOSS
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Method",
                 GroupName = "16 - Long B3: Stop Loss", Order = 1)]
        public NQORStopMethod105 LongB3StopMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Fixed SL Points (method i)",
                 GroupName = "16 - Long B3: Stop Loss", Order = 2)]
        public double LongB3FixedSLPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "SL % of Range (method ii)",
                 Description = "Long SL = Entry - X% × (Target - OR High)",
                 GroupName = "16 - Long B3: Stop Loss", Order = 3)]
        public double LongB3SLRangePct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Swing Strength (method iii)",
                 GroupName = "16 - Long B3: Stop Loss", Order = 4)]
        public int LongB3SwingStrength { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 17  –  LONG BUCKET 3 : BREAK-EVEN
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Break-Even Enabled",
                 GroupName = "17 - Long B3: Break-Even", Order = 1)]
        public bool LongB3BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Method",
                 GroupName = "17 - Long B3: Break-Even", Order = 2)]
        public NQORBEMethod105 LongB3BEMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "BE Trigger – Fixed Pts",
                 GroupName = "17 - Long B3: Break-Even", Order = 3)]
        public double LongB3BETriggerPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "BE Trigger – % of OR Range",
                 GroupName = "17 - Long B3: Break-Even", Order = 4)]
        public double LongB3BETriggerPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "BE Offset Pts",
                 Description = "Long SL placed this many pts ABOVE entry at BE",
                 GroupName = "17 - Long B3: Break-Even", Order = 5)]
        public double LongB3BEOffsetPts { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 18  –  SHORT BUCKET 1 : OPENING RANGE
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Enabled",
                 Description = "Enable short trades when OR falls within Bucket 1 size range",
                 GroupName = "18 - Short B1: Opening Range", Order = 1)]
        public bool ShortB1Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Min OR Size (pts)",
                 GroupName = "18 - Short B1: Opening Range", Order = 2)]
        public double ShortB1MinORSize { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max OR Size (pts)",
                 GroupName = "18 - Short B1: Opening Range", Order = 3)]
        public double ShortB1MaxORSize { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 20.0)]
        [Display(Name = "OR Multiplier",
                 Description = "Short Target = OR Low - Multiplier × OR Range",
                 GroupName = "18 - Short B1: Opening Range", Order = 4)]
        public double ShortB1ORMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Trigger Offset (pts)",
                 Description = "Distance from Short Target to Short Trigger line",
                 GroupName = "18 - Short B1: Opening Range", Order = 5)]
        public double ShortB1TriggerOffset { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 19  –  SHORT BUCKET 1 : TRADE MANAGEMENT
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts",
                 GroupName = "19 - Short B1: Trade Management", Order = 1)]
        public int ShortB1Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Trades Per Day",
                 GroupName = "19 - Short B1: Trade Management", Order = 2)]
        public int ShortB1MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Profitable Trades / Session",
                 GroupName = "19 - Short B1: Trade Management", Order = 3)]
        public int ShortB1MaxProfitableTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Losing Trades / Session",
                 GroupName = "19 - Short B1: Trade Management", Order = 4)]
        public int ShortB1MaxLosingTrades { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 20  –  SHORT BUCKET 1 : ENTRY METHOD
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Entry Method",
                 GroupName = "20 - Short B1: Entry Method", Order = 1)]
        public NQOREntryMethod105 ShortB1EntryMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "Retracement % (method ii)",
                 Description = "Short limit = Target plus X% of (OR Low - Target)",
                 GroupName = "20 - Short B1: Entry Method", Order = 2)]
        public double ShortB1RetracePct { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Min FVG Size pts (method iii)",
                 GroupName = "20 - Short B1: Entry Method", Order = 3)]
        public double ShortB1MinFVGSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG Entry Point (method iii)",
                 GroupName = "20 - Short B1: Entry Method", Order = 4)]
        public NQORFVGEntry105 ShortB1FVGEntry { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 21  –  SHORT BUCKET 1 : STOP LOSS
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Method",
                 GroupName = "21 - Short B1: Stop Loss", Order = 1)]
        public NQORStopMethod105 ShortB1StopMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Fixed SL Points (method i)",
                 GroupName = "21 - Short B1: Stop Loss", Order = 2)]
        public double ShortB1FixedSLPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "SL % of Range (method ii)",
                 Description = "Short SL = Entry + X% × (OR Low - Target)",
                 GroupName = "21 - Short B1: Stop Loss", Order = 3)]
        public double ShortB1SLRangePct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Swing Strength (method iii)",
                 GroupName = "21 - Short B1: Stop Loss", Order = 4)]
        public int ShortB1SwingStrength { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 22  –  SHORT BUCKET 1 : BREAK-EVEN
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Break-Even Enabled",
                 GroupName = "22 - Short B1: Break-Even", Order = 1)]
        public bool ShortB1BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Method",
                 GroupName = "22 - Short B1: Break-Even", Order = 2)]
        public NQORBEMethod105 ShortB1BEMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "BE Trigger – Fixed Pts",
                 GroupName = "22 - Short B1: Break-Even", Order = 3)]
        public double ShortB1BETriggerPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "BE Trigger – % of OR Range",
                 GroupName = "22 - Short B1: Break-Even", Order = 4)]
        public double ShortB1BETriggerPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "BE Offset Pts",
                 Description = "Short SL placed this many pts BELOW entry at BE",
                 GroupName = "22 - Short B1: Break-Even", Order = 5)]
        public double ShortB1BEOffsetPts { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 23  –  SHORT BUCKET 2 : OPENING RANGE
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Enabled",
                 Description = "Enable short trades when OR falls within Bucket 2 size range",
                 GroupName = "23 - Short B2: Opening Range", Order = 1)]
        public bool ShortB2Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Min OR Size (pts)",
                 GroupName = "23 - Short B2: Opening Range", Order = 2)]
        public double ShortB2MinORSize { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max OR Size (pts)",
                 GroupName = "23 - Short B2: Opening Range", Order = 3)]
        public double ShortB2MaxORSize { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 20.0)]
        [Display(Name = "OR Multiplier",
                 Description = "Short Target = OR Low - Multiplier × OR Range",
                 GroupName = "23 - Short B2: Opening Range", Order = 4)]
        public double ShortB2ORMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Trigger Offset (pts)",
                 GroupName = "23 - Short B2: Opening Range", Order = 5)]
        public double ShortB2TriggerOffset { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 24  –  SHORT BUCKET 2 : TRADE MANAGEMENT
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts",
                 GroupName = "24 - Short B2: Trade Management", Order = 1)]
        public int ShortB2Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Trades Per Day",
                 GroupName = "24 - Short B2: Trade Management", Order = 2)]
        public int ShortB2MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Profitable Trades / Session",
                 GroupName = "24 - Short B2: Trade Management", Order = 3)]
        public int ShortB2MaxProfitableTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Losing Trades / Session",
                 GroupName = "24 - Short B2: Trade Management", Order = 4)]
        public int ShortB2MaxLosingTrades { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 25  –  SHORT BUCKET 2 : ENTRY METHOD
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Entry Method",
                 GroupName = "25 - Short B2: Entry Method", Order = 1)]
        public NQOREntryMethod105 ShortB2EntryMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "Retracement % (method ii)",
                 Description = "Short limit = Target plus X% of (OR Low - Target)",
                 GroupName = "25 - Short B2: Entry Method", Order = 2)]
        public double ShortB2RetracePct { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Min FVG Size pts (method iii)",
                 GroupName = "25 - Short B2: Entry Method", Order = 3)]
        public double ShortB2MinFVGSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG Entry Point (method iii)",
                 GroupName = "25 - Short B2: Entry Method", Order = 4)]
        public NQORFVGEntry105 ShortB2FVGEntry { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 26  –  SHORT BUCKET 2 : STOP LOSS
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Method",
                 GroupName = "26 - Short B2: Stop Loss", Order = 1)]
        public NQORStopMethod105 ShortB2StopMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Fixed SL Points (method i)",
                 GroupName = "26 - Short B2: Stop Loss", Order = 2)]
        public double ShortB2FixedSLPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "SL % of Range (method ii)",
                 Description = "Short SL = Entry + X% × (OR Low - Target)",
                 GroupName = "26 - Short B2: Stop Loss", Order = 3)]
        public double ShortB2SLRangePct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Swing Strength (method iii)",
                 GroupName = "26 - Short B2: Stop Loss", Order = 4)]
        public int ShortB2SwingStrength { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 27  –  SHORT BUCKET 2 : BREAK-EVEN
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Break-Even Enabled",
                 GroupName = "27 - Short B2: Break-Even", Order = 1)]
        public bool ShortB2BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Method",
                 GroupName = "27 - Short B2: Break-Even", Order = 2)]
        public NQORBEMethod105 ShortB2BEMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "BE Trigger – Fixed Pts",
                 GroupName = "27 - Short B2: Break-Even", Order = 3)]
        public double ShortB2BETriggerPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "BE Trigger – % of OR Range",
                 GroupName = "27 - Short B2: Break-Even", Order = 4)]
        public double ShortB2BETriggerPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "BE Offset Pts",
                 Description = "Short SL placed this many pts BELOW entry at BE",
                 GroupName = "27 - Short B2: Break-Even", Order = 5)]
        public double ShortB2BEOffsetPts { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 28  –  SHORT BUCKET 3 : OPENING RANGE
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Enabled",
                 Description = "Enable short trades when OR falls within Bucket 3 size range",
                 GroupName = "28 - Short B3: Opening Range", Order = 1)]
        public bool ShortB3Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Min OR Size (pts)",
                 GroupName = "28 - Short B3: Opening Range", Order = 2)]
        public double ShortB3MinORSize { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max OR Size (pts)",
                 GroupName = "28 - Short B3: Opening Range", Order = 3)]
        public double ShortB3MaxORSize { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, 20.0)]
        [Display(Name = "OR Multiplier",
                 Description = "Short Target = OR Low - Multiplier × OR Range",
                 GroupName = "28 - Short B3: Opening Range", Order = 4)]
        public double ShortB3ORMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Trigger Offset (pts)",
                 GroupName = "28 - Short B3: Opening Range", Order = 5)]
        public double ShortB3TriggerOffset { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 29  –  SHORT BUCKET 3 : TRADE MANAGEMENT
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts",
                 GroupName = "29 - Short B3: Trade Management", Order = 1)]
        public int ShortB3Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Trades Per Day",
                 GroupName = "29 - Short B3: Trade Management", Order = 2)]
        public int ShortB3MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Profitable Trades / Session",
                 GroupName = "29 - Short B3: Trade Management", Order = 3)]
        public int ShortB3MaxProfitableTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Max Losing Trades / Session",
                 GroupName = "29 - Short B3: Trade Management", Order = 4)]
        public int ShortB3MaxLosingTrades { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 30  –  SHORT BUCKET 3 : ENTRY METHOD
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Entry Method",
                 GroupName = "30 - Short B3: Entry Method", Order = 1)]
        public NQOREntryMethod105 ShortB3EntryMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "Retracement % (method ii)",
                 Description = "Short limit = Target plus X% of (OR Low - Target)",
                 GroupName = "30 - Short B3: Entry Method", Order = 2)]
        public double ShortB3RetracePct { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "Min FVG Size pts (method iii)",
                 GroupName = "30 - Short B3: Entry Method", Order = 3)]
        public double ShortB3MinFVGSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "FVG Entry Point (method iii)",
                 GroupName = "30 - Short B3: Entry Method", Order = 4)]
        public NQORFVGEntry105 ShortB3FVGEntry { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 31  –  SHORT BUCKET 3 : STOP LOSS
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Method",
                 GroupName = "31 - Short B3: Stop Loss", Order = 1)]
        public NQORStopMethod105 ShortB3StopMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "Fixed SL Points (method i)",
                 GroupName = "31 - Short B3: Stop Loss", Order = 2)]
        public double ShortB3FixedSLPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 99.0)]
        [Display(Name = "SL % of Range (method ii)",
                 Description = "Short SL = Entry + X% × (OR Low - Target)",
                 GroupName = "31 - Short B3: Stop Loss", Order = 3)]
        public double ShortB3SLRangePct { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Swing Strength (method iii)",
                 GroupName = "31 - Short B3: Stop Loss", Order = 4)]
        public int ShortB3SwingStrength { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 32  –  SHORT BUCKET 3 : BREAK-EVEN
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Display(Name = "Break-Even Enabled",
                 GroupName = "32 - Short B3: Break-Even", Order = 1)]
        public bool ShortB3BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Method",
                 GroupName = "32 - Short B3: Break-Even", Order = 2)]
        public NQORBEMethod105 ShortB3BEMethod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 2000.0)]
        [Display(Name = "BE Trigger – Fixed Pts",
                 GroupName = "32 - Short B3: Break-Even", Order = 3)]
        public double ShortB3BETriggerPts { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 500.0)]
        [Display(Name = "BE Trigger – % of OR Range",
                 GroupName = "32 - Short B3: Break-Even", Order = 4)]
        public double ShortB3BETriggerPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 100.0)]
        [Display(Name = "BE Offset Pts",
                 Description = "Short SL placed this many pts BELOW entry at BE",
                 GroupName = "32 - Short B3: Break-Even", Order = 5)]
        public double ShortB3BEOffsetPts { get; set; }

        #endregion
    }
}
