# Arcanum — Manual de Instalação (Linux)

## Pré-requisitos

- Linux (Ubuntu, Mint, Debian ou derivados)
- Binário `Arcanum-linux-x64` baixado (disponível na página de releases)
- Um arquivo de ícone `.ico` ou `.png` salvo no seu computador

---

## Instalação rápida via script

### Passo 1 — Baixe o binário

Acesse a página de releases e baixe o arquivo `Arcanum-linux-x64`.  
Salve em `~/Downloads/`.

---

### Passo 2 — Abra o terminal na pasta do projeto

Clique com o botão direito na pasta `ARCANUM_CS` e escolha **"Abrir no Terminal"**.  
Ou execute:

```bash
cd ~/Documentos/ARCANUM_CS
```

---

### Passo 3 — Dê permissão ao script

```bash
chmod +x install_linux.sh
```

---

### Passo 4 — Execute o instalador

```bash
./install_linux.sh
```

O script vai perguntar o caminho do seu ícone. Exemplo:

```
Cole o caminho completo do ícone: /home/seu_usuario/Imagens/ico.ico
```

Digite o caminho e pressione **Enter**. O script fará tudo automaticamente.

---

## O que o instalador faz

| Etapa | Ação |
|-------|------|
| 1/4 | Cria a pasta `~/.local/bin` se não existir |
| 2/4 | Copia o binário para `~/.local/bin/Arcanum` e dá permissão de execução |
| 3/4 | Cria o atalho `.desktop` no menu de aplicativos com o ícone escolhido |
| 4/4 | Atualiza o banco de dados de aplicativos do sistema |

---

## Instalação manual (sem o script)

Se preferir fazer passo a passo no terminal:

```bash
# 1. Copiar o binário
mkdir -p ~/.local/bin
cp ~/Downloads/Arcanum-linux-x64 ~/.local/bin/Arcanum
chmod +x ~/.local/bin/Arcanum

# 2. Criar o atalho (substitua o caminho do ícone)
cat > ~/.local/share/applications/arcanum.desktop <<EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Arcanum
Comment=Gerenciador seguro de credenciais
Exec=$HOME/.local/bin/Arcanum
Icon=/CAMINHO/DO/SEU/ICONE.ico
Terminal=false
Categories=Utility;Security;
StartupWMClass=Arcanum
EOF

# 3. Atualizar o menu
update-desktop-database ~/.local/share/applications/
```

---

## Arcanum não aparece no menu após instalação?

Faça **logout** e **login** novamente, ou execute no terminal:

```bash
update-desktop-database ~/.local/share/applications/
```

---

## Desinstalar

```bash
rm ~/.local/bin/Arcanum
rm ~/.local/share/applications/arcanum.desktop
update-desktop-database ~/.local/share/applications/
```
