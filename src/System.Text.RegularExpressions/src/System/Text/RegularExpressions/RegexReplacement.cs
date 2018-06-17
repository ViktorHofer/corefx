// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// The RegexReplacement class represents a substitution string for
// use when using regexes to search/replace, etc. It's logically
// a sequence intermixed (1) constant strings and (2) group numbers.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace System.Text.RegularExpressions
{
    internal sealed class RegexReplacement
    {
        // Constants for special insertion patterns
        private const int Specials = 4;
        public const int LeftPortion = -1;
        public const int RightPortion = -2;
        public const int LastGroup = -3;
        public const int WholeString = -4;

        private readonly List<string> _strings; // table of string constants
        private readonly List<int> _rules;      // negative -> group #, positive -> string #

        /// <summary>
        /// Since RegexReplacement shares the same parser as Regex,
        /// the constructor takes a RegexNode which is a concatenation
        /// of constant strings and backreferences.
        /// </summary>
        public RegexReplacement(string rep, RegexNode concat, Hashtable _caps)
        {
            if (concat.Type() != RegexNode.Concatenate)
                throw new ArgumentException(SR.ReplacementError);

            Span<char> buffer = stackalloc char[256];
            ValueStringBuilder vsb = new ValueStringBuilder(buffer);
            List<string> strings = new List<string>();
            List<int> rules = new List<int>();

            for (int i = 0; i < concat.ChildCount(); i++)
            {
                RegexNode child = concat.Child(i);

                switch (child.Type())
                {
                    case RegexNode.Multi:
                        vsb.Append(child.Str);
                        break;

                    case RegexNode.One:
                        vsb.Append(child.Ch);
                        break;

                    case RegexNode.Ref:
                        if (vsb.Length > 0)
                        {
                            rules.Add(strings.Count);
                            strings.Add(vsb.ToString());
                            vsb.Length = 0;
                        }
                        int slot = child.M;

                        if (_caps != null && slot >= 0)
                            slot = (int)_caps[slot];

                        rules.Add(-Specials - 1 - slot);
                        break;

                    default:
                        throw new ArgumentException(SR.ReplacementError);
                }
            }

            if (vsb.Length > 0)
            {
                rules.Add(strings.Count);
                strings.Add(vsb.ToString());
            }

            Pattern = rep;
            _strings = strings;
            _rules = rules;
        }

        /// <summary>
        /// Either returns a weakly cached RegexReplacement helper or creates one and caches it.
        /// </summary>
        /// <returns></returns>
        public static RegexReplacement GetOrCreate(WeakReference<RegexReplacement> replRef, string replacement, Hashtable caps,
            int capsize, Hashtable capnames, RegexOptions roptions)
        {
            RegexReplacement repl;

            if (!replRef.TryGetTarget(out repl) || !repl.Pattern.Equals(replacement))
            {
                repl = RegexParser.ParseReplacement(replacement, caps, capsize, capnames, roptions);
                replRef.SetTarget(repl);
            }

            return repl;
        }

        /// <summary>
        /// The original pattern string
        /// </summary>
        public string Pattern { get; }

        /// <summary>
        /// Given a Match, emits into the ValueStringBuilder the evaluated
        /// substitution pattern.
        /// </summary>
        public void Replace(ReadOnlySpan<char> input, ref ValueStringBuilder vsb, Match match)
        {
            for (int i = 0; i < _rules.Count; i++)
            {
                int r = _rules[i];
                if (r >= 0)   // string lookup
                    vsb.Append(_strings[r]);
                else if (r < -Specials) // group lookup
                    vsb.Append(match.GroupToStringImpl(input, -Specials - 1 - r));
                else
                {
                    switch (-Specials - 1 - r)
                    { // special insertion patterns
                        case LeftPortion:
                            vsb.Append(match.GetLeftSubstring(input));
                            break;
                        case RightPortion:
                            vsb.Append(match.GetRightSubstring(input));
                            break;
                        case LastGroup:
                            vsb.Append(match.LastGroupToStringImpl(input));
                            break;
                        case WholeString:
                            vsb.Append(input);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Given a Match, emits into the ValueStringBuilder the evaluated
        /// Right-to-Left substitution pattern.
        /// </summary>
        public void ReplaceRTL(ReadOnlySpan<char> input, ref ValueStringBuilder vsb, Match match)
        {
            for (int i = _rules.Count - 1; i >= 0; i--)
            {
                int r = _rules[i];
                if (r >= 0)  // string lookup
                    vsb.AppendReversed(_strings[r]);
                else if (r < -Specials) // group lookup
                    vsb.AppendReversed(match.GroupToStringImpl(input, -Specials - 1 - r));
                else
                {
                    switch (-Specials - 1 - r)
                    { // special insertion patterns
                        case LeftPortion:
                            vsb.AppendReversed(match.GetLeftSubstring(input));
                            break;
                        case RightPortion:
                            vsb.AppendReversed(match.GetRightSubstring(input));
                            break;
                        case LastGroup:
                            vsb.AppendReversed(match.LastGroupToStringImpl(input));
                            break;
                        case WholeString:
                            vsb.AppendReversed(input);
                            break;
                    }
                }
            }
        }
    }
}
