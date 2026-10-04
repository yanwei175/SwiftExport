using System.Text.Json;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Models.Products;

namespace SwiftExport.Application.Tests.Products;

/// <summary>为离线保存测试提供共享事实；默认仅模拟 ASCII 编号及尾空格，不宣称验证数据库 Unicode 等值。</summary>
internal static class SharedBomTestFacts
{
    /// <summary>保留全图、异常未删组成及空请求组；读取对象均克隆，显式编号选择器只用于指定数据库匹配夹具。</summary>
    public static IReadOnlyList<SharedBomGroup> Build(IEnumerable<Product> products, IEnumerable<Bom> lines,
        IReadOnlyList<string> requested, Func<Bom, string>? lineNumber = null)
    {
        var parents = products.Where(x => !x.IsDeleted && x.BomVersion > 0 && !string.IsNullOrWhiteSpace(x.BomNo)).ToArray();
        var rows = lines.Where(x => !x.IsDeleted).ToArray();
        string RowNo(Bom row) => (lineNumber?.Invoke(row) ?? row.BomNo).TrimEnd(' ');
        var numbers = parents.Select(x => x.BomNo!.TrimEnd(' ')).Concat(rows.Select(RowNo))
            .Concat(requested.Select(x => x.TrimEnd(' '))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return numbers.Select((number, index) => new SharedBomGroup
            {
                GroupKey = index + 1L, BomNo = number,
                InputIndexes = Enumerable.Range(0, requested.Count).Where(i => Same(number, requested[i])).ToArray(),
                Products = parents.Where(x => Same(number, x.BomNo!)).Select(Clone).ToArray(),
                Lines = rows.Where(x => Same(number, RowNo(x))).Select(Clone).ToArray()
            }).ToArray();
    }

    /// <summary>模拟测试限定编号比较，保留前导空格，不推断任意 Unicode 数据库等价。</summary>
    private static bool Same(string left, string right) => string.Equals(left.TrimEnd(' '), right.TrimEnd(' '), StringComparison.OrdinalIgnoreCase);

    /// <summary>复制全部持久字段，防止准备阶段通过读取对象隐式改写集合。</summary>
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
