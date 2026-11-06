# Decisiones de F7–F9

Fuente: el análisis (`docs/analysis/f7-f9-analisis.html`, publicado en
https://claude.ai/artifact/VX683VkeY3zuYC4ZxZdscY). `/dev-flow` lee este archivo: antes de arrancar una fase,
junta las decisiones abiertas (`- [ ]`) de esa fase y las pregunta todas juntas una sola vez. Al cerrarlas, cambia
`[ ]` por `[x]` y anota `Decidido:`. Las ADRs de la fase (T-58, T-69, T-76) las copian.

## F7 · Multitenant

- [x] D0 ¿Cómo se garantiza que ninguna consulta se olvide del tenant? — Decidido (usuario, 2026-11-06): el filtro
  tiene que ser automático, no un `Where` a mano. Como no hay repositorios (ADR 0006: los handlers consultan
  `IAppDbContext`), va en `AppDbContext`: toda entidad del tenant implementa `ITenantOwned` y recibe un filtro
  global de EF Core `TenantId == tenant actual` (sin tenant resuelto no devuelve nada). Al guardar, un interceptor
  pone el `TenantId` y rechaza escribir en otro tenant. Un test de arquitectura falla si una entidad nueva no
  tiene el filtro, y otro si aparece `IgnoreQueryFilters`/`FromSql` fuera de una lista permitida.
- [ ] D1 ¿Un usuario puede pertenecer a varios tenants desde el primer día? — Recomendación: sí; el costo extra es
  chico y evita rehacer el modelo.
- [ ] D2 ¿Cómo se crean los tenants: auto-registro de la agencia o solo el super-admin? — Recomendación: solo el
  super-admin por ahora; el auto-registro puede venir después.
- [ ] D3 ¿El super-admin puede entrar a cualquier tenant para dar soporte? — Recomendación: sí, eligiéndolo de
  forma explícita y quedando registrado en la actividad.
- [ ] D4 ¿Se conserva `Demo:AllowDataWipe`? — Recomendación: sí, con el sentido de "permitir el reset del tenant
  demo", ya que el reset deja de borrar toda la base.

## F8 · Tema

- [ ] D5 ¿Sistema, claro y oscuro, o también alto contraste? — Recomendación: tres valores; alto contraste queda
  para otra fase.
- [ ] D6 El botón de la barra superior, ¿cicla entre los tres o abre un menú? — Recomendación: menú.
- [ ] D7 ¿El tema se aplica también en el login (desde el navegador) o solo con sesión? — Recomendación: también
  en el login, con el valor guardado en el navegador.

## F9 · API keys

- [ ] D8 El "agente" del Gantt, ¿es una etiqueta (`agent:backend-dev`) o un usuario de servicio? — Recomendación:
  etiqueta.
- [ ] D9 "Bloqueado", ¿es una marca sobre la tarea o un quinto estado del Kanban? — Recomendación: marca
  `IsBlocked`.
- [ ] D10 Las notas del Gantt, ¿van en un campo `StatusNote` o como comentarios? — Recomendación: `StatusNote`.
- [ ] D11 ¿Vista Timeline/Gantt dentro de TaskFlow en esta fase? — Recomendación: queda como tarjeta opcional
  (T-83); se hace solo si la pedís.
- [ ] D12 ¿Quién crea keys y el vencimiento es obligatorio? — Recomendación: cualquier miembro, con vencimiento
  por defecto de 90 días.
- [ ] D13 ¿Se publica la referencia de la API (Scalar/OpenAPI) en producción? — Recomendación: sí, sin datos.
