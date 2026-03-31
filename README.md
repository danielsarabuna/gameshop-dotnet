# Техническая документация MVP: Игровой Магазин (Distributed Game Store)

## 1. Обзор проекта
Целью MVP является создание масштабируемой платформы для продажи цифрового контента (игр, дополнений). Система строится на базе **микросервисной архитектуры** с физическим разделением фронтенда и бэкенда. Основной акцент сделан на обеспечении целостности транзакций, безопасности платежей и возможности независимого масштабирования компонентов.

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

### 7.1 Сборка решения
```bash
dotnet build WebShop.sln -c Release -m:1
```

### 7.2 Запуск сервисов локально
```bash
bash ./run-local.sh
```

Или через `make`:
```bash
make run-local
```

Если хотите также собрать/запустить Blazor-клиент `Shopping.Web`:
```bash
INCLUDE_WEB=1 bash ./run-local.sh
```

Ручной запуск (4 терминала):
```bash
dotnet run --project src/Services/Catalog.API/Catalog.API.csproj
dotnet run --project src/Services/Basket.API/Basket.API.csproj
dotnet run --project src/Services/Ordering/Ordering.API/Ordering.API.csproj
dotnet run --project src/ApiGateway/WebShop.ApiGateway/WebShop.ApiGateway.csproj
```

Порты (HTTP):
- API Gateway: `http://localhost:5100`
- Catalog: `http://localhost:5101`
- Basket: `http://localhost:5102`
- Ordering: `http://localhost:5103`

### 7.3 Тесты
```bash
dotnet test tests/Ordering.Domain.Tests/Ordering.Domain.Tests.csproj -c Release -p:NuGetAudit=false
```

### 7.4 Docker Compose
```bash
docker compose up --build
```
