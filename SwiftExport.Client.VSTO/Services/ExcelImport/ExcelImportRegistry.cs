using System;
using System.Collections.Generic;
using System.Linq;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;

namespace SwiftExport.Client.VSTO.Services.ExcelImport
{
    /// <summary>显式注册导入模型组合；字段从模型属性生成，不另维护字段列表。</summary>
    public sealed class ExcelImportRegistry
    {
        private readonly Dictionary<string, ExcelImportDefinition> byGroup;
        /// <summary>一次注册各组模型；大小写不同的重复组也拒绝，避免配置被拆成多个业务组。</summary>
        public ExcelImportRegistry(params ExcelImportDefinition[] definitions)
        {
            if (definitions == null || definitions.Any(value => value == null)) throw new ArgumentException("导入定义不能为空。");
            byGroup = new Dictionary<string, ExcelImportDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var definition in definitions)
            {
                if (byGroup.ContainsKey(definition.GroupCode)) throw new InvalidOperationException("导入分组重复：" + definition.GroupCode);
                byGroup.Add(definition.GroupCode, definition);
            }
            Groups = Array.AsReadOnly(definitions.ToArray());
        }
        public IReadOnlyList<ExcelImportDefinition> Groups { get; }
        /// <summary>定位已注册模型组，配置请求始终使用返回定义的规范分组编码。</summary>
        public ExcelImportDefinition Find(string groupCode) => groupCode != null && byGroup.TryGetValue(groupCode.Trim(), out var definition) ? definition : null;
        /// <summary>只注册目前真实可编辑的销售模型；采购和装箱单配置不删除，待模型实现后再注册。</summary>
        public static ExcelImportRegistry CreateDefault() => new ExcelImportRegistry(
            new ExcelImportDefinition("SalesOrder", "销售订单", typeof(SalesOrderImportHeader), typeof(SalesOrderImportLine)));
    }
}
