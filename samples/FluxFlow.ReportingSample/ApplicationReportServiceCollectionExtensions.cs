using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.ReportingSample;

public static class ApplicationReportServiceCollectionExtensions
{
    /// <summary>Registers one singleton shared by concrete injection and IEnumerable&lt;IApplicationReport&gt;.</summary>
    public static IServiceCollection AddApplicationReport<TReport>(this IServiceCollection services)
        where TReport : class, IApplicationReport
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TReport>();
        services.AddSingleton<IApplicationReport>(provider => provider.GetRequiredService<TReport>());
        return services;
    }
}
