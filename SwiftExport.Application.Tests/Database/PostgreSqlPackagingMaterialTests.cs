using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;

namespace SwiftExport.Application.Tests.Database;

/// <summary>包材候选、引用读取与单件自重的离线 SQL 契约，不替代真实 PG 并发验证。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>普通候选不强加类型和启用过滤，包材候选始终启用，显式 ActiveOnly 支持普通候选。</summary>
    [TestMethod]
    [DataRow(null, false)]
    [DataRow(null, true)]
    [DataRow("包材", false)]
    [DataRow("包材", true)]
    public async Task PackagingLookup_OnlyExplicitFiltersChangeOrdinaryBehavior(string? type, bool active)
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductRepository(DictionaryContext(fixture));
        await repository.SearchLookupAsync(new ProductLookupRequest { Keyword = "PK", MaterialType = type!, ActiveOnly = active });
        var sql = fixture.Connection.Text;
        Assert.AreEqual(type is not null, sql.Contains("p.material_type,' ') COLLATE public.erp_dictionary_ci=@MaterialType", StringComparison.Ordinal));
        Assert.AreEqual(active || type == MaterialTypesEnum.包材, sql.Contains("p.is_active=TRUE", StringComparison.Ordinal));
        if (type is not null) Assert.AreEqual(type, fixture.Connection.Values["MaterialType"]);
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>引用只取未删除包装的包材身份，空集合无查询；单件自重由物料列独立投影。</summary>
    [TestMethod]
    public async Task PackagingReferences_UseOnlyActiveRelationsAndExposeIndependentWeight()
    {
        using var fixture = new Fixture();
        var repository = new PostgreSqlProductRepository(DictionaryContext(fixture));
        Assert.HasCount(0, await repository.GetReferencedPackagingMaterialIdsAsync([]));
        Assert.HasCount(0, fixture.Connection.Commands);
        await repository.GetReferencedPackagingMaterialIdsAsync([Guid.NewGuid()]);
        StringAssert.Contains(fixture.Connection.Text, "packaging_material_id=ANY(@Ids) AND is_deleted=FALSE");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
        fixture.Connection.Rows.Columns.Add("UnitWeight", typeof(decimal));
        fixture.Connection.Rows.Rows.Add(0.25m);
        var dto = await repository.GetDtoByIdAsync(Guid.NewGuid());
        Assert.AreEqual(0.25m, dto!.UnitWeight);
        StringAssert.Contains(fixture.Connection.Text, "p.\"unit_weight\" AS \"UnitWeight\"");
        Assert.IsFalse(fixture.Connection.Text.Contains("nw_kg", StringComparison.Ordinal));
    }
}
