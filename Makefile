SHELL := /bin/bash

.PHONY: build build-all run-local run-local-api run-docker test clean-logs check

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
	docker compose up --build

test:
	dotnet test tests/Ordering.Domain.Tests/Ordering.Domain.Tests.csproj -c Debug -p:NuGetAudit=false
	dotnet test tests/WebShop.IntegrationTests/WebShop.IntegrationTests.csproj -c Debug -p:NuGetAudit=false

check:
	dotnet build WebShop.sln -c Debug -m:1
	dotnet test tests/Ordering.Domain.Tests/Ordering.Domain.Tests.csproj -c Debug -p:NuGetAudit=false
	dotnet test tests/WebShop.IntegrationTests/WebShop.IntegrationTests.csproj -c Debug -p:NuGetAudit=false

clean-logs:
	rm -rf .logs
