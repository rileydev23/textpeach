#!/usr/bin/env bash
# Installs TextPeach for the current user, without root: the binary in ~/.local/opt/textpeach (so the app can update
# itself there), a "textpeach" command in ~/.local/bin and a menu entry, so the desktop's app search finds it.
# Run it from the extracted release:  ./install.sh      Undo with:  ~/.local/opt/textpeach/uninstall.sh
set -euo pipefail
src="$(cd "$(dirname "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
dir="$HOME/.local/opt/textpeach"
bin="$HOME/.local/bin"

if [[ ! -f "$src/TextPeach" ]]; then
  echo "No se encontró el binario TextPeach junto a install.sh." >&2
  exit 1
fi

mkdir -p "$dir" "$bin" "$data/applications" "$data/icons/hicolor/256x256/apps"
# Through a temporary name: replacing a running binary in place would break it, a rename does not.
install -m 755 "$src/TextPeach" "$dir/TextPeach.tmp"
mv -f "$dir/TextPeach.tmp" "$dir/TextPeach"
install -m 755 "$src/uninstall.sh" "$dir/uninstall.sh"
install -m 644 "$src/installed.txt" "$dir/installed.txt"
install -m 644 "$src/textpeach.png" "$data/icons/hicolor/256x256/apps/textpeach.png"
ln -sf "$dir/TextPeach" "$bin/textpeach"

cat > "$data/applications/textpeach.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=TextPeach
GenericName=Code viewer
GenericName[es]=Visor de código
Comment=Code viewer and analyzer
Comment[es]=Visor y analizador de código
Exec="$dir/TextPeach" %F
Icon=textpeach
Terminal=false
Categories=Development;TextEditor;Utility;
Keywords=code;viewer;git;diff;
MimeType=text/plain;text/markdown;text/x-log;text/csv;text/html;text/css;text/xml;application/xml;application/json;application/x-yaml;application/toml;text/javascript;application/javascript;application/typescript;text/x-csharp;text/x-java;text/x-python;text/x-script.python;text/x-go;text/x-rust;text/x-csrc;text/x-chdr;text/x-c++src;text/x-c++hdr;text/x-php;text/x-ruby;text/x-lua;text/x-sql;application/x-shellscript;text/x-shellscript;
EOF

command -v update-desktop-database >/dev/null && update-desktop-database -q "$data/applications" || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$data/icons/hicolor" || true

echo "TextPeach instalado en $dir"
echo "Búscalo en el menú de aplicaciones o ejecuta: textpeach"
case ":$PATH:" in *":$bin:"*) ;; *) echo "(Agrega $bin a tu PATH para usar el comando textpeach.)" ;; esac
echo "Para desinstalar: $dir/uninstall.sh"
