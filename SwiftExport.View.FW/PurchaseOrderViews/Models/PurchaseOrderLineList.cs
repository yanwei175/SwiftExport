using Mapster;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.View.FW.Mvvm;
using System.Collections.Generic;
using System.Linq;

namespace SwiftExport.View.FW.PurchaseOrderViews.Models
{
    /// <summary>
    /// 采购订单行集合模型。
    /// 中文注释：基于 BaseListModel 管理勾选、脏数据与批量变更。
    /// </summary>
    public class PurchaseOrderLineList : BaseListModel<PurchaseOrderLineModel>
    {
        /// <summary>
        /// 中文注释：兼容旧调用习惯，提供显式加载入口。
        /// </summary>
        public void LoadFromDto(List<PurchaseOrderLineDto> dtos)
        {
            ReLoadRangeFromDtoList(dtos);
        }

        /// <summary>
        /// 中文注释：获取用于保存的 DTO 列表（新增+修改）。
        /// </summary>
        public List<PurchaseOrderLineDto> GetDirtyDtosForSave()
        {
            return Items
                .Where(x => x.State == ModelState.Added || x.State == ModelState.Modified)
                .Select(x => x.Adapt<PurchaseOrderLineDto>())
                .ToList();
        }
    }
}
