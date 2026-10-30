using System.Globalization;
using System.Resources;

namespace FestaScenarioAnalyzer;

internal static class FestaText
{
    static readonly ResourceManager Ui = new("FestaScenarioAnalyzer.i18n.Strings", typeof(FestaText).Assembly);
    static readonly ResourceManager Errors = new("FestaScenarioAnalyzer.i18n.Errors", typeof(FestaText).Assembly);

    public static string Get(string key, params object[] values)
        => string.Format(CultureInfo.CurrentCulture,
            Ui.GetString(key, CultureInfo.CurrentUICulture) ?? throw new InvalidOperationException($"Missing resource: {key}"), values);

    public static string Error(string key, params object[] values)
        => string.Format(CultureInfo.CurrentCulture,
            Errors.GetString(key, CultureInfo.CurrentUICulture) ?? throw new InvalidOperationException($"Missing resource: {key}"), values);
}
