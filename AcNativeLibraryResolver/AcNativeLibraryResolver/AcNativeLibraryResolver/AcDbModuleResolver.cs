/// AcDbModuleResolver.cs  
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license

/// AcDbModuleResolver Class
///
/// A minimal implementation of AcNativeLibraryResolver 
/// whose scope is limited to resolving the name of the 
/// AutoCAD database implementation dll (acdbXX.dll).
/// 
/// AcDbModuleResolver allows existing assemblies that import
/// API's from the acdbXX.dll in any AutoCAD Release, to work
/// on any subsequent AutoCAD release where the name of the
/// acdbXX.dll file differs, with no changes or recompilation 
/// needed.
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
/// The resolution is bi-directional and allows builds that
/// target a given AutoCAD release to also target older 
/// releases (back to AutoCAD 2025) with no code changes.
/// 
/// For example, given this:
/// 
///    [DllImport("acdb27.dll", ...)]
///    
/// When the same build runs on AutoCAD 2025, "acdb27.dll" 
/// will be resolved to "acdb25.dll".
/// 
/// In addition, the resolver looks for the special token
/// 'ACDB_DLL', and the wildcard patterns 'acdb##.dll', and 
/// 'acdb2#.dll', all of which will resolve to the acdb##.dll 
/// for the current release.
/// 
/// Of course, all of this implies that the APIs that are
/// being imported and used exist in all targeted releases
/// and have compatible signatures in all releases.
/// 
/// To enable AcDbModuleResolver, you only need to call it's
/// Initialize() method once, prior to calling any imported
/// API from acdbXX.dll. Initialize() is usually called from
/// an IExtensionApplication's Initialize() method.
/// 
/// AcDbModuleResolver requires .NET 8 (AutoCAD 2025) at minimum,
/// and is not supported on older framework versions or AutoCAD
/// releases that use them.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace AcMgdLib.Runtime
{
   public static class AcDbModuleResolver
   {
      internal const string ACDB_DLL_REGEX_PATTERN = @"^acdb\d{2}\.dll$";

      /// <summary>
      /// Can be used as the dllName argument to DllImport when importing
      /// from acdbXX.dll. Using this token explicity signals to this code
      /// that it should return the module handle for the loaded dll.
      /// </summary>
      public const string ACDB_DLL_TOKEN = "ACDB_DLL";
      
      static readonly HashSet<string> tokens = new HashSet<string>(
         ["acdb##.dll", "acdb2#.dll", ACDB_DLL_TOKEN], StringComparer.OrdinalIgnoreCase);
      
      static readonly Regex regex = new Regex(ACDB_DLL_REGEX_PATTERN, 
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
            Debug.WriteLine($"[DllImport(\"{dllName}\")] resolved to \"{acdbModule.ModuleName}\"");
            return acdbModule.BaseAddress;
         }

         /// Interpret "acad.exe" to imply the name of the current process
         /// executable, allowing consuming code to be used on verticals or
         /// toolsets that may have a different executable name:
         
         if(string.Equals(dllName, "acad.exe", StringComparison.OrdinalIgnoreCase))
            return Process.GetCurrentProcess().MainModule.BaseAddress;

         Debug.WriteLine($"Failed to resolve [DllImport(\"{dllName}\")] {new StackTrace(1, true).ToString()}");
         return IntPtr.Zero;
      }
   }


}