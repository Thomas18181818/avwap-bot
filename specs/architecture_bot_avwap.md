# Architecture du bot AVWAP

## Contexte
- **Nom du projet** : Robot AVWAP.
- **Plateforme cible** : NinjaTrader 8.
- **Marché de référence** : Micro E-mini NASDAQ-100 (MNQ).
- **Principe général** : le trader choisit manuellement l’ancre AVWAP en plaçant une ligne verticale avec le tag `AVWAP_ANCRE`. Le bot automatise ensuite la gestion des entrées, du suivi de position et du risque.

## Ancrage AVWAP
- L’utilisateur place sur le graphique une `VerticalLine` dont le tag exact est `AVWAP_ANCRE`.
- L’ancrage effectif s’applique toujours sur le **plus bas (Low)** de la bougie correspondant à la `VerticalLine`, même si la ligne n’est pas visuellement posée sur ce low.
- L’horodatage (`StartTime`) de la ligne verticale sert à retrouver l’index de la bougie d’ancrage.

## Calcul AVWAP
- Le bot calcule un AVWAP interne en appliquant la formule VWAP ancré :
  - `AVWAP = somme(TypicalPrice * Volume) / somme(Volume)`
  - `TypicalPrice = (High + Low + Close) / 3`
- Les cumuls sont réalisés depuis la bougie d’ancre jusqu’à la bougie courante.
- Le niveau AVWAP est stocké dans une série et tracé sur le graphique pour visualiser le prix moyen pondéré par le volume depuis l’ancrage.

## Règles d’entrée
- Le bot ne prend que des positions **LONG**.
- Les entrées sont déclenchées lorsque le prix revient au voisinage de l’AVWAP interne (tolérance paramétrable en ticks).
- L’humain conserve le contrôle de l’ancrage et peut repositionner la ligne `AVWAP_ANCRE` si nécessaire.

## Gestion de position
- Taille standard en **micro-contrats**, avec possibilité de composer la position (ex : 2 + 2 + 2 micros).
- Le bot gère automatiquement l’ouverture de position, le placement du stop loss et du take profit, ainsi que les sorties partielles si la position est composée.
- Le suivi intègre la surveillance du retour sur l’AVWAP, le respect des stops/take profit et la préparation d’extensions futures (filtres orderflow, imbalances, etc.).

## Gestion du risque
- Paramètres de gestion du risque configurables : nombre maximum de trades par session et perte journalière maximale.
- Des valeurs par défaut conservatrices sont recommandées pour limiter le risque.
- Si la limite de perte ou le nombre de trades maximum est atteint, le bot cesse d’ouvrir de nouvelles positions tout en restant actif pour la surveillance.
