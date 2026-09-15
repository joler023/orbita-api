-- =============================================================================
-- Datos de desarrollo para Órbita.
--
-- Para qué sirve: dejar la base de desarrollo/integración con volumen realista
-- (tres organizaciones, equipos, canales, contactos, bandeja, tablero comercial
-- y asistentes de IA con su base de conocimiento) para poder trabajar el
-- frontend y probar pantallas sin tener que crear todo a mano.
--
-- Cómo se corre: con el rol DUEÑO de las tablas (`orbita`), nunca con
-- `orbita_app`. La Row Level Security no se le aplica al dueño, que es
-- justamente lo que deja insertar filas de varias organizaciones en una sola
-- transacción. Con `orbita_app` esto insertaría cero filas sin dar error.
--
--     psql "$ConnectionStrings:PostgresAdmin" -f scripts/seed-dev.sql
--
-- Es idempotente: empieza borrando sus propias organizaciones por id fijo, así
-- que se puede volver a correr todas las veces que haga falta. No toca ninguna
-- fila que no haya creado él mismo.
--
-- Todas las personas de ejemplo comparten la contraseña `Orbita2026!`, con un
-- hash Argon2id distinto cada una (mismo formato que Argon2PasswordHasher).
-- Los correos terminan en @orbita.demo, que no es un dominio real.
--
-- Lo que este archivo NO hace, a propósito:
--   * No inventa credenciales de canal. `credentials_ref` apunta a una
--     referencia local que no existe en `channel_credentials`, así que ningún
--     envío real a Meta va a funcionar — no hay app de Meta configurada igual.
--   * Los embeddings de `knowledge_chunks` son vectores aleatorios. Sirven para
--     tener volumen y para que el índice HNSW tenga con qué trabajar, pero la
--     búsqueda semántica no va a devolver resultados con sentido hasta que se
--     reindexe con un modelo de embeddings real.
-- =============================================================================

BEGIN;

-- Semilla fija: dos corridas producen exactamente los mismos datos.
SELECT setseed(0.4242);

-- -----------------------------------------------------------------------------
-- 0. Limpieza de una corrida anterior
--
-- En orden de dependencia. Casi todas las claves foráneas a `tenants` son ON
-- DELETE CASCADE, pero unas pocas son RESTRICT (conversations →
-- channel_accounts, ai_runs → ai_agents, opportunities → pipelines/stages) y el
-- orden en que Postgres resuelve un cascade mixto no está garantizado, así que
-- se borra explícito.
-- -----------------------------------------------------------------------------
CREATE TEMP TABLE seed_tenants (id uuid PRIMARY KEY) ON COMMIT DROP;
INSERT INTO seed_tenants (id) VALUES
    ('a0000000-0000-4000-8000-000000000001'),
    ('a0000000-0000-4000-8000-000000000002'),
    ('a0000000-0000-4000-8000-000000000003');

DELETE FROM messages                  WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM conversations             WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM ai_runs                   WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM knowledge_indexing_queue  WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM knowledge_chunks          WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM knowledge_docs            WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM ai_agent_drafts           WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM ai_agents                 WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM opportunities             WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM pipeline_stages           WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM pipelines                 WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM contacts                  WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM contact_field_definitions WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM message_templates         WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM channel_accounts          WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM outbound_message_jobs     WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM outbox_events             WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM audit_log                 WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM tenant_model_preferences  WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM subscriptions             WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM memberships               WHERE tenant_id IN (SELECT id FROM seed_tenants);
DELETE FROM users                     WHERE email LIKE '%@orbita.demo';
DELETE FROM tenants                   WHERE id IN (SELECT id FROM seed_tenants);

-- Ojo: `plans` NO se toca. Las tres filas del catálogo (starter/growth/scale)
-- las siembra la migración AddBillingPlansAndSubscriptions con ids fijos, así
-- que son datos de producto, no datos de ejemplo. Este archivo solo las
-- referencia por su id.

-- -----------------------------------------------------------------------------
-- 1. Organizaciones
--
-- El país importa: SubscriptionService elige Wompi para CO y Stripe para el
-- resto, así que estas tres cubren los dos caminos de cobro.
-- -----------------------------------------------------------------------------
INSERT INTO tenants (id, slug, name, country_code, timezone, locale, is_active, created_at, updated_at, require_mfa_for_members) VALUES
    ('a0000000-0000-4000-8000-000000000001', 'panaderia-la-espiga', 'Panadería La Espiga', 'CO', 'America/Bogota',      'es-CO', true, now() - interval '95 days', now() - interval '3 days',  false),
    ('a0000000-0000-4000-8000-000000000002', 'clinica-sonrisa',     'Clínica Sonrisa',     'MX', 'America/Mexico_City', 'es-MX', true, now() - interval '62 days', now() - interval '9 days',  true),
    ('a0000000-0000-4000-8000-000000000003', 'boutique-marea',      'Boutique Marea',      'ES', 'Europe/Madrid',       'es-ES', true, now() - interval '28 days', now() - interval '1 day',   false);

-- -----------------------------------------------------------------------------
-- 2. Personas y membresías
--
-- Las cuatro roles de MemberRole aparecen en la primera organización, para que
-- se pueda probar qué esconde y qué permite cada una. Dos personas pertenecen a
-- dos organizaciones: es el caso que hace interesante al selector de `/me`.
-- -----------------------------------------------------------------------------
INSERT INTO users (id, email, password_hash, full_name, email_verified_at, last_login_at, created_at, failed_login_attempts, locked_until, password_set_at, two_factor_enabled_at, two_factor_secret_ciphertext) VALUES
    ('c0000000-0000-4000-8000-000000000001', 'manuela.rios@orbita.demo',    '$argon2id$v=19$m=65536,t=3,p=1$XPhpY4gGhXLkFCQIdISmQw==$hDBXHFRF1AMfps6pq09eG2k9F1cpwkJvkBQLaHHPvPs=', 'Manuela Ríos',       now() - interval '95 days', now() - interval '2 hours', now() - interval '95 days', 0, NULL, now() - interval '95 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000002', 'andres.gomez@orbita.demo',    '$argon2id$v=19$m=65536,t=3,p=1$EXe9LonApzNbZf3ARa/6sA==$chdQgC0jTV+fth0Oi6ELHeH2jhoqemxma3dRFVJvddc=', 'Andrés Gómez',       now() - interval '94 days', now() - interval '1 day',   now() - interval '94 days', 0, NULL, now() - interval '94 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000003', 'valentina.cruz@orbita.demo',  '$argon2id$v=19$m=65536,t=3,p=1$QACxILCJ7l8/f5pa2QNukQ==$c3Lb3RodjQLFSmZjQmK0J5O9Lz5gBgwdsVdOzGKH3Dc=', 'Valentina Cruz',     now() - interval '90 days', now() - interval '5 hours', now() - interval '90 days', 0, NULL, now() - interval '90 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000004', 'julian.mesa@orbita.demo',     '$argon2id$v=19$m=65536,t=3,p=1$fHY8lKFXxq1DF51QMqjU1w==$7hmOILhBoqIg1Ksyr8mxVCzGUoDF7pcn7bCY+M43Stk=', 'Julián Mesa',        now() - interval '88 days', now() - interval '3 days',  now() - interval '88 days', 0, NULL, now() - interval '88 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000005', 'paula.restrepo@orbita.demo',  '$argon2id$v=19$m=65536,t=3,p=1$abX46kHYU3aX+FKIu+qNXQ==$HjAR1Sx8TPOR/LFwCPa2HfFLcrRJPxJ7oa7gWf9+zbo=', 'Paula Restrepo',     now() - interval '80 days', now() - interval '8 days',  now() - interval '80 days', 0, NULL, now() - interval '80 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000006', 'santiago.leon@orbita.demo',   '$argon2id$v=19$m=65536,t=3,p=1$GQmj++tZAjrN5raRoSqZ6Q==$elRjkBkvQnAuWSJuPxC4wN8CzQwZ4U8Iw7io+ezgxtk=', 'Santiago León',      now() - interval '75 days', now() - interval '12 days', now() - interval '75 days', 2, NULL, now() - interval '75 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000007', 'carolina.duque@orbita.demo',  '$argon2id$v=19$m=65536,t=3,p=1$ReENgG8L8jrL/zI00Qramw==$AG2EyLoGYaKt2TMCOjfKdWXhtSiGAeWQoBkUtI+j6N0=', 'Carolina Duque',     now() - interval '62 days', now() - interval '1 hour',  now() - interval '62 days', 0, NULL, now() - interval '62 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000008', 'ricardo.navarro@orbita.demo', '$argon2id$v=19$m=65536,t=3,p=1$+KhjB5TEMSRaxsv6lQSoyw==$8rprSmaYQS6cKPH+5/tBNtdqnnk0ICh7S2Anb5aAo+w=', 'Ricardo Navarro',    now() - interval '60 days', now() - interval '6 hours', now() - interval '60 days', 0, NULL, now() - interval '60 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000009', 'daniela.ortega@orbita.demo',  '$argon2id$v=19$m=65536,t=3,p=1$jCmhW7ticMHRvq+xA6KIpA==$ph6wJhgI+cz35VER09fuKarIFgvJ+YG5vI0B6t0sPRQ=', 'Daniela Ortega',     now() - interval '58 days', now() - interval '2 days',  now() - interval '58 days', 0, NULL, now() - interval '58 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000010', 'felipe.cardenas@orbita.demo', '$argon2id$v=19$m=65536,t=3,p=1$DgYsPkmV63mOpCr8m8fYCQ==$PY4/m3oJGoySPFh9ZZST8VYQljRKmDBGtHWhdjlaeas=', 'Felipe Cárdenas',    now() - interval '55 days', now() - interval '4 days',  now() - interval '55 days', 0, NULL, now() - interval '55 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000011', 'lucia.ferrer@orbita.demo',    '$argon2id$v=19$m=65536,t=3,p=1$Rogjc6yvwSY/U7yCHwH+ag==$OU+WxF+Iyo2XXie+7CElQIkaMKRfv1xBq4RQ9ZPZLM8=', 'Lucía Ferrer',       now() - interval '28 days', now() - interval '30 minutes', now() - interval '28 days', 0, NULL, now() - interval '28 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000012', 'marc.soler@orbita.demo',      '$argon2id$v=19$m=65536,t=3,p=1$WVzJEbeM1kCNIZBCZ2Fk5Q==$FveXn6eRxmbJaRrD3inqLuxih9IDBKnKM/fyajQM0Lw=', 'Marc Soler',         now() - interval '27 days', now() - interval '3 hours', now() - interval '27 days', 0, NULL, now() - interval '27 days', NULL, NULL),
    ('c0000000-0000-4000-8000-000000000013', 'nuria.blanco@orbita.demo',    '$argon2id$v=19$m=65536,t=3,p=1$NJJ25U02XRT0YiPcmYwzIA==$uPwxMu3DAUTlvGBULFEq6+SQ91L3rsCfgYIy0zbO1rM=', 'Nuria Blanco',       now() - interval '20 days', now() - interval '9 days',  now() - interval '20 days', 0, NULL, now() - interval '20 days', NULL, NULL),
    -- Invitada que todavía no eligió contraseña: `password_set_at` en NULL y un
    -- hash que nunca verifica, igual que User.CreateInvited (ORB-A07).
    ('c0000000-0000-4000-8000-000000000014', 'invitada.pendiente@orbita.demo', '$argon2id$v=19$m=65536,t=3,p=1$LZFzTuKDtAJh/Wa18baTTw==$WfO3vWz1fvgl0Nw6yt5IUPHhS6kg+lmiJygHlR3CvOs=', 'Sara Quintero', NULL, NULL, now() - interval '4 days', 0, NULL, NULL, NULL, NULL);

INSERT INTO memberships (id, tenant_id, user_id, role, invited_by, invited_at, accepted_at, is_active, created_at) VALUES
    -- Panadería La Espiga: los cuatro roles.
    ('d0000000-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-000000000001', 'c0000000-0000-4000-8000-000000000001', 'Owner',  NULL, NULL, now() - interval '95 days', true, now() - interval '95 days'),
    ('d0000000-0000-4000-8000-000000000002', 'a0000000-0000-4000-8000-000000000001', 'c0000000-0000-4000-8000-000000000002', 'Admin',  'c0000000-0000-4000-8000-000000000001', now() - interval '94 days', now() - interval '94 days', true, now() - interval '94 days'),
    ('d0000000-0000-4000-8000-000000000003', 'a0000000-0000-4000-8000-000000000001', 'c0000000-0000-4000-8000-000000000003', 'Agent',  'c0000000-0000-4000-8000-000000000001', now() - interval '90 days', now() - interval '90 days', true, now() - interval '90 days'),
    ('d0000000-0000-4000-8000-000000000004', 'a0000000-0000-4000-8000-000000000001', 'c0000000-0000-4000-8000-000000000004', 'Agent',  'c0000000-0000-4000-8000-000000000002', now() - interval '88 days', now() - interval '88 days', true, now() - interval '88 days'),
    ('d0000000-0000-4000-8000-000000000005', 'a0000000-0000-4000-8000-000000000001', 'c0000000-0000-4000-8000-000000000005', 'Viewer', 'c0000000-0000-4000-8000-000000000001', now() - interval '80 days', now() - interval '80 days', true, now() - interval '80 days'),
    -- Alguien a quien sacaron del equipo: no debe aparecer en `/me` ni en la lista.
    ('d0000000-0000-4000-8000-000000000006', 'a0000000-0000-4000-8000-000000000001', 'c0000000-0000-4000-8000-000000000006', 'Agent',  'c0000000-0000-4000-8000-000000000001', now() - interval '75 days', now() - interval '75 days', false, now() - interval '75 days'),
    -- Invitación pendiente: aceptada en NULL, activa. Tampoco sale en `/me`.
    ('d0000000-0000-4000-8000-000000000007', 'a0000000-0000-4000-8000-000000000001', 'c0000000-0000-4000-8000-000000000014', 'Agent',  'c0000000-0000-4000-8000-000000000001', now() - interval '4 days', NULL, true, now() - interval '4 days'),

    -- Clínica Sonrisa.
    ('d0000000-0000-4000-8000-000000000008', 'a0000000-0000-4000-8000-000000000002', 'c0000000-0000-4000-8000-000000000007', 'Owner',  NULL, NULL, now() - interval '62 days', true, now() - interval '62 days'),
    ('d0000000-0000-4000-8000-000000000009', 'a0000000-0000-4000-8000-000000000002', 'c0000000-0000-4000-8000-000000000008', 'Admin',  'c0000000-0000-4000-8000-000000000007', now() - interval '60 days', now() - interval '60 days', true, now() - interval '60 days'),
    ('d0000000-0000-4000-8000-000000000010', 'a0000000-0000-4000-8000-000000000002', 'c0000000-0000-4000-8000-000000000009', 'Agent',  'c0000000-0000-4000-8000-000000000007', now() - interval '58 days', now() - interval '58 days', true, now() - interval '58 days'),
    ('d0000000-0000-4000-8000-000000000011', 'a0000000-0000-4000-8000-000000000002', 'c0000000-0000-4000-8000-000000000010', 'Agent',  'c0000000-0000-4000-8000-000000000008', now() - interval '55 days', now() - interval '55 days', true, now() - interval '55 days'),
    -- Manuela pertenece también a esta organización: dos opciones en el selector.
    ('d0000000-0000-4000-8000-000000000012', 'a0000000-0000-4000-8000-000000000002', 'c0000000-0000-4000-8000-000000000001', 'Viewer', 'c0000000-0000-4000-8000-000000000007', now() - interval '40 days', now() - interval '40 days', true, now() - interval '40 days'),

    -- Boutique Marea.
    ('d0000000-0000-4000-8000-000000000013', 'a0000000-0000-4000-8000-000000000003', 'c0000000-0000-4000-8000-000000000011', 'Owner',  NULL, NULL, now() - interval '28 days', true, now() - interval '28 days'),
    ('d0000000-0000-4000-8000-000000000014', 'a0000000-0000-4000-8000-000000000003', 'c0000000-0000-4000-8000-000000000012', 'Admin',  'c0000000-0000-4000-8000-000000000011', now() - interval '27 days', now() - interval '27 days', true, now() - interval '27 days'),
    ('d0000000-0000-4000-8000-000000000015', 'a0000000-0000-4000-8000-000000000003', 'c0000000-0000-4000-8000-000000000013', 'Agent',  'c0000000-0000-4000-8000-000000000011', now() - interval '20 days', now() - interval '20 days', true, now() - interval '20 days'),
    ('d0000000-0000-4000-8000-000000000016', 'a0000000-0000-4000-8000-000000000003', 'c0000000-0000-4000-8000-000000000002', 'Agent',  'c0000000-0000-4000-8000-000000000011', now() - interval '15 days', now() - interval '15 days', true, now() - interval '15 days');

-- -----------------------------------------------------------------------------
-- 3. Suscripciones — una por organización (índice único por tenant)
--
-- El proveedor no es una elección: SubscriptionService manda Wompi para CO y
-- Stripe para el resto. Los ids de plan son los que siembra la migración.
-- -----------------------------------------------------------------------------
INSERT INTO subscriptions (id, tenant_id, plan_id, provider, provider_customer_id, provider_subscription_id, status, current_period_end, created_at, updated_at) VALUES
    ('e0000000-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-000000000001', '9c8e6f5a-1b2c-4d3e-8f9a-0b1c2d3e4f51', 'Wompi',  'seed_wompi_cus_espiga',  'seed_wompi_sub_espiga',  'Active',   now() + interval '17 days', now() - interval '95 days', now() - interval '13 days'),
    ('e0000000-0000-4000-8000-000000000002', 'a0000000-0000-4000-8000-000000000002', '9c8e6f5a-1b2c-4d3e-8f9a-0b1c2d3e4f52', 'Stripe', 'seed_stripe_cus_sonrisa', 'seed_stripe_sub_sonrisa', 'Active',   now() + interval '4 days',  now() - interval '62 days', now() - interval '26 days'),
    ('e0000000-0000-4000-8000-000000000003', 'a0000000-0000-4000-8000-000000000003', '9c8e6f5a-1b2c-4d3e-8f9a-0b1c2d3e4f50', 'Stripe', 'seed_stripe_cus_marea',   'seed_stripe_sub_marea',   'Trialing', now() + interval '11 days', now() - interval '28 days', now() - interval '28 days');

-- -----------------------------------------------------------------------------
-- 4. Canales conectados
--
-- `external_id` es el phone_number_id de Meta para WhatsApp y el id de la
-- cuenta de negocio para Instagram. Son inventados, pero únicos, que es lo que
-- exige `ux_channel_external`.
-- -----------------------------------------------------------------------------
INSERT INTO channel_accounts (id, tenant_id, kind, external_id, waba_id, display_name, phone_e164, credentials_ref, webhook_secret, status, token_expires_at, connected_at, created_at) VALUES
    ('f0000000-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-000000000001', 'WhatsApp',  'seed-wa-101', 'seed-waba-101', 'La Espiga · Pedidos',   '+573001112233', 'local://seed-espiga-wa',   'seed-verify-espiga-wa',   'Connected',        now() + interval '45 days', now() - interval '94 days', now() - interval '94 days'),
    ('f0000000-0000-4000-8000-000000000002', 'a0000000-0000-4000-8000-000000000001', 'Instagram', 'seed-ig-101', NULL,            'La Espiga · Instagram', NULL,            'local://seed-espiga-ig',   'seed-verify-espiga-ig',   'Connected',        now() + interval '45 days', now() - interval '70 days', now() - interval '70 days'),
    ('f0000000-0000-4000-8000-000000000003', 'a0000000-0000-4000-8000-000000000002', 'WhatsApp',  'seed-wa-202', 'seed-waba-202', 'Clínica Sonrisa',       '+525551234567', 'local://seed-sonrisa-wa',  'seed-verify-sonrisa-wa',  'Connected',        now() + interval '6 days',  now() - interval '61 days', now() - interval '61 days'),
    -- Un token vencido, para poder ver el aviso en la pantalla de canales.
    ('f0000000-0000-4000-8000-000000000004', 'a0000000-0000-4000-8000-000000000002', 'Instagram', 'seed-ig-202', NULL,            'Sonrisa · Instagram',   NULL,            'local://seed-sonrisa-ig',  'seed-verify-sonrisa-ig',  'TokenExpired',     now() - interval '2 days',  now() - interval '50 days', now() - interval '50 days'),
    ('f0000000-0000-4000-8000-000000000005', 'a0000000-0000-4000-8000-000000000003', 'WhatsApp',  'seed-wa-303', 'seed-waba-303', 'Boutique Marea',        '+34600112233',  'local://seed-marea-wa',    'seed-verify-marea-wa',    'Connected',        now() + interval '52 days', now() - interval '27 days', now() - interval '27 days'),
    -- Una conexión a medio terminar: Meta nunca completó la verificación.
    ('f0000000-0000-4000-8000-000000000006', 'a0000000-0000-4000-8000-000000000003', 'Instagram', 'seed-ig-303', NULL,            'Marea · Instagram',     NULL,            'local://seed-marea-ig',    'seed-verify-marea-ig',    'PendingVerification', NULL,                 NULL,                       now() - interval '2 days');

-- -----------------------------------------------------------------------------
-- 5. Campos personalizados de contacto
-- -----------------------------------------------------------------------------
INSERT INTO contact_field_definitions (id, tenant_id, key, label, field_type, created_at) VALUES
    ('01000000-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-000000000001', 'barrio',            'Barrio',                'Text',   now() - interval '90 days'),
    ('01000000-0000-4000-8000-000000000002', 'a0000000-0000-4000-8000-000000000001', 'pedido_habitual',   'Pedido habitual',       'Text',   now() - interval '90 days'),
    ('01000000-0000-4000-8000-000000000003', 'a0000000-0000-4000-8000-000000000002', 'ultima_limpieza',   'Última limpieza',       'Date',   now() - interval '58 days'),
    ('01000000-0000-4000-8000-000000000004', 'a0000000-0000-4000-8000-000000000002', 'aseguradora',       'Aseguradora',           'Text',   now() - interval '58 days'),
    ('01000000-0000-4000-8000-000000000005', 'a0000000-0000-4000-8000-000000000003', 'talla',             'Talla habitual',        'Text',   now() - interval '25 days'),
    ('01000000-0000-4000-8000-000000000006', 'a0000000-0000-4000-8000-000000000003', 'ticket_promedio',   'Ticket promedio',       'Number', now() - interval '25 days');

-- -----------------------------------------------------------------------------
-- 6. Contactos — 1.800 en total (600 por organización)
--
-- El teléfono va sin `+`, como lo guarda Contact.NormalizePhone desde ORB-B03
-- para que coincida con el `wa_id` que manda Meta.
-- -----------------------------------------------------------------------------
INSERT INTO contacts (id, tenant_id, display_name, phone, instagram_username, email, channel, custom_fields, created_at, updated_at, ig_user_id, last_seen_at)
SELECT
    gen_random_uuid(),
    t.tenant_id,
    (ARRAY['María','Juan','Camila','Sebastián','Laura','Diego','Isabella','Mateo','Sofía','Samuel','Valeria','Nicolás','Gabriela','Emiliano','Antonia','Tomás','Renata','Martín','Elena','Joaquín'])[1 + (i % 20)]
        || ' ' ||
    (ARRAY['Gutiérrez','Moreno','Salazar','Ospina','Vargas','Herrera','Rincón','Peña','Castaño','Bermúdez','Mejía','Arango','Zapata','Pineda','Quiroga','Villalba','Cortés','Lozano','Ibáñez','Fuentes'])[1 + ((i / 20) % 20)],
    t.phone_prefix || lpad((1000000 + i * 7)::text, 7, '0'),
    CASE WHEN i % 5 = 0 THEN 'cliente_' || t.slug_short || '_' || i ELSE NULL END,
    CASE WHEN i % 3 = 0 THEN 'contacto' || i || '.' || t.slug_short || '@correo.demo' ELSE NULL END,
    CASE WHEN i % 5 = 0 THEN 'Instagram' ELSE 'WhatsApp' END,
    CASE
        WHEN t.tenant_id = 'a0000000-0000-4000-8000-000000000001'
            THEN jsonb_build_object('barrio', (ARRAY['Laureles','El Poblado','Belén','Envigado','Robledo'])[1 + (i % 5)])
        WHEN t.tenant_id = 'a0000000-0000-4000-8000-000000000002'
            THEN jsonb_build_object('aseguradora', (ARRAY['GNP','AXA','Metlife','Ninguna'])[1 + (i % 4)])
        ELSE jsonb_build_object('talla', (ARRAY['XS','S','M','L','XL'])[1 + (i % 5)])
    END,
    t.since + (i * interval '73 minutes'),
    now() - ((i % 30) * interval '1 day'),
    CASE WHEN i % 5 = 0 THEN 'seed-ig-user-' || t.slug_short || '-' || i ELSE NULL END,
    now() - ((i % 45) * interval '6 hours')
FROM (VALUES
    ('a0000000-0000-4000-8000-000000000001'::uuid, '57300',  'espiga',  now() - interval '94 days'),
    ('a0000000-0000-4000-8000-000000000002'::uuid, '52155',  'sonrisa', now() - interval '61 days'),
    ('a0000000-0000-4000-8000-000000000003'::uuid, '34600',  'marea',   now() - interval '27 days')
) AS t(tenant_id, phone_prefix, slug_short, since)
CROSS JOIN generate_series(1, 600) AS i;

-- -----------------------------------------------------------------------------
-- 7. Tablero comercial — un pipeline por organización con las seis etapas
--    del tablero por defecto (ORB-D04)
-- -----------------------------------------------------------------------------
INSERT INTO pipelines (id, tenant_id, name, is_default, created_at, updated_at) VALUES
    ('02000000-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-000000000001', 'Ventas',       true, now() - interval '94 days', now() - interval '94 days'),
    ('02000000-0000-4000-8000-000000000002', 'a0000000-0000-4000-8000-000000000002', 'Tratamientos', true, now() - interval '61 days', now() - interval '61 days'),
    ('02000000-0000-4000-8000-000000000003', 'a0000000-0000-4000-8000-000000000003', 'Ventas',       true, now() - interval '27 days', now() - interval '27 days');

INSERT INTO pipeline_stages (id, tenant_id, pipeline_id, name, sort_order, is_won, is_lost, created_at, updated_at)
SELECT
    gen_random_uuid(),
    p.tenant_id,
    p.id,
    s.name,
    s.sort_order,
    s.is_won,
    s.is_lost,
    p.created_at,
    p.created_at
FROM pipelines p
CROSS JOIN (VALUES
    ('Nuevo',       1, false, false),
    ('Contactado',  2, false, false),
    ('Cotizado',    3, false, false),
    ('Negociación', 4, false, false),
    ('Ganado',      5, true,  false),
    ('Perdido',     6, false, true)
) AS s(name, sort_order, is_won, is_lost)
WHERE p.tenant_id IN (SELECT id FROM seed_tenants);

-- 360 oportunidades repartidas por etapa, con montos en el orden de magnitud
-- que tiene sentido para cada negocio.
INSERT INTO opportunities (id, tenant_id, pipeline_id, stage_id, title, amount, created_at, updated_at, assigned_to_user_id, last_move_event_id, contact_id)
SELECT
    gen_random_uuid(),
    c.tenant_id,
    p.id,
    st.id,
    CASE c.tenant_id
        WHEN 'a0000000-0000-4000-8000-000000000001' THEN 'Pedido para ' || c.display_name
        WHEN 'a0000000-0000-4000-8000-000000000002' THEN 'Tratamiento de ' || c.display_name
        ELSE 'Compra de ' || c.display_name
    END,
    round((CASE c.tenant_id
        WHEN 'a0000000-0000-4000-8000-000000000001' THEN 80000 + random() * 900000
        WHEN 'a0000000-0000-4000-8000-000000000002' THEN 1500 + random() * 28000
        ELSE 40 + random() * 700
    END)::numeric, 2),
    c.created_at + interval '2 days',
    now() - (random() * interval '20 days'),
    m.user_id,
    NULL,
    c.id
FROM (
    SELECT
        c.*,
        row_number() OVER (PARTITION BY c.tenant_id ORDER BY c.created_at) AS rn
    FROM contacts c
    WHERE c.tenant_id IN (SELECT id FROM seed_tenants)
) c
JOIN pipelines p ON p.tenant_id = c.tenant_id
JOIN LATERAL (
    SELECT s.id
    FROM pipeline_stages s
    WHERE s.pipeline_id = p.id
    ORDER BY s.sort_order
    OFFSET (c.rn % 6) LIMIT 1
) st ON true
JOIN LATERAL (
    SELECT m.user_id
    FROM memberships m
    WHERE m.tenant_id = c.tenant_id AND m.is_active AND m.accepted_at IS NOT NULL
    ORDER BY m.created_at
    OFFSET (c.rn % 3) LIMIT 1
) m ON true
WHERE c.rn <= 180;

-- -----------------------------------------------------------------------------
-- 8. Asistentes de IA
--
-- Los ejes de estilo son los de AgentStyle (ORB-C10): formality, verbosity y
-- energy, nunca una "temperatura" expuesta al usuario. `tools` guarda solo las
-- claves habilitadas; hoy la única que realmente funciona es
-- consultar_conocimiento (ORB-C03).
-- -----------------------------------------------------------------------------
INSERT INTO ai_agents (id, tenant_id, name, system_prompt, temperature, max_tokens, is_enabled, created_at, instructions, personality, formality, tools, verbosity, energy) VALUES
    ('03000000-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-000000000001', 'Espiga',   'Sos el asistente de Panadería La Espiga.', 0.30, 800, true,  now() - interval '94 days', 'Confirmá siempre la hora de recogida antes de cerrar un pedido. Si preguntan por algo que no está en el catálogo, decilo y ofrecé pasar con una persona.', 'Cercano y resolutivo, habla como alguien del mostrador.', 'Warm',     '["consultar_conocimiento"]'::jsonb, 'Brief',    'Enthusiastic'),
    ('03000000-0000-4000-8000-000000000002', 'a0000000-0000-4000-8000-000000000001', 'Espiga Eventos', 'Sos el asistente de pedidos grandes de La Espiga.', 0.20, 600, false, now() - interval '30 days', 'Atendé solo pedidos para eventos de más de 20 personas. Pedí siempre fecha, cantidad y lugar de entrega.', 'Formal y muy concreto.', 'Formal', '["consultar_conocimiento"]'::jsonb, 'Detailed', 'Neutral'),
    ('03000000-0000-4000-8000-000000000003', 'a0000000-0000-4000-8000-000000000002', 'Sonrisa',  'Sos el asistente de Clínica Sonrisa.',    0.20, 900, true,  now() - interval '61 days', 'Nunca des un diagnóstico. Si preguntan por dolor o urgencias, pasá la conversación a una persona de inmediato.', 'Tranquilo y profesional, transmite calma.', 'Formal',   '["consultar_conocimiento"]'::jsonb, 'Balanced', 'Neutral'),
    ('03000000-0000-4000-8000-000000000004', 'a0000000-0000-4000-8000-000000000003', 'Marea',    'Sos el asistente de Boutique Marea.',     0.40, 700, true,  now() - interval '27 days', 'Si preguntan por una talla que no está, ofrecé avisar cuando vuelva a entrar. No prometas fechas de reposición.', 'Con estilo, breve, nada de formalismos.', 'Balanced', '["consultar_conocimiento"]'::jsonb, 'Brief',    'Enthusiastic');

-- Un borrador sin publicar, para la pantalla 2.6 (ORB-C10).
INSERT INTO ai_agent_drafts (agent_id, tenant_id, name, instructions, personality, formality, verbosity, energy, tools, updated_at) VALUES
    ('03000000-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-000000000001', 'Espiga', 'Confirmá siempre la hora de recogida. Desde este mes ofrecé también domicilio dentro de Laureles y Belén.', 'Cercano y resolutivo, habla como alguien del mostrador.', 'Warm', 'Balanced', 'Enthusiastic', '["consultar_conocimiento"]'::jsonb, now() - interval '2 days');

-- Preferencias de modelo por tarea (ORB-C13), con los modelos que ya están
-- tarifados en local/notas-entorno.md.
INSERT INTO tenant_model_preferences (id, tenant_id, provider_name, task, model, updated_at) VALUES
    ('04000000-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-000000000001', 'openai-compatible', 'Classify', 'deepseek/deepseek-v4-flash',  now() - interval '20 days'),
    ('04000000-0000-4000-8000-000000000002', 'a0000000-0000-4000-8000-000000000001', 'openai-compatible', 'Draft',    'openai/gpt-5.6-luna',        now() - interval '20 days'),
    ('04000000-0000-4000-8000-000000000003', 'a0000000-0000-4000-8000-000000000002', 'openai-compatible', 'Classify', 'deepseek/deepseek-v4-flash',  now() - interval '15 days'),
    ('04000000-0000-4000-8000-000000000004', 'a0000000-0000-4000-8000-000000000002', 'openai-compatible', 'Draft',    'google/gemini-3.5-flash-lite', now() - interval '15 days');

-- -----------------------------------------------------------------------------
-- 9. Base de conocimiento — 12 documentos y 294 fragmentos
--
-- El embedding es ruido aleatorio normalizado: sirve para volumen y para que el
-- índice HNSW tenga filas, no para que la búsqueda tenga sentido.
-- -----------------------------------------------------------------------------
INSERT INTO knowledge_docs (id, tenant_id, agent_id, title, source_type, source_ref, status, chunk_count, failure_reason, indexed_at, created_at) VALUES
    ('05000000-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-000000000001', '03000000-0000-4000-8000-000000000001', 'Catálogo de panadería 2026',        'Upload', 'tenants/a0000000-0000-4000-8000-000000000001/knowledge/05000000-0000-4000-8000-000000000001.pdf', 'Indexed',    48, NULL, now() - interval '90 days', now() - interval '90 days'),
    ('05000000-0000-4000-8000-000000000002', 'a0000000-0000-4000-8000-000000000001', '03000000-0000-4000-8000-000000000001', 'Preguntas frecuentes de pedidos',   'Manual', NULL,                                                                                              'Indexed',    36, NULL, now() - interval '88 days', now() - interval '88 days'),
    ('05000000-0000-4000-8000-000000000003', 'a0000000-0000-4000-8000-000000000001', '03000000-0000-4000-8000-000000000001', 'Horarios y puntos de entrega',      'Manual', NULL,                                                                                              'Indexed',    12, NULL, now() - interval '60 days', now() - interval '60 days'),
    ('05000000-0000-4000-8000-000000000004', 'a0000000-0000-4000-8000-000000000001', '03000000-0000-4000-8000-000000000002', 'Tarifas para eventos',              'Upload', 'tenants/a0000000-0000-4000-8000-000000000001/knowledge/05000000-0000-4000-8000-000000000004.docx', 'Indexed',    24, NULL, now() - interval '29 days', now() - interval '29 days'),
    ('05000000-0000-4000-8000-000000000005', 'a0000000-0000-4000-8000-000000000001', '03000000-0000-4000-8000-000000000001', 'Promociones de septiembre',         'Upload', 'tenants/a0000000-0000-4000-8000-000000000001/knowledge/05000000-0000-4000-8000-000000000005.pdf', 'Processing',  0, NULL, NULL,                       now() - interval '2 hours'),
    ('05000000-0000-4000-8000-000000000006', 'a0000000-0000-4000-8000-000000000001', '03000000-0000-4000-8000-000000000001', 'Lista de precios (escaneada)',      'Upload', 'tenants/a0000000-0000-4000-8000-000000000001/knowledge/05000000-0000-4000-8000-000000000006.pdf', 'Failed',      0, 'No se pudo extraer texto del PDF: parece ser una imagen escaneada sin capa de texto.', NULL, now() - interval '5 days'),
    ('05000000-0000-4000-8000-000000000007', 'a0000000-0000-4000-8000-000000000002', '03000000-0000-4000-8000-000000000003', 'Servicios y tratamientos',          'Upload', 'tenants/a0000000-0000-4000-8000-000000000002/knowledge/05000000-0000-4000-8000-000000000007.pdf', 'Indexed',    60, NULL, now() - interval '58 days', now() - interval '58 days'),
    ('05000000-0000-4000-8000-000000000008', 'a0000000-0000-4000-8000-000000000002', '03000000-0000-4000-8000-000000000003', 'Convenios con aseguradoras',        'Upload', 'tenants/a0000000-0000-4000-8000-000000000002/knowledge/05000000-0000-4000-8000-000000000008.docx', 'Indexed',    30, NULL, now() - interval '55 days', now() - interval '55 days'),
    ('05000000-0000-4000-8000-000000000009', 'a0000000-0000-4000-8000-000000000002', '03000000-0000-4000-8000-000000000003', 'Protocolo de urgencias',            'Manual', NULL,                                                                                              'Indexed',    18, NULL, now() - interval '40 days', now() - interval '40 days'),
    ('05000000-0000-4000-8000-000000000010', 'a0000000-0000-4000-8000-000000000003', '03000000-0000-4000-8000-000000000004', 'Catálogo temporada otoño',          'Upload', 'tenants/a0000000-0000-4000-8000-000000000003/knowledge/05000000-0000-4000-8000-000000000010.pdf', 'Indexed',    42, NULL, now() - interval '25 days', now() - interval '25 days'),
    ('05000000-0000-4000-8000-000000000011', 'a0000000-0000-4000-8000-000000000003', '03000000-0000-4000-8000-000000000004', 'Guía de tallas',                    'Manual', NULL,                                                                                              'Indexed',    14, NULL, now() - interval '24 days', now() - interval '24 days'),
    ('05000000-0000-4000-8000-000000000012', 'a0000000-0000-4000-8000-000000000003', '03000000-0000-4000-8000-000000000004', 'Política de cambios y devoluciones','Manual', NULL,                                                                                              'Indexed',    10, NULL, now() - interval '22 days', now() - interval '22 days');

INSERT INTO knowledge_chunks (id, tenant_id, doc_id, chunk_index, content, token_count, embedding)
SELECT
    gen_random_uuid(),
    d.tenant_id,
    d.id,
    n - 1,
    'Fragmento ' || n || ' de «' || d.title || '». '
        || 'Texto de ejemplo con el detalle correspondiente a esta sección del documento, '
        || 'suficientemente largo como para parecerse a un trozo real de ' || d.title || '.',
    60 + (n % 40),
    (SELECT ('[' || string_agg(round((random() * 2 - 1)::numeric, 5)::text, ',') || ']')::vector
     FROM generate_series(1, 1536))
FROM knowledge_docs d
CROSS JOIN LATERAL generate_series(1, d.chunk_count) AS n
WHERE d.tenant_id IN (SELECT id FROM seed_tenants) AND d.status = 'Indexed';

-- -----------------------------------------------------------------------------
-- 10. Plantillas de mensaje (ORB-B07)
-- -----------------------------------------------------------------------------
INSERT INTO message_templates (id, tenant_id, channel_account_id, meta_template_name, category, language, body, status, rejected_reason, approved_at, created_at) VALUES
    ('06000000-0000-4000-8000-000000000001', 'a0000000-0000-4000-8000-000000000001', 'f0000000-0000-4000-8000-000000000001', 'pedido_listo',        'Utility',   'es', 'Hola {{1}}, tu pedido ya está listo para recoger en {{2}}.',            'Approved', NULL, now() - interval '80 days', now() - interval '85 days'),
    ('06000000-0000-4000-8000-000000000002', 'a0000000-0000-4000-8000-000000000001', 'f0000000-0000-4000-8000-000000000001', 'promo_semanal',       'Marketing', 'es', 'Esta semana en La Espiga: {{1}}. Te esperamos.',                        'Approved', NULL, now() - interval '40 days', now() - interval '45 days'),
    ('06000000-0000-4000-8000-000000000003', 'a0000000-0000-4000-8000-000000000001', 'f0000000-0000-4000-8000-000000000001', 'encuesta_pedido',     'Utility',   'es', 'Hola {{1}}, ¿cómo te fue con tu pedido? Respondé con un número del 1 al 5.', 'Pending', NULL, NULL, now() - interval '6 days'),
    ('06000000-0000-4000-8000-000000000004', 'a0000000-0000-4000-8000-000000000002', 'f0000000-0000-4000-8000-000000000003', 'recordatorio_cita',   'Utility',   'es', 'Hola {{1}}, te recordamos tu cita del {{2}} a las {{3}}.',              'Approved', NULL, now() - interval '55 days', now() - interval '58 days'),
    ('06000000-0000-4000-8000-000000000005', 'a0000000-0000-4000-8000-000000000002', 'f0000000-0000-4000-8000-000000000003', 'confirmacion_cita',   'Utility',   'es', 'Tu cita quedó confirmada para el {{1}}. Si no podés asistir, avisanos.', 'Approved', NULL, now() - interval '50 days', now() - interval '52 days'),
    ('06000000-0000-4000-8000-000000000006', 'a0000000-0000-4000-8000-000000000002', 'f0000000-0000-4000-8000-000000000003', 'promo_blanqueamiento','Marketing', 'es', 'Blanqueamiento con {{1}}% de descuento hasta el {{2}}.',                'Rejected', 'El contenido promocional no cumple la política de mensajes de Meta.', NULL, now() - interval '18 days'),
    ('06000000-0000-4000-8000-000000000007', 'a0000000-0000-4000-8000-000000000003', 'f0000000-0000-4000-8000-000000000005', 'pedido_enviado',      'Utility',   'es', 'Hola {{1}}, tu pedido salió hoy. Número de seguimiento: {{2}}.',        'Approved', NULL, now() - interval '20 days', now() - interval '24 days'),
    ('06000000-0000-4000-8000-000000000008', 'a0000000-0000-4000-8000-000000000003', 'f0000000-0000-4000-8000-000000000005', 'carrito_abandonado',  'Marketing', 'es', '{{1}}, dejaste algo en el carrito. Te lo guardamos 24 horas.',          'Draft',    NULL, NULL, now() - interval '3 days');

-- -----------------------------------------------------------------------------
-- 11. Bandeja — conversaciones
--
-- Una conversación por contacto, para la mitad de los contactos de cada
-- organización (que es más o menos como se ve una bandeja real: no todos los
-- contactos escriben). La ventana de servicio de 24 horas queda abierta solo en
-- las conversaciones con actividad reciente, que es lo que hace que la pantalla
-- muestre los dos casos.
-- -----------------------------------------------------------------------------
INSERT INTO conversations (id, tenant_id, contact_id, channel_account_id, status, assignee_id, ai_agent_id, window_expires_at, human_agent_expires_at, unread_count, last_message_at, last_message_preview, first_response_seconds, closed_at, created_at)
SELECT
    gen_random_uuid(),
    c.tenant_id,
    c.id,
    ca.id,
    CASE
        WHEN c.rn % 10 IN (0, 1, 2, 3) THEN 'Open'
        WHEN c.rn % 10 IN (4, 5)       THEN 'Pending'
        WHEN c.rn % 10 = 6             THEN 'Snoozed'
        ELSE 'Closed'
    END,
    CASE WHEN c.rn % 3 = 0 THEN m.user_id ELSE NULL END,
    ag.id,
    NULL,
    NULL,
    0,
    NULL,
    NULL,
    CASE WHEN c.rn % 4 <> 0 THEN 40 + (c.rn % 900) ELSE NULL END,
    NULL,
    greatest(
        date_trunc('day', now() - interval '13 days'),
        now() - ((c.rn % 13) * interval '1 day') - ((c.rn % 7) * interval '1 hour')
    )
FROM (
    SELECT
        c.*,
        row_number() OVER (PARTITION BY c.tenant_id ORDER BY c.created_at) AS rn
    FROM contacts c
    WHERE c.tenant_id IN (SELECT id FROM seed_tenants)
) c
JOIN LATERAL (
    SELECT a.id
    FROM channel_accounts a
    WHERE a.tenant_id = c.tenant_id
      AND a.kind = c.channel
      AND a.status <> 'PendingVerification'
    LIMIT 1
) ca ON true
LEFT JOIN LATERAL (
    SELECT a.id
    FROM ai_agents a
    WHERE a.tenant_id = c.tenant_id AND a.is_enabled
    LIMIT 1
) ag ON true
JOIN LATERAL (
    SELECT m.user_id
    FROM memberships m
    WHERE m.tenant_id = c.tenant_id AND m.is_active AND m.accepted_at IS NOT NULL
    ORDER BY m.created_at
    OFFSET (c.rn % 4) LIMIT 1
) m ON true
WHERE c.rn <= 300;

-- -----------------------------------------------------------------------------
-- 12. Bandeja — mensajes
--
-- `messages` está particionada por `created_at` y la primera partición es
-- 2026-09, así que todo lo que se siembre tiene que caer de 2026-09-01 en
-- adelante. Por eso las conversaciones arrancan como mucho 13 días atrás.
--
-- Cada conversación recibe entre 6 y 45 mensajes alternando entrante/saliente.
-- Un entrante nace Delivered (Message.Inbound); un saliente termina Read,
-- Delivered, Sent o Failed.
-- -----------------------------------------------------------------------------
INSERT INTO messages (id, created_at, tenant_id, conversation_id, direction, category, body, media_key, media_mime, external_id, reply_to_external_id, template_id, sent_by_user_id, ai_run_id, status, error_code, sent_at, delivered_at, read_at, template_variables)
SELECT
    gen_random_uuid(),
    ts.at,
    v.tenant_id,
    v.id,
    CASE WHEN n % 2 = 1 THEN 'Inbound' ELSE 'Outbound' END,
    'Service',
    CASE
        WHEN n % 2 = 1 THEN (ARRAY[
            'Hola, buenas. ¿Todavía tienen disponibilidad para hoy?',
            '¿Cuánto cuesta y cómo hago para pagar?',
            'Perfecto, muchas gracias.',
            '¿Me lo pueden dejar listo para las 5?',
            'Buenas, quería consultar por lo que vi publicado.',
            '¿Aceptan transferencia?',
            'Listo, ya hice el pago. Adjunto el comprobante.',
            '¿A qué hora abren mañana?',
            'Una pregunta más: ¿hacen entregas?',
            'Ok, quedo atento entonces.'
        ])[1 + (n % 10)]
        ELSE (ARRAY[
            'Hola, con gusto. Sí, tenemos disponibilidad para hoy.',
            'Claro que sí, ahora te paso el detalle.',
            'Quedamos atentos. ¡Gracias por escribirnos!',
            'Perfecto, lo dejamos listo para esa hora.',
            'Sí, aceptamos transferencia y también pago en el local.',
            'Recibido el comprobante, lo verificamos y te confirmamos.',
            'Abrimos de 7 a.m. a 7 p.m. de lunes a sábado.',
            'Sí, hacemos entregas dentro de la ciudad.',
            'Te confirmo en unos minutos.',
            'Cualquier cosa quedamos por acá.'
        ])[1 + (n % 10)]
    END,
    NULL,
    CASE WHEN n % 11 = 0 THEN 'image/jpeg' ELSE NULL END,
    CASE WHEN n % 2 = 1 THEN 'wamid.seed.' || replace(v.id::text, '-', '') || '.' || n ELSE NULL END,
    NULL,
    NULL,
    CASE WHEN n % 2 = 0 AND v.assignee_id IS NOT NULL THEN v.assignee_id ELSE NULL END,
    NULL,
    CASE
        WHEN n % 2 = 1 THEN 'Delivered'
        WHEN n % 17 = 0 THEN 'Failed'
        WHEN n % 5 = 0  THEN 'Sent'
        WHEN n % 3 = 0  THEN 'Delivered'
        ELSE 'Read'
    END,
    CASE WHEN n % 2 = 0 AND n % 17 = 0 THEN '131047' ELSE NULL END,
    CASE WHEN n % 2 = 0 THEN ts.at + interval '2 seconds' ELSE NULL END,
    CASE
        WHEN n % 2 = 1 THEN ts.at
        WHEN n % 17 = 0 THEN NULL
        WHEN n % 5 = 0  THEN NULL
        ELSE ts.at + interval '6 seconds'
    END,
    CASE WHEN n % 2 = 0 AND n % 17 <> 0 AND n % 5 <> 0 AND n % 3 <> 0 THEN ts.at + interval '4 minutes' ELSE NULL END,
    NULL
FROM (
    SELECT c.*, 6 + (abs(hashtext(c.id::text)) % 40) AS message_count
    FROM conversations c
    WHERE c.tenant_id IN (SELECT id FROM seed_tenants)
) v
CROSS JOIN LATERAL generate_series(1, v.message_count) AS n
CROSS JOIN LATERAL (
    SELECT least(v.created_at + (n * interval '5 minutes'), now() - interval '1 minute') AS at
) ts;

-- Dejar la denormalización de la conversación consistente con sus mensajes:
-- es lo que lee la bandeja sin tocar la tabla particionada.
UPDATE conversations c
SET last_message_at = m.last_at,
    last_message_preview = left(m.last_body, 140),
    unread_count = CASE WHEN c.status = 'Closed' THEN 0 ELSE m.unread END,
    window_expires_at = m.last_inbound_at + interval '24 hours',
    closed_at = CASE WHEN c.status = 'Closed' THEN m.last_at + interval '20 minutes' ELSE NULL END
FROM (
    SELECT
        m.conversation_id,
        max(m.created_at) AS last_at,
        max(m.created_at) FILTER (WHERE m.direction = 'Inbound') AS last_inbound_at,
        (array_agg(m.body ORDER BY m.created_at DESC))[1] AS last_body,
        count(*) FILTER (WHERE m.direction = 'Inbound' AND m.read_at IS NULL) AS unread
    FROM messages m
    WHERE m.tenant_id IN (SELECT id FROM seed_tenants)
    GROUP BY m.conversation_id
) m
WHERE m.conversation_id = c.id;

-- -----------------------------------------------------------------------------
-- 13. Consumo de IA (ai_runs)
--
-- Una corrida por cada conversación que tiene asistente asignado, con el costo
-- y la latencia que ORB-C01 exige registrar en cada llamada al modelo.
-- -----------------------------------------------------------------------------
INSERT INTO ai_runs (id, tenant_id, agent_id, conversation_id, model, tokens_in, tokens_out, cost_usd, latency_ms, finish_reason, error, created_at)
SELECT
    gen_random_uuid(),
    c.tenant_id,
    c.ai_agent_id,
    c.id,
    CASE WHEN n % 3 = 0 THEN 'deepseek/deepseek-v4-flash' ELSE 'openai/gpt-5.6-luna' END,
    400 + (abs(hashtext(c.id::text || n::text)) % 2600),
    60 + (abs(hashtext(n::text || c.id::text)) % 400),
    round((0.00004 + random() * 0.0018)::numeric, 6),
    380 + (abs(hashtext(c.id::text)) % 4200),
    CASE WHEN n % 23 = 0 THEN 'length' ELSE 'stop' END,
    NULL,
    c.last_message_at - (n * interval '3 minutes')
FROM conversations c
CROSS JOIN LATERAL generate_series(1, 3) AS n
WHERE c.tenant_id IN (SELECT id FROM seed_tenants)
  AND c.ai_agent_id IS NOT NULL
  AND c.last_message_at IS NOT NULL;

-- -----------------------------------------------------------------------------
-- 14. Outbox y auditoría
--
-- El outbox nunca lleva PII: solo ids y enums (ORB-B04). La auditoría es
-- append-only y `orbita_app` tiene UPDATE/DELETE revocados sobre ella
-- (ORB-A15); esto corre como dueño, así que sí puede insertar.
-- -----------------------------------------------------------------------------
INSERT INTO outbox_events (tenant_id, aggregate_type, aggregate_id, event_type, payload, trace_id, occurred_at, published_at, attempts)
SELECT
    m.tenant_id,
    'Message',
    m.id,
    'message.received',
    jsonb_build_object(
        'messageId', m.id,
        'conversationId', m.conversation_id,
        'channelAccountId', c.channel_account_id,
        'contactId', c.contact_id,
        'direction', m.direction
    ),
    NULL,
    m.created_at,
    CASE WHEN random() < 0.95 THEN m.created_at + interval '600 milliseconds' ELSE NULL END,
    CASE WHEN random() < 0.95 THEN 1 ELSE 2 END
FROM messages m
JOIN conversations c ON c.id = m.conversation_id
WHERE m.tenant_id IN (SELECT id FROM seed_tenants)
  AND m.direction = 'Inbound'
  AND m.created_at > now() - interval '3 days';

INSERT INTO audit_log (tenant_id, actor_id, actor_type, action, entity_type, entity_id, diff, ip, user_agent, created_at)
SELECT
    m.tenant_id,
    m.user_id,
    'User',
    (ARRAY['membership.role_changed','channel.connected','agent.published','template.synced','subscription.changed'])[1 + (n % 5)],
    (ARRAY['Membership','ChannelAccount','AiAgent','MessageTemplate','Subscription'])[1 + (n % 5)],
    NULL,
    jsonb_build_object('nota', 'Entrada de ejemplo generada por scripts/seed-dev.sql'),
    '190.85.' || (n % 250) || '.' || ((n * 7) % 250),
    'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/141.0 Safari/537.36',
    now() - (n * interval '4 hours')
FROM memberships m
CROSS JOIN generate_series(1, 20) AS n
WHERE m.tenant_id IN (SELECT id FROM seed_tenants)
  AND m.is_active
  AND m.accepted_at IS NOT NULL;

COMMIT;

-- -----------------------------------------------------------------------------
-- Resumen de lo que quedó cargado.
-- -----------------------------------------------------------------------------
SELECT 'tenants' AS tabla, count(*) FROM tenants
UNION ALL SELECT 'users',              count(*) FROM users
UNION ALL SELECT 'memberships',        count(*) FROM memberships
UNION ALL SELECT 'plans',              count(*) FROM plans
UNION ALL SELECT 'subscriptions',      count(*) FROM subscriptions
UNION ALL SELECT 'channel_accounts',   count(*) FROM channel_accounts
UNION ALL SELECT 'contacts',           count(*) FROM contacts
UNION ALL SELECT 'conversations',      count(*) FROM conversations
UNION ALL SELECT 'messages',           count(*) FROM messages
UNION ALL SELECT 'pipelines',          count(*) FROM pipelines
UNION ALL SELECT 'pipeline_stages',    count(*) FROM pipeline_stages
UNION ALL SELECT 'opportunities',      count(*) FROM opportunities
UNION ALL SELECT 'ai_agents',          count(*) FROM ai_agents
UNION ALL SELECT 'knowledge_docs',     count(*) FROM knowledge_docs
UNION ALL SELECT 'knowledge_chunks',   count(*) FROM knowledge_chunks
UNION ALL SELECT 'ai_runs',            count(*) FROM ai_runs
UNION ALL SELECT 'message_templates',  count(*) FROM message_templates
UNION ALL SELECT 'outbox_events',      count(*) FROM outbox_events
UNION ALL SELECT 'audit_log',          count(*) FROM audit_log
ORDER BY 1;
