using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Aishi;
[HarmonyPatch]
internal static class AishiResourceDefaults
{
    private static PropertyInfo templateId = null!;
    private static MethodBase TargetMethod()
    {
        var method = AccessTools.Method(typeof(ItemHelper), nameof(ItemHelper.GenerateUpdForItem));
        templateId = AccessTools.Property(method.GetParameters()[0].ParameterType, "Id");
        return method;
    }

    private static void Postfix(object __0, ref Upd __result)
    {
        var id = templateId.GetValue(__0)?.ToString();
        var maximum = id switch
        {
            "2b20c487a99515b22b11ff78" => 5,
            "a2c16b836b441dc953652bd6" => 10,
            "337052e7f499ba2f52dfa089" => 1,
            _ => 0
        };
        if (maximum == 0 || __result?.Resource != null)
            return;
        __result ??= new Upd();
        __result.Resource = new UpdResource
        {
            Value = maximum
        };
    }
}
