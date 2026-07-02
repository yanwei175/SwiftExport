using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Products;

public partial class ProductService(IProductRepository productRepository, ServiceBaseContext context, IProductCategoryRepository categoryRepository)
    : ApplicationServiceBase(context), IProductService
{
    private readonly ServiceCrud<Product, ProductDto> _crud = new(productRepository, context);
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

}
