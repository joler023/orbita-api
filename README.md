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

Todavía no hay autenticación (login/JWT — `ORB-A06`), ni el resto del modelo de datos (conversaciones, mensajes, agentes de IA, pipeline de ventas, eventos) — se construye incrementalmente replicando el mismo patrón. El backlog completo de 61 historias está en [`../docs/Orbita-Historias-de-Usuario.pdf`](../docs/Orbita-Historias-de-Usuario.pdf).

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
| A — Plataforma, Identidad y Facturación | Desarrollador 1 | Platform, Identity & Tenancy, Billing, Audit | `ORB-A05` (registro), `ORB-A09` (aislamiento) |
| B — Canales y Bandeja | Desarrollador 2 | Channels, Inbox, Notifications | — |
| C — Agentes de IA | Desarrollador 3 | AI Agents | — |
| D — CRM, Contenido y Analítica | Desarrollador 4 | CRM, Campaigns, Analytics, sitio público | — |

Lo que **nunca se recorta** del MVP (ver el documento de backlog, sección "Resumen"): `ORB-A09` (aislamiento entre organizaciones), `ORB-B02`/`ORB-B03` (no perder mensajes) y `ORB-A13` (medición de consumo desde el día 1) — las tres son imposibles de retroajustar sin reescribir.

## Requisitos

- .NET SDK 10
- Docker (para Postgres local y para los tests de integración con Testcontainers)

## Cómo correrlo

```bash
# 1. Restaurar la herramienta de EF Core (una sola vez)
dotnet tool restore

# 2. Levantar Postgres local (pgvector/pg_trgm/citext/pgcrypto incluidos)
docker compose up -d

# 3. Aplicar migraciones
dotnet tool run dotnet-ef database update --project Orbita.Infrastructure --startup-project Orbita.Api

# 4. Correr la API (con hot reload)
dotnet watch run --project Orbita.Api
```

Con el entorno en `Development`, la documentación OpenAPI queda disponible vía Scalar en `/scalar/v1`.

## Comandos comunes

```bash
dotnet build Orbita.slnx                 # compilar
dotnet test Orbita.slnx                  # todos los tests (integración requiere Docker corriendo)
dotnet format Orbita.slnx                # formatear
```

Ver [`CLAUDE.md`](./CLAUDE.md) para el detalle de comandos de migraciones, cómo correr un test puntual, y las reglas de arquitectura/negocio (incluidas las de `orbita-schema.dbml`, que son vinculantes) que aplican a todo código nuevo.

## Repos y documentos relacionados

- [`orbita-front`](../orbita-front) — dashboard (Next.js) que consume esta API.
- `../docs` — carpeta compartida con:
  - [`OrbitaContextoyCompetencia.pdf`](../docs/OrbitaContextoyCompetencia.pdf) — estudio de mercado, competidores y posicionamiento.
  - [`OrbitaDASArquitectura.pdf`](../docs/OrbitaDASArquitectura.pdf) — decisiones de arquitectura (ADRs), costos y plan de evolución sin reprocesos.
  - [`OrbitaArquitectura.drawio.pdf`](../docs/OrbitaArquitectura.drawio.pdf) — diagramas (contexto, capas, despliegue, módulos, flujo de mensajería, costos, evolución).
  - [`orbita-schema.dbml`](../docs/orbita-schema.dbml) — modelo de datos completo, fuente de verdad del esquema.
  - [`Orbita-Historias-de-Usuario.pdf`](../docs/Orbita-Historias-de-Usuario.pdf) — backlog del MVP (61 historias, 4 tracks, dependencias).
  - [`Orbita-Guia-de-Pantallas.pdf`](../docs/Orbita-Guia-de-Pantallas.pdf) — guía de pantallas para diseño (qué construir primero, principios de UX).
