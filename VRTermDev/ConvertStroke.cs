using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VRTermDev
{
    public static class KeyboardMaps
    {
        public enum KeyboardTypes
        {
            American
        }

        private const byte ESC = 27;

        private static Dictionary<Keys, byte[]> _americanKeyboard = null;

        private static byte ctrl(char a)
        {
            return Convert.ToByte(a - 64);
        }

        private static void _add(this Dictionary<Keys, byte[]> col, int v, Keys mask, int ch)
        {
            int res = ch;
            res &= 0xFF;
            mask += v;

            col.Add(mask, new byte[1] { Convert.ToByte(res) });
        }

        private static void _add(this Dictionary<Keys, byte[]> col, int v, Keys mask, char ch)
        {
            int res = ch;
            res &= 0xFF;

            mask += v;

            col.Add(mask, new byte[1] { Convert.ToByte(res) });
        }

        private static void buildAmericanKeyboard()
        {
            _americanKeyboard = new Dictionary<Keys, byte[]>();

            // Lettered control keys
            for (var i = 0; i < 26; i++)
                _americanKeyboard._add((int) Keys.A+i, Keys.Control, i+1);

            _americanKeyboard.Add(Keys.D2 | Keys.Shift | Keys.Control, new byte[] { ctrl('@') });  // NUL

            // Non-Control Control keys
            _americanKeyboard.Add(Keys.Tab, new byte[] { ctrl('I') });
            _americanKeyboard.Add(Keys.Enter, new byte[] { ctrl('M') });
            _americanKeyboard.Add(Keys.LineFeed, new byte[] { ctrl('J') });
            _americanKeyboard.Add(Keys.Back, new byte[] { ctrl('H') });
            _americanKeyboard.Add(Keys.Escape, new byte[] { ESC });

            // Wierd controls
            _americanKeyboard.Add(Keys.OemBackslash | Keys.Control, new byte[] { ctrl('\\') });
            _americanKeyboard.Add(Keys.OemCloseBrackets | Keys.Control, new byte[] { ctrl(']') });
            _americanKeyboard.Add(Keys.OemOpenBrackets | Keys.Control, new byte[] { ctrl('[') });
            _americanKeyboard.Add(Keys.D6 | Keys.Shift | Keys.Control, new byte[] { ctrl('^') });
            _americanKeyboard.Add(Keys.OemMinus | Keys.Shift | Keys.Control, new byte[] { ctrl('_') });

            _americanKeyboard.Add(Keys.Space, new byte[] { 32 });
            _americanKeyboard.Add(Keys.Delete, new byte[] { 127 });

            // [ \ ] ^ _ 
            //_americanKeyboard.Add(Keys. | Keys.ControlKey, new byte[] { ctrl('A') });

            var shifts = ")!@#$%^&*(";
            for (var c = 0; c < 10; c++)
            {
                _americanKeyboard._add((int) Keys.D0+c, 0, c+48);  // Just Numbers
                _americanKeyboard._add((int)Keys.D0+c, Keys.Shift, shifts[c]); // Their shifts
            }

            // Letters
            for (var i = 0; i<26; i++)
            {
                _americanKeyboard._add((int)Keys.A+i, Keys.Shift, 65+i);
                _americanKeyboard._add((int)Keys.A+i, 0, 97+i);
            }

            // Lower Case
            _americanKeyboard.Add(Keys.OemBackslash, new byte[] { (int)'\\' });
            _americanKeyboard.Add(Keys.OemCloseBrackets, new byte[] { (int)']' });
            _americanKeyboard.Add(Keys.OemOpenBrackets, new byte[] { (int)'[' });
            _americanKeyboard.Add(Keys.Oemcomma, new byte[] { (int)',' });
            _americanKeyboard.Add(Keys.OemMinus, new byte[] { (int)'-' });
            _americanKeyboard.Add(Keys.Oemplus, new byte[] { (int)'=' });
            _americanKeyboard.Add(Keys.OemPipe, new byte[] { (int)'\\' });
            _americanKeyboard.Add(Keys.OemSemicolon, new byte[] { (int)';' });
            _americanKeyboard.Add(Keys.OemPeriod, new byte[] { (int)'.' });
            _americanKeyboard.Add(Keys.OemQuestion, new byte[] { (int)'/' });
            _americanKeyboard.Add(Keys.Oemtilde, new byte[] { (int)'`' });

            // Upper Case
            _americanKeyboard.Add(Keys.OemBackslash | Keys.Shift, new byte[] { (int)'|' });
            _americanKeyboard.Add(Keys.OemCloseBrackets | Keys.Shift, new byte[] { (int)'}' });
            _americanKeyboard.Add(Keys.OemOpenBrackets | Keys.Shift, new byte[] { (int)'{' });
            _americanKeyboard.Add(Keys.Oemcomma | Keys.Shift, new byte[] { (int)'<' });
            _americanKeyboard.Add(Keys.OemMinus | Keys.Shift, new byte[] { (int)'_' });
            _americanKeyboard.Add(Keys.Oemplus | Keys.Shift, new byte[] { (int)'+' });
            _americanKeyboard.Add(Keys.OemPipe | Keys.Shift, new byte[] { (int)'|' });
            _americanKeyboard.Add(Keys.OemSemicolon | Keys.Shift, new byte[] { (int)':' });
            _americanKeyboard.Add(Keys.OemPeriod | Keys.Shift, new byte[] { (int)'>' });
            _americanKeyboard.Add(Keys.OemQuestion | Keys.Shift, new byte[] { (int)'?' });
            _americanKeyboard.Add(Keys.Oemtilde | Keys.Shift, new byte[] { (int)'~' });

            // Function Keys
            // Arrows/Home
            // Keypad
        }


        public static byte[] ConvertStroke(KeyboardTypes keyB, PreviewKeyDownEventArgs e)
        {
            var empty = new byte[0];
            Dictionary<Keys, byte[]> keyMap = null;

            switch (keyB)
            {
                case KeyboardTypes.American:
                    if (_americanKeyboard == null)
                        buildAmericanKeyboard();
                    keyMap = _americanKeyboard;
                    break;
            }

            //if (keyMap == null) return empty;

            var code = e.KeyCode;
            var data = e.KeyData;
            var val = e.KeyValue;
            var mods = e.Modifiers;

            byte[] byteList;
            if (keyMap.TryGetValue(data, out byteList))
                return byteList;


            return empty;



        }
    }
}
