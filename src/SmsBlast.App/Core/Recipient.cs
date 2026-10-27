using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmsBlast.App.Core;

public sealed class Recipient : INotifyPropertyChanged
{
    private bool _selected = true;
    private string _status = string.Empty;
    private string _rowState = "ok";
    private string _rendered = string.Empty;
    private string _detail = string.Empty;

    /// <summary>Numéro de ligne dans le fichier source, en-tête comprise.</summary>
    public int LineNumber { get; init; }

    public string[] Values { get; init; } = System.Array.Empty<string>();

    public string RawPhone { get; init; } = string.Empty;
    public string E164 { get; init; } = string.Empty;
    public string Pretty { get; init; } = string.Empty;
    public PhoneKind Kind { get; init; } = PhoneKind.Invalid;
    public string KindText { get; init; } = string.Empty;

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public string Rendered
    {
        get => _rendered;
        set => Set(ref _rendered, value);
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string Detail
    {
        get => _detail;
        set => Set(ref _detail, value);
    }

    /// <summary>ok | invalid | landline | sending | sent | failed — pilote la couleur de la ligne.</summary>
    public string RowState
    {
        get => _rowState;
        set => Set(ref _rowState, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
