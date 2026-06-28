using System.Collections.Generic;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.UI.ViewModels;

namespace RomForge.UI.UnitTests.ViewModels;

[TestOf(typeof(BatchProgressWindowVM))]
public class BatchProgressWindowVMTests
{
    [Test]
    public void Slots_Count_MatchesSlotCountArgument()
    {
        BatchProgressWindowVM vm = new BatchProgressWindowVM(total: 10, slotCount: 3, isCancellable: false);

        vm.Slots.Should().HaveCount(3);
    }

    [Test]
    public void CountText_ReflectsCompletedAndTotal()
    {
        BatchProgressWindowVM vm = new BatchProgressWindowVM(total: 10, slotCount: 2, isCancellable: false);

        vm.Completed = 4;

        vm.CountText.Should().Be("4 of 10");
    }

    [Test]
    public void CountText_RaisesPropertyChanged_WhenCompletedChanges()
    {
        BatchProgressWindowVM vm = new BatchProgressWindowVM(total: 10, slotCount: 2, isCancellable: false);
        List<string?> raised = [];
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Completed = 3;

        raised.Should().Contain(nameof(BatchProgressWindowVM.CountText));
    }

    [Test]
    public void CancellationToken_IsNone_WhenNotCancellable()
    {
        BatchProgressWindowVM vm = new BatchProgressWindowVM(total: 5, slotCount: 2, isCancellable: false);

        vm.CancellationToken.Should().Be(System.Threading.CancellationToken.None);
    }

    [Test]
    public void CancellationToken_IsCancelled_AfterCancelCommand()
    {
        BatchProgressWindowVM vm = new BatchProgressWindowVM(total: 5, slotCount: 2, isCancellable: true);

        vm.CancelCommand.Execute(null);

        vm.CancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void BatchSlotVM_IsActive_FalseWhenFileNameIsNull()
    {
        BatchSlotVM slot = new BatchSlotVM();

        slot.IsActive.Should().BeFalse();
    }

    [Test]
    public void BatchSlotVM_IsActive_TrueWhenFileNameIsSet()
    {
        BatchSlotVM slot = new BatchSlotVM();

        slot.FileName = "game.7z";

        slot.IsActive.Should().BeTrue();
    }

    [Test]
    public void BatchSlotVM_IsActive_RaisesPropertyChanged_WhenFileNameChanges()
    {
        BatchSlotVM slot = new BatchSlotVM();
        List<string?> raised = [];
        slot.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        slot.FileName = "game.7z";

        raised.Should().Contain(nameof(BatchSlotVM.IsActive));
    }

    [Test]
    public void BatchSlotVM_IsActive_FalseAfterFileNameClearedToNull()
    {
        BatchSlotVM slot = new BatchSlotVM { FileName = "game.7z" };

        slot.FileName = null;

        slot.IsActive.Should().BeFalse();
    }
}
