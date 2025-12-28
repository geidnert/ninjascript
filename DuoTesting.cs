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
    public class DuoTesting : Strategy
    {
        public DuoTesting()
        {
            // Old vendor licensing
            //VendorLicense("AutoEdge", "Duo", "www.autoedge.io", "support@autoedge.io", null);
        }

        // [NinjaScriptProperty]
        // [Display(Name = "Instrument Preset", Description = "Select a preset configuration", Order = 0,
        //         GroupName = "A. Config")]
        // public StrategyPreset PresetSetting { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Candle Mode", GroupName = "A. Parameters", Order = 0)]
        public CandleMode CandleModeSetting { get; set; }

		[NinjaScriptProperty]
		[Display(
			Name = "London Auto Shift Times",
			Description = "If true, session/skip times are automatically shifted during UK/US DST mismatch weeks (London preset behavior)",
			Order = 0,
			GroupName = "B. Session Time")]
		public bool LondonAutoShiftTimes { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Number of Contracts", GroupName = "A. Config", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
		[Display(Name = "Entry Confirmation", Description = "Show popup confirmation before each entry", Order = 2, GroupName = "A. Config")]
		public bool RequireEntryConfirmation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Anti Hedge", Description = "Dont take trade in opposite direction to prevent hedging", Order = 3,
                GroupName = "A. Config")]
        public bool AntiHedge {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Max Wins Same Direction", Description = "0 = Disabled. After N consecutive wins in the same direction, the next entry must be opposite.", Order = 4, GroupName = "A. Config")]
        [Range(0, int.MaxValue)]
        public int MaxWinsSameDirection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reverse On Signal", Description = "If true, flatten current position when a reverse signal is generated, then place the new limit order", Order = 5, GroupName = "A. Config")]
        public bool ReverseOnSignal { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum 1st Candle Body", GroupName = "A. Parameters", Order = 1)]
        public double MinC1Body { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Maximum 1st Candle Body", GroupName = "A. Parameters", Order = 2)]
        public double MaxC1Body { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Minimum 2nd Candle Body", GroupName = "A. Parameters", Order = 3)]
        public double MinC2Body { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Maximum 2nd Candle Body", GroupName = "A. Parameters", Order = 4)]
        public double MaxC2Body { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Offset % of 2nd candle body", GroupName = "A. Parameters", Order = 5)]
        public double OffsetPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Take Profit % of 2nd candle body", GroupName = "A. Parameters", Order = 6)]
        public double TpPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cancel Order % of 2nd candle body", GroupName = "A. Parameters", Order = 7)]
        public double CancelPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cancel On Touch", Description = "If true, cancel as soon as price touches the cancel level; otherwise cancel on bar close", GroupName = "A. Parameters", Order = 50)]
        public bool CancelOnTouch { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Deviation %", Description = "Max random deviation applied to entry/TP %", 
            Order = 8, GroupName = "A. Parameters")]
        [Range(0, double.MaxValue)]
        public double DeviationPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL Padding", Description = "Extra padding added to stop loss (in price units, 0.25 increments)", 
            GroupName = "A. Parameters", Order = 9)]
        [Range(0, double.MaxValue)]
        public double SLPadding { get; set; }

		[NinjaScriptProperty]
        [Display(Name = "Max SL/TP Ratio %", Description = "Skip trades if SL is more than this % of TP (e.g., 200 = SL can be at most 2x TP)", 
                Order = 10, GroupName = "A. Parameters")]
        [Range(0, 1000)]
        public double MaxSLTPRatioPerc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SL type", Description = "Select the sl type you want", Order = 11, GroupName = "A. Parameters")]
        public SLPreset SLPresetSetting { get; set; }

		[NinjaScriptProperty]
        [Display(Name = "SL % of 1st Candle", Description = "0% = High of 1st candle (long), 100% = Low of 1st candle (long)", 
                Order = 12, GroupName = "A. Parameters")]
        [Range(0, 100)]
        public double SLPercentFirstCandle { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max SL (Points)", Description = "Maximum allowed stop loss in points. 0 = Disabled", Order = 13, GroupName = "A. Parameters")]
        public double MaxSLPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Session Start", Description = "When session is starting", Order = 1,
                GroupName = "B. Session Time")]
        public TimeSpan SessionStart
        {
            get {
                return sessionStart;
            }
            set {
                sessionStart = new TimeSpan(value.Hours, value.Minutes, 0);
            }
        }

        [NinjaScriptProperty]
        [Display(Name = "Session End",
                Description = "When session is ending, all positions and orders will be canceled when this time is passed",
                Order = 2, GroupName = "B. Session Time")]
        public TimeSpan SessionEnd
        {
            get {
                return sessionEnd;
            }
            set {
                sessionEnd = new TimeSpan(value.Hours, value.Minutes, 0);
            }
        }

		[NinjaScriptProperty]
		[Display(Name = "No Trades After",
				Description = "No more orders is being placed between this time and session end,", Order = 3,
				GroupName = "B. Session Time")]
		public TimeSpan NoTradesAfter
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
        [Display(Name = "Session Fill", Description = "Color of the session background", Order = 4, GroupName = "B. Session Time")]
        public Brush SessionBrush { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Close at Session End", 
                Description = "If true, open trades will be closed and working orders canceled at session end", 
                Order = 4, GroupName = "B. Session Time")]
        public bool CloseAtSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start", Description = "Start of skip window", Order = 1, GroupName = "C. Skip Times")]
        public TimeSpan SkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End", Description = "End of skip window", Order = 2, GroupName = "C. Skip Times")]
        public TimeSpan SkipEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip Start 2", Description = "Start of 2nd skip window", Order = 3, GroupName = "C. Skip Times")]
        public TimeSpan Skip2Start { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip End 2", Description = "End of 2nd skip window", Order = 4, GroupName = "C. Skip Times")]
        public TimeSpan Skip2End { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Force Close at Skip Start", Description = "If true, flatten/cancel as soon as a skip window begins", Order = 5, GroupName = "C. Skip Times")]
        public bool ForceCloseAtSkipStart { get; set; }

        // State tracking
        private bool longOrderPlaced = false;
        private bool shortOrderPlaced = false;
        private Order longEntryOrder = null;
        private Order shortEntryOrder = null;
        private TimeSpan sessionStart = new TimeSpan(9, 40, 0);
        private TimeSpan sessionEnd = new TimeSpan(14, 50, 0);  
		private TimeSpan noTradesAfter = new TimeSpan(14, 30, 0);
        private TimeSpan skipStart = new TimeSpan(11, 45, 0);
        private TimeSpan skipEnd = new TimeSpan(13, 20, 0);
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

        // --- Consecutive win direction filter ---
        private int consecutiveWinsSameDirection;
        private MarketPosition consecutiveWinsDirection = MarketPosition.Flat;
        private MarketPosition requiredNextEntryDirection = MarketPosition.Flat; // Flat = no restriction

        // --- Heartbeat reporting ---
        private string heartbeatFile = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "TradeMessengerHeartbeats.csv");
        private System.Timers.Timer heartbeatTimer;
        private DateTime lastHeartbeatWrite = DateTime.MinValue;
        private int heartbeatIntervalSeconds = 10; // send heartbeat every 10 seconds
        

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "DuoTesting";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsInstantiatedOnEachOptimizationIteration = false;

                // Default input values (same order as A. Parameters)
                CandleModeSetting = CandleMode.TwoCandle; 
                Contracts     = 1;
                MinC1Body   = 2.6;
                MaxC1Body   = 86.1;
                MinC2Body   = 12.6;
                MaxC2Body   = 73.7;
                OffsetPerc  = 29.2;
                TpPerc      = 68.1;
                CancelPerc  = 295;
                CancelOnTouch = false;
                DeviationPerc = 0;
                SLPadding = 0;
                MaxSLTPRatioPerc = 500;
                SLPresetSetting = SLPreset.First_Candle_High_Low;
                SLPercentFirstCandle = 97;
                MaxSLPoints = 161;

                // Other defaults
                SessionBrush  = Brushes.Gold;
                CloseAtSessionEnd = true;
                LondonAutoShiftTimes = false;
                SkipStart     = skipStart;
                SkipEnd       = skipEnd;
                Skip2Start     = skip2Start;
                Skip2End       = skip2End;
                ForceCloseAtSkipStart = true;
                RequireEntryConfirmation = false;
                AntiHedge = false;
                MaxWinsSameDirection = 0;
                ReverseOnSignal = false;

                // Default session times
                SessionStart  = new TimeSpan(09, 40, 0);
                SessionEnd    = new TimeSpan(14, 50, 0);
				NoTradesAfter = new TimeSpan(14, 30, 0);
            }
            else if (State == State.DataLoaded)
            {
                // --- Heartbeat timer setup ---
                heartbeatTimer = new System.Timers.Timer(heartbeatIntervalSeconds * 1000);
                heartbeatTimer.Elapsed += (s, e) => WriteHeartbeat();
                heartbeatTimer.AutoReset = true;
                heartbeatTimer.Start();

                //ApplyInstrumentPreset(PresetSetting);
                ApplyStopLossPreset(SLPresetSetting);

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

				EnsureEffectiveTimes(Time[0]);
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

                consecutiveWinsSameDirection = 0;
                consecutiveWinsDirection = MarketPosition.Flat;
                requiredNextEntryDirection = MarketPosition.Flat;
			
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
					    }
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
                    }
                    // Case 3: In a position -> do nothing, let TP/SL handle it
                }

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
			}

            // 🔒 HARD GUARD: absolutely no logic inside skip windows
            if (TimeInSkip(Time[0]))
                return;

            // 🔒 HARD GUARD: absolutely no new logic/entries outside session
            if (!TimeInSession(Time[0]))
                return;

			// 🔒 HARD GUARD: absolutely no logic after NoTradesAfter
			if (TimeInNoTradesAfter(Time[0])) {
				CancelEntryIfAfterNoTrades();
				return;
			}

            // === Long/Short Cancel: TP hit before entry ===
            if (!CancelOnTouch)
            {
                if (longEntryOrder != null && longEntryOrder.OrderState == OrderState.Working)
                {
                    if (High[0] >= currentLongCancelPrice && Low[0] > currentLongEntry)
                    //if (High[0] >= currentLongTP && Low[0] > currentLongEntry)
                    {
                        CancelLongEntry();
                    }
                }

                if (shortEntryOrder != null && shortEntryOrder.OrderState == OrderState.Working)
                {
                    if (Low[0] <= currentShortCancelPrice && High[0] < currentShortEntry)
                    //if (Low[0] <= currentShortTP && High[0] < currentShortEntry)
                    {
                        CancelShortEntry();
                    }
                }
            }

            // === Strategy signal logic (on bar close only) ===
	            if (Calculate == Calculate.OnBarClose 
	                && CurrentBar > skipBarUntil 
	                && CurrentBar != lastBarProcessed)
	            {
	                int c1Idx = CandleModeSetting == CandleMode.OneCandle ? 0 : 1;
	                int c2Idx = 0;
	                double c1Open = Open[c1Idx];
                double c1Close = Close[c1Idx];
                double c1High = High[c1Idx];
                double c1Low = Low[c1Idx];

                double c2Open = Open[c2Idx];
                double c2Close = Close[c2Idx];
                double c2High = High[c2Idx];
                double c2Low = Low[c2Idx];

	                double c1Body = Math.Abs(c1Close - c1Open);
	                double c2Body = Math.Abs(c2Close - c2Open);

	                if (CandleModeSetting == CandleMode.OneCandle)
	                {
	                    if (c1Body < MinC1Body || c1Body > MaxC1Body)
	                    {
	                        if (debug)
	                        {
	                            Print(
	                                $"{Time[0]} - Candle body filter skip | " +
	                                $"c1Body={c1Body:0.00} (min={MinC1Body:0.00}, max={MaxC1Body:0.00})");
	                        }
	                        return;
	                    }
	                }
	                else
	                {
	                    if (c1Body < MinC1Body || c1Body > MaxC1Body ||
	                        c2Body < MinC2Body || c2Body > MaxC2Body)
	                    {
	                        if (debug)
	                        {
	                            Print(
	                                $"{Time[0]} - Candle body filter skip | " +
	                                $"c1Body={c1Body:0.00} (min={MinC1Body:0.00}, max={MaxC1Body:0.00}) | " +
	                                $"c2Body={c2Body:0.00} (min={MinC2Body:0.00}, max={MaxC2Body:0.00})");
	                        }
	                        return;
	                    }
	                }

                bool c1Bull = c1Close > c1Open;
                bool c2Bull = c2Close > c2Open;
                bool c1Bear = c1Close < c1Open;
                bool c2Bear = c2Close < c2Open;

                bool validBull = CandleModeSetting == CandleMode.OneCandle ? c2Bull : (c1Bull && c2Bull);
                bool validBear = CandleModeSetting == CandleMode.OneCandle ? c2Bear : (c1Bear && c2Bear);

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

                // === Determine range depending on Candle Mode ===
                double range;
                if (CandleModeSetting == CandleMode.OneCandle)
                {
                    // 🟦 1-Candle mode: use body of current candle
                    range = Math.Abs(Close[c2Idx] - Open[c2Idx]);
                    if (debug)
                        Print($"{Time[0]} - 🟦 Using 1-Candle mode range: |Close[0] - Open[0]| = {range:0.00}");
                }
                else
                {
                    // 🟩 2-Candle mode: use Open of previous candle and Close of current candle
                    range = Math.Abs(Close[c2Idx] - Open[c1Idx]);
                    if (debug)
                        Print($"{Time[0]} - 🟩 Using 2-Candle mode range: |Close[0] - Open[1]| = {range:0.00}");
                }

                // === Apply range to entry / TP / cancel calculations ===
                double offset = range * (randomizedOffsetPerc / 100.0);
                double longEntry = c2Close - offset;
                double shortEntry = c2Close + offset;

                double longTP = longEntry + range * (randomizedTpPerc / 100.0);
                double shortTP = shortEntry - range * (randomizedTpPerc / 100.0);

                double longCancelPrice = longEntry + range * (CancelPerc / 100.0);
                double shortCancelPrice = shortEntry - range * (CancelPerc / 100.0);

                double longSLPoints = Math.Abs(longEntry - longSL) / TickSize;
                double shortSLPoints = Math.Abs(shortEntry - shortSL) / TickSize;

                if (debug) {
                    Print($"\n{Time[0]} - Candle body sizes:");
                    Print($"  ➤ c1Body = {c1Body:0.00} (Min allowed: {MinC1Body:0.00}, Max allowed: {MaxC1Body:0.00})");
                    if (CandleModeSetting == CandleMode.TwoCandle)
                        Print($"  ➤ c2Body = {c2Body:0.00} (Min allowed: {MinC2Body:0.00}, Max allowed: {MaxC2Body:0.00})");
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
                    if (!IsEntryAllowedByConsecutiveWinRule(MarketPosition.Long))
                    {
                        if (debug)
                            Print($"{Time[0]} - 🚫 Long entry blocked: requires {requiredNextEntryDirection} after {consecutiveWinsSameDirection} consecutive {consecutiveWinsDirection} wins.");
                    }
                    else
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

                    // === LONG RR CHECK ===
                    if (MaxSLTPRatioPerc > 0)  // only enforce if enabled
                    {
                        double longTPPoints2, longSLPoints2;

                        if (CandleModeSetting == CandleMode.OneCandle)
                        {
                            // Use current candle's full wick range for RR% in OneCandle mode
                            double oneCandleRange = Math.Abs(High[c2Idx] - Low[c2Idx]);
                            double estimatedTP = longEntry + oneCandleRange * (TpPerc / 100.0);
                            longTPPoints2 = Math.Abs(estimatedTP - longEntry) / TickSize;
                        }
                        else
                        {
                            longTPPoints2 = Math.Abs(longTP - longEntry) / TickSize;
                        }

                        longSLPoints2 = Math.Abs(longEntry - longSL) / TickSize;

                        if (longTPPoints2 > 0)
                        {
                            double ratioPerc = (longSLPoints2 / longTPPoints2) * 100.0;
                            if (ratioPerc > MaxSLTPRatioPerc)
                            {
                                if (debug)
                                    Print($"{Time[0]} - ❌ Skipping LONG trade. SL/TP ratio = {ratioPerc:0.00}% exceeds max {MaxSLTPRatioPerc}%");
                                return;
                            }
                        }
                    }

                    //double paddedLongSL = longSL - SLPadding;
					double paddedLongSL = GetSLForLong();
                    if (paddedLongSL >= longEntry)
                    {
                        if (debug)
                            Print($"{Time[0]} - 🚫 Skipping long trade: SL {paddedLongSL:0.00} is not below entry {longEntry:0.00}");
                        return;
                    }
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

                    SetStopLoss("LongEntry", CalculationMode.Price, paddedLongSL, false);
                    SetProfitTarget("LongEntry", CalculationMode.Price, longTP);
					EnterLongLimit(0, true, Contracts, longEntry, "LongEntry");
                    // Draw short horizontal lines
                    Draw.Line(this, "LongEntryLine_" + CurrentBar, false,
                        1, longEntry, 0, longEntry, Brushes.Gold, DashStyleHelper.Solid, 2);

                    Draw.Line(this, "LongTPLine_" + CurrentBar, false,
                        1, longTP, 0, longTP, Brushes.LimeGreen, DashStyleHelper.Solid, 2);

                    Draw.Line(this, "LongSLLine_" + CurrentBar, false,
                        1, paddedLongSL, 0, paddedLongSL, Brushes.Red, DashStyleHelper.Solid, 2);

                    Draw.Line(this, "LongCancelLine_" + CurrentBar, false, 1, longCancelPrice, 0, longCancelPrice, Brushes.Gray, DashStyleHelper.Dot, 2);

                    currentLongEntry = longEntry;
                    currentLongTP = longTP;
                    currentLongSL = paddedLongSL;
                    currentLongCancelPrice = longCancelPrice;
                    longOrderPlaced = true;
                    shortOrderPlaced = false;
                    UpdateInfo();
                    }
                }

                if (validBear && !shortOrderPlaced)
                {
                    if (!IsEntryAllowedByConsecutiveWinRule(MarketPosition.Short))
                    {
                        if (debug)
                            Print($"{Time[0]} - 🚫 Short entry blocked: requires {requiredNextEntryDirection} after {consecutiveWinsSameDirection} consecutive {consecutiveWinsDirection} wins.");
                    }
                    else
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

                    // === SHORT RR CHECK ===
                    if (MaxSLTPRatioPerc > 0)  // only enforce if enabled
                    {
                        double shortTPPoints2, shortSLPoints2;

                        if (CandleModeSetting == CandleMode.OneCandle)
                        {
                            // Use current candle's full wick range for RR% in OneCandle mode
                            double oneCandleRange = Math.Abs(High[c2Idx] - Low[c2Idx]);
                            double estimatedTP = shortEntry - oneCandleRange * (TpPerc / 100.0);
                            shortTPPoints2 = Math.Abs(shortEntry - estimatedTP) / TickSize;
                        }
                        else
                        {
                            shortTPPoints2 = Math.Abs(shortEntry - shortTP) / TickSize;
                        }

                        shortSLPoints2 = Math.Abs(shortSL - shortEntry) / TickSize;

                        if (shortTPPoints2 > 0)
                        {
                            double ratioPerc = (shortSLPoints2 / shortTPPoints2) * 100.0;
                            if (ratioPerc > MaxSLTPRatioPerc)
                            {
                                if (debug)
                                    Print($"{Time[0]} - ❌ Skipping SHORT trade. SL/TP ratio = {ratioPerc:0.00}% exceeds max {MaxSLTPRatioPerc}%");
                                return;
                            }
                        }
                    }

                    //double paddedShortSL = shortSL + SLPadding;
					double paddedShortSL = GetSLForShort();
                    if (paddedShortSL <= shortEntry)
                    {
                        if (debug)
                            Print($"{Time[0]} - 🚫 Skipping short trade: SL {paddedShortSL:0.00} is not above entry {shortEntry:0.00}");
                        return;
                    }
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

                        Draw.Line(this, "ShortCancelLine_" + CurrentBar, false, 1, shortCancelPrice, 0, shortCancelPrice, Brushes.Black, DashStyleHelper.Dot, 2);

                        if (debug)    
                            Print($"SKIP {Instrument.MasterInstrument.Name} SHORT, {otherSymbol} is LONG.");
                        return;
                    }
                    
                    SetStopLoss("ShortEntry", CalculationMode.Price, paddedShortSL, false);
                    SetProfitTarget("ShortEntry", CalculationMode.Price, shortTP);
					EnterShortLimit(0, true, Contracts, shortEntry, "ShortEntry");
                    Draw.Line(this, "ShortEntryLine_" + CurrentBar, false,
                        1, shortEntry, 0, shortEntry, Brushes.Gold, DashStyleHelper.Solid, 2);

                    Draw.Line(this, "ShortTPLine_" + CurrentBar, false,
                        1, shortTP, 0, shortTP, Brushes.LimeGreen, DashStyleHelper.Solid, 2);

                    Draw.Line(this, "ShortSLLine_" + CurrentBar, false,
                        1, paddedShortSL, 0, paddedShortSL, Brushes.Red, DashStyleHelper.Solid, 2);

                    Draw.Line(this, "ShortCancelLine_" + CurrentBar, false, 1, shortCancelPrice, 0, shortCancelPrice, Brushes.Gray, DashStyleHelper.Dot, 2);
                    currentShortEntry = shortEntry;
                    currentShortTP = shortTP;
                    currentShortSL = paddedShortSL;
                    currentShortCancelPrice = shortCancelPrice;
                    shortOrderPlaced = true;
                    longOrderPlaced = false;
                    UpdateInfo();
                    }
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

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity,
            MarketPosition marketPosition, string orderId, DateTime time)
        {
            var smallFont = new SimpleFont("Arial", 8) { Bold = true };

            // --- Clear "required next direction" once an entry in that direction fills ---
            if (MaxWinsSameDirection > 0 && requiredNextEntryDirection != MarketPosition.Flat
                && execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                if (execution.Order.Name == "LongEntry" && requiredNextEntryDirection == MarketPosition.Long)
                    requiredNextEntryDirection = MarketPosition.Flat;
                else if (execution.Order.Name == "ShortEntry" && requiredNextEntryDirection == MarketPosition.Short)
                    requiredNextEntryDirection = MarketPosition.Flat;
            }

            // ✅ Track TP fills
            if (execution.Order != null && execution.Order.Name == "Profit target"
                && execution.Order.OrderState == OrderState.Filled)
            {
                if (MaxWinsSameDirection > 0)
                {
                    MarketPosition winDirection = execution.Order.FromEntrySignal == "LongEntry"
                        ? MarketPosition.Long
                        : MarketPosition.Short;

                    if (consecutiveWinsDirection == winDirection)
                        consecutiveWinsSameDirection++;
                    else
                    {
                        consecutiveWinsDirection = winDirection;
                        consecutiveWinsSameDirection = 1;
                    }

                    if (consecutiveWinsSameDirection >= MaxWinsSameDirection)
                        requiredNextEntryDirection = winDirection == MarketPosition.Long ? MarketPosition.Short : MarketPosition.Long;
                }

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
                if (MaxWinsSameDirection > 0)
                {
                    consecutiveWinsSameDirection = 0;
                    consecutiveWinsDirection = MarketPosition.Flat;
                    requiredNextEntryDirection = MarketPosition.Flat;
                }

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
                    currentLongEntry = currentShortEntry = 0;
                    currentLongTP = currentShortTP = 0;
                    currentLongSL = currentShortSL = 0;
                    currentLongCancelPrice = currentShortCancelPrice = 0;

                    UpdateInfo();
                }
            }
        }

        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (!CancelOnTouch)
                return;

            if (marketDataUpdate.MarketDataType != MarketDataType.Last)
                return;

            if (BarsInProgress != 0 || CurrentBar < 1)
                return;

            if (TimeInSkip(Time[0]) || !TimeInSession(Time[0]) || TimeInNoTradesAfter(Time[0]))
                return;

            double price = marketDataUpdate.Price;

            if (longEntryOrder != null && longEntryOrder.OrderState == OrderState.Working)
            {
                if (price >= currentLongCancelPrice && price > currentLongEntry)
                    CancelLongEntry();
            }

            if (shortEntryOrder != null && shortEntryOrder.OrderState == OrderState.Working)
            {
                if (price <= currentShortCancelPrice && price < currentShortEntry)
                    CancelShortEntry();
            }
        }

        private void CancelLongEntry()
        {
            if (debug)
                Print($"{Time[0]} - 🚫 Long cancel price hit before entry fill. Canceling order.");
            CancelOrder(longEntryOrder);
            longOrderPlaced = false;
            longEntryOrder = null;

            // 🔄 Redraw canceled lines in gray
            Draw.Line(this, "LongEntryLine_" + CurrentBar, false, 1, currentLongEntry, 0, currentLongEntry, Brushes.Gray, DashStyleHelper.Solid, 2);
            Draw.Line(this, "LongTPLine_" + CurrentBar, false, 1, currentLongTP, 0, currentLongTP, Brushes.Gray, DashStyleHelper.Solid, 2);
            Draw.Line(this, "LongSLLine_" + CurrentBar, false, 1, GetSLForLong(), 0, GetSLForLong(), Brushes.Gray, DashStyleHelper.Solid, 2);
            Draw.Line(this, "LongCancelLine_" + CurrentBar, false, 1, currentLongCancelPrice, 0, currentLongCancelPrice, Brushes.Gray, DashStyleHelper.Dot, 2);

            skipBarUntil = CurrentBar;
            if (debug)
                Print($"{Time[0]} - ➡️ Skipping signals until bar > {skipBarUntil}");
        }

        private void CancelShortEntry()
        {
            if (debug)
                Print($"{Time[0]} - 🚫 Short cancel price hit before entry fill. Canceling order.");
            CancelOrder(shortEntryOrder);
            shortOrderPlaced = false;
            shortEntryOrder = null;

            // 🔄 Redraw canceled lines in gray
            Draw.Line(this, "ShortEntryLine_" + CurrentBar, false, 1, currentShortEntry, 0, currentShortEntry, Brushes.Gray, DashStyleHelper.Solid, 2);
            Draw.Line(this, "ShortTPLine_" + CurrentBar, false, 1, currentShortTP, 0, currentShortTP, Brushes.Gray, DashStyleHelper.Solid, 2);
            Draw.Line(this, "ShortSLLine_" + CurrentBar, false, 1, GetSLForShort(), 0, GetSLForShort(), Brushes.Gray, DashStyleHelper.Solid, 2);
            Draw.Line(this, "ShortCancelLine_" + CurrentBar, false, 1, currentShortCancelPrice, 0, currentShortCancelPrice, Brushes.Gray, DashStyleHelper.Dot, 2);

            skipBarUntil = CurrentBar;
            if (debug)
                Print($"{Time[0]} - ➡️ Skipping signals until bar > {skipBarUntil}");
        }

        private void HandleReverseOnSignal(MarketPosition desiredDirection)
        {
            if (!ReverseOnSignal)
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

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

        private bool IsEntryAllowedByConsecutiveWinRule(MarketPosition desiredDirection)
        {
            if (MaxWinsSameDirection <= 0)
                return true;

            if (requiredNextEntryDirection == MarketPosition.Flat)
                return true;

            return desiredDirection == requiredNextEntryDirection;
        }

		private void CancelEntryIfAfterNoTrades()
		{
			if (shortEntryOrder != null)
			{
                if (debug)
				    Print($"{Time[0]} - ⏰ Canceling SHORT entry due to NoTradesAfter");
				CancelOrder(shortEntryOrder);
			}

			if (longEntryOrder != null)
			{
                if (debug)
				    Print($"{Time[0]} - ⏰ Canceling LONG entry due to NoTradesAfter");
				CancelOrder(longEntryOrder);
			}

			// Reset state tracking after cancel
			longOrderPlaced = false;
			shortOrderPlaced = false;
			longEntryOrder = null;
			shortEntryOrder = null;
		}

        private void Flatten(string reason)
        {
            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (debug)
                    Print($"{Time[0]} - Flattening LONG due to {reason}");
                ExitLong("Exit_" + reason, "LongEntry");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (debug)
                    Print($"{Time[0]} - Flattening SHORT due to {reason}");
                ExitShort("Exit_" + reason, "ShortEntry");
            }
        }
	

	        private bool TimeInSkip(DateTime time)
	        {
				EnsureEffectiveTimes(time);
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
				EnsureEffectiveTimes(time);
			    TimeSpan now = time.TimeOfDay;
			
			    if (effectiveSessionStart < effectiveSessionEnd)
			        return now >= effectiveSessionStart && now < effectiveSessionEnd;   // strictly less
			    else
			        return now >= effectiveSessionStart || now < effectiveSessionEnd;
			}

			private bool TimeInNoTradesAfter(DateTime time)
			{
				EnsureEffectiveTimes(time);
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
				EnsureEffectiveTimes(barTime);
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

            string tag = "DUO_SessionFill_" + sessionStartTime.ToString("yyyyMMdd");

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
				Draw.VerticalLine(this, $"NoTradesAfter_{Time[0]:yyyyMMdd}", noTradesAfterTime, r,
								DashStyleHelper.Solid, 2);

	        }

			private void EnsureEffectiveTimes(DateTime barTime)
			{
				if (!LondonAutoShiftTimes)
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

				if (debug)
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
            int c1Idx = CandleModeSetting == CandleMode.OneCandle ? 0 : 1;
            int c2Idx = 0;
            if (SLPresetSetting == SLPreset.First_Candle_Percent)
            {
                double c1High = High[c1Idx];
                double c1Low = Low[c1Idx];
                double sl = c1High - (SLPercentFirstCandle / 100.0) * (c1High - c1Low);

                if (debug)
                    Print($"{Time[0]} - 📉 Long SL set at {SLPercentFirstCandle}% of Candle 1 range: {sl:0.00}");

                return sl - SLPadding;
            }

            double baseSL = First_Candle_High_Low ? Low[c1Idx] :
                            First_Candle_Open ? Open[c1Idx] :
                            Second_Candle_High_Low ? Low[c2Idx] :
                            Second_Candle_Open ? Open[c2Idx] :
                            Open[c1Idx];
            return baseSL - SLPadding;
        }

        private double GetSLForShort()
        {
            int c1Idx = CandleModeSetting == CandleMode.OneCandle ? 0 : 1;
            int c2Idx = 0;
            if (SLPresetSetting == SLPreset.First_Candle_Percent)
            {
                double c1High = High[c1Idx];
                double c1Low = Low[c1Idx];
                double sl = c1Low + (SLPercentFirstCandle / 100.0) * (c1High - c1Low);

                if (debug)
                    Print($"{Time[0]} - 📈 Short SL set at {SLPercentFirstCandle}% of Candle 1 range: {sl:0.00}");

                return sl + SLPadding;
            }

            double baseSL = First_Candle_High_Low ? High[c1Idx] :
                            First_Candle_Open ? Open[c1Idx] :
                            Second_Candle_High_Low ? High[c2Idx] :
                            Second_Candle_Open ? Open[c2Idx] :
                            Open[c1Idx];
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
            UpdateInfoText(GetPnLInfo() + /*"\n" + PresetSetting + */"\nContracts: " + Contracts + "\nAnti Hedge: " + AntiHedge + "\nDuo v" +GetAddOnVersion());
        }

        public void UpdateInfoText(string newText)
        {
            displayText = newText;

            Draw.TextFixed(owner: this, tag: "myStatusLabel", text: displayText, textPosition: TextPosition.BottomLeft,
                        textBrush: Brushes.DarkGray, font: new SimpleFont("Segoe UI", 10), outlineBrush: null,
                        areaBrush: Brushes.Black, areaOpacity: 85);
        }

        private string GetPnLInfo()
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

            // 🧠 Show $0 only if no position and nothing pending
            if (!hasPosition && !hasLongOrder && !hasShortOrder && !hasPendingLong && !hasPendingShort)
                return "TP: $0\nSL: $0";

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
                return "TP: $0\nSL: $0";

            // --- Compute TP/SL distances ---
            double tpTicks = Math.Abs(tp - entry) / TickSize;
            double slTicks = Math.Abs(entry - sl) / TickSize;

            double tpDollars = tpTicks * tickValue * Contracts;
            double slDollars = slTicks * tickValue * Contracts;

            return $"TP: ${tpDollars:0}\nSL: ${slDollars:0}";
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

        private void WriteHeartbeat()
        {
            try
            {
                string name = this.Name ?? GetType().Name;
                string line = $"{name},{DateTime.Now:O}";
                List<string> lines = new List<string>();

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
                bool success = false;
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

			Print($"  ➤ MinC1Body: {MinC1Body}");
			Print($"  ➤ MaxC1Body: {MaxC1Body}");
			Print($"  ➤ MinC2Body: {MinC2Body}");
			Print($"  ➤ MaxC2Body: {MaxC2Body}");
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
            NQ_MNQ_5m//,
            //ES_MES_5m
        }

        public enum CandleMode { OneCandle, TwoCandle }

        private void ApplyInstrumentPreset(StrategyPreset preset)
        {
            switch (preset)
            {
                case StrategyPreset.NQ_MNQ_5m:
                    MinC1Body   = 3.25;
                    MaxC1Body   = 86;
                    MinC2Body   = 12.25;
                    MaxC2Body   = 73;
                    OffsetPerc  = 30;					
                    TpPerc      = 77;
                    CancelPerc  = 295;
                    DeviationPerc = 0;
                    SLPadding = 0;
					MaxSLTPRatioPerc = 500;
                    SLPresetSetting = SLPreset.First_Candle_Percent;
					SLPercentFirstCandle = 99;
                    MaxSLPoints = 140;
                    break;

                // case StrategyPreset.ES_MES_5m:
                //     MinC1Body   = 3.75;
                //     MaxC1Body   = 23.5;
                //     MinC2Body   = 7.75;
                //     MaxC2Body   = 42.5;
                //     OffsetPerc  = 80;					
                //     TpPerc      = 52.25;
                //     CancelPerc  = 200;
                //     DeviationPerc = 0;
                //     SLPadding = 0;
				// 	MaxSLTPRatioPerc = 650;
                //     SLPresetSetting = SLPreset.First_Candle_Open;
				// 	SLPercentFirstCandle = 99;
                //     MaxSLPoints = 28;
                //     break;
            }

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
    }
}
