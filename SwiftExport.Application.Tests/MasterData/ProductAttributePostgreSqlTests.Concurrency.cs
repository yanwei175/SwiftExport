using Dapper;
using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.SysEnums.StaticEnums;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>两独立连接下的共享标签竞争；通过实际数据库阻塞确认排队顺序，不用固定延时推断。</summary>
public sealed partial class ProductAttributePostgreSqlTests
{
    /// <summary>选项删除与关联解除分别同新增选择串行；先破坏定义则选择失败，先选值则定义变更被引用阻止。</summary>
    [TestMethod]
    [DataRow("Option", true)]
    [DataRow("Option", false)]
    [DataRow("Relation", true)]
    [DataRow("Relation", false)]
    public async Task ChildDefinitionRemovalAndSelection_AreSerialized(string kind, bool removalFirst)
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var setup = fixture.NewScope(); var group = await fixture.DefineAsync(setup);
        var saveScope = fixture.NewScope(); var removeScope = fixture.NewScope();
        var request = await fixture.DetailRequestAsync(saveScope); request.SelectedOptionIds = [group.Options[0].Uid];
        var remove = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 }; ProductAttributePostgreSqlFixture.TouchGroup(remove, group);
        if (kind == "Option") remove.OptionChanges.DeleteItems.Add(new() { Uid = group.Options[0].Uid, Version = group.Options[0].Version });
        else
        {
            var relation = (await setup.Definitions.GetDefinitionsAsync(fixture.Profile.Uid)).Data.ProfileRelations.Single();
            remove.ProfileChanges.DeleteItems.Add(new() { Uid = relation.Uid, Version = relation.Version });
        }
        await using var owner = new NpgsqlConnection(fixture.ConnectionString); await owner.OpenAsync();
        await using var ownerTransaction = await owner.BeginTransactionAsync();
        // 用组更新锁作共同排队屏障；两个正式用例先取得各自模板共享锁，再在组锁排队。
        await owner.ExecuteAsync("SELECT uid FROM public.product_attribute_group WHERE uid=@Id FOR UPDATE", new { Id = group.Uid }, ownerTransaction);
        Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> removeTask;
        Task<ServiceResult<SaveProductMasterDetailResponse>> saveTask;
        if (removalFirst)
        {
            removeTask = fixture.SaveAsync(removeScope, remove);
            await WaitForParentLockAsync(owner, ownerTransaction, removeScope);
            saveTask = saveScope.Maintenance.SaveDetailAsync(request);
            await WaitForParentLockAsync(owner, ownerTransaction, saveScope);
        }
        else
        {
            saveTask = saveScope.Maintenance.SaveDetailAsync(request);
            await WaitForParentLockAsync(owner, ownerTransaction, saveScope);
            removeTask = fixture.SaveAsync(removeScope, remove);
            await WaitForParentLockAsync(owner, ownerTransaction, removeScope);
        }
        Assert.IsFalse(saveTask.IsCompleted); Assert.IsFalse(removeTask.IsCompleted);
        await ownerTransaction.CommitAsync();
        var removed = await removeTask.WaitAsync(TimeSpan.FromSeconds(15));
        var saved = await saveTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.AreEqual(removalFirst, removed.Success, removed.Message);
        Assert.AreEqual(!removalFirst, saved.Success, saved.Message);
        Assert.AreEqual(removalFirst ? ServiceErrorCodes.Validation : ServiceErrorCodes.BusinessBlocked,
            removalFirst ? saved.Error.Code : removed.Error.Code);
        var actual = await setup.Query.GetDetailAsync(fixture.Master.Uid);
        Assert.HasCount(removalFirst ? 0 : 1, actual.Data.SelectedOptionIds);
        Assert.IsTrue(actual.Data.AttributeSelections.All(row => row.IsAvailable));
        Assert.IsNull(saveScope.Session.Transaction); Assert.IsNull(removeScope.Session.Transaction);
    }

    /// <summary>换用共同模板与解除旧模板关联按组锁串行，不会提交失去适用性的选择。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ProfileChangeAndRelationRemoval_AreSerialized(bool removalFirst)
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var setup = fixture.NewScope(); var group = await fixture.DefineAsync(setup);
        var link = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        ProductAttributePostgreSqlFixture.TouchGroup(link, group);
        link.ProfileChanges.CreateItems.Add(new() { Data = new() { ProductAttributeProfileId = fixture.OtherProfile.Uid, AttributeGroupId = group.Uid } });
        var linked = await fixture.SaveAsync(setup, link); Assert.IsTrue(linked.Success, linked.Message);
        group = linked.Data.Definitions.Groups.Single(row => row.Uid == group.Uid);
        var initial = await fixture.DetailRequestAsync(setup); initial.SelectedOptionIds = [group.Options[0].Uid];
        Assert.IsTrue((await setup.Maintenance.SaveDetailAsync(initial)).Success);
        var saveScope = fixture.NewScope(); var removeScope = fixture.NewScope();
        var change = await fixture.DetailRequestAsync(saveScope); change.Master.ProductAttributeProfileId = fixture.OtherProfile.Uid;
        var relation = linked.Data.Definitions.ProfileRelations.Single(row => row.ProductAttributeProfileId == fixture.Profile.Uid);
        var remove = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        ProductAttributePostgreSqlFixture.TouchGroup(remove, group);
        remove.ProfileChanges.DeleteItems.Add(new() { Uid = relation.Uid, Version = relation.Version });
        await using var owner = new NpgsqlConnection(fixture.ConnectionString); await owner.OpenAsync();
        await using var transaction = await owner.BeginTransactionAsync();
        await owner.ExecuteAsync("SELECT uid FROM public.product_attribute_group WHERE uid=@Id FOR UPDATE", new { Id = group.Uid }, transaction);
        Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> removeTask;
        Task<ServiceResult<SaveProductMasterDetailResponse>> saveTask;
        if (removalFirst)
        {
            removeTask = fixture.SaveAsync(removeScope, remove);
            await WaitForParentLockAsync(owner, transaction, removeScope);
            saveTask = saveScope.Maintenance.SaveDetailAsync(change);
            await WaitForParentLockAsync(owner, transaction, saveScope);
        }
        else
        {
            saveTask = saveScope.Maintenance.SaveDetailAsync(change);
            await WaitForParentLockAsync(owner, transaction, saveScope);
            removeTask = fixture.SaveAsync(removeScope, remove);
            await WaitForParentLockAsync(owner, transaction, removeScope);
        }
        await transaction.CommitAsync();
        var removed = await removeTask.WaitAsync(TimeSpan.FromSeconds(15));
        var saved = await saveTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.IsTrue(saved.Success, saved.Message);
        Assert.AreEqual(!removalFirst, removed.Success, removed.Message);
        if (removalFirst) Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, removed.Error.Code);
        var actual = await setup.Query.GetDetailAsync(fixture.Master.Uid);
        Assert.AreEqual(fixture.OtherProfile.Uid, actual.Data.Master.ProductAttributeProfileId);
        Assert.HasCount(1, actual.Data.SelectedOptionIds);
        Assert.IsTrue(actual.Data.AttributeSelections.Single().IsAvailable);
        Assert.IsNull(saveScope.Session.Transaction); Assert.IsNull(removeScope.Session.Transaction);
    }
}
