/// DatabaseExtensions.cs  
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license


using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace AcNativeLibraryResolverTest
{
   /// <summary>
   /// Adds the SetDbmod(), GetDbmod() and FreezeDbmod() 
   /// wrapper/extension methods to the Database class.
   /// </summary>

   public static partial class DatabaseExtensions
   {
      class NativeMethods
      {
         /// Imports acdbSetDbmod() 
         /// 
         /// Tests/demos wildcard support in the DllImport attribute's
         /// dllName argument.
         /// 
         /// Note that in order for wildcard and mismatched release-
         /// dependent filename resolution used here to work, the
         /// AcNativeLibraryResolver's Initialize() method must be
         /// called before any of these imported APIs are called.
         /// 
         /// Note the use of the "acdb2#.dll" wildcard in the DllImport 
         /// attribute, which will be resolved to the correct version 
         /// of acdbXX.dll at runtime. This allows the same build output
         /// produced from this code to target any AutoCAD release from 
         /// AutoCAD 2025 onward.

         [DllImport("acdb2#.dll", 
            EntryPoint = "?acdbSetDbmod@@YAHPEAVAcDbDatabase@@H@Z",
            CallingConvention = CallingConvention.Cdecl)]
         internal static extern int acdbSetDbmod(IntPtr database, int newval);

         /// Imports acdbGetDbmod() 
         /// 
         /// Tests/demos autonomous recognization and replacement of
         /// mismatched release-dependent filenames in the DllImport
         /// attibute's dllName argument, by importing the native 
         /// acdbGetDbmod() function.
         /// 
         /// Note the (intentional) use of a mismatched release-specific
         /// dll filename. The filename (acdb24.dll) is the filename of 
         /// the ObjectDBX library in AutoCAD 2024. 
         /// 
         /// When the containing assembly is run on a later release, 
         /// AcNativeLibraryResolver detects the mismatched filename 
         /// and replaces it with the correct name of the library for 
         /// the release the code is running on.

         [DllImport("acdb24.dll", 
            EntryPoint = "?acdbGetDbmod@@YAHPEAVAcDbDatabase@@@Z", 
            CallingConvention = CallingConvention.Cdecl)]
         internal static extern int acdbGetDbmod(IntPtr database);

         /// <summary>
         /// Indicates if the calling code is running on the main thread.
         /// </summary>
         
         [DllImport("acdb2#.dll", 
            EntryPoint = "?acdbInMainThread@@YA_NXZ", 
            CallingConvention = CallingConvention.Cdecl)]
         [return: MarshalAs(UnmanagedType.U1)]
         internal static extern bool acdbInMainThread();

      }

      /// <summary>
      /// Extension method wrappers for the Database class. 
      /// </summary>

      public static int SetDbmod(this Database database, int newval)
      {
         return NativeMethods.acdbSetDbmod(Validate(database).UnmanagedObject, newval);
      }

      public static int GetDbmod(this Database database)
      {
         return NativeMethods.acdbGetDbmod(Validate(database).UnmanagedObject);
      }

      /// <summary>
      /// Automates saving and restoring a Database's DBMOD flags.
      /// </summary>
      /// <param arg="database">The Database whose DBMOD flags
      /// are to be managed.</param>
      /// <returns>An object that when disposed, causes the DBMOD
      /// flags of the given Database to be restored to the value
      /// it had when this method was most-recently called on the 
      /// same Database</returns>
      /// <remarks>
      /// This method is typically used when modifications must be
      /// made to a database, without putting the database into a
      /// modified/unsaved state, that would trigger a confirmation
      /// dialog if the user were to close the drawing. It is most
      /// commonly used when changes are made to a new document, or
      /// a document that was just opened, by an event handler.
      /// 
      /// This method is also useful for unit testing.
      /// 
      /// Example:
      /// <code>
      /// 
      ///     Document doc = Application.DocumentManager.MdiActiveDocument;
      ///     Database database = doc.Database;
      ///     
      ///     using(database.FreezeDbmod())
      ///     {
      ///        // Changes made to the database here will
      ///        // not alter its DBMOD flags.
      ///        
      ///        doc.Editor.Command("._ZOOM", "_Extents");
      ///     }
      ///
      /// </code>
      /// </remarks>

      public static IDisposable FreezeDbmod(this Database database)
      {
         return new Disposer(Validate(database));
      }

      class Disposer : IDisposable
      {
         int dbmod;
         Database db;
         
         public Disposer(Database db)
         {
            if(db is null)
               throw new ArgumentNullException(nameof(db));
            this.db = db;
            dbmod = NativeMethods.acdbGetDbmod(db.UnmanagedObject);
         }
         
         public void Dispose()
         {
            if(db != null && NativeMethods.acdbInMainThread() && db.IsValid())
            {
               NativeMethods.acdbSetDbmod(db.UnmanagedObject, dbmod);
            }
            db = null;
         }
      }

      /// <summary>
      /// Performs validation of a Database managed wrapper.
      /// </summary>
      /// <param arg="database"></param>
      /// <param arg="arg"></param>
      /// <returns></returns>
      /// <exception cref="ArgumentNullException"></exception>
      /// <exception cref="ObjectDisposedException"></exception>
      /// <exception cref="ArgumentException"></exception>

      static Database Validate(Database database, [CallerArgumentExpression(nameof(database))] string arg = "database")
      {
         if(database is null)
            throw new ArgumentNullException(arg);
         if(database.IsDisposed)
            throw new ObjectDisposedException(arg);
         if(!database.IsValid())
            throw new ArgumentException($"{arg}: Underlying AcDbDatabase does not exist");
         return database;
      }

      /// <summary>
      /// Indicates if the AcDbDatabase wrapped by a given 
      /// Database instance exists.
      /// </summary>

      public static bool IsValid(this Database database)
      {
         if(database is null)
            throw new ArgumentNullException(nameof(database));
         return !database.IsDisposed && Database.IdFromDb(database) != 0;
      }


   }

}