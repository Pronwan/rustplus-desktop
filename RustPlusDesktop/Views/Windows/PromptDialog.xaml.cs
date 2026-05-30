using System.Windows;

namespace RustPlusDesk.Views.Windows
{
    public partial class PromptDialog : Window
    {
        public string DialogTitle { get; set; }
        public string InputText { get; set; }

        public PromptDialog(string title, string defaultText)
        {
            DialogTitle = title;
            InputText = defaultText;
            DataContext = this;
            InitializeComponent();
            TxtInput.Focus();
            TxtInput.SelectAll();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
