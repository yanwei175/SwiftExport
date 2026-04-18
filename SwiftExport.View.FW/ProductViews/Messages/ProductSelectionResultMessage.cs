using SwiftExport.Contracts.DTOs.Products;
using System.Collections.Generic;

namespace SwiftExport.View.FW.ProductViews.Messages
{
    /// <summary>
    /// 产品选择结果消息，通过 MvvmLight Messenger 发布给订阅方。
    /// Result 为 null 表示用户取消；否则为选中的产品列表（一条或多条）。
    /// </summary>
    public class ProductSelectionResultMessage
    {
        /// <summary>用户最终确认的产品列表；null 表示用户取消了操作</summary>
        public List<ProductDto> Result { get; }

        public ProductSelectionResultMessage(List<ProductDto> result)
        {
            Result = result;
        }
    }
}
