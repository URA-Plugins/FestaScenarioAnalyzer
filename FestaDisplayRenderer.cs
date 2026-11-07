using System.Drawing;
using System.Collections.Immutable;
using System.Globalization;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using UmamusumeResponseAnalyzer.TerminalGui;
using TAttribute = Terminal.Gui.Drawing.Attribute;
using TColor = Terminal.Gui.Drawing.Color;
using UiColor = FestaScenarioAnalyzer.FestaDisplayColor;

namespace FestaScenarioAnalyzer;

internal static class FestaDisplayRenderer
{
    public static WorkspaceContent Render(FestaTurn turn, ImmutableArray<FestaDisplayLine> extra)
        => new(() => new Dashboard(turn, [.. AdviceRows(turn), .. extra]));

    // 右侧面板的教诲分配：每种训练一行，形如「速度 牵绊→特化」；尚未转成时只列教诲。
    // 行首用基础训练的简称（速/耐/力/根/智所在训练的二字名），不用演目名，便于和训练列对上。
    static IEnumerable<FestaDisplayLine> AdviceRows(FestaTurn turn)
    {
        if (turn.Acts.IsEmpty)
            yield break;
        yield return new([new FestaDisplaySegment(FestaText.Get("AdviceHeader"), FestaDisplayColor.Cyan)]);
        foreach (var act in turn.Acts.OrderBy(act => act.Type))
        {
            List<FestaDisplaySegment> segments =
            [
                new(FestaText.Get("Stat" + (act.Type - 1)), FestaDisplayColor.Cyan), new(" ")
            ];
            if (act.AdviceId == 0 || !FestaMasterData.TryAdviceShortName(act.AdviceId, out var advice))
                segments.Add(new(FestaText.Get("AdviceNone")));
            else
            {
                segments.Add(new(advice));
                if (act.FusionId != 0 && FestaMasterData.TryAdviceShortName(act.FusionId, out var fusion))
                {
                    segments.Add(new(FestaText.Get("FusionArrow")));
                    segments.Add(new(fusion, FestaDisplayColor.Green));
                }
            }
            yield return new([.. segments]);
        }
    }

    internal static bool TryScroll(View view, Command command)
        => view is Dashboard dashboard && dashboard.Scroll(command);

    sealed class Dashboard : View
    {
        const int CardWidth = 19;
        const int MainWidth = CardWidth * 5;
        const int InnerWidth = CardWidth - 3;
        const int AssistAdvice = 401;
        readonly Palette palette;
        readonly UiLine[] extraRows;
        readonly FrameView? extras;
        readonly int mainHeight;
        int extraWidth = 24;

        public Dashboard(FestaTurn turn, ImmutableArray<FestaDisplayLine> extra)
        {
            Id = "festa-root";
            Width = Dim.Fill();
            Height = Dim.Fill();
            CanFocus = true;
            TabStop = TabBehavior.TabGroup;
            ViewportSettings = ViewportSettingsFlags.HasScrollBars;
            palette = new(GetAttributeForRole(VisualRole.Normal));
            SetScheme(palette.Base);
            extraRows = extra.IsDefaultOrEmpty ? [] : [.. extra.Select(line => new UiLine(
                line.Segments.Select(segment => new TextRun(segment.Text, segment.Color)).ToArray()))];
            if (extraRows.Length > 0)
            {
                extras = CreateFrame("festa-extra", MainWidth, 0, extraWidth, 3, extraRows);
                Add(extras);
            }
            AddCommand(Command.PageUp, () => Scroll(Command.PageUp));
            AddCommand(Command.PageDown, () => Scroll(Command.PageDown));
            AddCommand(Command.Start, () => Scroll(Command.Start));
            AddCommand(Command.End, () => Scroll(Command.End));
            KeyBindings.ReplaceCommands(Key.PageUp, Command.PageUp);
            KeyBindings.ReplaceCommands(Key.PageDown, Command.PageDown);
            KeyBindings.ReplaceCommands(Key.Home, Command.Start);
            KeyBindings.ReplaceCommands(Key.End, Command.End);
            AddHeaders(turn);

            var y = 3;
            var important = ImportantRows(turn);
            if (important.Count != 0)
            {
                var height = RowsHeight(important, MainWidth - 4) + 2;
                Add(CreateFrame("festa-important", 0, y, MainWidth, height, important));
                y += height;
            }
            if (turn.Training.IsEmpty)
            {
                Add(CreateFrame("festa-no-training", 0, y, MainWidth, 3, [new(FestaText.Get("NoTraining"))]));
                mainHeight = y + 3;
                SetContentSize(new(MainWidth, mainHeight));
                return;
            }
            var maxGain = turn.Training.Where(training => training.IsEnabled)
                .Select(training => training.Gains.Sum()).DefaultIfEmpty().Max();
            var cards = turn.Training.Select(training => CreateCard(turn, training, maxGain)).ToArray();
            // Keep section separators aligned across all five cards.
            var heights = Enumerable.Range(0, 3)
                .Select(group => cards.Max(card => RowsHeight(card.Groups[group], InnerWidth))).ToArray();
            // 标记行五列共用：本回合任意一列有标记时整行保留，未标记的列留空以维持分组线对齐。
            var markRows = cards.Max(card => card.Marks.Count);
            var cardHeight = heights.Sum() + 7 + markRows;
            foreach (var card in cards)
            {
                var id = "festa-training:" + card.Training.CommandId;
                var frame = new FrameView
                {
                    Id = id, X = card.Training.Index * CardWidth, Y = y, Width = CardWidth, Height = cardHeight
                };
                frame.SetScheme(palette.Base);
                if (card.Training.Partners.Any(partner => partner.Shining))
                    frame.Border.GetOrCreateView().SetScheme(palette.Selected);
                frame.Add(CreateText([card.Title], Pos.Align(Alignment.Center), 0, card.Title.Text.GetColumns(), 1));
                var row = 1;
                if (card.Marks.Count > 0)
                {
                    var marks = CreateText(card.Marks, 1, row, InnerWidth, card.Marks.Count);
                    marks.Id = id + ":marks";
                    frame.Add(marks);
                }
                row += markRows;
                frame.Add(new Line { Id = id + ":separator:0", X = 1, Y = row++, Width = InnerWidth });
                var sections = new[] { "capacity", "cost", "gain" };
                for (var group = 0; group < card.Groups.Length; group++)
                {
                    var text = CreateText(card.Groups[group], 1, row, InnerWidth, heights[group]);
                    text.Id = id + ":" + sections[group];
                    frame.Add(text);
                    row += heights[group];
                    frame.Add(new Line { Id = id + ":separator:" + (group + 1), X = 1, Y = row++, Width = InnerWidth });
                }
                Add(frame);
            }
            mainHeight = y + cardHeight;
            SetContentSize(new(MainWidth, mainHeight));
        }

        void AddHeaders(FestaTurn turn)
        {
            var year = (turn.Turn - 1) / 24 + 1;
            var month = (turn.Turn - 1) % 24 / 2 + 1;
            var date = turn.Turn <= 72
                ? FestaText.Get("Date", year, month,
                    FestaText.Get(turn.Turn % 2 == 0 ? "SecondHalf" : "FirstHalf"), turn.Turn)
                : FestaText.Get("DateTurnOnly", turn.Turn);
            Add(CreateFrame("festa-date", 0, 0, 30, 3, [new(date)]));
            Add(CreateFrame("festa-total", 30, 0, 27, 3,
                [new(FestaText.Get("TotalStats", turn.Stats.Sum(), turn.SkillPoints), UiColor.Cyan)]));
            Add(CreateFrame("festa-vital", 57, 0, 24, 3,
                [new([
                    new(FestaText.Get("VitalLabel")), new(turn.Vital.ToString(), VitalColor(turn.Vital)),
                    new(FestaText.Get("VitalLimit", turn.MaxVital))
                ])]));
            Add(CreateFrame("festa-motivation", 81, 0, MainWidth - 81, 3,
                [new(FestaText.Get("Motivation" + turn.Motivation),
                    turn.Motivation == 5 ? UiColor.Green : turn.Motivation == 4 ? UiColor.Yellow : UiColor.Red)]));
        }

        static List<UiLine> ImportantRows(FestaTurn turn)
        {
            var rows = new List<UiLine>();
            if (turn.CampRemaining > 0)
                rows.Add(new(FestaText.Get("CampRemaining", turn.CampRemaining), UiColor.Cyan));
            if (turn.AssistRemain > 0)
                rows.Add(new(FestaText.Get("AssistRemain", turn.AssistRemain), UiColor.Yellow));
            if (turn.GaugeOverflow)
                rows.Add(new(FestaText.Get("GaugeOverflow"), UiColor.Yellow));
            if (turn.LevelOverflow)
                rows.Add(new(FestaText.Get("LevelOverflow"), UiColor.Yellow));
            if (!turn.NotLevelUpActs.IsEmpty)
                rows.Add(new(FestaText.Get("NotLevelUpActs",
                    string.Join(FestaText.Get("ListSeparator"), turn.NotLevelUpActs.Select(FestaMasterData.ActName))), UiColor.Yellow));
            return rows;
        }

        static TrainingCard CreateCard(FestaTurn turn, FestaTraining training, int maxGain)
        {
            var act = training.IsActTraining
                ? turn.Acts.FirstOrDefault(act => act.Type == training.Index + 1)
                : null;
            var title = new List<TextRun>
            {
                act is null
                    ? new(FestaText.Get("TrainingTitle", FestaText.Get("Stat" + training.Index)))
                    : new(FestaText.Get("ActTrainingTitle", FestaMasterData.ActShortName(act.Type), act.Level), UiColor.Cyan)
            };
            if (training.FailureRate > 0)
                title.Add(new(FestaText.Get("FailureRate", training.FailureRate), UiColor.Red));

            var remaining = turn.MaxStats[training.Index] - turn.Stats[training.Index];
            var capacity = new List<UiLine>
            {
                new(FestaText.Get("CurrentRemainStat")),
                new([
                    new(FestaText.Get("CurrentStatValue", turn.Stats[training.Index])),
                    new(remaining.ToString(), remaining <= 200 ? UiColor.Red : remaining <= 400 ? UiColor.Yellow : UiColor.Normal)
                ])
            };
            var cost = new List<UiLine>
            {
                new([
                    new(FestaText.Get("VitalLabel")), new(training.VitalAfter.ToString(), VitalColor(training.VitalAfter)),
                    new(FestaText.Get("VitalLimit", turn.MaxVital))
                ]),
                new(FestaText.Get("TrainingLevel", training.Level, training.Gauges.Sum(gauge => gauge.Gain)))
            };

            var gains = new List<UiLine>
            {
                new([
                    new(FestaText.Get("TrainingGainLabel")),
                    new(training.Gains.Sum().ToString(), training.IsEnabled && training.Gains.Sum() == maxGain ? UiColor.Cyan : UiColor.Normal),
                    new(FestaText.Get("TrainingPt", training.PtGain))
                ])
            };
            foreach (var partner in training.Partners)
            {
                var segments = new List<TextRun>();
                if (partner.IsHint)
                    segments.Add(new("!", UiColor.Red));
                var friendship = partner.IsSupportCard && partner.Friendship < 100
                    ? FestaText.Get("PartnerFriendship", partner.Friendship) : string.Empty;
                var nameWidth = InnerWidth - friendship.GetColumns() - (partner.IsHint ? 1 : 0);
                var name = partner.Name.GetColumns() <= nameWidth ? partner.Name
                    : TextFormatter.ClipOrPad(partner.Name, nameWidth - "…".GetColumns()).TrimEnd() + "…";
                segments.Add(new(name,
                    partner.IsFriend ? UiColor.Green : partner.Shining ? UiColor.Cyan : UiColor.Normal));
                if (friendship.Length > 0)
                    segments.Add(new(friendship, partner.Friendship < 80 ? UiColor.Red : UiColor.Yellow));
                gains.Add(new(segments));
            }
            for (var i = training.Partners.Length; i < 5; i++)
                gains.Add(new(string.Empty));
            var marks = new List<TextRun>();
            if (training.IsActTraining && CanTriggerAssist(turn, training))
                marks.Add(new(FestaText.Get("AssistMark"), UiColor.Yellow));
            if (training.UpperTraining)
                marks.Add(new(FestaText.Get("KensanMark"), UiColor.Purple));
            return new(training, new(title), MarkRows(marks, InnerWidth), [[.. capacity], [.. cost], [.. gains]]);
        }

        // 助力教诲的固定转成效果同样给出「助力支撑」，两种来源都算绑定。
        static bool CanTriggerAssist(FestaTurn turn, FestaTraining training)
        {
            var acts = turn.Acts.Where(act => act.AdviceId == AssistAdvice || act.FusionId == AssistAdvice)
                .Select(act => act.Type).ToHashSet();
            return acts.Count > 0 && training.Partners.Any(partner => acts.Contains(partner.ActType));
        }

        // 两个标记优先并排；卡内宽度容不下时拆成两行，宁可多占一行也不把名称从中间折开。
        static IReadOnlyList<UiLine> MarkRows(IReadOnlyList<TextRun> marks, int width)
        {
            if (marks.Count == 0)
                return [];
            var columns = marks.Sum(mark => mark.Text.GetColumns()) + marks.Count - 1;
            if (columns <= width)
            {
                var runs = new List<TextRun> { marks[0] };
                for (var i = 1; i < marks.Count; i++)
                    runs.AddRange([new TextRun(" "), marks[i]]);
                return [new(runs)];
            }
            return [.. marks.Select(mark => new UiLine([mark]))];
        }

        protected override void OnSubViewLayout(LayoutEventArgs args)
        {
            if (extras is not null)
            {
                var width = Math.Max(Frame.Width, MainWidth + 24);
                if (extraWidth != width - MainWidth)
                {
                    extraWidth = width - MainWidth;
                    extras.Width = extraWidth;
                    var text = extras.SubViews.OfType<StyledTextView>().Single();
                    text.Width = extraWidth - 4;
                    text.SetRows(extraRows, extraWidth - 4);
                }
                var visibleHeight = Math.Max(1, Frame.Height - (width > Frame.Width ? 1 : 0));
                var height = Math.Max(visibleHeight, Math.Max(mainHeight, RowsHeight(extraRows, extraWidth - 4) + 2));
                var contentSize = new Size(width, height);
                if (GetContentSize() != contentSize)
                    SetContentSize(contentSize);
                extras.Height = height;
            }
            var size = GetContentSize();
            var maxX = Math.Max(0, size.Width - Math.Max(1, Viewport.Width));
            var maxY = Math.Max(0, size.Height - Math.Max(1, Viewport.Height));
            if (Viewport.X > maxX || Viewport.Y > maxY)
                Viewport = new(Math.Min(Viewport.X, maxX), Math.Min(Viewport.Y, maxY), Viewport.Width, Viewport.Height);
            base.OnSubViewLayout(args);
        }

        internal bool Scroll(Command command)
        {
            var amount = command switch
            {
                Command.PageUp => -Math.Max(1, Viewport.Height),
                Command.PageDown => Math.Max(1, Viewport.Height),
                Command.Start => -GetContentSize().Height,
                Command.End => GetContentSize().Height,
                _ => 0
            };
            if (amount == 0)
                return false;
            ScrollVertical(amount);
            return true;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            var horizontal = mouse.Flags.HasFlag(MouseFlags.WheeledRight) ? 1
                : mouse.Flags.HasFlag(MouseFlags.WheeledLeft) ? -1 : 0;
            if (horizontal != 0)
            {
                ScrollHorizontal(horizontal);
                return true;
            }
            var delta = mouse.Flags.HasFlag(MouseFlags.WheeledDown) ? 1
                : mouse.Flags.HasFlag(MouseFlags.WheeledUp) ? -1 : 0;
            if (delta == 0)
                return base.OnMouseEvent(mouse);
            ScrollVertical(delta);
            return true;
        }

        FrameView CreateFrame(string id, int x, int y, int width, int height, IReadOnlyList<UiLine> rows)
        {
            var frame = new FrameView { Id = id, X = x, Y = y, Width = width, Height = height };
            frame.SetScheme(palette.Base);
            frame.Add(CreateText(rows, 1, 0, width - 4, Dim.Fill()));
            return frame;
        }

        StyledTextView CreateText(IReadOnlyList<UiLine> rows, Pos x, Pos y, int width, Dim height)
        {
            var text = new StyledTextView(palette)
            {
                X = x, Y = y, Width = width, Height = height,
                ReadOnly = true, CanFocus = false, WordWrap = false
            };
            text.SetRows(rows, width);
            return text;
        }
    }

    sealed record TrainingCard(FestaTraining Training, UiLine Title, IReadOnlyList<UiLine> Marks, UiLine[][] Groups);

    static int RowsHeight(IReadOnlyList<UiLine> rows, int width)
        => WrapRows(rows, width).Count();

    // Wrap before loading to preserve colors; keep numbers and ASCII words together.
    static IEnumerable<UiLine> WrapRows(IReadOnlyList<UiLine> rows, int width)
    {
        foreach (var row in rows)
        {
            var columns = 0;
            var runs = new List<TextRun>();
            foreach (var run in row.Runs)
            {
                var starts = StringInfo.ParseCombiningCharacters(run.Text);
                for (var i = 0; i < starts.Length; i++)
                {
                    var start = starts[i];
                    var end = i + 1 < starts.Length ? starts[i + 1] : run.Text.Length;
                    if (run.Text[start] is >= '!' and <= '~')
                        while (i + 1 < starts.Length && end - start < width && run.Text[end] is >= '!' and <= '~')
                        {
                            i++;
                            end = i + 1 < starts.Length ? starts[i + 1] : run.Text.Length;
                        }
                    var text = run.Text[start..end];
                    var length = text.GetColumns();
                    if (columns + length > width && runs.Count > 0)
                    {
                        yield return new(runs);
                        runs = [];
                        columns = 0;
                    }
                    if (runs.Count > 0 && runs[^1].Color == run.Color)
                        runs[^1] = runs[^1] with { Text = runs[^1].Text + text };
                    else
                        runs.Add(new(text, run.Color));
                    columns += length;
                }
            }
            yield return new(runs);
        }
    }

    static UiColor VitalColor(int vital) => vital switch
    {
        < 30 => UiColor.Red,
        < 50 => UiColor.Orange,
        < 70 => UiColor.Yellow,
        _ => UiColor.Green
    };

    sealed record TextRun(string Text, UiColor Color = UiColor.Normal);
    sealed record UiLine(IReadOnlyList<TextRun> Runs)
    {
        public UiLine(string text, UiColor color = UiColor.Normal) : this([new TextRun(text, color)]) { }
        public string Text => string.Concat(Runs.Select(run => run.Text));
    }


    sealed class StyledTextView : TextView
    {
        readonly Palette palette;

        public StyledTextView(Palette palette)
        {
            this.palette = palette;
            SetScheme(palette.Base);
        }

        public void SetRows(IReadOnlyList<UiLine> rows, int width)
            => Load([.. WrapRows(rows, width).Select(row => row.Runs.SelectMany(run =>
                Cell.ToCellList(run.Text, palette.Attribute(run.Color))).ToList())]);

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed) || mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                SuperView!.SetFocus();
                return true;
            }
            return false;
        }

        protected override void OnDrawNormalColor(List<Cell> line, int idxCol, int idxRow)
            => SetAttribute(line[idxCol].Attribute ?? palette.Normal);
        protected override void OnDrawReadOnlyColor(List<Cell> line, int idxCol, int idxRow)
            => SetAttribute(line[idxCol].Attribute ?? palette.Normal);
        protected override void OnDrawUsedColor(List<Cell> line, int idxCol, int idxRow)
            => SetAttribute(line[idxCol].Attribute ?? palette.Normal);
    }

    sealed class Palette
    {
        public Palette(TAttribute normal)
        {
            Normal = new(normal.Foreground, TColor.Black, normal.Style);
            Base = new() { Normal = Normal, ReadOnly = Normal, Focus = Normal };
            var selected = Attribute(UiColor.Green);
            Selected = new() { Normal = selected, ReadOnly = selected, Focus = selected };
        }

        public TAttribute Normal { get; }
        public Scheme Base { get; }
        public Scheme Selected { get; }

        public TAttribute Attribute(UiColor color) => new(color switch
        {
            UiColor.Cyan => new TColor(StandardColor.BrightCyan),
            UiColor.Green => new TColor(StandardColor.BrightGreen),
            UiColor.Yellow => new TColor(StandardColor.BrightYellow),
            UiColor.Orange => new TColor(255, 140, 0),
            UiColor.Red => new TColor(StandardColor.BrightRed),
            UiColor.Purple => new TColor(190, 120, 255),
            _ => Normal.Foreground
        }, TColor.Black, Normal.Style);
    }
}
