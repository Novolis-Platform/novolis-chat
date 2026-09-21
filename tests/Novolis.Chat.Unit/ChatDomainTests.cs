using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;
using Novolis.Chat.Live;
using Novolis.Game.Identity.Abstractions;

namespace Novolis.Chat.Unit;

public sealed class ChatDomainTests
{
    [Test]
    public async Task Channel_names_are_normalized_and_named_channels_are_registered()
    {
        var directory = new ChatDirectory();

        await Assert.That(ChannelId.Parse("lobby").NormalizedName).IsEqualTo("#lobby");
        await Assert.That(directory.IsKnownChannel("#lobby")).IsTrue();
        await Assert.That(
                directory.TryCreateChannel(SpaceId.Default, "crew", out var channel))
            .IsTrue();
        await Assert.That(channel).IsNotNull();
        await Assert.That(directory.IsKnownChannel("#crew")).IsTrue();
    }

    [Test]
    public async Task Media_policy_limits_one_conversation_without_blocking_another()
    {
        var directory = new ChatDirectory(new MediaSessionPolicy(MaxPeers: 2));
        directory.Join("#lobby", PlayerRef.New(), "alice", "connection-alice");
        directory.Join("#lobby", PlayerRef.New(), "bob", "connection-bob");
        directory.Join("#lobby", PlayerRef.New(), "carol", "connection-carol");

        await Assert.That(directory.VideoParticipantLimit).IsEqualTo(2);
        await Assert.That(
                directory.TryJoinVideo("#lobby", "connection-alice", "call-a"))
            .IsTrue();
        await Assert.That(
                directory.TryJoinVideo("#lobby", "connection-bob", "call-a"))
            .IsTrue();
        await Assert.That(
                directory.TryJoinVideo("#lobby", "connection-carol", "call-a"))
            .IsFalse();
        await Assert.That(
                directory.TryJoinVideo("#lobby", "connection-carol", "call-b"))
            .IsTrue();
    }

    [Test]
    public async Task Live_typing_expires_from_the_injected_clock()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var live = new ChatLiveState(clock);

        await Assert.That(live.SetTyping("#lobby", "alice", true)).Count().IsEqualTo(1);
        clock.Advance(ChatLiveState.DefaultTypingTtl + TimeSpan.FromMilliseconds(1));
        await Assert.That(live.GetTyping("#lobby")).IsEmpty();
    }

    [Test]
    public async Task Chat_frames_default_to_markdown_without_carrying_a_body()
    {
        var frame = ChatFrame.Create(
            "#lobby",
            Guid.NewGuid(),
            "alice",
            "bob",
            DateTimeOffset.UtcNow);
        var body = MarkdownBody.FromRaw("**hello**");

        await Assert.That(frame.BodyFormat).IsEqualTo(ChatBodyFormat.Markdown);
        await Assert.That(body.Source).IsEqualTo("**hello**");
        await Assert.That(frame.GetType().GetProperty("Body")).IsNull();
    }

    sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        DateTimeOffset _now = initial;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }
}
