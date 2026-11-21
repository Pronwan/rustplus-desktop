using System;
using System.Collections.Generic;

namespace RustPlusDesk.Views;

// Session-only drafts and results, isolated by lane and destination (server or account).
internal sealed class MainChatComposerState
{
    internal readonly record struct Destination(MainWindow.ChatChannel Lane, string Scope);

    private readonly Dictionary<Destination, string> _drafts = new();
    private readonly Dictionary<Destination, string> _errors = new();
    private readonly HashSet<Destination> _sending = new();

    internal string Draft(Destination destination) => _drafts.GetValueOrDefault(destination) ?? "";
    internal string? Error(Destination destination) => _errors.GetValueOrDefault(destination);
    internal bool IsSending(Destination destination) => _sending.Contains(destination);
    internal bool CanCompose(Destination destination, bool restricted) => !restricted && !IsSending(destination);

    internal void SetDraft(Destination destination, string text)
    {
        if (text.Length == 0) _drafts.Remove(destination);
        else _drafts[destination] = text;
    }

    internal bool TryBegin(Destination destination)
    {
        if (!_sending.Add(destination)) return false;
        _errors.Remove(destination);
        return true;
    }

    internal void Complete(Destination destination, string draft, bool sent, string? error)
    {
        _sending.Remove(destination);
        if (sent && string.Equals(Draft(destination), draft, StringComparison.Ordinal)) _drafts.Remove(destination);
        if (error is not null) _errors[destination] = error;
    }
}
