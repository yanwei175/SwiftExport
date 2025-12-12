using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Factory
{
    public class DataTableFactory<T> : IDataTableFactory<T> where T : BaseEntity
    {
        private readonly IMappingCache _cache;

        public DataTableFactory(IMappingCache cache)
        {
            _cache = cache;
        }

        public async Task<DataTable> CreatDtByEntityPropertysAsync(IEnumerable<T> entitys)
        {
            var dt = new DataTable();
            var props= typeof(T).GetProperties();
            //添加列
            foreach (var property in typeof(T).GetProperties())
            {
                var colName = _cache.GetFieldNameByEntityProperty<T>(property.Name);
                dt.Columns.Add(colName);
            }
            //添加行
            foreach (var entity in entitys)
            {
                var row = dt.NewRow();
                foreach (var property in props)
                {
                    var colName = _cache.GetFieldNameByEntityProperty<T>(property.Name);
                    row[colName] = property.GetValue(entity);
                }
                dt.Rows.Add(row);
            }
            return await Task.FromResult(dt);
        }
    }
}
