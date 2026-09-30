namespace Novolis.Chat.Abstractions;

public sealed record MediaSessionStarted(
    ConversationId Conversation,
    ConversationId Session,
    DateTimeOffset AtUtc) : ChatEvent(AtUtc);
