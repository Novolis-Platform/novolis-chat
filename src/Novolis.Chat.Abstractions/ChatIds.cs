namespace Novolis.Chat.Abstractions;

public readonly record struct SpaceId(Guid Value)
{
    public static SpaceId Default => new(Guid.Empty);

    public static SpaceId New() => new(Guid.NewGuid());

    public static SpaceId FromGuid(Guid value) => new(value);

    public override string ToString() => Value == Guid.Empty ? "default" : Value.ToString("N");
}

public readonly record struct ChannelId(SpaceId SpaceId, string Name)
{
    public string NormalizedName => NormalizeName(Name);

    public static ChannelId Create(SpaceId spaceId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ChannelId(spaceId, NormalizeName(name));
    }

    public static ChannelId Parse(string name) => Create(SpaceId.Default, name);

    public override string ToString() => NormalizedName;

    static string NormalizeName(string name)
    {
        var normalized = name.Trim();
        if (!normalized.StartsWith('#'))
            normalized = "#" + normalized;

        if (normalized.Length is < 2 or > 64)
            throw new ArgumentException("A channel name must contain 1 to 63 characters.", nameof(name));

        return normalized;
    }
}

public readonly record struct ConversationId(Guid Value)
{
    public static ConversationId New() => new(Guid.NewGuid());

    public static ConversationId FromGuid(Guid value) => new(value);

    public override string ToString() => Value.ToString("N");
}

public readonly record struct ThreadId(Guid Value)
{
    public static ThreadId New() => new(Guid.NewGuid());

    public static ThreadId FromGuid(Guid value) => new(value);
}

public readonly record struct MessageRef(Guid Value)
{
    public static MessageRef New() => new(Guid.NewGuid());

    public static MessageRef FromGuid(Guid value) => new(value);
}

public readonly record struct ChatNick(string Value)
{
    public static ChatNick Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ChatNick(value.Trim());
    }

    public override string ToString() => Value;
}
