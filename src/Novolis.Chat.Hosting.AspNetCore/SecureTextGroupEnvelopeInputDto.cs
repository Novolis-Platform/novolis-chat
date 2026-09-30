using System.Text.Json.Serialization;
using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;

namespace Novolis.Chat.Hosting.AspNetCore;

public sealed record SecureTextGroupEnvelopeInputDto(Guid RecipientDeviceId, byte[] Envelope);
