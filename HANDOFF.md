# HANDOFF

Documento de traspaso de contexto para quien retome este repo (otro desarrollador, u otra sesión de Claude Code). Objetivo: que puedas seguir trabajando sin releer todo el historial de commits. Se actualiza al terminar cada historia — no es una bitácora histórica, es una foto del momento.

Para las reglas de arquitectura/negocio vinculantes (que no cambian historia a historia), ver [`CLAUDE.md`](./CLAUDE.md). Este archivo es sobre *estado*, `CLAUDE.md` es sobre *reglas*.

## Última actualización

**2026-09-07** — **cambio de foco: este repo pasa a ser Track C · Agentes de IA (Desarrollador 3).** Track A queda construido pero ya no es frente de trabajo; sus huecos conocidos se listan abajo como deuda, no como pendientes activos.

`ORB-C01` (abstracción de proveedor de modelos) está terminada en la rama `feature/llm-provider-abstraction`, sin mergear todavía. Suite en verde: 184 unitarias (175 previas + 9 nuevas) y 25 pruebas de los adaptadores de IA, que no necesitan Docker. Las 62 de integración con Postgres no se corrieron en esta sesión porque Docker no estaba levantado.

## Qué está implementado

Ver la sección "Qué hay implementado hoy" en [`README.md`](./README.md) — se mantiene sincronizada ahí, no se duplica aquí.

Track C (Agentes de IA — **foco actual**):

- [x] `ORB-C01` Abstracción de proveedor de modelos
- [ ] `ORB-C02` Base de conocimiento — **es lo siguiente**
- [ ] `ORB-C03` Búsqueda semántica
- [ ] `ORB-C04`…`ORB-C09` Runtime del agente — bloqueadas por otros tracks (ver abajo)
- [ ] `ORB-C10`/`ORB-C11` Constructor de agentes y banco de pruebas — el frontend ya los está planeando en `orbita`

Track A (Plataforma, Identidad y Facturación — construido, sin trabajo activo):

- [x] `ORB-A05` Registro de organización
- [x] `ORB-A09` Aislamiento entre organizaciones
- [x] `ORB-A06` Inicio y cierre de sesión
- [x] `ORB-A07` Invitar miembros al equipo
- [x] `ORB-A08` Roles y permisos
- [x] `ORB-A10` Recuperación de contraseña
- [x] `ORB-A11` Verificación en dos pasos — política de MFA de tenant guardada, **no aplicada en runtime** (ver abajo)
- [x] `ORB-A12` Planes y suscripción — **código completo, sin credenciales reales conectadas** (ver abajo)
- [x] `ORB-A15` Bitácora de auditoría — solo cambios de rol/remoción de miembros auditados por ahora
- [ ] `ORB-A13` Medición de consumo — necesita que exista Track C (agentes de IA) primero
- [ ] `ORB-A14` Límites del plan — depende de A12 (listo) y A13 (no)

### El muro de dependencias de Track C

Solo la Épica C1 (`C01` → `C02` → `C03`) es ejecutable sin nadie más — el backlog la marca como "Fase 0 · sin esperar a nadie". Lo demás está bloqueado por tracks que no existen:

| Historia | Bloqueada por | Qué falta |
|---|---|---|
| `ORB-C04` El agente responde | `ORB-B03` (Track B) | No hay `conversations` ni `messages` |
| `ORB-C05` El agente ejecuta acciones | `ORB-D05` (Track D) | No hay pipeline ni `deals` |
| `ORB-C07` Traspaso a humano | `ORB-B15` (Track B) | No hay asignación de conversaciones |
| `ORB-C09` Registro de consumo de IA | `ORB-A13` | Medición de consumo, que a su vez espera a Track C |

Por eso el orden real de trabajo es `C02` → `C03`, y después reevaluar.

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
- **`orbita-front` ya no está en scaffold** — la rama `feature/d01-dashboard-shell` (sin mergear) trae cliente HTTP con cookie auth, sistema de diseño, pantallas de registro/login y el shell autenticado. Su sesión de Claude está planeando `ORB-C10`/`ORB-C11`.
- **Deuda de contrato con el frontend, detectada y no resuelta** (queda fuera de Track C, pero está verificada y hay que arreglarla antes de que el front se conecte de verdad):
  - `app.UseHttpsRedirection()` está fuera del bloque `IsDevelopment()` en `Program.cs`, así que con el perfil `https` activo `http://localhost:5091` responde 307 y le rompe al front la base URL y el preflight de CORS.
  - Los correos generan `/reset-password?token=` y `/accept-invite?token=` (`LoggingPasswordResetEmailSender.cs`, `LoggingInvitationEmailSender.cs`), pero el front tiene rutas en español (`/recuperar`) y no tiene `/accept-invite`.
  - `ProblemDetails` solo lleva `title` en inglés, y el front mapea su copy en español contra ese string — renombrar un título lo degrada a mensaje genérico en silencio. Falta una extensión `code` estable.
  - No hay endpoint de "mis organizaciones" ni rol del usuario en `GET /api/auth/me` (solo devuelve `userId`). Es el bloqueo nº 1 declarado por el frontend.
  - `POST /api/tenants` sigue sin `[Authorize]`.
- **`dotnet format` no corre en este entorno**: el build host de la herramienta pide el runtime `10.0.11` y no lo encuentra en `C:\Users\juanr\.dotnet\`. Es un problema del SDK instalado, no del código.

### Sobre los proveedores de IA (`ORB-C01`)

Ninguno cuesta dinero hoy. Ollama corre local en `localhost:11434` (hay que tener `ollama pull llama3.1` y `ollama pull nomic-embed-text`), y el adaptador OpenAI-compatible viene con `BaseUrl` vacío, así que se reporta como no configurado y la capa de resiliencia lo salta — mismo patrón que Stripe/Wompi sin credenciales. Para apuntarlo a un proveedor de pago basta con llenar `Ai:Providers:OpenAiCompatible:BaseUrl`, `ApiKey` y **los precios por millón de tokens**; sin esos precios el costo se registra como 0 y `ai_runs` va a subestimar el gasto.
