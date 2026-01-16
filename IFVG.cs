//
// Copyright (C) 2015, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
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
			public double Upper;
			public double Lower;
			public bool IsBullish;
			public bool IsActive;
			public DateTime SessionDate;
		}

		private List<FvgBox> activeFvgs;
		private int fvgCounter;
		private Brush fvgFill;
		private int fvgOpacity;
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
		private int maxBarsBetweenSweepAndIfvg;
		private bool enableHistoricalTrading;
		private int lastEntryBar;
		private SweepEvent lastSwingSweep;
		private SweepEvent lastSessionSweep;
		private bool debugLogging;
		private bool verboseDebugLogging;
		private MarketPosition lastMarketPosition;

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
				lastEntryBar = -1;
				debugLogging = false;
				verboseDebugLogging = false;
				lastMarketPosition = MarketPosition.Flat;
			}
			else if (State == State.Configure)
			{
				// Mirror SampleIntrabarBacktest style: add a 1-tick secondary series.
				AddDataSeries(Data.BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{
				activeFvgs = new List<FvgBox>();
				// Match DR.cs style: transparent outline, DodgerBlue fill, opacity handled by Draw.Rectangle.
				fvgFill = Brushes.DodgerBlue;
				if (fvgFill.CanFreeze)
					fvgFill.Freeze();
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
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;

			if (CurrentBar < 2)
				return;

			UpdateLiquidity();
			UpdateFvgs();
			UpdateSwingLiquidity();
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

			LogDebug(string.Format(
				"Sweep registered {0} price={1} bar={2} source={3}",
				direction,
				price,
				CurrentBar,
				isSession ? "session" : "swing"));
		}

		private SweepEvent GetEligibleSweep(TradeDirection direction)
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
				LogDebug(string.Format("No eligible sweep for {0}: none available", direction));
				return null;
			}

			int barsSince = CurrentBar - best.BarIndex;
			if (barsSince < 0 || barsSince > maxBarsBetweenSweepAndIfvg)
			{
				LogDebug(string.Format(
					"No eligible sweep for {0}: barsSince={1} max={2}",
					direction,
					barsSince,
					maxBarsBetweenSweepAndIfvg));
				return null;
			}

			LogDebug(string.Format(
				"Eligible sweep for {0}: price={1} bar={2} barsSince={3}",
				direction,
				best.Price,
				best.BarIndex,
				barsSince));
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

		private bool TryGetNthBullishPivotHighAboveEntry(double entryPrice, int occurrence, out double price, out int barsAgo)
		{
			price = 0;
			barsAgo = -1;
			int found = 0;

			for (int i = 1; i + 1 <= CurrentBar; i++)
			{
				if (High[i] <= High[i - 1] || High[i] <= High[i + 1])
					continue;
				if (High[i] <= entryPrice)
					continue;

				found++;
				if (found == occurrence)
				{
					price = High[i];
					barsAgo = i;
					return true;
				}
			}

			return false;
		}

		private bool TryGetNthBearishPivotLowBelowEntry(double entryPrice, int occurrence, out double price, out int barsAgo)
		{
			price = 0;
			barsAgo = -1;
			int found = 0;

			for (int i = 1; i + 1 <= CurrentBar; i++)
			{
				if (Low[i] >= Low[i - 1] || Low[i] >= Low[i + 1])
					continue;
				if (Low[i] >= entryPrice)
					continue;

				found++;
				if (found == occurrence)
				{
					price = Low[i];
					barsAgo = i;
					return true;
				}
			}

			return false;
		}

		private void TryEnterFromIfvg(TradeDirection direction)
		{
			if (State == State.Historical && !EnableHistoricalTrading)
			{
				LogDebug("Entry blocked: historical trading disabled");
				return;
			}
			if (lastEntryBar == CurrentBar)
			{
				LogDebug("Entry blocked: already entered this bar");
				return;
			}
			if (Position.MarketPosition != MarketPosition.Flat)
			{
				LogDebug(string.Format("Entry blocked: position not flat ({0})", Position.MarketPosition));
				return;
			}

			SweepEvent sweep = GetEligibleSweep(direction);
			if (sweep == null)
				return;

			double entryPrice = Close[0];
			const int tpOccurrence = 1;
			const int slOccurrence = 1;
			double targetPrice;
			int targetBarsAgo;
			bool hasTarget = direction == TradeDirection.Long
				? TryGetNthBullishPivotHighAboveEntry(entryPrice, tpOccurrence, out targetPrice, out targetBarsAgo)
				: TryGetNthBearishPivotLowBelowEntry(entryPrice, tpOccurrence, out targetPrice, out targetBarsAgo);
			if (!hasTarget)
			{
				LogDebug(string.Format("Entry blocked: no pivot target for {0} at {1}", direction, entryPrice));
				return;
			}

			double stopPrice;
			int stopBarsAgo;
			bool hasStop = direction == TradeDirection.Long
				? TryGetNthBearishPivotLowBelowEntry(entryPrice, slOccurrence, out stopPrice, out stopBarsAgo)
				: TryGetNthBullishPivotHighAboveEntry(entryPrice, slOccurrence, out stopPrice, out stopBarsAgo);
			if (!hasStop)
			{
				LogDebug(string.Format("Entry blocked: no pivot stop for {0} at {1}", direction, entryPrice));
				return;
			}

			string signalName = direction == TradeDirection.Long ? "Long" : "Short";
			SetStopLoss(signalName, CalculationMode.Price, stopPrice, false);
			SetProfitTarget(signalName, CalculationMode.Price, targetPrice);

			LogDebug(string.Format(
				"Placing {0} entry at {1} stop={2} target={3} stopBarsAgo={4} targetBarsAgo={5}",
				direction,
				entryPrice,
				stopPrice,
				targetPrice,
				stopBarsAgo,
				targetBarsAgo));

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
					LogTradeSeparator(string.Format(
						"ENTRY ATTEMPT {0} tag={1} lower={2} upper={3}",
						fvg.IsBullish ? "bullish" : "bearish",
						fvg.Tag,
						fvg.Lower,
						fvg.Upper));
					TradeDirection ifvgDirection = fvg.IsBullish ? TradeDirection.Short : TradeDirection.Long;
					TryEnterFromIfvg(ifvgDirection);
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

		private void LogDebug(string message)
		{
			if (!DebugLogging)
				return;
			Print(string.Format("IFVG DEBUG [{0}] {1}", Time[0], message));
		}

		private void LogVerbose(string message)
		{
			if (!DebugLogging || !VerboseDebugLogging)
				return;
			Print(string.Format("IFVG DEBUG [{0}] {1}", Time[0], message));
		}

		private void LogTradeSeparator(string message)
		{
			if (!DebugLogging)
				return;
			Print(string.Empty);
			Print(string.Format("IFVG DEBUG [{0}] {1}", Time[0], message));
		}

		protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (!DebugLogging)
				return;

			if (lastMarketPosition != marketPosition)
			{
				if (marketPosition == MarketPosition.Flat)
				{
					LogTradeSeparator(string.Format(
						"EXIT filled price={0} qty={1} order={2}",
						price,
						quantity,
						execution != null && execution.Order != null ? execution.Order.Name : "n/a"));
				}
				else
				{
					LogTradeSeparator(string.Format(
						"ENTRY filled {0} price={1} qty={2} order={3}",
						marketPosition,
						price,
						quantity,
						execution != null && execution.Order != null ? execution.Order.Name : "n/a"));
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

		#region Properties
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Show Inversed FVGs", GroupName = "FVG", Order = 0)]
		public bool ShowInvalidatedFvgs
		{
			get { return showInvalidatedFvgs; }
			set { showInvalidatedFvgs = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Min FVG Size (Points)", GroupName = "FVG", Order = 1)]
		public double MinFvgSizePoints
		{
			get { return minFvgSizePoints; }
			set { minFvgSizePoints = value; }
		}

		[Range(0, double.MaxValue), NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "Max FVG Size (Points)", GroupName = "FVG", Order = 2)]
		public double MaxFvgSizePoints
		{
			get { return maxFvgSizePoints; }
			set { maxFvgSizePoints = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "FVG Draw Limit", GroupName = "FVG", Order = 3)]
		public int FvgDrawLimit
		{
			get { return fvgDrawLimit; }
			set { fvgDrawLimit = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Combine FVG Series", GroupName = "FVG", Order = 4)]
		public bool CombineFvgSeries
		{
			get { return combineFvgSeries; }
			set { combineFvgSeries = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Swing Liquidity Sweep", GroupName = "Trade Config", Order = 0)]
		public bool UseSwingLiquiditySweep
		{
			get { return useSwingLiquiditySweep; }
			set { useSwingLiquiditySweep = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Use Session Liquidity Sweep", GroupName = "Trade Config", Order = 1)]
		public bool UseSessionLiquiditySweep
		{
			get { return useSessionLiquiditySweep; }
			set { useSessionLiquiditySweep = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Max Bars Between Sweep And IFVG", GroupName = "Trade Config", Order = 2)]
		public int MaxBarsBetweenSweepAndIfvg
		{
			get { return maxBarsBetweenSweepAndIfvg; }
			set { maxBarsBetweenSweepAndIfvg = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Enable Historical Trading", GroupName = "Trade Config", Order = 3)]
		public bool EnableHistoricalTrading
		{
			get { return enableHistoricalTrading; }
			set { enableHistoricalTrading = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Debug Logging", GroupName = "Trade Config", Order = 4)]
		public bool DebugLogging
		{
			get { return debugLogging; }
			set { debugLogging = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Verbose Debug Logging", GroupName = "Trade Config", Order = 5)]
		public bool VerboseDebugLogging
		{
			get { return verboseDebugLogging; }
			set { verboseDebugLogging = value; }
		}

		[NinjaScriptProperty]
		[Display(Name = "Asia Session Start", GroupName = "Session Liquidity", Order = 0)]
		public TimeSpan AsiaSessionStart
		{
			get { return asiaSessionStart; }
			set { asiaSessionStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "Asia Session End", GroupName = "Session Liquidity", Order = 1)]
		public TimeSpan AsiaSessionEnd
		{
			get { return asiaSessionEnd; }
			set { asiaSessionEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "London Session Start", GroupName = "Session Liquidity", Order = 2)]
		public TimeSpan LondonSessionStart
		{
			get { return londonSessionStart; }
			set { londonSessionStart = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[NinjaScriptProperty]
		[Display(Name = "London Session End", GroupName = "Session Liquidity", Order = 3)]
		public TimeSpan LondonSessionEnd
		{
			get { return londonSessionEnd; }
			set { londonSessionEnd = new TimeSpan(value.Hours, value.Minutes, 0); }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Session Draw Limit", GroupName = "Session Liquidity", Order = 4)]
		public int SessionDrawLimit
		{
			get { return sessionDrawLimit; }
			set { sessionDrawLimit = value; }
		}

		[Range(1, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Swings", GroupName = "Liquidity", Order = 0)]
		public int SwingStrength
		{
			get { return swingStrength; }
			set { swingStrength = value; }
		}

		[Range(0, int.MaxValue), NinjaScriptProperty]
		[Display(Name = "Swing Draw Bars", GroupName = "Liquidity", Order = 1)]
		public int SwingDrawBars
		{
			get { return swingDrawBars; }
			set { swingDrawBars = value; }
		}

		#endregion
	}
}
