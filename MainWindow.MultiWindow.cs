using Microsoft.UI.Xaml;

namespace Fallery;

public sealed partial class MainWindow
{
    public event Action<string?>? NewWindowRequested;

    private void NewWindow_Click(object sender, RoutedEventArgs e) => RequestNewWindow(GalleryState.CurrentFolder);

    private void RequestNewWindow(string? path)
    {
        if (_closed || _closing) return;
        NewWindowRequested?.Invoke(path);
    }
}
