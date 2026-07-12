import { serve } from "https://deno.land/std@0.168.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.39.8";
import nacl from "https://esm.sh/tweetnacl@1.0.3";
// Helper to convert hex string to Uint8Array
function hexToUint8Array(hex: string): Uint8Array {
  return new Uint8Array(hex.match(/.{1,2}/g)!.map((val) => parseInt(val, 16)));
}
// Discord Interaction Types
const InteractionType = {
  PING: 1,
  APPLICATION_COMMAND: 2,
};
// Discord Interaction Response Types
const InteractionResponseType = {
  PONG: 1,
  CHANNEL_MESSAGE_WITH_SOURCE: 4,
  DEFERRED_CHANNEL_MESSAGE_WITH_SOURCE: 5,
};
const supabaseUrl = Deno.env.get("SUPABASE_URL") || "";
const supabaseServiceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") || "";
const anonKey = Deno.env.get("SUPABASE_ANON_KEY") || "";
const supabase = createClient(supabaseUrl, supabaseServiceKey);

async function editOriginalInteraction(applicationId: string, interactionToken: string, data: Record<string, unknown>) {
  const response = await fetch(
    `https://discord.com/api/v10/webhooks/${applicationId}/${interactionToken}/messages/@original`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ content: data.content }),
    },
  );
  if (!response.ok) {
    console.error("Failed to complete Discord interaction:", response.status, await response.text());
  }
}

serve(async (req) => {
  // 1. Verify signatures from Discord
  const signature = req.headers.get("X-Signature-Ed25519");
  const timestamp = req.headers.get("X-Signature-Timestamp");
  if (!signature || !timestamp) {
    // Handle outgoing notification request from our desktop client
    const headersObj: Record<string, string> = {};
    req.headers.forEach((val, key) => {
      headersObj[key] = val;
    });
    console.log("[Edge Function Debug] Incoming request headers:", JSON.stringify(headersObj, null, 2));
    const authHeader = req.headers.get("Authorization") || "";
    const token = authHeader.replace("Bearer ", "").trim();
    const apikey = req.headers.get("apikey") || "";
    let isAuthorized = false;
    if (token === supabaseServiceKey || apikey === supabaseServiceKey) {
      isAuthorized = true;
    } else if (token === anonKey || apikey === anonKey) {
      isAuthorized = true;
    } else if (token) {
      const { data: { user }, error: authError } = await supabase.auth.getUser(token);
      if (!authError && user) {
        isAuthorized = true;
      }
    }
    if (!isAuthorized) {
      return new Response("Unauthorized", { status: 401 });
    }
    try {
      const body = await req.json();
      const { channel_id, content, tts } = body;
      if (!channel_id || !content) {
        return new Response("Missing channel_id or content", { status: 400 });
      }
      const { data: channelConfig, error: channelConfigError } = await supabase
        .from("discord_channels_config")
        .select("guild_id")
        .eq("channel_id", channel_id)
        .limit(1)
        .maybeSingle();
      if (channelConfigError || !channelConfig) {
        return new Response("Discord channel is not configured for Rust+ Desktop bot notifications", { status: 403 });
      }

      const { data: botSettings } = await supabase
        .from("discord_bot_settings")
        .select("owner_steam_id")
        .eq("guild_id", channelConfig.guild_id)
        .single();
      if (!botSettings) {
        return new Response("Discord bot setup not found", { status: 403 });
      }

      const { data: ownerProfile } = await supabase
        .from("user_profiles")
        .select("subscription_tier, is_manual_supporter, premium_until")
        .eq("steam_id", botSettings.owner_steam_id)
        .single();
      const premiumUntil = ownerProfile?.premium_until ? new Date(ownerProfile.premium_until) : null;
      const ownerHasPremium =
        ownerProfile?.is_manual_supporter === true ||
        ["supporter", "developer", "lead_contributor", "lead_developer"].includes(String(ownerProfile?.subscription_tier || "free")) ||
        (premiumUntil !== null && premiumUntil.getTime() > Date.now());
      if (!ownerHasPremium) {
        return new Response("Discord bot notifications require an active premium bot owner", { status: 403 });
      }

      const botToken = Deno.env.get("DISCORD_BOT_TOKEN") || "";
      if (!botToken) {
        return new Response("DISCORD_BOT_TOKEN not configured", { status: 500 });
      }
      const processedContent = tts
        ? content.replace(/\*\*/g, "").replace(/__/g, "").replace(/\*/g, "").replace(/_/g, "")
        : content;
      const discordResponse = await fetch(
        `https://discord.com/api/v10/channels/${channel_id}/messages`,
        {
          method: "POST",
          headers: {
            Authorization: `Bot ${botToken}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            content: processedContent,
            tts: !!tts,
          }),
        }
      );
      if (!discordResponse.ok) {
        const errorText = await discordResponse.text();
        return new Response(`Discord API error: ${errorText}`, { status: 502 });
      }
      return new Response(JSON.stringify({ success: true }), {
        headers: { "Content-Type": "application/json" },
      });
    } catch (err: any) {
      return new Response(`Server error: ${err.message}`, { status: 500 });
    }
  }
  const rawBody = await req.text();
  const discordPublicKey = Deno.env.get("DISCORD_PUBLIC_KEY") || "";
  if (!discordPublicKey) {
    console.error("DISCORD_PUBLIC_KEY is not configured in Supabase env");
    return new Response("Internal server configuration error", { status: 500 });
  }
  const isVerified = nacl.sign.detached.verify(
    new TextEncoder().encode(timestamp + rawBody),
    hexToUint8Array(signature),
    hexToUint8Array(discordPublicKey)
  );
  if (!isVerified) {
    return new Response("Invalid request signature", { status: 401 });
  }
  const interaction = JSON.parse(rawBody);
  // 2. Handle Ping (Verification by Discord)
  if (interaction.type === InteractionType.PING) {
    return new Response(
      JSON.stringify({ type: InteractionResponseType.PONG }),
      {
        headers: { "Content-Type": "application/json" },
      }
    );
  }
  // 3. Handle Slash Commands
  if (interaction.type === InteractionType.APPLICATION_COMMAND) {
    EdgeRuntime.waitUntil((async () => {
      const response = await (async () => {
    try {
      const guildId = interaction.guild_id;
    const commandName = interaction.data.name;
    const commandId = interaction.data.id;
    const options = interaction.data.options || [];
    if (!guildId) {
      return new Response(
        JSON.stringify({
          type: InteractionResponseType.CHANNEL_MESSAGE_WITH_SOURCE,
          data: {
            content: "❌ Commands can only be executed on Discord servers (guilds), not in DMs.",
            flags: 64, // Ephemeral (only visible to user)
          },
        }),
        { headers: { "Content-Type": "application/json" } }
      );
    }
    // Reuses global Supabase Client
    // Look up who owns this guild
    const { data: botSettings, error: settingsError } = await supabase
      .from("discord_bot_settings")
      .select("owner_steam_id, commands_enabled, allowed_command_role_ids")
      .eq("guild_id", guildId)
      .single();
    if (settingsError || !botSettings) {
      return new Response(
        JSON.stringify({
          type: InteractionResponseType.CHANNEL_MESSAGE_WITH_SOURCE,
          data: {
            content: "❌ This server is not set up for the bot. Please link the bot in the premium settings of the Rust+ Desktop app.",
            flags: 64,
          },
        }),
        { headers: { "Content-Type": "application/json" } }
      );
    }
    const { data: ownerProfile } = await supabase
      .from("user_profiles")
      .select("subscription_tier, is_manual_supporter, premium_until")
      .eq("steam_id", botSettings.owner_steam_id)
      .single();
    const premiumUntil = ownerProfile?.premium_until ? new Date(ownerProfile.premium_until) : null;
    const ownerHasPremium =
      ownerProfile?.is_manual_supporter === true ||
      ["supporter", "developer", "lead_contributor", "lead_developer"].includes(String(ownerProfile?.subscription_tier || "free")) ||
      (premiumUntil !== null && premiumUntil.getTime() > Date.now());
    if (!ownerHasPremium) {
      return new Response(
        JSON.stringify({
          type: InteractionResponseType.CHANNEL_MESSAGE_WITH_SOURCE,
          data: {
            content: "❌ Discord bot commands require an active premium bot owner.",
            flags: 64,
          },
        }),
        { headers: { "Content-Type": "application/json" } }
      );
    }
    if (botSettings.commands_enabled === false) {
      return new Response(
        JSON.stringify({
          type: InteractionResponseType.CHANNEL_MESSAGE_WITH_SOURCE,
          data: {
            content: "❌ Discord commands are disabled for this server.",
            flags: 64,
          },
        }),
        { headers: { "Content-Type": "application/json" } }
      );
    }
    const allowedRoleIds = String(botSettings.allowed_command_role_ids || "")
      .split(",")
      .map((roleId) => roleId.trim())
      .filter((roleId) => roleId.length > 0);
    if (allowedRoleIds.length > 0) {
      const memberRoleIds = Array.isArray(interaction.member?.roles)
        ? interaction.member.roles.map((roleId: unknown) => String(roleId))
        : [];
      const hasAllowedRole = memberRoleIds.some((roleId) => allowedRoleIds.includes(roleId));
      if (!hasAllowedRole) {
        return new Response(
          JSON.stringify({
            type: InteractionResponseType.CHANNEL_MESSAGE_WITH_SOURCE,
            data: {
              content: "❌ You do not have permission to use Rust+ Desktop bot commands on this server.",
              flags: 64,
            },
          }),
          { headers: { "Content-Type": "application/json" } }
        );
      }
    }
    // Parse options (if any)
    const payload: Record<string, any> = {
      interaction_token: interaction.token,
      application_id: interaction.application_id
    };
    if (commandName === "switch" || commandName === "turret") {
      const deviceOpt = options.find((o: any) => o.name === "device" || o.name === "switch_id");
      payload.device = deviceOpt ? String(deviceOpt.value) : "";
      // Keep entity_id as fallback if it's a number
      if (deviceOpt && typeof deviceOpt.value === "number") {
        payload.entity_id = deviceOpt.value;
      }
    }
    // Map Discord command names to DB queue command types
    const commandTypeMap: Record<string, string> = {
      "switch": "toggle_switch",
      "turret": "toggle_switch",
      "heli": "heli",
      "cargo": "cargo",
      "oilrig": "oilrig",
      "deepsea": "deepsea",
      "vendor": "vendor",
      "upkeep": "upkeep",
      "devicelist": "devicelist",
      "commands": "commands",
      "map": "map",
      "mapfull": "mapfull",
    };
    const dbCommandType = commandTypeMap[commandName] ?? commandName;
    // Write to command queue
    const { data: queueItem, error: queueError } = await supabase
      .from("bot_commands_queue")
      .insert({
        guild_id: guildId,
        command_type: dbCommandType,
        payload: payload,
        status: "pending",
      })
      .select()
      .single();
    console.log("[Edge Function Debug] Command insert result:", JSON.stringify({ queueItem, queueError }, null, 2));
    if (queueError || !queueItem) {
      return new Response(
        JSON.stringify({
          type: InteractionResponseType.CHANNEL_MESSAGE_WITH_SOURCE,
          data: {
            content: "❌ Error forwarding the command to the queue.",
            flags: 64,
          },
        }),
        { headers: { "Content-Type": "application/json" } }
      );
    }
    const queueItemId = queueItem.id;

    if (dbCommandType === "map" || dbCommandType === "mapfull") {
      return new Response(
        JSON.stringify({
          type: InteractionResponseType.DEFERRED_CHANNEL_MESSAGE_WITH_SOURCE,
        }),
        { headers: { "Content-Type": "application/json" } }
      );
    }

    // Discord is already deferred, so allow slower desktop/device commands to finish.
    let replyContent = "⌛ The desktop client is currently offline or not responding. Please make sure the app is running.";
    let success = false;
    for (let i = 0; i < 60; i++) {
      await new Promise((resolve) => setTimeout(resolve, 250));
      const { data: checkItem } = await supabase
        .from("bot_commands_queue")
        .select("status, response_payload")
        .eq("id", queueItemId)
        .single();
      console.log(`[Edge Function Debug] Polling attempt ${i}, status = ${checkItem?.status}`);
      if (checkItem && (checkItem.status === "completed" || checkItem.status === "failed")) {
        try {
          // response_payload is stored as JSONB (object), not a JSON string - read directly
          const resp = typeof checkItem.response_payload === "string"
            ? JSON.parse(checkItem.response_payload)
            : checkItem.response_payload;
          replyContent = resp?.Message || resp?.message || (checkItem.status === "completed" ? "✅ Command executed!" : "❌ Command failed.");
          success = checkItem.status === "completed";
        } catch {
          replyContent = checkItem.status === "completed" ? "✅ Command executed!" : "❌ Command failed.";
        }
        break;
      }
    }
      // Return the response immediately
      return new Response(
        JSON.stringify({
          type: InteractionResponseType.CHANNEL_MESSAGE_WITH_SOURCE,
          data: {
            content: replyContent,
          },
        }),
        { headers: { "Content-Type": "application/json" } }
      );
    } catch (e: any) {
      return new Response(
        JSON.stringify({
          type: InteractionResponseType.CHANNEL_MESSAGE_WITH_SOURCE,
          data: {
            content: `❌ CRITICAL ERROR IN EDGE FUNCTION: ${e.message}`,
            flags: 64,
          },
        }),
        { headers: { "Content-Type": "application/json" } }
      );
    }
      })();
      const responseBody = await response.json();
      if (responseBody.data) {
        await editOriginalInteraction(interaction.application_id, interaction.token, responseBody.data);
      }
    })());
    return new Response(
      JSON.stringify({ type: InteractionResponseType.DEFERRED_CHANNEL_MESSAGE_WITH_SOURCE }),
      { headers: { "Content-Type": "application/json" } },
    );
  }
  return new Response("Unknown interaction type", { status: 400 });
});
