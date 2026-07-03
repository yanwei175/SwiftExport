namespace SwiftExportClient.Bootstrapper;

public sealed class ClientOptions
{
    public const string SectionName = "Client";

    public string ApplicationName { get; set; } = "SwiftExport";

    public string Version { get; set; } = "0.1.0";
}
