using System.Text.Json.Serialization;
using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;

namespace Novolis.Chat.Hosting.AspNetCore;

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
