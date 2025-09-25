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
            _items.Add(n);
            if (_items.Count > 0) List.ScrollIntoView(_items[^1]);
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e) => _items.Clear();
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}