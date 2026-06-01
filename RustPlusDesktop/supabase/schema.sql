-- Supabase Schema for Rust+ Desktop
-- CORRECT version: TEXT columns (not JSONB), no FK constraints to user_profiles

-- 1. Create Tables

CREATE TABLE IF NOT EXISTS public.user_profiles (
    steam_id TEXT PRIMARY KEY,
    discord_id TEXT UNIQUE,
    discord_name TEXT,
    subscription_tier TEXT DEFAULT 'free',
    sync_accepted BOOLEAN DEFAULT true,
    discord_roles JSONB DEFAULT '[]'::jsonb,
    is_manual_supporter BOOLEAN DEFAULT false,
    last_active_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- map_overlays: NO foreign key to user_profiles (anon users must be able to insert)
--              overlay_data is TEXT (not JSONB) to avoid type mismatch with C# string
CREATE TABLE IF NOT EXISTS public.map_overlays (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    server_key TEXT NOT NULL,
    steam_id TEXT NOT NULL,
    overlay_data TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    UNIQUE(server_key, steam_id)
);

-- smart_devices: NO foreign key to user_profiles (same reasoning)
--               device_data is TEXT (not JSONB)
CREATE TABLE IF NOT EXISTS public.smart_devices (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    server_key TEXT NOT NULL,
    steam_id TEXT NOT NULL,
    device_data TEXT NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL,
    UNIQUE(server_key, steam_id)
);

-- 2. Enable Row Level Security (RLS)

ALTER TABLE public.user_profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.map_overlays ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.smart_devices ENABLE ROW LEVEL SECURITY;

-- 3. Define Policies

-- user_profiles: Only Discord-authenticated users (premium check, admin panel)
CREATE POLICY "Profiles readable by authenticated users" 
    ON public.user_profiles FOR SELECT 
    USING (auth.role() = 'authenticated');

CREATE POLICY "Profiles writable by authenticated users" 
    ON public.user_profiles FOR INSERT 
    WITH CHECK (auth.role() = 'authenticated');

CREATE POLICY "Profiles updatable by authenticated users" 
    ON public.user_profiles FOR UPDATE 
    USING (auth.role() = 'authenticated');

-- map_overlays: Open to anon key (steam_id is public, no sensitive data)
CREATE POLICY "Overlays readable by all"
    ON public.map_overlays FOR SELECT
    USING (true);

CREATE POLICY "Overlays writable by all"
    ON public.map_overlays FOR INSERT
    WITH CHECK (true);

CREATE POLICY "Overlays updatable by all"
    ON public.map_overlays FOR UPDATE
    USING (true);

CREATE POLICY "Overlays deletable by all"
    ON public.map_overlays FOR DELETE
    USING (true);

-- smart_devices: Open to anon key
CREATE POLICY "Devices readable by all"
    ON public.smart_devices FOR SELECT
    USING (true);

CREATE POLICY "Devices writable by all"
    ON public.smart_devices FOR INSERT
    WITH CHECK (true);

CREATE POLICY "Devices updatable by all"
    ON public.smart_devices FOR UPDATE
    USING (true);

CREATE POLICY "Devices deletable by all"
    ON public.smart_devices FOR DELETE
    USING (true);

-- 4. Trigger for updated_at
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
   NEW.updated_at = timezone('utc'::text, now());
   RETURN NEW;
END;
$$ language 'plpgsql';

CREATE TRIGGER update_map_overlays_updated_at
BEFORE UPDATE ON public.map_overlays
FOR EACH ROW EXECUTE PROCEDURE update_updated_at_column();

CREATE TRIGGER update_smart_devices_updated_at
BEFORE UPDATE ON public.smart_devices
FOR EACH ROW EXECUTE PROCEDURE update_updated_at_column();

-- 5. Protect Admin Fields Trigger
CREATE OR REPLACE FUNCTION protect_admin_fields()
RETURNS TRIGGER AS $DO$
BEGIN
   IF (NEW.subscription_tier IS DISTINCT FROM OLD.subscription_tier) OR (NEW.is_manual_supporter IS DISTINCT FROM OLD.is_manual_supporter) THEN
       IF current_setting('request.jwt.claims', true)::jsonb->>'role' = 'service_role' THEN
           RETURN NEW;
       END IF;

       IF NOT EXISTS (
           SELECT 1 FROM public.user_profiles 
           WHERE discord_id = auth.uid()::text 
           AND subscription_tier IN ('developer', 'lead_contributor')
       ) THEN
           RAISE EXCEPTION 'Not authorized to change admin fields';
       END IF;
   END IF;
   RETURN NEW;
END;
$DO$ language 'plpgsql';

CREATE TRIGGER tr_protect_admin_fields
BEFORE UPDATE ON public.user_profiles
FOR EACH ROW EXECUTE PROCEDURE protect_admin_fields();
