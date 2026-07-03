using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Shell;

namespace SwiftExportClient.Bootstrapper;

public static class ViewRegistrationExtensions
{
    public static IServiceCollection AddSwiftExportClientViews(this IServiceCollection services)
    {
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ShellWindow>();

        return services;
    }
}
