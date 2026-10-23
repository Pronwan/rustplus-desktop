namespace RustPlusDesk.Core.Accounts;

public enum AccountState { Missing, Active, Expired, Rejected, Offline, Invalid, Unavailable }

/// <summary>Account information safe to bind, serialize or show; never contains an auth token.</summary>
public sealed record CompanionAccount(AccountState State, string SteamId = "", DateTimeOffset? ExpiresAt = null);

public interface ICompanionAccountService
{
    Task<CompanionAccount> CheckAsync(string configPath, CancellationToken cancellationToken);
    Task<CompanionAccount> SignInAsync(string configPath, CancellationToken cancellationToken);
    Task<CompanionAccount> RenewPushAsync(string configPath, CancellationToken cancellationToken);
    Task<CompanionSignOutResult> SignOutAsync(string configPath, CompanionSignOutScope scope, CancellationToken cancellationToken);
}

public enum CompanionSignOutScope { CurrentDevice, AllDevices }
public enum CompanionSignOutResult { Completed, LocalOnly, RemoteCompletedLocalCleanupFailed }

/// <summary>The native browser boundary; callback URLs are consumed by infrastructure, never view models.</summary>
public interface ICompanionLoginBrowser
{
    Task<Uri?> AuthenticateAsync(Uri requestUri, Uri callbackUri, CancellationToken cancellationToken);
}

public enum AccountFailure { Rejected, Unavailable, InvalidConfiguration, ChangedConfiguration, PushRegistration, MissingDeviceRegistration }

public sealed class CompanionAccountException(AccountFailure failure) : Exception(failure.ToString())
{
    public AccountFailure Failure { get; } = failure;
}
