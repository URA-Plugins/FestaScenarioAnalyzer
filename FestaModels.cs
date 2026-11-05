using System.Collections.Immutable;

namespace FestaScenarioAnalyzer;

/// <summary>
/// 单回合剧本投影快照。既是面板数据源，也是跨插件只读契约：
/// <c>SendGameStatusPlugin</c> 经 <see cref="FestaScenarioState.Snapshot"/> 消费，
/// 增删字段须两个插件成对发布。
/// </summary>
public sealed record FestaTurn(
    int CharaId, int Turn, int Vital, int MaxVital, int Motivation, int SkillPoints,
    ImmutableArray<int> Stats, ImmutableArray<int> MaxStats,
    int MaxActLevel, int CampRemaining, int AssistRemain, int SupportTypeCount,
    ImmutableArray<FestaAct> Acts, ImmutableArray<FestaAdvice> Advices,
    ImmutableArray<FestaTraining> Training, ImmutableArray<FestaCheckpointResult> Checkpoints,
    ImmutableArray<FestaGaugeCommand> OtherCommands,
    bool GaugeOverflow, bool LevelOverflow, ImmutableArray<int> NotLevelUpActs,
    bool RepeatTurn,
    ImmutableArray<FestaMember> Members,
    bool FatigueRecovery, bool AssistSupportActive);

public sealed record FestaAct(int Type, int Level, int Gauge, int NeedGauge, int AdviceId, int FusionId, int OutingLevel);

public sealed record FestaAdvice(int Id, int FusionId);

/// <summary>担当名单条目；<c>TargetId</c> 与训练参与者同一编号空间（卡位 1 基）。</summary>
public sealed record FestaMember(int TargetId, int CharaId, int ActType);

public sealed record FestaTraining(
    int Index, int CommandId, int Level, bool IsEnabled, int FailureRate,
    ImmutableArray<int> Gains, int PtGain, int VitalAfter,
    ImmutableArray<FestaPartner> Partners, ImmutableArray<FestaGaugeGain> Gauges, bool UpperTraining)
{
    /// <summary>是否为演目稽古（5101–5105）。</summary>
    public bool IsActTraining => CommandId is >= 5101 and <= 5105;
}

public sealed record FestaPartner(
    int TargetId, int CharaId, int ActType, string Name, int Friendship,
    bool IsSupportCard, bool IsHint, bool IsFriend, bool Shining);

public sealed record FestaCheckpointResult(int Id, int ResultType);

public sealed record FestaGaugeGain(int ActType, int Gain);

public sealed record FestaGaugeCommand(int CommandType, ImmutableArray<FestaGaugeGain> Gauges);
