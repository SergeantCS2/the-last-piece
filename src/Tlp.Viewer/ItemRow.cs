using System.ComponentModel;
using System.Runtime.CompilerServices;
using Tlp.Core;
using Tlp.Viewer.Core;

namespace Tlp.Viewer;

public sealed class ItemRow(PackItem item) : INotifyPropertyChanged
{
    private string _status = "";
    private double _percent;
    private bool _ready;

    public PackItem Item { get; } = item;
    public string Name => Item.FileName;
    public string Detail =>
        $"{NightSession.Human(Item.Size)} · " + string.Join(" or ", Item.Sources.Select(s => s.Kind switch
        {
            PackSourceKind.Http => "from host",
            PackSourceKind.Magnet => "torrent",
            _ => s.Kind.ToString(),
        }));

    public string Status { get => _status; set => Set(ref _status, value); }
    public double Percent { get => _percent; set => Set(ref _percent, value); }
    public bool Ready { get => _ready; set => Set(ref _ready, value); }

    public void Apply(ItemStatus s)
    {
        Status = s.Message;
        Percent = Math.Round(s.Fraction * 100, 1);
        Ready = s.State is FileState.SizeMatches or FileState.Verified;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
