using SwiftExport.Core.Common;
using System;

namespace SwiftExport.Infrastructure.Interfaces
{
    public interface IMappingCache
    {
        // 获取实体映射信息
        String GetTableNameByEntity<T>() where T : BaseEntity;
        // 获取唯一字段
        String GetUniqueFieldByEntity<T>() where T : BaseEntity;
        // 获取字段映射信息 获取字段名称
        String GetFieldNameByEntityProperty<T>(string propertyName) where T : BaseEntity;
        // 获取UI列映射信息 获取UI列名称
        String GetUiColNameByEntityProperty<T>(string UiColName) where T : BaseEntity;


    }
}
