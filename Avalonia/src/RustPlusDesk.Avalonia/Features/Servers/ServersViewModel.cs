using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RustPlusDesk.Avalonia.Services;
using RustPlusDesk.Core.Servers;
using RustPlusDesk.Core.Connections;
using RustPlusDesk.Avalonia.Features.Connections;
using RustPlusDesk.Core.Accounts;
using RustPlusDesk.Avalonia.Features.Accounts;

namespace RustPlusDesk.Avalonia.Features.Servers;

public sealed partial class ServersViewModel(IServerCatalog catalog, IProfileFilePicker picker, string initialPath, IServerSessionFactory sessionFactory,
    IServerProfileEditor? profileEditor = null, IServerConnectionTester? connectionTester = null, ICompanionAccountService? accountService = null)
    : ObservableObject
{
    public ConnectionViewModel Connection { get; } = new(sessionFactory);
    public CompanionAccountViewModel? Account { get; private set; }
    public bool HasAccount => accountService is not null;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    private bool isAccountOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    private ServerSummary? selectedServer;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    private ServerEditorViewModel? editor;
    public bool HasEditor => Editor is not null;
    public bool HasOverlay => HasEditor || IsAccountOpen;

    [RelayCommand]
    private void OpenAccount()
    {
        if (IsBusy || HasOverlay || accountService is null) return;
        if (Account is null)
        {
            Account = new(accountService, SourcePath, Connection.StopAsync);
            OnPropertyChanged(nameof(Account));
        }
        IsAccountOpen = true;
    }

    [RelayCommand]
    private async Task CloseAccountAsync()
    {
        if (Account is not null) await Account.StopAsync();
        IsAccountOpen = false;
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
    private bool isBusy;

    [ObservableProperty] private string sourcePath = initialPath;
    [ObservableProperty] private string statusTitle = "No saved servers yet";
    [ObservableProperty] private string statusMessage = "Open an existing profiles.json file to see your servers.";

    public bool HasServers => Servers.Count > 0;
    public bool ShowStatus => !IsBusy && !HasServers;
    private bool CanLoad => !IsBusy && !HasOverlay;
    private bool CanEdit => CanLoad && SelectedServer is not null && profileEditor is not null && connectionTester is not null;

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
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!CanLoad) return;
        IsBusy = true;
        try { await LoadAsync(SourcePath, cancellationToken); }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task OpenFileAsync(CancellationToken cancellationToken)
    {
        if (!CanLoad) return;
        IsBusy = true;
        try
        {
            var path = await picker.PickAsync();
            if (path is not null) await LoadAsync(path, cancellationToken);
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
        RefreshCommand.Cancel();
        OpenFileCommand.Cancel();
        EditCommand.Cancel();
        if (Account is not null) await Account.StopAsync();
        await Connection.StopAsync();
        if (RefreshCommand.ExecutionTask is { } refresh) await refresh;
        if (OpenFileCommand.ExecutionTask is { } open) await open;
        if (EditCommand.ExecutionTask is { } edit) await edit;
    }
}
