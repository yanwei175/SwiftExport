using System.Collections.ObjectModel;
using DvFileStorage.Configuration;
using DvFileStorage.Local;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.Models;

namespace SwiftExport.WebApi.Files;

/// <summary>保存 WebAPI 启动时验证完成的显式容器快照，运行期不重新读取 JSON。</summary>
public sealed class ConfiguredFileContainerProvider : IFileContainerProvider
{
    private readonly IReadOnlyDictionary<string, FileContainer> _containers;

    /// <summary>使用已验证容器集合创建大小写稳定的只读提供器。</summary>
    internal ConfiguredFileContainerProvider(IDictionary<string, FileContainer> containers)
    {
        _containers = new ReadOnlyDictionary<string, FileContainer>(
            new Dictionary<string, FileContainer>(containers, StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public bool TryGetContainer(string containerId, out FileContainer? container)
    {
        if (string.IsNullOrEmpty(containerId))
        {
            container = null;
            return false;
        }

        return _containers.TryGetValue(containerId, out container);
    }
}

/// <summary>解析并验证容器白名单、API 基址和物理根目录隔离边界。</summary>
internal static class ConfiguredFileContainerFactory
{
    /// <summary>从宿主配置建立容器快照；无容器时返回空提供器且不要求 ApiBaseUrl。</summary>
    public static ConfiguredFileContainerProvider Create(
        IConfiguration configuration,
        IReadOnlyCollection<string> reservedRoots)
    {
        var children = configuration.GetSection("FileStorage:Containers").GetChildren().ToArray();
        if (children.Length == 0)
            return new ConfiguredFileContainerProvider(new Dictionary<string, FileContainer>());

        var apiBaseUri = ParseApiBaseUri(configuration["FileStorage:ApiBaseUrl"]);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<(string Id, string Path)>();
        var containers = new Dictionary<string, FileContainer>(StringComparer.Ordinal);
        foreach (var child in children)
        {
            var id = child.Key;
            if (!IsValidContainerId(id) || !ids.Add(id))
                throw new InvalidOperationException($"FileStorage:Containers 包含非法或大小写重复的容器标识：{id}");

            var options = child.Get<FileStorageContainerOptions>() ?? new FileStorageContainerOptions();
            if (string.IsNullOrWhiteSpace(options.RootPath))
                throw new InvalidOperationException($"文件容器 {id} 必须配置 RootPath。");
            if (options.MaxUploadBytes <= 0)
                throw new InvalidOperationException($"文件容器 {id} 的 MaxUploadBytes 必须为正整数。");

            var storage = new LocalFileStorage(new LocalFileStorageOptions { RootPath = options.RootPath });
            var fullRoot = NormalizeRoot(options.RootPath);
            EnsureReadable(fullRoot);
            foreach (var existing in roots)
            {
                if (IsSameOrNested(fullRoot, existing.Path) || IsSameOrNested(existing.Path, fullRoot))
                    throw new InvalidOperationException($"文件容器 {id} 与 {existing.Id} 的根目录重复或相互包含。");
            }

            roots.Add((id, fullRoot));
            containers.Add(id, new FileContainer
            {
                Id = id,
                Storage = storage,
                MaxUploadBytes = options.MaxUploadBytes,
                ApiBaseUri = apiBaseUri
            });
        }

        RejectReservedRootOverlap(reservedRoots, roots);
        return new ConfiguredFileContainerProvider(containers);
    }

    /// <summary>容器标识仅允许小写字母、数字和作为分隔符的单个短横线。</summary>
    private static bool IsValidContainerId(string id)
    {
        if (string.IsNullOrEmpty(id) || id[0] == '-' || id[^1] == '-') return false;
        var previousHyphen = false;
        foreach (var character in id)
        {
            if (character == '-')
            {
                if (previousHyphen) return false;
                previousHyphen = true;
                continue;
            }

            previousHyphen = false;
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9')) return false;
        }

        return true;
    }

    /// <summary>要求 HTTP/HTTPS 绝对基址且拒绝凭据、查询和片段，保留合法虚拟路径。</summary>
    private static Uri ParseApiBaseUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "配置容器时 FileStorage:ApiBaseUrl 必须是无凭据、查询和片段的 HTTP/HTTPS 绝对地址。");
        }

        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/')) builder.Path += "/";
        return builder.Uri;
    }

    private static string NormalizeRoot(string rootPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

    /// <summary>只读枚举用于启动时发现根目录读取权限问题，不创建写权限探针。</summary>
    private static void EnsureReadable(string rootPath)
    {
        using var entries = Directory.EnumerateFileSystemEntries(rootPath).GetEnumerator();
        _ = entries.MoveNext();
    }

    /// <summary>按操作系统路径比较规则判断 candidate 是否等于 root 或位于 root 下。</summary>
    private static bool IsSameOrNested(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(candidate, root, comparison)) return true;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }

    /// <summary>拒绝通用容器与公开图片或内部状态目录重叠，防止绕过专用业务边界。</summary>
    private static void RejectReservedRootOverlap(
        IEnumerable<string> reservedRoots,
        IEnumerable<(string Id, string Path)> roots)
    {
        var normalizedReserved = reservedRoots.Select(NormalizeRoot).ToArray();
        foreach (var root in roots)
        {
            foreach (var reserved in normalizedReserved)
            {
                if (IsSameOrNested(root.Path, reserved) || IsSameOrNested(reserved, root.Path))
                    throw new InvalidOperationException($"文件容器 {root.Id} 不能与产品图片或内部状态目录重复或相互包含。");
            }
        }
    }
}
