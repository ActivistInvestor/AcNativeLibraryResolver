/// ThisApplication.cs
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license


using System.Runtime.CompilerServices;
using AcMgdLib.Runtime;
using Autodesk.AutoCAD.Runtime;



namespace AcNativeLibraryResolverTest
{

   public class AcNativeLibraryResolverTestApplication : IExtensionApplication
   {
      public void Initialize()
      {
         /// We do not perform initialization here, because of the
         /// potential that it may be deferred until AutoCAD reaches
         /// an idle state (e.g., processes its message loop). 
         /// 
         /// Because other libraries may be dependent on initialization,
         /// we can circumvent AutoCAD's deferred assembly processing
         /// by doing our initialization in a module initializer, as is
         /// done below.
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
         AcNativeLibraryResolver.Initialize();
      }
   }

}

