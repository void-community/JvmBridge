# Contributing

Read [**AGENTS.md**](AGENTS.md) for coding, ownership, generation, testing, and commit conventions.

Use the SDK versions listed in README and run `dotnet tool restore`, `dotnet restore JvmBridge.slnx --locked-mode`, `dotnet msbuild build.proj -t:VerifyGenerated`, then `dotnet test JvmBridge.slnx -c Release --no-restore`. Native changes require the package-consumer xUnit tests described in README. CI executes the complete available matrix.

Generated bindings and the ABI inspector are not handwritten. Change inputs or generation logic and regenerate them together with `dotnet msbuild build.proj -t:Generate`. `dotnet run --project src/JvmBridge.Maintenance -- update` intentionally refreshes all JDK inventory and header inputs; regular builds use the checked-in locks and never query moving latest releases.

Report defects with the exact package/commit, .NET SDK, JVM vendor and full version, OS/architecture, launch arguments, and a minimal standalone Java/C# example. Include native crash logs where available, after removing sensitive application data.

Use scoped Conventional Commits with a gitmoji and a body explaining observable changes. Public API changes need review; dependency-only updates may auto-merge after required checks. Never manually edit the generated changelog.
