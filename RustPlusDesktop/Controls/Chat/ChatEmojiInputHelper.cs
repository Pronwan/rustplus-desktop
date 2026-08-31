using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using RustPlusDesk.Services.Emoji;

namespace RustPlusDesk.Controls.Chat;

public class ChatEmojiInputHelper
{
    private readonly TextBox _textBox;
    private readonly Popup _autocompletePopup;
    private readonly ChatEmojiAutocompletePopup _autocompleteControl;
    private readonly Popup _pickerPopup;
    private readonly ChatEmojiPickerPopup _pickerControl;
    private readonly Button? _pickerButton;

    private int _autocompleteStartIndex = -1;

    public ChatEmojiInputHelper(
        TextBox textBox,
        Popup autocompletePopup,
        ChatEmojiAutocompletePopup autocompleteControl,
        Popup pickerPopup,
        ChatEmojiPickerPopup pickerControl,
        Button? pickerButton = null)
    {
        _textBox = textBox;
        _autocompletePopup = autocompletePopup;
        _autocompleteControl = autocompleteControl;
        _pickerPopup = pickerPopup;
        _pickerControl = pickerControl;
        _pickerButton = pickerButton;

        _textBox.Loaded += TextBox_Loaded;
        _textBox.PreviewKeyDown += TextBox_PreviewKeyDown;
        _textBox.TextChanged += TextBox_TextChanged;
        _textBox.LostFocus += TextBox_LostFocus;

        _autocompleteControl.ItemSelected += Autocomplete_ItemSelected;
        _pickerControl.EmojiSelected += Picker_EmojiSelected;

        if (_pickerButton != null)
        {
            _pickerButton.Click += PickerButton_Click;
        }
    }

    private void TextBox_Loaded(object sender, RoutedEventArgs e)
    {
        var adornerLayer = AdornerLayer.GetAdornerLayer(_textBox);
        if (adornerLayer != null)
        {
            adornerLayer.Add(new EmojiInputAdorner(_textBox));
        }
    }

    private void PickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pickerPopup.IsOpen)
        {
            _pickerPopup.IsOpen = false;
        }
        else
        {
            _pickerControl.Reset();
            _pickerPopup.IsOpen = true;
        }
    }

    private void Picker_EmojiSelected(EmojiEntry entry)
    {
        InsertTag(entry.Tag);
        _pickerPopup.IsOpen = false;
        _textBox.Focus();
    }

    private void Autocomplete_ItemSelected(EmojiEntry entry)
    {
        ApplyAutocomplete(entry);
        _textBox.Focus();
    }

    private void TextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_autocompletePopup.IsKeyboardFocusWithin && !_autocompletePopup.IsMouseOver)
        {
            _autocompletePopup.IsOpen = false;
        }
    }

    private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_autocompletePopup.IsOpen)
        {
            if (e.Key == Key.Down)
            {
                _autocompleteControl.SelectNext();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                _autocompleteControl.SelectPrevious();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                var selected = _autocompleteControl.GetSelectedEntry();
                if (selected != null)
                {
                    ApplyAutocomplete(selected);
                    e.Handled = true;
                    return;
                }
                _autocompletePopup.IsOpen = false;
            }
            if (e.Key == Key.Escape)
            {
                _autocompletePopup.IsOpen = false;
                e.Handled = true;
                return;
            }
        }
    }

    private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        CheckAutocomplete();
    }

    private void CheckAutocomplete()
    {
        var text = _textBox.Text;
        int caret = _textBox.CaretIndex;

        if (caret <= 0 || caret > text.Length)
        {
            _autocompletePopup.IsOpen = false;
            return;
        }

        // Find preceding ':'
        int colonIndex = text.LastIndexOf(':', caret - 1);
        if (colonIndex < 0)
        {
            _autocompletePopup.IsOpen = false;
            return;
        }

        // Check if there is whitespace between ':' and caret
        var query = text.Substring(colonIndex + 1, caret - colonIndex - 1);
        if (query.Any(char.IsWhiteSpace) || (colonIndex > 0 && !char.IsWhiteSpace(text[colonIndex - 1]) && text[colonIndex - 1] != ':'))
        {
            _autocompletePopup.IsOpen = false;
            return;
        }

        var results = EmojiService.Search(query, 15).ToList();
        if (results.Count > 0)
        {
            _autocompleteStartIndex = colonIndex;
            _autocompleteControl.SetSuggestions(results);
            _autocompletePopup.IsOpen = true;
        }
        else
        {
            _autocompletePopup.IsOpen = false;
        }
    }

    private void ApplyAutocomplete(EmojiEntry entry)
    {
        var text = _textBox.Text;
        int caret = _textBox.CaretIndex;

        if (_autocompleteStartIndex >= 0 && _autocompleteStartIndex < text.Length)
        {
            int replaceLen = Math.Max(0, caret - _autocompleteStartIndex);
            var newText = text.Remove(_autocompleteStartIndex, replaceLen);
            var tagToInsert = entry.Tag + " ";
            newText = newText.Insert(_autocompleteStartIndex, tagToInsert);

            _textBox.Text = newText;
            _textBox.CaretIndex = _autocompleteStartIndex + tagToInsert.Length;
        }
        else
        {
            InsertTag(entry.Tag);
        }

        _autocompletePopup.IsOpen = false;
    }

    private void InsertTag(string tag)
    {
        var text = _textBox.Text;
        int caret = _textBox.CaretIndex;

        var tagToInsert = tag + " ";
        var newText = text.Insert(caret, tagToInsert);
        _textBox.Text = newText;
        _textBox.CaretIndex = caret + tagToInsert.Length;
    }
}
