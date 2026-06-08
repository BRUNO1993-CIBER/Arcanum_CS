using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Arcanum.Core;

namespace Arcanum.UI;

public partial class UnlockWindow : Window
{
    private static readonly string DefaultVaultPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     ".arcanum", "vault.arcanum");

    private string       _vaultPath;
    private VaultStorage _storage;

    public UnlockWindow()
    {
        InitializeComponent();
        _vaultPath = LoadVaultPath();
        _storage   = new VaultStorage(_vaultPath);
        Refresh();
        PbSenha.Focus();
    }

    // --- Helpers vault path ---

    private string LoadVaultPath()
    {
        return AppConfig.Load().LastVault ?? DefaultVaultPath;
    }

    private void SaveVaultPath()
    {
        var cfg = AppConfig.Load();
        cfg.LastVault = _vaultPath;
        AppConfig.Save(cfg);
    }

    private void SetVaultPath(string path)
    {
        _vaultPath = path;
        _storage   = new VaultStorage(path);
        SaveVaultPath();
        Refresh();
    }

    private void Refresh()
    {
        var parts   = _vaultPath.Replace('\\', '/').Split('/');
        var display = parts.Length >= 2
            ? string.Join("/", parts[^2], parts[^1])
            : _vaultPath;
        VaultPathText.Text = display;

        bool isNew               = !_storage.Exists();
        LblModo.Text             = isNew ? "Novo cofre"         : "Desbloquear cofre";
        LblModo.Foreground       = isNew
            ? new SolidColorBrush(Color.FromRgb(0x4A, 0x8B, 0x63))
            : new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB));
        LblSenha.Text            = isNew ? "Criar senha mestre" : "Senha mestre";
        BtnUnlock.Content        = isNew ? "Criar cofre"        : "Entrar";
        PanelConfirmar.IsVisible = isNew;
        TbStatus.Text          = "";
        TbMatch.Text           = "";
        PbSenha.Text           = "";
        PbConfirmar.Text       = "";
        PbSenha.Focus();
    }

    // --- Botões arquivo ---

    private async void OnAbrir(object sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title          = "Abrir vault existente",
                FileTypeFilter = [new FilePickerFileType("Arcanum vault") { Patterns = ["*.arcanum"] }],
                AllowMultiple  = false,
            });
        if (files.Count > 0)
            SetVaultPath(files[0].Path.LocalPath);
    }

    private async void OnNovo(object sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title             = "Criar novo vault",
                FileTypeChoices   = [new FilePickerFileType("Arcanum vault") { Patterns = ["*.arcanum"] }],
                DefaultExtension  = "arcanum",
                SuggestedFileName = "vault.arcanum",
            });
        if (file != null)
            SetVaultPath(file.Path.LocalPath);
    }

    // --- Mostrar/ocultar senha ---

    private void OnToggleSenha(object? _sender, RoutedEventArgs _e)
    {
        bool revealing       = PbSenha.PasswordChar == '\0';
        PbSenha.PasswordChar = revealing ? '•' : '\0';
        BtnShowSenha.Content = revealing ? "👁" : "🙈";
    }

    private void OnToggleConfirmar(object? _sender, RoutedEventArgs _e)
    {
        bool revealing           = PbConfirmar.PasswordChar == '\0';
        PbConfirmar.PasswordChar = revealing ? '•' : '\0';
        BtnShowConfirmar.Content = revealing ? "👁" : "🙈";
    }

    // --- Enter ---

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) OnUnlock(sender, new RoutedEventArgs());
    }

    // --- Unlock / Create ---

    private async void OnUnlock(object sender, RoutedEventArgs e)
    {
        string password = PbSenha.Text ?? "";
        if (string.IsNullOrEmpty(password))
        {
            SetStatus("Informe a senha mestre.", danger: true);
            return;
        }

        bool isNew = !_storage.Exists();

        if (isNew)
        {
            string confirm = PbConfirmar.Text ?? "";
            if (password != confirm)
            {
                SetStatus("As senhas não coincidem.", danger: true);
                return;
            }
            var err = PasswordStrength.ValidatePolicy(password);
            if (err is not null)
            {
                SetStatus(err, danger: true);
                return;
            }
            SetStatus("Criando cofre...", danger: false);
            IsEnabled = false;
            try
            {
                var vault = await Task.Run(() => _storage.CreateNew(password));
                OpenMain(vault, password);
            }
            catch (Exception ex)
            {
                SetStatus($"Erro: {ex.Message}", danger: true);
                IsEnabled = true;
            }
        }
        else
        {
            SetStatus("Autenticando...", danger: false);
            IsEnabled = false;
            try
            {
                var vault = await Task.Run(() => _storage.Load(password));
                OpenMain(vault, password);
            }
            catch (AuthenticationException)
            {
                SetStatus("Senha incorreta ou cofre adulterado.", danger: true);
                IsEnabled = true;
            }
            catch (Exception ex)
            {
                SetStatus($"Erro: {ex.Message}", danger: true);
                IsEnabled = true;
            }
        }
    }

    private void SetStatus(string message, bool danger)
    {
        TbStatus.Foreground = danger
            ? (IBrush?)App.Current?.Resources["DangerBrush"]
            : (IBrush?)App.Current?.Resources["SubtleBrush"];
        TbStatus.Text = message;
    }

    private void OpenMain(Vault vault, string password)
    {
        IsEnabled = true;
        var main = new MainWindow(vault, password, _storage);
        main.OnLocked += () => { Show(); Refresh(); };
        main.Show();
        Hide();
    }

    // --- Validação de senha mestre ---

    private void OnSenhaTextChanged(object? sender, TextChangedEventArgs e)
    {
        var (score, level, label) = PasswordStrength.Evaluate(PbSenha.Text);

        if (string.IsNullOrEmpty(PbSenha.Text))
        {
            StrengthBar.IsVisible = false;
            TbStrength.IsVisible  = false;
            return;
        }

        StrengthBar.Value     = score;
        StrengthBar.IsVisible = true;
        TbStrength.IsVisible  = true;
        TbStrength.Text       = label;

        var color = level switch
        {
            PasswordStrength.Level.Forte => Color.FromRgb(0x4C, 0xAF, 0x50),
            PasswordStrength.Level.Media => Color.FromRgb(0xFF, 0x98, 0x00),
            _                            => Color.FromRgb(0xF4, 0x43, 0x36),
        };
        var brush = new SolidColorBrush(color);
        StrengthBar.Foreground = brush;
        TbStrength.Foreground  = brush;
    }
}
