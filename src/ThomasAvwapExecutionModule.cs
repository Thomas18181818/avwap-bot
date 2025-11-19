#region Using declarations
using System;
using System.ComponentModel;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Module d'exécution LONG basé sur une AVWAP ancrée manuellement.
    /// Entrées déclenchées lors du contact prix/AVWAP avec validations optionnelles
    /// (imbalances, footprint). Aucun stop ni take profit n'est placé.
    /// </summary>
    public class ThomasAvwapExecutionModule : Strategy
    {
        private const string EntrySignalName = "ThomasAvwapExec";

        private ThomasAnchoredVWAP anchoredVwap;
        private DateTime currentTradingDay = Core.Globals.MinDate;
        private int tradesToday;
        private double sessionStartCumProfit;
        private int lastEntryBar = int.MinValue;

        protected override void OnStateChange()
        {
            switch (State)
            {
                case State.SetDefaults:
                    Name = "ThomasAvwapExecutionModule";
                    Description = "Entrées MNQ LONG déclenchées sur touch AVWAP avec filtres orderflow";
                    Calculate = Calculate.OnBarClose;
                    EntriesPerDirection = 8;
                    EntryHandling = EntryHandling.AllEntries;
                    IsExitOnSessionCloseStrategy = true;
                    ExitOnSessionCloseSeconds = 30;
                    IsInstantiatedOnEachOptimizationIteration = false;
                    BarsRequiredToTrade = 20;

                    BaseEntrySize = 2;
                    MaxPositionMicros = 8;
                    TouchToleranceTicks = 2;
                    CooldownBars = 3;
                    UseImbalanceFilter = false;
                    UseFootprintConfirmation = false;
                    MinimumBullishDeltaTicks = 2;
                    MaxTradesPerDay = 4;
                    MaxDailyLoss = 400;
                    break;

                case State.Configure:
                    // Pas de séries additionnelles nécessaires pour le moment.
                    break;

                case State.DataLoaded:
                    anchoredVwap = ThomasAnchoredVWAP();
                    AddChartIndicator(anchoredVwap);
                    break;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Gestion du changement de journée de trading.
            DateTime barDate = Times[0][0].Date;
            if (barDate != currentTradingDay)
            {
                currentTradingDay = barDate;
                tradesToday = 0;
                sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            }

            double sessionRealizedPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;

            if (tradesToday >= MaxTradesPerDay)
                return;

            if (sessionRealizedPnL <= -MaxDailyLoss)
                return;

            if (anchoredVwap == null)
                return;

            double avwapValue = anchoredVwap.AVWAPSeries[0];
            if (double.IsNaN(avwapValue))
                return;

            if (TickSize <= 0)
                return;

            // Bloque toute intervention si une position short existe (exécution extérieure).
            if (Position.MarketPosition == MarketPosition.Short)
                return;

            if (!IsCooldownSatisfied())
                return;

            if (!HasPriceTouchedAvwap(avwapValue))
                return;

            if (!AreOrderFlowFiltersPassing())
                return;

            int currentSize = Position.MarketPosition == MarketPosition.Long ? Position.Quantity : 0;
            int availableSize = Math.Max(0, MaxPositionMicros - currentSize);
            if (availableSize <= 0)
                return;

            int desiredSize = Math.Min(BaseEntrySize, availableSize);
            if (desiredSize <= 0)
                return;

            EnterLong(desiredSize, EntrySignalName);
        }

        private bool HasPriceTouchedAvwap(double avwapValue)
        {
            double tolerance = TouchToleranceTicks * TickSize;
            double lowerBound = Low[0] - tolerance;
            double upperBound = High[0] + tolerance;
            return avwapValue >= lowerBound && avwapValue <= upperBound;
        }

        private bool AreOrderFlowFiltersPassing()
        {
            if (!CheckImbalanceFilter())
                return false;

            if (!CheckFootprintConfirmation())
                return false;

            return true;
        }

        private bool CheckImbalanceFilter()
        {
            if (!UseImbalanceFilter)
                return true;

            double upperRange = High[0] - Math.Max(Open[0], Close[0]);
            double lowerRange = Math.Min(Open[0], Close[0]) - Low[0];
            lowerRange = Math.Max(lowerRange, TickSize);

            double ratio = upperRange / lowerRange;
            return ratio >= 1.0;
        }

        private bool CheckFootprintConfirmation()
        {
            if (!UseFootprintConfirmation)
                return true;

            double bullishDeltaTicks = (Close[0] - Open[0]) / TickSize;
            return bullishDeltaTicks >= MinimumBullishDeltaTicks;
        }

        private bool IsCooldownSatisfied()
        {
            if (lastEntryBar == int.MinValue)
                return true;

            int barsElapsed = CurrentBar - lastEntryBar;
            return barsElapsed >= CooldownBars;
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);

            if (execution?.Order == null)
                return;

            if (execution.Order.Name != EntrySignalName)
                return;

            if (execution.Order.OrderState == OrderState.Filled && execution.Order.Filled == execution.Order.Quantity)
            {
                tradesToday++;
                lastEntryBar = CurrentBar;
            }
        }

        #region Parameters
        [NinjaScriptProperty]
        [Range(1, 8)]
        [Display(Name = "Taille de base (micros)", Order = 1, GroupName = "Entrées")]
        public int BaseEntrySize
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range(1, 8)]
        [Display(Name = "Position max (micros)", Order = 2, GroupName = "Entrées")]
        public int MaxPositionMicros
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Tolérance touch (ticks)", Order = 3, GroupName = "Entrées")]
        public double TouchToleranceTicks
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "Cooldown (barres)", Order = 4, GroupName = "Entrées")]
        public int CooldownBars
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Filtre imbalance", Order = 1, GroupName = "Confirmations")]
        public bool UseImbalanceFilter
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Confirmation footprint", Order = 2, GroupName = "Confirmations")]
        public bool UseFootprintConfirmation
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Delta haussier mini (ticks)", Order = 3, GroupName = "Confirmations")]
        public double MinimumBullishDeltaTicks
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Trades max / jour", Order = 1, GroupName = "Risque")]
        public int MaxTradesPerDay
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Perte max journalière", Order = 2, GroupName = "Risque")]
        public double MaxDailyLoss
        {
            get; set;
        }
        #endregion
    }
}
