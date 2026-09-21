namespace Novolis.Chat.Abstractions;

/// <summary>
/// Public metadata that travels beside an opaque SecureText envelope.
/// </summary>
/// <param name="Conversation">The channel or conversation carrying the message.</param>
/// <param name="MessageId">The stable message identity from the protected envelope.</param>
/// <param name="FromNick">The sender display nick, not a platform identity.</param>
/// <param name="ToNick">The intended recipient or group recipient.</param>
/// <param name="SentAtUtc">The timestamp supplied by the protected envelope.</param>
/// <param name="Thread">Optional thread identity.</param>
/// <param name="Parent">Optional parent message reference.</param>
/// <param name="Reaction">Optional public reaction token.</param>
/// <param name="BodyFormat">The protected body format; chat currently requires Markdown.</param>
public sealed record ChatFrame(
    string Conversation,
    Guid MessageId,
    string FromNick,
    string ToNick,
    DateTimeOffset SentAtUtc,
    ThreadId? Thread = null,
    MessageRef? Parent = null,
    string? Reaction = null,
    ChatBodyFormat BodyFormat = ChatBodyFormat.Markdown)
{
    public static ChatFrame Create(
        string conversation,
        Guid messageId,
        string fromNick,
        string toNick,
        DateTimeOffset sentAtUtc,
        ThreadId? thread = null,
        MessageRef? parent = null,
        string? reaction = null,
        ChatBodyFormat bodyFormat = ChatBodyFormat.Markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversation);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromNick);
        ArgumentException.ThrowIfNullOrWhiteSpace(toNick);
        if (messageId == Guid.Empty)
            throw new ArgumentException("A chat frame requires a message id.", nameof(messageId));
        if (bodyFormat != ChatBodyFormat.Markdown)
            throw new ArgumentOutOfRangeException(
                nameof(bodyFormat),
                "Chat message bodies are Markdown.");

        return new ChatFrame(
            conversation.Trim(),
            messageId,
            ChatNick.Parse(fromNick).Value,
            ChatNick.Parse(toNick).Value,
            sentAtUtc,
            thread,
            parent,
            string.IsNullOrWhiteSpace(reaction) ? null : reaction.Trim(),
            bodyFormat);
    }
}

public sealed record ChatFrameAnnotations(
    ThreadId? Thread = null,
    MessageRef? Parent = null,
    string? Reaction = null);

public enum ChatPresenceStatus
{
    Offline = 0,
    Online = 1,
    Away = 2,
}

public sealed record ChatPresence(
    string Conversation,
    string Nick,
    ChatPresenceStatus Status,
    DateTimeOffset AtUtc);

public sealed record ChatTyping(
    string Conversation,
    string Nick,
    DateTimeOffset ExpiresAtUtc);

public sealed record ChatReceipt(
    string Conversation,
    MessageRef Message,
    string Nick,
    DateTimeOffset AtUtc);

public sealed record ChatReaction(
    string Conversation,
    MessageRef Message,
    string Nick,
    string Token,
    DateTimeOffset AtUtc);
