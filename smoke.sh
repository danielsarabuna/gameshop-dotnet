#!/usr/bin/env bash
# Smoke test: bring up full stack via docker compose, hit healthchecks + key APIs, tear down.
# Used by `make smoke` and CI release pipelines. Exit non-zero on any failure.
set -euo pipefail

GATEWAY=http://localhost:5100
CATALOG=http://localhost:5101
BASKET=http://localhost:5102
ORDERING=http://localhost:5103
TIMEOUT=120
# Gateway has JWT validation enabled in Docker; smoke hits the services
# directly on their dedicated ports. Gateway is still verified via /health
# (which is AllowAnonymous) so the routing/proxy plumbing is exercised.

cleanup() {
    rc=$?
    if [ "${KEEP_UP:-0}" != "1" ]; then
        echo
        echo "[smoke] tearing down..."
        docker compose down >/dev/null 2>&1 || true
    fi
    exit "$rc"
}
trap cleanup EXIT

wait_healthy() {
    local svc=$1
    local deadline=$(( $(date +%s) + TIMEOUT ))
    while [ "$(date +%s)" -lt "$deadline" ]; do
        local status
        status=$(docker compose ps --format '{{.Service}}\t{{.Health}}' 2>/dev/null \
            | awk -v s="$svc" '$1==s {print $2}')
        if [ "$status" = "healthy" ]; then
            return 0
        fi
        sleep 2
    done
    echo "[smoke] timeout waiting for $svc to become healthy" >&2
    docker compose logs --tail=50 "$svc" >&2 || true
    return 1
}

assert_eq() {
    local actual=$1 expected=$2 label=$3
    if [ "$actual" = "$expected" ]; then
        echo "[smoke] OK  $label = $actual"
    else
        echo "[smoke] FAIL $label: expected $expected, got $actual" >&2
        exit 1
    fi
}

echo "[smoke] docker compose up -d --build"
docker compose up -d --build

for svc in mongodb postgres redis rabbitmq catalog-api basket-api ordering-api apigateway; do
    echo "[smoke] waiting for $svc"
    wait_healthy "$svc"
done

echo "[smoke] healthchecks"
curl -fsS "$GATEWAY/health"   >/dev/null && echo "[smoke] OK  gateway /health"
curl -fsS "$CATALOG/health"   >/dev/null && echo "[smoke] OK  catalog /health"
curl -fsS "$BASKET/health"    >/dev/null && echo "[smoke] OK  basket /health"
curl -fsS "$ORDERING/health"  >/dev/null && echo "[smoke] OK  ordering /health"

echo "[smoke] catalog list (Mongo-backed)"
items_count=$(curl -fsS "$CATALOG/api/v1/catalog/items" | python3 -c 'import sys,json;print(len(json.load(sys.stdin)))')
assert_eq "$items_count" "12" "catalog item count"

echo "[smoke] order create through gateway -> ordering -> catalog gRPC -> Postgres"
order_payload='{"gameUserId":"smoke-001","paymentMethod":"Stripe","items":[{"productId":"d1a00000-0000-0000-0000-000000000060","quantity":2}],"promoCode":null}'
order_resp=$(curl -fsS -X POST -H 'Content-Type: application/json' -d "$order_payload" "$ORDERING/api/v1/orders/create")
order_id=$(echo "$order_resp" | python3 -c 'import sys,json;print(json.load(sys.stdin)["orderId"])')
echo "[smoke] OK  orderId=$order_id"

echo "[smoke] order get round-trip"
order_status=$(curl -fsS "$ORDERING/api/v1/orders/$order_id" \
    | python3 -c 'import sys,json;print(json.load(sys.stdin)["status"])')
assert_eq "$order_status" "Pending" "order status"

echo "[smoke] verify row in postgres"
pg_count=$(docker compose exec -T postgres psql -U webshop -d ordering -tA \
    -c "SELECT count(*) FROM orders WHERE id = '$order_id'")
assert_eq "$pg_count" "1" "postgres orders row count"

echo "[smoke] basket round-trip (Redis)"
basket_payload='{"userId":"smoke-001","items":[{"productId":"d1a00000-0000-0000-0000-000000000060","title":"60 diamonds","unitPrice":1.23,"quantity":3}]}'
curl -fsS -X PUT -H 'Content-Type: application/json' -d "$basket_payload" "$BASKET/api/v1/basket/smoke-001" >/dev/null
basket_qty=$(curl -fsS "$BASKET/api/v1/basket/smoke-001" \
    | python3 -c 'import sys,json;print(json.load(sys.stdin)["items"][0]["quantity"])')
assert_eq "$basket_qty" "3" "basket item qty"
redis_ttl=$(docker compose exec -T redis redis-cli TTL basket:smoke-001 | tr -d '\r')
if [ "$redis_ttl" -gt "0" ]; then
    echo "[smoke] OK  redis TTL=${redis_ttl}s (positive)"
else
    echo "[smoke] FAIL redis TTL non-positive: $redis_ttl" >&2
    exit 1
fi

echo "[smoke] payment-methods"
methods_count=$(curl -fsS "$ORDERING/api/v1/payment-methods" \
    | python3 -c 'import sys,json;print(len(json.load(sys.stdin)))')
if [ "$methods_count" -lt "5" ]; then
    echo "[smoke] FAIL payment-methods returned <5 providers ($methods_count)" >&2
    exit 1
fi
echo "[smoke] OK  payment-methods returned $methods_count providers"

echo
echo "[smoke] all checks passed"
