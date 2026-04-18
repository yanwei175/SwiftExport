using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.VSTO.UserWindow.CustomerViews.Models;
using SwiftExport.VSTO.UserWindow.Dialog;
using SwiftExport.VSTO.UserWindow.Mvvm;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.VSTO.UserWindow.CustomerViews.ViewModels
{
    public class CustomerPoConfigViewModel : BaseManagerViewModel<CustomerPoConfigModel, CustomerPoConfigDto>
    {
        public ObservableCollection<CustomerDto> Customers { get; set; } = new ObservableCollection<CustomerDto>();
        public ObservableCollection<SupplierDto> Suppliers { get; set; } = new ObservableCollection<SupplierDto>();

        private readonly ICustomerApi _customerApi;
        private readonly ISupplierApi _supplierApi;

        public CustomerPoConfigViewModel(ICustomerPoConfigApi api,
            ICustomerApi customerApi,
            ISupplierApi supplierApi
            ) : base(api)
        {
            _customerApi = customerApi;
            _supplierApi = supplierApi;
            // 构造时异步加载下拉框的数据
            _ = LoadDropdownDataAsync();
        }
        protected override IEnumerable<string> GetAddedItemKeys(IEnumerable<CustomerPoConfigModel> addedItems)
        {
            return addedItems.Select(x => x.CustomerCode);
        }
        private async Task LoadDropdownDataAsync()
        {
            try
            {
                // 拉取客户列表
                var customerRst = await _customerApi.GetAllAsync();
                if (customerRst.Success && customerRst.Data != null)
                {
                    foreach (var item in customerRst.Data) Customers.Add(item);
                }

                // 拉取供应商列表
                var supplierRst = await _supplierApi.GetAllAsync();
                if (supplierRst.Success && supplierRst.Data != null)
                {
                    foreach (var item in supplierRst.Data) Suppliers.Add(item);
                }
            }
            catch (Exception ex)
            {
                Msg.ShowError("加载下拉框基础资料失败: " + ex.Message);
            }
        }

        // ==========================================
        // 重写钩子方法，注入专属的“联合唯一性”校验
        // ==========================================
        protected override bool ValidateBeforeSave(List<CustomerPoConfigModel> dirtyModels)
        {

            var emptyItems = dirtyModels.Where(x => !x.IsValid).ToList();
            if (emptyItems.Any())
            {
                Msg.ShowWarning("存在未填写完整的行！\n\n【客户代码】和【供应商代码】为必填项，请补充完整后再保存。");
                return false;
            }

            // 我们把当前 DataGrid 里所有没有被删除的数据拿出来分组
            var activeItems = AllItems.Where(x => x.State != ModelState.Deleted).ToList();

            // ★ 架构师防呆升级：统一转大写 + 去除首尾空格 + 防 Null
            var duplicates = activeItems
                .GroupBy(x => new
                {
                    CustomerCode = x.CustomerCode?.Trim().ToUpper() ?? "",
                    SupplierCode = x.SupplierCode?.Trim().ToUpper() ?? ""
                })
                .Where(g => g.Count() > 1)
                .ToList(); // 这里不要直接 Select Key，我们把整组数据留下来

            if (duplicates.Any())
            {
                // ★ 体验细节：拿到重复组里的“第一行真实数据”，这样弹窗报错时显示的是用户原来输入的真实大小写
                var firstDupItem = duplicates.First().First();

                Msg.ShowWarning($"列表中存在重复的配置！\n\n" +
                                $"客户代码：{firstDupItem.CustomerCode}\n" +
                                $"供应商代码：{firstDupItem.SupplierCode}\n\n" +
                                $"请修改或删除重复行后再保存。");

                // 返回 false 告诉基类：拦截失败！不要发请求！
                return false;
            }

            // 返回 true 告诉基类：校验完美通过，放行！
            return true;
        }

    }
}