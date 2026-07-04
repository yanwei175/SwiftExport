using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Updates;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Updates;

public sealed class ClientVersionService(
    IClientVersionApi clientVersionApi,
    IApiCallRunner apiCallRunner,
    IOptions<ClientOptions> clientOptions,
    ILogger<ClientVersionService> logger) : IClientVersionService
{
    public async Task<ClientVersionCheckResult> CheckAsync()
    {
        var currentVersion = clientOptions.Value.Version;
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
