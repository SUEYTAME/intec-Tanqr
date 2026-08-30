# Plataforma de Tickets Digitales de Combustible — INTEC

Web + PWA para tickets con QR firmado, inventario y despacho. Desarrollo en curso.
El estado verificable y los pendientes están en `vault/Estado actual del proyecto.md`.
Agentes: leer `AGENTS.md` primero. Requisitos: `docs/SRS.pdf`.

## Acceder en Windows

1. Abre Docker Desktop.
2. Haz doble clic en **Iniciar.cmd**, o ejecuta `./scripts/iniciar.ps1` desde PowerShell 7.
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

- Login, sesión revocable, MFA TOTP opcional y códigos de recuperación de un solo uso.
- Departamentos, empleados y vehículos: crear, consultar, editar y desactivar.
- Administración de usuarios: crear, modificar perfil/rol, desactivar y restablecer contraseña.
- Auditoría de operaciones y accesos; permisos SQL que impiden alterar sus registros.
- Interfaz adaptable a móvil. Todavía no es una PWA instalable ni registra despachos.

Administrador administra cuentas; Administrador y Supervisor escriben catálogos.
Auditor y Administrador consultan auditoría. Los cinco roles pueden leer catálogos.
No hay registro público. Cambiar permisos, desactivar, cambiar contraseña o activar MFA
invalida sesiones previas. Los tokens se mantienen solo en memoria de la pestaña.
La edición de catálogos exige `If-Match` para detectar cambios concurrentes.

## Requisitos locales

.NET SDK según `backend/global.json`, Node 24.15.0, npm, PowerShell 7 y Docker Desktop.
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

El login local emite JWT; no se presenta como implementación completa de OAuth 2.0.
Integración OAuth/OIDC, cifrado AES-256 en reposo, TLS de producción, tickets/QR,
inventario, despacho, reportes y PWA siguen en el backlog.
La auditoría detecta cambios en su cadena y restringe al usuario SQL de aplicación;
no protege frente a un superusuario que controle la base completa y reconstruya la cadena.
`GET /api/auditoria/verificar` valida la cadena con permisos de Auditor/Administrador.

El vault dentro del repositorio mantiene estado, decisiones delegadas, evidencia y próximos
pasos para continuar en una sesión nueva con “continúa con el programa”.
