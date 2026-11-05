using System;

namespace RustPlusDesk.Services.Cloud
{
    /// <summary>
    /// A cloud call that came back with a non-success status.
    ///
    /// Carries the status so callers can tell apart the cases that need
    /// different handling — a conflict the user must resolve is not the same as
    /// a dropped connection, and retrying the former forever is just noise.
    /// </summary>
    public sealed class CloudApiException : Exception
    {
        public CloudApiException(int statusCode, string routePath, string reason)
            : base($"Cloud API {routePath} returned {statusCode}: {reason}")
        {
            StatusCode = statusCode;
            RoutePath = routePath;
            Reason = reason;
        }

        public int StatusCode { get; }

        public string RoutePath { get; }

        /// <summary>Server-supplied explanation, safe to show to the user.</summary>
        public string Reason { get; }

        /// <summary>The account cannot store this data until the user resolves something.</summary>
        public bool IsConflict => StatusCode == 409;
    }
}
