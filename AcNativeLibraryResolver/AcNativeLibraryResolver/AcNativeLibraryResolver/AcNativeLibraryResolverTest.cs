/// NativeLibraryResolverTest.cs  
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace AcNativeLibraryResolverTest
{
   /// <summary>
   /// The RESETDBMOD command resets the current 
   /// database's DBMOD flags to 0.
   /// 
   /// </summary>

   public static class AcNativeLibraryResolverTest
   {
      [CommandMethod("RESETDBMOD")]
      public static void ResetDbmodCommand()
      {
         HostApplicationServices.WorkingDatabase?.SetDbmod(0);
      }


      /// <summary>
      /// Performs a zoom extents without changing 
      /// the database's DBMOD flags.
      /// </summary>
      [CommandMethod("ZOOMEXTENTS")]
      public static void ZoomExtentsCommand()
      {
         Document doc = Application.DocumentManager.MdiActiveDocument;
         using(doc.Database.FreezeDbmod())
         {
            doc.Editor.Command("._ZOOM", "_Extents");
         }
      }
   }
}