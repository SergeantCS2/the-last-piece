using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tlp.Core;
using Tlp.Viewer.Core;

namespace Tlp.Viewer;

public sealed class ItemRow : INotifyPropertyChanged
{
    public ItemRow(PackItem item)
    {
        Item = item;
        _shared = new ViewerFileReport { FileName = item.FileName, State = ViewerFileState.Missing };
    }

    private string _status = "";
    private double _percent;
    private bool _ready;
    private ViewerFileReport _shared;

    public PackItem Item { get; }
    public string Name => Item.FileName;
    public string Detail =>
        $"{NightSession.Human(Item.Size)} · " + string.Join(" or ", Item.Sources.Select(s => s.Kind switch
        {
            PackSourceKind.Http => "host's copy",
            PackSourceKind.Magnet => "public torrent",
            PackSourceKind.Torrent => "group torrent",
            _ => s.Kind.ToString(),
        }));

    public string Status { get => _status; set => Set(ref _status, value); }
    public double Percent { get => _percent; set => Set(ref _percent, value); }
    public bool Ready { get => _ready; set => Set(ref _ready, value); }
    /// <summary>What the host's "who's ready" list is told about this file. Changes in 5% steps while downloading.</summary>
    public ViewerFileReport Shared { get => _shared; private set => Set(ref _shared, value); }

    public void Apply(ItemStatus s)
    {
        Status = s.Message;
        Percent = Math.Round(s.Fraction * 100, 1);
        Ready = s.State is FileState.SizeMatches or FileState.Verified;
        var next = StatusReporter.FileReport(s);
        Shared = next with { Percent = next.Percent / 5 * 5 };
    }

    public void Fail(string message)
    {
        Status = message;
        Shared = Shared with { State = ViewerFileState.Failed };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
