using System.Globalization;
using System.Resources;

namespace FestaScenarioAnalyzer;

internal sealed record FestaAdviceDefinition(int Id, int Group);
internal sealed record FestaSchedule(int Id, int Turn, int NeedLevel);

internal static partial class FestaMasterData
{
    static readonly ResourceManager Resources = new("FestaScenarioAnalyzer.i18n.Data", typeof(FestaMasterData).Assembly);

    public static string ActName(int type)
        => Text(ActNameKeys.TryGetValue(type, out var key)
            ? key
            : throw new ArgumentOutOfRangeException(nameof(type), type, Text("UnknownAct")));

    public static string AdviceName(int id)
        => Text(AdviceNameKeys.TryGetValue(id, out var key)
            ? key
            : throw new ArgumentOutOfRangeException(nameof(id), id, Text("UnknownAdvice")));

    // 官方演目名互不共享 2 字前缀（流镝马 / 神旗争夺战 / 纳舞 / 大曳神舆 / 飞悬），卡片标题与资源行取该前缀。
    public static string ActShortName(int type)
    {
        var name = ActName(type);
        var starts = StringInfo.ParseCombiningCharacters(name);
        return starts.Length <= 2 ? name : name[..starts[2]];
    }

    // 教诲名统一以「教诲」结尾；资源行去掉后缀，才能保证一种训练占一行。
    public static bool TryAdviceShortName(int id, out string name)
    {
        name = AdviceNameKeys.TryGetValue(id, out var key) ? Text(key) : string.Empty;
        if (name.EndsWith("教诲", StringComparison.Ordinal))
            name = name[..^2];
        return name.Length > 0;
    }

    public static IReadOnlyList<string> AdviceEffect(int id, int level)
    {
        ValidateLevel(level);
        if (!AdviceEffectIds.TryGetValue(id, out var ids))
            throw new ArgumentOutOfRangeException(nameof(id), id, Text("UnknownAdvice"));
        return Text($"AdviceEffect_{ids[level - 1]}").Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    public static IReadOnlyList<string> FusionEffect(int sourceId)
    {
        if (!FusionEffectIds.TryGetValue(sourceId, out var id))
            throw new ArgumentOutOfRangeException(nameof(sourceId), sourceId, Text("UnknownFusion"));
        return Text($"FusionEffect_{id}").Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    public static IReadOnlyList<string> ActEffects(int level, bool fiveSupportTypes)
    {
        ValidateLevel(level);
        return [.. ActEffectEntries
            .Where(x => x.Level == level && (!x.RequiresFiveTypes || fiveSupportTypes))
            .Select(x => Text($"ActEffect_{x.Id}"))];
    }

    static void ValidateLevel(int level)
    {
        if (level is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(level), level, Text("UnknownLevel"));
    }

    static string Text(string key)
        => Resources.GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new MissingManifestResourceException($"Festa data resource is missing: {key}");
}
