using System.Collections.Immutable;

namespace FestaScenarioAnalyzer;

public readonly record struct FestaTrainingDisplayId(int SingleModeCharaId, int Turn);
public enum FestaDisplayColor { Normal, Cyan, Green, Yellow, Orange, Red }
public readonly record struct FestaDisplaySegment(string Text, FestaDisplayColor Color = FestaDisplayColor.Normal);
public sealed record FestaDisplayLine(ImmutableArray<FestaDisplaySegment> Segments);

public static class FestaTrainingDisplay
{
    static readonly object Gate = new();
    static readonly Dictionary<FestaTrainingDisplayId, DisplayUnit> Units = [];
    static readonly List<FestaTrainingDisplayPartProducer> Producers = [];

    public static FestaTrainingDisplayPartProducer RegisterPartProducer(string sourceTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTitle);
        if (sourceTitle.Contains('\r') || sourceTitle.Contains('\n'))
            throw new ArgumentException(FestaText.Error("InvalidPartTitle"), nameof(sourceTitle));
        var producer = new FestaTrainingDisplayPartProducer(sourceTitle);
        lock (Gate) Producers.Add(producer);
        return producer;
    }

    internal static void Retain(object owner, FestaTrainingDisplayId id, Action refresh)
    {
        lock (Gate)
        {
            if (!Units.TryGetValue(id, out var unit))
                Units.Add(id, unit = new(owner, refresh));
            foreach (var producer in Producers)
                if (producer.Pending is { } pending && pending.Id == id)
                {
                    unit.Parts[producer] = pending.Lines;
                    producer.Pending = null;
                }
        }
    }

    internal static ImmutableArray<FestaDisplayLine> Capture(FestaTrainingDisplayId id)
    {
        lock (Gate)
        {
            var unit = Units[id];
            var rows = ImmutableArray.CreateBuilder<FestaDisplayLine>();
            foreach (var producer in Producers)
                if (unit.Parts.TryGetValue(producer, out var lines) && !lines.IsEmpty)
                {
                    rows.Add(new([new(producer.SourceTitle, FestaDisplayColor.Cyan)]));
                    rows.AddRange(lines);
                }
            return rows.ToImmutable();
        }
    }

    internal static void Remove(object owner, FestaTrainingDisplayId id)
    {
        lock (Gate)
            if (Units.TryGetValue(id, out var unit) && ReferenceEquals(unit.Owner, owner))
                Units.Remove(id);
    }

    internal static void Clear(object owner)
    {
        lock (Gate)
        {
            foreach (var id in Units.Where(pair => ReferenceEquals(pair.Value.Owner, owner)).Select(pair => pair.Key).ToArray())
                Units.Remove(id);
            foreach (var producer in Producers)
                producer.Pending = null;
        }
    }

    internal static void Update(FestaTrainingDisplayPartProducer producer, FestaTrainingDisplayId id,
        IReadOnlyList<FestaDisplayLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var snapshot = lines.Where(line => line.Segments.Any(segment => !string.IsNullOrWhiteSpace(segment.Text))).ToImmutableArray();
        Action? refresh = null;
        lock (Gate)
        {
            ObjectDisposedException.ThrowIf(producer.IsDisposed, producer);
            if (Units.TryGetValue(id, out var unit))
            {
                unit.Parts[producer] = snapshot;
                refresh = unit.Refresh;
            }
            else
                // EventLogger 先于训练画面运行；只保留一份待挂接内容，不积累无训练画面的回合。
                producer.Pending = (id, snapshot);
        }
        refresh?.Invoke();
    }

    internal static void RemoveProducer(FestaTrainingDisplayPartProducer producer)
    {
        Action[] refresh;
        lock (Gate)
        {
            if (producer.IsDisposed)
                return;
            producer.IsDisposed = true;
            producer.Pending = null;
            Producers.Remove(producer);
            refresh = [.. Units.Values.Where(unit => unit.Parts.Remove(producer)).Select(unit => unit.Refresh)];
        }
        foreach (var callback in refresh)
            callback();
    }

    sealed class DisplayUnit(object owner, Action refresh)
    {
        internal object Owner { get; } = owner;
        internal Action Refresh { get; } = refresh;
        internal Dictionary<FestaTrainingDisplayPartProducer, ImmutableArray<FestaDisplayLine>> Parts { get; } = [];
    }
}

public sealed class FestaTrainingDisplayPartProducer : IDisposable
{
    internal FestaTrainingDisplayPartProducer(string sourceTitle) => SourceTitle = sourceTitle;
    internal string SourceTitle { get; }
    internal bool IsDisposed { get; set; }
    internal (FestaTrainingDisplayId Id, ImmutableArray<FestaDisplayLine> Lines)? Pending { get; set; }

    public void Update(FestaTrainingDisplayId id, IReadOnlyList<FestaDisplayLine> lines)
        => FestaTrainingDisplay.Update(this, id, lines);

    public void Dispose() => FestaTrainingDisplay.RemoveProducer(this);
}
