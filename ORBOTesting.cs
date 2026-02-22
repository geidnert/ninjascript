#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
    public class ORBOTesting : Strategy
    {
        public ORBOTesting()
        {
            VendorLicense(204);
        }
        
        #region Enums
        public enum TargetMode
        {
            PercentOfOR,
            FixedTicks
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
            public int BreakevenTriggerPercent;
            public int BreakevenOffsetTicks;
            public int MaxBarsInTrade;
            public int MaxTradesPerDay;
        }
        #endregion
        
        #region Private Variables
        
        // ===== Opening Range Tracking =====
        private double orHigh = double.MinValue;
        private double orLow = double.MaxValue;
        private double orRange = 0;
        private bool orCaptured = false;
        private bool wickLinesDrawn = false;
        
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
        
        // ===== Session State =====
        private DateTime lastDate = DateTime.MinValue;
        private bool maxAccountLimitHit = false;
        private double startingBalance = 0;
        
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
        private static readonly Brush InfoHeaderTextBrush = CreateFrozenBrush(210, 0xD3, 0xD3, 0xD3);
        private static readonly Brush InfoLabelBrush = CreateFrozenBrush(255, 0xA0, 0xA5, 0xB8);
        private static readonly Brush InfoValueBrush = CreateFrozenBrush(255, 0xE6, 0xE8, 0xF2);
        
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"ORBO2 Bot v1.01 - Opening Range Breakout with OR Size Buckets.
4 Long buckets (L1-L4) and 4 Short buckets (S1-S4), each with independent parameters.
USE ON 1-MINUTE CHART.";
                
                Name = "ORBOTesting2";
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
                DebugMode = false;

                // ===== B. Long Bucket 1 =====
                L1_Enabled = true;
                L1_ORMinTicks = 155;
                L1_ORMaxTicks = 300;
                L1_UseBreakoutRearm = true;
                L1_RequireReturnToZone = true;
                L1_ConfirmationBars = 6;
                L1_EntryOffsetPercent = 15.31;
                L1_VarianceTicks = 0;
                L1_TPMode = TargetMode.PercentOfOR;
                L1_TakeProfitPercent = 49.15;
                L1_TakeProfitTicks = 331;
                L1_SLMode = TargetMode.PercentOfOR;
                L1_StopLossPercent = 110.66;
                L1_StopLossTicks = 325;
                L1_MaxStopLossTicks = 255;
                L1_BreakevenTriggerPercent = 18;
                L1_BreakevenOffsetTicks = 0;
                L1_MaxBarsInTrade = 97;
                L1_MaxTradesPerDay = 4;

                // ===== C. Long Bucket 2 =====
                L2_Enabled = true;
                L2_ORMinTicks = 301;
                L2_ORMaxTicks = 500;
                L2_UseBreakoutRearm = true;
                L2_RequireReturnToZone = true;
                L2_ConfirmationBars = 5;
                L2_EntryOffsetPercent = 15.18;
                L2_VarianceTicks = 0;
                L2_TPMode = TargetMode.PercentOfOR;
                L2_TakeProfitPercent = 55.22;
                L2_TakeProfitTicks = 331;
                L2_SLMode = TargetMode.PercentOfOR;
                L2_StopLossPercent = 104.01;
                L2_StopLossTicks = 325;
                L2_MaxStopLossTicks = 470;
                L2_BreakevenTriggerPercent = 17;
                L2_BreakevenOffsetTicks = 2;
                L2_MaxBarsInTrade = 107;
                L2_MaxTradesPerDay = 6;

                // ===== D. Long Bucket 3 =====
                L3_Enabled = true;
                L3_ORMinTicks = 501;
                L3_ORMaxTicks = 750;
                L3_UseBreakoutRearm = true;
                L3_RequireReturnToZone = true;
                L3_ConfirmationBars = 6;
                L3_EntryOffsetPercent = 20.8;
                L3_VarianceTicks = 0;
                L3_TPMode = TargetMode.PercentOfOR;
                L3_TakeProfitPercent = 69.96;
                L3_TakeProfitTicks = 331;
                L3_SLMode = TargetMode.PercentOfOR;
                L3_StopLossPercent = 44.24;
                L3_StopLossTicks = 325;
                L3_MaxStopLossTicks = 240;
                L3_BreakevenTriggerPercent = 27;
                L3_BreakevenOffsetTicks = 8;
                L3_MaxBarsInTrade = 173;
                L3_MaxTradesPerDay = 4;

                // ===== E. Long Bucket 4 =====
                L4_Enabled = true;
                L4_ORMinTicks = 751;
                L4_ORMaxTicks = 1092;
                L4_UseBreakoutRearm = true;
                L4_RequireReturnToZone = true;
                L4_ConfirmationBars = 4;
                L4_EntryOffsetPercent = 15.27;
                L4_VarianceTicks = 0;
                L4_TPMode = TargetMode.PercentOfOR;
                L4_TakeProfitPercent = 122.98;
                L4_TakeProfitTicks = 331;
                L4_SLMode = TargetMode.PercentOfOR;
                L4_StopLossPercent = 51.38;
                L4_StopLossTicks = 325;
                L4_MaxStopLossTicks = 455;
                L4_BreakevenTriggerPercent = 39;
                L4_BreakevenOffsetTicks = 40;
                L4_MaxBarsInTrade = 188;
                L4_MaxTradesPerDay = 3;

                // ===== F. Short Bucket 1 =====
                S1_Enabled = true;
                S1_ORMinTicks = 120;
                S1_ORMaxTicks = 300;
                S1_UseBreakoutRearm = true;
                S1_RequireReturnToZone = true;
                S1_ConfirmationBars = 5;
                S1_EntryOffsetPercent = 12.06;
                S1_VarianceTicks = 0;
                S1_TPMode = TargetMode.FixedTicks;
                S1_TakeProfitPercent = 75.0;
                S1_TakeProfitTicks = 268;
                S1_SLMode = TargetMode.PercentOfOR;
                S1_StopLossPercent = 103.67;
                S1_StopLossTicks = 145;
                S1_MaxStopLossTicks = 277;
                S1_BreakevenTriggerPercent = 28;
                S1_BreakevenOffsetTicks = 1;
                S1_MaxBarsInTrade = 60;
                S1_MaxTradesPerDay = 5;

                // ===== G. Short Bucket 2 =====
                S2_Enabled = true;
                S2_ORMinTicks = 301;
                S2_ORMaxTicks = 500;
                S2_UseBreakoutRearm = true;
                S2_RequireReturnToZone = true;
                S2_ConfirmationBars = 8;
                S2_EntryOffsetPercent = 28.04;
                S2_VarianceTicks = 0;
                S2_TPMode = TargetMode.PercentOfOR;
                S2_TakeProfitPercent = 186.01;
                S2_TakeProfitTicks = 268;
                S2_SLMode = TargetMode.PercentOfOR;
                S2_StopLossPercent = 107.15;
                S2_StopLossTicks = 145;
                S2_MaxStopLossTicks = 405;
                S2_BreakevenTriggerPercent = 24;
                S2_BreakevenOffsetTicks = 0;
                S2_MaxBarsInTrade = 116;
                S2_MaxTradesPerDay = 3;

                // ===== H. Short Bucket 3 =====
                S3_Enabled = true;
                S3_ORMinTicks = 501;
                S3_ORMaxTicks = 750;
                S3_UseBreakoutRearm = true;
                S3_RequireReturnToZone = true;
                S3_ConfirmationBars = 8;
                S3_EntryOffsetPercent = 13.14;
                S3_VarianceTicks = 0;
                S3_TPMode = TargetMode.PercentOfOR;
                S3_TakeProfitPercent = 56.12;
                S3_TakeProfitTicks = 268;
                S3_SLMode = TargetMode.PercentOfOR;
                S3_StopLossPercent = 108.34;
                S3_StopLossTicks = 145;
                S3_MaxStopLossTicks = 756;
                S3_BreakevenTriggerPercent = 28;
                S3_BreakevenOffsetTicks = 2;
                S3_MaxBarsInTrade = 0;
                S3_MaxTradesPerDay = 3;

                // ===== I. Short Bucket 4 =====
                S4_Enabled = true;
                S4_ORMinTicks = 751;
                S4_ORMaxTicks = 1025;
                S4_UseBreakoutRearm = true;
                S4_RequireReturnToZone = true;
                S4_ConfirmationBars = 3;
                S4_EntryOffsetPercent = 14.55;
                S4_VarianceTicks = 0;
                S4_TPMode = TargetMode.PercentOfOR;
                S4_TakeProfitPercent = 72.11;
                S4_TakeProfitTicks = 268;
                S4_SLMode = TargetMode.PercentOfOR;
                S4_StopLossPercent = 62.91;
                S4_StopLossTicks = 145;
                S4_MaxStopLossTicks = 312;
                S4_BreakevenTriggerPercent = 29;
                S4_BreakevenOffsetTicks = 10;
                S4_MaxBarsInTrade = 0;
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
                OREndTime = DateTime.Parse("09:45").TimeOfDay;
                SessionEnd = DateTime.Parse("16:05").TimeOfDay;
                NoTradesAfter = DateTime.Parse("14:55").TimeOfDay;
                
                // ===== M. Skip Times =====
                SkipStart = DateTime.Parse("00:00").TimeOfDay;
                SkipEnd = DateTime.Parse("00:00").TimeOfDay;
                UseNewsSkip = true;
                NewsBlockMinutes = 1;
                
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
            }
            else if (State == State.DataLoaded)
            {
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
                    p.BreakevenTriggerPercent = L1_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = L1_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = L1_MaxBarsInTrade;
                    p.MaxTradesPerDay = L1_MaxTradesPerDay;
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
                    p.BreakevenTriggerPercent = L2_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = L2_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = L2_MaxBarsInTrade;
                    p.MaxTradesPerDay = L2_MaxTradesPerDay;
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
                    p.BreakevenTriggerPercent = L3_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = L3_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = L3_MaxBarsInTrade;
                    p.MaxTradesPerDay = L3_MaxTradesPerDay;
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
                    p.BreakevenTriggerPercent = L4_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = L4_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = L4_MaxBarsInTrade;
                    p.MaxTradesPerDay = L4_MaxTradesPerDay;
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
                    p.BreakevenTriggerPercent = S1_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = S1_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = S1_MaxBarsInTrade;
                    p.MaxTradesPerDay = S1_MaxTradesPerDay;
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
                    p.BreakevenTriggerPercent = S2_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = S2_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = S2_MaxBarsInTrade;
                    p.MaxTradesPerDay = S2_MaxTradesPerDay;
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
                    p.BreakevenTriggerPercent = S3_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = S3_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = S3_MaxBarsInTrade;
                    p.MaxTradesPerDay = S3_MaxTradesPerDay;
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
                    p.BreakevenTriggerPercent = S4_BreakevenTriggerPercent;
                    p.BreakevenOffsetTicks = S4_BreakevenOffsetTicks;
                    p.MaxBarsInTrade = S4_MaxBarsInTrade;
                    p.MaxTradesPerDay = S4_MaxTradesPerDay;
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
            
            tradeCount++; longTradeCount++;
            currentSignalName = "LongOR_" + tradeCount;
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
            
            tradeCount++; shortTradeCount++;
            currentSignalName = "ShortOR_" + tradeCount;
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
            confirmationComplete = false; confirmationBarCount = 0;
            longBreakoutOccurred = false; shortBreakoutOccurred = false;
        }
        
        private void CheckBreakevenTrigger()
        {
            int bePct = lastTradeWasLong ? activeLongBucket.BreakevenTriggerPercent : activeShortBucket.BreakevenTriggerPercent;
            int beOff = lastTradeWasLong ? activeLongBucket.BreakevenOffsetTicks : activeShortBucket.BreakevenOffsetTicks;
            
            if (bePct <= 0 || beTriggerActive || orRange <= 0) return;
            
            double threshold = orRange * (bePct / 100.0);
            double profit = Position.MarketPosition == MarketPosition.Long ? Close[0] - entryPrice :
                            Position.MarketPosition == MarketPosition.Short ? entryPrice - Close[0] : 0;
            
            if (profit >= threshold)
            {
                beTriggerActive = true;
                double beStop = Position.MarketPosition == MarketPosition.Long ? entryPrice + beOff * TickSize : entryPrice - beOff * TickSize;
                SetStopLoss(currentSignalName, CalculationMode.Price, beStop, false);
                if (DebugMode) DebugPrint($"BREAKEVEN @ {profit:F2} pts | Stop: {beStop:F2}");
            }
        }
        
        private void ExitAllPositions(string reason)
        {
            if (Position.MarketPosition == MarketPosition.Long) ExitLong("Exit_" + reason, currentSignalName);
            else if (Position.MarketPosition == MarketPosition.Short) ExitShort("Exit_" + reason, currentSignalName);
            CancelAllOrders();
        }
        
        private void CancelAllOrders()
        {
            if (entryOrder != null && entryOrder.OrderState == OrderState.Working)
            { CancelOrder(entryOrder); entryOrder = null; }
        }
        
        #endregion

        #region Order Events
        
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order.Name == currentSignalName)
            {
                entryOrder = order;
                if (orderState == OrderState.Filled)
                {
                    entryPrice = averageFillPrice;
                    lastFilledEntryPrice = averageFillPrice;
                    entryBar = CurrentBar;
                    limitEntryPrice = 0; entryOrderBar = -1;
                    SetInitialStopAndTarget(lastTradeWasLong);
                    if (DebugMode) DebugPrint($"FILLED {(lastTradeWasLong ? "LONG" : "SHORT")} @ {averageFillPrice:F2}");
                }
                else if (orderState == OrderState.Cancelled)
                { entryOrder = null; limitEntryPrice = 0; entryOrderBar = -1; }
            }
        }
        
        private void SetInitialStopAndTarget(bool isLong)
        {
            if (orRange <= 0 || entryPrice <= 0) return;
            
            // Direction safety
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
            
            double stopPx, tpPx;
            if (isLong)
            {
                stopPx = entryPrice - stopDist;
                tpPx = entryPrice + profitDist;
                if (stopPx >= entryPrice) stopPx = entryPrice - (orRange * 0.5);
            }
            else
            {
                stopPx = entryPrice + stopDist;
                tpPx = entryPrice - profitDist;
                if (stopPx <= entryPrice) stopPx = entryPrice + (orRange * 0.5);
            }
            
            if (DebugMode)
            {
                string bl = isLong ? $"L{activeLongBucketIndex}" : $"S{activeShortBucketIndex}";
                DebugPrint($"SL/TP [{bl}]: {(isLong?"LONG":"SHORT")} Entry={entryPrice:F2} Stop={stopPx:F2} Target={tpPx:F2}");
            }
            
            SetStopLoss(currentSignalName, CalculationMode.Price, stopPx, false);
            SetProfitTarget(currentSignalName, CalculationMode.Price, tpPx);
        }
        
        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            if (Position.MarketPosition == MarketPosition.Flat && execution.Order.OrderState == OrderState.Filled)
            {
                bool isExit = !execution.Order.Name.Contains("LongOR_") && !execution.Order.Name.Contains("ShortOR_");
                if (isExit && lastFilledEntryPrice > 0)
                {
                    double pnl = lastTradeWasLong ? (price - lastFilledEntryPrice) / TickSize : (lastFilledEntryPrice - price) / TickSize;
                    sessionRealizedPnL += pnl;
                    if (DebugMode) DebugPrint($"CLOSED: {pnl:F1}t | Session: {sessionRealizedPnL:F1}t");
                }
                
                beTriggerActive = false;
                bool useRearm = lastTradeWasLong ? activeLongBucket.UseBreakoutRearm : activeShortBucket.UseBreakoutRearm;
                if (useRearm)
                {
                    hasReturnedOnce = false; waitingForConfirmation = true;
                    confirmationComplete = false; returnBar = -1;
                }
            }
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
                longBucketFound = false; shortBucketFound = false;
                activeLongBucketIndex = -1; activeShortBucketIndex = -1;
                breakoutActive = false; longBreakoutOccurred = false; shortBreakoutOccurred = false; breakoutBar = -1;
                hasReturnedOnce = false; waitingForConfirmation = false; confirmationComplete = false;
                confirmationBarCount = 0; returnBar = -1;
                orderPlaced = false; entryBar = -1; entryOrderBar = -1; entryOrder = null;
                beTriggerActive = false; maxAccountLimitHit = false;
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
                CancelOrder(entryOrder);
            entryOrder = null; limitEntryPrice = 0; entryOrderBar = -1;
        }
        
        private bool ShouldAccountBalanceExit()
        {
            if (MaxAccountBalance <= 0) return false;
            double bal = Account.Get(AccountItem.CashValue, Currency.UsDollar);
            if (bal >= MaxAccountBalance && !maxAccountLimitHit) maxAccountLimitHit = true;
            return maxAccountLimitHit;
        }
        
        private void ExitIfSessionEnded()
        { if (Time[0].TimeOfDay >= sessionEndTime && Position.MarketPosition != MarketPosition.Flat) ExitAllPositions("SessionEnd"); }

        private bool IsAfterSessionEnd(DateTime time)
        {
            return time.TimeOfDay >= sessionEndTime;
        }
        
        private void HandleNoTradeAndSkipTransitions()
        {
            bool inNoTradesAfterNow = IsInNoTradesAfterWindow(Time[0]);
            bool inSkipNow = IsInSkipWindow(Time[0]);
            bool inNewsSkipNow = IsInNewsSkipWindow(Time[0]);

            if (!wasInNoTradesAfterWindow && inNoTradesAfterNow)
            {
                CancelAllOrders();
                if (Position.MarketPosition != MarketPosition.Flat)
                    ExitAllPositions("NoTradesAfter");
                if (DebugMode) DebugPrint("Entered NoTradesAfter window: canceling working entries and flattening open position.");
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
                TextOptions.SetTextRenderingMode(text, TextRenderingMode.ClearType);

                string label = lines[i].label ?? string.Empty;
                string value = lines[i].value ?? string.Empty;

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

            lines.Add((string.Format("ORBO v{0}", GetAddOnVersion()), string.Empty, InfoHeaderTextBrush, Brushes.Transparent));

            if (!orCaptured)
            {
                lines.Add(("OR Size: 0 pts", string.Empty, Brushes.LightGray, Brushes.Transparent));
                bool armed = IsTradeArmed();
                lines.Add(("Armed:", armed ? "✔" : "⛔", Brushes.LightGray, armed ? Brushes.LimeGreen : Brushes.IndianRed));
                lines.Add(("Session: New York", string.Empty, Brushes.LightGray, Brushes.Transparent));
            }
            else
            {
                lines.Add(("OR Size: " + orRange.ToString("F2") + " pts", string.Empty, Brushes.LightGray, Brushes.Transparent));
                
                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    double pft = Position.MarketPosition == MarketPosition.Long ? Close[0] - entryPrice : entryPrice - Close[0];
                    double pPct = orRange > 0 ? (pft / orRange) * 100 : 0;
                    string inTradeLine = "IN TRADE: " + pft.ToString("F2") + " (" + pPct.ToString("F1") + "% OR)";
                    if (beTriggerActive)
                        inTradeLine += " [BE]";
                    lines.Add((inTradeLine, string.Empty, Brushes.LightGray, Brushes.Transparent));
                }

                bool armed = IsTradeArmed();
                lines.Add(("Armed:", armed ? "✔" : "⛔", Brushes.LightGray, armed ? Brushes.LimeGreen : Brushes.IndianRed));
                lines.Add(("Session: New York", string.Empty, Brushes.LightGray, Brushes.Transparent));
            }

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
        [Display(Name = "Use Breakout Rearm", Order = 4, GroupName = "B. Long Bucket 1")]
        public bool L1_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 5, GroupName = "B. Long Bucket 1")]
        public bool L1_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 6, GroupName = "B. Long Bucket 1")]
        public int L1_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 7, GroupName = "B. Long Bucket 1")]
        public double L1_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 8, GroupName = "B. Long Bucket 1")]
        public int L1_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 9, GroupName = "B. Long Bucket 1")]
        public TargetMode L1_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 10, GroupName = "B. Long Bucket 1")]
        public double L1_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 11, GroupName = "B. Long Bucket 1")]
        public int L1_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 12, GroupName = "B. Long Bucket 1")]
        public TargetMode L1_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 13, GroupName = "B. Long Bucket 1")]
        public double L1_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 14, GroupName = "B. Long Bucket 1")]
        public int L1_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 15, GroupName = "B. Long Bucket 1")]
        public int L1_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 16, GroupName = "B. Long Bucket 1")]
        public int L1_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 17, GroupName = "B. Long Bucket 1")]
        public int L1_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 18, GroupName = "B. Long Bucket 1")]
        public int L1_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 19, GroupName = "B. Long Bucket 1")]
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
        [Display(Name = "Use Breakout Rearm", Order = 4, GroupName = "C. Long Bucket 2")]
        public bool L2_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 5, GroupName = "C. Long Bucket 2")]
        public bool L2_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 6, GroupName = "C. Long Bucket 2")]
        public int L2_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 7, GroupName = "C. Long Bucket 2")]
        public double L2_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 8, GroupName = "C. Long Bucket 2")]
        public int L2_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 9, GroupName = "C. Long Bucket 2")]
        public TargetMode L2_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 10, GroupName = "C. Long Bucket 2")]
        public double L2_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 11, GroupName = "C. Long Bucket 2")]
        public int L2_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 12, GroupName = "C. Long Bucket 2")]
        public TargetMode L2_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 13, GroupName = "C. Long Bucket 2")]
        public double L2_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 14, GroupName = "C. Long Bucket 2")]
        public int L2_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 15, GroupName = "C. Long Bucket 2")]
        public int L2_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 16, GroupName = "C. Long Bucket 2")]
        public int L2_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 17, GroupName = "C. Long Bucket 2")]
        public int L2_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 18, GroupName = "C. Long Bucket 2")]
        public int L2_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 19, GroupName = "C. Long Bucket 2")]
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
        [Display(Name = "Use Breakout Rearm", Order = 4, GroupName = "D. Long Bucket 3")]
        public bool L3_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 5, GroupName = "D. Long Bucket 3")]
        public bool L3_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 6, GroupName = "D. Long Bucket 3")]
        public int L3_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 7, GroupName = "D. Long Bucket 3")]
        public double L3_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 8, GroupName = "D. Long Bucket 3")]
        public int L3_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 9, GroupName = "D. Long Bucket 3")]
        public TargetMode L3_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 10, GroupName = "D. Long Bucket 3")]
        public double L3_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 11, GroupName = "D. Long Bucket 3")]
        public int L3_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 12, GroupName = "D. Long Bucket 3")]
        public TargetMode L3_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 13, GroupName = "D. Long Bucket 3")]
        public double L3_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 14, GroupName = "D. Long Bucket 3")]
        public int L3_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 15, GroupName = "D. Long Bucket 3")]
        public int L3_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 16, GroupName = "D. Long Bucket 3")]
        public int L3_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 17, GroupName = "D. Long Bucket 3")]
        public int L3_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 18, GroupName = "D. Long Bucket 3")]
        public int L3_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 19, GroupName = "D. Long Bucket 3")]
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
        [Display(Name = "Use Breakout Rearm", Order = 4, GroupName = "E. Long Bucket 4")]
        public bool L4_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 5, GroupName = "E. Long Bucket 4")]
        public bool L4_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 6, GroupName = "E. Long Bucket 4")]
        public int L4_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 7, GroupName = "E. Long Bucket 4")]
        public double L4_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 8, GroupName = "E. Long Bucket 4")]
        public int L4_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 9, GroupName = "E. Long Bucket 4")]
        public TargetMode L4_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 10, GroupName = "E. Long Bucket 4")]
        public double L4_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 11, GroupName = "E. Long Bucket 4")]
        public int L4_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 12, GroupName = "E. Long Bucket 4")]
        public TargetMode L4_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 13, GroupName = "E. Long Bucket 4")]
        public double L4_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 14, GroupName = "E. Long Bucket 4")]
        public int L4_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 15, GroupName = "E. Long Bucket 4")]
        public int L4_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 16, GroupName = "E. Long Bucket 4")]
        public int L4_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 17, GroupName = "E. Long Bucket 4")]
        public int L4_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 18, GroupName = "E. Long Bucket 4")]
        public int L4_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 19, GroupName = "E. Long Bucket 4")]
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
        [Display(Name = "Use Breakout Rearm", Order = 4, GroupName = "F. Short Bucket 1")]
        public bool S1_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 5, GroupName = "F. Short Bucket 1")]
        public bool S1_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 6, GroupName = "F. Short Bucket 1")]
        public int S1_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 7, GroupName = "F. Short Bucket 1")]
        public double S1_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 8, GroupName = "F. Short Bucket 1")]
        public int S1_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 9, GroupName = "F. Short Bucket 1")]
        public TargetMode S1_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 10, GroupName = "F. Short Bucket 1")]
        public double S1_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 11, GroupName = "F. Short Bucket 1")]
        public int S1_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 12, GroupName = "F. Short Bucket 1")]
        public TargetMode S1_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 13, GroupName = "F. Short Bucket 1")]
        public double S1_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 14, GroupName = "F. Short Bucket 1")]
        public int S1_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 15, GroupName = "F. Short Bucket 1")]
        public int S1_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 16, GroupName = "F. Short Bucket 1")]
        public int S1_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 17, GroupName = "F. Short Bucket 1")]
        public int S1_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 18, GroupName = "F. Short Bucket 1")]
        public int S1_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 19, GroupName = "F. Short Bucket 1")]
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
        [Display(Name = "Use Breakout Rearm", Order = 4, GroupName = "G. Short Bucket 2")]
        public bool S2_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 5, GroupName = "G. Short Bucket 2")]
        public bool S2_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 6, GroupName = "G. Short Bucket 2")]
        public int S2_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 7, GroupName = "G. Short Bucket 2")]
        public double S2_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 8, GroupName = "G. Short Bucket 2")]
        public int S2_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 9, GroupName = "G. Short Bucket 2")]
        public TargetMode S2_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 10, GroupName = "G. Short Bucket 2")]
        public double S2_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 11, GroupName = "G. Short Bucket 2")]
        public int S2_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 12, GroupName = "G. Short Bucket 2")]
        public TargetMode S2_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 13, GroupName = "G. Short Bucket 2")]
        public double S2_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 14, GroupName = "G. Short Bucket 2")]
        public int S2_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 15, GroupName = "G. Short Bucket 2")]
        public int S2_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 16, GroupName = "G. Short Bucket 2")]
        public int S2_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 17, GroupName = "G. Short Bucket 2")]
        public int S2_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 18, GroupName = "G. Short Bucket 2")]
        public int S2_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 19, GroupName = "G. Short Bucket 2")]
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
        [Display(Name = "Use Breakout Rearm", Order = 4, GroupName = "H. Short Bucket 3")]
        public bool S3_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 5, GroupName = "H. Short Bucket 3")]
        public bool S3_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 6, GroupName = "H. Short Bucket 3")]
        public int S3_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 7, GroupName = "H. Short Bucket 3")]
        public double S3_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 8, GroupName = "H. Short Bucket 3")]
        public int S3_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 9, GroupName = "H. Short Bucket 3")]
        public TargetMode S3_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 10, GroupName = "H. Short Bucket 3")]
        public double S3_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 11, GroupName = "H. Short Bucket 3")]
        public int S3_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 12, GroupName = "H. Short Bucket 3")]
        public TargetMode S3_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 13, GroupName = "H. Short Bucket 3")]
        public double S3_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 14, GroupName = "H. Short Bucket 3")]
        public int S3_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 15, GroupName = "H. Short Bucket 3")]
        public int S3_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 16, GroupName = "H. Short Bucket 3")]
        public int S3_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 17, GroupName = "H. Short Bucket 3")]
        public int S3_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 18, GroupName = "H. Short Bucket 3")]
        public int S3_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 19, GroupName = "H. Short Bucket 3")]
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
        [Display(Name = "Use Breakout Rearm", Order = 4, GroupName = "I. Short Bucket 4")]
        public bool S4_UseBreakoutRearm { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Require Return to Zone", Order = 5, GroupName = "I. Short Bucket 4")]
        public bool S4_RequireReturnToZone { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 30)]
        [Display(Name = "Confirmation Bars", Order = 6, GroupName = "I. Short Bucket 4")]
        public int S4_ConfirmationBars { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Entry Offset % of OR", Order = 7, GroupName = "I. Short Bucket 4")]
        public double S4_EntryOffsetPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "Variance (Ticks)", Order = 8, GroupName = "I. Short Bucket 4")]
        public int S4_VarianceTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "TP Mode", Order = 9, GroupName = "I. Short Bucket 4")]
        public TargetMode S4_TPMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Take Profit % of OR", Order = 10, GroupName = "I. Short Bucket 4")]
        public double S4_TakeProfitPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Take Profit (Ticks)", Order = 11, GroupName = "I. Short Bucket 4")]
        public int S4_TakeProfitTicks { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "SL Mode", Order = 12, GroupName = "I. Short Bucket 4")]
        public TargetMode S4_SLMode { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.01, 500)]
        [Display(Name = "Stop Loss % of OR", Order = 13, GroupName = "I. Short Bucket 4")]
        public double S4_StopLossPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "Stop Loss (Ticks)", Order = 14, GroupName = "I. Short Bucket 4")]
        public int S4_StopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Max Stop Loss (Ticks)", Order = 15, GroupName = "I. Short Bucket 4")]
        public int S4_MaxStopLossTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "BE Trigger % of OR", Order = 16, GroupName = "I. Short Bucket 4")]
        public int S4_BreakevenTriggerPercent { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BE Offset (Ticks)", Order = 17, GroupName = "I. Short Bucket 4")]
        public int S4_BreakevenOffsetTicks { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Max Bars In Trade", Order = 18, GroupName = "I. Short Bucket 4")]
        public int S4_MaxBarsInTrade { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Max Trades/Day", Order = 19, GroupName = "I. Short Bucket 4")]
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
        [Display(Name = "Skip Start", Order = 5, GroupName = "L. Time")]
        public TimeSpan SkipStart { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Skip End", Order = 6, GroupName = "L. Time")]
        public TimeSpan SkipEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use News Skip", Description = "Block entries inside the configured minutes before and after listed 14:00 news events.", GroupName = "N. News", Order = 0)]
        public bool UseNewsSkip { get; set; }

        [NinjaScriptProperty]
        [Range(0, 240)]
        [Display(Name = "News Block Minutes", Description = "Minutes blocked before and after each 14:00 news timestamp.", GroupName = "N. News", Order = 1)]
        public int NewsBlockMinutes { get; set; }
        
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
