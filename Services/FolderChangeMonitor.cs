namespace Fallery.Services;

public sealed class FolderChangeMonitor : IDisposable
{
    private readonly object _gate = new();
    private List<FileSystemWatcher> _watchers = [];
    private string? _folder;
    private long _version;
    private bool _disposed;
    public event Action<string>? Changed;

    public async Task WatchAsync(string folder)
    {
        long version;
        List<FileSystemWatcher> previous;
        lock (_gate)
        {
            if (_disposed || string.Equals(_folder, folder, StringComparison.OrdinalIgnoreCase)) return;
            _folder = folder;
            version = ++_version;
            previous = _watchers;
            _watchers = [];
        }
        await Task.Run(() =>
        {
            foreach (var watcher in previous) watcher.Dispose();
            List<FileSystemWatcher> next = [];
            try
            {
                void Notify()
                {
                    lock (_gate) { if (_disposed || version != _version) return; }
                    Changed?.Invoke(folder);
                }
                void Add(string path, bool recursive, bool mediaOnly)
                {
                    var watcher = new FileSystemWatcher(path)
                    {
                        IncludeSubdirectories = recursive,
                        NotifyFilter = mediaOnly ? NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size : NotifyFilters.DirectoryName
                    };
                    next.Add(watcher);
                    void OnChanged(object sender, FileSystemEventArgs args)
                    {
                        if (!mediaOnly || FolderScanner.GetMediaKind(args.FullPath) is not null) Notify();
                    }
                    watcher.Created += OnChanged;
                    watcher.Deleted += OnChanged;
                    watcher.Changed += OnChanged;
                    watcher.Renamed += (_, args) =>
                    {
                        if (!mediaOnly || FolderScanner.GetMediaKind(args.FullPath) is not null
                            || FolderScanner.GetMediaKind(args.OldFullPath) is not null) Notify();
                    };
                    // 缓冲区溢出后完整重扫，不能只依赖可能遗漏的单个删除通知。
                    watcher.Error += (_, _) => Notify();
                    watcher.EnableRaisingEvents = true;
                }
                // 分开监听目录与媒体，避免设置 JSON、日志等写入触发刷新循环。
                Add(folder, recursive: true, mediaOnly: false);
                Add(folder, recursive: true, mediaOnly: true);
                if (Path.GetDirectoryName(folder) is { } parent) Add(parent, recursive: false, mediaOnly: false);
                lock (_gate)
                {
                    if (!_disposed && version == _version) { _watchers = next; next = []; }
                }
            }
            catch
            {
                lock (_gate) { if (version == _version) _folder = null; }
                throw;
            }
            finally { foreach (var watcher in next) watcher.Dispose(); }
        });
    }

    public void Dispose()
    {
        List<FileSystemWatcher> watchers;
        lock (_gate)
        {
            _disposed = true;
            _version++;
            watchers = _watchers;
            _watchers = [];
        }
        foreach (var watcher in watchers) watcher.Dispose();
    }
}
