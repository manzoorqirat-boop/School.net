-- 002_notice_priority_to_text.sql
--
-- Run ONCE against the Railway Postgres database, together with the matching
-- deploy. Idempotent — re-running is safe.
--
--   railway connect Postgres     (or psql "$DATABASE_URL")
--   \i 002_notice_priority_to_text.sql
--
-- ─────────────────────────────────────────────────────────────────────────────
-- WHY
--
-- notices.priority was created as a native enum (notice_priority). Writing to
-- it failed with:
--
--   InvalidCastException: Writing values of '<CLR type>' is not supported for
--   parameters having DataTypeName '<PG type>'
--
-- A native enum needs HasPostgresEnum, dsb.MapEnum and the CREATE TYPE to all
-- agree, and any mismatch shows up only at runtime on the first write. This
-- converts the column to plain text with a CHECK, which gives the same
-- integrity with none of that coupling.
--
-- The bootstrap in Program.cs cannot do this itself: it only CREATEs tables and
-- ADDs columns, never changes the type of an existing one.
-- ─────────────────────────────────────────────────────────────────────────────

BEGIN;

-- 1. enum → text. The USING cast reads each existing label as its own name, so
--    any rows already stored keep their value. (There are almost certainly
--    none — no notice has ever saved successfully — but this must not assume
--    an empty table.)
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name   = 'notices'
          AND column_name  = 'priority'
          AND data_type    = 'USER-DEFINED'
    ) THEN
        ALTER TABLE notices
            ALTER COLUMN priority TYPE text USING priority::text;
    END IF;
END
$$;

-- 2. The CHECK the enum used to provide. Added here because the table already
--    exists, so the model's CREATE TABLE (which carries this constraint) is
--    skipped as a duplicate on every boot.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_notices_priority'
    ) THEN
        ALTER TABLE notices
            ADD CONSTRAINT ck_notices_priority
            CHECK (priority IN ('normal', 'important', 'urgent'));
    END IF;
END
$$;

-- 3. Retire the type. Guarded by pg_depend rather than a bare DROP: if any
--    other column still uses it the DROP would fail and roll back the whole
--    transaction, undoing steps 1 and 2 for no good reason.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_type WHERE typname = 'notice_priority')
       AND NOT EXISTS (
           SELECT 1
           FROM pg_depend d
           JOIN pg_type t ON t.oid = d.refobjid
           WHERE t.typname = 'notice_priority'
             AND d.deptype = 'n'
       )
    THEN
        DROP TYPE notice_priority;
    END IF;
END
$$;

COMMIT;

-- ─────────────────────────────────────────────────────────────────────────────
-- VERIFY
--
--   SELECT data_type FROM information_schema.columns
--    WHERE table_name = 'notices' AND column_name = 'priority';   -- 'text'
--
--   SELECT conname FROM pg_constraint
--    WHERE conname = 'ck_notices_priority';                       -- 1 row
-- ─────────────────────────────────────────────────────────────────────────────
