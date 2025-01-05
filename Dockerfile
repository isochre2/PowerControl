# Consultez https://aka.ms/customizecontainer pour savoir comment personnaliser votre conteneur de débogage et comment Visual Studio utilise ce Dockerfile pour générer vos images afin d’accélérer le débogage.

# Cet index est utilisé lors de l’exécution à partir de VS en mode rapide (par défaut pour la configuration de débogage)
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS base
WORKDIR /app

EXPOSE 8081

# Cette phase est utilisée pour générer le projet de service
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-preview AS build
WORKDIR /src
COPY ["./PowerControl.csproj", "."]
RUN dotnet --version
RUN dotnet restore "./PowerControl.csproj"

COPY . .
WORKDIR "/src"
RUN dotnet build "./PowerControl.csproj" -c Release -f net8.0 -o /app/build

# Cette étape permet de publier le projet de service à copier dans la phase finale
FROM build AS publish
RUN dotnet publish "./PowerControl.csproj" -c Release -f net8.0 -o /app/publish

# Cette phase est utilisée en production ou lors de l’exécution à partir de VS en mode normal (par défaut quand la configuration de débogage n’est pas utilisée)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PowerControl.dll"]