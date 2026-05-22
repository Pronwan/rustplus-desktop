using System;
using RustPlusDesk.Views;

namespace RustPlusDesk.Modification
{
    public interface IMod
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        bool IsEnabled { get; set; }

        void Initialize(MainWindow mainWindow);
        void OnChatReceived(string text, string author, ulong steamId);
        void OnDeviceStateChanged(uint entityId, bool isOn, string kind);
        System.Windows.FrameworkElement? GetConfigUI();
    }
}
