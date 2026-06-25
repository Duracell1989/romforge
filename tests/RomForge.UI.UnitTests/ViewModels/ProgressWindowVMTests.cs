using System.Collections.Generic;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.UI.ViewModels;

namespace RomForge.UI.UnitTests.ViewModels;

[TestOf(typeof(ProgressWindowVM))]
public class ProgressWindowVMTests
{
    [Test]
    public void IsIndeterminate_WhenTotalIsZero_IsTrue()
    {
        ProgressWindowVM vm = new ProgressWindowVM(0, isCancellable: false);

        vm.IsIndeterminate.Should().BeTrue();
    }

    [Test]
    public void IsIndeterminate_WhenTotalIsPositive_IsFalse()
    {
        ProgressWindowVM vm = new ProgressWindowVM(10, isCancellable: false);

        vm.IsIndeterminate.Should().BeFalse();
    }

    [Test]
    public void IsIndeterminate_RaisesPropertyChanged_WhenTotalChangesFromZeroToPositive()
    {
        ProgressWindowVM vm = new ProgressWindowVM(0, isCancellable: false);
        List<string?> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Total = 5;

        raised.Should().Contain(nameof(ProgressWindowVM.IsIndeterminate));
    }
}
