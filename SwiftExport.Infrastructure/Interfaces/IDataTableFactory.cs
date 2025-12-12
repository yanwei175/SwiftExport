using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Interfaces
{
    public interface IDataTableFactory<T> where T:BaseEntity
    {
        Task <DataTable> CreatDtByEntityPropertysAsync(IEnumerable<T> entitys);
    }
}
