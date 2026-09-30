
using System.Diagnostics;
using System.Runtime.InteropServices;
using AcMgdLib.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Internal;
using Autodesk.AutoCAD.Runtime;

/// NativeLibraryResolverTest.cs  
/// 
/// ActivistInvestor / Tony Tanzilloanzillo
/// 
/// Distributed under the terms of the MIT license

namespace AcNativeLibraryResolverTest
{
   /// <summary>
   /// Commands specifically for use in testing
   /// AcNativeLibraryResolver. These commands
   /// list loaded, and unloaded modules.
   /// </summary>
   
   public static class TestCommands
   {
      [CommandMethod("LLM")]
      
      public static void ListLoadedModules()
      {
         var doc = Application.DocumentManager.MdiActiveDocument;
         var ed = doc.Editor;
         pattern = ed.GetPattern(null, pattern);
         if(pattern is null) 
            return;
         var modules = Process.GetCurrentProcess().Modules;
         bool flag = pattern == "*";
         if(flag)
            Write(ed, "$(CLEAR)");
         foreach(ProcessModule m in modules)
         {
            var name = m.ModuleName;
            if(flag || Utils.WcMatchEx(name, pattern, true))
               Debug.WriteLine($"{m.ModuleName}: {m.FileName}");
         }
      }

      static void Write(Editor ed, string msg)
      {
         if(outputToDebug)
            Debug.WriteLine(msg);
         else
            ed.WriteMessage($"\n{msg}");
      }

      static bool IsModuleLoaded(string pattern)
      {
         return Process.GetCurrentProcess().Modules
            .Cast<ProcessModule>()
            .FirstOrDefault(m => Utils.WcMatchEx(m.ModuleName, pattern, true)) != null;
      }

      static bool outputToDebug = IsModuleLoaded("AcMgdLib.dll");

      static string pattern = "*";

      static string GetPattern(this Editor ed, string prompt = null, string defaultValue = "*")
      {
         PromptStringOptions psr = new PromptStringOptions(prompt ?? "\nPattern: ");
         psr.AllowSpaces = false;
         psr.DefaultValue = defaultValue;
         psr.UseDefaultValue = !string.IsNullOrEmpty(defaultValue);
         var pr = ed.GetString(psr);
         if(pr.Status != PromptStatus.OK)
            return null;
         if(pr.StringResult == string.Empty)
            return "*";
         return pr.StringResult;
      }

      [CommandMethod("LUM")]
      public static void ListUnloadedModules()
      {
         var doc = Application.DocumentManager.MdiActiveDocument;
         var ed = doc.Editor;
         pattern = ed.GetPattern(null, pattern);
         if(pattern is null)
            return;
         var loaded = new HashSet<string>(
               Process.GetCurrentProcess().Modules
               .Cast<ProcessModule>()
               .Select(m => m.ModuleName), 
            StringComparer.OrdinalIgnoreCase);
         var moduleFileNames = GetModuleFilenames();
         bool flag = pattern == "*";
         if(flag)
            Write(doc.Editor, "$(CLEAR)");
         foreach(string filename in moduleFileNames)
         {
            if(!loaded.Contains(filename) && (flag || Utils.WcMatchEx(filename, pattern, true)))
               Debug.WriteLine(filename);
         }
      }

      static IEnumerable<string> GetModuleFilenames()
      {
         return Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName);
      }



   }
}