namespace Novolis.Chat.Abstractions;

public sealed record ChatTyping(
    string Conversation,
    string Nick,
    DateTimeOffset ExpiresAtUtc);
