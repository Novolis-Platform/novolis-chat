namespace Novolis.Chat.Abstractions;

public sealed record ChatPresence(
    string Conversation,
    string Nick,
    ChatPresenceStatus Status,
    DateTimeOffset AtUtc);
