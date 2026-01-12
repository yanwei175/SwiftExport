using Dapper;
using SwiftExport.Core.Entities.MasterData; // 引用 Core
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Infrastructure.Repositories;

// 定义接口 (通常这个接口定义应该放在 Core 层，为了演示方便先写这)
public interface IProductRepository
{
    Task<IEnumerable<Product>> GetAllAsync();
    Task<Product?> GetByIdAsync(int id);
}

public class ProductRepository : IProductRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ProductRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

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

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        using var conn = _connectionFactory.CreateConnection();
        return await conn.QueryAsync<Product>(SelectSql);
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        using var conn = _connectionFactory.CreateConnection();
        // Dapper 会自动参数化防止注入
        var sql = $"{SelectSql} AND ID = @Id";
        return await conn.QueryFirstOrDefaultAsync<Product>(sql, new { Id = id });
    }
}