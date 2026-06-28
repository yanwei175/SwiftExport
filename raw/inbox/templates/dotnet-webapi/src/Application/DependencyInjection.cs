using Microsoft.Extensions.DependencyInjection;
using {{ProjectName}}.Application.Contexts;
using {{ProjectName}}.Application.Interfaces;
using {{ProjectName}}.Application.Mappings;
using {{ProjectName}}.Application.Services.Sample;

namespace {{ProjectName}}.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        MapsterConfig.RegisterMappings(services);
        services.AddLogging();
        services.AddScoped<ServiceBaseContext>();
        services.AddScoped<ISampleItemService, SampleItemService>();
        return services;
    }
}
