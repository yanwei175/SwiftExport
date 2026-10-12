using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Npgsql;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SystemDtos.SystemParameters;

namespace SwiftExport.Tools.DbSeed;

/// <summary>显式初始化图片基址，不执行 DDL、不覆盖已配置值；连接信息只来自专用环境变量。</summary>
public static class ProductImageParameterInitializer
{
    /// <summary>从明确指定的服务器配置推导旧公开地址，按原有虚拟目录拼接 RequestPath。</summary>
    public static bool TryDeriveBaseUrl(string json, out Uri baseUri)
    {
        baseUri = null!;
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!document.RootElement.TryGetProperty("FileStorage", out var storage)
            || !storage.TryGetProperty("ApiBaseUrl", out var value)
            || !ProductImageAddress.TryNormalizeBaseUrl(value.GetString()!, out var apiBase)) return false;
        var requestPath = "/files/ProductImages";
        if (storage.TryGetProperty("ProductImages", out var images) && images.TryGetProperty("RequestPath", out var path))
            requestPath = path.GetString() ?? requestPath;
        if (!requestPath.StartsWith("/", StringComparison.Ordinal) || requestPath.Contains('\\')
            || requestPath.Contains('?') || requestPath.Contains('#') || requestPath.Contains("//", StringComparison.Ordinal)) return false;
        foreach (var segment in requestPath.Split('/')) if (segment is "." or "..") return false;
        return ProductImageAddress.TryNormalizeBaseUrl(new Uri(apiBase, requestPath.TrimStart('/') + "/").AbsoluteUri, out baseUri);
    }

    /// <summary>核对目标主机、端口和库名后初始化缺失值；dry-run 只读取，既有值永不覆盖。</summary>
    public static async Task RunAsync(string[] args)
    {
        var options = ParseArguments(args, out var dryRun);
        var json = await File.ReadAllTextAsync(Path.GetFullPath(options["--config"]));
        if (!TryDeriveBaseUrl(json, out var baseUri))
        {
            Console.WriteLine("服务器配置不能推导有效产品原图基址；保持未配置，请管理员从系统参数页面保存。");
            return;
        }
        var connectionString = Environment.GetEnvironmentVariable("SWIFTEXPORT_SYSTEMPARAM_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("缺少 SWIFTEXPORT_SYSTEMPARAM_CONNECTION；不使用内置连接。");
        ValidateTarget(connectionString, options["--target-host"], options["--target-port"], options["--target-database"]);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT parameter_value FROM public.system_parameter WHERE parameter_key=@key";
        command.Parameters.AddWithValue("key", SystemParameterKeys.ProductImagesBaseUrl);
        var existing = await command.ExecuteScalarAsync();
        if (existing is not null)
        {
            Console.WriteLine("产品图片基址已配置，保留现有值；未执行写入。");
            await transaction.RollbackAsync();
            return;
        }
        if (dryRun)
        {
            Console.WriteLine($"目标库 {options["--target-database"]} 尚未配置，拟初始化为 {baseUri.AbsoluteUri}；dry-run 未写入。");
            await transaction.RollbackAsync();
            return;
        }
        command.CommandText = """
            INSERT INTO public.system_parameter
              (uid,version,is_deleted,created_date,created_by,updated_date,updated_by,parameter_key,parameter_value)
            VALUES (@uid,1,FALSE,LOCALTIMESTAMP,'system-parameter-initializer',LOCALTIMESTAMP,'system-parameter-initializer',@key,@value)
            ON CONFLICT (parameter_key) DO NOTHING
            """;
        command.Parameters.AddWithValue("uid", GuidSequentialGenerator.NewGuid());
        command.Parameters.AddWithValue("value", baseUri.AbsoluteUri);
        var inserted = await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        Console.WriteLine(inserted == 1 ? "产品图片基址已初始化为版本 1。" : "参数已被并发建立，保留现有值。");
    }

    /// <summary>维护工具必须显式核对目标实例，拒绝与实际连接配置不一致的目标。</summary>
    public static void ValidateTarget(string connectionString, string host, string port, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!int.TryParse(port, out var expectedPort) || expectedPort <= 0 || expectedPort > 65535
            || !string.Equals(builder.Host, host, StringComparison.OrdinalIgnoreCase) || builder.Port != expectedPort
            || !string.Equals(builder.Database, database, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("目标主机、端口或库名与连接配置不一致，拒绝初始化。");
    }

    /// <summary>只接受本用例所需参数，缺失、重复及未知参数必须显式失败。</summary>
    private static Dictionary<string, string> ParseArguments(string[] args, out bool dryRun)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        dryRun = false;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--dry-run") { if (dryRun) throw new ArgumentException("重复 --dry-run。"); dryRun = true; continue; }
            if (args[index] is not ("--config" or "--target-host" or "--target-port" or "--target-database")
                || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("用法：--initialize-product-image-parameter --config <服务器JSON> --target-host <主机> --target-port <端口> --target-database <库名> [--dry-run]。");
            options.Add(args[index], args[++index]);
        }
        if (options.Count != 4) throw new ArgumentException("必须明确服务器配置、目标主机、端口和库名。");
        return options;
    }
}
