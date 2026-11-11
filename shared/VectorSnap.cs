using UnityEngine;

namespace ATTBuilding
{
    // UnityEngine math exists with the same API in the Mono (PC) and
    // Il2Cpp (Quest) UnityEngine.CoreModule references, so this file is
    // linked unchanged into both projects (the ArmorUpdate shared/ pattern).
    public static class VectorSnap
    {
        public static Vector3 SnapPosition(Vector3 position, float gridMetres)
        {
            if (gridMetres <= 0f) return position;
            return new Vector3(
                SnapMath.Quantize(position.x, gridMetres),
                SnapMath.Quantize(position.y, gridMetres),
                SnapMath.Quantize(position.z, gridMetres));
        }

        // Quantise each Euler axis independently so the result is axis-aligned
        // to the chosen increment on every axis (straight, but still freely
        // orientable: 0/15/30/45... per axis).
        public static Quaternion SnapRotation(Quaternion rotation, float angleStepDegrees)
        {
            if (angleStepDegrees <= 0f) return rotation;
            Vector3 e = rotation.eulerAngles;
            return Quaternion.Euler(
                SnapMath.SnapAngleDegrees(e.x, angleStepDegrees),
                SnapMath.SnapAngleDegrees(e.y, angleStepDegrees),
                SnapMath.SnapAngleDegrees(e.z, angleStepDegrees));
        }
    }
}
