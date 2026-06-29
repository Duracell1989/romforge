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

    // --- Cancellation ---

    [Test]
    public void CancellationToken_WhenNotCancellable_IsNone()
    {
        ProgressWindowVM vm = new ProgressWindowVM(0, isCancellable: false);

        vm.CancellationToken.Should().Be(System.Threading.CancellationToken.None);
    }

    [Test]
    public void CancellationToken_WhenCancellable_IsNotNone()
    {
        ProgressWindowVM vm = new ProgressWindowVM(0, isCancellable: true);

        vm.CancellationToken.Should().NotBe(System.Threading.CancellationToken.None);
    }

    [Test]
    public void CancelCommand_WhenCancellable_SetsCancellationRequested()
    {
        ProgressWindowVM vm = new ProgressWindowVM(0, isCancellable: true);

        vm.CancelCommand.Execute(null);

        vm.CancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void IsCancellable_WhenConstructedWithTrue_IsTrue()
    {
        ProgressWindowVM vm = new ProgressWindowVM(0, isCancellable: true);

        vm.IsCancellable.Should().BeTrue();
    }

    [Test]
    public void IsCancellable_WhenConstructedWithFalse_IsFalse()
    {
        ProgressWindowVM vm = new ProgressWindowVM(0, isCancellable: false);

        vm.IsCancellable.Should().BeFalse();
    }

    // --- CountText ---

    [Test]
    public void CountText_ReflectsCurrentAndTotal()
    {
        ProgressWindowVM vm = new ProgressWindowVM(100, isCancellable: false);
        vm.Current = 42;

        vm.CountText.Should().Be("42 of 100");
    }

    [Test]
    public void CountText_RaisesPropertyChanged_WhenCurrentChanges()
    {
        ProgressWindowVM vm = new ProgressWindowVM(10, isCancellable: false);
        List<string?> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Current = 5;

        raised.Should().Contain(nameof(ProgressWindowVM.CountText));
    }
}
