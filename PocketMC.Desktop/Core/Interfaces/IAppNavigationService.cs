using System;

namespace PocketMC.Desktop.Core.Interfaces
{
    public interface IAppNavigationService
    {
        bool NavigateToDashboard();
        bool NavigateToDetailPage(System.Windows.Controls.Page page, string breadcrumbLabel);
        bool NavigateBack();
    }
}