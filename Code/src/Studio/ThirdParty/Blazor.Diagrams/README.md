# Blazor Diagrams .NET 11 compatibility build

This directory contains the unchanged UI source and static assets from
[Blazor.Diagrams commit 6ae19bf850630c0cee239c3ee904de8384eaa9cb](https://github.com/Blazor-Diagrams/Blazor.Diagrams/tree/6ae19bf850630c0cee239c3ee904de8384eaa9cb/src/Blazor.Diagrams),
the source revision recorded in the `Z.Blazor.Diagrams` 3.0.4.1 package.
The upstream MIT license is retained in `LICENSE`. `upstream.sha256` records the
original source/asset hashes.

The published package restricts its Blazor dependencies to versions below 11.
Until upstream publishes a compatible package, Studio's .NET 11 target builds
this UI layer against Blazor 11. .NET 8 and .NET 10 still use the original package.
The diagram engine remains the unchanged `Z.Blazor.Diagrams.Core` 3.0.4.1 package.

Only the project definition differs from upstream: it targets .NET 11, references
the published engine package, and does not pack or invoke a web compiler. The
upstream prebuilt JavaScript and CSS files are included directly. Assembly name,
version, namespace and `_content/Z.Blazor.Diagrams` asset paths stay unchanged.
The internal project ID is distinct so NuGet does not confuse it with the original
package when restoring the shared UI's .NET 8 and .NET 10 targets.
No sources are linked from another directory, fetched during builds or rewritten.

When a compatible upstream package is available, remove this project/reference,
restore the unconditional UI package reference and repeat the two-canvas,
save/reopen and Undo/Redo checks before removing this directory.
