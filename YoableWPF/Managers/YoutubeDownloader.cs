using OpenCvSharp;
using System.IO;
using System.Windows;
using YoableWPF.Managers;
using YoableWPF;
using YoutubeExplode;
using Size = OpenCvSharp.Size;
using Rect = OpenCvSharp.Rect;

public class YoutubeDownloader
{
    private readonly YoutubeClient youtube = new YoutubeClient();
    private MainWindow mainWindow;
    private OverlayManager overlayManager;
    private CancellationTokenSource downloadCancellationToken;
    private const string OutputDirectory = "videodownloads";
    private int FrameSize = 640;

    public YoutubeDownloader(MainWindow form, OverlayManager overlay)
    {
        mainWindow = form;
        overlayManager = overlay;
        Directory.CreateDirectory(OutputDirectory);
    }

    public async Task<bool> DownloadAndProcessVideo(string videoUrl, int desiredFps = 5, int frameSize = 640)
    {
        string videoPath = "";
        string videoDirectory = "";
        double downloadProgress = 0;
        double processingProgress = 0;
        bool isDownloading = true;

        // Set the user selected framesize
        FrameSize = frameSize;

        try
        {
            downloadCancellationToken = new CancellationTokenSource();
            overlayManager.ShowOverlayWithProgress("Initializing...", downloadCancellationToken);

            var video = await youtube.Videos.GetAsync(videoUrl);
            var streamManifest = await youtube.Videos.Streams.GetManifestAsync(videoUrl);

            var streamInfo = streamManifest.GetVideoStreams()
                .Where(s => s.Container == YoutubeExplode.Videos.Streams.Container.Mp4)
                .OrderByDescending(s => s.VideoQuality)
                .FirstOrDefault();

            if (streamInfo == null)
            {
                MessageBox.Show($"No video streams found for {video.Title}",
                    "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            string safeVideoTitle = string.Join("_", video.Title.Split(Path.GetInvalidFileNameChars()));
            videoDirectory = Path.Combine(OutputDirectory, safeVideoTitle);
            Directory.CreateDirectory(videoDirectory);
            Directory.CreateDirectory(Path.Combine(videoDirectory, "frames"));

            videoPath = Path.Combine(videoDirectory, $"{video.Id}.mp4");
            long totalBytes = streamInfo.Size.Bytes;
            long downloadedBytes = 0;

            var progress = new Progress<double>(p =>
            {
                if (!isDownloading) return;
                downloadProgress = p * 100;
                downloadedBytes = (long)(totalBytes * p);

                mainWindow.Dispatcher.Invoke(() =>
                {
                    overlayManager.UpdateMessage($"Downloading {video.Title}... {downloadProgress:F2}% ({FormatFileSize(downloadedBytes)} / {FormatFileSize(totalBytes)})");
                    overlayManager.UpdateProgress((int)downloadProgress);
                });
            });

            await youtube.Videos.Streams.DownloadAsync(streamInfo, videoPath, progress);
            isDownloading = false;

            mainWindow.Dispatcher.Invoke(() => {
                overlayManager.UpdateMessage("Preparing to extract frames...");
                overlayManager.UpdateProgress(0);
            });

            await Task.Run(async () => {
                await ExtractFrames(videoPath, videoDirectory, desiredFps, p => {
                    processingProgress = p;
                    mainWindow.Dispatcher.Invoke(() => {
                        overlayManager.UpdateProgress((int)p);
                    });
                });
            });

            await mainWindow.Dispatcher.InvokeAsync(() => {
                string absolutePath = Path.GetFullPath(Path.Combine(videoDirectory, "frames"));
                mainWindow.LoadImages(absolutePath);
            });

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Processing failed: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            if (File.Exists(videoPath))
            {
                try { File.Delete(videoPath); }
                catch { }
            }
            overlayManager.HideOverlay();
        }
    }

    private async Task ExtractFrames(string videoPath, string videoDirectory, double desiredFps, Action<double> progressCallback)
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
                    if (downloadCancellationToken.Token.IsCancellationRequested)
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
                    double scale = Math.Max((double)FrameSize / frame.Width, (double)FrameSize / frame.Height);
                    var newSize = new Size(
                        (int)(frame.Width * scale),
                        (int)(frame.Height * scale)
                    );

                    Cv2.Resize(frame, resized, newSize, 0, 0, InterpolationFlags.Linear);

                    // Compute crop coordinates
                    int x = (newSize.Width - FrameSize) / 2;
                    int y = (newSize.Height - FrameSize) / 2;
                    var roi = new Rect(x, y, FrameSize, FrameSize);

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
                        }));
                    }

                    processedFrames++;
                    double progress = (processedFrames * 100.0) / totalFramesToProcess;
                    progressCallback(progress);

                    mainWindow.Dispatcher.Invoke(() =>
                    {
                        overlayManager.UpdateMessage($"Extracting frames... ({processedFrames} / {totalFramesToProcess})");
                    });

                    // Process tasks in batches to balance memory and speed
                    if (tasks.Count >= 16)
                    {
                        await Task.WhenAll(tasks.Take(8));
                        tasks.RemoveRange(0, 8);
                    }
                }

                // Wait for remaining tasks
                while (tasks.Any())
                {
                    await Task.WhenAll(tasks.Take(8));
                    tasks.RemoveRange(0, Math.Min(8, tasks.Count));
                }
            }
        }
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