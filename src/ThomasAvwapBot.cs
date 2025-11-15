#region Using declarations
using System;
using System.ComponentModel;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// Stratégie semi-automatisée exploitant l'indicateur ThomasAnchoredVWAP.
    /// L'humain choisit l'ancre AVWAP, la stratégie gère ensuite le déclenchement
    /// des entrées LONG, les stops, les profits et les garde-fous de risque.
    /// </summary>
    public class ThomasAvwapBot : Strategy
    {
        private const string EntrySignalName = "AVWAPLong";

        private ThomasAnchoredVWAP anchoredVwap;
        private int tradesToday;
        private double sessionStartCumProfit;
        private DateTime currentTradingDay = Core.Globals.MinDate;

        #region Strategy lifecycle
        protected override void OnStateChange()
        {
            switch (State)
            {
                case State.SetDefaults:
                    Name = "ThomasAvwapBot";
                    Description = "Stratégie LONG basée sur un AVWAP ancré par une ligne verticale taguée AVWAP_ANCRE.";
                    Calculate = Calculate.OnBarClose;
                    EntriesPerDirection = 1;
                    EntryHandling = EntryHandling.AllEntries;
                    IsExitOnSessionCloseStrategy = true;
                    ExitOnSessionCloseSeconds = 30;
                    IsInstantiatedOnEachOptimizationIteration = false;

                    PositionSizeMicros = 2;
                    EntryToleranceTicks = 2;
                    StopDistanceTicks = 20;
                    TakeProfitTicks = 40;
                    MaxTradesPerDay = 3;
                    MaxDailyLoss = 200;
                    break;

                case State.Configure:
                    // Configuration additionnelle si nécessaire (filtres, séries auxiliaires, etc.).
                    break;

                case State.DataLoaded:
                    anchoredVwap = ThomasAnchoredVWAP();
                    // Affiche l'indicateur directement sur le graphique de la stratégie pour faciliter le suivi.
                    AddChartIndicator(anchoredVwap);
                    break;
            }
        }
        #endregion

        #region Core logic
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 0)
                return;

            // Réinitialisation quotidienne basée sur la date de la bougie actuelle.
            DateTime barDate = Times[0][0].Date;
            if (currentTradingDay != barDate)
            {
                currentTradingDay = barDate;
                tradesToday = 0;
                sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            }

            double sessionRealizedPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;

            // Contrôles de risque : si le nombre de trades ou la perte maximale est atteint, on ne prend plus de nouvelles positions.
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

            // Mise à jour des stops / profits à chaque itération pour refléter les paramètres.
            SetStopLoss(CalculationMode.Ticks, StopDistanceTicks);
            SetProfitTarget(CalculationMode.Ticks, TakeProfitTicks);

            // Condition d'entrée : le prix courant revient au voisinage de l'AVWAP.
            double priceDifferenceTicks = Math.Abs(Close[0] - avwapValue) / TickSize;
            if (priceDifferenceTicks <= EntryToleranceTicks)
            {
                if (Position.MarketPosition == MarketPosition.Flat)
                {
                    EnterLong(PositionSizeMicros, EntrySignalName);

                    // TODO : ajouter des filtres avancés (imbalances, orderflow, contexte volatilité, etc.).
                }
            }
        }
        #endregion

        #region Trade tracking
        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);

            if (execution == null || execution.Order == null)
                return;

            if (execution.Order.Name == EntrySignalName && execution.Order.OrderState == OrderState.Filled)
            {
                // On n'incrémente le compteur de trades qu'une seule fois par ordre d'entrée complet.
                if (execution.Order.Filled == execution.Order.Quantity)
                    tradesToday++;
            }
        }
        #endregion

        #region Parameters
        [NinjaScriptProperty]
        [Display(Name = "Position (micros)", Order = 1, GroupName = "Paramètres d'entrée")]
        public int PositionSizeMicros
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Tolérance entrée (ticks)", Order = 2, GroupName = "Paramètres d'entrée")]
        public int EntryToleranceTicks
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Stop (ticks)", Order = 3, GroupName = "Gestion de position")]
        public int StopDistanceTicks
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Take profit (ticks)", Order = 4, GroupName = "Gestion de position")]
        public int TakeProfitTicks
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Trades max / jour", Order = 5, GroupName = "Gestion du risque")]
        public int MaxTradesPerDay
        {
            get; set;
        }

        [NinjaScriptProperty]
        [Display(Name = "Perte max journalière", Order = 6, GroupName = "Gestion du risque")]
        public double MaxDailyLoss
        {
            get; set;
        }
        #endregion
    }
}
