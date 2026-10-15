using System.Text.Json.Nodes;

namespace Aishi;
internal static class AishiHammerheadSlotMigration
{
    internal const string Template = "7f3d1490eb7e5442947f28f5";

    internal static int Repair(JsonNode? node)
    {
        int changed = 0;
        if (node is JsonObject obj)
        {
            if (obj["items"] is JsonArray items && obj["equipment"] is JsonValue equipmentValue && equipmentValue.TryGetValue<string>(out var equipment))
            {
                var equipped = items.OfType<JsonObject>().Where(item => item["parentId"]?.GetValue<string>() == equipment).ToArray();
                if (!equipped.Any(item => item["slotId"]?.GetValue<string>() == "FaceCover"))
                {
                    var helmet = equipped.FirstOrDefault(item => item["_tpl"]?.GetValue<string>() == Template && item["slotId"]?.GetValue<string>() == "Headwear");
                    if (helmet != null)
                    {
                        helmet["slotId"] = "FaceCover";
                        changed++;
                    }
                }
            }
            foreach (var pair in obj) changed += Repair(pair.Value);
        }
        else if (node is JsonArray array)
            foreach (var child in array) changed += Repair(child);
        return changed;
    }
}
