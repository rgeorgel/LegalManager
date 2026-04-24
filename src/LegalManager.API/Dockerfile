FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
WORKDIR /app
EXPOSE 8080
RUN apt-get update && apt-get install -y unzip curl python3 python3-pip && \
    pip3 install botocore boto3 --break-system-packages && \
    curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o "awscliv2.zip" && \
    unzip -q awscliv2.zip && ./aws/install && rm -rf awscliv2.zip aws && \
    apt-get clean && rm -rf /var/lib/apt/lists/*

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

FROM base
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LegalManager.API.dll"]