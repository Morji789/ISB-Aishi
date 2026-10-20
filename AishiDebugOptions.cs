using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Aishi;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1400)]
public sealed class AishiDebugOptions(TradersTable traders, AishiAnomalyConfig config) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!config.Debug.UnlockAllClothes || !traders.TryGetValue(DollarServicePolicy.AishiId, out var trader))
            return Task.CompletedTask;
        foreach (var suit in trader.Suits ?? [])
            Unlock(suit);
        return Task.CompletedTask;
    }

    internal static void Unlock(Suit suit)
    {
        MongoId[] money = ["5449016a4bdc2d6f028b456f", "5696686a4bdc2da3298b456a", "569668774bdc2da2298b4568"];
        var currency = suit.Requirements?.ItemRequirements?.FirstOrDefault(item => money.Contains(item.Tpl))?.Tpl ?? money[1];
        suit.IsActive = true;
        suit.ExternalObtain = false;
        suit.InternalObtain = true;
        suit.IsHiddenInPVE = false;
        suit.Requirements = new SuitRequirements
        {
            LoyaltyLevel = 1,
            ProfileLevel = 1,
            PrestigeLevel = 0,
            Standing = 0,
            QuestRequirements = [],
            AchievementRequirements = [],
            SkillRequirements = [],
            ItemRequirements = [new ItemRequirement { Tpl = currency, Count = 1 }],
            RequiredTid = DollarServicePolicy.AishiId
        };
    }
}
