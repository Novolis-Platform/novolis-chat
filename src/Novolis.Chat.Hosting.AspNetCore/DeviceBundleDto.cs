using System.Text.Json.Serialization;
using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;

namespace Novolis.Chat.Hosting.AspNetCore;

/// <summary>Public device material exchanged through a chat host.</summary>
public sealed record DeviceBundleDto(
    int ProtocolVersion,
    Guid DeviceId,
    byte[] SigningPublicKey,
    byte[] AgreementPublicKey,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    byte[] Signature);
