using System;

public static class EveStatDisplayEditorChecks
{
    public static string Run()
    {
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
        double hp = 200000000d;
        for (int i = 0; i < 10; i++)
        {
            string before = EveStatEditor.FormatValue(hp);
            hp -= 10d;
            string after = EveStatEditor.FormatValue(hp);
            Check(before != after, "Inspector hid a committed HP change at 200M HP.");
            Check(double.Parse(after) == (double)hp, "Inspector did not show the stored HP exactly.");
        }
        Check(EveStatEditor.FormatValue(0d) == "0", "Zero display changed.");
        Check(double.Parse(EveStatEditor.FormatValue(1.25d)) == 1.25d, "Fractional stats display changed.");
        Check(double.Parse(EveStatEditor.FormatValue(-199999984d)) == -199999984d, "Large negative stats lost display precision.");
        Check(double.Parse(EveStatEditor.FormatValue(999999999999999.75d)) == 999999999999999.75d,
            "Mono custom double formatting hid a fractional HP change at 1E15.");
        Check(double.Parse(EveStatEditor.FormatValue(25.756d)) == 25.76d, "Crit percentage display did not round to two decimals.");
        return "PASS " + count + " Inspector formatting checks: ten visible HP changes at 200M, stored values preserved, zero/fractions/negative values.";
    }
}
