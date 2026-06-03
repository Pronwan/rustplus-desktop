using System;
using System.Windows;
using System.Windows.Input;

namespace RustPlusDesk.Views.Windows
{
    public partial class BaseNoteWindow : Window
    {
        public string? NoteResult { get; private set; }

        public BaseNoteWindow(string? initialNote)
        {
            InitializeComponent();
            ApplyLocalizedText();
            TxtNote.Text = initialNote ?? "";
            TxtNote.Focus();
            if (!string.IsNullOrEmpty(TxtNote.Text))
            {
                TxtNote.Select(TxtNote.Text.Length, 0); // Position cursor at the end
            }
        }

        private static string T(string key, string fallback)
        {
            return RustPlusDesk.Properties.Resources.ResourceManager.GetString(key) ?? fallback;
        }

        private void ApplyLocalizedText()
        {
            Title = T("BaseNoteWindowTitle", "Edit Base Note");
            TxtTitle.Text = T("BaseNoteTitle", "Base Note");
            BtnSave.Content = T("Save", "Save");
            BtnCancel.Content = T("Cancel", "Cancel");
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { DragMove(); } catch { }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            NoteResult = TxtNote.Text;
            DialogResult = true;
            Close();
        }
    }
}
