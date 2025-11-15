# Architecture du bot AVWAP

Ce document décrit les principaux axes architecturaux prévus pour le bot AVWAP.

## Objectifs

- Exploiter les données de marché pour calculer l'Anchored Volume Weighted Average Price (AVWAP).
- Fournir des signaux exploitables dans NinjaTrader.
- Permettre une configuration et une extension simples.

## Composants pressentis

1. **Collecte de données** : interfaces vers les flux de marché pour récupérer les données de prix et de volume.
2. **Calculs AVWAP** : moteur de calcul dédié pour générer des niveaux AVWAP et des zones d'intérêt.
3. **Stratégies NinjaTrader** : scripts C# dans `src/` exploitant les signaux pour l'exécution et la visualisation.
4. **Interface utilisateur** : éléments de configuration et tableaux de bord pour ajuster les paramètres et consulter les analyses.

## Points d'attention

- Performance et latence dans le traitement des données temps réel.
- Gestion des paramètres d'ancrage (date/heure, événements spécifiques, etc.).
- Sécurité et journalisation des décisions de trading.
