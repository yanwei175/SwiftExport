using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;

namespace SwiftExport.Infrastructure.Interfaces
{
    public  interface IMappingCache
    {
        String  GetTableNameByEntity<T>() where T: BaseEntity;
        String GetUniqueFieldByEntity<T>() where T : BaseEntity;
        String GetFieldNameByEntityProperty<T>(string propertyName) where T : BaseEntity;
        String GetUiColNameByEntityProperty<T>(string UiColName) where T : BaseEntity;
        
    }
}
