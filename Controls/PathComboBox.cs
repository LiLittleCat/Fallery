using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Fallery.Controls;

/// <summary>路径下拉菜单与输入框等宽，不能按长路径的自然文本宽度撑开弹出层。</summary>
public sealed partial class PathComboBox : ComboBox
{
    private FrameworkElement? _popupBorder;

    public PathComboBox()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollMode(this, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Disabled);
        SizeChanged += (_, _) => ConstrainDropDown();
        DropDownOpened += (_, _) => ConstrainDropDown();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // 使用当前 WinUI ComboBox 模板的弹出层；不替换原生选择和键盘交互。
        _popupBorder = GetTemplateChild("PopupBorder") as FrameworkElement;
        if (GetTemplateChild("Popup") is Popup popup) popup.ShouldConstrainToRootBounds = true;
        if (GetTemplateChild("ScrollViewer") is ScrollViewer scroll) scroll.MinWidth = 0;
        ConstrainDropDown();
    }

    private void ConstrainDropDown()
    {
        if (_popupBorder is null || ActualWidth <= 0) return;
        _popupBorder.MinWidth = 0;
        _popupBorder.MaxWidth = _popupBorder.Width = ActualWidth;
    }
}
