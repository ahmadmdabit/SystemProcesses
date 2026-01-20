# ImageLoaderService Implementation Guide

## Overview

`ImageLoaderService` provides asynchronous, thread-safe image loading and caching for WPF applications. It performs IO off the UI thread, decodes images on the UI thread, uses pooled buffers to minimize allocations, and returns frozen `BitmapSource` instances safe for cross-thread consumption.

## Key Features

- **Async-first design**: File and HTTP loading occur off the UI thread
- **In-flight request deduplication**: Single load per canonical key prevents redundant operations
- **Optional decode pixel sizing**: Thumbnail support for memory optimization
- **Memory pooling**: `ArrayPool<byte>` for IO buffers reduces large-object allocations
- **Bounded cache**: Configurable max entries with eviction hook
- **Thread-safe**: `ConcurrentDictionary` for cache and inflight tracking
- **Resource cleanup**: Finalizer ensures `HttpClient` disposal

## Architecture

### Resource Canonicalization

The service normalizes input paths/URIs into canonical forms to ensure consistent cache keys:

- **File paths**: Converted to absolute paths via `Path.GetFullPath()`
- **HTTP/HTTPS URIs**: Normalized to absolute URIs
- **Pack URIs**: Normalized to absolute pack URIs
- **Relative paths**: Treated as file paths relative to app base

### Resource Kinds

Three resource types are supported:

1. **File**: Local file system paths (absolute or relative)
2. **Http**: HTTP/HTTPS remote images
3. **PackOrUri**: WPF pack URIs and other absolute URIs

### Load Flow

1. **Cache check**: Return immediately if cached
2. **Inflight deduplication**: Reuse existing load task if in-flight
3. **IO phase**: Read file/download off UI thread using pooled buffers
4. **Decode phase**: Create `BitmapImage` on UI thread via `Dispatcher.InvokeAsync()`
5. **Freeze**: Freeze `BitmapSource` for cross-thread safety
6. **Cache insertion**: Add to cache if under max entries
7. **Inflight cleanup**: Remove from inflight tracking

### Buffer Management

- **File loading**: Uses `ArrayPool<byte>` with file size validation
- **HTTP loading**: Streams into rented buffer with dynamic growth (up to `maxBytes`)
- **Size limits**: Enforces `maxBytes` (default 50MB) to prevent excessive allocations
- **Buffer return**: Buffers returned to pool after UI thread finishes decoding

## Public API

### Constructor

```csharp
public ImageLoaderService(
    Dispatcher? uiDispatcher = null,
    long maxBytes = AppConstants.DefaultMaxImageBytes,
    int maxCacheEntries = AppConstants.DefaultMaxCacheEntries)
```

- **uiDispatcher**: WPF UI dispatcher (defaults to `Application.Current.Dispatcher`)
- **maxBytes**: Maximum image size in bytes (default: 50MB)
- **maxCacheEntries**: Soft limit for cache entries (default: 1024)

### Core Methods

#### LoadAsync

```csharp
Task<BitmapSource> LoadAsync(
    string pathOrUri,
    int? decodePixelWidth = null,
    int? decodePixelHeight = null,
    CancellationToken cancellationToken = default)
```

Asynchronously loads an image and returns a frozen `BitmapSource`.

- **pathOrUri**: File path, absolute/relative URI, or pack URI
- **decodePixelWidth/Height**: Optional thumbnail sizing
- **cancellationToken**: Cancels the IO phase only
- **Returns**: Frozen `BitmapSource` safe for cross-thread use

**Behavior**:
- Returns cached result immediately if available
- Deduplicates concurrent requests for same resource
- Performs IO off UI thread, decoding on UI thread
- Freezes result for cross-thread safety

#### TryGetFromCache

```csharp
bool TryGetFromCache(string key, out BitmapSource bitmap)
```

Attempts to retrieve a cached image without IO.

- **key**: Same path/URI used in `LoadAsync()`
- **bitmap**: Cached result if found
- **Returns**: True if cache hit; false otherwise

#### RemoveFromCache

```csharp
bool RemoveFromCache(string key)
```

Removes a specific cached image.

- **key**: Same path/URI used in `LoadAsync()`
- **Returns**: True if removed; false if not found

#### ClearCache

```csharp
void ClearCache()
```

Clears all cached images. Use when switching themes or unloading large views.

#### Dispose

```csharp
void Dispose()
```

Releases resources including `HttpClient` and cached references. Prevents further load operations.

## Configuration Constants

The service uses constants from `AppConstants`:

- **DefaultMaxImageBytes**: 50MB (50 * 1024 * 1024)
- **DefaultMaxCacheEntries**: 1024
- **FileStreamBufferSize**: 80KB (81920)
- **HttpStreamBufferSize**: 80KB (81920)
- **IconDecodePixelWidth**: 32 pixels
- **IconDecodePixelHeight**: 32 pixels

## Thread Safety

- **Cache**: `ConcurrentDictionary<string, BitmapSource>` for thread-safe access
- **Inflight tracking**: `ConcurrentDictionary<string, Task<BitmapSource>>` for deduplication
- **Dispatcher marshalling**: All `BitmapImage` construction on UI thread via `Dispatcher.InvokeAsync()`
- **Frozen objects**: All returned `BitmapSource` instances frozen for cross-thread safety

## Performance Characteristics

- **Cache hit**: O(1) synchronous return
- **File load**: O(n) where n = file size (async IO)
- **HTTP load**: O(n) where n = response size (async IO + streaming)
- **Decode**: O(n) on UI thread where n = image dimensions
- **Memory**: Pooled buffers reduce GC pressure; frozen objects prevent cross-thread copies

## Error Handling

The service throws exceptions for:

- **ArgumentNullException**: Missing UI dispatcher or null path
- **FileNotFoundException**: File not found
- **InvalidOperationException**: File/image exceeds `maxBytes`
- **HttpRequestException**: HTTP request fails
- **ObjectDisposedException**: Operations after disposal

All exceptions are logged via Serilog with context information.

## Usage Examples

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

# ✅ 14. MVVM Toolkit (`[ObservableProperty]`) + ImageLoaderService (async)

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

# ✅ 15. WPF `ImageBrush` usage (bind a `BitmapSource` to brushes)

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

