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
    // Master orchestrator. DUO is the first module extraction target.
    public class MultiBotMaster : Strategy
    {
        private readonly Dictionary<int, int> timeframeToBip = new Dictionary<int, int>();
        private readonly List<IMultiBotEngine> modules = new List<IMultiBotEngine>();
        private int nextAdditionalBip = 1;
        private RiskManager riskManager;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "MultiBotMaster";
                Description = "Multi-module orchestrator (DUO module first; ORBO/ADAM/EVE ports pending).";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsInstantiatedOnEachOptimizationIteration = false;

                EnableDuo = true;
                EnableOrbo = false;
                EnableAdam = false;
                EnableEve = false;

                DuoEmaPeriod = 50;
                DuoExitCrossPoints = 0.0;
                DuoTradeDirection = DuoDirectionMode.Both;
                DuoUseSessionFilter = false;
                DuoUseNewYorkSkip = false;
                DuoNewYorkSkipStart = new DateTime(2000, 1, 1, 9, 30, 0);
                DuoNewYorkSkipEnd = new DateTime(2000, 1, 1, 9, 45, 0);
                DuoUseAdxFilter = false;
                DuoAdxPeriod = 14;
                DuoAdxMin = 0;
                DuoAdxMax = 0;
                DuoAdxMinSlopePoints = 0;
                DuoAsiaBlockSundayTrades = false;
                DuoAsiaSessionStart = new DateTime(2000, 1, 1, 0, 0, 0);
                DuoAsiaSessionEnd = new DateTime(2000, 1, 1, 6, 0, 0);
                DuoNewYorkSessionStart = new DateTime(2000, 1, 1, 13, 30, 0);
                DuoNewYorkSessionEnd = new DateTime(2000, 1, 1, 20, 0, 0);

                OrboTimeframeMinutes = 5;
                AdamTimeframeMinutes = 15;
                EveTimeframeMinutes = 60;

                OrderQuantity = 1;
                MaxTotalContracts = 2;
                MaxDailyLossCurrency = 500;
                MinBarsPerSeries = 20;
                ProcessOneIntentPerBar = true;
                SimulateSignalsOnly = false;
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
                modules.Clear();
                modules.Add(new DuoEngine(
                    0,
                    EnableDuo,
                    DuoEmaPeriod,
                    DuoExitCrossPoints,
                    DuoTradeDirection,
                    DuoUseSessionFilter,
                    DuoUseNewYorkSkip,
                    DuoNewYorkSkipStart.TimeOfDay,
                    DuoNewYorkSkipEnd.TimeOfDay,
                    DuoUseAdxFilter,
                    DuoAdxPeriod,
                    DuoAdxMin,
                    DuoAdxMax,
                    DuoAdxMinSlopePoints,
                    DuoAsiaBlockSundayTrades,
                    DuoAsiaSessionStart.TimeOfDay,
                    DuoAsiaSessionEnd.TimeOfDay,
                    DuoNewYorkSessionStart.TimeOfDay,
                    DuoNewYorkSessionEnd.TimeOfDay));
                modules.Add(new OrboEngine(GetBarsInProgress(OrboTimeframeMinutes), EnableOrbo));
                modules.Add(new AdamEngine(GetBarsInProgress(AdamTimeframeMinutes), EnableAdam));
                modules.Add(new EveEngine(GetBarsInProgress(EveTimeframeMinutes), EnableEve));

                riskManager = new RiskManager(MaxTotalContracts, MaxDailyLossCurrency);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[BarsInProgress] < MinBarsPerSeries)
                return;

            MultiBotContext context = new MultiBotContext(
                Times[BarsInProgress][0],
                BarsInProgress,
                CurrentBars[BarsInProgress],
                Opens[BarsInProgress][0],
                Highs[BarsInProgress][0],
                Lows[BarsInProgress][0],
                Closes[BarsInProgress][0],
                Position.MarketPosition,
                Position.Quantity);

            double cumulativeProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            riskManager.Sync(context.Time, cumulativeProfit);
            bool executedIntentThisBar = false;

            foreach (IMultiBotEngine module in modules.Where(m => m.IsEnabled && m.BarsInProgress == BarsInProgress))
            {
                if (ProcessOneIntentPerBar && executedIntentThisBar)
                    break;

                IList<TradeIntent> intents = module.OnBarUpdate(context);
                if (intents == null || intents.Count == 0)
                    continue;

                foreach (TradeIntent intent in intents)
                {
                    bool executed = HandleIntent(intent, cumulativeProfit);
                    if (executed)
                        executedIntentThisBar = true;

                    if (executed && ProcessOneIntentPerBar)
                        break;
                }
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

        private bool HandleIntent(TradeIntent intent, double cumulativeProfit)
        {
            if (intent == null || intent.Action == TradeIntentAction.None)
                return false;

            if (riskManager.IsDailyLossLocked(cumulativeProfit))
            {
                Print(string.Format("[{0}] Blocked by daily loss lockout: {1}", intent.ModuleName, intent.Action));
                return false;
            }

            string moduleName = string.IsNullOrWhiteSpace(intent.ModuleName) ? "UNKNOWN" : intent.ModuleName;
            string tag = string.Format("{0}_{1}_{2:HHmmss}", moduleName, intent.Action, Times[BarsInProgress][0]);

            if (SimulateSignalsOnly)
            {
                Print(string.Format("[SIM] {0} | {1}", tag, intent.Reason));
                return true;
            }

            switch (intent.Action)
            {
                case TradeIntentAction.EnterLong:
                    if (riskManager.CanEnter(Position.Quantity, OrderQuantity) && Position.MarketPosition == MarketPosition.Flat)
                    {
                        EnterLong(OrderQuantity, tag);
                        return true;
                    }
                    break;
                case TradeIntentAction.EnterShort:
                    if (riskManager.CanEnter(Position.Quantity, OrderQuantity) && Position.MarketPosition == MarketPosition.Flat)
                    {
                        EnterShort(OrderQuantity, tag);
                        return true;
                    }
                    break;
                case TradeIntentAction.ExitLong:
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        ExitLong(tag + "_X");
                        return true;
                    }
                    break;
                case TradeIntentAction.ExitShort:
                    if (Position.MarketPosition == MarketPosition.Short)
                    {
                        ExitShort(tag + "_X");
                        return true;
                    }
                    break;
            }

            return false;
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

        #region DUO
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "DuoEmaPeriod", GroupName = "DUO", Order = 10)]
        public int DuoEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "DuoExitCrossPoints", GroupName = "DUO", Order = 11)]
        public double DuoExitCrossPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoTradeDirection", GroupName = "DUO", Order = 12)]
        public DuoDirectionMode DuoTradeDirection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoUseSessionFilter", GroupName = "DUO", Order = 13)]
        public bool DuoUseSessionFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoUseNewYorkSkip", GroupName = "DUO", Order = 14)]
        public bool DuoUseNewYorkSkip { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoNewYorkSkipStart", GroupName = "DUO", Order = 15)]
        public DateTime DuoNewYorkSkipStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoNewYorkSkipEnd", GroupName = "DUO", Order = 16)]
        public DateTime DuoNewYorkSkipEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoUseAdxFilter", GroupName = "DUO", Order = 17)]
        public bool DuoUseAdxFilter { get; set; }

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "DuoAdxPeriod", GroupName = "DUO", Order = 18)]
        public int DuoAdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "DuoAdxMin", GroupName = "DUO", Order = 19)]
        public double DuoAdxMin { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "DuoAdxMax", GroupName = "DUO", Order = 20)]
        public double DuoAdxMax { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "DuoAdxMinSlopePoints", GroupName = "DUO", Order = 21)]
        public double DuoAdxMinSlopePoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoAsiaBlockSundayTrades", GroupName = "DUO", Order = 22)]
        public bool DuoAsiaBlockSundayTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoAsiaSessionStart", GroupName = "DUO", Order = 23)]
        public DateTime DuoAsiaSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoAsiaSessionEnd", GroupName = "DUO", Order = 24)]
        public DateTime DuoAsiaSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoNewYorkSessionStart", GroupName = "DUO", Order = 25)]
        public DateTime DuoNewYorkSessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "DuoNewYorkSessionEnd", GroupName = "DUO", Order = 26)]
        public DateTime DuoNewYorkSessionEnd { get; set; }
        #endregion

        #region Timeframes
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "OrboTimeframeMinutes", GroupName = "Timeframes", Order = 20)]
        public int OrboTimeframeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "AdamTimeframeMinutes", GroupName = "Timeframes", Order = 21)]
        public int AdamTimeframeMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "EveTimeframeMinutes", GroupName = "Timeframes", Order = 22)]
        public int EveTimeframeMinutes { get; set; }
        #endregion

        #region Risk
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "OrderQuantity", GroupName = "Risk", Order = 30)]
        public int OrderQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MaxTotalContracts", GroupName = "Risk", Order = 31)]
        public int MaxTotalContracts { get; set; }

        [NinjaScriptProperty]
        [Range(0, double.MaxValue)]
        [Display(Name = "MaxDailyLossCurrency", GroupName = "Risk", Order = 32)]
        public double MaxDailyLossCurrency { get; set; }
        #endregion

        #region Execution
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "MinBarsPerSeries", GroupName = "Execution", Order = 40)]
        public int MinBarsPerSeries { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProcessOneIntentPerBar", GroupName = "Execution", Order = 41)]
        public bool ProcessOneIntentPerBar { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SimulateSignalsOnly", GroupName = "Execution", Order = 42)]
        public bool SimulateSignalsOnly { get; set; }
        #endregion
    }
}
