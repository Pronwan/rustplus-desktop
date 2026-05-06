using System.Collections.ObjectModel;
using System.Windows;
using RustPlusDesk.Models;

namespace RustPlusDesk.Views
{
    public partial class AlarmWindow : Window
    {
        private readonly ObservableCollection<AlarmNotification> _items = new();

        public AlarmWindow()
        {
            InitializeComponent();
            List.ItemsSource = _items;
        }

        public void Add(AlarmNotification n)
        {
            _items.Insert(0, n);
            // Optional: Scroll zum neuesten Element am Anfang
            List.ScrollIntoView(_items[0]);
        }

        public void UpdateOrAdd(AlarmNotification n)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                // Wir aktualisieren nur Einträge, die noch die Standard-Nachricht haben
                if (_items[i].Message == "Alarm activated!" && _items[i].Server == n.Server)
                {
                    // Match, wenn IDs identisch ODER wenn einer von beiden keine ID hat (Fuzzy-Match für FCM ohne ID)
                    if (_items[i].EntityId == n.EntityId || _items[i].EntityId == null || n.EntityId == null)
                    {
                        _items[i] = n;
                        return;
                    }
                }
            }
            Add(n);
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                DragMove();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e) => _items.Clear();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}