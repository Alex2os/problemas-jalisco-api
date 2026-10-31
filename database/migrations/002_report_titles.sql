-- Apply after 001, before starting the updated API. No report data is rewritten.
BEGIN;
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
