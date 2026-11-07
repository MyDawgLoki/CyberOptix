using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CyberOptix.Services;

namespace CyberOptix.Controls
{
    /// <summary>
    /// A box that records the next key combination pressed while it has focus. The result is a Glass Cursor key number
    /// (the same numbers its own tray window saves), so the two settings screens read and write each other's keybinds.
    /// </summary>
    public sealed class KeybindBox : TextBox
    {
        public KeybindBox()
        {
            IsReadOnly = true;
            IsUndoEnabled = false;
            Cursor = Cursors.Hand;
            Width = 190;
            TextAlignment = TextAlignment.Center;
            ContextMenu = null;
            Loaded += (_, _) => { if (TryFindResource(typeof(TextBox)) is Style s) Style = s; };
        }

        /// <summary>The combination currently shown (0 = none).</summary>
        public int Keys { get; private set; }

        /// <summary>True when the box starts listening, false when it stops.</summary>
        public event Action<bool>? Capturing;

        /// <summary>Raised with the new combination (0 when cleared) after the user picks one.</summary>
        public event Action<int>? KeysPicked;

        public void Display(int keys)
        {
            Keys = keys;
            if (!IsKeyboardFocused) Text = GlassCursorKeys.Describe(keys);
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            Text = "Press keys...";
            Capturing?.Invoke(true);
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            Text = GlassCursorKeys.Describe(Keys);
            Capturing?.Invoke(false);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.ImeProcessed) key = e.ImeProcessedKey;
            var mods = Keyboard.Modifiers;

            switch (key)
            {
                case Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift:
                    Text = Partial(mods);
                    return;
                case Key.LWin or Key.RWin:
                    Text = "The Windows key can't be used";
                    return;
                case Key.Escape when mods == ModifierKeys.None:
                    Keyboard.ClearFocus();
                    return;
                case Key.Back or Key.Delete when mods == ModifierKeys.None:
                    Keys = 0;
                    Done();
                    return;
            }

            if ((mods & ModifierKeys.Windows) != 0)
            {
                Text = "The Windows key can't be used";
                return;
            }
            bool fKey = key >= Key.F1 && key <= Key.F24;
            if (mods == ModifierKeys.None && !fKey)
            {
                // A bare letter would stop that key working everywhere else.
                Text = "Add Ctrl, Alt or Shift";
                return;
            }

            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0) return;
            int combo = vk;
            if ((mods & ModifierKeys.Control) != 0) combo |= GlassCursorKeys.Control;
            if ((mods & ModifierKeys.Alt) != 0) combo |= GlassCursorKeys.Alt;
            if ((mods & ModifierKeys.Shift) != 0) combo |= GlassCursorKeys.Shift;
            Keys = combo;
            Done();
        }

        private static string Partial(ModifierKeys mods)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            return parts.Count == 0 ? "Press keys..." : string.Join(" + ", parts) + " + ...";
        }

        private void Done()
        {
            var picked = Keys;
            Keyboard.ClearFocus();
            KeysPicked?.Invoke(picked);
        }
    }
}
