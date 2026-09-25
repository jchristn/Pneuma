namespace Test.Benchmark.Reference
{
    using System;

    /// <summary>
    /// The classic Porter (1980) stemmer, as used by Lucene's PorterStemFilter and Elasticsearch's english
    /// analyzer. Input must be lower-case ASCII letters; anything else is returned unchanged.
    /// </summary>
    public static class PorterStemmer
    {
        #region Public-Methods

        /// <summary>
        /// Stem a lower-case word.
        /// </summary>
        /// <param name="word">The word.</param>
        /// <returns>The stem.</returns>
        public static string Stem(string word)
        {
            if (string.IsNullOrEmpty(word) || word.Length <= 2) return word ?? string.Empty;
            foreach (char c in word)
            {
                if (c < 'a' || c > 'z') return word;
            }

            char[] b = word.ToCharArray();
            int k = b.Length - 1;
            int j = 0;
            Step1ab(b, ref k, ref j);
            if (k > 0)
            {
                Step1c(b, k, ref j);
                Step2(b, ref k, ref j);
                Step3(b, ref k, ref j);
                Step4(b, ref k, ref j);
                Step5(b, ref k, ref j);
            }

            return new string(b, 0, k + 1);
        }

        #endregion

        #region Private-Methods

        private static bool Cons(char[] b, int i)
        {
            switch (b[i])
            {
                case 'a':
                case 'e':
                case 'i':
                case 'o':
                case 'u':
                    return false;
                case 'y':
                    return i == 0 || !Cons(b, i - 1);
                default:
                    return true;
            }
        }

        private static int M(char[] b, int j)
        {
            int n = 0;
            int i = 0;
            while (true)
            {
                if (i > j) return n;
                if (!Cons(b, i)) break;
                i++;
            }

            i++;
            while (true)
            {
                while (true)
                {
                    if (i > j) return n;
                    if (Cons(b, i)) break;
                    i++;
                }

                i++;
                n++;
                while (true)
                {
                    if (i > j) return n;
                    if (!Cons(b, i)) break;
                    i++;
                }

                i++;
            }
        }

        private static bool VowelInStem(char[] b, int j)
        {
            for (int i = 0; i <= j; i++)
            {
                if (!Cons(b, i)) return true;
            }

            return false;
        }

        private static bool DoubleC(char[] b, int j)
        {
            if (j < 1) return false;
            if (b[j] != b[j - 1]) return false;
            return Cons(b, j);
        }

        private static bool Cvc(char[] b, int i)
        {
            if (i < 2 || !Cons(b, i) || Cons(b, i - 1) || !Cons(b, i - 2)) return false;
            char ch = b[i];
            return ch != 'w' && ch != 'x' && ch != 'y';
        }

        private static bool Ends(char[] b, int k, ref int j, string s)
        {
            int length = s.Length;
            int o = k - length + 1;
            if (o < 0) return false;
            for (int i = 0; i < length; i++)
            {
                if (b[o + i] != s[i]) return false;
            }

            j = k - length;
            return true;
        }

        private static void SetTo(char[] b, ref int k, int j, string s)
        {
            int length = s.Length;
            int o = j + 1;
            for (int i = 0; i < length; i++) b[o + i] = s[i];
            k = j + length;
        }

        private static void R(char[] b, ref int k, int j, string s)
        {
            if (M(b, j) > 0) SetTo(b, ref k, j, s);
        }

        private static void Step1ab(char[] b, ref int k, ref int j)
        {
            if (b[k] == 's')
            {
                if (Ends(b, k, ref j, "sses")) k -= 2;
                else if (Ends(b, k, ref j, "ies")) SetTo(b, ref k, j, "i");
                else if (b[k - 1] != 's') k--;
            }

            if (Ends(b, k, ref j, "eed"))
            {
                if (M(b, j) > 0) k--;
            }
            else if ((Ends(b, k, ref j, "ed") || Ends(b, k, ref j, "ing")) && VowelInStem(b, j))
            {
                k = j;
                if (Ends(b, k, ref j, "at")) SetTo(b, ref k, j, "ate");
                else if (Ends(b, k, ref j, "bl")) SetTo(b, ref k, j, "ble");
                else if (Ends(b, k, ref j, "iz")) SetTo(b, ref k, j, "ize");
                else if (DoubleC(b, k))
                {
                    k--;
                    char ch = b[k];
                    if (ch == 'l' || ch == 's' || ch == 'z') k++;
                }
                else if (M(b, k) == 1 && Cvc(b, k))
                {
                    SetTo(b, ref k, k, "e");
                }
            }
        }

        private static void Step1c(char[] b, int k, ref int j)
        {
            if (Ends(b, k, ref j, "y") && VowelInStem(b, j)) b[k] = 'i';
        }

        private static void Step2(char[] b, ref int k, ref int j)
        {
            if (k == 0) return;
            switch (b[k - 1])
            {
                case 'a':
                    if (Ends(b, k, ref j, "ational")) { R(b, ref k, j, "ate"); break; }
                    if (Ends(b, k, ref j, "tional")) { R(b, ref k, j, "tion"); break; }
                    break;
                case 'c':
                    if (Ends(b, k, ref j, "enci")) { R(b, ref k, j, "ence"); break; }
                    if (Ends(b, k, ref j, "anci")) { R(b, ref k, j, "ance"); break; }
                    break;
                case 'e':
                    if (Ends(b, k, ref j, "izer")) { R(b, ref k, j, "ize"); break; }
                    break;
                case 'l':
                    if (Ends(b, k, ref j, "bli")) { R(b, ref k, j, "ble"); break; }
                    if (Ends(b, k, ref j, "alli")) { R(b, ref k, j, "al"); break; }
                    if (Ends(b, k, ref j, "entli")) { R(b, ref k, j, "ent"); break; }
                    if (Ends(b, k, ref j, "eli")) { R(b, ref k, j, "e"); break; }
                    if (Ends(b, k, ref j, "ousli")) { R(b, ref k, j, "ous"); break; }
                    break;
                case 'o':
                    if (Ends(b, k, ref j, "ization")) { R(b, ref k, j, "ize"); break; }
                    if (Ends(b, k, ref j, "ation")) { R(b, ref k, j, "ate"); break; }
                    if (Ends(b, k, ref j, "ator")) { R(b, ref k, j, "ate"); break; }
                    break;
                case 's':
                    if (Ends(b, k, ref j, "alism")) { R(b, ref k, j, "al"); break; }
                    if (Ends(b, k, ref j, "iveness")) { R(b, ref k, j, "ive"); break; }
                    if (Ends(b, k, ref j, "fulness")) { R(b, ref k, j, "ful"); break; }
                    if (Ends(b, k, ref j, "ousness")) { R(b, ref k, j, "ous"); break; }
                    break;
                case 't':
                    if (Ends(b, k, ref j, "aliti")) { R(b, ref k, j, "al"); break; }
                    if (Ends(b, k, ref j, "iviti")) { R(b, ref k, j, "ive"); break; }
                    if (Ends(b, k, ref j, "biliti")) { R(b, ref k, j, "ble"); break; }
                    break;
                case 'g':
                    if (Ends(b, k, ref j, "logi")) { R(b, ref k, j, "log"); break; }
                    break;
            }
        }

        private static void Step3(char[] b, ref int k, ref int j)
        {
            switch (b[k])
            {
                case 'e':
                    if (Ends(b, k, ref j, "icate")) { R(b, ref k, j, "ic"); break; }
                    if (Ends(b, k, ref j, "ative")) { R(b, ref k, j, string.Empty); break; }
                    if (Ends(b, k, ref j, "alize")) { R(b, ref k, j, "al"); break; }
                    break;
                case 'i':
                    if (Ends(b, k, ref j, "iciti")) { R(b, ref k, j, "ic"); break; }
                    break;
                case 'l':
                    if (Ends(b, k, ref j, "ical")) { R(b, ref k, j, "ic"); break; }
                    if (Ends(b, k, ref j, "ful")) { R(b, ref k, j, string.Empty); break; }
                    break;
                case 's':
                    if (Ends(b, k, ref j, "ness")) { R(b, ref k, j, string.Empty); break; }
                    break;
            }
        }

        private static void Step4(char[] b, ref int k, ref int j)
        {
            if (k == 0) return;
            bool matched;
            switch (b[k - 1])
            {
                case 'a': matched = Ends(b, k, ref j, "al"); break;
                case 'c': matched = Ends(b, k, ref j, "ance") || Ends(b, k, ref j, "ence"); break;
                case 'e': matched = Ends(b, k, ref j, "er"); break;
                case 'i': matched = Ends(b, k, ref j, "ic"); break;
                case 'l': matched = Ends(b, k, ref j, "able") || Ends(b, k, ref j, "ible"); break;
                case 'n': matched = Ends(b, k, ref j, "ant") || Ends(b, k, ref j, "ement") || Ends(b, k, ref j, "ment") || Ends(b, k, ref j, "ent"); break;
                case 'o':
                    matched = (Ends(b, k, ref j, "ion") && j >= 0 && (b[j] == 's' || b[j] == 't')) || Ends(b, k, ref j, "ou");
                    break;
                case 's': matched = Ends(b, k, ref j, "ism"); break;
                case 't': matched = Ends(b, k, ref j, "ate") || Ends(b, k, ref j, "iti"); break;
                case 'u': matched = Ends(b, k, ref j, "ous"); break;
                case 'v': matched = Ends(b, k, ref j, "ive"); break;
                case 'z': matched = Ends(b, k, ref j, "ize"); break;
                default: matched = false; break;
            }

            if (matched && M(b, j) > 1) k = j;
        }

        private static void Step5(char[] b, ref int k, ref int j)
        {
            j = k;
            if (b[k] == 'e')
            {
                int a = M(b, k - 1);
                if (a > 1 || (a == 1 && !Cvc(b, k - 1))) k--;
            }

            if (b[k] == 'l' && DoubleC(b, k) && M(b, k - 1) > 1) k--;
        }

        #endregion
    }
}
