#!/usr/bin/env bash
# Verifica a integridade do binário Arcanum baixado do release oficial.
# Uso: bash verificar.sh <caminho-do-binario>
# Exemplo: bash verificar.sh ~/Downloads/Arcanum-linux-x64

set -euo pipefail

BINARIO="${1:-}"

if [[ -z "$BINARIO" ]]; then
    echo "Uso: bash verificar.sh <caminho-do-binario>"
    echo "Exemplo: bash verificar.sh ~/Downloads/Arcanum-linux-x64"
    exit 1
fi

if [[ ! -f "$BINARIO" ]]; then
    echo "Erro: arquivo não encontrado: $BINARIO"
    exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUMS_FILE="$SCRIPT_DIR/SHA256SUMS"

if [[ ! -f "$SUMS_FILE" ]]; then
    echo "Erro: SHA256SUMS não encontrado em $SCRIPT_DIR"
    echo "Baixe o arquivo SHA256SUMS do mesmo release e coloque na mesma pasta que este script."
    exit 1
fi

HASH_ESPERADO=$(grep "Arcanum-linux-x64" "$SUMS_FILE" | awk '{print $1}')
HASH_ATUAL=$(sha256sum "$BINARIO" | awk '{print $1}')

echo ""
echo "Arquivo:        $BINARIO"
echo "Hash esperado:  $HASH_ESPERADO"
echo "Hash atual:     $HASH_ATUAL"
echo ""

if [[ "$HASH_ATUAL" == "$HASH_ESPERADO" ]]; then
    echo "✓ Binário íntegro — corresponde ao release oficial."
else
    echo "✗ ATENÇÃO: hash não confere. O arquivo pode ter sido corrompido ou substituído."
    echo "  Baixe novamente em: https://github.com/BRUNO1993-CIBER/ARCANUM_CS/releases"
fi
echo ""
