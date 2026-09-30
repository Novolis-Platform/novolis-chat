namespace Novolis.Chat.Abstractions;

public sealed record ChatFrameAnnotations(
    ThreadId? Thread = null,
    MessageRef? Parent = null,
    string? Reaction = null);
