/// AcNativeLibraryResolver.cs  
/// 
/// Activist Investor / Tony T
/// 
/// Distributed under the terms of the MIT license

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Internal;

namespace AcMgdLib.Runtime
{
   /// <summary>
   /// AcNativeLibraryResolver Class
   /// 
   /// See README.MD for details
   /// </summary>

   public static class AcNativeLibraryResolver
   {
      const string ACDB_DLL = "acdb2#.dll";
      static volatile bool initialized;
      static readonly HashSet<Assembly> registeredAssemblies = new HashSet<Assembly>();
      static readonly object lockHolder = new object();
      public static readonly int AcDbVersion = GetAcDbVersion();
      static readonly char v1 = (char)((AcDbVersion / 10) % 10 + '0');
      static readonly char v2 = (char)(AcDbVersion % 10 + '0');

      ///  Caches module name -> resolved module file path (or empty string for negative/ambiguous matches)
      static readonly ConcurrentDictionary<string, string> modulePaths =
          new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

      /// <summary>
      /// Registers the dynamic DllImport resolver for the specified assembly,
      /// or the calling assembly if no assembly was specified.
      /// 
      /// If the Initialize() method was called, calling this is not required.
      /// </summary>

      public static void Register(Assembly assembly = null)
      {
         assembly ??= Assembly.GetCallingAssembly();
         lock(lockHolder)
         {
            if(registeredAssemblies.Contains(assembly))
               return;
            if(!IsExempt(assembly))
            {
               NativeLibrary.SetDllImportResolver(assembly, Resolve);
               registeredAssemblies.Add(assembly);
               DebugWrite($"Registered assembly {assembly.GetName().Name} for dynamic DllImport resolution");
            }
         }
      }

      /// <summary>
      /// Registers all currently- and subsequently-loaded custom 
      /// assemblies for DllImport resolution. .NET Framework and 
      /// AutoCAD assemblies are not registered.
      /// 
      /// Referencing assemblies that are dependent on the services
      /// provided by this library must call this method once before
      /// any imported native API marked with the DllImport attribute
      /// is called. If this method is called, the Register() method
      /// does not need to be called for the referencing assembly.
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
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
               Register(assembly);
            }
            AppDomain.CurrentDomain.AssemblyLoad += assemblyLoad;
         }

         Debug.WriteLine($"AcNativeLibraryResolver Initialized (ThreadId = {Thread.CurrentThread.ManagedThreadId})");
      }

      private static void assemblyLoad(object sender, AssemblyLoadEventArgs args)
      {
         Register(args.LoadedAssembly);
      }

      /// <summary>
      /// TODO: Fails when module is not loaded.
      /// </summary>
      /// <param name="libraryName"></param>
      /// <param name="assembly"></param>
      /// <param name="searchPath"></param>
      /// <returns></returns>

      static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
      {
         if(string.IsNullOrWhiteSpace(libraryName))
            return IntPtr.Zero;

         string originalLibraryName = libraryName.ToString();
         string libraryName2 = libraryName.ToString();
         string msg = $"[DllImport(\"{libraryName}\")]";
         string name = assembly.GetName().Name;
         IntPtr handle = IntPtr.Zero;
         string path = string.Empty;

         /// Handle cases involving no wildcards or 
         /// mismatched release-dependent dll names:

         if(TryLoad(libraryName, assembly, searchPath, out handle))
            return handle;

         /// Try to resolve mismatched release-dependent dll name 
         /// (e.g., "acdb24.dll" => "acdb25.dll" on AutoCAD 2025 or later)
         
         if(TryReplaceFileVersion(ref libraryName2))
         {
            if(TryLoad(libraryName2, assembly, searchPath, out handle, originalLibraryName))
               return handle;
         }

         ///  Fetch from cache or execute GetLoadedModuleFilename on cache miss
         path = modulePaths.GetOrAdd(libraryName, GetLoadedModuleFilename);

         if(!string.IsNullOrWhiteSpace(path))
         {
            if(TryLoad(path, assembly, searchPath, out handle, originalLibraryName))
               return handle;
         }

         DebugWrite($"{msg} Failed to resolve to a loaded module.");

         ///  Return IntPtr.Zero to let standard .NET 
         ///  runtime resolution handle non-matching names
         return IntPtr.Zero;
      }
      static bool TryLoad(string libraryName, Assembly asm, DllImportSearchPath? searchPath, out IntPtr handle, string key = null)
      {
         key ??= libraryName;
         string msg = $"[DllImport(\"{key}\")]";
         string name = asm.GetName().Name;
         if(NativeLibrary.TryLoad(libraryName, asm, searchPath, out handle))
         {
            var m = AddLoadedModule(key, handle);
            DebugWrite($"{msg} resolved to {m.FileName}");
            return true;
         }
         return false;
      }

      /// <summary>
      /// Finds a loaded module matching the specified wildcard filename.
      /// Returns null if zero or multiple (ambiguous) matches exist.
      /// 
      /// The pattern argument must match <em>one and only one</em> loaded module 
      /// name (case-insensitive) for a successful match. If multiple matches are 
      /// found, null is returned to indicate ambiguity.
      /// </summary>

      static ProcessModule FindLoadedModule(string pattern, ProcessModuleCollection modules = null)
      {
         bool nested = modules is not null;
         var matches = (modules ??= Process.GetCurrentProcess().Modules)
             .Cast<ProcessModule>()
             .Where(m => Utils.WcMatchEx(m.ModuleName, pattern, true));

         if(matches.Skip(1).Any())     ///  Multiple ambiguous matches found.
            return null;

         if(!nested && !matches.Any())
         {
            if(TryReplaceFileVersion(ref pattern))
            {
               return FindLoadedModule(pattern, modules);
            }
            return null;
         }

         return matches.FirstOrDefault();
      }

      static ProcessModule FindLoadedModule(IntPtr handle)
      {
         return Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .FirstOrDefault(m => m.BaseAddress == handle);
      }

      static ProcessModule AddLoadedModule(string libraryName, IntPtr handle)
      {
         var module = FindLoadedModule(handle);
         if(module != null)
         {
            modulePaths.TryAdd(libraryName, module.FileName);
            Debug.WriteLine($"AddLoadedModule({libraryName}, {module.FileName})");
         }
         return module;
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
         if(asm is null || asm.IsDynamic)
            return true;
         bool result = false;
         var att = asm.GetCustomAttribute<AssemblyCompanyAttribute>();
         if(att != null)
         {
            string company = att.Company;
            result = company.StartsWith("Autodesk, Inc")
               || company.StartsWith("Microsoft Corporation");
         }
         return result;
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
      /// the AcDbVersion variable.
      /// 
      /// For example, given the filename "acdb24.dll", when running on
      /// AutoCAD 2026, this method will replace it with "acdb26.dll".
      /// 
      /// </summary>
      /// <param name="filename">The original filename, updated in-place 
      /// if matched.</param>
      /// <returns>True if a replacement was performed; otherwise, false.</returns>

      static bool TryReplaceFileVersion(ref string filename)
      {
         if(string.IsNullOrWhiteSpace(filename) || AcDbVersion <= 0)
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
         DebugWrite($"TryReplaceFileVersion({input}) => {filename}");
#endif

         return true;
      }

      static int GetAcDbVersion()
      {
         var module = Process.GetCurrentProcess().Modules
             .Cast<ProcessModule>()
             .FirstOrDefault(m => Utils.WcMatchEx(m.ModuleName, ACDB_DLL, true));
         if(module != null)
         {
            string moduleName = module.ModuleName;
            string versionStr = moduleName.Substring(4, 2);
            if(int.TryParse(versionStr, out int version))
               return version;
         }
         return 0;
      }


   }
}