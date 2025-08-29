using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PassHunter
{
    public static class ConsolePrinterOLD
    {
        public static void Info(string message)
        {
            Print(message, ConsoleColor.Yellow, "[INFO]");
        }
        public static void SetUpInfo(string message)
        {
            Print(message, ConsoleColor.Cyan, "[SETUP-INFO]");
        }

        public static void Warning(string message)
        {
            Print(message, ConsoleColor.DarkYellow, "[WARNING]");
        }

        public static void Error(string message)
        {
            Print(message, ConsoleColor.Red, "[ERROR]");
        }

        public static void Success(string message)
        {
            Print(message, ConsoleColor.Green, "[SUCCESS]");
        }

        private static void Print(string message, ConsoleColor color, string prefix)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {prefix} {message}");
            Console.ResetColor();
        }

        public static void LiveInfo(string message)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write($"\r[INFO] {message}".PadRight(Console.WindowWidth - 1));
            }
            catch (Exception ex)
            {
                ConsolePrinter.Info(message);
            }
            finally
            {
                Console.ResetColor();
            }

              //TODO test how much time does reseting color waste
        }
    }


    public static class ConsolePrinter
    {
        private static readonly object _sync = new();
        private static bool _inlineActive = false;
        private static int _inlineLen = 0;

        // ==== public API ====

        public static void LiveInfo(string text)
        {
            lock (_sync)
            {
                WriteInline(text, ConsoleColor.Blue);
            }
        }

        public static void EndProgress() // call when finishing a phase
        {
            lock (_sync)
            {
                if (_inlineActive)
                {
                    Console.Write('\r');
                    Console.Write(new string(' ', _inlineLen));
                    Console.Write('\r');
                    _inlineActive = false;
                    _inlineLen = 0;
                }
            }
        }

        public static void Info(string message) => WriteLine("[INFO]", message, ConsoleColor.Yellow);
        public static void SetUpInfo(string message) => WriteLine("[SETUP-INFO]", message, ConsoleColor.Cyan);
        public static void Success(string message) => WriteLine("[SUCCESS]", message, ConsoleColor.Green);
        public static void Error(string message) => WriteLine("[ERROR]", message, ConsoleColor.Red);        
        public static void Warning(string message) => WriteLine("[WARNING]", message, ConsoleColor.DarkYellow);

        
        

        // ==== internals ====

        private static void WriteLine(string tag, string message, ConsoleColor color)
        {
            lock (_sync)
            {
                // ensure the inline progress doesn't collide with this line
                EndProgress();

                var line = $"{DateTime.Now:HH:mm:ss} {tag} {message}";
                var old = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = color;
                    Console.WriteLine(line);
                }
                finally
                {
                    Console.ForegroundColor = old;
                }
            }
        }

        private static void WriteInline(string text, ConsoleColor color)
        {
            // Optional: avoid wrapping which breaks CR logic
            int maxWidth = Math.Max(1, Console.BufferWidth - 1);
            if (text.Length > maxWidth) text = text.Substring(0, maxWidth);

            var old = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;

                Console.Write('\r');
                Console.Write(text);

                // if new text shorter than previous, pad the tail with spaces
                int pad = _inlineLen - text.Length;
                if (pad > 0)
                {
                    Console.Write(new string(' ', pad));
                    Console.Write('\r');
                    Console.Write(text);
                }

                _inlineActive = true;
                _inlineLen = text.Length;
            }
            finally
            {
                Console.ForegroundColor = old;
            }
        }
    }

}
