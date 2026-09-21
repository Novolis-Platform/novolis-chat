namespace Novolis.Chat.Abstractions;

public abstract record ChatEvent(DateTimeOffset AtUtc);

public sealed record MessagePosted(ChatFrame Frame) : ChatEvent(Frame.SentAtUtc);

public sealed record MembershipUpdated(
    string Conversation,
    IReadOnlyList<string> Nicks,
    DateTimeOffset AtUtc) : ChatEvent(AtUtc);

public sealed record MediaSessionStarted(
    ConversationId Conversation,
    ConversationId Session,
    DateTimeOffset AtUtc) : ChatEvent(AtUtc);

public sealed record MediaSessionPolicy(
    int MaxPeers = 4,
    bool AllowRecording = false)
{
    public MediaSessionPolicy Normalize()
    {
        if (MaxPeers is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(MaxPeers), "Mesh sessions support 1 to 16 peers.");

        return this;
    }
}
