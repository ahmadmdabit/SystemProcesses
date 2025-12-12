using System;
using System.Text;

using Microsoft.Extensions.ObjectPool;

namespace SystemProcesses.Desktop.Helpers;

/// <summary>
/// Policy that creates and resets StringBuilder instances.
/// It will refuse to keep builders that grew beyond MaxBuilderCapacity
/// (so huge buffers are not retained indefinitely).
/// </summary>
public sealed class StringBuilderPooledObjectPolicy : IPooledObjectPolicy<StringBuilder>
{
    public int DefaultCapacity { get; }
    public int MaxBuilderCapacity { get; }

    public StringBuilderPooledObjectPolicy(int defaultCapacity = 256, int maxBuilderCapacity = 1 << 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(defaultCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBuilderCapacity, defaultCapacity);

        DefaultCapacity = defaultCapacity;
        MaxBuilderCapacity = maxBuilderCapacity;
    }

    public StringBuilder Create() => new(DefaultCapacity);

    /// <summary>
    /// Reset builder and decide whether to return to pool.
    /// Return false to drop it (GC collects) if capacity grew too large.
    /// </summary>
    public bool Return(StringBuilder obj)
    {
        if (obj == null)
        {
            return false;
        }

        // Clear content
        obj.Clear();

        // If the underlying buffer got too large, don't keep it in the pool;
        // letting it be GC'd avoids unbounded memory retention.
        if (obj.Capacity > MaxBuilderCapacity)
        {
            return false;
        }

        // Optionally shrink large but acceptable buffers, e.g. reduce to DefaultCapacity
        // to avoid keeping a lot of memory for sporadic bursts.
        // Uncomment if you want more aggressive shrinking:
        // if (obj.Capacity > DefaultCapacity * 4)
        //     obj.Capacity = DefaultCapacity;

        return true;
    }
}

/// <summary>
/// High-performance static pool helper.
/// Use Rent() to obtain a PooledStringBuilder; Dispose it (or use 'using') to return the builder.
/// </summary>
public static class StringBuilderPool
{
    // Tunable parameters:
    private const int DefaultCapacity = 256;       // initial capacity for new builders

    private const int MaxRetainedBuilders = 32;    // how many builders DefaultObjectPool will keep per-thread bucket
    private const int MaxBuilderCapacity = 1 << 16; // 65,536 chars max to retain

    private static readonly ObjectPool<StringBuilder> sbPool;

    static StringBuilderPool()
    {
        var policy = new StringBuilderPooledObjectPolicy(DefaultCapacity, MaxBuilderCapacity);
        // DefaultObjectPool is fast and thread-safe. MaxRetained controls contention vs memory.
        sbPool = new DefaultObjectPool<StringBuilder>(policy, MaxRetainedBuilders);
    }

    /// <summary>
    /// Rent a pooled builder wrapped in a struct that will return it on Dispose().
    /// </summary>
    public static PooledStringBuilder Rent() => new(sbPool.Get(), sbPool);

    /// <summary>
    /// Convenience: rent and pre-append a string.
    /// </summary>
    public static PooledStringBuilder Rent(string? initial)
    {
        var psb = Rent();
        if (!string.IsNullOrEmpty(initial))
        {
            psb.Builder.Append(initial);
        }

        return psb;
    }

    /// <summary>
    /// A lightweight struct wrapper that returns the builder to the pool when disposed.
    /// DO NOT use the StringBuilder after disposing the PooledStringBuilder.
    /// </summary>
    public readonly struct PooledStringBuilder : IDisposable
    {
        private readonly StringBuilder? builder;
        private readonly ObjectPool<StringBuilder>? pool;
        internal StringBuilder Builder => builder ?? throw new ObjectDisposedException(nameof(PooledStringBuilder));

        internal PooledStringBuilder(StringBuilder builder, ObjectPool<StringBuilder> pool)
        {
            this.builder = builder;
            this.pool = pool;
        }

        /// <summary>
        /// The live System.Text.StringBuilder instance.
        /// </summary>
        public StringBuilder Value => Builder;

        /// <summary>
        /// Convenience property name.
        /// </summary>
        public StringBuilder BuilderRef => Builder;

        /// <summary>
        /// Produce the resulting string and return the builder to the pool in one call.
        /// After calling Build(), the PooledStringBuilder is considered disposed.
        /// </summary>
        public string Build()
        {
            try
            {
                return Builder.ToString();
            }
            finally
            {
                Dispose();
            }
        }

        /// <summary>
        /// Common pattern: Append then call Build() or Dispose().
        /// </summary>
        public void Dispose()
        {
            // Nothing to do if already returned
            if (this.builder == null)
            {
                return;
            }

            // Get local copies (struct is readonly, but fields are readonly reference)
            var builder = this.builder;
            var pool = this.pool;

            // Return to pool (policy will Clear and check capacity). If pool is null, just let GC collect.
            pool?.Return(builder);
            // Note: we don't null out fields because struct is readonly; leaving as-is is fine.
        }
    }
}