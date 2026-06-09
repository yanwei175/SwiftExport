using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using System.Text;

namespace SwiftExport.Application.Services.Customers;

/// <summary>
/// 客户产品服务。
/// 因 CustomerProduct 实体与 CustomerProductDto 不对称（DTO 含联表字段），
/// 不继承 BaseService，写操作手动映射实体字段，读操作走仓储联表查询直接返回 DTO。
/// </summary>
public class CustomerProductServices(
    ICustomerRepository customerRepository,
    ICustomerProductRepository customerProductRepository,
    ServiceBaseContext context) : ICustomerProductService
{
    private readonly ICustomerProductRepository _repo = customerProductRepository;
    private readonly ICustomerRepository _customerRepository = customerRepository;
    private readonly DbSession _session = context.Session;
    private ILogger? _loggerInstance;
    private ILogger Logger => _loggerInstance ??= context.LoggerFactory.CreateLogger<CustomerProductServices>();

    // ============================================================
    // 读操作（联表查询，直接返回 DTO，字段完整）
    // ============================================================

    /// <summary>按 ID 联表查询单条（含 CustomerCode / DescriptionEn / CategoryName 等）</summary>
    public async Task<ServiceResult<CustomerProductDto>> GetByIdAsync(Guid id)
    {
        var dto = await _repo.GetDtoByIdAsync(id);
        if (dto == null)
        {
            Logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的客户产品", id);
            return ServiceResult<CustomerProductDto>.Fail("未找到数据");
        }
        return ServiceResult<CustomerProductDto>.Ok(dto);
    }

    /// <summary>按客户料号 + 客户 ID 精确查询（调用仓储联表版本确保字段完整）</summary>
    public async Task<ServiceResult<CustomerProductDto>> GetByCustomerSkuAsync(string customerSku, Guid customerId)
    {
        // 先拿实体拿到 Id，再走联表查询保证 DTO 字段完整
        var entity = await _repo.GetByCustomerSkuAsync(customerSku, customerId);
        if (entity == null)
        {
            Logger.LogWarning("查询失败：客户 {CustomerId} 下未找到料号 {CustomerSku}", customerId, customerSku);
            return ServiceResult<CustomerProductDto>.Fail($"未找到客户产品型号为 {customerSku} 的产品");
        }

        var dto = await _repo.GetDtoByIdAsync(entity.Uid);
        return ServiceResult<CustomerProductDto>.Ok(dto!);
    }

    /// <summary>联表分页查询</summary>
    public async Task<ServiceResult<PagedResultDto<CustomerProductDto>>> GetPagedCustomerProductAsync(ProductSearchRequest req)
    {
        var criteria = new QueryCriteria();

        // 批量关键字搜索（换行 / 逗号分隔）
        if (req.BatchKeywords != null && req.BatchKeywords.Count > 0)
        {

            foreach (var keyword in req.BatchKeywords) 
            {
                // (Sku IN (...) OR CustomerSku IN (...))
                criteria.AddOrGroup(
                    ("Sku", 查询操作符.包含某值, keyword),
                     ("CustomerPdDescription", 查询操作符.模糊匹配, keyword),
                    ("CustomerSku", 查询操作符.包含某值, keyword));
            }
        }

        // 精确条件
        if (req.CustomerId != Guid.Empty)
            criteria.Add("CustomerId", 查询操作符.等于, req.CustomerId);

        if (req.CustomerIds != null && req.CustomerIds.Count > 0)
            criteria.Add("CustomerId", 查询操作符.包含某值, req.CustomerIds);

        if (req.CategoryIds != null && req.CategoryIds.Count > 0)
            criteria.Add("CategoryId", 查询操作符.包含某值, req.CategoryIds);

        int pageIndex = req.PageIndex > 0 ? req.PageIndex : 1;
        int pageSize = req.PageSize > 0 ? req.PageSize : 50;

        var paged = await _repo.GetPagedCustomerProductAsync(criteria, pageIndex, pageSize, "cp.CreatedDate DESC");

        return ServiceResult<PagedResultDto<CustomerProductDto>>.Ok(new PagedResultDto<CustomerProductDto>
        {
            Items = paged.Items,
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        });
    }

    /// <summary>获取所有有产品记录的客户列表</summary>
    public async Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllHaveProductCustomerListAsync()
    {
        var ids = await _repo.GetAllCustomerIdListAsync();
        if (ids == null || ids.Count == 0)
            return ServiceResult<IReadOnlyList<CustomerDto>>.Fail("暂无客户产品记录");

        var list = await _customerRepository.GetByIdListAsync(ids.Select(x => Guid.Empty));
        if (list == null || list.Count == 0)
            return ServiceResult<IReadOnlyList<CustomerDto>>.Fail("未找到对应客户");

        return ServiceResult<IReadOnlyList<CustomerDto>>.Ok(list.Adapt<IReadOnlyList<CustomerDto>>());
    }

    // ============================================================
    // 写操作（手动映射 DTO → 实体，只取实体字段，联表字段忽略）
    // ============================================================

    /// <summary>
    /// 批量保存（新增 / 修改 / 删除），事务保护。
    /// DTO → Entity 手动映射，只写 CustomerProduct 表的真实列，
    /// 联表字段（CustomerCode / DescriptionEn / CategoryName 等）不会写入。
    /// </summary>
    public async Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<CustomerProductDto> batchReq)
    {
        Logger.LogInformation(
            "开始执行 [CustomerProduct] 批量保存事务，计划新增: {AddCount}，修改: {UpdateCount}，删除: {DeleteCount}",
            batchReq.AddList?.Count ?? 0, batchReq.UpdateList?.Count ?? 0, batchReq.DeleteIdList?.Count ?? 0);

        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new BatchResultDto();

        try
        {
            var errorMsg = new StringBuilder();

            // ── 新增 ──────────────────────────────────────────────
            if (batchReq.AddList != null && batchReq.AddList.Count > 0)
            {
                // 业务校验
                var addError = await ValidateBeforeAddAsync(batchReq.AddList);
                if (!string.IsNullOrEmpty(addError))
                    errorMsg.AppendLine(addError);

                if (errorMsg.Length == 0)
                {
                    // 手动映射：只取 CustomerProduct 实体字段，联表字段丢弃
                    var addEntities = batchReq.AddList.Select(dto => new CustomerProduct
                    {
                        CustomerId = dto.CustomerId,
                        CustomerCode= dto.CustomerCode,
                        SkuId = dto.SkuId == Guid.Empty ? null : dto.SkuId,
                        Sku = dto.Sku,
                        LinkSku = dto.LinkSku,
                        CustomerSku = dto.CustomerSku,
                        CustomerPdSeries = dto.CustomerPdSeries,
                        CustomerPdDescription = dto.CustomerPdDescription,
                        CustomerColorName = dto.CustomerColorName,
                        CustomerPdFeatures = dto.CustomerPdFeatures,
                        CustomerBarcode1 = dto.CustomerBarcode1,
                        CustomerBarcode2 = dto.CustomerBarcode2,
                        CustomerSkuUnit = dto.CustomerSkuUnit,
                        USDPrice = dto.USDPrice,
                        CNYPrice = dto.CNYPrice,
                        Currency = dto.Currency,
                        LeadTimeDays = dto.LeadTimeDays,
                        MOQ = dto.MOQ,
                        IsActive = dto.IsActive,
                        CreatedDate = DateTime.Now,
                        IsDeleted = false
                    }).ToList();

                    result.AddIdList = [.. await _repo.AddRangeAsync(addEntities)];
                    Logger.LogInformation("成功新增 {Count} 条客户产品", addEntities.Count);
                }
            }

            // ── 修改 ──────────────────────────────────────────────
            if (batchReq.UpdateList != null && batchReq.UpdateList.Count > 0)
            {
                // 基础校验：Id 必须有效
                var invalidIds = batchReq.UpdateList.Where(x => x.Uid == Guid.Empty).ToList();
                if (invalidIds.Count > 0)
                    errorMsg.AppendLine("修改列表中包含无效 Uid（Uid == Guid.Empty），请检查");

                if (errorMsg.Length == 0)
                {
                    var updateError = await ValidateBeforeUpdateAsync(batchReq.UpdateList);
                    if (!string.IsNullOrEmpty(updateError))
                        errorMsg.AppendLine(updateError);
                }

                if (errorMsg.Length == 0)
                {
                    foreach (var dto in batchReq.UpdateList)
                    {
                        // 先从数据库取现有实体，再只覆盖可编辑字段
                        var existing = await _repo.GetByIdAsync(dto.Uid);
                        if (existing == null)
                        {
                            errorMsg.AppendLine($"Uid {dto.Uid} 的客户产品不存在，无法修改");
                            break;
                        }

                        // 手动赋值：只更新 CustomerProduct 表自身的字段
                        existing.CustomerId = dto.CustomerId;
                        existing.SkuId = dto.SkuId == Guid.Empty ? null : dto.SkuId;
                        existing.Sku = dto.Sku;
                        existing.LinkSku = dto.LinkSku;
                        existing.CustomerSku = dto.CustomerSku;
                        existing.CustomerPdSeries = dto.CustomerPdSeries;
                        existing.CustomerPdDescription = dto.CustomerPdDescription;
                        existing.CustomerColorName = dto.CustomerColorName;
                        existing.CustomerPdFeatures = dto.CustomerPdFeatures;
                        existing.CustomerBarcode1 = dto.CustomerBarcode1;
                        existing.CustomerBarcode2 = dto.CustomerBarcode2;
                        existing.CustomerSkuUnit = dto.CustomerSkuUnit;
                        existing.USDPrice = dto.USDPrice;
                        existing.CNYPrice = dto.CNYPrice;
                        existing.Currency = dto.Currency;
                        existing.LeadTimeDays = dto.LeadTimeDays;
                        existing.MOQ = dto.MOQ;
                        existing.IsActive = dto.IsActive;

                        await _repo.UpdateAsync(existing);
                        result.UpdateIdList.Add(existing.Uid);
                    }

                    if (errorMsg.Length == 0)
                        Logger.LogInformation("成功修改 {Count} 条客户产品", batchReq.UpdateList.Count);
                }
            }

            // ── 删除（硬删除）──────────────────────────────────────
            if (batchReq.DeleteIdList != null && batchReq.DeleteIdList.Count > 0)
            {
                var deleteError = await ValidateBeforeDeleteAsync(batchReq.DeleteIdList);
                if (!string.IsNullOrEmpty(deleteError))
                    errorMsg.AppendLine(deleteError);

                if (errorMsg.Length == 0)
                {
                    await _repo.DeleteRangeAsync(batchReq.DeleteIdList);
                    result.DeleteIdList = batchReq.DeleteIdList;
                    Logger.LogInformation("成功删除 {Count} 条客户产品", batchReq.DeleteIdList.Count);
                }
            }

            // 有错误则回滚
            if (errorMsg.Length > 0)
            {
                _session.Transaction.Rollback();
                return ServiceResult<BatchResultDto>.Fail(errorMsg.ToString().TrimEnd());
            }

            _session.Transaction.Commit();
            return ServiceResult<BatchResultDto>.Ok(result);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            Logger.LogError(ex, "CustomerProduct 批量保存事务失败，已回滚");
            throw;
        }
        finally
        {
            _session.Transaction.Dispose();
        }
    }

    // ============================================================
    // 业务校验钩子（与原 BaseService Hook 设计一致）
    // ============================================================

    /// <summary>新增前校验：客户 ID 必填、料号不能为空、料号不能重复</summary>
    private async Task<string?> ValidateBeforeAddAsync(List<CustomerProductDto> dtos)
    {
        var codes = new List<string>();
        foreach (var dto in dtos)
        {
            if (dto.CustomerId == Guid.Empty)
                return "没有指定客户";
            if (string.IsNullOrWhiteSpace(dto.CustomerSku))
                return "客户产品型号不能为空";
            if (string.IsNullOrWhiteSpace(dto.CustomerCode))
                return "客户代码不能为空";
            codes.Add(dto.CustomerSku);
        }

        var existedCodes = await _repo.GetExistingCustomerSkuListAsync(codes, dtos.First().CustomerId);
        if (existedCodes.Any())
            return $"客户产品型号重复冲突，拒绝新增: {string.Join(", ", existedCodes)}";

        return null;
    }

    /// <summary>修改前校验：客户 ID 必填、料号不能为空</summary>
    private async Task<string?> ValidateBeforeUpdateAsync(List<CustomerProductDto> dtos)
    {
        foreach (var dto in dtos)
        {
            if (dto.CustomerId == Guid.Empty)
                return "客户产品必须指定客户";
            if (string.IsNullOrWhiteSpace(dto.CustomerSku))
                return "客户产品型号不能为空";
            if (string.IsNullOrWhiteSpace(dto.CustomerCode))
                return "客户代码不能为空";
        }
        return await Task.FromResult<string?>(null);
    }

    /// <summary>删除前校验（预留扩展点，默认放行）</summary>
    private async Task<string?> ValidateBeforeDeleteAsync(List<Guid> ids)
        => await Task.FromResult<string?>(null);
}