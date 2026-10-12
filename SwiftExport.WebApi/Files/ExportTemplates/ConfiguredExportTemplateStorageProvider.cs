using DvFileStorage.Configuration;
using DvFileStorage.Local;
using SwiftExport.Application.Interfaces.Files;
using SwiftExport.Application.Interfaces.Files.Models;
using SwiftExport.WebApi.Files.ProductImages;

namespace SwiftExport.WebApi.Files.ExportTemplates;

/// <summary>从启动配置建立独立模板存储，拒绝与公开图片或通用可写容器相互包含的根目录。</summary>
public sealed class ConfiguredExportTemplateStorageProvider : IExportTemplateStorageProvider
{
    private readonly ExportTemplateStorageContext? _context;

    /// <summary>仅在 FileStorage:ExportTemplates:Enabled 开启时验证根目录；不创建目录、不注册通用容器。</summary>
    public ConfiguredExportTemplateStorageProvider(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue<bool>("FileStorage:ExportTemplates:Enabled")) return;

        var configuredRoot = configuration["FileStorage:ExportTemplates:RootPath"];
        if (string.IsNullOrWhiteSpace(configuredRoot) || !Path.IsPathFullyQualified(configuredRoot))
            throw new InvalidOperationException("启用模板分发时必须配置已存在的绝对模板根目录。");

        var storage = new LocalFileStorage(new LocalFileStorageOptions { RootPath = configuredRoot });
        var root = NormalizeRoot(configuredRoot);
        // 只读确认目录可访问，禁止启动验证用创建文件探测权限。
        using (var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator())
            _ = entries.MoveNext();

        var imageRoot = configuration["FileStorage:RootPath"];
        if (!string.IsNullOrWhiteSpace(imageRoot))
        {
            var imageDirectoryName = configuration["FileStorage:ProductImages:DirectoryName"] ?? "ProductImages";
            RejectOverlap(root, Path.Combine(imageRoot, imageDirectoryName), "产品图片");
            RejectOverlap(root, Path.Combine(imageRoot, ProductImageConfiguration.LegacyProductImageStateDirectoryName), "产品图片内部状态");
            // DvFileStorage 临时目录没有公开常量；固定保留段须与既有字节存储的内部协议一致。
            RejectOverlap(root, Path.Combine(imageRoot, ".dvfilestorage-tmp"), "文件存储内部临时");
        }

        foreach (var container in configuration.GetSection("FileStorage:Containers").GetChildren())
        {
            var containerRoot = container["RootPath"];
            if (!string.IsNullOrWhiteSpace(containerRoot))
                RejectOverlap(root, containerRoot, "通用文件容器");
        }

        _context = new ExportTemplateStorageContext { Storage = storage };
    }

    /// <inheritdoc />
    public bool TryGetEnabledStorage(out ExportTemplateStorageContext? context)
    {
        context = _context;
        return context is not null;
    }

    /// <summary>两个目录任一相等或包含对方都拒绝，避免只检查一个方向留下读写绕行入口。</summary>
    private static void RejectOverlap(string templateRoot, string otherRoot, string category)
    {
        var normalized = NormalizeRoot(otherRoot);
        if (IsSameOrNested(templateRoot, normalized) || IsSameOrNested(normalized, templateRoot))
            throw new InvalidOperationException($"模板根目录不能与{category}目录重复或相互包含。");
    }

    private static string NormalizeRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrNested(string candidate, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(candidate, root, comparison)) return true;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, comparison);
    }
}
