using System.Text.Json.Serialization;
using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;

namespace Novolis.Chat.Hosting.AspNetCore;

public sealed record ChatSignalEnvelope(
    string Channel,
    string FromNick,
    string Kind,
    string Payload,
    string? ToNick = null,
    string? Conversation = null);
