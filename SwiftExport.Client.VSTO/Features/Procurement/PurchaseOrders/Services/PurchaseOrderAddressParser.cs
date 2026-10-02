using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models;
namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Services
{
    /// <summary>把纯托管地址快照转换为采购编辑模型，不猜客户、供应商和产品身份。</summary>
    public sealed class PurchaseOrderAddressParser
    {
        private readonly ExcelImportDefinition definition;
        /// <summary>使用采购模型特性生成的唯一字段目录。</summary>
        public PurchaseOrderAddressParser(ExcelImportRegistry registry) { definition=registry.Find("PurchaseOrder"); }
        /// <summary>缺失值保持空，非法日期提供反馈；1900 和 1904 日期系统明确区分。</summary>
        public PurchaseOrderAddressResult Parse(ExcelAddressSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var result=new PurchaseOrderAddressResult();
            foreach(var values in snapshot.Rows)
            {
                if(snapshot.Detail && values.All(v=>v.Value==null || v.Value is string text && string.IsNullOrWhiteSpace(text)) && values.All(v=>string.IsNullOrEmpty(v.Issue)))
                { result.Issues.Add("跳过全空明细行："+values.FirstOrDefault()?.Row); continue; }
                object model=snapshot.Detail ? (object)new PurchaseOrderImportLine() : PurchaseOrderImportHeader.CreateForImport();
                foreach(var cell in values)
                {
                    if(!string.IsNullOrEmpty(cell.Issue)) { result.Issues.Add("行 "+cell.Row+" "+cell.FieldKey+"："+cell.Issue); continue; }
                    object value=cell.Value;
                    try
                    {
                        var field=definition.Fields.FirstOrDefault(f=>f.FieldKey==cell.FieldKey);
                        if(field!=null && field.ModelType.GetProperty(field.PropertyName).PropertyType==typeof(DateTime?) && value is double number)
                        {
                            if(!snapshot.Date1904 && number>=60 && number<61) throw new ArgumentException();
                            value=snapshot.Date1904 ? new DateTime(1904,1,1).AddDays(number) : DateTime.FromOADate(number<60 ? number+1 : number);
                        }
                        if(!definition.TryAssign(model,cell.FieldKey,value,CultureInfo.GetCultureInfo("zh-CN"),out var error)) result.Issues.Add("行 "+cell.Row+" "+error);
                    }
                    catch(ArgumentException) { result.Issues.Add("行 "+cell.Row+" 日期序号无效。"); }
                }
                if(snapshot.Detail) result.Lines.Add((PurchaseOrderImportLine)model); else result.Header=(PurchaseOrderImportHeader)model;
            }
            result.Message=snapshot.WorkbookName+" / "+snapshot.WorksheetName+"："+(snapshot.Detail ? "读取明细 "+result.Lines.Count+" 行" : "读取单头")+"\n"+string.Join("\n",result.Issues);
            return result;
        }
    }
    /// <summary>接纳前的临时解析结果，不覆盖页面现有编辑。</summary>
    public sealed class PurchaseOrderAddressResult
    {
        public PurchaseOrderImportHeader Header { get; set; }
        public List<PurchaseOrderImportLine> Lines { get; } = new List<PurchaseOrderImportLine>();
        public List<string> Issues { get; } = new List<string>();
        public string Message { get; set; }
    }
}
