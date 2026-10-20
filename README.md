<p align="center">
  <img src="src/TextPeach/Assets/logo.png" width="128" alt="TextPeach" />
</p>

<h1 align="center">TextPeach</h1>

<p align="center">
  Visor y analizador de código para Windows, <b>de solo lectura</b> por diseño.<br/>
  Abre carpetas de proyecto, recórrelas, lee el código con colores, mira su estructura y sus métricas, y busca en todo el proyecto, sin miedo a cambiar nada por accidente.
</p>

---

## Índice

- [¿Por qué TextPeach?](#por-qué-textpeach)
- [Características](#características)
- [Requisitos](#requisitos)
- [Instalación](#instalación)
- [Primeros pasos](#primeros-pasos)
- [Cómo funciona](#cómo-funciona)
  - [Proyectos y grupos](#proyectos-y-grupos)
  - [La pestaña de un proyecto](#la-pestaña-de-un-proyecto)
  - [Vista renderizada y vista de código](#vista-renderizada-y-vista-de-código)
  - [Panel de análisis](#panel-de-análisis)
  - [Resumen del proyecto](#resumen-del-proyecto)
  - [Búsqueda](#búsqueda)
  - [Edición (opcional)](#edición-opcional)
  - [Cambios en el disco](#cambios-en-el-disco)
- [Atajos de teclado](#atajos-de-teclado)
- [Línea de comandos e integración](#línea-de-comandos-e-integración)
- [Lenguajes soportados](#lenguajes-soportados)
- [Datos, privacidad y modo portable](#datos-privacidad-y-modo-portable)
- [Límites conocidos](#límites-conocidos)
- [Compilar desde el código](#compilar-desde-el-código)
- [Estructura del repositorio](#estructura-del-repositorio)
- [Contribuir](#contribuir)
- [Licencia y créditos](#licencia-y-créditos)

---

## ¿Por qué TextPeach?

Los editores de código están pensados para **escribir**. Cuando lo que quieres es **revisar** un proyecto
(entender un repositorio nuevo, hacer una revisión, leer documentación, buscar dónde se usa algo) un editor
te deja el cursor listo para teclear, te avisa de cambios sin guardar y llena la pantalla de herramientas
que no necesitas.

TextPeach invierte la prioridad:

- **Todo se abre en solo lectura.** Teclear no cambia nada. Para editar un archivo hay que pulsar un botón
  explícito (**✎ Editar**) y guardar a mano.
- **Lee como un lector.** Markdown y texto se abren *renderizados* (títulos, tablas, listas, código) y el
  fuente queda a un clic.
- **Te cuenta cómo es cada archivo:** líneas de código y comentarios, complejidad aproximada, anidación,
  estructura (clases, métodos, títulos…), TODO/FIXME, codificación, indentación y fin de línea.
- **Trabaja con muchos proyectos a la vez,** en pestañas y organizados en grupos.

Es un único `.exe` portable, sin instalador, sin cuentas y sin conexión a internet.

## Características

| Área | Qué incluye |
|---|---|
| **Proyectos** | Lista persistente de carpetas, favoritos ★, grupos plegables, filtro difuso, escaneo de carpetas (lo encontrado se guarda como un grupo), arrastrar y soltar, varias pestañas abiertas que se restauran al reiniciar. |
| **Explorador** | Árbol de archivos perezoso (ignora `node_modules`, `bin`, `obj`, `.git`, `dist`…), filtro rápido por nombre, "Ir a archivo" (Ctrl+P) con ranking inteligente. |
| **Visor de código** | Resaltado TextMate (las mismas gramáticas que muchos editores modernos), números de línea, plegado de bloques, línea actual, zoom, ajuste de línea, mostrar espacios/tabs/fines de línea, buscar en el archivo (Ctrl+F), ir a línea (Ctrl+G). |
| **Vista renderizada** | Markdown (títulos, listas, tareas, tablas, citas, bloques de código, imágenes locales, enlaces) y texto plano formateado. Conmutador **Renderizada \| Código**. |
| **Análisis por archivo** | Métricas con avisos en ámbar, estructura (outline) navegable, marcadores TODO/FIXME/HACK/BUG/NOTE. |
| **Resumen del proyecto** | Totales de archivos, líneas y tamaño, reparto por lenguaje, archivos más extensos y todos los marcadores del proyecto. |
| **Búsqueda global** | En vivo, con mayúsculas, palabra completa, expresión regular y filtro por *glob* (`*.cs, src/**`); resultados agrupados por archivo con la coincidencia resaltada. |
| **Imágenes** | Vista previa de PNG, JPG, GIF, BMP, ICO y WebP con dimensiones. |
| **Edición opcional** | Por archivo, explícita, con aviso visible; guarda con la misma codificación y BOM originales. |
| **Integración** | Rutas por línea de comandos, instancia única (otras apps pueden pedirle abrir carpetas y archivos), "Abrir con la aplicación predeterminada". |

## Requisitos

- **Para usarlo:** Windows 10 u 11 de 64 bits. No necesita .NET instalado (el ejecutable es autocontenido).
- **Para compilarlo:** [SDK de .NET 10](https://dotnet.microsoft.com/download).

## Instalación

1. Descarga `TextPeach.exe` desde la sección *Releases* del repositorio (o compílalo, ver
   [Compilar desde el código](#compilar-desde-el-código)).
2. Ponlo en la carpeta que quieras y ejecútalo. No hay instalador.
3. *(Opcional)* Para que otras apps lo encuentren, ponlo en `%LOCALAPPDATA%\Programs\TextPeach\`, en
   `%ProgramFiles%\TextPeach\`, junto a la app que lo invoca o en una carpeta del `PATH`.
4. *(Opcional)* **Modo portable:** crea un archivo vacío `portable.txt` junto al `.exe` y los datos se
   guardarán en `.\data\` en lugar de en tu perfil.

## Primeros pasos

1. En la pestaña **Proyectos**, pulsa **Agregar proyecto...** y elige una carpeta. También puedes
   **arrastrar carpetas** a la ventana.
2. ¿Tienes una carpeta con muchos proyectos dentro? Usa **Escanear carpeta...**: TextPeach busca los
   proyectos que contiene y los guarda en un grupo con el nombre de esa carpeta.
3. Haz doble clic en un proyecto para abrirlo en una pestaña nueva.
4. Elige archivos en el árbol de la izquierda (o con **Ctrl+P**). A la derecha verás su análisis.
5. Usa **Ctrl+Shift+F** para buscar en todo el proyecto y **F1** para ver todos los atajos.

## Cómo funciona

### Proyectos y grupos

Un *proyecto* es simplemente una carpeta. TextPeach detecta su tipo por archivos conocidos (`.sln`/`.csproj`
→ .NET, `package.json` → Node, `Cargo.toml` → Rust, `go.mod` → Go, `pyproject.toml` → Python, `pom.xml` →
Java, etc.) y lo muestra en verde junto al nombre.

- **Favoritos:** aparecen primero dentro de su grupo; el botón **★ Favoritos** filtra solo esos.
- **Grupos:** **Nuevo grupo...** crea uno vacío; el menú contextual de un proyecto (o de varios
  seleccionados con Ctrl/Shift) permite **Mover a grupo**, crear uno nuevo o **Quitar del grupo**. Cada
  cabecera se pliega con un clic, muestra cuántos proyectos tiene y ofrece **Abrir todos**. Su menú permite
  renombrar el grupo, eliminarlo (los proyectos quedan sin grupo) o quitarlo junto con sus proyectos.
- **Escanear carpeta:** recorre hasta cuatro niveles, se detiene en cada proyecto encontrado (no entra en
  ellos) y los agrupa con el nombre de la carpeta escaneada. Los proyectos que ya estaban en otro grupo se
  quedan donde estaban.
- **Arrastrar una carpeta:** si es un proyecto, se agrega; si contiene varios proyectos, se importa como
  grupo.
- **Quitar de la lista** nunca borra nada del disco.

### La pestaña de un proyecto

```
┌ Toolbar: Ir a archivo · Buscar en proyecto · Ir a línea │ Resumen · Refrescar   N archivos   [Abrir con la app predeterminada]
├──────────────┬─────────────────────────────────────────────┬──────────────────────┤
│ Archivos     │ Resumen │ archivo-a ✕ │ archivo-b ✕          │ Análisis             │
│  / Buscar    │ ruta/archivo.ext  [Lenguaje]  [Renderizada|Código] [✎ Editar] [⋯] │
│              │                                              │ Estructura (n)       │
│ árbol o      │   código con colores, plegado y números      │ Marcadores (n)       │
│ resultados   │   de línea · o documento renderizado         │                      │
├──────────────┴─────────────────────────────────────────────┴──────────────────────┤
│ aviso                       Ln 12, Col 5 · 4 espacios · LF · UTF-8 · C# · Solo lectura │
└────────────────────────────────────────────────────────────────────────────────────┘
```

- **Panel izquierdo:** conmutador **Archivos / Buscar**. En *Archivos*, el filtro de arriba cambia el árbol
  por una lista plana de coincidencias (↓ para entrar en la lista, Enter para abrir, Esc para limpiar).
- **Centro:** pestañas de archivos abiertos (la primera, **Resumen**, es fija). Menú contextual: cerrar los
  demás o todos. Al cambiar de pestaña se conservan el cursor y el scroll de cada archivo.
- **Menú ⋯ del archivo:** copiar ruta completa o relativa, revelar en el árbol, abrir con la aplicación
  predeterminada, recargar desde el disco.
- **Barra de estado:** posición del cursor, indentación, fin de línea, codificación, lenguaje y modo
  (*Solo lectura* / *Editando*); ajuste de línea, espacios invisibles (¶) y tamaño de letra.

### Vista renderizada y vista de código

Los archivos `.md`, `.markdown`, `.mdx`, `.txt` y `.text` (hasta 2 MB) se abren en **vista renderizada**.
El conmutador **Renderizada | Código** de la cabecera alterna entre ambas.

- Markdown se interpreta con [Markdig](https://github.com/xoofx/markdig) y se dibuja con controles nativos
  (sin navegador embebido): títulos, párrafos, negrita/cursiva/tachado, código en línea y en bloque,
  listas numeradas y con viñetas, listas de tareas, citas, tablas, separadores e imágenes locales. El HTML
  crudo se muestra como texto y **nunca se ejecuta**; las imágenes remotas no se descargan.
- **Enlaces:** a otro archivo del proyecto lo abren en TextPeach; `#ancla` desplaza el documento; `http(s)`
  y `mailto` se abren con la aplicación predeterminada.
- Un clic en la **estructura** desplaza la vista renderizada hasta esa sección. Un resultado de búsqueda (que
  necesita la posición exacta) o **Editar** cambian a la vista de código.

### Panel de análisis

Se calcula en segundo plano al abrir el archivo (y mientras editas, con una pausa de 0,8 s).

| Métrica | Cómo se obtiene | Aviso en ámbar |
|---|---|---|
| Líneas | Líneas físicas (un salto final no cuenta una línea extra). | — |
| Código / Comentarios | Las líneas que empiezan por un comentario de línea o están dentro de uno de bloque cuentan como comentario (aprox.). | — |
| En blanco | Líneas vacías o solo con espacios. | — |
| Decisiones | Recuento aproximado de `if`, `for`, `while`, `case`, `catch`, `&&`, `||`, `?:`… Indicador de complejidad. | > 150 |
| Anidación máx. | Profundidad máxima de llaves `{ }` (ignorando cadenas). | > 6 |
| Símbolos | Elementos detectados en la estructura. | — |
| Línea más larga | Caracteres y número de línea. | > 160 |
| Indentación | Tabs, *N* espacios o mixta. | Mixta |
| Fin de línea | CRLF, LF o mixto. | Mixto |
| Codificación | UTF-8, UTF-8 BOM, UTF-16 LE/BE o Latin-1. | — |
| Espacios al final | Si alguna línea termina en espacios o tabs. | Sí |

**Estructura:** clases, interfaces, structs, enums, records, funciones, métodos y propiedades (lenguajes
de código), títulos (Markdown), selectores y reglas `@` (CSS), claves de primer nivel (JSON, YAML, TOML,
INI), objetos `CREATE …` (SQL) y jerarquía de elementos con su `id`/`x:Name` (XML, XAML, HTML). La
detección usa expresiones regulares por familia de lenguaje: es rápida y suficiente para orientarse, pero
no es un compilador.

**Marcadores:** `TODO`, `FIXME`, `HACK`, `XXX`, `BUG`, `NOTE`, `OPTIMIZE`, `REVIEW` con su texto y línea,
coloreados por gravedad. En Markdown y texto solo cuentan si abren la línea, y nunca dentro de
`código en línea`.

### Resumen del proyecto

La pestaña **Resumen** analiza todos los archivos del proyecto en paralelo (los de más de 2 MB y los
binarios se cuentan pero no se leen): archivos, líneas, líneas con contenido, tamaño total, número de
lenguajes, barras por lenguaje (porcentaje de líneas), los doce archivos más extensos y hasta 300
marcadores. Todo es clicable.

### Búsqueda

- **Ir a archivo (Ctrl+P):** ordena por nombre exacto, prefijo del nombre, nombre que contiene, ruta que
  contiene y, al final, coincidencia difusa; a igualdad gana la ruta más corta.
- **Buscar en proyecto (Ctrl+Shift+F):** en vivo mientras escribes. Opciones `Aa` (mayúsculas), `ab|`
  (palabra completa) y `.*` (expresión regular), y un campo *Incluir* con globs separados por comas. Si hay
  texto seleccionado, se usa como búsqueda. Hasta 3 000 coincidencias (200 por archivo).
- **Buscar en el archivo (Ctrl+F):** el panel de búsqueda del visor, con F3 / Shift+F3.

### Edición (opcional)

1. Pulsa **✎ Editar** (o Ctrl+E). Aparece una franja ámbar: *los cambios se escriben en el disco solo al
   pulsar Guardar*.
2. **Guardar** (Ctrl+S) escribe el archivo con su codificación original (incluido el BOM si lo tenía).
3. **Terminar edición** (Ctrl+E) vuelve a solo lectura; si hay cambios sin guardar, pregunta
   *Guardar / Descartar / Cancelar*.

La pestaña muestra `✎` mientras editas y `●` si hay cambios sin guardar. Cerrar la pestaña, el proyecto o la
aplicación con cambios pendientes siempre pide confirmación.

### Cambios en el disco

Cada proyecto vigila su carpeta. Si un archivo abierto cambia fuera de TextPeach, se recarga solo
conservando el cursor y el scroll; si lo estabas editando, aparece un aviso con **Recargar** en lugar de
pisar tu trabajo. El árbol y el resumen se actualizan cuando se crean, borran o renombran archivos.

## Atajos de teclado

| Atajo | Acción |
|---|---|
| `Ctrl+P` | Ir a archivo (en un proyecto) / abrir proyecto |
| `Ctrl+Shift+O` | Abrir proyecto (búsqueda rápida) |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Pestaña siguiente / anterior |
| `Ctrl+1 … Ctrl+9` | Ir a la pestaña N (1 = Proyectos) |
| `Ctrl+W` / `Ctrl+F4` | Cerrar el archivo actual (o el proyecto si no hay archivos) |
| `F1` | Acerca de y atajos de teclado |
| `Ctrl+Shift+F` | Buscar en todo el proyecto |
| `Ctrl+Shift+E` | Filtrar archivos del árbol |
| `Ctrl+F`, `F3`, `Shift+F3` | Buscar en el archivo actual |
| `Ctrl+G` | Ir a línea (`línea` o `línea:columna`) |
| `Ctrl+PageDown` / `Ctrl+PageUp` | Archivo siguiente / anterior |
| `Ctrl+E` | Activar / terminar la edición del archivo |
| `Ctrl+S` | Guardar (solo en modo edición) |
| `Ctrl +`, `Ctrl −`, `Ctrl+0` | Zoom del texto |
| `Alt+Z` | Ajuste de línea |
| `F5` | Refrescar árbol, archivos y resumen |
| `Enter` / `Supr` en la lista de proyectos | Abrir / quitar de la lista |

## Línea de comandos e integración

```
TextPeach.exe [carpeta] [archivo] ...
```

- Una **carpeta** se agrega a la lista (si no estaba) y se abre en una pestaña.
- Un **archivo** se abre dentro del proyecto guardado que lo contiene; si no hay ninguno, se agrega su
  carpeta como proyecto.
- **Instancia única:** si TextPeach ya está abierto, la nueva ejecución le entrega las rutas por una
  *named pipe* local (solo del usuario actual), trae la ventana al frente y termina. Así otras aplicaciones
  pueden pedirle que abra un repositorio o un archivo sin duplicar ventanas.

Para que otra app lo encuentre, basta con ubicar `TextPeach.exe` en una de las rutas indicadas en
[Instalación](#instalación).

## Lenguajes soportados

- **Resaltado de sintaxis:** todas las gramáticas incluidas en
  [TextMateSharp.Grammars](https://github.com/danipen/TextMateSharp) (C#, F#, VB, C/C++, Java, Kotlin, Go,
  Rust, Swift, Dart, JavaScript, TypeScript, JSX/TSX, Python, Ruby, PHP, Lua, Perl, R, Julia, PowerShell,
  Shell, Batch, SQL, HTML, XML/XAML, CSS/SCSS/Less, JSON, YAML, TOML, INI, Markdown, Dockerfile, Makefile,
  Diff, LaTeX y más), con el tema propio *Peach Dark*.
- **Estructura (outline):** C#, Java/Scala/Groovy/Dart, JavaScript/TypeScript (y Vue/Svelte), Python, Go,
  Rust, C/C++/Objective-C, PHP, Ruby, Kotlin, Swift, Shell, PowerShell, Lua, Markdown, CSS/SCSS/Less, SQL,
  JSON, YAML/TOML/INI y XML/XAML/HTML.
- **Comentarios y métricas:** los lenguajes anteriores y otros como Haskell, Elixir, Erlang, Clojure,
  LaTeX o Protobuf.

Para añadir un lenguaje, edita `src/TextPeach/Services/Languages.cs` (extensión, color y sintaxis de
comentarios) y, si quieres estructura, `FileAnalyzer.cs`.

## Datos, privacidad y modo portable

- TextPeach **no se conecta a internet**, no tiene telemetría ni cuentas. Solo abre enlaces web cuando tú
  haces clic en ellos (con tu navegador predeterminado).
- Guarda su estado en `%APPDATA%\TextPeach\state.json` (o en `.\data\` junto al exe con `portable.txt`):
  lista de proyectos y grupos, pestañas y archivos abiertos, tamaño/posición de la ventana y preferencias del
  visor. La ruta exacta aparece en **Acerca de** (F1).
- Nunca modifica archivos salvo que actives la edición y pulses **Guardar**.

## Límites conocidos

| Límite | Valor | Qué pasa al superarlo |
|---|---|---|
| Archivo de texto mostrable | 16 MB | Se muestra un aviso y la opción de abrirlo con la aplicación predeterminada. |
| Vista renderizada por defecto | 2 MB | Se abre en vista de código. |
| Archivos listados por proyecto | 60 000 | El resumen lo indica con "+". |
| Coincidencias de búsqueda | 3 000 (200 por archivo) | Se indica "(limitado)". |
| Análisis del resumen | archivos ≤ 2 MB | Los más grandes se cuentan pero no se leen. |

Otros: la estructura y las métricas son heurísticas (regex), no un análisis semántico; el plegado de
llaves no entiende todos los literales de cadena de todos los lenguajes; solo se publica para Windows x64
(Avalonia es multiplataforma, así que portarlo es posible).

## Compilar desde el código

```bat
git clone <url-del-repositorio> TextPeach
cd TextPeach

dev.cmd                       :: compila en Debug y ejecuta
dotnet test TextPeach.sln     :: ejecuta los tests
build.cmd                     :: genera dist\TextPeach.exe (single-file, autocontenido, win-x64)
```

`build.cmd` publica con ReadyToRun y compresión, sin *trimming* (las gramáticas TextMate se cargan por
reflexión). La primera compilación descarga los paquetes NuGet.

**Depuración sin teclado:** en Debug, la variable `TEXTPEACH_DEBUG_ACTIONS` ejecuta acciones al iniciar,
por ejemplo `open:docs/DISENO.md;reveal:80;search:Palette;edit;about`. Útil para capturas de pantalla.

## Estructura del repositorio

```
TextPeach/
├── build.cmd · dev.cmd · TextPeach.sln
├── docs/
│   ├── ARQUITECTURA.md      capas, flujo de un archivo, decisiones técnicas
│   └── DISENO.md            sistema de diseño (colores, tipografía, componentes, patrones)
├── src/TextPeach/
│   ├── Assets/              iconos y tema de sintaxis peach-dark.json
│   ├── Controls/            Palette, plegado, renderizador de documentos
│   ├── Services/            estado, escáner, cargador de archivos, analizador, lenguajes, grupos,
│   │                        resaltado, instancia única
│   ├── ViewModels/          MVVM (CommunityToolkit.Mvvm)
│   └── Views/               XAML de Avalonia y diálogos
└── tests/TextPeach.Tests/   xUnit: analizador, decodificación, ranking, plegado, grupos
```

Más detalle en [docs/ARQUITECTURA.md](docs/ARQUITECTURA.md). Las reglas visuales están en
[docs/DISENO.md](docs/DISENO.md).

## Contribuir

¡Las contribuciones son bienvenidas!

1. Abre un *issue* describiendo el problema o la propuesta (con capturas si es algo visual).
2. Haz un *fork*, crea una rama y envía un *pull request* pequeño y enfocado.
3. Antes de enviarlo: `dotnet build` sin errores y `dotnet test` en verde. Si añades lógica pura
   (analizador, parsers, grupos…), añade tests.

Convenciones:

- **Textos de la interfaz en español neutro** (tuteo, mayúscula solo al inicio); **código, nombres y
  comentarios en inglés**.
- Respeta [docs/DISENO.md](docs/DISENO.md): solo colores de la paleta, un botón principal por zona, lo
  que solo se lee nunca va en un campo editable, la interfaz no menciona aplicaciones de terceros.
- Todo lo que toque disco va fuera del hilo de la interfaz.
- Mantén TextPeach de solo lectura por defecto: cualquier acción que escriba en disco debe ser explícita.

## Licencia y créditos

TextPeach se distribuye bajo la licencia **MIT** (ver [LICENSE](LICENSE)).

Construido con:

- [Avalonia UI](https://avaloniaui.net/) (MIT)
- [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) y AvaloniaEdit.TextMate (MIT)
- [TextMateSharp](https://github.com/danipen/TextMateSharp) y sus gramáticas (MIT; gramáticas con sus propias licencias, mayormente MIT)
- [Markdig](https://github.com/xoofx/markdig) (BSD-2-Clause)
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) (MIT)
