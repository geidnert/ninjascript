#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
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
	public class IFVG : Strategy
	{
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
		private int swingDrawBars;
		private Brush swingLineBrush;
		private List<SwingLine> swingLines;
		private bool useSwingLiquiditySweep;
		private bool useSessionLiquiditySweep;
		private bool useDeliverFromFvg;
		private bool useDeliverFromHtfFvg;
		private bool useSmt;
		private int htfBarsInProgress = -1;
		private BarsPeriodType htfBarsPeriodType = BarsPeriodType.Minute;
		private int htfBarsPeriodValue = 5;
		private int smtBarsInProgress = -1;
		private int maxBarsBetweenSweepAndIfvg;
		private bool enableHistoricalTrading;
		private int lastEntryBar;
		private SweepEvent lastSwingSweep;
		private SweepEvent lastSessionSweep;
		private bool debugLogging;
		private bool verboseDebugLogging;
		private bool invalidateIfTargetHitBeforeEntry;
		private double minTpSlDistancePoints;
		private bool exitOnCloseBeyondEntryIfvg;
		private MarketPosition lastMarketPosition;
		private int intrabarTargetBarIndex;
		private bool intrabarTargetHitLong;
		private bool intrabarTargetHitShort;
		private double intrabarTargetPriceLong;
		private double intrabarTargetPriceShort;
		private bool useBreakEvenWickLine;
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
				Name = "IFVG";
				IsOverlay = true;
				fvgOpacity = 10;
				invalidatedOpacity = 10;
				showInvalidatedFvgs = true;
				minFvgSizePoints = 10;
				maxFvgSizePoints = 50;
				AsiaSessionStart = asiaSessionStart;
				AsiaSessionEnd = asiaSessionEnd;
				LondonSessionStart = londonSessionStart;
				LondonSessionEnd = londonSessionEnd;
				fvgDrawLimit = 2;
				combineFvgSeries = false;
				sessionDrawLimit = 2;
				swingStrength = 10;
				swingDrawBars = 300;
				useSwingLiquiditySweep = true;
				useSessionLiquiditySweep = true;
				maxBarsBetweenSweepAndIfvg = 20;
				enableHistoricalTrading = false;
				useDeliverFromFvg = false;
				useDeliverFromHtfFvg = false;
				useSmt = false;
				lastEntryBar = -1;
				debugLogging = false;
				verboseDebugLogging = false;
				invalidateIfTargetHitBeforeEntry = false;
				minTpSlDistancePoints = 0;
				exitOnCloseBeyondEntryIfvg = false;
				useBreakEvenWickLine = false;
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
				pendingTradeTag = null;
				activeTradeTag = null;
				lastTradeLabelPrinted = null;
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
				breakEvenLineBrush = Brushes.SeaGreen;
				if (breakEvenLineBrush.CanFreeze)
					breakEvenLineBrush.Freeze();
				breakEvenTags = new List<string>();
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

			UpdateLiquidity();
			UpdateFvgs();
			if (UseDeliverFromHtfFvg)
				DrawActiveHtfFvgs();
			UpdateSwingLiquidity();
			UpdateBreakEvenLine();
			CheckExitOnCloseBeyondEntryIfvg();
		}

		private void UpdateFvgs()
		{
			if (FvgDrawLimit <= 0)
			{
				if (activeFvgs.Count > 0)
				{
					for (int i = 0; i < activeFvgs.Count; i++)
						RemoveDrawObject(activeFvgs[i].Tag);
					activeFvgs.Clear();
				}
				return;
			}

			PruneFvgs(FvgDrawLimit);
			UpdateActiveFvgs();
			DetectNewFvg();
		}

		private void UpdateHtfFvgs()
		{
			if (!UseDeliverFromHtfFvg)
				return;
			if (htfBarsInProgress < 0 || CurrentBars[htfBarsInProgress] < 2)
				return;

			if (FvgDrawLimit > 0)
				PruneHtfFvgs(FvgDrawLimit);

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
			int primaryMinutes = BarsPeriod.BarsPeriodType == BarsPeriodType.Minute ? BarsPeriod.Value : 1;
			HtfMapping mapping = ResolveHtfMapping(primaryMinutes);
			htfBarsPeriodType = BarsPeriodType.Minute;
			htfBarsPeriodValue = mapping.HtfMinutes;
			if (htfBarsPeriodValue > 0)
			{
				AddDataSeries(htfBarsPeriodType, htfBarsPeriodValue);
				htfBarsInProgress = BarsArray.Length - 1;
			}
		}

		private void ConfigureSmtSeries()
		{
			if (!string.Equals(Instrument.MasterInstrument.Name, "MNQ", StringComparison.OrdinalIgnoreCase))
				return;

			string mesInstrument = ResolveMesInstrumentName();
			AddDataSeries(mesInstrument, BarsPeriod.BarsPeriodType, BarsPeriod.Value);
			smtBarsInProgress = BarsArray.Length - 1;
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
				"IFVG_{0}_{1}_{2:yyyyMMdd}",
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
				bool hasMnq = TryGetLastTwoSwingHighs(0, SwingStrength, out mnqLast, out mnqPrev, out mnqLastAgo, out mnqPrevAgo, out mnqCount);
				bool hasMes = TryGetLastTwoSwingHighs(smtBarsInProgress, SwingStrength, out mesLast, out mesPrev, out mesLastAgo, out mesPrevAgo, out mesCount);
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

			bool hasMnqLow = TryGetLastTwoSwingLows(0, SwingStrength, out mnqLast, out mnqPrev, out mnqLastAgo, out mnqPrevAgo, out mnqCount);
			bool hasMesLow = TryGetLastTwoSwingLows(smtBarsInProgress, SwingStrength, out mesLast, out mesPrev, out mesLastAgo, out mesPrevAgo, out mesCount);
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

			string tagBase = string.Format("IFVG_SMT_{0}_{1:yyyyMMdd_HHmmss}", direction, Time[0]);

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


		private void TryEnterFromIfvg(TradeDirection direction, double fvgLower, double fvgUpper, string fvgTag)
		{
			if (State == State.Historical && !EnableHistoricalTrading)
			{
				LogTrade(fvgTag, "BLOCKED (HistoricalDisabled)", false);
				return;
			}
			if (lastEntryBar == CurrentBar)
			{
				LogTrade(fvgTag, "BLOCKED (AlreadyEnteredThisBar)", false);
				return;
			}
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				LogTrade(fvgTag, string.Format("BLOCKED (PositionNotFlat: {0})", Position.MarketPosition), false);
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

			double entryPrice = Close[0];
			const int slOccurrence = 1;
			double firstTargetPrice;
			int firstTargetBarsAgo;
			bool hasFirstTarget = direction == TradeDirection.Long
				? TryGetNthBullishPivotHighAboveEntry(entryPrice, 1, MinTpSlDistancePoints, out firstTargetPrice, out firstTargetBarsAgo)
				: TryGetNthBearishPivotLowBelowEntry(entryPrice, 1, MinTpSlDistancePoints, out firstTargetPrice, out firstTargetBarsAgo);
			if (!hasFirstTarget)
			{
				LogTrade(fvgTag, string.Format("BLOCKED (NoTarget: {0} entry={1})", direction, entryPrice), false);
				return;
			}

			double targetPrice = firstTargetPrice;
			int targetBarsAgo = firstTargetBarsAgo;

			if (UseBreakEvenWickLine)
			{
				double minBeGap = MinTpSlDistancePoints;
				bool foundTarget = false;
				for (int occurrence = 2; occurrence <= 10; occurrence++)
				{
					double candidatePrice;
					int candidateBarsAgo;
					bool hasCandidate = direction == TradeDirection.Long
						? TryGetNthBullishPivotHighAboveEntry(entryPrice, occurrence, MinTpSlDistancePoints, out candidatePrice, out candidateBarsAgo)
						: TryGetNthBearishPivotLowBelowEntry(entryPrice, occurrence, MinTpSlDistancePoints, out candidatePrice, out candidateBarsAgo);
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
					? TryGetNthBearishPivotLowBelowEntry(entryPrice, stopOccurrence, MinTpSlDistancePoints, out stopPrice, out stopBarsAgo)
					: TryGetNthBullishPivotHighAboveEntry(entryPrice, stopOccurrence, MinTpSlDistancePoints, out stopPrice, out stopBarsAgo);

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

			if (UseBreakEvenWickLine)
			{
				ActivateBreakEvenLine(fvgTag, direction, firstTargetPrice, firstTargetBarsAgo);
				LogTrade(fvgTag, string.Format("BE LINE price={0} barsAgo={1}", firstTargetPrice, firstTargetBarsAgo), false);
			}

			stopPrice = Instrument.MasterInstrument.RoundToTickSize(stopPrice);

			string signalName = direction == TradeDirection.Long ? "Long" : "Short";
			SetStopLoss(signalName, CalculationMode.Price, stopPrice, false);
			SetProfitTarget(signalName, CalculationMode.Price, targetPrice);

			string beInfo = UseBreakEvenWickLine ? string.Format(" be={0}", Instrument.MasterInstrument.RoundToTickSize(firstTargetPrice)) : string.Empty;
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

			if (direction == TradeDirection.Long)
				EnterLong(signalName);
			else
				EnterShort(signalName);

			lastEntryBar = CurrentBar;
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

		private static DateTime GetSessionStartDate(DateTime barTime, TimeSpan start, TimeSpan end)
		{
			if (start < end)
				return barTime.Date;
			return barTime.TimeOfDay < end ? barTime.Date.AddDays(-1) : barTime.Date;
		}

		private static string GetLiquidityLabel(string sessionName, bool isHigh)
		{
			if (string.Equals(sessionName, "Asia", StringComparison.OrdinalIgnoreCase))
				return isHigh ? "AS.H" : "AS.L";
			if (string.Equals(sessionName, "London", StringComparison.OrdinalIgnoreCase))
				return isHigh ? "LO.H" : "LO.L";
			return isHigh ? "H" : "L";
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

			if (CurrentBar < SwingStrength * 2)
				return;

			int pivotBarsAgo = SwingStrength;
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
			string tag = string.Format("IFVG_Swing_{0}_{1}", isHigh ? "H" : "L", startBarIndex);

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
			if (!CombineFvgSeries)
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
			fvg.Tag = string.Format("IFVG_{0}_{1:yyyyMMdd_HHmmss}", fvgCounter++, Time[0]);

			double fvgSizePoints = Math.Abs(fvg.Upper - fvg.Lower);
			if (MinFvgSizePoints > 0 && fvgSizePoints < MinFvgSizePoints)
			{
				LogVerbose(string.Format("FVG rejected: size {0} < min {1}", fvgSizePoints, MinFvgSizePoints));
				return;
			}
			if (MaxFvgSizePoints > 0 && fvgSizePoints > MaxFvgSizePoints)
			{
				LogVerbose(string.Format("FVG rejected: size {0} > max {1}", fvgSizePoints, MaxFvgSizePoints));
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
			fvg.Tag = string.Format("IFVG_HTF_{0}_{1:yyyyMMdd_HHmmss}", htfFvgCounter++, Times[htfBarsInProgress][0]);

			double fvgSizePoints = Math.Abs(fvg.Upper - fvg.Lower);
			if (MinFvgSizePoints > 0 && fvgSizePoints < MinFvgSizePoints)
				return;
			if (MaxFvgSizePoints > 0 && fvgSizePoints > MaxFvgSizePoints)
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
			if (orderName.IndexOf("IFVGCloseExit", StringComparison.OrdinalIgnoreCase) >= 0)
				return "IFVG close exit";
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
				"EXIT SIGNAL (IFVGClose) {0} close={1} fvgLower={2} fvgUpper={3}",
				entryIfvgDirection,
				Close[0],
				entryIfvgLower,
				entryIfvgUpper), true);

			if (entryIfvgDirection == TradeDirection.Long)
				ExitLong("IFVGCloseExit", "Long");
			else
				ExitShort("IFVGCloseExit", "Short");
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
				TryGetNthBullishPivotHighAboveEntry(entryPrice, 1, MinTpSlDistancePoints, out targetPrice, out targetBarsAgo))
			{
				targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);
				intrabarTargetPriceLong = targetPrice;
				if (Highs[0][0] >= targetPrice)
					intrabarTargetHitLong = true;
			}

			if (!intrabarTargetHitShort &&
				TryGetNthBearishPivotLowBelowEntry(entryPrice, 1, MinTpSlDistancePoints, out targetPrice, out targetBarsAgo))
			{
				targetPrice = Instrument.MasterInstrument.RoundToTickSize(targetPrice);
				intrabarTargetPriceShort = targetPrice;
				if (Lows[0][0] <= targetPrice)
					intrabarTargetHitShort = true;
			}
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (!DebugLogging)
				return;

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

			if (lastMarketPosition != marketPosition)
			{
				if (marketPosition == MarketPosition.Flat)
				{
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
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Inversed FVGs", Description = "Draw invalidated FVGs using the inverted color.", GroupName = "FVG", Order = 0)]
		public bool ShowInvalidatedFvgs
		{
			get { return showInvalidatedFvgs; }
			set { showInvalidatedFvgs = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Min FVG Size (Points)", Description = "Minimum FVG size in points required to draw and track.", GroupName = "FVG", Order = 1)]
		public double MinFvgSizePoints
		{
			get { return minFvgSizePoints; }
			set { minFvgSizePoints = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Max FVG Size (Points)", Description = "Maximum FVG size in points allowed for drawing and tracking.", GroupName = "FVG", Order = 2)]
		public double MaxFvgSizePoints
		{
			get { return maxFvgSizePoints; }
			set { maxFvgSizePoints = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "FVG Draw Limit", Description = "Number of days of FVGs to keep on the chart.", GroupName = "FVG", Order = 3)]
		public int FvgDrawLimit
		{
			get { return fvgDrawLimit; }
			set { fvgDrawLimit = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Combine FVG Series", Description = "Merge back-to-back FVGs into a single combined zone.", GroupName = "FVG", Order = 4)]
		public bool CombineFvgSeries
		{
			get { return combineFvgSeries; }
			set { combineFvgSeries = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Swing Liquidity Sweep", Description = "Require a swing sweep to qualify entries.", GroupName = "Trade Config", Order = 0)]
		public bool UseSwingLiquiditySweep
		{
			get { return useSwingLiquiditySweep; }
			set { useSwingLiquiditySweep = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Session Liquidity Sweep", Description = "Require a session sweep to qualify entries.", GroupName = "Trade Config", Order = 1)]
		public bool UseSessionLiquiditySweep
		{
			get { return useSessionLiquiditySweep; }
			set { useSessionLiquiditySweep = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Deliver From Current FVG", Description = "Require the qualifying sweep to originate inside a prior active FVG on the current timeframe (not inversed).", GroupName = "Trade Config", Order = 2)]
		public bool UseDeliverFromFvg
		{
			get { return useDeliverFromFvg; }
			set { useDeliverFromFvg = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Deliver From HTF FVG", Description = "Require the qualifying sweep to originate inside a prior active higher timeframe FVG (not inversed).", GroupName = "Trade Config", Order = 3)]
		public bool UseDeliverFromHtfFvg
		{
			get { return useDeliverFromHtfFvg; }
			set { useDeliverFromHtfFvg = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use SMT", Description = "Require SMT divergence versus MES (MNQ only) to qualify entries.", GroupName = "Trade Config", Order = 4)]
		public bool UseSmt
		{
			get { return useSmt; }
			set { useSmt = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Max Bars Between Sweep And IFVG", Description = "Maximum bars allowed between the sweep and the IFVG invalidation.", GroupName = "Trade Config", Order = 5)]
		public int MaxBarsBetweenSweepAndIfvg
		{
			get { return maxBarsBetweenSweepAndIfvg; }
			set { maxBarsBetweenSweepAndIfvg = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Enable Historical Trading", Description = "Allow entries during historical or playback bars.", GroupName = "Trade Config", Order = 6)]
		public bool EnableHistoricalTrading
		{
			get { return enableHistoricalTrading; }
			set { enableHistoricalTrading = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Debug Logging", Description = "Log key entry and exit decisions to the Output window.", GroupName = "Trade Config", Order = 7)]
		public bool DebugLogging
		{
			get { return debugLogging; }
			set { debugLogging = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Verbose Debug Logging", Description = "Include FVG detection and filtering logs in debug output.", GroupName = "Trade Config", Order = 8)]
		public bool VerboseDebugLogging
		{
			get { return verboseDebugLogging; }
			set { verboseDebugLogging = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Invalidate If Target Hit Before Entry", Description = "Skip entries if the target is already hit before the bar closes.", GroupName = "Trade Config", Order = 9)]
		public bool InvalidateIfTargetHitBeforeEntry
		{
			get { return invalidateIfTargetHitBeforeEntry; }
			set { invalidateIfTargetHitBeforeEntry = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(Name = "Min TP/SL Distance (Points)", Description = "Minimum distance from entry for TP/SL pivot selection.", GroupName = "Trade Config", Order = 10)]
		public double MinTpSlDistancePoints
		{
			get { return minTpSlDistancePoints; }
			set { minTpSlDistancePoints = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Exit On Close Beyond Entry IFVG", Description = "Exit if a bar closes beyond the IFVG that triggered the entry.", GroupName = "Trade Config", Order = 11)]
		public bool ExitOnCloseBeyondEntryIfvg
		{
			get { return exitOnCloseBeyondEntryIfvg; }
			set { exitOnCloseBeyondEntryIfvg = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use BE Wick Line", Description = "Draw a BE line at the first TP wick and move SL to entry when hit; TP uses the next pivot.", GroupName = "Trade Config", Order = 12)]
		public bool UseBreakEvenWickLine
		{
			get { return useBreakEvenWickLine; }
			set { useBreakEvenWickLine = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Asia Session Start", Description = "Session start time for Asia liquidity tracking.", GroupName = "Session Liquidity", Order = 0)]
		public TimeSpan AsiaSessionStart
		{
			get { return asiaSessionStart; }
			set { asiaSessionStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Asia Session End", Description = "Session end time for Asia liquidity tracking.", GroupName = "Session Liquidity", Order = 1)]
		public TimeSpan AsiaSessionEnd
		{
			get { return asiaSessionEnd; }
			set { asiaSessionEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "London Session Start", Description = "Session start time for London liquidity tracking.", GroupName = "Session Liquidity", Order = 2)]
		public TimeSpan LondonSessionStart
		{
			get { return londonSessionStart; }
			set { londonSessionStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "London Session End", Description = "Session end time for London liquidity tracking.", GroupName = "Session Liquidity", Order = 3)]
		public TimeSpan LondonSessionEnd
		{
			get { return londonSessionEnd; }
			set { londonSessionEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Session Draw Limit", Description = "Number of days of session liquidity lines to keep.", GroupName = "Session Liquidity", Order = 4)]
		public int SessionDrawLimit
		{
			get { return sessionDrawLimit; }
			set { sessionDrawLimit = value; }
		}

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Swings", Description = "Bars on each side required to form a swing pivot.", GroupName = "Liquidity", Order = 0)]
		public int SwingStrength
		{
			get { return swingStrength; }
			set { swingStrength = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Swing Draw Bars", Description = "Number of bars to keep swing lines visible.", GroupName = "Liquidity", Order = 1)]
		public int SwingDrawBars
		{
			get { return swingDrawBars; }
			set { swingDrawBars = value; }
		}

		#endregion
	}
}
