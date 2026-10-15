using AishiShared;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Insurance;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;

namespace Aishi;
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 2000)]
public sealed class AishiDollarServices(DatabaseService database, HandbookHelper handbook, EventOutputHolder outputs, ItemHelper itemHelper, TraderHelper traderHelper) : IOnLoad
{
    public Task OnLoad()
    {
        DollarServicePolicy.Initialize(database.GetTemplates().Items, handbook, outputs);
        DollarServicePolicy.ConfigureInsurance(itemHelper, traderHelper);
        var trader = database.GetTraders()[DollarServicePolicy.AishiId].Base;
        trader.Repair.CurrencyCoefficient = 1d / DollarServicePolicy.RoublesPerDollar;
        trader.Insurance.MinPayment = (int)DollarServicePolicy.ToDollars(trader.Insurance.MinPayment ?? 0);
        new Harmony("com.samc137.aishi.dollar-services").PatchAll(typeof(AishiDollarServices).Assembly);
        return Task.CompletedTask;
    }
}

public static class DollarServicePolicy
{
    public const string AishiId = "690766de550bc322a810ea1e";
    public const string DollarId = "5696686a4bdc2da3298b456a";
    public const string RoubleId = "5449016a4bdc2d6f028b456f";
    private static Dictionary<MongoId, TemplateItem> items = new();
    public static double RoublesPerDollar { get; private set; }

    internal static EventOutputHolder Outputs = null!;
    private static ItemHelper itemHelper = null!;
    private static TraderHelper traderHelper = null!;
    internal static void ConfigureInsurance(ItemHelper helper, TraderHelper traders)
    {
        itemHelper = helper;
        traderHelper = traders;
    }

    public static double QuoteInsurance(InsuranceService service, PmcData? pmc, Item item, MongoId trader)
    {
        if (!IsAishi(trader))
            return service.GetRoublePriceToInsureItemWithTrader(pmc, item, trader);
        var native = Math.Ceiling(itemHelper.GetStaticItemPrice(item.Template) * (traderHelper.GetLoyaltyLevel(trader, pmc).InsurancePriceCoefficient / 100d) ?? 1d);
        return InsurancePrice(native, item.Template, trader);
    }

    public static void Initialize(Dictionary<MongoId, TemplateItem> templates, HandbookHelper handbook, EventOutputHolder outputs)
    {
        var rate = handbook.GetTemplatePrice(DollarId);
        if (!double.IsFinite(rate) || rate <= 0)
            throw new InvalidOperationException("Aishi: invalid USD handbook exchange rate.");
        items = templates;
        RoublesPerDollar = rate;
        Outputs = outputs;
    }

    public static bool IsAishi(MongoId trader) => trader.ToString() == AishiId;
    public static bool IsRestrictedIsb(MongoId template) => IsbInsuranceCatalog.Ids.Contains(template.ToString()) && items.TryGetValue(template, out var item) && item.Properties.InsuranceDisabled == true;
    public static double ToDollars(double roubles) => roubles <= 0 ? 0 : Math.Ceiling(roubles / RoublesPerDollar);
    public static double InsurancePrice(double nativeRoubles, MongoId template, MongoId trader)
    {
        if (!IsAishi(trader))
            return nativeRoubles;
        if (!double.IsFinite(nativeRoubles) || nativeRoubles < 0)
            throw new InvalidDataException("Aishi insurance quote is not a valid nonnegative price.");
        return Math.Ceiling(nativeRoubles / RoublesPerDollar * (IsRestrictedIsb(template) ? 4d : 1.4d));
    }

    public static double RepairPrice(double nativeRoubles, MongoId trader) => IsAishi(trader) ? Math.Floor(nativeRoubles / RoublesPerDollar) : Math.Round(nativeRoubles);
    public static void SetPaymentCurrency(ProcessBuyTradeRequestData request)
    {
        if (!IsAishi(request.TransactionId) || request.Action is not ("SptInsure" or "SptRepair"))
            return;
        foreach (var payment in request.SchemeItems)
            if (payment.Id.ToString() == RoubleId)
                payment.Id = DollarId;
    }
}

[HarmonyPatch(typeof(InsuranceService), nameof(InsuranceService.GetRoublePriceToInsureItemWithTrader))]
internal static class DollarInsuranceQuotePatch
{
    private static void Postfix(Item __1, MongoId __2, ref double __result) => __result = DollarServicePolicy.InsurancePrice(__result, __1.Template, __2);
}

[HarmonyPatch(typeof(PaymentService), nameof(PaymentService.PayMoney))]
internal static class DollarPaymentPatch
{
    private static void Prefix(ProcessBuyTradeRequestData __1) => DollarServicePolicy.SetPaymentCurrency(__1);
}

[HarmonyPatch(typeof(RepairService), nameof(RepairService.RepairItemByTrader))]
internal static class DollarRepairQuotePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int changed = 0;
        var nativeRound = AccessTools.Method(typeof(Math), nameof(Math.Round), new[] { typeof(double) });
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(nativeRound))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_S, 4).MoveLabelsFrom(instruction);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DollarServicePolicy), nameof(DollarServicePolicy.RepairPrice)));
                changed++;
            }
            else
                yield return instruction;
        }

        if (changed != 1)
            throw new InvalidOperationException("Aishi: native repair quote shape changed.");
    }
}

[HarmonyPatch(typeof(InsuranceController), nameof(InsuranceController.Insure))]
internal static class IsbInsuranceTraderGuard
{
    private static bool Prefix(PmcData __0, InsureRequestData __1, MongoId __2, ref ItemEventRouterResponse __result)
    {
        if (DollarServicePolicy.IsAishi(__1.TransactionId))
            return true;
        var selected = __1.Items.ToHashSet();
        if (!__0.Inventory.Items.Any(item => selected.Contains(item.Id) && DollarServicePolicy.IsRestrictedIsb(item.Template)))
            return true;
        __result = DollarServicePolicy.Outputs.GetOutput(__2);
        (__result.Warnings ??= []).Add(new Warning { ErrorMessage = "Only Aishi can insure these restricted ISB items." });
        return false;
    }
}

[HarmonyPatch]
internal static class DollarInsuranceRequestQuotePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(InsuranceController), nameof(InsuranceController.Cost));
        yield return AccessTools.Method(typeof(InsuranceController), nameof(InsuranceController.Insure));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int changed = 0;
        var quote = AccessTools.Method(typeof(InsuranceService), nameof(InsuranceService.GetRoublePriceToInsureItemWithTrader));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(quote))
            {
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DollarServicePolicy), nameof(DollarServicePolicy.QuoteInsurance))).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                changed++;
            }
            else
                yield return instruction;
        }

        if (changed != 1)
            throw new InvalidOperationException("Aishi: native insurance request quote shape changed.");
    }
}
