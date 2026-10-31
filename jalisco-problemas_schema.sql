-- Jalisco Problems — PostgreSQL setup and upgrade
-- HOW TO USE:
-- 1. Open your application's database in Neon SQL Editor, pgAdmin, or another PostgreSQL query editor.
-- 2. Connect as the database owner (or an account allowed to create/alter these tables).
-- 3. Copy this ENTIRE file, paste it into the query editor, and run all statements together.
-- Works with an empty database or the existing Jalisco Problems schema.
-- Existing users, reports, likes, comments, photos and history are preserved.
-- You may run this script again. No connection string, password or role name needs editing.
-- This sets up database metadata only. Configure the API's persistent photo directory separately.

BEGIN;

CREATE TABLE IF NOT EXISTS public.users (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username varchar(20) NOT NULL UNIQUE,
    email varchar(100) NOT NULL UNIQUE,
    passwordhash varchar(255) NOT NULL,
    created_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS public.reports (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    longitude double precision NOT NULL,
    latitude double precision NOT NULL,
    ubication varchar(255) NOT NULL,
    description varchar(255),
    type varchar(20) NOT NULL,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    user_id integer NOT NULL REFERENCES public.users(id)
);

CREATE TABLE IF NOT EXISTS public.likes (
    user_id integer NOT NULL REFERENCES public.users(id),
    report_id integer NOT NULL REFERENCES public.reports(id),
    PRIMARY KEY (user_id, report_id)
);

CREATE TABLE IF NOT EXISTS public.comments (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id integer NOT NULL REFERENCES public.users(id),
    report_id integer NOT NULL REFERENCES public.reports(id),
    user_comment varchar(100) NOT NULL,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);

-- Additive application schema extensions (also distributed as migration 001).
ALTER TABLE public.reports ADD COLUMN IF NOT EXISTS status text NOT NULL DEFAULT 'reported'
    CHECK (status IN ('reported','still_present','resolved'));
ALTER TABLE public.reports ADD COLUMN IF NOT EXISTS status_updated_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE public.reports ADD COLUMN IF NOT EXISTS status_version integer NOT NULL DEFAULT 0 CHECK (status_version >= 0);
CREATE TABLE IF NOT EXISTS public.report_status_history (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    report_id integer NOT NULL REFERENCES public.reports(id),
    previous_status text CHECK (previous_status IN ('reported','still_present','resolved')),
    status text NOT NULL CHECK (status IN ('reported','still_present','resolved')),
    actor_id integer REFERENCES public.users(id),
    changed_at timestamptz NOT NULL,
    version integer NOT NULL CHECK (version >= 0),
    source text NOT NULL CHECK (source IN ('created','updated','backfill')),
    UNIQUE(report_id, version)
);
-- Existing timestamps have no timezone. Record when tracking began instead of inventing one.
INSERT INTO public.report_status_history(report_id,previous_status,status,actor_id,changed_at,version,source)
SELECT id,NULL,status,NULL,status_updated_at,status_version,'backfill' FROM public.reports
ON CONFLICT (report_id,version) DO NOTHING;
CREATE OR REPLACE FUNCTION public.record_report_status() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF TG_OP = 'INSERT' THEN
    INSERT INTO public.report_status_history(report_id,status,actor_id,changed_at,version,source)
    VALUES(NEW.id,NEW.status,NEW.user_id,NEW.status_updated_at,NEW.status_version,'created');
  ELSE
    INSERT INTO public.report_status_history(report_id,previous_status,status,actor_id,changed_at,version,source)
    VALUES(NEW.id,OLD.status,NEW.status,NEW.user_id,NEW.status_updated_at,NEW.status_version,'updated');
  END IF;
  RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS report_status_audit ON public.reports;
CREATE TRIGGER report_status_audit AFTER INSERT OR UPDATE OF status_version ON public.reports
FOR EACH ROW EXECUTE FUNCTION public.record_report_status();
CREATE TABLE IF NOT EXISTS public.report_images (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    report_id integer NOT NULL REFERENCES public.reports(id),
    user_id integer NOT NULL REFERENCES public.users(id),
    storage_key varchar(36) NOT NULL UNIQUE,
    content_type text NOT NULL CHECK (content_type = 'image/jpeg'),
    byte_size integer NOT NULL CHECK (byte_size > 0 AND byte_size <= 5242880),
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    display_order smallint NOT NULL CHECK (display_order BETWEEN 1 AND 3),
    UNIQUE(report_id,display_order)
);
-- Three available slots + uniqueness enforce the limit even outside the API.

-- Required titles for new reports (migration 002).
-- Existing reports retain NULL titles. Require a title on new inserts or explicit title changes.
ALTER TABLE public.reports ADD COLUMN IF NOT EXISTS title varchar(80);
CREATE OR REPLACE FUNCTION public.require_report_title() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.title IS NULL OR NEW.title !~ '[^[:space:]]' OR position(chr(10) in NEW.title) > 0 OR position(chr(13) in NEW.title) > 0 THEN
        RAISE EXCEPTION 'A nonblank, single-line report title is required'
            USING ERRCODE = '23514', CONSTRAINT = 'reports_title_required';
    END IF;
    RETURN NEW;
END $$;
DROP TRIGGER IF EXISTS report_title_required ON public.reports;
CREATE TRIGGER report_title_required BEFORE INSERT OR UPDATE OF title ON public.reports
FOR EACH ROW EXECUTE FUNCTION public.require_report_title();
COMMIT;
