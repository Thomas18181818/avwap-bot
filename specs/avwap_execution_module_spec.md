# Module d'exécution AVWAP — Spécification Technique

## Objectif
Assurer l'exécution semi-automatique des entrées LONG sur MNQ lorsque le prix touche l'AVWAP ancré par l'utilisateur et que les filtres de confirmation (imbalances / footprint) sont validés. Le module doit respecter les limites de risque, empêcher les ré-entrées trop rapides et tenir compte des sorties partielles manuelles.

## Entrées et dépendances
- **ThomasAnchoredVWAP** : indicateur chargé de calculer l'AVWAP à partir d'une ligne verticale taguée `AVWAP_ANCRE`.
- **Flux de bougies du graphique** : utilisé pour détecter les touches de l'AVWAP et pour lire les volumes/prix nécessaires aux confirmations.
- **Système d'ordres NinjaTrader** : exécution des ordres `EnterLong` sur MNQ en micro-contrats.

## Paramètres exposés
| Paramètre | Rôle | Valeur par défaut |
| --- | --- | --- |
| `BaseEntrySize` | Taille du bloc d'entrée (micros) | 2 |
| `MaxPositionMicros` | Taille maximale agrégée de position | 8 |
| `TouchToleranceTicks` | Tolérance autour de l'AVWAP pour valider un « touch » | 2 |
| `CooldownBars` | Nombre minimal de bougies entre deux nouvelles entrées | 3 |
| `UseImbalanceFilter` | Active le filtre d'imbalance | `false` |
| `UseFootprintConfirmation` | Active la validation footprint | `false` |
| `MinimumBullishDeltaTicks` | Intensité minimale (en ticks) exigée pour la bougie footprint | 2 |
| `MaxTradesPerDay` | Nombre maximum d'entrées par session | 4 |
| `MaxDailyLoss` | Perte journalière maximale en USD | 400 |

## Logique d'exécution
1. **Initialisation** :
   - Charge l'indicateur `ThomasAnchoredVWAP`.
   - Prépare les compteurs de session (`tradesToday`, `sessionStartCumProfit`).
2. **Reset journalier** :
   - À chaque nouvelle date de bougie, réinitialise les compteurs de trades et de PnL pour la journée US.
3. **Contrôles de risque** :
   - Stoppe toute nouvelle entrée si `tradesToday >= MaxTradesPerDay` ou si `sessionPnL <= -MaxDailyLoss`.
4. **Lecture AVWAP** :
   - Récupère `avwapValue = anchoredVwap.AVWAPSeries[0]`. Si NaN, arrête l'évaluation.
5. **Détection du touch** :
   - Considère que le prix a touché l'AVWAP si `Low[0] - tol <= avwapValue <= High[0] + tol` avec `tol = TouchToleranceTicks * TickSize`.
6. **Filtres de confirmation** :
   - **Imbalance** : valide si `UseImbalanceFilter = false` ou si le rapport `UpRange / DownRange` est supérieur à 1 (bougie orientée acheteuse).
   - **Footprint** : valide si `UseFootprintConfirmation = false` ou si `(Close[0] - Open[0]) >= MinimumBullishDeltaTicks * TickSize`.
   - Les méthodes sont isolées pour permettre de brancher ultérieurement de vrais indicateurs d'orderflow.
7. **Cooldown** :
   - `CurrentBar - lastEntryBar >= CooldownBars` doit être vrai pour autoriser une nouvelle exécution.
8. **Calcul de la taille** :
   - `currentPosition = max(Position.Quantity, 0)` (seules les positions LONG sont autorisées).
   - `availableSize = MaxPositionMicros - currentPosition`.
   - `orderSize = min(BaseEntrySize, availableSize)`.
   - Annule si `orderSize <= 0` (position déjà à pleine taille).
9. **Exécution** :
   - Envoie `EnterLong(orderSize, "ThomasAvwapExec")`.
   - La date/barre de l'ordre est mémorisée et le compteur `tradesToday` est incrémenté à la complétion totale de l'ordre.
10. **Gestion des sorties manuelles** :
    - Aucune action lors des sorties. Le module lit simplement `Position.Quantity` à chaque bougie, ce qui actualise automatiquement `availableSize` pour les entrées suivantes.

## Sécurité et sur-trading
- **Limites** : `MaxTradesPerDay` et `MaxDailyLoss` sont vérifiés à chaque bougie avant toute prise de décision.
- **Cooldown** : trois bougies pleines doivent s'écouler entre deux ordres d'entrée, quel que soit l'état de la position (flat ou composée).
- **Blocage en cas de short** : si une position short existe (exécution externe), le module reste inactif jusqu'à retour à plat pour éviter les conflits.

## Scénarios couverts
1. **Retour unique sur l'AVWAP** : déclenche 1 × `BaseEntrySize` micros si tous les filtres sont validés.
2. **Composition progressive** : chaque fois que le prix retouche l'AVWAP (après cooldown) et que la position est < 8 micros, un nouvel ordre est envoyé jusqu'à saturation de `MaxPositionMicros`.
3. **Sorties partielles manuelles** : la taille redevenue disponible (8 - position courante) peut être reprise lors d'une prochaine validation.
4. **Blocage risques** : si deux pertes consécutives amènent la journée à -400 $, la stratégie reste active pour la surveillance mais n'émet plus d'ordre.

## Intégration NinjaTrader
- Le code C# associé (voir `src/ThomasAvwapExecutionModule.cs`) suit les conventions NinjaScript :
  - `OnStateChange` pour l'initialisation et l'ajout de l'indicateur.
  - `OnBarUpdate` pour la boucle de décision.
  - `OnExecutionUpdate` pour suivre les ordres et alimenter le cooldown.
- Aucun stop ni take profit n'est placé : l'utilisateur les gère manuellement.
- Le module est prêt à être importé via l'éditeur NinjaScript et à être ajouté sur un graphique MNQ avec l'indicateur `ThomasAnchoredVWAP` chargé.
