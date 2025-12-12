//using SwiftExport.Core.Interfaces;
//using System;
//using System.Collections.Generic;
//using System.Data;
//using System.Text;
//using System.Threading.Tasks;

//namespace SwiftExport.Infrastructure.Repositories
//{
//    public class UnitOfWork : IUnitOfWork
//    {
//        private readonly IDbConnection _connection;
//        private IDbTransaction _transaction;
//        private readonly ILogger<UnitOfWork> _logger;
//        private bool _disposed;

//        public UnitOfWork(IDbConnection connection, ILogger<UnitOfWork> logger)
//        {
//            _connection = connection;
//            _logger = logger;

//            if (_connection.State != ConnectionState.Open)
//            {
//                _connection.Open();
//            }
//        }

//        public async Task<int> SaveChangesAsync()
//        {
//            try
//            {
//                // Dapper不跟踪变更，这里只是模拟保存
//                _logger.LogInformation("Changes saved");
//                return await Task.FromResult(1);
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error saving changes");
//                throw;
//            }
//        }

//        public async Task BeginTransactionAsync()
//        {
//            try
//            {
//                if (_transaction != null)
//                {
//                    throw new InvalidOperationException("Transaction already started");
//                }

//                _transaction = _connection.BeginTransaction();
//                await Task.CompletedTask;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error beginning transaction");
//                throw;
//            }
//        }

//        public async Task CommitAsync()
//        {
//            try
//            {
//                if (_transaction == null)
//                {
//                    throw new InvalidOperationException("No active transaction");
//                }

//                _transaction.Commit();
//                _transaction.Dispose();
//                _transaction = null;

//                await Task.CompletedTask;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error committing transaction");
//                throw;
//            }
//        }

//        public async Task RollbackAsync()
//        {
//            try
//            {
//                if (_transaction == null)
//                {
//                    throw new InvalidOperationException("No active transaction");
//                }

//                _transaction.Rollback();
//                _transaction.Dispose();
//                _transaction = null;

//                await Task.CompletedTask;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Error rolling back transaction");
//                throw;
//            }
//        }

//        public void Dispose()
//        {
//            Dispose(true);
//            GC.SuppressFinalize(this);
//        }

//        protected virtual void Dispose(bool disposing)
//        {
//            if (!_disposed)
//            {
//                if (disposing)
//                {
//                    _transaction?.Dispose();
//                    _connection?.Dispose();
//                }
//                _disposed = true;
//            }
//        }

//        ~UnitOfWork()
//        {
//            Dispose(false);
//        }
//    }

//}
