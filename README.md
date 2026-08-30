# Plataforma de Tickets Digitales de Combustible — INTEC

Plataforma web + PWA de despacho para el control del inventario y despacho de combustible de
INTEC, mediante tickets digitales con QR firmado criptográficamente y trazabilidad completa.

Documento fuente: `docs/SRS.pdf` — 24 requisitos funcionales y 6 de seguridad.

## Para agentes

**Lee `AGENTS.md`.** Es el punto de entrada. No empieces por este README.

## Para personas

### Qué necesitas

| Herramienta | Versión | Estado |
|---|---|---|
| .NET SDK | 10.0.401 LTS | fijado en `backend/global.json` |
| Node.js | 24.x | |
| Docker Desktop | 29.x | **debe estar corriendo** antes de `docker compose` |

### Arrancar

```bash
cp .env.example .env          # solo si .env no existe; rellena POSTGRES_PASSWORD
docker compose config --quiet # valida sin mostrar credenciales
docker compose up -d          # solo PostgreSQL, en 127.0.0.1:5432
docker compose ps            # comprobar estado y salud

cd backend && dotnet run --project src/Combustible.Api
cd frontend && npm install && npm run dev
```

`POSTGRES_USER`, `POSTGRES_PASSWORD` y `POSTGRES_DB` son obligatorias; Compose
rechaza valores vacíos. `POSTGRES_PORT` es opcional (5432). No compartas ni comitees `.env`.
Adminer solo arranca con `docker compose --profile herramientas up -d`, en
`127.0.0.1:8080`. Las variables QR/JWT/SMTP/SMS son contratos previstos:
el esqueleto actual todavía no las consume ni conecta la API a PostgreSQL.

### Verificar

```bash
cd backend  && dotnet test    # pruebas del backend
cd frontend && npm run build  # compilación del frontend
```

La CI está definida en `.github/workflows/ci.yml`: build y pruebas de .NET en
Release, y `npm ci` + build del frontend. Se ejecuta en pushes y pull requests;
también permite arranque manual. Su ejecución real requiere un remoto en GitHub.

## Estructura

```
AGENTS.md            contrato para agentes — el punto de entrada
vault/               vault de Obsidian: contexto, decisiones, changelog y backlog
docs/SRS.pdf         el documento de requisitos
backend/             API en .NET 10 (Domain → Application → Infrastructure → Api)
frontend/            React + TypeScript + Vite
docker-compose.yml   PostgreSQL 17 + Adminer
```

El vault vive **dentro** del repositorio a propósito: un solo `git pull` trae el código y el
contexto que lo explica, y así nunca se desincronizan. Ábrelo en Obsidian con "Abrir carpeta
como vault" apuntando a `vault/`.

## Estado

Fase 0, montaje del entorno. Sin funcionalidad de producto todavía. El estado real está en
`vault/Estado actual del proyecto.md` y el backlog en `vault/Tareas pendientes.md`.
