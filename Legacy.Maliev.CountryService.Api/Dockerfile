# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV GITHUB_ACTIONS=false
COPY nuget.config .
COPY .dependencies/ .dependencies/
COPY Legacy.Maliev.CountryService.Api/Legacy.Maliev.CountryService.Api.csproj Legacy.Maliev.CountryService.Api/
COPY Legacy.Maliev.CountryService.Application/Legacy.Maliev.CountryService.Application.csproj Legacy.Maliev.CountryService.Application/
COPY Legacy.Maliev.CountryService.Domain/Legacy.Maliev.CountryService.Domain.csproj Legacy.Maliev.CountryService.Domain/
COPY Legacy.Maliev.CountryService.Data/Legacy.Maliev.CountryService.Data.csproj Legacy.Maliev.CountryService.Data/
RUN dotnet restore Legacy.Maliev.CountryService.Api/Legacy.Maliev.CountryService.Api.csproj \
    -p:MalievWorkspaceRoot=/src/.dependencies
COPY . .
RUN dotnet publish Legacy.Maliev.CountryService.Api/Legacy.Maliev.CountryService.Api.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false \
    -p:MalievWorkspaceRoot=/src/.dependencies

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.CountryService.Api.dll"]
