// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// The RegexInterpreter executes a block of regular expression codes
// while consuming input.

using System.Buffers;
using System.Diagnostics;
using System.Globalization;

namespace System.Text.RegularExpressions
{
    internal sealed class RegexInterpreter
    {
        private int _runtextbeg;                // beginning of text to search
        private int _runtextend;                // end of text to search
        private int _runtextstart;              // starting point for search
                                                
        private int _runtextpos;                // current position in text

        private int[] _runtrack;                // The backtracking stack.  Opcodes use this to store data regarding
        private int _runtrackpos;               // what they have matched and where to backtrack to.  Each "frame" on
                                                // the stack takes the form of [CodePosition Data1 Data2...], where
                                                // CodePosition is the position of the current opcode and
                                                // the data values are all optional.  The CodePosition can be negative, and
                                                // these values (also called "back2") are used by the BranchMark family of opcodes
                                                // to indicate whether they are backtracking after a successful or failed
                                                // match.
                                                // When we backtrack, we pop the CodePosition off the stack, set the current
                                                // instruction pointer to that code position, and mark the opcode
                                                // with a backtracking flag ("Back").  Each opcode then knows how to
                                                // handle its own data.

        private int[] _runstack;                // This stack is used to track text positions across different opcodes.
        private int _runstackpos;               // For example, in /(a*b)+/, the parentheses result in a SetMark/CaptureMark
                                                // pair. SetMark records the text position before we match a*b.  Then
                                                // CaptureMark uses that position to figure out where the capture starts.
                                                // Opcodes which push onto this stack are always paired with other opcodes
                                                // which will pop the value from it later.  A successful match should mean
                                                // that this stack is empty.

        private int[] _runcrawl;                // The crawl stack is used to keep track of captures.  Every time a group
        private int _runcrawlpos;               // has a capture, we push its group number onto the runcrawl stack.  In
                                                // the case of a balanced match, we push BOTH groups onto the stack.

        private int _runtrackcount;             // count of states that may do backtracking

        private Match _runmatch;                // result object
        private Regex _runregex;                // regex object

        private int _timeout;                   // timeout in milliseconds (needed for actual)
        private bool _ignoreTimeout;
        private int _timeoutOccursAt;

        // We have determined this value in a series of experiments where x86 retail
        // builds (ono-lab-optimized) were run on different pattern/input pairs. Larger values
        // of TimeoutCheckFrequency did not tend to increase performance; smaller values
        // of TimeoutCheckFrequency tended to slow down the execution.
        private const int TimeoutCheckFrequency = 1000;
        private int _timeoutChecksToSkip;

        private readonly RegexCode _code;
        private readonly CultureInfo _culture;
        private int _operator;
        private int _codepos;
        private bool _rightToLeft;
        private bool _caseInsensitive;

        public RegexInterpreter(RegexCode code, CultureInfo culture)
        {
            Debug.Assert(code != null, "code cannot be null.");
            Debug.Assert(culture != null, "culture cannot be null.");

            _code = code;
            _culture = culture;
        }

        /// <summary>
        /// Scans the string to find the first match. Uses the Match object
        /// both to feed text in and as a place to store matches that come out.
        ///
        /// All the action is in the abstract Go() method defined by subclasses. Our
        /// responsibility is to load up the class members (as done here) before
        /// calling Go.
        ///
        /// The optimizer can compute a set of candidate starting characters,
        /// and we could use a separate method Skip() that will quickly scan past
        /// any characters that we know can't match.
        /// </summary>
        public Match Scan(Regex regex, in MemoryOrPinnedSpan<char> mem, ReadOnlySpan<char> input, int textbeg, int textend, int textstart, int prevlen, bool quick, TimeSpan timeout)
        {
            _ignoreTimeout = (Regex.InfiniteMatchTimeout == timeout);
            _timeout = _ignoreTimeout
                                    ? (int)Regex.InfiniteMatchTimeout.TotalMilliseconds
                                    : (int)(timeout.TotalMilliseconds + 0.5); // Round
            _runregex = regex;
            _runtextbeg = textbeg;
            _runtextend = textend;
            _runtextstart = textstart;

            int bump = _runregex.RightToLeft ? -1 : 1;
            int stoppos = _runregex.RightToLeft ? _runtextbeg : _runtextend;

            _runtextpos = textstart;

            // If previous match was empty or failed, advance by one before matching
            if (prevlen == 0)
            {
                if (_runtextpos == stoppos)
                    return Match.Empty;

                _runtextpos += bump;
            }

            StartTimeoutWatch();
            bool initted = false;

            for (; ; )
            {
#if DEBUG
                if (_runregex.Debug)
                {
                    Debug.WriteLine("");
                    Debug.WriteLine("Search range: from " + _runtextbeg.ToString(CultureInfo.InvariantCulture) + " to " + _runtextend.ToString(CultureInfo.InvariantCulture));
                    Debug.WriteLine("Firstchar search starting at " + _runtextpos.ToString(CultureInfo.InvariantCulture) + " stopping at " + stoppos.ToString(CultureInfo.InvariantCulture));
                }
#endif
                if (FindFirstChar(input))
                {
                    CheckTimeout(input);

                    if (!initted)
                    {
                        InitMatch(mem);
                        initted = true;
                    }
#if DEBUG
                    if (_runregex.Debug)
                    {
                        Debug.WriteLine("Executing engine starting at " + _runtextpos.ToString(CultureInfo.InvariantCulture));
                        Debug.WriteLine("");
                    }
#endif
                    Go(input);

                    if (_runmatch._matchcount[0] > 0)
                    {
                        // in quick mode, a successful match returns null, and
                        // the allocated match object is left in the cache
                        if (quick)
                            return null;

                        // We'll return a match even if it touches a previous empty match
                        return TidyMatch();
                    }

                    // reset state for another go
                    _runtrackpos = _runtrack.Length;
                    _runstackpos = _runstack.Length;
                    _runcrawlpos = _runcrawl.Length;
                }

                // failure!

                if (_runtextpos == stoppos)
                {
                    return Match.Empty;
                }

                // Recognize leading []* and various anchors, and bump on failure accordingly

                // Bump by one and start again

                _runtextpos += bump;
            }
            // We never get here
        }

        private void StartTimeoutWatch()
        {
            if (_ignoreTimeout)
                return;

            _timeoutChecksToSkip = TimeoutCheckFrequency;

            // We are using Environment.TickCount and not Timewatch for performance reasons.
            // Environment.TickCount is an int that cycles. We intentionally let timeoutOccursAt
            // overflow it will still stay ahead of Environment.TickCount for comparisons made
            // in DoCheckTimeout():
            unchecked
            {
                _timeoutOccursAt = Environment.TickCount + _timeout;
            }
        }

        private void CheckTimeout(ReadOnlySpan<char> input)
        {
            if (_ignoreTimeout)
                return;

            DoCheckTimeout(input);
        }

        private void DoCheckTimeout(ReadOnlySpan<char> input)
        {
            if (--_timeoutChecksToSkip != 0)
                return;

            _timeoutChecksToSkip = TimeoutCheckFrequency;

            // Note that both, Environment.TickCount and timeoutOccursAt are ints and can overflow and become negative.
            // See the comment in StartTimeoutWatch().

            int currentMillis = Environment.TickCount;

            if (currentMillis < _timeoutOccursAt)
                return;

            if (0 > _timeoutOccursAt && 0 < currentMillis)
                return;

#if DEBUG
            if (_runregex.Debug)
            {
                Debug.WriteLine("");
                Debug.WriteLine("RegEx match timeout occurred!");
                Debug.WriteLine("Specified timeout:       " + TimeSpan.FromMilliseconds(_timeout).ToString());
                Debug.WriteLine("Timeout check frequency: " + TimeoutCheckFrequency);
                Debug.WriteLine("Search pattern:          " + _runregex.pattern);
                Debug.WriteLine("Input:                   " + input.ToString());
                Debug.WriteLine("About to throw RegexMatchTimeoutException.");
            }
#endif

            throw new RegexMatchTimeoutException(input.ToString(), _runregex.pattern, TimeSpan.FromMilliseconds(_timeout));
        }

        /// <summary>
        /// Initializes all the data members that are used by Go()
        /// </summary>
        private void InitMatch(MemoryOrPinnedSpan<char> input)
        {
            // Use a hashtabled Match object if the capture numbers are sparse

            if (_runmatch == null)
            {
                if (_runregex.caps != null)
                    _runmatch = new MatchSparse(_runregex, _runregex.caps, _runregex.capsize, input, _runtextbeg, _runtextend - _runtextbeg, _runtextstart);
                else
                    _runmatch = new Match(_runregex, _runregex.capsize, input, _runtextbeg, _runtextend - _runtextbeg, _runtextstart);
            }
            else
            {
                _runmatch.Reset(_runregex, input, _runtextbeg, _runtextend, _runtextstart);
            }

            // note we test runcrawl, because it is the last one to be allocated
            // If there is an alloc failure in the middle of the three allocations,
            // we may still return to reuse this instance, and we want to behave
            // as if the allocations didn't occur. (we used to test _trackcount != 0)

            if (_runcrawl != null)
            {
                _runtrackpos = _runtrack.Length;
                _runstackpos = _runstack.Length;
                _runcrawlpos = _runcrawl.Length;
                return;
            }

            InitTrackCount();

            int tracksize = _runtrackcount * 8;
            int stacksize = _runtrackcount * 8;

            if (tracksize < 32)
                tracksize = 32;
            if (stacksize < 16)
                stacksize = 16;

            _runtrack = new int[tracksize];
            _runtrackpos = tracksize;

            _runstack = new int[stacksize];
            _runstackpos = stacksize;

            _runcrawl = new int[32];
            _runcrawlpos = 32;
        }

        /// <summary>
        /// Put match in its canonical form before returning it.
        /// </summary>
        private Match TidyMatch()
        {
            Match match = _runmatch;
            _runmatch = null;
            match.Tidy(_runtextpos);

            return match;
        }

        /// <summary>
        /// Called by the implementation of Go() to increase the size of storage
        /// </summary>
        private void EnsureStorage()
        {
            if (_runstackpos < _runtrackcount * 4)
                DoubleStack();
            if (_runtrackpos < _runtrackcount * 4)
                DoubleTrack();
        }

        /// <summary>
        /// Called by the implementation of Go() to decide whether the pos
        /// at the specified index is a boundary or not. It's just not worth
        /// emitting inline code for this logic.
        /// </summary>
        private bool IsBoundary(ReadOnlySpan<char> text, int index, int startpos, int endpos)
        {
            return (index > startpos && RegexCharClass.IsWordChar(text[index - 1])) !=
                   (index < endpos && RegexCharClass.IsWordChar(text[index]));
        }

        private bool IsECMABoundary(ReadOnlySpan<char> text, int index, int startpos, int endpos)
        {
            return (index > startpos && RegexCharClass.IsECMAWordChar(text[index - 1])) !=
                   (index < endpos && RegexCharClass.IsECMAWordChar(text[index]));
        }

        private static bool CharInSet(char ch, string set, string category)
        {
            string charClass = RegexCharClass.ConvertOldStringsToClass(set, category);
            return RegexCharClass.CharInClass(ch, charClass);
        }

        private static bool CharInClass(char ch, string charClass)
        {
            return RegexCharClass.CharInClass(ch, charClass);
        }

        /// <summary>
        /// Called by the implementation of Go() to increase the size of the
        /// backtracking stack.
        /// </summary>
        private void DoubleTrack()
        {
            int[] newtrack;

            newtrack = new int[_runtrack.Length * 2];

            Array.Copy(_runtrack, 0, newtrack, _runtrack.Length, _runtrack.Length);
            _runtrackpos += _runtrack.Length;
            _runtrack = newtrack;
        }

        /// <summary>
        /// Called by the implementation of Go() to increase the size of the
        /// grouping stack.
        /// </summary>
        private void DoubleStack()
        {
            int[] newstack;

            newstack = new int[_runstack.Length * 2];

            Array.Copy(_runstack, 0, newstack, _runstack.Length, _runstack.Length);
            _runstackpos += _runstack.Length;
            _runstack = newstack;
        }

        /// <summary>
        /// Increases the size of the longjump unrolling stack.
        /// </summary>
        private void DoubleCrawl()
        {
            int[] newcrawl;

            newcrawl = new int[_runcrawl.Length * 2];

            Array.Copy(_runcrawl, 0, newcrawl, _runcrawl.Length, _runcrawl.Length);
            _runcrawlpos += _runcrawl.Length;
            _runcrawl = newcrawl;
        }

        /// <summary>
        /// Save a number on the longjump unrolling stack
        /// </summary>
        private void Crawl(int i)
        {
            if (_runcrawlpos == 0)
                DoubleCrawl();

            _runcrawl[--_runcrawlpos] = i;
        }

        /// <summary>
        /// Remove a number from the longjump unrolling stack
        /// </summary>
        private int Popcrawl()
        {
            return _runcrawl[_runcrawlpos++];
        }

        /// <summary>
        /// Get the height of the stack
        /// </summary>
        private int Crawlpos()
        {
            return _runcrawl.Length - _runcrawlpos;
        }

        /// <summary>
        /// Called by Go() to capture a subexpression. Note that the
        /// capnum used here has already been mapped to a non-sparse
        /// index (by the code generator RegexWriter).
        /// </summary>
        private void Capture(int capnum, int start, int end)
        {
            if (end < start)
            {
                int T;

                T = end;
                end = start;
                start = T;
            }

            Crawl(capnum);
            _runmatch.AddMatch(capnum, start, end - start);
        }

        /// <summary>
        /// Called by Go() to capture a subexpression. Note that the
        /// capnum used here has already been mapped to a non-sparse
        /// index (by the code generator RegexWriter).
        /// </summary>
        private void TransferCapture(int capnum, int uncapnum, int start, int end)
        {
            int start2;
            int end2;

            // these are the two intervals that are cancelling each other

            if (end < start)
            {
                int T;

                T = end;
                end = start;
                start = T;
            }

            start2 = MatchIndex(uncapnum);
            end2 = start2 + MatchLength(uncapnum);

            // The new capture gets the innermost defined interval

            if (start >= end2)
            {
                end = start;
                start = end2;
            }
            else if (end <= start2)
            {
                start = start2;
            }
            else
            {
                if (end > end2)
                    end = end2;
                if (start2 > start)
                    start = start2;
            }

            Crawl(uncapnum);
            _runmatch.BalanceMatch(uncapnum);

            if (capnum != -1)
            {
                Crawl(capnum);
                _runmatch.AddMatch(capnum, start, end - start);
            }
        }

        /*
         * Called by Go() to revert the last capture
         */
        private void Uncapture()
        {
            int capnum = Popcrawl();
            _runmatch.RemoveMatch(capnum);
        }

        /// <summary>
        /// Call out to runmatch to get around visibility issues
        /// </summary>
        private bool IsMatched(int cap)
        {
            return _runmatch.IsMatched(cap);
        }

        /// <summary>
        /// Call out to runmatch to get around visibility issues
        /// </summary>
        private int MatchIndex(int cap)
        {
            return _runmatch.MatchIndex(cap);
        }

        /// <summary>
        /// Call out to runmatch to get around visibility issues
        /// </summary>
        private int MatchLength(int cap)
        {
            return _runmatch.MatchLength(cap);
        }

        /// <summary>
        /// InitTrackCount must initialize the runtrackcount field; this is
        /// used to know how large the initial runtrack and runstack arrays
        /// must be.
        /// </summary>
        private void InitTrackCount()
        {
            _runtrackcount = _code.TrackCount;
        }

        private void Advance(int i)
        {
            _codepos += (i + 1);
            SetOperator(_code.Codes[_codepos]);
        }

        private void Goto(int newpos)
        {
            // when branching backward, ensure storage
            if (newpos < _codepos)
                EnsureStorage();

            SetOperator(_code.Codes[newpos]);
            _codepos = newpos;
        }

        private void Textto(int newpos)
        {
            _runtextpos = newpos;
        }

        private void Trackto(int newpos)
        {
            _runtrackpos = _runtrack.Length - newpos;
        }

        private int Textstart()
        {
            return _runtextstart;
        }

        private int Textpos()
        {
            return _runtextpos;
        }

        // push onto the backtracking stack
        private int Trackpos()
        {
            return _runtrack.Length - _runtrackpos;
        }

        private void TrackPush()
        {
            _runtrack[--_runtrackpos] = _codepos;
        }

        private void TrackPush(int I1)
        {
            _runtrack[--_runtrackpos] = I1;
            _runtrack[--_runtrackpos] = _codepos;
        }

        private void TrackPush(int I1, int I2)
        {
            _runtrack[--_runtrackpos] = I1;
            _runtrack[--_runtrackpos] = I2;
            _runtrack[--_runtrackpos] = _codepos;
        }

        private void TrackPush(int I1, int I2, int I3)
        {
            _runtrack[--_runtrackpos] = I1;
            _runtrack[--_runtrackpos] = I2;
            _runtrack[--_runtrackpos] = I3;
            _runtrack[--_runtrackpos] = _codepos;
        }

        private void TrackPush2(int I1)
        {
            _runtrack[--_runtrackpos] = I1;
            _runtrack[--_runtrackpos] = -_codepos;
        }

        private void TrackPush2(int I1, int I2)
        {
            _runtrack[--_runtrackpos] = I1;
            _runtrack[--_runtrackpos] = I2;
            _runtrack[--_runtrackpos] = -_codepos;
        }

        private void Backtrack()
        {
            int newpos = _runtrack[_runtrackpos++];
#if DEBUG
            if (_runmatch.Debug)
            {
                if (newpos < 0)
                    Debug.WriteLine("       Backtracking (back2) to code position " + (-newpos));
                else
                    Debug.WriteLine("       Backtracking to code position " + newpos);
            }
#endif

            if (newpos < 0)
            {
                newpos = -newpos;
                SetOperator(_code.Codes[newpos] | RegexCode.Back2);
            }
            else
            {
                SetOperator(_code.Codes[newpos] | RegexCode.Back);
            }

            // When branching backward, ensure storage
            if (newpos < _codepos)
                EnsureStorage();

            _codepos = newpos;
        }

        private void SetOperator(int op)
        {
            _caseInsensitive = (0 != (op & RegexCode.Ci));
            _rightToLeft = (0 != (op & RegexCode.Rtl));
            _operator = op & ~(RegexCode.Rtl | RegexCode.Ci);
        }

        private void TrackPop()
        {
            _runtrackpos++;
        }

        // pop framesize items from the backtracking stack
        private void TrackPop(int framesize)
        {
            _runtrackpos += framesize;
        }

        // Technically we are actually peeking at items already popped.  So if you want to
        // get and pop the top item from the stack, you do
        // TrackPop();
        // TrackPeek();
        private int TrackPeek()
        {
            return _runtrack[_runtrackpos - 1];
        }

        // get the ith element down on the backtracking stack
        private int TrackPeek(int i)
        {
            return _runtrack[_runtrackpos - i - 1];
        }

        // Push onto the grouping stack
        private void StackPush(int I1)
        {
            _runstack[--_runstackpos] = I1;
        }

        private void StackPush(int I1, int I2)
        {
            _runstack[--_runstackpos] = I1;
            _runstack[--_runstackpos] = I2;
        }

        private void StackPop()
        {
            _runstackpos++;
        }

        // pop framesize items from the grouping stack
        private void StackPop(int framesize)
        {
            _runstackpos += framesize;
        }

        // Technically we are actually peeking at items already popped.  So if you want to
        // get and pop the top item from the stack, you do
        // StackPop();
        // StackPeek();
        private int StackPeek()
        {
            return _runstack[_runstackpos - 1];
        }

        // get the ith element down on the grouping stack
        private int StackPeek(int i)
        {
            return _runstack[_runstackpos - i - 1];
        }

        private int Operator()
        {
            return _operator;
        }

        private int Operand(int i)
        {
            return _code.Codes[_codepos + i + 1];
        }

        private int Leftchars()
        {
            return _runtextpos - _runtextbeg;
        }

        private int Rightchars()
        {
            return _runtextend - _runtextpos;
        }

        private int Bump()
        {
            return _rightToLeft ? -1 : 1;
        }

        private int Forwardchars()
        {
            return _rightToLeft ? _runtextpos - _runtextbeg : _runtextend - _runtextpos;
        }

        private char Forwardcharnext(ReadOnlySpan<char> input)
        {
            char ch = (_rightToLeft ? input[--_runtextpos] : input[_runtextpos++]);

            return (_caseInsensitive ? _culture.TextInfo.ToLower(ch) : ch);
        }

        private bool Stringmatch(ReadOnlySpan<char> input, string str)
        {
            int c;
            int pos;

            if (!_rightToLeft)
            {
                if (_runtextend - _runtextpos < (c = str.Length))
                    return false;

                pos = _runtextpos + c;
            }
            else
            {
                if (_runtextpos - _runtextbeg < (c = str.Length))
                    return false;

                pos = _runtextpos;
            }

            if (!_caseInsensitive)
            {
                while (c != 0)
                    if (str[--c] != input[--pos])
                        return false;
            }
            else
            {
                while (c != 0)
                    if (str[--c] != _culture.TextInfo.ToLower(input[--pos]))
                        return false;
            }

            if (!_rightToLeft)
            {
                pos += str.Length;
            }

            _runtextpos = pos;

            return true;
        }

        private bool Refmatch(ReadOnlySpan<char> input, int index, int len)
        {
            int c;
            int pos;
            int cmpos;

            if (!_rightToLeft)
            {
                if (_runtextend - _runtextpos < len)
                    return false;

                pos = _runtextpos + len;
            }
            else
            {
                if (_runtextpos - _runtextbeg < len)
                    return false;

                pos = _runtextpos;
            }
            cmpos = index + len;

            c = len;

            if (!_caseInsensitive)
            {
                while (c-- != 0)
                    if (input[--cmpos] != input[--pos])
                        return false;
            }
            else
            {
                while (c-- != 0)
                    if (_culture.TextInfo.ToLower(input[--cmpos]) != _culture.TextInfo.ToLower(input[--pos]))
                        return false;
            }

            if (!_rightToLeft)
            {
                pos += len;
            }

            _runtextpos = pos;

            return true;
        }

        private void Backwardnext()
        {
            _runtextpos += _rightToLeft ? 1 : -1;
        }

        private char CharAt(ReadOnlySpan<char> input, int j)
        {
            return input[j];
        }

        /// <summary>
        /// The responsibility of FindFirstChar() is to advance runtextpos
        /// until it is at the next position which is a candidate for the
        /// beginning of a successful match.
        /// </summary>
        private bool FindFirstChar(ReadOnlySpan<char> input)
        {
            if (0 != (_code.Anchors & (RegexFCD.Beginning | RegexFCD.Start | RegexFCD.EndZ | RegexFCD.End)))
            {
                if (!_code.RightToLeft)
                {
                    if ((0 != (_code.Anchors & RegexFCD.Beginning) && _runtextpos > _runtextbeg) ||
                        (0 != (_code.Anchors & RegexFCD.Start) && _runtextpos > _runtextstart))
                    {
                        _runtextpos = _runtextend;
                        return false;
                    }
                    if (0 != (_code.Anchors & RegexFCD.EndZ) && _runtextpos < _runtextend - 1)
                    {
                        _runtextpos = _runtextend - 1;
                    }
                    else if (0 != (_code.Anchors & RegexFCD.End) && _runtextpos < _runtextend)
                    {
                        _runtextpos = _runtextend;
                    }
                }
                else
                {
                    if ((0 != (_code.Anchors & RegexFCD.End) && _runtextpos < _runtextend) ||
                        (0 != (_code.Anchors & RegexFCD.EndZ) && (_runtextpos < _runtextend - 1 ||
                                                               (_runtextpos == _runtextend - 1 && CharAt(input, _runtextpos) != '\n'))) ||
                        (0 != (_code.Anchors & RegexFCD.Start) && _runtextpos < _runtextstart))
                    {
                        _runtextpos = _runtextbeg;
                        return false;
                    }
                    if (0 != (_code.Anchors & RegexFCD.Beginning) && _runtextpos > _runtextbeg)
                    {
                        _runtextpos = _runtextbeg;
                    }
                }

                if (_code.BMPrefix != null)
                {
                    return _code.BMPrefix.IsMatch(input, _runtextpos, _runtextbeg, _runtextend);
                }

                return true; // found a valid start or end anchor
            }
            else if (_code.BMPrefix != null)
            {
                _runtextpos = _code.BMPrefix.Scan(input, _runtextpos, _runtextbeg, _runtextend);

                if (_runtextpos == -1)
                {
                    _runtextpos = (_code.RightToLeft ? _runtextbeg : _runtextend);
                    return false;
                }

                return true;
            }
            else if (_code.FCPrefix == null)
            {
                return true;
            }

            _rightToLeft = _code.RightToLeft;
            _caseInsensitive = _code.FCPrefix.GetValueOrDefault().CaseInsensitive;
            string set = _code.FCPrefix.GetValueOrDefault().Prefix;

            if (RegexCharClass.IsSingleton(set))
            {
                char ch = RegexCharClass.SingletonChar(set);

                for (int i = Forwardchars(); i > 0; i--)
                {
                    if (ch == Forwardcharnext(input))
                    {
                        Backwardnext();
                        return true;
                    }
                }
            }
            else
            {
                for (int i = Forwardchars(); i > 0; i--)
                {
                    if (RegexCharClass.CharInClass(Forwardcharnext(input), set))
                    {
                        Backwardnext();
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// The responsibility of Go() is to run the regular expression at
        /// runtextpos and call Capture() on all the captured subexpressions,
        /// then to leave runtextpos at the ending position. It should leave
        /// runtextpos where it started if there was no match.
        /// </summary>
        private void Go(ReadOnlySpan<char> input)
        {
            Goto(0);

            int advance = -1;
            for (; ;)
            {
                if (advance >= 0)
                {
                    // https://github.com/dotnet/coreclr/pull/14850#issuecomment-342256447
                    // Single common Advance call to reduce method size; and single method inline point
                    Advance(advance);
                    advance = -1;
                }
#if DEBUG
                if (_runmatch.Debug)
                {
                    DumpState(input);
                }
#endif

                CheckTimeout(input);

                switch (Operator())
                {
                    case RegexCode.Stop:
                        return;

                    case RegexCode.Nothing:
                        break;

                    case RegexCode.Goto:
                        Goto(Operand(0));
                        continue;

                    case RegexCode.Testref:
                        if (!IsMatched(Operand(0)))
                            break;
                        advance = 1;
                        continue;

                    case RegexCode.Lazybranch:
                        TrackPush(Textpos());
                        advance = 1;
                        continue;

                    case RegexCode.Lazybranch | RegexCode.Back:
                        TrackPop();
                        Textto(TrackPeek());
                        Goto(Operand(0));
                        continue;

                    case RegexCode.Setmark:
                        StackPush(Textpos());
                        TrackPush();
                        advance = 0;
                        continue;

                    case RegexCode.Nullmark:
                        StackPush(-1);
                        TrackPush();
                        advance = 0;
                        continue;

                    case RegexCode.Setmark | RegexCode.Back:
                    case RegexCode.Nullmark | RegexCode.Back:
                        StackPop();
                        break;

                    case RegexCode.Getmark:
                        StackPop();
                        TrackPush(StackPeek());
                        Textto(StackPeek());
                        advance = 0;
                        continue;

                    case RegexCode.Getmark | RegexCode.Back:
                        TrackPop();
                        StackPush(TrackPeek());
                        break;

                    case RegexCode.Capturemark:
                        if (Operand(1) != -1 && !IsMatched(Operand(1)))
                            break;
                        StackPop();
                        if (Operand(1) != -1)
                            TransferCapture(Operand(0), Operand(1), StackPeek(), Textpos());
                        else
                            Capture(Operand(0), StackPeek(), Textpos());
                        TrackPush(StackPeek());

                        advance = 2;

                        continue;

                    case RegexCode.Capturemark | RegexCode.Back:
                        TrackPop();
                        StackPush(TrackPeek());
                        Uncapture();
                        if (Operand(0) != -1 && Operand(1) != -1)
                            Uncapture();

                        break;

                    case RegexCode.Branchmark:
                        {
                            int matched;
                            StackPop();

                            matched = Textpos() - StackPeek();

                            if (matched != 0)
                            {                     // Nonempty match -> loop now
                                TrackPush(StackPeek(), Textpos());  // Save old mark, textpos
                                StackPush(Textpos());               // Make new mark
                                Goto(Operand(0));                   // Loop
                            }
                            else
                            {                                  // Empty match -> straight now
                                TrackPush2(StackPeek());            // Save old mark
                                advance = 1;                        // Straight
                            }
                            continue;
                        }

                    case RegexCode.Branchmark | RegexCode.Back:
                        TrackPop(2);
                        StackPop();
                        Textto(TrackPeek(1));                       // Recall position
                        TrackPush2(TrackPeek());                    // Save old mark
                        advance = 1;                                // Straight
                        continue;

                    case RegexCode.Branchmark | RegexCode.Back2:
                        TrackPop();
                        StackPush(TrackPeek());                     // Recall old mark
                        break;                                      // Backtrack

                    case RegexCode.Lazybranchmark:
                        {
                            // We hit this the first time through a lazy loop and after each
                            // successful match of the inner expression.  It simply continues
                            // on and doesn't loop.
                            StackPop();

                            int oldMarkPos = StackPeek();

                            if (Textpos() != oldMarkPos)
                            {              // Nonempty match -> try to loop again by going to 'back' state
                                if (oldMarkPos != -1)
                                    TrackPush(oldMarkPos, Textpos());   // Save old mark, textpos
                                else
                                    TrackPush(Textpos(), Textpos());
                            }
                            else
                            {
                                // The inner expression found an empty match, so we'll go directly to 'back2' if we
                                // backtrack.  In this case, we need to push something on the stack, since back2 pops.
                                // However, in the case of ()+? or similar, this empty match may be legitimate, so push the text
                                // position associated with that empty match.
                                StackPush(oldMarkPos);

                                TrackPush2(StackPeek());                // Save old mark
                            }
                            advance = 1;
                            continue;
                        }

                    case RegexCode.Lazybranchmark | RegexCode.Back:
                        {
                            // After the first time, Lazybranchmark | RegexCode.Back occurs
                            // with each iteration of the loop, and therefore with every attempted
                            // match of the inner expression.  We'll try to match the inner expression,
                            // then go back to Lazybranchmark if successful.  If the inner expression
                            // fails, we go to Lazybranchmark | RegexCode.Back2
                            int pos;

                            TrackPop(2);
                            pos = TrackPeek(1);
                            TrackPush2(TrackPeek());                // Save old mark
                            StackPush(pos);                         // Make new mark
                            Textto(pos);                            // Recall position
                            Goto(Operand(0));                       // Loop
                            continue;
                        }

                    case RegexCode.Lazybranchmark | RegexCode.Back2:
                        // The lazy loop has failed.  We'll do a true backtrack and
                        // start over before the lazy loop.
                        StackPop();
                        TrackPop();
                        StackPush(TrackPeek());                      // Recall old mark
                        break;

                    case RegexCode.Setcount:
                        StackPush(Textpos(), Operand(0));
                        TrackPush();
                        advance = 1;
                        continue;

                    case RegexCode.Nullcount:
                        StackPush(-1, Operand(0));
                        TrackPush();
                        advance = 1;
                        continue;

                    case RegexCode.Setcount | RegexCode.Back:
                        StackPop(2);
                        break;

                    case RegexCode.Nullcount | RegexCode.Back:
                        StackPop(2);
                        break;

                    case RegexCode.Branchcount:
                        // StackPush:
                        //  0: Mark
                        //  1: Count
                        {
                            StackPop(2);
                            int mark = StackPeek();
                            int count = StackPeek(1);
                            int matched = Textpos() - mark;

                            if (count >= Operand(1) || (matched == 0 && count >= 0))
                            {                                   // Max loops or empty match -> straight now
                                TrackPush2(mark, count);            // Save old mark, count
                                advance = 2;                        // Straight
                            }
                            else
                            {                                  // Nonempty match -> count+loop now
                                TrackPush(mark);                    // remember mark
                                StackPush(Textpos(), count + 1);    // Make new mark, incr count
                                Goto(Operand(0));                   // Loop
                            }
                            continue;
                        }

                    case RegexCode.Branchcount | RegexCode.Back:
                        // TrackPush:
                        //  0: Previous mark
                        // StackPush:
                        //  0: Mark (= current pos, discarded)
                        //  1: Count
                        TrackPop();
                        StackPop(2);
                        if (StackPeek(1) > 0)
                        {                         // Positive -> can go straight
                            Textto(StackPeek());                        // Zap to mark
                            TrackPush2(TrackPeek(), StackPeek(1) - 1);  // Save old mark, old count
                            advance = 2;                                // Straight
                            continue;
                        }
                        StackPush(TrackPeek(), StackPeek(1) - 1);       // recall old mark, old count
                        break;

                    case RegexCode.Branchcount | RegexCode.Back2:
                        // TrackPush:
                        //  0: Previous mark
                        //  1: Previous count
                        TrackPop(2);
                        StackPush(TrackPeek(), TrackPeek(1));           // Recall old mark, old count
                        break;                                          // Backtrack


                    case RegexCode.Lazybranchcount:
                        // StackPush:
                        //  0: Mark
                        //  1: Count
                        {
                            StackPop(2);
                            int mark = StackPeek();
                            int count = StackPeek(1);

                            if (count < 0)
                            {                        // Negative count -> loop now
                                TrackPush2(mark);                   // Save old mark
                                StackPush(Textpos(), count + 1);    // Make new mark, incr count
                                Goto(Operand(0));                   // Loop
                            }
                            else
                            {                                  // Nonneg count -> straight now
                                TrackPush(mark, count, Textpos());  // Save mark, count, position
                                advance = 2;                        // Straight
                            }
                            continue;
                        }

                    case RegexCode.Lazybranchcount | RegexCode.Back:
                        // TrackPush:
                        //  0: Mark
                        //  1: Count
                        //  2: Textpos
                        {
                            TrackPop(3);
                            int mark = TrackPeek();
                            int textpos = TrackPeek(2);

                            if (TrackPeek(1) < Operand(1) && textpos != mark)
                            { // Under limit and not empty match -> loop
                                Textto(textpos);                            // Recall position
                                StackPush(textpos, TrackPeek(1) + 1);       // Make new mark, incr count
                                TrackPush2(mark);                           // Save old mark
                                Goto(Operand(0));                           // Loop
                                continue;
                            }
                            else
                            {                                          // Max loops or empty match -> backtrack
                                StackPush(TrackPeek(), TrackPeek(1));       // Recall old mark, count
                                break;                                      // backtrack
                            }
                        }

                    case RegexCode.Lazybranchcount | RegexCode.Back2:
                        // TrackPush:
                        //  0: Previous mark
                        // StackPush:
                        //  0: Mark (== current pos, discarded)
                        //  1: Count
                        TrackPop();
                        StackPop(2);
                        StackPush(TrackPeek(), StackPeek(1) - 1);   // Recall old mark, count
                        break;                                      // Backtrack

                    case RegexCode.Setjump:
                        StackPush(Trackpos(), Crawlpos());
                        TrackPush();
                        advance = 0;
                        continue;

                    case RegexCode.Setjump | RegexCode.Back:
                        StackPop(2);
                        break;

                    case RegexCode.Backjump:
                        // StackPush:
                        //  0: Saved trackpos
                        //  1: Crawlpos
                        StackPop(2);
                        Trackto(StackPeek());

                        while (Crawlpos() != StackPeek(1))
                            Uncapture();

                        break;

                    case RegexCode.Forejump:
                        // StackPush:
                        //  0: Saved trackpos
                        //  1: Crawlpos
                        StackPop(2);
                        Trackto(StackPeek());
                        TrackPush(StackPeek(1));
                        advance = 0;
                        continue;

                    case RegexCode.Forejump | RegexCode.Back:
                        // TrackPush:
                        //  0: Crawlpos
                        TrackPop();

                        while (Crawlpos() != TrackPeek())
                            Uncapture();

                        break;

                    case RegexCode.Bol:
                        if (Leftchars() > 0 && CharAt(input, Textpos() - 1) != '\n')
                            break;
                        advance = 0;
                        continue;

                    case RegexCode.Eol:
                        if (Rightchars() > 0 && CharAt(input, Textpos()) != '\n')
                            break;
                        advance = 0;
                        continue;

                    case RegexCode.Boundary:
                        if (!IsBoundary(input, Textpos(), _runtextbeg, _runtextend))
                            break;
                        advance = 0;
                        continue;

                    case RegexCode.Nonboundary:
                        if (IsBoundary(input, Textpos(), _runtextbeg, _runtextend))
                            break;
                        advance = 0;
                        continue;

                    case RegexCode.ECMABoundary:
                        if (!IsECMABoundary(input, Textpos(), _runtextbeg, _runtextend))
                            break;
                        advance = 0;
                        continue;

                    case RegexCode.NonECMABoundary:
                        if (IsECMABoundary(input, Textpos(), _runtextbeg, _runtextend))
                            break;
                        advance = 0;
                        continue;

                    case RegexCode.Beginning:
                        if (Leftchars() > 0)
                            break;
                        advance = 0;
                        continue;

                    case RegexCode.Start:
                        if (Textpos() != Textstart())
                            break;
                        advance = 0;
                        continue;

                    case RegexCode.EndZ:
                        if (Rightchars() > 1 || Rightchars() == 1 && CharAt(input, Textpos()) != '\n')
                            break;
                        advance = 0;
                        continue;

                    case RegexCode.End:
                        if (Rightchars() > 0)
                            break;
                        advance = 0;
                        continue;

                    case RegexCode.One:
                        if (Forwardchars() < 1 || Forwardcharnext(input) != (char)Operand(0))
                            break;

                        advance = 1;
                        continue;

                    case RegexCode.Notone:
                        if (Forwardchars() < 1 || Forwardcharnext(input) == (char)Operand(0))
                            break;

                        advance = 1;
                        continue;

                    case RegexCode.Set:
                        if (Forwardchars() < 1 || !RegexCharClass.CharInClass(Forwardcharnext(input), _code.Strings[Operand(0)]))
                            break;

                        advance = 1;
                        continue;

                    case RegexCode.Multi:
                        {
                            if (!Stringmatch(input, _code.Strings[Operand(0)]))
                                break;

                            advance = 1;
                            continue;
                        }

                    case RegexCode.Ref:
                        {
                            int capnum = Operand(0);

                            if (IsMatched(capnum))
                            {
                                if (!Refmatch(input, MatchIndex(capnum), MatchLength(capnum)))
                                    break;
                            }
                            else
                            {
                                if ((_runregex.roptions & RegexOptions.ECMAScript) == 0)
                                    break;
                            }

                            advance = 1;
                            continue;
                        }

                    case RegexCode.Onerep:
                        {
                            int c = Operand(1);

                            if (Forwardchars() < c)
                                break;

                            char ch = (char)Operand(0);

                            while (c-- > 0)
                                if (Forwardcharnext(input) != ch)
                                    goto BreakBackward;

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Notonerep:
                        {
                            int c = Operand(1);

                            if (Forwardchars() < c)
                                break;

                            char ch = (char)Operand(0);

                            while (c-- > 0)
                                if (Forwardcharnext(input) == ch)
                                    goto BreakBackward;

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Setrep:
                        {
                            int c = Operand(1);

                            if (Forwardchars() < c)
                                break;

                            string set = _code.Strings[Operand(0)];

                            while (c-- > 0)
                                if (!RegexCharClass.CharInClass(Forwardcharnext(input), set))
                                    goto BreakBackward;

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Oneloop:
                        {
                            int c = Operand(1);

                            if (c > Forwardchars())
                                c = Forwardchars();

                            char ch = (char)Operand(0);
                            int i;

                            for (i = c; i > 0; i--)
                            {
                                if (Forwardcharnext(input) != ch)
                                {
                                    Backwardnext();
                                    break;
                                }
                            }

                            if (c > i)
                                TrackPush(c - i - 1, Textpos() - Bump());

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Notoneloop:
                        {
                            int c = Operand(1);

                            if (c > Forwardchars())
                                c = Forwardchars();

                            char ch = (char)Operand(0);
                            int i;

                            for (i = c; i > 0; i--)
                            {
                                if (Forwardcharnext(input) == ch)
                                {
                                    Backwardnext();
                                    break;
                                }
                            }

                            if (c > i)
                                TrackPush(c - i - 1, Textpos() - Bump());

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Setloop:
                        {
                            int c = Operand(1);

                            if (c > Forwardchars())
                                c = Forwardchars();

                            string set = _code.Strings[Operand(0)];
                            int i;

                            for (i = c; i > 0; i--)
                            {
                                if (!RegexCharClass.CharInClass(Forwardcharnext(input), set))
                                {
                                    Backwardnext();
                                    break;
                                }
                            }

                            if (c > i)
                                TrackPush(c - i - 1, Textpos() - Bump());

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Oneloop | RegexCode.Back:
                    case RegexCode.Notoneloop | RegexCode.Back:
                        {
                            TrackPop(2);
                            int i = TrackPeek();
                            int pos = TrackPeek(1);

                            Textto(pos);

                            if (i > 0)
                                TrackPush(i - 1, pos - Bump());

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Setloop | RegexCode.Back:
                        {
                            TrackPop(2);
                            int i = TrackPeek();
                            int pos = TrackPeek(1);

                            Textto(pos);

                            if (i > 0)
                                TrackPush(i - 1, pos - Bump());

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Onelazy:
                    case RegexCode.Notonelazy:
                        {
                            int c = Operand(1);

                            if (c > Forwardchars())
                                c = Forwardchars();

                            if (c > 0)
                                TrackPush(c - 1, Textpos());

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Setlazy:
                        {
                            int c = Operand(1);

                            if (c > Forwardchars())
                                c = Forwardchars();

                            if (c > 0)
                                TrackPush(c - 1, Textpos());

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Onelazy | RegexCode.Back:
                        {
                            TrackPop(2);
                            int pos = TrackPeek(1);
                            Textto(pos);

                            if (Forwardcharnext(input) != (char)Operand(0))
                                break;

                            int i = TrackPeek();

                            if (i > 0)
                                TrackPush(i - 1, pos + Bump());

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Notonelazy | RegexCode.Back:
                        {
                            TrackPop(2);
                            int pos = TrackPeek(1);
                            Textto(pos);

                            if (Forwardcharnext(input) == (char)Operand(0))
                                break;

                            int i = TrackPeek();

                            if (i > 0)
                                TrackPush(i - 1, pos + Bump());

                            advance = 2;
                            continue;
                        }

                    case RegexCode.Setlazy | RegexCode.Back:
                        {
                            TrackPop(2);
                            int pos = TrackPeek(1);
                            Textto(pos);

                            if (!RegexCharClass.CharInClass(Forwardcharnext(input), _code.Strings[Operand(0)]))
                                break;

                            int i = TrackPeek();

                            if (i > 0)
                                TrackPush(i - 1, pos + Bump());

                            advance = 2;
                            continue;
                        }

                    default:
                        throw NotImplemented.ByDesignWithMessage(SR.UnimplementedState);
                }

            BreakBackward:
                ;

                // "break Backward" comes here:
                Backtrack();
            }
        }

#if DEBUG
        private static string StackDescription(int[] a, int index)
        {
            var sb = new StringBuilder();

            sb.Append(a.Length - index);
            sb.Append('/');
            sb.Append(a.Length);

            if (sb.Length < 8)
                sb.Append(' ', 8 - sb.Length);

            sb.Append('(');

            for (int i = index; i < a.Length; i++)
            {
                if (i > index)
                    sb.Append(' ');
                sb.Append(a[i]);
            }

            sb.Append(')');

            return sb.ToString();
        }

        private string TextposDescription(ReadOnlySpan<char> input)
        {
            var sb = new StringBuilder();
            int remaining;

            sb.Append(_runtextpos);

            if (sb.Length < 8)
                sb.Append(' ', 8 - sb.Length);

            if (_runtextpos > _runtextbeg)
                sb.Append(RegexCharClass.CharDescription(input[_runtextpos - 1]));
            else
                sb.Append('^');

            sb.Append('>');

            remaining = _runtextend - _runtextpos;

            for (int i = _runtextpos; i < _runtextend; i++)
            {
                sb.Append(RegexCharClass.CharDescription(input[i]));
            }
            if (sb.Length >= 64)
            {
                sb.Length = 61;
                sb.Append("...");
            }
            else
            {
                sb.Append('$');
            }

            return sb.ToString();
        }

        private void DumpState(ReadOnlySpan<char> input)
        {
            Debug.WriteLine("Text:  " + TextposDescription(input));
            Debug.WriteLine("Track: " + StackDescription(_runtrack, _runtrackpos));
            Debug.WriteLine("Stack: " + StackDescription(_runstack, _runstackpos));

            Debug.WriteLine("       " + _code.OpcodeDescription(_codepos) +
                              ((_operator & RegexCode.Back) != 0 ? " Back" : "") +
                              ((_operator & RegexCode.Back2) != 0 ? " Back2" : ""));
            Debug.WriteLine("");
        }
#endif
    }
}
