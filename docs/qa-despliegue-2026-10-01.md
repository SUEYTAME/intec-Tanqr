# Revisión y publicación de cambios de Hesler — 2026-10-01

## Resultado

Publicado en https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com:
`intec-combustible:0.4.0-e21e3c2`, commit exacto
`e21e3c293a62a1fec4dc0dd3bc939817fee1128f`. Autorización del titular:
revisar los cambios de GitHub y desplegarlos en la app Azure de presentación.

## Qué se revisó

Comparación desde la versión activa `4db7a30`. Cambios de Hesler:
`c063a60`, `97f86f7`, `2c05e65`, `f714b08`, `e21e3c2`.
Textos de validación y cuenta en español; presentación del resultado de entrega
sin rutas ni respuestas técnicas de SMTP/Twilio; etiquetas de auditoría y nombre
del actor. La consulta resuelve el nombre actual del usuario; el identificador
original permanece en el evento almacenado y en su hash. No cambia el esquema,
las reglas de inventario, los estados de entrega ni la firma QR.

## Por qué los commits no aparecían en Azure

`.github/workflows/ci.yml` ejecuta backend, frontend y navegador, y publica en
GHCR una imagen privada. No contiene un job que despliegue en Azure. La VM
construye su imagen desde un paquete privado de Blob (ADR-015); el instalador
seguía fijado a `4db7a30`. No se encontró un despliegue automático desactivado.
Esta publicación es manual; futuros commits necesitan repetir el despliegue.

## Verificación previa

- CI [36932529714](https://github.com/SUEYTAME/intec-Tanqr/actions/runs/36932529714),
  exactamente para `e21e3c2`: cuatro jobs success; backend **122/122**,
  Playwright **10/10** (escritorio y móvil emulado), frontend build/lint y GHCR.
- Checkout limpio separado `C:\Dev\intec-combustible-review-e21e3c2`:
  `dotnet test backend -c Release --nologo` **122/122**, ninguna omitida;
  `npm ci`, `npm run build`, `npm run lint` correctos.
- Lockfile y dependencias sin cambios. `npm audit` informa una dependencia
  transitiva de desarrollo de severidad baja (`serialize-javascript`), preexistente;
  no se alteraron dependencias dentro de este despliegue.
- PostgreSQL respaldado a Blob antes de actualizar:
  `combustible-20261001T224635Z.dump`, 186956 bytes.
- Configuración anterior y snapshot agregado de 33 tablas guardados en la VM,
  `/opt/combustible/release-e21e3c2/`, acceso root. Sin exportar datos personales.

## Publicación y verificación posterior

- Paquete `src-e21e3c2.tar.gz`: 517318 bytes,
  SHA-256 `242fc5917944e27db988276f4a85abb600bdb74a0a2d20d87265a04d8545a331`.
  Instalador confirmó el checksum y terminó **2026-10-01 22:49:22 UTC**.
- Imagen construida:
  `sha256:a2249786323aeaa93c61f686a59dd87b736499f13906ebe81e6d39d198323538`.
- Inicialización: ninguna migración aplicada; base actualizada previamente.
  PostgreSQL conservó su contenedor y volumen; se recreó la aplicación.
- VM: app running, `RestartCount=0`; PostgreSQL healthy; salud pública
  `/health/ready` **HTTP 200**, `{"status":"ready"}`.
- Comparación de configuración: ningún cambio excepto `APP_VERSION`.
  Se preservó la configuración SMS vigente sin enviar mensajes de prueba.
- Comparación de las 33 tablas: ningún conteo cambió; los digest de tablas
  comparadas quedaron idénticos. Se incluyeron tanques, despachos, movimientos,
  recepciones, cierres, solicitudes, tickets y migraciones. Auditoría/sesiones
  se compararon por conteo, pues pueden crecer por actividad normal.
- Bundle público `/assets/index-psv1uddv.js` idéntico byte por byte al build local
  probado: SHA-256
  `87c81741e1f72edc7bee3b155262dbee991683c424b7bd765af1b22589177ee1e`.
- Sesión Administrador existente en Chrome: los 19 tickets siguen visibles;
  COM-2026-000019 muestra correo aceptado y SMS de demostración en español.
  Auditoría muestra nombres de usuario, sin modificar los eventos originales.

## Alcance y operación

No se detectaron regresiones en las pruebas ejecutadas. La suite de navegador
completa se ejecutó en CI con base efímera; la comprobación Azure fue de lectura
con la sesión ya abierta. No se emitieron ni despacharon tickets para esta revisión.
El frontend de una pestaña abierta conserva el JavaScript anterior: recargar con
**Ctrl+Shift+R** y volver a iniciar sesión para cargar todas las etiquetas nuevas.
No se recargó la sesión existente durante QA, para conservar su acceso.

Rollback disponible: imagen `0.4.0-4db7a30`, configuración/compose anteriores
en `/opt/combustible/release-e21e3c2/`, y respaldo de PostgreSQL. Como no hubo
cambios de esquema, una reversión de aplicación no necesita restaurar la base.

Evidencia local ignorada: `artifacts/azure/ci-e21e3c2.log`,
`deploy-e21e3c2-result.json`, `postflight-e21e3c2-result.json` y scripts de QA.
