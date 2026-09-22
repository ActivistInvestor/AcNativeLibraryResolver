/// ThisApplication.cs
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license


using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AcMgdLib.Runtime;
using Autodesk.AutoCAD.Runtime;



namespace AcNativeLibraryResolverTest
{

   public class AcNativeLibraryResolverTestApplication : IExtensionApplication
   {
      public void Initialize()
      {
         /// Special Case:
         /// 
         /// We do not perform initialization here, because of the
         /// potential that it may be deferred until AutoCAD enters
         /// an idle state (e.g., processes its message loop), which
         /// can happen when Profile-guided Optimization triggers JIT
         /// compilation of code in the assembly on a background thread.
         /// In that case, the assembly is loaded on the background
         /// thread as well.
         /// 
         /// If that happens, AutoCAD will not call this method until
         /// it enters the idle state, with the main/UI thread current
         /// (at roughly the same point when the Application.Idle event 
         /// is raised).
         /// 
         /// Because other libraries may be dependent on initialization
         /// of AcNativeLibraryResolver, we circumvent AutoCAD's deferred 
         /// assembly processing by doing our initialization in a module 
         /// initializer, rather than in this method, as is shown below.
         /// 
         /// Using a module initializer ensures that the Initialize()
         /// method of AcNativeLibraryResolver runs ASAP, which is usually 
         /// during the call to Assembly.Load().
         /// 
         /// Note that this also means that the module initializer
         /// may run on a background thread, rather than on the main
         /// or UI thread. In this case, the code that is called is
         /// thread-safe.
      }

      public void Terminate()
      {
      }
   }

   internal static class ModuleInitializer
   {
#pragma warning disable CA2255
      [ModuleInitializer]
#pragma warning restore CA2255
      internal static void InitializeModule()
      {
         try
         {
            AcNativeLibraryResolver.Initialize();
         }
         catch(System.Exception ex)
         {
            Debug.WriteLine(ex.ToString());
            if(ex.InnerException != null)
               Debug.WriteLine($"Inner exception: {ex.InnerException.ToString()}");
         }
      }
   }

}

