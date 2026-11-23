using System.Windows.Forms.Integration;
using InventorGostSpec.UI;
using InventorGostSpec.UI.Views;

namespace InventorGostSpec.AddIn.Infrastructure;

/// <summary>
/// Открытые окна спецификаций. Окно немодальное: на одну сборку — одно окно (повторное нажатие выводит его вперёд);
/// при закрытии сборки окно закрывается, а с несохранёнными изменениями — закрытие сборки отменяется.
/// </summary>
internal sealed class SpecificationWindows : IDisposable
{
    private readonly Inventor.Application _application;
    private readonly List<(Document Document, SpecificationWindow Window)> _open = new();
    private readonly Inventor.ApplicationEventsSink_OnCloseDocumentEventHandler _onCloseDocument;

    public SpecificationWindows(Inventor.Application application)
    {
        _application = application;
        _onCloseDocument = OnCloseDocument;
        _application.ApplicationEvents.OnCloseDocument += _onCloseDocument;
    }

    /// <summary>Окно спецификации этой сборки уже открыто — выводит его вперёд.</summary>
    public bool TryActivate(Document document)
    {
        if (Find(document) is not { } window)
        {
            return false;
        }

        window.BringToFront();
        return true;
    }

    /// <summary>Показывает окно немодально поверх главного окна Inventor.</summary>
    public void Show(Document document, SpecificationWindow window, IntPtr owner)
    {
        // Без этого клавиатура в немодальном окне WPF внутри Inventor работает не полностью (ввод текста, Tab).
        ElementHost.EnableModelessKeyboardInterop(window);
        _open.Add((document, window));
        window.Closed += (_, _) => _open.RemoveAll(x => ReferenceEquals(x.Window, window));
        WindowOwner.Show(window, owner);
    }

    private SpecificationWindow? Find(Document document)
    {
        var file = SafeFileName(document);
        return _open.FirstOrDefault(x =>
            ReferenceEquals(x.Document, document) ||
            (file.Length > 0 && string.Equals(SafeFileName(x.Document), file, StringComparison.OrdinalIgnoreCase))).Window;
    }

    private void OnCloseDocument(
        _Document documentObject,
        string fullDocumentName,
        Inventor.EventTimingEnum beforeOrAfter,
        Inventor.NameValueMap context,
        out Inventor.HandlingCodeEnum handlingCode)
    {
        handlingCode = Inventor.HandlingCodeEnum.kEventNotHandled;
        if (beforeOrAfter != Inventor.EventTimingEnum.kBefore)
        {
            return;
        }

        try
        {
            var open = _open.Where(x =>
                ReferenceEquals(x.Document, documentObject) ||
                (fullDocumentName.Length > 0 && string.Equals(SafeFileName(x.Document), fullDocumentName, StringComparison.OrdinalIgnoreCase)))
                .Select(x => x.Window)
                .ToList();
            foreach (var window in open)
            {
                if (!window.CloseForDocument())
                {
                    handlingCode = Inventor.HandlingCodeEnum.kEventCanceled;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("OnCloseDocument", ex);
        }
    }

    private static string SafeFileName(Document document)
    {
        try
        {
            return document.FullFileName ?? "";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return "";
        }
    }

    public void Dispose()
    {
        try
        {
            _application.ApplicationEvents.OnCloseDocument -= _onCloseDocument;
        }
        catch (Exception ex)
        {
            Log.Error("SpecificationWindows.Dispose", ex);
        }

        foreach (var (_, window) in _open.ToList())
        {
            window.ForceClose();
        }

        _open.Clear();
    }
}
