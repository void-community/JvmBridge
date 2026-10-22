# Footprint

What loading an agent costs, measured on CI runners with a minimal agent (`samples/FootprintAgent`: one helper class and one patch) and `java -version` on HotSpot. Launches with and without the agent alternate, and the start-up figures are medians of 20 runs. Peak memory is the resident set high-water mark of a trivial Java program on Linux and is blank elsewhere.

The library size of each row is the baseline that CI holds the next build to: growth beyond 10 percent fails the footprint job. Start-up time and memory are reported in each run's job summary but never fail a build, because shared runners are too noisy. Regenerate this page and the baseline deliberately with `dotnet run --project src/JvmBridge.Maintenance -- footprint <directory with the footprint-* artifacts>`.


No CI measurement has been recorded yet. The table appears here after the first footprint run, with one row per target and Java version.
