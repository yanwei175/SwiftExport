using Dapper;
using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Npgsql;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>产品标签的真实 PostgreSQL 用例验收；只在获批复制库运行，覆盖约束、原子性和分类父锁竞争。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductAttributePostgreSqlTests
{
    /// <summary>旧请求和明确清空必须在反序列化时保持不同含义。</summary>
    [TestMethod]
    public void SelectionProtocol_DistinguishesOmittedNullAndEmpty()
    {
        Assert.IsNull(JsonConvert.DeserializeObject<SaveProductMasterDetailRequest>("{}")!.SelectedOptionIds);
        Assert.IsNull(JsonConvert.DeserializeObject<SaveProductMasterDetailRequest>("{\"SelectedOptionIds\":null}")!.SelectedOptionIds);
        Assert.HasCount(0, JsonConvert.DeserializeObject<SaveProductMasterDetailRequest>("{\"SelectedOptionIds\":[]}")!.SelectedOptionIds);
    }

    /// <summary>同批创建、修改编码名称排序、组和值逐项版本、缓存空分类和空组均可读。</summary>
    [TestMethod]
    public async Task Definitions_CreateEditReadAndUnusedCascadeDelete()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var scope = fixture.NewScope();
        var empty = await scope.Definitions.GetDefinitionsAsync(fixture.Category.Uid);
        Assert.IsTrue(empty.Success); Assert.HasCount(0, empty.Data.Groups);
        var group = await fixture.DefineAsync(scope);
        var update = new SaveProductAttributeDefinitionsRequest();
        update.GroupChanges.UpdateItems.Add(new() { Uid = group.Uid, Version = group.Version,
            Data = new() { GroupCode = "INSTALLATION", GroupName = "安装方法", SelectionMode = ProductAttributeSelectionMode.Single, SortOrder = 3 } });
        var option = group.Options[0];
        update.OptionChanges.UpdateItems.Add(new() { Uid = option.Uid, Version = option.Version,
            Data = new() { AttributeGroupId = group.Uid, OptionCode = "TOP", OptionName = "吸顶安装", SortOrder = 4 } });
        var saved = await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, update);
        Assert.IsTrue(saved.Success, saved.Message);
        Assert.AreEqual(2, saved.Data.Definitions.Groups.Single().Version);
        Assert.AreEqual(2, saved.Data.Definitions.Groups.Single().Options.Single(row => row.Uid == option.Uid).Version);
        Assert.AreEqual("安装方法", saved.Data.Definitions.Groups.Single().GroupName);
        var all = await scope.Definitions.GetDefinitionsAsync(null);
        Assert.IsTrue(all.Success, all.Message); Assert.IsTrue(all.Data.Groups.Any(row => row.Uid == group.Uid));
        var deletion = new SaveProductAttributeDefinitionsRequest();
        deletion.GroupChanges.DeleteItems.Add(new() { Uid = group.Uid, Version = 2 });
        var deleted = await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, deletion);
        Assert.IsTrue(deleted.Success, deleted.Message); Assert.HasCount(0, deleted.Data.Definitions.Groups);
        Assert.AreEqual(2, await scope.Session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product_attribute_option WHERE attribute_group_id=@Id AND is_deleted=TRUE", new { Id = group.Uid }));
        // 已软删除编码可复用，组没有选项也必须出现在快照中。
        var recreate = new SaveProductAttributeDefinitionsRequest();
        recreate.GroupChanges.CreateItems.Add(new() { Data = new() { ClientUid = Guid.NewGuid(), GroupCode = "INSTALLATION", GroupName = "新空组" } });
        var recreated = await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, recreate);
        Assert.IsTrue(recreated.Success, recreated.Message); Assert.HasCount(0, recreated.Data.Definitions.Groups.Single().Options);
    }

    /// <summary>编码校验、错误归属和版本失败不会保存前序变更；真实编码索引负责额外比较语义。</summary>
    [TestMethod]
    [DataRow("DuplicateGroup")]
    [DataRow("DuplicateOption")]
    [DataRow("WrongGroup")]
    [DataRow("WrongCategory")]
    [DataRow("Version")]
    [DataRow("DatabaseRollback")]
    public async Task Definitions_InvalidChangesAreAtomic(string scenario)
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var scope = fixture.NewScope();
        var group = await fixture.DefineAsync(scope);
        var request = new SaveProductAttributeDefinitionsRequest();
        request.GroupChanges.CreateItems.Add(new() { Data = new() { ClientUid = Guid.NewGuid(), GroupCode = scenario == "DuplicateGroup" ? "install" : "SIZE", GroupName = "尺寸" } });
        if (scenario == "DuplicateOption") request.OptionChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = group.Uid, OptionCode = "ceiling", OptionName = "重复" } });
        if (scenario == "WrongGroup") request.OptionChanges.UpdateItems.Add(new() { Uid = group.Options[0].Uid, Version = 1,
            Data = new() { AttributeGroupId = Guid.NewGuid(), OptionCode = "TOP", OptionName = "非法迁移" } });
        if (scenario == "WrongCategory")
        {
            var other = await fixture.DefineAsync(scope, fixture.OtherCategory.Uid);
            request.GroupChanges.UpdateItems.Add(new() { Uid = other.Uid, Version = 1, Data = new() { GroupCode = "OTHER", GroupName = "另一分类的组" } });
        }
        if (scenario == "Version") request.GroupChanges.UpdateItems.Add(new() { Uid = group.Uid, Version = 2, Data = new() { GroupCode = "INSTALL", GroupName = "旧版本" } });
        if (scenario == "DatabaseRollback") request.GroupChanges.CreateItems.Add(new() { Data = new() { ClientUid = Guid.NewGuid(), GroupCode = "ＩＮＳＴＡＬＬ", GroupName = "数据库比较冲突" } });
        var result = await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, request);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(scenario == "Version" ? ServiceErrorCodes.VersionConflict : ServiceErrorCodes.Validation, result.Error.Code, result.Message);
        var snapshot = await scope.Definitions.GetDefinitionsAsync(fixture.Category.Uid);
        Assert.HasCount(1, snapshot.Data.Groups); Assert.AreEqual(group.Uid, snapshot.Data.Groups.Single().Uid);
        Assert.HasCount(2, snapshot.Data.Groups.Single().Options);
        Assert.IsNull(scope.Session.Transaction);
    }

    /// <summary>最终选择按 null 保持、空集清空、跨分类替换处理；标签也递增头部版本并保持旧字符串。</summary>
    [TestMethod]
    public async Task Detail_PreserveClearReplaceAndVersion()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var scope = fixture.NewScope();
        var group = await fixture.DefineAsync(scope);
        var request = await fixture.DetailRequestAsync(scope);
        request.SelectedOptionIds = group.Options.Select(row => row.Uid).ToList();
        var first = await scope.Maintenance.SaveDetailAsync(request);
        Assert.IsTrue(first.Success, first.Message); Assert.AreEqual(2, first.Data.Detail.Version);
        var page = await scope.Query.GetPagedAsync(new() { Keyword = fixture.Master.MasterSku });
        Assert.AreEqual("安装方式：吸顶、挂墙", page.Data.Items.Single().TagSummary);
        var preserved = await scope.Maintenance.SaveDetailAsync(await fixture.DetailRequestAsync(scope));
        Assert.IsTrue(preserved.Success, preserved.Message); Assert.HasCount(2, preserved.Data.Detail.SelectedOptionIds);
        Assert.AreEqual("保留旧标签", preserved.Data.Detail.Master.ProductTag);
        Assert.AreEqual("保留旧类型", preserved.Data.Detail.Master.ProductType);
        var other = await fixture.DefineAsync(scope, fixture.OtherCategory.Uid, ProductAttributeSelectionMode.Single);
        var wrong = await fixture.DetailRequestAsync(scope);
        wrong.Master.CategoryId = fixture.OtherCategory.Uid;
        var blocked = await scope.Maintenance.SaveDetailAsync(wrong);
        Assert.IsFalse(blocked.Success); Assert.AreEqual(ServiceErrorCodes.Validation, blocked.Error.Code);
        wrong.SelectedOptionIds = [other.Options[0].Uid];
        var replaced = await scope.Maintenance.SaveDetailAsync(wrong);
        Assert.IsTrue(replaced.Success, replaced.Message); CollectionAssert.AreEqual(wrong.SelectedOptionIds, replaced.Data.Detail.SelectedOptionIds);
        var clear = await fixture.DetailRequestAsync(scope); clear.SelectedOptionIds = [];
        var cleared = await scope.Maintenance.SaveDetailAsync(clear);
        Assert.IsTrue(cleared.Success, cleared.Message); Assert.HasCount(0, cleared.Data.Detail.SelectedOptionIds);
        var stale = await scope.Maintenance.SaveDetailAsync(clear);
        Assert.IsFalse(stale.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict, stale.Error.Code);
    }

    /// <summary>重复、失效、错误分类及单选超限均在写头部前拒绝。</summary>
    [TestMethod]
    [DataRow("Duplicate")]
    [DataRow("Missing")]
    [DataRow("Empty")]
    [DataRow("Deleted")]
    [DataRow("WrongCategory")]
    [DataRow("Single")]
    public async Task Detail_InvalidSelectionsPreserveHead(string scenario)
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var scope = fixture.NewScope();
        var group = await fixture.DefineAsync(scope, mode: scenario == "Single" ? ProductAttributeSelectionMode.Single : ProductAttributeSelectionMode.Multiple);
        var request = await fixture.DetailRequestAsync(scope);
        if (scenario == "Deleted")
        {
            var remove = new SaveProductAttributeDefinitionsRequest(); remove.OptionChanges.DeleteItems.Add(new() { Uid = group.Options[0].Uid, Version = 1 });
            Assert.IsTrue((await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, remove)).Success);
        }
        request.SelectedOptionIds = scenario switch
        {
            "Duplicate" => [group.Options[0].Uid, group.Options[0].Uid],
            "Missing" => [Guid.NewGuid()],
            "Empty" => [Guid.Empty],
            "Deleted" => [group.Options[0].Uid],
            "WrongCategory" => [(await fixture.DefineAsync(scope, fixture.OtherCategory.Uid)).Options[0].Uid],
            _ => group.Options.Select(row => row.Uid).ToList()
        };
        request.Master.DefaultDescriptionZh = "不能落库";
        var failed = await scope.Maintenance.SaveDetailAsync(request);
        Assert.IsFalse(failed.Success); Assert.AreEqual(ServiceErrorCodes.Validation, failed.Error.Code);
        var saved = await scope.Query.GetDetailAsync(fixture.Master.Uid);
        Assert.AreEqual(1, saved.Data.Version); Assert.IsNull(saved.Data.Master.DefaultDescriptionZh); Assert.HasCount(0, saved.Data.SelectedOptionIds);
    }

    /// <summary>两组及各组选项同次新增时，每个临时组身份都映射到各自服务端身份。</summary>
    [TestMethod]
    public async Task MultipleNewGroupsMapTheirOwnOptionsInOneCommit()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope();
        var request = new SaveProductAttributeDefinitionsRequest(); var installId = Guid.NewGuid(); var sizeId = Guid.NewGuid();
        request.GroupChanges.CreateItems.Add(new() { Data = new() { ClientUid = installId, GroupCode = "INSTALL", GroupName = "安装方式", SelectionMode = ProductAttributeSelectionMode.Multiple } });
        request.GroupChanges.CreateItems.Add(new() { Data = new() { ClientUid = sizeId, GroupCode = "SIZE", GroupName = "尺寸", SelectionMode = ProductAttributeSelectionMode.Single } });
        request.OptionChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = installId, OptionCode = "TOP", OptionName = "吸顶" } });
        request.OptionChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = installId, OptionCode = "WALL", OptionName = "挂墙" } });
        request.OptionChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = sizeId, OptionCode = "8", OptionName = "8寸" } });
        request.OptionChanges.CreateItems.Add(new() { Data = new() { AttributeGroupId = sizeId, OptionCode = "10", OptionName = "10寸" } });
        var saved = await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, request);
        Assert.IsTrue(saved.Success, saved.Message); Assert.HasCount(2, saved.Data.Definitions.Groups);
        Assert.AreEqual(2, saved.Data.GroupResults.SuccessCount); Assert.AreEqual(4, saved.Data.OptionResults.SuccessCount);
        foreach (var group in saved.Data.Definitions.Groups)
        {
            Assert.AreNotEqual(installId, group.Uid); Assert.AreNotEqual(sizeId, group.Uid); Assert.HasCount(2, group.Options);
            Assert.IsTrue(group.Options.All(option => option.AttributeGroupId == group.Uid));
            CollectionAssert.AreEquivalent(group.GroupCode == "INSTALL" ? new[] { "吸顶", "挂墙" } : new[] { "8寸", "10寸" }, group.Options.Select(option => option.OptionName).ToArray());
        }
    }

    /// <summary>首建主型号和标签共用一次提交，不要求先生成已有主型号。</summary>
    [TestMethod]
    public async Task Detail_NewMasterAndSelectionsSaveTogether()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var request = await fixture.DetailRequestAsync(scope); request.ProductMasterId = Guid.Empty; request.ExpectedVersion = 0;
        request.Master.MasterSku = "TAG-NEW-" + Guid.NewGuid().ToString("N"); request.SelectedOptionIds = [group.Options[0].Uid];
        var saved = await scope.Maintenance.SaveDetailAsync(request);
        Assert.IsTrue(saved.Success, saved.Message); fixture.MasterIds.Add(saved.Data.Detail.Uid);
        Assert.AreEqual(1, saved.Data.Detail.Version); CollectionAssert.AreEqual(request.SelectedOptionIds, saved.Data.Detail.SelectedOptionIds);
    }

    /// <summary>使用中的定义和多选改单选被拒绝；清空后允许改单选和定义删除。</summary>
    [TestMethod]
    public async Task ReferencedDefinitionsAndCategoryDeletionAreProtected()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var detail = await fixture.DetailRequestAsync(scope); detail.SelectedOptionIds = group.Options.Select(row => row.Uid).ToList();
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success);
        var remove = new SaveProductAttributeDefinitionsRequest(); remove.GroupChanges.DeleteItems.Add(new() { Uid = group.Uid, Version = 1 });
        var blockedGroup = await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, remove);
        Assert.IsFalse(blockedGroup.Success); StringAssert.Contains(blockedGroup.Message, "1 个主型号");
        var removeOption = new SaveProductAttributeDefinitionsRequest(); removeOption.OptionChanges.DeleteItems.Add(new() { Uid = group.Options[0].Uid, Version = 1 });
        Assert.IsFalse((await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, removeOption)).Success);
        var single = new SaveProductAttributeDefinitionsRequest(); single.GroupChanges.UpdateItems.Add(new() { Uid = group.Uid, Version = 1,
            Data = new() { GroupCode = group.GroupCode, GroupName = group.GroupName, SelectionMode = ProductAttributeSelectionMode.Single } });
        Assert.IsFalse((await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, single)).Success);
        // 无产品引用的分类仍有定义，单项删除不能绕过标签保护。
        await fixture.DefineAsync(scope, fixture.OtherCategory.Uid);
        var category = await scope.Categories.DeleteAsync(new() { Uid = fixture.OtherCategory.Uid, Version = 1 });
        Assert.IsFalse(category.Success); StringAssert.Contains(category.Message, "标签定义");
        detail = await fixture.DetailRequestAsync(scope); detail.SelectedOptionIds = [];
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success);
        Assert.IsTrue((await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, single)).Success);
        remove.GroupChanges.DeleteItems[0].Version = 2;
        Assert.IsTrue((await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, remove)).Success);
    }

    /// <summary>列表、快速归类和现有导入入口保持标签；删除主型号同时软删除关系。</summary>
    [TestMethod]
    public async Task CategoryWriteEntrancesRejectTaggedMasterAndDeleteCleansSelection()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync();
        var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var detail = await fixture.DetailRequestAsync(scope); detail.SelectedOptionIds = [group.Options[0].Uid];
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(detail)).Success);
        detail = await fixture.DetailRequestAsync(scope);
        var batch = new ProductMasterBatchSaveRequest(); var changed = detail.Master.Adapt<SwiftExport.Contracts.DTOs.Products.ProductMasters.ProductMasterWriteDto>(); changed.CategoryId = fixture.OtherCategory.Uid;
        batch.UpdateItems.Add(new() { Uid = fixture.Master.Uid, Version = detail.ExpectedVersion, Data = changed });
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, (await scope.Maintenance.BatchSaveAsync(batch)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, (await scope.Maintenance.AssignCategoryAsync(new() { TargetCategoryId = fixture.OtherCategory.Uid,
            Items = [new() { ProductMasterId = fixture.Master.Uid, ExpectedVersion = detail.ExpectedVersion }] })).Error.Code);
        var input = new ProductMasterImportRowInput { SourceRowNumber = 2, MasterSku = fixture.Master.MasterSku, CategoryPath = fixture.OtherCategory.CategoryName };
        var preview = await scope.Import.PreviewAsync(new() { Rows = [input] }); Assert.IsTrue(preview.Success, preview.Message);
        var commit = ProductMasterImportExportTests.Commit([input], preview.Data);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, (await scope.Import.ImportAsync(commit)).Error.Code);
        var current = await scope.Query.GetDetailAsync(fixture.Master.Uid); Assert.AreEqual(detail.ExpectedVersion, current.Data.Version); Assert.HasCount(1, current.Data.SelectedOptionIds);
        var deletion = new ProductMasterBatchSaveRequest(); deletion.DeleteItems.Add(new() { Uid = fixture.Master.Uid, Version = detail.ExpectedVersion });
        var deleted = await scope.Maintenance.BatchSaveAsync(deletion); Assert.IsTrue(deleted.Success, deleted.Message);
        Assert.AreEqual(0, await scope.Session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product_master_attribute_selection WHERE product_master_id=@Id AND is_deleted=FALSE", new { Id = fixture.Master.Uid }));
    }

    /// <summary>定义写入未授权时不进入写入事务；无权限键登录用户可以读取空缓存资料。</summary>
    [TestMethod]
    public async Task DefinitionWriteRequiresCategoryEdit()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope();
        fixture.User.Admin = false;
        var request = new SaveProductAttributeDefinitionsRequest(); request.GroupChanges.CreateItems.Add(new() { Data = new() { ClientUid = Guid.NewGuid(), GroupCode = "A", GroupName = "无权新增" } });
        var denied = await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, request);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, denied.Error.Code); Assert.IsNull(scope.Session.Transaction);
        Assert.IsTrue((await scope.Definitions.GetDefinitionsAsync(fixture.Category.Uid)).Success);
        fixture.User.Keys = [SwiftExport.Contracts.DTOs.Auth.PermissionKeys.ProductsCategoryEdit];
        Assert.IsTrue((await scope.Definitions.BatchSaveAsync(fixture.Category.Uid, request)).Success);
    }

    /// <summary>关系和头部已经暂存后，中性产品数据库颜色组合冲突仍撤销整个聚合。</summary>
    [TestMethod]
    public async Task NeutralWriteFailureRollsBackMasterAndSelections()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var color = new ProductColor { ColorCodeUnique = Guid.NewGuid().ToString("N"), ColorCode = "T", ColorNameZh = "测试色" };
        Assert.IsTrue((await scope.Services.GetRequiredService<IRepository<ProductColor>>().AddAsync(color)).Success); fixture.ColorIds.Add(color.Uid);
        var suffix = Guid.NewGuid().ToString("N");
        var existingCode = "TAG-ROLLBACK-" + suffix;
        Assert.IsTrue((await scope.Services.GetRequiredService<IRepository<ProductNeutral>>().AddAsync(new ProductNeutral
            { ProductMasterId = fixture.Master.Uid, ColorId = color.Uid, ProductCode = existingCode })).Success);
        var request = await fixture.DetailRequestAsync(scope); request.SelectedOptionIds = [group.Options[0].Uid]; request.Master.DefaultDescriptionZh = "须回滚";
        // 不重复编码，使用数据库的主型号＋颜色组合唯一约束在头部与选择写入后产生失败。
        request.CreateNeutrals.Add(new() { Data = new() { ProductCode = "TAG-NEW-" + suffix, ColorId = color.Uid } });
        var failure = await scope.Maintenance.SaveDetailAsync(request);
        Assert.IsFalse(failure.Success); Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, failure.Error.Code, failure.Message);
        var actual = await scope.Query.GetDetailAsync(fixture.Master.Uid);
        Assert.AreEqual(1, actual.Data.Version); Assert.IsNull(actual.Data.Master.DefaultDescriptionZh); Assert.HasCount(0, actual.Data.SelectedOptionIds);
        Assert.HasCount(1, actual.Data.Neutrals); Assert.AreEqual(existingCode, actual.Data.Neutrals[0].Data.ProductCode);
    }

    /// <summary>直接数据库写入也遵守有效选择唯一约束，不以应用校验代替数据库约束。</summary>
    [TestMethod]
    public async Task ActiveSelectionUniquenessIsEnforcedByPostgreSql()
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var scope = fixture.NewScope(); var group = await fixture.DefineAsync(scope);
        var request = await fixture.DetailRequestAsync(scope); request.SelectedOptionIds = [group.Options[0].Uid];
        Assert.IsTrue((await scope.Maintenance.SaveDetailAsync(request)).Success);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => scope.Session.Connection.ExecuteAsync(
            "INSERT INTO public.product_master_attribute_selection(uid,version,is_deleted,product_master_id,option_id) VALUES(@Uid,1,FALSE,@Master,@Option)",
            new { Uid = Guid.NewGuid(), Master = fixture.Master.Uid, Option = group.Options[0].Uid }));
        Assert.AreEqual("23505", exception.SqlState); Assert.AreEqual("ux_product_master_attribute_selection_master_option", exception.ConstraintName);
        var optionConflict = await Assert.ThrowsAsync<PostgresException>(() => scope.Session.Connection.ExecuteAsync(
            "INSERT INTO public.product_attribute_option(uid,attribute_group_id,option_code,option_name) VALUES(@Uid,@Group,'ceiling','数据库重复值')",
            new { Uid = Guid.NewGuid(), Group = group.Uid }));
        Assert.AreEqual("23505", optionConflict.SqlState); Assert.AreEqual("ux_product_attribute_option_group_code", optionConflict.ConstraintName);
        var groupConflict = await Assert.ThrowsAsync<PostgresException>(() => scope.Session.Connection.ExecuteAsync(
            "INSERT INTO public.product_attribute_group(uid,category_id,group_code,group_name,selection_mode) VALUES(@Uid,@Category,'install','数据库重复组',1)",
            new { Uid = Guid.NewGuid(), Category = fixture.Category.Uid }));
        Assert.AreEqual("23505", groupConflict.SqlState); Assert.AreEqual("ux_product_attribute_group_category_code", groupConflict.ConstraintName);
    }

    /// <summary>分类父锁把删除与新增选择串行化：先删除使选择失败，先选择使删除失败。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DefinitionDeleteAndSelectionSaveAreSerialized(bool deletionFirst)
    {
        await using var fixture = await ProductAttributePostgreSqlFixture.CreateAsync(); var setup = fixture.NewScope(); var group = await fixture.DefineAsync(setup);
        var saveScope = fixture.NewScope(); var deleteScope = fixture.NewScope(); var request = await fixture.DetailRequestAsync(saveScope); request.SelectedOptionIds = [group.Options[0].Uid];
        var remove = new SaveProductAttributeDefinitionsRequest(); remove.GroupChanges.DeleteItems.Add(new() { Uid = group.Uid, Version = 1 });
        await using var owner = new NpgsqlConnection(fixture.ConnectionString); await owner.OpenAsync();
        await using var ownerTransaction = await owner.BeginTransactionAsync();
        await owner.ExecuteAsync("SELECT uid FROM public.product_category WHERE uid=@Id FOR UPDATE", new { Id = fixture.Category.Uid }, ownerTransaction);
        Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> deleteTask;
        Task<ServiceResult<SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses.SaveProductMasterDetailResponse>> saveTask;
        if (deletionFirst)
        {
            deleteTask = deleteScope.Definitions.BatchSaveAsync(fixture.Category.Uid, remove);
            await WaitForParentLockAsync(owner, ownerTransaction, deleteScope);
            saveTask = saveScope.Maintenance.SaveDetailAsync(request);
            await WaitForParentLockAsync(owner, ownerTransaction, saveScope);
        }
        else
        {
            saveTask = saveScope.Maintenance.SaveDetailAsync(request);
            await WaitForParentLockAsync(owner, ownerTransaction, saveScope);
            deleteTask = deleteScope.Definitions.BatchSaveAsync(fixture.Category.Uid, remove);
            await WaitForParentLockAsync(owner, ownerTransaction, deleteScope);
        }
        Assert.IsFalse(deleteTask.IsCompleted); Assert.IsFalse(saveTask.IsCompleted);
        await ownerTransaction.CommitAsync();
        var deleted = await deleteTask.WaitAsync(TimeSpan.FromSeconds(10)); var saved = await saveTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(deletionFirst, deleted.Success, deleted.Message); Assert.AreEqual(!deletionFirst, saved.Success, saved.Message);
        Assert.AreEqual(deletionFirst ? ServiceErrorCodes.Validation : ServiceErrorCodes.BusinessBlocked, deletionFirst ? saved.Error.Code : deleted.Error.Code);
    }

    /// <summary>以 PostgreSQL 实际锁等待状态确认请求已排队，避免仅靠固定延时推断并发。</summary>
    private static async Task WaitForParentLockAsync(NpgsqlConnection monitor, NpgsqlTransaction transaction, ProductAttributePostgreSqlFixture.Scope scope)
    {
        var pid = ((NpgsqlConnection)scope.Session.Connection).ProcessID;
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < limit)
        {
            if (await monitor.ExecuteScalarAsync<bool>("SELECT cardinality(pg_blocking_pids(@Pid))>0", new { Pid = pid }, transaction)) return;
            await Task.Delay(20);
        }
        Assert.Fail("请求未进入预期分类父锁等待。");
    }
}
