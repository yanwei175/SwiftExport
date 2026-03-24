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

public class ProductService(IProductRepository productRepository, ServiceBaseContext context) : BaseService<Product, ProductDto>(productRepository, context), IProductService
{
    // 2. 将传入的特有 Repository 保存为当前类的私有只读字段
    private readonly IProductRepository _productRepository = productRepository;

    public async Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeListAsync()
    {
        // 使用特有的 _productRepository 调用特有方法
        var list = await _productRepository.GetAllCustomerCodeListAsync();
        if (list == null || list.Count == 0)
            return ServiceResult<IReadOnlyList<string>>.Fail("空的客户代码集合");

        return ServiceResult<IReadOnlyList<string>>.Ok(list);
    }

    // 实现特有的业务逻辑：根据 SKU 查询
    public async Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku)
    {
        var list = await _productRepository.GetListAsync("WHERE Sku = @Sku", new { Sku = sku });
        if (list == null || list.Count == 0)
            return ServiceResult<ProductDto>.Fail($"未找到 SKU 为 {sku} 的产品");
        var entity = list[0];
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
                    ("SKU", 查询操作符.包含某值, batchList),
                    ("CSKU", 查询操作符.包含某值, batchList)
                );
            }
        }

        // ==========================================
        // 处理单个关键字模糊搜索
        // ==========================================
        if (!string.IsNullOrWhiteSpace(search.Keyword))
        {
            criteria.AddOrGroup(
                ("SKU", 查询操作符.模糊匹配, search.Keyword),
                ("Description", 查询操作符.模糊匹配, search.Keyword),
                ("CSKU", 查询操作符.模糊匹配, search.Keyword)
            );
        }
        // ==========================================
        // 处理精确匹配条件
        // ==========================================
        if (!string.IsNullOrWhiteSpace(search.CustomerCode))
        {
            criteria.Add("CustomerCode", 查询操作符.等于, search.CustomerCode);
        }

        if (search.CategoryId > 0)
        {
            criteria.Add("ProductCategory", 查询操作符.等于, search.CategoryId);
        }

        // 4. 处理分页默认值保护
        int pageIndex = search.PageIndex > 0 ? search.PageIndex : 1;
        int pageSize = search.PageSize > 0 ? search.PageSize : 50;

        // 5. 核心：调用基类提供的泛型分页方法，完成数据库查询和 DTO 映射
        return await base.GetPagedListAsync(criteria, pageIndex, pageSize, "ID DESC");
    }



}