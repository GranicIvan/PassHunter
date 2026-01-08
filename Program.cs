using Aspose.Zip;
using PassHunter;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;


class Program
{

    static bool found = false;      
    static int maxLength = -1; // Maximum length of the password
    public static string foundPassword;

    static void Main(string[] args)
    {
        Options options = new Options();

        if(args.Length < 3)
        {
            ConsolePrinter.printHelp();
            return;

        }
        else if (args.Length >= 3) 
        {            

            try
            {
                maxLength = Convert.ToInt32(args[0]);
                if(maxLength < 1)
                {
                    
                    ConsolePrinter.Error("Max length of password must be greater than 0");
                    return;
                }
                ConsolePrinter.SetUpInfo("Max length of password is: " + maxLength);
            }
            catch (Exception ex)
            {
                ConsolePrinter.Error("First argument must be number, length of password \nError: " + ex.Message);
                return;
            }


            options.zipFilePath = Path.GetFullPath(args[1]);
            options.outputDirectory = Path.GetFullPath(args[2]);

            if (!File.Exists(options.zipFilePath))
            {
                ConsolePrinter.Warning("⚠️  ZIP file not found. Use / or \\\\ or \"\\\" ⚠️");
                return;
            }

            //Console.WriteLine("File path: " + options.zipFilePath);

            
            options.setOptions(args);

        }
   

        ConsolePrinter.SetUpInfo("Options: numbers:" + options.number + ",  a-z:" + options.lowercase + ",  A-Z:" + options.uppercase + ",  special:" + options.special);

               
        options.startWatch();


        for (int i = 1; i <= maxLength; i++)
        {
            ConsolePrinter.Info("Trying length: " + i);
            
            
            if (Cracker.TryPasswordsOfLengthParallelWEstimate(i, options, out foundPassword))
            {
                found = true;
                break;
            }
        }


        options.watch.Stop();
        TimeSpan elapsed = options.watch.Elapsed;
        
        var elapsedMs = options.watch.ElapsedMilliseconds;
        ConsolePrinter.SetUpInfo($"Program is DONE, and took: {elapsed.Days}d {elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s {elapsed.Milliseconds}ms");
        if (found)
        {
            ConsolePrinter.Success($"✅ Password found: \"{foundPassword}\"");
            ConsolePrinter.SetUpInfo($"Extracting files...");
            Cracker.ExtractOnce(options.zipFilePath, options.outputDirectory, foundPassword);
        }
        else
        {
            ConsolePrinter.Error("⛔ Failed to find password, file is still LOCKED.");            
        }
    }






}



