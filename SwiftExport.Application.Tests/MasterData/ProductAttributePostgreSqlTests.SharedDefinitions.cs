using Dapper;
using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Import;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>共享定义、分类来源与继承边界的真实数据库场景，使用同一受保护专用库夹具。</summary>
public sealed partial class ProductAttributePostgreSqlTests
{
    /// <summary>共享组在多个分类复用；祖先、当前分类重复关联只产生一个组，来源关联仍完整保留。</summary>
    [TestMethod]
    public async Task SharedGroup_IsInheritedAndDeduplicatedWithSourceRelations()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var child = await fixture.AddCategoryAsync(scope, "圆形顶喷", fixture.Category.Uid);
        var grandchild = await fixture.AddCategoryAsync(scope, "工程圆形顶喷", child.Uid);
        var links = new SaveProductAttributeDefinitionsRequest();
        ProductAttributePostgreSqlFixture.TouchGroup(links, group);
        links.CategoryChanges.CreateItems.Add(new() { Data = new() { CategoryId = child.Uid, AttributeGroupId = group.Uid } });
        links.CategoryChanges.CreateItems.Add(new() { Data = new() { CategoryId = fixture.OtherCategory.Uid, AttributeGroupId = group.Uid } });
        var linked = await fixture.SaveAsync(scope, links); Assert.IsTrue(linked.Success, linked.Message);
        var inherited = await scope.Definitions.GetDefinitionsAsync(grandchild.Uid);
        Assert.IsTrue(inherited.Success, inherited.Message); Assert.HasCount(1, inherited.Data.Groups);
        Assert.AreEqual(group.Uid, inherited.Data.Groups.Single().Uid); Assert.HasCount(2, inherited.Data.Groups.Single().Options);
        CollectionAssert.AreEquivalent(new[] { fixture.Category.Uid, child.Uid }, inherited.Data.CategoryRelations.Select(row => row.CategoryId).ToArray());
        var shared = await scope.Definitions.GetDefinitionsAsync(fixture.OtherCategory.Uid);
        Assert.HasCount(1, shared.Data.Groups); Assert.AreEqual(group.Uid, shared.Data.Groups.Single().Uid);
        Assert.HasCount(1, shared.Data.CategoryRelations);
        var request = await fixture.DetailRequestAsync(scope);
        request.Master.CategoryId = grandchild.Uid; request.SelectedOptionIds = [group.Options[0].Uid];
        var selected = await scope.Maintenance.SaveDetailAsync(request);
        Assert.IsTrue(selected.Success, selected.Message);
        Assert.IsTrue(selected.Data.Detail.AttributeSelections.Single().IsAvailable);
        Assert.AreEqual(grandchild.Uid, selected.Data.Detail.AttributeSelections.Single().CategoryId);
    }

    /// <summary>全局读取包含尚未关联分类且没有选项的组；分类读取不误纳入未关联组。</summary>
    [TestMethod]
    public async Task GlobalSnapshot_IncludesUnassignedEmptyGroup()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope();
        var create = new SaveProductAttributeDefinitionsRequest();
        create.GroupChanges.CreateItems.Add(new() { Data = new() { ClientUid = Guid.NewGuid(), GroupCode = fixture.CodePrefix, GroupName = "未关联空组" } });
        var saved = await fixture.SaveAsync(scope, create); Assert.IsTrue(saved.Success, saved.Message);
        var uid = saved.Data.GroupResults.Items.Single().Uid;
        var global = await scope.Definitions.GetDefinitionsAsync(null); Assert.IsTrue(global.Success, global.Message);
        Assert.HasCount(0, global.Data.Groups.Single(row => row.Uid == uid).Options);
        Assert.IsFalse(global.Data.CategoryRelations.Any(row => row.AttributeGroupId == uid));
        var scoped = await scope.Definitions.GetDefinitionsAsync(fixture.Category.Uid);
        Assert.IsTrue(scoped.Success, scoped.Message); Assert.HasCount(0, scoped.Data.Groups); Assert.HasCount(0, scoped.Data.CategoryRelations);
    }

    /// <summary>仅改子项也推进组聚合版本一次；缺失组版本或旧版本不能覆盖关联与选项。</summary>
    [TestMethod]
    public async Task ChildOnlyChanges_AdvanceAggregateVersionAndRejectStaleOrMissingTouch()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var options = new SaveProductAttributeDefinitionsRequest(); ProductAttributePostgreSqlFixture.TouchGroup(options, group);
        options.OptionChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = group.Uid, OptionCode = "FLOOR", OptionName = "落地" } });
        options.OptionChanges.UpdateItems.Add(new() { Uid = group.Options[0].Uid, Version = group.Options[0].Version,
            Data = new() { AttributeGroupId = group.Uid, OptionCode = "TOP", OptionName = "吸顶" } });
        var first = await fixture.SaveAsync(scope, options); Assert.IsTrue(first.Success, first.Message);
        var versionTwo = first.Data.Definitions.Groups.Single(row => row.Uid == group.Uid);
        Assert.AreEqual(2, versionTwo.Version); Assert.AreEqual(1, first.Data.GroupResults.UpdateSuccessCount);
        Assert.HasCount(3, versionTwo.Options);
        Assert.AreEqual(1, first.Data.GroupResults.Items.Single().OldVersion);
        Assert.AreEqual(2, first.Data.GroupResults.Items.Single().NewVersion);
        Assert.AreEqual(1, first.Data.OptionResults.Items.Single(row => row.Operation == SwiftExport.Contracts.SysEnums.StaticEnums.SaveOperation.Update).OldVersion);
        Assert.AreEqual(2, first.Data.OptionResults.Items.Single(row => row.Operation == SwiftExport.Contracts.SysEnums.StaticEnums.SaveOperation.Update).NewVersion);
        var links = new SaveProductAttributeDefinitionsRequest(); ProductAttributePostgreSqlFixture.TouchGroup(links, versionTwo);
        links.CategoryChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = group.Uid, CategoryId = fixture.OtherCategory.Uid } });
        var second = await fixture.SaveAsync(scope, links); Assert.IsTrue(second.Success, second.Message);
        Assert.AreEqual(3, second.Data.Definitions.Groups.Single(row => row.Uid == group.Uid).Version);
        var stale = await fixture.SaveAsync(scope, links);
        Assert.IsFalse(stale.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict, stale.Error.Code);
        var missing = new SaveProductAttributeDefinitionsRequest();
        missing.OptionChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = group.Uid, OptionCode = "BAD", OptionName = "不能落库" } });
        var absent = await fixture.SaveAsync(scope, missing);
        Assert.IsFalse(absent.Success); Assert.AreEqual(ServiceErrorCodes.Validation, absent.Error.Code);
        var actual = await scope.Definitions.GetDefinitionsAsync(fixture.Category.Uid);
        Assert.AreEqual(3, actual.Data.Groups.Single().Version); Assert.HasCount(3, actual.Data.Groups.Single().Options);
        Assert.AreEqual(2, await scope.Session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product_category_attribute_group WHERE attribute_group_id=@Id AND NOT is_deleted", new { Id = group.Uid }));
    }

    /// <summary>最后适用来源解除会撤销整页，新增祖先来源后解除直接来源仍保留产品选择。</summary>
    [TestMethod]
    public async Task RelationRemoval_RejectsLostApplicabilityAndAllowsAlternateAncestorSource()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var detail = await fixture.DetailRequestAsync(scope); detail.SelectedOptionIds = [group.Options[0].Uid];
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success);
        var snapshot = await scope.Definitions.GetDefinitionsAsync(fixture.Category.Uid);
        var direct = snapshot.Data.CategoryRelations.Single();
        var remove = new SaveProductAttributeDefinitionsRequest(); ProductAttributePostgreSqlFixture.TouchGroup(remove, group);
        remove.GroupChanges.UpdateItems[0].Data.GroupName = "失败不能改名";
        remove.CategoryChanges.DeleteItems.Add(new() { Uid = direct.Uid, Version = direct.Version });
        var blocked = await fixture.SaveAsync(scope, remove);
        Assert.IsFalse(blocked.Success); StringAssert.Contains(blocked.Message, "1");
        var unchanged = await scope.Definitions.GetDefinitionsAsync(fixture.Category.Uid);
        Assert.AreEqual(1, unchanged.Data.Groups.Single().Version); Assert.AreEqual("安装方式", unchanged.Data.Groups.Single().GroupName);
        Assert.AreEqual(direct.Uid, unchanged.Data.CategoryRelations.Single().Uid); Assert.IsNull(scope.Session.Transaction);
        var parent = await fixture.AddCategoryAsync(scope, "花洒");
        var categoryDto = fixture.Category.Adapt<ProductCategoryDto>(); categoryDto.ParentId = parent.Uid;
        var moved = await scope.Categories.UpdateAsync(new() { Uid = fixture.Category.Uid, Version = fixture.Category.Version, Data = categoryDto });
        Assert.IsTrue(moved.Success, moved.Message);
        var add = new SaveProductAttributeDefinitionsRequest(); ProductAttributePostgreSqlFixture.TouchGroup(add, group);
        add.CategoryChanges.CreateItems.Add(new() { Data = new() { CategoryId = parent.Uid, AttributeGroupId = group.Uid } });
        var added = await fixture.SaveAsync(scope, add); Assert.IsTrue(added.Success, added.Message);
        remove = new(); ProductAttributePostgreSqlFixture.TouchGroup(remove, added.Data.Definitions.Groups.Single(row => row.Uid == group.Uid));
        remove.CategoryChanges.DeleteItems.Add(new() { Uid = direct.Uid, Version = direct.Version });
        var removed = await fixture.SaveAsync(scope, remove); Assert.IsTrue(removed.Success, removed.Message);
        var remaining = await scope.Definitions.GetDefinitionsAsync(fixture.Category.Uid);
        Assert.HasCount(1, remaining.Data.Groups); Assert.AreEqual(parent.Uid, remaining.Data.CategoryRelations.Single().CategoryId);
        var preserved = await scope.Query.GetDetailAsync(fixture.Master.Uid);
        Assert.HasCount(1, preserved.Data.SelectedOptionIds); Assert.IsTrue(preserved.Data.AttributeSelections.Single().IsAvailable);
    }

    /// <summary>树移动的单项与批量入口共同保护后代产品选择，存在新来源时才允许移动。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CategoryMove_ProtectsInheritedSelectionsAtEveryWriteEntrance(bool batch)
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var child = await fixture.AddCategoryAsync(scope, "圆形顶喷", fixture.Category.Uid);
        var descendant = await fixture.AddCategoryAsync(scope, "工程顶喷", child.Uid);
        var detail = await fixture.DetailRequestAsync(scope); detail.Master.CategoryId = descendant.Uid; detail.SelectedOptionIds = [group.Options[0].Uid];
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success);
        var dto = child.Adapt<ProductCategoryDto>(); dto.ParentId = fixture.OtherCategory.Uid;
        var update = new UpdateItemRequest<ProductCategoryDto> { Uid = child.Uid, Version = child.Version, Data = dto };
        if (batch)
        {
            var denied = await scope.Categories.BatchSaveAsync(new ProductCategoryBatchSaveRequest { UpdateItems = [update] });
            Assert.IsFalse(denied.Success); StringAssert.Contains(denied.Message, "1");
        }
        else
        {
            var denied = await scope.Categories.UpdateAsync(update);
            Assert.IsFalse(denied.Success); StringAssert.Contains(denied.Message, "1");
        }
        Assert.AreEqual(fixture.Category.Uid, await scope.Session.Connection.ExecuteScalarAsync<Guid>("SELECT parent_id FROM public.product_category WHERE uid=@Id", new { Id = child.Uid }));
        Assert.AreEqual(1, await scope.Session.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.product_category WHERE uid=@Id", new { Id = child.Uid }));
        var link = new SaveProductAttributeDefinitionsRequest(); ProductAttributePostgreSqlFixture.TouchGroup(link, group);
        link.CategoryChanges.CreateItems.Add(new() { Data = new() { CategoryId = fixture.OtherCategory.Uid, AttributeGroupId = group.Uid } });
        Assert.IsTrue((await fixture.SaveAsync(scope, link)).Success);
        var accepted = await scope.Categories.UpdateAsync(update); Assert.IsTrue(accepted.Success, accepted.Message);
        var saved = await scope.Query.GetDetailAsync(fixture.Master.Uid); Assert.IsTrue(saved.Data.AttributeSelections.Single().IsAvailable);
    }

    /// <summary>列表、快速归类和导入换分类可保留共同适用的值，不再一律拒绝带标签产品。</summary>
    [TestMethod]
    [DataRow("List")]
    [DataRow("Assign")]
    [DataRow("Import")]
    public async Task CategoryWriteEntrances_AllowSharedSelections(string entrance)
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var link = new SaveProductAttributeDefinitionsRequest(); ProductAttributePostgreSqlFixture.TouchGroup(link, group);
        link.CategoryChanges.CreateItems.Add(new() { Data = new() { CategoryId = fixture.OtherCategory.Uid, AttributeGroupId = group.Uid } });
        Assert.IsTrue((await fixture.SaveAsync(scope, link)).Success);
        var detail = await fixture.DetailRequestAsync(scope); detail.SelectedOptionIds = [group.Options[0].Uid];
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success); detail = await fixture.DetailRequestAsync(scope);
        if (entrance == "List")
        {
            var changed = detail.Master.Adapt<SwiftExport.Contracts.DTOs.Products.ProductMasters.ProductMasterWriteDto>(); changed.CategoryId = fixture.OtherCategory.Uid;
            var saved = await scope.Maintenance.BatchSaveAsync(new ProductMasterBatchSaveRequest { UpdateItems = [new() { Uid = fixture.Master.Uid, Version = detail.ExpectedVersion, Data = changed }] });
            Assert.IsTrue(saved.Success, saved.Message);
        }
        else if (entrance == "Assign")
        {
            var saved = await scope.Maintenance.AssignCategoryAsync(new() { TargetCategoryId = fixture.OtherCategory.Uid,
                Items = [new() { ProductMasterId = fixture.Master.Uid, ExpectedVersion = detail.ExpectedVersion }] });
            Assert.IsTrue(saved.Success, saved.Message);
        }
        else
        {
            var saved = await scope.Import.ImportAsync([new ProductMasterImportRowDto { SourceRowNumber = 2, MasterSku = fixture.Master.MasterSku,
                CategoryPath = fixture.OtherCategory.CategoryName, DefaultPcsPerPackage = "1" }]);
            Assert.IsTrue(saved.Success, saved.Message); Assert.IsTrue(saved.Data.Items.Single().Success, saved.Data.Items.Single().Message);
        }
        var current = await scope.Query.GetDetailAsync(fixture.Master.Uid);
        Assert.AreEqual(fixture.OtherCategory.Uid, current.Data.Master.CategoryId);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, current.Data.SelectedOptionIds.ToArray());
        Assert.IsTrue(current.Data.AttributeSelections.Single().IsAvailable);
    }

    /// <summary>共享组改单选要检查另一关联分类中的产品，不能只检查最初定义分类。</summary>
    [TestMethod]
    public async Task SingleModeValidation_CoversAllSharingCategories()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var link = new SaveProductAttributeDefinitionsRequest(); ProductAttributePostgreSqlFixture.TouchGroup(link, group);
        link.CategoryChanges.CreateItems.Add(new() { Data = new() { CategoryId = fixture.OtherCategory.Uid, AttributeGroupId = group.Uid } });
        var linked = await fixture.SaveAsync(scope, link); Assert.IsTrue(linked.Success, linked.Message);
        var detail = await fixture.DetailRequestAsync(scope); detail.Master.CategoryId = fixture.OtherCategory.Uid; detail.SelectedOptionIds = group.Options.Select(row => row.Uid).ToList();
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success);
        var change = new SaveProductAttributeDefinitionsRequest(); ProductAttributePostgreSqlFixture.TouchGroup(change, linked.Data.Definitions.Groups.Single(row => row.Uid == group.Uid));
        change.GroupChanges.UpdateItems[0].Data.SelectionMode = ProductAttributeSelectionMode.Single;
        var denied = await fixture.SaveAsync(scope, change); Assert.IsFalse(denied.Success);
        Assert.AreEqual(2, (await scope.Definitions.GetDefinitionsAsync(fixture.OtherCategory.Uid)).Data.Groups.Single().Version);
    }
}
