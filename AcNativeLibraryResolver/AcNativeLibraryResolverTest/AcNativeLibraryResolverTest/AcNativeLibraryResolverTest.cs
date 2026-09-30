/// NativeLibraryResolverTest.cs  
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license

namespace AcNativeLibraryResolverTest
{
   public static class AcNativeLibraryResolverTest
   {
      /// <summary>
      /// The RESETDBMOD command resets the current 
      /// database's DBMOD flags to 0.
      /// 
      /// </summary>
      [CommandMethod("RESETDBMOD")]
      public static void ResetDbmodCommand()
      {
         HostApplicationServices.WorkingDatabase?.SetDbmod(0);
      }


      /// <summary>
      /// Performs a zoom extents without changing the 
      /// database's DBMOD flags. This pattern is Useful 
      /// with scripting that opens and plots a drawing
      /// (for example), without trigging a save/discard
      /// changes dialog when the drawing is closed.
      /// To prevent any number of actions from changing
      /// the database's DBMOD flags, place them in the
      /// using() block like the one shown below.
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