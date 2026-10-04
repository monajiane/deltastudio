using DeltaStudio.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// One composition used by both hosts: WinUI registers it into its window services, the
/// MCP/CLI host builds a standalone provider. Guarantees "two clients, one engine".
/// </summary>
public static class DeltaStudioServiceCollectionExtensions
{
    /// <summary>Adds the singleton engine to a service collection.</summary>
    public static IServiceCollection AddDeltaStudioEngine(this IServiceCollection services, string? dataDir = null)
    {
        services.AddSingleton(sp => new DeltaStudioEngine(sp.GetService<Microsoft.Extensions.Logging.ILogger<DeltaStudioEngine>>(), dataDir));
        services.AddSingleton(sp => sp.GetRequiredService<DeltaStudioEngine>().Workspace);
        services.AddSingleton(sp => sp.GetRequiredService<DeltaStudioEngine>().Safety);
        services.AddSingleton(sp => sp.GetRequiredService<DeltaStudioEngine>().Registry);
        return services;
    }
}
