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
