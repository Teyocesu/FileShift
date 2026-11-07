using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using FileShift.Core;
using FileShift.Infrastructure;

namespace FileShift.App;

public partial class MainWindow : Window
{
    private readonly ConversionQueue _queue;

    public MainWindow()
    {
        InitializeComponent();
        _queue = new ConversionQueue(new IConversionEngineAdapter[]
        {
            new ImageMagickAdapter(),
            new FFmpegAdapter(),
            new PdfAdapter(),
            new LibreOfficeAdapter()
        });
        _queue.JobUpdated += OnJobUpdated;
        RefreshJobList();
    }

    private void OnJobUpdated(ConversionJob job)
    {
        Dispatcher.InvokeAsync(RefreshJobList);
    }

    private void RefreshJobList()
    {
        LstJobs.Items.Clear();
        var jobs = _queue.Jobs;
        if (jobs.Count == 0)
        {
            LstJobs.Items.Add("Sin conversiones activas. Arrastrá archivos a la ventana o usá clic derecho -> 'Convertir a…' en el Explorador de Windows.");
            return;
        }

        foreach (var job in jobs)
        {
            var summary = $"[{job.Status}] { System.IO.Path.GetFileName(job.InputPath) } -> {job.TargetFormat.ToUpperInvariant()} ({job.ProgressPercent:0}%) | Motor: {job.EngineUsed}";
            if (!string.IsNullOrWhiteSpace(job.OutputPath) && job.Status == JobStatus.Completed)
                summary += $" -> {job.OutputPath}";
            if (!string.IsNullOrWhiteSpace(job.ErrorMessage))
                summary += $" | Error: {job.ErrorMessage}";
            LstJobs.Items.Add(summary);
        }
    }

    private string GetSelectedTargetFormat()
    {
        if (CmbTargetFormat.SelectedItem is ComboBoxItem item && item.Content is string fmt)
            return fmt;
        return "webp";
    }

    private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Seleccionar archivos para convertir con FileShift"
        };
        if (dlg.ShowDialog() == true)
        {
            var target = GetSelectedTargetFormat();
            foreach (var file in dlg.FileNames)
            {
                _queue.Enqueue(file, target);
            }
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var target = GetSelectedTargetFormat();
            foreach (var file in files)
            {
                _queue.Enqueue(file, target);
            }
        }
    }

    private void BtnNavActivity_Click(object sender, RoutedEventArgs e)
    {
        TxtHeaderTitle.Text = "Actividad";
        TxtHeaderSub.Text = "Arrastrá archivos aquí o convertí con clic derecho desde Windows Explorer";
    }

    private void BtnNavAutomations_Click(object sender, RoutedEventArgs e)
    {
        TxtHeaderTitle.Text = "Automatizaciones de Carpetas";
        TxtHeaderSub.Text = "Vigilancia activa con FileSystemWatcher, estabilidad de escritura y prevención de bucles";
    }

    private void BtnNavSettings_Click(object sender, RoutedEventArgs e)
    {
        TxtHeaderTitle.Text = "Configuración y Menú Contextual de Windows 11";
        TxtHeaderSub.Text = "Ajustá destinos favoritos, presets, concurrencia y salida no destructiva";
    }

    protected override void OnClosed(EventArgs e)
    {
        _queue.Dispose();
        base.OnClosed(e);
    }
}
