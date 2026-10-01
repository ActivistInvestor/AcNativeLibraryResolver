
/// DllExportDumper.cs  
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

/// This code requires <AllowUnsafeBlocks>true</AllowUnsafeBlocks> 

namespace AcMgdLib.DevTools
{
   /// <summary>
   /// Implements commands that dump the native API exports of all 
   /// loaded modules, filtered by module name and/or API name.
   /// 
   /// These commands search every loaded module whose name matches 
   /// the specified module pattern, and lists all of the API exports 
   /// whose unmangled names match the specified API pattern.
   /// 
   /// Commands: 
   /// 
   ///   DLLEXPORTS - Dumps matching exports to the AutoCAD 
   ///   console or debug console.
   ///   
   ///   DLLEXPORTSOUT - Dumps matching exports to a text file 
   ///   and opens it with the default text editor.
   /// 
   /// If used with anything-goes wildcards (e.g., "*" ), these commands 
   /// will dump every API entryPoint of every loaded module, which can be 
   /// extremely lengthy, and will overflow AutoCAD's console output
   /// buffer. It is strongly recommended that one use DLLEXPORTSOUT 
   /// for large results, or use the module and API pattern arguments 
   /// with wildcards that restrict output to a limited set of APIs.
   /// </summary>

   public static class DllExportDumper   
   {
      /// <summary>
      /// To redirect output to the debug console, rather than 
      /// the AutoCAD console, set this to true:
      /// </summary>
      static bool outputToDebug = ModuleIsLoaded("AcMgdLib.dll");
      const string outputFilePath = "AcDllExports.txt";
      static string modulePattern = "*";
      static string apiPattern = "*";
      const uint outputFlags = UNDNAME_NO_ACCESS_SPECIFIERS | UNDNAME_NO_MEMBER_TYPE
         | UNDNAME_NO_ALLOCATION_MODEL | UNDNAME_NO_MS_KEYWORDS;

      /// <summary>
      /// Dumps matching entryPoint records to the AutoCAD console or debug console.
      /// Suitable for limited results, but will overflow the AutoCAD console buffer 
      /// if used with wildcards that match a large number of exports. See the
      /// DLLEXPORTSOUT command for a more suitable option for large results.
      /// </summary>

      [CommandMethod("DLLEXPORTS")]
      public static void DumpExports()
      {
         Document doc = Application.DocumentManager.MdiActiveDocument;
         if(doc is null)
            return;
         Editor editor = doc.Editor;
         if(!editor.GetWildcard(ref modulePattern, "\nModule pattern: "))
            return;
         if(!editor.GetWildcard(ref apiPattern, "\nAPI Pattern: "))
            return;
         int matchCount = 0;
         var exports = GetExportsMatching(modulePattern, apiPattern);
         if(exports.Count == 0)
         {
            editor.WriteMessage($"\nNo matching exports found for module/api patterns '{modulePattern}'/'{apiPattern}'.\n");
            return;
         }  
         if(outputToDebug)
            Debug.WriteLine("$(CLEAR)"); // Supported only by a custom trace listener (not included)
         else
            Application.DisplayTextScreen = true;
         matchCount = exports.Sum(entry => entry.Value.Length);
         foreach(var entry in exports)
         {
            ProcessModule module = entry.Key;
            ModuleExport[] moduleExports = entry.Value;
            Write($"[Module: {module.ModuleName}]");
            foreach(var export in moduleExports)
            {
               Write($"    [{export.UnmangledName}]  {export.EntryPoint}");
            }
         }

         void Write(string msg)
         {
            if(outputToDebug)
               Debug.WriteLine(msg);
            else
               editor.WriteMessage($"\n{msg}");
         }

         editor.WriteMessage($"\n\nFound {matchCount} matching export(s).\n");
      }

      /// <summary>
      /// Writes results to a text file in the user's MyDocuments folder,
      /// rather than the AutoCAD console or debug console, and opens the
      /// file with the default text editor. This is suitable for large
      /// results that would overflow the AutoCAD console buffer. 
      /// </summary>

      [CommandMethod("DLLEXPORTSOUT")]
      public static void DumpExportsToFile()
      {
          Document doc = Application.DocumentManager.MdiActiveDocument;
          Editor editor = doc.Editor;
          if(!editor.GetWildcard(ref modulePattern, "\nModule pattern: "))
             return;
          if(!editor.GetWildcard(ref apiPattern, "\nAPI Pattern: "))
             return;
          var exports = GetExportsMatching(modulePattern, apiPattern);
          if(exports.Count == 0)
          {
             editor.WriteMessage($"\nNo matching exports found for module/api patterns '{modulePattern}'/'{apiPattern}'.\n");
             return;
          }
          int matchCount = exports.Sum(entry => entry.Value.Length);
          var lines = new List<string>();
          foreach(var entry in exports)
          {
             ProcessModule module = entry.Key;
             lines.Add($"[Module: {module.ModuleName}]");
             foreach(var export in entry.Value)
             {
                lines.Add($"    [{export.UnmangledName}]  {export.EntryPoint}");
             }
          }

          string fileName = GetExportFilePath();
          Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);
          File.WriteAllLines(fileName, lines.Count > 0 ? lines : new[] { "No matching exports found." });
          Process.Start(new ProcessStartInfo(fileName)
          {
             UseShellExecute = true
          });
          editor.WriteMessage($"\n\nWrote {matchCount} matching export(s) to {fileName}.\n");
       }

      private static string GetExportFilePath()
      {
          string documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
          if(string.IsNullOrWhiteSpace(documentsFolder))
             documentsFolder = AppDomain.CurrentDomain.BaseDirectory;
          return Path.Combine(documentsFolder, outputFilePath);
      }


      static bool GetWildcard(this Editor ed, ref string value, string prompt = "\nPattern: ")
      {
         PromptStringOptions pso = new PromptStringOptions(prompt);
         pso.AllowSpaces = false;
         value ??= "*";
         pso.DefaultValue = value;
         pso.UseDefaultValue = true;
         var pr = ed.GetString(pso);
         if(pr.Status != PromptStatus.OK)
            return false;
         if(string.IsNullOrWhiteSpace(pr.StringResult))
            value = "*";
         else 
            value = pr.StringResult;
         return true;
      }

      static bool ModuleIsLoaded(string pattern)
      {
         return Process.GetCurrentProcess().Modules
            .Cast<ProcessModule>()
            .Any(m => m.ModuleName.Matches(pattern)); 
      }

      static bool Matches(this string input, string pattern, bool ignoreCase = true)
      {
         if(string.IsNullOrWhiteSpace(pattern) || pattern == "*")
            return true;
         return Utils.WcMatchEx(input, pattern, ignoreCase);
      }

      [DllImport("dbghelp.dll", SetLastError = true, CharSet = CharSet.Ansi)]
      private static extern uint UnDecorateSymbolName(
          string name,
          StringBuilder outputString,
          int maxStringLength,
          uint flags);
      
      private static string UnmangleSymbol(string mangledName, uint flags = outputFlags)
      {
         if(string.IsNullOrEmpty(mangledName) || !mangledName.StartsWith("?"))
            return mangledName;

         StringBuilder buffer = new StringBuilder(2048);
         uint result = UnDecorateSymbolName(
             mangledName,
             buffer,
             buffer.Capacity,
             flags
         );

         return (result > 0) ? buffer.ToString() : mangledName;
      }

      private static IEnumerable<string> GetExports(ProcessModule module)
      {
         if(!module.ModuleName.Matches(modulePattern))
            return Enumerable.Empty<string>();
         IntPtr hModule = module.BaseAddress;
         if(hModule == IntPtr.Zero)
            return Enumerable.Empty<string>();
         List<string> exports = new List<string>();
         unsafe
         {
            byte* basePtr = (byte*)hModule;
            ushort magic = *(ushort*)basePtr;
            if(magic != 0x5A4D) // 'MZ'
               return exports;
            int lfanew = *(int*)(basePtr + 0x3C);
            if(lfanew <= 0 || lfanew > 0x1000)
               return exports;
            byte* ntHeaders = basePtr + lfanew;
            uint peSignature = *(uint*)ntHeaders;
            if(peSignature != 0x00004550) // 'PE\0\0'
               return exports;
            uint exportDataDirRva = *(uint*)(ntHeaders + 0x88);
            uint exportDataDirSize = *(uint*)(ntHeaders + 0x8C);
            if(exportDataDirRva == 0 || exportDataDirSize == 0)
               return exports;
            byte* exportDir = basePtr + exportDataDirRva;
            uint numberOfNames = *(uint*)(exportDir + 0x18);     
            uint addressOfNamesRva = *(uint*)(exportDir + 0x20); 
            if(numberOfNames == 0 || addressOfNamesRva == 0)
               return exports;
            uint* namesRvaTable = (uint*)(basePtr + addressOfNamesRva);
            for(uint i = 0; i < numberOfNames; i++)
            {
               uint nameRva = namesRvaTable[i];
               if(nameRva == 0) continue;

               byte* namePtr = basePtr + nameRva;
               string exportName = Marshal.PtrToStringAnsi((IntPtr)namePtr);

               if(!string.IsNullOrEmpty(exportName))
               {
                  exports.Add(exportName);
               }
            }
         }

         return exports;
      }

      public record ModuleExport(string EntryPoint, string UnmangledName);

      public static IDictionary<ProcessModule, ModuleExport[]> GetExportsMatching(string modulePattern = "*", string apiPattern = "*")
      {
         var result = new Dictionary<ProcessModule, ModuleExport[]>();
         var matchingModules = Process.GetCurrentProcess()
            .Modules.Cast<ProcessModule>()
            .Where(m => m.ModuleName.Matches(modulePattern));
         foreach(ProcessModule module in matchingModules)
         {
            var exports = GetExports(module)
               .Where(entryPoint => UnmangleSymbol(entryPoint, UNDNAME_NAME_ONLY).Matches(apiPattern))
               .Select(entryPoint => new ModuleExport(entryPoint, UnmangleSymbol(entryPoint)))
               .ToArray();
            if(exports.Any())
            {
               result[module] = exports;
            }
         }
         return result;
      }


      /// <summary>
      /// UndecorateSymbolName flags
      /// </summary>
      private const uint UNDNAME_NAME_ONLY = 0x1000;
      private const uint UNDNAME_COMPLETE = 0x0000;
      private const uint UNDNAME_NO_THISTYPE = 0x0060;
      private const uint UNDNAME_NO_ACCESS_SPECIFIERS = 0x0080;
      private const uint UNDNAME_NO_MEMBER_TYPE = 0x0200;
      private const uint UNDNAME_NO_ALLOCATION_MODEL = 0x0008; 
      private const uint UNDNAME_NO_MS_KEYWORDS = 0x0002; 


      [StructLayout(LayoutKind.Sequential)]
      private struct IMAGE_DOS_HEADER
      {
         public ushort e_magic;
         public ushort e_cblp;
         public ushort e_cp;
         public ushort e_crlc;
         public ushort e_cparhdr;
         public ushort e_minalloc;
         public ushort e_maxalloc;
         public ushort e_ss;
         public ushort e_sp;
         public ushort e_csum;
         public ushort e_ip;
         public ushort e_cs;
         public ushort e_lfarlc;
         public ushort e_ovno;
         [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
         public ushort[] e_res;
         public ushort e_oemid;
         public ushort e_oeminfo;
         [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
         public ushort[] e_res2;
         public int e_lfanew;   
      }

      [StructLayout(LayoutKind.Sequential)]
      private struct IMAGE_DATA_DIRECTORY
      {
         public uint VirtualAddress;
         public uint Size;
      }

      [StructLayout(LayoutKind.Sequential)]
      private struct IMAGE_NT_HEADERS64
      {
         public uint Signature;
         public IMAGE_FILE_HEADER FileHeader;
         public IMAGE_OPTIONAL_HEADER64 OptionalHeader;
      }

      [StructLayout(LayoutKind.Sequential)]
      private struct IMAGE_FILE_HEADER
      {
         public ushort Machine;
         public ushort NumberOfSections;
         public uint TimeDateStamp;
         public uint PointerToSymbolTable;
         public uint NumberOfSymbols;
         public ushort SizeOfOptionalHeader;
         public ushort Characteristics;
      }

      [StructLayout(LayoutKind.Sequential)]
      private struct IMAGE_OPTIONAL_HEADER64
      {
         public ushort Magic;
         public byte MajorLinkerVersion;
         public byte MinorLinkerVersion;
         public uint SizeOfCode;
         public uint SizeOfInitializedData;
         public uint SizeOfUninitializedData;
         public uint AddressOfEntryPoint;
         public uint BaseOfCode;
         public ulong ImageBase;
         public uint SectionAlignment;
         public uint FileAlignment;
         public ushort MajorOperatingSystemVersion;
         public ushort MinorOperatingSystemVersion;
         public ushort MajorImageVersion;
         public ushort MinorImageVersion;
         public ushort MajorSubsystemVersion;
         public ushort MinorSubsystemVersion;
         public uint Win32VersionValue;
         public uint SizeOfImage;
         public uint SizeOfHeaders;
         public uint CheckSum;
         public ushort Subsystem;
         public ushort DllCharacteristics;
         public ulong SizeOfStackReserve;
         public ulong SizeOfStackCommit;
         public ulong SizeOfHeapReserve;
         public ulong SizeOfHeapCommit;
         public uint LoaderFlags;
         public uint NumberOfRvaAndSizes;
         public IMAGE_DATA_DIRECTORY DataDirectory0;
      }

      [StructLayout(LayoutKind.Sequential)]
      private struct IMAGE_EXPORT_DIRECTORY
      {
         public uint Characteristics;
         public uint TimeDateStamp;
         public ushort MajorVersion;
         public ushort MinorVersion;
         public uint Name;
         public uint Base;
         public uint NumberOfFunctions;
         public uint NumberOfNames;
         public uint AddressOfFunctions;
         public uint AddressOfNames;
         public uint AddressOfNameOrdinals;
      }
   }
}