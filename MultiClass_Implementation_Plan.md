# Yoable Multi-Class Implementation Plan

## Overview
This document outlines the complete implementation plan for adding multi-class support to Yoable. Users will be able to define multiple classes with custom colors, assign labels to classes, and seamlessly switch between classes while drawing.

---

## Phase 1: Core Data Structures

### 1.1 Update Models.cs
Add the `LabelClass` class after `ImageListItem`:

```csharp
public class LabelClass : INotifyPropertyChanged
{
    private string _name;
    private string _colorHex;
    
    public int ClassId { get; set; }
    
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(DisplayText));
            }
        }
    }
    
    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (_colorHex != value)
            {
                _colorHex = value;
                OnPropertyChanged(nameof(ColorHex));
                OnPropertyChanged(nameof(ColorBrush));
            }
        }
    }
    
    // Helper properties for UI binding
    public string DisplayText => $"{Name} (ID: {ClassId})";
    public SolidColorBrush ColorBrush => new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString(ColorHex));
    
    public LabelClass(string name, string colorHex, int classId)
    {
        Name = name;
        ColorHex = colorHex;
        ClassId = classId;
    }
    
    public event PropertyChangedEventHandler PropertyChanged;
    
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
```

### 1.2 Update ProjectData.cs
Add after line 16 (after Version property):

```csharp
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
```

### 1.3 Update DrawingCanvas.cs - LabelData Class
Modify lines 10-27:

```csharp
public class LabelData
{
    public string Name { get; set; }
    public Rect Rect { get; set; }
    public int ClassId { get; set; } = 0; // Default class

    public LabelData(string name, Rect rect, int classId = 0)
    {
        Name = name;
        Rect = rect;
        ClassId = classId;
    }

    // Deep copy constructor
    public LabelData(LabelData source)
    {
        Name = source.Name;
        Rect = source.Rect;
        ClassId = source.ClassId;
    }
}
```

---

## Phase 2: Drawing Canvas Updates

### 2.1 Add Class Management to DrawingCanvas.cs

Add after line 36 (after Labels property):

```csharp
// Class management
public int CurrentClassId { get; set; } = 0;
public event EventHandler<int> CurrentClassChanged;
private List<LabelClass> availableClasses = new List<LabelClass>();

public void SetAvailableClasses(List<LabelClass> classes)
{
    availableClasses = classes ?? new List<LabelClass>();
    
    // If current class doesn't exist in new list, switch to first class
    if (availableClasses.Any() && !availableClasses.Any(c => c.ClassId == CurrentClassId))
    {
        CurrentClassId = availableClasses.First().ClassId;
        OnCurrentClassChanged();
    }
}

public string GetCurrentClassColor()
{
    var currentClass = availableClasses.FirstOrDefault(c => c.ClassId == CurrentClassId);
    return currentClass?.ColorHex ?? "#E57373"; // Fallback to red
}

protected virtual void OnCurrentClassChanged()
{
    CurrentClassChanged?.Invoke(this, CurrentClassId);
    InvalidateVisual(); // Redraw to show new color while drawing
}
```

### 2.2 Update Mouse Wheel Handler
Modify `DrawingCanvas_MouseWheel` method - add at the BEGINNING:

```csharp
private void DrawingCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
{
    // PRIORITY: If we're currently drawing, cycle through classes
    if (IsDrawing && availableClasses.Count > 1)
    {
        int currentIndex = availableClasses.FindIndex(c => c.ClassId == CurrentClassId);
        
        if (e.Delta > 0) // Scroll up - previous class
        {
            currentIndex = currentIndex <= 0 ? availableClasses.Count - 1 : currentIndex - 1;
        }
        else // Scroll down - next class
        {
            currentIndex = currentIndex >= availableClasses.Count - 1 ? 0 : currentIndex + 1;
        }
        
        CurrentClassId = availableClasses[currentIndex].ClassId;
        OnCurrentClassChanged();
        e.Handled = true;
        return;
    }
    
    // Rest of existing zoom logic...
    // (keep all the original zoom code)
}
```

### 2.3 Update Label Creation
Find all places where `new LabelData` is created and add `CurrentClassId`:

**Line 218 in PasteLabels:**
```csharp
var newLabel = new LabelData($"Label {Labels.Count + 1}",
    new Rect(
        clipboardLabel.Rect.X + offsetX,
        clipboardLabel.Rect.Y + offsetY,
        clipboardLabel.Rect.Width,
        clipboardLabel.Rect.Height
    ),
    CurrentClassId); // Use current class
```

**Any other label creation locations** - add the `CurrentClassId` parameter.

### 2.4 Update OnRender for Dynamic Class Colors
In the `OnRender` method, where labels are drawn:

```csharp
protected override void OnRender(DrawingContext dc)
{
    base.OnRender(dc);

    // ... existing image rendering code ...
    
    // Draw labels with their class colors
    double thickness = Properties.Settings.Default.LabelThickness;
    
    foreach (var label in Labels)
    {
        // Get color for this label's class
        var labelClass = availableClasses.FirstOrDefault(c => c.ClassId == label.ClassId);
        string colorHex = labelClass?.ColorHex ?? "#E57373"; // Fallback
        
        var brush = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
        var pen = new Pen(brush, thickness);
        
        // ... rest of drawing code using this pen ...
    }
    
    // Draw current rectangle being drawn with current class color
    if (IsDrawing)
    {
        string currentColorHex = GetCurrentClassColor();
        var currentBrush = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(currentColorHex));
        var currentPen = new Pen(currentBrush, thickness);
        
        // ... draw CurrentRect with currentPen ...
    }
}
```

---

## Phase 3: Labels Panel UI (MainWindow.xaml)

### 3.1 Restructure Right Panel
Replace the entire right panel section (starting at line 573) with:

```xml
<!-- Right Panel - Labels & Classes -->
<Border Grid.Row="1" Grid.Column="2" 
        Background="{DynamicResource SystemControlBackgroundChromeMediumLowBrush}"
        Padding="12">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>  <!-- Header -->
            <RowDefinition Height="Auto"/>  <!-- Class Section -->
            <RowDefinition Height="*"/>     <!-- Labels List -->
        </Grid.RowDefinitions>

        <!-- Panel Header -->
        <Grid Grid.Row="0" Margin="0,0,0,12">
            <TextBlock Text="Labels & Classes" 
                       FontSize="16" 
                       FontWeight="SemiBold"
                       VerticalAlignment="Center"/>
        </Grid>

        <!-- CLASS MANAGEMENT SECTION -->
        <Border Grid.Row="1" Style="{StaticResource ModernCard}" 
                Padding="12" Margin="0,0,0,12">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>
                
                <!-- Section Title -->
                <TextBlock Grid.Row="0" Text="Classes" 
                           FontSize="13" FontWeight="SemiBold" 
                           Margin="0,0,0,8"/>
                
                <!-- Current/Active Class Display -->
                <Border Grid.Row="1" CornerRadius="6" Padding="10,8" 
                        Margin="0,0,0,8" x:Name="CurrentClassBorder" 
                        Background="#44E57373" BorderBrush="#E57373" 
                        BorderThickness="1">
                    <StackPanel Orientation="Horizontal">
                        <Ellipse Width="12" Height="12" 
                                 x:Name="CurrentClassColorIndicator" 
                                 Fill="#E57373" 
                                 Margin="0,0,8,0" 
                                 VerticalAlignment="Center"/>
                        <TextBlock x:Name="CurrentClassNameText" 
                                   Text="default" 
                                   FontWeight="SemiBold" 
                                   VerticalAlignment="Center"/>
                        <TextBlock Text=" (drawing)" 
                                   FontSize="10" 
                                   VerticalAlignment="Center" 
                                   Opacity="0.7"
                                   Margin="4,0,0,0"/>
                    </StackPanel>
                </Border>
                
                <!-- Class List -->
                <ListBox Grid.Row="2" x:Name="ClassListBox"
                         Height="80"
                         Margin="0,0,0,8"
                         SelectionMode="Single"
                         SelectionChanged="ClassListBox_SelectionChanged">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Orientation="Horizontal">
                                <Ellipse Width="10" Height="10" 
                                         Fill="{Binding ColorBrush}"
                                         Margin="0,0,8,0"
                                         VerticalAlignment="Center"/>
                                <TextBlock Text="{Binding Name}" 
                                           VerticalAlignment="Center"/>
                                <TextBlock Text="{Binding ClassId, StringFormat=' (ID: {0})'}" 
                                           FontSize="10"
                                           Opacity="0.6"
                                           VerticalAlignment="Center"
                                           Margin="4,0,0,0"/>
                            </StackPanel>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
                
                <!-- Class Management Buttons -->
                <Grid Grid.Row="3">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    
                    <Button Grid.Column="0" 
                            Content="Add" 
                            Click="AddClass_Click" 
                            Padding="8,4" 
                            Margin="0,0,4,0">
                        <Button.ToolTip>
                            <ToolTip Content="Add a new class"/>
                        </Button.ToolTip>
                    </Button>
                    <Button Grid.Column="1" 
                            Content="Remove" 
                            x:Name="RemoveClassButton"
                            Click="RemoveClass_Click" 
                            Padding="8,4"
                            IsEnabled="False">
                        <Button.ToolTip>
                            <ToolTip Content="Remove selected class"/>
                        </Button.ToolTip>
                    </Button>
                </Grid>
            </Grid>
        </Border>

        <!-- LABELS LIST SECTION -->
        <Border Grid.Row="2" Style="{StaticResource ModernCard}" 
                Padding="0" Margin="0">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto"/>
                    <RowDefinition Height="*"/>
                </Grid.RowDefinitions>
                
                <!-- Labels Header -->
                <TextBlock Grid.Row="0" Text="Image Labels" 
                           FontSize="13" FontWeight="SemiBold" 
                           Margin="12,12,12,8"/>
                
                <!-- Labels ListBox -->
                <ListBox Grid.Row="1" x:Name="LabelListBox" 
                         Style="{StaticResource ModernListBox}"
                         SelectionMode="Single" 
                         SelectionChanged="LabelListBox_SelectionChanged">
                    <ListBox.ItemContainerStyle>
                        <Style TargetType="ListBoxItem" 
                               BasedOn="{StaticResource ImageListItemStyle}"/>
                    </ListBox.ItemContainerStyle>
                </ListBox>
            </Grid>
        </Border>
    </Grid>
</Border>
```

---

## Phase 4: MainWindow Code-Behind

### 4.1 Add Class Management Fields
Add to MainWindow class (after existing manager declarations):

```csharp
// Class management
private List<LabelClass> projectClasses = new List<LabelClass>();
```

### 4.2 Initialize Classes in Constructor
In MainWindow constructor, after line 65:

```csharp
// Subscribe to class changes from DrawingCanvas
drawingCanvas.CurrentClassChanged += DrawingCanvas_CurrentClassChanged;

// Initialize with default class if no project loaded
InitializeDefaultClass();
```

### 4.3 Add Class Management Methods

```csharp
private void InitializeDefaultClass()
{
    if (projectClasses.Count == 0)
    {
        projectClasses.Add(new LabelClass("default", "#E57373", 0));
        RefreshClassList();
    }
}

private void RefreshClassList()
{
    // Update ClassListBox
    ClassListBox.ItemsSource = null;
    ClassListBox.ItemsSource = projectClasses;
    
    // Update DrawingCanvas with available classes
    drawingCanvas.SetAvailableClasses(projectClasses);
    
    // Select current class in list
    var currentClass = projectClasses.FirstOrDefault(c => c.ClassId == drawingCanvas.CurrentClassId);
    if (currentClass != null)
    {
        ClassListBox.SelectedItem = currentClass;
    }
    
    // Update UI display
    UpdateCurrentClassUI();
}

private void UpdateCurrentClassUI()
{
    var currentClass = projectClasses.FirstOrDefault(c => c.ClassId == drawingCanvas.CurrentClassId);
    if (currentClass != null)
    {
        CurrentClassNameText.Text = currentClass.Name;
        
        var color = (Color)ColorConverter.ConvertFromString(currentClass.ColorHex);
        CurrentClassColorIndicator.Fill = new SolidColorBrush(color);
        
        // Semi-transparent background
        CurrentClassBorder.Background = new SolidColorBrush(
            Color.FromArgb(0x44, color.R, color.G, color.B));
        CurrentClassBorder.BorderBrush = new SolidColorBrush(color);
    }
}

private void DrawingCanvas_CurrentClassChanged(object sender, int classId)
{
    // Update UI when class changes (e.g., from mouse wheel during drawing)
    UpdateCurrentClassUI();
    
    // Update selection in ClassListBox
    var selectedClass = projectClasses.FirstOrDefault(c => c.ClassId == classId);
    if (selectedClass != null && ClassListBox.SelectedItem != selectedClass)
    {
        ClassListBox.SelectedItem = selectedClass;
    }
}

private void ClassListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (ClassListBox.SelectedItem is LabelClass selectedClass)
    {
        // Update current drawing class
        drawingCanvas.CurrentClassId = selectedClass.ClassId;
        UpdateCurrentClassUI();
        
        // Enable/disable remove button (can't remove last class)
        RemoveClassButton.IsEnabled = projectClasses.Count > 1;
    }
    else
    {
        RemoveClassButton.IsEnabled = false;
    }
}

private void AddClass_Click(object sender, RoutedEventArgs e)
{
    var dialog = new ClassInputDialog();
    dialog.Owner = this;
    
    if (dialog.ShowDialog() == true)
    {
        int newClassId = projectManager?.CurrentProject?.GetNextClassId() ?? 
                        (projectClasses.Any() ? projectClasses.Max(c => c.ClassId) + 1 : 0);
        
        var newClass = new LabelClass(dialog.ClassName, dialog.ClassColor, newClassId);
        projectClasses.Add(newClass);
        
        RefreshClassList();
        
        // Select the new class
        ClassListBox.SelectedItem = newClass;
        drawingCanvas.CurrentClassId = newClassId;
        
        // Mark project as modified
        if (projectManager?.IsProjectOpen == true)
        {
            MarkProjectDirty();
        }
    }
}

private void RemoveClass_Click(object sender, RoutedEventArgs e)
{
    if (ClassListBox.SelectedItem is not LabelClass classToRemove)
        return;
    
    // Can't remove the last class
    if (projectClasses.Count <= 1)
    {
        MessageBox.Show(
            "Cannot remove the last class. At least one class must exist.",
            "Cannot Remove Class",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return;
    }
    
    // Check if any labels use this class
    int labelCount = 0;
    foreach (var kvp in labelManager.LabelStorage)
    {
        labelCount += kvp.Value.Count(l => l.ClassId == classToRemove.ClassId);
    }
    
    if (labelCount > 0)
    {
        // Show migration dialog
        var migrationDialog = new ClassMigrationDialog(projectClasses, classToRemove, labelCount);
        migrationDialog.Owner = this;
        
        if (migrationDialog.ShowDialog() != true)
            return; // User cancelled
        
        int targetClassId = migrationDialog.TargetClassId;
        bool deleteLabels = migrationDialog.DeleteLabels;
        
        if (deleteLabels)
        {
            // Remove all labels with this class
            foreach (var kvp in labelManager.LabelStorage.ToList())
            {
                kvp.Value.RemoveAll(l => l.ClassId == classToRemove.ClassId);
                
                // Remove entry if no labels remain
                if (kvp.Value.Count == 0)
                {
                    labelManager.LabelStorage.TryRemove(kvp.Key, out _);
                }
            }
            
            // Update image statuses
            _ = UpdateAllImageStatusesAsync();
        }
        else
        {
            // Migrate labels to target class
            foreach (var kvp in labelManager.LabelStorage)
            {
                foreach (var label in kvp.Value.Where(l => l.ClassId == classToRemove.ClassId))
                {
                    label.ClassId = targetClassId;
                }
            }
        }
    }
    
    // Remove the class
    projectClasses.Remove(classToRemove);
    
    // If current drawing class was removed, switch to first class
    if (drawingCanvas.CurrentClassId == classToRemove.ClassId)
    {
        drawingCanvas.CurrentClassId = projectClasses.First().ClassId;
    }
    
    RefreshClassList();
    
    // Mark project as modified
    if (projectManager?.IsProjectOpen == true)
    {
        MarkProjectDirty();
    }
    
    // Refresh current image to show updated labels
    if (!string.IsNullOrEmpty(imageManager.CurrentImagePath))
    {
        var currentFile = Path.GetFileName(imageManager.CurrentImagePath);
        var labels = labelManager.GetLabels(currentFile);
        if (labels.Any())
        {
            drawingCanvas.Labels = new List<LabelData>(labels);
            drawingCanvas.InvalidateVisual();
        }
    }
    
    // Refresh label list to update UI
    uiStateManager.RefreshLabelList();
}
```

### 4.4 Update Project Loading/Saving

**In project load methods**, add:
```csharp
// After loading project
if (projectManager.CurrentProject.HasClasses)
{
    projectClasses = new List<LabelClass>(projectManager.CurrentProject.Classes);
}
else
{
    // Migrate old project - add default class
    projectClasses = new List<LabelClass> 
    { 
        new LabelClass("default", "#E57373", 0) 
    };
    projectManager.CurrentProject.Classes = projectClasses;
}
RefreshClassList();
```

**In project save methods**, add:
```csharp
// Before saving
if (projectManager?.CurrentProject != null)
{
    projectManager.CurrentProject.Classes = projectClasses;
}
```

### 4.5 Update Label List Display

Modify `RefreshLabelList()` in UIStateManager to show class indicators:

```csharp
public void RefreshLabelList()
{
    mainWindow.LabelListBox.Items.Clear();
    
    foreach (var label in mainWindow.drawingCanvas.Labels)
    {
        // Get class info
        var labelClass = mainWindow.projectClasses.FirstOrDefault(c => c.ClassId == label.ClassId);
        string className = labelClass?.Name ?? "unknown";
        string colorHex = labelClass?.ColorHex ?? "#999999";
        
        // Create styled item with class color bar
        var stackPanel = new StackPanel { Orientation = Orientation.Horizontal };
        
        // Color indicator bar
        stackPanel.Children.Add(new Border
        {
            Width = 4,
            Height = 20,
            Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(colorHex)),
            Margin = new Thickness(0, 0, 8, 0),
            CornerRadius = new CornerRadius(2)
        });
        
        // Label text
        stackPanel.Children.Add(new TextBlock
        {
            Text = $"[{className}] {label.Name}",
            VerticalAlignment = VerticalAlignment.Center
        });
        
        mainWindow.LabelListBox.Items.Add(stackPanel);
    }
}
```

---

## Phase 5: Color Picker Dialog

### 5.1 ClassInputDialog.xaml

```xml
<Window x:Class="YoableWPF.ClassInputDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.modernwpf.com/2019"
        xmlns:xctk="http://schemas.xceed.com/wpf/xaml/toolkit"
        ui:WindowHelper.UseModernWindowStyle="True"
        Title="Add New Class" Height="320" Width="420"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <!-- Class Name -->
        <TextBlock Grid.Row="0" Text="Class Name:" Margin="0,0,0,8"/>
        <TextBox Grid.Row="1" x:Name="ClassNameTextBox" 
                 Margin="0,0,0,16" 
                 ui:ControlHelper.PlaceholderText="Enter class name (e.g., person, car, dog)"/>
        
        <!-- Color Section -->
        <TextBlock Grid.Row="2" Text="Class Color:" Margin="0,0,0,8"/>
        
        <!-- Color Picker -->
        <xctk:ColorPicker Grid.Row="3" 
                          x:Name="ClassColorPicker"
                          SelectedColor="#E57373"
                          DisplayColorAndName="True"
                          ShowAvailableColors="True"
                          ShowRecentColors="True"
                          ShowStandardColors="True"
                          StandardButtonHeader="Standard Colors"
                          AvailableColorsHeader="Available Colors"
                          RecentColorsHeader="Recent Colors"
                          Width="200"
                          HorizontalAlignment="Left"
                          Margin="0,0,0,16"/>
        
        <!-- Preview -->
        <Border Grid.Row="4" 
                Background="{DynamicResource SystemControlPageBackgroundChromeLowBrush}"
                BorderBrush="{DynamicResource SystemControlForegroundBaseLowBrush}"
                BorderThickness="1"
                CornerRadius="6"
                Padding="12"
                Margin="0,0,0,16">
            <StackPanel>
                <TextBlock Text="Preview:" FontSize="11" Opacity="0.7" Margin="0,0,0,8"/>
                <Border x:Name="PreviewBorder"
                        Background="#44E57373"
                        BorderBrush="#E57373"
                        BorderThickness="2"
                        CornerRadius="4"
                        Padding="8,6">
                    <StackPanel Orientation="Horizontal">
                        <Ellipse Width="12" Height="12" 
                                 x:Name="PreviewColorIndicator"
                                 Fill="#E57373"
                                 Margin="0,0,8,0"
                                 VerticalAlignment="Center"/>
                        <TextBlock x:Name="PreviewText"
                                   Text="default"
                                   FontWeight="SemiBold"
                                   VerticalAlignment="Center"/>
                    </StackPanel>
                </Border>
            </StackPanel>
        </Border>
        
        <!-- Buttons -->
        <StackPanel Grid.Row="6" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="Cancel" Click="Cancel_Click" 
                    Width="80" Margin="0,0,8,0" IsCancel="True"/>
            <Button Content="Add Class" Click="OK_Click" 
                    Width="100" IsDefault="True"/>
        </StackPanel>
    </Grid>
</Window>
```

### 5.2 ClassInputDialog.xaml.cs

```csharp
using System.Windows;
using System.Windows.Media;
using Xceed.Wpf.Toolkit;

namespace YoableWPF
{
    public partial class ClassInputDialog : Window
    {
        public string ClassName { get; private set; }
        public string ClassColor { get; private set; }

        public ClassInputDialog()
        {
            InitializeComponent();
            ClassNameTextBox.Focus();
            
            // Subscribe to color changes for live preview
            ClassColorPicker.SelectedColorChanged += ColorPicker_SelectedColorChanged;
            ClassNameTextBox.TextChanged += ClassNameTextBox_TextChanged;
            
            // Initial preview update
            UpdatePreview();
        }

        private void ColorPicker_SelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            UpdatePreview();
        }

        private void ClassNameTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            // Update preview text
            PreviewText.Text = string.IsNullOrWhiteSpace(ClassNameTextBox.Text) 
                ? "class name" 
                : ClassNameTextBox.Text.Trim();
            
            // Update preview colors
            if (ClassColorPicker.SelectedColor.HasValue)
            {
                var color = ClassColorPicker.SelectedColor.Value;
                PreviewColorIndicator.Fill = new SolidColorBrush(color);
                PreviewBorder.BorderBrush = new SolidColorBrush(color);
                PreviewBorder.Background = new SolidColorBrush(
                    Color.FromArgb(0x44, color.R, color.G, color.B));
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            ClassName = ClassNameTextBox.Text.Trim();
            
            if (string.IsNullOrWhiteSpace(ClassName))
            {
                MessageBox.Show(
                    "Please enter a class name.",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ClassNameTextBox.Focus();
                return;
            }
            
            // Get selected color as hex
            if (ClassColorPicker.SelectedColor.HasValue)
            {
                var color = ClassColorPicker.SelectedColor.Value;
                ClassColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            else
            {
                ClassColor = "#E57373"; // Default red
            }
            
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
```

**NuGet Package Required:**
```
Install-Package Extended.Wpf.Toolkit
```
This provides the ColorPicker control with full RGB selection capability.

### 5.3 ClassMigrationDialog.xaml

```xml
<Window x:Class="YoableWPF.ClassMigrationDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.modernwpf.com/2019"
        ui:WindowHelper.UseModernWindowStyle="True"
        Title="Remove Class" Height="280" Width="420"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        
        <!-- Warning message -->
        <Border Grid.Row="0" Background="#44FFB74D" 
                BorderBrush="#FFB74D" BorderThickness="1"
                CornerRadius="4" Padding="12" Margin="0,0,0,16">
            <StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                    <TextBlock FontFamily="Segoe MDL2 Assets" 
                               Text="&#xE7BA;" 
                               FontSize="20"
                               Foreground="#FFB74D"
                               Margin="0,0,8,0"/>
                    <TextBlock Text="Class In Use" 
                               FontWeight="Bold" 
                               FontSize="14"
                               VerticalAlignment="Center"/>
                </StackPanel>
                <TextBlock TextWrapping="Wrap">
                    <Run Text="This class has "/>
                    <Run x:Name="LabelCountText" FontWeight="Bold"/>
                    <Run Text=" label(s). Choose how to handle them:"/>
                </TextBlock>
            </StackPanel>
        </Border>
        
        <!-- Option 1: Migrate -->
        <RadioButton Grid.Row="1" x:Name="MigrateRadio" 
                     Content="Move all labels to another class" 
                     IsChecked="True" Margin="0,0,0,8"
                     Checked="MigrateRadio_Checked"/>
        
        <ComboBox Grid.Row="2" x:Name="TargetClassComboBox" 
                  Margin="20,0,0,16" 
                  DisplayMemberPath="Name"
                  IsEnabled="True">
            <ComboBox.ItemTemplate>
                <DataTemplate>
                    <StackPanel Orientation="Horizontal">
                        <Ellipse Width="10" Height="10" 
                                 Fill="{Binding ColorBrush}"
                                 Margin="0,0,8,0"
                                 VerticalAlignment="Center"/>
                        <TextBlock Text="{Binding Name}"/>
                    </StackPanel>
                </DataTemplate>
            </ComboBox.ItemTemplate>
        </ComboBox>
        
        <!-- Option 2: Delete -->
        <RadioButton Grid.Row="3" x:Name="DeleteRadio" 
                     Content="Delete all labels with this class (cannot be undone!)" 
                     Foreground="#E57373" 
                     VerticalAlignment="Top"
                     Checked="DeleteRadio_Checked"/>
        
        <!-- Buttons -->
        <StackPanel Grid.Row="4" Orientation="Horizontal" 
                    HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button Content="Cancel" Click="Cancel_Click" 
                    Width="80" Margin="0,0,8,0" IsCancel="True"/>
            <Button Content="Remove Class" Click="OK_Click" 
                    Width="120" x:Name="RemoveButton"/>
        </StackPanel>
    </Grid>
</Window>
```

### 5.4 ClassMigrationDialog.xaml.cs

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace YoableWPF
{
    public partial class ClassMigrationDialog : Window
    {
        public int TargetClassId { get; private set; }
        public bool DeleteLabels { get; private set; }

        private List<LabelClass> availableClasses;
        private LabelClass classToRemove;

        public ClassMigrationDialog(List<LabelClass> allClasses, LabelClass classToRemove, int labelCount)
        {
            InitializeComponent();
            
            this.classToRemove = classToRemove;
            
            // Filter out the class being removed
            availableClasses = allClasses.Where(c => c.ClassId != classToRemove.ClassId).ToList();
            
            // Set up UI
            LabelCountText.Text = labelCount.ToString();
            TargetClassComboBox.ItemsSource = availableClasses;
            
            if (availableClasses.Any())
            {
                TargetClassComboBox.SelectedIndex = 0;
            }
        }

        private void MigrateRadio_Checked(object sender, RoutedEventArgs e)
        {
            TargetClassComboBox.IsEnabled = true;
            DeleteLabels = false;
        }

        private void DeleteRadio_Checked(object sender, RoutedEventArgs e)
        {
            TargetClassComboBox.IsEnabled = false;
            DeleteLabels = true;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (MigrateRadio.IsChecked == true)
            {
                if (TargetClassComboBox.SelectedItem is LabelClass targetClass)
                {
                    TargetClassId = targetClass.ClassId;
                    DeleteLabels = false;
                }
                else
                {
                    MessageBox.Show(
                        "Please select a target class.",
                        "Validation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }
            else
            {
                // Confirm deletion
                var result = MessageBox.Show(
                    $"Are you sure you want to delete all labels with class '{classToRemove.Name}'?\n\nThis action cannot be undone!",
                    "Confirm Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result != MessageBoxResult.Yes)
                    return;
                
                DeleteLabels = true;
            }
            
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
```

---

## Phase 6: Manager Updates

### 6.1 LabelManager.cs Changes

**Line 359 - Update ExportLabelsToYolo:**
```csharp
// Change from:
writer.WriteLine($"0 {x_center:F6} {y_center:F6} {width:F6} {height:F6}");

// To:
writer.WriteLine($"{label.ClassId} {x_center:F6} {y_center:F6} {width:F6} {height:F6}");
```

**Line 409 - Update ExportLabelsBatchAsync:**
```csharp
// Change from:
writer.WriteLine($"0 {x_center:F6} {y_center:F6} {width:F6} {height:F6}");

// To:
writer.WriteLine($"{label.ClassId} {x_center:F6} {y_center:F6} {width:F6} {height:F6}");
```

**Lines 193-260 - Update LoadYoloLabelsOptimized to parse ClassId:**
```csharp
// Around line 193, after parsing the line parts:
int classId = 0;
if (int.TryParse(parts[0].ToString(), out int parsedClassId))
{
    classId = parsedClassId;
}

// Around line 233, when creating the label:
var label = new LabelData(
    $"Imported Label {existingCount + newLabels.Count + 1}",
    new Rect(x, y, width, height),
    classId);
```

**Lines 300-337 - Update LoadYoloLabels (legacy method):**
```csharp
// Around line 304, in the while loop:
int classId = 0;
if (int.TryParse(parts[0], out int parsedClassId))
{
    classId = parsedClassId;
}

// Around line 316, when creating the label:
var label = new LabelData(
    $"Imported Label {labelCount}", 
    new Rect(x, y, width, height),
    classId);
```

**Lines 424-451 - Update AddAILabels signature:**
```csharp
// Change method signature from:
public void AddAILabels(string fileName, List<Rectangle> detectedBoxes)

// To:
public void AddAILabels(string fileName, List<(Rectangle box, int classId)> detectedBoxes)
{
    if (!labelStorage.ContainsKey(fileName))
        labelStorage.TryAdd(fileName, new List<LabelData>());

    foreach (var detection in detectedBoxes)
    {
        // Safety check
        if (detection.box.Width <= 0 || detection.box.Height <= 0)
        {
            Debug.WriteLine($"Warning: Skipping invalid box with dimensions {detection.box.Width}x{detection.box.Height}");
            continue;
        }

        var labelCount = labelStorage[fileName].Count + 1;
        var label = new LabelData(
            $"AI Label {labelCount}", 
            new Rect(detection.box.X, detection.box.Y, detection.box.Width, detection.box.Height),
            detection.classId);

        labelStorage.AddOrUpdate(
            fileName,
            new List<LabelData> { label },
            (k, existing) =>
            {
                existing.Add(label);
                return existing;
            });
    }
}
```

### 6.2 ProjectManager.cs Changes

**Line 98 - Update CreateNewProject:**
```csharp
CurrentProject = new ProjectData
{
    ProjectName = projectName,
    ProjectPath = Path.Combine(projectFolder, projectName + PROJECT_EXTENSION),
    ProjectFolder = projectFolder,
    CreatedDate = DateTime.Now,
    LastModified = DateTime.Now,
    Classes = new List<LabelClass> 
    { 
        new LabelClass("default", "#E57373", 0) 
    }
};
```

---

## Phase 7: Settings Cleanup

### 7.1 SettingsWindow.xaml
Remove the "Label Color" picker section from the settings. This is now managed per-class.

Keep only:
- **Label Thickness** - applies to all classes
- **Crosshair settings**
- **Other general settings**

The removed section should be around the label settings area.

---

## Phase 8: YoloAI Integration (Future Phase)

### 8.1 Update AI Detection Calls
When calling `labelManager.AddAILabels()`, pass ClassId from detections:

```csharp
var detections = yoloAI.DetectObjects(imagePath);
var boxesWithClasses = detections.Select(d => (d.Box, d.ClassId)).ToList();
labelManager.AddAILabels(fileName, boxesWithClasses);
```

### 8.2 Class Filtering/Mapping (Optional - Phase 2)
Before importing AI labels, you can add a dialog to let users:
- Select which AI class IDs to import
- Map AI classes to project classes
- Create new project classes for unmapped AI classes

This can be implemented in a future iteration.

---

## Testing Checklist

### Basic Functionality
- [ ] Create new project → should have default class
- [ ] Add multiple classes with custom colors
- [ ] Select class from ClassListBox → updates drawing color
- [ ] Draw labels → should use current class color
- [ ] Mouse wheel while drawing → cycles through classes and changes color
- [ ] Mouse wheel changes selected item in ClassListBox

### Label Management
- [ ] Labels display with class color indicator in label list
- [ ] Existing labels retain their class when switching current class
- [ ] Copy/paste labels preserve ClassId
- [ ] Undo/redo preserves ClassId

### Class Removal
- [ ] Cannot remove last class
- [ ] Remove class with no labels → works immediately
- [ ] Remove class with labels → shows migration dialog
- [ ] Migrate labels to another class → labels update correctly
- [ ] Delete labels option → removes all labels of that class

### Project Operations
- [ ] Save project → Classes saved correctly
- [ ] Load project with classes → Classes restored
- [ ] Load old project (v1) → Default class auto-created
- [ ] Export to YOLO → ClassIds in correct position
- [ ] Import YOLO labels → ClassIds parsed correctly

### AI Integration
- [ ] AI detection imports with ClassId
- [ ] Unknown ClassIds can be handled gracefully

### UI/UX
- [ ] Current class indicator updates when class changes
- [ ] Class colors display correctly throughout UI
- [ ] Preview in add class dialog updates live
- [ ] Remove class button enabled only when valid

---

## Summary of Changes

### Modified Files
1. **Models.cs** - Add LabelClass
2. **ProjectData.cs** - Add Classes list + helper methods
3. **DrawingCanvas.cs** - Add ClassId to LabelData, class cycling, color management
4. **MainWindow.xaml** - Restructure right panel with class section
5. **MainWindow_xaml.cs** - Class management methods, event handlers
6. **UIStateManager.cs** - Update RefreshLabelList() for class display
7. **LabelManager.cs** - Parse/export ClassId in YOLO format, update AI imports
8. **ProjectManager.cs** - Initialize default class in new projects
9. **SettingsWindow.xaml** - Remove single label color picker

### New Files
1. **ClassInputDialog.xaml + .cs** - Add class with color picker
2. **ClassMigrationDialog.xaml + .cs** - Handle class removal

### NuGet Package Required
```
Install-Package Extended.Wpf.Toolkit
```

### Key Features Implemented
✅ Class list in labels panel (like image filters)  
✅ Add/Remove buttons directly in panel  
✅ Full RGB color picker with preview  
✅ Mouse wheel cycles classes while drawing  
✅ Current class indicator with color  
✅ Class migration when removing  
✅ YOLO export with correct ClassIds  
✅ AI import with ClassId support  
✅ Backward compatibility (default class for old projects)  
✅ Number keys remain for label selection  

---

## Implementation Order

1. **Phase 1**: Core data structures (Models, ProjectData, LabelData)
2. **Phase 2**: Drawing canvas updates (class management, mouse wheel)
3. **Phase 3**: UI changes (MainWindow.xaml)
4. **Phase 4**: Code-behind (MainWindow_xaml.cs methods)
5. **Phase 5**: Dialogs (ClassInputDialog, ClassMigrationDialog)
6. **Phase 6**: Manager updates (LabelManager, ProjectManager)
7. **Phase 7**: Settings cleanup
8. **Phase 8**: Testing and refinement

Start with Phase 1 and work through systematically, testing after each phase.
