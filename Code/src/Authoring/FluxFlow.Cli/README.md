# FluxFlow CLI

Install the .NET local tool from a feed containing the tool package. Use a tool manifest in the directory where you build workflows.

~~~powershell
dotnet new tool-manifest
dotnet tool install --local dotnet-fluxflow --version 1.0.0-alpha.2 --add-source ./feed
$packages = (Get-ChildItem ./feed -Filter '*.nupkg').FullName
dotnet fluxflow generate ./inputs/application.json ./App.cs @packages
~~~

Commit the tool manifest to pin the CLI version. On another machine with the feed configured, run `dotnet tool restore`. The command runs on .NET 10 and 11. Supply the family mapping packages for your workflow. The generated source pins the packages it uses and can be published with `dotnet publish App.cs`.
