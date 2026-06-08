using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Arcanum.Core;

namespace Arcanum.UI;

public partial class ChangePasswordWindow : Window
{
    private readonly VaultStorage _storage;
    private readonly Vault        _vault;
    private readonly string       _currentPassword;

    public string? NewPassword { get; private set; }

    public ChangePasswordWindow(VaultStorage storage, Vault vault, string currentPassword)
    {
        InitializeComponent();
        _storage         = storage;
        _vault           = vault;
        _currentPassword = currentPassword;

        TbStatus.Text = "Mínimo 12 caracteres, maiúscula, minúscula, número e símbolo.";
        PbOld.Focus();

        KeyDown += (_, e) => { if (e.Key == Key.Return) OnSubmit(this, new RoutedEventArgs()); };
    }

    // --- Feedback em tempo real ---

    private void OnPasswordChanged(object? sender, TextChangedEventArgs e)
    {
        string pw = PbNew.Text     ?? "";
        string cf = PbConfirm.Text ?? "";

        if (string.IsNullOrEmpty(cf))
        {
            SetStatus("Mínimo 12 caracteres, maiúscula, minúscula, número e símbolo.", danger: false);
            return;
        }

        if (pw == cf)
            SetStatus("✓ Senhas conferem", danger: false, success: true);
        else
            SetStatus("✗ Senhas não conferem", danger: true);
    }

    // --- Toggle show/hide (funcional em Avalonia via PasswordChar) ---

    private void OnToggleOld(object? _s, RoutedEventArgs _e)     => ToggleBox(PbOld,     BtnShowOld);
    private void OnToggleNew(object? _s, RoutedEventArgs _e)     => ToggleBox(PbNew,     BtnShowNew);
    private void OnToggleConfirm(object? _s, RoutedEventArgs _e) => ToggleBox(PbConfirm, BtnShowConfirm);

    private static void ToggleBox(TextBox pb, Button btn)
    {
        bool revealing = pb.PasswordChar == '\0';
        pb.PasswordChar = revealing ? '•' : '\0';
        btn.Content     = revealing ? "👁" : "🙈";
    }

    // --- Submit ---

    private async void OnSubmit(object? sender, RoutedEventArgs e)
    {
        string old     = PbOld.Text     ?? "";
        string @new    = PbNew.Text     ?? "";
        string confirm = PbConfirm.Text ?? "";

        if (string.IsNullOrEmpty(old))
        {
            SetStatus("Informe a senha atual.", danger: true);
            return;
        }

        string? err = PasswordStrength.ValidatePolicy(@new);
        if (err is not null) { SetStatus(err, danger: true); return; }

        if (@new != confirm) { SetStatus("✗ Senhas não conferem.", danger: true); return; }

        SetStatus("Alterando...", danger: false);
        IsEnabled = false;

        try
        {
            await Task.Run(() => _storage.ChangePassword(_vault, old, @new));
            NewPassword = @new;
            SetStatus("✓ Senha alterada com sucesso!", danger: false, success: true);
            await Task.Delay(1_500);
            Close();
        }
        catch (AuthenticationException)
        {
            SetStatus("Senha atual incorreta.", danger: true);
            IsEnabled = true;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, danger: true);
            IsEnabled = true;
        }
    }

    // --- Helpers ---

    private void SetStatus(string text, bool danger, bool success = false)
    {
        TbStatus.Text = text;
        TbStatus.Foreground = danger
            ? (IBrush?)App.Current?.Resources["DangerBrush"]
            : success
                ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50))
                : (IBrush?)App.Current?.Resources["SubtleBrush"];
    }

}
