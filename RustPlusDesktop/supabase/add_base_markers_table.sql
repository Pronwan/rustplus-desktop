-- Migration: Create base_markers table and policies

CREATE TABLE IF NOT EXISTS public.base_markers (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    server_key TEXT NOT NULL,
    steam_id TEXT NOT NULL,
    marker_data TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    UNIQUE(server_key, steam_id)
);

ALTER TABLE public.base_markers ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Bases readable by all" ON public.base_markers;
CREATE POLICY "Bases readable by all"
    ON public.base_markers FOR SELECT
    USING (true);

DROP POLICY IF EXISTS "Bases writable by all" ON public.base_markers;
CREATE POLICY "Bases writable by all"
    ON public.base_markers FOR INSERT
    WITH CHECK (true);

DROP POLICY IF EXISTS "Bases updatable by all" ON public.base_markers;
CREATE POLICY "Bases updatable by all"
    ON public.base_markers FOR UPDATE
    USING (true);

DROP POLICY IF EXISTS "Bases deletable by all" ON public.base_markers;
CREATE POLICY "Bases deletable by all"
    ON public.base_markers FOR DELETE
    USING (true);

CREATE OR REPLACE TRIGGER update_base_markers_updated_at
BEFORE UPDATE ON public.base_markers
FOR EACH ROW EXECUTE PROCEDURE update_updated_at_column();

-- Add max_screenshots_per_base to tier_limits if not exists
ALTER TABLE public.tier_limits ADD COLUMN IF NOT EXISTS max_screenshots_per_base INT;

-- Populate values for max_screenshots_per_base
UPDATE public.tier_limits SET max_screenshots_per_base = 1 WHERE tier_code = 'free';
UPDATE public.tier_limits SET max_screenshots_per_base = 5 WHERE tier_code = 'supporter';
UPDATE public.tier_limits SET max_screenshots_per_base = 5 WHERE tier_code = 'developer';
UPDATE public.tier_limits SET max_screenshots_per_base = 5 WHERE tier_code = 'lead_contributor';
UPDATE public.tier_limits SET max_screenshots_per_base = 5 WHERE tier_code = 'lead_developer';
