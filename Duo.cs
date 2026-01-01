#region Using declarations
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Windows.Media;
    using System.ComponentModel.DataAnnotations;
    using System.Windows;
    using System.ComponentModel;
    using System.Xml.Serialization;
    using System.IO;
    using NinjaTrader.Cbi;
    using NinjaTrader.Data;
    using NinjaTrader.Gui;
    using NinjaTrader.Gui.Tools;
    using NinjaTrader.NinjaScript;
    using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge
{
    public class Duo : Strategy
    {
        public Duo()
        {
            VendorLicense(337);
        }

        [NinjaScriptProperty]
        [Display(Name = "Number of Contracts", GroupName = "Config", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
		[Display(Name = "Entry Confirmation", Description = "Show popup confirmation before each entry", Order = 2, GroupName = "Config")]
		public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Anti Hedge", Description = "Dont take trade in opposite direction to prevent hedging", Order = 3, GroupName = "Config")]
        public bool AntiHedge {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Webhook URL", Description = "Sends POST JSON to this URL on trade signals", Order = 4, GroupName = "Config")]
        public string WebhookUrl { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reverse On Signal", Description = "If true, flatten current position when a reverse signal is generated, then place the new limit order", Order = 5, GroupName = "Config")]
        public bool ReverseOnSignal { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Minimum 1st+2nd Candle Body", GroupName = "Parameters", Order = 1)]
        internal double MinC12Body { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Maximum 1st+2nd Candle Body", GroupName = "Parameters", Order = 2)]
        internal double MaxC12Body { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Offset % of 1st+2nd Candle Body", GroupName = "Parameters", Order = 5)]
        internal double OffsetPerc { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Take Profit % of 1st+2nd Candle Body", GroupName = "Parameters", Order = 6)]
        internal double TpPerc { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Cancel Order % of 1st+2nd Candle Body", GroupName = "Parameters", Order = 7)]
        internal double CancelPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Deviation %", Description = "Max random deviation applied to entry/TP %", Order = 8, GroupName = "Parameters")]
        [Range(0, double.MaxValue)]
        public double DeviationPerc { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "SL Padding", Description = "Extra padding added to stop loss (in price units, 0.25 increments)", GroupName = "Parameters", Order = 9)]
        // [Range(0, double.MaxValue)]
        internal double SLPadding { get; set; }

		// [NinjaScriptProperty]
        // [Display(Name = "Max SL/TP Ratio %", Description = "Skip trades if SL is more than this % of TP (e.g., 200 = SL can be at most 2x TP)", Order = 10, GroupName = "Parameters")]
        // [Range(0, 1000)]
        internal double MaxSLTPRatioPerc { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "SL type", Description = "Select the sl type you want", Order = 11, GroupName = "Parameters")]
        internal SLPreset SLPresetSetting { get; set; }

		// [NinjaScriptProperty]
        // [Display(Name = "SL % of 1st Candle", Description = "0% = High of 1st candle (long), 100% = Low of 1st candle (long)", Order = 12, GroupName = "Parameters")]
        // [Range(0, 100)]
        internal double SLPercentFirstCandle { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Max SL (Points)", Description = "Maximum allowed stop loss in points. 0 = Disabled", Order = 13, GroupName = "Parameters")]
        internal double MaxSLPoints { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Session Start", Description = "When session is starting", Order = 1, GroupName = "Session Time")]
        internal TimeSpan SessionStart
        {
            get {
                return sessionStart;
            }
            set {
                sessionStart = new TimeSpan(value.Hours, value.Minutes, 0);
            }
        }

        // [NinjaScriptProperty]
        // [Display(Name = "Session End", Description = "When session is ending, all positions and orders will be canceled when this time is passed", Order = 2, GroupName = "Session Time")]
        internal TimeSpan SessionEnd
        {
            get {
                return sessionEnd;
            }
            set {
                sessionEnd = new TimeSpan(value.Hours, value.Minutes, 0);
            }
        }

		// [NinjaScriptProperty]
		// [Display(Name = "No Trades After", Description = "No more orders is being placed between this time and session end,", Order = 3, GroupName = "Session Time")]
		internal TimeSpan NoTradesAfter
		{
			get {
				return noTradesAfter;
			}
			set {
				noTradesAfter = new TimeSpan(value.Hours, value.Minutes, 0);
			}
		}

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Session Fill", Description = "Color of the session background", Order = 4, GroupName = "Session Time")]
        public Brush SessionBrush { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Trade Asia Session", Description = "Allow trading during the Asia session", Order = 5, GroupName = "Session Time")]
        internal bool TradeAsiaSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade London Session (01:30-05:30)", Description = "Allow trading during the London session", Order = 6, GroupName = "Session Time")]
        public bool TradeLondonSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Trade New York Session (09:40-15:00)", Description = "Allow trading during the New York session", Order = 7, GroupName = "Session Time")]
        public bool TradeNewYorkSession { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Close at Session End", Description = "If true, open trades will be closed and working orders canceled at session end", Order = 4, GroupName = "Session Time")]
        internal bool CloseAtSessionEnd { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Skip Start", Description = "Start of skip window", Order = 1, GroupName = "Skip Times")]
        internal TimeSpan SkipStart { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Skip End", Description = "End of skip window", Order = 2, GroupName = "Skip Times")]
        internal TimeSpan SkipEnd { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Skip Start 2", Description = "Start of 2nd skip window", Order = 3, GroupName = "Skip Times")]
        internal TimeSpan Skip2Start { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Skip End 2", Description = "End of 2nd skip window", Order = 4, GroupName = "Skip Times")]
        internal TimeSpan Skip2End { get; set; }

        // [NinjaScriptProperty]
        // [Display(Name = "Force Close at Skip Start", Description = "If true, flatten/cancel as soon as a skip window begins", Order = 5, GroupName = "Skip Times")]
        internal bool ForceCloseAtSkipStart { get; set; }

        // State tracking
        private bool longOrderPlaced = false;
        private bool shortOrderPlaced = false;
        private Order longEntryOrder = null;
        private Order shortEntryOrder = null;
        private TimeSpan sessionStart = new TimeSpan(9, 40, 0);
        private TimeSpan sessionEnd = new TimeSpan(14, 50, 0);  
		private TimeSpan noTradesAfter = new TimeSpan(14, 30, 0);
        private TimeSpan skipStart = new TimeSpan(00, 00, 0);
        private TimeSpan skipEnd = new TimeSpan(00, 00, 0);
        private TimeSpan skip2Start = new TimeSpan(00, 00, 0);
        private TimeSpan skip2End = new TimeSpan(00, 00, 0); 
        private int skipBarUntil = -1;
        private int lastBarProcessed = -1;
        private double currentLongTP = 0;
        private double currentShortTP = 0;
        private double currentLongEntry = 0;
        private double currentShortEntry = 0;
        private double currentLongSL = 0;
        private double currentShortSL = 0;
        private bool First_Candle_High_Low = true;
        private bool First_Candle_Open = false;
        private bool Second_Candle_High_Low = false;
        private bool Second_Candle_Open= false;
        private double currentLongCancelPrice = 0;
        private double currentShortCancelPrice = 0;
        private Random rng;
        private string displayText = "Waiting...";
        private bool sessionClosed = false;
        private bool debug = true;
        private bool reverseFlattensLivePosition = true; // false = legacy behavior (only cancel pending reverse when flat)
        private int longSignalBar = -1;
        private int shortSignalBar = -1;
        private bool longLinesActive = false;
        private bool shortLinesActive = false;
        private int longExitBar = -1;
        private int shortExitBar = -1;
        private bool londonAutoShiftTimes;
        private StrategyPreset activePreset = StrategyPreset.New_York;
        private bool presetInitialized;
        private DateTime effectiveTimesDate = DateTime.MinValue;
        private TimeSpan effectiveSessionStart;
        private TimeSpan effectiveSessionEnd;
        private TimeSpan effectiveNoTradesAfter;
        private TimeSpan effectiveSkipStart;
        private TimeSpan effectiveSkipEnd;
        private TimeSpan effectiveSkip2Start;
        private TimeSpan effectiveSkip2End;
		private TimeZoneInfo targetTimeZone;
		private TimeZoneInfo londonTimeZone;
        private int lineTagCounter = 0;
        private string longLineTagPrefix = "LongLine_0_";
        private string shortLineTagPrefix = "ShortLine_0_";

        // --- Webhook state (send entry only after order Accepted/Working) ---
        private bool pendingLongWebhook;
        private double pendingLongEntry;
        private double pendingLongTP;
        private double pendingLongSL;
        private bool pendingShortWebhook;
        private double pendingShortEntry;
        private double pendingShortTP;
        private double pendingShortSL;
        private string lastLongWebhookOrderId;
        private string lastShortWebhookOrderId;
        private double lastLongWebhookEntry;
        private double lastLongWebhookTP;
        private double lastLongWebhookSL;
        private double lastShortWebhookEntry;
        private double lastShortWebhookTP;
        private double lastShortWebhookSL;
	        private int lastCancelWebhookBar = -1;
	        private int lastExitWebhookBar = -1;

	        // --- Heartbeat reporting ---
	        private string heartbeatFile = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv");
	        private System.Timers.Timer heartbeatTimer;
	        private DateTime lastHeartbeatWrite = DateTime.MinValue;
        private int heartbeatIntervalSeconds = 10; // send heartbeat every 10 seconds
        private static readonly object heartbeatFileLock = new object();
        private string heartbeatId;

        // === Shared Anti-Hedge Lock System ===
        private static readonly object hedgeLockSync = new object();
        private static readonly string hedgeLockFile = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "AntiHedgeLock.csv");

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Duo";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsInstantiatedOnEachOptimizationIteration = false;

                // Default input values
                Contracts     = 1;
                MinC12Body  = 19.5;
                MaxC12Body  = 130.5;
                OffsetPerc  = 1;
                TpPerc      = 28;
                CancelPerc  = 9999;
                DeviationPerc = 0;
                SLPadding = 0;
                MaxSLTPRatioPerc = 477;
                SLPresetSetting = SLPreset.First_Candle_High_Low;
                SLPercentFirstCandle = 100;
                MaxSLPoints = 152;

                // Other defaults
                SessionBrush  = Brushes.Gold;
                CloseAtSessionEnd = true;
                TradeAsiaSession = false;
                TradeLondonSession = true;
                TradeNewYorkSession = true;
                SkipStart     = skipStart;
                SkipEnd       = skipEnd;
                Skip2Start     = skip2Start;
                Skip2End       = skip2End;
                londonAutoShiftTimes = false;
                ForceCloseAtSkipStart = true;
                RequireEntryConfirmation = false;
                AntiHedge = false;
                WebhookUrl = "";
                ReverseOnSignal = true;

                // Default session times
                SessionStart  = new TimeSpan(09, 40, 0);
                SessionEnd    = new TimeSpan(14, 50, 0);
                NoTradesAfter = new TimeSpan(14, 30, 0);
            }
            else if (State == State.DataLoaded)
            {
                heartbeatId = BuildHeartbeatId();
                // --- Heartbeat timer setup ---
                heartbeatTimer = new System.Timers.Timer(heartbeatIntervalSeconds * 1000);
                heartbeatTimer.Elapsed += (s, e) => WriteHeartbeat();
                heartbeatTimer.AutoReset = true;
                heartbeatTimer.Start();

                //ApplyStopLossPreset(SLPresetSetting);

                //Print($"\n== PRESETS FINAL: {PresetSetting} | SL = {SLPresetSetting} ==\n");
            }
            else if (State == State.Configure)
            {
                rng = new Random();
            }
            else if (State == State.Terminated)
            {
                // --- Clean up heartbeat timer ---
                if (heartbeatTimer != null)
                {
                    heartbeatTimer.Stop();
                    heartbeatTimer.Dispose();
                    heartbeatTimer = null;
                }
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2)
                return;

			ApplyPresetForTime(Time[0]);
			EnsureEffectiveTimes(Time[0], true);
			TimeSpan sessionEnd = effectiveSessionEnd;
			TimeSpan noTradesAfter = effectiveNoTradesAfter;
			
			// Reset sessionClosed when a new session starts
			if (IsFirstTickOfBar && TimeInSession(Time[0]) && sessionClosed)
			{
			    sessionClosed = false;
			
			    longOrderPlaced = false;
			    shortOrderPlaced = false;
			    longEntryOrder = null;
			    shortEntryOrder = null;
			
			    currentLongEntry = 0;
			    currentShortEntry = 0;
			    currentLongTP = 0;
			    currentShortTP = 0;
                currentLongSL = 0;
			    currentShortSL = 0;
			    currentLongCancelPrice = 0;
			    currentShortCancelPrice = 0;
			
			    skipBarUntil = -1;
			    lastBarProcessed = -1;
                longSignalBar = -1;
                shortSignalBar = -1;
                longLinesActive = false;
	                shortLinesActive = false;
				    longExitBar = -1;
				    shortExitBar = -1;
				
				    displayText = "Waiting...";
				
				    Print($"{Time[0]} - New session started, state reset.");
				}

            DrawSessionBackground();
            UpdateInfo();

			// === Skip window cross detection ===
			bool crossedSkipWindow =
				(!TimeInSkip(Time[1]) && TimeInSkip(Time[0]))   // just entered a skip window
				|| (TimeInSkip(Time[1]) && !TimeInSkip(Time[0])); // just exited a skip window

			if (crossedSkipWindow)
			{
				if (TimeInSkip(Time[0]))
				{
					// 🔹 We just entered a skip window
                    if (debug)
					    Print($"{Time[0]} - ⛔ Entered skip window");

                    if (ForceCloseAtSkipStart)
                    {
					    if (Position.MarketPosition != MarketPosition.Flat)
					    {
						    Flatten("SkipWindow");
					    }
					    else
					    {
						    CancelOrder(shortEntryOrder);
						    CancelOrder(longEntryOrder);
	                            SendWebhookCancelSafe();
					    }
	                        longLinesActive = false;
	                        shortLinesActive = false;
	                        longSignalBar = -1;
	                        shortSignalBar = -1;
                        longExitBar = -1;
                        shortExitBar = -1;
                    }
				}
				else
				{
					// 🔹 We just exited the skip window
                    if (debug)
					    Print($"{Time[0]} - ✅ Exited skip window");
				}
			}


			// === Session check ===
			bool crossedSessionEnd = 
					(Time[1].TimeOfDay <= sessionEnd && Time[0].TimeOfDay > sessionEnd)
					|| (!TimeInSession(Time[0]) && TimeInSession(Time[1]));
            
			if (crossedSessionEnd)
            {
                if (CloseAtSessionEnd)
                {
                    if (Position.MarketPosition != MarketPosition.Flat)
                    {
                        // Case 1: In a position -> flatten (Managed Mode removes TP/SL too)
                        Flatten("SessionEnd");
                    }
                    else
                    {
                        // Case 2: No position but entry orders active -> cancel only entries
                        //CancelEntryOrders("SessionEnd");
							CancelOrder(shortEntryOrder);
							CancelOrder(longEntryOrder);
	                        SendWebhookCancelSafe();
	                    }
	                }
	                else // CloseAtSessionEnd == false
	                {
                    if (Position.MarketPosition == MarketPosition.Flat)
                    {
                        // Case 4: No position -> cancel only entries
                        //CancelEntryOrders("SessionEnd (CloseAtSessionEnd=false)");
							CancelOrder(shortEntryOrder);
							CancelOrder(longEntryOrder);
	                        SendWebhookCancelSafe();
	                    }
	                    // Case 3: In a position -> do nothing, let TP/SL handle it
	                }

                longLinesActive = false;
                shortLinesActive = false;
                longSignalBar = -1;
                shortSignalBar = -1;
                longExitBar = -1;
                shortExitBar = -1;

                sessionClosed = true;
            }

			// === No Trades After check ===
			bool crossedNoTradesAfter = 
				(Time[1].TimeOfDay <= noTradesAfter && Time[0].TimeOfDay > noTradesAfter);

			if (crossedNoTradesAfter)
			{
                if (debug)
				    Print($"{Time[0]} - ⛔ NoTradesAfter time crossed — canceling entry orders");
					CancelOrder(shortEntryOrder);
					CancelOrder(longEntryOrder);
	                SendWebhookCancelSafe();
				}

            // 🔒 HARD GUARD: absolutely no logic inside skip windows
            if (TimeInSkip(Time[0]))
                return;

            // 🔒 HARD GUARD: absolutely no new logic/entries outside session
            if (!TimeInSession(Time[0]))
                return;

            // ✅ Always keep preview lines updating while in session
            UpdatePreviewLines();

			// 🔒 HARD GUARD: no entries after NoTradesAfter (but keep lines)
			if (TimeInNoTradesAfter(Time[0])) {
				CancelEntryIfAfterNoTrades();
				return;
			}

            // === Long Cancel: TP hit before entry === 
            if (longEntryOrder != null && longEntryOrder.OrderState == OrderState.Working)
            {
                if (High[0] >= currentLongCancelPrice && Low[0] > currentLongEntry)
                //if (High[0] >= currentLongTP && Low[0] > currentLongEntry)
                {
	                    if (debug)
	                        Print($"{Time[0]} - 🚫 Long cancel price hit before entry fill. Canceling order.");
	                    CancelOrder(longEntryOrder);
	                    SendWebhookCancelSafe();
	                    longOrderPlaced = false;
	                    longEntryOrder = null;

                    int longCancelStartBarsAgo = longSignalBar >= 0 ? CurrentBar - longSignalBar : 1;

                    Draw.Line(this, longLineTagPrefix + "LongEntryLineActive", false,
                        longCancelStartBarsAgo, currentLongEntry,
                        0, currentLongEntry,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, longLineTagPrefix + "LongTPLineActive", false,
                        longCancelStartBarsAgo, currentLongTP,
                        0, currentLongTP,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, longLineTagPrefix + "LongSLLineActive", false,
                        longCancelStartBarsAgo, currentLongSL,
                        0, currentLongSL,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, longLineTagPrefix + "LongCancelLineActive", false,
                        longCancelStartBarsAgo, currentLongCancelPrice,
                        0, currentLongCancelPrice,
                        Brushes.Gray, DashStyleHelper.Dot, 2);

                    longLinesActive = false;
                    longSignalBar = -1;
                    longExitBar = -1;

                    skipBarUntil = CurrentBar;
                    if (debug)
                        Print($"{Time[0]} - ➡️ Skipping signals until bar > {skipBarUntil}");
                }
            }

            // === Short Cancel: TP hit before entry ===
            if (shortEntryOrder != null && shortEntryOrder.OrderState == OrderState.Working)
            {
                if (Low[0] <= currentShortCancelPrice && High[0] < currentShortEntry)
                //if (Low[0] <= currentShortTP && High[0] < currentShortEntry)
                {
	                    if (debug)
	                        Print($"{Time[0]} - 🚫 Short cancel price hit before entry fill. Canceling order.");
	                    CancelOrder(shortEntryOrder);
	                    SendWebhookCancelSafe();
	                    shortOrderPlaced = false;
	                    shortEntryOrder = null;

                    int shortCancelStartBarsAgo = shortSignalBar >= 0 ? CurrentBar - shortSignalBar : 1;

                    Draw.Line(this, shortLineTagPrefix + "ShortEntryLineActive", false,
                        shortCancelStartBarsAgo, currentShortEntry,
                        0, currentShortEntry,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, shortLineTagPrefix + "ShortTPLineActive", false,
                        shortCancelStartBarsAgo, currentShortTP,
                        0, currentShortTP,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, shortLineTagPrefix + "ShortSLLineActive", false,
                        shortCancelStartBarsAgo, currentShortSL,
                        0, currentShortSL,
                        Brushes.Gray, DashStyleHelper.Solid, 2);
                    Draw.Line(this, shortLineTagPrefix + "ShortCancelLineActive", false,
                        shortCancelStartBarsAgo, currentShortCancelPrice,
                        0, currentShortCancelPrice,
                        Brushes.Gray, DashStyleHelper.Dot, 2);

                    shortLinesActive = false;
                    shortSignalBar = -1;
                    shortExitBar = -1;

                    skipBarUntil = CurrentBar;
                    if (debug)
                        Print($"{Time[0]} - ➡️ Skipping signals until bar > {skipBarUntil}");
                }
            }

            // === Strategy signal logic (on bar close only) ===
            if (Calculate == Calculate.OnBarClose 
                && CurrentBar > skipBarUntil 
                && CurrentBar != lastBarProcessed)
            {
                double c1Open = Open[1];
                double c1Close = Close[1];
                double c1High = High[1];
                double c1Low = Low[1];

                double c2Open = Open[0];
                double c2Close = Close[0];
                double c2High = High[0];
                double c2Low = Low[0];

                double c12Body = Math.Abs(c2Close - c1Open);

                if (c12Body < MinC12Body || c12Body > MaxC12Body)
                {
                    return;
                }

                bool c1Bull = c1Close > c1Open;
                bool c2Bull = c2Close > c2Open;
                bool c1Bear = c1Close < c1Open;
                bool c2Bear = c2Close < c2Open;

                bool validBull = c1Bull && c2Bull;
                bool validBear = c1Bear && c2Bear;

                double longSL = 0;
                double shortSL = 0;

                if (First_Candle_High_Low)
                {
                    longSL = c1Low;
                    shortSL = c1High;
                }
                else if (First_Candle_Open)
                {
                    longSL = c1Open;
                    shortSL = c1Open;
                }
                else if (Second_Candle_High_Low)
                {
                    longSL = c2Low;
                    shortSL = c2High;
                }
                else if (Second_Candle_Open)
                {
                    longSL = c2Open;
                    shortSL = c2Open;
                }
                else
                {
                    // Fallback/default (optional)
                    longSL = c1Open;
                    shortSL = c1Open;
                }

                // Randomize % within DeviationPerc
                double randomizedOffsetPerc = OffsetPerc + (rng.NextDouble() * 2 - 1) * DeviationPerc;
                double randomizedTpPerc     = TpPerc     + (rng.NextDouble() * 2 - 1) * DeviationPerc;

                // Ensure no negative values
                randomizedOffsetPerc = Math.Max(0, randomizedOffsetPerc);
                randomizedTpPerc     = Math.Max(0, randomizedTpPerc);

                // Apply to calculations
                double offset = c12Body * (randomizedOffsetPerc / 100.0);
                double longEntry = c2Close - offset;
                double shortEntry = c2Close + offset;

                double longTP = longEntry + c12Body * (randomizedTpPerc / 100.0);
                double shortTP = shortEntry - c12Body * (randomizedTpPerc / 100.0);

                double longCancelPrice = longEntry + c12Body * (CancelPerc / 100.0);
                double shortCancelPrice = shortEntry - c12Body * (CancelPerc / 100.0);
                double longSLPoints = Math.Abs(longEntry - longSL) / TickSize;
                double shortSLPoints = Math.Abs(shortEntry - shortSL) / TickSize;

                if (debug) {
                    Print($"\n{Time[0]} - Candle body sizes:");
                    Print($"  ➤ c12Body = {c12Body:0.00} (Min allowed: {MinC12Body:0.00}, Max allowed: {MaxC12Body:0.00})");
                    Print($"  ➤ Entry Offset = {offset:0.00}");
                    Print($"  ➤ Long TP = {longTP:0.00}, Short TP = {shortTP:0.00}");
                }
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    if (debug)
                        Print($"{Time[0]} - Flat position, resetting orderPlaced flags.");
                    longOrderPlaced = false;
                    shortOrderPlaced = false;
                }

	                if (validBull && !longOrderPlaced)
	                {
	                    HandleReverseOnSignal(MarketPosition.Long);
	                    if (RequireEntryConfirmation)
	                    {
	                        if (!ShowEntryConfirmation("Long", longEntry, Contracts))
	                        {
                            if (debug)
                                Print($"User declined Long entry via confirmation dialog.");
                            return;
                        }
                    }

                    // Long RR check
                    if (MaxSLTPRatioPerc > 0)  // only enforce if enabled
                    {
                        double longTPPoints2 = Math.Abs(longTP - longEntry) / TickSize;
                        double longSLPoints2 = Math.Abs(longEntry - longSL) / TickSize;
                        if (longTPPoints2 > 0)
                        {
                            double ratioPerc = (longSLPoints2 / longTPPoints2) * 100.0;
                            if (ratioPerc > MaxSLTPRatioPerc)
                            {
                                if (debug)
                                    Print($"{Time[0]} - ❌ Skipping LONG trade. SL/TP ratio = {ratioPerc:0.00}% exceeds max {MaxSLTPRatioPerc}%");
                                return; // Skip this trade
                            }
                        }
                    }

                    //double paddedLongSL = longSL - SLPadding;
					double paddedLongSL = GetSLForLong();
                    double slInPoints = Math.Abs(longEntry - paddedLongSL);

                    if (MaxSLPoints > 0 && slInPoints > MaxSLPoints)
                    {
                        if (debug)
                            Print($"{Time[0]} - 🚫 Skipping long trade: SL = {slInPoints:0.00} points > MaxSL = {MaxSLPoints}");
                        return;
                    }

                    if (debug)
                        Print($"{Time[0]} - 📈 Valid long signal detected. Entry={longEntry:0.00}, SL={paddedLongSL:0.00}, TP={longTP:0.00}");

                    // Anti Hedge
                    string otherSymbol = GetOtherInstrument();
                    if (AntiHedge && (HasOppositePosition(otherSymbol, MarketPosition.Long) || HasOppositeOrder(otherSymbol, MarketPosition.Long)))
                    {
                        // Draw the 3 lines in black when anti-hedge prevents the trade
                        Draw.Line(this, "LongEntryLine_" + CurrentBar, false,
                            1, longEntry, 0, longEntry, Brushes.Black, DashStyleHelper.Solid, 2);

                        Draw.Line(this, "LongTPLine_" + CurrentBar, false,
                            1, longTP, 0, longTP, Brushes.Black, DashStyleHelper.Solid, 2);

                        Draw.Line(this, "LongSLLine_" + CurrentBar, false,
                            1, paddedLongSL, 0, paddedLongSL, Brushes.Black, DashStyleHelper.Solid, 2);
                        
                        Draw.Line(this, "LongCancelLine_" + CurrentBar, false, 1, longCancelPrice, 0, longCancelPrice, Brushes.Black, DashStyleHelper.Dot, 2);
                        
                        if (debug)
                            Print($"SKIP {Instrument.MasterInstrument.Name} LONG, {otherSymbol} is SHORT.");
                        return;
                    }

                    MarketPosition desiredDirection = MarketPosition.Long;
                    string instrument = Instrument.MasterInstrument.Name;
                    MarketPosition existing = GetHedgeLock(instrument);

                    if (AntiHedge)
                    {
                        bool conflict =
                            (desiredDirection == MarketPosition.Long && existing == MarketPosition.Short) ||
                            (desiredDirection == MarketPosition.Short && existing == MarketPosition.Long);

                        if (conflict)
                        {
                            Print($"🛑 AntiHedge active: {instrument} is already {existing}. Skipping {desiredDirection} entry.");
                            return;
                        }
                    }

						SetStopLoss("LongEntry", CalculationMode.Price, paddedLongSL, false);
	                    SetProfitTarget("LongEntry", CalculationMode.Price, longTP);
						EnterLongLimit(0, true, Contracts, longEntry, "LongEntry");
	                    QueueEntryWebhookLong(longEntry, longTP, paddedLongSL);

                    SetHedgeLock(instrument, desiredDirection); 

                    currentLongEntry = longEntry;
                    currentLongTP = longTP;
                    currentLongSL = paddedLongSL;
                    currentLongCancelPrice = longCancelPrice;
                    longSignalBar = CurrentBar;
                    longLineTagPrefix = $"LongLine_{++lineTagCounter}_{CurrentBar}_";
                    longLinesActive = true;
	                    longExitBar = -1;
	                    longOrderPlaced = true;
	                    shortOrderPlaced = false;
	                    UpdateInfo();
	                }

	                if (validBear && !shortOrderPlaced)
	                {
	                    HandleReverseOnSignal(MarketPosition.Short);
	                    if (RequireEntryConfirmation)
	                    {
	                        if (!ShowEntryConfirmation("Short", shortEntry, Contracts))
	                        {
                            if (debug)
                                Print($"User declined Long entry via confirmation dialog.");
                            return;
                        }
                    }

                    // Short RR check
                    if (MaxSLTPRatioPerc > 0)  // only enforce if enabled
                    {
                        double shortTPPoints2 = Math.Abs(shortEntry - shortTP) / TickSize;
                        double shortSLPoints2 = Math.Abs(shortSL - shortEntry) / TickSize;
                        if (shortTPPoints2 > 0)
                        {
                            double ratioPerc = (shortSLPoints2 / shortTPPoints2) * 100.0;
                            if (ratioPerc > MaxSLTPRatioPerc)
                            {
                                if (debug)
                                    Print($"{Time[0]} - ❌ Skipping SHORT trade. SL/TP ratio = {ratioPerc:0.00}% exceeds max {MaxSLTPRatioPerc}%");
                                return; // Skip this trade
                            }
                        }
                    }

                    //double paddedShortSL = shortSL + SLPadding;
					double paddedShortSL = GetSLForShort();
                    double slInPoints = Math.Abs(shortEntry - paddedShortSL);

                    if (MaxSLPoints > 0 && slInPoints > MaxSLPoints)
                    {
                        if (debug)
                            Print($"{Time[0]} - 🚫 Skipping short trade: SL = {slInPoints:0.00} points > MaxSL = {MaxSLPoints}");
                        return;
                    }
                    
                    if (debug)
                        Print($"{Time[0]} - 📉 Valid short signal detected. Entry={shortEntry:0.00}, SL={paddedShortSL:0.00}, TP={shortTP:0.00}");

                    // Anti Hedge
                    string otherSymbol = GetOtherInstrument();
                    if (AntiHedge && (HasOppositePosition(otherSymbol, MarketPosition.Short) || HasOppositeOrder(otherSymbol, MarketPosition.Short)))
                    {                        
                        // Draw the 3 lines in black when anti-hedge prevents the trade
                        Draw.Line(this, "ShortEntryLine_" + CurrentBar, false,
                            1, shortEntry, 0, shortEntry, Brushes.Black, DashStyleHelper.Solid, 2);

                        Draw.Line(this, "ShortTPLine_" + CurrentBar, false,
                            1, shortTP, 0, shortTP, Brushes.Black, DashStyleHelper.Solid, 2);

                        Draw.Line(this, "ShortSLLine_" + CurrentBar, false,
                            1, paddedShortSL, 0, paddedShortSL, Brushes.Black, DashStyleHelper.Solid, 2);
                        
                        Draw.Line(this, "ShortCancelLine_" + CurrentBar, false, 1, currentShortCancelPrice, 0, currentShortCancelPrice, Brushes.Black, DashStyleHelper.Dot, 2);
                        
                        if (debug)
                            Print($"SKIP {Instrument.MasterInstrument.Name} SHORT, {otherSymbol} is LONG.");
                        return;
                    }
                    
                    MarketPosition desiredDirection = MarketPosition.Short;
                    string instrument = Instrument.MasterInstrument.Name;
                    MarketPosition existing = GetHedgeLock(instrument);

                    if (AntiHedge)
                    {
                        bool conflict =
                            (desiredDirection == MarketPosition.Long && existing == MarketPosition.Short) ||
                            (desiredDirection == MarketPosition.Short && existing == MarketPosition.Long);

                        if (conflict)
                        {
                            Print($"🛑 AntiHedge active: {instrument} is already {existing}. Skipping {desiredDirection} entry.");
                            return;
                        }
                    }

	                    SetStopLoss("ShortEntry", CalculationMode.Price, paddedShortSL, false);
	                    SetProfitTarget("ShortEntry", CalculationMode.Price, shortTP);
						EnterShortLimit(0, true, Contracts, shortEntry, "ShortEntry");
	                    QueueEntryWebhookShort(shortEntry, shortTP, paddedShortSL);

                    SetHedgeLock(instrument, desiredDirection);

                    currentShortEntry = shortEntry;
                    currentShortTP = shortTP;
                    currentShortSL = paddedShortSL;
                    currentShortCancelPrice = shortCancelPrice;
                    shortSignalBar = CurrentBar;
                    shortLineTagPrefix = $"ShortLine_{++lineTagCounter}_{CurrentBar}_";
                    shortLinesActive = true;
	                    shortExitBar = -1;
	                    shortOrderPlaced = true;
	                    longOrderPlaced = false;
	                    UpdateInfo();
	                }
                lastBarProcessed = CurrentBar;

            }
        }

	        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
	                                            double averageFillPrice, OrderState orderState, DateTime time,
	                                            ErrorCode error, string comment)
	        {
	            if (order.Name == "LongEntry")
	                longEntryOrder = order;
	
	            if (order.Name == "ShortEntry")
	                shortEntryOrder = order;

	            if (order.Name == "LongEntry" && pendingLongWebhook)
	            {
	                string orderId = order != null ? (order.OrderId ?? string.Empty) : string.Empty;
	                bool isActive = orderState == OrderState.Accepted || orderState == OrderState.Working;
	                bool isNewOrderId = !string.Equals(lastLongWebhookOrderId, orderId, StringComparison.Ordinal);
	                bool payloadChanged =
	                    isNewOrderId ||
	                    !PricesEqual(lastLongWebhookEntry, pendingLongEntry) ||
	                    !PricesEqual(lastLongWebhookTP, pendingLongTP) ||
	                    !PricesEqual(lastLongWebhookSL, pendingLongSL);

	                if (isActive && payloadChanged)
	                {
                        if (!string.IsNullOrEmpty(lastLongWebhookOrderId))
                        {
                            if (debug)
                                Print($"{Time[0]} - 🔁 Webhook updating LONG working order: {lastLongWebhookEntry:0.00}/{lastLongWebhookTP:0.00}/{lastLongWebhookSL:0.00} -> {pendingLongEntry:0.00}/{pendingLongTP:0.00}/{pendingLongSL:0.00}");
                            SendWebhook("cancel");
                        }
                        else if (debug)
                        {
                            Print($"{Time[0]} - 📤 Webhook sending initial LONG order: {pendingLongEntry:0.00}/{pendingLongTP:0.00}/{pendingLongSL:0.00}");
                        }
	                    SendWebhook("buy", pendingLongEntry, pendingLongTP, pendingLongSL);
	                    pendingLongWebhook = false;
	                    lastLongWebhookOrderId = orderId;
                        lastLongWebhookEntry = pendingLongEntry;
                        lastLongWebhookTP = pendingLongTP;
                        lastLongWebhookSL = pendingLongSL;
	                }
	                else if (orderState == OrderState.Rejected || orderState == OrderState.Cancelled)
	                {
	                    pendingLongWebhook = false;
                        if (orderState == OrderState.Cancelled)
                        {
                            lastLongWebhookOrderId = null;
                            lastLongWebhookEntry = 0;
                            lastLongWebhookTP = 0;
                            lastLongWebhookSL = 0;
                        }
	                }
	            }

	            if (order.Name == "ShortEntry" && pendingShortWebhook)
	            {
	                string orderId = order != null ? (order.OrderId ?? string.Empty) : string.Empty;
	                bool isActive = orderState == OrderState.Accepted || orderState == OrderState.Working;
	                bool isNewOrderId = !string.Equals(lastShortWebhookOrderId, orderId, StringComparison.Ordinal);
	                bool payloadChanged =
	                    isNewOrderId ||
	                    !PricesEqual(lastShortWebhookEntry, pendingShortEntry) ||
	                    !PricesEqual(lastShortWebhookTP, pendingShortTP) ||
	                    !PricesEqual(lastShortWebhookSL, pendingShortSL);

	                if (isActive && payloadChanged)
	                {
                        if (!string.IsNullOrEmpty(lastShortWebhookOrderId))
                        {
                            if (debug)
                                Print($"{Time[0]} - 🔁 Webhook updating SHORT working order: {lastShortWebhookEntry:0.00}/{lastShortWebhookTP:0.00}/{lastShortWebhookSL:0.00} -> {pendingShortEntry:0.00}/{pendingShortTP:0.00}/{pendingShortSL:0.00}");
                            SendWebhook("cancel");
                        }
                        else if (debug)
                        {
                            Print($"{Time[0]} - 📤 Webhook sending initial SHORT order: {pendingShortEntry:0.00}/{pendingShortTP:0.00}/{pendingShortSL:0.00}");
                        }
	                    SendWebhook("sell", pendingShortEntry, pendingShortTP, pendingShortSL);
	                    pendingShortWebhook = false;
	                    lastShortWebhookOrderId = orderId;
                        lastShortWebhookEntry = pendingShortEntry;
                        lastShortWebhookTP = pendingShortTP;
                        lastShortWebhookSL = pendingShortSL;
	                }
	                else if (orderState == OrderState.Rejected || orderState == OrderState.Cancelled)
	                {
	                    pendingShortWebhook = false;
                        if (orderState == OrderState.Cancelled)
                        {
                            lastShortWebhookOrderId = null;
                            lastShortWebhookEntry = 0;
                            lastShortWebhookTP = 0;
                            lastShortWebhookSL = 0;
                        }
	                }
	            }
	
	            // 🧠 Only reset on cancellations (not fills)
	            if (order.OrderState == OrderState.Cancelled)
	            {
                bool allOrdersInactive =
                    (longEntryOrder == null || longEntryOrder.OrderState == OrderState.Cancelled || longEntryOrder.OrderState == OrderState.Filled) &&
                    (shortEntryOrder == null || shortEntryOrder.OrderState == OrderState.Cancelled || shortEntryOrder.OrderState == OrderState.Filled);

                // Only reset if we have no open position AND no active orders
                if (allOrdersInactive && Position.MarketPosition == MarketPosition.Flat)
                {
                    if (debug)
                        Print($"{Time[0]} - 🔁 Order canceled and flat — resetting info state");
                    longEntryOrder = null;
                    shortEntryOrder = null;
                    longOrderPlaced = false;
                    shortOrderPlaced = false;
                    currentLongEntry = currentShortEntry = 0;
                    currentLongTP = currentShortTP = 0;
                    currentLongSL = currentShortSL = 0;
                    currentLongCancelPrice = currentShortCancelPrice = 0;

                    UpdateInfo();
                }
            }
            else
            {
                // For all other order updates (Submitted, Working, Filled), just refresh info
                UpdateInfo();
            }
        }

        private bool PricesEqual(double a, double b)
        {
            double tolerance = TickSize > 0 ? TickSize * 0.5 : 1e-10;
            return Math.Abs(a - b) <= tolerance;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
	            MarketPosition marketPosition, string orderId, DateTime time)
	        {
	            var smallFont = new SimpleFont("Arial", 8) { Bold = true };

	            // ✅ Track TP fills
	            if (execution.Order != null && execution.Order.Name == "Profit target"
	                && execution.Order.OrderState == OrderState.Filled)
	            {
	                double entryPrice = execution.Order.FromEntrySignal == "LongEntry"
	                    ? currentLongEntry
	                    : currentShortEntry;

                double points = Math.Abs(price - entryPrice) / TickSize;
                double pointsInPrice = points * TickSize;

                double yOffset = (execution.Order.FromEntrySignal == "LongEntry")
                    ? price + (2 * TickSize)
                    : price - (2 * TickSize);

                string tag = "TPText_" + CurrentBar + "_" + execution.Order.FromEntrySignal;
                Draw.Text(this, tag, true,
                    $"+{pointsInPrice:0.00} points",
                    0, yOffset, 0,
                    Brushes.Green,
                    new SimpleFont("Arial", 15),
                    TextAlignment.Center,
                    null, null, 0);
            }

            // ✅ Track SL fills
	            if (execution.Order != null && execution.Order.Name == "Stop loss"
	                && execution.Order.OrderState == OrderState.Filled)
	            {
	                double entryPrice = execution.Order.FromEntrySignal == "LongEntry"
	                    ? currentLongEntry
	                    : currentShortEntry;

                double points = Math.Abs(price - entryPrice) / TickSize;
                double pointsInPrice = points * TickSize;

                double yOffset = (execution.Order.FromEntrySignal == "LongEntry")
                    ? price - (2 * TickSize)   // place text below stop for long
                    : price + (2 * TickSize);  // place text above stop for short

                string tag = "SLText_" + CurrentBar + "_" + execution.Order.FromEntrySignal;
                Draw.Text(this, tag, true,
                    $"-{pointsInPrice:0.00} points",
                    0, yOffset, 0,
                    Brushes.Red,
                    new SimpleFont("Arial", 15),
                    TextAlignment.Center,
                    null, null, 0);
            }

            if (execution.Order != null &&
                execution.Order.FromEntrySignal == "LongEntry" &&
                (execution.Order.Name == "Profit target" || execution.Order.Name == "Stop loss") &&
                execution.Order.OrderState == OrderState.Filled)
            {
                int barIndex = Bars.GetBar(time);
                longExitBar = barIndex >= 0 ? barIndex : CurrentBar;
            }

            if (execution.Order != null &&
                execution.Order.FromEntrySignal == "ShortEntry" &&
                (execution.Order.Name == "Profit target" || execution.Order.Name == "Stop loss") &&
                execution.Order.OrderState == OrderState.Filled)
            {
                int barIndex = Bars.GetBar(time);
                shortExitBar = barIndex >= 0 ? barIndex : CurrentBar;
            }

            // ✅ Reset only after final exit (TP/SL/flatten) when position is truly flat
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                bool hasWorkingOrders =
                    (longEntryOrder != null && longEntryOrder.OrderState == OrderState.Working) ||
                    (shortEntryOrder != null && shortEntryOrder.OrderState == OrderState.Working);

                bool isExitExecution = execution.Order != null &&
                    (execution.Order.Name == "Profit target" ||
                    execution.Order.Name == "Stop loss" ||
                    execution.Order.Name.StartsWith("Exit_"));

                // 🧠 Reset only if this execution was an exit, not an entry
                if (isExitExecution && !hasWorkingOrders)
                {
                    if (debug)
                        Print($"{Time[0]} - ✅ Position closed, resetting info state");
                    longEntryOrder = null;
                    shortEntryOrder = null;
                    longOrderPlaced = false;
                    shortOrderPlaced = false;

                    UpdateInfo();
                }
                ClearHedgeLock(Instrument.MasterInstrument.Name);
	            }
	        }

	        private void UpdatePreviewLines()
	        {
	            if (longLinesActive && longSignalBar >= 0)
	            {
                int startBarsAgo = CurrentBar - longSignalBar;
                int endBarsAgo = longExitBar >= 0 ? Math.Max(0, CurrentBar - longExitBar) : 0;

                Draw.Line(this, longLineTagPrefix + "LongEntryLineActive", false,
                    startBarsAgo, currentLongEntry,
                    endBarsAgo, currentLongEntry,
                    Brushes.Gold, DashStyleHelper.Solid, 2);

                Draw.Line(this, longLineTagPrefix + "LongTPLineActive", false,
                    startBarsAgo, currentLongTP,
                    endBarsAgo, currentLongTP,
                    Brushes.LimeGreen, DashStyleHelper.Solid, 2);

                Draw.Line(this, longLineTagPrefix + "LongSLLineActive", false,
                    startBarsAgo, currentLongSL,
                    endBarsAgo, currentLongSL,
                    Brushes.Red, DashStyleHelper.Solid, 2);

                Draw.Line(this, longLineTagPrefix + "LongCancelLineActive", false,
                    startBarsAgo, currentLongCancelPrice,
                    endBarsAgo, currentLongCancelPrice,
                    Brushes.Gray, DashStyleHelper.Dot, 2);
            }

            if (shortLinesActive && shortSignalBar >= 0)
            {
                int startBarsAgo = CurrentBar - shortSignalBar;
                int endBarsAgo = shortExitBar >= 0 ? Math.Max(0, CurrentBar - shortExitBar) : 0;

                Draw.Line(this, shortLineTagPrefix + "ShortEntryLineActive", false,
                    startBarsAgo, currentShortEntry,
                    endBarsAgo, currentShortEntry,
                    Brushes.Gold, DashStyleHelper.Solid, 2);

                Draw.Line(this, shortLineTagPrefix + "ShortTPLineActive", false,
                    startBarsAgo, currentShortTP,
                    endBarsAgo, currentShortTP,
                    Brushes.LimeGreen, DashStyleHelper.Solid, 2);

                Draw.Line(this, shortLineTagPrefix + "ShortSLLineActive", false,
                    startBarsAgo, currentShortSL,
                    endBarsAgo, currentShortSL,
                    Brushes.Red, DashStyleHelper.Solid, 2);

                Draw.Line(this, shortLineTagPrefix + "ShortCancelLineActive", false,
                    startBarsAgo, currentShortCancelPrice,
                    endBarsAgo, currentShortCancelPrice,
                    Brushes.Gray, DashStyleHelper.Dot, 2);
            }
        }

		private void CancelEntryIfAfterNoTrades()
		{
			if (shortEntryOrder != null)
			{
                if (debug)
				    Print($"{Time[0]} - ⏰ Canceling SHORT entry due to NoTradesAfter");
					CancelOrder(shortEntryOrder);
	                SendWebhookCancelSafe();
				}
	
				if (longEntryOrder != null)
				{
	                if (debug)
					    Print($"{Time[0]} - ⏰ Canceling LONG entry due to NoTradesAfter");
					CancelOrder(longEntryOrder);
	                SendWebhookCancelSafe();
				}

			longOrderPlaced = false;
			shortOrderPlaced = false;
			longEntryOrder = null;
			shortEntryOrder = null;
		}

        private void HandleReverseOnSignal(MarketPosition desiredDirection)
        {
            if (!ReverseOnSignal)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (!reverseFlattensLivePosition)
                    return;

                if (desiredDirection == MarketPosition.Long && Position.MarketPosition == MarketPosition.Short)
                {
                    if (debug)
                        Print($"{Time[0]} - 🔁 Reverse signal while SHORT position active. Exiting SHORT.");
                    Flatten("ReverseSignal");
                }
                else if (desiredDirection == MarketPosition.Short && Position.MarketPosition == MarketPosition.Long)
                {
                    if (debug)
                        Print($"{Time[0]} - 🔁 Reverse signal while LONG position active. Exiting LONG.");
                    Flatten("ReverseSignal");
                }

                return;
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                if (desiredDirection == MarketPosition.Long && IsOrderActive(shortEntryOrder))
                {
                    if (debug)
                        Print($"{Time[0]} - 🔁 Reverse signal while SHORT order working. Canceling SHORT entry.");
                    CancelOrder(shortEntryOrder);
                    shortEntryOrder = null;
                    shortOrderPlaced = false;
                }
                else if (desiredDirection == MarketPosition.Short && IsOrderActive(longEntryOrder))
                {
                    if (debug)
                        Print($"{Time[0]} - 🔁 Reverse signal while LONG order working. Canceling LONG entry.");
                    CancelOrder(longEntryOrder);
                    longEntryOrder = null;
                    longOrderPlaced = false;
                }
            }
        }

        private bool IsOrderActive(Order order)
        {
            return order != null &&
                (order.OrderState == OrderState.Working ||
                order.OrderState == OrderState.Submitted ||
                order.OrderState == OrderState.Accepted ||
                order.OrderState == OrderState.ChangePending);
        }

	        private void Flatten(string reason)
	        {
	            if (Position.MarketPosition == MarketPosition.Long)
	            {
	                if (debug)
	                    Print($"{Time[0]} - Flattening LONG due to {reason}");
	                ExitLong("Exit_" + reason, "LongEntry");
	                
	                // ✅ Send EXIT webhook for longs
	                SendWebhookExitSafe();
	            }
	            else if (Position.MarketPosition == MarketPosition.Short)
	            {
	                if (debug)
	                    Print($"{Time[0]} - Flattening SHORT due to {reason}");
	                ExitShort("Exit_" + reason, "ShortEntry");
	                
	                // ✅ Send EXIT webhook for shorts
	                SendWebhookExitSafe();
	            }

            longLinesActive = false;
            shortLinesActive = false;
            longSignalBar = -1;
            shortSignalBar = -1;
            longExitBar = -1;
            shortExitBar = -1;
        }
	
        private bool TimeInSkip(DateTime time)
        {
			EnsureEffectiveTimes(time, false);
            TimeSpan now = time.TimeOfDay;

            bool inSkip1 = false;
            bool inSkip2 = false;

            // ✅ Skip1 only active if both are not 00:00:00
            if (effectiveSkipStart != TimeSpan.Zero && effectiveSkipEnd != TimeSpan.Zero)
            {
                inSkip1 = (effectiveSkipStart < effectiveSkipEnd)
                    ? (now >= effectiveSkipStart && now <= effectiveSkipEnd)
                    : (now >= effectiveSkipStart || now <= effectiveSkipEnd); // overnight handling
            }

            // ✅ Skip2 only active if both are not 00:00:00
            if (effectiveSkip2Start != TimeSpan.Zero && effectiveSkip2End != TimeSpan.Zero)
            {
                inSkip2 = (effectiveSkip2Start < effectiveSkip2End)
                    ? (now >= effectiveSkip2Start && now <= effectiveSkip2End)
                    : (now >= effectiveSkip2Start || now <= effectiveSkip2End); // overnight handling
            }

            return inSkip1 || inSkip2;
        }

		private bool TimeInSession(DateTime time)
		{
			EnsureEffectiveTimes(time, false);
			if (!IsTradingEnabledForPreset(activePreset))
				return false;
		    TimeSpan now = time.TimeOfDay;
		
		    if (effectiveSessionStart < effectiveSessionEnd)
		        return now >= effectiveSessionStart && now < effectiveSessionEnd;   // strictly less
		    else
		        return now >= effectiveSessionStart || now < effectiveSessionEnd;
		}

		private bool IsTradingEnabledForPreset(StrategyPreset preset)
		{
			switch (preset)
			{
				case StrategyPreset.Asia:
					return TradeAsiaSession;
				case StrategyPreset.London:
					return TradeLondonSession;
				default:
					return TradeNewYorkSession;
			}
		}

		private bool TimeInNoTradesAfter(DateTime time)
		{
			EnsureEffectiveTimes(time, false);
			TimeSpan now = time.TimeOfDay;

			// If the session doesn’t cross midnight
			if (effectiveSessionStart < effectiveSessionEnd)
				return now >= effectiveNoTradesAfter && now < effectiveSessionEnd;

			// If the session crosses midnight
			return (now >= effectiveNoTradesAfter || now < effectiveSessionEnd);
		}

        private void DrawSessionBackground()
        {
            // Don't try drawing until we have a few bars
            if (CurrentBar < 1)
                return;

            // Find current bar time
            DateTime barTime = Time[0];
			EnsureEffectiveTimes(barTime, false);
            bool isOvernight = effectiveSessionStart > effectiveSessionEnd;

            DateTime sessionStartTime = barTime.Date + effectiveSessionStart;
            DateTime sessionEndTime = isOvernight
                ? sessionStartTime.AddDays(1).Date + effectiveSessionEnd
                : sessionStartTime.Date + effectiveSessionEnd;

            // Get the bar indexes (barsAgo) for start and end
            int startBarsAgo = Bars.GetBar(sessionStartTime);
            int endBarsAgo = Bars.GetBar(sessionEndTime);

            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            string tag = $"DUO_SessionFill_{activePreset}_{sessionStartTime:yyyyMMdd_HHmm}";

            if (DrawObjects[tag] == null)
            {
				// ✅ Use very high/low Y values to simulate full chart height
				Draw.Rectangle(
					this,
					tag,
					false,
					sessionStartTime,
					0,
					sessionEndTime,
					30000,
					Brushes.Transparent,
					SessionBrush ?? Brushes.DarkSlateGray,
					10
				).ZOrder = -1;
            }
			
			var r = new SolidColorBrush(Color.FromArgb(70, 255, 0, 0));

			// Calculate the exact DateTime for the NoTradesAfter time (on this chart date)
			//DateTime sessionStartTime = Time[0].Date + SessionStart;
			DateTime noTradesAfterTime = Time[0].Date + effectiveNoTradesAfter;

			// Handle overnight sessions (when SessionEnd < SessionStart)
			if (effectiveSessionStart > effectiveSessionEnd && effectiveNoTradesAfter < effectiveSessionStart)
				noTradesAfterTime = noTradesAfterTime.AddDays(1);

			// Draw the vertical line exactly at NoTradesAfter
			Draw.VerticalLine(this, $"NoTradesAfter_{activePreset}_{sessionStartTime:yyyyMMdd_HHmm}", noTradesAfterTime, r,
							DashStyleHelper.Solid, 2);

            DrawSkipWindow("Skip1", effectiveSkipStart, effectiveSkipEnd);
            DrawSkipWindow("Skip2", effectiveSkip2Start, effectiveSkip2End);
        }

        private void DrawSkipWindow(string tagPrefix, TimeSpan start, TimeSpan end)
        {
            // Only draw when both ends are configured
            if (start == TimeSpan.Zero || end == TimeSpan.Zero)
                return;

            DateTime barDate = Time[0].Date;

            DateTime windowStart = barDate + start;
            DateTime windowEnd = barDate + end;

            if (start > end)
                windowEnd = windowEnd.AddDays(1); // overnight skip window support

            int startBarsAgo = Bars.GetBar(windowStart);
            int endBarsAgo = Bars.GetBar(windowEnd);

            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            // Higher opacity fill, light dash-dot outlines
            var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));   // fill (~78% opaque)
            areaBrush.Freeze();
            var lineBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));    // lines (~35% opaque)
            lineBrush.Freeze();

            string rectTag = $"DUO_{tagPrefix}_Rect_{windowStart:yyyyMMdd}";
            Draw.Rectangle(
                this,
                rectTag,
                false,
                windowStart,
                0,
                windowEnd,
                30000,
                lineBrush,   // outline
                areaBrush,   // fill
                2
            ).ZOrder = -1;

            string startTag = $"DUO_{tagPrefix}_Start_{windowStart:yyyyMMdd}";
            Draw.VerticalLine(this, startTag, windowStart, lineBrush, DashStyleHelper.Solid, 2);

            string endTag = $"DUO_{tagPrefix}_End_{windowEnd:yyyyMMdd}";
            Draw.VerticalLine(this, endTag, windowEnd, lineBrush, DashStyleHelper.Solid, 2);
        }

		private void EnsureEffectiveTimes(DateTime barTime, bool log)
		{
			if (!londonAutoShiftTimes)
			{
				effectiveSessionStart = SessionStart;
				effectiveSessionEnd = SessionEnd;
				effectiveNoTradesAfter = NoTradesAfter;
				effectiveSkipStart = SkipStart;
				effectiveSkipEnd = SkipEnd;
				effectiveSkip2Start = Skip2Start;
				effectiveSkip2End = Skip2End;
				return;
			}

			DateTime date = barTime.Date;
			if (date == effectiveTimesDate)
				return;

			effectiveTimesDate = date;

			TimeSpan shift = GetLondonSessionShiftForDate(date);
			effectiveSessionStart = ShiftTime(SessionStart, shift);
			effectiveSessionEnd = ShiftTime(SessionEnd, shift);
			effectiveNoTradesAfter = ShiftTime(NoTradesAfter, shift);
			effectiveSkipStart = ShiftTime(SkipStart, shift);
			effectiveSkipEnd = ShiftTime(SkipEnd, shift);
			effectiveSkip2Start = ShiftTime(Skip2Start, shift);
			effectiveSkip2End = ShiftTime(Skip2End, shift);

			if (debug && log)
			{
				Print(
					$"{date:yyyy-MM-dd} - LondonAutoShiftTimes recompute | " +
					$"Base SS={SessionStart:hh\\:mm} SE={SessionEnd:hh\\:mm} NTA={NoTradesAfter:hh\\:mm} | " +
					$"Eff SS={effectiveSessionStart:hh\\:mm} SE={effectiveSessionEnd:hh\\:mm} NTA={effectiveNoTradesAfter:hh\\:mm} | " +
					$"Shift={shift.TotalHours:0.##}h");
			}
		}

		private TimeSpan GetLondonSessionShiftForDate(DateTime date)
		{
			// Use midday UTC to avoid DST transition hour edge cases.
			DateTime utcSample = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);

			// Compute a dynamic baseline for this year so we only shift during UK/US DST mismatch weeks.
			// Jan 15 is a stable reference day (both typically on standard time).
			DateTime utcRef = new DateTime(date.Year, 1, 15, 12, 0, 0, DateTimeKind.Utc);

			TimeZoneInfo londonTz = GetLondonTimeZone();
			TimeZoneInfo targetTz = GetTargetTimeZone();

			TimeSpan baseline = londonTz.GetUtcOffset(utcRef) - targetTz.GetUtcOffset(utcRef);
			TimeSpan actual = londonTz.GetUtcOffset(utcSample) - targetTz.GetUtcOffset(utcSample);

			return baseline - actual;
		}

		private TimeSpan ShiftTime(TimeSpan baseTime, TimeSpan shift)
		{
			long ticks = (baseTime.Ticks + shift.Ticks) % TimeSpan.TicksPerDay;
			if (ticks < 0)
				ticks += TimeSpan.TicksPerDay;
			return new TimeSpan(ticks);
		}

		private TimeZoneInfo GetTargetTimeZone()
		{
			if (targetTimeZone != null)
				return targetTimeZone;

			try
			{
				// Prefer the Bars/data-series time zone (matches Time[0]) if available.
				// Use reflection to avoid compile-time dependency on specific NinjaTrader members.
				var bars = Bars;
				if (bars != null)
				{
					var timeZoneProp = bars.GetType().GetProperty(
						"TimeZoneInfo",
						BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

					if (timeZoneProp != null && typeof(TimeZoneInfo).IsAssignableFrom(timeZoneProp.PropertyType))
						targetTimeZone = (TimeZoneInfo)timeZoneProp.GetValue(bars, null);
				}

				// Fallback to TradingHours template time zone.
				if (targetTimeZone == null)
					targetTimeZone = Bars?.TradingHours?.TimeZoneInfo;
			}
			catch
			{
				targetTimeZone = null;
			}

			if (targetTimeZone == null)
				targetTimeZone = TimeZoneInfo.Local;

			return targetTimeZone;
		}

		private TimeZoneInfo GetLondonTimeZone()
		{
			if (londonTimeZone != null)
				return londonTimeZone;

			try
			{
				// Windows time zone id (NinjaTrader runs on Windows).
				londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
			}
			catch
			{
				try
				{
					// Fallback for environments that support IANA ids.
					londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
				}
				catch
				{
					londonTimeZone = TimeZoneInfo.Utc;
				}
			}

			return londonTimeZone;
		}

        private double GetSLForLong()
        {
            if (SLPresetSetting == SLPreset.First_Candle_Percent)
            {
                double c1High = High[1];
                double c1Low = Low[1];
                double sl = c1High - (SLPercentFirstCandle / 100.0) * (c1High - c1Low);

                if (debug)
                    Print($"{Time[0]} - 📉 Long SL set at {SLPercentFirstCandle}% of Candle 1 range: {sl:0.00}");

                return sl - SLPadding;
            }

            double baseSL = First_Candle_High_Low ? Low[1] :
                            First_Candle_Open ? Open[1] :
                            Second_Candle_High_Low ? Low[0] :
                            Second_Candle_Open ? Open[0] :
                            Open[1];
            return baseSL - SLPadding;
        }

        private double GetSLForShort()
        {
            if (SLPresetSetting == SLPreset.First_Candle_Percent)
            {
                double c1High = High[1];
                double c1Low = Low[1];
                double sl = c1Low + (SLPercentFirstCandle / 100.0) * (c1High - c1Low);

                if (debug)
                    Print($"{Time[0]} - 📈 Short SL set at {SLPercentFirstCandle}% of Candle 1 range: {sl:0.00}");

                return sl + SLPadding;
            }

            double baseSL = First_Candle_High_Low ? High[1] :
                            First_Candle_Open ? Open[1] :
                            Second_Candle_High_Low ? High[0] :
                            Second_Candle_Open ? Open[0] :
                            Open[1];
            return baseSL + SLPadding;
        }
		
        private bool ShowEntryConfirmation(string orderType, double price, int quantity)
        {
            bool result = false;
            if (System.Windows.Application.Current == null) // Defensive: avoid crash in some headless NT environments
                return false;

            System.Windows.Application.Current.Dispatcher.Invoke(
                () =>
                {
                    var message = $"Confirm {orderType} entry\nPrice: {price}\nQty: {quantity}";
                    var res =
                        System.Windows.MessageBox.Show(message, "Entry Confirmation", System.Windows.MessageBoxButton.YesNo,
                                                    System.Windows.MessageBoxImage.Question);

                    result = (res == System.Windows.MessageBoxResult.Yes);
                });

            return result;
        }

        public void UpdateInfo() {
            UpdateInfoText();
        }

        public void UpdateInfoText()
        {
            var lines = BuildInfoLines();
            var font  = new SimpleFont("Consolas", 14); // monospaced

            int maxLabel = lines.Max(l => l.label.Length);
            int maxValue = Math.Max(1, lines.Max(l => l.value.Length));

            // 1) BACKGROUND BLOCK – uses visible chars but transparent text so
            //    NinjaTrader allocates full width (labels + values).
            string valuePlaceholder = new string('0', maxValue); // dummy width
            var bgLines = lines
                .Select(l => l.label.PadRight(maxLabel + 1) + valuePlaceholder)
                .ToArray();

            string bgText = string.Join(Environment.NewLine, bgLines);

            Draw.TextFixed(
                owner: this,
                tag: "myStatusLabel_bg",
                text: bgText,
                textPosition: TextPosition.BottomLeft,
                textBrush: Brushes.Transparent,  // text invisible
                font: font,
                outlineBrush: null,
                areaBrush: Brushes.Black,        // full background for whole block
                areaOpacity: 85);

            // 2) LABEL BLOCK – labels only, no background
            var labelLines = lines
                .Select(l => l.label)
                .ToArray();

            string labelText = string.Join(Environment.NewLine, labelLines);

            Draw.TextFixed(
                owner: this,
                tag: "myStatusLabel_labels",
                text: labelText,
                textPosition: TextPosition.BottomLeft,
                textBrush: Brushes.LightGray,
                font: font,
                outlineBrush: null,
                areaBrush: null,
                areaOpacity: 0);

            // 3) VALUE OVERLAYS – one block per line, only that line has a value
            string spacesBeforeValue = new string(' ', maxLabel + 1);

            for (int i = 0; i < lines.Count; i++)
            {
                string tag = $"myStatusLabel_val_{i}";

                var overlayLines = new string[lines.Count];
                for (int j = 0; j < lines.Count; j++)
                {
                    overlayLines[j] = (j == i)
                        ? spacesBeforeValue + lines[i].value   // value at value column
                        : string.Empty;                        // blank line
                }

                string overlayText = string.Join(Environment.NewLine, overlayLines);

                Draw.TextFixed(
                    owner: this,
                    tag: tag,
                    text: overlayText,
                    textPosition: TextPosition.BottomLeft,
                    textBrush: lines[i].brush,
                    font: font,
                    outlineBrush: null,
                    areaBrush: null,
                    areaOpacity: 0);
            }
        }

        private List<(string label, string value, Brush brush)> BuildInfoLines()
        {
            var lines = new List<(string label, string value, Brush brush)>();

            string tpLine, slLine;
            GetPnLLines(out tpLine, out slLine);

            lines.Add(("TP:        ", $"{tpLine}", Brushes.LimeGreen));
            lines.Add(("SL:        ", $"{slLine}", Brushes.IndianRed));
            lines.Add(("Contracts: ", $"{Contracts}", Brushes.LightGray));
            lines.Add(("Anti Hedge:", AntiHedge ? "✅" : "⛔", AntiHedge ? Brushes.LimeGreen : Brushes.IndianRed));
            lines.Add((FormatPresetLabel(activePreset), string.Empty, Brushes.LightGray));

            var version = $"v{GetAddOnVersion()}";
            lines.Add(($"{version}", string.Empty, Brushes.LightGray));

            return lines;
        }

        private void GetPnLLines(out string tpLine, out string slLine)
        {
            // --- Detect state ---
            bool hasPosition = Position.MarketPosition != MarketPosition.Flat;

            bool hasLongOrder =
                longEntryOrder != null &&
                (longEntryOrder.OrderState == OrderState.Working ||
                longEntryOrder.OrderState == OrderState.Submitted ||
                longEntryOrder.OrderState == OrderState.Accepted ||
                longEntryOrder.OrderState == OrderState.ChangePending);

            bool hasShortOrder =
                shortEntryOrder != null &&
                (shortEntryOrder.OrderState == OrderState.Working ||
                shortEntryOrder.OrderState == OrderState.Submitted ||
                shortEntryOrder.OrderState == OrderState.Accepted ||
                shortEntryOrder.OrderState == OrderState.ChangePending);

            // --- New logic: also consider our own flags ---
            bool hasPendingLong  = longOrderPlaced && !hasPosition;
            bool hasPendingShort = shortOrderPlaced && !hasPosition;

            // Show $0 only if no position and nothing pending
            if (!hasPosition && !hasLongOrder && !hasShortOrder && !hasPendingLong && !hasPendingShort)
            {
                tpLine = "$0";
                slLine = "$0";
                return;
            }

            // --- Tick value per instrument ---
            double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
            if (Instrument.MasterInstrument.Name == "MNQ") tickValue = 0.50;
            else if (Instrument.MasterInstrument.Name == "NQ") tickValue = 5.00;

            double entry = 0, tp = 0, sl = 0;

            // --- Active position ---
            if (hasPosition)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    entry = currentLongEntry;
                    tp = currentLongTP;
                    sl = currentLongSL;
                }
                else
                {
                    entry = currentShortEntry;
                    tp = currentShortTP;
                    sl = currentShortSL;
                }
            }
            // --- Pending long order ---
            else if (hasLongOrder || hasPendingLong)
            {
                entry = currentLongEntry;
                tp = currentLongTP;
                sl = currentLongSL;
            }
            // --- Pending short order ---
            else if (hasShortOrder || hasPendingShort)
            {
                entry = currentShortEntry;
                tp = currentShortTP;
                sl = currentShortSL;
            }

            // Guard: if anything not set, show zero
            if (entry == 0 || tp == 0 || sl == 0)
            {
                tpLine = "$0";
                slLine = "$0";
                return;
            }

            // --- Compute TP/SL distances ---
            double tpTicks = Math.Abs(tp - entry) / TickSize;
            double slTicks = Math.Abs(entry - sl) / TickSize;

            double tpDollars = tpTicks * tickValue * Contracts;
            double slDollars = slTicks * tickValue * Contracts;

            tpLine = $"${tpDollars:0}";
            slLine = $"${slDollars:0}";
        }

        string GetAddOnVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Version version = assembly.GetName().Version;
            return version.ToString();
        }

        private static readonly Dictionary<string, string> CrossPairs = new Dictionary<string, string> {
            { "MNQ", "MES" },
            { "MES", "MNQ" },
            { "NQ", "ES" },
            { "ES", "NQ" },
        };

        private string GetOtherInstrument()
        {
            string thisSymbol = Instrument.MasterInstrument.Name.ToUpper();

            if (CrossPairs.ContainsKey(thisSymbol))
                return CrossPairs[thisSymbol];

            throw new Exception("Strategy not on supported instrument!");
        }

        private bool HasOppositeOrder(string targetInstrument, MarketPosition desiredDirection)
        {
            foreach (var order in Account.Orders)
            {
                if (order.Instrument.MasterInstrument.Name.Equals(targetInstrument, StringComparison.OrdinalIgnoreCase) &&
                    (order.OrderState == OrderState.Working || order.OrderState == OrderState.Accepted))
                {
                    if ((desiredDirection == MarketPosition.Long && order.OrderAction == OrderAction.SellShort) ||
                        (desiredDirection == MarketPosition.Short && order.OrderAction == OrderAction.Buy))
                    {
                        if (debug)
                            Print("Has Opposite Order");
                        return true;
                    }
                }
            }
            if (debug)
                Print("Has No Opposite Order");
            return false;
        }

        private bool HasOppositePosition(string targetInstrument, MarketPosition desiredDirection)
        {
            foreach (var pos in Account.Positions)
            {
                if (pos.Instrument.MasterInstrument.Name.Equals(targetInstrument, StringComparison.OrdinalIgnoreCase))
                {
                    if ((desiredDirection == MarketPosition.Long && pos.MarketPosition == MarketPosition.Short &&
                        pos.Quantity > 0) ||
                        (desiredDirection == MarketPosition.Short && pos.MarketPosition == MarketPosition.Long &&
                        pos.Quantity > 0))
                    {
                        if (debug)
                            Print("Has Opposite Position");
                        return true;
                    }
                }
            }
            if (debug)
                Print("Has No Opposite Position");
            return false;
        }

        private string BuildHeartbeatId()
        {
            string baseName = Name ?? GetType().Name;
            string instrumentName = Instrument != null ? Instrument.FullName : "UnknownInstrument";
            string accountName = Account != null ? Account.Name : "UnknownAccount";
            string barsInfo = BarsPeriod != null
                ? $"{BarsPeriod.BarsPeriodType}-{BarsPeriod.Value}"
                : "NoBars";

            string configKey = $"{Contracts}-{OffsetPerc}-{TpPerc}-{CancelPerc}-{DeviationPerc}-{SLPadding}-{SLPresetSetting}";

            string raw = $"{baseName}-{instrumentName}-{barsInfo}-{accountName}-{configKey}";
            return raw.Replace(",", "_").Replace(Environment.NewLine, " ").Trim();
        }

        private void WriteHeartbeat()
        {
            try
            {
                string name = heartbeatId ?? this.Name ?? GetType().Name;
                string line = $"{name},{DateTime.Now:O}";
                List<string> lines = new List<string>();

                bool success = false;
                lock (heartbeatFileLock)
                {
                    // --- Load existing lines (if any) ---
                    if (System.IO.File.Exists(heartbeatFile))
                    {
                        for (int i = 0; i < 3; i++) // retry on read conflict
                        {
                            try
                            {
                                lines.AddRange(System.IO.File.ReadAllLines(heartbeatFile));
                                break;
                            }
                            catch (IOException)
                            {
                                System.Threading.Thread.Sleep(100);
                            }
                        }
                    }

                    // --- Update or add this strategy’s line ---
                    bool updated = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].StartsWith(name + ",", StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = line;
                            updated = true;
                            break;
                        }
                    }
                    if (!updated)
                        lines.Add(line);

                    // --- Write back with retry ---
                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            System.IO.File.WriteAllLines(heartbeatFile, lines.ToArray());
                            success = true;
                            break;
                        }
                        catch (IOException)
                        {
                            System.Threading.Thread.Sleep(100);
                        }
                    }
                }

                //if (!success)
                //    Print($"⚠️ Failed to write heartbeat after 3 attempts — file still in use: {heartbeatFile}");
                //else
                //    Print($"💓 Heartbeat written for {name} at {DateTime.UtcNow:HH:mm:ss}");
            }
            catch (Exception ex)
            {
                if (debug)
                    Print($"⚠️ Heartbeat write error: {ex.Message}");
            }
        }

        private void PrintInstrumentPreset(StrategyPreset preset)
		{
			Print($"\n== INSTRUMENT PRESET APPLIED: {preset} ==");

			Print($"  ➤ MinC12Body: {MinC12Body}");
			Print($"  ➤ MaxC12Body: {MaxC12Body}");
			Print($"  ➤ OffsetPerc: {OffsetPerc}");
			Print($"  ➤ TpPerc: {TpPerc}");
			Print($"  ➤ CancelPerc: {CancelPerc}");
			Print($"  ➤ DeviationPerc: {DeviationPerc}");
			Print($"  ➤ SLPadding: {SLPadding}");
			Print($"  ➤ MaxSLTPRatioPerc: {MaxSLTPRatioPerc}");
			Print($"  ➤ SLPresetSetting: {SLPresetSetting}");
			Print($"  ➤ SLPercentFirstCandle: {SLPercentFirstCandle}");
			Print($"  ➤ MaxSLPoints: {MaxSLPoints}");
			Print($"  ➤ SessionStart: {SessionStart}");
			Print($"  ➤ SessionEnd: {SessionEnd}");
		}

        public enum StrategyPreset
        {
            Asia,
            London,
            New_York
        }

		private void ApplyPresetForTime(DateTime time)
		{
			StrategyPreset desired = DeterminePresetForTime(time);
			if (!presetInitialized || desired != activePreset)
			{
				activePreset = desired;
				ApplyInstrumentPreset(activePreset);
				presetInitialized = true;
				if (debug)
				{
					EnsureEffectiveTimes(time, false);
					Print(
						$"{time:yyyy-MM-dd HH:mm} - Preset switched to {FormatPresetLabel(activePreset)} | " +
						$"SS={effectiveSessionStart:hh\\:mm} SE={effectiveSessionEnd:hh\\:mm} " +
						$"NTA={effectiveNoTradesAfter:hh\\:mm} Skip1={effectiveSkipStart:hh\\:mm}-{effectiveSkipEnd:hh\\:mm}");
				}
			}
		}

        private StrategyPreset DeterminePresetForTime(DateTime time)
        {
			TimeSpan asiaStart = new TimeSpan(0, 10, 0);
			TimeSpan asiaEnd = new TimeSpan(2, 0, 0);
			TimeSpan baseLondonStart = new TimeSpan(3, 0, 0);
			TimeSpan baseLondonEnd = new TimeSpan(5, 20, 0);
			TimeSpan shift = GetLondonSessionShiftForDate(time.Date);
			TimeSpan londonStart = ShiftTime(baseLondonStart, shift);
			TimeSpan londonEnd = ShiftTime(baseLondonEnd, shift);
			TimeSpan newYorkStart = new TimeSpan(9, 40, 0);
			TimeSpan newYorkEnd = new TimeSpan(14, 50, 0);
			TimeSpan now = time.TimeOfDay;

			if (TradeAsiaSession && IsTimeInRange(now, asiaStart, asiaEnd))
				return StrategyPreset.Asia;
			if (TradeLondonSession && IsTimeInRange(now, londonStart, londonEnd))
				return StrategyPreset.London;
			if (TradeNewYorkSession && IsTimeInRange(now, newYorkStart, newYorkEnd))
				return StrategyPreset.New_York;

			bool hasNext = false;
			TimeSpan nextStart = TimeSpan.MaxValue;
			StrategyPreset nextPreset = StrategyPreset.New_York;

			ConsiderNextPreset(StrategyPreset.Asia, TradeAsiaSession, asiaStart, now, ref hasNext, ref nextStart, ref nextPreset);
			ConsiderNextPreset(StrategyPreset.London, TradeLondonSession, londonStart, now, ref hasNext, ref nextStart, ref nextPreset);
			ConsiderNextPreset(StrategyPreset.New_York, TradeNewYorkSession, newYorkStart, now, ref hasNext, ref nextStart, ref nextPreset);

			return hasNext ? nextPreset : StrategyPreset.New_York;
		}

		private string FormatPresetLabel(StrategyPreset preset)
		{
			switch (preset)
			{
				case StrategyPreset.Asia:
					return "Asia";
				case StrategyPreset.London:
					return "London";
				default:
					return "New York";
			}
		}

		private bool IsTimeInRange(TimeSpan now, TimeSpan start, TimeSpan end)
		{
			if (start < end)
				return now >= start && now < end;

			return now >= start || now < end;
		}

		private void ConsiderNextPreset(
			StrategyPreset preset,
			bool enabled,
			TimeSpan start,
			TimeSpan now,
			ref bool hasNext,
			ref TimeSpan nextStart,
			ref StrategyPreset nextPreset)
		{
			if (!enabled)
				return;

			TimeSpan candidate = start < now ? start.Add(TimeSpan.FromDays(1)) : start;
			if (!hasNext || candidate < nextStart)
			{
				hasNext = true;
				nextStart = candidate;
				nextPreset = preset;
			}
		}

        private void ApplyInstrumentPreset(StrategyPreset preset)
        {
            switch (preset)
            {

                case StrategyPreset.Asia:
					londonAutoShiftTimes = true;
                    MinC12Body  = 2.7;
                    MaxC12Body  = 104.5;
                    OffsetPerc  = 20.5;					
                    TpPerc      = 78.5;
                    CancelPerc  = 309;
                    DeviationPerc = 0;
                    SLPadding = 0;
					MaxSLTPRatioPerc = 550;
                    SLPresetSetting = SLPreset.First_Candle_High_Low;
					SLPercentFirstCandle = 100;
                    MaxSLPoints = 92;

                    // ✅ Session preset values
                    SessionStart  = new TimeSpan(20, 00, 0);
                    SessionEnd    = new TimeSpan(00, 00, 0);
                    NoTradesAfter = new TimeSpan(23, 30, 0);
                    SkipStart = new TimeSpan(00, 00, 0);
                    SkipEnd = new TimeSpan(00, 00, 0);
                    Skip2Start = new TimeSpan(00, 00, 0);
                    Skip2End = new TimeSpan(00, 00, 0); 
                    break;

                case StrategyPreset.London:
					londonAutoShiftTimes = true;
                    MinC12Body  = 11;
                    MaxC12Body  = 156.5;
                    OffsetPerc  = 5.5;					
                    TpPerc      = 31.5;
                    CancelPerc  = 172;
                    DeviationPerc = 0;
                    SLPadding = 0;
					MaxSLTPRatioPerc = 446;
                    SLPresetSetting = SLPreset.First_Candle_High_Low;
					SLPercentFirstCandle = 100;
                    MaxSLPoints = 150;

                    // ✅ Session preset values
                    SessionStart  = new TimeSpan(1, 30, 0); // Remember to change in settings as well
                    SessionEnd    = new TimeSpan(5, 30, 0); // Remember to change in settings as well
                    NoTradesAfter = new TimeSpan(5, 00, 0);
                    SkipStart = new TimeSpan(00, 00, 0);
                    SkipEnd = new TimeSpan(00, 00, 0);
                    Skip2Start = new TimeSpan(00, 00, 0);
                    Skip2End = new TimeSpan(00, 00, 0); 
                    break;
                    
                case StrategyPreset.New_York:
					londonAutoShiftTimes = false;
                    MinC12Body  = 19.5;
                    MaxC12Body  = 130.5;
                    OffsetPerc  = 1;					
                    TpPerc      = 28;
                    CancelPerc  = 158;
                    DeviationPerc = 0;
                    SLPadding = 0;
					MaxSLTPRatioPerc = 477;
                    SLPresetSetting = SLPreset.First_Candle_High_Low;
					SLPercentFirstCandle = 100;
                    MaxSLPoints = 152;

                    // ✅ Session preset values
                    SessionStart  = new TimeSpan(9, 40, 0); // Remember to change in settings as well
                    SessionEnd    = new TimeSpan(15, 00, 0); // Remember to change in settings as well
                    NoTradesAfter = new TimeSpan(14, 30, 0);
                    SkipStart = new TimeSpan(11, 45, 0);
                    SkipEnd = new TimeSpan(13, 20, 0);
                    Skip2Start = new TimeSpan(00, 00, 0);
                    Skip2End = new TimeSpan(00, 00, 0); 
                    break;
            }

			effectiveTimesDate = DateTime.MinValue;
            //PrintInstrumentPreset(preset);
        }

        private void ApplyStopLossPreset(SLPreset preset)
        {
            switch (preset)
            {
                case SLPreset.First_Candle_High_Low:
                    First_Candle_High_Low = true;
                    First_Candle_Open = false;
                    Second_Candle_High_Low = false;
                    Second_Candle_Open = false;
                    break;
                case SLPreset.First_Candle_Open:
                    First_Candle_High_Low = false;
                    First_Candle_Open = true;
                    Second_Candle_High_Low = false;
                    Second_Candle_Open = false;
                    break;
                case SLPreset.Second_Candle_High_Low:
                    First_Candle_High_Low = false;
                    First_Candle_Open = false;
                    Second_Candle_High_Low = true;
                    Second_Candle_Open = false;
                    break;
                case SLPreset.Second_Candle_Open:
                    First_Candle_High_Low = false;
                    First_Candle_Open = false;
                    Second_Candle_High_Low = false;
                    Second_Candle_Open = true;
                    break;
				case SLPreset.First_Candle_Percent:
                    First_Candle_High_Low = false;
                    First_Candle_Open = false;
                    Second_Candle_High_Low = false;
                    Second_Candle_Open = false;
                    break;
            }

            //Print($"== SL PRESET APPLIED: {preset} ==");
        }

        public enum SLPreset
	    {
	        First_Candle_High_Low,
	        First_Candle_Open,
	        Second_Candle_High_Low,
	        Second_Candle_Open,
			First_Candle_Percent
	    }

        private void SetHedgeLock(string instrument, MarketPosition direction)
        {
            lock (hedgeLockSync)
            {
                var lines = File.Exists(hedgeLockFile)
                    ? File.ReadAllLines(hedgeLockFile).ToList()
                    : new List<string>();

                bool updated = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith(instrument + ",", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"{instrument},{direction}";
                        updated = true;
                        break;
                    }
                }
                if (!updated)
                    lines.Add($"{instrument},{direction}");

                File.WriteAllLines(hedgeLockFile, lines);
            }
        }

        private MarketPosition GetHedgeLock(string instrument)
        {
            lock (hedgeLockSync)
            {
                if (!File.Exists(hedgeLockFile))
                    return MarketPosition.Flat;

                foreach (var line in File.ReadAllLines(hedgeLockFile))
                {
                    var parts = line.Split(',');
                    if (parts.Length == 2 && parts[0].Equals(instrument, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Enum.TryParse(parts[1], out MarketPosition pos))
                            return pos;
                    }
                }
                return MarketPosition.Flat;
            }
        }

        private void ClearHedgeLock(string instrument)
        {
            lock (hedgeLockSync)
            {
                if (!File.Exists(hedgeLockFile))
                    return;

                var lines = File.ReadAllLines(hedgeLockFile).ToList();
                lines.RemoveAll(l => l.StartsWith(instrument + ",", StringComparison.OrdinalIgnoreCase));
                File.WriteAllLines(hedgeLockFile, lines);
            }
        }

	        string GetTicker(Instrument instrument)
	        {
            // Get the month code letter (F=Jan, G=Feb, H=Mar, etc.)
            string[] monthCodes = { "", "F", "G", "H", "J", "K", "M", "N", "Q", "U", "V", "X", "Z" };
            string monthCode = monthCodes[instrument.Expiry.Month];
            string yearCode = instrument.Expiry.Year.ToString().Substring(2, 2); // or full year if you prefer

	            return $"{instrument.MasterInstrument.Name}{monthCode}20{yearCode}";
	        }

	        private void QueueEntryWebhookLong(double entryPrice, double takeProfit, double stopLoss)
	        {
	            if (Position.MarketPosition != MarketPosition.Flat)
	                return;

	            pendingLongWebhook = true;
	            pendingLongEntry = entryPrice;
	            pendingLongTP = takeProfit;
	            pendingLongSL = stopLoss;
	        }

	        private void QueueEntryWebhookShort(double entryPrice, double takeProfit, double stopLoss)
	        {
	            if (Position.MarketPosition != MarketPosition.Flat)
	                return;

	            pendingShortWebhook = true;
	            pendingShortEntry = entryPrice;
	            pendingShortTP = takeProfit;
	            pendingShortSL = stopLoss;
	        }

	        private void SendWebhookCancelSafe()
	        {
	            if (Position.MarketPosition != MarketPosition.Flat)
	                return;

	            if (CurrentBar == lastCancelWebhookBar)
	                return;

	            lastCancelWebhookBar = CurrentBar;
	            SendWebhook("cancel");
	        }

	        private void SendWebhookExitSafe()
	        {
	            if (CurrentBar == lastExitWebhookBar)
	                return;

	            lastExitWebhookBar = CurrentBar;
	            SendWebhook("exit");
	        }

        private void SendWebhook(string eventType, double entryPrice = 0, double takeProfit = 0, double stopLoss = 0)
        {
            if (State != State.Realtime)
            return;

            if (string.IsNullOrEmpty(WebhookUrl))
                return;

            try
            {
                string ticker = GetTicker(Instrument);

                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
                string json = "";

                switch (eventType.ToLower())
                {
                    case "buy":
                    case "sell":
                        json = $@"
                        {{
                            ""ticker"": ""{ticker}"",
                            ""action"": ""{eventType}"",
                            ""orderType"": ""limit"",
                            ""limitPrice"": {entryPrice},
                            ""quantityType"": ""fixed_quantity"",
                            ""quantity"": {Contracts},
                            ""signalPrice"": {entryPrice},
                            ""time"": ""{time}"",
                            ""takeProfit"": {{
                                ""limitPrice"": {takeProfit}
                            }},
                            ""stopLoss"": {{
                                ""type"": ""stop"",
                                ""stopPrice"": {stopLoss}
                            }}
                        }}";
                        break;

	                    case "exit":
	                        json = $@"
	                        {{
	                            ""ticker"": ""{ticker}"",
	                            ""action"": ""exit"",
	                            ""orderType"": ""market"",
	                            ""quantityType"": ""fixed_quantity"",
	                            ""quantity"": {Contracts},
	                            ""cancel"": true,
	                            ""time"": ""{time}""
	                        }}";
	                        break;

                    case "cancel":
                        json = $@"
                        {{
                            ""ticker"": ""{ticker}"",
                            ""action"": ""cancel"",
                            ""time"": ""{time}""
                        }}";
                        break;
                }


                using (var client = new System.Net.WebClient())
                {
                    client.Headers[System.Net.HttpRequestHeader.ContentType] = "application/json";
                    client.UploadString(WebhookUrl, "POST", json);
                }

                Print($"✅ Webhook sent to TradersPost: {eventType.ToUpper()} for {ticker}");
            }
            catch (Exception ex)
            {
                Print($"⚠️ Webhook error: {ex.Message}");
            }
        }
	    }
	}
