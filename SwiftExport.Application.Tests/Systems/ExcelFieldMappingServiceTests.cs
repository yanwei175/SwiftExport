using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Systems.ExcelFieldMappings;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.ExcelFieldMappings;
using SwiftExport.Core.Interfaces.Systems.ExcelFieldMappings;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>用内存仓储验证映射服务的输入、别名与乐观并发边界，不模拟 SQL Server。</summary>
[TestClass]
public sealed class ExcelFieldMappingServiceTests
{
    /// <summary>多种表头可指向同一字段；同组规范化别名冲突，跨组可重用。</summary>
    [TestMethod]
    public async Task Create_AllowsAliasesAndCrossGroupReuse_BlocksSameGroupDuplicate()
    {
        var service = new ExcelFieldMappingService(new RepositoryStub());
        var first = await service.CreateAsync(Create("SalesOrder", "  Ｃｕｓｔｏｍｅｒ  sku ", "Line.CustomerSku"), default);
        Assert.IsTrue(first.Success);
        Assert.AreEqual("CUSTOMER SKU", SwiftExport.Contracts.Common.ExcelHeaderNormalizer.Normalize(first.Data.ExcelHeader));
        Assert.IsTrue((await service.CreateAsync(Create("SalesOrder", "客户型号", "Line.CustomerSku"), default)).Success);
        var duplicate = await service.CreateAsync(Create("SalesOrder", "customer sku", "Line.CustomerSku"), default);
        Assert.AreEqual(ExcelFieldMappingErrorCodes.DuplicateHeader, duplicate.Error.Code);
        Assert.IsTrue((await service.CreateAsync(Create("PackingList", "customer sku", "Line.CustomerSkuSnapshot"), default)).Success);
    }

    /// <summary>未知分组、身份字段和空表头均不能入库。</summary>
    [TestMethod]
    public async Task Create_RejectsUnknownFieldsAndEmptyHeader()
    {
        var service = new ExcelFieldMappingService(new RepositoryStub());
        foreach (var request in new[]
        {
            Create("Unknown", "SKU", "Line.CustomerSku"), Create("SalesOrder", "SKU", "Line.Uid"),
            Create("SalesOrder", " \t ", "Line.CustomerSku")
        })
            Assert.AreEqual(ServiceErrorCodes.Validation, (await service.CreateAsync(request, default)).Error.Code);
    }

    /// <summary>旧版本不能覆盖配置或删除配置，更新成功只增加一次版本。</summary>
    [TestMethod]
    public async Task UpdateAndDelete_UseExpectedVersion()
    {
        var service = new ExcelFieldMappingService(new RepositoryStub());
        var created = (await service.CreateAsync(Create("PurchaseOrder", "数量", "Line.Quantity"), default)).Data;
        var changed = await service.UpdateAsync(created.Uid, new UpdateExcelFieldMappingRequest
        {
            ExpectedVersion = created.Version, ExcelHeader = "采购数量", TargetFieldKey = "Line.Quantity", IsEnabled = false
        }, default);
        Assert.IsTrue(changed.Success);
        Assert.AreEqual(created.Version + 1, changed.Data.Version);
        var stale = await service.UpdateAsync(created.Uid, new UpdateExcelFieldMappingRequest
        {
            ExpectedVersion = created.Version, ExcelHeader = "旧值", TargetFieldKey = "Line.Quantity"
        }, default);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, stale.Error.Code);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, (await service.DeleteAsync(created.Uid, created.Version, default)).Error.Code);
        Assert.IsTrue((await service.DeleteAsync(created.Uid, changed.Data.Version, default)).Success);
    }

    private static CreateExcelFieldMappingRequest Create(string group, string header, string field) => new()
    {
        GroupCode = group, ExcelHeader = header, TargetFieldKey = field, IsEnabled = true
    };

    /// <summary>仅模拟基础仓储公开契约中的单实体行为，供服务层规则测试使用。</summary>
    private sealed class RepositoryStub : IExcelFieldMappingRepository
    {
        private readonly List<ExcelFieldMapping> rows = [];

        /// <inheritdoc />
        public Task<IReadOnlyList<ExcelFieldMapping>> GetByGroupAsync(string groupCode, bool includeDisabled, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ExcelFieldMapping>>(rows.Where(row => !row.IsDeleted && row.GroupCode == groupCode && (includeDisabled || row.IsEnabled)).ToArray());
        /// <inheritdoc />
        public Task<ExcelFieldMapping?> GetByNormalizedHeaderAsync(string groupCode, string normalizedHeader, CancellationToken cancellationToken)
            => Task.FromResult(rows.FirstOrDefault(row => !row.IsDeleted && row.GroupCode == groupCode && row.NormalizedHeader == normalizedHeader));
        /// <inheritdoc />
        public Task<ExcelFieldMapping?> GetByIdAsync(Guid uid, bool withUpdLock = false) => Task.FromResult(rows.FirstOrDefault(row => !row.IsDeleted && row.Uid == uid));
        /// <inheritdoc />
        public Task<RepositoryWriteResult> AddAsync(ExcelFieldMapping entity)
        {
            entity.Uid = Guid.NewGuid(); entity.Version = 1;
            rows.Add(entity);
            return Task.FromResult(RepositoryWriteResult.Ok(entity.Uid, null, 1));
        }
        /// <inheritdoc />
        public Task<RepositoryWriteResult> UpdateAsync(ExcelFieldMapping entity)
        {
            entity.Version++;
            return Task.FromResult(RepositoryWriteResult.Ok(entity.Uid, entity.Version - 1, entity.Version));
        }
        /// <inheritdoc />
        public Task<RepositoryWriteResult> DeleteAsync(Guid uid, int version, bool softDelete = true)
        {
            var row = rows.FirstOrDefault(item => item.Uid == uid && item.Version == version && !item.IsDeleted);
            if (row is null) return Task.FromResult(RepositoryWriteResult.Fail(uid, version, "CONCURRENCY_CONFLICT", "冲突"));
            row.IsDeleted = true;
            return Task.FromResult(RepositoryWriteResult.Ok(uid, version, version));
        }
        /// <inheritdoc />
        public Task<bool> ExistsIdAsync(Guid uid) => Task.FromResult(rows.Any(row => row.Uid == uid));
        /// <inheritdoc />
        public Task<IReadOnlyList<Guid>> GetExistingIdListAsync(IEnumerable<Guid> uidList, bool withUpdLock = false)
            => Task.FromResult<IReadOnlyList<Guid>>(rows.Select(row => row.Uid).Intersect(uidList).ToArray());
        /// <inheritdoc />
        public Task<IReadOnlyList<ExcelFieldMapping>> GetByIdListAsync(IEnumerable<Guid> uidList, bool withUpdLock = false)
            => Task.FromResult<IReadOnlyList<ExcelFieldMapping>>(rows.Where(row => uidList.Contains(row.Uid)).ToArray());
        /// <inheritdoc />
        public Task<ExcelFieldMapping?> GetSingleEntityAsync() => Task.FromResult(rows.FirstOrDefault());
        /// <inheritdoc />
        public Task<IReadOnlyList<ExcelFieldMapping>> GetAllAsync() => Task.FromResult<IReadOnlyList<ExcelFieldMapping>>(rows.ToArray());
    }
}
