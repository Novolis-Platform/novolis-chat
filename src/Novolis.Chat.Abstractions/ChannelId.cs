namespace Novolis.Chat.Abstractions;

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
