# Sistema de diseño de TextPeach

<!-- peach-hijo: DISENO v1.0 -->
> TextPeach comparte su sistema de diseño con las demás apps Peach (GitPeach, RestPeach): mismos colores,
> componentes, atajos y tono, para que pasar de una a otra no cueste nada. Este documento aplica esas reglas a
> TextPeach y agrega lo propio, marcado como **Propio de TextPeach**. Si cambias algo que también afecta a las
> otras apps (un color, un atajo común, un componente), dilo en el *pull request*: se cambia en las tres.

> **Regla de oro:** TextPeach parece un IDE sobrio para **leer** código. Fondo gris carbón, texto gris claro, un
> solo azul de acento, y color solo cuando significa algo (estado de git, lenguaje, advertencia). Nada
> decorativo, y nada que invite a escribir sin querer.

Índice:

1. [Principios](#1-principios)
2. [Base técnica](#2-base-técnica)
3. [Color](#3-color)
4. [Tipografía](#4-tipografía)
5. [Espaciado, forma y bordes](#5-espaciado-forma-y-bordes)
6. [Anatomía de la ventana](#6-anatomía-de-la-ventana)
7. [Componentes](#7-componentes)
8. [Patrones de interacción](#8-patrones-de-interacción)
9. [Teclado](#9-teclado)
10. [Textos y tono](#10-textos-y-tono)
11. [Iconografía y marca](#11-iconografía-y-marca)
12. [Métricas y gráficos](#12-métricas-y-gráficos)
13. [Rendimiento percibido](#13-rendimiento-percibido)
14. [Dónde está cada cosa en el código](#14-dónde-está-cada-cosa-en-el-código)

---

## 1. Principios

1. **Oscuro y denso, como un IDE.** Solo tema oscuro (`RequestedThemeVariant="Dark"`). Filas compactas, fuente
   base de 13 px, mucha información visible a la vez. No hay modo claro.
2. **El color es información.** El 90 % de la pantalla son grises. El azul de acento marca lo seleccionado y la
   acción principal. Verde, rojo, ámbar, morado… solo comunican estado o tipo, siempre con el mismo significado
   ([3.4](#34-colores-semánticos)), el mismo que en GitPeach y RestPeach.
3. **Una acción principal por zona.** En cada barra hay como máximo un botón `accent` (azul).
4. **Solo lectura primero (Propio de TextPeach).** Los archivos se abren en **solo lectura**; editar es un botón
   ámbar aparte (`Ctrl+E`) y guardar solo existe en modo edición. Git solo se **lee**: las acciones de git
   quedan para GitPeach.
5. **Lo destructivo nunca es por defecto.** Descartar o sobrescribir requieren un gesto explícito y, si no se
   puede deshacer, confirmación. "Quitar de la lista" dice que no borra nada del disco.
6. **Nada se mueve solo.** La ventana se abre donde la dejaste, invisible hasta estar colocada. Las listas se
   reemplazan de una vez. Al recargar un archivo se conservan el cursor y el scroll. La única excepción es
   **Seguir** ([8.6](#86-seguir-lo-que-cambia-en-el-disco)), que el usuario activa a propósito.
7. **Todo se puede hacer con teclado.** Cada acción frecuente tiene atajo y todos se listan en F1
   ([9](#9-teclado)).
8. **Estado persistente.** Pestañas, archivos abiertos, ventana, favoritos, vista elegida por archivo y
   preferencias se guardan solos en `state.json`.
9. **Inglés y español en la interfaz, inglés en el código.** Textos visibles en los dos idiomas
   ([10](#10-textos-y-tono)); clases, variables y comentarios en inglés.
10. **Sin nombres de terceros.** La interfaz nunca menciona ni lanza aplicaciones de terceros. Lo permitido:
    **"Abrir con la aplicación predeterminada"** (delega en el sistema), el explorador de archivos del sistema,
    la **Consola** (los shells instalados: PowerShell, cmd, Git Bash, WSL; en Linux los de `/etc/shells`) y
    GitPeach, la app hermana para git.
11. **Lo que solo se lee nunca parece editable.** Texto informativo, mensajes, atajos y documentos se muestran
    con `TextBlock`/`SelectableTextBlock`: sin cursor, sin fondo de campo, sin borde de foco. `TextBox` es solo
    para escribir.
12. **Portable y autocontenido.** Un único `.exe` que funciona sin instalar. Con un `portable.txt` al lado, los
    datos se guardan junto al exe. El instalador (`textpeach-windows-x64-setup.exe`, `install.sh`) es opcional: copia ese
    mismo exe a una carpeta del usuario, lo registra en el buscador del sistema y deja `installed.txt` al lado.
13. **Actualizar no interrumpe.** Una versión nueva solo se nota como un botón **↑ x.y.z** junto al **?**; nada
    se descarga sin confirmar en su popup (Actualizar y reiniciar / Ahora no / Omitir esta versión).

---

## 2. Base técnica

| Pieza | Elección | Notas |
|---|---|---|
| UI | **Avalonia 11.3** + `Avalonia.Themes.Fluent` | `RequestedThemeVariant="Dark"`, estilos propios encima (`Styles.axaml`). |
| Runtime | **.NET 10** (`net10.0`), C# `latest`, `Nullable` e `ImplicitUsings` | `InvariantGlobalization=true`, `SatelliteResourceLanguages=en`. |
| MVVM | **CommunityToolkit.Mvvm 8.4** | `[ObservableProperty]` sobre campos `_camelCase`, `[RelayCommand]`. |
| Bindings | `AvaloniaUseCompiledBindingsByDefault=true` | Todo XAML declara `x:DataType`. |
| Render | Software (`Win32RenderingMode.Software`) | Se excluye `av_libglesv2.dll` al publicar (≈25 MB menos). |
| DPI | `app.manifest` con `PerMonitorV2` | |
| Editor | **AvaloniaEdit 11.4** + **AvaloniaEdit.TextMate**, tema `peach-dark.json` | Por las gramáticas (reflexión) **no se publica con *trimming***. |
| Documentos | **Markdig** → controles nativos de Avalonia | Nunca un navegador embebido. |
| Publicación | `build.cmd [versión]` → single-file, self-contained, `win-x64`, ReadyToRun | `dev.cmd` compila Debug y ejecuta. |
| Estado | `%APPDATA%\peachapps\TextPeach\state.json` o `.\data\` con `portable.txt` | JSON con *source generator*, escritura atómica. |
| Tests | xUnit en `tests/TextPeach.Tests` | Toda la lógica pura (analizador, decodificación, diff, git, idiomas). |

---

## 3. Color

Todos los valores son hexadecimales sRGB. En XAML se escriben en minúsculas (`#1e1f22`); en C# se usan desde
`Controls/Palette.cs`. Nunca inventes un gris nuevo: elige el más cercano de estas tablas.

### 3.1 Superficies (de más oscura a más clara)

| Token (`Palette.*`) | Hex | Uso en TextPeach |
|---|---|---|
| (título) | `#111214` | Barra de título propia, botón Home. |
| `Chrome` | `#18191c` | Barra de pestañas de la ventana, barra de estado. |
| `Code` | `#1b1c1f` | Editor, diff, visor de imágenes, regla de cambios. |
| `Bg` | `#1e1f22` | Fondo de ventana y contenido; vista renderizada; franja de pestañas de archivos. |
| `Sidebar` | `#222327` | Árbol / búsqueda / cambios, panel de análisis, tarjetas del Resumen. |
| `Panel` | `#26272b` | Toolbar, cabecera del archivo, diálogos, `TextBox`, *stat tiles*. |
| `Panel2` | `#2d2e33` | Botón normal, *hover* de filas, chips, badge del lenguaje. |
| `FileBg` | `#34363c` | Cabecera de archivo dentro de un diff. |
| `Border` | `#3a3c42` | **Todos** los bordes, separadores, `GridSplitter`, *hover* de botón, contador *pill*. |

- Cada franja horizontal (pestañas, toolbar, banner, contenido, estado) cambia de superficie y se separa con un
  borde de 1 px `#3a3c42` del lado que toca al contenido.
- Nunca blanco ni grises claros como fondo; el más claro permitido es `#3a3c42` (borde o *hover*).
- Línea actual del editor: `#232428`.

### 3.2 Texto

| Token | Hex | Uso |
|---|---|---|
| `Text` | `#d4d4d4` | Texto normal. |
| `ContextFg` | `#c8c8c8` | Líneas de contexto en el diff; número de línea activo. |
| `Dim` | `#8a8d93` | Rutas, fechas, metadatos, *watermarks*, ✕ de pestañas. Clase `TextBlock.dim`. |
| (gutter) | `#5d6066` | Números de línea. |
| (comentario) | `#6f737a` | Comentarios en código (cursiva). |
| Blanco | `#ffffff` | **Solo** sobre fondos de color: botón `accent`, pestaña o segmento seleccionado. |

Jerarquía con `Text` vs `Dim` y peso (SemiBold), no con tamaños muy distintos. Rutas: directorio `Dim` +
nombre `Text`/SemiBold, pegados.

### 3.3 Acento y selección

| Uso | Hex |
|---|---|
| Acento (botón principal, cursor, subrayado de segmento activo) | `#4a90d9` |
| Acento *hover* | `#5aa0e9` |
| Seleccionado (filas, árbol, `ToggleButton:checked`, selección de texto) | `#33507a` |
| Seleccionado + *hover* | `#3a5a88` |
| Fila en *hover* | `#2d2e33` |

### 3.4 Colores semánticos

Significado fijo, igual en todas las apps Peach. Si un color ya significa algo, no lo uses para otra cosa.

| Nombre | Hex | Significado en TextPeach |
|---|---|---|
| Verde | `#6cc070` | Positivo / identidad: tipo de proyecto, contador de archivos, línea o archivo agregado, "Seguir" activo. |
| Rojo | `#e0645a` | Error, borrado, "carpeta no encontrada", `FIXME`/`BUG`. |
| Ámbar | `#e0a24a` | Atención: favorito ★, modificado, métrica fuera de rango, `HACK`/`XXX`, carpetas del árbol, clases en la estructura, "Editando", hay cambios en el disco. |
| Ámbar claro | `#f0c890` | Texto de aviso sobre `#5a4318`. |
| Azul | `#4a90d9` | `TODO`, encabezados, claves. |
| Morado | `#9a7ae0` | Funciones y métodos. |
| Teal | `#4ac7c0` | Renombrado; interfaces, structs, tipos. |
| Rosa | `#d96a9b` | Conflicto de git; palabras clave en código. |
| Oliva | `#c8c84a` | Enums; decoradores/atributos. |

### 3.5 Banners

Todo el ancho, debajo de la cabecera del archivo, `Padding="10,5"`, texto SemiBold, botones a la derecha.

| Tipo | Fondo | Cuándo |
|---|---|---|
| Atención | `#5a4318` (botones con borde `#8a6d1f`) | **Modo edición**. |
| Información | `#25364d` | "El archivo cambió en el disco" mientras se edita, con **Recargar**. |

### 3.6 Diff

| Elemento | Fondo | Texto |
|---|---|---|
| Línea agregada | `#1d3a2a` | `#a6e3b4` |
| Línea borrada | `#431f22` | `#f4a6a6` |
| Contexto | transparente | `#c8c8c8` |
| Relleno (lado a lado, donde el otro lado tiene líneas) | `#202124` | — |
| Notas | — | `Dim`, cursiva |

Coincidencias de búsqueda: fondo `#5a4318`, texto `#ffd27a`.

### 3.7 Serie categórica y color por lenguaje

Orden fijo, por índice (`i % 8`):

| # | Hex | # | Hex |
|---|---|---|---|
| 0 | `#4a90d9` azul | 4 | `#9a7ae0` morado |
| 1 | `#e0a24a` ámbar | 5 | `#4ac7c0` teal |
| 2 | `#6cc070` verde | 6 | `#e0645a` rojo |
| 3 | `#d96a9b` rosa | 7 | `#c8c84a` oliva |

**Propio de TextPeach:** cada lenguaje usa uno de esos 8 colores (`Services/Languages.cs`; p. ej. C# morado,
TypeScript azul, JavaScript oliva, Python azul, Rust ámbar, HTML rojo, JSON oliva); texto plano y
desconocidos usan `Dim`. Se muestra como un punto `●` de 9–10 px antes del nombre, en el badge de la cabecera y
en las barras del Resumen.

### 3.8 Resaltado de sintaxis "Peach Dark"

`Assets/peach-dark.json` (tema TextMate), el mismo en todas las apps Peach:

| Ámbito TextMate | Hex | Estilo |
|---|---|---|
| Texto por defecto | `#d4d4d4` sobre `#1b1c1f` | |
| `comment` | `#6f737a` | cursiva |
| Comentario de documentación | `#7c8a7f` | cursiva |
| `keyword`, `storage.type`, `storage.modifier` | `#d96a9b` | |
| `keyword.operator` | `#a9acb2` | |
| `punctuation` | `#8a8d93` | |
| `string` | `#8fcf94` | |
| Escapes, regex, *placeholders* | `#e0645a` | |
| `constant.numeric`, `constant.language` | `#e0a24a` | |
| Tipos, clases, namespaces | `#4ac7c0` | |
| Funciones | `#7db4ea` | |
| Propiedades / claves | `#a8c8e8` | |
| `this`/`self` | `#d96a9b` | cursiva |
| Etiquetas HTML/XML · atributos | `#4a90d9` · `#9a7ae0` | |
| Decoradores · preprocesador | `#c8c84a` · `#9a7ae0` | |
| Markdown: títulos `#4a90d9` bold · código `#e0a24a` · citas `Dim` cursiva · enlaces `#7db4ea` | | |
| `invalid` | `#e0645a` | |

Editor: selección `#33507a`, línea actual `#232428`, cursor `#4a90d9`, *gutter* `#5d6066`, plegado `Dim`
sobre `Code` (activo: `Text` sobre `#2d2e33`). TextMate colorea en segundo plano, así que tras abrir o
recargar un archivo se repintan las líneas visibles unas veces (`Controls/SyntaxColoring.cs`) para que la
primera pantalla nunca quede en blanco.

---

## 4. Tipografía

**UI:** `Segoe UI`. **Monoespaciada:** `Cascadia Mono, Consolas, Courier New` (clase `TextBlock.mono`; el
editor usa la misma pila).

| Tamaño | Uso |
|---|---|
| 9–10 | Glifos `●`/`■`, ✕ de pestañas de archivo, números de línea en listas laterales, etiquetas `TODO`. |
| 11 | Metadatos de la cabecera, badges, chips, etiquetas de *stat tile*, "hace n min". |
| 12 | Listas densas (árbol, resultados, estructura, métricas), pestañas de archivos, rutas bajo un nombre. |
| **13** | **Base** de la interfaz y tamaño inicial del editor (zoom con `Ctrl +/−`). |
| 14 | Nombre del proyecto en la Home; párrafos de la vista renderizada. |
| 16 | Título de un estado vacío. |
| 18 | Título del Resumen. |
| 20 | Valor de un *stat tile*. |

Pesos: `Normal`; **SemiBold** para títulos de sección, nombres principales y el elemento actual; **Bold** solo
para letras de estado y de tipo. Cursiva solo para notas ("Sin resultados"), comentarios y el título de la
pestaña de vista previa.

Recorte: `TextTrimming="CharacterEllipsis"` en listas y cabeceras (con la información completa en el
tooltip); rutas largas `LeadingCharacterEllipsis`.

---

## 5. Espaciado, forma y bordes

Unidad base de 2 px; valores habituales 2, 4, 6, 8, 10, 12, 14, 18.

| Elemento | Medida |
|---|---|
| Toolbar | `Padding="8,6"` (proyecto) o `"10"` (Home); botones con `Spacing="4"`. |
| Separador vertical en toolbar | `Border Width="1" Background="#3a3c42" Margin="6,2"`. |
| Barra de estado | `Padding="10,3"`. |
| Fila de la Home | `Margin="10,8"`, columnas `28,*,Auto`. |
| Fila densa | `Margin="8,2"`–`"8,3"`; árbol `+12` px por nivel. |
| Cabecera de sección en un panel | `Margin="8,6"`, título SemiBold, acción `flat` a la derecha. |
| Diálogo | `Margin="18"`, `Spacing="16"`; botones `MinWidth=84`, `Padding="12,5"`, a la derecha, el principal primero. |
| Tarjeta / *stat tile* | `CornerRadius=4`, `Padding="14,10"`, borde 1 px, separación 10 px. |
| Botón | `CornerRadius=3`, `Padding="10,4"`, borde 1 px. `flat`: sin borde, `Padding="6,2"`. |
| Chip | `Padding="6,1"`, 11 px, `MinWidth=26`. |
| Contador *pill* | `CornerRadius=7`, `Padding="6,0"`, fondo `#3a3c42`, 10 px. |
| `GridSplitter` | 4 px, `#3a3c42`. |
| Pestaña de archivo | `Margin="10,5,4,5"`, 12 px, separador derecho de 1 px. |

Esquinas: 3 px controles, 4 px tarjetas, 0 en segmentos y pestañas. Sin sombras ni degradados (salvo el logo).

---

## 6. Anatomía de la ventana

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ 🟣 TextPeach  proyecto · C:\ruta                              ─   ☐   ✕      │ título     #111214 (34 px)
├──────────────────────────────────────────────────────────────────────────────┤
│▌Proyectos▐│ proyecto-a ✕ │ proyecto-b ✕ │                                 ? │ pestañas   #18191c
├──────────────────────────────────────────────────────────────────────────────┤
│ ← → │ Ir a archivo  Buscar  Ir a línea │ Resumen  Refrescar  80 archivos       ● Seguir  Consola │ toolbar #26272b
├──────────────┬┬───────────────────────────────────────────┬┬─────────────────┤
│ Archivos     ││ Resumen │ archivo.cs ✕ │ otro.md ✕         ││ Análisis        │
│ Buscar       ││ src/archivo.cs  C#  41 KB   Cambios +3 −1 ││ Estructura      │
│ Cambios ●    ││ (banner si se edita)                      ││ Marcadores      │
│              ││ editor │ diff │ vista renderizada │ imagen ││                 │
│ #222327      ││ #1b1c1f                                ▌▌ ││ #222327         │
│              │├───────────────────────────────────────────┴┴─────────────────┤
│              ││ Consola  + Nueva   Filas|Columnas|Cuadrícula  Abajo|Página|Ventana  ✕ │ si está abierta
│              ││ PS C:\proyecto>                                              #1b1c1f │
├──────────────┴┴──────────────────────────────────────────────────────────────┤
│ ⑂ main ↑1 3 cambios   aviso…        Ln 12, Col 4 · 4 espacios · LF · UTF-8 · C# · Solo lectura │ estado #18191c
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Barra de título propia** (34 px, `#111214`): sin marco de Windows. Icono 18 px, "TextPeach" SemiBold y, en
  `dim` 12 px, el proyecto abierto y su ruta. Botones de ventana `Button.caption` de 46×34 dibujados con `Path`
  (cerrar en *hover* rojo `#e0645a`). Arrastrar mueve; doble clic maximiza. En la barra de tareas el título es
  "proyecto — TextPeach".
- **Barra de pestañas** (`#18191c`, alto mínimo 36 px): a la izquierda el botón fijo **Proyectos**
  (`Button.home`, fondo `#111214`, activo con línea inferior azul de 2 px), después una pestaña por proyecto y,
  a la derecha, `?` (F1). Sin icono. Tooltip: la ruta completa.
- **Toolbar** (`#26272b`): `←` `→`, navegación (Ir a archivo, Buscar en proyecto, Ir a línea), Resumen,
  Refrescar y el número de archivos en verde SemiBold. A la derecha **Seguir** y **Consola** (`ToggleButton`,
  marcado mientras la consola está abierta; ver [8.11](#811-consola)).
- **Cuerpo** en tres columnas con `GridSplitter` de 4 px: barra lateral 260 px (mín. 160), contenido (mín.
  360), análisis 290 px (mín. 200). La consola abierta **abajo** ocupa una fila bajo el contenido y el análisis
  (240 px por defecto, mín. 80, se recuerda); como **página**, el lugar del contenido y del análisis.
- **Barra de estado**: a la izquierda la rama de git (o "Sin git") y el último aviso; a la derecha posición del
  cursor, indentación, fin de línea, codificación, lenguaje en su color, "Solo lectura" / "Editando", ajuste de
  línea, `¶` y tamaño de fuente.

**Colocación de la ventana** (`App.PlaceWindow`): 1200×760 DIP por defecto sin superar el 92 % del área de
trabajo, mínimo 800×500; recuerda tamaño, posición, monitor y maximizado; el marco siempre queda visible; la
ventana arranca con `Opacity=0` y se muestra ya colocada.

---

## 7. Componentes

Los estilos viven en `src/TextPeach/Styles.axaml`. Usa clases, no colores sueltos (salvo fondos de franjas).

### 7.1 Botones

| Clase | Aspecto | Cuándo |
|---|---|---|
| (ninguna) | `#2d2e33`, borde `#3a3c42`, *hover* `#3a3c42` | Acciones normales. |
| `accent` | `#4a90d9`, texto blanco, sin borde | La principal de la zona (Agregar proyecto, Guardar, Aceptar). Una por zona. |
| `flat` | transparente, sin borde | Secundarias en filas y cabeceras (✕, Limpiar, toggles de la barra de estado). |
| `warn` | `#5a4318`, borde `#8a6d1f` | "✎ Editar": cambiar a un modo con riesgo. |
| `seg` (+ `active`) | subrayado de 2 px; activo azul y texto blanco SemiBold | Archivos / Buscar / Cambios. |
| `mode left/right` (+ `active`) | dos botones pegados | Renderizada \| Código ([7.13](#713-conmutador-de-vista)). |
| `ToggleButton` | marcado `#33507a` | Filtros persistentes (★ Favoritos). |
| `ToggleButton.chip` | 11 px | Opciones de búsqueda (`Aa`, `ab|`, `.*`). |
| `ToggleButton.follow` | punto gris que se enciende verde; marcado azul | **Seguir** ([8.6](#86-seguir-lo-que-cambia-en-el-disco)). |
| `ToggleButton.diff` | 12 px, marcado azul | "Cambios +n −n" de la cabecera del archivo. |

Cursor de mano en todo lo que se pulsa como botón o enlace; flecha en filas, árbol, pestañas, botones de
ventana y botones deshabilitados. Etiquetas cortas; si abre un diálogo, termina en `...`. Botones de una letra
o glifo (`✕`, `←`, `⋯`, `¶`) siempre con `ToolTip.Tip`.

### 7.2 Campos de texto

`TextBox` con fondo `#26272b` y borde `#3a3c42`; *watermark* terminado en `...`/`…` ("Filtrar archivos...").
El filtro principal recibe el foco al abrir la vista. `Esc` limpia; `Enter` ejecuta; `↓` pasa a la lista.

### 7.3 Listas

Fondo transparente, sin borde ni *padding*; seleccionado `#33507a`, *hover* `#2d2e33`. Las listas de
navegación (resultados, marcadores, cambios, archivos más extensos) abren con un clic. Las listas grandes se
reemplazan enteras.

### 7.4 Árbol de archivos

Ítems de 22 px, 12 px de fuente. Carpetas con `■` ámbar; archivos con `●` del color del lenguaje. Carga
perezosa ("…" hasta expandir), carpetas primero, orden alfabético sin distinguir mayúsculas. Se ocultan las
carpetas de herramientas y build (`.git`, `node_modules`, `bin`, `obj`, `dist`…) y las que el proyecto ignora
([8.7](#87-historial-y-carpetas-ignoradas)). Un clic abre en vista previa; doble clic fija la pestaña o
expande la carpeta. El clic derecho selecciona la fila (sin abrirla) y muestra el menú, por grupos:

| Grupo | Entradas |
|---|---|
| Abrir (solo archivos) | Abrir · Abrir con la aplicación predeterminada |
| Crear | Nuevo archivo... · Nueva carpeta... (en la carpeta, o en la del archivo; el nombre admite `src/a.cs`) |
| Portapapeles | Cortar `Ctrl+X` · Copiar `Ctrl+C` · Pegar `Ctrl+V` · Duplicar |
| Rutas y sistema | Copiar ruta completa · Copiar ruta relativa · Mostrar en el Explorador · Abrir consola aquí |
| Proyecto | Ignorar esta carpeta · Carpetas ignoradas… |
| Al final | Cambiar nombre... `F2` · Eliminar... `Supr` |

Sobre el espacio vacío del árbol, lo mismo para la raíz del proyecto. Copiar pone los archivos también en el
portapapeles del sistema (se pegan en el Explorador) y Pegar acepta lo copiado en el Explorador; lo cortado se
mueve solo si el portapapeles sigue teniendo exactamente eso. Nada se pisa: un nombre ocupado da
"a - copia.txt", "a - copia (2).txt". Eliminar siempre confirma y manda a la **papelera**; si no hay papelera,
una segunda confirmación dice que no se puede deshacer. Un archivo abierto que se renombra o mueve sigue abierto
en su nueva ruta; uno que se está editando bloquea la acción ("termina la edición primero").

### 7.5 Pestañas

- **De proyecto** (ventana): ver [6](#6-anatomía-de-la-ventana). `Ctrl+1` selecciona Proyectos.
- **De archivo** (`ListBox.doctabs`): franja `#1e1f22`, separador de 1 px, la seleccionada toma el color de la
  cabecera (`#26272b`) y texto blanco. Primera pestaña fija: **Resumen**. Menú: Cerrar los demás, Cerrar todos.
  Prefijos: `● ` sin guardar, `✎ ` en edición. Con cambios de git, el título toma el color del estado.
- **Vista previa:** un clic en el árbol, un salto desde la búsqueda, un marcador o el historial abre el archivo
  en una única pestaña con el título en *cursiva* (`TextBlock.preview`), que el siguiente reemplaza. Doble clic
  o empezar a editar la deja fija.
- **Desbordamiento:** `ListBox.tabstrip` con la barra de 4 px en su propia fila debajo y la rueda del mouse
  desplazando de lado (`Views/TabStrip.cs`).

### 7.6 Cabecera del archivo

Franja `#26272b`, `Padding="10,5"`: ruta (dir `dim` + nombre SemiBold), badge del lenguaje (borde `#3a3c42`,
fondo `#2d2e33`, texto del color del lenguaje, 11 px) y metadatos `dim` 11 px (tamaño, fecha) que se recortan
con `…`. A la derecha: **Cambios +n −n** (si hay cambios de git), Renderizada | Código (si aplica), ✎ Editar /
Guardar y `⋯` con lo secundario (copiar ruta, revelar en el árbol, abrir con la app predeterminada, recargar
del disco).

### 7.7 Tarjetas y *stat tiles*

Tarjeta: `#222327`, borde 1 px, `CornerRadius=4`, título `TextBlock.section`. *Stat tile*: `#26272b`, valor 20 px
SemiBold, etiqueta `dim` 11 px, `MinWidth=150`, en `WrapPanel`. Se usan en el Resumen.

### 7.8 Métricas clave-valor

`Grid ColumnDefinitions="*,Auto"`, etiqueta `dim` 12 px, valor 12 px a la derecha. Fuera de rango: ámbar, con el
criterio en el tooltip; si es heurística, el tooltip dice "Aproximado".

### 7.9 Estados vacíos

Centrados y no interactivos: logo 64 px al 85 %, título 16 px, frase `dim` con qué hacer ("Agrega uno, escanea
una carpeta o arrastra carpetas a esta ventana."). En paneles chicos, una línea `dim` 12 px ("Sin marcadores").

### 7.10 Diálogos

Construidos en código (`Views/Dialogs.cs`), modales, fondo `#26272b`, sin redimensionar, centrados.

| Diálogo | Contenido | Teclas |
|---|---|---|
| `Confirm` / `Choose` | Texto + [acción (accent)] [Cancelar] | Esc cancela |
| `Message` | `TextBlock` con ajuste dentro de un `ScrollViewer` (máx. 320) + [Cerrar]. **Nunca** un `TextBox`. | Esc/Enter cierra |
| `About` (F1, `?`) | Logo 48 px + "TextPeach" 18 SemiBold + versión + frase · atajos en dos columnas por grupo · **Idioma** (English \| Español) · "Configuración guardada en: …" · [Abrir carpeta de configuración] [Cerrar] | Esc/Enter cierra |
| `Prompt` / `EditLines` | Etiquetas `dim` sobre cada campo; primer campo enfocado | Enter acepta, Esc cancela |
| `QuickOpen` | Buscador + lista sobre `#1e1f22`; nombre SemiBold y ruta `dim` 11 px | ↑/↓, Enter, Esc |

### 7.11 Menús contextuales

Verbos cortos, separadores para agrupar, lo destructivo al final ("Quitar de la lista"). Lo que abre otra
ventana termina en `...`.

### 7.12 Barra de estado

Izquierda: rama de git ([7.15](#715-git-de-solo-lectura)) y el último aviso; nunca diálogos para información
no crítica. Derecha: contexto del archivo (cursor o "Vista renderizada", indentación, fin de línea,
codificación, lenguaje en su color, "Solo lectura" `dim` / "Editando" ámbar SemiBold), toggles `flat` y tamaño
de fuente.

### 7.13 Conmutador de vista

**Renderizada | Código** en la cabecera del archivo: dos botones pegados (`CornerRadius="3,0,0,3"` y
`"0,3,3,0"`), `Padding="10,4"`, 12 px; inactivo texto `Dim`, activo `#33507a` y blanco. Solo aparece si el
archivo admite vista renderizada. La elección se recuerda **por archivo**.

### 7.14 Vista renderizada de documentos (Propio de TextPeach)

`.md`, `.markdown`, `.mdx`, `.txt` y `.text` se abren **renderizados** (hasta 2 MB); el código queda a un clic.

| Elemento | Estilo |
|---|---|
| Página | `#1e1f22`, `MaxWidth=920`, `Padding="40,28,40,60"` |
| Párrafo | Segoe UI 14, interlineado 22, margen inferior 12, seleccionable |
| H1 / H2 | 26 / 21 px SemiBold, línea inferior 1 px `#3a3c42` |
| H3 / H4 / H5-6 | 17 / 15 / 14 px SemiBold (H5-6 en `Dim`) |
| Código en línea | Monoespaciada 12.5, fondo `#2d2e33`, texto `#e0a24a` |
| Bloque de código | `#1b1c1f`, borde 1 px, radio 4, `Padding="14,10"`, 12.5 px; lenguaje `Dim` 10 px arriba a la derecha |
| Enlace | `#7db4ea` subrayado. Relativo → abre el archivo; `#ancla` → desplaza; `http(s)`/`mailto` → app predeterminada |
| Cita | Borde izquierdo 3 px `#3a3c42`, texto `Dim` |
| Listas | `•` o número en `Dim`, columna de 22 px; tareas `☑` verde / `☐` `Dim` |
| Tabla | Celdas con borde 1 px, `Padding="10,5"`, cabecera SemiBold sobre `#26272b` |
| Imágenes | Locales a tamaño natural (reducidas al ancho); remotas no se cargan |
| HTML crudo | Texto `Dim` monoespaciado; nunca se ejecuta |

Saltar desde la estructura desplaza la vista renderizada; un resultado de búsqueda cambia a Código. Editar
siempre pasa a Código.

### 7.15 Git de solo lectura (Propio de TextPeach)

TextPeach muestra git como la mayoría de los editores pero **nunca** ejecuta una acción que escriba: solo
`git --no-optional-locks status --porcelain=v1 -z --branch` y `git show HEAD:<ruta>`.

| Estado | Color | Letra |
|---|---|---|
| Modificado | Ámbar `#e0a24a` | `M` |
| Sin seguimiento | Verde `#6cc070` | `U` |
| Agregado | Verde `#6cc070` | `A` |
| Renombrado | Teal `#4ac7c0` | `R` |
| Borrado | Rojo `#e0645a` | `D` |
| Conflicto | Rosa `#d96a9b` | `!` |
| Ignorado | `Dim` | — |

- **Archivos** (árbol, filtro, pestañas): nombre en el color del estado y la letra a la derecha
  (`TextBlock.gitstatus`, 11 px SemiBold).
- **Carpetas:** el color del cambio más fuerte que contienen (conflicto > modificado > borrado > renombrado >
  agregado > sin seguimiento) y `•` a la derecha.
- **Rama** (barra de estado): icono de rama dibujado + nombre + `↑n ↓n` + "n cambios". **Un clic abre el
  repositorio en GitPeach**; si no está instalado, el diálogo común que explica qué es y ofrece **Ir al
  repositorio**.
- **Marcas junto a los números de línea** (`GitChangeMargin`, 3 px): verde agregado, ámbar modificado, cuña roja
  donde se borraron líneas. Un archivo sin seguimiento es todo verde.
- **Regla junto al scroll** (`OverviewRuler`, 10 px): el archivo entero a escala; a la izquierda los cambios de
  git, a la derecha las coincidencias de búsqueda (`#ffd27a`) y una línea en el cursor. Un clic salta ahí.
- **Diff** (`Alt+D` o "Cambios +n −n"): reemplaza la vista por el diff contra el último commit, *En línea* o
  *Lado a lado* (se recuerda; los dos paneles se desplazan juntos), `↑ ↓` entre cambios, y abre en el primero.
- Se actualiza con *debounce* (600 ms) ante cambios de la carpeta o de `.git` y con F5.
- **Sin git** (no es un repositorio, git no está instalado o no respondió): la barra de estado dice "Sin git"
  con el motivo en el tooltip, y el Resumen muestra una tarjeta `Border.warning` (borde ámbar de 3 px a la
  izquierda, `⚠`).

### 7.16 Barras de desplazamiento

Una sola para todo, en `Styles.axaml`: línea de **4 px**, esquinas de 2 px, sin flechas, no crece con el
mouse; `AllowAutoHide=False`, así nunca tapa contenido.

---

## 8. Patrones de interacción

### 8.1 Home de proyectos

- Toolbar: **Agregar proyecto...** (accent), **Escanear carpeta...**, **Nuevo grupo...**, **Actualizar**, filtro
  y, a la derecha, `▾ Expandir` / `▸ Contraer` y `★ Favoritos`.
- Fila: ★ ámbar · nombre 14 px SemiBold · tipo de proyecto en verde (.NET, Node, Python…) · "(carpeta no
  encontrada)" en rojo · ruta `dim` · fecha `yyyy-MM-dd HH:mm` a la derecha.
- Orden: favoritos y luego último abierto. Filtro difuso sobre nombre + ruta. Doble clic o Enter abre; Supr
  quita de la lista (con multiselección). Quitar nunca toca el disco y el aviso lo dice.
- **Grupos:** "Escanear carpeta" guarda lo encontrado en un grupo con el nombre de la carpeta; arrastrar una
  carpeta de proyectos equivale a escanearla. Cabecera de grupo `#222327` con chevron, nombre SemiBold, contador
  y "Abrir todos"; los proyectos del grupo se sangran 26 px; lo no agrupado va al final en "Sin grupo"; el
  plegado se guarda. Menús de grupo y de proyecto producidos como `MenuAction`.

### 8.2 Pestañas persistentes

Los proyectos abiertos y el activo se restauran al iniciar, y dentro de cada uno sus archivos abiertos, el
activo y la pestaña de vista previa. Cerrar con cambios sin guardar pide confirmación.

### 8.3 Ocupado y avisos

Progreso indeterminado en la barra de estado mientras se trabaja; el resultado de una acción va al aviso
("Copiado: …"). Los errores con detalle técnico abren además un `Message`.

### 8.4 Vigilancia del disco

Un `FileSystemWatcher` por proyecto con *debounce* (300 ms archivo, 600–700 ms estructura). Se refresca solo lo
afectado y se conservan selección, expansión, cursor y scroll. Si el archivo se está editando, no se pisa: banner
informativo con **Recargar**.

### 8.5 Solo lectura y edición

Abrir nunca habilita edición. "✎ Editar" (`warn`, `Ctrl+E`) pasa a modo edición: banner ámbar, **Guardar**
(`accent`, `Ctrl+S`) y **Terminar edición**. Terminar con cambios pregunta Guardar / Descartar / Cancelar. Se
guarda con la misma codificación y BOM con que se leyó.

### 8.6 Seguir lo que cambia en el disco

Pensado para mirar lo que hace un agente de IA. La barra lateral tiene la sección **Cambios** (punto ámbar si
hay algo): archivos creados (`+` verde), modificados (`~` ámbar) o borrados (`−` rojo, tachado
`TextBlock.gone`) desde que se abrió el proyecto, el más reciente arriba, con "hace n min". **Seguir** abre solo,
en la pestaña de vista previa, cada archivo que cambia y salta al cambio; nunca interrumpe una edición.

### 8.7 Historial y carpetas ignoradas

- `Alt+←` / `Alt+→` (y `←` `→` en la toolbar) vuelven a donde estabas antes de saltar desde la búsqueda, la
  estructura, un marcador, "Ir a línea" o la regla.
- Las carpetas a ignorar se eligen por proyecto (menú del árbol); quedan fuera del árbol, la búsqueda y la lista
  de cambios. Una línea por carpeta: un nombre la ignora en cualquier nivel, una ruta solo esa, `!nombre` muestra
  una que se ignora por defecto.

### 8.8 Búsqueda

- **Ir a archivo** (`Ctrl+P`): nombre exacto > prefijo > contiene en nombre > contiene en ruta > difuso; empates
  por ruta más corta.
- **Buscar en proyecto** (`Ctrl+Shift+F`): en vivo con *debounce* de 350 ms, opciones `Aa`/`ab|`/`.*`, filtro por
  glob, resultados agrupados por archivo con contador *pill* y la coincidencia resaltada; límite indicado
  ("(limitado)") y tiempo. Si hay texto seleccionado, se usa como consulta.
- **Buscar en el archivo** (`Ctrl+F`, `F3` / `Shift+F3`).

### 8.9 Integración con el sistema y con GitPeach

- "Abrir con la aplicación predeterminada" (`Process.Start` con `UseShellExecute=true`): menú del árbol, menú
  `⋯` del archivo, nota de binarios y menú de la Home; ya no tiene botón en la toolbar. Los enlaces
  `http(s)`/`mailto` también van al sistema.
- Todo lo de git se delega en **GitPeach** (clic en la rama). `GitPeachLocator` la busca en: su
  `location.json`, junto al propio exe, `..\GitPeach`, `%LOCALAPPDATA%\Programs\GitPeach`,
  `%ProgramFiles%\GitPeach` y el `PATH`.
- Acepta rutas por línea de comandos (`TextPeach.exe <carpeta> [archivo]`) y es de **instancia única**: una
  segunda ejecución (por ejemplo, GitPeach pidiendo abrir un archivo) entrega sus rutas a la ventana abierta y
  termina.

### 8.10 Carpeta compartida `peachapps`

```
%APPDATA%\peachapps\TextPeach\   state.json · location.json
```

`state.json` guarda la app; en modo portable va en `.\data\`. `location.json` (`exe`, `version`, `lastRun`) se
escribe cada vez que se abre una compilación Release, para que GitPeach y RestPeach la encuentren aunque se haya
movido. "Acerca de" muestra la carpeta real y la abre.

`state.json` también guarda la búsqueda de versiones (`CheckUpdates`, `LastUpdateCheck`, `LatestVersion`,
`SkippedVersion`): como mucho una consulta a GitHub al día, y el botón reaparece al abrir sin volver a preguntar.

### 8.11 Consola

Terminales reales (pseudoterminal: ConPTY en Windows, `forkpty` en Linux; emulación xterm con
`Iciclecreek.Avalonia.Terminal`) en la carpeta del proyecto, con cualquiera de los shells instalados
(`Services/Shells.cs`, detectados una vez): en Windows **PowerShell 7**, **Windows PowerShell**, **cmd**,
**Git Bash** (junto al `git.exe` del `PATH` o en las carpetas de siempre; arranca en el proyecto con
`CHERE_INVOKING=1`) y **WSL** si hay una distribución; en Linux y macOS el `$SHELL` del usuario primero y luego los
de `/etc/shells` (uno por nombre, sin `nologin`/`false`).

- **Abrir:** botón **Consola** de la toolbar o `Ctrl+J` (también desde dentro de una terminal). Si no hay
  ninguna, abre una. "Abrir consola aquí" en el árbol abre otra ya en esa carpeta.
- **Hasta 4 terminales** por proyecto, cada una con su propio shell (una cmd y una PowerShell a la vez, por
  ejemplo). **+ Nueva** abre el shell predeterminado y **▾**, pegado a su derecha, lista todos (el
  predeterminado marcado); elegir uno lo deja como predeterminado (`ConsoleShell` en `state.json`). "Abrir
  consola aquí" del árbol también es un submenú con los shells. Cabecera de 24 px por terminal
  (`Border.termhead`: "Shell · carpeta" y ✕); la activa, un tono más clara (`#2d2e33`).
- **Disposición** (con 2 o más): **Filas** (una sobre otra) · **Columnas** (lado a lado) · **Cuadrícula** (dos por
  fila: con 4, una en cada esquina; con 3, la última ocupa la fila de abajo). Splitters de 4 px entre ellas.
- **Lugar:** **Abajo** (bajo el código y el análisis) · **Página** (pestaña "Consola" en la franja de archivos,
  justo después de Resumen, que toma el lugar del código y del análisis) · **Ventana** (aparte, "Consola —
  proyecto"; cerrarla devuelve la consola abajo). Una vista simple de un archivo no tiene página.
- Lugar, disposición y alto del panel se recuerdan en `state.json` (`ConsolePlace`, `ConsoleArrangement`,
  `ConsoleHeight`) y valen para todos los proyectos.
- **Ocultar no cierra:** el ✕ de la barra, `Ctrl+J` o cerrar la pestaña "Consola" la ocultan y las terminales
  siguen corriendo. Una terminal termina con su ✕, cuando su shell sale (`exit`) o al cerrar el proyecto o la app.
- Cambiar la consola de lugar nunca reinicia los shells: `ConsoleView` es una por proyecto y se mueve entera.
- Colores: fondo `Code`, texto `Text`, selección `#33507a`, cursor azul y los 16 colores ANSI tomados de
  [3.4](#34-colores-semánticos); fuente monoespaciada de 13 px.

---

## 9. Teclado

Atajos comunes a todas las apps Peach (no se reutilizan para otra cosa) y los propios de TextPeach. Todos se
declaran en XAML (`KeyBindings`) y se listan en `ViewModels/Shortcuts.cs`, que alimenta F1. Los botones
muestran su atajo en el tooltip.

| Atajo | Acción |
|---|---|
| `Ctrl+P` | Ir a archivo (en un proyecto) / abrir proyecto |
| `Ctrl+Shift+O` | Abrir proyecto (búsqueda rápida) |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Pestaña siguiente / anterior |
| `Ctrl+1 … Ctrl+9` | Ir a la pestaña N |
| `Ctrl+W` / `Ctrl+F4` | Cerrar archivo (o el proyecto si no hay archivos) |
| `F1` | Acerca de, atajos e idioma |
| `F5` | Refrescar árbol, archivos, resumen y git |
| `Ctrl+Shift+F` | Buscar en todo el proyecto |
| `Ctrl+Shift+E` | Filtrar el árbol |
| `Ctrl+F` · `F3` / `Shift+F3` | Buscar en el archivo |
| `Ctrl+G` | Ir a línea (línea:columna) |
| `Alt+←` / `Alt+→` | Atrás / adelante |
| `Alt+D` | Cambios del archivo contra el último commit |
| `Ctrl+PageDown` / `PageUp` | Archivo siguiente / anterior |
| `Ctrl+E` · `Ctrl+S` | Editar / terminar · Guardar (solo editando) |
| `Ctrl +` / `Ctrl −` / `Ctrl+0` | Zoom del texto |
| `Alt+Z` | Ajuste de línea |
| `Ctrl+J` | Mostrar u ocultar la consola |
| `F2` · `Supr` · `Ctrl+C` / `X` / `V` | En el árbol: cambiar nombre, eliminar, copiar, cortar, pegar |
| `Esc` | Cerrar diálogo / limpiar filtro o búsqueda |

---

## 10. Textos y tono

- **Inglés y español.** Ningún texto visible va escrito en XAML ni en C#: todos están en `Services/Strings.cs`
  como par (inglés, español) bajo una clave por pantalla (`home.add`, `pv.goToFile`…). En XAML
  `{l:L clave}`, en C# `Loc.T`, `Loc.F` y `Loc.Plural` (`clave.one` / `clave.many`). Un test verifica que cada
  clave usada exista en los dos idiomas con los mismos marcadores.
- El idioma se elige en "Acerca de" o, si no, es el de Windows; se aplica al reiniciar.
- No se traducen: nombres de apps, atajos, términos técnicos (commit, TODO, CRLF), rutas y lenguajes.
- Inglés claro y corto, *sentence case*. Español neutro con tuteo ("Agrega uno…"), mayúscula solo al inicio, sin
  exclamaciones ni emojis.
- Títulos de ventana y pestañas: el nombre del elemento.
- Avisos descriptivos: "Agregado: X", "Quitado de la lista: X (no se borró nada del disco)".
- Errores: qué pasó + el dato concreto ("La carpeta ya no existe: C:\…").
- Notas en cursiva `dim`: "Archivo binario no mostrado (2.3 MB)", "Sin resultados".
- Números con separador de miles, tamaños en B/KB/MB con un decimal, fechas `yyyy-MM-dd HH:mm`.

---

## 11. Iconografía y marca

- **Glifos Unicode** en vez de una fuente de iconos: `★` favorito, `✕` cerrar, `●` lenguaje/sin guardar, `■`
  carpeta, `✎` editar, `⋯` más, `¶` espacios, `?` ayuda, `← →` historial, `↑ ↓` cambio anterior/siguiente.
  Símbolos de la estructura: `N` namespace, `C` clase, `I` interfaz, `S` struct, `E` enum, `R` record, `ƒ`
  función, `M` método, `P` propiedad, `#` título, `K` clave, `{}` selector, `T` tabla, `<>` elemento.
- **Logo:** ciruela violeta (de `#3b3790` a `#c08ef0`, la mitad izquierda más oscura) con hoja verde y `</>`
  blanco. Cada app Peach tiene su fruta y color para distinguirse en la barra de tareas. Va en `app.ico`,
  `icon64.png` y `logo.png`; se muestra en la barra de título, "Acerca de" y los estados vacíos, **nunca** en la
  barra de pestañas. Es el único lugar con degradados.
- El nombre es **TextPeach**, una palabra, con T y P mayúsculas. Un fork debe usar otro nombre y logo.

---

## 12. Métricas y gráficos

- **Barras horizontales** simples (8 px de alto, esquina derecha redondeada, ancho proporcional al máximo) en vez
  de tortas: porcentaje SemiBold, valor `dim` y conteo `dim`. Color = color del lenguaje.
- Los indicadores heurísticos (comentarios, decisiones) se marcan como aproximados en el tooltip.
- Umbrales en ámbar: línea > 160 caracteres, anidación > 6, decisiones > 150, indentación o fin de línea mixtos,
  espacios al final.

---

## 13. Rendimiento percibido

- Disco, git y análisis en segundo plano; al hilo de la interfaz solo vuelven los resultados.
- Colecciones reemplazadas de una vez; *debounce* en filtros (350 ms), *watchers* (300–700 ms) y análisis
  mientras se edita (800 ms).
- Límites explícitos y comunicados: archivos de texto hasta 16 MB, diff hasta 20 000 filas, búsqueda hasta
  3 000 coincidencias, proyectos hasta 60 000 archivos.
- "Cargando…" en `dim` en vez de un panel vacío.

---

## 14. Dónde está cada cosa en el código

| Qué | Dónde |
|---|---|
| Estilos y clases (`accent`, `seg`, `chip`, `follow`, `diff`, `preview`, barras de 4 px…) | `src/TextPeach/Styles.axaml` |
| Colores en C# | `Controls/Palette.cs` |
| Tema de sintaxis | `Assets/peach-dark.json` + `Services/Highlighting.cs` |
| Marcas de git, regla, diff | `Controls/ChangeMarkers.cs`, `Controls/DiffView.cs`, `Services/LineDiff.cs` |
| Vista renderizada | `Controls/DocumentRenderer.cs` |
| Diálogos | `Views/Dialogs.cs` |
| Textos | `Services/Strings.cs` |
| Atajos de F1 | `ViewModels/Shortcuts.cs` |
| Consola (lugar, disposición, terminales) | `ViewModels/ConsoleViewModel.cs`, `Views/ConsoleView.axaml(.cs)` |
| Acciones de archivo del árbol | `ViewModels/ProjectViewModel.FileActions.cs`, `Services/FileOps.cs` |
| Colocación de la ventana | `App.axaml.cs` (`PlaceWindow`) |

Si agregas un patrón de interfaz nuevo, documéntalo aquí.
