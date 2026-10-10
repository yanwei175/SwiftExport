namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Models
{
    public sealed partial class SalesOrderImportHeader
    {
        private string originalTerms = "", originalUpdateDateText = "";
        /// <summary>订单单头的来源与解析反馈，仅包含纯托管数据。</summary>
        public SalesOrderImportSource Source { get; } = new SalesOrderImportSource();
        /// <summary>保留原始 Terms 文本，不能自动推断付款比例和贸易术语。</summary>
        public string OriginalTerms { get => originalTerms; set => SetProperty(ref originalTerms, value); }
        /// <summary>原表 UpdateDate，仅供预览，不参与保存 DTO。</summary>
        public string OriginalUpdateDateText { get => originalUpdateDateText; set => SetProperty(ref originalUpdateDateText, value); }
        /// <summary>订单单头默认允许手工录入；来源变化同步通知校验汇总。</summary>
        public SalesOrderImportHeader()
        {
            Source.PropertyChanged += (sender, args) => OnPropertyChanged(nameof(Error));
        }
        /// <summary>创建待解析单头，日期与币种须明确指定；废弃汇率保持空值。</summary>
        public static SalesOrderImportHeader CreateForImport()
        {
            return new SalesOrderImportHeader { OrderDate = null, Currency = "" };
        }
    }
}
