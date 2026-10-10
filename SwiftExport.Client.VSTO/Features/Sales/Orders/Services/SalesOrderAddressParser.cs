using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Client.VSTO.Services.ExcelImport;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Services
{
    /// <summary>把地址读取快照转换为临时编辑模型，保留原值，不匹配产品身份。</summary>
    public sealed class SalesOrderAddressParser
    {
        private readonly ExcelImportDefinition definition;
        /// <summary>复用模型特性生成的字段目录，不接受任意属性反射。</summary>
        public SalesOrderAddressParser(ExcelImportRegistry registry) { definition = registry.Find("SalesOrder"); }
        /// <summary>按行赋值并保留来源原文；销售旧汇率不解析、不产生错误，其他转换失败保留待修正。</summary>
        public SalesOrderAddressResult Parse(ExcelAddressSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var result = new SalesOrderAddressResult();
            foreach (var values in snapshot.Rows)
            {
                if (snapshot.Detail && values.All(v => v.Value == null || v.Value is string s && string.IsNullOrWhiteSpace(s))
                    && values.All(v => string.IsNullOrEmpty(v.Issue)))
                { result.Issues.Add("跳过全空明细行：" + values.FirstOrDefault()?.Row); continue; }
                object model = snapshot.Detail ? (object)SalesOrderImportLine.CreateForImport() : SalesOrderImportHeader.CreateForImport();
                var source = model is SalesOrderImportLine line ? line.Source : ((SalesOrderImportHeader)model).Source;
                source.SetCells(values.Select(v => new SalesOrderImportSourceCell(v.Row, v.Column, "", v.FieldKey, Convert.ToString(v.Value, CultureInfo.InvariantCulture))));
                foreach (var cell in values)
                {
                    if (ExcelAddressRules.IsDeprecatedField(definition.GroupCode, cell.FieldKey)) continue;
                    var field = definition.Fields.First(f => f.FieldKey == cell.FieldKey);
                    object value = cell.Value;
                    if (!string.IsNullOrEmpty(cell.Issue)) { result.Issues.Add(field.DisplayName + "：" + cell.Issue); continue; }
                    try
                    {
                        if (field.ModelType.GetProperty(field.PropertyName)?.PropertyType == typeof(DateTime?))
                        {
                            if (value is double number)
                            {
                                if (!snapshot.Date1904 && number >= 60 && number < 61) throw new ArgumentException();
                                value = snapshot.Date1904 ? new DateTime(1904, 1, 1).AddDays(number)
                                    : DateTime.FromOADate(number < 60 ? number + 1 : number);
                            }
                        }
                        // 英文模板日期采用明确文化；无法转换的原值仍在来源快照中供人工核对。
                        if (!definition.TryAssign(model, cell.FieldKey, value, CultureInfo.GetCultureInfo("en-US"), out var error))
                            result.Issues.Add("行 " + cell.Row + " " + error);
                    }
                    catch (ArgumentException) { result.Issues.Add("行 " + cell.Row + " 日期序号超出范围。"); }
                }
                if (snapshot.Detail) result.Lines.Add((SalesOrderImportLine)model);
                else result.Header = (SalesOrderImportHeader)model;
            }
            result.Message = snapshot.WorkbookName + " / " + snapshot.WorksheetName + "：" + (snapshot.Detail ? "读取明细 " + result.Lines.Count + " 行" : "读取单头")
                + (result.Issues.Count == 0 ? "" : "\n" + string.Join("\n", result.Issues));
            return result;
        }
    }
    /// <summary>接纳前的临时模型与问题，不修改原页面。</summary>
    public sealed class SalesOrderAddressResult
    {
        public SalesOrderImportHeader Header { get; set; }
        public List<SalesOrderImportLine> Lines { get; } = new List<SalesOrderImportLine>();
        public List<string> Issues { get; } = new List<string>();
        public string Message { get; set; }
    }
}
