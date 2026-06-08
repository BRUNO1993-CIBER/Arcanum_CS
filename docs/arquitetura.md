# Arcanum — Estrutura do Projeto e Dependências

## Estrutura do Projeto

```
ARCANUM_CS/
├── Arcanum.csproj              # Projeto Avalonia .NET 10 — NuGet: Argon2 + OtpNet + Avalonia 12
├── App.xaml                    # Paleta de cores, estilos de botão (Primary/Success/Danger/Secondary/Entry)
├── App.xaml.cs                 # Startup — inicialização do app cross-platform (Windows e Linux)
├── Assets/
│   ├── ico.ico                 # Ícone da aplicação (taskbar + janela)
│   └── logo.png                # Logo exibido na tela de desbloqueio
├── Core/
│   ├── AppConfig.cs            # Leitura/escrita de ~/.arcanum/config.json (último vault + tempo de lock)
│   ├── Crypto.cs               # Argon2id + AES-256-GCM + HKDF — zero dependência de UI
│   ├── PasswordStrength.cs     # Utilitário puro de força de senha (Score 0–100, nível Fraca/Média/Forte)
│   ├── Vault.cs                # Modelos de dados (VaultEntry, Vault) + CRUD + JSON
│   └── Storage.cs              # I/O do .arcanum com write atômico + change_password
└── UI/
    ├── UnlockWindow.xaml/.cs   # Tela de login: layout 2 colunas (logo + formulário), desbloqueio e criação
    ├── MainWindow.xaml/.cs     # Janela principal: lista + formulário + TOTP live + auto-lock configurável
    └── ChangePasswordWindow.xaml/.cs  # Dialog de troca de senha mestre
```

**Princípios de separação:**
- `Core/` não importa nada de `UI/` — a camada criptográfica é completamente testável sem interface gráfica
- `App.xaml` é a única fonte de verdade para cores e estilos — mudar o visual do app inteiro é editar um único arquivo
- `ChangePasswordWindow` não conhece os detalhes internos do vault — recebe os objetos prontos, segue o princípio de responsabilidade única
- `Assets/` centraliza recursos estáticos separados do código C#

---

## Dependências

```xml
<!-- NuGet -->
Konscious.Security.Cryptography.Argon2  1.3.1   # Argon2id
OtpNet                                  1.4.0   # Geração de códigos TOTP
Avalonia                                12.0.4  # Framework UI cross-platform (Windows + Linux)
Avalonia.Desktop                        12.0.4  # Backend nativo desktop

<!-- .NET 10 nativo (sem NuGet) -->
System.Security.Cryptography            # AES-GCM, HKDF, RandomNumberGenerator
System.IO.Compression.ZLibStream        # Compressão zlib
System.Text.Json                        # Serialização JSON
```

---

## Notas técnicas para desenvolvedores

- Avalonia 12 processa `.axaml` por padrão; usamos `.xaml` com declaração explícita `<AvaloniaXaml>` no `.csproj`
- `PasswordBox` não existe no Avalonia — usamos `TextBox` com `PasswordChar='•'` e toggle via `'\0'`
- Clipboard: `using Avalonia.Input.Platform` é obrigatório para `SetTextAsync` (é extension method)
- Diálogos modais: `await dlg.ShowDialog(this)` — sem `DialogResult`, usa propriedades públicas
- `OnClosing` assíncrono: padrão `_closeConfirmed` para evitar loop infinito
- `DockPanel` com `Dock="Bottom"`: visual de cima para baixo é a ordem INVERSA da declaração XAML — último declarado fica mais próximo da área fill
