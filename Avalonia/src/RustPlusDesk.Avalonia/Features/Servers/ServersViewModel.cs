using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Avalonia.Services;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Avalonia.Features.Connections;
using RustPlusDesk.Core.Accounts;
using RustPlusDesk.Avalonia.Features.Accounts;
using RustPlusDesk.Core.Devices;
using RustPlusDesk.Avalonia.Features.Devices;
using RustPlusDesk.Core.Recycling;
using RustPlusDesk.Avalonia.Features.Recycling;

namespace RustPlusDesk.Avalonia.Features.Servers;

public sealed partial class ServersViewModel(IServerCatalog catalog, IProfileFilePicker picker, string initialPath, IServerSessionFactory sessionFactory,
    IServerProfileEditor? profileEditor = null, IServerConnectionTester? connectionTester = null, ICompanionAccountService? accountService = null,
    ICompanionListener? companionListener = null, ICompanionPushHealth? pushHealth = null, IDeviceProfileEditor? deviceEditor = null,
    IRecyclerCatalog? recyclerCatalog = null)
    : ObservableObject
{
    public ConnectionViewModel Connection { get; } = new(sessionFactory);
    public CompanionAccountViewModel? Account { get; private set; }
    public CompanionListenerViewModel? Listener { get; private set; }
    public bool HasListener => Listener is not null;
    public bool RequiresSignIn => Account?.RequiresSignIn == true || Listener?.RequiresSignIn == true;
    private bool shuttingDown;
    private bool initialized;
    private bool pendingPairingRefresh;
    private readonly Dictionary<string, string> pairedProfileKeys = [];
    public bool HasAccount => accountService is not null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(OrganizeDevicesCommand))]
    private bool isAccountOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(OrganizeDevicesCommand))]
    private ServerSummary? selectedServer;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    private ServerEditorViewModel? editor;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOrganizer))]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    private DeviceOrganizerViewModel? organizer;
    public bool HasOrganizer => Organizer is not null;
    public bool HasDeviceEditor => deviceEditor is not null;
    public bool HasEditor => Editor is not null;
    public bool HasOverlay => HasEditor || HasOrganizer || IsAccountOpen;
    public bool HasRecyclerTool => recyclerCatalog is not null;
    [ObservableProperty]
    private RecyclerViewModel? recycler;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecyclerSelected))]
    [NotifyCanExecuteChangedFor(nameof(CloseRecyclerCommand))]
    private int workspaceIndex;
    public bool IsRecyclerSelected => WorkspaceIndex == 1;
    [ObservableProperty] private string toolMessage = "";

    partial void OnWorkspaceIndexChanged(int value)
    {
        if (value != 1 || Recycler is not null) return;
        try
        {
            if (recyclerCatalog is null) { WorkspaceIndex = 0; return; }
            Recycler = new(recyclerCatalog.Load());
            ToolMessage = "";
        }
        catch (Exception)
        {
            WorkspaceIndex = 0;
            ToolMessage = "Unable to load the bundled recycling catalog. Check or reinstall this preview.";
        }
    }
    [RelayCommand(CanExecute = nameof(IsRecyclerSelected))]
    private void CloseRecycler() => WorkspaceIndex = 0;

    [RelayCommand]
    private void OpenAccount()
    {
        if (IsBusy || HasOverlay || accountService is null) return;
        EnsureAccountServices();
        IsAccountOpen = true;
    }

    private void EnsureAccountServices()
    {
        if (accountService is null) return;
        if (Account is null)
        {
            Account = new(accountService, SourcePath, BeforeSignInAsync, StartListenerAsync);
            Account.PropertyChanged += (_, args) =>
            { if (args.PropertyName == nameof(CompanionAccountViewModel.RequiresSignIn)) OnPropertyChanged(nameof(RequiresSignIn)); };
            OnPropertyChanged(nameof(Account));
            OnPropertyChanged(nameof(RequiresSignIn));
        }
        if (Listener is null && companionListener is not null)
        {
            Listener = new(companionListener, SourcePath, PairingSavedAsync, pushHealth,
                (source, ct) => accountService.RenewPushAsync(CompanionAccountViewModel.ConfigPathFor(source), ct));
            Listener.PropertyChanged += (_, args) =>
            { if (args.PropertyName == nameof(CompanionListenerViewModel.RequiresSignIn)) OnPropertyChanged(nameof(RequiresSignIn)); };
            OnPropertyChanged(nameof(Listener));
            OnPropertyChanged(nameof(HasListener));
            OnPropertyChanged(nameof(RequiresSignIn));
        }
    }

    [RelayCommand]
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (initialized || shuttingDown) return;
        initialized = true;
        try
        {
            await RefreshCommand.ExecuteAsync(null);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAccountServices();
            if (Account is null || Listener is null) return;
            string initialSource = SourcePath;
            using var cancel = cancellationToken.Register(() => Account.CheckCommand.Cancel());
            await Account.CheckCommand.ExecuteAsync(null);
            cancellationToken.ThrowIfCancellationRequested();
            if (!shuttingDown && SourcePath == initialSource && Account.Account.State is not
                (AccountState.Missing or AccountState.Expired or AccountState.Rejected or AccountState.Invalid))
                await StartListenerAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private Task StartListenerAsync()
    {
        if (!shuttingDown && Listener is { IsRunning: false, IsChecking: false }) Listener.StartCommand.Execute(null);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task CloseAccountAsync()
    {
        if (Account is not null) await Account.StopAsync();
        IsAccountOpen = false;
        await RefreshPairingsAsync();
    }

    private async Task BeforeSignInAsync()
    {
        if (Listener is not null) await Listener.SetSourceAsync(SourcePath);
        await Connection.StopAsync();
    }

    private Task PairingSavedAsync(CompanionUpdate update)
    {
        pendingPairingRefresh = true;
        if (update.ReplacesProfileKey is { } previous && update.ProfileKey is { } updated && previous != updated)
            pairedProfileKeys[previous] = updated;
        return RefreshPairingsAsync();
    }

    private async Task RefreshPairingsAsync()
    {
        if (!pendingPairingRefresh || IsBusy || HasOverlay || shuttingDown) return;
        pendingPairingRefresh = false;
        bool reconnect = Connection.IsConnected;
        await RefreshCommand.ExecuteAsync(null);
        if (reconnect && !shuttingDown && SelectedServer is not null) Connection.ConnectCommand.Execute(null);
    }

    partial void OnSelectedServerChanged(ServerSummary? value) => Connection.Select(SourcePath, value);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServers))]
    [NotifyPropertyChangedFor(nameof(ShowStatus))]
    private IReadOnlyList<ServerSummary> servers = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatus))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(OrganizeDevicesCommand))]
    private bool isBusy;

    [ObservableProperty] private string sourcePath = initialPath;
    [ObservableProperty] private string statusTitle = "No saved servers yet";
    [ObservableProperty] private string statusMessage = "Open an existing profiles.json file to see your servers.";

    public bool HasServers => Servers.Count > 0;
    public bool ShowStatus => !IsBusy && !HasServers;
    private bool CanLoad => !IsBusy && !HasOverlay;
    private bool CanEdit => CanLoad && SelectedServer is not null && profileEditor is not null && connectionTester is not null;
    private bool CanOrganizeDevices => CanLoad && SelectedServer?.Devices.Count > 0 && deviceEditor is not null;

    [RelayCommand(CanExecute = nameof(CanOrganizeDevices))]
    private async Task OrganizeDevicesAsync(CancellationToken cancellationToken)
    {
        if (!CanOrganizeDevices || SelectedServer is not { } selected) return;
        IsBusy = true;
        try
        {
            Organizer = new(SourcePath, selected, deviceEditor!);
            var saved = await Organizer.Completion.WaitAsync(cancellationToken);
            if (saved is not null)
            {
                bool reconnect = Connection.IsConnected;
                await LoadAsync(SourcePath, CancellationToken.None);
                if (reconnect && !shuttingDown && SelectedServer is not null) Connection.ConnectCommand.Execute(null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (Organizer is { } current) await current.StopAsync();
            Organizer = null;
            IsBusy = false;
            await RefreshPairingsAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAsync(CancellationToken cancellationToken)
    {
        if (!CanEdit || SelectedServer is not { } selected) return;
        IsBusy = true;
        try
        {
            Editor = new(SourcePath, selected, profileEditor!, connectionTester!);
            var saved = await Editor.Completion.WaitAsync(cancellationToken);
            if (saved is not null)
            {
                bool reconnect = Connection.IsConnected || saved.Host != selected.Host || saved.Port != selected.Port;
                await LoadAsync(SourcePath, CancellationToken.None);
                SelectedServer = Servers.FirstOrDefault(server => server.ProfileKey == saved.ProfileKey) ?? SelectedServer;
                if (reconnect && SelectedServer is not null) Connection.ConnectCommand.Execute(null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (Editor is { } current) await current.StopAsync();
            Editor = null;
            IsBusy = false;
            await RefreshPairingsAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!CanLoad) return;
        IsBusy = true;
        string? desiredSelection = SelectedServer?.ProfileKey;
        try
        {
            do
            {
                pendingPairingRefresh = false;
                await LoadAsync(SourcePath, cancellationToken);
                for (int hop = 0; desiredSelection is not null && hop < pairedProfileKeys.Count; hop++)
                    if (pairedProfileKeys.TryGetValue(desiredSelection, out string? updated)) desiredSelection = updated;
                    else break;
                if (desiredSelection is not null)
                    SelectedServer = Servers.FirstOrDefault(server => server.ProfileKey == desiredSelection) ?? SelectedServer;
            } while (pendingPairingRefresh && !shuttingDown && !cancellationToken.IsCancellationRequested);
        }
        finally { IsBusy = false; pairedProfileKeys.Clear(); }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task OpenFileAsync(CancellationToken cancellationToken)
    {
        if (!CanLoad) return;
        IsBusy = true;
        try
        {
            var path = await picker.PickAsync();
            if (path is not null)
            {
                if (Listener is not null) await Listener.SetSourceAsync(path);
                pendingPairingRefresh = false;
                pairedProfileKeys.Clear();
                await LoadAsync(path, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await Connection.StopAsync();
            SelectedServer = null;
            ShowError("File picker unavailable", "Unable to open that file. Try a local JSON file with read permission.");
        }
        finally { IsBusy = false; }
    }

    private async Task LoadAsync(string path, CancellationToken cancellationToken)
    {
        await Connection.StopAsync();
        if (Account is not null) await Account.SetSourceAsync(path);
        string? previousKey = SelectedServer?.ProfileKey;
        SelectedServer = null;
        SourcePath = path;
        Servers = [];
        StatusTitle = "No saved servers yet";
        StatusMessage = "Open an existing profiles.json file to see your servers.";
        try
        {
            Servers = await catalog.LoadAsync(path, cancellationToken);
            SelectedServer = Servers.FirstOrDefault(server => server.ProfileKey == previousKey) ?? Servers.FirstOrDefault();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            ShowError("Profiles file not found", "Open the profiles.json file from your existing Rust+ Desktop installation.");
        }
        catch (JsonException)
        {
            ShowError("Unable to read profiles", "This file is not a valid Rust+ Desktop server list. Your file has not been changed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Unable to open profiles", "Check that the file is available and you have permission to read it.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ShowError("Loading canceled", "Refresh to try again.");
        }
    }

    private void ShowError(string title, string message)
    {
        Servers = [];
        StatusTitle = title;
        StatusMessage = message;
    }

    public async Task ShutdownAsync()
    {
        shuttingDown = true;
        InitializeCommand.Cancel();
        RefreshCommand.Cancel();
        OpenFileCommand.Cancel();
        EditCommand.Cancel();
        OrganizeDevicesCommand.Cancel();
        if (Account is not null) await Account.StopAsync();
        if (Listener is not null) await Listener.StopListeningAsync();
        await Connection.StopAsync();
        if (RefreshCommand.ExecutionTask is { } refresh) await refresh;
        if (OpenFileCommand.ExecutionTask is { } open) await open;
        if (EditCommand.ExecutionTask is { } edit) await edit;
        if (OrganizeDevicesCommand.ExecutionTask is { } organize) await organize;
        if (InitializeCommand.ExecutionTask is { } initialize) await initialize;
    }
}
