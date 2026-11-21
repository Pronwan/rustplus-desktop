using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Models;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class MainWindowPriorityFixTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    [TestMethod]
    public void ViewPlayersShortcut_StartsHiddenAndUsesDedicatedNavigation()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var xaml = XDocument.Load(Path.Combine(root, "src/RustPlusDesktop/MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var shortcut = xaml.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "BtnShowOnline");

        Assert.AreEqual("Collapsed", (string?)shortcut.Attribute("Visibility"));
        Assert.AreEqual("BtnViewPlayers_Click", (string?)shortcut.Attribute("Click"));
    }

    [STATestMethod]
    public void ViewPlayersShortcut_RetiredWorkspaceDoesNotOpenAnotherTab()
    {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var shortcut = new Wpf.Ui.Controls.Button { Visibility = Visibility.Visible };
        SetField(window, "BtnShowOnline", shortcut);

        window.ApplyPlayersTabVisibility();

        Assert.AreEqual(Visibility.Collapsed, shortcut.Visibility);
    }

    [STATestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ServerDeleteConfirmation_RespectsPrivacyMode(bool privacyMode)
    {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var confirmation = new TextBlock();
        var overlay = new Grid { Visibility = Visibility.Collapsed };
        SetField(window, "TxtDeleteConfirmation", confirmation);
        SetField(window, "DeleteConfirmationOverlay", overlay);
        SetField(window, "ServerDeleteCancelButton", new Wpf.Ui.Controls.Button());
        SetField(window, "_abbreviateNames", privacyMode);
        var profile = new ServerProfile { Name = "Private Server 48291" };

        window.Server_Delete_Click(new Button { Tag = profile }, new RoutedEventArgs());

        Assert.AreEqual(Visibility.Visible, overlay.Visibility);
        Assert.AreEqual(!privacyMode, confirmation.Text.Contains(profile.Name, StringComparison.Ordinal));
        Assert.AreSame(profile, typeof(MainWindow).GetField("_serverToDelete", Fields)!.GetValue(window));
    }

    private static void SetField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, Fields)!.SetValue(window, value);
}
