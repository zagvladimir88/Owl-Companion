using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using OwaWidget.App.Services;
using OwaWidget.Eas;
using OwaWidget.Eas.Commands;
using Wpf.Ui.Controls;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace OwaWidget.App.Views;

public partial class LoginWindow : FluentWindow
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly AppSettings _settings;
    private readonly AppState _state;
    private readonly bool _firstRun;

    private bool _revealed;
    private bool _done;
    private bool _passwordError;
    private string _idleLabel = "Подключиться";

    public LoginWindow(AppSettings settings, AppState state)
    {
        _settings = settings;
        _state = state;
        _firstRun = settings.User.Length == 0;

        InitializeComponent();

        BrandText.Text = Tracking.Wide("OWL");
        ServerBox.Text = settings.Server;
        UserBox.Text = settings.User;

        PasswordHint.Text = _firstRun ? "Пароль от почты" : "Новый пароль от почты";

        if (_firstRun)
        {
            HeadingText.Text = "Корпоративная почта";
            LeadText.Text = "Owl показывает встречи и письма в трее. Для этого нужен доступ к вашему почтовому ящику.";
        }
        else
        {
            HeadingText.Text = "Пароль больше не подходит";
            LeadText.Visibility = Visibility.Collapsed;

            ShowAlert(
                error: false,
                title: string.Empty,
                detail: "Сохранённый пароль перестал приниматься — обычно это значит, что он изменился в корпоративной системе. Данные не потеряны, синхронизация продолжится после входа.",
                note: LastSyncNote());

            _passwordError = true;
            PaintField(PasswordField, focused: false, error: true);
        }

        ServerBox.TextChanged += (_, _) => UpdateHints();
        UserBox.TextChanged += (_, _) => UpdateHints();
        PasswordBox.PasswordChanged += (_, _) => UpdateHints();
        PasswordPlain.TextChanged += (_, _) => UpdateHints();

        Wire(ServerBox, ServerField);
        Wire(UserBox, UserField);
        Wire(PasswordPlain, PasswordField);

        PasswordBox.GotFocus += (_, _) => { PaintField(PasswordField, focused: true, error: false); UpdateCaps(); };
        PasswordBox.LostFocus += (_, _) => { PaintField(PasswordField, focused: false, error: false); UpdateCaps(); };
        PasswordBox.PreviewKeyDown += OnPasswordKey;
        PasswordBox.PreviewKeyUp += OnPasswordKey;
        PasswordPlain.PreviewKeyDown += OnPasswordKey;
        PasswordPlain.PreviewKeyUp += OnPasswordKey;
        PasswordPlain.GotFocus += (_, _) => UpdateCaps();
        PasswordPlain.LostFocus += (_, _) => UpdateCaps();

        UpdateHints();
        Loaded += (_, _) => (_firstRun ? (System.Windows.Controls.Control)UserBox : PasswordBox).Focus();
    }

    private void Wire(TextBox input, System.Windows.Controls.Border field)
    {
        input.GotFocus += (_, _) => PaintField(field, focused: true, error: false);
        input.LostFocus += (_, _) => PaintField(field, focused: false, error: false);
    }

    private static string LastSyncNote()
    {
        var stamps = new[] { CacheStore.MailPath, CacheStore.CalendarPath }
            .Where(File.Exists)
            .Select(File.GetLastWriteTime)
            .ToList();

        if (stamps.Count == 0)
        {
            return string.Empty;
        }

        var last = stamps.Max();
        var day = last.Date == DateTime.Today
            ? "сегодня"
            : last.Date == DateTime.Today.AddDays(-1)
                ? "вчера"
                : last.ToString("d MMMM", Russian);

        return $"Последняя успешная синхронизация — {day} в {last:HH:mm}.";
    }

    private void UpdateHints()
    {
        ServerHint.Visibility = ServerBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        UserHint.Visibility = UserBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        var empty = _revealed ? PasswordPlain.Text.Length == 0 : PasswordBox.Password.Length == 0;
        PasswordHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPasswordKey(object sender, KeyEventArgs e) => UpdateCaps();

    private void UpdateCaps()
    {
        var on = Keyboard.IsKeyToggled(Key.CapsLock) &&
                 (PasswordBox.IsKeyboardFocusWithin || PasswordPlain.IsKeyboardFocusWithin);

        CapsWarning.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggleReveal(object sender, MouseButtonEventArgs e)
    {
        _revealed = !_revealed;

        if (_revealed)
        {
            PasswordPlain.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordPlain.Visibility = Visibility.Visible;
            PasswordPlain.CaretIndex = PasswordPlain.Text.Length;
            PasswordPlain.Focus();
        }
        else
        {
            PasswordBox.Password = PasswordPlain.Text;
            PasswordPlain.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordBox.Focus();
        }

        RevealGlyph.SetResourceReference(
            System.Windows.Shapes.Path.DataProperty,
            _revealed ? "EyeOffIcon" : "EyeIcon");

        RevealButton.ToolTip = _revealed ? "Скрыть пароль" : "Показать пароль";
    }

    private string CurrentPassword => _revealed ? PasswordPlain.Text : PasswordBox.Password;

    private void PaintField(System.Windows.Controls.Border field, bool focused, bool error)
    {
        if (error)
        {
            field.BorderBrush = Palette.Accent;
            field.BorderThickness = new Thickness(1);
            return;
        }

        if (focused)
        {
            field.BorderBrush = Palette.Accent;
            field.BorderThickness = new Thickness(2);
            field.Padding = field == PasswordField
                ? new Thickness(10, 0, 6, 0)
                : new Thickness(10, 0, 10, 0);

            return;
        }

        field.ClearValue(System.Windows.Controls.Border.BorderBrushProperty);
        field.ClearValue(System.Windows.Controls.Border.BorderThicknessProperty);
        field.ClearValue(System.Windows.Controls.Border.PaddingProperty);
    }

    private void ShowAlert(bool error, string title, string detail, string note)
    {
        AlertCard.Visibility = Visibility.Visible;
        AlertCard.Background = error ? Palette.ErrorTint : Palette.Card;
        AlertCard.BorderBrush = error ? Palette.ErrorLine : Palette.Line;

        AlertGlyph.Stroke = error ? Palette.Accent : Palette.Tertiary;
        AlertGlyph.SetResourceReference(
            System.Windows.Shapes.Path.DataProperty,
            error ? "AlertIcon" : "InfoIcon");

        AlertTitle.Text = title;
        AlertTitle.Visibility = title.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        AlertDetail.Text = detail;
        AlertDetail.Margin = new Thickness(0, title.Length == 0 ? 0 : 4, 0, 0);

        AlertNote.Text = note;
        AlertNote.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        if (_done)
        {
            DialogResult = true;
            Close();
            return;
        }

        _passwordError = false;

        var server = ServerBox.Text.Trim();
        var user = UserBox.Text.Trim();
        var password = CurrentPassword;

        if (server.Length == 0 || user.Length == 0 || password.Length == 0)
        {
            ShowAlert(true, "Заполните все поля", "Сервер, логин и пароль нужны все три.", string.Empty);
            return;
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var serverUri))
        {
            ShowAlert(true, "Адрес сервера указан неверно", "Ожидается адрес вида https://owa.example.ru.", string.Empty);
            return;
        }

        SetBusy(true);

        try
        {
            using var client = new EasClient(new EasOptions
            {
                ServerUri = serverUri,
                User = user,
                Password = password,
                DeviceId = _state.DeviceId,
                DeviceType = "OwaWidget",
                ProtocolVersion = "14.1",
                UserAgent = "OwaWidget/1.0"
            });

            var options = new EasOptions
            {
                ServerUri = serverUri,
                User = user,
                Password = password,
                DeviceId = _state.DeviceId
            };

            var policyKey = await ProvisionCommand.AcquirePolicyKeyAsync(client, options);
            var folders = await FolderSyncCommand.ExecuteAsync(client);

            _settings.Server = server;
            _settings.User = user;
            _state.PolicyKey = policyKey;
            _state.FolderSyncKey = folders.SyncKey;
            _state.InboxId = folders.Changed
                .FirstOrDefault(f => f.Type == Eas.Models.EasFolderType.Inbox)?.ServerId;
            _state.CalendarId = folders.Changed
                .FirstOrDefault(f => f.Type == Eas.Models.EasFolderType.Calendar)?.ServerId;
            _state.MailSyncKey = "0";
            _state.CalendarSyncKey = "0";

            CredentialStore.Save(server, user, password);
            AppStorage.Save(_settings);
            AppStorage.Save(_state);

            if (_firstRun)
            {
                ShowDone();
                return;
            }

            DialogResult = true;
            Close();
        }
        catch (EasHttpException exception) when (exception.IsAuthFailure)
        {
            ShowAlert(
                true,
                "Неверный логин или пароль",
                "Сервер ответил отказом. Проверьте раскладку и формат логина — попытка была одна.",
                string.Empty);

            _passwordError = true;
            _idleLabel = "Попробовать снова";
        }
        catch (Exception exception)
        {
            ShowAlert(true, "Не удалось подключиться", exception.Message, string.Empty);
            _idleLabel = "Попробовать снова";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowDone()
    {
        _done = true;

        var now = DateTime.Now;
        TrayClock.Text = now.ToString("HH:mm");
        TrayDate.Text = now.ToString("dd.MM.yyyy");

        HeadingGlyph.Visibility = Visibility.Visible;
        HeadingText.Text = "Подключено";
        LeadText.Visibility = Visibility.Visible;
        LeadText.Text = "Загружаем встречи и письма — обычно это несколько секунд.";

        AlertCard.Visibility = Visibility.Collapsed;
        TrustCard.Visibility = Visibility.Collapsed;
        FormBlock.Visibility = Visibility.Collapsed;
        DoneBlock.Visibility = Visibility.Visible;

        CancelButton.Visibility = Visibility.Collapsed;
        _idleLabel = "Открыть Owl";
        ConnectText.Text = _idleLabel;
        ConnectButton.Focus();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = _done;
        Close();
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void SetBusy(bool busy)
    {
        ConnectButton.IsEnabled = !busy;
        ConnectText.Text = busy ? "Проверяем…" : _idleLabel;
        BusyText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        ServerBox.IsEnabled = !busy;
        UserBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;
        PasswordPlain.IsEnabled = !busy;
        RevealButton.IsEnabled = !busy;

        foreach (var input in new System.Windows.Controls.Control[] { ServerBox, UserBox, PasswordBox, PasswordPlain })
        {
            input.Foreground = busy ? Palette.Faint : Palette.Ink;
        }

        foreach (var field in new[] { ServerField, UserField, PasswordField })
        {
            field.Background = busy ? Palette.Surface : Palette.Card;

            if (busy)
            {
                field.BorderBrush = Palette.Divider;
                field.BorderThickness = new Thickness(1);
            }
            else
            {
                PaintField(field, focused: false, error: field == PasswordField && _passwordError);
            }
        }
    }
}