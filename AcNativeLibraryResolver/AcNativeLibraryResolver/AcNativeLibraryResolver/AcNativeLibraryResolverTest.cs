/// NativeLibraryResolverTest.cs  
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license

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
      /// the database's DBMOD flags. Useful from
      /// scripting that opens and plots a drawing,
      /// without trigging a save/discard changes
      /// confirmation dialog.
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