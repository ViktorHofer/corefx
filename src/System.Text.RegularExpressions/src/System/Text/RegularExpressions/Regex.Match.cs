// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;

namespace System.Text.RegularExpressions
{
    public partial class Regex
    {
        /// <summary>
        /// Searches the input string for one or more occurrences of the text supplied in the given pattern.
        /// </summary>
        public static bool IsMatch(string input, string pattern)
        {
            return IsMatch(input, pattern, RegexOptions.None, s_defaultMatchTimeout);
        }

        /// <summary>
        /// Searches the input string for one or more occurrences of the text
        /// supplied in the pattern parameter with matching options supplied in the options
        /// parameter.
        /// </summary>
        public static bool IsMatch(string input, string pattern, RegexOptions options)
        {
            return IsMatch(input, pattern, options, s_defaultMatchTimeout);
        }

        /// <summary>
        /// Searches the input string for one or more occurrences of the text
        /// supplied in the pattern parameter with matching options supplied in the options
        /// parameter, bounded by the specified matchTimeout.
        /// </summary>
        public static bool IsMatch(string input, string pattern, RegexOptions options, TimeSpan matchTimeout)
        {
            return new Regex(pattern, options, matchTimeout, true).IsMatch(input);
        }

        /// <summary>
        /// Searches the input text for one or more occurrences of the text
        /// supplied in the pattern parameter with optional matching options supplied in the options
        /// parameter, optionally bounded by the specified matchTimeout.
        /// </summary>
        public static bool IsMatch(ReadOnlySpan<char> input, string pattern, RegexOptions options = RegexOptions.None, TimeSpan? matchTimeout = null)
        {
            return new Regex(pattern, options, matchTimeout ?? s_defaultMatchTimeout, true).IsMatch(input);
        }

        /*
         * Returns true if the regex finds a match within the specified string
         */
        /// <summary>
        /// Searches the input string for one or more matches using the previous pattern,
        /// options, and starting position.
        /// </summary>
        public bool IsMatch(string input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return IsMatch(input.AsSpan());
        }

        /// <summary>
        /// Searches the input Span for one or more matches using the previous pattern,
        /// options, and starting position.
        /// </summary>
        public bool IsMatch(ReadOnlySpan<char> input)
        {
            return IsMatch(input, UseOptionR() ? input.Length : 0);
        }

        /*
         * Returns true if the regex finds a match after the specified position
         * (proceeding leftward if the regex is leftward and rightward otherwise)
         */
        /// <summary>
        /// Searches the input string for one or more matches using the previous pattern and options,
        /// with a new starting position.
        /// </summary>
        public bool IsMatch(string input, int startat)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return IsMatch(input.AsSpan(), startat);
        }

        /// <summary>
        /// Searches the input Span for one or more matches using the previous pattern and options,
        /// with a new starting position.
        /// </summary>
        public bool IsMatch(ReadOnlySpan<char> input, int startat)
        {
            return (Run(true, -1, MemoryOrPinnedSpan<char>.Empty, input, 0, input.Length, startat) == null);
        }

        /// <summary>
        /// Searches the input string for one or more occurrences of the text
        /// supplied in the pattern parameter.
        /// </summary>
        public static Match Match(string input, string pattern)
        {
            return Match(input, pattern, RegexOptions.None, s_defaultMatchTimeout);
        }

        /// <summary>
        /// Searches the input string for one or more occurrences of the text
        /// supplied in the pattern parameter. Matching is modified with matching 
        /// options supplied in the options parameter.
        /// </summary>
        public static Match Match(string input, string pattern, RegexOptions options)
        {
            return Match(input, pattern, options, s_defaultMatchTimeout);
        }

        /// <summary>
        /// Searches the input string for one or more occurrences of the text
        /// supplied in the pattern parameter. Matching is modified with matching 
        /// options supplied in the options parameter. Bounded by a timeout supplied
        /// in the matchTimeout parameter.
        /// </summary>
        public static Match Match(string input, string pattern, RegexOptions options, TimeSpan matchTimeout)
        {
            return new Regex(pattern, options, matchTimeout, true).Match(input);
        }

        /// <summary>
        /// Searches the input text for one or more occurrences of the text
        /// supplied in the pattern parameter. Optionally, matching is modified with matching 
        /// options supplied in the options parameter. Optionally bounded by a timeout supplied
        /// in the matchTimeout parameter.
        /// </summary>
        public static Match Match(ReadOnlyMemory<char> input, string pattern, RegexOptions options = RegexOptions.None, TimeSpan? matchTimeout = null)
        {
            return new Regex(pattern, options, matchTimeout ?? s_defaultMatchTimeout, true).Match(input);
        }

        /*
         * Finds the first match for the regular expression starting at the beginning
         * of the string (or at the end of the string if the regex is leftward)
         */
        /// <summary>
        /// Matches a regular expression with a string and returns
        /// the precise result as a RegexMatch object.
        /// </summary>
        public Match Match(string input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return Match(input.AsMemory());
        }

        public Match Match(ReadOnlyMemory<char> input)
        {
            return Match(input, 0, input.Length, UseOptionR() ? input.Length : 0);
        }

        /*
         * Finds the first match, starting at the specified position
         */
        /// <summary>
        /// Matches a regular expression with a string and returns
        /// the precise result as a RegexMatch object.
        /// </summary>
        public Match Match(string input, int startat)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return Match(input.AsMemory(), startat);
        }

        public Match Match(ReadOnlyMemory<char> input, int startat)
        {
            return Match(input, 0, input.Length, startat);
        }

        /*
         * Finds the first match, restricting the search to the specified interval of
         * the char array.
         */
        /// <summary>
        /// Matches a regular expression with a string and returns the precise result as a
        /// RegexMatch object.
        /// </summary>
        public Match Match(string input, int beginning, int length)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return Match(input.AsMemory(), beginning, length);
        }

        public Match Match(ReadOnlyMemory<char> input, int beginning, int length)
        {
            return Match(input, beginning, length, UseOptionR() ? beginning + length : beginning);
        }

        private Match Match(ReadOnlyMemory<char> input, int beginning, int length, int startat)
        {
            return Run(false, -1, new MemoryOrPinnedSpan<char>(input), ReadOnlySpan<char>.Empty, beginning, length, startat);
        }

        /// <summary>
        /// Returns all the successful matches as if Match were called iteratively numerous times.
        /// </summary>
        public static MatchCollection Matches(string input, string pattern)
        {
            return Matches(input, pattern, RegexOptions.None, s_defaultMatchTimeout);
        }

        /// <summary>
        /// Returns all the successful matches as if Match were called iteratively numerous times.
        /// </summary>
        public static MatchCollection Matches(string input, string pattern, RegexOptions options)
        {
            return Matches(input, pattern, options, s_defaultMatchTimeout);
        }

        public static MatchCollection Matches(string input, string pattern, RegexOptions options, TimeSpan matchTimeout)
        {
            return new Regex(pattern, options, matchTimeout, true).Matches(input);
        }

        public static MatchCollection Matches(ReadOnlyMemory<char> input, string pattern, RegexOptions options = RegexOptions.None, TimeSpan? matchTimeout = null)
        {
            return new Regex(pattern, options, matchTimeout ?? s_defaultMatchTimeout, true).Matches(input);
        }

        /*
         * Finds the first match for the regular expression starting at the beginning
         * of the string Enumerator(or at the end of the string if the regex is leftward)
         */
        /// <summary>
        /// Returns all the successful matches as if Match was called iteratively numerous times.
        /// </summary>
        public MatchCollection Matches(string input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return Matches(input.AsMemory());
        }

        public MatchCollection Matches(ReadOnlyMemory<char> input)
        {
            return Matches(input, UseOptionR() ? input.Length : 0);
        }

        /*
         * Finds the first match, starting at the specified position
         */
        /// <summary>
        /// Returns all the successful matches as if Match was called iteratively numerous times.
        /// </summary>
        public MatchCollection Matches(string input, int startat)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            return Matches(input.AsMemory(), startat);
        }

        public MatchCollection Matches(ReadOnlyMemory<char> input, int startat)
        {
            return new MatchCollection(this, new MemoryOrPinnedSpan<char>(input), 0, input.Length, startat);
        }
    }
}
