using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
#if AISHI_SPT41
using SPTarkov.Server.Core.Helpers.Quest;
#else
using SPTarkov.Server.Core.Helpers;
#endif
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils.Cloners;

namespace Aishi;

[Injectable(TypePriority = int.MaxValue - 20)]
public sealed class AishiFastTravel(SaveServer saves, ICloner cloner, QuestHelper quests) : IOnLoad
{
    private static SaveServer _saves = null!;
    private static ICloner _cloner = null!;
    private static QuestHelper _quests = null!;
    private const string QuestId = "c3dd45d0da2679c60db3c3cb";
    private const string TraderId = "690766de550bc322a810ea1e";
    private const string LaborQuest = "690bcb580b8f9c461c3eae53";
    private const string DoorsQuest = "690d8ad1750d535b2e9bc135";
    private const string InterceptionQuest = "690e2689cbd10f2ac5e766eb";
    private const string DirectQuest = "690bb986148ac0cfb2669d32";
    private const string PaymentCondition = "f3ea573061656070b2292e20";
    private static readonly HashSet<string> Alternatives =
    [
        InterceptionQuest, LaborQuest,
        DoorsQuest, DirectQuest
    ];
    private static readonly HashSet<string> QuestIds =
    [
        QuestId, InterceptionQuest, LaborQuest,
        DoorsQuest, DirectQuest,
        "690a531eee4e5b9753c9e1f5", "690f6e1e9e966ad660423991", "69a9df0ea81c67206b147080"
    ];

#if AISHI_SPT41
    public Task OnLoadAsync(CancellationToken token)
#else
    public Task OnLoad()
#endif
    {
        _saves = saves;
        _cloner = cloner;
        _quests = quests;
        var harmony = new Harmony("com.samc137.aishi.fast-travel");
        harmony.Patch(AccessTools.Method(typeof(QuestHelper), nameof(QuestHelper.GetClientQuests)), prefix: new HarmonyMethod(typeof(AishiFastTravel), nameof(BeforeList)), postfix: new HarmonyMethod(typeof(AishiFastTravel), nameof(Filter)));
        harmony.Patch(AccessTools.Method(typeof(QuestHelper), "FailQuests"), prefix: new HarmonyMethod(typeof(AishiFastTravel), nameof(FailAlternatives)));
        harmony.Patch(AccessTools.Method(typeof(QuestHelper), nameof(QuestHelper.CompleteQuest)), prefix: new HarmonyMethod(typeof(AishiFastTravel), nameof(BeforeQuest)), postfix: new HarmonyMethod(typeof(AishiFastTravel), nameof(AfterQuest)));
        harmony.Patch(AccessTools.Method(typeof(QuestController), nameof(QuestController.AcceptQuest)), prefix: new HarmonyMethod(typeof(AishiFastTravel), nameof(BeforeQuest)), postfix: new HarmonyMethod(typeof(AishiFastTravel), nameof(AfterQuest)));
        harmony.Patch(AccessTools.Method(typeof(QuestHelper), nameof(QuestHelper.FailQuest)), prefix: new HarmonyMethod(typeof(AishiFastTravel), nameof(BeforeQuest)), postfix: new HarmonyMethod(typeof(AishiFastTravel), nameof(AfterFailure)));
        harmony.Patch(AccessTools.Method(typeof(GameController), nameof(GameController.GameStart)), prefix: new HarmonyMethod(typeof(AishiFastTravel), nameof(BeforeGameStart)));
        harmony.Patch(AccessTools.Method(typeof(ProfileController), nameof(ProfileController.GetCompleteProfile)), prefix: new HarmonyMethod(typeof(AishiFastTravel), nameof(BeforeList)));
        return Task.CompletedTask;
    }

    private static PmcData? Profile(MongoId session) => session.IsEmpty || !_saves.ProfileExists(session) ? null : _saves.GetProfile(session).CharacterData?.PmcData;

    private static void BeforeGameStart(MongoId __1) => Repair(Profile(__1));

    private static void BeforeList(MongoId __0) => Repair(Profile(__0));

    private static void Filter(MongoId __0, ref List<Quest> __result)
    {
        var pmc = Profile(__0);
        if (pmc?.Quests?.Any(q => q.QId.ToString() == QuestId && q.Status == QuestStatusEnum.Success) == true)
            __result = __result.Select(q =>
            {
                var state = pmc.Quests.FirstOrDefault(s => s.QId == q.Id);
                if (!Alternatives.Contains(q.Id.ToString()) || state?.Status != QuestStatusEnum.Fail)
                    return q;
                var copy = _cloner.Clone(q)!;
                copy.Status = (int)QuestStatusEnum.Fail;
                copy.Restartable = false;
                return copy;
            }).ToList();
        if (pmc?.TradersInfo?.TryGetValue(TraderId, out var trader) != true || trader.Unlocked != true)
            return;
        __result.RemoveAll(q => q.Id.ToString() == QuestId);
    }

    private static bool Matches(PmcData pmc, QuestCondition condition) => condition.ConditionType == "Quest" && condition.Status != null && pmc.Quests.Any(q => condition.Status.Contains(q.Status) && (condition.Target?.Item == q.QId.ToString() || condition.Target?.List?.Contains(q.QId.ToString()) == true));

    private static void Repair(PmcData? pmc)
    {
        if (pmc?.Quests == null)
            return;
        lock (pmc)
        {
            if (pmc.Quests.Any(q => (q.QId.ToString() == QuestId || q.QId.ToString() == DirectQuest) && q.Status == QuestStatusEnum.Success) && pmc.TradersInfo?.TryGetValue(TraderId, out var trader) == true)
                trader.Unlocked = true;
            var shortcut = pmc.Quests.FirstOrDefault(q => q.QId.ToString() == QuestId);
            if (pmc.Info?.Level >= 1 && pmc.TradersInfo?.TryGetValue(TraderId, out var lockedTrader) == true && lockedTrader.Unlocked != true && shortcut?.Status is QuestStatusEnum.Fail or QuestStatusEnum.Locked)
            {
                var paid = shortcut.CompletedConditions?.Contains(PaymentCondition) == true
                    || pmc.TaskConditionCounters?.TryGetValue(PaymentCondition, out var payment) == true && payment.Value.GetValueOrDefault() > 0;
                shortcut.Status = paid ? QuestStatusEnum.Started : QuestStatusEnum.AvailableForStart;
                shortcut.StatusTimers ??= [];
                shortcut.StatusTimers.Remove(QuestStatusEnum.Fail);
                shortcut.StatusTimers[shortcut.Status] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                shortcut.AvailableAfter = null;
            }
            var definitions = _quests.GetQuestsFromDb().Where(q => QuestIds.Contains(q.Id.ToString())).ToArray();
            if (pmc.Quests.Any(q => q.QId.ToString() == LaborQuest && q.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish or QuestStatusEnum.Success))
            {
                foreach (var definition in definitions.Where(q => q.Id.ToString() is DoorsQuest or InterceptionQuest or DirectQuest))
                {
                    var state = pmc.Quests.FirstOrDefault(q => q.QId == definition.Id);
                    if (state?.Status is not (QuestStatusEnum.Fail or QuestStatusEnum.Locked) || definition.Conditions?.Fail?.Any(c => Matches(pmc, c)) == true)
                        continue;
                    var failIds = definition.Conditions!.Fail!.Select(c => c.Id.ToString()).ToHashSet();
                    var started = state.StatusTimers?.ContainsKey(QuestStatusEnum.Started) == true
                        || state.CompletedConditions?.Any(id => !failIds.Contains(id.ToString())) == true
                        || pmc.TaskConditionCounters?.Values.Any(counter => counter.SourceId == definition.Id) == true;
                    var available = definition.Conditions.AvailableForStart.All(c => c.ConditionType == "Quest" ? Matches(pmc, c)
                        : c.ConditionType == "Level" && pmc.Info?.Level != null && _quests.DoesPlayerLevelFulfilCondition(pmc.Info.Level.Value, c));
                    state.Status = started ? QuestStatusEnum.Started : available ? QuestStatusEnum.AvailableForStart : QuestStatusEnum.Locked;
                    state.CompletedConditions?.RemoveAll(id => failIds.Contains(id.ToString()));
                    state.StatusTimers ??= [];
                    state.StatusTimers.Remove(QuestStatusEnum.Fail);
                    state.StatusTimers.TryAdd(state.Status, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    state.AvailableAfter = null;
                }
            }
            bool changed;
            do
            {
                changed = false;
                foreach (var definition in definitions)
                {
                    var state = pmc.Quests.FirstOrDefault(q => q.QId == definition.Id);
                    if (state != null && state.Status is QuestStatusEnum.Success or QuestStatusEnum.Fail)
                        continue;
                    if (definition.Conditions?.Fail?.Any(c => Matches(pmc, c)) != true)
                        continue;
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    if (state == null)
                    {
                        state = new QuestStatus { QId = definition.Id, StartTime = timestamp, Status = QuestStatusEnum.Fail, StatusTimers = [], CompletedConditions = [] };
                        pmc.Quests.Add(state);
                    }
                    state.Status = QuestStatusEnum.Fail;
                    state.StatusTimers ??= [];
                    state.StatusTimers[QuestStatusEnum.Fail] = timestamp;
                    state.AvailableAfter = null;
                    changed = true;
                }
            } while (changed);
        }
    }

    private static void FailAlternatives(PmcData __1, ref List<Quest> __2)
    {
        __2 = __2.Select(q =>
        {
            if (!QuestIds.Contains(q.Id.ToString()))
                return q;
            if (__1.Quests.Any(s => s.QId == q.Id && s.Status == QuestStatusEnum.Success))
                return null;
            var conditions = q.Conditions?.Fail?.Where(c => Matches(__1, c)).ToList();
            if (conditions == null || conditions.Count == 0)
                return null;
            var copy = _cloner.Clone(q)!;
            copy.Conditions.Fail = _cloner.Clone(conditions)!;
            foreach (var condition in copy.Conditions.Fail)
                condition.Status = [QuestStatusEnum.Success];
            return copy;
        }).Where(q => q != null).Cast<Quest>().ToList();
    }

    private sealed class ResponseState
    {
        public Dictionary<string, QuestStatusEnum> Quests { get; } = new(StringComparer.Ordinal);
        public bool Unlocked { get; set; }
    }

    private static void BeforeQuest(PmcData __0, out ResponseState __state)
    {
        __state = new ResponseState();
        foreach (var quest in __0.Quests.Where(q => QuestIds.Contains(q.QId.ToString())))
            __state.Quests[quest.QId.ToString()] = quest.Status;
        __state.Unlocked = __0.TradersInfo?.TryGetValue(TraderId, out var trader) == true && trader.Unlocked == true;
    }

    private static void AfterQuest(PmcData __0, MongoId __2, ItemEventRouterResponse __result, ResponseState __state) => UpdateResponse(__0, __2, __result, __state);

    private static void AfterFailure(PmcData __0, MongoId __2, ItemEventRouterResponse? __3, ResponseState __state) => UpdateResponse(__0, __2, __3, __state);

    private static void UpdateResponse(PmcData pmc, MongoId session, ItemEventRouterResponse? output, ResponseState before)
    {
        Repair(pmc);
        if (output?.ProfileChanges?.TryGetValue(session, out var changes) != true)
            return;
        var changed = pmc.Quests.Where(q => QuestIds.Contains(q.QId.ToString()) && (!before.Quests.TryGetValue(q.QId.ToString(), out var status) || status != q.Status)).ToDictionary(q => q.QId.ToString(), StringComparer.Ordinal);
        if (changed.Count > 0)
        {
            changes.QuestsStatus ??= [];
            changes.QuestsStatus.RemoveAll(q => changed.ContainsKey(q.QId.ToString()));
            changes.QuestsStatus.AddRange(_cloner.Clone(changed.Values.ToList())!);
            foreach (var definition in _quests.GetQuestsFromDb().Where(q => changed.ContainsKey(q.Id.ToString())))
            {
                var state = changed[definition.Id.ToString()];
                if (state.Status != QuestStatusEnum.Fail && !changes.Quests.Any(q => q.Id == definition.Id))
                    continue;
                var copy = _cloner.Clone(definition)!;
                copy.SptStatus = state.Status;
                copy.Status = (int)state.Status;
                if (Alternatives.Contains(definition.Id.ToString()) && pmc.Quests.Any(q => q.QId.ToString() == QuestId && q.Status == QuestStatusEnum.Success))
                    copy.Restartable = false;
                changes.Quests.RemoveAll(q => q.Id == definition.Id);
                changes.Quests.Add(copy);
            }
        }
        if (!before.Unlocked && pmc.TradersInfo?.TryGetValue(TraderId, out var trader) == true && trader.Unlocked == true)
        {
            changes.TraderRelations ??= [];
            changes.TraderRelations[TraderId] = new TraderData { Unlocked = trader.Unlocked, Disabled = trader.Disabled, Loyalty = trader.LoyaltyLevel, Standing = trader.Standing, SalesSum = trader.SalesSum };
        }
    }
}
