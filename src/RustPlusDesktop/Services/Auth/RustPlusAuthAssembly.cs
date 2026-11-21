using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RustPlusDesk.Services
{
    /// <summary>
    /// Says whether RustPlus.Auth.dll can be loaded, and reads the Rust+ token without needing it to.
    ///
    /// Windows Smart App Control and WDAC can block that one DLL (0x800711C7) while the rest of the
    /// app runs. The failure happens when a method that names one of its types is compiled, before
    /// that method's own try/catch is entered, so a guard has to live in a caller that names none of
    /// them. Check <see cref="IsAvailable"/> there, or use the wrappers here, which keep every
    /// reference to the DLL in a separate, never-inlined method.
    /// </summary>
    public static class RustPlusAuthAssembly
    {
        /// <summary>HRESULT for ERROR_SYSTEM_INTEGRITY_POLICY_VIOLATION: Smart App Control or WDAC said no.</summary>
        public const int PolicyBlockedHResult = unchecked((int)0x800711C7);

        private static readonly Lazy<Exception?> s_loadError = new(Probe);

        /// <summary>True when RustPlus.Auth.dll loads. Probed once per run.</summary>
        public static bool IsAvailable => s_loadError.Value is null;

        /// <summary>Why the DLL did not load, or null when it did.</summary>
        public static Exception? LoadError => s_loadError.Value;

        /// <summary>True when Windows Application Control blocked the DLL, as opposed to it being missing.</summary>
        public static bool IsBlockedByPolicy => IsPolicyBlock(s_loadError.Value);

        public static bool IsPolicyBlock(Exception? ex)
        {
            for (var e = ex; e is not null; e = e.InnerException)
            {
                if (e.HResult == PolicyBlockedHResult) return true;
            }
            return false;
        }

        /// <summary>One line for the log and the pairing status, or null when the DLL loaded.</summary>
        public static string? UnavailableReason
        {
            get
            {
                var error = s_loadError.Value;
                if (error is null) return null;

                return IsPolicyBlock(error)
                    ? "Windows Smart App Control (Application Control) blocked RustPlus.Auth.dll. " +
                      "Rust+ login, pairing and the token check cannot run until Windows allows it."
                    : $"RustPlus.Auth.dll could not be loaded ({error.GetType().Name}: {error.Message}).";
            }
        }

        /// <summary>The longer text for the one-time warning dialog.</summary>
        public static string BlockedDialogText =>
            "Windows blocked one of RustPlus Desktop's files (RustPlus.Auth.dll) with Smart App Control.\n\n" +
            "Until Windows allows it, Rust+ login, server pairing and the companion socket connection " +
            "will not work.\n\n" +
            "This usually clears up by itself once Windows has checked the file online: make sure you " +
            "are connected to the internet and restart the app. If it keeps happening, reinstall the " +
            "latest version from the official download page.";

        /// <summary>Thrown by auth-dependent entry points when the DLL is unavailable.</summary>
        public static InvalidOperationException CreateUnavailableException() =>
            new(UnavailableReason ?? "RustPlus.Auth.dll is unavailable.", s_loadError.Value);

        /// <summary>The stored Rust+ companion token, or null when there is none or the DLL is unavailable.</summary>
        public static string? TryReadCurrentToken()
        {
            if (!IsAvailable) return null;
            try { return ReadCurrentTokenCore(); }
            catch (Exception ex) when (ex is FileLoadException or FileNotFoundException or BadImageFormatException) { return null; }
        }

        /// <summary>The token's expiry, or null when it has none, cannot be parsed, or the DLL is unavailable.</summary>
        public static DateTime? TryGetTokenExpiry(string token)
        {
            if (!IsAvailable) return null;
            try { return GetTokenExpiryCore(token); }
            catch (Exception ex) when (ex is FileLoadException or FileNotFoundException or BadImageFormatException) { return null; }
        }

        private static Exception? Probe()
        {
            try
            {
                Assembly.Load(new AssemblyName("RustPlus.Auth"));
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string? ReadCurrentTokenCore() => RustCompanionAuthService.ReadCurrentToken();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static DateTime? GetTokenExpiryCore(string token) =>
            RustCompanionAuthService.ParseJwtClaims(token).ExpiresAtUtc;
    }
}
