/// LayerFilterExtensionsApplication.cs
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license


using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using AcRx = Autodesk.AutoCAD.Runtime;
using System.Threading;
using System.Diagnostics;
using AcMgdLib.Runtime;


namespace DllImportResolver
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

