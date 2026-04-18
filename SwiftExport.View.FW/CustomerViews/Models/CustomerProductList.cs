using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.CustomerViews.Models
{
    public class CustomerProductList : BaseListModel<CustomerProductModel>
    {

        /// <summary>
        /// 从 DTO 列表加载订单头集合（用于订单管理页）
        /// </summary>
        public void LoadFromDto(List<CustomerProductDto> dtos)
        {
            Clear();
            if (dtos != null)
            {
                foreach (var dto in dtos)
                {
                    var model = new CustomerProductModel();
                    model.LoadFromDto(dto);
                    Items.Add(model);
                }
            }

            RaiseAllState();
        }
    }
}
