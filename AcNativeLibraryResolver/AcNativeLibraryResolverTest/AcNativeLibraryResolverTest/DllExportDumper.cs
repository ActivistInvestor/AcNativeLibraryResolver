
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
   public static class DllExportDumper   
   {
      /// <summary>
      /// Output can be extremely lengthy. To redirect it to the
      /// debug console, rather than the AutoCAD console, set this 
      /// to true:
      /// </summary>
      static bool outputToDebug = ModuleIsLoaded("AcMgdLib.dll");
      static string modulePattern = "*";
      static string apiPattern = "*";
      const uint outputFlags = UNDNAME_NO_THISTYPE | UNDNAME_NO_ACCESS_SPECIFIERS | UNDNAME_NO_MEMBER_TYPE;

      [CommandMethod("DLLEXPORTS")]
      public static void DumpExports()
      {
         Document doc = Application.DocumentManager.MdiActiveDocument;
         if(doc is null)
            return;
         Editor editor = doc.Editor;
         modulePattern = editor.GetWildcardPattern("\nModule pattern: ", modulePattern);
         if(modulePattern is null)
            return;
         apiPattern = editor.GetWildcardPattern("\nAPI Pattern: ", apiPattern);
         if(apiPattern is null)
            return;
         int matchCount = 0;
         if(outputToDebug)
            Debug.WriteLine("$(CLEAR)"); // Supported only by a custom trace listener (not included)
         else
            Application.DisplayTextScreen = true;

         void Write(string msg)
         {
            if(outputToDebug)
               Debug.WriteLine(msg);
            else
               editor.WriteMessage($"\n{msg}");
         }

         var matchingModules = Process.GetCurrentProcess()
            .Modules.Cast<ProcessModule>()
            .Where(m => m.ModuleName.Matches(modulePattern));

         foreach(ProcessModule module in matchingModules)
         {
            try
            {
               var exports = GetExports(module);
               if(!exports.Any())
                  continue;   
               bool flag = false;
               foreach(string export in exports)
               {
                  /// Only match aginst the actual api name, not arguments or return types.
                  string unmangledName = UnmangleSymbol(export, UNDNAME_NAME_ONLY);
                  if(unmangledName.Matches(apiPattern))
                  {
                     if(!flag)
                     {
                        flag = true;
                        Write($"[Module: {module.ModuleName}]");
                     }
                     unmangledName = UnmangleSymbol(export, outputFlags);
                     Write($"    [{unmangledName}]  {export}");
                     ++matchCount;
                  }
               }
            }
            catch(System.Exception ex)
            {
               Debug.WriteLine($"Exception accessing {module.ModuleName}: {ex.ToString()}");
            }
         }
         editor.WriteMessage($"\n\nFound {matchCount} matching export(s).\n");
      }

      static string GetWildcardPattern(this Editor ed, string prompt = "\nPattern: ", string defaultValue = null)
      {
         PromptStringOptions psr = new PromptStringOptions(prompt);
         psr.AllowSpaces = false;
         defaultValue ??= "*";
         psr.DefaultValue = defaultValue;
         psr.UseDefaultValue = true;
         var pr = ed.GetString(psr);
         if(pr.Status != PromptStatus.OK)
            return null;
         if(string.IsNullOrWhiteSpace(pr.StringResult))
            return "*";
         return pr.StringResult;
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
      
      private static string UnmangleSymbol(string mangledName, uint flags)
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

      private const uint UNDNAME_NAME_ONLY = 0x1000;
      private const uint UNDNAME_COMPLETE = 0x0000;
      private const uint UNDNAME_NO_THISTYPE = 0x0060;
      private const uint UNDNAME_NO_ACCESS_SPECIFIERS = 0x0080;
      private const uint UNDNAME_NO_MEMBER_TYPE = 0x0200;

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