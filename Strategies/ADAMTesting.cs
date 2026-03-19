#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class ADAMTesting : Strategy
    {
        private const string HeartbeatStrategyName = "ADAMTesting";

        public ADAMTesting()
        {
            VendorLicense(1175);
        }

        public enum TPModeEnum { FixedTicks, ORMultiple }
        public enum SLModeEnum { FixedTicks, ORMultiple }
        public enum BETriggerModeEnum { FixedTicks, ORMultiple }
        public enum WebhookProvider { TradersPost, ProjectX }

        #region Variables
        private double orHigh = double.MinValue;
        private double orLow = double.MaxValue;
        private bool orSet = false;
        
        private double longEntryLevel = 0;
        private double shortEntryLevel = 0;
        
        private bool canTakeNewEntry = true;
        private bool priceReturnedToOR = true;
        private double entryPrice = 0;
        
        private double pendingStopPrice = 0;
        private int pendingTargetTicks = 0;
        
        private double currentStopPrice = 0;
        private double currentTargetPrice = 0;
        private int currentTargetTicks = 0;
        private bool isInSession = false;
        
        private int sessionTradeCountLong = 0;
        private int sessionTradeCountShort = 0;
        private int sessionTradeCountTotal = 0;
        
        private DateTime lastTradingDay = DateTime.MinValue;
        private DateTime sessionStart;
        private DateTime sessionEnd;
        
        private bool wasInPosition = false;
        private bool noBucketMatched = false;
        
        private double sessionPnLLong = 0;
        private double sessionPnLShort = 0;
        private double sessionPnLTotal = 0;
        private double sessionLossLong = 0;
        private double sessionLossShort = 0;
        private double sessionLossTotal = 0;
        
        private bool maxLossLongReached = false;
        private bool maxLossShortReached = false;
        private bool maxLossTotalReached = false;
        private bool maxProfitLongReached = false;
        private bool maxProfitShortReached = false;
        private bool maxProfitTotalReached = false;
        private bool maxAccountLimitHit;
        private string webhookUrl = string.Empty;
        private string webhookTickerOverride = string.Empty;
        private bool debugLogging = false;
        private bool isConfiguredTimeframeValid = true;
        private bool isConfiguredInstrumentValid = true;
        private bool timeframePopupShown;
        private bool instrumentPopupShown;
        private string projectXSessionToken;
        private DateTime projectXTokenAcquiredUtc = Core.Globals.MinDate;
        private int? projectXLastOrderId;
        private string projectXLastOrderContractId;
        
        private MarketPosition lastTradeDirection = MarketPosition.Flat;
        
        private bool beTriggered = false;
        private double beNewStopPrice = 0;
        private bool bracketOrdersPlaced = false;
        private string currentEntrySignal = string.Empty;
        private int entrySignalSequence = 0;
        private string tradeLineTagPrefix = string.Empty;
        private int tradeLineTagCounter = 0;
        private bool tradeLinesActive = false;
        private bool tradeLineHasTp = false;
        private int tradeLineSignalBar = -1;
        private int tradeLineExitBar = -1;
        private double tradeLineEntryPrice = 0.0;
        private double tradeLineTpPrice = 0.0;
        private double tradeLineSlPrice = 0.0;
        
        private int activeBucketL = 0;
        private int activeBucketS = 0;
        private StrategyHeartbeatReporter heartbeatReporter;
        

        // Active parameters for matched Long bucket
        private bool activeL_Enabled = false;
        private int activeL_ORMin = 0;
        private int activeL_ORMax = 0;
        private int activeL_BreakoutTicks = 0;
        private int activeL_FirstTradeOffset = 0;
        private int activeL_TradeWindowStart = 0;
        private SLModeEnum activeL_StopLossMode = default(SLModeEnum);
        private int activeL_StopLossTicks = 0;
        private double activeL_StopLossORMultiple = 0;
        private TPModeEnum activeL_TakeProfitMode = default(TPModeEnum);
        private int activeL_TakeProfitTicks = 0;
        private double activeL_TakeProfitORMultiple = 0;
        private int activeL_MaxTrades = 0;
        private int activeL_MaxTradesTotal = 0;
        private int activeL_MaxSessionLoss = 0;
        private int activeL_MaxSessionProfit = 0;
        private bool activeL_BEEnabled = false;
        private BETriggerModeEnum activeL_BETriggerMode = default(BETriggerModeEnum);
        private int activeL_BETriggerTicks = 0;
        private double activeL_BETriggerORMultiple = 0;
        private int activeL_BEOffsetTicks = 0;
        // Active parameters for matched Short bucket
        private bool activeS_Enabled = false;
        private int activeS_ORMin = 0;
        private int activeS_ORMax = 0;
        private int activeS_BreakoutTicks = 0;
        private int activeS_FirstTradeOffset = 0;
        private int activeS_TradeWindowStart = 0;
        private SLModeEnum activeS_StopLossMode = default(SLModeEnum);
        private int activeS_StopLossTicks = 0;
        private double activeS_StopLossORMultiple = 0;
        private TPModeEnum activeS_TakeProfitMode = default(TPModeEnum);
        private int activeS_TakeProfitTicks = 0;
        private double activeS_TakeProfitORMultiple = 0;
        private int activeS_MaxTrades = 0;
        private int activeS_MaxTradesTotal = 0;
        private int activeS_MaxSessionLoss = 0;
        private int activeS_MaxSessionProfit = 0;
        private bool activeS_BEEnabled = false;
        private BETriggerModeEnum activeS_BETriggerMode = default(BETriggerModeEnum);
        private int activeS_BETriggerTicks = 0;
        private double activeS_BETriggerORMultiple = 0;
        private int activeS_BEOffsetTicks = 0;

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
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(255, 0x1E, 0x90, 0xFF);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);
        private static readonly Brush PassedNewsRowBrush = CreateFrozenBrush(30, 211, 211, 211);

        #endregion

        #region Parameters
        
        // ==================== COMMON PARAMETERS ====================
        
        [NinjaScriptProperty]
        [Range(9, 16)]
        [Display(Name = "Trade Window End Hour", Description = "Hour (EST) to stop taking NEW trades", Order = 1, GroupName = "1. Common Parameters")]
        public int TradeWindowEndHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Trade Window End Minute", Description = "Minute to stop taking NEW trades", Order = 2, GroupName = "1. Common Parameters")]
        public int TradeWindowEndMinute { get; set; }

        [NinjaScriptProperty]
        [Range(9, 16)]
        [Display(Name = "Cut-Off Hour", Description = "Hour (EST) to close any open position", Order = 3, GroupName = "1. Common Parameters")]
        public int CutOffHour { get; set; }

        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Cut-Off Minute", Description = "Minute to close any open position", Order = 4, GroupName = "1. Common Parameters")]
        public int CutOffMinute { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contract Quantity", Description = "Number of contracts to trade", Order = 5, GroupName = "1. Common Parameters")]
        public int ContractQuantity { get; set; }
        
        [NinjaScriptProperty]
        [Range(9, 16)]
        [Display(Name = "Forced Close Hour", Description = "Hour (EST) to force close ALL positions", Order = 6, GroupName = "1. Common Parameters")]
        public int ForcedCloseHour { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 59)]
        [Display(Name = "Forced Close Minute", Description = "Minute to force close ALL positions", Order = 7, GroupName = "1. Common Parameters")]
        public int ForcedCloseMinute { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Loss Total (Ticks)", Description = "Max total session loss - stops ALL trading (0=disabled)", Order = 8, GroupName = "1. Common Parameters")]
        public int MaxSessionLossTotal { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Profit Total (Ticks)", Description = "Max total session profit - stops ALL trading (0=disabled)", Order = 9, GroupName = "1. Common Parameters")]
        public int MaxSessionProfitTotal { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Description = "Block entries inside minutes before/after listed 14:00 news events.", Order = 10, GroupName = "1. Common Parameters")]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(1, 120)]
        [Display(Name = "News Block Minutes", Description = "Minutes blocked before and after each listed news timestamp.", Order = 11, GroupName = "1. Common Parameters")]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Entry Confirmation", Description = "Show a Yes/No confirmation popup before each new long/short entry.", Order = 12, GroupName = "1. Common Parameters")]
        public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, double.MaxValue)]
        [Display(Name = "Max Account Balance", Description = "When net liquidation reaches or exceeds this value, entries are blocked and open positions are flattened. 0 disables.", Order = 13, GroupName = "1. Common Parameters")]
        public double MaxAccountBalance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL", Description = "HTTP endpoint for order webhooks. Leave empty to disable TradersPost webhooks.", Order = 0, GroupName = "2. Webhooks")]
        public string WebhookUrl
        {
            get { return webhookUrl ?? string.Empty; }
            set { webhookUrl = value ?? string.Empty; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Ticker Override", Description = "Optional TradersPost ticker/instrument name override. Leave empty to use the chart instrument automatically.", Order = 1, GroupName = "2. Webhooks")]
        public string WebhookTickerOverride
        {
            get { return webhookTickerOverride ?? string.Empty; }
            set { webhookTickerOverride = value ?? string.Empty; }
        }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "Webhook Provider", Description = "Select webhook target: TradersPost or ProjectX.", Order = 2, GroupName = "2. Webhooks")]
        public WebhookProvider WebhookProviderType { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Base URL", Description = "ProjectX gateway base URL.", Order = 3, GroupName = "2. Webhooks")]
        public string ProjectXApiBaseUrl { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Username", Description = "ProjectX login username.", Order = 4, GroupName = "2. Webhooks")]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX API Key", Description = "ProjectX login key.", Order = 5, GroupName = "2. Webhooks")]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Account ID", Description = "ProjectX account id used for order routing.", Order = 6, GroupName = "2. Webhooks")]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Browsable(false)]
        [Display(Name = "ProjectX Contract ID", Description = "ProjectX contract id (for example CON.F.US.DA6.M25).", Order = 7, GroupName = "2. Webhooks")]
        public string ProjectXContractId { get; set; }


        // ==================== BUCKET 1 LONG ====================

        [NinjaScriptProperty]
        [Display(Name = "Enabled", Description = "Enable LONG trading for Bucket 1", Order = 1, GroupName = "B1L. Bucket 1 Long")]
        public bool B1L_Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Min Ticks", Description = "Minimum OR size in ticks", Order = 2, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_ORMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Max Ticks", Description = "Maximum OR size in ticks", Order = 3, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_ORMax { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Breakout Ticks", Description = "Ticks above OR High for LONG entry", Order = 4, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_BreakoutTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "First Trade Offset (Ticks)", Description = "Reduce entry level by this many ticks for first trade only (0=disabled)", Order = 5, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_FirstTradeOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "Trade Window Start (Min after OR)", Description = "Minutes after OR to start allowing LONG trades", Order = 6, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Mode", Description = "Fixed ticks or OR multiple for stop loss", Order = 7, GroupName = "B1L. Bucket 1 Long")]
        public SLModeEnum B1L_StopLossMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss Ticks", Description = "Ticks from OR boundary for stop loss (FixedTicks mode)", Order = 8, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Stop Loss OR Multiple", Description = "OR range multiple for stop loss (ORMultiple mode)", Order = 9, GroupName = "B1L. Bucket 1 Long")]
        public double B1L_StopLossORMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit Mode", Description = "Fixed ticks or OR multiple for take profit", Order = 10, GroupName = "B1L. Bucket 1 Long")]
        public TPModeEnum B1L_TakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take Profit Ticks", Description = "Fixed ticks for take profit (FixedTicks mode)", Order = 11, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Take Profit OR Multiple", Description = "OR range multiple for take profit (ORMultiple mode)", Order = 12, GroupName = "B1L. Bucket 1 Long")]
        public double B1L_TakeProfitORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Trades", Description = "Max LONG trades per session (0=unlimited)", Order = 13, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_MaxTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Total", Description = "Max total trades (long+short) for this bucket", Order = 14, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_MaxTradesTotal { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Description = "Max loss from LONG trades (0=disabled)", Order = 15, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_MaxSessionLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Description = "Max profit from LONG trades (0=disabled)", Order = 16, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_MaxSessionProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break Even Enabled", Description = "Enable break-even for LONG trades", Order = 17, GroupName = "B1L. Bucket 1 Long")]
        public bool B1L_BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Mode", Description = "Trigger BE after fixed ticks or OR multiple profit", Order = 18, GroupName = "B1L. Bucket 1 Long")]
        public BETriggerModeEnum B1L_BETriggerMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Description = "Ticks in profit to trigger BE (FixedTicks mode)", Order = 19, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_BETriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "BE Trigger OR Multiple", Description = "OR multiple in profit to trigger BE (ORMultiple mode)", Order = 20, GroupName = "B1L. Bucket 1 Long")]
        public double B1L_BETriggerORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "Ticks above entry price for BE stop (0=exact entry)", Order = 21, GroupName = "B1L. Bucket 1 Long")]
        public int B1L_BEOffsetTicks { get; set; }

        // ==================== BUCKET 1 SHORT ====================

        [NinjaScriptProperty]
        [Display(Name = "Enabled", Description = "Enable SHORT trading for Bucket 1", Order = 1, GroupName = "B1S. Bucket 1 Short")]
        public bool B1S_Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Min Ticks", Description = "Minimum OR size in ticks", Order = 2, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_ORMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Max Ticks", Description = "Maximum OR size in ticks", Order = 3, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_ORMax { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Breakout Ticks", Description = "Ticks below OR Low for SHORT entry", Order = 4, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_BreakoutTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "First Trade Offset (Ticks)", Description = "Increase entry level by this many ticks for first trade only (0=disabled)", Order = 5, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_FirstTradeOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "Trade Window Start (Min after OR)", Description = "Minutes after OR to start allowing SHORT trades", Order = 6, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Mode", Description = "Fixed ticks or OR multiple for stop loss", Order = 7, GroupName = "B1S. Bucket 1 Short")]
        public SLModeEnum B1S_StopLossMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss Ticks", Description = "Ticks from OR boundary for stop loss (FixedTicks mode)", Order = 8, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Stop Loss OR Multiple", Description = "OR range multiple for stop loss (ORMultiple mode)", Order = 9, GroupName = "B1S. Bucket 1 Short")]
        public double B1S_StopLossORMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit Mode", Description = "Fixed ticks or OR multiple for take profit", Order = 10, GroupName = "B1S. Bucket 1 Short")]
        public TPModeEnum B1S_TakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take Profit Ticks", Description = "Fixed ticks for take profit (FixedTicks mode)", Order = 11, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Take Profit OR Multiple", Description = "OR range multiple for take profit (ORMultiple mode)", Order = 12, GroupName = "B1S. Bucket 1 Short")]
        public double B1S_TakeProfitORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Trades", Description = "Max SHORT trades per session (0=unlimited)", Order = 13, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_MaxTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Total", Description = "Max total trades (long+short) for this bucket", Order = 14, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_MaxTradesTotal { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Description = "Max loss from SHORT trades (0=disabled)", Order = 15, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_MaxSessionLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Description = "Max profit from SHORT trades (0=disabled)", Order = 16, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_MaxSessionProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break Even Enabled", Description = "Enable break-even for SHORT trades", Order = 17, GroupName = "B1S. Bucket 1 Short")]
        public bool B1S_BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Mode", Description = "Trigger BE after fixed ticks or OR multiple profit", Order = 18, GroupName = "B1S. Bucket 1 Short")]
        public BETriggerModeEnum B1S_BETriggerMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Description = "Ticks in profit to trigger BE (FixedTicks mode)", Order = 19, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_BETriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "BE Trigger OR Multiple", Description = "OR multiple in profit to trigger BE (ORMultiple mode)", Order = 20, GroupName = "B1S. Bucket 1 Short")]
        public double B1S_BETriggerORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "Ticks below entry price for BE stop (0=exact entry)", Order = 21, GroupName = "B1S. Bucket 1 Short")]
        public int B1S_BEOffsetTicks { get; set; }

        // ==================== BUCKET 2 LONG ====================

        [NinjaScriptProperty]
        [Display(Name = "Enabled", Description = "Enable LONG trading for Bucket 2", Order = 1, GroupName = "B2L. Bucket 2 Long")]
        public bool B2L_Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Min Ticks", Description = "Minimum OR size in ticks", Order = 2, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_ORMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Max Ticks", Description = "Maximum OR size in ticks", Order = 3, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_ORMax { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Breakout Ticks", Description = "Ticks above OR High for LONG entry", Order = 4, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_BreakoutTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "First Trade Offset (Ticks)", Description = "Reduce entry level by this many ticks for first trade only (0=disabled)", Order = 5, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_FirstTradeOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "Trade Window Start (Min after OR)", Description = "Minutes after OR to start allowing LONG trades", Order = 6, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Mode", Description = "Fixed ticks or OR multiple for stop loss", Order = 7, GroupName = "B2L. Bucket 2 Long")]
        public SLModeEnum B2L_StopLossMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss Ticks", Description = "Ticks from OR boundary for stop loss (FixedTicks mode)", Order = 8, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Stop Loss OR Multiple", Description = "OR range multiple for stop loss (ORMultiple mode)", Order = 9, GroupName = "B2L. Bucket 2 Long")]
        public double B2L_StopLossORMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit Mode", Description = "Fixed ticks or OR multiple for take profit", Order = 10, GroupName = "B2L. Bucket 2 Long")]
        public TPModeEnum B2L_TakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take Profit Ticks", Description = "Fixed ticks for take profit (FixedTicks mode)", Order = 11, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Take Profit OR Multiple", Description = "OR range multiple for take profit (ORMultiple mode)", Order = 12, GroupName = "B2L. Bucket 2 Long")]
        public double B2L_TakeProfitORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Trades", Description = "Max LONG trades per session (0=unlimited)", Order = 13, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_MaxTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Total", Description = "Max total trades (long+short) for this bucket", Order = 14, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_MaxTradesTotal { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Description = "Max loss from LONG trades (0=disabled)", Order = 15, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_MaxSessionLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Description = "Max profit from LONG trades (0=disabled)", Order = 16, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_MaxSessionProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break Even Enabled", Description = "Enable break-even for LONG trades", Order = 17, GroupName = "B2L. Bucket 2 Long")]
        public bool B2L_BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Mode", Description = "Trigger BE after fixed ticks or OR multiple profit", Order = 18, GroupName = "B2L. Bucket 2 Long")]
        public BETriggerModeEnum B2L_BETriggerMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Description = "Ticks in profit to trigger BE (FixedTicks mode)", Order = 19, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_BETriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "BE Trigger OR Multiple", Description = "OR multiple in profit to trigger BE (ORMultiple mode)", Order = 20, GroupName = "B2L. Bucket 2 Long")]
        public double B2L_BETriggerORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "Ticks above entry price for BE stop (0=exact entry)", Order = 21, GroupName = "B2L. Bucket 2 Long")]
        public int B2L_BEOffsetTicks { get; set; }

        // ==================== BUCKET 2 SHORT ====================

        [NinjaScriptProperty]
        [Display(Name = "Enabled", Description = "Enable SHORT trading for Bucket 2", Order = 1, GroupName = "B2S. Bucket 2 Short")]
        public bool B2S_Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Min Ticks", Description = "Minimum OR size in ticks", Order = 2, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_ORMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Max Ticks", Description = "Maximum OR size in ticks", Order = 3, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_ORMax { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Breakout Ticks", Description = "Ticks below OR Low for SHORT entry", Order = 4, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_BreakoutTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "First Trade Offset (Ticks)", Description = "Increase entry level by this many ticks for first trade only (0=disabled)", Order = 5, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_FirstTradeOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "Trade Window Start (Min after OR)", Description = "Minutes after OR to start allowing SHORT trades", Order = 6, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Mode", Description = "Fixed ticks or OR multiple for stop loss", Order = 7, GroupName = "B2S. Bucket 2 Short")]
        public SLModeEnum B2S_StopLossMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss Ticks", Description = "Ticks from OR boundary for stop loss (FixedTicks mode)", Order = 8, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Stop Loss OR Multiple", Description = "OR range multiple for stop loss (ORMultiple mode)", Order = 9, GroupName = "B2S. Bucket 2 Short")]
        public double B2S_StopLossORMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit Mode", Description = "Fixed ticks or OR multiple for take profit", Order = 10, GroupName = "B2S. Bucket 2 Short")]
        public TPModeEnum B2S_TakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take Profit Ticks", Description = "Fixed ticks for take profit (FixedTicks mode)", Order = 11, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Take Profit OR Multiple", Description = "OR range multiple for take profit (ORMultiple mode)", Order = 12, GroupName = "B2S. Bucket 2 Short")]
        public double B2S_TakeProfitORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Trades", Description = "Max SHORT trades per session (0=unlimited)", Order = 13, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_MaxTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Total", Description = "Max total trades (long+short) for this bucket", Order = 14, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_MaxTradesTotal { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Description = "Max loss from SHORT trades (0=disabled)", Order = 15, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_MaxSessionLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Description = "Max profit from SHORT trades (0=disabled)", Order = 16, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_MaxSessionProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break Even Enabled", Description = "Enable break-even for SHORT trades", Order = 17, GroupName = "B2S. Bucket 2 Short")]
        public bool B2S_BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Mode", Description = "Trigger BE after fixed ticks or OR multiple profit", Order = 18, GroupName = "B2S. Bucket 2 Short")]
        public BETriggerModeEnum B2S_BETriggerMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Description = "Ticks in profit to trigger BE (FixedTicks mode)", Order = 19, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_BETriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "BE Trigger OR Multiple", Description = "OR multiple in profit to trigger BE (ORMultiple mode)", Order = 20, GroupName = "B2S. Bucket 2 Short")]
        public double B2S_BETriggerORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "Ticks below entry price for BE stop (0=exact entry)", Order = 21, GroupName = "B2S. Bucket 2 Short")]
        public int B2S_BEOffsetTicks { get; set; }

        // ==================== BUCKET 3 LONG ====================

        [NinjaScriptProperty]
        [Display(Name = "Enabled", Description = "Enable LONG trading for Bucket 3", Order = 1, GroupName = "B3L. Bucket 3 Long")]
        public bool B3L_Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Min Ticks", Description = "Minimum OR size in ticks", Order = 2, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_ORMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Max Ticks", Description = "Maximum OR size in ticks", Order = 3, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_ORMax { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Breakout Ticks", Description = "Ticks above OR High for LONG entry", Order = 4, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_BreakoutTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "First Trade Offset (Ticks)", Description = "Reduce entry level by this many ticks for first trade only (0=disabled)", Order = 5, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_FirstTradeOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "Trade Window Start (Min after OR)", Description = "Minutes after OR to start allowing LONG trades", Order = 6, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Mode", Description = "Fixed ticks or OR multiple for stop loss", Order = 7, GroupName = "B3L. Bucket 3 Long")]
        public SLModeEnum B3L_StopLossMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss Ticks", Description = "Ticks from OR boundary for stop loss (FixedTicks mode)", Order = 8, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Stop Loss OR Multiple", Description = "OR range multiple for stop loss (ORMultiple mode)", Order = 9, GroupName = "B3L. Bucket 3 Long")]
        public double B3L_StopLossORMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit Mode", Description = "Fixed ticks or OR multiple for take profit", Order = 10, GroupName = "B3L. Bucket 3 Long")]
        public TPModeEnum B3L_TakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take Profit Ticks", Description = "Fixed ticks for take profit (FixedTicks mode)", Order = 11, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Take Profit OR Multiple", Description = "OR range multiple for take profit (ORMultiple mode)", Order = 12, GroupName = "B3L. Bucket 3 Long")]
        public double B3L_TakeProfitORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Trades", Description = "Max LONG trades per session (0=unlimited)", Order = 13, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_MaxTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Total", Description = "Max total trades (long+short) for this bucket", Order = 14, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_MaxTradesTotal { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Description = "Max loss from LONG trades (0=disabled)", Order = 15, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_MaxSessionLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Description = "Max profit from LONG trades (0=disabled)", Order = 16, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_MaxSessionProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break Even Enabled", Description = "Enable break-even for LONG trades", Order = 17, GroupName = "B3L. Bucket 3 Long")]
        public bool B3L_BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Mode", Description = "Trigger BE after fixed ticks or OR multiple profit", Order = 18, GroupName = "B3L. Bucket 3 Long")]
        public BETriggerModeEnum B3L_BETriggerMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Description = "Ticks in profit to trigger BE (FixedTicks mode)", Order = 19, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_BETriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "BE Trigger OR Multiple", Description = "OR multiple in profit to trigger BE (ORMultiple mode)", Order = 20, GroupName = "B3L. Bucket 3 Long")]
        public double B3L_BETriggerORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "Ticks above entry price for BE stop (0=exact entry)", Order = 21, GroupName = "B3L. Bucket 3 Long")]
        public int B3L_BEOffsetTicks { get; set; }

        // ==================== BUCKET 3 SHORT ====================

        [NinjaScriptProperty]
        [Display(Name = "Enabled", Description = "Enable SHORT trading for Bucket 3", Order = 1, GroupName = "B3S. Bucket 3 Short")]
        public bool B3S_Enabled { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Min Ticks", Description = "Minimum OR size in ticks", Order = 2, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_ORMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OR Max Ticks", Description = "Maximum OR size in ticks", Order = 3, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_ORMax { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Breakout Ticks", Description = "Ticks below OR Low for SHORT entry", Order = 4, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_BreakoutTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "First Trade Offset (Ticks)", Description = "Increase entry level by this many ticks for first trade only (0=disabled)", Order = 5, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_FirstTradeOffset { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "Trade Window Start (Min after OR)", Description = "Minutes after OR to start allowing SHORT trades", Order = 6, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_TradeWindowStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop Loss Mode", Description = "Fixed ticks or OR multiple for stop loss", Order = 7, GroupName = "B3S. Bucket 3 Short")]
        public SLModeEnum B3S_StopLossMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Stop Loss Ticks", Description = "Ticks from OR boundary for stop loss (FixedTicks mode)", Order = 8, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_StopLossTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Stop Loss OR Multiple", Description = "OR range multiple for stop loss (ORMultiple mode)", Order = 9, GroupName = "B3S. Bucket 3 Short")]
        public double B3S_StopLossORMultiple { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit Mode", Description = "Fixed ticks or OR multiple for take profit", Order = 10, GroupName = "B3S. Bucket 3 Short")]
        public TPModeEnum B3S_TakeProfitMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Take Profit Ticks", Description = "Fixed ticks for take profit (FixedTicks mode)", Order = 11, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_TakeProfitTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Take Profit OR Multiple", Description = "OR range multiple for take profit (ORMultiple mode)", Order = 12, GroupName = "B3S. Bucket 3 Short")]
        public double B3S_TakeProfitORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Trades", Description = "Max SHORT trades per session (0=unlimited)", Order = 13, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_MaxTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Max Trades Total", Description = "Max total trades (long+short) for this bucket", Order = 14, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_MaxTradesTotal { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Loss (Ticks)", Description = "Max loss from SHORT trades (0=disabled)", Order = 15, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_MaxSessionLoss { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "Max Session Profit (Ticks)", Description = "Max profit from SHORT trades (0=disabled)", Order = 16, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_MaxSessionProfit { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Break Even Enabled", Description = "Enable break-even for SHORT trades", Order = 17, GroupName = "B3S. Bucket 3 Short")]
        public bool B3S_BEEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BE Trigger Mode", Description = "Trigger BE after fixed ticks or OR multiple profit", Order = 18, GroupName = "B3S. Bucket 3 Short")]
        public BETriggerModeEnum B3S_BETriggerMode { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "BE Trigger Ticks", Description = "Ticks in profit to trigger BE (FixedTicks mode)", Order = 19, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_BETriggerTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "BE Trigger OR Multiple", Description = "OR multiple in profit to trigger BE (ORMultiple mode)", Order = 20, GroupName = "B3S. Bucket 3 Short")]
        public double B3S_BETriggerORMultiple { get; set; }

        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "BE Offset Ticks", Description = "Ticks below entry price for BE stop (0=exact entry)", Order = 21, GroupName = "B3S. Bucket 3 Short")]
        public int B3S_BEOffsetTicks { get; set; }

        
        #endregion


        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"30s ORB Strategy";
                Name = "ADAMTesting";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 300;
                DefaultQuantity = 1;
                IsUnmanaged = false;
                IsOverlay = true;
                StartBehavior = StartBehavior.WaitUntilFlat;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = false;
                
                // Common
                TradeWindowEndHour = 14;
                TradeWindowEndMinute = 11;
                CutOffHour = 15;
                CutOffMinute = 59;
                ContractQuantity = 1;
                ForcedCloseHour = 15;
                ForcedCloseMinute = 18;
                MaxSessionLossTotal = 1400;
                MaxSessionProfitTotal = 739;
                UseNewsSkip = true;
                NewsBlockMinutes = 1;
                RequireEntryConfirmation = false;
                MaxAccountBalance = 0.0;
                WebhookUrl = string.Empty;
                WebhookTickerOverride = string.Empty;
                WebhookProviderType = WebhookProvider.TradersPost;
                ProjectXApiBaseUrl = "https://gateway-api-demo.s2f.projectx.com";
                ProjectXUsername = string.Empty;
                ProjectXApiKey = string.Empty;
                ProjectXAccountId = string.Empty;
                ProjectXContractId = string.Empty;
                debugLogging = true;
                

                // Bucket 1 Long
                B1L_Enabled = true;
                B1L_ORMin = 0;
                B1L_ORMax = 125;
                B1L_BreakoutTicks = 0;
                B1L_FirstTradeOffset = 36;
                B1L_TradeWindowStart = 21;
                B1L_StopLossMode = SLModeEnum.ORMultiple;
                B1L_StopLossTicks = 143;
                B1L_StopLossORMultiple = 1.93;
                B1L_TakeProfitMode = TPModeEnum.ORMultiple;
                B1L_TakeProfitTicks = 567;
                B1L_TakeProfitORMultiple = 5.07;
                B1L_MaxTrades = 3;
                B1L_MaxTradesTotal = 9;
                B1L_MaxSessionLoss = 0;
                B1L_MaxSessionProfit = 0;
                B1L_BEEnabled = true;
                B1L_BETriggerMode = BETriggerModeEnum.FixedTicks;
                B1L_BETriggerTicks = 466;
                B1L_BETriggerORMultiple = 4.07;
                B1L_BEOffsetTicks = 128;

                // Bucket 1 Short
                B1S_Enabled = true;
                B1S_ORMin = 0;
                B1S_ORMax = 125;
                B1S_BreakoutTicks = 25;
                B1S_FirstTradeOffset = 11;
                B1S_TradeWindowStart = 21;
                B1S_StopLossMode = SLModeEnum.FixedTicks;
                B1S_StopLossTicks = 194;
                B1S_StopLossORMultiple = 1.8;
                B1S_TakeProfitMode = TPModeEnum.ORMultiple;
                B1S_TakeProfitTicks = 620;
                B1S_TakeProfitORMultiple = 5.75;
                B1S_MaxTrades = 3;
                B1S_MaxTradesTotal = 9;
                B1S_MaxSessionLoss = 0;
                B1S_MaxSessionProfit = 0;
                B1S_BEEnabled = true;
                B1S_BETriggerMode = BETriggerModeEnum.FixedTicks;
                B1S_BETriggerTicks = 605;
                B1S_BETriggerORMultiple = 4.0;
                B1S_BEOffsetTicks = 10;

                // Bucket 2 Long
                B2L_Enabled = true;
                B2L_ORMin = 126;
                B2L_ORMax = 178;
                B2L_BreakoutTicks = 9;
                B2L_FirstTradeOffset = 23;
                B2L_TradeWindowStart = 22;
                B2L_StopLossMode = SLModeEnum.FixedTicks;
                B2L_StopLossTicks = 186;
                B2L_StopLossORMultiple = 1.3;
                B2L_TakeProfitMode = TPModeEnum.ORMultiple;
                B2L_TakeProfitTicks = 500;
                B2L_TakeProfitORMultiple = 5.04;
                B2L_MaxTrades = 3;
                B2L_MaxTradesTotal = 9;
                B2L_MaxSessionLoss = 0;
                B2L_MaxSessionProfit = 0;
                B2L_BEEnabled = true;
                B2L_BETriggerMode = BETriggerModeEnum.FixedTicks;
                B2L_BETriggerTicks = 405;
                B2L_BETriggerORMultiple = 4.61;
                B2L_BEOffsetTicks = 37;

                // Bucket 2 Short
                B2S_Enabled = true;
                B2S_ORMin = 126;
                B2S_ORMax = 184;
                B2S_BreakoutTicks = 25;
                B2S_FirstTradeOffset = 1;
                B2S_TradeWindowStart = 23;
                B2S_StopLossMode = SLModeEnum.FixedTicks;
                B2S_StopLossTicks = 180;
                B2S_StopLossORMultiple = 2.0;
                B2S_TakeProfitMode = TPModeEnum.ORMultiple;
                B2S_TakeProfitTicks = 620;
                B2S_TakeProfitORMultiple = 5.25;
                B2S_MaxTrades = 3;
                B2S_MaxTradesTotal = 9;
                B2S_MaxSessionLoss = 0;
                B2S_MaxSessionProfit = 0;
                B2S_BEEnabled = true;
                B2S_BETriggerMode = BETriggerModeEnum.FixedTicks;
                B2S_BETriggerTicks = 625;
                B2S_BETriggerORMultiple = 4.87;
                B2S_BEOffsetTicks = 31;

                // Bucket 3 Long
                B3L_Enabled = true;
                B3L_ORMin = 179;
                B3L_ORMax = 233;
                B3L_BreakoutTicks = 1;
                B3L_FirstTradeOffset = 14;
                B3L_TradeWindowStart = 40;
                B3L_StopLossMode = SLModeEnum.FixedTicks;
                B3L_StopLossTicks = 194;
                B3L_StopLossORMultiple = 0.95;
                B3L_TakeProfitMode = TPModeEnum.ORMultiple;
                B3L_TakeProfitTicks = 396;
                B3L_TakeProfitORMultiple = 2.15;
                B3L_MaxTrades = 3;
                B3L_MaxTradesTotal = 9;
                B3L_MaxSessionLoss = 0;
                B3L_MaxSessionProfit = 0;
                B3L_BEEnabled = true;
                B3L_BETriggerMode = BETriggerModeEnum.FixedTicks;
                B3L_BETriggerTicks = 344;
                B3L_BETriggerORMultiple = 4.61;
                B3L_BEOffsetTicks = 100;

                // Bucket 3 Short
                B3S_Enabled = true;
                B3S_ORMin = 185;
                B3S_ORMax = 280;
                B3S_BreakoutTicks = 25;
                B3S_FirstTradeOffset = 27;
                B3S_TradeWindowStart = 51;
                B3S_StopLossMode = SLModeEnum.FixedTicks;
                B3S_StopLossTicks = 216;
                B3S_StopLossORMultiple = 2.0;
                B3S_TakeProfitMode = TPModeEnum.ORMultiple;
                B3S_TakeProfitTicks = 620;
                B3S_TakeProfitORMultiple = 3.8;
                B3S_MaxTrades = 3;
                B3S_MaxTradesTotal = 9;
                B3S_MaxSessionLoss = 0;
                B3S_MaxSessionProfit = 0;
                B3S_BEEnabled = true;
                B3S_BETriggerMode = BETriggerModeEnum.FixedTicks;
                B3S_BETriggerTicks = 349;
                B3S_BETriggerORMultiple = 4.87;
                B3S_BEOffsetTicks = 79;

            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Second, 30);
            }
            else if (State == State.DataLoaded)
            {
                ValidateRequiredPrimaryTimeframe(30);
                ValidateRequiredPrimaryInstrument();
                EnsureNewsDatesInitialized();
                heartbeatReporter = new StrategyHeartbeatReporter(
                    HeartbeatStrategyName,
                    System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv"));
                projectXSessionToken = null;
                projectXTokenAcquiredUtc = Core.Globals.MinDate;
                projectXLastOrderId = null;
                projectXLastOrderContractId = null;
                LogDebug("ADAM30sORBot_v3.03 loaded | TickSize=" + TickSize + " | Instrument=" + Instrument.FullName);
                LogDebug(String.Format("  Cut-Off: {0}:{1:D2} | Forced Close: {2}:{3:D2}", 
                    CutOffHour, CutOffMinute, ForcedCloseHour, ForcedCloseMinute));
                for (int b = 1; b <= 3; b++)
                {
                    bool enL = b == 1 ? B1L_Enabled : b == 2 ? B2L_Enabled : B3L_Enabled;
                    bool enS = b == 1 ? B1S_Enabled : b == 2 ? B2S_Enabled : B3S_Enabled;
                    int mnL = b == 1 ? B1L_ORMin : b == 2 ? B2L_ORMin : B3L_ORMin;
                    int mxL = b == 1 ? B1L_ORMax : b == 2 ? B2L_ORMax : B3L_ORMax;
                    int mnS = b == 1 ? B1S_ORMin : b == 2 ? B2S_ORMin : B3S_ORMin;
                    int mxS = b == 1 ? B1S_ORMax : b == 2 ? B2S_ORMax : B3S_ORMax;
                    LogDebug(String.Format("  Bucket {0}: L:{1}({2}-{3}t) S:{4}({5}-{6}t)", 
                        b, enL ? "ON" : "OFF", mnL, mxL, enS ? "ON" : "OFF", mnS, mxS));
                }
            }
            else if (State == State.Realtime)
            {
                if (heartbeatReporter != null)
                    heartbeatReporter.Start();
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


        protected override void OnBarUpdate()
        {
            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
            {
                CancelAllOrders();
                ExitAllPositions("InvalidConfiguration");
                return;
            }

            if (CurrentBar < 20) return;
            if (BarsInProgress != 0) return;

            DateTime tradingDay = Time[0].Date;
            bool isNewSession = Bars.IsFirstBarOfSession || tradingDay != lastTradingDay;
            TimeSpan currentTimeOfDay = Time[0].TimeOfDay;
            
            if (currentTimeOfDay.Hours == 9 && currentTimeOfDay.Minutes == 30 && !orSet && tradingDay != lastTradingDay)
                isNewSession = true;
            
            if (isNewSession && tradingDay != lastTradingDay)
            {
                LogDebug("");
                LogDebug(String.Format("========== SESSION RESET: {0} ==========", tradingDay.ToString("yyyy-MM-dd")));
                ResetForNewSession(tradingDay);
            }

            if (ShouldAccountBalanceExit())
            {
                ExitAllPositions("MaxBalance");
                return;
            }

            ExitIfSessionEnded();
            if (IsAfterSessionEnd(Time[0]))
                return;
            UpdateTradeLines();

            // Set OR
            if (!orSet)
            {
                TimeSpan barTime = Time[0].TimeOfDay;
                if (barTime.Hours == 9 && barTime.Minutes == 30 && barTime.Seconds == 30)
                {
                    orHigh = High[0];
                    orLow = Low[0];
                    double rangeInTicks = Math.Round((orHigh - orLow) / TickSize);
                    
                    LogDebug(String.Format("{0} | *** OR BAR CLOSED *** High={1:F2} Low={2:F2} Range={3:F0}t",
                        Time[0].ToString("HH:mm:ss"), orHigh, orLow, rangeInTicks));
                    
                    int matchedLong = FindMatchingBucketLong((int)rangeInTicks);
                    int matchedShort = FindMatchingBucketShort((int)rangeInTicks);
                    
                    if (matchedLong == 0 && matchedShort == 0)
                    {
                        noBucketMatched = true;
                        LogDebug(String.Format("  *** NO BUCKET MATCHED *** OR={0}t - NO TRADING TODAY ***", rangeInTicks));
                        // Still set orSet so panel shows OR info
                        orSet = true;
                        UpdateInfoPanel();
                        return;
                    }
                    
                    if (rangeInTicks >= 1.0)
                    {
                        orSet = true;
                        isInSession = true;
                        noBucketMatched = false;
                        
                        if (matchedLong > 0)
                            LoadBucketLongParameters(matchedLong);
                        if (matchedShort > 0)
                            LoadBucketShortParameters(matchedShort);
                        
                        longEntryLevel = orHigh + (activeL_BreakoutTicks * TickSize);
                        shortEntryLevel = orLow - (activeS_BreakoutTicks * TickSize);
                        
                        DrawSessionLines();
                        UpdateInfoPanel();
                        
                        LogDebug(String.Format("  *** OR SET *** Long B{0}>{1:F2} | Short B{2}<{3:F2} - READY ***",
                            activeBucketL, longEntryLevel, activeBucketS, shortEntryLevel));
                    }
                }
                return;
            }

            // Forced close
            TimeSpan forcedCloseTime = new TimeSpan(ForcedCloseHour, ForcedCloseMinute, 0);
            if (currentTimeOfDay >= forcedCloseTime)
            {
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    LogDebug(String.Format("{0} | *** FORCED CLOSE ***", Time[0].ToString("HH:mm:ss")));
                    string activeSignal = GetActiveEntrySignal();
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(BuildExitSignalName("ForcedClose"), activeSignal);
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort(BuildExitSignalName("ForcedClose"), activeSignal);
                }
                if (orSet || orHigh > double.MinValue || orLow < double.MaxValue)
                {
                    orHigh = double.MinValue;
                    orLow = double.MaxValue;
                    orSet = false;
                    longEntryLevel = 0;
                    shortEntryLevel = 0;
                    isInSession = false;
                    RemoveDrawObject("ORHighLine");
                    RemoveDrawObject("ORLowLine");
                    RemoveDrawObject("LongEntryLine");
                    RemoveDrawObject("ShortEntryLine");
                    UpdateInfoPanel();
                }
                return;
            }

            // Cut-off
            TimeSpan cutOffTime = new TimeSpan(CutOffHour, CutOffMinute, 0);
            if (currentTimeOfDay >= cutOffTime && Position.MarketPosition != MarketPosition.Flat)
            {
                LogDebug(String.Format("{0} | *** CUT-OFF ***", Time[0].ToString("HH:mm:ss")));
                string activeSignal = GetActiveEntrySignal();
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(BuildExitSignalName("CutOffExit"), activeSignal);
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(BuildExitSignalName("CutOffExit"), activeSignal);
            }

            // Max session loss total
            if (MaxSessionLossTotal > 0 && sessionLossTotal >= MaxSessionLossTotal && !maxLossTotalReached)
            {
                maxLossTotalReached = true;
                canTakeNewEntry = false;
                LogDebug(String.Format("{0} | *** MAX SESSION LOSS TOTAL ***", Time[0].ToString("HH:mm:ss")));
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    string activeSignal = GetActiveEntrySignal();
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(BuildExitSignalName("MaxLossExit"), activeSignal);
                    else ExitShort(BuildExitSignalName("MaxLossExit"), activeSignal);
                }
                UpdateInfoPanel();
                return;
            }
            
            // Max loss per direction
            if (activeL_MaxSessionLoss > 0 && sessionLossLong >= activeL_MaxSessionLoss && !maxLossLongReached)
            {
                maxLossLongReached = true;
                LogDebug(String.Format("{0} | *** MAX LONG LOSS ***", Time[0].ToString("HH:mm:ss")));
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong(BuildExitSignalName("MaxLossLongExit"), GetActiveEntrySignal());
                UpdateInfoPanel();
            }
            if (activeS_MaxSessionLoss > 0 && sessionLossShort >= activeS_MaxSessionLoss && !maxLossShortReached)
            {
                maxLossShortReached = true;
                LogDebug(String.Format("{0} | *** MAX SHORT LOSS ***", Time[0].ToString("HH:mm:ss")));
                if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort(BuildExitSignalName("MaxLossShortExit"), GetActiveEntrySignal());
                UpdateInfoPanel();
            }

            // Detect position closed
            bool currentlyInPosition = Position.MarketPosition != MarketPosition.Flat;
            if (wasInPosition && !currentlyInPosition)
            {
                LogDebug(String.Format("{0} | Position closed", Time[0].ToString("HH:mm:ss")));
                ResetForNextTrade();
            }
            wasInPosition = currentlyInPosition;

            double currentPrice = Close[0];

            // === BREAK EVEN ===
            if (Position.MarketPosition != MarketPosition.Flat && entryPrice > 0 && !beTriggered)
            {
                double profitTicks = 0;
                if (Position.MarketPosition == MarketPosition.Long && activeL_BEEnabled)
                {
                    profitTicks = Math.Round((currentPrice - entryPrice) / TickSize);
                    double triggerTicks = activeL_BETriggerTicks;
                    if (activeL_BETriggerMode == BETriggerModeEnum.ORMultiple)
                        triggerTicks = Math.Round((orHigh - orLow) / TickSize) * activeL_BETriggerORMultiple;
                    if (profitTicks >= triggerTicks)
                    {
                        beNewStopPrice = entryPrice + (activeL_BEOffsetTicks * TickSize);
                        double sanitizedBeStop;
                        if (TrySanitizeStopPriceForCurrentMarket(MarketPosition.Long, beNewStopPrice, out sanitizedBeStop)
                            && sanitizedBeStop > currentStopPrice)
                        {
                            beTriggered = true;
                            currentStopPrice = sanitizedBeStop;
                            SetStopLoss(GetActiveEntrySignal(), CalculationMode.Price, currentStopPrice, false);
                            LogDebug(String.Format("{0} | *** BE LONG *** +{1:F0}t >= {2:F0}t | SL->{3:F2}",
                                Time[0].ToString("HH:mm:ss"), profitTicks, triggerTicks, currentStopPrice));
                            UpdateTradeLines();
                        }
                        UpdateInfoPanel();
                    }
                }
                else if (Position.MarketPosition == MarketPosition.Short && activeS_BEEnabled)
                {
                    profitTicks = Math.Round((entryPrice - currentPrice) / TickSize);
                    double triggerTicks = activeS_BETriggerTicks;
                    if (activeS_BETriggerMode == BETriggerModeEnum.ORMultiple)
                        triggerTicks = Math.Round((orHigh - orLow) / TickSize) * activeS_BETriggerORMultiple;
                    if (profitTicks >= triggerTicks)
                    {
                        beNewStopPrice = entryPrice - (activeS_BEOffsetTicks * TickSize);
                        double sanitizedBeStop;
                        if (TrySanitizeStopPriceForCurrentMarket(MarketPosition.Short, beNewStopPrice, out sanitizedBeStop)
                            && sanitizedBeStop < currentStopPrice)
                        {
                            beTriggered = true;
                            currentStopPrice = sanitizedBeStop;
                            SetStopLoss(GetActiveEntrySignal(), CalculationMode.Price, currentStopPrice, false);
                            LogDebug(String.Format("{0} | *** BE SHORT *** +{1:F0}t >= {2:F0}t | SL->{3:F2}",
                                Time[0].ToString("HH:mm:ss"), profitTicks, triggerTicks, currentStopPrice));
                            UpdateTradeLines();
                        }
                        UpdateInfoPanel();
                    }
                }
            }

            // Max profit total
            if (MaxSessionProfitTotal > 0 && !maxProfitTotalReached)
            {
                double totalProfitTicks = sessionPnLTotal;
                if (Position.MarketPosition != MarketPosition.Flat && entryPrice > 0)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        totalProfitTicks += Math.Round((currentPrice - entryPrice) / TickSize);
                    else totalProfitTicks += Math.Round((entryPrice - currentPrice) / TickSize);
                }
                if (totalProfitTicks >= MaxSessionProfitTotal)
                {
                    maxProfitTotalReached = true;
                    canTakeNewEntry = false;
                    LogDebug(String.Format("{0} | *** MAX SESSION PROFIT TOTAL ***", Time[0].ToString("HH:mm:ss")));
                    if (Position.MarketPosition != MarketPosition.Flat)
                    {
                        string activeSignal = GetActiveEntrySignal();
                        if (Position.MarketPosition == MarketPosition.Long)
                            ExitLong(BuildExitSignalName("MaxProfitExit"), activeSignal);
                        else ExitShort(BuildExitSignalName("MaxProfitExit"), activeSignal);
                    }
                    UpdateInfoPanel();
                    return;
                }
            }
            
            // Max profit per direction
            if (activeL_MaxSessionProfit > 0 && !maxProfitLongReached)
            {
                double lp = sessionPnLLong;
                if (Position.MarketPosition == MarketPosition.Long && entryPrice > 0)
                    lp += Math.Round((currentPrice - entryPrice) / TickSize);
                if (lp >= activeL_MaxSessionProfit)
                {
                    maxProfitLongReached = true;
                    LogDebug(String.Format("{0} | *** MAX LONG PROFIT ***", Time[0].ToString("HH:mm:ss")));
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(BuildExitSignalName("MaxProfitLongExit"), GetActiveEntrySignal());
                    UpdateInfoPanel();
                }
            }
            if (activeS_MaxSessionProfit > 0 && !maxProfitShortReached)
            {
                double sp = sessionPnLShort;
                if (Position.MarketPosition == MarketPosition.Short && entryPrice > 0)
                    sp += Math.Round((entryPrice - currentPrice) / TickSize);
                if (sp >= activeS_MaxSessionProfit)
                {
                    maxProfitShortReached = true;
                    LogDebug(String.Format("{0} | *** MAX SHORT PROFIT ***", Time[0].ToString("HH:mm:ss")));
                    if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort(BuildExitSignalName("MaxProfitShortExit"), GetActiveEntrySignal());
                    UpdateInfoPanel();
                }
            }
            
            // Price return to OR
            if (Position.MarketPosition == MarketPosition.Flat && !priceReturnedToOR)
            {
                if (currentPrice >= orLow && currentPrice <= orHigh)
                {
                    priceReturnedToOR = true;
                    LogDebug(String.Format("{0} | *** PRICE RETURNED TO OR ***", Time[0].ToString("HH:mm:ss")));
                    UpdateInfoPanel();
                }
            }

            if (IsFirstTickOfBar) UpdateInfoPanel();

            // Window check
            TimeSpan currentTime = Time[0].TimeOfDay;
            TimeSpan windowEnd = new TimeSpan(TradeWindowEndHour, TradeWindowEndMinute, 0);
            if (currentTime > windowEnd || currentTime >= cutOffTime) return;
            
            isInSession = true;
            if (Position.MarketPosition != MarketPosition.Flat) return;

            bool longAllowed = CanTakeLong(currentTime);
            bool shortAllowed = CanTakeShort(currentTime);

            if (canTakeNewEntry && priceReturnedToOR && !maxLossTotalReached && !maxProfitTotalReached && !noBucketMatched)
            {
                double effectiveLongEntry = longEntryLevel;
                double effectiveShortEntry = shortEntryLevel;
                
                if (sessionTradeCountLong == 0 && activeL_FirstTradeOffset > 0)
                    effectiveLongEntry = longEntryLevel - (activeL_FirstTradeOffset * TickSize);
                if (sessionTradeCountShort == 0 && activeS_FirstTradeOffset > 0)
                    effectiveShortEntry = shortEntryLevel + (activeS_FirstTradeOffset * TickSize);
                
                // LONG
                if (longAllowed && currentPrice > effectiveLongEntry)
                {
                    string nextSignal = BuildEntrySignalName(true);
                    double nextStopPrice = CalculateLongStopPrice();
                    int nextTargetTicks = CalculateLongTargetTicks();
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", currentPrice, ContractQuantity))
                    {
                        LogDebug(String.Format("{0} | Entry confirmation declined | LONG.", Time[0].ToString("HH:mm:ss.fff")));
                        return;
                    }

                    currentEntrySignal = nextSignal;
                    pendingStopPrice = nextStopPrice;
                    LogDebug(String.Format("=== LONG ENTRY (Bucket {0}) ===", activeBucketL));
                    if (sessionTradeCountLong == 0 && activeL_FirstTradeOffset > 0)
                        LogDebug(String.Format("  Offset: -{0}t | Effective: {1:F2}", activeL_FirstTradeOffset, effectiveLongEntry));
                    
                    pendingTargetTicks = nextTargetTicks;
                    EnterLong(ContractQuantity, nextSignal);
                    
                    canTakeNewEntry = false;
                    priceReturnedToOR = false;
                    lastTradeDirection = MarketPosition.Long;
                    bracketOrdersPlaced = false;
                    sessionTradeCountLong++;
                    sessionTradeCountTotal++;
                    
                    LogDebug(String.Format("{0} | LONG #{1}(L:{2}/S:{3}) | Price={4} > {5} | SL={6} TP={7}t",
                        Time[0].ToString("HH:mm:ss.fff"), sessionTradeCountTotal, sessionTradeCountLong, sessionTradeCountShort,
                        currentPrice, effectiveLongEntry, pendingStopPrice, pendingTargetTicks));
                }
                // SHORT
                else if (shortAllowed && currentPrice < effectiveShortEntry)
                {
                    string nextSignal = BuildEntrySignalName(false);
                    double nextStopPrice = CalculateShortStopPrice();
                    int nextTargetTicks = CalculateShortTargetTicks();
                    if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", currentPrice, ContractQuantity))
                    {
                        LogDebug(String.Format("{0} | Entry confirmation declined | SHORT.", Time[0].ToString("HH:mm:ss.fff")));
                        return;
                    }

                    currentEntrySignal = nextSignal;
                    pendingStopPrice = nextStopPrice;
                    LogDebug(String.Format("=== SHORT ENTRY (Bucket {0}) ===", activeBucketS));
                    if (sessionTradeCountShort == 0 && activeS_FirstTradeOffset > 0)
                        LogDebug(String.Format("  Offset: +{0}t | Effective: {1:F2}", activeS_FirstTradeOffset, effectiveShortEntry));
                    
                    pendingTargetTicks = nextTargetTicks;
                    EnterShort(ContractQuantity, nextSignal);
                    
                    canTakeNewEntry = false;
                    priceReturnedToOR = false;
                    lastTradeDirection = MarketPosition.Short;
                    bracketOrdersPlaced = false;
                    sessionTradeCountShort++;
                    sessionTradeCountTotal++;
                    
                    LogDebug(String.Format("{0} | SHORT #{1}(L:{2}/S:{3}) | Price={4} < {5} | SL={6} TP={7}t",
                        Time[0].ToString("HH:mm:ss.fff"), sessionTradeCountTotal, sessionTradeCountLong, sessionTradeCountShort,
                        currentPrice, effectiveShortEntry, pendingStopPrice, pendingTargetTicks));
                }
            }
        }


        #region Helper Methods


        private int FindMatchingBucketLong(int orRangeTicks)
        {
            if (B1L_Enabled && orRangeTicks >= B1L_ORMin && orRangeTicks <= B1L_ORMax)
                return 1;
            if (B2L_Enabled && orRangeTicks >= B2L_ORMin && orRangeTicks <= B2L_ORMax)
                return 2;
            if (B3L_Enabled && orRangeTicks >= B3L_ORMin && orRangeTicks <= B3L_ORMax)
                return 3;
            return 0;
        }

        private int FindMatchingBucketShort(int orRangeTicks)
        {
            if (B1S_Enabled && orRangeTicks >= B1S_ORMin && orRangeTicks <= B1S_ORMax)
                return 1;
            if (B2S_Enabled && orRangeTicks >= B2S_ORMin && orRangeTicks <= B2S_ORMax)
                return 2;
            if (B3S_Enabled && orRangeTicks >= B3S_ORMin && orRangeTicks <= B3S_ORMax)
                return 3;
            return 0;
        }

        private void LoadBucketLongParameters(int bucket)
        {
            activeBucketL = bucket;
            switch (bucket)
            {
                case 1:
                    activeL_Enabled = B1L_Enabled;
                    activeL_ORMin = B1L_ORMin;
                    activeL_ORMax = B1L_ORMax;
                    activeL_BreakoutTicks = B1L_BreakoutTicks;
                    activeL_FirstTradeOffset = B1L_FirstTradeOffset;
                    activeL_TradeWindowStart = B1L_TradeWindowStart;
                    activeL_StopLossMode = B1L_StopLossMode;
                    activeL_StopLossTicks = B1L_StopLossTicks;
                    activeL_StopLossORMultiple = B1L_StopLossORMultiple;
                    activeL_TakeProfitMode = B1L_TakeProfitMode;
                    activeL_TakeProfitTicks = B1L_TakeProfitTicks;
                    activeL_TakeProfitORMultiple = B1L_TakeProfitORMultiple;
                    activeL_MaxTrades = B1L_MaxTrades;
                    activeL_MaxTradesTotal = B1L_MaxTradesTotal;
                    activeL_MaxSessionLoss = B1L_MaxSessionLoss;
                    activeL_MaxSessionProfit = B1L_MaxSessionProfit;
                    activeL_BEEnabled = B1L_BEEnabled;
                    activeL_BETriggerMode = B1L_BETriggerMode;
                    activeL_BETriggerTicks = B1L_BETriggerTicks;
                    activeL_BETriggerORMultiple = B1L_BETriggerORMultiple;
                    activeL_BEOffsetTicks = B1L_BEOffsetTicks;
                    break;
                case 2:
                    activeL_Enabled = B2L_Enabled;
                    activeL_ORMin = B2L_ORMin;
                    activeL_ORMax = B2L_ORMax;
                    activeL_BreakoutTicks = B2L_BreakoutTicks;
                    activeL_FirstTradeOffset = B2L_FirstTradeOffset;
                    activeL_TradeWindowStart = B2L_TradeWindowStart;
                    activeL_StopLossMode = B2L_StopLossMode;
                    activeL_StopLossTicks = B2L_StopLossTicks;
                    activeL_StopLossORMultiple = B2L_StopLossORMultiple;
                    activeL_TakeProfitMode = B2L_TakeProfitMode;
                    activeL_TakeProfitTicks = B2L_TakeProfitTicks;
                    activeL_TakeProfitORMultiple = B2L_TakeProfitORMultiple;
                    activeL_MaxTrades = B2L_MaxTrades;
                    activeL_MaxTradesTotal = B2L_MaxTradesTotal;
                    activeL_MaxSessionLoss = B2L_MaxSessionLoss;
                    activeL_MaxSessionProfit = B2L_MaxSessionProfit;
                    activeL_BEEnabled = B2L_BEEnabled;
                    activeL_BETriggerMode = B2L_BETriggerMode;
                    activeL_BETriggerTicks = B2L_BETriggerTicks;
                    activeL_BETriggerORMultiple = B2L_BETriggerORMultiple;
                    activeL_BEOffsetTicks = B2L_BEOffsetTicks;
                    break;
                case 3:
                    activeL_Enabled = B3L_Enabled;
                    activeL_ORMin = B3L_ORMin;
                    activeL_ORMax = B3L_ORMax;
                    activeL_BreakoutTicks = B3L_BreakoutTicks;
                    activeL_FirstTradeOffset = B3L_FirstTradeOffset;
                    activeL_TradeWindowStart = B3L_TradeWindowStart;
                    activeL_StopLossMode = B3L_StopLossMode;
                    activeL_StopLossTicks = B3L_StopLossTicks;
                    activeL_StopLossORMultiple = B3L_StopLossORMultiple;
                    activeL_TakeProfitMode = B3L_TakeProfitMode;
                    activeL_TakeProfitTicks = B3L_TakeProfitTicks;
                    activeL_TakeProfitORMultiple = B3L_TakeProfitORMultiple;
                    activeL_MaxTrades = B3L_MaxTrades;
                    activeL_MaxTradesTotal = B3L_MaxTradesTotal;
                    activeL_MaxSessionLoss = B3L_MaxSessionLoss;
                    activeL_MaxSessionProfit = B3L_MaxSessionProfit;
                    activeL_BEEnabled = B3L_BEEnabled;
                    activeL_BETriggerMode = B3L_BETriggerMode;
                    activeL_BETriggerTicks = B3L_BETriggerTicks;
                    activeL_BETriggerORMultiple = B3L_BETriggerORMultiple;
                    activeL_BEOffsetTicks = B3L_BEOffsetTicks;
                    break;
            }
            LogDebug(String.Format("  LONG -> Bucket {0} | OR:{1}-{2}t SL={3} TP={4} BE={5}",
                bucket, activeL_ORMin, activeL_ORMax,
                activeL_StopLossMode == SLModeEnum.FixedTicks ? activeL_StopLossTicks.ToString() + "t" : activeL_StopLossORMultiple.ToString() + "x",
                activeL_TakeProfitMode == TPModeEnum.FixedTicks ? activeL_TakeProfitTicks.ToString() + "t" : activeL_TakeProfitORMultiple.ToString() + "x",
                activeL_BEEnabled));
        }

        private void LoadBucketShortParameters(int bucket)
        {
            activeBucketS = bucket;
            switch (bucket)
            {
                case 1:
                    activeS_Enabled = B1S_Enabled;
                    activeS_ORMin = B1S_ORMin;
                    activeS_ORMax = B1S_ORMax;
                    activeS_BreakoutTicks = B1S_BreakoutTicks;
                    activeS_FirstTradeOffset = B1S_FirstTradeOffset;
                    activeS_TradeWindowStart = B1S_TradeWindowStart;
                    activeS_StopLossMode = B1S_StopLossMode;
                    activeS_StopLossTicks = B1S_StopLossTicks;
                    activeS_StopLossORMultiple = B1S_StopLossORMultiple;
                    activeS_TakeProfitMode = B1S_TakeProfitMode;
                    activeS_TakeProfitTicks = B1S_TakeProfitTicks;
                    activeS_TakeProfitORMultiple = B1S_TakeProfitORMultiple;
                    activeS_MaxTrades = B1S_MaxTrades;
                    activeS_MaxTradesTotal = B1S_MaxTradesTotal;
                    activeS_MaxSessionLoss = B1S_MaxSessionLoss;
                    activeS_MaxSessionProfit = B1S_MaxSessionProfit;
                    activeS_BEEnabled = B1S_BEEnabled;
                    activeS_BETriggerMode = B1S_BETriggerMode;
                    activeS_BETriggerTicks = B1S_BETriggerTicks;
                    activeS_BETriggerORMultiple = B1S_BETriggerORMultiple;
                    activeS_BEOffsetTicks = B1S_BEOffsetTicks;
                    break;
                case 2:
                    activeS_Enabled = B2S_Enabled;
                    activeS_ORMin = B2S_ORMin;
                    activeS_ORMax = B2S_ORMax;
                    activeS_BreakoutTicks = B2S_BreakoutTicks;
                    activeS_FirstTradeOffset = B2S_FirstTradeOffset;
                    activeS_TradeWindowStart = B2S_TradeWindowStart;
                    activeS_StopLossMode = B2S_StopLossMode;
                    activeS_StopLossTicks = B2S_StopLossTicks;
                    activeS_StopLossORMultiple = B2S_StopLossORMultiple;
                    activeS_TakeProfitMode = B2S_TakeProfitMode;
                    activeS_TakeProfitTicks = B2S_TakeProfitTicks;
                    activeS_TakeProfitORMultiple = B2S_TakeProfitORMultiple;
                    activeS_MaxTrades = B2S_MaxTrades;
                    activeS_MaxTradesTotal = B2S_MaxTradesTotal;
                    activeS_MaxSessionLoss = B2S_MaxSessionLoss;
                    activeS_MaxSessionProfit = B2S_MaxSessionProfit;
                    activeS_BEEnabled = B2S_BEEnabled;
                    activeS_BETriggerMode = B2S_BETriggerMode;
                    activeS_BETriggerTicks = B2S_BETriggerTicks;
                    activeS_BETriggerORMultiple = B2S_BETriggerORMultiple;
                    activeS_BEOffsetTicks = B2S_BEOffsetTicks;
                    break;
                case 3:
                    activeS_Enabled = B3S_Enabled;
                    activeS_ORMin = B3S_ORMin;
                    activeS_ORMax = B3S_ORMax;
                    activeS_BreakoutTicks = B3S_BreakoutTicks;
                    activeS_FirstTradeOffset = B3S_FirstTradeOffset;
                    activeS_TradeWindowStart = B3S_TradeWindowStart;
                    activeS_StopLossMode = B3S_StopLossMode;
                    activeS_StopLossTicks = B3S_StopLossTicks;
                    activeS_StopLossORMultiple = B3S_StopLossORMultiple;
                    activeS_TakeProfitMode = B3S_TakeProfitMode;
                    activeS_TakeProfitTicks = B3S_TakeProfitTicks;
                    activeS_TakeProfitORMultiple = B3S_TakeProfitORMultiple;
                    activeS_MaxTrades = B3S_MaxTrades;
                    activeS_MaxTradesTotal = B3S_MaxTradesTotal;
                    activeS_MaxSessionLoss = B3S_MaxSessionLoss;
                    activeS_MaxSessionProfit = B3S_MaxSessionProfit;
                    activeS_BEEnabled = B3S_BEEnabled;
                    activeS_BETriggerMode = B3S_BETriggerMode;
                    activeS_BETriggerTicks = B3S_BETriggerTicks;
                    activeS_BETriggerORMultiple = B3S_BETriggerORMultiple;
                    activeS_BEOffsetTicks = B3S_BEOffsetTicks;
                    break;
            }
            LogDebug(String.Format("  SHORT -> Bucket {0} | OR:{1}-{2}t SL={3} TP={4} BE={5}",
                bucket, activeS_ORMin, activeS_ORMax,
                activeS_StopLossMode == SLModeEnum.FixedTicks ? activeS_StopLossTicks.ToString() + "t" : activeS_StopLossORMultiple.ToString() + "x",
                activeS_TakeProfitMode == TPModeEnum.FixedTicks ? activeS_TakeProfitTicks.ToString() + "t" : activeS_TakeProfitORMultiple.ToString() + "x",
                activeS_BEEnabled));
        }

        
        private bool CanTakeLong(TimeSpan currentTime)
        {
            if (maxAccountLimitHit) return false;
            if (activeBucketL == 0) return false;
            if (IsLastBarOfSession()) return false;
            TimeSpan ws = new TimeSpan(9, 30 + activeL_TradeWindowStart, 30);
            if (currentTime < ws) return false;
            if (activeL_MaxTradesTotal > 0 && sessionTradeCountTotal >= activeL_MaxTradesTotal) return false;
            if (activeL_MaxTrades > 0 && sessionTradeCountLong >= activeL_MaxTrades) return false;
            if (maxLossLongReached || maxProfitLongReached) return false;
            return true;
        }
        
        private bool CanTakeShort(TimeSpan currentTime)
        {
            if (maxAccountLimitHit) return false;
            if (activeBucketS == 0) return false;
            if (IsLastBarOfSession()) return false;
            TimeSpan ws = new TimeSpan(9, 30 + activeS_TradeWindowStart, 30);
            if (currentTime < ws) return false;
            if (activeS_MaxTradesTotal > 0 && sessionTradeCountTotal >= activeS_MaxTradesTotal) return false;
            if (activeS_MaxTrades > 0 && sessionTradeCountShort >= activeS_MaxTrades) return false;
            if (maxLossShortReached || maxProfitShortReached) return false;
            return true;
        }

        private string BuildEntrySignalName(bool isLong)
        {
            entrySignalSequence++;
            return string.Format(CultureInfo.InvariantCulture, "{0}_{1}", isLong ? "ADAMLong" : "ADAMShort", entrySignalSequence);
        }

        private bool IsEntrySignalName(string orderName)
        {
            if (string.IsNullOrEmpty(orderName))
                return false;

            return orderName.StartsWith("ADAMLong", StringComparison.OrdinalIgnoreCase)
                || orderName.StartsWith("ADAMShort", StringComparison.OrdinalIgnoreCase);
        }

        private string GetActiveEntrySignal()
        {
            if (!string.IsNullOrEmpty(currentEntrySignal))
                return currentEntrySignal;

            if (Position.MarketPosition == MarketPosition.Short)
                return "ADAMShort";

            return "ADAMLong";
        }

        private string BuildExitSignalName(string reason)
        {
            return "ADAM" + reason;
        }

        private void CancelAllOrders()
        {
            if (Account == null || Instrument == null)
                return;

            bool cancelledAny = false;

            try
            {
                foreach (Order order in Account.Orders)
                {
                    if (!IsOrderActive(order))
                        continue;

                    if (order.Instrument == null || order.Instrument.FullName != Instrument.FullName)
                        continue;

                    string orderName = order.Name ?? string.Empty;
                    if (!orderName.StartsWith("ADAM", StringComparison.OrdinalIgnoreCase))
                        continue;

                    CancelOrder(order);
                    cancelledAny = true;
                }

                if (cancelledAny)
                    SendWebhook("cancel");
            }
            catch (Exception ex)
            {
                LogDebug("CancelAllOrders error: " + ex.Message);
            }
        }

        private void ExitAllPositions(string reason)
        {
            string activeSignal = GetActiveEntrySignal();
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(BuildExitSignalName(reason), activeSignal);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(BuildExitSignalName(reason), activeSignal);
        }

        private bool ShouldAccountBalanceExit()
        {
            if (MaxAccountBalance <= 0.0)
                return false;

            if (maxAccountLimitHit)
                return true;

            double balance;
            if (!TryGetCurrentNetLiquidation(out balance))
                return false;

            if (balance < MaxAccountBalance)
                return false;

            maxAccountLimitHit = true;
            CancelAllOrders();
            LogDebug(string.Format(CultureInfo.InvariantCulture, "Max account balance reached | netLiq={0:0.00} target={1:0.00}", balance, MaxAccountBalance));
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
            }
            catch
            {
            }

            try
            {
                double realizedCash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                double unrealized = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);
                netLiquidation = realizedCash + unrealized;
                return true;
            }
            catch
            {
                netLiquidation = 0.0;
                return false;
            }
        }

        #region Webhooks

        private bool ShouldSendExitWebhook(Execution execution, string orderName, MarketPosition marketPosition)
        {
            if (execution == null || execution.Order == null)
                return false;

            if (IsEntrySignalName(orderName))
                return false;

            string fromEntry = execution.Order.FromEntrySignal ?? string.Empty;
            if (IsEntrySignalName(fromEntry))
                return true;

            string normalized = orderName ?? string.Empty;
            if (normalized.Length == 0)
                return marketPosition == MarketPosition.Flat;

            return normalized.StartsWith("ADAM", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Stop loss", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Profit target", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Exit on session close", StringComparison.OrdinalIgnoreCase)
                || marketPosition == MarketPosition.Flat;
        }

        private bool TryGetEntryWebhookAction(Execution execution, out string action, out bool isMarketEntry)
        {
            action = null;
            isMarketEntry = false;

            if (execution == null || execution.Order == null)
                return false;

            string orderName = execution.Order.Name ?? string.Empty;
            if (orderName.StartsWith("ADAMLong", StringComparison.OrdinalIgnoreCase))
                action = "buy";
            else if (orderName.StartsWith("ADAMShort", StringComparison.OrdinalIgnoreCase))
                action = "sell";
            else
                return false;

            OrderType orderType = execution.Order.OrderType;
            isMarketEntry = orderType == OrderType.Market || orderType == OrderType.StopMarket;
            return true;
        }

        private int GetDefaultWebhookQuantity()
        {
            return Math.Max(1, ContractQuantity);
        }

        private string GetWebhookTicker()
        {
            if (!string.IsNullOrWhiteSpace(WebhookTickerOverride))
                return WebhookTickerOverride.Trim();

            return Instrument != null && Instrument.MasterInstrument != null
                ? Instrument.MasterInstrument.Name
                : "UNKNOWN";
        }

        private void SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime)
                return;

            if (WebhookProviderType == WebhookProvider.ProjectX)
            {
                int orderQtyForProvider = quantityOverride > 0 ? quantityOverride : GetDefaultWebhookQuantity();
                SendProjectX(eventType, entryPrice, takeProfit, stopLoss, isMarketEntry, orderQtyForProvider);
                return;
            }

            if (string.IsNullOrWhiteSpace(WebhookUrl))
                return;

            try
            {
                int orderQty = quantityOverride > 0 ? quantityOverride : GetDefaultWebhookQuantity();
                string ticker = GetWebhookTicker();
                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);
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
                    return;

                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.UploadString(WebhookUrl, "POST", json);
                }
            }
            catch (Exception ex)
            {
                LogDebug("Webhook error: " + ex.Message);
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
                switch ((eventType ?? string.Empty).ToLowerInvariant())
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
                LogDebug("ProjectX error: " + ex.Message);
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

        #endregion

        private bool IsOrderActive(Order order)
        {
            return order != null
                && (order.OrderState == OrderState.Working
                    || order.OrderState == OrderState.Submitted
                    || order.OrderState == OrderState.Accepted
                    || order.OrderState == OrderState.ChangePending
                    || order.OrderState == OrderState.PartFilled);
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
            LogDebug("Timeframe validation failed | " + message);
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
                LogDebug("Failed to show timeframe popup: " + ex.Message);
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
            LogDebug("Instrument validation failed | " + message);
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
                LogDebug("Failed to show instrument popup: " + ex.Message);
            }
        }
        
        private double CalculateLongStopPrice()
        {
            if (activeL_StopLossMode == SLModeEnum.ORMultiple)
            {
                int slTicks = (int)(Math.Round((orHigh - orLow) / TickSize) * activeL_StopLossORMultiple);
                return orHigh - (slTicks * TickSize);
            }
            return orHigh - (activeL_StopLossTicks * TickSize);
        }
        
        private double CalculateShortStopPrice()
        {
            if (activeS_StopLossMode == SLModeEnum.ORMultiple)
            {
                int slTicks = (int)(Math.Round((orHigh - orLow) / TickSize) * activeS_StopLossORMultiple);
                return orLow + (slTicks * TickSize);
            }
            return orLow + (activeS_StopLossTicks * TickSize);
        }
        
        private int CalculateLongTargetTicks()
        {
            if (activeL_TakeProfitMode == TPModeEnum.ORMultiple)
                return (int)(Math.Round((orHigh - orLow) / TickSize) * activeL_TakeProfitORMultiple);
            return activeL_TakeProfitTicks;
        }
        
        private int CalculateShortTargetTicks()
        {
            if (activeS_TakeProfitMode == TPModeEnum.ORMultiple)
                return (int)(Math.Round((orHigh - orLow) / TickSize) * activeS_TakeProfitORMultiple);
            return activeS_TakeProfitTicks;
        }

        private void ExitIfSessionEnded()
        {
            if (!IsLastBarOfSession())
                return;

            string activeSignal = GetActiveEntrySignal();
            if (Position.MarketPosition == MarketPosition.Long)
                ExitLong(BuildExitSignalName("SessionEnd"), activeSignal);
            else if (Position.MarketPosition == MarketPosition.Short)
                ExitShort(BuildExitSignalName("SessionEnd"), activeSignal);
        }

        private bool IsAfterSessionEnd(DateTime time)
        {
            return IsLastBarOfSession();
        }

        private bool IsLastBarOfSession()
        {
            return Bars != null && Bars.IsLastBarOfSession;
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

            double marketPrice = Close[0];
            double stopReferencePrice = GetReferencePriceForStop(positionDirection);
            if (stopReferencePrice <= 0 || double.IsNaN(stopReferencePrice) || double.IsInfinity(stopReferencePrice))
                stopReferencePrice = marketPrice;
            double minTick = TickSize;
            int targetTicks = Math.Max(0, rawTargetTicks);

            stopPrice = Instrument.MasterInstrument.RoundToTickSize(rawStopPrice);
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

        private MarketPosition ResolveEntryDirection(string entrySignalName, MarketPosition fallback)
        {
            if (!string.IsNullOrEmpty(entrySignalName))
            {
                if (entrySignalName.StartsWith("ADAMLong", StringComparison.OrdinalIgnoreCase))
                    return MarketPosition.Long;
                if (entrySignalName.StartsWith("ADAMShort", StringComparison.OrdinalIgnoreCase))
                    return MarketPosition.Short;
            }

            if (fallback == MarketPosition.Long || fallback == MarketPosition.Short)
                return fallback;

            if (Position.MarketPosition == MarketPosition.Long || Position.MarketPosition == MarketPosition.Short)
                return Position.MarketPosition;

            return MarketPosition.Flat;
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
        
        #endregion
        
        #region Session / Trade Reset
        
        private void ResetForNewSession(DateTime tradingDay)
        {
            int prevTradeCount = sessionTradeCountTotal;
            double prevPnL = sessionPnLTotal;
            
            orHigh = double.MinValue; orLow = double.MaxValue; orSet = false;
            longEntryLevel = 0; shortEntryLevel = 0;
            canTakeNewEntry = true; priceReturnedToOR = true;
            entryPrice = 0; pendingStopPrice = 0; pendingTargetTicks = 0;
            currentStopPrice = 0; currentTargetPrice = 0; currentTargetTicks = 0;
            isInSession = false; wasInPosition = false;
            lastTradingDay = tradingDay;
            lastTradeDirection = MarketPosition.Flat;
            beTriggered = false; beNewStopPrice = 0;
            bracketOrdersPlaced = false;
            currentEntrySignal = string.Empty;
            activeBucketL = 0; activeBucketS = 0;
            noBucketMatched = false;
            sessionTradeCountLong = 0; sessionTradeCountShort = 0; sessionTradeCountTotal = 0;
            sessionPnLLong = 0; sessionPnLShort = 0; sessionPnLTotal = 0;
            sessionLossLong = 0; sessionLossShort = 0; sessionLossTotal = 0;
            maxLossLongReached = false; maxLossShortReached = false; maxLossTotalReached = false;
            maxProfitLongReached = false; maxProfitShortReached = false; maxProfitTotalReached = false;
            
            sessionStart = tradingDay.Add(new TimeSpan(9, 30, 0));
            sessionEnd = tradingDay.Add(new TimeSpan(16, 0, 0));
            DrawSessionBackground();
            DrawCutOffWindow(tradingDay);
            
            RemoveDrawObject("ORHighLine"); RemoveDrawObject("ORLowLine");
            RemoveDrawObject("LongEntryLine"); RemoveDrawObject("ShortEntryLine");
            
            LogDebug(String.Format("  Prev: {0} trades | P&L: {1:F0}t | Reset done", prevTradeCount, prevPnL));
        }

        private void ResetForNextTrade()
        {
            entryPrice = 0; pendingStopPrice = 0; pendingTargetTicks = 0;
            currentStopPrice = 0; currentTargetPrice = 0; currentTargetTicks = 0;
            beTriggered = false; beNewStopPrice = 0;
            bracketOrdersPlaced = false;
            currentEntrySignal = string.Empty;
            canTakeNewEntry = true;
            
            bool noLongsLeft = (activeL_MaxTrades > 0 && sessionTradeCountLong >= activeL_MaxTrades) || maxLossLongReached || maxProfitLongReached;
            bool noShortsLeft = (activeS_MaxTrades > 0 && sessionTradeCountShort >= activeS_MaxTrades) || maxLossShortReached || maxProfitShortReached;
            bool totalMaxL = activeL_MaxTradesTotal > 0 && sessionTradeCountTotal >= activeL_MaxTradesTotal;
            bool totalMaxS = activeS_MaxTradesTotal > 0 && sessionTradeCountTotal >= activeS_MaxTradesTotal;
            
            if (maxLossTotalReached || maxProfitTotalReached || ((noLongsLeft || totalMaxL) && (noShortsLeft || totalMaxS)))
            {
                canTakeNewEntry = false;
                LogDebug("  *** NO MORE TRADES ***");
            }
            else
            {
                LogDebug(String.Format("  Waiting for OR | T:{0} L:{1} S:{2}", 
                    sessionTradeCountTotal, sessionTradeCountLong, sessionTradeCountShort));
            }
            
            UpdateInfoPanel();
        }
        
        #endregion
        
        #region Drawing

        private void DrawSessionBackground()
        {
            if (sessionStart == default(DateTime) || sessionEnd <= sessionStart)
                return;

            string rectTag = string.Format("ADAM_SessionFill_{0:yyyyMMdd_HHmm}", sessionStart);
            if (DrawObjects[rectTag] != null)
                return;

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

        private void DrawCutOffWindow(DateTime tradingDay)
        {
            DateTime windowStart = tradingDay.Add(new TimeSpan(CutOffHour, CutOffMinute, 0));
            DateTime configuredEnd = tradingDay.Add(new TimeSpan(ForcedCloseHour, ForcedCloseMinute, 0));
            DateTime windowEnd = configuredEnd > windowStart ? configuredEnd : sessionEnd;
            if (windowEnd <= windowStart)
                windowEnd = windowStart.AddMinutes(1);

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

            string tagBase = string.Format("ADAM_CutOff_{0:yyyyMMdd_HHmm}", windowStart);
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
            if (windowEnd > windowStart)
                Draw.VerticalLine(this, tagBase + "_End", windowEnd, lineBrush, DashStyleHelper.DashDot, 2);
        }

        private void DrawSessionLines()
        {
            Draw.Line(this, "ORHighLine", false, sessionStart, orHigh, sessionEnd, orHigh, 
                System.Windows.Media.Brushes.White, DashStyleHelper.Solid, 2);
            Draw.Line(this, "ORLowLine", false, sessionStart, orLow, sessionEnd, orLow, 
                System.Windows.Media.Brushes.White, DashStyleHelper.Solid, 2);
            Draw.Line(this, "LongEntryLine", false, sessionStart, longEntryLevel, sessionEnd, longEntryLevel, 
                System.Windows.Media.Brushes.Orange, DashStyleHelper.Dash, 2);
            Draw.Line(this, "ShortEntryLine", false, sessionStart, shortEntryLevel, sessionEnd, shortEntryLevel, 
                System.Windows.Media.Brushes.Orange, DashStyleHelper.Dash, 2);
        }
        
        private void DrawTradeLines()
        {
            if (!tradeLinesActive || tradeLineSignalBar < 0)
                return;

            int startBarsAgo = Math.Max(0, CurrentBar - tradeLineSignalBar);
            int endBarsAgo = tradeLineExitBar >= 0
                ? Math.Max(0, CurrentBar - tradeLineExitBar)
                : 0;

            DrawTradeLinesAtBarsAgo(startBarsAgo, endBarsAgo);
        }

        private void StartTradeLines(double tradeEntryPrice, double stopPrice, double takeProfitPrice, bool hasTakeProfit)
        {
            if (tradeLinesActive)
                FinalizeTradeLines();

            tradeLineTagPrefix = string.Format("ADAM_TradeLine_{0}_{1}_", ++tradeLineTagCounter, CurrentBar);
            tradeLinesActive = true;
            tradeLineHasTp = hasTakeProfit;
            tradeLineSignalBar = Math.Max(0, CurrentBar - 1);
            tradeLineExitBar = -1;
            tradeLineEntryPrice = Instrument.MasterInstrument.RoundToTickSize(tradeEntryPrice);
            tradeLineSlPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
            tradeLineTpPrice = hasTakeProfit ? Instrument.MasterInstrument.RoundToTickSize(takeProfitPrice) : 0.0;

            DrawTradeLinesAtBarsAgo(1, 0);
        }

        private void UpdateTradeLines()
        {
            DrawTradeLines();
        }

        private void FinalizeTradeLines()
        {
            if (!tradeLinesActive)
                return;

            tradeLineExitBar = CurrentBar;
            DrawTradeLines();
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

        private void LogDebug(string message)
        {
            if (!debugLogging)
                return;

            Print(message ?? string.Empty);
        }
        
        #endregion
        
        #region Info Panel
        
        private void UpdateInfoPanel()
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
                return "⛔";

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
            RemoveDrawObject("InfoPanel");
            RemoveDrawObject("Info");
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

            lines.Add((string.Format("ADAM v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(("Contracts:", ContractQuantity.ToString(CultureInfo.InvariantCulture), Brushes.LightGray, Brushes.LightGray));
            double orRange = 0.0;
            if (orSet && orHigh > double.MinValue && orLow < double.MaxValue)
                orRange = Math.Max(0.0, orHigh - orLow);

            string orSizeText = orRange > 0.0
                ? string.Format(CultureInfo.InvariantCulture, "{0:F2} pts", orRange)
                : "0 pts";
            lines.Add(("OR Size:", orSizeText, Brushes.LightGray, Brushes.LightGray));

            bool isArmed = false;
            if (Position.MarketPosition == MarketPosition.Flat
                && orSet
                && canTakeNewEntry
                && priceReturnedToOR
                && !maxLossTotalReached
                && !maxProfitTotalReached
                && !noBucketMatched)
            {
                TimeSpan currentTime = Time[0].TimeOfDay;
                TimeSpan cutOffTime = new TimeSpan(CutOffHour, CutOffMinute, 0);
                TimeSpan windowEnd = new TimeSpan(TradeWindowEndHour, TradeWindowEndMinute, 0);
                if (currentTime <= windowEnd && currentTime < cutOffTime)
                    isArmed = CanTakeLong(currentTime) || CanTakeShort(currentTime);
            }
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

        private bool ShowEntryConfirmation(string orderType, double price, int quantity)
        {
            if (State != State.Realtime)
                return true;

            bool result = false;
            if (System.Windows.Application.Current == null)
                return false;

            System.Windows.Application.Current.Dispatcher.Invoke(
                () =>
                {
                    string message = string.Format(CultureInfo.InvariantCulture, "Confirm {0} entry\nPrice: {1}\nQty: {2}", orderType, price, quantity);
                    MessageBoxResult res =
                        System.Windows.MessageBox.Show(message, "Entry Confirmation", System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);
                    result = res == System.Windows.MessageBoxResult.Yes;
                });

            return result;
        }
        
        private string GetStatusText()
        {
            if (noBucketMatched) return "NO BUCKET - NO TRADES";
            if (maxLossTotalReached) return "MAX LOSS - STOPPED";
            if (maxProfitTotalReached) return "MAX PROFIT - STOPPED";
            if (Position.MarketPosition == MarketPosition.Long) return "LONG TRADE";
            if (Position.MarketPosition == MarketPosition.Short) return "SHORT TRADE";
            
            string flags = "";
            if (maxLossLongReached) flags += " [L:LOSS]";
            if (maxLossShortReached) flags += " [S:LOSS]";
            if (maxProfitLongReached) flags += " [L:PROF]";
            if (maxProfitShortReached) flags += " [S:PROF]";
            
            bool noLongs = (activeL_MaxTrades > 0 && sessionTradeCountLong >= activeL_MaxTrades) || 
                          (activeL_MaxTradesTotal > 0 && sessionTradeCountTotal >= activeL_MaxTradesTotal) ||
                          maxLossLongReached || maxProfitLongReached || activeBucketL == 0;
            bool noShorts = (activeS_MaxTrades > 0 && sessionTradeCountShort >= activeS_MaxTrades) ||
                           (activeS_MaxTradesTotal > 0 && sessionTradeCountTotal >= activeS_MaxTradesTotal) ||
                           maxLossShortReached || maxProfitShortReached || activeBucketS == 0;
            
            if (noLongs && noShorts) return "MAX TRADES - DONE" + flags;
            if (noLongs) flags = " [SHORTS ONLY]" + flags;
            else if (noShorts) flags = " [LONGS ONLY]" + flags;
            if (!priceReturnedToOR) return "WAIT FOR OR" + flags;
            if (!isInSession && !noBucketMatched && orSet) return "WAITING" + flags;
            return "ACTIVE" + flags;
        }
        
        #endregion
        
        #region Execution Handling

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
            double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null || orderState != OrderState.Rejected)
                return;

            string name = order.Name ?? string.Empty;
            LogDebug(string.Format("{0} | REJECTED | name={1} state={2} error={3} comment={4}",
                time.ToString("HH:mm:ss.fff"), name, orderState, error, nativeError ?? string.Empty));

            if (IsEntrySignalName(name))
            {
                currentEntrySignal = string.Empty;
                SendWebhook("cancel");
            }
            bracketOrdersPlaced = false;

            if (Position.MarketPosition == MarketPosition.Flat)
                ResetForNextTrade();
        }
        
        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order == null || execution.Order.OrderState != OrderState.Filled) return;
            
            LogDebug(String.Format("{0} | FILL | {1} at {2} | Pos: {3}", 
                time.ToString("HH:mm:ss.fff"), execution.Order.Name, price, marketPosition));
            
            if (IsEntrySignalName(execution.Order.Name)
                && !bracketOrdersPlaced)
            {
                currentEntrySignal = execution.Order.Name;
                entryPrice = price;
                currentStopPrice = pendingStopPrice;
                currentTargetTicks = pendingTargetTicks;

                MarketPosition entryDirection = ResolveEntryDirection(execution.Order.Name, marketPosition);
                if (entryDirection == MarketPosition.Flat)
                    entryDirection = ResolveEntryDirection(execution.Order.Name, Position.MarketPosition);

                double sanitizedStop;
                double sanitizedTarget;
                if (TrySanitizeProtectivePrices(entryDirection, entryPrice, currentStopPrice, currentTargetTicks, out sanitizedStop, out sanitizedTarget)
                    && TryFinalSanitizeStopForLiveMarket(entryDirection, entryPrice, ref sanitizedStop))
                {
                    currentStopPrice = sanitizedStop;
                    currentTargetPrice = sanitizedTarget;

                    SetStopLoss(execution.Order.Name, CalculationMode.Price, currentStopPrice, false);
                    if (currentTargetTicks > 0 && currentTargetPrice > 0)
                        SetProfitTarget(execution.Order.Name, CalculationMode.Price, currentTargetPrice);

                    LogDebug(String.Format("  {0} | Entry={1} SL={2} TP={3}",
                        entryDirection == MarketPosition.Long ? "LONG" : "SHORT",
                        entryPrice, currentStopPrice, currentTargetTicks > 0 ? currentTargetPrice : 0));

                    StartTradeLines(entryPrice, currentStopPrice, currentTargetPrice, currentTargetTicks > 0 && currentTargetPrice > 0);

                    string entryAction;
                    bool isMarketEntry;
                    if (TryGetEntryWebhookAction(execution, out entryAction, out isMarketEntry))
                        SendWebhook(entryAction, entryPrice, currentTargetPrice, currentStopPrice, isMarketEntry, quantity);
                }
                else
                {
                    LogDebug(String.Format("  BRACKET SKIPPED | dir={0} entry={1} rawSL={2} rawTPt={3}",
                        entryDirection, entryPrice, currentStopPrice, currentTargetTicks));
                }

                bracketOrdersPlaced = true;
                pendingStopPrice = 0; pendingTargetTicks = 0;
                UpdateInfoPanel();
            }
            
            if (marketPosition == MarketPosition.Flat)
            {
                if (entryPrice > 0)
                {
                    double pnlTicks = 0;
                    bool wasLong = lastTradeDirection == MarketPosition.Long;
                    bool wasShort = lastTradeDirection == MarketPosition.Short;
                    if (lastTradeDirection == MarketPosition.Flat)
                    {
                        if (execution.Order.Name.Contains("Long")) wasLong = true;
                        else if (execution.Order.Name.Contains("Short")) wasShort = true;
                    }
                    
                    if (wasLong)
                    {
                        pnlTicks = Math.Round((price - entryPrice) / TickSize);
                        sessionPnLLong += pnlTicks;
                        if (pnlTicks < 0) sessionLossLong += Math.Abs(pnlTicks);
                    }
                    else if (wasShort)
                    {
                        pnlTicks = Math.Round((entryPrice - price) / TickSize);
                        sessionPnLShort += pnlTicks;
                        if (pnlTicks < 0) sessionLossShort += Math.Abs(pnlTicks);
                    }
                    
                    sessionPnLTotal += pnlTicks;
                    if (pnlTicks < 0) sessionLossTotal += Math.Abs(pnlTicks);
                    
                    LogDebug(String.Format("  {0} P&L: {1:F0}t | Session: T={2:F0} L={3:F0} S={4:F0}",
                        wasLong ? "LONG" : "SHORT", pnlTicks, sessionPnLTotal, sessionPnLLong, sessionPnLShort));
                }
                
                FinalizeTradeLines();
                LogDebug(String.Format("  *** EXIT via {0} ***", execution.Order.Name));
                if (ShouldSendExitWebhook(execution, execution.Order.Name ?? string.Empty, marketPosition))
                    SendWebhook("exit", 0, 0, 0, true, quantity);
                ResetForNextTrade();
            }
        }
        
        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat && entryPrice > 0)
            {
                LogDebug(String.Format("{0} | OnPositionUpdate: FLAT", Time[0].ToString("HH:mm:ss")));
                FinalizeTradeLines();
                ResetForNextTrade();
            }
        }
        
        #endregion
    }
}
