FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY WebShop.sln ./
COPY src/Services/Ordering/Ordering.API/Ordering.API.csproj src/Services/Ordering/Ordering.API/
COPY src/Services/Ordering/Ordering.Application/Ordering.Application.csproj src/Services/Ordering/Ordering.Application/
COPY src/Services/Ordering/Ordering.Domain/Ordering.Domain.csproj src/Services/Ordering/Ordering.Domain/
COPY src/Services/Ordering/Ordering.Infrastructure/Ordering.Infrastructure.csproj src/Services/Ordering/Ordering.Infrastructure/
COPY src/BuildingBlocks/Logging/Logging.csproj src/BuildingBlocks/Logging/
COPY src/BuildingBlocks/EventBus/EventBus.csproj src/BuildingBlocks/EventBus/
COPY src/BuildingBlocks/EventBus.RabbitMq/EventBus.RabbitMq.csproj src/BuildingBlocks/EventBus.RabbitMq/
COPY src/Contracts/IntegrationEvents/IntegrationEvents.csproj src/Contracts/IntegrationEvents/

RUN dotnet restore src/Services/Ordering/Ordering.API/Ordering.API.csproj

COPY . .
RUN dotnet publish src/Services/Ordering/Ordering.API/Ordering.API.csproj -c Release -o /app/publish /p:UseAppHost=false

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
ENTRYPOINT ["dotnet", "Ordering.API.dll"]

