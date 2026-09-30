namespace Novolis.Chat.Abstractions;

public readonly record struct ChatNick(string Value)
{
    public static ChatNick Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ChatNick(value.Trim());
    }

    public override string ToString() => Value;
}
