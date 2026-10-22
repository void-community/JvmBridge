# Footprint

What loading an agent costs, measured on CI runners with a minimal agent (`samples/FootprintAgent`: one helper class and one patch) and `java -version` on HotSpot. Launches with and without the agent alternate, and the start-up figures are medians of 20 runs. Peak memory is the resident set high-water mark of a trivial Java program on Linux and is blank elsewhere.

The library size of each row is the baseline that CI holds the next build to: growth beyond 10 percent turns the footprint check red, without holding back a publish. Start-up time and memory are reported in each run's job summary but never fail a build, because shared runners are too noisy: a difference of a few milliseconds is within that noise, and a negative figure means exactly that. Regenerate this page and the baseline deliberately with `dotnet run --project src/JvmBridge.Maintenance -- footprint <directory with the footprint-* artifacts>`.

| Target | Java | Library (MiB) | Start-up without (ms) | Start-up with (ms) | Added (ms) | Peak memory added (MiB) |
|---|---|---|---|---|---|---|
| linux-arm64 | Java 8 | 2.14 | 35.1 | 42.7 | 7.7 | 2.8 |
| linux-arm64 | Java 17 | 2.14 | 31.2 | 38.2 | 7.0 | 2.7 |
| linux-arm64 | Java 21 | 2.14 | 30.4 | 38.5 | 8.1 | 4.9 |
| linux-x64 | Java 8 | 2.14 | 55.3 | 63.4 | 8.1 | 2.8 |
| linux-x64 | Java 17 | 2.14 | 39.7 | 48.8 | 9.1 | 2.6 |
| linux-x64 | Java 21 | 2.14 | 27.0 | 33.6 | 6.5 | 2.7 |
| osx-arm64 | Java 8 | 2.02 | 66.2 | 73.6 | 7.4 |  |
| osx-arm64 | Java 17 | 2.02 | 62.6 | 63.1 | 0.5 |  |
| osx-arm64 | Java 21 | 2.02 | 46.7 | 54.8 | 8.0 |  |
| osx-x64 | Java 8 | 2.11 | 127.1 | 131.7 | 4.6 |  |
| osx-x64 | Java 17 | 2.11 | 178.3 | 193.0 | 14.7 |  |
| osx-x64 | Java 21 | 2.11 | 187.9 | 186.3 | -1.6 |  |
| win-arm64 | Java 17 | 2.14 | 109.9 | 111.8 | 1.9 |  |
| win-arm64 | Java 21 | 2.14 | 99.0 | 103.6 | 4.6 |  |
| win-x64 | Java 8 | 2.12 | 93.8 | 102.3 | 8.5 |  |
| win-x64 | Java 17 | 2.12 | 66.8 | 74.2 | 7.4 |  |
| win-x64 | Java 21 | 2.12 | 71.0 | 82.0 | 11.0 |  |
