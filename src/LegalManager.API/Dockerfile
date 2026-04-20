FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY ["src/LegalManager.API/LegalManager.API.csproj", "src/LegalManager.API/"]
COPY ["src/LegalManager.Application/LegalManager.Application.csproj", "src/LegalManager.Application/"]
COPY ["src/LegalManager.Domain/LegalManager.Domain.csproj", "src/LegalManager.Domain/"]
COPY ["src/LegalManager.Infrastructure/LegalManager.Infrastructure.csproj", "src/LegalManager.Infrastructure/"]
RUN dotnet restore "src/LegalManager.API/LegalManager.API.csproj"
COPY . .
RUN dotnet build "src/LegalManager.API/LegalManager.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "src/LegalManager.API/LegalManager.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LegalManager.API.dll"]
