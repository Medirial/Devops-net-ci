#!/usr/bin/env bash
#
# Attend qu'un endpoint HTTP reponde 2xx, ou sort en erreur au bout du delai.
# Utilise par `make up` et, en phase 6, par le script de deploiement Kubernetes.

set -euo pipefail

readonly DEFAULT_TIMEOUT=60
readonly DEFAULT_INTERVAL=2

usage() {
    cat <<EOF
Usage: ${0##*/} -u URL [-t SECONDES] [-i SECONDES]

  -u URL       endpoint a interroger (obligatoire)
  -t SECONDES  delai maximum, defaut ${DEFAULT_TIMEOUT}
  -i SECONDES  intervalle entre deux essais, defaut ${DEFAULT_INTERVAL}
  -h           affiche cette aide

Sort en 0 si l'endpoint repond 2xx avant le delai, 1 sinon.

Exemple:
  ${0##*/} -u http://localhost:8080/health/ready -t 90
EOF
}

require() {
    command -v "$1" >/dev/null 2>&1 || {
        echo "erreur: '$1' est introuvable dans le PATH" >&2
        exit 1
    }
}

main() {
    local url="" timeout="$DEFAULT_TIMEOUT" interval="$DEFAULT_INTERVAL"

    while getopts ':u:t:i:h' opt; do
        case "$opt" in
            u) url="$OPTARG" ;;
            t) timeout="$OPTARG" ;;
            i) interval="$OPTARG" ;;
            h) usage; exit 0 ;;
            :) echo "erreur: -$OPTARG attend une valeur" >&2; usage >&2; exit 1 ;;
            ?) echo "erreur: option inconnue -$OPTARG" >&2; usage >&2; exit 1 ;;
        esac
    done

    if [[ -z "$url" ]]; then
        echo "erreur: -u est obligatoire" >&2
        usage >&2
        exit 1
    fi

    require curl

    local deadline
    deadline=$(( SECONDS + timeout ))

    echo "attente de ${url} (delai ${timeout}s)"

    while (( SECONDS < deadline )); do
        # --fail met curl en echec sur un code >= 400 ; sans lui, une page d'erreur
        # renvoyee en 503 serait consideree comme une reponse valide.
        if curl --silent --fail --show-error --max-time "$interval" "$url" >/dev/null 2>&1; then
            echo "pret apres $(( SECONDS - (deadline - timeout) ))s"
            return 0
        fi
        sleep "$interval"
    done

    echo "erreur: ${url} n'a pas repondu en ${timeout}s" >&2
    return 1
}

main "$@"
