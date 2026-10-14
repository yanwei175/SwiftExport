using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Products.ProductMasters.Maintenance;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.WebApi.ErrorHandling;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>通过真实保存用例验证父引用锁顺序、精确占用分类，以及失败后的事务回滚边界。</summary>
[TestClass]
public sealed class ProductMasterDetailLockOrderTests
{
    /// <summary>旧客户端或省略写协议版本不得误把未知字符串忽略后清空系列身份，拒绝路径不开事务。</summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public async Task OldSeriesWriteContract_IsRejectedBeforeTransaction(int contractVersion)
    {
        var fixture = new Fixture();
        var request = fixture.CreateRequest(false);
        request.ContractVersion = contractVersion;
        var detail = await fixture.Service.SaveDetailAsync(request);
        var batch = await fixture.Service.BatchSaveAsync(new()
        {
            ContractVersion = contractVersion,
            CreateItems = [new() { Data = request.Master }]
        });
        Assert.IsFalse(detail.Success);
        Assert.IsFalse(batch.Success);
        Assert.AreEqual(ServiceErrorCodes.Validation, detail.Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Validation, batch.Error.Code);
        Assert.AreEqual(0, fixture.Calls.Count);
        Assert.AreEqual(0, fixture.WriteCount);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>普通列表与详情保存都应拒绝非正父级装率，并在写入前回滚事务。</summary>
    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(false, -1)]
    [DataRow(true, 0)]
    [DataRow(true, -1)]
    public async Task NonPositiveDefaultPcs_ShouldRejectWithoutWrites(bool batch, int pcs)
    {
        var fixture = new Fixture();
        var request = fixture.CreateRequest(false);
        request.Master.DefaultPcsPerPackage = pcs;

        var result = batch
            ? (await fixture.Service.BatchSaveAsync(new ProductMasterBatchSaveRequest
            {
                ContractVersion = 3,
                CreateItems = [new() { Data = request.Master }]
            })).Success
            : (await fixture.Service.SaveDetailAsync(request)).Success;

        Assert.IsFalse(result, "非正装率必须由普通维护用例阻断。");
        fixture.AssertRollbackWithoutWrites();
    }

    /// <summary>列表新增与详情修改应原样把空装率或正整数传给仓储，并正常提交。</summary>
    [TestMethod]
    [DataRow(false, null)]
    [DataRow(false, 1)]
    [DataRow(false, 12)]
    [DataRow(true, null)]
    [DataRow(true, 1)]
    [DataRow(true, 12)]
    public async Task OptionalDefaultPcs_ShouldPreserveValueInOrdinarySaves(bool batch, int? pcs)
    {
        var fixture = new Fixture();
        var request = fixture.CreateRequest(false);
        request.Master.DefaultPcsPerPackage = pcs;

        var success = batch
            ? (await fixture.Service.BatchSaveAsync(new ProductMasterBatchSaveRequest
            {
                ContractVersion = 3,
                CreateItems = [new() { Data = request.Master }]
            })).Success
            : (await fixture.Service.SaveDetailAsync(request)).Success;

        Assert.IsTrue(success);
        Assert.AreEqual(pcs, fixture.WrittenMasters.Single().DefaultPcsPerPackage);
        Assert.AreEqual(1, fixture.Session.ConnectionStub.TransactionStub.CommitCount);
        Assert.AreEqual(0, fixture.Session.ConnectionStub.TransactionStub.RollbackCount);
        fixture.AssertTransactionReleased();
    }

    /// <summary>仅保存头部或同时新增明细时，引用锁必须先于主型号和中性行锁，并正常提交。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ValidSave_ShouldLockReferencesBeforeAggregateAndCommit(bool withNeutral)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.SaveDetailAsync(fixture.CreateRequest(withNeutral));

        Assert.IsTrue(result.Success, result.Message);
        fixture.AssertLockPrefix(withNeutral);
        Assert.AreEqual(withNeutral ? 2 : 1, fixture.WriteCount);
        Assert.AreEqual(4, result.Data.Detail.Version);
        Assert.AreEqual(withNeutral ? 1 : 0, result.Data.Detail.Neutrals.Count);
        Assert.AreEqual(1, fixture.Session.ConnectionStub.TransactionStub.CommitCount);
        Assert.AreEqual(0, fixture.Session.ConnectionStub.TransactionStub.RollbackCount);
        fixture.AssertTransactionReleased();
    }

    /// <summary>失效分类在头部锁和所有写入之前阻断保存，包含明细时也不得继续锁颜色。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingCategory_ShouldRollbackBeforeAggregateLocksWithoutWrites(bool withNeutral)
    {
        var fixture = new Fixture { CategoryExists = false };

        var result = await fixture.Service.SaveDetailAsync(fixture.CreateRequest(withNeutral));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        CollectionAssert.AreEqual(new[] { "category" }, fixture.Calls.ToArray());
        fixture.AssertRollbackWithoutWrites();
    }

    /// <summary>失效颜色在主型号和中性行锁之前阻断新增明细，并回滚父引用校验事务。</summary>
    [TestMethod]
    public async Task MissingColor_ShouldRollbackBeforeAggregateLocksWithoutWrites()
    {
        var fixture = new Fixture { ColorExists = false };

        var result = await fixture.Service.SaveDetailAsync(fixture.CreateRequest(true));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        CollectionAssert.AreEqual(new[] { "category", "supplier", "color" }, fixture.Calls.ToArray());
        fixture.AssertRollbackWithoutWrites();
    }

    /// <summary>调整锁顺序后仍须重验头版本；无论是否包含明细都返回原版本冲突代码且不写入。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StaleHeadVersion_ShouldReturnVersionConflictAndRollbackWithoutWrites(bool withNeutral)
    {
        var fixture = new Fixture();
        var request = fixture.CreateRequest(withNeutral);
        request.ExpectedVersion = 2;

        var result = await fixture.Service.SaveDetailAsync(request);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        fixture.AssertLockPrefix(withNeutral);
        Assert.AreEqual(withNeutral ? 5 : 4, fixture.Calls.Count);
        fixture.AssertRollbackWithoutWrites();
    }

    /// <summary>主型号批量第二次新增遭遇编码占用或原失败时，撤销第一条写入。</summary>
    [TestMethod]
    [DataRow("PRODUCT_MASTER_SKU_OCCUPIED", ServiceErrorCodes.Validation, 400)]
    [DataRow("CONCURRENCY_CONFLICT", ServiceErrorCodes.VersionConflict, 409)]
    [DataRow("WRITE_FAILED", ServiceErrorCodes.BusinessBlocked, 422)]
    public async Task MasterBatchWriteFailure_ShouldRollbackPriorInsertAndSkipSync(string writeCode, string expectedCode, int status)
    {
        var fixture = new Fixture { FailWriteNumber = 2, WriteErrorCode = writeCode };
        var first = fixture.CreateRequest(false).Master;
        var second = fixture.CreateRequest(false).Master;
        second.MasterSku = "MASTER-LOCK-ORDER-2";

        var result = await fixture.Service.BatchSaveAsync(new ProductMasterBatchSaveRequest
        {
            ContractVersion = 3,
            CreateItems = [new() { Data = first }, new() { Data = second }]
        });

        Assert.IsFalse(result.Success);
        Assert.AreEqual(expectedCode, result.Error.Code);
        Assert.AreEqual(status, ServiceErrorHttpMapper.GetStatusCode(result.Error.Code));
        fixture.AssertWriteRollback(2);
        Assert.AreEqual(0, fixture.CreatedMasterCount);
    }

    /// <summary>详情保存精确区分头编码、明细编码及颜色组合占用，并撤销已经成功的头部写入。</summary>
    [TestMethod]
    [DataRow(1, "PRODUCT_MASTER_SKU_OCCUPIED", ServiceErrorCodes.Validation, 400)]
    [DataRow(2, "PRODUCT_NEUTRAL_CODE_OCCUPIED", ServiceErrorCodes.Validation, 400)]
    [DataRow(2, "PRODUCT_NEUTRAL_MASTER_COLOR_OCCUPIED", ServiceErrorCodes.BusinessBlocked, 422)]
    [DataRow(2, "CONCURRENCY_CONFLICT", ServiceErrorCodes.VersionConflict, 409)]
    [DataRow(2, "WRITE_FAILED", ServiceErrorCodes.BusinessBlocked, 422)]
    public async Task DetailWriteFailure_ShouldRollbackHeadAndSkipSync(int failWrite, string writeCode, string expectedCode, int status)
    {
        var fixture = new Fixture { FailWriteNumber = failWrite, WriteErrorCode = writeCode };

        var result = await fixture.Service.SaveDetailAsync(fixture.CreateRequest(true));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(expectedCode, result.Error.Code);
        Assert.AreEqual(status, ServiceErrorHttpMapper.GetStatusCode(result.Error.Code));
        fixture.AssertWriteRollback(failWrite);
    }

    /// <summary>未识别技术异常不能冒充版本冲突；失败详情必须回滚已写头部且仍由 HTTP 500 出口处理。</summary>
    [TestMethod]
    public async Task UnexpectedDetailWriteException_ShouldRollbackAndRemainUnexpected()
    {
        var fixture = new Fixture { FailWriteNumber = 2, ThrowOnWriteFailure = true };

        var result = await fixture.Service.SaveDetailAsync(fixture.CreateRequest(true));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error.Code);
        Assert.AreEqual(500, ServiceErrorHttpMapper.GetStatusCode(result.Error.Code));
        fixture.AssertWriteRollback(2);
    }

    /// <summary>删除来源主型号允许留下失效物料引用及其独立标签；实际删除用例不能因物料标签阻断或隐式清理。</summary>
    [TestMethod]
    public async Task DeleteMaster_WithLinkedProductTags_PreservesProductReferenceAndSelections()
    {
        var product = new Product { Uid = Guid.NewGuid(), Sku = "SOURCE-DELETE-PRODUCT" };
        var selection = new ProductAttributeSelection
        {
            OwnerType = ProductAttributeOwnerType.Product, OwnerId = product.Uid, OptionId = Guid.NewGuid()
        };
        var groupId = Guid.NewGuid();
        var productSelectionReads = 0;
        var groupLocks = 0;
        // 只提供删除用例必需的读取；任何选择写入都作为意外调用失败，避免“空读取”绕开回归。
        var attributes = Proxy<IProductAttributeRepository>((method, args) => method switch
        {
            "GetProfilesAsync" => Task.FromResult<IReadOnlyList<SysDictData>>([]),
            "GetSelectionsAsync" => ReadSelections((ProductAttributeOwnerType)args[0]!, (IEnumerable<Guid>)args[1]!),
            "GetGroupIdsForOptionsAsync" => Task.FromResult<IReadOnlyList<Guid>>(
                ((IEnumerable<Guid>)args[0]!).Contains(selection.OptionId) ? [groupId] : []),
            "LockGroupsForSelectionAsync" => LockGroups((IEnumerable<Guid>)args[0]!),
            _ => throw new AssertFailedException($"删除主型号不应调用物料标签维护：{method}。")
        });
        var fixture = new Fixture(attributes) { LinkedProduct = product };
        var masterId = fixture.CreateRequest(false).ProductMasterId;
        product.ProductMasterId = masterId;

        var result = await fixture.Service.BatchSaveAsync(new ProductMasterBatchSaveRequest
        {
            ContractVersion = 3,
            DeleteItems = [new() { Uid = masterId, Version = 3 }]
        });

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(fixture.MasterDeleted);
        Assert.IsTrue(productSelectionReads > 0, "必须真实读取关联物料的选择，不能依赖空标签夹具绕过保护。");
        Assert.AreEqual(1, groupLocks);
        Assert.AreEqual(masterId, product.ProductMasterId);
        Assert.AreEqual(ProductAttributeOwnerType.Product, selection.OwnerType);
        Assert.AreEqual(product.Uid, selection.OwnerId);
        Assert.IsFalse(selection.IsDeleted);
        Assert.AreEqual(1, fixture.WriteCount);
        Assert.IsFalse(fixture.Calls.Contains("product.write"));
        Assert.AreEqual(1, fixture.Session.ConnectionStub.TransactionStub.CommitCount);
        Assert.AreEqual(0, fixture.Session.ConnectionStub.TransactionStub.RollbackCount);
        fixture.AssertTransactionReleased();

        // 按对象类型提供真实非空物料选择；主型号自身没有选择，删除它不会触及本物料关系。
        Task<IReadOnlyList<ProductAttributeSelection>> ReadSelections(ProductAttributeOwnerType ownerType, IEnumerable<Guid> ids)
        {
            if (ownerType != ProductAttributeOwnerType.Product) return Task.FromResult<IReadOnlyList<ProductAttributeSelection>>([]);
            productSelectionReads++;
            return Task.FromResult<IReadOnlyList<ProductAttributeSelection>>(ids.Contains(product.Uid) ? [selection] : []);
        }

        // 仍保留定义共享锁的调用证据，本测试不宣称模拟真实 PostgreSQL 的锁竞争。
        Task<IReadOnlyList<ProductAttributeGroup>> LockGroups(IEnumerable<Guid> ids)
        {
            CollectionAssert.AreEqual(new[] { groupId }, ids.ToArray());
            groupLocks++;
            return Task.FromResult<IReadOnlyList<ProductAttributeGroup>>([new ProductAttributeGroup { Uid = groupId }]);
        }
    }

    /// <summary>列表和详情修改描述、配置、供应商及重量体积时，只写主型号，不隐式写关联物料。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MasterSave_DoesNotOverwriteProductData(bool batch)
    {
        var fixture = new Fixture();
        var request = fixture.CreateRequest(false);
        request.Master.DefaultDescriptionEn = "主型号描述";
        request.Master.DefaultFeaturesZh = "主型号配置";
        request.Master.DefaultGrossWeight = 12.5m;
        request.Master.DefaultCbm = 0.5m;
        var success = batch
            ? (await fixture.Service.BatchSaveAsync(new ProductMasterBatchSaveRequest
            {
                ContractVersion = 3,
                UpdateItems = [new() { Uid = request.ProductMasterId, Version = request.ExpectedVersion, Data = request.Master }]
            })).Success
            : (await fixture.Service.SaveDetailAsync(request)).Success;

        Assert.IsTrue(success);
        Assert.AreEqual(1, fixture.WriteCount);
        Assert.AreEqual("主型号描述", fixture.WrittenMasters.Single().DefaultDescriptionEn);
        Assert.AreEqual("主型号配置", fixture.WrittenMasters.Single().DefaultFeaturesZh);
        Assert.AreEqual(12.5m, fixture.WrittenMasters.Single().DefaultGrossWeight);
        Assert.AreEqual(0.5m, fixture.WrittenMasters.Single().DefaultCbm);
        Assert.AreEqual(1, fixture.Session.ConnectionStub.TransactionStub.CommitCount);
        fixture.AssertTransactionReleased();
    }

    /// <summary>受控仓储记录实际调用和写入，使用会话契约提供带快照回滚的离线事务，不执行 SQL。</summary>
    private sealed class Fixture
    {
        private readonly Guid _masterId = Guid.NewGuid();
        private readonly Guid _categoryId = Guid.NewGuid();
        private readonly Guid _supplierId = Guid.NewGuid();
        private readonly Guid _colorId = Guid.NewGuid();
        private readonly List<ProductNeutralDto> _neutrals = [];
        private readonly List<Guid> _createdMasters = [];
        private int _version = 3;

        /// <summary>构造真实业务服务，全部仓储共享同一受控会话。</summary>
        public Fixture(IProductAttributeRepository? attributes = null)
        {
            var originalVersion = _version;
            var originalDeleted = MasterDeleted;
            ProductNeutralDto[] originalNeutrals = [];
            Guid[] originalMasters = [];
            // 仅恢复本夹具暂存的新增和头版本，不声称模拟真实数据库的约束或锁竞争。
            Session.ConnectionStub.OnBegin = () =>
            {
                originalVersion = _version;
                originalDeleted = MasterDeleted;
                originalNeutrals = _neutrals.ToArray();
                originalMasters = _createdMasters.ToArray();
            };
            Session.ConnectionStub.TransactionStub.OnRollback = () =>
            {
                _version = originalVersion;
                MasterDeleted = originalDeleted;
                _neutrals.Clear();
                _neutrals.AddRange(originalNeutrals);
                _createdMasters.Clear();
                _createdMasters.AddRange(originalMasters);
            };
            Service = new ProductMasterMaintenanceService(
                Proxy<IProductMasterRepository>(MasterCall),
                Proxy<IProductNeutralRepository>(NeutralCall),
                Proxy<IProductCategoryRepository>(CategoryCall),
                Proxy<ISupplierRepository>((method, args) => ReferenceCall(method, args, "supplier", _supplierId,
                    new[] { new Supplier { Uid = _supplierId, SupplierName = "测试供应商", SupplierCode = "S", DocRelateNo = "S", IsActive = true } })),
                Proxy<IProductColorRepository>((method, args) => ReferenceCall<ProductColor>(method, args, "color", _colorId,
                    ColorExists ? [new ProductColor { Uid = _colorId }] : [])),
                Proxy<IProductRepository>(ProductCall),
                attributes ?? new EmptyProductAttributeRepository(),
                new ServiceBaseContext(Session, NullLoggerFactory.Instance));
        }

        public SessionStub Session { get; } = new();
        public ProductMasterMaintenanceService Service { get; }
        public List<string> Calls { get; } = [];
        /// <summary>记录仓储实际收到的主型号写入数据，用于核对可选装率的持久化边界。</summary>
        public List<ProductMaster> WrittenMasters { get; } = [];
        public bool CategoryExists { get; set; } = true;
        public bool ColorExists { get; set; } = true;
        public int WriteCount { get; private set; }
        public int SuccessfulWrites { get; private set; }
        public int FailWriteNumber { get; set; }
        public string WriteErrorCode { get; set; } = "WRITE_FAILED";
        public bool ThrowOnWriteFailure { get; set; }
        public int CreatedMasterCount => _createdMasters.Count;
        /// <summary>删除场景可指定独立物料，仓储只返回其身份，不修改来源或选值。</summary>
        public Product? LinkedProduct { get; set; }
        /// <summary>记录主型号软删除结果，供真实批量删除路径验收。</summary>
        public bool MasterDeleted { get; private set; }

        /// <summary>创建使用有效父引用的头部请求，可附带一条新增中性明细。</summary>
        public SaveProductMasterDetailRequest CreateRequest(bool withNeutral) => new()
        {
            ContractVersion = 3,
            ProductMasterId = _masterId,
            ExpectedVersion = 3,
            Master = new ProductMasterWriteDto
            {
                MasterSku = "MASTER-LOCK-ORDER", CategoryId = _categoryId, DefaultSupplierId = _supplierId
            },
            CreateNeutrals = withNeutral
                ? [new CreateItemRequest<ProductNeutralWriteDto>
                {
                    Data = new ProductNeutralWriteDto { ProductCode = "NEUTRAL-LOCK-ORDER", ColorId = _colorId }
                }]
                : []
        };

        /// <summary>断言实际引用锁调用先于聚合锁；头部保存不得请求不存在的明细颜色锁。</summary>
        public void AssertLockPrefix(bool withNeutral)
        {
            string[] expected = withNeutral
                ? ["category", "supplier", "color", "master.lock", "neutral.lock"]
                : ["category", "supplier", "master.lock", "neutral.lock"];
            CollectionAssert.AreEqual(expected, Calls.Take(expected.Length).ToArray());
            if (!withNeutral) Assert.IsFalse(Calls.Contains("color"));
        }

        /// <summary>确认写入前失败只回滚一次，没有提交或遗留会话事务。</summary>
        public void AssertRollbackWithoutWrites()
        {
            Assert.AreEqual(0, WriteCount);
            Assert.AreEqual(0, Session.ConnectionStub.TransactionStub.CommitCount);
            Assert.AreEqual(1, Session.ConnectionStub.TransactionStub.RollbackCount);
            AssertTransactionReleased();
        }

        /// <summary>确认最外层用例释放并清理本次事务。</summary>
        public void AssertTransactionReleased()
        {
            Assert.IsNull(Session.Transaction);
            Assert.AreEqual(1, Session.ConnectionStub.TransactionStub.DisposeCount);
        }

        /// <summary>确认成功的前序暂存写入已撤销，错误之后没有保存后重读，且事务已释放。</summary>
        public void AssertWriteRollback(int attempts)
        {
            Assert.AreEqual(attempts, WriteCount);
            Assert.AreEqual(attempts - 1, SuccessfulWrites);
            Assert.AreEqual(3, _version);
            Assert.AreEqual(0, _neutrals.Count);
            Assert.IsFalse(Calls.Contains("product.write"));
            Assert.IsFalse(Calls.Contains("master.read"));
            Assert.AreEqual(0, Session.ConnectionStub.TransactionStub.CommitCount);
            Assert.AreEqual(1, Session.ConnectionStub.TransactionStub.RollbackCount);
            AssertTransactionReleased();
        }

        /// <summary>只在指定写入次数返回受控失败或抛技术异常，供真实服务决定分类和回滚。</summary>
        private RepositoryWriteResult? WriteFailure(Guid uid, int? version)
        {
            if (WriteCount != FailWriteNumber) return null;
            if (ThrowOnWriteFailure) throw new InvalidOperationException("受控技术异常");
            return RepositoryWriteResult.Fail(uid, version, WriteErrorCode, "受控写入失败");
        }

        /// <summary>校验父引用使用更新锁且处于同一事务，并返回受控有效记录集。</summary>
        private object CategoryCall(string method, object?[] args)
        {
            Assert.IsNotNull(Session.Transaction);
            if (method == "GetAllAsync") return Task.FromResult<IReadOnlyList<ProductCategory>>(CategoryExists ? [new ProductCategory { Uid = _categoryId }] : []);
            Assert.AreEqual("GetByIdAsync", method);
            Assert.AreEqual(_categoryId, args[0]);
            Assert.AreEqual(true, args[1]);
            Calls.Add("category");
            return Task.FromResult<ProductCategory?>(CategoryExists ? new ProductCategory { Uid = _categoryId } : null);
        }

        /// <summary>校验供应商及颜色引用仍使用更新锁且处于同一事务。</summary>
        private object ReferenceCall<T>(string method, object?[] args, string name, Guid id, T[] rows)
        {
            Assert.AreEqual("GetByIdListAsync", method);
            Assert.IsNotNull(Session.Transaction);
            Assert.AreEqual(true, args[1]);
            CollectionAssert.AreEqual(new[] { id }, ((IEnumerable<Guid>)args[0]!).ToArray());
            Calls.Add(name);
            return Task.FromResult<IReadOnlyList<T>>(rows);
        }

        /// <summary>提供头部版本、编码占用和更新回执，记录真实头部加锁位置。</summary>
        private object MasterCall(string method, object?[] args)
        {
            Assert.IsNotNull(Session.Transaction);
            switch (method)
            {
                case "GetByIdAsync":
                    Assert.AreEqual(_masterId, args[0]);
                    Assert.AreEqual(true, args[1]);
                    Calls.Add("master.lock");
                    return Task.FromResult<ProductMaster?>(new ProductMaster
                    {
                        Uid = _masterId, Version = _version, CategoryId = _categoryId, IsDeleted = MasterDeleted
                    });
                case "GetDetailHeaderAsync":
                    Assert.AreEqual(_masterId, args[0]);
                    Calls.Add((bool)args[1]! ? "master.lock" : "master.read");
                    return Task.FromResult<ProductMasterDetailDto?>(new ProductMasterDetailDto
                    {
                        Uid = _masterId, Version = _version, Master = CreateRequest(false).Master
                    });
                case "GetByMasterSkusAsync":
                    Assert.AreEqual(false, args[1], "编码占用查询不得在自身主型号锁后反向锁定其他主型号。");
                    Calls.Add((bool)args[1]! ? "master.code.lock" : "master.code.read");
                    return Task.FromResult<IReadOnlyList<ProductMaster>>([]);
                case "GetByIdListAsync":
                    var observedIds = ((IEnumerable<Guid>)args[0]!).ToArray();
                    // 详情新增的锁前观察只读旧分类；不计为主型号锁，不削弱既有锁顺序断言。
                    if (observedIds.Length > 0)
                    {
                        Assert.AreEqual(false, args[1]);
                        CollectionAssert.AreEqual(new[] { _masterId }, observedIds);
                        return Task.FromResult<IReadOnlyList<ProductMaster>>([new ProductMaster { Uid = _masterId, Version = _version, CategoryId = _categoryId }]);
                    }
                    return Task.FromResult<IReadOnlyList<ProductMaster>>([]);
                case "AddAsync":
                    Calls.Add("master.write");
                    WriteCount++;
                    var createdId = Guid.NewGuid();
                    var insertFailure = WriteFailure(createdId, null);
                    if (insertFailure is not null) return Task.FromResult(insertFailure);
                    WrittenMasters.Add((ProductMaster)args[0]!);
                    _createdMasters.Add(createdId);
                    SuccessfulWrites++;
                    return Task.FromResult(RepositoryWriteResult.Ok(createdId, null, 1));
                case "UpdateAsync":
                    var master = (ProductMaster)args[0]!;
                    Assert.AreEqual(_masterId, master.Uid);
                    Assert.AreEqual(3, master.Version);
                    Calls.Add("master.write");
                    WriteCount++;
                    var updateFailure = WriteFailure(master.Uid, master.Version);
                    if (updateFailure is not null) return Task.FromResult(updateFailure);
                    WrittenMasters.Add(master);
                    SuccessfulWrites++;
                    return Task.FromResult(RepositoryWriteResult.Ok(_masterId, master.Version, ++_version));
                case "DeleteAsync":
                    Assert.AreEqual(_masterId, args[0]);
                    Assert.AreEqual(_version, args[1]);
                    Assert.AreEqual(true, args[2]);
                    Calls.Add("master.delete");
                    WriteCount++;
                    MasterDeleted = true;
                    SuccessfulWrites++;
                    return Task.FromResult(RepositoryWriteResult.Ok(_masterId, _version, ++_version));
                default:
                    throw new AssertFailedException($"未预期的主型号仓储调用：{method}。");
            }
        }

        /// <summary>提供聚合明细和空编码占用集，新增后让服务重新读取到保存结果。</summary>
        private object NeutralCall(string method, object?[] args)
        {
            Assert.IsNotNull(Session.Transaction);
            switch (method)
            {
                case "SoftDeleteByProductMasterIdAsync":
                    Assert.AreEqual(_masterId, args[0]);
                    Calls.Add("neutral.delete");
                    _neutrals.Clear();
                    return Task.FromResult(RepositoryWriteResult.Ok(_masterId, null, null));
                case "GetByProductMasterIdAsync":
                    Assert.AreEqual(_masterId, args[0]);
                    Calls.Add((bool)args[1]! ? "neutral.lock" : "neutral.read");
                    return Task.FromResult<IReadOnlyList<ProductNeutralDto>>(_neutrals.ToArray());
                case "GetByProductCodesAsync":
                    Assert.AreEqual(true, args[1]);
                    Calls.Add("neutral.code.lock");
                    return Task.FromResult<IReadOnlyList<ProductNeutral>>([]);
                case "AddAsync":
                    var neutral = (ProductNeutral)args[0]!;
                    Assert.AreEqual(_masterId, neutral.ProductMasterId);
                    Calls.Add("neutral.write");
                    WriteCount++;
                    var failure = WriteFailure(neutral.Uid, null);
                    if (failure is not null) return Task.FromResult(failure);
                    SuccessfulWrites++;
                    var id = Guid.NewGuid();
                    _neutrals.Add(new ProductNeutralDto
                    {
                        Uid = id, Version = 1, ProductMasterId = _masterId,
                        Data = new ProductNeutralWriteDto { ProductCode = neutral.ProductCode, ColorId = neutral.ColorId }
                    });
                    return Task.FromResult(RepositoryWriteResult.Ok(id, 0, 1));
                default:
                    throw new AssertFailedException($"未预期的中性产品仓储调用：{method}。");
            }
        }

        /// <summary>只允许读取关联物料身份；主型号保存发生任何物料写入都立即失败。</summary>
        private object ProductCall(string method, object?[] args)
        {
            if (method == "GetIdsByProductMasterIdsAsync")
                return Task.FromResult<IReadOnlyList<Guid>>(LinkedProduct is { ProductMasterId: { } masterId } linked
                    && ((IEnumerable<Guid>)args[0]!).Contains(masterId) ? [linked.Uid] : []);
            throw new AssertFailedException($"主型号保存不应写入物料：{method}。");
        }
    }

    /// <summary>创建仅响应夹具声明调用的仓储代理，未预期调用会使真实用例失败。</summary>
    private static T Proxy<T>(Func<string, object?[], object> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, RepositoryProxy>();
        ((RepositoryProxy)(object)proxy).Call = call;
        return proxy;
    }

    /// <summary>将仓储接口调用转发到受控行为实现，保留真实服务的执行路径。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object> Call { get; set; } = null!;

        /// <summary>返回对应仓储方法的异步结果，不访问数据库。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args ?? []);
    }

    /// <summary>通过 Core 会话契约持有测试事务，不依赖具体数据库提供者。</summary>
    private sealed class SessionStub : IDbSession, IDatabaseExceptionClassifier
    {
        public ConnectionStub ConnectionStub { get; } = new();
        public IDbConnection Connection => ConnectionStub;
        public IDbTransaction? Transaction { get; set; }
        public IDatabaseExceptionClassifier Errors => this;

        /// <summary>该夹具不产生数据库异常，意外异常保持未知分类。</summary>
        public DatabaseFailureKind Classify(Exception exception) => DatabaseFailureKind.Unknown;
    }

    /// <summary>仅支持开启受控事务，禁止命令创建以确保测试不会执行 SQL。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public TransactionStub TransactionStub { get; } = new();
        public Action OnBegin { get; set; } = () => { };
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "offline";
        public ConnectionState State => ConnectionState.Open;

        /// <summary>为真实服务的事务边界返回唯一受控事务。</summary>
        public IDbTransaction BeginTransaction() { OnBegin(); return TransactionStub; }
        public IDbTransaction BeginTransaction(IsolationLevel il) => BeginTransaction();
        /// <summary>内存连接没有需要关闭的外部资源。</summary>
        public void Close() { }
        /// <summary>内存连接始终可用，打开操作无副作用。</summary>
        public void Open() { }
        /// <summary>内存连接没有需要释放的外部资源。</summary>
        public void Dispose() { }
        /// <summary>禁止切换真实数据库，防止夹具越过离线边界。</summary>
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>禁止创建 SQL 命令，确保真实服务只通过受控仓储访问数据。</summary>
        public IDbCommand CreateCommand() => throw new NotSupportedException();
    }

    /// <summary>记录事务生命周期并触发受控状态快照回滚，不模拟数据库约束或锁机制。</summary>
    private sealed class TransactionStub : IDbTransaction
    {
        public Action OnRollback { get; set; } = () => { };
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public int DisposeCount { get; private set; }
        public IDbConnection Connection => throw new NotSupportedException();
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        /// <summary>记录服务提交次数。</summary>
        public void Commit() => CommitCount++;
        /// <summary>记录服务回滚次数。</summary>
        public void Rollback() { RollbackCount++; OnRollback(); }
        /// <summary>记录服务释放次数。</summary>
        public void Dispose() => DisposeCount++;
    }
}
