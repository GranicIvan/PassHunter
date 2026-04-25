using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace PassHunter
{
 
    public static class ConsolePrinter
    {
        private static readonly object _sync = new();
        private static bool _inlineActive = false;
        private static int _inlineLen = 0;


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
        public static void Help(string message) => WriteLine("", message, ConsoleColor.Yellow);
        public static void Version(string message) => WriteLine("", message, ConsoleColor.Blue);



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


        public static void printHelp()
        {
            System.Reflection.Assembly asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            string name = asm.GetName().Name ?? "PassHunter";
            string verRaw = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                 ?? asm.GetName().Version?.ToString()
                 ?? "unknown";

            string ver = verRaw.Split('+')[0];

            string runtime = RuntimeInformation.FrameworkDescription;

            ConsolePrinter.Version($"{name} v{ver} | Runtime: {runtime}");

            ConsolePrinter.Help("Usage: PassHunter.exe <maxLength> <archiveFilePath> <outputDirectory>");
            ConsolePrinter.Help("       PassHunter.exe --fasttest|-ft [testDirectory]  (default: testFiles/FastTest in output folder)");
            ConsolePrinter.Help("Supported archive types: .zip, .rar");
            ConsolePrinter.Help("Options:");
            ConsolePrinter.Help("  -n : Include numbers");
            ConsolePrinter.Help("  -l : Include lowercase letters");
            ConsolePrinter.Help("  -u : Include uppercase letters");
            ConsolePrinter.Help("  -s : Include special characters");

            ConsolePrinter.Help("Path must use / or \\\\ or \"\\\". For example: C:/user/file.rar  or  C:\\\\user\\\\file.zip or \"C:\\user\\file.zip\"");
            //TODO Explain unsuppoted terminals, and they print mant rows instead of updateing one, Also slower

            ConsolePrinter.Help("");
            ConsolePrinter.Help("Resume (continue a previously interrupted session):");
            ConsolePrinter.Help("  Usage:   PassHunter.exe --resume <checkpointFile> [newArchivePath]");
            ConsolePrinter.Help("  Example: PassHunter.exe --resume C:/files/secret.checkpoint.json");
            ConsolePrinter.Help("  Example: PassHunter.exe --resume C:/files/secret.checkpoint.json D:/backup/secret.zip");
            ConsolePrinter.Help("  - Press Ctrl+C during any run to stop and save a checkpoint next to the archive.");
            ConsolePrinter.Help("  - The checkpoint file stores the last safely attempted position, elapsed time, and all options.");
            ConsolePrinter.Help("  - On resume, all options (charset, max length, etc.) are restored from the checkpoint automatically.");
            ConsolePrinter.Help("  - If the archive was moved, provide the new path as the third argument.");
            ConsolePrinter.Help("  - The checkpoint file is deleted automatically when the password is found.");
        }

    }

}
