-- ==========================================
-- ALEXA TESTING PIN SETUP (TEMPORARY FOR CERTIFICATION)
-- ==========================================

-- 1. SET TEST PIN '999999' FOR USER 933b9c3d-e621-48b2-953a-3ecc27303485
-- Run this query in Supabase to set the test PIN for the certification process.
-- This merges "alexa_pin": "999999" into the existing fcm_config JSON.
UPDATE public.user_fcm_credentials
SET fcm_config = fcm_config || '{"alexa_pin": "999999"}'::jsonb
WHERE user_id = '933b9c3d-e621-48b2-953a-3ecc27303485';


-- 2. REVERT / DELETE TEST PIN AFTER CERTIFICATION
-- Run this query in Supabase to remove the test PIN and restore the database to its original state.
-- This deletes the "alexa_pin" key from the fcm_config JSON.
UPDATE public.user_fcm_credentials
SET fcm_config = fcm_config - 'alexa_pin'
WHERE user_id = '933b9c3d-e621-48b2-953a-3ecc27303485';
