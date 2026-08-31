# Task API

[![CI](https://github.com/Medirial/Devops-net-ci/actions/workflows/ci.yml/badge.svg)](https://github.com/Medirial/Devops-net-ci/actions/workflows/ci.yml)

API .NET conteneurisée, avec pipeline CI/CD et déploiement Kubernetes.

## Stack

- .NET 9, ASP.NET Core Minimal API
- Entity Framework Core, PostgreSQL 16
- xUnit, Coverlet
- Docker, Docker Compose
- GitHub Actions, SonarCloud, Trivy, GHCR
- Kubernetes (Minikube)

## Prérequis

- .NET SDK 9
- Docker et Docker Compose
- make

## Démarrage

```bash
make up
```

L'API écoute sur `http://localhost:8080`.

## Commandes

```bash
make help     # liste les cibles
make run      # lance l'API sans conteneur
make test     # tests et couverture
make build    # construit l'image
make down     # arrête la stack
```

## Structure

```
src/         API
tests/       tests unitaires
k8s/         manifests Kubernetes
scripts/     scripts de déploiement et diagnostic
```

## Intégration continue

Six jobs, enchaînés par `needs` et ordonnés par coût croissant.

```
lint ──> test ──┬──> sonar
                └──> image ──> scan ──> publish
```

| Job | Rôle |
|---|---|
| `lint` | ShellCheck sur `scripts/` |
| `test` | build, tests, rapport de couverture |
| `sonar` | analyse SonarCloud, Quality Gate bloquant |
| `image` | build Docker, vérification non-root, taille |
| `scan` | scan Trivy de l'image |
| `publish` | publication sur GHCR |

Le rapport de couverture et le rapport Trivy sont publiés en artefact à chaque exécution,
y compris lorsque l'étape échoue.

### Analyse statique

SonarCloud consomme le rapport OpenCover produit par le job `test`. Le Quality Gate est
bloquant : `sonar.qualitygate.wait=true` fait attendre le verdict et sortir en erreur.

Les paramètres d'analyse sont dans `sonar-project.properties`. Les exclusions de couverture
sont alignées sur `coverlet.runsettings`.

Le job est conditionné à la présence du secret `SONAR_TOKEN`. Tant que le secret n'est pas
renseigné, le job reste vert avec ses étapes ignorées, et il s'active sans modification du
workflow le jour où il l'est.

### Scan de vulnérabilités

Trivy échoue sur les CVE `CRITICAL` et `HIGH`. Les CVE sans correctif publié en amont sont
écartées (`ignore-unfixed`) : elles ne sont pas corrigeables ici et laisseraient le job
rouge en permanence.

Le premier scan a échoué sur `CVE-2026-14456` (HIGH), qui touche `libssl3` et `libcrypto3`.
Le correctif était publié par Alpine mais absent de l'image de base. Le `Dockerfile` met
désormais à jour les paquets système au moment du build.

### Publication de l'image

L'image est poussée sur `ghcr.io/medirial/devops-net-ci`, uniquement depuis `develop` et
`main`. Trois tags : SHA court, nom de branche, et `latest` sur `main` seulement. Le tag
par SHA est immuable et désigne un commit unique, ce qui rend un retour arrière possible.

L'authentification utilise le `GITHUB_TOKEN` du run, sans secret à créer. La permission
`packages: write` est déclarée sur ce seul job ; les autres gardent un jeton en lecture
seule.

### Durées

Mesurées sur `develop`, la seule branche où les six jobs tournent. Le cache de couches
Docker est cloisonné par branche : la première exécution après un merge ne réutilise pas
celui rempli par les exécutions de la pull request.

| Job | 1re exécution | Exécution suivante |
|---|---|---|
| `lint` | 8 s | 6 s |
| `test` | 25 s | 32 s |
| `sonar` | 5 s | 4 s |
| `image` | 45 s | 23 s |
| `scan` | 66 s | 32 s |
| `publish` | 17 s | 32 s |
| **Pipeline complet** | **172 s** | **134 s** |

`sonar` tourne en parallèle de `image` : il n'allonge pas le pipeline. Ses étapes sont
ignorées tant que `SONAR_TOKEN` n'est pas renseigné.

Sur une pull request, `publish` n'est pas exécuté et le pipeline complet est mesuré à
125 s.

### Caches

Effet des caches, mesuré sur deux exécutions consécutives :

| Étape | Cache froid | Cache chaud |
|---|---|---|
| `dotnet restore` | 9 s | 2 s |
| Build de l'image | 56 s | 8 s |

Le cache NuGet est indexé sur un hash des `.csproj` : il n'est reconstruit que lorsqu'une
dépendance change.

Faute de disque partagé entre deux jobs, `scan` et `publish` reconstruisent l'image depuis
le cache de couches rempli par `image`. La reconstruction coûte 14 s dans `scan` et 6 s
dans `publish`, contre 45 s pour le build initial.
