#!/usr/bin/env bash
set -euo pipefail
# Entorno efímero de GitHub Actions. No consume ni reemplaza la .env local.
if [[ "${CI:-}" != "true" ]]; then echo 'Este script requiere un runner CI.' >&2; exit 1; fi
export POSTGRES_USER=combustible POSTGRES_DB=combustible
export POSTGRES_PASSWORD="$(openssl rand -base64 32)"
export APP_DB_PASSWORD="$(openssl rand -base64 32)"
export JWT_SIGNING_KEY="$(openssl rand -base64 32)"
export BOOTSTRAP_PASSWORD="$(openssl rand -base64 32)"
export BOOTSTRAP_EMAIL=admin@localhost.test
export DATA_ENCRYPTION_KEY="$(openssl rand -base64 32)"
export QR_SIGNING_KEY_B64="$(openssl ecparam -name prime256v1 -genkey | openssl pkcs8 -topk8 -nocrypt | base64 -w0)"
export OAUTH_SIGNING_KEY_B64="$(openssl ecparam -name prime256v1 -genkey | openssl pkcs8 -topk8 -nocrypt | base64 -w0)"
export OUTBOX_DIR="$PWD/artifacts/outbox"
for key in POSTGRES_PASSWORD APP_DB_PASSWORD JWT_SIGNING_KEY BOOTSTRAP_PASSWORD DATA_ENCRYPTION_KEY QR_SIGNING_KEY_B64 OAUTH_SIGNING_KEY_B64; do
  echo "::add-mask::${!key}"
done
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Database="Host=127.0.0.1;Port=15432;Database=combustible;Username=combustible;Password=$POSTGRES_PASSWORD"
api_pid=''; web_pid=''; created=false
cleanup() {
  if [[ -n "$api_pid" ]]; then kill "$api_pid" 2>/dev/null || true; fi
  if [[ -n "$web_pid" ]]; then kill "$web_pid" 2>/dev/null || true; fi
  if [[ "$created" == true ]]; then docker stop combustible-e2e >/dev/null; fi
}
trap cleanup EXIT
docker run --rm -d --name combustible-e2e -e POSTGRES_USER -e POSTGRES_DB -e POSTGRES_PASSWORD \
  -p 127.0.0.1:15432:5432 --health-cmd='pg_isready -U combustible -d combustible' \
  --health-interval=1s --health-retries=30 postgres:17-alpine
created=true
for attempt in {1..30}; do
  if [[ "$(docker inspect --format '{{.State.Health.Status}}' combustible-e2e)" == healthy ]]; then break; fi
  sleep 1
done
mkdir -p artifacts
dotnet backend/src/Combustible.Api/bin/Debug/net10.0/Combustible.Api.dll --initialize > artifacts/initialize.log 2>&1
export ConnectionStrings__Database="Host=127.0.0.1;Port=15432;Database=combustible;Username=combustible_app;Password=$APP_DB_PASSWORD"
dotnet backend/src/Combustible.Api/bin/Debug/net10.0/Combustible.Api.dll --urls http://127.0.0.1:5080 > artifacts/api.log 2>&1 &
api_pid=$!
(cd frontend && exec node node_modules/vite/bin/vite.js) > artifacts/web.log 2>&1 &
web_pid=$!
ready=false
for attempt in {1..30}; do
  if curl --fail --silent http://127.0.0.1:5080/health/ready >/dev/null && curl --fail --silent http://127.0.0.1:5173 >/dev/null; then ready=true; break; fi
  sleep 1
done
if [[ "$ready" != true ]]; then echo 'Los servidores no arrancaron.' >&2; exit 1; fi
(cd frontend && npx playwright test)
