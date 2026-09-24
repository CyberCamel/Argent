using Argent.Core.DataSources;
using Argent.Runtime.DataSources;
using Microsoft.Extensions.DependencyInjection;

namespace Argent.Runtime.DependencyInjection;

public static class DataSourceExtensions
{
    public static IServiceCollection AddArgentDataSources(this IServiceCollection services)
    {
        services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IDataSourceProvider, SqlDataSourceProvider>();
        services.AddScoped<IDataSourceProvider, RestDataSourceProvider>();
        services.AddScoped<IDataSourceProvider, SoapDataSourceProvider>();
        services.AddScoped<IDataSourceCatalog, DataSourceCatalog>();
        services.AddScoped<IDataSourceRunner, DataSourceRunner>();

        return services;
    }
}
