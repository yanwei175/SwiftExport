using Dapper;
using Mapster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.Catalog.Maintenance;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>物料详情的真实 PostgreSQL 验收，仅使用获批复制库及独立身份夹具，不初始化或清空数据库。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductDetailPostgreSqlTests
{
    /// <summary>资料和自身选值同次提交；保持、清空和替换都推进物料版本，同身份的其他对象选择独立。</summary>
    [TestMethod]
    public async Task Detail_SelectionProtocolAndOwnerTypeAreAtomic()
    {
        await using var fixture = await MaterialFixture.CreateAsync();
        var scope = fixture.Tags.NewScope();
        var group = await fixture.Tags.DefineAsync(scope);
        var product = await fixture.AddProductAsync(scope);
        var attributes = scope.Services.GetRequiredService<IProductAttributeRepository>();
        var foreign = new ProductAttributeSelection
        { OwnerType = ProductAttributeOwnerType.ProductNeutral, OwnerId = product.Uid, OptionId = group.Options[0].Uid };
        var foreignWrite = await attributes.AddSelectionAsync(foreign);
        Assert.IsTrue(foreignWrite.Success);
        fixture.SelectionIds.Add(foreignWrite.Uid);
        var service = Maintenance(scope);
        var request = Request(product, [group.Options[0].Uid]);
        request.Data.Note = "资料与选值原子保存";
        var initial = await service.SaveDetailAsync(request);
        Assert.IsTrue(initial.Success, initial.Message);
        Assert.AreEqual(product.Version + 1, initial.Data.Detail.Version);
        Assert.AreEqual("资料与选值原子保存", initial.Data.Detail.Data.Note);
        Assert.AreEqual(fixture.Tags.Master.MasterSku, initial.Data.Detail.MasterSku,
            "来源主型号展示使用实时权威关系，不采用物料旧文字型号。");
        Assert.IsTrue(initial.Data.Detail.AttributeSelections.Single().IsAvailable);
        Assert.AreEqual(ProductAttributeOwnerType.Product, initial.Data.Detail.AttributeSelections.Single().OwnerType);

        foreach (var mode in new[] { "keep", "replace", "clear" })
        {
            var before = await ReadProductAsync(scope, product.Uid);
            List<Guid>? final = mode == "keep" ? null : mode == "clear" ? [] : [group.Options[1].Uid];
            var saved = await service.SaveDetailAsync(Request(before, final));
            Assert.IsTrue(saved.Success, saved.Message);
            Assert.AreEqual(before.Version + 1, saved.Data.Detail.Version, "仅标签保存也递增物料版本。");
            var expected = mode == "keep" ? new[] { group.Options[0].Uid }
                : mode == "clear" ? [] : new[] { group.Options[1].Uid };
            CollectionAssert.AreEquivalent(expected, saved.Data.Detail.SelectedOptionIds);
            var own = await attributes.GetSelectionsAsync(ProductAttributeOwnerType.Product, [product.Uid], default);
            CollectionAssert.AreEquivalent(expected, own.Select(row => row.OptionId).ToArray());
            Assert.AreEqual(foreignWrite.Uid,
                (await attributes.GetSelectionsAsync(ProductAttributeOwnerType.ProductNeutral, [product.Uid], default)).Single().Uid);
            Assert.IsNull(scope.Session.Transaction);
        }
    }

    /// <summary>详情只改可写资料，随后旧列表保存仍保留来源、单品尺寸、启停、BOM和独立包装记录。</summary>
    [TestMethod]
    public async Task DetailAndLegacyList_PreserveReadOnlyFieldsAndPackageReference()
    {
        await using var fixture = await MaterialFixture.CreateAsync();
        var scope = fixture.Tags.NewScope();
        var product = await fixture.AddProductAsync(scope, withReadOnlyFacts: true);
        var package = new ProductPackage
        { ProductId = product.Uid, IsDefault = true, PcsPerPackage = 12, Nw_KG = 20m, Gw_KG = 22m, Cbm = 0.4m };
        var write = await scope.Services.GetRequiredService<IRepository<ProductPackage>>().AddAsync(package);
        Assert.IsTrue(write.Success);
        fixture.PackageIds.Add(write.Uid);
        var service = Maintenance(scope);
        var request = Request(product, []);
        request.Data.UnitWeight = 1.75m;
        request.Data.UnitCBM = 0.015m;
        request.Data.CNYPrice = null;
        request.Data.USDPrice = null;
        request.Data.Note = "详情修改";
        var saved = await service.SaveDetailAsync(request);
        Assert.IsTrue(saved.Success, saved.Message);
        Assert.IsNull(saved.Data.Detail.Data.CNYPrice);
        Assert.IsNull(saved.Data.Detail.Data.USDPrice);
        Assert.AreEqual(12m, saved.Data.Detail.PcsOfCarton);
        Assert.AreEqual(20m, saved.Data.Detail.NwOfCarton);
        Assert.AreEqual(22m, saved.Data.Detail.GwOfCarton);
        Assert.AreEqual(0.4m, saved.Data.Detail.CbmOfCarton);
        var repository = scope.Services.GetRequiredService<IProductRepository>();
        var dto = (await repository.GetDtoByIdAsync(product.Uid))!;
        dto.Note = "旧列表修改";
        dto.PcsOfCarton = 999m;
        dto.CbmOfCarton = 999m;
        var list = await service.UpdateAsync(new UpdateItemRequest<ProductDto>
        { Uid = product.Uid, Version = saved.Data.Detail.Version, Data = dto });
        Assert.IsTrue(list.Success, list.Message);
        var actual = await ReadProductAsync(scope, product.Uid);
        Assert.AreEqual(product.ProductMasterId, actual.ProductMasterId);
        Assert.AreEqual(1.75m, actual.UnitWeight);
        Assert.AreEqual(0.015m, actual.UnitCBM);
        Assert.IsFalse(actual.IsActive);
        Assert.AreEqual(product.BomNo, actual.BomNo);
        Assert.AreEqual(product.BomVersion, actual.BomVersion);
        var unchangedPackage = await scope.Services.GetRequiredService<IRepository<ProductPackage>>().GetByIdAsync(write.Uid);
        Assert.AreEqual(12, unchangedPackage!.PcsPerPackage);
        Assert.AreEqual(0.4m, unchangedPackage.Cbm);
        Assert.AreEqual(1, unchangedPackage.Version);
        Assert.AreEqual("旧列表修改", actual.Note);
    }

    /// <summary>过期版本、重复、缺失和单选超量均拒绝，真实事务没有提交资料或选择的部分结果。</summary>
    [TestMethod]
    public async Task Detail_InvalidInputAndStaleVersionRollBackAllWrites()
    {
        await using var fixture = await MaterialFixture.CreateAsync();
        var scope = fixture.Tags.NewScope();
        var group = await fixture.Tags.DefineAsync(scope, mode: ProductAttributeSelectionMode.Single);
        var product = await fixture.AddProductAsync(scope);
        var service = Maintenance(scope);
        var initial = await service.SaveDetailAsync(Request(product, [group.Options[0].Uid]));
        Assert.IsTrue(initial.Success, initial.Message);
        foreach (var scenario in new[] { "version", "missing", "duplicate", "single" })
        {
            var before = await ReadProductAsync(scope, product.Uid);
            var request = Request(before, scenario == "missing" ? [Guid.NewGuid()]
                : scenario == "duplicate" ? [group.Options[0].Uid, group.Options[0].Uid]
                : scenario == "single" ? group.Options.Select(row => row.Uid).ToList() : [group.Options[0].Uid]);
            request.Data.Note = "失败不应留下资料";
            if (scenario == "version") request.ExpectedVersion--;
            var rejected = await service.SaveDetailAsync(request);
            Assert.IsFalse(rejected.Success, scenario);
            Assert.AreEqual(scenario == "version" ? ServiceErrorCodes.VersionConflict : ServiceErrorCodes.Validation, rejected.Error.Code);
            var after = await ReadProductAsync(scope, product.Uid);
            Assert.AreEqual(before.Version, after.Version);
            Assert.AreEqual(before.Note, after.Note);
            var selected = await scope.Services.GetRequiredService<IProductAttributeRepository>()
                .GetSelectionsAsync(ProductAttributeOwnerType.Product, [product.Uid], default);
            Assert.AreEqual(group.Options[0].Uid, selected.Single().OptionId);
            Assert.IsNull(scope.Session.Transaction);
        }
    }

    /// <summary>主型号详情与列表不能换成令物料选择失效的模板，明确清空物料标签后换模板恢复可用。</summary>
    [TestMethod]
    public async Task MasterProfileChanges_ProtectMaterialSelectionsAcrossEntrances()
    {
        await using var fixture = await MaterialFixture.CreateAsync();
        var scope = fixture.Tags.NewScope();
        var group = await fixture.Tags.DefineAsync(scope);
        var product = await fixture.AddProductAsync(scope);
        var service = Maintenance(scope);
        var selected = await service.SaveDetailAsync(Request(product, [group.Options[0].Uid]));
        Assert.IsTrue(selected.Success, selected.Message);
        foreach (var entrance in new[] { "detail", "list" })
        {
            var request = await fixture.Tags.DetailRequestAsync(scope);
            request.Master.ProductAttributeProfileId = fixture.Tags.OtherProfile.Uid;
            (bool Success, string? Code, string Message) rejected;
            if (entrance == "detail")
            {
                var result = await scope.Maintenance.SaveDetailAsync(request);
                rejected = (result.Success, result.Error?.Code, result.Message);
            }
            else
            {
                var result = await scope.Maintenance.BatchSaveAsync(new ProductMasterBatchSaveRequest
                { ContractVersion = 3, UpdateItems = [new() { Uid = fixture.Tags.Master.Uid, Version = request.ExpectedVersion, Data = request.Master }] });
                rejected = (result.Success, result.Error?.Code, result.Message);
            }
            Assert.IsFalse(rejected.Success, rejected.Message);
            Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, rejected.Code);
            var unchanged = await scope.Query.GetDetailAsync(fixture.Tags.Master.Uid);
            Assert.AreEqual(fixture.Tags.Profile.Uid, unchanged.Data.Master.ProductAttributeProfileId);
            Assert.AreEqual(request.ExpectedVersion, unchanged.Data.Version);
            StringAssert.Contains(rejected.Message, "1");
        }
        var clear = await service.SaveDetailAsync(Request(await ReadProductAsync(scope, product.Uid), []));
        Assert.IsTrue(clear.Success, clear.Message);
        var change = await fixture.Tags.DetailRequestAsync(scope);
        change.Master.ProductAttributeProfileId = fixture.Tags.OtherProfile.Uid;
        var changed = await scope.Maintenance.SaveDetailAsync(change);
        Assert.IsTrue(changed.Success, changed.Message);
        Assert.AreEqual(fixture.Tags.OtherProfile.Uid, changed.Data.Detail.Master.ProductAttributeProfileId);
    }

    /// <summary>来源删除不能让物料选值悬空；物料单项和批量删除同事务清理本类型选择，其他对象保持。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MasterAndMaterialDelete_RespectSelectionOwnership(bool batch)
    {
        await using var fixture = await MaterialFixture.CreateAsync();
        var scope = fixture.Tags.NewScope();
        var group = await fixture.Tags.DefineAsync(scope);
        var product = await fixture.AddProductAsync(scope);
        var service = Maintenance(scope);
        var selected = await service.SaveDetailAsync(Request(product, [group.Options[0].Uid]));
        Assert.IsTrue(selected.Success, selected.Message);
        var attributes = scope.Services.GetRequiredService<IProductAttributeRepository>();
        var other = await attributes.AddSelectionAsync(new ProductAttributeSelection
        { OwnerType = ProductAttributeOwnerType.ProductNeutral, OwnerId = product.Uid, OptionId = group.Options[0].Uid });
        Assert.IsTrue(other.Success);
        fixture.SelectionIds.Add(other.Uid);
        var master = await fixture.Tags.DetailRequestAsync(scope);
        var deletion = new ProductMasterBatchSaveRequest
        { ContractVersion = 3, DeleteItems = [new() { Uid = fixture.Tags.Master.Uid, Version = master.ExpectedVersion }] };
        var removedMaster = await scope.Maintenance.BatchSaveAsync(deletion);
        Assert.IsTrue(removedMaster.Success, removedMaster.Message);
        var retained = await scope.Services.GetRequiredService<IProductRepository>().GetByIdAsync(product.Uid);
        Assert.AreEqual(fixture.Tags.Master.Uid, retained!.ProductMasterId);
        Assert.HasCount(1, await attributes.GetSelectionsAsync(ProductAttributeOwnerType.Product, [product.Uid], default));
        var item = new DeleteItemRequest { Uid = product.Uid, Version = selected.Data.Detail.Version };
        if (batch)
        {
            var deleted = await service.BatchSaveAsync(new ProductBatchSaveRequest { DeleteItems = [item] });
            Assert.IsTrue(deleted.Success, deleted.Message);
        }
        else
        {
            var deleted = await service.DeleteAsync(item);
            Assert.IsTrue(deleted.Success, deleted.Message);
        }
        Assert.HasCount(0, await attributes.GetSelectionsAsync(ProductAttributeOwnerType.Product, [product.Uid], default));
        Assert.AreEqual(other.Uid, (await attributes.GetSelectionsAsync(ProductAttributeOwnerType.ProductNeutral, [product.Uid], default)).Single().Uid);
        Assert.IsNull(scope.Session.Transaction);
        var permitted = await scope.Maintenance.BatchSaveAsync(deletion);
        Assert.IsTrue(permitted.Success, permitted.Message);
    }

    /// <summary>定义解除关联和多选改单选都计入物料选择，整页失败恢复组字段及版本。</summary>
    [TestMethod]
    public async Task DefinitionMaintenance_ProtectsMaterialRelationsAndSingleMode()
    {
        await using var fixture = await MaterialFixture.CreateAsync();
        var scope = fixture.Tags.NewScope();
        var group = await fixture.Tags.DefineAsync(scope);
        var product = await fixture.AddProductAsync(scope);
        var saved = await Maintenance(scope).SaveDetailAsync(Request(product, group.Options.Select(row => row.Uid).ToList()));
        Assert.IsTrue(saved.Success, saved.Message);
        foreach (var kind in new[] { "relation", "single", "option" })
        {
            var request = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
            ProductAttributePostgreSqlFixture.TouchGroup(request, group);
            request.GroupChanges.UpdateItems[0].Data.GroupName = "拒绝时不能修改";
            if (kind == "single") request.GroupChanges.UpdateItems[0].Data.SelectionMode = ProductAttributeSelectionMode.Single;
            else if (kind == "option") request.OptionChanges.DeleteItems.Add(new() { Uid = group.Options[0].Uid, Version = group.Options[0].Version });
            else
            {
                var relation = (await scope.Definitions.GetDefinitionsAsync(fixture.Tags.Profile.Uid)).Data.ProfileRelations.Single();
                request.ProfileChanges.DeleteItems.Add(new() { Uid = relation.Uid, Version = relation.Version });
            }
            var rejected = await fixture.Tags.SaveAsync(scope, request);
            Assert.IsFalse(rejected.Success, kind);
            Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, rejected.Error.Code);
            var snapshot = await scope.Definitions.GetDefinitionsAsync(fixture.Tags.Profile.Uid);
            Assert.AreEqual(group.Version, snapshot.Data.Groups.Single().Version);
            Assert.AreEqual(group.GroupName, snapshot.Data.Groups.Single().GroupName);
            Assert.HasCount(2, snapshot.Data.Groups.Single().Options);
            Assert.IsNull(scope.Session.Transaction);
        }
    }

    /// <summary>关联解除或上游换模板与新增物料选择真实竞争；两种排队顺序均不能提交悬空选择或死锁。</summary>
    [TestMethod]
    [DataRow("relation", true)]
    [DataRow("relation", false)]
    [DataRow("master", true)]
    [DataRow("master", false)]
    public async Task ConcurrentSourceMutationAndSelection_NeverLeaveUnavailableChoice(string kind, bool mutationFirst)
    {
        await using var fixture = await MaterialFixture.CreateAsync();
        var setup = fixture.Tags.NewScope();
        var group = await fixture.Tags.DefineAsync(setup);
        var product = await fixture.AddProductAsync(setup);
        var saveScope = fixture.Tags.NewScope();
        var changeScope = fixture.Tags.NewScope();
        var choose = Request(product, [group.Options[0].Uid]);
        var changeMaster = await fixture.Tags.DetailRequestAsync(changeScope);
        changeMaster.Master.ProductAttributeProfileId = fixture.Tags.OtherProfile.Uid;
        var remove = new SaveProductAttributeDefinitionsRequest { ContractVersion = 2 };
        ProductAttributePostgreSqlFixture.TouchGroup(remove, group);
        var relation = (await setup.Definitions.GetDefinitionsAsync(fixture.Tags.Profile.Uid)).Data.ProfileRelations.Single();
        remove.ProfileChanges.DeleteItems.Add(new() { Uid = relation.Uid, Version = relation.Version });
        await using var barrier = new NpgsqlConnection(fixture.Tags.ConnectionString);
        await barrier.OpenAsync();
        await using var transaction = await barrier.BeginTransactionAsync();
        var sql = kind == "master" ? "SELECT uid FROM public.product_master WHERE uid=@Id FOR UPDATE"
            : "SELECT uid FROM public.product_attribute_group WHERE uid=@Id FOR UPDATE";
        await barrier.ExecuteAsync(sql, new { Id = kind == "master" ? fixture.Tags.Master.Uid : group.Uid }, transaction);

        async Task<bool> MutateAsync() => kind == "master"
            ? (await changeScope.Maintenance.SaveDetailAsync(changeMaster)).Success
            : (await fixture.Tags.SaveAsync(changeScope, remove)).Success;
        Task<bool> mutation;
        Task<ServiceResult<SaveProductDetailResponse>> selection;
        if (mutationFirst)
        {
            mutation = MutateAsync();
            await WaitForLockAsync(barrier, transaction, changeScope);
            selection = Maintenance(saveScope).SaveDetailAsync(choose);
            await WaitForLockAsync(barrier, transaction, saveScope);
        }
        else
        {
            selection = Maintenance(saveScope).SaveDetailAsync(choose);
            await WaitForLockAsync(barrier, transaction, saveScope);
            mutation = MutateAsync();
            await WaitForLockAsync(barrier, transaction, changeScope);
        }
        Assert.IsFalse(selection.IsCompleted);
        Assert.IsFalse(mutation.IsCompleted);
        await transaction.CommitAsync();
        var mutated = await mutation.WaitAsync(TimeSpan.FromSeconds(15));
        var selected = await selection.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.IsTrue(mutated || selected.Success, "两条正式请求至少一条完成；序列化冲突由另一请求明确拒绝。");
        Assert.IsFalse(mutated && selected.Success, "破坏来源与保存选择不能同时提交。");
        var actual = await setup.Services.GetRequiredService<IProductAttributeRepository>()
            .GetSelectionDetailsAsync(ProductAttributeOwnerType.Product, [product.Uid], default);
        Assert.IsTrue(actual.All(row => row.IsAvailable), "竞争之后不能留下失去模板适用性的选择。");
        Assert.HasCount(selected.Success ? 1 : 0, actual);
        Assert.IsNull(saveScope.Session.Transaction);
        Assert.IsNull(changeScope.Session.Transaction);
    }

    /// <summary>无来源主型号时允许维护基础资料及空选择，但禁止保存任何标签。</summary>
    [TestMethod]
    public async Task UnlinkedMaterial_AllowsEmptySelectionAndRejectsNonemptySelection()
    {
        await using var fixture = await MaterialFixture.CreateAsync();
        var scope = fixture.Tags.NewScope();
        var group = await fixture.Tags.DefineAsync(scope);
        var product = await fixture.AddProductAsync(scope, unlinked: true);
        var service = Maintenance(scope);
        var request = Request(product, []);
        request.Data.Note = "未关联物料资料";
        var saved = await service.SaveDetailAsync(request);
        Assert.IsTrue(saved.Success, saved.Message);
        Assert.IsNull(saved.Data.Detail.ProductAttributeProfileId);
        var invalid = Request(await ReadProductAsync(scope, product.Uid), [group.Options[0].Uid]);
        invalid.Data.Note = "不能提交";
        var rejected = await service.SaveDetailAsync(invalid);
        Assert.IsFalse(rejected.Success);
        Assert.AreEqual(ServiceErrorCodes.Validation, rejected.Error.Code);
        Assert.AreEqual("未关联物料资料", (await ReadProductAsync(scope, product.Uid)).Note);
    }

    /// <summary>使用真实仓储和共享会话构造应用用例；夹具只注册 Infrastructure，不引入另一事务。</summary>
    private static ProductMaintenanceService Maintenance(ProductAttributePostgreSqlFixture.Scope scope)
        => new(scope.Services.GetRequiredService<IProductRepository>(), new ServiceBaseContext(scope.Session, NullLoggerFactory.Instance),
            scope.Services.GetRequiredService<IProductColorRepository>(), scope.Services.GetRequiredService<ISupplierRepository>(),
            scope.Services.GetRequiredService<IWarehouseQueryRepository>(), scope.Services.GetRequiredService<IBomRepository>(),
            scope.Services.GetRequiredService<ISysDictDataRepository>(), scope.Services.GetRequiredService<IProductAttributeRepository>(),
            scope.Services.GetRequiredService<IProductMasterRepository>());

    /// <summary>从当前物料复制可写资料，不将模板、包装和 BOM 传入详情写 DTO。</summary>
    private static SaveProductDetailRequest Request(Product product, List<Guid>? selections)
        => new() { ProductId = product.Uid, ExpectedVersion = product.Version, Data = product.Adapt<ProductDetailWriteDto>(), SelectedOptionIds = selections };

    /// <summary>读取当前实体及版本；失败时保留明确夹具缺失证据。</summary>
    private static async Task<Product> ReadProductAsync(ProductAttributePostgreSqlFixture.Scope scope, Guid productId)
        => await scope.Services.GetRequiredService<IProductRepository>().GetByIdAsync(productId)
            ?? throw new AssertFailedException("本次物料夹具不存在。");

    /// <summary>查询实际 PostgreSQL 阻塞状态作为排队证据，短轮询有截止时间，不以固定长延时猜测并发。</summary>
    private static async Task WaitForLockAsync(NpgsqlConnection monitor, NpgsqlTransaction transaction, ProductAttributePostgreSqlFixture.Scope scope)
    {
        var pid = ((NpgsqlConnection)scope.Session.Connection).ProcessID;
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < limit)
        {
            if (await monitor.ExecuteScalarAsync<bool>("SELECT cardinality(pg_blocking_pids(@Pid))>0", new { Pid = pid }, transaction)) return;
            await Task.Delay(20);
        }
        Assert.Fail("物料或来源维护请求未进入预期锁等待。");
    }

    /// <summary>复用已有严格连接守卫，额外追踪本任务物料、包装及跨对象选择；退出只清理捕获的身份。</summary>
    private sealed class MaterialFixture(ProductAttributePostgreSqlFixture tags) : IAsyncDisposable
    {
        internal readonly ProductAttributePostgreSqlFixture Tags = tags;
        internal readonly List<Guid> ProductIds = [];
        internal readonly List<Guid> PackageIds = [];
        internal readonly List<Guid> SelectionIds = [];

        /// <summary>初始化只使用现有复制库夹具，不创建或迁移结构。</summary>
        internal static async Task<MaterialFixture> CreateAsync() => new(await ProductAttributePostgreSqlFixture.CreateAsync());

        /// <summary>直接建立唯一测试物料，保留可选只读事实用于验证详情写边界。</summary>
        internal async Task<Product> AddProductAsync(ProductAttributePostgreSqlFixture.Scope scope, bool withReadOnlyFacts = false, bool unlinked = false)
        {
            var product = new Product
            {
                Sku = "DETAIL-" + Guid.NewGuid().ToString("N"), ProductMasterId = unlinked ? null : Tags.Master.Uid,
                SupplyType = SupplyTypeEnums.采购, MaterialType = MaterialTypesEnum.零件,
                IsActive = !withReadOnlyFacts, BomNo = withReadOnlyFacts ? "DETAIL-BOM-" + Guid.NewGuid().ToString("N") : null,
                BomVersion = withReadOnlyFacts ? 3 : 0, CNYPrice = 10m, USDPrice = 2m
            };
            var write = await scope.Services.GetRequiredService<IProductRepository>().AddAsync(product);
            Assert.IsTrue(write.Success, write.Message);
            ProductIds.Add(write.Uid);
            product.Uid = write.Uid;
            product.Version = write.NewVersion!.Value;
            return product;
        }

        /// <summary>先移除本次物料附属记录再释放原标签夹具；绝不按全表或模板范围清理无关物料。</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (ProductIds.Count > 0 || PackageIds.Count > 0 || SelectionIds.Count > 0)
                {
                    foreach (var scope in Tags.Scopes) scope.Dispose();
                    await using var connection = new NpgsqlConnection(Tags.ConnectionString);
                    await connection.OpenAsync();
                    await using var transaction = await connection.BeginTransactionAsync();
                    await connection.ExecuteAsync("""
                        DELETE FROM public.product_attribute_selection WHERE (owner_type=4 AND owner_id=ANY(@Products)) OR uid=ANY(@Selections);
                        DELETE FROM public.product_package WHERE uid=ANY(@Packages);
                        DELETE FROM public.product WHERE uid=ANY(@Products);
                        """, new { Products = ProductIds.ToArray(), Packages = PackageIds.ToArray(), Selections = SelectionIds.ToArray() }, transaction);
                    await transaction.CommitAsync();
                }
            }
            finally { await Tags.DisposeAsync(); }
        }
    }
}
