# Footprint

What loading an agent costs, measured on CI runners with a minimal agent (`samples/FootprintAgent`: one helper class and one patch) and `java -version` on HotSpot. Launches with and without the agent alternate, and the start-up figures are medians of 20 runs. Peak memory is the resident set high-water mark of a trivial Java program on Linux and is blank elsewhere.

The library size of each row is the baseline that CI holds the next build to: growth beyond 10 percent fails the footprint job. Start-up time and memory are reported in each run's job summary but never fail a build, because shared runners are too noisy: a difference of a few milliseconds is within that noise, and a negative figure means exactly that. Regenerate this page and the baseline deliberately with `dotnet run --project src/JvmBridge.Maintenance -- footprint <directory with the footprint-* artifacts>`.

| Target | Java | Library (MiB) | Start-up without (ms) | Start-up with (ms) | Added (ms) | Peak memory added (MiB) |
|---|---|---|---|---|---|---|
| linux-arm64 | Java 8 | 2.02 | 33.8 | 42.1 | 8.3 | 2.5 |
| linux-arm64 | Java 17 | 2.02 | 29.8 | 36.5 | 6.7 | 2.4 |
| linux-arm64 | Java 21 | 2.02 | 31.9 | 39.7 | 7.8 | 2.4 |
| linux-x64 | Java 8 | 1.97 | 55.6 | 65.4 | 9.8 | 2.5 |
| linux-x64 | Java 17 | 1.97 | 40.9 | 49.9 | 9.0 | 2.3 |
| linux-x64 | Java 21 | 1.97 | 31.2 | 37.8 | 6.5 | 2.5 |
| osx-arm64 | Java 8 | 1.86 | 106.1 | 104.7 | -1.5 |  |
| osx-arm64 | Java 17 | 1.86 | 95.0 | 99.3 | 4.3 |  |
| osx-arm64 | Java 21 | 1.86 | 63.3 | 67.4 | 4.1 |  |
| osx-x64 | Java 8 | 1.94 | 107.3 | 103.1 | -4.2 |  |
| osx-x64 | Java 17 | 1.94 | 115.1 | 118.8 | 3.7 |  |
| osx-x64 | Java 21 | 1.94 | 178.2 | 181.1 | 2.9 |  |
| win-arm64 | Java 17 | 1.92 | 110.4 | 111.9 | 1.5 |  |
| win-arm64 | Java 21 | 1.92 | 102.9 | 107.9 | 5.0 |  |
| win-x64 | Java 8 | 1.90 | 69.1 | 72.5 | 3.4 |  |
| win-x64 | Java 17 | 1.90 | 61.2 | 70.1 | 8.9 |  |
| win-x64 | Java 21 | 1.90 | 61.5 | 72.0 | 10.5 |  |
