import { serve } from "https://deno.land/std@0.168.0/http/server.ts"
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.7.1"

const GUILD_ID = "1501962264826216598";
const ROLE_LEAD_DEV = "1501967651696541867";
const ROLE_LEAD_CONTRIB = "1501968098801090590";
const ROLE_MODERATOR = "1501968745608909001";
const ROLE_COMM_MGR = "1505589772343836844";
const ROLE_CREATOR = "1501981081623331018";
const ROLE_PATREON = "1502371823101284492";
const ROLE_SUPPORTER = "1502365221904187403";

const SUPPORTER_ROLES = [
  ROLE_LEAD_DEV,
  ROLE_LEAD_CONTRIB,
  ROLE_MODERATOR,
  ROLE_COMM_MGR,
  ROLE_CREATOR,
  ROLE_PATREON,
  ROLE_SUPPORTER
];

serve(async (req) => {
  if (req.method === 'OPTIONS') {
    return new Response('ok', { headers: { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': 'authorization, x-client-info, apikey, content-type' } })
  }

  try {
    const supabaseClient = createClient(
      Deno.env.get('SUPABASE_URL') ?? '',
      Deno.env.get('SUPABASE_SERVICE_ROLE_KEY') ?? ''
    )

    // Verify caller JWT
    const authHeader = req.headers.get('Authorization')!
    const token = authHeader.replace('Bearer ', '')
    const { data: { user }, error: authError } = await supabaseClient.auth.getUser(token)

    if (authError || !user) {
      return new Response(JSON.stringify({ error: 'Unauthorized' }), { status: 401 })
    }

    const discordId = user.user_metadata?.provider_id || user.identities?.[0]?.id;
    if (!discordId) {
      return new Response(JSON.stringify({ error: 'No Discord identity found for user' }), { status: 400 })
    }

    const body = await req.json().catch(() => ({}));
    const providerToken = body.providerToken;
    const botToken = Deno.env.get('DISCORD_BOT_TOKEN');

    let discordRoles: string[] = [];
    let discordName = "";
    let discordRes;

    if (botToken) {
      // Best way: Use Discord Bot Token to fetch guild member by discordId directly
      discordRes = await fetch(`https://discord.com/api/v10/guilds/${GUILD_ID}/members/${discordId}`, {
        headers: {
          'Authorization': `Bot ${botToken}`
        }
      });
    } else if (providerToken) {
      // Fallback: Use OAuth user provider token
      discordRes = await fetch(`https://discord.com/api/v10/users/@me/guilds/${GUILD_ID}/member`, {
        headers: {
          'Authorization': `Bearer ${providerToken}`
        }
      });
    } else {
      return new Response(JSON.stringify({ error: 'Neither DISCORD_BOT_TOKEN is set in Supabase Secrets nor providerToken was supplied.' }), { status: 400 })
    }

    if (discordRes.ok) {
      const memberData = await discordRes.json();
      discordRoles = memberData.roles || [];
      discordName = memberData.user?.username || memberData.nick || "";
    } else {
      console.warn("Discord API fetch failed.", await discordRes.text());
    }

    // Check manual override
    let manualOverride = false;
    const { data: profile } = await supabaseClient
      .from('user_profiles')
      .select('is_manual_supporter')
      .eq('discord_id', discordId)
      .single();
      
    manualOverride = profile?.is_manual_supporter || false;

    let tier = 'free';
    if (discordRoles.includes(ROLE_LEAD_DEV)) tier = 'developer';
    else if (discordRoles.includes(ROLE_LEAD_CONTRIB)) tier = 'lead_contributor';
    else if (SUPPORTER_ROLES.some(r => discordRoles.includes(r)) || manualOverride) tier = 'supporter';

    // Update Profile
    await supabaseClient
      .from('user_profiles')
      .update({
        discord_roles: discordRoles,
        subscription_tier: tier,
        ...(discordName ? { discord_name: discordName } : {})
      })
      .eq('discord_id', discordId)

    return new Response(JSON.stringify({ tier, roles: discordRoles, manualOverride }), {
      headers: { 'Content-Type': 'application/json' }
    })
  } catch (error) {
    return new Response(JSON.stringify({ error: error.message }), { status: 500 })
  }
})
