using FestaScenarioAnalyzer;
using Gallop;
using Gallop.Endpoints;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Plugin;
using FestaPlugin = FestaScenarioAnalyzer.FestaScenarioAnalyzer;

internal static class CaptureReplay
{
    internal static async Task RunAsync(WorkspaceSmokeSession ui, string path)
    {
        const string marker = "-umamusume-single_mode_festa-";
        var files = Directory.EnumerateFiles(path, "*" + marker + "*.msgpack")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        Check(files.Length != 0, "No Festa MessagePack captures were found.");

        using var context = new RuntimePluginContext(ui.Application);
        var analyzer = new FestaPlugin();
        IPlugin plugin = analyzer;
        plugin.Initialize(context);
        await plugin.StartAsync();
        var registrations = context.AnalyzerRegistry.Registrations
            .Where(registration => registration.Kind == AnalyzerKind.Response)
            .SelectMany(registration => PluginManager.ExpandEndpointPatterns(registration.Patterns)
                .Select(endpoint => (endpoint.Path, Registration: registration)))
            .ToDictionary(pair => pair.Path, pair => pair.Registration, StringComparer.Ordinal);
        var requests = 0;
        var responses = 0;
        var dispatched = 0;
        var successfulTraining = 0;
        var failedTraining = 0;
        var campTraining = 0;
        var revisedTraining = 0;
        var fusionChecks = 0;
        var trainingFrames = new HashSet<bool>();
        var commands = new SortedDictionary<int, int>();
        TrainingRequest? pending = null;

        try
        {
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var commandId = pending?.Prediction.CommandId;
                try
                {
                    var markerIndex = name.IndexOf(marker, StringComparison.Ordinal);
                    Check(markerIndex > 0, "Missing endpoint marker.");
                    var direction = name[markerIndex - 1];
                    Check(direction is 'Q' or 'R', "Capture direction must be Q or R.");
                    var endpointPath = "/umamusume/single_mode_festa/" +
                        name[(markerIndex + marker.Length)..^".msgpack".Length];
                    var endpoint = GameEndpointCatalog.ByPath[endpointPath];
                    var dispatch = new AnalyzerDispatchContext(endpoint, await File.ReadAllBytesAsync(file),
                        new GameHttpHeaders(null, null, null, null, null, null));
                    Check(pending is null || direction == 'R' &&
                        endpoint.ResponseType == typeof(SingleModeFestaExecCommandResponse),
                        "Training request was not immediately followed by its exec_command response: " + pending?.FileName);

                    if (direction == 'Q')
                    {
                        requests++;
                        var request = dispatch.GetDto(endpoint.RequestType);
                        if (request is SingleModeFestaExecCommandRequest exec &&
                            exec.single_mode_exec_command_request_common is { command_type: 1 } training)
                        {
                            commandId = training.command_id;
                            var before = analyzer.Latest
                                ?? throw new InvalidOperationException("Training request has no preceding Festa state.");
                            Check(before.Turn == training.current_turn, "Training request turn differs from the preceding state.");
                            Check(before.Vital == training.current_vital, "Training request vitality differs from the preceding state.");
                            var prediction = before.Training.Single(candidate => candidate.CommandId == training.command_id);
                            Check(prediction.IsEnabled, "Requested training is disabled in the preceding state.");
                            pending = new(name, before, prediction);
                        }
                        continue;
                    }

                    responses++;
                    var response = dispatch.GetDto(endpoint.ResponseType);
                    if (pending is { } action)
                    {
                        var result = ((SingleModeFestaExecCommandResponse)response).data;
                        if (result is not { command_result: { } command, chara_info: { } after })
                            throw new InvalidOperationException("Training response lacks command_result or chara_info.");
                        Check(command.command_id == action.Prediction.CommandId,
                            "Training response command differs from the request.");
                        Check(after.single_mode_chara_id == action.Before.CharaId,
                            "Training response belongs to a different career than the prediction.");
                        if (command.result_state == 2)
                        {
                            var stats = new[] { after.speed, after.stamina, after.power, after.guts, after.wiz };
                            for (var i = 0; i < stats.Length; i++)
                            {
                                var actual = FestaProjection.Revise(stats[i]) - action.Before.Stats[i];
                                Check(actual == action.Prediction.Gains[i],
                                    $"Stat[{i}] gain: predicted {action.Prediction.Gains[i]}, actual {actual}.");
                            }
                            var pt = after.skill_point - action.Before.SkillPoints;
                            Check(pt == action.Prediction.PtGain,
                                $"Skill-point gain: predicted {action.Prediction.PtGain}, actual {pt}.");
                            Check(after.vital == action.Prediction.VitalAfter,
                                $"Vitality after training: predicted {action.Prediction.VitalAfter}, actual {after.vital}.");
                            successfulTraining++;
                            if (action.Before.CampRemaining > 0) campTraining++;
                            if (action.Before.Stats.Any(stat => stat > 1200)) revisedTraining++;
                            commands[action.Prediction.CommandId] = commands.GetValueOrDefault(action.Prediction.CommandId) + 1;
                        }
                        else
                            failedTraining++;
                        pending = null;
                    }

                    if (!registrations.TryGetValue(endpoint.Path, out var registration))
                        continue;
                    var isFusion = response is SingleModeFestaFusionAdviceResponse;
                    var previous = analyzer.Latest;
                    var previousFrame = isFusion ? ui.CaptureScreen() : null;
                    await registration.Invoke(dispatch);
                    ui.Flush();
                    dispatched++;
                    if (isFusion)
                    {
                        Check(ReferenceEquals(previous, analyzer.Latest), "Partial fusion response changed the latest Festa state.");
                        Check(previousFrame == ui.CaptureScreen(), "Partial fusion response changed the displayed training.");
                        fusionChecks++;
                    }
                    if (analyzer.Latest is { } current && !current.Training.IsEmpty &&
                        trainingFrames.Add(current.Training.Any(training => training.IsActTraining)))
                    {
                        _ = ui.CaptureScreen(120, 35);
                        var cards = ui.InvokeOnOwner(() => HistoryConfigDialog.Descendants(ui.Application.TopRunnableView!)
                            .Where(view => view is Terminal.Gui.Views.FrameView &&
                                view.Id?.ToString()?.StartsWith("festa-training:", StringComparison.Ordinal) == true)
                            .Select(view => (Id: view.Id!.ToString(), Bounds: view.FrameToScreen())).ToArray());
                        Check(current.Training.Length == 5 && current.Training.Select(training => training.Index).Distinct().Count() == 5,
                            "Training projection does not contain exactly one command per stat.");
                        Check(cards.Length == 5, "Training framebuffer does not contain exactly five cards.");
                        foreach (var training in current.Training)
                        {
                            var card = cards.Single(card => card.Id == "festa-training:" + training.CommandId);
                            Check(card.Bounds.Left >= 0 && card.Bounds.Right <= 95 && card.Bounds.Top is >= 0 and < 35,
                                $"Training card {training.CommandId} is outside the 120x35 viewport.");
                        }
                    }
                }
                catch (Exception error)
                {
                    throw new InvalidOperationException(
                        $"Capture replay failed: {name}; command={commandId?.ToString() ?? "n/a"}.", error);
                }
            }

            Check(pending is null, "Capture ends before the training response: " + pending?.FileName);
            Check(responses > 0 && dispatched > 0, "No registered Festa responses were replayed.");
            Check(successfulTraining > 0, "No successful training request/response pairs were verified.");
            Console.WriteLine($"Festa capture replay passed: requests={requests}, responses={responses}, dispatched={dispatched}, " +
                $"successful training={successfulTraining}, failed training={failedTraining}, camp={campTraining}, " +
                $"above-1200={revisedTraining}, fusion checks={fusionChecks}, five-column frames={trainingFrames.Count}.");
            Console.WriteLine("Verified training commands: " + string.Join(", ", commands.Select(pair => $"{pair.Key}={pair.Value}")));
        }
        finally
        {
            await plugin.DisposeAsync();
            ui.Flush();
        }
    }

    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    sealed record TrainingRequest(string FileName, FestaTurn Before, FestaTraining Prediction);
}
