using CommunityToolkit.Mvvm.ComponentModel;
using Fallery.Models;

namespace Fallery.ViewModels;

public sealed partial class ViewerViewModel : ObservableObject, IDisposable
{
    private CancellationTokenSource? _openCts;
    public IReadOnlyList<MediaItem> Playlist { get; private set; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(CurrentItem), nameof(Title), nameof(Counter), nameof(CanPrevious), nameof(CanNext))]
    public partial int CurrentIndex { get; set; } = -1;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string Message { get; set; } = "";
    public bool IsOpen => CurrentIndex >= 0 && CurrentIndex < Playlist.Count;
    public MediaItem? CurrentItem => IsOpen ? Playlist[CurrentIndex] : null;
    public string Title => CurrentItem?.Name ?? "";
    public string Counter => IsOpen ? $"{CurrentIndex + 1} / {Playlist.Count}" : "";
    public bool CanPrevious => IsOpen && CurrentIndex > 0;
    public bool CanNext => IsOpen && CurrentIndex + 1 < Playlist.Count;
    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public void SetPlaylist(IReadOnlyList<MediaItem> items)
    {
        var current = CurrentItem;
        Playlist = items;
        if (current is not null)
        {
            int index = items.ToList().IndexOf(current);
            if (index < 0) Close();
            else CurrentIndex = index;
        }
        foreach (var name in new[] { nameof(Playlist), nameof(CurrentItem), nameof(IsOpen), nameof(Title), nameof(Counter), nameof(CanPrevious), nameof(CanNext) })
            OnPropertyChanged(name);
    }

    public ViewerRequest? BeginOpen(int index)
    {
        if (index < 0 || index >= Playlist.Count) return null;
        _openCts?.Cancel();
        _openCts?.Dispose();
        _openCts = new();
        CurrentIndex = index;
        Message = "正在加载…";
        return new(Playlist[index], _openCts.Token);
    }

    public void Close()
    {
        _openCts?.Cancel();
        _openCts?.Dispose();
        _openCts = null;
        CurrentIndex = -1;
        Message = "";
    }

    public void Dispose() => Close();
}

public sealed record ViewerRequest(MediaItem Item, CancellationToken Token);
