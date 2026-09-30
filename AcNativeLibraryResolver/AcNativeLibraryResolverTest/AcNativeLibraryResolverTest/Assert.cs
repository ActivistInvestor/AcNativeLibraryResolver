/// Assert.cs
/// 
/// Activist Investor / Tony T
///
/// Distributed under the terms of the MIT license.

using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace AcMgdLib.Diagnostics
{
   public static partial class Assert
   {
      /// <summary>
      /// Performs validation of a Database managed wrapper, including
      /// verifying that the underlying AcDbDatabase exists and is usable.
      /// </summary>
      /// <param arg="database"></param>
      /// <param arg="arg"></param>
      /// <returns></returns>
      /// <exception cref="ArgumentNullException"></exception>
      /// <exception cref="ObjectDisposedException"></exception>
      /// <exception cref="ArgumentException"></exception>

      internal static Database IsValid(Database database, [CallerArgumentExpression(nameof(database))] string arg = "database")
      {
         if(database is null)
            throw new ArgumentNullException(arg);
         if(database.IsDisposed)
            throw new ObjectDisposedException(arg);
         if(!database.IsAlive())
            throw new ArgumentException($"{arg}: Underlying AcDbDatabase does not exist");
         return database;
      }

      /// <summary>
      /// Indicates if the AcDbDatabase wrapped by a given 
      /// Database instance exists.
      /// </summary>

      public static bool IsAlive(this Database database)
      {
         if(database is null)
            throw new ArgumentNullException(nameof(database));
         return Database.IdFromDb(database) != 0L;
      }

      public static void IsNotNull(object arg, [CallerArgumentExpression("arg")] string msg = "null argument")
      {
         if(arg is null)
            throw new ArgumentNullException(msg);
      }
      public static void IsNotNullOrDisposed(DisposableWrapper arg, [CallerArgumentExpression(nameof(arg))] string msg = "null or disposed object")
      {
         if(arg is null)
            throw new ArgumentNullException(msg);
         if(arg.IsDisposed)
            throw new ObjectDisposedException(arg.GetType().FullName);
         if(arg.UnmanagedObject == IntPtr.Zero)
            throw new InvalidOperationException("invalid managed wrapper");
      }



   }
}
