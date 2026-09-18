using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MeetVault.Core;
using MeetVault.Infrastructure;

namespace MeetVault.App;

/// <summary>ViewModel for one pack row in the Model Manager.</summary>
public sealed class ModelPackRow : INotifyPropertyChanged
{
    private readonly double _hardwareRamGb;

    public ModelPackRow(ModelPackState state, double hardwareRamGb)
    {
        State = state;
        _hardwareRamGb = hardwareRamGb;
    }

    public ModelPackState State { get; set; }

    public string DisplayName => State.Pack.DisplayName;
    public string Description => State.Pack.Description;
    public string Group => string.IsNullOrWhiteSpace(State.Pack.Group) ? State.Pack.Kind : State.Pack.Group;
    public bool IsInstalled => State.IsInstalled;

    public string SizeLabel =>
        State.Pack.SizeBytes >= 1024 * 1024
            ? $"{State.Pack.SizeBytes / (1024.0 * 1024):0.#} MB · {State.Pack.Kind} · v{State.Pack.Version}"
            : $"{State.Pack.Kind} · v{State.Pack.Version}";

    public string StateLabel => State.IsInstalled
        ? $"Installed v{State.InstalledVersion} ({State.SizeOnDiskBytes / (1024.0 * 1024):0.#} MB on disk)"
        : "Not installed";

    /// <summary>RAM tier badge, e.g. "CPU · 16 GB RAM"; empty for runtime packs without a tier.</summary>
    public string RamBadge => State.Pack.MinRamGb > 0
        ? $"CPU · {State.Pack.MinRamGb:0} GB RAM"
        : string.Empty;

    public bool NeedsMoreRam => !State.IsInstalled && State.Pack.MinRamGb > _hardwareRamGb;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh(ModelPackState state)
    {
        State = state;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NeedsMoreRam)));
    }
}

public partial class ModelManagerWindow : Window
{
    private readonly AppBootstrapper _app;
    private readonly ObservableCollection<ModelPackRow> _rows = [];
    private readonly Dictionary<string, ProgressBar> _progressBars = [];
    private HardwareInfo _hardware = new();
    private double _hardwareRamGb = 16;

    public ModelManagerWindow(AppBootstrapper app)
    {
        InitializeComponent();
        _app = app;
        OfflineModeCheck.IsChecked = _app.Settings.OfflineMode;
        PacksList.ItemsSource = _rows;
        LoadHardwareBanner();
        LoadCatalog();
    }

    private void LoadHardwareBanner()
    {
        _hardware = _app.Hardware.Detect();
        _hardwareRamGb = _hardware.RamGb;
        HardwareText.Text = "This PC: " + _hardware.Describe() + " — every pack below runs on CPU, no GPU needed.";
        RecommendationText.Text =
            $"Recommended for this machine: {_app.ModelRegistryService.Load().Find(_hardware.RecommendedWhisperPackId)?.DisplayName ?? _hardware.RecommendedWhisperPackId}" +
            $" + {_app.ModelRegistryService.Load().Find(_hardware.RecommendedLlmPackId)?.DisplayName ?? _hardware.RecommendedLlmPackId}";
        RecommendedButton.ToolTip = string.Join("\n", _hardware.RecommendedWhisperPackId, _hardware.RecommendedLlmPackId,
            "whisper-vad-silero", "runtime-ffmpeg", "runtime-whisper-cpu", "runtime-llama-cpu", "runtime-piper-tts", "piper-voice-en-lessac-medium");
    }

    /// <summary>Installs the full CPU set this machine's hardware recommends, sequentially.</summary>
    private async void InstallRecommended_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Settings.OfflineMode)
        {
            MessageBox.Show(this, "Offline Mode is enabled. Disable it to download packs.", "Offline Mode",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ids = new[]
        {
            "runtime-ffmpeg", "runtime-whisper-cpu", "runtime-llama-cpu", "runtime-piper-tts",
            _hardware.RecommendedWhisperPackId, "whisper-vad-silero", _hardware.RecommendedLlmPackId,
            "piper-voice-en-lessac-medium",
        };
        var missing = ids.Where(id => _app.ModelManager.GetPack(id) is not { IsInstalled: true }).ToList();
        if (missing.Count == 0)
        {
            StatusText.Text = "Everything recommended for this machine is already installed.";
            await QueueUnprocessedMeetingsAsync();
            return;
        }

        RecommendedButton.IsEnabled = false;
        try
        {
            for (int i = 0; i < missing.Count; i++)
            {
                var pack = _app.ModelManager.GetPack(missing[i])?.Pack;
                if (pack is null) continue;
                StatusText.Text = $"[{i + 1}/{missing.Count}] Downloading {pack.DisplayName}…";
                var progress = new Progress<double>(p => Dispatcher.BeginInvoke(() =>
                {
                    OverallProgress.Value = p;
                    OverallProgress.Visibility = Visibility.Visible;
                }));
                await _app.ModelManager.InstallAsync(pack.Id, progress);
            }
            StatusText.Text = "Recommended set installed. MeetVault is ready — everything runs locally on CPU.";
            await QueueUnprocessedMeetingsAsync();
            LoadCatalog();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Install failed: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Install failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            OverallProgress.Visibility = Visibility.Collapsed;
            RecommendedButton.IsEnabled = true;
        }
    }

    private void LoadCatalog()
    {
        _rows.Clear();
        foreach (var state in _app.ModelManager.GetCatalog())
        {
            _rows.Add(new ModelPackRow(state, _hardwareRamGb));
        }
        SummaryText.Text = $"{_rows.Count(r => r.IsInstalled)} of {_rows.Count} pack(s) installed. " +
            "Model weights are downloaded separately and never bundled with the app.";
    }

    private async Task QueueUnprocessedMeetingsAsync()
    {
        var meetings = await _app.Repository.GetAllAsync();
        var pending = meetings.Where(m => m.Status is ProcessingStatus.Imported
            or ProcessingStatus.Failed
            or ProcessingStatus.Canceled).ToList();
        foreach (var meeting in pending)
            _app.Queue.Enqueue(meeting.Id);

        if (pending.Count > 0)
            StatusText.Text = $"Installed. Queued {pending.Count} meeting(s) for processing.";
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModelPackRow row) return;
        if (_app.Settings.OfflineMode)
        {
            MessageBox.Show(this, "Offline Mode is enabled. Disable it to download packs.", "Offline Mode", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var button = (Button)sender;
        button.IsEnabled = false;
        var progressBar = FindPackProgressBar(row);
        if (progressBar is not null) progressBar.Visibility = Visibility.Visible;
        StatusText.Text = $"Downloading {row.DisplayName}…";

        try
        {
            var progress = new Progress<double>(p => Dispatcher.BeginInvoke(() =>
            {
                if (progressBar is not null) progressBar.Value = p;
                OverallProgress.Value = p;
                OverallProgress.Visibility = Visibility.Visible;
                StatusText.Text = $"Downloading {row.DisplayName}: {p * 100:F0}%";
            }));
            await _app.ModelManager.InstallAsync(row.State.Pack.Id, progress);
            StatusText.Text = $"Installed {row.DisplayName} v{row.State.Pack.Version}.";
            await QueueUnprocessedMeetingsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Install failed: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Install failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (progressBar is not null) progressBar.Visibility = Visibility.Collapsed;
            OverallProgress.Visibility = Visibility.Collapsed;
            button.IsEnabled = true;
            LoadCatalog();
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModelPackRow row) return;
        var confirm = MessageBox.Show(
            this,
            $"Remove {row.DisplayName}? Model files will be deleted from disk.",
            "Remove pack",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _app.ModelManager.Remove(row.State.Pack.Id);
            StatusText.Text = $"Removed {row.DisplayName}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Remove failed: " + ex.Message;
        }
        LoadCatalog();
    }

    private void UseButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModelPackRow row) return;
        var pack = row.State.Pack;
        switch (pack.KindEnum)
        {
            case ModelKind.WhisperModel:
                _app.Settings.WhisperModelId = pack.Id;
                break;
            case ModelKind.LlmModel:
                _app.Settings.LlmModelId = pack.Id;
                break;
            case ModelKind.TtsVoice:
                _app.Settings.TtsVoice = "piper:" + Path.GetFileNameWithoutExtension(pack.FileName);
                break;
            default:
                StatusText.Text = "Runtime packs are used automatically once installed.";
                return;
        }
        _app.SaveSettings();
        StatusText.Text = $"{pack.DisplayName} is now the active model for its category.";
    }

    private void OfflineModeCheck_Changed(object sender, RoutedEventArgs e)
    {
        _app.Settings.OfflineMode = OfflineModeCheck.IsChecked == true;
        _app.SaveSettings();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private ProgressBar? FindPackProgressBar(ModelPackRow row)
    {
        // Walk visual tree of the row container to find its progress bar.
        if (PacksList.ItemContainerGenerator.ContainerFromItem(row) is not ContentPresenter presenter)
            return null;
        return FindDescendant<ProgressBar>(presenter);
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var deep = FindDescendant<T>(child);
            if (deep is not null) return deep;
        }
        return null;
    }
}
