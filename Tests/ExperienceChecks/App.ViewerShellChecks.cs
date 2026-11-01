using System.Reflection;
using Fallery.Models;
using Microsoft.UI.Xaml.Controls;

namespace Fallery.ExperienceChecks;

public partial class App
{
    private static int RunViewerShellChecks(MainWindow window)
    {
        int checks = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); checks++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string method, params object?[] args) => typeof(MainWindow).GetMethod(method, flags)!.Invoke(window, args);
        var player = new MouseSeekPlayer { IsMuted = true, Volume = 0 };
        var field = typeof(MainWindow).GetField("_videoPlayer", flags)!;
        field.SetValue(window, player);
        try
        {
            player.Play();
            Call("UpdateTaskbarPlaybackProgress");
            var progress = typeof(MainWindow).GetField("_taskbarProgress", flags)!.GetValue(window)!;
            var update = progress.GetType().GetMethod("Update")!;
            bool Update(double position, double duration, bool playing) =>
                (bool)update.Invoke(progress, [TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(duration), playing])!;
            Check(Update(20, 60, true), "原生任务栏正常进度调用失败");
            Check(Update(20, 60, false), "原生任务栏暂停状态调用失败");
            Check(Update(40, 60, true), "原生任务栏从暂停恢复播放失败");
            Check(Update(-5, 60, true) && Update(70, 60, true), "任务栏进度应限制在有效范围");
            Check(Update(0, 0, false), "未知时长应移除任务栏进度");
            Check((bool)progress.GetType().GetMethod("Clear")!.Invoke(progress, null)!, "清除任务栏进度失败");
        }
        finally { Call("StopVideo", false); }

        var selected = (HashSet<string>)typeof(MainWindow).GetField("_selectedPaths", flags)!.GetValue(window)!;
        var oldSelection = selected.ToArray();
        selected.Clear();
        var first = new MediaItem(Path.Combine(AppContext.BaseDirectory, "menu-photo.jpg"), MediaKind.Image);
        var second = new MediaItem(Path.Combine(AppContext.BaseDirectory, "menu-video.mp4"), MediaKind.Video);
        window.GalleryState.Items.AddRange([first, second]);
        selected.Add(first.Path); selected.Add(second.Path);
        try
        {
            var galleryMenu = (MenuFlyout)Call("BuildItemMenu", first.Path, false, null, true, false)!;
            Check(galleryMenu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "复制 2 项"), "浏览区应保留原有多选菜单");
            foreach (var item in new[] { first, second })
            {
                var menu = (MenuFlyout)Call("BuildItemMenu", item.Path, false, null, true, true)!;
                var entries = menu.Items.OfType<MenuFlyoutItem>().ToList();
                Check(entries.Any(i => Equals(i.Tag, "reveal")), "查看器菜单应提供资源管理器定位");
                Check(entries.Any(i => Equals(i.Tag, "copy-path")), "查看器菜单应提供复制路径");
                Check(entries.Any(i => Equals(i.Tag, "copy-file") && i.Text == "复制文件"), "查看器只能复制当前文件");
                Check(entries.Any(i => Equals(i.Tag, "delete") && i.Text == "删除"), "查看器只能删除当前文件");
            }
            Check(selected.Count == 2, "查看器菜单构建不能改动浏览区多选状态");
        }
        finally
        {
            selected.Clear();
            selected.UnionWith(oldSelection);
            window.GalleryState.Items.Remove(first);
            window.GalleryState.Items.Remove(second);
        }
        return checks;
    }
}
