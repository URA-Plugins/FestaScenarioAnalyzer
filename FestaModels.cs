using System.Collections.Immutable;

namespace FestaScenarioAnalyzer;

internal sealed record FestaTurn(
    int CharaId, int Turn, int Vital, int MaxVital, int Motivation, int SkillPoints,
    ImmutableArray<int> Stats, ImmutableArray<int> MaxStats,
    int MaxActLevel, int CampRemaining, int AssistRemain, int SupportTypeCount,
    ImmutableArray<FestaAct> Acts, ImmutableArray<FestaAdvice> Advices,
    ImmutableArray<FestaTraining> Training, ImmutableArray<FestaCheckpointResult> Checkpoints,
    ImmutableArray<FestaGaugeCommand> OtherCommands,
    bool GaugeOverflow, bool LevelOverflow, ImmutableArray<int> NotLevelUpActs,
    bool RepeatTurn);

internal sealed record FestaAct(int Type, int Level, int Gauge, int NeedGauge, int AdviceId, int FusionId, int OutingLevel);
internal sealed record FestaAdvice(int Id, int FusionId);
internal sealed record FestaTraining(
    int Index, int CommandId, int Level, bool IsEnabled, int FailureRate,
    ImmutableArray<int> Gains, int PtGain, int VitalAfter,
    ImmutableArray<FestaPartner> Partners, ImmutableArray<FestaGaugeGain> Gauges, bool UpperTraining)
{
    internal bool IsActTraining => CommandId is >= 5101 and <= 5105;
}
internal sealed record FestaPartner(
    int TargetId, int CharaId, int ActType, string Name, int Friendship,
    bool IsSupportCard, bool IsHint, bool IsFriend, bool Shining);
internal sealed record FestaCheckpointResult(int Id, int ResultType);
internal sealed record FestaGaugeGain(int ActType, int Gain);
internal sealed record FestaGaugeCommand(int CommandType, ImmutableArray<FestaGaugeGain> Gauges);
