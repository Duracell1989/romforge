using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Matching;
using RomForge.Core.Models;
using RomForge.UI.ViewModels;

namespace RomForge.UI.UnitTests.ViewModels;

[TestOf(typeof(LoadedDatVM))]
public class LoadedDatVMTests
{
    private static DatFile MakeDat() =>
        new DatFile { Header = new DatHeader { DatName = "Test", System = "Test" }, Games = [] };

    private static LoadedDatVM MakeVm(params Game[] games)
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");
        foreach (Game g in games)
            vm.Games.Add(
                new GameRowVM(
                    new MatchResult { Game = g, Status = MatchStatus.Missing },
                    "/imgs",
                    new DatHeader(),
                    []
                )
            );
        return vm;
    }

    private static Game MakeGame(int release, string title, string? publisher = null) =>
        new Game { ReleaseNumber = release, Title = title, Publisher = publisher };

    private static GameRowVM MakeRow(MatchResult result) =>
        new GameRowVM(result, string.Empty, new DatHeader(), []);

    private static LoadedDatVM MakeVmWithStatuses()
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");

        // A = Verified (no flags) → StatusSortKey 4
        vm.Games.Add(new GameRowVM(
            new MatchResult { Game = new Game { ReleaseNumber = 1, Title = "A" }, Status = MatchStatus.Verified },
            "/imgs", new DatHeader(), []
        ));
        // B = Missing → StatusSortKey 0
        vm.Games.Add(new GameRowVM(
            new MatchResult { Game = new Game { ReleaseNumber = 2, Title = "B" }, Status = MatchStatus.Missing },
            "/imgs", new DatHeader(), []
        ));
        // C = Verified + IncorrectlyNamed → StatusSortKey 3
        vm.Games.Add(new GameRowVM(
            new MatchResult
            {
                Game = new Game { ReleaseNumber = 3, Title = "C" },
                Status = MatchStatus.Verified,
                IsIncorrectlyNamed = true,
            },
            "/imgs", new DatHeader(), []
        ));
        return vm;
    }

    [Test]
    public void FilteredGames_DefaultOrder_MatchesInsertionOrder()
    {
        LoadedDatVM vm = MakeVm(
            MakeGame(3, "Zelda"),
            MakeGame(1, "Mario"),
            MakeGame(2, "Metroid")
        );

        vm.FilteredGames.Select(g => g.ReleaseNumber).Should().ContainInOrder(3, 1, 2);
    }

    [Test]
    public void SortBy_ReleaseNumber_SortsAscending()
    {
        LoadedDatVM vm = MakeVm(
            MakeGame(3, "Zelda"),
            MakeGame(1, "Mario"),
            MakeGame(2, "Metroid")
        );

        vm.SortByCommand.Execute("ReleaseNumber");

        vm.FilteredGames.Select(g => g.ReleaseNumber).Should().ContainInOrder(1, 2, 3);
    }

    [Test]
    public void SortBy_ReleaseNumberTwice_SortsDescending()
    {
        LoadedDatVM vm = MakeVm(
            MakeGame(3, "Zelda"),
            MakeGame(1, "Mario"),
            MakeGame(2, "Metroid")
        );

        vm.SortByCommand.Execute("ReleaseNumber");
        vm.SortByCommand.Execute("ReleaseNumber");

        vm.FilteredGames.Select(g => g.ReleaseNumber).Should().ContainInOrder(3, 2, 1);
    }

    [Test]
    public void SortBy_Title_SortsAlphabeticallyAscending()
    {
        LoadedDatVM vm = MakeVm(
            MakeGame(1, "Zelda"),
            MakeGame(2, "Mario"),
            MakeGame(3, "Metroid")
        );

        vm.SortByCommand.Execute("Title");

        vm.FilteredGames.Select(g => g.Title).Should().ContainInOrder("Mario", "Metroid", "Zelda");
    }

    [Test]
    public void SortBy_TitleDescending_SortsReverseAlphabetically()
    {
        LoadedDatVM vm = MakeVm(
            MakeGame(1, "Zelda"),
            MakeGame(2, "Mario"),
            MakeGame(3, "Metroid")
        );

        vm.SortByCommand.Execute("Title");
        vm.SortByCommand.Execute("Title");

        vm.FilteredGames.Select(g => g.Title).Should().ContainInOrder("Zelda", "Metroid", "Mario");
    }

    [Test]
    public void SortBy_Publisher_SortsAscending()
    {
        LoadedDatVM vm = MakeVm(
            MakeGame(1, "A", publisher: "Nintendo"),
            MakeGame(2, "B", publisher: "Capcom"),
            MakeGame(3, "C", publisher: "Acclaim")
        );

        vm.SortByCommand.Execute("Publisher");

        vm.FilteredGames.Select(g => g.Publisher).Should().ContainInOrder("Acclaim", "Capcom", "Nintendo");
    }

    [Test]
    public void SortBy_Status_SortsAscendingByPriority()
    {
        // Insertion order: A=Verified(4), B=Missing(0), C=IncorrectlyNamed(3)
        // Ascending by StatusSortKey: B(0), C(3), A(4)
        LoadedDatVM vm = MakeVmWithStatuses();

        vm.SortByCommand.Execute("Status");

        vm.FilteredGames.Select(g => g.Title).Should().ContainInOrder("B", "C", "A");
    }

    [Test]
    public void SortBy_DifferentColumn_ResetsToAscending()
    {
        LoadedDatVM vm = MakeVm(
            MakeGame(3, "Zelda"),
            MakeGame(1, "Mario"),
            MakeGame(2, "Metroid")
        );

        vm.SortByCommand.Execute("ReleaseNumber");
        vm.SortByCommand.Execute("ReleaseNumber");
        vm.SortByCommand.Execute("Title");

        vm.FilteredGames.Select(g => g.Title).Should().ContainInOrder("Mario", "Metroid", "Zelda");
    }

    [Test]
    public void SortBy_ReleaseNumber_UpdatesSortIndicator()
    {
        LoadedDatVM vm = MakeVm(MakeGame(1, "A"));

        vm.ReleaseNumberSortIndicator.Should().Be(string.Empty);

        vm.SortByCommand.Execute("ReleaseNumber");
        vm.ReleaseNumberSortIndicator.Should().Be(" ▲");
        vm.TitleSortIndicator.Should().Be(string.Empty);

        vm.SortByCommand.Execute("ReleaseNumber");
        vm.ReleaseNumberSortIndicator.Should().Be(" ▼");
    }

    // --- StatusSummary ---

    [Test]
    public void StatusSummary_NoGames_ReturnsNoScanYet()
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");

        vm.StatusSummary.Should().Be("No scan yet");
    }

    [Test]
    public void StatusSummary_AllMissing_ContainsMissingCount()
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game(), Status = MatchStatus.Missing }));
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game(), Status = MatchStatus.Missing }));

        vm.StatusSummary.Should().Contain("2 missing");
        vm.StatusSummary.Should().Contain("2 games");
    }

    [Test]
    public void StatusSummary_GoodGame_ContainsGoodCount()
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game(), Status = MatchStatus.Verified, IsReArchived = true }));

        vm.StatusSummary.Should().Contain("1 good");
    }

    [Test]
    public void StatusSummary_PreparedGameNoReArchived_CountsAsGood()
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game(), Status = MatchStatus.Verified }));

        vm.StatusSummary.Should().Contain("1 good");
    }

    [Test]
    public void StatusSummary_MixedFlags_CountsMutuallyExclusive()
    {
        // A game that is both Untrimmed AND WrongArchiveType should count only as untrimmed.
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");
        vm.Games.Add(MakeRow(new MatchResult
        {
            Game = new Game(),
            Status = MatchStatus.Verified,
            IsUntrimmed = true,
            IsWrongArchiveType = true,
        }));

        vm.StatusSummary.Should().Contain("1 untrimmed");
        vm.StatusSummary.Should().NotContain("wrong archive");
    }

    [Test]
    public void StatusSummary_FilterActive_ShowsFilteredOf()
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game(), Status = MatchStatus.Missing }));
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game(), Status = MatchStatus.Verified }));
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game(), Status = MatchStatus.Verified }));

        vm.ShowMissing = false;

        vm.StatusSummary.Should().StartWith("Showing 2 of 3");
    }

    // --- MatchesFilter ---

    [Test]
    public void ShowMissing_False_HidesMissingRows()
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game { ReleaseNumber = 1 }, Status = MatchStatus.Missing }));
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game { ReleaseNumber = 2 }, Status = MatchStatus.Verified }));

        vm.ShowMissing = false;

        vm.FilteredGames.Should().HaveCount(1);
        vm.FilteredGames[0].ReleaseNumber.Should().Be(2);
    }

    [Test]
    public void ShowVerified_False_HidesPureVerifiedRows()
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game { ReleaseNumber = 1 }, Status = MatchStatus.Verified }));
        vm.Games.Add(MakeRow(new MatchResult { Game = new Game { ReleaseNumber = 2 }, Status = MatchStatus.Missing }));

        vm.ShowVerified = false;

        vm.FilteredGames.Should().HaveCount(1);
        vm.FilteredGames[0].ReleaseNumber.Should().Be(2);
    }

    [Test]
    public void TitleFilter_CaseInsensitive_FiltersRows()
    {
        LoadedDatVM vm = MakeVm(
            MakeGame(1, "Mario Kart"),
            MakeGame(2, "Zelda"),
            MakeGame(3, "mario world")
        );

        vm.TitleFilter = "mario";

        vm.FilteredGames.Should().HaveCount(2);
        vm.FilteredGames.Select(g => g.ReleaseNumber).Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [Test]
    public void ShowUntrimmed_False_HidesUntrimmedRows()
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");
        vm.Games.Add(MakeRow(new MatchResult
        {
            Game = new Game { ReleaseNumber = 1 },
            Status = MatchStatus.Verified,
            IsUntrimmed = true,
        }));
        vm.Games.Add(MakeRow(new MatchResult
        {
            Game = new Game { ReleaseNumber = 2 },
            Status = MatchStatus.Verified,
        }));

        vm.ShowUntrimmed = false;

        vm.FilteredGames.Should().HaveCount(1);
        vm.FilteredGames[0].ReleaseNumber.Should().Be(2);
    }

    [Test]
    public void SortBy_PreservesActiveFilter()
    {
        LoadedDatVM vm = new LoadedDatVM(MakeDat(), "/test/dat.xml");
        vm.Games.Add(new GameRowVM(
            new MatchResult { Game = new Game { ReleaseNumber = 3, Title = "Zelda" }, Status = MatchStatus.Verified },
            "/imgs", new DatHeader(), []
        ));
        vm.Games.Add(new GameRowVM(
            new MatchResult { Game = new Game { ReleaseNumber = 1, Title = "Mario" }, Status = MatchStatus.Missing },
            "/imgs", new DatHeader(), []
        ));
        vm.Games.Add(new GameRowVM(
            new MatchResult { Game = new Game { ReleaseNumber = 2, Title = "Metroid" }, Status = MatchStatus.Verified },
            "/imgs", new DatHeader(), []
        ));
        vm.ShowMissing = false;

        vm.SortByCommand.Execute("ReleaseNumber");

        vm.FilteredGames.Should().HaveCount(2);
        vm.FilteredGames.Select(g => g.ReleaseNumber).Should().ContainInOrder(2, 3);
    }
}
