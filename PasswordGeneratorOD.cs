namespace PassHunter
{
    class PasswordGeneratorOD //On demand - doesnt have CurrentPassword field
    {
        private int currentLength;
        private int[] indexes;

        public List<char> possibleCharacters = new List<char>();


        public PasswordGeneratorOD(int currentLength, Options options)
        {
            this.currentLength = currentLength;            


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


            //Console.WriteLine("Possible characters: " + string.Concat(possibleCharacters));

            //Console.Write("+++");
            //foreach (char c in possibleCharacters)
            //{
            //    Console.Write(c);
            //}
            //Console.Write("+++");

            indexes = new int[currentLength];

        }

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
            
        }



        public override string ToString()
        {
            char[] result = new char[currentLength];
            for (int i = 0; i < currentLength; i++)
            {
                result[i] = possibleCharacters[indexes[i]];
            }
            return new string(result);
        }

    }
    
}
