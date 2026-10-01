using System;
using System.Windows;
using System.Windows.Controls;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.ViewModels;
namespace SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Views
{
    /// <summary>配置页只桥接加载和下拉切换，地址选择与保存交给 ViewModel。</summary>
    public partial class ExcelReadProfileView : UserControl
    {
        private readonly ExcelReadProfileViewModel model;
        private bool selecting;
        /// <summary>使用窗格 Scope 的模型，不通过全局容器解析服务。</summary>
        public ExcelReadProfileView(ExcelReadProfileViewModel model)
        { InitializeComponent(); this.model = model; DataContext = model; Loaded += OnLoaded; }
        /// <summary>首次打开定位导入页配置。</summary>
        public void SetInitialProfile(Guid? uid) => model.SetInitialProfile(uid);
        /// <summary>供销毁页面的宿主确认未保存修改；侧栏 × 仅隐藏，不销毁。</summary>
        public bool CanClose() => model.CanClose();
        private async void OnLoaded(object sender, RoutedEventArgs args) { await model.InitializeAsync(); }
        private async void ProfileChanged(object sender, SelectionChangedEventArgs args)
        {
            if (model == null || selecting || args.AddedItems.Count == 0 || model.IsBusy) return;
            if (!(ProfileSelector.SelectedValue is Guid uid)) return;
            selecting = true;
            try { await model.SelectAsync(uid); ProfileSelector.SelectedValue = model.CurrentUid; }
            finally { selecting = false; }
        }
    }
}
