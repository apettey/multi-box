using MultiBox.Core.Config;
using MultiBox.Core.Parsing;
using Xunit;

namespace MultiBox.Core.Tests;

/// <summary>
/// The Fast Screen Switcher, and the eve-o-preview config it has to be able to read.
///
/// The JSON below is a real exported "EVE-O Preview.json", not an invented one, which is why
/// it contains the awkward cases: hotkey arrays holding a single empty string, placeholder
/// members for unused groups, and two clients sharing position 2 in group 1.
/// </summary>
public class CycleGroupTests
{
    private const string RealEveOConfig = """
    {
      "ConfigVersion": 1,
      "CycleGroup1ForwardHotkeys": [ "F13", "Control+F13" ],
      "CycleGroup1BackwardHotkeys": [ "F20", "Control+F20" ],
      "CycleGroup1ClientsOrder": {
        "EVE - Commander Tyrael": 1,
        "EVE - Lieutent Tyrael": 2,
        "EVE - Electro MagneticForce": 2,
        "EVE - Major Tyrael": 3,
        "EVE - Sergeant Tyrael": 4
      },
      "CycleGroup2ForwardHotkeys": [ "F14", "Control+F14" ],
      "CycleGroup2BackwardHotkeys": [ "F21", "Control+F21" ],
      "CycleGroup2ClientsOrder": {
        "EVE - Commander Tyrael": 1,
        "EVE - Lieutent Tyrael": 2,
        "EVE - Electro MagneticForce": 2
      },
      "CycleGroup3ForwardHotkeys": [ "F15", "Control+F15" ],
      "CycleGroup3BackwardHotkeys": [ "" ],
      "CycleGroup3ClientsOrder": {
        "EVE - Major Tyrael": 1,
        "EVE - Sergeant Tyrael": 2
      },
      "CycleGroup4ForwardHotkeys": [ "" ],
      "CycleGroup4BackwardHotkeys": [ "" ],
      "CycleGroup4ClientsOrder": { "EVE - cycle group 4": 1 },
      "CycleGroup5ForwardHotkeys": [ "" ],
      "CycleGroup5BackwardHotkeys": [ "" ],
      "CycleGroup5ClientsOrder": { "EVE - cycle group 5": 1 },
      "PerClientAliases": {
        "EVE - Commander Tyrael": "Main",
        "EVE - Sergeant Tyrael": "Alt"
      },
      "FlatLayout": {
        "EVE - Lieutent Tyrael": "3700, 150",
        "EVE - Commander Tyrael": "2787, 150"
      },
      "ClientLayout": {
        "EVE - Major Tyrael": { "X": 0, "Y": 0, "Width": 2560, "Height": 1440, "IsMaximized": true }
      }
    }
    """;

    private static MultiBoxConfig ImportRealConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, RealEveOConfig);
        try
        {
            var config = new MultiBoxConfig();
            Assert.True(EveOPreviewImport.TryImport(path, config));
            return config;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportReadsHotkeysForEachGroup()
    {
        var config = ImportRealConfig();

        Assert.Equal(new[] { "F13", "Control+F13" }, config.CycleGroups[0].ForwardHotkeys);
        Assert.Equal(new[] { "F20", "Control+F20" }, config.CycleGroups[0].BackwardHotkeys);
        Assert.Equal(new[] { "F15", "Control+F15" }, config.CycleGroups[2].ForwardHotkeys);
    }

    [Fact]
    public void AnEmptyHotkeyStringIsNotAHotkey()
    {
        var config = ImportRealConfig();

        // eve-o writes [""] for an unset hotkey. Registering that would be a silent no-op
        // at best and a stolen key at worst.
        Assert.Empty(config.CycleGroups[2].BackwardHotkeys);
        Assert.Empty(config.CycleGroups[3].ForwardHotkeys);
    }

    [Fact]
    public void MembersAreOrderedByPositionAndKeyedByCharacterNotWindowTitle()
    {
        var config = ImportRealConfig();

        Assert.Equal(
            new[] { "Commander Tyrael", "Lieutent Tyrael", "Electro MagneticForce", "Major Tyrael", "Sergeant Tyrael" },
            config.CycleGroups[0].Members);
    }

    [Fact]
    public void TiedPositionsKeepTheirFileOrder()
    {
        var config = ImportRealConfig();

        // Group 1 gives both "Lieutent Tyrael" and "Electro MagneticForce" position 2.
        // eve-o allows that, so the sort has to be stable rather than throwing them around.
        var members = config.CycleGroups[1].Members;
        Assert.Equal(new[] { "Commander Tyrael", "Lieutent Tyrael", "Electro MagneticForce" }, members);
    }

    [Fact]
    public void AliasesAreImported()
    {
        var config = ImportRealConfig();

        Assert.Equal("Main", config.Aliases["Commander Tyrael"]);
        Assert.Equal("Alt", config.Aliases["Sergeant Tyrael"]);
    }

    [Fact]
    public void ImportStillReadsLayoutAlongsideCycleGroups()
    {
        var config = ImportRealConfig();

        Assert.Equal(new Pt(2787, 150), config.FlatLayout["EVE - Commander Tyrael"]);
        Assert.True(config.ClientLayout["EVE - Major Tyrael"].IsMaximized);
    }

    // --- cycling ----------------------------------------------------------------------------

    private static CycleGroup Group(params string[] members) => new() { Members = members.ToList() };

    [Fact]
    public void ForwardWalksTheGroupInOrder()
    {
        var group = Group("A", "B", "C");

        Assert.Equal("B", group.Step("A", _ => true, forward: true));
        Assert.Equal("C", group.Step("B", _ => true, forward: true));
    }

    [Fact]
    public void ForwardWrapsPastTheEnd()
    {
        var group = Group("A", "B", "C");
        Assert.Equal("A", group.Step("C", _ => true, forward: true));
    }

    [Fact]
    public void BackwardWrapsPastTheStart()
    {
        var group = Group("A", "B", "C");
        Assert.Equal("C", group.Step("A", _ => true, forward: false));
    }

    [Fact]
    public void FirstPressLandsOnTheFirstMember()
    {
        var group = Group("A", "B", "C");

        // Nothing raised yet. Landing on "B" here would make the first press of a hotkey
        // skip a client for no reason the player could see.
        Assert.Equal("A", group.Step(null, _ => true, forward: true));
    }

    [Fact]
    public void ClientsThatAreNotRunningAreSkipped()
    {
        var group = Group("A", "B", "C");
        Assert.Equal("C", group.Step("A", name => name != "B", forward: true));
    }

    [Fact]
    public void AGroupWithNothingRunningYieldsNothing()
    {
        var group = Group("A", "B");
        Assert.Null(group.Step("A", _ => false, forward: true));
    }

    [Fact]
    public void AnEmptyGroupYieldsNothing()
    {
        Assert.Null(new CycleGroup().Step(null, _ => true, forward: true));
    }

    [Fact]
    public void BackwardFromAFreshGroupLandsOnTheLastMember()
    {
        var group = Group("A", "B", "C");

        // Not "B". Entering a ring backwards from nowhere means starting at its end.
        Assert.Equal("C", group.Step(null, _ => true, forward: false));
    }

    // --- navigator: where a press resumes from -----------------------------------------------

    private static List<CycleGroup> TwoGroups() => new()
    {
        Group("A", "B", "C"),
        Group("X", "Y")
    };

    [Fact]
    public void RepeatedPressesOfOneGroupWalkThroughIt()
    {
        var groups = TwoGroups();
        var nav = new CycleNavigator();

        Assert.Equal("A", nav.Next(groups, 0, _ => true, forward: true));
        Assert.Equal("B", nav.Next(groups, 0, _ => true, forward: true));
        Assert.Equal("C", nav.Next(groups, 0, _ => true, forward: true));
    }

    [Fact]
    public void ReturningToAGroupRestartsItFromTheBeginning()
    {
        var groups = TwoGroups();
        var nav = new CycleNavigator();

        nav.Next(groups, 0, _ => true, forward: true);   // group 1 -> A
        nav.Next(groups, 0, _ => true, forward: true);   // group 1 -> B
        nav.Next(groups, 1, _ => true, forward: true);   // group 2 -> X

        // Back to group 1. Resuming at C would mean the same keypress lands somewhere
        // different depending on history the player can no longer see.
        Assert.Equal("A", nav.Next(groups, 0, _ => true, forward: true));
    }

    [Fact]
    public void SwitchingGroupsAlsoRestartsTheGroupBeingSwitchedTo()
    {
        var groups = TwoGroups();
        var nav = new CycleNavigator();

        nav.Next(groups, 1, _ => true, forward: true);   // group 2 -> X
        nav.Next(groups, 1, _ => true, forward: true);   // group 2 -> Y
        nav.Next(groups, 0, _ => true, forward: true);   // group 1 -> A

        Assert.Equal("X", nav.Next(groups, 1, _ => true, forward: true));
    }

    [Fact]
    public void AFailedStepDoesNotMoveThePosition()
    {
        var groups = TwoGroups();
        var nav = new CycleNavigator();

        Assert.Equal("A", nav.Next(groups, 0, _ => true, forward: true));
        Assert.Null(nav.Next(groups, 0, _ => false, forward: true));

        // Nothing was running, so the next successful press continues from A, not past it.
        Assert.Equal("B", nav.Next(groups, 0, _ => true, forward: true));
    }

    [Fact]
    public void ResetSendsTheNextPressBackToTheStart()
    {
        var groups = TwoGroups();
        var nav = new CycleNavigator();

        nav.Next(groups, 0, _ => true, forward: true);
        nav.Next(groups, 0, _ => true, forward: true);
        nav.Reset();

        Assert.Equal("A", nav.Next(groups, 0, _ => true, forward: true));
    }

    [Fact]
    public void AnUnknownGroupIndexIsIgnored()
    {
        var groups = TwoGroups();
        var nav = new CycleNavigator();

        Assert.Null(nav.Next(groups, 7, _ => true, forward: true));
        Assert.Null(nav.Next(groups, -1, _ => true, forward: true));
    }

    [Fact]
    public void CharacterFromKeyStripsTheWindowTitlePrefix()
    {
        Assert.Equal("Commander Tyrael", MultiBoxConfig.CharacterFromKey("EVE - Commander Tyrael"));
        Assert.Equal("Commander Tyrael", MultiBoxConfig.CharacterFromKey("Commander Tyrael"));
    }

    // --- capacitor transfer -------------------------------------------------------------------

    [Theory]
    [InlineData("240 GJ energy transfered to Basilisk // Ashe Corvin VI.TA / - Large Remote Capacitor Transmitter")]
    [InlineData("240 GJ energy transferred to Basilisk // Ashe Corvin VI.TA / - Large Remote Capacitor Transmitter")]
    public void CapacitorTransferOutIsParsed(string body)
    {
        var parser = new GamelogParser("Juno Vael");
        var line = $"[ 2026.08.28 18:42:07 ] (combat) {body}";

        var parsed = parser.ParseLine(line);

        Assert.NotNull(parsed);
        Assert.Equal(Model.CombatEventKind.CapacitorTransfer, parsed!.Kind);
        Assert.Equal(Model.Direction.Outgoing, parsed.Direction);
        Assert.Equal(240, parsed.Amount);
        Assert.Equal("Ashe Corvin", parsed.Counterparty?.Name);
    }

    [Fact]
    public void CapacitorTransferInIsCreditedToTheListener()
    {
        var parser = new GamelogParser("Juno Vael");
        var line = "[ 2026.08.28 18:42:07 ] (combat) 310 GJ energy transfered by " +
                   "Nestor // Ashe Corvin VI.TA / - Large Remote Capacitor Transmitter";

        var parsed = parser.ParseLine(line);

        Assert.NotNull(parsed);
        Assert.Equal(Model.Direction.Incoming, parsed!.Direction);
        Assert.Equal("Juno Vael", parsed.Victim);
        Assert.Equal(310, parsed.Amount);
    }

    [Fact]
    public void NeutralisationIsNotMistakenForATransfer()
    {
        var parser = new GamelogParser("Sable Ryn");
        var line = "[ 2026.08.28 18:42:02 ] (combat) 84 GJ energy neutralized Curse - Curse";

        var parsed = parser.ParseLine(line);

        Assert.NotNull(parsed);
        Assert.Equal(Model.CombatEventKind.EnergyNeutralized, parsed!.Kind);
    }
}
