namespace YoableWPF.Models
{
    public sealed record DuplicateImageCandidate(
        string FileName,
        string FullPath,
        long FileSize);

    public sealed class DuplicateImageGroup
    {
        public DuplicateImageGroup(string contentHash, IEnumerable<DuplicateImageCandidate> candidates)
        {
            ContentHash = contentHash;
            Candidates = candidates.ToList();
        }

        public string ContentHash { get; }
        public List<DuplicateImageCandidate> Candidates { get; }
    }
}
