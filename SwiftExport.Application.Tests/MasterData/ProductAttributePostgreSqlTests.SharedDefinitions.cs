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

/// <summary>模板共享定义与分类独立边界的真实数据库场景，使用同一受保护专用库夹具。</summary>
public sealed partial class ProductAttributePostgreSqlTests
{
    /// <summary>共享组可直接关联多个模板，读取只返回指定模板的来源，不经过分类层级。</summary>
    [TestMethod]
    public async Task SharedGroup_IsDirectlyAvailableToMultipleProfiles()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var link = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        ProductAttributePostgreSqlFixture.TouchGroup(link, group);
        link.ProfileChanges.CreateItems.Add(new() { Data = new() { ProductAttributeProfileId = fixture.OtherProfile.Uid, AttributeGroupId = group.Uid } });
        var linked = await fixture.SaveAsync(scope, link); Assert.IsTrue(linked.Success, linked.Message);
        foreach (var profile in new[] { fixture.Profile.Uid, fixture.OtherProfile.Uid })
        {
            var direct = await scope.Definitions.GetDefinitionsAsync(profile);
            Assert.IsTrue(direct.Success, direct.Message); Assert.HasCount(1, direct.Data.Groups);
            Assert.AreEqual(group.Uid, direct.Data.Groups.Single().Uid); Assert.HasCount(2, direct.Data.Groups.Single().Options);
            Assert.HasCount(1, direct.Data.ProfileRelations);
            Assert.AreEqual(profile, direct.Data.ProfileRelations.Single().ProductAttributeProfileId);
        }
        var request = await fixture.DetailRequestAsync(scope);
        request.Master.ProductAttributeProfileId = fixture.OtherProfile.Uid; request.SelectedOptionIds = [group.Options[0].Uid];
        var selected = await scope.Maintenance.SaveDetailAsync(request);
        Assert.IsTrue(selected.Success, selected.Message);
        Assert.IsTrue(selected.Data.Detail.AttributeSelections.Single().IsAvailable);
        Assert.AreEqual(fixture.OtherProfile.Uid, selected.Data.Detail.AttributeSelections.Single().ProductAttributeProfileId);
    }

    /// <summary>全局读取包含尚未关联模板且没有选项的组；模板读取不误纳入未关联组。</summary>
    [TestMethod]
    public async Task GlobalSnapshot_IncludesUnassignedEmptyGroup()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope();
        var create = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        create.GroupChanges.CreateItems.Add(new() { Data = new() { ClientUid = Guid.NewGuid(), GroupCode = fixture.CodePrefix, GroupName = "未关联空组" } });
        var saved = await fixture.SaveAsync(scope, create); Assert.IsTrue(saved.Success, saved.Message);
        var uid = saved.Data.GroupResults.Items.Single().Uid;
        var global = await scope.Definitions.GetDefinitionsAsync(null); Assert.IsTrue(global.Success, global.Message);
        Assert.HasCount(0, global.Data.Groups.Single(row => row.Uid == uid).Options);
        Assert.IsFalse(global.Data.ProfileRelations.Any(row => row.AttributeGroupId == uid));
        var scoped = await scope.Definitions.GetDefinitionsAsync(fixture.Profile.Uid);
        Assert.IsTrue(scoped.Success, scoped.Message); Assert.HasCount(0, scoped.Data.Groups); Assert.HasCount(0, scoped.Data.ProfileRelations);
    }

    /// <summary>仅改子项也推进组聚合版本一次；缺失组版本或旧版本不能覆盖关联与选项。</summary>
    [TestMethod]
    public async Task ChildOnlyChanges_AdvanceAggregateVersionAndRejectStaleOrMissingTouch()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var options = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 }; ProductAttributePostgreSqlFixture.TouchGroup(options, group);
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
        var links = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 }; ProductAttributePostgreSqlFixture.TouchGroup(links, versionTwo);
        links.ProfileChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = group.Uid, ProductAttributeProfileId = fixture.OtherProfile.Uid } });
        var second = await fixture.SaveAsync(scope, links); Assert.IsTrue(second.Success, second.Message);
        Assert.AreEqual(3, second.Data.Definitions.Groups.Single(row => row.Uid == group.Uid).Version);
        var stale = await fixture.SaveAsync(scope, links);
        Assert.IsFalse(stale.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict, stale.Error.Code);
        var missing = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        missing.OptionChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = group.Uid, OptionCode = "BAD", OptionName = "不能落库" } });
        var absent = await fixture.SaveAsync(scope, missing);
        Assert.IsFalse(absent.Success); Assert.AreEqual(ServiceErrorCodes.Validation, absent.Error.Code);
        var actual = await scope.Definitions.GetDefinitionsAsync(fixture.Profile.Uid);
        Assert.AreEqual(3, actual.Data.Groups.Single().Version); Assert.HasCount(3, actual.Data.Groups.Single().Options);
        Assert.AreEqual(2, await scope.Session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product_attribute_profile_group WHERE attribute_group_id=@Id AND NOT is_deleted", new { Id = group.Uid }));
    }

    /// <summary>解除正在使用的模板关联整页回滚；先明确改用共享模板后可以解除旧关联。</summary>
    [TestMethod]
    public async Task RelationRemoval_RejectsLostApplicabilityUntilOwnerChangesProfile()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var detail = await fixture.DetailRequestAsync(scope); detail.SelectedOptionIds = [group.Options[0].Uid];
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success);
        var snapshot = await scope.Definitions.GetDefinitionsAsync(fixture.Profile.Uid);
        var direct = snapshot.Data.ProfileRelations.Single();
        var remove = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        ProductAttributePostgreSqlFixture.TouchGroup(remove, group);
        remove.GroupChanges.UpdateItems[0].Data.GroupName = "失败不能改名";
        remove.ProfileChanges.DeleteItems.Add(new() { Uid = direct.Uid, Version = direct.Version });
        var blocked = await fixture.SaveAsync(scope, remove);
        Assert.IsFalse(blocked.Success); StringAssert.Contains(blocked.Message, "1");
        var unchanged = await scope.Definitions.GetDefinitionsAsync(fixture.Profile.Uid);
        Assert.AreEqual(1, unchanged.Data.Groups.Single().Version); Assert.AreEqual("安装方式", unchanged.Data.Groups.Single().GroupName);
        Assert.AreEqual(direct.Uid, unchanged.Data.ProfileRelations.Single().Uid); Assert.IsNull(scope.Session.Transaction);
        var add = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        ProductAttributePostgreSqlFixture.TouchGroup(add, group);
        add.ProfileChanges.CreateItems.Add(new() { Data = new() { ProductAttributeProfileId = fixture.OtherProfile.Uid, AttributeGroupId = group.Uid } });
        var added = await fixture.SaveAsync(scope, add); Assert.IsTrue(added.Success, added.Message);
        // 仅给另一模板添加来源不能使旧模板选值合法；由详情明确换模板才允许解除。
        detail = await fixture.DetailRequestAsync(scope); detail.Master.ProductAttributeProfileId = fixture.OtherProfile.Uid;
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success);
        remove = new() { ContractVersion = 2 };
        ProductAttributePostgreSqlFixture.TouchGroup(remove, added.Data.Definitions.Groups.Single(row => row.Uid == group.Uid));
        remove.ProfileChanges.DeleteItems.Add(new() { Uid = direct.Uid, Version = direct.Version });
        var removed = await fixture.SaveAsync(scope, remove); Assert.IsTrue(removed.Success, removed.Message);
        Assert.HasCount(0, (await scope.Definitions.GetDefinitionsAsync(fixture.Profile.Uid)).Data.Groups);
        var preserved = await scope.Query.GetDetailAsync(fixture.Master.Uid);
        Assert.HasCount(1, preserved.Data.SelectedOptionIds); Assert.IsTrue(preserved.Data.AttributeSelections.Single().IsAvailable);
    }

    /// <summary>分类树单项和批量移动均不改变主型号模板及已选标签。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CategoryMove_DoesNotChangeTemplateSelections(bool batch)
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
            var saved = await scope.Categories.BatchSaveAsync(new ProductCategoryBatchSaveRequest { UpdateItems = [update] });
            Assert.IsTrue(saved.Success, saved.Message);
        }
        else
        {
            var saved = await scope.Categories.UpdateAsync(update); Assert.IsTrue(saved.Success, saved.Message);
        }
        Assert.AreEqual(fixture.OtherCategory.Uid, await scope.Session.Connection.ExecuteScalarAsync<Guid>("SELECT parent_id FROM public.product_category WHERE uid=@Id", new { Id = child.Uid }));
        Assert.AreEqual(2, await scope.Session.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.product_category WHERE uid=@Id", new { Id = child.Uid }));
        var current = await scope.Query.GetDetailAsync(fixture.Master.Uid);
        Assert.AreEqual(fixture.Profile.Uid, current.Data.Master.ProductAttributeProfileId);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, current.Data.SelectedOptionIds.ToArray());
        Assert.IsTrue(current.Data.AttributeSelections.Single().IsAvailable);
    }

    /// <summary>列表和快速归类改变分类时保留模板选值，导入不参与分类维护。</summary>
    [TestMethod]
    [DataRow("List")]
    [DataRow("Assign")]
    [DataRow("Import")]
    public async Task CategoryWriteEntrances_AllowSharedSelections(string entrance)
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var detail = await fixture.DetailRequestAsync(scope); detail.SelectedOptionIds = [group.Options[0].Uid];
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success); detail = await fixture.DetailRequestAsync(scope);
        if (entrance == "List")
        {
            var changed = detail.Master.Adapt<SwiftExport.Contracts.DTOs.Products.ProductMasters.ProductMasterWriteDto>(); changed.CategoryId = fixture.OtherCategory.Uid;
            var saved = await scope.Maintenance.BatchSaveAsync(new ProductMasterBatchSaveRequest { ContractVersion = 3, UpdateItems = [new() { Uid = fixture.Master.Uid, Version = detail.ExpectedVersion, Data = changed }] });
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
                DefaultPcsPerPackage = "1" }]);
            Assert.IsTrue(saved.Success, saved.Message); Assert.IsTrue(saved.Data.Items.Single().Success, saved.Data.Items.Single().Message);
        }
        var current = await scope.Query.GetDetailAsync(fixture.Master.Uid);
        Assert.AreEqual(entrance == "Import" ? fixture.Category.Uid : fixture.OtherCategory.Uid, current.Data.Master.CategoryId);
        CollectionAssert.AreEqual(new[] { group.Options[0].Uid }, current.Data.SelectedOptionIds.ToArray());
        Assert.IsTrue(current.Data.AttributeSelections.Single().IsAvailable);
    }

    /// <summary>共享组改单选检查所有关联模板中的产品，不能只检查最初模板。</summary>
    [TestMethod]
    public async Task SingleModeValidation_CoversAllSharingProfiles()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var link = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 }; ProductAttributePostgreSqlFixture.TouchGroup(link, group);
        link.ProfileChanges.CreateItems.Add(new() { Data = new() { ProductAttributeProfileId = fixture.OtherProfile.Uid, AttributeGroupId = group.Uid } });
        var linked = await fixture.SaveAsync(scope, link); Assert.IsTrue(linked.Success, linked.Message);
        var detail = await fixture.DetailRequestAsync(scope); detail.Master.ProductAttributeProfileId = fixture.OtherProfile.Uid; detail.SelectedOptionIds = group.Options.Select(row => row.Uid).ToList();
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success);
        var change = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 }; ProductAttributePostgreSqlFixture.TouchGroup(change, linked.Data.Definitions.Groups.Single(row => row.Uid == group.Uid));
        change.GroupChanges.UpdateItems[0].Data.SelectionMode = ProductAttributeSelectionMode.Single;
        var denied = await fixture.SaveAsync(scope, change); Assert.IsFalse(denied.Success);
        Assert.AreEqual(2, (await scope.Definitions.GetDefinitionsAsync(fixture.OtherProfile.Uid)).Data.Groups.Single().Version);
    }
}
