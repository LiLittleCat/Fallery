using System.Reflection;
using Fallery.Controls;
using Fallery.Models;
using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static async Task<int> RunSelectionChecksAsync(MainWindow window)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var root = (FrameworkElement)window.Content;
        var permanentDialog = (ContentDialog)Call("CreateIdenticalDeleteConfirmation", "C:\\sample", 12, true, IdenticalImageMatchMode.NameSizeContent)!;
        Check(permanentDialog.PrimaryButtonText == "彻底删除" && permanentDialog.DefaultButton == ContentDialogButton.Close
            && ((TextBlock)permanentDialog.Content).Text.Contains("不会放入回收站")
            && ((TextBlock)permanentDialog.Content).Text.Contains("12"), "彻底删除必须单独确认数量与不进回收站，默认取消");
        var recycleDialog = (ContentDialog)Call("CreateIdenticalDeleteConfirmation", "C:\\sample", 12, false, IdenticalImageMatchMode.NameSizeContent)!;
        Check(recycleDialog.PrimaryButtonText == "移到回收站" && recycleDialog.DefaultButton == ContentDialogButton.Close
            && !((TextBlock)recycleDialog.Content).Text.Contains("不会放入回收站"), "回收站操作不能复用永久删除文案");
        Check(((TextBlock)recycleDialog.Content).Text.Contains("文件名、大小和内容均相同"), "默认删除确认必须明确三重匹配规则");
        foreach (bool permanent in new[] { false, true })
        {
            var nameDialog = (ContentDialog)Call("CreateIdenticalDeleteConfirmation", "C:\\sample", 12, permanent, IdenticalImageMatchMode.NameOnly)!;
            Check(nameDialog.Title.ToString()!.Contains("同名图片")
                && ((TextBlock)nameDialog.Content).Text.Contains("仅文件名相同，大小和内容可能不同")
                && nameDialog.DefaultButton == ContentDialogButton.Close,
                "仅名称匹配的回收站/彻底删除确认都必须明确内容可能不同并默认取消");
        }
        var matchOption = (CheckBox)typeof(MainWindow).GetMethod("CreateIdenticalImageMatchOption", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        Check(matchOption.IsChecked == false && ((TextBlock)matchOption.Content).Text.Contains("大小和内容可不同"),
            "仅名称匹配不能默认开启，选项需明确其含义");
        var namedMatch = new IdenticalImageMatch("C:\\sample\\logo.png", default, "hash", "C:\\other\\logo.png") { Mode = IdenticalImageMatchMode.NameOnly };
        Check(new MainWindow.IdenticalImageRow(namedMatch).Name.Contains("仅文件名相同"), "仅同名结果不能标成内容相同");
        var items = window.GalleryState.Items;
        checks += await RunMarqueeChecksAsync(window);
        var paths = (HashSet<string>)typeof(MainWindow).GetField("_selectedPaths", flags)!.GetValue(window)!;
        void Click(int index, bool control = false, bool shift = false) => Call("ApplyItemSelection", items[index], control, shift);
        Click(35);
        Call("SelectAllItems");
        Check(paths.Count == items.Count, "Ctrl+A 的生产选择方法应包含所有虚拟化和未实现的项目");
        Call("SelectItem", new object[] { null! });
        Check(paths.Count == 0, "取消选择应清空实际操作目标");
        Click(35);
        Call("MoveKeyboardSelection", Windows.System.VirtualKey.End, false, true);
        Check(paths.Count == items.Count - 35 && paths.Contains(items[^1].Path), "Shift+End 应从锚点选到末项");
        Call("MoveKeyboardSelection", Windows.System.VirtualKey.Home, false, true);
        Check(paths.Count == 36 && paths.Contains(items[0].Path), "Shift+Home 应保留原锚点并收缩另一侧选择");
        Click(35);
        Call("MoveKeyboardSelection", Windows.System.VirtualKey.End, true, false);
        Check(paths.Count == 1 && paths.Contains(items[35].Path), "Ctrl+方向定位应只移动焦点，不改变选择");
        Call("MoveKeyboardSelection", Windows.System.VirtualKey.Home, false, false);
        Check(paths.Count == 1 && paths.Contains(items[0].Path), "Home 应选中并定位首项");
        int neighbor = (int)Call("FindKeyboardNeighbor", 0, Windows.System.VirtualKey.Right)!;
        Check(neighbor > 0 && neighbor < items.Count, "方向导航应使用实际布局寻找相邻卡片");
        Click(35);
        Click(45, control: true);
        var duplicateMenu = (MenuFlyout)Call("BuildItemMenu", items[45].Path, false, null!, true, false)!;
        Check(!duplicateMenu.Items.OfType<MenuFlyoutItem>().Any(entry => Equals(entry.Tag, "identical-images")), "混选文件夹时不能开启相同图片清理");
        Check(paths.Count == 2 && paths.Contains(items[35].Path) && paths.Contains(items[45].Path), "Ctrl 点击应追加不相邻的文件夹和文件");
        Click(35, control: true);
        Check(paths.Count == 1 && paths.Contains(items[45].Path), "Ctrl 再次点击应只取消该项");
        duplicateMenu = (MenuFlyout)Call("BuildItemMenu", items[45].Path, false, null!, true, false)!;
        Check(duplicateMenu.Items.OfType<MenuFlyoutItem>().Any(entry => Equals(entry.Tag, "identical-images")), "单选图片应提供相同内容清理入口");
        Click(40, shift: true);
        Check(paths.Count == 6 && paths.Contains(items[35].Path) && paths.Contains(items[40].Path)
            && !paths.Contains(items[45].Path), "Shift 必须从最后点击锚点选择连续范围并替换旧选择");
        Click(37, shift: true);
        Check(paths.Count == 3 && !paths.Contains(items[40].Path), "连续 Shift 点击应收缩范围，不能移动锚点");
        Click(45, control: true, shift: true);
        Check(paths.Count == 11, "Ctrl+Shift 应追加范围");
        var targets = ((string Path, bool Folder)[])Call("SelectedFileTargets")!;
        Check(targets.Length == 11 && targets.Count(target => target.Folder) == 5,
            "批量操作应包含完整选中范围中的文件夹和媒体，按显示顺序提供");
        Check(((TextBlock)root.FindName("SelectionCountText")).Text == "已选择 11 项", "底栏应同步多选数量");
        var menu = (MenuFlyout)Call("BuildItemMenu", items[40].Path, false, null!, true, false)!;
        Check(menu.Items.OfType<MenuFlyoutItem>().Any(entry => Equals(entry.Tag, "copy-file") && entry.Text == "复制 11 项")
            && menu.Items.OfType<MenuFlyoutItem>().Any(entry => Equals(entry.Tag, "delete") && entry.Text == "删除 11 项"),
            "右键批量复制和删除必须明确目标数量");
        var selectedBefore = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await ((Task)Call("NavigateAsync", window.GalleryState.CurrentFolder!, false, true)!).WaitAsync(TimeSpan.FromSeconds(20));
        await Task.Delay(150); root.UpdateLayout();
        Check(paths.SetEquals(selectedBefore), "刷新目录后必须按路径恢复多选，不依赖旧模型引用");
        window.GalleryState.Descending = true;
        await window.GalleryState.ApplySortAsync();
        await Task.Delay(150); root.UpdateLayout();
        Check(paths.SetEquals(selectedBefore), "重新排序应保留选中路径");
        var active = window.GalleryState.Items.First(item => paths.Contains(item.Path));
        Call("BringItemIntoView", active);
        await Task.Delay(150); root.UpdateLayout();
        var realized = (Dictionary<UIElement, MediaItem>)typeof(MainWindow).GetField("_realizedItems", flags)!.GetValue(window)!;
        Check(realized.Keys.OfType<MediaTile>().All(tile => tile.IsSelected == (tile.Item is not null && paths.Contains(tile.Item.Path))),
            "虚拟化滚动与复用后的卡片高亮必须与多选集合一致");
        int changes = 0;
        void Changed() => changes++;
        window.GalleryState.ItemsChanged += Changed;
        try { window.GalleryState.RemoveItems(selectedBefore); }
        finally { window.GalleryState.ItemsChanged -= Changed; }
        Check(changes == 1 && window.GalleryState.Items.All(item => !selectedBefore.Contains(item.Path)),
            "批量移除模型只能发布一次列表变更，不能逐项重建界面");
        // 这里只移除隔离样本的模型，没有删除任何文件或访问用户剪贴板。
        Call("SelectItem", window.GalleryState.Items[0]);
        Check(paths.Count == 1 && ((TextBlock)root.FindName("SelectionCountText")).Text == "", "普通单选应清除多选状态和计数");
        // 连续走生产删除后处理；只移除隔离样本模型，不删除磁盘文件，不点击确认框。
        var scroller = (ScrollViewer)root.FindName("GalleryScroller");
        for (int step = 0; step < 3; step++)
        {
            var current = ((string Path, bool Folder)[])Call("SelectedFileTargets")!;
            Check(current.Length == 1, "连续删除前必须有一个实际选中目标");
            string next = window.GalleryState.Items[1].Path;
            // 模拟焦点已从被回收卡片落到了滑块，验证删除后能交回浏览区域。
            ((Slider)root.FindName("ThumbSize")).Focus(FocusState.Programmatic);
            await (Task)Call("AfterDeleteAsync", current[0].Path, current[0].Folder, -1)!;
            await Task.Delay(100); root.UpdateLayout();
            var remaining = ((string Path, bool Folder)[])Call("SelectedFileTargets")!;
            Check(remaining.Length == 1 && remaining[0].Path == next && paths.Contains(next),
                "删除后的相邻高亮项必须同时成为下一次 Delete 的实际选择");
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            while (focused is not null && focused != scroller) focused = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(focused);
            Check(focused == scroller, "删除后的键盘焦点必须回到浏览区，不能留在滑块或文件树");
        }
        // 模拟文件移除后索引仍在同步，首次点击必须立即生效；不删除任何磁盘文件。
        typeof(MainWindow).GetField("_deleting", flags)!.SetValue(window, true);
        typeof(MainWindow).GetField("_deleteSelectionReady", flags)!.SetValue(window, false);
        try
        {
            var pendingTarget = window.GalleryState.Items[0];
            Call("SelectItem", pendingTarget);
            Call("ApplyItemSelection", window.GalleryState.Items[1], false, false);
            Check(paths.SetEquals(new[] { pendingTarget.Path }), "实际文件删除尚未完成时仍需避免选择被中途改写");
            await (Task)Call("AfterDeleteAsync", pendingTarget.Path, pendingTarget.IsFolder, -1)!;
            var clicked = window.GalleryState.Items[1];
            Call("ApplyItemSelection", clicked, false, false);
            Check(paths.SetEquals(new[] { clicked.Path }), "删除后索引仍在同步时，第一次选择就必须成为实际操作目标");
            Call("FocusGalleryItem", clicked);
            var clickFocus = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot);
            Check(clickFocus is MediaTile { Item: { } focusedItem } && focusedItem.Path == clicked.Path,
                "删除后的点击应能直接聚焦实际卡片");
            await Task.Delay(100); root.UpdateLayout();
            Check(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot), clickFocus)
                && paths.SetEquals(new[] { clicked.Path }), "删除后排队的恢复不能覆盖用户的新点击、选择或焦点");
            var additional = window.GalleryState.Items[2];
            Call("ApplyItemSelection", additional, true, false);
            Check(paths.SetEquals(new[] { clicked.Path, additional.Path }), "删除后的缓存同步期间 Ctrl 多选也应立即生效");
        }
        finally
        {
            typeof(MainWindow).GetField("_deleting", flags)!.SetValue(window, false);
            typeof(MainWindow).GetField("_deleteSelectionReady", flags)!.SetValue(window, false);
        }
        var tree = (TreeView)root.FindName("FolderTree");
        await (Task)Call("ReturnToFolderTreeAsync")!;
        var treeNode = tree.SelectedNode!;
        string browseFolder = window.GalleryState.CurrentFolder!;
        var selectedBeforeTransfer = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool FocusWithin(DependencyObject target)
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            while (focused is not null && focused != target) focused = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(focused);
            return focused == target;
        }
        Check(treeNode.Content is FolderEntry entry && entry.Path == browseFolder && FocusWithin(tree),
            "Esc 返回应把实际焦点交给当前目录树节点");
        Check((Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) as Control)?.FocusState == FocusState.Pointer,
            "自动返回树时不应强制显示键盘焦点框");
        await (Task)Call("EnterGalleryFromTreeAsync", tree, treeNode)!;
        Check(FocusWithin(scroller) && paths.SetEquals(selectedBeforeTransfer), "树进入文件区应保留有效选择并交接键盘焦点");
        await (Task)Call("ReturnToFolderTreeAsync")!;
        Check(FocusWithin(tree) && paths.SetEquals(selectedBeforeTransfer), "返回文件树不能清空文件区的选择");
        var navigationTree = new TreeView { SelectionMode = TreeViewSelectionMode.Single };
        var parentNode = new TreeViewNode { Content = new FolderEntry("C:\\sample", "parent"), IsExpanded = true };
        var firstChild = new TreeViewNode { Content = new FolderEntry("C:\\sample\\one", "one") };
        var secondChild = new TreeViewNode { Content = new FolderEntry("C:\\sample\\two", "two") };
        parentNode.Children.Add(firstChild); parentNode.Children.Add(secondChild);
        var sibling = new TreeViewNode { Content = new FolderEntry("C:\\sibling", "sibling") };
        navigationTree.RootNodes.Add(parentNode); navigationTree.RootNodes.Add(sibling);
        Call("MoveTreeSelection", navigationTree, firstChild, Windows.System.VirtualKey.Down);
        Check(navigationTree.SelectedNode == secondChild, "树下方向键应同步选择到下一可见节点");
        Call("MoveTreeSelection", navigationTree, secondChild, Windows.System.VirtualKey.Up);
        Check(navigationTree.SelectedNode == firstChild, "树上方向键应同步选择到上一可见节点");
        Call("MoveTreeSelection", navigationTree, firstChild, Windows.System.VirtualKey.Left);
        Call("MoveTreeSelection", navigationTree, parentNode, Windows.System.VirtualKey.Left);
        Call("MoveTreeSelection", navigationTree, parentNode, Windows.System.VirtualKey.Down);
        Check(!parentNode.IsExpanded && navigationTree.SelectedNode == sibling, "树左键应回父节点/收起，之后导航跳过隐藏子节点");
        var photo = window.GalleryState.Items.First(item => !item.IsFolder && !item.IsVideo);
        Call("SelectItem", photo);
        await (Task)Call("OpenKeyboardSelectionAsync")!;
        Check(window.ViewerState.IsOpen && window.ViewerState.CurrentItem?.Path == photo.Path, "空格/Enter 共用打开路径应显示所选图片");
        treeNode.IsExpanded = false;
        await (Task)Call("CloseViewerAndReturnToTreeAsync")!;
        Check(FocusWithin(tree), "关闭查看器后应能继续用键盘选择文件树");
        Check(tree.SelectedNode == treeNode && !treeNode.IsExpanded && paths.Contains(photo.Path),
            "关闭媒体返回同一目录不能重新展开目录路径或丢失查看项选择");
        Check((Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) as Control)?.FocusState == FocusState.Pointer,
            "空格打开后关闭应直接返回节点，不额外显示键盘焦点框");
        Call("MoveTreeSelection", tree, treeNode, Windows.System.VirtualKey.Left);
        Check((Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) as Control)?.FocusState == FocusState.Keyboard,
            "用户实际使用树方向键时仍应显示键盘焦点提示");
        await (Task)Call("OpenKeyboardSelectionAsync")!;
        Call("CloseViewer");
        Check(FocusWithin(tree)
            && (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) as Control)?.FocusState == FocusState.Pointer,
            "返回按钮关闭查看器也应直接恢复目录节点且不闪焦点框");
        return checks;
    }
}
