using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace YoableWPF.Managers
{
    /// <summary>
    /// In-memory LRU cache of decoded (and frozen) bitmaps, with background prefetching
    /// of neighboring images so switching images feels instant on large datasets.
    /// All public members are thread-safe; cached bitmaps are frozen so they can be
    /// decoded on background threads and rendered on the UI thread.
    /// </summary>
    public class ImageCacheManager
    {
        // Memory budget for cached bitmaps (estimated as width * height * 4 bytes).
        public long MaxCacheBytes { get; set; } = 512L * 1024 * 1024;

        // Hard cap on entry count regardless of size, protects against tiny-image datasets.
        public int MaxCacheEntries { get; set; } = 512;

        private class CacheEntry
        {
            public string Path;
            public BitmapImage Bitmap;
            public long SizeBytes;
        }

        private readonly object cacheLock = new();
        private readonly Dictionary<string, LinkedListNode<CacheEntry>> cacheMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<CacheEntry> lruList = new(); // Most recently used at the front
        private long currentCacheBytes = 0;

        private CancellationTokenSource prefetchCts;

        /// <summary>
        /// Returns the cached bitmap for the given path, or null on a cache miss.
        /// </summary>
        public BitmapImage TryGet(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return null;

            lock (cacheLock)
            {
                if (cacheMap.TryGetValue(imagePath, out var node))
                {
                    lruList.Remove(node);
                    lruList.AddFirst(node);
                    return node.Value.Bitmap;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the cached bitmap, decoding and caching it on a miss.
        /// Safe to call from any thread. Returns null if the file cannot be decoded.
        /// </summary>
        public BitmapImage GetOrLoad(string imagePath)
        {
            var cached = TryGet(imagePath);
            if (cached != null) return cached;

            var bitmap = DecodeFrozen(imagePath);
            if (bitmap != null)
            {
                Add(imagePath, bitmap);
            }
            return bitmap;
        }

        /// <summary>
        /// Kicks off background decoding of the given paths (typically neighbors of the
        /// currently displayed image). Cancels any prefetch batch still in flight.
        /// </summary>
        public void Prefetch(IEnumerable<string> imagePaths)
        {
            var paths = imagePaths?.Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (paths == null || paths.Count == 0) return;

            CancellationTokenSource cts;
            lock (cacheLock)
            {
                prefetchCts?.Cancel();
                prefetchCts = new CancellationTokenSource();
                cts = prefetchCts;
            }
            var token = cts.Token;

            Task.Run(() =>
            {
                var options = new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4)
                };

                try
                {
                    Parallel.ForEach(paths, options, path =>
                    {
                        if (token.IsCancellationRequested) return;
                        if (TryGet(path) != null) return; // Already cached

                        var bitmap = DecodeFrozen(path);
                        if (bitmap != null && !token.IsCancellationRequested)
                        {
                            Add(path, bitmap);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // A newer prefetch batch superseded this one
                }
            }, token);
        }

        public void Clear()
        {
            lock (cacheLock)
            {
                prefetchCts?.Cancel();
                prefetchCts = null;
                cacheMap.Clear();
                lruList.Clear();
                currentCacheBytes = 0;
            }
        }

        private void Add(string imagePath, BitmapImage bitmap)
        {
            long sizeBytes = (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;

            lock (cacheLock)
            {
                if (cacheMap.ContainsKey(imagePath)) return;

                // Evict least recently used entries until the new bitmap fits
                while (lruList.Count > 0 &&
                       (currentCacheBytes + sizeBytes > MaxCacheBytes || lruList.Count >= MaxCacheEntries))
                {
                    var lru = lruList.Last;
                    lruList.RemoveLast();
                    cacheMap.Remove(lru.Value.Path);
                    currentCacheBytes -= lru.Value.SizeBytes;
                }

                var entry = new CacheEntry { Path = imagePath, Bitmap = bitmap, SizeBytes = sizeBytes };
                var node = lruList.AddFirst(entry);
                cacheMap[imagePath] = node;
                currentCacheBytes += sizeBytes;
            }
        }

        private static BitmapImage DecodeFrozen(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath)) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze(); // Allows cross-thread use and avoids WPF cloning costs
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
