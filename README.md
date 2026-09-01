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

## Déploiement Kubernetes

Testé sur Minikube 1.38, Kubernetes 1.35, avec le pilote Docker.

```bash
make k8s-deploy
make k8s-forward     # l'API répond alors sur http://localhost:8080
make k8s-clean
```

`make k8s-deploy` applique les manifests, attend la fin du Job de migration, puis attend le
rollout. Il sort en code non nul si le déploiement n'aboutit pas.

### Manifests

Les fichiers sont numérotés parce que `kubectl apply -f k8s/` traite un répertoire dans
l'ordre alphabétique des noms de fichiers. Sans préfixe, le Deployment serait appliqué
avant le namespace qu'il référence.

| Fichier | Contenu |
|---|---|
| `00-namespace.yaml` | namespace `taskapi` |
| `10-configmap.yaml` | configuration non sensible |
| `20-secret.yaml` | mot de passe de la base, valeur de développement |
| `30-postgres.yaml` | Deployment et Service PostgreSQL 16 |
| `40-migration-job.yaml` | Job appliquant les migrations |
| `50-app-deployment.yaml` | Deployment de l'API, 2 replicas |
| `60-app-service.yaml` | Service ClusterIP de l'API |

L'image déployée est celle publiée par la CI sur GHCR, désignée par son tag de commit.
Elle est tirée depuis le registre plutôt que chargée localement : c'est la même image que
celle que Trivy a scannée, et le package étant public, aucun `imagePullSecret` n'est
nécessaire.

### Migrations et replicas

L'API appliquait ses migrations au démarrage. Avec deux replicas, les deux pods les
appliqueraient en même temps sur la même base au premier déploiement.

Les migrations sont donc appliquées par un Job dédié, qui lance la même image avec
`Database__MigrationMode=only` : il migre puis sort. Les replicas démarrent avec
`Database__MigrationMode=none`.

Le Job et le Deployment sont appliqués ensemble, sans ordre imposé. C'est la sonde
readiness de l'API qui rend cet ordre indifférent : elle vérifie que la base répond **et**
que le schéma attendu par le binaire y est appliqué. Un replica démarré avant la fin du Job
reste hors des endpoints du Service jusqu'à ce que la migration soit passée.

Un initContainer a été écarté : il serait exécuté une fois par pod, donc deux fois en
concurrence au premier déploiement, et une fois de plus à chaque rolling update.

### Sondes

| Sonde | Chemin | Ce qu'elle vérifie | Conséquence d'un échec |
|---|---|---|---|
| `livenessProbe` | `/health/live` | le process répond | le conteneur est tué et redémarré |
| `readinessProbe` | `/health/ready` | la base répond et le schéma est à jour | le pod sort des endpoints du Service |

La liveness ne teste jamais la base. Si elle le faisait, une coupure de PostgreSQL ferait
redémarrer tous les pods en boucle, alors que redémarrer ne répare pas une base.

Vérifié sur le cluster. PostgreSQL arrêté, les deux pods passent en `READY 0/1` en 14 s et
disparaissent des endpoints du Service, avec `RESTARTS` à 0. Ils y reviennent une fois la
base et le schéma revenus.

`initialDelaySeconds` est calé sur le démarrage réellement observé : un pod passe de créé à
`READY` en 11 à 13 s, dont 5 s d'`initialDelaySeconds` de la readiness.

### Mise à jour sans coupure

La stratégie est `RollingUpdate` avec `maxUnavailable: 0` et `maxSurge: 1` : un nouveau pod
est créé et doit passer sa readiness avant qu'un ancien soit arrêté. Le nombre de pods
prêts ne descend donc jamais en dessous de deux.

Mesuré pendant un changement de tag d'image, avec un client interrogeant `GET /tasks` à
travers le Service toutes les 100 ms : **600 appels, 600 réponses 200**, aucune erreur.
Le rollout a duré 76 s, le `kubectl rollout undo` 23 s.

### Ressources

| | requests | limits | consommation mesurée |
|---|---|---|---|
| API | 100m / 128Mi | 1 / 256Mi | 42 Mio au repos |
| PostgreSQL | 100m / 128Mi | 1 / 512Mi | 73 Mio au repos |

Les `requests` sont ce que l'ordonnanceur réserve, donc ce sur quoi il décide si un pod
tient sur un nœud. Les `limits` sont un plafond : dépasser la limite mémoire fait tuer le
conteneur en `OOMKilled`, un incident borné à ce pod. Sans limite, un pod peut saturer le
nœud, et l'OOM killer du noyau tue alors un processus quelconque, pas forcément le fautif.

### Sécurité du conteneur

`runAsNonRoot: true`, `runAsUser: 1654` et `allowPrivilegeEscalation: false`.

`runAsUser` n'est pas redondant avec `runAsNonRoot`. Le Dockerfile déclare `USER app`, un
nom que kubelet ne sait pas résoudre en identifiant avant de démarrer le conteneur : sans
uid numérique, le pod est refusé.

### Limites assumées

Les données de PostgreSQL vivent dans un `emptyDir` : elles disparaissent quand le pod est
supprimé ou replanifié. C'est un choix, pas un oubli. Un PersistentVolumeClaim sur Minikube
ajoute une StorageClass et un provisionneur à comprendre, sans rapport avec ce que ce
déploiement cherche à montrer.

Ce qu'il faudrait pour un vrai déploiement : un StatefulSet plutôt qu'un Deployment, un
volume persistant, et surtout une base gérée par l'infrastructure, parce que sauvegardes,
restauration testée et montées de version ne s'improvisent pas dans un pod.

Pas d'Ingress non plus : l'accès passe par `kubectl port-forward`. Un Ingress demanderait
un contrôleur à installer et à maintenir pour un seul service.

### Durées mesurées

| Opération | Durée |
|---|---|
| Premier déploiement, aucune image en cache | 475 s |
| dont pull de `postgres:16-alpine` (294 Mo) | 4 min 35 |
| dont pull de l'image de l'API depuis GHCR (134 Mo) | 2 min 55 |
| Déploiement complet, images en cache | 28 s |
| Job de migration, image en cache | 17 s |
| Rolling update des deux replicas | 76 s |
| `kubectl rollout undo` | 23 s |
| Pod supprimé à la main, recréé et `READY` | 11 s |

Le premier déploiement est dominé par le téléchargement des images. Les pulls suivants de
la même image coûtent une dizaine de secondes, le temps de vérifier le manifeste.
