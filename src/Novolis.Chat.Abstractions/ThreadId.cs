namespace Novolis.Chat.Abstractions;

public readonly record struct ThreadId(Guid Value)
{
    public static ThreadId New() => new(Guid.NewGuid());

    public static ThreadId FromGuid(Guid value) => new(value);
}
