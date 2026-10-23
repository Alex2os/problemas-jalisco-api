-- Non-destructive, repeatable PostgreSQL migration. Apply BEFORE deploying this API.
BEGIN;
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
COMMIT;
