-- ============================================================
-- MIGRATION: Fix map_overlays and smart_devices tables
-- Run this in the Supabase Dashboard > SQL Editor
-- ============================================================

-- STEP 1: Drop all existing RLS policies (old AND new) to start fresh
DROP POLICY IF EXISTS "Users can view map overlays" ON public.map_overlays;
DROP POLICY IF EXISTS "Users can insert/update their own map overlays" ON public.map_overlays;
DROP POLICY IF EXISTS "Overlays readable by all" ON public.map_overlays;
DROP POLICY IF EXISTS "Overlays writable by all" ON public.map_overlays;
DROP POLICY IF EXISTS "Overlays updatable by all" ON public.map_overlays;
DROP POLICY IF EXISTS "Overlays deletable by all" ON public.map_overlays;

DROP POLICY IF EXISTS "Users can view smart devices" ON public.smart_devices;
DROP POLICY IF EXISTS "Users can insert/update their own smart devices" ON public.smart_devices;
DROP POLICY IF EXISTS "Devices readable by all" ON public.smart_devices;
DROP POLICY IF EXISTS "Devices writable by all" ON public.smart_devices;
DROP POLICY IF EXISTS "Devices updatable by all" ON public.smart_devices;
DROP POLICY IF EXISTS "Devices deletable by all" ON public.smart_devices;

DROP POLICY IF EXISTS "Profiles readable by authenticated users" ON public.user_profiles;
DROP POLICY IF EXISTS "Profiles writable by authenticated users" ON public.user_profiles;
DROP POLICY IF EXISTS "Profiles updatable by authenticated users" ON public.user_profiles;
DROP POLICY IF EXISTS "Users can view all profiles" ON public.user_profiles;
DROP POLICY IF EXISTS "Users can update own profile" ON public.user_profiles;
DROP POLICY IF EXISTS "Users can insert their own profile" ON public.user_profiles;

-- STEP 2: Drop FK constraints so any steam_id can insert without Discord login
ALTER TABLE public.map_overlays DROP CONSTRAINT IF EXISTS map_overlays_steam_id_fkey;
ALTER TABLE public.smart_devices DROP CONSTRAINT IF EXISTS smart_devices_steam_id_fkey;

-- STEP 3: Convert overlay_data from JSONB to TEXT
-- (C# model sends a JSON string, not a JSONB object)
ALTER TABLE public.map_overlays ALTER COLUMN overlay_data TYPE TEXT USING overlay_data::text;

-- STEP 4: Convert device_data from JSONB to TEXT
ALTER TABLE public.smart_devices ALTER COLUMN device_data TYPE TEXT USING device_data::text;

-- STEP 5: Ensure UNIQUE constraints exist for upsert ON CONFLICT
ALTER TABLE public.map_overlays DROP CONSTRAINT IF EXISTS map_overlays_server_key_steam_id_key;
ALTER TABLE public.map_overlays ADD CONSTRAINT map_overlays_server_key_steam_id_key UNIQUE (server_key, steam_id);

ALTER TABLE public.smart_devices DROP CONSTRAINT IF EXISTS smart_devices_server_key_steam_id_key;
ALTER TABLE public.smart_devices ADD CONSTRAINT smart_devices_server_key_steam_id_key UNIQUE (server_key, steam_id);

-- STEP 6: New RLS policies

-- user_profiles: Discord-only (admin panel stays secure)
CREATE POLICY "Profiles readable by authenticated users"
    ON public.user_profiles FOR SELECT
    USING (auth.role() = 'authenticated');

CREATE POLICY "Profiles writable by authenticated users"
    ON public.user_profiles FOR INSERT
    WITH CHECK (auth.role() = 'authenticated');

CREATE POLICY "Profiles updatable by authenticated users"
    ON public.user_profiles FOR UPDATE
    USING (auth.role() = 'authenticated');

-- map_overlays: Open to anon key (no sensitive data, partitioned by steam_id)
CREATE POLICY "Overlays readable by all"
    ON public.map_overlays FOR SELECT USING (true);

CREATE POLICY "Overlays writable by all"
    ON public.map_overlays FOR INSERT WITH CHECK (true);

CREATE POLICY "Overlays updatable by all"
    ON public.map_overlays FOR UPDATE USING (true);

CREATE POLICY "Overlays deletable by all"
    ON public.map_overlays FOR DELETE USING (true);

-- smart_devices: Open to anon key
CREATE POLICY "Devices readable by all"
    ON public.smart_devices FOR SELECT USING (true);

CREATE POLICY "Devices writable by all"
    ON public.smart_devices FOR INSERT WITH CHECK (true);

CREATE POLICY "Devices updatable by all"
    ON public.smart_devices FOR UPDATE USING (true);

CREATE POLICY "Devices deletable by all"
    ON public.smart_devices FOR DELETE USING (true);

-- Done! Verify with:
-- SELECT table_name, column_name, data_type FROM information_schema.columns
--   WHERE table_schema = 'public' AND table_name IN ('map_overlays','smart_devices')
--   AND column_name IN ('overlay_data','device_data');
-- Expected: data_type = 'text' for both
