using System;
using System.Windows;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views
{
    /// <summary>Premium modal for entering a camera identifier.</summary>
    public partial class AddCameraDialog : WpfUi.FluentWindow
    {
        /// <summary>The identifier the user entered (empty until confirmed).</summary>
        public string CameraId { get; private set; } = string.Empty;

        public AddCameraDialog()
        {
            InitializeComponent();
            Loaded += (_, __) => { TxtId.Focus(); };
        }

        /// <summary>Shows the dialog and returns the entered id, or null if cancelled.</summary>
        public static string? Prompt(Window owner)
        {
            var dlg = new AddCameraDialog { Owner = owner };
            return dlg.ShowDialog() == true ? dlg.CameraId : null;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var id = (TxtId.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                Err.Message = "Please enter a camera identifier.";
                Err.IsOpen = true;
                TxtId.Focus();
                return;
            }

            CameraId = id;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
