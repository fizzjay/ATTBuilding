using System;

namespace ATTBuilding
{
    // Pure scalar snapping: no Unity, no game types. Compiles identically into
    // the PC Mono build and the Quest IL2CPP build, and is mirrored by tests/.
    public static class SnapMath
    {
        // Nearest multiple of step. step <= 0 disables snapping (value unchanged).
        public static float Quantize(float value, float step)
        {
            if (step <= 0f || float.IsNaN(value) || float.IsInfinity(value)) return value;
            return (float)Math.Round(value / step) * step;
        }

        // Snap an angle to a set increment (degrees), normalised to [0, 360).
        // This is what guarantees a placed piece is "straight": 0/15/30/45...
        public static float SnapAngleDegrees(float degrees, float stepDegrees)
        {
            if (stepDegrees <= 0f) return NormalizeDegrees(degrees);
            return NormalizeDegrees(Quantize(NormalizeDegrees(degrees), stepDegrees));
        }

        public static float NormalizeDegrees(float degrees)
        {
            float d = degrees % 360f;
            if (d < 0f) d += 360f;
            return d;
        }

        public static bool ParseBool(string text, bool fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            switch (text.Trim().ToLowerInvariant())
            {
                case "true": case "on": case "1": case "yes": return true;
                case "false": case "off": case "0": case "no": return false;
                default: return fallback;
            }
        }
    }
}
