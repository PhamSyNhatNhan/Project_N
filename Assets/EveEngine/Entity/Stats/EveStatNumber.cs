using System.Globalization;

namespace EveEngine
{
    public static class EveStatNumber
    {
        // Mono's custom double formatting first rounds to 15 significant digits.
        // Preserve round-trip digits before applying presentation-only decimal rounding.
        public static string Format(double value, CultureInfo culture = null)
        {
            culture = culture ?? CultureInfo.CurrentCulture;
            string exact = value.ToString("G17", CultureInfo.InvariantCulture);
            if (decimal.TryParse(exact, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal display))
                return display.ToString("0.##", culture);
            return value.ToString("0.##E+0", culture);
        }
    }
}
