using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PhotoSiftReborn;

// M6H: presentation-only selection. The existing source-import and results
// root handlers remain the sole owners of validation and persistence.
public sealed partial class MainWindow
{
    private readonly TextBlock _studioResultsHint = Muted("Select a results folder before sorting.");
    private Button? _studioChooseOutput;

    private async Task ShowStudioPhotoSourceChoiceAsync()
    {
        if (_busy || !EnsureWritable()) return;
        var chooser = new Window
        {
            Title = "Add photographs — PhotoSift Reborn",
            Width = 560, Height = 360, MinWidth = 470, MinHeight = 310,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = StudioPalette.Shell,
            Foreground = StudioPalette.Text
        };
        var content = new StackPanel { Margin = new Thickness(22), Spacing = 14 };
        content.Children.Add(new TextBlock
        {
            Text = "What would you like to add?",
            FontSize = 23, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(Muted("Choose specific photographs, including multiple files from a folder, or import an entire folder. Adding photos never moves the originals."));
        var files = Button("Choose individual photos...", () => chooser.Close((int)StudioImportSource.Files));
        files.Classes.Add("studio-primary");
        files.MinHeight = 44;
        content.Children.Add(files);
        var folder = Button("Choose a folder...", () => chooser.Close((int)StudioImportSource.Folder));
        folder.MinHeight = 44;
        content.Children.Add(folder);
        var cancel = Button("Cancel - keep current collection", () => chooser.Close((int)StudioImportSource.Cancel));
        cancel.HorizontalAlignment = HorizontalAlignment.Right;
        content.Children.Add(cancel);
        chooser.Content = content;
        var choice = StudioImportGuidance.RequireSource(await chooser.ShowDialog<int>(this));
        // Re-enter existing permission checks after the modal is closed.
        // No chooser return value can grant a native operation or approval.
        if (choice == StudioImportSource.Files) await AddFilesAsync();
        else if (choice == StudioImportSource.Folder) await AddInputAsync();
    }
}
