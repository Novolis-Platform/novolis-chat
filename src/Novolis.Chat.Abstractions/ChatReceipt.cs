namespace Novolis.Chat.Abstractions;

public sealed record ChatReceipt(
    string Conversation,
    MessageRef Message,
    string Nick,
    DateTimeOffset AtUtc);
