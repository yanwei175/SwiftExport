using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        // 提供给 Repository 使用
        IDbTransaction Transaction { get; }
        IDbConnection Connection { get; }
        // 提供给 Application Service 终结事务
        Task CommitAsync();
        Task RollbackAsync();

    }
}
