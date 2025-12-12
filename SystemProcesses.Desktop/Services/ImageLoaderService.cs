using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SystemProcesses.Desktop.Services;

public interface IImageLoaderService
{
    void ClearCache();
    void Dispose();
    Task<BitmapSource> LoadAsync(string pathOrUri, int? decodePixelWidth = null, int? decodePixelHeight = null, CancellationToken cancellationToken = default);
    bool RemoveFromCache(string key);
    bool TryGetFromCache(string key, out BitmapSource bitmap);
}

/// <summary>
/// Provides asynchronous, thread-safe image loading and caching for WPF applications.
/// Performs IO off the UI thread, decodes images with <see cref="BitmapImage"/> on the UI thread,
/// uses pooled buffers to minimize large-object allocations, and returns frozen <see cref="BitmapSource"/>
/// instances safe for cross-thread consumption.
/// </summary>
/// <remarks>
/// Features:
/// - Async-first file and HTTP loading
/// - In-flight request deduplication (single load per canonical key)
/// - Optional decode pixel sizing for thumbnails
/// - Memory pooling (ArrayPool) for IO buffers
/// - Simple bounded-cache hook and public cache management APIs
/// - Designed to be pluggable with custom eviction policies
/// </remarks>
public sealed class ImageLoaderService : IDisposable, IImageLoaderService
{
    /// <summary>
    /// Primary in-memory cache mapping canonical resource keys to frozen <see cref="BitmapSource"/> instances.
    /// </summary>
    private readonly ConcurrentDictionary<string, BitmapSource> cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Tracks in-flight load tasks for each canonical key to deduplicate concurrent requests.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task<BitmapSource>> inflight = new(StringComparer.Ordinal);

    /// <summary>
    /// The WPF UI <see cref="Dispatcher"/> on which <see cref="BitmapImage"/> instances are constructed (BeginInit/EndInit).
    /// </summary>
    private readonly Dispatcher uiDispatcher;

    /// <summary>
    /// Shared <see cref="HttpClient"/> used to download remote images. Disposed when the service is disposed.
    /// </summary>
    private readonly HttpClient httpClient = new();

    /// <summary>
    /// Maximum allowed bytes for a single image payload read into memory to protect against large downloads or files.
    /// </summary>
    private readonly long maxBytes;

    /// <summary>
    /// Soft limit for the number of entries allowed in the cache. Implementations should use this hook when applying eviction policies.
    /// </summary>
    private readonly int maxCacheEntries; // simple eviction by oldest insertion not implemented here; we keep a simple boundary

    /// <summary>
    /// Flag indicating this instance has been disposed. Used to prevent operations after disposal.
    /// </summary>
    private bool disposed;

    /// <summary>
    /// Semaphore used to coordinate Dispose operations and prevent races during shutdown.
    /// </summary>
    private readonly SemaphoreSlim disposeLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="ImageLoaderService"/>.
    /// </summary>
    /// <param name="uiDispatcher">
    /// The UI <see cref="Dispatcher"/> used to construct WPF image objects. If null,
    /// the constructor attempts to use <see cref="Application.Current.Dispatcher"/>.
    /// </param>
    /// <param name="maxBytes">
    /// Maximum allowed size (in bytes) for a single image payload read into memory.
    /// This guards against attempting to buffer extremely large images.
    /// </param>
    /// <param name="maxCacheEntries">
    /// Maximum number of entries to retain in the in-memory cache before applying
    /// an external eviction policy. This implementation uses the value as a soft limit;
    /// integrate an eviction policy for precise size control.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when the UI dispatcher cannot be determined or supplied.
    /// </exception>
    public ImageLoaderService(Dispatcher? uiDispatcher = null, long maxBytes = 50 * 1024 * 1024, int maxCacheEntries = 1024)
    {
        this.uiDispatcher = uiDispatcher ?? Application.Current?.Dispatcher
            ?? throw new ArgumentNullException(nameof(uiDispatcher), "UI Dispatcher is required.");
        this.maxBytes = maxBytes;
        this.maxCacheEntries = Math.Max(1, maxCacheEntries);
    }

    /// <summary>
    /// Asynchronously loads an image identified by <paramref name="pathOrUri"/> and returns a frozen <see cref="BitmapSource"/>.
    /// </summary>
    /// <param name="pathOrUri">A file path, absolute/relative URI, or pack URI that identifies the image resource.</param>
    /// <param name="decodePixelWidth">Optional decode pixel width to request a thumbnail-sized decode.</param>
    /// <param name="decodePixelHeight">Optional decode pixel height to request a thumbnail-sized decode.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the IO phase of this request.</param>
    /// <returns>
    /// A task that completes with a frozen <see cref="BitmapSource"/> suitable for direct binding to WPF UI elements.
    /// If the image is present in the cache the task completes synchronously with the cached instance.
    /// </returns>
    /// <remarks>
    /// - The method deduplicates concurrent requests for the same canonical resource+decode key:
    ///   only a single underlying load/IO operation will be performed; other callers await the same task.
    /// - Cancellation only affects the initiating IO operation; when multiple callers share an in-flight task,
    ///   only the token provided to the task creator controls that IO operation. Callers should expect this behavior.
    /// - The returned <see cref="BitmapSource"/> is frozen to allow cross-thread usage.
    /// </remarks>
    public Task<BitmapSource> LoadAsync(string pathOrUri, int? decodePixelWidth = null, int? decodePixelHeight = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrUri)) throw new ArgumentNullException(nameof(pathOrUri));
        ThrowIfDisposed();

        // Canonicalize key based on resource type + decode settings
        var (kind, canonical) = Canonicalize(pathOrUri);
        var cacheKey = CanonicalKeyWithDecode(canonical, decodePixelWidth, decodePixelHeight);

        // quick cache hit
        if (cache.TryGetValue(cacheKey, out var cached) && cached != null)
            return Task.FromResult(cached);

        // dedupe using in-flight tasks
        var task = inflight.GetOrAdd(cacheKey, _ => LoadInternalAsync(kind, canonical, decodePixelWidth, decodePixelHeight, cancellationToken));

        // When the task completes, remove from inflight and insert into cache on success.
        // We attach a continuation to manage lifecycle and eviction heuristics.
        _ = task.ContinueWith(t =>
        {
            inflight.TryRemove(cacheKey, out _);
            if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
            {
                // Simple size-limited cache: if we exceed max entries, skip caching or evict randomly (this is a hook).
                if (cache.Count < maxCacheEntries)
                    cache[cacheKey] = t.Result;
                // else: consider adding eviction policy (LRU) or using MemoryCache
            }
            return 0;
        }, TaskScheduler.Default);

        return task;
    }

    /// <summary>
    /// Internal core loader that dispatches the load flow based on resource kind (file, HTTP, pack/URI).
    /// </summary>
    /// <param name="kind">The canonical <see cref="ResourceKind"/> describing how to treat the input.</param>
    /// <param name="canonical">A canonicalized identifier for the resource (absolute file path or absolute URI).</param>
    /// <param name="decodePixelWidth">Optional decode width for thumbnails.</param>
    /// <param name="decodePixelHeight">Optional decode height for thumbnails.</param>
    /// <param name="cancellationToken">Token used to cancel the IO portion of the load.</param>
    /// <returns>A <see cref="Task{BitmapSource}"/> that completes when the image is available and frozen.</returns>
    /// <remarks>
    /// This method ensures that heavy IO and network reads occur off the UI thread and that the final
    /// <see cref="BitmapImage"/> construction (BeginInit/EndInit) runs on the UI dispatcher as required by WPF.
    /// </remarks>
    private async Task<BitmapSource> LoadInternalAsync(ResourceKind kind, string canonical, int? decodePixelWidth, int? decodePixelHeight, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (kind)
        {
            case ResourceKind.File:
                return await LoadFileAsync(canonical, decodePixelWidth, decodePixelHeight, cancellationToken).ConfigureAwait(false);

            case ResourceKind.Http:
                return await LoadHttpAsync(new Uri(canonical, UriKind.Absolute), decodePixelWidth, decodePixelHeight, cancellationToken).ConfigureAwait(false);

            case ResourceKind.PackOrUri:
                // create on UI thread directly from URI
                var uri = new Uri(canonical, UriKind.Absolute);

                // If we're already on the UI thread, create the BitmapImage directly (no dispatch)
                if (uiDispatcher.CheckAccess())
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = uri;
                    if (decodePixelWidth.HasValue) bmp.DecodePixelWidth = decodePixelWidth.Value;
                    if (decodePixelHeight.HasValue) bmp.DecodePixelHeight = decodePixelHeight.Value;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.None;
                    bmp.EndInit();
                    if (bmp.CanFreeze) bmp.Freeze();
                    return bmp;
                }

                // Otherwise schedule on UI thread; await its Task (no blocking)
                return await uiDispatcher.InvokeAsync(() =>
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = uri;
                    if (decodePixelWidth.HasValue) bmp.DecodePixelWidth = decodePixelWidth.Value;
                    if (decodePixelHeight.HasValue) bmp.DecodePixelHeight = decodePixelHeight.Value;
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.None;
                    bmp.EndInit();
                    if (bmp.CanFreeze) bmp.Freeze();
                    return (BitmapSource)bmp;
                }, DispatcherPriority.Normal).Task.ConfigureAwait(false);

            default:
                throw new NotSupportedException($"Unsupported resource kind: {kind}");
        }
    }

    #region File + HTTP loaders (minimized allocations, pooled buffer)

    /// <summary>
    /// Reads a local file asynchronously into a pooled buffer, then decodes it into a frozen <see cref="BitmapSource"/> on the UI thread.
    /// </summary>
    /// <param name="fullPath">Canonical full path to the local file.</param>
    /// <param name="decodePixelWidth">Optional decode width for thumbnail decoding.</param>
    /// <param name="decodePixelHeight">Optional decode height for thumbnail decoding.</param>
    /// <param name="cancellationToken">Cancellation token to abort the file read.</param>
    /// <returns>A frozen <see cref="BitmapSource"/> representing the decoded image.</returns>
    /// <remarks>
    /// - Uses <see cref="ArrayPool{Byte}"/> to rent a buffer for file IO to reduce large-object allocations.
    /// - Validates file existence and size before allocation; throws if file is absent or exceeds configured maximum size.
    /// - Returns a <see cref="BitmapSource"/> that has been frozen to allow safe caching and cross-thread use.
    /// </remarks>
    private async Task<BitmapSource> LoadFileAsync(string fullPath, int? decodePixelWidth, int? decodePixelHeight, CancellationToken cancellationToken)
    {
        // Validate & size check early (no allocations)
        var fi = new FileInfo(fullPath);
        if (!fi.Exists) throw new FileNotFoundException("Image file not found", fullPath);
        if (fi.Length > maxBytes) throw new InvalidOperationException($"File too large ({fi.Length} bytes)");

        int length = (int)fi.Length;
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(length);
        try
        {
            // Async read into rented buffer
            using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                int read = 0;
                while (read < length)
                {
                    int r = await fs.ReadAsync(buffer, read, length - read, cancellationToken).ConfigureAwait(false);
                    if (r == 0) break;
                    read += r;
                }
                if (read != length) length = read;
            }

            // Create image on UI thread from MemoryStream that references rented buffer (no copy)
            return await uiDispatcher.InvokeAsync(() =>
            {
                using var ms = new MemoryStream(buffer, 0, length, writable: false);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                if (decodePixelWidth.HasValue) bmp.DecodePixelWidth = decodePixelWidth.Value;
                if (decodePixelHeight.HasValue) bmp.DecodePixelHeight = decodePixelHeight.Value;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                if (bmp.CanFreeze) bmp.Freeze();
                return (BitmapSource)bmp;
            }, DispatcherPriority.Normal).Task.ConfigureAwait(false);
        }
        finally
        {
            // return pool after UI thread finished EndInit above
            pool.Return(buffer, clearArray: false);
        }
    }

    /// <summary>
    /// Downloads an image via HTTP/HTTPS into a pooled buffer and decodes it into a frozen <see cref="BitmapSource"/> on the UI thread.
    /// </summary>
    /// <param name="uri">Absolute HTTP or HTTPS URI to the remote image.</param>
    /// <param name="decodePixelWidth">Optional decode width for thumbnail decoding.</param>
    /// <param name="decodePixelHeight">Optional decode height for thumbnail decoding.</param>
    /// <param name="cancellationToken">Cancellation token to abort the network read.</param>
    /// <returns>A frozen <see cref="BitmapSource"/> representing the decoded image.</returns>
    /// <remarks>
    /// - The implementation uses <see cref="HttpClient"/> with response headers read mode and streams the body into a rented buffer.
    /// - Enforces <see cref="_maxBytes"/> to mitigate downloading excessive data.
    /// - For very large remote images consider streaming to disk to moderate peak memory pressure.
    /// </remarks>
    private async Task<BitmapSource> LoadHttpAsync(Uri uri, int? decodePixelWidth, int? decodePixelHeight, CancellationToken cancellationToken)
    {
        using var resp = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var contentLength = resp.Content.Headers.ContentLength ?? -1;
        if (contentLength > maxBytes) throw new InvalidOperationException($"Remote image too large ({contentLength} bytes)");

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        // Read into rented buffer growing as needed
        var pool = ArrayPool<byte>.Shared;
        int bufferSize = contentLength > 0 && contentLength <= int.MaxValue ? (int)contentLength : 81920;
        byte[] buffer = pool.Rent(Math.Min(bufferSize, (int)Math.Min(maxBytes, int.MaxValue)));
        int total = 0;
        try
        {
            while (true)
            {
                if (total == buffer.Length)
                {
                    int newSize = Math.Min(buffer.Length * 2, (int)Math.Min(maxBytes, int.MaxValue));
                    if (newSize == buffer.Length) throw new InvalidOperationException("Remote image exceeds allowed max size");
                    var bigger = pool.Rent(newSize);
                    Buffer.BlockCopy(buffer, 0, bigger, 0, total);
                    pool.Return(buffer, clearArray: false);
                    buffer = bigger;
                }
                int read = await stream.ReadAsync(buffer, total, buffer.Length - total, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (total > maxBytes) throw new InvalidOperationException("Remote image exceeds allowed max size");
            }

            return await uiDispatcher.InvokeAsync(() =>
            {
                using var ms = new MemoryStream(buffer, 0, total, writable: false);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                if (decodePixelWidth.HasValue) bmp.DecodePixelWidth = decodePixelWidth.Value;
                if (decodePixelHeight.HasValue) bmp.DecodePixelHeight = decodePixelHeight.Value;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                if (bmp.CanFreeze) bmp.Freeze();
                return (BitmapSource)bmp;
            }, DispatcherPriority.Normal).Task.ConfigureAwait(false);
        }
        finally
        {
            pool.Return(buffer, clearArray: false);
        }
    }

    #endregion File + HTTP loaders (minimized allocations, pooled buffer)

    #region Key canonicalization helpers

    /// <summary>
    /// Appends decode pixel width/height to a canonical resource identifier to form a cache key that distinguishes differently decoded variants.
    /// </summary>
    /// <param name="canonical">Canonical base identifier for the resource.</param>
    /// <param name="width">Optional decode pixel width.</param>
    /// <param name="height">Optional decode pixel height.</param>
    /// <returns>A string key combining the canonical identifier and decode parameters.</returns>
    /// <remarks>
    /// This ensures that thumbnail requests and full-resolution requests do not collide in the cache.
    /// </remarks>
    private static string CanonicalKeyWithDecode(string canonical, int? width, int? height)
    {
        if (!width.HasValue && !height.HasValue) return canonical;
        return $"{canonical}|w={width?.ToString() ?? ""}|h={height?.ToString() ?? ""}";
    }

    /// <summary>
    /// Produces a canonical resource representation for the provided input string and identifies the resource kind.
    /// </summary>
    /// <param name="input">Input path or URI supplied by callers.</param>
    /// <returns>
    /// A tuple containing the <see cref="ResourceKind"/> and a canonical string appropriate for use as a cache/inflight key.
    /// For files this is the absolute file path; for HTTP URIs this is the absolute URI; for pack URIs this is normalized absolute URI.
    /// </returns>
    /// <remarks>
    /// Ensures consistent cache keys across different representations (relative vs absolute paths, alternate URI encodings, etc.).
    /// </remarks>
    private (ResourceKind kind, string canonical) Canonicalize(string input)
    {
        if (input.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
        {
            // normalize pack URI
            var u = new Uri(input, UriKind.Absolute);
            return (ResourceKind.PackOrUri, u.AbsoluteUri);
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var abs))
        {
            if (abs.Scheme == Uri.UriSchemeFile)
            {
                // full local path
                return (ResourceKind.File, Path.GetFullPath(abs.LocalPath));
            }

            if (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps)
            {
                return (ResourceKind.Http, abs.AbsoluteUri);
            }

            // other absolute URIs -> treat as pack/uri
            return (ResourceKind.PackOrUri, abs.AbsoluteUri);
        }

        // not absolute -> treat as file relative to app base
        var full = Path.GetFullPath(input);
        return (ResourceKind.File, full);
    }

    /// <summary>
    /// Represents the canonical category of a resource string so the loader knows how to treat it (e.g. local file, HTTP URI, or pack/other URI).
    /// </summary>
    private enum ResourceKind
    { File, Http, PackOrUri }

    #endregion Key canonicalization helpers

    #region Cache management

    /// <summary>
    /// Attempts to retrieve a cached <see cref="BitmapSource"/> for the provided key without performing any IO.
    /// </summary>
    /// <param name="key">The same path or URI string used previously to load the image.</param>
    /// <param name="bitmap">When this method returns, contains the cached bitmap if the method returned true; otherwise null.</param>
    /// <returns>True if the cache contains an entry for the canonicalized key; otherwise false.</returns>
    /// <remarks>
    /// This helper performs canonicalization consistent with <see cref="LoadAsync"/> so callers may pass file paths,
    /// absolute URIs, or pack URIs directly.
    /// </remarks>
    public bool TryGetFromCache(string key, out BitmapSource bitmap)
    {
        var (k, canonical) = Canonicalize(key);
        var cacheKey = CanonicalKeyWithDecode(canonical, null, null);
        return cache.TryGetValue(cacheKey, out bitmap!);
    }

    /// <summary>
    /// Clears all entries from the in-memory image cache.
    /// </summary>
    /// <remarks>
    /// Use this to aggressively free memory held by cached <see cref="BitmapSource"/> instances.
    /// Callers should ensure no UI is simultaneously depending on the cached objects.
    /// </remarks>
    public void ClearCache() => cache.Clear();

    /// <summary>
    /// Removes a specific cached image identified by <paramref name="key"/> from the cache.
    /// </summary>
    /// <param name="key">The path or URI previously used to load the image.</param>
    /// <returns>True if the entry was found and removed; otherwise false.</returns>
    /// <remarks>
    /// The method canonicalizes the supplied key the same way as <see cref="LoadAsync"/> before attempting removal.
    /// </remarks>
    public bool RemoveFromCache(string key)
    {
        var (k, canonical) = Canonicalize(key);
        var cacheKey = CanonicalKeyWithDecode(canonical, null, null);
        return cache.TryRemove(cacheKey, out _);
    }

    #endregion Cache management

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(ImageLoaderService));
    }

    /// <summary>
    /// Releases resources used by the service, including the internal <see cref="HttpClient"/> and cached references.
    /// </summary>
    /// <remarks>
    /// - Disposing the service prevents further load operations; calls after disposal will throw.
    /// - Inflight loads are not forcibly aborted in this implementation; production code can extend Dispose
    ///   to signal a cancellation token and await inflight tasks if graceful shutdown is required.
    /// </remarks>
    public void Dispose()
    {
        disposeLock.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            httpClient.Dispose();

            // do not forcibly cancel inflight tasks here; production code might use a cancellation
            // token source to signal shutdown and await inflight tasks gracefully.
            cache.Clear();
            inflight.Clear();
        }
        finally { disposeLock.Release(); }
    }
}