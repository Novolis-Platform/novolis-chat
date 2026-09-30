namespace Novolis.Chat.Abstractions;

public readonly record struct MessageRef(Guid Value)
{
    public static MessageRef New() => new(Guid.NewGuid());

    public static MessageRef FromGuid(Guid value) => new(value);
}
