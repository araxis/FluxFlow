using FluxFlow.Conversations.Local;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Conversations;

public static class ConversationServiceCollectionExtensions
{
    public static IServiceCollection AddLocalConversations(this IServiceCollection services)
    {
        services.AddHttpClient(nameof(LocalProposalGenerator), ConfigureClient);
        services.AddTransient<IProposalGenerator, LocalProposalGenerator>();
        services.AddHttpClient<IProposalConnectionService, LocalProposalConnectionService>(ConfigureClient);
        return services;
    }

    private static void ConfigureClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromMinutes(2);
        client.MaxResponseContentBufferSize = 1_048_576;
    }
}
