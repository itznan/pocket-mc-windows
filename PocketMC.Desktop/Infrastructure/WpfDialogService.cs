using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Infrastructure
{
    public class WpfDialogService : IDialogService
    {
        public Task<DialogResult> ShowDialogAsync(string title, string message, DialogType type = DialogType.Information, bool showCancel = false)
        {
            var btn = showCancel ? MessageBoxButton.YesNoCancel : MessageBoxButton.YesNo;
            var img = type switch
            {
                DialogType.Warning => MessageBoxImage.Warning,
                DialogType.Error => MessageBoxImage.Error,
                DialogType.Question => MessageBoxImage.Question,
                _ => MessageBoxImage.Information
            };

            var result = MessageBox.Show(message, title, btn, img);
            return Task.FromResult(result switch
            {
                MessageBoxResult.Yes => DialogResult.Yes,
                MessageBoxResult.No => DialogResult.No,
                MessageBoxResult.Cancel => DialogResult.Cancel,
                _ => DialogResult.Ok
            });
        }

        public void ShowMessage(string title, string message, DialogType type = DialogType.Information)
        {
            var img = type switch
            {
                DialogType.Warning => MessageBoxImage.Warning,
                DialogType.Error => MessageBoxImage.Error,
                DialogType.Question => MessageBoxImage.Question,
                _ => MessageBoxImage.Information
            };

            MessageBox.Show(message, title, MessageBoxButton.OK, img);
        }

        public Task<string?> OpenFolderDialogAsync(string title)
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
        }

        public Task<string?> OpenFileDialogAsync(string title, string filter = "All Files (*.*)|*.*")
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = false };
            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
        }

        public Task<string[]> OpenFilesDialogAsync(string title, string filter = "All Files (*.*)|*.*")
        {
            var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
            return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileNames : System.Array.Empty<string>());
        }
    }
}
