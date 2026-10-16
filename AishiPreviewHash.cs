using System.Globalization;
using System.Text.Json.Nodes;

namespace Aishi;

internal static class AishiPreviewHash
{
    internal static int Calculate(JsonArray flat, Func<string, JsonObject> template)
    {
        var root = flat.First(item => item?["parentId"] == null)!;
        bool Is(string id, string name)
        {
            var seen = new HashSet<string>();
            while (seen.Add(id))
            {
                var node = template(id);
                if (node["_name"]?.GetValue<string>() == name) return true;
                if (node["_parent"]?.GetValue<string>() is not { Length: > 0 } parent) return false;
                id = parent;
            }
            return false;
        }
        int Visit(JsonNode item, int seed)
        {
            var tpl = item["_tpl"]!.GetValue<string>();
            var properties = template(tpl)["_props"]!;
            var hash = Mongo(tpl);
            if (properties["HasHinge"]?.GetValue<bool>() == true) hash ^= 23 + (item["upd"]?["Togglable"]?["On"]?.GetValue<bool>() == false ? 0 : 1);
            if (properties["Foldable"]?.GetValue<bool>() == true) hash ^= (23 + (item["upd"]?["Foldable"]?["Folded"]?.GetValue<bool>() == true ? 1 : 0)) << 1;
            var id = item["_id"]!.GetValue<string>();
            var children = flat.Where(child => child?["parentId"]?.GetValue<string>() == id).ToArray();
            if (Is(tpl, "Magazine"))
            {
                var count = children.Sum(child => child?["upd"]?["StackObjectsCount"]?.GetValue<int>() ?? 1);
                hash ^= (23 + Visible(count, properties["VisibleAmmoRangesString"]?.GetValue<string>())) << 2;
            }
            var result = unchecked(hash * seed);
            if (properties["HideEntrails"]?.GetValue<bool>() == true) return result;
            foreach (var child in children)
            {
                var slot = unchecked(2777 * Text(child!["slotId"]!.GetValue<string>()) + 7901 * Mongo(tpl));
                var next = unchecked(seed * 6529) ^ slot;
                if (properties["Cartridges"] is JsonArray { Count: > 0 }) next ^= 2879;
                result ^= Visit(child!, next);
            }
            return result;
        }
        return 17 ^ Visit(root, 1) ^ (Is(root["_tpl"]!.GetValue<string>(), "Ammo") ? 27 * 56 : 0);
    }

    private static int Mongo(string id)
    {
        var counter = ulong.Parse(id[8..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return unchecked((int)uint.Parse(id[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture) ^ (int)((counter >> 32) * 3637) ^ (int)((counter & uint.MaxValue) * 5807));
    }

    private static int Text(string value)
    {
        int first = 5381, second = first;
        for (var i = 0; i < value.Length; i += 2)
        {
            first = unchecked(first * 33) ^ value[i];
            if (i + 1 < value.Length) second = unchecked(second * 33) ^ value[i + 1];
        }
        return unchecked(first + second * 1566083941);
    }

    private static int Visible(int count, string? ranges)
    {
        var pairs = (string.IsNullOrEmpty(ranges) ? "1-2" : ranges).Split(';').Select(part => part.Split('-').Select(int.Parse).ToArray()).ToArray();
        for (var i = 0; i < pairs.Length; i++)
        {
            if (pairs[i][0] <= count && count <= pairs[i][1]) return count;
            if (count < pairs[i][0]) return i == 0 ? pairs[i][0] : pairs[i - 1][1];
        }
        return pairs[^1][1];
    }
}
