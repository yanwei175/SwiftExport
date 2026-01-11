using Dapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories
{
    public class ProductCategoryRepository : DapperRepository<ProductCategory>, IProductCategoryRepository
    {

        private readonly IDataTableFactory<ProductCategory> _fac;

        public ProductCategoryRepository(IMappingCache cache, IDbConnectionFactory _connFac, IDataTableFactory<ProductCategory> fac) : base(cache, _connFac)
        {
            _fac = fac;
        }

        public async Task<int> GetMaxSortNoAsync(int? parentId)
        {
            // 获取当前层级下最大的 SortNo，方便新加的排在后面
            string sql = "SELECT ISNULL(MAX(SortNo), 0) FROM ProductCategory WHERE IsDelete <> 1";
            using (var conn = _connFac.CreateConnection())
            {
                if (parentId.HasValue)
                {
                    sql += " AND ParentID = @ParentID";
                    return conn.ExecuteScalar<int>(sql, new { ParentID = parentId });
                }
                else
                {
                    sql += " AND (ParentID IS NULL OR ParentID='')";
                    return conn.ExecuteScalar<int>(sql);
                }

            }
            ;

        }
    }
}
