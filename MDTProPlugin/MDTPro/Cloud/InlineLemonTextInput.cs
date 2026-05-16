using System;
using System.Collections.Generic;
using System.Windows.Forms;
using LemonUI.Menus;
using Rage;

namespace MDTPro.Cloud {
    /// <summary>Inline text editor for LemonUI rows that avoids GTA's on-screen keyboard.</summary>
    internal sealed class InlineLemonTextInput {
        readonly NativeMenu _menu;
        readonly NativeItem _item;
        readonly int _maxLength;
        readonly HashSet<Keys> _previousKeys = new HashSet<Keys>();
        string _buffer = "";
        string _startBuffer = "";
        string _idleDescription = "";
        bool _masked;

        internal event Action<string> Completed;
        internal event Action<string> Cancelled;

        internal bool IsEditing { get; private set; }
        internal string Value => _buffer;

        internal InlineLemonTextInput(NativeMenu menu, NativeItem item, int maxLength) {
            _menu = menu;
            _item = item;
            _maxLength = Math.Max(1, Math.Min(64, maxLength));
        }

        internal void SetValue(string value, bool masked) {
            _buffer = Clamp(value ?? "");
            _masked = masked;
            Render();
        }

        internal void Start(string value, bool masked, string editDescription) {
            _buffer = Clamp(value ?? "");
            _startBuffer = _buffer;
            _masked = masked;
            _idleDescription = _item.Description ?? "";
            _item.Description = string.IsNullOrWhiteSpace(editDescription)
                ? "Type text. Enter to finish, Esc to cancel, Backspace to delete."
                : editDescription;
            IsEditing = true;
            _menu.AcceptsInput = false;
            SeedPreviousKeys();
            Render();
        }

        internal void CancelActiveEdit() {
            if (!IsEditing) return;
            Cancel();
        }

        /// <returns><c>true</c> when an active edit consumed input or is suppressing menu input.</returns>
        internal bool Process() {
            if (!IsEditing) {
                _previousKeys.Clear();
                return false;
            }

            bool shift = IsDown(Keys.ShiftKey) || IsDown(Keys.LShiftKey) || IsDown(Keys.RShiftKey);
            foreach (Keys key in PrintableKeys) {
                bool down = IsDown(key);
                if (down && !_previousKeys.Contains(key)) {
                    if (TryMapPrintable(key, shift, out char c) && _buffer.Length < _maxLength) {
                        _buffer += c;
                        Render();
                    }
                }
                Remember(key, down);
            }

            ProcessCommandKey(Keys.Back, Backspace);
            if (!IsEditing) return true;
            ProcessCommandKey(Keys.Enter, Finish);
            if (!IsEditing) return true;
            ProcessCommandKey(Keys.Return, Finish);
            if (!IsEditing) return true;
            ProcessCommandKey(Keys.Escape, Cancel);

            Remember(Keys.ShiftKey, IsDown(Keys.ShiftKey));
            Remember(Keys.LShiftKey, IsDown(Keys.LShiftKey));
            Remember(Keys.RShiftKey, IsDown(Keys.RShiftKey));
            return true;
        }

        void Finish() {
            EndEditing();
            Completed?.Invoke(_buffer);
        }

        void Cancel() {
            _buffer = _startBuffer;
            Render();
            EndEditing();
            Cancelled?.Invoke(_buffer);
        }

        void EndEditing() {
            IsEditing = false;
            _menu.AcceptsInput = true;
            _item.Description = _idleDescription;
            _previousKeys.Clear();
        }

        void Backspace() {
            if (_buffer.Length == 0) return;
            _buffer = _buffer.Substring(0, _buffer.Length - 1);
            Render();
        }

        void ProcessCommandKey(Keys key, Action action) {
            bool down = IsDown(key);
            bool edge = down && !_previousKeys.Contains(key);
            Remember(key, down);
            if (!edge) return;
            action();
        }

        void SeedPreviousKeys() {
            _previousKeys.Clear();
            foreach (Keys key in PrintableKeys)
                if (IsDown(key)) _previousKeys.Add(key);
            foreach (Keys key in CommandKeys)
                if (IsDown(key)) _previousKeys.Add(key);
        }

        void Render() {
            _item.AltTitle = _masked ? new string('*', _buffer.Length) : _buffer;
        }

        string Clamp(string value) {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= _maxLength ? value : value.Substring(0, _maxLength);
        }

        void Remember(Keys key, bool down) {
            if (down) _previousKeys.Add(key);
            else _previousKeys.Remove(key);
        }

        static bool IsDown(Keys key) {
            try {
                return Game.IsKeyDownRightNow(key);
            } catch {
                return false;
            }
        }

        static bool TryMapPrintable(Keys key, bool shift, out char c) {
            c = '\0';
            if (key >= Keys.A && key <= Keys.Z) {
                c = (char)((shift ? 'A' : 'a') + (key - Keys.A));
                return true;
            }
            if (key >= Keys.D0 && key <= Keys.D9) {
                int digit = key - Keys.D0;
                c = shift ? ShiftedDigits[digit] : (char)('0' + digit);
                return true;
            }
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) {
                c = (char)('0' + (key - Keys.NumPad0));
                return true;
            }
            switch (key) {
                case Keys.Space: c = ' '; return true;
                case Keys.OemMinus: c = shift ? '_' : '-'; return true;
                case Keys.Oemplus: c = shift ? '+' : '='; return true;
                case Keys.OemOpenBrackets: c = shift ? '{' : '['; return true;
                case Keys.OemCloseBrackets: c = shift ? '}' : ']'; return true;
                case Keys.OemSemicolon: c = shift ? ':' : ';'; return true;
                case Keys.OemQuotes: c = shift ? '"' : '\''; return true;
                case Keys.Oemcomma: c = shift ? '<' : ','; return true;
                case Keys.OemPeriod: c = shift ? '>' : '.'; return true;
                case Keys.OemQuestion: c = shift ? '?' : '/'; return true;
                case Keys.OemPipe: c = shift ? '|' : '\\'; return true;
                case Keys.Oemtilde: c = shift ? '~' : '`'; return true;
                default: return false;
            }
        }

        static readonly char[] ShiftedDigits = { ')', '!', '@', '#', '$', '%', '^', '&', '*', '(' };

        static readonly Keys[] CommandKeys = {
            Keys.Back, Keys.Enter, Keys.Return, Keys.Escape,
            Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey
        };

        static readonly Keys[] PrintableKeys = {
            Keys.A, Keys.B, Keys.C, Keys.D, Keys.E, Keys.F, Keys.G, Keys.H, Keys.I, Keys.J, Keys.K, Keys.L, Keys.M,
            Keys.N, Keys.O, Keys.P, Keys.Q, Keys.R, Keys.S, Keys.T, Keys.U, Keys.V, Keys.W, Keys.X, Keys.Y, Keys.Z,
            Keys.D0, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9,
            Keys.NumPad0, Keys.NumPad1, Keys.NumPad2, Keys.NumPad3, Keys.NumPad4, Keys.NumPad5, Keys.NumPad6, Keys.NumPad7, Keys.NumPad8, Keys.NumPad9,
            Keys.Space, Keys.OemMinus, Keys.Oemplus, Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemSemicolon,
            Keys.OemQuotes, Keys.Oemcomma, Keys.OemPeriod, Keys.OemQuestion, Keys.OemPipe, Keys.Oemtilde
        };
    }
}
