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
- **Contactos** (`GET|POST /api/tenants/{tenantId}/contacts`, `GET|PATCH .../contacts/{contactId}`, `GET .../contacts/{contactId}/opportunities`, `GET|POST .../contact-fields`): ficha con campos custom (jsonb), deduplicación por teléfono o Instagram (409), y vínculo opcional de oportunidades vía `contactId`. El historial de conversación lo aporta Track B. Es `ORB-D02`.
- **Tablero de oportunidades** (`GET /api/tenants/{tenantId}/pipelines/{pipelineId}/board`, `POST .../opportunities`, `POST .../opportunities/{id}/move`, hub `/hubs/crm`): kanban con suma por etapa, filtros por responsable y fecha, movimiento idempotente por `eventId` y eco en vivo por SignalR. Es `ORB-D05`.
- **Pipelines y etapas** (`GET|POST /api/tenants/{tenantId}/pipelines`, `PATCH|DELETE .../pipelines/{pipelineId}`, etapas con create/update/reorder/delete): varios tableros por organización, etapas ordenables con marca de ganada/perdida, y al borrar una etapa hay que reubicar sus oportunidades. Cada registro de organización deja un pipeline `Ventas` por defecto. Es la implementación de `ORB-D04`. El tablero kanban (ORB-D05) todavía no mueve tarjetas.
- **Verificación en dos pasos** (`POST /api/auth/2fa/setup|confirm|disable`, `POST /api/auth/2fa/backup-codes/regenerate`): TOTP con QR (el backend entrega la URI `otpauth://`, el frontend dibuja el código), 10 códigos de respaldo de un solo uso, y el login exige el segundo factor cuando está activado. Es la implementación de `ORB-A11`. El owner puede exigir MFA a todo el equipo (`PATCH /api/tenants/{tenantId}/settings/mfa-policy`), pero esa política **todavía no se aplica en tiempo real** — hacerlo bien requiere resolver antes a qué tenant pertenece una sesión al momento de iniciar sesión, la misma decisión pendiente que dejó sin claim de tenant al JWT (ver `CLAUDE.md`).
- **Sesión y organizaciones** (`GET /api/auth/me`): devuelve quién inició sesión (id, correo, nombre) **y las organizaciones a las que pertenece**, cada una con su slug, su nombre y el rol de esa persona en ella. Existe porque el JWT deliberadamente no lleva claim de tenant y el `tenantId` viaja en la ruta: sin esto, un cliente recién autenticado —en un navegador que nunca usó— no tiene de dónde sacar a qué organización entrar. Con una sola organización entra directo, con varias muestra un selector, y con ninguna ofrece crear una. Solo salen las membresías **aceptadas y activas**: una invitación pendiente es una oferta, no un lugar donde se puede actuar, y listarla pondría en el selector una puerta que abre a un 403.

  Lo interesante es cómo se resuelve contra la Row-Level Security. La política de `memberships` filtra por `app.tenant_id`, así que la consulta "mis membresías" devolvía cero filas en silencio, porque justamente no hay tenant todavía. La solución es una **segunda política** (`own_memberships`, `FOR SELECT`) que casa por `app.user_id` — Postgres combina las permisivas con OR — más `IUnitOfWork.QueryInUserScopeAsync`, el único lugar del código que setea ese valor, siempre desde el claim `sub` y nunca desde la petición. Al ser `SET LOCAL`, el valor muere con la transacción, de modo que la política queda inerte en todos los demás caminos. Es la implementación de `ORB-A16`.

Y el arranque de **Channels** (track del Desarrollador 2):

- **Conectar WhatsApp** (`POST /api/tenants/{tenantId}/channels/whatsapp`, `GET .../channels`, `GET|DELETE .../channels/{id}`, `POST .../channels/{id}/verify`, y la verificación del webhook de Meta en `GET /api/webhooks/whatsapp[/{channelAccountId}]`): el dashboard corre el Embedded Signup de Meta y envía el código OAuth; el backend lo cambia por el token, lo guarda cifrado detrás de un puerto de secretos (stand-in local de Secrets Manager/KMS), registra un callback de webhook por cuenta y marca la cuenta como conectada cuando Meta lo verifica. Estado y aviso de caducidad del token en la respuesta; desconectar borra la credencial y conserva el historial. Es la implementación de `ORB-B01`. **No hay una app de Meta real conectada todavía** — `Channels:Meta:*` está vacío en `appsettings`; ver la sección "Channels" de `CLAUDE.md`.
- **Ingesta de webhooks** (`POST /api/webhooks/whatsapp[/{channelAccountId}]`, `GET|POST /api/webhooks/instagram`): verifica `X-Hub-Signature-256`, resuelve la cuenta desde la ruta o desde el propio payload, descarta duplicados y encola el payload crudo en `inbound_webhook_events` (stand-in de SQS, `ON CONFLICT DO NOTHING`) — sin interpretarlo todavía. Es `ORB-B02`.
- **Normalización de entrantes**: un worker en segundo plano drena la cola anterior, interpreta el payload de WhatsApp (texto, media, ubicación, reacciones, botones, tipos no soportados) y crea/actualiza contacto, conversación (particionada por canal + contacto) y mensaje en una sola unidad de trabajo por evento. Tabla `messages` particionada por fecha desde el día uno. Es `ORB-B03`.
- **Outbox transaccional**: cada mensaje entrante y cada conversación nueva quedan como eventos de integración (`message.received`, `conversation.opened`, sin PII) en la misma transacción que el cambio de dominio; un worker aparte los publica cada 500 ms a los handlers registrados en el proceso. Es `ORB-B04`.
- **Envío de mensajes de texto** (`POST /api/tenants/{tenantId}/conversations/{conversationId}/messages`): persist-first (el mensaje existe en estado `Queued` antes de intentar enviarlo), cola de envío con rate limit de 80/s por cuenta, y errores de Meta clasificados como transitorios (reintento con backoff) o permanentes (falla con mensaje en español). Es `ORB-B05`.
- **Multimedia** (`POST .../conversations/{id}/media/upload-url`, `POST .../conversations/{id}/messages/media`, `GET .../messages/{id}/media-url`): URLs firmadas de subida/descarga (15 min, la firma es toda la autorización), almacenamiento local con las mismas claves que usaría R2, y descarga de media entrante desde Meta sin perder el mensaje si la descarga falla. Es `ORB-B06`.
- **Ventana de 24 h y plantillas** (`GET|POST /api/tenants/{tenantId}/templates`, `POST .../templates/sync`, `POST .../conversations/{id}/messages/template`): fuera de la ventana de servicio de WhatsApp solo se puede escribir con una plantilla aprobada; el registro de plantillas es local (el alta real ocurre en Meta Business Manager) y se sincroniza el estado real con `sync`. Es `ORB-B07`.
- **Estados de entrega y reintentos** (`POST /api/tenants/{tenantId}/messages/{messageId}/retry`): los webhooks de estado de Meta (`sent`/`delivered`/`read`/`failed`) actualizan el mensaje enviado correspondiente sin poder retroceder un estado ya avanzado; un mensaje que falló con un error transitorio (p. ej. límite de envío) se puede reintentar, uno que falló por un error permanente (p. ej. fuera de ventana) no. Con esto queda completo el flujo de WhatsApp de punta a punta (conectar → recibir → responder → confirmar entrega). Es `ORB-B08`.

Y el arranque de **Agentes de IA** (track del Desarrollador 3, que es el foco actual del repo):

- **Abstracción de proveedor de modelos** (`ILlmProvider`): un puerto con completion, streaming, function calling y embeddings, con dos implementaciones — un adaptador para el formato OpenAI (`/v1/chat/completions`), que es el proveedor principal y se apunta a OpenAI, OpenRouter, Groq o vLLM cambiando solo `Ai:Providers:openai-compatible:BaseUrl` y `ApiKey`, y otro para Ollama, que queda como respaldo de chat. **El modelo se resuelve por proveedor y por tarea** (`Classify`/`Draft`/`Embed`) vía `ILlmModelSelector`, porque los ids no son portables: pedirle a Ollama un modelo de OpenRouter fallaría siempre, y sin eso la conmutación no funcionaría. `ResilientLlmProvider` añade reintento con retroceso exponencial y conmutación al proveedor alterno, distinguiendo un proveedor caído (5xx/429/timeout: se reintenta) de una petición mal formada (4xx: se propaga sin reintentar, porque otro proveedor daría el mismo error). Toda llamada devuelve tokens, costo y latencia (`LlmUsage`), y el costo se calcula por modelo, no por proveedor, porque un mismo gateway sirve varios modelos a tarifas distintas. Es la implementación de `ORB-C01`.

- **Base de conocimiento** (`POST|GET /api/tenants/{tenantId}/ai-agents/{agentId}/knowledge`, `.../knowledge/text` para texto pegado, `.../reindex`, `DELETE`): subida de PDF, DOCX, TXT y Markdown, troceado con solapamiento por párrafos, y embeddings generados en segundo plano con el estado del documento visible (`Pending`/`Processing`/`Indexed`/`Failed`, con motivo legible en español cuando falla). Los vectores viven en la misma Postgres vía pgvector, no en una base vectorial aparte. Cada llamada de embedding queda registrada en `ai_runs` en la misma transacción que los fragmentos que produjo. Al registrar una organización se siembra un asistente por defecto **deshabilitado**, para que nadie empiece con la pantalla vacía. Es la implementación de `ORB-C02`. La lista pagina con cursor (`{items, nextCursor}`), que es la forma acordada con el frontend para todas las listas del producto.

- **Búsqueda semántica** (`POST /api/tenants/{tenantId}/ai-agents/{agentId}/knowledge/search`): busca por similitud de coseno sobre pgvector, filtrada por tenant **y por agente** (dos asistentes de la misma organización pueden tener material distinto a propósito), y devuelve cada fragmento con su puntaje y su fuente — que es lo que el banco de pruebas de `ORB-C11` necesita para mostrar de dónde salió una respuesta. Cada búsqueda queda registrada en `ai_runs`, porque embeber la pregunta cuesta tokens como cualquier otra llamada al modelo. Es la implementación de `ORB-C03`.

- **Constructor de agentes** (`GET|POST /api/tenants/{tenantId}/ai-agents`, `GET|PATCH|DELETE .../{agentId}`, `POST .../publish`, `DELETE .../draft`, `PATCH .../enabled`, `GET /api/ai-tools`): el dueño escribe nombre, personalidad e instrucciones, y mueve tres sliders — formal↔cercano, breve↔detallado, neutro↔entusiasta. **La pantalla nunca ve jerga de modelos**: `temperature`, `max_tokens`, `model` y `system_prompt` son columnas reales pero no salen en ningún payload, y el backend los deriva de los tres ejes (los dos primeros mueven la temperatura, el tercero el presupuesto de tokens). Hay una prueba de integración que falla si alguno de esos cuatro nombres aparece en el JSON, porque la regla de producto es más fácil de romper por descuido que de detectar. **Guardar no toca al asistente que está atendiendo**: `PATCH` escribe un borrador (`ai_agent_drafts`, una fila por agente, con su propia RLS) y solo `publish` lo copia encima; publicar no enciende el agente y encender no publica, que son dos decisiones distintas. Un borrador idéntico a lo publicado no cuenta como cambio pendiente, para que el badge de la pantalla no mienta. El catálogo de herramientas es un endpoint, no una constante del frontend: cuatro de las cinco dependen de módulos que todavía no existen y se declaran `isAvailable: false` con el motivo, en vez de exigir un release del frontend por cada una que aterrice. Es la implementación de `ORB-C10` (solo el backend; las pantallas 2.5–2.8 son del frontend).

- **Banco de pruebas** (`POST /api/tenants/{tenantId}/ai-agents/{agentId}/test-chat`): el dueño conversa con su propio asistente antes de que lo haga un cliente, y ve la respuesta junto con los fragmentos que se recuperaron (con puntaje y fuente), qué herramientas corrieron y qué costó el intercambio. **Prueba el borrador cuando hay uno**, que es justamente para lo que sirve tener borradores. No persiste nada más allá de la medición en `ai_runs` — el historial lo manda quien llama, así que no necesita conversaciones ni mensajes y no depende de Track B; `ai_runs.conversation_id` es nullable precisamente para esto. Un proveedor caído responde `502`, no `500`: la petición estaba bien y esta API está bien. Es la implementación de `ORB-C11`. Las trazas de las otras cuatro herramientas llegarán cuando existan los módulos que las sostienen (`ORB-D05`, `ORB-B03`).

- **El agente responde** (sin endpoint: ocurre solo, cuando un cliente escribe): el asistente reacciona al evento `message.received` del outbox, toma las últimas 20 vueltas de la conversación como contexto, recupera los fragmentos relevantes de su propia base de conocimiento, y contesta en el idioma en que le escribieron. Si no tiene la información, lo dice y ofrece pasar con una persona, en vez de inventar. La respuesta sale por la misma cola de envío que usa un humano, así que hereda el persist-first, los reintentos y el límite de 80/s por cuenta de `ORB-B05`, y queda registrada en `ai_runs` con su modelo, tokens, costo y latencia. Se queda callado —y sin gastar una sola llamada al modelo— cuando no hay asistente habilitado, cuando alguien del equipo ya tomó la conversación, cuando la ventana de 24 h está cerrada o cuando no hay texto que contestar (una foto sin pie, por ejemplo). La conversación registra qué asistente la atiende la primera vez que contesta, y no lo cambia después: elegir entre varios asistentes es `ORB-C08`. Es la implementación de `ORB-C04`.

- **Casos de prueba guardados** (`GET|POST /api/tenants/{tenantId}/ai-agents/{agentId}/test-cases`, `DELETE .../test-cases/{id}`): el dueño guarda un intercambio con nombre y lo vuelve a correr después de cambiar su asistente, que es la única razón por la que un caso guardado sirve — y por la que vive en el servidor y no en el navegador, que lo perdería justo cuando hace falta (otra máquina, otro navegador, datos del sitio limpiados). Volver a correrlo no es un endpoint nuevo: la pantalla lee el caso y lo manda al banco de pruebas que ya existe, así hay un solo lugar donde se arma el prompt. Hasta 20 casos por asistente, 40 mensajes por caso, y el nombre lo escribe la persona. Completa `ORB-C11`.

- **Guardrails** (`PUT /api/tenants/{tenantId}/ai-agents/{agentId}/guardrails`): el dueño escribe de qué prefiere que su asistente no hable y con qué frase responder cuando pase. Un tema bloqueado se responde con esa frase sin gastar una llamada al modelo; un bucle de respuestas o demasiadas respuestas en la misma ventana de 24 h se responden con silencio, porque en esos dos casos el asistente ya dijo de más. Lo que el modelo produjo también se revisa antes de salir: vacío, más largo de lo que WhatsApp acepta, repetido, o transcribiendo sus propias instrucciones. Los temas coinciden **por palabra completa** y sin acentos, para que "precio" no dispare en "apreciamos" ni "talla" en "pantalla" — el costo de un falso positivo es un asistente mudo que nadie puede diagnosticar desde afuera. Cada bloqueo deja un evento `agent.reply_blocked` con el motivo. A diferencia del resto de la configuración, **aplica al guardar, sin publicar**: un ajuste cuyo propósito es que el asistente deje de hablar de algo no puede esperar a que alguien apriete Publicar. Es la implementación de `ORB-C06`.

- **El agente ejecuta acciones** (sin endpoint propio: ocurre dentro de la conversación): el asistente puede registrar una oportunidad de venta y moverla de etapa mientras atiende. Las herramientas actúan **solo sobre el contacto de la conversación**, nunca sobre un id que el modelo proponga — ese sería el único camino hacia el registro de otra organización. El bucle está acotado a tres rondas de herramientas más una llamada final sin herramientas, así el cliente siempre recibe texto y una respuesta cuesta a lo sumo cuatro llamadas. Una herramienta que falla no rompe la conversación: el modelo recibe una frase en español que puede transmitir, y el error interno nunca llega al cliente. Cada acción queda en la bitácora con `actor_type = AiAgent`. `agendar_cita` sigue declarada **no disponible**: no hay agenda detrás, y ofrecerla sería mentirle al cliente. (`escalar_a_humano` lo estuvo por la misma razón hasta que `ORB-C07` construyó la cola.) Es la implementación de `ORB-C05`.

- **Registro de consumo de IA**: `ai_runs` guarda además qué herramientas llamó cada corrida y qué fragmentos de conocimiento se usaron. Un run por llamada al modelo, así que en un bucle de herramientas la ronda que las pidió las lleva y la siguiente no — que es literalmente lo que pasó, y lo que la facturación por acción necesita. Los fragmentos se registran solo en la primera ronda: entraron en el prompt de todas, pero se recuperaron una vez. Es la parte de `ORB-C09` que no depende de `ORB-A13`.

- **Enrutador** (`GET|PUT /api/tenants/{tenantId}/routing/rules`, `PUT .../ai-agents/{agentId}/business-hours`): una lista ordenada de reglas decide quién atiende una conversación nueva — por canal, por palabra clave, hacia un asistente o hacia el equipo. El orden del arreglo es el orden de evaluación, y gana la primera regla que casa. Una conversación que ya tiene asistente se queda con él: las reglas eligen quién la toma, no la rebotan a mitad de camino. Cada asistente tiene su horario de atención, evaluado en la zona horaria de la organización, y fuera de horario puede contestar igual o dejarla para el equipo. Las reglas deciden **quién**; el horario decide **si ese asistente contesta ahora**. Es la implementación de `ORB-C08`.

- **Caché semántica de respuestas** (`GET|PUT /api/tenants/{tenantId}/ai-agents/{agentId}/semantic-cache`): si otro cliente ya preguntó lo mismo, el asistente reutiliza esa respuesta en vez de pagar otra llamada al modelo. Apagada por defecto, y se enciende eligiendo qué tan libre puede ser la reutilización —cautelosa, equilibrada o amplia—: **el umbral numérico nunca sale al frontend**, por la misma razón que no sale `temperature`. Solo se reutiliza la primera respuesta de una conversación, y solo si no llamó ninguna herramienta: cualquier otra fue moldeada por las vueltas anteriores, y repetirle a alguien una respuesta que registró el pedido de otro sería afirmar algo que nunca pasó para él. Se invalida sola: subir, reindexar o borrar un documento —o publicar instrucciones nuevas— retira toda respuesta anterior. El porcentaje de aciertos se lee de `ai_runs`, sin contadores nuevos. Es la implementación de `ORB-C12`.

- **Selección de modelo por tarea y por tenant** (`GET /api/tenants/{tenantId}/ai-models/{providerName}`, `PUT|DELETE .../{task}`): un modelo barato para clasificar y uno bueno para redactar, con la posibilidad de que cada organización sobreescriba el suyo. Resuelve en dos capas (preferencia del tenant → configuración), con caché en memoria de un minuto e invalidación al escribir, para no pagar una consulta por llamada al modelo. El dato para medir el ahorro ya está en `ai_runs` (modelo y costo por llamada); el panel que lo muestra es `ORB-D11`. Es la implementación de `ORB-C13`.

- **Traspaso a una persona** (`GET /api/tenants/{tenantId}/handoffs`, `POST .../conversations/{conversationId}/return-to-assistant`): cuando el cliente pide hablar con alguien, se nota frustrado, pregunta por un tema que el dueño bloqueó, o el propio asistente decide que no puede ayudar (herramienta `escalar_a_humano`), la conversación sale del asistente y queda en una cola esperando a una persona, con un resumen de lo conversado escrito en el momento del traspaso. El asistente no vuelve a contestar esa conversación hasta que alguien del equipo se la devuelve, aunque el cliente siga escribiendo. La cola se sirve de la espera más antigua a la más reciente, pagina con cursor y dice cuántas hay en total — es justo la lista que crece el día que el equipo no da abasto. El resumen cuesta una llamada barata al modelo por traspaso y, si el proveedor está caído, el traspaso ocurre igual sin resumen. No asigna a nadie en particular: elegir quién la toma es `ORB-B15`. Es la implementación de `ORB-C07`.

Todavía no está el resto de la bandeja (`ORB-B12`–`ORB-B15`: vista de conversación con paginado, tiempo real, asignación a una persona concreta) ni Instagram como canal completo (`ORB-B09`/`ORB-B10`, detrás del App Review de Meta) — se construye incrementalmente replicando el mismo patrón. El backlog completo de 61 historias está en [`../docs/Orbita-Historias-de-Usuario.pdf`](../docs/Orbita-Historias-de-Usuario.pdf).

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
| D — CRM, Contenido y Analítica | Desarrollador 4 | CRM, Campaigns, Analytics, sitio público | `ORB-D04` (pipelines), `ORB-D05` (tablero), `ORB-D02` (contactos) |

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

## Usar una base administrada en vez de Docker

Si no querés depender de Docker para tener base de datos (por ejemplo apuntando a
Neon, que es lo que usa el entorno de desarrollo/integración compartido), el
cambio es de configuración, no de código:

```bash
# 1. Una sola vez por base: extensiones y rol dueño.
#    Se corre con el rol administrador que dé el proveedor.
psql "<cadena del rol administrador>" -v orbita_password="'<contraseña>'" \
     -f scripts/init-managed-db.sql

# 2. Migrar como `orbita`, por el endpoint DIRECTO (sin pooler).
dotnet tool run dotnet-ef database update --project Orbita.Infrastructure \
    --startup-project Orbita.Api --connection "<cadena de orbita>"

# 3. Apuntar la app al rol de runtime, copiando .env.example a .env y llenando
#    ConnectionStrings:Postgres con la cadena de `orbita_app`.
cp .env.example .env
```

`.env` está en `.gitignore` y le gana a `appsettings.Development.json`, así que
cada quien apunta su máquina a donde quiera sin tocar archivos versionados.

Dos cosas que no son opcionales:

- **La app se conecta como `orbita_app`, nunca como el rol administrador del
  proveedor.** En Neon, `neondb_owner` tiene `BYPASSRLS`: usarlo apaga el
  aislamiento entre organizaciones sin dar ningún error.
- **Las migraciones van por el endpoint directo, la app por el del pooler.** El
  pooler (PgBouncer en modo transacción) es compatible con el `SET LOCAL
  app.tenant_id` que abre `IUnitOfWork` dentro de cada transacción, pero no con
  una sesión larga de DDL.

Seguís necesitando Docker para `dotnet test`: las pruebas de integración levantan
su propio Postgres efímero con Testcontainers y no usan esta base.

## Cargar datos de ejemplo

```bash
psql "<cadena de orbita>" -f scripts/seed-dev.sql
```

Deja tres organizaciones con equipo, canales, ~1.800 contactos, ~840
conversaciones con ~20.000 mensajes, tablero comercial, asistentes de IA y su
base de conocimiento. Corre como el rol **dueño** (`orbita`), porque tiene que
insertar filas de varias organizaciones en una sola transacción y la RLS no se
aplica al dueño de la tabla.

Es idempotente: borra sus propias organizaciones por id fijo antes de empezar, y
no toca ninguna fila que no haya creado él (el catálogo de `plans`, por ejemplo,
lo siembra una migración y el seed solo lo referencia).

Todas las personas de ejemplo entran con la contraseña `Orbita2026!`:

| Correo | Organización | Rol |
|---|---|---|
| `manuela.rios@orbita.demo` | Panadería La Espiga (+ Clínica Sonrisa) | Owner (y Viewer) |
| `andres.gomez@orbita.demo` | Panadería La Espiga (+ Boutique Marea) | Admin (y Agent) |
| `valentina.cruz@orbita.demo` | Panadería La Espiga | Agent |
| `paula.restrepo@orbita.demo` | Panadería La Espiga | Viewer |
| `carolina.duque@orbita.demo` | Clínica Sonrisa | Owner |
| `lucia.ferrer@orbita.demo` | Boutique Marea | Owner |

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
