#!/usr/bin/env bash
# Removes what install.sh added. Your settings (projects, tabs, window) in ~/.config/peachapps/TextPeach stay.
set -euo pipefail
data="${XDG_DATA_HOME:-$HOME/.local/share}"
dir="$HOME/.local/opt/textpeach"
config="${XDG_CONFIG_HOME:-$HOME/.config}/peachapps/TextPeach"

rm -f "$data/applications/textpeach.desktop"
rm -f "$data/icons/hicolor/256x256/apps/textpeach.png"
# Only our own link, never a "textpeach" the user put there.
if [[ -L "$HOME/.local/bin/textpeach" && "$(readlink "$HOME/.local/bin/textpeach")" == "$dir/TextPeach" ]]; then
  rm -f "$HOME/.local/bin/textpeach"
fi
rm -f "$dir/TextPeach" "$dir/TextPeach.new" "$dir/TextPeach.bin.new" "$dir/installed.txt" "$dir/uninstall.sh"
rmdir "$dir" 2>/dev/null || true

command -v update-desktop-database >/dev/null && update-desktop-database -q "$data/applications" || true
command -v gtk-update-icon-cache >/dev/null && gtk-update-icon-cache -q -t "$data/icons/hicolor" || true

echo "TextPeach desinstalado."
echo "Tu configuración sigue en $config (bórrala a mano si ya no la quieres)."
