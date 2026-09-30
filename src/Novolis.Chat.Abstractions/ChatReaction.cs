namespace Novolis.Chat.Abstractions;

public sealed record ChatReaction(
    string Conversation,
    MessageRef Message,
    string Nick,
    string Token,
    DateTimeOffset AtUtc);
