FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY WebShop.sln ./
COPY src/WebApps/Shopping.Web/Shopping.Web.csproj src/WebApps/Shopping.Web/

RUN dotnet restore src/WebApps/Shopping.Web/Shopping.Web.csproj

COPY . .
RUN dotnet publish src/WebApps/Shopping.Web/Shopping.Web.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# Strip the framework's precompressed copies — nginx serves uncompressed
# assets and applies on-the-fly gzip. Avoids the "Content-Encoding missing"
# trap when a static file server forwards .br/.gz files raw.
RUN find /app/publish/wwwroot -name "*.br" -delete \
    && find /app/publish/wwwroot -name "*.gz" -delete

FROM nginx:1.27-alpine AS final
RUN apk add --no-cache curl
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY src/WebApps/Shopping.Web/nginx/default.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --retries=10 --start-period=10s \
    CMD curl -fsS http://localhost:8080/health || exit 1
