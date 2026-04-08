using System;
using System.Threading.Tasks;
using System.Windows;
using PocketMC.Desktop.Core.Interfaces;

namespace PocketMC.Desktop.Infrastructure
{
    public class WpfAppDispatcher : IAppDispatcher
    {
        public void Invoke(Action action)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(action);
            }
            else
            {
                action();
            }
        }

        public async Task InvokeAsync(Func<Task> action)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                await Application.Current.Dispatcher.InvokeAsync(action);
            }
            else
            {
                await action();
            }
        }

        public async Task InvokeAsync(Action action)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                await Application.Current.Dispatcher.InvokeAsync(action);
            }
            else
            {
                action();
            }
        }
    }
}
