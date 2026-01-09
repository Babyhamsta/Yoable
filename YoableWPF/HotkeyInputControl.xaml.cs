using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace YoableWPF
{
    public partial class HotkeyInputControl : UserControl
    {
        public static readonly DependencyProperty HotkeyProperty =
            DependencyProperty.Register(nameof(Hotkey), typeof(string), typeof(HotkeyInputControl),
                new PropertyMetadata(string.Empty, OnHotkeyChanged));

        public static readonly DependencyProperty IsRecordingProperty =
            DependencyProperty.Register(nameof(IsRecording), typeof(bool), typeof(HotkeyInputControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty HasHotkeyProperty =
            DependencyProperty.Register(nameof(HasHotkey), typeof(bool), typeof(HotkeyInputControl),
                new PropertyMetadata(false));

        public static readonly DependencyProperty DisplayTextProperty =
            DependencyProperty.Register(nameof(DisplayText), typeof(string), typeof(HotkeyInputControl),
                new PropertyMetadata("Click to set hotkey"));

        private bool isRecording = false;
        private ModifierKeys pressedModifiers = ModifierKeys.None;
        private Key pressedKey = Key.None;

        public string Hotkey
        {
            get => (string)GetValue(HotkeyProperty);
            set => SetValue(HotkeyProperty, value);
        }

        public bool IsRecording
        {
            get => (bool)GetValue(IsRecordingProperty);
            set => SetValue(IsRecordingProperty, value);
        }

        public bool HasHotkey
        {
            get => (bool)GetValue(HasHotkeyProperty);
            set => SetValue(HasHotkeyProperty, value);
        }

        public string DisplayText
        {
            get => (string)GetValue(DisplayTextProperty);
            set => SetValue(DisplayTextProperty, value);
        }

        public HotkeyInputControl()
        {
            InitializeComponent();
            UpdateDisplayText();
        }

        private static void OnHotkeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HotkeyInputControl control)
            {
                control.HasHotkey = !string.IsNullOrEmpty(control.Hotkey);
                control.UpdateDisplayText();
            }
        }

        private void HotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            StartRecording();
        }

        private void HotkeyButton_GotFocus(object sender, RoutedEventArgs e)
        {
            // Don't auto-start recording on focus
        }

        private void HotkeyButton_LostFocus(object sender, RoutedEventArgs e)
        {
            if (isRecording)
            {
                StopRecording();
            }
        }

        private void HotkeyButton_KeyDown(object sender, KeyEventArgs e)
        {
            if (!isRecording)
                return;

            e.Handled = true;

            // Handle Escape to cancel
            if (e.Key == Key.Escape)
            {
                StopRecording();
                return;
            }

            // Ignore modifier keys alone (they will be captured in UpdatePressedModifiers)
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                UpdatePressedModifiers();
                UpdateDisplayTextWhileRecording();
                return;
            }

            // Ignore system keys
            if (e.Key == Key.System)
            {
                return;
            }

            // Update modifiers
            UpdatePressedModifiers();
            pressedKey = e.Key;

            // If a non-modifier key is pressed, complete the recording
            if (pressedKey != Key.None && pressedKey != Key.LeftCtrl && pressedKey != Key.RightCtrl &&
                pressedKey != Key.LeftShift && pressedKey != Key.RightShift &&
                pressedKey != Key.LeftAlt && pressedKey != Key.RightAlt &&
                pressedKey != Key.LWin && pressedKey != Key.RWin)
            {
                CompleteRecording();
            }
        }

        private void HotkeyButton_KeyUp(object sender, KeyEventArgs e)
        {
            if (!isRecording)
                return;

            // Update modifiers on key up
            UpdatePressedModifiers();
        }

        private void HotkeyButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!isRecording)
                return;

            // Prevent default behavior for modifier keys
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                e.Handled = true;
            }
        }

        private void UpdatePressedModifiers()
        {
            pressedModifiers = Keyboard.Modifiers;
        }

        private void StartRecording()
        {
            isRecording = true;
            IsRecording = true;
            pressedModifiers = ModifierKeys.None;
            pressedKey = Key.None;
            
            // Show recording UI
            if (RecordingTextBlock != null) RecordingTextBlock.Visibility = Visibility.Visible;
            if (KeysItemsControl != null) KeysItemsControl.Visibility = Visibility.Collapsed;
            if (EmptyTextBlock != null) EmptyTextBlock.Visibility = Visibility.Collapsed;
            
            HotkeyButton.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x00, 0xBC, 0xD4));
            
            // Force focus to receive keyboard events
            HotkeyButton.Focus();
            Keyboard.Focus(HotkeyButton);
        }

        private void StopRecording()
        {
            isRecording = false;
            IsRecording = false;
            
            UpdateDisplayText();
            HotkeyButton.Background = null;
        }

        private void CompleteRecording()
        {
            // Build hotkey string
            string hotkeyString = BuildHotkeyString(pressedModifiers, pressedKey);
            
            if (!string.IsNullOrEmpty(hotkeyString))
            {
                Hotkey = hotkeyString;
            }

            StopRecording();
        }

        private string BuildHotkeyString(ModifierKeys modifiers, Key key)
        {
            if (key == Key.None)
                return string.Empty;

            var parts = new System.Collections.Generic.List<string>();

            if ((modifiers & ModifierKeys.Control) != 0)
                parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Shift) != 0)
                parts.Add("Shift");
            if ((modifiers & ModifierKeys.Alt) != 0)
                parts.Add("Alt");
            if ((modifiers & ModifierKeys.Windows) != 0)
                parts.Add("Win");

            // Convert key to string
            string keyString = KeyToString(key);
            if (!string.IsNullOrEmpty(keyString))
            {
                parts.Add(keyString);
            }

            return string.Join(" + ", parts);
        }

        private string KeyToString(Key key)
        {
            // Handle special keys
            switch (key)
            {
                case Key.Enter: return "Enter";
                case Key.Space: return "Space";
                case Key.Tab: return "Tab";
                case Key.Back: return "Back";
                case Key.Delete: return "Del";
                case Key.Insert: return "Ins";
                case Key.Home: return "Home";
                case Key.End: return "End";
                case Key.PageUp: return "PgUp";
                case Key.PageDown: return "PgDn";
                case Key.Up: return "Up";
                case Key.Down: return "Down";
                case Key.Left: return "Left";
                case Key.Right: return "Right";
                case Key.Escape: return "Esc";
                case Key.F1: return "F1";
                case Key.F2: return "F2";
                case Key.F3: return "F3";
                case Key.F4: return "F4";
                case Key.F5: return "F5";
                case Key.F6: return "F6";
                case Key.F7: return "F7";
                case Key.F8: return "F8";
                case Key.F9: return "F9";
                case Key.F10: return "F10";
                case Key.F11: return "F11";
                case Key.F12: return "F12";
                default:
                    // For letter keys, return the letter
                    if (key >= Key.A && key <= Key.Z)
                        return key.ToString();
                    // For number keys
                    if (key >= Key.D0 && key <= Key.D9)
                        return key.ToString().Substring(1); // Remove 'D' prefix
                    if (key >= Key.NumPad0 && key <= Key.NumPad9)
                        return "Num" + (key - Key.NumPad0);
                    return key.ToString();
            }
        }

        private void UpdateDisplayTextWhileRecording()
        {
            string tempText = BuildHotkeyString(pressedModifiers, pressedKey);
            if (RecordingTextBlock != null)
            {
                if (string.IsNullOrEmpty(tempText))
                {
                    RecordingTextBlock.Text = "Press any key combination...";
                }
                else
                {
                    RecordingTextBlock.Text = tempText + " + ...";
                }
            }
        }

        private void UpdateDisplayText()
        {
            if (string.IsNullOrEmpty(Hotkey))
            {
                DisplayText = "Click to set hotkey";
                if (EmptyTextBlock != null) EmptyTextBlock.Visibility = Visibility.Visible;
                if (KeysItemsControl != null) KeysItemsControl.Visibility = Visibility.Collapsed;
                if (RecordingTextBlock != null) RecordingTextBlock.Visibility = Visibility.Collapsed;
            }
            else
            {
                DisplayText = Hotkey;
                if (EmptyTextBlock != null) EmptyTextBlock.Visibility = Visibility.Collapsed;
                if (RecordingTextBlock != null) RecordingTextBlock.Visibility = Visibility.Collapsed;
                
                if (KeysItemsControl != null)
                {
                    var keys = Hotkey.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries);
                    
                    // Add "+" between keys for display
                    var displayItems = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < keys.Length; i++)
                    {
                        displayItems.Add(keys[i]);
                        if (i < keys.Length - 1)
                        {
                            displayItems.Add("+");
                        }
                    }
                    
                    KeysItemsControl.ItemsSource = displayItems;
                    KeysItemsControl.Visibility = Visibility.Visible;
                }
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Hotkey = string.Empty;
            UpdateDisplayText();
        }
    }
}

