/// AcNativeLibraryResolver.cs  
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license

/// Revisions
/// 
/// 9-20-26: 
/// 
/// Major refactoring to eliminate the use of SetDllImportResolver(),
/// due to significant overhead it caused. That API was replaced with 
/// an AssemblyLoadContext.ResolvingUnmanagedDLL event handler, which
/// only notifies when a module cannot be found using default probing 
/// algorithim/rules. In contrast, the SetDllImportResolver callback
/// is preemptive, and is called before any default probing is done, 
/// to give the consumer the ability to redirect to a module other than
/// the one that would be chosen by default probing. That resulted in
/// many superfluous calls to the resolver callback. Since preemptively
/// redirecting existing dlls is not required in this use case, its 
/// overhead can be avoided. Note that SetDllImportResolver() can pose 
/// significant security risks, as it gives any loaded code the means to 
/// surruptitiously redirect loading of one .DLL to another .DLL.
/// 
/// Automatic mismatched release-dependent filename resolution:
/// 
/// When DllImport is used with a dll that does not exist in the current
/// product, and the filename ends with two numeric digits, those two
/// digits are replaced with the year/release number. So for example, 
/// if "acdb24.dll" is used, and the code is running on AutoCAD 2026,
/// the dllName will resolve to "acdb26.dll".
/// 
/// DllImport from acad.exe:
/// 
/// When "acad.exe" is used in a DllImport's dllName, it is replaced
/// with the name of the current process, enabling portability across
/// product variants that may not have the same executable filename.
/// 
/// Additional miscellaneous bugs were also resolved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.Runtime;

namespace AcMgdLib.Runtime
{
   /// <summary>
   /// AcNativeLibraryResolver Class
   /// 
   /// See README.MD for details
   /// </summary>

   public static class AcNativeLibraryResolver
   {
      const string ACDB_DLL_PATTERN = "acdb2#.dll";
      internal const string ACDB_DLL_TOKEN = "ACDB_DLL";
      internal const string ACDB_REGEX_PATTERN = @"^acdb\d{2}\.dll$";
      const string ACAD_EXE = "acad.exe";
      static volatile bool initialized;
      static readonly object lockHolder = new object();
      static readonly Regex acDbRegex = new Regex(ACDB_REGEX_PATTERN, RegexOptions.IgnoreCase | RegexOptions.Compiled);
      public static ProcessModule acDbModule;
      public static int acDbVersion = GetAcDbModuleVersion();
      public static readonly ProcessModule mainModule = Process.GetCurrentProcess().MainModule;
      static string acDbModuleName = $"acdb{acDbVersion}.dll";
      static readonly char v1 = acDbVersion.ToString()[0]; 
      static readonly char v2 = acDbVersion.ToString()[1]; 
      static volatile bool resolving = false;
      
      ///  Caches module path -> resolved ProcessModule
      static readonly ConcurrentDictionary<string, ProcessModule> modules =
          new ConcurrentDictionary<string, ProcessModule>();

      /// <summary>
      /// Must be called once to enable custom DllImport module resolution:
      /// </summary>
      
      public static void Initialize()
      {
         if(initialized)
            return;
         lock(lockHolder)
         {
            if(initialized)
               return;
            initialized = true;
            /// Add commonly-used wildcards matching
            /// acdbXX.dll and acad.exe to cache:
            AddLoadedModule("acdb2#.dll", acDbModule);
            AddLoadedModule("acdb##.dll", acDbModule);
            AddLoadedModule("acad.exe", mainModule);
            AssemblyLoadContext.Default.ResolvingUnmanagedDll += resolvingUnmanagedDll;
            /// Force loading of acutWcMatchEx() before
            /// attempting to resolve wildcard dllnames:
            NativeMethods.acutWcMatchEx("", "", true);
         }
         Debug.WriteLine($"AcNativeLibraryResolver Initialized (ThreadId = {Thread.CurrentThread.ManagedThreadId})");
      }

      static IntPtr resolvingUnmanagedDll(Assembly assembly, string libraryName)
      {
         /// Special handling:
         /// 
         /// acutWcMatchEx() is used to resolve wildcard dllnames,
         /// so we can't use a wildcard pattern in its DllImport
         /// attribute, because it's not loaded yet. So we use a
         /// special token that's recognized by Resolve(), which
         /// will simply return the already-loaded dll's module 
         /// handle without doing any pattern matching.
         
         if(IsEqual(libraryName, ACDB_DLL_TOKEN))
            return acDbModule.BaseAddress;

         if(resolving)
            return IntPtr.Zero;
         lock(lockHolder)
         {
            if(resolving)
               return IntPtr.Zero;
            resolving = true;
            try
            {
               return Resolve(libraryName, assembly);
            }
            finally
            {
               resolving = false;
            }
         }
      }

      /// <summary>
      /// Performs specialized resolution of the dllName argument 
      /// passed to a DllImport attribute.
      /// 
      /// Supported scenarios:
      /// 
      ///   1. Wcmatch-style wildcards. 
      ///      
      ///   If the dllName argument is a wildcard, it must match
      ///   exactly one and only one loaded module, or only one
      ///   module filename in the base directory. The wildcard
      ///   is replaced with the matching module's path.
      ///   
      ///   Wildcard matching aginst unloaded modules is limited
      ///   to the application base directory (where the process
      ///   executable is located). Other locations are NOT probed.
      ///   
      ///   2. Mismatched release-dependent module names.
      ///   
      ///   If the filename in the dllName argument ends with two 
      ///   numeric digits, and there is no module with that name
      ///   found, the two numeric digits are replaced with those
      ///   of the current product release (e.g., 25, 26, 27, etc). 
      ///   
      ///   Eg., a dllName argument of "acdb24.dll" will be replaced 
      ///   with "acdb25.dll" on AutoCAD 2025; or with "acdb26.dll" 
      ///   on AutoCAD 2026, and so on.
      ///   
      ///   3. Non-default executable path.
      ///   
      ///   If the dllName argument is "acad.exe", it always resolves 
      ///   to the current process executable's main module, regardless 
      ///   of what its filename is. Importing APIs from acad.exe makes
      ///   the importing library dependent on it and therefore cannot 
      ///   be used with accoreconsole.exe.
      ///   
      ///   4. Optimized 'hot-path' for acdbXX.dll
      ///   
      ///   Because importing from acdbXX.dll is the most-common use
      ///   case, the code is optimized to recognize wildcards that
      ///   match that dll's name, and will return its module handle 
      ///   with no further processing when those wildcards are used.
      /// 
      /// </summary>
      /// <param path="libraryName"></param>
      /// <param path="assembly"></param>
      /// <param path="searchPath"></param>
      /// <returns></returns>
      
      static IntPtr Resolve(string libraryName, Assembly assembly)
      {
         if(string.IsNullOrWhiteSpace(libraryName))
            return IntPtr.Zero;

         /// Special handling for "acad.exe", that always resolves to the
         /// current process executable. If libraryName is "acad.exe", we 
         /// return the handle of the main module, regardless of what its
         /// module/filename is. This makes code that imports APIs from
         /// the process executable/main module portable across different
         /// flavors/toolsets that may not have the same executable name.

         if(IsEqual(libraryName, ACAD_EXE))
         {
            return mainModule.BaseAddress;
         }

         ProcessModule module;

         /// Prioritize cached modules:   
        
         if(modules.TryGetValue(libraryName, out module))
         {
            return module.BaseAddress;
         }

         string altLibraryName = libraryName.ToString();
         string msg = $"[DllImport(\"{libraryName}\")]";
         IntPtr handle = IntPtr.Zero;
         string path = string.Empty;

         /// Try to resolve a mismatched, release-dependent module path 
         /// (e.g., "acdb24.dll" => "acdb25.dll" on AutoCAD 2025).
         /// This should work with both loaded and unloaded modules of any 
         /// type (e.g., *.dll, *.arx, *.crx, *.dbx)

         if(TryUpgradeFileVersion(ref altLibraryName))
         {
            if(TryLoad(altLibraryName, assembly, out handle, libraryName))
               return handle;
         }

         /// Look for a loaded module whose path matches 
         /// a libraryName wildcard pattern. This will not
         /// find the path of a library that is not loaded.

         var process = Process.GetCurrentProcess();
         process.Refresh();
         var matches = process.Modules
             .Cast<ProcessModule>()
             .Where(m => m.ModuleName.Matches(libraryName))
             .ToList();


         if(matches.Count > 1)
         {
            DebugWrite($"{nameof(Resolve)}(\"{libraryName}\"): Multiple ambiguous matches found.");
            return IntPtr.Zero;
         }

         if(matches.Count == 1)
         {
            module = matches[0];
            if(module != null)
            {
               AddLoadedModule(libraryName, module);
               return module.BaseAddress;
            }
         }

         /// No loaded module's name matches the pattern.
         /// Next, try looking in the base directory for 
         /// a matching file:

         string baseDir = AppDomain.CurrentDomain.BaseDirectory;

         var matchingFiles = Directory.EnumerateFiles(baseDir, "*", SearchOption.TopDirectoryOnly)
             .Where(path => Path.GetFileName(path).Matches(libraryName))
             .ToList();

         if(matchingFiles.Count > 1)
         {
            DebugWrite($"{msg}: Multiple ambiguous files found in BaseDirectory.");
            return IntPtr.Zero;
         }
         if(matchingFiles.Count == 1)
         {
            string matchedFilePath = matchingFiles[0];
            string matchedFileName = Path.GetFileName(matchedFilePath);

            // Attempt to load the module into the process using the DynamicLinker
            try
            {
               SystemObjects.DynamicLinker.LoadModule(matchedFileName, true, false);
               module = FindLoadedModule(matchedFilePath);
               if(module != null)
               {
                  AddLoadedModule(libraryName, module);
                  return module.BaseAddress;
               }
            }
            catch(System.Exception ex)
            {
               DebugWrite($"LoadModule(\"{matchedFilePath}\") failed: {ex.Message}");
               return IntPtr.Zero;
            }
         }
         DebugWrite($"{msg} Module name resolution failed.");
         return IntPtr.Zero;
      }

      static bool TryLoad(string libraryName, Assembly asm, out IntPtr handle, string key = null)
      {
         key ??= libraryName;
         string msg = $"[DllImport(\"{key}\")]";
         if(NativeLibrary.TryLoad(libraryName, asm, null, out handle))
         {
            var m = AddLoadedModule(key, handle);
            DebugWrite($"{msg} resolved to {m.FileName.FormatPath()}");
            return true;
         }
         return false;
      }

      static bool IsEqual(string a, string b)
      {
         if(a == null)
            return b == null;
         if(b == null)
            return false;
         return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
      }

      static ProcessModule FindLoadedModule(IntPtr handle)
      {
         var process = Process.GetCurrentProcess();
         process.Refresh();
         return process.Modules.Cast<ProcessModule>()
            .FirstOrDefault(m => m.BaseAddress == handle);
      }

      static ProcessModule FindLoadedModule(string path)
      {
         string filename = Path.GetFileName(path);
         var process = Process.GetCurrentProcess();
         process.Refresh();
         return process.Modules.Cast<ProcessModule>()
            .FirstOrDefault(m => IsEqual(m.ModuleName, filename));
      }

      static ProcessModule AddLoadedModule(string libraryName, IntPtr handle)
      {
         var module = FindLoadedModule(handle);
         if(module != null)
         {
            modules.TryAdd(libraryName, module);
         }
         return module;
      }

      static ProcessModule AddLoadedModule(string key, ProcessModule module)
      {
         modules.TryAdd(key, module);
         return module;
      }

      [Conditional("DEBUG")]
      static void DebugWrite(string msg)
      {
         Debug.WriteLine($"{nameof(AcNativeLibraryResolver)}: {msg}");
      }

      /// <summary>
      /// Replaces a mismatched release number in a release-dependent 
      /// filename with the current release number of the running product. 
      /// The current release number of the running product is stored in 
      /// the acDbVersion variable.
      /// 
      /// For example, given the filename "acdb24.dll", when running on
      /// AutoCAD 2026, this method will replace it with "acdb26.dll".
      /// 
      /// </summary>
      /// <param path="filename">The original filename, updated in-place 
      /// if matched.</param>
      /// <returns>True if a replacement was performed; otherwise, false.</returns>

      static bool TryUpgradeFileVersion(ref string filename)
      {
         if(string.IsNullOrWhiteSpace(filename))
            return false;

         ReadOnlySpan<char> span = filename.AsSpan().Trim();
#if DEBUG
         string input = new string(span);
#endif
         int dotIndex = span.LastIndexOf('.');
         if(dotIndex < 3)
            return false;
         char c1 = span[dotIndex - 2];
         char c2 = span[dotIndex - 1];
         if(!char.IsAsciiDigit(c1) || !char.IsAsciiDigit(c2))
            return false;
         if(c1 == v1 && c2 == v2)
         {
            Debug.WriteLine($"filename {filename} version already matches");
            return false;
         }
         filename = $"{filename.Substring(0, dotIndex - 2)}{v1}{v2}{filename.Substring(dotIndex)}";
#if DEBUG
         DebugWrite($"{nameof(TryUpgradeFileVersion)}({input}) => {filename}");
#endif
         return true;
      }

      public static ProcessModule GetAcDbModule()
      {
         if(acDbModule is null)
         {
            acDbModule = Process.GetCurrentProcess().Modules
                .Cast<ProcessModule>()
                .First(static m => acDbRegex.IsMatch(m.ModuleName));
         }
         return acDbModule;
      }

      static int GetAcDbModuleVersion()
      {
         var module = GetAcDbModule();
         if(module != null)
         {
            acDbModule = module;
            string moduleName = module.ModuleName;
            string versionStr = moduleName.Substring(4, 2);
            if(int.TryParse(versionStr, out int version))
               return version;
         }
         return 0;
      }

      public static string FormatPath(this string path)
      {
         if(Path.IsPathRooted(path))
         {
            return $"{Path.GetFileName(path)} in {Path.GetDirectoryName(path)}";
         }
         return path;
      }
   }


   internal static class NativeMethods
   {
      internal static bool Matches(this string str, string pattern, bool ignoreCase = true)
      {
         if(string.IsNullOrWhiteSpace(str) || string.IsNullOrWhiteSpace(pattern))
            return false;
         return acutWcMatchEx(str, pattern, ignoreCase);
      }

      /// <summary>
      /// 
      /// Because we do not want this library to have a dependence
      /// on AutoCAD (e.g., usable with AutoCAD Core Console), we 
      /// will P/Invoke acutWcmatchEx() directly, rather than use 
      /// Autodesk.AutoCAD.Internal.Utils, to avoid a dependence on 
      /// AcMgd.dll.
      /// 
      /// The Resolve() method recognizes the token used as the
      /// dllName in the DllImport attribute, and returns the
      /// handle of the containing module.
      /// </summary>

      [DllImport(AcNativeLibraryResolver.ACDB_DLL_TOKEN, 
         EntryPoint = "?acutWcMatchEx@@YA_NPEB_W0_N@Z",
         CallingConvention = CallingConvention.Cdecl,
         CharSet = CharSet.Unicode)]
      [return: MarshalAs(UnmanagedType.U1)] 
      internal static extern bool acutWcMatchEx(
         [MarshalAs(UnmanagedType.LPWStr)] string pattern,
         [MarshalAs(UnmanagedType.LPWStr)] string text,
         [MarshalAs(UnmanagedType.U1)] bool ignoreCase);
   }


}