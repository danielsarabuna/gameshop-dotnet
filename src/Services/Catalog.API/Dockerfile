FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY WebShop.sln ./
COPY src/Services/Catalog.API/Catalog.API.csproj src/Services/Catalog.API/
COPY src/BuildingBlocks/Logging/Logging.csproj src/BuildingBlocks/Logging/
COPY src/Contracts/Catalog.Grpc/Catalog.Grpc.csproj src/Contracts/Catalog.Grpc/

RUN dotnet restore src/Services/Catalog.API/Catalog.API.csproj

COPY . .
RUN dotnet publish src/Services/Catalog.API/Catalog.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --retries=10 --start-period=15s \
    CMD curl -fsS http://localhost:8080/health || exit 1

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Catalog.API.dll"]

