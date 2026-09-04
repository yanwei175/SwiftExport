using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SwiftExport.Application.Tests.DemandPlanning;

[TestClass]
public sealed class NetDemandLegacyDependencyGuardTests
{
    private static readonly string[] ScopeDirectories =
    [
        "SwiftExport.Contracts/DTOs/DemandPlanning",
        "SwiftExport.Contracts/Apis/DemandPlanning",
        "SwiftExport.Contracts/SysEnums/DemandPlanning",
        "SwiftExport.Application/Interfaces/DemandPlanning",
        "SwiftExport.Application/Services/DemandPlanning",
        "SwiftExport.Core/Entities/DemandPlanning",
        "SwiftExport.Core/Interfaces/DemandPlanning",
        "SwiftExport.Infrastructure/Repositories/DemandPlanning",
        "SwiftExport.WebApi/Controllers/DemandPlanning",
        "SwiftExport.Client.Wpf/FeaturesEn/DemandPlanning"
    ];

    private static readonly (string Name, Regex Pattern)[] ForbiddenDependencies =
    [
        CreateRule("旧 IndependentDemand Entity", @"\bIndependentDemand\b"),
        CreateRule("旧 DependentDemand Entity", @"\bDependentDemand\b"),
        CreateRule(
            "旧需求 DTO namespace",
            @"\bSwiftExport\.Contracts\.DTOs\.(?:Independent|DependentDemands)(?:\.|\b)"),
        CreateRule(
            "旧需求 DTO",
            @"\b(?:IndependentDemand|DependentDemand)[A-Za-z0-9_]*(?:Dto|Request|Response)\b"),
        CreateRule("旧 DependToPurchaseRequest DTO", @"\bDependToPurchaseRequest\b"),
        CreateRule(
            "旧需求 API",
            @"\bI(?:IndependentDemand|DependentDemand)Api\b"),
        CreateRule(
            "旧需求 Application Service",
            @"\bI?(?:IndependentDemand|DependentDemand)Services?\b"),
        CreateRule(
            "旧需求 Repository",
            @"\bI?(?:IndependentDemand|DependentDemand)Repository\b"),
        CreateRule(
            "旧需求 StatusEnum",
            @"\b(?:IndependentDemand|DependentDemand)StatusEnum\b"),
        CreateRule("旧采购服务", @"\bI?(?:PurchaseOrder|FollowUp)Service\b"),
        CreateRule("旧执行服务", @"\bI?(?:Receipt|PackingList|Stock)Service\b"),
        CreateRule("旧采购/执行 API", @"\bI(?:PurchaseOrder|FollowUp|Receive|PackingList)Api\b"),
        CreateRule(
            "旧采购/执行 Repository",
            @"\bI?(?:PurchaseOrder(?:Head|Line)|Receive(?:Head|Line)|Outbound(?:Head|Line)|PackingList(?:Head|Line)|Inventory(?:Transaction)?|Warehouse)Repository\b"),
        CreateRule(
            "旧执行 Entity",
            @"\b(?:FollowUpLog|ReceiveHead|ReceiveLine|ReceiptItem|OutboundHead|OutboundLine|PackingListHead|PackingListLine|Inventory|InventoryTransaction|Warehouse)\b"),
        CreateRule("旧采购状态枚举", @"\bPurchaseOrderStatusEnum\b"),
        CreateRule(
            "旧采购/执行 DTO namespace",
            @"\bSwiftExport\.Contracts\.DTOs\.(?:PurchaseOrders|PackingLists|Warehouses)(?:\.|\b)")
    ];

    private static readonly Regex IgnoredSourceText = new(
        @"<!--.*?-->|/\*.*?\*/|//[^\r\n]*|\$*""{3,}.*?""{3,}|\$?@""(?:""""|[^""])*""|@\$""(?:""""|[^""])*""|\$?""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    [TestMethod]
    public void NetDemandSpecialty_MustNotReferenceLegacyDemandChain()
    {
        var repositoryRoot = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var relativeDirectory in ScopeDirectories)
        {
            var absoluteDirectory = Path.Combine(
                repositoryRoot,
                relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absoluteDirectory))
            {
                continue;
            }

            var sourceFiles = Directory
                .EnumerateFiles(absoluteDirectory, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Where(path => !ContainsBuildOutputDirectory(path));

            foreach (var sourceFile in sourceFiles)
            {
                InspectFile(repositoryRoot, sourceFile, violations);
            }
        }

        Assert.IsEmpty(
            violations,
            "净需求专项代码禁止重新依赖旧 IndependentDemand/DependentDemand 链路："
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static bool ContainsBuildOutputDirectory(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static void InspectFile(string repositoryRoot, string sourceFile, ICollection<string> violations)
    {
        var source = File.ReadAllText(sourceFile);
        var codeOnly = IgnoredSourceText.Replace(source, match => MaskButKeepLineBreaks(match.Value));
        var relativeFile = Path.GetRelativePath(repositoryRoot, sourceFile).Replace('\\', '/');

        foreach (var rule in ForbiddenDependencies)
        {
            foreach (Match match in rule.Pattern.Matches(codeOnly))
            {
                var lineNumber = 1 + codeOnly.AsSpan(0, match.Index).Count('\n');
                violations.Add($"- {relativeFile}:{lineNumber} [{rule.Name}] {match.Value}");
            }
        }
    }

    private static string MaskButKeepLineBreaks(string sourceText)
    {
        var characters = sourceText.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (characters[index] != '\r' && characters[index] != '\n')
            {
                characters[index] = ' ';
            }
        }

        return new string(characters);
    }

    private static string FindRepositoryRoot()
    {
        var startDirectories = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        };

        foreach (var startDirectory in startDirectories)
        {
            for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "SwiftExport.Core")))
                {
                    return directory.FullName;
                }
            }
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录，静态依赖门禁无法执行。");
        return string.Empty;
    }

    private static (string Name, Regex Pattern) CreateRule(string name, string pattern)
        => (name, new Regex(
            pattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant));
}
