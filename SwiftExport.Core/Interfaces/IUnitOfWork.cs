using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface IUnitOfWork : IDisposable
    {
        Task<int> SaveChangesAsync();
        Task BeginTransactionAsync();
        Task CommitAsync();
        Task RollbackAsync();
    }
}
