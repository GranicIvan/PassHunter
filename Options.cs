using System.Diagnostics;

namespace PassHunter
{
    class Options
    {
        // by default numbers and lowercase letters are selected
        public bool number;
        public bool lowercase;
        public bool uppercase ;
        public bool special;
        public string zipFilePath = "";
        public string outputDirectory = "";
        public Stopwatch watch = new Stopwatch();

        internal void setOptions(string[] args)
        {  
            number = args.Any(s => string.Equals(s, "-n", StringComparison.OrdinalIgnoreCase));    // Return true if numbers are selected
            lowercase = args.Any(s => string.Equals(s, "-l", StringComparison.OrdinalIgnoreCase)); // Return true if lowercase letters are selected
            uppercase = args.Any(s => string.Equals(s, "-u", StringComparison.OrdinalIgnoreCase)); // Return true if capital letters are selected
            special = args.Any(s => string.Equals(s, "-s", StringComparison.OrdinalIgnoreCase));   // Return true if special characters are selected
            //TODO add check for extra characters eg -x or -e
            //TODO add check for excluding characters eg -exclude

            if (!number && !lowercase && !uppercase && !special)
            {
                ConsolePrinter.Warning("⚠️  No options selected. Using default options: -n -l ⚠️");
                setDefaultOptions();
            }
        }

        internal void setDefaultOptions()
        {
            number = true;
            lowercase = true;
            uppercase = false;
            special = false;
        }

        internal void startWatch()
        {
            watch.Start();
        }
    }
}
