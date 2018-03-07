// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;

namespace System.Buffers
{
    internal static class SpanHelper
    {
        public static string CopyInput(this ReadOnlySpan<char> input, Span<char> output, bool targetSpan, out int charsWritten)
        {
            if (targetSpan)
            {
                if (input.TryCopyTo(output))
                    charsWritten = input.Length;
                else
                    charsWritten = 0;

                return null;
            }
            else
            {
                charsWritten = 0;
                return input.ToString();
            }
        }

        public static string CopyOutput(this ValueStringBuilder vsb, Span<char> output, bool reverse, bool targetSpan, out int charsWritten)
        {
            if (reverse)
                vsb.Reverse();

            if (targetSpan)
            {
                vsb.TryCopyTo(output, out charsWritten);
                return null;
            }
            else
            {
                charsWritten = 0;
                return vsb.ToString();
            }
        }
    }
}
