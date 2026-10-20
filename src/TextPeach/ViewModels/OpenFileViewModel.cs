using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TextPeach.Controls;
using TextPeach.Services;
using TextPeach.Views;

namespace TextPeach.ViewModels;

/// <summary>One file open for reading inside a project. Read-only unless the user presses "Editar".</summary>
public sealed partial class OpenFileViewModel : TabViewModel, IDisposable
{
    public string FullPath { get; }
    public string RelPath { get; }
    public LanguageInfo Language { get; }
    public string? Scope { get; }
    public TextDocument Document { get; } = new();

    public string FileName => Path.GetFileName(FullPath);
    public string Directory => RelPath.LastIndexOf('/') is var i and > 0 ? RelPath[..(i + 1)] : "";
    public string LanguageName => Language.Name;
    public Avalonia.Media.IBrush LanguageBrush => Palette.FromHex(Language.Color);

    [ObservableProperty] private FileKind _kind = FileKind.Text;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _encodingName = "";
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string _imageInfo = "";
    [ObservableProperty] private string _noteText = "";
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _changedOnDisk;
    [ObservableProperty] private string _infoText = "";
    [ObservableProperty] private string _lineEnding = "";
    [ObservableProperty] private string _indentation = "";
    [ObservableProperty] private IReadOnlyList<MetricRow> _metrics = Array.Empty<MetricRow>();
    [ObservableProperty] private IReadOnlyList<OutlineRowVM> _outline = Array.Empty<OutlineRowVM>();
    [ObservableProperty] private IReadOnlyList<MarkerRowVM> _markers = Array.Empty<MarkerRowVM>();
    [ObservableProperty] private FileAnalysis _analysis = FileAnalysis.Empty;
    [ObservableProperty] private bool _isRendered;
    /// <summary>Bumped on every (re)load so the rendered view knows to rebuild.</summary>
    [ObservableProperty] private int _contentVersion;

    /// <summary>How this file can be shown besides plain code.</summary>
    public RenderKind RenderMode { get; }
    private bool _renderDecided;

    public bool IsText => Kind == FileKind.Text && !IsLoading;
    public bool CanRender => RenderMode != RenderKind.None && IsText;
    public bool IsCodeVisible => IsText && !IsRendered;
    public bool IsRenderedVisible => IsText && IsRendered;
    public bool IsImage => Kind == FileKind.Image;
    public bool IsNote => Kind is FileKind.Binary or FileKind.TooLarge or FileKind.Error;
    public bool CanEdit => Kind == FileKind.Text && !IsEditing && !IsLoading;
    public string OutlineHeader => $"Estructura ({Outline.Count})";
    public string MarkersHeader => $"Marcadores ({Markers.Count})";

    // Where the reader was, so switching tabs comes back to the same spot.
    public int CaretOffset { get; set; }
    public double VerticalOffset { get; set; }
    public double HorizontalOffset { get; set; }
    public bool ViewStateSaved { get; set; }
    public double RenderedOffset { get; set; }

    /// <summary>Asks the view to scroll to (line, column) and select <c>length</c> characters.</summary>
    public event Action<int, int, int>? RevealRequested;
    /// <summary>The document was replaced from disk; the view should refresh highlighting/folding.</summary>
    public event Action? Reloaded;

    private Encoding _encoding = new UTF8Encoding(false);
    private DateTime _modified;
    private long _size;
    private bool _loadingText;
    private DateTime _ignoreDiskUntil;
    private CancellationTokenSource? _analysisCts;
    private (int Line, int Col, int Len)? _pendingReveal;

    public OpenFileViewModel(string root, string relPath)
    {
        RelPath = relPath;
        FullPath = Path.Combine(root, relPath.Replace('/', Path.DirectorySeparatorChar));
        Language = Languages.For(FullPath);
        Scope = Highlighting.Instance.ScopeFor(FullPath);
        RenderMode = Path.GetExtension(FullPath).ToLowerInvariant() switch
        {
            ".md" or ".markdown" or ".mdx" => RenderKind.Markdown,
            ".txt" or ".text" => RenderKind.PlainText,
            _ => RenderKind.None,
        };
        Title = FileName;
        ToolTip = relPath;
        Document.TextChanged += (_, _) =>
        {
            if (_loadingText || !IsEditing) return;
            IsDirty = true;
            ScheduleAnalysis(TimeSpan.FromMilliseconds(800));
        };
        _ = LoadAsync(keepView: false);
    }

    partial void OnKindChanged(FileKind value) => NotifyKind();
    partial void OnIsLoadingChanged(bool value) => NotifyKind();
    partial void OnIsEditingChanged(bool value) { OnPropertyChanged(nameof(CanEdit)); UpdateTitle(); }
    partial void OnIsRenderedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCodeVisible));
        OnPropertyChanged(nameof(IsRenderedVisible));
    }
    partial void OnIsDirtyChanged(bool value) => UpdateTitle();
    partial void OnOutlineChanged(IReadOnlyList<OutlineRowVM> value) => OnPropertyChanged(nameof(OutlineHeader));
    partial void OnMarkersChanged(IReadOnlyList<MarkerRowVM> value) => OnPropertyChanged(nameof(MarkersHeader));

    private void NotifyKind()
    {
        OnPropertyChanged(nameof(IsText));
        OnPropertyChanged(nameof(IsImage));
        OnPropertyChanged(nameof(IsNote));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRender));
        OnPropertyChanged(nameof(IsCodeVisible));
        OnPropertyChanged(nameof(IsRenderedVisible));
    }

    private void UpdateTitle() => Title = (IsDirty ? "● " : IsEditing ? "✎ " : "") + FileName;

    // ------------------------------------------------------------ loading

    public async Task LoadAsync(bool keepView)
    {
        IsLoading = Document.TextLength == 0;
        var loaded = await Task.Run(() => FileLoader.Load(FullPath));
        _modified = loaded.Modified;
        _size = loaded.Size;
        Kind = loaded.Kind;
        ChangedOnDisk = false;
        switch (loaded.Kind)
        {
            case FileKind.Text:
                _encoding = loaded.Encoding;
                EncodingName = loaded.EncodingName;
                _loadingText = true;
                try
                {
                    if (keepView && Document.TextLength > 0)
                    {
                        // Replace in place so the caret and scroll position survive the reload.
                        if (Document.Text != loaded.Text) Document.Replace(0, Document.TextLength, loaded.Text);
                    }
                    else Document.Text = loaded.Text;
                    Document.UndoStack.ClearAll();
                }
                finally { _loadingText = false; }
                IsDirty = false;
                if (!_renderDecided)
                {
                    // Documents open "compiled"; huge ones stay as code so the first paint is instant.
                    _renderDecided = true;
                    IsRendered = RenderMode != RenderKind.None && _size <= 2 * 1024 * 1024;
                }
                ContentVersion++;
                break;
            case FileKind.Image:
                try
                {
                    Image = await Task.Run(() => new Bitmap(FullPath));
                    ImageInfo = $"{Image.PixelSize.Width} × {Image.PixelSize.Height} px · {FileLoader.FormatSize(_size)}";
                }
                catch (Exception ex)
                {
                    Kind = FileKind.Error;
                    NoteText = "No se pudo mostrar la imagen: " + ex.Message;
                }
                break;
            case FileKind.Binary:
                NoteText = $"Archivo binario no mostrado ({FileLoader.FormatSize(_size)})";
                break;
            case FileKind.TooLarge:
                NoteText = $"Archivo demasiado grande para mostrarlo ({FileLoader.FormatSize(_size)}, límite {FileLoader.FormatSize(FileLoader.MaxTextBytes)})";
                break;
            case FileKind.Error:
                NoteText = "No se pudo leer el archivo: " + loaded.Error;
                break;
        }
        IsLoading = false;
        InfoText = $"{FileLoader.FormatSize(_size)} · modificado {_modified:yyyy-MM-dd HH:mm}";
        if (Kind == FileKind.Text)
        {
            await AnalyzeNowAsync();
            Reloaded?.Invoke();
            if (_pendingReveal is { } r) { _pendingReveal = null; RevealRequested?.Invoke(r.Line, r.Col, r.Len); }
        }
        else
        {
            Metrics = new[]
            {
                new MetricRow { Label = "Tipo", Value = Kind switch { FileKind.Image => "Imagen", FileKind.Binary => "Binario", FileKind.TooLarge => "Texto (muy grande)", _ => "Error" } },
                new MetricRow { Label = "Tamaño", Value = FileLoader.FormatSize(_size) },
                new MetricRow { Label = "Modificado", Value = _modified == default ? "—" : _modified.ToString("yyyy-MM-dd HH:mm") },
            };
        }
    }

    /// <summary>Called by the project watcher when this file changed on disk.</summary>
    public async Task OnDiskChangedAsync()
    {
        if (DateTime.UtcNow < _ignoreDiskUntil) return;
        if (!File.Exists(FullPath))
        {
            NoteText = "El archivo ya no existe en el disco.";
            ChangedOnDisk = true;
            return;
        }
        if (IsEditing) { ChangedOnDisk = true; return; }
        await LoadAsync(keepView: true);
    }

    public void Reveal(int line, int col = 1, int len = 0)
    {
        if (IsLoading) _pendingReveal = (line, col, len);
        else RevealRequested?.Invoke(line, col, len);
    }

    // ------------------------------------------------------------ analysis

    private void ScheduleAnalysis(TimeSpan delay)
    {
        _analysisCts?.Cancel();
        var cts = _analysisCts = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Task.Delay(delay, cts.Token); } catch (OperationCanceledException) { return; }
            await AnalyzeNowAsync();
        });
    }

    private async Task AnalyzeNowAsync()
    {
        var text = Document.Text;
        var lang = Language;
        var a = await Task.Run(() => FileAnalyzer.Analyze(text, lang));
        Analysis = a;
        LineEnding = a.LineEnding;
        Indentation = a.Indent;
        Outline = a.Outline.Select(o => new OutlineRowVM { Item = o }).ToList();
        Markers = a.Markers.Select(m => new MarkerRowVM { Tag = m.Tag, Text = m.Text, Line = m.Line }).ToList();
        Metrics = BuildMetrics(a);
    }

    private List<MetricRow> BuildMetrics(FileAnalysis a)
    {
        var warn = Palette.Amber;
        var rows = new List<MetricRow>
        {
            new() { Label = "Líneas", Value = $"{a.Lines:N0}" },
        };
        if (!Language.IsProse)
        {
            double ratio = a.Code + a.Comment > 0 ? 100.0 * a.Comment / (a.Code + a.Comment) : 0;
            rows.Add(new() { Label = "Código", Value = $"{a.Code:N0}" });
            rows.Add(new() { Label = "Comentarios", Value = $"{a.Comment:N0}  ({ratio:0}%)", Hint = "Aproximado: líneas que empiezan con un comentario" });
        }
        rows.Add(new() { Label = "En blanco", Value = $"{a.Blank:N0}" });
        if (!Language.IsProse && a.Branches > 0)
            rows.Add(new() { Label = "Decisiones", Value = $"{a.Branches:N0}", Hint = "Aproximado: if/for/while/case/catch/&&/||/?: (indicador de complejidad)", ValueBrush = a.Branches > 150 ? warn : Palette.Text });
        if (a.MaxDepth > 0)
            rows.Add(new() { Label = "Anidación máx.", Value = a.MaxDepth.ToString(), Hint = "Profundidad máxima de llaves { }", ValueBrush = a.MaxDepth > 6 ? warn : Palette.Text });
        if (a.Outline.Count > 0)
            rows.Add(new() { Label = "Símbolos", Value = $"{a.Outline.Count:N0}" });
        rows.Add(new() { Label = "Línea más larga", Value = $"{a.LongestLine:N0} car. (L{a.LongestLineNo})", ValueBrush = a.LongestLine > 160 ? warn : Palette.Text });
        rows.Add(new() { Label = "Caracteres", Value = $"{a.Chars:N0}" });
        rows.Add(new() { Label = "Tamaño", Value = FileLoader.FormatSize(_size) });
        rows.Add(new() { Label = "Indentación", Value = a.Indent, ValueBrush = a.Indent.StartsWith("Mixta") ? warn : Palette.Text });
        rows.Add(new() { Label = "Fin de línea", Value = a.LineEnding, ValueBrush = a.LineEnding.StartsWith("Mixto") ? warn : Palette.Text });
        rows.Add(new() { Label = "Codificación", Value = EncodingName });
        rows.Add(new() { Label = "Espacios al final", Value = a.TrailingWhitespace ? "Sí" : "No", ValueBrush = a.TrailingWhitespace ? warn : Palette.Text });
        rows.Add(new() { Label = "Modificado", Value = _modified.ToString("yyyy-MM-dd HH:mm") });
        return rows;
    }

    // ------------------------------------------------------------ editing

    [RelayCommand]
    private void Edit()
    {
        if (!CanEdit) return;
        IsRendered = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void ShowRendered()
    {
        if (CanRender && !IsEditing) IsRendered = true;
    }

    [RelayCommand]
    private void ShowCode() => IsRendered = false;

    [RelayCommand] private Task Save() => SaveAsync();

    public async Task<bool> SaveAsync()
    {
        if (!IsEditing) return true;
        try
        {
            _ignoreDiskUntil = DateTime.UtcNow.AddSeconds(1.5);
            var text = Document.Text;
            await Task.Run(() => File.WriteAllText(FullPath, text, _encoding));
            var info = new FileInfo(FullPath);
            _modified = info.LastWriteTime;
            _size = info.Length;
            IsDirty = false;
            ChangedOnDisk = false;
            InfoText = $"{FileLoader.FormatSize(_size)} · guardado {_modified:HH:mm:ss}";
            await AnalyzeNowAsync();
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.Message("No se pudo guardar", ex.Message);
            return false;
        }
    }

    /// <summary>Leaves edit mode. Unsaved changes are confirmed and then discarded (reloaded from disk).</summary>
    [RelayCommand] private Task FinishEditing() => FinishEditingAsync();

    public async Task<bool> FinishEditingAsync()
    {
        if (!IsEditing) return true;
        if (IsDirty)
        {
            var choice = await Dialogs.Choose("Cambios sin guardar",
                $"{FileName} tiene cambios sin guardar.", "Guardar", "Descartar");
            if (choice == 0) { if (!await SaveAsync()) return false; }
            else if (choice == 1) await LoadAsync(keepView: true);
            else return false;
        }
        else if (ChangedOnDisk) await LoadAsync(keepView: true);
        IsEditing = false;
        IsDirty = false;
        return true;
    }

    [RelayCommand]
    private async Task ReloadFromDiskAsync()
    {
        if (IsDirty && !await Dialogs.Confirm("Recargar", "Se perderán los cambios sin guardar. ¿Recargar desde el disco?", "Recargar"))
            return;
        await LoadAsync(keepView: true);
    }

    public void Dispose()
    {
        _analysisCts?.Cancel();
        Image?.Dispose();
    }
}
