using System.Reflection;
using System.Runtime.CompilerServices;
using EFT.UI;
using HarmonyLib;
using InsuranceCompany = InsuranceCompanyClass;

namespace AishiKeys
{
    [HarmonyPatch(typeof(TraderScreensGroup), "method_7")]
    internal static class AishiInsuranceContext
    {
        private sealed class Visit
        {
            internal InsuranceCompany Company;
            internal string Previous;
        }

        private static readonly ConditionalWeakTable<TraderScreensGroup, Visit> Visits = new ConditionalWeakTable<TraderScreensGroup, Visit>();
        private static readonly MethodInfo Session = AccessTools.Method(typeof(TraderScreensGroup), "get_GInterface223_0");
        private static readonly MethodInfo Trader = AccessTools.Method(typeof(TraderScreensGroup), "get_GClass2572_0");
        private static void Prefix(TraderScreensGroup __instance)
        {
            var trader = Trader.Invoke(__instance, null) as TraderClass;
            var session = Session.Invoke(__instance, null) as ITraderInteractions;
            if (trader?.Info.Id != AishiDollarUi.TraderId)
            {
                Restore(__instance);
                return;
            }

            var company = session?.InsuranceCompany;
            if (company == null || Visits.TryGetValue(__instance, out _))
                return;
            Visits.Add(__instance, new Visit { Company = company, Previous = company.SelectedInsurerId });
            company.SelectedInsurerId = AishiDollarUi.TraderId;
        }

        internal static void Restore(TraderScreensGroup screen)
        {
            if (!Visits.TryGetValue(screen, out var visit))
                return;
            if (visit.Company.SelectedInsurerId == AishiDollarUi.TraderId)
                visit.Company.SelectedInsurerId = visit.Previous;
            Visits.Remove(screen);
        }
    }

    [HarmonyPatch(typeof(TraderScreensGroup), "Close")]
    internal static class AishiInsuranceContextClose
    {
        private static void Prefix(TraderScreensGroup __instance) => AishiInsuranceContext.Restore(__instance);
    }
}
