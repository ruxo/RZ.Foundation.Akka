# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build
dotnet build src/Akka/RZ.Foundation.Akka.csproj

# Build release + pack NuGet
./build.ps1 <destination-folder>
```

There are no test projects in this repository.

## Architecture

This is a single-project .NET 10 library (`src/Akka/`) that integrates Akka.NET actor systems into ASP.NET Core via dependency injection. It builds on `RZ.Foundation` for functional error handling (`Outcome<T>`, `ErrorInfo`) and `LanguageExt` for FP primitives (`Option<T>`, `Unit`).

### Key layers

- **DI integration** (`AkkaInstaller.cs`) — `AddAkkaSystem` extension methods on `IServiceCollection` that wire up `ActorSystem`, register actors via `ActorRegistration`, and expose them through `IAKkaServices`. Note the interface is spelled `IAKkaServices` (capital K).
- **Configuration** (`AkkaConfig.cs`) — Builds HOCON config from typed properties. Supports simple (ask-timeout only) and cluster (seed nodes, remote transport) modes.
- **Base actor** (`RzUntypedActor.cs`) — Generic base class `RzUntypedActor<T>` providing DI-resolved `Services`, a logger, async helpers (`Run`), and graceful shutdown (`FloodAndStop`).
- **Message patterns** (`Specialized.cs`) — Strongly-typed request-response via `CanResponse<T>`, error signaling via `ICanRaiseError`, and distributed tracing via `TraceableCommand<T>` / `TraceableResponder<TSelf, TRes>` with `ActivityId`.
- **Actor utilities** (`ActorExtension.cs`) — Extension methods on `ActorSystem`, `IUntypedActorContext`, and `ICanTell` for DI-based actor creation, safe ask patterns (`TryAsk`), and response helpers.

### Global usings (`CommonUsings.cs`)

```csharp
global using static RZ.Foundation.AOT.Prelude;  // Provides Fail(), SuccessOutcome(), FailedOutcome(), Optional(), etc.
global using Unit = LanguageExt.Unit;
```

## Code Conventions

- **C# preview features** — Uses `extension(Type name) { }` block syntax for extension methods (C# 14 preview).
- **Functional error handling** — `Outcome<T>` instead of exceptions; Go-style `if (Fail(..., out var error, out var value))` pattern from `RZ.Foundation.AOT.Prelude`.
- **Records for messages** — Sealed records for immutable message types; abstract records for base message patterns.
- **`[PublicAPI]`** — JetBrains annotation marks public API surface.
- **`ConfigureAwait(false)`** — Used on all awaits inside actor code.
- **File-scoped namespaces** and **primary constructors** throughout.
