using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Novolis.Chat.Directory;
using Novolis.Chat.Live;

namespace Novolis.Chat.Hosting.AspNetCore;

public static class ChatHostServiceCollectionExtensions
{
    public static IServiceCollection AddChatHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ChatDirectory>();
        services.AddSingleton<ChatLiveState>();
        services.AddSignalR();
        return services;
    }

    public static IEndpointConventionBuilder MapChatHub(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/hubs/chat")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapHub<ChatHub>(pattern);
    }
}
