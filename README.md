# backpack-minisync

A small command line tool that syncs **specific versions** of npm and NuGet
packages, plus **only the dependency versions needed to install them**, to a
directory on disk, ready to carry into an offline network.

It is the minimal sibling of [backpack](https://github.com/linus-berg/backpack).
Backpack mirrors every version of every package it meets, and it needs
RabbitMQ, MongoDB, Redis, S3 and an OIDC provider to do so. minisync is a single
executable that runs in a terminal. You ask for `react@19.0` and get React
19.0.8, `react-dom` 19.0.8 and `scheduler` 0.25.0: three tarballs, 1.1 MB.
There are no services, no database and no queues, and everything is stored on
the local disk.

Files are written in the same layout backpack uses for its S3 keys, so tooling
that consumes a backpack bucket can consume a minisync directory.

## Quick start (Windows)

Download `minisync-win-x64.exe` from the
[releases](https://github.com/linus-berg/backpack-minisync/releases), rename it
to `minisync.exe` and copy it to the server (or [build it](#building)). It is
self-contained, so .NET does not need to be installed. Then:

```powershell
minisync init                                       # creates minisync.json
minisync add npm react@19.0 react-dom@19.0
minisync add nuget Newtonsoft.Json@13.0.3 Serilog.AspNetCore@8.0.3
minisync sync --dry-run                             # see exactly what will be downloaded
minisync sync
```

The packages end up in `data\` next to `minisync.json`. Run `minisync sync`
again whenever the config changes. Files that are already on disk are never
downloaded twice.

## Commands

| Command | |
| --- | --- |
| `minisync init` | Create `minisync.json` with every setting at its default. |
| `minisync add <npm\|nuget> <name>[@<version>]...` | Add packages. Each one is resolved first, so typos fail here instead of during the sync. Without a version, the current latest version is pinned. Adding a second version of a package keeps both. |
| `minisync remove <npm\|nuget> <name>[@<version>]...` | Remove a package, or a single version of it. |
| `minisync list` | Show the configured packages. |
| `minisync sync` | Resolve everything and download what is missing. |

| Option | |
| --- | --- |
| `-c, --config <file>` | Config file, default `minisync.json`. |
| `-n, --dry-run` | Resolve and list every package (`new` or `have`) along with the package that pulled it in. Nothing is downloaded. |
| `-o, --output <dir>` | Use another output directory for this run. |
| `--only <npm\|nuget>` | Sync one ecosystem. |
| `-v, --verbose` | Show retries, skipped platform packages and other details. |

Exit codes: `0` success, `1` something could not be resolved or downloaded
(everything else is still synced), `2` invalid command line, `130` cancelled
with Ctrl+C.

## How versions are chosen

Every package request (a root in the config, or a dependency of a package
already selected) resolves to **one** version. Each ecosystem follows the rules
of its own client, so the offline side ends up with the versions that
`npm install` or `dotnet restore` will actually ask for.

**npm**, following npm's own `npm-pick-manifest` rules:

- A spec is anything npm accepts from a registry: `19.0.0` (exact), `19.0`
  (any 19.0.x), `^19.0.0`, `~1.2`, `>=1 <2`, `1.x`, or a tag such as `latest`.
- The `latest` tag is used if it satisfies the spec. Otherwise the highest
  satisfying version is used, and deprecated versions are avoided where
  possible. Prereleases are only chosen when the spec names one.
- A dependency first reuses a version that is already selected, if one
  satisfies it. This is the same deduplication npm performs, and it keeps the
  set small.
- `dependencies`, `optionalDependencies` and `peerDependencies` are followed.
  `devDependencies` never are. Neither are peers marked optional in
  `peerDependenciesMeta`, nor anything listed in `bundleDependencies`, since
  those ship inside the tarball.
- `npm:` aliases (`"string-width-cjs": "npm:string-width@^4.2.0"`) resolve to
  the real package.
- A package that ships an `npm-shrinkwrap.json` (some CLIs do, such as older
  `firebase-tools`) is installed by npm with exactly the versions locked in
  that file. minisync reads the shrinkwrap from the tarball and syncs those
  versions instead of resolving the declared ranges.
- Optional dependencies that declare `os`/`cpu` and do not match the configured
  platforms are skipped. For esbuild with `os: ["win32"], cpu: ["x64"]`, only
  `@esbuild/win32-x64` is synced, not the other 23 platform binaries.
- Git, URL and `file:` dependencies cannot come from a registry. They are
  reported as errors.

**NuGet**, following the rules of restore:

- A root with a bare version (`13.0.3`) is exact. A floating version (`13.*`)
  or a range (`[13.0,14.0)`) takes the highest match, and `latest`/`*` takes
  the highest stable version.
- A dependency gets the **lowest** version that satisfies its range, which is
  what restore picks. That version never changes once published, so repeated
  syncs stay small and stable.
- Only the dependency groups that apply to `target_frameworks` are followed,
  using restore's "nearest framework" rule. This matters a lot. With
  `net8.0`, `Serilog.AspNetCore 8.0.3` needs 22 packages. With every group
  (`target_frameworks` left empty) it pulls in 114, mostly legacy
  `System.*`/`runtime.*` packages for .NET Framework and .NET Standard 1.x.

Downloads are verified before they are kept. npm tarballs must match the
registry's integrity hash, and a `.nupkg` must be a readable package with the
expected id and version. A download goes to a `.part` file and is only moved
into place after verification, so an interrupted sync never leaves a broken
file behind.

## The config file

```json
{
  "output": "data",
  "concurrency": 8,
  "delta": true,
  "npm": {
    "registry": "https://registry.npmjs.org/",
    "peer_dependencies": true,
    "optional_dependencies": true,
    "os": ["win32", "linux"],
    "cpu": ["x64"],
    "allow": [],
    "deny": [],
    "packages": {
      "react": "19.0",
      "react-dom": "19.0",
      "typescript": ["5.6.3", "5.7.2"]
    }
  },
  "nuget": {
    "source": "https://api.nuget.org/v3/index.json",
    "target_frameworks": ["net8.0", "net48"],
    "allow": [],
    "deny": [],
    "packages": {
      "Newtonsoft.Json": "13.0.3",
      "Serilog.AspNetCore": "8.0.3"
    }
  }
}
```

| Setting | Default | |
| --- | --- | --- |
| `output` | `data` | Where packages are stored. A relative path is relative to the config file. |
| `concurrency` | `8` | Parallel HTTP requests. |
| `delta` | `false` | Also copy each newly downloaded file into `delta/<module>/<yyyy_MM_dd>/`, the same structure as backpack's delta deposits. Carry only that folder across to move just what is new. |
| `*.packages` | | Package name mapped to a version spec, or to an array of specs to sync several versions. |
| `*.allow` | `[]` | Glob patterns (`*`, `?`, case-insensitive) of the **dependencies** that may be synced. Empty allows everything. Packages listed in `packages` are always allowed. |
| `*.deny` | `[]` | Glob patterns of packages that are never synced or traversed, even if listed in `packages`. |
| `npm.registry` | npmjs.org | Registry to read from, e.g. a Nexus or Artifactory proxy. |
| `npm.peer_dependencies` | `true` | Follow peer dependencies (npm 7+ installs them). |
| `npm.optional_dependencies` | `true` | Follow optional dependencies. |
| `npm.os` / `npm.cpu` | `[]` (all) | Platforms of the offline machines, as Node's `process.platform` / `process.arch` values (`win32`, `linux`, `darwin` / `x64`, `arm64`). |
| `nuget.source` | nuget.org | A NuGet **v3** service index. |
| `nuget.target_frameworks` | `[]` (all) | Frameworks of the offline projects, e.g. `net8.0`, `net48`, `netstandard2.0`. `net8.0-windows` is read as `net8.0-windows7.0`, as the SDK does. Give other platforms with their version (`net8.0-android34.0`). |

Packages blocked by `allow`/`deny` are listed as warnings at the end of the
sync, along with the package that wanted them. If the offline side needs one of
them, the install there will fail, so the list tells you exactly what was held
back. Unknown keys in the file are errors, so a typo such as `target_framework`
cannot be silently ignored.

## Output layout

```
data/
  npm/registry.npmjs.org/react/react-19.0.8.tgz
  npm/registry.npmjs.org/@esbuild/win32-x64/win32-x64-0.24.0.tgz
  nuget/api.nuget.org/v3-flatcontainer/serilog/3.1.1/serilog.3.1.1.nupkg
  delta/npm/2026_10_12/registry.npmjs.org/react/react-19.0.8.tgz       (with "delta": true)
```

This is backpack's `<module>/<host><path>` key scheme on disk, with npm's `/-/`
removed from tarball paths in the same way.

## Using the packages offline

**npm**: publish the tarballs to the offline registry (Verdaccio, Nexus,
Artifactory, ...), e.g. `npm publish <file>.tgz --registry <offline registry>`.
During an install, npm asks for every optional platform package, including the
ones minisync skipped. Those requests 404 and npm carries on without them,
exactly as it does online.

**NuGet**: push the `.nupkg` files to the offline feed with
`dotnet nuget push <file> --source <feed>`, or copy them all into a single
folder and use that folder as a package source. The `{id}/{version}/` tree
itself is **not** a valid local feed, because NuGet expects a `.nuspec` beside
every package in that layout.

## Networking

minisync honours `HTTP_PROXY`, `HTTPS_PROXY` and `NO_PROXY`, and on Windows the
system proxy settings. It authenticates to proxies as the logged-in user. Failed
requests (connection errors, timeouts, `408`/`429`/`5xx`) are retried up to
four times with backoff. Downloads have no total time limit, only a limit on
how long the server may stay silent, so large packages complete over slow
links. npm metadata is fetched in the abbreviated "corgi" format, and NuGet
metadata is read from one small `.nuspec` per version, to keep traffic down.

## Building

Requires the .NET 8 SDK.

```powershell
dotnet test                                       # run the tests
dotnet publish src/MiniSync -c Release -r win-x64 -o publish
```

`publish/minisync.exe` is a single self-contained executable (about 36 MB).
Use `-r linux-x64` for Linux.

The [build workflow](.github/workflows/build.yml) does this for `win-x64` and
`linux-x64` on every push and pull request:

1. It runs the tests on Windows and on Linux.
2. It smoke tests each published executable on its own OS with a small real
   sync.
3. It uploads the executables as the `minisync-win-x64` and `minisync-linux-x64`
   artifacts.

Pushing a version tag, e.g. `git tag v1.0.0 && git push origin v1.0.0`, also
creates a GitHub release with both executables and a `SHA256SUMS` file for
checking them after the transfer. The tag's version is stamped into the
executables. The Linux executable ships as a `.tar.gz` so that it keeps its
executable bit.

## Limitations

- Lockfiles are not read. `npm ci` (or `npm install` with a
  `package-lock.json`) and NuGet's `packages.lock.json` ask for the locked
  versions, which can differ from what minisync resolves. List those exact
  versions in `packages` if a project must install from its lockfile.
- Some restores need packages that no dependency list mentions. These must be
  listed as roots:
  - With a `RuntimeIdentifier`, or for a self-contained publish, the SDK needs
    runtime packs such as `Microsoft.NETCore.App.Runtime.win-x64` and
    `Microsoft.AspNetCore.App.Runtime.win-x64`, plus `Microsoft.NETCore.App.Host.<rid>`.
  - Trimming and AOT need `Microsoft.NET.ILLink.Tasks` and
    `Microsoft.DotNet.ILCompiler`.
  - Old `System.*` 4.x packages also pull in RID-specific packages through
    their `runtime.json`.
- npm install scripts that download files from outside the registry, such as
  prebuilt native binaries from GitHub, cannot work offline. Install with
  `--ignore-scripts`, or provide those files separately.
- Registries that require authentication are not supported. minisync reads
  public registries, or proxies that allow anonymous reads.
- npm's abbreviated metadata does not include `libc`, so on Linux both the glibc
  and musl variants of a platform package are synced.
- npm ranges float. A later sync can select newer patch releases of transitive
  dependencies; the new files are added, and existing ones are kept.
- Peer dependency ranges are resolved like any other dependency. minisync does
  not reproduce npm's peer conflict handling, so a combination that npm rejects
  online is not fixed here.
