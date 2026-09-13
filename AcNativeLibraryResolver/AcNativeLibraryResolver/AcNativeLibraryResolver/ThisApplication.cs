/// LayerFilterExtensionsApplication.cs
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license


using System.Diagnostics;
using AcMgdLib.Runtime;
using Autodesk.AutoCAD.Runtime;


namespace AcNativeLibraryResolverTest
{

   public class ThisApplication : IExtensionApplication
   {
      public void Initialize()
      {
         AcNativeLibraryResolver.Initialize();
         Debug.WriteLine("AcNativeLibraryResolver initialized."); 
      }

      public void Terminate()
      {
      }
   }
}

