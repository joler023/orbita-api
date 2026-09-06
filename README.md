# Órbita — API

Backend de Órbita: un CRM conversacional multi-tenant con agentes de IA sobre WhatsApp e Instagram (TikTok en la fase 1), construido en ASP.NET Core.

## Qué hay implementado hoy

El slice vertical de **Tenants** (crear + consultar por id) es la implementación de referencia del patrón que debe seguir cada nuevo agregado: entidad de dominio rica → servicio de aplicación → persistencia EF Core/Postgres → controller. Todavía no hay autenticación, ni el resto del modelo de datos (conversaciones, mensajes, agentes de IA, CRM, eventos) — se construye incrementalmente replicando este mismo patrón.

## Arquitectura

Clean Architecture con dirección de dependencia estricta hacia el dominio:

| Proyecto | Responsabilidad |
|---|---|
| `Orbita.Domain` | Entidades ricas (`Tenant`) e interfaces de repositorio (`ITenantRepository`). Sin dependencias de framework. |
| `Orbita.Application` | Un servicio por agregado (`ITenantService`/`TenantService`), DTOs/requests, excepciones de negocio. |
| `Orbita.Infrastructure` | EF Core (`OrbitaDbContext`), configuraciones de entidad, migraciones, repositorios, Postgres vía Npgsql. |
| `Orbita.Api` | Controllers, composición (`Program.cs`), manejo global de errores (`ErrorHandling/GlobalExceptionHandler`). |
| `Orbita.UnitTests` | Dominio y aplicación en aislamiento (sin base de datos, sin HTTP). |
| `Orbita.IntegrationTests` | `Orbita.Api` real vía `WebApplicationFactory`, contra Postgres real (Testcontainers). |

El modelo de datos completo (multi-tenant, aislamiento por `tenant_id`, log de eventos append-only, patrón Outbox, etc.) vive en `../docs/orbita-schema.dbml` y es la fuente de verdad para cualquier entidad nueva.

Convenciones obligatorias de desarrollo (SOLID, tipado ultra estricto, testing, commits, branching) están en [`CLAUDE.md`](./CLAUDE.md). Importante: los commits **nunca** llevan coautoría de IA (`Co-Authored-By`, `Claude-Session`, etc.) — el autor es siempre la persona.

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

Ver [`CLAUDE.md`](./CLAUDE.md) para el detalle de comandos de migraciones, cómo correr un test puntual, y las reglas de arquitectura/negocio que aplican a todo código nuevo.

## Repos relacionados

- [`orbita-front`](https://github.com/joler023/orbita) — dashboard (Next.js) que consume esta API.
- `../docs` — modelo de datos, historias de usuario y guía de pantallas del producto.
