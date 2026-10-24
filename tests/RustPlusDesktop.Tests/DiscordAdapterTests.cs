using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Cloud;

namespace RustPlusDesktop.Tests;

[TestClass]
public class DiscordAdapterTests
{
    [TestMethod]
    public void ExtractChannelsConfig_ParsesCloudGuildsWithChannelsArray()
    {
        var json = """
        [
            {
                "id": "guild-uuid-1",
                "guild_id": "1228747157251690707",
                "commands_enabled": true,
                "channels": [
                    {
                        "id": "chan-uuid-1",
                        "notification_type": "raid",
                        "channel_id": "111111111",
                        "mention_text": "@everyone",
                        "tts_enabled": true
                    },
                    {
                        "id": "chan-uuid-2",
                        "notification_type": "events",
                        "channel_id": "222222222",
                        "mention_text": "@here",
                        "tts_enabled": false
                    }
                ]
            }
        ]
        """;

        var channels = CloudDiscordAdapter.ExtractChannelsConfig(json, "1228747157251690707");

        Assert.AreEqual(2, channels.Count);
        Assert.AreEqual("raid", channels[0].NotificationType);
        Assert.AreEqual("111111111", channels[0].ChannelId);
        Assert.AreEqual("@everyone", channels[0].MentionText);
        Assert.IsTrue(channels[0].TtsEnabled);

        Assert.AreEqual("events", channels[1].NotificationType);
        Assert.AreEqual("222222222", channels[1].ChannelId);
        Assert.AreEqual("@here", channels[1].MentionText);
        Assert.IsFalse(channels[1].TtsEnabled);
    }

    [TestMethod]
    public void ExtractChannelsConfig_ParsesLegacySupabaseDiscordChannelsConfig()
    {
        var json = """
        [
            {
                "guild_id": "1228747157251690707",
                "commands_enabled": true,
                "discord_channels_config": [
                    {
                        "id": "chan-uuid-1",
                        "notification_type": "chat",
                        "channel_id": "333333333",
                        "mention_text": "",
                        "tts_enabled": false
                    }
                ]
            }
        ]
        """;

        var channels = CloudDiscordAdapter.ExtractChannelsConfig(json, "1228747157251690707");

        Assert.AreEqual(1, channels.Count);
        Assert.AreEqual("chat", channels[0].NotificationType);
        Assert.AreEqual("333333333", channels[0].ChannelId);
    }

    [TestMethod]
    public void ExtractChannelsConfig_ParsesDirectChannelList()
    {
        var json = """
        [
            {
                "id": "chan-uuid-1",
                "notification_type": "shop",
                "channel_id": "444444444",
                "mention_text": "",
                "tts_enabled": false
            }
        ]
        """;

        var channels = CloudDiscordAdapter.ExtractChannelsConfig(json);

        Assert.AreEqual(1, channels.Count);
        Assert.AreEqual("shop", channels[0].NotificationType);
        Assert.AreEqual("444444444", channels[0].ChannelId);
    }

    [TestMethod]
    public void ExtractChannelsConfig_FiltersByTargetGuildId()
    {
        var json = """
        [
            {
                "guild_id": "other-guild",
                "channels": [
                    { "notification_type": "raid", "channel_id": "999" }
                ]
            },
            {
                "guild_id": "target-guild",
                "channels": [
                    { "notification_type": "raid", "channel_id": "888" }
                ]
            }
        ]
        """;

        var channels = CloudDiscordAdapter.ExtractChannelsConfig(json, "target-guild");

        Assert.AreEqual(1, channels.Count);
        Assert.AreEqual("888", channels[0].ChannelId);
    }

    [TestMethod]
    public void ExtractChannelsConfig_HandlesInvalidAndEmptyInputsGracefully()
    {
        Assert.AreEqual(0, CloudDiscordAdapter.ExtractChannelsConfig(null).Count);
        Assert.AreEqual(0, CloudDiscordAdapter.ExtractChannelsConfig("").Count);
        Assert.AreEqual(0, CloudDiscordAdapter.ExtractChannelsConfig("   ").Count);
        Assert.AreEqual(0, CloudDiscordAdapter.ExtractChannelsConfig("invalid-json").Count);
        Assert.AreEqual(0, CloudDiscordAdapter.ExtractChannelsConfig("[]").Count);
        Assert.AreEqual(0, CloudDiscordAdapter.ExtractChannelsConfig("{}").Count);
    }
}
