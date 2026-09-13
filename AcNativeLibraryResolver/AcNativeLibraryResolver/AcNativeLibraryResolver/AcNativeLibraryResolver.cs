/// AcNativeLibraryResolver.cs  
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
   /// See README.MD for details
   /// </summary>

   public static class AcNativeLibraryResolver
   {
      const string ACDB_DLL = "acdb2#.dll";
      static ConcurrentDictionary<Assembly, bool> knownAssemblies = new();
      static bool initialized;
      static readonly HashSet<Assembly> _registeredAssemblies = new HashSet<Assembly>();
      static readonly object _lock = new object();

      ///  Caches filename -> resolved module file path (or empty string for negative/ambiguous matches)
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
      /// Finds a loaded module matching the specified wildcard filename.
      /// Returns null if zero or multiple (ambiguous) matches exist.
      /// 
      /// The filename argument must match one and only one loaded module name 
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
      /// <param name="filename">The original filename, updated in-place 
      /// if matched.</param>
      /// <returns>True if a replacement was performed; otherwise, false.</returns>
      
      static bool TryReplaceFileVersion(ref string filename)
      {
         if(string.IsNullOrWhiteSpace(filename) || AcDllVersion <= 0)
            return false;

         ReadOnlySpan<char> span = filename.AsSpan().Trim();
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

         // Avoid allocating if the filename already has the target version digits
         if(span[d1Idx] == targetD1 && span[d2Idx] == targetD2)
            return false;

         filename = string.Create(filename.Length, (filename, d1Idx, d2Idx, c1: (char)targetD1, c2: (char)targetD2), (buf, state) =>
         {
            state.filename.AsSpan().CopyTo(buf);
            buf[state.d1Idx] = state.c1;
            buf[state.d2Idx] = state.c2;
         });

#if DEBUG
         DebugWrite($"TryReplaceFileVersion({input}) => {filename}");
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