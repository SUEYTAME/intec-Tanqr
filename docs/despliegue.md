# Despliegue en producción

Versión de la aplicación: **0.4.0**. Última revisión: 2026-09-22.

## Arquitectura

Un solo contenedor sirve **la API y la interfaz web desde el mismo origen**. Kestrel termina
**TLS 1.3** directamente (TLS 1.2 y anteriores se rechazan; prueba
`Kestrel_rechaza_TLS_1_2_y_negocia_TLS_1_3`). No hay proxy inverso a propósito: sin él, el
límite de intentos de acceso y la auditoría ven la IP real de cada cliente.

```
Navegador / PWA ──HTTPS 1.3──► app (Kestrel :8443 → host :443) ──► db (PostgreSQL 17, sin puertos publicados)
```

La aplicación corre como usuario sin privilegios (`app`) y se conecta a la base con el rol
`combustible_app`, que no puede alterar la auditoría ni el historial de inventario. Las
migraciones las ejecuta un paso separado (`init`) con el propietario de la base.

Encabezados de seguridad: `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`,
`Referrer-Policy: no-referrer`, `X-Frame-Options: DENY` y, en la interfaz, una CSP de un solo
origen con `frame-ancestors 'none'` (prueba `Un_solo_origen_sirve_la_interfaz_con_CSP_y_la_API_sigue_intacta`).

## Requisitos previos (bloqueos del proyecto)

| Bloqueo | Qué hace falta | Sin él |
|---|---|---|
| B-03 | Dominio y certificado TLS del dominio en formato `.pfx` | No hay HTTPS válido; la cámara del despacho no funciona en el teléfono |
| B-02 | Servidor SMTP institucional (host, puerto, usuario, remitente) | El compose no arranca: `SMTP_HOST` es obligatorio |
| B-01 | Pasarela SMS | Los SMS quedan en la bandeja local (`/app/outbox`), nunca como enviados |
| B-04 | Datos reales (empleados, vehículos, tanques) | Se cargan desde la interfaz después del arranque |

Servidor: Linux con Docker Engine y Docker Compose v2.

## Pasos

### Usar la imagen de GitHub

CI publica en `ghcr.io/sueytame/intec-combustible` únicamente después de pasar backend,
frontend y navegador en la rama `fase-2-producto`. Cada imagen lleva `sha-<commit completo>`;
`fase-2-producto` apunta a la última publicación correcta. El paquete se conserva privado.
El código y la imagen publicados no equivalen a un servidor funcionando en internet.

Para usar una revisión publicada, autenticar Docker en `ghcr.io` con una credencial con
permiso de lectura del paquete (por entrada estándar, nunca pegada en un comando guardado).
Descargar y asignar la etiqueta local que espera el compose:

```bash
docker pull ghcr.io/sueytame/intec-combustible:sha-<commit completo>
docker tag ghcr.io/sueytame/intec-combustible:sha-<commit completo> intec-combustible:0.4.0
```

Luego continuar desde el paso 2 con `APP_VERSION=0.4.0`. Guardar el SHA o digest descargado
en el registro del despliegue. La autenticación y visibilidad siguen la
[documentación de GitHub Container registry](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry).

### Construir y configurar el servidor

1. **Construir la imagen** (desde la raíz del repositorio):

   ```bash
   docker build -t intec-combustible:0.4.0 .
   ```

2. **Configurar.** Copiar `deploy/produccion.env.example` a `deploy/produccion.env` (ignorado por
   Git) y completar todas las variables. Las claves se generan como indica la plantilla.
   Guardar una copia de `DATA_ENCRYPTION_KEY` y `QR_SIGNING_KEY_B64` **fuera del servidor**:
   perderlas hace ilegibles los datos personales y los tickets emitidos.

3. **Certificado.** Poner `tls.pfx` en la carpeta indicada por `TLS_PFX_DIR`, legible por el
   usuario del contenedor.

4. **Inicializar la base** (primera vez y en cada actualización de versión):

   ```bash
   docker compose -f deploy/docker-compose.prod.yml --env-file deploy/produccion.env --profile init run --rm init
   ```

   Aplica migraciones, crea/actualiza el rol `combustible_app` y sus permisos, cifra datos
   personales anteriores y crea el administrador inicial (`BOOTSTRAP_EMAIL`).

5. **Arrancar:**

   ```bash
   docker compose -f deploy/docker-compose.prod.yml --env-file deploy/produccion.env up -d app
   ```

6. **Comprobar:**

   ```bash
   curl -fsS https://DOMINIO/health/ready
   ```

   Entrar con el administrador inicial, **activar MFA** en *Mi seguridad* y cambiar la
   contraseña. Crear los usuarios reales.

## Operación

- **Copias de seguridad:** diariamente
  `docker compose -f deploy/docker-compose.prod.yml --env-file deploy/produccion.env exec db pg_dump -U "$POSTGRES_USER" -Fc "$POSTGRES_DB" > respaldo.dump`,
  guardadas fuera del servidor. Probar la restauración periódicamente.
- **Ancla de auditoría (RS-06):** un Auditor o Administrador descarga cada semana el ancla firmada
  (*Auditoría → Descargar ancla firmada*) y la guarda fuera del sistema. Sirve para demostrar
  que nadie reconstruyó la bitácora.
- **Actualizar:** construir la nueva imagen, cambiar `APP_VERSION`, repetir los pasos 4 y 5.
- **Registros:** `docker compose -f deploy/docker-compose.prod.yml logs app`.
- **Trabajos automáticos:** vencimiento de tickets, avisos, programaciones y reintentos de entrega
  corren dentro de la aplicación; un contenedor `app` basta. Con varias réplicas, los bloqueos de
  PostgreSQL (`pg_try_advisory_xact_lock`, `FOR UPDATE SKIP LOCKED`) evitan trabajo duplicado.

## Qué no cubre este despliegue

- Alta disponibilidad de la base de datos (una sola instancia).
- Envío real de SMS (B-01).
- Prueba en Android físico (CA-6): hacerla contra este despliegue, con el certificado real.
