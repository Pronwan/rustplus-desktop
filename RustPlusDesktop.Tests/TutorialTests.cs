using Microsoft.VisualStudio.TestTools.UnitTesting;
using RustPlusDesk.Features.Tutorials;
using System.Windows;
using System.Windows.Controls;

namespace RustPlusDesktop.Tests;

[TestClass]
public sealed class TutorialTests
{
    private string _path = null!;

    [TestInitialize]
    public void Initialize() => _path = Path.Combine(Path.GetTempPath(), $"rustplus-tutorial-{Guid.NewGuid():N}.json");

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp");
    }

    [TestMethod]
    public void DefinitionsHaveUniqueTutorialAndStepIds()
    {
        var errors = new TutorialRegistry().Validate();
        Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    public void EveryDefinitionUsesARealLocalizationKey()
    {
        var errors = new TutorialRegistry().Validate(key =>
            !string.Equals(RustPlusDesk.Properties.Resources.GetString(key), key, StringComparison.Ordinal));
        Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    public void DefinitionsOnlyUseCurrentVisibleWorkflows()
    {
        var registry = new TutorialRegistry();

        Assert.IsNull(registry.Find("players"));
        var heatmapSteps = registry.Find("heatmaps")!.Steps;
        Assert.AreEqual("Map.ServerHud", heatmapSteps.Single(x => x.Id == "heatmaps.open").TargetId);
        Assert.IsNotNull(heatmapSteps.Single(x => x.Id == "heatmaps.selector").CanShow);
        Assert.IsTrue(registry.Find("pairing-servers")!.Steps
            .Where(x => x.Id is "pairing.soft" or "pairing.full" or "pairing.disconnect")
            .All(x => x.TargetId == "Servers.ConnectionActions"));
        Assert.IsTrue(registry.Find("bases-screenshots")!.Steps.All(x => x.TargetId == "Map.Canvas"));
        Assert.IsTrue(registry.Find("updates-diagnostics")!.Steps.All(x => x.Placement == TutorialPlacement.Center));
        CollectionAssert.AreEqual(
            new[] { "raid-calculator", "device-automation" },
            registry.Tutorials.Where(x => x.IsNewFeature).Select(x => x.Id).ToArray());
        Assert.AreEqual("Shops.Panel", registry.Find("shops-vending")!.Steps.Single(x => x.Id == "shops.panel").TargetId);
    }

    [TestMethod]
    public async Task ProgressRoundTripsAndResetOnePreservesOthers()
    {
        var registry = new TutorialRegistry();
        var store = new TutorialProgressStore(_path);
        var first = registry.Tutorials[0];
        var second = registry.Tutorials[1];
        var progress = await store.GetAsync(first);
        progress.Status = TutorialStatus.InProgress;
        progress.CompletedStepIds.Add(first.Steps[0].Id);
        progress.LastCompletedStepId = first.Steps[0].Id;
        await store.SaveAsync(progress);
        await store.SaveAsync(new TutorialProgress { TutorialId = second.Id, TutorialVersion = second.Version, Status = TutorialStatus.Completed });

        var loaded = await store.GetAsync(first);
        Assert.AreEqual(TutorialStatus.InProgress, loaded.Status);
        Assert.IsTrue(loaded.CompletedStepIds.Contains(first.Steps[0].Id));

        await store.ResetAsync(first.Id);
        Assert.AreEqual(TutorialStatus.NotStarted, (await store.GetAsync(first)).Status);
        Assert.AreEqual(TutorialStatus.Completed, (await store.GetAsync(second)).Status);
    }

    [TestMethod]
    public async Task NewDefinitionVersionMarksCompletedTutorialUpdated()
    {
        var store = new TutorialProgressStore(_path);
        var old = Definition(version: 1);
        await store.SaveAsync(new TutorialProgress { TutorialId = old.Id, TutorialVersion = 1, Status = TutorialStatus.Completed });

        var updated = await store.GetAsync(Definition(version: 2));

        Assert.AreEqual(TutorialStatus.Updated, updated.Status);
        Assert.AreEqual(2, updated.TutorialVersion);
    }

    [TestMethod]
    public async Task SkippedProgressIsNotCompletedAndResetAllKeepsPreferences()
    {
        var store = new TutorialProgressStore(_path);
        var definition = Definition(1);
        await store.SaveAsync(new TutorialProgress { TutorialId = definition.Id, TutorialVersion = 1, Status = TutorialStatus.Skipped, SkippedAtUtc = DateTime.UtcNow });
        await store.SavePreferencesAsync(new TutorialPreferences
        {
            FirstRunPromptDismissed = true,
            AutoStartBasicTutorial = false,
            OfferedTutorialIds = ["raid-calculator"]
        });

        Assert.AreEqual(TutorialStatus.Skipped, (await store.GetAsync(definition)).Status);
        await store.ResetAllAsync();
        Assert.AreEqual(TutorialStatus.NotStarted, (await store.GetAsync(definition)).Status);
        var preferences = await store.GetPreferencesAsync();
        Assert.IsTrue(preferences.FirstRunPromptDismissed);
        Assert.IsTrue(preferences.OfferedTutorialIds.Contains("raid-calculator"));
    }

    [TestMethod]
    public async Task CorruptProgressFallsBackWithoutThrowing()
    {
        await File.WriteAllTextAsync(_path, "not-json");
        var progress = await new TutorialProgressStore(_path).GetAsync(Definition(1));
        Assert.AreEqual(TutorialStatus.NotStarted, progress.Status);
    }

    [TestMethod]
    public void ConditionsCanFilterUnavailableSteps()
    {
        var context = new EmptyContext();
        var step = new TutorialStep { Id = "conditional", TitleKey = "t", DescriptionKey = "d", Placement = TutorialPlacement.Center, CanShow = c => c.HasDevices };
        Assert.IsFalse(step.CanShow(context));
    }

    [STATestMethod]
    public void ContinueStartsAtFirstIncompleteStepAndSkipStaysDistinct()
    {
        var definition = Definition(1);
        definition = new TutorialDefinition
        {
            Id = definition.Id, Version = 1, Category = "Test", TitleKey = "Tutorials.application-basics.Title",
            DescriptionKey = "Tutorials.application-basics.Description",
            Steps =
            [
                CenterStep("first", "Tutorials.Step.basics.navigation.Title", "Tutorials.Step.basics.navigation.Description"),
                CenterStep("second", "Tutorials.Step.basics.collapse.Title", "Tutorials.Step.basics.collapse.Description")
            ]
        };
        var store = new TutorialProgressStore(_path);
        store.SaveAsync(new TutorialProgress { TutorialId = definition.Id, TutorialVersion = 1, Status = TutorialStatus.InProgress, CompletedStepIds = ["first"] }).GetAwaiter().GetResult();
        var service = Service(definition, store, new FakeResolver(new TutorialTarget(Rect.Empty)), out _);

        service.ContinueAsync(definition.Id).GetAwaiter().GetResult();
        Assert.AreEqual("second", service.ActiveStep?.Id);
        service.SkipAsync().GetAwaiter().GetResult();
        Assert.AreEqual(TutorialStatus.Skipped, store.GetAsync(definition).GetAwaiter().GetResult().Status);
    }

    [STATestMethod]
    public async Task OptionalMissingTargetSkipsButRequiredMissingTargetIsPresented()
    {
        var definition = new TutorialDefinition
        {
            Id = "missing", Version = 1, Category = "Test", TitleKey = "Tutorials.application-basics.Title",
            DescriptionKey = "Tutorials.application-basics.Description",
            Steps =
            [
                new TutorialStep { Id = "optional", TitleKey = "Tutorials.Step.basics.navigation.Title", DescriptionKey = "Tutorials.Step.basics.navigation.Description", TargetId = "missing", IsOptional = true },
                new TutorialStep { Id = "required", TitleKey = "Tutorials.Step.basics.collapse.Title", DescriptionKey = "Tutorials.Step.basics.collapse.Description", TargetId = "missing-too" }
            ]
        };
        var store = new TutorialProgressStore(_path);
        var service = Service(definition, store, new FakeResolver(null), out var presenter);
        int optionalSkipped = 0;
        int failures = 0;
        service.StepSkipped += (_, _) => optionalSkipped++;
        service.TargetResolutionFailed += (_, _) => failures++;

        await service.StartAsync(definition.Id);

        Assert.AreEqual("required", service.ActiveStep?.Id);
        Assert.AreEqual(1, optionalSkipped);
        Assert.AreEqual(2, failures);
        Assert.IsTrue(presenter.Last?.IsTargetUnavailable);
    }

    private static TutorialDefinition Definition(int version) => new()
    {
        Id = "test",
        Version = version,
        Category = "Test",
        TitleKey = "title",
        DescriptionKey = "description",
        Steps = [new TutorialStep { Id = "step", TitleKey = "step-title", DescriptionKey = "step-description", Placement = TutorialPlacement.Center }]
    };

    private static TutorialStep CenterStep(string id, string title, string description) => new()
    { Id = id, TitleKey = title, DescriptionKey = description, Placement = TutorialPlacement.Center };

    private static TutorialService Service(TutorialDefinition definition, ITutorialProgressStore store,
        ITutorialTargetResolver resolver, out FakePresenter presenter)
    {
        presenter = new FakePresenter();
        return new TutorialService(new FakeRegistry(definition), store, resolver, new FakeNavigation(), presenter, new EmptyContext(), new Grid());
    }

    private sealed class FakeRegistry(TutorialDefinition definition) : ITutorialRegistry
    {
        public IReadOnlyList<TutorialDefinition> Tutorials => [definition];
        public TutorialDefinition? Find(string tutorialId) => tutorialId == definition.Id ? definition : null;
        public IReadOnlyList<string> Validate(Func<string, bool>? localizationKeyExists = null) => [];
    }

    private sealed class FakeResolver(TutorialTarget? target) : ITutorialTargetResolver
    {
        public Task<TutorialTarget?> ResolveAsync(TutorialStep step, FrameworkElement root, CancellationToken cancellationToken = default) => Task.FromResult(target);
    }

    private sealed class FakeNavigation : ITutorialNavigationCoordinator
    {
        public Task PrepareAsync(TutorialStep step, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ITutorialStateSnapshot CaptureState() => EmptyTutorialStateSnapshot.Instance;
    }

    private sealed class FakePresenter : ITutorialPresenter
    {
        public bool IsVisible { get; private set; }
        public TutorialPresentation? Last { get; private set; }
        public void Show(TutorialPresentation presentation) { Last = presentation; IsVisible = true; }
        public void Hide() => IsVisible = false;
    }

    private sealed class EmptyContext : ITutorialContext
    {
        public bool IsLoggedIn => false;
        public bool IsPremium => false;
        public bool HasPairedServer => false;
        public bool HasSelectedServer => false;
        public bool IsSoftConnected => false;
        public bool IsFullConnected => false;
        public bool HasMap => false;
        public bool HasDevices => false;
        public bool HasTeam => false;
        public bool IsDiscordConfigured => false;
        public bool HasAutomationRules => false;
    }
}
