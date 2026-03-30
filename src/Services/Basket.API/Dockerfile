FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY WebShop.sln ./
COPY src/Services/Basket.API/Basket.API.csproj src/Services/Basket.API/
COPY src/BuildingBlocks/Logging/Logging.csproj src/BuildingBlocks/Logging/

RUN dotnet restore src/Services/Basket.API/Basket.API.csproj

COPY . .
RUN dotnet publish src/Services/Basket.API/Basket.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Basket.API.dll"]

