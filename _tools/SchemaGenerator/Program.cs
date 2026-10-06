using System.Text;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.DatabaseSchema;

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
// 只生成当前PG目标的本地评审脚本；旧SQL夹具及基线已退出，不生成其他提供者脚本。
if (args.Any(argument => argument != "--verify"))
    throw new ArgumentException("仅支持 --verify；结构生成工具只面向 PostgreSQL。");
var outputPath = Path.Combine(repositoryRoot, "SwiftExport.Infrastructure", "Database", "Generated",
    "SwiftExport.PostgreSql.Schema.preview.sql");
var generated = new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly);
var verify = args.Contains("--verify");

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

/// <summary>沿工具输出目录寻找仓库根；只定位本地生成路径，不连接或部署数据库。</summary>
static string FindRepositoryRoot(string startPath)
{
    for (var directory = new DirectoryInfo(startPath); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "SwiftExport.slnx")))
            return directory.FullName;
    }
    throw new InvalidOperationException("无法定位 SwiftExport 仓库根目录。");
}
