/// ThisApplication.cs
/// 
/// ActivistInvestor / Tony Tanzillo
/// 
/// Distributed under the terms of the MIT license


using System.Diagnostics;
using System.Runtime.CompilerServices;
using AcMgdLib.Runtime;
using Autodesk.AutoCAD.Runtime;


namespace AcNativeLibraryResolverTest
{
   public class AcNativeLibraryResolverTestApplication : IExtensionApplication
   {
      public void Initialize()
      {
         AcNativeLibraryResolver.Initialize();
      }

      public void Terminate()
      {
      }

   }

}

