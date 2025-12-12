using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExportAddIn.UserFactory
{
    public interface IServerFactory
    {
        T GetSingletonService<T>();
        IServiceScope CreateScope();

    }
}
