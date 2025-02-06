using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using YoableWPF;

namespace YoableWPF.Managers
{
    public class CloudUploader
    {
        private readonly HttpClient httpClient = new HttpClient();
        private MainWindow mainWindow;
        private OverlayManager overlayManager;
        private CancellationTokenSource uploadCancellationToken;
        private const string UploadUrl = "http://89.47.89.41/upload";
        private const string BearerToken = "AimmyKeyForTheKeysOfAllKeysVeryPrivate"; // WHAT DO YOU WANT FROM ME? SECURITY? ITS OPEN SOURCE BRO.. That's okay there is security serverside..

        public CloudUploader(MainWindow form, OverlayManager overlay)
        {
            mainWindow = form;
            overlayManager = overlay;
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
        }

        public async Task<bool> AskForUploadAsync(List<string> labelFiles, List<string> imageFiles)
        {
            MessageBoxResult result = MessageBox.Show(
                "Would you like to upload your dataset (images & labels) to Yoable's development cloud for use in their training? It's greatly appreciated, as it can allow them to provide better models that can help you auto label more images.",
                "Upload Dataset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                uploadCancellationToken = new CancellationTokenSource();
                overlayManager.ShowOverlayWithProgress("Uploading dataset...", uploadCancellationToken);

                List<string> filesToUpload = new List<string>();
                filesToUpload.AddRange(labelFiles);
                filesToUpload.AddRange(imageFiles);

                bool uploadSuccess = await UploadFilesAsync(filesToUpload);
                overlayManager.HideOverlay();
                return uploadSuccess;
            }

            return false;
        }

        private async Task<bool> UploadFilesAsync(List<string> filePaths)
        {
            try
            {
                int totalFiles = filePaths.Count;
                int uploaded = 0;
                SemaphoreSlim semaphore = new SemaphoreSlim((int)Properties.Settings.Default.MaxConcurrentUploads);

                await Task.Run(async () =>
                {
                    var uploadTasks = new List<Task>();
                    foreach (var filePath in filePaths)
                    {
                        if (uploadCancellationToken.Token.IsCancellationRequested) break;
                        await semaphore.WaitAsync();
                        var task = UploadFileAsync(filePath).ContinueWith(t =>
                        {
                            if (t.Result)
                            {
                                Interlocked.Increment(ref uploaded);
                                UpdateProgress(uploaded, totalFiles);
                            }
                            semaphore.Release();
                        });
                        uploadTasks.Add(task);
                    }
                    await Task.WhenAll(uploadTasks);
                }, uploadCancellationToken.Token);

                MessageBox.Show("Upload completed successfully!", "Upload Status", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Upload failed: {ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                overlayManager.HideOverlay();
            }
        }

        private async Task<bool> UploadFileAsync(string filePath)
        {
            try
            {
                using (var content = new MultipartFormDataContent())
                {
                    byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                    content.Add(new ByteArrayContent(fileBytes), "file", Path.GetFileName(filePath));

                    HttpResponseMessage response = await httpClient.PostAsync(UploadUrl, content);
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        private void UpdateProgress(int uploaded, int total)
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                int progress = (int)(uploaded / (float)total * 100);
                overlayManager.UpdateProgress(progress);
                overlayManager.UpdateMessage($"Uploading {uploaded}/{total} files...");
            });
        }
    }
}