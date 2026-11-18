namespace RustPlusDesk.Models;

public enum ConnectionFailureKind
{
    Unavailable,
    MissingCompanionId,
    ServerError,
    Unsupported,
    RePairRequired,
    SignInRequired
}
