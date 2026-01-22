#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
#endregion

// This namespace holds all strategies and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
	public class iFVG : Strategy
	{
		private static readonly object hedgeLockSync = new object();
		private static readonly string hedgeLockFile = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "AntiHedgeLock.csv");

		private class FvgBox
		{
			public string Tag;
			public int StartBarIndex;
			public int EndBarIndex;
			public int CreatedBarIndex;
			public double Upper;
			public double Lower;
			public bool IsBullish;
			public bool IsActive;
			public DateTime SessionDate;
		}

		private class HtfFvgBox
		{
			public string Tag;
			public DateTime StartTime;
			public DateTime EndTime;
			public DateTime CreatedTime;
			public double Upper;
			public double Lower;
			public bool IsBullish;
			public bool IsActive;
			public DateTime SessionDate;
		}

		private List<FvgBox> activeFvgs;
		private List<FvgBox> breakEvenFvgs;
		private List<HtfFvgBox> activeHtfFvgs;
		private int fvgCounter;
		private int htfFvgCounter;
		private Brush fvgFill;
		private int fvgOpacity;
		private Brush htfFvgFill;
		private Brush htfLabelBrush;
		private Brush invalidatedFill;
		private int invalidatedOpacity;
		private bool showInvalidatedFvgs;
		private double minFvgSizePoints;
		private double maxFvgSizePoints;
		private int fvgDrawLimit;
		private bool combineFvgSeries;
		private double londonMinFvgSizePoints;
		private double londonMaxFvgSizePoints;
		private int londonFvgDrawLimit;
		private bool londonCombineFvgSeries;
		private double newYorkMinFvgSizePoints;
		private double newYorkMaxFvgSizePoints;
		private int newYorkFvgDrawLimit;
		private bool newYorkCombineFvgSeries;
		private double activeMinFvgSizePoints;
		private double activeMaxFvgSizePoints;
		private int activeFvgDrawLimit;
		private bool activeCombineFvgSeries;
		private int contracts;
		private bool tradeMonday;
		private bool tradeTuesday;
		private bool tradeWednesday;
		private bool tradeThursday;
		private bool tradeFriday;
		private TimeSpan skipStart;
		private TimeSpan skipEnd;
		private bool closeAtSkipStart;
		private TimeSpan asiaSessionStart = new TimeSpan(20, 0, 0);
		private TimeSpan asiaSessionEnd = new TimeSpan(0, 0, 0);
		private TimeSpan londonSessionStart = new TimeSpan(2, 0, 0);
		private TimeSpan londonSessionEnd = new TimeSpan(5, 0, 0);
		private Brush asiaLineBrush;
		private Brush londonLineBrush;
		private List<LiquidityLine> liquidityLines;
		private SessionLiquidityState asiaState;
		private SessionLiquidityState londonState;
		private int sessionDrawLimit;
		private int swingStrength;
		private int londonSwingStrength;
		private int newYorkSwingStrength;
		private int activeSwingStrength;
		private int swingDrawBars;
		private Brush swingLineBrush;
		private List<SwingLine> swingLines;
		private bool useSwingLiquiditySweep;
		private bool useSessionLiquiditySweep;
		private bool useDeliverFromFvg;
		private bool useDeliverFromHtfFvg;
		private bool useSmt;
		private bool useSession1;
		private bool useSession2;
		private bool useSession3;
		private bool autoShiftSession1;
		private bool autoShiftSession2;
		private bool autoShiftSession3;
		private bool closeAtSessionEnd;
		private Brush sessionBrush;
		private TimeSpan sessionStart;
		private TimeSpan sessionEnd;
		private TimeSpan noTradesAfter;
		private TimeSpan session2SessionStart;
		private TimeSpan session2SessionEnd;
		private TimeSpan session2NoTradesAfter;
		private TimeSpan newYorkSkipStart;
		private TimeSpan newYorkSkipEnd;
		private bool newYorkCloseAtSkipStart;
		private TimeSpan session3SessionStart;
		private TimeSpan session3SessionEnd;
		private TimeSpan session3NoTradesAfter;
		private TimeSpan activeSessionStart;
		private TimeSpan activeSessionEnd;
		private TimeSpan activeNoTradesAfter;
		private TimeSpan effectiveSessionStart;
		private TimeSpan effectiveSessionEnd;
		private TimeSpan effectiveNoTradesAfter;
		private DateTime effectiveTimesDate;
		private DateTime lastSessionEvalTime;
		private bool sessionClosed;
		private bool sessionInitialized;
		private bool activeAutoShiftTimes;
		private SessionSlot activeSession;
		private TimeZoneInfo targetTimeZone;
		private TimeZoneInfo londonTimeZone;
		private bool useVolumeSmaFilter;
		private int volumeFastSmaPeriod;
		private int volumeSlowSmaPeriod;
		private double volumeSmaMultiplier;
		private SMA volumeFastSma;
		private SMA volumeSlowSma;
		private int htfBarsInProgress = -1;
		private BarsPeriodType htfBarsPeriodType = BarsPeriodType.Minute;
		private int htfBarsPeriodValue = 5;
		private int smtBarsInProgress = -1;
		private bool htfSeriesAdded;
		private bool smtSeriesAdded;
		private string smtInstrumentName;
		private int maxBarsBetweenSweepAndIfvg;
		private bool enableHistoricalTrading;
		private int lastEntryBar;
		private SweepEvent lastSwingSweep;
		private SweepEvent lastSessionSweep;
		private bool debugLogging;
		private bool verboseDebugLogging;
		private bool invalidateIfTargetHitBeforeEntry;
		private double minTpSlDistancePoints;
		private double londonMinTpSlDistancePoints;
		private double newYorkMinTpSlDistancePoints;
		private double activeMinTpSlDistancePoints;
		private bool exitOnCloseBeyondEntryIfvg;
		private MarketPosition lastMarketPosition;
		private int intrabarTargetBarIndex;
		private bool intrabarTargetHitLong;
		private bool intrabarTargetHitShort;
		private double intrabarTargetPriceLong;
		private double intrabarTargetPriceShort;
		private bool useBreakEvenWickLine;
		private bool antiHedge;
		private bool blockWhenInPosition;
		private bool breakEvenActive;
		private bool breakEvenTriggered;
		private bool breakEvenArmed;
		private double breakEvenPrice;
		private int breakEvenStartBarIndex;
		private int breakEvenEndBarIndex;
		private TradeDirection? breakEvenDirection;
		private string breakEvenTag;
		private string breakEvenLabelTag;
		private List<string> breakEvenTags;
		private Brush breakEvenLineBrush;
		private bool targetLineActive;
		private bool stopLineActive;
		private double targetLinePrice;
		private double stopLinePrice;
		private int targetLineStartBarIndex;
		private int stopLineStartBarIndex;
		private string targetLineTag;
		private string targetLineLabelTag;
		private string stopLineTag;
		private string stopLineLabelTag;
		private List<string> targetStopTags;
		private Brush targetLineBrush;
		private Brush stopLineBrush;
		private double entryIfvgLower;
		private double entryIfvgUpper;
		private TradeDirection? entryIfvgDirection;
		private string pendingTradeTag;
		private string activeTradeTag;
		private string lastTradeLabelPrinted;

		private enum TradeDirection
		{
			Long,
			Short
		}

		private enum SessionSlot
		{
			None,
			Session1,
			Session2,
			Session3
		}

		private class SweepEvent
		{
			public TradeDirection Direction;
			public double Price;
			public int BarIndex;
		}

		private class LiquidityLine
		{
			public string Tag;
			public string Label;
			public double Price;
			public int StartBarIndex;
			public int EndBarIndex;
			public bool IsActive;
			public bool IsArmed;
			public bool IsHigh;
			public Brush Brush;
			public DateTime SessionDate;
		}

		private class SessionLiquidityState
		{
			public string Name;
			public bool InSession;
			public int SessionStartBarIndex;
			public int SessionHighBarIndex;
			public int SessionLowBarIndex;
			public double SessionHigh;
			public double SessionLow;
			public DateTime SessionStartDate;
			public LiquidityLine HighLine;
			public LiquidityLine LowLine;
		}

		private class SwingLine
		{
			public string Tag;
			public double Price;
			public int StartBarIndex;
			public int EndBarIndex;
			public bool IsActive;
			public bool IsHigh;
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Calculate = Calculate.OnBarClose;
				Name = "iFVG";
				IsOverlay = true;
				fvgOpacity = 10;
				invalidatedOpacity = 10;
				showInvalidatedFvgs = true;
				minFvgSizePoints = 10;
				maxFvgSizePoints = 50;
				londonMinFvgSizePoints = 10;
				londonMaxFvgSizePoints = 50;
				newYorkMinFvgSizePoints = 10;
				newYorkMaxFvgSizePoints = 50;
				AsiaSessionStart = asiaSessionStart;
				AsiaSessionEnd = asiaSessionEnd;
				LondonSessionStart = londonSessionStart;
				LondonSessionEnd = londonSessionEnd;
				fvgDrawLimit = 2;
				combineFvgSeries = false;
				londonFvgDrawLimit = 2;
				londonCombineFvgSeries = false;
				newYorkFvgDrawLimit = 2;
				newYorkCombineFvgSeries = false;
				contracts = 1;
				tradeMonday = true;
				tradeTuesday = true;
				tradeWednesday = true;
				tradeThursday = true;
				tradeFriday = true;
				skipStart = new TimeSpan(0, 0, 0);
				skipEnd = new TimeSpan(0, 0, 0);
				closeAtSkipStart = true;
				sessionDrawLimit = 2;
				swingStrength = 10;
				londonSwingStrength = 10;
				newYorkSwingStrength = 10;
				swingDrawBars = 300;
				useSwingLiquiditySweep = true;
				useSessionLiquiditySweep = true;
				maxBarsBetweenSweepAndIfvg = 20;
				enableHistoricalTrading = false;
				useDeliverFromFvg = false;
				useDeliverFromHtfFvg = false;
				useSmt = false;
				useSession1 = true;
				useSession2 = true;
				useSession3 = true;
				autoShiftSession1 = false;
				autoShiftSession2 = true;
				autoShiftSession3 = false;
				closeAtSessionEnd = true;
				SessionBrush = Brushes.Gold;
				London_SessionStart = new TimeSpan(1, 30, 0);
				London_SessionEnd = new TimeSpan(5, 30, 0);
				London_NoTradesAfter = new TimeSpan(5, 0, 0);
				NewYork_SessionStart = new TimeSpan(9, 40, 0);
				NewYork_SessionEnd = new TimeSpan(15, 0, 0);
				NewYork_NoTradesAfter = new TimeSpan(14, 30, 0);
				NewYork_SkipStart = new TimeSpan(11, 45, 0);
				NewYork_SkipEnd = new TimeSpan(13, 15, 0);
				NewYork_CloseAtSkipStart = true;
				Asia_SessionStart = new TimeSpan(20, 0, 0);
				Asia_SessionEnd = new TimeSpan(0, 0, 0);
				Asia_NoTradesAfter = new TimeSpan(23, 30, 0);
				useVolumeSmaFilter = false;
				volumeFastSmaPeriod = 5;
				volumeSlowSmaPeriod = 100;
				volumeSmaMultiplier = 2.0;
				lastEntryBar = -1;
				debugLogging = false;
				verboseDebugLogging = false;
				invalidateIfTargetHitBeforeEntry = false;
				minTpSlDistancePoints = 0;
				londonMinTpSlDistancePoints = 0;
				newYorkMinTpSlDistancePoints = 0;
				exitOnCloseBeyondEntryIfvg = false;
				useBreakEvenWickLine = false;
				antiHedge = false;
				blockWhenInPosition = false;
				lastMarketPosition = MarketPosition.Flat;
				intrabarTargetBarIndex = -1;
				entryIfvgLower = 0;
				entryIfvgUpper = 0;
				entryIfvgDirection = null;
				breakEvenActive = false;
				breakEvenTriggered = false;
				breakEvenArmed = false;
				breakEvenPrice = 0;
				breakEvenStartBarIndex = -1;
				breakEvenEndBarIndex = -1;
				breakEvenDirection = null;
				breakEvenTag = null;
				breakEvenLabelTag = null;
				breakEvenTags = null;
				targetLineActive = false;
				stopLineActive = false;
				targetLinePrice = 0;
				stopLinePrice = 0;
				targetLineStartBarIndex = -1;
				stopLineStartBarIndex = -1;
				targetLineTag = null;
				targetLineLabelTag = null;
				stopLineTag = null;
				stopLineLabelTag = null;
				targetStopTags = null;
				pendingTradeTag = null;
				activeTradeTag = null;
				lastTradeLabelPrinted = null;
				sessionClosed = false;
				sessionInitialized = false;
				activeSession = SessionSlot.None;
				lastSessionEvalTime = DateTime.MinValue;
				effectiveTimesDate = DateTime.MinValue;
			}
			else if (State == State.Configure)
			{
				// Mirror SampleIntrabarBacktest style: add a 1-tick secondary series.
				AddDataSeries(Data.BarsPeriodType.Tick, 1);
				ConfigureHtfSeries();
				ConfigureSmtSeries();
			}
			else if (State == State.DataLoaded)
			{
				activeFvgs = new List<FvgBox>();
				breakEvenFvgs = new List<FvgBox>();
				activeHtfFvgs = new List<HtfFvgBox>();
				// Match DR.cs style: transparent outline, DodgerBlue fill, opacity handled by Draw.Rectangle.
				fvgFill = Brushes.DodgerBlue;
				if (fvgFill.CanFreeze)
					fvgFill.Freeze();
				htfFvgFill = Brushes.DarkCyan;
				if (htfFvgFill.CanFreeze)
					htfFvgFill.Freeze();
				htfLabelBrush = Brushes.DarkCyan;
				if (htfLabelBrush.CanFreeze)
					htfLabelBrush.Freeze();
				invalidatedFill = Brushes.Gray;
				if (invalidatedFill.CanFreeze)
					invalidatedFill.Freeze();
				asiaLineBrush = Brushes.DarkCyan;
				if (asiaLineBrush.CanFreeze)
					asiaLineBrush.Freeze();
				londonLineBrush = Brushes.DarkOrange;
				if (londonLineBrush.CanFreeze)
					londonLineBrush.Freeze();
				liquidityLines = new List<LiquidityLine>();
				asiaState = new SessionLiquidityState { Name = "Asia" };
				londonState = new SessionLiquidityState { Name = "London" };
				swingLineBrush = Brushes.DimGray;
				if (swingLineBrush.CanFreeze)
					swingLineBrush.Freeze();
				swingLines = new List<SwingLine>();
				breakEvenLineBrush = new SolidColorBrush(Color.FromArgb(128, 255, 165, 0));
				if (breakEvenLineBrush.CanFreeze)
					breakEvenLineBrush.Freeze();
				breakEvenTags = new List<string>();
				targetLineBrush = new SolidColorBrush(Color.FromArgb(128, 0, 128, 0));
				if (targetLineBrush.CanFreeze)
					targetLineBrush.Freeze();
				stopLineBrush = new SolidColorBrush(Color.FromArgb(128, 255, 0, 0));
				if (stopLineBrush.CanFreeze)
					stopLineBrush.Freeze();
				targetStopTags = new List<string>();
				volumeFastSma = SMA(Volume, VolumeFastSmaPeriod);
				volumeSlowSma = SMA(Volume, VolumeSlowSmaPeriod);
				ResolveSecondarySeriesIndexes();
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 1)
			{
				UpdateIntrabarTargetHit();
				CheckBreakEvenTrigger();
				return;
			}
			if (BarsInProgress == htfBarsInProgress)
			{
				if (UseDeliverFromHtfFvg)
					UpdateHtfFvgs();
				return;
			}
			if (BarsInProgress == smtBarsInProgress)
				return;

			if (BarsInProgress != 0)
				return;

			if (CurrentBar < 2)
				return;

			EnsureActiveSession(Time[0]);
			EnsureEffectiveTimes(Time[0], false);

			if (IsFirstTickOfBar && TimeInSession(Time[0]) && sessionClosed)
			{
				sessionClosed = false;
				LogDebug("New session started, state reset.");
				LogDebug(string.Format("Session start ({0})", activeSession));
			}

			DrawSessionBackground();

			bool crossedSessionEnd = CrossedSessionEnd(Time[1], Time[0]);
			bool isLastBarOfTradingSession = Bars.IsLastBarOfSession && !sessionClosed;

			if (crossedSessionEnd || isLastBarOfTradingSession)
			{
				if (CloseAtSessionEnd)
					FlattenAndCancel("SessionEnd");
				else if (Position.MarketPosition == MarketPosition.Flat)
					CancelAllOrders();

				sessionClosed = true;
			}

			bool crossedNoTradesAfter =
				activeSession != SessionSlot.None
				&& Time[1].TimeOfDay <= effectiveNoTradesAfter
				&& Time[0].TimeOfDay > effectiveNoTradesAfter;

			if (crossedNoTradesAfter)
			{
				LogDebug("NoTradesAfter crossed, canceling orders.");
				CancelAllOrders();
			}

			bool wasInGlobalSkip = IsInGlobalSkip(Time[1]);
			bool nowInGlobalSkip = IsInGlobalSkip(Time[0]);
			bool wasInNewYorkSkip = IsInNewYorkSkip(Time[1]);
			bool nowInNewYorkSkip = IsInNewYorkSkip(Time[0]);

			bool crossedSkipWindow =
				(wasInGlobalSkip != nowInGlobalSkip)
				|| (wasInNewYorkSkip != nowInNewYorkSkip);

			bool enteredGlobalSkip = !wasInGlobalSkip && nowInGlobalSkip;
			bool enteredNewYorkSkip = !wasInNewYorkSkip && nowInNewYorkSkip;
			bool shouldFlatten =
				(enteredGlobalSkip && CloseAtSkipStart)
				|| (enteredNewYorkSkip && NewYork_CloseAtSkipStart);

			if (crossedSkipWindow && shouldFlatten)
			{
				LogDebug("Skip window started, flattening/canceling.");
				if (Position.MarketPosition != MarketPosition.Flat)
					FlattenAndCancel("SkipWindow");
				else
					CancelAllOrders();
			}

			UpdateLiquidity();
			UpdateFvgs();
			if (UseDeliverFromHtfFvg)
				DrawActiveHtfFvgs();
			UpdateSwingLiquidity();
			UpdateBreakEvenLine();
			UpdateTargetStopLines();
			CheckExitOnCloseBeyondEntryIfvg();
		}

		private void UpdateFvgs()
		{
			if (activeFvgDrawLimit <= 0)
			{
				if (activeFvgs.Count > 0)
				{
					for (int i = 0; i < activeFvgs.Count; i++)
						RemoveDrawObject(activeFvgs[i].Tag);
					activeFvgs.Clear();
				}
				return;
			}

			PruneFvgs(activeFvgDrawLimit);
			PruneBreakEvenFvgs(activeFvgDrawLimit);
			UpdateActiveFvgs();
			UpdateBreakEvenFvgs();
			DetectNewFvg();
		}

		private void UpdateHtfFvgs()
		{
			if (!UseDeliverFromHtfFvg)
				return;
			if (htfBarsInProgress < 0 || CurrentBars[htfBarsInProgress] < 2)
				return;

			if (activeFvgDrawLimit > 0)
				PruneHtfFvgs(activeFvgDrawLimit);

			UpdateActiveHtfFvgs();
			DetectNewHtfFvg();
		}

		private void UpdateLiquidity()
		{
			if (SessionDrawLimit <= 0)
			{
				if (liquidityLines.Count > 0)
				{
					for (int i = 0; i < liquidityLines.Count; i++)
					{
						RemoveDrawObject(liquidityLines[i].Tag);
						RemoveDrawObject(liquidityLines[i].Tag + "_Lbl");
					}
					liquidityLines.Clear();
				}
				return;
			}

			PruneLiquidityLines(SessionDrawLimit);

			DateTime barTime = Time[0];
			ProcessSession(asiaState, barTime, AsiaSessionStart, AsiaSessionEnd, asiaLineBrush);
			ProcessSession(londonState, barTime, LondonSessionStart, LondonSessionEnd, londonLineBrush);
			UpdateLiquidityLines();
		}

		private void ConfigureHtfSeries()
		{
			if (BarsPeriod == null)
				return;

			int primaryMinutes = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? BarsPeriod.Value : 1;
			HtfMapping mapping = ResolveHtfMapping(primaryMinutes);
			htfBarsPeriodType = BarsPeriodType.Minute;
			htfBarsPeriodValue = mapping.HtfMinutes;
			if (htfBarsPeriodValue > 0)
			{
				AddDataSeries(htfBarsPeriodType, htfBarsPeriodValue);
				htfSeriesAdded = true;
			}
		}

		private void ConfigureSmtSeries()
		{
			string masterName = Instrument?.MasterInstrument?.Name;
			if (string.IsNullOrEmpty(masterName))
				return;

			if (!string.Equals(masterName, "MNQ", StringComparison.OrdinalIgnoreCase))
				return;

			string mesInstrument = ResolveMesInstrumentName();
			AddDataSeries(mesInstrument, BarsPeriod.BarsPeriodType, BarsPeriod.Value);
			smtInstrumentName = mesInstrument;
			smtSeriesAdded = true;
		}

		private string ResolveMesInstrumentName()
		{
			string fullName = Instrument.FullName;
			if (string.IsNullOrEmpty(fullName))
				return "MES";

			if (fullName.StartsWith("MNQ", StringComparison.OrdinalIgnoreCase))
			{
				if (fullName.Length > 3)
					return "MES" + fullName.Substring(3);
				return "MES";
			}

			return "MES";
		}

		private void ResolveSecondarySeriesIndexes()
		{
			if (BarsArray == null || BarsArray.Length == 0)
				return;

			string masterName = Instrument?.MasterInstrument?.Name;

			htfBarsInProgress = -1;
			smtBarsInProgress = -1;

			for (int i = 0; i < BarsArray.Length; i++)
			{
				Bars bars = BarsArray[i];
				if (bars == null)
					continue;

				if (htfSeriesAdded && htfBarsInProgress < 0)
				{
					if (bars.BarsPeriod.BarsPeriodType == htfBarsPeriodType
						&& bars.BarsPeriod.Value == htfBarsPeriodValue
						&& !string.IsNullOrEmpty(masterName)
						&& string.Equals(bars.Instrument?.MasterInstrument?.Name, masterName, StringComparison.OrdinalIgnoreCase))
					{
						htfBarsInProgress = i;
					}
				}

				if (smtSeriesAdded && smtBarsInProgress < 0)
				{
					if (bars.BarsPeriod.BarsPeriodType == BarsPeriod.BarsPeriodType
						&& bars.BarsPeriod.Value == BarsPeriod.Value
						&& !string.IsNullOrEmpty(smtInstrumentName)
						&& string.Equals(bars.Instrument?.MasterInstrument?.Name, smtInstrumentName, StringComparison.OrdinalIgnoreCase))
					{
						smtBarsInProgress = i;
					}
				}
			}

			if (DebugLogging && (htfSeriesAdded || smtSeriesAdded) && (htfBarsInProgress < 0 || smtBarsInProgress < 0))
				Print(string.Format("iFVG: secondary series index not resolved (HTF={0}, SMT={1}).", htfBarsInProgress, smtBarsInProgress));
		}

		private class HtfMapping
		{
			public int MaxPrimaryMinutes;
			public int HtfMinutes;
		}

		private HtfMapping ResolveHtfMapping(int primaryMinutes)
		{
			HtfMapping[] mappings = new[]
			{
				new HtfMapping { MaxPrimaryMinutes = 1, HtfMinutes = 5 },
				new HtfMapping { MaxPrimaryMinutes = int.MaxValue, HtfMinutes = 60 }
			};

			for (int i = 0; i < mappings.Length; i++)
			{
				if (primaryMinutes <= mappings[i].MaxPrimaryMinutes)
					return mappings[i];
			}

			return mappings[mappings.Length - 1];
		}

		private string GetHtfLabel()
		{
			if (htfBarsPeriodType != BarsPeriodType.Minute || htfBarsPeriodValue <= 0)
				return string.Empty;
			if (htfBarsPeriodValue >= 60 && htfBarsPeriodValue % 60 == 0)
			{
				int hours = htfBarsPeriodValue / 60;
				return string.Format("{0}H FVG", hours);
			}
			return string.Format("{0}m FVG", htfBarsPeriodValue);
		}

		private void ProcessSession(
			SessionLiquidityState state,
			DateTime barTime,
			TimeSpan sessionStart,
			TimeSpan sessionEnd,
			Brush lineBrush)
		{
			bool inSession = IsTimeInRange(barTime.TimeOfDay, sessionStart, sessionEnd);

			if (!state.InSession && inSession)
			{
				state.InSession = true;
				state.SessionStartDate = GetSessionStartDate(barTime, sessionStart, sessionEnd);
				state.SessionStartBarIndex = CurrentBar;
				state.SessionHighBarIndex = CurrentBar;
				state.SessionLowBarIndex = CurrentBar;
				state.SessionHigh = High[0];
				state.SessionLow = Low[0];
				state.HighLine = CreateLiquidityLine(state, true, lineBrush);
				state.LowLine = CreateLiquidityLine(state, false, lineBrush);
			}

			if (inSession)
			{
				if (High[0] > state.SessionHigh)
				{
					state.SessionHigh = High[0];
					state.SessionHighBarIndex = CurrentBar;
					if (state.HighLine != null)
					{
						state.HighLine.Price = state.SessionHigh;
						state.HighLine.StartBarIndex = state.SessionHighBarIndex;
					}
				}

				if (Low[0] < state.SessionLow)
				{
					state.SessionLow = Low[0];
					state.SessionLowBarIndex = CurrentBar;
					if (state.LowLine != null)
					{
						state.LowLine.Price = state.SessionLow;
						state.LowLine.StartBarIndex = state.SessionLowBarIndex;
					}
				}

				if (state.HighLine != null)
					state.HighLine.EndBarIndex = CurrentBar;
				if (state.LowLine != null)
					state.LowLine.EndBarIndex = CurrentBar;
			}

			if (state.InSession && !inSession)
			{
				state.InSession = false;
				if (state.HighLine != null)
					state.HighLine.IsArmed = true;
				if (state.LowLine != null)
					state.LowLine.IsArmed = true;
			}
		}

		private LiquidityLine CreateLiquidityLine(SessionLiquidityState state, bool isHigh, Brush lineBrush)
		{
			string tag = string.Format(
				"iFVG_{0}_{1}_{2:yyyyMMdd}",
				state.Name,
				isHigh ? "High" : "Low",
				state.SessionStartDate);

			LiquidityLine line = new LiquidityLine
			{
				Tag = tag,
				Label = GetLiquidityLabel(state.Name, isHigh),
				Price = isHigh ? state.SessionHigh : state.SessionLow,
				StartBarIndex = isHigh ? state.SessionHighBarIndex : state.SessionLowBarIndex,
				EndBarIndex = CurrentBar,
				IsActive = true,
				IsArmed = false,
				IsHigh = isHigh,
				Brush = lineBrush,
				SessionDate = state.SessionStartDate.Date
			};

			liquidityLines.Add(line);
			return line;
		}

		private void UpdateLiquidityLines()
		{
			for (int i = 0; i < liquidityLines.Count; i++)
			{
				LiquidityLine line = liquidityLines[i];
				if (!line.IsActive)
					continue;

				bool hit = line.IsArmed &&
					(line.IsHigh ? High[0] >= line.Price : Low[0] <= line.Price);

				line.EndBarIndex = CurrentBar;

				int startBarsAgo = CurrentBar - line.StartBarIndex;
				int endBarsAgo = CurrentBar - line.EndBarIndex;
				if (startBarsAgo < 0)
					startBarsAgo = 0;
				if (endBarsAgo < 0)
					endBarsAgo = 0;

				Draw.Line(
					this,
					line.Tag,
					false,
					startBarsAgo,
					line.Price,
					endBarsAgo,
					line.Price,
					line.Brush,
					DashStyleHelper.Solid,
					1
				);

				Draw.Text(
					this,
					line.Tag + "_Lbl",
					false,
					line.Label,
					endBarsAgo,
					line.Price,
					0,
					line.Brush,
					new SimpleFont("Arial", 10),
					TextAlignment.Left,
					Brushes.Transparent,
					Brushes.Transparent,
					0
				);

				if (hit)
				{
					RegisterSweep(line.IsHigh ? TradeDirection.Short : TradeDirection.Long, line.Price, true);
					line.IsActive = false;
					RemoveDrawObject(line.Tag + "_Lbl");
				}
			}
		}

		private void RegisterSweep(TradeDirection direction, double price, bool isSession)
		{
			SweepEvent existing = isSession ? lastSessionSweep : lastSwingSweep;
			if (existing != null &&
				existing.Direction == direction &&
				existing.Price == price &&
				existing.BarIndex == CurrentBar)
				return;

			SweepEvent sweep = new SweepEvent
			{
				Direction = direction,
				Price = price,
				BarIndex = CurrentBar
			};

			if (isSession)
				lastSessionSweep = sweep;
			else
				lastSwingSweep = sweep;

			LogVerbose(string.Format(
				"Sweep registered {0} price={1} bar={2} source={3}",
				direction,
				price,
				CurrentBar,
				isSession ? "session" : "swing"));
		}

		private SweepEvent GetEligibleSweep(TradeDirection direction, string fvgTag)
		{
			SweepEvent best = null;

			if (useSessionLiquiditySweep && lastSessionSweep != null && lastSessionSweep.Direction == direction)
				best = lastSessionSweep;

			if (useSwingLiquiditySweep && lastSwingSweep != null && lastSwingSweep.Direction == direction)
			{
				if (best == null || lastSwingSweep.BarIndex > best.BarIndex)
					best = lastSwingSweep;
			}

			if (best == null)
			{
				LogTrade(fvgTag, string.Format("BLOCKED (SweepMissing: {0})", direction), false);
				return null;
			}

			int barsSince = CurrentBar - best.BarIndex;
			if (barsSince < 0 || barsSince > maxBarsBetweenSweepAndIfvg)
			{
				LogTrade(fvgTag, string.Format(
					"BLOCKED (SweepExpired: {0} barsSince={1} max={2})",
					direction,
					barsSince,
					maxBarsBetweenSweepAndIfvg), false);
				return null;
			}

			LogTrade(fvgTag, string.Format(
				"Eligible sweep {0} price={1} bar={2} barsSince={3}",
				direction,
				best.Price,
				best.BarIndex,
				barsSince), false);
			return best;
		}

		private double? GetClosestLiquidityTarget(TradeDirection direction, double entryPrice)
		{
			double? best = null;

			for (int i = 0; i < liquidityLines.Count; i++)
			{
				LiquidityLine line = liquidityLines[i];
				if (!line.IsActive)
					continue;
				if (direction == TradeDirection.Long && line.IsHigh)
				{
					if (line.Price > entryPrice && (!best.HasValue || line.Price < best.Value))
						best = line.Price;
				}
				else if (direction == TradeDirection.Short && !line.IsHigh)
				{
					if (line.Price < entryPrice && (!best.HasValue || line.Price > best.Value))
						best = line.Price;
				}
			}

			for (int i = 0; i < swingLines.Count; i++)
			{
				SwingLine line = swingLines[i];
				if (!line.IsActive)
					continue;
				if (direction == TradeDirection.Long && line.IsHigh)
				{
					if (line.Price > entryPrice && (!best.HasValue || line.Price < best.Value))
						best = line.Price;
				}
				else if (direction == TradeDirection.Short && !line.IsHigh)
				{
					if (line.Price < entryPrice && (!best.HasValue || line.Price > best.Value))
						best = line.Price;
				}
			}

			return best;
		}

		private double? GetClosestStopLevel(TradeDirection direction, double entryPrice)
		{
			double? best = null;

			for (int i = 0; i < liquidityLines.Count; i++)
			{
				LiquidityLine line = liquidityLines[i];
				if (!line.IsActive)
					continue;
				if (direction == TradeDirection.Long && !line.IsHigh)
				{
					if (line.Price < entryPrice && (!best.HasValue || line.Price > best.Value))
						best = line.Price;
				}
				else if (direction == TradeDirection.Short && line.IsHigh)
				{
					if (line.Price > entryPrice && (!best.HasValue || line.Price < best.Value))
						best = line.Price;
				}
			}

			for (int i = 0; i < swingLines.Count; i++)
			{
				SwingLine line = swingLines[i];
				if (!line.IsActive)
					continue;
				if (direction == TradeDirection.Long && !line.IsHigh)
				{
					if (line.Price < entryPrice && (!best.HasValue || line.Price > best.Value))
						best = line.Price;
				}
				else if (direction == TradeDirection.Short && line.IsHigh)
				{
					if (line.Price > entryPrice && (!best.HasValue || line.Price < best.Value))
						best = line.Price;
				}
			}

			return best;
		}

		private bool TryGetDeliveringFvg(SweepEvent sweep, string excludedFvgTag, out FvgBox delivering)
		{
			delivering = null;
			if (sweep == null || activeFvgs == null || activeFvgs.Count == 0)
				return false;

			for (int i = 0; i < activeFvgs.Count; i++)
			{
				FvgBox fvg = activeFvgs[i];
				if (!fvg.IsActive)
					continue;
				if (!string.IsNullOrEmpty(excludedFvgTag) && fvg.Tag == excludedFvgTag)
					continue;
				if (sweep.BarIndex < fvg.CreatedBarIndex)
					continue;
				if (sweep.BarIndex < fvg.StartBarIndex || sweep.BarIndex > fvg.EndBarIndex)
					continue;
				if (sweep.Price < fvg.Lower || sweep.Price > fvg.Upper)
					continue;

				delivering = fvg;
				return true;
			}

			return false;
		}

		private bool TryGetDeliveringHtfFvg(SweepEvent sweep, out HtfFvgBox delivering)
		{
			delivering = null;
			if (sweep == null || activeHtfFvgs == null || activeHtfFvgs.Count == 0)
				return false;

			int barsAgo = CurrentBar - sweep.BarIndex;
			if (barsAgo < 0 || barsAgo > CurrentBar)
				return false;

			DateTime sweepTime = Times[0][barsAgo];

			for (int i = 0; i < activeHtfFvgs.Count; i++)
			{
				HtfFvgBox fvg = activeHtfFvgs[i];
				if (!fvg.IsActive)
					continue;
				if (sweepTime < fvg.CreatedTime)
					continue;
				if (sweepTime < fvg.StartTime)
					continue;
				if (sweep.Price < fvg.Lower || sweep.Price > fvg.Upper)
					continue;

				delivering = fvg;
				return true;
			}

			return false;
		}

		private bool TryGetLastTwoSwingHighs(int barsInProgress, int strength, out double lastHigh, out double prevHigh, out int lastBarsAgo, out int prevBarsAgo, out int foundCount)
		{
			lastHigh = 0;
			prevHigh = 0;
			lastBarsAgo = -1;
			prevBarsAgo = -1;
			foundCount = 0;

			if (CurrentBars[barsInProgress] < strength * 2)
				return false;

			int found = 0;
			for (int barsAgo = strength; barsAgo + strength <= CurrentBars[barsInProgress]; barsAgo++)
			{
				double value = Highs[barsInProgress][barsAgo];
				bool isSwing = true;
				for (int i = 1; i <= strength; i++)
				{
					if (value <= Highs[barsInProgress][barsAgo - i] || value <= Highs[barsInProgress][barsAgo + i])
					{
						isSwing = false;
						break;
					}
				}
				if (!isSwing)
					continue;

				if (found == 0)
				{
					lastHigh = value;
					lastBarsAgo = barsAgo;
					found++;
					foundCount = found;
				}
				else
				{
					prevHigh = value;
					prevBarsAgo = barsAgo;
					foundCount = 2;
					return true;
				}
			}

			return false;
		}

		private bool TryGetLastTwoSwingLows(int barsInProgress, int strength, out double lastLow, out double prevLow, out int lastBarsAgo, out int prevBarsAgo, out int foundCount)
		{
			lastLow = 0;
			prevLow = 0;
			lastBarsAgo = -1;
			prevBarsAgo = -1;
			foundCount = 0;

			if (CurrentBars[barsInProgress] < strength * 2)
				return false;

			int found = 0;
			for (int barsAgo = strength; barsAgo + strength <= CurrentBars[barsInProgress]; barsAgo++)
			{
				double value = Lows[barsInProgress][barsAgo];
				bool isSwing = true;
				for (int i = 1; i <= strength; i++)
				{
					if (value >= Lows[barsInProgress][barsAgo - i] || value >= Lows[barsInProgress][barsAgo + i])
					{
						isSwing = false;
						break;
					}
				}
				if (!isSwing)
					continue;

				if (found == 0)
				{
					lastLow = value;
					lastBarsAgo = barsAgo;
					found++;
					foundCount = found;
				}
				else
				{
					prevLow = value;
					prevBarsAgo = barsAgo;
					foundCount = 2;
					return true;
				}
			}

			return false;
		}

		private bool TryGetSmtSignal(TradeDirection direction, out string details)
		{
			details = string.Empty;
			if (smtBarsInProgress < 0)
			{
				details = "SMT missing MES series";
				return false;
			}

			double mnqLast;
			double mnqPrev;
			double mesLast;
			double mesPrev;
			int mnqLastAgo;
			int mnqPrevAgo;
			int mesLastAgo;
			int mesPrevAgo;
			int mnqCount;
			int mesCount;

			if (direction == TradeDirection.Short)
			{
				bool hasMnq = TryGetLastTwoSwingHighs(0, activeSwingStrength, out mnqLast, out mnqPrev, out mnqLastAgo, out mnqPrevAgo, out mnqCount);
				bool hasMes = TryGetLastTwoSwingHighs(smtBarsInProgress, activeSwingStrength, out mesLast, out mesPrev, out mesLastAgo, out mesPrevAgo, out mesCount);
				if (!hasMnq || !hasMes)
				{
					details = string.Format("SMT insufficient swing highs (MNQ={0} MES={1})", mnqCount, mesCount);
					return false;
				}

				bool mesHigherHigh = mesLast > mesPrev;
				bool mnqHigherHigh = mnqLast > mnqPrev;
				if (mesHigherHigh && !mnqHigherHigh)
				{
					details = string.Format("SMT bearish MES {0}>{1} MNQ {2}<={3}", mesLast, mesPrev, mnqLast, mnqPrev);
					DrawSmtLines(direction, mnqLast, mnqLastAgo, mnqPrev, mnqPrevAgo, mesLast, mesLastAgo, mesPrev, mesPrevAgo);
					return true;
				}

				details = string.Format("SMT no bearish MES {0}>{1} MNQ {2}>{3}", mesLast, mesPrev, mnqLast, mnqPrev);
				return false;
			}

			bool hasMnqLow = TryGetLastTwoSwingLows(0, activeSwingStrength, out mnqLast, out mnqPrev, out mnqLastAgo, out mnqPrevAgo, out mnqCount);
			bool hasMesLow = TryGetLastTwoSwingLows(smtBarsInProgress, activeSwingStrength, out mesLast, out mesPrev, out mesLastAgo, out mesPrevAgo, out mesCount);
			if (!hasMnqLow || !hasMesLow)
			{
				details = string.Format("SMT insufficient swing lows (MNQ={0} MES={1})", mnqCount, mesCount);
				return false;
			}

			bool mesLowerLow = mesLast < mesPrev;
			bool mnqLowerLow = mnqLast < mnqPrev;
			if (mesLowerLow && !mnqLowerLow)
			{
				details = string.Format("SMT bullish MES {0}<{1} MNQ {2}>={3}", mesLast, mesPrev, mnqLast, mnqPrev);
				DrawSmtLines(direction, mnqLast, mnqLastAgo, mnqPrev, mnqPrevAgo, mesLast, mesLastAgo, mesPrev, mesPrevAgo);
				return true;
			}

			details = string.Format("SMT no bullish MES {0}<{1} MNQ {2}<{3}", mesLast, mesPrev, mnqLast, mnqPrev);
			return false;
		}

		private void DrawSmtLines(TradeDirection direction, double mnqLastPrice, int mnqLastBarsAgo, double mnqPrevPrice, int mnqPrevBarsAgo, double mesLastPrice, int mesLastBarsAgo, double mesPrevPrice, int mesPrevBarsAgo)
		{
			if (mnqLastBarsAgo < 0 || mnqPrevBarsAgo < 0 || mesLastBarsAgo < 0 || mesPrevBarsAgo < 0)
				return;

			Color smtColor = Color.FromArgb(140, 255, 140, 0);
			SolidColorBrush smtBrush = new SolidColorBrush(smtColor);
			smtBrush.Freeze();

			int mnqMidBarsAgo = (mnqPrevBarsAgo + mnqLastBarsAgo) / 2;
			double mnqMidPrice = (mnqPrevPrice + mnqLastPrice) / 2.0;

			DateTime mesLastTime = Times[smtBarsInProgress][mesLastBarsAgo];
			DateTime mesPrevTime = Times[smtBarsInProgress][mesPrevBarsAgo];
			int mesLastPrimaryBar = BarsArray[0].GetBar(mesLastTime);
			int mesPrevPrimaryBar = BarsArray[0].GetBar(mesPrevTime);
			if (mesLastPrimaryBar < 0 || mesPrevPrimaryBar < 0)
				return;
			int mesLastBarsAgoPrimary = CurrentBar - mesLastPrimaryBar;
			int mesPrevBarsAgoPrimary = CurrentBar - mesPrevPrimaryBar;
			if (mesLastBarsAgoPrimary < 0 || mesPrevBarsAgoPrimary < 0)
				return;

			string tagBase = string.Format("iFVG_SMT_{0}_{1:yyyyMMdd_HHmmss}", direction, Time[0]);

			Draw.Line(
				this,
				tagBase + "_MNQ",
				false,
				mnqPrevBarsAgo,
				mnqPrevPrice,
				mnqLastBarsAgo,
				mnqLastPrice,
				smtBrush,
				DashStyleHelper.Dot,
				2
			);

			Draw.Line(
				this,
				tagBase + "_MES",
				false,
				mesPrevBarsAgoPrimary,
				mesPrevPrice,
				mesLastBarsAgoPrimary,
				mesLastPrice,
				smtBrush,
				DashStyleHelper.Dot,
				2
			);

			Draw.Text(
				this,
				tagBase + "_Lbl",
				false,
				"SMT",
				Time[mnqMidBarsAgo],
				mnqMidPrice,
				0,
				smtBrush,
				new SimpleFont("Arial", 10),
				TextAlignment.Center,
				Brushes.Transparent,
				Brushes.Transparent,
				0
			);
		}

		private void UpdateBreakEvenLine()
		{
			if (!UseBreakEvenWickLine)
			{
				if (breakEvenActive)
					ClearBreakEvenLine();
				return;
			}
			if (breakEvenEndBarIndex >= 0)
				return;
			if (!breakEvenActive || string.IsNullOrEmpty(breakEvenTag))
				return;

			int startBarsAgo = CurrentBar - breakEvenStartBarIndex;
			if (startBarsAgo < 0)
				startBarsAgo = 0;

			Draw.Line(
				this,
				breakEvenTag,
				false,
				startBarsAgo,
				breakEvenPrice,
				0,
				breakEvenPrice,
				breakEvenLineBrush,
				DashStyleHelper.Solid,
				1
			);

			Draw.Text(
				this,
				breakEvenLabelTag,
				false,
				"BE",
				0,
				breakEvenPrice,
				0,
				breakEvenLineBrush,
				new SimpleFont("Arial", 10),
				TextAlignment.Left,
				Brushes.Transparent,
				Brushes.Transparent,
				0
			);
		}

		private void UpdateTargetStopLines()
		{
			if (!targetLineActive && !stopLineActive)
				return;

			if (targetLineActive && !string.IsNullOrEmpty(targetLineTag))
			{
				int startBarsAgo = CurrentBar - targetLineStartBarIndex;
				if (startBarsAgo < 0)
					startBarsAgo = 0;

				Draw.Line(
					this,
					targetLineTag,
					false,
					startBarsAgo,
					targetLinePrice,
					0,
					targetLinePrice,
					targetLineBrush,
					DashStyleHelper.Solid,
					1
				);

				Draw.Text(
					this,
					targetLineLabelTag,
					false,
					"TP",
					0,
					targetLinePrice,
					0,
					targetLineBrush,
					new SimpleFont("Arial", 10),
					TextAlignment.Left,
					Brushes.Transparent,
					Brushes.Transparent,
					0
				);
			}

			if (stopLineActive && !string.IsNullOrEmpty(stopLineTag))
			{
				int startBarsAgo = CurrentBar - stopLineStartBarIndex;
				if (startBarsAgo < 0)
					startBarsAgo = 0;

				Draw.Line(
					this,
					stopLineTag,
					false,
					startBarsAgo,
					stopLinePrice,
					0,
					stopLinePrice,
					stopLineBrush,
					DashStyleHelper.Solid,
					1
				);

				Draw.Text(
					this,
					stopLineLabelTag,
					false,
					"SL",
					0,
					stopLinePrice,
					0,
					stopLineBrush,
					new SimpleFont("Arial", 10),
					TextAlignment.Left,
					Brushes.Transparent,
					Brushes.Transparent,
					0
				);
			}
		}

		private void ClearBreakEvenLine()
		{
			RemoveBreakEvenDrawObjects();
			breakEvenActive = false;
			breakEvenTriggered = false;
			breakEvenArmed = false;
			breakEvenPrice = 0;
			breakEvenStartBarIndex = -1;
			breakEvenEndBarIndex = -1;
			breakEvenDirection = null;
			breakEvenTag = null;
			breakEvenLabelTag = null;
		}

		private void ClearTargetStopLines()
		{
			RemoveTargetStopDrawObjects();
			targetLineActive = false;
			stopLineActive = false;
			targetLinePrice = 0;
			stopLinePrice = 0;
			targetLineStartBarIndex = -1;
			stopLineStartBarIndex = -1;
			targetLineTag = null;
			targetLineLabelTag = null;
			stopLineTag = null;
			stopLineLabelTag = null;
		}

		private void RemoveBreakEvenDrawObjects()
		{
			if (!string.IsNullOrEmpty(breakEvenTag))
				RemoveDrawObject(breakEvenTag);
			if (!string.IsNullOrEmpty(breakEvenLabelTag))
				RemoveDrawObject(breakEvenLabelTag);
			if (breakEvenTags == null || breakEvenTags.Count == 0)
				return;
			for (int i = 0; i < breakEvenTags.Count; i++)
				RemoveDrawObject(breakEvenTags[i]);
			breakEvenTags.Clear();
		}

		private void RemoveTargetStopDrawObjects()
		{
			if (!string.IsNullOrEmpty(targetLineTag))
				RemoveDrawObject(targetLineTag);
			if (!string.IsNullOrEmpty(targetLineLabelTag))
				RemoveDrawObject(targetLineLabelTag);
			if (!string.IsNullOrEmpty(stopLineTag))
				RemoveDrawObject(stopLineTag);
			if (!string.IsNullOrEmpty(stopLineLabelTag))
				RemoveDrawObject(stopLineLabelTag);
			if (targetStopTags == null || targetStopTags.Count == 0)
				return;
			for (int i = 0; i < targetStopTags.Count; i++)
				RemoveDrawObject(targetStopTags[i]);
			targetStopTags.Clear();
		}

		private void DrawBreakEvenLineFixed(DateTime endTime)
		{
			if (string.IsNullOrEmpty(breakEvenTag))
				return;

			int startBarsAgo = CurrentBar - breakEvenStartBarIndex;
			if (startBarsAgo < 0)
				startBarsAgo = 0;

			DateTime startTime = Times[0][startBarsAgo];

			Draw.Line(
				this,
				breakEvenTag,
				false,
				startTime,
				breakEvenPrice,
				endTime,
				breakEvenPrice,
				breakEvenLineBrush,
				DashStyleHelper.Solid,
				1
			);

			Draw.Text(
				this,
				breakEvenLabelTag,
				false,
				"BE",
				endTime,
				breakEvenPrice,
				0,
				breakEvenLineBrush,
				new SimpleFont("Arial", 10),
				TextAlignment.Left,
				Brushes.Transparent,
				Brushes.Transparent,
				0
			);
		}

		private void FinalizeBreakEvenLine()
		{
			if (!breakEvenActive)
				return;
			ClearBreakEvenLine();
		}

		private void ActivateBreakEvenLine(string fvgTag, TradeDirection direction, double price, int barsAgo)
		{
			breakEvenActive = true;
			breakEvenTriggered = false;
			breakEvenArmed = true;
			breakEvenPrice = price;
			breakEvenStartBarIndex = CurrentBar - barsAgo;
			breakEvenEndBarIndex = -1;
			breakEvenDirection = direction;
			breakEvenTag = string.Format("{0}_BE", fvgTag);
			breakEvenLabelTag = breakEvenTag + "_Lbl";
			if (breakEvenTags != null)
			{
				breakEvenTags.Add(breakEvenTag);
				breakEvenTags.Add(breakEvenLabelTag);
			}
			UpdateBreakEvenLine();
		}

		private void ActivateTargetStopLines(string fvgTag, double targetPrice, int targetBarsAgo, double stopPrice, int stopBarsAgo)
		{
			ClearTargetStopLines();
			targetLineActive = true;
			stopLineActive = true;
			targetLinePrice = targetPrice;
			stopLinePrice = stopPrice;
			targetLineStartBarIndex = CurrentBar - targetBarsAgo;
			stopLineStartBarIndex = CurrentBar - stopBarsAgo;
			targetLineTag = string.Format("{0}_TP", fvgTag);
			targetLineLabelTag = targetLineTag + "_Lbl";
			stopLineTag = string.Format("{0}_SL", fvgTag);
			stopLineLabelTag = stopLineTag + "_Lbl";
			if (targetStopTags != null)
			{
				targetStopTags.Add(targetLineTag);
				targetStopTags.Add(targetLineLabelTag);
				targetStopTags.Add(stopLineTag);
				targetStopTags.Add(stopLineLabelTag);
			}
			UpdateTargetStopLines();
		}

		private void CheckBreakEvenTrigger()
		{
			if (!UseBreakEvenWickLine || !breakEvenActive || breakEvenTriggered)
				return;
			if (Position.MarketPosition == MarketPosition.Flat || breakEvenDirection == null)
				return;

			double high = BarsInProgress == 1 ? Highs[1][0] : High[0];
			double low = BarsInProgress == 1 ? Lows[1][0] : Low[0];
			bool hit = breakEvenDirection == TradeDirection.Long
				? high >= breakEvenPrice
				: low <= breakEvenPrice;

			if (!hit)
				return;

			breakEvenTriggered = true;
			string signalName = breakEvenDirection == TradeDirection.Long ? "Long" : "Short";
			double entryPrice = Position.AveragePrice;
			SetStopLoss(signalName, CalculationMode.Price, entryPrice, false);
			LogTrade(activeTradeTag, string.Format("BE HIT move SL to entry={0}", entryPrice), true);
		}

		private bool TryGetNthBullishPivotHighAboveEntry(double entryPrice, int occurrence, double minDistancePoints, out double price, out int barsAgo)
		{
			price = 0;
			barsAgo = -1;
			int found = 0;

			for (int i = 1; i + 1 <= CurrentBars[0]; i++)
			{
				if (Highs[0][i] <= Highs[0][i - 1] || Highs[0][i] <= Highs[0][i + 1])
					continue;
				if (Highs[0][i] <= entryPrice)
					continue;
				if (minDistancePoints > 0 && (Highs[0][i] - entryPrice) < minDistancePoints)
					continue;

				found++;
				if (found == occurrence)
				{
					price = Highs[0][i];
					barsAgo = i;
					return true;
				}
			}

			return false;
		}

		private bool TryGetNthBearishPivotLowBelowEntry(double entryPrice, int occurrence, double minDistancePoints, out double price, out int barsAgo)
		{
			price = 0;
			barsAgo = -1;
			int found = 0;

			for (int i = 1; i + 1 <= CurrentBars[0]; i++)
			{
				if (Lows[0][i] >= Lows[0][i - 1] || Lows[0][i] >= Lows[0][i + 1])
					continue;
				if (Lows[0][i] >= entryPrice)
					continue;
				if (minDistancePoints > 0 && (entryPrice - Lows[0][i]) < minDistancePoints)
					continue;

				found++;
				if (found == occurrence)
				{
					price = Lows[0][i];
					barsAgo = i;
					return true;
				}
			}

			return false;
		}

		private bool TryGetBreakEvenFromFvg(TradeDirection direction, double entryPrice, double pivotPrice, out double bePrice, out int beBarsAgo)
		{
			bePrice = 0;
			beBarsAgo = -1;

			if (breakEvenFvgs == null || breakEvenFvgs.Count == 0)
				return false;

			bool found = false;
			double bestPrice = 0;
			int bestBarsAgo = -1;

			for (int i = 0; i < breakEvenFvgs.Count; i++)
			{
				FvgBox fvg = breakEvenFvgs[i];
				if (!fvg.IsActive)
					continue;

				double candidatePrice = direction == TradeDirection.Long ? fvg.Lower : fvg.Upper;
				if (direction == TradeDirection.Long)
				{
					if (candidatePrice <= entryPrice || candidatePrice >= pivotPrice)
						continue;
					if (!found || candidatePrice < bestPrice)
					{
						found = true;
						bestPrice = candidatePrice;
						bestBarsAgo = CurrentBar - fvg.CreatedBarIndex;
					}
				}
				else
				{
					if (candidatePrice >= entryPrice || candidatePrice <= pivotPrice)
						continue;
					if (!found || candidatePrice > bestPrice)
					{
						found = true;
						bestPrice = candidatePrice;
						bestBarsAgo = CurrentBar - fvg.CreatedBarIndex;
					}
				}
			}

			if (!found)
				return false;

			bePrice = Instrument.MasterInstrument.RoundToTickSize(bestPrice);
			beBarsAgo = bestBarsAgo;
			return true;
		}


		private void TryEnterFromIfvg(TradeDirection direction, double fvgLower, double fvgUpper, string fvgTag)
		{
			if (State == State.Historical && !EnableHistoricalTrading)
			{
				LogTrade(fvgTag, "BLOCKED (HistoricalDisabled)", false);
				return;
			}
			if (!IsTradingDayAllowed(Time[0]))
			{
				LogTrade(fvgTag, "BLOCKED (DayDisabled)", false);
				return;
			}
			if (ShouldBlockForHedge(direction, fvgTag))
				return;
			if (lastEntryBar == CurrentBar)
			{
				LogTrade(fvgTag, "BLOCKED (AlreadyEnteredThisBar)", false);
				return;
			}

			if (!TimeInSession(Time[0]))
			{
				LogTrade(fvgTag, "BLOCKED (OutsideSession)", false);
				return;
			}

			if (TimeInNoTradesAfter(Time[0]))
			{
				LogTrade(fvgTag, "BLOCKED (NoTradesAfter)", false);
				return;
			}
			if (TimeInSkip(Time[0]))
			{
				LogTrade(fvgTag, "BLOCKED (SkipWindow)", false);
				return;
			}

			SweepEvent sweep = GetEligibleSweep(direction, fvgTag);
			if (sweep == null)
				return;

			if (UseDeliverFromFvg)
			{
				FvgBox deliveringFvg;
				if (!TryGetDeliveringFvg(sweep, fvgTag, out deliveringFvg))
				{
					LogTrade(fvgTag, string.Format("BLOCKED (DeliverFromFVG: sweep={0} bar={1})", sweep.Price, sweep.BarIndex), false);
					return;
				}
				LogTrade(fvgTag, string.Format("Filter DeliverFromFVG ok tag={0} sweep={1} bar={2}", deliveringFvg.Tag, sweep.Price, sweep.BarIndex), false);
			}

			if (UseDeliverFromHtfFvg)
			{
				HtfFvgBox deliveringHtf;
				if (!TryGetDeliveringHtfFvg(sweep, out deliveringHtf))
				{
					LogTrade(fvgTag, string.Format("BLOCKED (DeliverFromHTF: sweep={0} bar={1})", sweep.Price, sweep.BarIndex), false);
					return;
				}
				LogTrade(fvgTag, string.Format("Filter DeliverFromHTF ok tag={0} sweep={1} bar={2}", deliveringHtf.Tag, sweep.Price, sweep.BarIndex), false);
			}

			if (UseSmt)
			{
				string smtDetails;
				if (!TryGetSmtSignal(direction, out smtDetails))
				{
					LogTrade(fvgTag, string.Format("BLOCKED ({0})", smtDetails), false);
					return;
				}
				LogTrade(fvgTag, smtDetails, false);
			}

			if (!PassesVolumeSmaFilter(fvgTag))
				return;

			double entryPrice = Close[0];
			const int slOccurrence = 1;
			double firstTargetPrice;
			int firstTargetBarsAgo;
			bool hasFirstTarget = direction == TradeDirection.Long
				? TryGetNthBullishPivotHighAboveEntry(entryPrice, 1, activeMinTpSlDistancePoints, out firstTargetPrice, out firstTargetBarsAgo)
				: TryGetNthBearishPivotLowBelowEntry(entryPrice, 1, activeMinTpSlDistancePoints, out firstTargetPrice, out firstTargetBarsAgo);
			if (!hasFirstTarget)
			{
				LogTrade(fvgTag, string.Format("BLOCKED (NoTarget: {0} entry={1})", direction, entryPrice), false);
				return;
			}

			double targetPrice = firstTargetPrice;
			int targetBarsAgo = firstTargetBarsAgo;

			if (UseBreakEvenWickLine)
			{
				double minBeGap = activeMinTpSlDistancePoints;
				bool foundTarget = false;
				for (int occurrence = 2; occurrence <= 10; occurrence++)
				{
					double candidatePrice;
					int candidateBarsAgo;
					bool hasCandidate = direction == TradeDirection.Long
						? TryGetNthBullishPivotHighAboveEntry(entryPrice, occurrence, activeMinTpSlDistancePoints, out candidatePrice, out candidateBarsAgo)
						: TryGetNthBearishPivotLowBelowEntry(entryPrice, occurrence, activeMinTpSlDistancePoints, out candidatePrice, out candidateBarsAgo);
					if (!hasCandidate)
						break;

					bool beyondBe = direction == TradeDirection.Long
						? candidatePrice > firstTargetPrice
						: candidatePrice < firstTargetPrice;
					bool farEnough = minBeGap <= 0 || Math.Abs(candidatePrice - firstTargetPrice) >= minBeGap;

					if (beyondBe && farEnough)
					{
						targetPrice = candidatePrice;
						targetBarsAgo = candidateBarsAgo;
						if (occurrence > 2)
							LogTrade(fvgTag, string.Format("TP adjusted (Pivot {0}) price={1} barsAgo={2}", occurrence, candidatePrice, candidateBarsAgo), false);
						foundTarget = true;
						break;
					}
				}

				if (!foundTarget)
				{
					LogTrade(fvgTag, string.Format("BLOCKED (NoTargetBeyondBE: {0} entry={1})", direction, entryPrice), false);
					return;
				}
			}

			targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);

			if (InvalidateIfTargetHitBeforeEntry &&
				intrabarTargetBarIndex == CurrentBar &&
				(direction == TradeDirection.Long
					? (intrabarTargetHitLong && Math.Abs(intrabarTargetPriceLong - targetPrice) <= Instrument.MasterInstrument.TickSize / 2.0)
					: (intrabarTargetHitShort && Math.Abs(intrabarTargetPriceShort - targetPrice) <= Instrument.MasterInstrument.TickSize / 2.0)))
			{
				LogTrade(fvgTag, string.Format(
					"BLOCKED (TargetHitIntrabar: {0} entry={1} target={2})",
					direction,
					entryPrice,
					targetPrice), false);
				return;
			}

			if (InvalidateIfTargetHitBeforeEntry &&
				(direction == TradeDirection.Long
					? (High[0] >= targetPrice || Close[0] >= targetPrice)
					: (Low[0] <= targetPrice || Close[0] <= targetPrice)))
			{
				LogTrade(fvgTag, string.Format(
					"BLOCKED (TargetHitBeforeClose: {0} entry={1} target={2})",
					direction,
					entryPrice,
					targetPrice), false);
				return;
			}

			double stopPrice = 0;
			int stopBarsAgo = -1;
			bool hasStop = false;
			bool hasAnyStop = false;
			double fallbackStopPrice = 0;
			int fallbackStopBarsAgo = -1;
			int stopOccurrence = slOccurrence;
			while (true)
			{
				bool foundStop = direction == TradeDirection.Long
					? TryGetNthBearishPivotLowBelowEntry(entryPrice, stopOccurrence, activeMinTpSlDistancePoints, out stopPrice, out stopBarsAgo)
					: TryGetNthBullishPivotHighAboveEntry(entryPrice, stopOccurrence, activeMinTpSlDistancePoints, out stopPrice, out stopBarsAgo);

				if (!foundStop)
					break;

				if (!hasAnyStop)
				{
					hasAnyStop = true;
					fallbackStopPrice = stopPrice;
					fallbackStopBarsAgo = stopBarsAgo;
				}

				bool outsideFvg = direction == TradeDirection.Long ? stopPrice < fvgLower : stopPrice > fvgUpper;
				if (outsideFvg)
				{
					hasStop = true;
					break;
				}

				stopOccurrence++;
			}

			if (!hasStop)
			{
				if (hasAnyStop)
				{
					stopPrice = fallbackStopPrice;
					stopBarsAgo = fallbackStopBarsAgo;
					LogTrade(fvgTag, string.Format(
						"WARN StopInsideFVG {0} entry={1} stop={2} fvgLower={3} fvgUpper={4}",
						direction,
						entryPrice,
						stopPrice,
						fvgLower,
						fvgUpper), false);
				}
				else
				{
					LogTrade(fvgTag, string.Format(
						"BLOCKED (NoStop: {0} entry={1})",
						direction,
						entryPrice), false);
					return;
				}
			}

			double bePrice = firstTargetPrice;
			int beBarsAgo = firstTargetBarsAgo;
			if (UseBreakEvenWickLine)
			{
				double fvgBePrice;
				int fvgBeBarsAgo;
				if (TryGetBreakEvenFromFvg(direction, entryPrice, firstTargetPrice, out fvgBePrice, out fvgBeBarsAgo))
				{
					bePrice = fvgBePrice;
					beBarsAgo = fvgBeBarsAgo;
					LogTrade(fvgTag, string.Format("BE LINE from FVG price={0} barsAgo={1}", bePrice, beBarsAgo), false);
				}

				ActivateBreakEvenLine(fvgTag, direction, bePrice, beBarsAgo);
				if (bePrice == firstTargetPrice)
					LogTrade(fvgTag, string.Format("BE LINE price={0} barsAgo={1}", bePrice, beBarsAgo), false);
			}

			stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);
			ActivateTargetStopLines(fvgTag, targetPrice, targetBarsAgo, stopPrice, stopBarsAgo);

			string signalName = direction == TradeDirection.Long ? "Long" : "Short";
			SetStopLoss(signalName, CalculationMode.Price, stopPrice, false);
			SetProfitTarget(signalName, CalculationMode.Price, targetPrice);

			string beInfo = UseBreakEvenWickLine ? string.Format(" be={0}", Instrument.MasterInstrument.RoundToTickSize(bePrice)) : string.Empty;
			LogTrade(fvgTag, string.Format(
				"ENTRY SENT {0} entry={1} stop={2} target={3}{4} stopBarsAgo={5} targetBarsAgo={6} sweepPrice={7} sweepBar={8}",
				direction,
				entryPrice,
				stopPrice,
				targetPrice,
				beInfo,
				stopBarsAgo,
				targetBarsAgo,
				sweep.Price,
				sweep.BarIndex), false);

			entryIfvgLower = fvgLower;
			entryIfvgUpper = fvgUpper;
			entryIfvgDirection = direction;
			pendingTradeTag = fvgTag;
			SetHedgeLock(GetInstrumentKey(), direction == TradeDirection.Long ? MarketPosition.Long : MarketPosition.Short);

			if (direction == TradeDirection.Long)
				EnterLong(Contracts, signalName);
			else
				EnterShort(Contracts, signalName);

			lastEntryBar = CurrentBar;
		}

		private bool PassesVolumeSmaFilter(string fvgTag)
		{
			if (!UseVolumeSmaFilter)
				return true;

			int requiredBars = Math.Max(VolumeFastSmaPeriod, VolumeSlowSmaPeriod);
			if (CurrentBar < requiredBars - 1)
			{
				LogTrade(fvgTag, string.Format(
					"BLOCKED (VolumeSmaWarmup: bars={0} need={1})",
					CurrentBar + 1,
					requiredBars), false);
				return false;
			}

			double fast = volumeFastSma[0];
			double slow = volumeSlowSma[0];
			if (slow <= 0)
			{
				LogTrade(fvgTag, string.Format(
					"BLOCKED (VolumeSmaInvalid: fast={0} slow={1})",
					fast,
					slow), false);
				return false;
			}

			if (fast <= slow * VolumeSmaMultiplier)
			{
				LogTrade(fvgTag, string.Format(
					"BLOCKED (VolumeSma: fast={0} slow={1} mult={2})",
					fast,
					slow,
					VolumeSmaMultiplier), false);
				return false;
			}

			LogTrade(fvgTag, string.Format(
				"Filter VolumeSma ok fast={0} slow={1} mult={2}",
				fast,
				slow,
				VolumeSmaMultiplier), false);
			return true;
		}

		private void PruneLiquidityLines(int drawLimitDays)
		{
			if (drawLimitDays <= 0)
				return;

			DateTime cutoffDate = Time[0].Date.AddDays(-(drawLimitDays - 1));
			for (int i = liquidityLines.Count - 1; i >= 0; i--)
			{
				LiquidityLine line = liquidityLines[i];
				if (line.SessionDate < cutoffDate)
				{
					RemoveDrawObject(line.Tag);
					RemoveDrawObject(line.Tag + "_Lbl");
					liquidityLines.RemoveAt(i);
				}
			}
		}

		private static bool IsTimeInRange(TimeSpan now, TimeSpan start, TimeSpan end)
		{
			if (start == end)
				return false;
			if (start < end)
				return now >= start && now < end;
			return now >= start || now < end;
		}

		private bool TimeInSkip(DateTime time)
		{
			return IsInGlobalSkip(time) || IsInNewYorkSkip(time);
		}

		private bool IsInGlobalSkip(DateTime time)
		{
			return IsTimeInRange(time.TimeOfDay, SkipStart, SkipEnd);
		}

		private bool IsInNewYorkSkip(DateTime time)
		{
			if (activeSession != SessionSlot.Session3)
				return false;
			return IsTimeInRange(time.TimeOfDay, NewYork_SkipStart, NewYork_SkipEnd);
		}

		private static DateTime GetSessionStartDate(DateTime barTime, TimeSpan start, TimeSpan end)
		{
			if (start < end)
				return barTime.Date;
			return barTime.TimeOfDay < end ? barTime.Date.AddDays(-1) : barTime.Date;
		}

		private string GetInstrumentKey()
		{
			return Instrument != null ? Instrument.MasterInstrument.Name : string.Empty;
		}

		private void SetHedgeLock(string instrument, MarketPosition direction)
		{
			if (string.IsNullOrWhiteSpace(instrument))
				return;

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
			if (string.IsNullOrWhiteSpace(instrument))
				return MarketPosition.Flat;

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
			}

			return MarketPosition.Flat;
		}

		private void ClearHedgeLock(string instrument)
		{
			if (string.IsNullOrWhiteSpace(instrument))
				return;

			lock (hedgeLockSync)
			{
				if (!File.Exists(hedgeLockFile))
					return;

				var lines = File.ReadAllLines(hedgeLockFile).ToList();
				lines.RemoveAll(l => l.StartsWith(instrument + ",", StringComparison.OrdinalIgnoreCase));
				File.WriteAllLines(hedgeLockFile, lines);
			}
		}

		private MarketPosition GetAccountPosition()
		{
			if (Account == null || Instrument == null)
				return MarketPosition.Flat;

			foreach (var pos in Account.Positions)
			{
				if (pos.Instrument.MasterInstrument.Name.Equals(Instrument.MasterInstrument.Name, StringComparison.OrdinalIgnoreCase))
					return pos.MarketPosition;
			}

			return MarketPosition.Flat;
		}

		private bool ShouldBlockForHedge(TradeDirection direction, string fvgTag)
		{
			string instrument = GetInstrumentKey();
			MarketPosition accountPosition = GetAccountPosition();
			MarketPosition strategyPosition = Position.MarketPosition;
			MarketPosition lockPosition = GetHedgeLock(instrument);
			MarketPosition activePosition = lockPosition != MarketPosition.Flat
				? lockPosition
				: (accountPosition != MarketPosition.Flat ? accountPosition : strategyPosition);

			if (blockWhenInPosition && (lockPosition != MarketPosition.Flat || accountPosition != MarketPosition.Flat || strategyPosition != MarketPosition.Flat))
			{
				LogTrade(fvgTag, string.Format("BLOCKED (ActivePosition: {0})", activePosition), false);
				return true;
			}

			if (!antiHedge)
				return false;

			bool opposite = direction == TradeDirection.Long
				? (lockPosition == MarketPosition.Short || accountPosition == MarketPosition.Short || strategyPosition == MarketPosition.Short)
				: (lockPosition == MarketPosition.Long || accountPosition == MarketPosition.Long || strategyPosition == MarketPosition.Long);

			if (!opposite)
				return false;

			LogTrade(fvgTag, string.Format("BLOCKED (AntiHedge: {0})", activePosition), false);
			return true;
		}

		private bool IsTradingDayAllowed(DateTime time)
		{
			switch (time.DayOfWeek)
			{
				case DayOfWeek.Monday:
					return tradeMonday;
				case DayOfWeek.Tuesday:
					return tradeTuesday;
				case DayOfWeek.Wednesday:
					return tradeWednesday;
				case DayOfWeek.Thursday:
					return tradeThursday;
				case DayOfWeek.Friday:
					return tradeFriday;
				default:
					return true;
			}
		}

		private static string GetLiquidityLabel(string sessionName, bool isHigh)
		{
			if (string.Equals(sessionName, "Asia", StringComparison.OrdinalIgnoreCase))
				return isHigh ? "AS.H" : "AS.L";
			if (string.Equals(sessionName, "London", StringComparison.OrdinalIgnoreCase))
				return isHigh ? "LO.H" : "LO.L";
			return isHigh ? "H" : "L";
		}

		private void FlattenAndCancel(string reason)
		{
			CancelAllOrders();

			if (Position.MarketPosition == MarketPosition.Long)
			{
				LogDebug(string.Format("Flattening LONG due to {0}", reason));
				ExitLong("Exit_" + reason, "Long");
			}
			else if (Position.MarketPosition == MarketPosition.Short)
			{
				LogDebug(string.Format("Flattening SHORT due to {0}", reason));
				ExitShort("Exit_" + reason, "Short");
			}
		}

		private void CancelAllOrders()
		{
			if (Account == null)
				return;

			foreach (Order order in Account.Orders)
			{
				if (order == null)
					continue;
				if (order.Instrument != Instrument)
					continue;
				if (order.OrderState == OrderState.Working ||
					order.OrderState == OrderState.Submitted ||
					order.OrderState == OrderState.Accepted ||
					order.OrderState == OrderState.ChangePending)
				{
					CancelOrder(order);
				}
			}
		}

		private bool TimeInSession(DateTime time)
		{
			EnsureActiveSession(time);
			if (activeSession == SessionSlot.None)
				return false;
			EnsureEffectiveTimes(time, false);
			TimeSpan now = time.TimeOfDay;

			if (effectiveSessionStart < effectiveSessionEnd)
				return now >= effectiveSessionStart && now < effectiveSessionEnd;

			return now >= effectiveSessionStart || now < effectiveSessionEnd;
		}

		private bool TimeInNoTradesAfter(DateTime time)
		{
			EnsureActiveSession(time);
			if (activeSession == SessionSlot.None)
				return false;
			EnsureEffectiveTimes(time, false);
			TimeSpan now = time.TimeOfDay;

			if (effectiveSessionStart < effectiveSessionEnd)
				return now >= effectiveNoTradesAfter && now < effectiveSessionEnd;

			return now >= effectiveNoTradesAfter || now < effectiveSessionEnd;
		}

		private void EnsureActiveSession(DateTime time)
		{
			if (time <= lastSessionEvalTime)
				return;
			lastSessionEvalTime = time;

			SessionSlot desired = DetermineSessionForTime(time);
			if (!sessionInitialized || desired != activeSession)
			{
				activeSession = desired;
				if (activeSession != SessionSlot.None)
					ApplyInputsForSession(activeSession);
				effectiveTimesDate = DateTime.MinValue;
				sessionInitialized = true;
			}
		}

		private SessionSlot DetermineSessionForTime(DateTime time)
		{
			TimeSpan now = time.TimeOfDay;

			GetSessionWindowForSession(SessionSlot.Session1, time.Date, out TimeSpan session1Start, out TimeSpan session1End);
			GetSessionWindowForSession(SessionSlot.Session2, time.Date, out TimeSpan session2Start, out TimeSpan session2End);
			GetSessionWindowForSession(SessionSlot.Session3, time.Date, out TimeSpan session3Start, out TimeSpan session3End);

			bool session1Configured = IsSessionConfigured(SessionSlot.Session1);
			bool session2Configured = IsSessionConfigured(SessionSlot.Session2);
			bool session3Configured = IsSessionConfigured(SessionSlot.Session3);

			if (session1Configured && IsTimeInRange(now, session1Start, session1End))
				return SessionSlot.Session1;
			if (session2Configured && IsTimeInRange(now, session2Start, session2End))
				return SessionSlot.Session2;
			if (session3Configured && IsTimeInRange(now, session3Start, session3End))
				return SessionSlot.Session3;

			if (!session1Configured && !session2Configured && !session3Configured)
				return SessionSlot.None;

			DateTime nextSession1Start = DateTime.MaxValue;
			DateTime nextSession2Start = DateTime.MaxValue;
			DateTime nextSession3Start = DateTime.MaxValue;

			if (session1Configured)
			{
				nextSession1Start = time.Date + session1Start;
				if (nextSession1Start <= time)
					nextSession1Start = nextSession1Start.AddDays(1);
			}

			if (session2Configured)
			{
				nextSession2Start = time.Date + session2Start;
				if (nextSession2Start <= time)
					nextSession2Start = nextSession2Start.AddDays(1);
			}

			if (session3Configured)
			{
				nextSession3Start = time.Date + session3Start;
				if (nextSession3Start <= time)
					nextSession3Start = nextSession3Start.AddDays(1);
			}

			SessionSlot nextSession = SessionSlot.None;
			DateTime nextStart = DateTime.MaxValue;

			if (session1Configured && nextSession1Start < nextStart)
			{
				nextStart = nextSession1Start;
				nextSession = SessionSlot.Session1;
			}

			if (session2Configured && nextSession2Start < nextStart)
			{
				nextStart = nextSession2Start;
				nextSession = SessionSlot.Session2;
			}

			if (session3Configured && nextSession3Start < nextStart)
			{
				nextStart = nextSession3Start;
				nextSession = SessionSlot.Session3;
			}

			return nextSession;
		}

		private bool CrossedSessionEnd(DateTime previousTime, DateTime currentTime)
		{
			return CrossedSessionEndForSession(SessionSlot.Session1, previousTime, currentTime)
				|| CrossedSessionEndForSession(SessionSlot.Session2, previousTime, currentTime)
				|| CrossedSessionEndForSession(SessionSlot.Session3, previousTime, currentTime);
		}

		private bool CrossedSessionEndForSession(SessionSlot session, DateTime previousTime, DateTime currentTime)
		{
			if (!IsSessionConfigured(session))
				return false;

			GetSessionWindowForSession(session, currentTime.Date, out TimeSpan start, out TimeSpan end);

			bool wasInSession = IsTimeInRange(previousTime.TimeOfDay, start, end);
			bool nowInSession = IsTimeInRange(currentTime.TimeOfDay, start, end);
			return wasInSession && !nowInSession;
		}

		private void GetSessionWindowForSession(SessionSlot session, DateTime date, out TimeSpan start, out TimeSpan end)
		{
			start = TimeSpan.Zero;
			end = TimeSpan.Zero;
			bool autoShift = false;

			switch (session)
			{
				case SessionSlot.Session1:
					start = Asia_SessionStart;
					end = Asia_SessionEnd;
					autoShift = AutoShiftSession1;
					break;
				case SessionSlot.Session2:
					start = London_SessionStart;
					end = London_SessionEnd;
					autoShift = AutoShiftSession2;
					break;
				case SessionSlot.Session3:
					start = NewYork_SessionStart;
					end = NewYork_SessionEnd;
					autoShift = AutoShiftSession3;
					break;
			}

			if (autoShift)
			{
				TimeSpan shift = GetLondonSessionShiftForDate(date);
				start = ShiftTime(start, shift);
				end = ShiftTime(end, shift);
			}
		}

		private bool IsSessionConfigured(SessionSlot session)
		{
			switch (session)
			{
				case SessionSlot.Session1:
					return UseSession1 && Asia_SessionStart != Asia_SessionEnd;
				case SessionSlot.Session2:
					return UseSession2 && London_SessionStart != London_SessionEnd;
				case SessionSlot.Session3:
					return UseSession3 && NewYork_SessionStart != NewYork_SessionEnd;
				default:
					return false;
			}
		}

		private void ApplyInputsForSession(SessionSlot session)
		{
			switch (session)
			{
				case SessionSlot.Session1:
					activeAutoShiftTimes = AutoShiftSession1;
					activeSessionStart = Asia_SessionStart;
					activeSessionEnd = Asia_SessionEnd;
					activeNoTradesAfter = Asia_NoTradesAfter;
					activeMinFvgSizePoints = MinFvgSizePoints;
					activeMaxFvgSizePoints = MaxFvgSizePoints;
					activeFvgDrawLimit = FvgDrawLimit;
					activeCombineFvgSeries = CombineFvgSeries;
					activeSwingStrength = SwingStrength;
					activeMinTpSlDistancePoints = MinTpSlDistancePoints;
					break;
				case SessionSlot.Session2:
					activeAutoShiftTimes = AutoShiftSession2;
					activeSessionStart = London_SessionStart;
					activeSessionEnd = London_SessionEnd;
					activeNoTradesAfter = London_NoTradesAfter;
					activeMinFvgSizePoints = London_MinFvgSizePoints;
					activeMaxFvgSizePoints = London_MaxFvgSizePoints;
					activeFvgDrawLimit = London_FvgDrawLimit;
					activeCombineFvgSeries = London_CombineFvgSeries;
					activeSwingStrength = London_SwingStrength;
					activeMinTpSlDistancePoints = London_MinTpSlDistancePoints;
					break;
				case SessionSlot.Session3:
					activeAutoShiftTimes = AutoShiftSession3;
					activeSessionStart = NewYork_SessionStart;
					activeSessionEnd = NewYork_SessionEnd;
					activeNoTradesAfter = NewYork_NoTradesAfter;
					activeMinFvgSizePoints = NewYork_MinFvgSizePoints;
					activeMaxFvgSizePoints = NewYork_MaxFvgSizePoints;
					activeFvgDrawLimit = NewYork_FvgDrawLimit;
					activeCombineFvgSeries = NewYork_CombineFvgSeries;
					activeSwingStrength = NewYork_SwingStrength;
					activeMinTpSlDistancePoints = NewYork_MinTpSlDistancePoints;
					break;
			}

			LogDebug(string.Format(
				"Session settings applied ({0}): minFvg={1} maxFvg={2} drawLimit={3} combine={4} swingStrength={5} minTpSl={6}",
				session,
				activeMinFvgSizePoints,
				activeMaxFvgSizePoints,
				activeFvgDrawLimit,
				activeCombineFvgSeries,
				activeSwingStrength,
				activeMinTpSlDistancePoints));
		}

		private void EnsureEffectiveTimes(DateTime barTime, bool log)
		{
			if (activeSession == SessionSlot.None)
			{
				effectiveSessionStart = TimeSpan.Zero;
				effectiveSessionEnd = TimeSpan.Zero;
				effectiveNoTradesAfter = TimeSpan.Zero;
				return;
			}

			if (!activeAutoShiftTimes)
			{
				effectiveSessionStart = activeSessionStart;
				effectiveSessionEnd = activeSessionEnd;
				effectiveNoTradesAfter = activeNoTradesAfter;
				return;
			}

			DateTime date = barTime.Date;
			if (date == effectiveTimesDate)
				return;

			effectiveTimesDate = date;

			TimeSpan shift = GetLondonSessionShiftForDate(date);
			effectiveSessionStart = ShiftTime(activeSessionStart, shift);
			effectiveSessionEnd = ShiftTime(activeSessionEnd, shift);
			effectiveNoTradesAfter = ShiftTime(activeNoTradesAfter, shift);

			if (DebugLogging && log)
			{
				LogDebug(string.Format(
					"AutoShift recompute base {0}-{1} nta={2} effective {3}-{4} nta={5} shift={6}h",
					activeSessionStart,
					activeSessionEnd,
					activeNoTradesAfter,
					effectiveSessionStart,
					effectiveSessionEnd,
					effectiveNoTradesAfter,
					shift.TotalHours));
			}
		}

		private TimeSpan GetLondonSessionShiftForDate(DateTime date)
		{
			DateTime utcSample = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc);
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
				var bars = Bars;
				if (bars != null)
				{
					var timeZoneProp = bars.GetType().GetProperty(
						"TimeZoneInfo",
						BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

					if (timeZoneProp != null && typeof(TimeZoneInfo).IsAssignableFrom(timeZoneProp.PropertyType))
						targetTimeZone = (TimeZoneInfo)timeZoneProp.GetValue(bars, null);
				}

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
				londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
			}
			catch
			{
				try
				{
					londonTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
				}
				catch
				{
					londonTimeZone = TimeZoneInfo.Utc;
				}
			}

			return londonTimeZone;
		}

		private void DrawSessionBackground()
		{
			if (CurrentBar < 1)
				return;

			DateTime barTime = Time[0];
			EnsureActiveSession(barTime);
			if (activeSession == SessionSlot.None)
				return;
			EnsureEffectiveTimes(barTime, false);

			bool isOvernight = effectiveSessionStart > effectiveSessionEnd;
			DateTime sessionStartTime = barTime.Date + effectiveSessionStart;
			DateTime sessionEndTime = isOvernight
				? sessionStartTime.AddDays(1).Date + effectiveSessionEnd
				: sessionStartTime.Date + effectiveSessionEnd;

			int startBarsAgo = Bars.GetBar(sessionStartTime);
			int endBarsAgo = Bars.GetBar(sessionEndTime);

			if (startBarsAgo < 0 || endBarsAgo < 0)
				return;

			string tag = string.Format("iFVG_SessionFill_{0}_{1:yyyyMMdd_HHmm}", activeSession, sessionStartTime);
			if (DrawObjects[tag] == null)
			{
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

			var lineBrush = new SolidColorBrush(Color.FromArgb(70, 255, 0, 0));
			if (lineBrush.CanFreeze)
				lineBrush.Freeze();

			DateTime noTradesAfterTime = barTime.Date + effectiveNoTradesAfter;
			if (isOvernight && effectiveNoTradesAfter < effectiveSessionStart)
				noTradesAfterTime = noTradesAfterTime.AddDays(1);

			Draw.VerticalLine(
				this,
				string.Format("iFVG_NoTradesAfter_{0}_{1:yyyyMMdd_HHmm}", activeSession, sessionStartTime),
				noTradesAfterTime,
				lineBrush,
				DashStyleHelper.Solid,
				2);

			if (activeSession == SessionSlot.Session3)
				DrawSkipWindow("NY", NewYork_SkipStart, NewYork_SkipEnd);
		}

		private void DrawSkipWindow(string tagPrefix, TimeSpan start, TimeSpan end)
		{
			if (start == TimeSpan.Zero || end == TimeSpan.Zero || start == end)
				return;

			DateTime barDate = Time[0].Date;
			DateTime windowStart = barDate + start;
			DateTime windowEnd = barDate + end;

			if (start > end)
				windowEnd = windowEnd.AddDays(1);

			int startBarsAgo = Bars.GetBar(windowStart);
			int endBarsAgo = Bars.GetBar(windowEnd);
			if (startBarsAgo < 0 || endBarsAgo < 0)
				return;

			var areaBrush = new SolidColorBrush(Color.FromArgb(200, 255, 0, 0));
			if (areaBrush.CanFreeze)
				areaBrush.Freeze();
			var outlineBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
			if (outlineBrush.CanFreeze)
				outlineBrush.Freeze();

			string rectTag = string.Format("iFVG_{0}_Skip_Rect_{1:yyyyMMdd}", tagPrefix, windowStart);
			Draw.Rectangle(
				this,
				rectTag,
				false,
				windowStart,
				0,
				windowEnd,
				30000,
				outlineBrush,
				areaBrush,
				2
			).ZOrder = -1;

			string startTag = string.Format("iFVG_{0}_Skip_Start_{1:yyyyMMdd_HHmm}", tagPrefix, windowStart);
			Draw.VerticalLine(this, startTag, windowStart, outlineBrush, DashStyleHelper.Solid, 2);

			string endTag = string.Format("iFVG_{0}_Skip_End_{1:yyyyMMdd_HHmm}", tagPrefix, windowEnd);
			Draw.VerticalLine(this, endTag, windowEnd, outlineBrush, DashStyleHelper.Solid, 2);
		}

		private void UpdateSwingLiquidity()
		{
			if (SwingDrawBars <= 0)
			{
				if (swingLines.Count > 0)
				{
					for (int i = 0; i < swingLines.Count; i++)
						RemoveDrawObject(swingLines[i].Tag);
					swingLines.Clear();
				}
				return;
			}

			if (CurrentBar < activeSwingStrength * 2)
				return;

			int pivotBarsAgo = activeSwingStrength;
			if (IsSwingHigh(pivotBarsAgo))
				AddSwingLine(true, pivotBarsAgo, High[pivotBarsAgo]);
			if (IsSwingLow(pivotBarsAgo))
				AddSwingLine(false, pivotBarsAgo, Low[pivotBarsAgo]);

			UpdateSwingLines();
			PruneSwingLines();
		}

		private bool IsSwingHigh(int barsAgo)
		{
			int span = barsAgo * 2 + 1;
			double max = MAX(High, span)[0];
			return High[barsAgo] >= max;
		}

		private bool IsSwingLow(int barsAgo)
		{
			int span = barsAgo * 2 + 1;
			double min = MIN(Low, span)[0];
			return Low[barsAgo] <= min;
		}

		private void AddSwingLine(bool isHigh, int barsAgo, double price)
		{
			int startBarIndex = CurrentBar - barsAgo;
			string tag = string.Format("iFVG_Swing_{0}_{1}", isHigh ? "H" : "L", startBarIndex);

			SwingLine line = new SwingLine
			{
				Tag = tag,
				Price = price,
				StartBarIndex = startBarIndex,
				EndBarIndex = CurrentBar,
				IsActive = true,
				IsHigh = isHigh
			};

			swingLines.Add(line);
		}

		private void UpdateSwingLines()
		{
			for (int i = 0; i < swingLines.Count; i++)
			{
				SwingLine line = swingLines[i];
				if (!line.IsActive)
					continue;

				bool hit = line.IsHigh ? High[0] >= line.Price : Low[0] <= line.Price;
				line.EndBarIndex = CurrentBar;

				int startBarsAgo = CurrentBar - line.StartBarIndex;
				int endBarsAgo = CurrentBar - line.EndBarIndex;
				if (startBarsAgo < 0)
					startBarsAgo = 0;
				if (endBarsAgo < 0)
					endBarsAgo = 0;

				Draw.Line(
					this,
					line.Tag,
					false,
					startBarsAgo,
					line.Price,
					endBarsAgo,
					line.Price,
					swingLineBrush,
					DashStyleHelper.Solid,
					1
				);

				if (hit)
				{
					RegisterSweep(line.IsHigh ? TradeDirection.Short : TradeDirection.Long, line.Price, false);
					line.IsActive = false;
				}
			}
		}

		private void PruneSwingLines()
		{
			int cutoffIndex = CurrentBar - SwingDrawBars;
			for (int i = swingLines.Count - 1; i >= 0; i--)
			{
				SwingLine line = swingLines[i];
				if (line.StartBarIndex < cutoffIndex)
				{
					RemoveDrawObject(line.Tag);
					swingLines.RemoveAt(i);
				}
			}
		}

		private void UpdateActiveFvgs()
		{
			double bodyHigh = Math.Max(Open[0], Close[0]);
			double bodyLow = Math.Min(Open[0], Close[0]);
			for (int i = 0; i < activeFvgs.Count; i++)
			{
				FvgBox fvg = activeFvgs[i];
				if (!fvg.IsActive)
					continue;

				bool invalidated = fvg.IsBullish
					? (Close[0] < fvg.Lower && Close[0] < Open[0])
					: (Close[0] > fvg.Upper && Close[0] > Open[0]);

				fvg.EndBarIndex = CurrentBar;

				int startBarsAgo = CurrentBar - fvg.StartBarIndex;
				int endBarsAgo = CurrentBar - fvg.EndBarIndex;
				if (startBarsAgo < 0)
					startBarsAgo = 0;
				if (endBarsAgo < 0)
					endBarsAgo = 0;

				Draw.Rectangle(
					this,
					fvg.Tag,
					false,
					startBarsAgo,
					fvg.Lower,
					endBarsAgo,
					fvg.Upper,
					Brushes.Transparent,
					fvgFill,
					fvgOpacity
				);

				if (invalidated)
				{
					LogTrade(fvg.Tag, string.Format(
						"ENTRY ATTEMPT {0} fvgLower={1} fvgUpper={2} close={3}",
						fvg.IsBullish ? "bullish" : "bearish",
						fvg.Lower,
						fvg.Upper,
						Close[0]), true);
					TradeDirection ifvgDirection = fvg.IsBullish ? TradeDirection.Short : TradeDirection.Long;
					TryEnterFromIfvg(ifvgDirection, fvg.Lower, fvg.Upper, fvg.Tag);
					fvg.IsActive = false;
					if (!ShowInvalidatedFvgs)
						RemoveDrawObject(fvg.Tag);
					else
						Draw.Rectangle(
							this,
							fvg.Tag,
							false,
							startBarsAgo,
							fvg.Lower,
							endBarsAgo,
							fvg.Upper,
							Brushes.Transparent,
							invalidatedFill,
							invalidatedOpacity
						);
				}
			}
		}

		private void UpdateBreakEvenFvgs()
		{
			for (int i = 0; i < breakEvenFvgs.Count; i++)
			{
				FvgBox fvg = breakEvenFvgs[i];
				if (!fvg.IsActive)
					continue;

				bool invalidated = fvg.IsBullish
					? (Close[0] < fvg.Lower && Close[0] < Open[0])
					: (Close[0] > fvg.Upper && Close[0] > Open[0]);

				fvg.EndBarIndex = CurrentBar;

				if (invalidated)
					fvg.IsActive = false;
			}
		}

		private void UpdateActiveHtfFvgs()
		{
			double close = Closes[htfBarsInProgress][0];
			double open = Opens[htfBarsInProgress][0];
			for (int i = 0; i < activeHtfFvgs.Count; i++)
			{
				HtfFvgBox fvg = activeHtfFvgs[i];
				if (!fvg.IsActive)
					continue;

				bool invalidated = fvg.IsBullish
					? (close < fvg.Lower && close < open)
					: (close > fvg.Upper && close > open);

				fvg.EndTime = Times[htfBarsInProgress][0];

				if (invalidated)
					fvg.IsActive = false;
			}
		}

		private void DrawActiveHtfFvgs()
		{
			if (!UseDeliverFromHtfFvg)
				return;
			if (htfBarsInProgress < 0 || activeHtfFvgs == null || activeHtfFvgs.Count == 0)
				return;

			DateTime endTime = Time[0];
			string htfLabel = GetHtfLabel();
			for (int i = 0; i < activeHtfFvgs.Count; i++)
			{
				HtfFvgBox fvg = activeHtfFvgs[i];
				if (!fvg.IsActive && !ShowInvalidatedFvgs)
				{
					RemoveDrawObject(fvg.Tag);
					RemoveDrawObject(fvg.Tag + "_Lbl");
					continue;
				}

				Brush fill = fvg.IsActive ? htfFvgFill : invalidatedFill;
				int opacity = fvg.IsActive ? fvgOpacity : invalidatedOpacity;
				DateTime end = fvg.IsActive ? endTime : fvg.EndTime;

				Draw.Rectangle(
					this,
					fvg.Tag,
					false,
					fvg.StartTime,
					fvg.Lower,
					end,
					fvg.Upper,
					Brushes.Transparent,
					fill,
					opacity
				);

				if (!string.IsNullOrEmpty(htfLabel))
				{
					Draw.Text(
						this,
						fvg.Tag + "_Lbl",
						false,
						htfLabel,
						end,
						fvg.Upper,
						0,
						htfLabelBrush,
						new SimpleFont("Arial", 10),
						TextAlignment.Left,
						Brushes.Transparent,
						Brushes.Transparent,
						0
					);
				}
			}
		}

		private bool TryCombineWithPreviousFvg(FvgBox newFvg)
		{
			if (!activeCombineFvgSeries)
				return false;
			if (activeFvgs.Count == 0)
				return false;

			FvgBox lastFvg = activeFvgs[activeFvgs.Count - 1];
			if (!lastFvg.IsActive)
				return false;
			if (lastFvg.IsBullish != newFvg.IsBullish)
				return false;
			if (lastFvg.SessionDate != newFvg.SessionDate)
				return false;
			if (newFvg.StartBarIndex != lastFvg.StartBarIndex + 1)
				return false;

			lastFvg.Lower = Math.Min(lastFvg.Lower, newFvg.Lower);
			lastFvg.Upper = Math.Max(lastFvg.Upper, newFvg.Upper);
			lastFvg.EndBarIndex = newFvg.EndBarIndex;

			LogVerbose(string.Format(
				"Combined FVGs into tag={0} lower={1} upper={2} endBar={3}",
				lastFvg.Tag,
				lastFvg.Lower,
				lastFvg.Upper,
				lastFvg.EndBarIndex));

			int startBarsAgo = CurrentBar - lastFvg.StartBarIndex;
			int endBarsAgo = CurrentBar - lastFvg.EndBarIndex;
			if (startBarsAgo < 0)
				startBarsAgo = 0;
			if (endBarsAgo < 0)
				endBarsAgo = 0;

			Draw.Rectangle(
				this,
				lastFvg.Tag,
				false,
				startBarsAgo,
				lastFvg.Lower,
				endBarsAgo,
				lastFvg.Upper,
				Brushes.Transparent,
				fvgFill,
				fvgOpacity
			);

			return true;
		}

		private void DetectNewFvg()
		{
			// FVG detection uses the 3-bar displacement: bar[2] -> bar[0].
			bool bullishFvg = Low[0] > High[2];
			bool bearishFvg = High[0] < Low[2];

			if (!bullishFvg && !bearishFvg)
				return;

			LogVerbose(string.Format(
				"FVG detected {0} low0={1} high2={2} high0={3} low2={4}",
				bullishFvg ? "bullish" : "bearish",
				Low[0],
				High[2],
				High[0],
				Low[2]));

			FvgBox fvg = new FvgBox();
			fvg.IsBullish = bullishFvg;
			fvg.Lower = bullishFvg ? High[2] : High[0];
			fvg.Upper = bullishFvg ? Low[0] : Low[2];
			fvg.StartBarIndex = CurrentBar - 2;
			fvg.EndBarIndex = CurrentBar;
			fvg.CreatedBarIndex = CurrentBar;
			fvg.IsActive = true;
			fvg.SessionDate = Time[0].Date;
			fvg.Tag = string.Format("iFVG_{0}_{1:yyyyMMdd_HHmmss}", fvgCounter++, Time[0]);
			breakEvenFvgs.Add(fvg);

			double fvgSizePoints = Math.Abs(fvg.Upper - fvg.Lower);
			if (activeMinFvgSizePoints > 0 && fvgSizePoints < activeMinFvgSizePoints)
			{
				LogVerbose(string.Format("FVG rejected: size {0} < min {1}", fvgSizePoints, activeMinFvgSizePoints));
				return;
			}
			if (activeMaxFvgSizePoints > 0 && fvgSizePoints > activeMaxFvgSizePoints)
			{
				LogVerbose(string.Format("FVG rejected: size {0} > max {1}", fvgSizePoints, activeMaxFvgSizePoints));
				return;
			}

			if (TryCombineWithPreviousFvg(fvg))
				return;

			activeFvgs.Add(fvg);

			Draw.Rectangle(
				this,
				fvg.Tag,
				false,
				2,
				fvg.Lower,
				0,
				fvg.Upper,
				Brushes.Transparent,
				fvgFill,
				fvgOpacity
			);

			LogVerbose(string.Format(
				"FVG added tag={0} lower={1} upper={2} startBar={3} endBar={4}",
				fvg.Tag,
				fvg.Lower,
				fvg.Upper,
				fvg.StartBarIndex,
				fvg.EndBarIndex));
		}

		private void DetectNewHtfFvg()
		{
			// FVG detection uses the 3-bar displacement: bar[2] -> bar[0].
			double low0 = Lows[htfBarsInProgress][0];
			double high0 = Highs[htfBarsInProgress][0];
			double low2 = Lows[htfBarsInProgress][2];
			double high2 = Highs[htfBarsInProgress][2];
			bool bullishFvg = low0 > high2;
			bool bearishFvg = high0 < low2;

			if (!bullishFvg && !bearishFvg)
				return;

			HtfFvgBox fvg = new HtfFvgBox();
			fvg.IsBullish = bullishFvg;
			fvg.Lower = bullishFvg ? high2 : high0;
			fvg.Upper = bullishFvg ? low0 : low2;
			fvg.StartTime = Times[htfBarsInProgress][2];
			fvg.EndTime = Times[htfBarsInProgress][0];
			fvg.CreatedTime = Times[htfBarsInProgress][0];
			fvg.IsActive = true;
			fvg.SessionDate = Times[htfBarsInProgress][0].Date;
			fvg.Tag = string.Format("iFVG_HTF_{0}_{1:yyyyMMdd_HHmmss}", htfFvgCounter++, Times[htfBarsInProgress][0]);

			double fvgSizePoints = Math.Abs(fvg.Upper - fvg.Lower);
			if (activeMinFvgSizePoints > 0 && fvgSizePoints < activeMinFvgSizePoints)
				return;
			if (activeMaxFvgSizePoints > 0 && fvgSizePoints > activeMaxFvgSizePoints)
				return;

			activeHtfFvgs.Add(fvg);
		}

		private void LogDebug(string message)
		{
			if (!DebugLogging)
				return;
			Print(string.Format("{0} - {1}", Time[0], message));
		}

		private void LogVerbose(string message)
		{
			if (!DebugLogging || !VerboseDebugLogging)
				return;
			Print(string.Format("{0} - {1}", Time[0], message));
		}

		private void LogTrade(string tradeTag, string message, bool addSeparator)
		{
			if (!DebugLogging)
				return;
			string label = FormatTradeLabel(tradeTag);
			if (!string.IsNullOrEmpty(label) && label != lastTradeLabelPrinted)
			{
				if (!string.IsNullOrEmpty(lastTradeLabelPrinted))
					Print(string.Empty);
				lastTradeLabelPrinted = label;
			}
			if (!string.IsNullOrEmpty(label))
				Print(string.Format("{0} - {1} {2}", Time[0], label, message));
			else
				Print(string.Format("{0} - {1}", Time[0], message));
		}

		private string FormatTradeLabel(string tradeTag)
		{
			if (string.IsNullOrEmpty(tradeTag))
				return string.Empty;

			string[] parts = tradeTag.Split('_');
			if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]))
				return "T" + parts[1];

			return string.Empty;
		}

		private string FormatExitReason(string orderName)
		{
			if (string.IsNullOrEmpty(orderName))
				return "Exit";
			if (orderName.IndexOf("Profit", StringComparison.OrdinalIgnoreCase) >= 0)
				return "Profit target";
			if (orderName.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
				return "Stop loss";
			if (orderName.IndexOf("iFVGCloseExit", StringComparison.OrdinalIgnoreCase) >= 0)
				return "iFVG close exit";
			return orderName;
		}

		private void CheckExitOnCloseBeyondEntryIfvg()
		{
			if (!ExitOnCloseBeyondEntryIfvg)
				return;
			if (Position.MarketPosition == MarketPosition.Flat || entryIfvgDirection == null)
				return;
			if (CurrentBar <= lastEntryBar)
				return;

			bool shouldExit = entryIfvgDirection == TradeDirection.Long
				? Close[0] < entryIfvgLower
				: Close[0] > entryIfvgUpper;

			if (!shouldExit)
				return;

			LogTrade(activeTradeTag, string.Format(
				"EXIT SIGNAL (iFVGClose) {0} close={1} fvgLower={2} fvgUpper={3}",
				entryIfvgDirection,
				Close[0],
				entryIfvgLower,
				entryIfvgUpper), true);

			if (entryIfvgDirection == TradeDirection.Long)
				ExitLong("iFVGCloseExit", "Long");
			else
				ExitShort("iFVGCloseExit", "Short");
		}

		private void UpdateIntrabarTargetHit()
		{
			if (!InvalidateIfTargetHitBeforeEntry)
				return;
			if (CurrentBars[0] < 2)
				return;

			int primaryBar = CurrentBars[0];
			if (intrabarTargetBarIndex != primaryBar)
			{
				intrabarTargetBarIndex = primaryBar;
				intrabarTargetHitLong = false;
				intrabarTargetHitShort = false;
				intrabarTargetPriceLong = 0;
				intrabarTargetPriceShort = 0;
			}

			double entryPrice = Closes[0][0];
			double targetPrice;
			int targetBarsAgo;

			if (!intrabarTargetHitLong &&
				TryGetNthBullishPivotHighAboveEntry(entryPrice, 1, activeMinTpSlDistancePoints, out targetPrice, out targetBarsAgo))
			{
				targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);
				intrabarTargetPriceLong = targetPrice;
				if (Highs[0][0] >= targetPrice)
					intrabarTargetHitLong = true;
			}

			if (!intrabarTargetHitShort &&
				TryGetNthBearishPivotLowBelowEntry(entryPrice, 1, activeMinTpSlDistancePoints, out targetPrice, out targetBarsAgo))
			{
				targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);
				intrabarTargetPriceShort = targetPrice;
				if (Lows[0][0] <= targetPrice)
					intrabarTargetHitShort = true;
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			string orderName = execution != null && execution.Order != null ? execution.Order.Name : "n/a";
			if (breakEvenActive)
			{
				if (marketPosition == MarketPosition.Flat && !breakEvenArmed)
					ClearBreakEvenLine();
				else if (breakEvenDirection == TradeDirection.Long && marketPosition == MarketPosition.Short)
					ClearBreakEvenLine();
				else if (breakEvenDirection == TradeDirection.Short && marketPosition == MarketPosition.Long)
					ClearBreakEvenLine();
			}

			if (marketPosition == MarketPosition.Flat)
				ClearTargetStopLines();
			else if (lastMarketPosition == MarketPosition.Long && marketPosition == MarketPosition.Short)
				ClearTargetStopLines();
			else if (lastMarketPosition == MarketPosition.Short && marketPosition == MarketPosition.Long)
				ClearTargetStopLines();

			if (lastMarketPosition != marketPosition)
			{
				if (marketPosition == MarketPosition.Flat)
				{
					ClearHedgeLock(GetInstrumentKey());
					if (DebugLogging)
						LogTrade(activeTradeTag, string.Format(
							"EXIT FILLED reason={0} price={1} qty={2} order={3}",
							FormatExitReason(orderName),
							price,
							quantity,
							orderName), true);
					entryIfvgDirection = null;
					activeTradeTag = null;
					pendingTradeTag = null;
					FinalizeBreakEvenLine();
				}
				else
				{
					if (!string.IsNullOrEmpty(pendingTradeTag))
						activeTradeTag = pendingTradeTag;
					breakEvenArmed = false;
					if (DebugLogging)
						LogTrade(activeTradeTag, string.Format(
							"ENTRY FILLED {0} price={1} qty={2} order={3}",
							marketPosition,
							price,
							quantity,
							orderName), true);
					pendingTradeTag = null;
				}
			}

			lastMarketPosition = marketPosition;
		}

		private void PruneFvgs(int drawLimitDays)
		{
			if (drawLimitDays <= 0)
				return;

			DateTime cutoffDate = Time[0].Date.AddDays(-(drawLimitDays - 1));
			for (int i = activeFvgs.Count - 1; i >= 0; i--)
			{
				FvgBox fvg = activeFvgs[i];
				if (fvg.SessionDate < cutoffDate)
				{
					RemoveDrawObject(fvg.Tag);
					activeFvgs.RemoveAt(i);
				}
			}
		}

		private void PruneBreakEvenFvgs(int drawLimitDays)
		{
			if (drawLimitDays <= 0)
				return;

			DateTime cutoffDate = Time[0].Date.AddDays(-(drawLimitDays - 1));
			for (int i = breakEvenFvgs.Count - 1; i >= 0; i--)
			{
				FvgBox fvg = breakEvenFvgs[i];
				if (fvg.SessionDate < cutoffDate)
					breakEvenFvgs.RemoveAt(i);
			}
		}

		private void PruneHtfFvgs(int drawLimitDays)
		{
			if (drawLimitDays <= 0)
				return;

			DateTime cutoffDate = Time[0].Date.AddDays(-(drawLimitDays - 1));
			for (int i = activeHtfFvgs.Count - 1; i >= 0; i--)
			{
				HtfFvgBox fvg = activeHtfFvgs[i];
				if (fvg.SessionDate < cutoffDate)
				{
					RemoveDrawObject(fvg.Tag);
					activeHtfFvgs.RemoveAt(i);
				}
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Inversed FVGs", Description = "Draw invalidated FVGs using the inverted color.", GroupName = "C - FVG", Order = 0)]
		public bool ShowInvalidatedFvgs
		{
			get { return showInvalidatedFvgs; }
			set { showInvalidatedFvgs = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Min FVG Size (Points)", Description = "Minimum FVG size in points required to draw and track.", GroupName = "G - Session 1 (Asia)", Order = 10)]
		public double MinFvgSizePoints
		{
			get { return minFvgSizePoints; }
			set { minFvgSizePoints = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Max FVG Size (Points)", Description = "Maximum FVG size in points allowed for drawing and tracking.", GroupName = "G - Session 1 (Asia)", Order = 11)]
		public double MaxFvgSizePoints
		{
			get { return maxFvgSizePoints; }
			set { maxFvgSizePoints = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "FVG Draw Limit", Description = "Number of days of FVGs to keep on the chart.", GroupName = "G - Session 1 (Asia)", Order = 12)]
		public int FvgDrawLimit
		{
			get { return fvgDrawLimit; }
			set { fvgDrawLimit = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Combine FVG Series", Description = "Merge back-to-back FVGs into a single combined zone.", GroupName = "G - Session 1 (Asia)", Order = 13)]
		public bool CombineFvgSeries
		{
			get { return combineFvgSeries; }
			set { combineFvgSeries = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Min FVG Size (Points)", Description = "Minimum FVG size in points required to draw and track.", GroupName = "H - Session 2 (London)", Order = 10)]
		public double London_MinFvgSizePoints
		{
			get { return londonMinFvgSizePoints; }
			set { londonMinFvgSizePoints = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Max FVG Size (Points)", Description = "Maximum FVG size in points allowed for drawing and tracking.", GroupName = "H - Session 2 (London)", Order = 11)]
		public double London_MaxFvgSizePoints
		{
			get { return londonMaxFvgSizePoints; }
			set { londonMaxFvgSizePoints = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "FVG Draw Limit", Description = "Number of days of FVGs to keep on the chart.", GroupName = "H - Session 2 (London)", Order = 12)]
		public int London_FvgDrawLimit
		{
			get { return londonFvgDrawLimit; }
			set { londonFvgDrawLimit = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Combine FVG Series", Description = "Merge back-to-back FVGs into a single combined zone.", GroupName = "H - Session 2 (London)", Order = 13)]
		public bool London_CombineFvgSeries
		{
			get { return londonCombineFvgSeries; }
			set { londonCombineFvgSeries = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Min FVG Size (Points)", Description = "Minimum FVG size in points required to draw and track.", GroupName = "I - Session 3 (New York)", Order = 10)]
		public double NewYork_MinFvgSizePoints
		{
			get { return newYorkMinFvgSizePoints; }
			set { newYorkMinFvgSizePoints = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Max FVG Size (Points)", Description = "Maximum FVG size in points allowed for drawing and tracking.", GroupName = "I - Session 3 (New York)", Order = 11)]
		public double NewYork_MaxFvgSizePoints
		{
			get { return newYorkMaxFvgSizePoints; }
			set { newYorkMaxFvgSizePoints = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "FVG Draw Limit", Description = "Number of days of FVGs to keep on the chart.", GroupName = "I - Session 3 (New York)", Order = 12)]
		public int NewYork_FvgDrawLimit
		{
			get { return newYorkFvgDrawLimit; }
			set { newYorkFvgDrawLimit = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Combine FVG Series", Description = "Merge back-to-back FVGs into a single combined zone.", GroupName = "I - Session 3 (New York)", Order = 13)]
		public bool NewYork_CombineFvgSeries
		{
			get { return newYorkCombineFvgSeries; }
			set { newYorkCombineFvgSeries = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Swing Liquidity Sweep", Description = "Require a swing sweep to qualify entries.", GroupName = "B - Trade Config", Order = 0)]
		public bool UseSwingLiquiditySweep
		{
			get { return useSwingLiquiditySweep; }
			set { useSwingLiquiditySweep = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Session Liquidity Sweep", Description = "Require a session sweep to qualify entries.", GroupName = "B - Trade Config", Order = 1)]
		public bool UseSessionLiquiditySweep
		{
			get { return useSessionLiquiditySweep; }
			set { useSessionLiquiditySweep = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Deliver From Current FVG", Description = "Require the qualifying sweep to originate inside a prior active FVG on the current timeframe (not inversed).", GroupName = "B - Trade Config", Order = 2)]
		public bool UseDeliverFromFvg
		{
			get { return useDeliverFromFvg; }
			set { useDeliverFromFvg = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Deliver From HTF FVG", Description = "Require the qualifying sweep to originate inside a prior active higher timeframe FVG (not inversed).", GroupName = "B - Trade Config", Order = 3)]
		public bool UseDeliverFromHtfFvg
		{
			get { return useDeliverFromHtfFvg; }
			set { useDeliverFromHtfFvg = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use SMT", Description = "Require SMT divergence versus MES (MNQ only) to qualify entries.", GroupName = "B - Trade Config", Order = 4)]
		public bool UseSmt
		{
			get { return useSmt; }
			set { useSmt = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Session 1", Description = "Allow trading during the first session window.", GroupName = "F - Sessions", Order = 0)]
		public bool UseSession1
		{
			get { return useSession1; }
			set { useSession1 = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Session 2", Description = "Allow trading during the second session window.", GroupName = "F - Sessions", Order = 1)]
		public bool UseSession2
		{
			get { return useSession2; }
			set { useSession2 = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Session 3", Description = "Allow trading during the third session window.", GroupName = "F - Sessions", Order = 2)]
		public bool UseSession3
		{
			get { return useSession3; }
			set { useSession3 = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Auto Shift Session 1", Description = "Apply DST auto-shift to Session 1 times.", GroupName = "F - Sessions", Order = 3)]
		public bool AutoShiftSession1
		{
			get { return autoShiftSession1; }
			set { autoShiftSession1 = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Auto Shift Session 2", Description = "Apply DST auto-shift to Session 2 times.", GroupName = "F - Sessions", Order = 4)]
		public bool AutoShiftSession2
		{
			get { return autoShiftSession2; }
			set { autoShiftSession2 = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Auto Shift Session 3", Description = "Apply DST auto-shift to Session 3 times.", GroupName = "F - Sessions", Order = 5)]
		public bool AutoShiftSession3
		{
			get { return autoShiftSession3; }
			set { autoShiftSession3 = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Close At Session End", Description = "Flatten positions and cancel orders when the session ends.", GroupName = "F - Sessions", Order = 6)]
		public bool CloseAtSessionEnd
		{
			get { return closeAtSessionEnd; }
			set { closeAtSessionEnd = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Session Fill", Description = "Color of the session background.", GroupName = "F - Sessions", Order = 7)]
		public Brush SessionBrush
		{
			get { return sessionBrush; }
			set { sessionBrush = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Session Start", Description = "When session 2 is starting.", GroupName = "H - Session 2 (London)", Order = 0)]
		public TimeSpan London_SessionStart
		{
			get { return sessionStart; }
			set { sessionStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Session End", Description = "When session 2 is ending.", GroupName = "H - Session 2 (London)", Order = 1)]
		public TimeSpan London_SessionEnd
		{
			get { return sessionEnd; }
			set { sessionEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "No Trades After", Description = "No new trades between this time and session end.", GroupName = "H - Session 2 (London)", Order = 2)]
		public TimeSpan London_NoTradesAfter
		{
			get { return noTradesAfter; }
			set { noTradesAfter = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Session Start", Description = "When session 3 is starting.", GroupName = "I - Session 3 (New York)", Order = 0)]
		public TimeSpan NewYork_SessionStart
		{
			get { return session2SessionStart; }
			set { session2SessionStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Session End", Description = "When session 3 is ending.", GroupName = "I - Session 3 (New York)", Order = 1)]
		public TimeSpan NewYork_SessionEnd
		{
			get { return session2SessionEnd; }
			set { session2SessionEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "No Trades After", Description = "No new trades between this time and session end.", GroupName = "I - Session 3 (New York)", Order = 2)]
		public TimeSpan NewYork_NoTradesAfter
		{
			get { return session2NoTradesAfter; }
			set { session2NoTradesAfter = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Skip Start", Description = "Start of New York skip window.", GroupName = "I - Session 3 (New York)", Order = 3)]
		public TimeSpan NewYork_SkipStart
		{
			get { return newYorkSkipStart; }
			set { newYorkSkipStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Skip End", Description = "End of New York skip window.", GroupName = "I - Session 3 (New York)", Order = 4)]
		public TimeSpan NewYork_SkipEnd
		{
			get { return newYorkSkipEnd; }
			set { newYorkSkipEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Close At Skip Start", Description = "Flatten positions and cancel orders when New York skip window starts.", GroupName = "I - Session 3 (New York)", Order = 5)]
		public bool NewYork_CloseAtSkipStart
		{
			get { return newYorkCloseAtSkipStart; }
			set { newYorkCloseAtSkipStart = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Session Start", Description = "When session 1 is starting.", GroupName = "G - Session 1 (Asia)", Order = 0)]
		public TimeSpan Asia_SessionStart
		{
			get { return session3SessionStart; }
			set { session3SessionStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Session End", Description = "When session 1 is ending.", GroupName = "G - Session 1 (Asia)", Order = 1)]
		public TimeSpan Asia_SessionEnd
		{
			get { return session3SessionEnd; }
			set { session3SessionEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "No Trades After", Description = "No new trades between this time and session end.", GroupName = "G - Session 1 (Asia)", Order = 2)]
		public TimeSpan Asia_NoTradesAfter
		{
			get { return session3NoTradesAfter; }
			set { session3NoTradesAfter = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Volume SMA Filter", Description = "Require fast volume SMA to exceed slow volume SMA by a multiplier.", GroupName = "B - Trade Config", Order = 5)]
		public bool UseVolumeSmaFilter
		{
			get { return useVolumeSmaFilter; }
			set { useVolumeSmaFilter = value; }
		}

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Volume SMA Fast Period", Description = "Fast period for the volume SMA filter.", GroupName = "B - Trade Config", Order = 6)]
		public int VolumeFastSmaPeriod
		{
			get { return volumeFastSmaPeriod; }
			set { volumeFastSmaPeriod = value; }
		}

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Volume SMA Slow Period", Description = "Slow period for the volume SMA filter.", GroupName = "B - Trade Config", Order = 7)]
		public int VolumeSlowSmaPeriod
		{
			get { return volumeSlowSmaPeriod; }
			set { volumeSlowSmaPeriod = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(Name = "Volume SMA Multiplier", Description = "Fast volume SMA must be greater than slow SMA times this multiplier.", GroupName = "B - Trade Config", Order = 8)]
		public double VolumeSmaMultiplier
		{
			get { return volumeSmaMultiplier; }
			set { volumeSmaMultiplier = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Max Bars Between Sweep And iFVG", Description = "Maximum bars allowed between the sweep and the iFVG invalidation.", GroupName = "B - Trade Config", Order = 9)]
		public int MaxBarsBetweenSweepAndIfvg
		{
			get { return maxBarsBetweenSweepAndIfvg; }
			set { maxBarsBetweenSweepAndIfvg = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Enable Historical Trading", Description = "Allow entries during historical or playback bars.", GroupName = "A - General", Order = 1)]
		public bool EnableHistoricalTrading
		{
			get { return enableHistoricalTrading; }
			set { enableHistoricalTrading = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Debug Logging", Description = "Log key entry and exit decisions to the Output window.", GroupName = "A - General", Order = 2)]
		public bool DebugLogging
		{
			get { return debugLogging; }
			set { debugLogging = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Verbose Debug Logging", Description = "Include FVG detection and filtering logs in debug output.", GroupName = "A - General", Order = 3)]
		public bool VerboseDebugLogging
		{
			get { return verboseDebugLogging; }
			set { verboseDebugLogging = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Invalidate If Target Hit Before Entry", Description = "Skip entries if the target is already hit before the bar closes.", GroupName = "B - Trade Config", Order = 10)]
		public bool InvalidateIfTargetHitBeforeEntry
		{
			get { return invalidateIfTargetHitBeforeEntry; }
			set { invalidateIfTargetHitBeforeEntry = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(Name = "Min TP/SL Distance (Points)", Description = "Minimum distance from entry for TP/SL pivot selection.", GroupName = "G - Session 1 (Asia)", Order = 14)]
		public double MinTpSlDistancePoints
		{
			get { return minTpSlDistancePoints; }
			set { minTpSlDistancePoints = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(Name = "Min TP/SL Distance (Points)", Description = "Minimum distance from entry for TP/SL pivot selection.", GroupName = "H - Session 2 (London)", Order = 14)]
		public double London_MinTpSlDistancePoints
		{
			get { return londonMinTpSlDistancePoints; }
			set { londonMinTpSlDistancePoints = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(Name = "Min TP/SL Distance (Points)", Description = "Minimum distance from entry for TP/SL pivot selection.", GroupName = "I - Session 3 (New York)", Order = 14)]
		public double NewYork_MinTpSlDistancePoints
		{
			get { return newYorkMinTpSlDistancePoints; }
			set { newYorkMinTpSlDistancePoints = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Exit On Close Beyond Entry iFVG", Description = "Exit if a bar closes beyond the iFVG that triggered the entry.", GroupName = "B - Trade Config", Order = 12)]
		public bool ExitOnCloseBeyondEntryIfvg
		{
			get { return exitOnCloseBeyondEntryIfvg; }
			set { exitOnCloseBeyondEntryIfvg = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use BE Wick Line", Description = "Draw a BE line at the first TP wick and move SL to entry when hit; TP uses the next pivot.", GroupName = "B - Trade Config", Order = 13)]
		public bool UseBreakEvenWickLine
		{
			get { return useBreakEvenWickLine; }
			set { useBreakEvenWickLine = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Anti Hedge", Description = "Prevent entries that would open an opposite-direction position.", GroupName = "B - Trade Config", Order = 14)]
		public bool AntiHedge
		{
			get { return antiHedge; }
			set { antiHedge = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Block When In Position", Description = "Prevent entries if any position is already open.", GroupName = "B - Trade Config", Order = 15)]
		public bool BlockWhenInPosition
		{
			get { return blockWhenInPosition; }
			set { blockWhenInPosition = value; }
		}

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Contracts", Description = "Number of contracts to trade per entry.", GroupName = "A - General", Order = 0)]
		public int Contracts
		{
			get { return contracts; }
			set { contracts = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Monday", Description = "Allow trading on Mondays.", GroupName = "D - Trade Days", Order = 0)]
		public bool TradeMonday
		{
			get { return tradeMonday; }
			set { tradeMonday = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Tuesday", Description = "Allow trading on Tuesdays.", GroupName = "D - Trade Days", Order = 1)]
		public bool TradeTuesday
		{
			get { return tradeTuesday; }
			set { tradeTuesday = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Wednesday", Description = "Allow trading on Wednesdays.", GroupName = "D - Trade Days", Order = 2)]
		public bool TradeWednesday
		{
			get { return tradeWednesday; }
			set { tradeWednesday = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Thursday", Description = "Allow trading on Thursdays.", GroupName = "D - Trade Days", Order = 3)]
		public bool TradeThursday
		{
			get { return tradeThursday; }
			set { tradeThursday = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Friday", Description = "Allow trading on Fridays.", GroupName = "D - Trade Days", Order = 4)]
		public bool TradeFriday
		{
			get { return tradeFriday; }
			set { tradeFriday = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Skip Start", Description = "Start of skip window (chart time).", GroupName = "E - Skip Times", Order = 0)]
		public TimeSpan SkipStart
		{
			get { return skipStart; }
			set { skipStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Skip End", Description = "End of skip window (chart time).", GroupName = "E - Skip Times", Order = 1)]
		public TimeSpan SkipEnd
		{
			get { return skipEnd; }
			set { skipEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Close At Skip Start", Description = "Flatten positions and cancel orders when skip window starts.", GroupName = "E - Skip Times", Order = 2)]
		public bool CloseAtSkipStart
		{
			get { return closeAtSkipStart; }
			set { closeAtSkipStart = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Asia Session Start", Description = "Session start time for Asia liquidity tracking.", GroupName = "J - Session Liquidity", Order = 0)]
		public TimeSpan AsiaSessionStart
		{
			get { return asiaSessionStart; }
			set { asiaSessionStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Asia Session End", Description = "Session end time for Asia liquidity tracking.", GroupName = "J - Session Liquidity", Order = 1)]
		public TimeSpan AsiaSessionEnd
		{
			get { return asiaSessionEnd; }
			set { asiaSessionEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "London Session Start", Description = "Session start time for London liquidity tracking.", GroupName = "J - Session Liquidity", Order = 2)]
		public TimeSpan LondonSessionStart
		{
			get { return londonSessionStart; }
			set { londonSessionStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "London Session End", Description = "Session end time for London liquidity tracking.", GroupName = "J - Session Liquidity", Order = 3)]
		public TimeSpan LondonSessionEnd
		{
			get { return londonSessionEnd; }
			set { londonSessionEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Session Draw Limit", Description = "Number of days of session liquidity lines to keep.", GroupName = "J - Session Liquidity", Order = 4)]
		public int SessionDrawLimit
		{
			get { return sessionDrawLimit; }
			set { sessionDrawLimit = value; }
		}

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Swings", Description = "Bars on each side required to form a swing pivot.", GroupName = "G - Session 1 (Asia)", Order = 15)]
		public int SwingStrength
		{
			get { return swingStrength; }
			set { swingStrength = value; }
		}

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Swings", Description = "Bars on each side required to form a swing pivot.", GroupName = "H - Session 2 (London)", Order = 15)]
		public int London_SwingStrength
		{
			get { return londonSwingStrength; }
			set { londonSwingStrength = value; }
		}

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Swings", Description = "Bars on each side required to form a swing pivot.", GroupName = "I - Session 3 (New York)", Order = 15)]
		public int NewYork_SwingStrength
		{
			get { return newYorkSwingStrength; }
			set { newYorkSwingStrength = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Swing Draw Bars", Description = "Number of bars to keep swing lines visible.", GroupName = "K - Swing Liquidity", Order = 1)]
		public int SwingDrawBars
		{
			get { return swingDrawBars; }
			set { swingDrawBars = value; }
		}

		#endregion
	}
}
