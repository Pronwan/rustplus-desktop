using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace RustPlusDesk.Features.Tutorials.Views;

public partial class TutorialsPage : UserControl
{
    private readonly ObservableCollection<TutorialCardViewModel> _cards = [];
    private ITutorialRegistry? _registry;
    private ITutorialProgressStore? _store;
    private ITutorialService? _service;
    private bool _loading;
    private bool _eventsAttached;
    private readonly Queue<string> _recommendedQueue = new();

    public TutorialsPage()
    {
        InitializeComponent();
        var view = CollectionViewSource.GetDefaultView(_cards);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TutorialCardViewModel.Category)));
        TutorialCards.ItemsSource = view;
    }

    public event EventHandler? CloseRequested;

    public async Task InitializeAsync(ITutorialRegistry registry, ITutorialProgressStore store, ITutorialService service)
    {
        _registry = registry;
        _store = store;
        _service = service;
        if (!_eventsAttached)
        {
            service.TutorialCompleted += RecommendedTutorialCompleted;
            _eventsAttached = true;
        }
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_registry is null || _store is null) return;
        _loading = true;
        FlowDirection = System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var preferences = await _store.GetPreferencesAsync();
        AutoBasicToggle.IsChecked = preferences.AutoStartBasicTutorial;
        AutoFeaturesToggle.IsChecked = preferences.AutoStartNewFeatureTutorials;
        _cards.Clear();
        foreach (var definition in _registry.Tutorials.OrderBy(x => x.DisplayOrder))
        {
            var progress = await _store.GetAsync(definition);
            _cards.Add(new TutorialCardViewModel(definition, progress));
        }
        _loading = false;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null || sender is not FrameworkElement { Tag: string id }) return;
        var card = _cards.First(x => x.Id == id);
        Visibility = Visibility.Collapsed;
        if (card.Status is TutorialStatus.InProgress or TutorialStatus.Updated) await _service.ContinueAsync(id);
        else await _service.StartAsync(id);
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null || sender is not FrameworkElement { Tag: string id }) return;
        await _service.ResetAsync(id);
        Visibility = Visibility.Collapsed;
        await _service.StartAsync(id);
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null || sender is not FrameworkElement { Tag: string id }) return;
        await _service.ResetAsync(id);
        await RefreshAsync();
    }

    private async void StartRecommended_Click(object sender, RoutedEventArgs e)
    {
        if (_registry is null || _service is null) return;
        _recommendedQueue.Clear();
        foreach (string tutorialId in _cards.Where(x => x.IsRecommended && x.Status != TutorialStatus.Completed).Select(x => x.Id))
            _recommendedQueue.Enqueue(tutorialId);
        if (_recommendedQueue.Count == 0)
            foreach (string tutorialId in _registry.Tutorials.Where(x => x.IsRecommended).Select(x => x.Id)) _recommendedQueue.Enqueue(tutorialId);
        Visibility = Visibility.Collapsed;
        await _service.ContinueAsync(_recommendedQueue.Dequeue());
    }

    private async void ContinueLast_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null || _service is null) return;
        var preferences = await _store.GetPreferencesAsync();
        if (preferences.LastTutorialId is null) return;
        Visibility = Visibility.Collapsed;
        await _service.ContinueAsync(preferences.LastTutorialId);
    }

    private async void RestartAll_Click(object sender, RoutedEventArgs e)
    {
        if (_registry is null || _service is null) return;
        await _service.ResetAllAsync();
        Visibility = Visibility.Collapsed;
        await _service.StartAsync(_registry.Tutorials.First(x => x.IsRecommended).Id);
    }

    private async void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null) return;
        await _service.ResetAllAsync();
        await RefreshAsync();
    }

    private async void Preferences_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || _store is null) return;
        var preferences = await _store.GetPreferencesAsync();
        preferences.AutoStartBasicTutorial = AutoBasicToggle.IsChecked == true;
        preferences.AutoStartNewFeatureTutorials = AutoFeaturesToggle.IsChecked == true;
        await _store.SavePreferencesAsync(preferences);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async void RecommendedTutorialCompleted(object? sender, TutorialEventArgs e)
    {
        if (_service is not null && _recommendedQueue.Count > 0)
            await _service.ContinueAsync(_recommendedQueue.Dequeue());
    }
}

public sealed class TutorialCardViewModel
{
    public TutorialCardViewModel(TutorialDefinition definition, TutorialProgress progress)
    {
        Id = definition.Id;
        Category = Properties.Resources.GetString($"Tutorials.Category.{definition.Category}");
        if (Category.StartsWith("Tutorials.Category.", StringComparison.Ordinal)) Category = definition.Category;
        Title = Properties.Resources.GetString(definition.TitleKey);
        Description = Properties.Resources.GetString(definition.DescriptionKey);
        Status = progress.Status;
        IsRecommended = definition.IsRecommended;
        IsNewFeature = definition.IsNewFeature;
        int completed = progress.CompletedStepIds.Count(id => definition.Steps.Any(x => x.Id == id));
        ProgressPercent = definition.Steps.Count == 0 ? 0 : completed * 100d / definition.Steps.Count;
        ProgressLabel = string.Format(Properties.Resources.GetString("Tutorials.Common.StepCount"), completed, definition.Steps.Count);
        StatusLabel = Properties.Resources.GetString($"Tutorials.Status.{progress.Status}");
        PrimaryActionLabel = Properties.Resources.GetString(progress.Status is TutorialStatus.InProgress or TutorialStatus.Updated
            ? "Tutorials.Common.Continue" : "Tutorials.Common.Start");
    }

    public string Id { get; }
    public string Category { get; }
    public string Title { get; }
    public string Description { get; }
    public TutorialStatus Status { get; }
    public bool IsRecommended { get; }
    public bool IsNewFeature { get; }
    public bool IsUpdated => Status == TutorialStatus.Updated;
    public double ProgressPercent { get; }
    public string ProgressLabel { get; }
    public string StatusLabel { get; }
    public string PrimaryActionLabel { get; }
}
