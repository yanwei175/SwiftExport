using SwiftExport.Contracts.DTOs.Updates;

namespace SwiftExportClient.Updates;

public sealed class ClientVersionCheckResult
{
    public UpdateRequirement Requirement { get; init; }

    public string CurrentVersion { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public ClientVersionInfo? VersionInfo { get; init; }

    public bool CanContinue => Requirement is not UpdateRequirement.Required;

    public static ClientVersionCheckResult None(string currentVersion, ClientVersionInfo versionInfo)
    {
        return new ClientVersionCheckResult
        {
            Requirement = UpdateRequirement.None,
            CurrentVersion = currentVersion,
            VersionInfo = versionInfo,
            Message = "当前客户端已是可用版本。"
        };
    }

    public static ClientVersionCheckResult Optional(string currentVersion, ClientVersionInfo versionInfo)
    {
        return new ClientVersionCheckResult
        {
            Requirement = UpdateRequirement.Optional,
            CurrentVersion = currentVersion,
            VersionInfo = versionInfo,
            Message = $"发现新版本 {versionInfo.LatestVersion}，当前版本 {currentVersion}。"
        };
    }

    public static ClientVersionCheckResult Required(string currentVersion, ClientVersionInfo versionInfo)
    {
        return new ClientVersionCheckResult
        {
            Requirement = UpdateRequirement.Required,
            CurrentVersion = currentVersion,
            VersionInfo = versionInfo,
            Message = $"当前版本 {currentVersion} 已低于最低支持版本 {versionInfo.MinimumSupportedVersion}，请更新后继续使用。"
        };
    }

    public static ClientVersionCheckResult Failed(string currentVersion, string message)
    {
        return new ClientVersionCheckResult
        {
            Requirement = UpdateRequirement.CheckFailed,
            CurrentVersion = currentVersion,
            Message = message
        };
    }
}
