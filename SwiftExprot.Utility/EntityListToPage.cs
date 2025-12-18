using System;
using System.Collections.Generic;
using System.Linq;

namespace SwiftExprot.Utility
{
    public static class EntityListToPage
    {
        /// <summary>
        /// 将一个实体列表分页，返回字典，键为页码（从1开始），值为该页的数据列表
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="list">源数据集合</param>
        /// <param name="pageSize">每页大小</param>
        /// <returns>分页后的字典</returns>
        public static Dictionary<int, List<T>> ListToPage<T>(IEnumerable<T> list, int pageSize)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            if (pageSize <= 0) throw new ArgumentException("pageSize 必须大于 0");

            var result = new Dictionary<int, List<T>>();
            var items = list.ToList();
            int totalPages = (int)Math.Ceiling(items.Count / (double)pageSize);

            for (int page = 1; page <= totalPages; page++)
            {
                var pageItems = items
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                result[page] = pageItems;
            }
            return result;
        }







    }


}
