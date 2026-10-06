using Dapper;
using Mapster;
using SwiftExport.Application.Interfaces.ForExcel;
using SwiftExport.Application.Interfaces.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Maintenance;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Confirmation;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth.Accounts;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using SwiftExport.Core.Interfaces.Systems.Currencies;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Auth.Development;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.WebApi.Configuration;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Application.Interfaces.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Infrastructure.Repositories.Commands;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Interfaces.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;
using System.Net;
using System.Net.Http.Json;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Application.Interfaces.SalesOrders.Draft;
using SwiftExport.Application.Interfaces.SalesOrders.Confirmation;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Establishment;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Reversal;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证宿主完整PG注册、实际命令链与明确门禁的本地HTTP登录；不初始化结构，成功事实登记保留并支持原输入重放。</summary>
[TestClass, DoNotParallelize, TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlRuntimeWiringTests
{
    /// <summary>真实PG读取配置主记录后让另一连接提交字段变更，验证RR完整旧快照及同主版本竞争；仅保留本批新配置。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeExcelProfileSnapshotAndVersionCompetition()
    {
        const string batch = "02e119c9-bec4-4b8f-a9c3-9dd718b5e184", group = "PGM0502E119";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_EXCEL_PROFILE_BATCH"), "仅本批首次，不重置成功配置。");
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.excel_read_profile WHERE group_code=@Group", new { Group = group }));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("SELECT uid AS \"UserId\",user_name AS \"UserName\" FROM public.sys_user WHERE user_name='dv' AND is_active AND NOT is_deleted");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        // 透明屏障只控制测试读取时序，真实仓储/会话、全部返回值与隔离级别均不替换。
        builder.Services.AddScoped<SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles.IExcelReadProfileRepository>(sp =>
        {
            var proxy = System.Reflection.DispatchProxy.Create<SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles.IExcelReadProfileRepository, RuntimeExcelProfileReadProbe>();
            ((RuntimeExcelProfileReadProbe)proxy).Target = new SwiftExport.Infrastructure.Repositories.Systems.ExcelReadProfiles.PostgreSqlExcelReadProfileRepository(sp.GetRequiredService<RepositoryBaseContext>());
            return proxy;
        });
        await using var app = builder.Build();
        using var writeScope = app.Services.CreateScope(); using var readScope = app.Services.CreateScope();
        var writer = writeScope.ServiceProvider; var reader = readScope.ServiceProvider;
        var writeSession = writer.GetRequiredService<DbSession>(); var readSession = reader.GetRequiredService<DbSession>();
        var writePid = ((NpgsqlConnection)writeSession.Connection).ProcessID; var readPid = ((NpgsqlConnection)readSession.Connection).ProcessID;
        var holderPid = ((NpgsqlConnection)observer.Connection).ProcessID;
        Assert.AreNotEqual(writePid, readPid); Assert.AreNotEqual(writePid, holderPid); Assert.AreNotEqual(readPid, holderPid);
        var writeService = writer.GetRequiredService<SwiftExport.Application.Interfaces.Systems.ExcelReadProfiles.IExcelReadProfileService>();
        var readService = reader.GetRequiredService<SwiftExport.Application.Interfaces.Systems.ExcelReadProfiles.IExcelReadProfileService>();
        var probe = (RuntimeExcelProfileReadProbe)reader.GetRequiredService<SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles.IExcelReadProfileRepository>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<ServiceResult<SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileDto>>? pendingRead = null, firstSave = null, secondSave = null;
        Guid profileId = Guid.Empty; string? blocking = null;
        try
        {
            var created = await writeService.CreateAsync(new SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.CreateExcelReadProfileRequest
                { GroupCode = group, ConfigName = "PGM05_02e119_Old", Remark = batch }, deadline.Token);
            Assert.IsTrue(created.Success, created.Message); profileId = created.Data.Uid;
            Assert.AreEqual(1, created.Data.Version); Assert.AreEqual("dv", created.Data.CreatedBy);
            var duplicate = await writeService.CreateAsync(new SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.CreateExcelReadProfileRequest
                { GroupCode = group, ConfigName = "PGM05_02e119_Old", Remark = batch }, deadline.Token);
            Assert.AreEqual("EXCEL_READ_DUPLICATE_NAME", duplicate.Error.Code); Assert.IsNull(writeSession.Transaction);
            var initial = await writeService.UpdateAsync(profileId, Request(1, "Old", "A", "A8", "D8", "Line.Quantity"), deadline.Token);
            Assert.IsTrue(initial.Success, initial.Message); Assert.AreEqual(2, initial.Data.Version);
            var old = await readService.GetAsync(profileId, deadline.Token); Assert.IsTrue(old.Success, old.Message);
            var oldJson = Json(old.Data);
            var skuUid = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.excel_read_field WHERE profile_uid=@Uid AND target_field_key='Line.Sku' AND NOT is_deleted", new { Uid = profileId });
            var quantityUid = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.excel_read_field WHERE profile_uid=@Uid AND target_field_key='Line.Quantity' AND NOT is_deleted", new { Uid = profileId });

            probe.ProfileId = profileId; probe.Enabled = true;
            pendingRead = readService.GetAsync(profileId, deadline.Token);
            await probe.Reached.Task.WaitAsync(deadline.Token);
            Assert.AreEqual(profileId, probe.ObservedId); Assert.AreEqual(2, probe.ObservedVersion);
            Assert.AreEqual(System.Data.IsolationLevel.RepeatableRead, readSession.Transaction!.IsolationLevel);
            var snapshotIsolation = await readSession.Connection.ExecuteScalarAsync<string>("SHOW transaction_isolation", transaction: readSession.Transaction);
            Assert.AreEqual("repeatable read", snapshotIsolation); Assert.IsFalse(pendingRead.IsCompleted);
            // PG的RR不阻塞这个更新；旧读取在提交后继续获取字段时仍必须属于旧主版本。
            var changed = await writeService.UpdateAsync(profileId, Request(2, "New", "B", "B8", "E8", "Line.Weight"), deadline.Token);
            Assert.IsTrue(changed.Success, changed.Message); Assert.AreEqual(3, changed.Data.Version);
            Assert.IsFalse(pendingRead.IsCompleted);
            probe.Release.TrySetResult();
            var frozen = await pendingRead; Assert.IsTrue(frozen.Success, frozen.Message);
            Assert.AreEqual(oldJson, Json(frozen.Data), "主版本、全部字段及审计/完整性投影必须属于同一个真实旧快照。");
            Assert.IsNull(readSession.Transaction);
            var latest = await readService.GetAsync(profileId, deadline.Token);
            Assert.IsTrue(latest.Success, latest.Message); Assert.AreEqual(3, latest.Data.Version); Assert.AreEqual("PGM05_02e119_New", latest.Data.ConfigName);
            CollectionAssert.AreEquivalent(new[] { "Line.Sku", "Line.Weight" }, latest.Data.Fields.Select(f => f.TargetFieldKey).ToArray());
            Assert.AreEqual("B8", latest.Data.Fields.Single(f => f.TargetFieldKey == "Line.Sku").CellAddress);
            Assert.AreEqual(skuUid, await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.excel_read_field WHERE profile_uid=@Uid AND target_field_key='Line.Sku' AND NOT is_deleted", new { Uid = profileId }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT is_deleted FROM public.excel_read_field WHERE uid=@Uid", new { Uid = quantityUid }));
            var beforeStale = await Snapshot();
            var stale = await readService.UpdateAsync(profileId, Request(2, "Stale", "C", "C8", "F8", "Line.Weight"), deadline.Token);
            Assert.AreEqual(ServiceErrorCodes.VersionConflict, stale.Error.Code); Assert.AreEqual(beforeStale, await Snapshot());

            // 观察连接只锁本批新头，使两个原Update同时等待真实行锁；释放后只有一方能消费主版本3。
            observer.Transaction = observer.Connection.BeginTransaction(System.Data.IsolationLevel.ReadCommitted);
            await observer.Connection.ExecuteAsync("SELECT uid FROM public.excel_read_profile WHERE uid=@Uid FOR UPDATE", new { Uid = profileId }, observer.Transaction);
            firstSave = writeService.UpdateAsync(profileId, Request(3, "RaceA", "D", "D8", "F8", "Line.Weight"), deadline.Token);
            secondSave = readService.UpdateAsync(profileId, Request(3, "RaceB", "E", "E8", "G8", "Line.Weight"), deadline.Token);
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                await observer.Connection.ExecuteAsync("SELECT pg_stat_clear_snapshot()", transaction: observer.Transaction);
                var waits = await observer.Connection.ExecuteScalarAsync<int>("""
                    SELECT count(*) FROM pg_stat_activity a WHERE a.pid=ANY(@Pids) AND a.state='active' AND a.wait_event_type='Lock'
                      AND (@Owner=ANY(pg_blocking_pids(a.pid)) OR EXISTS
                        (SELECT 1 FROM unnest(pg_blocking_pids(a.pid)) b WHERE b=ANY(@Pids)))
                    """, new { Pids = new[] { writePid, readPid }, Owner = holderPid }, observer.Transaction);
                if (waits == 2) break;
                Assert.IsFalse(firstSave.IsCompleted || secondSave.IsCompleted, "未形成真实服务器双等待，保存提前结束。");
                await Task.Delay(25, deadline.Token);
            }
            blocking = await observer.Connection.ExecuteScalarAsync<string>("SELECT jsonb_agg(jsonb_build_object('pid',pid,'blocking',pg_blocking_pids(pid)) ORDER BY pid)::text FROM pg_stat_activity WHERE pid=ANY(@Pids)",
                new { Pids = new[] { writePid, readPid } }, observer.Transaction);
            observer.Transaction.Rollback(); observer.Transaction.Dispose(); observer.Transaction = null;
            var outcomes = await Task.WhenAll(firstSave, secondSave);
            Assert.AreEqual(1, outcomes.Count(r => r.Success));
            Assert.AreEqual(ServiceErrorCodes.VersionConflict, outcomes.Single(r => !r.Success).Error.Code);
            var winner = outcomes.Single(r => r.Success).Data;
            var final = await readService.GetAsync(profileId, deadline.Token);
            Assert.IsTrue(final.Success, final.Message); Assert.AreEqual(4, final.Data.Version);
            Assert.AreEqual(winner.ConfigName, final.Data.ConfigName); Assert.AreEqual(winner.LastRowColumn, final.Data.LastRowColumn);
            Assert.AreEqual(Json(winner.Fields), Json(final.Data.Fields));
            Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.excel_read_field WHERE profile_uid=@Uid", new { Uid = profileId }));
            Assert.IsNull(writeSession.Transaction); Assert.IsNull(readSession.Transaction);
            Assert.IsNull(writeSession.CommandLease); Assert.IsNull(readSession.CommandLease);
        }
        finally
        {
            // 任何断言失败都释放屏障和观察行锁，再观察/取消在途任务，避免遗留占用的真实连接。
            probe.Release.TrySetResult(); deadline.Cancel();
            if (observer.Transaction is not null) { observer.Transaction.Rollback(); observer.Transaction.Dispose(); observer.Transaction = null; }
            foreach (var task in new[] { pendingRead, firstSave, secondSave })
                if (task is not null) { try { await task; } catch (Exception) { /* 主路径失败保留，清理仅观察结束。 */ } }
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, Group = group, ProfileId = profileId,
                WritePid = writePid, ReadPid = readPid, HolderPid = holderPid, Blocking = blocking,
                Policy = "仅failure新1配置3字段留存；真实RR完整旧快照、字段归属与主版本双竞争，旧资料/隔离/结构不改" }));
        }

        // 查询只冻结本批头和全部字段JSON；请求是原合法完整快照，未增加配置业务选项。
        async Task<string> Snapshot() => (await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(p)::text FROM public.excel_read_profile p WHERE uid=@Uid", new { Uid = profileId }))
            + (await observer.Connection.ExecuteScalarAsync<string>("SELECT jsonb_agg(to_jsonb(f) ORDER BY uid)::text FROM public.excel_read_field f WHERE profile_uid=@Uid", new { Uid = profileId }));
        static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value);
        static SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.UpdateExcelReadProfileRequest Request(int version, string name, string column, string sku, string other, string key) => new()
        { ExpectedVersion = version, ConfigName = "PGM05_02e119_" + name, Remark = batch, EndMode = 1, LastRowColumn = column,
            Fields = [new() { TargetFieldKey = "Line.Sku", WorksheetName = name, CellAddress = sku, IsDownward = true },
                new() { TargetFieldKey = key, WorksheetName = null!, CellAddress = other, IsDownward = true }] };
    }

    /// <summary>只在本批新配置的真实主记录读取后暂停一次；所有方法仍由原PG仓储负责。</summary>
    public class RuntimeExcelProfileReadProbe : System.Reflection.DispatchProxy
    {
        internal SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles.IExcelReadProfileRepository Target = null!;
        internal Guid ProfileId, ObservedId;
        internal int ObservedVersion;
        internal bool Enabled;
        internal readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>转发真实调用并保留异常原类型；写入/字段读取及其他配置不设置屏障。</summary>
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                var result = targetMethod!.Invoke(Target, args);
                return Enabled && targetMethod.Name == nameof(Target.FindAsync) && args![0] is Guid uid && uid == ProfileId && args[1] is false
                    ? PauseAsync((Task<SwiftExport.Core.Entities.Systems.ExcelReadProfiles.ExcelReadProfile?>)result!, (CancellationToken)args[2]!) : result;
            }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        /// <summary>读取已发生且原事务仍打开时通知观察方；释放只改变测试时序，不改变快照或返回主记录。</summary>
        private async Task<SwiftExport.Core.Entities.Systems.ExcelReadProfiles.ExcelReadProfile?> PauseAsync(
            Task<SwiftExport.Core.Entities.Systems.ExcelReadProfiles.ExcelReadProfile?> read, CancellationToken token)
        {
            var head = await read;
            Enabled = false; ObservedId = head?.Uid ?? Guid.Empty; ObservedVersion = head?.Version ?? 0;
            Reached.TrySetResult(); await Release.Task.WaitAsync(token);
            return head;
        }
    }


     /// <summary>原字典类型/明细服务通过真实PG保存父子归属并验证中途版本冲突整批回滚；仅保留本批新资料，不写既有字典。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeDictionaryBatchParentAndAtomicRollback()
    {
        const string batch = "6d14f49f-1536-4081-8103-be4dd2566b3c";
        const string parentCode = "PGM056D14P", childCode = "PGM056D14C", failedCode = "PGM056D14X";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_DICTIONARY_BATCH"), "仅本批首次，成功资料不重置重跑。");
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_dict_type WHERE dict_code=ANY(@Codes)",
            new { Codes = new[] { parentCode, childCode, failedCode } }));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("SELECT uid AS \"UserId\",user_name AS \"UserName\" FROM public.sys_user WHERE user_name='dv' AND is_active AND NOT is_deleted");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        using var writeScope = app.Services.CreateScope(); using var readScope = app.Services.CreateScope();
        var writer = writeScope.ServiceProvider; var reader = readScope.ServiceProvider;
        var writeSession = writer.GetRequiredService<DbSession>(); var readSession = reader.GetRequiredService<DbSession>();
        var writePid = ((NpgsqlConnection)writeSession.Connection).ProcessID; var readPid = ((NpgsqlConnection)readSession.Connection).ProcessID;
        Assert.AreNotEqual(writePid, readPid);
        var typeService = writer.GetRequiredService<SwiftExport.Application.Interfaces.Systems.Dictionaries.Types.ISysDictTypeService>();
        var dataService = writer.GetRequiredService<SwiftExport.Application.Interfaces.Systems.Dictionaries.Data.ISysDictDataService>();
        var types = reader.GetRequiredService<SwiftExport.Core.Interfaces.Systems.Dictionaries.Types.ISysDictTypeRepository>();
        var data = reader.GetRequiredService<SwiftExport.Core.Interfaces.Systems.Dictionaries.Data.ISysDictDataRepository>();
        Guid parentId = Guid.Empty, childId = Guid.Empty, dataId = Guid.Empty;
        try
        {
            var logicalParent = Guid.Parse("6d14f49f-1536-4081-8103-be4dd2566b3d");
            var logicalChild = Guid.Parse("6d14f49f-1536-4081-8103-be4dd2566b3e");
            // 子项故意在前，原服务负责排序并将逻辑父标识换成仓储生成的实际标识。
            var created = await typeService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDictTypeBatchSaveRequest
            { CreateItems = [
                new CreateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDictTypeDto> { Data = Type(childCode, logicalChild, logicalParent) },
                new CreateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDictTypeDto> { Data = Type(parentCode, logicalParent, null) }] });
            Assert.IsTrue(created.Success, created.Error?.Code + ": " + created.Message);
            Assert.AreEqual(2, created.Data.SuccessCount);
            var currentTypes = await types.GetByDictCodesAsync([parentCode, childCode]);
            var parent = currentTypes.Single(t => t.DictCode == parentCode); var child = currentTypes.Single(t => t.DictCode == childCode);
            parentId = parent.Uid; childId = child.Uid;
            Assert.AreNotEqual(logicalParent, parentId); Assert.AreNotEqual(logicalChild, childId);
            Assert.AreEqual(parentId, child.ParentId); Assert.IsFalse(parent.IsSystem || child.IsSystem);
            Assert.AreEqual("dv", parent.CreatedBy); Assert.AreEqual(1, child.Version);

            var itemCreated = await dataService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDictDataBatchSaveRequest
            { CreateItems = [new CreateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDictDataDto> { Data = Item("PGM05-6d14-Original") }] });
            Assert.IsTrue(itemCreated.Success, itemCreated.Error?.Code + ": " + itemCreated.Message);
            var item = (await data.GetByCodeAsync(childCode)).Single(); dataId = item.Uid;
            Assert.AreEqual(childCode, item.DictCode); Assert.IsFalse(item.IsSystem || item.IsEnabled); Assert.AreEqual(1, item.Version);
            var itemUpdated = await dataService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDictDataBatchSaveRequest
            { UpdateItems = [new UpdateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDictDataDto>
                { Uid = dataId, Version = 1, Data = Item("PGM05-6d14-Updated") }] });
            Assert.IsTrue(itemUpdated.Success, itemUpdated.Error?.Code + ": " + itemUpdated.Message);
            item = (await data.GetByCodeAsync(childCode)).Single();
            Assert.AreEqual(2, item.Version); Assert.AreEqual("PGM05-6d14-Updated", item.DataNameEn);
            var itemBefore = await Snapshot("sys_dict_data", dataId);
            var parentBefore = await Snapshot("sys_dict_type", parentId);
            var childBefore = await Snapshot("sys_dict_type", childId);

            // 新增在更新前实际走原服务写入；后一个旧版本更新失败时不允许留下前一个新增。
            var stale = await dataService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDictDataBatchSaveRequest
            { CreateItems = [new CreateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDictDataDto> { Data = Item("PGM05-6d14-Rollback") }],
                UpdateItems = [new UpdateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDictDataDto>
                    { Uid = dataId, Version = 1, Data = Item("PGM05-6d14-Stale") }] });
            Assert.IsFalse(stale.Success); StringAssert.Contains(stale.Message, "所有操作已回滚");
            Assert.AreEqual(1, (await data.GetByCodeAsync(childCode)).Count);
            Assert.AreEqual(itemBefore, await Snapshot("sys_dict_data", dataId));
            var page = await data.GetPagedAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDictDataSearchRequest
                { Keyword = "PGM05-6d14", BatchKeywords = [childCode.ToLowerInvariant()], PageIndex = 1, PageSize = 20 });
            Assert.AreEqual(1, page.TotalCount); Assert.AreEqual(dataId, page.Items.Single().Uid);
            // 停用的有效明细仍阻止类型删除，这是原业务边界，不能为PG绕过。
            var referenced = await typeService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDictTypeBatchSaveRequest
            { DeleteItems = [new DeleteItemRequest { Uid = childId, Version = 1, IsSoftDelete = true }] });
            Assert.IsFalse(referenced.Success); StringAssert.Contains(referenced.Message, "仍有字典明细");
            Assert.AreEqual(parentBefore, await Snapshot("sys_dict_type", parentId));
            Assert.AreEqual(childBefore, await Snapshot("sys_dict_type", childId));

            var parentUpdated = await typeService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDictTypeBatchSaveRequest
            { UpdateItems = [new UpdateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDictTypeDto>
                { Uid = parentId, Version = 1, Data = Type(parentCode, parentId, null, "父项新版") }] });
            Assert.IsTrue(parentUpdated.Success, parentUpdated.Error?.Code + ": " + parentUpdated.Message);
            Assert.AreEqual(2, (await types.GetByDictCodesAsync([parentCode])).Single().Version);
            parentBefore = await Snapshot("sys_dict_type", parentId);
            var staleType = await typeService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDictTypeBatchSaveRequest
            { CreateItems = [new CreateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDictTypeDto> { Data = Type(failedCode, Guid.NewGuid(), null) }],
                UpdateItems = [new UpdateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDictTypeDto>
                    { Uid = parentId, Version = 1, Data = Type(parentCode, parentId, null, "过期修改") }] });
            Assert.IsFalse(staleType.Success); StringAssert.Contains(staleType.Message, "所有操作已回滚");
            Assert.HasCount(0, await types.GetByDictCodesAsync([failedCode]));
            Assert.AreEqual(parentBefore, await Snapshot("sys_dict_type", parentId));
            Assert.AreEqual(childBefore, await Snapshot("sys_dict_type", childId));
            Assert.AreEqual(itemBefore, await Snapshot("sys_dict_data", dataId));
            Assert.IsNull(writeSession.Transaction); Assert.IsNull(readSession.Transaction);
            Assert.IsNull(writeSession.CommandLease); Assert.IsNull(writeSession.SourceDemandLease);
        }
        finally
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, ParentId = parentId, ChildId = childId, DataId = dataId,
                WritePid = writePid, ReadPid = readPid, Codes = new[] { parentCode, childCode, failedCode },
                Policy = "原服务新2类型1明细留存，父子映射/原停用引用拒绝/两个中途版本冲突整批回滚；不改旧资料或结构" }));
        }

        // DTO遵循原校验；观察连接只按本批真实UID读取完整行，不混用服务写事务。
        Task<string?> Snapshot(string table, Guid uid) => observer.Connection.ExecuteScalarAsync<string>($"SELECT to_jsonb(t)::text FROM public.{table} t WHERE uid=@Uid", new { Uid = uid });
        SwiftExport.Contracts.DTOs.SystemDtos.SysDictTypeDto Type(string code, Guid uid, Guid? parent, string name = "父子测试") => new()
        { Uid = uid, DictCode = code, DictName = name, ParentId = parent, GroupCode = "PGM05", GroupName = "迁移验证", Remark = batch };
        SwiftExport.Contracts.DTOs.SystemDtos.SysDictDataDto Item(string name) => new()
        { DictCode = childCode, DataNameEn = name, DataNameZh = "PG字典6d14验证", IsEnabled = false, Remark = batch };
    }



    /// <summary>实际dv开发HTTP保存销售草稿，在原头写入和行号暂存后使PG事务失败，验证完整回滚及同输入恢复；仅保留本批新事实。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeSalesHttpStageFailureRollsBackAndRecovers()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        const string batch = "a0c0d521-48fc-48c2-bebf-da738188f23a";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_SALES_HTTP_BATCH"), "仅首次登记批次，不重置成功事实。");
        var createId = Guid.Parse(batch);
        var updateId = Guid.Parse("a0c0d521-48fc-48c2-bebf-da738188f23b");
        var lineIds = new[] { Guid.Parse("a0c0d521-48fc-48c2-bebf-da738188f23d"), Guid.Parse("a0c0d521-48fc-48c2-bebf-da738188f23e") };
        var customerId = Guid.Parse("1310ce86-31f8-4c65-8efa-6205300427fd");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var resuming = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_SALES_HTTP_RESUME_HEAD") == "78db0c78-2af7-4606-9713-4a3504451697";
        Assert.AreEqual(resuming ? 1 : 0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id AND actor_uid='825d7e51-f659-4c81-9ea9-4a2d03015360' AND command_type='Orders.SalesOrder.CreateDraft'", new { Id = createId }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = updateId }));
        Assert.AreEqual(resuming ? 2 : 0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sales_order_line WHERE uid=ANY(@Ids)", new { Ids = lineIds }));
        Assert.AreEqual(resuming ? 1 : 0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE doc_type='SO' AND year_month='690310'"));
        // 首次HTTP创建已提交但测试枚举解析失败时，只允许原精确草稿及回执继续；不重新创建或覆盖其它成功事实。
        if (resuming) Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sales_order_header WHERE uid='78db0c78-2af7-4606-9713-4a3504451697' AND version=1 AND created_by='dv' AND remark='原HTTP创建' AND NOT is_deleted"));
        var control = new RuntimeSalesStageControl();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development", ApplicationName = typeof(WebApiConfiguration).Assembly.GetName().Name });
        Assert.IsTrue(builder.Configuration.GetValue<bool>("Auth:UseDevUser"));
        Assert.AreEqual("dv", builder.Configuration["Auth:DevUserName"]);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices();
        builder.Services.AddScoped<SwiftExport.Core.Interfaces.SalesOrders.Document.ISalesOrderLineRepository>(sp =>
        {
            var proxy = System.Reflection.DispatchProxy.Create<SwiftExport.Core.Interfaces.SalesOrders.Document.ISalesOrderLineRepository, RuntimeSalesStageProbe>();
            var probe = (RuntimeSalesStageProbe)(object)proxy;
            probe.Target = new SwiftExport.Infrastructure.Repositories.SalesOrders.Document.PostgreSqlSalesOrderLineRepository(sp.GetRequiredService<RepositoryBaseContext>());
            probe.Session = sp.GetRequiredService<DbSession>(); probe.Control = control;
            control.Sessions.Add(probe.Session);
            control.Pids.Add(((NpgsqlConnection)probe.Session.Connection).ProcessID);
            return proxy;
        });
        await using var app = builder.Build();
        app.UseWebApiPipeline(); app.Urls.Add("http://127.0.0.1:0");
        Guid salesId = Guid.Empty;
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) };
            using var meResponse = await client.GetAsync("/api/auth/me");
            Assert.AreEqual(HttpStatusCode.OK, meResponse.StatusCode);
            var me = await meResponse.Content.ReadFromJsonAsync<MeDto>();
            Assert.IsNotNull(me); Assert.AreEqual("dv", me.UserName);
            Assert.AreEqual("825d7e51-f659-4c81-9ea9-4a2d03015360", me.UserId); Assert.IsTrue(me.IsAdmin);
            var request = new SaveSalesOrderRequest
            {
                CommandId = createId,
                Header = new SalesOrderEditHeaderDto { CustomerId = customerId, OrderDate = new DateTime(6903, 10, 6), Currency = "CNY", ExchangeRate = 1, CustomerPoNo = "PG-M17-a0c0d521", Remark = "原HTTP创建" },
                Lines = lineIds.Select((uid,index) => new SalesOrderEditLineDto { Uid = uid, IsNonStandard = true, DescriptionEn = "PG HTTP draft " + index, UnitSnapshot = "Pcs", Qty = index + 1, CnyPrice = 10, UsdPrice = 0 }).ToList()
            };
            var created = await Save(request);
            Assert.IsTrue(created.Success, created.Error?.Code + ": " + created.Message);
            salesId = created.Data.Document.Header.Uid; Assert.AreNotEqual(Guid.Empty, salesId);
            Assert.AreEqual(1, created.Data.Document.Header.Version);
            CollectionAssert.AreEqual(lineIds, created.Data.Document.Lines.Select(x => x.Uid).ToArray());
            var beforeHead = await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(t)::text FROM public.sales_order_header t WHERE uid=@Id", new { Id = salesId });
            var beforeLines = await observer.Connection.QueryAsync<string>("SELECT to_jsonb(t)::text FROM public.sales_order_line t WHERE sales_order_id=@Id ORDER BY uid", new { Id = salesId });
            request.CommandId = updateId; request.Header.Uid = salesId; request.Header.Version = 1; request.Header.Remark = "原HTTP更新";
            foreach (var line in request.Lines) { line.Version = 1; line.Qty += 1; }
            // 遵守现有同用户/路由2秒防抖，不替换过滤器或清缓存。
            await Task.Delay(2100);
            control.Enabled = true;
            var failed = await Save(request, HttpStatusCode.InternalServerError);
            Assert.IsFalse(failed.Success); Assert.AreEqual(ServiceErrorCodes.Unexpected, failed.Error?.Code);
            Assert.AreEqual("22012", control.SqlState); Assert.AreEqual(2, control.StagedRows);
            Assert.AreEqual(2, control.StagedHeadVersion); Assert.IsTrue(control.StagedNumbers.All(x => x > 2));
            Assert.AreEqual(beforeHead, await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(t)::text FROM public.sales_order_header t WHERE uid=@Id", new { Id = salesId }));
            CollectionAssert.AreEqual(beforeLines.ToArray(), (await observer.Connection.QueryAsync<string>("SELECT to_jsonb(t)::text FROM public.sales_order_line t WHERE sales_order_id=@Id ORDER BY uid", new { Id = salesId })).ToArray());
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = updateId }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=ANY(@Ids)", new { Ids = new[] { createId, updateId } }));
            Assert.IsTrue(control.Sessions.All(x => x.Transaction is null && x.CommandLease is null && x.SourceDemandLease is null));
            control.Enabled = false;
            await Task.Delay(2100);
            var recovered = await Save(request);
            Assert.IsTrue(recovered.Success, recovered.Error?.Code + ": " + recovered.Message);
            Assert.AreEqual(salesId, recovered.Data.Document.Header.Uid); Assert.AreEqual(2, recovered.Data.Document.Header.Version);
            CollectionAssert.AreEqual(new[] { 1, 2 }, recovered.Data.Document.Lines.Select(x => x.LineNumber).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 2 }, recovered.Data.Document.Lines.Select(x => x.Version).ToArray());
            CollectionAssert.AreEqual(new[] { 2m, 3m }, recovered.Data.Document.Lines.Select(x => x.Qty).ToArray());
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids) AND actor_uid=@Actor", new { Ids = new[] { createId, updateId }, Actor = Guid.Parse(me.UserId) }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT current_no FROM public.document_sequence WHERE doc_type='SO' AND year_month='690310'"));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.gross_demand WHERE source_type='SO' AND source_uid=@Id", new { Id = salesId }));
            Assert.IsTrue(control.Sessions.All(x => x.Transaction is null && x.CommandLease is null && x.SourceDemandLease is null));
        }
        finally
        {
            await app.StopAsync();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, SalesId = salesId, LineIds = lineIds, Commands = new[] { createId, updateId }, Pids = control.Pids.Distinct().ToArray(), control.StagedRows, control.StagedHeadVersion, control.StagedNumbers, control.SqlState,
                Policy = "仅failure库本批HTTP销售草稿；dv原开发身份；真实暂存后22012回滚及原命令恢复，成功事实保留，无生产/结构/审计规则修改" }));
        }

        async Task<ServiceResult<SaveSalesOrderResponse>> Save(SaveSalesOrderRequest input, HttpStatusCode expectedStatus = HttpStatusCode.OK)
        {
            using var requestClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) };
            using var response = await requestClient.PostAsJsonAsync("/api/sales-orders/detail/save", input);
            Assert.AreEqual(expectedStatus, response.StatusCode);
            var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            var result = await response.Content.ReadFromJsonAsync<ServiceResult<SaveSalesOrderResponse>>(options);
            Assert.IsNotNull(result); return result;
        }
    }

    /// <summary>记录本批真实暂存后的状态与PG错误；仅在测试进程启用，不改变业务条件。</summary>
    public sealed class RuntimeSalesStageControl
    {
        internal bool Enabled;
        internal int StagedRows;
        internal int StagedHeadVersion;
        internal int[] StagedNumbers = [];
        internal string? SqlState;
        internal readonly List<DbSession> Sessions = [];
        internal readonly List<int> Pids = [];
    }

    /// <summary>透明转发原销售行仓储，只在一次真实暂存完成后于原PG事务制造数据库错误，验证外层回滚。</summary>
    public class RuntimeSalesStageProbe : System.Reflection.DispatchProxy
    {
        internal SwiftExport.Core.Interfaces.SalesOrders.Document.ISalesOrderLineRepository Target = null!;
        internal DbSession Session = null!;
        internal RuntimeSalesStageControl Control = null!;

        /// <summary>保留原方法返回和异常类型；指定Stage调用先真实执行，再观察事务内写入及触发PG错误。</summary>
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                var value = targetMethod!.Invoke(Target, args);
                return Control.Enabled && targetMethod.Name == nameof(Target.StageLineNumbersAsync)
                    ? FailAfterStageAsync((Task<int>)value!, (Guid)args![0]!) : value;
            }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        /// <summary>实际更新已落在业务事务中才执行除零，错误22012来自PG服务器；不模拟事务失败或返回业务错误。</summary>
        private async Task<int> FailAfterStageAsync(Task<int> write, Guid orderId)
        {
            Control.StagedRows = await write; Control.Enabled = false;
            Control.StagedHeadVersion = await Session.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.sales_order_header WHERE uid=@Id", new { Id = orderId }, transaction: Session.Transaction);
            Control.StagedNumbers = (await Session.Connection.QueryAsync<int>("SELECT line_number FROM public.sales_order_line WHERE sales_order_id=@Id AND NOT is_deleted ORDER BY line_number", new { Id = orderId }, transaction: Session.Transaction)).ToArray();
            try { await Session.Connection.ExecuteAsync("SELECT 1/0", transaction: Session.Transaction); }
            catch (PostgresException error) { Control.SqlState = error.SqlState; throw; }
            throw new AssertFailedException("真实PG除零未产生预期服务器异常。");
        }
    }


    /// <summary>原币种批量服务的普通非本位CRUD、业务引用拒绝及中途写入冲突回滚；通过新PG作用域读取，不触碰已暂缓本位币切换。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeCurrencyBatchCrudReferencesAndAtomicRollback()
    {
        const string batch = "a61b0aa3-54d8-4926-ae04-2b4973eb5100";
        const string codeA = "P11A61B0A", codeB = "P11B61B0A", codeC = "P11C61B0A";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_CURRENCY_BATCH"), "仅本批首次，成功资料不重置重跑。");
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_dic_currency WHERE currency_code=ANY(@Codes)", new { Codes = new[] { codeA, codeB, codeC } }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.supplier WHERE supplier_code='PGM11A61B0AA3'"));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("SELECT uid AS \"UserId\",user_name AS \"UserName\" FROM public.sys_user WHERE user_name='dv' AND is_active AND NOT is_deleted");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        using var writeScope = app.Services.CreateScope(); using var readScope = app.Services.CreateScope();
        var writer = writeScope.ServiceProvider; var reader = readScope.ServiceProvider;
        var writeSession = writer.GetRequiredService<DbSession>(); var readSession = reader.GetRequiredService<DbSession>();
        var writePid = ((NpgsqlConnection)writeSession.Connection).ProcessID; var readPid = ((NpgsqlConnection)readSession.Connection).ProcessID;
        Assert.AreNotEqual(writePid, readPid);
        var service = writer.GetRequiredService<SwiftExport.Application.Interfaces.Systems.Currencies.ISysDicCurrencyService>();
        var currencyRepository = reader.GetRequiredService<ISysDicCurrencyRepository>();
        Guid currencyA = Guid.Empty, currencyB = Guid.Empty, supplierId = Guid.Empty;
        try
        {
            var created = await service.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDicCurrencyBatchSaveRequest
            { CreateItems = [new CreateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDicCurrencyDto> { Data = Currency(codeA, 1.25m) },
                new CreateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDicCurrencyDto> { Data = Currency(codeB, 2m) }] });
            Assert.IsTrue(created.Success, created.Error?.Code + ": " + created.Message);
            Assert.AreEqual(2, created.Data.SuccessCount);
            var currencies = await currencyRepository.GetByCurrencyCodesAsync([codeA, codeB]);
            currencyA = currencies.Single(c => c.CurrencyCode == codeA).Uid; currencyB = currencies.Single(c => c.CurrencyCode == codeB).Uid;
            Assert.IsFalse(currencies.Any(c => c.IsSystem || c.IsBase));
            var updated = await service.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDicCurrencyBatchSaveRequest
            { UpdateItems = [new UpdateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDicCurrencyDto> { Uid = currencyA, Version = 1, Data = Currency(codeA, 1.75m) }] });
            Assert.IsTrue(updated.Success, updated.Error?.Code + ": " + updated.Message);
            var current = (await currencyRepository.GetByCurrencyCodesAsync([codeA])).Single();
            Assert.AreEqual(2, current.Version); Assert.AreEqual(1.75m, current.RateToBase); Assert.IsFalse(current.IsSystem);
            var supplier = await writer.GetRequiredService<SwiftExport.Application.Interfaces.Suppliers.Management.ISupplierService>().AddAsync(
                new CreateItemRequest<SwiftExport.Contracts.DTOs.Suppliers.SupplierDto> { Data = new() { SupplierCode = "PGM11A61B0AA3", SupplierName = "PG普通币种引用a61b0aa3", DocRelateNo = "PG-M11", Currency = codeA } });
            Assert.IsTrue(supplier.Success, supplier.Error?.Code + ": " + supplier.Message); supplierId = supplier.Data.Uid!.Value;
            var currencyBefore = await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(c)::text FROM public.sys_dic_currency c WHERE uid=@Id", new { Id = currencyA });
            var supplierBefore = await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(s)::text FROM public.supplier s WHERE uid=@Id", new { Id = supplierId });

            // 前一Create会实际执行，后一旧wrapper Version使原Update零行，原最外层必须整体回滚而非留下新增币种。
            var stale = await service.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDicCurrencyBatchSaveRequest
            { CreateItems = [new CreateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDicCurrencyDto> { Data = Currency(codeC, 3m) }],
                UpdateItems = [new UpdateItemRequest<SwiftExport.Contracts.DTOs.SystemDtos.SysDicCurrencyDto> { Uid = currencyA, Version = 1, Data = Currency(codeA, 9m) }] });
            Assert.IsFalse(stale.Success); StringAssert.Contains(stale.Message, "所有操作已回滚");
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_dic_currency WHERE currency_code=@Code", new { Code = codeC }));
            Assert.AreEqual(currencyBefore, await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(c)::text FROM public.sys_dic_currency c WHERE uid=@Id", new { Id = currencyA }));
            var referenced = await service.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDicCurrencyBatchSaveRequest
            { DeleteItems = [new DeleteItemRequest { Uid = currencyA, Version = 2, IsSoftDelete = true }] });
            Assert.IsFalse(referenced.Success); StringAssert.Contains(referenced.Message, "不能改编码或删除");
            Assert.AreEqual(currencyBefore, await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(c)::text FROM public.sys_dic_currency c WHERE uid=@Id", new { Id = currencyA }));
            Assert.AreEqual(supplierBefore, await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(s)::text FROM public.supplier s WHERE uid=@Id", new { Id = supplierId }));
            var deleted = await service.BatchSaveAsync(new SwiftExport.Contracts.DTOs.SystemDtos.Requests.SysDicCurrencyBatchSaveRequest
            { DeleteItems = [new DeleteItemRequest { Uid = currencyB, Version = 1, IsSoftDelete = true }] });
            Assert.IsTrue(deleted.Success, deleted.Error?.Code + ": " + deleted.Message);
            Assert.HasCount(0, await currencyRepository.GetByCurrencyCodesAsync([codeB]));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT is_deleted FROM public.sys_dic_currency WHERE uid=@Id", new { Id = currencyB }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.sys_dic_currency WHERE uid=@Id", new { Id = currencyB }), "原软删不增Version，不为PG改合同。");
            Assert.IsNull(writeSession.Transaction); Assert.IsNull(readSession.Transaction);
            Assert.IsNull(writeSession.CommandLease); Assert.IsNull(writeSession.SourceDemandLease);
        }
        finally
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, CurrencyIds = new[] { currencyA, currencyB }, SupplierId = supplierId,
                Codes = new[] { codeA, codeB, codeC }, WritePid = writePid, ReadPid = readPid,
                Policy = "仅failure新非本位资料/普通原服务，2币种1供应商留存，中途写冲突整批回滚/引用拒绝/原软删，不调用全表NULL重置或写旧资料" }));
        }

        // DTO遵循原校验约束；本位币及系统标记由服务保护，不用旧资料初始化夹具。
        static SwiftExport.Contracts.DTOs.SystemDtos.SysDicCurrencyDto Currency(string code, decimal rate) => new()
        { CurrencyCode = code, CurrencyNameZh = "PG普通币种a61b0aa3", CurrencyNameEn = "PG ordinary currency", CurrencySymbol = "¤", Precision = 2,
            RateToBase = rate, IsBase = false, IsSystem = false, IsEnabled = true, Remark = batch };
    }



    /// <summary>真实需求读完旧共享组成并持有根锁时，另一连接保存等待；前一需求完整使用旧比例，保存提交后新来源完整使用新比例。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeSharedBomDemandAndSaveInterleave()
    {
        const string batch = "9de3b79e-6ab4-49fd-b78e-187f3a958100";
        const string prefix = "PGM03-9de3b79e";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_BOM_INTERLEAVE_BATCH"), "只执行已登记首次，不重置成功事实。");
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        Guid Id(int index) => Guid.Parse("9de3b79e-6ab4-49fd-b78e-187f3a958" + (100 + index).ToString("D3"));
        var commandIds = Enumerable.Range(0, 4).Select(Id).ToArray();
        var lineIds = new[] { Id(4), Id(5) };
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = commandIds }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product WHERE sku=ANY(@Skus)", new { Skus = new[] { prefix + "-R", prefix + "-C" } }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.bom WHERE bom_no=@No", new { No = prefix }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE doc_type='SO' AND year_month='680310'"));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("SELECT uid AS \"UserId\",user_name AS \"UserName\" FROM public.sys_user WHERE user_name='dv' AND is_active AND NOT is_deleted");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        // 只有当前测试作用域的BOM接口透明包裹实际PG仓储；生产DI及事务协议保持原样。
        builder.Services.AddScoped<IBomRepository>(sp =>
        {
            var proxy = System.Reflection.DispatchProxy.Create<IBomRepository, RuntimeBomReadProbe>();
            ((RuntimeBomReadProbe)(object)proxy).Target = new SwiftExport.Infrastructure.Repositories.Products.PostgreSqlBomRepository(sp.GetRequiredService<RepositoryBaseContext>());
            return proxy;
        });
        await using var app = builder.Build();
        using var writeScope = app.Services.CreateScope();
        using var readScope = app.Services.CreateScope();
        var writer = writeScope.ServiceProvider; var reader = readScope.ServiceProvider;
        var writeSession = writer.GetRequiredService<DbSession>(); var readSession = reader.GetRequiredService<DbSession>();
        var writePid = ((NpgsqlConnection)writeSession.Connection).ProcessID; var readPid = ((NpgsqlConnection)readSession.Connection).ProcessID;
        Assert.AreNotEqual(writePid, readPid);
        var probe = (RuntimeBomReadProbe)(object)reader.GetRequiredService<IBomRepository>();
        var root = new Product { Sku = prefix + "-R", BomNo = prefix, BomVersion = 3, SupplyType = "采购", Unit = "Pcs", MaterialType = "成品" };
        var child = new Product { Sku = prefix + "-C", BomVersion = 0, SupplyType = "采购", Unit = "Pcs", MaterialType = "客供件" };
        Guid bomId = Guid.Empty, salesId = Guid.Empty;
        Guid[] grossIds = [], netIds = [];
        string? blocking = null; int nextPid = 0;
        var pending = new List<Task>();
        try
        {
            foreach (var product in new[] { root, child })
            {
                var inserted = await writer.GetRequiredService<IRepository<Product>>().AddAsync(product);
                Assert.IsTrue(inserted.Success, inserted.ErrorCode + ": " + inserted.Message);
            }
            var bomService = writer.GetRequiredService<SwiftExport.Application.Interfaces.IBomService>();
            var created = await bomService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.Products.Requests.BomBatchSaveRequest
            { CreateItems = [new CreateItemRequest<SwiftExport.Contracts.DTOs.Products.BomDto> { Data = new() { BomNo = prefix, SubSkuId = child.Uid, Numerator = 1, Denominator = 1 } }] });
            Assert.IsTrue(created.Success, created.Error?.Code + ": " + created.Message);
            var original = (await writer.GetRequiredService<IBomRepository>().GetBomByBomNoAsync(prefix)).Single();
            bomId = original.Uid;
            var saved = await writer.GetRequiredService<ISalesOrderDraftService>().SaveAsync(new SaveSalesOrderRequest
            {
                CommandId = Id(0),
                Header = new SalesOrderEditHeaderDto { CustomerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f"), OrderDate = new DateTime(6803, 10, 6), DeliveryDate = new DateTime(6803, 11, 20), Currency = "CNY", ExchangeRate = 1, CustomerPoNo = prefix, Remark = prefix },
                Lines = [new SalesOrderEditLineDto { Uid = lineIds[0], ProductId = root.Uid, Sku = root.Sku, Qty = 3, CnyPrice = 10, UsdPrice = 0 },
                    new SalesOrderEditLineDto { Uid = lineIds[1], ProductId = root.Uid, Sku = root.Sku, Qty = 9, CnyPrice = 10, UsdPrice = 0 }]
            });
            Assert.IsTrue(saved.Success, saved.Error?.Code + ": " + saved.Message);
            salesId = saved.Data.Document.Header.Uid;
            var confirmed = await writer.GetRequiredService<ISalesOrderConfirmationService>().ConfirmAsync(salesId, new ConfirmSalesOrderCommand { CommandId = Id(1), Uid = salesId, Version = saved.Data.Document.Header.Version });
            Assert.IsTrue(confirmed.Success, confirmed.Error?.Code + ": " + confirmed.Message);
            var demands = (await reader.GetRequiredService<IRepository<GrossDemand>>().GetByIdListAsync(
                (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.gross_demand WHERE source_type='SO' AND source_uid=@Id", new { Id = salesId })).ToArray())).OrderBy(d => d.DemandQty).ToArray();
            Assert.HasCount(2, demands); grossIds = demands.Select(d => d.Uid).ToArray();
            probe.Enabled = true;
            var establishing = reader.GetRequiredService<IMaterialDemandEstablishmentService>().EstablishAsync(Command(Id(2), demands[0]));
            pending.Add(establishing);
            await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.IsNotNull(readSession.Transaction);
            Assert.HasCount(1, probe.Observed);
            Assert.AreEqual(1, probe.Observed[0].Numerator); Assert.AreEqual(1, probe.Observed[0].Denominator);
            var saving = bomService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.Products.Requests.BomBatchSaveRequest
            { UpdateItems = [new UpdateItemRequest<SwiftExport.Contracts.DTOs.Products.BomDto> { Uid = bomId, Version = original.Version, Data = new() { BomNo = prefix, SubSkuId = child.Uid, Numerator = 2, Denominator = 3 } }] });
            pending.Add(saving);
            // 等待真实服务器阻塞关系，不用定时睡眠猜测保存已取得锁，也不要求writer在根锁释放前提交。
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                blocking = await observer.Connection.ExecuteScalarAsync<string>("""
                    SELECT json_build_object('writer',a.pid,'blockers',pg_blocking_pids(a.pid),'wait_type',a.wait_event_type,'wait_event',a.wait_event)::text
                    FROM pg_stat_activity a WHERE a.pid=@Writer AND @Reader=ANY(pg_blocking_pids(a.pid))
                      AND EXISTS(SELECT 1 FROM pg_locks l WHERE l.pid=a.pid AND l.relation='public.product'::regclass AND l.mode='RowShareLock')
                    """, new { Writer = writePid, Reader = readPid });
                if (blocking is not null) break;
                if (saving.IsCompleted) break;
                await Task.Delay(25);
            }
            Assert.IsNotNull(blocking, "保存必须在真实产品锁上等需求，不能把未观察到的等待当通过。");
            Assert.IsFalse(saving.IsCompleted);
            probe.Release.TrySetResult();
            var established = await establishing.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.IsTrue(established.Success, established.Error?.Code + ": " + established.Message);
            Assert.AreEqual(2, established.Data.CreatedMaterialDemandCount);
            var updated = await saving.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.IsTrue(updated.Success, updated.Error?.Code + ": " + updated.Message);
            var current = (await writer.GetRequiredService<IBomRepository>().GetBomByBomNoAsync(prefix)).Single();
            Assert.AreEqual(original.Version + 1, current.Version);
            Assert.AreEqual(original.ParentProductId, current.ParentProductId); Assert.AreEqual(original.ParentSkuSnapshot, current.ParentSkuSnapshot);
            Assert.AreEqual(2, current.Numerator); Assert.AreEqual(3, current.Denominator);
            var savedBom = await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(b)::text FROM public.bom b WHERE uid=@Id", new { Id = bomId });
            using var nextScope = app.Services.CreateScope();
            var next = nextScope.ServiceProvider; var nextSession = next.GetRequiredService<DbSession>();
            nextPid = ((NpgsqlConnection)nextSession.Connection).ProcessID;
            Assert.AreNotEqual(readPid, nextPid); Assert.AreNotEqual(writePid, nextPid);
            var nextEstablished = await next.GetRequiredService<IMaterialDemandEstablishmentService>().EstablishAsync(Command(Id(3), demands[1]));
            Assert.IsTrue(nextEstablished.Success, nextEstablished.Error?.Code + ": " + nextEstablished.Message);
            Assert.AreEqual(2, nextEstablished.Data.CreatedMaterialDemandCount);
            var nets = await next.GetRequiredService<IRepository<NetDemand>>().GetByIdListAsync(
                (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.net_demand WHERE gross_demand_id=ANY(@Ids)", new { Ids = grossIds })).ToArray());
            netIds = nets.Select(n => n.Uid).ToArray(); Assert.HasCount(4, nets);
            for (var i = 0; i < demands.Length; i++)
            {
                var demand = demands[i];
                Assert.AreEqual(demand.DemandQty, nets.Single(n => n.GrossDemandId == demand.Uid && n.ProductId == root.Uid).NetQty);
                var component = nets.Single(n => n.GrossDemandId == demand.Uid && n.ProductId == child.Uid);
                Assert.AreEqual(i == 0 ? 3m : 6m, component.NetQty);
                Assert.AreEqual(bomId, component.BomLineId); Assert.AreEqual(root.Sku, component.ParentSkuSnapshot);
                Assert.AreEqual(demand.DemandBasisFingerprint, component.SourceDemandBasisFingerprintSnapshot);
            }
            Assert.AreEqual(savedBom, await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(b)::text FROM public.bom b WHERE uid=@Id", new { Id = bomId }));
            Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = commandIds }));
            foreach (var session in new[] { writeSession, readSession, nextSession })
            { Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        }
        finally
        {
            probe.Release.TrySetResult();
            // 观察失败也先放行真实任务并等待终态，不将一次超时当作已停止，不删已提交事实。
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(35));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, Prefix = prefix, ProductIds = new[] { root.Uid, child.Uid },
                BomId = bomId, SalesId = salesId, LineIds = lineIds, GrossIds = grossIds, NetIds = netIds, CommandIds = commandIds,
                WritePid = writePid, ReadPid = readPid, NextPid = nextPid, Blocking = blocking, ObservedBomVersion = probe.Observed.FirstOrDefault()?.Version,
                Policy = "仅failure新批次；真实仓储读取后屏障及服务器等待；完整旧需求/新需求，全部成功事实留存" }));
        }

        // 两个不同来源和命令只消费各自真实版本，不复活或重建第一批已成功需求。
        static EstablishMaterialDemandsCommand Command(Guid commandId, GrossDemand demand) => new()
        { CommandId = commandId, Items = [new EstablishMaterialDemandItem { GrossDemandId = demand.Uid, SourceDemandVersion = demand.Version, SourceVersion = demand.SourceVersion }] };
    }

    /// <summary>测试专用透明仓储屏障：读取真实旧组成后暂停一次，所有结果与写入继续由原PG仓储负责。</summary>
    public class RuntimeBomReadProbe : System.Reflection.DispatchProxy
    {
        internal IBomRepository Target = null!;
        internal bool Enabled;
        internal Bom[] Observed = [];
        internal readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>转发真实方法，保留异常原类型；只有指定作用域的一次按编号读取加入屏障。</summary>
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            try
            {
                var value = targetMethod!.Invoke(Target, args);
                return Enabled && targetMethod.Name == nameof(IBomRepository.GetBomByBomNoAsync)
                    ? PauseAsync((Task<IEnumerable<Bom>>)value!) : value;
            }
            catch (System.Reflection.TargetInvocationException error) when (error.InnerException is not null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }

        /// <summary>先完整读取实际组成再通知主控；释放只控制测试时序，不修改数据库锁、隔离或返回内容。</summary>
        private async Task<IEnumerable<Bom>> PauseAsync(Task<IEnumerable<Bom>> read)
        {
            var rows = (await read).ToArray();
            Enabled = false; Observed = rows; Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return rows;
        }
    }

    /// <summary>原BOM服务保存共享比例后，新PG作用域经原销售来源和需求建立服务读取两种正标记根；只留存本批新事实，不修改业务规则。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeSharedBomSaveFeedsMaterialEstablishment()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        const string batch = "36b5e4cf-f981-4356-bc94-54f8a8a69100";
        const string prefix = "PGM03-36b5e4cf";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_SHARED_BOM_BATCH"), "本批仅首次执行，成功事实不得重置重跑。");
        var saveId = Guid.Parse(batch);
        var confirmId = Guid.Parse("36b5e4cf-f981-4356-bc94-54f8a8a69101");
        var establishId = Guid.Parse("36b5e4cf-f981-4356-bc94-54f8a8a69102");
        var lineIds = new[] { Guid.Parse("36b5e4cf-f981-4356-bc94-54f8a8a69103"), Guid.Parse("36b5e4cf-f981-4356-bc94-54f8a8a69104") };
        var commandIds = new[] { saveId, confirmId, establishId };
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = commandIds }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product WHERE sku=ANY(@Skus)", new { Skus = new[] { prefix + "-R1", prefix + "-R3", prefix + "-C" } }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.bom WHERE bom_no=@No", new { No = prefix }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE doc_type='SO' AND year_month='670310'"));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("SELECT uid AS \"UserId\",user_name AS \"UserName\" FROM public.sys_user WHERE user_name='dv' AND is_active AND NOT is_deleted");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices();
        builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        using var writeScope = app.Services.CreateScope();
        using var demandScope = app.Services.CreateScope();
        var writer = writeScope.ServiceProvider;
        var reader = demandScope.ServiceProvider;
        var writeSession = writer.GetRequiredService<DbSession>();
        var readSession = reader.GetRequiredService<DbSession>();
        var writePid = ((NpgsqlConnection)writeSession.Connection).ProcessID;
        var readPid = ((NpgsqlConnection)readSession.Connection).ProcessID;
        Assert.AreNotEqual(writePid, readPid);
        var roots = new[]
        {
            new Product { Sku = prefix + "-R1", BomNo = prefix, BomVersion = 1, SupplyType = "采购", Unit = "Pcs", MaterialType = "成品" },
            new Product { Sku = prefix + "-R3", BomNo = prefix, BomVersion = 3, SupplyType = "采购", Unit = "Pcs", MaterialType = "成品" }
        };
        var child = new Product { Sku = prefix + "-C", BomVersion = 0, SupplyType = "采购", Unit = "Pcs", MaterialType = "客供件" };
        Guid bomId = Guid.Empty, salesId = Guid.Empty;
        Guid[] grossIds = [], netIds = [];
        string? savedBom = null;
        try
        {
            // 三个新产品只是明确登记的局部主数据夹具；保存BOM、销售来源和需求均走原真实服务，不手写Gross/Net/回执。
            foreach (var product in roots.Append(child))
            {
                var inserted = await writer.GetRequiredService<IRepository<Product>>().AddAsync(product);
                Assert.IsTrue(inserted.Success, inserted.ErrorCode + ": " + inserted.Message);
            }
            var bomService = writer.GetRequiredService<SwiftExport.Application.Interfaces.IBomService>();
            var created = await bomService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.Products.Requests.BomBatchSaveRequest
            {
                CreateItems = [new CreateItemRequest<SwiftExport.Contracts.DTOs.Products.BomDto> { Data = new() { BomNo = prefix, SubSkuId = child.Uid, Numerator = 1, Denominator = 1 } }]
            });
            Assert.IsTrue(created.Success, created.Error?.Code + ": " + created.Message);
            var original = (await writer.GetRequiredService<IBomRepository>().GetBomByBomNoAsync(prefix)).Single();
            bomId = original.Uid;
            var updated = await bomService.BatchSaveAsync(new SwiftExport.Contracts.DTOs.Products.Requests.BomBatchSaveRequest
            {
                UpdateItems = [new UpdateItemRequest<SwiftExport.Contracts.DTOs.Products.BomDto> { Uid = bomId, Version = original.Version, Data = new() { BomNo = prefix, SubSkuId = child.Uid, Numerator = 2, Denominator = 3 } }]
            });
            Assert.IsTrue(updated.Success, updated.Error?.Code + ": " + updated.Message);
            var current = (await reader.GetRequiredService<IBomRepository>().GetBomByBomNoAsync(prefix)).Single();
            Assert.AreEqual(original.Version + 1, current.Version);
            Assert.AreEqual(original.ParentProductId, current.ParentProductId);
            Assert.AreEqual(original.ParentSkuSnapshot, current.ParentSkuSnapshot);
            Assert.AreEqual(2, current.Numerator); Assert.AreEqual(3, current.Denominator);
            savedBom = await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(b)::text FROM public.bom b WHERE uid=@Id", new { Id = bomId });
            var saved = await writer.GetRequiredService<ISalesOrderDraftService>().SaveAsync(new SaveSalesOrderRequest
            {
                CommandId = saveId,
                Header = new SalesOrderEditHeaderDto { CustomerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f"), OrderDate = new DateTime(6703, 10, 6), DeliveryDate = new DateTime(6703, 11, 20), Currency = "CNY", ExchangeRate = 1, CustomerPoNo = prefix, Remark = prefix },
                Lines = [new SalesOrderEditLineDto { Uid = lineIds[0], ProductId = roots[0].Uid, Sku = roots[0].Sku, Qty = 3, CnyPrice = 10, UsdPrice = 0 },
                    new SalesOrderEditLineDto { Uid = lineIds[1], ProductId = roots[1].Uid, Sku = roots[1].Sku, Qty = 9, CnyPrice = 10, UsdPrice = 0 }]
            });
            Assert.IsTrue(saved.Success, saved.Error?.Code + ": " + saved.Message);
            salesId = saved.Data.Document.Header.Uid;
            var confirmed = await writer.GetRequiredService<ISalesOrderConfirmationService>().ConfirmAsync(salesId, new ConfirmSalesOrderCommand { CommandId = confirmId, Uid = salesId, Version = saved.Data.Document.Header.Version });
            Assert.IsTrue(confirmed.Success, confirmed.Error?.Code + ": " + confirmed.Message);
            Assert.AreEqual(2, confirmed.Data.GrossDemandCount);
            grossIds = (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.gross_demand WHERE source_type='SO' AND source_uid=@Id", new { Id = salesId })).ToArray();
            var demands = await reader.GetRequiredService<IRepository<GrossDemand>>().GetByIdListAsync(grossIds);
            var established = await reader.GetRequiredService<IMaterialDemandEstablishmentService>().EstablishAsync(new EstablishMaterialDemandsCommand
            {
                CommandId = establishId,
                Items = demands.Select(d => new EstablishMaterialDemandItem { GrossDemandId = d.Uid, SourceDemandVersion = d.Version, SourceVersion = d.SourceVersion }).ToList()
            });
            Assert.IsTrue(established.Success, established.Error?.Code + ": " + established.Message);
            Assert.AreEqual(4, established.Data.CreatedMaterialDemandCount);
            var nets = await reader.GetRequiredService<IRepository<NetDemand>>().GetByIdListAsync(
                (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.net_demand WHERE gross_demand_id=ANY(@Ids)", new { Ids = grossIds })).ToArray());
            netIds = nets.Select(n => n.Uid).ToArray();
            Assert.HasCount(4, nets);
            foreach (var root in roots)
            {
                var demand = demands.Single(d => d.ProductId == root.Uid);
                var rootNet = nets.Single(n => n.GrossDemandId == demand.Uid && n.ProductId == root.Uid);
                var childNet = nets.Single(n => n.GrossDemandId == demand.Uid && n.ProductId == child.Uid);
                Assert.AreEqual(demand.DemandQty, rootNet.NetQty);
                Assert.AreEqual(demand.DemandQty * 2 / 3, childNet.NetQty);
                Assert.AreEqual(bomId, childNet.BomLineId);
                Assert.AreEqual(root.Sku, childNet.ParentSkuSnapshot);
                Assert.AreEqual(demand.DemandBasisFingerprint, childNet.SourceDemandBasisFingerprintSnapshot);
            }
            Assert.AreEqual(savedBom, await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(b)::text FROM public.bom b WHERE uid=@Id", new { Id = bomId }));
            Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = commandIds }));
            Assert.IsNull(writeSession.Transaction); Assert.IsNull(readSession.Transaction);
            Assert.IsNull(writeSession.CommandLease); Assert.IsNull(readSession.CommandLease);
            Assert.IsNull(writeSession.SourceDemandLease); Assert.IsNull(readSession.SourceDemandLease);
        }
        finally
        {
            // 成功及部分成功均保留身份；不删除新回执/需求或修旧数据，失败后由主控依据真实阶段判断接续。
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, Prefix = prefix, ProductIds = roots.Append(child).Select(p => p.Uid).ToArray(),
                BomId = bomId, SalesId = salesId, LineIds = lineIds, GrossIds = grossIds, NetIds = netIds, CommandIds = commandIds, WritePid = writePid, ReadPid = readPid,
                Policy = "仅failure明确首次批次；三个局部产品夹具，真实BOM保存/销售来源/需求服务；成功事实留存，不写运行库或备份" }));
        }
    }


    /// <summary>不同命令竞争同一原人工出库的真实Item及原流水；各阶段一个成功一个版本拒绝，原流水不变且完整反向只处置本批新事实。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeSameOutboundItemAndOriginalReversalCompetition()
    {
        const string batch = "0a77ecca-8ebd-4da1-812f-a8f1c1f85f00";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_SAME_OUTBOUND_BATCH"));
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(0,11).Select(Id).ToArray() }), "只执行预登记首次，有成功事实不能重造。");
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='660210'"));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.supplier WHERE supplier_code='PGG40A77ECCA'"));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.warehouse_site WHERE code='PG-G4-0a77ecca'"));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u WHERE u.is_active AND NOT u.is_deleted
            AND rtrim(u.user_name,' ') COLLATE public.erp_dictionary_ci='dv' COLLATE public.erp_dictionary_ci
            AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=u.uid
                AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN')
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(),"SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor); await using var app = builder.Build();
        var productId = Guid.Parse("00008dec-d28a-4036-bc06-da0a2159e16b");
        var customerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f"); var date = new DateTime(6602,10,6);
        Guid supplierId = Guid.Empty, warehouseId = Guid.Empty, purchaseId = Guid.Empty, purchaseLineId = Guid.Empty,
            inboundId = Guid.Empty, outboundId = Guid.Empty, outboundItemId = Guid.Empty, originalLedgerId = Guid.Empty, balanceId = Guid.Empty;
        var evidence = new List<object>(); var pending = new List<Task>(); var pids = new List<int>();
        try
        {
            // 原一供应商一活动虚拟仓规则不变，只用原服务创建本批独立供应商/仓与独立采购。
            var supplier = await Run(sp => sp.GetRequiredService<SwiftExport.Application.Interfaces.Suppliers.Management.ISupplierService>().AddAsync(
                new CreateItemRequest<SwiftExport.Contracts.DTOs.Suppliers.SupplierDto> { ClientKey = batch,
                    Data = new() { Version = 1, SupplierCode = "PGG40A77ECCA", SupplierName = "PG同单据竞争供应商0a77ecca", DocRelateNo = "PG-G4", Currency = "CNY" } }));
            supplierId = supplier.Uid!.Value;
            warehouseId = (await Run(sp => sp.GetRequiredService<SwiftExport.Application.Interfaces.Inventory.Warehouses.IWarehouseManagementService>()
                .CreateSupplierVirtualWarehouseAsync(new() { CommandId = Id(0), SupplierId = supplierId, Code = "PG-G4-0a77ecca", Name = "PG同单据竞争仓0a77ecca" }))).Uid;
            var purchase = await Run(sp => sp.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(new() { CommandId = Id(1),
                CustomerId = customerId, SupplierId = supplierId, OrderDate = date, Remark = batch,
                Lines = [new CreateIndependentPurchaseOrderLineCommand { ProductId = productId, Quantity = 2, UnitPrice = 10, TaxPrice = 10 }] }));
            purchaseId = purchase.PurchaseOrderId; purchaseLineId = purchase.Lines.Single().PurchaseOrderLineId;
            var basis = await Run(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(purchaseId));
            await Run(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(purchaseId,
                new() { CommandId = Id(2), ExpectedVersion = 1, ContextFingerprint = basis.Fingerprint }));
            inboundId = (await Run(sp => sp.GetRequiredService<IInboundDraftService>().CreateAsync(new() { CommandId = Id(3),
                Draft = new InboundDraftData { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, WarehouseSiteId = warehouseId,
                    ReceiptDate = date, Remark = batch, Lines = [new InboundDraftLineInput { SourceLineId = purchaseLineId, Quantity = 2 }] } }))).Uid;
            await Run(sp => sp.GetRequiredService<IInboundPostingService>().PostAsync(inboundId, new() { CommandId = Id(4), ExpectedVersion = 1 }));
            balanceId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.inventory_balance WHERE warehouse_site_id=@Id AND product_id=@Product", new { Id = warehouseId, Product = productId });
            outboundId = (await Run(sp => sp.GetRequiredService<IOutboundDraftService>().CreateAsync(new() { CommandId = Id(5),
                Draft = new OutboundDraftData { DocumentType = OutboundDocumentTypeEnum.StockIssue, WarehouseSiteId = warehouseId,
                    ShipmentDate = date, IssueReason = "PG同Item不同父命令竞争", Remark = batch,
                    Lines = [new OutboundDraftLineInput { ProductId = productId, Quantity = 2 }] } }))).Uid;
            outboundItemId = await observer.Connection.QuerySingleAsync<Guid>("SELECT command_item_id FROM public.outbound_document_line WHERE outbound_document_id=@Id", new { Id = outboundId });
            var preview = await Run(sp => sp.GetRequiredService<IOutboundPostingService>().PreviewPostAsync(outboundId, new() { ExpectedVersion = 1 }));
            Assert.IsTrue(preview.CanPost); Assert.AreEqual(2m, preview.StockImpacts.Single().CurrentQuantity);
            await Race("同Item正向", "SELECT uid FROM public.warehouse_site WHERE uid=@Id FOR UPDATE", warehouseId,
                (sp,i,ct) => sp.GetRequiredService<IOutboundPostingService>().PostAsync(outboundId, new() { CommandId = Id(6+i), ExpectedVersion = 1 }, ct));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=3 AND on_hand_qty=0 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE command_item_id=@Id", new { Id = outboundItemId }));
            originalLedgerId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.inventory_ledger_entry WHERE command_item_id=@Id", new { Id = outboundItemId });
            var originalBeforeReverse = await OriginalFact(); Assert.IsNotNull(originalBeforeReverse);
            var reversePreview = await Run(sp => sp.GetRequiredService<IOutboundReversalService>().PreviewReverseAsync(outboundId, new() { ExpectedVersion = 2 }));
            Assert.IsTrue(reversePreview.CanReverse);
            // 只锁本批原流水作第三连接屏障；首次反向到原库存内核锁点，第二请求等待同一真实单据头。
            await Race("同原流水反向", "SELECT uid FROM public.inventory_ledger_entry WHERE uid=@Id FOR UPDATE", originalLedgerId,
                (sp,i,ct) => sp.GetRequiredService<IOutboundReversalService>().ReverseAsync(outboundId,
                    new() { CommandId = Id(8+i), ExpectedVersion = 2, Reason = "PG同原流水不同父命令竞争" }, ct));
            Assert.AreEqual(originalBeforeReverse, await OriginalFact());
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE reversal_of_ledger_id=@Id", new { Id = originalLedgerId }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=4 AND on_hand_qty=2 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            await Run(sp => sp.GetRequiredService<IInboundReversalService>().ReverseAsync(inboundId,
                new() { CommandId = Id(10), ExpectedVersion = 2, Reason = "PG本批入库原完整反向归零" }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=5 AND on_hand_qty=0 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE warehouse_site_id=@Id", new { Id = warehouseId }));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry r JOIN public.inventory_ledger_entry o ON o.uid=r.reversal_of_ledger_id WHERE r.warehouse_site_id=@Id AND r.quantity_delta=-o.quantity_delta", new { Id = warehouseId }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(7), Id(9) } }));
        }
        finally { Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, SupplierId = supplierId, WarehouseId = warehouseId,
            PurchaseId = purchaseId, PurchaseLineId = purchaseLineId, InboundId = inboundId, OutboundId = outboundId, OutboundItemId = outboundItemId,
            OriginalLedgerId = originalLedgerId, BalanceId = balanceId, Evidence = evidence, Pids = pids.Distinct().ToArray(),
            TasksTerminal = pending.All(t => t.IsCompleted), Policy = "仅failure；独立新事实及660210号段留存，不改旧单据/业务/锁/隔离，不重置" })); }

        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
        void Clean(IServiceProvider sp)
        { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        async Task<T> Run<T>(Func<IServiceProvider,Task<ServiceResult<T>>> action)
        { using var scope = app.Services.CreateScope(); var result = await action(scope.ServiceProvider); Assert.IsTrue(result.Success,result.Error?.Code+":"+result.Message); Clean(scope.ServiceProvider); return result.Data; }
        Task<string?> OriginalFact() => observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(l)::text FROM public.inventory_ledger_entry l WHERE uid=@Id", new { Id = originalLedgerId });
        // 两实际调用按原默认隔离竞争，第三连接只作屏障；每阶段完整观察真实等待再释放，所有任务均等待终态。
        async Task Race<T>(string stage, string barrierSql, Guid barrierId, Func<IServiceProvider,int,CancellationToken,Task<ServiceResult<T>>> action)
        {
            using var holder = new DbSession(new PostgreSqlConnectionFactory(connection));
            using var firstScope = app.Services.CreateScope(); using var secondScope = app.Services.CreateScope();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var scopes = new[] { firstScope, secondScope }; var sessions = scopes.Select(s => s.ServiceProvider.GetRequiredService<DbSession>()).ToArray();
            var phasePids = sessions.Select(Pid).ToArray(); pids.AddRange(phasePids.Append(Pid(holder)));
            Assert.AreEqual(3, phasePids.Append(Pid(holder)).Distinct().Count());
            holder.Transaction = holder.Connection.BeginTransaction();
            await holder.Connection.ExecuteScalarAsync<Guid>(barrierSql, new { Id = barrierId }, holder.Transaction);
            var tasks = new List<Task<ServiceResult<T>>>();
            try
            {
                tasks.Add(action(firstScope.ServiceProvider,0,deadline.Token)); pending.Add(tasks[0]);
                await Observe([phasePids[0]], stage, deadline.Token);
                tasks.Add(action(secondScope.ServiceProvider,1,deadline.Token)); pending.Add(tasks[1]);
                await Observe(phasePids, stage, deadline.Token);
                holder.Transaction.Commit(); holder.Transaction.Dispose(); holder.Transaction = null;
                var results = await Task.WhenAll(tasks);
                Assert.IsTrue(results[0].Success,results[0].Error?.Code+":"+results[0].Message);
                Assert.IsFalse(results[1].Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict,results[1].Error?.Code,results[1].Message);
                foreach (var scope in scopes) Clean(scope.ServiceProvider);
                evidence.Add(new { Stage = stage+"终态", First = results[0], Second = results[1], Barrier = barrierId, Pids = phasePids, Holder = Pid(holder) });
            }
            finally
            {
                if (holder.Transaction is not null) { holder.Transaction.Rollback(); holder.Transaction.Dispose(); holder.Transaction = null; }
                deadline.Cancel();
                try { await Task.WhenAll(tasks); } catch (Exception error) { evidence.Add(new { Stage = stage+"收尾", ErrorType = error.GetType().Name }); }
                Assert.IsTrue(tasks.All(t => t.IsCompleted));
            }
        }
        // 查询服务器真实阻塞和原出库头行锁关联，不用固定延时猜测已进入业务。
        async Task Observe(int[] candidates, string stage, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var count = await observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT count(*) FROM pg_stat_activity a WHERE a.pid=ANY(@Pids) AND a.xact_start IS NOT NULL
                    AND cardinality(pg_blocking_pids(a.pid))>0 AND EXISTS(SELECT 1 FROM pg_locks l WHERE l.pid=a.pid
                        AND l.locktype='relation' AND l.relation='public.outbound_document'::regclass AND l.mode='RowShareLock')
                    """, new { Pids = candidates }, cancellationToken: token));
                if (count == candidates.Length)
                {
                    var graph = await observer.Connection.ExecuteScalarAsync<string>("SELECT jsonb_agg(jsonb_build_object('pid',pid,'blocking',pg_blocking_pids(pid),'xact_start',xact_start,'query',left(query,200)))::text FROM pg_stat_activity WHERE pid=ANY(@Pids)", new { Pids = candidates });
                    evidence.Add(new { Stage = stage+"真实等待", Pids = candidates, Graph = graph }); return;
                }
                await Task.Delay(20,token);
            }
        }
    }

    /// <summary>两真实来源回退先持首来源、等待次来源，补关联在首快照前等待；回退提交后关联按最新修订拒绝，所有成功事实留存。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeCrossSourceRollbackBeforeAssociation()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        const string batch = "eac1e32d-21b7-4f6e-84d1-a7a66ae10700";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_CROSS_SOURCE_BATCH"));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(0, 11).Select(Id).ToArray() }),
            "只执行预登记首次；成功事实不得重置或换身份重跑。");
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='660110'"));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u
            WHERE u.is_active AND NOT u.is_deleted AND rtrim(u.user_name,' ') COLLATE public.erp_dictionary_ci='dv' COLLATE public.erp_dictionary_ci
              AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
                WHERE ur.user_id=u.uid AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN')
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor); await using var app = builder.Build();
        var productId = Guid.Parse("00008dec-d28a-4036-bc06-da0a2159e16b");
        var customerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f");
        var supplierId = Guid.Parse("017b0b30-2744-4e4b-8bfb-1169e5be98eb");
        var sales = new Guid[2]; var gross = new Guid[2]; var nets = new Guid[2];
        Guid purchaseId = Guid.Empty, purchaseLineId = Guid.Empty;
        var evidence = new List<object>(); var pending = new List<Task>(); var pids = new List<int>();
        try
        {
            for (var i = 0; i < 2; i++)
            {
                using var scope = app.Services.CreateScope(); var sp = scope.ServiceProvider;
                var product = await sp.GetRequiredService<IRepository<Product>>().GetByIdAsync(productId); Assert.IsNotNull(product);
                var saved = await sp.GetRequiredService<ISalesOrderDraftService>().SaveAsync(new() { CommandId = Id(i * 3),
                    Header = new SalesOrderEditHeaderDto { CustomerId = customerId, OrderDate = new DateTime(6601,10,6),
                        DeliveryDate = new DateTime(6601,11,20), Currency = "CNY", ExchangeRate = 1,
                        CustomerPoNo = "PG-G2-eac1e32d-" + i, Remark = batch },
                    Lines = [new SalesOrderEditLineDto { Uid = Id(i * 3 + 1), ProductId = productId, Sku = product.Sku, Qty = 3, CnyPrice = 10, UsdPrice = 0 }] });
                Assert.IsTrue(saved.Success, saved.Error?.Code + ":" + saved.Message); sales[i] = saved.Data.Document.Header.Uid;
                var confirmed = await sp.GetRequiredService<ISalesOrderConfirmationService>().ConfirmAsync(sales[i],
                    new() { CommandId = Id(i * 3 + 2), Uid = sales[i], Version = saved.Data.Document.Header.Version });
                Assert.IsTrue(confirmed.Success, confirmed.Error?.Code + ":" + confirmed.Message); Assert.AreEqual(1, confirmed.Data.GrossDemandCount); Clean(sp);
                gross[i] = await observer.Connection.QuerySingleAsync<Guid>(
                    "SELECT uid FROM public.gross_demand WHERE source_uid=@Source AND source_line_id=@Line", new { Source = sales[i], Line = Id(i * 3 + 1) });
            }
            await Run(sp => sp.GetRequiredService<IMaterialDemandEstablishmentService>().EstablishAsync(new() { CommandId = Id(6),
                Items = gross.Select(id => new EstablishMaterialDemandItem { GrossDemandId = id, SourceDemandVersion = 1, SourceVersion = 2 }).ToList() }));
            for (var i = 0; i < 2; i++) nets[i] = await observer.Connection.QuerySingleAsync<Guid>(
                "SELECT uid FROM public.net_demand WHERE gross_demand_id=@Id AND lifecycle_status=0 AND NOT is_deleted", new { Id = gross[i] });
            // 真实锁按摘要键排序，不能用Guid顺序假定准备次序；屏障只持后一个来源。
            var order = Enumerable.Range(0, 2).OrderBy(i => PostgreSqlSourceDemandLock.Key("Source", sales[i])).ToArray();
            var first = order[0]; var last = order[1];
            var draft = await Run(sp => sp.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(new() { CommandId = Id(7),
                CustomerId = customerId, SupplierId = supplierId, OrderDate = new DateTime(6601,10,6), Remark = batch,
                Lines = [new CreateIndependentPurchaseOrderLineCommand { ProductId = productId, Quantity = 1, UnitPrice = 10, TaxPrice = 10 }] }));
            purchaseId = draft.PurchaseOrderId; purchaseLineId = draft.Lines.Single().PurchaseOrderLineId;
            var item = new ProcurementDemandAssociationItem { CommandItemId = Id(10), NetDemandId = nets[first],
                ExpectedDemandVersion = 1, ExpectedSourceDemandVersion = 1, PurchaseOrderLineId = purchaseLineId, ExpectedLineVersion = 1, Quantity = 1 };
            var preview = await Run(sp => sp.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(purchaseId,
                new() { ExpectedVersion = 1, Items = [item] }));
            var beforePurchase = await PurchaseFacts();
            Assert.IsNotNull(beforePurchase);
            var rollback = new RollbackMaterialDemandsCommand { CommandId = Id(8), Reason = "PG两来源集合先回退",
                Items = gross.Select(id => new RollbackMaterialDemandItem { GrossDemandId = id, DecompositionRevision = 1 }).ToList() };
            var associate = new AssociateProcurementDemandsCommand { CommandId = Id(9), ExpectedVersion = 1,
                PreviewToken = preview.Token, Reason = "PG回退后拒绝旧关联预览", Items = [item] };
            using (var holderScope = app.Services.CreateScope())
            using (var rollbackScope = app.Services.CreateScope())
            using (var associateScope = app.Services.CreateScope())
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                var holder = holderScope.ServiceProvider.GetRequiredService<DbSession>();
                var reversing = rollbackScope.ServiceProvider.GetRequiredService<DbSession>();
                var linking = associateScope.ServiceProvider.GetRequiredService<DbSession>();
                pids.AddRange([Pid(holder), Pid(reversing), Pid(linking)]); Assert.AreEqual(3, pids.Distinct().Count());
                var held = await PostgreSqlSourceDemandLock.AcquireBeforeTransactionAsync(holder, [sales[last]], [],
                    Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, deadline.Token); Assert.IsNotNull(held);
                try
                {
                    var reversingTask = rollbackScope.ServiceProvider.GetRequiredService<IMaterialDemandReversalService>().RollbackAsync(rollback, deadline.Token);
                    pending.Add(reversingTask);
                    await Observe("两来源部分准备", reversing, holder, sales[last], () =>
                        reversing.SourceDemandLease is { IsCurrent: true } lease && lease.Covers("Source", sales[first]) && !lease.Covers("Source", sales[last]), deadline.Token);
                    var linkingTask = associateScope.ServiceProvider.GetRequiredService<IProcurementDemandAssociationService>().AssociateAsync(purchaseId, associate, deadline.Token);
                    pending.Add(linkingTask);
                    await Observe("关联在首快照前等待回退", linking, reversing, sales[first], () =>
                        linking.SourceDemandLease is { IsCurrent: true } lease && lease.Covers("CommandItem", Id(10)) && !lease.Covers("Source", sales[first]), deadline.Token);
                    Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>(
                        "SELECT count(*) FROM public.net_demand WHERE uid=ANY(@Ids) AND version=1 AND lifecycle_status=0", new { Ids = nets }));
                    await held.DisposeAsync();
                    var reversed = await reversingTask; var linked = await linkingTask;
                    Assert.IsTrue(reversed.Success, reversed.Error?.Code + ":" + reversed.Message); Assert.AreEqual(2, reversed.Data.RolledBackSourceCount);
                    Assert.IsFalse(linked.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict, linked.Error?.Code, linked.Message);
                    Clean(rollbackScope.ServiceProvider); Clean(associateScope.ServiceProvider);
                    evidence.Add(new { Stage = "回退先提交终态", Reversal = reversed, Association = linked, Pids = pids });
                }
                finally
                {
                    await held.DisposeAsync(); deadline.Cancel();
                    try { await Task.WhenAll(pending); }
                    catch (Exception error) { evidence.Add(new { Stage = "竞争收尾", ErrorType = error.GetType().Name }); }
                    Assert.IsTrue(pending.All(t => t.IsCompleted), "所有已启动请求必须终结。");
                }
            }
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM public.net_demand WHERE uid=ANY(@Ids) AND version=2 AND lifecycle_status=8 AND NOT is_deleted", new { Ids = nets }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM public.demand_execution_link WHERE net_demand_id=ANY(@Ids) OR command_item_id=@Item", new { Ids = nets, Item = Id(10) }));
            Assert.AreEqual(beforePurchase, await PurchaseFacts());
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = Id(8) }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = Id(9) }));
            // 不重执成功业务；新第三作用域重取两个来源/Gross/Net/Item证明部分准备和失败清理。
            using (var probe = app.Services.CreateScope())
            {
                var sp = probe.ServiceProvider;
                await using var lease = await ((IMaterialDemandTransactionPreparation)sp.GetRequiredService<INetDemandExecutionLinkRepository>())
                    .AcquireMaterialBeforeTransactionAsync(sales, gross, nets, [Id(10)]);
                Assert.IsNotNull(lease);
                var session = sp.GetRequiredService<DbSession>();
                Assert.IsTrue(sales.All(id => session.SourceDemandLease!.Covers("Source", id)));
                Assert.IsTrue(gross.All(id => session.SourceDemandLease!.Covers("Gross", id)));
                Assert.IsTrue(session.SourceDemandLease!.Covers("CommandItem", Id(10)));
            }
        }
        finally { Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, SalesIds = sales, SalesLineIds = new[] { Id(1), Id(4) },
            GrossIds = gross, NetIds = nets, PurchaseId = purchaseId, PurchaseLineId = purchaseLineId, Evidence = evidence,
            TasksTerminal = pending.All(t => t.IsCompleted), Pids = pids, Policy = "只failure；所有成功事实及新660110号段留存，不写运行/备份或重置" })); }

        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
        void Clean(IServiceProvider sp)
        { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        async Task<T> Run<T>(Func<IServiceProvider, Task<ServiceResult<T>>> action)
        { using var scope = app.Services.CreateScope(); var result = await action(scope.ServiceProvider); Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message); Clean(scope.ServiceProvider); return result.Data; }
        Task<string?> PurchaseFacts() => observer.Connection.ExecuteScalarAsync<string>("""
            SELECT jsonb_build_object('head',to_jsonb(h),'lines',(SELECT jsonb_agg(to_jsonb(l) ORDER BY uid)
                FROM public.purchase_order_line l WHERE l.purchase_order_id=h.uid))::text FROM public.purchase_order_head h WHERE h.uid=@Id
            """, new { Id = purchaseId });
        // 会话锁采用try-lock轮询，记录真实owner键及未开事务，不伪称pg_blocking_pids有等待边。
        async Task Observe(string stage, DbSession candidate, DbSession owner, Guid source, Func<bool> covered, CancellationToken token)
        {
            var key = PostgreSqlSourceDemandLock.Key("Source", source); var bits = unchecked((ulong)key);
            const string sql = """
                SELECT count(*) FROM pg_stat_activity a WHERE a.pid=@Candidate AND a.xact_start IS NULL AND a.backend_xid IS NULL
                  AND a.query LIKE '%pg_try_advisory_lock%' AND EXISTS(SELECT 1 FROM pg_locks l
                    WHERE l.pid=@Owner AND l.locktype='advisory' AND l.granted AND l.objsubid=1 AND l.classid::bigint=@High AND l.objid::bigint=@Low)
                """;
            var args = new { Candidate = Pid(candidate), Owner = Pid(owner), High = (long)(bits >> 32), Low = (long)(bits & uint.MaxValue) };
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (candidate.Transaction is null && covered() && await observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, args, cancellationToken: token)) == 1) break;
                await Task.Delay(20, token);
            }
            evidence.Add(new { Stage = stage, Candidate = Pid(candidate), Owner = Pid(owner), Source = source, Key = key, TransactionNull = true, ServerTryLockObserved = true });
        }
    }


    /// <summary>真实入库形成新仓余额2，两张原人工出库各2在仓库锁后争用；不足库存及先反入库拒绝，完整反向只处置本批新事实。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeOutboundNegativeStockCompetition()
    {
        const string batch = "9935668e-0c0c-4399-b6e2-46985bdbcc00";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_NEGATIVE_STOCK_BATCH"));
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var supplierId = Guid.Empty; var purchaseLineId = Guid.Empty; var purchaseId = Guid.Empty;
        var customerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f");
        var productId = Guid.Parse("00008dec-d28a-4036-bc06-da0a2159e16b");
        var date = new DateTime(6506, 10, 6);
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.supplier WHERE supplier_code='PGG49935668E'"));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.warehouse_site WHERE code='PG-G4-9935668e'"));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='650610'"));
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u WHERE u.is_active AND NOT u.is_deleted
            AND rtrim(u.user_name,' ') COLLATE public.erp_dictionary_ci='dv' COLLATE public.erp_dictionary_ci
            AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=u.uid
                AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        var evidence = new List<object>(); var warehouseId = Guid.Empty; var inboundId = Guid.Empty;
        var outboundA = Guid.Empty; var outboundB = Guid.Empty; var balanceId = Guid.Empty;
        var pids = new List<int>(); var competing = new List<Task<ServiceResult<OutboundPostResult>>>();
        try
        {
            // 原规则一供应商一活动虚拟仓；本批先用原服务创建专用供应商及独立采购，不改或借用旧余额。
            var supplier = await Run(sp => sp.GetRequiredService<SwiftExport.Application.Interfaces.Suppliers.Management.ISupplierService>().AddAsync(
                new CreateItemRequest<SwiftExport.Contracts.DTOs.Suppliers.SupplierDto> { ClientKey = batch, Data = new() {
                    Version = 1, SupplierCode = "PGG49935668E", SupplierName = "PG负库存专用供应商9935668e", DocRelateNo = "PG-G4", Currency = "CNY" } }));
            supplierId = supplier.Uid!.Value;
            warehouseId = (await Run(sp => sp.GetRequiredService<SwiftExport.Application.Interfaces.Inventory.Warehouses.IWarehouseManagementService>()
                .CreateSupplierVirtualWarehouseAsync(new() { CommandId = Id(0), SupplierId = supplierId, Code = "PG-G4-9935668e", Name = "PG负库存竞争专用仓9935668e" }))).Uid;
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_balance WHERE warehouse_site_id=@Id", new { Id = warehouseId }));
            var purchase = await Run(sp => sp.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(new() { CommandId = Id(10),
                CustomerId = customerId, SupplierId = supplierId, OrderDate = date, Remark = batch,
                Lines = [new CreateIndependentPurchaseOrderLineCommand { ProductId = productId, Quantity = 2, UnitPrice = 10, TaxPrice = 10 }] }));
            purchaseId = purchase.PurchaseOrderId; purchaseLineId = purchase.Lines.Single().PurchaseOrderLineId;
            var confirmation = await Run(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(purchaseId));
            await Run(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(purchaseId,
                new() { CommandId = Id(11), ExpectedVersion = 1, ContextFingerprint = confirmation.Fingerprint }));
            inboundId = (await Run(sp => sp.GetRequiredService<IInboundDraftService>().CreateAsync(new() { CommandId = Id(1),
                Draft = new InboundDraftData { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, WarehouseSiteId = warehouseId, ReceiptDate = date,
                    Remark = batch, Lines = [new InboundDraftLineInput { SourceLineId = purchaseLineId, Quantity = 2 }] } }))).Uid;
            await Run(sp => sp.GetRequiredService<IInboundPostingService>().PostAsync(inboundId, new() { CommandId = Id(2), ExpectedVersion = 1 }));
            balanceId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.inventory_balance WHERE warehouse_site_id=@Id AND product_id=@Product", new { Id = warehouseId, Product = productId });
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=2 AND on_hand_qty=2 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            outboundA = (await CreateOutbound(Id(3))).Uid; outboundB = (await CreateOutbound(Id(4))).Uid;
            foreach (var id in new[] { outboundA, outboundB })
            {
                var preview = await Run(sp => sp.GetRequiredService<IOutboundPostingService>().PreviewPostAsync(id, new() { ExpectedVersion = 1 }));
                Assert.IsTrue(preview.CanPost, Serialize(preview)); Assert.AreEqual(2m, preview.StockImpacts.Single().CurrentQuantity);
                evidence.Add(new { Stage = "原预览", Id = id, Preview = preview });
            }
            // 新仓FOR UPDATE只作真实第三连接屏障；两个原Post已读到可用2后依次等待，未改生产锁/隔离或加入重试。
            using (var holder = new DbSession(new PostgreSqlConnectionFactory(connection)))
            using (var firstScope = app.Services.CreateScope())
            using (var secondScope = app.Services.CreateScope())
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                var first = firstScope.ServiceProvider; var second = secondScope.ServiceProvider;
                var firstSession = first.GetRequiredService<DbSession>(); var secondSession = second.GetRequiredService<DbSession>();
                var firstPid = Pid(firstSession); var secondPid = Pid(secondSession);
                pids.AddRange([Pid(holder), firstPid, secondPid]); Assert.AreEqual(3, pids.Distinct().Count());
                holder.Transaction = holder.Connection.BeginTransaction();
                await holder.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.warehouse_site WHERE uid=@Id FOR UPDATE", new { Id = warehouseId }, holder.Transaction);
                try
                {
                    var firstTask = first.GetRequiredService<IOutboundPostingService>().PostAsync(outboundA, new() { CommandId = Id(5), ExpectedVersion = 1 }, deadline.Token);
                    competing.Add(firstTask); await Observe([firstPid], deadline.Token);
                    var secondTask = second.GetRequiredService<IOutboundPostingService>().PostAsync(outboundB, new() { CommandId = Id(6), ExpectedVersion = 1 }, deadline.Token);
                    competing.Add(secondTask); await Observe([firstPid, secondPid], deadline.Token);
                    Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT on_hand_qty=2 AND version=2 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
                    holder.Transaction.Commit(); holder.Transaction.Dispose(); holder.Transaction = null;
                    var results = await Task.WhenAll(competing);
                    Assert.IsTrue(results[0].Success, results[0].Message);
                    Assert.IsFalse(results[1].Success); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, results[1].Error?.Code, results[1].Message);
                    Clean(first); Clean(second); evidence.Add(new { Stage = "真实竞争终态", First = results[0], Second = results[1], Pids = pids });
                }
                finally
                {
                    if (holder.Transaction is not null) { holder.Transaction.Rollback(); holder.Transaction.Dispose(); holder.Transaction = null; }
                    deadline.Cancel();
                    foreach (var task in competing) { try { await task; } catch (OperationCanceledException) { } }
                    Assert.IsTrue(competing.All(x => x.IsCompleted), "所有竞争请求必须终结，不能遗留后台写入。");
                }
            }
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=3 AND on_hand_qty=0 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE warehouse_site_id=@Id", new { Id = warehouseId }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=1 AND posting_status=0 FROM public.outbound_document WHERE uid=@Id", new { Id = outboundB }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = Id(6) }));
            // 余额已被合法出库耗尽时，原入库反向必须拒绝，不伪造采购状态或直接补库存。
            var beforeRejectedReverse = await Snapshot();
            using (var scope = app.Services.CreateScope())
            {
                var rejected = await scope.ServiceProvider.GetRequiredService<IInboundReversalService>().ReverseAsync(inboundId,
                    new() { CommandId = Id(9), ExpectedVersion = 2, Reason = "PG不足库存原入库反向应拒绝" });
                Assert.IsFalse(rejected.Success); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, rejected.Error?.Code, rejected.Message); Clean(scope.ServiceProvider);
                Assert.AreEqual(beforeRejectedReverse, await Snapshot()); evidence.Add(new { Stage = "原入库反向不足库存拒绝", Result = rejected, Snapshot = await Snapshot() });
            }
            await Run(sp => sp.GetRequiredService<IOutboundReversalService>().ReverseAsync(outboundA,
                new() { CommandId = Id(7), ExpectedVersion = 2, Reason = "PG本批出库原完整反向" }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=4 AND on_hand_qty=2 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            await Run(sp => sp.GetRequiredService<IInboundReversalService>().ReverseAsync(inboundId,
                new() { CommandId = Id(8), ExpectedVersion = 2, Reason = "PG本批入库原完整反向" }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=5 AND on_hand_qty=0 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE warehouse_site_id=@Id", new { Id = warehouseId }));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry r JOIN public.inventory_ledger_entry o ON o.uid=r.reversal_of_ledger_id WHERE r.warehouse_site_id=@Id AND r.quantity_delta=-o.quantity_delta", new { Id = warehouseId }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(6), Id(9) } }));
            evidence.Add(new { Stage = "原正反向终态", Snapshot = await Snapshot(), LedgerCount = 4, BalanceVersion = 5, BalanceQuantity = 0 });
        }
        finally { Console.WriteLine(Serialize(new { Batch = batch, WarehouseId = warehouseId, InboundId = inboundId, OutboundA = outboundA,
            OutboundB = outboundB, BalanceId = balanceId, PurchaseLineId = purchaseLineId, PurchaseId = purchaseId, SupplierId = supplierId, ProductId = productId, Pids = pids,
            TasksTerminal = competing.All(x => x.IsCompleted), Evidence = evidence,
            Policy = "只新仓/新入出库草稿和本批新余额流水原反向；52454旧行全部保护，只本批新独立采购，无重置/DDL/运行或备份库写入" })); }
        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
        string Serialize(object value) => System.Text.Json.JsonSerializer.Serialize(value);
        void Clean(IServiceProvider sp) { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        async Task<T> Run<T>(Func<IServiceProvider, Task<ServiceResult<T>>> action)
        { using var scope = app.Services.CreateScope(); var result = await action(scope.ServiceProvider); Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message); Clean(scope.ServiceProvider); return result.Data; }
        Task<OutboundDraftCommandResult> CreateOutbound(Guid id) => Run(sp => sp.GetRequiredService<IOutboundDraftService>().CreateAsync(new() {
            CommandId = id, Draft = new OutboundDraftData { DocumentType = OutboundDocumentTypeEnum.StockIssue, WarehouseSiteId = warehouseId,
                ShipmentDate = date, IssueReason = "PG本批负库存竞争", Remark = batch,
                Lines = [new OutboundDraftLineInput { ProductId = productId, Quantity = 2 }] } }));
        // 查询真实服务器阻塞图与仓库行锁关系，单次期限只约束此两候选，不用固定睡眠猜测执行次序。
        async Task Observe(int[] candidates, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var count = await observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT count(*) FROM pg_stat_activity a WHERE a.pid=ANY(@Pids) AND cardinality(pg_blocking_pids(a.pid))>0
                    AND a.xact_start IS NOT NULL AND EXISTS(SELECT 1 FROM pg_locks l WHERE l.pid=a.pid
                        AND l.locktype='relation' AND l.relation='public.warehouse_site'::regclass AND l.mode='RowShareLock')
                    """, new { Pids = candidates }, cancellationToken: token));
                if (count == candidates.Length)
                {
                    var graph = await observer.Connection.ExecuteScalarAsync<string>("SELECT jsonb_agg(jsonb_build_object('pid',pid,'blocking',pg_blocking_pids(pid),'xact_start',xact_start,'query',query))::text FROM pg_stat_activity WHERE pid=ANY(@Pids)", new { Pids = candidates });
                    evidence.Add(new { Stage = "真实库存等待", Candidates = candidates, Graph = graph }); return;
                }
                await Task.Delay(20, token);
            }
        }
        // 只对失败反向证明全50表零增量；主控另排除精确新增UID验证全部原数据。
        async Task<string> Snapshot() => (await observer.Connection.ExecuteScalarAsync<string>("""
            SELECT md5(string_agg(table_name||':'||row_count||':'||data_md5,chr(10) ORDER BY table_name)) FROM (SELECT c.relname table_name,(xpath('/row/row_count/text()',x.doc))[1]::text row_count,(xpath('/row/data_md5/text()',x.doc))[1]::text data_md5 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace CROSS JOIN LATERAL (SELECT query_to_xml(format('SELECT count(*) AS row_count, md5(coalesce(string_agg(to_jsonb(t)::text, chr(10) ORDER BY to_jsonb(t)::text), '''')) AS data_md5 FROM public.%I t',c.relname),false,true,'') doc) x WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname) facts
            """))!;
    }


    /// <summary>实际Excel销售和箱单检查复用迁入客户产品；箱单草稿独立重验、保留NULL包装，并由另一PG连接重放原成功命令。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeExcelSalesPackingCheckAndCreate()
    {
        const string batch = "269d6921-0c54-4e90-8582-1f00a458c600";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_EXCEL_SALES_PACKING_BATCH"));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        var customer = Guid.Parse("1310ce86-31f8-4c65-8efa-6205300427fd");
        var relation = Guid.Parse("000090cb-65f1-4c3c-b35a-04fe486bd774");
        var product = Guid.Parse("25fa26e4-675f-45f8-8f77-f36066eb2e39");
        var date = new DateTime(6505, 10, 5);
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='650510'"));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_head WHERE uid=@Id", new { Id = Id(40) }));
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u WHERE u.is_active AND NOT u.is_deleted
            AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=u.uid
                AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = PostgreSqlIntegrationEnvironment.GetFailureConnectionString() });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        using var firstScope = app.Services.CreateScope(); using var secondScope = app.Services.CreateScope();
        var first = firstScope.ServiceProvider; var second = secondScope.ServiceProvider;
        var firstPid = ((NpgsqlConnection)first.GetRequiredService<DbSession>().Connection).ProcessID;
        var secondPid = ((NpgsqlConnection)second.GetRequiredService<DbSession>().Connection).ProcessID;
        Assert.AreNotEqual(firstPid, secondPid);
        var sales = new SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.CheckSalesOrderImportRequest {
            CustomerCode = " 3086-aq ", Header = new() { OrderDate = date, Currency = "CNY", ExchangeRate = 1,
                InvoiceNo = "PG-M26-269d6921", CustomerPoNo = "PG-M26-269d6921" },
            Lines = [new() { ClientLineId = Id(50), CustomerSku = " srk6504mb ", SelectedCustomerProductId = relation, Qty = 1 },
                new() { ClientLineId = Id(51), IsNonStandard = true, DescriptionEn = "PG导入非标", Unit = "Pcs", Qty = 1 }] };
        var packing = new SwiftExport.Contracts.DTOs.ForExcel.PackingLists.CheckPackingListImportRequest {
            Header = new() { CustomerId = customer, InvoiceDate = date, Note = batch, ContainerNo = " PG箱号 " },
            Lines = [new() { Uid = Id(41), LineNumber = 1, ProductId = product, SelectedCustomerProductId = relation,
                    CustomerSkuSnapshot = " srk6504mb ", UnitSnapshot = "Pcs", Qty = 1, PackageCount = 99 },
                new() { Uid = Id(42), LineNumber = 2, IsNonStandardSnapshot = true, DescriptionEnSnapshot = " PG导入非标 ",
                    UnitSnapshot = " Pcs ", Qty = 1, PackageCount = 99 }] };
        var before = await Snapshot(); var evidence = new List<object>();
        try
        {
            var checkedSales = await CheckSales();
            Assert.IsTrue(checkedSales.CanSave, Serialize(checkedSales)); Assert.AreEqual(customer, checkedSales.Customer.Uid);
            Assert.AreEqual("Matched", checkedSales.Lines[0].MatchStatus); Assert.AreEqual(product, checkedSales.Lines[0].Product.ProductId);
            Assert.AreEqual(relation, checkedSales.Lines[0].Product.CustomerProductId);
            Assert.AreEqual("NonStandard", checkedSales.Lines[1].MatchStatus); Assert.IsNull(checkedSales.Lines[1].Product);
            Assert.AreEqual(0, checkedSales.ExistingOrders.Count);
            sales.Lines[0].SelectedCustomerProductId = Id(52);
            var invalidSales = await CheckSales(); Assert.IsFalse(invalidSales.CanSave);
            Assert.IsTrue(invalidSales.Lines[0].Issues.Any(x => x.Code == "SelectionInvalid"));
            sales.Lines[0].SelectedCustomerProductId = relation;
            var checkedPacking = await CheckPacking(); Assert.IsTrue(checkedPacking.CanSave, Serialize(checkedPacking));
            Assert.AreEqual(product, checkedPacking.Lines[0].Product.ProductId); Assert.AreEqual("NonStandard", checkedPacking.Lines[1].MatchStatus);
            // 正式创建必须再次读取客户产品；拒绝发生在取号之后时，整个原事务也不得留下号段或回执。
            packing.Lines[0].SelectedCustomerProductId = Id(52);
            var invalidPacking = await CheckPacking(); Assert.IsFalse(invalidPacking.CanSave);
            Assert.IsTrue(invalidPacking.Lines[0].Issues.Any(x => x.Code == "ProductNotMatched"));
            var rejected = await first.GetRequiredService<IPackingListDraftService>().CreateAsync(Command(Id(1)));
            Assert.IsFalse(rejected.Success); Assert.AreEqual(PackingListErrorCodes.InvalidDraft, rejected.Error?.Code, rejected.Message); Clean(first);
            Assert.AreEqual(before, await Snapshot());
            evidence.Add(new { Stage = "正式重验拒绝", Result = rejected, Snapshot = await Snapshot() });
            packing.Lines[0].SelectedCustomerProductId = relation; packing.Lines[0].OriginalLinkSku = "PG-M26-269d6921";
            var mismatch = await CheckPacking(); Assert.IsFalse(mismatch.CanSave);
            Assert.IsTrue(mismatch.Lines[0].Issues.Any(x => x.Code == "LinkSkuMismatch" && !x.Acknowledged));
            packing.Lines[0].AcceptLinkSkuMismatch = true;
            Assert.IsTrue((await CheckPacking()).CanSave);
            Assert.AreEqual(before, await Snapshot());
            var command = Command(Id(2));
            var saved = await first.GetRequiredService<IPackingListDraftService>().CreateAsync(command);
            Assert.IsTrue(saved.Success, saved.Error?.Code + ":" + saved.Message); Assert.AreEqual(Id(40), saved.Data.Uid); Clean(first);
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("""
                SELECT version=1 AND customer_id=@Customer AND invoice_date=@Date AND container_no='PG箱号'
                    AND lifecycle_status=0 AND posting_status=0 AND create_command_id=@Command AND post_command_id IS NULL
                FROM public.packing_list_head WHERE uid=@Id
                """, new { Id = Id(40), Customer = customer, Date = date, Command = Id(2) }));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("""
                SELECT count(*) FROM public.packing_list_line WHERE packing_list_head_id=@Id AND version=1 AND qty=1
                    AND gross_demand_id IS NULL AND packing_group_id IS NULL AND pcs_per_package IS NULL AND package_count IS NULL
                    AND nw_per_package IS NULL AND gw_per_package IS NULL AND cbm_per_package IS NULL
                    AND sub_total_nw IS NULL AND sub_total_gw IS NULL AND sub_total_cbm IS NULL AND unit_snapshot='Pcs'
                """, new { Id = Id(40) }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT product_id=@Product AND NOT is_non_standard_snapshot AND sku_snapshot='SRK6504MB' AND customer_sku_snapshot='srk6504mb' FROM public.packing_list_line WHERE uid=@Id", new { Id = Id(41), Product = product }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT product_id IS NULL AND is_non_standard_snapshot AND description_en_snapshot='PG导入非标' FROM public.packing_list_line WHERE uid=@Id", new { Id = Id(42) }));
            var after = await Snapshot();
            var replay = await second.GetRequiredService<IPackingListDraftService>().CreateAsync(command);
            Assert.IsTrue(replay.Success, replay.Message); Assert.IsTrue(replay.Data.AlreadyApplied); Assert.AreEqual(Id(40), replay.Data.Uid); Clean(second);
            Assert.AreEqual(after, await Snapshot());
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = Id(1) }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = Id(2) }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT current_no FROM public.document_sequence WHERE doc_type='PL' AND year_month='650510'"));
            evidence.Add(new { Stage = "正式创建及另一backend原输入重放", Result = saved, Replay = replay, After = after, AfterReplay = await Snapshot() });
        }
        finally { Console.WriteLine(Serialize(new { Batch = batch, Customer = customer, Relation = relation, Product = product, PackingListId = Id(40),
            FirstPid = firstPid, SecondPid = secondPid, Before = before, Evidence = evidence,
            Policy = "只保留新650510未关联箱单头两行/号段/日志/回执；旧事实及主数据只读，无库存/交付/DDL/重置/运行或备份库写入" })); }
        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        string Serialize(object value) => System.Text.Json.JsonSerializer.Serialize(value);
        void Clean(IServiceProvider sp) { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        CreatePackingListDraftCommand Command(Guid id) => new() { CommandId = id, Uid = Id(40), Header = packing.Header, Lines = packing.Lines };
        // 检查不能回写调用方输入，也不能自动把NULL商业值或包装展示值补成事实。
        async Task<SwiftExport.Contracts.DTOs.ForExcel.SalesOrders.CheckSalesOrderImportResponse> CheckSales()
        {
            var input = Serialize(sales); var result = await first.GetRequiredService<IForExcelSalesOrderService>().CheckAsync(sales);
            Assert.IsTrue(result.Success, result.Message); Assert.AreEqual(input, Serialize(sales)); Clean(first);
            evidence.Add(new { Stage = "销售检查", Result = result }); return result.Data;
        }
        async Task<SwiftExport.Contracts.DTOs.ForExcel.PackingLists.CheckPackingListImportResponse> CheckPacking()
        {
            var input = Serialize(packing); var result = await first.GetRequiredService<IForExcelPackingListService>().CheckAsync(packing);
            Assert.IsTrue(result.Success, result.Message); Assert.AreEqual(input, Serialize(packing)); Clean(first);
            evidence.Add(new { Stage = "箱单检查", Result = result }); return result.Data;
        }
        // 全部50表完整字段摘要证明拒绝与重放零增量；执行后的精确新增UID排除另由主控比对全部旧行。
        async Task<string> Snapshot()
        {
            var hash = await observer.Connection.ExecuteScalarAsync<string>("""
                SELECT md5(string_agg(table_name||':'||row_count||':'||data_md5,chr(10) ORDER BY table_name)) FROM (SELECT c.relname table_name,(xpath('/row/row_count/text()',x.doc))[1]::text row_count,(xpath('/row/data_md5/text()',x.doc))[1]::text data_md5 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace CROSS JOIN LATERAL (SELECT query_to_xml(format('SELECT count(*) AS row_count, md5(coalesce(string_agg(to_jsonb(t)::text, chr(10) ORDER BY to_jsonb(t)::text), '''')) AS data_md5 FROM public.%I t',c.relname),false,true,'') doc) x WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname) facts
                """); Assert.IsNotNull(hash); return hash;
        }
    }

    /// <summary>接回已真实确认的本批售后，不重跑保存/确认；核对两CR来源及装箱投影后，原安全取消与另一backend重放。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeAfterSalesConfirmedSourceExit()
    {
        const string batch = "18bd9715-a372-4d43-9569-76f5cd25c500";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_AFTERSALES_BATCH"));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u WHERE u.is_active AND NOT u.is_deleted
            AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=u.uid
                AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid
            """);
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=3 AND confirmation_status=1 FROM public.after_sales_header WHERE uid=@Id", new { Id = Id(40) }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = Id(4) }));
        var grossIds = new[] { Guid.Parse("3b1dfded-b862-4a31-be60-4a35034e9015"), Guid.Parse("01f1d0aa-4733-4254-aeef-4a35034e901d") };
        Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.gross_demand WHERE uid=ANY(@Ids) AND source_uid=@Id AND source_type='CR' AND source_version=3 AND customer_id='1310ce86-31f8-4c65-8efa-6205300427fd' AND demand_qty=1 AND unit_snapshot='Pcs' AND required_date=timestamp '6504-11-19' AND lifecycle_status=0", new { Ids = grossIds, Id = Id(40) }));
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT product_id='25fa26e4-675f-45f8-8f77-f36066eb2e39' AND NOT is_non_standard AND sku_snapshot='SRK6504MB' FROM public.gross_demand WHERE uid=@Id", new { Id = grossIds[0] }));
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT product_id IS NULL AND is_non_standard AND description_snapshot='PG售后非标补件' FROM public.gross_demand WHERE uid=@Id", new { Id = grossIds[1] }));
        var sourceBeforeExit = (await observer.Connection.QueryAsync<string>("SELECT to_jsonb(t)::text FROM public.gross_demand t WHERE uid=ANY(@Ids) ORDER BY uid", new { Ids = grossIds })).ToArray();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = PostgreSqlIntegrationEnvironment.GetFailureConnectionString() });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build(); using var firstScope = app.Services.CreateScope(); using var secondScope = app.Services.CreateScope();
        var first = firstScope.ServiceProvider; var second = secondScope.ServiceProvider;
        var firstPid = ((NpgsqlConnection)first.GetRequiredService<DbSession>().Connection).ProcessID;
        var secondPid = ((NpgsqlConnection)second.GetRequiredService<DbSession>().Connection).ProcessID; Assert.AreNotEqual(firstPid, secondPid);
        var enrichments = await first.GetRequiredService<IPackingListQueryRepository>().GetSourceEnrichmentsAsync(grossIds);
        Assert.AreEqual(2, enrichments.Count); Assert.IsTrue(enrichments.All(x => x.CustomerPoNo == "PG-M25-18bd9715"));
        var command = new CancelAfterSalesDemandConfirmationCommand { CommandId = Id(4), Version = 3, Reason = "本批来源退出验收" };
        var result = await first.GetRequiredService<IAfterSalesCancellationService>().CancelDemandConfirmationAsync(Id(40), command);
        Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message); Assert.IsFalse(result.Data.AlreadyApplied); Clean(first);
        Assert.AreEqual(4, result.Data.Version); Assert.AreEqual(ConfirmationStatusEnum.待确认, result.Data.ConfirmationStatus);
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.gross_demand WHERE source_uid=@Id", new { Id = Id(40) }));
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=4 AND confirmation_status=0 AND demand_confirmed_at IS NULL AND demand_confirmed_by IS NULL AND service_amount_cny=0 AND local_file_folder='PG-M25资料路径' FROM public.after_sales_header WHERE uid=@Id", new { Id = Id(40) }));
        Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.after_sales_line WHERE header_id=@Id AND version=1 AND quantity=1 AND unit_snapshot='Pcs'", new { Id = Id(40) }));
        var after = await Snapshot();
        var replay = await second.GetRequiredService<IAfterSalesCancellationService>().CancelDemandConfirmationAsync(Id(40), command);
        Assert.IsTrue(replay.Success, replay.Message); Assert.IsTrue(replay.Data.AlreadyApplied); Assert.AreEqual(4, replay.Data.Version); Clean(second);
        Assert.AreEqual(after, await Snapshot());
        Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(1), Id(2), Id(3), Id(4) } }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = Id(5) }));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, FirstRun = false, AfterSalesId = Id(40), GrossIds = grossIds, SourceBeforeExit = sourceBeforeExit,
            Enrichments = enrichments, FirstPid = firstPid, SecondPid = secondPid, Result = result, Replay = replay, After = after, AfterReplay = await Snapshot() }));
        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        void Clean(IServiceProvider sp) { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        // 原成功取消重放只能返回历史结果，不再次处置来源或写日志。
        async Task<string> Snapshot()
        {
            var hash = await observer.Connection.ExecuteScalarAsync<string>("""
                SELECT md5(string_agg(table_name||':'||row_count||':'||data_md5,chr(10) ORDER BY table_name)) FROM (SELECT c.relname table_name,(xpath('/row/row_count/text()',x.doc))[1]::text row_count,(xpath('/row/data_md5/text()',x.doc))[1]::text data_md5 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace CROSS JOIN LATERAL (SELECT query_to_xml(format('SELECT count(*) AS row_count, md5(coalesce(string_agg(to_jsonb(t)::text, chr(10) ORDER BY to_jsonb(t)::text), '''')) AS data_md5 FROM public.%I t',c.relname),false,true,'') doc) x WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname) facts
                """); Assert.IsNotNull(hash); return hash;
        }
    }

    /// <summary>原售后服务保存标准及非标补件，确认接收CR来源并安全取消；未定成本拒绝、成功回执重放与旧数据保护实际留证。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeAfterSalesSaveConfirmCancel()
    {
        const string batch = "18bd9715-a372-4d43-9569-76f5cd25c500";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_AFTERSALES_BATCH"));
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var customer = Guid.Parse("1310ce86-31f8-4c65-8efa-6205300427fd");
        var supplier = Guid.Parse("40c4eb8d-f3c8-4ef2-a37e-4a35030e8ea3");
        var product = Guid.Parse("25fa26e4-675f-45f8-8f77-f36066eb2e39");
        var date = new DateTime(6504, 10, 5); var evidence = new List<object>(); Guid[] grossIds = [];
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.after_sales_header WHERE uid=@Id", new { Id = Id(40) }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='650410'"));
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u WHERE u.is_active AND NOT u.is_deleted
            AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=u.uid
                AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        using var firstScope = app.Services.CreateScope(); using var secondScope = app.Services.CreateScope();
        var first = firstScope.ServiceProvider; var second = secondScope.ServiceProvider;
        var firstPid = ((NpgsqlConnection)first.GetRequiredService<DbSession>().Connection).ProcessID;
        var secondPid = ((NpgsqlConnection)second.GetRequiredService<DbSession>().Connection).ProcessID;
        Assert.AreNotEqual(firstPid, secondPid);
        var create = new SaveAfterSalesCommand { CommandId = Id(1), Uid = Id(40),
            Header = new() { CustomerId = customer, AcceptedDate = date, ResponsibleSupplierId = supplier, IssueProductId = product,
                CustomerComplaintNo = " PG-M25-18bd9715 ", IssueDescription = "PG原售后流程补件", ServiceAmountCny = null,
                RequiredDate = date.AddDays(45), ReturnedReceivedDate = date.AddDays(-1), LocalFileFolder = " PG-M25资料路径 ", Remark = batch },
            AddedLines = [new() { Uid = Id(41), ProductId = product, Quantity = 1 },
                new() { Uid = Id(42), IsNonStandard = true, DescriptionSnapshot = "PG售后非标补件", UnitSnapshot = "Pcs", Quantity = 1 }] };
        try
        {
            var saved = await Pair(sp => sp.GetRequiredService<IAfterSalesMaintenanceService>().SaveAsync(Id(40), create, true), "保存未定成本草稿");
            Assert.AreEqual(1, saved.Version);
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.gross_demand WHERE source_uid=@Id", new { Id = Id(40) }));
            var beforeReject = await Snapshot();
            var rejected = await first.GetRequiredService<IAfterSalesConfirmationService>().ConfirmDemandAsync(Id(40), new() { CommandId = Id(5), Version = 1 });
            Assert.IsFalse(rejected.Success); Assert.AreEqual("VALIDATION", rejected.Error?.Code, rejected.Message);
            Clean(first); Assert.AreEqual(beforeReject, await Snapshot()); evidence.Add(new { Stage = "未定成本确认拒绝零事实增量", Result = rejected, Before = beforeReject, After = await Snapshot() });
            var detail = await first.GetRequiredService<IAfterSalesQueryService>().GetAsync(Id(40)); Assert.IsTrue(detail.Success, detail.Message);
            Assert.AreEqual(2, detail.Data.Lines.Count); Assert.IsNull(detail.Data.Header.ServiceAmountCny);
            Assert.AreEqual(date.AddDays(45), detail.Data.Header.RequiredDate); Assert.AreEqual(date.AddDays(-1), detail.Data.Header.ReturnedReceivedDate);
            var listed = await first.GetRequiredService<IAfterSalesQueryService>().SearchAsync(new() { CustomerId = customer, Keyword = "PG-M25-18bd9715" });
            Assert.IsTrue(listed.Success, listed.Message); Assert.AreEqual(1, listed.Data.TotalCount);
            var header = detail.Data.Header.Adapt<AfterSalesEditHeaderDto>(); header.ServiceAmountCny = 0;
            var updated = await Pair(sp => sp.GetRequiredService<IAfterSalesMaintenanceService>().SaveAsync(Id(40),
                new() { CommandId = Id(2), Uid = Id(40), Version = 1, Header = header }, false), "明确免费并保存");
            Assert.AreEqual(2, updated.Version);
            var confirmed = await Pair(sp => sp.GetRequiredService<IAfterSalesConfirmationService>().ConfirmDemandAsync(Id(40),
                new() { CommandId = Id(3), Version = 2 }), "确认标准及非标实物来源");
            Assert.AreEqual(3, confirmed.Version); Assert.AreEqual(ConfirmationStatusEnum.已确认, confirmed.ConfirmationStatus);
            grossIds = (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.gross_demand WHERE source_type='CR' AND source_uid=@Id AND NOT is_deleted", new { Id = Id(40) })).ToArray();
            Assert.AreEqual(2, grossIds.Length);
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.gross_demand WHERE source_uid=@Id AND source_type='CR' AND source_version=3 AND customer_id=@Customer AND demand_qty=1 AND unit_snapshot='Pcs' AND lifecycle_status=0", new { Id = Id(40), Customer = customer }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT product_id=@Product AND NOT is_non_standard AND sku_snapshot='SRK6504MB' FROM public.gross_demand WHERE source_uid=@Id AND source_line_id=@Line", new { Id = Id(40), Line = Id(41), Product = product }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT product_id IS NULL AND is_non_standard AND description_snapshot='PG售后非标补件' FROM public.gross_demand WHERE source_uid=@Id AND source_line_id=@Line", new { Id = Id(40), Line = Id(42) }));
            var enrichments = await first.GetRequiredService<IPackingListQueryRepository>().GetSourceEnrichmentsAsync(grossIds);
            Assert.AreEqual(2, enrichments.Count); Assert.IsTrue(enrichments.All(x => x.CustomerPoNo == "PG-M25-18bd9715"));
            var sourceBeforeExit = (await observer.Connection.QueryAsync<string>("SELECT to_jsonb(t)::text FROM public.gross_demand t WHERE uid=ANY(@Ids) ORDER BY uid", new { Ids = grossIds })).ToArray();
            evidence.Add(new { Stage = "原服务生成的两CR来源/交期/单位及装箱投影", GrossIds = grossIds, GrossFacts = sourceBeforeExit, Enrichments = enrichments });
            // 仅原取消服务处置本批新建且从未拆解/交付的Gross；不删除任何旧来源或重置数据。
            var cancelled = await Pair(sp => sp.GetRequiredService<IAfterSalesCancellationService>().CancelDemandConfirmationAsync(Id(40),
                new() { CommandId = Id(4), Version = 3, Reason = "本批来源退出验收" }), "安全取消本批确认");
            Assert.AreEqual(4, cancelled.Version); Assert.AreEqual(ConfirmationStatusEnum.待确认, cancelled.ConfirmationStatus);
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.gross_demand WHERE source_uid=@Id", new { Id = Id(40) }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=4 AND confirmation_status=0 AND demand_confirmed_at IS NULL AND demand_confirmed_by IS NULL AND service_amount_cny=0 AND local_file_folder='PG-M25资料路径' FROM public.after_sales_header WHERE uid=@Id", new { Id = Id(40) }));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.after_sales_line WHERE header_id=@Id AND version=1 AND quantity=1 AND unit_snapshot='Pcs'", new { Id = Id(40) }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = Id(5) }));
            Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(1), Id(2), Id(3), Id(4) } }));
        }
        finally { Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, AfterSalesId = Id(40), LineIds = new[] { Id(41), Id(42) }, GrossIds = grossIds,
            FirstPid = firstPid, SecondPid = secondPid, Evidence = evidence, Policy = "原服务新650410售后草稿/号段/日志/回执，原取消只删除本批两个新且未拆解来源，旧52435行全保护；无主数据/DDL/运行或备份写入" })); }
        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        void Clean(IServiceProvider sp) { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        // 不并发扩大矩阵；每个既有命令在两个不同backend执行一次，并证明成功回执重放零增量。
        async Task<AfterSalesCommandResult> Pair(Func<IServiceProvider, Task<ServiceResult<AfterSalesCommandResult>>> action, string stage)
        {
            var result = await action(first); Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message); Assert.IsFalse(result.Data.AlreadyApplied); Clean(first);
            var beforeReplay = await Snapshot(); var replay = await action(second);
            Assert.IsTrue(replay.Success, replay.Error?.Code + ":" + replay.Message); Assert.IsTrue(replay.Data.AlreadyApplied); Clean(second);
            Assert.AreEqual(result.Data.Uid, replay.Data.Uid); Assert.AreEqual(result.Data.Version, replay.Data.Version); Assert.AreEqual(beforeReplay, await Snapshot());
            evidence.Add(new { Stage = stage, Result = result, Replay = replay, BeforeReplay = beforeReplay, AfterReplay = await Snapshot() }); return result.Data;
        }
        // 全50表现有字段摘要比较拒绝与重放零增量，后续另以精确新增UID比对全部旧行。
        async Task<string> Snapshot()
        {
            var hash = await observer.Connection.ExecuteScalarAsync<string>("""
                SELECT md5(string_agg(table_name||':'||row_count||':'||data_md5,chr(10) ORDER BY table_name)) FROM (SELECT c.relname table_name,(xpath('/row/row_count/text()',x.doc))[1]::text row_count,(xpath('/row/data_md5/text()',x.doc))[1]::text data_md5 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace CROSS JOIN LATERAL (SELECT query_to_xml(format('SELECT count(*) AS row_count, md5(coalesce(string_agg(to_jsonb(t)::text, chr(10) ORDER BY to_jsonb(t)::text), '''')) AS data_md5 FROM public.%I t',c.relname),false,true,'') doc) x WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname) facts
                """);
            Assert.IsNotNull(hash); return hash;
        }
    }

    /// <summary>以迁入的真实客户产品关系验证Excel采购检查及正式创建重验；仅保留新草稿，拒绝与成功重放均不得追加事实。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeExcelPurchaseCheckAndCreate()
    {
        const string batch = "96079021-c968-4768-932f-28d01a0ec400";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_EXCEL_PURCHASE_BATCH"));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        var customer = Guid.Parse("1310ce86-31f8-4c65-8efa-6205300427fd");
        var supplier = Guid.Parse("40c4eb8d-f3c8-4ef2-a37e-4a35030e8ea3");
        var relation = Guid.Parse("000090cb-65f1-4c3c-b35a-04fe486bd774");
        var product = Guid.Parse("25fa26e4-675f-45f8-8f77-f36066eb2e39");
        var date = new DateTime(6503, 10, 5);
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='650310'"));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_head WHERE bo_no='EXCEL-PG-96079021'"));
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u WHERE u.is_active AND NOT u.is_deleted
            AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=u.uid
                AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = PostgreSqlIntegrationEnvironment.GetFailureConnectionString() });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        using var firstScope = app.Services.CreateScope(); using var secondScope = app.Services.CreateScope();
        var first = firstScope.ServiceProvider; var second = secondScope.ServiceProvider;
        var firstPid = ((NpgsqlConnection)first.GetRequiredService<DbSession>().Connection).ProcessID;
        var secondPid = ((NpgsqlConnection)second.GetRequiredService<DbSession>().Connection).ProcessID;
        Assert.AreNotEqual(firstPid, secondPid);
        var request = new CheckPurchaseOrderImportRequest { CustomerCode = " 3086-aq ", SupplierId = supplier,
            Header = new() { OrderDate = date, BoNo = " EXCEL-PG-96079021 ", LogoType = " PG采购配置 ", InletHoseType = " 进水管配置 ",
                DiscountRate = 0.95m, DiscountAmount = 100m, DiscountRemark = " 仅记录不调价 ", Remark = batch },
            Lines = [new() { ClientLineId = Id(40), CustomerSku = " srk6504mb ", SelectedCustomerProductId = relation,
                Quantity = 1, UnitPrice = null, TaxPrice = null, PlannedDeliveryDate = date.AddDays(45) },
                new() { ClientLineId = Id(41), CustomerSku = " srk6504mb ", SelectedCustomerProductId = relation,
                Quantity = 1, UnitPrice = 116, TaxPrice = null, PlannedDeliveryDate = date.AddDays(45), SupplierSku = "", DescriptionZh = "",
                FeaturesZh = " Excel采购商业配置 " }] };
        var before = await Snapshot(); var evidence = new List<object>(); Guid purchaseId = Guid.Empty;
        try
        {
            var checkedPrice = await Check();
            Assert.IsFalse(checkedPrice.CanSave); Assert.IsTrue(checkedPrice.Lines[0].Issues.Any(x => x.Field == "UnitPrice"));
            await Reject(Id(1), ServiceErrorCodes.Validation);
            request.Lines[0].UnitPrice = 116;
            request.Lines[0].SelectedCustomerProductId = Id(42);
            var checkedIdentity = await Check();
            Assert.IsFalse(checkedIdentity.CanSave); Assert.IsTrue(checkedIdentity.Lines[0].Issues.Any(x => x.Code == "SelectionInvalid"));
            await Reject(Id(2), "SelectionInvalid");
            request.Lines[0].SelectedCustomerProductId = relation; request.Lines[0].LinkSku = " PG-Excel-96079021 ";
            var checkedMismatch = await Check();
            Assert.IsFalse(checkedMismatch.CanSave); Assert.IsTrue(checkedMismatch.Lines[0].Issues.Any(x => x.Code == "LinkSkuMismatch" && !x.Acknowledged));
            await Reject(Id(3), ServiceErrorCodes.Validation);
            request.Lines[0].AcceptLinkSkuMismatch = true;
            var accepted = await Check(); Assert.IsTrue(accepted.CanSave, Serialize(accepted));
            Assert.AreEqual(customer, accepted.Customer.Uid);
            Assert.IsTrue(accepted.Lines.All(x => x.MatchStatus == "Matched" && x.Product.ProductId == product && x.Product.CustomerProductId == relation));
            Assert.AreEqual(before, await Snapshot(), "只读检查及三次拒绝不得修改任何原事实或取号。");
            var command = Command(Id(4));
            var result = await first.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(command);
            Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message); purchaseId = result.Data.PurchaseOrderId; Clean(first);
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("""
                SELECT version=1 AND origin_kind=2 AND customer_id=@Customer AND supplier_id=@Supplier AND bo_no='EXCEL-PG-96079021'
                    AND logo_type='PG采购配置' AND inletr_hose_type='进水管配置' AND box_type IS NULL AND reply_date IS NULL
                    AND discount_rate=0.95 AND discount_amount=100 AND zhe_kou_bei_zhu='仅记录不调价'
                    AND demand_source_type IS NULL AND demand_source_uid IS NULL AND execution_request_id=@Command
                FROM public.purchase_order_head WHERE uid=@Id
                """, new { Id = purchaseId, Customer = customer, Supplier = supplier, Command = Id(4) }));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("""
                SELECT count(*) FROM public.purchase_order_line WHERE purchase_order_id=@Id AND version=1 AND product_id=@Product
                    AND customer_sku='srk6504mb' AND unit_snapshot='Pcs' AND quantity=1 AND unlinked_qty=1 AND unit_price=116
                    AND unit_amount=116 AND tax_price IS NULL AND tax_amount IS NULL AND source_type IS NULL
                    AND delivery_target_supplier_id IS NULL
                """, new { Id = purchaseId, Product = product }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT link_sku='PG-Excel-96079021' FROM public.purchase_order_line WHERE purchase_order_id=@Id AND line_number=1", new { Id = purchaseId }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT supplier_sku IS NULL AND description_zh IS NULL AND features_zh='Excel采购商业配置' FROM public.purchase_order_line WHERE purchase_order_id=@Id AND line_number=2", new { Id = purchaseId }));
            var after = await Snapshot();
            var replay = await second.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(command);
            Assert.IsTrue(replay.Success, replay.Message); Assert.AreEqual(purchaseId, replay.Data.PurchaseOrderId); Clean(second);
            Assert.AreEqual(after, await Snapshot(), "另一真实连接重放必须返回原成功事实，不得再取号或写日志。");
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(1), Id(2), Id(3) } }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = Id(4) }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT current_no FROM public.document_sequence WHERE doc_type='PO' AND year_month='650310'"));
            evidence.Add(new { Stage = "正式创建及另一backend原输入重放", Result = result, Replay = replay, After = after, AfterReplay = await Snapshot() });
        }
        finally { Console.WriteLine(Serialize(new { Batch = batch, Customer = customer, Supplier = supplier, Relation = relation, Product = product, PurchaseId = purchaseId,
            FirstPid = firstPid, SecondPid = secondPid, Before = before, Evidence = evidence, Policy = "仅新650310采购草稿/两行/号段/日志/回执；旧数据与主数据只读，无确认、DDL、清理、运行或备份库写入" })); }
        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        string Serialize(object value) => System.Text.Json.JsonSerializer.Serialize(value);
        void Clean(IServiceProvider sp) { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        CreateIndependentPurchaseOrderCommand Command(Guid id)
        {
            var command = request.Header.Adapt<CreateIndependentPurchaseOrderCommand>(); command.CommandId = id; command.CustomerId = customer; command.SupplierId = supplier;
            command.Lines = request.Lines.Select(x => { var line = x.Adapt<CreateIndependentPurchaseOrderLineCommand>(); line.ProductId = product; return line; }).ToList(); return command;
        }
        // 检查只返回快照，不修改调用方输入；正式创建另行验证，不信任先前CanSave。
        async Task<CheckPurchaseOrderImportResponse> Check()
        {
            var input = Serialize(request); var result = await first.GetRequiredService<IForExcelPurchaseOrderService>().CheckAsync(request);
            Assert.IsTrue(result.Success, result.Message); Assert.AreEqual(input, Serialize(request)); Clean(first);
            evidence.Add(new { Stage = "检查", Result = result }); return result.Data;
        }
        async Task Reject(Guid id, string code)
        {
            var result = await first.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(Command(id));
            Assert.IsFalse(result.Success); Assert.AreEqual(code, result.Error?.Code, result.Message); Clean(first);
            Assert.AreEqual(before, await Snapshot()); evidence.Add(new { Stage = "正式创建重验拒绝", CommandId = id, Result = result, Snapshot = await Snapshot() });
        }
        // 当前全部50表完整字段摘要，仅用于证明拒绝和原输入重放零事实增量，不替代后续精确旧行保护比对。
        async Task<string> Snapshot()
        {
            var hash = await observer.Connection.ExecuteScalarAsync<string>("""
                SELECT md5(string_agg(table_name||':'||row_count||':'||data_md5,chr(10) ORDER BY table_name)) FROM (SELECT c.relname table_name,(xpath('/row/row_count/text()',x.doc))[1]::text row_count,(xpath('/row/data_md5/text()',x.doc))[1]::text data_md5 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace CROSS JOIN LATERAL (SELECT query_to_xml(format('SELECT count(*) AS row_count, md5(coalesce(string_agg(to_jsonb(t)::text, chr(10) ORDER BY to_jsonb(t)::text), '''')) AS data_md5 FROM public.%I t',c.relname),false,true,'') doc) x WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname) facts
                """);
            Assert.IsNotNull(hash); return hash;
        }
    }

    /// <summary>箱单前段实际库存写入后制造后段原生40001，另验证整单反向后段失败；全部业务事实回滚、身份释放与序列缺口明确留证。</summary>
    [TestMethod]
    public async Task RealPg_RuntimePackingLateForwardAndReverseFailuresRollback()
    {
        const string batch = "fa689708-f1ab-4095-9da2-408b28f7b800";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_LATE_FAILURE_BATCH"));
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var productId = Guid.Parse("00042191-0e61-4461-b509-df820f9f7c69");
        var supplierId = Guid.Parse("40c4eb8d-f3c8-4ef2-a37e-4a35030e8ea3");
        var warehouseId = Guid.Parse("7ac538d0-6c30-4cff-b151-4a35030e8efc"); var customerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f");
        var date = new DateTime(6502, 10, 5);
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_dict_data WHERE data_name_en='PG-G5-fa689708'"), "已有输入即禁止隐式重跑或重造。");
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_balance WHERE product_id=@Id", new { Id = productId }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='650210'"));
        Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT current_no FROM public.document_sequence WHERE uid='81c56363-0544-4381-96b6-4a35028ebec0'"));
        var initialSequence = await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u WHERE u.is_active AND NOT u.is_deleted
            AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=u.uid
                AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid
            """);
        Guid markerId = Guid.Empty, salesId = Guid.Empty, grossId = Guid.Empty, netId = Guid.Empty, autoPurchaseId = Guid.Empty, autoLineId = Guid.Empty, balanceId = Guid.Empty;
        Guid targetCommand = Guid.Empty; var stage = ""; var evidence = new List<object>(); Guid[] pendingItems = [];
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        // 只复用测试故障装饰器；未武装时完整转发，生产仓储、事务隔离与业务资格不替换。
        builder.Services.AddScoped<RuntimeSerializationLogProbe>(sp => new(new PostgreSqlBusinessOperationLogRepository(sp.GetRequiredService<RepositoryBaseContext>()),
            sp.GetRequiredService<DbSession>(), connection, markerId, targetCommand, BeforeConflict));
        builder.Services.AddScoped<IBusinessOperationLogRepository>(sp => sp.GetRequiredService<RuntimeSerializationLogProbe>());
        await using var app = builder.Build();
        try
        {
            using (var inputScope = app.Services.CreateScope())
            {
                var marker = new SysDictData { DictCode = "Unit", DataNameEn = "PG-G5-fa689708", DataNameZh = "后段故障MVCC专用标记", IsEnabled = false, Remark = batch };
                Assert.IsTrue((await inputScope.ServiceProvider.GetRequiredService<IRepository<SysDictData>>().AddAsync(marker)).Success);
                markerId = marker.Uid; Clean(inputScope.ServiceProvider);
            }
            var markerBefore = await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(t)::text FROM public.sys_dict_data t WHERE uid=@Id", new { Id = markerId });
            // 销售来源、需求与关联采购均由原命令生成，不直接插入已确认采购、Gross/Net/Link或假箱单。
            var sku = await observer.Connection.QuerySingleAsync<string>("SELECT sku FROM public.product WHERE uid=@Id AND is_active AND NOT is_deleted", new { Id = productId });
            var saved = await Run(sp => sp.GetRequiredService<ISalesOrderDraftService>().SaveAsync(new() { CommandId = Id(1),
                Header = new SalesOrderEditHeaderDto { CustomerId = customerId, OrderDate = date, Currency = "CNY", ExchangeRate = 1, CustomerPoNo = "PG-G5-fa689708", Remark = batch, DeliveryDate = date.AddDays(45) },
                Lines = [new SalesOrderEditLineDto { Uid = Id(40), ProductId = productId, Sku = sku, Qty = 1, CnyPrice = 10, UsdPrice = 0 }] }));
            salesId = saved.Document.Header.Uid;
            var confirmedSales = await Run(sp => sp.GetRequiredService<ISalesOrderConfirmationService>().ConfirmAsync(salesId, new() { CommandId = Id(2), Uid = salesId, Version = 1 }));
            Assert.AreEqual(1, confirmedSales.GrossDemandCount);
            grossId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.gross_demand WHERE source_uid=@Id AND source_line_id=@Line AND source_type='SO'", new { Id = salesId, Line = Id(40) });
            var gross = await RunEntity<GrossDemand>(grossId);
            var established = await Run(sp => sp.GetRequiredService<IMaterialDemandEstablishmentService>().EstablishAsync(new() { CommandId = Id(3),
                Items = [new EstablishMaterialDemandItem { GrossDemandId = grossId, SourceDemandVersion = gross.Version, SourceVersion = gross.SourceVersion }] }));
            Assert.AreEqual(1, established.CreatedMaterialDemandCount);
            netId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.net_demand WHERE gross_demand_id=@Id AND NOT is_deleted", new { Id = grossId });
            var net = await RunEntity<NetDemand>(netId);
            var purchasePreview = await Run(sp => sp.GetRequiredService<IPurchaseDraftPreviewService>().PreviewAsync(new() { ExecutionRequestId = Id(4),
                EntryMode = PurchaseDraftEntryModeEnum.ManualSelection, LineMode = PurchaseDraftLineModeEnum.KeepSourceLines,
                Items = [new PurchaseDraftPreviewItem { CommandItemId = Id(41), NetDemandId = netId, ExpectedVersion = net.Version,
                    ExpectedRemainingQty = 1, PurchaseQty = 1, SupplierId = supplierId, UnitPrice = 10, TaxPrice = 10 }] }));
            Assert.IsFalse(purchasePreview.HasBlockers, string.Join(";", purchasePreview.Warnings.Select(x => x.Message)));
            var sourceGroup = purchasePreview.SourceGroups.Single();
            var linked = await Run(sp => sp.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(new() { ExecutionRequestId = Id(4),
                EntryMode = PurchaseDraftEntryModeEnum.ManualSelection, PreviewToken = purchasePreview.PreviewToken,
                Sources = [new CreatePurchaseDraftSourceCommand { SourceType = "SO", SourceUid = salesId, SourcePreviewToken = sourceGroup.SourcePreviewToken,
                    LineMode = PurchaseDraftLineModeEnum.KeepSourceLines, ConfirmedWarningCodes = sourceGroup.Warnings.Where(x => x.RequiresConfirmation).Select(x => x.Code).Distinct().ToList(),
                    Items = [new CreatePurchaseDraftItemCommand { CommandItemId = Id(41), NetDemandId = netId, ExpectedVersion = net.Version,
                        ExpectedRemainingQty = 1, PurchaseQty = 1, SupplierId = supplierId, UnitPrice = 10, TaxPrice = 10 }] }] }));
            Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded, linked.Sources.Single().Outcome, linked.Sources.Single().Message);
            autoPurchaseId = linked.Sources.Single().PurchaseDrafts.Single().PurchaseOrderId;
            autoLineId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@Id AND NOT is_deleted", new { Id = autoPurchaseId });
            await Confirm(autoPurchaseId, Id(5));

            await Run(sp => sp.GetRequiredService<IPackingListDraftService>().CreateAsync(new() { CommandId = Id(9), Uid = Id(42),
                Header = new PackingListDraftHeaderInput { CustomerId = customerId, InvoiceDate = date, Note = batch },
                Lines = [new PackingListDraftLineInput { Uid = Id(43), LineNumber = 1, GrossDemandId = grossId, ProductId = productId, UnitSnapshot = "Pcs", Qty = 1 }] }));
            var preview = await Run(sp => sp.GetRequiredService<IPackingListPostingService>().PreviewAsync(Id(42), new() { ExpectedVersion = 1, BusinessDate = date }, default));
            Assert.AreEqual(0, preview.Blockers.Count, string.Join(";", preview.Blockers.Select(x => x.Message)));
            CollectionAssert.AreEquivalent(new[] { "Receipt", "Shipment" }, preview.Actions.Select(x => x.ActionKind).ToArray());
            Assert.IsTrue(preview.Actions.All(x => x.WarehouseSiteId == warehouseId));
            var beforeForward = await Snapshot(); stage = "正向收货已写，出货前失败";
            targetCommand = Child(Id(10), "outbound");
            using (var failureScope = app.Services.CreateScope())
            {
                var result = await failureScope.ServiceProvider.GetRequiredService<IPackingListPostingService>().PostAsync(Id(42),
                    new() { CommandId = Id(10), ExpectedVersion = 1, BusinessDate = date, PreviewToken = preview.PreviewToken }, default);
                Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, result.Error?.Code, result.Message);
                var probe = failureScope.ServiceProvider.GetRequiredService<RuntimeSerializationLogProbe>();
                Assert.AreEqual("40001", probe.ObservedSqlState); Clean(failureScope.ServiceProvider);
                Assert.AreEqual(beforeForward, await Snapshot(), "前段单据、余额、流水、号段、日志、执行及回执必须全部回滚。");
                Assert.AreEqual(initialSequence + 1, await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence"));
                evidence.Add(new { Stage = stage, Result = result, ProbeSqlState = probe.ObservedSqlState, WriterPid = probe.ObservedWriterPid, SnapshotBefore = beforeForward, SnapshotAfter = await Snapshot() });
                await Reclaim(failureScope.ServiceProvider.GetRequiredService<DbSession>(), Id(10), ["inbound", "outbound"]);
            }
            // 失败命令不自动重执；明确以新业务命令正常提交，为真实反向失败保留合法原单与两条不可变原流水。
            targetCommand = Guid.Empty; stage = "";
            await Run(sp => sp.GetRequiredService<IPackingListPostingService>().PostAsync(Id(42), new() { CommandId = Id(11), ExpectedVersion = 1, BusinessDate = date, PreviewToken = preview.PreviewToken }, default));
            balanceId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Product", new { Warehouse = warehouseId, Product = productId });
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=3 AND on_hand_qty=0 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            var reversePreview = await Run(sp => sp.GetRequiredService<IPackingListReversalService>().ReversePreviewAsync(Id(42), new() {
                ExpectedVersion = 2, BusinessDate = date, Reason = "PG后段失败原整单反向" }, default));
            Assert.AreEqual(0, reversePreview.Blockers.Count, string.Join(";", reversePreview.Blockers.Select(x => x.Message)));
            var beforeReverse = await Snapshot(); stage = "反向出货已恢复，反向收货前失败"; targetCommand = Child(Id(12), "reverse-inbound");
            using (var failureScope = app.Services.CreateScope())
            {
                var result = await failureScope.ServiceProvider.GetRequiredService<IPackingListReversalService>().ReverseAsync(Id(42), new() {
                    CommandId = Id(12), ExpectedVersion = 2, BusinessDate = date, Reason = "PG后段失败原整单反向", PreviewToken = reversePreview.PreviewToken }, default);
                Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, result.Error?.Code, result.Message);
                var probe = failureScope.ServiceProvider.GetRequiredService<RuntimeSerializationLogProbe>();
                Assert.AreEqual("40001", probe.ObservedSqlState); Clean(failureScope.ServiceProvider);
                Assert.AreEqual(beforeReverse, await Snapshot(), "反向前段事实必须完整回滚，原已过账单据/原流水保持。");
                Assert.AreEqual(initialSequence + 4, await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence"));
                evidence.Add(new { Stage = stage, Result = result, ProbeSqlState = probe.ObservedSqlState, WriterPid = probe.ObservedWriterPid, SnapshotBefore = beforeReverse, SnapshotAfter = await Snapshot() });
                await Reclaim(failureScope.ServiceProvider.GetRequiredService<DbSession>(), Id(12), ["reverse-outbound", "reverse-inbound"]);
            }
            targetCommand = Guid.Empty; stage = "";
            await Run(sp => sp.GetRequiredService<IPackingListReversalService>().ReverseAsync(Id(42), new() { CommandId = Id(13), ExpectedVersion = 2,
                BusinessDate = date, Reason = "PG后段失败原整单反向", PreviewToken = reversePreview.PreviewToken }, default));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=5 AND on_hand_qty=0 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE warehouse_site_id=@Warehouse AND product_id=@Product", new { Warehouse = warehouseId, Product = productId }));
            Assert.AreEqual(initialSequence + 6, await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence"));
            Assert.AreEqual(markerBefore, await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(t)::text FROM public.sys_dict_data t WHERE uid=@Id", new { Id = markerId }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(10), Id(12) } }));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(11), Id(13) } }));
            Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT current_no FROM public.document_sequence WHERE uid='81c56363-0544-4381-96b6-4a35028ebec0'"));
        }
        finally
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, FirstRun = true, MarkerId = markerId, ProductId = productId, WarehouseId = warehouseId, SupplierId = supplierId,
                SalesId = salesId, GrossId = grossId, NetId = netId, PurchaseId = autoPurchaseId, PurchaseLineId = autoLineId, PackingListId = Id(42), PackingLineId = Id(43), BalanceId = balanceId,
                InitialSequence = initialSequence, Evidence = evidence, Policy = "原服务新来源/需求/采购/箱单，新库存键；两原生40001仅同值UPDATE本批新标记；所有旧行保护，仅原PO/202610正常3到4；无自动重试/重置/DDL/运行或备份库写入" }));
        }
        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        Guid Child(Guid parent, string purpose) => SwiftExport.Application.Services.Shipping.PackingListAutomaticCommandBuilder.DeriveId(parent, purpose);
        int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
        void Clean(IServiceProvider sp) { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        async Task<T> Run<T>(Func<IServiceProvider, Task<ServiceResult<T>>> action)
        { using var scope = app.Services.CreateScope(); var result = await action(scope.ServiceProvider); Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message); Clean(scope.ServiceProvider); return result.Data; }
        async Task<T> RunEntity<T>(Guid id) where T : SwiftExport.Core.Common.BaseEntity
        { using var scope = app.Services.CreateScope(); var entity = await scope.ServiceProvider.GetRequiredService<IRepository<T>>().GetByIdAsync(id); Assert.IsNotNull(entity); return entity; }
        async Task Confirm(Guid id, Guid commandId)
        { var basis = await Run(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(id));
          await Run(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(id, new() { CommandId = commandId, ExpectedVersion = 1, ContextFingerprint = basis.Fingerprint })); }
        // 全50表当前字段摘要比较，失败动作不能遗留前段事实；数据库库存序列独立检查允许回滚缺口。
        async Task<string> Snapshot()
        {
            var hash = await observer.Connection.ExecuteScalarAsync<string>("""
            SELECT md5(string_agg(table_name||':'||row_count||':'||data_md5,chr(10) ORDER BY table_name)) FROM (SELECT c.relname table_name,(xpath('/row/row_count/text()',x.doc))[1]::text row_count,(xpath('/row/data_md5/text()',x.doc))[1]::text data_md5 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace CROSS JOIN LATERAL (SELECT query_to_xml(format('SELECT count(*) AS row_count, md5(coalesce(string_agg(to_jsonb(t)::text, chr(10) ORDER BY to_jsonb(t)::text), '''')) AS data_md5 FROM public.%I t',c.relname),false,true,'') doc) x WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname) facts
            """);
            Assert.IsNotNull(hash); return hash;
        }
        // 故障发生前用同一真实事务证明前段已写，而非只在尚未写库存的入口抛错。
        async Task BeforeConflict(DbSession session)
        {
            Assert.IsNotNull(session.Transaction);
            var args = new { Warehouse = warehouseId, Product = productId, Packing = Id(42) };
            var qty = await session.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Product", args, session.Transaction);
            var version = await session.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Product", args, session.Transaction);
            pendingItems = (await session.Connection.QueryAsync<Guid>("SELECT command_item_id FROM public.inventory_ledger_entry WHERE warehouse_site_id=@Warehouse AND product_id=@Product ORDER BY sequence", args, session.Transaction)).ToArray();
            var documents = await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inbound_document WHERE trigger_packing_list_id=@Packing", args, session.Transaction);
            var executions = await session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_posting_execution WHERE packing_list_head_id=@Packing", args, session.Transaction);
            Assert.AreEqual(1m, qty); Assert.AreEqual(stage.StartsWith("正向") ? 2 : 4, version); Assert.AreEqual(stage.StartsWith("正向") ? 1 : 3, pendingItems.Length);
            Assert.AreEqual(1, documents); Assert.AreEqual(stage.StartsWith("正向") ? 0 : 1, executions);
            evidence.Add(new { Stage = stage + "前段实际事务内证据", TransactionPid = Pid(session), Qty = qty, Version = version, PendingItems = pendingItems, InboundDocuments = documents, Executions = executions });
        }
        // 另一个backend重新取得父/子命令、库存命令/Item及余额锁，证明失败无会话或事务资源泄漏。
        async Task Reclaim(DbSession failed, Guid parent, string[] purposes)
        {
            Assert.AreNotEqual(Pid(failed), Pid(observer));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'", new { Pid = Pid(failed) }));
            foreach (var command in purposes.Select(x => Child(parent, x)).Prepend(parent))
            { await using var lease = await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(observer, command, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default); Assert.IsNotNull(lease); }
            using var scope = app.Services.CreateScope(); var session = scope.ServiceProvider.GetRequiredService<DbSession>(); Assert.AreNotEqual(Pid(failed), Pid(session));
            using var transaction = session.Connection.BeginTransaction(); session.Transaction = transaction;
            try
            {
                var repository = scope.ServiceProvider.GetRequiredService<SwiftExport.Core.Interfaces.Inventory.Posting.IInventoryPostingRepository>();
                foreach (var command in purposes.Select(x => Child(parent, x))) await repository.AcquireCommandLockAsync(command, default);
                foreach (var item in pendingItems) await repository.LockLedgerByCommandItemAsync(item);
                await repository.LockBalanceAsync(warehouseId, productId);
                transaction.Rollback(); evidence.Add(new { Stage = stage + "失败后另backend重取身份", FailedPid = Pid(failed), CommandProbePid = Pid(observer), InventoryProbePid = Pid(session), PendingItems = pendingItems });
            }
            finally { session.Transaction = null; }
            Clean(scope.ServiceProvider);
        }
    }
    /// <summary>真实箱单自动收发与不同采购来源的人工入库竞争首次空余额；只创建新输入并用原服务完整反向，保护所有历史事实。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeAutomaticAndManualFirstBalanceCompetition()
    {
        const string batch = "e4ef52de-638f-45fa-991c-8fd9c66bb700";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_CROSS_STOCK_BATCH"));
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.supplier WHERE supplier_code='PGG4E4EF52DE'"), "成功输入存在即禁止隐式重造或重跑。");
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='650110'"));
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u WHERE u.is_active AND NOT u.is_deleted
            AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=u.uid
                AND NOT ur.is_deleted AND r.is_active AND NOT r.is_deleted AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor); await using var app = builder.Build();
        var date = new DateTime(6501, 10, 5); var productId = Guid.Parse("00008dec-d28a-4036-bc06-da0a2159e16b");
        var customerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f"); var evidence = new List<object>();
        Guid supplierId = Guid.Empty, warehouseId = Guid.Empty, salesId = Guid.Empty, grossId = Guid.Empty, netId = Guid.Empty;
        Guid autoPurchaseId = Guid.Empty, manualPurchaseId = Guid.Empty, inboundId = Guid.Empty, balanceId = Guid.Empty;
        var autoLineId = Guid.Empty; var manualLineId = Guid.Empty;
        try
        {
            var supplier = await Run(sp => sp.GetRequiredService<SwiftExport.Application.Interfaces.Suppliers.Management.ISupplierService>().AddAsync(
                new CreateItemRequest<SwiftExport.Contracts.DTOs.Suppliers.SupplierDto> { ClientKey = batch, Data = new() {
                    Version = 1, SupplierCode = "PGG4E4EF52DE", SupplierName = "PG跨业务首次库存专用供应商e4ef52de", DocRelateNo = "PG-G4", Currency = "CNY" } }));
            supplierId = supplier.Uid!.Value;
            var warehouse = await Run(sp => sp.GetRequiredService<SwiftExport.Application.Interfaces.Inventory.Warehouses.IWarehouseManagementService>()
                .CreateSupplierVirtualWarehouseAsync(new() { CommandId = Id(0), SupplierId = supplierId, Code = "PG-G4-e4ef52de", Name = "PG跨业务首次库存专用仓e4ef52de" }));
            warehouseId = warehouse.Uid;
            // 销售来源、需求与关联采购均由原命令生成，不直接插入已确认采购、Gross/Net/Link或假箱单。
            var sku = await observer.Connection.QuerySingleAsync<string>("SELECT sku FROM public.product WHERE uid=@Id AND is_active AND NOT is_deleted", new { Id = productId });
            var saved = await Run(sp => sp.GetRequiredService<ISalesOrderDraftService>().SaveAsync(new() { CommandId = Id(1),
                Header = new SalesOrderEditHeaderDto { CustomerId = customerId, OrderDate = date, Currency = "CNY", ExchangeRate = 1, CustomerPoNo = "PG-G4-e4ef52de", Remark = batch, DeliveryDate = date.AddDays(45) },
                Lines = [new SalesOrderEditLineDto { Uid = Id(40), ProductId = productId, Sku = sku, Qty = 1, CnyPrice = 10, UsdPrice = 0 }] }));
            salesId = saved.Document.Header.Uid;
            var confirmedSales = await Run(sp => sp.GetRequiredService<ISalesOrderConfirmationService>().ConfirmAsync(salesId, new() { CommandId = Id(2), Uid = salesId, Version = 1 }));
            Assert.AreEqual(1, confirmedSales.GrossDemandCount);
            grossId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.gross_demand WHERE source_uid=@Id AND source_line_id=@Line AND source_type='SO'", new { Id = salesId, Line = Id(40) });
            var gross = await RunEntity<GrossDemand>(grossId);
            var established = await Run(sp => sp.GetRequiredService<IMaterialDemandEstablishmentService>().EstablishAsync(new() { CommandId = Id(3),
                Items = [new EstablishMaterialDemandItem { GrossDemandId = grossId, SourceDemandVersion = gross.Version, SourceVersion = gross.SourceVersion }] }));
            Assert.AreEqual(1, established.CreatedMaterialDemandCount);
            netId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.net_demand WHERE gross_demand_id=@Id AND NOT is_deleted", new { Id = grossId });
            var net = await RunEntity<NetDemand>(netId);
            var purchasePreview = await Run(sp => sp.GetRequiredService<IPurchaseDraftPreviewService>().PreviewAsync(new() { ExecutionRequestId = Id(4),
                EntryMode = PurchaseDraftEntryModeEnum.ManualSelection, LineMode = PurchaseDraftLineModeEnum.KeepSourceLines,
                Items = [new PurchaseDraftPreviewItem { CommandItemId = Id(41), NetDemandId = netId, ExpectedVersion = net.Version,
                    ExpectedRemainingQty = 1, PurchaseQty = 1, SupplierId = supplierId, UnitPrice = 10, TaxPrice = 10 }] }));
            Assert.IsFalse(purchasePreview.HasBlockers, string.Join(";", purchasePreview.Warnings.Select(x => x.Message)));
            var sourceGroup = purchasePreview.SourceGroups.Single();
            var linked = await Run(sp => sp.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(new() { ExecutionRequestId = Id(4),
                EntryMode = PurchaseDraftEntryModeEnum.ManualSelection, PreviewToken = purchasePreview.PreviewToken,
                Sources = [new CreatePurchaseDraftSourceCommand { SourceType = "SO", SourceUid = salesId, SourcePreviewToken = sourceGroup.SourcePreviewToken,
                    LineMode = PurchaseDraftLineModeEnum.KeepSourceLines, ConfirmedWarningCodes = sourceGroup.Warnings.Where(x => x.RequiresConfirmation).Select(x => x.Code).Distinct().ToList(),
                    Items = [new CreatePurchaseDraftItemCommand { CommandItemId = Id(41), NetDemandId = netId, ExpectedVersion = net.Version,
                        ExpectedRemainingQty = 1, PurchaseQty = 1, SupplierId = supplierId, UnitPrice = 10, TaxPrice = 10 }] }] }));
            Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded, linked.Sources.Single().Outcome, linked.Sources.Single().Message);
            autoPurchaseId = linked.Sources.Single().PurchaseDrafts.Single().PurchaseOrderId;
            autoLineId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@Id AND NOT is_deleted", new { Id = autoPurchaseId });
            await Confirm(autoPurchaseId, Id(5));
            var independent = await Run(sp => sp.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(new() { CommandId = Id(6),
                CustomerId = customerId, SupplierId = supplierId, OrderDate = date, Remark = batch,
                Lines = [new CreateIndependentPurchaseOrderLineCommand { ProductId = productId, Quantity = 2, UnitPrice = 10, TaxPrice = 10 }] }));
            manualPurchaseId = independent.PurchaseOrderId; manualLineId = independent.Lines.Single().PurchaseOrderLineId;
            await Confirm(manualPurchaseId, Id(7)); Assert.AreNotEqual(autoPurchaseId, manualPurchaseId);
            var inboundDraft = await Run(sp => sp.GetRequiredService<IInboundDraftService>().CreateAsync(new() { CommandId = Id(8),
                Draft = new InboundDraftData { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, WarehouseSiteId = warehouseId, ReceiptDate = date,
                    Remark = batch, Lines = [new InboundDraftLineInput { SourceLineId = manualLineId, Quantity = 2 }] } }));
            inboundId = inboundDraft.Uid;
            var packing = await Run(sp => sp.GetRequiredService<IPackingListDraftService>().CreateAsync(new() { CommandId = Id(9), Uid = Id(42),
                Header = new PackingListDraftHeaderInput { CustomerId = customerId, InvoiceDate = date, Note = batch },
                Lines = [new PackingListDraftLineInput { Uid = Id(43), LineNumber = 1, GrossDemandId = grossId, ProductId = productId, UnitSnapshot = "Pcs", Qty = 1 }] }));
            Assert.IsNotNull(packing);
            var preview = await Run(sp => sp.GetRequiredService<IPackingListPostingService>().PreviewAsync(Id(42), new() { ExpectedVersion = 1, BusinessDate = date }, default));
            Assert.AreEqual(0, preview.Blockers.Count, string.Join(";", preview.Blockers.Select(x => x.Message)));
            CollectionAssert.AreEquivalent(new[] { "Receipt", "Shipment" }, preview.Actions.Select(x => x.ActionKind).ToArray());
            Assert.IsTrue(preview.Actions.All(x => x.WarehouseSiteId == warehouseId)); Assert.AreEqual(autoLineId, preview.Actions.Single(x => x.ActionKind == "Receipt").PurchaseLineId);
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_balance WHERE warehouse_site_id=@Id", new { Id = warehouseId }));
            var command = new PostPackingListCommand { CommandId = Id(10), ExpectedVersion = 1, BusinessDate = date, PreviewToken = preview.PreviewToken };
            using (var holder = new DbSession(new PostgreSqlConnectionFactory(connection)))
            using (var automaticScope = app.Services.CreateScope())
            using (var manualScope = app.Services.CreateScope())
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                var automaticSession = automaticScope.ServiceProvider.GetRequiredService<DbSession>(); var manualSession = manualScope.ServiceProvider.GetRequiredService<DbSession>();
                Assert.AreEqual(4, new[] { Pid(holder), Pid(observer), Pid(automaticSession), Pid(manualSession) }.Distinct().Count());
                using var barrier = holder.Connection.BeginTransaction();
                await holder.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.warehouse_site WHERE uid=@Id FOR UPDATE", new { Id = warehouseId }, barrier);
                var automatic = automaticScope.ServiceProvider.GetRequiredService<IPackingListPostingService>().PostAsync(Id(42), command, deadline.Token);
                Task<ServiceResult<InboundPostResult>>? manual = null; var observed = false;
                try
                {
                    await Observe([Pid(automaticSession)], deadline.Token);
                    manual = manualScope.ServiceProvider.GetRequiredService<IInboundPostingService>().PostAsync(inboundId, new() { CommandId = Id(11), ExpectedVersion = 1 }, deadline.Token);
                    await Observe([Pid(automaticSession), Pid(manualSession)], deadline.Token);
                    Assert.IsFalse(automatic.IsCompleted); Assert.IsFalse(manual.IsCompleted);
                    Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_balance WHERE warehouse_site_id=@Id", new { Id = warehouseId }));
                    observed = true;
                }
                finally
                {
                    barrier.Rollback(); if (!observed) deadline.Cancel();
                    try { await Task.WhenAll(manual is null ? [automatic] : new Task[] { automatic, manual }); } catch when (!observed) { /* 释放屏障并等待全部任务后保留原观察失败。 */ }
                }
                var autoResult = await automatic; Assert.IsTrue(autoResult.Success, autoResult.Error?.Code + ":" + autoResult.Message);
                Assert.IsNotNull(manual); var manualResult = await manual; Assert.IsTrue(manualResult.Success, manualResult.Error?.Code + ":" + manualResult.Message);
                Clean(automaticScope.ServiceProvider); Clean(manualScope.ServiceProvider);
                Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_locks WHERE pid=ANY(@Pids) AND locktype='advisory'", new { Pids = new[] { Pid(automaticSession), Pid(manualSession) } }));
                evidence.Add(new { Stage = "跨业务首次竞争终态", AutomaticPid = Pid(automaticSession), ManualPid = Pid(manualSession), Automatic = autoResult, Manual = manualResult });
            }
            balanceId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Product", new { Warehouse = warehouseId, Product = productId });
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=4 AND on_hand_qty=2 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE warehouse_site_id=@Id", new { Id = warehouseId }));
            var reversePreview = await Run(sp => sp.GetRequiredService<IPackingListReversalService>().ReversePreviewAsync(Id(42), new() { ExpectedVersion = 2, BusinessDate = date, Reason = "PG跨业务原整单反向" }, default));
            Assert.AreEqual(0, reversePreview.Blockers.Count, string.Join(";", reversePreview.Blockers.Select(x => x.Message)));
            await Run(sp => sp.GetRequiredService<IPackingListReversalService>().ReverseAsync(Id(42), new() { CommandId = Id(12), ExpectedVersion = 2, BusinessDate = date,
                Reason = "PG跨业务原整单反向", PreviewToken = reversePreview.PreviewToken }, default));
            await Run(sp => sp.GetRequiredService<IInboundReversalService>().ReverseAsync(inboundId, new() { CommandId = Id(13), ExpectedVersion = 2, Reason = "PG人工库存原完整反向" }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=7 AND on_hand_qty=0 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            Assert.AreEqual(6, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE warehouse_site_id=@Id", new { Id = warehouseId }));
            Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry r JOIN public.inventory_ledger_entry o ON o.uid=r.reversal_of_ledger_id WHERE r.warehouse_site_id=@Id AND r.quantity_delta=-o.quantity_delta", new { Id = warehouseId }));
            Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(10), Id(11), Id(12), Id(13) } }));
        }
        finally
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, FirstRun = true, SupplierId = supplierId, WarehouseId = warehouseId, SalesId = salesId,
                GrossId = grossId, NetId = netId, AutoPurchaseId = autoPurchaseId, AutoLineId = autoLineId, ManualPurchaseId = manualPurchaseId, ManualLineId = manualLineId,
                InboundId = inboundId, PackingListId = Id(42), PackingLineId = Id(43), BalanceId = balanceId, Evidence = evidence,
                Policy = "所有旧事实保护；只用原服务新增及反向本批身份，无重置/清库/DDL/运行库或备份写入" }));
        }
        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
        void Clean(IServiceProvider sp) { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        // 每次正式入口与预览使用独立真实请求作用域；不替换生产依赖或自动重试失败命令。
        async Task<T> Run<T>(Func<IServiceProvider, Task<ServiceResult<T>>> action)
        { using var scope = app.Services.CreateScope(); var result = await action(scope.ServiceProvider); Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message); Clean(scope.ServiceProvider); return result.Data; }
        async Task<T> RunEntity<T>(Guid id) where T : SwiftExport.Core.Common.BaseEntity
        { using var scope = app.Services.CreateScope(); var entity = await scope.ServiceProvider.GetRequiredService<IRepository<T>>().GetByIdAsync(id); Assert.IsNotNull(entity); return entity; }
        async Task Confirm(Guid id, Guid commandId)
        { var basis = await Run(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(id));
          await Run(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(id, new() { CommandId = commandId, ExpectedVersion = 1, ContextFingerprint = basis.Fingerprint })); }
        // 查询真实服务器阻塞图与仓库FOR UPDATE关系锁；不依赖会被截断的长SQL文本或固定睡眠。
        async Task Observe(int[] pids, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var count = await observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT count(*) FROM pg_stat_activity a WHERE a.pid=ANY(@Pids) AND a.xact_start IS NOT NULL AND cardinality(pg_blocking_pids(a.pid))>0
                    AND EXISTS(SELECT 1 FROM pg_locks l WHERE l.pid=a.pid AND l.relation='public.warehouse_site'::regclass AND l.mode='RowShareLock' AND l.granted)
                    """, new { Pids = pids }, cancellationToken: token));
                if (count == pids.Length) break; await Task.Delay(20, token);
            }
            evidence.Add(new { Stage = "仓库库存键实际等待", Pids = pids, Graph = await observer.Connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT jsonb_agg(jsonb_build_object('pid',pid,'blocking',pg_blocking_pids(pid),'query',query,'xact_start',xact_start) ORDER BY pid)::text FROM pg_stat_activity WHERE pid=ANY(@Pids)",
                new { Pids = pids }, cancellationToken: token)) });
        }
    }
    /// <summary>接续已实际完成首次竞争的赢家，保留三次失败TRX；仅执行原整单反过账/输家草稿软删/采购取消，不重跑竞争。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeRetainedCapacityWinnerReversesAndAllowsPurchaseCancellation()
    {
        const string batch = "6a67084f-3c6f-4956-8d72-aa439228b500";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_FIRST_INBOUND_FINISH"));
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString(); using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var purchaseId = Guid.Parse("1a5adefe-348e-4de5-b2c0-4a3502f11e25"); var warehouseId = Guid.Parse("be0da779-2a9e-4066-a41a-4a3502f11db4");
        var winner = Guid.Parse("d24f78f2-01d0-4185-a86f-4a3502f11f6a"); var loser = Guid.Parse("9f57bb6a-32a8-492b-a34d-4a3502f11f09");
        var balanceId = Guid.Parse("c54c4c00-0bd3-4973-b9d8-4a3502f645df");
        Assert.AreEqual(6, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(0, 11).Select(Id).ToArray() }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(4), Id(6), Id(7), Id(8), Id(9) } }));
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=2 AND posting_status=1 AND NOT is_deleted FROM public.inbound_document WHERE uid=@Id", new { Id = winner }));
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=1 AND posting_status=0 AND NOT is_deleted FROM public.inbound_document WHERE uid=@Id", new { Id = loser }));
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=2 AND on_hand_qty=2 AND last_ledger_sequence=29 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
        var originalLedger = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.inventory_ledger_entry WHERE source_document_id=@Id AND command_id=@Command AND quantity_delta=2 AND reversal_of_ledger_id IS NULL", new { Id = winner, Command = Id(5) });
        var originalJson = await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(t)::text FROM public.inventory_ledger_entry t WHERE uid=@Id", new { Id = originalLedger });
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.command_receipt r
            JOIN public.sys_user u ON u.uid=r.actor_uid WHERE r.command_id=@Id
            """, new { Id = Id(5) });
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor); await using var app = builder.Build(); var pids = new List<int>();
        try
        {
            var reversePreview = await Run(sp => sp.GetRequiredService<IInboundReversalService>().PreviewReverseAsync(winner, new() { ExpectedVersion = 2 }));
            Assert.IsTrue(reversePreview.CanReverse, string.Join(";", reversePreview.Blockers.Select(x => x.Message)));
            var reversed = await Run(sp => sp.GetRequiredService<IInboundReversalService>().ReverseAsync(winner, new() { CommandId = Id(7), ExpectedVersion = 2, Reason = "PG首轮赢家完整反过账保留两流水" }));
            Assert.AreEqual(3, reversed.Version);
            var deleted = await Run(sp => sp.GetRequiredService<IInboundDraftService>().DeleteAsync(loser, new() { CommandId = Id(8), ExpectedVersion = 1, Reason = "PG仅本批未过账输家草稿原软删" }));
            Assert.IsTrue(deleted.IsDeleted); Assert.AreEqual(2, deleted.Version);
            var allowed = await Run(sp => sp.GetRequiredService<IProcurementOrderReversalService>().PreviewAsync(purchaseId, ProcurementReversalOperation.CancelConfirmation, new() { ExpectedVersion = 2 }));
            Assert.IsTrue(allowed.CanExecute, string.Join(";", allowed.Blockers)); Assert.AreEqual(0, allowed.Contributions.Count);
            var cancelled = await Run(sp => sp.GetRequiredService<IProcurementOrderReversalService>().CancelConfirmationAsync(purchaseId, new() { CommandId = Id(9), ExpectedVersion = 2, Reason = "PG完整反过账且草稿软删后原取消放行" }));
            Assert.AreEqual(3, cancelled.Version); Assert.AreEqual(ConfirmationStatusEnum.待确认, cancelled.ConfirmationStatus);
            // 原事实必须保持，反向仅新增一条-2并使新余额V2→V3/Qty0；原旧G1余额全程未写。
            Assert.AreEqual(originalJson, await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(t)::text FROM public.inventory_ledger_entry t WHERE uid=@Id", new { Id = originalLedger }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE reversal_of_ledger_id=@Original AND source_document_id=@Head AND quantity_delta=-2 AND replacement_of_ledger_id IS NULL", new { Original = originalLedger, Head = winner }));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE source_document_id=@Id", new { Id = winner }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE source_document_id=@Id", new { Id = loser }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=3 AND on_hand_qty=0 AND last_ledger_sequence=30 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=3 AND on_hand_qty=0 AND last_ledger_sequence=28 FROM public.inventory_balance WHERE uid='0189ec8d-af2f-4c4a-8a69-4a3502ab14e2'"));
            var replay = await Run(sp => sp.GetRequiredService<IInboundPostingService>().PostAsync(winner, new() { CommandId = Id(5), ExpectedVersion = 1 }));
            Assert.AreEqual(2, replay.Version); Assert.AreEqual(InboundPostingStatusEnum.Posted, replay.PostingStatus);
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=3 AND posting_status=8 FROM public.inbound_document WHERE uid=@Id", new { Id = winner }));
            Assert.AreEqual(9, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(0, 11).Select(Id).ToArray() }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(4), Id(6) } }));
            using var probe = app.Services.CreateScope(); var session = probe.ServiceProvider.GetRequiredService<DbSession>(); pids.Add(Pid(session));
            foreach (var command in Enumerable.Range(0, 11).Select(Id))
            { await using var lease = await ((ICommandTransactionPreparation)probe.ServiceProvider.GetRequiredService<ICommandReceiptRepository>()).AcquireBeforeTransactionAsync(command, default); Assert.IsNotNull(lease); }
            Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease);
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'", new { Pid = Pid(session) }));
        }
        finally
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, Phase = "仅反向终态接续", FirstRun = false, CompetitionRerun = false, PurchaseId = purchaseId,
                WarehouseId = warehouseId, Winner = winner, Loser = loser, BalanceId = balanceId, OriginalLedgerId = originalLedger, OriginalLedgerBefore = originalJson, Backends = pids, Observer = Pid(observer),
                Policy = "仅本批自有头行/新余额按原命令更新，旧余额/原流水保留，三次失败TRX不覆盖，无清库/重置/DDL/运行库/备份写入" }));
        }
        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
        // 各入口使用原请求作用域并证明事务/租约释放，真实成功结果之外不隐式重试失败命令。
        async Task<T> Run<T>(Func<IServiceProvider, Task<ServiceResult<T>>> action)
        {
            using var scope = app.Services.CreateScope(); var session = scope.ServiceProvider.GetRequiredService<DbSession>(); pids.Add(Pid(session));
            var result = await action(scope.ServiceProvider); Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message);
            Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); return result.Data;
        }
    }
    /// <summary>通过真实采购和入库草稿验证新容量3/两次各2的首次竞争及采购反向阻断；仅按原规则反过账和软删本批草稿。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeFirstInboundCapacityAndPurchaseCancellationCompetition()
    {
        const string batch = "6a67084f-3c6f-4956-8d72-aa439228b500";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_FIRST_INBOUND_BATCH"));
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var retainedInputs = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_FIRST_INBOUND_INPUTS"); if (retainedInputs is not null) Assert.AreEqual(batch, retainedInputs);
        Assert.AreEqual(retainedInputs is null ? 0 : 5, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(0, 11).Select(Id).ToArray() }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(4, 6).Select(Id).ToArray() }), "任何Post/反向/取消成功事实都禁止重新竞争。");
        Assert.AreEqual(retainedInputs is null ? 0 : 2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='650010'"));
        var productId = Guid.Parse("00008dec-d28a-4036-bc06-da0a2159e16b"); Guid warehouseId = Guid.Empty, balanceId = Guid.Empty;
        Assert.AreEqual(retainedInputs is null ? 0 : 1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.warehouse_site WHERE code='PG-G3-6a67084f'"));
        // 首轮供应商输入已提交而夹具UID断言失败时，只允许明确登记实际返回UID接续；不删输入、复位或冒称整场首次全绿。
        var retainedSupplier = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_FIRST_INBOUND_SUPPLIER"); var supplierId = Guid.Empty;
        if (retainedSupplier is null)
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.supplier WHERE supplier_code='PGG36A67084F'"));
        else
        {
            supplierId = Guid.Parse(retainedSupplier); Assert.AreEqual(Guid.Parse("5748f17e-3652-44b9-aa16-4a3502ed5d41"), supplierId);
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=1 AND supplier_code='PGG36A67084F' AND is_active AND NOT is_deleted FROM public.supplier WHERE uid=@Id", new { Id = supplierId }));
            Assert.AreEqual(retainedInputs is null ? 0 : 1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.warehouse_site WHERE supplier_id=@Id", new { Id = supplierId }));
            Assert.AreEqual(retainedInputs is null ? 0 : 1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_head WHERE supplier_id=@Id", new { Id = supplierId }));
        }
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u WHERE u.is_active=TRUE AND u.is_deleted=FALSE
            AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=u.uid
                AND ur.is_deleted=FALSE AND r.is_active=TRUE AND r.is_deleted=FALSE AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid;
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor); await using var app = builder.Build();
        Guid purchaseId = Guid.Empty, purchaseLineId = Guid.Empty; var heads = new Guid[2]; var lines = new Guid[2];
        var evidence = new List<object>(); var winner = -1;
        try
        {
            if (retainedInputs is not null)
            {
                // 上轮观察SQL被1kB活动文本截断误判而取消；仅接回原服务已创建输入，真实Post尚未启动且任何库存事实都拒绝续跑。
                warehouseId = Guid.Parse("be0da779-2a9e-4066-a41a-4a3502f11db4"); purchaseId = Guid.Parse("1a5adefe-348e-4de5-b2c0-4a3502f11e25"); purchaseLineId = Guid.Parse("4d2f758d-062f-43cf-b715-4a3502f11e27");
                heads = [Guid.Parse("9f57bb6a-32a8-492b-a34d-4a3502f11f09"), Guid.Parse("d24f78f2-01d0-4185-a86f-4a3502f11f6a")];
                lines = [Guid.Parse("a82a409e-baa2-4620-a5d2-4a3502f11f17"), Guid.Parse("3f3cd116-78e0-4695-8fb5-4a3502f11f70")];
                Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT supplier_id=@Supplier AND code='PG-G3-6a67084f' AND is_active AND NOT is_deleted FROM public.warehouse_site WHERE uid=@Id", new { Supplier = supplierId, Id = warehouseId }));
                Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=2 AND confirmation_status=1 AND active_confirmation_id IS NOT NULL AND NOT is_deleted AND supplier_id=@Supplier FROM public.purchase_order_head WHERE uid=@Id", new { Id = purchaseId, Supplier = supplierId }));
                Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT quantity=3 AND product_id=@Product AND purchase_order_id=@Head AND NOT is_deleted FROM public.purchase_order_line WHERE uid=@Id", new { Id = purchaseLineId, Product = productId, Head = purchaseId }));
                Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inbound_document WHERE uid=ANY(@Ids) AND version=1 AND posting_status=0 AND NOT is_deleted AND warehouse_site_id=@Warehouse", new { Ids = heads, Warehouse = warehouseId }));
                Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inbound_document_line WHERE uid=ANY(@Ids) AND quantity=2 AND source_line_id=@Source AND NOT is_deleted", new { Ids = lines, Source = purchaseLineId }));
                Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE source_document_id=ANY(@Ids)", new { Ids = heads }));
                Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_balance WHERE warehouse_site_id=@Id", new { Id = warehouseId }));
                foreach (var head in heads)
                { var preview = await Run(sp => sp.GetRequiredService<IInboundPostingService>().PreviewPostAsync(head, new() { ExpectedVersion = 1 })); Assert.IsTrue(preview.CanPost); Assert.AreEqual(3m, preview.Lines.Single().RemainingQty); }
            }
            else
            {
            if (supplierId == Guid.Empty)
            {
                var supplier = await Run(sp => sp.GetRequiredService<SwiftExport.Application.Interfaces.Suppliers.Management.ISupplierService>().AddAsync(
                    new CreateItemRequest<SwiftExport.Contracts.DTOs.Suppliers.SupplierDto> { ClientKey = "PG-G3-6a67084f",
                        Data = new() { Version = 1, SupplierCode = "PGG36A67084F", SupplierName = "PG首次容量竞争专用供应商6a67084f", DocRelateNo = "PG-G3", Currency = "CNY" } }));
                // 源SQL及PG仓储均生成新增UID，必须以真实保存结果为准，不修改生产实现保留客户端ID。
                Assert.IsTrue(supplier.Success, supplier.ErrorCode + ":" + supplier.Message); Assert.IsNotNull(supplier.Uid); supplierId = supplier.Uid.Value; Assert.AreNotEqual(Guid.Empty, supplierId);
            }
            var warehouse = await Run(sp => sp.GetRequiredService<SwiftExport.Application.Interfaces.Inventory.Warehouses.IWarehouseManagementService>()
                .CreateSupplierVirtualWarehouseAsync(new() { CommandId = Id(10), SupplierId = supplierId, Code = "PG-G3-6a67084f", Name = "PG首次容量竞争专用仓6a67084f" }));
            warehouseId = warehouse.Uid;
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_balance WHERE warehouse_site_id=@Id", new { Id = warehouseId }));
            var purchase = await Run(sp => sp.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(new() { CommandId = Id(0),
                CustomerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f"), SupplierId = supplierId,
                OrderDate = new DateTime(6500, 10, 5), Remark = "PG首次容量竞争6a67084f",
                Lines = [new CreateIndependentPurchaseOrderLineCommand { ProductId = productId, Quantity = 3, UnitPrice = 10, TaxPrice = 10 }] }));
            purchaseId = purchase.PurchaseOrderId; purchaseLineId = purchase.Lines.Single().PurchaseOrderLineId; Assert.AreEqual(1, purchase.Version);
            var basis = await Run(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(purchaseId));
            var confirmed = await Run(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(purchaseId,
                new() { CommandId = Id(1), ExpectedVersion = 1, ContextFingerprint = basis.Fingerprint }));
            Assert.AreEqual(2, confirmed.Version); Assert.IsNotNull(confirmed.ConfirmationId);
            for (var i = 0; i < 2; i++)
            {
                var index = i;
                var draft = await Run(sp => sp.GetRequiredService<IInboundDraftService>().CreateAsync(new() { CommandId = Id(2 + index),
                    Draft = new InboundDraftData { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, WarehouseSiteId = warehouseId,
                        ReceiptDate = new DateTime(6500, 10, 5), Remark = "PG容量3各2-" + index, Lines = [new InboundDraftLineInput { SourceLineId = purchaseLineId, Quantity = 2 }] } }));
                heads[i] = draft.Uid; Assert.AreEqual(1, draft.Version);
                lines[i] = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.inbound_document_line WHERE inbound_document_id=@Id", new { Id = draft.Uid });
                var preview = await Run(sp => sp.GetRequiredService<IInboundPostingService>().PreviewPostAsync(draft.Uid, new() { ExpectedVersion = 1 }));
                Assert.IsTrue(preview.CanPost, string.Join(";", preview.Blockers.Select(x => x.Message))); Assert.AreEqual(3m, preview.Lines.Single().RemainingQty);
            }
            }
            // 原规则下活动草稿已阻断采购反向；不能把反向预览人为改成允许来构造另一业务语义。
            var cancellationPreview = await Run(sp => sp.GetRequiredService<IProcurementOrderReversalService>().PreviewAsync(purchaseId, ProcurementReversalOperation.CancelConfirmation, new() { ExpectedVersion = 2 }));
            Assert.IsFalse(cancellationPreview.CanExecute); Assert.IsTrue(cancellationPreview.Blockers.Any(x => x.Contains("草稿")));
            var cancel = new CancelProcurementOrderConfirmationCommand { CommandId = Id(6), ExpectedVersion = 2, Reason = "PG与两个入库请求竞争，原草稿资格阻断" };
            using (var holder = new DbSession(new PostgreSqlConnectionFactory(connection)))
            using (var cancelScope = app.Services.CreateScope())
            using (var firstScope = app.Services.CreateScope())
            using (var secondScope = app.Services.CreateScope())
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
            {
                var scopes = new[] { firstScope, secondScope }; var postSessions = scopes.Select(s => s.ServiceProvider.GetRequiredService<DbSession>()).ToArray();
                var cancelSession = cancelScope.ServiceProvider.GetRequiredService<DbSession>();
                Assert.AreEqual(5, postSessions.Select(Pid).Append(Pid(cancelSession)).Append(Pid(holder)).Append(Pid(observer)).Distinct().Count());
                using var barrier = holder.Connection.BeginTransaction();
                await holder.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.purchase_order_head WHERE uid=@Id FOR UPDATE", new { Id = purchaseId }, barrier);
                var cancelTask = cancelScope.ServiceProvider.GetRequiredService<IProcurementOrderReversalService>().CancelConfirmationAsync(purchaseId, cancel, deadline.Token);
                Task<ServiceResult<InboundPostResult>>[] posts = []; var observed = false;
                try
                {
                    // 先证明采购取消已进入真实头锁等待，再启动两次过账；都等待同一新采购，不以固定延迟代替服务器等待图。
                    await Observe("采购取消先排队", [Pid(cancelSession)], Pid(holder), deadline.Token);
                    posts = scopes.Select((s, i) => s.ServiceProvider.GetRequiredService<IInboundPostingService>().PostAsync(heads[i], new() { CommandId = Id(4 + i), ExpectedVersion = 1 }, deadline.Token)).ToArray();
                    await Observe("两次首次容量及采购取消等待", postSessions.Select(Pid).Append(Pid(cancelSession)).ToArray(), Pid(holder), deadline.Token);
                    Assert.IsFalse(cancelTask.IsCompleted); Assert.IsTrue(posts.All(t => !t.IsCompleted));
                    Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE source_document_id=ANY(@Ids)", new { Ids = heads })); observed = true;
                }
                finally
                {
                    barrier.Rollback(); if (!observed) deadline.Cancel();
                    try { await Task.WhenAll(posts.Cast<Task>().Append(cancelTask)); } catch when (!observed) { /* 屏障释放后所有任务收尾，保留观察失败。 */ }
                }
                Assert.IsTrue(observed);
                var cancelled = await cancelTask; var posted = await Task.WhenAll(posts);
                evidence.Add(new { Stage = "首次竞争结果", CancelPid = Pid(cancelSession), PostPids = postSessions.Select(Pid).ToArray(), Cancel = cancelled, Posts = posted,
                    Terminal = posts.Select(t => t.Status.ToString()).Append(cancelTask.Status.ToString()).ToArray() });
                Assert.IsFalse(cancelled.Success); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, cancelled.Error?.Code); StringAssert.Contains(cancelled.Message, "草稿");
                Assert.AreEqual(1, posted.Count(r => r.Success)); Assert.AreEqual(1, posted.Count(r => !r.Success));
                winner = Array.FindIndex(posted, r => r.Success); var loser = 1 - winner;
                Assert.AreEqual(2, posted[winner].Data.Version); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, posted[loser].Error?.Code); StringAssert.Contains(posted[loser].Message, "不能超量入库");
                foreach (var scope in scopes.Append(cancelScope)) Clean(scope.ServiceProvider);
                Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_locks WHERE pid=ANY(@Pids) AND locktype='advisory'", new { Pids = postSessions.Select(Pid).Append(Pid(cancelSession)).ToArray() }));
            }
            balanceId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Product", new { Warehouse = warehouseId, Product = productId });
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=2 AND on_hand_qty=2 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=2 AND confirmation_status=1 AND active_confirmation_id IS NOT NULL FROM public.purchase_order_head WHERE uid=@Id", new { Id = purchaseId }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(4), Id(5), Id(6) } }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(4), Id(5), Id(6) } }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=ANY(@Ids)", new { Ids = new[] { Id(4), Id(5), Id(6) } }));
            var previewReverse = await Run(sp => sp.GetRequiredService<IInboundReversalService>().PreviewReverseAsync(heads[winner], new() { ExpectedVersion = 2 }));
            Assert.IsTrue(previewReverse.CanReverse, string.Join(";", previewReverse.Blockers.Select(x => x.Message)));
            var reversed = await Run(sp => sp.GetRequiredService<IInboundReversalService>().ReverseAsync(heads[winner], new() { CommandId = Id(7), ExpectedVersion = 2, Reason = "PG首轮赢家完整反过账保留两流水" }));
            Assert.AreEqual(3, reversed.Version);
            var deleted = await Run(sp => sp.GetRequiredService<IInboundDraftService>().DeleteAsync(heads[1 - winner], new() { CommandId = Id(8), ExpectedVersion = 1, Reason = "PG仅本批未过账输家草稿原软删" }));
            Assert.IsTrue(deleted.IsDeleted);
            var allowed = await Run(sp => sp.GetRequiredService<IProcurementOrderReversalService>().PreviewAsync(purchaseId, ProcurementReversalOperation.CancelConfirmation, new() { ExpectedVersion = 2 }));
            Assert.IsTrue(allowed.CanExecute, string.Join(";", allowed.Blockers)); Assert.AreEqual(0, allowed.Contributions.Count);
            var final = await Run(sp => sp.GetRequiredService<IProcurementOrderReversalService>().CancelConfirmationAsync(purchaseId, new() { CommandId = Id(9), ExpectedVersion = 2, Reason = "PG完整反过账且草稿软删后原取消放行" }));
            Assert.AreEqual(3, final.Version); Assert.AreEqual(ConfirmationStatusEnum.待确认, final.ConfirmationStatus);
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=3 AND on_hand_qty=0 FROM public.inventory_balance WHERE uid=@Id", new { Id = balanceId }));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE source_document_id=ANY(@Ids)", new { Ids = heads }));
            Assert.AreEqual(0m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT sum(quantity_delta) FROM public.inventory_ledger_entry WHERE source_document_id=ANY(@Ids)", new { Ids = heads }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry r JOIN public.inventory_ledger_entry o ON o.uid=r.reversal_of_ledger_id WHERE o.source_document_id=@Id AND o.quantity_delta=2 AND r.quantity_delta=-2 AND r.source_document_id=o.source_document_id", new { Id = heads[winner] }));
            // 原成功命令从新scope重放；失败命令不自动再执，禁止把新快照上的拒绝当第一次竞争。
            var replay = await Run(sp => sp.GetRequiredService<IInboundPostingService>().PostAsync(heads[winner], new() { CommandId = Id(4 + winner), ExpectedVersion = 1 }));
            Assert.AreEqual(2, replay.Version);
            Assert.AreEqual(9, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(0, 11).Select(Id).ToArray() }));
            using var probe = app.Services.CreateScope();
            foreach (var command in Enumerable.Range(0, 11).Select(Id))
            { await using var lease = await ((ICommandTransactionPreparation)probe.ServiceProvider.GetRequiredService<ICommandReceiptRepository>()).AcquireBeforeTransactionAsync(command, default); Assert.IsNotNull(lease); }
            Clean(probe.ServiceProvider);
        }
        finally
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, FirstRun = retainedSupplier is null, FirstCapacityCompetition = true, PurchaseId = purchaseId, PurchaseLineId = purchaseLineId, Heads = heads, Lines = lines, Winner = winner,
                ProductId = productId, SupplierId = supplierId, WarehouseId = warehouseId, BalanceId = balanceId, Evidence = evidence,
                Policy = "原服务新增独立仓和余额V1到V2；全部旧余额/旧事实保护，原流水保留新增反向，本批输家草稿仅原软删；无清库/重置/DDL/运行库/备份写入" }));
        }
        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
        void Clean(IServiceProvider sp) { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        // 活动SQL受track_activity_query_size=1kB截断，长头投影尾部不可据LIKE判断；用真实等待图及采购表RowShare锁证明FOR UPDATE等待。
        async Task Observe(string stage, int[] pids, int holderPid, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var count = await observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT count(*) FROM pg_stat_activity a WHERE a.pid=ANY(@Pids) AND a.xact_start IS NOT NULL
                    AND cardinality(pg_blocking_pids(a.pid))>0
                    AND EXISTS(SELECT 1 FROM pg_locks l WHERE l.pid=a.pid AND l.relation='public.purchase_order_head'::regclass AND l.mode='RowShareLock' AND l.granted)
                    """, new { Pids = pids }, cancellationToken: token));
                if (count == pids.Length) break; await Task.Delay(20, token);
            }
            var graph = await observer.Connection.ExecuteScalarAsync<string>(new CommandDefinition("""
                SELECT jsonb_agg(jsonb_build_object('pid',pid,'blocking',pg_blocking_pids(pid),'xact_start',xact_start,'query',query) ORDER BY pid)::text
                FROM pg_stat_activity WHERE pid=ANY(@Pids)
                """, new { Pids = pids }, cancellationToken: token));
            evidence.Add(new { Stage = stage, HolderPid = holderPid, ObserverPid = Pid(observer), CandidatePids = pids, ServerBlockingGraph = graph });
        }
        // 每个预览/提交各自真实请求作用域，保留原默认事务及采购Serializable，不复用请求模拟下一入口。
        async Task<T> Run<T>(Func<IServiceProvider, Task<ServiceResult<T>>> action)
        {
            using var scope = app.Services.CreateScope(); var result = await action(scope.ServiceProvider);
            Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message); Clean(scope.ServiceProvider); return result.Data;
        }
    }
    /// <summary>新SO首次Gross接收争用及来源退出先于需求建立；明确保留原重复确认成功和未拆解新Gross硬删规则。</summary>
    [TestMethod]
    public Task RealPg_RuntimeFirstSourceReceptionAndExitBeforeEstablishment()
        => RunFirstSourceReceptionAndExitAsync("8530d46c-7b27-475a-80dc-0da6ca81d300", 6403, null);

    /// <summary>保留PG的NULL交期映射断言，以显式合法交期验证首次来源及确定退出次序；原SQL对照及NULL失败事实留在历史证据。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeExplicitDeliverySourceReceptionAndExitBeforeEstablishment()
    {
        const string batch = "94d5891b-327b-45d6-9c91-c395758bb400";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_SOURCE_EXIT_BATCH"));
        using var pg = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        Assert.IsNull(await pg.Connection.ExecuteScalarAsync<DateTime?>("SELECT NULL::timestamp"));
        var pgHeader = await pg.Connection.QuerySingleAsync<SwiftExport.Core.Entities.SalesOrders.SalesOrderHeader>("SELECT NULL::timestamp AS \"DeliveryDate\"");
        Assert.IsFalse(SqlMapper.Settings.ApplyNullValues); Assert.IsNotNull(pgHeader.DeliveryDate);
        Assert.IsTrue(Math.Abs((pgHeader.DeliveryDate.Value - DateTime.Now.AddDays(45)).TotalMinutes) < 1);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Probe = "PG的NULL实体默认值", RawPgNull = true, ApplyNullValues = SqlMapper.Settings.ApplyNullValues,
            PgMappedDefault = pgHeader.DeliveryDate, Scope = "常量SELECT无业务表写入；原SQL对照已留证，NULL映射问题仅记录，不改业务" }));
        await RunFirstSourceReceptionAndExitAsync(batch, 6404, new DateTime(6404, 11, 19));
    }

    /// <summary>本轮两个输入场景共用真实服务和同一锁屏障，NULL场景不放宽断言，显式交期另登记新来源；不重试已提交事实。</summary>
    private async Task RunFirstSourceReceptionAndExitAsync(string batch, int year, DateTime? deliveryDate)
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_SOURCE_EXIT_BATCH"));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(0, 6).Select(Id).ToArray() }), "只执行预登记首次，有成功事实必须另行登记接续。");
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month=@YearMonth", new { YearMonth = year + "10" }));
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u WHERE u.is_active=TRUE AND u.is_deleted=FALSE
            AND EXISTS(SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id WHERE ur.user_id=u.uid
                AND ur.is_deleted=FALSE AND r.is_active=TRUE AND r.is_deleted=FALSE AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid;
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor); await using var app = builder.Build();
        var productId = Guid.Parse("00008dec-d28a-4036-bc06-da0a2159e16b"); var customerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f");
        Guid salesId = Guid.Empty, grossId = Guid.Empty; string? grossBeforeExit = null;
        var evidence = new List<object>(); var confirmCommands = new ConfirmSalesOrderCommand[2];
        try
        {
            using (var draft = app.Services.CreateScope())
            {
                var sp = draft.ServiceProvider; var product = await sp.GetRequiredService<IRepository<Product>>().GetByIdAsync(productId); Assert.IsNotNull(product);
                var saved = await sp.GetRequiredService<ISalesOrderDraftService>().SaveAsync(new() { CommandId = Id(0),
                    Header = new SalesOrderEditHeaderDto { CustomerId = customerId, OrderDate = new DateTime(year, 10, 5), DeliveryDate = deliveryDate, Currency = "CNY", ExchangeRate = 1, CustomerPoNo = "PG-G2-" + batch[..8], Remark = "PG首次来源及退出交错" + batch[..8] },
                    Lines = [new SalesOrderEditLineDto { Uid = Id(1), ProductId = productId, Sku = product.Sku, Qty = 3, CnyPrice = 10, UsdPrice = 0 }] });
                Assert.IsTrue(saved.Success, saved.Error?.Code + ":" + saved.Message); salesId = saved.Data.Document.Header.Uid; Assert.AreEqual(1, saved.Data.Document.Header.Version); Clean(sp);
            }
            confirmCommands = new[] { 2, 3 }.Select(offset => new ConfirmSalesOrderCommand { CommandId = Id(offset), Uid = salesId, Version = 1 }).ToArray();
            using (var holderScope = app.Services.CreateScope())
            using (var firstScope = app.Services.CreateScope())
            using (var secondScope = app.Services.CreateScope())
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
            {
                var holder = holderScope.ServiceProvider.GetRequiredService<DbSession>(); var scopes = new[] { firstScope, secondScope };
                var sessions = scopes.Select(s => s.ServiceProvider.GetRequiredService<DbSession>()).ToArray(); Assert.AreEqual(3, sessions.Select(Pid).Append(Pid(holder)).Distinct().Count());
                var held = await PostgreSqlSourceDemandLock.AcquireBeforeTransactionAsync(holder, [salesId], [], Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, deadline.Token); Assert.IsNotNull(held);
                var pending = scopes.Select((s, i) => s.ServiceProvider.GetRequiredService<ISalesOrderConfirmationService>().ConfirmAsync(salesId, confirmCommands[i], deadline.Token)).ToArray();
                var observed = false; ServiceResult<SalesOrderConfirmationResultDto>[]? results = null;
                try
                {
                    foreach (var candidate in sessions) await Observe("首次Gross", candidate, holder, "Source", salesId, false, deadline.Token);
                    Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.gross_demand WHERE source_uid=@Id", new { Id = salesId })); observed = true;
                }
                finally
                {
                    await held.DisposeAsync(); if (!observed) deadline.Cancel();
                    try { results = await Task.WhenAll(pending); } catch when (!observed) { /* 所有任务收尾，保留首次观察失败。 */ }
                }
                Assert.IsTrue(observed); Assert.IsNotNull(results);
                // 原服务允许已确认同事实再次确认，两个不同命令都成功，但不重复推进头版本或创建Gross。
                foreach (var result in results) { Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message); Assert.AreEqual(2, result.Data.Version); Assert.AreEqual(1, result.Data.GrossDemandCount); }
                foreach (var scope in scopes) Clean(scope.ServiceProvider);
                evidence.Add(new { Stage = "首次Gross结果", Backends = sessions.Select(Pid).ToArray(), Results = results, Terminal = pending.Select(t => t.Status.ToString()).ToArray() });
            }
            grossId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.gross_demand WHERE source_uid=@Id AND source_line_id=@Line", new { Id = salesId, Line = Id(1) });
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=1 AND source_version=2 AND demand_qty=3 AND unit_snapshot='Pcs' FROM public.gross_demand WHERE uid=@Id", new { Id = grossId }));
            grossBeforeExit = await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(t)::text FROM public.gross_demand t WHERE uid=@Id", new { Id = grossId }); Assert.IsNotNull(grossBeforeExit);
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.net_demand WHERE gross_demand_id=@Id", new { Id = grossId }));
            var cancelInput = new CancelSalesOrderConfirmationCommand { CommandId = Id(4), Uid = salesId, Version = 2, Reason = "PG新来源尚未拆解，退出先于建立" };
            var establishInput = new EstablishMaterialDemandsCommand { CommandId = Id(5), Items = [new EstablishMaterialDemandItem { GrossDemandId = grossId, SourceDemandVersion = 1, SourceVersion = 2 }] };
            using (var holderScope = app.Services.CreateScope())
            using (var cancelScope = app.Services.CreateScope())
            using (var establishScope = app.Services.CreateScope())
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
            {
                var holder = holderScope.ServiceProvider.GetRequiredService<DbSession>(); var cancelSession = cancelScope.ServiceProvider.GetRequiredService<DbSession>(); var establishSession = establishScope.ServiceProvider.GetRequiredService<DbSession>();
                Assert.AreEqual(3, new[] { Pid(holder), Pid(cancelSession), Pid(establishSession) }.Distinct().Count());
                // 仅用既有原生Gross键作测试屏障：取消先取得Source并等Gross；建立只能随后等取消持有的Source，次序确定且都未开业务事务。
                var held = await PostgreSqlSourceDemandLock.AcquireBeforeTransactionAsync(holder, [], [grossId], Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, deadline.Token); Assert.IsNotNull(held);
                var cancelTask = cancelScope.ServiceProvider.GetRequiredService<ISalesOrderCancellationService>().CancelConfirmationAsync(salesId, cancelInput);
                Task<ServiceResult<EstablishMaterialDemandsResult>>? establishTask = null; var observed = false;
                try
                {
                    await Observe("退出等待Gross", cancelSession, holder, "Gross", grossId, true, deadline.Token);
                    Assert.IsTrue(cancelSession.SourceDemandLease!.Covers("Source", salesId));
                    establishTask = establishScope.ServiceProvider.GetRequiredService<IMaterialDemandEstablishmentService>().EstablishAsync(establishInput, deadline.Token);
                    await Observe("建立等待退出的Source", establishSession, cancelSession, "Source", salesId, false, deadline.Token);
                    Assert.IsFalse(cancelTask.IsCompleted); Assert.IsFalse(establishTask.IsCompleted);
                    Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.net_demand WHERE gross_demand_id=@Id", new { Id = grossId })); observed = true;
                }
                finally
                {
                    await held.DisposeAsync(); if (!observed) deadline.Cancel();
                    try { await Task.WhenAll(establishTask is null ? [cancelTask] : new Task[] { cancelTask, establishTask }); } catch when (!observed) { /* 无取消参数的原销售入口也必须等待终态，不改其API。 */ }
                }
                Assert.IsTrue(observed); Assert.IsNotNull(establishTask); var cancelled = await cancelTask; var established = await establishTask;
                Assert.IsTrue(cancelled.Success, cancelled.Error?.Code + ":" + cancelled.Message); Assert.AreEqual(3, cancelled.Data.Version);
                Assert.AreEqual(1, cancelled.Data.HardDeletedGrossDemandCount); Assert.AreEqual(0, cancelled.Data.VoidedGrossDemandCount);
                Assert.IsFalse(established.Success); Assert.AreEqual("SOURCE_NOT_FOUND", established.Error?.Code);
                Clean(cancelScope.ServiceProvider); Clean(establishScope.ServiceProvider);
                evidence.Add(new { Stage = "退出先提交结果", Backends = new[] { Pid(cancelSession), Pid(establishSession) }, Cancel = cancelled, Establish = established, Terminal = new[] { cancelTask.Status.ToString(), establishTask.Status.ToString() } });
            }
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.gross_demand WHERE source_uid=@Id", new { Id = salesId }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.net_demand WHERE gross_demand_id=@Id", new { Id = grossId }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=3 AND confirmation_status=0 AND NOT is_deleted FROM public.sales_order_header WHERE uid=@Id", new { Id = salesId }));
            Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(0, 6).Select(Id).ToArray() }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id", new { Id = Id(5) }));
            using (var replayScope = app.Services.CreateScope())
            {
                var sp = replayScope.ServiceProvider;
                var replay = await sp.GetRequiredService<ISalesOrderCancellationService>().CancelConfirmationAsync(salesId, cancelInput); Assert.IsTrue(replay.Success); Assert.AreEqual(1, replay.Data.HardDeletedGrossDemandCount);
                foreach (var input in confirmCommands)
                { var result = await sp.GetRequiredService<ISalesOrderConfirmationService>().ConfirmAsync(salesId, input); Assert.IsTrue(result.Success); Assert.AreEqual(2, result.Data.Version); }
                // 回执返回原结果，不得因当前已退出重放旧确认而重新接收来源或推进现头版本。
                Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.gross_demand WHERE source_uid=@Id", new { Id = salesId })); Clean(sp);
                foreach (var id in new[] { Id(2), Id(3), Id(4), Id(5) })
                { await using var commandLease = await ((ICommandTransactionPreparation)sp.GetRequiredService<ICommandReceiptRepository>()).AcquireBeforeTransactionAsync(id, default); Assert.IsNotNull(commandLease); }
                await using var sourceLease = await PostgreSqlSourceDemandLock.AcquireBeforeTransactionAsync(sp.GetRequiredService<DbSession>(), [salesId], [grossId], Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default); Assert.IsNotNull(sourceLease);
            }
        }
        finally
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, FirstRun = true, SalesId = salesId, SalesLineId = Id(1), GrossId = grossId, GrossBeforeExit = grossBeforeExit, ProductId = productId, CustomerId = customerId, Evidence = evidence,
                Policy = "仅本批未拆解新Gross按原销售取消规则删除；原数据全保护，其余成功事实/号段留存；不清库/重置/DDL/运行库/备份写入" }));
        }

        Guid Id(int offset) => Guid.Parse(batch[..^2] + offset.ToString("x2"));
        int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
        // 请求返回及重放必须释放原事务/命令/来源准备，不能只看业务响应。
        void Clean(IServiceProvider sp)
        { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.CommandLease); Assert.IsNull(session.SourceDemandLease); }
        // 真实try-lock目录+owner同键+候选完整当前租约/未覆盖目标键证明首事务前等待；Source/Gross屏障分别记载。
        async Task Observe(string stage, DbSession candidate, DbSession owner, string family, Guid id, bool sourceHeld, CancellationToken token)
        {
            var key = PostgreSqlSourceDemandLock.Key(family, id); var bits = unchecked((ulong)key);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var observed = await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                    SELECT EXISTS(SELECT 1 FROM pg_stat_activity a WHERE a.pid=@Candidate AND a.backend_xid IS NULL AND a.xact_start IS NULL AND a.query LIKE '%pg_try_advisory_lock%')
                    AND EXISTS(SELECT 1 FROM pg_locks l WHERE l.pid=@Owner AND l.locktype='advisory' AND l.granted AND l.objsubid=1 AND l.classid::bigint=@High AND l.objid::bigint=@Low)
                    """, new { Candidate = Pid(candidate), Owner = Pid(owner), High = (long)(bits >> 32), Low = (long)(bits & uint.MaxValue) }, cancellationToken: token));
                if (observed && candidate.Transaction is null && candidate.SourceDemandLease is { IsCurrent: true } lease && !lease.Covers(family, id) && lease.Covers("Source", salesId) == sourceHeld) break;
                await Task.Delay(20, token);
            }
            evidence.Add(new { Stage = stage, Candidate = Pid(candidate), Owner = Pid(owner), Observer = Pid(observer), Family = family, Identity = id, Key = key, TransactionNull = true, SourceHeld = sourceHeld, ServerTryLockObserved = true });
        }
    }

    /// <summary>从新SO真实来源验证首次Net/Link竞争、部分材料准备取消及补关联/回退交错；不重试业务、不复位已提交事实。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeFirstDemandAndLinkCompetition()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        const string batch = "6dd97054-21ae-481a-93ab-28d4c9502200";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_FIRST_DEMAND_BATCH"));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(0, 16).Select(Id).ToArray() }), "只执行本批首次；有成功事实必须另登记接续，不能换身份重跑竞争。");
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='640210'"));
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u
            WHERE u.is_active=TRUE AND u.is_deleted=FALSE AND EXISTS(
                SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
                WHERE ur.user_id=u.uid AND ur.is_deleted=FALSE AND r.is_active=TRUE AND r.is_deleted=FALSE AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid;
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        var productId = Guid.Parse("00008dec-d28a-4036-bc06-da0a2159e16b");
        var customerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f");
        var supplierId = Guid.Parse("017b0b30-2744-4e4b-8bfb-1169e5be98eb");
        Guid salesId = Guid.Empty, grossId = Guid.Empty, netId = Guid.Empty, purchaseId = Guid.Empty, independentId = Guid.Empty, independentLineId = Guid.Empty;
        var evidence = new List<object>();
        try
        {
            using (var sourceScope = app.Services.CreateScope())
            {
                var sp = sourceScope.ServiceProvider;
                var product = await sp.GetRequiredService<IRepository<Product>>().GetByIdAsync(productId);
                Assert.IsNotNull(product); Assert.AreEqual(0, product.BomVersion); Assert.AreEqual("Pcs", product.Unit);
                var saved = await sp.GetRequiredService<ISalesOrderDraftService>().SaveAsync(new SaveSalesOrderRequest
                {
                    CommandId = Id(0), Header = new SalesOrderEditHeaderDto { CustomerId = customerId, OrderDate = new DateTime(6402, 10, 5), Currency = "CNY", ExchangeRate = 1, CustomerPoNo = "PG-G2-6dd97054", Remark = "PG首次需求竞争6dd97054" },
                    Lines = [new SalesOrderEditLineDto { Uid = Id(1), ProductId = productId, Sku = product.Sku, Qty = 3, CnyPrice = 10, UsdPrice = 0 }]
                });
                Assert.IsTrue(saved.Success, saved.Error?.Code + ":" + saved.Message); salesId = saved.Data.Document.Header.Uid;
                var confirmed = await sp.GetRequiredService<ISalesOrderConfirmationService>().ConfirmAsync(salesId, new() { CommandId = Id(2), Uid = salesId, Version = saved.Data.Document.Header.Version });
                Assert.IsTrue(confirmed.Success, confirmed.Error?.Code + ":" + confirmed.Message); Assert.AreEqual(1, confirmed.Data.GrossDemandCount);
                Clean(sp);
            }
            grossId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.gross_demand WHERE source_uid=@Id AND source_line_id=@Line", new { Id = salesId, Line = Id(1) });
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.net_demand WHERE gross_demand_id=@Id", new { Id = grossId }));

            // 两不同建立命令先在同一第三连接来源键前轮询，释放后先者创建、后者看到新活动修订，均保持原Serializable。
            var establishInputs = new[] { 3, 4 }.Select(offset => new EstablishMaterialDemandsCommand { CommandId = Id(offset), Items = [new EstablishMaterialDemandItem { GrossDemandId = grossId, SourceDemandVersion = 1, SourceVersion = 2 }] }).ToArray();
            var established = await Race("首次Net", Enumerable.Range(0, 2).Select<int, Func<IServiceProvider, CancellationToken, Task<ServiceResult<EstablishMaterialDemandsResult>>>>(i =>
                (sp, ct) => sp.GetRequiredService<IMaterialDemandEstablishmentService>().EstablishAsync(establishInputs[i], ct)).ToArray(), []);
            foreach (var result in established) Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message);
            Assert.AreEqual(1, established.Sum(x => x.Data.CreatedSourceCount)); Assert.AreEqual(1, established.Sum(x => x.Data.AlreadyEstablishedCount));
            netId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.net_demand WHERE gross_demand_id=@Id AND lifecycle_status=0 AND is_deleted=FALSE", new { Id = grossId });
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT decomposition_revision=1 AND version=1 AND net_qty=3 FROM public.net_demand WHERE uid=@Id", new { Id = netId }));
            foreach (var offset in new[] { 3, 4 }) Assert.AreEqual(1, await ReceiptCount(Id(offset)));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.demand_execution_link WHERE net_demand_id=@Id", new { Id = netId }));

            var transfers = new[] { await PreviewTransfer(Id(5), Id(7)), await PreviewTransfer(Id(6), Id(8)), await PreviewTransfer(Id(9), Id(10)) };
            // 真实FromDemand先取得Item再等来源；在业务事务之前取消，所有已取得资源必须释放且不能生成事实。
            using (var holderScope = app.Services.CreateScope())
            using (var candidateScope = app.Services.CreateScope())
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
            {
                var holder = holderScope.ServiceProvider.GetRequiredService<DbSession>();
                var candidate = candidateScope.ServiceProvider.GetRequiredService<DbSession>();
                var held = await ((ISourceDemandTransactionPreparation)holderScope.ServiceProvider.GetRequiredService<IGrossDemandDocumentRepository>()).AcquireBeforeTransactionAsync([salesId], [grossId]);
                Assert.IsNotNull(held);
                var pending = candidateScope.ServiceProvider.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(transfers[2], cancel.Token);
                try
                {
                    await Observe("部分准备取消", [candidate], holder, [Id(10)], cancel.Token);
                    cancel.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
                    Clean(candidateScope.ServiceProvider);
                    Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype='advisory'", new { Pid = Pid(candidate) }));
                    Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.demand_execution_link WHERE command_item_id=@Id", new { Id = Id(10) }));
                }
                finally
                {
                    cancel.Cancel(); await held.DisposeAsync();
                    try { await pending; } catch (OperationCanceledException) { }
                }
            }

            // 两次真实预览都基于余3，各拟采购2；第二请求必须在默认事务首快照前等来源并按最新余1拒绝。
            var transferred = await Race("首次Link", Enumerable.Range(0, 2).Select<int, Func<IServiceProvider, CancellationToken, Task<ServiceResult<CreatePurchaseDraftsResult>>>>(i =>
                (sp, ct) => sp.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(transfers[i], ct)).ToArray(), [Id(7), Id(8)]);
            foreach (var result in transferred) Assert.IsTrue(result.Success, result.Error?.Code + ":" + result.Message);
            var sourceResults = transferred.Select(x => x.Data.Sources.Single()).ToArray();
            Assert.AreEqual(1, sourceResults.Count(x => x.Outcome == NetDemandCommandOutcomeEnum.Succeeded));
            Assert.AreEqual(1, sourceResults.Count(x => x.Outcome == NetDemandCommandOutcomeEnum.Failed));
            Assert.AreEqual("PREVIEW_EXPIRED", sourceResults.Single(x => x.Outcome == NetDemandCommandOutcomeEnum.Failed).ErrorCode);
            var winner = Array.FindIndex(sourceResults, x => x.Outcome == NetDemandCommandOutcomeEnum.Succeeded);
            purchaseId = sourceResults[winner].PurchaseDrafts.Single().PurchaseOrderId;
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.demand_execution_link WHERE net_demand_id=@Id", new { Id = netId }));
            Assert.AreEqual(2m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT sum(allocated_qty-released_qty) FROM public.demand_execution_link WHERE net_demand_id=@Id", new { Id = netId }));
            using (var replayScope = app.Services.CreateScope())
            {
                var replay = await replayScope.ServiceProvider.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(transfers[winner]);
                Assert.IsTrue(replay.Success); Assert.AreEqual(NetDemandCommandOutcomeEnum.AlreadyApplied, replay.Data.Sources.Single().Outcome);
                Assert.AreEqual(purchaseId, replay.Data.Sources.Single().PurchaseDrafts.Single().PurchaseOrderId); Clean(replayScope.ServiceProvider);
            }
            using (var reverseScope = app.Services.CreateScope())
            {
                var sp = reverseScope.ServiceProvider;
                var preview = await sp.GetRequiredService<IProcurementOrderReversalService>().PreviewAsync(purchaseId, ProcurementReversalOperation.Void, new() { ExpectedVersion = 1 });
                Assert.IsTrue(preview.Success); Assert.IsTrue(preview.Data.CanExecute); Assert.AreEqual(2m, preview.Data.Contributions.Sum(x => x.Quantity));
                var released = await sp.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(purchaseId, new() { CommandId = Id(11), ExpectedVersion = 1, Reason = "PG首次竞争完成后释放本批采购贡献" });
                Assert.IsTrue(released.Success, released.Error?.Code + ":" + released.Message); Clean(sp);
            }
            using (var draftScope = app.Services.CreateScope())
            {
                var created = await draftScope.ServiceProvider.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(new() { CommandId = Id(12), CustomerId = customerId, SupplierId = supplierId, OrderDate = new DateTime(6402, 10, 5),
                    Remark = "PG关联回退竞争6dd97054", Lines = [new CreateIndependentPurchaseOrderLineCommand { ProductId = productId, Quantity = 1, UnitPrice = 10, TaxPrice = 10 }] });
                Assert.IsTrue(created.Success, created.Error?.Code + ":" + created.Message); independentId = created.Data.PurchaseOrderId; independentLineId = created.Data.Lines.Single().PurchaseOrderLineId; Clean(draftScope.ServiceProvider);
            }
            var associationItem = new ProcurementDemandAssociationItem { CommandItemId = Id(14), NetDemandId = netId, ExpectedDemandVersion = 1, ExpectedSourceDemandVersion = 1, PurchaseOrderLineId = independentLineId, ExpectedLineVersion = 1, Quantity = 1 };
            string associationToken;
            using (var previewScope = app.Services.CreateScope())
            {
                var preview = await previewScope.ServiceProvider.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(independentId, new() { ExpectedVersion = 1, Items = [associationItem] });
                Assert.IsTrue(preview.Success, preview.Error?.Code + ":" + preview.Message); associationToken = preview.Data.Token; Clean(previewScope.ServiceProvider);
            }
            var association = new AssociateProcurementDemandsCommand { CommandId = Id(13), ExpectedVersion = 1, PreviewToken = associationToken, Reason = "PG真实补关联与需求回退交错", Items = [associationItem] };
            var rollback = new RollbackMaterialDemandsCommand { CommandId = Id(15), Reason = "PG真实需求回退与补关联交错", Items = [new RollbackMaterialDemandItem { GrossDemandId = grossId, DecompositionRevision = 1 }] };
            var raced = await Race<object>("补关联与回退", [
                async (sp, ct) => (object)await sp.GetRequiredService<IProcurementDemandAssociationService>().AssociateAsync(independentId, association, ct),
                async (sp, ct) => (object)await sp.GetRequiredService<IMaterialDemandReversalService>().RollbackAsync(rollback, ct)], [Id(14), Guid.Empty]);
            var associated = (ServiceResult<ProcurementOrderCommandResult>)raced[0]; var rolled = (ServiceResult<RollbackMaterialDemandsResult>)raced[1];
            Assert.AreNotEqual(associated.Success, rolled.Success);
            if (associated.Success)
            {
                Assert.AreEqual("DOWNSTREAM_EXECUTION_EXISTS", rolled.Error?.Code); Assert.AreEqual(2, associated.Data.Version);
                Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=1 AND lifecycle_status=0 FROM public.net_demand WHERE uid=@Id", new { Id = netId }));
                Assert.AreEqual(1m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT sum(allocated_qty-released_qty) FROM public.demand_execution_link WHERE net_demand_id=@Id AND link_status=0", new { Id = netId }));
            }
            else
            {
                Assert.AreEqual(ServiceErrorCodes.VersionConflict, associated.Error?.Code); Assert.AreEqual(1, rolled.Data.RolledBackSourceCount);
                Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=2 AND lifecycle_status=8 FROM public.net_demand WHERE uid=@Id", new { Id = netId }));
                Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.demand_execution_link WHERE net_demand_id=@Id AND link_status=0", new { Id = netId }));
            }
            Assert.AreEqual(1, await ReceiptCount(Id(13)) + await ReceiptCount(Id(15)));
            evidence.Add(new { Stage = "最终业务", Association = associated.Success, AssociationError = associated.Error?.Code, Rollback = rolled.Success, RollbackError = rolled.Error?.Code, PurchaseWinner = winner });
            // 最后新第三请求取得所有材料/Item及各父命令身份，证明取消/竞争失败没有遗留会话租约。
            using (var probeScope = app.Services.CreateScope())
            {
                var sp = probeScope.ServiceProvider; var probe = sp.GetRequiredService<DbSession>();
                foreach (var id in new[] { Id(0), Id(2), Id(3), Id(4), Id(11), Id(12), Id(13), Id(15) })
                { await using var commandLease = await ((ICommandTransactionPreparation)sp.GetRequiredService<ICommandReceiptRepository>()).AcquireBeforeTransactionAsync(id, default); Assert.IsNotNull(commandLease); }
                await using var lease = await ((IMaterialDemandTransactionPreparation)sp.GetRequiredService<INetDemandExecutionLinkRepository>()).AcquireMaterialBeforeTransactionAsync([salesId], [grossId], [netId], [Id(7), Id(8), Id(10), Id(14)]);
                Assert.IsNotNull(lease); Assert.IsTrue(probe.SourceDemandLease!.Covers("CommandItem", Id(10)));
            }
        }
        finally
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, FirstRun = true, SalesId = salesId, SalesLineId = Id(1), GrossId = grossId, NetId = netId, PurchaseId = purchaseId,
                IndependentId = independentId, IndependentLineId = independentLineId, ProductId = productId, CustomerId = customerId, SupplierId = supplierId, Evidence = evidence,
                Policy = "所有成功事实留存；原PO/202610号段仅正常推进一次，不复位；仅failure，无DDL/运行库/备份写入" }));
        }

        Guid Id(int offset) => Guid.Parse("6dd97054-21ae-481a-93ab-28d4c95022" + offset.ToString("x2"));
        int Pid(DbSession session) => ((NpgsqlConnection)session.Connection).ProcessID;
        Task<int> ReceiptCount(Guid id) => observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = id });
        // 原生服务每阶段结束后必须同时释放事务、命令和来源准备，不只检查请求返回值。
        void Clean(IServiceProvider sp)
        { var session = sp.GetRequiredService<DbSession>(); Assert.IsNull(session.Transaction); Assert.IsNull(session.SourceDemandLease); Assert.IsNull(session.CommandLease); }

        // 两候选使用独立预览身份，原数量/单位/客户规则完整执行；取消输入也经合法预览而非伪造事实。
        async Task<CreatePurchaseDraftsCommand> PreviewTransfer(Guid request, Guid item)
        {
            using var scope = app.Services.CreateScope(); var sp = scope.ServiceProvider;
            var input = new PurchaseDraftPreviewRequest { ExecutionRequestId = request, EntryMode = PurchaseDraftEntryModeEnum.ManualSelection, LineMode = PurchaseDraftLineModeEnum.KeepSourceLines,
                Items = [new PurchaseDraftPreviewItem { CommandItemId = item, NetDemandId = netId, ExpectedVersion = 1, ExpectedRemainingQty = 3, PurchaseQty = 2, SupplierId = supplierId, UnitPrice = 10, TaxPrice = 10 }] };
            var preview = await sp.GetRequiredService<IPurchaseDraftPreviewService>().PreviewAsync(input);
            Assert.IsTrue(preview.Success, preview.Error?.Code + ":" + preview.Message); Assert.IsFalse(preview.Data.HasBlockers);
            var group = preview.Data.SourceGroups.Single(); Clean(sp);
            return new() { ExecutionRequestId = request, EntryMode = input.EntryMode, PreviewToken = preview.Data.PreviewToken,
                Sources = [new CreatePurchaseDraftSourceCommand { SourceType = "SO", SourceUid = salesId, SourcePreviewToken = group.SourcePreviewToken, LineMode = input.LineMode,
                    Items = [new CreatePurchaseDraftItemCommand { CommandItemId = item, NetDemandId = netId, ExpectedVersion = 1, ExpectedRemainingQty = 3, PurchaseQty = 2, SupplierId = supplierId, UnitPrice = 10, TaxPrice = 10 }],
                    ConfirmedWarningCodes = group.Warnings.Where(x => x.RequiresConfirmation).Select(x => x.Code).Distinct().ToList() }] };
        }

        // 第三连接持同来源整组租约；观察实际try-lock与未开事务后才释放，所有发起任务必须等待终态。
        async Task<T[]> Race<T>(string stage, Func<IServiceProvider, CancellationToken, Task<T>>[] actions, Guid[] items)
        {
            using var holderScope = app.Services.CreateScope(); using var firstScope = app.Services.CreateScope(); using var secondScope = app.Services.CreateScope();
            var scopes = new[] { firstScope, secondScope }; var sessions = scopes.Select(x => x.ServiceProvider.GetRequiredService<DbSession>()).ToArray();
            var holder = holderScope.ServiceProvider.GetRequiredService<DbSession>();
            Assert.AreEqual(3, sessions.Select(Pid).Append(Pid(holder)).Distinct().Count());
            var held = await ((ISourceDemandTransactionPreparation)holderScope.ServiceProvider.GetRequiredService<IGrossDemandDocumentRepository>()).AcquireBeforeTransactionAsync([salesId], [grossId]);
            Assert.IsNotNull(held); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var pending = actions.Select((action, i) => action(scopes[i].ServiceProvider, deadline.Token)).ToArray();
            T[]? results = null; var observed = false;
            try
            { await Observe(stage, sessions, holder, items, deadline.Token); observed = true; }
            finally
            {
                await held.DisposeAsync(); if (!observed) deadline.Cancel();
                try { results = await Task.WhenAll(pending); } catch when (!observed) { /* 保留观察失败，全部任务已收尾。 */ }
            }
            Assert.IsTrue(observed); Assert.IsNotNull(results);
            foreach (var scope in scopes) Clean(scope.ServiceProvider);
            evidence.Add(new { Stage = stage, Backends = sessions.Select(Pid).ToArray(), Results = results, Terminal = pending.Select(x => x.Status.ToString()).ToArray() }); return results;
        }

        // 当前生产采用try-lock轮询，服务器不存在blocking_pids等待边；只记录实有owner键/请求SQL/无业务事务及部分Item覆盖。
        async Task Observe(string stage, DbSession[] candidates, DbSession holder, Guid[] items, CancellationToken token)
        {
            var key = PostgreSqlSourceDemandLock.Key("Source", salesId); var bits = unchecked((ulong)key); var pids = candidates.Select(Pid).ToArray();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var count = await observer.Connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                    SELECT count(*) FROM pg_stat_activity a WHERE a.pid=ANY(@Pids) AND a.backend_xid IS NULL AND a.xact_start IS NULL
                      AND a.query LIKE '%pg_try_advisory_lock%' AND EXISTS(
                        SELECT 1 FROM pg_locks l WHERE l.pid=@Holder AND l.locktype='advisory' AND l.granted AND l.objsubid=1
                          AND l.classid::bigint=@High AND l.objid::bigint=@Low)
                    """, new { Pids = pids, Holder = Pid(holder), High = (long)(bits >> 32), Low = (long)(bits & uint.MaxValue) }, cancellationToken: token));
                var prepared = candidates.Select((s, i) => s.Transaction is null && s.SourceDemandLease is { IsCurrent: true } lease
                    && !lease.Covers("Source", salesId) && (items.Length == 0 || items[i] == Guid.Empty || lease.Covers("CommandItem", items[i]))).All(x => x);
                if (count == candidates.Length && prepared) break;
                await Task.Delay(20, token);
            }
            var nets = await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.net_demand WHERE gross_demand_id=@Id", new { Id = grossId });
            var links = netId == Guid.Empty ? 0 : await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.demand_execution_link WHERE net_demand_id=@Id", new { Id = netId });
            if (stage == "首次Net") Assert.AreEqual(0, nets);
            if (stage is "首次Link" or "部分准备取消") Assert.AreEqual(0, links);
            evidence.Add(new { Stage = stage + "事务前等待", Backends = pids, Holder = Pid(holder), Observer = Pid(observer), OwnerKey = key, NetCount = nets, LinkCount = links,
                TransactionNull = candidates.All(s => s.Transaction is null), ItemCovered = candidates.Select((s, i) => items.Length != 0 && items[i] != Guid.Empty && s.SourceDemandLease!.Covers("CommandItem", items[i])).ToArray(), ServerTryLockObserved = true });
        }
    }

    /// <summary>接续已登记SO标准来源，真实创建独立采购并补关联，确认、入库正反向、删行/作废释放及需求/来源退出；成功事实和首次/重放分支均登记保留。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeStandardSourceAssociationAndReversalChain()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        const string batch = "3cbe8ab3-be60-4af8-b36b-3e04e1206ab0";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_STANDARD_CHAIN_BATCH"), "必须显式指定本批接续身份，不能自行重置或造来源。");
        var sourcePurchaseId = Guid.Parse("7d6ceca7-6d9e-4239-8a8b-4a35028ebec2");
        var sourcePurchaseLineId = Guid.Parse("1c32eac3-ccd6-4beb-8bba-4a35028ebec3");
        var netId = Guid.Parse("139c9d7b-de16-4fc9-b9b0-4a35028ebdf9");
        var grossId = Guid.Parse("7309ef86-e59b-4d78-ae38-4a35028ebda0");
        var salesId = Guid.Parse("fac8a163-27a8-470f-ae9d-4a35028ebd23");
        var productId = Guid.Parse("00008dec-d28a-4036-bc06-da0a2159e16b");
        var warehouseId = Guid.Parse("021eb2cc-4a61-474e-8b3d-98697b4e2ae5");
        var customerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f");
        var supplierId = Guid.Parse("017b0b30-2744-4e4b-8bfb-1169e5be98eb");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var initialReceipts = (await observer.Connection.QueryAsync<Guid>("SELECT command_id FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = Enumerable.Range(0, 15).Select(Id).ToArray() })).ToArray();
        var firstRun = initialReceipts.Length == 0;
        if (firstRun)
        {
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT version=2 AND confirmation_status=0 AND origin_kind=1 FROM public.purchase_order_head WHERE uid=@Id", new { Id = sourcePurchaseId }));
            Assert.AreEqual(2m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT sum(allocated_qty-released_qty) FROM public.demand_execution_link WHERE net_demand_id=@Id AND link_status=0 AND is_deleted=FALSE", new { Id = netId }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_balance WHERE product_id=@Product", new { Product = productId }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='640110'"));
        }
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u
            WHERE u.is_active=TRUE AND u.is_deleted=FALSE AND EXISTS(
                SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
                WHERE ur.user_id=u.uid AND ur.is_deleted=FALSE AND r.is_active=TRUE AND r.is_deleted=FALSE AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid;
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        var backends = new List<int[]>();
        var completed = new List<Guid>();
        Guid independentId = Guid.Empty, independentLineId = Guid.Empty, inboundId = Guid.Empty;
        try
        {
            var create = new CreateIndependentPurchaseOrderCommand { CommandId = Id(0), CustomerId = customerId, SupplierId = supplierId, OrderDate = new DateTime(6401, 10, 5),
                Remark = "PG标准来源补关联3cbe8ab3", Lines = [new CreateIndependentPurchaseOrderLineCommand { ProductId = productId, Quantity = 1, UnitPrice = 10, TaxPrice = 10 }] };
            var independent = await Commit(sp => sp.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(create), Id(0));
            independentId = independent.PurchaseOrderId; independentLineId = independent.Lines.Single().PurchaseOrderLineId;
            Assert.AreEqual(1, independent.Version);
            var item = new ProcurementDemandAssociationItem { CommandItemId = Id(15), PurchaseOrderLineId = independentLineId, ExpectedLineVersion = 1,
                NetDemandId = netId, ExpectedDemandVersion = 1, ExpectedSourceDemandVersion = 1, Quantity = 1 };
            var associationToken = await FrozenToken(Id(1), async () =>
            {
                var preview = await Read(sp => sp.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(independentId, new() { ExpectedVersion = 1, Items = [item] }));
                Assert.AreEqual(1m, preview.Items.Single().DemandRemainingBefore); Assert.AreEqual(0m, preview.Items.Single().DemandRemainingAfter);
                Assert.AreEqual(1m, preview.Items.Single().LineUnlinkedBefore); Assert.AreEqual(0m, preview.Items.Single().LineUnlinkedAfter);
                return preview.Token;
            });
            var association = new AssociateProcurementDemandsCommand { CommandId = Id(1), ExpectedVersion = 1, PreviewToken = associationToken, Reason = "PG原标准来源补关联", Items = [item] };
            var associated = await Commit(sp => sp.GetRequiredService<IProcurementDemandAssociationService>().AssociateAsync(independentId, association), Id(1));
            Assert.AreEqual(2, associated.Version); Assert.AreEqual(2, associated.Lines.Single().Version);
            if (!await HasReceipt(Id(12)))
            {
                var planBefore = await PlanDigest();
                using var scope = app.Services.CreateScope();
                var blocked = await scope.ServiceProvider.GetRequiredService<IMaterialDemandReversalService>().RollbackAsync(new() { CommandId = Id(14), Reason = "PG有采购贡献拒绝回退",
                    Items = [new RollbackMaterialDemandItem { GrossDemandId = grossId, DecompositionRevision = 1 }] });
                Assert.IsFalse(blocked.Success); Assert.AreEqual("DOWNSTREAM_EXECUTION_EXISTS", blocked.Error?.Code);
                Assert.IsNull(scope.ServiceProvider.GetRequiredService<DbSession>().Transaction);
                Assert.AreEqual(planBefore, await PlanDigest()); Assert.IsFalse(await HasReceipt(Id(14)));
            }
            var sourceBasis = await FrozenToken(Id(2), async () => (await Read(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(sourcePurchaseId))).Fingerprint);
            var sourceConfirm = new ConfirmProcurementOrderCommand { CommandId = Id(2), ExpectedVersion = 2, ContextFingerprint = sourceBasis };
            var sourceConfirmed = await Commit(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(sourcePurchaseId, sourceConfirm), Id(2));
            Assert.AreEqual(3, sourceConfirmed.Version); Assert.IsNotNull(sourceConfirmed.ConfirmationId);
            var independentBasis = await FrozenToken(Id(3), async () => (await Read(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(independentId))).Fingerprint);
            var independentConfirm = new ConfirmProcurementOrderCommand { CommandId = Id(3), ExpectedVersion = 2, ContextFingerprint = independentBasis };
            var independentConfirmed = await Commit(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(independentId, independentConfirm), Id(3));
            Assert.AreEqual(3, independentConfirmed.Version); Assert.IsNotNull(independentConfirmed.ConfirmationId);

            // 单据和行由实际草稿服务生成，两个采购来源共享同一产品余额；不直接插入入库或库存事实。
            var createInbound = new CreateInboundDraftCommand { CommandId = Id(4), Draft = new InboundDraftData { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, WarehouseSiteId = warehouseId,
                ReceiptDate = new DateTime(6401, 10, 5), Remark = "PG标准来源联合入库3cbe8ab3", Lines = [new InboundDraftLineInput { SourceLineId = sourcePurchaseLineId, Quantity = 1 }, new InboundDraftLineInput { SourceLineId = independentLineId, Quantity = 1 }] } };
            var draft = await Commit(sp => sp.GetRequiredService<IInboundDraftService>().CreateAsync(createInbound), Id(4));
            inboundId = draft.Uid; Assert.AreEqual(1, draft.Version);
            if (!await HasReceipt(Id(5)))
            {
                var preview = await Read(sp => sp.GetRequiredService<IInboundPostingService>().PreviewPostAsync(inboundId, new() { ExpectedVersion = 1 }));
                Assert.IsTrue(preview.CanPost, string.Join(";", preview.Blockers.Select(x => x.Code + ":" + x.Message))); Assert.AreEqual(2, preview.Lines.Count);
            }
            var post = new PostInboundCommand { CommandId = Id(5), ExpectedVersion = 1 };
            var posted = await Commit(sp => sp.GetRequiredService<IInboundPostingService>().PostAsync(inboundId, post), Id(5));
            Assert.AreEqual(2, posted.Version); Assert.AreEqual(InboundPostingStatusEnum.Posted, posted.PostingStatus);
            if (!await HasReceipt(Id(6)))
            {
                Assert.AreEqual(2m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Product", new { Warehouse = warehouseId, Product = productId }));
                var blockedPreview = await Read(sp => sp.GetRequiredService<IProcurementOrderReversalService>().PreviewAsync(sourcePurchaseId, ProcurementReversalOperation.CancelConfirmation, new() { ExpectedVersion = 3 }));
                Assert.IsFalse(blockedPreview.CanExecute); Assert.IsTrue(blockedPreview.Blockers.Count > 0);
                var before = await PlanDigest();
                using var scope = app.Services.CreateScope();
                var blocked = await scope.ServiceProvider.GetRequiredService<IProcurementOrderReversalService>().CancelConfirmationAsync(sourcePurchaseId, new() { CommandId = Id(7), ExpectedVersion = 3, Reason = "PG未反向入库阻断采购" });
                Assert.IsFalse(blocked.Success); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, blocked.Error?.Code);
                Assert.IsNull(scope.ServiceProvider.GetRequiredService<DbSession>().Transaction); Assert.IsFalse(await HasReceipt(Id(7))); Assert.AreEqual(before, await PlanDigest());
                var reversePreview = await Read(sp => sp.GetRequiredService<IInboundReversalService>().PreviewReverseAsync(inboundId, new() { ExpectedVersion = 2 }));
                Assert.IsTrue(reversePreview.CanReverse, string.Join(";", reversePreview.Blockers.Select(x => x.Code + ":" + x.Message)));
            }
            var reverse = new ReverseInboundCommand { CommandId = Id(6), ExpectedVersion = 2, Reason = "PG联合链入库完整反向" };
            var reversed = await Commit(sp => sp.GetRequiredService<IInboundReversalService>().ReverseAsync(inboundId, reverse), Id(6));
            Assert.AreEqual(3, reversed.Version); Assert.AreEqual(InboundPostingStatusEnum.Reversed, reversed.PostingStatus);
            Assert.AreEqual(0m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Product", new { Warehouse = warehouseId, Product = productId }));
            Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE source_document_id=@Id", new { Id = inboundId }));
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry o JOIN public.inventory_ledger_entry r ON r.reversal_of_ledger_id=o.uid AND r.quantity_delta=-o.quantity_delta WHERE o.source_document_id=@Id AND o.reversal_of_ledger_id IS NULL", new { Id = inboundId }));

            if (!await HasReceipt(Id(8))) await RequireReversalPreview(sourcePurchaseId, ProcurementReversalOperation.CancelConfirmation, 3, 0);
            var cancel = new CancelProcurementOrderConfirmationCommand { CommandId = Id(8), ExpectedVersion = 3, Reason = "PG下游完整反向后取消确认" };
            var cancelled = await Commit(sp => sp.GetRequiredService<IProcurementOrderReversalService>().CancelConfirmationAsync(sourcePurchaseId, cancel), Id(8));
            Assert.AreEqual(4, cancelled.Version); Assert.AreEqual(ConfirmationStatusEnum.待确认, cancelled.ConfirmationStatus);
            if (!await HasReceipt(Id(9)))
            {
                // 取消确认只改状态，不释放三项贡献；后续删行和作废各自明确释放，不能用取消确认替代。
                Assert.AreEqual(3m, await ActiveContribution());
                var preview = await Read(sp => sp.GetRequiredService<IProcurementOrderReversalService>().PreviewAsync(sourcePurchaseId, ProcurementReversalOperation.DeleteLines,
                    new() { ExpectedVersion = 4, Lines = [new ProcurementLineVersionDto { PurchaseOrderLineId = sourcePurchaseLineId, Version = 1 }] }));
                Assert.IsTrue(preview.CanExecute, string.Join(";", preview.Blockers)); Assert.AreEqual(1m, preview.Contributions.Sum(x => x.Quantity));
            }
            var delete = new DeleteProcurementDraftLinesCommand { CommandId = Id(9), ExpectedVersion = 4, Reason = "PG释放已完整反向收货的来源采购行",
                Lines = [new ProcurementLineVersionDto { PurchaseOrderLineId = sourcePurchaseLineId, Version = 1 }] };
            var deleted = await Commit(sp => sp.GetRequiredService<IProcurementOrderReversalService>().DeleteLinesAsync(sourcePurchaseId, delete), Id(9));
            Assert.AreEqual(5, deleted.Version);
            if (!await HasReceipt(Id(10))) await RequireReversalPreview(sourcePurchaseId, ProcurementReversalOperation.Void, 5, 1);
            var sourceVoid = new VoidProcurementOrderCommand { CommandId = Id(10), ExpectedVersion = 5, Reason = "PG来源剩余采购贡献释放" };
            Assert.AreEqual(6, (await Commit(sp => sp.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(sourcePurchaseId, sourceVoid), Id(10))).Version);
            if (!await HasReceipt(Id(11))) await RequireReversalPreview(independentId, ProcurementReversalOperation.Void, 3, 1);
            var independentVoid = new VoidProcurementOrderCommand { CommandId = Id(11), ExpectedVersion = 3, Reason = "PG补关联采购贡献释放" };
            Assert.AreEqual(4, (await Commit(sp => sp.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(independentId, independentVoid), Id(11))).Version);
            Assert.AreEqual(0m, await ActiveContribution());
            Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.demand_execution_link WHERE net_demand_id=@Id AND released_qty=allocated_qty AND link_status=8 AND is_deleted=FALSE", new { Id = netId }));
            var rollback = new RollbackMaterialDemandsCommand { CommandId = Id(12), Reason = "PG联合链释放后回退需求", Items = [new RollbackMaterialDemandItem { GrossDemandId = grossId, DecompositionRevision = 1 }] };
            Assert.AreEqual(1, (await Commit(sp => sp.GetRequiredService<IMaterialDemandReversalService>().RollbackAsync(rollback), Id(12))).RolledBackSourceCount);
            var cancelSales = new CancelSalesOrderConfirmationCommand { CommandId = Id(13), Uid = salesId, Version = 2, Reason = "PG联合链安全退出销售来源" };
            var exited = await Commit(sp => sp.GetRequiredService<ISalesOrderCancellationService>().CancelConfirmationAsync(salesId, cancelSales), Id(13));
            Assert.AreEqual(3, exited.Version); Assert.AreEqual(ConfirmationStatusEnum.待确认, exited.ConfirmationStatus);
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT NOT is_deleted AND lifecycle_status=8 AND version=2 FROM public.net_demand WHERE uid=@Id", new { Id = netId }));
            Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("SELECT NOT is_deleted AND lifecycle_status=9 FROM public.gross_demand WHERE uid=@Id", new { Id = grossId }));
            Assert.IsFalse(await HasReceipt(Id(7))); Assert.IsFalse(await HasReceipt(Id(14)));
        }
        finally
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, FirstRun = firstRun, InitialReceipts = initialReceipts, Completed = completed,
                SourcePurchaseId = sourcePurchaseId, SourcePurchaseLineId = sourcePurchaseLineId, IndependentId = independentId, IndependentLineId = independentLineId,
                InboundId = inboundId, AssociationItemId = Id(15), SalesId = salesId, GrossId = grossId, NetId = netId, ProductId = productId, WarehouseId = warehouseId, Backends = backends,
                Policy = "本批成功业务/回执/审计/流水/号段均留存；只修改上一G1本例来源/需求/采购/贡献，其他事实全摘要保护；首次与接续重放分开报告" }));
        }

        // 所有命令都属于唯一预登记批次；请求级作用域每阶段重建，避免把上一预览或反向的缓存状态带到新请求。
        Guid Id(int offset) => Guid.Parse("3cbe8ab3-be60-4af8-b36b-3e04e1206a" + (0xb0 + offset).ToString("x2"));
        Task<bool> HasReceipt(Guid id) => observer.Connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM public.command_receipt WHERE command_id=@Id)", new { Id = id });
        Task<decimal> ActiveContribution() => observer.Connection.ExecuteScalarAsync<decimal>("SELECT coalesce(sum(allocated_qty-released_qty),0) FROM public.demand_execution_link WHERE net_demand_id=@Id AND link_status=0 AND is_deleted=FALSE", new { Id = netId });
        // 拒绝分支只比较本批关联的来源、需求和采购全字段，避免把空查询结果当作相同业务快照。
        async Task<string> PlanDigest()
        {
            var digest = await observer.Connection.ExecuteScalarAsync<string>("""
            SELECT md5(coalesce(string_agg(data,chr(10) ORDER BY data),'')) FROM (
                SELECT to_jsonb(t)::text AS data FROM public.net_demand t WHERE uid=@Net
                UNION ALL SELECT to_jsonb(t)::text FROM public.gross_demand t WHERE uid=@Gross
                UNION ALL SELECT to_jsonb(t)::text FROM public.demand_execution_link t WHERE net_demand_id=@Net
                UNION ALL SELECT to_jsonb(t)::text FROM public.purchase_order_head t WHERE uid=ANY(@Purchases)
                UNION ALL SELECT to_jsonb(t)::text FROM public.purchase_order_line t WHERE purchase_order_id=ANY(@Purchases)) all_facts;
            """, new { Net = netId, Gross = grossId, Purchases = new[] { sourcePurchaseId, independentId } });
            Assert.IsNotNull(digest); return digest;
        }

        // 第一次预览冻结原始业务依据，重放读取同一非敏感指纹；不能从已经退出的当前单据重构旧指纹。
        async Task<string> FrozenToken(Guid id, Func<Task<string>> preview)
        {
            var path = Path.Combine(FindRoot(), "TestResults", "postgresql-migration", "标准联合链-依据-" + id.ToString("N") + ".txt");
            if (await HasReceipt(id)) return await File.ReadAllTextAsync(path);
            var token = await preview(); Assert.AreEqual(64, token.Length);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, token); return token;
        }
        // 每个只读预览使用新请求作用域，并证明预览没有留下事务。
        async Task<T> Read<T>(Func<IServiceProvider, Task<ServiceResult<T>>> action)
        {
            using var scope = app.Services.CreateScope(); var result = await action(scope.ServiceProvider);
            Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message); Assert.IsNull(scope.ServiceProvider.GetRequiredService<DbSession>().Transaction); return result.Data;
        }
        // 每个首次提交后保留第一连接，再打开另一请求连接重放；真实权限、公共回执与事务清理均沿现有实现。
        async Task<T> Commit<T>(Func<IServiceProvider, Task<ServiceResult<T>>> action, Guid id)
        {
            using var first = app.Services.CreateScope(); using var second = app.Services.CreateScope();
            var firstSession = first.ServiceProvider.GetRequiredService<DbSession>(); var secondSession = second.ServiceProvider.GetRequiredService<DbSession>();
            var pids = new[] { ((NpgsqlConnection)firstSession.Connection).ProcessID, ((NpgsqlConnection)secondSession.Connection).ProcessID }; Assert.AreNotEqual(pids[0], pids[1]); backends.Add(pids);
            var result = await action(first.ServiceProvider); Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
            var replay = await action(second.ServiceProvider); Assert.IsTrue(replay.Success, replay.Error?.Code + ": " + replay.Message);
            Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(result.Data), System.Text.Json.JsonSerializer.Serialize(replay.Data));
            Assert.IsNull(firstSession.Transaction); Assert.IsNull(secondSession.Transaction);
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = id })); completed.Add(id); return result.Data;
        }
        // 反向首次执行必须经原预览证明下游资格和拟释放量，不能用余额为零代替完整反向事实。
        async Task RequireReversalPreview(Guid id, ProcurementReversalOperation operation, int version, decimal contribution)
        {
            var preview = await Read(sp => sp.GetRequiredService<IProcurementOrderReversalService>().PreviewAsync(id, operation, new() { ExpectedVersion = version }));
            Assert.IsTrue(preview.CanExecute, string.Join(";", preview.Blockers)); Assert.AreEqual(contribution, preview.Contributions.Sum(x => x.Quantity));
        }
    }

    /// <summary>从真实销售保存/确认建立标准来源及需求，再通过真实预览新建和追加采购；仅登记批次首次执行，成功事实留存供后续联合验收。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeStandardSourceCreatesAndAppendsPurchase()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        const string batch = "25df913f-e4f9-4082-99ad-782a3d3eed44";
        Assert.AreEqual(batch, Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_STANDARD_SOURCE_BATCH"), "必须明确登记本批首次写入，不隐式重跑成功事实。");
        var saveId = Guid.Parse(batch);
        var lineId = Guid.Parse("25df913f-e4f9-4082-99ad-782a3d3eed45");
        var confirmId = Guid.Parse("25df913f-e4f9-4082-99ad-782a3d3eed46");
        var establishId = Guid.Parse("25df913f-e4f9-4082-99ad-782a3d3eed47");
        var createId = Guid.Parse("25df913f-e4f9-4082-99ad-782a3d3eed48");
        var createItemId = Guid.Parse("25df913f-e4f9-4082-99ad-782a3d3eed49");
        var appendId = Guid.Parse("25df913f-e4f9-4082-99ad-782a3d3eed4a");
        var appendItemId = Guid.Parse("25df913f-e4f9-4082-99ad-782a3d3eed4b");
        var customerId = Guid.Parse("cc533ab1-88ff-4fa4-ab28-53199670768f");
        var supplierId = Guid.Parse("017b0b30-2744-4e4b-8bfb-1169e5be98eb");
        var productId = Guid.Parse("00008dec-d28a-4036-bc06-da0a2159e16b");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var commandIds = new[] { saveId, confirmId, establishId, createId, appendId };
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = commandIds }), "已有事实必须按原输入另做接续，不将重放算本批首次。");
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.demand_execution_link WHERE command_item_id=ANY(@Ids)", new { Ids = new[] { createItemId, appendItemId } }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE doc_type='SO' AND year_month='640010'"), "仅使用本批尚不存在的销售号段，不重置旧号码。");
        var actor = await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u
            WHERE u.is_active=TRUE AND u.is_deleted=FALSE AND EXISTS(
                SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
                WHERE ur.user_id=u.uid AND ur.is_deleted=FALSE AND r.is_active=TRUE AND r.is_deleted=FALSE AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid;
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices();
        builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();
        using var first = app.Services.CreateScope();
        using var second = app.Services.CreateScope();
        var services = first.ServiceProvider;
        var replayServices = second.ServiceProvider;
        var session = services.GetRequiredService<DbSession>();
        var replaySession = replayServices.GetRequiredService<DbSession>();
        Assert.AreNotEqual(((NpgsqlConnection)session.Connection).ProcessID, ((NpgsqlConnection)replaySession.Connection).ProcessID);
        var product = await services.GetRequiredService<IRepository<Product>>().GetByIdAsync(productId);
        Assert.IsNotNull(product); Assert.IsTrue(product.IsActive); Assert.AreEqual(0, product.BomVersion); Assert.AreEqual("采购", product.SupplyType);
        Assert.AreEqual("Pcs", product.Unit);
        Guid salesId = Guid.Empty, grossId = Guid.Empty, netId = Guid.Empty, purchaseId = Guid.Empty;
        try
        {
            // 通过原销售命令产生单位和客户来源快照，不直接写GrossDemand/NetDemand/Link或已确认采购。
            var save = new SaveSalesOrderRequest
            {
                CommandId = saveId,
                Header = new SalesOrderEditHeaderDto { CustomerId = customerId, OrderDate = new DateTime(6400, 10, 5), Currency = "CNY", ExchangeRate = 1, CustomerPoNo = "PG-G1-25df913f", Remark = "PG迁移联合验收25df913f" },
                Lines = [new SalesOrderEditLineDto { Uid = lineId, ProductId = productId, Sku = product.Sku, Qty = 3, CnyPrice = 10, UsdPrice = 0 }]
            };
            var saved = await services.GetRequiredService<ISalesOrderDraftService>().SaveAsync(save);
            Assert.IsTrue(saved.Success, saved.Error?.Code + ": " + saved.Message);
            salesId = saved.Data.Document.Header.Uid;
            Assert.AreEqual(lineId, saved.Data.Document.Lines.Single().Uid);
            Assert.AreEqual("Pcs", saved.Data.Document.Lines.Single().UnitSnapshot);
            var savedReplay = await replayServices.GetRequiredService<ISalesOrderDraftService>().SaveAsync(save);
            Assert.IsTrue(savedReplay.Success, savedReplay.Error?.Code + ": " + savedReplay.Message);
            Assert.AreEqual(salesId, savedReplay.Data.Document.Header.Uid);
            var confirm = new ConfirmSalesOrderCommand { CommandId = confirmId, Uid = salesId, Version = saved.Data.Document.Header.Version };
            var confirmed = await services.GetRequiredService<ISalesOrderConfirmationService>().ConfirmAsync(salesId, confirm);
            Assert.IsTrue(confirmed.Success, confirmed.Error?.Code + ": " + confirmed.Message);
            Assert.AreEqual(1, confirmed.Data.GrossDemandCount);
            var confirmedReplay = await replayServices.GetRequiredService<ISalesOrderConfirmationService>().ConfirmAsync(salesId, confirm);
            Assert.IsTrue(confirmedReplay.Success, confirmedReplay.Error?.Code + ": " + confirmedReplay.Message);
            Assert.AreEqual(confirmed.Data.Version, confirmedReplay.Data.Version);
            grossId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.gross_demand WHERE source_type='SO' AND source_uid=@Id AND source_line_id=@Line", new { Id = salesId, Line = lineId });
            var gross = await services.GetRequiredService<IRepository<GrossDemand>>().GetByIdAsync(grossId);
            Assert.IsNotNull(gross); Assert.AreEqual("Pcs", gross.UnitSnapshot); Assert.AreEqual(customerId, gross.CustomerId); Assert.AreEqual(3m, gross.DemandQty);
            Assert.AreEqual(confirmed.Data.Version, gross.SourceVersion);
            var establish = new EstablishMaterialDemandsCommand { CommandId = establishId, Items = [new EstablishMaterialDemandItem { GrossDemandId = grossId, SourceDemandVersion = gross.Version, SourceVersion = gross.SourceVersion }] };
            var established = await services.GetRequiredService<IMaterialDemandEstablishmentService>().EstablishAsync(establish);
            Assert.IsTrue(established.Success, established.Error?.Code + ": " + established.Message);
            Assert.AreEqual(1, established.Data.CreatedSourceCount); Assert.AreEqual(1, established.Data.CreatedMaterialDemandCount);
            var establishedReplay = await replayServices.GetRequiredService<IMaterialDemandEstablishmentService>().EstablishAsync(establish);
            Assert.IsTrue(establishedReplay.Success, establishedReplay.Error?.Code + ": " + establishedReplay.Message);
            Assert.AreEqual(1, establishedReplay.Data.CreatedMaterialDemandCount);
            netId = await observer.Connection.QuerySingleAsync<Guid>("SELECT uid FROM public.net_demand WHERE gross_demand_id=@Id AND is_deleted=FALSE", new { Id = grossId });
            var net = await services.GetRequiredService<IRepository<NetDemand>>().GetByIdAsync(netId);
            Assert.IsNotNull(net); Assert.AreEqual(1, net.DecompositionRevision); Assert.AreEqual(3m, net.NetQty); Assert.AreEqual("Pcs", net.UnitSnapshot);
            Assert.AreEqual(gross.DemandBasisFingerprint, net.SourceDemandBasisFingerprintSnapshot);
            var created = await TransferAsync(createId, createItemId, 3, null);
            purchaseId = created.PurchaseOrderId;
            Assert.AreEqual(1, created.Version); Assert.IsFalse(created.AppendedToExisting);
            var appended = await TransferAsync(appendId, appendItemId, 2, created);
            Assert.AreEqual(purchaseId, appended.PurchaseOrderId); Assert.IsTrue(appended.AppendedToExisting); Assert.AreEqual(2, appended.Version);
            var head = await services.GetRequiredService<IRepository<PurchaseOrderHead>>().GetByIdAsync(purchaseId);
            Assert.IsNotNull(head); Assert.AreEqual(2, head.Version); Assert.AreEqual(1, head.OriginKind); Assert.AreEqual(salesId, head.DemandSourceUid); Assert.AreEqual("SO", head.DemandSourceType);
            Assert.AreEqual(customerId, head.CustomerId); Assert.AreEqual("CNY", head.Currency); Assert.AreEqual(ConfirmationStatusEnum.待确认, head.ConfirmationStatus);
            Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_line WHERE purchase_order_id=@Id AND is_deleted=FALSE AND quantity=1 AND unlinked_qty=0", new { Id = purchaseId }));
            Assert.AreEqual(2m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT sum(allocated_qty-released_qty) FROM public.demand_execution_link WHERE net_demand_id=@Id AND is_deleted=FALSE", new { Id = netId }));
            foreach (var id in new[] { saveId, confirmId, establishId })
                Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = id }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT current_no FROM public.document_sequence WHERE doc_type='SO' AND year_month='640010'"));
            Assert.IsNull(session.Transaction); Assert.IsNull(replaySession.Transaction);

            // 新建和追加分别持有独立预览/命令项；逐来源事务与原Link重放机制保持，不能假设有公共命令回执。
            async Task<CreatedPurchaseDraftDto> TransferAsync(Guid requestId, Guid itemId, decimal remaining, CreatedPurchaseDraftDto? append)
            {
                var previewInput = new PurchaseDraftPreviewRequest { ExecutionRequestId = requestId, EntryMode = PurchaseDraftEntryModeEnum.ManualSelection, LineMode = PurchaseDraftLineModeEnum.KeepSourceLines,
                    Items = [new PurchaseDraftPreviewItem { CommandItemId = itemId, NetDemandId = netId, ExpectedVersion = net.Version, ExpectedRemainingQty = remaining, PurchaseQty = 1, SupplierId = supplierId, UnitPrice = 10, TaxPrice = 10 }] };
                if (append is not null) previewInput.Targets.Add(new PurchaseDraftTargetSelectionDto { SourceType = "SO", SourceUid = salesId, SupplierId = supplierId, TargetMode = PurchaseDraftTargetModeEnum.AppendExisting,
                    ExistingPurchaseOrderId = append.PurchaseOrderId, ExpectedPurchaseOrderVersion = append.Version });
                var preview = await services.GetRequiredService<IPurchaseDraftPreviewService>().PreviewAsync(previewInput);
                Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message);
                Assert.IsFalse(preview.Data.HasBlockers, string.Join(";", preview.Data.Warnings.Select(x => x.Code + ":" + x.Message)));
                var group = preview.Data.SourceGroups.Single();
                var input = new CreatePurchaseDraftsCommand { ExecutionRequestId = requestId, EntryMode = previewInput.EntryMode, PreviewToken = preview.Data.PreviewToken,
                    Sources = [new CreatePurchaseDraftSourceCommand { SourceType = "SO", SourceUid = salesId, SourcePreviewToken = group.SourcePreviewToken, LineMode = previewInput.LineMode,
                        Items = [new CreatePurchaseDraftItemCommand { CommandItemId = itemId, NetDemandId = netId, ExpectedVersion = net.Version, ExpectedRemainingQty = remaining, PurchaseQty = 1, SupplierId = supplierId, UnitPrice = 10, TaxPrice = 10 }],
                        ConfirmedWarningCodes = group.Warnings.Where(x => x.RequiresConfirmation).Select(x => x.Code).Distinct().ToList() }] };
                if (append is not null) input.Sources.Single().Targets.Add(new CreatePurchaseDraftTargetCommand { SupplierId = supplierId, TargetMode = PurchaseDraftTargetModeEnum.AppendExisting,
                    ExistingPurchaseOrderId = append.PurchaseOrderId, ExpectedPurchaseOrderVersion = append.Version });
                var result = await services.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(input);
                Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
                var sourceResult = result.Data.Sources.Single();
                Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded, sourceResult.Outcome, sourceResult.ErrorCode + ": " + sourceResult.Message);
                var replay = await replayServices.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(input);
                Assert.IsTrue(replay.Success, replay.Error?.Code + ": " + replay.Message);
                Assert.AreEqual(NetDemandCommandOutcomeEnum.AlreadyApplied, replay.Data.Sources.Single().Outcome);
                Assert.AreEqual(sourceResult.PurchaseDrafts.Single().PurchaseOrderId, replay.Data.Sources.Single().PurchaseDrafts.Single().PurchaseOrderId);
                Assert.IsNull(session.Transaction); Assert.IsNull(replaySession.Transaction);
                return sourceResult.PurchaseDrafts.Single();
            }
        }
        finally
        {
            // 已成功提交的来源、计划和追溯不能删改；即使后段断言失败也打印身份，供主控保护原数据和明确接续。
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Batch = batch, SaveId = saveId, LineId = lineId, ConfirmId = confirmId, EstablishId = establishId,
                CreateId = createId, CreateItemId = createItemId, AppendId = appendId, AppendItemId = appendItemId, SalesId = salesId, GrossId = grossId, NetId = netId, PurchaseId = purchaseId,
                CustomerId = customerId, SupplierId = supplierId, ProductId = productId, FirstBackend = ((NpgsqlConnection)session.Connection).ProcessID, ReplayBackend = ((NpgsqlConnection)replaySession.Connection).ProcessID,
                Policy = "只在failure首次执行，成功事实和真实号段留存；不插来源/需求/Link绕过服务，不重置或改备份" }));
        }
    }

    /// <summary>使用当前开发配置在dv_next验证默认PG接线与两个只读HTTP入口；追加会话只读保护，不建账号或调用写接口。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeCurrentConfigurationReadOnlyHttp()
    {
        _ = PostgreSqlIntegrationEnvironment.GetConnectionString();
        const string batch = "682ccdc0-1804-4c79-93da-9125c3703500";
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_CURRENT_READ_ONLY_BATCH") != batch)
            Assert.Inconclusive("仅运行主控登记的当前开发配置只读宿主，不启动写入验收。");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development", ApplicationName = typeof(WebApiConfiguration).Assembly.GetName().Name });
        var settings = new Npgsql.NpgsqlConnectionStringBuilder(builder.Configuration.GetConnectionString("Default"));
        Assert.AreEqual("192.168.3.12", settings.Host); Assert.AreEqual(5432, settings.Port); Assert.AreEqual("dv_next", settings.Database);
        Assert.IsTrue(builder.Configuration.GetValue<bool>("Auth:UseDevUser"), "本场景只验证用户现有开发身份入口，不改认证配置。");
        var configuredName = builder.Configuration["Auth:DevUserName"] ?? "dev";
        // 保留当前实例、凭据、配置与连接选项，仅对本地验收会话追加数据库默认只读保护。
        // 启动路径已核实不播种/同步结构；两HTTP端点不取业务锁，不调用写服务。
        settings.Options = ((settings.Options ?? "") + " -c default_transaction_read_only=on").Trim();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Default"] = settings.ConnectionString });
        builder.AddWebApiServices(); await using var app = builder.Build();
        using (var scope = app.Services.CreateScope())
        {
            var session = scope.ServiceProvider.GetRequiredService<IDbSession>();
            Assert.IsInstanceOfType<Npgsql.NpgsqlConnection>(session.Connection);
            Assert.AreEqual("dv_next", await session.Connection.ExecuteScalarAsync<string>("SELECT current_database()"));
            Assert.AreEqual("on", await session.Connection.ExecuteScalarAsync<string>("SHOW default_transaction_read_only"));
        }
        app.UseWebApiPipeline(); app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) };
            using var response = await client.GetAsync("/api/auth/me");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var me = await response.Content.ReadFromJsonAsync<MeDto>(); Assert.IsNotNull(me);
            Assert.IsTrue(Guid.TryParse(me.UserId, out var userId) && userId != Guid.Empty);
            Assert.AreEqual(configuredName, me.UserName);
            using var productResponse = await client.PostAsJsonAsync("/api/product/page", new { PageIndex = 1, PageSize = 2, Keyword = "" });
            Assert.AreEqual(HttpStatusCode.OK, productResponse.StatusCode);
            var products = await productResponse.Content.ReadFromJsonAsync<ServiceResult<PagedResponseBase<SwiftExport.Contracts.DTOs.Products.ProductDto>>>();
            Assert.IsNotNull(products); Assert.IsTrue(products.Success); Assert.IsNotNull(products.Data);
            Assert.AreEqual(2, products.Data.Items.Count); Assert.AreEqual(1, products.Data.PageIndex);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            { batch, Database = settings.Database, DefaultSessionReadOnly = true, HttpRequests = 2,
                userId, Permissions = me.Permissions.Count, ProductTotal = products.Data.TotalCount,
                Scope = "当前开发身份免登录及产品分页；不证明dv_next正常密码登录或业务写入", DatabaseWrites = 0 }));
        }
        finally { await app.StopAsync(); }
    }

    /// <summary>只读验证补迁后的六表真实HTTP服务与资料通用仓储；复用已有隔离账号，不创建端点或改数据。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeMigratedBaseDataReads()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        const string batch = "e478aa2b-3326-478f-981f-2c590a693801";
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_BASEDATA_READ_BATCH") != batch)
            Assert.Inconclusive("仅运行主控登记的七表补迁只读验收，不生成替代数据。");
        var userId = Guid.Parse("516cf0ea-1d68-4b14-b047-c1df88852008");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.sys_user WHERE uid=@userId AND is_active AND NOT is_deleted", new { userId }));
        var customerProduct = await observer.Connection.QueryFirstAsync<SwiftExport.Core.Entities.Relations.CustomerProduct>(
            """SELECT uid AS "Uid",version AS "Version",customer_id AS "CustomerId",sku_id AS "SkuId" FROM public.customer_product WHERE NOT is_deleted ORDER BY uid""");
        var poConfig = await observer.Connection.QueryFirstAsync<SwiftExport.Core.Entities.Relations.CustomerPoConfig>(
            """SELECT uid AS "Uid",version AS "Version",customer_id AS "CustomerId",supplier_id AS "SupplierId",default_discount_rate AS "DefaultDiscountRate" FROM public.customer_po_config WHERE NOT is_deleted ORDER BY uid""");
        var bomNo = await observer.Connection.QueryFirstAsync<string>("SELECT bom_no FROM public.bom WHERE NOT is_deleted ORDER BY uid");
        var bomUids = (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.bom WHERE NOT is_deleted AND bom_no=@bomNo", new { bomNo })).ToArray();
        var neutralUids = (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.product_neutral WHERE NOT is_deleted")).ToArray();
        Assert.AreEqual(7, neutralUids.Length);
        var profileUids = (await observer.Connection.QueryAsync<Guid>("SELECT uid FROM public.excel_read_profile WHERE NOT is_deleted ORDER BY uid")).ToArray();
        Assert.AreEqual(2, profileUids.Length);
        var detail = await observer.Connection.QueryFirstAsync<SwiftExport.Core.Entities.MasterData.ProductDetail>(
            """SELECT uid AS "Uid",version AS "Version",product_id AS "ProductId",label_path AS "LabelPath",im_path AS "IMPath",drawing_path AS "DrawingPath" FROM public.product_detail WHERE NOT is_deleted ORDER BY uid""");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development", ApplicationName = typeof(WebApiConfiguration).Assembly.GetName().Name });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Default"] = connection, ["Auth:UseDevUser"] = "false" });
        builder.AddWebApiServices(); await using var app = builder.Build();
        // 当前资料表没有业务服务/HTTP消费者，只核现有通用PG注册；不为迁移制造新业务入口。
        using (var scope = app.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<SwiftExport.Core.Interfaces.IRepository<SwiftExport.Core.Entities.MasterData.ProductDetail>>();
            var actual = await repository.GetByIdAsync(detail.Uid);
            Assert.IsNotNull(actual); Assert.AreEqual(detail.Uid, actual.Uid); Assert.AreEqual(detail.Version, actual.Version);
            Assert.AreEqual(detail.ProductId, actual.ProductId); Assert.AreEqual(detail.LabelPath, actual.LabelPath);
            Assert.AreEqual(detail.IMPath, actual.IMPath); Assert.AreEqual(detail.DrawingPath, actual.DrawingPath);
            Assert.IsInstanceOfType<Npgsql.NpgsqlConnection>(scope.ServiceProvider.GetRequiredService<IDbSession>().Connection);
        }
        app.UseWebApiPipeline(); app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) };
            using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
            { UserName = "PGHTTP-P08-" + userId.ToString("N"), Password = "HttpP08!" + userId.ToString("N") });
            Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
            var token = await login.Content.ReadFromJsonAsync<LoginTokenDto>(); Assert.IsNotNull(token); Assert.IsFalse(string.IsNullOrWhiteSpace(token.Access_token));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Access_token);

            using var customerResponse = await client.GetAsync("/api/customerproduct/" + customerProduct.Uid);
            Assert.AreEqual(HttpStatusCode.OK, customerResponse.StatusCode);
            var customer = await customerResponse.Content.ReadFromJsonAsync<ServiceResult<SwiftExport.Contracts.DTOs.Customers.CustomerProductDto>>();
            Assert.IsNotNull(customer); Assert.IsTrue(customer.Success); Assert.IsNotNull(customer.Data);
            Assert.AreEqual(customerProduct.Uid, customer.Data.Uid); Assert.AreEqual(customerProduct.Version, customer.Data.Version);
            Assert.AreEqual(customerProduct.CustomerId, customer.Data.CustomerId); Assert.AreEqual(customerProduct.SkuId, customer.Data.SkuId);
            Assert.IsNull(customer.Data.USDPrice); Assert.IsNull(customer.Data.CNYPrice);
            using var configResponse = await client.GetAsync("/api/customerpoconfig/" + poConfig.Uid);
            Assert.AreEqual(HttpStatusCode.OK, configResponse.StatusCode);
            var config = await configResponse.Content.ReadFromJsonAsync<ServiceResult<SwiftExport.Contracts.DTOs.Customers.CustomerPoConfigDto>>();
            Assert.IsNotNull(config); Assert.IsTrue(config.Success); Assert.IsNotNull(config.Data);
            Assert.AreEqual(poConfig.Uid, config.Data.Uid); Assert.AreEqual(poConfig.Version, config.Data.Version);
            Assert.AreEqual(poConfig.CustomerId, config.Data.CustomerId); Assert.AreEqual(poConfig.SupplierId, config.Data.SupplierId);
            Assert.AreEqual(poConfig.DefaultDiscountRate, config.Data.DefaultDiscountRate);
            using var bomResponse = await client.GetAsync("/api/bom/by-no/" + Uri.EscapeDataString(bomNo));
            Assert.AreEqual(HttpStatusCode.OK, bomResponse.StatusCode);
            var bom = await bomResponse.Content.ReadFromJsonAsync<ServiceResult<List<SwiftExport.Contracts.DTOs.Products.BomDto>>>();
            Assert.IsNotNull(bom); Assert.IsTrue(bom.Success); Assert.IsNotNull(bom.Data);
            CollectionAssert.AreEquivalent(bomUids, bom.Data.Select(x => x.Uid).ToArray());
            using var neutralResponse = await client.PostAsJsonAsync("/api/productneutralmanager/page", new { PageIndex = 1, PageSize = 20, Keyword = "" });
            Assert.AreEqual(HttpStatusCode.OK, neutralResponse.StatusCode);
            var neutrals = await neutralResponse.Content.ReadFromJsonAsync<ServiceResult<PagedResponseBase<SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.ProductNeutralManagerListItemDto>>>();
            Assert.IsNotNull(neutrals); Assert.IsTrue(neutrals.Success); Assert.IsNotNull(neutrals.Data);
            Assert.AreEqual(7, neutrals.Data.TotalCount); CollectionAssert.AreEquivalent(neutralUids, neutrals.Data.Items.Select(x => x.Uid).ToArray());
            var fieldsRead = 0;
            foreach (var uid in profileUids)
            {
                var expectedFields = (await observer.Connection.QueryAsync<SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadFieldDto>(
                    """SELECT target_field_key AS "TargetFieldKey",worksheet_name AS "WorksheetName",cell_address AS "CellAddress",is_downward AS "IsDownward" FROM public.excel_read_field WHERE profile_uid=@uid AND NOT is_deleted""", new { uid })).ToArray();
                using var response = await client.GetAsync("/api/excel-read-profiles/" + uid);
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                var profile = await response.Content.ReadFromJsonAsync<ServiceResult<SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileDto>>();
                Assert.IsNotNull(profile); Assert.IsTrue(profile.Success); Assert.IsNotNull(profile.Data); Assert.AreEqual(uid, profile.Data.Uid);
                // API字段以业务键及四个配置值表达，不暴露存储UID；比对完整原契约，不为测试增加字段。
                CollectionAssert.AreEquivalent(expectedFields.Select(Signature).ToArray(), profile.Data.Fields.Select(Signature).ToArray());
                if (expectedFields.Length == 0)
                {
                    Assert.IsFalse(profile.Data.CanRead);
                    Assert.IsTrue(profile.Data.Issues.Any(x => x.Code == "EXCEL_READ_FIELDS_REQUIRED"));
                }
                fieldsRead += profile.Data.Fields.Count;
            }
            Assert.AreEqual(10, fieldsRead);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            { batch, HttpRequests = 7, CustomerProductId = customerProduct.Uid, CustomerPoConfigId = poConfig.Uid,
                BomRows = bom.Data.Count, NeutralRows = neutrals.Data.TotalCount, Profiles = profileUids.Length, Fields = fieldsRead,
                ProductDetailId = detail.Uid, ProductDetailScope = "现有PG通用仓储，无业务HTTP入口", DatabaseWrites = 0 }));
        }
        finally { await app.StopAsync(); }

        // 失败只报告摘要，避免把工作表名称或配置原值写进测试输出。
        static string Signature(SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadFieldDto field)
            => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(field))));
    }

    /// <summary>独立测试库使用专用账号正常登录真实HTTP宿主，核验凭据版本、权限投影和只读分页；不伪造令牌或修改已有账号。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeAuthenticatedHttpQueries()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var userId = Guid.Parse("516cf0ea-1d68-4b14-b047-c1df88852008");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_HTTP_BATCH") != userId.ToString())
            Assert.Inconclusive("只运行主控登记的正常HTTP登录批次。");
        var relationId = Guid.Parse("516cf0ea-1d68-4b14-b047-c1df88852009");
        var userName = "PGHTTP-P08-" + userId.ToString("N"); var password = "HttpP08!" + userId.ToString("N");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development", ApplicationName = typeof(WebApiConfiguration).Assembly.GetName().Name });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Default"] = connection, ["Auth:UseDevUser"] = "false" });
        builder.AddWebApiServices(); await using var app = builder.Build();
        using var fixtureScope = app.Services.CreateScope();
        var hasher = fixtureScope.ServiceProvider.GetRequiredService<SwiftExport.Application.Interfaces.Auth.Passwords.IPasswordHasher>();
        var prior = await observer.Connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM public.sys_user WHERE uid=@UserId OR user_name=@UserName", new { userId, userName });
        if (prior == 0)
        {
            var roleId = await observer.Connection.QuerySingleAsync<Guid>(
                "SELECT uid FROM public.sys_role WHERE role_code='SYSTEM_ADMIN' AND is_active AND NOT is_deleted");
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM public.sys_user_role WHERE uid=@Id", new { Id = relationId }));
            // 仅建立本例账号与已有角色的测试关系，不执行生产注册用例、不扩充角色/权限或更新既有账号。
            using var fixtureTransaction = observer.Connection.BeginTransaction();
            await observer.Connection.ExecuteAsync("""
                INSERT INTO public.sys_user(uid,version,is_deleted,created_date,created_by,user_name,nick_name,password_hash,credential_version,is_active)
                VALUES(@UserId,1,FALSE,CURRENT_TIMESTAMP,@Actor,@UserName,'PG正常HTTP测试',@Hash,1,TRUE);
                INSERT INTO public.sys_user_role(uid,version,is_deleted,created_date,created_by,user_id,role_id)
                VALUES(@RelationId,1,FALSE,CURRENT_TIMESTAMP,@Actor,@UserId,@RoleId);
                """, new { userId, userName, relationId, roleId, Actor = Guid.Parse("1b1ecf70-0913-4dd9-8c40-442cf4055c7b"), Hash = hasher.Hash(password) }, fixtureTransaction);
            fixtureTransaction.Commit();
        }
        else
        {
            Assert.AreEqual(1, prior, "不覆盖部分或同名其他输入。");
            var retained = await observer.Connection.QuerySingleAsync<SwiftExport.Core.Entities.Auth.SysUser>(
                "SELECT uid AS \"Uid\",password_hash AS \"PasswordHash\",credential_version AS \"CredentialVersion\" FROM public.sys_user WHERE uid=@UserId AND user_name=@UserName AND is_active AND NOT is_deleted", new { userId, userName });
            Assert.IsTrue(hasher.Verify(password, retained.PasswordHash), "原输入必须完整匹配，不重置密码。");
            Assert.AreEqual(1, retained.CredentialVersion);
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM public.sys_user_role WHERE uid=@RelationId AND user_id=@UserId AND NOT is_deleted", new { relationId, userId }));
        }
        var actions = app.Services.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
        var discoveredLogin = actions.OfType<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()
            .Any(x => x.ControllerTypeInfo.AsType() == typeof(SwiftExport.WebApi.Controllers.Auth.AuthController) && x.MethodInfo.Name == "Login");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { ApplicationName = app.Environment.ApplicationName, ActionCount = actions.Count, discoveredLogin }));
        Assert.IsTrue(discoveredLogin, "真实HTTP验收必须发现原WebApi登录端点，不能把空Controller宿主的401当认证结果。");
        app.UseWebApiPipeline(); app.Urls.Add("http://127.0.0.1:0");
        var observations = new List<object>();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(15) };
            using var anonymous = await client.GetAsync("/api/auth/me");
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode); observations.Add(new { Route = "GET /api/auth/me anonymous", Status = 401 });
            using var wrong = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto { UserName = userName, Password = "wrong-password" });
            Assert.AreEqual(HttpStatusCode.Unauthorized, wrong.StatusCode); observations.Add(new { Route = "POST /api/auth/login wrong password", Status = 401 });
            using var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto { UserName = userName, Password = password });
            Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
            var token = await login.Content.ReadFromJsonAsync<LoginTokenDto>(); Assert.IsNotNull(token); Assert.IsFalse(string.IsNullOrWhiteSpace(token.Access_token));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Access_token);
            observations.Add(new { Route = "POST /api/auth/login", Status = 200 });
            using var meResponse = await client.GetAsync("/api/auth/me"); Assert.AreEqual(HttpStatusCode.OK, meResponse.StatusCode);
            var me = await meResponse.Content.ReadFromJsonAsync<MeDto>(); Assert.IsNotNull(me); Assert.AreEqual(userId.ToString(), me.UserId); Assert.IsTrue(me.IsAdmin);
            CollectionAssert.Contains(me.RoleCodes, "SYSTEM_ADMIN"); Assert.IsTrue(me.Permissions.Count > 0);
            observations.Add(new { Route = "GET /api/auth/me", Status = 200, PermissionCount = me.Permissions.Count });
            foreach (var route in new[] { "/api/sysdiccurrency/enabled", "/api/productcategory/all" })
            {
                using var response = await client.GetAsync(route); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, route);
                var result = await response.Content.ReadFromJsonAsync<ServiceResult<System.Text.Json.JsonElement>>(); Assert.IsNotNull(result); Assert.IsTrue(result.Success, route);
                Assert.AreEqual(System.Text.Json.JsonValueKind.Array, result.Data.ValueKind); observations.Add(new { Route = "GET " + route, Status = 200, Items = result.Data.GetArrayLength() });
            }
            using var products = await client.PostAsJsonAsync("/api/product/page", new { PageIndex = 1, PageSize = 2, Keyword = "" });
            Assert.AreEqual(HttpStatusCode.OK, products.StatusCode);
            var page = await products.Content.ReadFromJsonAsync<ServiceResult<PagedResponseBase<SwiftExport.Contracts.DTOs.Products.ProductDto>>>();
            Assert.IsNotNull(page); Assert.IsTrue(page.Success); Assert.IsNotNull(page.Data); Assert.AreEqual(1, page.Data.PageIndex); Assert.AreEqual(2, page.Data.PageSize);
            Assert.AreEqual(Math.Min(2, page.Data.TotalCount), page.Data.Items.Count);
            observations.Add(new { Route = "POST /api/product/page", Status = 200, Items = page.Data.Items.Count, page.Data.TotalCount });
            using var warehouses = await client.GetAsync("/api/inventory/warehouse-sites/picker?PageIndex=1&PageSize=2");
            Assert.AreEqual(HttpStatusCode.OK, warehouses.StatusCode);
            var warehousePage = await warehouses.Content.ReadFromJsonAsync<ServiceResult<System.Text.Json.JsonElement>>(); Assert.IsNotNull(warehousePage); Assert.IsTrue(warehousePage.Success);
            observations.Add(new { Route = "GET /api/inventory/warehouse-sites/picker", Status = 200 });
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { userId, relationId, observations, Policy = "正常登录/JWT/凭据版本/权限及只读查询，密码与Token不输出，两个专用输入保留，不更新既有数据" }));
        }
        finally { await app.StopAsync(); }
    }

    /// <summary>复用已登记独立采购，通过两个真实请求依次保存、确认、取消和作废；成功回执只追加保留，重复运行只重放原输入。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeIndependentPurchaseLifecycleAcrossConnections()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var registeredCommand = Guid.Parse("402cff7a-925e-4d53-a2a9-e5ad4300c500");
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_PURCHASE_LIFECYCLE_COMMAND"), out var saveId) || saveId != registeredCommand)
            Assert.Inconclusive("只允许主控已登记采购和命令，不创建替代夹具或改任意业务单据。");
        var headId = Guid.Parse("dc79fc76-ca77-4aff-9ce8-4a350164ceca");
        var lineId = Guid.Parse("ea643088-1a03-4397-aee0-4a350164cecd");
        var confirmId = CommandId(1); var cancelId = CommandId(2); var voidId = CommandId(3);
        var successIds = new[] { saveId, confirmId, cancelId, voidId };
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("SELECT uid AS \"UserId\",user_name AS \"UserName\" FROM public.sys_user WHERE uid='1b1ecf70-0913-4dd9-8c40-442cf4055c7b' AND is_active AND NOT is_deleted");
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_head WHERE uid=@Id AND execution_request_id='263221ad-07f6-4259-a191-c7158fb6909a' AND origin_kind=2", new { Id = headId }));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build();

        // 原业务要求确认前明确含税单价；先证明空值被拒绝，再由真实商务保存输入零价，不修改默认业务语义。
        if (!await HasReceipt(saveId))
        {
            using var scope = app.Services.CreateScope();
            var confirmation = scope.ServiceProvider.GetRequiredService<IProcurementOrderConfirmationService>();
            var basis = await confirmation.GetConfirmationContextAsync(headId);
            Assert.IsTrue(basis.Success, basis.Error?.Code + ": " + basis.Message);
            var refusal = await confirmation.ConfirmAsync(headId, new() { CommandId = CommandId(4), ExpectedVersion = 1, ContextFingerprint = basis.Data.Fingerprint });
            Assert.IsFalse(refusal.Success); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, refusal.Error?.Code);
            Assert.IsNull(scope.ServiceProvider.GetRequiredService<DbSession>().Transaction);
        }
        var save = new UpdatePurchaseOrderDraftCommand { CommandId = saveId, ExpectedVersion = 1,
            Lines = [new() { PurchaseOrderLineId = lineId, ExpectedVersion = 1, DescriptionZh = "真实PG非标采购", UnitPrice = 0, TaxPrice = 0 }] };
        await RunPair(sp => sp.GetRequiredService<IProcurementOrderDraftService>().SaveAsync(headId, save), saveId, 2);

        // 保存实际确认依据，以便阶段失败后用相同命令和输入重放；文件只有非敏感指纹，不保存连接配置。
        var basisPath = Path.Combine(FindRoot(), "TestResults", "postgresql-migration", "采购生命周期-确认依据-" + saveId.ToString("N") + ".txt");
        string fingerprint;
        if (!await HasReceipt(confirmId))
        {
            using var scope = app.Services.CreateScope();
            var basis = await scope.ServiceProvider.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(headId);
            Assert.IsTrue(basis.Success, basis.Error?.Code + ": " + basis.Message);
            Assert.AreEqual(2, basis.Data.Version); fingerprint = basis.Data.Fingerprint;
            Directory.CreateDirectory(Path.GetDirectoryName(basisPath)!); await File.WriteAllTextAsync(basisPath, fingerprint);
        }
        else fingerprint = await File.ReadAllTextAsync(basisPath);
        Assert.AreEqual(64, fingerprint.Length);
        var confirm = new ConfirmProcurementOrderCommand { CommandId = confirmId, ExpectedVersion = 2, ContextFingerprint = fingerprint };
        var confirmed = await RunPair(sp => sp.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(headId, confirm), confirmId, 3);
        Assert.IsNotNull(confirmed.ConfirmationId); Assert.AreEqual(ConfirmationStatusEnum.已确认, confirmed.ConfirmationStatus);

        await CheckPreview(cancelId, ProcurementReversalOperation.CancelConfirmation, 3);
        var cancel = new CancelProcurementOrderConfirmationCommand { CommandId = cancelId, ExpectedVersion = 3, Reason = "PG独立采购取消确认" };
        var cancelled = await RunPair(sp => sp.GetRequiredService<IProcurementOrderReversalService>().CancelConfirmationAsync(headId, cancel), cancelId, 4);
        Assert.IsNull(cancelled.ConfirmationId); Assert.AreEqual(ConfirmationStatusEnum.待确认, cancelled.ConfirmationStatus);
        await CheckPreview(voidId, ProcurementReversalOperation.Void, 4);
        var voidCommand = new VoidProcurementOrderCommand { CommandId = voidId, ExpectedVersion = 4, Reason = "PG独立采购作废" };
        var voided = await RunPair(sp => sp.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(headId, voidCommand), voidId, 5);
        Assert.AreEqual(PurchaseOrderLifecycleStatusEnum.Voided, voided.LifecycleStatus);

        using (var scope = app.Services.CreateScope())
        {
            var reversals = scope.ServiceProvider.GetRequiredService<IProcurementOrderReversalService>();
            var conflict = await reversals.CancelConfirmationAsync(headId, new() { CommandId = cancelId, ExpectedVersion = 3, Reason = "改变已提交原因" });
            Assert.IsFalse(conflict.Success); Assert.AreEqual(ServiceErrorCodes.CommandConflict, conflict.Error?.Code);
            var stale = await reversals.VoidAsync(headId, new() { CommandId = CommandId(9), ExpectedVersion = 4, Reason = voidCommand.Reason });
            Assert.IsFalse(stale.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict, stale.Error?.Code);
            Assert.IsNull(scope.ServiceProvider.GetRequiredService<DbSession>().Transaction);
        }
        Assert.IsTrue(await observer.Connection.ExecuteScalarAsync<bool>("""
            SELECT h.version=5 AND h.lifecycle_status=9 AND h.confirmation_status=0 AND NOT h.is_deleted
              AND h.active_confirmation_id IS NULL AND h.confirmed_date IS NULL AND h.confirmed_by IS NULL
              AND l.version=2 AND NOT l.is_deleted AND l.product_id IS NULL AND l.quantity=2 AND l.unlinked_qty=2
              AND l.unit_price=0 AND l.unit_amount=0 AND l.tax_price=0 AND l.tax_amount=0
              AND l.unit_snapshot='P07-81030bfc018d4a95953b54696c6356a0' AND l.source_uid IS NULL
            FROM public.purchase_order_head h JOIN public.purchase_order_line l ON l.purchase_order_id=h.uid WHERE h.uid=@Head AND l.uid=@Line
            """, new { Head = headId, Line = lineId }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = new[] { CommandId(4), CommandId(9) } }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=ANY(@Ids)", new { Ids = new[] { CommandId(4), CommandId(9) } }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT current_no FROM public.document_sequence WHERE doc_type='PO' AND year_month='800310'"));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { headId, lineId, successIds, fingerprint, Policy = "仅既有本例头/行生命周期更新，新增四回执四日志保留；其他事实摘要由主控核对" }));

        // 派生身份属于这一个已登记批次；重复运行不得换CommandId创建更多事实。
        Guid CommandId(byte suffix) { var bytes = registeredCommand.ToByteArray(); bytes[15] ^= suffix; return new Guid(bytes); }
        Task<bool> HasReceipt(Guid id) => observer.Connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM public.command_receipt WHERE command_id=@Id)", new { Id = id });

        // 每阶段独立请求作用域与物理连接并发；先提交者和重放者都必须返回原冻结结果，日志及回执各仅一条。
        async Task<ProcurementOrderCommandResult> RunPair(Func<IServiceProvider, Task<ServiceResult<ProcurementOrderCommandResult>>> action, Guid id, int version)
        {
            using var first = app.Services.CreateScope(); using var second = app.Services.CreateScope();
            var firstSession = first.ServiceProvider.GetRequiredService<DbSession>(); var secondSession = second.ServiceProvider.GetRequiredService<DbSession>();
            Assert.AreSame(firstSession, first.ServiceProvider.GetRequiredService<IDbSession>());
            Assert.AreNotEqual(((NpgsqlConnection)firstSession.Connection).ProcessID, ((NpgsqlConnection)secondSession.Connection).ProcessID);
            var results = await Task.WhenAll(action(first.ServiceProvider), action(second.ServiceProvider));
            foreach (var result in results)
            {
                Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
                Assert.AreEqual(headId, result.Data.PurchaseOrderId); Assert.AreEqual(version, result.Data.Version);
                Assert.AreEqual(2, result.Data.Lines.Single().Version);
            }
            Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(results[0].Data), System.Text.Json.JsonSerializer.Serialize(results[1].Data));
            Assert.IsNull(firstSession.Transaction); Assert.IsNull(secondSession.Transaction);
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = id }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id", new { Id = id }));
            return results[0].Data;
        }

        // 已提交阶段不重新对后续状态做旧版本预览；首次阶段仍必须经过实际一致读取和原资格规则。
        async Task CheckPreview(Guid id, ProcurementReversalOperation operation, int version)
        {
            if (await HasReceipt(id)) return;
            using var scope = app.Services.CreateScope();
            var preview = await scope.ServiceProvider.GetRequiredService<IProcurementOrderReversalService>().PreviewAsync(headId, operation, new() { ExpectedVersion = version });
            Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message);
            Assert.IsTrue(preview.Data.CanExecute, string.Join(";", preview.Data.Blockers));
            Assert.AreEqual(0, preview.Data.Contributions.Count); Assert.AreEqual(0, preview.Data.Blockers.Count);
            Assert.IsNull(scope.ServiceProvider.GetRequiredService<DbSession>().Transaction);
        }
    }

    /// <summary>配置构造入口与宿主工厂统一PG，真实只读验证库名和异常分类；不初始化结构或写入。</summary>
    [TestMethod]
    public async Task RealPg_ConfigurationSessionUsesPostgreSqlFactory()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection }).Build();
        using var session = new DbSession(configuration);
        Assert.IsInstanceOfType<NpgsqlConnection>(session.Connection);
        Assert.AreSame(PostgreSqlExceptionClassifier.Instance, session.Errors);
        Assert.AreEqual("dv_next_pg_failure_tests_20261004", await session.Connection.ExecuteScalarAsync<string>("SELECT current_database()"));
        Assert.IsNull(session.Transaction);
    }

    /// <summary>通过真实同值UPDATE造成Serializable旧快照行锁40001，验证自动端口保留分类且零业务增量；仅触及已登记测试单位的MVCC版本。</summary>
    [TestMethod]
    public async Task RealPg_AutomaticSerializationFailureReachesOuterClassifier()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var unitId = Guid.Parse("ad219bee-d668-4eae-ae2c-4a350164ce2e");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var before = await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(t)::text FROM public.sys_dict_data t WHERE uid=@Id", new { Id = unitId });
        Assert.IsNotNull(before, "本例只能使用此前登记的测试单位，不更新任意业务字典。");
        var sequence = await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("SELECT uid AS \"UserId\",user_name AS \"UserName\" FROM public.sys_user WHERE uid='1b1ecf70-0913-4dd9-8c40-442cf4055c7b'");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        builder.Services.AddScoped<RuntimeSerializationLogProbe>(sp => new(new PostgreSqlBusinessOperationLogRepository(sp.GetRequiredService<RepositoryBaseContext>()), sp.GetRequiredService<DbSession>(), connection, unitId));
        builder.Services.AddScoped<IBusinessOperationLogRepository>(sp => sp.GetRequiredService<RuntimeSerializationLogProbe>());
        await using var app = builder.Build(); using var requestScope = app.Services.CreateScope(); var services = requestScope.ServiceProvider;
        var session = services.GetRequiredService<DbSession>(); var parentId = Guid.NewGuid(); var childId = Guid.NewGuid(); var documentId = Guid.NewGuid(); var lineId = Guid.NewGuid();
        var request = new AutomaticPostingRequest { CommandId = childId, CorrelationId = parentId,
            PackingListId = Guid.Parse("6cbd7756-b6fa-494b-ae54-4a35019c1cfa"), PackingListNo = "M24-40001", BusinessDate = new DateTime(7000, 10, 5),
            Groups = [new() { GroupId = documentId, WarehouseSiteId = Guid.Parse("89ce70b7-d27a-49a2-aa09-4a35017a9804"), OutboundType = OutboundDocumentTypeEnum.FinalCustomerShipment,
                Lines = [new() { LineId = lineId, BasisId = lineId, ProductId = Guid.Parse("93e9f210-3be1-44e6-821a-4a35017a97f6"), Unit = "PCS", Quantity = 1 }] }] };
        var failure = await Assert.ThrowsAsync<BusinessCommandException>(() => services.GetRequiredService<BusinessCommandExecutor>().ExecuteAsync(
            parentId, "Migration.Automatic.SerializationProof", request.PackingListId, new { childId }, async ct =>
            {
                await services.GetRequiredService<IAutomaticPostingScope>().PrepareAsync(new() { CorrelationId = parentId, PackingListId = request.PackingListId,
                    CommandIds = [childId], OutboundDocumentIds = [documentId], CommandItemIds = [lineId], StockKeys = [new(request.Groups[0].WarehouseSiteId, request.Groups[0].Lines[0].ProductId)] }, ct);
                return await services.GetRequiredService<IOutboundPostingPort>().CreateAndPostAsync(request, ct);
            }, default, System.Data.IsolationLevel.Serializable,
            ct => ((ICommandRelatedTransactionPreparation)services.GetRequiredService<IBusinessOperationLogRepository>()).AcquireRelatedBeforeTransactionAsync(parentId, [childId], ct)));
        Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, failure.Code);
        Assert.AreEqual("40001", services.GetRequiredService<RuntimeSerializationLogProbe>().ObservedSqlState);
        Assert.IsNull(session.Transaction);
        Assert.AreEqual(before, await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(t)::text FROM public.sys_dict_data t WHERE uid=@Id", new { Id = unitId }));
        Assert.AreEqual(sequence, await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence"));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.outbound_document WHERE uid=@Id", new { Id = documentId }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = parentId }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id", new { Id = childId }));
        // 清理后的另一个backend可以取得父/子身份，错误路径不遗留事务前会话锁。
        await using (var lease = await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(observer, parentId, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default)) Assert.IsNotNull(lease);
        await using (var lease = await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(observer, childId, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default)) Assert.IsNotNull(lease);
    }

    /// <summary>仅在实际自动端口兼容审计读后触发真实两backend的MVCC冲突；其他查询/写入及租约完整转发，不伪造数据库异常。</summary>
    private sealed class RuntimeSerializationLogProbe(IBusinessOperationLogRepository inner, DbSession session, string connection, Guid unitId,
        Guid? targetCommandId = null, Func<DbSession, Task>? beforeConflict = null)
        : IBusinessOperationLogRepository, ICommandRelatedTransactionPreparation
    {
        /// <summary>实际驱动返回的SQLSTATE，不从消息猜测。</summary>
        internal string? ObservedSqlState { get; private set; }
        /// <summary>实际完成同值更新的独立backend身份，用于故障清理证据。</summary>
        internal int? ObservedWriterPid { get; private set; }
        /// <summary>保留真实审计读取后，以同值写入制造旧快照行锁冲突；字典业务列不变。</summary>
        public async Task<BusinessOperationLog?> GetByModuleCommandAsync(string module, Guid commandId)
        {
            var result = await inner.GetByModuleCommandAsync(module, commandId);
            // 旧单端口场景未指定筛选仍在首次读取故障；组合场景仅在声明的后段子命令触发，前段真实写入完整转发。
            if (targetCommandId.HasValue && targetCommandId.Value != commandId) return result;
            if (beforeConflict is not null) await beforeConflict(session);
            await session.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.sys_dict_data WHERE uid=@Id", new { Id = unitId }, session.Transaction);
            await using var writer = new NpgsqlConnection(connection); await writer.OpenAsync();
            ObservedWriterPid = writer.ProcessID;
            Assert.AreNotEqual(((NpgsqlConnection)session.Connection).ProcessID, writer.ProcessID);
            Assert.AreEqual(1, await writer.ExecuteAsync("UPDATE public.sys_dict_data SET version=version WHERE uid=@Id", new { Id = unitId }));
            try { await session.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.sys_dict_data WHERE uid=@Id FOR UPDATE", new { Id = unitId }, session.Transaction); }
            catch (PostgresException error) { ObservedSqlState = error.SqlState; Console.WriteLine("真实自动端口冲突 SQLSTATE=" + error.SqlState); throw; }
            Assert.Fail("两backend同值UPDATE后Serializable行锁必须发生实际40001。");
            return result;
        }
        /// <summary>转发既有子命令事务前准备，不改变锁身份。</summary>
        public Task<IAsyncDisposable?> AcquireRelatedBeforeTransactionAsync(Guid commandId, IReadOnlyList<Guid> relatedCommandIds, CancellationToken token) =>
            ((ICommandRelatedTransactionPreparation)inner).AcquireRelatedBeforeTransactionAsync(commandId, relatedCommandIds, token);
        /// <summary>转发原日志读取。</summary>
        public Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId) => inner.GetByCommandAsync(module, operationType, commandId);
        /// <summary>转发原日志追加。</summary>
        public Task<int> InsertAsync(BusinessOperationLog log) => inner.InsertAsync(log);
        /// <summary>转发原聚合追踪。</summary>
        public Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid aggregateUid, CancellationToken cancellationToken = default) => inner.GetByAggregateAsync(module, aggregateUid, cancellationToken);
    }

    /// <summary>真实三连接验证已声明子命令在首次Serializable快照前互斥、事务内不扩展、释放及竞争取消；不写业务数据。</summary>
    [TestMethod]
    public async Task RealPg_RelatedCommandPreparationLocksBeforeSnapshotAndReleases()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var parent = new DbSession(new PostgreSqlConnectionFactory(connection));
        using var contender = new DbSession(new PostgreSqlConnectionFactory(connection));
        using var holder = new DbSession(new PostgreSqlConnectionFactory(connection));
        var parentId = Guid.NewGuid(); var childId = Guid.NewGuid();
        await using (var lease = await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(parent, parentId,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default))
        {
            Assert.IsNotNull(lease);
            Assert.AreSame(lease, await PostgreSqlCommandLock.AcquireRelatedBeforeTransactionAsync(parent, parentId, [childId], default));
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
                await Assert.ThrowsAsync<OperationCanceledException>(() => PostgreSqlCommandLock.AcquireBeforeTransactionAsync(
                    contender, childId, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, cancellation.Token));
            using var transaction = parent.Connection.BeginTransaction(System.Data.IsolationLevel.Serializable);
            parent.Transaction = transaction;
            try
            {
                Assert.AreEqual(0, await PostgreSqlCommandLock.AcquireAsync(parent, childId, default));
                await Assert.ThrowsAsync<InvalidOperationException>(() => PostgreSqlCommandLock.AcquireAsync(parent, Guid.NewGuid(), default));
                await Assert.ThrowsAsync<InvalidOperationException>(() => PostgreSqlCommandLock.AcquireRelatedBeforeTransactionAsync(parent, parentId, [Guid.NewGuid()], default));
                transaction.Rollback();
            }
            finally { parent.Transaction = null; }
        }
        // 其他物理连接能重新获取子身份，证明完整释放；原父身份也能再次准备。
        await using (var released = await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(contender, childId,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default)) Assert.IsNotNull(released);
        await using (var held = await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(holder, childId,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default))
        await using (var lease = await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(parent, parentId,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default))
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAsync<OperationCanceledException>(() => PostgreSqlCommandLock.AcquireRelatedBeforeTransactionAsync(
                parent, parentId, [childId], cancellation.Token));
            Assert.IsNull(parent.Transaction);
        }
        await using (var released = await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(contender, parentId,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default)) Assert.IsNotNull(released);
    }

    /// <summary>标准库存来源的完整箱单Serializable过账、自动出库、执行证据及反向；固定登记身份接续，不以非标无库存样例替代联合库存链。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeStockPackingListPostsAndReversesAcrossConnections()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_STOCK_PACKING_COMMAND"), out var postId))
            Assert.Inconclusive("必须指定本批标准库存箱单CommandId，不自动重复建批。");
        var bytes = postId.ToByteArray(); bytes[15] ^= 1; var reverseId = new Guid(bytes);
        var prefix = "M24-" + postId.ToString("N"); var date = new DateTime(6000, 10, 5);
        var productId = Guid.Parse("93e9f210-3be1-44e6-821a-4a35017a97f6");
        var warehouseId = Guid.Parse("89ce70b7-d27a-49a2-aa09-4a35017a9804");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.command_receipt r
            JOIN public.sys_user u ON u.uid=r.actor_uid WHERE r.command_id='72b0d74b-1a72-4f70-a8e5-53eeb53080c4'
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build(); using var first = app.Services.CreateScope(); using var second = app.Services.CreateScope();
        var services = first.ServiceProvider; var session = services.GetRequiredService<DbSession>();
        Assert.AreNotEqual(((NpgsqlConnection)session.Connection).ProcessID, ((NpgsqlConnection)second.ServiceProvider.GetRequiredService<DbSession>().Connection).ProcessID);
        var headId = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.packing_list_head WHERE packing_list_no=@Prefix", new { Prefix = prefix });
        if (headId == Guid.Empty)
        {
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE doc_type='OI' AND year_month='600010'"), "只使用未存在的测试未来号段，不覆盖既有计数。");
            using var transaction = session.Connection.BeginTransaction(); session.Transaction = transaction;
            try
            {
                var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prefix)));
                var source = new GrossDemand { SourceType = "MD", SourceNo = prefix, ProductId = productId, SkuSnapshot = prefix, UnitSnapshot = "PCS", DemandQty = 1,
                    CustomerId = Guid.Parse("eec2a7df-44a2-47ef-9922-4a350164ce11"), DemandBasisFingerprint = fingerprint };
                Assert.IsTrue((await services.GetRequiredService<IRepository<GrossDemand>>().AddAsync(source)).Success);
                // 仅组装本例新输入的自引用；通用Add会重生主键，必须在同一夹具事务使用实际回写身份。
                Assert.AreEqual(1, await session.Connection.ExecuteAsync("UPDATE public.gross_demand SET source_uid=uid,source_line_id=uid WHERE uid=@Id", new { Id = source.Uid }, transaction));
                var demand = new NetDemand { GrossDemandId = source.Uid, ProductId = productId, GrossQty = 1, NetQty = 1, UnitSnapshot = "PCS",
                    DecompositionRevision = 1, LineNumber = 1, MaterialTypeSnapshot = "成品", SupplyTypeSnapshot = "采购", SourceDemandBasisFingerprintSnapshot = fingerprint };
                Assert.IsTrue((await services.GetRequiredService<IRepository<NetDemand>>().AddAsync(demand)).Success);
                Assert.AreEqual(1, await session.Connection.ExecuteAsync("UPDATE public.net_demand SET root_net_demand_id=uid WHERE uid=@Id", new { Id = demand.Uid }, transaction));
                var link = new DemandExecutionLink { NetDemandId = demand.Uid, TargetType = "PO", TargetHeadId = Guid.Parse("ebd27b4a-2f46-4046-9cd9-4a35017a9811"),
                    TargetLineId = Guid.Parse("f279fd58-5d0a-4692-aef5-4a35017a9815"), TargetNo = prefix, AllocatedQty = 1 };
                Assert.IsTrue((await services.GetRequiredService<IRepository<DemandExecutionLink>>().AddAsync(link)).Success);
                var head = new PackingListHead { PackingListNo = prefix, CustomerId = source.CustomerId.Value, InvoiceDate = date, CreateCommandId = postId };
                Assert.IsTrue((await services.GetRequiredService<IRepository<PackingListHead>>().AddAsync(head)).Success);
                var line = new PackingListLine { PackingListHeadId = head.Uid, GrossDemandId = source.Uid, ProductId = productId, LineNumber = 1,
                    SkuSnapshot = prefix, UnitSnapshot = "PCS", Qty = 1, SourceTypeSnapshot = "MD", SourceDocumentNoSnapshot = prefix };
                Assert.IsTrue((await services.GetRequiredService<IRepository<PackingListLine>>().AddAsync(line)).Success);
                transaction.Commit(); headId = head.Uid;
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { PostId = postId, ReverseId = reverseId, SourceId = source.Uid, RootId = demand.Uid, LinkId = link.Uid, HeadId = head.Uid, LineId = line.Uid, YearMonth = "600010" }));
            }
            finally { session.Transaction = null; }
        }
        var posting = services.GetRequiredService<IPackingListPostingService>();
        var postToken = await observer.Connection.ExecuteScalarAsync<string?>("SELECT preview_token FROM public.packing_list_posting_execution WHERE command_id=@Id", new { Id = postId });
        if (postToken is null)
        {
            var preview = await posting.PreviewAsync(headId, new PackingListPostPreviewRequest { ExpectedVersion = 1, BusinessDate = date }, default);
            Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message);
            Assert.AreEqual(0, preview.Data.Blockers.Count, string.Join(";", preview.Data.Blockers.Select(x => x.Message)));
            Assert.AreEqual(1, preview.Data.Actions.Count); Assert.AreEqual("Shipment", preview.Data.Actions.Single().ActionKind);
            Assert.AreEqual(warehouseId, preview.Data.Actions.Single().WarehouseSiteId);
            postToken = preview.Data.PreviewToken;
        }
        var postCommand = new PostPackingListCommand { CommandId = postId, ExpectedVersion = 1, BusinessDate = date, PreviewToken = postToken };
        var posted = await Task.WhenAll(posting.PostAsync(headId, postCommand, default),
            second.ServiceProvider.GetRequiredService<IPackingListPostingService>().PostAsync(headId, postCommand, default));
        foreach (var result in posted) Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
        Assert.AreEqual(posted[0].Data.ExecutionId, posted[1].Data.ExecutionId);
        Assert.AreEqual(1, posted[0].Data.Documents.Count); var outboundId = posted[0].Data.Documents.Single().Uid;
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.outbound_document WHERE trigger_packing_list_id=@Id", new { Id = headId }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_posting_execution WHERE command_id=@Id", new { Id = postId }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = postId }));
        using var third = app.Services.CreateScope(); using var fourth = app.Services.CreateScope();
        var reversing = third.ServiceProvider.GetRequiredService<IPackingListReversalService>();
        var reverseToken = await observer.Connection.ExecuteScalarAsync<string?>("SELECT preview_token FROM public.packing_list_posting_execution WHERE command_id=@Id", new { Id = reverseId });
        if (reverseToken is null)
        {
            Assert.AreEqual(3m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@Id", new { Id = productId }));
            var preview = await reversing.ReversePreviewAsync(headId, new PackingListReversePreviewRequest { ExpectedVersion = 2, BusinessDate = date, Reason = "PG标准库存箱单反向" }, default);
            Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message);
            Assert.AreEqual(0, preview.Data.Blockers.Count, string.Join(";", preview.Data.Blockers.Select(x => x.Message)));
            reverseToken = preview.Data.PreviewToken;
        }
        var reverseCommand = new ReversePackingListCommand { CommandId = reverseId, ExpectedVersion = 2, BusinessDate = date, Reason = "PG标准库存箱单反向", PreviewToken = reverseToken };
        var reversed = await Task.WhenAll(reversing.ReverseAsync(headId, reverseCommand, default),
            fourth.ServiceProvider.GetRequiredService<IPackingListReversalService>().ReverseAsync(headId, reverseCommand, default));
        foreach (var result in reversed) Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
        Assert.AreEqual(reversed[0].Data.ExecutionId, reversed[1].Data.ExecutionId);
        Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.packing_list_head WHERE uid=@Id AND posting_status=8", new { Id = headId }));
        Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.outbound_document WHERE uid=@Id AND posting_status=8", new { Id = outboundId }));
        Assert.AreEqual(4m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@Id", new { Id = productId }));
        Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE source_document_id=@Id", new { Id = outboundId }));
        Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_posting_execution WHERE packing_list_head_id=@Id", new { Id = headId }));
        Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_posting_execution_line l JOIN public.packing_list_posting_execution h ON h.uid=l.execution_id WHERE h.packing_list_head_id=@Id", new { Id = headId }));
        foreach (var scope in new[] { first, second, third, fourth }) Assert.IsNull(scope.ServiceProvider.GetRequiredService<DbSession>().Transaction);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { PostId = postId, ReverseId = reverseId, HeadId = headId, OutboundId = outboundId,
            Sequence = await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence"), Policy = "本例成功输入/库存/执行/回执/日志/号段保留，不重置" }));
    }

    /// <summary>零余额标准来源经同一箱单自动收货及出货，两个真实请求只提交一次；整单反向先恢复出库再反向入库，新增输入和追加事实保留。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeReceiptPackingListPostsAndReversesAcrossConnections()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var registeredId = Guid.Parse("8eb3622a-556c-4c83-a8d5-bca8c4229300");
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_RECEIPT_PACKING_COMMAND"), out var postId) || postId != registeredId)
            Assert.Inconclusive("只运行主控登记的自动收货箱单批次，不自动重复造数。");
        var bytes = postId.ToByteArray(); bytes[15] ^= 1; var reverseId = new Guid(bytes);
        var prefix = "M24R-" + postId.ToString("N"); var date = new DateTime(6100, 10, 5);
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("SELECT uid AS \"UserId\",user_name AS \"UserName\" FROM public.sys_user WHERE uid='1b1ecf70-0913-4dd9-8c40-442cf4055c7b' AND is_active AND NOT is_deleted");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build(); using var first = app.Services.CreateScope(); using var second = app.Services.CreateScope();
        var services = first.ServiceProvider; var session = services.GetRequiredService<DbSession>();
        Assert.AreNotEqual(((NpgsqlConnection)session.Connection).ProcessID, ((NpgsqlConnection)second.ServiceProvider.GetRequiredService<DbSession>().Connection).ProcessID);
        var headId = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.packing_list_head WHERE packing_list_no=@Prefix", new { Prefix = prefix });
        if (headId == Guid.Empty)
        {
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product WHERE sku=@Prefix", new { Prefix = prefix }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='610010'"), "不覆盖现有测试或业务号段。");
            using var transaction = session.Connection.BeginTransaction(); session.Transaction = transaction;
            try
            {
                // 十条输入原子建立，已确认PO为明确夹具，不声称验证了创建/确认采购或需求拆解业务。
                var supplier = new Supplier { SupplierCode = prefix, SupplierName = "PG自动收货测试供应商", DocRelateNo = prefix };
                Assert.IsTrue((await services.GetRequiredService<IRepository<Supplier>>().AddAsync(supplier)).Success);
                var product = new Product { Sku = prefix, Unit = "PCS", MaterialType = "成品", SupplyType = "采购", IsActive = true };
                Assert.IsTrue((await services.GetRequiredService<IRepository<Product>>().AddAsync(product)).Success);
                var warehouse = new WarehouseSite { Code = prefix, Name = "PG自动收货测试仓", SiteType = WarehouseSiteTypeEnum.SupplierVirtual, SupplierId = supplier.Uid, IsActive = true };
                Assert.IsTrue((await services.GetRequiredService<IRepository<WarehouseSite>>().AddAsync(warehouse)).Success);
                var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(prefix)));
                var source = new GrossDemand { SourceType = "MD", SourceNo = prefix, ProductId = product.Uid, SkuSnapshot = prefix, UnitSnapshot = "PCS", DemandQty = 2,
                    CustomerId = Guid.Parse("eec2a7df-44a2-47ef-9922-4a350164ce11"), DemandBasisFingerprint = fingerprint };
                Assert.IsTrue((await services.GetRequiredService<IRepository<GrossDemand>>().AddAsync(source)).Success);
                Assert.AreEqual(1, await session.Connection.ExecuteAsync("UPDATE public.gross_demand SET source_uid=uid,source_line_id=uid WHERE uid=@Id", new { Id = source.Uid }, transaction));
                var root = new NetDemand { GrossDemandId = source.Uid, ProductId = product.Uid, GrossQty = 2, NetQty = 2, UnitSnapshot = "PCS", DecompositionRevision = 1,
                    LineNumber = 1, MaterialTypeSnapshot = "成品", SupplyTypeSnapshot = "采购", SourceDemandBasisFingerprintSnapshot = fingerprint };
                Assert.IsTrue((await services.GetRequiredService<IRepository<NetDemand>>().AddAsync(root)).Success);
                Assert.AreEqual(1, await session.Connection.ExecuteAsync("UPDATE public.net_demand SET root_net_demand_id=uid WHERE uid=@Id", new { Id = root.Uid }, transaction));
                var purchase = new PurchaseOrderHead { OrderNo = prefix, OrderDate = date, OriginKind = 2, CustomerId = source.CustomerId.Value, SupplierId = supplier.Uid,
                    SupplierCode = prefix, ConfirmationStatus = ConfirmationStatusEnum.已确认, ExecutionRequestId = postId };
                Assert.IsTrue((await services.GetRequiredService<IRepository<PurchaseOrderHead>>().AddAsync(purchase)).Success);
                var purchaseLine = new PurchaseOrderLine { PurchaseOrderId = purchase.Uid, OrderNo = prefix, LineNumber = 1, ProductId = product.Uid, Sku = prefix,
                    Quantity = 2, UnlinkedQty = 0, UnitSnapshot = "PCS", MaterialType = "成品", UnitPrice = 0, TaxPrice = 0, UnitAmount = 0, TaxAmount = 0 };
                Assert.IsTrue((await services.GetRequiredService<IRepository<PurchaseOrderLine>>().AddAsync(purchaseLine)).Success);
                var link = new DemandExecutionLink { NetDemandId = root.Uid, ExecutionRequestId = postId, CommandItemId = root.Uid,
                    TargetType = "PO", TargetHeadId = purchase.Uid, TargetLineId = purchaseLine.Uid, TargetNo = prefix, AllocatedQty = 2 };
                Assert.IsTrue((await services.GetRequiredService<IRepository<DemandExecutionLink>>().AddAsync(link)).Success);
                var head = new PackingListHead { PackingListNo = prefix, CustomerId = source.CustomerId.Value, InvoiceDate = date, CreateCommandId = postId };
                Assert.IsTrue((await services.GetRequiredService<IRepository<PackingListHead>>().AddAsync(head)).Success);
                var line = new PackingListLine { PackingListHeadId = head.Uid, GrossDemandId = source.Uid, ProductId = product.Uid, LineNumber = 1, SkuSnapshot = prefix,
                    UnitSnapshot = "PCS", Qty = 2, SourceTypeSnapshot = "MD", SourceDocumentNoSnapshot = prefix };
                Assert.IsTrue((await services.GetRequiredService<IRepository<PackingListLine>>().AddAsync(line)).Success);
                transaction.Commit(); headId = head.Uid;
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { postId, reverseId, SupplierId = supplier.Uid, ProductId = product.Uid, WarehouseId = warehouse.Uid,
                    SourceId = source.Uid, RootId = root.Uid, PurchaseId = purchase.Uid, PurchaseLineId = purchaseLine.Uid, LinkId = link.Uid, HeadId = head.Uid, LineId = line.Uid, YearMonth = "610010" }));
            }
            finally { session.Transaction = null; }
        }
        var productId = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.product WHERE sku=@Prefix", new { Prefix = prefix });
        var warehouseId = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.warehouse_site WHERE code=@Prefix", new { Prefix = prefix });
        var posting = services.GetRequiredService<IPackingListPostingService>();
        var postToken = await observer.Connection.ExecuteScalarAsync<string?>("SELECT preview_token FROM public.packing_list_posting_execution WHERE command_id=@Id", new { Id = postId });
        if (postToken is null)
        {
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_balance WHERE product_id=@Id", new { Id = productId }), "本例必须从不存在的余额行开始，不能伪装为已有库存。");
            var preview = await posting.PreviewAsync(headId, new() { ExpectedVersion = 1, BusinessDate = date }, default);
            Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message);
            Assert.AreEqual(0, preview.Data.Blockers.Count, string.Join(";", preview.Data.Blockers.Select(x => x.Message)));
            Assert.AreEqual(2, preview.Data.Actions.Count);
            CollectionAssert.AreEquivalent(new[] { "Receipt", "Shipment" }, preview.Data.Actions.Select(x => x.ActionKind).ToArray());
            foreach (var action in preview.Data.Actions) { Assert.AreEqual(2m, action.Quantity); Assert.AreEqual(warehouseId, action.WarehouseSiteId); }
            postToken = preview.Data.PreviewToken;
        }
        var command = new PostPackingListCommand { CommandId = postId, ExpectedVersion = 1, BusinessDate = date, PreviewToken = postToken };
        var posted = await Task.WhenAll(posting.PostAsync(headId, command, default), second.ServiceProvider.GetRequiredService<IPackingListPostingService>().PostAsync(headId, command, default));
        foreach (var result in posted) Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
        Assert.AreEqual(posted[0].Data.ExecutionId, posted[1].Data.ExecutionId); Assert.AreEqual(2, posted[0].Data.Documents.Count);
        Assert.AreEqual(0m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Product", new { Warehouse = warehouseId, Product = productId }));
        using var third = app.Services.CreateScope(); using var fourth = app.Services.CreateScope();
        Assert.AreNotEqual(((NpgsqlConnection)third.ServiceProvider.GetRequiredService<DbSession>().Connection).ProcessID, ((NpgsqlConnection)fourth.ServiceProvider.GetRequiredService<DbSession>().Connection).ProcessID);
        var reversing = third.ServiceProvider.GetRequiredService<IPackingListReversalService>();
        var reverseToken = await observer.Connection.ExecuteScalarAsync<string?>("SELECT preview_token FROM public.packing_list_posting_execution WHERE command_id=@Id", new { Id = reverseId });
        const string reason = "PG自动收货及出货整单反向";
        if (reverseToken is null)
        {
            var preview = await reversing.ReversePreviewAsync(headId, new() { ExpectedVersion = 2, BusinessDate = date, Reason = reason }, default);
            Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message);
            Assert.AreEqual(0, preview.Data.Blockers.Count, string.Join(";", preview.Data.Blockers.Select(x => x.Message)));
            Assert.AreEqual(2, preview.Data.Actions.Count); reverseToken = preview.Data.PreviewToken;
        }
        var reverse = new ReversePackingListCommand { CommandId = reverseId, ExpectedVersion = 2, BusinessDate = date, Reason = reason, PreviewToken = reverseToken };
        var reversed = await Task.WhenAll(reversing.ReverseAsync(headId, reverse, default), fourth.ServiceProvider.GetRequiredService<IPackingListReversalService>().ReverseAsync(headId, reverse, default));
        foreach (var result in reversed) Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
        Assert.AreEqual(reversed[0].Data.ExecutionId, reversed[1].Data.ExecutionId);
        Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.packing_list_head WHERE uid=@Id AND posting_status=8", new { Id = headId }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inbound_document WHERE trigger_packing_list_id=@Id AND version=3 AND posting_status=8", new { Id = headId }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.outbound_document WHERE trigger_packing_list_id=@Id AND version=3 AND posting_status=8", new { Id = headId }));
        Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE product_id=@Id", new { Id = productId }));
        Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE product_id=@Id AND reversal_of_ledger_id IS NOT NULL", new { Id = productId }));
        Assert.AreEqual(0m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT sum(quantity_delta) FROM public.inventory_ledger_entry WHERE product_id=@Id", new { Id = productId }));
        Assert.AreEqual(0m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@Id", new { Id = productId }));
        Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_posting_execution WHERE packing_list_head_id=@Id", new { Id = headId }));
        Assert.AreEqual(4, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_posting_execution_line l JOIN public.packing_list_posting_execution h ON h.uid=l.execution_id WHERE h.packing_list_head_id=@Id", new { Id = headId }));
        foreach (var scope in new[] { first, second, third, fourth }) Assert.IsNull(scope.ServiceProvider.GetRequiredService<DbSession>().Transaction);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { postId, reverseId, headId, productId, warehouseId,
            Sequence = await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence"), Policy = "成功输入及全部自动入出库/流水/执行/回执/日志/号段保留，禁止重置或删除追加事实" }));
    }

    /// <summary>实际客供收货建立库存后，箱单组合收货/出货/客供耗用/非标交付及反向；复用登记输入，全部成功事实保留。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeAllPackingActionsPostAndReverse()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var registeredId = Guid.Parse("ae31d72e-7c55-4d25-90c9-282dbbe02500");
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_ALL_PACKING_COMMAND"), out var postId) || postId != registeredId)
            Assert.Inconclusive("只允许本批已登记四动作箱单命令，不创建随机替代事实。");
        var bytes = postId.ToByteArray(); bytes[15] ^= 1; var reverseId = new Guid(bytes); bytes[15] ^= 3; var seedId = new Guid(bytes);
        var prefix = "M24A-" + postId.ToString("N"); var date = new DateTime(6200, 10, 5);
        var rootProduct = Guid.Parse("d76404eb-46f0-4bbe-b41d-4a3501e0334e"); var warehouseId = Guid.Parse("9feb675e-9728-48a1-9393-4a3501e03351");
        var sourceId = Guid.Parse("c9ea08f3-6fd4-4a35-a444-4a3501e03354"); var rootId = Guid.Parse("121be989-00d3-4dde-851d-4a3501e0335d");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("SELECT uid AS \"UserId\",user_name AS \"UserName\" FROM public.sys_user WHERE uid='1b1ecf70-0913-4dd9-8c40-442cf4055c7b' AND is_active AND NOT is_deleted");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build(); using var first = app.Services.CreateScope(); using var second = app.Services.CreateScope();
        var services = first.ServiceProvider; var session = services.GetRequiredService<DbSession>();
        Assert.AreNotEqual(((NpgsqlConnection)session.Connection).ProcessID, ((NpgsqlConnection)second.ServiceProvider.GetRequiredService<DbSession>().Connection).ProcessID);
        var headId = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.packing_list_head WHERE packing_list_no=@Prefix", new { Prefix = prefix });
        if (headId == Guid.Empty)
        {
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product WHERE sku=@Prefix", new { Prefix = prefix }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='620010'"));
            var fingerprint = await observer.Connection.ExecuteScalarAsync<string>("SELECT demand_basis_fingerprint FROM public.gross_demand WHERE uid=@Id", new { Id = sourceId });
            Assert.IsNotNull(fingerprint); Assert.AreEqual(64, fingerprint.Length);
            using var transaction = session.Connection.BeginTransaction(); session.Transaction = transaction;
            try
            {
                // 只组装明确自有输入；客供初始余额不直接插入，稍后经真实人工采购入库命令产生。
                var product = new Product { Sku = prefix, Unit = "PCS", MaterialType = "客供件", SupplyType = "采购", IsActive = true };
                Assert.IsTrue((await services.GetRequiredService<IRepository<Product>>().AddAsync(product)).Success);
                var purchaseLine = new PurchaseOrderLine { PurchaseOrderId = Guid.Parse("78459881-e305-421f-a720-4a3501e03364"), OrderNo = "M24R-8eb3622a556c4c83a8d5bca8c4229300",
                    LineNumber = 2, ProductId = product.Uid, Sku = prefix, Quantity = 1, UnlinkedQty = 1, UnitSnapshot = "PCS", MaterialType = "客供件", UnitPrice = 0, UnitAmount = 0, TaxPrice = 0, TaxAmount = 0 };
                Assert.IsTrue((await services.GetRequiredService<IRepository<PurchaseOrderLine>>().AddAsync(purchaseLine)).Success);
                var child = new NetDemand { GrossDemandId = sourceId, ParentNetDemandId = rootId, RootNetDemandId = rootId, DeliveryTargetNetDemandId = rootId,
                    DeliveryMode = DemandDeliveryModeEnum.ParentExecutionSupplier, ProductId = product.Uid, GrossQty = 1, NetQty = 1, UnitSnapshot = "PCS", DecompositionRevision = 1,
                    LineNumber = 2, MaterialTypeSnapshot = "客供件", SupplyTypeSnapshot = "采购", SourceDemandBasisFingerprintSnapshot = fingerprint };
                Assert.IsTrue((await services.GetRequiredService<IRepository<NetDemand>>().AddAsync(child)).Success);
                var delivery = new GrossDemand { SourceType = "MD", SourceNo = prefix + "-D", IsNonStandard = true, UnitSnapshot = "PCS", DemandQty = 1,
                    CustomerId = Guid.Parse("eec2a7df-44a2-47ef-9922-4a350164ce11"), DemandBasisFingerprint = fingerprint };
                Assert.IsTrue((await services.GetRequiredService<IRepository<GrossDemand>>().AddAsync(delivery)).Success);
                Assert.AreEqual(1, await session.Connection.ExecuteAsync("UPDATE public.gross_demand SET source_uid=uid,source_line_id=uid WHERE uid=@Id", new { Id = delivery.Uid }, transaction));
                var seed = new InboundDocument { DocumentNo = prefix + "-S", DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, CreationMode = InboundCreationModeEnum.Manual,
                    WarehouseSiteId = warehouseId, ReceiptDate = date };
                Assert.IsTrue((await services.GetRequiredService<IRepository<InboundDocument>>().AddAsync(seed)).Success);
                var seedLine = new InboundDocumentLine { InboundDocumentId = seed.Uid, LineNumber = 1, ProductId = product.Uid, SourceLineId = purchaseLine.Uid, SkuSnapshot = prefix, UnitSnapshot = "PCS", Quantity = 1 };
                Assert.IsTrue((await services.GetRequiredService<IRepository<InboundDocumentLine>>().AddAsync(seedLine)).Success);
                var head = new PackingListHead { PackingListNo = prefix, CustomerId = delivery.CustomerId.Value, InvoiceDate = date, CreateCommandId = postId };
                Assert.IsTrue((await services.GetRequiredService<IRepository<PackingListHead>>().AddAsync(head)).Success);
                var standardLine = new PackingListLine { PackingListHeadId = head.Uid, LineNumber = 1, GrossDemandId = sourceId, ProductId = rootProduct,
                    SkuSnapshot = "M24R-8eb3622a556c4c83a8d5bca8c4229300", UnitSnapshot = "PCS", Qty = 2, SourceTypeSnapshot = "MD", SourceDocumentNoSnapshot = "M24R-8eb3622a556c4c83a8d5bca8c4229300" };
                Assert.IsTrue((await services.GetRequiredService<IRepository<PackingListLine>>().AddAsync(standardLine)).Success);
                var deliveryLine = new PackingListLine { PackingListHeadId = head.Uid, LineNumber = 2, GrossDemandId = delivery.Uid, IsNonStandardSnapshot = true, SkuSnapshot = "PG非标交付", UnitSnapshot = "PCS", Qty = 1,
                    SourceTypeSnapshot = "MD", SourceDocumentNoSnapshot = delivery.SourceNo };
                Assert.IsTrue((await services.GetRequiredService<IRepository<PackingListLine>>().AddAsync(deliveryLine)).Success);
                transaction.Commit(); headId = head.Uid;
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { postId, reverseId, seedId, ProductId = product.Uid, PurchaseLineId = purchaseLine.Uid, ChildId = child.Uid,
                    DeliveryId = delivery.Uid, SeedHeadId = seed.Uid, SeedLineId = seedLine.Uid, HeadId = head.Uid, StandardLineId = standardLine.Uid, DeliveryLineId = deliveryLine.Uid, YearMonth = "620010" }));
            }
            finally { session.Transaction = null; }
        }
        var componentId = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.product WHERE sku=@Prefix", new { Prefix = prefix });
        var seedHeadId = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.inbound_document WHERE document_no=@No", new { No = prefix + "-S" });
        var seedCommand = new PostInboundCommand { CommandId = seedId, ExpectedVersion = 1 };
        var seeded = await Task.WhenAll(services.GetRequiredService<IInboundPostingService>().PostAsync(seedHeadId, seedCommand), second.ServiceProvider.GetRequiredService<IInboundPostingService>().PostAsync(seedHeadId, seedCommand));
        foreach (var result in seeded) Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE product_id=@Id AND transaction_type=11 AND quantity_delta=1", new { Id = componentId }));
        var posting = services.GetRequiredService<IPackingListPostingService>();
        var postToken = await observer.Connection.ExecuteScalarAsync<string?>("SELECT preview_token FROM public.packing_list_posting_execution WHERE command_id=@Id", new { Id = postId });
        if (postToken is null)
        {
            var preview = await posting.PreviewAsync(headId, new() { ExpectedVersion = 1, BusinessDate = date }, default);
            Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message);
            Assert.AreEqual(0, preview.Data.Blockers.Count, string.Join(";", preview.Data.Blockers.Select(x => x.Message)));
            CollectionAssert.AreEquivalent(new[] { "Receipt", "Shipment", "Consumption", "Delivery" }, preview.Data.Actions.Select(x => x.ActionKind).ToArray());
            Assert.AreEqual(1m, preview.Data.Actions.Single(x => x.ActionKind == "Consumption").Quantity);
            Assert.AreEqual(1m, preview.Data.Actions.Single(x => x.ActionKind == "Delivery").Quantity);
            Assert.IsNull(preview.Data.Actions.Single(x => x.ActionKind == "Delivery").WarehouseSiteId);
            postToken = preview.Data.PreviewToken;
        }
        var command = new PostPackingListCommand { CommandId = postId, ExpectedVersion = 1, BusinessDate = date, PreviewToken = postToken };
        var posted = await Task.WhenAll(posting.PostAsync(headId, command, default), second.ServiceProvider.GetRequiredService<IPackingListPostingService>().PostAsync(headId, command, default));
        foreach (var result in posted) Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
        Assert.AreEqual(posted[0].Data.ExecutionId, posted[1].Data.ExecutionId); Assert.AreEqual(3, posted[0].Data.Documents.Count);
        using var third = app.Services.CreateScope(); using var fourth = app.Services.CreateScope();
        Assert.AreNotEqual(((NpgsqlConnection)third.ServiceProvider.GetRequiredService<DbSession>().Connection).ProcessID, ((NpgsqlConnection)fourth.ServiceProvider.GetRequiredService<DbSession>().Connection).ProcessID);
        var reversing = third.ServiceProvider.GetRequiredService<IPackingListReversalService>();
        var reverseToken = await observer.Connection.ExecuteScalarAsync<string?>("SELECT preview_token FROM public.packing_list_posting_execution WHERE command_id=@Id", new { Id = reverseId });
        const string reason = "PG四动作箱单整单反向";
        if (reverseToken is null)
        {
            Assert.AreEqual(0m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@Id", new { Id = componentId }));
            var preview = await reversing.ReversePreviewAsync(headId, new() { ExpectedVersion = 2, BusinessDate = date, Reason = reason }, default);
            Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message);
            Assert.AreEqual(0, preview.Data.Blockers.Count, string.Join(";", preview.Data.Blockers.Select(x => x.Message)));
            Assert.AreEqual(4, preview.Data.Actions.Count); reverseToken = preview.Data.PreviewToken;
        }
        var reverse = new ReversePackingListCommand { CommandId = reverseId, ExpectedVersion = 2, BusinessDate = date, Reason = reason, PreviewToken = reverseToken };
        var reversed = await Task.WhenAll(reversing.ReverseAsync(headId, reverse, default), fourth.ServiceProvider.GetRequiredService<IPackingListReversalService>().ReverseAsync(headId, reverse, default));
        foreach (var result in reversed) Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
        Assert.AreEqual(reversed[0].Data.ExecutionId, reversed[1].Data.ExecutionId);
        Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.packing_list_head WHERE uid=@Id AND posting_status=8", new { Id = headId }));
        Assert.AreEqual(1m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@Id", new { Id = componentId }));
        Assert.AreEqual(0m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@Id", new { Id = rootProduct }));
        Assert.AreEqual(3, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE product_id=@Id", new { Id = componentId }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE product_id=@Id AND transaction_type=21 AND quantity_delta=-1", new { Id = componentId }));
        Assert.AreEqual(8, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_posting_execution_line l JOIN public.packing_list_posting_execution e ON e.uid=l.execution_id WHERE e.packing_list_head_id=@Id", new { Id = headId }));
        Assert.AreEqual(2, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_posting_execution_line l JOIN public.packing_list_posting_execution e ON e.uid=l.execution_id WHERE e.packing_list_head_id=@Id AND l.action_kind='Delivery' AND l.ledger_id IS NULL AND l.document_id IS NULL AND l.warehouse_site_id IS NULL", new { Id = headId }));
        foreach (var scope in new[] { first, second, third, fourth }) Assert.IsNull(scope.ServiceProvider.GetRequiredService<DbSession>().Transaction);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { postId, reverseId, seedId, headId, componentId, rootProduct, warehouseId,
            Sequence = await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence"), Policy = "成功输入和所有追加事实精确保留；本例原根余额仅更新版本/序列，数量仍0" }));
    }

    /// <summary>不同父命令在来源锁后建立真实旧快照竞争，最多提交一次；失败清理、刷新拒绝及赢家反向按原业务执行，成功事实保留。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeDifferentPackingCommandsCompete()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var registeredId = Guid.Parse("9c4e3fe7-4d0a-40a7-bf0c-25a3d9306400");
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_PACKING_RACE_COMMAND"), out var firstId) || firstId != registeredId)
            Assert.Inconclusive("只运行主控明确登记的不同命令竞争批次。");
        var bytes = firstId.ToByteArray(); bytes[15] ^= 1; var commands = new[] { firstId, new Guid(bytes) };
        var prefixes = commands.Select(x => "B22-" + x.ToString("N")).ToArray(); var date = new DateTime(6300, 10, 5);
        var sourceId = Guid.Parse("c9ea08f3-6fd4-4a35-a444-4a3501e03354"); var rootProduct = Guid.Parse("d76404eb-46f0-4bbe-b41d-4a3501e0334e");
        var componentId = Guid.Parse("3e324d59-65ab-4906-ad70-4a3501eff0c8");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("SELECT uid AS \"UserId\",user_name AS \"UserName\" FROM public.sys_user WHERE uid='1b1ecf70-0913-4dd9-8c40-442cf4055c7b' AND is_active AND NOT is_deleted");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build(); using var first = app.Services.CreateScope(); using var second = app.Services.CreateScope();
        var services = new[] { first.ServiceProvider, second.ServiceProvider }; var sessions = services.Select(x => x.GetRequiredService<DbSession>()).ToArray();
        var backends = sessions.Select(x => ((NpgsqlConnection)x.Connection).ProcessID).ToArray(); Assert.AreNotEqual(backends[0], backends[1]);
        var heads = new Guid[2];
        for (var i = 0; i < 2; i++) heads[i] = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.packing_list_head WHERE packing_list_no=@No", new { No = prefixes[i] });
        Assert.IsTrue(heads.All(x => x == Guid.Empty) || heads.All(x => x != Guid.Empty), "两输入必须完整，不覆盖部分夹具。");
        if (heads[0] == Guid.Empty)
        {
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE year_month='630010'"));
            using var transaction = sessions[0].Connection.BeginTransaction(); sessions[0].Transaction = transaction;
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    var head = new PackingListHead { PackingListNo = prefixes[i], CustomerId = Guid.Parse("eec2a7df-44a2-47ef-9922-4a350164ce11"), InvoiceDate = date, CreateCommandId = commands[i] };
                    Assert.IsTrue((await services[0].GetRequiredService<IRepository<PackingListHead>>().AddAsync(head)).Success);
                    var line = new PackingListLine { PackingListHeadId = head.Uid, GrossDemandId = sourceId, ProductId = rootProduct, LineNumber = 1, Qty = 2, UnitSnapshot = "PCS",
                        SkuSnapshot = "M24R-8eb3622a556c4c83a8d5bca8c4229300", SourceTypeSnapshot = "MD", SourceDocumentNoSnapshot = "M24R-8eb3622a556c4c83a8d5bca8c4229300" };
                    Assert.IsTrue((await services[0].GetRequiredService<IRepository<PackingListLine>>().AddAsync(line)).Success); heads[i] = head.Uid;
                }
                transaction.Commit();
            }
            finally { sessions[0].Transaction = null; }
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { commands, heads, Policy = "四新输入一次原子提交，成功事实全部保留" }));
        }
        var tokenPath = Path.Combine(FindRoot(), "TestResults", "postgresql-migration", "不同箱单竞争-原预览-" + firstId.ToString("N") + ".json");
        var prior = (await observer.Connection.QueryAsync<Guid>("SELECT command_id FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = commands })).ToArray();
        var tokens = new string[2]; var winner = -1;
        if (prior.Length == 0)
        {
            for (var i = 0; i < 2; i++)
            {
                var preview = await services[i].GetRequiredService<IPackingListPostingService>().PreviewAsync(heads[i], new() { ExpectedVersion = 1, BusinessDate = date }, default);
                Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message);
                Assert.AreEqual(0, preview.Data.Blockers.Count, string.Join(";", preview.Data.Blockers.Select(x => x.Message)));
                CollectionAssert.AreEquivalent(new[] { "Receipt", "Shipment", "Consumption" }, preview.Data.Actions.Select(x => x.ActionKind).ToArray());
                tokens[i] = preview.Data.PreviewToken;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(tokenPath)!); await File.WriteAllTextAsync(tokenPath, System.Text.Json.JsonSerializer.Serialize(tokens));
            await using var blockerConnection = new NpgsqlConnection(connection); await blockerConnection.OpenAsync();
            await using var blocker = await blockerConnection.BeginTransactionAsync();
            await blockerConnection.ExecuteAsync("SELECT uid FROM public.gross_demand WHERE uid=@Id FOR UPDATE", new { Id = sourceId }, blocker);
            var pending = Enumerable.Range(0, 2).Select(i => services[i].GetRequiredService<IPackingListPostingService>().PostAsync(heads[i], PostCommand(i), default)).ToArray();
            var bothWaiting = false;
            try
            {
                // 外连接仅持锁不改行；观察两个实际业务事务进入来源FOR UPDATE排队，不用固定延迟代替旧快照证据。
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < deadline)
                {
                    if (await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_stat_activity WHERE pid=ANY(@Pids) AND cardinality(pg_blocking_pids(pid))>0 AND query LIKE '%gross_demand%FOR UPDATE%'", new { Pids = backends }) == 2)
                    { bothWaiting = true; break; }
                    await Task.Delay(50);
                }
            }
            finally { await blocker.RollbackAsync(); }
            var results = await Task.WhenAll(pending);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { commands, heads, backends, bothWaiting, Results = results.Select(x => new { x.Success, Code = x.Error?.Code, x.Message }).ToArray() }));
            Assert.IsTrue(bothWaiting, "两个真实业务backend必须都先等来源锁，不能用偶然串行替代本轮竞争。");
            Assert.AreEqual(1, results.Count(x => x.Success)); winner = Array.FindIndex(results, x => x.Success);
            Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable, results[1 - winner].Error?.Code, "Serializable原生竞争须由现有分类返回，不伪装业务成功或提交未知。");
        }
        else
        {
            Assert.AreEqual(1, prior.Length, "原竞争只能保留一个赢家，不掩盖双提交。"); winner = Array.IndexOf(commands, prior[0]);
            tokens = System.Text.Json.JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(tokenPath))!;
        }
        var loser = 1 - winner;
        foreach (var session in sessions) Assert.IsNull(session.Transaction);
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = commands }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.packing_list_posting_execution WHERE command_id=ANY(@Ids)", new { Ids = commands }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.packing_list_head WHERE uid=@Id AND posting_status=0", new { Id = heads[loser] }));
        // 从另一个物理连接重获失败父/子会话锁，证明错误后没有留租约；不修改业务事实。
        var childIds = new[] { "inbound", "outbound" }.Select(purpose => new Guid(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"PackingList:v1:{commands[loser]:N}:{purpose}"))[..16])).ToArray();
        await using (var lease = await PostgreSqlCommandLock.AcquireBeforeTransactionAsync(observer, commands[loser], Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, default))
        {
            Assert.IsNotNull(lease);
            await using var related = await PostgreSqlCommandLock.AcquireRelatedBeforeTransactionAsync(observer, commands[loser], childIds, default); Assert.IsNotNull(related);
        }
        var reverseBytes = commands[winner].ToByteArray(); reverseBytes[15] ^= 128; var reverseId = new Guid(reverseBytes);
        var reverseToken = await observer.Connection.ExecuteScalarAsync<string?>("SELECT preview_token FROM public.packing_list_posting_execution WHERE command_id=@Id", new { Id = reverseId });
        if (reverseToken is null)
        {
            var fresh = await services[loser].GetRequiredService<IPackingListPostingService>().PreviewAsync(heads[loser], new() { ExpectedVersion = 1, BusinessDate = date }, default);
            Assert.IsTrue(fresh.Success); Assert.IsTrue(fresh.Data.Blockers.Any(x => x.Code == "DELIVERY_EXCEEDED"));
            var refused = await services[loser].GetRequiredService<IPackingListPostingService>().PostAsync(heads[loser], PostCommand(loser), default);
            Assert.IsFalse(refused.Success); Assert.AreEqual("DELIVERY_EXCEEDED", refused.Error?.Code);
            var preview = await services[winner].GetRequiredService<IPackingListReversalService>().ReversePreviewAsync(heads[winner], new() { ExpectedVersion = 2, BusinessDate = date, Reason = "PG不同命令竞争赢家反向" }, default);
            Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message); Assert.AreEqual(0, preview.Data.Blockers.Count); reverseToken = preview.Data.PreviewToken;
        }
        var reverse = new ReversePackingListCommand { CommandId = reverseId, ExpectedVersion = 2, BusinessDate = date, Reason = "PG不同命令竞争赢家反向", PreviewToken = reverseToken };
        // 正向和反向属于不同HTTP请求，必须使用新的scoped自动资源上下文；不能为测试复用请求而改生产生命周期。
        using var reverseFirst = app.Services.CreateScope(); using var reverseSecond = app.Services.CreateScope();
        var reversed = await Task.WhenAll(reverseFirst.ServiceProvider.GetRequiredService<IPackingListReversalService>().ReverseAsync(heads[winner], reverse, default),
            reverseSecond.ServiceProvider.GetRequiredService<IPackingListReversalService>().ReverseAsync(heads[winner], reverse, default));
        foreach (var result in reversed) Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
        var replay = await services[winner].GetRequiredService<IPackingListPostingService>().PostAsync(heads[winner], PostCommand(winner), default); Assert.IsTrue(replay.Success);
        Assert.AreEqual(0m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@Id", new { Id = rootProduct }));
        Assert.AreEqual(1m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@Id", new { Id = componentId }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = commands[loser] }));
        Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE aggregate_uid=@Id", new { Id = heads[loser] }));
        foreach (var session in sessions) Assert.IsNull(session.Transaction);
        Assert.IsNull(reverseFirst.ServiceProvider.GetRequiredService<DbSession>().Transaction); Assert.IsNull(reverseSecond.ServiceProvider.GetRequiredService<DbSession>().Transaction);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { commands, heads, winner, loser, reverseId, Sequence = await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence") }));

        // 原版本及原预览是竞争输入的一部分；失败不得换CommandId绕过已经变化的业务事实。
        PostPackingListCommand PostCommand(int i) => new() { CommandId = commands[i], ExpectedVersion = 1, BusinessDate = date, PreviewToken = tokens[i] };
    }

    /// <summary>真实入库整单反向在已插入回执后发生测试异常时完整回滚；原命令可重试并双连接重放，成功追加事实保留。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeInboundReverseRollsBackReceiptFailureThenReplays()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_INBOUND_REVERSE_COMMAND"), out var reverseId))
            Assert.Inconclusive("必须指定本批登记反向CommandId，不自动创建替代事实。");
        var bytes = reverseId.ToByteArray(); bytes[15] ^= 1; var postId = new Guid(bytes);
        var prefix = "M21-REV-" + reverseId.ToString("N");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.command_receipt r
            JOIN public.sys_user u ON u.uid=r.actor_uid WHERE r.command_id='72b0d74b-1a72-4f70-a8e5-53eeb53080c4'
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        var armed = 1; var injected = 0;
        // 只在本例回执追加后注入一次异常；真实事务前租约、锁、Find和INSERT均完整转发，不伪造数据库成功结果。
        builder.Services.AddScoped<ICommandReceiptRepository>(services => new RuntimeReceiptFailureProbe(
            ActivatorUtilities.CreateInstance<PostgreSqlCommandReceiptRepository>(services), receipt =>
            {
                if (receipt.CommandId == reverseId && Interlocked.CompareExchange(ref armed, 0, 1) == 1)
                { Interlocked.Increment(ref injected); throw new InvalidOperationException("本例回执已真实追加后的受控验收异常"); }
            }));
        await using var app = builder.Build(); using var first = app.Services.CreateScope(); using var second = app.Services.CreateScope();
        var services = first.ServiceProvider; var session = services.GetRequiredService<DbSession>();
        var secondSession = second.ServiceProvider.GetRequiredService<DbSession>();
        Assert.AreNotEqual(((NpgsqlConnection)session.Connection).ProcessID, ((NpgsqlConnection)secondSession.Connection).ProcessID);
        var headId = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.inbound_document WHERE document_no=@Prefix", new { Prefix = prefix });
        var productId = Guid.Parse("93e9f210-3be1-44e6-821a-4a35017a97f6");
        var warehouseId = Guid.Parse("89ce70b7-d27a-49a2-aa09-4a35017a9804");
        if (headId == Guid.Empty)
        {
            using var transaction = session.Connection.BeginTransaction(); session.Transaction = transaction;
            try
            {
                var head = new InboundDocument { DocumentNo = prefix, DocumentType = InboundDocumentTypeEnum.PurchaseReceipt,
                    CreationMode = InboundCreationModeEnum.Manual, WarehouseSiteId = warehouseId, ReceiptDate = new DateTime(2026, 10, 5) };
                Assert.IsTrue((await services.GetRequiredService<IRepository<InboundDocument>>().AddAsync(head)).Success);
                var line = new InboundDocumentLine { InboundDocumentId = head.Uid, ProductId = productId,
                    SourceLineId = Guid.Parse("f279fd58-5d0a-4692-aef5-4a35017a9815"), LineNumber = 1, SkuSnapshot = prefix, UnitSnapshot = "PCS", Quantity = 1 };
                Assert.IsTrue((await services.GetRequiredService<IRepository<InboundDocumentLine>>().AddAsync(line)).Success);
                transaction.Commit(); headId = head.Uid;
            }
            finally { session.Transaction = null; }
        }
        var post = await services.GetRequiredService<IInboundPostingService>().PostAsync(headId, new PostInboundCommand { CommandId = postId, ExpectedVersion = 1 });
        Assert.IsTrue(post.Success, post.Error?.Code + ": " + post.Message);
        var lineId = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.inbound_document_line WHERE inbound_document_id=@Id", new { Id = headId });
        var originalId = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.inventory_ledger_entry WHERE command_id=@Id", new { Id = postId });
        var originalJson = await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(e)::text FROM public.inventory_ledger_entry e WHERE uid=@Id", new { Id = originalId });
        var reverse = services.GetRequiredService<IInboundReversalService>();
        var command = new ReverseInboundCommand { CommandId = reverseId, ExpectedVersion = 2, Reason = "  PG反向原子回滚验收  " };
        const string stateSql = """
            SELECT jsonb_build_object(
              'head',(SELECT to_jsonb(h) FROM public.inbound_document h WHERE uid=@Head),
              'balance',(SELECT to_jsonb(b) FROM public.inventory_balance b WHERE warehouse_site_id=@Warehouse AND product_id=@Product),
              'ledger',(SELECT coalesce(jsonb_agg(to_jsonb(e) ORDER BY e.uid),'[]'::jsonb) FROM public.inventory_ledger_entry e WHERE product_id=@Product),
              'logs',(SELECT coalesce(jsonb_agg(to_jsonb(l) ORDER BY l.uid),'[]'::jsonb) FROM public.business_operation_log l WHERE command_id=ANY(@Commands)),
              'receipts',(SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r.command_id),'[]'::jsonb) FROM public.command_receipt r WHERE command_id=ANY(@Commands)))::text
            """;
        var stateArgs = new { Head = headId, Warehouse = warehouseId, Product = productId, Commands = new[] { postId, reverseId } };
        if (await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = reverseId }) == 0)
        {
            var preview = await reverse.PreviewReverseAsync(headId, new InboundReversePreviewRequest { ExpectedVersion = 2 });
            Assert.IsTrue(preview.Success, preview.Message); Assert.IsTrue(preview.Data.CanReverse, string.Join(";", preview.Data.Blockers.Select(x => x.Message)));
            Assert.AreEqual(4m, preview.Data.StockImpacts.Single().ExpectedQuantity);
            var before = await observer.Connection.ExecuteScalarAsync<string>(stateSql, stateArgs);
            var sequenceBefore = await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence");
            var failed = await reverse.ReverseAsync(headId, command);
            Assert.IsFalse(failed.Success); Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, failed.Error?.Code);
            Assert.AreEqual(1, injected, "异常必须发生在真实PG回执写入之后，不能由前置校验拒绝替代。");
            Assert.AreEqual(before, await observer.Connection.ExecuteScalarAsync<string>(stateSql, stateArgs), "库存、单据、日志及已经INSERT的回执必须整体恢复到失败前状态。");
            Assert.IsNull(session.Transaction);
            Assert.AreEqual(sequenceBefore + 1, await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence"), "nextval消费不随业务回滚恢复，记录缺口而不重置序列。");
        }
        else armed = 0;
        var results = await Task.WhenAll(reverse.ReverseAsync(headId, command),
            second.ServiceProvider.GetRequiredService<IInboundReversalService>().ReverseAsync(headId,
                new ReverseInboundCommand { CommandId = reverseId, ExpectedVersion = 2, Reason = "PG反向原子回滚验收" }));
        foreach (var result in results)
        {
            Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message); Assert.AreEqual(3, result.Data.Version);
            Assert.AreEqual(InboundPostingStatusEnum.Reversed, result.Data.PostingStatus);
            Assert.AreEqual("PG反向原子回滚验收", result.Data.ReverseReason);
        }
        Assert.AreEqual("  PG反向原子回滚验收  ", command.Reason, "服务不改写原DTO。");
        Assert.AreEqual(originalJson, await observer.Connection.ExecuteScalarAsync<string>("SELECT to_jsonb(e)::text FROM public.inventory_ledger_entry e WHERE uid=@Id", new { Id = originalId }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE command_id=@Id AND reversal_of_ledger_id=@Original AND quantity_delta=-1 AND balance_after=4", new { Id = reverseId, Original = originalId }));
        Assert.AreEqual(4m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@Id", new { Id = productId }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = reverseId }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id", new { Id = reverseId }));
        var conflict = await reverse.ReverseAsync(headId, new ReverseInboundCommand { CommandId = reverseId, ExpectedVersion = 2, Reason = "不同原因" });
        Assert.IsFalse(conflict.Success); Assert.AreEqual(ServiceErrorCodes.CommandConflict, conflict.Error?.Code);
        var repeat = await reverse.ReverseAsync(headId, new ReverseInboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 3, Reason = "重复反向" });
        Assert.IsFalse(repeat.Success); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, repeat.Error?.Code);
        Assert.IsNull(session.Transaction); Assert.IsNull(secondSession.Transaction);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { PostId = postId, ReverseId = reverseId, HeadId = headId, LineId = lineId, OriginalId = originalId,
            Injected = injected, Sequence = await observer.Connection.ExecuteScalarAsync<long>("SELECT last_value FROM public.inventory_ledger_sequence"), Policy = "成功正反流水及回执日志保留，异常回滚的序列缺口不重置" }));
    }

    /// <summary>真实回执仓储的测试故障边界；完整保留PG事务准备协议，仅在实际INSERT后执行明确的本例回调。</summary>
    private sealed class RuntimeReceiptFailureProbe(PostgreSqlCommandReceiptRepository inner, Action<CommandReceipt> afterInsert)
        : ICommandReceiptRepository, ICommandTransactionPreparation
    {
        /// <summary>完整转发真实事务前租约。</summary>
        public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid commandId, CancellationToken token) => inner.AcquireBeforeTransactionAsync(commandId, token);
        /// <summary>完整转发真实命令锁。</summary>
        public Task<int> LockAsync(Guid commandId, CancellationToken token) => inner.LockAsync(commandId, token);
        /// <summary>读取真实PG回执，不伪造重放。</summary>
        public Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken token) => inner.FindAsync(commandId, token);
        /// <summary>先真实追加回执，再调用故障回调，让原业务执行器负责整组回滚。</summary>
        public async Task InsertAsync(CommandReceipt receipt, CancellationToken token)
        {
            await inner.InsertAsync(receipt, token); afterInsert(receipt);
        }
    }

    /// <summary>在完整宿主上提交人工采购入库；显式固定命令身份可复验同一夹具，保留成功只追加事实，不重置序列或修改既有数据。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeInboundPostsOnceAndRejectsNewCommand()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_INBOUND_COMMAND"), out var commandId))
            Assert.Inconclusive("必须指定本批登记的入库CommandId；不自动创建多批事实。");
        var ids = Enumerable.Range(1, 6).Select(number =>
        {
            var bytes = commandId.ToByteArray(); bytes[15] ^= (byte)number; return new Guid(bytes);
        }).ToArray();
        var prefix = "M21-" + commandId.ToString("N");
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        // 通用AddAsync沿用SQL源语义重新生成主键；复验时必须读取实际回写身份，不能把预设Guid当作已保存身份。
        var existingIds = (await observer.Connection.QueryAsync<Guid>("""
            SELECT uid FROM (
                SELECT 1 AS position,uid FROM public.product WHERE sku=@Prefix
                UNION ALL SELECT 2,uid FROM public.warehouse_site WHERE code=@Prefix
                UNION ALL SELECT 3,uid FROM public.purchase_order_head WHERE order_no=@Prefix
                UNION ALL SELECT 4,uid FROM public.purchase_order_line WHERE order_no=@Prefix
                UNION ALL SELECT 5,uid FROM public.inbound_document WHERE document_no=@Prefix
                UNION ALL SELECT 6,uid FROM public.inbound_document_line WHERE sku_snapshot=@Prefix
            ) fixture ORDER BY position
            """, new { Prefix = prefix })).ToArray();
        Assert.IsTrue(existingIds.Length is 0 or 6, "本例身份必须完整，不能覆盖不完整夹具。");
        if (existingIds.Length == 6) ids = existingIds;
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.command_receipt r
            JOIN public.sys_user u ON u.uid=r.actor_uid WHERE r.command_id=@Id
            """, new { Id = Guid.Parse("263221ad-07f6-4259-a191-c7158fb6909a") });
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build(); using var first = app.Services.CreateScope(); using var second = app.Services.CreateScope();
        var services = first.ServiceProvider;
        var session = services.GetRequiredService<DbSession>();
        var secondSession = second.ServiceProvider.GetRequiredService<DbSession>();
        Assert.AreNotEqual(((NpgsqlConnection)session.Connection).ProcessID, ((NpgsqlConnection)secondSession.Connection).ProcessID);
        try
        {
            // 六条输入夹具原子建立；已登记身份存在时只核对，不覆盖或重新初始化。
            if (await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inbound_document WHERE uid=@Id", new { Id = ids[4] }) == 0)
            {
                using var transaction = session.Connection.BeginTransaction(); session.Transaction = transaction;
                try
                {
                    var product = new Product { Uid = ids[0], Sku = prefix, Unit = "PCS", MaterialType = "成品", SupplyType = "采购", IsActive = true };
                    var warehouse = new WarehouseSite { Uid = ids[1], Code = prefix, Name = "PG实际入库测试仓", IsActive = true,
                        SupplierId = Guid.Parse("2416e672-0631-4d64-8631-4a350164ce2a") };
                    var purchase = new PurchaseOrderHead { Uid = ids[2], OrderNo = prefix, OrderDate = new DateTime(2026, 10, 5), OriginKind = 2,
                        CustomerId = Guid.Parse("eec2a7df-44a2-47ef-9922-4a350164ce11"), SupplierId = warehouse.SupplierId.Value,
                        SupplierCode = "PG实际入库测试来源", ConfirmationStatus = ConfirmationStatusEnum.已确认, ExecutionRequestId = commandId };
                    var purchaseLine = new PurchaseOrderLine { Uid = ids[3], PurchaseOrderId = purchase.Uid, OrderNo = prefix, LineNumber = 1,
                        ProductId = product.Uid, Sku = prefix, Quantity = 5, UnlinkedQty = 5, UnitSnapshot = "PCS", MaterialType = "成品", UnitPrice = 0 };
                    var inbound = new InboundDocument { Uid = ids[4], DocumentNo = prefix, DocumentType = InboundDocumentTypeEnum.PurchaseReceipt,
                        CreationMode = InboundCreationModeEnum.Manual, WarehouseSiteId = warehouse.Uid, ReceiptDate = new DateTime(2026, 10, 5) };
                    var line = new InboundDocumentLine { Uid = ids[5], InboundDocumentId = inbound.Uid, LineNumber = 1, ProductId = product.Uid,
                        SourceLineId = purchaseLine.Uid, SkuSnapshot = prefix, UnitSnapshot = "PCS", Quantity = 2 };
                    Assert.IsTrue((await services.GetRequiredService<IRepository<Product>>().AddAsync(product)).Success);
                    ids[0] = product.Uid; purchaseLine.ProductId = product.Uid; line.ProductId = product.Uid;
                    Assert.IsTrue((await services.GetRequiredService<IRepository<WarehouseSite>>().AddAsync(warehouse)).Success);
                    ids[1] = warehouse.Uid; inbound.WarehouseSiteId = warehouse.Uid;
                    Assert.IsTrue((await services.GetRequiredService<IRepository<PurchaseOrderHead>>().AddAsync(purchase)).Success);
                    ids[2] = purchase.Uid; purchaseLine.PurchaseOrderId = purchase.Uid;
                    Assert.IsTrue((await services.GetRequiredService<IRepository<PurchaseOrderLine>>().AddAsync(purchaseLine)).Success);
                    ids[3] = purchaseLine.Uid; line.SourceLineId = purchaseLine.Uid;
                    Assert.IsTrue((await services.GetRequiredService<IRepository<InboundDocument>>().AddAsync(inbound)).Success);
                    ids[4] = inbound.Uid; line.InboundDocumentId = inbound.Uid;
                    Assert.IsTrue((await services.GetRequiredService<IRepository<InboundDocumentLine>>().AddAsync(line)).Success);
                    ids[5] = line.Uid;
                    transaction.Commit();
                }
                finally { session.Transaction = null; }
            }
            var posting = services.GetRequiredService<IInboundPostingService>();
            var beforeVersion = await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.inbound_document WHERE uid=@Id", new { Id = ids[4] });
            Assert.IsTrue(beforeVersion is 1 or 2, "只验证已登记的未过账或已过账单据，不静默跳过其他状态。");
            if (beforeVersion == 1)
            {
                var preview = await posting.PreviewPostAsync(ids[4], new InboundPostPreviewRequest { ExpectedVersion = 1 });
                Assert.IsTrue(preview.Success, preview.Error?.Code + ": " + preview.Message);
                Assert.IsTrue(preview.Data.CanPost, string.Join(";", preview.Data.Blockers.Select(x => x.Message)));
                Assert.AreEqual(2m, preview.Data.StockImpacts.Single().ExpectedQuantity);
                var refusedId = Guid.NewGuid();
                var stale = await posting.PostAsync(ids[4], new PostInboundCommand { CommandId = refusedId, ExpectedVersion = 2 });
                Assert.IsFalse(stale.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict, stale.Error?.Code);
                Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = refusedId }));
                Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE product_id=@Id", new { Id = ids[0] }));
                Assert.IsNull(session.Transaction);
            }
            var command = new PostInboundCommand { CommandId = commandId, ExpectedVersion = 1 };
            var results = await Task.WhenAll(posting.PostAsync(ids[4], command),
                second.ServiceProvider.GetRequiredService<IInboundPostingService>().PostAsync(ids[4], command));
            foreach (var result in results)
            {
                Assert.IsTrue(result.Success, result.Error?.Code + ": " + result.Message);
                Assert.AreEqual(ids[4], result.Data.Uid); Assert.AreEqual(2, result.Data.Version);
                Assert.AreEqual(InboundPostingStatusEnum.Posted, result.Data.PostingStatus);
            }
            Assert.IsNull(session.Transaction); Assert.IsNull(secondSession.Transaction);
            // 本例来源可继续用于后续容量竞争；当前余额应等于保留的全部正式流水，而原命令事实仍唯一。
            Assert.AreEqual(await observer.Connection.ExecuteScalarAsync<decimal>("SELECT sum(quantity_delta) FROM public.inventory_ledger_entry WHERE warehouse_site_id=@Warehouse AND product_id=@Product", new { Warehouse = ids[1], Product = ids[0] }),
                await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE warehouse_site_id=@Warehouse AND product_id=@Product", new { Warehouse = ids[1], Product = ids[0] }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE command_id=@Id AND command_item_id=@Item AND quantity_delta=2 AND balance_after=2 AND source_document_id=@Head", new { Id = commandId, Item = ids[5], Head = ids[4] }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = commandId }));
            Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id", new { Id = commandId }));
            var refusedNewId = Guid.NewGuid();
            var refusedNew = await posting.PostAsync(ids[4], new PostInboundCommand { CommandId = refusedNewId, ExpectedVersion = 2 });
            Assert.IsFalse(refusedNew.Success); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, refusedNew.Error?.Code);
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id", new { Id = refusedNewId }));
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE command_id=@Id", new { Id = refusedNewId }));
            Assert.IsNull(session.Transaction);
        }
        finally
        {
            // nextval不随回滚恢复；登记实际序列状态与身份，绝不删除追加事实或重新设置号段。
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { commandId, ProductId = ids[0], WarehouseId = ids[1],
                PurchaseId = ids[2], PurchaseLineId = ids[3], InboundId = ids[4], InboundLineId = ids[5],
                SequenceState = await observer.Connection.QuerySingleAsync("SELECT last_value,is_called FROM public.inventory_ledger_sequence"),
                Policy = "成功事实及输入保留；不重置序列或禁用触发器" }));
        }
    }

    /// <summary>两个实际入库命令竞争同一采购剩余容量；由真实采购头锁形成等待，释放后复核最新收货事实，失败命令不提交库存或回执。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeInboundCapacityCompetitionReadsCommittedReceipt()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        if (!Guid.TryParse(Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_RUNTIME_INBOUND_CAPACITY_COMMAND"), out var commandId))
            Assert.Inconclusive("必须指定本批登记的容量竞争CommandId，不自动重复创建。");
        var bytes = commandId.ToByteArray(); bytes[15] ^= 1;
        var commands = new[] { commandId, new Guid(bytes) };
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connection));
        var source = await observer.Connection.QuerySingleAsync<RetainedInboundSource>("""
            SELECT p.uid AS "ProductId",w.uid AS "WarehouseId",h.uid AS "PurchaseId",l.uid AS "PurchaseLineId"
            FROM public.product p JOIN public.purchase_order_line l ON l.product_id=p.uid
            JOIN public.purchase_order_head h ON h.uid=l.purchase_order_id
            JOIN public.warehouse_site w ON w.code=p.sku
            WHERE p.sku='M21-72b0d74b1a724f70a8e553eeb53080c4'
            """);
        var remaining = await observer.Connection.ExecuteScalarAsync<decimal>("""
            SELECT l.quantity-COALESCE((SELECT sum(il.quantity) FROM public.inbound_document_line il
              JOIN public.inbound_document ih ON ih.uid=il.inbound_document_id
              WHERE il.source_line_id=l.uid AND ih.posting_status=1 AND ih.is_deleted=FALSE AND il.is_deleted=FALSE),0)
            FROM public.purchase_order_line l WHERE l.uid=@Id
            """, new { Id = source.PurchaseLineId });
        Assert.IsTrue(remaining is 3m or 1m, "只接续登记的首次竞争或保留成功事实，不覆盖其他业务变化。");
        var actor = await observer.Connection.QuerySingleAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.command_receipt r
            JOIN public.sys_user u ON u.uid=r.actor_uid WHERE r.command_id='72b0d74b-1a72-4f70-a8e5-53eeb53080c4'
            """);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices(); builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app = builder.Build(); using var first = app.Services.CreateScope(); using var second = app.Services.CreateScope();
        var scopes = new[] { first.ServiceProvider, second.ServiceProvider };
        var heads = new Guid[2]; var lines = new Guid[2];
        for (var index = 0; index < 2; index++)
        {
            var prefix = "M21-CAP-" + commands[index].ToString("N");
            heads[index] = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.inbound_document WHERE document_no=@Prefix", new { Prefix = prefix });
            if (heads[index] != Guid.Empty)
            {
                lines[index] = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.inbound_document_line WHERE inbound_document_id=@Id", new { Id = heads[index] });
                Assert.AreNotEqual(Guid.Empty, lines[index]); continue;
            }
            Assert.AreEqual(3m, remaining, "已提交竞争场景不再创建替代输入。");
            var session = scopes[index].GetRequiredService<DbSession>();
            using var transaction = session.Connection.BeginTransaction(); session.Transaction = transaction;
            try
            {
                var head = new InboundDocument { DocumentNo = prefix, DocumentType = InboundDocumentTypeEnum.PurchaseReceipt,
                    CreationMode = InboundCreationModeEnum.Manual, WarehouseSiteId = source.WarehouseId, ReceiptDate = new DateTime(2026, 10, 5) };
                Assert.IsTrue((await scopes[index].GetRequiredService<IRepository<InboundDocument>>().AddAsync(head)).Success);
                var line = new InboundDocumentLine { InboundDocumentId = head.Uid, ProductId = source.ProductId, SourceLineId = source.PurchaseLineId,
                    LineNumber = 1, SkuSnapshot = prefix, UnitSnapshot = "PCS", Quantity = 2 };
                Assert.IsTrue((await scopes[index].GetRequiredService<IRepository<InboundDocumentLine>>().AddAsync(line)).Success);
                transaction.Commit(); heads[index] = head.Uid; lines[index] = line.Uid;
            }
            finally { session.Transaction = null; }
        }
        var backends = scopes.Select(x => ((NpgsqlConnection)x.GetRequiredService<DbSession>().Connection).ProcessID).ToArray();
        Assert.AreNotEqual(backends[0], backends[1]);
        Task<SwiftExport.Contracts.Wrappers.ServiceResult<InboundPostResult>>[] pending;
        // 若上一轮业务已提交但观察断言失败，保留原成功事实。再次观察仅用未过账输家和新拒绝命令，不造第二份库存。
        var waitCommands = commands.ToArray(); var waitHeads = heads.ToArray();
        if (remaining == 1m)
        {
            var unposted = await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.inbound_document WHERE uid=ANY(@Ids) AND posting_status=0", new { Ids = heads });
            Assert.AreNotEqual(Guid.Empty, unposted);
            for (var index = 0; index < 2; index++)
            {
                var identity = commands[index].ToByteArray(); identity[15] ^= 4; waitCommands[index] = new Guid(identity); waitHeads[index] = unposted;
            }
        }
        var waited = false;
        using (var blocker = observer.Connection.BeginTransaction())
        {
            await observer.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.purchase_order_head WHERE uid=@Id FOR UPDATE", new { Id = source.PurchaseId }, blocker);
            pending = Enumerable.Range(0, 2).Select(index => scopes[index].GetRequiredService<IInboundPostingService>()
                .PostAsync(waitHeads[index], new PostInboundCommand { CommandId = waitCommands[index], ExpectedVersion = 1 })).ToArray();
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    // 观察连接持有事务，pg_stat_activity默认缓存需显式刷新，不能把首轮采样当作后续活动状态。
                    await observer.Connection.ExecuteAsync("SELECT pg_stat_clear_snapshot()", transaction: blocker);
                    // 服务器实际等待图覆盖直接及排队后的间接阻塞，不用固定sleep假装已命中锁等待。
                    if (await observer.Connection.ExecuteScalarAsync<int>("""
                        SELECT count(*) FROM pg_stat_activity WHERE pid=ANY(@Backends)
                          AND cardinality(pg_blocking_pids(pid))>0 AND query LIKE '%purchase_order_head%FOR UPDATE%'
                        """, new { Backends = backends }, blocker) == 2) { waited = true; break; }
                    await Task.Delay(50);
                }
            }
            finally { blocker.Rollback(); }
        }
        var results = await Task.WhenAll(pending);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Commands = commands, Heads = heads, Lines = lines, Backends = backends, Waited = waited, RemainingBefore = remaining, WaitCommands = waitCommands, WaitHeads = waitHeads }));
        Assert.IsTrue(waited, "两个真实后端必须先进入采购头锁等待，不能用偶然串行执行作为竞争证据。");
        if (remaining == 1m)
        {
            foreach (var result in results) { Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error?.Code); }
            Assert.AreEqual(0, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = waitCommands }));
            results = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => scopes[index].GetRequiredService<IInboundPostingService>()
                .PostAsync(heads[index], new PostInboundCommand { CommandId = commands[index], ExpectedVersion = 1 })));
        }
        Assert.AreEqual(1, results.Count(x => x.Success)); Assert.AreEqual(1, results.Count(x => !x.Success));
        var winner = Array.FindIndex(results, x => x.Success); var loser = 1 - winner;
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, results[loser].Error?.Code);
        StringAssert.Contains(results[loser].Message, "不能超量入库");
        Assert.AreEqual(4m, await observer.Connection.ExecuteScalarAsync<decimal>("SELECT on_hand_qty FROM public.inventory_balance WHERE product_id=@Id", new { Id = source.ProductId }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE command_id=ANY(@Ids)", new { Ids = commands }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=ANY(@Ids)", new { Ids = commands }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=ANY(@Ids)", new { Ids = commands }));
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.inbound_document WHERE uid=@Id AND posting_status=0", new { Id = heads[loser] }));
        foreach (var scope in scopes) Assert.IsNull(scope.GetRequiredService<DbSession>().Transaction);
        var replay = await scopes[winner].GetRequiredService<IInboundPostingService>().PostAsync(heads[winner], new PostInboundCommand { CommandId = commands[winner], ExpectedVersion = 1 });
        Assert.IsTrue(replay.Success); Assert.AreEqual(heads[winner], replay.Data.Uid);
        Assert.AreEqual(1, await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.inventory_ledger_entry WHERE command_id=ANY(@Ids)", new { Ids = commands }));
    }

    /// <summary>已登记入库测试来源身份，不包含配置或账号凭据。</summary>
    private sealed class RetainedInboundSource
    {
        /// <summary>本例产品身份。</summary>
        public Guid ProductId { get; set; }
        /// <summary>本例仓库身份。</summary>
        public Guid WarehouseId { get; set; }
        /// <summary>已确认采购输入头身份。</summary>
        public Guid PurchaseId { get; set; }
        /// <summary>采购容量来源行身份。</summary>
        public Guid PurchaseLineId { get; set; }
    }

    /// <summary>两个真实请求会话并发提交同一独立采购命令，验证Serializable、真实取号、日志与回执只提交一次并可重放。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeIndependentPurchaseCreatesOnceAcrossTwoConnections()
    {
        var connection=PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var observer=new DbSession(new PostgreSqlConnectionFactory(connection));
        var actor=await observer.Connection.QueryFirstAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.sys_user u
            WHERE u.is_active=TRUE AND u.is_deleted=FALSE AND EXISTS(
                SELECT 1 FROM public.sys_user_role ur JOIN public.sys_role r ON r.uid=ur.role_id
                WHERE ur.user_id=u.uid AND ur.is_deleted=FALSE AND r.is_active=TRUE AND r.is_deleted=FALSE
                  AND r.role_code='SYSTEM_ADMIN') ORDER BY u.uid;
            """);
        var fixture="P07-"+Guid.NewGuid().ToString("N");var commandId=Guid.NewGuid();
        var customer=new Customer{CustomerCode=fixture,CustomerName="PG命令测试",DocRelateNo=fixture,IsActive=true};
        var supplier=new Supplier{SupplierCode=fixture,SupplierName="PG命令测试",DocRelateNo=fixture,IsActive=true};
        var unit=new SysDictData{DictCode="Unit",DataNameEn=fixture,IsEnabled=true};
        var year=4000+Math.Abs(commandId.GetHashCode()%5000);var date=new DateTime(year,10,5);var month=date.ToString("yyyyMM");
        Assert.AreEqual(0,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.document_sequence WHERE doc_type='PO' AND year_month=@Month",new{Month=month}),"本例只使用不存在的随机未来年月取号，不重置现有号段。");
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=Path.Combine(FindRoot(),"SwiftExport.WebApi"),EnvironmentName="Development"});
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:Default"]=connection});
        builder.AddWebApiServices();builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app=builder.Build();using var first=app.Services.CreateScope();using var second=app.Services.CreateScope();
        var firstServices=first.ServiceProvider;var firstSession=firstServices.GetRequiredService<DbSession>();var secondSession=second.ServiceProvider.GetRequiredService<DbSession>();
        Assert.AreNotEqual(((NpgsqlConnection)firstSession.Connection).ProcessID,((NpgsqlConnection)secondSession.Connection).ProcessID);
        try
        {
            Assert.IsTrue((await firstServices.GetRequiredService<IRepository<Customer>>().AddAsync(customer)).Success);
            Assert.IsTrue((await firstServices.GetRequiredService<IRepository<Supplier>>().AddAsync(supplier)).Success);
            Assert.IsTrue((await firstServices.GetRequiredService<IRepository<SysDictData>>().AddAsync(unit)).Success);
            var command=new CreateIndependentPurchaseOrderCommand{CommandId=commandId,CustomerId=customer.Uid,SupplierId=supplier.Uid,OrderDate=date,
                Lines=[new CreateIndependentPurchaseOrderLineCommand{IsNonStandard=true,DescriptionZh="真实PG非标采购",Quantity=2m,UnitPrice=0m,Unit=fixture}]};
            var results=await Task.WhenAll(firstServices.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(command),
                second.ServiceProvider.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(command));
            foreach(var result in results)Assert.IsTrue(result.Success,result.Error?.Code+": "+result.Message);
            var id=results[0].Data.PurchaseOrderId;Assert.AreEqual(id,results[1].Data.PurchaseOrderId);Assert.AreEqual(results[0].Data.OrderNo,results[1].Data.OrderNo);
            Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_head WHERE execution_request_id=@Id",new{Id=commandId}));
            Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_line WHERE purchase_order_id=@Id",new{Id=id}));
            Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id",new{Id=commandId}));
            Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id",new{Id=commandId}));
            Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT current_no FROM public.document_sequence WHERE doc_type='PO' AND year_month=@Month",new{Month=month}));
            var replay=await firstServices.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(command);
            Assert.IsTrue(replay.Success);Assert.AreEqual(id,replay.Data.PurchaseOrderId);
        }
        finally
        {
            // 回执及日志在源/PG均只追加，不能删除或关闭触发器来让测试通过；完整输入身份用于登记和后续同命令重放。
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{commandId,CustomerId=customer.Uid,SupplierId=supplier.Uid,UnitId=unit.Uid,Fixture=fixture,YearMonth=month,Policy="提交成功事实保留；不删除回执/日志/号段或禁用只追加保护"}));
        }
    }

    /// <summary>只接受主控已登记的成功测试命令，在两实际请求会话复用原输入重放，不创建新夹具或消费取号。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeIndependentPurchaseRetainedCommandReplaysWithoutChanges()
    {
        var connection=PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        if(!Guid.TryParse(Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_REPLAY_PURCHASE_COMMAND"),out var commandId))
            Assert.Inconclusive("未指定主控已登记的成功测试CommandId；不创建替代事实。");
        using var observer=new DbSession(new PostgreSqlConnectionFactory(connection));
        var actor=await observer.Connection.QuerySingleAsync<RuntimeUser>("""
            SELECT u.uid AS "UserId",u.user_name AS "UserName" FROM public.command_receipt r
            JOIN public.sys_user u ON u.uid=r.actor_uid WHERE r.command_id=@Id
              AND r.command_type='Procurement.Order.Create' AND u.is_active=TRUE AND u.is_deleted=FALSE
            """,new{Id=commandId});
        var prior=await observer.Connection.QuerySingleAsync<RetainedPurchase>("""
            SELECT h.uid AS "HeadId",h.customer_id AS "CustomerId",h.supplier_id AS "SupplierId",h.order_date AS "OrderDate",l.unit_snapshot AS "Unit"
            FROM public.purchase_order_head h JOIN public.purchase_order_line l ON l.purchase_order_id=h.uid
            WHERE h.execution_request_id=@Id AND l.product_id IS NULL AND l.quantity=2 AND l.unit_price=0
              AND l.description_zh='真实PG非标采购';
            """,new{Id=commandId});
        var command=new CreateIndependentPurchaseOrderCommand{CommandId=commandId,CustomerId=prior.CustomerId,SupplierId=prior.SupplierId,OrderDate=prior.OrderDate,
            Lines=[new CreateIndependentPurchaseOrderLineCommand{IsNonStandard=true,DescriptionZh="真实PG非标采购",Quantity=2m,UnitPrice=0m,Unit=prior.Unit}]};
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=Path.Combine(FindRoot(),"SwiftExport.WebApi"),EnvironmentName="Development"});
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:Default"]=connection});
        builder.AddWebApiServices();builder.Services.AddSingleton<ICurrentUser>(actor);
        await using var app=builder.Build();using var first=app.Services.CreateScope();using var second=app.Services.CreateScope();
        var firstSession=first.ServiceProvider.GetRequiredService<DbSession>();var secondSession=second.ServiceProvider.GetRequiredService<DbSession>();
        Assert.AreNotEqual(((NpgsqlConnection)firstSession.Connection).ProcessID,((NpgsqlConnection)secondSession.Connection).ProcessID);
        var results=await Task.WhenAll(first.ServiceProvider.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(command),
            second.ServiceProvider.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(command));
        foreach(var result in results){Assert.IsTrue(result.Success,result.Error?.Code+": "+result.Message);Assert.AreEqual(prior.HeadId,result.Data.PurchaseOrderId);}
        Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_head WHERE execution_request_id=@Id",new{Id=commandId}));
        Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_line WHERE purchase_order_id=@Id",new{Id=prior.HeadId}));
        Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id",new{Id=commandId}));
        Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id",new{Id=commandId}));
        Assert.AreEqual(1,await observer.Connection.ExecuteScalarAsync<int>("SELECT current_no FROM public.document_sequence WHERE doc_type='PO' AND year_month=@Month",new{Month=prior.OrderDate.ToString("yyyyMM")}));
    }

    /// <summary>已登记成功场景的业务输入身份；不读取凭据，不从任意单据构造新命令。</summary>
    private sealed class RetainedPurchase
    {
        /// <summary>已提交头身份。</summary>
        public Guid HeadId{get;set;}
        /// <summary>原创建客户身份。</summary>
        public Guid CustomerId{get;set;}
        /// <summary>原创建供应商身份。</summary>
        public Guid SupplierId{get;set;}
        /// <summary>原订单日期，对应原指纹日期格式。</summary>
        public DateTime OrderDate{get;set;}
        /// <summary>原本例唯一单位。</summary>
        public string Unit{get;set;}="";
    }

    /// <summary>仅读取既有活动管理员身份，为实际服务权限核验提供当前请求用户，不修改账号或授权。</summary>
    private sealed class RuntimeUser:ICurrentUser
    {
        /// <summary>实际既有用户名。</summary>
        public string UserName{get;set;}="";
        /// <summary>实际既有账号身份。</summary>
        public Guid? UserId{get;set;}
        /// <summary>本例已确认有效管理员身份，服务仍须查询实际权限。</summary>
        public bool IsAuthenticated=>true;
    }

    /// <summary>使用真实 WebApi 注册解析所有应用与领域接口，检查共享会话/别名，并执行普通基础查询。</summary>
    [TestMethod]
    public async Task RealPg_RuntimeWiringResolvesCompleteHost()
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = Path.Combine(FindRoot(), "SwiftExport.WebApi"), EnvironmentName = "Development"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connection });
        builder.AddWebApiServices();
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var session = services.GetRequiredService<DbSession>();
        Assert.IsInstanceOfType<NpgsqlConnection>(session.Connection);
        Assert.AreSame(session, services.GetRequiredService<IDbSession>());
        Assert.AreSame(session, services.GetRequiredService<RepositoryBaseContext>().Session);
        Assert.AreSame(PostgreSqlExceptionClassifier.Instance, services.GetRequiredService<IDatabaseExceptionClassifier>());
        Assert.IsInstanceOfType<PostgreSqlDapperRepository<Product>>(services.GetRequiredService<IRepository<Product>>());

        // 覆盖真正注册的所有领域/应用接口；缺构造依赖或遗留 SQL 实现都不能以仅编译替代验收。
        var contracts = builder.Services.Select(x => x.ServiceType).Where(x => x.IsInterface && !x.ContainsGenericParameters
            && (x.Namespace?.StartsWith("SwiftExport.Core.Interfaces", StringComparison.Ordinal) == true
                || x.Namespace?.StartsWith("SwiftExport.Application.Interfaces", StringComparison.Ordinal) == true)).Distinct();
        foreach (var contract in contracts)
        {
            var service = services.GetRequiredService(contract);
            if (contract.Name.EndsWith("Repository", StringComparison.Ordinal))
                StringAssert.StartsWith(service.GetType().Name, "PostgreSql", contract.FullName);
        }
        Assert.AreSame<object>(services.GetRequiredService<IPackingListQueryRepository>(), services.GetRequiredService<IPackingListDocumentRepository>());
        Assert.AreSame<object>(services.GetRequiredService<IPackingListQueryRepository>(), services.GetRequiredService<IPackingListSourceExitRepository>());
        Assert.AreSame(services.GetRequiredService<IProcurementReversalRepository>(), services.GetRequiredService<IProcurementOrderFactLockRepository>());
        await services.GetRequiredService<ISysDicCurrencyRepository>().GetEnabledCurrenciesAsync();
        var missingName = "PG-Wiring-" + Guid.NewGuid().ToString("N");
        Assert.IsNull(await services.GetRequiredService<IAuthRepository>().GetActiveUserByNameAsync(missingName));
        Assert.IsNull(await services.GetRequiredService<PostgreSqlDevUserQuery>().FindUidAsync(missingName));
        Assert.AreEqual("dv_next_pg_failure_tests_20261004", await session.Connection.ExecuteScalarAsync<string>("SELECT current_database()"));
    }

    /// <summary>定位实际仓库配置，不使用测试输出目录内可能过期的配置副本。</summary>
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport.WebApi", "appsettings.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("无法定位 WebApi 配置。");
    }
}
