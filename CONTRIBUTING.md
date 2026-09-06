# Contributing

Read [**AGENTS.md**](AGENTS.md) for coding, ownership, generation, testing, and commit conventions.

Use the pinned SDK and run `dotnet tool restore` followed by `python eng/check.py`. Native changes require package-consumer JVM tests; `python eng/test_agent.py --help` describes targeted runs. CI executes the complete available matrix.

Generated bindings and the ABI inspector are not handwritten. Change inputs or generation logic and regenerate them together. `python eng/update.py` intentionally refreshes all JDK inventory and header inputs; regular builds use the checked-in locks and never query moving latest releases.

Report defects with the exact package/commit, .NET SDK, JVM vendor and full version, OS/architecture, launch arguments, and a minimal standalone Java/C# example. Include native crash logs where available, after removing sensitive application data.

Use scoped Conventional Commits with a gitmoji and a body explaining observable changes. Public API changes need review; dependency-only updates may auto-merge after required checks. Never manually edit the generated changelog.
