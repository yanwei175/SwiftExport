using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Resources.Controls;

/// <summary>
/// 为业务选择场景提供明确确认交互：方向键只浏览，鼠标释放或 Enter 才确认。
/// </summary>
public sealed class CommittedAutoSuggestBox : AutoSuggestBox
{
    /// <summary>
    /// 用户关闭候选或把焦点移出控件、需要恢复已提交显示时发生。
    /// </summary>
    public event EventHandler? EditAbandoned;

    /// <summary>
    /// 用户通过编辑删除全部文字或点击清空按钮时发生；程序设置空文字不会触发。
    /// </summary>
    public event EventHandler? UserClearRequested;

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        var fontSizeBinding = new Binding(nameof(FontSize)) { Source = this };
        TextBox?.SetBinding(Control.FontSizeProperty, fontSizeBinding);
        SuggestionsList?.SetBinding(Control.FontSizeProperty, new Binding(nameof(FontSize)) { Source = this });

    }

    /// <inheritdoc />
    protected override void AcquireTemplateResources()
    {
        ReleaseInteractionHandlers();

        if (TextBox is not null)
        {
            TextBox.PreviewKeyDown += TextBoxOnPreviewKeyDown;
            TextBox.LostKeyboardFocus += EditorPartOnLostKeyboardFocus;
            TextBox.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(ClearButtonOnClick), true);
        }

        if (SuggestionsList is not null)
        {
            SuggestionsList.PreviewKeyDown += SuggestionsListOnPreviewKeyDown;
            SuggestionsList.PreviewMouseLeftButtonUp += SuggestionsListOnPreviewMouseLeftButtonUp;
            SuggestionsList.LostKeyboardFocus += EditorPartOnLostKeyboardFocus;
        }

        base.AcquireTemplateResources();
    }

    /// <inheritdoc />
    protected override void ReleaseTemplateResources()
    {
        ReleaseInteractionHandlers();
        base.ReleaseTemplateResources();
    }

    /// <inheritdoc />
    protected override void OnTextChanged(AutoSuggestionBoxTextChangeReason reason, string text)
    {
        base.OnTextChanged(reason, text);
        if (reason is AutoSuggestionBoxTextChangeReason.UserInput && string.IsNullOrEmpty(text))
        {
            UserClearRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    protected override void OnSuggestionChosen(object selectedItem)
    {
        // 4.3.0 在候选高亮变化时调用此方法；业务提交统一由明确输入事件发起。
    }

    private void TextBoxOnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape && IsSuggestionListOpen)
        {
            SetCurrentValue(IsSuggestionListOpenProperty, false);
            EditAbandoned?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Down && !IsSuggestionListOpen && string.IsNullOrWhiteSpace(TextBox?.Text))
        {
            SetCurrentValue(IsSuggestionListOpenProperty, true);
            e.Handled = true;
        }
    }

    private void SuggestionsListOnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter && SuggestionsList?.SelectedItem is { } selectedItem)
        {
            SetCurrentValue(IsSuggestionListOpenProperty, false);
            base.OnSuggestionChosen(selectedItem);
            e.Handled = true;
            return;
        }

        if (e.Key is not Key.Escape)
        {
            return;
        }

        SetCurrentValue(IsSuggestionListOpenProperty, false);
        _ = TextBox?.Focus();
        EditAbandoned?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void SuggestionsListOnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionsList?.SelectedItem is not { } selectedItem || !IsFromListItem(e.OriginalSource))
        {
            return;
        }

        SetCurrentValue(IsSuggestionListOpenProperty, false);
        base.OnSuggestionChosen(selectedItem);
        e.Handled = true;
    }

    private void EditorPartOnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (!IsKeyboardFocusWithin && SuggestionsList?.IsKeyboardFocusWithin is not true)
                {
                    EditAbandoned?.Invoke(this, EventArgs.Empty);
                }
            }));
    }

    private void ClearButtonOnClick(object sender, RoutedEventArgs e)
    {
        // WPF-UI 4.3.0 把清空按钮造成的文字变化标记为 ProgrammaticChange，需由明确点击补足用户意图。
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (string.IsNullOrEmpty(TextBox?.Text))
                {
                    UserClearRequested?.Invoke(this, EventArgs.Empty);
                }
            }));
    }

    private static bool IsFromListItem(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Wpf.Ui.Controls.ListViewItem)
            {
                return true;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void ReleaseInteractionHandlers()
    {
        if (TextBox is not null)
        {
            TextBox.PreviewKeyDown -= TextBoxOnPreviewKeyDown;
            TextBox.LostKeyboardFocus -= EditorPartOnLostKeyboardFocus;
            TextBox.RemoveHandler(ButtonBase.ClickEvent, new RoutedEventHandler(ClearButtonOnClick));
        }

        if (SuggestionsList is not null)
        {
            SuggestionsList.PreviewKeyDown -= SuggestionsListOnPreviewKeyDown;
            SuggestionsList.PreviewMouseLeftButtonUp -= SuggestionsListOnPreviewMouseLeftButtonUp;
            SuggestionsList.LostKeyboardFocus -= EditorPartOnLostKeyboardFocus;
        }
    }

}
