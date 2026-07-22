using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using YoableWPF.Models;

namespace YoableWPF.Managers
{
    public sealed class DuplicateImageDetector
    {
        private const int MaxConcurrentReads = 4;

        public async Task<List<DuplicateImageGroup>> FindDuplicatesAsync(
            IEnumerable<KeyValuePair<string, ImageManager.ImageInfo>> images,
            IProgress<(int current, int total)>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var candidates = images
                .Select(pair => CreateCandidate(pair.Key, pair.Value.Path))
                .Where(candidate => candidate != null)
                .Cast<DuplicateImageCandidate>()
                .GroupBy(candidate => candidate.FileSize)
                .Where(group => group.Count() > 1)
                .SelectMany(group => group)
                .ToArray();

            if (candidates.Length == 0)
                return new List<DuplicateImageGroup>();

            int processed = 0;
            var byHash = new ConcurrentDictionary<string, ConcurrentBag<DuplicateImageCandidate>>(
                StringComparer.Ordinal);
            var options = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxConcurrentReads
            };

            await Parallel.ForEachAsync(candidates, options, async (candidate, token) =>
            {
                try
                {
                    await using var stream = new FileStream(
                        candidate.FullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 1024 * 128,
                        useAsync: true);
                    byte[] hash = await SHA256.HashDataAsync(stream, token);
                    string hashText = Convert.ToHexString(hash);
                    byHash.GetOrAdd(hashText, _ => new ConcurrentBag<DuplicateImageCandidate>())
                        .Add(candidate);
                }
                catch (IOException)
                {
                    // A file may disappear while the project is open. Skip it and keep scanning.
                }
                catch (UnauthorizedAccessException)
                {
                    // Unreadable images are ignored by duplicate detection.
                }
                finally
                {
                    int current = Interlocked.Increment(ref processed);
                    progress?.Report((current, candidates.Length));
                }
            });

            return byHash
                .Where(pair => pair.Value.Count > 1)
                .Select(pair => new DuplicateImageGroup(
                    pair.Key,
                    pair.Value.OrderBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)))
                .OrderBy(group => group.Candidates[0].FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static DuplicateImageCandidate? CreateCandidate(string fileName, string fullPath)
        {
            try
            {
                var fileInfo = new FileInfo(fullPath);
                if (!fileInfo.Exists)
                    return null;

                return new DuplicateImageCandidate(fileName, fullPath, fileInfo.Length);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
