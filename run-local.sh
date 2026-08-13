#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

CONFIGURATION="${CONFIGURATION:-Debug}"
LOG_DIR="${LOG_DIR:-"$ROOT_DIR/.logs"}"

mkdir -p "$LOG_DIR"

PIDS=()

cleanup() {
  echo
  echo "Stopping services..."
  for pid in "${PIDS[@]:-}"; do
    kill "$pid" 2>/dev/null || true
  done
}

trap cleanup EXIT INT TERM

echo "Building solution ($CONFIGURATION)..."
# Note: building the full solution may trigger Xcode toolchain/license prompts on macOS
# (e.g., due to the Blazor WebAssembly project). For local API development we only
# build the backend services + gateway by default.
dotnet build src/Services/Catalog.API/Catalog.API.csproj -c "$CONFIGURATION" -m:1 >/dev/null
dotnet build src/Services/Basket.API/Basket.API.csproj -c "$CONFIGURATION" -m:1 >/dev/null
dotnet build src/Services/Ordering/Ordering.API/Ordering.API.csproj -c "$CONFIGURATION" -m:1 >/dev/null
dotnet build src/ApiGateway/WebShop.ApiGateway/WebShop.ApiGateway.csproj -c "$CONFIGURATION" -m:1 >/dev/null

if [[ "${INCLUDE_WEB:-0}" == "1" ]]; then
  echo "Building React frontend..."
  (cd src/WebApps/Shopping.Web && npm install >/dev/null 2>&1 && npm run build >/dev/null 2>&1) || true
fi

start_service() {
  local name="$1"
  local port="$2"
  local project="$3"
  local log_file="$LOG_DIR/${name}.log"

  echo "Starting ${name} on http://localhost:${port} (log: ${log_file})"

  ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}" \
  ASPNETCORE_URLS="http://localhost:${port}" \
    dotnet run --no-launch-profile --project "$project" -c "$CONFIGURATION" --no-build >"$log_file" 2>&1 &

  PIDS+=("$!")
}

start_service "catalog" "5101" "src/Services/Catalog.API/Catalog.API.csproj"
start_service "basket" "5102" "src/Services/Basket.API/Basket.API.csproj"
start_service "ordering" "5103" "src/Services/Ordering/Ordering.API/Ordering.API.csproj"
start_service "gateway" "5100" "src/ApiGateway/WebShop.ApiGateway/WebShop.ApiGateway.csproj"

if [[ "${INCLUDE_WEB:-0}" == "1" ]]; then
  echo "Starting web on http://localhost:5200 (log: ${LOG_DIR}/web.log)"
  (cd src/WebApps/Shopping.Web && npm run dev -- --port 5200) >"$LOG_DIR/web.log" 2>&1 &
  PIDS+=("$!")
fi

echo
echo "Ready:"
echo "  Gateway:  http://localhost:5100/health"
echo "  Catalog:  http://localhost:5101/health"
echo "  Basket:   http://localhost:5102/health"
echo "  Ordering: http://localhost:5103/health"
if [[ "${INCLUDE_WEB:-0}" == "1" ]]; then
  echo "  Web:      http://localhost:5200"
fi
echo
echo "Through gateway:"
echo "  http://localhost:5100/api/v1/catalog/items"
echo "  http://localhost:5100/api/v1/basket/test-user"
echo
echo "Press Ctrl+C to stop."

wait
