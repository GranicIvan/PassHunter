using System.Runtime.CompilerServices;

namespace PassHunter
{
    class PasswordGeneratorOD //On demand - doesnt have CurrentPassword field
    {
        private int currentLength;
        private int[] indexes;
        private char[] _chars = Array.Empty<char>();


        public List<char> possibleCharacters = new List<char>();


        public PasswordGeneratorOD(int currentLength, Options options)
        {
            this.currentLength = currentLength;            

            /*
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
            */


            //Console.WriteLine("Possible characters: " + string.Concat(possibleCharacters));

            //Console.Write("+++");
            //foreach (char c in possibleCharacters)
            //{
            //    Console.Write(c);
            //}
            //Console.Write("+++");

            indexes = new int[currentLength];

            if (options.CharSet == null || options.CharSet.Length == 0)
            {
                // Build from flags when caller didn't prebuild
                options.BuildCharSet();
            }
            _chars = options.CharSet;
            //_chars = possibleCharacters.ToArray(); // one-time materialize to array for faster indexing


            //if (options.CharSet.Length == 0) options.BuildCharSet();
            //_chars = options.CharSet;


        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void nextPassword()
        {
            int index = currentLength - 1;
            while (index >= 0)
            {

                if (indexes[index] + 1 < _chars.Length)
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




        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            return string.Create(currentLength, (_chars, indexes), static (dst, st) =>
            {
                var (chars, idx) = st;
                for (int i = 0; i < dst.Length; i++)
                    dst[i] = chars[idx[i]];
            });
        }






        public long SpaceSize
        {
            get
            {
                checked
                {
                    long s = 1;
                    for (int i = 0; i < currentLength; i++)
                        s *= _chars.Length;
                    return s;
                }
                /*
                long n = possibleCharacters.Count;
                long res = 1;
                for (int i = 0; i < currentLength; i++)
                {
                    checked { res *= n; } 
                }
                return res;
                */
            }
        }

        public void SetPositionFromLinearIndex(long position)
        {
            int n = _chars.Length;

            if (n == 0)
                throw new InvalidOperationException("Character set is empty.");


            for (int i = currentLength - 1; i >= 0; i--)
            {
                indexes[i] = (int)(position % n);
                position /= n;
            }
        }




    }

}
