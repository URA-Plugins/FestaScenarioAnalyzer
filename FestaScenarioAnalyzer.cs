using Gallop;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;

namespace FestaScenarioAnalyzer;

public sealed class FestaScenarioAnalyzer : IPlugin
{
    internal const string WorkspaceTitle = "FestaScenarioAnalyzer";
    readonly object gate = new();
    readonly FestaDisplayHistory history = new(WorkspaceTitle, "training", "Festa");
    readonly Dictionary<FestaDisplayHistory.Key, FestaTurn> snapshots = [];
    Workspace? workspace;
    bool active;
    int currentCharaId;
    internal FestaTurn? Latest { get; private set; }

    public void Initialize(IPluginContext context)
    {
        history.Initialize(context.Application, key => Show(key, false), key =>
        {
            lock (gate) snapshots.Remove(key);
            FestaTrainingDisplay.Remove(this, new(key.SingleModeCharaId, key.Turn));
        });
        active = true;
        Register<SingleModeFestaLoadResponse>(context, "load", r =>
            r.data?.single_mode_load_common is { } d
                ? new(d.chara_info, d.home_info, r.data.festa_data_set, d.unchecked_event_array, d.race_start_info is not null) : null);
        Register<SingleModeFestaStartResponse>(context, "start", r =>
            r.data?.single_mode_start_common is { } d
                ? new(d.chara_info, d.home_info, r.data.festa_data_set, d.unchecked_event_array) : null);
        Register<SingleModeFestaExecCommandResponse>(context, "exec_command", r => r.data is { } d
            ? new(d.chara_info, d.home_info, d.festa_data_set, d.unchecked_event_array) : null);
        Register<SingleModeFestaCheckEventResponse>(context, "check_event", r => r.data is { } d
            ? new(d.chara_info, d.home_info, d.festa_data_set, d.unchecked_event_array, d.race_start_info is not null) : null);
        Register<SingleModeFestaAttachAdviceResponse>(context, "attach_advice", r => r.data is { } d
            ? new(d.chara_info, d.home_info, d.festa_data_set, d.unchecked_event_array) : null);
        Register<SingleModeFestaCheckPointResponse>(context, "check_point", r => r.data is { } d
            ? new(d.chara_info, d.home_info, d.festa_data_set, d.unchecked_event_array) : null);
        Register<SingleModeFestaContinueResponse>(context, "continue", r => r.data is { } d
            ? new(d.chara_info, d.home_info, d.festa_data_set, d.unchecked_event_array, d.race_start_info is not null) : null);
        Register<SingleModeFestaFestaLiveResponse>(context, "festa_live", r => r.data is { } d
            ? new(d.chara_info, d.home_info, d.festa_data_set, d.unchecked_event_array) : null);
        Register<SingleModeFestaGainSkillsResponse>(context, "gain_skills", r => r.data is { } d
            ? new(d.chara_info, d.home_info, d.festa_data_set, null) : null);
        Register<SingleModeFestaRaceEndResponse>(context, "race_end", r => r.data is { } d
            ? new(d.chara_info, d.home_info, d.festa_data_set, null) : null);
        Register<SingleModeFestaRaceEntryResponse>(context, "race_entry", r => r.data is { } d
            ? new(d.chara_info, d.home_info, d.festa_data_set, d.unchecked_event_array, d.race_start_info is not null) : null);
        Register<SingleModeFestaRaceOutResponse>(context, "race_out", r => r.data is { } d
            ? new(d.chara_info, d.home_info, d.festa_data_set, d.unchecked_event_array) : null);
        Register<SingleModeFestaChangeShortCutResponse>(context, "change_short_cut", r => r.data is { } d
            ? new(d.chara_info, null, d.festa_data_set, d.unchecked_event_array) : null);
        Register<SingleModeFestaFinishClawCraneResponse>(context, "finish_claw_crane", r => r.data is { } d
            ? new(d.chara_info, null, d.festa_data_set, d.unchecked_event_array) : null);

        // 转成响应没有育成身份与训练信息，等待下一份完整响应，避免拼接旧预测。
        context.Analyzers.Register<SingleModeFestaFusionAdviceResponse>(AnalyzerKind.Response,
            [EndpointPattern.Exact("/umamusume/single_mode_festa/fusion_advice")],
            _ => ValueTask.CompletedTask, priority: 1);
    }

    void Register<T>(IPluginContext context, string endpoint, Func<T, Response?> project)
        => context.Analyzers.Register<T>(AnalyzerKind.Response,
            [EndpointPattern.Exact("/umamusume/single_mode_festa/" + endpoint)],
            invocation => Apply(project(invocation.Payload)), priority: 1);

    ValueTask Apply(Response? response)
    {
        if (response?.Chara is not { } chara)
            return ValueTask.CompletedTask;
        Workspace target;
        FestaDisplayHistory.Key key;
        lock (gate)
        {
            if (!active)
                return ValueTask.CompletedTask;
            if (chara.scenario_id != 15)
                throw new InvalidDataException(FestaText.Error("WrongScenario", chara.scenario_id, chara.single_mode_chara_id));
            if (currentCharaId != chara.single_mode_chara_id)
            {
                currentCharaId = chara.single_mode_chara_id;
                Latest = null;
            }
            if (chara.state is 2 or 3 || response.Data is null)
                return ValueTask.CompletedTask;

            var snapshot = FestaProjection.Create(chara, response.Home, response.Data,
                chara.playing_state == 1 && response.Events is not { Length: > 0 } && !response.Racing);
            Latest = snapshot;
            if (snapshot.Training.IsEmpty)
                return ValueTask.CompletedTask;
            key = new FestaDisplayHistory.Key(snapshot.CharaId, snapshot.Turn);
            snapshots[key] = snapshot;
            target = workspace ??= Workspace.Create(WorkspaceTitle);
        }
        FestaTrainingDisplay.Retain(this, new(key.SingleModeCharaId, key.Turn), () =>
        {
            if (history.IsVisible(key))
                Show(key, false);
        });
        // 发布会等待 UI；历史按键回调同样需要 gate，因此不能持锁发布。
        if (history.ShouldShow(key))
            Show(key, true);
        else
            history.Track(target, key);
        return ValueTask.CompletedTask;
    }

    bool Show(FestaDisplayHistory.Key key, bool switchToWorkspace)
    {
        Workspace target;
        FestaTurn snapshot;
        lock (gate)
        {
            if (!active || workspace is null)
                return false;
            if (!snapshots.TryGetValue(key, out snapshot!))
                throw new InvalidOperationException(FestaText.Error("DisplayMissing", key.SingleModeCharaId, key.Turn));
            target = workspace;
        }
        history.Show(target, key, FestaDisplayRenderer.Render(snapshot,
            FestaTrainingDisplay.Capture(new(key.SingleModeCharaId, key.Turn))), switchToWorkspace);
        return true;
    }

    public Task ConfigPromptAsync(Terminal.Gui.App.IApplication application, CancellationToken cancellationToken = default)
        => history.ConfigPromptAsync(application, cancellationToken);

    public ValueTask DisposeAsync()
    {
        Workspace? target;
        lock (gate)
        {
            active = false;
            snapshots.Clear();
            Latest = null;
            currentCharaId = 0;
            target = workspace;
            workspace = null;
        }
        history.Stop();
        FestaTrainingDisplay.Clear(this);
        target?.RemovePanel("training");
        return ValueTask.CompletedTask;
    }

    sealed record Response(SingleModeChara Chara, SingleModeHomeInfo? Home, SingleModeFestaDataSet? Data,
        SingleModeEventInfo[]? Events, bool Racing = false);
}
