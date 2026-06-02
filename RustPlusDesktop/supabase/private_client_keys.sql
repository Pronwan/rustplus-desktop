-- Secure table mapping steam_id → client public key + hashed recovery mnemonic
-- Edge function (auth-handshake) is the only writer using service_role key.
-- No direct anon/authenticated access.

CREATE SCHEMA IF NOT EXISTS private;

CREATE TABLE IF NOT EXISTS private.client_keys (
    steam_id TEXT PRIMARY KEY REFERENCES public.user_profiles(steam_id) ON DELETE CASCADE,
    public_key TEXT NOT NULL,
    recovery_hash TEXT NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

ALTER TABLE private.client_keys ENABLE ROW LEVEL SECURITY;

-- Only service_role (edge function) can read/write this table.
CREATE POLICY "service_role_only"
    ON private.client_keys
    USING (current_setting('request.jwt.claims', true)::jsonb->>'role' = 'service_role')
    WITH CHECK (current_setting('request.jwt.claims', true)::jsonb->>'role' = 'service_role');

-- Trigger: updated_at on change
CREATE OR REPLACE FUNCTION private.update_client_keys_updated_at()
RETURNS TRIGGER AS $$
BEGIN
   NEW.updated_at = timezone('utc'::text, now());
   RETURN NEW;
END;
$$ language 'plpgsql';

CREATE TRIGGER tr_client_keys_updated_at
    BEFORE UPDATE ON private.client_keys
    FOR EACH ROW EXECUTE PROCEDURE private.update_client_keys_updated_at();
