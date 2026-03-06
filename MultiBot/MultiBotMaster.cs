#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies.AutoEdge.MultiBot
{
    public class MultiBotMaster : Strategy
    {
        private readonly Dictionary<int, int> timeframeToBip = new Dictionary<int, int>();
        private readonly List<IMultiBotEngine> engines = new List<IMultiBotEngine>();
        private int nextAdditionalBip = 1;
        private RiskManager riskManager;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MultiBotMaster";
                Description = "Master orchestrator for DUO/ORBO/ADAM/EVE engines.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;

                EnableDuo = true;
                EnableOrbo = true;
                EnableAdam = true;
                EnableEve = true;

                OrboTimeframeMinutes = 5;
                AdamTimeframeMinutes = 15;
                EveTimeframeMinutes = 60;

                OrderQuantity = 1;
                MinBarsPerSeries = 20;
                SimulateSignalsOnly = true;

                MaxTotalContracts = 2;
                MaxDailyLossCurrency = 500;
            }
            else if (State == State.Configure)
            {
                timeframeToBip.Clear();
                nextAdditionalBip = 1;

                RegisterMinuteSeries(OrboTimeframeMinutes);
                RegisterMinuteSeries(AdamTimeframeMinutes);
                RegisterMinuteSeries(EveTimeframeMinutes);
            }
            else if (State == State.DataLoaded)
            {
                engines.Clear();
                engines.Add(new DuoEngine(0, EnableDuo));
                engines.Add(new OrboEngine(GetBarsInProgress(OrboTimeframeMinutes), EnableOrbo));
                engines.Add(new AdamEngine(GetBarsInProgress(AdamTimeframeMinutes), EnableAdam));
                engines.Add(new EveEngine(GetBarsInProgress(EveTimeframeMinutes), EnableEve));

                riskManager = new RiskManager(MaxTotalContracts, MaxDailyLossCurrency);
            }
        }

        protected override void OnBarUpdate()
        {
            if (!AreSeriesReady())
                return;

            MultiBotContext context = new MultiBotContext(
                Times[BarsInProgress][0],
                BarsInProgress,
                CurrentBars[BarsInProgress],
                Closes[BarsInProgress][0],
                Position.MarketPosition,
                Position.Quantity);

            double cumulativeProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            riskManager.Sync(context.Time, cumulativeProfit);

            foreach (IMultiBotEngine engine in engines.Where(e => e.IsEnabled && e.BarsInProgress == BarsInProgress))
            {
                EngineSignal signal = engine.OnBarUpdate(context);
                HandleSignal(engine, signal, cumulativeProfit);
            }
        }

        private void RegisterMinuteSeries(int minutes)
        {
            int safeMinutes = Math.Max(1, minutes);
            if (timeframeToBip.ContainsKey(safeMinutes))
                return;

            AddDataSeries(BarsPeriodType.Minute, safeMinutes);
            timeframeToBip[safeMinutes] = nextAdditionalBip;
            nextAdditionalBip++;
        }

        private int GetBarsInProgress(int minutes)
        {
            int safeMinutes = Math.Max(1, minutes);
            int bip;

            if (timeframeToBip.TryGetValue(safeMinutes, out bip))
                return bip;

            return 0;
        }

        private bool AreSeriesReady()
        {
            if (CurrentBars[BarsInProgress] < MinBarsPerSeries)
                return false;

            foreach (int bip in timeframeToBip.Values)
            {
                if (CurrentBars[bip] < MinBarsPerSeries)
                    return false;
            }

            return true;
        }

        private void HandleSignal(IMultiBotEngine engine, EngineSignal signal, double cumulativeProfit)
        {
            if (signal == null || signal.Action == EngineSignalAction.None)
                return;

            if (riskManager.IsDailyLossLocked(cumulativeProfit))
            {
                Print(string.Format("[{0}] Signal blocked by daily loss lockout: {1}", engine.Name, signal.Action));
                return;
            }

            string tag = string.Format("{0}_{1}_{2:HHmmss}", engine.Name, signal.Action, Times[BarsInProgress][0]);

            if (SimulateSignalsOnly)
            {
                Print(string.Format("[SIM] {0} | {1} | {2}", tag, signal.Action, signal.Reason));
                return;
            }

            switch (signal.Action)
            {
                case EngineSignalAction.EnterLong:
                    if (riskManager.CanEnter(Position.Quantity, OrderQuantity) && Position.MarketPosition == MarketPosition.Flat)
                        EnterLong(OrderQuantity, tag);
                    break;
                case EngineSignalAction.EnterShort:
                    if (riskManager.CanEnter(Position.Quantity, OrderQuantity) && Position.MarketPosition == MarketPosition.Flat)
                        EnterShort(OrderQuantity, tag);
                    break;
                case EngineSignalAction.ExitLong:
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(tag + "_X");
                    break;
                case EngineSignalAction.ExitShort:
                    if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort(tag + "_X");
                    break;
            }
        }

        #region Engine Toggles
        [NinjaScriptProperty]
        [Display(Name = "EnableDuo", GroupName = "Engines", Order = 1)]
        public bool EnableDuo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EnableOrbo", GroupName = "Engines", Order = 2)]
        public bool EnableOrbo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EnableAdam", GroupName = "Engines", Order = 3)]
        public bool EnableAdam { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EnableEve", GroupName = "Engines", Order = 4)]
        public bool EnableEve { get; set; }
        #endregion

        #region Timeframes
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "OrboTimeframeMinutes", GroupName = "Timeframes", Order = 10)]
        public int OrboTimeframeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "AdamTimeframeMinutes", GroupName = "Timeframes", Order = 11)]
        public int AdamTimeframeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EveTimeframeMinutes", GroupName = "Timeframes", Order = 12)]
        public int EveTimeframeMinutes { get; set; }
        #endregion

        #region Risk
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "OrderQuantity", GroupName = "Risk", Order = 20)]
        public int OrderQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MaxTotalContracts", GroupName = "Risk", Order = 21)]
        public int MaxTotalContracts { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "MaxDailyLossCurrency", GroupName = "Risk", Order = 22)]
        public double MaxDailyLossCurrency { get; set; }
        #endregion

        #region Execution
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MinBarsPerSeries", GroupName = "Execution", Order = 30)]
        public int MinBarsPerSeries { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SimulateSignalsOnly", GroupName = "Execution", Order = 31)]
        public bool SimulateSignalsOnly { get; set; }
        #endregion
    }
}
