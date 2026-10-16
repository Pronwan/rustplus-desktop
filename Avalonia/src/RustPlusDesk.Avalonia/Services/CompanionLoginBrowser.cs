using Avalonia.Controls;
using Avalonia.Threading;
using RustPlusDesk.Core.Accounts;

namespace RustPlusDesk.Avalonia.Services;

/// <summary>Owns a native authentication dialog through completion, cancellation and application shutdown.</summary>
public sealed class CompanionLoginBrowser(Window owner) : ICompanionLoginBrowser
{
    public async Task<Uri?> AuthenticateAsync(Uri requestUri, Uri callbackUri, CancellationToken cancellationToken)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => AuthenticateAsync(requestUri, callbackUri, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException();
        NativeWebDialog? dialog = null;
        bool finished = false;
        using var cancellation = cancellationToken.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (!finished) dialog?.Close();
        }));
        try
        {
            var options = new WebAuthenticatorOptions(requestUri, callbackUri)
            {
#pragma warning disable CA1416 // The desktop platform guard above excludes every unsupported target.
                Mode = WebAuthenticatorMode.NativeWebDialog,
#pragma warning restore CA1416
                NonPersistent = true,
                NativeWebDialogFactory = () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return dialog = new NativeWebDialog { Title = "Rust+ Desktop — Sign in with Steam", CanUserResize = true };
                }
            };
            var result = await WebAuthenticationBroker.AuthenticateAsync(owner, options);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(result.Error)) throw new CompanionAccountException(AccountFailure.Rejected);
            return result.CallbackUri;
        }
        finally { finished = true; }
    }
}
