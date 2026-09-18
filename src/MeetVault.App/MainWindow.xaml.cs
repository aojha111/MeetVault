using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MeetVault.Core;
using MeetVault.Infrastructure;

namespace MeetVault.App;

/// <summary>
/// Main window: date-grouped meetings list, detail pane with the in-app audio brief
/// player, global search, and live processing progress. Talks to the shared AppBootstrapper
/// so the app and CLI behave identically.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppBootstrapper _app;
    private readonly DispatcherTimer _playerTimer;
    private readonly DispatcherTimer _queueTimer;
    private Meeting? _selectedMeeting;
    private bool _seeking;

    public MainWindow()
    {
        InitializeComponent();
        _app = App.Bootstrapper!;

        _playerTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _playerTimer.Tick += PlayerTimer_Tick;

        _queueTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _queueTimer.Tick += (_, _) => RefreshQueueText();
        _queueTimer.Start();

        _app.Queue.Progress += Queue_Progress;
        RefreshMeetings();
        UpdateStatus("Ready.");

        // Reflect the saved theme in the dropdown (indices: 0 label, 1 system, 2 light, 3 dark).
        ThemeCombo.SelectedIndex = _app.Settings.Theme.ToLowerInvariant() switch
        {
            "light" => 2,
            "dark" => 3,
            "system" => 1,
            _ => 3,
        };
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_app is null || ThemeCombo.SelectedIndex < 1) return;
        _app.Settings.Theme = ThemeCombo.SelectedIndex switch
        {
            1 => "system",
            2 => "light",
            _ => "dark",
        };
        _app.SaveSettings();
        ThemeManager.Apply(_app.Settings.Theme);
    }

    protected override void OnClosed(EventArgs e)
    {
        _app.Queue.Progress -= Queue_Progress;
        StopPlayback();
        base.OnClosed(e);
    }

    // ────────────────────────────────────────────────────────── meetings list

    private async void RefreshMeetings()
    {
        try
        {
            var meetings = await _app.Repository.GetAllAsync();
            var filtered = meetings.Where(DateGroupNode.RangeFilter(RangeKey(), DateOnly.FromDateTime(DateTime.Today))).ToList();
            MeetingCountText.Text = filtered.Count == 1 ? "1 meeting" : $"{filtered.Count} meetings";
            MeetingsTree.ItemsSource = DateGroupNode.BuildTree(filtered).Children;
        }
        catch (Exception ex)
        {
            UpdateStatus("Failed to load meetings: " + ex.Message);
        }
    }

    private string RangeKey() => RangeCombo.SelectedIndex switch
    {
        1 => "today",
        2 => "yesterday",
        3 => "week",
        4 => "month",
        _ => "all",
    };

    private void RangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshMeetings();

    private async void MeetingsTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object?> e)
    {
        if (e.NewValue is not DateGroupNode node || node.Kind != "meeting" || node.Meeting is null)
            return;
        await OpenMeetingAsync(node.Meeting);
    }

    private void MeetingsTree_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Delete && _selectedMeeting is not null)
        {
            DeleteMeeting(_selectedMeeting);
        }
    }

    private void DeleteMeeting(Meeting meeting)
    {
        var result = MessageBox.Show(
            this,
            $"""Delete "{meeting.Title}" and its stored analysis, transcript and audio brief? The original recording file is not deleted.""",
            "Delete meeting",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        StopPlaybackIfMeeting(meeting.Id);
        _ = Task.Run(async () =>
        {
            await _app.Meetings.DeleteAsync(meeting.Id);
            await Dispatcher.InvokeAsync(RefreshMeetings);
        });
    }

    // ────────────────────────────────────────────────────────── add / import

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add meeting recording",
            Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.wmv;*.m4v;*.mpg;*.mpeg;*.ts;*.flv;*.mp3;*.wav;*.m4a;*.aac;*.ogg;*.opus;*.flac;*.wma|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        ImportFile(dialog.FileName);
    }

    private void ImportFile(string path)
    {
        UpdateStatus($"Importing {Path.GetFileName(path)}…");
        _ = Task.Run(async () =>
        {
            try
            {
                var (meeting, error) = await _app.Meetings.ImportAsync(path);
                if (meeting is null)
                {
                    await Dispatcher.InvokeAsync(() => MessageBox.Show(this, error, "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning));
                    return;
                }

                if (_app.Settings.AutoProcessOnImport)
                    _app.Queue.Enqueue(meeting.Id);

                await Dispatcher.InvokeAsync(() =>
                {
                    RefreshMeetings();
                    UpdateStatus($"Imported \"{meeting.Title}\".");
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        });
    }

    // ────────────────────────────────────────────────────────── processing progress

    private void Queue_Progress(object? sender, MeetingProgress p)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_selectedMeeting?.Id == p.MeetingId)
            {
                DetailStatus.Text = p.Status is ProcessingStatus.Failed ? "Failed" : p.Stage;
                if (p.Percent >= 0)
                {
                    StatusProgress.IsIndeterminate = false;
                    StatusProgress.Value = p.Percent;
                    StatusProgress.Visibility = Visibility.Visible;
                }
                if (p.Status is ProcessingStatus.Failed && p.Error is not null)
                {
                    ShowError(p.Error);
                }
            }
            if (p.Status is ProcessingStatus.Completed or ProcessingStatus.Failed or ProcessingStatus.Canceled)
            {
                UpdateStatus($"{p.Stage}: {p.Message}");
                if (p.Status == ProcessingStatus.Completed) HideError();
                RefreshMeetings();
                if (_selectedMeeting?.Id == p.MeetingId && p.Status == ProcessingStatus.Completed)
                {
                    _ = LoadDetailAsync(_selectedMeeting);
                }
            }
            RefreshQueueText();
        });
    }

    private void RefreshQueueText()
    {
        var pending = _app.Queue.PendingCount;
        QueueText.Text = pending > 0 ? $"{pending} queued" : string.Empty;
    }

    // ────────────────────────────────────────────────────────── detail

    private async Task OpenMeetingAsync(Meeting meeting)
    {
        _selectedMeeting = meeting;
        HideError();
        await LoadDetailAsync(meeting);
    }

    private async Task LoadDetailAsync(Meeting meeting)
    {
        try
        {
            DetailTitle.Text = meeting.Title;
            DetailDate.Text = meeting.MeetingDate.ToString("MMMM d, yyyy") +
                (meeting.StartTime is { } st ? $" · {st:HH:mm}" : "");
            DetailDuration.Text = meeting.DurationSeconds > 0
                ? TimeSpan.FromSeconds(meeting.DurationSeconds).ToString(@"h\h\ mm\m")
                : string.Empty;
            DetailStatus.Text = meeting.Status switch
            {
                ProcessingStatus.Imported => "Imported — ready to process",
                ProcessingStatus.Queued => "Queued",
                ProcessingStatus.Processing => "Processing…",
                ProcessingStatus.Completed => "Processed",
                ProcessingStatus.Failed => "Failed",
                ProcessingStatus.Canceled => "Canceled",
                _ => string.Empty,
            };

            var analysis = await _app.Repository.GetAnalysisAsync(meeting.Id);
            var transcript = await _app.Repository.GetTranscriptAsync(meeting.Id);
            var related = await _app.Search.RelatedMeetingsAsync(meeting.Id);

            SummaryText.Text = string.IsNullOrWhiteSpace(analysis?.Summary)
                ? "No analysis yet. The meeting is processed in the background once models are installed."
                : analysis!.Summary;
            SetList(AgendaList, AgendaHeader, analysis?.Agenda);
            SetList(DiscussionList, DiscussionHeader, analysis?.KeyDiscussionPoints);
            SetList(DecisionsList, DecisionsHeader, analysis?.Decisions.Select(d => d.DecisionText).ToList());
            SetList(ActionsList, ActionsHeader, analysis?.ActionItems.Select(a =>
                a.Task + (string.IsNullOrWhiteSpace(a.Owner) ? "" : $" — {a.Owner}") +
                (string.IsNullOrWhiteSpace(a.Deadline) ? "" : $" (due {a.Deadline})")).ToList());
            SetList(RisksList, RisksHeader, analysis?.Risks);
            SetList(QuestionsList, QuestionsHeader, analysis?.OpenQuestions.Select(q => q.Question).ToList());

            // Topics chips
            TopicsPanel.Children.Clear();
            TopicsHeader.Visibility = analysis?.Topics is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
            foreach (var topic in analysis?.Topics ?? [])
            {
                TopicsPanel.Children.Add(new Border
                {
                    Style = (Style)FindResource("Chip"),
                    Child = new TextBlock { Text = topic },
                });
            }

            // Related meetings
            RelatedPanel.Children.Clear();
            RelatedHeader.Visibility = related.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var rel in related)
            {
                var link = new Button
                {
                    Content = $"{rel.MeetingDate:yyyy-MM-dd}  {rel.Title}",
                    Style = (Style)FindResource("SmallButton"),
                    Margin = new Thickness(0, 0, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                var id = rel.Id;
                link.Click += async (_, _) =>
                {
                    var target = await _app.Repository.GetAsync(id);
                    if (target is not null) await OpenMeetingAsync(target);
                };
                RelatedPanel.Children.Add(link);
            }

            // Transcript
            var hasTranscript = transcript.Count > 0;
            TranscriptList.ItemsSource = hasTranscript ? transcript : null;
            TranscriptHeader.Visibility = hasTranscript ? Visibility.Visible : Visibility.Collapsed;

            DetailPanel.Visibility = Visibility.Visible;
            LoadPlayerFor(meeting);

            if (meeting.Status == ProcessingStatus.Failed && !string.IsNullOrEmpty(meeting.LastError))
                ShowError(meeting.LastError);
            else
                HideError();
        }
        catch (Exception ex)
        {
            UpdateStatus("Failed to load meeting: " + ex.Message);
        }
    }

    private static void SetList(ItemsControl list, TextBlock header, List<string>? items)
    {
        items ??= [];
        list.ItemsSource = items;
        header.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ────────────────────────────────────────────────────────── audio player

    private System.Windows.Media.MediaPlayer? _player;
    private string? _playerFile;

    private void LoadPlayerFor(Meeting meeting)
    {
        var path = meeting.AudioBriefPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            PlayerHintText.Text = meeting.CompletedStage >= PipelineStage.BriefScriptGenerated
                ? "Audio brief generation did not complete. Use Retry or Regenerate."
                : "No audio brief generated yet.";
            PlayButton.IsEnabled = false;
            return;
        }

        if (_playerFile == path && _player is not null)
        {
            PlayerHintText.Text = Path.GetFileName(path);
            PlayButton.IsEnabled = true;
            return;
        }

        StopPlayback();
        _player = new System.Windows.Media.MediaPlayer();
        _player.MediaOpened += (_, _) =>
        {
            SeekSlider.Maximum = _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan.TotalSeconds : 100;
            CurrentTimeText.Text = $"00:00 / {FormatTime(_player.NaturalDuration)}";
        };
        _player.MediaEnded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            StopPlayback();
        });
        _player.MediaFailed += (_, args) => Dispatcher.BeginInvoke(() =>
        {
            PlayerHintText.Text = "Playback failed: " + args.ErrorException.Message;
            PlayButton.IsEnabled = false;
        });
        _player.Volume = VolumeSlider.Value;
        _player.SpeedRatio = SpeedFromCombo();
        _player.Open(new Uri(path));
        _playerFile = path;
        PlayerHintText.Text = Path.GetFileName(path);
        PlayButton.IsEnabled = true;
    }

    private double SpeedFromCombo() => SpeedCombo.SelectedIndex switch
    {
        0 => 0.75,
        2 => 1.25,
        3 => 1.5,
        4 => 2.0,
        _ => 1.0,
    };

    private static string FormatTime(Duration duration) =>
        duration.HasTimeSpan ? duration.TimeSpan.ToString(@"mm\:ss") : "00:00";

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        if (PlayButton.Content.ToString() == "Play")
        {
            _player.Play();
            _playerTimer.Start();
            PlayButton.Content = "Pause";
        }
        else
        {
            _player.Pause();
            _playerTimer.Stop();
            PlayButton.Content = "Play";
        }
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        _player.Position = TimeSpan.Zero;
        if (PlayButton.Content.ToString() != "Play")
        {
            _player.Play();
            _playerTimer.Start();
            PlayButton.Content = "Pause";
        }
    }

    private void StopPlayback()
    {
        _playerTimer.Stop();
        _player?.Close();
        _player = null;
        _playerFile = null;
        SeekSlider.Value = 0;
        PlayButton.Content = "Play";
    }

    private void StopPlaybackIfMeeting(long meetingId)
    {
        if (_selectedMeeting?.Id == meetingId) StopPlayback();
    }

    private void PlayerTimer_Tick(object? sender, EventArgs e)
    {
        if (_player is null || _seeking) return;
        if (_player.NaturalDuration.HasTimeSpan)
        {
            var total = _player.NaturalDuration.TimeSpan.TotalSeconds;
            if (total > 0)
                SeekSlider.Value = _player.Position.TotalSeconds / total * SeekSlider.Maximum;
            CurrentTimeText.Text = $"{FormatTime(_player.Position)} / {FormatTime(_player.NaturalDuration)}";
        }
    }

    private void SeekSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) => _seeking = true;

    private void SeekSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        ApplySeek();
        _seeking = false;
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_seeking && _player is not null && _player.NaturalDuration.HasTimeSpan)
        {
            CurrentTimeText.Text = $"{FormatTime(TimeSpan.FromSeconds(e.NewValue / SeekSlider.Maximum * _player.NaturalDuration.TimeSpan.TotalSeconds))} / {FormatTime(_player.NaturalDuration)}";
        }
    }

    private void ApplySeek()
    {
        if (_player is null || !_player.NaturalDuration.HasTimeSpan) return;
        var total = _player.NaturalDuration.TimeSpan.TotalSeconds;
        if (total > 0)
            _player.Position = TimeSpan.FromSeconds(SeekSlider.Value / SeekSlider.Maximum * total);
    }

    private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_player is not null) _player.SpeedRatio = SpeedFromCombo();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_player is not null) _player.Volume = e.NewValue;
    }

    // ────────────────────────────────────────────────────────── search

    private async void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        var query = SearchBox.Text.Trim();
        if (query.Length == 0) return;

        try
        {
            var response = await _app.Search.SearchAsync(query);
            if (response.Hits.Count == 0)
            {
                UpdateStatus($"No results for \"{query}\".");
                return;
            }

            var first = response.Hits[0];
            var meeting = await _app.Repository.GetAsync(first.MeetingId);
            if (meeting is not null)
            {
                await OpenMeetingAsync(meeting);
                UpdateStatus($"{response.Hits.Count} result(s) for \"{query}\" — showing first match: {meeting.Title}");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("Search failed: " + ex.Message);
        }
    }

    // ────────────────────────────────────────────────────────── actions

    private void RegenerateBriefButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMeeting is null) return;
        _app.Queue.Enqueue(_selectedMeeting.Id, force: true);
        UpdateStatus("Regenerating meeting brief…");
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMeeting is null) return;
        HideError();
        _app.Queue.Enqueue(_selectedMeeting.Id, force: false);
        UpdateStatus("Retrying processing…");
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMeeting is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export meeting",
            Filter = "Markdown minutes|*.md|JSON analysis|*.json|Plain-text transcript|*.txt",
            FileName = _selectedMeeting.Title.Replace(' ', '-'),
        };
        if (dialog.ShowDialog(this) != true) return;

        var meeting = _selectedMeeting;
        var target = dialog.FileName;
        _ = Task.Run(async () =>
        {
            try
            {
                var analysis = await _app.Repository.GetAnalysisAsync(meeting.Id);
                var transcript = await _app.Repository.GetTranscriptAsync(meeting.Id);
                var content = Path.GetExtension(target).ToLowerInvariant() switch
                {
                    ".json" => analysis is null ? "{}" : JsonUtil.ToPrettyJson(analysis),
                    ".txt" => ExportService.TranscriptToPlainText(transcript),
                    _ => ExportService.ToMarkdown(meeting, analysis, transcript),
                };
                await File.WriteAllTextAsync(target, content);
                await Dispatcher.InvokeAsync(() => UpdateStatus("Exported to " + target));
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        });
    }

    private void ModelManagerButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ModelManagerWindow(_app) { Owner = this };
        window.ShowDialog();
        RefreshMeetings();
    }

    // ────────────────────────────────────────────────────────── status helpers

    private void UpdateStatus(string message) => StatusText.Text = message;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorPanel.Visibility = Visibility.Collapsed;
}
