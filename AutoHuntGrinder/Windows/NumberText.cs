using System.Globalization;

namespace AutoHuntGrinder.Windows;

// Built once per value, so a number drawn every frame is not formatted again.
internal static class NumberText
{
    private static string[] numbers = [];

    public static string Of(int value)
    {
        if (value >= numbers.Length)
        {
            Grow(value + 1);
        }

        return numbers[value];
    }

    private static void Grow(int count)
    {
        var grown = new string[Math.Max(count, numbers.Length * 2)];
        Array.Copy(numbers, grown, numbers.Length);
        for (var index = numbers.Length; index < grown.Length; index++)
        {
            grown[index] = index.ToString(CultureInfo.InvariantCulture);
        }

        numbers = grown;
    }
}
