# Roadmap

- [x] A feature that has been completed
- [ ] A feature that has NOT yet been completed

Features that have a checkmark are complete and available for
download in the
[CI build](http://vsixgallery.com/extension/bae2a4ae-be17-4f34-be32-f7f103918589/).

# Change log

These are the changes to each version that has been released
on the official Visual Studio extension gallery.

## 1.0.1

- [x] Fixed issue where ToolsVersion was not being upgraded [#1].
- [x] Fixed issue where packages.config was deleted from the project and disk, but not source control [#2].
- [x] Fixed issue where the project XML header was removed [#3].

## 1.0

- [x] Initial release
- [x] Works in Visual Studio 2017 only.
- [x] Reads your packages.config and moves the references to your .csproj or .vbproj file.
  - [x] Removes all legacy reference nodes from the project file, including package related MSBuild targets and error conditions.
  - [x] Creates PackageReference nodes for each installed package.
  - [x] Makes a backup of your original files (just in case).
