-- 001_notices_birthdays.sql
--
-- FIRST hand-written migration in this repo. There is no 000, and there are no
-- EF migrations either — the schema has been created from the model at boot
-- (GenerateCreateScript) since day one. Number future files upward from here.
--
-- Run ONCE against the Railway Postgres database, before or after deploying
-- the matching build. Every statement is idempotent, so re-running is safe.
--
--   railway connect Postgres     (or psql "$DATABASE_URL")
--   \i 001_notices_birthdays.sql
--
-- ─────────────────────────────────────────────────────────────────────────────
-- WHY THIS FILE EXISTS
--
-- The boot-time bootstrap in Program.cs runs GenerateCreateScript() and only
-- ever CREATEs. It never ALTERs. That is fine for `notices`, which is a new
-- table and will be created automatically on the next deploy — but `users`
-- already exists, so its CREATE TABLE is skipped wholesale as a duplicate and
-- the new `dob` column goes with it. Without the ALTER below, every request to
-- /api/birthdays fails with:
--
--   42703: column u.dob does not exist
--
-- which the exception middleware reports to the client as a bare
-- "Internal server error" — the same shape as the fee_invoices outage.
-- ─────────────────────────────────────────────────────────────────────────────

BEGIN;

-- 1. Staff date of birth. NULL for every existing row; the widget skips nulls.
ALTER TABLE users ADD COLUMN IF NOT EXISTS dob date;

-- 2. The notice_priority enum. Normally emitted by the bootstrap alongside the
--    notices table, but created here too so this file stands alone if the
--    table was somehow created without it.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'notice_priority') THEN
        CREATE TYPE notice_priority AS ENUM ('normal', 'important', 'urgent');
    END IF;
END
$$;

COMMIT;

-- ─────────────────────────────────────────────────────────────────────────────
-- VERIFY
--
--   SELECT column_name FROM information_schema.columns
--    WHERE table_name = 'users' AND column_name = 'dob';        -- 1 row
--
--   SELECT to_regclass('public.notices');                        -- not null
--                                                                -- (after deploy)
--
-- If `notices` is still NULL after a deploy, the bootstrap decided the schema
-- was complete. Check the boot log for the "Application schema already present"
-- line versus the "N of M model table(s) are missing" one.
-- ─────────────────────────────────────────────────────────────────────────────
