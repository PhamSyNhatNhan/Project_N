using System.Reflection;
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
#if UNITY_5_3_OR_NEWER
// Opt-in diagnostics are consumed by disposable native fixtures, not by the mod API.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]
#endif
