using Dapper;
using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;

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
        var remove = new SaveProductAttributeDefinitionsRequest(); ProductAttributePostgreSqlFixture.TouchGroup(remove, group);
        if (kind == "Option") remove.OptionChanges.DeleteItems.Add(new() { Uid = group.Options[0].Uid, Version = group.Options[0].Version });
        else
        {
            var relation = (await setup.Definitions.GetDefinitionsAsync(fixture.Category.Uid)).Data.CategoryRelations.Single();
            remove.CategoryChanges.DeleteItems.Add(new() { Uid = relation.Uid, Version = relation.Version });
        }
        await using var owner = new NpgsqlConnection(fixture.ConnectionString); await owner.OpenAsync();
        await using var ownerTransaction = await owner.BeginTransactionAsync();
        // 关联用例先分类再组；纯选项用例只取组更新锁，与正式用例的稳定顺序一致。
        if (kind == "Relation")
            await owner.ExecuteAsync("SELECT uid FROM public.product_category WHERE uid=@Id FOR UPDATE", new { Id = fixture.Category.Uid }, ownerTransaction);
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

    /// <summary>移动祖先路径与新增继承选择排队后重新检查树，不能留下不再适用的选择。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CategoryMoveAndInheritedSelection_AreSerialized(bool movementFirst)
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var setup = fixture.NewScope(); var group = await fixture.DefineAsync(setup);
        var child = await fixture.AddCategoryAsync(setup, "圆形顶喷", fixture.Category.Uid);
        var initialize = await fixture.DetailRequestAsync(setup); initialize.Master.CategoryId = child.Uid;
        Assert.IsTrue((await setup.Maintenance.SaveDetailAsync(initialize)).Success);
        var saveScope = fixture.NewScope(); var moveScope = fixture.NewScope();
        var selection = await fixture.DetailRequestAsync(saveScope); selection.SelectedOptionIds = [group.Options[0].Uid];
        var dto = child.Adapt<ProductCategoryDto>(); dto.ParentId = fixture.OtherCategory.Uid;
        var update = new UpdateItemRequest<ProductCategoryDto> { Uid = child.Uid, Version = child.Version, Data = dto };
        await using var owner = new NpgsqlConnection(fixture.ConnectionString); await owner.OpenAsync();
        await using var ownerTransaction = await owner.BeginTransactionAsync();
        await owner.ExecuteAsync("SELECT uid FROM public.product_category WHERE uid=@Id FOR UPDATE", new { Id = fixture.Category.Uid }, ownerTransaction);
        Task<ServiceResult<SaveItemResult>> moveTask;
        Task<ServiceResult<SaveProductMasterDetailResponse>> saveTask;
        if (movementFirst)
        {
            moveTask = moveScope.Categories.UpdateAsync(update);
            await WaitForParentLockAsync(owner, ownerTransaction, moveScope);
            saveTask = saveScope.Maintenance.SaveDetailAsync(selection);
            await WaitForParentLockAsync(owner, ownerTransaction, saveScope);
        }
        else
        {
            saveTask = saveScope.Maintenance.SaveDetailAsync(selection);
            await WaitForParentLockAsync(owner, ownerTransaction, saveScope);
            moveTask = moveScope.Categories.UpdateAsync(update);
            await WaitForParentLockAsync(owner, ownerTransaction, moveScope);
        }
        await ownerTransaction.CommitAsync();
        var moved = await moveTask.WaitAsync(TimeSpan.FromSeconds(15)); var saved = await saveTask.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.AreEqual(movementFirst, moved.Success, moved.Message);
        Assert.AreEqual(!movementFirst, saved.Success, saved.Message);
        if (movementFirst)
            Assert.IsTrue(saved.Error.Code is ServiceErrorCodes.VersionConflict or ServiceErrorCodes.Validation, saved.Message);
        var actual = await setup.Query.GetDetailAsync(fixture.Master.Uid);
        Assert.HasCount(movementFirst ? 0 : 1, actual.Data.SelectedOptionIds);
        Assert.IsTrue(actual.Data.AttributeSelections.All(row => row.IsAvailable));
        Assert.IsNull(saveScope.Session.Transaction); Assert.IsNull(moveScope.Session.Transaction);
    }
}
