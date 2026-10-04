using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>BOM PostgreSQL 普通读取验收，只在固定保留库创建随机事务数据并回滚，不改旧行或结构。</summary>
[TestClass]
public sealed class PostgreSqlBomQueryIntegrationTests
{
    /// <summary>共享有效组成保留不同正标记、停用父子及快照；缺失组件不丢行，非正标记和软删行不展示。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlBomQueryIntegration")]
    public Task CurrentQueries_MapSnapshotsAndSoftDeletedReferenceBoundaries() => InRollback(async context =>
    {
        var prefix = Prefix();
        var color = new ProductColor { ColorCodeUnique = prefix, ColorCode = prefix, ColorNameZh = "中文饰面" };
        var deletedColor = new ProductColor { ColorCodeUnique = prefix + "D", ColorCode = prefix, ColorNameZh = "已删饰面", IsDeleted = true };
        var parent = ProductRow(prefix + "Parent", prefix + "Bom ", 2);
        parent.IsActive = false;
        var child = ProductRow(prefix + "Child");
        child.IsActive = false;
        child.DescriptionZh = "当前中文描述";
        child.FeaturesZh = "当前中文配置";
        child.MainImage = "current-image";
        child.ColorId = color.Uid;
        var deletedChild = ProductRow(prefix + "Deleted");
        deletedChild.IsDeleted = true;
        deletedChild.DescriptionZh = "不能展示";
        var badColorChild = ProductRow(prefix + "BadColor");
        badColorChild.ColorId = deletedColor.Uid;
        foreach (var entity in new BaseEntity[] { color, deletedColor, parent, child, deletedChild, badColorChild }) await Insert(context, entity);
        var first = Line(parent, child.Uid, "old-component-snapshot", 2, 2);
        first.BomNo = prefix.ToLowerInvariant() + "bom";
        first.Note = "note";
        first.Numerator = 2;
        first.Denominator = 3;
        var deletedReference = Line(parent, deletedChild.Uid, "deleted-child-snapshot", 2, 1);
        var missingReference = Line(parent, Guid.NewGuid(), "missing-child-snapshot", 2, 3);
        var hiddenFinish = Line(parent, badColorChild.Uid, "deleted-color-snapshot", 2, 4);
        var nullComponent = Line(parent, Guid.Empty, "null-child-identity-snapshot", 2, 6);
        nullComponent.ComponentProductId = null;
        var otherPositive = Line(parent, child.Uid, "other-positive", 1, 7);
        var zero = Line(parent, child.Uid, "zero", 0, 8);
        var negative = Line(parent, child.Uid, "negative", -1, 9);
        var deleted = Line(parent, child.Uid, "deleted-line", 2, 5);
        deleted.IsDeleted = true;
        foreach (var row in new[] { first, deletedReference, missingReference, hiddenFinish, nullComponent, otherPositive, zero, negative, deleted }) await Insert(context, row);
        var repository = new PostgreSqlBomRepository(context);
        var dto = (await repository.GetBomDtoByBomNoAsync(" " + prefix.ToUpperInvariant() + "BOM ")).ToArray();
        Assert.HasCount(6, dto);
        CollectionAssert.AreEqual(new[] { deletedReference.Uid, first.Uid, missingReference.Uid, hiddenFinish.Uid, nullComponent.Uid, otherPositive.Uid }, dto.Select(row => row.Uid).ToArray());
        Assert.AreEqual(Guid.Empty, dto.Single(row => row.Uid == nullComponent.Uid).SubSkuId);
        Assert.AreEqual("null-child-identity-snapshot", dto.Single(row => row.Uid == nullComponent.Uid).SubSku);
        var mapped = dto.Single(row => row.Uid == first.Uid);
        Assert.AreEqual(first.BomNo, mapped.BomNo);
        Assert.AreEqual(child.Uid, mapped.SubSkuId);
        Assert.AreEqual("old-component-snapshot", mapped.SubSku);
        Assert.AreEqual("当前中文描述", mapped.Description);
        Assert.AreEqual("当前中文配置", mapped.Features);
        Assert.AreEqual("中文饰面", mapped.Finish);
        Assert.AreEqual("current-image", mapped.MainImage);
        Assert.AreEqual(2, mapped.Numerator);
        Assert.AreEqual(3, mapped.Denominator);
        Assert.AreEqual("note", mapped.Note);
        Assert.AreEqual(first.Version, mapped.Version);
        Assert.AreEqual("__pgtest_bom_query__", mapped.CreatedBy);
        Assert.IsNotNull(mapped.CreatedDate);
        foreach (var uid in new[] { deletedReference.Uid, missingReference.Uid, nullComponent.Uid })
        {
            var row = dto.Single(item => item.Uid == uid);
            Assert.IsNull(row.Description);
            Assert.IsNull(row.Features);
            Assert.IsNull(row.MainImage);
            Assert.IsNull(row.Finish);
        }
        Assert.IsNull(dto.Single(row => row.Uid == hiddenFinish.Uid).Finish);
        var entities = (await repository.GetBomByBomNoAsync(prefix.ToLowerInvariant() + "bom")).ToArray();
        Assert.HasCount(6, entities);
        Assert.IsNull(entities.Single(row => row.Uid == nullComponent.Uid).ComponentProductId);
        Assert.IsTrue(entities.All(row => row.BomVersion > 0 && row.ParentProductId == parent.Uid));
        Assert.AreEqual(1, entities.Single(row => row.Uid == otherPositive.Uid).BomVersion);
        Assert.AreEqual(first.ParentSkuSnapshot, entities.Single(row => row.Uid == first.Uid).ParentSkuSnapshot);
        var page = await repository.GetPagedAsync(new() { Keyword = prefix });
        Assert.AreEqual(6, page.TotalCount);
        Assert.HasCount(6, page.Items);
        Assert.AreEqual("missing-child-snapshot", page.Items.Single(row => row.Uid == missingReference.Uid).SubSku);
    });

    /// <summary>指定标记兼容读取独立于父存在性；有效存在要求真实共享父，不能激活孤立旧组。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlBomQueryIntegration")]
    public Task VersionAndExists_KeepHistoricalAndParentIndependentScopes() => InRollback(async context =>
    {
        var prefix = Prefix();
        var parent = ProductRow(prefix + "Parent", prefix + "Bom", 2);
        var deletedParent = ProductRow(prefix + "DeletedParent", prefix + "DeletedBom", 3);
        deletedParent.IsDeleted = true;
        await Insert(context, parent);
        await Insert(context, deletedParent);
        var history = Line(parent, Guid.NewGuid(), prefix + "HistoryComponent ", 1, 1);
        var deleted = Line(parent, Guid.NewGuid(), prefix + "DeletedComponent", 1, 2);
        deleted.IsDeleted = true;
        var deletedParentLine = Line(deletedParent, Guid.NewGuid(), prefix + "DeletedParentChild", 3, 1);
        var orphan = Line(parent, Guid.NewGuid(), prefix + "OrphanChild", 7, 1);
        orphan.ParentProductId = Guid.NewGuid();
        orphan.BomNo = prefix + "OrphanBom";
        var nullParent = Line(parent, Guid.NewGuid(), prefix + "NullParentChild", 8, 1);
        nullParent.ParentProductId = null;
        nullParent.BomNo = prefix + "NullParentBom";
        var mismatch = Line(parent, Guid.NewGuid(), prefix + "MismatchChild", 2, 2);
        mismatch.BomNo = prefix + "WrongBom";
        foreach (var row in new[] { history, deleted, deletedParentLine, orphan, nullParent, mismatch }) await Insert(context, row);
        var repository = new PostgreSqlBomRepository(context);
        Assert.AreEqual(history.Uid, (await repository.GetByBomNoAndVersionAsync(" " + prefix.ToLowerInvariant() + "bom ", 1)).Single().Uid);
        Assert.AreEqual(history.Uid, (await repository.GetBomByBomNoAsync(parent.BomNo!)).Single().Uid);
        Assert.HasCount(0, (await repository.GetBomDtoByBomNoAsync(deletedParent.BomNo!)).ToArray());
        Assert.AreEqual(deletedParentLine.Uid, (await repository.GetByBomNoAndVersionAsync(deletedParent.BomNo!, 3)).Single().Uid);
        Assert.AreEqual(orphan.Uid, (await repository.GetByBomNoAndVersionAsync(orphan.BomNo, 7)).Single().Uid);
        Assert.IsNull((await repository.GetByBomNoAndVersionAsync(nullParent.BomNo, 8)).Single().ParentProductId);
        Assert.IsFalse(await repository.HasActiveBomForProductAsync(Guid.Empty, nullParent.BomNo));
        Assert.HasCount(0, (await repository.GetBomByBomNoAsync(mismatch.BomNo)).ToArray());
        Assert.IsTrue(await repository.HasActiveBomForProductAsync(parent.Uid));
        Assert.IsTrue(await repository.HasActiveBomForProductAsync(Guid.Empty, " " + parent.BomNo!.ToLowerInvariant() + " "));
        Assert.IsTrue(await repository.HasActiveBomForProductAsync(Guid.NewGuid(), parent.BomNo));
        Assert.IsFalse(await repository.HasActiveBomForProductAsync(deletedParent.Uid));
        Assert.IsFalse(await repository.HasActiveBomForProductAsync(Guid.Empty, deletedParent.BomNo));
        Assert.IsFalse(await repository.HasActiveBomForProductAsync(orphan.ParentProductId!.Value));
        Assert.IsFalse(await repository.HasActiveBomForProductAsync(Guid.Empty, orphan.BomNo));
        Assert.IsFalse(await repository.HasActiveBomForProductAsync(Guid.Empty, " "));
        Assert.IsFalse(await repository.HasActiveBomForProductAsync(Guid.NewGuid()));
        Assert.IsTrue(await repository.IsBomItemExistAsync(parent.BomNo!.ToLowerInvariant(), (prefix + "historycomponent").ToLowerInvariant()));
        Assert.IsTrue(await repository.IsBomItemExistAsync(parent.BomNo!, deleted.ComponentSkuSnapshot));
        Assert.IsFalse(await repository.IsBomItemExistAsync(" " + parent.BomNo, history.ComponentSkuSnapshot));
        Assert.IsFalse(await repository.IsBomItemExistAsync(parent.BomNo!, " " + history.ComponentSkuSnapshot));
        Assert.HasCount(0, await repository.GetByBomNoAndVersionAsync(parent.BomNo!, 0));
        Assert.HasCount(0, await repository.GetByBomNoAndVersionAsync(" ", 1));
        Assert.AreEqual(1, (await repository.GetPagedAsync(new() { Keyword = prefix })).TotalCount);
    });

    /// <summary>存在查询有且仅有软删历史时给出不同答案，不因同库既有活跃数据造成假阳性。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlBomQueryIntegration")]
    public Task Exists_WhenOnlySoftDeletedHistoryRemainsDiffersByEntry() => InRollback(async context =>
    {
        var prefix = Prefix();
        var parent = ProductRow(prefix, prefix + "Bom", 2);
        await Insert(context, parent);
        var row = Line(parent, Guid.NewGuid(), prefix + "Child", 1, 1);
        row.IsDeleted = true;
        await Insert(context, row);
        foreach (var version in new[] { 2, 3 })
        {
            var another = Line(parent, Guid.NewGuid(), row.ComponentSkuSnapshot, version, 2);
            another.IsDeleted = true;
            await Insert(context, another);
        }
        var repository = new PostgreSqlBomRepository(context);
        Assert.IsFalse(await repository.HasActiveBomForProductAsync(parent.Uid, parent.BomNo));
        Assert.IsTrue(await repository.IsBomItemExistAsync(row.BomNo, row.ComponentSkuSnapshot));
        Assert.HasCount(0, await repository.GetByBomNoAndVersionAsync(row.BomNo, 1));
        Assert.HasCount(0, (await repository.GetBomByBomNoAsync(row.BomNo)).ToArray());
    });

    /// <summary>分页原三字段批量词与十字段关键词逐项命中，转义和三百条上限生效，同编号行号按 UID 稳定分页。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlBomQueryIntegration")]
    public Task Paging_SearchFieldsEscapingBatchCapAndStableOrder() => InRollback(async context =>
    {
        var prefix = Prefix();
        var parent = ProductRow(prefix + "Parent", prefix + "Bom", 1);
        // 原文本唯一索引只按存储编号区分；等价编号变体允许相同行号，用于稳定排序验收，不修改索引。
        var otherParent = ProductRow(prefix + "OtherParent", prefix.ToLowerInvariant() + "bom ", 1);
        var child = ProductRow(prefix + "Child");
        foreach (var product in new[] { parent, otherParent, child }) await Insert(context, product);
        var first = Line(parent, child.Uid, prefix + "First", 1, 1);
        var second = Line(otherParent, child.Uid, prefix + "Second", 1, 1);
        await Insert(context, first);
        await Insert(context, second);
        var repository = new PostgreSqlBomRepository(context);
        var page = await repository.GetPagedAsync(new() { Keyword = prefix.ToLowerInvariant(), PageIndex = 0, PageSize = 999 });
        Assert.AreEqual(2, page.TotalCount);
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(500, page.PageSize);
        var ordered = new[] { first.Uid, second.Uid }.Order().ToArray();
        CollectionAssert.AreEqual(ordered, page.Items.Select(row => row.Uid).ToArray());
        Assert.AreEqual(ordered[1], (await repository.GetPagedAsync(new() { Keyword = prefix, PageIndex = 2, PageSize = 1 })).Items.Single().Uid);
        Assert.HasCount(0, (await repository.GetPagedAsync(new() { Keyword = prefix, PageIndex = 3, PageSize = 1 })).Items);
        Assert.AreEqual(20, (await repository.GetPagedAsync(new() { Keyword = prefix, PageSize = 0 })).PageSize);
        foreach (var keyword in new[] { parent.BomNo!, first.ParentSkuSnapshot, first.ComponentSkuSnapshot })
            Assert.IsTrue((await repository.GetPagedAsync(new() { BatchKeywords = [" " + keyword.ToLowerInvariant() + " "] })).Items.Any(row => row.Uid == first.Uid));
        Assert.AreEqual(first.Uid, (await repository.GetPagedAsync(new() { Keyword = first.ParentSkuSnapshot.ToLowerInvariant() })).Items.Single().Uid);
        Assert.AreEqual(first.Uid, (await repository.GetPagedAsync(new() { Keyword = first.ComponentSkuSnapshot.ToLowerInvariant() })).Items.Single().Uid);
        var overLimit = Enumerable.Range(0, 300).Select(index => prefix + "Absent" + index).Append(first.ComponentSkuSnapshot).ToList();
        Assert.AreEqual(0, (await repository.GetPagedAsync(new() { BatchKeywords = overLimit })).TotalCount);
        overLimit[299] = first.ComponentSkuSnapshot;
        Assert.AreEqual(first.Uid, (await repository.GetPagedAsync(new() { BatchKeywords = overLimit })).Items.Single().Uid);
        var literal = prefix + "~%_[\\ X1Z";
        var literalRow = Line(parent, child.Uid, literal, 1, 2);
        await Insert(context, literalRow);
        Assert.AreEqual(literalRow.Uid, (await repository.GetPagedAsync(new() { Keyword = prefix + "~%_[\\ X?Z" })).Items.Single().Uid);
        // 每个实时描述、配置和颜色字段独立建随机目标，避免 OR 其他列命中掩盖漏实现。
        foreach (var field in new[] { "DescriptionEn", "DescriptionZh", "FeaturesEn", "FeaturesZh", "ColorCode", "ColorNameEn", "ColorNameZh" })
        {
            var keyword = Prefix();
            var uniqueChild = ProductRow(Prefix());
            if (field.StartsWith("Color", StringComparison.Ordinal))
            {
                var color = new ProductColor { ColorCodeUnique = Prefix(), ColorCode = Prefix() };
                typeof(ProductColor).GetProperty(field)!.SetValue(color, keyword);
                await Insert(context, color);
                uniqueChild.ColorId = color.Uid;
            }
            else typeof(Product).GetProperty(field)!.SetValue(uniqueChild, keyword);
            await Insert(context, uniqueChild);
            var uniqueLine = Line(parent, uniqueChild.Uid, Prefix(), 1, 10 + Array.IndexOf(new[] { "DescriptionEn", "DescriptionZh", "FeaturesEn", "FeaturesZh", "ColorCode", "ColorNameEn", "ColorNameZh" }, field));
            await Insert(context, uniqueLine);
            Assert.AreEqual(uniqueLine.Uid, (await repository.GetPagedAsync(new() { Keyword = keyword.ToLowerInvariant() })).Items.Single().Uid);
        }
    });

    /// <summary>按共享父或组件读取有效引用；不同正标记和悬空来源保留，软删排除，数据库负责编号等值。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlBomQueryIntegration")]
    public Task RelatedReferences_AreCurrentAndUseDatabaseEquality() => InRollback(async context =>
    {
        var prefix = Prefix();
        var parent = ProductRow(prefix + "Parent", prefix + "Bom", 2);
        var otherParent = ProductRow(prefix + "Other", prefix + "OtherBom", 1);
        var child = ProductRow(prefix + "Child");
        foreach (var product in new[] { parent, otherParent, child }) await Insert(context, product);
        var current = Line(parent, child.Uid, "current-snapshot", 2, 1);
        // C# OrdinalIgnoreCase 不把全角英文字母视为等值；此处专门验证数据库当前引用不能被应用层漏掉。
        current.BomNo = parent.BomNo!.Replace('P', 'Ｐ');
        var history = Line(parent, Guid.NewGuid(), "history-snapshot", 1, 1);
        var componentReference = Line(otherParent, child.Uid, "other-snapshot", 1, 1);
        var unrelated = Line(otherParent, Guid.NewGuid(), "unrelated", 1, 2);
        var deleted = Line(parent, child.Uid, "deleted", 2, 2);
        deleted.IsDeleted = true;
        var orphan = Line(otherParent, child.Uid, "orphan-snapshot", 1, 3);
        orphan.ParentProductId = Guid.NewGuid();
        foreach (var line in new[] { current, history, componentReference, unrelated, deleted, orphan }) await Insert(context, line);
        var repository = new PostgreSqlBomRepository(context);
        var rows = await repository.GetCurrentReferencesForProductsAsync([parent.Uid, child.Uid, parent.Uid, Guid.Empty]);
        CollectionAssert.AreEquivalent(new[] { current.Uid, history.Uid, componentReference.Uid, orphan.Uid }, rows.Select(row => row.Uid).ToArray());
        Assert.AreEqual(2, rows.Single(row => row.Uid == current.Uid).BomVersion);
        Assert.AreEqual("current-snapshot", rows.Single(row => row.Uid == current.Uid).ComponentSkuSnapshot);
        Assert.HasCount(0, await repository.GetCurrentReferencesForProductsAsync([Guid.NewGuid()]));
        // 显式标记兼容入口仍按指定数值筛选，不作为当前组成资格。
        Assert.AreEqual(history.Uid, (await repository.GetByBomNoAndVersionAsync(parent.BomNo, 1)).Single().Uid);
    });

    /// <summary>集合查询保留历史及软删事实、存储编号精确分组；等价或重复输入不放大行，前导空格不被裁去。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlBomQueryIntegration")]
    public Task CollectionQuery_PreservesHistoryAndRawGroups() => InRollback(async context =>
    {
        var code = Prefix();
        var parent = ProductRow(Prefix(), code, 2);
        // 集合查询没有父有效性联接；有意不插父产品以验证悬空历史也保留。
        var history = Line(parent, Guid.NewGuid(), "history", 1, 1);
        var deleted = Line(parent, Guid.NewGuid(), "deleted", 2, 2);
        deleted.IsDeleted = true;
        var equivalent = Line(parent, Guid.NewGuid(), "equivalent", 2, 3);
        equivalent.BomNo = code.Replace('P', 'Ｐ') + " ";
        var leading = Line(parent, Guid.NewGuid(), "leading", 2, 4);
        leading.BomNo = " " + code;
        foreach (var row in new[] { history, deleted, equivalent, leading }) await Insert(context, row);
        var repository = new PostgreSqlBomRepository(context);
        var groups = await repository.GetBomsCollectionByBomNoListAsync([code, code.ToLowerInvariant(), equivalent.BomNo, code]);
        Assert.HasCount(2, groups);
        CollectionAssert.AreEquivalent(new[] { history.Uid, deleted.Uid, equivalent.Uid }, groups.Values.SelectMany(rows => rows).Select(row => row.Uid).ToArray());
        Assert.IsTrue(groups[code].Single(row => row.Uid == deleted.Uid).IsDeleted);
        Assert.AreEqual(1, groups[code].Single(row => row.Uid == history.Uid).BomVersion);
        Assert.AreEqual(equivalent.Uid, groups[equivalent.BomNo].Single().Uid);
        var leadingOnly = await repository.GetBomsCollectionByBomNoListAsync([leading.BomNo]);
        Assert.AreEqual(leading.Uid, leadingOnly.Values.Single().Single().Uid);
    });

    /// <summary>共享产品不放大物理组成；兼容来源产品失效、换号或缺失时，只要仍有引用者就可读取。</summary>
    [TestMethod]
    [DataRow("deleted")]
    [DataRow("changed")]
    [DataRow("missing")]
    [TestCategory("PostgreSqlBomQueryIntegration")]
    public Task SharedCurrentQueries_DoNotDependOnSourceParent(string sourceState) => InRollback(async context =>
    {
        var code = Prefix();
        var source = ProductRow(Prefix(), code, 1);
        var first = ProductRow(Prefix(), code.ToLowerInvariant() + " ", 2);
        var second = ProductRow(Prefix(), code.Replace('P', 'Ｐ'), 7);
        second.IsActive = false; // 停用不抹掉仍存在的共享组成。
        var child = ProductRow(Prefix());
        var line = Line(source, child.Uid, "shared-snapshot", 1, 1);
        if (sourceState == "deleted") source.IsDeleted = true;
        if (sourceState == "changed") source.BomNo = Prefix();
        foreach (var product in new[] { first, second, child }) await Insert(context, product);
        if (sourceState != "missing") await Insert(context, source);
        await Insert(context, line);
        var repository = new PostgreSqlBomRepository(context);
        var entity = (await repository.GetBomByBomNoAsync(code)).Single();
        Assert.AreEqual(line.Uid, entity.Uid);
        Assert.AreEqual(source.Uid, entity.ParentProductId, "普通读取不得重写兼容来源身份。");
        Assert.AreEqual(line.Uid, (await repository.GetBomDtoByBomNoAsync(code)).Single().Uid);
        var page = await repository.GetPagedAsync(new() { BatchKeywords = [code] });
        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual(line.Uid, page.Items.Single().Uid);
        Assert.IsTrue(await repository.HasActiveBomForProductAsync(first.Uid));
        Assert.IsTrue(await repository.HasActiveBomForProductAsync(second.Uid));
        Assert.IsFalse(await repository.HasActiveBomForProductAsync(source.Uid));
        var references = await repository.GetCurrentReferencesForProductsAsync([first.Uid, second.Uid, child.Uid]);
        Assert.AreEqual(line.Uid, references.Single().Uid, "多个共享父或组件命中仍只返回一个物理 UID。");
        // 仅修改本场景插入的两个引用者；退出最后一个引用后普通当前读取应为空，物理行仍保留。
        Assert.AreEqual(2, await context.Session.Connection.ExecuteAsync(
            "UPDATE public.product SET is_deleted=TRUE WHERE uid=ANY(@Ids)",
            new { Ids = new[] { first.Uid, second.Uid } }, context.Session.Transaction));
        Assert.HasCount(0, (await repository.GetBomByBomNoAsync(code)).ToArray());
        Assert.HasCount(0, (await repository.GetBomDtoByBomNoAsync(code)).ToArray());
        Assert.AreEqual(0, (await repository.GetPagedAsync(new() { BatchKeywords = [code] })).TotalCount);
        Assert.IsFalse(await repository.HasActiveBomForProductAsync(Guid.Empty, code));
        Assert.HasCount(0, await repository.GetCurrentReferencesForProductsAsync([first.Uid, second.Uid, child.Uid]));
        Assert.AreEqual(line.Uid, (await repository.GetByBomNoAndVersionAsync(code, 1)).Single().Uid);
    });

    /// <summary>共享保存事实按数据库等值归组，物理行不随引用者倍增；异常标记、空组成和失效来源均显式保留。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlBomQueryIntegration")]
    public Task SharedSaveFacts_GroupDatabaseEquivalentNumbersWithoutDuplicatingLines() => InRollback(async context =>
    {
        var code = Prefix();
        var source = ProductRow(Prefix(), code, 1);
        var first = ProductRow(Prefix(), code.ToLowerInvariant() + " ", 1);
        var second = ProductRow(Prefix(), code.Replace('P', 'Ｐ'), 3);
        second.IsActive = false;
        var excluded = ProductRow(Prefix(), code, 0);
        var deleted = ProductRow(Prefix(), code, 1);
        deleted.IsDeleted = true;
        var empty = ProductRow(Prefix(), Prefix(), 1);
        var child = ProductRow(Prefix());
        foreach (var row in new[] { first, second, excluded, deleted, empty, child }) await Insert(context, row);
        var line = Line(source, child.Uid, "shared-fact", 1, 1);
        var anomaly = Line(source, child.Uid, "anomaly", 2, 2);
        var removed = Line(source, child.Uid, "removed", 1, 3);
        removed.IsDeleted = true;
        foreach (var row in new[] { line, anomaly, removed }) await Insert(context, row);
        var repository = new PostgreSqlBomRepository(context);
        var facts = await repository.GetSharedSaveFactsAsync([code, second.BomNo!, code.ToLowerInvariant(), " " + code, empty.BomNo!]);
        var group = facts.Single(item => item.InputIndexes.Contains(0));
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, group.InputIndexes.ToArray());
        CollectionAssert.AreEquivalent(new[] { first.Uid, second.Uid }, group.Products.Select(item => item.Uid).ToArray());
        CollectionAssert.AreEquivalent(new[] { line.Uid, anomaly.Uid }, group.Lines.Select(item => item.Uid).ToArray());
        Assert.AreEqual(source.Uid, group.Lines.Single(item => item.Uid == line.Uid).ParentProductId);
        Assert.AreEqual("anomaly", group.Lines.Single(item => item.Uid == anomaly.Uid).ComponentSkuSnapshot);
        Assert.AreEqual(2, group.Lines.Single(item => item.Uid == anomaly.Uid).BomVersion);
        Assert.IsFalse(group.Products.Single(item => item.Uid == second.Uid).IsActive);
        Assert.AreEqual(3, group.Products.Single(item => item.Uid == second.Uid).BomVersion);
        var leading = facts.Single(item => item.InputIndexes.Contains(3));
        Assert.AreNotEqual(group.GroupKey, leading.GroupKey);
        Assert.HasCount(0, leading.Lines);
        Assert.HasCount(0, leading.Products);
        var emptyGroup = facts.Single(item => item.InputIndexes.Contains(4));
        Assert.HasCount(0, emptyGroup.Lines);
        Assert.AreEqual(empty.Uid, emptyGroup.Products.Single().Uid);
        var withoutRequests = await repository.GetSharedSaveFactsAsync([]);
        Assert.IsTrue(withoutRequests.Any(item => item.Lines.Any(row => row.Uid == line.Uid)));
        Assert.IsTrue(withoutRequests.All(item => item.InputIndexes.Count == 0));
    });

    /// <summary>每个场景只写随机实体并在调用方事务回滚；没有提交、DDL 或现存资料写操作。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        var user = new TestUser();
        try { await scenario(new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>实体全字段经现有 PostgreSQL 元数据和参数绑定插入，审计时间不依赖数据库隐式默认值。</summary>
    private static async Task Insert(RepositoryBaseContext context, BaseEntity entity)
    {
        context.AuditStamp.StampForInsert(entity);
        var metadata = PostgreSqlEntityMetadata.GetMetadata(entity.GetType());
        var columns = string.Join(",", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
        var values = string.Join(",", metadata.Columns.Select(column => "@" + column.ParameterName));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(
            $"INSERT INTO public.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({values})",
            PostgreSqlParameters.Entity(entity, metadata), context.Session.Transaction));
    }

    /// <summary>随机产品保持基础必填属性，BOM 标识和当前版本由场景明确给出。</summary>
    private static Product ProductRow(string sku, string? bomNo = null, int version = 0) => new()
    { Uid = Guid.NewGuid(), Sku = sku, SupplyType = "采购", BomNo = bomNo, BomVersion = version };

    /// <summary>组成行独立保存父子身份及编码快照，缺失子身份也是只读查询边界夹具。</summary>
    private static Bom Line(Product parent, Guid component, string snapshot, int version, int number) => new()
    { Uid = Guid.NewGuid(), BomNo = parent.BomNo!, BomVersion = version, ParentProductId = parent.Uid,
        ParentSkuSnapshot = parent.Sku, ComponentProductId = component, ComponentSkuSnapshot = snapshot, LineNumber = number, Numerator = 1, Denominator = 1 };

    /// <summary>短随机前缀避免查询命中保留库的现有资料。</summary>
    private static string Prefix() => "PGBQ" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>仅提供测试审计身份。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_bom_query__";
        public bool IsAuthenticated => false;
    }
}
