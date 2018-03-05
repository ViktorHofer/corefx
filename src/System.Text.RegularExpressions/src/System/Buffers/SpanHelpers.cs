using System.Text;

namespace System.Buffers
{
    internal class SpanHelpers
    {
        public static string CopyInput(ReadOnlySpan<char> input, Span<char> output, bool targetSpan, out int charsWritten)
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

        public static string CopyOutput(ValueStringBuilder vsb, Span<char> output, bool reverse, bool targetSpan, out int charsWritten)
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
