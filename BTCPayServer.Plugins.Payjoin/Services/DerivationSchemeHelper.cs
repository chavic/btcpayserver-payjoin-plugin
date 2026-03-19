using System.Text.RegularExpressions;

namespace BTCPayServer.Plugins.Payjoin.Services;

internal static class DerivationSchemeHelper
{
    public static DerivationSchemeSettings Parse(string derivationScheme, BTCPayNetwork network)
    {
        var parser = new DerivationSchemeParser(network);

        if (Regex.IsMatch(derivationScheme, @"\(.*?\)"))
        {
            return parser.ParseOD(derivationScheme);
        }

        var strategy = parser.Parse(derivationScheme);
        return new DerivationSchemeSettings(strategy, network);
    }
}
