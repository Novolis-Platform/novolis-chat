using System.Collections.Concurrent;
using Novolis.Chat.Abstractions;
using Novolis.Game.Identity.Abstractions;
using Novolis.Security.SecureText;

namespace Novolis.Chat.Directory;

public sealed record ChatSpaceInfo(SpaceId Id, string Name);
