using Microsoft.Extensions.Logging;
using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Aishi;
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed class AishiLocaleFinalization(ModHelper modHelper, LocaleTable localeTable, ILogger<AishiLocaleFinalization> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        bool localeDiagnostics = false;
        try
        {
            localeDiagnostics = AishiLocaleData.LocaleDiagnosticsEnabled(root);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Aishi] Could not read config.jsonc logging options; locale diagnostics remain disabled.");
        }

        var supplemental = AishiLocaleData.ReadDefaults(root);
        foreach (var(language, locale)in localeTable.Global)
        {
            var custom = AishiLocaleData.ReadOverride(root, language);
            if (localeDiagnostics)
                logger.LogInformation("[Aishi locales] Language={Language}; fallback={Fallback}; custom={Custom}. Template and preset defaults fill missing keys; translations take precedence.", language, supplemental["en"].Count, custom?.Count ?? 0);
            locale.AddTransformer(current =>
            {
                AishiLocaleData.Apply(current, supplemental, language, custom);
                return current;
            });
        }

        return Task.CompletedTask;
    }
}
