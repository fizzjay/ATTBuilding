namespace ATTBuilding
{
    // Single source of truth for console toggles, shared by both builds.
    // Written only from CommandParser results, read by the platform Builder
    // (tablet + finger ray). There is no grabbing anywhere in the mod.
    public static class BuildState
    {
        public static bool BuildMode;                 // "build mode on"
        public static bool Snapping;                  // "snapping true"
        public static bool LivePositionSnap;          // legacy toggle: tablet moves always snap live now
        public static float GridMetres = 0.25f;       // "build grid 0.25"
        public static float AngleStepDegrees = 15f;   // "build snapangle 15"
    }
}
