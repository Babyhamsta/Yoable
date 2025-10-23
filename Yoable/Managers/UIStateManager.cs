using System;
using System.Collections.Generic;
using System.Linq;
using Yoable.Models;

namespace Yoable.Managers;

/// <summary>
/// Cross-platform UI state manager
/// Manages image list caching, sorting, and filtering without direct UI dependencies
/// </summary>
public class UIStateManager
{
    private List<ImageListItem> allImages = new List<ImageListItem>();
    private Dictionary<string, ImageListItem> imageListItemCache = new Dictionary<string, ImageListItem>();
    private ImageStatus? currentFilter = null;
    private SortMode currentSortMode = SortMode.ByName;

    public enum SortMode
    {
        ByName,
        ByStatus
    }

    // Cache management methods
    public Dictionary<string, ImageListItem> ImageListItemCache => imageListItemCache;

    public void AddToCache(string fileName, ImageListItem item)
    {
        imageListItemCache[fileName] = item;
    }

    public bool TryGetFromCache(string fileName, out ImageListItem? item)
    {
        return imageListItemCache.TryGetValue(fileName, out item);
    }

    public void ClearCache()
    {
        imageListItemCache.Clear();
    }

    public void BuildCache(IEnumerable<ImageListItem> items)
    {
        imageListItemCache.Clear();
        foreach (var item in items)
        {
            imageListItemCache[item.FileName] = item;
        }
    }

    /// <summary>
    /// Calculate status counts from a collection of images
    /// </summary>
    public StatusCounts CalculateStatusCounts(IEnumerable<ImageListItem> items)
    {
        var itemsList = items.ToList();
        return new StatusCounts
        {
            NeedsReview = itemsList.Count(x => x.Status == ImageStatus.VerificationNeeded),
            Unverified = itemsList.Count(x => x.Status == ImageStatus.NoLabel),
            Verified = itemsList.Count(x => x.Status == ImageStatus.Verified),
            Negative = itemsList.Count(x => x.Status == ImageStatus.Negative),
            Background = itemsList.Count(x => x.Status == ImageStatus.Background)
        };
    }

    /// <summary>
    /// Sort images by name
    /// </summary>
    public List<ImageListItem> SortImagesByName(IEnumerable<ImageListItem> items)
    {
        currentSortMode = SortMode.ByName;
        return items.OrderBy(x => x.FileName).ToList();
    }

    /// <summary>
    /// Sort images by status
    /// Custom sort order: VerificationNeeded first, then NoLabel, then others
    /// </summary>
    public List<ImageListItem> SortImagesByStatus(IEnumerable<ImageListItem> items)
    {
        currentSortMode = SortMode.ByStatus;
        return items.OrderBy(x => GetStatusSortOrder(x.Status))
                   .ThenBy(x => x.FileName)
                   .ToList();
    }

    private int GetStatusSortOrder(ImageStatus status)
    {
        return status switch
        {
            ImageStatus.VerificationNeeded => 0,
            ImageStatus.NoLabel => 1,
            ImageStatus.Negative => 2,
            ImageStatus.Background => 3,
            ImageStatus.Verified => 4,
            _ => 5
        };
    }

    /// <summary>
    /// Filter images by status
    /// </summary>
    public List<ImageListItem> FilterImagesByStatus(IEnumerable<ImageListItem> items, ImageStatus? status)
    {
        currentFilter = status;
        var itemsList = items.ToList();

        // Store all images for future filtering
        if (allImages.Count == 0)
        {
            allImages = new List<ImageListItem>(itemsList);
        }

        if (status == null)
        {
            // Show all images
            return itemsList;
        }
        else
        {
            // Filter by status
            return itemsList.Where(i => i.Status == status.Value).ToList();
        }
    }

    /// <summary>
    /// Refresh the complete list of images (call when images are added/removed)
    /// </summary>
    public void RefreshAllImagesList(IEnumerable<ImageListItem> items)
    {
        allImages = new List<ImageListItem>(items);
    }

    /// <summary>
    /// Get the current filter
    /// </summary>
    public ImageStatus? CurrentFilter => currentFilter;

    /// <summary>
    /// Get the current sort mode
    /// </summary>
    public SortMode CurrentSortMode => currentSortMode;

    /// <summary>
    /// Apply current sort mode to a list of items
    /// </summary>
    public List<ImageListItem> ApplyCurrentSort(IEnumerable<ImageListItem> items)
    {
        return currentSortMode switch
        {
            SortMode.ByName => SortImagesByName(items),
            SortMode.ByStatus => SortImagesByStatus(items),
            _ => items.ToList()
        };
    }

    /// <summary>
    /// Apply current filter to a list of items
    /// </summary>
    public List<ImageListItem> ApplyCurrentFilter(IEnumerable<ImageListItem> items)
    {
        return FilterImagesByStatus(items, currentFilter);
    }

    /// <summary>
    /// Apply both current sort and filter
    /// </summary>
    public List<ImageListItem> ApplySortAndFilter(IEnumerable<ImageListItem> items)
    {
        var filtered = ApplyCurrentFilter(items);
        return ApplyCurrentSort(filtered);
    }
}

/// <summary>
/// Status counts for display
/// </summary>
public class StatusCounts
{
    public int NeedsReview { get; set; }
    public int Unverified { get; set; }
    public int Verified { get; set; }
    public int Negative { get; set; }
    public int Background { get; set; }
}
