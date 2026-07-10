using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiCardSync.Core.Models;

namespace MultiCardSync.App.ViewModels;

/// <summary>
/// "⬇️ Tortib olingan" jadvalidagi bitta satr. Har satrda "📤 Yozish" tugmasi bor —
/// bosilganda faqat SHU yozuvni sheetга yozadi (yozish mantig'i asosiy VM'da).
/// </summary>
public partial class PreviewRowViewModel : ObservableObject
{
    private readonly Func<PreviewRowViewModel, Task> _write;

    public PreviewRowViewModel(PreviewTransaction tx, Func<PreviewRowViewModel, Task> write)
    {
        Transaction = tx;
        _write = write;

        var t = tx.Tx;
        Date = (t.Date ?? "").Replace("T", " ");
        Direction = tx.Type == "credit" ? "Приход" : "Расход";
        Amount = (t.AmountValue ?? 0m).ToString("#,##0", CultureInfo.InvariantCulture);
        Note = (t.Note ?? "").Trim();
    }

    /// <summary>Yoziladigan xom tranzaksiya (asosiy VM shundan foydalanadi).</summary>
    public PreviewTransaction Transaction { get; }

    public string Date { get; }
    public string Direction { get; }
    public string Amount { get; }
    public string Note { get; }

    /// <summary>Fetch paytida yangi (sheetда yo'q) edimi.</summary>
    public bool IsNew => Transaction.IsNew;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    [NotifyPropertyChangedFor(nameof(CanWrite))]
    [NotifyCanExecuteChangedFor(nameof(WriteRowCommand))]
    private bool _isWritten;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    [NotifyPropertyChangedFor(nameof(CanWrite))]
    [NotifyCanExecuteChangedFor(nameof(WriteRowCommand))]
    private bool _isBusy;

    /// <summary>Tugma faolmi: faqat yangi, hali yozilmagan va band bo'lmagan qatorlar.</summary>
    public bool CanWrite => IsNew && !IsWritten && !IsBusy;

    public string State =>
        IsBusy ? "⏳ Yozilmoqda…" :
        !IsNew ? "• Sinxronlangan" :
        IsWritten ? "✅ Yozildi" :
        "🆕 Yangi";

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private Task WriteRow() => _write(this);
}
