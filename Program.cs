using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Aspose.Zip;
using static System.Net.Mime.MediaTypeNames;

class Program
{

    public static bool found;
    public static string password;   
   
    public static int maxLength = 5; // Maximum length of the password

    public static string foundPassword;

    static void Main(string[] args)
    {
        Options options = new Options();

        if (args.Length == 0)
        {
            maxLength = 5;
            options.outputDirectory = "extracted/6";
            options.zipFilePath = "D:\\Fajlovi D\\Projekti\\Pedjin rar fajl\\Za zip fajl\\rad\\c sharp 1\\csCracker\\z3.zip"; // Path to your ZIP file
           
        }
        else if (args.Length == 1)
        {
            //Checking is user wants help
            if (args[0].ToLower() == "help" || args[0].ToLower() == "-h" || args[0].ToLower() == "--help" || args[0].ToLower() == "-help")
            {
                printHelp();
                return;
            }

            //Int32.TryParse( args[0], out maxLength );
            try
            {
                maxLength = Convert.ToInt32(args[0]);
                Console.WriteLine("Max length of password is: " + maxLength);
            }
            catch (Exception ex)
            {
                Console.WriteLine("First argument must be number, length of password \nError: " + ex.Message);
                return;
            }

            options.zipFilePath = Path.GetFullPath(args[0]); // Path to your ZIP file
            
        }
        else if (args.Length >= 3) 
        {
            Int32.TryParse(args[0], out maxLength);

            try
            {
                maxLength = Convert.ToInt32(args[0]);
                Console.WriteLine("Max length of password is: " + maxLength);
            }
            catch (Exception ex)
            {
                Console.WriteLine("First argument must be number, length of password \nError: " + ex.Message);
                return;
            }

            //options.zipFilePath = args[1]; // Path to your ZIP file            
            //options.outputDirectory = args[2]; // Destination directory

            options.zipFilePath = Path.GetFullPath(args[1]);
            options.outputDirectory = Path.GetFullPath(args[2]);

            if (!File.Exists(options.zipFilePath))
            {
                Console.WriteLine("⚠️  ZIP file not found. Use / or \\\\ or \"\\\" ⚠️");
                return;
            }

            //Console.WriteLine("File path: " + options.zipFilePath);
            


            options.number = args.Any(s => string.Equals(s, "-n", StringComparison.OrdinalIgnoreCase)); // Return true if numbers are selected
            options.lowercase = args.Any(s => string.Equals(s, "-l", StringComparison.OrdinalIgnoreCase)); // Return true if lowercase letters are selected
            options.uppercase = args.Any(s => string.Equals(s, "-u", StringComparison.OrdinalIgnoreCase)); // Return true if capital letters are selected
            options.special = args.Any(s => string.Equals(s, "-s", StringComparison.OrdinalIgnoreCase)); // Return true if special characters are selected            
            //TODO add check for extra characters eg -x or -e
            //TODO add check for excluding characters eg -exclude

        }
        else
        {
            Console.WriteLine("Please provide the path to the ZIP file as a command-line argument.");
            return;
        }

        //foreach (var arg in args)
        //{
        //    Console.WriteLine("arg: "+arg);
        //}


        Console.WriteLine("Options: numbers:" + options.number + ",  a-z:" + options.lowercase + ",  A-Z:" + options.uppercase + ",  special:" + options.special);

        password = "0"; 
       

        var watch = System.Diagnostics.Stopwatch.StartNew();



        for (int i = 0; i <= maxLength; i++)
        {
            Console.WriteLine("Trying length: " + i);

            found = ExtactionForLength( i, options);
            if (found)
            {
                break;
            }
        }


        watch.Stop();
        TimeSpan elapsed = watch.Elapsed;
        
        var elapsedMs = watch.ElapsedMilliseconds;
        Console.WriteLine($"Program is DONE, and took: {elapsed.Days}d {elapsed.Hours}h {elapsed.Minutes}m {elapsed.Seconds}s {elapsed.Milliseconds}ms");
        if (found)
        {
            Console.WriteLine("Password has been found and it is:\"" + foundPassword+"\"");
        }
        else
        {
            Console.WriteLine("Password has NOT been found, file is still LOCKED");
        }
    }

    static void printHelp()
    {
        Console.WriteLine("Usage: csCracker.exe <maxLength> <zipFilePath> <outputDirectory>");
        Console.WriteLine("Options:");
        Console.WriteLine("  -n : Include numbers");
        Console.WriteLine("  -l : Include lowercase letters");
        Console.WriteLine("  -u : Include uppercase letters");
        Console.WriteLine("  -s : Include special characters");

        Console.WriteLine("Path must use / or \\\\ or \"\\\" ");
        Console.WriteLine("Examples: C:/user/file.zip  or  C:\\\\user\\\\file.zip or \"C:\\user\\file.zip\"");
    }


    static bool Extaction(string zipFilePath, string outputDirectory, string password)
    {
        //Console.WriteLine("Trying password: " + password);
       
        // Set up the decryption options with the provided password
        var options = new ArchiveLoadOptions
        {
            DecryptionPassword = password
        };
        // Open the ZIP archive with the specified options
        try
        {
            using (var archive = new Archive(zipFilePath, options))
            {
                // Ensure the output directory exists
                Directory.CreateDirectory(outputDirectory);

                // Extract all contents to the output directory
                archive.ExtractToDirectory(outputDirectory);
                foundPassword = password;
            }
        }
        catch (Exception ex) 
        {
            return false;
        }
        return true;
    }

    static bool ExtactionForLength(int currentLength, Options options)
    {
        PasswordGenerator passwordGen = new PasswordGenerator(currentLength, options);

        long totalCombinations = (long)Math.Pow(passwordGen.possibleCharacters.Count, currentLength);
        for (long i = 0; i < totalCombinations; i++)
        {

            //found = Extaction(options.zipFilePath, options.outputDirectory, string.Join("", passwordGen).ToString());
            string password = passwordGen.ToString();
            found = Extaction(options.zipFilePath, options.outputDirectory, password);


            
            
            if (found)
            {
                return true;
            }

            passwordGen.nextPassword();
            //passwordGen.nextPasswordOld2();
            //password = new string(passwordGen.CurrentPassword);
        }       

        return false;
    }


}

class PasswordGenerator
{
    int currentLength; //current length 


    public List<char> possibleCharacters = new List<char>();


    public char[] CurrentPassword { get; set; }
   


    private int[] indexes;
    public void nextPassword()
    {
        int index = currentLength - 1;
        while (index >= 0)
        {
            
            if (indexes[index] + 1 < possibleCharacters.Count)
            {
                indexes[index]++;
                break;
            }
            else
            {
                indexes[index] = 0;
                index--;
            }


            //Console.WriteLine("Current password: " + new string(CurrentPassword));
        }
        UpdateCurrentPassword();
    }


    public PasswordGenerator(int currentLength, Options options )
    {
        this.currentLength = currentLength;


        CurrentPassword = new char[currentLength];




        if (options.number)
            for (char c = '0'; c <= '9'; c++) possibleCharacters.Add(c);

        if (options.lowercase)
            for (char c = 'a'; c <= 'z'; c++) possibleCharacters.Add(c);

        if (options.uppercase)
            for (char c = 'A'; c <= 'Z'; c++) possibleCharacters.Add(c);

        if (options.special)
        {
            //for (char c = ' '; c <= '~'; c++) possibleCharacters.Add(c);
            possibleCharacters.AddRange(new char[] {
                '!', '@', '#', '$', '%', '^', '&', '*', '(', ')',
                '[', ']', '{', '}', '-', '/', '\\', '=', '?', ':', ';', '"', '\'', '<', '>', ',', '.', '`', '~',
            });

        }
        


        for (int i = 0; i < currentLength; i++)
        {
            CurrentPassword[i] = possibleCharacters[0];
        }

        //Console.WriteLine("Possible characters: " + string.Concat(possibleCharacters));

        //Console.Write("+++");
        //foreach (char c in possibleCharacters)
        //{
        //    Console.Write(c);
        //}
        //Console.Write("+++");

        indexes = new int[currentLength];
        CurrentPassword = new char[currentLength];
        for (int i = 0; i < currentLength; i++)
        {
            indexes[i] = 0;
           
        }
        UpdateCurrentPassword();


    }

    private void UpdateCurrentPassword()
    {
        for (int i = 0; i < currentLength; i++)
        {                       
            CurrentPassword[i] = possibleCharacters[indexes[i]];
        }
        //Console.WriteLine("Updated password: " + new string(CurrentPassword));
    }


    public override string ToString() {
        return new string(CurrentPassword);
    }



}

class Options
{
    // by default numbers and lowercase letters are selected
    public bool number = true;
    public bool lowercase = true;
    public bool uppercase = false;
    public bool special = false;
    public string zipFilePath = "";
    public string outputDirectory = "";

}