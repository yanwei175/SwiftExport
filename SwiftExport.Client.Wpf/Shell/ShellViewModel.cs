using Microsoft.Extensions.Options;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Shell;

public sealed class ShellViewModel : ViewModelBaseEx
{
    public ShellViewModel(IOptions<ClientOptions> clientOptions)
    {
        var options = clientOptions.Value;

        Title = options.ApplicationName;
        StatusMessage = $"Management client shell · v{options.Version}";
        StageTitle = "Ready for stage 4";
        StageDescription = "Host, DI, configuration, and the MVVM page foundation are ready. API, authentication, navigation, and business modules will be added in later stages.";
    }

    public string StatusMessage { get; }

    public string StageTitle { get; }

    public string StageDescription { get; }
}
