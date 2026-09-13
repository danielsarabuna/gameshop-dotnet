SHELL := /bin/bash

.PHONY: build build-all run-local run-local-api run-docker test clean-logs check \
	local-init local-up local-up-full local-doctor local-logs local-down \
	supabase-plan supabase-apply supabase-verify supabase-test

LOCAL_COMPOSE := docker compose --env-file .env.local -f docker-compose.yml

build:
	dotnet build src/ApiGateway/WebShop.ApiGateway/WebShop.ApiGateway.csproj -c Debug -m:1
	dotnet build src/Services/Catalog.API/Catalog.API.csproj -c Debug -m:1
	dotnet build src/Services/Basket.API/Basket.API.csproj -c Debug -m:1
	dotnet build src/Services/Ordering/Ordering.API/Ordering.API.csproj -c Debug -m:1

build-all:
	dotnet build WebShop.sln -c Debug -m:1

run-local:
	INCLUDE_WEB=1 bash ./run-local.sh

run-local-api:
	bash ./run-local.sh

run-docker:
	$(MAKE) local-up

local-init:
	bash ./scripts/local-init.sh

local-up: local-init
	COMPOSE_PARALLEL_LIMIT=2 $(LOCAL_COMPOSE) up --build -d
	$(MAKE) local-doctor

local-up-full: local-init
	COMPOSE_PARALLEL_LIMIT=2 $(LOCAL_COMPOSE) --profile full up --build -d
	$(MAKE) local-doctor

supabase-plan:
	bash ./scripts/supabase-schema.sh plan "$${SUPABASE_ENV_FILE:-.env.local}"

supabase-apply:
	bash ./scripts/supabase-schema.sh apply "$${SUPABASE_ENV_FILE:-.env.local}"

supabase-verify:
	bash ./scripts/supabase-schema.sh verify "$${SUPABASE_ENV_FILE:-.env.local}"

supabase-test:
	bash ./scripts/test-supabase-schema.sh

local-doctor:
	bash ./scripts/local-doctor.sh

local-logs:
	$(LOCAL_COMPOSE) logs -f --tail=150 web apigateway catalog-api ordering-api postgres

local-down:
	$(LOCAL_COMPOSE) --profile full down

test:
	dotnet test tests/Ordering.Domain.Tests/Ordering.Domain.Tests.csproj -c Debug -p:NuGetAudit=false
	dotnet test tests/WebShop.IntegrationTests/WebShop.IntegrationTests.csproj -c Debug -p:NuGetAudit=false

check:
	dotnet build WebShop.sln -c Debug -m:1
	dotnet test tests/Ordering.Domain.Tests/Ordering.Domain.Tests.csproj -c Debug -p:NuGetAudit=false
	dotnet test tests/WebShop.IntegrationTests/WebShop.IntegrationTests.csproj -c Debug -p:NuGetAudit=false

smoke:
	./smoke.sh

clean-logs:
	rm -rf .logs
