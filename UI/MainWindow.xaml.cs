using System;
using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Arcanum.Core;
using OtpNet;

namespace Arcanum.UI;

public partial class MainWindow : Window
{
    public event Action? OnLocked;

    private Vault        _vault;
    private string       _password;
    private VaultStorage _storage;

    private VaultEntry? _selectedEntry;
    private Button?     _selectedBtn;
    private readonly Dictionary<string, Button> _btnMap = [];

    private string? _totpSeed;
    private bool    _closeConfirmed;

    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _totpRefresh;
    private DateTime _lastActivity = DateTime.UtcNow;
    private int      _lockSeconds  = 60;

    private static readonly (string Label, int Seconds)[] LockOptions =
    [
        ("30 seg", 30),
        ("1 min",  60),
        ("5 min",  300),
        ("15 min", 900),
        ("30 min", 1800),
        ("Nunca",  0),
    ];

    public MainWindow(Vault vault, string password, VaultStorage storage)
    {
        InitializeComponent();
        _vault    = vault;
        _password = password;
        _storage  = storage;

        _idleTimer   = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _totpRefresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        TbUrl.TextChanged += (_, _) =>
        {
            var t = TbUrl.Text?.Trim() ?? "";
            BtnOpenUrl.IsEnabled = t.StartsWith("http://") || t.StartsWith("https://");
        };

        PointerMoved  += (_, _) => _lastActivity = DateTime.UtcNow;
        KeyDown       += (_, e) => { _lastActivity = DateTime.UtcNow; if (e.Key == Avalonia.Input.Key.Escape) DoLock(); };
        PointerPressed += (_, _) => _lastActivity = DateTime.UtcNow;

        _idleTimer.Tick   += OnIdleTick;
        _totpRefresh.Tick += (_, _) => UpdateTotpLive();
        _idleTimer.Start();

        InitLockCombo();
        RefreshList();
    }

    // --- Closing ---

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && HasUnsavedChanges())
        {
            e.Cancel = true;
            bool ok = await Dialog.Confirm(this,
                "Há alterações não aplicadas.\nDeseja fechar sem salvar?", "Fechar");
            if (ok)
            {
                _closeConfirmed = true;
                Close();
            }
            return;
        }
        _idleTimer.Stop();
        _totpRefresh.Stop();
        if (!IsVisible) OnLocked?.Invoke();
        base.OnClosing(e);
    }

    // --- Auto-lock config ---

    private void InitLockCombo()
    {
        foreach (var (label, _) in LockOptions)
            CbLockTime.Items.Add(label);

        _lockSeconds = AppConfig.Load().LockSeconds;
        var idx = Array.FindIndex(LockOptions, o => o.Seconds == _lockSeconds);
        CbLockTime.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void OnLockTimeChanged(object? sender, SelectionChangedEventArgs e)
    {
        var i = CbLockTime.SelectedIndex;
        if (i < 0 || i >= LockOptions.Length) return;
        _lockSeconds = LockOptions[i].Seconds;
        AppConfig.SaveLockSeconds(_lockSeconds);
    }

    // --- Lista de entradas ---

    private static readonly Color[] AvatarColors =
    [
        Color.FromRgb(0x5C, 0x6B, 0xC0), // indigo
        Color.FromRgb(0x42, 0x8B, 0x6B), // verde
        Color.FromRgb(0x8B, 0x42, 0x55), // rosa
        Color.FromRgb(0x7B, 0x5C, 0x8B), // roxo
        Color.FromRgb(0x5C, 0x7B, 0x8B), // azul-aço
        Color.FromRgb(0x8B, 0x7B, 0x42), // âmbar
        Color.FromRgb(0x42, 0x5C, 0x8B), // azul
        Color.FromRgb(0x8B, 0x5C, 0x42), // laranja
    ];

    private static Color GetAvatarColor(string service)
    {
        int hash = service.ToUpperInvariant().Aggregate(0, (acc, c) => acc * 31 + c);
        return AvatarColors[Math.Abs(hash) % AvatarColors.Length];
    }

    private void RefreshList()
    {
        EntryList.Children.Clear();
        _btnMap.Clear();
        _selectedBtn = null;

        var query   = TbSearch.Text ?? "";
        var entries = _vault.Search(query);

        foreach (var entry in entries.OrderBy(e => e.Service, StringComparer.OrdinalIgnoreCase))
        {
            var initial = entry.Service.Length > 0
                ? entry.Service[0].ToString().ToUpperInvariant()
                : "?";

            var circle = new Border
            {
                Width        = 26,
                Height       = 26,
                CornerRadius = new Avalonia.CornerRadius(13),
                Background   = new SolidColorBrush(GetAvatarColor(entry.Service)),
                Child        = new TextBlock
                {
                    Text                = initial,
                    FontSize            = 12,
                    FontWeight          = Avalonia.Media.FontWeight.Bold,
                    Foreground          = Brushes.White,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Center,
                },
            };

            var label = new TextBlock
            {
                Text              = entry.Service,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize          = 13,
            };

            var panel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing     = 10,
                Children    = { circle, label },
            };

            var btn = new Button
            {
                Content = panel,
                Margin  = new Avalonia.Thickness(0, 2, 0, 2),
                Tag     = entry,
            };
            btn.Classes.Add("Entry");
            var e2 = entry;
            btn.Click += (_, _) => SelectEntry(e2);
            EntryList.Children.Add(btn);
            _btnMap[entry.Id] = btn;
        }

        if (_selectedEntry is not null && _btnMap.TryGetValue(_selectedEntry.Id, out var sel))
        {
            _selectedBtn = sel;
            HighlightBtn(sel);
        }
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => RefreshList();

    private void SelectEntry(VaultEntry entry)
    {
        if (_selectedBtn is not null)
            _selectedBtn.Background = Brushes.Transparent;

        _selectedEntry = entry;
        _selectedBtn   = _btnMap.GetValueOrDefault(entry.Id);
        if (_selectedBtn is not null) HighlightBtn(_selectedBtn);

        TbServico.Text = entry.Service;
        TbLogin.Text   = entry.Login;
        TbUrl.Text     = entry.Url;

        PbSenha.Text         = entry.Password;
        PbSenha.PasswordChar = '•';
        BtnShowSenha.Content = "👁";
        UpdateStrengthBar(entry.Password);

        PbTotp.Text         = entry.TotpSeed ?? "";
        PbTotp.PasswordChar = '•';
        BtnShowTotp.Content = "👁";

        TbNotes.Text = entry.Notes;
        TryShowTotpLive(entry.TotpSeed);
    }

    private static void HighlightBtn(Button btn)
        => btn.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C));

    // --- Nova entrada ---

    private void OnNewEntry(object? sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        if (_selectedBtn is not null)
        {
            _selectedBtn.Background = Brushes.Transparent;
            _selectedBtn = null;
        }
        _selectedEntry = null;

        TbServico.Text = "";
        TbLogin.Text   = "";
        TbUrl.Text     = "";

        PbSenha.Text         = "";
        PbSenha.PasswordChar = '•';
        BtnShowSenha.Content = "👁";
        UpdateStrengthBar("");

        PbTotp.Text         = "";
        PbTotp.PasswordChar = '•';
        BtnShowTotp.Content = "👁";

        TbNotes.Text           = "";
        TotpLivePanel.IsVisible = false;
        _totpRefresh.Stop();
    }

    // --- Aplicar ---

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        string service = TbServico.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(service))
        {
            await Dialog.Info(this, "Informe o nome do serviço.", "Atenção");
            return;
        }

        string password = PbSenha.Text ?? "";
        string totp     = PbTotp.Text  ?? "";
        string? totpRaw = string.IsNullOrWhiteSpace(totp) ? null : totp.Trim();

        if (totpRaw is not null)
        {
            try { Base32Encoding.ToBytes(totpRaw.ToUpperInvariant().Replace(" ", "")); }
            catch
            {
                await Dialog.Info(this,
                    "O seed TOTP deve ser Base32 válido (letras A-Z e dígitos 2-7).",
                    "TOTP inválido");
                return;
            }
        }

        if (_selectedEntry is not null)
        {
            _vault.Update(_selectedEntry.Id, en =>
            {
                en.Service  = service;
                en.Login    = TbLogin.Text ?? "";
                en.Url      = TbUrl.Text   ?? "";
                en.Password = password;
                en.TotpSeed = totpRaw;
                en.Notes    = TbNotes.Text?.TrimEnd() ?? "";
            });
        }
        else
        {
            var entry = new VaultEntry
            {
                Service  = service,
                Login    = TbLogin.Text ?? "",
                Url      = TbUrl.Text   ?? "",
                Password = password,
                TotpSeed = totpRaw,
                Notes    = TbNotes.Text?.TrimEnd() ?? "",
            };
            _vault.Add(entry);
            _selectedEntry = entry;
        }

        Save();
        RefreshList();

        BtnAplicar.Content = "✓ Salvo";
        await Task.Delay(1_500);
        if (IsVisible) BtnAplicar.Content = "Aplicar";
    }

    // --- Excluir ---

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null) return;
        bool ok = await Dialog.Confirm(this,
            $"Excluir '{_selectedEntry.Service}'?", "Confirmar");
        if (!ok) return;

        _vault.Delete(_selectedEntry.Id);
        _selectedEntry = null;
        Save();
        ClearForm();
        RefreshList();
    }

    // --- Senha show/hide ---

    private void OnToggleSenha(object? _sender, RoutedEventArgs _e)
    {
        bool revealing       = PbSenha.PasswordChar == '\0';
        PbSenha.PasswordChar = revealing ? '•' : '\0';
        BtnShowSenha.Content = revealing ? "👁" : "🙈";
    }

    // --- Copiar senha ---

    private async void OnCopySenha(object? sender, RoutedEventArgs e)
    {
        string pw = PbSenha.Text ?? "";
        if (string.IsNullOrEmpty(pw)) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(pw);
        BtnCopiarSenha.Content = "✓";
        await Task.Delay(1_500);
        if (IsVisible) BtnCopiarSenha.Content = "Copiar";

        await Task.Delay(28_500);
        try { await clipboard.ClearAsync(); } catch { }
    }

    // --- Gerar senha ---

    private void OnGenerateSenha(object? _sender, RoutedEventArgs _e)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()-_=+";
        var buf = new byte[24];
        RandomNumberGenerator.Fill(buf);
        PbSenha.Text = new string(buf.Select(b => chars[b % chars.Length]).ToArray());
    }

    private void OnSenhaTextChanged(object? sender, TextChangedEventArgs e)
        => UpdateStrengthBar(PbSenha.Text);

    private void UpdateStrengthBar(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            StrengthBar.IsVisible = false;
            TbStrength.IsVisible  = false;
            return;
        }

        var (score, level, label) = PasswordStrength.Evaluate(password);

        StrengthBar.Value      = score;
        StrengthBar.IsVisible  = true;
        TbStrength.IsVisible   = true;
        TbStrength.Text        = label;

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

    // --- TOTP show/hide ---

    private void OnToggleTotp(object? _sender, RoutedEventArgs _e)
    {
        bool revealing      = PbTotp.PasswordChar == '\0';
        PbTotp.PasswordChar = revealing ? '•' : '\0';
        BtnShowTotp.Content = revealing ? "👁" : "🙈";
    }

    // --- TOTP live ---

    private void OnTotpChanged(object? sender, TextChangedEventArgs e)
    {
        string seed = (PbTotp.Text ?? "").Trim().ToUpperInvariant().Replace(" ", "");
        TryShowTotpLive(string.IsNullOrEmpty(seed) ? null : seed);
    }

    private void TryShowTotpLive(string? seed)
    {
        _totpRefresh.Stop();
        if (seed is null || !IsValidBase32(seed))
        {
            TotpLivePanel.IsVisible = false;
            return;
        }
        _totpSeed               = seed;
        TotpLivePanel.IsVisible = true;
        UpdateTotpLive();
        _totpRefresh.Start();
    }

    private void UpdateTotpLive()
    {
        if (_totpSeed is null) return;
        try
        {
            var totp = new Totp(Base32Encoding.ToBytes(_totpSeed));
            string code = totp.ComputeTotp();
            int secs    = totp.RemainingSeconds();
            TbTotpCode.Text = $"{code[..3]} {code[3..]}";
            TbTotpSecs.Text = $"{secs}s";
            TotpBar.Value   = secs / 30.0 * 100;
        }
        catch { TotpLivePanel.IsVisible = false; _totpRefresh.Stop(); }
    }

    private async void OnCopyTotp(object? sender, RoutedEventArgs e)
    {
        if ((TbTotpCode.Text?.Length ?? 0) < 6) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(TbTotpCode.Text!.Replace(" ", ""));
        if (sender is Button btn)
        {
            btn.Content = "✓";
            await Task.Delay(1_500);
            if (IsVisible) btn.Content = "Copiar";
        }
    }

    private static bool IsValidBase32(string s)
    {
        try { Base32Encoding.ToBytes(s); return true; }
        catch { return false; }
    }

    // --- Abrir URL ---

    private void OnOpenUrl(object? sender, RoutedEventArgs e)
    {
        var url = TbUrl.Text?.Trim() ?? "";
        if (!url.StartsWith("http://") && !url.StartsWith("https://")) return;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    // --- Trocar senha mestre ---

    private async void OnChangeMasterPassword(object? sender, RoutedEventArgs e)
    {
        var dlg = new ChangePasswordWindow(_storage, _vault, _password);
        await dlg.ShowDialog(this);
        if (dlg.NewPassword is not null)
            _password = dlg.NewPassword;
    }

    // --- Lock ---

    private void OnLock(object? sender, RoutedEventArgs e) => DoLock();

    private void DoLock()
    {
        _idleTimer.Stop();
        _totpRefresh.Stop();
        OnLocked?.Invoke();
        Close();
    }

    // --- Idle lock ---

    private void OnIdleTick(object? sender, EventArgs e)
    {
        if (_lockSeconds > 0 && (DateTime.UtcNow - _lastActivity).TotalSeconds >= _lockSeconds)
            DoLock();
    }

    // --- Unsaved check ---

    private bool HasUnsavedChanges()
    {
        if (_selectedEntry is null) return false;
        return TbServico.Text != _selectedEntry.Service
            || TbLogin.Text   != _selectedEntry.Login
            || TbUrl.Text     != _selectedEntry.Url
            || (PbSenha.Text  ?? "") != _selectedEntry.Password
            || (PbTotp.Text   ?? "") != (_selectedEntry.TotpSeed ?? "")
            || (TbNotes.Text?.TrimEnd() ?? "") != _selectedEntry.Notes;
    }

    // --- Salvar ---

    private async void Save()
    {
        try { _storage.Save(_vault, _password); }
        catch (Exception ex)
        {
            await Dialog.Info(this, $"Erro ao salvar: {ex.Message}", "Erro");
        }
    }
}
