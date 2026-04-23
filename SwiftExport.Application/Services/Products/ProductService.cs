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

public class ProductService(IProductRepository productRepository, ServiceBaseContext context, IProductCategoryRepository categoryRepository) : BaseService<Product, ProductDto>(productRepository, context), IProductService
{
    // 2. 将传入的特有 Repository 保存为当前类的私有只读字段
    private readonly IProductRepository _productRepository = productRepository;
    private readonly IProductCategoryRepository _categoryRepository = categoryRepository;

    public override async Task<ServiceResult<IReadOnlyList<ProductDto>>> GetAllAsync()
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

    public async Task<ServiceResult<PagedResultDto<ProductDto>>> GetPagedListAsync(ProductSearchDto search)
    {
        // 1. 实例化底层查询对象
        var criteria = new QueryCriteria();

        // ==========================================
        // 处理批量搜索 (BatchKeywords) - 新增逻辑
        // ==========================================
        if (!string.IsNullOrWhiteSpace(search.BatchKeywords))
        {
            // 1. 分割字符串：支持 换行符(\n, \r) 和 逗号(,)
            var batchList = search.BatchKeywords
                .Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())           // 去除首尾空格
                .Where(k => !string.IsNullOrEmpty(k)) // 再次确保没有空字符串
                .Distinct()                      // 去重
                .ToList();

            if (batchList.Count > 0)
            {
                // 场景 A：只匹配 SKU (性能最高，最常用)
                // criteria.Add("SKU", 查询操作符.包含某值, batchList);

                // 场景 B：匹配 SKU 或 客户型号 (更灵活)
                // 逻辑：(SKU IN (...) OR CSKU IN (...))
                criteria.AddOrGroup(
                    ("Sku", 查询操作符.包含某值, batchList),
                    ("CSku", 查询操作符.包含某值, batchList)
                );
            }
        }

        // ==========================================
        // 处理单个关键字模糊搜索
        // ==========================================
        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            criteria.AddOrGroup(
                ("Sku", 查询操作符.模糊匹配, search.Keyword),
                ("Description", 查询操作符.模糊匹配, search.Keyword),
                ("CSku", 查询操作符.模糊匹配, search.Keyword)
            );
        }
        // ==========================================
        // 处理精确匹配条件
        // ==========================================
        if (!string.IsNullOrWhiteSpace(search.ColorCodeUnique))
        {
            criteria.Add("ColorCodeUnique", 查询操作符.等于, search.ColorCodeUnique);
        }
        if (search.CustomerIds != null && search.CustomerIds.Count > 0)
        {
            criteria.Add("CustomerId", 查询操作符.包含某值, search.CustomerIds);
        }

        if (search.CategoryId > 0)
        {
            var ids = await _categoryRepository.GetSubById(search.CategoryId);
            search.CategoryIds.AddRange(ids.Select(c => c.Id));
        }

        if (search.CategoryIds != null && search.CategoryIds.Count > 0)
        {
            criteria.Add("CategoryId", 查询操作符.包含某值, search.CategoryIds);
        }

        else if (!string.IsNullOrWhiteSpace(search.CategoryName))
        {
            criteria.Add("CategoryName", 查询操作符.等于, search.CategoryName);
        }

        // 4. 处理分页默认值保护
        int pageIndex = search.PageIndex > 0 ? search.PageIndex : 1;
        int pageSize = search.PageSize > 0 ? search.PageSize : 50;

        var paged = await _productRepository.GetPagedReadModelAsync(criteria, pageIndex, pageSize, "ID DESC");

        return ServiceResult<PagedResultDto<ProductDto>>.Ok(new PagedResultDto<ProductDto>
        {
            Items = paged.Items.Adapt<IReadOnlyList<ProductDto>>(),
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        });
    }





    #region 批量保存
    protected override bool IsSoftDelete => false;
    protected override async Task<string?> ValidateBeforeAddAsync(List<Product> entities)
    {
        var codes = new List<string>();
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.Sku))
                return "产品型号不能为空";
            codes.Add(item.Sku);
        }

        // 调用子仓储专属方法检查重复
        var repositoryImpl = (IProductRepository)_repository;
        var existedCodes = await repositoryImpl.GetExistingSkuListAsync(codes);
        if (existedCodes.Any())
        {
            return $"以下产品型号已存在，拒绝新增: {string.Join(", ", existedCodes)}";
        }

        // 返回 null 代表一切正常，放行！
        return null;
    }

    protected override async Task<string?> ValidateBeforeUpdateAsync(List<Product> entities)
    {
        var codes = new List<string>();
        foreach (var item in entities)
        {
            if (string.IsNullOrWhiteSpace(item.Sku))
                return "产品型号不能为空";
            codes.Add(item.Sku);
        }

        // 调用子仓储专属方法检查重复
        var repositoryImpl = (IProductRepository)_repository;
        var existedCodes = await repositoryImpl.GetExistingSkuListAsync(codes);
        if (existedCodes.Any())
        {
            return $"以下产品型号已存在，拒绝修改: {string.Join(", ", existedCodes)}";
        }

        // 返回 null 代表一切正常，放行！
        return null;
    }



    #endregion

}