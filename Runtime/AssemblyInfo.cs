using System.Runtime.CompilerServices;

// The game's runtime assembly reads the parser's internal helpers (SongInfo.Translated for the
// online song list); the Unity test assemblies may too.
[assembly: InternalsVisibleTo("OurTaiko.Runtime")]
[assembly: InternalsVisibleTo("OurTaiko.Tests")]
[assembly: InternalsVisibleTo("OurTaiko.PlayModeTests")]
// The dotnet test project (dotnet~/OurTaiko.TjaParser.Tests).
[assembly: InternalsVisibleTo("OurTaiko.TjaParser.Tests")]
