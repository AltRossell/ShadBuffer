using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ShadBuffer
{
    public sealed class Hotkey
    {
        private const uint ModAlt = 0x1;
        private const uint ModControl = 0x2;
        private const uint ModShift = 0x4;
        private const uint ModNoRepeat = 0x4000;

        private static readonly Dictionary<Keys, string[]> KeyNames = BuildKeyNames();

        public Keys Key { get; private set; }
        public bool Alt { get; private set; }
        public bool Control { get; private set; }
        public bool Shift { get; private set; }

        public uint Modifiers => (Alt ? ModAlt : 0) | (Control ? ModControl : 0) | (Shift ? ModShift : 0) | ModNoRepeat;
        public uint VirtualKey => (uint)Key;

        public static Hotkey Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            string[] parts = text.Split('+');
            string label = parts[parts.Length - 1];
            var match = KeyNames.Where(entry => entry.Value[0] == label).ToList();
            if (match.Count == 0)
            {
                return null;
            }
            return new Hotkey
            {
                Key = match[0].Key,
                Alt = parts.Contains("Alt"),
                Control = parts.Contains("Ctrl"),
                Shift = parts.Contains("Shift"),
            };
        }

        // returns null for lone modifiers and keys that would fire while you type
        public static Hotkey FromKeyData(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (!KeyNames.ContainsKey(code))
            {
                return null;
            }
            var hotkey = new Hotkey
            {
                Key = code,
                Alt = (keyData & Keys.Alt) != 0,
                Control = (keyData & Keys.Control) != 0,
                Shift = (keyData & Keys.Shift) != 0,
            };
            bool isFunctionKey = code >= Keys.F1 && code <= Keys.F24;
            bool isSpecial = code == Keys.Pause || code == Keys.Scroll || code == Keys.PrintScreen;
            if (!hotkey.Alt && !hotkey.Control && !isFunctionKey && !isSpecial)
            {
                return null;
            }
            return hotkey;
        }

        public override string ToString()
        {
            string text = "";
            if (Control)
            {
                text += "Ctrl+";
            }
            if (Alt)
            {
                text += "Alt+";
            }
            if (Shift)
            {
                text += "Shift+";
            }
            return text + KeyNames[Key][0];
        }

        public string ToObsJson()
        {
            string binding = "";
            if (Alt)
            {
                binding += "\"alt\":true,";
            }
            if (Control)
            {
                binding += "\"control\":true,";
            }
            if (Shift)
            {
                binding += "\"shift\":true,";
            }
            return "{\"ReplayBuffer.Save\":[{" + binding + "\"key\":\"" + KeyNames[Key][1] + "\"}]}";
        }

        private static Dictionary<Keys, string[]> BuildKeyNames()
        {
            var names = new Dictionary<Keys, string[]>();
            for (Keys key = Keys.A; key <= Keys.Z; key++)
            {
                string letter = ((char)('A' + (key - Keys.A))).ToString();
                names[key] = new[] { letter, "OBS_KEY_" + letter };
            }
            for (Keys key = Keys.D0; key <= Keys.D9; key++)
            {
                string digit = (key - Keys.D0).ToString();
                names[key] = new[] { digit, "OBS_KEY_" + digit };
            }
            for (Keys key = Keys.F1; key <= Keys.F24; key++)
            {
                string function = "F" + (key - Keys.F1 + 1);
                names[key] = new[] { function, "OBS_KEY_" + function };
            }
            for (Keys key = Keys.NumPad0; key <= Keys.NumPad9; key++)
            {
                string pad = (key - Keys.NumPad0).ToString();
                names[key] = new[] { "Num" + pad, "OBS_KEY_NUM" + pad };
            }
            names[Keys.Home] = new[] { "Home", "OBS_KEY_HOME" };
            names[Keys.End] = new[] { "End", "OBS_KEY_END" };
            names[Keys.Insert] = new[] { "Insert", "OBS_KEY_INSERT" };
            names[Keys.Delete] = new[] { "Delete", "OBS_KEY_DELETE" };
            names[Keys.PageUp] = new[] { "PageUp", "OBS_KEY_PAGEUP" };
            names[Keys.PageDown] = new[] { "PageDown", "OBS_KEY_PAGEDOWN" };
            names[Keys.Pause] = new[] { "Pause", "OBS_KEY_PAUSE" };
            names[Keys.Scroll] = new[] { "ScrollLock", "OBS_KEY_SCROLLLOCK" };
            names[Keys.PrintScreen] = new[] { "PrintScreen", "OBS_KEY_PRINT" };
            return names;
        }
    }
}
