using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace RomForge.Core.Parsers
{
    internal static class XElementExtensions
    {
        internal static XElement? ElementIgnoreCase(this XElement element, string name) =>
            element.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));

        internal static IEnumerable<XElement> ElementsIgnoreCase(this XElement element, string name) =>
            element.Elements().Where(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
