using Gallop;
using UmamusumeResponseAnalyzer.Entities;

internal sealed class Fixture(int charaId = 11, int turn = 25, int marker = 321)
{
    internal static readonly int[] BaseTrainIds = [101, 105, 102, 103, 106];
    internal static readonly BaseName[] Names =
    [
        new BaseName(1001, "スペシャルウィーク長い名前の表示確認", "特别"),
        new BaseName(1002, "サイレンススズカ", "铃鹿"),
        new BaseName(1003, "トウカイテイオー", "帝王"),
        new BaseName(1004, "メジロマックイーン", "麦昆"),
        new BaseName(1005, "シンボリルドルフ", "皇帝"),
        new BaseName(9001, "吾妻たづな", "吾妻"),
        new BaseName(102, "理事長", "理事长"),
        .. BaseTrainIds.Select((id, index) => new SupportCardName(91001 + index, "試験",
            index == 0 ? "スペシャルウィーク長い名前の表示確認" : "试验", id, 1001 + index)),
        new SupportCardName(91006, "試験友人", "友人", 0, 9001),
        new SupportCardName(30081, "祖にして導く者", "祖群", 0, 9001),
    ];

    internal SingleModeChara Chara { get; } = new()
    {
        single_mode_chara_id = charaId, turn = turn, scenario_id = 15, playing_state = 1,
        speed = 1190, stamina = 1200, power = 1201, guts = 100, wiz = 100,
        max_speed = 2000, max_stamina = 2000, max_power = 2000, max_guts = 2000, max_wiz = 2000,
        vital = 45, max_vital = 100, motivation = 4, skill_point = marker,
        support_card_array = [.. Enumerable.Range(1, 6).Select(id => new SingleModeSupportCard { position = id, support_card_id = 91000 + id })],
        evaluation_info_array = [.. Enumerable.Range(1, 6).Select(id => new EvaluationInfo { target_id = id, evaluation = id == 2 ? 79 : 80 })],
        training_level_info_array = [.. BaseTrainIds.Select(id => new TrainingLevelInfo { command_id = id, level = 3 })],
        skill_array = [], skill_tips_array = [], chara_effect_id_array = [],
    };

    internal SingleModeHomeInfo Home { get; } = new()
    {
        command_info_array = [.. BaseTrainIds.Select((id, index) => new SingleModeCommandInfo
        {
            command_type = 1, command_id = id, is_enable = 1, failure_rate = index * 5,
            training_partner_array = index == 0 ? [1, 6] : [1, index + 1],
            tips_event_partner_array = index == 0 ? [1] : [], sub_command_partner_array = [],
            params_inc_dec_info_array = [Param(1, 10), Param(2, 10), Param(3, 10), Param(4, 10), Param(5, 10), Param(30, 12), Param(10, index == 4 ? 5 : -15)],
        })],
    };

    internal SingleModeFestaDataSet Data { get; } = new()
    {
        festa_info = new() { max_act_level = 6, summer_camp_remain_turn_num = 0 },
        act_info_array = [.. Enumerable.Range(1, 5).Select(type => new SingleModeFestaActInfo
        {
            act_type = type, level = type, act_gauge = type * 11, need_act_gauge = 100 + type,
            friend_outing_gain_act_level = type == 1 ? 1 : 0,
        })],
        advice_info_array = [],
        evaluation_info_array = [.. Enumerable.Range(1, 6).Select(id => new SingleModeFestaEvaluationInfo { target_id = id, chara_id = id == 6 ? 9001 : 1000 + id, act_type = (id - 1) % 5 + 1 })],
        command_info_array = [.. BaseTrainIds.Select((id, index) => new SingleModeFestaCommandInfo
        {
            command_type = 1, command_id = id,
            params_inc_dec_info_array = [Param(1, 11), Param(2, 11), Param(3, 11), Param(4, 11), Param(5, 11), Param(30, 8), Param(10, index == 4 ? 30 : 9)],
            predict_gain_act_gauge_array = [new() { act_type = index + 1, gain_act_gauge = 30 }],
        })],
        check_point_result_array = [],
        command_gain_act_gauge_array = [new() { command_type = 3, predict_gain_act_gauge_array = [new() { act_type = 2, gain_act_gauge = 20 }] }],
        not_up_parameter_info = new() { not_level_up_act_type_array = [2] },
    };

    internal SingleModeFestaLoadResponse Load() => new()
    {
        data = new()
        {
            single_mode_load_common = new() { chara_info = Chara, home_info = Home, unchecked_event_array = [] },
            festa_data_set = Data,
        },
    };

    internal SingleModeFestaExecCommandResponse Exec() => new()
    {
        data = new() { chara_info = Chara, home_info = Home, festa_data_set = Data, unchecked_event_array = [] },
    };

    internal SingleModeFestaAttachAdviceResponse Attach() => new()
    {
        data = new() { chara_info = Chara, home_info = Home, festa_data_set = Data, unchecked_event_array = [] },
    };

    internal static SingleModeParamsIncDecInfo Param(int target, int value) => new() { target_type = target, value = value };
}
