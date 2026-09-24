using Argent.Core.DomainObjects;
using Argent.Runtime.DomainObjects;
using Microsoft.Extensions.DependencyInjection;

namespace Argent.Runtime.DependencyInjection;

public static class DomainObjectExtensions
{
    public static IServiceCollection AddArgentDomainObjects(this IServiceCollection services)
    {
        services.AddScoped<IDomainObjectDefinitionService, DomainObjectDefinitionService>();
        services.AddScoped<IDomainObjectStore, DomainObjectStore>();

        return services;
    }
}
