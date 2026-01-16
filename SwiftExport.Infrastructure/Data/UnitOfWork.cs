using SwiftExport.Core.Interfaces;

namespace SwiftExport.Infrastructure.Data;

public class UnitOfWork(DbSession session) : IUnitOfWork
{
    private readonly DbSession _session = session;

    public void BeginTransaction()
    {
        _session.Transaction = _session.Connection.BeginTransaction();
    }

    public void Commit()
    {
        _session.Transaction?.Commit();
        _session.Transaction = null;
    }

    public void Rollback()
    {
        _session.Transaction?.Rollback();
        _session.Transaction = null;
    }

    public void Dispose()
    {
        _session.Transaction?.Dispose();
        GC.SuppressFinalize(this);
    }
}