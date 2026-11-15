using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Image = DiscBg.Image;

namespace DiscBg.Gui;

public sealed partial class ScanView : UserControl
{
    readonly MainWindow owner;
    Image? scan;
    Task<Image>? removal;
    bool showAfter = true, opening = true, closed;
    IEnumerable<Controls.StageView> Previews => Backdrops.Thumbnails.Prepend(Preview);

    public ScanView(MainWindow owner, string path)
    {
        this.owner = owner;
        Path = path;
        InitializeComponent();
        SaveOverlay.Closed += () =>
        {
            SaveState = SaveState.None;
            SaveButton.Focus(FocusState.Programmatic);
        };
        UpdateState();
    }

    public string Path { get; }

    public SaveState SaveState
    {
        get => saveState;
        private set { saveState = value; UpdateState(); }
    }
    SaveState saveState;

    public bool IsBusy => opening || SaveState == SaveState.Saving || removal is { IsCompleted: false };

    public event Action? StateChanged;

    void UpdateState()
    {
        bool loaded = scan is not null, free = loaded && SaveState != SaveState.Saving;
        BeforeButton.IsEnabled = AfterButton.IsEnabled = loaded;
        (BeforeButton.IsChecked, AfterButton.IsChecked) = (!showAfter, showAfter);
        SaveButton.IsEnabled = free && removal is not { IsFaulted: true };
        bool working = removal is { IsCompleted: false };
        (AfterRing.IsActive, AfterRing.Visibility) = (working, working ? Visibility.Visible : Visibility.Collapsed);
        StateChanged?.Invoke();
    }

    void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        InfoPanel.Visibility = text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (text.Length > 0) FrameworkElementAutomationPeer.FromElement(StatusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    public async Task OpenAsync()
    {
        foreach (var v in Previews) v.ShowWorking();
        SetStatus("Opening…");
        try
        {
            scan = await Task.Run(() => Image.LoadAsync(Path));
        }
        catch (Exception ex)
        {
            foreach (var v in Previews) v.Clear("Could not open this scan");
            SetStatus(ex.Message);
            return;
        }
        finally
        {
            opening = false;
            UpdateState();
        }
        if (closed) return;
        FormatText.Text = $"{scan.Width:N0} × {scan.Height:N0} px · {Dpi(scan)}";
        await Refresh();
    }

    public void Close()
    {
        closed = true;
        scan = null;
    }

    Task<Image> Result()
    {
        if (removal is { } existing) return existing;
        var s = scan!;
        var job = removal = Task.Run(() => Remover.RemoveBackground(s));
        _ = job.ContinueWith(_ => UpdateState(), TaskScheduler.FromCurrentSynchronizationContext());
        return job;
    }

    async Task Refresh()
    {
        if (scan is not { } opened) return;
        if (!showAfter) _ = Preview.ShowAsync(opened);
        var job = Result();
        if (!job.IsCompleted)
        {
            foreach (var v in showAfter ? Previews : Backdrops.Thumbnails) v.ShowWorking();
            SetStatus("Removing the background…");
        }
        UpdateState();
        Image result;
        try
        {
            result = await job;
        }
        catch (Exception ex)
        {
            if (closed) return;
            foreach (var v in showAfter ? Previews : Backdrops.Thumbnails) v.Clear("Could not remove the background");
            SetStatus(ex.Message);
            UpdateState();
            return;
        }
        if (closed) return;
        SetStatus("");
        Preview.FitTo(result);
        await Task.WhenAll((showAfter ? Previews : Backdrops.Thumbnails).Select(v => v.ShowAsync(result)));
    }

    void OnViewToggle(object sender, RoutedEventArgs e) => SetView(ReferenceEquals(sender, AfterButton));

    void SetView(bool after)
    {
        bool changed = after != showAfter;
        showAfter = after;
        if (changed) _ = Refresh();
        else UpdateState();
    }

    internal static string Dpi(Image image) => image.Dpi is double dpi ? $"{dpi:0.##} dpi" : "dpi not recorded";

    public FrameworkElement StageSpace => StageArea;

    void OnStageAreaSizeChanged(object sender, SizeChangedEventArgs e) =>
        Preview.Width = Preview.Height = Math.Min(e.NewSize.Width, e.NewSize.Height);

    async void OnSave(object sender, RoutedEventArgs e)
    {
        if (scan is null || !SaveButton.IsEnabled) return;
        var picker = new FileSavePicker(owner.AppWindow.Id)
        {
            SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(Path) + Settings.Current.SaveSuffix,
            SuggestedFolder = System.IO.Path.GetDirectoryName(Path),
        };
        picker.FileTypeChoices.Add("PNG image", [".png"]);
        if (await picker.PickSaveFileAsync() is not { } file) return;

        SaveState = SaveState.Saving;
        var cancel = SaveOverlay.ShowProgress(file.Path);
        try
        {
            SaveOverlay.Report(removal is { IsCompleted: true } ? "Writing the PNG…" : "Removing the background…", 0);
            var result = await Result();
            cancel.ThrowIfCancellationRequested();
            SaveOverlay.Report("Writing the PNG…", 0);
            await Task.Run(() => result.SavePngAsync(file.Path));
            if (cancel.IsCancellationRequested)
            {
                // Too late to stop the write: take it back
                File.Delete(file.Path);
                throw new OperationCanceledException();
            }
            var facts = new List<(string, string?)>
            {
                ("Size", $"{result.Width:N0} × {result.Height:N0} px"),
            };
            facts.Add(("Resolution", Dpi(result)));
            SaveOverlay.ShowDone(file.Path, [.. facts]);
            SaveState = SaveState.Saved;
        }
        catch (OperationCanceledException)
        {
            SaveOverlay.Close();
        }
        catch (Exception ex)
        {
            SaveOverlay.ShowFailed(ex.Message);
            SaveState = SaveState.Failed;
        }
    }
}

public enum SaveState { None, Saving, Saved, Failed }
