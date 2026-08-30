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
cp .env.example .env          # y rellena los valores
docker compose up -d          # PostgreSQL en :5432, Adminer en :8080

cd backend && dotnet run --project src/Combustible.Api
cd frontend && npm install && npm run dev
```

### Verificar

```bash
cd backend  && dotnet test    # pruebas del backend
cd frontend && npm run build  # compilación del frontend
```

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
