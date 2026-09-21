using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;
using Novolis.Chat.Live;
using Novolis.Game.Identity.AspNetCore;
using Novolis.Messaging.SecureText;
using Novolis.Security.SecureText;

namespace Novolis.Chat.Hosting.AspNetCore;

/// <summary>
/// SignalR orchestration for chat membership, opaque SecureText relay, live
/// events, and conversation-scoped RTC signaling.
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    readonly ChatDirectory _directory;
    readonly ChatLiveState _live;
    readonly IChatHistoryStore _store;
    readonly ILogger<ChatHub> _logger;

    public ChatHub(
        ChatDirectory directory,
        ChatLiveState live,
        IChatHistoryStore store,
        ILogger<ChatHub> logger)
    {
        _directory = directory;
        _live = live;
        _store = store;
        _logger = logger;
    }

    public async Task<ChatChannelListDto> GetChannels()
    {
        return await Task.FromResult(
            new ChatChannelListDto(
                _directory.GetSpaces(),
                _directory.GetChannels(SpaceId.Default))).ConfigureAwait(false);
    }

    public Task<ChatChannelInfo> CreateChannel(string name)
    {
        if (!_directory.TryCreateChannel(SpaceId.Default, name, out var channel))
            throw new HubException("The channel name is invalid or already exists.");

        return Task.FromResult(channel!);
    }

    public async Task Join(string channel)
    {
        channel = NormalizeChannel(channel);
        EnsureKnownChannel(channel);
        if (!Context.User!.TryGetPlayerRef(out var player))
            throw new HubException("Missing player claim.");

        var nick = ResolveNick();
        var prior = _directory.FindChannelForConnection(Context.ConnectionId);
        if (prior is not null && !string.Equals(prior, channel, StringComparison.OrdinalIgnoreCase))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, prior).ConfigureAwait(false);
            var leftRoster = _directory.Part(prior, Context.ConnectionId);
            if (leftRoster is not null)
            {
                await Clients.Group(prior)
                    .SendAsync("Roster", new ChatRosterDto(prior, leftRoster))
                    .ConfigureAwait(false);
                await Clients.Group(prior)
                    .SendAsync("Presence", _live.RemovePresence(prior, nick))
                    .ConfigureAwait(false);
            }
        }

        var roster = _directory.Join(channel, player, nick, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, channel).ConfigureAwait(false);

        await Clients.Group(channel)
            .SendAsync("Roster", new ChatRosterDto(channel, roster))
            .ConfigureAwait(false);
        await Clients.Group(channel)
            .SendAsync("Presence", _live.SetPresence(channel, nick))
            .ConfigureAwait(false);
        _logger.LogInformation("{Nick} joined {Channel}", nick, channel);
    }

    public async Task Part(string channel)
    {
        channel = NormalizeChannel(channel);
        var nick = ResolveNick();
        var wasVideo = _directory.TryPartVideo(channel, Context.ConnectionId);
        var roster = _directory.Part(channel, Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, channel).ConfigureAwait(false);

        if (wasVideo)
        {
            await Clients.Group(channel)
                .SendAsync(
                    "Signal",
                    new ChatSignalEnvelope(channel, nick, "video-part", string.Empty))
                .ConfigureAwait(false);
        }

        if (roster is not null)
        {
            await Clients.Group(channel)
                .SendAsync("Roster", new ChatRosterDto(channel, roster))
                .ConfigureAwait(false);
            await Clients.Group(channel)
                .SendAsync("Presence", _live.RemovePresence(channel, nick))
                .ConfigureAwait(false);
        }
    }

    public Task RegisterDevice(string channel, DeviceBundleDto bundle)
    {
        channel = NormalizeChannel(channel);
        EnsureKnownChannel(channel);
        if (!Context.User!.TryGetPlayerRef(out _))
            throw new HubException("Missing player claim.");

        EnsureJoined(channel);
        var publicBundle = ToPublicBundle(bundle);
        if (!publicBundle.Verify())
            throw new HubException("The device bundle signature or validity window is invalid.");
        if (!_directory.TryRegisterDevice(
                channel,
                ResolveNick(),
                Context.ConnectionId,
                publicBundle))
        {
            throw new HubException("The device cannot be registered for this connection.");
        }

        _logger.LogInformation(
            "Registered secure-text device {DeviceId} for {Nick}",
            publicBundle.DeviceId,
            ResolveNick());
        return Task.CompletedTask;
    }

    public Task<DeviceBundleDto?> GetDeviceBundle(string channel, string nick)
    {
        channel = NormalizeChannel(channel);
        EnsureKnownChannel(channel);
        EnsureJoined(channel);

        var bundle = _directory.TryGetDeviceBundle(channel, nick);
        return Task.FromResult(bundle is null ? null : ToDto(bundle));
    }

    public Task<IReadOnlyList<SecureTextRelayEnvelopeDto>> GetSecureHistory(string channel)
    {
        channel = NormalizeChannel(channel);
        EnsureJoined(channel);
        return _store.GetRecentAsync(channel, ResolveNick());
    }

    public async Task CreateSecureTextGroup(
        string channel,
        Guid groupId,
        string name,
        IReadOnlyList<SecureTextGroupMemberDto> members)
    {
        channel = NormalizeChannel(channel);
        EnsureJoined(channel);
        if (members is null)
            throw new HubException("The protected group membership is required.");

        var nick = ResolveNick();
        var initiator = members.SingleOrDefault(member =>
            string.Equals(member.Nick, nick, StringComparison.OrdinalIgnoreCase));
        if (initiator is null
            || !_directory.TryCreateSecureTextGroup(
                channel,
                groupId,
                name,
                nick,
                initiator.DeviceId,
                members,
                out var group))
        {
            throw new HubException("The protected group membership is invalid.");
        }

        await BroadcastSecureTextGroupAsync(channel, group!).ConfigureAwait(false);
    }

    public async Task ApproveSecureTextGroup(string channel, Guid groupId)
    {
        channel = NormalizeChannel(channel);
        EnsureJoined(channel);

        var nick = ResolveNick();
        var bundle = _directory.TryGetDeviceBundle(channel, nick);
        if (bundle is null
            || !_directory.TryApproveSecureTextGroup(
                channel,
                groupId,
                nick,
                bundle.DeviceId,
                out var group))
        {
            throw new HubException("The protected group approval is invalid.");
        }

        await BroadcastSecureTextGroupAsync(channel, group!).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<SecureTextGroupDto>> GetSecureTextGroups(string channel)
    {
        channel = NormalizeChannel(channel);
        EnsureJoined(channel);

        var bundle = _directory.TryGetDeviceBundle(channel, ResolveNick());
        if (bundle is null)
            throw new HubException("Register a secure-text device before reading protected groups.");

        return Task.FromResult(_directory.GetSecureTextGroupsForDevice(channel, bundle.DeviceId));
    }

    public Task<IReadOnlyList<SecureTextGroupRelayEnvelopeDto>> GetSecureTextGroupHistory(
        string channel,
        Guid groupId)
    {
        channel = NormalizeChannel(channel);
        EnsureJoined(channel);

        var nick = ResolveNick();
        var bundle = _directory.TryGetDeviceBundle(channel, nick);
        if (bundle is null || !_directory.IsSecureTextGroupMember(channel, groupId, nick, bundle.DeviceId))
            throw new HubException("The device is not a protected group member.");

        return _store.GetRecentGroupAsync(channel, groupId, nick);
    }

    public Task SendSecureText(
        string channel,
        string toNick,
        byte[] envelope) =>
        SendSecureTextGuarded(channel, toNick, envelope, null);

    public Task SendSecureTextWithAnnotations(
        string channel,
        string toNick,
        byte[] envelope,
        ChatFrameAnnotations annotations) =>
        SendSecureTextGuarded(channel, toNick, envelope, annotations);

    async Task SendSecureTextGuarded(
        string channel,
        string toNick,
        byte[] envelope,
        ChatFrameAnnotations? annotations)
    {
        try
        {
            await SendSecureTextCore(channel, toNick, envelope, annotations)
                .ConfigureAwait(false);
        }
        catch (HubException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unhandled protected direct-message failure.");
            throw new HubException("The protected message could not be relayed.");
        }
    }

    async Task SendSecureTextCore(
        string channel,
        string toNick,
        byte[] envelope,
        ChatFrameAnnotations? annotations)
    {
        channel = NormalizeChannel(channel);
        EnsureKnownChannel(channel);
        EnsureJoined(channel);
        if (string.IsNullOrWhiteSpace(toNick))
            throw new HubException("A recipient is required.");
        if (envelope is null
            || envelope.Length > SecureTextEnvelopeCodec.GetMaximumSerializedBytes())
        {
            throw new HubException("The protected envelope length is invalid.");
        }

        var protectedEnvelope = DeserializeEnvelope(envelope, "The protected envelope is invalid.");
        var fromNick = ResolveNick();
        if (!_directory.IsRegisteredDevice(
                channel,
                fromNick,
                protectedEnvelope.Header.SenderDeviceId.Value))
        {
            throw new HubException("The protected envelope sender device is not registered for this connection.");
        }

        var targetBundle = _directory.TryGetDeviceBundle(channel, toNick);
        if (targetBundle is null
            || targetBundle.DeviceId != protectedEnvelope.Header.RecipientDeviceId.Value)
        {
            throw new HubException("The protected envelope recipient does not match the selected peer.");
        }

        var targetConnection = _directory.FindConnectionForDevice(channel, targetBundle.DeviceId);
        if (targetConnection is null)
            throw new HubException("The selected peer is not connected.");

        var frame = ChatFrame.Create(
            channel,
            protectedEnvelope.Header.MessageId,
            fromNick,
            toNick.Trim(),
            protectedEnvelope.Header.SentAtUtc,
            annotations?.Thread,
            annotations?.Parent,
            annotations?.Reaction);
        var relay = new SecureTextRelayEnvelopeDto(frame, envelope);
        try
        {
            await _store.AppendAsync(relay).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not append protected direct message {MessageId} to chat history.",
                frame.MessageId);
            throw new HubException("The protected message could not be stored.");
        }
        await Clients.Client(targetConnection).SendAsync("SecureText", relay).ConfigureAwait(false);
    }

    public Task SendSecureTextGroup(
        string channel,
        Guid groupId,
        IReadOnlyList<SecureTextGroupEnvelopeInputDto> envelopes) =>
        SendSecureTextGroupCore(channel, groupId, envelopes, null);

    public Task SendSecureTextGroupWithAnnotations(
        string channel,
        Guid groupId,
        IReadOnlyList<SecureTextGroupEnvelopeInputDto> envelopes,
        ChatFrameAnnotations annotations) =>
        SendSecureTextGroupCore(channel, groupId, envelopes, annotations);

    async Task SendSecureTextGroupCore(
        string channel,
        Guid groupId,
        IReadOnlyList<SecureTextGroupEnvelopeInputDto> envelopes,
        ChatFrameAnnotations? annotations)
    {
        channel = NormalizeChannel(channel);
        EnsureJoined(channel);

        var group = _directory.TryGetSecureTextGroup(channel, groupId);
        if (group is null || group.ApprovedDeviceIds.Count != group.Members.Count)
            throw new HubException("Every group device must explicitly approve this membership before messaging.");

        var fromNick = ResolveNick();
        var senderBundle = _directory.TryGetDeviceBundle(channel, fromNick);
        if (senderBundle is null
            || !_directory.IsSecureTextGroupMember(
                channel,
                groupId,
                fromNick,
                senderBundle.DeviceId))
        {
            throw new HubException("The sender device is not an active protected group member.");
        }

        var expectedRecipients = group.Members
            .Where(member => member.DeviceId != senderBundle.DeviceId)
            .OrderBy(member => member.DeviceId)
            .ToArray();
        if (envelopes is null
            || envelopes.Count != expectedRecipients.Length
            || envelopes.Select(envelope => envelope.RecipientDeviceId).Distinct().Count() != envelopes.Count)
        {
            throw new HubException("A separate protected envelope is required for every group recipient.");
        }

        var sent = new List<(SecureTextGroupRelayEnvelopeDto Relay, string ConnectionId)>();
        foreach (var expected in expectedRecipients)
        {
            var input = envelopes.SingleOrDefault(
                envelope => envelope.RecipientDeviceId == expected.DeviceId);
            if (input is null
                || input.Envelope is null
                || input.Envelope.Length > SecureTextEnvelopeCodec.GetMaximumSerializedBytes())
            {
                throw new HubException("A protected group envelope is invalid.");
            }

            var protectedEnvelope = DeserializeEnvelope(
                input.Envelope,
                "A protected group envelope is invalid.");
            var expectedConversation = SecureTextConversationId.DeriveForGroupMember(
                SecureTextGroupId.FromGuid(groupId),
                SecureTextDeviceId.FromGuid(senderBundle.DeviceId),
                SecureTextDeviceId.FromGuid(expected.DeviceId));
            if (protectedEnvelope.Header.SenderDeviceId.Value != senderBundle.DeviceId
                || protectedEnvelope.Header.RecipientDeviceId.Value != expected.DeviceId
                || protectedEnvelope.Header.ConversationId != expectedConversation)
            {
                throw new HubException("A protected group envelope does not match the approved membership.");
            }

            var targetConnection = _directory.FindConnectionForDevice(channel, expected.DeviceId);
            if (targetConnection is null)
                throw new HubException("Every protected group member must be connected.");

            var frame = ChatFrame.Create(
                channel,
                protectedEnvelope.Header.MessageId,
                fromNick,
                expected.Nick,
                protectedEnvelope.Header.SentAtUtc,
                annotations?.Thread,
                annotations?.Parent,
                annotations?.Reaction);
            sent.Add((
                new SecureTextGroupRelayEnvelopeDto(
                    frame,
                    groupId,
                    group.Name,
                    input.Envelope),
                targetConnection));
        }

        try
        {
            foreach (var entry in sent)
                await _store.AppendGroupAsync(entry.Relay).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Could not append protected group {GroupId} messages to chat history.",
                groupId);
            throw new HubException("The protected group message could not be stored.");
        }
        foreach (var entry in sent)
        {
            await Clients.Client(entry.ConnectionId)
                .SendAsync("SecureTextGroup", entry.Relay)
                .ConfigureAwait(false);
        }
    }

    public async Task Typing(
        string channel,
        bool isTyping,
        string? conversation = null)
    {
        channel = NormalizeChannel(channel);
        EnsureJoined(channel);
        var key = NormalizeConversation(conversation, channel);
        var snapshot = _live.SetTyping(key, ResolveNick(), isTyping);
        await Clients.Group(channel).SendAsync("Typing", snapshot).ConfigureAwait(false);
    }

    public async Task Receipt(string channel, Guid messageId)
    {
        channel = NormalizeChannel(channel);
        EnsureJoined(channel);
        var snapshot = _live.RecordReceipt(channel, messageId, ResolveNick());
        await Clients.Group(channel).SendAsync("Receipt", snapshot[^1]).ConfigureAwait(false);
    }

    /// <summary>
    /// Relays RTC signaling. Video membership is scoped to the supplied
    /// conversation key, defaulting to the channel when omitted.
    /// </summary>
    public async Task Signal(
        string channel,
        string kind,
        string payload,
        string? toNick = null,
        string? conversation = null)
    {
        channel = NormalizeChannel(channel);
        EnsureJoined(channel);

        kind = (kind ?? string.Empty).Trim().ToLowerInvariant();
        if (kind.Length == 0)
            throw new HubException("Signal kind required.");

        payload ??= string.Empty;
        toNick = string.IsNullOrWhiteSpace(toNick) ? null : toNick.Trim();
        var fromNick = ResolveNick();
        var conversationKey = NormalizeConversation(conversation, channel);

        switch (kind)
        {
            case "video-join":
                if (!_directory.TryJoinVideo(channel, Context.ConnectionId, conversationKey))
                {
                    throw new HubException(
                        $"Video mesh full (max {_directory.VideoParticipantLimit}).");
                }
                break;
            case "video-part":
                _directory.TryPartVideo(channel, Context.ConnectionId, conversationKey);
                break;
            case "offer":
            case "answer":
            case "ice":
                break;
            default:
                throw new HubException($"Unknown signal kind '{kind}'.");
        }

        var envelope = new ChatSignalEnvelope(
            channel,
            fromNick,
            kind,
            payload,
            toNick,
            conversationKey);
        if (toNick is not null)
        {
            var targetConnection = _directory.FindConnectionForNick(channel, toNick);
            if (targetConnection is null)
                throw new HubException($"Unknown nick '{toNick}'.");
            await Clients.Client(targetConnection).SendAsync("Signal", envelope).ConfigureAwait(false);
        }
        else
        {
            await Clients.OthersInGroup(channel).SendAsync("Signal", envelope).ConfigureAwait(false);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var channel = _directory.FindChannelForConnection(Context.ConnectionId);
        var nick = _directory.FindNick(Context.ConnectionId);
        if (channel is not null && nick is not null)
        {
            if (_directory.TryPartVideo(channel, Context.ConnectionId))
            {
                await Clients.OthersInGroup(channel)
                    .SendAsync(
                        "Signal",
                        new ChatSignalEnvelope(channel, nick, "video-part", string.Empty))
                    .ConfigureAwait(false);
            }

            var roster = _directory.PartAll(Context.ConnectionId);
            if (roster is not null)
            {
                await Clients.Group(channel)
                    .SendAsync("Roster", new ChatRosterDto(channel, roster))
                    .ConfigureAwait(false);
                await Clients.Group(channel)
                    .SendAsync("Presence", _live.RemovePresence(channel, nick))
                    .ConfigureAwait(false);
            }
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    void EnsureKnownChannel(string channel)
    {
        if (!_directory.IsKnownChannel(channel))
            throw new HubException($"Unknown channel '{channel}'.");
    }

    void EnsureJoined(string channel)
    {
        EnsureKnownChannel(channel);
        var current = _directory.FindChannelForConnection(Context.ConnectionId);
        if (!string.Equals(current, channel, StringComparison.OrdinalIgnoreCase))
            throw new HubException("Join the channel before using this operation.");
    }

    string ResolveNick()
    {
        var nick = Context.User?.FindFirst("nick")?.Value
                   ?? Context.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(nick))
            throw new HubException("Missing nick claim.");
        return nick;
    }

    async Task BroadcastSecureTextGroupAsync(
        string channel,
        SecureTextGroupDto group)
    {
        foreach (var member in group.Members)
        {
            var connectionId = _directory.FindConnectionForDevice(channel, member.DeviceId);
            if (connectionId is not null)
            {
                await Clients.Client(connectionId)
                    .SendAsync("SecureTextGroupMembership", group)
                    .ConfigureAwait(false);
            }
        }
    }

    static string NormalizeChannel(string channel)
    {
        try
        {
            return ChannelId.Parse(channel).NormalizedName;
        }
        catch (ArgumentException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    static string NormalizeConversation(string? conversation, string channel) =>
        string.IsNullOrWhiteSpace(conversation) ? channel : conversation.Trim();

    static SecureTextEnvelope DeserializeEnvelope(byte[] payload, string message)
    {
        try
        {
            return SecureTextEnvelopeCodec.Deserialize(payload);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or EndOfStreamException
                or NotSupportedException
                or ArgumentException)
        {
            throw new HubException(message);
        }
    }

    static SecureTextPublicBundle ToPublicBundle(DeviceBundleDto bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return new SecureTextPublicBundle(
            bundle.ProtocolVersion,
            bundle.DeviceId,
            bundle.SigningPublicKey,
            bundle.AgreementPublicKey,
            bundle.IssuedAtUtc,
            bundle.ExpiresAtUtc,
            bundle.Signature);
    }

    static DeviceBundleDto ToDto(SecureTextPublicBundle bundle) =>
        new(
            bundle.ProtocolVersion,
            bundle.DeviceId,
            bundle.ExportSigningPublicKey(),
            bundle.ExportAgreementPublicKey(),
            bundle.IssuedAtUtc,
            bundle.ExpiresAtUtc,
            bundle.ExportSignature());
}
