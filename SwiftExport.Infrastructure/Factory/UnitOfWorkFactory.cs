using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using SwiftExport.Infrastructure.Repositories;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Infrastructure.Factory
{
    public class UnitOfWorkFactory : IUintOfWorkFactory
    {
        private readonly IDbConnectionFactory _factory;
        public UnitOfWorkFactory(IDbConnectionFactory factory) { _factory=factory;}

        public IUnitOfWork CreateUoW()
        {
            return new UnitOfWork(_factory);
        }

    }
}
