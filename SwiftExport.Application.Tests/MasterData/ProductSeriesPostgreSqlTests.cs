using Dapper;
using Mapster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Systems.Dictionaries.Data;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>在已授权专用库验证系列身份读写、字典引用与真实锁竞争，只创建和清理本次夹具。</summary>
[TestClass]
public sealed class ProductSeriesPostgreSqlTests
{
    /// <summary>系列读写使用稳定 Uid，显示及搜索实时采用新名称；无效系列回滚，明确空值可以清除。</summary>
    [TestMethod]
    public async Task SeriesIdentity_RenameReadSearchValidateAndClear()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var scope = fixture.NewScope();
        var dictionaries = Dictionaries(scope);
        var dto = fixture.Series.Adapt<SysDictDataDto>();
        dto.DataNameZh = fixture.CodePrefix + "新系列名";
        Assert.IsTrue((await dictionaries.UpdateAsync(new() { Uid = dto.Uid, Version = dto.Version, Data = dto })).Success);
        var detail = (await scope.Query.GetDetailAsync(fixture.Master.Uid)).Data;
        Assert.AreEqual(fixture.Series.Uid, detail.Master.ProductSeriesId);
        Assert.AreEqual(dto.DataNameZh, detail.ProductSeriesName);
        var page = (await scope.Query.GetPagedAsync(new() { Keyword = dto.DataNameZh })).Data;
        Assert.AreEqual(fixture.Master.Uid, page.Items.Single().Uid);
        Assert.AreEqual(dto.DataNameZh, page.Items.Single().ProductSeriesName);
        // 模板、缺失或空身份及停用系列分别不得作为新系列写入。
        var disabled = await fixture.AddProfileAsync(scope, "失效系列", DictCodes.ProductSeries);
        disabled.IsEnabled = false;
        Assert.IsTrue((await scope.Services.GetRequiredService<IRepository<SysDictData>>().UpdateAsync(disabled)).Success);
        foreach (var invalid in new[] { fixture.Profile.Uid, Guid.NewGuid(), Guid.Empty, disabled.Uid })
        {
            var request = await fixture.DetailRequestAsync(scope);
            request.Master.ProductSeriesId = invalid;
            var rejected = await scope.Maintenance.SaveDetailAsync(request);
            Assert.IsFalse(rejected.Success);
            Assert.AreEqual(ServiceErrorCodes.Validation, rejected.Error.Code);
            Assert.AreEqual(fixture.Series.Uid, (await scope.Query.GetDetailAsync(fixture.Master.Uid)).Data.Master.ProductSeriesId);
        }
        var clear = await fixture.DetailRequestAsync(scope);
        clear.Master.ProductSeriesId = null;
        var cleared = await scope.Maintenance.SaveDetailAsync(clear);
        Assert.IsTrue(cleared.Success, cleared.Message);
        Assert.IsNull(cleared.Data.Detail.Master.ProductSeriesId);
        Assert.IsNull(cleared.Data.Detail.ProductSeriesName);
    }

    /// <summary>停用未引用系列与主型号首次采用它的两种排队顺序都不能同时提交失效引用，也不会死锁。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SeriesDisableAndMasterSelection_AreSerialized(bool disableFirst)
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var setup = fixture.NewScope();
        var series = await fixture.AddProfileAsync(setup, "并发系列", DictCodes.ProductSeries);
        var saveScope = fixture.NewScope();
        var disableScope = fixture.NewScope();
        var request = await fixture.DetailRequestAsync(saveScope);
        request.Master.ProductSeriesId = series.Uid;
        var dto = series.Adapt<SysDictDataDto>();
        dto.IsEnabled = false;
        var dictionaries = Dictionaries(disableScope);
        await using var barrier = new NpgsqlConnection(fixture.ConnectionString);
        await barrier.OpenAsync();
        await using var transaction = await barrier.BeginTransactionAsync();
        await barrier.ExecuteAsync("SELECT uid FROM public.sys_dict_data WHERE uid=@Id FOR UPDATE", new { Id = series.Uid }, transaction);
        async Task<bool> DisableAsync() => (await dictionaries.UpdateAsync(new() { Uid = dto.Uid, Version = dto.Version, Data = dto })).Success;
        async Task<bool> SaveAsync() => (await saveScope.Maintenance.SaveDetailAsync(request)).Success;
        Task<bool> disable;
        Task<bool> save;
        if (disableFirst)
        {
            disable = DisableAsync();
            await WaitForLockAsync(barrier, transaction, disableScope);
            save = SaveAsync();
            await WaitForLockAsync(barrier, transaction, saveScope);
        }
        else
        {
            save = SaveAsync();
            await WaitForLockAsync(barrier, transaction, saveScope);
            disable = DisableAsync();
            await WaitForLockAsync(barrier, transaction, disableScope);
        }
        await transaction.CommitAsync();
        var disabled = await disable.WaitAsync(TimeSpan.FromSeconds(15));
        var saved = await save.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.AreEqual(disableFirst, disabled);
        Assert.AreEqual(!disableFirst, saved);
        var current = (await setup.Query.GetDetailAsync(fixture.Master.Uid)).Data;
        Assert.AreEqual(disableFirst ? fixture.Series.Uid : series.Uid, current.Master.ProductSeriesId);
        Assert.IsNull(saveScope.Session.Transaction);
        Assert.IsNull(disableScope.Session.Transaction);
    }

    /// <summary>构造字典用例时复用同一 scope 的会话和仓储，不引入额外事务。</summary>
    private static SysDictDataService Dictionaries(ProductAttributePostgreSqlFixture.Scope scope) => new(
        scope.Services.GetRequiredService<ISysDictDataRepository>(), scope.Services.GetRequiredService<ISysDictTypeRepository>(),
        new ServiceBaseContext(scope.Session, NullLoggerFactory.Instance), scope.Services.GetRequiredService<IProductAttributeRepository>());

    /// <summary>以实际 pg_blocking_pids 确认正式请求已经排队，短轮询有截止时间，不用长延时猜测顺序。</summary>
    private static async Task WaitForLockAsync(NpgsqlConnection monitor, NpgsqlTransaction transaction, ProductAttributePostgreSqlFixture.Scope scope)
    {
        var pid = ((NpgsqlConnection)scope.Session.Connection).ProcessID;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await monitor.ExecuteScalarAsync<bool>("SELECT cardinality(pg_blocking_pids(@Pid))>0", new { Pid = pid }, transaction)) return;
            await Task.Delay(20);
        }
        Assert.Fail("系列保存竞争请求未在预期时间内进入真实数据库锁等待。");
    }
}
