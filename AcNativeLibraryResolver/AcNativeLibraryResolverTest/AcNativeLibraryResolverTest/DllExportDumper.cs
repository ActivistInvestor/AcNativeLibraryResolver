/// DllExportDumper.cs  
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license

using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

/// This code requires <AllowUnsafeBlocks>true</AllowUnsafeBlocks> 

namespace AcMgdLib.DevTools
{
   /// <summary>
   /// Implements the DLLEXPORTS command that dumps the native API 
   /// exports of all loaded modules, filtered by module name and/or 
   /// API name.
   /// 
   /// Results can be displayed on the AutoCAD console, a debug console, 
   /// or can be output to a text file named "AcDllExportsResults.txt" 
   /// in the user's Documents folder. If output to file is chosen, the 
   /// file is opened in the default text editor after writing.
   /// 
   /// This command searches every loaded module whose name matches 
   /// the specified module pattern, and lists all of the API exports 
   /// whose signatures match the specified API pattern.
   /// 
   /// Usage:
   /// 
   ///    Command: DLLEXPORTS
   ///    Module pattern<*>: (enter a wildcard pattern for the module name)
   ///    API pattern<*>: (enter a wildcard pattern for the API name)
   ///    Found NN matching export(s),
   ///    Output to Console or File? [Console/File] <Console>: (specify File or Console)
   /// 
   /// If used with anything-goes wildcards (e.g., "*" ), this command
   /// will dump every API EntryPoint of every loaded module, which can 
   /// be extremely lengthy, and will overflow AutoCAD's console output
   /// buffer. It is strongly recommended that you specify output to File 
   /// for large results, or use a module and API pattern that restrict 
   /// output to a limited set of APIs.
   /// 
   /// Regular expressions can be used for the API pattern by prefixing
   /// the pattern with a question mark '?'. The '?' itself is not part 
   /// of the regex pattern and is removed before compiling.
   /// </summary>

   public static class DllExportDumper   
   {
      /// <summary>
      /// To redirect output to the debug console, rather than 
      /// the AutoCAD console, set this to true:
      /// </summary>
#if(DEBUG)
      static bool outputToDebug = true;
#else
      static bool outputToDebug = false;
#endif
      const string outputFilePath = "AcDllExportsResults.txt";
      static string modulePattern = "*";
      static string apiPattern = "*";
      const uint outputFlags = UNDNAME_NO_ACCESS_SPECIFIERS | UNDNAME_NO_MEMBER_TYPE
         | UNDNAME_NO_ALLOCATION_MODEL | UNDNAME_NO_MS_KEYWORDS;

      static Regex regex = null;

      /// <summary>
      /// Dumps matching API exports to the AutoCAD console, a debug console,
      /// or a file named 'AcDllExports.txt' in the user's Documents folder. 
      /// </summary>
      /// 

      static bool Matches(this string input, string pattern)
      {
         pattern = pattern?.Trim() ?? "*";
         if((pattern == "*" || pattern == "") && regex is null)
            return true;
         if(regex is not null)
            return regex.IsMatch(input);
         else
            return Utils.WcMatchEx(input, pattern, true);
      }


      [CommandMethod("DLLEXPORTS")]
      public static void DumpExports()
      {
         Document doc = Application.DocumentManager.MdiActiveDocument;
         if(doc is null)
            return;
         Editor editor = doc.Editor;
         if(!editor.GetWildcard(ref modulePattern, "\nModule pattern: "))
            return;
         if(!editor.GetWildcard(ref apiPattern, "\nAPI pattern: "))
            return;
         regex = null;
         if(apiPattern.StartsWith("?") && apiPattern.Length > 1)
         {
            apiPattern = apiPattern.Substring(1);
            regex = new Regex(apiPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
         }
         var exports = GetExportsMatching(modulePattern, apiPattern);
         int matchCount = exports.Sum(pair => pair.Value.Count);
         int moduleCount = exports.Count;
         editor.WriteMessage($"\n  Module Pattern: '{modulePattern}'\n  API Pattern: '{apiPattern}'\n");
         if(exports.Count == 0)
         {
            editor.WriteMessage($"\nNo matching exports found.");
            return;
         }
         editor.WriteMessage($"\nFound {matchCount} matching export(s) in {moduleCount} module(s)");
         PromptKeywordOptions pko = new PromptKeywordOptions(
            "\nOutput to [Console/File] <File>: ", "Console File");
         pko.Keywords.Default = matchCount > 100 ? "File" : "Console";
         var pkr = editor.GetKeywords(pko);
         if(pkr.Status != PromptStatus.OK)
            return;
         if(pkr.StringResult == "File")
         { 
            string file = DumpExportsToFile(exports);
            editor.WriteMessage($"\nResults output to '{file}'.\n");
            return;
         }
         if(outputToDebug)
            Debug.WriteLine("$(CLEAR)"); // Supported only by a custom listener (not included)
         else
            Application.DisplayTextScreen = true;
         foreach(var entry in exports)
         {
            ProcessModule module = entry.Key;
            List<ExportRecord> result = entry.Value;
            Write($"[Module: {module.ModuleName}]");
            foreach(var export in result)
            {
               Write($"    [{export.Signature}]  {export.EntryPoint}");
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

      private static string DumpExportsToFile(IDictionary<ProcessModule, List<ExportRecord>> exports)
      {
         int matchCount = exports.Sum(pair => pair.Value.Count);
         var lines = new List<string>();
         string filename = GetExportFilePath();
         lines.Add($"{filename} - DLLEXPORTS Command Output");
         lines.Add($"    Module Pattern: {modulePattern}");
         lines.Add($"    API Pattern:    {apiPattern}");
         lines.Add($"    {matchCount} matching export(s): ");
         lines.Add("");
         foreach(var entry in exports)
         {
            ProcessModule module = entry.Key;
            lines.Add($"[Module: {module.ModuleName}]");
            foreach(var export in entry.Value)
               lines.Add($"    [{export.Signature}]  {export.EntryPoint}");
         }
         Directory.CreateDirectory(Path.GetDirectoryName(filename));
         File.WriteAllLines(filename, lines);
         Process.Start(new ProcessStartInfo(filename)
         {
            UseShellExecute = true
         });
         return filename;
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
            .Any(m => Utils.WcMatchEx(m.ModuleName, pattern, true)); 
      }

      //static bool Matches(this string input, string pattern, bool ignoreCase)
      //{
      //   if(string.IsNullOrWhiteSpace(pattern) || pattern == "*")
      //      return true;
      //   return Utils.WcMatchEx(input, pattern, ignoreCase);
      //}

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

      private static List<string> GetExports(ProcessModule module, string apiPattern)
      {
         List<string> exports = null;
         if(!Utils.WcMatchEx(module.ModuleName, modulePattern, true))
            return exports;
         IntPtr hModule = module.BaseAddress;
         if(hModule == IntPtr.Zero)
            return exports;
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
            if(peSignature != 0x00004550) 
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
            exports = new List<string>();
            for(uint i = 0; i < numberOfNames; i++)
            {
               uint nameRva = namesRvaTable[i];
               if(nameRva == 0) continue;

               byte* namePtr = basePtr + nameRva;
               string exportName = Marshal.PtrToStringAnsi((IntPtr)namePtr);
               string unmangledName = UnmangleSymbol(exportName, UNDNAME_NAME_ONLY);

               if(!string.IsNullOrEmpty(exportName) && unmangledName.Matches(apiPattern))
               {
                  exports.Add(exportName);
               }
            }
         }

         return exports;
      }

      public record ExportRecord(string EntryPoint, string Signature);

      public static IDictionary<ProcessModule, List<ExportRecord>> GetExportsMatching(string modulePattern = "*", string apiPattern = "*")
      {
         var result = new Dictionary<ProcessModule, List<ExportRecord>>();
         var matchingModules = Process.GetCurrentProcess()
            .Modules.Cast<ProcessModule>()
            .Where(m => Utils.WcMatchEx(m.ModuleName, modulePattern, true));
         foreach(ProcessModule module in matchingModules)
         {
            var exports = GetExports(module, apiPattern);
            if(exports?.Count > 0)
               result[module] = exports.ConvertAll(e => new ExportRecord(e, UnmangleSymbol(e))).ToList();
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