using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;

namespace Aishi;
[Injectable]
public sealed class AishiAnomalyReveal
{
    private readonly SaveServer saves;
    private readonly AishiAnomalyConfig config;
    private readonly AishiKteData data;
    private static string detectorTemplate = "2b20c487a99515b22b11ff78";
    public AishiAnomalyReveal(SaveServer saveServer, AishiAnomalyConfig settings, AishiKteData knowledge)
    {
        data = knowledge;
        saves = saveServer;
        config = settings;
        detectorTemplate = settings.Reveal.RequiredEquippedItem;
    }

    private const string Receipt = "aishiRevealedAnomalies";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly FieldInfo? SaveHashes = typeof(SaveServer).GetField("saveMd5", BindingFlags.Instance | BindingFlags.NonPublic);
    public sealed class Request : IRequestData
    {
        [JsonPropertyName("detector")]
        public string Detector { get; set; } = "";

        [JsonPropertyName("anomaly")]
        public string Anomaly { get; set; } = "";

        [JsonPropertyName("template")]
        public string Template { get; set; } = "";

        [JsonPropertyName("profileId")]
        public string ProfileId { get; set; } = "";

        [JsonPropertyName("raid")]
        public bool Raid { get; set; }

        [JsonPropertyName("remaining")]
        public double Remaining { get; set; }
    }

    private static bool ValidId(string? value) => value?.Length == 24 && value.All(Uri.IsHexDigit);
    internal static HashSet<string> Revealed(SptProfile profile)
    {
        if (profile.ExtensionData?.TryGetValue(Receipt, out var previous) != true)
            return new(StringComparer.Ordinal);
        if (previous is IEnumerable<string> strings)
            return new(strings.Where(id => ValidId(id) && AishiAnomalyPolicy.Ids.Contains(id)), StringComparer.Ordinal);
        if (previous is JsonElement element && element.ValueKind == JsonValueKind.Array)
            return new(element.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Where(id => ValidId(id) && AishiAnomalyPolicy.Ids.Contains(id)), StringComparer.Ordinal);
        return new(StringComparer.Ordinal);
    }

    private SptProfile? Profile(MongoId session)
    {
        if (session.IsEmpty || !saves.GetProfiles().TryGetValue(session, out var profile) || profile?.ProfileInfo == null || profile.ProfileInfo.InvalidOrUnloadableProfile == true)
            return null;
        return profile;
    }

    private static Action ResourceRollback(Item item)
    {
        var oldUpd = item.Upd;
        var oldResource = oldUpd?.Resource;
        var oldValue = oldResource?.Value;
        return () =>
        {
            item.Upd = oldUpd;
            if (oldUpd != null)
                oldUpd.Resource = oldResource;
            if (oldResource != null)
                oldResource.Value = oldValue;
        };
    }

    private async Task<bool> Persist(MongoId session, IEnumerable<Action> rollback, CancellationToken cancellation, HashSet<string> discovered)
    {
        try
        {
            await saves.SaveProfileAsync(session, cancellation);
            var pmc = Profile(session)?.CharacterData?.PmcData;
            if (pmc == null || pmc.Id is not { IsEmpty: false })
                throw new InvalidOperationException("The active profile changed before analysis was saved.");
            data.Store(pmc.Id.ToString(), discovered);
            return true;
        }
        catch
        {
            foreach (var restore in rollback)
                restore();
            if (SaveHashes?.GetValue(saves)is IDictionary<MongoId, string> hashes)
                hashes.Remove(session);
            try
            {
                await saves.SaveProfileAsync(session);
            }
            catch
            {
            }

            if (SaveHashes?.GetValue(saves)is IDictionary<MongoId, string> retryHashes)
                retryHashes.Remove(session);
            return false;
        }
    }

    public async Task<object> Settings(MongoId session, CancellationToken cancellation)
    {
        var gate = Locks.GetOrAdd(session.ToString(), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellation);
        try
        {
            var profile = Profile(session);
            var pmc = profile?.CharacterData?.PmcData;
            var items = pmc?.Inventory?.Items;
            if (profile == null || pmc == null || items == null || pmc.Id is not { IsEmpty: false } || items.Any(item => item == null))
                return new
                {
                    ready = false,
                    requiredEquippedItem = config.Reveal.RequiredEquippedItem,
                    removeKTErestrictions = config.Debug.RemoveKTErestrictions,
                    profileId = "",
                    revealed = Array.Empty<string>(),
                    charges = new Dictionary<string, double>()
                };
            var detectors = items.Where(item => item.Template.ToString() == config.Reveal.RequiredEquippedItem).ToArray();
            var rollback = new List<Action>();
            var profileId = pmc.Id.ToString();
            var discovered = Revealed(profile);
            discovered.UnionWith(data.Get(profileId));
            foreach (var detector in detectors.Where(item => item.Upd?.Resource?.Value == null))
            {
                rollback.Add(ResourceRollback(detector));
                detector.Upd ??= new Upd();
                detector.Upd.Resource = new UpdResource
                {
                    Value = 5
                };
            }

            if (rollback.Count > 0 && !await Persist(session, rollback, cancellation, discovered))
                throw new InvalidOperationException("The analyser charges could not be saved.");
            data.Store(profileId, discovered);
            return new
            {
                ready = true,
                requiredEquippedItem = config.Reveal.RequiredEquippedItem,
                    removeKTErestrictions = config.Debug.RemoveKTErestrictions,
                profileId,
                revealed = discovered.ToArray(),
                charges = detectors.ToDictionary(item => item.Id.ToString(), item => item.Upd?.Resource?.Value ?? 0)
            };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> Reveal(MongoId session, object info, CancellationToken cancellation)
    {
        Request? request;
        try
        {
            request = JsonSerializer.Deserialize<Request>(JsonSerializer.Serialize(info), Options);
        }
        catch (Exception error)when (error is JsonException or ArgumentException)
        {
            return Failure("Invalid anomaly.");
        }

        if (request == null || !ValidId(request.Template) || !AishiAnomalyPolicy.Ids.Contains(request.Template) || !ValidId(request.Anomaly) || !ValidId(request.Detector))
            return Failure("Invalid anomaly.");
        request.Template = request.Template.ToLowerInvariant();
        request.Detector = request.Detector.ToLowerInvariant();
        request.Anomaly = request.Anomaly.ToLowerInvariant();
        if (request.Raid && (!double.IsFinite(request.Remaining) || request.Remaining < 0 || request.Remaining > 5 || request.Remaining != Math.Floor(request.Remaining)))
            return Failure("Invalid analyser charges.");
        var gate = Locks.GetOrAdd(session.ToString(), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellation);
        try
        {
            var profile = Profile(session);
            var pmc = profile?.CharacterData?.PmcData;
            if (profile == null || pmc == null || pmc.Inventory?.Items == null || pmc.Id is not { IsEmpty: false } || pmc.Inventory.Items.Any(item => item == null))
                return Failure("The active inventory is unavailable.");
            var profileId = pmc.Id.ToString();
            if (request.ProfileId?.Length > 0 && request.ProfileId != profileId)
                return Failure("The active profile changed. Try again.");
            var inventory = pmc.Inventory;
            var items = inventory.Items;
            var detector = items.FirstOrDefault(item => item.Id.ToString() == request.Detector && item.Template.ToString() == config.Reveal.RequiredEquippedItem);
            var pockets = items.FirstOrDefault(item => item.ParentId == inventory.Equipment?.ToString() && item.SlotId == "Pockets");
            if (detector == null || pockets == null || detector.ParentId != pockets.Id.ToString() || detector.SlotId?.StartsWith("SpecialSlot", StringComparison.Ordinal) != true)
                return Failure("Equip the analyser in a special slot.");
            var anomaly = items.FirstOrDefault(item => item.Id.ToString() == request.Anomaly);
            if (anomaly != null && anomaly.Template.ToString() != request.Template || !request.Raid && anomaly == null)
                return Failure("The anomaly is no longer in your inventory.");
            var revealed = Revealed(profile);
            revealed.UnionWith(data.Get(profileId));
            double current = detector.Upd?.Resource?.Value ?? 5;
            if (revealed.Contains(request.Template))
                return JsonSerializer.Serialize(new { success = true, already = true, remaining = current, profileId, revealed = revealed.ToArray() });
            if (request.Raid)
                current = Math.Min(current, request.Remaining);
            if (!double.IsFinite(current) || current < 1 || current > 5 || current != Math.Floor(current))
                return Failure("The analyser has no charges remaining.");
            var rollback = ResourceRollback(detector);
            var previousExtension = profile.ExtensionData;
            var hadReceipt = previousExtension?.TryGetValue(Receipt, out _) == true;
            var previousReceipt = hadReceipt ? previousExtension![Receipt] : null;
            Action rollbackReceipt = () =>
            {
                if (previousExtension == null)
                    profile.ExtensionData = null;
                else if (hadReceipt)
                    previousExtension[Receipt] = previousReceipt!;
                else
                    previousExtension.Remove(Receipt);
            };
            detector.Upd ??= new Upd();
            detector.Upd.Resource ??= new UpdResource();
            detector.Upd.Resource.Value = current - 1;
            revealed.Add(request.Template);
            profile.ExtensionData ??= new Dictionary<string, object>();
            profile.ExtensionData[Receipt] = revealed.ToArray();
            if (!await Persist(session, [rollback, rollbackReceipt], cancellation, revealed))
                return Failure("Profile saving failed; analysis was not charged.");
            return JsonSerializer.Serialize(new { success = true, remaining = current - 1, profileId, revealed = revealed.ToArray() });
        }
        finally
        {
            gate.Release();
        }
    }

    internal static void ReconcileCharges(SptProfile before, PmcData after)
    {
        var prior = before.CharacterData?.PmcData?.Inventory?.Items;
        var returned = after.Inventory?.Items;
        if (prior == null || returned == null)
            return;
        var remaining = prior.Where(item => item.Template.ToString() == detectorTemplate && item.Upd?.Resource?.Value != null).GroupBy(item => item.Id.ToString()).ToDictionary(group => group.Key, group => group.First().Upd!.Resource!.Value ?? 0);
        foreach (var item in returned.Where(item => item.Template.ToString() == detectorTemplate))
        {
            if (!remaining.TryGetValue(item.Id.ToString(), out var saved) || saved < 0 || saved > 5 || !double.IsFinite(saved))
                continue;
            var submitted = item.Upd?.Resource?.Value;
            if (submitted.HasValue && double.IsFinite(submitted.Value) && submitted.Value >= 0 && submitted.Value <= saved)
                continue;
            item.Upd ??= new Upd();
            item.Upd.Resource ??= new UpdResource();
            item.Upd.Resource.Value = saved;
        }
    }

    private static string Failure(string error) => JsonSerializer.Serialize(new { success = false, error });
}

[HarmonyPatch(typeof(LocationLifecycleService), "HandlePostRaidPmc")]
internal static class AishiAnalyserRaidCharges
{
    private static void Prefix(SptProfile __1, EndLocalRaidRequestData __6)
    {
        if (__6.Results?.Profile != null)
            AishiAnomalyReveal.ReconcileCharges(__1, __6.Results.Profile);
    }
}
