namespace Novolis.Chat.Abstractions;

public sealed record MembershipUpdated(
    string Conversation,
    IReadOnlyList<string> Nicks,
    DateTimeOffset AtUtc) : ChatEvent(AtUtc);
