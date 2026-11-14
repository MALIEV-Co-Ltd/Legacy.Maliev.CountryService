# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
ENV GITHUB_ACTIONS=false
RUN apt-get update && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*
RUN git clone --filter=blob:none --no-checkout https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults.git /dependencies/Legacy.Maliev.ServiceDefaults \
    && git -C /dependencies/Legacy.Maliev.ServiceDefaults checkout 173d63218d9142b34075bca87f5c6d933bcbdfb6 \
    && git clone --filter=blob:none --no-checkout https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts.git /dependencies/Legacy.Maliev.CompatibilityContracts \
    && git -C /dependencies/Legacy.Maliev.CompatibilityContracts checkout fe17b8cea8ace3eef5434f8b29c9038f5c975613
COPY nuget.config .
COPY Legacy.Maliev.CountryService.Api/Legacy.Maliev.CountryService.Api.csproj Legacy.Maliev.CountryService.Api/
COPY Legacy.Maliev.CountryService.Application/Legacy.Maliev.CountryService.Application.csproj Legacy.Maliev.CountryService.Application/
COPY Legacy.Maliev.CountryService.Domain/Legacy.Maliev.CountryService.Domain.csproj Legacy.Maliev.CountryService.Domain/
COPY Legacy.Maliev.CountryService.Data/Legacy.Maliev.CountryService.Data.csproj Legacy.Maliev.CountryService.Data/
RUN dotnet restore Legacy.Maliev.CountryService.Api/Legacy.Maliev.CountryService.Api.csproj \
    -p:MalievWorkspaceRoot=/dependencies
COPY . .
RUN dotnet publish Legacy.Maliev.CountryService.Api/Legacy.Maliev.CountryService.Api.csproj \
    --configuration Release --no-restore --output /app -warnaserror /p:UseAppHost=false \
    -p:MalievWorkspaceRoot=/dependencies

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.CountryService.Api.dll"]
