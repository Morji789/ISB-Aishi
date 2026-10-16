using System.Reflection;
using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
#if AISHI_SPT41
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Tables;
#else
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Services;
#endif

namespace Aishi;

#if AISHI_SPT41
[Injectable(TypePriority = OnLoadOrder.PostLoad + 2)]
public sealed class AishiHideoutRecipes(ModHelper modHelper, TemplateTable templates, HideoutTable hideout, WTTServerCommonLib.WTTServerCommonLib common) : IOnLoad
#else
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader + 2)]
public sealed class AishiHideoutRecipes(ModHelper modHelper, DatabaseService database, WTTServerCommonLib.WTTServerCommonLib common) : IOnLoad
#endif
{
#if AISHI_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
#else
    public Task OnLoad()
#endif
    {
#if AISHI_SPT41
        cancellationToken.ThrowIfCancellationRequested();
#else
        var templates = database.GetTemplates();
        var hideout = database.GetTables().Hideout;
#endif
        var root = Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "db");
        var folders = Directory.EnumerateDirectories(root).Select(folder => Path.Combine(folder, "Recipes"))
            .Append(Path.Combine(root, "CustomHideoutRecipes")).Where(Directory.Exists);
        var owned = new HashSet<MongoId>();
        foreach (var folder in folders)
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".json" or ".jsonc"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            var entries = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToArray()
                : [document.RootElement];
            foreach (var recipe in entries)
                owned.Add(recipe.GetProperty("_id").GetString()!);
        }

        var recipes = hideout.Production.Recipes;
        if (recipes == null)
            return Task.CompletedTask;
        var unavailable = recipes.Where(recipe => owned.Contains(recipe.Id) && recipe.Requirements?.Any(requirement =>
            requirement.Type == "QuestComplete" && (!requirement.QuestId.HasValue || !templates.Quests.ContainsKey(requirement.QuestId.Value))) == true).ToArray();
        foreach (var recipe in unavailable)
        {
            recipes.Remove(recipe);
            common.CustomHideoutRecipeService.GetExtendedRecipes().Remove(recipe.Id);
        }
        return Task.CompletedTask;
    }
}
