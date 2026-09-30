using System.Text.Json.Serialization;
using Novolis.Chat.Abstractions;
using Novolis.Chat.Directory;

namespace Novolis.Chat.Hosting.AspNetCore;

public sealed record ChatRosterDto(string Channel, IReadOnlyList<string> Nicks);
