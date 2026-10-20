namespace TextPeach.ViewModels;

/// <summary>Single source for the keyboard shortcuts shown in the help dialog (F1).</summary>
public static class Shortcuts
{
    public static readonly (string Gesture, string Description)[] Global =
    {
        ("Ctrl+P", "Ir a archivo (en un proyecto) / abrir proyecto"),
        ("Ctrl+Shift+O", "Abrir proyecto (búsqueda rápida)"),
        ("Ctrl+Tab / Ctrl+Shift+Tab", "Pestaña siguiente / anterior"),
        ("Ctrl+1 … Ctrl+9", "Ir a la pestaña N"),
        ("Ctrl+W / Ctrl+F4", "Cerrar archivo (o el proyecto si no hay archivos)"),
        ("F1", "Acerca de y atajos de teclado"),
    };

    public static readonly (string Gesture, string Description)[] Project =
    {
        ("Ctrl+Shift+F", "Buscar en todo el proyecto"),
        ("Ctrl+Shift+E", "Filtrar archivos del árbol"),
        ("Ctrl+F", "Buscar en el archivo actual (F3 / Shift+F3)"),
        ("Ctrl+G", "Ir a línea (línea:columna)"),
        ("Ctrl+PageDown / PageUp", "Archivo siguiente / anterior"),
        ("Ctrl+E", "Activar / terminar edición del archivo"),
        ("Ctrl+S", "Guardar (solo en modo edición)"),
        ("Ctrl + / Ctrl − / Ctrl+0", "Zoom del texto"),
        ("Alt+Z", "Ajuste de línea"),
        ("F5", "Refrescar árbol, archivos y resumen"),
        ("Esc (en el filtro)", "Limpiar el filtro"),
    };

    public const string AboutText = "Visor y analizador de código. Abre carpetas de proyecto para leer sus archivos, ver su estructura y métricas, y buscar en ellos; nada se modifica salvo que actives la edición.";

    public static (string Title, (string Gesture, string Description)[] Items)[] Groups =>
        new[] { ("General", Global), ("Proyecto", Project) };
}
