using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace Aishi;
public sealed class SkillRewardPreset
{
    public bool Enabled { get; set; }
    public double Chance { get; set; } = 100;
    public IntRange Levels { get; set; } = new()
    {
        Min = 1,
        Max = 2
    };
    public List<string> Pool { get; set; } = [];
}

public static class AishiSkillRewards
{
    public static void Normalize(SkillRewardPreset preset)
    {
        preset.Chance = double.IsFinite(preset.Chance) ? Math.Clamp(preset.Chance, 0, 100) : 0;
        preset.Levels ??= new IntRange
        {
            Min = 1,
            Max = 2
        };
        int min = Math.Clamp(preset.Levels.Min, 1, 51), max = Math.Clamp(preset.Levels.Max, 1, 51);
        preset.Levels.Min = Math.Min(min, max);
        preset.Levels.Max = Math.Max(min, max);
        var names = Enum.GetNames<SkillTypes>();
        preset.Pool = (preset.Pool ?? []).Select(skill => names.FirstOrDefault(name => string.Equals(name, skill?.Trim(), StringComparison.OrdinalIgnoreCase))).Where(skill => skill != null).Select(skill => skill!).Distinct(StringComparer.Ordinal).ToList();
    }

    public static Reward? Create(SkillRewardPreset? preset, int index)
    {
        if (preset?.Enabled != true || preset.Pool.Count == 0 || preset.Chance <= 0 || Random.Shared.NextDouble() * 100 >= preset.Chance)
            return null;
        var skill = preset.Pool[Random.Shared.Next(preset.Pool.Count)];
        var levels = Random.Shared.Next(preset.Levels.Min, preset.Levels.Max + 1);
        return new Reward
        {
            Id = new MongoId(),
            Unknown = false,
            GameMode = [],
            AvailableInGameEditions = [],
            Index = index,
            Target = skill,
            Value = levels * 100d,
            Type = RewardType.Skill
        };
    }
}
