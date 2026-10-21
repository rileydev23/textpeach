# Arquitectura de TextPeach

Visor y analizador de código de solo lectura, de la familia Peach (ver [DISENO.md](DISENO.md)).
**Patrón:** MVVM con CommunityToolkit.Mvvm. Las vistas enlazan; la lógica pura vive en `Services/` y está testeada.

```
Views (XAML + code-behind del editor)
  │ bindings
ViewModels ── MainWindowViewModel ─┬─ HomeViewModel (lista de proyectos)
  │                                └─ ProjectViewModel (una pestaña por proyecto)
  │                                     ├─ SummaryViewModel (pestaña "Resumen")
  │                                     └─ OpenFileViewModel (una pestaña por archivo)
  └──► Services: ProjectStore · ProjectGrouping · ProjectScanner · FileLoader · FileAnalyzer · Languages
                 · Highlighting · SingleInstance · PeachApps · GitStatus · GitPeachLocator · Loc/Strings
```

| Carpeta | Responsabilidad |
|---|---|
| `Services/` | `ProjectStore` (state.json), `ProjectScanner` (lista de archivos, tipo de proyecto), `FileLoader` (codificación, binarios, límites), `FileAnalyzer` (métricas, outline y marcadores por regex), `Languages` (extensión → lenguaje, color, comentarios), `Highlighting` (gramáticas TextMate + tema `peach-dark.json`), `PeachApps` (carpeta compartida y `location.json`), `GitStatus` (estado de git de solo lectura), `GitPeachLocator` (dónde está GitPeach), `Loc` + `Strings` (textos en inglés y español). |
| `ViewModels/` | Estado de pantalla. `ViewerSettings` (fuente, ajuste de línea, espacios) es compartido por todas las pestañas. |
| `Views/` | `ProjectView` hospeda **un único** `TextEditor` (AvaloniaEdit) y le cambia el `TextDocument` según la pestaña; guarda/restaura cursor y scroll por archivo. Avalonia recicla la vista entre pestañas de proyecto, así que todo se reengancha en `DataContextChanged`. |
| `Controls/` | `Palette`, `Folding` (plegado por llaves, indentación, etiquetas XML o títulos Markdown) y `DocumentRenderer` (Markdig → controles nativos para la vista renderizada). |

## Lista de proyectos y grupos

`HomeViewModel` construye filas (`GroupHeaderRow` / `ProjectRow`) a partir de `ProjectGrouping.Build`, que es
puro y está testeado. "Escanear carpeta" (`ImportFolderAsGroupAsync`) usa `ProjectScanner.FindProjects` y
guarda lo encontrado en un grupo con el nombre de la carpeta. Los menús contextuales son listas de
`MenuAction` (con submenús) que dibuja `MenuPresenter`.

## Instancia única

`Program.Main` llama a `SingleInstance.TryBecomePrimary`: la primera ejecución toma un mutex por usuario y
escucha una *named pipe*; las siguientes envían sus rutas (absolutas) y terminan. `App` recibe esas rutas,
llama a `MainWindowViewModel.OpenFromArgs` y trae la ventana al frente.

## Estado de git (solo lectura)

`ProjectViewModel.ScheduleGitRefresh` (con *debounce* de 600 ms) llama a `GitStatus.ReadAsync`, que busca la
carpeta con `.git` subiendo desde el proyecto y ejecuta
`git --no-optional-locks status --porcelain=v1 -z --branch --untracked-files=normal --ignored=traditional`
(máx. 15 s). `GitStatus.Parse` (puro, testeado) produce un `GitSnapshot`: rama, ahead/behind, estado por
archivo relativo al proyecto, carpetas enteras (sin seguimiento/ignoradas) y el cambio más fuerte dentro de
cada carpeta. El snapshot se guarda en `ProjectViewModel.Git` (`GitDecorations`), que comparten todos los
`FileNode` (también los que se cargan después al expandir), las filas del filtro y las pestañas
(`TabViewModel.Change`). Se refresca al abrir el proyecto, con F5 y ante cambios del *watcher*, incluidos los de
`.git` (que no refrescan el árbol). La rama de la barra de estado ejecuta `OpenInGitPeachCommand`:
`GitPeachLocator.Open(repo)` o, si no está, el diálogo con el enlace a su repositorio. Ningún comando de git
escribe nada.

## Idiomas

`Loc.Init(state.Language)` corre en `App` antes de crear cualquier vista: elige "en"/"es" según lo guardado o
el idioma de Windows (`GetUserDefaultUILanguage`, porque la app usa globalización invariante). Las vistas leen
sus textos al construirse (`{l:L clave}` es una `MarkupExtension` que devuelve `Loc.T(clave)`), así que
cambiar de idioma se aplica al reiniciar. Todas las cadenas están en `Services/Strings.cs`; `LanguageTests`
verifica que cada clave usada exista en ambos idiomas con los mismos marcadores.

## Flujo de un archivo

1. `ProjectViewModel.OpenFile(rel)` crea o reutiliza un `OpenFileViewModel`.
2. `FileLoader.Load` decide el tipo (texto, imagen, binario, muy grande) y decodifica (BOM, UTF-8 estricto, Latin-1).
3. `FileAnalyzer.Analyze` corre en segundo plano y llena métricas, estructura y marcadores.
4. La vista instala TextMate y el plegado sobre el documento; el editor queda en solo lectura.
5. "Editar" habilita la escritura; "Guardar" escribe con la misma codificación; un `FileSystemWatcher` recarga
   el archivo si cambia en disco (o avisa si se está editando).

## Desarrollo

```
dev.cmd                     compila Debug y ejecuta
dotnet test TextPeach.sln   tests del analizador, decodificación, ranking, plegado, git e idiomas
build.cmd                   dist\TextPeach.exe portable (single-file, self-contained)
```

En Debug, la variable `TEXTPEACH_DEBUG_ACTIONS="open:ruta/rel;tree:ruta/rel;search:texto;edit"` ejecuta acciones al iniciar
(útil para capturas sin simular teclas).
