# Publicación en Azure — INTEC

Actualizado: 2026-09-23. **Desplegada y verificada.** Es una demo con datos ficticios.

**URL:** https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com

## Cuenta y alcance autorizados

El usuario autorizó crear lo necesario exclusivamente en su cuenta de INTEC:

- Cuenta: `1128305@est.intec.edu.do`.
- Suscripción: `Azure for Students`, `44f41884-c42a-4162-898f-d83d8d987ff3`.
- Tenant: `6856181f-daf8-4725-ac51-dd9f7dfe2f2b`.
- Grupo: `rg-intec-fuel-dev-b805`, región `northcentralus`.

Los scripts pasan la suscripción explícitamente: la sesión CLI también tiene una cuenta
de otra organización que no está autorizada para este despliegue.

## Acceso

- Usuario inicial: `admin@combustible-demo.test` (ficticio, rol Administrador).
- Contraseña: **solo en Key Vault**, nunca en el repositorio ni en el chat. Para verla, con tu
  sesión de Azure iniciada:

  ```powershell
  az keyvault secret show --subscription 44f41884-c42a-4162-898f-d83d8d987ff3 --vault-name kv-intec-fuel-dev-b805 -n bootstrap-password --query value -o tsv
  ```

- Al entrar por primera vez: **activar MFA** en *Mi seguridad* y cambiar la contraseña. El cambio
  no actualiza Key Vault; ese secreto solo sirve para el primer acceso.

## Recursos

| Recurso | Nombre | Para qué |
|---|---|---|
| VM Ubuntu 24.04, `Standard_B2als_v2` (2 CPU, 4 GiB) | `vm-intec-fuel-dev-b805` | Docker: app (Kestrel TLS 1.3 :443) + PostgreSQL 17 sin puertos publicados |
| IP pública estática + DNS | `pip-intec-fuel-dev-b805` → `130.131.46.104` | FQDN `*.cloudapp.azure.com` |
| NSG | `nsg-intec-fuel-dev-b805` | 443 y 80 (ACME) públicos; 22 solo IP de administración |
| Key Vault (RBAC) | `kv-intec-fuel-dev-b805` | 8 secretos de la app + `smtp-*` |
| Storage (sin claves de cuenta) | `stintecfueldevb805` | `backups/` diarios y `deployments/` (código) |
| Communication Services + Email | `acs-intec-fuel-dev-b805`, `email-intec-fuel-dev-b805` | SMTP de la demo |
| App Entra | `intec-combustible-smtp-b805` | Credencial SMTP (secreto vence 2027-09-23) |

IaC: `infra/main.bicep`, `infra/modules/resources.bicep`, `infra/correo.bicep`. Decisiones: ADR-014 y ADR-015.

## Cómo se desplegó (y cómo repetirlo)

Todo es idempotente; repetir un paso reutiliza lo existente.

1. Iniciar sesión **con MFA**. Si Azure responde "without authenticating through MFA", el token solo
   tiene `amr=pwd`; forzar el segundo factor:

   ```powershell
   $c = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('{"access_token":{"amr":{"essential":true,"values":["mfa"]}}}'))
   az login --tenant 6856181f-daf8-4725-ac51-dd9f7dfe2f2b --use-device-code --scope https://management.core.windows.net//.default --claims-challenge $c
   ```

2. `./scripts/azure-infra.ps1` (what-if) y luego `./scripts/azure-infra.ps1 -Deploy`.
3. `./scripts/azure-secretos.ps1`: genera una vez los secretos y los guarda en Key Vault.
4. `./scripts/azure-correo.ps1` (what-if) y `-Deploy`: app Entra, ACS y `smtp-*` en Key Vault.
5. Subir el código probado y ejecutar el instalador en la VM (sin SSH, ver ADR-015):

   ```powershell
   git archive --format=tar.gz -o artifacts/azure/src-345e623.tar.gz 345e6231e2fc1d69ec2c99a0fa5300471793da62
   az storage blob upload --subscription 44f41884-c42a-4162-898f-d83d8d987ff3 --auth-mode login --account-name stintecfueldevb805 -c deployments -n src-345e623.tar.gz -f artifacts/azure/src-345e623.tar.gz --overwrite
   az vm run-command invoke --subscription 44f41884-c42a-4162-898f-d83d8d987ff3 -g rg-intec-fuel-dev-b805 -n vm-intec-fuel-dev-b805 --command-id RunShellScript --scripts '@deploy/azure/instalar.sh'
   ```

   `deploy/azure/instalar.sh` comprueba el SHA-256 del paquete, construye la imagen, escribe el env
   (0600) desde Key Vault, obtiene el certificado Let's Encrypt, inicializa la base, arranca la app
   e instala respaldo y prueba de restauración. **Para actualizar** a otro commit: cambiar
   `REVISION` y `SRC_SHA256` en el script, subir el nuevo paquete y repetir el paso 5.

## Operación

- **Certificado:** `certbot.timer` renueva; el hook `/etc/letsencrypt/renewal-hooks/deploy/combustible.sh`
  genera el PFX y reinicia la app. Vence 2026-12-22 si no se renovara.
- **Respaldo:** `combustible-backup.timer` diario 07:30 UTC → `backups/combustible-<AAAAMMDDTHHMMSSZ>.dump`
  en Blob (versionado y borrado suave 7 días). Copias locales de 7 días en `/var/backups/combustible`.
- **Prueba de restauración:** `/usr/local/sbin/combustible-restore-test` restaura el último respaldo en
  un PostgreSQL aislado sin red y compara filas por tabla con la base en uso.
- Ejecutar cualquiera de estos con `az vm run-command invoke ... --scripts "<comando>"`.
- **Registros:** `docker compose -p combustible -f /opt/combustible/docker-compose.yml --env-file /opt/combustible/produccion.env logs app`
  y `/var/log/combustible-instalar.log`.

## Verificación (2026-09-23, desde internet)

| Comprobación | Resultado |
|---|---|
| TLS 1.3 | Aceptado, `TLS_AES_256_GCM_SHA384`, certificado Let's Encrypt válido (`Verify return code: 0`) |
| TLS 1.2 | Rechazado: alerta 70 `protocol version` |
| `/health/ready` | 200 `{"status":"ready"}` |
| Login real + `/api/auth/me` | Correcto, rol Administrador; clave incorrecta → 401 |
| Interfaz/PWA | Contexto seguro, manifest y service worker activos, sin errores de consola |
| PostgreSQL desde internet | Cerrado (5432/15432) |
| Respaldo a Blob | `combustible-20260923T085017Z.dump`, 100653 bytes |
| Restauración | 33 tablas, filas idénticas |
| Renovación | `certbot renew --dry-run` correcto |

## Costo

Estimación consultada en [Azure Retail Prices API](https://prices.azure.com/api/retail/prices):
unos **USD 36.29/mes** (VM 27.45, disco 5.00, IPv4 3.65, Blob 0.16, Key Vault 0.03). No incluye
impuestos, tráfico saliente adicional ni correo (ACS cobra por mensaje). El crédito restante de la
cuenta no se ha consultado. Apagar la VM (`az vm deallocate`) detiene el cargo de cómputo, no el de
disco ni IP. Una sola VM no ofrece alta disponibilidad.

## Límites conocidos

- **SSH:** la clave `artifacts/azure/id_ed25519` tiene una frase de paso desconocida; la administración
  se hace con `run-command` (ADR-015).
- **Imagen:** construida en la VM desde `345e623`, no descargada de GHCR (paquete privado sin credencial en la VM).
- **Correo:** ACS entregó el ticket de prueba `COM-2026-000001` en el buzón del usuario (2026-09-23, bandeja de entrada, QR y PDF). Es correo de demo;
  el SMTP institucional (B-02) sigue pendiente para operación real.
- SMS (B-01), datos reales (B-04) y prueba en Android físico (CA-6) siguen pendientes. Revisión desplegada: `465eaa1` (ADR-016).
- Data Protection guarda sus claves dentro del contenedor (aviso en el arranque). Solo afecta al token de restablecimiento de contraseña, que se genera y consume en la misma petición.
- Key Vault con *purge protection* activada (2026-09-23, irreversible).
- Datos DEMO en producción: departamento/empleado/vehículo/combustible/estación/tanque `DEMO*`, ticket `COM-2026-000001` consumido, cierre del 2026-09-23 de `DEMO-EST` y usuario `despacho@combustible-demo.test` desactivado.
