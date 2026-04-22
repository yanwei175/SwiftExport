using GalaSoft.MvvmLight.Command;
using Mapster;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.UiInterface;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.View.FW.Mvvm;
using SwiftExport.View.FW.Routing;
using SwiftExport.View.FW.SystemViews.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SwiftExport.View.FW.SystemViews.ViewModels
{
    /// <summary>
    /// 视图路由配置管理 ViewModel
    /// 提供路由配置的查看、新增、编辑、删除功能，
    /// 并支持手动刷新前端路由缓存（使改动立即生效）。
    /// </summary>
    public class SysViewRouteManagerViewModel : ViewModelBaseEx
    {
        private readonly ISysViewRouteApi _api;
        private readonly IViewRouteService _routeService;
        private readonly IDialogService _msg;

        // 中文注释：由 View 订阅，用于在保存完成后关闭弹窗并返回结果。
        public event Action<bool?> CloseWindow;

        // ============= 列表数据 =============

        /// <summary>路由配置列表，绑定到 DataGrid</summary>
        public SysViewRouteList CurrentTable { get; } = new SysViewRouteList();

        // ============= 选中行 =============

        private SysViewRouteModel _selectedRoute;
        /// <summary>当前选中的路由行</summary>
        public SysViewRouteModel SelectedRoute
        {
            get => _selectedRoute;
            set
            {
                if (SetProperty(ref _selectedRoute, value))
                {
                    DeleteAsyncCommand.RaiseCanExecuteChanged();
                }

            }
        }

        // ============= 状态 =============

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsAllChecked
        {
            get => CurrentTable.IsAllChecked;
            set
            {
                if (CurrentTable.IsAllChecked == value)
                    return;
                CurrentTable.IsAllChecked = value;
                RaisePropertyChanged(nameof(IsAllChecked));
            }
        }

        // ============= 构造函数 =============

        public SysViewRouteManagerViewModel(
            ISysViewRouteApi api,
            IViewRouteService routeService,
            IDialogService msg

            )
        {
            _api = api;
            _routeService = routeService;
            _msg = msg;
            LoadAsyncCommand = new RelayCommand(async () => await LoadAsync(), () => IsNotBusy);
            RefreshCacheCommand = new RelayCommand(async () => await RefreshCacheAsync(), () => IsNotBusy);
            AddCommand = new RelayCommand(Add);
            BatchSaveAsyncCommand = new RelayCommand(async () => await BatchSaveAsync(), () => CurrentTable.IsDirty && CurrentTable.IsAllValid && !IsBusy);
            DeleteAsyncCommand = new RelayCommand(async () => await DeleteAsync(), () => (CurrentTable.CheckedCount > 0 && !IsBusy) || SelectedRoute != null);
            InvertCheckCommand = new RelayCommand(InvertCheck);

            // ============================
            // 注册 Dirty 命令
            // ============================
            CurrentTable.RegisterDirtyNotifyCommand(BatchSaveAsyncCommand);

            // ============================
            // 注册 Checked 命令
            // ============================
            CurrentTable.RegisterCheckedNotifyCommand(DeleteAsyncCommand);

            // ============================
            // 注册 Busy 命令
            // ============================
            RegisterBusyNotifyCommand(BatchSaveAsyncCommand);
            RegisterBusyNotifyCommand(DeleteAsyncCommand);
            RegisterBusyNotifyCommand(RefreshCacheCommand);
            RegisterBusyNotifyCommand(LoadAsyncCommand);
            RegisterBusyNotifyCommand(InvertCheckCommand);



            _ = LoadAsync();
        }

        // ============= 刷新前端缓存 =============

        public RelayCommand RefreshCacheCommand { get; }
        /// <summary>刷新前端路由缓存（让数据库改动立即对路由跳转生效）</summary>
        private async Task RefreshCacheAsync()
        {
            using (BusyScope())
            {
                StatusMessage = "正在刷新路由缓存...";
                await _routeService.RefreshAsync();
                StatusMessage = "路由缓存已刷新，新路由立即生效";
                _msg.ShowCardAsync("路由缓存已刷新，新路由立即生效");
            }
        }

        //解决CheckBox报错
        public ICommand ItemCheckedChangedCommand { get; }

        // ============= 新增 =============

        public RelayCommand AddCommand { get; }
        private void Add()
        {
            using (BusyScope())
            {
                var model = new SysViewRouteModel() { DisplayName = "新路由配置", IsEnabled = true };
                model.MarkAsAdded();
                CurrentTable.AddModelToFirst(model);
                StatusMessage = "已添加新路由配置行，请编辑相关信息并保存。";
            }

        }

        // ============= 保存 =============
        public RelayCommand BatchSaveAsyncCommand { get; }
        private async Task BatchSaveAsync()
        {
            var dirtyModels = CurrentTable.GetChangeSet();
            if (dirtyModels.AddList.Count == 0 && dirtyModels.UpdateList.Count == 0 && dirtyModels.DeleteIdList.Count == 0)
            {
                StatusMessage = "没有需要保存的更改。";
                return;
            }
            if (CurrentTable.IsAllValid == false)
            {
                StatusMessage = "请修正数据中的错误后再保存。";
                return;
            }
            StatusMessage = "正在保存更改...";

            var req = dirtyModels.Adapt<BatchRequestGeneric<SysViewRouteDto>>(); // 先调用Adapt方法进行类型转换，确保所有属性都被正确映射
            try
            {
                var result = await _api.BatchSaveAsync(req);
                if (result.Success)
                {
                    StatusMessage = "保存成功。";
                    _msg.ShowCardAsync("保存成功！");
                    _ = LoadAsync();
                }
                else
                {
                    StatusMessage = "保存失败: " + result.Message;
                    return;
                }
            }
            catch (Exception ex)
            {
                _msg.ShowCardAsync("异常: " + ex.Message);
            }
        }


        // ============= 删除 =============        
        public RelayCommand DeleteAsyncCommand { get; }
        private async Task DeleteAsync()
        {
            var checkItems = CurrentTable.GetCheckedItems();
            List<SysViewRouteModel> waitToDelete = new List<SysViewRouteModel>();

            // 情况 1：没有勾选任何行
            if (checkItems.Count == 0)
            {
                if (SelectedRoute == null)
                    return; // ★ 没有勾选也没有选中 → 直接退出

                if (SelectedRoute.Id <= 0)
                {
                    // 删除新建行（本地删除）
                    var tempIndex = CurrentTable.Items.IndexOf(SelectedRoute);
                    CurrentTable.Remove(SelectedRoute);

                    try
                    {
                        SelectedRoute = CurrentTable.Items[tempIndex];
                    }
                    catch
                    {
                        SelectedRoute = CurrentTable.Items.FirstOrDefault();
                    }

                    return; // ★ 新建行删除不需要 API
                }

                // ★ 有选中行（且 Id > 0）→ 加入待删除列表
                waitToDelete.Add(SelectedRoute);
            }
            else
            {
                // 情况 2：有勾选行 → 全部加入待删除列表
                waitToDelete = checkItems;
            }

            // ★★★ 关键：这里不 return，继续走 API 删除流程

            StatusMessage = $"准备删除 {waitToDelete.Count} 条记录...";

            try
            {
                if (waitToDelete.Count > 0)
                {
                    if (!await _msg.ShowConfirmAsync(
                        $"确定要删除选中的 {waitToDelete.Count} 条记录吗？", "确认删除"))
                        return;

                    var req = new BatchRequestGeneric<SysViewRouteDto>
                    {
                        DeleteIdList = waitToDelete.Select(x => x.Id).ToList()
                    };

                    var rst = await _api.BatchSaveAsync(req);

                    if (rst.Success)
                    {
                        await LoadAsync();
                        _msg.ShowCardAsync("删除成功！");
                    }
                    else
                    {
                        await _msg.ShowErrorAsync("删除失败：" + rst.Message);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                await _msg.ShowErrorAsync("删除异常：" + ex.Message);
            }
            finally
            {
                StatusMessage = "准备就绪";
            }
        }


        // ============= 数据加载 =============
        public RelayCommand LoadAsyncCommand { get; }
        private async Task LoadAsync()
        {
            StatusMessage = "正在加载路由配置...";
            using (BusyScope())
            {
                try
                {
                    var result = await _api.GetAllAsync();
                    if (!result.Success)
                    {
                        StatusMessage = "加载失败：" + result.Message;
                        return;
                    }

                    CurrentTable.ReLoadRangeFromDtoList(result.Data.ToList());

                    StatusMessage = $"共 {result.Data.Count} 条路由配置";
                }
                catch (Exception ex)
                {
                    StatusMessage = "加载异常：" + ex.Message;
                }


            }

        }

        //反选命令
        public RelayCommand InvertCheckCommand { get; private set; }
        private void InvertCheck()
        {
            CurrentTable.InvertCheckAll();
        }











    }
}
