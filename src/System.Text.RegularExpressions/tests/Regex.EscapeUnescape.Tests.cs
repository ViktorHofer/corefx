// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Xunit;

namespace System.Text.RegularExpressions.Tests
{
    public class RegexEscapeUnescapeTests
    {
        [Theory]
        [InlineData("Hello", "Hello")]
        [InlineData("#$^*+(){}<>\\|. ", @"\#\$\^\*\+\(\)\{}<>\\\|\.\ ")]
        [InlineData("\n\r\t\f", "\\n\\r\\t\\f")]
        [InlineData(@"\", @"\\")]
        [InlineData("", "")]
        public static void Escape(string str, string expected)
        {
            // Use Escape(string)
            Assert.Equal(expected, Regex.Escape(str));

            // Use Escape(ReadOnlySpan, Span)
            Span<char> output = stackalloc char[255];
            int charsWritten = Regex.Escape(str.AsSpan(), output);
            SpanTestHelpers.VerifySpan(expected, charsWritten, output);
        }

        [Fact]
        public void Escape_NullString_ThrowsArgumentNullException()
        {
            AssertExtensions.Throws<ArgumentNullException>("str", () => Regex.Escape(null));
        }

        [Theory]
        [InlineData("Hello", "Hello")]
        [InlineData(@"\#\$\^\*\+\(\)\{}<>\\\|\.\ ", "#$^*+(){}<>\\|. ")]
        [InlineData("\\n\\r\\t\\f", "\n\r\t\f")]
        [InlineData(@"\\", @"\")]
        [InlineData(@"\", "")]
        [InlineData("", "")]
        public void Unescape(string str, string expected)
        {
            Assert.Equal(expected, Regex.Unescape(str));

            // Use Escape(ReadOnlySpan, Span)
            Span<char> output = stackalloc char[255];
            int charsWritten = Regex.Unescape(str.AsSpan(), output);
            SpanTestHelpers.VerifySpan(expected, charsWritten, output);
        }

        [Fact]
        public void Unscape_NullString_ThrowsArgumentNullException()
        {
            AssertExtensions.Throws<ArgumentNullException>("str", () => Regex.Unescape(null));
        }
    }
}
