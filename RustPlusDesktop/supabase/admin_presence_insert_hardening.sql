-- ============================================================
-- MIGRATION: Harden user_profiles admin fields on INSERT
-- Run this if admin_presence_migration.sql was already executed
-- before INSERT protection was added.
-- ============================================================

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
