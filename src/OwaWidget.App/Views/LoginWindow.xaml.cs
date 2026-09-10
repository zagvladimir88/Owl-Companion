using System.Windows;
using OwaWidget.App.Services;
using OwaWidget.Eas;
using OwaWidget.Eas.Commands;
using Wpf.Ui.Controls;

namespace OwaWidget.App.Views;

public partial class LoginWindow : FluentWindow
{
    private readonly AppSettings _settings;
    private readonly AppState _state;

    public LoginWindow(AppSettings settings, AppState state)
    {
        _settings = settings;
        _state = state;

        InitializeComponent();

        ServerBox.Text = settings.Server;
        UserBox.Text = settings.User;
    }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        var server = ServerBox.Text.Trim();
        var user = UserBox.Text.Trim();
        var password = PasswordBox.Password;

        if (server.Length == 0 || user.Length == 0 || password.Length == 0)
        {
            ShowError("Заполните все поля.");
            return;
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var serverUri))
        {
            ShowError("Адрес сервера указан неверно.");
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

            DialogResult = true;
            Close();
        }
        catch (EasHttpException exception) when (exception.IsAuthFailure)
        {
            ShowError("Неверный логин или пароль. Повторных попыток не делаем, чтобы не заблокировать учётную запись.");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void SetBusy(bool busy)
    {
        ConnectButton.IsEnabled = !busy;
        ConnectButton.Content = busy ? "Проверяю…" : "Подключиться";
        ServerBox.IsEnabled = !busy;
        UserBox.IsEnabled = !busy;
        PasswordBox.IsEnabled = !busy;
    }
}