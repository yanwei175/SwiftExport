using Dapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.EntitieView;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories
{
    public class ViewUserRoleRepository : DapperRepository<ViewUserRole>, IViewUserRoleRepository
    {
        private readonly IDbConnectionFactory _connFactory;
        private readonly IMappingCache _cache;
        private readonly string tableName;
        private readonly IDataTableFactory<ViewUserRole> _fac;
        public ViewUserRoleRepository(IDbConnectionFactory connFactory, IMappingCache cache,IDataTableFactory<ViewUserRole> fac) : base(connFactory, cache)
        {
            _connFactory = connFactory;
            _cache = cache;
            tableName = _cache.GetTableNameByEntity<ViewUserRole>();
            _fac = fac;
        }

        public async Task<ViewUserRole> Login(string userName, string psswd)
        {
            var sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND UserName = @UserName AND Password=@Password";
            using (var conn = _connFactory.CreateConnection())
                {
                    var userRole = conn.QueryFirstOrDefaultAsync<ViewUserRole>(sql, new { UserName = userName, Password=psswd });
                    return await userRole;
                }
        }
    }
}
