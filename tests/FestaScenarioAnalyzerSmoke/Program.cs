using System.Globalization;
using System.Drawing;
using System.Text.Json;
using FestaScenarioAnalyzer;
using Gallop;
using Gallop.Endpoints;
using Terminal.Gui.Input;
using Terminal.Gui.Drawing;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using FestaPlugin = FestaScenarioAnalyzer.FestaScenarioAnalyzer;

RepositoryChecks.Run();
Environment.SetEnvironmentVariable("DisableRealDriverIO", "1");
Environment.SetEnvironmentVariable("URA_TEST_UI_CULTURE", "zh-CN");
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
var artifactDirectory = Path.GetFullPath(Path.Combine("artifacts", "grouped-ui-20261030"));
Directory.CreateDirectory(artifactDirectory);
var captureMode = args is ["--captures", _, "--database", _];
if (args.Length != 0 && !captureMode && args is not ["--concurrency-only"])
    throw new ArgumentException("Usage: [--concurrency-only] | --captures <packet-directory> --database <Host-data-directory>");
var capturePath = captureMode ? Path.GetFullPath(args[1]) : null;
var databasePath = captureMode ? Path.GetFullPath(args[3]) : null;
using var workingDirectory = new TempCurrentDirectory("FestaScenarioAnalyzer");
UmamusumeResponseAnalyzer.Config.Initialize();
if (captureMode)
{
    try
    {
        Directory.SetCurrentDirectory(databasePath!);
        if (await UmamusumeResponseAnalyzer.Database.Initialize() != UmamusumeResponseAnalyzer.DatabaseAvailability.Ready)
            throw new InvalidOperationException("Capture replay requires a complete Host database: " + databasePath);
    }
    finally { Directory.SetCurrentDirectory(workingDirectory.Path); }
    using var captureUi = new WorkspaceSmokeSession(120, 35);
    await CaptureReplay.RunAsync(captureUi, capturePath!);
    return;
}
using var database = new SmokeDatabase();
var concurrencyOnly = args.Contains("--concurrency-only", StringComparer.Ordinal);
if (!concurrencyOnly)
{
    Run("training formula and server state", TestProjection);
    Run("command variants and invalid duplicates", TestCommands);
    Run("immutable snapshots", TestSnapshot);
    Run("complete master data and five-type threshold", TestMasterData);
}
using var ui = new WorkspaceSmokeSession(120, 35);
if (!concurrencyOnly)
{
    Run("grouped framebuffer, narrow screen and resize", () => TestRenderer(ui, artifactDirectory));
    await RunAsync("registered dispatch, history, configuration and disposal", () => TestPlugin(ui));
}
await RunAsync("history navigation concurrent with response", () => TestConcurrentNavigation(ui, artifactDirectory));
Console.WriteLine($"FestaScenarioAnalyzer {(concurrencyOnly ? "concurrency " : "")}smoke passed (synthetic DTO fixtures; no live game verification).");

static void TestProjection()
{
    var fixture = new Fixture();
    var turn = FestaProjection.Create(fixture.Chara, fixture.Home, fixture.Data, true);
    Equal(5, turn.Training.Length, "five available trainings");
    Sequence([32, 42, 42, 21, 21], turn.Training[0].Gains, "crossing 1200 uses revised gains");
    Equal(20, turn.Training[0].PtGain, "home and scenario skill points");
    Equal(39, turn.Training[0].VitalAfter, "scenario vitality reduces the home training cost");
    Equal(80, turn.Training[4].VitalAfter, "home and scenario wisdom healing");
    Equal(101, turn.Acts[0].NeedGauge, "server gauge threshold");
    Equal(1, turn.Acts[0].OutingLevel, "outing level preview");
    Equal(20, turn.OtherCommands.Single().Gauges.Single().Gain, "non-training gauge preview");
    Equal(2, turn.NotLevelUpActs.Single(), "server level limitation");
    Equal(6, turn.SupportTypeCount, "support types include the friend category");
    Check(turn.Training.Take(2).All(training => training.Partners.Any(partner => partner.TargetId == 1)),
        "cooperation partner is preserved in each training");
    Check(turn.Training[0].Partners.Single(partner => partner.TargetId == 1).IsHint, "hint partner");
    Check(turn.Training[0].Partners.Single(partner => partner.TargetId == 6).IsFriend, "friend classification");
    var npcFixture = new Fixture();
    npcFixture.Home.command_info_array[0].training_partner_array = [102, 1];
    var npc = FestaProjection.Create(npcFixture.Chara, npcFixture.Home, npcFixture.Data, true).Training[0].Partners.Last();
    Equal(102, npc.CharaId, "ordinary NPC uses target ID as character ID");
    Equal("理事长", npc.Name, "ordinary NPC name lookup");
    var groupFixture = new Fixture();
    groupFixture.Chara.support_card_array[0].support_card_id = 30081;
    groupFixture.Chara.evaluation_info_array[0].evaluation = 20;
    groupFixture.Chara.chara_effect_id_array = [100];
    Check(FestaProjection.Create(groupFixture.Chara, groupFixture.Home, groupFixture.Data, true)
        .Training[0].Partners.Single(partner => partner.TargetId == 1).Shining, "group card 30081 shines while effect 100 is active");
    fixture.Chara.vital = 3;
    Equal(0, FestaProjection.Create(fixture.Chara, fixture.Home, fixture.Data, true).Training[0].VitalAfter, "vital lower cap");
    fixture.Chara.vital = 99;
    Equal(100, FestaProjection.Create(fixture.Chara, fixture.Home, fixture.Data, true).Training[4].VitalAfter, "vital upper cap");
    Equal(0, FestaProjection.Create(fixture.Chara, fixture.Home, fixture.Data, false).Training.Length, "event state cannot train");
    Equal(0, FestaProjection.Create(fixture.Chara, null, fixture.Data, true).Training.Length, "missing home has no predicted training");
}

static void TestCommands()
{
    foreach (var ids in new[] { Fixture.BaseTrainIds, new[] { 601, 602, 603, 604, 605 }, new[] { 5101, 5102, 5103, 5104, 5105 } })
    {
        var fixture = new Fixture();
        for (var i = 0; i < ids.Length; i++)
        {
            fixture.Home.command_info_array[i].command_id = ids[i];
            fixture.Data.command_info_array[i].command_id = ids[i];
            fixture.Chara.training_level_info_array[i].command_id = ids[i];
        }
        var turn = FestaProjection.Create(fixture.Chara, fixture.Home, fixture.Data, true);
        Sequence([0, 1, 2, 3, 4], turn.Training.Select(training => training.Index), "speed/stamina/power/guts/wisdom order");
        Sequence(ids, turn.Training.Select(training => training.CommandId), "actual command IDs retained");
        Check(turn.Training.All(training => training.PtGain == 20), "scenario params matched for command family");
    }

    var sparse = new Fixture();
    sparse.Home.command_info_array = [sparse.Home.command_info_array[0], new() { command_id = 9999, command_type = 3 }];
    Equal(0, FestaProjection.Create(sparse.Chara, sparse.Home, sparse.Data, true).Training.Length, "incomplete command set has no training prediction");
    var disabled = new Fixture();
    disabled.Home.command_info_array[0].is_enable = 0;
    Check(!FestaProjection.Create(disabled.Chara, disabled.Home, disabled.Data, true).Training[0].IsEnabled, "disabled command retained as disabled");

    var duplicateHome = new Fixture();
    duplicateHome.Home.command_info_array = [.. duplicateHome.Home.command_info_array, duplicateHome.Home.command_info_array[0]];
    Throws<InvalidDataException>(() => FestaProjection.Create(duplicateHome.Chara, duplicateHome.Home, duplicateHome.Data, true), "duplicate home training");
    var duplicateScenario = new Fixture();
    duplicateScenario.Data.command_info_array = [.. duplicateScenario.Data.command_info_array, duplicateScenario.Data.command_info_array[0]];
    Throws<InvalidDataException>(() => FestaProjection.Create(duplicateScenario.Chara, duplicateScenario.Home, duplicateScenario.Data, true), "duplicate scenario training");
    var paired = new Fixture();
    paired.Home.command_info_array = [.. paired.Home.command_info_array, new()
    {
        command_type = 1, command_id = 5101, is_enable = 1, failure_rate = 40,
        params_inc_dec_info_array = [Fixture.Param(1, 20), Fixture.Param(30, 30), Fixture.Param(10, -18)],
        training_partner_array = [2], tips_event_partner_array = []
    }];
    paired.Data.command_info_array = [.. paired.Data.command_info_array, new()
    {
        command_type = 1, command_id = 5101,
        params_inc_dec_info_array = [Fixture.Param(1, 1), Fixture.Param(30, 4), Fixture.Param(10, 9)],
        predict_gain_act_gauge_array = [new() { act_type = 1, gain_act_gauge = 60 }]
    }];
    paired.Chara.training_level_info_array = [.. paired.Chara.training_level_info_array, new() { command_id = 5101, level = 7 }];
    var pairedTurn = FestaProjection.Create(paired.Chara, paired.Home, paired.Data, true);
    Equal(5, pairedTurn.Training.Length, "enabled practice replaces its ordinary training column");
    Sequence([5101, 105, 102, 103, 106], pairedTurn.Training.Select(training => training.CommandId),
        "replacement preserves five stat columns in order");
    var practice = pairedTurn.Training.Single(x => x.CommandId == 5101);
    Sequence([32, 0, 0, 0, 0], practice.Gains, "practice uses its own home and scenario params");
    Equal(34, practice.PtGain, "practice skill points do not include normal training");
    Equal(36, practice.VitalAfter, "practice vitality combines cost and scenario reduction");
    Equal(7, practice.Level, "practice has a separate training level");
    Equal(40, practice.FailureRate, "practice failure rate comes from its home command");
    Equal(60, practice.Gauges.Single().Gain, "practice has a separate gauge preview");
    Equal(2, practice.Partners.Single().TargetId, "practice has a separate partner list");
    paired.Home.command_info_array[^1].is_enable = 0;
    var ordinary = FestaProjection.Create(paired.Chara, paired.Home, paired.Data, true).Training[0];
    Equal(101, ordinary.CommandId, "disabled practice leaves ordinary training visible");
    Sequence([32, 42, 42, 21, 21], ordinary.Gains, "ordinary gains exclude the disabled practice");
    Equal(20, ordinary.PtGain, "ordinary training keeps its own scenario bonus");
    Equal(39, ordinary.VitalAfter, "ordinary vitality excludes the disabled practice");
    Equal(30, ordinary.Gauges.Single().Gain, "ordinary gauge preview excludes the disabled practice");
    paired.Home.command_info_array = [.. paired.Home.command_info_array.Where(command => command.command_id != 101)];
    var disabledPractice = FestaProjection.Create(paired.Chara, paired.Home, paired.Data, true).Training[0];
    Equal(5101, disabledPractice.CommandId, "sole disabled practice remains an explicit unavailable command");
    Check(!disabledPractice.IsEnabled, "sole disabled practice is not made executable");
    paired.Home.command_info_array = [.. paired.Home.command_info_array, paired.Home.command_info_array[^1]];
    Throws<InvalidDataException>(() => FestaProjection.Create(paired.Chara, paired.Home, paired.Data, true),
        "duplicate disabled practice cannot be silently selected");
    var missingAdvice = new Fixture();
    missingAdvice.Data.act_info_array[0].advice_id = FestaMasterData.Advices[0].Id;
    Throws<InvalidDataException>(() => FestaProjection.Create(missingAdvice.Chara, missingAdvice.Home, missingAdvice.Data, true), "equipped teaching requires its fusion record");
}

static void TestSnapshot()
{
    var fixture = new Fixture();
    fixture.Data.not_up_parameter_info.is_overflow_act_gauge_event_gain = true;
    fixture.Data.not_up_parameter_info.is_overflow_act_level_event_gain = true;
    var turn = FestaProjection.Create(fixture.Chara, fixture.Home, fixture.Data, true);
    var before = JsonSerializer.Serialize(turn);
    fixture.Chara.speed = 1;
    fixture.Chara.evaluation_info_array[0].evaluation = 1;
    fixture.Home.command_info_array[0].training_partner_array[0] = 99;
    fixture.Home.command_info_array[0].params_inc_dec_info_array[0].value = 999;
    fixture.Data.act_info_array[0].act_gauge = 999;
    fixture.Data.evaluation_info_array[0].chara_id = 999;
    fixture.Data.command_info_array[0].predict_gain_act_gauge_array[0].gain_act_gauge = 999;
    fixture.Data.not_up_parameter_info.not_level_up_act_type_array[0] = 5;
    Equal(before, JsonSerializer.Serialize(turn), "snapshot must own nested DTO state");
    Check(turn.GaugeOverflow && turn.LevelOverflow, "server overflow flags");
}

static void TestMasterData()
{
    Equal(10, FestaMasterData.Advices.Count, "all ten teachings");
    Equal(10, FestaMasterData.Advices.Select(advice => advice.Id).Distinct().Count(), "unique teaching IDs");
    foreach (var culture in new[] { "zh-CN", "en-US", "ja-JP" })
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        for (var type = 1; type <= 5; type++)
            Check(!string.IsNullOrWhiteSpace(FestaMasterData.ActName(type)), "act resource " + culture);
        foreach (var advice in FestaMasterData.Advices)
        {
            Check(!string.IsNullOrWhiteSpace(FestaMasterData.AdviceName(advice.Id)), "teaching resource " + culture);
            for (var level = 1; level <= 10; level++)
                Check(FestaMasterData.AdviceEffect(advice.Id, level).Count > 0, "teaching effect at each level");
            if (advice.Group == 1)
                Check(FestaMasterData.FusionEffect(advice.Id).Count > 0, "each fusion source");
        }
        for (var level = 1; level <= 10; level++)
        {
            var fourTypes = FestaMasterData.ActEffects(level, false);
            var fiveTypes = FestaMasterData.ActEffects(level, true);
            Check(fiveTypes.Count >= fourTypes.Count && fourTypes.All(fiveTypes.Contains), "five-type effects are additional");
            if (level >= 8)
                Check(fiveTypes.Count > fourTypes.Count, "five-type late level bonus");
        }
    }
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
    Sequence([24, 48, 68], FestaMasterData.Schedules.Select(schedule => schedule.Turn), "checkpoint turns from master schedule");
    Sequence([3, 5, 6], FestaMasterData.Schedules.Select(schedule => schedule.NeedLevel), "checkpoint target levels");
    Throws<ArgumentOutOfRangeException>(() => FestaMasterData.ActName(6), "unknown act fails clearly");
    Throws<ArgumentOutOfRangeException>(() => FestaMasterData.AdviceEffect(FestaMasterData.Advices[0].Id, 0), "locked level is not a level-one estimate");
}

static void TestRenderer(WorkspaceSmokeSession ui, string artifactDirectory)
{
    var fixture = new Fixture();
    var turn = FestaProjection.Create(fixture.Chara, fixture.Home, fixture.Data, true);
    var vitalities = new[] { 29, 39, 69, 80, 50 };
    turn = turn with
    {
        MaxStats = [1390, 1500, 2000, 2200, 2400],
        Training = [.. turn.Training.Select(training => training with
        {
            CommandId = training.Index < 2 ? 5101 + training.Index : training.CommandId,
            Level = training.Index < 2 ? training.Index + 1 : training.Level,
            IsEnabled = training.Index != 4,
            Gains = [training.Index == 4 ? 999 : 20 + training.Index * 10, 0, 0, 0, 0],
            PtGain = 120 + training.Index, VitalAfter = vitalities[training.Index],
            Gauges = training.Index == 1 ? [.. training.Gauges, new(3, 0), new(5, 1), new(4, 2)] : training.Gauges,
            Partners = training.Index == 2
                ? [new(21, 1001, 1, "羁绊零", 0, false, false, false),
                    new(22, 1002, 2, "羁绊满", 100, false, false, false),
                    new(23, 1003, 3, "羁绊一", 1, false, false, false)]
                : training.Partners
        })]
    };
    var snapshot = JsonSerializer.Serialize(turn);
    var workspace = Workspace.Create("FestaSmokeRenderer");
    workspace.SetPanel("renderer", "Festa", FestaDisplayRenderer.Render(turn), fullBleed: true);
    var screen = ui.CaptureScreen(120, 35, restore: false);
    File.WriteAllText(Path.Combine(artifactDirectory, "training-framebuffer.txt"), screen);
    var cells = ui.CaptureCells(120, 35);
    var views = ui.InvokeOnOwner(() => HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!).ToArray());
    var page = views.Single(view => view.Id == "festa-root");
    Check(ui.InvokeOnOwner(() => !page.HorizontalScrollBar.Visible && !page.VerticalScrollBar.Visible),
        "120x35 fits all grouped training cards without scrollbars");
    Equal(5, views.Count(view => view is FrameView && view.Id?.StartsWith("festa-training:", StringComparison.Ordinal) == true),
        "one visible card per stat");
    Check(!views.Any(view => view.Id is "festa-tab-training" or "festa-tab-comparison" or "festa-extras" ||
        view.Id?.StartsWith("festa-act:", StringComparison.Ordinal) == true), "training has no comparison, act cards or details sidebar");
    Check(Enumerable.Range(0, 33).SelectMany(y => Enumerable.Range(95, 25).Select(x => cells[y, x].Grapheme))
        .All(string.IsNullOrWhiteSpace), "the area to the right of the 95-column dashboard is blank");
    Contains(screen, "321", "skill points remain visible at the default viewport");
    FrameToken(ui, cells, "festa-important", FestaText.Get("ActLevelGap", FestaMasterData.ActName(2), 3));
    Check(!screen.Contains("支援类型", StringComparison.Ordinal) && !screen.Contains("外出", StringComparison.Ordinal) &&
        !screen.Contains("教诲", StringComparison.Ordinal), "decision view omits support-type explanations, outing and teaching details");
    var cyan = new Terminal.Gui.Drawing.Color(StandardColor.BrightCyan);
    var red = new Terminal.Gui.Drawing.Color(StandardColor.BrightRed);
    var yellow = new Terminal.Gui.Drawing.Color(StandardColor.BrightYellow);
    var green = new Terminal.Gui.Drawing.Color(StandardColor.BrightGreen);
    var vitalityColors = new[] { red, new Terminal.Gui.Drawing.Color(255, 140, 0), yellow, green, yellow };
    foreach (var training in turn.Training)
    {
        var id = "festa-training:" + training.CommandId;
        var bounds = ui.InvokeOnOwner(() => views.Single(view => view.Id == id).FrameToScreen());
        Equal(19, bounds.Width, "each stat card keeps its 19-column width");
        Check(bounds.Top >= 0 && bounds.Right <= 95 && bounds.Bottom <= 35, "all five training cards fit in the default viewport");
        _ = FindToken(cells, FestaText.Get(training.IsActTraining ? "ActTrainingTitle" : "TrainingTitle", FestaText.Get("Stat" + training.Index)), bounds);
        var capacityBounds = ui.InvokeOnOwner(() => views.Single(view => view.Id == id + ":capacity").FrameToScreen());
        var costBounds = ui.InvokeOnOwner(() => views.Single(view => view.Id == id + ":cost").FrameToScreen());
        var gainBounds = ui.InvokeOnOwner(() => views.Single(view => view.Id == id + ":gain").FrameToScreen());
        _ = FindToken(cells, "当前:可获得", capacityBounds);
        var current = FindToken(cells, turn.Stats[training.Index] + ":" + (turn.MaxStats[training.Index] - turn.Stats[training.Index]), capacityBounds);
        if (training.Index < 2)
        {
            var warningColor = training.Index == 0 ? red : yellow;
            Equal(warningColor, cells[current.Y, current.X + (turn.Stats[training.Index] + ":").GetColumns()].Attribute!.Value.Foreground,
                "remaining capacity uses the warning color");
            Check(cells[current.Y, current.X].Attribute!.Value.Foreground != warningColor, "current stat does not inherit capacity warning color");
        }
        var vitality = FindToken(cells, training.VitalAfter + "/100", costBounds);
        Equal(vitalityColors[training.Index], cells[vitality.Y, vitality.X].Attribute!.Value.Foreground,
            "training vitality uses its absolute threshold color");
        Check(cells[vitality.Y, vitality.X + training.VitalAfter.ToString().Length].Attribute!.Value.Foreground != vitalityColors[training.Index],
            "vitality maximum does not inherit the current value color");
        _ = FindToken(cells, "Lv" + training.Level, costBounds);
        var gain = FindToken(cells, "属:" + training.Gains.Sum() + "|Pt:" + training.PtGain, gainBounds);
        Equal(training.Index == 3, cells[gain.Y, gain.X + "属:".GetColumns()].Attribute!.Value.Foreground == cyan,
            "only the highest enabled stat gain number is highlighted");
        Check(cells[gain.Y, gain.X].Attribute!.Value.Foreground != cyan, "stat gain label does not inherit the highlight");
        var pt = FindToken(cells, "Pt:" + training.PtGain, gainBounds);
        Check(cells[pt.Y, pt.X].Attribute!.Value.Foreground != cyan, "Pt does not inherit the gain highlight");
        if (training.FailureRate > 0)
        {
            var failure = FindToken(cells, "(" + training.FailureRate + "%)", bounds);
            Equal(red, cells[failure.Y, failure.X].Attribute!.Value.Foreground, "failure risk in the title remains red");
        }
        foreach (var gauge in training.Gauges)
            _ = FindToken(cells, FestaText.Get("GaugeGain", FestaMasterData.ActName(gauge.ActType), gauge.Gain), costBounds);
        var renderedNames = string.Concat(Enumerable.Range(gainBounds.Top, gainBounds.Height)
            .SelectMany(y => Enumerable.Range(gainBounds.Left, gainBounds.Width).Select(x => cells[y, x].Grapheme))
            .Where(grapheme => !string.IsNullOrWhiteSpace(grapheme)));
        foreach (var partner in training.Partners)
            Contains(renderedNames, string.Concat(partner.Name.Where(character => !char.IsWhiteSpace(character))),
                "long support and NPC names remain complete across wrapped rows");
    }
    for (var separator = 0; separator < 4; separator++)
    {
        var positions = ui.InvokeOnOwner(() => turn.Training.Select(training => views
            .Single(view => view.Id == $"festa-training:{training.CommandId}:separator:{separator}").FrameToScreen().Y).ToArray());
        Equal(1, positions.Distinct().Count(), "each training group separator aligns across all five columns");
    }
    Equal(1, ui.InvokeOnOwner(() => turn.Training.Select(training => views
        .Single(view => view.Id == $"festa-training:{training.CommandId}:gain").FrameToScreen().Y).Distinct().Count()),
        "training gain rows align despite multi-act gauges and long names");
    FrameToken(ui, cells, "festa-training:106", "当前不可执行");
    var firstGain = ui.InvokeOnOwner(() => views.Single(view => view.Id == "festa-training:5101:gain").FrameToScreen());
    var hint = FindToken(cells, "![速]", firstGain);
    Equal(red, cells[hint.Y, hint.X].Attribute!.Value.Foreground, "hint marker remains red");
    Equal(cyan, cells[hint.Y, hint.X + 1].Attribute!.Value.Foreground, "shining support name remains cyan");
    var friendship = FindToken(cells, "80", firstGain);
    Equal(yellow, cells[friendship.Y, friendship.X].Attribute!.Value.Foreground, "friendship retains its own color after a long name wraps");
    var friend = FindToken(cells, turn.Training[0].Partners.Single(partner => partner.IsFriend).Name, firstGain);
    Equal(green, cells[friend.Y, friend.X].Attribute!.Value.Foreground, "friend support name remains green");
    FrameToken(ui, cells, "festa-training:102:gain", "羁绊一 1");
    var friendshipBounds = ui.InvokeOnOwner(() => views.Single(view => view.Id == "festa-training:102:gain").FrameToScreen());
    var friendshipRows = string.Join('\n', Enumerable.Range(friendshipBounds.Top, friendshipBounds.Height).Select(y =>
        string.Concat(Enumerable.Range(friendshipBounds.Left, friendshipBounds.Width).Select(x => cells[y, x].Grapheme))));
    Check(!friendshipRows.Contains(" 0", StringComparison.Ordinal) && !friendshipRows.Contains(" 100", StringComparison.Ordinal),
        "zero and full friendship are omitted");
    Check(!screen.Contains("(0%)", StringComparison.Ordinal), "zero failure does not occupy title space");
    TestHorizontalMouseScroll(ui);
    Equal(snapshot, JsonSerializer.Serialize(turn), "rendering and scrolling preserve the immutable turn snapshot");
    Contains(ui.CaptureScreen(120, 35, restore: false), "321", "resize returns the visible header");

    var quiet = turn with
    {
        Turn = 73, CampRemaining = 0, GaugeOverflow = false, LevelOverflow = false, NotLevelUpActs = [],
        Acts = [.. turn.Acts.Select(act => act with { Level = 10 })],
        Training = [.. turn.Training.Select(training => training with { IsEnabled = true, Partners = [], Gauges = [] })]
    };
    workspace.SetPanel("renderer", "Festa", FestaDisplayRenderer.Render(quiet), fullBleed: true);
    var finalStage = ui.CaptureScreen(120, 35, restore: false);
    Contains(finalStage, "第73回合", "post-senior turn keeps the exact turn number");
    Check(!finalStage.Contains("4年", StringComparison.Ordinal), "post-senior turn does not invent a fourth training year");
    var quietViews = ui.InvokeOnOwner(() => HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!).ToArray());
    Check(!quietViews.Any(view => view.Id == "festa-important"), "no empty prompt frame is reserved when no decision warnings remain");
    Check(ui.InvokeOnOwner(() => quiet.Training.All(training => quietViews
        .Single(view => view.Id == $"festa-training:{training.CommandId}:gain").Frame.Height == 1)),
        "empty supporter lists do not reserve five blank rows");
    workspace.SetPanel("renderer", "Festa", FestaDisplayRenderer.Render(quiet with { Turn = 25 }), fullBleed: true);
    ui.CaptureScreen(120, 35, restore: false);
    Check(ui.InvokeOnOwner(() => !HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!).Any(view => view.Id == "festa-important")),
        "a reached future checkpoint does not create a prompt");
    var warnings = quiet with { CampRemaining = 2, GaugeOverflow = true, LevelOverflow = true, NotLevelUpActs = [2] };
    workspace.SetPanel("renderer", "Festa", FestaDisplayRenderer.Render(warnings), fullBleed: true);
    var warningCells = ui.CaptureCells(120, 35);
    foreach (var message in new[] { FestaText.Get("CampRemaining", 2), FestaText.Get("GaugeOverflow"), FestaText.Get("LevelOverflow"),
        FestaText.Get("NotLevelUpActs", FestaMasterData.ActName(2)) })
        FrameToken(ui, warningCells, "festa-important", message);
    var highLevel = quiet with
    {
        Vital = 108, MaxVital = 108,
        Training = [.. quiet.Training.Select(training => training with { FailureRate = 100, VitalAfter = 108 })]
    };
    workspace.SetPanel("renderer", "Festa", FestaDisplayRenderer.Render(highLevel), fullBleed: true);
    var highLevelCells = ui.CaptureCells(120, 35);
    foreach (var training in highLevel.Training)
    {
        var bounds = ui.InvokeOnOwner(() => HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!)
            .Single(view => view.Id == "festa-training:" + training.CommandId).FrameToScreen());
        _ = FindToken(highLevelCells, "108/108", bounds);
        var failure = FindToken(highLevelCells, "(100%)", bounds);
        foreach (var x in new[] { failure.X, failure.X + "(100%)".GetColumns() - 1 })
            Equal(red, highLevelCells[failure.Y, x].Attribute!.Value.Foreground, "failure title remains red through the closing parenthesis");
    }
    workspace.RemovePanel("renderer");
    ui.Bootstrap.SwitchTo();
    ui.CaptureScreen(120, 35, restore: false);
}

static async Task TestPlugin(WorkspaceSmokeSession ui)
{
    using var context = new RuntimePluginContext(ui.Application);
    var analyzer = new FestaPlugin();
    var snapshots = (IReadOnlyDictionary<FestaDisplayHistory.Key, FestaTurn>)typeof(FestaPlugin)
        .GetField("snapshots", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .GetValue(analyzer)!;
    IPlugin plugin = analyzer;
    var initialWorkspace = Workspace.Current;
    plugin.Initialize(context);
    await plugin.StartAsync();
    Check(ReferenceEquals(initialWorkspace, Workspace.Current), "initialization does not select an empty workspace");
    Check(context.AnalyzerRegistry.Registrations.All(registration => registration.Kind == AnalyzerKind.Response), "response registrations only");
    var first = new Fixture(11, 25, 73001);
    foreach (var (endpoint, payload, hasHome) in RegularResponses(first))
    {
        await Dispatch(context, endpoint, payload);
        Equal(73001, analyzer.Latest!.SkillPoints, "regular response projection " + endpoint.Name);
        Equal(hasHome ? 5 : 0, analyzer.Latest.Training.Length, "regular response training availability " + endpoint.Name);
    }
    await Dispatch(context, typeof(GameApi.SingleModeFesta.Load), first.Load());
    var workspace = Workspace.Current;
    Contains(ui.CaptureScreen(), "73001", "load dispatch renders full response");
    var beforeEvent = ui.CaptureScreen();
    var pendingEvent = new Fixture(11, 25, 73099).Exec();
    pendingEvent.data.chara_info.playing_state = 5;
    pendingEvent.data.unchecked_event_array = [new()];
    await Dispatch(context, typeof(GameApi.SingleModeFesta.ExecCommand), pendingEvent);
    Equal(73099, analyzer.Latest!.SkillPoints, "event response updates current state");
    Check(analyzer.Latest.Training.IsEmpty, "event state contains no executable training prediction");
    Equal(beforeEvent, ui.CaptureScreen(), "event response preserves the last complete training screen");
    Equal(73001, snapshots[new(11, 25)].SkillPoints, "event response does not replace training history");
    first.Chara.skill_point = 99999;
    var second = new Fixture(11, 26, 73002);
    await Dispatch(context, typeof(GameApi.SingleModeFesta.ExecCommand), second.Exec());
    TestHorizontalMouseScroll(ui);
    Focus(ui, "festa-root");
    ui.SendKey(Key.CursorLeft);
    Contains(ui.CaptureScreen(), "73001", "left arrow after mouse scrolling still selects the immutable oldest history");
    await Dispatch(context, typeof(GameApi.SingleModeFesta.AttachAdvice), new Fixture(11, 25, 73011).Attach());
    Contains(ui.CaptureScreen(), "73011", "visible older key replacement refreshes in place");
    Equal(2, snapshots.Count, "same-key replacement retains exactly two snapshots");
    Equal(73011, snapshots[new(11, 25)].SkillPoints, "same-key snapshot contents are replaced");
    var third = new Fixture(22, 26, 73003);
    var incompleteNewRun = third.Load();
    incompleteNewRun.data.festa_data_set = null!;
    await Dispatch(context, typeof(GameApi.SingleModeFesta.Load), incompleteNewRun);
    Check(analyzer.Latest is null, "new run without a data set cannot retain the previous run's latest state");
    await Dispatch(context, typeof(GameApi.SingleModeFesta.Load), third.Load());
    Contains(ui.CaptureScreen(), "73011", "new run does not move an older history selection");
    Contains(ui.CaptureScreen(), "有新的训练历史", "new history notifies while browsing an older turn");
    ui.SendKey(Key.CursorRight);
    Contains(ui.CaptureScreen(), "73003", "same turn in different run is a separate history entry");

    var fusion = new SingleModeFestaFusionAdviceResponse
    {
        data = new() { festa_data_set_fusion = new() { advice_info_array = [new() { advice_id = 999999, fusion_advice_id = 999998 }], act_info_array = [] } },
    };
    var beforeFusion = ui.CaptureScreen();
    await Dispatch(context, typeof(GameApi.SingleModeFesta.FusionAdvice), fusion);
    Equal(beforeFusion, ui.CaptureScreen(), "partial fusion cannot replace or mutate full snapshot");
    var replacement = new Fixture(22, 26, 73004);
    await Dispatch(context, typeof(GameApi.SingleModeFesta.AttachAdvice), replacement.Attach());
    Contains(ui.CaptureScreen(), "73004", "full attach response refreshes same history key");
    Focus(ui, "festa-root");
    ui.SendKey(Key.CursorUp);
    Contains(ui.CaptureScreen(), "73002", "replacement does not append duplicate history");
    ui.SendKey(Key.CursorDown);
    Contains(ui.CaptureScreen(), "73004", "newer key navigation");

    var settingsPath = Path.Combine("PluginData", "FestaScenarioAnalyzer", "settings.json");
    try
    {
        await ui.RunDialogAsync(() => plugin.ConfigPromptAsync(ui.Application), dialog =>
        {
            HistoryConfigDialog.SetHistoryLimit(dialog, 1);
            var handled = false;
            void Observe(object? sender, Key key) => handled = key.Handled;
            ui.Application.Keyboard.KeyDown += Observe;
            try { ui.Application.Keyboard.RaiseKeyDownEvent(Key.CursorLeft); }
            finally { ui.Application.Keyboard.KeyDown -= Observe; }
            Check(!handled, "history arrows are not intercepted in modal config");
            HistoryConfigDialog.AcceptButton(ui.Application, dialog, "取消");
        });
        throw new InvalidOperationException("Cancel must signal cancellation.");
    }
    catch (OperationCanceledException) { }
    Check(!File.Exists(settingsPath), "cancel does not write settings");
    Focus(ui, "festa-root");
    ui.SendKey(Key.CursorLeft);
    Contains(ui.CaptureScreen(), "73011", "oldest snapshot is visible before reducing history capacity");
    await SaveLimit(2);
    Equal(2, JsonDocument.Parse(File.ReadAllText(settingsPath)).RootElement.GetProperty("historyLimit").GetInt32(), "save persists history limit");
    Equal(2, snapshots.Count, "shrinking history releases the previously visible evicted snapshot");
    Check(snapshots.ContainsKey(new(11, 26)) && snapshots.ContainsKey(new(22, 26)), "only retained history keys survive shrinking");
    Contains(ui.CaptureScreen(), "73004", "shrinking while browsing an evicted key shows latest");
    Focus(ui, "festa-root");
    ui.SendKey(Key.CursorLeft);
    Contains(ui.CaptureScreen(), "73002", "reducing history evicts oldest run entry");
    await Dispatch(context, typeof(GameApi.SingleModeFesta.ExecCommand), new Fixture(22, 27, 73005).Exec());
    Contains(ui.CaptureScreen(), "73005", "evicting the selected entry returns to latest");
    await SaveLimit(0);
    Equal(new FestaDisplayHistory.Key(22, 27), snapshots.Keys.Single(), "history zero retains only the visible live snapshot");
    await Dispatch(context, typeof(GameApi.SingleModeFesta.ExecCommand), new Fixture(22, 28, 73006).Exec());
    Equal(new FestaDisplayHistory.Key(22, 28), snapshots.Keys.Single(), "history zero releases the previous live snapshot");
    Focus(ui, "festa-root");
    Check(!ui.WasHandledByApplicationKeyDown(Key.CursorUp), "history zero releases arrow interception");
    Contains(ui.CaptureScreen(), "73006", "history zero still shows latest");
    ui.CaptureScreen(70, 18, restore: false);
    Focus(ui, "festa-root");
    var pageTop = ui.CaptureScreen(70, 18, restore: false);
    ui.SendKey(Key.PageDown);
    Check(pageTop != ui.CaptureScreen(70, 18, restore: false), "history zero keeps page scrolling");
    ui.SendKey(Key.Home);
    await SaveLimit(2);
    Equal(new FestaDisplayHistory.Key(22, 28), snapshots.Keys.Single(), "enabling history retains the current live snapshot until a response arrives");
    Focus(ui, "festa-root");
    ui.SendKey(Key.CursorLeft);
    Contains(ui.CaptureScreen(), "73006", "enabling history preserves the current picture without backfilling history");
    await Dispatch(context, typeof(GameApi.SingleModeFesta.AttachAdvice), new Fixture(22, 28, 73016).Attach());
    Equal(new FestaDisplayHistory.Key(22, 28), snapshots.Keys.Single(), "same-key response after enabling history keeps its snapshot");
    Equal(73016, snapshots[new(22, 28)].SkillPoints, "same-key live update retains the new contents");
    await SaveLimit(0);
    await SaveLimit(2);
    await Dispatch(context, typeof(GameApi.SingleModeFesta.ExecCommand), new Fixture(22, 29, 73007).Exec());
    Equal(new FestaDisplayHistory.Key(22, 29), snapshots.Keys.Single(), "first new key after enabling history releases the unretained live snapshot");
    Focus(ui, "festa-root");
    ui.SendKey(Key.CursorLeft);
    Contains(ui.CaptureScreen(), "73007", "history does not backfill the output from before it was enabled");
    await plugin.DisposeAsync();
    Equal(0, snapshots.Count, "disposal releases all retained snapshots");
    Check(analyzer.Latest is null, "disposal clears the current snapshot");
    ui.Flush();
    Check(!ui.CaptureScreen().Contains("73007", StringComparison.Ordinal), "dispose removes analyzer panel");
    Check(ReferenceEquals(workspace, Workspace.Create(workspace.Title)), "dispose preserves host workspace identity");
    File.Delete(settingsPath);

    async Task SaveLimit(int limit) => await ui.RunDialogAsync(() => plugin.ConfigPromptAsync(ui.Application), dialog =>
    {
        HistoryConfigDialog.SetHistoryLimit(dialog, limit);
        HistoryConfigDialog.AcceptButton(ui.Application, dialog, "保存");
    });
}

static IEnumerable<(Type Endpoint, object Payload, bool HasHome)> RegularResponses(Fixture fixture)
{
    yield return (typeof(GameApi.SingleModeFesta.Start), new SingleModeFestaStartResponse
    {
        data = new() { single_mode_start_common = new() { chara_info = fixture.Chara, home_info = fixture.Home, unchecked_event_array = [] }, festa_data_set = fixture.Data },
    }, true);
    yield return (typeof(GameApi.SingleModeFesta.CheckEvent), new SingleModeFestaCheckEventResponse
    {
        data = new() { chara_info = fixture.Chara, home_info = fixture.Home, festa_data_set = fixture.Data, unchecked_event_array = [] },
    }, true);
    yield return (typeof(GameApi.SingleModeFesta.CheckPoint), new SingleModeFestaCheckPointResponse
    {
        data = new() { chara_info = fixture.Chara, home_info = fixture.Home, festa_data_set = fixture.Data, unchecked_event_array = [] },
    }, true);
    yield return (typeof(GameApi.SingleModeFesta.Continue), new SingleModeFestaContinueResponse
    {
        data = new() { chara_info = fixture.Chara, home_info = fixture.Home, festa_data_set = fixture.Data, unchecked_event_array = [] },
    }, true);
    yield return (typeof(GameApi.SingleModeFesta.FestaLive), new SingleModeFestaFestaLiveResponse
    {
        data = new() { chara_info = fixture.Chara, home_info = fixture.Home, festa_data_set = fixture.Data, unchecked_event_array = [] },
    }, true);
    yield return (typeof(GameApi.SingleModeFesta.GainSkills), new SingleModeFestaGainSkillsResponse
    {
        data = new() { chara_info = fixture.Chara, home_info = fixture.Home, festa_data_set = fixture.Data },
    }, true);
    yield return (typeof(GameApi.SingleModeFesta.RaceEnd), new SingleModeFestaRaceEndResponse
    {
        data = new() { chara_info = fixture.Chara, home_info = fixture.Home, festa_data_set = fixture.Data },
    }, true);
    yield return (typeof(GameApi.SingleModeFesta.RaceEntry), new SingleModeFestaRaceEntryResponse
    {
        data = new() { chara_info = fixture.Chara, home_info = fixture.Home, festa_data_set = fixture.Data, unchecked_event_array = [] },
    }, true);
    yield return (typeof(GameApi.SingleModeFesta.RaceOut), new SingleModeFestaRaceOutResponse
    {
        data = new() { chara_info = fixture.Chara, home_info = fixture.Home, festa_data_set = fixture.Data, unchecked_event_array = [] },
    }, true);
    yield return (typeof(GameApi.SingleModeFesta.ChangeShortCut), new SingleModeFestaChangeShortCutResponse
    {
        data = new() { chara_info = fixture.Chara, festa_data_set = fixture.Data, unchecked_event_array = [] },
    }, false);
    yield return (typeof(GameApi.SingleModeFesta.FinishClawCrane), new SingleModeFestaFinishClawCraneResponse
    {
        data = new() { chara_info = fixture.Chara, festa_data_set = fixture.Data, unchecked_event_array = [] },
    }, false);
}

static async ValueTask Dispatch(RuntimePluginContext context, Type endpointType, object payload)
{
    var endpoint = GameEndpointCatalog.ByEndpointType[endpointType];
    var registrations = context.AnalyzerRegistry.Registrations.Where(registration =>
        registration.Kind == AnalyzerKind.Response && PluginManager.ExpandEndpointPatterns(registration.Patterns).Contains(endpoint)).ToArray();
    Equal(1, registrations.Length, "exactly one analyzer registration for " + endpointType.Name);
    await registrations[0].Invoke(payload, endpoint);
}

static async Task TestConcurrentNavigation(WorkspaceSmokeSession ui, string artifactDirectory)
{
    using var context = new RuntimePluginContext(ui.Application);
    var analyzer = new FestaPlugin();
    analyzer.Initialize(context);
    Console.WriteLine("concurrency: publishing first turn");
    await Dispatch(context, typeof(GameApi.SingleModeFesta.Load), new Fixture(77, 25).Load());
    Console.WriteLine("concurrency: publishing second turn");
    await Dispatch(context, typeof(GameApi.SingleModeFesta.ExecCommand), new Fixture(77, 26).Exec());
    Console.WriteLine("concurrency: focusing dashboard");
    Focus(ui, "festa-root");
    Console.WriteLine("concurrency: entering UI barrier");
    using var enteredUi = new ManualResetEventSlim();
    var response = Task.Run(async () =>
    {
        Check(enteredUi.Wait(TimeSpan.FromSeconds(3)), "UI entered concurrent navigation test");
        await Dispatch(context, typeof(GameApi.SingleModeFesta.ExecCommand), new Fixture(77, 27).Exec());
    });
    ui.InvokeOnOwner(() =>
    {
        Console.WriteLine("concurrency: UI barrier active");
        enteredUi.Set();
        Check(SpinWait.SpinUntil(() => analyzer.Latest?.Turn == 27, TimeSpan.FromSeconds(3)), "background response reaches snapshot publication");
        Console.WriteLine("concurrency: background snapshot observed; navigating history");
        ui.Application.Keyboard.RaiseKeyDownEvent(Key.CursorLeft);
        Console.WriteLine("concurrency: history key returned");
        return true;
    });
    await response.WaitAsync(TimeSpan.FromSeconds(5));
    ui.Flush();
    var screen = ui.CaptureScreen();
    File.WriteAllText(Path.Combine(artifactDirectory, "concurrent-framebuffer.txt"), screen);
    Contains(screen, "第25回合", "concurrent history navigation completes on the selected turn");
    await analyzer.DisposeAsync();
}

static void TestHorizontalMouseScroll(WorkspaceSmokeSession ui)
{
    ui.CaptureScreen(70, 18, restore: false);
    var page = ui.InvokeOnOwner(() => HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!)
        .Single(view => view.Id == "festa-root"));
    var scrollbar = ui.InvokeOnOwner(() => page.HorizontalScrollBar);
    var bounds = ui.InvokeOnOwner(() => scrollbar.FrameToScreen());
    Check(ui.InvokeOnOwner(() => scrollbar.Visible && page.VerticalScrollBar.Visible), "narrow page exposes both scrollbars");
    var initial = ui.InvokeOnOwner(() => page.Viewport.X);
    ui.SendMouse(new Mouse { ScreenPosition = new(3, 5), Flags = MouseFlags.WheeledRight });
    Check(ui.InvokeOnOwner(() => page.Viewport.X) > initial, "horizontal wheel over text scrolls the whole dashboard");
    ui.SendMouse(new Mouse { ScreenPosition = new(3, 5), Flags = MouseFlags.WheeledLeft });
    Equal(initial, ui.InvokeOnOwner(() => page.Viewport.X), "horizontal wheel can return to the first column");
    Click(new(bounds.Right - 1, bounds.Y));
    var afterArrow = ui.InvokeOnOwner(() => page.Viewport.X);
    Check(afterArrow > initial, "horizontal scrollbar arrow responds to a mouse click");
    Click(new(bounds.Right - 2, bounds.Y));
    var afterTrack = ui.InvokeOnOwner(() => page.Viewport.X);
    Check(afterTrack > afterArrow, "horizontal scrollbar track responds to a mouse click");
    DragSlider(new(bounds.Left + 1, bounds.Y));
    var afterDragLeft = ui.InvokeOnOwner(() => page.Viewport.X);
    Check(afterDragLeft < afterTrack, "dragging the horizontal slider left moves the viewport");
    DragSlider(new(bounds.Right - 2, bounds.Y));
    Check(ui.InvokeOnOwner(() => page.Viewport.X) > afterDragLeft, "dragging the horizontal slider right moves the viewport");
    Contains(ui.CaptureScreen(70, 18, restore: false), "智力", "mouse scrolling exposes the rightmost training column");
    var beforeWheel = ui.InvokeOnOwner(() => page.Viewport.Location);
    ui.SendMouse(new Mouse { ScreenPosition = new(3, 5), Flags = MouseFlags.WheeledDown });
    Check(ui.InvokeOnOwner(() => page.Viewport.Y > beforeWheel.Y && page.Viewport.X == beforeWheel.X),
        "vertical wheel over text scrolls the single page without changing its horizontal position");
    Click(new(3, 5));
    Check(ui.InvokeOnOwner(() => page.HasFocus), "clicking body text focuses the single page");
    ui.SendKey(Key.Home);
    Equal(0, ui.InvokeOnOwner(() => page.Viewport.Y), "Home returns to the page top");
    ui.SendKey(Key.PageDown);
    var paged = ui.InvokeOnOwner(() => page.Viewport.Y);
    Check(paged > 0, "PageDown scrolls the focused page");
    ui.SendKey(Key.PageUp);
    Check(ui.InvokeOnOwner(() => page.Viewport.Y) < paged, "PageUp scrolls back toward the header");
    ui.SendKey(Key.End);
    Check(ui.InvokeOnOwner(() => page.Viewport.Y) > 0, "End reaches the lower part of the page");
    ui.SendKey(Key.Home);
    DragSlider(new(bounds.Left + 1, bounds.Y));

    void Click(Point point)
    {
        foreach (var flags in new[] { MouseFlags.PositionReport, MouseFlags.LeftButtonPressed, MouseFlags.LeftButtonReleased, MouseFlags.LeftButtonClicked })
            ui.SendMouse(new Mouse { ScreenPosition = point, Flags = flags });
    }

    void DragSlider(Point destination)
    {
        var slider = ui.InvokeOnOwner(() => scrollbar.Slider.FrameToScreen());
        var start = new Point(slider.Left + slider.Width / 2, slider.Y);
        ui.SendMouse(new Mouse { ScreenPosition = start, Flags = MouseFlags.LeftButtonPressed });
        ui.SendMouse(new Mouse { ScreenPosition = destination, Flags = MouseFlags.LeftButtonPressed | MouseFlags.PositionReport });
        ui.SendMouse(new Mouse { ScreenPosition = destination, Flags = MouseFlags.LeftButtonReleased });
    }
}

static void FrameToken(WorkspaceSmokeSession ui, Cell[,] cells, string frameId, string token)
{
    var bounds = ui.InvokeOnOwner(() => HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!)
        .Single(view => view.Id == frameId).FrameToScreen());
    _ = FindToken(cells, token, bounds);
}

static Point FindToken(Cell[,] cells, string token, Rectangle? bounds = null)
{
    var region = bounds ?? new Rectangle(0, 0, cells.GetLength(1), cells.GetLength(0));
    var elements = new List<string>();
    var enumerator = StringInfo.GetTextElementEnumerator(token);
    while (enumerator.MoveNext())
        elements.Add((string)enumerator.Current);
    for (var y = Math.Max(0, region.Top); y < Math.Min(cells.GetLength(0), region.Bottom); y++)
    for (var x = Math.Max(0, region.Left); x < Math.Min(cells.GetLength(1), region.Right); x++)
    {
        var column = x;
        var index = 0;
        while (index < elements.Count && column < Math.Min(cells.GetLength(1), region.Right) && cells[y, column].Grapheme == elements[index])
        {
            column += Math.Max(1, elements[index].GetColumns());
            index++;
        }
        if (index == elements.Count)
            return new(x, y);
    }
    throw new InvalidOperationException($"Framebuffer token '{token}' not found in {region}.");
}

static void Focus(WorkspaceSmokeSession ui, string id)
{
    ui.Flush();
    ui.InvokeOnOwner(() =>
    {
        var root = ui.Application.TopRunnableView ?? throw new InvalidOperationException("UI has no root.");
        var view = HistoryConfigDialog.Descendants(root).Single(view => view.Id == id);
        Check(view.HasFocus || view.SetFocus(), "focus " + id);
        return true;
    });
}

static void Run(string name, Action action)
{
    Console.WriteLine("RUN " + name);
    action();
    Console.WriteLine("PASS " + name);
}

static async Task RunAsync(string name, Func<Task> action)
{
    Console.WriteLine("RUN " + name);
    await action();
    Console.WriteLine("PASS " + name);
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual, string message)
    => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, actual {actual}");

static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    => Check(expected.SequenceEqual(actual), $"{message}: expected [{string.Join(',', expected)}], actual [{string.Join(',', actual)}]");

static void Contains(string screen, string text, string message)
    => Check(screen.Contains(text, StringComparison.Ordinal), $"{message}: missing {text}\n{screen}");

static void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException(message + ": expected " + typeof(T).Name);
}
