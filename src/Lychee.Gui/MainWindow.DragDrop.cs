using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Lychee.Gui;

public sealed partial class MainWindow
{
    bool dropAccepted;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? dragLeft;

    bool DragArrived(List<string> paths)
    {
        dragLeft?.Stop();
        if (DropZone.IsShown) return dropAccepted;
        int scans = paths.Count(IsScan);
        dropAccepted = scans > 0;
        DropZone.Show(scans, paths.Count - scans);
        return dropAccepted;
    }

    // Leave then enter fire between elements: only a leave with no drag events after counts
    void EndDragSoon()
    {
        if (dragLeft is null)
        {
            dragLeft = DispatcherQueue.CreateTimer();
            dragLeft.Interval = TimeSpan.FromMilliseconds(60);
            dragLeft.IsRepeating = false;
            dragLeft.Tick += (_, _) =>
            {
                dragLeft.Stop();
                DropZone.Hide();
            };
        }
        dragLeft.Start();
    }

    async void OnDragEnter(object sender, DragEventArgs e)
    {
        dragLeft?.Stop();
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        var items = await e.DataView.GetStorageItemsAsync();
        e.AcceptedOperation = DragArrived([.. items.OfType<StorageFile>().Select(f => f.Path)]) ? DataPackageOperation.Copy : DataPackageOperation.None;
        deferral.Complete();
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        dragLeft?.Stop();
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = dropAccepted ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = dropAccepted ? "Open" : "Not an image";
    }

    void OnDragLeave(object sender, DragEventArgs e) => EndDragSoon();

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var paths = (await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>().Select(f => f.Path).ToList();
        dragLeft?.Stop();
        DropZone.Hide();
        OpenPaired(paths);
    }
}
