# Keycloak — локальная конфигурация

При `docker compose up` контейнер `keycloak` стартует с флагом `--import-realm` и подхватывает все JSON из `infra/keycloak/import/`. Сейчас там один файл — `webshop-realm.json`.

## Что разворачивается

Realm: **`webshop`**

| Объект | Значение |
|---|---|
| Realm name | `webshop` |
| Issuer URL | `http://keycloak:8080/realms/webshop` (внутри compose) / `http://localhost:8080/realms/webshop` (с хоста) |
| JWKS URL | `<issuer>/protocol/openid-connect/certs` |
| Access token lifespan | 15 мин |
| Brute-force protection | включена |
| Локали | en / ru / de / fr / es |

### Клиенты

| clientId | Тип | Назначение |
|---|---|---|
| `shopping-web` | public + PKCE (S256) | Blazor WASM витрина (`http://localhost:5200`). Standard flow only. |
| `webshop-api` | bearer-only | "Аудитория" для микросервисов и API Gateway, под валидацию JWT. |

### Роли realm

- `user` — обычный покупатель.
- `admin` — администратор.

### Тестовые пользователи

| Username | Password | Роли |
|---|---|---|
| `tester` | `tester` | `user` |
| `admin-user` | `admin` | `user`, `admin` |

> Это **только для dev**. Никогда не разворачивайте этих пользователей в prod.

## Как проверить, что realm поднялся

```bash
docker compose up -d keycloak
curl -s http://localhost:8080/realms/webshop/.well-known/openid-configuration | jq .issuer
# → "http://localhost:8080/realms/webshop"
```

Админка Keycloak: `http://localhost:8080`, логин `admin / admin` (см. env в `docker-compose.yml`).

## Как пере-экспортировать realm

После любых ручных правок в админке:

```bash
docker compose exec keycloak \
  /opt/keycloak/bin/kc.sh export \
  --dir /opt/keycloak/data/import \
  --realm webshop \
  --users realm_file
```

Затем скопировать наружу и зафиксировать в репозитории.

## Связь с остальной системой

- API Gateway (задача 5.2) валидирует JWT с `Authority = http://keycloak:8080/realms/webshop` и `Audience = webshop-api`.
- Shopping.Web (задача 5.3) логинится через `shopping-web` (PKCE) и шлёт `Bearer`-токен на gateway.
