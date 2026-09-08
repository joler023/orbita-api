# HANDOFF

Documento de traspaso de contexto para quien retome este repo (otro desarrollador, u otra sesión de Claude Code). Objetivo: que puedas seguir trabajando sin releer todo el historial de commits. Se actualiza al terminar cada historia — no es una bitácora histórica, es una foto del momento.

Para las reglas de arquitectura/negocio vinculantes (que no cambian historia a historia), ver [`CLAUDE.md`](./CLAUDE.md). Este archivo es sobre *estado*, `CLAUDE.md` es sobre *reglas*.

## Última actualización

**2026-09-08 (5)** — `ORB-B05` (envío de texto) completo, misma rama apilada (`feature/inbound-message-processing`). Nuevo `POST /api/tenants/{tenantId}/conversations/{conversationId}/messages` (202), persist-first (`Message` Queued + `OutboundMessageJob` encolado en el mismo `SaveChangesAsync`), `OutboundMessageWorker` (tick de 1 s) despachando vía `IChannelAdapter.SendTextAsync` (nuevo en el puerto), `MetaErrorCatalog` clasificando transitorio/permanente y traduciendo a español en lectura, `TokenBucketRateLimiter` (80/s por cuenta, solo en memoria — no coordina entre instancias), nuevo permiso `SendMessages` (Owner/Admin/Agent, no Viewer). `WhatsAppChannelAdapter` pasó de `Singleton` a `Scoped` en el DI (ahora depende de `IChannelCredentialStore`/`IWhatsAppCloudApiClient`, ambos `Scoped`). Ver CLAUDE.md, sección "Mensajes salientes (ORB-B05)". 338 unitarias en verde; misma limitación de Docker para las de integración nuevas (`MessagesControllerTests`), salvo `TokenBucketRateLimiterTests` que no necesita BD y sí corrió.

**2026-09-08 (4)** — `ORB-B04` (Transactional Outbox) completo en `feature/inbound-message-processing` (misma rama que B03, siguiendo apilada). `OutboxEvent` (bigint, sin RLS, `DELETE` revocado para `orbita_app`), `IOutboxWriter`/`OutboxDispatchService`/`InProcessIntegrationEventPublisher`, `OutboxDispatcherWorker` (tick de 500 ms), y el retrofit de `InboundMessageProcessor` para emitir `message.received`/`conversation.opened` sin PII (solo ids/enums — verificado con un test que busca el body/teléfono en el JSON persistido y falla si aparecen). Ver CLAUDE.md, sección "Outbox (ORB-B04)" para las decisiones no obvias (backoff sin columna nueva, el `FOR UPDATE SKIP LOCKED` solo protege contra el mismo dispatcher, no contra múltiples instancias). 312 unitarias en verde; igual limitación de Docker que B01-B03 para las de integración nuevas (`OutboxTests`).

**2026-09-08 (3)** — `ORB-B03` (normalización de entrantes) completo en `feature/inbound-message-processing`, ramificada desde `feature/webhook-ingestion` (B02). Antes de escribir B03 se mergeó `origin/feature/d02-contactos` (Track D: `ORB-D04`/`D05`/`D02` — pipelines, oportunidades, contactos) a esta rama, porque B03 reutiliza el `Contact` de Track D en vez de crear el suyo — decisión explícita del usuario, ver `track-b-plan-and-branch-order` en memoria. El merge trajo conflictos aditivos esperados en `RolePermissions.cs`/`Permission.cs` (unión de permisos de ambos tracks), `OrbitaDbContext.cs`, `GlobalExceptionHandler.cs`, `DependencyInjection.cs` (Application), la migration snapshot y los docs — todos resueltos combinando ambos lados (verificado con `dotnet ef migrations has-pending-model-changes` → sin drift), ninguno era lógica en conflicto real. `Conversation`/`Message` (tabla particionada desde el día 1, RLS heredada), `InboundMessageProcessor`, `WhatsAppPayloadParser`/`WhatsAppChannelAdapter` e `InboundMessageWorker` quedaron implementados — ver CLAUDE.md, sección "Bandeja: mensajes entrantes (ORB-B03)" para el detalle y las decisiones no obvias (canonización del teléfono sin `+`, idempotencia en capas, columnas del DBML incluidas por adelantado). **Esta rama sigue sin tocar `develop`**: sigue la misma estrategia de apilar branches de B01/B02/B03 hasta que el usuario pruebe el flujo completo en Yaak/Scalar. Docker sigue sin estar disponible en esta máquina: las pruebas de integración nuevas (`InboundMessageFlowTests`, `WhatsAppPayloadParserTests` sin BD) compilan; las de BD no se ejecutaron. 304 unitarias en verde.

**2026-09-08 (2)** — `ORB-B02` (ingesta de webhooks) en la rama `feature/webhook-ingestion`, **ramificada desde `feature/whatsapp-channel-connect` (B01), no desde `develop`** — desviación deliberada del plan original: el desarrollador quiere probar la API completa (WhatsApp connect + webhooks) en Yaak/Scalar antes de abrir el PR de B01 a `develop`, así que B02 se apiló sobre B01 en vez de esperar el merge. **Quien retome tiene que hacer rebase de `feature/webhook-ingestion` sobre `develop` (después de que B01 se mergee) antes de abrir su propio PR** — tal como está, su diff incluye todos los commits de B01. Ver "Track B" abajo para el detalle de lo implementado. Misma limitación de Docker que B01: las 5 pruebas de integración nuevas (`WhatsAppWebhooksControllerTests`) compilan pero no se ejecutaron; las 4 de `MetaWebhookSignatureVerifierTests` (sin base de datos) sí corrieron y pasan. Las 237 unitarias están en verde. El test de carga descrito en el plan original (concurrencia con payloads duplicados) no se escribió — ver CLAUDE.md, sección "Ingestión de webhooks (ORB-B02)".

**2026-09-08 (1)** — Arranca el Track B (Canales y Bandeja) en este repo con `ORB-B01` (conectar WhatsApp) en la rama `feature/whatsapp-channel-connect`. El plan detallado de las 19 historias del track (orden, ramas, ports/stand-ins, dependencias con Track D) quedó acordado antes de empezar; ver la sección "Track B" abajo. La máquina donde se desarrolló `ORB-B01` **no tenía Docker**, así que las 11 pruebas de integración nuevas (`ChannelsControllerTests`, `ChannelCredentialStoreTests`) compilan pero no se ejecutaron ahí — quien retome debe correr `dotnet test Orbita.slnx` con Docker antes de mergear. Las 215 unitarias sí están en verde.

**2026-09-07 (2)** — `ORB-D02` (ficha de contacto) en `feature/d02-contactos`, apilada sobre `feature/d05-oportunidades`.

**2026-09-07 (1)** — `ORB-A08`, `ORB-A10`, `ORB-A11`, `ORB-A12` y `ORB-A15` ya están mergeados en `develop` (en ese orden). El merge de `ORB-A15` dejó `RolePermissions.cs` con las tres ramas pisándose (claves de diccionario duplicadas para `Owner`/`Admin`, que compilaban pero reventaban en tiempo de ejecución) y el `.csproj` de Infrastructure con una referencia duplicada — ya corregido directamente en `develop`. Toda la suite (175 unitarias + 62 de integración) está en verde sobre `develop` a día de hoy.

## Qué está implementado

Ver la sección "Qué hay implementado hoy" en [`README.md`](./README.md) — se mantiene sincronizada ahí, no se duplica aquí.

Track D (CRM — dueño de pipelines/contactos en este repo, vertical full-stack):

- [x] `ORB-D04` Pipelines y etapas — default `Ventas` al registrar
- [x] `ORB-D05` Tablero de oportunidades — create/move + SignalR
- [x] `ORB-D02` Ficha de contacto — listado, dedup por teléfono/Instagram, campos custom, vínculo a oportunidades; historial de conversación queda para Track B
- [ ] `ORB-D06` Crear oportunidad desde la conversación (depende de `ORB-D05` y `ORB-B13`)
- [ ] `ORB-D03` Búsqueda de contactos

Track A (Plataforma, Identidad y Facturación):

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

Con esto, el track de Desarrollador 1 queda sin historias P1 pendientes que no dependan de otro track. Lo que sigue (`ORB-A13`/`ORB-A14`) necesita que Track C construya los agentes de IA primero, o (P2, `ORB-A11` ya cubierto) no hay más trabajo aislado obvio — la próxima sesión debería confirmar con el resto del equipo antes de inventar alcance nuevo aquí.

Track B (Canales y Bandeja — Desarrollador 2), en orden de ejecución acordado. Una rama por historia desde `develop`; la infraestructura externa (Lambda, SQS, Redis, Secrets Manager/KMS, R2, EventBridge) va detrás de ports con stand-ins locales, sin SDK de AWS todavía:

- [x] `ORB-B01` Conectar WhatsApp — rama `feature/whatsapp-channel-connect`, **sin app de Meta real conectada** (ver `CLAUDE.md`, sección "Channels")
- [x] `ORB-B02` Ingesta de webhooks — rama `feature/webhook-ingestion` (apilada sobre B01, ver nota arriba), `POST` en las mismas rutas de `/api/webhooks/whatsapp` + nuevas `/api/webhooks/instagram`, firma `X-Hub-Signature-256`, cola durable en Postgres (`inbound_webhook_events`) como stand-in de SQS
- [x] `ORB-B03` Normalización de entrantes — rama `feature/inbound-message-processing` (apilada sobre B02, ver nota arriba); mergeó `feature/d02-contactos` para reutilizar el `Contact` de Track D en vez de crear uno propio
- [x] `ORB-B04` Transactional Outbox — rama `feature/inbound-message-processing` (apilada sobre B03)
- [x] `ORB-B05` Envío de texto — rama `feature/inbound-message-processing` (apilada)
- [ ] `ORB-B06` Media · `ORB-B07` Ventana de 24 h y plantillas
- [ ] `ORB-B12` Bandeja · `ORB-B13` Vista de conversación · `ORB-B14` Tiempo real (reutiliza el `AddSignalR`/`CrmHub` de d02) · `ORB-B15` Asignación
- [ ] P1: `ORB-B08` Estados de entrega · `ORB-B09` Instagram (**arrancar el App Review de Meta ya**, tarda semanas) · `ORB-B11` `human_agent` · `ORB-B19` DLQ · `ORB-B16` Notas · `ORB-B17` Etiquetas · `ORB-B18` Respuestas rápidas
- [ ] P2: `ORB-B10` Comentarios y menciones de Instagram

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
- **Los tokens de canal (`ORB-B01`) siguen el mismo esquema**: `IChannelCredentialStore` es el port (Secrets Manager + KMS en producción, `credentials_ref` = ARN) y `DataProtectionChannelCredentialStore` el stand-in local (ciphertext en la tabla `channel_credentials`, referencia opaca `local://{guid}`). El token nunca está en claro en Postgres ni en logs — los `HttpClient` hacia Graph API se registran con `RemoveAllLoggers()` a propósito.
- **`channel_accounts` no lleva RLS ni query filter** (misma categoría que `subscriptions`): un webhook de Meta llega solo con `(kind, external_id)` y sin tenant en sesión. El `webhook_secret` del DBML es el `hub.verify_token` por cuenta (Meta soporta un callback por WABA vía `override_callback_uri`), no la clave HMAC — esa es el app secret, configuración de plataforma. `waba_id` es una columna añadida al DBML.
- La fila de `channel_accounts` se **confirma antes** de pedirle a Meta la suscripción del webhook, porque Meta verifica el callback de forma síncrona dentro de esa llamada y nuestro handler busca la cuenta por id. No reordenar.

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
- **`ORB-B01`: sin app de Meta real** — `Channels:Meta:*` está vacío; los clientes de Graph API (`MetaAuthClient`, `WhatsAppCloudApiClient`) nunca han hablado con Meta de verdad. Crear la app en Meta for Developers (producto WhatsApp) y arrancar el App Review para Instagram cuanto antes: es un plazo externo que no controlamos.
- **`ORB-B01`: pruebas de integración escritas pero no ejecutadas en la máquina de desarrollo** (sin Docker). Correr la suite completa con Docker antes de mergear la rama.
- **`ORB-B02`: `feature/webhook-ingestion` está apilada sobre `feature/whatsapp-channel-connect`, no sobre `develop`.** Antes de abrir su propio PR hay que mergear B01 a `develop` primero y luego rebasar esta rama — si se abre el PR tal cual, arrastra los 6 commits de B01. Mismo problema de Docker que B01 para `WhatsAppWebhooksControllerTests`. El test de carga/concurrencia del plan original no se escribió (ver CLAUDE.md).
- **`ORB-B03` mergeó `feature/d02-contactos` en vez de esperar que llegue a `develop`** (decisión del usuario, ver arriba). El `Contact` de Track D sigue usando sus propios nombres de columna (`phone`, `instagram_username`), que NO coinciden con el DBML (`phone_e164`, `ig_user_id`) — deuda de reconciliación sin resolver, documentada pero no arreglada. `Contact.InstagramUserId`/`ig_user_id` (añadido en B03) sí sigue el nombre del DBML. `NormalizePhone` ya canoniza a solo dígitos (sin `+`) para calzar con el `wa_id` de Meta.
- **`ORB-B03`: `TenantIsolationTests` no se extendió** para conversations/messages (el mecanismo de RLS ya está probado por otras tablas). Falta un test determinístico de fallo repetido del worker (`WorkerFailure_ThreeTimes_MarksDead` del plan original) — `InboundMessageFlowTests` usa polling con `Eventually` en vez de un adapter fake inyectado por `WithWebHostBuilder`.
