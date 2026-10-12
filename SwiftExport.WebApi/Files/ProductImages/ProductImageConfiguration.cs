namespace SwiftExport.WebApi.Files.ProductImages;

/// <summary>解析并验证公开图片与上传入口共享的目录、路由和资源限制配置。</summary>
internal static class ProductImageConfiguration
{
    private const string StorageTemporaryDirectoryName = ".dvfilestorage-tmp";
    /// <summary>旧 JSON 状态目录不再写入，但继续保留，防止被公开图片或通用容器占用。</summary>
    internal const string LegacyProductImageStateDirectoryName = ".product-image-state";
    private static readonly char[] InvalidSegmentCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly HashSet<string> ReservedDirectoryNames = new(
        ["CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$",
         "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>创建物理目录和静态路由快照；公开回执地址由系统参数按上传批次读取。</summary>
    public static ProductImageStorageOptions Create(IConfiguration configuration)
    {
        var publicEnabled = configuration.GetValue<bool>("FileStorage:Enabled");
        var uploadEnabled = configuration.GetValue<bool>("FileStorage:ProductImages:UploadEnabled");
        var directoryName = configuration["FileStorage:ProductImages:DirectoryName"] ?? "ProductImages";
        var requestPath = configuration["FileStorage:ProductImages:RequestPath"] ?? "/files/ProductImages";
        ValidateDirectoryName(directoryName);
        ValidateRequestPath(requestPath);

        var maxFileBytes = configuration.GetValue<long?>("FileStorage:ProductImages:MaxFileBytes") ?? 10 * 1024 * 1024;
        var maxBatchBytes = configuration.GetValue<long?>("FileStorage:ProductImages:MaxBatchBytes") ?? 25 * 1024 * 1024;
        var maxFiles = configuration.GetValue<int?>("FileStorage:ProductImages:MaxFiles") ?? 20;
        var maxPixels = configuration.GetValue<long?>("FileStorage:ProductImages:MaxPixels") ?? 24_000_000;
        var maxConcurrent = configuration.GetValue<int?>("FileStorage:ProductImages:MaxConcurrentProcessing") ?? 2;

        if (uploadEnabled && !publicEnabled)
            throw new InvalidOperationException("启用产品图片上传时必须同时启用既有公开图片入口。");
        if (uploadEnabled &&
            (maxFileBytes <= 0 || maxFileBytes > int.MaxValue || maxBatchBytes < maxFileBytes ||
             maxFiles <= 0 || maxPixels <= 0 || maxConcurrent <= 0))
        {
            throw new InvalidOperationException("产品图片上传大小、文件数、像素和处理并发配置必须是有效正值。");
        }

        var root = configuration["FileStorage:RootPath"];
        string? fullRoot = null;
        if (!string.IsNullOrWhiteSpace(root) && Path.IsPathFullyQualified(root))
            fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (publicEnabled || uploadEnabled)
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
                throw new InvalidOperationException("启用产品图片服务时 FileStorage:RootPath 必须是服务器绝对目录。");
            var imageDirectory = Path.Combine(fullRoot!, directoryName);
            if (!Directory.Exists(imageDirectory))
                throw new InvalidOperationException($"产品图片目录不存在，请先创建根目录下的 {directoryName}。");
            RejectReparsePoint(imageDirectory);
            using var entries = Directory.EnumerateFileSystemEntries(imageDirectory).GetEnumerator();
            _ = entries.MoveNext();
        }

        return new ProductImageStorageOptions
        {
            PublicHostingEnabled = publicEnabled,
            UploadEnabled = uploadEnabled,
            RootPath = fullRoot,
            DirectoryName = directoryName,
            RequestPath = requestPath,
            MaxFileBytes = maxFileBytes,
            MaxBatchBytes = maxBatchBytes,
            MaxFiles = maxFiles,
            MaxPixels = maxPixels,
            MaxConcurrentProcessing = maxConcurrent
        };
    }

    private static void ValidateDirectoryName(string value)
    {
        if (string.IsNullOrEmpty(value) || value is "." or ".." ||
            value.Equals(StorageTemporaryDirectoryName, StringComparison.OrdinalIgnoreCase) ||
            value.Equals(LegacyProductImageStateDirectoryName, StringComparison.OrdinalIgnoreCase) ||
            value.IndexOfAny(InvalidSegmentCharacters) >= 0 ||
            value.EndsWith(' ') || value.EndsWith('.') ||
            value.Any(character => character == '\0' || char.IsControl(character)) ||
            ReservedDirectoryNames.Contains(value.Split('.', 2)[0]))
        {
            throw new InvalidOperationException(
                "FileStorage:ProductImages:DirectoryName 必须是安全的单个目录段，且不能占用存储内部目录。");
        }
    }

    private static void ValidateRequestPath(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith('/') || value.EndsWith('/') ||
            value.Contains('?') || value.Contains('#') || value.Contains('\\') ||
            value.Contains("//", StringComparison.Ordinal) ||
            value.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment =>
                segment is "." or ".." || segment.Any(char.IsControl)))
        {
            throw new InvalidOperationException("FileStorage:ProductImages:RequestPath 必须是无查询和片段的固定绝对请求前缀。");
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("产品图片目录不能是符号链接或其他重解析点。");
    }
}
