using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Interfaces
{
    public interface IUintOfWorkFactory
    {
       IUnitOfWork CreateUoW();
    }
}
