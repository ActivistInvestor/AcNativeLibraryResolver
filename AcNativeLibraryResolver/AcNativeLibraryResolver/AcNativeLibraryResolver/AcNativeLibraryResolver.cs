/// AcNativeLibraryResolver47.cs  
/// 
/// Activist Investor / Tony T
/// 
/// Distributed under the terms of the MIT license

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.Internal;

namespace AcMgdLib.Runtime
{
   /// <summary>
   /// AcNativeLibraryResolver Class
   /// 
   /// AcNativeLibraryResolver is a utility class that provides 
   /// a mechanism for dynamically resolving native library P/Invoke
   /// imports (DllImport) in .NET assemblies, by enabling the use
   /// of AutoCAD wcmatch-style wildcard patterns in the library name.
   /// 
   /// Using this class, you can specify a wildcard pattern for the 
   /// dll name in your DllImport attribute, and the resolver will 
   /// attempt to locate the corresponding loaded module in the current 
   /// process.
   /// 
   /// The primiary use case is for calling native APIs that reside in
   /// DLLs that have release-dependent filenames.
   /// 
   /// The following is a list of common AutoCAD DLLs that have release-
   /// dependent names, and the wildcard patterns that can be used to
   /// resolve them to the correct file for the AutoCAD release which the
   /// code is running on, from AutoCAD 2025 and later. The '#' character 
   /// in the pattern is a wildcard that matches a single digit, which is 
   /// the release number of the AutoCAD DLL. Note that the files listed 
   /// may not be included with all supported releases (AutoCAD 2025 or
   /// later).
   /// 
   /// AutoCAD 2025 filename      Recommended Wildcard pattern
   /// =======================================================
   /// adui25.dll                 adui2#.dll
   /// acdb25.dll                 acdb2#.dll 
   /// AcDimX25.dll               AcDimX2#.dll
   /// acge25.dll                 acge2#.dll
   /// acgex25.dll                acgex2#.dll
   /// AcGradient25.dll           AcGradient2#.dll
   /// AcPersSubentNaming25.dll   AcPersSubentNaming2#.dll
   /// acui25.dll                 acui2#.dll
   /// AcWebDAV25.dll             AcWebDAV2#.dll
   /// atlst25.dll                atlst2#.dll
   /// hcreg25.dll                hcreg2#.dll
   /// heidi25.dll                heidi2#.dll
   /// modlr25.dll                modlr2#.dll
   /// oletohdi25.dll             oletohdi2#.dll
   /// plotcfg25.dll              plotcfg2#.dll
   /// pm25.dll                   pm2#.dll
   /// pmutil25.dll               pmutil2#.dll
   /// regacad25.dll              regacad2#.dll
   /// 
   /// Automatic resolution of DllImport dllnames.
   /// 
   /// In addition to supporting the use of wildcards in the DllImport
   /// attribute's dllName argument, this library will automatically 
   /// replace mismatched version-dependent filenames with the correct
   /// filename for the AutoCAD release the code is running on.
   /// 
   /// For example, given this:
   /// 
   ///    [DllImport("acdb24.dll", ...)]
   ///    
   /// When running on any release of AutoCAD (starting with AutoCAD 2025 
   /// or later), the dllName argument in the above DllImport attribute 
   /// will be automatically replaced as follows:
   /// 
   ///    AutoCAD Release      Replacement filename
   ///    =========================================
   ///    2025                 "acdb25.dll"
   ///    2026                 "acdb26.dll"
   ///    2027                 "acdb27.dll"
   ///    
   /// Automatic version-dependent filename resolution works for any of 
   /// the above AutoCAD dlls having version-dependent names.
   /// 
   /// </summary>

   public static class AcNativeLibraryResolver
   {
      const string ACDB_DLL = "acdb2#.dll";
      static ConcurrentDictionary<Assembly, bool> knownAssemblies = new();
      static bool initialized;
      static readonly HashSet<Assembly> _registeredAssemblies = new HashSet<Assembly>();
      static readonly object _lock = new object();

      ///  Caches pattern -> resolved module file path (or empty string for negative/ambiguous matches)
      static readonly ConcurrentDictionary<string, string> modulePaths =
          new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      /// <summary>
      /// Registers the dynamic DllImport resolver for the calling assembly.
      /// If the Initialize() method is called, calling this is not required.
      /// </summary>

      public static void Register(Assembly assembly = null)
      {
         assembly ??= Assembly.GetCallingAssembly();
         if(!IsExempt(assembly))
         {
            lock(_lock)
            {
               if(_registeredAssemblies.Add(assembly))
               {
                  NativeLibrary.SetDllImportResolver(assembly, Resolve);
                  DebugWrite($"Registered assembly {assembly.GetName().Name} for DllImport resolution");
               }
            }
         }
      }

      /// <summary>
      /// Registers all currently- and subsequently-loaded custom 
      /// assemblies for DllImport resolution. .NET Framework and 
      /// AutoCAD assemblies are not registered.
      /// </summary>

      public static void Initialize()
      {
         if(initialized)
            return;
         initialized = true;
         foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
         {
            Register(assembly);
         }
         AppDomain.CurrentDomain.AssemblyLoad += assemblyLoad;
      }

      static void assemblyLoad(object sender, AssemblyLoadEventArgs args)
      {
         Register(args.LoadedAssembly);
      }

      static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
      {
         if(string.IsNullOrWhiteSpace(libraryName))
            return IntPtr.Zero;

         string msg = $"[DllImport(\"{libraryName}\")]";
         string name = assembly.GetName().Name;

         ///  Fetch from cache or execute FindLoadedModuleFilePath only on cache miss
         string path = modulePaths.GetOrAdd(libraryName, GetLoadedModuleFilename);
         
         if(!string.IsNullOrWhiteSpace(path))
         {
            lock(_lock)
            {
               if(NativeLibrary.TryLoad(path, assembly, searchPath, out IntPtr handle))
               {
                  DebugWrite($"{msg} resolved to {path}");
                  return handle;
               }
            }
         }

         DebugWrite($"{msg} Failed to resolve to a loaded module.");

         ///  Return IntPtr.Zero to let standard .NET 
         ///  runtime resolution handle non-matching names
         return IntPtr.Zero;
      }

      /// <summary>
      /// Finds a loaded module matching the specified wildcard pattern.
      /// Returns null if zero or multiple (ambiguous) matches exist.
      /// 
      /// The pattern argument must match one and only one loaded module name 
      /// (case-insensitive) for a successful match. If multiple matches are 
      /// found, null is returned to indicate ambiguity.
      /// </summary>
      
      static ProcessModule FindLoadedModule(string pattern, bool nested = false)
      {
         var matches = Process.GetCurrentProcess().Modules
             .Cast<ProcessModule>()
             .Where(m => Utils.WcMatchEx(m.ModuleName, pattern, true));

         if(matches.Skip(1).Any())     ///  Multiple ambiguous matches found.
            return null;

         if(!matches.Any() && !nested)
         {
            if(TryReplaceFileVersion(ref pattern))
            {
               return FindLoadedModule(pattern, true);
            }
            return null;
         }

         return matches.FirstOrDefault();
      }

      static string GetLoadedModuleFilename(string pattern)
      {
         return FindLoadedModule(pattern)?.FileName ?? string.Empty;
      }

      /// <summary>
      /// Loaded assemblies are checked to determine if they are 
      /// framework or AutoCAD assemblies, or dynamically-generated. 
      /// If so, they are not registered for DllImport resolution.
      /// </summary>

      static bool IsExempt(Assembly asm)
      {
         if(asm is null)
            return true;
         if(asm.IsDynamic)
            return true;
         bool result = false;
         if(knownAssemblies.TryGetValue(asm, out result))
            return result;
         var att = asm.GetCustomAttribute<AssemblyCompanyAttribute>();
         if(att != null)
         {
            string company = att.Company;
            result = company.StartsWith("Autodesk, Inc")
               || company.StartsWith("Microsoft Corporation");
         }
         knownAssemblies.TryAdd(asm, result);
         return result;
      }

      [Conditional("DEBUG")]
      static void DebugWrite(string msg)
      {
         Debug.WriteLine($"{nameof(AcNativeLibraryResolver)}: {msg}");
      }

      public static readonly int AcDllVersion = GetAcDllVersion();

      /// <summary>
      /// Replaces the numeric version in a version-dependent filename
      /// with the current version of the running product. The version
      /// of the running product is in the AcDllVersion variable.
      /// 
      /// For example, given the filename "acdb24.dll", when running on
      /// AutoCAD 2026, this method will replace it with "acdb26.dll".
      /// 
      /// </summary>
      /// <param name="pattern">The original filename, updated in-place 
      /// if matched.</param>
      /// <returns>True if a replacement was performed; otherwise, false.</returns>
      
      static bool TryReplaceFileVersion(ref string pattern)
      {
         if(string.IsNullOrWhiteSpace(pattern) || AcDllVersion <= 0)
            return false;

         ReadOnlySpan<char> span = pattern.AsSpan().Trim();
#if DEBUG
         string input = new string(span);
#endif
         int dotIndex = span.LastIndexOf('.');

         // Must have an extension and at least 2 characters
         // preceding it for version digits
         if(dotIndex < 2)
            return false;

         int d1Idx = dotIndex - 2;
         int d2Idx = dotIndex - 1;

         // Ensure the two characters prior to the extension are numeric digits
         if(!char.IsAsciiDigit(span[d1Idx]) || !char.IsAsciiDigit(span[d2Idx]))
            return false;

         int targetD1 = (AcDllVersion / 10) % 10 + '0';
         int targetD2 = AcDllVersion % 10 + '0';

         // Avoid allocating if the pattern already has the target version digits
         if(span[d1Idx] == targetD1 && span[d2Idx] == targetD2)
            return false;

         pattern = string.Create(pattern.Length, (pattern, d1Idx, d2Idx, c1: (char)targetD1, c2: (char)targetD2), (buf, state) =>
         {
            state.pattern.AsSpan().CopyTo(buf);
            buf[state.d1Idx] = state.c1;
            buf[state.d2Idx] = state.c2;
         });

#if DEBUG
         DebugWrite($"TryReplaceFileVersion({input}) => {pattern}");
#endif

         return true;
      }

      static int GetAcDllVersion()
      {
         var matches = Process.GetCurrentProcess().Modules
             .Cast<ProcessModule>()
             .Where(m => Utils.WcMatchEx(m.ModuleName, ACDB_DLL, true));
         if(matches.Any())
         {
            string moduleName = matches.First().ModuleName;
            string versionStr = moduleName.Substring(4, 2);
            if(int.TryParse(versionStr, out int version))
               return version;
         }
         return 0;
      }


   }
}