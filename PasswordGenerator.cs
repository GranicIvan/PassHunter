namespace PassHunter
{
    class PasswordGenerator
    {
        int currentLength;

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
            }
            UpdateCurrentPassword();
        }


        public PasswordGenerator(int currentLength, Options options)
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
                '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '_','+', '|',
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


        public override string ToString()
        {
            return new string(CurrentPassword);
        }


    }
}
