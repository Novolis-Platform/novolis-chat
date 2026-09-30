namespace Novolis.Chat.Abstractions;

public readonly record struct ConversationId(Guid Value)
{
    public static ConversationId New() => new(Guid.NewGuid());

    public static ConversationId FromGuid(Guid value) => new(value);

    public override string ToString() => Value.ToString("N");
}
