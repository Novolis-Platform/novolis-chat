using System.Collections.Concurrent;
using Novolis.Chat.Abstractions;
using Novolis.Game.Identity.Abstractions;
using Novolis.Security.SecureText;

namespace Novolis.Chat.Directory;

public sealed record SecureTextGroupDto(
    Guid GroupId,
    string Name,
    string InitiatorNick,
    IReadOnlyList<SecureTextGroupMemberDto> Members,
    IReadOnlyList<Guid> ApprovedDeviceIds);
