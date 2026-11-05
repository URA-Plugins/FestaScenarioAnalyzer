namespace FestaScenarioAnalyzer;

/// <summary>
/// 剧本 15 最近一帧投影的跨插件只读门面。分析器每次投影成功后写入、卸载时清除；
/// 消费方（如 <c>SendGameStatusPlugin</c>）通过 <see cref="Snapshot"/> 读取不可变快照，
/// 并须校验 <see cref="FestaTurn.CharaId"/> / <see cref="FestaTurn.Turn"/> 与当前帧一致。
/// </summary>
public static class FestaScenarioState
{
    static readonly object Gate = new();
    static FestaTurn? current;

    /// <summary>读取最近一帧投影；分析器未就绪或已卸载时返回 null。</summary>
    public static FestaTurn? Snapshot()
    {
        lock (Gate)
            return current;
    }

    internal static void Update(FestaTurn snapshot)
    {
        lock (Gate)
            current = snapshot;
    }

    internal static void Clear()
    {
        lock (Gate)
            current = null;
    }
}