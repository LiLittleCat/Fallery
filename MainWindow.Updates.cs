using Fallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Fallery;

public sealed partial class MainWindow
{
    private StackPanel BuildUpdateSection()
    {
        var service = VelopackUpdates.Shared;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "版本与更新", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = $"{AppIdentity.DisplayName} {AppIdentity.Version}", IsTextSelectionEnabled = true });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 400, FontSize = 12 };
        var notes = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 400, MaxLines = 8, IsTextSelectionEnabled = true };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100 };
        var action = new Button();
        var cancel = new Button { Content = "取消下载" };
        var confirmation = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        confirmation.Children.Add(new TextBlock { Text = "将保存设置和续播位置，关闭所有 Fallery 窗口并重启安装更新。", TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
        var restart = new Button { Content = "确认重启更新" };
        confirmation.Children.Add(restart);
        void Refresh()
        {
            if (_closed) return;
            var value = service.Status;
            status.Text = value.Message;
            notes.Text = value.Release?.Notes ?? "";
            notes.Visibility = string.IsNullOrWhiteSpace(notes.Text) ? Visibility.Collapsed : Visibility.Visible;
            progress.Visibility = value.State is AppUpdateState.Checking or AppUpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
            progress.IsIndeterminate = value.State == AppUpdateState.Checking;
            progress.Value = value.Progress;
            action.Content = value.State switch { AppUpdateState.Available => "下载更新", AppUpdateState.Ready => "重启并更新", _ => "检查更新" };
            action.IsEnabled = value.State is AppUpdateState.Idle or AppUpdateState.Available or AppUpdateState.Ready or AppUpdateState.Latest or AppUpdateState.Failed;
            cancel.Visibility = value.State is AppUpdateState.Checking or AppUpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
            cancel.Content = value.State == AppUpdateState.Checking ? "取消检查" : "取消下载";
            restart.IsEnabled = value.State == AppUpdateState.Ready;
            if (value.State != AppUpdateState.Ready) confirmation.Visibility = Visibility.Collapsed;
        }
        void Changed() => DispatcherQueue.TryEnqueue(Refresh);
        panel.Loaded += (_, _) => { service.Changed += Changed; Refresh(); };
        panel.Unloaded += (_, _) => service.Changed -= Changed;
        action.Click += async (_, _) =>
        {
            if (service.Status.State == AppUpdateState.Ready) confirmation.Visibility = Visibility.Visible;
            else if (service.Status.State == AppUpdateState.Available) await service.DownloadAsync();
            else await service.CheckAsync();
        };
        cancel.Click += (_, _) => service.Cancel();
        restart.Click += async (_, _) =>
        {
            confirmation.Visibility = Visibility.Collapsed;
            if (App.Current is { } app) await app.ApplyUpdateAsync(this);
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(action); buttons.Children.Add(cancel);
        panel.Children.Add(status); panel.Children.Add(notes); panel.Children.Add(progress); panel.Children.Add(buttons); panel.Children.Add(confirmation);
        panel.Children.Add(new HyperlinkButton { Content = "发行页面", NavigateUri = new Uri("https://github.com/LiLittleCat/Fallery/releases") });
        panel.Children.Add(new TextBlock { Text = $"数据目录：{AppIdentity.DataDirectory}", TextWrapping = TextWrapping.Wrap, MaxWidth = 400, FontSize = 12, IsTextSelectionEnabled = true });
        Refresh();
        return panel;
    }

    internal bool CanPrepareUpdate(bool requestingWindow) => !_closed && !_closing && !_pasting && !_deleting && !_pickingFolder
        && _searchDialog is null && (requestingWindow || _settingsDialog is null);

    internal void BeginUpdateShutdown()
    {
        _closing = true;
        Root.IsHitTestVisible = false;
        StopHoverPreview();
        CloseItemMenu();
        CloseAddressMenu();
        _settingsTimer.Stop();
        GalleryState.Cancel();
        CloseViewer();
    }

    internal async Task SaveBeforeUpdateAsync()
    {
        if (!await _resumeSaveTask) throw new IOException("无法保存续播记录，请检查数据目录权限后重试。");
        if (_settingsReady && !await _settingsStore.SaveAsync(CaptureSettings()))
            throw new IOException("无法保存设置，请检查数据目录权限后重试。");
    }

    internal void CancelUpdateShutdown()
    {
        if (_closed) return;
        _closing = false;
        Root.IsHitTestVisible = true;
    }

    internal void CloseForUpdate()
    {
        _allowClose = true;
        Close();
    }
}
