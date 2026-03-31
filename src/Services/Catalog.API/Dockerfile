FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY WebShop.sln ./
COPY src/Services/Catalog.API/Catalog.API.csproj src/Services/Catalog.API/
COPY src/BuildingBlocks/Logging/Logging.csproj src/BuildingBlocks/Logging/

RUN dotnet restore src/Services/Catalog.API/Catalog.API.csproj

COPY . .
RUN dotnet publish src/Services/Catalog.API/Catalog.API.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Catalog.API.dll"]

