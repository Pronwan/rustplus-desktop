using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Web.WebView2.Wpf;
using RustPlusDesk.Services.GeneticsLab;
using RustPlusDesk.Views;

namespace RustPlusDesktop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class GeneticsLabTabContentTests
{
    // Another test creates the App on a worker thread that then exits, leaving Application.Current
    // pointing at resources owned by a dead thread; WPF-UI controls built later resolve their styles
    // there and throw. Such a stale App is set aside for these tests and put back afterwards.
    private static readonly System.Reflection.FieldInfo? AppInstance =
        typeof(Application).GetField("_appInstance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

    private Application? _staleApp;

    [TestInitialize]
    public void SetAsideStaleApplication()
    {
        if (Application.Current is { } app && !app.Dispatcher.Thread.IsAlive)
        {
            _staleApp = app;
            AppInstance?.SetValue(null, null);
        }
    }

    [TestCleanup]
    public void RestoreStaleApplication()
    {
        if (_staleApp != null) AppInstance?.SetValue(null, _staleApp);
        _staleApp = null;
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Open_MissingContentShowsRepairGuidanceWithoutStartingWebView(bool hasFolderWithoutIndex)
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            int browserStarts = 0;
            var view = new GeneticsLabTabContent(
                () => hasFolderWithoutIndex ? new GeneticsLabContent(folder, null, null, IsBundled: true) : null,
                _ => { browserStarts++; return Task.CompletedTask; });

            Assert.IsTrue(view.EnsureWebViewAsync().IsCompletedSuccessfully);

            Assert.AreEqual(0, browserStarts);
            Assert.AreEqual(Visibility.Visible, Element<FrameworkElement>(view, "FailurePanel").Visibility);
            Assert.AreEqual(Visibility.Collapsed, Element<FrameworkElement>(view, "WebViewHost").Visibility);
            Assert.AreEqual(Visibility.Collapsed, Element<FrameworkElement>(view, "LoadingOverlay").Visibility);
            StringAssert.Contains(Element<TextBlock>(view, "FailureMessage").Text, "reinstall RustPlus Desktop");
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [STATestMethod]
    public void Retry_AfterRuntimeFailureCreatesAFreshBrowserAndReportsPersistentFailure()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "index.html"), "<!doctype html>");
            var browsers = new List<WebView2>();
            int contentChecks = 0;
            var view = new GeneticsLabTabContent(
                () => { contentChecks++; return new GeneticsLabContent(folder, null, null, IsBundled: true); },
                browser =>
                {
                    browsers.Add(browser);
                    return Task.FromException(new InvalidOperationException("Simulated runtime failure"));
                });

            Assert.IsTrue(view.EnsureWebViewAsync().IsCompletedSuccessfully);
            Assert.IsTrue(view.EnsureWebViewAsync().IsCompletedSuccessfully);
            Assert.AreEqual(1, browsers.Count, "A visibility change must not silently clear the failure or start a second browser.");
            StringAssert.Contains(Element<TextBlock>(view, "FailureMessage").Text, "Microsoft Edge WebView2 Runtime");

            var retry = Element<Wpf.Ui.Controls.Button>(view, "RetryButton");
            retry.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, retry));

            Assert.AreEqual(2, contentChecks);
            Assert.AreEqual(2, browsers.Count);
            Assert.AreNotSame(browsers[0], browsers[1]);
            Assert.AreEqual(Visibility.Visible, Element<FrameworkElement>(view, "FailurePanel").Visibility);
            Assert.AreEqual(Visibility.Collapsed, Element<FrameworkElement>(view, "WebViewHost").Visibility);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static T Element<T>(GeneticsLabTabContent view, string name) where T : FrameworkElement =>
        (T)view.FindName(name);
}
