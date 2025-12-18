using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Core.Common
{
    public enum 查询操作符
    {
        等于,        // Equal
        模糊匹配,    // Like
        大于,        // GreaterThan
        小于,        // LessThan
        大于等于,    // GreaterOrEqual
        小于等于,    // LessOrEqual
        区间         // Between
    }
    /// <summary>
    /// 表示单个字段的查询条件配置对象。
    /// </summary>
    /// <remarks>
    /// 该对象用于定义在查询时，针对某个特定字段应采用的操作符、单个值或值区间。
    /// 通常作为字典的值被使用，字典的Key为待查询的数据库字段名。
    /// </remarks>
    public class QueryCondition
    {
        /// <summary>
        /// 获取或设置用于该字段的查询操作符（例如：等于、大于、Like、Between）。
        /// </summary>
        public 查询操作符 操作符 { get; set; }
        /// <summary>
        /// 获取或设置查询值。
        /// </summary>
        /// <value>
        /// 当操作符为“等于”、“大于”等单值操作时，该属性用于传入比较值。
        /// 当操作符为“Between”（区间）时，该属性用于传入区间的起始值。
        /// </value>
        public object 值1 { get; set; }
        /// <summary>
        /// 获取或设置区间的结束值。
        /// </summary>
        /// <value>
        /// 仅当 <see cref="操作符"/> 设置为“Between”等区间操作时使用，表示区间的结束值。
        /// </value>
        public object 值2 { get; set; } // 区间用第二个值
    }

}
