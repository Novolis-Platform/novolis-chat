using System.Text.Json.Serialization;
using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;

namespace Novolis.Chat.Hosting.AspNetCore;

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
