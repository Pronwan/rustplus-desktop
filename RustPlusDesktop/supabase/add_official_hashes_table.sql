-- Migration: Create private.official_hashes table to whitelist client binaries
-- Only service_role (edge function) can read/write this table.

CREATE SCHEMA IF NOT EXISTS private;

CREATE TABLE IF NOT EXISTS private.official_hashes (
    hash TEXT PRIMARY KEY,
    version TEXT DEFAULT 'unknown' NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT timezone('utc'::text, now()) NOT NULL
);

-- Enable Row Level Security (RLS)
ALTER TABLE private.official_hashes ENABLE ROW LEVEL SECURITY;

-- Allow only service_role to access this table
CREATE POLICY "service_role_only"
    ON private.official_hashes
    USING (current_setting('request.jwt.claims', true)::jsonb->>'role' = 'service_role')
    WITH CHECK (current_setting('request.jwt.claims', true)::jsonb->>'role' = 'service_role');
