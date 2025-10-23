using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.IO;
using Yoable.Services;

namespace Yoable.Desktop;

public partial class NewProjectDialog : Window
{
    private IFileService _fileService = null!;
    private IDialogService _dialogService = null!;

    public string? ProjectName { get; private set; }
    public string? ProjectLocation { get; private set; }

    public NewProjectDialog()
    {
        InitializeComponent();

        _fileService = new AvaloniaFileService();
        _dialogService = new AvaloniaDialogService();

        // Get controls
        var browseButton = this.FindControl<Button>("BrowseButton");
        var createButton = this.FindControl<Button>("CreateButton");
        var cancelButton = this.FindControl<Button>("CancelButton");
        var locationTextBox = this.FindControl<TextBox>("ProjectLocationTextBox");

        // Wire up event handlers
        if (browseButton != null)
            browseButton.Click += BrowseButton_Click;
        if (createButton != null)
            createButton.Click += CreateButton_Click;
        if (cancelButton != null)
            cancelButton.Click += CancelButton_Click;

        // Set default location to Documents/Yoable
        var documentsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        if (locationTextBox != null)
            locationTextBox.Text = Path.Combine(documentsPath, "Yoable");
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var selectedPath = await _fileService.OpenFolderAsync("Select Project Location");
        if (selectedPath != null)
        {
            var locationTextBox = this.FindControl<TextBox>("ProjectLocationTextBox");
            if (locationTextBox != null)
                locationTextBox.Text = selectedPath;
        }
    }

    private async void CreateButton_Click(object? sender, RoutedEventArgs e)
    {
        var nameTextBox = this.FindControl<TextBox>("ProjectNameTextBox");
        var locationTextBox = this.FindControl<TextBox>("ProjectLocationTextBox");

        if (nameTextBox == null || string.IsNullOrWhiteSpace(nameTextBox.Text))
        {
            await _dialogService.ShowErrorAsync("Validation Error", "Please enter a project name.");
            return;
        }

        if (locationTextBox == null || string.IsNullOrWhiteSpace(locationTextBox.Text))
        {
            await _dialogService.ShowErrorAsync("Validation Error", "Please select a project location.");
            return;
        }

        ProjectName = nameTextBox.Text;
        ProjectLocation = locationTextBox.Text;

        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
