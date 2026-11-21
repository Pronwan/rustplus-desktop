using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RustPlusDesk.ViewModels;

namespace RustPlusDesk.Views;

public partial class RaidCalculatorView : UserControl
{
    private readonly RaidCalculatorViewModel _viewModel = new();
    private bool _initialized;
    private bool _compactLayout;

    public RaidCalculatorView()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    public event RoutedEventHandler? CloseRequested;

    private async void RaidCalculatorView_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout(ActualWidth);
        if (_initialized) return;
        _initialized = true;
        await _viewModel.InitializeAsync();
    }

    private void RaidRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (PlanPanel is null) return; // XAML is still loading.

        _compactLayout = ArrangePanes(width, CompactPaneTabs, CatalogColumn, ShoppingColumn,
            CatalogSpacer, ShoppingSpacer, CatalogPanel, ShoppingPanel);
        UpdatePaneVisibility();
    }

    internal static bool ArrangePanes(double width, FrameworkElement tabs, ColumnDefinition catalogColumn,
        ColumnDefinition shoppingColumn, ColumnDefinition catalogSpacer, ColumnDefinition shoppingSpacer,
        Border catalog, Border shopping)
    {
        bool compact = width < 900;
        bool roomy = width >= 1080;
        tabs.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        // Every compact pane occupies the plan column; the wide rails keep their original widths.
        catalogColumn.Width = new GridLength(compact ? 0 : roomy ? 330 : 292);
        shoppingColumn.Width = new GridLength(compact ? 0 : roomy ? 300 : 272);
        catalogSpacer.Width = shoppingSpacer.Width = new GridLength(compact ? 0 : 10);
        Grid.SetColumn(catalog, compact ? 2 : 0);
        Grid.SetColumn(shopping, compact ? 2 : 4);
        return compact;
    }

    internal void RevealTutorialTarget(string? targetId) => SelectTutorialPane(targetId, CatalogTab, PlanTab);

    internal static void SelectTutorialPane(string? targetId, RadioButton catalog, RadioButton plan)
    {
        switch (targetId)
        {
            case "Raid.Search":
                catalog.IsChecked = true;
                break;
            case "Raid.Plan" or "Raid.Methods":
                plan.IsChecked = true;
                break;
        }
    }

    private void CompactPane_Checked(object sender, RoutedEventArgs e)
    {
        if (_compactLayout) UpdatePaneVisibility();
    }

    private void UpdatePaneVisibility()
    {
        CatalogPanel.Visibility = PaneVisibility(_compactLayout, CatalogTab.IsChecked);
        PlanPanel.Visibility = PaneVisibility(_compactLayout, PlanTab.IsChecked);
        ShoppingPanel.Visibility = PaneVisibility(_compactLayout, ShoppingTab.IsChecked);
    }

    internal static Visibility PaneVisibility(bool compact, bool? selected) =>
        !compact || selected == true ? Visibility.Visible : Visibility.Collapsed;

    private void AddTarget_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RaidTargetCardViewModel target)
            _viewModel.AddTarget(target);
    }

    private void Increment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RaidPlanItemViewModel item) item.Quantity++;
    }

    private void Decrement_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RaidPlanItemViewModel item) item.Quantity--;
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RaidPlanItemViewModel item) _viewModel.Duplicate(item);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RaidPlanItemViewModel item) _viewModel.Remove(item);
    }

    private void UseCheapest_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RaidPlanItemViewModel item) item.UseCheapest();
    }

    private void SmartSource_Changed(object sender, RoutedEventArgs e)
    {
        if ((sender as CheckBox)?.DataContext is RaidGlobalMethodChoice choice)
            _viewModel.SetSourceSelected(choice, ((CheckBox)sender).IsChecked == true);
    }

    private void ClearPlan_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasPlan) return;
        if (MessageBox.Show(RustPlusDesk.Properties.Resources.GetString("CodeUiClearEveryTargetFromThisRaidPlan"), RustPlusDesk.Properties.Resources.GetString("UiClearRaidPlan"), MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
            _viewModel.Clear();
    }

    private async void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasPlan) return;
        try { await SetClipboardTextAsync(_viewModel.BuildSummary()); }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, RustPlusDesk.Properties.Resources.GetString("CodeUiCouldNotCopySummary"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static Task SetClipboardTextAsync(string text)
    {
        const int clipboardBusy = unchecked((int)0x800401D0);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    completion.SetResult();
                    return;
                }
                catch (COMException exception) when (exception.HResult == clipboardBusy && attempt < 49)
                {
                    Thread.Sleep(100);
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                    return;
                }
            }
        }) { IsBackground = true, Name = "Raid summary clipboard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);

    private void Quantity_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

    private void Quantity_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text) || e.DataObject.GetData(DataFormats.Text) is not string text ||
            !int.TryParse(text, out int value) || value is < 1 or > 9999)
            e.CancelCommand();
    }
}
