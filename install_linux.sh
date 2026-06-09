#!/bin/bash

echo "=================================================="
echo "   Arcanum — Instalador para Linux"
echo "=================================================="
echo ""

# Pede o caminho do ícone
read -p "Cole o caminho completo do ícone (ex: /home/seu_usuario/Imagens/ico.ico): " ICO_PATH

# Verifica se o ícone existe
if [ ! -f "$ICO_PATH" ]; then
    echo ""
    echo "[ERRO] Arquivo de ícone não encontrado: $ICO_PATH"
    echo "Verifique o caminho e tente novamente."
    exit 1
fi

echo ""
echo "[1/4] Criando pasta ~/.local/bin ..."
mkdir -p "$HOME/.local/bin"
echo "      OK"

echo ""
echo "[2/4] Copiando o binário do Arcanum ..."
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BIN_SRC="$HOME/Downloads/Arcanum-linux-x64"

if [ ! -f "$BIN_SRC" ]; then
    echo "      [AVISO] Binário não encontrado em ~/Downloads/Arcanum-linux-x64"
    read -p "      Cole o caminho completo do binário: " BIN_SRC
fi

if [ ! -f "$BIN_SRC" ]; then
    echo "[ERRO] Binário não encontrado. Abortando."
    exit 1
fi

cp "$BIN_SRC" "$HOME/.local/bin/Arcanum"
chmod +x "$HOME/.local/bin/Arcanum"
echo "      OK — instalado em ~/.local/bin/Arcanum"

echo ""
echo "[3/4] Criando atalho no menu de aplicativos ..."
mkdir -p "$HOME/.local/share/applications"

cat > "$HOME/.local/share/applications/arcanum.desktop" <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Arcanum
Comment=Gerenciador seguro de credenciais
Exec=$HOME/.local/bin/Arcanum
Icon=$ICO_PATH
Terminal=false
Categories=Utility;Security;
StartupWMClass=Arcanum
EOF

echo "      OK — atalho criado"

echo ""
echo "[4/4] Atualizando banco de aplicativos ..."
update-desktop-database "$HOME/.local/share/applications/" 2>/dev/null
echo "      OK"

echo ""
echo "=================================================="
echo "   Arcanum instalado com sucesso!"
echo "   Procure por 'Arcanum' no menu de aplicativos."
echo "   Se não aparecer, faça logout e login novamente."
echo "=================================================="
