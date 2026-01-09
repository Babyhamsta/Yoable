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

            await youtube.Videos.Streams.DownloadAsync(streamInfo, videoPath, progress, downloadCancellationToken.Token);
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
        catch (OperationCanceledException)
        {
            // Clean up the video directory if cancelled
            if (!string.IsNullOrEmpty(videoDirectory) && Directory.Exists(videoDirectory))
            {
                try { Directory.Delete(videoDirectory, true); }
                catch { }
            }
            return false;
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

            desiredFps = Math.Min(desiredFps, fps);

            // Calculate which frames we need
            int frameInterval = (int)Math.Round(fps / desiredFps);
            var framePositions = new HashSet<int>();
            for (int i = 0; i < frameCount; i += frameInterval)
            {
                framePositions.Add(i);
            }

            int totalFramesToProcess = framePositions.Count;
            int processedFrames = 0;
            int currentFrameIndex = 0;
            string framesDirectory = Path.Combine(videoDirectory, "frames");

            // Pre-calculate scale and crop values (they're the same for every frame)
            Mat firstFrame = new Mat();
            capture.Read(firstFrame);
            double scale = Math.Max((double)FrameSize / firstFrame.Width, (double)FrameSize / firstFrame.Height);
            var newSize = new Size(
                (int)(firstFrame.Width * scale),
                (int)(firstFrame.Height * scale)
            );
            int cropX = (newSize.Width - FrameSize) / 2;
            int cropY = (newSize.Height - FrameSize) / 2;
            var roi = new Rect(cropX, cropY, FrameSize, FrameSize);
            firstFrame.Dispose();

            // Reset to beginning
            capture.Set(VideoCaptureProperties.PosFrames, 0);

            var params_ = new int[] { (int)ImwriteFlags.JpegQuality, 98 };

            using (var frame = new Mat())
            using (var resized = new Mat())
            {
                // Read sequentially (no seeking)
                while (capture.Read(frame))
                {
                    if (downloadCancellationToken.Token.IsCancellationRequested)
                        break;

                    // Only process frames we need
                    if (framePositions.Contains(currentFrameIndex))
                    {
                        Cv2.Resize(frame, resized, newSize, 0, 0, InterpolationFlags.Nearest);

                        using (Mat cropped = new Mat(resized, roi))
                        {
                            string frameUuid = Guid.NewGuid().ToString();
                            string framePath = Path.Combine(framesDirectory, $"frame_{frameUuid}.jpg");

                            // Write synchronously
                            Cv2.ImWrite(framePath, cropped, params_);
                        }

                        processedFrames++;

                        // Update progress less frequently to reduce UI overhead
                        if (processedFrames % 10 == 0 || processedFrames == totalFramesToProcess)
                        {
                            double progress = (processedFrames * 100.0) / totalFramesToProcess;
                            progressCallback(progress);

                            mainWindow.Dispatcher.Invoke(() =>
                            {
                                overlayManager.UpdateMessage($"Extracting frames... ({processedFrames} / {totalFramesToProcess})");
                            });
                        }
                    }

                    currentFrameIndex++;
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