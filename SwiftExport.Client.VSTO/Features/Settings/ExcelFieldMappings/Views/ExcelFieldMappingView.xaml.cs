// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using System;
using System.Windows;
using System.Windows.Controls;
using SwiftExport.Client.VSTO.Features.Settings.ExcelFieldMappings.ViewModels;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;

namespace SwiftExport.Client.VSTO.Features.Settings.ExcelFieldMappings.Views
{
    /// <summary>连接设置窗口加载与分组选择事件，业务状态由 ViewModel 维护。</summary>
    public partial class ExcelFieldMappingView : UserControl
    {
        private readonly ExcelFieldMappingViewModel model;
        private bool selecting;
        /// <summary>使用当前设置窗口 Scope 创建页面。</summary>
        public ExcelFieldMappingView(ExcelFieldMappingViewModel model)
        {
            InitializeComponent(); this.model = model; DataContext = model;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs args)
        {
            try { await model.InitializeAsync(); }
            catch (OperationCanceledException) { }
        }

        // 选择控件先更新自身；取消或失败时将它恢复到 ViewModel 已接纳的分组。
        private async void OnGroupSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (selecting || model == null || !(GroupSelector.SelectedItem is ExcelFieldGroupDto selected)) return;
            selecting = true;
            try { await model.SelectGroupAsync(selected.GroupCode); }
            finally
            {
                GroupSelector.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, model.CurrentGroup);
                selecting = false;
            }
        }
    }
}
