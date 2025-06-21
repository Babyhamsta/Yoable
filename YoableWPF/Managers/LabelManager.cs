using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace YoableWPF.Managers
{
    public class LabelManager
    {
        // Keep same structure as original
        private Dictionary<string, List<LabelData>> labelStorage = new();

        public Dictionary<string, List<LabelData>> LabelStorage => labelStorage;

        // Direct port of label management methods
        public List<LabelData> GetLabels(string fileName)
        {
            return labelStorage.ContainsKey(fileName) ?
                new List<LabelData>(labelStorage[fileName]) :
                new List<LabelData>();
        }

        public void SaveLabels(string fileName, List<LabelData> labels)
        {
            labelStorage[fileName] = new List<LabelData>(labels);
        }

        public void ClearAll()
        {
            labelStorage.Clear();
        }

        // Direct port of LoadYoloLabels
        public int LoadYoloLabels(string labelFile, string imagePath, ImageManager imageManager)
        {
            if (!File.Exists(labelFile)) return 0;

            string fileName = Path.GetFileName(imagePath);

            // Ensure the correct image path
            if (!imageManager.ImagePathMap.TryGetValue(Path.GetFileName(imagePath), out ImageManager.ImageInfo imageInfo))
            {
                return 0;
            }
            imagePath = imageInfo.Path;

            int labelsAdded = 0;

            try
            {
                using (FileStream fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (Bitmap tempImage = new Bitmap(fs))
                {
                    int imgWidth = tempImage.Width;
                    int imgHeight = tempImage.Height;

                    if (!labelStorage.ContainsKey(fileName))
                        labelStorage[fileName] = new List<LabelData>();

                    using StreamReader reader = new StreamReader(labelFile);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] parts = line.Trim().Split(' ');
                        if (parts.Length != 5) continue;

                        float xCenter = float.Parse(parts[1], CultureInfo.InvariantCulture) * imgWidth;
                        float yCenter = float.Parse(parts[2], CultureInfo.InvariantCulture) * imgHeight;
                        float width = float.Parse(parts[3], CultureInfo.InvariantCulture) * imgWidth;
                        float height = float.Parse(parts[4], CultureInfo.InvariantCulture) * imgHeight;

                        double x = xCenter - (width / 2);
                        double y = yCenter - (height / 2);

                        var labelCount = labelStorage[fileName].Count + 1;
                        var label = new LabelData($"Imported Label {labelCount}", new Rect(x, y, width, height));
                        labelStorage[fileName].Add(label);

                        labelsAdded++;
                    }
                }
            }
            catch (Exception)
            {
                // Silent fail like original
            }

            return labelsAdded;
        }

        // Direct port of ExportLabelsToYolo
        public void ExportLabelsToYolo(string filePath, string imagePath, List<LabelData> labelsToExport)
        {
            using Bitmap image = new Bitmap(imagePath);
            int imageWidth = image.Width;
            int imageHeight = image.Height;

            using StreamWriter writer = new(filePath)
            {
                AutoFlush = true
            };

            foreach (var label in labelsToExport)
            {
                float x_center = (float)((label.Rect.X + label.Rect.Width / 2f) / imageWidth);
                float y_center = (float)(label.Rect.Y + label.Rect.Height / 2f) / imageHeight;
                float width = (float)label.Rect.Width / (float)imageWidth;
                float height = (float)label.Rect.Height / (float)imageHeight;

                writer.WriteLine($"0 {x_center:F6} {y_center:F6} {width:F6} {height:F6}");
            }
        }

        // For AI labels
        public void AddAILabels(string fileName, List<Rectangle> detectedBoxes)
        {
            if (!labelStorage.ContainsKey(fileName))
                labelStorage[fileName] = new List<LabelData>();

            foreach (var box in detectedBoxes)
            {
                // Safety check to ensure positive dimensions
                if (box.Width <= 0 || box.Height <= 0)
                {
                    Debug.WriteLine($"Warning: Skipping invalid box with dimensions {box.Width}x{box.Height}");
                    continue;
                }

                var labelCount = labelStorage[fileName].Count + 1;
                var label = new LabelData($"AI Label {labelCount}", new Rect(box.X, box.Y, box.Width, box.Height));
                labelStorage[fileName].Add(label);
            }
        }
    }
}