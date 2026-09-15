-- =============================================================================
-- Preparación de una base PostgreSQL administrada (Neon, RDS, Supabase…) para
-- que corran las migraciones de Órbita.
--
-- `docker-compose.yml` + `scripts/init-extensions.sql` ya dejan la base local
-- lista; una base administrada no, por dos motivos:
--
--   1. Las extensiones del DBML no vienen instaladas.
--   2. La migración ORB-A09 (AddUsersAndMemberships) nombra al rol dueño
--      explícitamente: `ALTER DEFAULT PRIVILEGES FOR ROLE orbita ...`. Si las
--      migraciones corren como cualquier otro rol, las tablas que creen las
--      migraciones posteriores nacen sin los permisos que `orbita_app` necesita
--      para leerlas y escribirlas, y la app falla en runtime sobre cada tabla
--      nueva. Por eso el rol tiene que llamarse `orbita` y tiene que ser él
--      quien corra `dotnet ef database update`.
--
-- Se ejecuta UNA vez, con el rol administrador que dé el proveedor
-- (`neondb_owner`, `postgres`, etc.), ANTES de la primera migración:
--
--     psql "<cadena del rol administrador>" -v orbita_password="'...'" \
--          -f scripts/init-managed-db.sql
--
-- Sobre el rol administrador del proveedor: en Neon, `neondb_owner` tiene
-- BYPASSRLS, así que la Row Level Security no se le aplica. No lo uses nunca
-- como el rol de runtime de la app — el aislamiento entre organizaciones
-- desaparecería sin ningún error visible. Esa es justamente la razón de que
-- existan dos roles (ver CLAUDE.md, "Two roles, two connection strings").
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- El rol dueño/migrador. CREATEROLE porque la migración ORB-A09 crea
-- `orbita_app` por su cuenta; NOSUPERUSER para que no se salte la RLS.
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'orbita') THEN
        EXECUTE format(
            'CREATE ROLE orbita WITH LOGIN PASSWORD %L CREATEROLE NOSUPERUSER NOCREATEDB',
            :orbita_password);
    END IF;
END
$$;

-- En PostgreSQL 15+ el esquema public ya no da CREATE a cualquiera.
GRANT USAGE, CREATE ON SCHEMA public TO orbita;

-- Después de esto:
--   1. dotnet tool run dotnet-ef database update ... --connection "<cadena de orbita>"
--      (por el endpoint DIRECTO del proveedor, no por el pooler)
--   2. La migración crea `orbita_app` con la contraseña de desarrollo que trae
--      escrita. Si la base es accesible desde internet, rotala:
--          ALTER ROLE orbita_app WITH PASSWORD '...';
--      y poné la nueva en `ConnectionStrings:Postgres` de tu `.env`.
