using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Fallery;

public sealed partial class MainWindow
{
    private async void FolderTreeItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // 只处理节点内容，展开箭头不进入重命名；快捷入口和磁盘根节点保持原行为。
        if (sender is not FrameworkElement { DataContext: TreeViewNode { Content: Models.FolderEntry entry } node }
            || FolderTree.RootNodes.Contains(node)) return;
        e.Handled = true;
        FolderTree.SelectedNode = node;
        await RenameItemAsync(entry.Path, folder: true);
    }

    private async Task RenameItemAsync(string path, bool folder)
    {
        if (_pasting || _deleting || _settingsDialog is not null || _closed || _closing
            || System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(path)) is null) return;
        _pasting = true;
        try { await RenameItemCoreAsync(path, folder); }
        finally
        {
            _pasting = false;
            if (!_closed && !_closing && GalleryState.CurrentFolder is { } current) QueueFolderChange(current);
        }
    }

    private async Task RenameItemCoreAsync(string path, bool folder)
    {
        StopHoverPreview();
        string name = System.IO.Path.GetFileName(path);
        var input = new TextBox { Text = name, Header = "新名称", MinWidth = 300, MaxLength = 255 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(input); panel.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme,
            Title = "重命名", Content = panel, PrimaryButtonText = "确定", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        dialog.Opened += (_, _) =>
        {
            input.Focus(FocusState.Programmatic);
            input.Select(0, folder ? name.Length : System.IO.Path.GetFileNameWithoutExtension(name).Length);
        };
        string? renamed = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            args.Cancel = true; dialog.IsPrimaryButtonEnabled = false;
            try { renamed = await FileRename.RenameAsync(path, folder, input.Text); args.Cancel = false; }
            catch (Exception ex) { error.Text = ex.Message; }
            finally { dialog.IsPrimaryButtonEnabled = true; deferral.Complete(); }
        };
        _settingsDialog = dialog;
        try { await dialog.ShowAsync(); }
        finally { if (ReferenceEquals(_settingsDialog, dialog)) _settingsDialog = null; }
        if (renamed is null || _closed || _closing) return;
        if (folder && GalleryState.CurrentFolder is { } current && IsSameOrChildFolder(current, path))
        {
            string relative = System.IO.Path.GetRelativePath(path, current);
            await NavigateAsync(System.IO.Path.GetFullPath(System.IO.Path.Combine(renamed, relative)));
        }
        else await RefreshAndSelectFilesAsync([renamed], System.IO.Path.GetDirectoryName(renamed)!);
    }

    private async Task RefreshAndSelectFilesAsync(IReadOnlyList<string> paths, string destination)
    {
        if (_closed || _closing || !string.Equals(GalleryState.CurrentFolder, destination, StringComparison.OrdinalIgnoreCase)) return;
        int version = _navigationVersion + 1;
        await NavigateAsync(destination, refresh: true);
        if (_closed || version != _navigationVersion || !string.Equals(GalleryState.CurrentFolder, destination, StringComparison.OrdinalIgnoreCase)) return;
        CancelBrowsePositionRestore();
        RestoreItemSelection(paths, paths.FirstOrDefault(), paths.FirstOrDefault());
        RememberBrowsePosition();
        if (_selectedItem is { } item) BringItemIntoView(item);
        GalleryScroller.Focus(FocusState.Programmatic);
    }

    private async Task<FileConflictChoice> ResolveFileConflictAsync(FileConflict conflict)
    {
        if (_closed || _closing) return FileConflictChoice.Cancel;
        var choices = new List<(string Label, FileConflictChoice Choice)>();
        if (conflict.SourceFolder == conflict.TargetFolder)
            choices.Add((conflict.SourceFolder ? "合并文件夹（内部重名文件将分别询问）" : "替换目标文件", FileConflictChoice.Replace));
        choices.Add(("跳过此项", FileConflictChoice.Skip));
        choices.Add(("保留两者（自动改名）", FileConflictChoice.KeepBoth));
        var options = new ComboBox { ItemsSource = choices.Select(choice => choice.Label).ToArray(), SelectedIndex = choices.FindIndex(choice => choice.Choice == FileConflictChoice.Skip), HorizontalAlignment = HorizontalAlignment.Stretch };
        var panel = new StackPanel { Spacing = 12, MaxWidth = 460 };
        string Describe(string path, bool isFolder)
        {
            try { var info = new FileInfo(path); return isFolder ? path : $"{path}\n{info.Length:N0} 字节 · {info.LastWriteTime:g}"; }
            catch { return path; }
        }
        string source = await Task.Run(() => Describe(conflict.Source, conflict.SourceFolder));
        string target = await Task.Run(() => Describe(conflict.Target, conflict.TargetFolder));
        if (_closed || _closing) return FileConflictChoice.Cancel;
        panel.Children.Add(new TextBlock { Text = $"来源：{source}\n\n目标：{target}", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        panel.Children.Add(options);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.RequestedTheme,
            Title = "目标中已有同名项目", Content = panel, PrimaryButtonText = "继续", CloseButtonText = "取消粘贴", DefaultButton = ContentDialogButton.Primary };
        _settingsDialog = dialog;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary ? choices[options.SelectedIndex].Choice : FileConflictChoice.Cancel; }
        finally { if (ReferenceEquals(_settingsDialog, dialog)) _settingsDialog = null; }
    }
}
