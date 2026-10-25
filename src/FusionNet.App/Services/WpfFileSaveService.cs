using System.IO;
using FusionNet.Core.Abstractions;
using FusionNet.Core.Utilities;

namespace FusionNet.App.Services;

/// <summary>WPF implementation of the file pickers used by export buttons.</summary>
public sealed class WpfFileSaveService : IFileSaveService
{
    public string? PickSavePath(string suggestedFileName, string filter)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = string.IsNullOrWhiteSpace(suggestedFileName) ? "fusionnet-report.txt" : suggestedFileName,
            Filter = string.IsNullOrWhiteSpace(filter) ? "All files (*.*)|*.*" : filter,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = EnsureDirectory(AppPaths.ReportDirectory)
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickOpenPath(string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = string.IsNullOrWhiteSpace(filter) ? "All files (*.*)|*.*" : filter,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string EnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            // picker still works from the default location
        }

        return path;
    }
}
