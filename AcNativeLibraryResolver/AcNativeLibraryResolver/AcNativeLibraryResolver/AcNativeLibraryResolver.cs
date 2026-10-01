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
/// with the moduleName of the current process, enabling portability across
/// product variants that may not have the same executable filename.
/// 
/// Supported file types:
/// 
/// If a module of any type is already loaded, its module handle will
/// be returned. However, for unloaded modules, implicit loading is 
/// limited to .dll and .exe files (.dbx files have not been tested). 
/// In the shipping base AutoCAD product, there are currently no known 
/// .arx/.crx libraries with release-dependent filenames.
/// 
/// 
/// Additional miscellaneous bugs were also resolved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;

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
      static readonly Regex regex = new Regex(ACDB_REGEX_PATTERN, 
         RegexOptions.IgnoreCase | RegexOptions.Compiled);
      static ProcessModule acDbModule;
      static int acDbVersion = AcDbModuleVersion;
      static readonly ProcessModule mainModule = Process.GetCurrentProcess().MainModule;
      static string acDbModuleName = $"acdb{acDbVersion}.dll";
      static readonly char v1 = acDbVersion.ToString()[0]; 
      static readonly char v2 = acDbVersion.ToString()[1]; 
      static volatile bool resolving = false;
      static int acdbModuleVersion;
      static readonly HashSet<string> moduleExtensions = 
         new ([".exe", ".dll", ".arx.", ".crx", ".dbx"], StringComparer.OrdinalIgnoreCase);

      ///  Caches original libraryPath argument -> resolved ProcessModule
      ///  There can be multiple entries for the same module.
      static readonly ConcurrentDictionary<string, ProcessModule> modules =
          new ConcurrentDictionary<string, ProcessModule>();

      /// <summary>
      /// Must be called once to enable specialized 
      /// DllImport module resolution, prior to calling
      /// any dependent imported native API.
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
            AddModuleAlias("acdb2#.dll", AcDbModule);
            AddModuleAlias("acdb##.dll", AcDbModule);
            AddModuleAlias("acad.exe", mainModule);
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
            return AcDbModule.BaseAddress;

         if(resolving)
         {
            DebugWrite($"Reentered. libraryName = {libraryName}, Assembly = {assembly?.GetName().Name ?? "null"}");
            return IntPtr.Zero;
         }
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
      ///   Caveat:
      ///   
      ///   While full wcmatch-style wildcard support is provided,
      ///   it is strongly recommended that wildcards be limited
      ///   to '?' and '#' (match numeric digit) only, because of
      ///   the potential of ambiguous matches.
      ///   
      ///   2. Mismatched release-dependent module names.
      ///   
      ///   If the filename in the dllName argument ends with two 
      ///   numeric digits, and there is no module with that moduleName
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
      ///   be used with product variants that may have a different
      ///   executable moduleName.
      ///   
      ///   4. Optimized 'hot-path' for acdbXX.dll
      ///   
      ///   Because importing from acdbXX.dll is the overwhelmingly most-
      ///   common use case, the code is optimized to recognize wildcards 
      ///   that match that dll's moduleName, and will return its module handle 
      ///   with no further processing when those wildcards are used.
      /// 
      /// </summary>
      /// <param path="pattern">the dllName argument to a DllImport attribute</param>
      /// <param path="assembly">the assembly that contains the DllImport attribute</param>
      /// <returns>The handle of the resolve module, or IntPtr.Zero if a module 
      /// could not be resolved.</returns>
      
      static IntPtr Resolve(string pattern, Assembly assembly)
      {

         if(string.IsNullOrWhiteSpace(pattern))
            return IntPtr.Zero;

         /// Special handling for "acad.exe", that always resolves to the
         /// current process executable. If pattern is "acad.exe", we 
         /// return the handle of the main module, regardless of what its
         /// module/filename is. This makes code that imports APIs from
         /// the process executable/main module portable across different
         /// product variants that may not have the same executable moduleName.

         if(IsEqual(pattern, ACAD_EXE))
         {
            return mainModule.BaseAddress;
         }

         ProcessModule module;

         /// Prioritize cached modules:   

         string msg = $"[DllImport(\"{pattern}\")]";

         if(modules.TryGetValue(pattern, out module))
         {
            Debug.WriteLine($"{msg} resolved to {module.ModuleName}");
            return module.BaseAddress;
         }

         string normalizedName = pattern.ToString();
         IntPtr handle = IntPtr.Zero;
         string path = string.Empty;
         var process = Process.GetCurrentProcess();
         process.Refresh();
         var map = process.Modules.Cast<ProcessModule>().ToDictionary(m => m.ModuleName);
         var allModules = new HashSet<string>(map.Keys, StringComparer.OrdinalIgnoreCase);
         /// Holds the names of loaded modules, and unloaded
         /// modules in the base directory:
         allModules.UnionWith(FindFiles("*.dll,*.arx,*.crx,*.dbx"));

         /// Look for a loaded or unloaded module whose ModuleName 
         /// matches the wildcard pattern. 

         var matches = allModules.Where(name => name.Matches(pattern)).ToArray();

         /// The check for ambiguous matches must be made across
         /// all modules, both loaded and unloaded (in the base
         /// directory):
         
         if(matches.Length > 1)
         {
            DebugWrite($"\"{pattern}\": Multiple ambiguous matches found.");
            return IntPtr.Zero;
         }

         /// A single match was found, which may or may not be loaded:
         if(matches.Length == 1)
         {
            return TryGetOrLoadModule(matches[0], pattern);
         }

         /// Try to resolve a mismatched, release-dependent module name 
         /// (e.g., "acdb24.dll" => "acdb25.dll" on AutoCAD 2025).

         if(TryNormalizeVersionedName(ref normalizedName))
         {
            return TryGetOrLoadModule(normalizedName, pattern);
         }

         DebugWrite($"{msg} Module name resolution failed.");
         return IntPtr.Zero;

         IntPtr TryGetOrLoadModule(string name, string pattern)
         {
            ProcessModule module;
            if(map.TryGetValue(name, out module))
               return AddModuleAlias(pattern, module);
            if(TryLoad(name, assembly, out handle, pattern))
               return handle;
            DebugWrite($"{msg} => {name} Module name resolution failed.");
            return IntPtr.Zero;
         }
      }


      static bool TryLoad(string libraryName, Assembly asm, out IntPtr handle, string key = null)
      {
         key ??= libraryName;
         string msg = $"[DllImport(\"{key}\")]";
         if(NativeLibrary.TryLoad(libraryName, asm, null, out handle) && handle != IntPtr.Zero)
         {
            var module = AddModuleAlias(key, handle);
            if(module != null)
            {
               DebugWrite($"{msg} resolved to {module.FileName.FormatPath()}");
               return true;
            }
            else
            {
               DebugWrite($"{msg} could not be resolved");
            }
         }
         return false;
      }

      /// <summary>
      /// For some unexplained reason, HostApplicationServices.FindFile("acdb27.dll", null, FindFileHint.Default)
      /// throws eFilerError. This does not.
      /// </summary>
      /// <param moduleName="name"></param>
      /// <returns></returns>
      
      static string FindFile(string name)
      {
         var result = new StringBuilder(1024);
         int status = NativeMethods.acedFindFile(name, result, (ulong)result.Capacity);
         return (status == 5100) ? result.ToString() : null;
      }

      /// <summary>
      /// Find all files in a folder matching a specified wcmatch-style wildcard
      /// </summary>
      /// <param moduleName="path"></param>
      /// <param moduleName="pattern"></param>
      /// <returns></returns>
      
      static string[] FindFiles(string path, string pattern)
      {
         if(!Directory.Exists(path))
            return Array.Empty<string>();
         return Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).Matches(pattern))
            .ToArray();
      }

      static string[] FindFiles(string pattern)
      {
         return FindFiles(AppDomain.CurrentDomain.BaseDirectory, pattern);
      }

      static bool IsEqual(string a, string b)
      {
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

      static ProcessModule AddModuleAlias(string key, nint handle = 0)
      {
         ProcessModule module = null;
         if(handle == IntPtr.Zero)
            module = FindLoadedModule(key);
         else
            module = FindLoadedModule(handle);
         if(module != null)
         {
            modules.TryAdd(key, module);
         }
         return module;
      }

      static IntPtr AddModuleAlias(string key, ProcessModule module)
      {
         if(modules.TryAdd(key, module))
            Debug.WriteLine($"[DllImport(\"{key}\")] resolved to {module.ModuleName}");
         return module.BaseAddress;
      }

      [Conditional("DEBUG")]
      static void DebugWrite(string msg, [CallerMemberName] string member = "")
      {
         if(!string.IsNullOrEmpty(member))
            member = $".{member}()";
         else
            member = "";
         Debug.WriteLine($"{nameof(AcNativeLibraryResolver)}{member}: {msg}");
      }

      /// <summary>
      /// Replaces a mismatched release number in a release-dependent 
      /// filename with the current release number of the running product. 
      /// The current release number of the running product is stored in 
      /// the AcDbModuleVersion variable.
      /// 
      /// For example, given the filename "acdb24.dll", when running on
      /// AutoCAD 2026, this method will replace it with "acdb26.dll".
      /// 
      /// </summary>
      /// <param path="filename">The original filename, updated in-place 
      /// if matched.</param>
      /// <returns>True if a replacement was performed; otherwise, false.</returns>

      static bool TryNormalizeVersionedName(ref string filename)
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
         filename = $"{filename.Substring(0, dotIndex - 2)}{v1}{v2}{filename.Substring(dotIndex)}";
#if DEBUG
         DebugWrite($"{input} => {filename}");
#endif
         return true;
      }

      static ProcessModule AcDbModule
      {
         get
         {
            if(acDbModule is null)
            {
               acDbModule = Process.GetCurrentProcess()
                  .Modules.Cast<ProcessModule>()
                  .First(static m => regex.IsMatch(m.ModuleName));
            }
            return acDbModule;
         }
      }

      static int AcDbModuleVersion
      {
         get
         {
            if(acdbModuleVersion < 1)
            {
               var module = AcDbModule;
               if(module != null)
               {
                  string moduleName = module.ModuleName;
                  string versionStr = moduleName.Substring(4, 2);
                  if(int.TryParse(versionStr, out int version))
                     acdbModuleVersion = version;
               }
            }
            return acdbModuleVersion;
         }
      }

      public static string FormatPath(this string path)
      {
         if(Path.IsPathRooted(path))
         {
            return $"{Path.GetFileName(path)} (in {Path.GetDirectoryName(path)}\\)";
         }
         return path;
      }

   }


   internal static partial class NativeMethods
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
      /// Autodesk.AutoCAD.Internal.Utils.WcMatchEx(), to avoid a 
      /// dependence on AcMgd.dll.
      /// 
      /// The Resolve() method recognizes the token used as the
      /// dllName in the DllImport attribute, and returns the
      /// handle of the acdbXX.dll module.
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

      [DllImport(
          "accore.dll",
          CallingConvention = CallingConvention.Cdecl,
          CharSet = CharSet.Unicode,
          EntryPoint = "?acedFindFile@@YAHPEB_WPEA_W_K@Z",
          ExactSpelling = true)]
      internal static extern int acedFindFile(
          string fileName,
          StringBuilder result,
          ulong bufferLength);

   }


}