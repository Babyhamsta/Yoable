using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace YoableWPF.Models
{
    /// <summary>
    /// A body class and the head class that belong to the same team (e.g. police body + police
    /// head). Lets ClassTransfer reconcile a body box's team with the head box inside it.
    /// </summary>
    public class ClassTeamPair
    {
        public int BodyClassId { get; set; } = -1;
        public int HeadClassId { get; set; } = -1;
    }

    /// <summary>
    /// Represents all data for a Yoable project
    /// </summary>
    public class ProjectData
    {
        public string ProjectName { get; set; }
        public string ProjectPath { get; set; }  // Full path to the .yoable file
        public string ProjectFolder { get; set; }  // Folder containing the project file
        public DateTime CreatedDate { get; set; }
        public DateTime LastModified { get; set; }
        public string Version { get; set; } = "1.0";

        // Class definitions for multi-class labeling
        public List<LabelClass> Classes { get; set; } = new List<LabelClass>();

        // Helper methods for class management
        public LabelClass GetClassById(int id)
        {
            return Classes?.FirstOrDefault(c => c.ClassId == id);
        }

        public LabelClass GetDefaultClass()
        {
            return Classes?.FirstOrDefault() ?? new LabelClass("default", "#E57373", 0);
        }

        public int GetNextClassId()
        {
            return Classes?.Any() == true ? Classes.Max(c => c.ClassId) + 1 : 0;
        }

        public bool HasClasses => Classes?.Count > 0;

        // Image references (paths only, no copies)
        public List<ImageReference> Images { get; set; } = new List<ImageReference>();

        // Labels created in-app (stored in project folder)
        // Key: filename, Value: relative path to label file in project folder
        public Dictionary<string, string> AppCreatedLabels { get; set; } = new Dictionary<string, string>();

        // Labels that reference external .txt files (imported labels)
        // Key: filename, Value: full path to external label file
        public Dictionary<string, string> ImportedLabelPaths { get; set; } = new Dictionary<string, string>();

        // Suggested labels (propagation/retrieval results)
        // Key: filename, Value: list of suggested labels
        public Dictionary<string, List<SuggestedLabel>> SuggestedLabels { get; set; } = new Dictionary<string, List<SuggestedLabel>>();

        // UI State
        public Dictionary<string, ImageStatus> ImageStatuses { get; set; } = new Dictionary<string, ImageStatus>();
        public int LastSelectedImageIndex { get; set; } = -1;
        // Inclusive filename checkpoint for the user's manual labeling progress.
        public string ManualProgressImageFile { get; set; } = string.Empty;
        public string CurrentSortMode { get; set; } = "ByName";
        public string CurrentFilterMode { get; set; } = "All";

        // Model configurations
        // List of loaded model paths
        public List<string> LoadedModelPaths { get; set; } = new List<string>();
        // Legacy single-target mapping, only read from projects saved before a model class could map
        // to several project classes. MigrateLegacyModelClassMappings folds it into
        // ModelClassMappingSets on load and it is never written back.
        public Dictionary<string, Dictionary<int, int>> ModelClassMappings { get; set; } = new Dictionary<string, Dictionary<int, int>>();
        // Key: model path, Value: model class ID -> allowed project class IDs.
        public Dictionary<string, Dictionary<int, List<int>>> ModelClassMappingSets { get; set; } = new Dictionary<string, Dictionary<int, List<int>>>();
        // Key: model path, Value: ModelRole. Only used by the ClassTransfer ensemble mode.
        public Dictionary<string, int> ModelRoles { get; set; } = new Dictionary<string, int>();
        // Body/head class pairs that belong to the same team, used to keep a body box's team
        // consistent with the head box inside it. Only used by the ClassTransfer ensemble mode.
        public List<ClassTeamPair> ClassTeamPairs { get; set; } = new List<ClassTeamPair>();
        // Project class ID -> confidence threshold used when labeling the current image.
        public Dictionary<int, float> AIClassConfidenceThresholds { get; set; } = new Dictionary<int, float>();

        // Statistics (optional, for display purposes)
        public int TotalImages => Images?.Count ?? 0;
        public int TotalLabels => (AppCreatedLabels?.Count ?? 0) + (ImportedLabelPaths?.Count ?? 0);

        /// <summary>
        /// Converts any legacy one-to-one model class mappings into the multi-target format.
        /// Safe to call repeatedly; entries already present in the new format win.
        /// </summary>
        public void MigrateLegacyModelClassMappings()
        {
            ModelClassMappingSets ??= new Dictionary<string, Dictionary<int, List<int>>>();

            if (ModelClassMappings == null || ModelClassMappings.Count == 0)
                return;

            foreach (var modelMapping in ModelClassMappings)
            {
                if (modelMapping.Value == null || ModelClassMappingSets.ContainsKey(modelMapping.Key))
                    continue;

                ModelClassMappingSets[modelMapping.Key] = modelMapping.Value.ToDictionary(
                    classMapping => classMapping.Key,
                    classMapping => new List<int> { classMapping.Value });
            }

            ModelClassMappings.Clear();
        }
    }

    /// <summary>
    /// Reference to an image file (no actual image data stored)
    /// </summary>
    public class ImageReference
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public ImageReference()
        {
        }

        public ImageReference(string fileName, string fullPath, Size dimensions)
        {
            FileName = fileName;
            FullPath = fullPath;
            Width = dimensions.Width;
            Height = dimensions.Height;
        }

        public Size GetSize()
        {
            return new Size(Width, Height);
        }
    }
}
