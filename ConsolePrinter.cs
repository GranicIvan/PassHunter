using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PassHunter
{
    public static class ConsolePrinter
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

}
