using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace RustPlusDesk.Features.Tutorials;

public enum TutorialPlacement { Auto, Top, Bottom, Left, Right, Center }
public enum TutorialStatus { NotStarted, InProgress, Completed, Skipped, Updated }

public sealed class TutorialDefinition
{
    public required string Id { get; init; }
    public required int Version { get; init; }
    public required string Category { get; init; }
    public required string TitleKey { get; init; }
    public required string DescriptionKey { get; init; }
    public string Icon { get; init; } = "Book24";
    public int DisplayOrder { get; init; }
    public bool IsRecommended { get; init; }
    public bool IsNewFeature { get; init; }
    public IReadOnlyList<TutorialStep> Steps { get; init; } = [];
}

public sealed class TutorialStep
{
    public required string Id { get; init; }
    public required string TitleKey { get; init; }
    public required string DescriptionKey { get; init; }
    public string? TipKey { get; init; }
    public string? PageKey { get; init; }
    public string? TargetId { get; init; }
    public string? WebViewTargetId { get; init; }
    public TutorialPlacement Placement { get; init; } = TutorialPlacement.Auto;
    public Thickness SpotlightPadding { get; init; } = new(8);
    public bool IsOptional { get; init; }
    public bool AllowTargetInteraction { get; init; }
    public bool AutoScrollIntoView { get; init; } = true;
    public Func<ITutorialContext, bool>? CanShow { get; init; }
    public Func<ITutorialContext, CancellationToken, Task>? BeforeShowAsync { get; init; }
    public Func<ITutorialContext, CancellationToken, Task>? AfterHideAsync { get; init; }
}

public sealed class TutorialProgress
{
    public required string TutorialId { get; set; }
    public int TutorialVersion { get; set; }
    public TutorialStatus Status { get; set; }
    public string? LastCompletedStepId { get; set; }
    public HashSet<string> CompletedStepIds { get; set; } = [];
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? SkippedAtUtc { get; set; }
}

public sealed class TutorialPreferences
{
    public bool FirstRunPromptDismissed { get; set; }
    public bool AutoStartBasicTutorial { get; set; } = true;
    public bool AutoStartNewFeatureTutorials { get; set; } = true;
    public HashSet<string> OfferedTutorialIds { get; set; } = [];
    public string? LastTutorialId { get; set; }
}

public sealed record TutorialTarget(Rect Bounds, FrameworkElement? Element = null);

public sealed class TutorialEventArgs(string tutorialId, string? stepId = null, string? reason = null) : EventArgs
{
    public string TutorialId { get; } = tutorialId;
    public string? StepId { get; } = stepId;
    public string? Reason { get; } = reason;
}

public interface ITutorialContext
{
    bool IsLoggedIn { get; }
    bool IsPremium { get; }
    bool HasPairedServer { get; }
    bool HasSelectedServer { get; }
    bool IsSoftConnected { get; }
    bool IsFullConnected { get; }
    bool HasMap { get; }
    bool HasDevices { get; }
    bool HasTeam { get; }
    bool IsDiscordConfigured { get; }
    bool HasAutomationRules { get; }
}

public interface ITutorialStateSnapshot
{
    Task RestoreAsync(CancellationToken cancellationToken = default);
}

public sealed class EmptyTutorialStateSnapshot : ITutorialStateSnapshot
{
    public static EmptyTutorialStateSnapshot Instance { get; } = new();
    public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
