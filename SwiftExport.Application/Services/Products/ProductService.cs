using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Infrastructure.Data;
using System.Text;

namespace SwiftExport.Application.Services.Products;

public partial class ProductService(IProductRepository productRepository, ServiceBaseContext context, IProductCategoryRepository categoryRepository) : IProductService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<ProductService> _logger = context.LoggerFactory.CreateLogger<ProductService>();
    // 2. 将传入的特有 Repository 保存为当前类的私有只读字段
    private readonly IProductRepository _productRepository = productRepository;
    private readonly IProductCategoryRepository _categoryRepository = categoryRepository;

    public async Task<ServiceResult<IReadOnlyList<ProductDto>>> GetAllAsync()
    {
        return ServiceResult<IReadOnlyList<ProductDto>>.Fail("数据太多,不允许GetAll");
    }

    public async Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeListAsync()
    {
        // 使用特有的 _customerProductRepository 调用特有方法
        var list = await _productRepository.GetAllCustomerCodeListAsync();
        if (list == null || list.Count == 0)
            return ServiceResult<IReadOnlyList<string>>.Fail("空的客户代码集合");

        return ServiceResult<IReadOnlyList<string>>.Ok(list);
    }

    // 实现特有的业务逻辑：根据 SKU 查询
    public async Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku)
    {
        var entity = await _productRepository.GetBySkuAsync(sku);
        if (entity == null)
            return ServiceResult<ProductDto>.Fail($"未找到 SKU 为 {sku} 的产品");

        return ServiceResult<ProductDto>.Ok(entity.Adapt<ProductDto>());
    }

    public async Task<ServiceResult<PagedResultDto<ProductDto>>> GetPagedListAsync(ProductSearchRequest req)
    {
        // 1. 实例化底层查询对象
        var criteria = new QueryCriteria();


        if (req.BatchKeywords != null && req.BatchKeywords.Count > 0)
        {

            foreach (var keyword in req.BatchKeywords) 
            {
                // 场景 A：只匹配 SKU (性能最高，最常用)
                // criteria.Add("SKU", 查询操作符.模糊匹配, batchList);

                // 场景 B：匹配 SKU 或 客户型号 (更灵活)
                // 逻辑：(SKU IN (...) OR CSKU IN (...))
                criteria.AddOrGroup(
                    (nameof(Product.Sku), 查询操作符.模糊匹配, keyword),
                    (nameof(Product.DefaultCustomerSku), 查询操作符.模糊匹配, keyword)
                );
            }
        }


        // ==========================================
        // 处理精确匹配条件
        // ==========================================
        if (!string.IsNullOrWhiteSpace(req.ColorCodeUnique))
        {
            criteria.Add(nameof(ProductColor.ColorCodeUnique), 查询操作符.等于, req.ColorCodeUnique);
        }
        if (req.CustomerIds != null && req.CustomerIds.Count > 0)
        {
            criteria.Add(nameof(Customer.Uid), 查询操作符.包含某值, req.CustomerIds);
        }
        if (req.CustomerCodes != null && req.CustomerCodes.Count > 0)
        {
            criteria.Add(nameof(Product.DefaultCustomerCode), 查询操作符.包含某值, req.CustomerCodes);
        }
        if (req.CategoryId != Guid.Empty)
        {
            List<ProductCategory> ids = (await _categoryRepository.GetSubById(req.CategoryId)).ToList();
            if (req.CategoryIds == null) req.CategoryIds = new List<Guid>();
            req.CategoryIds.AddRange(ids.Select(c => c.Uid));
            req.CategoryIds.Add(req.CategoryId);
        }

        if (req.CategoryIds != null && req.CategoryIds.Count > 0)
        {
            criteria.Add(nameof(Product.CategoryId), 查询操作符.包含某值, req.CategoryIds);
        }

        else if (!string.IsNullOrWhiteSpace(req.CategoryName))
        {
            criteria.Add(nameof(ProductCategory.CategoryName), 查询操作符.等于, req.CategoryName);
        }
        
        if (!string.IsNullOrWhiteSpace(req.MaterialType))
        {
            criteria.Add(nameof(Product.MaterialType), 查询操作符.等于, req.MaterialType);
        }

        // 4. 处理分页默认值保护
        int pageIndex = req.PageIndex > 0 ? req.PageIndex : 1;
        int pageSize = req.PageSize > 0 ? req.PageSize : 50;

        var paged = await _productRepository.GetPagedProductDtoAsync(criteria, pageIndex, pageSize, "ID DESC");

        return ServiceResult<PagedResultDto<ProductDto>>.Ok(new PagedResultDto<ProductDto>
        {
            Items = paged.Items,
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        });
    }


    #region 批量保存
    private async Task<string?> ValidateBeforeAddAsync(List<Product> entities)
    {
        var codes = new List<string>();
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.Sku))
                return "产品型号不能为空";
            codes.Add(item.Sku);
        }

        var existedCodes = await _productRepository.GetExistingSkuListAsync(codes);
        if (existedCodes.Any())
        {
            return $"以下产品型号已存在，拒绝新增: {string.Join(", ", existedCodes)}";
        }

        // 返回 null 代表一切正常，放行！
        return null;
    }

    private Task<string?> ValidateBeforeUpdateAsync(List<Product> entities)
    {
        var codes = new List<string>();
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.Sku))
                return Task.FromResult<string?>("产品型号不能为空");
            codes.Add(item.Sku);
        }

        // 返回 null 代表一切正常，放行！
        return Task.FromResult<string?>(null);
    }



    #endregion



    #region 显式基础读写

    public async Task<ServiceResult<ProductDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _productRepository.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, nameof(Product));
                return ServiceResult<ProductDto>.Fail("未找到数据");
            }

            return ServiceResult<ProductDto>.Ok(entity.Adapt<ProductDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<ProductDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PagedResultDto<ProductDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC")
    {
        try
        {
            if (_productRepository is not IPagedCriteriaRepository<Product> pagedRepository)
                return ServiceResult<PagedResultDto<ProductDto>>.Fail($"{nameof(Product)} 仓储未实现动态分页查询");

            var paged = await pagedRepository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
            return ServiceResult<PagedResultDto<ProductDto>>.Ok(new PagedResultDto<ProductDto>
            {
                Items = paged.Items.Adapt<List<ProductDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<ProductDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> AddAsync(ProductDto dto)
    {
        try
        {
            var entity = dto.Adapt<Product>();
            entity.CreatedDate = DateTime.Now;
            entity.IsDeleted = false;
            var uid = await _productRepository.AddAsync(entity);
            return ServiceResult<Guid>.Ok(uid);
        }
        catch (Exception ex)
        {
            return ServiceResult<Guid>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> UpdateAsync(ProductDto dto)
    {
        try
        {
            var existing = await _productRepository.GetByIdAsync(dto.Uid);
            if (existing == null) return ServiceResult<int>.Fail("数据不存在");

            dto.Adapt(existing);
            await _productRepository.UpdateAsync(existing);
            return ServiceResult<int>.Ok(1);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> DeleteAsync(Guid uid)
    {
        try
        {
            return ServiceResult<int>.Ok(await _productRepository.DeleteAsync(uid));
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<ProductDto> batchReq)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new BatchResultDto();

        var createList = batchReq.AddList?.Adapt<List<Product>>();
        var updateList = batchReq.UpdateList?.Adapt<List<Product>>();
        var deleteIds = batchReq.DeleteIdList;

        try
        {
            var errorMsg = new StringBuilder();

            if (createList is { Count: > 0 })
            {
                var error = await ValidateBeforeAddAsync(createList);
                if (!string.IsNullOrEmpty(error)) errorMsg.AppendLine(error);
            }

            if (updateList is { Count: > 0 })
            {
                var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

                var needUpdateUids = updateList.Select(x => x.Uid).ToList();
                var existingUids = await _productRepository.GetExistingIdListAsync(needUpdateUids);
                var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
                if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
                {
                    var error = await ValidateBeforeUpdateAsync(updateList);
                    if (!string.IsNullOrEmpty(error)) errorMsg.AppendLine(error);
                }
            }

            if (deleteIds is { Count: > 0 })
            {
                var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
            }

            if (errorMsg.Length > 0)
                return ServiceResult<BatchResultDto>.Fail(errorMsg.ToString().TrimEnd());

            if (_productRepository is not IBatchWriteRepository<Product> batchRepository)
                return ServiceResult<BatchResultDto>.Fail($"{nameof(Product)} 仓储未实现批量保存");

            if (createList is { Count: > 0 })
            {
                result.AddIdList = [.. await batchRepository.AddRangeAsync(createList)];
            }

            if (updateList is { Count: > 0 })
            {
                await batchRepository.UpdateRangeAsync(updateList);
                result.UpdateIdList = [.. updateList.Select(x => x.Uid)];
            }

            if (deleteIds is { Count: > 0 })
            {
                await batchRepository.DeleteRangeSoftAsync(deleteIds);
                result.DeleteIdList = deleteIds;
            }

            _session.Transaction.Commit();
            return ServiceResult<BatchResultDto>.Ok(result);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<BatchResultDto>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    #endregion

}
