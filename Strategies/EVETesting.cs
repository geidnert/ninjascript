#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        public EVETesting()
        {
            VendorLicense(1236);
        }

        private const string StrategySignalPrefix = "EVETesting";
        private const string HeartbeatStrategyName = "EVETesting";
        private const string LongEntrySignal = StrategySignalPrefix + "Long";
        private const string ShortEntrySignal = StrategySignalPrefix + "Short";
        private const int RealtimeTemplateResetTicks = 4000;

    public enum WebhookProvider
    {
        TradersPost,
        ProjectX
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
        public string Description { get; set; }
        public string SymbolId { get; set; }
        public bool ActiveContract { get; set; }
    }

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
        private bool     _wasInSkipWindow;
        private bool     _wasInNewsSkipWindow;
        private bool     _longTriggerHitDuringSkip;
        private bool     _shortTriggerHitDuringSkip;
        private bool     isConfiguredTimeframeValid = true;
        private bool     isConfiguredInstrumentValid = true;
        private bool     timeframePopupShown;
        private bool     instrumentPopupShown;
        private bool     maxAccountLimitHit;
        private string   projectXSessionToken;
        private DateTime projectXTokenAcquiredUtc = Core.Globals.MinDate;
        private List<ProjectXAccountInfo> projectXAccounts;
        private readonly Dictionary<string, long> projectXLastOrderIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private string   projectXResolvedContractId;
        private string   projectXResolvedInstrumentKey = string.Empty;
        private double   projectXLastSyncedStopPrice;
        private double   projectXLastSyncedTargetPrice;
        private StrategyHeartbeatReporter heartbeatReporter;
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

        // ── News + infobox overlay ─────────────────────────────────────────────
        private static readonly string NewsDatesRaw =
@"2025-01-08,14:00
2025-01-29,14:00
2025-02-19,14:00
2025-03-19,14:00
2025-04-09,14:00
2025-05-07,14:00
2025-05-28,14:00
2025-06-18,14:00
2025-07-09,14:00
2025-07-30,14:00
2025-08-20,14:00
2025-09-17,14:00
2025-10-08,14:00
2025-10-29,14:00
2025-11-19,14:00
2025-12-10,14:00
2025-12-30,14:00
2026-01-28,14:00
2026-02-18,14:00
2026-03-18,14:00
2026-04-08,14:00
2026-04-29,14:00
2026-05-20,14:00
2026-06-17,14:00
2026-07-08,14:00
2026-07-29,14:00
2026-08-19,14:00
2026-09-16,14:00
2026-10-07,14:00
2026-10-28,14:00
2026-11-18,14:00
2026-12-09,14:00
2026-12-30,14:00";
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
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(255, 0xFF, 0x00, 0xFF);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);
        private static readonly Brush PassedNewsRowBrush = CreateFrozenBrush(30, 211, 211, 211);
        private static readonly FontFamily InfoEmojiFontFamily = new FontFamily("Segoe UI Emoji");
        private static readonly FontFamily InfoSymbolFontFamily = new FontFamily("Segoe UI Symbol");
        private static readonly HashSet<string> InfoEmojiTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "✔", "✅", "❌", "✖", "⛔", "🚫", "⬜", "🕒"
        };
        private static readonly HashSet<string> InfoSymbolTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "■", "□", "●", "○", "▲", "▼", "◆", "◇"
        };

        private enum InfoValueRunKind
        {
            Default,
            Emoji,
            Symbol
        }

        // =====================================================================
        //  BUCKET PARAMETER HELPERS  (resolve active bucket → correct property)
        // =====================================================================

        // ── Long getters ──────────────────────────────────────────────────────
        private int    GetLongContracts()            { return CommonContracts; }
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
        private double GetLongMaxSLPts()             { switch (_longActiveBucket) { case 1: return LongB1MaxSLPts;            case 2: return LongB2MaxSLPts;            default: return LongB3MaxSLPts;            } }

        // ── Short getters ─────────────────────────────────────────────────────
        private int    GetShortContracts()           { return CommonContracts; }
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
        private double GetShortMaxSLPts()            { switch (_shortActiveBucket) { case 1: return ShortB1MaxSLPts;           case 2: return ShortB2MaxSLPts;           default: return ShortB3MaxSLPts;           } }

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
                Description = "EVETesting – Based on EVETesting-1008. Adds AGGRESSIVE MODE / CALM MODE toggle: CALM MODE skips Section K and lets limit orders sit until filled, session end, or skip window.";
                Description = "EVETesting v8 – v6: Re-arm logic hardened. v7: Per-bucket Max SL cap (all methods except Fixed).";
                Calculate   = Calculate.OnBarClose;

                EntriesPerDirection = 10;
                EntryHandling       = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;

                // ── Shared: Time Filters ───────────────────────────────────────
                NoNewTradesHour = 14;
                NoNewTradesMin  = 36;
                SkipStartHour   = 12;
                SkipStartMin    = 0;
                SkipEndHour     = 13;
                SkipEndMin      = 30;
                ForceFlatHour   = 15;
                ForceFlatMin    = 57;
                SessionEndHour  = 17;
                SessionEndMin   = 0;

                // ── Shared: Session Filters ────────────────────────────────────
                CommonContracts   = 5;
                AlternatingEnabled  = false;
                ReEntryEnabled      = true;
                EnableTargetCancel  = true;   // ON = AGGRESSIVE MODE by default; OFF = CALM MODE
                RequireEntryConfirmation = false;
                DebugLogging = false;
                ShowORLines = true;
                UseSkipTime = true;
                CloseAtSkipStart = false;
                CloseAtNewsStart = false;
                UseNewsSkip = false;
                NewsBlockMinutes = 1;
                MaxAccountBalance = 0.0;
                WebhookUrl = string.Empty;
                WebhookTickerOverride = string.Empty;
                WebhookProviderType = WebhookProvider.TradersPost;
                ProjectXApiBaseUrl = "https://api.topstepx.com";
                ProjectXUsername = string.Empty;
                ProjectXApiKey = string.Empty;
                ProjectXAccountId = string.Empty;
                ProjectXContractId = string.Empty;
                ProjectXTradeAllAccounts = false;

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
                LongB1MaxSLPts           = 60.0;
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
                LongB2MaxSLPts           = 87.0;
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
                LongB3MaxSLPts           = 60.0;
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
                ShortB1MaxSLPts           = 86.0;
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
                ShortB2MaxSLPts           = 81.0;
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
                ShortB3MaxSLPts           = 114.0;
                ShortB3SwingStrength      = 5;
                ShortB3BEEnabled          = false;
                ShortB3BEMethod           = NQORBEMethod105.FixedPoints;
                ShortB3BETriggerPts       = 41.0;
                ShortB3BETriggerPct       = 42.0;
                ShortB3BEOffsetPts        = 1.5;
            }
            else if (State == State.DataLoaded)
            {
                ValidateRequiredPrimaryTimeframe(30);
                ValidateRequiredPrimaryInstrument();
                heartbeatReporter = new StrategyHeartbeatReporter(
                    HeartbeatStrategyName,
                    System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv"));
                projectXSessionToken = null;
                projectXTokenAcquiredUtc = Core.Globals.MinDate;
                projectXAccounts = null;
                projectXLastOrderIds.Clear();
                projectXResolvedContractId = null;
                projectXResolvedInstrumentKey = string.Empty;
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
                _longB1SwingInd  = Swing(LongB1SwingStrength);
                _longB2SwingInd  = Swing(LongB2SwingStrength);
                _longB3SwingInd  = Swing(LongB3SwingStrength);
                _shortB1SwingInd = Swing(ShortB1SwingStrength);
                _shortB2SwingInd = Swing(ShortB2SwingStrength);
                _shortB3SwingInd = Swing(ShortB3SwingStrength);
                EnsureNewsDatesInitialized();
            }
            else if (State == State.Realtime)
            {
                projectXLastSyncedStopPrice = 0.0;
                projectXLastSyncedTargetPrice = 0.0;
                if (heartbeatReporter != null)
                    heartbeatReporter.Start();
                RunProjectXStartupPreflight();
            }
            else if (State == State.Terminated)
            {
                if (heartbeatReporter != null)
                {
                    heartbeatReporter.Dispose();
                    heartbeatReporter = null;
                }
                DisposeInfoBoxOverlay();
            }
        }

        // =====================================================================
        //  MAIN BAR UPDATE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (CurrentBar < 3)      return;
            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
            {
                CancelPendingEntriesForInvalidConfiguration();
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(BuildExitSignalName("InvalidConfigurationL"), GetOpenLongEntrySignal());
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(BuildExitSignalName("InvalidConfigurationS"), GetOpenShortEntrySignal());
                return;
            }

            DrawSessionTimeWindows();
            UpdateInfoText();

            // ── A. Daily reset ────────────────────────────────────────────────
            if (Time[0].Date != _lastResetDate)
            {
                if (_lastResetDate != DateTime.MinValue)
                    CancelPendingEntriesForSessionBoundary("Session reset");
                _lastResetDate = Time[0].Date;
                ResetSession();
            }

            if (State == State.Realtime && Position.MarketPosition == MarketPosition.Flat)
                ResetManagedProtectiveTemplatesForRealtime();

            // ── B. Apply pending OCO brackets ─────────────────────────────────
            if (_applyLongOCO)
            {
                TryApplyPendingProtectiveOrders(
                    LongEntrySignal,
                    MarketPosition.Long,
                    _longEntryPrice > 0 ? _longEntryPrice : Close[0],
                    ref _pendingLongSL,
                    ref _pendingLongTP,
                    "Long");
                _applyLongOCO = false;
            }
            if (_applyShortOCO)
            {
                TryApplyPendingProtectiveOrders(
                    ShortEntrySignal,
                    MarketPosition.Short,
                    _shortEntryPrice > 0 ? _shortEntryPrice : Close[0],
                    ref _pendingShortSL,
                    ref _pendingShortTP,
                    "Short");
                _applyShortOCO = false;
            }

            // ── B2. Apply pending Break-Even SL updates ───────────────────────
            if (_applyLongBE)
            {
                TryApplyPendingBreakEvenStop(
                    LongEntrySignal,
                    MarketPosition.Long,
                    _longEntryPrice,
                    ref _pendingLongBESL,
                    "Long");
                _applyLongBE = false;
                LogDebug(Time[0] + " | ✦ Long SL moved to BE @ " + _pendingLongBESL);
            }
            if (_applyShortBE)
            {
                TryApplyPendingBreakEvenStop(
                    ShortEntrySignal,
                    MarketPosition.Short,
                    _shortEntryPrice,
                    ref _pendingShortBESL,
                    "Short");
                _applyShortBE = false;
                LogDebug(Time[0] + " | ✦ Short SL moved to BE @ " + _pendingShortBESL);
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

            if (IsAccountBalanceBlocked())
                return;

            bool inSkipWindow = IsInSkipWindow(Time[0]);
            HandleSkipWindowTransition(inSkipWindow);

            bool inNewsSkipWindow = IsInNewsSkipWindow(Time[0]);
            HandleNewsSkipWindowTransition(inNewsSkipWindow);
            if (inNewsSkipWindow) return;

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
                        LogDebug(Time[0] + " | OR size " + range + " pts – no matching bucket for either direction. Session skipped.");
                        _forceFlatDone = true;
                        return;
                    }

                    if (!_longORValid)
                        LogDebug(Time[0] + " | OR size " + range + " pts – no matching Long bucket. Long side skipped.");
                    if (!_shortORValid)
                        LogDebug(Time[0] + " | OR size " + range + " pts – no matching Short bucket. Short side skipped.");

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

                    LogDebug(Time[0] + " | ═══ OR captured ═══"
                          + "  H=" + _orHigh + "  L=" + _orLow + "  Range=" + _orRange
                          + (_longORValid  ? " | Long  Bucket=" + _longActiveBucket  + "  Target=" + _longTarget  + "  Trigger=" + _longTrigger  : " | Long  skipped")
                          + (_shortORValid ? " | Short Bucket=" + _shortActiveBucket + "  Target=" + _shortTarget + "  Trigger=" + _shortTrigger : " | Short skipped"));
                }
                return;
            }

            // ── E. Trading permission gate (per direction) ────────────────────
            var noNewTime = new TimeSpan(NoNewTradesHour, NoNewTradesMin, 0);
            bool isLastBarOfSession = IsLastBarOfSession();
            if (isLastBarOfSession)
                CancelPendingEntriesForSessionBoundary("Session end");
            bool canTradeLong  = _longORValid
                              && !inSkipWindow
                              && !inNewsSkipWindow
                              && !isLastBarOfSession
                              && tod < noNewTime
                              && _longTradesToday     < GetLongMaxTradesPerDay()
                              && _longProfitableToday < GetLongMaxProfitableTrades()
                              && _longLosingToday     < GetLongMaxLosingTrades();

            bool canTradeShort = _shortORValid
                              && !inSkipWindow
                              && !inNewsSkipWindow
                              && !isLastBarOfSession
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
                if (inSkipWindow)
                    _longTriggerHitDuringSkip = true;
                LogDebug(Time[0] + " | Long trigger crossed @ " + _longTrigger + "  [Bucket " + _longActiveBucket + "]");
                if (canTradeLong && !_longInTrade && !_shortInTrade)
                    TryArmLong();
            }

            if (_shortBreakout && !_shortTriggerHit && Low[0] <= _shortTrigger)
            {
                _shortTriggerHit = true;
                if (inSkipWindow)
                    _shortTriggerHitDuringSkip = true;
                LogDebug(Time[0] + " | Short trigger crossed @ " + _shortTrigger + "  [Bucket " + _shortActiveBucket + "]");
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
            // v6: Three-layer guard:
            //   1) ReEntryEnabled toggle must be on
            //   2) Price must close back through OR in breakout direction
            //      (Long: Close > OR High | Short: Close < OR Low)
            //   3) Flag only cleared AFTER confirmed arm – if TryArm* is blocked
            //      by price guard it stays true and retries on subsequent bars
            if (_longRearmNeeded && canTradeLong && !_longInTrade && !_longEntryArmed && !_shortInTrade)
            {
                if (Close[0] > _orHigh)
                {
                    LogDebug(Time[0] + " | Long re-arm condition met – price closed above OR High ("
                          + _orHigh + ")  [B" + _longActiveBucket + "] – attempting arm...");
                    TryArmLong();
                    if (_longEntryArmed)
                    {
                        _longRearmNeeded = false;
                        LogDebug(Time[0] + " | Long re-arm SUCCESS  [B" + _longActiveBucket + "]");
                    }
                    else
                    {
                        LogDebug(Time[0] + " | Long re-arm deferred – TryArmLong blocked (price guard)  [B" + _longActiveBucket + "]");
                    }
                }
            }
            if (_shortRearmNeeded && canTradeShort && !_shortInTrade && !_shortEntryArmed && !_longInTrade)
            {
                if (Close[0] < _orLow)
                {
                    LogDebug(Time[0] + " | Short re-arm condition met – price closed below OR Low ("
                          + _orLow + ")  [B" + _shortActiveBucket + "] – attempting arm...");
                    TryArmShort();
                    if (_shortEntryArmed)
                    {
                        _shortRearmNeeded = false;
                        LogDebug(Time[0] + " | Short re-arm SUCCESS  [B" + _shortActiveBucket + "]");
                    }
                    else
                    {
                        LogDebug(Time[0] + " | Short re-arm deferred – TryArmShort blocked (price guard)  [B" + _shortActiveBucket + "]");
                    }
                }
            }

            // ── K. Cancel limit orders if price already reached target ────────
            // Fix2: gated by EnableTargetCancel. When OFF, orders sit until
            // filled, session end, or skip window – no cancel/re-arm churn.
            if (EnableTargetCancel)
            {
                if (_longEntryArmed && _longLimitOrder != null && High[0] >= _longTarget)
                {
                    TryCancelOrder(_longLimitOrder);
                    _longEntryArmed = false;
                    _longFVGPending = false;
                    _longTriggerHit = false;
                    LogDebug(Time[0] + " | Long limit cancelled – target already reached");
                }
                if (_shortEntryArmed && _shortLimitOrder != null && Low[0] <= _shortTarget)
                {
                    TryCancelOrder(_shortLimitOrder);
                    _shortEntryArmed = false;
                    _shortFVGPending = false;
                    _shortTriggerHit = false;
                    LogDebug(Time[0] + " | Short limit cancelled – target already reached");
                }
            }

        }

        // =====================================================================
        //  ARMING HELPERS
        // =====================================================================

        private void TryArmLong()
        {
            if (_longEntryArmed)                                      return;
            if (IsLastBarOfSession())                                 return;
            if (_longTradesToday     >= GetLongMaxTradesPerDay())      return;
            if (_longProfitableToday >= GetLongMaxProfitableTrades())  return;
            if (_longLosingToday     >= GetLongMaxLosingTrades())      return;

            if (AlternatingEnabled && _lastWinDirection == 1)
            {
                LogDebug(Time[0] + " | Long blocked – alternating rule (last win was Long)");
                return;
            }

            switch (GetLongEntryMethod())
            {
                case NQOREntryMethod105.MarketOnTrigger:
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", Close[0], GetLongContracts()))
                    {
                        LogDebug(Time[0] + " | Long entry confirmation declined.");
                        return;
                    }
                    _pendingLongSL = CalcLongSL(Close[0]);
                    _pendingLongTP = _longTarget;
                    _applyLongOCO  = true;
                    EnterLong(GetLongContracts(), LongEntrySignal);
                    _longEntryArmed = true;
                    LogDebug(Time[0] + " | Long MARKET order submitted  [B" + _longActiveBucket + "]");
                    break;

                case NQOREntryMethod105.RetracementLimit:
                {
                    double retDist  = (_longTarget - _orHigh) * (GetLongRetracePct() / 100.0);
                    double limitPx  = _longTarget - retDist;

                    if (!CanPlaceLongLimitSafely(limitPx, "Long re-arm"))
                        return;
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", limitPx, GetLongContracts()))
                    {
                        LogDebug(Time[0] + " | Long entry confirmation declined.");
                        return;
                    }

                    _longLimitOrder = EnterLongLimit(0, true, GetLongContracts(), limitPx, LongEntrySignal);
                    _longEntryArmed = true;
                    LogDebug(Time[0] + " | Long RETRACEMENT limit @ " + limitPx
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
                        LogDebug(Time[0] + " | Long FVG armed – scanning for FVG...  [B" + _longActiveBucket + "]");
                    }
                    break;
            }
        }

        private void TryArmShort()
        {
            if (_shortEntryArmed)                                       return;
            if (IsLastBarOfSession())                                   return;
            if (_shortTradesToday     >= GetShortMaxTradesPerDay())      return;
            if (_shortProfitableToday >= GetShortMaxProfitableTrades())  return;
            if (_shortLosingToday     >= GetShortMaxLosingTrades())      return;

            if (AlternatingEnabled && _lastWinDirection == 2)
            {
                LogDebug(Time[0] + " | Short blocked – alternating rule (last win was Short)");
                return;
            }

            switch (GetShortEntryMethod())
            {
                case NQOREntryMethod105.MarketOnTrigger:
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", Close[0], GetShortContracts()))
                    {
                        LogDebug(Time[0] + " | Short entry confirmation declined.");
                        return;
                    }
                    _pendingShortSL = CalcShortSL(Close[0]);
                    _pendingShortTP = _shortTarget;
                    _applyShortOCO  = true;
                    EnterShort(GetShortContracts(), ShortEntrySignal);
                    _shortEntryArmed = true;
                    LogDebug(Time[0] + " | Short MARKET order submitted  [B" + _shortActiveBucket + "]");
                    break;

                case NQOREntryMethod105.RetracementLimit:
                {
                    double retDist   = (_orLow - _shortTarget) * (GetShortRetracePct() / 100.0);
                    double limitPx   = _shortTarget + retDist;

                    if (!CanPlaceShortLimitSafely(limitPx, "Short re-arm"))
                        return;
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", limitPx, GetShortContracts()))
                    {
                        LogDebug(Time[0] + " | Short entry confirmation declined.");
                        return;
                    }

                    _shortLimitOrder = EnterShortLimit(0, true, GetShortContracts(), limitPx, ShortEntrySignal);
                    _shortEntryArmed = true;
                    LogDebug(Time[0] + " | Short RETRACEMENT limit @ " + limitPx
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
                        LogDebug(Time[0] + " | Short FVG armed – scanning for FVG...  [B" + _shortActiveBucket + "]");
                    }
                    break;
            }
        }

        private void PlaceLongFVGOrder()
        {
            double px       = LongFVGEntryPrice(_longFVGBot, _longFVGTop, _longFVGMid);
            if (!CanPlaceLongLimitSafely(px, "Long FVG"))
                return;
            if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", px, GetLongContracts()))
            {
                LogDebug(Time[0] + " | Long entry confirmation declined.");
                return;
            }
            _longLimitOrder = EnterLongLimit(0, true, GetLongContracts(), px, LongEntrySignal);
            _longEntryArmed = true;
            LogDebug(Time[0] + " | Long FVG LIMIT @ " + px
                  + "  (gap: " + _longFVGBot + "–" + _longFVGTop + ")"
                  + "  [B" + _longActiveBucket + "]");
        }

        private void PlaceShortFVGOrder()
        {
            double px        = ShortFVGEntryPrice(_shortFVGBot, _shortFVGTop, _shortFVGMid);
            if (!CanPlaceShortLimitSafely(px, "Short FVG"))
                return;
            if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", px, GetShortContracts()))
            {
                LogDebug(Time[0] + " | Short entry confirmation declined.");
                return;
            }
            _shortLimitOrder = EnterShortLimit(0, true, GetShortContracts(), px, ShortEntrySignal);
            _shortEntryArmed = true;
            LogDebug(Time[0] + " | Short FVG LIMIT @ " + px
                  + "  (gap: " + _shortFVGBot + "–" + _shortFVGTop + ")"
                  + "  [B" + _shortActiveBucket + "]");
        }

        // Half-day/session-edge safety: avoid submitting a limit order when price is
        // already on the wrong side, which can cause immediate unintended fills/rejections.
        private bool CanPlaceLongLimitSafely(double limitPrice, string context)
        {
            double referencePrice = GetCurrentAsk();
            if (referencePrice <= 0 || double.IsNaN(referencePrice) || double.IsInfinity(referencePrice))
                referencePrice = Close[0];

            if (referencePrice <= limitPrice)
            {
                LogDebug(Time[0] + " | " + context + " skipped – price " + referencePrice
                      + " already <= limit " + limitPrice
                      + "  [B" + _longActiveBucket + "]");
                return false;
            }

            return true;
        }

        private bool CanPlaceShortLimitSafely(double limitPrice, string context)
        {
            double referencePrice = GetCurrentBid();
            if (referencePrice <= 0 || double.IsNaN(referencePrice) || double.IsInfinity(referencePrice))
                referencePrice = Close[0];

            if (referencePrice >= limitPrice)
            {
                LogDebug(Time[0] + " | " + context + " skipped – price " + referencePrice
                      + " already >= limit " + limitPrice
                      + "  [B" + _shortActiveBucket + "]");
                return false;
            }

            return true;
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
            if (order == null)
                return;

            if (IsLongEntryOrderName(order.Name))
            {
                _longLimitOrder = order;
                if (state == OrderState.Cancelled || state == OrderState.Rejected)
                {
                    _longEntryArmed = false;
                    _longFVGPending = false;
                    projectXLastSyncedStopPrice = 0.0;
                    projectXLastSyncedTargetPrice = 0.0;
                    SendWebhook("cancel");
                }
            }
            if (IsShortEntryOrderName(order.Name))
            {
                _shortLimitOrder = order;
                if (state == OrderState.Cancelled || state == OrderState.Rejected)
                {
                    _shortEntryArmed = false;
                    _shortFVGPending = false;
                    projectXLastSyncedStopPrice = 0.0;
                    projectXLastSyncedTargetPrice = 0.0;
                    SendWebhook("cancel");
                }
            }

            TrySyncProjectXProtectiveOrder(order, limitPrice, stopPrice, state);
        }

        protected override void OnExecutionUpdate(Execution exec, string execId, double price,
            int qty, MarketPosition mp, string orderId, DateTime time)
        {
            if (exec.Order.OrderState != OrderState.Filled) return;
            string orderName = exec.Order != null ? (exec.Order.Name ?? string.Empty) : string.Empty;
            int executionQty = Math.Max(1, qty);

            // ── Long entry filled ─────────────────────────────────────────────
            if (IsLongEntryOrderName(orderName))
            {
                _longInTrade    = true;
                _longEntryArmed = false;
                _longLimitOrder = null;
                _longEntryPrice = price;
                _longBEMoved    = false;
                _longTradesToday++;

                double sl = CalcLongSL(price);
                double tp = _longTarget;
                int targetTicks = Math.Max(0, PriceToTicks(Math.Abs(tp - price)));
                if (TrySanitizeProtectivePrices(MarketPosition.Long, price, sl, targetTicks, out sl, out tp))
                {
                    _pendingLongSL = sl;
                    _pendingLongTP = tp;
                }
                else
                {
                    _pendingLongSL = sl;
                    _pendingLongTP = _longTarget;
                }
                if (State == State.Realtime)
                    _applyLongOCO = !TryApplyPendingProtectiveOrders(
                        LongEntrySignal,
                        MarketPosition.Long,
                        price,
                        ref _pendingLongSL,
                        ref _pendingLongTP,
                        "Long");
                else
                    _applyLongOCO = true;

                DateTime slT1 = time;
                DateTime slT2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
                Draw.Line(this, "LongSL_" + (++_drawSeq), false,
                          slT1, sl, slT2, sl, Brushes.Red, DashStyleHelper.Dash, 2);

                bool longWebhookSent = SendWebhook("buy", price, _pendingLongTP, sl, exec.Order.OrderType == OrderType.Market, executionQty);
                if (WebhookProviderType == WebhookProvider.ProjectX && longWebhookSent)
                {
                    projectXLastSyncedStopPrice = RoundToInstrumentTick(_pendingLongSL);
                    projectXLastSyncedTargetPrice = RoundToInstrumentTick(_pendingLongTP);
                }

                LogDebug(time + " | ► LONG FILLED @ " + price
                      + "  SL=" + sl + "  TP=" + _pendingLongTP
                      + "  [B" + _longActiveBucket + "  trade " + _longTradesToday + "/" + GetLongMaxTradesPerDay() + "]");
            }

            // ── Short entry filled ────────────────────────────────────────────
            else if (IsShortEntryOrderName(orderName))
            {
                _shortInTrade    = true;
                _shortEntryArmed = false;
                _shortLimitOrder = null;
                _shortEntryPrice = price;
                _shortBEMoved    = false;
                _shortTradesToday++;

                double sl = CalcShortSL(price);
                double tp = _shortTarget;
                int targetTicks = Math.Max(0, PriceToTicks(Math.Abs(tp - price)));
                if (TrySanitizeProtectivePrices(MarketPosition.Short, price, sl, targetTicks, out sl, out tp))
                {
                    _pendingShortSL = sl;
                    _pendingShortTP = tp;
                }
                else
                {
                    _pendingShortSL = sl;
                    _pendingShortTP = _shortTarget;
                }
                if (State == State.Realtime)
                    _applyShortOCO = !TryApplyPendingProtectiveOrders(
                        ShortEntrySignal,
                        MarketPosition.Short,
                        price,
                        ref _pendingShortSL,
                        ref _pendingShortTP,
                        "Short");
                else
                    _applyShortOCO = true;

                DateTime slT1 = time;
                DateTime slT2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
                Draw.Line(this, "ShortSL_" + (++_drawSeq), false,
                          slT1, sl, slT2, sl, Brushes.Red, DashStyleHelper.Dash, 2);

                bool shortWebhookSent = SendWebhook("sell", price, _pendingShortTP, sl, exec.Order.OrderType == OrderType.Market, executionQty);
                if (WebhookProviderType == WebhookProvider.ProjectX && shortWebhookSent)
                {
                    projectXLastSyncedStopPrice = RoundToInstrumentTick(_pendingShortSL);
                    projectXLastSyncedTargetPrice = RoundToInstrumentTick(_pendingShortTP);
                }

                LogDebug(time + " | ► SHORT FILLED @ " + price
                      + "  SL=" + sl + "  TP=" + _pendingShortTP
                      + "  [B" + _shortActiveBucket + "  trade " + _shortTradesToday + "/" + GetShortMaxTradesPerDay() + "]");
            }

            if (ShouldSendExitWebhook(exec, orderName, mp))
                SendWebhook("exit", 0, 0, 0, true, executionQty);
        }

        private bool ShouldSendExitWebhook(Execution execution, string orderName, MarketPosition marketPosition)
        {
            if (execution == null || execution.Order == null)
                return false;

            if (IsEntryOrderName(orderName))
                return false;

            return marketPosition == MarketPosition.Flat;
        }

        private bool IsEntryOrderName(string orderName)
        {
            if (string.IsNullOrWhiteSpace(orderName))
                return false;

            return IsLongEntryOrderName(orderName) || IsShortEntryOrderName(orderName);
        }

        private bool IsLongEntryOrderName(string orderName)
        {
            return string.Equals(orderName, LongEntrySignal, StringComparison.Ordinal);
        }

        private bool IsShortEntryOrderName(string orderName)
        {
            return string.Equals(orderName, ShortEntrySignal, StringComparison.Ordinal);
        }

        private string GetOpenLongEntrySignal()
        {
            return LongEntrySignal;
        }

        private string GetOpenShortEntrySignal()
        {
            return ShortEntrySignal;
        }

        private string BuildExitSignalName(string reason)
        {
            return StrategySignalPrefix + reason;
        }

        private void LogDebug(string message)
        {
            if (DebugLogging)
                Print(message);
        }

        private void LogProjectXDiscovery(string message)
        {
            if (WebhookProviderType != WebhookProvider.ProjectX)
                return;

            LogDebug("[ProjectX] " + (message ?? string.Empty));
        }

        private void LogProjectXStatus(string message)
        {
            if (WebhookProviderType != WebhookProvider.ProjectX)
                return;

            Print("[ProjectX] " + (message ?? string.Empty));
        }

        private bool IsProjectXProtectiveOrderActiveState(OrderState orderState)
        {
            return orderState == OrderState.Submitted
                || orderState == OrderState.Accepted
                || orderState == OrderState.Working
                || orderState == OrderState.PartFilled;
        }

        private double RoundToInstrumentTick(double price)
        {
            return Instrument != null && Instrument.MasterInstrument != null
                ? Instrument.MasterInstrument.RoundToTickSize(price)
                : price;
        }

        private bool ArePricesEquivalent(double left, double right)
        {
            if (left <= 0.0 || right <= 0.0)
                return false;

            return Math.Abs(left - right) <= TickSize * 0.5;
        }

        private bool TryGetProjectXProtectiveOrderKind(Order order, out bool isStopOrder)
        {
            isStopOrder = false;
            if (order == null)
                return false;

            string orderName = order.Name ?? string.Empty;
            string fromEntrySignal = order.FromEntrySignal ?? string.Empty;
            bool belongsToManagedEntry = IsEntryOrderName(fromEntrySignal);

            if (!belongsToManagedEntry
                && !orderName.Equals("Stop loss", StringComparison.OrdinalIgnoreCase)
                && !orderName.Equals("Profit target", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (order.OrderType == OrderType.StopMarket
                || order.OrderType == OrderType.StopLimit
                || orderName.Equals("Stop loss", StringComparison.OrdinalIgnoreCase))
            {
                isStopOrder = true;
                return true;
            }

            if (order.OrderType == OrderType.Limit
                || orderName.Equals("Profit target", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private void TrySyncProjectXProtectiveOrder(Order order, double limitPrice, double stopPrice, OrderState orderState)
        {
            if (State != State.Realtime || WebhookProviderType != WebhookProvider.ProjectX)
                return;
            if (order == null || Position.MarketPosition == MarketPosition.Flat)
                return;
            if (!IsProjectXProtectiveOrderActiveState(orderState))
                return;

            bool isStopOrder;
            if (!TryGetProjectXProtectiveOrderKind(order, out isStopOrder))
                return;

            if (isStopOrder)
            {
                double actualStopPrice = stopPrice > 0.0 ? RoundToInstrumentTick(stopPrice) : RoundToInstrumentTick(order.StopPrice);
                if (actualStopPrice <= 0.0 || ArePricesEquivalent(projectXLastSyncedStopPrice, actualStopPrice))
                    return;

                if (SyncProjectXProtectivePrice(actualStopPrice, true))
                    projectXLastSyncedStopPrice = actualStopPrice;
            }
            else
            {
                double actualTargetPrice = limitPrice > 0.0 ? RoundToInstrumentTick(limitPrice) : RoundToInstrumentTick(order.LimitPrice);
                if (actualTargetPrice <= 0.0 || ArePricesEquivalent(projectXLastSyncedTargetPrice, actualTargetPrice))
                    return;

                if (SyncProjectXProtectivePrice(actualTargetPrice, false))
                    projectXLastSyncedTargetPrice = actualTargetPrice;
            }
        }

        private bool SyncProjectXProtectivePrice(double price, bool isStopOrder)
        {
            if (price <= 0.0)
                return false;
            if (!EnsureProjectXSession())
                return false;

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetProjectXTargets(out targetAccounts, out contractId))
                return false;

            bool modifiedAny = false;
            foreach (var account in targetAccounts)
            {
                try
                {
                    if (ProjectXModifyProtectiveOrders(account.Id, contractId, price, isStopOrder))
                        modifiedAny = true;
                }
                catch (Exception ex)
                {
                    LogDebug(string.Format(
                        "[ProjectX] protective sync error | kind={0} accountId={1} contractId={2} price={3:0.00} error={4}",
                        isStopOrder ? "stop" : "target",
                        account.Id,
                        contractId,
                        price,
                        ex.Message));
                }
            }

            return modifiedAny;
        }

        private bool ProjectXModifyProtectiveOrders(int accountId, string contractId, double price, bool isStopOrder)
        {
            string searchJson = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string searchResponse = ProjectXPost("/api/Order/searchOpen", searchJson, true);
            bool success;
            if (TryGetJsonBool(searchResponse, "success", out success) && !success)
                return false;

            int expectedSide = Position.MarketPosition == MarketPosition.Long ? 1 : 0;
            int expectedType = isStopOrder ? 4 : 1;
            bool modifiedAny = false;

            foreach (var order in ExtractProjectXOrders(searchResponse))
            {
                object contractObj;
                if (!order.TryGetValue("contractId", out contractObj))
                    continue;
                if (!string.Equals(contractObj != null ? contractObj.ToString() : string.Empty, contractId, StringComparison.OrdinalIgnoreCase))
                    continue;

                object typeObj;
                int type;
                if (!order.TryGetValue("type", out typeObj) || !TryConvertToInt(typeObj, out type) || type != expectedType)
                    continue;

                object sideObj;
                int side;
                if (!order.TryGetValue("side", out sideObj) || !TryConvertToInt(sideObj, out side) || side != expectedSide)
                    continue;

                object idObj;
                long orderId;
                if (!order.TryGetValue("id", out idObj) || !TryConvertToLong(idObj, out orderId) || orderId <= 0)
                    continue;

                object sizeObj;
                int size;
                if (!order.TryGetValue("size", out sizeObj) || !TryConvertToInt(sizeObj, out size) || size <= 0)
                    size = Math.Max(1, Position.Quantity);

                object existingPriceObj;
                double existingPrice;
                if (isStopOrder)
                {
                    if (order.TryGetValue("stopPrice", out existingPriceObj)
                        && existingPriceObj != null
                        && double.TryParse(existingPriceObj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out existingPrice)
                        && ArePricesEquivalent(RoundToInstrumentTick(existingPrice), price))
                        continue;
                }
                else
                {
                    if (order.TryGetValue("limitPrice", out existingPriceObj)
                        && existingPriceObj != null
                        && double.TryParse(existingPriceObj.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out existingPrice)
                        && ArePricesEquivalent(RoundToInstrumentTick(existingPrice), price))
                        continue;
                }

                string response = ProjectXModifyOrder(accountId, orderId, size, isStopOrder ? (double?)null : price, isStopOrder ? (double?)price : null);
                if (!string.IsNullOrWhiteSpace(response))
                    modifiedAny = true;
            }

            if (!modifiedAny)
            {
                LogDebug(string.Format(
                    "[ProjectX] protective sync skipped | kind={0} accountId={1} contractId={2} price={3:0.00} reason=no-open-order",
                    isStopOrder ? "stop" : "target",
                    accountId,
                    contractId,
                    price));
            }

            return modifiedAny;
        }

        private string ProjectXModifyOrder(int accountId, long orderId, int size, double? limitPrice, double? stopPrice)
        {
            string json = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"orderId\":{1},\"size\":{2},\"limitPrice\":{3},\"stopPrice\":{4},\"trailPrice\":null}}",
                accountId,
                orderId,
                size > 0 ? size.ToString(CultureInfo.InvariantCulture) : "null",
                limitPrice.HasValue ? limitPrice.Value.ToString(CultureInfo.InvariantCulture) : "null",
                stopPrice.HasValue ? stopPrice.Value.ToString(CultureInfo.InvariantCulture) : "null");
            return ProjectXPost("/api/Order/modify", json, true);
        }

        private bool TryApplyPendingProtectiveOrders(string entrySignal, MarketPosition direction, double fillPrice, ref double pendingStop, ref double pendingTarget, string label)
        {
            int targetTicks = pendingTarget > 0 && fillPrice > 0
                ? Math.Max(0, PriceToTicks(Math.Abs(pendingTarget - fillPrice)))
                : 0;

            double stopPrice;
            double targetPrice;
            if (!TrySanitizeProtectivePrices(direction, fillPrice, pendingStop, targetTicks, out stopPrice, out targetPrice))
            {
                LogDebug(Time[0] + " | " + label + " protective order sanitize failed. SL=" + pendingStop + " TP=" + pendingTarget + " fill=" + fillPrice);
                return false;
            }

            if (Math.Abs(stopPrice - pendingStop) >= TickSize || (targetTicks > 0 && Math.Abs(targetPrice - pendingTarget) >= TickSize))
            {
                LogDebug(Time[0] + " | " + label + " protective orders adjusted to live market. SL " + pendingStop + " -> " + stopPrice
                      + (targetTicks > 0 ? " | TP " + pendingTarget + " -> " + targetPrice : string.Empty));
            }

            pendingStop = stopPrice;
            if (targetTicks > 0)
                pendingTarget = targetPrice;

            SetStopLoss(entrySignal, CalculationMode.Price, pendingStop, false);
            if (targetTicks > 0)
                SetProfitTarget(entrySignal, CalculationMode.Price, pendingTarget);

            return true;
        }

        private bool TryApplyPendingBreakEvenStop(string entrySignal, MarketPosition direction, double fillPrice, ref double pendingStop, string label)
        {
            double stopPrice = pendingStop;
            if (!TryFinalSanitizeStopForLiveMarket(direction, fillPrice, ref stopPrice))
            {
                LogDebug(Time[0] + " | " + label + " BE sanitize failed. SL=" + pendingStop + " fill=" + fillPrice);
                return false;
            }

            if (Math.Abs(stopPrice - pendingStop) >= TickSize)
                LogDebug(Time[0] + " | " + label + " BE adjusted to live market. SL " + pendingStop + " -> " + stopPrice);

            pendingStop = stopPrice;
            SetStopLoss(entrySignal, CalculationMode.Price, pendingStop, false);
            return true;
        }

        private bool TrySanitizeStopPriceForCurrentMarket(MarketPosition positionDirection, double rawStopPrice, out double stopPrice)
        {
            stopPrice = 0;
            if (rawStopPrice <= 0 || TickSize <= 0)
                return false;

            double minTick = TickSize;
            double referencePrice = GetReferencePriceForStop(positionDirection);
            if (referencePrice <= 0 || double.IsNaN(referencePrice) || double.IsInfinity(referencePrice))
                referencePrice = Close[0];

            stopPrice = Instrument.MasterInstrument.RoundToTickSize(rawStopPrice);
            if (positionDirection == MarketPosition.Long)
            {
                if (stopPrice >= referencePrice)
                    stopPrice = Instrument.MasterInstrument.RoundToTickSize(referencePrice - minTick);
            }
            else if (positionDirection == MarketPosition.Short)
            {
                if (stopPrice <= referencePrice)
                    stopPrice = Instrument.MasterInstrument.RoundToTickSize(referencePrice + minTick);
            }
            else
            {
                return false;
            }

            return stopPrice > 0 && !double.IsNaN(stopPrice) && !double.IsInfinity(stopPrice);
        }

        private double GetReferencePriceForStop(MarketPosition positionDirection)
        {
            if (positionDirection == MarketPosition.Long)
            {
                double bid = GetCurrentBid();
                if (bid > 0)
                    return bid;
            }
            else if (positionDirection == MarketPosition.Short)
            {
                double ask = GetCurrentAsk();
                if (ask > 0)
                    return ask;
            }

            return Close[0];
        }

        private bool TrySanitizeProtectivePrices(MarketPosition positionDirection, double fillPrice, double rawStopPrice, int rawTargetTicks, out double stopPrice, out double targetPrice)
        {
            stopPrice = 0;
            targetPrice = 0;

            if (fillPrice <= 0 || TickSize <= 0)
                return false;

            if (!TrySanitizeStopPriceForCurrentMarket(positionDirection, rawStopPrice, out stopPrice))
                return false;

            double marketPrice = Close[0];
            double stopReferencePrice = GetReferencePriceForStop(positionDirection);
            if (stopReferencePrice <= 0 || double.IsNaN(stopReferencePrice) || double.IsInfinity(stopReferencePrice))
                stopReferencePrice = marketPrice;
            double minTick = TickSize;
            int targetTicks = Math.Max(0, rawTargetTicks);

            if (positionDirection == MarketPosition.Long)
            {
                if (stopPrice >= fillPrice)
                    stopPrice = Instrument.MasterInstrument.RoundToTickSize(fillPrice - minTick);
                if (stopPrice >= stopReferencePrice)
                    stopPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(fillPrice - minTick, stopReferencePrice - minTick));

                if (targetTicks > 0)
                {
                    targetPrice = Instrument.MasterInstrument.RoundToTickSize(fillPrice + (targetTicks * TickSize));
                    if (targetPrice <= fillPrice)
                        targetPrice = Instrument.MasterInstrument.RoundToTickSize(fillPrice + minTick);
                    if (targetPrice <= marketPrice)
                        targetPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(fillPrice + minTick, marketPrice + minTick));
                }
            }
            else if (positionDirection == MarketPosition.Short)
            {
                if (stopPrice <= fillPrice)
                    stopPrice = Instrument.MasterInstrument.RoundToTickSize(fillPrice + minTick);
                if (stopPrice <= stopReferencePrice)
                    stopPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(fillPrice + minTick, stopReferencePrice + minTick));

                if (targetTicks > 0)
                {
                    targetPrice = Instrument.MasterInstrument.RoundToTickSize(fillPrice - (targetTicks * TickSize));
                    if (targetPrice >= fillPrice)
                        targetPrice = Instrument.MasterInstrument.RoundToTickSize(fillPrice - minTick);
                    if (targetPrice >= marketPrice)
                        targetPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(fillPrice - minTick, marketPrice - minTick));
                }
            }
            else
            {
                return false;
            }

            return true;
        }

        private bool TryFinalSanitizeStopForLiveMarket(MarketPosition positionDirection, double fillPrice, ref double stopPrice)
        {
            if (positionDirection != MarketPosition.Long && positionDirection != MarketPosition.Short)
                return false;

            double minTick = TickSize;
            if (minTick <= 0)
                return false;

            double reference = GetReferencePriceForStop(positionDirection);
            if (reference <= 0 || double.IsNaN(reference) || double.IsInfinity(reference))
                reference = Close[0];

            stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

            if (positionDirection == MarketPosition.Long)
            {
                if (stopPrice >= reference)
                    stopPrice = Instrument.MasterInstrument.RoundToTickSize(reference - minTick);
                if (stopPrice >= fillPrice)
                    stopPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Min(fillPrice - minTick, stopPrice));
            }
            else
            {
                if (stopPrice <= reference)
                    stopPrice = Instrument.MasterInstrument.RoundToTickSize(reference + minTick);
                if (stopPrice <= fillPrice)
                    stopPrice = Instrument.MasterInstrument.RoundToTickSize(Math.Max(fillPrice + minTick, stopPrice));
            }

            return stopPrice > 0 && !double.IsNaN(stopPrice) && !double.IsInfinity(stopPrice);
        }

        protected override void OnPositionUpdate(Position pos, double avgPx,
            int qty, MarketPosition mp)
        {
            if (mp != MarketPosition.Flat) return;

            projectXLastSyncedStopPrice = 0.0;
            projectXLastSyncedTargetPrice = 0.0;

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
                    LogDebug(Time[0] + " | Long closed – SCRATCH (BE stop)");
                }
                else if (pnl > 0)
                {
                    _longProfitableToday++;
                    _lastWinDirection = 1;
                    LogDebug(Time[0] + " | Long closed – WIN  (#" + _longProfitableToday + " profitable today)");
                }
                else
                {
                    _longLosingToday++;
                    LogDebug(Time[0] + " | Long closed – LOSS (#" + _longLosingToday + " losses today)");
                }

                _longBEMoved    = false;
                _longEntryPrice = 0;

                bool canRearm = ReEntryEnabled
                             && !isScratch
                             && _longTriggerHit
                             && !_forceFlatDone
                             && _longTradesToday     < GetLongMaxTradesPerDay()
                             && _longProfitableToday < GetLongMaxProfitableTrades()
                             && _longLosingToday     < GetLongMaxLosingTrades()
                             && !(AlternatingEnabled && _lastWinDirection == 1);

                _longRearmNeeded = canRearm;
                if (isScratch)
                    LogDebug(Time[0] + " | Long re-arm blocked – BE scratch, no re-entry");
                else
                    LogDebug(Time[0] + " | Long re-arm=" + _longRearmNeeded);
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
                    LogDebug(Time[0] + " | Short closed – SCRATCH (BE stop)");
                }
                else if (pnl > 0)
                {
                    _shortProfitableToday++;
                    _lastWinDirection = 2;
                    LogDebug(Time[0] + " | Short closed – WIN  (#" + _shortProfitableToday + " profitable today)");
                }
                else
                {
                    _shortLosingToday++;
                    LogDebug(Time[0] + " | Short closed – LOSS (#" + _shortLosingToday + " losses today)");
                }

                _shortBEMoved    = false;
                _shortEntryPrice = 0;

                bool canRearm = ReEntryEnabled
                             && !isScratch
                             && _shortTriggerHit
                             && !_forceFlatDone
                             && _shortTradesToday     < GetShortMaxTradesPerDay()
                             && _shortProfitableToday < GetShortMaxProfitableTrades()
                             && _shortLosingToday     < GetShortMaxLosingTrades()
                             && !(AlternatingEnabled && _lastWinDirection == 2);

                _shortRearmNeeded = canRearm;
                if (isScratch)
                    LogDebug(Time[0] + " | Short re-arm blocked – BE scratch, no re-entry");
                else
                    LogDebug(Time[0] + " | Short re-arm=" + _shortRearmNeeded);
            }
        }

        // =====================================================================
        //  STOP LOSS CALCULATORS
        // =====================================================================

        private double CalcLongSL(double fillPrice)
        {
            double sl;
            switch (GetLongStopMethod())
            {
                case NQORStopMethod105.FixedPoints:
                    return fillPrice - GetLongFixedSLPts();   // Fixed: no cap applied

                case NQORStopMethod105.PercentRange:
                    sl = fillPrice - (_longTarget - _orHigh) * (GetLongSLRangePct() / 100.0);
                    break;

                case NQORStopMethod105.SwingPoint:
                {
                    double swLow = LastSwingLow(150);
                    sl = swLow > 0 ? swLow - TickSize : fillPrice - GetLongFixedSLPts();
                    break;
                }

                case NQORStopMethod105.FVGCandle:
                    sl = _longFVGFound
                        ? _longFVGCandleLow - TickSize
                        : fillPrice - GetLongFixedSLPts();
                    break;

                default:
                    sl = fillPrice - GetLongFixedSLPts();
                    break;
            }

            // ── Apply per-bucket Max SL cap (non-Fixed methods only) ──────────
            double maxSL = fillPrice - GetLongMaxSLPts();
            if (sl < maxSL)
            {
                LogDebug("CalcLongSL | SL capped from " + sl + " to " + maxSL
                      + " (MaxSLPts=" + GetLongMaxSLPts() + ")  [B" + _longActiveBucket + "]");
                sl = maxSL;
            }
            return sl;
        }

        private double CalcShortSL(double fillPrice)
        {
            double sl;
            switch (GetShortStopMethod())
            {
                case NQORStopMethod105.FixedPoints:
                    return fillPrice + GetShortFixedSLPts();  // Fixed: no cap applied

                case NQORStopMethod105.PercentRange:
                    sl = fillPrice + (_orLow - _shortTarget) * (GetShortSLRangePct() / 100.0);
                    break;

                case NQORStopMethod105.SwingPoint:
                {
                    double swHigh = LastSwingHigh(150);
                    sl = swHigh > 0 ? swHigh + TickSize : fillPrice + GetShortFixedSLPts();
                    break;
                }

                case NQORStopMethod105.FVGCandle:
                    sl = _shortFVGFound
                        ? _shortFVGCandleHigh + TickSize
                        : fillPrice + GetShortFixedSLPts();
                    break;

                default:
                    sl = fillPrice + GetShortFixedSLPts();
                    break;
            }

            // ── Apply per-bucket Max SL cap (non-Fixed methods only) ──────────
            double maxSL = fillPrice + GetShortMaxSLPts();
            if (sl > maxSL)
            {
                LogDebug("CalcShortSL | SL capped from " + sl + " to " + maxSL
                      + " (MaxSLPts=" + GetShortMaxSLPts() + ")  [B" + _shortActiveBucket + "]");
                sl = maxSL;
            }
            return sl;
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

        private void DrawSessionTimeWindows()
        {
            if (CurrentBar < 1)
                return;

            DrawSessionBackground(Time[0]);
            DrawNoNewTradesLine(Time[0]);
            DrawSkipWindow(Time[0]);
            DrawNewsWindows(Time[0]);
        }

        private void DrawSessionBackground(DateTime barTime)
        {
            DateTime sessionStart = barTime.Date.Add(new TimeSpan(9, 30, 0));
            DateTime sessionEnd = barTime.Date.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));
            if (sessionEnd <= sessionStart)
                sessionEnd = sessionEnd.AddDays(1);

            string rectTag = string.Format("EVE_SessionFill_{0:yyyyMMdd_HHmm}", sessionStart);
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
                    Brushes.Gold,
                    10).ZOrder = -1;
            }
        }

        private void DrawNoNewTradesLine(DateTime barTime)
        {
            DateTime lineTime = barTime.Date.Add(new TimeSpan(NoNewTradesHour, NoNewTradesMin, 0));
            string tag = string.Format("EVE_NoNewTrades_{0:yyyyMMdd_HHmm}", lineTime);
            Draw.VerticalLine(this, tag, lineTime, Brushes.Red, DashStyleHelper.DashDot, 2);
        }

        private void DrawSkipWindow(DateTime barTime)
        {
            if (!IsSkipWindowConfigured())
                return;

            DateTime windowStart = barTime.Date.Add(new TimeSpan(SkipStartHour, SkipStartMin, 0));
            DateTime windowEnd = barTime.Date.Add(new TimeSpan(SkipEndHour, SkipEndMin, 0));
            if (windowStart > windowEnd)
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

            string tagBase = string.Format("EVE_Skip_{0:yyyyMMdd_HHmm}", windowStart);
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
                catch
                {
                }

                string tagBase = string.Format("EVE_News_{0:yyyyMMdd_HHmm}", newsTime);
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

        private void DrawSessionLines()
        {
            DateTime t1 = _orDate.Add(new TimeSpan(9, 30, 0));
            DateTime t2 = _orDate.Add(new TimeSpan(SessionEndHour, SessionEndMin, 0));

            if (ShowORLines)
            {
                Draw.Line(this, "OR_H", false, t1, _orHigh, t2, _orHigh, Brushes.Magenta,    DashStyleHelper.Solid, 2);
                Draw.Line(this, "OR_L", false, t1, _orLow,  t2, _orLow,  Brushes.Magenta,    DashStyleHelper.Solid, 2);
            }
            else
            {
                RemoveDrawObject("OR_H");
                RemoveDrawObject("OR_L");
            }

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
                ExitLong(BuildExitSignalName("ForceFlatL"), GetOpenLongEntrySignal());
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(BuildExitSignalName("ForceFlatS"), GetOpenShortEntrySignal());

            LogDebug(Time[0] + " | ★ Force flatten executed.");
        }

        private bool IsAccountBalanceBlocked()
        {
            if (MaxAccountBalance <= 0.0)
                return false;

            double balance;
            if (!TryGetCurrentNetLiquidation(out balance))
                return false;

            if (balance >= MaxAccountBalance && !maxAccountLimitHit)
            {
                maxAccountLimitHit = true;
                LogDebug(string.Format(CultureInfo.InvariantCulture,
                    "Max account balance reached | netLiq={0:0.00} target={1:0.00}",
                    balance,
                    MaxAccountBalance));
            }

            if (!maxAccountLimitHit)
                return false;

            TryCancelOrder(_longLimitOrder);
            TryCancelOrder(_shortLimitOrder);

            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(BuildExitSignalName("MaxBalanceL"), GetOpenLongEntrySignal());
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(BuildExitSignalName("MaxBalanceS"), GetOpenShortEntrySignal());

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
                if (netLiquidation > 0.0)
                    return true;

                double realizedCash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                double unrealized = Position.MarketPosition != MarketPosition.Flat
                    ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0])
                    : 0.0;
                netLiquidation = realizedCash + unrealized;
                return realizedCash > 0.0 || Position.MarketPosition != MarketPosition.Flat;
            }
            catch
            {
                return false;
            }
        }

        private void TryCancelOrder(Order o)
        {
            if (o == null) return;
            if (o.OrderState == OrderState.Working ||
                o.OrderState == OrderState.Accepted)
                CancelOrder(o);
        }

        private bool IsSkipWindowConfigured()
        {
            if (!UseSkipTime)
                return false;

            return !(SkipStartHour == 0
                && SkipStartMin == 0
                && SkipEndHour == 0
                && SkipEndMin == 0);
        }

        private bool IsInSkipWindow(DateTime time)
        {
            if (!IsSkipWindowConfigured())
                return false;

            TimeSpan skipStart = new TimeSpan(SkipStartHour, SkipStartMin, 0);
            TimeSpan skipEnd = new TimeSpan(SkipEndHour, SkipEndMin, 0);
            TimeSpan now = time.TimeOfDay;
            if (skipStart < skipEnd)
                return now >= skipStart && now < skipEnd;

            return now >= skipStart || now < skipEnd;
        }

        private bool IsInNewsSkipWindow(DateTime time)
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
                if (time >= windowStart && time < windowEnd)
                    return true;
            }

            return false;
        }

        private bool IsLastBarOfSession()
        {
            return Bars != null && Bars.IsLastBarOfSession;
        }

        private void HandleSkipWindowTransition(bool inSkipWindowNow)
        {
            if (!_wasInSkipWindow && inSkipWindowNow)
            {
                bool hadLongWorkingEntry = _longEntryArmed || _longFVGPending || _longLimitOrder != null;
                bool hadShortWorkingEntry = _shortEntryArmed || _shortFVGPending || _shortLimitOrder != null;
                _longTriggerHitDuringSkip = false;
                _shortTriggerHitDuringSkip = false;

                CancelPendingEntriesForSkip();

                // Preserve entry intent across skip window: if a working entry was canceled at skip start
                // after trigger was already hit, request re-arm once skip window ends.
                if (hadLongWorkingEntry && _longTriggerHit && !_longInTrade)
                    _longRearmNeeded = true;
                if (hadShortWorkingEntry && _shortTriggerHit && !_shortInTrade)
                    _shortRearmNeeded = true;

                if (CloseAtSkipStart)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(BuildExitSignalName("SkipWindowL"), GetOpenLongEntrySignal());
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort(BuildExitSignalName("SkipWindowS"), GetOpenShortEntrySignal());
                }

                LogDebug(Time[0] + " | Entered skip window: canceled working entries."
                      + (CloseAtSkipStart ? " Open position flattened." : " CloseAtSkipStart disabled."));
            }
            else if (_wasInSkipWindow && !inSkipWindowNow)
            {
                // If trigger was crossed during skip, schedule a post-skip re-arm attempt.
                if (_longTriggerHitDuringSkip && _longTriggerHit && !_longInTrade && !_longEntryArmed)
                    _longRearmNeeded = true;

                if (_shortTriggerHitDuringSkip && _shortTriggerHit && !_shortInTrade && !_shortEntryArmed)
                    _shortRearmNeeded = true;

                _longTriggerHitDuringSkip = false;
                _shortTriggerHitDuringSkip = false;
            }

            _wasInSkipWindow = inSkipWindowNow;
        }

        private void HandleNewsSkipWindowTransition(bool inNewsSkipWindowNow)
        {
            if (!_wasInNewsSkipWindow && inNewsSkipWindowNow)
            {
                CancelPendingEntriesForSkip();

                if (CloseAtNewsStart)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(BuildExitSignalName("NewsSkipL"), GetOpenLongEntrySignal());
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort(BuildExitSignalName("NewsSkipS"), GetOpenShortEntrySignal());
                }

                LogDebug(Time[0] + " | Entered news skip window: canceled working entries."
                      + (CloseAtNewsStart ? " Open position flattened." : " CloseAtNewsStart disabled."));
            }

            _wasInNewsSkipWindow = inNewsSkipWindowNow;
        }

        private void CancelPendingEntriesForSkip()
        {
            TryCancelOrder(_longLimitOrder);
            TryCancelOrder(_shortLimitOrder);

            _longLimitOrder = null;
            _shortLimitOrder = null;
            _longEntryArmed = false;
            _shortEntryArmed = false;
            _longFVGPending = false;
            _shortFVGPending = false;
        }

        private void CancelPendingEntriesForInvalidConfiguration()
        {
            TryCancelOrder(_longLimitOrder);
            TryCancelOrder(_shortLimitOrder);

            _longLimitOrder = null;
            _shortLimitOrder = null;
            _longEntryArmed = false;
            _shortEntryArmed = false;
            _longFVGPending = false;
            _shortFVGPending = false;
        }

        private void CancelPendingEntriesForSessionBoundary(string reason)
        {
            bool hadPendingEntries =
                _longEntryArmed ||
                _shortEntryArmed ||
                _longFVGPending ||
                _shortFVGPending ||
                _longLimitOrder != null ||
                _shortLimitOrder != null;

            if (!hadPendingEntries)
                return;

            TryCancelOrder(_longLimitOrder);
            TryCancelOrder(_shortLimitOrder);

            _longLimitOrder = null;
            _shortLimitOrder = null;
            _longEntryArmed = false;
            _shortEntryArmed = false;
            _longFVGPending = false;
            _shortFVGPending = false;

            LogDebug(Time[0] + " | " + reason + ": canceled carryover working entries.");
        }

        private void ResetManagedProtectiveTemplatesForRealtime()
        {
            SetStopLoss(LongEntrySignal, CalculationMode.Ticks, RealtimeTemplateResetTicks, false);
            SetProfitTarget(LongEntrySignal, CalculationMode.Ticks, RealtimeTemplateResetTicks);
            SetStopLoss(ShortEntrySignal, CalculationMode.Ticks, RealtimeTemplateResetTicks, false);
            SetProfitTarget(ShortEntrySignal, CalculationMode.Ticks, RealtimeTemplateResetTicks);
        }

        private void ValidateRequiredPrimaryTimeframe(int requiredSeconds)
        {
            bool isSecondSeries = BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Second;
            bool timeframeMatches = isSecondSeries && BarsPeriod.Value == requiredSeconds;
            isConfiguredTimeframeValid = timeframeMatches;
            if (timeframeMatches)
                return;

            string actualTimeframe = BarsPeriod == null
                ? "Unknown"
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", BarsPeriod.Value, BarsPeriod.BarsPeriodType);
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "{0} must run on a {1}-second chart. Current chart is {2}. Trading is disabled until timeframe is corrected.",
                Name,
                requiredSeconds,
                actualTimeframe);
            LogDebug(string.Format(CultureInfo.InvariantCulture, "{0} | {1}", Name, message));
            ShowTimeframeValidationPopup(message);
        }

        private void ShowTimeframeValidationPopup(string message)
        {
            if (timeframePopupShown)
                return;

            timeframePopupShown = true;
            if (System.Windows.Application.Current == null)
                return;

            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () =>
                    {
                        System.Windows.MessageBox.Show(
                            message,
                            "Invalid Timeframe",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    });
            }
            catch (Exception ex)
            {
                LogDebug(string.Format(CultureInfo.InvariantCulture, "{0} | Failed to show timeframe popup: {1}", Name, ex.Message));
            }
        }

        private void ValidateRequiredPrimaryInstrument()
        {
            string instrumentName = Instrument != null && Instrument.MasterInstrument != null
                ? (Instrument.MasterInstrument.Name ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;
            bool instrumentMatches = instrumentName == "NQ" || instrumentName == "MNQ";
            isConfiguredInstrumentValid = instrumentMatches;
            if (instrumentMatches)
                return;

            string actualInstrument = string.IsNullOrWhiteSpace(instrumentName) ? "Unknown" : instrumentName;
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "{0} must run on NQ or MNQ. Current instrument is {1}. Trading is disabled until instrument is corrected.",
                Name,
                actualInstrument);
            LogDebug(string.Format(CultureInfo.InvariantCulture, "{0} | {1}", Name, message));
            ShowInstrumentValidationPopup(message);
        }

        private void ShowInstrumentValidationPopup(string message)
        {
            if (instrumentPopupShown)
                return;

            instrumentPopupShown = true;
            if (System.Windows.Application.Current == null)
                return;

            try
            {
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () =>
                    {
                        System.Windows.MessageBox.Show(
                            message,
                            "Invalid Instrument",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    });
            }
            catch (Exception ex)
            {
                LogDebug(string.Format(CultureInfo.InvariantCulture, "{0} | Failed to show instrument popup: {1}", Name, ex.Message));
            }
        }

        private bool ShowEntryConfirmation(string orderType, double price, int quantity)
        {
            bool result = false;
            if (System.Windows.Application.Current == null)
                return false;

            System.Windows.Application.Current.Dispatcher.Invoke(
                () =>
                {
                    string message = string.Format(CultureInfo.InvariantCulture, "Confirm {0} entry\nPrice: {1}\nQty: {2}", orderType, price, quantity);
                    MessageBoxResult response = System.Windows.MessageBox.Show(
                        message,
                        "Entry Confirmation",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Question);
                    result = response == System.Windows.MessageBoxResult.Yes;
                });

            return result;
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
            _wasInSkipWindow  = false;
            _wasInNewsSkipWindow = false;
            _longTriggerHitDuringSkip = false;
            _shortTriggerHitDuringSkip = false;
            projectXLastSyncedStopPrice = 0.0;
            projectXLastSyncedTargetPrice = 0.0;
        }

        // =====================================================================
        //  INFOBOX
        // =====================================================================

        private void UpdateInfoText()
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
                    FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                    TextAlignment = (isHeader || isFooter) ? TextAlignment.Center : TextAlignment.Left,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                TextOptions.SetTextFormattingMode(text, TextFormattingMode.Display);

                string label = lines[i].label ?? string.Empty;
                string value = lines[i].value ?? string.Empty;
                string normalizedValue = NormalizeInfoValueToken(value);
                bool valueUsesEmojiRendering = ClassifyInfoValueRunKind(normalizedValue) == InfoValueRunKind.Emoji;
                TextOptions.SetTextRenderingMode(text, valueUsesEmojiRendering ? TextRenderingMode.Grayscale : TextRenderingMode.ClearType);

                text.Inlines.Add(new Run(label) { Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : InfoLabelBrush });
                if (!string.IsNullOrEmpty(value))
                {
                    text.Inlines.Add(new Run(" ") { Foreground = (isHeader || isFooter) ? InfoHeaderTextBrush : InfoLabelBrush });

                    Brush stateValueBrush = lines[i].valueBrush;
                    if (stateValueBrush == null || stateValueBrush == Brushes.Transparent)
                        stateValueBrush = lines[i].labelBrush;
                    if (stateValueBrush == null || stateValueBrush == Brushes.Transparent)
                        stateValueBrush = InfoValueBrush;

                    var valueRun = BuildInfoValueRun(normalizedValue, stateValueBrush);
                    text.Inlines.Add(valueRun);
                }

                rowBorder.Child = text;
                infoBoxRowsPanel.Children.Add(rowBorder);
            }
        }

        private Run BuildInfoValueRun(string value, Brush stateValueBrush)
        {
            string safeValue = value ?? string.Empty;
            string normalizedValue = NormalizeInfoValueToken(safeValue);
            switch (ClassifyInfoValueRunKind(normalizedValue))
            {
                case InfoValueRunKind.Emoji:
                    var emojiRun = new Run(normalizedValue) { FontFamily = InfoEmojiFontFamily };
                    emojiRun.Foreground = stateValueBrush;
                    TextOptions.SetTextRenderingMode(emojiRun, TextRenderingMode.Grayscale);
                    return emojiRun;
                case InfoValueRunKind.Symbol:
                    return new Run(normalizedValue) { FontFamily = InfoSymbolFontFamily, Foreground = stateValueBrush };
                default:
                    return new Run(normalizedValue) { Foreground = stateValueBrush };
            }
        }

        private string NormalizeInfoValueToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value ?? string.Empty;

            string token = value.Trim();
            if (token == "○" || token == "◯" || token == "⚪")
                return "🚫";

            return value;
        }

        private InfoValueRunKind ClassifyInfoValueRunKind(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return InfoValueRunKind.Default;

            string token = value.Trim();
            if (InfoEmojiTokens.Contains(token) || ContainsEmojiCodePoint(token))
                return InfoValueRunKind.Emoji;
            if (InfoSymbolTokens.Contains(token))
                return InfoValueRunKind.Symbol;
            return InfoValueRunKind.Default;
        }

        private bool ContainsEmojiCodePoint(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                int codePoint = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }

                if ((codePoint >= 0x1F300 && codePoint <= 0x1FAFF) ||
                    (codePoint >= 0x2600 && codePoint <= 0x27BF))
                {
                    return true;
                }
            }

            return false;
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
            RemoveDrawObject("Info");
            RemoveDrawObject("InfoPanel");
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

            lines.Add((string.Format("EVE v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(("Contracts:", GetContractsTextForInfo(), Brushes.LightGray, Brushes.LightGray));
            bool isArmed = IsTradeArmedForInfo();
            lines.Add(("Armed:", isArmed ? "✅" : "🚫", Brushes.LightGray, isArmed ? Brushes.LimeGreen : Brushes.IndianRed));

            if (!UseNewsSkip)
            {
                lines.Add(("News:", "Disabled", Brushes.LightGray, Brushes.LightGray));
            }
            else
            {
                List<DateTime> weekNews = GetCurrentWeekNews(Time[0]);
                if (weekNews.Count == 0)
                {
                    lines.Add(("News:", "🚫", Brushes.LightGray, Brushes.IndianRed));
                }
                else
                {
                    for (int i = 0; i < weekNews.Count; i++)
                    {
                        DateTime newsTime = weekNews[i];
                        bool blockPassed = Time[0] > newsTime.AddMinutes(NewsBlockMinutes);
                        string dayPart = newsTime.ToString("ddd", CultureInfo.InvariantCulture);
                        string timePart = newsTime.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
                        string value = dayPart + " " + timePart;
                        Brush newsBrush = blockPassed ? PassedNewsRowBrush : Brushes.LightGray;
                        lines.Add(("News:", value, newsBrush, newsBrush));
                    }
                }
            }

            lines.Add(("Session:", "New York", Brushes.LightGray, Brushes.LightGray));
            lines.Add(("AutoEdge Systems™", string.Empty, InfoLabelBrush, Brushes.Transparent));

            return lines;
        }

        private bool IsTradeArmedForInfo()
        {
            if (!_orSet || _forceFlatDone || Position.MarketPosition != MarketPosition.Flat)
                return false;
            if (IsLastBarOfSession())
                return false;
            if (IsInSkipWindow(Time[0]))
                return false;
            if (IsInNewsSkipWindow(Time[0]))
                return false;

            if (_longEntryArmed || _shortEntryArmed)
                return true;

            TimeSpan tod = Time[0].TimeOfDay;
            TimeSpan noNewTime = new TimeSpan(NoNewTradesHour, NoNewTradesMin, 0);

            bool canTradeLong = _longORValid
                && !_longInTrade
                && tod < noNewTime
                && _longTradesToday < GetLongMaxTradesPerDay()
                && _longProfitableToday < GetLongMaxProfitableTrades()
                && _longLosingToday < GetLongMaxLosingTrades();

            bool canTradeShort = _shortORValid
                && !_shortInTrade
                && tod < noNewTime
                && _shortTradesToday < GetShortMaxTradesPerDay()
                && _shortProfitableToday < GetShortMaxProfitableTrades()
                && _shortLosingToday < GetShortMaxLosingTrades();

            return canTradeLong || canTradeShort;
        }

        private string GetContractsTextForInfo()
        {
            return CommonContracts.ToString(CultureInfo.InvariantCulture);
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
                StartPoint = new System.Windows.Point(0.5, 0.0),
                EndPoint = new System.Windows.Point(0.5, 1.0)
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

        private string GetAddOnVersion()
        {
            Assembly assembly = GetType().Assembly;
            Version version = assembly.GetName().Version;
            return version != null ? version.ToString() : "0.0.0.0";
        }

        private void EnsureNewsDatesInitialized()
        {
            if (newsDatesInitialized)
                return;

            NewsDates.Clear();
            LoadHardcodedNewsDates();
            NewsDates.Sort();
            newsDatesInitialized = true;
        }

        private void LoadHardcodedNewsDates()
        {
            if (string.IsNullOrWhiteSpace(NewsDatesRaw))
                return;

            string[] entries = NewsDatesRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                DateTime parsed;
                if (!DateTime.TryParse(entries[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
                    continue;

                if (parsed.TimeOfDay == new TimeSpan(14, 0, 0) && !NewsDates.Contains(parsed))
                    NewsDates.Add(parsed);
            }
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

        private int GetDefaultWebhookQuantity()
        {
            int longQty = _longORValid ? GetLongContracts() : 0;
            int shortQty = _shortORValid ? GetShortContracts() : 0;
            int qty = Math.Max(longQty, shortQty);
            return Math.Max(1, qty);
        }

        private bool SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime)
                return false;

            if (WebhookProviderType == WebhookProvider.ProjectX)
            {
                int orderQtyForProvider = quantityOverride > 0 ? quantityOverride : GetDefaultWebhookQuantity();
                LogDebug(string.Format(
                    "[ProjectX] webhook attempt | event={0} qty={1} market={2} entry={3:0.00} tp={4:0.00} sl={5:0.00}",
                    eventType,
                    orderQtyForProvider,
                    isMarketEntry,
                    entryPrice,
                    takeProfit,
                    stopLoss));
                return SendProjectX(eventType, entryPrice, takeProfit, stopLoss, isMarketEntry, orderQtyForProvider);
            }

            if (string.IsNullOrWhiteSpace(WebhookUrl))
                return false;

            try
            {
                int orderQty = quantityOverride > 0 ? quantityOverride : GetDefaultWebhookQuantity();
                string ticker = !string.IsNullOrWhiteSpace(WebhookTickerOverride)
                    ? WebhookTickerOverride.Trim()
                    : (Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : "UNKNOWN");
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
                string json = string.Empty;
                string action = (eventType ?? string.Empty).ToLowerInvariant();

                if (action == "buy" || action == "sell")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"{1}\",\"orderType\":\"{2}\",\"quantityType\":\"fixed_quantity\",\"quantity\":{3},\"signalPrice\":{4},\"time\":\"{5}\",\"takeProfit\":{{\"limitPrice\":{6}}},\"stopLoss\":{{\"type\":\"stop\",\"stopPrice\":{7}}}}}",
                        ticker,
                        action,
                        isMarketEntry ? "market" : "limit",
                        orderQty,
                        entryPrice,
                        timestamp,
                        takeProfit,
                        stopLoss);
                }
                else if (action == "exit")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"exit\",\"orderType\":\"market\",\"quantityType\":\"fixed_quantity\",\"quantity\":{1},\"cancel\":true,\"time\":\"{2}\"}}",
                        ticker,
                        orderQty,
                        timestamp);
                }
                else if (action == "cancel")
                {
                    json = string.Format(CultureInfo.InvariantCulture,
                        "{{\"ticker\":\"{0}\",\"action\":\"cancel\",\"time\":\"{1}\"}}",
                        ticker,
                        timestamp);
                }

                if (string.IsNullOrWhiteSpace(json))
                    return false;

                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.UploadString(WebhookUrl, "POST", json);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool SendProjectX(string eventType, double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            if (!EnsureProjectXSession())
            {
                LogDebug(string.Format("[ProjectX] webhook skipped | event={0} reason=auth-unavailable", eventType));
                return false;
            }

            List<ProjectXAccountInfo> targetAccounts;
            string contractId;
            if (!TryGetProjectXTargets(out targetAccounts, out contractId))
            {
                LogDebug(string.Format("[ProjectX] webhook skipped | event={0} reason=account-selection-or-contract-unavailable", eventType));
                return false;
            }

            LogDebug(string.Format(
                "[ProjectX] targets | event={0} accounts={1} contractId={2}",
                eventType,
                string.Join(", ", targetAccounts.Select(a => string.Format(CultureInfo.InvariantCulture, "{0}:{1}", a.Id, a.Name ?? string.Empty)).ToArray()),
                contractId));

            foreach (var account in targetAccounts)
            {
                try
                {
                    switch ((eventType ?? string.Empty).ToLowerInvariant())
                    {
                        case "buy":
                        case "sell":
                            if (ProjectXPrepareForEntry(account.Id, contractId))
                                ProjectXPlaceOrder(eventType, account.Id, contractId, entryPrice, takeProfit, stopLoss, isMarketEntry, quantity);
                            break;
                        case "exit":
                            ProjectXFlattenPosition(account.Id, contractId);
                            break;
                        case "cancel":
                            ProjectXCancelOrders(account.Id, contractId);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LogDebug(string.Format(
                        "[ProjectX] account error | event={0} accountId={1} accountName={2} error={3}",
                        eventType,
                        account.Id,
                        account.Name ?? string.Empty,
                        ex.Message));
                }
            }

            return targetAccounts.Count > 0;
        }

        private void RunProjectXStartupPreflight()
        {
            if (WebhookProviderType != WebhookProvider.ProjectX)
            {
                LogDebug(string.Format("[ProjectX] startup preflight skipped | provider={0}", WebhookProviderType));
                return;
            }

            string instrumentKey = GetProjectXInstrumentKey();
            string selectors = string.Join(", ", ParseProjectXAccountSelectors(ProjectXAccountId).ToArray());
            LogProjectXDiscovery(string.Format(
                "startup preflight begin | instrument={0} selectors={1} baseUrl={2}",
                string.IsNullOrWhiteSpace(instrumentKey) ? "<unknown>" : instrumentKey,
                string.IsNullOrWhiteSpace(selectors) ? "<none>" : selectors,
                string.IsNullOrWhiteSpace(ProjectXApiBaseUrl) ? "<empty>" : ProjectXApiBaseUrl));

            if (!EnsureProjectXSession())
            {
                LogProjectXDiscovery("startup preflight failed | stage=auth");
                return;
            }

            List<ProjectXAccountInfo> accounts;
            if (!TryLoadProjectXAccounts(out accounts))
            {
                LogProjectXDiscovery("startup preflight failed | stage=accounts");
                return;
            }

            string contractId;
            if (!TryResolveProjectXContractId(out contractId))
            {
                LogProjectXDiscovery("startup preflight failed | stage=contract");
                return;
            }

            List<ProjectXAccountInfo> targetAccounts;
            string targetContractId;
            if (!TryGetProjectXTargets(out targetAccounts, out targetContractId))
            {
                LogProjectXDiscovery("startup preflight failed | stage=targets");
                return;
            }

            LogProjectXStatus(string.Format(
                "webhook targets | count={0} contractId={1}",
                targetAccounts.Count,
                targetContractId ?? string.Empty));
            foreach (var account in targetAccounts)
            {
                LogProjectXStatus(string.Format(
                    "target account | id={0} name={1}",
                    account.Id,
                    account.Name ?? string.Empty));
            }

            LogProjectXDiscovery(string.Format(
                "startup preflight ready | accounts={0} contractId={1}",
                FormatProjectXAccountsForLog(targetAccounts),
                targetContractId));
        }

        private bool EnsureProjectXSession()
        {
            if (string.IsNullOrWhiteSpace(ProjectXApiBaseUrl))
            {
                LogProjectXStatus("login failed | reason=empty-base-url");
                return false;
            }

            if (!string.IsNullOrWhiteSpace(projectXSessionToken) &&
                (DateTime.UtcNow - projectXTokenAcquiredUtc).TotalHours < 23)
                return true;

            if (string.IsNullOrWhiteSpace(ProjectXUsername) || string.IsNullOrWhiteSpace(ProjectXApiKey))
            {
                LogProjectXStatus("login failed | reason=missing-credentials");
                return false;
            }

            string loginJson = string.Format(CultureInfo.InvariantCulture,
                "{{\"userName\":\"{0}\",\"apiKey\":\"{1}\"}}",
                ProjectXUsername,
                ProjectXApiKey);

            string response = ProjectXPost("/api/Auth/loginKey", loginJson, false, true);
            if (string.IsNullOrWhiteSpace(response))
            {
                LogProjectXStatus("login failed | reason=empty-response");
                return false;
            }

            string token;
            if (!TryGetJsonString(response, "token", out token))
            {
                LogProjectXStatus("login failed | reason=missing-token");
                return false;
            }

            projectXSessionToken = token;
            projectXTokenAcquiredUtc = DateTime.UtcNow;
            projectXAccounts = null;
            projectXLastOrderIds.Clear();
            projectXResolvedContractId = null;
            projectXResolvedInstrumentKey = string.Empty;
            LogProjectXStatus("login succeeded");
            return true;
        }

        private bool TryGetProjectXTargets(out List<ProjectXAccountInfo> targetAccounts, out string contractId)
        {
            targetAccounts = null;
            contractId = null;

            if (!TryResolveProjectXContractId(out contractId))
                return false;

            List<ProjectXAccountInfo> accounts;
            if (!TryLoadProjectXAccounts(out accounts))
                return false;

            var selectors = ParseProjectXAccountSelectors(ProjectXAccountId);
            if (selectors.Count == 0)
            {
                LogProjectXStatus("warning | no webhooks will be sent because ProjectX Accounts is empty.");
                LogProjectXDiscovery("account selection failed | reason=no-selection");
                return false;
            }

            var matchedAccounts = new List<ProjectXAccountInfo>();
            var matchedIds = new HashSet<int>();
            var unmatchedSelectors = new List<string>();

            foreach (string selector in selectors)
            {
                int accountId;
                List<ProjectXAccountInfo> matches = int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out accountId)
                    ? accounts.Where(a => a.CanTrade && a.Id == accountId).ToList()
                    : accounts.Where(a => a.CanTrade && string.Equals(a.Name ?? string.Empty, selector, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matches.Count == 0)
                {
                    unmatchedSelectors.Add(selector);
                    continue;
                }

                foreach (var match in matches)
                {
                    if (matchedIds.Add(match.Id))
                        matchedAccounts.Add(match);
                }
            }

            if (unmatchedSelectors.Count > 0)
            {
                LogProjectXDiscovery(string.Format(
                    "account selection unmatched | selectors={0}",
                    string.Join(", ", unmatchedSelectors.ToArray())));
            }

            if (matchedAccounts.Count == 0)
            {
                LogProjectXDiscovery("account selection failed | reason=no-matching-tradable-accounts");
                return false;
            }

            targetAccounts = matchedAccounts;
            return true;
        }

        private bool TryLoadProjectXAccounts(out List<ProjectXAccountInfo> accounts)
        {
            if (projectXAccounts != null && projectXAccounts.Count > 0)
            {
                accounts = projectXAccounts;
                return true;
            }

            string response = ProjectXPost("/api/Account/search", "{\"onlyActiveAccounts\":true}", true, true);
            if (string.IsNullOrWhiteSpace(response))
            {
                LogProjectXStatus("warning | no webhooks will be sent because no ProjectX accounts were found.");
                LogProjectXDiscovery("account load failed | reason=empty-response");
                accounts = null;
                return false;
            }

            accounts = ExtractProjectXAccounts(response).ToList();
            projectXAccounts = accounts.Count > 0 ? accounts : null;

            LogProjectXStatus(string.Format("accounts found | count={0}", accounts.Count));
            if (accounts.Count == 0)
            {
                LogProjectXStatus("warning | no webhooks will be sent because no ProjectX accounts were found.");
                return false;
            }

            foreach (var account in accounts)
            {
                LogProjectXStatus(string.Format(
                    "account | id={0} name={1} canTrade={2} isVisible={3}",
                    account.Id,
                    account.Name ?? string.Empty,
                    account.CanTrade,
                    account.IsVisible));
            }

            return true;
        }

        private bool TryResolveProjectXContractId(out string contractId)
        {
            contractId = null;

            string instrumentKey = GetProjectXInstrumentKey();
            string overrideContractId = ProjectXContractId != null ? ProjectXContractId.Trim() : string.Empty;
            if (!string.IsNullOrWhiteSpace(overrideContractId))
            {
                contractId = overrideContractId;
                projectXResolvedContractId = contractId;
                projectXResolvedInstrumentKey = instrumentKey;
                LogProjectXDiscovery(string.Format(
                    "contract override | instrument={0} contractId={1}",
                    instrumentKey,
                    contractId));
                return true;
            }

            if (!string.IsNullOrWhiteSpace(projectXResolvedContractId) &&
                string.Equals(projectXResolvedInstrumentKey, instrumentKey, StringComparison.OrdinalIgnoreCase))
            {
                contractId = projectXResolvedContractId;
                return true;
            }

            string root = GetProjectXInstrumentRoot();
            if (string.IsNullOrWhiteSpace(root))
            {
                LogProjectXDiscovery("contract resolve failed | reason=empty-instrument-root");
                return false;
            }

            DateTime expiry;
            string desiredSuffix = TryGetInstrumentExpiry(out expiry) || TryParseInstrumentExpiryFromFullName(out expiry)
                ? GetProjectXFuturesMonthCode(expiry.Month) + expiry.ToString("yy", CultureInfo.InvariantCulture)
                : string.Empty;

            List<ProjectXContractInfo> contracts;
            if (!TrySearchProjectXContracts(root, desiredSuffix, out contracts))
                return false;

            ProjectXContractInfo selected = SelectProjectXContract(root, desiredSuffix, contracts);
            if (selected == null || string.IsNullOrWhiteSpace(selected.Id))
            {
                LogProjectXDiscovery(string.Format(
                    "contract resolve failed | root={0} desiredSuffix={1} candidates={2}",
                    root,
                    string.IsNullOrWhiteSpace(desiredSuffix) ? "active" : desiredSuffix,
                    string.Join(", ", contracts.Select(c => c.Id ?? string.Empty).ToArray())));
                return false;
            }

            contractId = selected.Id;
            projectXResolvedContractId = contractId;
            projectXResolvedInstrumentKey = instrumentKey;
            LogProjectXDiscovery(string.Format(
                "contract resolved | instrument={0} root={1} desiredSuffix={2} contractId={3} name={4} active={5}",
                instrumentKey,
                root,
                string.IsNullOrWhiteSpace(desiredSuffix) ? "active" : desiredSuffix,
                selected.Id,
                selected.Name ?? string.Empty,
                selected.ActiveContract));
            return true;
        }

        private bool TrySearchProjectXContracts(string root, string desiredSuffix, out List<ProjectXContractInfo> contracts)
        {
            contracts = null;

            string primarySearchText = !string.IsNullOrWhiteSpace(desiredSuffix) ? root + desiredSuffix : root;
            if (TrySearchProjectXContractsByText(primarySearchText, root, out contracts) && contracts.Count > 0)
                return true;

            if (!string.Equals(primarySearchText, root, StringComparison.OrdinalIgnoreCase) &&
                TrySearchProjectXContractsByText(root, root, out contracts) && contracts.Count > 0)
                return true;

            LogProjectXDiscovery(string.Format(
                "contract search failed | root={0} desiredSuffix={1}",
                root,
                string.IsNullOrWhiteSpace(desiredSuffix) ? "active" : desiredSuffix));
            return false;
        }

        private bool TrySearchProjectXContractsByText(string searchText, string root, out List<ProjectXContractInfo> contracts)
        {
            if (TrySearchProjectXContractsByText(searchText, root, true, out contracts) && contracts.Count > 0)
                return true;

            if (TrySearchProjectXContractsByText(searchText, root, false, out contracts) && contracts.Count > 0)
                return true;

            contracts = new List<ProjectXContractInfo>();
            return false;
        }

        private bool TrySearchProjectXContractsByText(string searchText, string root, bool live, out List<ProjectXContractInfo> contracts)
        {
            string requestJson = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"live\":{0},\"searchText\":\"{1}\"}}",
                live ? "true" : "false",
                searchText);
            string response = ProjectXPost("/api/Contract/search", requestJson, true, true);
            contracts = ExtractProjectXContracts(response)
                .Where(c => DoesProjectXContractMatchRoot(c, root))
                .ToList();

            LogProjectXDiscovery(string.Format(
                "contract search | searchText={0} live={1} matches={2}",
                searchText,
                live,
                contracts.Count));
            return !string.IsNullOrWhiteSpace(response);
        }

        private ProjectXContractInfo SelectProjectXContract(string root, string desiredSuffix, List<ProjectXContractInfo> contracts)
        {
            if (contracts == null || contracts.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(desiredSuffix))
            {
                var exactMatches = contracts
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id) &&
                        c.Id.EndsWith("." + desiredSuffix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (exactMatches.Count > 0)
                    return exactMatches.FirstOrDefault(c => c.ActiveContract) ?? exactMatches[0];
            }

            var activeMatches = contracts.Where(c => c.ActiveContract).ToList();
            if (activeMatches.Count > 0)
                return activeMatches[0];

            return contracts[0];
        }

        private bool DoesProjectXContractMatchRoot(ProjectXContractInfo contract, string root)
        {
            if (contract == null || string.IsNullOrWhiteSpace(root))
                return false;

            string expectedSymbolId = "F.US." + root;
            if (!string.IsNullOrWhiteSpace(contract.SymbolId) &&
                string.Equals(contract.SymbolId, expectedSymbolId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(contract.Id) &&
                contract.Id.IndexOf(".US." + root + ".", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (!string.IsNullOrWhiteSpace(contract.Name) &&
                contract.Name.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private string GetProjectXInstrumentKey()
        {
            if (Instrument == null)
                return string.Empty;

            string fullName = Instrument.FullName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(fullName))
                return fullName.Trim().ToUpperInvariant();

            return GetProjectXInstrumentRoot();
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
                PropertyInfo property = Instrument.GetType().GetProperty("Expiry", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (property == null)
                    return false;

                object raw = property.GetValue(Instrument, null);
                if (!(raw is DateTime))
                    return false;

                DateTime dt = (DateTime)raw;
                if (dt.Year < 2000)
                    return false;

                expiry = dt;
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
            string fullName = Instrument != null ? (Instrument.FullName ?? string.Empty).Trim().ToUpperInvariant() : string.Empty;
            if (string.IsNullOrWhiteSpace(fullName))
                return false;

            Match match = Regex.Match(fullName, @"\b(?<month>\d{1,2})[-/](?<year>\d{2,4})\b");
            if (!match.Success)
                return false;

            int month;
            int year;
            if (!int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out month) ||
                !int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out year))
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
            switch (month)
            {
                case 1: return "F";
                case 2: return "G";
                case 3: return "H";
                case 4: return "J";
                case 5: return "K";
                case 6: return "M";
                case 7: return "N";
                case 8: return "Q";
                case 9: return "U";
                case 10: return "V";
                case 11: return "X";
                case 12: return "Z";
                default: return string.Empty;
            }
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
            if (accounts == null)
                return "<none>";

            var items = accounts
                .Select(a => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}:{1}",
                    a.Id,
                    a.Name ?? string.Empty))
                .ToArray();
            return items.Length > 0 ? string.Join(", ", items) : "<none>";
        }

        private string GetProjectXOrderKey(int accountId, string contractId)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}", accountId, contractId ?? string.Empty);
        }

        private string ProjectXPlaceOrder(string side, int accountId, string contractId, double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            int orderSide = side.Equals("buy", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            int orderType = isMarketEntry ? 2 : 1;
            int normalizedQuantity = Math.Max(1, quantity);
            double entry = Instrument.MasterInstrument.RoundToTickSize(entryPrice);
            bool isLong = orderSide == 0;
            int tpTicks = NormalizeProjectXBracketTicks(
                PriceToTicks(takeProfit - entry),
                4,
                isLong ? 1 : -1);
            int slTicks = NormalizeProjectXBracketTicks(
                PriceToTicks(stopLoss - entry),
                1,
                isLong ? -1 : 1);

            string limitPart = isMarketEntry
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, ",\"limitPrice\":{0}", entry);

            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\",\"type\":{2},\"side\":{3},\"size\":{4}{5},\"takeProfitBracket\":{{\"quantity\":{6},\"type\":1,\"ticks\":{7}}},\"stopLossBracket\":{{\"quantity\":{6},\"type\":4,\"ticks\":{8}}}}}",
                accountId,
                contractId,
                orderType,
                orderSide,
                normalizedQuantity,
                limitPart,
                normalizedQuantity,
                tpTicks,
                slTicks);

            string response = ProjectXPost("/api/Order/place", json, true);
            long orderId;
            if (TryGetJsonLong(response, "orderId", out orderId))
                projectXLastOrderIds[GetProjectXOrderKey(accountId, contractId)] = orderId;

            return response;
        }

        private int NormalizeProjectXBracketTicks(int rawTicks, int minAbsTicks, int zeroTickDirection)
        {
            int direction = rawTicks == 0 ? Math.Sign(zeroTickDirection) : Math.Sign(rawTicks);
            int absTicks = Math.Abs(rawTicks);
            if (absTicks < minAbsTicks)
                absTicks = minAbsTicks;
            return direction * absTicks;
        }

        private bool ProjectXPrepareForEntry(int accountId, string contractId)
        {
            ProjectXCancelOrders(accountId, contractId);

            if (!WaitForProjectXOrdersCleared(accountId, contractId, RealtimeTemplateResetTicks))
            {
                LogDebug(string.Format(
                    "[ProjectX] prepare failed | stage=cancel-clear accountId={0} contractId={1}",
                    accountId,
                    contractId));
                return false;
            }

            int positionSize;
            if (TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize) && positionSize != 0)
            {
                ProjectXClosePosition(accountId, contractId);

                if (!WaitForProjectXFlat(accountId, contractId, RealtimeTemplateResetTicks))
                {
                    LogDebug(string.Format(
                        "[ProjectX] prepare failed | stage=flat accountId={0} contractId={1} positionSize={2}",
                        accountId,
                        contractId,
                        positionSize));
                    return false;
                }

                ProjectXCancelOrders(accountId, contractId);
                if (!WaitForProjectXOrdersCleared(accountId, contractId, RealtimeTemplateResetTicks))
                {
                    LogDebug(string.Format(
                        "[ProjectX] prepare failed | stage=post-close-cancel accountId={0} contractId={1}",
                        accountId,
                        contractId));
                    return false;
                }
            }

            return true;
        }

        private void ProjectXFlattenPosition(int accountId, string contractId)
        {
            ProjectXCancelOrders(accountId, contractId);
            if (!WaitForProjectXOrdersCleared(accountId, contractId, RealtimeTemplateResetTicks))
            {
                LogDebug(string.Format(
                    "[ProjectX] flatten warning | stage=cancel-clear accountId={0} contractId={1}",
                    accountId,
                    contractId));
            }

            int positionSize;
            if (TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize) && positionSize != 0)
            {
                ProjectXClosePosition(accountId, contractId);
                if (!WaitForProjectXFlat(accountId, contractId, RealtimeTemplateResetTicks))
                {
                    LogDebug(string.Format(
                        "[ProjectX] flatten warning | stage=flat accountId={0} contractId={1} positionSize={2}",
                        accountId,
                        contractId,
                        positionSize));
                }
            }

            ProjectXCancelOrders(accountId, contractId);
            if (!WaitForProjectXOrdersCleared(accountId, contractId, RealtimeTemplateResetTicks))
            {
                LogDebug(string.Format(
                    "[ProjectX] flatten warning | stage=post-close-cancel accountId={0} contractId={1}",
                    accountId,
                    contractId));
            }
        }

        private string ProjectXClosePosition(int accountId, string contractId)
        {
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"accountId\":{0},\"contractId\":\"{1}\"}}",
                accountId,
                contractId);
            return ProjectXPost("/api/Position/closeContract", json, true);
        }

        private void ProjectXCancelOrders(int accountId, string contractId)
        {
            foreach (long orderId in GetProjectXOpenOrderIds(accountId, contractId))
            {
                string cancelJson = string.Format(CultureInfo.InvariantCulture,
                    "{{\"accountId\":{0},\"orderId\":{1}}}",
                    accountId,
                    orderId);
                ProjectXPost("/api/Order/cancel", cancelJson, true);
            }

            projectXLastOrderIds.Remove(GetProjectXOrderKey(accountId, contractId));
        }

        private bool WaitForProjectXFlat(int accountId, string contractId, int timeoutMs)
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow <= deadlineUtc)
            {
                int positionSize;
                if (TryGetProjectXOpenPositionSize(accountId, contractId, out positionSize) && positionSize == 0)
                    return true;

                System.Threading.Thread.Sleep(150);
            }

            return false;
        }

        private bool WaitForProjectXOrdersCleared(int accountId, string contractId, int timeoutMs)
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow <= deadlineUtc)
            {
                if (GetProjectXOpenOrderIds(accountId, contractId).Count == 0)
                    return true;

                System.Threading.Thread.Sleep(150);
            }

            return false;
        }

        private List<long> GetProjectXOpenOrderIds(int accountId, string contractId)
        {
            var orderIds = new List<long>();
            string searchJson = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string searchResponse = ProjectXPost("/api/Order/searchOpen", searchJson, true);
            bool success;
            if (TryGetJsonBool(searchResponse, "success", out success) && !success)
                return orderIds;

            foreach (var order in ExtractProjectXOrders(searchResponse))
            {
                object contractObj;
                if (!order.TryGetValue("contractId", out contractObj))
                    continue;
                if (!string.Equals(contractObj != null ? contractObj.ToString() : string.Empty, contractId, StringComparison.OrdinalIgnoreCase))
                    continue;

                object idObj;
                long id;
                if (!order.TryGetValue("id", out idObj) || !TryConvertToLong(idObj, out id) || id <= 0)
                    continue;

                orderIds.Add(id);
            }

            return orderIds;
        }

        private bool TryGetProjectXOpenPositionSize(int accountId, string contractId, out int signedSize)
        {
            signedSize = 0;
            string searchJson = string.Format(CultureInfo.InvariantCulture, "{{\"accountId\":{0}}}", accountId);
            string searchResponse = ProjectXPost("/api/Position/searchOpen", searchJson, true);
            bool success;
            if (TryGetJsonBool(searchResponse, "success", out success) && !success)
                return false;

            foreach (var position in ExtractProjectXPositions(searchResponse))
            {
                object contractObj;
                if (!position.TryGetValue("contractId", out contractObj))
                    continue;
                if (!string.Equals(contractObj != null ? contractObj.ToString() : string.Empty, contractId, StringComparison.OrdinalIgnoreCase))
                    continue;

                object typeObj;
                object sizeObj;
                int type;
                int size;
                if (!position.TryGetValue("type", out typeObj) || !TryConvertToInt(typeObj, out type))
                    continue;
                if (!position.TryGetValue("size", out sizeObj) || !TryConvertToInt(sizeObj, out size) || size <= 0)
                    continue;

                signedSize += type == 2 ? -size : size;
            }

            return true;
        }

        private string ProjectXPost(string path, string json, bool requiresAuth)
        {
            return ProjectXPost(path, json, requiresAuth, false);
        }

        private string ProjectXPost(string path, string json, bool requiresAuth, bool alwaysLog)
        {
            string baseUrl = ProjectXApiBaseUrl != null ? ProjectXApiBaseUrl.TrimEnd('/') : string.Empty;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            if (alwaysLog)
                LogProjectXDiscovery(string.Format(
                    "request | url={0}{1} auth={2} payload={3}",
                    baseUrl,
                    path,
                    requiresAuth,
                    SanitizeProjectXJsonForLog(json)));
            else
                LogDebug(string.Format(
                    "[ProjectX] request | url={0}{1} auth={2} payload={3}",
                    baseUrl,
                    path,
                    requiresAuth,
                    SanitizeProjectXJsonForLog(json)));

            try
            {
                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    if (requiresAuth && !string.IsNullOrWhiteSpace(projectXSessionToken))
                        client.Headers[System.Net.HttpRequestHeader.Authorization] = "Bearer " + projectXSessionToken;

                    string response = client.UploadString(baseUrl + path, "POST", json);
                    if (alwaysLog)
                        LogProjectXDiscovery(string.Format(
                            "response | url={0}{1} body={2}",
                            baseUrl,
                            path,
                            SanitizeProjectXJsonForLog(response)));
                    else
                        LogDebug(string.Format(
                            "[ProjectX] response | url={0}{1} body={2}",
                            baseUrl,
                            path,
                            SanitizeProjectXJsonForLog(response)));
                    return response;
                }
            }
            catch (System.Net.WebException ex)
            {
                string errorBody = ReadWebExceptionResponse(ex);
                if (alwaysLog)
                    LogProjectXDiscovery(string.Format(
                        "request failed | url={0}{1} error={2} body={3}",
                        baseUrl,
                        path,
                        ex.Message,
                        SanitizeProjectXJsonForLog(errorBody)));
                else
                    LogDebug(string.Format(
                        "[ProjectX] request failed | url={0}{1} error={2} body={3}",
                        baseUrl,
                        path,
                        ex.Message,
                        SanitizeProjectXJsonForLog(errorBody)));
                return errorBody;
            }
            catch (Exception ex)
            {
                if (alwaysLog)
                    LogProjectXDiscovery(string.Format("request failed | url={0}{1} error={2}", baseUrl, path, ex.Message));
                else
                    LogDebug(string.Format("[ProjectX] request failed | url={0}{1} error={2}", baseUrl, path, ex.Message));
                return null;
            }
        }

        private string ReadWebExceptionResponse(System.Net.WebException ex)
        {
            if (ex == null || ex.Response == null)
                return null;

            try
            {
                using (var stream = ex.Response.GetResponseStream())
                {
                    if (stream == null)
                        return null;
                    using (var reader = new System.IO.StreamReader(stream))
                        return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private string SanitizeProjectXJsonForLog(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            string sanitized = json;
            sanitized = RedactProjectXJsonValue(sanitized, "apiKey");
            sanitized = RedactProjectXJsonValue(sanitized, "loginKey");
            sanitized = RedactProjectXJsonValue(sanitized, "token");
            sanitized = RedactProjectXJsonValue(sanitized, "newToken");
            return sanitized;
        }

        private string RedactProjectXJsonValue(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return json ?? string.Empty;

            return Regex.Replace(
                json,
                "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"[^\"]*\"",
                "\"" + key + "\":\"***\"");
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
                return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetJsonLong(string json, string key, out long value)
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
                return TryConvertToLong(raw, out value);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetJsonBool(string json, string key, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(json);
                object raw;
                if (data == null || !data.TryGetValue(key, out raw) || raw == null)
                    return false;
                return TryConvertToBool(raw, out value);
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<ProjectXAccountInfo> ExtractProjectXAccounts(string json)
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
            if (data == null || !data.TryGetValue("accounts", out raw) || raw == null)
                yield break;

            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null)
                    continue;

                object idObj;
                int id;
                if (!dict.TryGetValue("id", out idObj) || !TryConvertToInt(idObj, out id) || id <= 0)
                    continue;

                object nameObj;
                object canTradeObj;
                object isVisibleObj;
                bool canTrade;
                bool isVisible;

                dict.TryGetValue("name", out nameObj);
                dict.TryGetValue("canTrade", out canTradeObj);
                dict.TryGetValue("isVisible", out isVisibleObj);
                TryConvertToBool(canTradeObj, out canTrade);
                TryConvertToBool(isVisibleObj, out isVisible);

                yield return new ProjectXAccountInfo
                {
                    Id = id,
                    Name = nameObj != null ? nameObj.ToString() : string.Empty,
                    CanTrade = canTrade,
                    IsVisible = isVisible
                };
            }
        }

        private IEnumerable<ProjectXContractInfo> ExtractProjectXContracts(string json)
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
            if (data == null || !data.TryGetValue("contracts", out raw) || raw == null)
                yield break;

            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var dict = item as Dictionary<string, object>;
                if (dict == null)
                    continue;

                object idObj;
                if (!dict.TryGetValue("id", out idObj) || idObj == null)
                    continue;

                object nameObj;
                object descriptionObj;
                object symbolIdObj;
                object activeObj;
                bool activeContract;

                dict.TryGetValue("name", out nameObj);
                dict.TryGetValue("description", out descriptionObj);
                dict.TryGetValue("symbolId", out symbolIdObj);
                dict.TryGetValue("activeContract", out activeObj);
                TryConvertToBool(activeObj, out activeContract);

                yield return new ProjectXContractInfo
                {
                    Id = idObj.ToString(),
                    Name = nameObj != null ? nameObj.ToString() : string.Empty,
                    Description = descriptionObj != null ? descriptionObj.ToString() : string.Empty,
                    SymbolId = symbolIdObj != null ? symbolIdObj.ToString() : string.Empty,
                    ActiveContract = activeContract
                };
            }
        }

        private bool TryConvertToInt(object raw, out int value)
        {
            value = 0;
            if (raw == null)
                return false;

            if (raw is int)
            {
                value = (int)raw;
                return true;
            }

            if (raw is long)
            {
                long longValue = (long)raw;
                if (longValue < int.MinValue || longValue > int.MaxValue)
                    return false;
                value = (int)longValue;
                return true;
            }

            if (raw is decimal)
            {
                value = (int)(decimal)raw;
                return true;
            }

            if (raw is double)
            {
                value = (int)(double)raw;
                return true;
            }

            return int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToLong(object raw, out long value)
        {
            value = 0;
            if (raw == null)
                return false;

            if (raw is int)
            {
                value = (int)raw;
                return true;
            }

            if (raw is long)
            {
                value = (long)raw;
                return true;
            }

            if (raw is decimal)
            {
                decimal decimalValue = (decimal)raw;
                if (decimalValue < long.MinValue || decimalValue > long.MaxValue)
                    return false;
                value = (long)decimalValue;
                return true;
            }

            if (raw is double)
            {
                double doubleValue = (double)raw;
                if (doubleValue < long.MinValue || doubleValue > long.MaxValue)
                    return false;
                value = (long)doubleValue;
                return true;
            }

            return long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private bool TryConvertToBool(object raw, out bool value)
        {
            value = false;
            if (raw == null)
                return false;

            if (raw is bool)
            {
                value = (bool)raw;
                return true;
            }

            return bool.TryParse(raw.ToString(), out value);
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

            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var dict = item as Dictionary<string, object>;
                if (dict != null)
                    yield return dict;
            }
        }

        private IEnumerable<Dictionary<string, object>> ExtractProjectXPositions(string json)
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
            if (data == null || !data.TryGetValue("positions", out raw) || raw == null)
                yield break;

            var items = raw as System.Collections.IEnumerable;
            if (items == null)
                yield break;

            foreach (var item in items)
            {
                var dict = item as Dictionary<string, object>;
                if (dict != null)
                    yield return dict;
            }
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
        [Range(0, 23)]
        [Display(Name = "Skip Start – Hour (EST)",
                 Description = "Set start/end both to 00:00 to disable skip window.",
                 GroupName = "01 - Common: Time Filters", Order = 3)]
        public int SkipStartHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Skip Start – Minute",
                 GroupName = "01 - Common: Time Filters", Order = 4)]
        public int SkipStartMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, 23)]
        [Display(Name = "Skip End – Hour (EST)",
                 GroupName = "01 - Common: Time Filters", Order = 5)]
        public int SkipEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Skip End – Minute",
                 GroupName = "01 - Common: Time Filters", Order = 6)]
        public int SkipEndMin { get; set; }

        [NinjaScriptProperty]
        [Range(9, 18)]
        [Display(Name = "Force Flatten – Hour (EST)",
                 GroupName = "01 - Common: Time Filters", Order = 7)]
        public int ForceFlatHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Force Flatten – Minute",
                 GroupName = "01 - Common: Time Filters", Order = 8)]
        public int ForceFlatMin { get; set; }

        [NinjaScriptProperty]
        [Range(9, 23)]
        [Display(Name = "Session Lines End – Hour (EST)",
                 Description = "Visual end time of OR / Target / Trigger lines",
                 GroupName = "01 - Common: Time Filters", Order = 9)]
        public int SessionEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Session Lines End – Minute",
                 GroupName = "01 - Common: Time Filters", Order = 10)]
        public int SessionEndMin { get; set; }

        // ════════════════════════════════════════════════════════════════════
        //  GROUP 02  –  COMMON : SESSION FILTERS
        // ════════════════════════════════════════════════════════════════════

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts",
                 Description = "Single shared contracts setting used for all buckets and both directions.",
                 GroupName = "02 - Common: Session Filters", Order = 0)]
        public int CommonContracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Alternating Direction",
                 Description = "After a WIN on either side, only allow the opposite direction next",
                 GroupName = "02 - Common: Session Filters", Order = 1)]
        public bool AlternatingEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Re-Entry After Loss",
                 Description = "After a genuine stop-loss, re-arm once price closes back through OR (Short: Close < OR Low | Long: Close > OR High). BE scratches never re-arm.",
                 GroupName = "02 - Common: Session Filters", Order = 2)]
        public bool ReEntryEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AGGRESSIVE MODE",
                 Description = "Enabled = AGGRESSIVE MODE: cancel limit orders if price reaches target before entry fills. Disabled = CALM MODE: skip Section K and let orders sit until filled, session end, or skip window.",
                 GroupName = "02 - Common: Session Filters", Order = 3)]
        public bool EnableTargetCancel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Confirmation",
                 Description = "Show a Yes/No confirmation popup before each new long/short entry.",
                 GroupName = "13. Risk", Order = 2)]
        public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Debug Logging",
                 Description = "Enable diagnostic output to the NinjaScript Output window.",
                 GroupName = "02 - Common: Session Filters", Order = 5)]
        public bool DebugLogging { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show OR Lines",
                 Description = "Draw OR high/low chart lines.",
                 GroupName = "02 - Common: Session Filters", Order = 6)]
        public bool ShowORLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Skip Time",
                 Description = "Enable skip time entry blocking between Skip Start and Skip End.",
                 GroupName = "02 - Common: Session Filters", Order = 7)]
        public bool UseSkipTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close At Skip Start",
                 Description = "If true, flatten open position when skip window begins.",
                 GroupName = "02 - Common: Session Filters", Order = 8)]
        public bool CloseAtSkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close At News Start",
                 Description = "If true, flatten open position when news skip window begins.",
                 GroupName = "02 - Common: Session Filters", Order = 9)]
        public bool CloseAtNewsStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip",
                 Description = "Infobox news rows: show listed 14:00 news events for the current week.",
                 GroupName = "02 - Common: Session Filters", Order = 10)]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60)]
        [Display(Name = "News Block Minutes",
                 Description = "Used for news row fade timing in infobox.",
                 GroupName = "02 - Common: Session Filters", Order = 11)]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL",
                 Description = "HTTP endpoint for TradersPost order webhooks. Leave empty to disable TradersPost webhooks.",
                 GroupName = "12. Webhooks", Order = 0)]
        public string WebhookUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Ticker Override",
                 Description = "Optional TradersPost ticker/instrument name override. Leave empty to use the chart instrument automatically.",
                 GroupName = "12. Webhooks", Order = 1)]
        public string WebhookTickerOverride { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Account Balance",
                 Description = "When net liquidation reaches or exceeds this value, entries are blocked and open positions are flattened. 0 disables.",
                 GroupName = "13. Risk", Order = 0)]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Provider",
                 Description = "Select webhook target: TradersPost or ProjectX.",
                 GroupName = "12. Webhooks", Order = 2)]
        public WebhookProvider WebhookProviderType { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX API Base URL",
                 Description = "ProjectX gateway base URL. Leave the default ProjectX gateway URL or paste your firm-specific endpoint.",
                 GroupName = "12. Webhooks", Order = 3)]
        public string ProjectXApiBaseUrl { get; set; }

        [Browsable(false)]
        public bool ProjectXTradeAllAccounts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Username",
                 Description = "ProjectX login username for direct ProjectX order routing.",
                 GroupName = "12. Webhooks", Order = 5)]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX API Key",
                 Description = "ProjectX API key used together with the ProjectX username.",
                 GroupName = "12. Webhooks", Order = 6)]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Accounts",
                 Description = "Comma-separated ProjectX account ids or exact account names.",
                 GroupName = "12. Webhooks", Order = 7)]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Contract ID",
                 Description = "Hidden optional override for support/debug use only.",
                 GroupName = "12. Webhooks", Order = 8)]
        public string ProjectXContractId { get; set; }

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

        [Browsable(false)]
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

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max SL Points (cap)",
                 Description = "Maximum SL distance in points from entry. Applies to all methods except Fixed. Set high to effectively disable.",
                 GroupName = "06 - Long B1: Stop Loss", Order = 5)]
        public double LongB1MaxSLPts { get; set; }

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

        [Browsable(false)]
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

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max SL Points (cap)",
                 Description = "Maximum SL distance in points from entry. Applies to all methods except Fixed. Set high to effectively disable.",
                 GroupName = "11 - Long B2: Stop Loss", Order = 5)]
        public double LongB2MaxSLPts { get; set; }

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

        [Browsable(false)]
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

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max SL Points (cap)",
                 Description = "Maximum SL distance in points from entry. Applies to all methods except Fixed. Set high to effectively disable.",
                 GroupName = "16 - Long B3: Stop Loss", Order = 5)]
        public double LongB3MaxSLPts { get; set; }

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

        [Browsable(false)]
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

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max SL Points (cap)",
                 Description = "Maximum SL distance in points from entry. Applies to all methods except Fixed. Set high to effectively disable.",
                 GroupName = "21 - Short B1: Stop Loss", Order = 5)]
        public double ShortB1MaxSLPts { get; set; }

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

        [Browsable(false)]
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

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max SL Points (cap)",
                 Description = "Maximum SL distance in points from entry. Applies to all methods except Fixed. Set high to effectively disable.",
                 GroupName = "26 - Short B2: Stop Loss", Order = 5)]
        public double ShortB2MaxSLPts { get; set; }

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

        [Browsable(false)]
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

        [NinjaScriptProperty]
        [Range(1.0, 5000.0)]
        [Display(Name = "Max SL Points (cap)",
                 Description = "Maximum SL distance in points from entry. Applies to all methods except Fixed. Set high to effectively disable.",
                 GroupName = "31 - Short B3: Stop Loss", Order = 5)]
        public double ShortB3MaxSLPts { get; set; }

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
