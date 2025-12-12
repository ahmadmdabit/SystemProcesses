Below are **clean, realistic, idiomatic usage examples** for the ImageLoaderService in WPF.
They cover the most common use-cases in real applications:

* ViewModel async loading
* Binding to Image controls
* Using cancellation
* Thumbnail loading
* HTTP image sources
* Pack URI resources
* Prewarming cache
* Manual cache management
* Using in virtualized controls (DataGrid / ListView)
* Handling high-frequency UI updates

All examples are short, practical, and copy-paste ready.

---

# ✅ 1. Basic usage inside a ViewModel (async property)

```csharp
public class ProcessViewModel : INotifyPropertyChanged
{
    private readonly ImageLoaderService _images = new ImageLoaderService();

    private BitmapSource? _icon;
    public BitmapSource? Icon
    {
        get => _icon;
        private set { _icon = value; OnPropertyChanged(); }
    }

    public async Task LoadIconAsync(string path)
    {
        Icon = await _images.LoadAsync(path);
    }
}
```

**Usage from View:**

```xml
<Image Width="26" Height="26" Source="{Binding Icon}" />
```

---

# ✅ 2. Loading file-based icons

```csharp
Icon = await _images.LoadAsync(@"C:\Apps\MyApp\Assets\cpu.png");
```

Works with relative or absolute paths.

---

# ✅ 3. Loading embedded pack URI resources

```csharp
Icon = await _images.LoadAsync(
    "pack://application:,,,/MyAssembly;component/Resources/error.png"
);
```

---

# ✅ 4. Loading an HTTP / HTTPS image

```csharp
Icon = await _images.LoadAsync("https://example.com/images/user-avatar.png");
```

This runs:

* network IO off UI thread
* decode on UI thread
* returns a frozen BitmapSource

---

# ✅ 5. Loading thumbnails (decode optimization)

```csharp
Icon = await _images.LoadAsync(
    pathOrUri: "images/large-wallpaper.jpg",
    decodePixelWidth: 64,
    decodePixelHeight: 64
);
```

Perfect when:

* showing preview lists
* DataGrid thumbnails
* Process icons
* Navigation UIs

This reduces CPU + memory usage drastically.

---

# ✅ 6. Cancelling slow downloads or large image loads

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

try
{
    Icon = await _images.LoadAsync("https://slow.server/large.png", cancellationToken: cts.Token);
}
catch (OperationCanceledException)
{
    Icon = null;
}
```

---

# ✅ 7. Prewarming the cache (app startup optimization)

```csharp
await _images.LoadAsync("pack://application:,,,/Resources/ok.png");
await _images.LoadAsync("pack://application:,,,/Resources/error.png");
await _images.LoadAsync("pack://application:,,,/Resources/warning.png");
```

This eliminates UI lag when these icons first appear.

---

# ✅ 8. Checking the cache before loading

```csharp
if (_images.TryGetFromCache(path, out var cached))
{
    Icon = cached;
}
else
{
    Icon = await _images.LoadAsync(path);
}
```

---

# ✅ 9. Removing a stale entry from cache

```csharp
_images.RemoveFromCache(path);
```

---

# ✅ 10. Clearing the entire cache (memory reset)

```csharp
_images.ClearCache();
```

Useful when:

* switching themes
* unloading a large document
* navigating between heavy views

---

# ✅ 11. Using in a DataGrid / TreeView with virtualization (recommended pattern)

```csharp
public async Task LoadAsync(ProcessInfo process)
{
    // Virtualized controls often require cancellation-awareness
    using var cts = new CancellationTokenSource();

    IsLoading = true;
    process.Icon = await _images.LoadAsync(process.IconPath, decodePixelWidth: 20);
    IsLoading = false;
}
```

**XAML:**

```xml
<DataGrid ItemsSource="{Binding Processes}">
    <DataGrid.Columns>
        <DataGridTemplateColumn Header="">
            <DataGridTemplateColumn.CellTemplate>
                <DataTemplate>
                    <Image Width="20" Height="20" Source="{Binding Icon}" />
                </DataTemplate>
            </DataGridTemplateColumn.CellTemplate>
        </DataGridTemplateColumn>
        <!-- ... -->
    </DataGrid.Columns>
</DataGrid>
```

This avoids:

* blocking UI
* heavy memory usage
* redundant loads when scrolling

---

# ✅ 12. Using from code-behind (quick, direct usage)

```csharp
private readonly ImageLoaderService _images = new ImageLoaderService();

private async void Window_Loaded(object sender, RoutedEventArgs e)
{
    MyImage.Source = await _images.LoadAsync("Assets/app-logo.png");
}
```

---

# ✅ 13. Advanced: batch-loading icons (e.g., for menu items)

```csharp
var tasks = new[]
{
    _images.LoadAsync("Resources/new.png"),
    _images.LoadAsync("Resources/open.png"),
    _images.LoadAsync("Resources/save.png"),
};

var results = await Task.WhenAll(tasks);

NewIcon    = results[0];
OpenIcon   = results[1];
SaveIcon   = results[2];
```

---

Below are three focused, ready-to-drop examples that show (1) how to use the MVVM Toolkit `ObservableProperty` with the `ImageLoaderService`, (2) how to use a `BitmapSource` as an `ImageBrush` in XAML, and (3) how to load native Windows icons (from file or associated icon) and convert them to a frozen `BitmapSource`. Each example is short, idiomatic, and oriented for WPF desktop apps.

---

# 1) MVVM Toolkit (`[ObservableProperty]`) + ImageLoaderService (async)

```csharp
// NuGet: CommunityToolkit.Mvvm
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

public partial class FileItemViewModel : ObservableObject
{
    private readonly ImageLoaderService _imageLoader;

    public FileItemViewModel(ImageLoaderService imageLoader, string path)
    {
        _imageLoader = imageLoader;
        FilePath = path;
    }

    /// <summary>File path or URI for this item.</summary>
    public string FilePath { get; }

    // Backing image property that the view binds to.
    [ObservableProperty]
    private BitmapSource? icon;

    // Optional: loading flag
    [ObservableProperty]
    private bool isLoading;

    // Loads the icon async and sets Icon property (cancellation aware).
    public async Task LoadIconAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        try
        {
            IsLoading = true;
            // Request a small thumbnail to save memory
            var bmp = await _imageLoader.LoadAsync(FilePath, decodePixelWidth: 32, cancellationToken: ct);
            Icon = bmp; // frozen BitmapSource -> safe to set from any thread if frozen; otherwise set on UI thread
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

Usage in a parent `ViewModel` (e.g., create VM and call `LoadIconAsync` when item becomes visible or prewarm in background). UI binds to `Icon`:

```xml
<!-- DataTemplate -->
<Image Width="32" Height="32" Source="{Binding Icon}" />
```

---

# 2) WPF `ImageBrush` usage (bind a `BitmapSource` to brushes)

Two approaches: inline `ImageBrush` where `ImageSource` binds directly, or expose an `ImageBrush` property from VM.

## A — Bind `ImageSource` inside XAML `ImageBrush`:

```xml
<!-- XAML DataTemplate or control -->
<Rectangle Width="120" Height="80" RadiusX="6" RadiusY="6">
  <Rectangle.Fill>
    <!-- ImageBrush.ImageSource binds to the ViewModel BitmapSource property -->
    <ImageBrush ImageSource="{Binding Icon}" Stretch="UniformToFill"/>
  </Rectangle.Fill>
</Rectangle>
```

* `Icon` is a `BitmapSource` from your ViewModel (e.g., via `ImageLoaderService`).
* Because the `BitmapSource` should be frozen by the loader, it's safe to reuse directly.

## B — Expose `ImageBrush` from ViewModel (less common, shown for completeness)

```csharp
// in viewmodel: create brush on UI thread or freeze the underlying BitmapSource and create brush in XAML
public ImageBrush ThumbnailBrush => new ImageBrush(Icon) { Stretch = Stretch.UniformToFill };
```

Then bind:

```xml
<Rectangle Fill="{Binding ThumbnailBrush}" Width="120" Height="80"/>
```

Notes:

* Prefer binding `ImageSource` for clarity; creating `ImageBrush` objects per row can cause extra allocations unless reused.
* If you must reuse a brush, create it once on UI thread and reuse.

---

# 3) Loading icons from Win32 APIs → convert to `BitmapSource`

Below are two safe and practical ways to obtain Windows icons and convert them into a frozen WPF `BitmapSource`.

## A — Using `Icon.ExtractAssociatedIcon` (simple for file-associations)

```csharp
using System;
using System.Drawing; // System.Drawing.Common on .NET 6+ (Windows desktop)
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

private static class WinIconLoader
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Extracts the associated icon for a file path and returns a frozen BitmapSource.
    /// </summary>
    public static Task<BitmapSource> LoadAssociatedIconAsync(string filePath)
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            using Icon ico = Icon.ExtractAssociatedIcon(filePath) ?? SystemIcons.Application;
            var bs = Imaging.CreateBitmapSourceFromHIcon(
                ico.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            if (bs.CanFreeze) bs.Freeze();

            // Icon.ExtractAssociatedIcon returns an Icon that should be disposed; DestroyIcon called by disposing Icon.
            return bs;
        }, System.Windows.Threading.DispatcherPriority.Normal).Task;
    }
}
```

Notes:

* `Icon.ExtractAssociatedIcon` is synchronous (fast for small icons). We create the `BitmapSource` on the UI thread (required) then freeze it.
* Use `SystemIcons.Application` fallback if null.

## B — Using `SHGetFileInfo` (get small/large / shell icons) — P/Invoke variant

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

private static class ShellIcon
{
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_LARGEICON = 0x000000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Returns a frozen BitmapSource for the shell icon (small or large).
    /// </summary>
    public static Task<BitmapSource> LoadShellIconAsync(string path, bool smallIcon = true)
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var flags = SHGFI_ICON | (smallIcon ? SHGFI_SMALLICON : SHGFI_LARGEICON);
            var shfi = new SHFILEINFO();
            IntPtr res = SHGetFileInfo(path, 0, out shfi, (uint)Marshal.SizeOf(shfi), flags);
            if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
                throw new InvalidOperationException("Failed to obtain shell icon.");

            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(
                    shfi.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                if (bmp.CanFreeze) bmp.Freeze();
                return bmp;
            }
            finally
            {
                // release native icon handle
                DestroyIcon(shfi.hIcon);
            }
        }, System.Windows.Threading.DispatcherPriority.Normal).Task;
    }
}
```

Notes:

* `SHGetFileInfo` allows requesting small/large icons as the shell reports them.
* Must call `DestroyIcon` to free the native handle.
* All UI/WPF interop (CreateBitmapSourceFromHIcon and Freeze) is done on the UI thread.

---

## Integration tip: Use ImageLoaderService for fallback & caching

* You can return the `BitmapSource` from the Win32 loader into the same cache used by `ImageLoaderService` (or call `_imageLoader.CacheAndReturn(key, bitmap)` if you expose a cache hook) so future requests reuse the frozen `BitmapSource`.
* Typical pattern:

  1. Try `_imageLoader.TryGetFromCache(key)`.
  2. If missing, attempt Win32 icon load.
  3. Add to cache, return the value.

