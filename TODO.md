# TODO

This file tracks remaining release and maintenance work for PLC IO Checker Project Builder.

## Release Follow-Up

- [ ] Decide whether the Windows release should be code-signed.
  Current GitHub release packaging publishes a self-contained single-file EXE without an installer or code signature.
- [ ] Confirm the GitHub Release asset after each release.
  Check that `PlcIoCheckerProjectBuilder-win-x64.zip` contains `PlcIoCheckerProjectBuilder.exe` and that the app starts on a clean Windows PC.
- [ ] Confirm the public manual links before each release.
  The app Help menu opens `https://fa-yoshinobu.github.io/PlcIoChecker_Site/`, and the top header link opens `https://fa-yoshinobu.github.io/PlcIoChecker_Site/projectbuilder/projectbuilder.html`.
- [ ] Decide whether to keep ZIP-only distribution or add an installer.
  Current distribution is ZIP only from GitHub Releases.

## Maintenance Notes

- [x] Build output is ignored.
  `dotnet/publish/` and `artifacts/` are intentionally excluded from Git.
- [x] ProjectBuilder manual links are present in the app UI.
  Help menu opens the general manual site, and the header link opens the ProjectBuilder manual page.
- [x] Release build path is documented.
  Use `build-dotnet-onefile.bat` to generate `dotnet/publish/win-x64/PlcIoCheckerProjectBuilder.exe`.
