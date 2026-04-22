namespace PocketMC.Desktop.Features.Shell.Interfaces
{
    public interface IStartupShellHost
    {
        void ShowRootDirectorySetup();
        void CompleteRootDirectorySetup();
        void RequestMicaUpdate();
        bool NavigateToDashboard();
        bool NavigateToTunnel();
        void ShowError(string title, string message);
        void ShutdownApplication();
    }
}

