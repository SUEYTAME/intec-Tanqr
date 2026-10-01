#!/usr/bin/env bash
# Solo en contenedor efímero: prueba las barreras y recuperación sin acceder a Azure.
set -euo pipefail
[ "${TANQR_DEPLOY_TEST_CONTAINER:-}" = 1 ] || { echo 'Ejecutar exclusivamente en contenedor de pruebas'; exit 1; }
install -d /tmp/bin /opt/combustible /usr/local/sbin /var/lock
export PATH="/tmp/bin:$PATH"
export REVISION=1111111111111111111111111111111111111111
export SRC_SHA256=2222222222222222222222222222222222222222222222222222222222222222
export IMAGE_SHA256=3333333333333333333333333333333333333333333333333333333333333333
export DEPLOY_RUN_NUMBER=10
cat > /tmp/bin/docker <<'SH'
#!/usr/bin/env bash
set -eu
echo "$*" >> /tmp/docker-calls
case "$*" in
  *Config.Image*) echo intec-combustible:0.4.0-1111111 ;;
  *opencontainers.image.revision*) echo "$REVISION" ;;
  *State.Status*) echo running ;;
  *RestartCount*) echo 0 ;;
  *State.Health.Status*) echo healthy ;;
esac
SH
cat > /tmp/bin/curl <<'SH'
#!/usr/bin/env bash
echo '{"status":"ready"}'
SH
cat > /usr/local/sbin/combustible-backup <<'SH'
#!/usr/bin/env bash
echo backup >> /tmp/order
exit "${BACKUP_STATUS:-0}"
SH
chmod +x /tmp/bin/* /usr/local/sbin/combustible-backup
reset_case() {
  rm -rf /opt/combustible/releases
  rm -f /opt/combustible/deploy-run-number /tmp/order /tmp/docker-calls
  printf 'APP_VERSION=old\nSECRET=preserved\n' > /opt/combustible/produccion.env
  printf 'previous compose\n' > /opt/combustible/docker-compose.yml
  export INSTALLER_B64
  INSTALLER_B64=$(base64 -w0 <<'SH'
#!/usr/bin/env bash
set -eu
[ "$(head -n1 /tmp/order)" = backup ]
echo install >> /tmp/order
printf 'APP_VERSION=new\nSECRET=preserved\n' > /opt/combustible/produccion.env
echo new-compose > /opt/combustible/docker-compose.yml
exit "${INSTALL_STATUS:-0}"
SH
)
}
expect_failure() {
  if "$@" > /tmp/result 2>&1; then echo 'Se esperaba fallo'; cat /tmp/result; exit 1; fi
}
reset_case
bash deploy/azure/actualizar-ci.sh > /tmp/result
grep -qx "TANQR_DEPLOY_OK:$REVISION" /tmp/result
[ "$(cat /opt/combustible/deploy-run-number)" = 10 ]
echo 'PASS: éxito exige imagen/revisión/salud, respaldo antes de instalar y marcador final'
reset_case
INSTALL_STATUS=42 expect_failure bash deploy/azure/actualizar-ci.sh
grep -qx 'APP_VERSION=old' /opt/combustible/produccion.env
grep -qx 'SECRET=preserved' /opt/combustible/produccion.env
grep -qx 'previous compose' /opt/combustible/docker-compose.yml
grep -q 'up -d --no-deps app' /tmp/docker-calls
[ ! -f /opt/combustible/deploy-run-number ]
echo 'PASS: fallo recupera env/compose/app anteriores y no marca éxito'
reset_case
BACKUP_STATUS=1 expect_failure bash deploy/azure/actualizar-ci.sh
grep -qx 'APP_VERSION=old' /opt/combustible/produccion.env
[ ! -f /tmp/docker-calls ]
echo 'PASS: respaldo fallido impide tocar la aplicación'
reset_case
echo 11 > /opt/combustible/deploy-run-number
expect_failure bash deploy/azure/actualizar-ci.sh
[ ! -f /tmp/order ]
echo 'PASS: ejecución antigua rechazada antes del respaldo/despliegue'
REVISION=invalid expect_failure bash deploy/azure/actualizar-ci.sh
echo 'PASS: revisión inválida rechazada'
cat > /tmp/bin/az <<'SH'
#!/usr/bin/env bash
echo '{"value":[{"message":"Enable succeeded: script failed"}]}'
SH
chmod +x /tmp/bin/az
export AZURE_SUBSCRIPTION_ID=test DEPLOY_REVISION="$REVISION" GITHUB_RUN_NUMBER=10 GITHUB_STEP_SUMMARY=/tmp/summary
expect_failure bash scripts/ci-azure-deploy.sh
grep -q 'La VM no confirmó' /tmp/result
echo 'PASS: respuesta HTTP exitosa de Run Command sin marcador se trata como fallo'
