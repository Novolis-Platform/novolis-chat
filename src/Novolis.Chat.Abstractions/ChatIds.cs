namespace Novolis.Chat.Abstractions;

public readonly record struct SpaceId(Guid Value)
{
    public static SpaceId Default => new(Guid.Empty);

    public static SpaceId New() => new(Guid.NewGuid());

    public static SpaceId FromGuid(Guid value) => new(value);

    public override string ToString() => Value == Guid.Empty ? "default" : Value.ToString("N");
}
