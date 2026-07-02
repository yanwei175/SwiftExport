using Microsoft.Xaml.Behaviors;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// ============================================================
    /// TextBoxPopupAssist —— 企业级终极自动补全行为（API 搜索专用）
    /// ============================================================
    ///
    /// ✨ 设计目的：
    ///     专门用于 TextBox + Popup 的自动补全（API 搜索）场景。
    ///     最适合 DataGrid 单元格编辑时弹出搜索建议。
    ///
    /// ✨ 功能特点：
    ///     ✔ Popup 跟随当前 TextBox 定位
    ///     ✔ 支持防抖搜索（300ms）
    ///     ✔ 支持上下键移动选择
    ///     ✔ 支持回车确认选择
    ///     ✔ 支持鼠标点击确认选择
    ///     ✔ 支持 ESC 关闭 Popup
    ///     ✔ CommitCommand 始终传递当前 DataGrid 行对象
    ///     ✔ Popup 内选中项通过 ListBox.SelectedItem / ViewModel 属性同步
    ///     ✔ 完全 MVVM（无 code-behind）
    ///
    /// ✨ 适用场景：
    ///     ✔ DataGrid 单元格自动补全（最强用途）
    ///     ✔ SKU / 产品 / 客户 / 物料编码搜索
    ///     ✔ 需要 API 搜索建议的输入框
    ///
    /// ✨ 不适用场景：
    ///     ❌ Popup 内部有 TextBox（会抢焦点）
    ///     ❌ 每个 TextBox 都 new 一个 Popup（性能差）
    ///     ❌ 本地过滤（请用 AutoCompleteAssist）
    ///     ❌ 需要复杂 UI（请做成 UserControl）
    ///
    /// ✨ 使用方式（示例）：
    ///     <TextBox>
    ///         <i:Interaction.Behaviors>
    ///             <assist:TextBoxPopupAssist
    ///                 Popup="{Binding ElementName=PopupForThisColumn}"
    ///                 SearchCommand="{Binding DataContext.SearchCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}"
    ///                 CommitCommand="{Binding DataContext.CommitCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}"/>
    ///         </i:Interaction.Behaviors>
    ///     </TextBox>
    ///
    /// ============================================================
    /// </summary>
    public class TextBoxPopupAssist : Behavior<TextBox>
    {
        // ===========================
        // Popup（必须绑定）
        // ===========================
        public Popup Popup
        {
            get => (Popup)GetValue(PopupProperty);
            set => SetValue(PopupProperty, value);
        }
        public static readonly DependencyProperty PopupProperty =
            DependencyProperty.Register(nameof(Popup), typeof(Popup), typeof(TextBoxPopupAssist));

        // ===========================
        // SearchCommand（必须绑定）
        // 参数：string keyword
        // ===========================
        public ICommand SearchCommand
        {
            get => (ICommand)GetValue(SearchCommandProperty);
            set => SetValue(SearchCommandProperty, value);
        }
        public static readonly DependencyProperty SearchCommandProperty =
            DependencyProperty.Register(nameof(SearchCommand), typeof(ICommand), typeof(TextBoxPopupAssist));

        // ===========================
        // CommitCommand（必须绑定）
        // 参数：当前 DataGrid 行对象（RowModel）
        // 注意：不是 Popup 里的选中项。
        // Popup 选中项应通过 ViewModel 的 SelectedItem 属性读取。
        // ===========================
        public ICommand CommitCommand
        {
            get => (ICommand)GetValue(CommitCommandProperty);
            set => SetValue(CommitCommandProperty, value);
        }
        public static readonly DependencyProperty CommitCommandProperty =
            DependencyProperty.Register(nameof(CommitCommand), typeof(ICommand), typeof(TextBoxPopupAssist));

        // ===========================
        // 内部字段
        // ===========================
        private CancellationTokenSource _cts;
        private ListBox _popupListBox;
        private bool _suppressTextChanged;

        protected override void OnAttached()
        {
            AssociatedObject.GotFocus += OnGotFocus;
            AssociatedObject.TextChanged += OnTextChanged;
            AssociatedObject.PreviewKeyDown += OnPreviewKeyDown;
            AssociatedObject.Loaded += OnAssociatedObjectLoaded;
        }

        protected override void OnDetaching()
        {
            AssociatedObject.GotFocus -= OnGotFocus;
            AssociatedObject.TextChanged -= OnTextChanged;
            AssociatedObject.PreviewKeyDown -= OnPreviewKeyDown;
            AssociatedObject.Loaded -= OnAssociatedObjectLoaded;

            _cts?.Cancel();
            DetachPopupListBox();
        }

        // ===========================
        // 控件加载完成后，尝试挂接 Popup 内部 ListBox 的鼠标事件
        // 这里不直接依赖命名元素，而是递归查找 Popup.Child 下的第一个 ListBox
        // ===========================
        private void OnAssociatedObjectLoaded(object sender, RoutedEventArgs e)
        {
            EnsurePopupListBox();
        }

        // ===========================
        // TextBox 获得焦点 → 刷新 Popup 定位目标
        // 注意：这里不强制打开 Popup，Popup 的打开时机仍由 ViewModel 控制
        // ===========================
        private void OnGotFocus(object sender, RoutedEventArgs e)
        {
            if (Popup == null) return;

            Popup.PlacementTarget = AssociatedObject;
            EnsurePopupListBox();
        }



        // ===========================
        // 通过 Popup.Tag 存储最后一次提交时间戳（毫秒）
        // 这样即使 DataGrid 重建了 TextBox（新 Behavior 实例），
        // 也能从共享的 Popup 对象上读取到提交时间，避免触发二次搜索。
        // ===========================
        private const int CommitSuppressMs = 600;

        /// <summary>
        /// 标记共享 Popup 的最后提交时间（写入 Popup.Tag）
        /// </summary>
        private void MarkPopupCommitTime()
        {
            if (Popup == null) return;
            Popup.Tag = Environment.TickCount;
        }

        /// <summary>
        /// 判断当前是否处于提交后的抑制期
        /// 从 Popup.Tag 读取上次提交时间戳
        /// </summary>
        private bool IsInCommitSuppressWindow()
        {
            if (Popup?.Tag is int lastCommitTick)
            {
                return Math.Abs(Environment.TickCount - lastCommitTick) < CommitSuppressMs;
            }
            return false;
        }




        // ===========================
        // TextChanged → 防抖搜索
        // 300ms 内连续输入只会触发最后一次查询
        // 注意：如果当前变化来自内部回填，则跳过本次搜索，避免选中后再次触发 API 查询
        // 同时通过统一调度入口执行命令，降低宿主线程模型差异导致的异常概率
        // ===========================
        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            // 本实例内部抑制（回填期间）
            if (_suppressTextChanged) return;
            // 跨实例抑制：DataGrid 重建 TextBox 后新实例也能感知到提交窗口
            if (IsInCommitSuppressWindow()) return;
            if (SearchCommand == null) return;

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var keyword = AssociatedObject.Text;

            Task.Delay(300, _cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                RunOnUi(() =>
                {
                    if (SearchCommand.CanExecute(keyword))
                    {
                        SearchCommand.Execute(keyword);
                    }
                });
            }, TaskScheduler.Default);
        }

        // ===========================
        // 键盘事件：上下键、回车、ESC
        // 说明：
        //   1. 上下键只负责移动 Popup 内部 ListBox 的选中项
        //   2. 回车时传给 CommitCommand 的仍然是当前 DataGrid 行对象
        //   3. Popup 里真正被选中的产品，需要由 ViewModel 自己从 SelectedItem 属性读取
        // ===========================
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Popup == null || !Popup.IsOpen) return;

            var listBox = EnsurePopupListBox();
            if (listBox == null) return;

            if (e.Key == Key.Down)
            {
                MoveSelection(listBox, 1);
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                MoveSelection(listBox, -1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                CommitCurrentRow();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Popup.IsOpen = false;
                e.Handled = true;
            }
        }

        // ===========================
        // 鼠标点击 Popup 项 → 延迟到选择状态完成后，再提交当前行对象
        // 之所以使用 BeginInvoke，是为了确保 ListBox.SelectedItem / ViewModel 绑定已更新
        // 同时仅在真正点击到 ListBoxItem 时才提交，避免点到空白区域或滚动条误触发
        // ===========================
        private void PopupListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) == null) return;

            RunOnUiAsync(CommitCurrentRow);
        }

        // ===========================
        // 获取并缓存 Popup 内部的 ListBox，同时确保事件只挂接一次
        // ===========================
        private ListBox EnsurePopupListBox()
        {
            var listBox = FindDescendant<ListBox>(Popup?.Child);
            if (ReferenceEquals(_popupListBox, listBox)) return _popupListBox;

            DetachPopupListBox();
            _popupListBox = listBox;

            if (_popupListBox != null)
            {
                _popupListBox.PreviewMouseLeftButtonUp += PopupListBox_PreviewMouseLeftButtonUp;
            }

            return _popupListBox;
        }

        // ===========================
        // 解绑旧的 ListBox 事件，防止重复订阅
        // ===========================
        private void DetachPopupListBox()
        {
            if (_popupListBox == null) return;

            _popupListBox.PreviewMouseLeftButtonUp -= PopupListBox_PreviewMouseLeftButtonUp;
            _popupListBox = null;
        }

        // ===========================
        // 在 Popup 列表中移动当前选中项，并保证滚动可见
        // 这里不会更改焦点，只更新 Selector 的 SelectedIndex
        // ===========================
        private static void MoveSelection(ListBox listBox, int step)
        {
            if (listBox == null || listBox.Items.Count == 0) return;

            var nextIndex = listBox.SelectedIndex;
            if (nextIndex < 0)
            {
                nextIndex = step > 0 ? 0 : listBox.Items.Count - 1;
            }
            else
            {
                nextIndex += step;
                if (nextIndex < 0) nextIndex = 0;
                if (nextIndex >= listBox.Items.Count) nextIndex = listBox.Items.Count - 1;
            }

            listBox.SelectedIndex = nextIndex;
            listBox.ScrollIntoView(listBox.SelectedItem);
        }

        // ===========================
        // 向 CommitCommand 提交当前 DataGrid 行对象
        // 说明：
        //   - 命令参数固定传递 AssociatedObject.DataContext
        //   - Popup 当前选中项不在这里直接传递
        //   - 由外层 ViewModel 自行读取绑定的 SelectedItem 属性
        //   - 如果当前没有有效选中项，则不提交也不关闭 Popup
        //   - 在执行提交前临时抑制 TextChanged，避免回填值再次触发搜索
        // ===========================
        private void CommitCurrentRow()
        {
            var listBox = EnsurePopupListBox();
            if (listBox == null || listBox.Items.Count == 0) return;

            if (listBox.SelectedItem == null)
            {
                listBox.SelectedIndex = 0;
            }

            if (listBox.SelectedItem == null) return;

            // 中文注释：优先取当前编辑框绑定的行对象。
            var rowObject = AssociatedObject.DataContext;

            // 中文注释：单元格模式下，DataGrid 可能未更新 SelectedItem，
            // 此时兜底从 CurrentCell.Item 取行对象，避免 rowObj 丢失。
            if (rowObject == null)
            {
                var dataGrid = FindAncestor<DataGrid>(AssociatedObject);
                rowObject = dataGrid?.CurrentCell.Item;
            }

            if (rowObject == null) return;

            // 在提交前先打上时间戳，使后续任何新 TextBox 实例都在抑制窗口内
            MarkPopupCommitTime();

            _suppressTextChanged = true;
            try
            {
                if (CommitCommand?.CanExecute(rowObject) == true)
                {
                    CommitCommand.Execute(rowObject);
                }
            }
            finally
            {
                RunOnUiAsync(() =>
                {
                    _suppressTextChanged = false;
                });
            }

            if (Popup != null)
            {
                Popup.IsOpen = false;
            }
        }

        // ===========================
        // 统一 UI 线程调用入口
        // 优先使用 MateWpfThemes.FW.DispatcherProvider，
        // 由宿主在启动时注入 Dispatcher；若未注入，DispatcherProvider 内部会回退为直接执行。
        // ===========================
        private static void RunOnUi(Action action)
        {
            DispatcherProvider.Invoke(action);
        }

        // ===========================
        // 统一 UI 异步投递入口
        // 与 RunOnUi 相同策略，由 DispatcherProvider 统一处理。
        // ===========================
        private static void RunOnUiAsync(Action action)
        {
            DispatcherProvider.InvokeAsync(action);
        }

        // ===========================
        // 递归向上查找指定类型的可视化父元素
        // 主要用于判断鼠标是否点中了某个 ListBoxItem
        // ===========================
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                var target = current as T;
                if (target != null) return target;
                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        // ===========================
        // 递归查找指定类型的可视化子元素
        // ===========================
        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;

            if (root is T target) return target;

            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var result = FindDescendant<T>(child);
                if (result != null) return result;
            }

            return null;
        }
    }
}

