using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using FileShift.Core;
using FileShift.Infrastructure;

namespace FileShift.App;

public partial class MainWindow : Window
{
    private readonly UserSettings _settings;
    private readonly ConversionQueue _queue;
    private readonly FolderAutomationService _automationService;
    private readonly bool _libreOfficeInstalled;
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private IReadOnlyList<string> _selectedFiles = [];
    private bool _exitRequested;
    private bool _shutdownComplete;

    public MainWindow(IReadOnlyList<string> startupFiles)
    {
        InitializeComponent();

        _settings = UserSettingsStore.Load();
        _libreOfficeInstalled = LibreOfficeAdapter.FindSofficeBinary() is not null;

        _queue = new ConversionQueue(
            new IConversionEngineAdapter[]
            {
                new ImageMagickAdapter(),
                new FFmpegAdapter(),
                new PdfAdapter(),
                new LibreOfficeAdapter()
            },
            _settings.MaxConcurrency,
            new OutputIntegrityValidator());

        _automationService = new FolderAutomationService(_queue, () => Dispatcher.Invoke(() => _settings.Presets.ToArray()));
        _automationService.SyncRules(_settings.Automations);

        _queue.JobUpdated += OnJobUpdated;

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "FileShift",
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("Abrir FileShift", null, (_, _) => RestoreWindow());
        _tray.ContextMenuStrip.Items.Add("Salir", null, (_, _) => { _exitRequested = true; Close(); });
        _tray.DoubleClick += (_, _) => RestoreWindow();

        TxtQuality.Text = _settings.DefaultQuality.ToString(CultureInfo.InvariantCulture);
        InitializeTargetFormatPickers();
        PopulatePresetsUi();
        PopulateAutomationsUi();
        PopulateGeneralSettingsUi();
        PopulateMenuSettingsUi();
        RefreshJobList();
        _ = RunDoctorDiagnosticsAsync();
        if (startupFiles.Count > 0) SelectFiles(startupFiles);
    }

    private void InitializeTargetFormatPickers()
    {
        IReadOnlyList<string> compatibleTargets = _selectedFiles.Count == 0
            ? GetAllCanonicalFormats()
            : _selectedFiles.Select(file => FormatMatrix.GetSupportedTargets(Path.GetExtension(file), _libreOfficeInstalled))
                .Aggregate((left, right) => left.Intersect(right).ToArray());

        TxtCompatibilityHint.Text = _selectedFiles.Count == 0 ? "Seleccioná archivos para convertir."
            : compatibleTargets.Count == 0 ? "Los archivos seleccionados no tienen un destino compatible en común."
            : $"{_selectedFiles.Count} archivo(s). Destinos en común: {string.Join(", ", compatibleTargets)}";
        BtnConvertSelection.IsEnabled = _selectedFiles.Count > 0 && compatibleTargets.Count > 0;

        var previous = CmbTargetFormat.SelectedItem as string;
        CmbTargetFormat.Items.Clear();
        foreach (var fmt in compatibleTargets)
        {
            CmbTargetFormat.Items.Add(fmt);
        }

        if (previous is not null && compatibleTargets.Contains(previous))
        {
            CmbTargetFormat.SelectedItem = previous;
        }
        else if (_selectedFiles.Count == 0)
        {
            CmbTargetFormat.SelectedItem = "webp";
        }

        if (CmbRuleTargetFormat.Items.Count == 0)
        {
            foreach (var fmt in GetAllCanonicalFormats())
            {
                CmbRuleTargetFormat.Items.Add(fmt);
                CmbPresetTarget.Items.Add(fmt);
                CmbAddFavoriteFormat.Items.Add(fmt);
            }
            CmbRuleTargetFormat.SelectedItem = "webp";
            CmbPresetTarget.SelectedItem = "webp";
            CmbAddFavoriteFormat.SelectedItem = "webp";
        }
    }

    private static IReadOnlyList<string> GetAllCanonicalFormats()
    {
        return FormatMatrix.Formats.Keys
            .Where(k => k != "jpg")
            .OrderBy(k => FormatMatrix.Formats[k].Category)
            .ThenBy(k => k)
            .ToList();
    }

    private void OnJobUpdated(ConversionJob job)
    {
        Dispatcher.InvokeAsync(() =>
        {
            RefreshJobList();
            if (job.Status is JobStatus.Completed or JobStatus.Failed && _settings.ShowNotifications && !IsVisible)
            {
                _tray.ShowBalloonTip(3000, "FileShift",
                    job.Status == JobStatus.Completed ? $"Procesado: {job.FileName}" : job.ErrorMessage ?? job.FileName,
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            if (job.Status == JobStatus.Completed)
            {
                if (job.OutputPath is { } path && job.Options.TriggeredBy.StartsWith("Automation:", StringComparison.Ordinal) &&
                    _settings.Automations.Any(rule => rule.CopyConvertedFile && job.Options.TriggeredBy == $"Automation:{rule.Id}"))
                {
                    var paths = new System.Collections.Specialized.StringCollection { path };
                    paths.AddRange(job.AdditionalOutputPaths.ToArray());
                    try { Clipboard.SetFileDropList(paths); }
                    catch (System.Runtime.InteropServices.ExternalException)
                    {
                        TxtStatusBar.Text = "Se completó la conversión. El portapapeles está ocupado.";
                        return;
                    }
                }
                TxtStatusBar.Text = $"Completado: {Path.GetFileName(job.InputPath)} -> {Path.GetFileName(job.OutputPath)} (SHA-256 original verificado)";
                if (_settings.RevealInExplorerWhenDone && !string.IsNullOrWhiteSpace(job.OutputPath))
                {
                    RevealFileInExplorer(job.OutputPath);
                }
            }
            else if (job.Status == JobStatus.Failed)
            {
                TxtStatusBar.Text = $"Error en {Path.GetFileName(job.InputPath)}: {job.ErrorMessage}";
            }
        });
    }

    private sealed record JobListItem(ConversionJob Job, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private void RefreshJobList()
    {
        var selectedId = (LstJobs.SelectedItem as JobListItem)?.Job.Id;
        LstJobs.Items.Clear();

        var jobs = _queue.Jobs;
        if (jobs.Count == 0)
        {
            LstJobs.Items.Add("Sin conversiones en cola. Arrastrá archivos aquí, usá '+ Añadir archivos…' o convertí desde el menú contextual de Windows 11.");
            return;
        }

        foreach (var job in jobs)
        {
            var statusBadge = job.Status switch
            {
                JobStatus.Completed => "[OK]",
                JobStatus.Failed => "[FALLÓ]",
                JobStatus.Cancelled => "[CANCELADO]",
                JobStatus.Validating => "[VALIDANDO]",
                JobStatus.Running => $"[{job.ProgressPercent:0}%]",
                _ => "[EN COLA]"
            };

            var line = $"{statusBadge,-12} {Path.GetFileName(job.InputPath)} -> {job.TargetFormat.ToUpperInvariant()} | Motor: {job.EngineUsed}";
            if (job.Status == JobStatus.Completed && !string.IsNullOrWhiteSpace(job.OutputPath))
            {
                var pagesExtra = job.AdditionalOutputPaths.Count > 0
                    ? $" (+{job.AdditionalOutputPaths.Count} págs adicionales)"
                    : "";
                line += $" -> {job.OutputPath}{pagesExtra} | Original intacto: {(job.OriginalIntactVerified ? "Sí" : "No")}";
            }
            else if (!string.IsNullOrWhiteSpace(job.ErrorMessage))
            {
                line += $" | Error: {job.ErrorMessage}";
            }

            var item = new JobListItem(job, line);
            LstJobs.Items.Add(item);
            if (selectedId == job.Id)
            {
                LstJobs.SelectedItem = item;
            }
        }
    }

    private ConversionOptions BuildCurrentOptions()
    {
        int quality = int.TryParse(TxtQuality.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int q)
            ? Math.Clamp(q, 1, 100)
            : _settings.DefaultQuality;

        int? longestEdge = int.TryParse(TxtLongestEdge.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int px) && px > 0
            ? px
            : null;

        string? pageRange = string.IsNullOrWhiteSpace(TxtPageRange.Text) ? null : TxtPageRange.Text.Trim();
        var preset = CmbPreset.SelectedItem as ConversionPreset;
        if (preset?.TargetFormat != CmbTargetFormat.SelectedItem as string)
            preset = null;
        string? customOut = string.IsNullOrWhiteSpace(_settings.CustomOutputDirectory) ? null : _settings.CustomOutputDirectory;

        return new ConversionOptions(
            Quality: quality,
            LongestEdgePx: longestEdge,
            AudioBitrateKbps: preset?.AudioBitrateKbps,
            StripMetadata: ChkStripMetadata.IsChecked == true,
            ConflictPolicy: _settings.DefaultConflictPolicy,
            CustomOutputDirectory: customOut,
            PageRange: pageRange,
            PresetName: preset?.Name,
            TriggeredBy: "WPF"
        );
    }

    private void SelectFiles(IReadOnlyList<string> files)
    {
        _selectedFiles = files;
        InitializeTargetFormatPickers();
        if (CmbPreset.SelectedItem is ConversionPreset preset &&
            !CmbTargetFormat.Items.Contains(preset.TargetFormat))
            CmbPreset.SelectedIndex = 0;
    }

    private void BtnConvertSelection_Click(object sender, RoutedEventArgs e)
    {
        if (CmbTargetFormat.SelectedItem is not string target)
        {
            TxtStatusBar.Text = "Elegí un formato de destino compatible.";
            return;
        }

        var options = BuildCurrentOptions();
        foreach (var file in _selectedFiles)
            _queue.Enqueue(file, target, options);

        TxtStatusBar.Text = $"Encolados {_selectedFiles.Count} archivo(s) hacia .{target.ToUpperInvariant()}";
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
            SelectFiles(dlg.FileNames);
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
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            SelectFiles(files);
        }
    }

    private void BtnCancelJob_Click(object sender, RoutedEventArgs e)
    {
        if (LstJobs.SelectedItem is JobListItem item)
        {
            _queue.CancelJob(item.Job.Id);
            TxtStatusBar.Text = $"Cancelación solicitada para {Path.GetFileName(item.Job.InputPath)}";
        }
    }

    private void BtnRetryJob_Click(object sender, RoutedEventArgs e)
    {
        if (LstJobs.SelectedItem is JobListItem item && File.Exists(item.Job.InputPath))
        {
            _queue.Enqueue(item.Job.InputPath, item.Job.TargetFormat, item.Job.Options);
            TxtStatusBar.Text = $"Reintentando conversión de {Path.GetFileName(item.Job.InputPath)}";
        }
    }

    private void BtnOpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (LstJobs.SelectedItem is JobListItem item &&
            !string.IsNullOrWhiteSpace(item.Job.OutputPath) &&
            File.Exists(item.Job.OutputPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.Job.OutputPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                TxtStatusBar.Text = $"No se pudo abrir el archivo: {ex.Message}";
            }
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (LstJobs.SelectedItem is JobListItem item)
        {
            var path = item.Job.OutputPath ?? item.Job.InputPath;
            RevealFileInExplorer(path);
        }
    }

    private static void RevealFileInExplorer(string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = false
                };
                psi.ArgumentList.Add($"/select,{filePath}");
                Process.Start(psi);
            }
        }
        catch
        {
            // Ignore explorer launch errors in non-desktop sessions
        }
    }

    private void BtnClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        _queue.ClearCompletedJobs();
        RefreshJobList();
        TxtStatusBar.Text = "Historial de trabajos completados limpiado.";
    }

    // --- PRESETS ---
    private void PopulatePresetsUi()
    {
        CmbPreset.Items.Clear();
        CmbPreset.Items.Add("(Sin preset — manual)");
        LstPresets.Items.Clear();
        CmbRulePreset.Items.Clear();
        CmbRulePreset.Items.Add("(Sin preset)");

        foreach (var preset in _settings.Presets)
        {
            CmbPreset.Items.Add(preset);
            CmbRulePreset.Items.Add(preset);
            var edgeInfo = preset.LongestEdgePx.HasValue ? $"{preset.LongestEdgePx.Value}px" : "Original";
            LstPresets.Items.Add($"{preset.Name} [{preset.Id}] -> {preset.TargetFormat.ToUpperInvariant()} | Calidad: {preset.Quality} | Escala: {edgeInfo}");
        }
        CmbPreset.SelectedIndex = 0;
        CmbRulePreset.SelectedIndex = 0;
    }

    private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbPreset.SelectedItem is ConversionPreset preset)
        {
            if (!CmbTargetFormat.Items.Contains(preset.TargetFormat))
            {
                CmbPreset.SelectedIndex = 0;
                TxtStatusBar.Text = "El formato del preset no es compatible con la selección.";
                return;
            }
            CmbTargetFormat.SelectedItem = preset.TargetFormat;
            TxtQuality.Text = preset.Quality.ToString(CultureInfo.InvariantCulture);
            TxtLongestEdge.Text = preset.LongestEdgePx?.ToString(CultureInfo.InvariantCulture) ?? "";
            ChkStripMetadata.IsChecked = preset.StripMetadata;
        }
    }

    private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtPresetName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        var id = name.ToLowerInvariant().Replace(' ', '-');
        var target = CmbPresetTarget.SelectedItem as string ?? "webp";
        int quality = int.TryParse(TxtPresetQuality.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int q)
            ? Math.Clamp(q, 1, 100)
            : 85;
        int? edge = int.TryParse(TxtPresetEdge.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int px) && px > 0
            ? px
            : null;

        _settings.Presets.RemoveAll(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        _settings.Presets.Add(new ConversionPreset(id, name, target, quality, edge, null, ChkPresetStripMeta.IsChecked == true));

        UserSettingsStore.Save(_settings);
        PopulatePresetsUi();
        TxtStatusBar.Text = $"Preset '{name}' guardado en settings.json.";
    }

    private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
    {
        int idx = LstPresets.SelectedIndex;
        if (idx >= 0 && idx < _settings.Presets.Count)
        {
            var removed = _settings.Presets[idx];
            _settings.Presets.RemoveAt(idx);
            UserSettingsStore.Save(_settings);
            PopulatePresetsUi();
            TxtStatusBar.Text = $"Preset '{removed.Name}' eliminado.";
        }
    }

    // --- AUTOMATIONS ---
    private void PopulateAutomationsUi()
    {
        if (string.IsNullOrWhiteSpace(TxtRuleFolder.Text))
        {
            TxtRuleFolder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }

        LstAutomations.Items.Clear();
        foreach (var rule in _settings.Automations)
        {
            var state = rule.Enabled ? "[ACTIVA]" : "[PAUSADA]";
            var filters = rule.AllowedInputFormats.Count > 0 ? string.Join(",", rule.AllowedInputFormats) : "*";
            LstAutomations.Items.Add($"{state,-10} {rule.Name} | Carpeta: {rule.SourceDirectory} | Filtro: {filters} -> .{rule.TargetFormat.ToUpperInvariant()}");
        }
    }

    private void BtnAddRule_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtRuleName.Text.Trim();
        var folder = TxtRuleFolder.Text.Trim();
        var filterRaw = string.IsNullOrWhiteSpace(TxtRuleFilter.Text) ? "png;jpg" : TxtRuleFilter.Text.Trim();
        var preset = CmbRulePreset.SelectedItem as ConversionPreset;
        var target = preset?.TargetFormat ?? CmbRuleTargetFormat.SelectedItem as string ?? "webp";

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(folder))
            return;

        var allowed = filterRaw
            .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.TrimStart('*', '.').ToLowerInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s) && s != "*")
            .ToList();

        var rule = new AutomationRule(
            Id: $"rule-{Guid.NewGuid():N}"[..13],
            Name: name,
            Enabled: true,
            SourceDirectory: folder,
            AllowedInputFormats: allowed,
            TargetFormat: target,
            PresetId: preset?.Id,
            OutputDirectory: _settings.SaveNextToOriginal ? null : _settings.CustomOutputDirectory,
            ConflictPolicy: _settings.DefaultConflictPolicy,
            CopyConvertedFile: ChkRuleCopyClipboard.IsChecked == true,
            LastResult: "Vigilando carpeta activamente");

        _settings.Automations.Add(rule);
        _automationService.SyncRules(_settings.Automations);
        UserSettingsStore.Save(_settings);
        PopulateAutomationsUi();
        TxtStatusBar.Text = $"Regla '{name}' activada y guardada.";
    }

    private void BtnToggleRule_Click(object sender, RoutedEventArgs e)
    {
        int idx = LstAutomations.SelectedIndex;
        if (idx >= 0 && idx < _settings.Automations.Count)
        {
            var current = _settings.Automations[idx];
            var updated = current with { Enabled = !current.Enabled };
            _settings.Automations[idx] = updated;
            _automationService.SyncRules(_settings.Automations);
            UserSettingsStore.Save(_settings);
            PopulateAutomationsUi();
            TxtStatusBar.Text = $"Regla '{updated.Name}' {(updated.Enabled ? "activada" : "pausada")}.";
        }
    }

    private void BtnDeleteRule_Click(object sender, RoutedEventArgs e)
    {
        int idx = LstAutomations.SelectedIndex;
        if (idx >= 0 && idx < _settings.Automations.Count)
        {
            var removed = _settings.Automations[idx];
            _settings.Automations.RemoveAt(idx);
            _automationService.SyncRules(_settings.Automations);
            UserSettingsStore.Save(_settings);
            PopulateAutomationsUi();
            TxtStatusBar.Text = $"Regla '{removed.Name}' eliminada.";
        }
    }

    // --- GENERAL SETTINGS & DOCTOR ---
    private void PopulateGeneralSettingsUi()
    {
        CmbConflictPolicy.SelectedIndex = _settings.DefaultConflictPolicy switch
        {
            ConflictPolicy.Overwrite => 1,
            ConflictPolicy.Skip => 2,
            _ => 0
        };
        TxtCustomOutputDir.Text = _settings.CustomOutputDirectory ?? "";
        CmbConcurrency.SelectedIndex = _settings.MaxConcurrency switch
        {
            <= 2 => 0,
            <= 4 => 1,
            _ => 2
        };
        ChkShowNotifications.IsChecked = _settings.ShowNotifications;
        ChkRevealInExplorer.IsChecked = _settings.RevealInExplorerWhenDone;
        ChkKeepInBackground.IsChecked = _settings.KeepRunningInBackground;
    }

    private void BtnSaveGeneralSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.DefaultConflictPolicy = CmbConflictPolicy.SelectedIndex switch
        {
            1 => ConflictPolicy.Overwrite,
            2 => ConflictPolicy.Skip,
            _ => ConflictPolicy.AutoRename
        };

        _settings.CustomOutputDirectory = TxtCustomOutputDir.Text.Trim();
        _settings.SaveNextToOriginal = string.IsNullOrWhiteSpace(_settings.CustomOutputDirectory);

        if (CmbConcurrency.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int threads))
        {
            _settings.MaxConcurrency = threads;
            _queue.UpdateMaxConcurrency(threads);
        }

        _settings.ShowNotifications = ChkShowNotifications.IsChecked == true;
        _settings.RevealInExplorerWhenDone = ChkRevealInExplorer.IsChecked == true;
        _settings.KeepRunningInBackground = ChkKeepInBackground.IsChecked == true;

        UserSettingsStore.Save(_settings);
        TxtStatusBar.Text = $"Configuración general guardada en {UserSettingsStore.GetSettingsFilePath()}";
    }

    private void BtnRunDoctor_Click(object sender, RoutedEventArgs e)
    {
        _ = RunDoctorDiagnosticsAsync();
    }

    private async Task RunDoctorDiagnosticsAsync()
    {
        var results = await DoctorService.RunDiagnosticsAsync();
        DoctorService.UpdateShellConversionMatrix(results);
        await Dispatcher.InvokeAsync(() =>
        {
            LstDoctorResults.Items.Clear();
            foreach (var diag in results)
            {
                var badge = diag.Available ? "[OK]" : "[NO DETECTADO]";
                LstDoctorResults.Items.Add($"{badge,-16} {diag.Name,-34} | {diag.Version} ({diag.LicenseNotice})");
            }
        });
    }

    // --- WINDOWS 11 CONTEXT MENU FAVORITES ---
    private void PopulateMenuSettingsUi()
    {
        ChkEnableContextMenu.IsChecked = _settings.ContextMenuEnabled;
        CmbMenuCategory.SelectedIndex = 0;
        RefreshMenuCategoryList();
    }

    private string GetSelectedMenuCategoryKey()
    {
        if (CmbMenuCategory.SelectedItem is ComboBoxItem item && item.Content is string text)
        {
            return text.ToLowerInvariant();
        }
        return "image";
    }

    private void CmbMenuCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshMenuCategoryList();
    }

    private void RefreshMenuCategoryList()
    {
        if (LstMenuFavorites is null) return;
        var catKey = GetSelectedMenuCategoryKey();
        LstMenuFavorites.Items.Clear();

        if (_settings.MenuFavorites.TryGetValue(catKey, out var favs))
        {
            for (int i = 0; i < favs.Count; i++)
            {
                LstMenuFavorites.Items.Add($"{i + 1}. {favs[i].ToUpperInvariant()} (.{favs[i]})");
            }
        }
    }

    private void BtnAddFavoriteFormat_Click(object sender, RoutedEventArgs e)
    {
        var catKey = GetSelectedMenuCategoryKey();
        var fmt = (CmbAddFavoriteFormat.SelectedItem as string)?.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(fmt)) return;

        if (!_settings.MenuFavorites.TryGetValue(catKey, out var list))
        {
            list = new List<string>();
            _settings.MenuFavorites[catKey] = list;
        }

        if (!list.Contains(fmt, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(fmt);
            RefreshMenuCategoryList();
        }
    }

    private void BtnMoveFavUp_Click(object sender, RoutedEventArgs e)
    {
        var catKey = GetSelectedMenuCategoryKey();
        int idx = LstMenuFavorites.SelectedIndex;
        if (idx > 0 && _settings.MenuFavorites.TryGetValue(catKey, out var list) && idx < list.Count)
        {
            (list[idx - 1], list[idx]) = (list[idx], list[idx - 1]);
            RefreshMenuCategoryList();
            LstMenuFavorites.SelectedIndex = idx - 1;
        }
    }

    private void BtnMoveFavDown_Click(object sender, RoutedEventArgs e)
    {
        var catKey = GetSelectedMenuCategoryKey();
        int idx = LstMenuFavorites.SelectedIndex;
        if (_settings.MenuFavorites.TryGetValue(catKey, out var list) && idx >= 0 && idx < list.Count - 1)
        {
            (list[idx + 1], list[idx]) = (list[idx], list[idx + 1]);
            RefreshMenuCategoryList();
            LstMenuFavorites.SelectedIndex = idx + 1;
        }
    }

    private void BtnRemoveFav_Click(object sender, RoutedEventArgs e)
    {
        var catKey = GetSelectedMenuCategoryKey();
        int idx = LstMenuFavorites.SelectedIndex;
        if (_settings.MenuFavorites.TryGetValue(catKey, out var list) && idx >= 0 && idx < list.Count)
        {
            list.RemoveAt(idx);
            RefreshMenuCategoryList();
        }
    }

    private void BtnSaveMenuSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.ContextMenuEnabled = ChkEnableContextMenu.IsChecked == true;
        UserSettingsStore.Save(_settings);
        TxtStatusBar.Text = $"Menú contextual sincronizado en {UserSettingsStore.GetMenuCacheFilePath()}";
    }

    // --- NAVIGATION SWITCHING ---
    private void ShowOnlyView(UIElement activeView, Button activeButton, string title, string subtitle)
    {
        ViewActivity.Visibility = activeView == ViewActivity ? Visibility.Visible : Visibility.Collapsed;
        ViewAutomations.Visibility = activeView == ViewAutomations ? Visibility.Visible : Visibility.Collapsed;
        ViewSettingsGeneral.Visibility = activeView == ViewSettingsGeneral ? Visibility.Visible : Visibility.Collapsed;
        ViewSettingsMenu.Visibility = activeView == ViewSettingsMenu ? Visibility.Visible : Visibility.Collapsed;
        ViewSettingsPresets.Visibility = activeView == ViewSettingsPresets ? Visibility.Visible : Visibility.Collapsed;

        var navButtons = new[] { BtnNavActivity, BtnNavAutomations, BtnNavGeneral, BtnNavMenu, BtnNavPresets };
        foreach (var btn in navButtons)
        {
            if (btn == activeButton)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                btn.Foreground = Brushes.White;
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
        }

        TxtHeaderTitle.Text = title;
        TxtHeaderSub.Text = subtitle;
    }

    private void BtnNavActivity_Click(object sender, RoutedEventArgs e) =>
        ShowOnlyView(ViewActivity, BtnNavActivity, "Actividad y Cola de Conversión", "Arrastrá archivos aquí o convertí con clic derecho desde el Explorador de Windows 11");

    private void BtnNavAutomations_Click(object sender, RoutedEventArgs e) =>
        ShowOnlyView(ViewAutomations, BtnNavAutomations, "Automatizaciones de Carpetas", "Vigilancia activa con FileSystemWatcher, verificación de estabilidad y prevención de bucles");

    private void BtnNavGeneral_Click(object sender, RoutedEventArgs e) =>
        ShowOnlyView(ViewSettingsGeneral, BtnNavGeneral, "Configuración General y Motores", "Política no destructiva de colisiones, concurrencia y estado de motores locales");

    private void BtnNavMenu_Click(object sender, RoutedEventArgs e) =>
        ShowOnlyView(ViewSettingsMenu, BtnNavMenu, "Menú Contextual de Windows 11", "Configurá formatos favoritos por tipo de archivo para la extensión IExplorerCommand");

    private void BtnNavPresets_Click(object sender, RoutedEventArgs e) =>
        ShowOnlyView(ViewSettingsPresets, BtnNavPresets, "Presets de Conversión", "Perfiles reutilizables desde el menú contextual, la aplicación de escritorio y el CLI");

    private void RestoreWindow()
    {
        _tray.Visible = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_shutdownComplete || e.Cancel) return;

        e.Cancel = true;
        if (_settings.KeepRunningInBackground && !_exitRequested)
        {
            _tray.Visible = true;
            Hide();
            return;
        }

        if (!IsEnabled) return;
        IsEnabled = false;
        _automationService.Dispose();
        _queue.JobUpdated -= OnJobUpdated;
        await _queue.DisposeAsync();
        _shutdownComplete = true;
        await Dispatcher.InvokeAsync(Close);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        base.OnClosed(e);
    }
}
