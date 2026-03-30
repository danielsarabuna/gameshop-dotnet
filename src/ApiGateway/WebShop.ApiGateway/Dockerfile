FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY WebShop.sln ./
COPY src/ApiGateway/WebShop.ApiGateway/WebShop.ApiGateway.csproj src/ApiGateway/WebShop.ApiGateway/
COPY src/BuildingBlocks/Logging/Logging.csproj src/BuildingBlocks/Logging/

RUN dotnet restore src/ApiGateway/WebShop.ApiGateway/WebShop.ApiGateway.csproj

COPY . .
RUN dotnet publish src/ApiGateway/WebShop.ApiGateway/WebShop.ApiGateway.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WebShop.ApiGateway.dll"]

