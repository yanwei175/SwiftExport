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
        private readonly IDataTableFactory<ViewUserRole> _fac;
        public ViewUserRoleRepository(IDbConnectionFactory _connFac, IMappingCache cache, IDataTableFactory<ViewUserRole> fac) : base( cache, _connFac)
        {
            _fac = fac;
        }

        public async Task<ViewUserRole> Login(string userName, string psswd)
        {
            var sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND UserName = @UserName AND Password=@Password";
            using (var conn = _connFac.CreateConnection())
            return await conn.QueryFirstOrDefaultAsync<ViewUserRole>(sql, new { UserName = userName, Password=psswd });
        }
    }
}
