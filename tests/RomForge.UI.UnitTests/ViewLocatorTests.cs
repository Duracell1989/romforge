using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.UI.ViewModels;

namespace RomForge.UI.UnitTests
{
    /// <summary>
    /// Locks the view-model to view naming convention ViewLocator depends on. It resolves a view by
    /// string surgery — swap "ViewModels." for "Views." and drop a trailing "Vm" — so a rename on
    /// either side compiles cleanly and only fails at runtime, as a "Not Found" TextBlock.
    /// </summary>
    [TestOf(typeof(ViewLocator))]
    public sealed class ViewLocatorTests
    {
        private static IEnumerable<Type> WindowViewModels() =>
            typeof(VmBase)
                .Assembly.GetTypes()
                .Where(t => !t.IsAbstract && t.Namespace == "RomForge.UI.ViewModels" && t.Name.EndsWith("WindowVm", StringComparison.Ordinal));

        [Test]
        public void WindowViewModels_AreDiscovered()
        {
            // Guards the test itself: a convention change that empties the set would otherwise
            // make every assertion below vacuously pass.
            WindowViewModels().Should().NotBeEmpty();
        }

        [Test]
        public void EveryWindowViewModel_ResolvesToAnExistingViewType()
        {
            foreach (Type viewModelType in WindowViewModels())
            {
                string viewTypeName = ViewLocator.ResolveViewTypeName(viewModelType);

                // Resolved through the UI assembly explicitly. ViewLocator itself can use the
                // bare Type.GetType because it lives in that same assembly; this test does not.
                typeof(VmBase).Assembly.GetType(viewTypeName).Should().NotBeNull($"{viewModelType.Name} should resolve to view type {viewTypeName}");
            }
        }

        [Test]
        public void ResolveViewTypeName_DropsTheVmSuffix()
        {
            ViewLocator.ResolveViewTypeName(typeof(MainWindowVm)).Should().Be("RomForge.UI.Views.MainWindow");
        }
    }
}
