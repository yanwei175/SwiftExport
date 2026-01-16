using SwiftExport.Core.Entities.MasterData; // 引用 Core
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories;



public class ProductRepository(DbSession _session) : DapperRepository<Product>(_session), IProductRepository
{

    // ⚡ 关键：SQL 映射桥接
    // 数据库里的字段 (Chinese/Pinyin) -> 实体属性 (English)
    private const string SelectSql = @"
        SELECT 
            ID as Id,
            SKU as Sku,
            Description as Description,
            
            -- 处理拼音/旧命名映射
            RMBPrice as RmbPrice,
            USDPrice as UsdPrice,
            
            -- 处理那些必填字段 (= default!)
            -- 如果数据库是 NULL，用 ISNULL 给个默认值防止报错
            ISNULL(CustomerCode, '') as CustomerCode,
            ISNULL(CSKU, '') as CSku,
            ISNULL(CDescription, '') as CDescription, 
            
            CreateDate as CreatedTime
        FROM Product 
        WHERE IsDelete = 0"; // 假设你的软删除字段叫 IsDelete

}