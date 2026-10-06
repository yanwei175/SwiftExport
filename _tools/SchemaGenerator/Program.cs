using System.Text;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.DatabaseSchema;

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
// 默认生成当前PG目标的本地评审脚本；迁移完成前可显式核对旧SQL基线，工具从不连接或部署数据库。
var provider = "postgresql";
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--verify") continue;
    if (args[index] != "--provider" || index + 1 >= args.Length)
        throw new ArgumentException("仅支持 --verify 和 --provider sqlserver|postgresql。");
    provider = args[++index].ToLowerInvariant();
    if (provider is not "sqlserver" and not "postgresql")
        throw new ArgumentException("未知数据库提供者。");
}
var outputPath = Path.Combine(repositoryRoot, "SwiftExport.Infrastructure", "Database", "Generated",
    provider == "postgresql" ? "SwiftExport.PostgreSql.Schema.preview.sql" : "SwiftExport.Schema.sql");
var generated = provider == "postgresql"
    ? new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly)
    : new SqlServerSchemaGenerator().Generate(typeof(BaseEntity).Assembly);
var verify = args.Any(argument => string.Equals(argument, "--verify", StringComparison.OrdinalIgnoreCase));

if (verify)
{
    if (!File.Exists(outputPath))
        throw new InvalidOperationException($"生成文件不存在：{outputPath}");
    var current = (await File.ReadAllTextAsync(outputPath, Encoding.UTF8))
        .Replace("\r\n", "\n", StringComparison.Ordinal);
    if (!string.Equals(current, generated, StringComparison.Ordinal))
        throw new InvalidOperationException("实体元数据与已提交数据库基线不一致，请重新运行 SchemaGenerator。");
    Console.WriteLine("数据库基线与实体元数据一致。");
    return;
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(outputPath, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine($"已生成数据库基线：{outputPath}");

static string FindRepositoryRoot(string startPath)
{
    for (var directory = new DirectoryInfo(startPath); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "SwiftExport.slnx")))
            return directory.FullName;
    }
    throw new InvalidOperationException("无法定位 SwiftExport 仓库根目录。");
}
