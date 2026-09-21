using System.Text.Json.Serialization;
using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;

namespace Novolis.Chat.Hosting.AspNetCore;

public sealed record ChatRosterDto(string Channel, IReadOnlyList<string> Nicks);

public sealed record ChatSignalEnvelope(
    string Channel,
    string FromNick,
    string Kind,
    string Payload,
    string? ToNick = null,
    string? Conversation = null);

/// <summary>Public device material exchanged through a chat host.</summary>
public sealed record DeviceBundleDto(
    int ProtocolVersion,
    Guid DeviceId,
    byte[] SigningPublicKey,
    byte[] AgreementPublicKey,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    byte[] Signature);

/// <summary>One opaque SecureText envelope plus its public chat frame.</summary>
public sealed record SecureTextRelayEnvelopeDto(ChatFrame Frame, byte[] Envelope)
{
    [JsonIgnore]
    public string Channel => Frame.Conversation;

    [JsonIgnore]
    public string FromNick => Frame.FromNick;

    [JsonIgnore]
    public string ToNick => Frame.ToNick;
}

public sealed record SecureTextGroupEnvelopeInputDto(Guid RecipientDeviceId, byte[] Envelope);

/// <summary>One recipient copy of an opaque group envelope.</summary>
public sealed record SecureTextGroupRelayEnvelopeDto(
    ChatFrame Frame,
    Guid GroupId,
    string GroupName,
    byte[] Envelope)
{
    [JsonIgnore]
    public string Channel => Frame.Conversation;

    [JsonIgnore]
    public string FromNick => Frame.FromNick;

    [JsonIgnore]
    public string ToNick => Frame.ToNick;
}

/// <summary>
/// Product-owned append-only storage for opaque direct and group envelopes.
/// The chat host never interprets protected message bodies.
/// </summary>
public interface IChatHistoryStore
{
    Task AppendAsync(
        SecureTextRelayEnvelopeDto message,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SecureTextRelayEnvelopeDto>> GetRecentAsync(
        string channel,
        string nick,
        int take = 100,
        CancellationToken cancellationToken = default);

    Task AppendGroupAsync(
        SecureTextGroupRelayEnvelopeDto message,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SecureTextGroupRelayEnvelopeDto>> GetRecentGroupAsync(
        string channel,
        Guid groupId,
        string nick,
        int take = 100,
        CancellationToken cancellationToken = default);
}

public sealed record ChatChannelListDto(
    IReadOnlyList<ChatSpaceInfo> Spaces,
    IReadOnlyList<ChatChannelInfo> Channels);
