namespace Novolis.Chat.Abstractions;

public sealed record MediaSessionPolicy(
    int MaxPeers = 4,
    bool AllowRecording = false)
{
    public MediaSessionPolicy Normalize()
    {
        if (MaxPeers is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(MaxPeers), "Mesh sessions support 1 to 16 peers.");

        return this;
    }
}
