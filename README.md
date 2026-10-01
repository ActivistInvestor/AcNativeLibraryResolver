### AcNativeLibraryResolver Class

AcNativeLibraryResolver is a utility/helper class that performs dynamic resolution of the filenames of native libraries containing APIs that are imported and called by AutoCAD managed extensions using the [DllImport attribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.dllimportattribute?view=net-10.0) or the [LibraryImport attribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.libraryimportattribute?view=net-10.0).

The purpose of AcNativeLibraryResolver is to allow you to import and call native AutoCAD APIs that reside in DLLs that have *release-dependent filenames*, without having to *hard-wire the exact filenames of those DLLs into your source code*. 

By eliminating hard-wired, release-dependent filenames from your source code, you eliminate a dependence on a specific product release, making both the source code and binaries produced from it *portable* across multiple AutoCAD product releases.

### Prerequisites:
AcNativeLibraryResolver *requires AutoCAD 2025 and .NET 8.0 or later*. Older AutoCAD releases and framework versions are not supported. The [AcMgdLib](https://github.com/ActivistInvestor/AcMgdLib) repository contains an alternative solution that works on older AutoCAD/Framework versions, but is vastly-more complicated to use than this solution. See [DllImport.cs](https://github.com/ActivistInvestor/AcMgdLib/blob/main/AcMgdLib/Common/DllImport.cs) & [AcDbNativeMethods.cs](https://github.com/ActivistInvestor/AcMgdLib/blob/main/AcMgdLib/Common/AcDbNativeMethods.cs)

## Supported Functionality

AcNativeLibraryResolver supports the following basic operations on the dllName argument passed to the DllImport attribute:

### Wcmatch-style wildcards:
     
You can specify AutoCAD style *wildcard* patterns in the dllName argument. A wildcard pattern must match exactly *one and only one* loaded module, or module filename in the base directory. If a match is found, it replaces the argument.
  
### Mismatched release-dependent module names:
  
If the filename in the dllName argument ends with exactly two numeric digits, and there is no module found having that filename, the two numeric digits are replaced with that of the current product release (e.g., 25, 26, 27, etc). 
  
Hence, the dllName argument `"acdb24.dll"` will be replaced with `"acdb25.dll"` on AutoCAD 2025; `"acdb26.dll"` on AutoCAD 2026, and so on.
  
### Host executable name resolution:
  
If the dllName argument is `"acad.exe"`, and the filename of the current process is not `"acad.exe"`, the `"acad.exe"` argument is replaced with the filename of the current process. Hence, `"acad.exe"` is always interpreted as the name of the current process executable, allowing code that imports APIs from it to be portable across multiple product variants that may have different executable names.

## Background

AcNativeLibraryResolver was designed to address a problem that immensely-complicates importing and calling native APIs in AutoCAD managed extensions, one that has existed since the AutoCAD .NET API was introduced over 20 years ago, and for that long, has discouraged developers from using P/Invoke.

### The Problem:

When using the [DllImport attribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.dllimportattribute?view=net-10.0) (or the [LibraryImport attribute](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.libraryimportattribute?view=net-10.0)) to import a native api, you must *explicitly* specify the name of the library containing that API as a *compile-time constant*:
```csharp
[DllImport("acdb25.dll", 
     CallingConvention.Cdecl, 
     EntryPoint = "?acdbSetDbmod@@YAHPEAVAcDbDatabase@@H@Z")]

static extern int acdbSetDbmod(IntPtr database, int newVal);
```
Several AutoCAD DLLs have *release-dependent filenames*, which means that their filenames *change in each product release*. For example, The library that provides the implementation of the AutoCAD database resides in a DLL whose name starts with "acdb" followed by two numeric digits that are the product release year, which is the library that is referenced in the above example.

In AutoCAD 2025 that file's name is `acdb25.dll`. In AutoCAD 2026, its name is `acdb26.dll`, and so forth. Because the DllImport attribute normally requires you to *explicitly hardwire* the exact name of the library file containing the imported API in your code, you can't use the same build of your assembly across different product releases, because the name of that library changes in each release.

Nothing more than the use of the [DllImport] attribute shown above makes the assembly that contains it dependent on acdb25.dll (AutoCAD 2025), which means the same assembly cannot be used with other releases of AutoCAD in which the name of that DLL differs. Instead you must generate multiple builds of your assembly, one for each release of AutoCAD that you want to support. This is a major inconvenience, and is the reason why many developers avoid using P/Invoke in their managed extensions.

### A Solution:

AcNativeLibraryResolver allows you to specify *wildcard patterns* in the dllName argument of DllImport attributes. When you specify a wildcard as the name of the module, AcNativeLibraryResolver will attempt to locate a loaded module whose name matches the wildcard pattern, and will replace the pattern with the name of the matching dll. If it doesn't find a loaded module whose name matches the pattern, it will look for a file in the base folder (where the process executable resides) and if a match is found, it loads the module. 

The primiary use case for AcNativeLibraryResolver is calling native APIs that reside in DLLs that have *release-dependent filenames*.

The following is a list of common AutoCAD DLLs that have release-dependent names, and the wildcard patterns that can be used to resolve them to the correct file for the AutoCAD release which the code is running on, from AutoCAD 2025 and later. The `'#'` character in the pattern is a wildcard that matches a single digit, which is the last digit othe release number of the AutoCAD DLL. Note that the files listed may not be present in all supported releases (AutoCAD 2025 or later).

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

If a wildcard pattern matches multiple loaded modules, it is ambiguous and is treated as an error (usually manifesting in the form of a DllNotFoundException). The match is performed against the names of all loaded modules and all unloaded modules residing in the base directory where the process executable is located. For this reason, one should always use the *most-restrictive matching wildcard* possible, which are those listed in the above table. If for example, you used `"acdb*.dll"` as a wildcard, it will match multiple loaded modules and result in a failure. 

For the AutoCAD database implementation DLL (acdbXX.dll), you can avoid using wildcards entirely by instead using the special token `"ACDB_DLL"` as the dllName argument. This token is recognized by AcNativeLibraryResolver and always resolves to acdbXX.dll.

### Mismatched release-dependent DLL filename resolution

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

With AcNativeLibraryResolver loaded, existing managed extensions that were compiled from source code that included release-dependent filenames in their DllImport attributes can be used without error on any supported AutoCAD release.

### Usage

The following shows the above example DllImport attribute used to import the `acdbSetDbmod()` native API, using a wildcard dllName argument *that will work on any AutoCAD release* (starting from AutoCAD 2025).

```csharp
[DllImport("acdb2#.dll", 
     CallingConvention.Cdecl, 
     EntryPoint = "?acdbSetDbmod@@YAHPEAVAcDbDatabase@@H@Z")]
static extern int acdbSetDbmod(IntPtr database, int newVal);
```

The only difference between the two examples is the use of the `"acdb2#"` wildcard in the dllName argument of the DllImport attribute. It's just that simple. 

Enabling dynamic resolution of the DllImport attribute's dllName argument only requires a call to the AcNativeLibraryResolver's `Initialize()` method, prior to calling any imported APIs that are marked with the DllImport attribute. The included project contains example/test code with an `IExtensionApplication` whose `Initialize()` method shows the necessary step. 

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

If multiple .NET assemblies require dynamic DllImport resolution, it is highly-recommended that the AcNativeLibraryResolver be deployed as a separate assembly and be referenced from each assembly that requires its services. The Initialize() method can be called any number of times.

## Supported file types

If a module of any type is already loaded, its module handle will be returned. However, for unloaded modules, implicit loading is limited to .dll files (.dbx files have not been tested). In the shipping base AutoCAD product, there are currently no known .arx/.crx libraries with release-dependent filenames.

## AcDbModuleResolver class

In addition to AcNativeLibraryResolver, this repository also includes the **AcDbModuleResolver** class, which is a lightweight/minimal implementation of AcNativeLibraryResolver, that only resolves the module name of the AutoCAD database implementation dll (acdbXX.dll).

If your needs are limited to importing and calling APIs in acdbXX.dll, this class provides the same functionality as AcNativeLibraryResolver, in a lightwight package.

## DllExportDumper class

The DllExportDumper class implements 2 AutoCAD commands that dump the signatures and entry points of native APIs exported by any currently-loaded module, matching a specified wildcard pattern. You can specify a wildcard pattern for both the module and the export symbol name. 

This 'bonus' utility is extremely useful for finding exported native APIs contained in *any* loaded module in the process. It is unique in that it can search across all loaded modules, unlike most similar standalone tools (such as Process Informer) that limit the search scope to a single module.

There are two commands included. The `DLLEXPORTS` command dumps the results to the AutoCAD command line, while the `DLLEXPORTSOUT` command dumps the results to a text file (`AcDllExports.txt`) in the MyDocuments folder and then opens it with the default text editor. The output includes the module name, signature, and entrypoint symbol of each matching export.

