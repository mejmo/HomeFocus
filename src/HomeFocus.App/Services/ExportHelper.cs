using System.Diagnostics;
using HomeFocus.App.Views;
using HomeFocus.Core.Reports;
using Microsoft.Win32;

namespace HomeFocus.App.Services;

public static class ExportHelper
{
    public static void ExportCsv(DateOnly from, DateOnly toExclusive, string fileName)
    {
        var dlg = new SaveFileDialog
        {
            FileName = fileName,
            Filter = "CSV (*.csv)|*.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() != true) return;

        var s = AppServices.Instance;
        var (f, t) = ReportService.Bounds(from, toExclusive);
        try
        {
            var n = CsvExporter.Export(s.Activities, s.Categorizer, s.CategoryList.ToDictionary(c => c.Id, c => c.Name), f, t, dlg.FileName);
            if (MessageDialog.Show("Export hotový", $"Uložených {n} záznamov do súboru {System.IO.Path.GetFileName(dlg.FileName)}.",
                    "Otvoriť priečinok", "Zavrieť"))
                Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\"");
        }
        catch (IOException ex)
        {
            MessageDialog.Show("Export zlyhal", ex.Message, null, "Zavrieť");
        }
    }
}
