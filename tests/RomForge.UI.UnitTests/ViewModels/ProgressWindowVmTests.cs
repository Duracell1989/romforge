using System.Collections.Generic;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.UI.ViewModels;

namespace RomForge.UI.UnitTests.ViewModels
{
    [TestOf(typeof(ProgressWindowVm))]
    public class ProgressWindowVmTests
    {
        [Test]
        public void IsIndeterminate_WhenTotalIsZero_IsTrue()
        {
            ProgressWindowVm vm = new ProgressWindowVm(0, isCancellable: false);

            vm.IsIndeterminate.Should().BeTrue();
        }

        [Test]
        public void IsIndeterminate_WhenTotalIsPositive_IsFalse()
        {
            ProgressWindowVm vm = new ProgressWindowVm(10, isCancellable: false);

            vm.IsIndeterminate.Should().BeFalse();
        }

        [Test]
        public void IsIndeterminate_RaisesPropertyChanged_WhenTotalChangesFromZeroToPositive()
        {
            ProgressWindowVm vm = new ProgressWindowVm(0, isCancellable: false);
            List<string?> raised = [];
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Total = 5;

            raised.Should().Contain(nameof(ProgressWindowVm.IsIndeterminate));
        }

        [Test]
        public void IsIndeterminate_TotalGoesFromPositiveToZero_BecomesTrue()
        {
            ProgressWindowVm vm = new ProgressWindowVm(10, isCancellable: false);

            vm.Total = 0;

            vm.IsIndeterminate.Should().BeTrue();
        }

        [Test]
        public void HasPhase_EmptyString_ReturnsFalse()
        {
            ProgressWindowVm vm = new ProgressWindowVm(0, isCancellable: false);

            vm.HasPhase.Should().BeFalse();
        }

        [Test]
        public void HasPhase_NonEmptyString_ReturnsTrue()
        {
            ProgressWindowVm vm = new ProgressWindowVm(0, isCancellable: false);

            vm.Phase = "Enumerating files...";

            vm.HasPhase.Should().BeTrue();
        }

        [Test]
        public void HasPhase_RaisesPropertyChanged_WhenPhaseChanges()
        {
            ProgressWindowVm vm = new ProgressWindowVm(0, isCancellable: false);
            List<string?> raised = [];
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Phase = "Computing CRCs...";

            raised.Should().Contain(nameof(ProgressWindowVm.HasPhase));
        }

        // --- Cancellation ---

        [Test]
        public void CancellationToken_WhenNotCancellable_IsNone()
        {
            ProgressWindowVm vm = new ProgressWindowVm(0, isCancellable: false);

            vm.CancellationToken.Should().Be(System.Threading.CancellationToken.None);
        }

        [Test]
        public void CancellationToken_WhenCancellable_IsNotNone()
        {
            ProgressWindowVm vm = new ProgressWindowVm(0, isCancellable: true);

            vm.CancellationToken.Should().NotBe(System.Threading.CancellationToken.None);
        }

        [Test]
        public void CancelCommand_WhenCancellable_SetsCancellationRequested()
        {
            ProgressWindowVm vm = new ProgressWindowVm(0, isCancellable: true);

            vm.CancelCommand.Execute(null);

            vm.CancellationToken.IsCancellationRequested.Should().BeTrue();
        }

        [Test]
        public void IsCancellable_WhenConstructedWithTrue_IsTrue()
        {
            ProgressWindowVm vm = new ProgressWindowVm(0, isCancellable: true);

            vm.IsCancellable.Should().BeTrue();
        }

        [Test]
        public void IsCancellable_WhenConstructedWithFalse_IsFalse()
        {
            ProgressWindowVm vm = new ProgressWindowVm(0, isCancellable: false);

            vm.IsCancellable.Should().BeFalse();
        }

        // --- CountText ---

        [Test]
        public void CountText_ReflectsCurrentAndTotal()
        {
            ProgressWindowVm vm = new ProgressWindowVm(100, isCancellable: false);
            vm.Current = 42;

            vm.CountText.Should().Be("42 of 100");
        }

        [Test]
        public void CountText_RaisesPropertyChanged_WhenCurrentChanges()
        {
            ProgressWindowVm vm = new ProgressWindowVm(10, isCancellable: false);
            List<string?> raised = [];
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Current = 5;

            raised.Should().Contain(nameof(ProgressWindowVm.CountText));
        }
    }
}
