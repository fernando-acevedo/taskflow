---
name: dev-flow
description: Ejecuta fases del roadmap de TaskFlow de punta a punta (agentes → tests → review → fixes → commit → PR → Gantt) sin pedir confirmación entre pasos. "/dev-flow F7 F8" corre esas fases; "/dev-flow" solo sugiere qué fases se pueden hacer y cuáles en paralelo, y espera que elijas.
argument-hint: "[F7 F8 ...]"
disable-model-invocation: true
---

# /dev-flow

Fases pedidas: `$ARGUMENTS`

El estado de las tareas sale del SEED de `docs/panel-gantt.html` (las tarjetas `T-xx`). La fase de una tarjeta
es el prefijo de su título: `F7 ...` es la feature F7, `Fase 5 ...` es la fase 5 del roadmap (se pide `Fase5`).
Helper: `node .claude/skills/dev-flow/gantt.js phases | tasks F7 F8 | next-id | set '<json>' | bump`.

## Regla de autonomía (lo más importante)

El usuario elige las fases y después no quiere que le pregunten nada. **No preguntes "¿sigo?", "¿hago el
commit?", "¿abro el PR?" ni avises cada vez que termina un agente.** Encadená los pasos en silencio.

Se le pregunta al usuario solo en estos casos, y siempre con `AskUserQuestion` (opción recomendada primero):
1. `/dev-flow` sin argumentos: para que elija las fases (ver abajo).
2. Decisiones abiertas de las fases elegidas (`- [ ]` en `docs/analysis/decisiones-*.md`): **una sola ronda, al
   principio, antes de despachar nada**. La primera pregunta es "¿Uso las recomendaciones para todas?"; si dice
   que no, preguntá las restantes de a 4 por llamada. Anotá las respuestas en el archivo (`[x]` + `Decidido:`).
3. Bloqueos reales: un permiso denegado (no lo esquives), algo que solo puede hacer el usuario (Azure, secretos,
   merge), una acción destructiva o irreversible fuera del repo, tests que siguen fallando después de dos
   intentos de arreglo, o una duda de producto que cambia el modelo de datos y no tiene recomendación escrita.
   Ante un bloqueo de una fase, seguí con las otras fases elegidas que no dependan de ella y preguntá al final.

Todo lo demás lo decidís vos siguiendo `CLAUDE.md`, las ADRs y el código vecino, y lo anotás en la sección
"Supuestos" del PR.

## Sin argumentos: sugerir

1. `node .claude/skills/dev-flow/gantt.js phases`.
2. Mostrá en 4 a 8 líneas: fases listas, fases que esperan a otra (y a cuál), tareas manuales tuyas (agente `yo`:
   merges, deploy) y los pares que pueden ir en paralelo.
3. `AskUserQuestion` con `multiSelect: true` y una opción por fase lista (como máximo 4; si hay más, las que
   desbloquean más trabajo). Con la respuesta, seguí como si la hubiera escrito en `$ARGUMENTS`.

## Con argumentos: ejecutar

### 1. Preparar (sin preguntar, salvo las decisiones)
- `gantt.js phases` y `gantt.js tasks <fases>`. Si una fase espera a otra que no está en la lista ni hecha,
  avisá en una línea y sacala. Si espera a una que sí está en la lista, va después de esa, en la misma rama
  apilada.
- Ronda única de decisiones abiertas (regla 2).
- Ramas: una por fase, `feature/<fase>-<slug>` (ej. `feature/f8-theme`). La base es la rama del último PR sin
  mergear (ramas apiladas: `gh pr list --state open`); si no hay, `main`. El PR apunta a esa base.

### 2. Paralelo entre fases
Dos fases van en paralelo solo si `gantt.js phases` las lista como par posible. Cada una trabaja en su propio
worktree, para que sus agentes no se pisen:
`git worktree add .worktrees/<fase> -b feature/<fase>-<slug> <base>` (dentro del repo para que los agentes tengan permiso; `.worktrees/` va en `.git/info/exclude`; `npm ci` en su `frontend/`). En cada encargo decí la ruta absoluta del
worktree y que trabaje solo ahí. Las migraciones de dos fases nunca corren a la vez (las dos tocan el snapshot):
la de la segunda fase se genera después de mergear la primera a su rama, o se regenera (`db-migrator`) antes de
su commit. Al cerrar, `git worktree remove`.

### 3. Correr cada fase (orden por dependencias de las tarjetas)
Despachá las tarjetas en el orden de `gantt.js tasks`: en un mismo mensaje todas las que estén listas y toquen
carpetas distintas (`backend/` ∥ `frontend/`); la misma capa va en serie. El patrón de siempre es
backend → migración ∥ frontend → tests ∥ review → fixes.
- Agentes y modelos según la tabla de `CLAUDE.md`; nunca opus. Si `backend-dev` o `frontend-dev` no cargan, usá
  `general-purpose` con `model: sonnet` y decile que siga `.claude/agents/<nombre>.md`.
- Cada encargo: id `T-xx`, objetivo, archivos de partida, criterio de terminado, las decisiones cerradas que
  aplican, y la frase "No preguntes nada: decidí con las convenciones y anotá supuestos".
- Tarjeta de ADRs (T-58, T-69, T-76…): la escribe `backend-dev` con las decisiones cerradas.
- Review: arreglá en la misma fase los hallazgos altos y medios (tarjeta de fixes de la fase); los bajos van al
  PR como "Pendientes menores". Si el review deja trabajo que no entra en la tarjeta de fixes, creá una tarjeta
  nueva con `gantt.js next-id` (nunca reuses ids) y título, agente, fechas y deps completos.
- Una tarjeta `bloqueado`: un reintento con el blocker explicado al agente; si vuelve bloqueada, es un bloqueo
  real (regla 3).
- Después de cada agente: `gantt.js set` con su línea `Gantt: {...}`.

### 4. Cerrar cada fase (sin preguntar)
1. Tests completos: backend `dotnet test` y frontend `npm run lint`, `npm test -- --watch=false`, `npm run
   build`. Si algo falla, un agente lo arregla.
2. Commit en inglés, en el estilo de `git log`, terminando con la línea `Co-Authored-By` que pida el sistema.
3. `git push -u origin <rama>` y `gh pr create --base <base> --body-file <archivo>`. Escribí el cuerpo con Write
   en el scratchpad y corré `gh` con `</dev/null`. Secciones del cuerpo: Summary, Supuestos, Pendientes menores,
   Tests y la línea de Claude Code al final.
4. Esperá el check del PR (`gh run watch <id> --exit-status` en segundo plano). Si falla, arreglalo y volvé a
   subir; no cierres la fase con el check en rojo.
5. Gantt: `gantt.js set` con las tarjetas en `hecho` (notas con el número del PR), después `gantt.js bump` y
   republicá `docs/panel-gantt.html` en el mismo artifact (https://claude.ai/artifact/Xtg1ij9fngi8hCGN8tMeP8). Hacé
   un commit "Gantt panel: <fase> done".
6. Actualizá la memoria `roadmap-progress.md` (PRs, siguiente id libre).

### 5. Reporte (único mensaje al usuario por fase)
Cuando una fase queda con el PR en verde: un mensaje corto con el link del PR, el total de tests, los supuestos
que valga la pena revisar y lo que queda manual. Después seguí con la siguiente fase pedida sin esperar
respuesta. Al terminar todas, una línea con lo que sigue (`/dev-flow` para ver qué queda).

## Límites que no cambian
- No deployar ni correr nada contra Azure; no mergear PRs (es tarea del usuario: T-49).
- El reset de la demo nunca apunta a la base `TaskFlow` de desarrollo.
- Sin secretos en el repo.
