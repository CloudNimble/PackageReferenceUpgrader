# BindingRedirects Doctor

<!-- Replace this badge with your own-->
[![Build status](https://ci.appveyor.com/api/projects/status/d1wl3gdb8hn8ngc3?svg=true)](https://ci.appveyor.com/project/robertmclaws/bindingredirectsdoctor)

<!-- Update the VS Gallery link after you upload the VSIX-->
Download this extension from the [VS Gallery](https://visualstudiogallery.msdn.microsoft.com/[GuidFromGallery])
or get the [CI build](http://vsixgallery.com/extension/3d111d8d-7d15-4c6e-8ca3-494426e539ff/).

---------------------------------------

Cleans and sorts the Assembly Binding Redirects in your projects to make them more manageable.

See the [change log](CHANGELOG.md) for changes and road map.

## Features

- Assembly binding redirect cleanup.

### Assembly binding redirect cleanup
- Removes duplicates (especially common with .NET Core / .NET Standard apps and mixed solutions).
- Automatically uses the latest version of an assembly for any duplicate redirects.
- Sorts the redirects for easy manageability (especially when using source control).

![Context Menu](art/context-menu.png)

## Contribute
Check out the [contribution guidelines](CONTRIBUTING.md)
if you want to contribute to this project.

For cloning and building this project yourself, make sure
to install the
[Extensibility Tools 2015](https://visualstudiogallery.msdn.microsoft.com/ab39a092-1343-46e2-b0f1-6a3f91155aa6)
extension for Visual Studio which enables some features
used by this project.

## License
[Apache 2.0](LICENSE)