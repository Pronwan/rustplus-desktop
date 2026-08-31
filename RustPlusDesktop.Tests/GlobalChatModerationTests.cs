using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;

namespace RustPlusDesktop.Tests;

[TestClass]
public class GlobalChatModerationTests
{
    [TestMethod]
    public void StaffRoleBadges_AdminAndSuperAdmin_ReturnExpectedBadges()
    {
        var superAdminBadge = ChatLine.GetBadgeForRoles(new[] { "super_admin" });
        Assert.IsNotNull(superAdminBadge);
        Assert.AreEqual("SUPER ADMIN", superAdminBadge.DisplayText);

        var adminBadge = ChatLine.GetBadgeForRoles(new[] { "admin" });
        Assert.IsNotNull(adminBadge);
        Assert.AreEqual("ADMIN", adminBadge.DisplayText);

        var modBadge = ChatLine.GetBadgeForRoles(new[] { "moderator" });
        Assert.IsNotNull(modBadge);
        Assert.AreEqual("MOD", modBadge.DisplayText);

        var cmBadge = ChatLine.GetBadgeForRoles(new[] { "community_manager" });
        Assert.IsNotNull(cmBadge);
        Assert.AreEqual("CM", cmBadge.DisplayText);

        var customBadge = ChatLine.GetBadgeForRoles(new[] { "vip_host" });
        Assert.IsNotNull(customBadge);
        Assert.AreEqual("VIP_HOST", customBadge.DisplayText);

        var emptyBadge = ChatLine.GetBadgeForRoles(Array.Empty<string>());
        Assert.IsNull(emptyBadge);

        var nullBadge = ChatLine.GetBadgeForRoles(null);
        Assert.IsNull(nullBadge);
    }

    [TestMethod]
    public void SystemSanctionEvent_Timeout_PropertiesAndBrushes()
    {
        var sanction = new SystemSanctionEvent
        {
            Id = "test-timeout-1",
            Type = "system_sanction",
            Action = "issued",
            Kind = "timeout",
            Scope = "chat",
            Reason = "Spamming links",
            Duration = "1 hour",
            Target = new SanctionTarget
            {
                Id = "target-123",
                Name = "Gamer123",
                DisplayName = "Gamer123",
                SteamId = "76561198000000001",
            },
            Moderator = new SanctionModerator
            {
                Id = "mod-456",
                Name = "AdminGuy",
                DisplayName = "AdminGuy",
                Roles = new[] { "admin" },
            },
            CreatedAt = new DateTime(2026, 8, 31, 14, 0, 0, DateTimeKind.Utc),
        };

        Assert.IsTrue(sanction.IsTimeout);
        Assert.IsFalse(sanction.IsBan);
        Assert.IsFalse(sanction.IsLifted);
        Assert.IsTrue(sanction.HasDuration);
        Assert.AreEqual("[1 hour]", sanction.DurationBadgeText);
        Assert.AreEqual("CHAT TIMEOUT", sanction.HeaderTitle);
        Assert.AreEqual("Gamer123", sanction.TargetDisplayName);
        Assert.AreEqual("76561198000000001", sanction.TargetSteamId);
        Assert.AreEqual("(76561198000000001)", sanction.TargetSteamIdFormatted);
        Assert.AreEqual("AdminGuy", sanction.ModeratorDisplayName);
        Assert.IsNotNull(sanction.ModeratorRoleBadge);
        Assert.AreEqual("ADMIN", sanction.ModeratorRoleBadge.DisplayText);
        Assert.IsNotNull(sanction.CardBackgroundBrush);
        Assert.IsNotNull(sanction.CardBorderBrush);

        var line = ChatLine.FromSanction(sanction);
        Assert.IsTrue(line.IsSystemSanction);
        Assert.AreEqual(sanction.Id, line.Id);
        Assert.AreEqual(sanction.Reason, line.Body);
        Assert.AreEqual(sanction, line.SanctionEvent);
    }

    [TestMethod]
    public void SystemSanctionEvent_Ban_PropertiesAndBrushes()
    {
        var sanction = new SystemSanctionEvent
        {
            Id = "test-ban-1",
            Type = "system_sanction",
            Action = "issued",
            Kind = "ban",
            Scope = "chat",
            Reason = "Severe toxicity",
            Duration = null,
            Target = new SanctionTarget
            {
                Id = "bad-user",
                Name = "Troll",
                DisplayName = "TrollKing",
                SteamId = "76561198000000099",
            },
            Moderator = new SanctionModerator
            {
                Id = "super-mod",
                Name = "LeadMod",
                DisplayName = "LeadMod",
                Roles = new[] { "moderator" },
            },
            CreatedAt = new DateTime(2026, 8, 31, 14, 15, 0, DateTimeKind.Utc),
        };

        Assert.IsFalse(sanction.IsTimeout);
        Assert.IsTrue(sanction.IsBan);
        Assert.IsFalse(sanction.IsLifted);
        Assert.IsFalse(sanction.HasDuration);
        Assert.AreEqual("PERMANENT BAN", sanction.HeaderTitle);
        Assert.IsNotNull(sanction.CardBackgroundBrush);
        Assert.IsNotNull(sanction.CardBorderBrush);
    }

    [TestMethod]
    public void SystemSanctionEvent_Lifted_PropertiesAndBrushes()
    {
        var sanction = new SystemSanctionEvent
        {
            Id = "test-lifted-1",
            Type = "system_sanction",
            Action = "lifted",
            Kind = "timeout",
            Scope = "chat",
            Reason = "Appeal approved",
            Target = new SanctionTarget
            {
                Id = "user-1",
                DisplayName = "ReformedUser",
            },
            Moderator = new SanctionModerator
            {
                Id = "mod-1",
                DisplayName = "SupportAdmin",
            },
            CreatedAt = new DateTime(2026, 8, 31, 14, 30, 0, DateTimeKind.Utc),
        };

        Assert.IsFalse(sanction.IsTimeout);
        Assert.IsFalse(sanction.IsBan);
        Assert.IsTrue(sanction.IsLifted);
        Assert.AreEqual("SANCTION LIFTED", sanction.HeaderTitle);
        Assert.IsNotNull(sanction.CardBackgroundBrush);
        Assert.IsNotNull(sanction.CardBorderBrush);
    }

    [TestMethod]
    public void ChatSlowModeEvent_PopulatesCorrectly()
    {
        var sm = new ChatSlowModeEvent
        {
            Seconds = 15,
            UpdatedById = "mod-1",
            UpdatedByName = "ModAlex",
        };

        Assert.AreEqual(15, sm.Seconds);
        Assert.AreEqual("mod-1", sm.UpdatedById);
        Assert.AreEqual("ModAlex", sm.UpdatedByName);
    }
}
