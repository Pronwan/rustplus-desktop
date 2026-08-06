-- Table for storing the user's active Alexa server selection
create table public.user_alexa_settings (
  user_id uuid not null primary key,
  active_server_key text not null,
  steam_id text not null,
  updated_at timestamp with time zone default now()
);

alter table public.user_alexa_settings enable row level security;

-- Policy: Users can only read their own alexa settings
create policy "Users can read own alexa settings"
  on public.user_alexa_settings for select
  to authenticated
  using (user_id = auth.uid());

-- Policy: Users can insert their own alexa settings
create policy "Users can insert own alexa settings"
  on public.user_alexa_settings for insert
  to authenticated
  with check (user_id = auth.uid());

-- Policy: Users can update their own alexa settings
create policy "Users can update own alexa settings"
  on public.user_alexa_settings for update
  to authenticated
  using (user_id = auth.uid());

-- Policy: Users can delete their own alexa settings
create policy "Users can delete own alexa settings"
  on public.user_alexa_settings for delete
  to authenticated
  using (user_id = auth.uid());
