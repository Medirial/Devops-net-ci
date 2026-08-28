SHELL := /bin/bash
.DEFAULT_GOAL := help

IMAGE_NAME ?= taskapi
IMAGE_TAG  ?= local

.PHONY: help
help: ## Affiche cette aide
	@grep -E '^[a-zA-Z0-9_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-16s\033[0m %s\n", $$1, $$2}'

.PHONY: run
run: ## Lance l'API en local (sans conteneur)
	dotnet run --project src

.PHONY: test
test: ## Lance les tests avec rapport de couverture
	dotnet test --collect:"XPlat Code Coverage" \
		--settings coverlet.runsettings \
		--results-directory ./coverage

.PHONY: lint
lint: ## Vérifie les scripts avec shellcheck
	@if [ -d scripts ] && [ -n "$$(ls -A scripts 2>/dev/null)" ]; then \
		shellcheck scripts/*.sh; \
	else \
		echo "aucun script à vérifier"; \
	fi

# --- Cibles ajoutées en phase 3 (Docker) ---

.PHONY: build
build: ## Construit l'image Docker
	docker build -t $(IMAGE_NAME):$(IMAGE_TAG) .

.PHONY: up
up: ## Démarre la stack locale (API + PostgreSQL)
	docker compose up -d --build
	@./scripts/wait-for-healthy.sh -u http://localhost:8080/health/ready -t 120

.PHONY: down
down: ## Arrête la stack locale
	docker compose down

.PHONY: logs
logs: ## Suit les logs de l'API
	docker compose logs -f api

.PHONY: size
size: ## Compare la taille multi-stage / SDK unique
	docker build -q -t $(IMAGE_NAME):$(IMAGE_TAG) . >/dev/null
	docker build -q -f Dockerfile.single -t $(IMAGE_NAME):single . >/dev/null
	@docker images --format '  {{.Repository}}:{{.Tag}}	{{.Size}}' | grep '^  $(IMAGE_NAME):'

# --- Cibles ajoutées en phases 5 à 7 ---

.PHONY: scan
scan: ## Scanne l'image avec Trivy
	trivy image --severity HIGH,CRITICAL $(IMAGE_NAME):$(IMAGE_TAG)

.PHONY: k8s-deploy
k8s-deploy: ## Déploie sur Minikube
	./scripts/deploy.sh

.PHONY: troubleshoot
troubleshoot: ## Collecte l'état pour diagnostic
	./scripts/troubleshoot.sh

.PHONY: clean
clean: ## Supprime conteneurs, volumes et artefacts de build
	-docker compose down -v
	-rm -rf coverage
	-find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
