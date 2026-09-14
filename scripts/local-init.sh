#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_dir/.env.local"

if [[ ! -f "$env_file" ]]; then
  touch "$env_file"
  chmod 600 "$env_file"
  echo "Created an ignored .env.local with safe local defaults."
fi

upsert_env() {
  local key="$1"
  local value="$2"
  local next_file
  next_file="$(mktemp)"
  awk -v key="$key" -v value="$value" '
    BEGIN { found = 0 }
    index($0, key "=") == 1 { print key "=" value; found = 1; next }
    { print }
    END { if (!found) print key "=" value }
  ' "$env_file" > "$next_file"
  mv "$next_file" "$env_file"
}

set_default_env() {
  local key="$1"
  local value="$2"
  if ! awk -F= -v key="$key" '$1 == key { found = 1 } END { exit !found }' "$env_file"; then
    upsert_env "$key" "$value"
  fi
}

set_default_env POSTGRES_USER "webshop"
set_default_env POSTGRES_PASSWORD "webshop-local-only"
set_default_env POSTGRES_DB "ordering"
set_default_env SUPABASE_URL ""
set_default_env SUPABASE_SERVICE_ROLE_KEY ""
set_default_env SUPABASE_DB_URL ""
set_default_env CATALOG_BUCKET "dev"
set_default_env CATALOG_CACHE_INVALIDATION_SECRET ""
set_default_env GAME_TICKET_JWT_PRIVATE_KEY_PEM_BASE64 ""
set_default_env GAME_TICKET_JWT_PUBLIC_KEY_PEM_BASE64 ""
set_default_env PAYMENTS_MOCK_ENABLED "true"

private_key="$(awk -F= '$1 == "GAME_TICKET_JWT_PRIVATE_KEY_PEM_BASE64" { sub(/^[^=]*=/, ""); print; exit }' "$env_file")"
public_key="$(awk -F= '$1 == "GAME_TICKET_JWT_PUBLIC_KEY_PEM_BASE64" { sub(/^[^=]*=/, ""); print; exit }' "$env_file")"

if [[ -z "$private_key" || -z "$public_key" ]]; then
  key_dir="$(mktemp -d)"
  trap 'rm -rf "$key_dir"' EXIT
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$key_dir/private.pem" >/dev/null 2>&1
  openssl rsa -pubout -in "$key_dir/private.pem" -out "$key_dir/public.pem" >/dev/null 2>&1
  private_key="$(base64 < "$key_dir/private.pem" | tr -d '\n')"
  public_key="$(base64 < "$key_dir/public.pem" | tr -d '\n')"
  upsert_env GAME_TICKET_JWT_PRIVATE_KEY_PEM_BASE64 "$private_key"
  upsert_env GAME_TICKET_JWT_PUBLIC_KEY_PEM_BASE64 "$public_key"
  echo "Generated a local-only game-ticket RSA key pair."
fi

cache_invalidation_secret="$(awk -F= '$1 == "CATALOG_CACHE_INVALIDATION_SECRET" { sub(/^[^=]*=/, ""); print; exit }' "$env_file")"
if [[ -z "$cache_invalidation_secret" ]]; then
  cache_invalidation_secret="$(openssl rand -hex 32)"
  upsert_env CATALOG_CACHE_INVALIDATION_SECRET "$cache_invalidation_secret"
  echo "Generated a local-only catalog cache-invalidation secret."
fi

supabase_url="$(awk -F= '$1 == "SUPABASE_URL" { sub(/^[^=]*=/, ""); print; exit }' "$env_file")"
supabase_service_key="$(awk -F= '$1 == "SUPABASE_SERVICE_ROLE_KEY" { sub(/^[^=]*=/, ""); print; exit }' "$env_file")"
if [[ -z "$supabase_url" || -z "$supabase_service_key" ]]; then
  echo "Optional setup: add SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY for the remote catalog and purchase-delivery flow."
  echo "See README.md for the complete environment variable reference."
fi

echo "Local environment is ready: $env_file"
