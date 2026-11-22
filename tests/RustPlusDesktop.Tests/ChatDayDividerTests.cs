using System.Globalization;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Converters;
using RustPlusDesk.Models;

namespace RustPlusDesktop.Tests;

[TestClass]
public class ChatDayDividerTests
{
    private static ChatLine At(DateTime? local) => new() { Body = "x", SentAt = local };

    private static readonly DateTime Day1 = new(2026, 11, 20, 23, 50, 0, DateTimeKind.Local);
    private static readonly DateTime Day2 = new(2026, 11, 21, 0, 2, 0, DateTimeKind.Local);

    [TestMethod]
    public void FirstTimedLine_OpensItsDay()
    {
        var lines = new[] { At(Day1), At(Day1.AddMinutes(1)) };
        Assert.AreEqual(Day1.Date, ChatDayDivider.DividerDay(lines, 0));
        Assert.IsNull(ChatDayDivider.DividerDay(lines, 1));
    }

    [TestMethod]
    public void DividerAppearsOnlyWhereTheDayChanges()
    {
        var lines = new[] { At(Day1), At(Day2), At(Day2.AddMinutes(3)) };
        Assert.AreEqual(Day2.Date, ChatDayDivider.DividerDay(lines, 1));
        Assert.IsNull(ChatDayDivider.DividerDay(lines, 2));
    }

    [TestMethod]
    public void UntimedLines_GetNoDivider_AndAreSkippedWhenLookingBack()
    {
        var lines = new[] { At(Day1), At(null), At(Day1.AddMinutes(2)) };
        Assert.IsNull(ChatDayDivider.DividerDay(lines, 1));
        Assert.IsNull(ChatDayDivider.DividerDay(lines, 2));
    }

    [TestMethod]
    public void OutOfRangeIndex_ReturnsNull()
    {
        var lines = new[] { At(Day1) };
        Assert.IsNull(ChatDayDivider.DividerDay(lines, -1));
        Assert.IsNull(ChatDayDivider.DividerDay(lines, 1));
    }

    [TestMethod]
    public void SameLocalDay_RequiresBothTimes()
    {
        Assert.IsTrue(ChatDayDivider.IsSameLocalDay(At(Day1), At(Day1.AddMinutes(5))));
        Assert.IsFalse(ChatDayDivider.IsSameLocalDay(At(Day1), At(Day2)));
        Assert.IsFalse(ChatDayDivider.IsSameLocalDay(At(Day1), At(null)));
    }

    [TestMethod]
    public void Label_UsesWeekdayAndDate_AndAddsTheYearOnlyForOtherYears()
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        Assert.AreEqual("Saturday, November 21", ChatDayDivider.Label(Day2.Date, Day2.Date, en));
        Assert.AreEqual("Wednesday, December 31, 2025",
            ChatDayDivider.Label(new DateTime(2025, 12, 31), Day2.Date, en));
    }

    [TestMethod]
    public void Converter_ReturnsVisibilityAndLabelForTheItemInItsList()
    {
        var first = At(Day1);
        var second = At(Day1.AddMinutes(1));
        var third = At(Day2);
        var source = new[] { first, second, third };
        var converter = new ChatDayDividerConverter();

        Assert.AreEqual(Visibility.Visible, converter.Convert(new object[] { first, source }, typeof(Visibility), "visibility", CultureInfo.InvariantCulture));
        Assert.AreEqual(Visibility.Collapsed, converter.Convert(new object[] { second, source }, typeof(Visibility), "visibility", CultureInfo.InvariantCulture));
        Assert.AreEqual(Visibility.Visible, converter.Convert(new object[] { third, source }, typeof(Visibility), "visibility", CultureInfo.InvariantCulture));
        Assert.AreEqual("", converter.Convert(new object[] { second, source }, typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.AreNotEqual("", converter.Convert(new object[] { third, source }, typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Converter_HandlesUnsetBindingValues()
    {
        var converter = new ChatDayDividerConverter();
        Assert.AreEqual(Visibility.Collapsed,
            converter.Convert(new object[] { DependencyProperty.UnsetValue, DependencyProperty.UnsetValue }, typeof(Visibility), "visibility", CultureInfo.InvariantCulture));
    }
}
