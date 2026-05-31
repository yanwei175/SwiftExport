using Mapster;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.View.FW.Mvvm;
using System;

namespace SwiftExport.View.FW.SalesOrderViews.Models
{
    /// <summary>
    /// 销售订单聚合模型。
    /// 用于详情页承载一张完整订单：订单头 + 明细行集合。
    /// </summary>
    public class SalesOrderModel
    {
        public SalesOrderHeaderModel Header { get; }
        public SalesOrderLineList Lines { get; }

        /// <summary>
        /// 中文注释：聚合级有效性。
        /// 订单头有效且所有未删除明细都有效时，整张订单才有效。
        /// </summary>
        public bool IsValid => Header.IsValid && Lines.IsAllValid;

        /// <summary>
        /// 中文注释：聚合级脏状态。
        /// 订单头或明细任一发生用户修改，都视为整张订单已修改。
        /// </summary>
        public bool IsDirty => Header.State != ModelState.Unchanged || Lines.IsDirty;

        public SalesOrderModel()
        {
            Header = new SalesOrderHeaderModel();
            Lines = new SalesOrderLineList(Header);
        }

        public void LoadFromDto(SalesOrderDetailDto dto)
        {
            if (dto == null) return;

            if (dto.Header != null)
                Header.LoadFromDto(dto.Header);

            // 中文注释：确保行集合始终绑定当前 Header，加载明细后自动刷新主表汇总字段。
            Lines.BindHeader(Header);
            Lines.ReLoadRangeFromDtoList(dto.Lines);
        }
    }
}
