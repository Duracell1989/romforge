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

    [Test]
    public void IsIndeterminate_TotalGoesFromPositiveToZero_BecomesTrue()
    {
        ProgressWindowVM vm = new ProgressWindowVM(10, isCancellable: false);

        vm.Total = 0;

        vm.IsIndeterminate.Should().BeTrue();
    }

    [Test]
    public void HasPhase_EmptyString_ReturnsFalse()
    {
        ProgressWindowVM vm = new ProgressWindowVM(0, isCancellable: false);

        vm.HasPhase.Should().BeFalse();
    }

    [Test]
    public void HasPhase_NonEmptyString_ReturnsTrue()
    {
        ProgressWindowVM vm = new ProgressWindowVM(0, isCancellable: false);

        vm.Phase = "Enumerating files...";

        vm.HasPhase.Should().BeTrue();
    }

    [Test]
    public void HasPhase_RaisesPropertyChanged_WhenPhaseChanges()
    {
        ProgressWindowVM vm = new ProgressWindowVM(0, isCancellable: false);
        List<string?> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Phase = "Computing CRCs...";

        raised.Should().Contain(nameof(ProgressWindowVM.HasPhase));
    }
}
