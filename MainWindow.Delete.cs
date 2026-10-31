using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.VisualBasic.FileIO;
using Windows.System;
using Windows.UI.Core;

namespace Fallery;

public sealed partial class MainWindow
{
    private bool _deleting;

    private static bool IsShiftDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;

    /// <summary>Delete 键的作用对象：焦点在文件树时是树上选中的目录，查看器打开时是当前大图，否则是浏览区选中项。</summary>
    private Task DeleteFocusedItemAsync(bool permanent, DependencyObject? source)
    {
        for (var element = source; element is not null; element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
        {
            if (element == FavoritesTree)
            {
                if (FavoritesTree.SelectedNode is { Content: Models.FolderEntry favorite } && !string.IsNullOrEmpty(favorite.Path))
                    RemoveFavorite(favorite.Path);
                return Task.CompletedTask;
            }
            if (element != FolderTree) continue;
            // 树根是磁盘和图片库入口，不能从这里删除。
            if (FolderTree.SelectedNode is not { Content: Models.FolderEntry entry } node || FolderTree.RootNodes.Contains(node))
                return Task.CompletedTask;
            return DeleteWithConfirmationAsync(entry.Path, true, permanent);
        }
        var item = ViewerState.IsOpen ? ViewerState.CurrentItem : _selectedItem;
        return item is null ? Task.CompletedTask : DeleteWithConfirmationAsync(item.Path, item.IsFolder, permanent);
    }

    // 资源管理器只在本地固定磁盘上使用回收站；网络位置和可移动磁盘会直接永久删除。
    private static bool SupportsRecycleBin(string path)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && !root.StartsWith(@"\\", StringComparison.Ordinal)
                && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return false; }
    }

    private async Task DeleteWithConfirmationAsync(string path, bool folder, bool permanent)
    {
        if (_deleting || _closed || _closing || _settingsDialog is not null) return;
        _deleting = true;
        try
        {
            bool recycle = !permanent && SupportsRecycleBin(path);
            string kind = folder ? "文件夹" : "文件";
            string question = recycle ? $"确实要把此{kind}放入回收站吗？"
                : permanent ? $"确实要永久删除此{kind}吗？"
                : $"此位置不支持回收站，{kind}将被永久删除。确实要删除吗？";
            var content = new StackPanel { Spacing = 8, MaxWidth = 420 };
            content.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)),
                FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetDirectoryName(path), FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme, Title = recycle ? $"删除{kind}" : $"永久删除{kind}",
                Content = content, PrimaryButtonText = "是", CloseButtonText = "否", DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || _closed) return;

            // 查看器正在显示或播放的文件需要先释放占用，否则删除会因文件被锁定而失败。
            int viewerIndex = -1;
            if (ViewerState.CurrentItem is { } current
                && (string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase) || (folder && IsSameOrChildFolder(current.Path, path))))
            {
                viewerIndex = ViewerState.CurrentIndex;
                StopVideo();
                ClearViewerImage();
            }
            await DeletePathAsync(path, folder, recycle);
            await AfterDeleteAsync(path, folder, viewerIndex);
            GalleryState.Status = recycle ? $"已移到回收站：{System.IO.Path.GetFileName(path)}" : $"已永久删除：{System.IO.Path.GetFileName(path)}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_closed) GalleryState.Status = $"删除失败：{ex.Message}";
        }
        finally { _deleting = false; }
    }

    private static Task DeletePathAsync(string path, bool folder, bool recycle)
    {
        if (!recycle)
        {
            return Task.Run(() =>
            {
                if (folder) Directory.Delete(path, recursive: true);
                else File.Delete(path);
            });
        }
        // 回收站走 Windows Shell，需要 STA 线程；放在独立线程上避免阻塞界面。
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (folder) FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                else FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private async Task AfterDeleteAsync(string path, bool folder, int viewerIndex)
    {
        if (_closed) return;
        if (folder) RemoveTreeNode(path);
        // 删掉的是当前目录或其祖先时回到被删目录的上一级。
        if (folder && GalleryState.CurrentFolder is { } currentFolder && IsSameOrChildFolder(currentFolder, path))
        {
            if (System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path)) is { } parent)
                await NavigateAsync(parent);
            return;
        }
        int index = GalleryState.Items.FindIndex(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
        double offset = GalleryScroller.VerticalOffset;
        if (!GalleryState.RemoveItem(path)) return;
        // 列表重绑会回到顶部；只删一项时保持原来的浏览位置，并把选中移到相邻项，方便连续删除。
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed && !IsFilmstrip) GalleryScroller.ChangeView(null, offset, null, true);
        });
        if (viewerIndex >= 0 && GalleryState.MediaItems.Count > 0)
            await ShowInViewerAsync(Math.Min(viewerIndex, GalleryState.MediaItems.Count - 1));
        else if (viewerIndex >= 0) CloseViewer();
        else if (index >= 0 && GalleryState.Items.Count > 0)
            SelectItem(GalleryState.Items[Math.Min(index, GalleryState.Items.Count - 1)]);
    }

    private void RemoveTreeNode(string path)
    {
        var target = NormalizeFolderPath(path);
        var pending = new Stack<IList<TreeViewNode>>();
        pending.Push(FolderTree.RootNodes);
        while (pending.TryPop(out var nodes))
        {
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                var node = nodes[i];
                if (node.Content is Models.FolderEntry entry && string.Equals(NormalizeFolderPath(entry.Path), target, StringComparison.OrdinalIgnoreCase))
                {
                    if (ReferenceEquals(_currentFolderNode, node)) _currentFolderNode = null;
                    nodes.RemoveAt(i);
                    return;
                }
                if (node.Children.Count > 0) pending.Push(node.Children);
            }
        }
    }
}
