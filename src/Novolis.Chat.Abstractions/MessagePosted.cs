namespace Novolis.Chat.Abstractions;

public sealed record MessagePosted(ChatFrame Frame) : ChatEvent(Frame.SentAtUtc);
