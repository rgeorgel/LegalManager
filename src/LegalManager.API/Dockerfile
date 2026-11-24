# syntax=docker/dockerfile:1
FROM ghcr.io/rgeorgel/legalmanager-base:latest AS base

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["src/LegalManager.API/LegalManager.API.csproj", "src/LegalManager.API/"]
COPY ["src/LegalManager.Application/LegalManager.Application.csproj", "src/LegalManager.Application/"]
COPY ["src/LegalManager.Domain/LegalManager.Domain.csproj", "src/LegalManager.Domain/"]
COPY ["src/LegalManager.Infrastructure/LegalManager.Infrastructure.csproj", "src/LegalManager.Infrastructure/"]
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore "src/LegalManager.API/LegalManager.API.csproj"
COPY . .

# Só o publish: ele já compila. Um "dotnet build" antes compilava tudo duas vezes a cada deploy.
# Sem --no-restore: a camada do restore pode vir do cache do Docker sem os pacotes no cache do NuGet
# (são caches diferentes); o restore aqui é rápido quando os pacotes já estão lá.
FROM build AS publish
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet publish "src/LegalManager.API/LegalManager.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base
# Fuso do processo explícito: com Npgsql.EnableLegacyTimestampBehavior, datas timestamptz são
# lidas no fuso do servidor, e o frontend depende disso para exibir prazos/eventos (gravados na
# hora "de parede" de Brasília — ver BrasiliaTime.AgoraParede). Não altere sem migrar esses dados.
ENV TZ=UTC
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LegalManager.API.dll"]