# HANDOFF

Documento de traspaso de contexto para quien retome este repo (otro desarrollador, u otra sesión de Claude Code). Objetivo: que puedas seguir trabajando sin releer todo el historial de commits. Se actualiza al terminar cada historia — no es una bitácora histórica, es una foto del momento.

Para las reglas de arquitectura/negocio vinculantes (que no cambian historia a historia), ver [`CLAUDE.md`](./CLAUDE.md). Este archivo es sobre *estado*, `CLAUDE.md` es sobre *reglas*.

## Última actualización

**2026-09-14 (integración)** — Se trae `develop` (con Track C ya mergeado, ver nota siguiente) a `feature/webhook-ingestion` y a `feature/whatsapp-channel-connect` (Track B, `ORB-B01`/`ORB-B02`). El choque real fue el ya conocido de `Permission.cs`/`RolePermissions.cs` (claves de Track B junto a `ManageAiAgents` de Track C) más `OrbitaDbContext.cs` (DbSets de ambos lados) — resueltos conservando ambos lados en los dos casos.

**2026-09-14** — **Track C entero está mergeado en `develop`**: `C01`, `C02`, `C03`, `C10`, `C11` y `C13`, en ese orden, como un stack de PRs encadenadas (#22 a #28). Y con este merge entra también `ORB-A16` (sesión y organizaciones), que iba aparte porque es de Track A.

`ORB-A16` se escribió desde la sesión de Track C, con autorización explícita del equipo, porque el frontend estaba bloqueado —no se podía entrar al producto en un navegador nuevo— y ningún otro track lo iba a tomar ese día. Toca `memberships` y el `UnitOfWork`, así que quien lleve Track A debería revisarla aunque ya esté adentro.

Al juntar las dos cosas hubo tres choques, todos resueltos acá: `IUnitOfWork` y `UnitOfWork` (cada lado agregó un método distinto en el mismo lugar, se conservan los cuatro) y —el que **git no marca como conflicto**— el `PassThroughUnitOfWork` de `TenantAwareLlmModelSelectorTests`, que implementa `IUnitOfWork` a mano y deja de compilar en cuanto aparece un método nuevo en la interfaz. Vale la pena recordarlo: cualquier rama con una implementación propia de esa interfaz se rompe en silencio al mergear, y el síntoma aparece en un test que no tiene nada que ver con el cambio.

El estado detallado de Track C está en `local/checklist-track-c.md`, y el contrato acordado con el frontend en `local/coordinacion-front-back.md` (ambos fuera de git).

**2026-09-08 (2)** — `ORB-B02` (ingesta de webhooks) en la rama `feature/webhook-ingestion`, **ramificada desde `feature/whatsapp-channel-connect` (B01), no desde `develop`** — desviación deliberada del plan original: el desarrollador quiere probar la API completa (WhatsApp connect + webhooks) en Yaak/Scalar antes de abrir el PR de B01 a `develop`, así que B02 se apiló sobre B01 en vez de esperar el merge. **Quien retome tiene que hacer rebase de `feature/webhook-ingestion` sobre `develop` (después de que B01 se mergee) antes de abrir su propio PR** — tal como está, su diff incluye todos los commits de B01. Ver "Track B" abajo para el detalle de lo implementado. Misma limitación de Docker que B01: las 5 pruebas de integración nuevas (`WhatsAppWebhooksControllerTests`) compilan pero no se ejecutaron; las 4 de `MetaWebhookSignatureVerifierTests` (sin base de datos) sí corrieron y pasan. Las 237 unitarias están en verde. El test de carga descrito en el plan original (concurrencia con payloads duplicados) no se escribió — ver CLAUDE.md, sección "Ingestión de webhooks (ORB-B02)".

**2026-09-08 (1)** — Arranca el Track B (Canales y Bandeja) en este repo con `ORB-B01` (conectar WhatsApp) en la rama `feature/whatsapp-channel-connect`. El plan detallado de las 19 historias del track (orden, ramas, ports/stand-ins, dependencias con Track D) quedó acordado antes de empezar; ver la sección "Track B" abajo. La máquina donde se desarrolló `ORB-B01` **no tenía Docker**, así que las 11 pruebas de integración nuevas (`ChannelsControllerTests`, `ChannelCredentialStoreTests`) compilan pero no se ejecutaron ahí — quien retome debe correr `dotnet test Orbita.slnx` con Docker antes de mergear. Las 215 unitarias sí están en verde.

La nota anterior, del **2026-09-07**: `ORB-A08`, `ORB-A10`, `ORB-A11`, `ORB-A12` y `ORB-A15` quedaron mergeadas en `develop` en ese orden, y el merge de `ORB-A15` dejó `RolePermissions.cs` con claves de diccionario duplicadas (compilaban y reventaban en runtime) más una referencia duplicada en el `.csproj` de Infrastructure, ya corregidas en `develop`.

## Qué está implementado

Ver la sección "Qué hay implementado hoy" en [`README.md`](./README.md) — se mantiene sincronizada ahí, no se duplica aquí.

Track A (Plataforma, Identidad y Facturación — dueño de este repo):

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
- [ ] `ORB-B03` Normalización de entrantes — **bloqueada hasta que `origin/feature/d02-contactos` (Track D) esté en `develop`**: reutiliza su `Contact`, no crea uno propio
- [ ] `ORB-B04` Transactional Outbox
- [ ] `ORB-B05` Envío de texto · `ORB-B06` Media · `ORB-B07` Ventana de 24 h y plantillas
- [ ] `ORB-B12` Bandeja · `ORB-B13` Vista de conversación · `ORB-B14` Tiempo real (reutiliza el `AddSignalR`/`CrmHub` de d02) · `ORB-B15` Asignación
- [ ] P1: `ORB-B08` Estados de entrega · `ORB-B09` Instagram (**arrancar el App Review de Meta ya**, tarda semanas) · `ORB-B11` `human_agent` · `ORB-B19` DLQ · `ORB-B16` Notas · `ORB-B17` Etiquetas · `ORB-B18` Respuestas rápidas
- [ ] P2: `ORB-B10` Comentarios y menciones de Instagram

Track C (Agentes de IA — foco actual):

- [x] `ORB-C01` Abstracción de proveedor de modelos
- [x] `ORB-C02` Base de conocimiento — el criterio "50 páginas en menos de 2 minutos" **sin medir**, necesita un modelo real conectado
- [x] `ORB-C03` Búsqueda semántica — el criterio "200 ms con 100.000 fragmentos" **sin medir**, y con un límite conocido del índice (ver abajo)
- [x] `ORB-C10` Constructor de agentes — solo el backend; las pantallas 2.5–2.8 son del frontend
- [x] `ORB-C11` Banco de pruebas — las trazas de 4 de las 5 herramientas esperan a `ORB-D05`/`ORB-B03`
- [x] `ORB-C13` Selección de modelo por tarea
- [ ] `ORB-C12` Caché semántico — desbloqueada, sin empezar
- [ ] `ORB-C04` El agente responde — necesita `ORB-B03` (mensajes, Track B)
- [ ] `ORB-C05` El agente ejecuta acciones — necesita `ORB-D05` (oportunidades, Track D)
- [ ] `ORB-C06` Guardrails · `ORB-C07` Traspaso a humano · `ORB-C08` Enrutador · `ORB-C09` Consumo de IA — encadenadas detrás de C04

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
- **Sin saldo en OpenRouter.** La API key está configurada y es válida, pero la cuenta está en USD 0: todo modelo de pago devuelve `402` y **no existen embeddings gratuitos**, así que nada de `ORB-C02`/`ORB-C03` puede indexar ni buscar contra un modelo real. Las pruebas pasan con un embebedor falso determinista. Con USD 5 sobra (indexar un PDF de 50 páginas cuesta ~USD 0.0007).
- **El índice HNSW no se usa hoy.** Al filtrar por `tenant_id`, Postgres prefiere filtrar por tenant y ordenar los sobrevivientes — correcto mientras el corpus de un cliente sea chico, y no cuando sean decenas de miles de fragmentos **por tenant**. Opciones documentadas en la migración `AddKnowledgeChunkHnswIndex`.
- **Riesgo de conflicto al mergear Track C.** Sus ramas tocan seis archivos compartidos con Track B/D: `Permission.cs`, `RolePermissions.cs`, `OrbitaDbContext.cs`, los dos `DependencyInjection.cs` y `GlobalExceptionHandler.cs`. Ya pasó con `ORB-A15` (claves duplicadas que compilaban y reventaban en runtime); conviene avisar al equipo antes de mergear.
- **`orbita-front` sigue en scaffold** — no hay cliente HTTP ni pantallas reales todavía, así que ningún endpoint de este repo tiene todavía un consumidor real más allá de las pruebas de integración.
- **`ORB-B01`: sin app de Meta real** — `Channels:Meta:*` está vacío; los clientes de Graph API (`MetaAuthClient`, `WhatsAppCloudApiClient`) nunca han hablado con Meta de verdad. Crear la app en Meta for Developers (producto WhatsApp) y arrancar el App Review para Instagram cuanto antes: es un plazo externo que no controlamos.
- **`ORB-B01`: pruebas de integración escritas pero no ejecutadas en la máquina de desarrollo** (sin Docker). Correr la suite completa con Docker antes de mergear la rama.
- **`ORB-B02`: `feature/webhook-ingestion` está apilada sobre `feature/whatsapp-channel-connect`, no sobre `develop`.** Antes de abrir su propio PR hay que mergear B01 a `develop` primero y luego rebasar esta rama — si se abre el PR tal cual, arrastra los 6 commits de B01. Mismo problema de Docker que B01 para `WhatsAppWebhooksControllerTests`. El test de carga/concurrencia del plan original no se escribió (ver CLAUDE.md).
- **`ORB-B03` depende de Track D**: `Contact` vive en `origin/feature/d02-contactos` (sin mergear). No arrancar B03 hasta que esté en `develop`; los nombres de columna de ese `Contact` (`phone`, `instagram_username`) no coinciden con el DBML (`phone_e164`, `ig_user_id`) y hay que acordar la canonización del teléfono (sin `+`, como el `wa_id` de Meta).
