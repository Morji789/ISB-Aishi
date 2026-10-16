using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AishiKeysPro;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Screens;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
#if !SPT41
using ItemFactory = ItemFactoryClass;
using ItemIconCreator = GClass926;
using ItemIcon = GClass929;
using IntVec2 = XYCellSizeStruct;
using IconsHash = GClass928;
using EftScreenManager = CurrentScreenSingletonClass;
using FlatItem = FlatItemsDataClass;
#else
using FlatItem = JsonType.FlatItem;
#endif

namespace AishiKeys
{
    internal static class AishiPreviewCache
    {
        private sealed class Catalog { public int version; public Job[] items; }
        private sealed class Job
        {
            public string key, template, stamp;
            public FlatItem[] flat;
            public int cacheKey;
        }
        private static Catalog catalog;
        private sealed class State
        {
            public Dictionary<int, string> stamps = new Dictionary<int, string>();
            public Dictionary<int, int> aliases = new Dictionary<int, int>();
        }
        private static State state;
        private static int position;
        private static bool busy, stopped, headless;
        private static float next;
        private static string statePath;

        internal static bool CanGenerate => !stopped && !headless && !Singleton<AbstractGame>.Instantiated && !Singleton<GameWorld>.Instantiated
            && EftScreenManager.Instance.CurrentBaseScreenController?.ScreenType == EEftScreenType.MainMenu;

        internal static void Update(bool headless)
        {
            AishiPreviewCache.headless = headless;
            if (headless || busy || stopped || Time.realtimeSinceStartup < next || catalog != null && position >= catalog.items.Length) return;
            next = Time.realtimeSinceStartup + 0.25f;
            if (!CanGenerate || ItemUiContext.Instance?.ClientSession == null || !Singleton<ItemFactory>.Instantiated || !Singleton<ItemIconCreator>.Instantiated) return;
            busy = true;
            _ = Run();
        }

        internal static void Stop() => stopped = true;

        private static async Task Run()
        {
            try
            {
                if (catalog == null)
                {
                    catalog = JsonConvert.DeserializeObject<Catalog>(await RequestHandler.GetJsonAsync("/aishi/previews/catalog"));
                    if (catalog?.version != 1 || catalog.items == null) throw new InvalidDataException("Invalid Aishi preview catalog.");
                    var creator = Singleton<ItemIconCreator>.Instance;
                    statePath = Path.Combine(Path.GetDirectoryName(IndexPath(creator)), "aishi-previews.json");
                    try { state = JsonConvert.DeserializeObject<State>(File.ReadAllText(statePath)); }
                    catch (IOException) { state = null; }
                    catch (JsonException) { state = null; }
                    state = state ?? new State();
                    state.stamps = state.stamps ?? new Dictionary<int, string>();
                    state.aliases = state.aliases ?? new Dictionary<int, int>();
                }
                var start = Time.realtimeSinceStartup;
                while (position < catalog.items.Length && CanGenerate)
                {
                    var creator = Singleton<ItemIconCreator>.Instance;
                    if (QueueCount(creator) > 0 || Rendering(creator)) break;
                    var job = catalog.items[position];
                    try
                    {
                        var factory = Singleton<ItemFactory>.Instance;
                        Item item;
                        if (job.flat == null) item = factory.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), job.template, null);
                        else
                        {
                            var tree = factory.FlatItemsToTree(job.flat);
                            if (tree.DeserializationErrors.Count > 0) throw new InvalidDataException("Invalid item tree.");
                            var root = Array.Find(job.flat, flat => !flat.parentId.HasValue);
                            item = tree.Items[root._id];
                        }
                        var hash = IconsHash.GetItemHash(item);
                        var stamp = "2:" + typeof(Item).Assembly.ManifestModule.ModuleVersionId + ":" + job.stamp;
                        var changed = false;
                        if (!state.stamps.TryGetValue(hash, out var previous) || previous != stamp || !HasFile(creator, hash))
                        {
                            if (!await Generate(creator, item, hash)) break;
                            state.stamps[hash] = stamp;
                            changed = true;
                        }
                        if (job.cacheKey != hash && (!Files(creator).ContainsKey(job.cacheKey) || state.aliases.TryGetValue(job.cacheKey, out var owner) && owner == hash) && (!Files(creator).TryGetValue(job.cacheKey, out var saved) || saved != Files(creator)[hash]))
                        {
                            Files(creator)[job.cacheKey] = Files(creator)[hash];
                            state.aliases[job.cacheKey] = hash;
                            changed = true;
                        }
                        if (changed) await Persist(creator);
                        position++;
                    }
                    catch (Exception error)
                    {
                        AishiKeysMod.Logger.LogWarning("Aishi preview could not be generated for " + job.template + ": " + error.Message);
                        position++;
                    }
                    if (Time.realtimeSinceStartup - start >= 0.004f) break;
                }
            }
            catch (Exception error)
            {
                catalog = null;
                next = Time.realtimeSinceStartup + 60f;
                AishiKeysMod.Logger.LogWarning("Aishi preview cache is unavailable: " + error.Message);
            }
            finally { busy = false; }
        }

        private static async Task<bool> Generate(ItemIconCreator creator, Item item, int hash)
        {
            Sprite captured = null, temporary = null;
            Texture2D readable = null;
            try
            {
                var cells = item.CalculateCellSize();
                var size = new IntVec2(Math.Max(128, Math.Min(512, cells.X * 64)), Math.Max(128, Math.Min(512, cells.Y * 64)));
                var rendered = await creator.RenderModel(item, async (model, pivot) =>
                {
                    await Prepare(creator);
                    while (Rendering(creator)) await Task.Yield();
                    if (!CanGenerate) return null;
                    SetRendering(creator, true);
                    try { return captured = Capture(creator, model, size, pivot); }
                    finally { SetRendering(creator, false); }
                });
                if (!CanGenerate || rendered.sprite == null) return false;
                var sprite = rendered.sprite;
                if (sprite == captured)
                {
                    await Save(creator, new ItemIcon(hash) { Sprite = captured });
                    Memory(creator).Remove(hash);
                    return HasFile(creator, hash);
                }
                var target = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(sprite.texture, target);
                    RenderTexture.active = target;
                    readable = new Texture2D((int)sprite.rect.width, (int)sprite.rect.height, TextureFormat.RGBA32, false);
                    readable.ReadPixels(sprite.rect, 0, 0); readable.Apply();
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
                temporary = Sprite.Create(readable, new Rect(0, 0, readable.width, readable.height), Vector2.zero);
                await Save(creator, new ItemIcon(hash) { Sprite = temporary });
                Memory(creator).Remove(hash);
                return HasFile(creator, hash);
            }
            finally
            {
                if (temporary != null) UnityEngine.Object.Destroy(temporary);
                if (readable != null) UnityEngine.Object.Destroy(readable);
                if (captured != null) { UnityEngine.Object.Destroy(captured.texture); UnityEngine.Object.Destroy(captured); }
            }
        }

        private static bool HasFile(ItemIconCreator creator, int hash) => Files(creator).TryGetValue(hash, out var id) && File.Exists(Path.Combine(Path.GetDirectoryName(IndexPath(creator)), id + ".png"));

        private static async Task Persist(ItemIconCreator creator)
        {
            await Write(IndexPath(creator), () => JsonConvert.SerializeObject(Files(creator)));
            await Write(statePath, () => JsonConvert.SerializeObject(state));
        }

        private static async Task Write(string path, Func<string> content)
        {
            var temp = path + ".aishi-tmp";
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.WriteAllText(temp, content());
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                    return;
                }
                catch (IOException) when (attempt < 3) { await Task.Delay(25); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }

#if SPT41
        private static Dictionary<int, int> Files(ItemIconCreator creator) => creator._fileCacheIndex;
        private static Dictionary<int, ItemIcon> Memory(ItemIconCreator creator) => creator._memoryCacheIndex;
        private static string IndexPath(ItemIconCreator creator) => creator._indexPath;
        private static int QueueCount(ItemIconCreator creator) => creator._queueCount;
        private static bool Rendering(ItemIconCreator creator) => creator._isIconCreating;
        private static void SetRendering(ItemIconCreator creator, bool value) => creator._isIconCreating = value;
        private static Task Prepare(ItemIconCreator creator) => creator.PrepareCameraAsync();
        private static Sprite Capture(ItemIconCreator creator, GameObject model, IntVec2 size, PreviewPivot pivot) => creator.CaptureSpriteOfModel(model, in size, pivot);
        private static Task Save(ItemIconCreator creator, ItemIcon icon) => creator.SaveIconAsync(icon);
#else
        private static Dictionary<int, int> Files(ItemIconCreator creator) => creator.Dictionary_1;
        private static Dictionary<int, ItemIcon> Memory(ItemIconCreator creator) => creator.Dictionary_0;
        private static string IndexPath(ItemIconCreator creator) => creator.String_1;
        private static int QueueCount(ItemIconCreator creator) => creator.Int_1;
        private static bool Rendering(ItemIconCreator creator) => creator.Bool_0;
        private static void SetRendering(ItemIconCreator creator, bool value) => creator.Bool_0 = value;
        private static Task Prepare(ItemIconCreator creator) => creator.method_0();
        private static Sprite Capture(ItemIconCreator creator, GameObject model, IntVec2 size, PreviewPivot pivot) => creator.method_4(model, in size, pivot);
        private static Task Save(ItemIconCreator creator, ItemIcon icon) => creator.method_5(icon);
#endif
    }
}
