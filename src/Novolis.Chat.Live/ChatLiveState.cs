using Novolis.Chat.Abstractions;

namespace Novolis.Chat.Live;

/// <summary>
/// Process-local live state. It is intentionally ephemeral; a host can add a
/// coordination layer later when a second application needs the same state.
/// </summary>
public sealed class ChatLiveState
{
    public static readonly TimeSpan DefaultTypingTtl = TimeSpan.FromSeconds(5);

    readonly object _gate = new();
    readonly TimeProvider _clock;
    readonly Dictionary<string, Dictionary<string, ChatPresence>> _presence =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Dictionary<string, ChatTyping>> _typing =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Dictionary<Guid, ChatReceipt>> _receipts =
        new(StringComparer.OrdinalIgnoreCase);

    public ChatLiveState(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public IReadOnlyList<ChatPresence> SetPresence(
        string conversation,
        string nick,
        ChatPresenceStatus status = ChatPresenceStatus.Online)
    {
        var key = Normalize(conversation);
        var normalizedNick = NormalizeNick(nick);
        lock (_gate)
        {
            var entries = GetOrCreate(_presence, key);
            entries[normalizedNick] = new ChatPresence(key, normalizedNick, status, UtcNow);
            return entries.Values.OrderBy(value => value.Nick, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public IReadOnlyList<ChatPresence> RemovePresence(string conversation, string nick)
    {
        var key = Normalize(conversation);
        lock (_gate)
        {
            if (_presence.TryGetValue(key, out var entries))
            {
                entries.Remove(NormalizeNick(nick));
                if (entries.Count == 0)
                    _presence.Remove(key);
                else
                    return entries.Values
                        .OrderBy(value => value.Nick, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }

            return [];
        }
    }

    public IReadOnlyList<ChatPresence> GetPresence(string conversation)
    {
        var key = Normalize(conversation);
        lock (_gate)
        {
            return _presence.TryGetValue(key, out var entries)
                ? entries.Values.OrderBy(value => value.Nick, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        }
    }

    public IReadOnlyList<ChatTyping> SetTyping(
        string conversation,
        string nick,
        bool isTyping,
        TimeSpan? ttl = null)
    {
        var key = Normalize(conversation);
        var normalizedNick = NormalizeNick(nick);
        lock (_gate)
        {
            var entries = GetOrCreate(_typing, key);
            PurgeTyping(entries);
            if (isTyping)
            {
                var effectiveTtl = ttl ?? DefaultTypingTtl;
                if (effectiveTtl <= TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(nameof(ttl), "Typing TTL must be positive.");
                entries[normalizedNick] = new ChatTyping(key, normalizedNick, UtcNow + effectiveTtl);
            }
            else
            {
                entries.Remove(normalizedNick);
            }

            if (entries.Count == 0)
                _typing.Remove(key);

            return entries.Values
                .OrderBy(value => value.Nick, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<ChatTyping> GetTyping(string conversation)
    {
        var key = Normalize(conversation);
        lock (_gate)
        {
            if (!_typing.TryGetValue(key, out var entries))
                return [];

            PurgeTyping(entries);
            if (entries.Count == 0)
            {
                _typing.Remove(key);
                return [];
            }

            return entries.Values
                .OrderBy(value => value.Nick, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public IReadOnlyList<ChatReceipt> RecordReceipt(
        string conversation,
        Guid messageId,
        string nick)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("A receipt requires a message id.", nameof(messageId));

        var key = Normalize(conversation);
        var normalizedNick = NormalizeNick(nick);
        lock (_gate)
        {
            var entries = GetOrCreate(_receipts, key);
            entries[messageId] = new ChatReceipt(
                key,
                MessageRef.FromGuid(messageId),
                normalizedNick,
                UtcNow);
            return entries.Values
                .OrderBy(value => value.AtUtc)
                .ToArray();
        }
    }

    public IReadOnlyList<ChatReceipt> GetReceipts(string conversation)
    {
        var key = Normalize(conversation);
        lock (_gate)
        {
            return _receipts.TryGetValue(key, out var entries)
                ? entries.Values.OrderBy(value => value.AtUtc).ToArray()
                : [];
        }
    }

    DateTimeOffset UtcNow => _clock.GetUtcNow();

    void PurgeTyping(Dictionary<string, ChatTyping> entries)
    {
        var now = UtcNow;
        foreach (var nick in entries
                     .Where(pair => pair.Value.ExpiresAtUtc <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            entries.Remove(nick);
        }
    }

    static Dictionary<TKey, TValue> GetOrCreate<TKey, TValue>(
        Dictionary<string, Dictionary<TKey, TValue>> state,
        string key)
        where TKey : notnull
    {
        if (!state.TryGetValue(key, out var entries))
        {
            entries = typeof(TKey) == typeof(string)
                ? new Dictionary<TKey, TValue>(
                    (IEqualityComparer<TKey>)(object)StringComparer.OrdinalIgnoreCase)
                : new Dictionary<TKey, TValue>();
            state[key] = entries;
        }

        return entries;
    }

    static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }

    static string NormalizeNick(string value) => ChatNick.Parse(value).Value;
}
