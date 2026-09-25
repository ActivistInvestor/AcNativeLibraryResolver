### AcNativeLibraryResolver Class

AcNativeLibraryResolver is a utility/helper class that performs dynamic resolution of the filenames of native libraries containing APIs that are imported and called by managed extensions using the [DllImport attribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.dllimportattribute?view=net-10.0) or the [LibraryImport attribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.libraryimportattribute?view=net-10.0).

The principle objective of AcNativeLibraryResolver is to allow you to import and call native AutoCAD APIs that live in DLLs that have *release-dependent filenames* in a way that allows you to *avoid having to hardwire the exact filenames of those DLLs into your code*, thereby allowing the code to be portable across multiple AutoCAD product releases.


### Prerequisites:
AcNativeLibraryResolver *requires AutoCAD 2025 and .NET 8.0 or later*. Older AutoCAD releases and framework versions are not supported. The [AcMgdLib](https://github.com/ActivistInvestor/AcMgdLib) repository contains an alternative solution that works on older AutoCAD/Framework versions, but is far-more complicated to use than this solution. See [DllImport.cs](https://github.com/ActivistInvestor/AcMgdLib/blob/main/AcMgdLib/Common/DllImport.cs) & [AcDbNativeMethods.cs](https://github.com/ActivistInvestor/AcMgdLib/blob/main/AcMgdLib/Common/AcDbNativeMethods.cs)

## Supported Functionality

AcNativeLibraryResolver supports the following basic operations on the dllName argument passed to the DllImport attribute:

### Wcmatch-style wildcards:
     
You can specify AutoCAD style wildcard patterns in the dllName argument. A wildcard pattern must match exactly *one and only one* loaded module, or module filename in the base directory. The entire argument is replaced with the matching filename.
  
### Mismatched release-dependent module names:
  
If the filename in the dllName argument ends with exactly two numeric digits, and there is no module found having that filename, the two numeric digits are replaced with that of the current product release (e.g., 25, 26, 27, etc). 
  
Hence, the dllName argument `"acdb24.dll"` will be replaced with `"acdb25.dll"` on AutoCAD 2025; `"acdb26.dll"` on AutoCAD 2026, and so on.
  
### Host executable name resolution:
  
If the dllName argument is `"acad.exe"`, and the filename of the current process is not `"acad.exe"`, the `"acad.exe"` argument is replaced with the filename of the current process. Hence, `"acad.exe"` is always interpreted as the name of the current process executable, allowing code that imports APIs from it to be portable across multiple product veriants that may use different executable names.

## Background

AcNativeLibraryResolver was designed to solve a basic problem that complicates importing and calling native APIs in AutoCAD managed extensions, that has existed since the AutoCAD .NET API was introduced over 20 years ago.

### The Problem:

When using the [DllImport attribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.dllimportattribute?view=net-10.0) (or the [LibraryImport attribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.libraryimportattribute?view=net-10.0)) to import a native api, you must *explicitly* specify the name of the library containing that API in the form of a *compile-time constant*:

```csharp
[DllImport("acdb25.dll", 
     CallingConvention.Cdecl, 
     EntryPoint = "?acdbSetDbmod@@YAHPEAVAcDbDatabase@@H@Z")]

static extern int acdbSetDbmod(IntPtr database, int newVal);
```

Several AutoCAD DLLs have *release-dependent filenames*, which means that their filenames *change in each product release*. For example, The library that provides the implementation of the AutoCAD database resides in a DLL whose name starts with "acdb" followed by two numeric digits that are the product release year, which is the library that is referenced in the above example.

In AutoCAD 2025 that file's name is `acdb25.dll`. In AutoCAD 2026, its name is `acdb26.dll`, and so forth. Because the DllImport attribute normally requires you to *explicitly hardwire* the exact name of the library file containing the imported API in your code, you can't use the same build of your assembly across different product releases, because the name of that library changes in each release.

Nothing more than the use of the [DllImport] attribute shown above makes the assembly that contains it dependent on acdb25.dll (AutoCAD 2025), which means the same assembly cannot be used with other releases of AutoCAD in which the name of that DLL differs.

### A Solution:

AcNativeLibraryResolver allows you to specify *wildcard patterns* in the dllName argument of DllImport attributes. When you specify a wildcard as the name of the module, AcNativeLibraryResolver will attempt to locate a loaded module whose name matches the wildcard pattern, and will replace the pattern with the name of the matching dll. If it doesn't find a loaded module whose name matches the pattern, it will look for a dll file in the base folder (where the process executable resides) and if a match is found, it loads the module. 

The primiary use case for AcNativeLibraryResolver is calling native APIs that reside in DLLs that have *release-dependent filenames*.

The following is a list of common AutoCAD DLLs that have release-dependent names, and the wildcard patterns that can be used to resolve them to the correct file for the AutoCAD release which the code is running on, from AutoCAD 2025 and later. The '#' character in the pattern is a wildcard that matches a single digit, which is the release number of the AutoCAD DLL. Note that the files listed may not be included with all supported releases (AutoCAD 2025 or later).

|AutoCAD 2025 filename|Recommended Wildcard pattern|
|---------------------|----------------------------|
|adui25.dll           |      adui2#.dll|
|**acdb25.dll**           |acdb2#.dll|
|AcDimX25.dll         |AcDimX2#.dll|
|acge25.dll           |acge2#.dll|
|**acgex25.dll**          |acgex2#.dll|
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

#### A word of caution regarding the use of wildcards: 

Wildcard patterns used in the DllImport attribute's dllName argument must match the name of ***one and only one*** loaded module. If a wildcard pattern matches the names of multiple loaded modules, it is ambiguous and is treated as an error (usually a DllNotFoundException). For this reason, one should always use the *most-restrictive matching wildcard* possible, which are those listed in the above table. If for example, you used `"acdb*.dll"` as a wildcard, it will match multiple loaded modules and result in a failure.

For the AutoCAD database implementation DLL (acdbXX.dll), you can avoid using wildcards entirely by instead using the special token `"ACDB_DLL"` as the dllName argument. This token is recognized by AcNativeLibraryResolver and always resolves to acdbXX.dll.

### Autonomous mismatched release-dependent DLL filename resolution

In addition to enabling the use of wildcards in the DllImport attribute's dllName argument, AcNativeLibraryResolver can *automatically* recognize and replace mismatched release-dependent dll filenames with the correct filename for the AutoCAD release the code is running on.

For example, given this:

   ```[DllImport("acdb24.dll", ...)]```
   
When running on AutoCAD 2025 or any later release, the dllName argument in the above DllImport attribute is *automatically* replaced as follows:

   |AutoCAD Release    |Replacement filename|
   |----------------|--------------------------|
   |2025|acdb25.dll
   |2026|acdb26.dll
   |2027|acdb27.dll
   
Mismatched release-dependent filename detection/resolution works for any of the above listed AutoCAD dlls having release-dependent names, provided they reside in the same folder where the current process executable (e.g., acad.exe) is located.


### Usage

The following shows the above example DllImport attribute used to import the `acdbSetDbmod()` native API, using a wildcard dllName argument *that will work on any AutoCAD release* (starting from AutoCAD 2025).

```csharp
[DllImport("acdb2#.dll", 
     CallingConvention.Cdecl, 
     EntryPoint = "?acdbSetDbmod@@YAHPEAVAcDbDatabase@@H@Z")]
static extern int acdbSetDbmod(IntPtr database, int newVal);
```

The only difference between the two examples is the use of the `"acdb2#"` wildcard in the dllName argument of the DllImport attribute. It's just that simple. 

Enabling dynamic resolution of the DllImport attribute's dllName argument only requires a call to the AcNativeLibraryResolver's `Initialize()` method, prior to calling any imported APIs that are marked with the DllImport attribute. The included project contains example/test code with an IExtensionApplication whose Initialize() method calls the AcNativeLibraryResolver's Initialize() method. 

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

If multiple .NET assemblies require dynamic DllImport  resolution, it is highly-recommended that the AcNativeLibraryResolver be deployed as a separate assembly and be referenced from each assembly that requires its services.

## AcDbModuleResolver class

In addition to AcNativeLibraryResolver, this repository also includes the **AcDbModuleResolver** class, which is a lightweight/minimal (and limited) implementation of AcNativeLibraryResolver, that only resolves the module name of the AutoCAD database implementation dll (acdbXX.dll).

If your needs are limited to importing and calling APIs in acdbXX.dll, this class provides the same functionality as AcNativeLibraryResolver, in a lightwight form.