using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Services.Support;

namespace RustPlusDesktop.Tests;

[TestClass]
public class TicketPresentationTests
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 15, 30, 0, TimeSpan.FromHours(2));

    [DataTestMethod]
    [DataRow("open", TicketTone.Accent)]
    [DataRow("in_progress", TicketTone.Accent)]
    [DataRow("awaiting_user", TicketTone.Warning)]
    [DataRow("resolved", TicketTone.Success)]
    [DataRow("closed", TicketTone.Neutral)]
    [DataRow("something_new", TicketTone.Neutral)]
    [DataRow(null, TicketTone.Neutral)]
    public void ToneFor_MapsEveryStatus(string? status, TicketTone expected)
        => Assert.AreEqual(expected, TicketPresentation.ToneFor(status));

    [TestMethod]
    public void IsLocked_OnlyForResolvedAndClosed()
    {
        Assert.IsTrue(TicketPresentation.IsLocked("resolved"));
        Assert.IsTrue(TicketPresentation.IsLocked("closed"));
        Assert.IsFalse(TicketPresentation.IsLocked("open"));
        Assert.IsFalse(TicketPresentation.IsLocked("awaiting_user"));
        Assert.IsFalse(TicketPresentation.IsLocked(null));
    }

    [TestMethod]
    public void Reference_TakesLastSixAlphanumericsUppercased()
    {
        Assert.AreEqual("#3F9A1C", TicketPresentation.Reference("01jd8q2w7k3f9a1c"));
        Assert.AreEqual("#ABC123", TicketPresentation.Reference("t-ab-c1-23"));
        Assert.AreEqual("#X1", TicketPresentation.Reference("x1"));
        Assert.AreEqual("", TicketPresentation.Reference(null));
        Assert.AreEqual("", TicketPresentation.Reference("--"));
    }

    [TestMethod]
    public void ListTime_ScalesWithAge()
    {
        Assert.AreEqual("", N(TicketPresentation.ListTime(null, Now, En)));
        Assert.AreEqual("3:12 PM", N(TicketPresentation.ListTime(Now.AddMinutes(-18), Now, En)));
        Assert.AreEqual("Sat", N(TicketPresentation.ListTime(Now.AddDays(-1), Now, En)));
        Assert.AreEqual("November 10", N(TicketPresentation.ListTime(Now.AddDays(-12), Now, En)));
        Assert.AreEqual("11/22/2025", N(TicketPresentation.ListTime(Now.AddYears(-1), Now, En)));
    }

    [TestMethod]
    public void ListTime_UsesTheViewersOffset()
    {
        // 23:30 UTC on the 21st is already the 22nd at UTC+2: today, so a time rather than a weekday.
        var lateUtc = new DateTimeOffset(2026, 11, 21, 23, 30, 0, TimeSpan.Zero);
        Assert.AreEqual("1:30 AM", N(TicketPresentation.ListTime(lateUtc, Now, En)));
    }

    [TestMethod]
    public void MessageTime_AddsTheDateOnlyWhenNotToday()
    {
        Assert.AreEqual("3:12 PM", N(TicketPresentation.MessageTime(Now.AddMinutes(-18), Now, En)));
        Assert.AreEqual("November 21, 3:30 PM", N(TicketPresentation.MessageTime(Now.AddDays(-1), Now, En)));
        Assert.AreEqual("11/22/2025, 3:30 PM", N(TicketPresentation.MessageTime(Now.AddYears(-1), Now, En)));
        Assert.AreEqual("", N(TicketPresentation.MessageTime(null, Now, En)));
    }

    [TestMethod]
    public void FullTime_IsTheLongForm()
        => Assert.AreEqual("Sunday, November 22, 2026 3:30 PM", N(TicketPresentation.FullTime(Now, Now, En)));

    /// <summary>Newer ICU puts a narrow no-break space before AM/PM; compare with plain spaces.</summary>
    private static string N(string s) => s.Replace('\u202F', ' ').Replace('\u00A0', ' ');
}
