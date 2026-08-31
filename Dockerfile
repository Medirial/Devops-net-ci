# Etage 1 : compilation. Le SDK (~1 Go) n'existe que le temps du build.
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /source

# Le csproj est copie seul, avant le code : tant que les dependances ne bougent pas,
# Docker reutilise le cache du restore. Copier tout d'un coup invaliderait cette
# couche a chaque modification d'un fichier .cs.
COPY src/TaskApi.csproj src/
RUN dotnet restore src/TaskApi.csproj

COPY src/ src/
RUN dotnet publish src/TaskApi.csproj -c Release -o /app/publish --no-restore

# Etage 2 : execution. Seul le resultat du publish est repris ; ni le SDK, ni les
# sources, ni le cache NuGet n'atteignent l'image finale.
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final

# apk upgrade avant l'installation : l'image de base est reconstruite par son editeur a
# son propre rythme, et ses paquets systeme retardent sur les correctifs Alpine. Le scan
# Trivy de la CI a remonte CVE-2026-14456 (HIGH) sur libssl3 et libcrypto3 en 3.5.7-r0
# alors que 3.5.8-r0 etait publie. Mettre a jour au build ferme cette fenetre sans
# attendre l'editeur.
# Contrepartie assumee : le build n'est plus reproductible a l'octet pres, deux builds du
# meme Dockerfile a deux dates donnent des versions de paquets differentes. C'est le prix
# a payer pour ne pas livrer une CVE connue et corrigee en amont.
#
# curl n'est pas dans l'image aspnet. Installe uniquement pour le HEALTHCHECK : sans
# client HTTP dans le conteneur, Docker ne peut pas interroger /health/ready.
# --no-cache evite d'ecrire l'index des paquets dans la couche.
RUN apk upgrade --no-cache && apk add --no-cache curl

WORKDIR /app
COPY --from=build /app/publish .

# L'image aspnet fournit deja un utilisateur non-root nomme "app".
USER app

# 8080 et non 80 : depuis .NET 8 le port par defaut est 8080, car lier un port < 1024
# demande une capability que ce conteneur n'a pas.
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

# start-period couvre le demarrage .NET et l'application des migrations : pendant ce
# delai un echec ne compte pas dans les retries.
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=5 \
    CMD curl -fsS http://localhost:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "TaskApi.dll"]
