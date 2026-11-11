namespace ATTBuilding
{
    // Single source of truth for console toggles, shared by both builds.
    // Written only from CommandParser results, read by the platform BuildMode.
    public static class BuildState
    {
        public static bool BuildMode;                 // "build mode on"
        public static bool Snapping;                  // "snapping true"
        public static bool LivePositionSnap;          // snap position while held (default: rotation live, position on release)
        public static float GridMetres = 0.25f;       // "build grid 0.25"
        public static float AngleStepDegrees = 15f;   // "build snapangle 15"
        public static int HeldInstanceId;             // gameObject instance id currently held, 0 = none
    }
}
