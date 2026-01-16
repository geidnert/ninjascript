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
		}

		private List<FvgBox> activeFvgs;
		private int fvgCounter;
		private Brush fvgFill;
		private int fvgOpacity;
		private bool showInvalidatedFvgs;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Calculate = Calculate.OnBarClose;
				Name = "IFVG";
				IsOverlay = true;
				fvgOpacity = 10;
				showInvalidatedFvgs = true;
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
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0)
				return;

			if (CurrentBar < 2)
				return;

			UpdateActiveFvgs();
			DetectNewFvg();
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
					fvg.IsActive = false;
					if (!ShowInvalidatedFvgs)
						RemoveDrawObject(fvg.Tag);
				}
			}
		}

		private void DetectNewFvg()
		{
			// FVG detection uses the 3-bar displacement: bar[2] -> bar[0].
			bool bullishFvg = Low[0] > High[2];
			bool bearishFvg = High[0] < Low[2];

			if (!bullishFvg && !bearishFvg)
				return;

			FvgBox fvg = new FvgBox();
			fvg.IsBullish = bullishFvg;
			fvg.Lower = bullishFvg ? High[2] : High[0];
			fvg.Upper = bullishFvg ? Low[0] : Low[2];
			fvg.StartBarIndex = CurrentBar - 2;
			fvg.EndBarIndex = CurrentBar;
			fvg.IsActive = true;
			fvg.Tag = string.Format("IFVG_{0}_{1:yyyyMMdd_HHmmss}", fvgCounter++, Time[0]);

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
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(ResourceType = typeof(Custom.Resource), Name = "ShowInvalidatedFvgs", GroupName = "NinjaScriptParameters", Order = 0)]
		public bool ShowInvalidatedFvgs
		{
			get { return showInvalidatedFvgs; }
			set { showInvalidatedFvgs = value; }
		}
		#endregion
	}
}
