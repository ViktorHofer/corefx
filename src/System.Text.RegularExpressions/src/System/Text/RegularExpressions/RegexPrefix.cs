// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace System.Text.RegularExpressions
{
    internal readonly struct RegexPrefix
    {
        public RegexPrefix(string prefix, bool ci)
        {
            Prefix = prefix;
            CaseInsensitive = ci;
        }

        public bool CaseInsensitive { get; }

        public static RegexPrefix Empty { get; } = new RegexPrefix(string.Empty, false);

        public string Prefix { get; }
    }
}
