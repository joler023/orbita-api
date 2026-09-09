# Órbita — API

Backend de **Órbita**: un CRM conversacional multi-tenant con agentes de IA sobre WhatsApp e Instagram (TikTok en fase 1), construido en ASP.NET Core.

## Qué es Órbita y por qué existe

En LATAM la venta de una PYME ocurre por WhatsApp (y cada vez más por Instagram y TikTok), no por correo ni CRM tradicional. El problema es operativo: el WhatsApp del negocio vive en un solo teléfono, varios vendedores se pisan, nada queda registrado y no hay forma de medir nada. Órbita resuelve eso: reúne las conversaciones en un solo lugar donde un equipo entero puede atender sin pisarse, con un agente de IA que responde y **ejecuta acciones sobre el CRM** (crear oportunidades, mover etapas, agendar citas) cuando nadie está disponible.

El mercado ya está validado (Kommo, Clientify, Leadsales, Trengo, Zenvia, Whato, Sync-Manager son competidores activos y con precio público). El espacio de Órbita está en tres apuestas, en orden de defendibilidad:

1. **Agentes que ejecutan, no que responden bonito** — el diferenciador de producto real; requiere *function calling* con herramientas tipadas del propio dominio, RAG y *guardrails*.
2. **Precio sin castigo por equipo** — usuarios ilimitados, cobro por consumo (conversaciones + tokens de IA). Ataca directamente el modelo de asiento de Kommo.
3. **TikTok como canal de primera clase** — casi nadie lo tiene todavía; entra en fase 1 detrás del mismo puerto de canales.

El detalle completo (estudio de mercado, posicionamiento, riesgos) vive en [`../docs/OrbitaContextoyCompetencia.pdf`](../docs/OrbitaContextoyCompetencia.pdf).

## Qué hay implementado hoy

El slice vertical de **Tenants** (crear + consultar por id) es la implementación de referencia del patrón que debe seguir cada nuevo agregado: entidad de dominio rica → servicio de aplicación → persistencia EF Core/Postgres → controller.

Sobre esa base ya existe el arranque de **Identity & Tenancy** (track del Desarrollador 1 en el backlog, ver más abajo):

- `User` y `Membership` (rol `owner/admin/agent/viewer`), como segundo y tercer agregado del dominio.
- **Registro de organización** (`POST /api/organizations`): crea tenant + usuario dueño + membresía `owner` en una sola transacción, con slug generado automáticamente desde el nombre del negocio (con sufijo si choca) y contraseña con hash Argon2id. Es la implementación de `ORB-A05` del backlog.
- **Aislamiento multi-tenant** (`ORB-A09`, la historia de mayor consecuencia del backlog): filtro de consulta global de EF Core sobre un `ITenantContext` ambiental, más Row-Level Security de PostgreSQL activada sobre `memberships` (`current_setting('app.tenant_id')`), sincronizada por conexión desde `UnitOfWork`. Ver la nota en `Orbita.Infrastructure/Persistence/UnitOfWork.cs` y las pruebas de integración de aislamiento.
- **Inicio y cierre de sesión** (`POST /api/auth/login|refresh|logout`, `GET /api/auth/me`): JWT de acceso de vida corta en cookie httpOnly, refresh token rotativo con detección de reutilización (revoca toda la familia si un token ya usado vuelve a presentarse), y bloqueo temporal tras intentos fallidos de login. Es la implementación de `ORB-A06`.
- **Invitar miembros al equipo** (`POST /api/tenants/{tenantId}/invitations`, `resend`, `DELETE` para revocar, y `POST /api/invitations/accept` para aceptar): enlace de un solo uso que caduca a los 7 días, con detección de si el correo ya tiene cuenta (le pide confirmar su contraseña existente) o es nuevo (elige una). Aceptar deja a la persona logueada de una. Es la implementación de `ORB-A07`; el envío real de correo queda pendiente (no hay proveedor conectado todavía — se loguea el link en su lugar).
- **Recuperación de contraseña** (`POST /api/auth/forgot-password`, `POST /api/auth/reset-password`): enlace de un solo uso que caduca en una hora, respuesta idéntica exista o no la cuenta (no filtra qué correos están registrados), y al restablecer se invalidan todas las sesiones activas de la persona, no solo la que pidió el cambio. Es la implementación de `ORB-A10`.
- **Roles y permisos** (`GET /api/tenants/{tenantId}/members`, `PATCH .../members/{membershipId}/role`, `DELETE .../members/{membershipId}`): matriz de permisos por rol (`owner`/`admin`/`agent`/`viewer`) aplicada en el backend a través de un servicio de autorización compartido, con la regla de que un tenant nunca se queda sin al menos un owner activo. Es la implementación de `ORB-A08`; generaliza el chequeo "owner o admin" que `ORB-A07` había dejado en línea.
- **Bitácora de auditoría** (`GET /api/tenants/{tenantId}/audit-log`, filtrable por entidad, actor y rango de fechas): tabla `audit_log` append-only (sin UPDATE ni DELETE, reforzado también a nivel de base de datos), que registra actor, acción, entidad y diferencia. Es la implementación de `ORB-A15`. Por ahora solo los cambios de rol y la remoción de miembros del equipo quedan auditados — el patrón (`IAuditLogger`) está listo para engancharse a cualquier otra acción sensible a medida que se vaya necesitando.
- **Planes y suscripción** (`GET /api/plans`, `POST|PATCH|DELETE /api/tenants/{tenantId}/subscription`, `GET .../subscription/invoices`, webhooks en `/api/billing/webhooks/{stripe|wompi}`): catálogo de 3 planes sembrado en la migración, integración real contra Stripe (internacional) y Wompi (Colombia) detrás de un mismo puerto `IPaymentProvider`, cambio de plan con prorrateo automático vía Stripe. Es la implementación de `ORB-A12`. **Ninguna de las dos integraciones tiene credenciales reales conectadas todavía** — el código está listo, pero conectar una cuenta real (y, para Wompi, construir el scheduler de recobro periódico que todavía no existe) queda pendiente; ver la sección "Billing" de `CLAUDE.md` para el detalle exacto de qué falta.
- **Búsqueda de contactos** (`GET /api/tenants/{tenantId}/contacts?q=`): mismo listado, ahora con `pg_trgm` (typos y nombres incompletos) y `ILIKE` para prefijos. Índices GIN sobre nombre, teléfono, Instagram y correo. Es `ORB-D03`.
- **Contactos** (`GET|POST /api/tenants/{tenantId}/contacts`, `GET|PATCH .../contacts/{contactId}`, `GET .../contacts/{contactId}/opportunities`, `GET|POST .../contact-fields`): ficha con campos custom (jsonb), deduplicación por teléfono o Instagram (409), y vínculo opcional de oportunidades vía `contactId`. El historial de conversación lo aporta Track B. Es `ORB-D02`.
- **Tablero de oportunidades** (`GET /api/tenants/{tenantId}/pipelines/{pipelineId}/board`, `POST .../opportunities`, `POST .../opportunities/{id}/move`, hub `/hubs/crm`): kanban con suma por etapa, filtros por responsable y fecha, movimiento idempotente por `eventId` y eco en vivo por SignalR. Es `ORB-D05`.
- **Pipelines y etapas** (`GET|POST /api/tenants/{tenantId}/pipelines`, `PATCH|DELETE .../pipelines/{pipelineId}`, etapas con create/update/reorder/delete): varios tableros por organización, etapas ordenables con marca de ganada/perdida, y al borrar una etapa hay que reubicar sus oportunidades. Cada registro de organización deja un pipeline `Ventas` por defecto. Es la implementación de `ORB-D04`. El tablero kanban (ORB-D05) todavía no mueve tarjetas.
- **Verificación en dos pasos** (`POST /api/auth/2fa/setup|confirm|disable`, `POST /api/auth/2fa/backup-codes/regenerate`): TOTP con QR (el backend entrega la URI `otpauth://`, el frontend dibuja el código), 10 códigos de respaldo de un solo uso, y el login exige el segundo factor cuando está activado. Es la implementación de `ORB-A11`. El owner puede exigir MFA a todo el equipo (`PATCH /api/tenants/{tenantId}/settings/mfa-policy`), pero esa política **todavía no se aplica en tiempo real** — hacerlo bien requiere resolver antes a qué tenant pertenece una sesión al momento de iniciar sesión, la misma decisión pendiente que dejó sin claim de tenant al JWT (ver `CLAUDE.md`).

Todavía no hay el resto del modelo de datos (conversaciones, mensajes, agentes de IA, eventos) — se construye incrementalmente replicando el mismo patrón. El backlog completo de 61 historias está en [`../docs/Orbita-Historias-de-Usuario.pdf`](../docs/Orbita-Historias-de-Usuario.pdf).

## Arquitectura

Clean Architecture con dirección de dependencia estricta hacia el dominio:

| Proyecto | Responsabilidad |
|---|---|
| `Orbita.Domain` | Entidades ricas (`Tenant`, `User`, `Membership`) e interfaces de repositorio/puertos (`ITenantRepository`, `IUserRepository`, `IMembershipRepository`, `ITenantContext`, `IUnitOfWork`). Sin dependencias de framework. |
| `Orbita.Application` | Un servicio por caso de uso (`ITenantService`, `IOrganizationRegistrationService`), DTOs/requests, puertos que necesitan infraestructura pero no deben acoplar el dominio (`IPasswordHasher`), excepciones de negocio. |
| `Orbita.Infrastructure` | EF Core (`OrbitaDbContext`), configuraciones de entidad, migraciones, repositorios, `UnitOfWork` (sincroniza la sesión de Postgres para RLS), Postgres vía Npgsql. |
| `Orbita.Api` | Controllers, composición (`Program.cs`), manejo global de errores (`ErrorHandling/GlobalExceptionHandler`). |
| `Orbita.UnitTests` | Dominio y aplicación en aislamiento (sin base de datos, sin HTTP). |
| `Orbita.IntegrationTests` | `Orbita.Api` real vía `WebApplicationFactory`, contra Postgres real (Testcontainers). |

**Nota sobre esta arquitectura vs. el DAS.** El [documento de arquitectura](../docs/OrbitaDASArquitectura.pdf) (ADR-001) describe un monolito modular con 12 módulos y fronteras verificadas por ArchUnitNET. Esta implementación adapta esa idea a una Clean Architecture de 4 proyectos: la dirección de dependencia (Domain ← Application ← Infrastructure ← Api) ya es la frontera, verificada por el compilador en cada build, y cada agregado nuevo vive en su propia carpeta dentro de cada capa (`Tenants/`, `Identity/`, …) en vez de en un proyecto `.Contracts` separado. Es la variante adecuada para un equipo de 1-2 personas; si el proyecto crece hacia los 12 módulos del DAS, la migración es reorganizar carpetas en proyectos, no reescribir lógica.

El modelo de datos completo (multi-tenant, aislamiento por `tenant_id`, log de eventos append-only, patrón Outbox, particionado, etc.) vive en [`../docs/orbita-schema.dbml`](../docs/orbita-schema.dbml) y es la fuente de verdad para cualquier entidad nueva. Los diagramas (contexto, capas, despliegue AWS, módulos, flujo de mensajería, costos, evolución) están en [`../docs/OrbitaArquitectura.drawio.pdf`](../docs/OrbitaArquitectura.drawio.pdf).

Convenciones obligatorias de desarrollo (SOLID, tipado ultra estricto, testing, commits, branching) están en [`CLAUDE.md`](./CLAUDE.md). Importante: los commits **nunca** llevan coautoría de IA (`Co-Authored-By`, `Claude-Session`, etc.) — el autor es siempre la persona.

## El backlog y en qué orden se construye

El backlog completo (61 historias, 4 desarrolladores, división vertical por módulo) vive en [`../docs/Orbita-Historias-de-Usuario.pdf`](../docs/Orbita-Historias-de-Usuario.pdf). Este repo es el terreno de los cuatro tracks (el frontend consume la misma API), pero la mayor parte del trabajo de plataforma/identidad/facturación (Track A) ocurre aquí:

| Track | Dueño | Módulos | Historias clave ya iniciadas |
|---|---|---|---|
| A — Plataforma, Identidad y Facturación | Desarrollador 1 | Platform, Identity & Tenancy, Billing, Audit | `ORB-A05` (registro), `ORB-A09` (aislamiento), `ORB-A06` (sesión), `ORB-A07` (invitaciones), `ORB-A08` (roles y permisos), `ORB-A10` (recuperación de contraseña), `ORB-A11` (2FA), `ORB-A12` (planes y suscripción), `ORB-A15` (bitácora de auditoría) |
| B — Canales y Bandeja | Desarrollador 2 | Channels, Inbox, Notifications | — |
| C — Agentes de IA | Desarrollador 3 | AI Agents | — |
| D — CRM, Contenido y Analítica | Desarrollador 4 | CRM, Campaigns, Analytics, sitio público | `ORB-D04` (pipelines), `ORB-D05` (tablero), `ORB-D02` (contactos), `ORB-D03` (búsqueda) |

Lo que **nunca se recorta** del MVP (ver el documento de backlog, sección "Resumen"): `ORB-A09` (aislamiento entre organizaciones), `ORB-B02`/`ORB-B03` (no perder mensajes) y `ORB-A13` (medición de consumo desde el día 1) — las tres son imposibles de retroajustar sin reescribir.

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Docker Desktop corriendo (para Postgres local y para los tests de integración con Testcontainers)
- Git

No hace falta instalar `dotnet-ef` global — está pinneado en `dotnet-tools.json` y se restaura como herramienta local del repo (paso 1 abajo).

## Cómo correrlo (de cero a API corriendo)

```bash
# 0. Clonar y entrar al repo
git clone <url-del-repo>
cd Orbita

# 1. Restaurar la herramienta de EF Core (una sola vez por clon)
dotnet tool restore

# 2. Restaurar paquetes NuGet y compilar, para detectar problemas de entorno temprano
dotnet build Orbita.slnx

# 3. Levantar Postgres local (pgvector/pg_trgm/citext/pgcrypto incluidos)
docker compose up -d

# 4. Confirmar que Postgres ya aceptó conexiones (el healthcheck tarda unos segundos)
docker compose ps

# 5. Aplicar migraciones (usa el rol admin/owner, no el rol de runtime — ver CLAUDE.md)
dotnet tool run dotnet-ef database update --project Orbita.Infrastructure --startup-project Orbita.Api --connection "Host=localhost;Port=5432;Database=orbita_dev;Username=orbita;Password=orbita"

# 6. Correr la API (con hot reload)
dotnet watch run --project Orbita.Api

# 7. (opcional pero recomendado) Correr toda la suite de pruebas para confirmar que el entorno quedó bien
dotnet test Orbita.slnx
```

`appsettings.Development.json` ya trae valores de desarrollo listos para usar (credenciales dummy, JWT de firma solo-dev) — no hace falta crear ningún `.env` ni secreto propio para correr el proyecto localmente.

Con el entorno en `Development`, la documentación OpenAPI queda disponible vía Scalar en `/scalar/v1`.

### Problemas comunes al levantar el entorno

- **`docker compose up -d` falla o el healthcheck nunca pasa a "healthy"**: confirmar que Docker Desktop esté corriendo y que el puerto `5432` no esté ocupado por otra instancia de Postgres local (`docker compose down` y reintentar, o cambiar el puerto publicado en `docker-compose.yml` si de verdad lo necesitas ocupado para otra cosa).
- **La migración falla con error de autenticación**: se está usando el rol equivocado. Migraciones siempre corren como `orbita` (dueño), nunca como `orbita_app` (runtime, sin permisos de DDL) — ver la nota "Two roles, two connection strings" en [`CLAUDE.md`](./CLAUDE.md).
- **Los tests de integración fallan o se cuelgan**: casi siempre es que Docker no está corriendo — Testcontainers necesita el daemon disponible para levantar el Postgres efímero de cada corrida.
- **Puerto 5432 ya en uso por otro proyecto**: bajar el otro contenedor/servicio o remapear el puerto publicado en `docker-compose.yml`; la app lee el host/puerto desde `ConnectionStrings:Postgres` en `appsettings.Development.json`.

## Comandos comunes

```bash
dotnet build Orbita.slnx                 # compilar
dotnet test Orbita.slnx                  # todos los tests (integración requiere Docker corriendo)
dotnet format Orbita.slnx                # formatear
```

Ver [`CLAUDE.md`](./CLAUDE.md) para el detalle de comandos de migraciones, cómo correr un test puntual, y las reglas de arquitectura/negocio (incluidas las de `orbita-schema.dbml`, que son vinculantes) que aplican a todo código nuevo.

Ver [`HANDOFF.md`](./HANDOFF.md) para una foto del estado actual del trabajo (qué historia está en curso, qué decisiones recientes no hay que reabrir, qué falta) — es lo primero que hay que leer al retomar el repo después de un tiempo sin tocarlo.

## Plantilla de Pull Request

Usar esta plantilla para todo PR de este repo (una historia de usuario por PR, una rama `feature/<nombre>` por historia — ver la convención de branching en `CLAUDE.md`):

```markdown
# [Nombre de la PR]

## Historia de Usuario
[HU-XX - Nombre de la tarea]

## ¿Qué hace este PR?

Descripción clara y concisa de los cambios realizados.

## Cambios Realizados

- [ ] Cambio 1
- [ ] Cambio 2
- [ ] Cambio 3

## Cómo Probar

1. Paso 1 para reproducir / verificar el comportamiento
2. Paso 2
3. Resultado esperado

## Checklist

- [ ] El código compila sin errores
- [ ] Las pruebas pasan localmente
- [ ] No se dejó código comentado ni console.log de depuración
- [ ] La rama está actualizada con la rama base
```

## Repos y documentos relacionados

- [`orbita-front`](../orbita-front) — dashboard (Next.js) que consume esta API.
- `../docs` — carpeta compartida con:
  - [`OrbitaContextoyCompetencia.pdf`](../docs/OrbitaContextoyCompetencia.pdf) — estudio de mercado, competidores y posicionamiento.
  - [`OrbitaDASArquitectura.pdf`](../docs/OrbitaDASArquitectura.pdf) — decisiones de arquitectura (ADRs), costos y plan de evolución sin reprocesos.
  - [`OrbitaArquitectura.drawio.pdf`](../docs/OrbitaArquitectura.drawio.pdf) — diagramas (contexto, capas, despliegue, módulos, flujo de mensajería, costos, evolución).
  - [`orbita-schema.dbml`](../docs/orbita-schema.dbml) — modelo de datos completo, fuente de verdad del esquema.
  - [`Orbita-Historias-de-Usuario.pdf`](../docs/Orbita-Historias-de-Usuario.pdf) — backlog del MVP (61 historias, 4 tracks, dependencias).
  - [`Orbita-Guia-de-Pantallas.pdf`](../docs/Orbita-Guia-de-Pantallas.pdf) — guía de pantallas para diseño (qué construir primero, principios de UX).
