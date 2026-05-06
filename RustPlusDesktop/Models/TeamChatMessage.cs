namespace RustPlusDesk.Models;

public readonly record struct TeamChatMessage(
    System.DateTime Timestamp,
    string Author,
     ulong SteamId,
    string Text
);