-- Tabelle für die Cloud-Worker Server-Verbindungen
create table public.user_servers (
  user_id uuid not null,
  steam_id text not null,
  server_ip text not null,
  server_port integer not null,
  player_token text not null,
  updated_at timestamp with time zone default now(),
  primary key (steam_id, server_ip, server_port)
);

alter table public.user_servers enable row level security;

-- Policy: Nutzer können nur ihre eigenen Server sehen und bearbeiten
create policy "Users can read own servers"
  on public.user_servers for select
  to authenticated
  using (user_id = auth.uid());

create policy "Users can insert own servers"
  on public.user_servers for insert
  to authenticated
  with check (user_id = auth.uid());

create policy "Users can update own servers"
  on public.user_servers for update
  to authenticated
  using (user_id = auth.uid());

create policy "Users can delete own servers"
  on public.user_servers for delete
  to authenticated
  using (user_id = auth.uid());

-- Der Service Role (für den Worker) darf alles lesen
