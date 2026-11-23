using System.Runtime.InteropServices;

namespace InventorGostSpec.AddIn.InventorApi;

/// <summary>
/// Окно «iProperties» Inventor для компонента сборки — как ПКМ → iProperties на компоненте в браузере:
/// вхождение выбирается в сборке и выполняется команда Inventor «iProperties».
/// </summary>
internal static class PropertiesDialog
{
    /// <summary>Внутреннее имя команды Inventor «iProperties».</summary>
    private const string IPropertiesCommand = "AppiPropertiesCmd";

    /// <summary>Показывает окно и ждёт его закрытия.</summary>
    /// <param name="owner">Главное окно Inventor (на время окна спецификации оно отключено).</param>
    public static void Show(Inventor.Application application, AssemblyDocument assembly, BOMRow row, IntPtr owner)
    {
        var occurrence = FirstOccurrence(row) ?? throw new InvalidOperationException("У компонента нет вхождения в сборке.");
        var selectSet = assembly.SelectSet;

        // Окно спецификации модальное: главное окно Inventor отключено. Окно iProperties при закрытии
        // включает его снова — тогда в Inventor можно было бы работать при открытом окне спецификации.
        var ownerWasDisabled = owner != IntPtr.Zero && !IsWindowEnabled(owner);
        try
        {
            selectSet.Clear();
            selectSet.Select(occurrence);
            application.CommandManager.ControlDefinitions[IPropertiesCommand].Execute2(true);
        }
        finally
        {
            selectSet.Clear();
            if (ownerWasDisabled)
            {
                EnableWindow(owner, false);
            }
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool enable);
}
