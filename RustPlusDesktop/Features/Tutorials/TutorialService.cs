using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RustPlusDesk.Features.Tutorials;

public interface ITutorialNavigationCoordinator
{
    Task PrepareAsync(TutorialStep step, CancellationToken cancellationToken = default);
    ITutorialStateSnapshot CaptureState();
}

public sealed record TutorialPresentation(
    string TutorialTitle, string StepTitle, string Description, string? Tip,
    int StepNumber, int StepCount, TutorialPlacement Placement, TutorialTarget Target,
    bool CanGoBack, bool IsLastStep, bool IsTargetUnavailable, bool AllowTargetInteraction);

public interface ITutorialPresenter
{
    bool IsVisible { get; }
    void Show(TutorialPresentation presentation);
    void Hide();
}

public interface ITutorialService
{
    bool IsRunning { get; }
    TutorialDefinition? ActiveTutorial { get; }
    TutorialStep? ActiveStep { get; }
    event EventHandler<TutorialEventArgs>? TutorialStarted;
    event EventHandler<TutorialEventArgs>? StepShown;
    event EventHandler<TutorialEventArgs>? StepCompleted;
    event EventHandler<TutorialEventArgs>? StepSkipped;
    event EventHandler<TutorialEventArgs>? TutorialCompleted;
    event EventHandler<TutorialEventArgs>? TutorialSkipped;
    event EventHandler<TutorialEventArgs>? TutorialCancelled;
    event EventHandler<TutorialEventArgs>? TargetResolutionFailed;
    Task StartAsync(string tutorialId, string? startStepId = null, CancellationToken cancellationToken = default);
    Task ContinueAsync(string tutorialId, CancellationToken cancellationToken = default);
    Task NextAsync();
    Task BackAsync();
    Task SkipAsync();
    Task FinishAsync();
    Task CancelAsync();
    Task StartForCurrentPageAsync();
    Task ResetAsync(string tutorialId);
    Task ResetAllAsync();
    Task RefreshAsync();
}

public sealed class TutorialService(
    ITutorialRegistry registry,
    ITutorialProgressStore progressStore,
    ITutorialTargetResolver targetResolver,
    ITutorialNavigationCoordinator navigation,
    ITutorialPresenter presenter,
    ITutorialContext context,
    FrameworkElement root) : ITutorialService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<TutorialStep> _steps = [];
    private int _index;
    private CancellationTokenSource? _runCts;
    private ITutorialStateSnapshot _snapshot = EmptyTutorialStateSnapshot.Instance;

    public bool IsRunning => ActiveTutorial is not null;
    public TutorialDefinition? ActiveTutorial { get; private set; }
    public TutorialStep? ActiveStep => IsRunning && _index >= 0 && _index < _steps.Count ? _steps[_index] : null;

    public event EventHandler<TutorialEventArgs>? TutorialStarted;
    public event EventHandler<TutorialEventArgs>? StepShown;
    public event EventHandler<TutorialEventArgs>? StepCompleted;
    public event EventHandler<TutorialEventArgs>? StepSkipped;
    public event EventHandler<TutorialEventArgs>? TutorialCompleted;
    public event EventHandler<TutorialEventArgs>? TutorialSkipped;
    public event EventHandler<TutorialEventArgs>? TutorialCancelled;
    public event EventHandler<TutorialEventArgs>? TargetResolutionFailed;

    public async Task StartAsync(string tutorialId, string? startStepId = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EndRunAsync(restore: true, CancellationToken.None);
            ActiveTutorial = registry.Find(tutorialId) ?? throw new ArgumentException($"Unknown tutorial '{tutorialId}'.", nameof(tutorialId));
            _steps = ActiveTutorial.Steps.Where(x => x.CanShow?.Invoke(context) != false).ToList();
            if (_steps.Count == 0) { ActiveTutorial = null; return; }
            _index = startStepId is null ? 0 : Math.Max(0, _steps.FindIndex(x => x.Id == startStepId));
            _snapshot = navigation.CaptureState();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var progress = await progressStore.GetAsync(ActiveTutorial, cancellationToken);
            progress.Status = TutorialStatus.InProgress;
            progress.StartedAtUtc ??= DateTime.UtcNow;
            progress.TutorialVersion = ActiveTutorial.Version;
            await progressStore.SaveAsync(progress, cancellationToken);
            await SetLastTutorialAsync(ActiveTutorial.Id, cancellationToken);
            TutorialStarted?.Invoke(this, new(ActiveTutorial.Id));
            await ShowCurrentAsync(_runCts.Token);
        }
        finally { _gate.Release(); }
    }

    public async Task ContinueAsync(string tutorialId, CancellationToken cancellationToken = default)
    {
        var definition = registry.Find(tutorialId) ?? throw new ArgumentException($"Unknown tutorial '{tutorialId}'.", nameof(tutorialId));
        var progress = await progressStore.GetAsync(definition, cancellationToken);
        string? next = definition.Steps.FirstOrDefault(x => !progress.CompletedStepIds.Contains(x.Id) && x.CanShow?.Invoke(context) != false)?.Id;
        await StartAsync(tutorialId, next, cancellationToken);
    }

    public async Task NextAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (ActiveTutorial is null || ActiveStep is null) return;
            await CompleteCurrentStepAsync();
            if (_index >= _steps.Count - 1) await FinishCoreAsync();
            else { _index++; await ShowCurrentAsync(_runCts?.Token ?? CancellationToken.None); }
        }
        finally { _gate.Release(); }
    }

    public async Task BackAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (ActiveTutorial is null || _index == 0) return;
            _index--;
            await ShowCurrentAsync(_runCts?.Token ?? CancellationToken.None);
        }
        finally { _gate.Release(); }
    }

    public async Task SkipAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (ActiveTutorial is null) return;
            var tutorial = ActiveTutorial;
            var progress = await progressStore.GetAsync(tutorial);
            progress.Status = TutorialStatus.Skipped;
            progress.SkippedAtUtc = DateTime.UtcNow;
            await progressStore.SaveAsync(progress);
            TutorialSkipped?.Invoke(this, new(tutorial.Id, ActiveStep?.Id));
            await EndRunAsync(restore: true, CancellationToken.None);
        }
        finally { _gate.Release(); }
    }

    public async Task FinishAsync()
    {
        await _gate.WaitAsync();
        try { if (ActiveTutorial is not null) await FinishCoreAsync(); }
        finally { _gate.Release(); }
    }

    public async Task CancelAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (ActiveTutorial is null) return;
            TutorialCancelled?.Invoke(this, new(ActiveTutorial.Id, ActiveStep?.Id));
            await EndRunAsync(restore: true, CancellationToken.None);
        }
        finally { _gate.Release(); }
    }

    public async Task StartForCurrentPageAsync()
    {
        var match = registry.Tutorials.FirstOrDefault(x => x.Steps.Any(s => s.PageKey is not null && s.CanShow?.Invoke(context) != false));
        if (match is not null) await ContinueAsync(match.Id);
    }

    public Task ResetAsync(string tutorialId) => progressStore.ResetAsync(tutorialId);
    public Task ResetAllAsync() => progressStore.ResetAllAsync();

    public async Task RefreshAsync()
    {
        if (!IsRunning) return;
        await _gate.WaitAsync();
        try { if (IsRunning) await ShowCurrentAsync(_runCts?.Token ?? CancellationToken.None); }
        finally { _gate.Release(); }
    }

    private async Task ShowCurrentAsync(CancellationToken cancellationToken)
    {
        if (ActiveTutorial is null || ActiveStep is null) return;
        var step = ActiveStep;
        try
        {
            await navigation.PrepareAsync(step, cancellationToken);
            if (step.BeforeShowAsync is not null) await step.BeforeShowAsync(context, cancellationToken);
            TutorialTarget? target = await targetResolver.ResolveAsync(step, root, cancellationToken);
            bool unavailable = target is null;
            if (unavailable)
            {
                TargetResolutionFailed?.Invoke(this, new(ActiveTutorial.Id, step.Id, "target-unavailable"));
                if (step.IsOptional)
                {
                    StepSkipped?.Invoke(this, new(ActiveTutorial.Id, step.Id, "optional-target-unavailable"));
                    if (_index < _steps.Count - 1) { _index++; await ShowCurrentAsync(cancellationToken); }
                    else await FinishCoreAsync();
                    return;
                }
                target = new TutorialTarget(Rect.Empty);
            }

            presenter.Show(new(
                Text(ActiveTutorial.TitleKey), Text(step.TitleKey),
                unavailable ? Text("Tutorials.Common.TargetUnavailable") : Text(step.DescriptionKey),
                HasText(step.TipKey) ? Text(step.TipKey!) : null,
                _index + 1, _steps.Count, unavailable ? TutorialPlacement.Center : step.Placement,
                target!, _index > 0, _index == _steps.Count - 1, unavailable, step.AllowTargetInteraction));
            StepShown?.Invoke(this, new(ActiveTutorial.Id, step.Id));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TargetResolutionFailed?.Invoke(this, new(ActiveTutorial.Id, step.Id, ex.GetType().Name));
            presenter.Show(new(Text(ActiveTutorial.TitleKey), Text(step.TitleKey), Text("Tutorials.Common.TargetUnavailable"), null,
                _index + 1, _steps.Count, TutorialPlacement.Center, new TutorialTarget(Rect.Empty), _index > 0, _index == _steps.Count - 1, true, false));
        }
    }

    private async Task CompleteCurrentStepAsync()
    {
        if (ActiveTutorial is null || ActiveStep is null) return;
        var step = ActiveStep;
        if (step.AfterHideAsync is not null) await step.AfterHideAsync(context, CancellationToken.None);
        var progress = await progressStore.GetAsync(ActiveTutorial);
        progress.Status = TutorialStatus.InProgress;
        progress.LastCompletedStepId = step.Id;
        progress.CompletedStepIds.Add(step.Id);
        await progressStore.SaveAsync(progress);
        StepCompleted?.Invoke(this, new(ActiveTutorial.Id, step.Id));
    }

    private async Task FinishCoreAsync()
    {
        if (ActiveTutorial is null) return;
        var tutorial = ActiveTutorial;
        if (ActiveStep is not null)
        {
            var progressBefore = await progressStore.GetAsync(tutorial);
            if (!progressBefore.CompletedStepIds.Contains(ActiveStep.Id)) await CompleteCurrentStepAsync();
        }
        var progress = await progressStore.GetAsync(tutorial);
        progress.Status = TutorialStatus.Completed;
        progress.CompletedAtUtc = DateTime.UtcNow;
        progress.SkippedAtUtc = null;
        await progressStore.SaveAsync(progress);
        TutorialCompleted?.Invoke(this, new(tutorial.Id));
        await EndRunAsync(restore: true, CancellationToken.None);
    }

    private async Task EndRunAsync(bool restore, CancellationToken cancellationToken)
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = null;
        presenter.Hide();
        ActiveTutorial = null;
        _steps = [];
        _index = 0;
        if (restore) await _snapshot.RestoreAsync(cancellationToken);
        _snapshot = EmptyTutorialStateSnapshot.Instance;
    }

    private async Task SetLastTutorialAsync(string tutorialId, CancellationToken cancellationToken)
    {
        var preferences = await progressStore.GetPreferencesAsync(cancellationToken);
        preferences.LastTutorialId = tutorialId;
        await progressStore.SavePreferencesAsync(preferences, cancellationToken);
    }

    private static string Text(string key) => Properties.Resources.GetString(key);
    private static bool HasText(string? key) => key is not null && !string.Equals(Text(key), key, StringComparison.Ordinal);
}
