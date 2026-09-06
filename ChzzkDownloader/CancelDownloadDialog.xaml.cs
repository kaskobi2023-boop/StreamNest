using System.Windows;

namespace ChzzkDownloader;

public enum CancelDownloadChoice
{
    Continue,
    KeepPartialFiles,
    DeletePartialFiles
}

public partial class CancelDownloadDialog : Window
{
    public CancelDownloadDialog()
    {
        InitializeComponent();
    }

    public CancelDownloadChoice Choice { get; private set; } = CancelDownloadChoice.Continue;

    private void ContinueButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = CancelDownloadChoice.Continue;
        DialogResult = false;
    }

    private void KeepButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = CancelDownloadChoice.KeepPartialFiles;
        DialogResult = true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = CancelDownloadChoice.DeletePartialFiles;
        DialogResult = true;
    }
}
