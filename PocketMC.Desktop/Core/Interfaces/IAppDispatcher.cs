using System;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Core.Interfaces
{
    public interface IAppDispatcher
    {
        void Invoke(Action action);
        Task InvokeAsync(Func<Task> action);
        Task InvokeAsync(Action action);
    }
}
