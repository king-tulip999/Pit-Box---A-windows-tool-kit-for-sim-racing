using System.Windows;

namespace Core.Views;

internal interface IModalHost
{
    double ModalRoom { get; }
    Task<bool> ShowModal(FrameworkElement content);
    void CloseModal(bool result);
    Task<bool> Confirm(string title, string message, string confirmText, bool danger = false);
}
