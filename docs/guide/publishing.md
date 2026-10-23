# Publishing for Linux

A NativeAOT agent links the .NET runtime into its library, and the runtime calls glibc. The library therefore needs the glibc of the machine that published it, or newer: an agent published on `ubuntu-24.04` asks for `GLIBC_2.34` and does not load on Ubuntu 20.04, Debian 11, RHEL 8 or the `focal` images of eclipse-temurin:

```text
Could not find agent library /a/MyAgent.so in absolute path, with error: /lib/aarch64-linux-gnu/libc.so.6: version `GLIBC_2.32' not found (required by /a/MyAgent.so)
```

The jar route fails the same way, as `UnsatisfiedLinkError` and `processing of -javaagent failed`. None of these symbols come from your code, so every agent published the same way has the same floor.

## Publishing against an old glibc

.NET builds its own Linux binaries against the root file systems in its cross-build images, which are Ubuntu 18.04 for x64 and arm64. Publishing against that root file system as a *sysroot* gives an agent the same low floor:

| Target | Published on the runner | Published against the sysroot |
|---|---|---|
| `linux-x64` | glibc 2.34 | **glibc 2.17** (it asks for symbols up to `GLIBC_2.16`) |
| `linux-arm64` | glibc 2.34 | **glibc 2.17** |
| `linux-arm` | glibc 2.34 | glibc 2.34: the arm32 root file system of .NET 10's image is newer (its libc has `GLIBC_2.35`) |

glibc 2.17 covers CentOS and RHEL 7, Ubuntu 18.04 and 20.04, Debian 10 and 11, and everything newer. The integration tests run the HelloAgent sample, published this way, on CentOS 7 (glibc 2.17) with Java 8, 17 and 21, through `-agentpath` and through its jar, and fail when the library asks for a newer glibc. musl (`linux-musl-*`) is a different C library and is not affected.

### On GitHub Actions

The `glibc-sysroot` action of [**void-community/Actions**](https://github.com/void-community/Actions) copies the root file system out of .NET's pinned cross-build image, installs clang and lld, and gives the properties to pass to `dotnet publish`. It works on x64 and arm64 runners (the image itself only runs on x64, so the action copies files out of it instead of building inside it):

```yaml
- name: Prepare the glibc 2.17 sysroot
  id: sysroot
  if: runner.os == 'Linux'
  uses: void-community/actions/glibc-sysroot@779ebfbf56a61e10114658c560da49324f0f2380 # main
  with:
    rid: ${{ matrix.rid }}   # linux-x64 or linux-arm64
- run: dotnet publish MyAgent.csproj -c Release -r ${{ matrix.rid }} --self-contained ${{ steps.sysroot.outputs.properties }} -o artifacts/agent/${{ matrix.rid }}
```

That repository has no tags: pin a commit of `main` and keep the `# main` comment, and Renovate opens an update when the action changes. The properties are `-p:SysRoot=/crossrootfs/<arch> -p:LinkerFlavor=lld`, plus `-p:ObjCopyName=llvm-objcopy` when the runner's architecture differs from the target's. The `jvmbridge-agent` template's workflow does this for its Linux platforms.

### Anywhere else

The same works on any Linux machine with Docker, clang, lld and the .NET SDK:

```sh
image=mcr.microsoft.com/dotnet-buildtools/prereqs:azurelinux-3.0-net10.0-cross-arm64   # -cross-amd64 for linux-x64
container=$(docker create --platform linux/amd64 "$image" /bin/true)
sudo mkdir -p /crossrootfs && sudo docker cp "$container:/crossrootfs/arm64" /crossrootfs/   # /crossrootfs/x64 for linux-x64
docker rm "$container"
dotnet publish MyAgent.csproj -c Release -r linux-arm64 --self-contained -p:SysRoot=/crossrootfs/arm64 -p:LinkerFlavor=lld
```

Keep the root file system at `/crossrootfs/<arch>`: its `lib` is a link to `/crossrootfs/<arch>/usr/lib`. A `JvmBridgeAgentJar` publish packs the library it links, so the jar has the same floor.

### Checking an agent

`readelf -V MyAgent.so | grep -o 'GLIBC_[0-9.]*' | sort -uV | tail -1` prints the newest glibc version the library asks for; the system needs at least that one (`ldd --version` prints what a system has).
