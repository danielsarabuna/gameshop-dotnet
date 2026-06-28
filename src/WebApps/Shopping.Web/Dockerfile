FROM node:20-alpine AS build
WORKDIR /app

COPY src/WebApps/Shopping.Web/package*.json ./
RUN npm ci

COPY src/WebApps/Shopping.Web/ ./
RUN npm run build

FROM nginx:1.27-alpine AS final
RUN apk add --no-cache curl
COPY --from=build /app/dist /usr/share/nginx/html
COPY src/WebApps/Shopping.Web/nginx/default.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=3s --retries=10 --start-period=10s \
    CMD curl -fsS http://localhost:8080/health || exit 1
