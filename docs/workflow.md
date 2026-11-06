# Workflow multiagente de TaskFlow

Objetivo: trabajar varias tareas en paralelo gastando pocos tokens. El hilo principal queda liviano porque los
subagentes trabajan en contexto aislado y devuelven solo un resumen. El estado de las tareas vive en el
**Panel Gantt** (artifact), que es la única fuente de verdad entre lotes.

Agentes disponibles: `.claude/agents/` (`backend-dev`, `frontend-dev`, `db-migrator`, `test-runner`,
`code-reviewer`). La tabla de qué agente usar para cada tarea está en `CLAUDE.md`.

## Atajo: `/dev-flow`

`/dev-flow F7 F8` ejecuta esas fases completas (agentes, tests, review, fixes, commit, PR y Gantt) sin pedir
confirmación entre pasos; solo pregunta al principio por las decisiones abiertas de la fase
(`docs/analysis/decisiones-*.md`) y ante un bloqueo real. `/dev-flow` sin argumentos lista las fases listas,
qué espera a qué y qué pares pueden ir en paralelo (cada fase en su worktree), y te deja elegir. Las secciones
de abajo describen el mismo flujo hecho a mano.

## a) Definir el lote y cargarlo en el Gantt

1. Escribí 2 a 6 tareas con id (`T-07`), título, agente, fechas y dependencias.
2. Cargalas en el Panel Gantt (botón "Nueva tarea", o pegando un JSON en "Importar / exportar").
3. Una tarea entra al lote solo si sus dependencias están en "hecho" o dentro del mismo lote en serie.
4. Criterio de paralelo: dos tareas pueden correr a la vez si tocan carpetas distintas. `backend-dev` +
   `frontend-dev` sí. `backend-dev` + `db-migrator` no (las dos tocan `backend/`). `test-runner` y
   `code-reviewer` van después de que terminen los que editan.

## b) Despachar

Un solo mensaje con todos los encargos paralelos, para que salgan juntos. Para elegir el agente, escribí `@` y
elegilo del autocompletado (aparece como `@agent-backend-dev`, etc.), o nombralo en texto ("usá el subagente
backend-dev"). Cada encargo lleva:

- id de la tarea y objetivo en 1 o 2 líneas;
- archivos o carpetas de partida, si los sabés (le ahorra búsquedas al agente);
- criterio de terminado (qué test o comportamiento lo prueba).

Ejemplo:

```
Despachá en paralelo, en un solo paso, y no leas código vos:

@agent-backend-dev T-01: agregar GET /api/projects/{projectId}/activity que devuelva las últimas 50
actividades del proyecto (paginado por page/pageSize). Empezá por Application/Projects y
Api/Controllers/ProjectsController. Terminado = test de integración nuevo en ProjectActivityTests pasando.

@agent-frontend-dev T-03: componente ActivityFeed en features/projects/ui que muestre una lista de
actividades (entrada: signal de items). Textos en en.json y es.json. Sin conectar a la API todavía.
Terminado = spec del componente pasando y lint ok.

Cuando terminen, devolveme solo los dos bloques RESULTADO y un array JSON con las líneas Gantt.
```

Si dos tareas de la misma capa tienen que ir en paralelo, pedí que cada agente trabaje en un worktree aislado
("corré cada uno en su propio worktree") y después integrás las ramas.

## c) Recibir resúmenes y actualizar el Gantt

**Automático (0 tokens):** abrí `docs/panel-gantt.html` desde el disco. Los hooks de `.claude/settings.json`
(`.claude/hooks/gantt-sync.js`) escriben en `docs/gantt-updates.js` y el panel lo relee cada 5 s:

- al despachar un agente con un encargo que menciona `T-xx`, la tarea pasa a "en progreso";
- al terminar, se aplica su línea `Gantt: {...}`; si no la devolvió, la tarea queda "bloqueado" para revisar.

**Sincronizar con TaskFlow (opcional, 0 tokens):** el mismo hook puede mandar cada actualización a un proyecto de
TaskFlow con `POST /api/projects/{id}/tasks/sync` (ADR 0016 y 0017). Se activa solo si las tres variables están
definidas en el entorno donde corre Claude Code:

```bash
TASKFLOW_URL=https://api.tu-taskflow.example     # sin barra final
TASKFLOW_API_KEY=tf_...                          # una API key con scope Write (Ajustes > API keys), nunca se imprime
TASKFLOW_PROJECT=<id del proyecto>               # el proyecto donde viven las tarjetas
```

- La key se crea con una cuenta que sea miembro del proyecto; no puede crear proyectos, así que el proyecto ya tiene
  que existir.
- Mapeo: `T-xx` → `externalKey`; estado → columna (`pendiente` ToDo, `en_progreso` InProgress, `hecho` Done);
  `bloqueado` → `isBlocked` (la tarjeta conserva su columna); `notes` → `statusNote`; `start`/`end` →
  `startDate`/`dueDate`; `deps` → `dependsOn`. El agente (D8) va como etiqueta
  `agent:<agente>` (`labels`); la descripción solo lleva el título completo cuando pasa de 200 caracteres. `labels`
  reemplaza las etiquetas de la tarea y crea las que falten solo si quien llama es un propietario del proyecto con
  sesión de persona: una API key nunca crea etiquetas (ADR 0016), así que antes de la primera sincronización con la key
  hay que crear las etiquetas `agent:*` en el proyecto (o correr el import una vez con una sesión de propietario). Si
  falta una, el ítem falla con `Labels.CreateNotAllowed` y el lote no se aplica.
- La API reemplaza todos los campos de la tarea, así que el hook completa la actualización con la tarea del SEED de
  `docs/panel-gantt.html`; una tarea que no está en el SEED (ni trae `title`) no se manda.
- Si faltan variables o la API falla, nada cambia: el hook sigue escribiendo `docs/gantt-updates.js` y el mensaje del
  hook dice si TaskFlow respondió.
- Carga inicial (una vez, repetible sin duplicar): `node .claude/skills/dev-flow/gantt-import.js` manda todo el SEED
  en lotes de hasta 200. Con `--dry-run` imprime los payloads sin mandar nada y sin necesitar las variables.
- Probar el hook sin escribir ni mandar: `echo '{"last_assistant_message":"Gantt: {\"id\":\"T-85\",\"status\":\"hecho\"}"}' | node .claude/hooks/gantt-sync.js stop --dry-run`.
  Tests: `node --test .claude/hooks/taskflow-sync.test.js`.

El link publicado en claude.ai no se sincroniza solo; el resto de esta sección es el camino manual.

Cada agente termina con un bloque corto y una línea `Gantt: {"id":"T-01","status":"hecho","notes":"..."}`.
Los agentes **no** tocan el Gantt. Vos (o Claude, si se lo pedís) juntás esas líneas en un array y lo pegás en
el Panel Gantt → "Importar / exportar" → **Fusionar por id**. Solo cambian los campos que vienen en el JSON.

Para que Claude reorganice el plan: exportá el JSON completo del panel, pegalo en el chat ("actualizá este
estado: T-02 pasa a bloqueado, agregá T-08 después de T-04") y pegá de vuelta la respuesta con **Reemplazar
todo**.

Después de los agentes que editan: `@agent-test-runner` para el lote y `@agent-code-reviewer` sobre el diff.
Si el reviewer pide cambios, abrí una tarea nueva en el Gantt en vez de seguir la conversación larga.

## d) Cerrar el lote

1. Commit (vos, o pedíselo explícitamente a Claude).
2. Exportá el JSON del Gantt si querés un respaldo (el panel guarda en el `localStorage` de tu navegador, que no
   se sincroniza entre equipos).
3. `/clear`. El lote siguiente arranca desde el Gantt, no desde el historial del chat.
4. Si tenés que seguir en la misma sesión, usá `/compact` con foco:
   `/compact conservar solo archivos tocados, decisiones y blockers del lote actual`.

## e) Cuándo escalar a Dynamic Workflows

Los workflows dinámicos (orquestación con muchos agentes en abanico) consumen **muchos** tokens: cada agente
arranca en frío y relee contexto. Usalos solo si se cumplen las dos condiciones:

- es una auditoría o barrido grande que toca la mayoría del repo (seguridad completa, migración de una
  convención en todas las features, revisión de todas las ADRs contra el código);
- el trabajo no se puede partir en 3 a 6 tareas del Gantt con este setup.

Para lo demás (features, bugs, una migración, una review de diff) alcanza el flujo de arriba. Antes de
escalar, acotá el alcance por carpeta y fijá un tope de agentes.

TODO: confirmar cómo se invoca Dynamic Workflows en tu versión de Claude Code y anotar el comando acá.

## Reglas de costo

- Modelos: `haiku` para tareas mecánicas (tests, review de diff), `sonnet` para lógica. Nunca `opus` salvo
  pedido explícito.
- Un encargo, un agente, una tarea. Un agente con tres tareas pierde las ventajas del contexto aislado.
- El hilo principal no relee archivos que un agente ya leyó; confía en el resumen y pide detalle solo si un
  blocker lo exige.
- Comandos con salida filtrada (`tail`, `grep`). Preferir CLI (`gh`, `git`, `dotnet`, `npm`) antes que MCP.
