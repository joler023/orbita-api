# HANDOFF

Documento de traspaso de contexto para quien retome este repo (otro desarrollador, u otra sesión de Claude Code). Objetivo: que puedas seguir trabajando sin releer todo el historial de commits. Se actualiza al terminar cada historia — no es una bitácora histórica, es una foto del momento.

Para las reglas de arquitectura/negocio vinculantes (que no cambian historia a historia), ver [`CLAUDE.md`](./CLAUDE.md). Este archivo es sobre *estado*, `CLAUDE.md` es sobre *reglas*.

## Última actualización

**2026-09-07** — tras terminar `ORB-A12` (planes y suscripción), en la rama `feature/subscription-billing` (sin mergear a `develop` todavía). `ORB-A08` y `ORB-A10` ya están en `develop`; `ORB-A11` (verificación en dos pasos) está lista en `feature/two-factor-auth`, también sin mergear.

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
- [x] `ORB-A12` Planes y suscripción — **código completo, sin credenciales reales conectadas** (ver "Riesgos y deuda conocida")
- [ ] `ORB-A13` Medición de consumo — necesita que exista Track C (agentes de IA) primero
- [ ] `ORB-A14` Límites del plan — depende de A12 (listo) y A13 (no)
- [ ] `ORB-A15` Bitácora de auditoría — sin bloqueos, siguiente candidata natural

## Decisiones que ya se tomaron (no reabrir sin motivo)

Estas están documentadas con más detalle en `CLAUDE.md`, se listan aquí para que salten a la vista antes de tocar el área relacionada:

- El JWT de acceso viaja en cookie `httpOnly`/`SameSite=Lax`, no en header `Authorization`. Solo lleva `sub` (id de usuario), nunca un claim de tenant — cuál organización actúa una sesión se resuelve explícitamente en cada endpoint tenant-scoped, no se infiere del token. Esta decisión pendiente es la razón por la que la política "exigir MFA a todo el equipo" (`ORB-A11`) y cualquier lectura cruzando tenants siguen bloqueadas — ver el punto de RLS abajo.
- Los permisos por rol viven en `RolePermissions` (`Owner`/`Admin`/`Agent`/`Viewer`), consultados a través de `ITenantAuthorizationService` — cualquier chequeo de rol nuevo pasa por ahí, no se repite inline. `ManageBilling` es Owner-only (a diferencia de `ManageTeam`/`ManageSettings`, que también dan Admin).
- **Row-Level Security bloquea toda lectura cuando no hay un tenant activo en la sesión** (devuelve cero filas, no "todas"). Por eso `InvitationToken`, `PasswordResetToken`, `RefreshToken` y ahora `Subscription` (ORB-A12) se diseñan a propósito **sin** RLS/query filter — se buscan por un id opaco externo antes de saber a qué tenant pertenecen, y el chequeo de tenant se hace explícito en el código de aplicación en su lugar (defensa en profundidad).
- `Subscription` elige el proveedor de pago automáticamente por `Tenant.CountryCode` (`CO` → Wompi, el resto → Stripe) — no hay ni debe haber un endpoint para elegir proveedor a mano.
- El envío de correo real está pendiente en toda la plataforma (no hay proveedor conectado); se loguea el link en su lugar. Cuando se construya `Notifications`, ese es el reemplazo, no un parche aquí.

## Cómo retomar el trabajo

1. Leer la tabla de arriba para saber qué falta.
2. `git fetch && git log --oneline develop..origin/develop` para confirmar que no hay nada mergeado que no se haya bajado.
3. Cada historia nueva es su propia rama `feature/<nombre>` desde `develop` (ver la convención de branching en `CLAUDE.md`). No continuar historias distintas en la misma rama.
4. El patrón de implementación (Domain → Application → Infrastructure → Api → Tests → Docs, cada capa su propio commit) está ya establecido en los commits de `ORB-A05` en adelante — seguirlo en vez de inventar uno nuevo.
5. Antes de dar una historia por terminada: `dotnet build Orbita.slnx` sin errores, `dotnet test Orbita.slnx` en verde (requiere Docker corriendo para los tests de integración), y actualizar tanto `README.md` ("Qué hay implementado hoy") como este archivo.

## Riesgos y deuda conocida

- **Sin envío real de correo** — bloquea probar invitaciones/reset de contraseña/2FA con un buzón real, no solo con el log.
- **`ORB-A12`: ni Stripe ni Wompi tienen credenciales reales conectadas.** El código está completo y probado (con un `IPaymentProvider` falso en los tests de integración), pero conectar una cuenta real requiere: crear los Price de Stripe por plan (`Plan.SetStripePriceId`), registrar los webhooks en ambos dashboards, y llenar `Billing:Stripe:*`/`Billing:Wompi:*` en `appsettings`. Ver la sección "Billing" de `CLAUDE.md` para el detalle completo.
- **`ORB-A12`: Wompi no tiene forma de cobrar de manera recurrente todavía.** Wompi no tiene objeto de suscripción — hace falta un scheduler propio que cree una transacción por cada tenant en Wompi por ciclo de facturación, y ese scheduler no existe. No conectar una cuenta real de Wompi en producción hasta construirlo.
- **`ORB-A11`: política de MFA de tenant sin aplicar** — el flag `Tenant.RequireMfaForMembers` existe y se puede configurar, pero ningún login lo respeta todavía (misma causa raíz que el JWT sin claim de tenant).
- **`orbita-front` sigue en scaffold** — no hay cliente HTTP ni pantallas reales todavía, así que ningún endpoint de este repo tiene todavía un consumidor real más allá de las pruebas de integración.
