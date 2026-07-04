using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SwiftExportClient.Authentication;

public sealed class LocalTokenStore(ILogger<LocalTokenStore> logger) : ITokenStore
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SwiftExport",
        "Client");

    private static readonly string FilePath = Path.Combine(FolderPath, "token.cache");

    private readonly object _gate = new();
    private string? _accessToken;
    private bool _loaded;

    public bool HasAccessToken => !string.IsNullOrWhiteSpace(GetAccessToken());

    public string? GetAccessToken()
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _accessToken;
        }
    }

    public void SetAccessToken(string accessToken)
    {
        lock (_gate)
        {
            _accessToken = accessToken;
            Directory.CreateDirectory(FolderPath);

            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(accessToken),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            File.WriteAllBytes(FilePath, protectedBytes);
            _loaded = true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _accessToken = null;
            _loaded = true;

            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to delete local token cache.");
            }
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            var protectedBytes = File.ReadAllBytes(FilePath);
            var bytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            _accessToken = Encoding.UTF8.GetString(bytes);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load local token cache.");
            _accessToken = null;
        }
    }
}
