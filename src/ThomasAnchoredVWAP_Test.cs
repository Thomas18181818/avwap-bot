#region Using declarations
using System;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class ThomasAnchoredVWAP_Test : Indicator
    {
        // Tag de la ligne verticale MANUELLE
        private const string AnchorTag = "AVWAP_ANCRE";

        private VerticalLine anchorLine    = null;
        private int          anchorBarIndex = -1;
        private int          lastAnchorBarIndex = -1;

        private int          anchorMoveVersion = 0;

        [Browsable(false)]
        [XmlIgnore]
        public int AnchorMoveVersion
        {
            get { return anchorMoveVersion; }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "ThomasAnchoredVWAP_Test";
                Calculate                = Calculate.OnEachTick;    // pour que ça réagisse quand tu bouges la ligne
                IsOverlay                = true;
                DisplayInDataBox         = true;
                DrawOnPricePanel         = true;
                IsSuspendedWhileInactive = true;

                AddPlot(Brushes.LimeGreen, "AVWAP");
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 0)
                return;

            // 1) Chercher la ligne verticale avec le tag AVWAP_ANCRE
            anchorLine = null;
            foreach (var obj in DrawObjects)
            {
                VerticalLine v = obj as VerticalLine;
                if (v != null && v.Tag == AnchorTag)
                {
                    anchorLine = v;
                    break;
                }
            }

            // Pas de ligne → aucune AVWAP
            if (anchorLine == null || anchorLine.StartAnchor == null)
            {
                Values[0][0] = double.NaN;
                anchorBarIndex = -1;
                return;
            }

            // 2) Déterminer l’index de barre de l’ancre à partir du temps
            DateTime anchorTime = anchorLine.StartAnchor.Time;
            int newAnchorIndex  = Bars.GetBar(anchorTime);

            // Time en dehors de l’historique visible
            if (newAnchorIndex < 0 || newAnchorIndex > CurrentBar)
            {
                Values[0][0] = double.NaN;
                anchorBarIndex = -1;
                return;
            }

            // Si l’ancre a changé de barre → on log + incrémente la version
            if (newAnchorIndex != anchorBarIndex)
            {
                anchorBarIndex = newAnchorIndex;

                if (anchorBarIndex != lastAnchorBarIndex)
                {
                    lastAnchorBarIndex = anchorBarIndex;
                    anchorMoveVersion++;

                    Print(string.Format(
                        "{0} AnchoredVWAP → ancre déplacée | anchorBarIndex={1} | AnchorMoveVersion={2}",
                        Time[0],
                        anchorBarIndex,
                        anchorMoveVersion));
                }
            }

            if (anchorBarIndex < 0 || anchorBarIndex > CurrentBar)
            {
                Values[0][0] = double.NaN;
                return;
            }

            // 3) On efface toute la courbe existante
            for (int barsAgo = 0; barsAgo <= CurrentBar; barsAgo++)
                Values[0][barsAgo] = double.NaN;

            // 4) Recalcul complet de l’AVWAP (SUR LES LOWS) depuis la barre d’ancrage
            double sumPV  = 0.0;
            double sumVol = 0.0;

            for (int barIndex = anchorBarIndex; barIndex <= CurrentBar; barIndex++)
            {
                int barsAgo = CurrentBar - barIndex;

                double price = Low[barsAgo];   // on prend toujours le LOW
                double vol   = Volume[barsAgo];

                sumPV  += price * vol;
                sumVol += vol;

                if (sumVol > 0)
                    Values[0][barsAgo] = sumPV / sumVol;
                else
                    Values[0][barsAgo] = double.NaN;
            }

            // Value[0] (= Values[0][0]) est déjà positionné ci-dessus
        }

        public override string DisplayName => Name;
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private ThomasAnchoredVWAP_Test[] cacheThomasAnchoredVWAP_Test;

        public ThomasAnchoredVWAP_Test ThomasAnchoredVWAP_Test()
        {
            return ThomasAnchoredVWAP_Test(Input);
        }

        public ThomasAnchoredVWAP_Test ThomasAnchoredVWAP_Test(ISeries<double> input)
        {
            if (cacheThomasAnchoredVWAP_Test != null)
                for (int idx = 0; idx < cacheThomasAnchoredVWAP_Test.Length; idx++)
                    if (cacheThomasAnchoredVWAP_Test[idx] != null && cacheThomasAnchoredVWAP_Test[idx].EqualsInput(input))
                        return cacheThomasAnchoredVWAP_Test[idx];

            return CacheIndicator<ThomasAnchoredVWAP_Test>(new ThomasAnchoredVWAP_Test(), input, ref cacheThomasAnchoredVWAP_Test);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.ThomasAnchoredVWAP_Test ThomasAnchoredVWAP_Test()
        {
            return indicator.ThomasAnchoredVWAP_Test(Input);
        }

        public Indicators.ThomasAnchoredVWAP_Test ThomasAnchoredVWAP_Test(ISeries<double> input)
        {
            return indicator.ThomasAnchoredVWAP_Test(input);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.ThomasAnchoredVWAP_Test ThomasAnchoredVWAP_Test()
        {
            return indicator.ThomasAnchoredVWAP_Test(Input);
        }

        public Indicators.ThomasAnchoredVWAP_Test ThomasAnchoredVWAP_Test(ISeries<double> input)
        {
            return indicator.ThomasAnchoredVWAP_Test(input);
        }
    }
}

#endregion
