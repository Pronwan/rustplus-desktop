-- ============================================================
-- MIGRATION: Admin security + user activity/team presence
-- Run this in Supabase Dashboard > SQL Editor after migration_fix.sql
-- ============================================================

-- 1) Add profile columns used by the Admin Panel and presence heartbeat.
ALTER TABLE public.user_profiles
    ADD COLUMN IF NOT EXISTS user_id UUID,
    ADD COLUMN IF NOT EXISTS is_online BOOLEAN DEFAULT false,
    ADD COLUMN IF NOT EXISTS current_server_key TEXT DEFAULT '',
    ADD COLUMN IF NOT EXISTS current_server_name TEXT DEFAULT '',
    ADD COLUMN IF NOT EXISTS team_member_count INTEGER DEFAULT 0,
    ADD COLUMN IF NOT EXISTS team_members_json TEXT DEFAULT '[]',
    ADD COLUMN IF NOT EXISTS manual_premium_at TIMESTAMP WITH TIME ZONE;

CREATE INDEX IF NOT EXISTS idx_user_profiles_user_id ON public.user_profiles(user_id);
CREATE INDEX IF NOT EXISTS idx_user_profiles_last_active_at ON public.user_profiles(last_active_at);

-- 2) Helper: current caller is an admin if their authenticated Supabase user_id
-- maps to a developer/lead profile. The jwt/user_metadata fallback helps old rows
-- until the app has written user_id once.
CREATE OR REPLACE FUNCTION public.is_admin_user()
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM public.user_profiles p
        WHERE p.subscription_tier IN ('developer', 'lead_contributor', 'lead_developer')
          AND (
              p.user_id = auth.uid()
              OR p.discord_id = auth.uid()::text
              OR p.discord_id = (auth.jwt() -> 'user_metadata' ->> 'provider_id')
              OR p.discord_id = (auth.jwt() -> 'app_metadata' ->> 'provider_id')
          )
    );
$$;

-- 3) Replace broad profile policies.
DROP POLICY IF EXISTS "Profiles readable by authenticated users" ON public.user_profiles;
DROP POLICY IF EXISTS "Profiles writable by authenticated users" ON public.user_profiles;
DROP POLICY IF EXISTS "Profiles updatable by authenticated users" ON public.user_profiles;
DROP POLICY IF EXISTS "Users can view all profiles" ON public.user_profiles;
DROP POLICY IF EXISTS "Users can update own profile" ON public.user_profiles;
DROP POLICY IF EXISTS "Users can insert their own profile" ON public.user_profiles;
DROP POLICY IF EXISTS "Profiles readable by owner or admins" ON public.user_profiles;
DROP POLICY IF EXISTS "Profiles insertable by owner" ON public.user_profiles;
DROP POLICY IF EXISTS "Profiles updatable by owner or admins" ON public.user_profiles;

CREATE POLICY "Profiles readable by owner or admins"
    ON public.user_profiles FOR SELECT
    USING (
        auth.role() = 'authenticated'
        AND (
            user_id = auth.uid()
            OR discord_id = (auth.jwt() -> 'user_metadata' ->> 'provider_id')
            OR discord_id = (auth.jwt() -> 'app_metadata' ->> 'provider_id')
            OR public.is_admin_user()
        )
    );

CREATE POLICY "Profiles insertable by owner"
    ON public.user_profiles FOR INSERT
    WITH CHECK (
        auth.role() = 'authenticated'
        AND (
            user_id = auth.uid()
            OR user_id IS NULL
        )
    );

CREATE POLICY "Profiles updatable by owner or admins"
    ON public.user_profiles FOR UPDATE
    USING (
        auth.role() = 'authenticated'
        AND (
            user_id = auth.uid()
            OR discord_id = (auth.jwt() -> 'user_metadata' ->> 'provider_id')
            OR discord_id = (auth.jwt() -> 'app_metadata' ->> 'provider_id')
            OR public.is_admin_user()
        )
    )
    WITH CHECK (
        auth.role() = 'authenticated'
        AND (
            user_id = auth.uid()
            OR user_id IS NULL
            OR discord_id = (auth.jwt() -> 'user_metadata' ->> 'provider_id')
            OR discord_id = (auth.jwt() -> 'app_metadata' ->> 'provider_id')
            OR public.is_admin_user()
        )
    );

-- 4) Protect premium/admin fields server-side.
CREATE OR REPLACE FUNCTION protect_admin_fields()
RETURNS TRIGGER AS $$
BEGIN
   IF NEW.user_id IS NULL THEN
      NEW.user_id = auth.uid();
   END IF;

   IF TG_OP = 'INSERT' THEN
      IF COALESCE(NEW.subscription_tier, 'free') <> 'free'
         OR COALESCE(NEW.is_manual_supporter, false) <> false
         OR NEW.manual_premium_at IS NOT NULL THEN

          IF current_setting('request.jwt.claims', true)::jsonb->>'role' = 'service_role' THEN
              RETURN NEW;
          END IF;

          IF NOT public.is_admin_user() THEN
              RAISE EXCEPTION 'Not authorized to set admin fields';
          END IF;
      END IF;
   END IF;

   IF TG_OP = 'UPDATE'
      AND (
          NEW.subscription_tier IS DISTINCT FROM OLD.subscription_tier
          OR NEW.is_manual_supporter IS DISTINCT FROM OLD.is_manual_supporter
          OR NEW.manual_premium_at IS DISTINCT FROM OLD.manual_premium_at
      ) THEN

       IF current_setting('request.jwt.claims', true)::jsonb->>'role' = 'service_role' THEN
           RETURN NEW;
       END IF;

       IF NOT public.is_admin_user() THEN
           RAISE EXCEPTION 'Not authorized to change admin fields';
       END IF;
   END IF;

   IF TG_OP = 'UPDATE'
      AND NEW.is_manual_supporter = true
      AND OLD.is_manual_supporter IS DISTINCT FROM true
      AND NEW.manual_premium_at IS NULL THEN
      NEW.manual_premium_at = timezone('utc'::text, now());
   END IF;

   IF TG_OP = 'INSERT'
      AND NEW.is_manual_supporter = true
      AND NEW.manual_premium_at IS NULL THEN
      NEW.manual_premium_at = timezone('utc'::text, now());
   END IF;

   IF NEW.is_manual_supporter = false THEN
      NEW.manual_premium_at = NULL;
   END IF;

   RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_protect_admin_fields ON public.user_profiles;
CREATE TRIGGER tr_protect_admin_fields
BEFORE INSERT OR UPDATE ON public.user_profiles
FOR EACH ROW EXECUTE PROCEDURE protect_admin_fields();

-- 5) Optional helper for the Admin Panel / SQL checks.
-- Users are considered online if the app heartbeat touched them recently.
-- SELECT steam_id, discord_name, is_online, last_active_at
-- FROM public.user_profiles
-- WHERE is_online = true AND last_active_at > timezone('utc'::text, now()) - interval '5 minutes';
