#!/usr/bin/env bash
# Ejecutado en la VM por Run Command; parámetros estrictos generados por CI.
set -euo pipefail
[[ "${REVISION:-}" =~ ^[0-9a-f]{40}$ && "${SRC_SHA256:-}" =~ ^[0-9a-f]{64}$ \
  && "${IMAGE_SHA256:-}" =~ ^[0-9a-f]{64}$ && "${DEPLOY_RUN_NUMBER:-}" =~ ^[1-9][0-9]*$ ]] \
  || { echo 'Parámetros de despliegue inválidos'; exit 1; }
BASE=/opt/combustible
exec 9>/var/lock/combustible-deploy.lock
flock -w 1200 9
if [ -f "$BASE/deploy-run-number" ]; then
  previous_run=$(cat "$BASE/deploy-run-number")
  [[ "$previous_run" =~ ^[1-9][0-9]*$ ]] || { echo 'Registro de despliegue inválido'; exit 1; }
  [ "$DEPLOY_RUN_NUMBER" -ge "$previous_run" ] || { echo 'Despliegue antiguo rechazado: ya se publicó una ejecución posterior'; exit 1; }
fi
RELEASE="$BASE/releases/$REVISION"
install -d -m 0700 "$RELEASE"
[ -f "$BASE/produccion.env" ] && [ -f "$BASE/docker-compose.yml" ] \
  || { echo 'La VM debe estar aprovisionada antes del despliegue CI'; exit 1; }
cp -p "$BASE/produccion.env" "$RELEASE/previous.env"
cp -p "$BASE/docker-compose.yml" "$RELEASE/previous-compose.yml"
# El respaldo termina antes de tocar la configuración o la aplicación.
/usr/local/sbin/combustible-backup
printf '%s' "$INSTALLER_B64" | base64 -d > "$RELEASE/instalar.sh"
chmod 0700 "$RELEASE/instalar.sh"
unset INSTALLER_B64
rollback() {
  local original_status=$?
  trap - ERR
  echo 'Actualización fallida: recuperando aplicación/configuración anteriores. No se restaura la BD automáticamente.' >&2
  cp -p "$RELEASE/previous.env" "$BASE/produccion.env"
  cp -p "$RELEASE/previous-compose.yml" "$BASE/docker-compose.yml"
  if docker compose -p combustible -f "$BASE/docker-compose.yml" --env-file "$BASE/produccion.env" up -d --no-deps app \
      && curl --fail --silent --show-error --retry 10 --retry-delay 5 --retry-connrefused \
        https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com/health/ready; then
    echo 'ROLLBACK_OK' >&2
  else
    echo 'ROLLBACK_FAILED: requiere intervención' >&2
  fi
  exit "$original_status"
}
trap rollback ERR
bash "$RELEASE/instalar.sh"
expected="intec-combustible:0.4.0-${REVISION:0:7}"
[ "$(docker inspect combustible-app-1 --format '{{.Config.Image}}')" = "$expected" ]
[ "$(docker inspect combustible-app-1 --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')" = "$REVISION" ]
[ "$(docker inspect combustible-app-1 --format '{{.State.Status}}')" = running ]
[ "$(docker inspect combustible-app-1 --format '{{.RestartCount}}')" = 0 ]
[ "$(docker inspect combustible-db-1 --format '{{.State.Health.Status}}')" = healthy ]
curl --fail --silent --show-error --retry 5 --retry-delay 5 \
  https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com/health/ready
printf '%s\n' "$DEPLOY_RUN_NUMBER" > "$BASE/deploy-run-number.new"
mv "$BASE/deploy-run-number.new" "$BASE/deploy-run-number"
printf '%s\n' "$REVISION" > "$BASE/deployed-revision"
trap - ERR
# Run Command puede devolver HTTP éxito aunque el script falle: CI exige este marcador final.
printf '\nTANQR_DEPLOY_OK:%s\n' "$REVISION"
