using System.Reflection;

// NOTE: no `using MelonLoader;` in this file, and `Main` is fully qualified
// below. MelonLoader itself contains a `MelonLoader.Main` type, so a bare
// typeof(Main) here (global namespace, both usings imported) is ambiguous
// between `ATTBuilding.Main` and `MelonLoader.Main` -- CS0104.
[assembly: AssemblyVersion("0.4.16.0")]
[assembly: AssemblyTitle("ATTBuilding")]
[assembly: AssemblyProduct("ATTBuilding")]
[assembly: MelonLoader.MelonInfo(typeof(ATTBuilding.ATTBuildingMod), "ATTBuilding", "0.4.16", "ATTBuilding")]
[assembly: MelonLoader.MelonGame("Alta", "A Township Tale")]
