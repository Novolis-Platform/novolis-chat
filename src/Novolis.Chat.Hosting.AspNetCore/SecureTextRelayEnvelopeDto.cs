using System.Text.Json.Serialization;
using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;

namespace Novolis.Chat.Hosting.AspNetCore;

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
