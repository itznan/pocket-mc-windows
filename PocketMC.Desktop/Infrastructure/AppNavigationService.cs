using System;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Infrastructure
{
    public class AppNavigationService : IAppNavigationService
    {
        private MainWindow? _mainWindow;

        public void Initialize(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public bool NavigateToDashboard()
        {
            return _mainWindow?.NavigateToDashboard() ?? false;
        }

        public bool NavigateToDetailPage(Type pageType, object? parameter = null, string? breadcrumbLabel = null)
        {
            // Note: Wpf.Ui Navigate method on INavigationService doesn't perfectly align with passing already-instantiated objects
            // unless we use ReplaceContent or similar custom methods in MainWindow.
            // Our MainWindow has `NavigateToDetailPage(Page page, string breadcrumbLabel)`
            throw new NotImplementedException("Use NavigateToDetailPage(Page, string) instead for now.");
        }

        // Let's add the specific override
        public bool NavigateToDetailPage(System.Windows.Controls.Page page, string breadcrumbLabel)
        {
            return _mainWindow?.NavigateToDetailPage(page, breadcrumbLabel) ?? false;
        }

        public bool NavigateBack()
        {
            return _mainWindow?.NavigateBackFromDetail() ?? false;
        }
    }
}
