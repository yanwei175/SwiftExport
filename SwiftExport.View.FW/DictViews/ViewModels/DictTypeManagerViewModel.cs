using GalaSoft.MvvmLight.Command;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.View.FW.DictViews.Models;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.DictViews.ViewModels
{
    // 中文注释：字典类型管理 ViewModel（独立窗体）
    public class DictTypeManagerViewModel : BaseSmallDataManagerViewModel<DictTypeModel, DictTypeDto>
    {
        private readonly IDialogService _msg;
        private readonly IDictCacheService _dictCacheService;

        public RelayCommand<DictTypeModel> TypeDeleteCommand { get; }

        public DictTypeManagerViewModel(ISysDictTypeApi dictTypeApi, IDialogService dialogService, IDictCacheService dictCacheService)
            : base(dictTypeApi, dialogService)
        {
            _msg = dialogService;
            _dictCacheService = dictCacheService;
            TypeDeleteCommand = new RelayCommand<DictTypeModel>(ExecuteTypeDelete);
        }

        // 中文注释：字典类型本地过滤规则
        protected override bool FilterItem(DictTypeModel item, string searchText)
        {
            return (item.DictName?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.DictCode?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (item.Remark?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // 中文注释：新增行时设置默认值
        protected override void AddNewRow()
        {
            base.AddNewRow();
            var newlyAdded = AllItems.FirstOrDefault();
            if (newlyAdded != null)
            {
                newlyAdded.IsSystem = false;
            }
        }

        // 中文注释：保存前校验（类型编码不可重复）
        protected override async Task<bool> ValidateBeforeSaveAsync(List<DictTypeModel> dirtyModels)
        {
            var activeItems = AllItems.Where(x => x.State != ModelState.Deleted).ToList();
            var duplicates = activeItems
                .GroupBy(x => x.DictCode?.Trim().ToUpper() ?? string.Empty)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() > 1)
                .ToList();

            if (duplicates.Any())
            {
                await _msg.ShowWarningAsync($"保存失败：存在重复的字典编码【{duplicates.First().Key}】。");
                return false;
            }

            return true;
        }

        // 中文注释：保存成功后刷新字典缓存
        protected override async Task AfterSaveSuccessAsync()
        {
            await _dictCacheService.RefreshAllAsync();
        }

        // 中文注释：类型删除逻辑，系统内置项禁止删除
        private void ExecuteTypeDelete(DictTypeModel model)
        {
            if (model == null) return;

            if (model.IsSystem && model.State != ModelState.Added)
            {
                _ = _msg.ShowWarningAsync("系统内置字典类型禁止删除。");
                return;
            }

            SingleDeleteCommand.Execute(model);
        }
    }
}
