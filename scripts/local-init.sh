#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="$repo_dir/.env.local"
example_file="$repo_dir/.env.local.example"

if [[ ! -f "$env_file" ]]; then
  if [[ -f "$repo_dir/.env" ]]; then
    cp "$repo_dir/.env" "$env_file"
    echo "Created .env.local from the existing ignored .env."
  else
    cp "$example_file" "$env_file"
    echo "Created .env.local from .env.local.example."
  fi
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

if rg -q 'replace-with-development-service-role-key|your-project\.supabase\.co' "$env_file"; then
  echo "Action required: set SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY in .env.local."
  exit 1
fi

echo "Local environment is ready: $env_file"
