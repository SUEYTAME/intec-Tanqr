#!/usr/bin/env bash
# Instalación/actualización idempotente en la VM de Azure INTEC. Se ejecuta como root con
#   az vm run-command invoke ... --scripts @deploy/azure/instalar.sh   (ver docs/azure.md)
# No usa SSH: el código llega por Blob privado y los secretos por Key Vault, ambos con la
# identidad administrada de la VM. Nunca imprime secretos (sin set -x).
set -euo pipefail
exec > >(tee -a /var/log/combustible-instalar.log) 2>&1

FQDN=intec-fuel-dev-b805.northcentralus.cloudapp.azure.com
VAULT=kv-intec-fuel-dev-b805
STORAGE=stintecfueldevb805
REVISION=9fed40e889e168895b13caea3e773e73aba08b9c
SRC_SHA256=677f782e11955a22189c6505b90ee03e3bb5630d52f122c3eb252017fcdab9cf
APP_VERSION=0.4.0-${REVISION:0:7}
# Cuenta inicial ficticia (demo). No es un dato de INTEC.
BOOTSTRAP_EMAIL=admin@combustible-demo.test
BASE=/opt/combustible
APP_UID=1654   # usuario "app" de las imágenes mcr.microsoft.com/dotnet (se comprueba abajo)

log() { echo "[$(date -u +%H:%M:%S)] $*"; }
token() { curl -fsS -H Metadata:true "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=$1" \
  | python3 -c 'import json,sys;print(json.load(sys.stdin)["access_token"])'; }
install -d -m 0700 "$BASE" "$BASE/certs" /var/backups/combustible

# --- 1. Código de la revisión probada en CI, desde Blob privado ---
SRC_DIR="$BASE/src-${REVISION:0:7}"
if [ ! -d "$SRC_DIR" ]; then
  log "Descargando código $REVISION"
  ST=$(token https://storage.azure.com/)
  curl -fsS -H "Authorization: Bearer $ST" -H "x-ms-version: 2023-11-03" \
    "https://$STORAGE.blob.core.windows.net/deployments/src-${REVISION:0:7}.tar.gz" -o "$BASE/src.tar.gz"
  echo "$SRC_SHA256  $BASE/src.tar.gz" | sha256sum -c -
  install -d -m 0700 "$SRC_DIR"; tar -xzf "$BASE/src.tar.gz" -C "$SRC_DIR"; rm -f "$BASE/src.tar.gz"
fi
if ! docker image inspect "intec-combustible:$APP_VERSION" >/dev/null 2>&1; then
  log "Construyendo imagen intec-combustible:$APP_VERSION"
  docker build -q -t "intec-combustible:$APP_VERSION" "$SRC_DIR"
fi
[ "$(docker run --rm --entrypoint id "intec-combustible:$APP_VERSION" -u app)" = "$APP_UID" ] || { echo "UID de app inesperado"; exit 1; }
install -m 0600 "$SRC_DIR/deploy/docker-compose.prod.yml" "$BASE/docker-compose.yml"

# --- 2. Secretos desde Key Vault a un env local 0600 ---
KV=$(token https://vault.azure.net)
secret() { curl -fsS -H "Authorization: Bearer $KV" "https://$VAULT.vault.azure.net/secrets/$1?api-version=7.4" \
  | python3 -c 'import json,sys;print(json.load(sys.stdin)["value"])'; }
umask 077
{
  echo "APP_VERSION=$APP_VERSION"
  echo "POSTGRES_USER=combustible"
  echo "POSTGRES_PASSWORD=$(secret postgres-password)"
  echo "POSTGRES_DB=combustible"
  echo "APP_DB_PASSWORD=$(secret app-db-password)"
  echo "BOOTSTRAP_EMAIL=$BOOTSTRAP_EMAIL"
  echo "BOOTSTRAP_PASSWORD=$(secret bootstrap-password)"
  echo "JWT_SIGNING_KEY=$(secret jwt-signing-key)"
  echo "DATA_ENCRYPTION_KEY=$(secret data-encryption-key)"
  echo "QR_SIGNING_KEY_B64=$(secret qr-signing-key-b64)"
  echo "OAUTH_SIGNING_KEY_B64=$(secret oauth-signing-key-b64)"
  echo "PUBLIC_BASE_URL=https://$FQDN"
  echo "SMTP_HOST=$(secret smtp-host)"
  echo "SMTP_PORT=$(secret smtp-port)"
  echo "SMTP_USER=$(secret smtp-user)"
  echo "SMTP_PASSWORD=$(secret smtp-password)"
  echo "SMTP_FROM=$(secret smtp-from)"
  echo "TLS_PFX_DIR=$BASE/certs"
  echo "TLS_PFX_PASSWORD=$(secret tls-pfx-password)"
} > "$BASE/produccion.env.new"
mv "$BASE/produccion.env.new" "$BASE/produccion.env"
umask 022
unset KV
COMPOSE="docker compose -p combustible -f $BASE/docker-compose.yml --env-file $BASE/produccion.env"

# --- 3. Certificado Let's Encrypt y renovación (Kestrel TLS 1.3 directo, sin proxy) ---
install -d -m 0755 /etc/letsencrypt/renewal-hooks/deploy
cat > /etc/letsencrypt/renewal-hooks/deploy/combustible.sh <<EOF
#!/bin/sh
# Convierte el certificado renovado a PFX para Kestrel y reinicia la app.
set -eu
PASS=\$(grep '^TLS_PFX_PASSWORD=' $BASE/produccion.env | cut -d= -f2-)
export PASS
openssl pkcs12 -export -inkey /etc/letsencrypt/live/$FQDN/privkey.pem -in /etc/letsencrypt/live/$FQDN/fullchain.pem \
  -out $BASE/certs/tls.pfx.new -passout env:PASS
chown $APP_UID:$APP_UID $BASE/certs/tls.pfx.new; chmod 0400 $BASE/certs/tls.pfx.new
mv $BASE/certs/tls.pfx.new $BASE/certs/tls.pfx
chown $APP_UID:$APP_UID $BASE/certs; chmod 0500 $BASE/certs
if docker compose -p combustible -f $BASE/docker-compose.yml --env-file $BASE/produccion.env ps --status running app | grep -q app; then
  docker compose -p combustible -f $BASE/docker-compose.yml --env-file $BASE/produccion.env restart app
fi
EOF
chmod 0700 /etc/letsencrypt/renewal-hooks/deploy/combustible.sh
if [ ! -f "/etc/letsencrypt/live/$FQDN/fullchain.pem" ]; then
  log "Solicitando certificado para $FQDN"
  certbot certonly --standalone -d "$FQDN" --non-interactive --agree-tos --register-unsafely-without-email
fi
[ -f "$BASE/certs/tls.pfx" ] || RENEWED_LINEAGE="/etc/letsencrypt/live/$FQDN" /etc/letsencrypt/renewal-hooks/deploy/combustible.sh
systemctl enable --now certbot.timer

# --- 4. Base de datos, inicialización y aplicación ---
log "Inicializando base (migraciones, rol restringido, administrador inicial)"
$COMPOSE --profile init run --rm init
$COMPOSE up -d app
for i in $(seq 1 30); do
  curl -fsS --resolve "$FQDN:443:127.0.0.1" "https://$FQDN/health/ready" >/dev/null 2>&1 && break
  sleep 5
done
curl -fsS --resolve "$FQDN:443:127.0.0.1" "https://$FQDN/health/ready"; echo

# --- 5. Respaldo diario privado en Blob y prueba de restauración ---
cat > /usr/local/sbin/combustible-backup <<EOF
#!/usr/bin/env bash
# pg_dump diario al contenedor privado "backups" con la identidad administrada de la VM.
set -euo pipefail
f=/var/backups/combustible/combustible-\$(date -u +%Y%m%dT%H%M%SZ).dump
$COMPOSE exec -T db sh -c 'pg_dump -U "\$POSTGRES_USER" -Fc "\$POSTGRES_DB"' > "\$f"
t=\$(curl -fsS -H Metadata:true "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https://storage.azure.com/" | python3 -c 'import json,sys;print(json.load(sys.stdin)["access_token"])')
curl -fsS -X PUT -H "Authorization: Bearer \$t" -H "x-ms-version: 2023-11-03" -H "x-ms-blob-type: BlockBlob" \
  --data-binary @"\$f" "https://$STORAGE.blob.core.windows.net/backups/\$(basename "\$f")"
find /var/backups/combustible -name '*.dump' -mtime +7 -delete
echo "Respaldo subido: \$(basename "\$f") (\$(stat -c %s "\$f") bytes)"
EOF
cat > /usr/local/sbin/combustible-restore-test <<EOF
#!/usr/bin/env bash
# Descarga el último respaldo de Blob, lo restaura en un PostgreSQL temporal aislado (sin red)
# y compara el número de filas de cada tabla con la base en uso.
set -euo pipefail
t=\$(curl -fsS -H Metadata:true "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https://storage.azure.com/" | python3 -c 'import json,sys;print(json.load(sys.stdin)["access_token"])')
name=\$(curl -fsS -H "Authorization: Bearer \$t" -H "x-ms-version: 2023-11-03" "https://$STORAGE.blob.core.windows.net/backups?restype=container&comp=list" \
  | python3 -c 'import sys,re;n=sorted(re.findall(r"<Name>([^<]+\.dump)</Name>",sys.stdin.read()));print(n[-1] if n else "")')
[ -n "\$name" ] || { echo "No hay respaldos en Blob"; exit 1; }
tmp=\$(mktemp -d); trap 'docker rm -f combustible-restore-test >/dev/null 2>&1 || true; rm -rf "\$tmp"' EXIT
curl -fsS -H "Authorization: Bearer \$t" -H "x-ms-version: 2023-11-03" "https://$STORAGE.blob.core.windows.net/backups/\$name" -o "\$tmp/r.dump"
chmod 0644 "\$tmp/r.dump"; chmod 0755 "\$tmp"
docker run -d --name combustible-restore-test --network none -e POSTGRES_PASSWORD=\$(openssl rand -hex 16) -v "\$tmp:/r:ro" postgres:17-alpine >/dev/null
for i in \$(seq 1 30); do docker exec combustible-restore-test pg_isready -U postgres >/dev/null 2>&1 && break; sleep 2; done
sleep 3
docker exec combustible-restore-test createdb -U postgres restaurada
docker exec combustible-restore-test pg_restore -U postgres -d restaurada --no-owner --no-acl /r/r.dump
q="select string_agg(format('select %L as t, count(*) as n from %I.%I', table_schema||'.'||table_name, table_schema, table_name), ' union all ') from information_schema.tables where table_schema='public' and table_type='BASE TABLE'"
counts() { \$1 psql -U "\$2" -d "\$3" -At -c "\$(\$1 psql -U "\$2" -d "\$3" -At -c "\$q") order by 1"; }
live=\$(counts "$COMPOSE exec -T db" combustible combustible)
rest=\$(counts "docker exec combustible-restore-test" postgres restaurada)
echo "Respaldo \$name: \$(echo "\$rest" | wc -l) tablas restauradas"
if [ "\$live" = "\$rest" ]; then echo "RESTAURACION OK: filas por tabla idénticas a la base en uso"; else echo "DIFERENCIAS (puede haber escrituras posteriores al respaldo):"; diff <(echo "\$live") <(echo "\$rest") || true; exit 2; fi
EOF
chmod 0700 /usr/local/sbin/combustible-backup /usr/local/sbin/combustible-restore-test
cat > /etc/systemd/system/combustible-backup.service <<EOF
[Unit]
Description=Respaldo diario de PostgreSQL de combustible a Blob privado
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/combustible-backup
EOF
cat > /etc/systemd/system/combustible-backup.timer <<EOF
[Unit]
Description=Respaldo diario de combustible
[Timer]
OnCalendar=*-*-* 07:30:00 UTC
Persistent=true
[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now combustible-backup.timer
log "Instalación terminada: https://$FQDN"
$COMPOSE ps --format '{{.Service}} {{.Status}} {{.Ports}}'
