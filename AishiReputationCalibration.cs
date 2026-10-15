using System.Text.Json;
using System.Text.Json.Serialization;
using Path = System.IO.Path;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Aishi;
public sealed class ReputationCalibrationConfig
{
    public bool Enabled { get; set; }
    public int MigrationVersion { get; set; } = 1;
    public bool Logs { get; set; }
}

public sealed class ReputationRewardValue
{
    public string QuestId { get; set; } = "";
    public string Phase { get; set; } = "Success";
    public string RewardId { get; set; } = "";
    public decimal Value { get; set; }

    [JsonIgnore]
    public string Key => QuestId + "/" + Phase + "/" + RewardId;
}

public sealed class ReputationCalibrationState
{
    public int Schema { get; set; } = 1;
    public Dictionary<string, decimal> PaidRewards { get; set; } = new(StringComparer.Ordinal);
    public HashSet<int> AppliedVersions { get; set; } = [];
}

public static class AishiReputationLedger
{
    public const string TraderId = "690766de550bc322a810ea1e";
    public const string Marker = "aishiReputationCalibration";
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    public static ReputationCalibrationState? Initialize(PmcData? pmc, IReadOnlyList<ReputationRewardValue> previous, IReadOnlyList<ReputationRewardValue> current)
    {
        if (pmc?.TradersInfo?.TryGetValue(TraderId, out var trader) != true || trader == null)
            return null;
        if (trader.ExtensionData.TryGetValue(Marker, out var stored))
        {
            var state = stored as ReputationCalibrationState ?? JsonSerializer.Deserialize<ReputationCalibrationState>(JsonSerializer.Serialize(stored), JsonOptions);
            if (state == null || state.Schema != 1 || state.PaidRewards == null || state.AppliedVersions == null)
                throw new InvalidOperationException("Unsupported Aishi reputation calibration state.");
            trader.ExtensionData[Marker] = state;
            return state;
        }

        if (trader.ExtensionData.ContainsKey("aishiReputationScale"))
            throw new InvalidOperationException("Aishi reputation still has the retired multiplier marker. Restore its known original standing before enabling reward-difference calibration.");
        var created = new ReputationCalibrationState();
        var old = previous.ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
        var statuses = (pmc.Quests ?? []).GroupBy(q => q.QId.ToString()).ToDictionary(g => g.Key, g => g.Last());
        foreach (var reward in current)
            if (statuses.TryGetValue(reward.QuestId, out var status) && WasPaid(reward.Phase, status))
                created.PaidRewards[reward.Key] = old.GetValueOrDefault(reward.Key, reward.Value);
        trader.ExtensionData[Marker] = created;
        return created;
    }

    private static bool WasPaid(string phase, QuestStatus quest) => phase switch
    {
        "Success" => quest.Status == QuestStatusEnum.Success,
        "Fail" => quest.Status is QuestStatusEnum.Fail or QuestStatusEnum.FailRestartable,
        "Started" => quest.StartTime > 0 || quest.StatusTimers?.ContainsKey(QuestStatusEnum.Started) == true || quest.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish or QuestStatusEnum.Success,
        _ => false
    };
    public static decimal Apply(PmcData pmc, ReputationCalibrationState state, IReadOnlyList<ReputationRewardValue> current, ReputationCalibrationConfig config)
    {
        if (!config.Enabled || state.AppliedVersions.Contains(config.MigrationVersion))
            return 0;
        if (config.MigrationVersion <= 0)
            throw new InvalidOperationException("Aishi calibration migrationVersion must be positive.");
        var trader = pmc.TradersInfo![TraderId];
        if (!trader.Standing.HasValue || !double.IsFinite(trader.Standing.Value))
            throw new InvalidOperationException("Aishi standing is missing or not finite; calibration was not applied.");
        decimal delta = 0;
        var updates = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var reward in current)
            if (state.PaidRewards.TryGetValue(reward.Key, out var paid))
            {
                delta = checked(delta + reward.Value - paid);
                updates[reward.Key] = reward.Value;
            }

        var result = checked((decimal)trader.Standing.Value + delta);
        trader.Standing = (double)result;
        foreach (var reward in updates)
            state.PaidRewards[reward.Key] = reward.Value;
        state.AppliedVersions.Add(config.MigrationVersion);
        return delta;
    }

    public static void RecordPaid(ReputationCalibrationState state, IReadOnlyList<ReputationRewardValue> current, MongoId quest, QuestStatusEnum phase)
    {
        var id = quest.ToString();
        var status = phase.ToString();
        foreach (var reward in current)
            if (reward.QuestId == id && reward.Phase == status)
                state.PaidRewards[reward.Key] = reward.Value;
    }
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1100)]
public sealed class AishiReputationCalibration(SaveServer saves, TraderHelper traders, TemplateTable templates, ILogger<AishiReputationCalibration> logger) : IOnLoad
{
    private static AishiReputationCalibration _instance = null!;
    private readonly SaveServer _saves = saves;
    private readonly TraderHelper _traders = traders;
    private readonly ILogger<AishiReputationCalibration> _logger = logger;
    private ReputationCalibrationConfig _config = new();
    private ReputationRewardValue[] _previous = [];
    private ReputationRewardValue[] _current = [];
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    public Task OnLoadAsync(CancellationToken cancellationToken = default)
    {
        var root = Path.GetDirectoryName(typeof(AishiReputationCalibration).Assembly.Location)!;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config.jsonc")), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        _config = doc.RootElement.GetProperty("reputationCalibration").Deserialize<ReputationCalibrationConfig>(AishiReputationLedger.JsonOptions)!;
        if (_config == null || _config.MigrationVersion <= 0)
            throw new InvalidDataException("Aishi reputationCalibration configuration is invalid.");
        _previous = JsonSerializer.Deserialize<ReputationRewardValue[]>(File.ReadAllText(Path.Combine(root, "db", "Reputation", "original-rewards.json")), AishiReputationLedger.JsonOptions)!;
        var quests = templates.Quests;
        _current = quests.SelectMany(q => (q.Value.Rewards ?? []).SelectMany(phase => phase.Value.Where(r => r.Type == RewardType.TraderStanding && r.Target == AishiReputationLedger.TraderId && r.Value.HasValue).Select(r => new ReputationRewardValue { QuestId = q.Key.ToString(), Phase = phase.Key.ToString(), RewardId = r.Id.ToString(), Value = checked((decimal)r.Value!.Value) }))).ToArray();
        _instance = this;
        var harmony = new Harmony("com.samc137.aishi.reputation-calibration");
        harmony.Patch(AccessTools.Method(typeof(GameController), nameof(GameController.GameStart)), prefix: new HarmonyMethod(typeof(AishiReputationCalibration), nameof(BeforeGameStart)));
        harmony.Patch(AccessTools.Method(typeof(QuestRewardHelper), nameof(QuestRewardHelper.ApplyQuestReward)), prefix: new HarmonyMethod(typeof(AishiReputationCalibration), nameof(BeforeReward)), postfix: new HarmonyMethod(typeof(AishiReputationCalibration), nameof(AfterReward)));
        return Task.CompletedTask;
    }

    private ReputationCalibrationState? Initialize(PmcData pmc)
    {
        try
        {
            return AishiReputationLedger.Initialize(pmc, _previous, _current);
        }
        catch (Exception error)
        {
            if (_reported.Add(pmc.Id.ToString() ?? "unknown"))
                _logger.LogError(error, "Aishi reputation calibration could not read profile state; standing was not changed.");
            return null;
        }
    }

    private static void BeforeGameStart(MongoId __1)
    {
        var self = _instance;
        if (__1.IsEmpty || !self._saves.ProfileExists(__1))
            return;
        var profile = self._saves.GetProfile(__1);
        if (profile.ProfileInfo?.IsWiped == true || profile.ProfileInfo?.InvalidOrUnloadableProfile == true)
            return;
        var pmc = profile.CharacterData?.PmcData;
        if (pmc == null)
            return;
        lock (pmc)
        {
            var state = self.Initialize(pmc);
            if (state == null)
                return;
            try
            {
                decimal delta = AishiReputationLedger.Apply(pmc, state, self._current, self._config);
                self._traders.LevelUp(AishiReputationLedger.TraderId, pmc);
                if (self._config.Logs && delta != 0)
                    self._logger.LogInformation("[Aishi reputation] Applied revision {Revision}: reward difference {Delta}; standing {Standing}.", self._config.MigrationVersion, delta, pmc.TradersInfo![AishiReputationLedger.TraderId].Standing);
            }
            catch (Exception error)
            {
                self._logger.LogError(error, "Aishi reputation calibration failed; revision was not completed.");
            }
        }
    }

    private static void BeforeReward(PmcData __0, out ReputationCalibrationState? __state)
    {
        lock (__0)
            __state = _instance.Initialize(__0);
    }

    private static void AfterReward(PmcData __0, MongoId __1, QuestStatusEnum __2, ReputationCalibrationState? __state)
    {
        if (__state == null)
            return;
        lock (__0)
            AishiReputationLedger.RecordPaid(__state, _instance._current, __1, __2);
    }
}
