# HANDOFF

Documento de traspaso de contexto para quien retome este repo (otro desarrollador, u otra sesión de Claude Code). Objetivo: que puedas seguir trabajando sin releer todo el historial de commits. Se actualiza al terminar cada historia — no es una bitácora histórica, es una foto del momento.

Para las reglas de arquitectura/negocio vinculantes (que no cambian historia a historia), ver [`CLAUDE.md`](./CLAUDE.md). Este archivo es sobre *estado*, `CLAUDE.md` es sobre *reglas*.

## Última actualización

**2026-09-16** — **Épica C2 completa: `ORB-C04`, `C05`, `C06`, `C08`, `C09` y `C12` implementadas, probadas y verificadas con los modelos reales contra Neon.** La suite entera está en verde: **573 unitarias + 263 de integración, cero fallos**, y `dotnet format --verify-no-changes` limpio.

Lo que quedó funcionando de punta a punta (webhook firmado → cola → worker → outbox → asistente → cola de salida), comprobado contra Neon con OpenRouter de verdad:

- **C04** — el asistente responde en español, apoyado en los documentos indexados, y la conversación queda con su `ai_agent_id`.
- **C06** — un tema bloqueado responde la frase del dueño como mensaje de sistema, sin gastar una llamada al modelo, y deja `agent.reply_blocked` en el outbox con el motivo y el tema.
- **C08** — una regla por palabra clave sin agente deja la conversación para el equipo: el asistente no contesta.
- **C05** — el asistente registró una oportunidad real con `crear_oportunidad` (`tools_called: ['crear_oportunidad']` en `ai_runs`, auditada con `actor_type = AiAgent`) y siguió contestando en la misma respuesta.
- **C12** — la misma pregunta de un segundo cliente se respondió **desde la caché, sin llamar al modelo**: USD 0.000169 la primera vez, ~USD 0.0000003 la segunda.

Gasto real de OpenRouter hasta ahora: **USD 0.0011 de los 5 de saldo** (`total_usage` de la API). Las filas caras de `ai_runs` son del seed, no de gasto real.

Las tres migraciones que faltaban (`AddAiRunToolsAndChunks`, `AddRoutingRulesAndBusinessHours`, `AddSemanticAnswerCache`) ya están aplicadas en Neon.

Lo que sigue sin existir, a propósito: `ORB-C07` (traspaso a humano) depende de `ORB-B15`; `agendar_cita` y `escalar_a_humano` siguen no disponibles porque no hay agenda ni cola humana detrás; la condición "etiqueta" del enrutador no existe porque no hay tabla de etiquetas; y `ORB-A13` (medición de consumo) es quien debe leer `ai_runs`.

### Antes de esto


**2026-09-15 (5)** — **La suite de integración corrió entera por primera vez y está en verde: 512 unitarias + 248 de integración, cero fallos.** Antes de esto nunca se había ejecutado completa (la máquina donde se escribió no tenía Docker), y al correrla aparecieron 21 fallos: 16 ya estaban en `develop`.

Detrás de esos 21 había **ocho bugs reales**, no pruebas mal escritas. Siete de producción:

1. **Ningún envío funcionaba**: `OutboundMessageService` leía `conversations` fuera de scope de tenant → 404 sobre conversaciones existentes.
2. **El segundo mensaje de cada cliente rompía la ingesta**: `InboundMessageProcessor` igual → contacto duplicado, `ix_contacts_tenant_phone` violado, evento muerto.
3. **Borrar un asistente daba 500** (FK `RESTRICT` de `ai_runs` sin manejar).
4. **`MessageTemplateService` devolvía lista vacía siempre**, no detectaba duplicados, y `SyncFromMetaAsync` **duplicaba el catálogo entero en cada corrida**.
5. **`OutboundMessageDispatchService` moría con `MessageNotFound` en cada intento**: el mensaje se encolaba (202, visible en la bandeja) y nunca salía.
6. **`MediaService` devolvía 404** en cada URL de subida.
7. **Cada evento del outbox esperaba 30 segundos antes de su primer intento** — la fórmula de retroceso se aplicaba también al intento inicial (`power(2, 0) = 1`). Medido contra la base real: `message.received` tardaba 38 s. Eso anulaba el tick de 500 ms que ORB-B04 eligió a propósito y hacía **aritméticamente imposible** el criterio de ORB-C04 ("latencia percibida por debajo de 6 s en el p95"), que estaba dado por cumplido sin haberse medido.

Los seis primeros son la misma falla: leer una tabla con RLS fuera de una transacción que fije `app.tenant_id`. Ver CLAUDE.md, "Reads of RLS'd tables must run inside a tenant scope".

Y uno de pruebas que era de nuestro track: **el fixture hacía `RemoveAll<IHostedService>()`** para quitar el indexador de conocimiento (ORB-C02) y de paso borraba **todos** los workers de Track B. Más un octavo: `PipelinesControllerTests` construía su `DbContext` sin `UseVector()`, así que EF no podía armar el modelo.

**Las aserciones tampoco veían lo que el worker escribía**: leían tablas con RLS desde un scope sin tenant. Nuevo `TenantsApiFixture.CreateOwnerDbContext()` para eso, y `TestRequests.ConnectVerifiedWhatsAppAsync()` porque conectar un canal sin completar el handshake de Meta deja la cuenta en `PendingVerification` y todo envío responde 409.

**Lo más importante para no repetir:** una prueba que afirma *ausencia* **pasa** con este bug en vez de fallar. `Results_never_cross_tenants` habría certificado aislamiento entre tenants con la búsqueda completamente rota, porque `Assert.All` sobre colección vacía pasa — y estaba listada en el checklist como logro de ORB-C03. Las tres pruebas de aislamiento de Track C llevan ahora **control positivo antes del negativo**. Lo detectó la sesión del frontend, no nosotros.

Hueco conocido que sigue abierto: no hay cobertura de un reintento manual exitoso (`POST .../messages/{id}/retry` → `Sent`), porque llegar a `Failed` con un error transitorio exige agotar cinco intentos con retroceso de 30 s, 60 s, 120 s. Forzarlo reescribiendo `next_attempt_at` choca con el lock de fila del worker (probado: once minutos y falló igual). Lo que lo destraba es el `MutableTimeProvider` en el host de pruebas — el mismo que ORB-B06 y ORB-B07 anotaron como faltante; ya existe en `Orbita.UnitTests/TestSupport`.

**2026-09-15 (4)** — `fix/agent-delete-with-history`, encima de C04. Borrar un asistente que ya había corrido devolvía **500** (violación de la FK `RESTRICT` de `ai_runs`); ahora devuelve **409** con mensaje en español. Lo destapó la sesión del frontend preguntando qué le pasa a `conversations.ai_agent_id` cuando alguien usa el botón Eliminar de la pantalla 2.5, que ya está construido de su lado. Ver CLAUDE.md, "Borrar un asistente que ya trabajó".

**2026-09-15 (2)** — **Dos bugs serios de Row Level Security, arreglados en `fix/outbound-reads-under-rls`.** Los encontró la primera corrida real contra una base con RLS aplicando de verdad; las pruebas de integración que debían haberlos detectado existían desde B03 y B05, pero nunca se habían ejecutado (sin Docker en aquella máquina).

- **Ningún envío funcionaba.** `OutboundMessageService` leía `conversations` fuera de un scope de tenant, así que todo `POST .../conversations/{id}/messages` devolvía 404 sobre una conversación existente.
- **El segundo mensaje de cada cliente rompía la ingesta.** `InboundMessageProcessor` leía `contacts`/`conversations`/`messages` igual, así que la búsqueda de contacto siempre fallaba: creaba un contacto duplicado, violaba `ix_contacts_tenant_phone` y el evento de webhook quedaba muerto. El chequeo de idempotencia por `external_id` estaba inerte por lo mismo.

Hay un miembro nuevo en `IUnitOfWork`, `ExecuteAndSaveInTenantScopeAsync`, para el caso "leer y escribir en la misma transacción con el mismo `app.tenant_id`". Ver CLAUDE.md, sección "Reads of RLS'd tables must run inside a tenant scope". **Ojo al mergear**: agregar un miembro a `IUnitOfWork` rompe toda implementación a mano de la interfaz y git no lo marca como conflicto.
**2026-09-15 (3)** — `ORB-C04` (el agente responde) en `feature/c04-agent-responds`, la punta del stack (`fix/embedding-model-pricing` → `fix/outbound-reads-under-rls` → esta). Con esto arranca la Épica C2 de Track C, que estaba bloqueada por `ORB-B03` hasta que Track B entró a `develop`.

El asistente se engancha al evento `message.received` del outbox, no a `InboundMessageProcessor`: `AgentReplyIntegrationHandler` es la primera implementación de `IIntegrationEventHandler` en todo el repo — el seam que construyó B04 estaba vacío. Ver CLAUDE.md, sección "El agente responde (ORB-C04)", para las decisiones que no conviene reabrir.

Dos cosas que había que arreglar para que esto funcionara y que no eran evidentes en el plan:

- **`conversations.ai_agent_id` no lo escribía nadie.** La columna existe desde B03 pero ninguna clase la llenaba, así que un agente nunca habría respondido. Ahora `Conversation.AssignAgent` la fija la primera vez que un asistente contesta, y no reasigna nunca (eso es C08).
- **`openai/text-embedding-3-small` no tenía precio configurado**, así que cada indexación y cada búsqueda se registraban en `ai_runs.cost_usd` como gratis. Va arreglado en la rama de abajo del stack, con una prueba que recorre la configuración que se despacha y falla si algún modelo asignado a una tarea no tiene precio.

Se agregaron dos ayudas de prueba que varias historias venían pidiendo por escrito: `MutableTimeProvider` (el que faltaba para probar expiraciones — B06 y B07 lo dejaron anotado) y `PassThroughUnitOfWork` en `Orbita.UnitTests/TestSupport`.

**2026-09-15** — Hay **base de desarrollo/integración administrada** (Neon, PostgreSQL 18) con las 24 migraciones aplicadas y datos de ejemplo cargados. No reemplaza a Docker: las pruebas de integración siguen levantando su propio Postgres con Testcontainers, esto es para correr la API y para que el frontend tenga contra qué trabajar.

Lo que hay que saber antes de apuntar cualquier otra base administrada a este repo:

- **El rol dueño tiene que llamarse `orbita`.** La migración `AddUsersAndMemberships` (ORB-A09) lo nombra literalmente en `ALTER DEFAULT PRIVILEGES FOR ROLE orbita`. Si las migraciones corren como cualquier otro rol, toda tabla creada por una migración posterior nace sin los permisos que `orbita_app` necesita, y la app falla en runtime tabla por tabla. `scripts/init-managed-db.sql` crea ese rol y las extensiones; se corre una vez, antes de la primera migración.
- **El rol administrador del proveedor no sirve como rol de runtime.** El `neondb_owner` de Neon tiene `BYPASSRLS`: usarlo apaga el aislamiento entre organizaciones sin dar ningún error. La app se conecta como `orbita_app` y punto. Verificado: sin tenant en sesión, `contacts` devuelve cero filas; con tenant, solo las de ese tenant.
- **Migraciones por el endpoint directo, app por el del pooler.** El pooler es PgBouncer en modo transacción, compatible con el `SET LOCAL app.tenant_id` que abre `IUnitOfWork` dentro de cada transacción, pero no con una sesión larga de DDL.

`scripts/seed-dev.sql` deja tres organizaciones (CO/MX/ES, que cubren los dos caminos de cobro), 14 personas con los cuatro roles, 6 canales, 1.800 contactos, 840 conversaciones con ~21.000 mensajes, 540 oportunidades, 4 asistentes con su base de conocimiento y bitácora de auditoría. Es idempotente y no toca `plans`, que es catálogo sembrado por migración, no dato de ejemplo. Todas las personas entran con `Orbita2026!`.

La configuración local (cadenas de conexión, key de OpenRouter) va en `.env`, que está en `.gitignore`; `.env.example` documenta las claves.

**2026-09-14 (verificación post-merge)** — `feature/whatsapp-channel-connect`, `feature/webhook-ingestion` y `feature/inbound-message-processing` (Track B completo, `ORB-B01`–`ORB-B08`, con `d02`/`d04`/`d05` de Track D adentro) llegaron a `develop` — el dev original de Track B dejó el proyecto con el trabajo terminado pero sin mergear; otra sesión hizo la integración. `dotnet build Orbita.slnx` (0 errores, 0 advertencias) y `dotnet test Orbita.UnitTests` (489/489) verificados dos veces, en dos entornos distintos, sobre ese merge. **En ese momento las pruebas de integración no habían podido correr por falta de Docker** — desde entonces sí corrieron (ver la entrada del 2026-09-15 arriba, "la suite de integración corrió entera por primera vez"), y destaparon justamente los bugs de lectura bajo RLS que esa corrida documenta. Quedan sin mergear (stack de Track D independiente, nadie los está tomando): `feature/d03-busqueda`, `feature/d13-exportacion`.

**2026-09-14 (integración final)** — Se trae `develop` (Track C + la integración anterior de B01/B02, ver notas siguientes) a `feature/inbound-message-processing` (Track B, `ORB-B03`–`ORB-B08`, con `d02`/`d04`/`d05` de Track D ya adentro). Choques: el ya conocido de `Permission.cs`/`RolePermissions.cs` (unión completa, 15 permisos) y `OrbitaDbContext.cs` (DbSets/filtros de ambos lados); además `IMediaStorage`/`LocalFileMediaStorage` los había creado cada track por su lado para su propia historia (`ORB-B06` y `ORB-C02`) — mismo puerto, mismo nombre, implementación de Track C es superconjunto (agrega `DeleteAsync`), se conservó esa. `OrganizationRegistrationService` también lo tocaron ambos (Track B siembra pipeline por defecto, Track C siembra agente por defecto) — quedan las dos siembras. Todo resuelto combinando ambos lados, sin perder lógica de ninguno; snapshot de EF regenerado con `dotnet-ef` y verificado sin drift contra el `OrbitaDbContext` ya fusionado.

**2026-09-14 (integración)** — Se trae `develop` (con Track C ya mergeado, ver nota siguiente) a `feature/webhook-ingestion` y a `feature/whatsapp-channel-connect` (Track B, `ORB-B01`/`ORB-B02`). El choque real fue el ya conocido de `Permission.cs`/`RolePermissions.cs` (claves de Track B junto a `ManageAiAgents` de Track C) más `OrbitaDbContext.cs` (DbSets de ambos lados) — resueltos conservando ambos lados en los dos casos.

**2026-09-14** — **Track C entero está mergeado en `develop`**: `C01`, `C02`, `C03`, `C10`, `C11` y `C13`, en ese orden, como un stack de PRs encadenadas (#22 a #28). Y con este merge entra también `ORB-A16` (sesión y organizaciones), que iba aparte porque es de Track A.

`ORB-A16` se escribió desde la sesión de Track C, con autorización explícita del equipo, porque el frontend estaba bloqueado —no se podía entrar al producto en un navegador nuevo— y ningún otro track lo iba a tomar ese día. Toca `memberships` y el `UnitOfWork`, así que quien lleve Track A debería revisarla aunque ya esté adentro.

Al juntar las dos cosas hubo tres choques, todos resueltos acá: `IUnitOfWork` y `UnitOfWork` (cada lado agregó un método distinto en el mismo lugar, se conservan los cuatro) y —el que **git no marca como conflicto**— el `PassThroughUnitOfWork` de `TenantAwareLlmModelSelectorTests`, que implementa `IUnitOfWork` a mano y deja de compilar en cuanto aparece un método nuevo en la interfaz. Vale la pena recordarlo: cualquier rama con una implementación propia de esa interfaz se rompe en silencio al mergear, y el síntoma aparece en un test que no tiene nada que ver con el cambio.

El estado detallado de Track C está en `local/checklist-track-c.md`, y el contrato acordado con el frontend en `local/coordinacion-front-back.md` (ambos fuera de git).

**2026-09-08 (8)** — `ORB-B08` (estados de entrega) completo, misma rama apilada (`feature/inbound-message-processing`). Con esto **queda completo el flujo end-to-end de WhatsApp planeado hasta B08** (conectar → recibir → responder texto/media/plantilla → confirmación de entrega/lectura → reintento). `Message` sumó `MarkDelivered`/`MarkRead` (transiciones monótonas: un `delivered` tardío nunca degrada un `read`) y `ResetForRetry`. `InboundMessageProcessor` ahora sí actúa sobre los `InboundStatusUpdate` que venía parseando desde B03 (un id externo desconocido se ignora con warning, nunca es error). Nuevo `MessageNotRetryableException` (409) y `OutboundMessageService.RetryAsync`: solo reintenta un mensaje `Failed` cuyo error sea transitorio según `MetaErrorCatalog`, re-resolviendo la cuenta de canal por `RequireSendableConversationAsync(requireOpenWindow: false)` en vez de confiar en `Conversation.ChannelAccountId` a ciegas. Nuevo endpoint `POST /api/tenants/{tenantId}/messages/{messageId}/retry` (mismo patrón sin `conversationId` que `GET .../messages/{id}/media-url` de B06). Ver CLAUDE.md, sección "Estados de entrega y reintentos (ORB-B08)". 377 unitarias en verde; misma limitación de Docker para las de integración nuevas (`MessageStatusTests`). Con B01-B08 completos, lo que sigue (por pedido explícito del usuario) es: (1) guía paso a paso para configurar las variables de Meta y una base de datos real, y (2) recién después de eso, arrancar el trabajo en `orbita-front`.

**2026-09-08 (7)** — `ORB-B07` (ventana de 24 h y plantillas) completo, misma rama apilada. `Conversation.CanSendFreeForm` (ya existía desde B03) ahora sí se aplica: `SendTextAsync`/`SendMediaAsync` devuelven 409 (`ServiceWindowClosedException`) fuera de ventana, `SendTemplateAsync` no. `MessageTemplate` (alta local + `SyncFromMetaAsync` trae el estado real de Meta), `Message.TemplateVariablesJson` (columna nueva: el dispatcher necesita las variables crudas, no el texto ya renderizado, para llamarle a Meta con `type: "template"`). Nuevo permiso `ManageTemplates` (Owner/Admin). Nuevos endpoints `GET|POST /api/tenants/{tenantId}/templates`, `POST .../templates/sync`, `POST .../conversations/{id}/messages/template`. Ver CLAUDE.md, sección "Ventana de servicio y plantillas (ORB-B07)". 365 unitarias en verde. Pendiente conocido: sigue sin existir un `MutableTimeProvider` para probar la ventana cerrándose en un test de integración real (mismo gap que B06 con la expiración de URLs firmadas) — la lógica de ventana sí está cubierta a nivel unitario con `FixedTimeProvider`.

**2026-09-08 (6)** — `ORB-B06` (multimedia) completo, misma rama apilada. `Message.OutboundMedia`, `MediaMimeCatalog` (límites de tamaño calcados de Meta), `IMediaStorage`/`LocalFileMediaStorage` (disco local con guardas contra path traversal), `IMediaUrlSigner`/`MediaUrlSigner` (token Data Protection, es toda la autorización de `GET|PUT /api/media/{token}`, ambas rutas `[AllowAnonymous]`), nuevos endpoints `POST .../conversations/{id}/media/upload-url`, `POST .../conversations/{id}/messages/media`, `GET .../messages/{id}/media-url`. `IChannelAdapter` sumó `SendMediaAsync`/`DownloadMediaAsync`; `InboundMessageProcessor` descarga y guarda media entrante sin nunca perder el mensaje si la descarga falla (`message.media_failed`, sin PII). Ver CLAUDE.md, sección "Multimedia (ORB-B06)". 349 unitarias en verde; misma limitación de Docker para las nuevas de integración (`MediaControllerTests`, `MediaMessageFlowTests`). Pendiente conocido: no hay test de token realmente expirado (falta un `MutableTimeProvider`, que además hace falta para B07).

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
- [x] `ORB-A16` Sesión y organizaciones — `GET /api/auth/me` devuelve las membresías del usuario; segunda política RLS `own_memberships` sobre `memberships`
- [ ] `ORB-A13` Medición de consumo — necesita que exista Track C (agentes de IA) primero
- [ ] `ORB-A14` Límites del plan — depende de A12 (listo) y A13 (no)

Con esto, el track de Desarrollador 1 queda sin historias P1 pendientes que no dependan de otro track. Lo que sigue (`ORB-A13`/`ORB-A14`) necesita que Track C construya los agentes de IA primero, o (P2, `ORB-A11` ya cubierto) no hay más trabajo aislado obvio — la próxima sesión debería confirmar con el resto del equipo antes de inventar alcance nuevo aquí.

Track B (Canales y Bandeja — Desarrollador 2), en orden de ejecución acordado. Una rama por historia desde `develop`; la infraestructura externa (Lambda, SQS, Redis, Secrets Manager/KMS, R2, EventBridge) va detrás de ports con stand-ins locales, sin SDK de AWS todavía:

- [x] `ORB-B01` Conectar WhatsApp — rama `feature/whatsapp-channel-connect`, **sin app de Meta real conectada** (ver `CLAUDE.md`, sección "Channels")
- [x] `ORB-B02` Ingesta de webhooks — rama `feature/webhook-ingestion` (apilada sobre B01, ver nota arriba), `POST` en las mismas rutas de `/api/webhooks/whatsapp` + nuevas `/api/webhooks/instagram`, firma `X-Hub-Signature-256`, cola durable en Postgres (`inbound_webhook_events`) como stand-in de SQS
- [x] `ORB-B03` Normalización de entrantes — rama `feature/inbound-message-processing` (apilada sobre B02, ver nota arriba); mergeó `feature/d02-contactos` para reutilizar el `Contact` de Track D en vez de crear uno propio
- [x] `ORB-B04` Transactional Outbox — rama `feature/inbound-message-processing` (apilada sobre B03)
- [x] `ORB-B05` Envío de texto — rama `feature/inbound-message-processing` (apilada)
- [x] `ORB-B06` Media — rama `feature/inbound-message-processing` (apilada)
- [x] `ORB-B07` Ventana de 24 h y plantillas — rama `feature/inbound-message-processing` (apilada)
- [x] `ORB-B08` Estados de entrega — rama `feature/inbound-message-processing` (apilada); **con esto queda completo el flujo de WhatsApp planeado para esta tanda (B01-B08)**
- [ ] `ORB-B12` Bandeja · `ORB-B13` Vista de conversación · `ORB-B14` Tiempo real (reutiliza el `AddSignalR`/`CrmHub` de d02) · `ORB-B15` Asignación
- [ ] P1: `ORB-B09` Instagram (**arrancar el App Review de Meta ya**, tarda semanas) · `ORB-B11` `human_agent` · `ORB-B19` DLQ · `ORB-B16` Notas · `ORB-B17` Etiquetas · `ORB-B18` Respuestas rápidas
- [ ] P2: `ORB-B10` Comentarios y menciones de Instagram

Track C (Agentes de IA — foco actual):

- [x] `ORB-C01` Abstracción de proveedor de modelos
- [x] `ORB-C02` Base de conocimiento — el criterio "50 páginas en menos de 2 minutos" **sin medir**, necesita un modelo real conectado
- [x] `ORB-C03` Búsqueda semántica — el criterio "200 ms con 100.000 fragmentos" **sin medir**, y con un límite conocido del índice (ver abajo)
- [x] `ORB-C10` Constructor de agentes — solo el backend; las pantallas 2.5–2.8 son del frontend
- [x] `ORB-C11` Banco de pruebas — las trazas de 4 de las 5 herramientas esperan a `ORB-D05`/`ORB-B03`
- [x] `ORB-C13` Selección de modelo por tarea
- [x] `ORB-C12` Caché semántico — apagada por defecto; se enciende con un umbral por asistente
- [x] `ORB-C04` El agente responde — verificado con modelos reales contra Neon
- [x] `ORB-C05` El agente ejecuta acciones — `crear_oportunidad` y `mover_etapa`; `agendar_cita`/`escalar_a_humano` siguen no disponibles
- [x] `ORB-C06` Guardrails — temas bloqueados, límite de respuestas por ventana, bucles, y revisión de la respuesta
- [x] `ORB-C08` Enrutador — reglas ordenadas por tenant y horario de atención por asistente
- [x] `ORB-C09` Consumo de IA — `tools_called` y `retrieved_chunk_ids` en `ai_runs`; la facturación es `ORB-A13`, que no existe
- [ ] `ORB-C07` Traspaso a humano — **bloqueada por `ORB-B15`** (asignación a personas): sin cola humana, prometerle un traspaso al cliente sería mentirle

## Decisiones que ya se tomaron (no reabrir sin motivo)

Estas están documentadas con más detalle en `CLAUDE.md`, se listan aquí para que salten a la vista antes de tocar el área relacionada:

- El JWT de acceso viaja en cookie `httpOnly`/`SameSite=Lax`, no en header `Authorization` — para que funcione igual con el handshake de SignalR más adelante.
- El JWT solo lleva `sub` (id de usuario), nunca un claim de tenant. Cuál organización actúa una sesión se resuelve explícitamente en cada endpoint tenant-scoped, no se infiere del token.
- Las invitaciones toman el tenant de la ruta (`/api/tenants/{tenantId}/...`), no del JWT, para no necesitar nunca un "listar mis membresías cruzando tenants" que pelearía con Row-Level Security.
- Los permisos por rol viven en `RolePermissions` (`Owner`/`Admin`/`Agent`/`Viewer`), consultados a través de `ITenantAuthorizationService` — cualquier chequeo de rol nuevo pasa por ahí, no se repite inline.
- **Hay dos variables de sesión, no una.** `app.tenant_id` es la de siempre; `app.user_id` la agregó `ORB-A16` para poder preguntar "¿a qué organizaciones pertenezco?", que por definición no se puede responder con un tenant ya elegido. La segunda política sobre `memberships` (`own_memberships`, `FOR SELECT`) casa por esa variable, y **un solo método la setea** — `IUnitOfWork.QueryInUserScopeAsync`, siempre con el id del claim `sub`, nunca con uno que venga en la petición. Al ser `SET LOCAL` muere con su transacción, así que la política es inerte en cualquier otro camino. Si alguna vez hace falta setearla desde otro lado, revisar esto primero.
- **Row-Level Security bloquea toda lectura cuando no hay un tenant activo en la sesión** (cero filas, no "todas"). `InvitationToken`, `PasswordResetToken`, `RefreshToken` y `Subscription` se diseñan a propósito **sin** RLS/query filter por esto — se buscan por un id opaco externo antes de saber a qué tenant pertenecen. `audit_log` sí lleva RLS normal (siempre se lee con el tenant ya conocido), pero cualquier lectura después de que `EnsurePermissionAsync` ya cerró su propia transacción necesita su propio `QueryInTenantScopeAsync` — `SET LOCAL app.tenant_id` no sobrevive a la transacción que lo puso.
- **La política "exigir MFA a todo el equipo" (`Tenant.RequireMfaForMembers`) existe pero no se aplica todavía.** Aplicarla de verdad requiere saber, antes de emitir tokens, a qué tenant(s) pertenece quien inicia sesión — y toda lectura cruzando tenants está bloqueada a propósito por la RLS de `memberships` (devuelve cero filas sin un tenant en la sesión). Es la misma decisión pendiente que el JWT sin claim de tenant. No intentar resolverlo con un bypass de RLS.
- `AuditLogEntry` es la primera entidad con id `bigint` en vez de `Guid` (así lo pide orbita-schema.dbml) — no hereda de `Entity`. Es además la única tabla verdaderamente append-only: `orbita_app` tiene `UPDATE`/`DELETE` revocados sobre ella específicamente, a nivel de base de datos.
- El envío de correo real está pendiente en toda la plataforma (no hay proveedor conectado); se loguea el link en su lugar. Cuando se construya `Notifications`, ese es el reemplazo, no un parche aquí.
- **El modelo nunca se expone al frontend.** `ORB-C10` deriva `temperature`, `max_tokens`, `model` y `system_prompt` de tres ejes de producto (formal↔cercano, breve↔detallado, neutro↔entusiasta) y no los devuelve en ningún payload; el único lugar donde sí se muestran tokens y costo es el banco de pruebas de `ORB-C11`, que es una pantalla de diagnóstico. Afinar el mapeo es un cambio de backend sin release del frontend, que es exactamente para lo que se guardan los ejes en vez de los números.
- **Editar un agente escribe un borrador, no el agente.** `ai_agent_drafts` (una fila por agente, con su propia RLS) guarda lo no publicado; `publish` lo copia encima y lo borra. Publicar no enciende el agente y encender no publica.
- **La cola de indexado (`knowledge_indexing_queue`) es la única tabla de Track C sin RLS, a propósito**: se lee antes de saber el tenant, porque averiguarlo es justamente para qué se lee. Solo guarda dos ids y una fecha. Track B llegó a la misma conclusión para su propia cola.
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
- **OpenRouter ya tiene saldo (USD 5) y está verificado de punta a punta**: responden tanto la generación (`deepseek/deepseek-v4-flash`) como los embeddings (`openai/text-embedding-3-small`, 1536 dimensiones, que es exactamente lo que espera `KnowledgeChunk.EmbeddingDimensions`). Con esto quedan *medibles* los dos criterios de aceptación que estaban sin medir: el «50 páginas en menos de 2 minutos» de `ORB-C02` y el «200 ms con 100.000 fragmentos» de `ORB-C03`. Siguen sin medirse. Las pruebas automáticas siguen usando un embebedor falso determinista a propósito — una suite que gaste dinero por correrse no es una suite.
- **El índice HNSW no se usa hoy.** Al filtrar por `tenant_id`, Postgres prefiere filtrar por tenant y ordenar los sobrevivientes — correcto mientras el corpus de un cliente sea chico, y no cuando sean decenas de miles de fragmentos **por tenant**. Opciones documentadas en la migración `AddKnowledgeChunkHnswIndex`.
- **Riesgo de conflicto al mergear Track C.** Sus ramas tocan seis archivos compartidos con Track B/D: `Permission.cs`, `RolePermissions.cs`, `OrbitaDbContext.cs`, los dos `DependencyInjection.cs` y `GlobalExceptionHandler.cs`. Ya pasó con `ORB-A15` (claves duplicadas que compilaban y reventaban en runtime); conviene avisar al equipo antes de mergear.
- **`orbita-front` sigue en scaffold** — no hay cliente HTTP ni pantallas reales todavía, así que ningún endpoint de este repo tiene todavía un consumidor real más allá de las pruebas de integración.
- **`ORB-B01`: sin app de Meta real** — `Channels:Meta:*` está vacío; los clientes de Graph API (`MetaAuthClient`, `WhatsAppCloudApiClient`) nunca han hablado con Meta de verdad. Crear la app en Meta for Developers (producto WhatsApp) y arrancar el App Review para Instagram cuanto antes: es un plazo externo que no controlamos.
- **`ORB-B01`: pruebas de integración escritas pero no ejecutadas en la máquina de desarrollo** (sin Docker). Correr la suite completa con Docker antes de mergear la rama.
- **`ORB-B02`: `feature/webhook-ingestion` está apilada sobre `feature/whatsapp-channel-connect`, no sobre `develop`.** Antes de abrir su propio PR hay que mergear B01 a `develop` primero y luego rebasar esta rama — si se abre el PR tal cual, arrastra los 6 commits de B01. Mismo problema de Docker que B01 para `WhatsAppWebhooksControllerTests`. El test de carga/concurrencia del plan original no se escribió (ver CLAUDE.md).
- **`ORB-B03` mergeó `feature/d02-contactos` en vez de esperar que llegue a `develop`** (decisión del usuario, ver arriba). El `Contact` de Track D sigue usando sus propios nombres de columna (`phone`, `instagram_username`), que NO coinciden con el DBML (`phone_e164`, `ig_user_id`) — deuda de reconciliación sin resolver, documentada pero no arreglada. `Contact.InstagramUserId`/`ig_user_id` (añadido en B03) sí sigue el nombre del DBML. `NormalizePhone` ya canoniza a solo dígitos (sin `+`) para calzar con el `wa_id` de Meta.
- **`ORB-B03`: `TenantIsolationTests` no se extendió** para conversations/messages (el mecanismo de RLS ya está probado por otras tablas). Falta un test determinístico de fallo repetido del worker (`WorkerFailure_ThreeTimes_MarksDead` del plan original) — `InboundMessageFlowTests` usa polling con `Eventually` en vez de un adapter fake inyectado por `WithWebHostBuilder`.
- **B01-B08 y la épica C2 ya corrieron de verdad**: la suite completa está en verde contra Postgres real (Testcontainers) y el flujo entrante→respuesta se verificó contra Neon con los modelos reales. Lo que sigue sin probarse contra Meta de verdad es el envío saliente: **no hay app de Meta**, así que `WhatsAppCloudApiClient` nunca ha hablado con Graph API — los mensajes salen encolados y el despachador falla contra el fake. Crear la app de Meta sigue siendo el plazo externo más largo.
- **`ORB-C12`: no hay limpieza de `agent_answer_cache`.** Una entrada cuya huella ya no coincide no se devuelve nunca más, pero tampoco se borra: es peso muerto, no un riesgo de corrección. Hace falta un job de retención antes de volumen real, igual que para `outbox_events`.
- **La latencia p95 de `ORB-C04` (menos de 6 segundos) sigue sin medirse.** `ai_runs.latency_ms` es donde está el dato; en las corridas reales las llamadas de generación fueron de 2 a 5 segundos, pero nadie lo está midiendo de forma continua.
