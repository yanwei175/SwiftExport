using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Document;

/// <summary>复用Gross来源发现与唯一租约，增加真实净需求和全局命令项的身份发现；全部资源在父事务首次快照之前保护。</summary>
public sealed partial class PostgreSqlGrossDemandDocumentRepository
{
    private static readonly EntityMetadata NetMap = EntityMetadataCache.GetMetadata(typeof(NetDemand));
    private static readonly EntityMetadata LinkMap = EntityMetadataCache.GetMetadata(typeof(DemandExecutionLink));

    /// <summary>先发现全部历史绑定，获取CommandItem→S→U后重验；身份或成员变化时整组释放重准备，共用30秒预算，不重试业务委托。</summary>
    public async Task<IAsyncDisposable?> AcquireMaterialBeforeTransactionAsync(IReadOnlyCollection<Guid> sourceUids,
        IReadOnlyCollection<Guid> grossDemandIds, IReadOnlyCollection<Guid> netDemandIds,
        IReadOnlyCollection<Guid> commandItemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceUids); ArgumentNullException.ThrowIfNull(grossDemandIds);
        ArgumentNullException.ThrowIfNull(netDemandIds); ArgumentNullException.ThrowIfNull(commandItemIds);
        if (context.Session.Transaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("物料准备必须先于父事务及首次快照。");
        var declaredSources = sourceUids.Distinct().ToArray(); var declaredGross = grossDemandIds.Distinct().ToArray();
        var declaredNet = netDemandIds.Distinct().ToArray(); var items = commandItemIds.Distinct().ToArray();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                budget.Token.ThrowIfCancellationRequested();
                var beforeItems = await ItemBindings(items, budget.Token);
                var nets = declaredNet.Concat(beforeItems.Select(x => x.NetDemandId)).Distinct().ToArray();
                var beforeNet = await NetBindings(nets, budget.Token);
                var gross = declaredGross.Concat(beforeNet.Select(x => x.GrossDemandId)).Distinct().ToArray();
                var beforeGross = await Bindings(gross, false, budget.Token);
                var sources = declaredSources.Concat(beforeGross.Select(x => x.SourceUid)).Distinct().ToArray();
                var members = await Bindings(sources, true, budget.Token);
                var uids = gross.Concat(members.Select(x => x.Uid)).Distinct().ToArray();
                var lease = await PostgreSqlSourceDemandLock.AcquireMaterialBeforeTransactionAsync(context.Session,
                    items, sources, uids, context.LoggerFactory.CreateLogger("PostgreSqlMaterialDemandPreparation"), budget.Token);
                if (lease is null) return null;
                try
                {
                    var afterItems = await ItemBindings(items, budget.Token);
                    var afterNet = await NetBindings(nets, budget.Token);
                    var afterGross = await Bindings(gross, false, budget.Token);
                    var currentMembers = await Bindings(sources, true, budget.Token);
                    var held = (PostgreSqlSourceDemandLock.Lease)lease;
                    // 取消不释放命令项身份：发现不筛软删、状态或TargetType，资格仍由原业务事务内判定。
                    if (SameItems(beforeItems, afterItems) && SameNets(beforeNet, afterNet)
                        && SameBindings(beforeGross, afterGross) && currentMembers.All(x => held.Covers("Gross", x.Uid)))
                        return lease;
                }
                catch { await lease.DisposeAsync(); throw; }
                // 禁止持U后补C_item或S；未知/改绑只全释放重准备，首次快照和真实号码仍未产生。
                await lease.DisposeAsync();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        { return null; }
    }

    /// <summary>按已知净需求身份发现Gross归属，含全部修订、生命周期与软删；空输入免SQL，不在准备阶段判断活动资格。</summary>
    private async Task<IReadOnlyList<NetBinding>> NetBindings(Guid[] ids, CancellationToken token)
    {
        if (ids.Length == 0) return [];
        var rows = await context.Session.Connection.QueryAsync<NetBinding>(new CommandDefinition(
            $"SELECT {MappedColumn(NetMap, "Uid")} AS \"Uid\",{MappedColumn(NetMap, "GrossDemandId")} AS \"GrossDemandId\" FROM {MappedTable(NetMap)} WHERE {MappedColumn(NetMap, "Uid")}=ANY(@Uids);",
            PostgreSqlParameters.Ids(ids), cancellationToken: token));
        return rows.ToList();
    }

    /// <summary>全局命令项只读取不可变关系身份，不过滤Empty、取消或反向；唯一约束仍是最终写入防线。</summary>
    private async Task<IReadOnlyList<ItemBinding>> ItemBindings(Guid[] ids, CancellationToken token)
    {
        if (ids.Length == 0) return [];
        var rows = await context.Session.Connection.QueryAsync<ItemBinding>(new CommandDefinition(
            $"SELECT {MappedColumn(LinkMap, "Uid")} AS \"Uid\",{MappedColumn(LinkMap, "CommandItemId")} AS \"CommandItemId\",{MappedColumn(LinkMap, "NetDemandId")} AS \"NetDemandId\" FROM {MappedTable(LinkMap)} WHERE {MappedColumn(LinkMap, "CommandItemId")}=ANY(@Uids);",
            PostgreSqlParameters.Ids(ids), cancellationToken: token));
        return rows.ToList();
    }

    /// <summary>净需求缺失、创建或改绑均要求重新发现；不以生命周期变更扩大技术身份集合。</summary>
    private static bool SameNets(IReadOnlyList<NetBinding> before, IReadOnlyList<NetBinding> after)
        => before.Count == after.Count && before.All(x => after.Any(y => x.Uid == y.Uid && x.GrossDemandId == y.GrossDemandId));
    /// <summary>命令项出现或关系身份变化均重新准备，不在此作重放或参数一致性裁决。</summary>
    private static bool SameItems(IReadOnlyList<ItemBinding> before, IReadOnlyList<ItemBinding> after)
        => before.Count == after.Count && before.All(x => after.Any(y => x.Uid == y.Uid && x.CommandItemId == y.CommandItemId && x.NetDemandId == y.NetDemandId));
    /// <summary>固定元数据决定物理表，禁止外部标识符。</summary>
    private static string MappedTable(EntityMetadata map) => PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);
    /// <summary>固定CLR属性按现有PG映射取列，不复制实体命名规则。</summary>
    private static string MappedColumn(EntityMetadata map, string property)
        => PostgreSqlNames.Quote(map.Columns.Single(x => x.PropertyName == property).ColumnName);
    /// <summary>技术发现净需求归属的私有投影，不新增业务实体、DTO或数据库结构。</summary>
    private sealed class NetBinding { public Guid Uid { get; set; } public Guid GrossDemandId { get; set; } }
    /// <summary>技术发现全局命令项关系的私有投影，不用于业务重放结果。</summary>
    private sealed class ItemBinding { public Guid Uid { get; set; } public Guid CommandItemId { get; set; } public Guid NetDemandId { get; set; } }
}
