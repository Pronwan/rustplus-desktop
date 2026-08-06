-- Add mention fields to user_profiles
ALTER TABLE public.user_profiles
ADD COLUMN IF NOT EXISTS fcm_discord_webhook_url text,
ADD COLUMN IF NOT EXISTS fcm_discord_webhook_mention text;

-- Add mention field to discord_channels_config
ALTER TABLE public.discord_channels_config
ADD COLUMN IF NOT EXISTS mention_text text;
