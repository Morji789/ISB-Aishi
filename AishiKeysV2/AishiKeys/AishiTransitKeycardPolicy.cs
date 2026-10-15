using System;
using System.Collections.Generic;

namespace AishiKeys
{
    internal static class AishiTransitKeycardPolicy
    {
        private const string Labs = "5b0fc42d86f7744a585f9105";
        private const string Labyrinth = "6733700029c367a3d40b02af";
        private static readonly HashSet<string> LabsCards = new HashSet<string>(StringComparer.Ordinal)
        {
            "69439100a320344f805ba61f",
            "694479f61287f9a2b1060a7f",
            "6944b4b15a0413f1b7f50724",
            "69860f957139b00c69e69cb7",
            "698610e3b949497163d3d742",
            "6a0a5091eb733b78d06f82b2",
            "999a5a06e1a05ca89b786f34",
            "6a321d846b38e922175d1878"
        };
        private static readonly HashSet<string> LabyrinthCards = new HashSet<string>(StringComparer.Ordinal)
        {
            "694479f61287f9a2b1060a7f",
            "6944b4b15a0413f1b7f50724",
            "999a5a06e1a05ca89b786f34"
        };
        internal static bool IsReusable(string target, string templateId)
        {
            if (target == Labs)
                return LabsCards.Contains(templateId);
            if (target == Labyrinth)
                return LabyrinthCards.Contains(templateId);
            return false;
        }
    }
}
