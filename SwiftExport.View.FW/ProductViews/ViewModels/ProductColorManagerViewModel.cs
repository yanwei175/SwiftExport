using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.ProductViews.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.ProductViews.ViewModels
{
    public class ProductColorManagerViewModel : BaseSmallDataManagerViewModel<ProductColorModel, ProductColorDto>
    {
        private readonly IDialogService _msg;
        public ProductColorManagerViewModel(IProductColorApi api, IDialogService dialogService) : base(api, dialogService)
        {
            _msg = dialogService;
        }

        protected override bool FilterItem(ProductColorModel item, string searchText)
        {
            return (item.ColorCode?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.ColorNameEn?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.ColorNameZh?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        protected override async Task<bool> ValidateBeforeSaveAsync(List<ProductColorModel> dirtyModels)
        {
            // 1. 必填项空值拦截 (这是替代了以前前端红框的防线)
            var emptyItems = dirtyModels.Where(x => !x.IsValid).ToList();
            if (emptyItems.Any())
            {
                await _msg.ShowWarningAsync("存在未填写完整的行！\n\n【颜色唯一代码】和【颜色代码】为必填项，请补充完整后再保存。");
                return false;
            }

            // 所有没有被删除的数据拿出来分组
            var activeItems = AllItems.Where(x => x.State != ModelState.Deleted).ToList();

            // 统一转大写 + 去除首尾空格 + 防 Null
            var duplicates = activeItems
                .GroupBy(x => new
                {
                    CustomerCode = x.ColorCodeUnique?.Trim().ToUpper() ?? ""
                })
                .Where(g => g.Count() > 1)
                .ToList(); // 这里不要直接 Select Key，我们把整组数据留下来

            if (duplicates.Any())
            {
                // ★ 体验细节：拿到重复组里的“第一行真实数据”，这样弹窗报错时显示的是用户原来输入的真实大小写
                var firstDupItem = duplicates.First().First();

                await _msg.ShowWarningAsync($"列表中存在重复的配置！\n\n" +
                                  $"颜色唯一代码：{firstDupItem.ColorCodeUnique}\n" +
                                  $"请修改或删除重复行后再保存。");

                // 返回 false 告诉基类：拦截失败！不要发请求！
                return false;
            }

            // 返回 true 告诉基类：校验完美通过，放行！
            return true;
        }



    }
}
