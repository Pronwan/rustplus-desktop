using System;

namespace RustPlusDesk.Services;

/// <summary>
/// Thrown when the Rust+ WebSocket server or Facepunch proxy rejects authentication
/// (HTTP status 418 "I'm a teapot", invalid/expired player token, access denied).
/// Indicates that the player token needs to be refreshed by re-pairing the server from in-game.
/// </summary>
public class RustPlusAccessDeniedException : Exception
{
    public RustPlusAccessDeniedException(string message) : base(message)
    {
    }

    public RustPlusAccessDeniedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
