using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RomForge.UI.ViewModels;

namespace RomForge.UI
{
    /// <summary>
    /// Given a view model, returns the corresponding view if possible.
    /// </summary>
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away.",
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator"
    )]
    public class ViewLocator : IDataTemplate
    {
        /// <summary>
        /// Maps a view-model type to the full name of its view type. Split out from
        /// <see cref="Build"/> so it can be tested without an Avalonia host — <see cref="Build"/>
        /// instantiates the resolved <see cref="Control"/>, this does not.
        /// </summary>
        internal static string ResolveViewTypeName(Type viewModelType)
        {
            string fullName = viewModelType.FullName!.Replace("ViewModels.", "Views.", StringComparison.Ordinal);
            return fullName.EndsWith("Vm", StringComparison.Ordinal) ? fullName[..^2] : fullName;
        }

        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            string name = ResolveViewTypeName(param.GetType());
            var type = Type.GetType(name);

            if (type != null)
            {
                return (Control)Activator.CreateInstance(type)!;
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        public bool Match(object? data)
        {
            return data is VmBase;
        }
    }
}
