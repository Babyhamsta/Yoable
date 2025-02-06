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
            if (desiredFps > fps) desiredFps = fps;

            int frameStep = (int)(fps / desiredFps);
            int totalFramesToProcess = (int)Math.Ceiling((double)frameCount / frameStep);
            int processedFrames = 0;

            var tasks = new List<Task>();
            string framesDirectory = Path.Combine(videoDirectory, "frames");

            using (var frame = new Mat())
            using (var resized = new Mat())
            {
                for (int currentFrame = 0; currentFrame < frameCount; currentFrame += frameStep)
                {
                    if (downloadCancellationToken.Token.IsCancellationRequested) break;

                    if (!capture.Read(frame)) break;

                    // Scale to maintain aspect ratio, ensuring the smallest dimension is >= FrameSize
                    double scale = Math.Max((double)FrameSize / frame.Width, (double)FrameSize / frame.Height);
                    int newWidth = (int)(frame.Width * scale);
                    int newHeight = (int)(frame.Height * scale);

                    Cv2.Resize(frame, resized, new Size(newWidth, newHeight));

                    // Compute the center crop
                    int x = (newWidth - FrameSize) / 2;
                    int y = (newHeight - FrameSize) / 2;
                    var roi = new Rect(x, y, FrameSize, FrameSize);

                    // Allocate a new Mat for cropped image
                    Mat cropped = new Mat(resized, roi);

                    string framePath = Path.Combine(framesDirectory, $"frame_{Guid.NewGuid()}.jpg");

                    // Offload image writing asynchronously
                    tasks.Add(Task.Run(() =>
                    {
                        Cv2.ImWrite(framePath, cropped);
                        cropped.Dispose(); // Free memory after writing
                    }));

                    processedFrames++;

                    double progress = (processedFrames * 100.0) / totalFramesToProcess;
                    progressCallback(progress);

                    mainWindow.Dispatcher.Invoke(() =>
                    {
                        overlayManager.UpdateMessage($"Extracting frames... ({processedFrames} / {totalFramesToProcess})");
                    });
                }
            }

            // Wait for all image write operations to complete
            await Task.WhenAll(tasks);
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