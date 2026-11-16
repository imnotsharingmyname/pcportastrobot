using System;

namespace AstroHybrid.App.Services
{
    // Vibration strength, shared by the app and the classic launcher (issue #36).
    // Stored in Build\astrohybrid-settings.json as "rumblepct" (percent, 0-200). "rumble" keeps the old
    // preset index (0 = Off, 1 = 40, 2 = 70, 3 = 100, 4 = 150) for files written by older builds.
    public static class RumbleSetting
    {
        public const int MaxPercent = 200;
        public const int Unset = -1;

        // The presets older builds wrote as an index, and that "rumble" still carries.
        public static readonly int[] LegacyPercents = { 0, 40, 70, 100, 150 };

        public static int ClampPercent(int percent)
        {
            if (percent < 0) return 0;
            return percent > MaxPercent ? MaxPercent : percent;
        }

        public static int FromLegacyIndex(int index)
        {
            if (index < 0) index = 0;
            if (index >= LegacyPercents.Length) index = LegacyPercents.Length - 1;
            return LegacyPercents[index];
        }

        // The stored percent wins; a file that only has the old index is mapped through the old presets.
        public static int EffectivePercent(int storedPercent, int legacyIndex)
        {
            return storedPercent >= 0 ? ClampPercent(storedPercent) : FromLegacyIndex(legacyIndex);
        }

        // The index an older launcher understands: the closest preset, and never "Off" for a strength above 0.
        public static int ToLegacyIndex(int percent)
        {
            percent = ClampPercent(percent);
            int best = 0;
            for (int i = 1; i < LegacyPercents.Length; i++)
            {
                if (Math.Abs(LegacyPercents[i] - percent) < Math.Abs(LegacyPercents[best] - percent))
                {
                    best = i;
                }
            }
            if (percent > 0 && LegacyPercents[best] == 0) best = 1;
            return best;
        }

        // "Stronger vibration on Switch-style pads" used to pass a flat 96 of 255 whatever the strength:
        // at low strengths the boost was louder than the strength itself, which is the constant buzzing
        // reported for Joy-Cons and Switch Pro pads. It now follows the chosen strength (100% = 96, the
        // old value; 70% = 67; 5% = 4).
        public static int SwitchFloor(int percent, bool boost)
        {
            return boost ? 96 * ClampPercent(percent) / 100 : 0;
        }
    }
}
