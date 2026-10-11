using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Updates;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Updates;

/// <summary>使用真实安装版本检查后端最低兼容版本，不依赖可能过期的配置版本。</summary>
public sealed class ClientVersionService(
    IClientVersionApi clientVersionApi,
    IApiCallRunner apiCallRunner,
    IClientUpdateService updates,
    ILogger<ClientVersionService> logger) : IClientVersionService
{
    /// <summary>读取后端发布策略并区分必须升级、建议升级及检查失败。</summary>
    public async Task<ClientVersionCheckResult> CheckAsync()
    {
        var currentVersion = updates.CurrentVersion;
        var result = await apiCallRunner.RunAsync(
            clientVersionApi.GetAsync,
            "版本检查失败");

        if (!result.Success || result.Data is null)
        {
            var message = string.IsNullOrWhiteSpace(result.Message)
                ? "版本检查失败。"
                : result.Message;

            logger.LogWarning("Client version check failed: {Message}", message);
            return ClientVersionCheckResult.Failed(currentVersion, message);
        }

        return Evaluate(currentVersion, result.Data);
    }

    private static ClientVersionCheckResult Evaluate(
        string currentVersion,
        ClientVersionInfo versionInfo)
    {
        if (CompareVersions(currentVersion, versionInfo.MinimumSupportedVersion) < 0)
        {
            return ClientVersionCheckResult.Required(currentVersion, versionInfo);
        }

        if (CompareVersions(currentVersion, versionInfo.LatestVersion) < 0)
        {
            return ClientVersionCheckResult.Optional(currentVersion, versionInfo);
        }

        return ClientVersionCheckResult.None(currentVersion, versionInfo);
    }

    private static int CompareVersions(string left, string right)
    {
        if (Version.TryParse(NormalizeVersion(left), out var leftVersion)
            && Version.TryParse(NormalizeVersion(right), out var rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string value)
    {
        var trimmed = value.Trim();
        var suffixIndex = trimmed.IndexOfAny(['-', '+']);
        return suffixIndex >= 0 ? trimmed[..suffixIndex] : trimmed;
    }
}
