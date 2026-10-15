using System.Diagnostics;
using Dapper;
using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>两个独立真实 PG 会话的包装引用互斥验收；有限提交随机夹具，最后按夹具物料身份清理。</summary>
public sealed partial class ProductPackagingPostgreSqlTests
{
    /// <summary>两个排队顺序下，建立引用与包材删除/改型不能同时成功，不得形成指向失效物料的有效包装。</summary>
    [TestMethod]
    [DataRow("delete", true)] [DataRow("delete", false)]
    [DataRow("retype", true)] [DataRow("retype", false)]
    [DataRow("soft-delete", true)] [DataRow("soft-delete", false)]
    public async Task ReferenceCreationAndMaterialInvalidation_AreMutuallyExclusive(string mutation, bool referenceFirst)
    {
        await using var fixture = await ProductPackagingPostgreSqlFixture.CreateAsync();
        var owner = await fixture.AddProductAsync(); var material = await fixture.AddProductAsync(true);
        using var first = fixture.NewScope(); using var second = fixture.NewScope();
        var ownerSnapshot = (await first.Query.GetDetailAsync(owner.Uid)).Data;
        var materialSnapshot = (await second.Repository.GetDtoByIdAsync(material.Uid))!;
        var request = Request(ownerSnapshot, new() { CreateItems = [Create(ProductPackageTypeCodes.Carton, 12, material.Uid)] });
        await using var barrier = new NpgsqlConnection(fixture.ConnectionString); await barrier.OpenAsync();
        await using var blocked = await barrier.BeginTransactionAsync();
        await barrier.ExecuteAsync("SELECT uid FROM public.product WHERE uid=@Id FOR UPDATE", new { Id = material.Uid }, blocked);
        var firstPid = ((NpgsqlConnection)first.Session.Connection).ProcessID;
        var secondPid = ((NpgsqlConnection)second.Session.Connection).ProcessID;
        Task<ServiceResult<SaveProductDetailResponse>> reference;
        Task<(bool Success, string? Code, string Message)> change;
        async Task<(bool Success, string? Code, string Message)> Mutate()
        {
            if (mutation == "delete")
            {
                var deleted = await second.Maintenance.DeleteAsync(new() { Uid = material.Uid, Version = material.Version });
                return (deleted.Success, deleted.Error?.Code, deleted.Message);
            }
            if (mutation == "soft-delete") materialSnapshot.IsDeleted = true;
            else materialSnapshot.MaterialType = MaterialTypesEnum.零件;
            var updated = await second.Maintenance.UpdateAsync(new() { Uid = material.Uid, Version = material.Version, Data = materialSnapshot });
            return (updated.Success, updated.Error?.Code, updated.Message);
        }
        if (referenceFirst)
        {
            reference = first.Maintenance.SaveDetailAsync(request); await WaitForLock(barrier, blocked, firstPid, reference);
            change = Mutate(); await WaitForLock(barrier, blocked, secondPid, change);
        }
        else
        {
            change = Mutate(); await WaitForLock(barrier, blocked, secondPid, change);
            reference = first.Maintenance.SaveDetailAsync(request); await WaitForLock(barrier, blocked, firstPid, reference);
        }
        await blocked.RollbackAsync();
        await Task.WhenAll(reference, change).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.IsFalse(reference.Result.Success && change.Result.Success, "包装引用与包材失效不能同时提交。");
        Assert.IsTrue(reference.Result.Success || change.Result.Success, "至少一个排队操作应成功，不接受死锁/超时代替业务互斥。");
        if (!reference.Result.Success) Assert.IsTrue(reference.Result.Error.Code is ServiceErrorCodes.ServiceUnavailable or ServiceErrorCodes.VersionConflict or ServiceErrorCodes.Validation);
        if (!change.Result.Success)
            Assert.IsTrue(change.Result.Code is ServiceErrorCodes.BusinessBlocked or ServiceErrorCodes.Validation or ServiceErrorCodes.VersionConflict or ServiceErrorCodes.ServiceUnavailable,
                $"包材失效侧错误类别 {change.Result.Code}：{change.Result.Message}");
        Console.WriteLine($"引用结果：{reference.Result.Success}/{reference.Result.Error?.Code}；包材失效结果：{change.Result.Success}/{change.Result.Code}。");
        using var check = fixture.NewScope();
        var packages = await check.Repository.GetPackagesAsync(owner.Uid);
        Assert.IsTrue(packages.All(row => row.IsPackagingMaterialValid), "不能提交指向已删除或非包材的活动关系。");
        Assert.IsNull(first.Session.Transaction); Assert.IsNull(second.Session.Transaction);
    }

    /// <summary>同 Owner 同类型并发新增最多一条；父 Version/Serializable 与唯一索引共同保护，不靠客户端先查。</summary>
    [TestMethod]
    public async Task ConcurrentSameRoleCreation_LeavesExactlyOneCarton()
    {
        await using var fixture = await ProductPackagingPostgreSqlFixture.CreateAsync(); var owner = await fixture.AddProductAsync();
        using var first = fixture.NewScope(); using var second = fixture.NewScope();
        var initial = (await first.Query.GetDetailAsync(owner.Uid)).Data;
        await using var barrier = new NpgsqlConnection(fixture.ConnectionString); await barrier.OpenAsync();
        await using var blocked = await barrier.BeginTransactionAsync();
        await barrier.ExecuteAsync("SELECT uid FROM public.product WHERE uid=@Id FOR UPDATE", new { Id = owner.Uid }, blocked);
        var a = first.Maintenance.SaveDetailAsync(Request(initial, new() { CreateItems = [Create(ProductPackageTypeCodes.Carton, 12)] }));
        await WaitForLock(barrier, blocked, ((NpgsqlConnection)first.Session.Connection).ProcessID, a);
        var b = second.Maintenance.SaveDetailAsync(Request(initial, new() { CreateItems = [Create(ProductPackageTypeCodes.Carton, 99)] }));
        await WaitForLock(barrier, blocked, ((NpgsqlConnection)second.Session.Connection).ProcessID, b);
        await blocked.RollbackAsync(); await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.AreEqual(1, new[] { a.Result.Success, b.Result.Success }.Count(value => value));
        var rejected = a.Result.Success ? b.Result : a.Result;
        Assert.IsTrue(rejected.Error.Code is ServiceErrorCodes.ServiceUnavailable or ServiceErrorCodes.VersionConflict);
        using var check = fixture.NewScope(); var detail = (await check.Query.GetDetailAsync(owner.Uid)).Data;
        Assert.HasCount(1, detail.Packages); Assert.AreEqual(2, detail.Version);
        var duplicate = await check.Maintenance.SaveDetailAsync(Request(detail, new() { CreateItems = [Create(ProductPackageTypeCodes.Carton, 3)] }));
        Assert.IsFalse(duplicate.Success); Assert.AreEqual(ServiceErrorCodes.Validation, duplicate.Error.Code);
        Assert.HasCount(1, (await check.Repository.GetPackagesAsync(owner.Uid)));
    }

    /// <summary>Owner 与包材交叉引用也从相同 PG uid 次序取锁，允许可识别序列化失败，不接受死锁或未知错误。</summary>
    [TestMethod]
    public async Task CrossOwnerMaterialReferences_UseStableGlobalProductOrder()
    {
        await using var fixture = await ProductPackagingPostgreSqlFixture.CreateAsync();
        var a = await fixture.AddProductAsync(true); var b = await fixture.AddProductAsync(true);
        using var first = fixture.NewScope(); using var second = fixture.NewScope();
        var aDetail = (await first.Query.GetDetailAsync(a.Uid)).Data; var bDetail = (await second.Query.GetDetailAsync(b.Uid)).Data;
        var parameters = new PostgreSqlParameters(); parameters.AddIds("Ids", [a.Uid, b.Uid]);
        var smallest = await first.Session.Connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.product WHERE uid=ANY(@Ids) ORDER BY uid LIMIT 1", parameters);
        await using var barrier = new NpgsqlConnection(fixture.ConnectionString); await barrier.OpenAsync();
        await using var blocked = await barrier.BeginTransactionAsync();
        await barrier.ExecuteAsync("SELECT uid FROM public.product WHERE uid=@Id FOR UPDATE", new { Id = smallest }, blocked);
        var x = first.Maintenance.SaveDetailAsync(Request(aDetail, new() { CreateItems = [Create(ProductPackageTypeCodes.Carton, 12, b.Uid)] }));
        await WaitForLock(barrier, blocked, ((NpgsqlConnection)first.Session.Connection).ProcessID, x);
        var y = second.Maintenance.SaveDetailAsync(Request(bDetail, new() { CreateItems = [Create(ProductPackageTypeCodes.Carton, 12, a.Uid)] }));
        await WaitForLock(barrier, blocked, ((NpgsqlConnection)second.Session.Connection).ProcessID, y);
        await blocked.RollbackAsync(); await Task.WhenAll(x, y).WaitAsync(TimeSpan.FromSeconds(20));
        foreach (var result in new[] { x.Result, y.Result }.Where(result => !result.Success))
            Assert.IsTrue(result.Error.Code is ServiceErrorCodes.ServiceUnavailable or ServiceErrorCodes.VersionConflict, result.Message);
        using var retry = fixture.NewScope();
        if (!x.Result.Success) await AddCarton(retry, a.Uid, b.Uid);
        if (!y.Result.Success) await AddCarton(retry, b.Uid, a.Uid);
        Assert.IsTrue((await retry.Repository.GetPackagesAsync(a.Uid)).Single().IsPackagingMaterialValid);
        Assert.IsTrue((await retry.Repository.GetPackagesAsync(b.Uid)).Single().IsPackagingMaterialValid);
    }

    /// <summary>用服务端等待图确认真实排队，不以固定延时猜测并发；只在有界时间内轮询。</summary>
    private static async Task WaitForLock(NpgsqlConnection observer, NpgsqlTransaction transaction, int processId, Task operation)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(6))
        {
            if (await observer.ExecuteScalarAsync<bool>("SELECT cardinality(pg_blocking_pids(@Pid)) > 0", new { Pid = processId }, transaction)) return;
            if (operation.IsCompleted) { await operation; Assert.Fail("操作未进入预期行锁等待，不能据此宣称并发通过。"); }
            await Task.Delay(20);
        }
        Assert.Fail("未在有界时间内观察到真实行锁等待。");
    }
}
