using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Infrastructure;

public sealed class WindowsToastNotificationService : INotificationService
{
    private const string AppUserModelId = "PocketMC.Desktop";
    private static bool _isRegistered;
    private readonly ILogger<WindowsToastNotificationService> _logger;

    public WindowsToastNotificationService(ILogger<WindowsToastNotificationService> logger)
    {
        _logger = logger;
    }

    public static void RegisterApplication()
    {
        if (_isRegistered) return;

        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        ToastNotificationManagerCompat.OnActivated += _ => { };
        _isRegistered = true;
    }

    public void ShowAgentConnected()
    {
        ShowToast("Agent connected", "Your Playit Agent is Ready.");
    }

    public void ShowTunnelCreated(int serverPort, string address)
    {
        ShowToast("Tunnel created", $"Port {serverPort} is now publicly accessible on {address}. You can now close the browser window.");
    }

    public void ShowInformation(string title, string message)
    {
        ShowToast(title, message);
    }

    private void ShowToast(string title, string body)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show Windows toast notification '{Title}'.", title);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
