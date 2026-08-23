# Task API

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
