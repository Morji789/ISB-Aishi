using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using BepInEx.Logging;

namespace AishiKeys
{
    [Serializable]
    internal sealed class ClothingFolders
    {
        public bool Enabled = true;
        public ClothingFolder[] Groups = new ClothingFolder[0];
    }

    [Serializable]
    internal sealed class ClothingFolder
    {
        public string Name = string.Empty;
        public string[] SuiteIds = new string[0];
    }

    internal static class AishiClothingGroups
    {
        private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Aishi Clothing Groups");
        internal static Dictionary<string, AishiClothingCatalog.Entry> Load()
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(AishiClothingGroups).Assembly.Location), "ClothingGroups.json");
            try
            {
                if (!File.Exists(path))
                    return AishiClothingCatalog.Entries;
                return Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.LogWarning("ClothingGroups.json: " + ex.Message + ". Using the default folders for this visit.");
                return AishiClothingCatalog.Entries;
            }
        }

        internal static Dictionary<string, AishiClothingCatalog.Entry> Parse(string json)
        {
            var config = JsonConvert.DeserializeObject<ClothingFolders>(json) ?? throw new FormatException("Empty configuration");
            var result = new Dictionary<string, AishiClothingCatalog.Entry>(StringComparer.OrdinalIgnoreCase);
            if (!config.Enabled)
                return result;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in config.Groups ?? new ClothingFolder[0])
            {
                if (group == null || string.IsNullOrWhiteSpace(group.Name) || !names.Add(group.Name.Trim()))
                    throw new FormatException("Each folder needs a unique name");
                int rank = 0;
                foreach (string raw in group.SuiteIds ?? new string[0])
                {
                    string id = raw?.Trim();
                    if (id == null || id.Length != 24 || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[0-9a-fA-F]{24}$"))
                        throw new FormatException("Invalid Suite ID in " + group.Name);
                    if (result.ContainsKey(id))
                        throw new FormatException("Suite ID appears in more than one folder: " + id);
                    result.Add(id, new AishiClothingCatalog.Entry(group.Name.Trim(), "", rank++));
                }
            }

            return result;
        }
    }
}
