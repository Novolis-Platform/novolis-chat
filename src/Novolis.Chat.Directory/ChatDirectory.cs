using System.Collections.Concurrent;
using Novolis.Chat.Abstractions;
using Novolis.Game.Identity.Abstractions;
using Novolis.Security.SecureText;

namespace Novolis.Chat.Directory;

/// <summary>
/// In-memory directory for named spaces, channels, connected members, devices,
/// and explicitly approved SecureText group membership.
/// </summary>
public sealed class ChatDirectory
{
    public const string Lobby = "#lobby";
    public const int MaxVideoParticipants = 4;
    public const int MaxSecureTextGroupMembers = 8;

    readonly ConcurrentDictionary<string, SpaceState> _spaces = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, ChannelState> _channels = new(StringComparer.OrdinalIgnoreCase);
    readonly MediaSessionPolicy _mediaSessionPolicy;

    public ChatDirectory(MediaSessionPolicy? mediaSessionPolicy = null)
    {
        _mediaSessionPolicy = (mediaSessionPolicy ?? new MediaSessionPolicy()).Normalize();
        var defaultSpace = new SpaceState(SpaceId.Default, "Default");
        _spaces[SpaceKey(SpaceId.Default)] = defaultSpace;
        RegisterChannel(defaultSpace, Lobby);
    }

    /// <summary>Admission policy applied to each conversation-scoped media session.</summary>
    public MediaSessionPolicy MediaSessionPolicy => _mediaSessionPolicy;

    /// <summary>
    /// Effective participant limit after applying the policy and the native mesh ceiling.
    /// </summary>
    public int VideoParticipantLimit => Math.Min(_mediaSessionPolicy.MaxPeers, MaxVideoParticipants);

    public IReadOnlyList<ChatSpaceInfo> GetSpaces() =>
        _spaces.Values
            .OrderBy(space => space.Name, StringComparer.OrdinalIgnoreCase)
            .Select(space => new ChatSpaceInfo(space.Id, space.Name))
            .ToArray();

    public IReadOnlyList<ChatChannelInfo> GetChannels(SpaceId spaceId) =>
        _channels.Values
            .Where(channel => channel.SpaceId == spaceId)
            .OrderBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
            .Select(channel => new ChatChannelInfo(
                ChannelId.Create(channel.SpaceId, channel.Name),
                channel.Name))
            .ToArray();

    public bool TryCreateSpace(SpaceId spaceId, string name, out ChatSpaceInfo? space)
    {
        space = null;
        if (spaceId.Value == Guid.Empty || string.IsNullOrWhiteSpace(name))
            return false;

        var candidate = new SpaceState(spaceId, name.Trim());
        if (!_spaces.TryAdd(SpaceKey(spaceId), candidate))
            return false;

        space = new ChatSpaceInfo(spaceId, candidate.Name);
        return true;
    }

    public bool TryCreateChannel(
        SpaceId spaceId,
        string name,
        out ChatChannelInfo? channel)
    {
        channel = null;
        if (!_spaces.ContainsKey(SpaceKey(spaceId)))
            return false;

        string normalized;
        try
        {
            normalized = ChannelId.Create(spaceId, name).NormalizedName;
        }
        catch (ArgumentException)
        {
            return false;
        }

        var state = new ChannelState(spaceId, normalized);
        if (!_channels.TryAdd(ChannelKey(spaceId, normalized), state))
            return false;

        channel = new ChatChannelInfo(ChannelId.Create(spaceId, normalized), normalized);
        return true;
    }

    public bool IsKnownChannel(string channel) => TryGetChannel(channel, out _);

    public IReadOnlyList<string> Join(
        string channel,
        PlayerRef player,
        string nick,
        string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nick);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            state.Members[connectionId] = new Member(player, nick.Trim());
            return RosterNicks(state);
        }
    }

    public IReadOnlyList<string>? Part(string channel, string connectionId)
    {
        if (!TryGetChannel(channel, out var state))
            return null;

        lock (state.Gate)
        {
            state.Members.Remove(connectionId);
            RemoveVideoMember(state, connectionId);
            return RosterNicks(state);
        }
    }

    public IReadOnlyList<string>? PartAll(string connectionId)
    {
        string? channel = null;
        IReadOnlyList<string>? roster = null;
        foreach (var pair in _channels)
        {
            lock (pair.Value.Gate)
            {
                if (!pair.Value.Members.Remove(connectionId))
                    continue;

                RemoveVideoMember(pair.Value, connectionId);
                channel = pair.Value.Name;
                roster = RosterNicks(pair.Value);
            }
        }

        return channel is null ? null : roster;
    }

    public string? FindChannelForConnection(string connectionId)
    {
        foreach (var pair in _channels)
        {
            lock (pair.Value.Gate)
            {
                if (pair.Value.Members.ContainsKey(connectionId))
                    return pair.Value.Name;
            }
        }

        return null;
    }

    public string? FindNick(string connectionId)
    {
        foreach (var pair in _channels)
        {
            lock (pair.Value.Gate)
            {
                if (pair.Value.Members.TryGetValue(connectionId, out var member))
                    return member.Nick;
            }
        }

        return null;
    }

    /// <summary>
    /// Registers a video participant inside one channel conversation. The
    /// conversation key keeps separate calls from consuming one another's mesh.
    /// </summary>
    public bool TryJoinVideo(
        string channel,
        string connectionId,
        string? conversation = null)
    {
        var state = GetOrThrow(channel);
        var conversationKey = NormalizeConversation(conversation, state.Name);
        lock (state.Gate)
        {
            if (!state.Members.ContainsKey(connectionId))
                return false;

            var members = GetVideoMembers(state, conversationKey);
            if (members.Contains(connectionId))
                return true;
            if (members.Count >= VideoParticipantLimit)
                return false;

            members.Add(connectionId);
            return true;
        }
    }

    public bool TryPartVideo(
        string channel,
        string connectionId,
        string? conversation = null)
    {
        if (!TryGetChannel(channel, out var state))
            return false;

        var conversationKey = NormalizeConversation(conversation, state.Name);
        lock (state.Gate)
        {
            if (conversation is null)
                return RemoveVideoMember(state, connectionId);

            return state.VideoMembers.TryGetValue(conversationKey, out var members)
                   && members.Remove(connectionId);
        }
    }

    public string? FindConnectionForNick(string channel, string nick)
    {
        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            return state.Members
                .FirstOrDefault(pair =>
                    string.Equals(pair.Value.Nick, nick, StringComparison.OrdinalIgnoreCase))
                .Key;
        }
    }

    public bool TryRegisterDevice(
        string channel,
        string nick,
        string connectionId,
        SecureTextPublicBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            if (!state.Members.TryGetValue(connectionId, out var member)
                || !string.Equals(member.Nick, nick, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (state.Devices.TryGetValue(bundle.DeviceId, out var existing)
                && !string.Equals(existing.Nick, nick, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var replacedDeviceIds = state.Devices
                .Where(pair => string.Equals(pair.Value.Nick, nick, StringComparison.OrdinalIgnoreCase)
                               && pair.Key != bundle.DeviceId)
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var deviceId in replacedDeviceIds)
                state.Devices.Remove(deviceId);

            state.Devices[bundle.DeviceId] = new Device(nick, bundle);
            return true;
        }
    }

    public SecureTextPublicBundle? TryGetDeviceBundle(string channel, string nick)
    {
        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            foreach (var device in state.Devices.Values)
            {
                if (string.Equals(device.Nick, nick, StringComparison.OrdinalIgnoreCase))
                    return device.Bundle;
            }

            return null;
        }
    }

    public bool IsRegisteredDevice(string channel, string nick, Guid deviceId)
    {
        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            return IsRegisteredDevice(state, nick, deviceId);
        }
    }

    public string? FindConnectionForDevice(string channel, Guid deviceId)
    {
        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            if (!state.Devices.TryGetValue(deviceId, out var device))
                return null;

            return state.Members
                .FirstOrDefault(pair =>
                    string.Equals(pair.Value.Nick, device.Nick, StringComparison.OrdinalIgnoreCase))
                .Key;
        }
    }

    public bool TryCreateSecureTextGroup(
        string channel,
        Guid groupId,
        string name,
        string initiatorNick,
        Guid initiatorDeviceId,
        IReadOnlyList<SecureTextGroupMemberDto> members,
        out SecureTextGroupDto? group)
    {
        group = null;
        if (groupId == Guid.Empty
            || string.IsNullOrWhiteSpace(name)
            || members is null
            || members.Count is < 2 or > MaxSecureTextGroupMembers)
        {
            return false;
        }

        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            if (state.SecureTextGroups.ContainsKey(groupId)
                || !IsRegisteredDevice(state, initiatorNick, initiatorDeviceId)
                || !MembersAreValid(state, members)
                || !members.Any(member => member.DeviceId == initiatorDeviceId
                                          && string.Equals(member.Nick, initiatorNick, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var groupState = new SecureTextGroupState(
                groupId,
                name.Trim(),
                initiatorNick.Trim(),
                members.Select(member => new SecureTextGroupMember(member.Nick.Trim(), member.DeviceId)).ToArray());
            groupState.ApprovedDeviceIds.Add(initiatorDeviceId);
            state.SecureTextGroups[groupId] = groupState;
            group = ToDto(groupState);
            return true;
        }
    }

    public bool TryApproveSecureTextGroup(
        string channel,
        Guid groupId,
        string nick,
        Guid deviceId,
        out SecureTextGroupDto? group)
    {
        group = null;
        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            if (!state.SecureTextGroups.TryGetValue(groupId, out var groupState)
                || !IsRegisteredDevice(state, nick, deviceId)
                || !groupState.Members.Any(member =>
                    member.DeviceId == deviceId
                    && string.Equals(member.Nick, nick, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            groupState.ApprovedDeviceIds.Add(deviceId);
            group = ToDto(groupState);
            return true;
        }
    }

    public SecureTextGroupDto? TryGetSecureTextGroup(string channel, Guid groupId)
    {
        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            return state.SecureTextGroups.TryGetValue(groupId, out var group)
                ? ToDto(group)
                : null;
        }
    }

    public IReadOnlyList<SecureTextGroupDto> GetSecureTextGroupsForDevice(
        string channel,
        Guid deviceId)
    {
        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            return state.SecureTextGroups.Values
                .Where(group => group.Members.Any(member => member.DeviceId == deviceId))
                .Select(ToDto)
                .ToArray();
        }
    }

    public bool IsSecureTextGroupMember(
        string channel,
        Guid groupId,
        string nick,
        Guid deviceId)
    {
        var state = GetOrThrow(channel);
        lock (state.Gate)
        {
            return state.SecureTextGroups.TryGetValue(groupId, out var group)
                   && group.Members.Any(member =>
                       member.DeviceId == deviceId
                       && string.Equals(member.Nick, nick, StringComparison.OrdinalIgnoreCase));
        }
    }

    bool TryGetChannel(string channel, out ChannelState state)
    {
        state = null!;
        if (string.IsNullOrWhiteSpace(channel))
            return false;

        string normalized;
        try
        {
            normalized = ChannelId.Parse(channel).NormalizedName;
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (_channels.TryGetValue(ChannelKey(SpaceId.Default, normalized), out var found))
        {
            state = found;
            return true;
        }

        return false;
    }

    ChannelState GetOrThrow(string channel)
    {
        if (!TryGetChannel(channel, out var state))
            throw new InvalidOperationException($"Unknown channel '{channel}'.");

        return state;
    }

    void RegisterChannel(SpaceState space, string name)
    {
        var normalized = ChannelId.Create(space.Id, name).NormalizedName;
        _channels[ChannelKey(space.Id, normalized)] = new ChannelState(space.Id, normalized);
    }

    static string SpaceKey(SpaceId id) => id.Value.ToString("N");

    static string ChannelKey(SpaceId spaceId, string name) =>
        $"{SpaceKey(spaceId)}:{name.Trim().ToUpperInvariant()}";

    static string NormalizeConversation(string? conversation, string channel)
    {
        var value = string.IsNullOrWhiteSpace(conversation) ? channel : conversation.Trim();
        return value.ToUpperInvariant();
    }

    static IReadOnlyList<string> RosterNicks(ChannelState state) =>
        state.Members.Values
            .Select(member => member.Nick)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(nick => nick, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    static HashSet<string> GetVideoMembers(ChannelState state, string conversation)
    {
        if (!state.VideoMembers.TryGetValue(conversation, out var members))
        {
            members = new HashSet<string>(StringComparer.Ordinal);
            state.VideoMembers[conversation] = members;
        }

        return members;
    }

    static bool RemoveVideoMember(ChannelState state, string connectionId)
    {
        var removed = false;
        foreach (var members in state.VideoMembers.Values)
            removed |= members.Remove(connectionId);
        return removed;
    }

    static bool IsRegisteredDevice(ChannelState state, string nick, Guid deviceId) =>
        state.Devices.TryGetValue(deviceId, out var device)
        && string.Equals(device.Nick, nick, StringComparison.OrdinalIgnoreCase);

    static bool MembersAreValid(
        ChannelState state,
        IReadOnlyList<SecureTextGroupMemberDto> members)
    {
        if (members.Select(member => member.DeviceId).Distinct().Count() != members.Count
            || members.Select(member => member.Nick).Distinct(StringComparer.OrdinalIgnoreCase).Count() != members.Count)
        {
            return false;
        }

        foreach (var member in members)
        {
            if (string.IsNullOrWhiteSpace(member.Nick)
                || member.DeviceId == Guid.Empty
                || !IsRegisteredDevice(state, member.Nick, member.DeviceId)
                || state.Members.Values.All(connected =>
                    !string.Equals(connected.Nick, member.Nick, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return true;
    }

    static SecureTextGroupDto ToDto(SecureTextGroupState group) =>
        new(
            group.GroupId,
            group.Name,
            group.InitiatorNick,
            group.Members
                .Select(member => new SecureTextGroupMemberDto(member.Nick, member.DeviceId))
                .ToArray(),
            group.ApprovedDeviceIds.Order().ToArray());

    sealed class SpaceState(SpaceId id, string name)
    {
        public SpaceId Id { get; } = id;
        public string Name { get; } = name;
    }

    sealed class ChannelState(SpaceId spaceId, string name)
    {
        public SpaceId SpaceId { get; } = spaceId;
        public string Name { get; } = name;
        public object Gate { get; } = new();
        public Dictionary<string, Member> Members { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> VideoMembers { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<Guid, Device> Devices { get; } = [];
        public Dictionary<Guid, SecureTextGroupState> SecureTextGroups { get; } = [];
    }

    readonly record struct Member(PlayerRef Player, string Nick);

    readonly record struct Device(string Nick, SecureTextPublicBundle Bundle);

    readonly record struct SecureTextGroupMember(string Nick, Guid DeviceId);

    sealed class SecureTextGroupState(
        Guid groupId,
        string name,
        string initiatorNick,
        IReadOnlyList<SecureTextGroupMember> members)
    {
        public Guid GroupId { get; } = groupId;
        public string Name { get; } = name;
        public string InitiatorNick { get; } = initiatorNick;
        public IReadOnlyList<SecureTextGroupMember> Members { get; } = members;
        public HashSet<Guid> ApprovedDeviceIds { get; } = [];
    }
}

public sealed record ChatSpaceInfo(SpaceId Id, string Name);

public sealed record ChatChannelInfo(ChannelId Id, string Name);

public sealed record SecureTextGroupMemberDto(string Nick, Guid DeviceId);

public sealed record SecureTextGroupDto(
    Guid GroupId,
    string Name,
    string InitiatorNick,
    IReadOnlyList<SecureTextGroupMemberDto> Members,
    IReadOnlyList<Guid> ApprovedDeviceIds);
