using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Products.ProductMasters;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>复用事务可观察夹具验证物料详情、独立选值及旧列表保存的兼容边界，不替代真实 PostgreSQL 并发验证。</summary>
public sealed partial class ProductBomSaveBoundaryTests
{
    /// <summary>详情只覆盖写 DTO，保留主型号、启停和 BOM；可空价格与颜色及单品尺寸不能被旧 DTO 压成默认值。</summary>
    [TestMethod]
    public async Task Detail_SavePreservesReadOnlyFieldsAndNullableValues()
    {
        var fixture = DetailFixture(out var product, out var first, out _);
        product.IsActive = false;
        product.BomNo = "DETAIL-BOM";
        product.LinkSku = "仅属于物料的型号";
        product.BomVersion = 8;
        var request = DetailRequest(product, [first]);
        request.Data.Note = "详情备注";
        request.Data.UnitWeight = 2.25m;
        request.Data.UnitCBM = 0.007m;
        request.Data.CNYPrice = null;
        request.Data.USDPrice = null;
        request.Data.ColorId = null;
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsTrue(result.Success, result.Message);
        var saved = fixture.Products[product.Uid];
        Assert.AreEqual(product.ProductMasterId, saved.ProductMasterId);
        Assert.AreEqual(product.LinkSku, saved.LinkSku);
        Assert.IsFalse(saved.IsActive);
        Assert.AreEqual("DETAIL-BOM", saved.BomNo);
        Assert.AreEqual(8, saved.BomVersion);
        Assert.AreEqual(2.25m, saved.UnitWeight);
        Assert.AreEqual(0.007m, saved.UnitCBM);
        Assert.IsNull(saved.CNYPrice);
        Assert.IsNull(saved.USDPrice);
        Assert.IsNull(saved.ColorId);
        Assert.AreEqual("详情备注", saved.Note);
        Assert.AreEqual(6, result.Data.Detail.Version);
        CollectionAssert.AreEqual(new[] { first }, result.Data.Detail.SelectedOptionIds);
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>省略选择保留、空集合清空、非空最终替换；相同对象身份的主型号选择保持不变。</summary>
    [TestMethod]
    [DataRow("keep")]
    [DataRow("clear")]
    [DataRow("replace")]
    public async Task Detail_SelectionProtocolIsScopedToMaterial(string mode)
    {
        var fixture = DetailFixture(out var product, out var first, out var second);
        fixture.Selection(product.Uid, first);
        var other = fixture.Selection(product.Uid, first, ProductAttributeOwnerType.ProductMaster);
        var selected = mode == "keep" ? null : mode == "clear" ? new List<Guid>() : [second];
        var result = await fixture.Service.SaveDetailAsync(DetailRequest(product, selected));
        Assert.IsTrue(result.Success, result.Message);
        var actual = fixture.Selections.Values.Where(row => !row.IsDeleted && row.OwnerType == ProductAttributeOwnerType.Product)
            .Select(row => row.OptionId).ToArray();
        var expected = mode == "keep" ? new[] { first } : mode == "clear" ? [] : new[] { second };
        CollectionAssert.AreEquivalent(expected, actual);
        Assert.IsFalse(fixture.Selections[other.Uid].IsDeleted);
        Assert.AreEqual(6, fixture.Products[product.Uid].Version, "只改标签也推进物料版本。");
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>服务端拒绝重复、无效、单选超量、无模板及失效模板，基础资料均不写入。</summary>
    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("missing")]
    [DataRow("single")]
    [DataRow("no-profile")]
    [DataRow("disabled-profile")]
    public async Task Detail_InvalidSelectionRejectsAllWrites(string scenario)
    {
        var fixture = DetailFixture(out var product, out var first, out var second);
        var selected = new List<Guid> { first };
        switch (scenario)
        {
            case "duplicate": selected.Add(first); break;
            case "missing": selected = [Guid.NewGuid()]; break;
            case "single": fixture.Groups[0].SelectionMode = ProductAttributeSelectionMode.Single; selected.Add(second); break;
            case "no-profile": fixture.Masters[product.ProductMasterId!.Value].ProductAttributeProfileId = null; break;
            case "disabled-profile": fixture.Profiles.Values.Single().IsEnabled = false; break;
        }
        var before = fixture.State();
        var request = DetailRequest(product, selected);
        request.Data.Note = "不得写入";
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.HasCount(0, fixture.Writes);
        Assert.AreEqual(before, fixture.State());
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>没有来源主型号的普通物料仍可编辑基础资料，空标签集合不会创造模板或选择。</summary>
    [TestMethod]
    public async Task Detail_NoSourceMasterAllowsEmptySelection()
    {
        var fixture = new Fixture();
        var product = fixture.Product("UNLINKED");
        var request = DetailRequest(product, []);
        request.Data.UnitWeight = 3m;
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsTrue(result.Success, result.Message);
        Assert.IsNull(result.Data.Detail.ProductAttributeProfileId);
        Assert.AreEqual(3m, fixture.Products[product.Uid].UnitWeight);
        Assert.HasCount(0, fixture.Selections);
    }

    /// <summary>后序标签写入失败恢复已经更新的物料、旧选择及其他对象关系。</summary>
    [TestMethod]
    public async Task Detail_SelectionWriteFailureRollsBackMaterialAndSelections()
    {
        var fixture = DetailFixture(out var product, out var first, out var second);
        fixture.Selection(product.Uid, first);
        var before = fixture.State();
        var oldSelections = fixture.Selections.Values.Select(Clone).ToArray();
        fixture.FailSelectionWrite = true;
        var request = DetailRequest(product, [second]);
        request.Data.Note = "应回滚";
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(before, fixture.State());
        CollectionAssert.AreEquivalent(oldSelections.Select(row => row.OptionId).ToArray(),
            fixture.Selections.Values.Where(row => !row.IsDeleted).Select(row => row.OptionId).ToArray());
        fixture.AssertTransaction(0, 1);
    }

    /// <summary>旧物料版本在任何写入之前拒绝，并返回明确版本冲突。</summary>
    [TestMethod]
    public async Task Detail_VersionConflictDoesNotWrite()
    {
        var fixture = DetailFixture(out var product, out var first, out _);
        var request = DetailRequest(product, [first]);
        request.ExpectedVersion--;
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.HasCount(0, fixture.Writes);
        fixture.AssertTransaction(0, 1);
    }

    /// <summary>详情锁顺序明确为模板、组、主型号、物料；后续公共校验只能复用已取得的物料锁。</summary>
    [TestMethod]
    public async Task Detail_LocksSourceBeforeMaterial()
    {
        var fixture = DetailFixture(out var product, out var first, out _);
        var result = await fixture.Service.SaveDetailAsync(DetailRequest(product, [first]));
        Assert.IsTrue(result.Success, result.Message);
        CollectionAssert.AreEqual(new[] { "profile", "group", "master", "product" }, fixture.DetailLockTrace);
    }

    /// <summary>列表独立保存主型号和自身型号；未携带的单品重量和体积保持原值。</summary>
    [TestMethod]
    public async Task OrdinaryUpdate_PreservesFieldsMissingFromLegacyDto()
    {
        var fixture = DetailFixture(out var product, out _, out _);
        product.UnitWeight = 2m;
        product.UnitCBM = 0.125m;
        product.LinkSku = "不依赖中性产品的型号";
        var dto = Dto(product);
        dto.MasterSku = "手工型号文字";
        dto.Note = "列表修改";
        var result = await fixture.Entry.UpdateAsync(new() { Uid = product.Uid, Version = product.Version, Data = dto });
        Assert.IsTrue(result.Success, result.Message);
        var saved = fixture.Products[product.Uid];
        Assert.AreEqual(product.ProductMasterId, saved.ProductMasterId);
        Assert.AreEqual(2m, saved.UnitWeight);
        Assert.AreEqual(0.125m, saved.UnitCBM);
        Assert.AreEqual(product.LinkSku, saved.LinkSku);
    }

    /// <summary>主型号引用失效、产品型号没有中性产品匹配时，基础资料和既有标签仍可保存。</summary>
    [TestMethod]
    public async Task Detail_DanglingIndependentSourcesKeepSelectionsAndOtherFields()
    {
        var fixture = DetailFixture(out var product, out var first, out _);
        fixture.Selection(product.Uid, first);
        product.BomNo = "KEEP-BOM"; product.BomVersion = 2; product.UnitWeight = 2m;
        var request = DetailRequest(product, null);
        request.Data.ProductMasterId = Guid.NewGuid();
        request.Data.LinkSku = "未建立中性产品的自由型号";
        request.Data.Note = "失效来源仍可保存";
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsTrue(result.Success, result.Message);
        var saved = fixture.Products[product.Uid];
        Assert.AreEqual(request.Data.ProductMasterId, saved.ProductMasterId);
        Assert.AreEqual(request.Data.LinkSku, saved.LinkSku);
        Assert.AreEqual(request.Data.LinkSku, result.Data.Detail.LinkSku);
        Assert.AreEqual(request.Data.LinkSku, result.Data.Detail.Data.LinkSku);
        Assert.AreEqual("KEEP-BOM", saved.BomNo); Assert.AreEqual(2, saved.BomVersion);
        Assert.AreEqual(2m, saved.UnitWeight);
        CollectionAssert.AreEqual(new[] { first }, result.Data.Detail.SelectedOptionIds);
        Assert.AreEqual(6, saved.Version);
    }

    /// <summary>列表保存独立主型号和自由型号，明确清空不影响单品尺寸和其他物料资料。</summary>
    [TestMethod]
    public async Task OrdinaryUpdate_WritesAndClearsIndependentModel()
    {
        var fixture = DetailFixture(out var product, out _, out _);
        product.UnitWeight = 3m; product.UnitCBM = 0.05m;
        var dto = Dto(product);
        dto.ProductMasterId = Guid.NewGuid(); dto.LinkSku = "未匹配中性产品的物料型号";
        dto.Note = "主型号和物料型号独立保存";
        var changed = await fixture.Entry.UpdateAsync(new() { Uid = product.Uid, Version = product.Version, Data = dto });
        Assert.IsTrue(changed.Success, changed.Message);
        var saved = fixture.Products[product.Uid];
        Assert.AreEqual(dto.ProductMasterId, saved.ProductMasterId);
        Assert.AreEqual(dto.LinkSku, saved.LinkSku);
        var clear = Dto(saved); clear.ProductMasterId = null; clear.LinkSku = null;
        var cleared = await fixture.Entry.UpdateAsync(new() { Uid = saved.Uid, Version = saved.Version, Data = clear });
        Assert.IsTrue(cleared.Success, cleared.Message);
        var final = fixture.Products[product.Uid];
        Assert.IsNull(final.ProductMasterId); Assert.IsNull(final.LinkSku);
        Assert.AreEqual(3m, final.UnitWeight); Assert.AreEqual(0.05m, final.UnitCBM);
        Assert.AreEqual("主型号和物料型号独立保存", final.Note);
    }

    /// <summary>物料单项和批量删除只清理本类型选择，选择及物料删除同事务提交。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MaterialDelete_ClearsOnlyItsSelections(bool batch)
    {
        var fixture = DetailFixture(out var product, out var first, out _);
        var own = fixture.Selection(product.Uid, first);
        var other = fixture.Selection(product.Uid, first, ProductAttributeOwnerType.ProductNeutral);
        var item = new DeleteItemRequest { Uid = product.Uid, Version = product.Version };
        var success = batch
            ? (await fixture.Service.BatchSaveAsync(new() { DeleteItems = [item] })).Success
            : (await fixture.Entry.DeleteAsync(item)).Success;
        Assert.IsTrue(success);
        Assert.IsTrue(fixture.Selections[own.Uid].IsDeleted);
        Assert.IsFalse(fixture.Selections[other.Uid].IsDeleted);
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>提交确认丢失不能假称成功或自动再次保存，保持结果未知分类。</summary>
    [TestMethod]
    public async Task Detail_CommitUnknownOutcomeIsNotReportedAsSuccess()
    {
        var fixture = DetailFixture(out var product, out var first, out _);
        fixture.Session.ConnectionStub.FailCommitAfter = true;
        var result = await fixture.Service.SaveDetailAsync(DetailRequest(product, [first]));
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, result.Error.Code);
        Assert.HasCount(1, fixture.Writes);
        Assert.AreEqual(6, fixture.Products[product.Uid].Version);
        fixture.AssertTransaction(1, 1);
    }

    /// <summary>构造统一模板和两个组内选项，不预先运行服务校验。</summary>
    private static Fixture DetailFixture(out Product product, out Guid first, out Guid second)
    {
        var fixture = new Fixture();
        var profile = new SysDictData { Uid = Guid.NewGuid(), DictCode = DictCodes.ProductAttributeProfile, IsEnabled = true };
        fixture.Profiles.Add(profile.Uid, profile);
        var master = new ProductMaster { Uid = Guid.NewGuid(), MasterSku = "MASTER", ProductAttributeProfileId = profile.Uid };
        fixture.Masters.Add(master.Uid, master);
        product = fixture.Product("MATERIAL");
        product.ProductMasterId = master.Uid;
        first = Guid.NewGuid(); second = Guid.NewGuid();
        var group = new ProductAttributeGroupDto { Uid = Guid.NewGuid(), GroupCode = "INSTALL", GroupName = "安装方式", SelectionMode = ProductAttributeSelectionMode.Multiple };
        group.Options = [new ProductAttributeOptionDto { Uid = first, AttributeGroupId = group.Uid, OptionCode = "WALL", OptionName = "挂墙" },
            new ProductAttributeOptionDto { Uid = second, AttributeGroupId = group.Uid, OptionCode = "TOP", OptionName = "吸顶" }];
        fixture.Groups.Add(group);
        return fixture;
    }

    /// <summary>写 DTO 包含独立主型号身份与自由产品型号，BOM 与只读主型号编码不参与提交。</summary>
    private static SaveProductDetailRequest DetailRequest(Product product, List<Guid>? selection)
        => new() { ProductId = product.Uid, ExpectedVersion = product.Version,
            Data = product.Adapt<ProductDetailWriteDto>(), SelectedOptionIds = selection };

    /// <summary>只为物料详情场景提供真实接口的定义、来源和选择持久化替身。</summary>
    private sealed partial class Fixture
    {
        /// <summary>建立指定对象类型的既有选择，身份相同但类型不同的记录保持独立。</summary>
        public ProductAttributeSelection Selection(Guid ownerId, Guid optionId, ProductAttributeOwnerType ownerType = ProductAttributeOwnerType.Product)
        {
            var row = new ProductAttributeSelection { Uid = Guid.NewGuid(), OwnerType = ownerType, OwnerId = ownerId, OptionId = optionId };
            Selections.Add(row.Uid, row);
            return row;
        }

        /// <summary>主型号锁只记录接口顺序，不模拟 PostgreSQL 的锁强度。</summary>
        private object MasterCall(string name, object?[] args)
        {
            Record();
            if (name != nameof(IProductMasterRepository.GetByIdAsync)) throw new AssertFailedException("未授权主型号调用：" + name);
            if (args.Length > 1 && args[1] is true) DetailLockTrace.Add("master");
            var row = Masters.GetValueOrDefault((Guid)args[0]!);
            return Task.FromResult(row is null ? null : Clone(row));
        }

        /// <summary>所有选择读写包含对象类型，并遵循事务快照；故障注入发生在基础资料写入之后。</summary>
        private object AttributeCall(string name, object?[] args)
        {
            Record();
            switch (name)
            {
                case nameof(IProductAttributeRepository.GetProfilesAsync):
                    if (args[1] is true) DetailLockTrace.Add("profile");
                    var profileIds = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<SysDictData>>(Profiles.Values.Where(row => profileIds.Contains(row.Uid)).Select(Clone).ToArray());
                case nameof(IProductAttributeRepository.GetGroupIdsForOptionsAsync):
                    var optionIds = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<Guid>>(Groups.Where(group => group.Options.Any(option => optionIds.Contains(option.Uid))).Select(group => group.Uid).ToArray());
                case nameof(IProductAttributeRepository.LockGroupsForSelectionAsync):
                    DetailLockTrace.Add("group");
                    var groupIds = ((IEnumerable<Guid>)args[0]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<ProductAttributeGroup>>(Groups.Where(row => groupIds.Contains(row.Uid)).Select(row => row.Adapt<ProductAttributeGroup>()).ToArray());
                case nameof(IProductAttributeRepository.GetDefinitionsAsync):
                    return Task.FromResult<IReadOnlyList<ProductAttributeGroupDto>>(Profiles.ContainsKey((Guid)args[0]!) ? Groups.Select(Clone).ToArray() : []);
                case nameof(IProductAttributeRepository.GetSelectionsAsync):
                    var ownerType = (ProductAttributeOwnerType)args[0]!;
                    var owners = ((IEnumerable<Guid>)args[1]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<ProductAttributeSelection>>(Selections.Values.Where(row => !row.IsDeleted && row.OwnerType == ownerType && owners.Contains(row.OwnerId)).Select(Clone).ToArray());
                case nameof(IProductAttributeRepository.GetSelectionDetailsAsync):
                    var type = (ProductAttributeOwnerType)args[0]!;
                    var selectedOwners = ((IEnumerable<Guid>)args[1]!).ToHashSet();
                    return Task.FromResult<IReadOnlyList<ProductAttributeSelectionDto>>(Selections.Values.Where(row => !row.IsDeleted && row.OwnerType == type && selectedOwners.Contains(row.OwnerId))
                        .Select(row => new ProductAttributeSelectionDto { OwnerType = type, OwnerId = row.OwnerId, OptionId = row.OptionId }).ToArray());
                case nameof(IProductAttributeRepository.AddSelectionAsync):
                    var selection = Clone((ProductAttributeSelection)args[0]!);
                    if (FailSelectionWrite) return Task.FromResult(RepositoryWriteResult.Fail(selection.Uid, 1, ServiceErrorCodes.WriteFailed, "受控选择写入失败"));
                    Selections.Add(selection.Uid, selection);
                    return Task.FromResult(RepositoryWriteResult.Ok(selection.Uid, null, 1));
                case nameof(IProductAttributeRepository.DeleteSelectionAsync):
                    var removed = Selections[(Guid)args[0]!];
                    removed.IsDeleted = true;
                    return Task.FromResult(RepositoryWriteResult.Ok(removed.Uid, removed.Version, removed.Version));
                default: throw new AssertFailedException("未授权标签调用：" + name);
            }
        }
    }
}
