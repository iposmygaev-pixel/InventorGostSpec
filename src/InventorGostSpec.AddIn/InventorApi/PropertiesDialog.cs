using System.Runtime.InteropServices;
using System.Text;
using InventorGostSpec.AddIn.Infrastructure;
using InventorGostSpec.UI.ViewModels;

namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>
/// Окно «iProperties» Inventor для компонента сборки — как ПКМ → iProperties на компоненте в браузере:
/// вхождение выбирается в сборке и выполняется команда Inventor «iProperties».
/// </summary>
/// <remarks>
/// Окно спецификации модальное, и Inventor может отказать в синхронном запуске команды (E_FAIL). Тогда команда
/// запускается асинхронно, а открытие и закрытие её окна отслеживаются по окнам потока Inventor.
/// </remarks>
internal sealed class PropertiesDialog
{
    /// <summary>Внутреннее имя команды Inventor «iProperties».</summary>
    private const string IPropertiesCommand = "AppiPropertiesCmd";

    private readonly Inventor.Application _application;
    private readonly AssemblyDocument _assembly;
    private readonly IntPtr _owner;
    private HashSet<IntPtr> _before = new();
    private bool _ownerWasDisabled;
    private bool _seen;
    private bool _done;

    public PropertiesDialog(Inventor.Application application, AssemblyDocument assembly, IntPtr owner)
    {
        _application = application;
        _assembly = assembly;
        _owner = owner;
    }

    /// <summary>Выбирает вхождение и запускает команду. Ошибка — исключение с текстом для пользователя.</summary>
    public InventorDialogState Open(BOMRow row)
    {
        var occurrence = FirstOccurrence(row) ?? throw new InvalidOperationException("У компонента нет вхождения в сборке.");

        Inventor.ControlDefinition command;
        try
        {
            command = _application.CommandManager.ControlDefinitions[IPropertiesCommand];
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Команда Inventor «iProperties» ({IPropertiesCommand}) не найдена: {ex.Message}", ex);
        }

        var selectSet = _assembly.SelectSet;
        try
        {
            selectSet.Clear();
            selectSet.Select(occurrence);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(
                $"Не удалось выбрать компонент в сборке: {ex.Message}\nСборка должна быть активным документом (не в режиме редактирования подсборки).", ex);
        }

        _ownerWasDisabled = _owner != IntPtr.Zero && !IsWindowEnabled(_owner);
        _before = CaptionWindows();
        try
        {
            command.Execute2(true);

            // Команда могла не дождаться закрытия своего окна — тогда оно ещё открыто; нового окна нет — уже закрыто.
            if (State() == InventorDialogState.Open)
            {
                return InventorDialogState.Open;
            }

            _done = true;
            return InventorDialogState.Closed;
        }
        catch (COMException ex)
        {
            Log.Info("iProperties", $"Синхронный запуск не удался ({ex.Message}) — запуск асинхронно.");
        }

        try
        {
            command.Execute2(false);
        }
        catch (COMException ex)
        {
            ClearSelection();
            throw new InvalidOperationException($"Inventor не запустил команду «iProperties»: {ex.Message}", ex);
        }

        _done = false;
        _seen = false;
        return InventorDialogState.Starting;
    }

    /// <summary>
    /// Открыто ли окно команды: новое окно с заголовком в потоке Inventor (не окно WPF аддина).
    /// После синхронного запуска без нового окна — закрыто.
    /// </summary>
    public InventorDialogState State()
    {
        if (_done)
        {
            return InventorDialogState.Closed;
        }

        if (CaptionWindows().Any(h => !_before.Contains(h)))
        {
            _seen = true;
            return InventorDialogState.Open;
        }

        return _seen ? InventorDialogState.Closed : InventorDialogState.Starting;
    }

    /// <summary>Снимает выбор в сборке; главное окно Inventor снова отключено, пока открыто окно спецификации.</summary>
    public void End()
    {
        _done = true;
        ClearSelection();
        if (_ownerWasDisabled && IsWindowEnabled(_owner))
        {
            EnableWindow(_owner, false);
        }
    }

    private void ClearSelection()
    {
        try
        {
            _assembly.SelectSet.Clear();
        }
        catch (COMException ex)
        {
            Log.Error("iProperties: SelectSet.Clear", ex);
        }
    }

    /// <summary>Первое вхождение строки BOM (у фантомных подсборок — вхождение внутри них).</summary>
    private static ComponentOccurrence? FirstOccurrence(BOMRow row)
    {
        foreach (ComponentOccurrence occurrence in row.ComponentOccurrences)
        {
            return occurrence;
        }

        return null;
    }

    /// <summary>Видимые окна с заголовком в потоке Inventor, кроме окон WPF (окно спецификации и его диалоги).</summary>
    private static HashSet<IntPtr> CaptionWindows()
    {
        var result = new HashSet<IntPtr>();
        var className = new StringBuilder(256);
        EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && (GetWindowLong(hwnd, GwlStyle) & WsCaption) == WsCaption)
            {
                className.Clear();
                GetClassName(hwnd, className, className.Capacity);
                if (!className.ToString().StartsWith("HwndWrapper", StringComparison.Ordinal))
                {
                    result.Add(hwnd);
                }
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    private const int GwlStyle = -16;
    private const int WsCaption = 0x00C00000;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enable);
}
