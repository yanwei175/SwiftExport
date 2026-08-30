using Microsoft.Extensions.DependencyInjection;
using {{ProjectName}}.Core.Interfaces;
using {{ProjectName}}.Core.Interfaces.Auth;
using {{ProjectName}}.Infrastructure.Contexts;
using {{ProjectName}}.Infrastructure.Data;
using {{ProjectName}}.Infrastructure.Repositories.Base;
using {{ProjectName}}.Infrastructure.Repositories.Sample;
using {{ProjectName}}.Infrastructure.Services.Auth;

namespace {{ProjectName}}.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddScoped<DbSession>();
        services.AddScoped<RepositoryBaseContext>();
        services.AddScoped<IAuditStampService, AuditStampService>();
        services.AddScoped(typeof(IRepository<>), typeof(DapperRepository<>));
        services.AddScoped<ISampleItemRepository, SampleItemRepository>();
        return services;
    }
}
