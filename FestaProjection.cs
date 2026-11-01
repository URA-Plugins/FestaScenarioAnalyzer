using System.Collections.Immutable;
using Gallop;
using UmamusumeResponseAnalyzer;
using UmamusumeResponseAnalyzer.Entities;

namespace FestaScenarioAnalyzer;

internal static class FestaProjection
{
    internal static readonly int[] BaseTrainIds = [101, 105, 102, 103, 106];

    internal static int TrainIndex(int commandId) => commandId switch
    {
        101 or 601 or 5101 => 0,
        105 or 602 or 5102 => 1,
        102 or 603 or 5103 => 2,
        103 or 604 or 5104 => 3,
        106 or 605 or 5105 => 4,
        _ => -1
    };

    internal static int Revise(int value) => value > 1200 ? value * 2 - 1200 : value;

    internal static FestaTurn Create(SingleModeChara chara, SingleModeHomeInfo? home,
        SingleModeFestaDataSet data, bool canTrain)
    {
        var stats = new[] { chara.speed, chara.stamina, chara.power, chara.guts, chara.wiz };
        var maxStats = new[] { chara.max_speed, chara.max_stamina, chara.max_power, chara.max_guts, chara.max_wiz };
        var advices = (data.advice_info_array ?? []).ToDictionary(x => x.advice_id);
        var acts = data.act_info_array ?? [];
        if (acts.Select(x => x.act_type).Distinct().Count() != acts.Length ||
            acts.Any(x => x.act_type is < 1 or > 5 || x.level is < 0 or > 10 || x.act_gauge < 0 || x.need_act_gauge < 0))
            throw new InvalidDataException(FestaText.Error("InvalidActs", chara.single_mode_chara_id, chara.turn));
        if (acts.Length > 0 && data.festa_info is null)
            throw new InvalidDataException(FestaText.Error("MissingField", "festa_info"));
        foreach (var act in acts)
            if (act.advice_id != 0 && !advices.ContainsKey(act.advice_id))
                throw new InvalidDataException(FestaText.Error("MissingAdvice", chara.single_mode_chara_id, chara.turn, act.act_type, act.advice_id));

        var supportCards = (chara.support_card_array ?? []).ToDictionary(x => x.position);
        var supports = supportCards.Values.ToDictionary(x => x.position, x =>
            Database.Names.TryGetSupportCard(x.support_card_id, out var card) ? card : null);
        var supportTypeCount = supports.Values.Any(x => x is null)
            ? -1
            : supports.Values.Select(x => x!.Type).Distinct().Count();
        var members = (data.evaluation_info_array ?? []).ToDictionary(x => x.target_id);
        var friendships = (chara.evaluation_info_array ?? []).ToDictionary(x => x.target_id, x => x.evaluation);

        var training = ImmutableArray<FestaTraining>.Empty;
        if (canTrain && home?.command_info_array is { } homeCommands)
        {
            var groups = homeCommands.Where(x => x.command_type == 1 && TrainIndex(x.command_id) >= 0)
                .GroupBy(x => (Index: TrainIndex(x.command_id), IsAct: x.command_id is >= 5101 and <= 5105))
                .ToDictionary(x => x.Key, x => x.ToArray());
            if (groups.Keys.Select(x => x.Index).Distinct().Count() == 5)
            {
                var result = ImmutableArray.CreateBuilder<FestaTraining>(groups.Count);
                foreach (var (key, candidates) in groups.OrderBy(x => x.Key.IsAct).ThenBy(x => x.Key.Index))
                {
                    var index = key.Index;
                    var enabled = candidates.Where(x => x.is_enable == 1).ToArray();
                    var command = enabled.Length == 1 ? enabled[0]
                        : enabled.Length == 0 && candidates.Length == 1 ? candidates[0]
                        : throw new InvalidDataException(FestaText.Error("AmbiguousHome", chara.turn, BaseTrainIds[index]));
                    var scenarioCommands = (data.command_info_array ?? [])
                        .Where(x => x.command_type == 1 && x.command_id == command.command_id).ToArray();
                    if (scenarioCommands.Length > 1)
                        throw new InvalidDataException(FestaText.Error("AmbiguousScenario", chara.turn, BaseTrainIds[index]));
                    var scenario = scenarioCommands.SingleOrDefault();
                    var values = new int[31];
                    foreach (var value in command.params_inc_dec_info_array ?? [])
                        if (value.target_type is >= 1 and <= 5 or 10 or 30)
                            values[value.target_type] += value.value;

                    foreach (var value in scenario?.params_inc_dec_info_array ?? [])
                        if (value.target_type is >= 1 and <= 5 or 10 or 30)
                            values[value.target_type] += value.value;

                    var exactLevels = (chara.training_level_info_array ?? [])
                        .Where(x => x.command_id == command.command_id).ToArray();
                    var levels = exactLevels.Length > 0 || key.IsAct ? exactLevels
                        : (chara.training_level_info_array ?? []).Where(x => x.command_id == BaseTrainIds[index]).ToArray();
                    if (levels.Length != 1)
                        throw new InvalidDataException(FestaText.Error("TrainingLevel", chara.turn, command.command_id));
                    var partners = (command.training_partner_array ?? []).Select(targetId =>
                    {
                        members.TryGetValue(targetId, out var member);
                        supports.TryGetValue(targetId, out var support);
                        supportCards.TryGetValue(targetId, out var supportInfo);
                        var charaId = member?.chara_id ?? support?.CharaId ?? (targetId is >= 100 and < 1000 ? targetId : 0);
                        var name = supportInfo is not null ? Database.Names.DisplayNickname(supportInfo.support_card_id)
                            : charaId != 0 ? Database.Names.DisplayNickname(charaId)
                            : FestaText.Error("UnmappedPartner", targetId);
                        var friendship = friendships.GetValueOrDefault(targetId);
                        var groupEffect = supportInfo?.support_card_id switch
                        {
                            30137 => 102,
                            30067 => 101,
                            30081 => 100,
                            _ => 0
                        };
                        var shining = support is not null && friendship >= 80 && support.CanTriggerFriendshipTraining(BaseTrainIds[index])
                            || groupEffect != 0 && chara.chara_effect_id_array?.Contains(groupEffect) == true;
                        return new FestaPartner(targetId, charaId, member?.act_type ?? 0, name, friendship, supportInfo is not null,
                            command.tips_event_partner_array?.Contains(targetId) == true,
                            support?.IsFriendCard == true, shining);
                    }).OrderBy(x => supportCards.ContainsKey(x.TargetId) ? 0 : 1).ToImmutableArray();
                    result.Add(new(index, command.command_id, levels[0].level,
                        command.is_enable == 1, command.failure_rate,
                        [.. Enumerable.Range(0, 5).Select(i => Revise(stats[i] + values[i + 1]) - Revise(stats[i]))],
                        values[30], Math.Clamp(chara.vital + values[10], 0, chara.max_vital), partners,
                        [.. (scenario?.predict_gain_act_gauge_array ?? []).Select(x => new FestaGaugeGain(x.act_type, x.gain_act_gauge))]));
                }
                training = [.. result.GroupBy(item => item.Index).OrderBy(group => group.Key).Select(group =>
                    group.SingleOrDefault(item => item.IsActTraining && item.IsEnabled)
                    ?? group.SingleOrDefault(item => !item.IsActTraining)
                    ?? group.Single())];
            }
        }

        return new(chara.single_mode_chara_id, chara.turn, chara.vital, chara.max_vital, chara.motivation, chara.skill_point,
            [.. stats.Select(Revise)], [.. maxStats.Select(Revise)],
            data.festa_info?.max_act_level ?? 0, data.festa_info?.summer_camp_remain_turn_num ?? 0, supportTypeCount,
            [.. acts.OrderBy(x => x.act_type).Select(x => new FestaAct(x.act_type, x.level, x.act_gauge, x.need_act_gauge,
                x.advice_id, x.advice_id == 0 ? 0 : advices[x.advice_id].fusion_advice_id, x.friend_outing_gain_act_level))],
            [.. advices.Values.Select(x => new FestaAdvice(x.advice_id, x.fusion_advice_id))], training,
            [.. (data.check_point_result_array ?? []).Select(x => new FestaCheckpointResult(x.schedule_id, x.result_type))],
            [.. (data.command_gain_act_gauge_array ?? []).Select(x => new FestaGaugeCommand(x.command_type,
                [.. (x.predict_gain_act_gauge_array ?? []).Select(g => new FestaGaugeGain(g.act_type, g.gain_act_gauge))]))],
            data.not_up_parameter_info?.is_overflow_act_gauge_event_gain == true,
            data.not_up_parameter_info?.is_overflow_act_level_event_gain == true,
            [.. data.not_up_parameter_info?.not_level_up_act_type_array ?? []], chara.playing_state != 1);
    }
}
