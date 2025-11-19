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
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// L'indicateur est placé dans l'espace de noms standard de NinjaTrader pour être reconnu automatiquement.
namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Indicateur AVWAP ancré sur une ligne verticale taguée "AVWAP_ANCRE".
    /// L'utilisateur place manuellement la ligne d'ancrage ; l'indicateur calcule ensuite
    /// un VWAP ancré en utilisant les prix typiques et les volumes depuis la bougie d'ancre.
    /// </summary>
    public class ThomasAnchoredVWAP : Indicator
    {
        private const string AnchorTag = "AVWAP_ANCRE";
        private const double TimeToleranceSeconds = 60; // Tolérance de 60 secondes pour retrouver la bougie d'ancre.

        private Series<double> avwapSeries;
        private DateTime anchorTime = Core.Globals.MinDate;
        private bool anchorFound;
        private int anchorMoveVersion;

        /// <summary>
        /// Méthode appelée lors des changements d'état de l'indicateur (initialisation, chargement, etc.).
        /// </summary>
        protected override void OnStateChange()
        {
            switch (State)
            {
                case State.SetDefaults:
                    Name = "ThomasAnchoredVWAP";
                    Description = "Calcule un AVWAP ancré sur une ligne verticale taguée AVWAP_ANCRE.";
                    Calculate = Calculate.OnBarClose;
                    IsOverlay = true;
                    IsSuspendedWhileInactive = false;
                    AddPlot(Brushes.CornflowerBlue, "AVWAP");
                    anchorTime = Core.Globals.MinDate;
                    anchorFound = false;
                    anchorMoveVersion = 0;
                    break;

                case State.DataLoaded:
                    avwapSeries = new Series<double>(this);
                    break;
            }
        }

        /// <summary>
        /// Boucle principale appelée à chaque nouvelle bougie (ou tick selon le paramètre Calculate).
        /// </summary>
        protected override void OnBarUpdate()
        {
            // Tant qu'aucune ligne d'ancrage n'est trouvée, on renvoie une valeur vide (NaN) pour éviter un tracé erroné.
            if (!TryUpdateAnchor())
            {
                Values[0][0] = double.NaN;
                avwapSeries[0] = double.NaN;
                return;
            }

            int anchorBarsAgo = FindAnchorBarsAgo(anchorTime);
            if (anchorBarsAgo < 0)
            {
                // Ligne présente mais impossible de retrouver la bougie (par exemple historique insuffisant).
                Values[0][0] = double.NaN;
                avwapSeries[0] = double.NaN;
                return;
            }

            double cumulativePriceVolume = 0;
            double cumulativeVolume = 0;

            // On cumule TypicalPrice * Volume depuis la bougie d'ancre jusqu'à la bougie actuelle.
            for (int barsAgo = anchorBarsAgo; barsAgo >= 0; barsAgo--)
            {
                double typicalPrice = (High[barsAgo] + Low[barsAgo] + Close[barsAgo]) / 3.0;
                double barVolume = Volume[barsAgo];

                cumulativePriceVolume += typicalPrice * barVolume;
                cumulativeVolume += barVolume;
            }

            double avwapValue = double.NaN;
            if (cumulativeVolume > 0)
                avwapValue = cumulativePriceVolume / cumulativeVolume;

            // Stockage dans la série interne et traçage sur le graphique.
            avwapSeries[0] = avwapValue;
            Values[0][0] = avwapValue;
        }

        /// <summary>
        /// Tente de mettre à jour les informations d'ancrage à partir des objets dessinés sur le graphique.
        /// </summary>
        private bool TryUpdateAnchor()
        {
            // Pas de graphique (par exemple en mode stratégie backtest) : on essaie quand même d'utiliser l'ancre précédente.
            // Si aucune ancre n'a été mémorisée, on ne peut rien calculer.
            if (ChartControl == null && !anchorFound)
                return false;

            bool localAnchorFound = false;
            bool anchorChanged = false;

            foreach (DrawingTool drawingTool in DrawObjects)
            {
                if (drawingTool is VerticalLine verticalLine && verticalLine.Tag == AnchorTag)
                {
                    DateTime foundAnchorTime = verticalLine.StartAnchor.Time;

                    // Si la ligne a été déplacée, on mémorise la nouvelle heure d'ancrage.
                    if (foundAnchorTime != anchorTime)
                    {
                        anchorTime = foundAnchorTime;
                        anchorChanged = true;
                    }

                    localAnchorFound = true;
                    break;
                }
            }

            anchorFound = localAnchorFound;
            if (anchorFound && anchorChanged)
                anchorMoveVersion++;

            return anchorFound;
        }

        /// <summary>
        /// Cherche le nombre de barres (bars ago) entre la bougie actuelle et la bougie d'ancre.
        /// Une petite tolérance temporelle est appliquée pour gérer les cas où la ligne verticale ne tombe pas exactement sur
        /// l'heure de la bougie.
        /// </summary>
        private int FindAnchorBarsAgo(DateTime targetTime)
        {
            if (targetTime == Core.Globals.MinDate)
                return -1;

            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
            {
                DateTime barTime = Time[barsAgo];
                double difference = Math.Abs((barTime - targetTime).TotalSeconds);
                if (difference <= TimeToleranceSeconds)
                    return barsAgo;
            }

            return -1;
        }

        #region Properties
        /// <summary>
        /// Expose la valeur actuelle de l'AVWAP pour d'autres scripts (stratégies, etc.).
        /// </summary>
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> AVWAPSeries
        {
            get { return avwapSeries; }
        }

        /// <summary>
        /// Incrémente à chaque déplacement d'ancre pour permettre un filtrage des entrées.
        /// </summary>
        [Browsable(false)]
        [XmlIgnore]
        public int AnchorMoveVersion
        {
            get { return anchorMoveVersion; }
        }
        #endregion
    }
}
