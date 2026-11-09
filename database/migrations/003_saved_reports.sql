BEGIN;
-- Private saved reports; additive and safe to repeat.
CREATE TABLE IF NOT EXISTS public.saved_reports (
    user_id integer NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    report_id integer NOT NULL REFERENCES public.reports(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id, report_id)
);
CREATE INDEX IF NOT EXISTS saved_reports_report_id_idx ON public.saved_reports(report_id);
COMMIT;
