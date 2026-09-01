SHELL := /bin/bash
.DEFAULT_GOAL := help

IMAGE_NAME ?= taskapi
IMAGE_TAG  ?= local

KUBECTL       ?= kubectl
K8S_NAMESPACE ?= taskapi

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

# Les commentaires sont hors des recettes : une ligne de recette, meme commentaire, est
# passee au shell et donc affichee a l'execution.
#
# Le namespace est applique en premier parce que les autres manifests le referencent, et
# parce que la suppression du Job qui suit echouerait sur un namespace inexistant.
# Le Job est supprime avant l'apply : un Job est quasi immuable, re-appliquer le meme nom
# avec une image differente est refuse par l'API.
# 600 s d'attente et non 180 : au tout premier deploiement, le noeud n'a aucune des deux
# images en cache. Mesure sur ce poste, 475 s pour tout amener dont 4 min 35 pour l'image
# PostgreSQL ; images en cache, le meme deploiement complet prend 19 s.
# rollout status sort en code non nul si le deploiement n'aboutit pas : c'est ce qui fait
# echouer la cible, et ce que reprendra le script de deploiement.
.PHONY: k8s-deploy
k8s-deploy: ## Déploie sur Minikube et attend que le rollout soit terminé
	$(KUBECTL) apply -f k8s/00-namespace.yaml
	$(KUBECTL) -n $(K8S_NAMESPACE) delete job taskapi-migrate --ignore-not-found
	$(KUBECTL) apply -f k8s/
	$(KUBECTL) -n $(K8S_NAMESPACE) wait --for=condition=complete job/taskapi-migrate --timeout=600s
	$(KUBECTL) -n $(K8S_NAMESPACE) rollout status deployment/taskapi --timeout=180s

.PHONY: k8s-logs
k8s-logs: ## Suit les logs des pods de l'API sur Minikube
	$(KUBECTL) -n $(K8S_NAMESPACE) logs -f -l app.kubernetes.io/component=api --all-containers

.PHONY: k8s-forward
k8s-forward: ## Expose l'API du cluster sur http://localhost:8080
	$(KUBECTL) -n $(K8S_NAMESPACE) port-forward service/taskapi 8080:80

# Supprimer le namespace suffit : tous les objets du projet y vivent. C'est la raison
# pratique d'avoir un namespace dedie plutot que de deployer dans default.
.PHONY: k8s-clean
k8s-clean: ## Supprime le namespace et tout ce qu'il contient
	$(KUBECTL) delete namespace $(K8S_NAMESPACE) --ignore-not-found

.PHONY: troubleshoot
troubleshoot: ## Collecte l'état pour diagnostic
	./scripts/troubleshoot.sh

.PHONY: clean
clean: ## Supprime conteneurs, volumes et artefacts de build
	-docker compose down -v
	-rm -rf coverage
	-find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
