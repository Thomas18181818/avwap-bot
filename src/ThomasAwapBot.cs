#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ThomasAwapBot : Strategy
    {
        private const string EntrySignalName = "AVWAPLong";

        // === Indicateur AVWAP ===
        private ThomasAnchoredVWAP_Test anchoredVwap;

        // === Suivi du risque / session (côté STRATÉGIE) ===
        private int      tradesToday;
        private DateTime currentTradingDay = Core.Globals.MinDate;
        private double   sessionStartCumProfit;

        // === Suivi de position via le COMPTE (entrées bot + sorties manuelles) ===
        private int  currentTradeQty;   // >0 = en position, 0 = flat au niveau compte
        private bool flatEventPending;  // flag déclenché par l’event compte, appliqué dans OnBarUpdate

        // === Gestion de l’ordre d’entrée (UNMANAGED) ===
        private Order entryOrder;
        private bool  entryOrderWorking;

        // === Cooldown après FLAT compte ===
        private int lastExitBar = -1;

        // === Paramètres internes ===
        private int    positionSizeMicros;
        private int    entryToleranceTicks;
        private int    maxTradesPerDay;
        private double maxDailyLoss;
        private int    minBarsAfterExit;

        // === Suivi des déplacements AVWAP ===
        private int lastAnchorMoveVersion = -1;
        private int anchorMoveBar         = -1;
        private int barsAfterAnchorMove;      // exposé en propriété

        protected override void OnStateChange()
        {
            switch (State)
            {
                case State.SetDefaults:
                    Name      = "ThomasAwapBot";
                    Calculate = Calculate.OnEachTick;

                    // IMPORTANT : mode UNMANAGED
                    IsUnmanaged = true;

                    EntriesPerDirection          = 1;
                    EntryHandling                = EntryHandling.AllEntries;
                    IsExitOnSessionCloseStrategy = true;
                    ExitOnSessionCloseSeconds    = 30;
                    BarsRequiredToTrade          = 20;

                    positionSizeMicros  = 6;
                    entryToleranceTicks = 8;
                    maxTradesPerDay     = 1000;
                    maxDailyLoss        = 1000;
                    minBarsAfterExit    = 3;

                    barsAfterAnchorMove = 2;   // par défaut : 2 bougies (≈10s sur UT 5s)

                    break;

                case State.Configure:
                    TraceOrders = true;
                    break;

                case State.DataLoaded:
                    anchoredVwap = ThomasAnchoredVWAP_Test();
                    AddChartIndicator(anchoredVwap);

                    entryOrder        = null;
                    entryOrderWorking = false;

                    currentTradeQty   = 0;
                    flatEventPending  = false;

                    lastAnchorMoveVersion = -1;
                    anchorMoveBar         = -1;

                    // Abonnement aux exécutions du COMPTE (bot + tes ordres manuels)
                    if (Account != null)
                        Account.ExecutionUpdate += OnAccountExecutionUpdate;

                    Print("INIT OK : AVWAP chargé + stratégie UNMANAGED (entrées auto, TP/SL 100 % manuels, tempo après déplacement AVWAP).");
                    break;

                case State.Terminated:
                    // Nettoyage de l’abonnement compte
                    if (Account != null)
                        Account.ExecutionUpdate -= OnAccountExecutionUpdate;
                    break;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // ===== Application différée de l’évènement FLAT compte =====
            if (flatEventPending)
            {
                lastExitBar      = CurrentBar;
                flatEventPending = false;
                Print($"{Time[0]} → FLAT détecté via compte | lastExitBar={lastExitBar} | currentTradeQty={currentTradeQty}");
            }

            // ===== Changement de journée =====
            DateTime barDate = Times[0][0].Date;
            if (currentTradingDay != barDate)
            {
                currentTradingDay     = barDate;
                tradesToday           = 0;
                sessionStartCumProfit = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                lastExitBar           = -1;

                currentTradeQty       = 0;
                flatEventPending      = false;

                entryOrder            = null;
                entryOrderWorking     = false;

                lastAnchorMoveVersion = -1;
                anchorMoveBar         = -1;

                Print($"NOUVELLE JOURNÉE {currentTradingDay:yyyy-MM-dd} → reset compteurs.");
            }

            // ===== Max Daily Loss (perf stratégie uniquement) =====
            double sessionPnL = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - sessionStartCumProfit;
            if (sessionPnL <= -maxDailyLoss)
            {
                if (State == State.Realtime)
                    Print($"STOP TRADING (stratégie) : MaxDailyLoss atteint ({sessionPnL})");
                return;
            }

            // ===== Max Trades / jour (entrées AVWAP) =====
            if (tradesToday >= maxTradesPerDay)
            {
                if (State == State.Realtime)
                    Print("STOP TRADING : MaxTradesPerDay atteint.");
                return;
            }

            // ===== AVWAP =====
            if (anchoredVwap == null || double.IsNaN(anchoredVwap[0]) || TickSize <= 0)
                return;

            double avwapValue = anchoredVwap[0];
            double tolerance  = entryToleranceTicks * TickSize;

            bool barTouchesAvwap =
                Low[0]  <= avwapValue + tolerance &&
                High[0] >= avwapValue - tolerance;

            // ===== Suivi déplacement AVWAP (via AnchorMoveVersion de l’indicateur) =====
            if (anchoredVwap.AnchorMoveVersion != lastAnchorMoveVersion)
            {
                lastAnchorMoveVersion = anchoredVwap.AnchorMoveVersion;
                anchorMoveBar         = CurrentBar;

                Print($"{Time[0]} → AVWAP déplacé | AnchorMoveVersion={lastAnchorMoveVersion} | anchorMoveBar={anchorMoveBar}");
            }

            // AVWAP considéré comme “stabilisé” après N bougies
            bool avwapStable = (anchorMoveBar < 0) ||
                               (CurrentBar - anchorMoveBar >= barsAfterAnchorMove);

            // ===== Cooldown basé sur FLAT COMPTE =====
            bool cooldownOK = (lastExitBar < 0) || (CurrentBar - lastExitBar >= minBarsAfterExit);

            // Flat du point de vue du COMPTE (bot + toi)
            bool isFlatAccount = (currentTradeQty == 0);

            // Pas d’ordre d’entrée en cours (UNMANAGED)
            bool canSubmitEntry =
                (entryOrder == null) ||
                (entryOrder.OrderState == OrderState.Filled ||
                 entryOrder.OrderState == OrderState.Cancelled ||
                 entryOrder.OrderState == OrderState.Rejected);

            // ===== Filtre directionnel : le prix doit venir D'AU-DESSUS de l'AVWAP =====
            bool touchFromAbove = PriceIsTouchingAvwapFromAbove(avwapValue, entryToleranceTicks);

            // ===== Entrée LONG AVWAP (UNMANAGED, entrée seulement) =====
            if (barTouchesAvwap
                && touchFromAbove              // nouveau filtre : uniquement si le prix vient du haut
                && isFlatAccount
                && canSubmitEntry
                && cooldownOK
                && avwapStable
                && State == State.Realtime)    // on ne s’en sert qu’en temps réel
            {
                double limitPrice = 0;
                double stopPrice  = 0;

                entryOrder = SubmitOrderUnmanaged(
                    0,                              // BarsInProgressIndex
                    OrderAction.Buy,                // Achat
                    OrderType.Market,               // Market
                    positionSizeMicros,             // quantité
                    limitPrice,
                    stopPrice,
                    null,                           // pas d’OCO
                    EntrySignalName);

                entryOrderWorking = true;

                Print($"{Time[0]} → SUBMIT BUY MARKET (UNMANAGED) | qty={positionSizeMicros} | AVWAP={avwapValue} | currentTradeQty={currentTradeQty}");
            }
        }

        // === Vérifie que le touch AVWAP se fait en venant d'AU-DESSUS ===
        private bool PriceIsTouchingAvwapFromAbove(double avwapValue, int tolTicks)
        {
            double tol = tolTicks * TickSize;

            // 1. Bougie précédente clairement AU-DESSUS de l'AVWAP
            bool wasAbove = Close[1] > avwapValue + tol;

            // 2. La mèche actuelle vient tester AVWAP (zone de tolérance)
            bool touchesNow = Low[0] <= avwapValue + tol;

            // 3. La bougie actuelle ne clôture pas sous l'AVWAP (rebond, pas cassure)
            bool closesAbove = Close[0] >= avwapValue;

            return wasAbove && touchesNow && closesAbove;
        }

        // === Suivi des ordres UNMANAGED de la STRATÉGIE ===
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
                                              int quantity, int filled, double averageFillPrice,
                                              OrderState orderState, DateTime time,
                                              ErrorCode error, string nativeError)
        {
            if (order == null)
                return;

            // On ne suit que l’ordre d’entrée
            if (entryOrder != null && order.OrderId == entryOrder.OrderId)
            {
                entryOrder = order; // garder l’état à jour

                Print($"{time} → OnOrderUpdate ENTRY | State={orderState} | Filled={filled} | Err={error} | Native='{nativeError}'");

                if (orderState == OrderState.Filled)
                {
                    tradesToday++;
                    entryOrderWorking = false;
                }
                else if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                {
                    entryOrderWorking = false;
                }
            }
        }

        // === Suivi des exécutions au niveau COMPTE (bot + tes ordres manuels) ===
        private void OnAccountExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                if (e == null)
                    return;

                // Filtrer sur le compte de la stratégie (sender est normalement l’Account)
                Account evtAccount = sender as Account;
                if (Account != null && evtAccount != null && evtAccount.Name != Account.Name)
                    return;

                Execution exec = e.Execution;
                if (exec == null || exec.Instrument == null)
                    return;

                // Même instrument que la stratégie
                if (exec.Instrument.FullName != Instrument.FullName)
                    return;

                if (exec.Order == null
                    || (exec.Order.OrderState != OrderState.Filled
                        && exec.Order.OrderState != OrderState.PartFilled))
                    return;

                int qty = Math.Abs(e.Quantity);

                // +qty sur Buy, -qty sur Sell
                int direction =
                    (exec.Order.OrderAction == OrderAction.Buy || exec.Order.OrderAction == OrderAction.BuyToCover)
                    ? 1
                    : -1;

                currentTradeQty += direction * qty;
                if (currentTradeQty < 0)
                    currentTradeQty = 0;

                Print($"{exec.Time} → ACCOUNT EXEC | {exec.Order.OrderAction} | qty={qty} | currentTradeQty={currentTradeQty}");

                // Si on vient de revenir FLAT (compte) → on posera lastExitBar au prochain OnBarUpdate
                if (currentTradeQty == 0)
                    flatEventPending = true;
            }
            catch (Exception ex)
            {
                Print("OnAccountExecutionUpdate ERROR: " + ex.ToString());
            }
        }

        #region === PROPRIÉTÉS ===

        [NinjaScriptProperty]
        [Display(Name = "Taille position (micros)", Order = 1, GroupName = "Gestion")]
        public int PositionSizeMicros
        {
            get { return positionSizeMicros; }
            set { positionSizeMicros = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Tolérance entrée (ticks)", Order = 2, GroupName = "Conditions")]
        public int EntryToleranceTicks
        {
            get { return entryToleranceTicks; }
            set { entryToleranceTicks = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Trades max/jour (strat)", Order = 3, GroupName = "Risque")]
        public int MaxTradesPerDay
        {
            get { return maxTradesPerDay; }
            set { maxTradesPerDay = Math.Max(1, value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Perte max journalière (strat)", Order = 4, GroupName = "Risque")]
        public double MaxDailyLoss
        {
            get { return maxDailyLoss; }
            set { maxDailyLoss = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Bars après sortie (cooldown)", Order = 5, GroupName = "Risque")]
        public int MinBarsAfterExit
        {
            get { return minBarsAfterExit; }
            set { minBarsAfterExit = Math.Max(0, value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Bars après déplacement AVWAP", Order = 6, GroupName = "Conditions")]
        public int BarsAfterAnchorMove
        {
            get { return barsAfterAnchorMove; }
            set { barsAfterAnchorMove = Math.Max(0, value); }
        }

        #endregion
    }
}
