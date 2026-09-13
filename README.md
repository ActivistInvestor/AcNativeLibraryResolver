### AcNativeLibraryResolver Class

AcNativeLibraryResolver is a utility class that provides a mechanism for dynamically resolving filenames of native libraries containing APIs that are imported and called by managed extensions using the DllImport attribute. It enables the use of AutoCAD wcmatch-style wildcard patterns in the dllName argument of the DllImport attribute. 

### Prerequisites:
This code requires AutoCAD 2025 and .NET 8.0 or later. Older AutoCAD releases and framework versions are not supported. The AcMgdLib repository contains an alternative solution that works on older AutoCAD/Framework versions, but is somewhat more complicated than this solution.

## The Problem:

When using the DllImport attribute to import a native api, you must explicitly specify the name of the dll containing that API. Several AutoCAD DLLs have *release-dependent filenames*, which means that their names change in each product release. For example, The library that provides the bulk of the ObjectDBX database component resides in a DLL whose name starts with "acdb" followed by two numeric digits that are the product release year. 

In AutoCAD 2025 this file's name is "acdb25.dll". In AutoCAD 2026, its name is "acdb26.dll", and so forth. Because the DllImport attribute normally requires you to explicitly hardwire the exact name of the library file containing the imported API, you can't use the same build of your assembly across different product releases in which the name of the DLL differs.

So for example, a build that targets releases of AutoCAD that use acdb25.dll (AutoCAD 2025), cannot be used with releases of AutoCAD that use acdb26.dll (AutoCAD 2026), and so forth, and in some cases, it may be due to nothing other than the use of the [DllImport] attribute to import functions from a DLL with a release-dependent filename.

For example:

```csharp
[DllImport("acdb25.dll", 
     CallingConvention.Cdecl, 
     EntryPoint = "?acdbSetDbmod@@YAHPEAVAcDbDatabase@@H@Z")]
static extern int acdbSetDbmod(IntPtr database, int newVal);
```

Nothing more than the above use of [DllImport] makes the assembly that contains it dependent on acdb25.dll (AutoCAD 2025), which means the same assembly cannot be used with other releases of AutoCAD in which the name of that DLL differs.

## The solution:


Using AcNativeLibraryResolver, you can specify a *wildcard pattern* for the dll name in your DllImport attribute, and AcNativeLibraryResolver will attempt to locate a loaded module whose name matches the wildcard pattern. 

The primiary use case for AcNativeLibraryResolver is calling native APIs that reside in DLLs that have release-dependent filenames.

The following is a list of common AutoCAD DLLs that have release-dependent names, and the wildcard patterns that can be used to resolve them to the correct file for the AutoCAD release which the code is running on, from AutoCAD 2025 and later. The '#' character in the pattern is a wildcard that matches a single digit, which is the release number of the AutoCAD DLL. Note that the files listed may not be included with all supported releases (AutoCAD 2025 or later).

|AutoCAD 2025 filename|Recommended Wildcard pattern|
|---------------------|----------------------------|
|adui25.dll           |      adui2#.dll|
|acdb25.dll           |acdb2#.dll|
|AcDimX25.dll         |AcDimX2#.dll|
|acge25.dll           |acge2#.dll|
|acgex25.dll          |acgex2#.dll|
|AcGradient25.dll     |AcGradient2#.dll|
|AcPersSubentNaming25.dll|AcPersSubentNaming2#.dll|
|acui25.dll           |acui2#.dll|
|AcWebDAV25.dll       |AcWebDAV2#.dll|
|atlst25.dll          |atlst2#.dll|
|hcreg25.dll          |hcreg2#.dll|
|heidi25.dll          |heidi2#.dll|
|modlr25.dll          |modlr2#.dll|
|oletohdi25.dll       |oletohdi2#.dll|
|plotcfg25.dll        |plotcfg2#.dll|
|pm25.dll             |pm2#.dll|
|pmutil25.dll         |pmutil2#.dll|
|regacad25.dll        |regacad2#.dll|

## Automatic resolution of DllImport DLL filenames

In addition to supporting the use of wildcards in the DllImport attribute's dllName argument, AcNativeLibraryResolver will *automatically* replace mismatched version-dependent filenames with the correct filename for the AutoCAD release the code is running on.

For example, given this:

   ```[DllImport("acdb24.dll", ...)]```
   
When running on any release of AutoCAD (starting with AutoCAD 2025 or later), the dllName argument in the above DllImport attribute is automatically replaced as follows:

   |AutoCAD Release    |Replacement filename|
   |----------------|--------------------------|
   |2025|acdb25.dll
   |2026|acdb26.dll
   |2027|acdb27.dll
   
Automatic version-dependent filename resolution works for any of the above listed AutoCAD dlls having version-dependent names.

## Usage

The following shows the above example DllImport attribute used to import the acdbSetDbmod() native API, using a wildcard dllName that will work on any AutoCAD release from AutoCAD 2025 or later.

```csharp
[DllImport("acdb2#.dll", 
     CallingConvention.Cdecl, 
     EntryPoint = "?acdbSetDbmod@@YAHPEAVAcDbDatabase@@H@Z")]
static extern int acdbSetDbmod(IntPtr database, int newVal);
```

The only difference between the two examples is the use of the "acdb2#" wildcard in the dllName argument of the DllImport attribute. It's just that simple. 

AcNativeLibraryResolver allows you to call native AutoCAD APIs that live in DLLs that have release-dependent filenames *without having to hard-code the name of the DLL into your code*.

Enabling wildcard support in the DllImport attribute's dllName argument only requires a call to the AcNativeLibraryResolver's `Initialize()` method prior to calling any imported APIs that are marked with the DllImport attribute. The included project contains example/test code with an IExtensionApplication whose Initialize() method calls the AcNativeLibraryResolver's Initialize() method. 

```csharp
public class MyApplication : IExtensionApplication
{
   public void Initialize()
   {
       AcNativeLibraryResolver.Initialize();
   }
   
   public void Terminate()
   {
   }
}
```

If multiple .NET assemblies are being used which require DllImport resolution, it is highly-recommended that the AcNativeLibraryResolver be deployed as a separate assembly and be referenced from each assembly that requires its services.