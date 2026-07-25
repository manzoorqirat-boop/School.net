-- 003_notice_priority_fix.sql
--
-- SUPERSEDES 002_notice_priority_to_text.sql. If you ran 002 and it errored,
-- that is expected and harmless — it was wrapped in a transaction and rolled
-- back completely. Run this instead; it does not care whether 002 ran.
--
--   railway connect Postgres     (or psql "$DATABASE_URL")
--   \i 003_notice_priority_fix.sql
--
-- ─────────────────────────────────────────────────────────────────────────────
-- WHAT WENT WRONG
--
-- notices.priority is an INTEGER column, not the notice_priority enum. The
-- build that created the table did not have the enum registered, so EF used its
-- default enum→int mapping. Two failures followed from that one fact:
--
--   InvalidCastException  — dsb.MapEnum told Npgsql to write a PG enum value
--                           into a column that is actually int
--   42804                 — after the switch to text, writing text into int
--
-- 002 assumed the column was the enum type and skipped its conversion step,
-- then aborted on a CHECK comparing an integer column to text literals.
--
-- This file converts from WHATEVER the column is today, and orders the steps so
-- the CHECK is only added once the column is genuinely text.
-- ─────────────────────────────────────────────────────────────────────────────

BEGIN;

-- 1. Whatever it is now → text.
--
--    int mapping follows the CLR declaration order in NoticePriority:
--    Normal = 0, Important = 1, Urgent = 2. Anything unexpected degrades to
--    'normal' rather than failing the migration — a wrong-but-valid priority
--    is recoverable, a half-applied schema is not.
DO $$
DECLARE
    col_type text;
BEGIN
    SELECT data_type INTO col_type
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name   = 'notices'
      AND column_name  = 'priority';

    IF col_type IS NULL THEN
        RAISE NOTICE 'notices.priority not found — nothing to convert.';

    ELSIF col_type = 'text' THEN
        RAISE NOTICE 'notices.priority is already text — skipping conversion.';

    ELSE
        -- A DEFAULT bound to the old type blocks ALTER COLUMN TYPE.
        ALTER TABLE notices ALTER COLUMN priority DROP DEFAULT;

        IF col_type IN ('integer', 'smallint', 'bigint') THEN
            ALTER TABLE notices
                ALTER COLUMN priority TYPE text
                USING CASE priority
                          WHEN 1 THEN 'important'
                          WHEN 2 THEN 'urgent'
                          ELSE        'normal'
                      END;
        ELSE
            -- USER-DEFINED (the enum), or anything else castable by name.
            ALTER TABLE notices
                ALTER COLUMN priority TYPE text
                USING priority::text;
        END IF;
    END IF;
END
$$;

-- 2. NOT NULL + default, now that the column is text.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'notices'
          AND column_name = 'priority' AND data_type = 'text'
    ) THEN
        UPDATE notices SET priority = 'normal' WHERE priority IS NULL;
        ALTER TABLE notices ALTER COLUMN priority SET DEFAULT 'normal';
        ALTER TABLE notices ALTER COLUMN priority SET NOT NULL;
    END IF;
END
$$;

-- 3. The CHECK. Only valid once the column is text — this is the step that
--    aborted 002.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'notices'
          AND column_name = 'priority' AND data_type = 'text'
    ) AND NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_notices_priority'
    ) THEN
        ALTER TABLE notices
            ADD CONSTRAINT ck_notices_priority
            CHECK (priority IN ('normal', 'important', 'urgent'));
    END IF;
END
$$;

-- 4. Retire the enum type if it exists and nothing else depends on it.
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
-- VERIFY — all three should be true before retrying a save
--
--   SELECT data_type, is_nullable, column_default
--     FROM information_schema.columns
--    WHERE table_name = 'notices' AND column_name = 'priority';
--        -- text | NO | 'normal'::text
--
--   SELECT conname FROM pg_constraint WHERE conname = 'ck_notices_priority';
--        -- 1 row
--
--   SELECT 1 FROM pg_type WHERE typname = 'notice_priority';
--        -- 0 rows
-- ─────────────────────────────────────────────────────────────────────────────
