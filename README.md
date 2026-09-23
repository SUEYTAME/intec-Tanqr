# Plataforma de Tickets Digitales de Combustible — INTEC

Web + PWA para tickets con QR firmado, inventario y despacho. Versión 0.4.0: funcionalmente
completa; producción pendiente de SMTP, SMS, dominio/certificado y datos reales (B-01..B-04).
INTEC es el contexto del documento de requisitos. Una demo puede usar datos ficticios y
correo de prueba; no necesita esperar recursos institucionales. Publicar el repositorio o
su contenedor en GitHub no pone automáticamente la aplicación en internet.
El estado verificable y los pendientes están en `vault/Estado actual del proyecto.md`.
Agentes: leer `AGENTS.md` primero. Requisitos: `docs/SRS.pdf`.

## Acceder en Windows

1. Abre Docker Desktop.
2. Haz doble clic en **Iniciar.cmd**, o ejecuta `powershell -File scripts/iniciar.ps1` (Windows PowerShell 5.1 basta).
3. Entra en **http://localhost:5173**.
4. Consulta el usuario y la contraseña inicial en **artifacts/acceso-local.txt**.

El arranque crea los secretos que falten en `.env`, levanta PostgreSQL, aplica las
migraciones explícitamente y crea un administrador local solo si no hay usuarios.
No reemplaza contraseñas existentes ni borra volúmenes. `.env` y `artifacts/` se ignoran
por Git; no los publiques. Las credenciales locales no son una configuración de producción.
Si cambias la contraseña desde la aplicación, el archivo de acceso conserva el valor inicial.

Para actualizar los servidores tras cambiar código: `./scripts/iniciar.ps1 -Reiniciar`.
Para detener solo API y web: `./scripts/detener.ps1`. PostgreSQL y sus datos se conservan.
Los scripts solo detienen PID, fecha de inicio y comando que coincidan con su registro.

## Qué puedes usar

- Login, sesión revocable, MFA TOTP y códigos de recuperación; usuarios y 5 roles.
- Catálogos: departamentos, empleados (cédula, correo y móvil cifrados AES-256-GCM), vehículos,
  combustibles, estaciones y tanques.
- Solicitudes manuales, programadas y recurrentes; aprobación; tickets numerados sin huecos con
  QR firmado (ECDSA P-256), PDF, envío por correo (Mailpit en local) y enlace público seguro.
- Despacho en la PWA instalable: cámara, lector externo o foto; validación en línea, confirmación
  de identidad y descuento atómico del inventario. Sin conexión no se registra nada.
- Inventario en tiempo real: recepciones con RNC, transferencias, ajustes y mermas con motivo.
- Cierre diario con acta PDF; reportes CSV/Excel/PDF; panel ejecutivo; notificaciones.
- Auditoría encadenada con ancla firmada externa; OAuth 2.0 client credentials para integraciones.

Manual: `docs/manual-usuario.md`. Despliegue en producción: `docs/despliegue.md`.
Demo en Azure (datos ficticios): https://intec-fuel-dev-b805.northcentralus.cloudapp.azure.com —
acceso y operación en `docs/azure.md`.

## Requisitos locales

.NET SDK según `backend/global.json`, Node 24.15.0, npm, Windows PowerShell 5.1 y Docker Desktop.
Correo local: Mailpit en http://127.0.0.1:18025.
PostgreSQL se publica únicamente en `127.0.0.1:15432` (configurable en `.env`);
API en `127.0.0.1:5080`, web en `127.0.0.1:5173`.
Adminer es opcional: `docker compose --profile herramientas up -d`, puerto 8080 local.
No se detienen servicios ajenos si un puerto está ocupado: corrige la configuración.

## Verificar

```powershell
# Desde backend/
dotnet build
dotnet test
# Desde frontend/
npm ci
npm run build
npm run lint
npx playwright install chromium
# Desde la raíz, con la aplicación levantada
./scripts/probar-interfaz.ps1

docker compose config --quiet
docker compose ps
```

Las pruebas .NET usan PostgreSQL 17 efímero con Testcontainers: requieren Docker activo,
no acceden a la base local. Las pruebas de navegador locales crean departamentos marcados
`QA-` / `Prueba UI` y los dejan inactivos; en CI corren en una base efímera separada.
No confundir la emulación móvil con una prueba en un Android físico.

CI en `.github/workflows/ci.yml`: build/test .NET, build/lint React y navegador real con
API + PostgreSQL. Los secretos de CI se generan por ejecución y se enmascaran.

## Límites actuales

- SMS: solo bandeja local hasta tener pasarela (B-01). Correo real requiere SMTP institucional (B-02).
- Certificado y dominio (B-03); sin ellos la cámara del teléfono no funciona fuera de localhost.
- CA-6 (prueba en Android físico) pendiente. La emulación móvil no la sustituye.
- Carga probada con base casi vacía (50 usuarios, 250 rps, 0 errores); no con volumen real.

El vault dentro del repositorio mantiene estado, decisiones delegadas, evidencia y próximos
pasos para continuar en una sesión nueva con “continúa con el programa”.
