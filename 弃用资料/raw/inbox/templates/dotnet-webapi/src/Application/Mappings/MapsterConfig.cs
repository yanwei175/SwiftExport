using Mapster;
using Microsoft.Extensions.DependencyInjection;

namespace {{ProjectName}}.Application.Mappings;

public static class MapsterConfig
{
    public static void RegisterMappings(IServiceCollection services)
    {
        TypeAdapterConfig.GlobalSettings.Default.PreserveReference(true);
    }
}
