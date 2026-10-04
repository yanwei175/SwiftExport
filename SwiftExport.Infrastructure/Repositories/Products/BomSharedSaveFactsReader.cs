using System.Data;
using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Models.Products;

namespace SwiftExport.Infrastructure.Repositories.Products;

/// <summary>仅汇总两提供者单语句返回的共享 BOM 事实，不比较编号、不执行业务校验或控制事务。</summary>
internal static class BomSharedSaveFactsReader
{
    /// <summary>按数据库本次组键汇总三类事实，输入空组和没有组成的共享产品仍保留；组键不可作为持久身份。</summary>
    internal static async Task<IReadOnlyList<SharedBomGroup>> ReadAsync(IDbConnection connection, IDbTransaction transaction,
        string sql, object parameters)
    {
        var groups = new Dictionary<long, GroupRows>();
        await connection.QueryAsync<FactHeader, Bom, Product, int>(sql, (header, line, product) =>
        {
            if (!groups.TryGetValue(header.GroupKey, out var group))
            {
                group = new GroupRows(header.BomNo);
                groups.Add(header.GroupKey, group);
            }
            // Kind 与首列 NULL 映射共同区分空实体；不按编号重新合并数据库等值组。
            if (header.Kind == 0 && line is not null) group.Lines.Add(line);
            if (header.Kind == 1 && product is not null) group.Products.Add(product);
            if (header.Kind == 2 && header.InputIndex.HasValue) group.InputIndexes.Add(header.InputIndex.Value);
            return 0;
        }, parameters, transaction, splitOn: "Uid,Uid");
        return groups.OrderBy(pair => pair.Key).Select(pair => new SharedBomGroup
            {
                GroupKey = pair.Key, BomNo = pair.Value.BomNo,
                InputIndexes = pair.Value.InputIndexes.OrderBy(index => index).ToArray(),
                Lines = pair.Value.Lines.OrderBy(line => line.Uid).ToArray(),
                Products = pair.Value.Products.OrderBy(product => product.Uid).ToArray()
            }).ToArray();
    }

    /// <summary>数据库分组头，与两个实体的 Uid 分割列分离；编号仅作原始展示文本。</summary>
    private sealed class FactHeader
    {
        public long GroupKey { get; set; }
        public string BomNo { get; set; } = null!;
        public int Kind { get; set; }
        public int? InputIndex { get; set; }
    }

    /// <summary>单次读取的模块内临时容器，三种事实分别保存，不把物理行复制成产品边。</summary>
    private sealed class GroupRows(string bomNo)
    {
        public string BomNo { get; } = bomNo;
        public List<int> InputIndexes { get; } = [];
        public List<Bom> Lines { get; } = [];
        public List<Product> Products { get; } = [];
    }
}
