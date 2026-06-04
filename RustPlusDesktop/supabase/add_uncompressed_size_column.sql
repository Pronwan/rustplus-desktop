-- Add uncompressed_size column to map_overlays table
ALTER TABLE public.map_overlays 
ADD COLUMN IF NOT EXISTS uncompressed_size INT DEFAULT 0 NOT NULL;
