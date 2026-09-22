---
tipo: proyecto
estado: activo
actualizado: 2026-09-22
---

# Cómo trabajamos

Reglas de colaboración entre el usuario, Claude y Astra. Si una regla aquí choca con
`AGENTS.md`, gana `AGENTS.md`.

## Montaje del vault en Obsidian

Abrir Obsidian → "Abrir carpeta como vault" → `C:\Dev\intec-combustible\vault`.

El vault vive **dentro** del repositorio, no fuera. Así un solo `git pull` trae a la vez el
código y el contexto que lo explica, y nunca se quedan desincronizados. Es una diferencia
deliberada con `micarrito-vault`, que está separado porque allí el código es de otro
repositorio y el vault se comparte con un colaborador externo.

## El reparto entre Claude y Astra

Los dos pueden escribir código y pruebas. Lo que los diferencia son las herramientas de
inspección, según [[Entorno de agentes]].

| Trabajo | Quién | Por qué |
|---|---|---|
| Backend, dominio, pruebas | cualquiera | Ambos tienen las skills de .NET y PostgreSQL |
| Frontend y PWA | cualquiera | Ambos tienen las skills de React |
| Verificar la PWA en navegador real, Lighthouse | **Claude** | Solo Claude tiene el MCP de chrome-devtools |
| Cualquier cosa de Azure | **Claude** | Solo Claude tiene el MCP de azure |
| Diagramas ER y de secuencia | **Claude** | Solo Claude tiene el MCP de lucid |
| Sesiones largas de implementación sin inspección | **Astra** | Descarga a Claude para lo que solo él puede hacer |

**No trabajen los dos sobre el mismo archivo a la vez.** No hay bloqueo: el segundo en guardar
pisa al primero. Antes de empezar, mira la última entrada de [[Bitacora de cambios]].

## Reglas de git

- **Rama por fase**: `fase-0-entorno`, `fase-1-dominio`, etc. Nunca commits directos a `main`.
- **Un commit por tarea del backlog**, no por sesión. Un commit gigante no se revisa.
- **Mensaje**: qué cambió y por qué, no qué archivos se tocaron — eso lo dice el diff.
- **Nada de commits con el build roto.** Si `dotnet build` o `npm run build` fallan, no se
  comitea.
- **Nunca se comitean secretos.** Credenciales SMTP, claves de la pasarela SMS y claves de
  firma de QR van en variables de entorno y en `.env`, que está en `.gitignore`. Si una clave
  llega a entrar en un commit, se considera comprometida y se rota: borrar el commit no basta.

## Antes de decir "está hecho"

Esta lista no es adorno. Es la diferencia entre un proyecto que funciona y uno que parece que
funciona.

1. ¿Corriste `dotnet test` y pasó? Pega el resultado en la bitácora.
2. ¿Corriste `npm run build` y pasó?
3. ¿Existe una prueba que falle si alguien rompe lo que acabas de hacer? Si no, no está hecho:
   está escrito.
4. ¿Actualizaste [[Trazabilidad de requisitos del SRS]] con dónde vive y con qué prueba?
5. ¿Escribiste la entrada en [[Bitacora de cambios]] con el hash del commit?

**"Debería funcionar" no cuenta.** Si no lo corriste, dilo: "escrito, sin verificar". Es
información útil. Decir que funciona sin haberlo probado no lo es.

## Cuando algo esté ambiguo

El SRS tiene 7 huecos identificados (H-01 a H-07 en [[Trazabilidad de requisitos del SRS]]).
Para esos y para cualquier ambigüedad nueva:

- Si la ambigüedad **no cambia el trabajo**, elige la opción sensata, escríbela como supuesto
  en el ADR y sigue.
- Si **sí lo cambia** —modelo de datos, reglas de negocio, seguridad— **pregunta al usuario**.
  Adivinar el modelo de inventario y descubrir a las tres fases que estaba mal cuesta mucho
  más que una pregunta.

Nunca se rellena un hueco con lo que suene plausible. Este sistema controla inventario de
combustible y registros de auditoría: un supuesto inventado se convierte en un descuadre que
alguien tiene que explicar.

## Qué hacer al retomar en frío

El usuario dirá algo como "continúa con el programa". Eso significa, sin preguntar más:

1. `AGENTS.md`
2. [[Estado actual del proyecto]]
3. [[Tareas pendientes]] → primera tarea desbloqueada de la fase activa
4. Ejecutarla
5. Cerrar con el checklist de arriba
