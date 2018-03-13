// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text;

namespace System.Buffers
{
    internal static class SpanHelper
    {
        public static string CopyInput(this ReadOnlySpan<char> input, bool targetSpan, Span<char> destination, out int charsWritten, out bool spanSuccess)
        {
            if (targetSpan)
            {
                spanSuccess = input.TryCopyTo(destination);
                if (spanSuccess)
                    charsWritten = input.Length;
                else
                    charsWritten = 0;

                return null;
            }
            else
            {
                spanSuccess = false;
                charsWritten = 0;
                return input.ToString();
            }
        }

        public static string CopyOutput(this ValueStringBuilder vsb, bool targetSpan, Span<char> destination, out int charsWritten, out bool spanSuccess, bool reverse)
        {
            if (reverse)
                vsb.Reverse();

            if (targetSpan)
            {
                spanSuccess = vsb.TryCopyTo(destination, out charsWritten);
                return null;
            }
            else
            {
                spanSuccess = false;
                charsWritten = 0;
                return vsb.ToString();
            }
        }
    }
}
