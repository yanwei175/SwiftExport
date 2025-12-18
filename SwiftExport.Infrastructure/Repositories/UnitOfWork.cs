using Microsoft.Data.SqlClient;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IDbConnection _connection;
        private readonly IDbTransaction _transaction;
        private bool _disposed;

        // 假设你的连接字符串通过 DI 注入
        public UnitOfWork(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
            // 1. 通过工厂创建并打开连接
            _connection = _connectionFactory.CreateConnection();
            _connection.Open();

            // 2. 启动事务
            _transaction = _connection.BeginTransaction();
        }

        public IDbConnection Connection=> _connection;
        public IDbTransaction Transaction => _transaction;

        // 【异步提交实现】
        public Task CommitAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    _transaction.Commit();
                }
                catch
                {
                    _transaction.Rollback();
                    throw;
                }
                finally
                {
                    Dispose();
                }
            });
        }

        // 【异步回滚实现】
        public Task RollbackAsync()
        {
            return Task.Run(() =>
            {
                _transaction.Rollback();
                Dispose();
            });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 清理托管资源
                    _transaction?.Dispose();
                    _connection?.Close();
                    _connection?.Dispose();
                }
                _disposed = true;
            }
        }




    }
}

