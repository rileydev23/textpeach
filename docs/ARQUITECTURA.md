# Arquitectura de TextPeach

<!-- peach-hijo: ARQUITECTURA v1.0 -->
Visor y analizador de código de solo lectura (el diseño está en [DISENO.md](DISENO.md)).
**Patrón:** MVVM con CommunityToolkit.Mvvm. Las vistas enlazan; la lógica pura vive en `Services/` y está testeada.
Los servicios marcados como *comunes* (`PeachApps`, `SingleInstance`, `Loc`, `GitStatus`…) son iguales en
GitPeach y RestPeach; si los cambias, avísalo en el *pull request*.

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
| `Controls/` | `Palette`, `Folding` (plegado por llaves, indentación, etiquetas XML o títulos Markdown), `DocumentRenderer` (Markdig → controles nativos para la vista renderizada), `ChangeMarkers` (`GitChangeMargin` junto a los números de línea y `OverviewRuler` junto al scroll), `DiffView` (diff en línea o lado a lado) y `SyntaxColoring` (instala TextMate y repinta mientras tokeniza). |

`Services/` también tiene `LineDiff` (diff por líneas de Myers, marcas por línea y pares para lado a lado, puro y
testeado) e `IgnoreRules` (carpetas ignoradas del proyecto más las de siempre).

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

## Cambios contra el último commit

Cuando el estado de git llega, `ProjectViewModel` le pasa a cada `OpenFileViewModel` su ruta en el repositorio y
su estado (`SetGitBase`). El archivo lee la versión de `HEAD` con `GitStatus.ReadHeadTextAsync`
(`git show HEAD:<ruta>`, con límite de tiempo y tamaño) y calcula en segundo plano `LineDiff.Compute` y
`LineDiff.Marks`. Publica `Marks`, `DiffRows`, `HasChanges` y `DiffStats` y avisa con `MarksChanged`; la vista
pinta el margen y la regla y, si el diff está abierto, lo reconstruye conservando la posición. Un archivo sin
seguimiento es todo "agregado"; uno sin cambios según git no lee nada.

## Navegación: vista previa, historial y seguir cambios

- `OpenFile(rel, preview: true)` reutiliza la única pestaña de vista previa (si no tiene cambios); `Pin` la fija.
  Un clic en el árbol abre en vista previa, el doble clic fija.
- Cada salto (cambio de pestaña, `RevealInCurrent`, ir a línea) guarda la ubicación anterior en una pila de
  atrás; `GoBack`/`GoForward` viajan entre las dos pilas sin registrar el viaje.
- El *watcher* del proyecto alimenta `Recent` (`RecentChangeVM`: creado, modificado o borrado, con la hora). Con
  `FollowChanges` activo, cada cambio abre el archivo en vista previa (con un *debounce* de 150 ms) y salta a la
  primera línea que cambió, salvo que el archivo actual se esté editando.
- Las carpetas ignoradas y la vista elegida por archivo (renderizada o código) se guardan por proyecto en
  `state.json` (`ProjectPrefs`).

## Desarrollo

```
dev.cmd                     compila Debug y ejecuta
dotnet test TextPeach.sln   tests del analizador, decodificación, ranking, plegado, git e idiomas
build.cmd                   dist\TextPeach.exe portable (single-file, self-contained)
```

En Debug, la variable `TEXTPEACH_DEBUG_ACTIONS="open:ruta/rel;tree:ruta/rel;search:texto;edit"` ejecuta acciones al iniciar
(útil para capturas sin simular teclas).
