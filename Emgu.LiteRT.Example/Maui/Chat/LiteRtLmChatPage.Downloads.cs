using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Maui.Demo.Lite;

public partial class LiteRtLmChatPage
{
    private sealed class ModelDownloadRow
    {
        public ModelOption Option;
        public Label Status;
        public Microsoft.Maui.Controls.Shapes.Path StatusIcon;
        public Label Detail;
        public Button Continue;
        public bool InSheet;
    }

    private readonly List<ModelDownloadRow> _modelDownloadRows = new();
    private ModelChoice? _downloadingChoice;
    private long _downloadReceived;
    private long? _downloadTotal;

    private static string DownloadAmount(long received, long? total)
    {
        bool gigabytes = (total ?? received) >= 1_000_000_000;
        double divisor = gigabytes ? 1e9 : 1e6;
        return total.HasValue
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.0} of {1:0.0} {2}", received / divisor, total.Value / divisor, gigabytes ? "GB" : "MB")
            : string.Format(CultureInfo.CurrentCulture, "{0:0.0} {1}", received / divisor, gigabytes ? "GB" : "MB");
    }

    private static string PausedDownload(ModelOption option) => "Download paused · "
        + DownloadAmount(option.File.PartialDownloadBytes, option.File.PartialDownloadTotalBytes);

    private static void SetStatusIcon(ModelDownloadRow row, string data, Color color)
    {
        if (row.StatusIcon == null) return;
        row.StatusIcon.Data = (Microsoft.Maui.Controls.Shapes.Geometry)new Microsoft.Maui.Controls.Shapes.PathGeometryConverter().ConvertFromInvariantString(data);
        row.StatusIcon.Fill = new SolidColorBrush(color);
    }

    private void UpdateModelDownloadRows()
    {
        foreach (ModelDownloadRow row in _modelDownloadRows)
            UpdateModelDownloadRow(row);
    }

    private void UpdateModelDownloadRow(ModelDownloadRow row)
    {
        ModelOption option = row.Option;
        bool downloading = _downloadingChoice == option.Choice;
        bool downloaded = IsDownloaded(option);
        bool paused = !downloading && !downloaded && option.File.PartialDownloadBytes > 0;
        // On this device / a download is waiting, in progress or paused, next to the text.
        SetStatusIcon(row, downloaded ? Theme.PathDevice : paused ? Theme.PathPause : Theme.PathCloudDownload,
            downloaded ? Theme.Success : downloading ? Theme.Accent : Theme.SecondaryText);
        if (downloading)
            row.Status.Text = "Downloading · " + DownloadAmount(_downloadReceived, _downloadTotal);
        else if (downloaded)
        {
            try { row.Status.Text = "Downloaded · " + DownloadAmount(new FileInfo(option.File.LocalFile).Length, null) + " on disk"; }
            catch (IOException) { row.Status.Text = "Downloaded"; }
            catch (UnauthorizedAccessException) { row.Status.Text = "Downloaded"; }
        }
        else row.Status.Text = paused ? PausedDownload(option) : "Download · " + option.Size;
        string detail = option.Detail;
        int prefix = detail.IndexOf(" download.", StringComparison.Ordinal);
        if (downloaded && detail.StartsWith("~", StringComparison.Ordinal) && prefix >= 0)
            detail = detail.Substring(prefix + " download.".Length).TrimStart();
        row.Detail.Text = detail;
        row.Continue.IsVisible = paused;
        row.Continue.IsEnabled = CanChangeModel;
    }
}
