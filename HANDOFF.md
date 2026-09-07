# HANDOFF

Documento de traspaso de contexto para quien retome este repo (otro desarrollador, u otra sesión de Claude Code). Objetivo: que puedas seguir trabajando sin releer todo el historial de commits. Se actualiza al terminar cada historia — no es una bitácora histórica, es una foto del momento.

Para las reglas de arquitectura/negocio vinculantes (que no cambian historia a historia), ver [`CLAUDE.md`](./CLAUDE.md). Este archivo es sobre *estado*, `CLAUDE.md` es sobre *reglas*.

## Última actualización

**2026-09-07** — tras terminar `ORB-A15` (bitácora de auditoría), en la rama `feature/audit-log` (sin mergear a `develop` todavía). `ORB-A08` y `ORB-A10` ya están en `develop`; `ORB-A11` (verificación en dos pasos) y `ORB-A12` (planes y suscripción) están listas cada una en su propia rama, también sin mergear.

## Qué está implementado

Ver la sección "Qué hay implementado hoy" en [`README.md`](./README.md) — se mantiene sincronizada ahí, no se duplica aquí.

Track A (Plataforma, Identidad y Facturación — dueño de este repo):

- [x] `ORB-A05` Registro de organización
- [x] `ORB-A09` Aislamiento entre organizaciones
- [x] `ORB-A06` Inicio y cierre de sesión
- [x] `ORB-A07` Invitar miembros al equipo
- [x] `ORB-A08` Roles y permisos
- [x] `ORB-A10` Recuperación de contraseña
- [x] `ORB-A11` Verificación en dos pasos — en `feature/two-factor-auth`, sin mergear
- [x] `ORB-A12` Planes y suscripción — en `feature/subscription-billing`, sin mergear; **código completo, sin credenciales reales conectadas**
- [x] `ORB-A15` Bitácora de auditoría — en `feature/audit-log`, sin mergear; solo cambios de rol/remoción de miembros auditados por ahora
- [ ] `ORB-A13` Medición de consumo — necesita que exista Track C (agentes de IA) primero
- [ ] `ORB-A14` Límites del plan — depende de A12 (listo) y A13 (no)

Con esto, el track de Desarrollador 1 queda sin historias P1 pendientes que no dependan de otro track. Lo que sigue (`ORB-A13`/`ORB-A14`) necesita que Track C construya los agentes de IA primero, o (P2, `ORB-A11` ya cubierto) no hay más trabajo aislado obvio — la próxima sesión debería confirmar con el resto del equipo antes de inventar alcance nuevo aquí.

## Decisiones que ya se tomaron (no reabrir sin motivo)

Estas están documentadas con más detalle en `CLAUDE.md`, se listan aquí para que salten a la vista antes de tocar el área relacionada:

- El JWT de acceso viaja en cookie `httpOnly`/`SameSite=Lax`, no en header `Authorization` — para que funcione igual con el handshake de SignalR más adelante.
- El JWT solo lleva `sub` (id de usuario), nunca un claim de tenant. Cuál organización actúa una sesión se resuelve explícitamente en cada endpoint tenant-scoped, no se infiere del token.
- Las invitaciones toman el tenant de la ruta (`/api/tenants/{tenantId}/...`), no del JWT, para no necesitar nunca un "listar mis membresías cruzando tenants" que pelearía con Row-Level Security.
- Los permisos por rol viven en `RolePermissions` (`Owner`/`Admin`/`Agent`/`Viewer`), consultados a través de `ITenantAuthorizationService` — cualquier chequeo de rol nuevo pasa por ahí, no se repite inline.
- **Row-Level Security bloquea toda lectura cuando no hay un tenant activo en la sesión** (cero filas, no "todas"). `InvitationToken`, `PasswordResetToken`, `RefreshToken` y `Subscription` se diseñan a propósito **sin** RLS/query filter por esto — se buscan por un id opaco externo antes de saber a qué tenant pertenecen. `audit_log` sí lleva RLS normal (siempre se lee con el tenant ya conocido), pero cualquier lectura después de que `EnsurePermissionAsync` ya cerró su propia transacción necesita su propio `QueryInTenantScopeAsync` — `SET LOCAL app.tenant_id` no sobrevive a la transacción que lo puso.
- **La política "exigir MFA a todo el equipo" (`Tenant.RequireMfaForMembers`) existe pero no se aplica todavía.** Aplicarla de verdad requiere saber, antes de emitir tokens, a qué tenant(s) pertenece quien inicia sesión — y toda lectura cruzando tenants está bloqueada a propósito por la RLS de `memberships` (devuelve cero filas sin un tenant en la sesión). Es la misma decisión pendiente que el JWT sin claim de tenant. No intentar resolverlo con un bypass de RLS.
- `AuditLogEntry` es la primera entidad con id `bigint` en vez de `Guid` (así lo pide orbita-schema.dbml) — no hereda de `Entity`. Es además la única tabla verdaderamente append-only: `orbita_app` tiene `UPDATE`/`DELETE` revocados sobre ella específicamente, a nivel de base de datos.
- El envío de correo real está pendiente en toda la plataforma (no hay proveedor conectado); se loguea el link en su lugar. Cuando se construya `Notifications`, ese es el reemplazo, no un parche aquí.
- El secreto TOTP se cifra con la Data Protection API de ASP.NET Core (`IUserSecretProtector`), no con un KMS real — es un reemplazo temporal, igual que el envío de correo por log. Su key ring local no sirve para producción multi-instancia.

## Cómo retomar el trabajo

1. Leer la tabla de arriba para saber qué falta.
2. `git fetch && git log --oneline develop..origin/develop` para confirmar que no hay nada mergeado que no se haya bajado.
3. Cada historia nueva es su propia rama `feature/<nombre>` desde `develop` (ver la convención de branching en `CLAUDE.md`). No continuar historias distintas en la misma rama.
4. El patrón de implementación (Domain → Application → Infrastructure → Api → Tests → Docs, cada capa su propio commit) está ya establecido en los commits de `ORB-A05` en adelante — seguirlo en vez de inventar uno nuevo.
5. Antes de dar una historia por terminada: `dotnet build Orbita.slnx` sin errores, `dotnet test Orbita.slnx` en verde (requiere Docker corriendo para los tests de integración), y actualizar tanto `README.md` ("Qué hay implementado hoy") como este archivo.

## Riesgos y deuda conocida

- **Sin envío real de correo** — bloquea probar invitaciones/reset de contraseña/2FA con un buzón real, no solo con el log.
- **`ORB-A12`: ni Stripe ni Wompi tienen credenciales reales conectadas**, y Wompi todavía no tiene forma de cobrar de manera recurrente (no existe el scheduler). Ver la sección "Billing" de `CLAUDE.md`.
- **`ORB-A11`: política de MFA de tenant sin aplicar** — el flag existe y se puede configurar, pero ningún login lo respeta todavía (misma causa raíz que el JWT sin claim de tenant).
- **`ORB-A15`: la bitácora solo cubre cambios de rol y remoción de miembros por ahora** — es el patrón de referencia, no una cobertura exhaustiva. Engancharla a más acciones sensibles (facturación, configuración de tenant, etc.) es trabajo incremental de una línea por caso, no una historia nueva.
- **`orbita-front` sigue en scaffold** — no hay cliente HTTP ni pantallas reales todavía, así que ningún endpoint de este repo tiene todavía un consumidor real más allá de las pruebas de integración.
