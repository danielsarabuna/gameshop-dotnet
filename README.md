# WebShop / GameShop — MVP (Distributed Game Store)

## 1. Обзор проекта
Целью MVP является создание масштабируемой платформы для продажи цифрового контента (игр, дополнений). Система строится на базе **микросервисной архитектуры** с физическим разделением фронтенда и бэкенда. Основной акцент сделан на обеспечении целостности транзакций, безопасности платежей и возможности независимого масштабирования компонентов.

Дополнительно в репозитории есть демо‑фронтенд **GameShop** (Blazor WebAssembly) — тёмная витрина для покупки внутриигровой валюты (алмазы) и Premium‑подписки для dating‑sim.

## 2. Объем MVP (MVP Scope)
Для первой версии продукта реализуются следующие критические модули:
*   **Catalog Service:** Просмотр списка игр и метаданных.
*   **Basket Service:** Управление временной корзиной покупок.
*   **Ordering Service:** Оформление заказов и базовая логика оплаты.
*   **Identity Service:** Регистрация и вход пользователей через **OIDC**.

## 3. Архитектурный стек
Система использует современные паттерны и инструменты экосистемы .NET 9:
*   **Framework:** ASP.NET Core Minimal APIs для высокой производительности.
*   **Architecture:** **Clean Architecture** (внутри сервисов) и **CQRS** (через MediatR).
*   **Communication:** 
    *   **External:** REST (JSON) для взаимодействия фронтенда с API через шлюз **YARP**.
    *   **Internal:** **gRPC** для сверхбыстрой синхронной связи между сервисами.
    *   **Asynchronous:** **RabbitMQ + MassTransit** для обмена событиями (например, подтверждение оплаты).
*   **Persistence:** 
    *   **PostgreSQL:** Основное хранилище заказов (транзакционная целостность).
    *   **MongoDB:** Гибкий каталог товаров.
    *   **Redis:** Быстрое хранение корзин пользователей.

## 4. Иерархия проекта (Solution Structure)
Проект организован в рамках единого решения для удобства разработки, но с четким разделением для независимого развертывания:

```text
Solution/
├── src/
│   ├── WebApps/                # Фронтенд (развертывается на Web-сервере)
│   │   └── Shopping.Web/       # Blazor-клиент
│   ├── Services/               # Бэкенд-сервисы (App-серверы)
│   │   ├── Catalog.API/        # Работа с каталогом (MongoDB)
│   │   ├── Basket.API/         # Корзина (Redis)
│   │   └── Ordering/           # Заказы (PostgreSQL + Clean Architecture)
│   │       ├── Ordering.API/           (Presentation)
│   │       ├── Ordering.Application/   (CQRS/MediatR)
│   │       ├── Ordering.Domain/        (Entities/Rules)
│   │       └── Ordering.Infrastructure/ (DB Access)
│   └── BuildingBlocks/         # Общие библиотеки
│       ├── EventBus/           # Логика RabbitMQ/MassTransit
│       └── Logging/            # Структурированное логирование (Serilog)
├── tests/                      # Модульные и интеграционные тесты
└── docker-compose.yml          # Оркестрация всей системы
```

## 5. Ключевые рабочие процессы (Workflows)

### 5.1 Процесс оформления заказа
1.  **Frontend** отправляет запрос через **API Gateway** в сервис `Ordering`.
2.  **Ordering Service** проверяет наличие товаров через gRPC-вызов к `Catalog`.
3.  **Обработка оплаты:** Для MVP используется стратегия **токенизации** (через Stripe). Бэкенд никогда не видит и не хранит полные данные карт, получая только безопасный "токен".
4.  **Асинхронное выполнение:** После успешной оплаты сервис публикует событие `OrderCreated` в шину сообщений, чтобы уведомить другие системы без блокировки пользователя.

### 5.2 Аудит и отслеживание (Event Sourcing)
Для финансовых операций в сервисе заказов используется библиотека **Marten**. Вместо перезаписи состояния сохраняется вся последовательность событий (`OrderPlaced`, `PaymentValidated`), что обеспечивает идеальный аудит всех покупок игрока.

## 6. Безопасность и мониторинг
*   **Идентификация:** Централизованное управление пользователями через **Keycloak** (OIDC). Микросервисы валидируют **JWT-токены** локально, что исключает лишние сетевые запросы при проверке прав.
*   **Observability:** Полная прозрачность системы через стек **OpenTelemetry**: логи (Serilog), метрики (Prometheus) и трассировка (Jaeger) для визуализации пути запроса между серверами.
*   **Секреты:** Все строки подключения и ключи API хранятся во внешних хранилищах (Azure Key Vault или HashiCorp Vault), а не в коде проекта.

## 7. Быстрый старт (Dev)

### 7.1 Требования
- .NET 9 SDK
- (Опционально) Docker (для запуска всей системы через `docker compose`)
- macOS: если при сборке/запуске появляется сообщение про Xcode/Apple SDKs license — примите лицензию:
  ```bash
  sudo xcodebuild -license
  ```

### 7.2 Запуск только фронтенда (GameShop UI)
```bash
dotnet run --project src/WebApps/Shopping.Web/Shopping.Web.csproj
```
Открыть: `http://localhost:5200`

Что есть в UI (демо):
- Dark theme + адаптив (desktop/mobile)
- Локализация: RU/EN/DE/FR/ES (переключатель в хедере)
- Cart drawer справа (qty, промокод — demo, total)
- Canvas background effect: **Floating Hearts** (desktop реагирует на курсор; mobile — без push‑эффекта)

### 7.3 Сборка решения
```bash
dotnet build WebShop.sln -c Release -m:1
```

### 7.4 Запуск локально (API + Gateway + Shopping.Web)
```bash
make run-local
```

Логи пишутся в `.logs/` (в том числе `web.log` для Shopping.Web). Остановка — `Ctrl+C`.

Если запускаете скрипт напрямую и получаете `Permission denied`, сделайте его исполняемым:
```bash
chmod +x ./run-local.sh
```

Скрипт без `make` с вебом: `INCLUDE_WEB=1 ./run-local.sh`.

Для **только** API + Gateway (без сборки/запуска веб-приложения):
```bash
make run-local-api
```
(эквивалентно `bash ./run-local.sh`.)

Порты (HTTP):
- Web (Shopping.Web): `http://localhost:5200` (только `make run-local`, не `run-local-api`)
- API Gateway: `http://localhost:5100`
- Catalog: `http://localhost:5101`
- Basket: `http://localhost:5102`
- Ordering: `http://localhost:5103`

Health checks:
- `http://localhost:5100/health`
- `http://localhost:5101/health`
- `http://localhost:5102/health`
- `http://localhost:5103/health`

Через gateway:
- `http://localhost:5100/api/v1/catalog/items`
- `http://localhost:5100/api/v1/basket/test-user`
- `http://localhost:5100/api/v1/orders/create`
- `http://localhost:5100/api/v1/promocodes/apply`
- `http://localhost:5100/api/v1/payments/stripe` (также `paypal`, `yookassa`)
- `http://localhost:5100/api/v1/webhooks/stripe` (также `paypal`, `yookassa`)

Catalog seed (in-memory):
- 9 пакетов алмазов (`type=Currency`)
- 3 Premium-подписки (`type=Subscription`)

Promo codes (in-memory):
- `LOVE10` — 10% на всё
- `PREM20` — 20% только на Premium
- `SAVE5` — фикс €5 только на алмазы

Webhook security (dev):
- Если задан `Webhooks:Secret` или `Webhooks:{provider}:Secret`, нужно передать заголовок `X-Webhook-Secret`.

Supabase (начисление/лог покупок):
- Установите `SUPABASE_URL` и `SUPABASE_SERVICE_ROLE_KEY` (или `Supabase:Url`, `Supabase:ServiceRoleKey` в конфиге).

### 7.5 Тесты
```bash
dotnet test tests/Ordering.Domain.Tests/Ordering.Domain.Tests.csproj -c Release -p:NuGetAudit=false
```

### 7.6 Docker Compose (вся система)
```bash
docker compose up --build
```
