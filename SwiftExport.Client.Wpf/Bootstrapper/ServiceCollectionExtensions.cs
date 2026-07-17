using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Authentication;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Services.Images;
using SwiftExportClient.Updates;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Services;
using Wpf.Ui;

namespace SwiftExportClient.Bootstrapper;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSwiftExportClientServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));
        services.Configure<ClientOptions>(configuration.GetSection(ClientOptions.SectionName));
        services.Configure<ImageCacheOptions>(configuration.GetSection(ImageCacheOptions.SectionName));
        services.AddSingleton<ITokenStore, LocalTokenStore>();
        services.AddSingleton<ICurrentUserService, CurrentUserService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<INavigationRouteRegistry, NavigationRouteRegistry>();
        services.AddSingleton<Navigation.INavigationService, Navigation.NavigationService>();
        services.AddSingleton<IApiCallRunner, ApiCallRunner>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<IStatusMessageService, StatusMessageService>();
        services.AddSingleton<IMessageService, WpfUiMessageService>();
        services.AddSingleton<IDialogService, WpfUiDialogService>();
        services.AddSingleton<IClientVersionService, ClientVersionService>();
        services.AddSingleton<IImageCacheService, ImageCacheService>();
        services.AddTransient<INetDemandTraceExportService, NetDemandTraceExportService>();
        services.AddTransient<INetDemandDialogService, NetDemandDialogService>();
        services.AddTransient<AuthTokenHandler>();
        services.AddSwiftExportApis();

        return services;
    }
}
