using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace YoableWPF.Managers
{
    public class HotkeyManager
    {
        private Dictionary<string, (ModifierKeys modifiers, Key key)> hotkeyMap = new Dictionary<string, (ModifierKeys, Key)>();

        public void RegisterHotkey(string action, string hotkeyString)
        {
            if (string.IsNullOrEmpty(hotkeyString))
                return;

            var parsed = ParseHotkeyString(hotkeyString);
            if (parsed.HasValue)
            {
                hotkeyMap[action] = parsed.Value;
            }
        }

        public bool IsHotkeyPressed(string action, KeyEventArgs e)
        {
            if (!hotkeyMap.TryGetValue(action, out var hotkey))
                return false;

            // Check modifiers match
            if (e.KeyboardDevice.Modifiers != hotkey.modifiers)
                return false;

            // Check key matches
            if (e.Key != hotkey.key)
                return false;

            return true;
        }

        private (ModifierKeys modifiers, Key key)? ParseHotkeyString(string hotkeyString)
        {
            if (string.IsNullOrEmpty(hotkeyString))
                return null;

            var parts = hotkeyString.Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return null;

            ModifierKeys modifiers = ModifierKeys.None;
            Key key = Key.None;

            foreach (var part in parts)
            {
                string trimmed = part.Trim();
                
                // Check for modifiers
                if (trimmed.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Control;
                }
                else if (trimmed.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Shift;
                }
                else if (trimmed.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Alt;
                }
                else if (trimmed.Equals("Win", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= ModifierKeys.Windows;
                }
                else
                {
                    // Try to parse as key
                    key = StringToKey(trimmed);
                    if (key == Key.None)
                        return null; // Invalid key
                }
            }

            if (key == Key.None)
                return null;

            return (modifiers, key);
        }

        private Key StringToKey(string keyString)
        {
            // Handle special keys
            switch (keyString.ToUpper())
            {
                case "ENTER": return Key.Enter;
                case "SPACE": return Key.Space;
                case "TAB": return Key.Tab;
                case "BACKSPACE": return Key.Back;
                case "DELETE": return Key.Delete;
                case "INSERT": return Key.Insert;
                case "HOME": return Key.Home;
                case "END": return Key.End;
                case "PAGEUP": return Key.PageUp;
                case "PAGEDOWN": return Key.PageDown;
                case "UP": return Key.Up;
                case "DOWN": return Key.Down;
                case "LEFT": return Key.Left;
                case "RIGHT": return Key.Right;
                case "ESC": return Key.Escape;
                case "ESCAPE": return Key.Escape;
                case "F1": return Key.F1;
                case "F2": return Key.F2;
                case "F3": return Key.F3;
                case "F4": return Key.F4;
                case "F5": return Key.F5;
                case "F6": return Key.F6;
                case "F7": return Key.F7;
                case "F8": return Key.F8;
                case "F9": return Key.F9;
                case "F10": return Key.F10;
                case "F11": return Key.F11;
                case "F12": return Key.F12;
                default:
                    // Try to parse as letter or number
                    if (keyString.Length == 1)
                    {
                        char c = keyString[0];
                        if (c >= 'A' && c <= 'Z')
                        {
                            return (Key)((int)Key.A + (c - 'A'));
                        }
                        if (c >= '0' && c <= '9')
                        {
                            return (Key)((int)Key.D0 + (c - '0'));
                        }
                    }
                    // Try enum parse
                    if (Enum.TryParse<Key>(keyString, true, out Key parsedKey))
                    {
                        return parsedKey;
                    }
                    return Key.None;
            }
        }

        public void Clear()
        {
            hotkeyMap.Clear();
        }
    }
}

