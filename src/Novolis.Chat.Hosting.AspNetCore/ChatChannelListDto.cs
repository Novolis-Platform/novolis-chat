using System.Text.Json.Serialization;
using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;

namespace Novolis.Chat.Hosting.AspNetCore;

public sealed record ChatChannelListDto(
    IReadOnlyList<ChatSpaceInfo> Spaces,
    IReadOnlyList<ChatChannelInfo> Channels);
