using System.Reflection;

// NOTE: no `using MelonLoader;` in this file, and `Main` is fully qualified
// below. MelonLoader itself contains a `MelonLoader.Main` type, so a bare
// typeof(Main) here (global namespace, both usings imported) is ambiguous
// between `ATTBuildingQuest.Main` and `MelonLoader.Main` -- CS0104.
[assembly: AssemblyVersion("0.2.0.0")]
[assembly: AssemblyTitle("ATTBuilding")]
[assembly: AssemblyProduct("ATTBuilding")]
[assembly: MelonLoader.MelonInfo(typeof(ATTBuildingQuest.ATTBuildingMod), "ATTBuilding", "0.2.0", "ATTBuilding")]
[assembly: MelonLoader.MelonGame("Alta", "Att_Quest")]
