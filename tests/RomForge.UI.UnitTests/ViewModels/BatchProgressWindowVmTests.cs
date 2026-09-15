using System.Collections.Generic;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.UI.ViewModels;

namespace RomForge.UI.UnitTests.ViewModels
{
    [TestOf(typeof(BatchProgressWindowVm))]
    public class BatchProgressWindowVmTests
    {
        [Test]
        public void Slots_Count_MatchesSlotCountArgument()
        {
            BatchProgressWindowVm vm = new BatchProgressWindowVm(total: 10, slotCount: 3, isCancellable: false);

            vm.Slots.Should().HaveCount(3);
        }

        [Test]
        public void CountText_ReflectsCompletedAndTotal()
        {
            BatchProgressWindowVm vm = new BatchProgressWindowVm(total: 10, slotCount: 2, isCancellable: false);

            vm.Completed = 4;

            vm.CountText.Should().Be("4 of 10");
        }

        [Test]
        public void CountText_RaisesPropertyChanged_WhenCompletedChanges()
        {
            BatchProgressWindowVm vm = new BatchProgressWindowVm(total: 10, slotCount: 2, isCancellable: false);
            List<string?> raised = [];
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            vm.Completed = 3;

            raised.Should().Contain(nameof(BatchProgressWindowVm.CountText));
        }

        [Test]
        public void CancellationToken_IsNone_WhenNotCancellable()
        {
            BatchProgressWindowVm vm = new BatchProgressWindowVm(total: 5, slotCount: 2, isCancellable: false);

            vm.CancellationToken.Should().Be(System.Threading.CancellationToken.None);
        }

        [Test]
        public void CancellationToken_IsCancelled_AfterCancelCommand()
        {
            BatchProgressWindowVm vm = new BatchProgressWindowVm(total: 5, slotCount: 2, isCancellable: true);

            vm.CancelCommand.Execute(null);

            vm.CancellationToken.IsCancellationRequested.Should().BeTrue();
        }

        [Test]
        public void BatchSlotVm_IsActive_FalseWhenFileNameIsNull()
        {
            BatchSlotVm slot = new BatchSlotVm();

            slot.IsActive.Should().BeFalse();
        }

        [Test]
        public void BatchSlotVm_IsActive_TrueWhenFileNameIsSet()
        {
            BatchSlotVm slot = new BatchSlotVm();

            slot.FileName = "game.7z";

            slot.IsActive.Should().BeTrue();
        }

        [Test]
        public void BatchSlotVm_IsActive_RaisesPropertyChanged_WhenFileNameChanges()
        {
            BatchSlotVm slot = new BatchSlotVm();
            List<string?> raised = [];
            slot.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            slot.FileName = "game.7z";

            raised.Should().Contain(nameof(BatchSlotVm.IsActive));
        }

        [Test]
        public void BatchSlotVm_IsActive_FalseAfterFileNameClearedToNull()
        {
            BatchSlotVm slot = new BatchSlotVm { FileName = "game.7z" };

            slot.FileName = null;

            slot.IsActive.Should().BeFalse();
        }
    }
}
