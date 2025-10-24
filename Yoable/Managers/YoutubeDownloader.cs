using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Yoable.Services;
using YoutubeExplode;

namespace Yoable.Managers;

/// <summary>
/// Cross-platform YouTube video downloader and frame extractor
/// </summary>
public class YoutubeDownloader
{
    private readonly IDialogService _dialogService;
    private readonly YoutubeClient _youtube = new YoutubeClient();
    private const string OutputDirectory = "videodownloads";

    public YoutubeDownloader(IDialogService dialogService)
    {
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        Directory.CreateDirectory(OutputDirectory);
    }

    /// <summary>
    /// Downloads a YouTube video and extracts frames
    /// </summary>
    /// <param name="videoUrl">YouTube video URL</param>
    /// <param name="desiredFps">Desired frames per second</param>
    /// <param name="frameSize">Size to resize frames to (square)</param>
    /// <param name="progress">Progress reporter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Path to extracted frames directory, or null if failed</returns>
    public async Task<string?> DownloadAndProcessVideoAsync(
        string videoUrl,
        int desiredFps = 5,
        int frameSize = 640,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string videoPath = "";
        string videoDirectory = "";

        try
        {
            progress?.Report((0, 100, "Initializing..."));

            var video = await _youtube.Videos.GetAsync(videoUrl, cancellationToken);
            var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoUrl, cancellationToken);

            var streamInfo = streamManifest.GetVideoStreams()
                .Where(s => s.Container == YoutubeExplode.Videos.Streams.Container.Mp4)
                .OrderByDescending(s => s.VideoQuality)
                .FirstOrDefault();

            if (streamInfo == null)
            {
                await _dialogService.ShowErrorAsync("Download Error", $"No video streams found for {video.Title}");
                return null;
            }

            string safeVideoTitle = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));
            videoDirectory = Path.Combine(OutputDirectory, safeVideoTitle);
            Directory.CreateDirectory(videoDirectory);
            Directory.CreateDirectory(Path.Combine(videoDirectory, "frames"));

            videoPath = Path.Combine(videoDirectory, $"{video.Id}.mp4");
            long totalBytes = streamInfo.Size.Bytes;

            var downloadProgress = new Progress<double>(p =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                int downloadPercent = (int)(p * 70); // Download is 0-70% of overall progress
                long downloadedBytes = (long)(totalBytes * p);
                progress?.Report((downloadPercent, 100,
                    $"Downloading {video.Title}... {p * 100:F2}% ({FormatFileSize(downloadedBytes)} / {FormatFileSize(totalBytes)})"));
            });

            await _youtube.Videos.Streams.DownloadAsync(streamInfo, videoPath, downloadProgress, cancellationToken);

            progress?.Report((70, 100, "Preparing to extract frames..."));

            var extractionProgress = new Progress<double>(p =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                int extractPercent = 70 + (int)(p * 0.3); // Extraction is 70-100% of overall progress
                progress?.Report((extractPercent, 100, $"Extracting frames... {p:F2}%"));
            });

            await ExtractFramesAsync(videoPath, videoDirectory, desiredFps, frameSize, extractionProgress, cancellationToken);

            progress?.Report((100, 100, "Complete"));

            string framesDirectory = Path.Combine(videoDirectory, "frames");
            return Path.GetFullPath(framesDirectory);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Processing Error", $"Processing failed: {ex.Message}");
            return null;
        }
        finally
        {
            // Clean up the video file (keep frames only)
            if (File.Exists(videoPath))
            {
                try { File.Delete(videoPath); }
                catch { }
            }
        }
    }

    private async Task ExtractFramesAsync(
        string videoPath,
        string videoDirectory,
        double desiredFps,
        int frameSize,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            using (var capture = new VideoCapture(videoPath))
            {
                if (!capture.IsOpened())
                    throw new Exception("Could not open video file");

                int frameCount = (int)capture.Get(VideoCaptureProperties.FrameCount);
                double fps = capture.Get(VideoCaptureProperties.Fps);
                double videoDurationMs = (frameCount / fps) * 1000;

                // Ensure we don't exceed the source fps
                desiredFps = Math.Min(desiredFps, fps);

                // Pre-calculate all frame positions we'll need
                var framePositions = new List<int>();
                double sourceIntervalMs = 1000.0 / fps;
                double targetIntervalMs = 1000.0 / desiredFps;
                double currentTimeMs = 0;
                int lastPosition = -2;

                while (currentTimeMs < videoDurationMs)
                {
                    int framePosition = (int)(currentTimeMs / sourceIntervalMs);
                    if (framePosition - lastPosition >= 2 && framePosition < frameCount)
                    {
                        framePositions.Add(framePosition);
                        lastPosition = framePosition;
                    }
                    currentTimeMs += targetIntervalMs;
                }

                int totalFramesToProcess = framePositions.Count;
                int processedFrames = 0;
                var tasks = new List<Task>();
                string framesDirectory = Path.Combine(videoDirectory, "frames");

                // Reuse Mats outside the loop
                using (var frame = new Mat())
                using (var resized = new Mat())
                {
                    foreach (int framePosition in framePositions)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        // Read frame directly without setting position if possible
                        bool needsSeek = false;
                        if (framePosition != capture.Get(VideoCaptureProperties.PosFrames))
                        {
                            needsSeek = true;
                            capture.Set(VideoCaptureProperties.PosFrames, framePosition);
                        }

                        if (!capture.Read(frame))
                            break;

                        // Skip frames if we had to seek (helps avoid duplicate frames)
                        if (needsSeek)
                        {
                            capture.Read(frame); // Skip one frame to avoid potential duplicates
                        }

                        // Optimize resize operation
                        double scale = Math.Max((double)frameSize / frame.Width, (double)frameSize / frame.Height);
                        var newSize = new OpenCvSharp.Size(
                            (int)(frame.Width * scale),
                            (int)(frame.Height * scale)
                        );

                        Cv2.Resize(frame, resized, newSize, 0, 0, InterpolationFlags.Linear);

                        // Compute crop coordinates
                        int x = (newSize.Width - frameSize) / 2;
                        int y = (newSize.Height - frameSize) / 2;
                        var roi = new OpenCvSharp.Rect(x, y, frameSize, frameSize);

                        // Create a new Mat for the cropped image
                        using (Mat cropped = new Mat(resized, roi))
                        {
                            // Generate UUID for frame name
                            string frameUuid = Guid.NewGuid().ToString();
                            string framePath = Path.Combine(framesDirectory, $"frame_{frameUuid}.jpg");

                            // Clone and compress in parallel
                            Mat croppedClone = cropped.Clone();
                            tasks.Add(Task.Run(() =>
                            {
                                try
                                {
                                    var params_ = new int[] { (int)ImwriteFlags.JpegQuality, 98 };
                                    Cv2.ImWrite(framePath, croppedClone, params_);
                                }
                                finally
                                {
                                    croppedClone.Dispose();
                                }
                            }, cancellationToken));
                        }

                        processedFrames++;
                        double currentProgress = (processedFrames * 100.0) / totalFramesToProcess;
                        progress?.Report(currentProgress);

                        // Process tasks in batches to balance memory and speed
                        if (tasks.Count >= 16)
                        {
                            Task.WhenAll(tasks.Take(8)).Wait(cancellationToken);
                            tasks.RemoveRange(0, 8);
                        }
                    }

                    // Wait for remaining tasks
                    while (tasks.Any())
                    {
                        int batchSize = Math.Min(8, tasks.Count);
                        Task.WhenAll(tasks.Take(batchSize)).Wait(cancellationToken);
                        tasks.RemoveRange(0, batchSize);
                    }
                }
            }
        }, cancellationToken);
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
}
