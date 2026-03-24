#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    /// <summary>
    /// ORBO - Opening Range Breakout Strategy v1.01
    /// 
    /// NEW IN V3: OR Size Buckets
    /// - 4 OR size buckets for Longs (L1-L4) and 4 for Shorts (S1-S4)
    /// - Each bucket defines an OR size range (min-max ticks) and has its own full parameter set
    /// - When OR is captured, the bot finds the matching bucket for each direction
    /// - If no bucket matches, that direction is skipped for the day
    /// </summary>
    public class ORBOibTesting : Strategy
    {
        private const string StrategySignalPrefix = "ORBOibTesting";
        private const string LongSignalPrefix = StrategySignalPrefix + "LongOR_";
        private const string ShortSignalPrefix = StrategySignalPrefix + "ShortOR_";
        private const string ExitSignalPrefix = StrategySignalPrefix;

        public ORBOibTesting()
        {
            VendorLicense(204);
        }
        
        #region Enums
        public enum TargetMode
        {
            PercentOfOR,
            FixedTicks
        }

        public enum WebhookProvider
        {
            TradersPost,
            ProjectX
        }
        #endregion
        
        #region Bucket Parameter Struct
        private struct BucketParams
        {
            public bool Enabled;
            public int ORMinTicks;
            public int ORMaxTicks;
            public bool UseBreakoutRearm;
            public bool RequireReturnToZone;
            public int ConfirmationBars;
            public double EntryOffsetPercent;
            public int VarianceTicks;
            public TargetMode TPMode;
            public double TakeProfitPercent;
            public int TakeProfitTicks;
            public TargetMode SLMode;
            public double StopLossPercent;
            public int StopLossTicks;
            public int MaxStopLossTicks;
            public bool UseTrailingStop;
            public double TrailStopPercent;
            public double TrailActivationPercent;
            public int TrailStepTicks;
            public bool TrailLockOREnabled;
            public double TrailLockORPercent;
            public bool TrailLockTicksEnabled;
            public int TrailLockTicks;
            public bool UseBreakeven;
            public int BreakevenTriggerPercent;
            public int BreakevenOffsetTicks;
            public int MaxBarsInTrade;
            public int MaxTradesPerDay;
            public bool UsePreMarketFilter;
            public int MaxPreMarketRangeTicks;
        }
        #endregion
        
        #region Private Variables
        
        // ===== Opening Range Tracking =====
        private double orHigh = double.MinValue;
        private double orLow = double.MaxValue;
        private double orRange = 0;
        private bool orCaptured = false;
        private bool wickLinesDrawn = false;
        
        // ===== Pre-Market Range Tracking =====
        private double pmrHigh = double.MinValue;
        private double pmrLow = double.MaxValue;
        private double pmrRange = 0;
        private double pmrSizeInTicks = 0;
        private bool pmrCaptured = false;
        
        // ===== Active Bucket State =====
        private BucketParams activeLongBucket;
        private BucketParams activeShortBucket;
        private bool longBucketFound = false;
        private bool shortBucketFound = false;
        private int activeLongBucketIndex = -1;
        private int activeShortBucketIndex = -1;
        
        // ===== Breakout State =====
        private bool breakoutActive = false;
        private bool longBreakoutOccurred = false;
        private bool shortBreakoutOccurred = false;
        private int breakoutBar = -1;
        
        // ===== Confirmation State =====
        private bool hasReturnedOnce = false;
        private bool waitingForConfirmation = false;
        private bool confirmationComplete = false;
        private int confirmationBarCount = 0;
        private int returnBar = -1;
        
        // ===== Entry State =====
        private int tradeCount = 0;
        private int longTradeCount = 0;
        private int shortTradeCount = 0;
        private bool orderPlaced = false;
        private double entryPrice = 0;
        private double limitEntryPrice = 0;
        private double lastFilledEntryPrice = 0;
        private int entryBar = -1;
        private int entryOrderBar = -1;
        private Order entryOrder = null;
        private string currentSignalName = "";
        private bool lastTradeWasLong = false;
        
        // ===== Breakeven State =====
        private bool beTriggerActive = false;
        
        // ===== Trailing Stop State =====
        private double trailStopPrice = 0;
        private double bestPriceSinceEntry = 0;
        private bool trailActivated = false;
        private bool trailLocked = false;
        
        // ===== Session State =====
        private DateTime lastDate = DateTime.MinValue;
        private bool maxAccountLimitHit = false;
        private double startingBalance = 0;
        private bool isConfiguredTimeframeValid = true;
        private bool isConfiguredInstrumentValid = true;
        private bool timeframePopupShown;
        private bool instrumentPopupShown;
        
        // ===== Session P&L Tracking =====
        private double sessionRealizedPnL = 0;
        private bool sessionProfitLimitHit = false;
        private bool sessionLossLimitHit = false;
        
        // ===== Time References =====
        private TimeSpan orStartTime;
        private TimeSpan orEndTime;
        private TimeSpan sessionEndTime;
        private TimeSpan noTradesAfterTime;
        private TimeSpan skipStartTime;
        private TimeSpan skipEndTime;
        private TimeSpan preMarketStartTime;
        private TimeSpan preMarketEndTime;
        private bool wasInNoTradesAfterWindow;
        private bool wasInSkipWindow;
        private bool wasInNewsSkipWindow;
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
        
        // ===== Random for variance =====
        private Random random = new Random();

        // ===== Webhooks =====
        private string projectXSessionToken;
        private DateTime projectXTokenAcquiredUtc = Core.Globals.MinDate;
        private int? projectXLastOrderId;
        private string projectXLastOrderContractId;

        // ===== Info Box Overlay =====
        private Border infoBoxContainer;
        private StackPanel infoBoxRowsPanel;
        private bool legacyInfoDrawingsCleared;
        private static readonly Brush InfoHeaderFooterGradientBrush = CreateFrozenVerticalGradientBrush(
            Color.FromArgb(240, 0x2A, 0x2F, 0x45),
            Color.FromArgb(240, 0x1E, 0x23, 0x36),
            Color.FromArgb(240, 0x14, 0x18, 0x28));
        private static readonly Brush InfoBodyOddBrush = CreateFrozenBrush(240, 0x0F, 0x0F, 0x17);
        private static readonly Brush InfoBodyEvenBrush = CreateFrozenBrush(240, 0x11, 0x11, 0x18);
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(255, 0xFF, 0xD7, 0x00);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);
        private static readonly Brush PassedNewsRowBrush = CreateFrozenBrush(30, 211, 211, 211);
        
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"ORBO2ibTesting";
                
                Name = "ORBO2ibTesting";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                IsInstantiatedOnEachOptimizationIteration = false;
                
                // ===== A. General Settings =====
                NumberOfContracts = 1;
                MaxAccountBalance = 0;
                RequireEntryConfirmation = false;
                DebugMode = false;

                // ===== B. Long Bucket 1 =====
                L1_Enabled = true;
                L1_ORMinTicks = 155;
                L1_ORMaxTicks = 300;
                L1_UsePreMarketFilter = false;
                L1_MaxPreMarketRangeTicks = 0;
                L1_UseBreakoutRearm = true;
                L1_RequireReturnToZone = true;
                L1_ConfirmationBars = 2;
                L1_EntryOffsetPercent = 26.91;
                L1_VarianceTicks = 0;
                L1_TPMode = TargetMode.PercentOfOR;
                L1_TakeProfitPercent = 81.43;
                L1_TakeProfitTicks = 331;
                L1_SLMode = TargetMode.PercentOfOR;
                L1_StopLossPercent = 72.4;
                L1_StopLossTicks = 325;
                L1_MaxStopLossTicks = 152;
                L1_UseTrailingStop = true;
                L1_TrailStopPercent = 37.4;
                L1_TrailActivationPercent = 30.9;
                L1_TrailStepTicks = 0;
                L1_TrailLockOREnabled = false;
                L1_TrailLockORPercent = 17.0;
                L1_TrailLockTicksEnabled = false;
                L1_TrailLockTicks = 20;
                L1_UseBreakeven = true;
                L1_BreakevenTriggerPercent = 32;
                L1_BreakevenOffsetTicks = 2;
                L1_MaxBarsInTrade = 93;
                L1_MaxTradesPerDay = 3;

                // ===== C. Long Bucket 2 =====
                L2_Enabled = true;
                L2_ORMinTicks = 301;
                L2_ORMaxTicks = 500;
                L2_UsePreMarketFilter = false;
                L2_MaxPreMarketRangeTicks = 0;
                L2_UseBreakoutRearm = true;
                L2_RequireReturnToZone = true;
                L2_ConfirmationBars = 9;
                L2_EntryOffsetPercent = 2.6;
                L2_VarianceTicks = 0;
                L2_TPMode = TargetMode.PercentOfOR;
                L2_TakeProfitPercent = 97.9;
                L2_TakeProfitTicks = 331;
                L2_SLMode = TargetMode.PercentOfOR;
                L2_StopLossPercent = 98.85;
                L2_StopLossTicks = 325;
                L2_MaxStopLossTicks = 403;
                L2_UseTrailingStop = true;
                L2_TrailStopPercent = 68.3;
                L2_TrailActivationPercent = 7.5;
                L2_TrailStepTicks = 0;
                L2_TrailLockOREnabled = false;
                L2_TrailLockORPercent = 14.0;
                L2_TrailLockTicksEnabled = false;
                L2_TrailLockTicks = 20;
                L2_UseBreakeven = true;
                L2_BreakevenTriggerPercent = 12;
                L2_BreakevenOffsetTicks = 5;
                L2_MaxBarsInTrade = 103;
                L2_MaxTradesPerDay = 3;

                // ===== D. Long Bucket 3 =====
                L3_Enabled = true;
                L3_ORMinTicks = 501;
                L3_ORMaxTicks = 750;
                L3_UsePreMarketFilter = false;
                L3_MaxPreMarketRangeTicks = 0;
                L3_UseBreakoutRearm = true;
                L3_RequireReturnToZone = true;
                L3_ConfirmationBars = 4;
                L3_EntryOffsetPercent = 13.64;
                L3_VarianceTicks = 0;
                L3_TPMode = TargetMode.PercentOfOR;
                L3_TakeProfitPercent = 22.96;
                L3_TakeProfitTicks = 331;
                L3_SLMode = TargetMode.PercentOfOR;
                L3_StopLossPercent = 13.89;
                L3_StopLossTicks = 325;
                L3_MaxStopLossTicks = 100;
                L3_UseTrailingStop = true;
                L3_TrailStopPercent = 16.8;
                L3_TrailActivationPercent = 17.5;
                L3_TrailStepTicks = 0;
                L3_TrailLockOREnabled = true;
                L3_TrailLockORPercent = 40.0;
                L3_TrailLockTicksEnabled = false;
                L3_TrailLockTicks = 20;
                L3_UseBreakeven = true;
                L3_BreakevenTriggerPercent = 20;
                L3_BreakevenOffsetTicks = 7;
                L3_MaxBarsInTrade = 127;
                L3_MaxTradesPerDay = 5;

                // ===== E. Long Bucket 4 =====
                L4_Enabled = true;
                L4_ORMinTicks = 751;
                L4_ORMaxTicks = 1140;
                L4_UsePreMarketFilter = false;
                L4_MaxPreMarketRangeTicks = 0;
                L4_UseBreakoutRearm = true;
                L4_RequireReturnToZone = true;
                L4_ConfirmationBars = 3;
                L4_EntryOffsetPercent = 36.41;
                L4_VarianceTicks = 0;
                L4_TPMode = TargetMode.PercentOfOR;
                L4_TakeProfitPercent = 68.8;
                L4_TakeProfitTicks = 331;
                L4_SLMode = TargetMode.PercentOfOR;
                L4_StopLossPercent = 40.9;
                L4_StopLossTicks = 325;
                L4_MaxStopLossTicks = 318;
                L4_UseTrailingStop = true;
                L4_TrailStopPercent = 36.4;
                L4_TrailActivationPercent = 37.1;
                L4_TrailStepTicks = 0;
                L4_TrailLockOREnabled = false;
                L4_TrailLockORPercent = 15.0;
                L4_TrailLockTicksEnabled = false;
                L4_TrailLockTicks = 20;
                L4_UseBreakeven = true;
                L4_BreakevenTriggerPercent = 45;
                L4_BreakevenOffsetTicks = 20;
                L4_MaxBarsInTrade = 188;
                L4_MaxTradesPerDay = 2;

                // ===== F. Short Bucket 1 =====
                S1_Enabled = true;
                S1_ORMinTicks = 120;
                S1_ORMaxTicks = 300;
                S1_UsePreMarketFilter = false;
                S1_MaxPreMarketRangeTicks = 0;
                S1_UseBreakoutRearm = true;
                S1_RequireReturnToZone = false;
                S1_ConfirmationBars = 4;
                S1_EntryOffsetPercent = 2.91;
                S1_VarianceTicks = 0;
                S1_TPMode = TargetMode.PercentOfOR;
                S1_TakeProfitPercent = 150.0;
                S1_TakeProfitTicks = 268;
                S1_SLMode = TargetMode.PercentOfOR;
                S1_StopLossPercent = 61.9;
                S1_StopLossTicks = 145;
                S1_MaxStopLossTicks = 152;
                S1_UseTrailingStop = false;
                S1_TrailStopPercent = 40.2;
                S1_TrailActivationPercent = 37.0;
                S1_TrailStepTicks = 0;
                S1_TrailLockOREnabled = false;
                S1_TrailLockORPercent = 14.0;
                S1_TrailLockTicksEnabled = false;
                S1_TrailLockTicks = 20;
                S1_UseBreakeven = true;
                S1_BreakevenTriggerPercent = 36;
                S1_BreakevenOffsetTicks = 20;
                S1_MaxBarsInTrade = 62;
                S1_MaxTradesPerDay = 4;

                // ===== G. Short Bucket 2 =====
                S2_Enabled = true;
                S2_ORMinTicks = 301;
                S2_ORMaxTicks = 500;
                S2_UsePreMarketFilter = false;
                S2_MaxPreMarketRangeTicks = 0;
                S2_UseBreakoutRearm = true;
                S2_RequireReturnToZone = true;
                S2_ConfirmationBars = 5;
                S2_EntryOffsetPercent = 16.5;
                S2_VarianceTicks = 0;
                S2_TPMode = TargetMode.PercentOfOR;
                S2_TakeProfitPercent = 87.0;
                S2_TakeProfitTicks = 268;
                S2_SLMode = TargetMode.PercentOfOR;
                S2_StopLossPercent = 60.0;
                S2_StopLossTicks = 145;
                S2_MaxStopLossTicks = 221;
                S2_UseTrailingStop = true;
                S2_TrailStopPercent = 72.0;
                S2_TrailActivationPercent = 5.5;
                S2_TrailStepTicks = 0;
                S2_TrailLockOREnabled = false;
                S2_TrailLockORPercent = 14.0;
                S2_TrailLockTicksEnabled = false;
                S2_TrailLockTicks = 20;
                S2_UseBreakeven = true;
                S2_BreakevenTriggerPercent = 15;
                S2_BreakevenOffsetTicks = 18;
                S2_MaxBarsInTrade = 16;
                S2_MaxTradesPerDay = 4;

                // ===== H. Short Bucket 3 =====
                S3_Enabled = true;
                S3_ORMinTicks = 501;
                S3_ORMaxTicks = 750;
                S3_UsePreMarketFilter = false;
                S3_MaxPreMarketRangeTicks = 0;
                S3_UseBreakoutRearm = true;
                S3_RequireReturnToZone = true;
                S3_ConfirmationBars = 8;
                S3_EntryOffsetPercent = 18.44;
                S3_VarianceTicks = 0;
                S3_TPMode = TargetMode.PercentOfOR;
                S3_TakeProfitPercent = 66.61;
                S3_TakeProfitTicks = 268;
                S3_SLMode = TargetMode.PercentOfOR;
                S3_StopLossPercent = 56.0;
                S3_StopLossTicks = 145;
                S3_MaxStopLossTicks = 350;
                S3_UseTrailingStop = true;
                S3_TrailStopPercent = 44.0;
                S3_TrailActivationPercent = 12.0;
                S3_TrailStepTicks = 0;
                S3_TrailLockOREnabled = false;
                S3_TrailLockORPercent = 7.0;
                S3_TrailLockTicksEnabled = false;
                S3_TrailLockTicks = 20;
                S3_UseBreakeven = true;
                S3_BreakevenTriggerPercent = 20;
                S3_BreakevenOffsetTicks = 19;
                S3_MaxBarsInTrade = 0;
                S3_MaxTradesPerDay = 2;

                // ===== I. Short Bucket 4 =====
                S4_Enabled = true;
                S4_ORMinTicks = 751;
                S4_ORMaxTicks = 1260;
                S4_UsePreMarketFilter = false;
                S4_MaxPreMarketRangeTicks = 0;
                S4_UseBreakoutRearm = true;
                S4_RequireReturnToZone = true;
                S4_ConfirmationBars = 1;
                S4_EntryOffsetPercent = 5.83;
                S4_VarianceTicks = 0;
                S4_TPMode = TargetMode.PercentOfOR;
                S4_TakeProfitPercent = 66.4;
                S4_TakeProfitTicks = 268;
                S4_SLMode = TargetMode.PercentOfOR;
                S4_StopLossPercent = 34.25;
                S4_StopLossTicks = 145;
                S4_MaxStopLossTicks = 299;
                S4_UseTrailingStop = true;
                S4_TrailStopPercent = 35.4;
                S4_TrailActivationPercent = 48.7;
                S4_TrailStepTicks = 0;
                S4_TrailLockOREnabled = false;
                S4_TrailLockORPercent = 50.0;
                S4_TrailLockTicksEnabled = false;
                S4_TrailLockTicks = 20;
                S4_UseBreakeven = true;
                S4_BreakevenTriggerPercent = 52;
                S4_BreakevenOffsetTicks = 20;
                S4_MaxBarsInTrade = 180;
                S4_MaxTradesPerDay = 1;

                // ===== J. Order Management =====
                CancelOrderPercent = 0;
                CancelOrderBars = 0;
                
                // ===== K. Session Risk Management =====
                MaxSessionProfitTicks = 1020;
                MaxSessionLossTicks = 600;
                MaxTradesPerDay = 7;
                
                // ===== L. Session Time =====
                ORStartTime = DateTime.Parse("09:30").TimeOfDay;
                OREndTime = DateTime.Parse("10:30").TimeOfDay;
                SessionEnd = DateTime.Parse("15:20").TimeOfDay;
                NoTradesAfter = DateTime.Parse("14:45").TimeOfDay;
                
                // ===== M. Skip Times =====
                SkipStart = DateTime.Parse("00:00").TimeOfDay;
                SkipEnd = DateTime.Parse("00:00").TimeOfDay;

                // ===== Pre-Market Range =====
                PreMarketStart = DateTime.Parse("06:00").TimeOfDay;
                PreMarketEnd = DateTime.Parse("09:30").TimeOfDay;

                UseNewsSkip = true;
                NewsBlockMinutes = 1;

                WebhookUrl = string.Empty;
                WebhookProviderType = WebhookProvider.TradersPost;
                ProjectXApiBaseUrl = "https://gateway-api-demo.s2f.projectx.com";
                ProjectXUsername = string.Empty;
                ProjectXApiKey = string.Empty;
                ProjectXAccountId = string.Empty;
                ProjectXContractId = string.Empty;
                
                // ===== N. Visual =====
                RangeBoxBrush = Brushes.DodgerBlue;
                ShowEntryLines = true;
                ShowTargetLines = true;
                ShowStopLines = true;
            }
            else if (State == State.Configure)
            {
                orStartTime = ORStartTime;
                orEndTime = OREndTime;
                sessionEndTime = SessionEnd;
                noTradesAfterTime = NoTradesAfter;
                skipStartTime = SkipStart;
                skipEndTime = SkipEnd;
                preMarketStartTime = PreMarketStart;
                preMarketEndTime = PreMarketEnd;
            }
            else if (State == State.DataLoaded)
            {
                ValidateRequiredPrimaryTimeframe(1);
                ValidateRequiredPrimaryInstrument();
                startingBalance = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                EnsureNewsDatesInitialized();
            }
            else if (State == State.Terminated)
            {
                DisposeInfoBoxOverlay();
            }
        }

        protected override void OnBarUpdate()
        {
            if (!isConfiguredTimeframeValid || !isConfiguredInstrumentValid)
            {
                CancelAllOrders();
                if (Position.MarketPosition != MarketPosition.Flat)
                    ExitAllPositions("InvalidConfiguration");
                return;
            }

            DrawSessionTimeWindows();

            if (CurrentBar < BarsRequiredToTrade)
                return;
            ResetDailyStateIfNeeded();
            CheckSessionPnLLimits();
            
            if (sessionProfitLimitHit || sessionLossLimitHit)
                return;
            
            if (ShouldAccountBalanceExit())
            {
                ExitAllPositions("MaxBalance");
                return;
            }
            
            ExitIfSessionEnded();
            if (IsAfterSessionEnd(Time[0]))
            {
                CancelAllOrders();
                return;
            }
            HandleNoTradeAndSkipTransitions();

            bool inNoTradesAfter = IsInNoTradesAfterWindow(Time[0]);
            bool inSkipWindow = IsInSkipWindow(Time[0]);
            bool inNewsSkipWindow = IsInNewsSkipWindow(Time[0]);
            if (inNoTradesAfter || inSkipWindow || inNewsSkipWindow)
                return;
            
            CaptureOpeningRange();
            
            if (orCaptured && (longBucketFound || shortBucketFound))
            {
                MonitorBreakoutAndConfirmation();
                TryEntryWithConfirmation();
            }
            
            ManagePosition();
            UpdateInfoText();
        }

        #region Bucket Resolution
        
        private BucketParams GetLongBucketParams(int bucketIndex)
        {
            BucketParams p = new BucketParams();
            switch (bucketIndex)
            {
                case 1:
                    p.Enabled = L1_Enabled;
                    p.ORMinTicks = L1_ORMinTicks;
                    p.ORMaxTicks = L1_ORMaxTicks;
                    p.UseBreakoutRearm = L1_UseBreakoutRearm;
                    p.RequireReturnToZone = L1_RequireReturnToZone;
                    p.ConfirmationBars = L1_ConfirmationBars;
                    p.EntryOffsetPercent = L1_EntryOffsetPercent;
                    p.VarianceTicks = L1_VarianceTicks;
                    p.TPMode = L1_TPMode;
                    p.TakeProfitPercent = L1_TakeProfitPercent;
                    p.TakeProfitTicks = L1_TakeProfitTicks;
                    p.SLMode = L1_SLMode;
                    p.StopLossPercent = L1_StopLossPercent;
                    p.StopLossTicks = L1_StopLossTicks;
                    p.MaxStopLossTicks = L1_MaxStopLossTicks;
                    p.UseTrailingStop = L1_UseTrailingStop;
                    p.TrailStopPercent = L1_TrailStopPercent;
                    p.TrailActivationPercent = L1_TrailActivationPercent;
                    p.TrailStepTicks = L1_TrailStepTicks;
                    p.TrailLockOREnabled = L1_TrailLockOREnabled;
                    p.TrailLockORPercent = L1_TrailLockORPercent;
                    p.TrailLockTicksEnabled = L1_TrailLockTicksEnabled;
                    p.TrailLockTicks = L1_TrailLockTicks;
                    p.UseBreakeven = L1_UseBreakeven;
                    p.BreakevenTriggerPercent = L1_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = L1_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = L1_MaxBarsInTrade;
                    p.MaxTradesPerDay = L1_MaxTradesPerDay;
                    p.UsePreMarketFilter = L1_UsePreMarketFilter;
                    p.MaxPreMarketRangeTicks = L1_MaxPreMarketRangeTicks;
                    break;
                case 2:
                    p.Enabled = L2_Enabled;
                    p.ORMinTicks = L2_ORMinTicks;
                    p.ORMaxTicks = L2_ORMaxTicks;
                    p.UseBreakoutRearm = L2_UseBreakoutRearm;
                    p.RequireReturnToZone = L2_RequireReturnToZone;
                    p.ConfirmationBars = L2_ConfirmationBars;
                    p.EntryOffsetPercent = L2_EntryOffsetPercent;
                    p.VarianceTicks = L2_VarianceTicks;
                    p.TPMode = L2_TPMode;
                    p.TakeProfitPercent = L2_TakeProfitPercent;
                    p.TakeProfitTicks = L2_TakeProfitTicks;
                    p.SLMode = L2_SLMode;
                    p.StopLossPercent = L2_StopLossPercent;
                    p.StopLossTicks = L2_StopLossTicks;
                    p.MaxStopLossTicks = L2_MaxStopLossTicks;
                    p.UseTrailingStop = L2_UseTrailingStop;
                    p.TrailStopPercent = L2_TrailStopPercent;
                    p.TrailActivationPercent = L2_TrailActivationPercent;
                    p.TrailStepTicks = L2_TrailStepTicks;
                    p.TrailLockOREnabled = L2_TrailLockOREnabled;
                    p.TrailLockORPercent = L2_TrailLockORPercent;
                    p.TrailLockTicksEnabled = L2_TrailLockTicksEnabled;
                    p.TrailLockTicks = L2_TrailLockTicks;
                    p.UseBreakeven = L2_UseBreakeven;
                    p.BreakevenTriggerPercent = L2_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = L2_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = L2_MaxBarsInTrade;
                    p.MaxTradesPerDay = L2_MaxTradesPerDay;
                    p.UsePreMarketFilter = L2_UsePreMarketFilter;
                    p.MaxPreMarketRangeTicks = L2_MaxPreMarketRangeTicks;
                    break;
                case 3:
                    p.Enabled = L3_Enabled;
                    p.ORMinTicks = L3_ORMinTicks;
                    p.ORMaxTicks = L3_ORMaxTicks;
                    p.UseBreakoutRearm = L3_UseBreakoutRearm;
                    p.RequireReturnToZone = L3_RequireReturnToZone;
                    p.ConfirmationBars = L3_ConfirmationBars;
                    p.EntryOffsetPercent = L3_EntryOffsetPercent;
                    p.VarianceTicks = L3_VarianceTicks;
                    p.TPMode = L3_TPMode;
                    p.TakeProfitPercent = L3_TakeProfitPercent;
                    p.TakeProfitTicks = L3_TakeProfitTicks;
                    p.SLMode = L3_SLMode;
                    p.StopLossPercent = L3_StopLossPercent;
                    p.StopLossTicks = L3_StopLossTicks;
                    p.MaxStopLossTicks = L3_MaxStopLossTicks;
                    p.UseTrailingStop = L3_UseTrailingStop;
                    p.TrailStopPercent = L3_TrailStopPercent;
                    p.TrailActivationPercent = L3_TrailActivationPercent;
                    p.TrailStepTicks = L3_TrailStepTicks;
                    p.TrailLockOREnabled = L3_TrailLockOREnabled;
                    p.TrailLockORPercent = L3_TrailLockORPercent;
                    p.TrailLockTicksEnabled = L3_TrailLockTicksEnabled;
                    p.TrailLockTicks = L3_TrailLockTicks;
                    p.UseBreakeven = L3_UseBreakeven;
                    p.BreakevenTriggerPercent = L3_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = L3_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = L3_MaxBarsInTrade;
                    p.MaxTradesPerDay = L3_MaxTradesPerDay;
                    p.UsePreMarketFilter = L3_UsePreMarketFilter;
                    p.MaxPreMarketRangeTicks = L3_MaxPreMarketRangeTicks;
                    break;
                case 4:
                    p.Enabled = L4_Enabled;
                    p.ORMinTicks = L4_ORMinTicks;
                    p.ORMaxTicks = L4_ORMaxTicks;
                    p.UseBreakoutRearm = L4_UseBreakoutRearm;
                    p.RequireReturnToZone = L4_RequireReturnToZone;
                    p.ConfirmationBars = L4_ConfirmationBars;
                    p.EntryOffsetPercent = L4_EntryOffsetPercent;
                    p.VarianceTicks = L4_VarianceTicks;
                    p.TPMode = L4_TPMode;
                    p.TakeProfitPercent = L4_TakeProfitPercent;
                    p.TakeProfitTicks = L4_TakeProfitTicks;
                    p.SLMode = L4_SLMode;
                    p.StopLossPercent = L4_StopLossPercent;
                    p.StopLossTicks = L4_StopLossTicks;
                    p.MaxStopLossTicks = L4_MaxStopLossTicks;
                    p.UseTrailingStop = L4_UseTrailingStop;
                    p.TrailStopPercent = L4_TrailStopPercent;
                    p.TrailActivationPercent = L4_TrailActivationPercent;
                    p.TrailStepTicks = L4_TrailStepTicks;
                    p.TrailLockOREnabled = L4_TrailLockOREnabled;
                    p.TrailLockORPercent = L4_TrailLockORPercent;
                    p.TrailLockTicksEnabled = L4_TrailLockTicksEnabled;
                    p.TrailLockTicks = L4_TrailLockTicks;
                    p.UseBreakeven = L4_UseBreakeven;
                    p.BreakevenTriggerPercent = L4_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = L4_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = L4_MaxBarsInTrade;
                    p.MaxTradesPerDay = L4_MaxTradesPerDay;
                    p.UsePreMarketFilter = L4_UsePreMarketFilter;
                    p.MaxPreMarketRangeTicks = L4_MaxPreMarketRangeTicks;
                    break;
            }
            return p;
        }
        
        private BucketParams GetShortBucketParams(int bucketIndex)
        {
            BucketParams p = new BucketParams();
            switch (bucketIndex)
            {
                case 1:
                    p.Enabled = S1_Enabled;
                    p.ORMinTicks = S1_ORMinTicks;
                    p.ORMaxTicks = S1_ORMaxTicks;
                    p.UseBreakoutRearm = S1_UseBreakoutRearm;
                    p.RequireReturnToZone = S1_RequireReturnToZone;
                    p.ConfirmationBars = S1_ConfirmationBars;
                    p.EntryOffsetPercent = S1_EntryOffsetPercent;
                    p.VarianceTicks = S1_VarianceTicks;
                    p.TPMode = S1_TPMode;
                    p.TakeProfitPercent = S1_TakeProfitPercent;
                    p.TakeProfitTicks = S1_TakeProfitTicks;
                    p.SLMode = S1_SLMode;
                    p.StopLossPercent = S1_StopLossPercent;
                    p.StopLossTicks = S1_StopLossTicks;
                    p.MaxStopLossTicks = S1_MaxStopLossTicks;
                    p.UseTrailingStop = S1_UseTrailingStop;
                    p.TrailStopPercent = S1_TrailStopPercent;
                    p.TrailActivationPercent = S1_TrailActivationPercent;
                    p.TrailStepTicks = S1_TrailStepTicks;
                    p.TrailLockOREnabled = S1_TrailLockOREnabled;
                    p.TrailLockORPercent = S1_TrailLockORPercent;
                    p.TrailLockTicksEnabled = S1_TrailLockTicksEnabled;
                    p.TrailLockTicks = S1_TrailLockTicks;
                    p.UseBreakeven = S1_UseBreakeven;
                    p.BreakevenTriggerPercent = S1_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = S1_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = S1_MaxBarsInTrade;
                    p.MaxTradesPerDay = S1_MaxTradesPerDay;
                    p.UsePreMarketFilter = S1_UsePreMarketFilter;
                    p.MaxPreMarketRangeTicks = S1_MaxPreMarketRangeTicks;
                    break;
                case 2:
                    p.Enabled = S2_Enabled;
                    p.ORMinTicks = S2_ORMinTicks;
                    p.ORMaxTicks = S2_ORMaxTicks;
                    p.UseBreakoutRearm = S2_UseBreakoutRearm;
                    p.RequireReturnToZone = S2_RequireReturnToZone;
                    p.ConfirmationBars = S2_ConfirmationBars;
                    p.EntryOffsetPercent = S2_EntryOffsetPercent;
                    p.VarianceTicks = S2_VarianceTicks;
                    p.TPMode = S2_TPMode;
                    p.TakeProfitPercent = S2_TakeProfitPercent;
                    p.TakeProfitTicks = S2_TakeProfitTicks;
                    p.SLMode = S2_SLMode;
                    p.StopLossPercent = S2_StopLossPercent;
                    p.StopLossTicks = S2_StopLossTicks;
                    p.MaxStopLossTicks = S2_MaxStopLossTicks;
                    p.UseTrailingStop = S2_UseTrailingStop;
                    p.TrailStopPercent = S2_TrailStopPercent;
                    p.TrailActivationPercent = S2_TrailActivationPercent;
                    p.TrailStepTicks = S2_TrailStepTicks;
                    p.TrailLockOREnabled = S2_TrailLockOREnabled;
                    p.TrailLockORPercent = S2_TrailLockORPercent;
                    p.TrailLockTicksEnabled = S2_TrailLockTicksEnabled;
                    p.TrailLockTicks = S2_TrailLockTicks;
                    p.UseBreakeven = S2_UseBreakeven;
                    p.BreakevenTriggerPercent = S2_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = S2_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = S2_MaxBarsInTrade;
                    p.MaxTradesPerDay = S2_MaxTradesPerDay;
                    p.UsePreMarketFilter = S2_UsePreMarketFilter;
                    p.MaxPreMarketRangeTicks = S2_MaxPreMarketRangeTicks;
                    break;
                case 3:
                    p.Enabled = S3_Enabled;
                    p.ORMinTicks = S3_ORMinTicks;
                    p.ORMaxTicks = S3_ORMaxTicks;
                    p.UseBreakoutRearm = S3_UseBreakoutRearm;
                    p.RequireReturnToZone = S3_RequireReturnToZone;
                    p.ConfirmationBars = S3_ConfirmationBars;
                    p.EntryOffsetPercent = S3_EntryOffsetPercent;
                    p.VarianceTicks = S3_VarianceTicks;
                    p.TPMode = S3_TPMode;
                    p.TakeProfitPercent = S3_TakeProfitPercent;
                    p.TakeProfitTicks = S3_TakeProfitTicks;
                    p.SLMode = S3_SLMode;
                    p.StopLossPercent = S3_StopLossPercent;
                    p.StopLossTicks = S3_StopLossTicks;
                    p.MaxStopLossTicks = S3_MaxStopLossTicks;
                    p.UseTrailingStop = S3_UseTrailingStop;
                    p.TrailStopPercent = S3_TrailStopPercent;
                    p.TrailActivationPercent = S3_TrailActivationPercent;
                    p.TrailStepTicks = S3_TrailStepTicks;
                    p.TrailLockOREnabled = S3_TrailLockOREnabled;
                    p.TrailLockORPercent = S3_TrailLockORPercent;
                    p.TrailLockTicksEnabled = S3_TrailLockTicksEnabled;
                    p.TrailLockTicks = S3_TrailLockTicks;
                    p.UseBreakeven = S3_UseBreakeven;
                    p.BreakevenTriggerPercent = S3_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = S3_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = S3_MaxBarsInTrade;
                    p.MaxTradesPerDay = S3_MaxTradesPerDay;
                    p.UsePreMarketFilter = S3_UsePreMarketFilter;
                    p.MaxPreMarketRangeTicks = S3_MaxPreMarketRangeTicks;
                    break;
                case 4:
                    p.Enabled = S4_Enabled;
                    p.ORMinTicks = S4_ORMinTicks;
                    p.ORMaxTicks = S4_ORMaxTicks;
                    p.UseBreakoutRearm = S4_UseBreakoutRearm;
                    p.RequireReturnToZone = S4_RequireReturnToZone;
                    p.ConfirmationBars = S4_ConfirmationBars;
                    p.EntryOffsetPercent = S4_EntryOffsetPercent;
                    p.VarianceTicks = S4_VarianceTicks;
                    p.TPMode = S4_TPMode;
                    p.TakeProfitPercent = S4_TakeProfitPercent;
                    p.TakeProfitTicks = S4_TakeProfitTicks;
                    p.SLMode = S4_SLMode;
                    p.StopLossPercent = S4_StopLossPercent;
                    p.StopLossTicks = S4_StopLossTicks;
                    p.MaxStopLossTicks = S4_MaxStopLossTicks;
                    p.UseTrailingStop = S4_UseTrailingStop;
                    p.TrailStopPercent = S4_TrailStopPercent;
                    p.TrailActivationPercent = S4_TrailActivationPercent;
                    p.TrailStepTicks = S4_TrailStepTicks;
                    p.TrailLockOREnabled = S4_TrailLockOREnabled;
                    p.TrailLockORPercent = S4_TrailLockORPercent;
                    p.TrailLockTicksEnabled = S4_TrailLockTicksEnabled;
                    p.TrailLockTicks = S4_TrailLockTicks;
                    p.UseBreakeven = S4_UseBreakeven;
                    p.BreakevenTriggerPercent = S4_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = S4_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = S4_MaxBarsInTrade;
                    p.MaxTradesPerDay = S4_MaxTradesPerDay;
                    p.UsePreMarketFilter = S4_UsePreMarketFilter;
                    p.MaxPreMarketRangeTicks = S4_MaxPreMarketRangeTicks;
                    break;
            }
            return p;
        }
        
        private bool ResolveLongBucket(double orSizeInTicks)
        {
            for (int i = 1; i <= 4; i++)
            {
                BucketParams bp = GetLongBucketParams(i);
                if (bp.Enabled && orSizeInTicks >= bp.ORMinTicks && orSizeInTicks <= bp.ORMaxTicks)
                {
                    activeLongBucket = bp;
                    activeLongBucketIndex = i;
                    return true;
                }
            }
            return false;
        }
        
        private bool ResolveShortBucket(double orSizeInTicks)
        {
            for (int i = 1; i <= 4; i++)
            {
                BucketParams bp = GetShortBucketParams(i);
                if (bp.Enabled && orSizeInTicks >= bp.ORMinTicks && orSizeInTicks <= bp.ORMaxTicks)
                {
                    activeShortBucket = bp;
                    activeShortBucketIndex = i;
                    return true;
                }
            }
            return false;
        }
        
        #endregion

        #region Opening Range Capture
        
        private void CaptureOpeningRange()
        {
            TimeSpan currentTime = Time[0].TimeOfDay;
            
            // === Pre-Market Range Capture ===
            CapturePreMarketRange(currentTime);
            
            if (currentTime >= orStartTime && currentTime < orEndTime)
            {
                if (orHigh == double.MinValue) orHigh = High[0];
                if (orLow == double.MaxValue) orLow = Low[0];
                if (High[0] > orHigh) orHigh = High[0];
                if (Low[0] < orLow) orLow = Low[0];
                
                if (DebugMode)
                    DebugPrint($"OR Building: High={orHigh:F2}, Low={orLow:F2}");
            }
            else if (!orCaptured && currentTime >= orEndTime)
            {
                if (orHigh == double.MinValue || orLow == double.MaxValue)
                    ReconstructORFromHistory();
                
                if (orHigh != double.MinValue && orLow != double.MaxValue && orHigh > orLow)
                {
                    orCaptured = true;
                    orRange = orHigh - orLow;
                    double orSizeInTicks = orRange / TickSize;
                    
                    longBucketFound = ResolveLongBucket(orSizeInTicks);
                    shortBucketFound = ResolveShortBucket(orSizeInTicks);
                    
                    // === Apply Pre-Market Range Filter ===
                    ApplyPreMarketRangeFilter();
                    
                    breakoutActive = longBucketFound || shortBucketFound;
                    
                    if (DebugMode)
                    {
                        DebugPrint($"=== OR CAPTURED ===");
                        DebugPrint($"OR: {orHigh:F2} - {orLow:F2} | Range: {orRange:F2} ({orSizeInTicks:F0} ticks)");
                        if (longBucketFound)
                        {
                            double off = orRange * (activeLongBucket.EntryOffsetPercent / 100.0);
                            DebugPrint($"LONG BUCKET L{activeLongBucketIndex}: {activeLongBucket.ORMinTicks}-{activeLongBucket.ORMaxTicks}t | Entry: {(orHigh + off):F2}");
                        }
                        else
                            DebugPrint($"NO LONG BUCKET matched for {orSizeInTicks:F0} ticks");
                        if (shortBucketFound)
                        {
                            double off = orRange * (activeShortBucket.EntryOffsetPercent / 100.0);
                            DebugPrint($"SHORT BUCKET S{activeShortBucketIndex}: {activeShortBucket.ORMinTicks}-{activeShortBucket.ORMaxTicks}t | Entry: {(orLow - off):F2}");
                        }
                        else
                            DebugPrint($"NO SHORT BUCKET matched for {orSizeInTicks:F0} ticks");
                    }
                    
                    DrawORRange();
                }
            }
        }
        
        private void ReconstructORFromHistory()
        {
            if (DebugMode) DebugPrint("Reconstructing OR from history...");
            for (int i = 0; i < CurrentBar && i < 100; i++)
            {
                if (Time[i].Date != Time[0].Date) continue;
                TimeSpan t = Time[i].TimeOfDay;
                if (t >= orStartTime && t < orEndTime)
                {
                    if (orHigh == double.MinValue) orHigh = High[i];
                    if (orLow == double.MaxValue) orLow = Low[i];
                    if (High[i] > orHigh) orHigh = High[i];
                    if (Low[i] < orLow) orLow = Low[i];
                }
            }
        }
        
        private void CapturePreMarketRange(TimeSpan currentTime)
        {
            if (pmrCaptured) return;
            
            if (currentTime >= preMarketStartTime && currentTime < preMarketEndTime)
            {
                if (pmrHigh == double.MinValue) pmrHigh = High[0];
                if (pmrLow == double.MaxValue) pmrLow = Low[0];
                if (High[0] > pmrHigh) pmrHigh = High[0];
                if (Low[0] < pmrLow) pmrLow = Low[0];
                
                if (DebugMode)
                    DebugPrint($"PMR Building: High={pmrHigh:F2}, Low={pmrLow:F2}");
            }
            else if (currentTime >= preMarketEndTime)
            {
                pmrCaptured = true;
                if (pmrHigh != double.MinValue && pmrLow != double.MaxValue && pmrHigh > pmrLow)
                {
                    pmrRange = pmrHigh - pmrLow;
                    pmrSizeInTicks = pmrRange / TickSize;
                }
                else
                {
                    pmrRange = 0;
                    pmrSizeInTicks = 0;
                }
                if (DebugMode)
                    DebugPrint($"=== PMR CAPTURED === High={pmrHigh:F2} Low={pmrLow:F2} Range={pmrRange:F2} ({pmrSizeInTicks:F0} ticks)");
            }
        }
        
        private void ApplyPreMarketRangeFilter()
        {
            if (longBucketFound && activeLongBucket.UsePreMarketFilter
                && activeLongBucket.MaxPreMarketRangeTicks > 0
                && pmrSizeInTicks > activeLongBucket.MaxPreMarketRangeTicks)
            {
                if (DebugMode)
                    DebugPrint($"PMR FILTER: Long bucket L{activeLongBucketIndex} disabled | PMR {pmrSizeInTicks:F0}t > max {activeLongBucket.MaxPreMarketRangeTicks}t");
                longBucketFound = false;
                activeLongBucketIndex = -1;
            }
            
            if (shortBucketFound && activeShortBucket.UsePreMarketFilter
                && activeShortBucket.MaxPreMarketRangeTicks > 0
                && pmrSizeInTicks > activeShortBucket.MaxPreMarketRangeTicks)
            {
                if (DebugMode)
                    DebugPrint($"PMR FILTER: Short bucket S{activeShortBucketIndex} disabled | PMR {pmrSizeInTicks:F0}t > max {activeShortBucket.MaxPreMarketRangeTicks}t");
                shortBucketFound = false;
                activeShortBucketIndex = -1;
            }
        }
        
        private void DrawORRange()
        {
            if (!orCaptured || wickLinesDrawn) return;
            string d = Time[0].Date.ToString("MMdd");
            DateTime t0 = Time[0];
            DateTime t1 = Time[0].Date.Add(sessionEndTime);
            
            Draw.Line(this, "ORHigh_" + d, false, t0, orHigh, t1, orHigh, Brushes.White, DashStyleHelper.Solid, 2);
            Draw.Line(this, "ORLow_" + d, false, t0, orLow, t1, orLow, Brushes.White, DashStyleHelper.Solid, 2);
            
            if (longBucketFound)
            {
                double entryOff = orRange * (activeLongBucket.EntryOffsetPercent / 100.0);
                double entryLvl = orHigh + entryOff;
                if (ShowEntryLines)
                    Draw.Line(this, "LongEntry_" + d, false, t0, entryLvl, t1, entryLvl, Brushes.Orange, DashStyleHelper.Dash, 1);
                if (ShowTargetLines)
                {
                    double tp = activeLongBucket.TPMode == TargetMode.FixedTicks ? activeLongBucket.TakeProfitTicks * TickSize : orRange * (activeLongBucket.TakeProfitPercent / 100.0);
                    Draw.Line(this, "LongTarget_" + d, false, t0, entryLvl + tp, t1, entryLvl + tp, Brushes.DodgerBlue, DashStyleHelper.Dash, 1);
                }
                if (ShowStopLines)
                {
                    double sl = activeLongBucket.SLMode == TargetMode.FixedTicks ? activeLongBucket.StopLossTicks * TickSize : orRange * (activeLongBucket.StopLossPercent / 100.0);
                    Draw.Line(this, "LongStop_" + d, false, t0, entryLvl - sl, t1, entryLvl - sl, Brushes.Red, DashStyleHelper.Dash, 1);
                }
            }
            
            if (shortBucketFound)
            {
                double entryOff = orRange * (activeShortBucket.EntryOffsetPercent / 100.0);
                double entryLvl = orLow - entryOff;
                if (ShowEntryLines)
                    Draw.Line(this, "ShortEntry_" + d, false, t0, entryLvl, t1, entryLvl, Brushes.Orange, DashStyleHelper.Dash, 1);
                if (ShowTargetLines)
                {
                    double tp = activeShortBucket.TPMode == TargetMode.FixedTicks ? activeShortBucket.TakeProfitTicks * TickSize : orRange * (activeShortBucket.TakeProfitPercent / 100.0);
                    Draw.Line(this, "ShortTarget_" + d, false, t0, entryLvl - tp, t1, entryLvl - tp, Brushes.DodgerBlue, DashStyleHelper.Dash, 1);
                }
                if (ShowStopLines)
                {
                    double sl = activeShortBucket.SLMode == TargetMode.FixedTicks ? activeShortBucket.StopLossTicks * TickSize : orRange * (activeShortBucket.StopLossPercent / 100.0);
                    Draw.Line(this, "ShortStop_" + d, false, t0, entryLvl + sl, t1, entryLvl + sl, Brushes.Red, DashStyleHelper.Dash, 1);
                }
            }
            
            wickLinesDrawn = true;
        }
        
        #endregion

        #region Breakout Detection & Confirmation
        
        private void MonitorBreakoutAndConfirmation()
        {
            if (!breakoutActive || Position.MarketPosition != MarketPosition.Flat) return;
            if (entryOrder != null && entryOrder.OrderState == OrderState.Working) return;
            
            // === LONG ===
            if (longBucketFound && (!confirmationComplete || !longBreakoutOccurred))
            {
                double longEntryLevel = orHigh + orRange * (activeLongBucket.EntryOffsetPercent / 100.0);
                if (Close[0] > longEntryLevel)
                {
                    if (!longBreakoutOccurred) { longBreakoutOccurred = true; confirmationBarCount = 1; }
                    else confirmationBarCount++;
                    
                    if (confirmationBarCount >= activeLongBucket.ConfirmationBars)
                        confirmationComplete = true;
                }
                else if (longBreakoutOccurred && !confirmationComplete)
                { confirmationBarCount = 0; longBreakoutOccurred = false; }
            }
            
            // === SHORT ===
            if (shortBucketFound && (!confirmationComplete || !shortBreakoutOccurred))
            {
                double shortEntryLevel = orLow - orRange * (activeShortBucket.EntryOffsetPercent / 100.0);
                if (Close[0] < shortEntryLevel)
                {
                    if (!shortBreakoutOccurred) { shortBreakoutOccurred = true; confirmationBarCount = 1; }
                    else confirmationBarCount++;
                    
                    if (confirmationBarCount >= activeShortBucket.ConfirmationBars)
                        confirmationComplete = true;
                }
                else if (shortBreakoutOccurred && !confirmationComplete)
                { confirmationBarCount = 0; shortBreakoutOccurred = false; }
            }
        }
        
        #endregion

        #region Entry Logic
        
        private void TryEntryWithConfirmation()
        {
            if (!IsReadyForNewOrder()) return;
            if (IsInNoTradesAfterWindow(Time[0])) return;
            if (IsInSkipWindow(Time[0])) return;
            if (IsInNewsSkipWindow(Time[0])) return;
            if (!confirmationComplete) return;
            
            // === LONG ENTRY ===
            if (longBreakoutOccurred && !shortBreakoutOccurred && longBucketFound)
            {
                if (activeLongBucket.MaxTradesPerDay > 0 && longTradeCount >= activeLongBucket.MaxTradesPerDay) return;
                double entryLevel = orHigh + orRange * (activeLongBucket.EntryOffsetPercent / 100.0);
                { PlaceLongLimitEntry(entryLevel); return; }
            }
            
            // === SHORT ENTRY ===
            if (shortBreakoutOccurred && !longBreakoutOccurred && shortBucketFound)
            {
                if (activeShortBucket.MaxTradesPerDay > 0 && shortTradeCount >= activeShortBucket.MaxTradesPerDay) return;
                double entryLevel = orLow - orRange * (activeShortBucket.EntryOffsetPercent / 100.0);
                { PlaceShortLimitEntry(entryLevel); return; }
            }
        }

        private bool IsTradeArmed()
        {
            if (!IsReadyForNewOrder()) return false;
            if (IsInNoTradesAfterWindow(Time[0])) return false;
            if (IsInSkipWindow(Time[0])) return false;
            if (IsInNewsSkipWindow(Time[0])) return false;

            bool longArmed = !confirmationComplete
                && longBreakoutOccurred
                && !shortBreakoutOccurred
                && longBucketFound
                && (activeLongBucket.MaxTradesPerDay <= 0 || longTradeCount < activeLongBucket.MaxTradesPerDay);

            bool shortArmed = !confirmationComplete
                && shortBreakoutOccurred
                && !longBreakoutOccurred
                && shortBucketFound
                && (activeShortBucket.MaxTradesPerDay <= 0 || shortTradeCount < activeShortBucket.MaxTradesPerDay);

            return longArmed || shortArmed;
        }
        
        private bool IsReadyForNewOrder()
        {
            if (Position.MarketPosition != MarketPosition.Flat) return false;
            if (entryOrder != null && entryOrder.OrderState == OrderState.Working) return false;
            if (!orCaptured || orRange <= 0) return false;
            if (IsLastBarOfSession()) return false;
            if (maxAccountLimitHit) return false;
            if (sessionProfitLimitHit || sessionLossLimitHit) return false;
            if (!longBucketFound && !shortBucketFound) return false;
            if (MaxTradesPerDay > 0 && tradeCount >= MaxTradesPerDay) return false;
            return true;
        }
        
        private void PlaceLongLimitEntry(double entryLevel)
        {
            if (activeLongBucket.VarianceTicks > 0)
                entryLevel += random.Next(-activeLongBucket.VarianceTicks, activeLongBucket.VarianceTicks + 1) * TickSize;

            if (RequireEntryConfirmation && !ShowEntryConfirmation("Long", entryLevel, NumberOfContracts))
            {
                if (DebugMode) DebugPrint("Entry confirmation declined | LONG.");
                return;
            }
            
            tradeCount++; longTradeCount++;
            currentSignalName = BuildSignalName(true, tradeCount);
            limitEntryPrice = entryLevel;
            entryOrderBar = CurrentBar;
            lastTradeWasLong = true;
            
            if (DebugMode)
                DebugPrint($">>> LONG LIMIT #{tradeCount} (L:{longTradeCount}) [L{activeLongBucketIndex}] @ {entryLevel:F2}");
            
            entryOrder = EnterLongLimit(0, true, NumberOfContracts, entryLevel, currentSignalName);
            waitingForConfirmation = false;
            confirmationComplete = false;
        }
        
        private void PlaceShortLimitEntry(double entryLevel)
        {
            if (activeShortBucket.VarianceTicks > 0)
                entryLevel += random.Next(-activeShortBucket.VarianceTicks, activeShortBucket.VarianceTicks + 1) * TickSize;

            if (RequireEntryConfirmation && !ShowEntryConfirmation("Short", entryLevel, NumberOfContracts))
            {
                if (DebugMode) DebugPrint("Entry confirmation declined | SHORT.");
                return;
            }
            
            tradeCount++; shortTradeCount++;
            currentSignalName = BuildSignalName(false, tradeCount);
            limitEntryPrice = entryLevel;
            entryOrderBar = CurrentBar;
            lastTradeWasLong = false;
            
            if (DebugMode)
                DebugPrint($">>> SHORT LIMIT #{tradeCount} (S:{shortTradeCount}) [S{activeShortBucketIndex}] @ {entryLevel:F2}");
            
            entryOrder = EnterShortLimit(0, true, NumberOfContracts, entryLevel, currentSignalName);
            waitingForConfirmation = false;
            confirmationComplete = false;
        }
        
        #endregion

        #region Position Management
        
        private void ManagePosition()
        {
            ManagePendingOrders();
            if (Position.MarketPosition == MarketPosition.Flat) return;
            
            int maxBars = lastTradeWasLong ? activeLongBucket.MaxBarsInTrade : activeShortBucket.MaxBarsInTrade;
            if (maxBars > 0 && entryBar > 0 && (CurrentBar - entryBar) >= maxBars)
            { ExitAllPositions("MaxBars"); return; }
            
            ManageTrailingStop();
            CheckBreakevenTrigger();
        }
        
        private void ManagePendingOrders()
        {
            if (limitEntryPrice <= 0 || entryOrderBar <= 0) return;
            if (Position.MarketPosition != MarketPosition.Flat) return;
            if (entryOrder != null && (entryOrder.OrderState == OrderState.Filled || entryOrder.OrderState == OrderState.Cancelled)) return;
            
            if (CancelOrderBars > 0 && (CurrentBar - entryOrderBar) >= CancelOrderBars)
            {
                if (DebugMode) DebugPrint($"TIMEOUT: Cancelling limit after {CurrentBar - entryOrderBar} bars");
                if (entryOrder != null) CancelOrder(entryOrder);
                ResetForNewSetup();
            }
        }
        
        private void ResetForNewSetup()
        {
            entryOrder = null; entryOrderBar = -1; limitEntryPrice = 0;
            currentSignalName = string.Empty;
            confirmationComplete = false; confirmationBarCount = 0;
            longBreakoutOccurred = false; shortBreakoutOccurred = false;
        }

        private double GetReferencePriceForProtectiveStop(MarketPosition positionDirection)
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

        private bool CanAmendProtectiveStopForCurrentMarket(MarketPosition positionDirection, double proposedStopPrice)
        {
            if (double.IsNaN(proposedStopPrice) || double.IsInfinity(proposedStopPrice) || proposedStopPrice <= 0)
                return false;

            double referencePrice = GetReferencePriceForProtectiveStop(positionDirection);
            if (double.IsNaN(referencePrice) || double.IsInfinity(referencePrice) || referencePrice <= 0)
                return true;

            if (positionDirection == MarketPosition.Long)
                return proposedStopPrice < referencePrice;
            if (positionDirection == MarketPosition.Short)
                return proposedStopPrice > referencePrice;

            return true;
        }
        
        private void ManageTrailingStop()
        {
            if (Position.MarketPosition == MarketPosition.Flat) return;
            if (beTriggerActive) return; // BE has fired; stop trailing
            if (orRange <= 0 || entryPrice <= 0) return;
            if (entryBar >= 0 && CurrentBar <= entryBar) return;
            if (IsAfterSessionEnd(Time[0])) return;
            
            BucketParams bp = lastTradeWasLong ? activeLongBucket : activeShortBucket;
            if (!bp.UseTrailingStop || trailStopPrice <= 0) return;
            
            // If trail is locked, no further movement
            if (trailLocked) return;
            
            // Update best price regardless of activation (needed for activation check)
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            if (isLong)
            {
                if (High[0] > bestPriceSinceEntry)
                    bestPriceSinceEntry = High[0];
            }
            else
            {
                if (Low[0] < bestPriceSinceEntry || bestPriceSinceEntry <= 0)
                    bestPriceSinceEntry = Low[0];
            }
            
            double profit = isLong ? bestPriceSinceEntry - entryPrice : entryPrice - bestPriceSinceEntry;
            
            // === Check Activation Threshold ===
            if (!trailActivated)
            {
                if (bp.TrailActivationPercent > 0)
                {
                    double activationDist = orRange * (bp.TrailActivationPercent / 100.0);
                    if (profit >= activationDist)
                    {
                        trailActivated = true;
                        if (DebugMode)
                            DebugPrint($"TRAIL ACTIVATED: Profit={profit:F2} >= {activationDist:F2} ({bp.TrailActivationPercent:F1}% of OR)");
                    }
                }
                else
                {
                    trailActivated = true;
                }
                if (!trailActivated) return;
            }
            
            // === Check Lock Conditions (either can freeze independently) ===
            if (bp.TrailLockOREnabled && bp.TrailLockORPercent > 0)
            {
                double lockDist = orRange * (bp.TrailLockORPercent / 100.0);
                if (profit >= lockDist)
                {
                    trailLocked = true;
                    if (DebugMode)
                        DebugPrint($"TRAIL LOCKED (OR): Profit={profit:F2} >= {lockDist:F2} ({bp.TrailLockORPercent:F1}% of OR) | Stop frozen at {trailStopPrice:F2}");
                    return;
                }
            }
            if (bp.TrailLockTicksEnabled && bp.TrailLockTicks > 0)
            {
                double lockTicksDist = bp.TrailLockTicks * TickSize;
                if (profit >= lockTicksDist)
                {
                    trailLocked = true;
                    if (DebugMode)
                        DebugPrint($"TRAIL LOCKED (Ticks): Profit={profit / TickSize:F0}t >= {bp.TrailLockTicks}t | Stop frozen at {trailStopPrice:F2}");
                    return;
                }
            }
            
            // === Compute trail distance (% of OR, capped by MaxStopLossTicks) ===
            double trailDist = orRange * (bp.TrailStopPercent / 100.0);
            if (bp.MaxStopLossTicks > 0)
            {
                double maxDist = bp.MaxStopLossTicks * TickSize;
                if (trailDist > maxDist) trailDist = maxDist;
            }
            
            // === Calculate candidate stop ===
            double newStop;
            if (isLong)
                newStop = Instrument.MasterInstrument.RoundToTickSize(bestPriceSinceEntry - trailDist);
            else
                newStop = Instrument.MasterInstrument.RoundToTickSize(bestPriceSinceEntry + trailDist);
            
            // === Apply Step Filter ===
            if (bp.TrailStepTicks > 0)
            {
                double stepDist = bp.TrailStepTicks * TickSize;
                double moveDist = isLong ? (newStop - trailStopPrice) : (trailStopPrice - newStop);
                if (moveDist < stepDist) return; // Not enough movement for a step
            }
            
            // === Move stop only in favorable direction ===
            bool shouldMove = isLong ? (newStop > trailStopPrice) : (newStop < trailStopPrice);
            if (shouldMove)
            {
                if (!CanAmendProtectiveStopForCurrentMarket(Position.MarketPosition, newStop))
                {
                    if (DebugMode)
                        DebugPrint($"TRAIL SKIPPED: proposed stop {newStop:F2} is on the wrong side of market for {Position.MarketPosition}.");
                    return;
                }

                trailStopPrice = newStop;
                SetStopLoss(currentSignalName, CalculationMode.Price, trailStopPrice, false);
                if (DebugMode)
                    DebugPrint($"TRAIL {(isLong ? "LONG" : "SHORT")}: Best={bestPriceSinceEntry:F2} Stop={trailStopPrice:F2}");
            }
        }
        
        private void CheckBreakevenTrigger()
        {
            bool useBreakeven = lastTradeWasLong ? activeLongBucket.UseBreakeven : activeShortBucket.UseBreakeven;
            if (!useBreakeven) return;
            
            int bePct = lastTradeWasLong ? activeLongBucket.BreakevenTriggerPercent : activeShortBucket.BreakevenTriggerPercent;
            int beOff = lastTradeWasLong ? activeLongBucket.BreakevenOffsetTicks : activeShortBucket.BreakevenOffsetTicks;
            
            if (bePct <= 0 || beTriggerActive || orRange <= 0) return;
            if (entryBar >= 0 && CurrentBar <= entryBar) return;
            if (IsAfterSessionEnd(Time[0])) return;
            
            double threshold = orRange * (bePct / 100.0);
            double profit = Position.MarketPosition == MarketPosition.Long ? Close[0] - entryPrice :
                            Position.MarketPosition == MarketPosition.Short ? entryPrice - Close[0] : 0;
            
            if (profit >= threshold)
            {
                beTriggerActive = true;
                double beStop = Position.MarketPosition == MarketPosition.Long ? entryPrice + beOff * TickSize : entryPrice - beOff * TickSize;
                
                // If trailing was active, keep the tighter (more favorable) stop
                BucketParams beBp = lastTradeWasLong ? activeLongBucket : activeShortBucket;
                if (beBp.UseTrailingStop && trailStopPrice > 0)
                {
                    if (Position.MarketPosition == MarketPosition.Long && trailStopPrice > beStop)
                        beStop = trailStopPrice;
                    else if (Position.MarketPosition == MarketPosition.Short && trailStopPrice < beStop)
                        beStop = trailStopPrice;
                }

                if (!CanAmendProtectiveStopForCurrentMarket(Position.MarketPosition, beStop))
                {
                    if (DebugMode)
                        DebugPrint($"BREAKEVEN SKIPPED: proposed stop {beStop:F2} is on the wrong side of market for {Position.MarketPosition}.");
                    return;
                }
                
                SetStopLoss(currentSignalName, CalculationMode.Price, beStop, false);
                if (DebugMode) DebugPrint($"BREAKEVEN @ {profit:F2} pts | Stop: {beStop:F2} (trail active={beBp.UseTrailingStop})");
            }
        }
        
        private void ExitAllPositions(string reason)
        {
            if (Position.MarketPosition == MarketPosition.Long) ExitLong(BuildExitSignalName(reason), currentSignalName);
            else if (Position.MarketPosition == MarketPosition.Short) ExitShort(BuildExitSignalName(reason), currentSignalName);
            CancelAllOrders();
        }
        
        private void CancelAllOrders()
        {
            CancelAllPendingOrders();
        }
        
        #endregion

        #region Order Events
        
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null)
                return;

            string orderName = order.Name ?? string.Empty;
            bool isEntryOrder = IsEntryOrderName(orderName);
            if (isEntryOrder)
            {
                entryOrder = order;

                if (orderState == OrderState.Rejected)
                {
                    HandleOrderRejected(order, error, nativeError);
                    return;
                }

                if (orderName == currentSignalName && orderState == OrderState.Filled)
                {
                    entryPrice = averageFillPrice;
                    lastFilledEntryPrice = averageFillPrice;
                    entryBar = CurrentBar;
                    limitEntryPrice = 0; entryOrderBar = -1;
                    SetInitialStopAndTarget(IsLongSignalName(orderName));
                    if (DebugMode) DebugPrint($"FILLED {(lastTradeWasLong ? "LONG" : "SHORT")} @ {averageFillPrice:F2}");
                }
                else if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                { entryOrder = null; limitEntryPrice = 0; entryOrderBar = -1; }
            }
        }

        private bool IsEntryOrderName(string orderName)
        {
            if (string.IsNullOrEmpty(orderName))
                return false;

            return orderName.StartsWith(LongSignalPrefix, StringComparison.OrdinalIgnoreCase)
                || orderName.StartsWith(ShortSignalPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsLongSignalName(string orderName)
        {
            return !string.IsNullOrEmpty(orderName)
                && orderName.StartsWith(LongSignalPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private string BuildSignalName(bool isLong, int ordinal)
        {
            string prefix = isLong ? LongSignalPrefix : ShortSignalPrefix;
            return string.Format("{0}{1}_{2}", prefix, Time[0].ToString("yyyyMMdd"), ordinal);
        }

        private string BuildExitSignalName(string reason)
        {
            return ExitSignalPrefix + reason;
        }

        private void HandleOrderRejected(Order order, ErrorCode error, string nativeError)
        {
            string name = order != null ? (order.Name ?? string.Empty) : string.Empty;
            string state = order != null ? order.OrderState.ToString() : "Unknown";
            Print(string.Format("{0} | ORBOTesting | bar={1} | Order rejected guard | name={2} state={3} error={4} comment={5}",
                Time[0], CurrentBar, name, state, error, nativeError ?? string.Empty));

            if (IsEntryOrderName(name))
            {
                CancelAllPendingOrders();
                ResetForNewSetup();
            }
        }
        
        private void SetInitialStopAndTarget(bool isLong)
        {
            double stopPx, tpPx;
            if (!TryBuildInitialStopAndTargetPrices(isLong, entryPrice, true, out stopPx, out tpPx))
                return;

            if (DebugMode)
            {
                string bl = isLong ? $"L{activeLongBucketIndex}" : $"S{activeShortBucketIndex}";
                DebugPrint($"SL/TP [{bl}]: {(isLong ? "LONG" : "SHORT")} Entry={entryPrice:F2} Stop={stopPx:F2} Target={tpPx:F2}");
            }

            SetStopLoss(currentSignalName, CalculationMode.Price, stopPx, false);
            SetProfitTarget(currentSignalName, CalculationMode.Price, tpPx);
            
            // Initialize trailing stop state
            BucketParams trailBp = isLong ? activeLongBucket : activeShortBucket;
            if (trailBp.UseTrailingStop)
            {
                trailStopPrice = stopPx;
                bestPriceSinceEntry = entryPrice;
                trailActivated = (trailBp.TrailActivationPercent <= 0);
                trailLocked = false;
                if (DebugMode)
                {
                    string tbl = isLong ? $"L{activeLongBucketIndex}" : $"S{activeShortBucketIndex}";
                    DebugPrint($"TRAIL INIT [{tbl}]: Start={trailStopPrice:F2} TrailDist={trailBp.TrailStopPercent:F2}% of OR");
                }
            }
            else
            {
                trailStopPrice = 0;
                bestPriceSinceEntry = 0;
                trailActivated = false;
                trailLocked = false;
            }
        }

        private bool TryBuildInitialStopAndTargetPrices(bool isLong, double baseEntryPrice, bool clampToMarket, out double stopPx, out double tpPx)
        {
            stopPx = 0;
            tpPx = 0;

            if (orRange <= 0 || baseEntryPrice <= 0)
                return false;

            if (Position.MarketPosition == MarketPosition.Long && !isLong) isLong = true;
            else if (Position.MarketPosition == MarketPosition.Short && isLong) isLong = false;

            BucketParams bp = isLong ? activeLongBucket : activeShortBucket;
            double profitDist = bp.TPMode == TargetMode.FixedTicks ? bp.TakeProfitTicks * TickSize : orRange * (bp.TakeProfitPercent / 100.0);
            double stopDist = bp.SLMode == TargetMode.FixedTicks ? bp.StopLossTicks * TickSize : orRange * (bp.StopLossPercent / 100.0);

            if (bp.MaxStopLossTicks > 0)
            {
                double maxStop = bp.MaxStopLossTicks * TickSize;
                if (stopDist > maxStop) stopDist = maxStop;
            }

            if (isLong)
            {
                stopPx = baseEntryPrice - stopDist;
                tpPx = baseEntryPrice + profitDist;
                stopPx = Instrument.MasterInstrument.RoundToTickSize(stopPx);
                tpPx = Instrument.MasterInstrument.RoundToTickSize(tpPx);
                if (stopPx >= baseEntryPrice)
                    stopPx = Instrument.MasterInstrument.RoundToTickSize(baseEntryPrice - TickSize);
                if (tpPx <= baseEntryPrice)
                    tpPx = Instrument.MasterInstrument.RoundToTickSize(baseEntryPrice + TickSize);
            }
            else
            {
                stopPx = baseEntryPrice + stopDist;
                tpPx = baseEntryPrice - profitDist;
                stopPx = Instrument.MasterInstrument.RoundToTickSize(stopPx);
                tpPx = Instrument.MasterInstrument.RoundToTickSize(tpPx);
                if (stopPx <= baseEntryPrice)
                    stopPx = Instrument.MasterInstrument.RoundToTickSize(baseEntryPrice + TickSize);
                if (tpPx >= baseEntryPrice)
                    tpPx = Instrument.MasterInstrument.RoundToTickSize(baseEntryPrice - TickSize);
            }

            if (clampToMarket)
            {
                double marketPx = Close[0];
                if (isLong)
                {
                    if (stopPx >= marketPx)
                        stopPx = Instrument.MasterInstrument.RoundToTickSize(Math.Min(baseEntryPrice - TickSize, marketPx - TickSize));
                    if (tpPx <= marketPx)
                        tpPx = Instrument.MasterInstrument.RoundToTickSize(Math.Max(baseEntryPrice + TickSize, marketPx + TickSize));
                }
                else
                {
                    if (stopPx <= marketPx)
                        stopPx = Instrument.MasterInstrument.RoundToTickSize(Math.Max(baseEntryPrice + TickSize, marketPx + TickSize));
                    if (tpPx >= marketPx)
                        tpPx = Instrument.MasterInstrument.RoundToTickSize(Math.Min(baseEntryPrice - TickSize, marketPx - TickSize));
                }
            }

            return true;
        }
        
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null || execution.Order.OrderState != OrderState.Filled)
                return;

            string orderName = execution.Order.Name ?? string.Empty;
            bool isEntry = IsEntryOrderName(orderName);
            int executionQty = Math.Abs(quantity);

            if (isEntry)
            {
                string entryAction;
                bool isMarketEntry;
                if (TryGetEntryWebhookAction(execution, out entryAction, out isMarketEntry))
                {
                    bool isLongEntry = string.Equals(entryAction, "buy", StringComparison.OrdinalIgnoreCase);
                    double fillPrice = execution.Price;
                    double stopPx, tpPx;
                    if (TryBuildInitialStopAndTargetPrices(isLongEntry, fillPrice, false, out stopPx, out tpPx))
                        SendWebhook(entryAction, fillPrice, tpPx, stopPx, isMarketEntry, executionQty);
                }
            }

            if (ShouldSendExitWebhook(execution, orderName, marketPosition))
                SendWebhook("exit", 0, 0, 0, true, executionQty);

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                bool isExit = !isEntry;
                if (isExit && lastFilledEntryPrice > 0)
                {
                    double pnl = lastTradeWasLong ? (price - lastFilledEntryPrice) / TickSize : (lastFilledEntryPrice - price) / TickSize;
                    sessionRealizedPnL += pnl;
                    if (DebugMode) DebugPrint($"CLOSED: {pnl:F1}t | Session: {sessionRealizedPnL:F1}t");
                }
                
                beTriggerActive = false;
                trailStopPrice = 0; bestPriceSinceEntry = 0;
                trailActivated = false; trailLocked = false;
                bool useRearm = lastTradeWasLong ? activeLongBucket.UseBreakoutRearm : activeShortBucket.UseBreakoutRearm;
                if (useRearm)
                {
                    hasReturnedOnce = false; waitingForConfirmation = true;
                    confirmationComplete = false; returnBar = -1;
                }
            }
        }

        private bool ShouldSendExitWebhook(Execution execution, string orderName, MarketPosition marketPosition)
        {
            if (execution == null || execution.Order == null)
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
                || normalized.StartsWith(ExitSignalPrefix, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("Exit_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return marketPosition == MarketPosition.Flat;
        }

        private bool TryGetEntryWebhookAction(Execution execution, out string action, out bool isMarketEntry)
        {
            action = null;
            isMarketEntry = false;

            if (execution == null || execution.Order == null)
                return false;

            string orderName = execution.Order.Name ?? string.Empty;
            if (!IsEntryOrderName(orderName))
                return false;

            switch (execution.Order.OrderAction)
            {
                case OrderAction.Buy:
                case OrderAction.BuyToCover:
                    action = "buy";
                    break;
                case OrderAction.Sell:
                case OrderAction.SellShort:
                    action = "sell";
                    break;
                default:
                    return false;
            }

            isMarketEntry = execution.Order.OrderType == OrderType.Market;
            return true;
        }
        
        #endregion

        #region Session Management
        
        private void ResetDailyStateIfNeeded()
        {
            if (Time[0].Date != lastDate.Date)
            {
                lastDate = Time[0];
                orHigh = double.MinValue; orLow = double.MaxValue; orRange = 0;
                orCaptured = false; wickLinesDrawn = false;
                pmrHigh = double.MinValue; pmrLow = double.MaxValue; pmrRange = 0;
                pmrSizeInTicks = 0; pmrCaptured = false;
                longBucketFound = false; shortBucketFound = false;
                activeLongBucketIndex = -1; activeShortBucketIndex = -1;
                breakoutActive = false; longBreakoutOccurred = false; shortBreakoutOccurred = false; breakoutBar = -1;
                hasReturnedOnce = false; waitingForConfirmation = false; confirmationComplete = false;
                confirmationBarCount = 0; returnBar = -1;
                orderPlaced = false; entryBar = -1; entryOrderBar = -1; entryOrder = null;
                currentSignalName = string.Empty;
                entryPrice = 0; limitEntryPrice = 0; lastFilledEntryPrice = 0;
                beTriggerActive = false; trailStopPrice = 0; bestPriceSinceEntry = 0;
                trailActivated = false; trailLocked = false;
                maxAccountLimitHit = false;
                wasInNoTradesAfterWindow = false; wasInSkipWindow = false; wasInNewsSkipWindow = false;
                tradeCount = 0; longTradeCount = 0; shortTradeCount = 0;
                sessionRealizedPnL = 0; sessionProfitLimitHit = false; sessionLossLimitHit = false;
                if (DebugMode) DebugPrint($"========== NEW DAY: {Time[0].Date:yyyy-MM-dd} ==========");
            }
        }
        
        private bool IsInNoTradesAfterWindow(DateTime time)
        {
            if (!IsNoTradesAfterConfigured())
                return false;
            return time.TimeOfDay >= noTradesAfterTime;
        }

        private bool IsInSkipWindow(DateTime time)
        {
            if (!IsSkipWindowConfigured())
                return false;

            TimeSpan now = time.TimeOfDay;
            if (skipStartTime < skipEndTime)
                return now >= skipStartTime && now < skipEndTime;

            return now >= skipStartTime || now < skipEndTime;
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
                if (time >= windowStart && time <= windowEnd)
                    return true;
            }

            return false;
        }

        private bool IsNoTradesAfterConfigured()
        {
            return noTradesAfterTime != TimeSpan.Zero;
        }

        private bool IsSkipWindowConfigured()
        {
            return skipStartTime != TimeSpan.Zero
                && skipEndTime != TimeSpan.Zero
                && skipStartTime != skipEndTime;
        }

        private void EnsureNewsDatesInitialized()
        {
            if (newsDatesInitialized)
                return;

            NewsDates.Clear();
            LoadHardcodedNewsDates();
            NewsDates.Sort();
            newsDatesInitialized = true;

            if (DebugMode)
                DebugPrint($"News dates loaded: {NewsDates.Count}");
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
                    if (DebugMode)
                        DebugPrint("Invalid news date entry: " + trimmed);
                    continue;
                }

                if (parsed.TimeOfDay == new TimeSpan(14, 0, 0) && !NewsDates.Contains(parsed))
                    NewsDates.Add(parsed);
            }
        }
        
        private void CheckSessionPnLLimits()
        {
            double unrealized = 0;
            if (Position.MarketPosition == MarketPosition.Long) unrealized = (Close[0] - Position.AveragePrice) / TickSize;
            else if (Position.MarketPosition == MarketPosition.Short) unrealized = (Position.AveragePrice - Close[0]) / TickSize;
            double total = sessionRealizedPnL + unrealized;
            
            if (MaxSessionProfitTicks > 0 && total >= MaxSessionProfitTicks && !sessionProfitLimitHit)
            {
                sessionProfitLimitHit = true;
                if (Position.MarketPosition != MarketPosition.Flat) ExitAllPositions("MaxProfit");
                CancelAllPendingOrders();
            }
            if (MaxSessionLossTicks > 0 && total <= -MaxSessionLossTicks && !sessionLossLimitHit)
            {
                sessionLossLimitHit = true;
                if (Position.MarketPosition != MarketPosition.Flat) ExitAllPositions("MaxLoss");
                CancelAllPendingOrders();
            }
        }
        
        private void CancelAllPendingOrders()
        {
            if (entryOrder != null && (entryOrder.OrderState == OrderState.Working || entryOrder.OrderState == OrderState.Accepted || entryOrder.OrderState == OrderState.Submitted))
            {
                CancelOrder(entryOrder);
                SendWebhook("cancel");
            }
            entryOrder = null; limitEntryPrice = 0; entryOrderBar = -1;
        }
        
        private bool ShouldAccountBalanceExit()
        {
            if (MaxAccountBalance <= 0)
                return false;

            double bal = 0.0;
            try
            {
                if (Account != null)
                {
                    bal = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                    if (bal <= 0.0)
                    {
                        double realizedCash = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                        double unrealized = Position.MarketPosition != MarketPosition.Flat
                            ? Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0])
                            : 0.0;
                        bal = realizedCash + unrealized;
                    }
                }
            }
            catch
            {
                return false;
            }

            if (bal >= MaxAccountBalance && !maxAccountLimitHit)
            {
                maxAccountLimitHit = true;
                CancelAllOrders();
                if (DebugMode)
                    DebugPrint(string.Format(CultureInfo.InvariantCulture, "Max account balance reached | netLiq={0:0.00} target={1:0.00}", bal, MaxAccountBalance));
            }

            return maxAccountLimitHit;
        }
        
        private void ExitIfSessionEnded()
        { if (Time[0].TimeOfDay >= sessionEndTime && Position.MarketPosition != MarketPosition.Flat) ExitAllPositions("SessionEnd"); }

        private bool IsAfterSessionEnd(DateTime time)
        {
            return time.TimeOfDay >= sessionEndTime;
        }

        private bool IsLastBarOfSession()
        {
            return Bars != null && Bars.IsLastBarOfSession;
        }
        
        private void HandleNoTradeAndSkipTransitions()
        {
            bool inNoTradesAfterNow = IsInNoTradesAfterWindow(Time[0]);
            bool inSkipNow = IsInSkipWindow(Time[0]);
            bool inNewsSkipNow = IsInNewsSkipWindow(Time[0]);

            if (!wasInNoTradesAfterWindow && inNoTradesAfterNow)
            {
                CancelAllOrders();
                if (DebugMode) DebugPrint("Entered NoTradesAfter window: canceling working entries. Open positions will be closed at Session End.");
            }

            if (!wasInSkipWindow && inSkipNow)
            {
                CancelAllOrders();
                if (Position.MarketPosition != MarketPosition.Flat)
                    ExitAllPositions("SkipWindow");
                if (DebugMode) DebugPrint("Entered Skip window: canceling working entries and flattening open position.");
            }

            if (!wasInNewsSkipWindow && inNewsSkipNow)
            {
                CancelAllOrders();
                if (Position.MarketPosition != MarketPosition.Flat)
                    ExitAllPositions("NewsSkip");
                if (DebugMode) DebugPrint("Entered News skip window: canceling working entries and flattening open position.");
            }

            wasInNoTradesAfterWindow = inNoTradesAfterNow;
            wasInSkipWindow = inSkipNow;
            wasInNewsSkipWindow = inNewsSkipNow;
        }

        private void DrawSessionTimeWindows()
        {
            if (CurrentBar < 1)
                return;

            DrawSessionBackground();
            DrawNoTradesAfterLine(Time[0]);
            DrawSkipWindow(Time[0]);
            DrawNewsWindows(Time[0]);
        }

        private DateTime GetSessionStartTime(DateTime barTime)
        {
            if (orStartTime <= sessionEndTime)
                return barTime.Date + orStartTime;

            if (barTime.TimeOfDay < sessionEndTime)
                return barTime.Date.AddDays(-1) + orStartTime;

            return barTime.Date + orStartTime;
        }

        private void DrawSessionBackground()
        {
            DateTime sessionStart = GetSessionStartTime(Time[0]);
            DateTime sessionEnd = orStartTime > sessionEndTime
                ? sessionStart.AddDays(1).Date + sessionEndTime
                : sessionStart.Date + sessionEndTime;

            string rectTag = string.Format("ORBO_SessionFill_{0:yyyyMMdd_HHmm}", sessionStart);
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

        private void DrawNoTradesAfterLine(DateTime barTime)
        {
            if (!IsNoTradesAfterConfigured())
                return;

            DateTime lineTime = barTime.Date + noTradesAfterTime;
            string tag = string.Format("ORBO_NoTradesAfter_{0:yyyyMMdd_HHmm}", lineTime);
            Draw.VerticalLine(this, tag, lineTime, Brushes.Red, DashStyleHelper.DashDot, 2);
        }

        private void DrawSkipWindow(DateTime barTime)
        {
            if (!IsSkipWindowConfigured())
                return;

            DateTime windowStart = barTime.Date + skipStartTime;
            DateTime windowEnd = barTime.Date + skipEndTime;
            if (skipStartTime > skipEndTime)
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

            string tagBase = string.Format("ORBO_Skip_{0:yyyyMMdd_HHmm}", windowStart);
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

                string tagBase = string.Format("ORBO_News_{0:yyyyMMdd_HHmm}", newsTime);
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
        
        #endregion

        #region Display
        
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

            lines.Add((string.Format("ORBOibTesting v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));
            lines.Add(("Contracts:", NumberOfContracts.ToString(CultureInfo.InvariantCulture), Brushes.LightGray, Brushes.LightGray));
            string orSizeText = orCaptured && orRange > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:F2} pts", orRange)
                : "0 pts";
            lines.Add(("OR Size:", orSizeText, Brushes.LightGray, Brushes.LightGray));
            string pmrSizeText = pmrCaptured && pmrRange > 0
                ? string.Format(CultureInfo.InvariantCulture, "{0:F0} ticks", pmrSizeInTicks)
                : "Pending";
            lines.Add(("PMR Size:", pmrSizeText, Brushes.LightGray, Brushes.LightGray));
            bool isArmed = IsTradeArmed();
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

        private void SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0, bool isMarketEntry = false, int quantityOverride = 0)
        {
            if (State != State.Realtime)
                return;

            if (WebhookProviderType == WebhookProvider.ProjectX)
            {
                int orderQtyForProvider = quantityOverride > 0 ? quantityOverride : Math.Max(1, NumberOfContracts);
                WebhookLog(string.Format(CultureInfo.InvariantCulture,
                    "Signal | provider=ProjectX event={0} qty={1} market={2} entry={3:0.00} tp={4:0.00} sl={5:0.00}",
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
                WebhookLog(string.Format("Signal skipped | provider=TradersPost event={0} reason=empty-url", eventType));
                return;
            }

            try
            {
                int orderQty = quantityOverride > 0 ? quantityOverride : Math.Max(1, NumberOfContracts);
                string ticker = Instrument != null ? Instrument.MasterInstrument.Name : "UNKNOWN";
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
                {
                    WebhookLog(string.Format("Signal skipped | provider=TradersPost event={0} reason=empty-payload", eventType));
                    return;
                }

                WebhookLog(string.Format(CultureInfo.InvariantCulture,
                    "Signal | provider=TradersPost event={0} action={1} qty={2} market={3} entry={4:0.00} tp={5:0.00} sl={6:0.00}",
                    eventType,
                    action,
                    orderQty,
                    isMarketEntry,
                    entryPrice,
                    takeProfit,
                    stopLoss));

                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.UploadString(WebhookUrl, "POST", json);
                }

                WebhookLog(string.Format("Signal sent | provider=TradersPost event={0} action={1} qty={2}", eventType, action, orderQty));
            }
            catch (Exception ex)
            {
                WebhookLog(string.Format("Webhook error: {0}", ex.Message));
            }
        }

        private static readonly FontFamily InfoEmojiFontFamily = new FontFamily("Segoe UI Emoji");
        private static readonly FontFamily InfoSymbolFontFamily = new FontFamily("Segoe UI Symbol");

        private static readonly HashSet<string> InfoEmojiTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "✔", "✔️", "✅", "❌", "✖", "⛔", "⛔️", "🚫", "⬜", "🕒"
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
                    var emojiRun = new Run(normalizedValue) { FontFamily = InfoEmojiFontFamily, Foreground = stateValueBrush };
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

        private void SendProjectX(string eventType, double entryPrice, double takeProfit, double stopLoss, bool isMarketEntry, int quantity)
        {
            if (!EnsureProjectXSession())
            {
                WebhookLog("Signal skipped | provider=ProjectX reason=auth-unavailable");
                return;
            }

            int accountId;
            string contractId;
            if (!TryGetProjectXIds(out accountId, out contractId))
            {
                WebhookLog("Signal skipped | provider=ProjectX reason=missing-account-or-contract");
                return;
            }

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
                WebhookLog(string.Format("ProjectX error: {0}", ex.Message));
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

        private void WebhookLog(string message)
        {
            if (DebugMode)
                DebugPrint("[Webhook] " + message);
        }

        private void ValidateRequiredPrimaryTimeframe(int requiredMinutes)
        {
            bool isMinuteSeries = BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute;
            bool timeframeMatches = isMinuteSeries && BarsPeriod.Value == requiredMinutes;
            isConfiguredTimeframeValid = timeframeMatches;
            if (timeframeMatches)
                return;

            string actualTimeframe = BarsPeriod == null
                ? "Unknown"
                : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", BarsPeriod.Value, BarsPeriod.BarsPeriodType);
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "{0} must run on a {1}-minute chart. Current chart is {2}. Trading is disabled until timeframe is corrected.",
                Name,
                requiredMinutes,
                actualTimeframe);
            Print(string.Format(CultureInfo.InvariantCulture, "{0} | {1}", Name, message));
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
                Print(string.Format(CultureInfo.InvariantCulture, "{0} | Failed to show timeframe popup: {1}", Name, ex.Message));
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
            Print(string.Format(CultureInfo.InvariantCulture, "{0} | {1}", Name, message));
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
                Print(string.Format(CultureInfo.InvariantCulture, "{0} | Failed to show instrument popup: {1}", Name, ex.Message));
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
                    MessageBoxResult res =
                        System.Windows.MessageBox.Show(message, "Entry Confirmation", System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Question);

                    result = res == System.Windows.MessageBoxResult.Yes;
                });

            return result;
        }
        
        private void DebugPrint(string msg)
        { Print($"[ORBO {Time[0]:HH:mm:ss}] {msg}"); }
        
        #endregion

        #region Properties
        
        // ==========================================
        // ===== A. General Settings =====
        // ==========================================
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts", Order = 1, GroupName = "A. General")]
        public int NumberOfContracts { get; set; }
        
        
        
        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "Max Account Balance", Order = 4, GroupName = "A. General")]
        public double MaxAccountBalance { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Entry Confirmation", Description = "Show a Yes/No confirmation popup before each new long/short entry.", Order = 5, GroupName = "A. General")]
        public bool RequireEntryConfirmation { get; set; }
        
        
        [NinjaScriptProperty]
        [Display(Name = "Debug Mode", Order = 6, GroupName = "A. General")]
        public bool DebugMode { get; set; }
        
        // ==========================================
        // ===== B. Long Bucket 1 =====
        // ==========================================
        [NinjaScriptProperty]
        [Display(Name = "Enable L1", Order = 1, GroupName = "B. Long Bucket 1")]
        public bool L1_Enabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Min (Ticks)", Order = 2, GroupName = "B. Long Bucket 1")]
        public int L1_ORMinTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Max (Ticks)", Order = 3, GroupName = "B. Long Bucket 1")]
        public int L1_ORMaxTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use PMR Filter", Description = "When enabled, skip this bucket if pre-market range exceeds Max PMR Ticks.", Order = 4, GroupName = "B. Long Bucket 1")]
        public bool L1_UsePreMarketFilter { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "Max PMR (Ticks)", Description = "Maximum pre-market range in ticks. 0 = no limit when filter is on.", Order = 5, GroupName = "B. Long Bucket 1")]
        public int L1_MaxPreMarketRangeTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakout Rearm", Order = 6, GroupName = "B. Long Bucket 1")]
        public bool L1_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 7, GroupName = "B. Long Bucket 1")]
        public bool L1_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 8, GroupName = "B. Long Bucket 1")]
        public int L1_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 9, GroupName = "B. Long Bucket 1")]
        public double L1_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 10, GroupName = "B. Long Bucket 1")]
        public int L1_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 11, GroupName = "B. Long Bucket 1")]
        public TargetMode L1_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 12, GroupName = "B. Long Bucket 1")]
        public double L1_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 13, GroupName = "B. Long Bucket 1")]
        public int L1_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 14, GroupName = "B. Long Bucket 1")]
        public TargetMode L1_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 15, GroupName = "B. Long Bucket 1")]
        public double L1_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 16, GroupName = "B. Long Bucket 1")]
        public int L1_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 17, GroupName = "B. Long Bucket 1")]
        public int L1_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Trailing Stop", Description = "Trail the stop loss behind price. Trailing stops when BE trigger fires.", Order = 18, GroupName = "B. Long Bucket 1")]
        public bool L1_UseTrailingStop { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Stop % of OR", Description = "Trailing distance as percentage of Opening Range.", Order = 19, GroupName = "B. Long Bucket 1")]
        public double L1_TrailStopPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Activation (% of OR)", Description = "Trail starts moving only after profit reaches this % of OR. 0 = immediate.", Order = 20, GroupName = "B. Long Bucket 1")]
        public double L1_TrailActivationPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Step (Ticks)", Description = "Trail moves in discrete increments of this size. 0 = continuous.", Order = 21, GroupName = "B. Long Bucket 1")]
        public int L1_TrailStepTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at % of OR", Description = "Freeze the trailing stop when profit reaches the specified % of OR.", Order = 22, GroupName = "B. Long Bucket 1")]
        public bool L1_TrailLockOREnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Lock (% of OR)", Description = "Profit threshold in % of OR at which the trail freezes.", Order = 23, GroupName = "B. Long Bucket 1")]
        public double L1_TrailLockORPercent { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at Ticks", Description = "Freeze the trailing stop when profit reaches Entry + specified ticks.", Order = 24, GroupName = "B. Long Bucket 1")]
        public bool L1_TrailLockTicksEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Trail Lock (Ticks)", Description = "Profit threshold in ticks from entry at which the trail freezes.", Order = 25, GroupName = "B. Long Bucket 1")]
        public int L1_TrailLockTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakeven", Description = "Enable breakeven stop adjustment when profit threshold is reached.", Order = 26, GroupName = "B. Long Bucket 1")]
        public bool L1_UseBreakeven { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 27, GroupName = "B. Long Bucket 1")]
        public int L1_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 28, GroupName = "B. Long Bucket 1")]
        public int L1_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 29, GroupName = "B. Long Bucket 1")]
        public int L1_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 30, GroupName = "B. Long Bucket 1")]
        public int L1_MaxTradesPerDay { get; set; }
        
        // ==========================================
        // ===== C. Long Bucket 2 =====
        // ==========================================
        [NinjaScriptProperty]
        [Display(Name = "Enable L2", Order = 1, GroupName = "C. Long Bucket 2")]
        public bool L2_Enabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Min (Ticks)", Order = 2, GroupName = "C. Long Bucket 2")]
        public int L2_ORMinTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Max (Ticks)", Order = 3, GroupName = "C. Long Bucket 2")]
        public int L2_ORMaxTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use PMR Filter", Description = "When enabled, skip this bucket if pre-market range exceeds Max PMR Ticks.", Order = 4, GroupName = "C. Long Bucket 2")]
        public bool L2_UsePreMarketFilter { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "Max PMR (Ticks)", Description = "Maximum pre-market range in ticks. 0 = no limit when filter is on.", Order = 5, GroupName = "C. Long Bucket 2")]
        public int L2_MaxPreMarketRangeTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakout Rearm", Order = 6, GroupName = "C. Long Bucket 2")]
        public bool L2_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 7, GroupName = "C. Long Bucket 2")]
        public bool L2_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 8, GroupName = "C. Long Bucket 2")]
        public int L2_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 9, GroupName = "C. Long Bucket 2")]
        public double L2_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 10, GroupName = "C. Long Bucket 2")]
        public int L2_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 11, GroupName = "C. Long Bucket 2")]
        public TargetMode L2_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 12, GroupName = "C. Long Bucket 2")]
        public double L2_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 13, GroupName = "C. Long Bucket 2")]
        public int L2_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 14, GroupName = "C. Long Bucket 2")]
        public TargetMode L2_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 15, GroupName = "C. Long Bucket 2")]
        public double L2_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 16, GroupName = "C. Long Bucket 2")]
        public int L2_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 17, GroupName = "C. Long Bucket 2")]
        public int L2_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Trailing Stop", Description = "Trail the stop loss behind price. Trailing stops when BE trigger fires.", Order = 18, GroupName = "C. Long Bucket 2")]
        public bool L2_UseTrailingStop { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Stop % of OR", Description = "Trailing distance as percentage of Opening Range.", Order = 19, GroupName = "C. Long Bucket 2")]
        public double L2_TrailStopPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Activation (% of OR)", Description = "Trail starts moving only after profit reaches this % of OR. 0 = immediate.", Order = 20, GroupName = "C. Long Bucket 2")]
        public double L2_TrailActivationPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Step (Ticks)", Description = "Trail moves in discrete increments of this size. 0 = continuous.", Order = 21, GroupName = "C. Long Bucket 2")]
        public int L2_TrailStepTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at % of OR", Description = "Freeze the trailing stop when profit reaches the specified % of OR.", Order = 22, GroupName = "C. Long Bucket 2")]
        public bool L2_TrailLockOREnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Lock (% of OR)", Description = "Profit threshold in % of OR at which the trail freezes.", Order = 23, GroupName = "C. Long Bucket 2")]
        public double L2_TrailLockORPercent { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at Ticks", Description = "Freeze the trailing stop when profit reaches Entry + specified ticks.", Order = 24, GroupName = "C. Long Bucket 2")]
        public bool L2_TrailLockTicksEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Trail Lock (Ticks)", Description = "Profit threshold in ticks from entry at which the trail freezes.", Order = 25, GroupName = "C. Long Bucket 2")]
        public int L2_TrailLockTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakeven", Description = "Enable breakeven stop adjustment when profit threshold is reached.", Order = 26, GroupName = "C. Long Bucket 2")]
        public bool L2_UseBreakeven { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 27, GroupName = "C. Long Bucket 2")]
        public int L2_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 28, GroupName = "C. Long Bucket 2")]
        public int L2_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 29, GroupName = "C. Long Bucket 2")]
        public int L2_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 30, GroupName = "C. Long Bucket 2")]
        public int L2_MaxTradesPerDay { get; set; }
        
        // ==========================================
        // ===== D. Long Bucket 3 =====
        // ==========================================
        [NinjaScriptProperty]
        [Display(Name = "Enable L3", Order = 1, GroupName = "D. Long Bucket 3")]
        public bool L3_Enabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Min (Ticks)", Order = 2, GroupName = "D. Long Bucket 3")]
        public int L3_ORMinTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Max (Ticks)", Order = 3, GroupName = "D. Long Bucket 3")]
        public int L3_ORMaxTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use PMR Filter", Description = "When enabled, skip this bucket if pre-market range exceeds Max PMR Ticks.", Order = 4, GroupName = "D. Long Bucket 3")]
        public bool L3_UsePreMarketFilter { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "Max PMR (Ticks)", Description = "Maximum pre-market range in ticks. 0 = no limit when filter is on.", Order = 5, GroupName = "D. Long Bucket 3")]
        public int L3_MaxPreMarketRangeTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakout Rearm", Order = 6, GroupName = "D. Long Bucket 3")]
        public bool L3_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 7, GroupName = "D. Long Bucket 3")]
        public bool L3_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 8, GroupName = "D. Long Bucket 3")]
        public int L3_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 9, GroupName = "D. Long Bucket 3")]
        public double L3_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 10, GroupName = "D. Long Bucket 3")]
        public int L3_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 11, GroupName = "D. Long Bucket 3")]
        public TargetMode L3_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 12, GroupName = "D. Long Bucket 3")]
        public double L3_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 13, GroupName = "D. Long Bucket 3")]
        public int L3_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 14, GroupName = "D. Long Bucket 3")]
        public TargetMode L3_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 15, GroupName = "D. Long Bucket 3")]
        public double L3_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 16, GroupName = "D. Long Bucket 3")]
        public int L3_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 17, GroupName = "D. Long Bucket 3")]
        public int L3_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Trailing Stop", Description = "Trail the stop loss behind price. Trailing stops when BE trigger fires.", Order = 18, GroupName = "D. Long Bucket 3")]
        public bool L3_UseTrailingStop { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Stop % of OR", Description = "Trailing distance as percentage of Opening Range.", Order = 19, GroupName = "D. Long Bucket 3")]
        public double L3_TrailStopPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Activation (% of OR)", Description = "Trail starts moving only after profit reaches this % of OR. 0 = immediate.", Order = 20, GroupName = "D. Long Bucket 3")]
        public double L3_TrailActivationPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Step (Ticks)", Description = "Trail moves in discrete increments of this size. 0 = continuous.", Order = 21, GroupName = "D. Long Bucket 3")]
        public int L3_TrailStepTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at % of OR", Description = "Freeze the trailing stop when profit reaches the specified % of OR.", Order = 22, GroupName = "D. Long Bucket 3")]
        public bool L3_TrailLockOREnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Lock (% of OR)", Description = "Profit threshold in % of OR at which the trail freezes.", Order = 23, GroupName = "D. Long Bucket 3")]
        public double L3_TrailLockORPercent { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at Ticks", Description = "Freeze the trailing stop when profit reaches Entry + specified ticks.", Order = 24, GroupName = "D. Long Bucket 3")]
        public bool L3_TrailLockTicksEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Trail Lock (Ticks)", Description = "Profit threshold in ticks from entry at which the trail freezes.", Order = 25, GroupName = "D. Long Bucket 3")]
        public int L3_TrailLockTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakeven", Description = "Enable breakeven stop adjustment when profit threshold is reached.", Order = 26, GroupName = "D. Long Bucket 3")]
        public bool L3_UseBreakeven { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 27, GroupName = "D. Long Bucket 3")]
        public int L3_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 28, GroupName = "D. Long Bucket 3")]
        public int L3_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 29, GroupName = "D. Long Bucket 3")]
        public int L3_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 30, GroupName = "D. Long Bucket 3")]
        public int L3_MaxTradesPerDay { get; set; }
        
        // ==========================================
        // ===== E. Long Bucket 4 =====
        // ==========================================
        [NinjaScriptProperty]
        [Display(Name = "Enable L4", Order = 1, GroupName = "E. Long Bucket 4")]
        public bool L4_Enabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Min (Ticks)", Order = 2, GroupName = "E. Long Bucket 4")]
        public int L4_ORMinTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Max (Ticks)", Order = 3, GroupName = "E. Long Bucket 4")]
        public int L4_ORMaxTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use PMR Filter", Description = "When enabled, skip this bucket if pre-market range exceeds Max PMR Ticks.", Order = 4, GroupName = "E. Long Bucket 4")]
        public bool L4_UsePreMarketFilter { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "Max PMR (Ticks)", Description = "Maximum pre-market range in ticks. 0 = no limit when filter is on.", Order = 5, GroupName = "E. Long Bucket 4")]
        public int L4_MaxPreMarketRangeTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakout Rearm", Order = 6, GroupName = "E. Long Bucket 4")]
        public bool L4_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 7, GroupName = "E. Long Bucket 4")]
        public bool L4_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 8, GroupName = "E. Long Bucket 4")]
        public int L4_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 9, GroupName = "E. Long Bucket 4")]
        public double L4_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 10, GroupName = "E. Long Bucket 4")]
        public int L4_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 11, GroupName = "E. Long Bucket 4")]
        public TargetMode L4_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 12, GroupName = "E. Long Bucket 4")]
        public double L4_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 13, GroupName = "E. Long Bucket 4")]
        public int L4_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 14, GroupName = "E. Long Bucket 4")]
        public TargetMode L4_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 15, GroupName = "E. Long Bucket 4")]
        public double L4_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 16, GroupName = "E. Long Bucket 4")]
        public int L4_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 17, GroupName = "E. Long Bucket 4")]
        public int L4_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Trailing Stop", Description = "Trail the stop loss behind price. Trailing stops when BE trigger fires.", Order = 18, GroupName = "E. Long Bucket 4")]
        public bool L4_UseTrailingStop { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Stop % of OR", Description = "Trailing distance as percentage of Opening Range.", Order = 19, GroupName = "E. Long Bucket 4")]
        public double L4_TrailStopPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Activation (% of OR)", Description = "Trail starts moving only after profit reaches this % of OR. 0 = immediate.", Order = 20, GroupName = "E. Long Bucket 4")]
        public double L4_TrailActivationPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Step (Ticks)", Description = "Trail moves in discrete increments of this size. 0 = continuous.", Order = 21, GroupName = "E. Long Bucket 4")]
        public int L4_TrailStepTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at % of OR", Description = "Freeze the trailing stop when profit reaches the specified % of OR.", Order = 22, GroupName = "E. Long Bucket 4")]
        public bool L4_TrailLockOREnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Lock (% of OR)", Description = "Profit threshold in % of OR at which the trail freezes.", Order = 23, GroupName = "E. Long Bucket 4")]
        public double L4_TrailLockORPercent { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at Ticks", Description = "Freeze the trailing stop when profit reaches Entry + specified ticks.", Order = 24, GroupName = "E. Long Bucket 4")]
        public bool L4_TrailLockTicksEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Trail Lock (Ticks)", Description = "Profit threshold in ticks from entry at which the trail freezes.", Order = 25, GroupName = "E. Long Bucket 4")]
        public int L4_TrailLockTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakeven", Description = "Enable breakeven stop adjustment when profit threshold is reached.", Order = 26, GroupName = "E. Long Bucket 4")]
        public bool L4_UseBreakeven { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 27, GroupName = "E. Long Bucket 4")]
        public int L4_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 28, GroupName = "E. Long Bucket 4")]
        public int L4_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 29, GroupName = "E. Long Bucket 4")]
        public int L4_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 30, GroupName = "E. Long Bucket 4")]
        public int L4_MaxTradesPerDay { get; set; }
        
        // ==========================================
        // ===== F. Short Bucket 1 =====
        // ==========================================
        [NinjaScriptProperty]
        [Display(Name = "Enable S1", Order = 1, GroupName = "F. Short Bucket 1")]
        public bool S1_Enabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Min (Ticks)", Order = 2, GroupName = "F. Short Bucket 1")]
        public int S1_ORMinTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Max (Ticks)", Order = 3, GroupName = "F. Short Bucket 1")]
        public int S1_ORMaxTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use PMR Filter", Description = "When enabled, skip this bucket if pre-market range exceeds Max PMR Ticks.", Order = 4, GroupName = "F. Short Bucket 1")]
        public bool S1_UsePreMarketFilter { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "Max PMR (Ticks)", Description = "Maximum pre-market range in ticks. 0 = no limit when filter is on.", Order = 5, GroupName = "F. Short Bucket 1")]
        public int S1_MaxPreMarketRangeTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakout Rearm", Order = 6, GroupName = "F. Short Bucket 1")]
        public bool S1_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 7, GroupName = "F. Short Bucket 1")]
        public bool S1_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 8, GroupName = "F. Short Bucket 1")]
        public int S1_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 9, GroupName = "F. Short Bucket 1")]
        public double S1_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 10, GroupName = "F. Short Bucket 1")]
        public int S1_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 11, GroupName = "F. Short Bucket 1")]
        public TargetMode S1_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 12, GroupName = "F. Short Bucket 1")]
        public double S1_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 13, GroupName = "F. Short Bucket 1")]
        public int S1_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 14, GroupName = "F. Short Bucket 1")]
        public TargetMode S1_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 15, GroupName = "F. Short Bucket 1")]
        public double S1_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 16, GroupName = "F. Short Bucket 1")]
        public int S1_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 17, GroupName = "F. Short Bucket 1")]
        public int S1_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Trailing Stop", Description = "Trail the stop loss behind price. Trailing stops when BE trigger fires.", Order = 18, GroupName = "F. Short Bucket 1")]
        public bool S1_UseTrailingStop { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Stop % of OR", Description = "Trailing distance as percentage of Opening Range.", Order = 19, GroupName = "F. Short Bucket 1")]
        public double S1_TrailStopPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Activation (% of OR)", Description = "Trail starts moving only after profit reaches this % of OR. 0 = immediate.", Order = 20, GroupName = "F. Short Bucket 1")]
        public double S1_TrailActivationPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Step (Ticks)", Description = "Trail moves in discrete increments of this size. 0 = continuous.", Order = 21, GroupName = "F. Short Bucket 1")]
        public int S1_TrailStepTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at % of OR", Description = "Freeze the trailing stop when profit reaches the specified % of OR.", Order = 22, GroupName = "F. Short Bucket 1")]
        public bool S1_TrailLockOREnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Lock (% of OR)", Description = "Profit threshold in % of OR at which the trail freezes.", Order = 23, GroupName = "F. Short Bucket 1")]
        public double S1_TrailLockORPercent { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at Ticks", Description = "Freeze the trailing stop when profit reaches Entry + specified ticks.", Order = 24, GroupName = "F. Short Bucket 1")]
        public bool S1_TrailLockTicksEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Trail Lock (Ticks)", Description = "Profit threshold in ticks from entry at which the trail freezes.", Order = 25, GroupName = "F. Short Bucket 1")]
        public int S1_TrailLockTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakeven", Description = "Enable breakeven stop adjustment when profit threshold is reached.", Order = 26, GroupName = "F. Short Bucket 1")]
        public bool S1_UseBreakeven { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 27, GroupName = "F. Short Bucket 1")]
        public int S1_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 28, GroupName = "F. Short Bucket 1")]
        public int S1_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 29, GroupName = "F. Short Bucket 1")]
        public int S1_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 30, GroupName = "F. Short Bucket 1")]
        public int S1_MaxTradesPerDay { get; set; }
        
        // ==========================================
        // ===== G. Short Bucket 2 =====
        // ==========================================
        [NinjaScriptProperty]
        [Display(Name = "Enable S2", Order = 1, GroupName = "G. Short Bucket 2")]
        public bool S2_Enabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Min (Ticks)", Order = 2, GroupName = "G. Short Bucket 2")]
        public int S2_ORMinTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Max (Ticks)", Order = 3, GroupName = "G. Short Bucket 2")]
        public int S2_ORMaxTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use PMR Filter", Description = "When enabled, skip this bucket if pre-market range exceeds Max PMR Ticks.", Order = 4, GroupName = "G. Short Bucket 2")]
        public bool S2_UsePreMarketFilter { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "Max PMR (Ticks)", Description = "Maximum pre-market range in ticks. 0 = no limit when filter is on.", Order = 5, GroupName = "G. Short Bucket 2")]
        public int S2_MaxPreMarketRangeTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakout Rearm", Order = 6, GroupName = "G. Short Bucket 2")]
        public bool S2_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 7, GroupName = "G. Short Bucket 2")]
        public bool S2_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 8, GroupName = "G. Short Bucket 2")]
        public int S2_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 9, GroupName = "G. Short Bucket 2")]
        public double S2_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 10, GroupName = "G. Short Bucket 2")]
        public int S2_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 11, GroupName = "G. Short Bucket 2")]
        public TargetMode S2_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 12, GroupName = "G. Short Bucket 2")]
        public double S2_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 13, GroupName = "G. Short Bucket 2")]
        public int S2_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 14, GroupName = "G. Short Bucket 2")]
        public TargetMode S2_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 15, GroupName = "G. Short Bucket 2")]
        public double S2_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 16, GroupName = "G. Short Bucket 2")]
        public int S2_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 17, GroupName = "G. Short Bucket 2")]
        public int S2_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Trailing Stop", Description = "Trail the stop loss behind price. Trailing stops when BE trigger fires.", Order = 18, GroupName = "G. Short Bucket 2")]
        public bool S2_UseTrailingStop { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Stop % of OR", Description = "Trailing distance as percentage of Opening Range.", Order = 19, GroupName = "G. Short Bucket 2")]
        public double S2_TrailStopPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Activation (% of OR)", Description = "Trail starts moving only after profit reaches this % of OR. 0 = immediate.", Order = 20, GroupName = "G. Short Bucket 2")]
        public double S2_TrailActivationPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Step (Ticks)", Description = "Trail moves in discrete increments of this size. 0 = continuous.", Order = 21, GroupName = "G. Short Bucket 2")]
        public int S2_TrailStepTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at % of OR", Description = "Freeze the trailing stop when profit reaches the specified % of OR.", Order = 22, GroupName = "G. Short Bucket 2")]
        public bool S2_TrailLockOREnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Lock (% of OR)", Description = "Profit threshold in % of OR at which the trail freezes.", Order = 23, GroupName = "G. Short Bucket 2")]
        public double S2_TrailLockORPercent { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at Ticks", Description = "Freeze the trailing stop when profit reaches Entry + specified ticks.", Order = 24, GroupName = "G. Short Bucket 2")]
        public bool S2_TrailLockTicksEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Trail Lock (Ticks)", Description = "Profit threshold in ticks from entry at which the trail freezes.", Order = 25, GroupName = "G. Short Bucket 2")]
        public int S2_TrailLockTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakeven", Description = "Enable breakeven stop adjustment when profit threshold is reached.", Order = 26, GroupName = "G. Short Bucket 2")]
        public bool S2_UseBreakeven { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 27, GroupName = "G. Short Bucket 2")]
        public int S2_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 28, GroupName = "G. Short Bucket 2")]
        public int S2_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 29, GroupName = "G. Short Bucket 2")]
        public int S2_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 30, GroupName = "G. Short Bucket 2")]
        public int S2_MaxTradesPerDay { get; set; }
        
        // ==========================================
        // ===== H. Short Bucket 3 =====
        // ==========================================
        [NinjaScriptProperty]
        [Display(Name = "Enable S3", Order = 1, GroupName = "H. Short Bucket 3")]
        public bool S3_Enabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Min (Ticks)", Order = 2, GroupName = "H. Short Bucket 3")]
        public int S3_ORMinTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Max (Ticks)", Order = 3, GroupName = "H. Short Bucket 3")]
        public int S3_ORMaxTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use PMR Filter", Description = "When enabled, skip this bucket if pre-market range exceeds Max PMR Ticks.", Order = 4, GroupName = "H. Short Bucket 3")]
        public bool S3_UsePreMarketFilter { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "Max PMR (Ticks)", Description = "Maximum pre-market range in ticks. 0 = no limit when filter is on.", Order = 5, GroupName = "H. Short Bucket 3")]
        public int S3_MaxPreMarketRangeTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakout Rearm", Order = 6, GroupName = "H. Short Bucket 3")]
        public bool S3_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 7, GroupName = "H. Short Bucket 3")]
        public bool S3_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 8, GroupName = "H. Short Bucket 3")]
        public int S3_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 9, GroupName = "H. Short Bucket 3")]
        public double S3_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 10, GroupName = "H. Short Bucket 3")]
        public int S3_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 11, GroupName = "H. Short Bucket 3")]
        public TargetMode S3_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 12, GroupName = "H. Short Bucket 3")]
        public double S3_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 13, GroupName = "H. Short Bucket 3")]
        public int S3_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 14, GroupName = "H. Short Bucket 3")]
        public TargetMode S3_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 15, GroupName = "H. Short Bucket 3")]
        public double S3_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 16, GroupName = "H. Short Bucket 3")]
        public int S3_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 17, GroupName = "H. Short Bucket 3")]
        public int S3_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Trailing Stop", Description = "Trail the stop loss behind price. Trailing stops when BE trigger fires.", Order = 18, GroupName = "H. Short Bucket 3")]
        public bool S3_UseTrailingStop { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Stop % of OR", Description = "Trailing distance as percentage of Opening Range.", Order = 19, GroupName = "H. Short Bucket 3")]
        public double S3_TrailStopPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Activation (% of OR)", Description = "Trail starts moving only after profit reaches this % of OR. 0 = immediate.", Order = 20, GroupName = "H. Short Bucket 3")]
        public double S3_TrailActivationPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Step (Ticks)", Description = "Trail moves in discrete increments of this size. 0 = continuous.", Order = 21, GroupName = "H. Short Bucket 3")]
        public int S3_TrailStepTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at % of OR", Description = "Freeze the trailing stop when profit reaches the specified % of OR.", Order = 22, GroupName = "H. Short Bucket 3")]
        public bool S3_TrailLockOREnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Lock (% of OR)", Description = "Profit threshold in % of OR at which the trail freezes.", Order = 23, GroupName = "H. Short Bucket 3")]
        public double S3_TrailLockORPercent { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at Ticks", Description = "Freeze the trailing stop when profit reaches Entry + specified ticks.", Order = 24, GroupName = "H. Short Bucket 3")]
        public bool S3_TrailLockTicksEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Trail Lock (Ticks)", Description = "Profit threshold in ticks from entry at which the trail freezes.", Order = 25, GroupName = "H. Short Bucket 3")]
        public int S3_TrailLockTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakeven", Description = "Enable breakeven stop adjustment when profit threshold is reached.", Order = 26, GroupName = "H. Short Bucket 3")]
        public bool S3_UseBreakeven { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 27, GroupName = "H. Short Bucket 3")]
        public int S3_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 28, GroupName = "H. Short Bucket 3")]
        public int S3_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 29, GroupName = "H. Short Bucket 3")]
        public int S3_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 30, GroupName = "H. Short Bucket 3")]
        public int S3_MaxTradesPerDay { get; set; }
        
        // ==========================================
        // ===== I. Short Bucket 4 =====
        // ==========================================
        [NinjaScriptProperty]
        [Display(Name = "Enable S4", Order = 1, GroupName = "I. Short Bucket 4")]
        public bool S4_Enabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Min (Ticks)", Order = 2, GroupName = "I. Short Bucket 4")]
        public int S4_ORMinTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "OR Max (Ticks)", Order = 3, GroupName = "I. Short Bucket 4")]
        public int S4_ORMaxTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use PMR Filter", Description = "When enabled, skip this bucket if pre-market range exceeds Max PMR Ticks.", Order = 4, GroupName = "I. Short Bucket 4")]
        public bool S4_UsePreMarketFilter { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 99999)]
        [Display(Name = "Max PMR (Ticks)", Description = "Maximum pre-market range in ticks. 0 = no limit when filter is on.", Order = 5, GroupName = "I. Short Bucket 4")]
        public int S4_MaxPreMarketRangeTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakout Rearm", Order = 6, GroupName = "I. Short Bucket 4")]
        public bool S4_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 7, GroupName = "I. Short Bucket 4")]
        public bool S4_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 8, GroupName = "I. Short Bucket 4")]
        public int S4_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 9, GroupName = "I. Short Bucket 4")]
        public double S4_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 10, GroupName = "I. Short Bucket 4")]
        public int S4_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 11, GroupName = "I. Short Bucket 4")]
        public TargetMode S4_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 12, GroupName = "I. Short Bucket 4")]
        public double S4_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 13, GroupName = "I. Short Bucket 4")]
        public int S4_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 14, GroupName = "I. Short Bucket 4")]
        public TargetMode S4_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 15, GroupName = "I. Short Bucket 4")]
        public double S4_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 16, GroupName = "I. Short Bucket 4")]
        public int S4_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 17, GroupName = "I. Short Bucket 4")]
        public int S4_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Trailing Stop", Description = "Trail the stop loss behind price. Trailing stops when BE trigger fires.", Order = 18, GroupName = "I. Short Bucket 4")]
        public bool S4_UseTrailingStop { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Stop % of OR", Description = "Trailing distance as percentage of Opening Range.", Order = 19, GroupName = "I. Short Bucket 4")]
        public double S4_TrailStopPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Activation (% of OR)", Description = "Trail starts moving only after profit reaches this % of OR. 0 = immediate.", Order = 20, GroupName = "I. Short Bucket 4")]
        public double S4_TrailActivationPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Trail Step (Ticks)", Description = "Trail moves in discrete increments of this size. 0 = continuous.", Order = 21, GroupName = "I. Short Bucket 4")]
        public int S4_TrailStepTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at % of OR", Description = "Freeze the trailing stop when profit reaches the specified % of OR.", Order = 22, GroupName = "I. Short Bucket 4")]
        public bool S4_TrailLockOREnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Trail Lock (% of OR)", Description = "Profit threshold in % of OR at which the trail freezes.", Order = 23, GroupName = "I. Short Bucket 4")]
        public double S4_TrailLockORPercent { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Lock Trail at Ticks", Description = "Freeze the trailing stop when profit reaches Entry + specified ticks.", Order = 24, GroupName = "I. Short Bucket 4")]
        public bool S4_TrailLockTicksEnabled { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Trail Lock (Ticks)", Description = "Profit threshold in ticks from entry at which the trail freezes.", Order = 25, GroupName = "I. Short Bucket 4")]
        public int S4_TrailLockTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Breakeven", Description = "Enable breakeven stop adjustment when profit threshold is reached.", Order = 26, GroupName = "I. Short Bucket 4")]
        public bool S4_UseBreakeven { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 27, GroupName = "I. Short Bucket 4")]
        public int S4_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 28, GroupName = "I. Short Bucket 4")]
        public int S4_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 29, GroupName = "I. Short Bucket 4")]
        public int S4_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 30, GroupName = "I. Short Bucket 4")]
        public int S4_MaxTradesPerDay { get; set; }
        

        // ==========================================
        // ===== J. Order Management =====
        // ==========================================
        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Cancel Order % of OR", Order = 1, GroupName = "J. Orders")]
        public int CancelOrderPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Cancel Order Bars", Order = 2, GroupName = "J. Orders")]
        public int CancelOrderBars { get; set; }
        
        // ==========================================
        // ===== K. Session Risk Management =====
        // ==========================================
        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Max Session Profit (Ticks)", Order = 1, GroupName = "K. Session Risk")]
        public int MaxSessionProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Max Session Loss (Ticks)", Order = 2, GroupName = "K. Session Risk")]
        public int MaxSessionLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Total Trades/Day", Order = 3, GroupName = "K. Session Risk")]
        public int MaxTradesPerDay { get; set; }
        
        // ==========================================
        // ===== L. Time Settings =====
        // ==========================================
        [NinjaScriptProperty]
        [Display(Name = "OR Start Time", Order = 1, GroupName = "L. Time")]
        public TimeSpan ORStartTime { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "OR End Time", Order = 2, GroupName = "L. Time")]
        public TimeSpan OREndTime { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Session End", Order = 3, GroupName = "L. Time")]
        public TimeSpan SessionEnd { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "No Trades After", Order = 4, GroupName = "L. Time")]
        public TimeSpan NoTradesAfter { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Pre-Market Start", Order = 5, GroupName = "L. Time")]
        public TimeSpan PreMarketStart { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Pre-Market End", Order = 6, GroupName = "L. Time")]
        public TimeSpan PreMarketEnd { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Skip Start", Order = 7, GroupName = "L. Time")]
        public TimeSpan SkipStart { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Skip End", Order = 8, GroupName = "L. Time")]
        public TimeSpan SkipEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Description = "Block entries inside the configured minutes before and after listed 14:00 news events.", GroupName = "N. News", Order = 0)]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Block Minutes", Description = "Minutes blocked before and after each 14:00 news timestamp.", GroupName = "N. News", Order = 1)]
        public int NewsBlockMinutes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TradersPost Webhook URL", Description = "HTTP endpoint for order webhooks. Leave empty to disable.", GroupName = "O. Webhooks", Order = 0)]
        public string WebhookUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Webhook Provider", Description = "Select webhook target: TradersPost or ProjectX.", GroupName = "O. Webhooks", Order = 1)]
        public WebhookProvider WebhookProviderType { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX API Base URL", Description = "ProjectX gateway base URL.", GroupName = "O. Webhooks", Order = 2)]
        public string ProjectXApiBaseUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Username", Description = "ProjectX login username.", GroupName = "O. Webhooks", Order = 3)]
        public string ProjectXUsername { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX API Key", Description = "ProjectX login key.", GroupName = "O. Webhooks", Order = 4)]
        public string ProjectXApiKey { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Account ID", Description = "ProjectX account id used for order routing.", GroupName = "O. Webhooks", Order = 5)]
        public string ProjectXAccountId { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProjectX Contract ID", Description = "ProjectX contract id (for example CON.F.US.DA6.M25).", GroupName = "O. Webhooks", Order = 6)]
        public string ProjectXContractId { get; set; }
        
        // ==========================================
        // ===== M. Visual =====
        // ==========================================
        [XmlIgnore]
        [Display(Name = "Range Box Color", Order = 1, GroupName = "M. Visual")]
        public Brush RangeBoxBrush { get; set; }
        
        [Browsable(false)]
        public string RangeBoxBrushSerializable
        {
            get { return Serialize.BrushToString(RangeBoxBrush); }
            set { RangeBoxBrush = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Show Entry Lines", Order = 2, GroupName = "M. Visual")]
        public bool ShowEntryLines { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Show Target Lines", Order = 3, GroupName = "M. Visual")]
        public bool ShowTargetLines { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Show Stop Lines", Order = 4, GroupName = "M. Visual")]
        public bool ShowStopLines { get; set; }
        
        #endregion
    }
}
