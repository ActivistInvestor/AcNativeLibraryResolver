/// AcDbModuleResolver.cs  
/// 
/// ActivistInvestor / Tony T
/// 
/// Distributed under the terms of the MIT license

/// AcDbModuleResolver Class
///
/// A minimal implementation of AcNativeLibraryResolver 
/// that is limited to resolving the name of the AutoCAD 
/// database implementation dll (acdbXX.dll).
/// 
/// This code will resolve DllImport dllName arguments
/// that reference 'acdb##.dll', where '##' is any two
/// numeric digits, and resolve it to the name of that
/// dll for the current product the code is running on.
/// 
/// For example:
/// 
///   [DllImport("acdb24.dll", ...)]
///  
/// When the above is run on AutoCAD 2025, the resolver
/// will return the module handle of acdb25.dll. When run 
/// on AutoCAD 2026, the resolver returns the handle of
/// acdb26.dll, and so forth. The dllName argument to the
/// DllImport attribute can be any string that starts with
/// 'acdb', followed by any two numeric digits, and it will 
/// be resolved to the correct module based on the product
/// release on which the code is running.
/// 
/// In addition, the resolver looks for the special token
/// 'ACDB_DLL', and the wildcard patterns 'acdb##.dll', and 
/// 'acdb2#.dll', all of which will resolve to the acdb##.dll 
/// for the current release.
/// 
/// AcDbModuleResolver allows existing assemblies that import
/// API's from the acdbXX.dll in any AutoCAD Release, to work
/// on any subsequent AutoCAD release where the name of the
/// acdbXX.dll file differs, with no changes or recompilation 
/// needed.
/// 
/// To enable AcDbModuleResolver, you only need to call it's
/// Initialize() method once, prior to calling any imported
/// API from acdbXX.dll. Initialize() is usually called from
/// an IExtensionApplication's Initialize() method.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace AcMgdLib.Runtime
{
   public static class AcDbModuleResolver
   {
      internal const string ACDB_REGEX_PATTERN = @"^acdb\d{2}\.dll$";
      static readonly HashSet<string> tokens = new HashSet<string>(
         ["acdb##.dll", "acdb2#.dll", "ACDB_DLL"], StringComparer.OrdinalIgnoreCase);
      
      static readonly Regex regex = new Regex(ACDB_REGEX_PATTERN, 
         RegexOptions.IgnoreCase | RegexOptions.Compiled);

      static readonly ProcessModule acdbModule = Process.GetCurrentProcess().Modules
         .Cast<ProcessModule>()
         .First(static m => regex.IsMatch(m.ModuleName));

      public static void Initialize()
      {
         // dummy method to trigger static constructor
         // which does the actual initialization.
      }

      static AcDbModuleResolver()
      {
         AssemblyLoadContext.Default.ResolvingUnmanagedDll += resolve;
      }

      static IntPtr resolve(Assembly assembly, string dllName)
      {
         if(regex.IsMatch(dllName) || tokens.Contains(dllName))
         {
            Debug.WriteLine($"[DllImport(\"{dllName}\")] resolved to {acdbModule.ModuleName}");
            return acdbModule.BaseAddress;
         }

         if(string.Equals(dllName, "acad.exe", StringComparison.OrdinalIgnoreCase))
            return Process.GetCurrentProcess().MainModule.BaseAddress;

         Debug.WriteLine($"Failed to resolve [DllImport(\"{dllName}\",...)]");
         return IntPtr.Zero;
      }
   }


}