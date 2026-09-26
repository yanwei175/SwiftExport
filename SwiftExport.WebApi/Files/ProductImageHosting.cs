using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace SwiftExport.WebApi.Files;

/// <summary>将部署目录内的产品图片作为只读 HTTP 资源提供，不公开附件、临时文件或整个存储根目录。</summary>
public static class ProductImageHosting
{
    /// <summary>按启动时配置挂载固定图片目录；未启用时保持原管道，启用但目录无效时阻止启动。</summary>
    public static void UseProductImages(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("FileStorage:Enabled")) return;

        var root = app.Configuration["FileStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("启用图片服务时 FileStorage:RootPath 必须是服务器绝对目录。");

        var images = Path.Combine(Path.GetFullPath(root), "ProductImages");
        if (!Directory.Exists(images))
            throw new InvalidOperationException("产品图片目录不存在，请先创建根目录下的 ProductImages 并配置服务账户读取权限。");

        // 只读枚举用来尽早发现部署权限问题，不自动创建或修改服务器目录。
        using (var entries = Directory.EnumerateFileSystemEntries(images).GetEnumerator())
            _ = entries.MoveNext();

        var provider = new PhysicalFileProvider(images);
        app.Lifetime.ApplicationStopped.Register(provider.Dispose);
        var types = new FileExtensionContentTypeProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
            [".gif"] = "image/gif", [".webp"] = "image/webp", [".bmp"] = "image/bmp"
        });

        // 在认证前提供用户确认的公共产品图片；静态文件中间件仅响应 GET/HEAD，不提供上传或目录浏览。
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = provider,
            RequestPath = "/files/ProductImages",
            ContentTypeProvider = types,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = context =>
            {
                // 固定地址允许人工替换：客户端每次复用响应前验证，不能标记为 immutable。
                context.Context.Response.Headers.CacheControl = "no-cache";
                context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            }
        });
    }
}
