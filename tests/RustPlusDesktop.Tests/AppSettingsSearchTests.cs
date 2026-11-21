using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Views;
using Resources = RustPlusDesk.Properties.Resources;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesktop.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AppSettingsSearchTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // The app ships translations under lang\<culture> and resolves them itself at startup; the test
    // host does not run that code, so the same lookup is registered here against the app's output.
    [ClassInitialize]
    public static void LoadAppTranslations(TestContext _)
    {
        string testOutput = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string tfm = Path.GetFileName(testOutput);
        string configuration = Path.GetFileName(Path.GetDirectoryName(testOutput)!);
        string root = Path.GetFullPath(Path.Combine(testOutput, "../../../../.."));
        string lang = Path.Combine(root, "src", "RustPlusDesktop", "bin", configuration, tfm, "lang");
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (name.Name?.EndsWith(".resources", StringComparison.OrdinalIgnoreCase) != true || string.IsNullOrEmpty(name.CultureName))
                return null;
            string path = Path.Combine(lang, name.CultureName, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }

    [STATestMethod]
    public void Search_SkipsUnavailableControlsAndFindsNewlyAvailableControls()
    {
        var view = CreateOverlay();
        var testCall = new Button { Content = "Test callback", Visibility = Visibility.Collapsed };
        var revokeCall = new Button { Content = "Revoke callback" };
        var approvalPanel = new StackPanel { IsEnabled = false };
        approvalPanel.Children.Add(revokeCall);
        Section(view, "SectionOfflineAlerts").Children.Add(testCall);
        Section(view, "SectionOfflineAlerts").Children.Add(approvalPanel);

        CollectionAssert.AreEqual(Array.Empty<FrameworkElement>(), Search(view, "callback"));

        testCall.Visibility = Visibility.Visible;
        approvalPanel.IsEnabled = true;

        CollectionAssert.AreEquivalent(new FrameworkElement[] { testCall, revokeCall }, Search(view, "callback"));

        testCall.Visibility = Visibility.Hidden;
        CollectionAssert.AreEqual(new FrameworkElement[] { revokeCall }, Search(view, "callback"));
    }

    [STATestMethod]
    public void Search_FindsAvailableControlsInInactiveCategoriesAndClosedExpanders()
    {
        var view = CreateOverlay();
        var nestedControl = new Button { Content = "Nested settings action" };
        var unavailableGroup = new StackPanel { Visibility = Visibility.Collapsed };
        unavailableGroup.Children.Add(new Button { Content = "Nested settings action" });
        var body = new StackPanel();
        body.Children.Add(unavailableGroup);
        body.Children.Add(nestedControl);
        var expander = new WpfUi.CardExpander { Content = body, IsExpanded = false };
        var section = Section(view, "SectionCloudFeatures");
        section.Children.Add(expander);
        Invoke(view, "ShowSettingsCategory", "alerts", null);

        CollectionAssert.AreEqual(new FrameworkElement[] { nestedControl }, Search(view, "nested settings action"));
        Assert.AreEqual(Visibility.Collapsed, section.Visibility, "Searching should not show another category.");
        Assert.IsFalse(expander.IsExpanded, "Searching should not open cards until selection.");
    }

    [STATestMethod]
    public void Search_IndexAndBreadcrumbsUseTheCurrentLanguage()
    {
        var previousCulture = Resources.Culture;
        try
        {
            Resources.Culture = CultureInfo.GetCultureInfo("en-US");
            var view = CreateOverlay();
            var button = new Button { Content = "Sample action" };
            Section(view, "SectionBehavior").Children.Add(button);
            CollectionAssert.AreEqual(new FrameworkElement[] { button }, Search(view, "sample action"));

            Resources.Culture = CultureInfo.GetCultureInfo("fr-FR");
            button.Content = "Action exemple";
            var expectedSection = Resources.GetString("Behavior");
            var expectedCategory = Resources.GetString("General");
            Assert.AreNotEqual("Behavior", expectedSection);
            Assert.AreNotEqual("General", expectedCategory);

            CollectionAssert.AreEqual(new FrameworkElement[] { button }, Search(view, "exemple"));
            var result = Field<ItemsControl>(view, "SettingsSearchResults").Items[0];
            Assert.AreEqual(expectedSection, result.GetType().GetProperty("SectionTitle")!.GetValue(result));
            Assert.AreEqual(expectedCategory, result.GetType().GetProperty("CategoryTitle")!.GetValue(result));
            CollectionAssert.AreEqual(new FrameworkElement[] { Section(view, "SectionBehavior") }, Search(view, expectedSection));
        }
        finally
        {
            Resources.Culture = previousCulture;
        }
    }

    [STATestMethod]
    public void Search_NoMatchesUsesLocalizedFeedback()
    {
        var previousCulture = Resources.Culture;
        try
        {
            Resources.Culture = CultureInfo.GetCultureInfo("fr-FR");
            var view = CreateOverlay();

            Search(view, "f8b459a7");

            Assert.AreEqual(Resources.GetString("CodeUiNoResultsFoundMatchingFilter"), Field<TextBlock>(view, "SettingsDetailSubtitle").Text);
        }
        finally
        {
            Resources.Culture = previousCulture;
        }
    }

    // Exercise the overlay's search and navigation handlers against real WPF controls without
    // loading the rest of the settings UI (which connects to the app's settings/services).
    private static AppSettingsOverlay CreateOverlay()
    {
        var view = (AppSettingsOverlay)RuntimeHelpers.GetUninitializedObject(typeof(AppSettingsOverlay));
        foreach (var field in typeof(AppSettingsOverlay).GetFields(Members)
                     .Where(field => field.Name.StartsWith("Section", StringComparison.Ordinal) && field.FieldType == typeof(StackPanel)))
        {
            field.SetValue(view, new StackPanel());
        }

        SetField(view, "SettingsCategoryPage", new Grid());
        SetField(view, "SettingsDetailPage", new Grid { Visibility = Visibility.Collapsed });
        SetField(view, "SettingsDetailTitle", new WpfUi.TextBlock());
        SetField(view, "SettingsDetailSubtitle", new WpfUi.TextBlock());
        SetField(view, "SettingsCategoryList", new WpfUi.ListView());
        SetField(view, "SettingsSectionList", new ListBox());
        SetField(view, "SettingsSearchBox", new WpfUi.TextBox());
        SetField(view, "SettingsSearchResults", new ItemsControl());
        SetField(view, "SettingsSearchResultsScroller", new ScrollViewer());
        SetField(view, "SettingsScrollViewer", new ScrollViewer());
        InitializeEmptyCollection(view, "_settingsOptions");
        InitializeEmptyCollection(view, "_settingsHighlights");
        Invoke(view, "InitializeSettingsNavigation");
        return view;
    }

    private static FrameworkElement[] Search(AppSettingsOverlay view, string query)
    {
        var searchBox = Field<WpfUi.TextBox>(view, "SettingsSearchBox");
        searchBox.Text = query;
        Invoke(view, "SettingsSearchBox_TextChanged", searchBox, new TextChangedEventArgs(TextBoxBase.TextChangedEvent, UndoAction.None));
        return Field<ItemsControl>(view, "SettingsSearchResults").Items.Cast<object>().Select(result =>
            (FrameworkElement)result.GetType().GetProperty("Target")!.GetValue(result)!).ToArray();
    }

    private static StackPanel Section(AppSettingsOverlay view, string name) => Field<StackPanel>(view, name);

    private static T Field<T>(AppSettingsOverlay view, string name) => (T)typeof(AppSettingsOverlay).GetField(name, Members)!.GetValue(view)!;

    private static void SetField(AppSettingsOverlay view, string name, object value) => typeof(AppSettingsOverlay).GetField(name, Members)!.SetValue(view, value);

    private static void InitializeEmptyCollection(AppSettingsOverlay view, string name)
    {
        var field = typeof(AppSettingsOverlay).GetField(name, Members)!;
        var empty = Array.CreateInstance(field.FieldType.GetGenericArguments()[0], 0);
        field.SetValue(view, field.FieldType.IsInterface ? empty : Activator.CreateInstance(field.FieldType));
    }

    private static void Invoke(AppSettingsOverlay view, string method, params object?[] args) => typeof(AppSettingsOverlay).GetMethod(method, Members)!.Invoke(view, args);
}
