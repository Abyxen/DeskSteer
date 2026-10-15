using System.Windows;
using Microsoft.Win32;

namespace DeskSteer.App;

public interface IDiagnosticDestination
{
    string? ChoosePath(Window owner);
}

public sealed class DiagnosticDestination : IDiagnosticDestination
{
    public string? ChoosePath(Window owner)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "DeskSteer-diagnostics.json", Filter = "JSON diagnostics|*.json", DefaultExt = ".json"
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
