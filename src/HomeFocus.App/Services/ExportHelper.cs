using System.Diagnostics;
using HomeFocus.App.Localization;
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
            if (MessageDialog.Show(Loc.T("export.done"), Loc.F("export.doneText", n, System.IO.Path.GetFileName(dlg.FileName)),
                    Loc.T("common.openFolder"), Loc.T("common.close")))
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + dlg.FileName } });
        }
        catch (IOException ex)
        {
            MessageDialog.Show(Loc.T("export.failed"), ex.Message, null, Loc.T("common.close"));
        }
    }
}
