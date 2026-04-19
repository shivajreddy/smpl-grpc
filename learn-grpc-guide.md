# Learn gRPC: A Complete Guide for Revit Plugin Architectures

A structured learning path to master gRPC, built around the patterns used in this RevitLiveSync project. Each phase builds on the previous one. By the end, you will understand every gRPC concept used in this codebase and be able to design similar systems from scratch.

---

## Table of Contents

1. [Phase 1: Foundations -- What Problem Does gRPC Solve?](#phase-1-foundations----what-problem-does-grpc-solve)
2. [Phase 2: Protocol Buffers (Protobuf)](#phase-2-protocol-buffers-protobuf)
3. [Phase 3: Your First gRPC Service](#phase-3-your-first-grpc-service)
4. [Phase 4: The Four gRPC Communication Patterns](#phase-4-the-four-grpc-communication-patterns)
5. [Phase 5: gRPC in .NET (C#)](#phase-5-grpc-in-net-c)
6. [Phase 6: Error Handling, Status Codes, and Metadata](#phase-6-error-handling-status-codes-and-metadata)
7. [Phase 7: Server-Streaming Deep Dive](#phase-7-server-streaming-deep-dive)
8. [Phase 8: Hosting gRPC Inside a Host Process (Revit Pattern)](#phase-8-hosting-grpc-inside-a-host-process-revit-pattern)
9. [Phase 9: Thread Marshalling and the Command Queue Pattern](#phase-9-thread-marshalling-and-the-command-queue-pattern)
10. [Phase 10: Project Structure and Shared Contracts](#phase-10-project-structure-and-shared-contracts)
11. [Phase 11: Advanced Topics](#phase-11-advanced-topics)
12. [Phase 12: Exercises -- Build It Yourself](#phase-12-exercises----build-it-yourself)
13. [Appendix: Key Files in RevitLiveSync](#appendix-key-files-in-revitlivesync)
14. [Appendix: Recommended Resources](#appendix-recommended-resources)

---

## Phase 1: Foundations -- What Problem Does gRPC Solve?

### The Problem

You have two separate programs that need to talk to each other. In RevitLiveSync's case:

- **Program A (Plugin):** runs inside the Revit.exe process, has access to the BIM model
- **Program B (Host):** a standalone WPF desktop app that wants to read/write that model

They are separate processes. They cannot share memory or call each other's functions directly. They need a communication protocol.

### Options You Might Know

| Approach | How It Works | Downsides |
|---|---|---|
| REST/HTTP + JSON | Text-based, human-readable | Slow serialization, no streaming, no type safety |
| Named Pipes | OS-level byte streams | No schema, manual serialization, platform-specific |
| Raw TCP Sockets | Lowest level | You build everything yourself |
| COM / .NET Remoting | Legacy interop | Fragile, hard to debug, deprecated |

### What gRPC Offers

gRPC (Google Remote Procedure Call) gives you:

1. **A schema language** (Protocol Buffers) -- you define your messages and services in a `.proto` file, and tooling generates code in your language
2. **Fast binary serialization** -- much smaller and faster than JSON
3. **HTTP/2 transport** -- multiplexed connections, streaming built-in
4. **Four communication patterns** -- unary, server-streaming, client-streaming, bidirectional streaming
5. **Cross-language support** -- the same `.proto` file generates code for C#, Python, Go, Rust, Java, etc.
6. **Type safety** -- generated code gives you compile-time checks

### Why gRPC Fits the Revit Plugin Architecture

- Revit plugins run inside Revit's process. gRPC lets external apps communicate without DLL injection or COM hacks.
- Server-streaming lets Revit **push** model changes in real time (you cannot do this cleanly with REST).
- Protobuf provides a single schema (`live_sync.proto`) that both the Plugin and Host compile against, so they can never get out of sync.
- HTTP/2 on localhost has negligible overhead -- fast enough for real-time sync.

### Key Concepts to Internalize

- **RPC = Remote Procedure Call**: the client calls a method, the server executes it and returns a result. It *looks* like a local function call but crosses a process/network boundary.
- **Stub / Client**: auto-generated code on the client side that handles serialization and network transport.
- **Service Implementation**: your server-side code that actually does the work.
- **Channel**: the client's connection to the server (manages HTTP/2 connection pooling).

---

## Phase 2: Protocol Buffers (Protobuf)

Protobuf is the serialization format and schema language that gRPC uses. You must understand it before touching gRPC.

### The `.proto` File

This is the **single source of truth** for your API. Here is a minimal example:

```protobuf
syntax = "proto3";                    // Always use proto3 (the current version)
option csharp_namespace = "MyApp";    // Controls the generated C# namespace

package myservice;                    // Logical grouping (like a namespace in proto-world)

// Define a service (the API contract)
service Greeter {
  rpc SayHello (HelloRequest) returns (HelloReply);
}

// Define messages (the data structures)
message HelloRequest {
  string name = 1;        // Field number 1 (NOT a default value -- it's a wire identifier)
}

message HelloReply {
  string greeting = 1;
}
```

### Field Numbers Are Critical

```protobuf
message ElementData {
  int64 element_id = 1;     // "= 1" is the field number on the wire
  string unique_id = 2;     // "= 2" is the field number on the wire
  string category = 3;
}
```

- Field numbers are **permanent identifiers** used in the binary encoding.
- Once you assign a number to a field, **never reuse it** even if you delete the field.
- Numbers 1-15 use 1 byte on the wire. Numbers 16-2047 use 2 bytes. Put your most common fields in 1-15.
- You can add new fields freely (old clients ignore unknown fields). This is how protobuf achieves backward compatibility.

### Scalar Types Mapping

| Proto Type | C# Type | Notes |
|---|---|---|
| `double` | `double` | 64-bit float |
| `float` | `float` | 32-bit float |
| `int32` | `int` | Variable-length encoding, inefficient for negatives |
| `int64` | `long` | Used for Revit ElementIds in this project |
| `bool` | `bool` | |
| `string` | `string` | Always UTF-8 |
| `bytes` | `ByteString` | Raw binary data |

### Repeated Fields = Lists

```protobuf
message ModelSnapshot {
  repeated ElementData elements = 3;   // generates: RepeatedField<ElementData> in C#
  repeated LevelInfo levels = 4;
}
```

`repeated` means "zero or more." In C#, this becomes a `RepeatedField<T>` (similar to `List<T>`, but you call `.Add()` or `.AddRange()` -- you cannot reassign it).

### Enums

```protobuf
enum ChangeType {
  ADDED = 0;      // The first enum value MUST be 0 (it's the default)
  MODIFIED = 1;
  DELETED = 2;
}
```

- The zero value is the default. Name it something sensible (or use `UNKNOWN = 0` if no natural default exists).
- Enums map to C# enums directly.

### Nested Messages

Messages can reference other messages:

```protobuf
message ElementChange {
  int64 element_id = 1;
  ChangeType change_type = 2;
  ElementData current_state = 3;  // another message type, nullable (null if deleted)
}
```

### Default Values

Proto3 has implicit defaults (you cannot tell if a field was explicitly set to the default value or simply omitted):

| Type | Default |
|---|---|
| `int32/int64` | `0` |
| `double/float` | `0.0` |
| `bool` | `false` |
| `string` | `""` |
| `message` | `null` |
| `enum` | first value (number 0) |

### Exercise

Look at `proto/live_sync.proto` in this project. Identify:
- How many services are defined? (1: `RevitSync`)
- How many RPC methods? (4)
- Which method uses streaming? (`SubscribeToChanges` -- the `stream` keyword on the return type)
- What is the relationship between `ModelDelta` and `ElementChange`? (A delta contains a list of changes)

---

## Phase 3: Your First gRPC Service

### Step-by-Step: Build a Minimal gRPC Service in .NET

#### 1. Create the Solution

```bash
dotnet new sln -n GrpcLearning
dotnet new classlib -n GrpcLearning.Shared -f net8.0
dotnet new web -n GrpcLearning.Server -f net8.0
dotnet new console -n GrpcLearning.Client -f net8.0
dotnet sln add GrpcLearning.Shared GrpcLearning.Server GrpcLearning.Client
```

#### 2. Add NuGet Packages

```bash
# Shared (proto codegen)
cd GrpcLearning.Shared
dotnet add package Google.Protobuf
dotnet add package Grpc.Tools
dotnet add package Grpc.Net.Client

# Server
cd ../GrpcLearning.Server
dotnet add package Grpc.AspNetCore
dotnet add reference ../GrpcLearning.Shared/GrpcLearning.Shared.csproj

# Client
cd ../GrpcLearning.Client
dotnet add package Grpc.Net.Client
dotnet add reference ../GrpcLearning.Shared/GrpcLearning.Shared.csproj
```

#### 3. Write the Proto File

Create `proto/greeter.proto` at the solution root:

```protobuf
syntax = "proto3";
option csharp_namespace = "GrpcLearning.Shared";

service Greeter {
  rpc SayHello (HelloRequest) returns (HelloReply);
}

message HelloRequest {
  string name = 1;
}

message HelloReply {
  string message = 1;
}
```

#### 4. Add Proto to Shared `.csproj`

```xml
<ItemGroup>
  <Protobuf Include="..\..\proto\greeter.proto" GrpcServices="Both" Link="Protos\greeter.proto" />
</ItemGroup>
```

This tells `Grpc.Tools` to generate both client and server stubs. Building the project produces:
- `Greeter.GreeterBase` -- abstract base class you override on the server
- `Greeter.GreeterClient` -- typed client class you use on the client

#### 5. Implement the Server

```csharp
// GrpcLearning.Server/Services/GreeterService.cs
using Grpc.Core;
using GrpcLearning.Shared;

public class GreeterService : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HelloReply
        {
            Message = $"Hello, {request.Name}!"
        });
    }
}
```

Register it in `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();

var app = builder.Build();
app.MapGrpcService<GreeterService>();
app.Run();
```

#### 6. Implement the Client

```csharp
// GrpcLearning.Client/Program.cs
using Grpc.Net.Client;
using GrpcLearning.Shared;

var channel = GrpcChannel.ForAddress("http://localhost:5000");
var client = new Greeter.GreeterClient(channel);

var reply = await client.SayHelloAsync(new HelloRequest { Name = "World" });
Console.WriteLine(reply.Message);  // "Hello, World!"
```

### What Just Happened

1. You wrote a `.proto` file (the contract).
2. `Grpc.Tools` generated C# classes at build time.
3. You subclassed `GreeterBase` to implement the server logic.
4. You used `GreeterClient` on the client side -- it serializes your request to protobuf, sends it over HTTP/2, deserializes the response.
5. The generated code handled all serialization, framing, and transport.

---

## Phase 4: The Four gRPC Communication Patterns

gRPC supports four patterns. Understanding when to use each is essential.

### Pattern 1: Unary RPC

```protobuf
rpc GetFullModel (ModelRequest) returns (ModelSnapshot);
```

**Request-response.** Client sends one message, server returns one message. This is like a normal function call. Used in RevitLiveSync for `GetFullModel`, `GetElements`, `ModifyElements`.

**When to use:** Simple queries, CRUD operations, one-shot commands.

### Pattern 2: Server-Streaming RPC

```protobuf
rpc SubscribeToChanges (SubscriptionRequest) returns (stream ModelDelta);
```

The `stream` keyword on the return type. Client sends one request, server sends **many** responses over time. The connection stays open. Used in RevitLiveSync for real-time change notifications.

**Server side (from `RevitSyncServiceImpl.cs`):**
```csharp
public override async Task SubscribeToChanges(
    SubscriptionRequest request,
    IServerStreamWriter<ModelDelta> responseStream,  // <-- write multiple messages here
    ServerCallContext context)
{
    // Keep writing until the client disconnects
    await foreach (var delta in channel.Reader.ReadAllAsync(context.CancellationToken))
    {
        await responseStream.WriteAsync(delta);  // Push a message to the client
    }
}
```

**Client side (from `RevitSyncClient.cs`):**
```csharp
var stream = _client.SubscribeToChanges(new SubscriptionRequest { ... });

await foreach (var delta in stream.ResponseStream.ReadAllAsync(cancellationToken))
{
    OnDeltaReceived?.Invoke(delta);  // Process each message as it arrives
}
```

**When to use:** Real-time feeds, event subscriptions, progress updates, log streaming.

### Pattern 3: Client-Streaming RPC

```protobuf
rpc UploadElements (stream ElementData) returns (UploadResult);
```

Client sends **many** messages, server returns **one** response after receiving all of them. Not used in RevitLiveSync, but useful for bulk uploads.

**When to use:** File uploads, batch data ingestion, collecting metrics.

### Pattern 4: Bidirectional Streaming RPC

```protobuf
rpc Chat (stream ChatMessage) returns (stream ChatMessage);
```

Both sides send multiple messages independently. The two streams operate independently -- the server does not have to wait for the client to finish before responding.

**When to use:** Chat applications, collaborative editing, interactive protocols.

### Summary Table

| Pattern | Request | Response | RevitLiveSync Usage |
|---|---|---|---|
| Unary | 1 message | 1 message | GetFullModel, GetElements, ModifyElements |
| Server-streaming | 1 message | N messages | SubscribeToChanges |
| Client-streaming | N messages | 1 message | (not used) |
| Bidirectional | N messages | N messages | (not used) |

---

## Phase 5: gRPC in .NET (C#)

### NuGet Packages Cheat Sheet

| Package | Purpose | Used By |
|---|---|---|
| `Google.Protobuf` | Protobuf runtime (serialization/deserialization) | Shared |
| `Grpc.Tools` | Build-time `.proto` -> C# code generator | Shared |
| `Grpc.Net.Client` | gRPC client library (makes calls) | Client / Shared |
| `Grpc.AspNetCore` | gRPC server hosting on ASP.NET Core/Kestrel | Server (Plugin) |

### Code Generation

When you build a project with a `<Protobuf>` item in the `.csproj`, `Grpc.Tools` generates two files in `obj/`:

1. **`{ProtoName}.cs`** -- Message classes (e.g., `ModelRequest`, `ModelSnapshot`, `ElementData`)
2. **`{ProtoName}Grpc.cs`** -- Service base class (`RevitSync.RevitSyncBase`) and client class (`RevitSync.RevitSyncClient`)

The `GrpcServices` attribute controls what gets generated:

| Value | Generates |
|---|---|
| `"Both"` | Client stubs + server base class |
| `"Client"` | Client stubs only |
| `"Server"` | Server base class only |
| `"None"` | Messages only, no service code |

RevitLiveSync uses `"Both"` in Shared so both Plugin and Host can reference the same assembly.

### The Server Side

```csharp
// 1. Subclass the generated base class
public class RevitSyncServiceImpl : RevitSync.RevitSyncBase
{
    // 2. Override the method (generated as virtual)
    public override async Task<ModelSnapshot> GetFullModel(
        ModelRequest request,       // Deserialized from protobuf automatically
        ServerCallContext context)   // Metadata, deadlines, cancellation
    {
        // 3. Do your work, return the response message
        return new ModelSnapshot { DocumentTitle = "My Model" };
    }
}
```

`ServerCallContext` gives you access to:
- `context.CancellationToken` -- fires when the client disconnects or a deadline expires
- `context.RequestHeaders` -- gRPC metadata (like HTTP headers)
- `context.Peer` -- client address info
- `context.Deadline` -- when the call times out

### The Client Side

```csharp
// 1. Create a channel (manages the HTTP/2 connection)
var channel = GrpcChannel.ForAddress("http://localhost:50051");

// 2. Create a typed client (generated from proto)
var client = new RevitSync.RevitSyncClient(channel);

// 3. Call methods (async by default)
var snapshot = await client.GetFullModelAsync(new ModelRequest
{
    IncludeParameters = true
});

// 4. Clean up
await channel.ShutdownAsync();
channel.Dispose();
```

### Channel Best Practices

- **Channels are expensive to create** -- they establish HTTP/2 connections. Create one and reuse it.
- **Channels are thread-safe** -- multiple concurrent calls can share the same channel.
- Channels handle reconnection automatically.
- In RevitLiveSync, `RevitSyncClient` creates one channel in `ConnectAsync()` and reuses it for all calls.

---

## Phase 6: Error Handling, Status Codes, and Metadata

### gRPC Status Codes

gRPC has its own set of status codes (analogous to HTTP status codes but more specific):

| Code | Name | When to Use |
|---|---|---|
| 0 | `OK` | Success (implicit, you never set this manually) |
| 1 | `Cancelled` | Client cancelled the call |
| 2 | `Unknown` | Unknown error |
| 3 | `InvalidArgument` | Client sent bad data |
| 4 | `DeadlineExceeded` | Timeout |
| 5 | `NotFound` | Resource does not exist |
| 7 | `PermissionDenied` | Auth failure |
| 12 | `Unimplemented` | Method not implemented |
| 13 | `Internal` | Server-side bug |
| 14 | `Unavailable` | Server not ready (transient, retry is appropriate) |

### Throwing Errors on the Server

From `RevitSyncServiceImpl.cs`:

```csharp
if (handler == null)
    throw new RpcException(new Status(StatusCode.Unavailable, "LiveSync handler not initialized"));

if (response.Type == SyncResponseType.Error)
    throw new RpcException(new Status(StatusCode.Internal, response.ErrorMessage ?? "Unknown error"));
```

Throw `RpcException` with a `Status` to communicate structured errors to the client.

### Catching Errors on the Client

From `RevitSyncClient.cs`:

```csharp
try
{
    return await _client.GetFullModelAsync(new ModelRequest { ... });
}
catch (RpcException ex)
{
    // ex.StatusCode -- the gRPC status code (enum)
    // ex.Status.Detail -- the human-readable error message
    OnError?.Invoke($"GetFullModel failed: {ex.Status.Detail}");
    return null;
}
```

### Common Error Handling Patterns

```csharp
catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
{
    // Server is down -- retry with backoff
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
{
    // Timeout -- maybe increase deadline or retry
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
{
    // Client or server cancelled -- usually intentional
}
```

---

## Phase 7: Server-Streaming Deep Dive

This is the most important pattern for Revit plugin architectures. It is how the plugin pushes real-time model changes to external apps.

### How It Works Internally

1. Client opens an HTTP/2 stream with the initial request.
2. Server holds the stream open and writes messages whenever it has data.
3. Each message is length-prefixed on the wire (gRPC framing: 1 byte compression flag + 4 bytes message length + protobuf payload).
4. Client reads messages as they arrive using `ReadAllAsync()`.
5. Either side can end the stream: server completes the method, or client cancels via `CancellationToken`.

### Server-Side Implementation Pattern

RevitLiveSync uses `System.Threading.Channels` to bridge between the change-producing thread and the gRPC streaming method:

```csharp
public override async Task SubscribeToChanges(
    SubscriptionRequest request,
    IServerStreamWriter<ModelDelta> responseStream,
    ServerCallContext context)
{
    // Create an unbounded channel (producer-consumer queue)
    var channel = Channel.CreateUnbounded<ModelDelta>();

    // Wire up the producer: when changes happen, write to the channel
    accumulator.OnDeltaReady = delta =>
    {
        channel.Writer.TryWrite(delta);
    };

    try
    {
        // Consumer loop: read from channel, write to gRPC stream
        await foreach (var delta in channel.Reader.ReadAllAsync(context.CancellationToken))
        {
            await responseStream.WriteAsync(delta);
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected -- normal
    }
    finally
    {
        // Clean up
        accumulator.IsActive = false;
        accumulator.OnDeltaReady = null;
    }
}
```

Key observations:
- The method **does not return** until the client disconnects or cancels. The gRPC stream stays open.
- `System.Threading.Channels.Channel` is a thread-safe async producer-consumer queue. The change accumulator writes to it from one thread, the gRPC method reads from it on another.
- `context.CancellationToken` fires when the client disconnects. This is how the `await foreach` loop terminates.

### Client-Side Consumption Pattern

```csharp
var stream = _client.SubscribeToChanges(
    new SubscriptionRequest { GeometryChanges = true, ParameterChanges = true },
    cancellationToken: _streamCts.Token
);

await foreach (var delta in stream.ResponseStream.ReadAllAsync(_streamCts.Token))
{
    OnDeltaReceived?.Invoke(delta);
}
```

To stop: cancel the `CancellationTokenSource`:
```csharp
_streamCts.Cancel();  // This ends the await foreach loop
```

### Backpressure

If the client reads slowly, the server's `WriteAsync` will eventually block (HTTP/2 flow control). This is automatic -- you do not need to implement backpressure yourself.

---

## Phase 8: Hosting gRPC Inside a Host Process (Revit Pattern)

This is the unusual and powerful pattern used in RevitLiveSync: **embedding a full ASP.NET Core Kestrel web server inside another application's process** (Revit).

### Why Embed the Server?

Revit plugins are DLLs loaded into Revit.exe. You cannot run a separate server process because:
- The Revit API is only accessible from within the Revit process.
- A separate process would need COM or some other IPC to call the Revit API -- more complexity.
- Embedding the server means gRPC method implementations can directly access Revit objects (with thread marshalling).

### How It Works

From `GrpcHostService.cs`:

```csharp
public class GrpcHostService
{
    private WebApplication? _app;
    private Thread? _serverThread;

    public void Start()
    {
        // Run on a background thread so we don't block Revit's UI thread
        _serverThread = new Thread(RunServer)
        {
            Name = "LiveSync.gRPC",
            IsBackground = true   // Dies when Revit exits
        };
        _serverThread.Start();
    }

    private void RunServer()
    {
        var builder = WebApplication.CreateBuilder();

        // Configure Kestrel to listen on localhost:50051 with HTTP/2 only
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenLocalhost(50051, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        // Register gRPC services
        builder.Services.AddGrpc();
        _app = builder.Build();
        _app.MapGrpcService<RevitSyncServiceImpl>();

        // Start and block
        _app.StartAsync(_cts.Token).GetAwaiter().GetResult();
        _cts.Token.WaitHandle.WaitOne();
    }
}
```

### Key Design Decisions

1. **Background thread**: The Kestrel server runs on its own thread so it does not block Revit's UI.
2. **`IsBackground = true`**: Ensures the thread does not prevent Revit from exiting.
3. **`HttpProtocols.Http2`**: gRPC requires HTTP/2. No need for HTTP/1.1 fallback since both sides are controlled by you.
4. **`localhost` only**: Security -- only local apps can connect. No network exposure.
5. **No TLS**: Acceptable for localhost communication. In production/networked scenarios, you would add TLS.

### Lifecycle

| Event | Action |
|---|---|
| Revit starts, loads plugin (`OnStartup`) | `GrpcHostService.Start()` -- spawns the server thread |
| External app connects | Kestrel handles the HTTP/2 connection |
| Revit closes (`OnShutdown`) | `GrpcHostService.Stop()` -- cancels token, stops Kestrel |

---

## Phase 9: Thread Marshalling and the Command Queue Pattern

This is the hardest and most important pattern for Revit plugin architectures.

### The Problem

- gRPC methods execute on **background threads** (ASP.NET Core thread pool).
- The Revit API is **single-threaded** -- it can only be called from Revit's main UI thread.
- If you call `doc.GetElement()` from a gRPC thread, Revit will crash or throw an exception.

### The Solution: Command Queue + ExternalEvent

```
gRPC thread                              Revit main thread
-----------                              -----------------
1. Create SyncCommand                    
2. Create TaskCompletionSource<Result>
3. Enqueue (command, tcs)  ──────────>  
4. Raise ExternalEvent     ──────────>   5. Revit calls handler.Execute()
6. await tcs.Task (suspended)            7. Dequeue command
                                         8. Call Revit API (safe!)
                                         9. Build response
                                         10. tcs.SetResult(response)  ──>  11. gRPC method resumes
                                                                           12. Return response to client
```

### In Code

**Enqueue side (gRPC thread):**
```csharp
public async Task<SyncResponse> EnqueueAsync(SyncCommand command)
{
    command.Completion = new TaskCompletionSource<SyncResponse>();
    _commandQueue.Enqueue(command);   // Thread-safe ConcurrentQueue
    _externalEvent.Raise();           // Tell Revit to call our handler
    return await command.Completion.Task;  // Suspend until Revit thread completes
}
```

**Execute side (Revit main thread):**
```csharp
public void Execute(UIApplication app)  // Called by Revit on the main thread
{
    while (_commandQueue.TryDequeue(out var command))
    {
        var response = ProcessCommand(command, app);  // Safe to call Revit API here
        command.Completion.SetResult(response);        // Resume the gRPC thread
    }
}
```

### Why TaskCompletionSource?

`TaskCompletionSource<T>` is a bridge between callback-based code and async/await. It lets you:
1. Create a `Task<T>` that is not yet complete.
2. `await` it on the gRPC thread (suspending without blocking).
3. Complete it from the Revit thread with `SetResult()`.

This is the standard .NET pattern for adapting event-driven or callback-based APIs to async/await.

### The ExternalEvent Mechanism

`ExternalEvent` is a Revit API class. When you call `.Raise()`, Revit schedules your `IExternalEventHandler.Execute()` to run on the main thread during the next idle cycle. This is the **only safe way** to get onto Revit's main thread from a background thread.

---

## Phase 10: Project Structure and Shared Contracts

### The Three-Project Pattern

```
Solution
├── Shared   (class library, proto codegen)
├── Server   (references Shared)
└── Client   (references Shared)
```

This is the recommended structure for any gRPC project in .NET.

### Why a Shared Project?

Without it, you would need to either:
- **Duplicate the `.proto` in both projects** -- they generate separate types that are not assignment-compatible even though they are structurally identical.
- **Copy generated code manually** -- error-prone and tedious.

With the Shared project:
- The `.proto` is compiled once.
- Both Plugin and Host reference the same assembly.
- Types are identical -- a `ModelSnapshot` created on the server is the exact same C# type the client deserializes into.

### Shared `.csproj` Configuration

```xml
<Protobuf Include="..\..\proto\live_sync.proto"
          GrpcServices="Both"
          Link="Protos\live_sync.proto" />
```

- `Include` -- path to the proto file (can be outside the project directory).
- `GrpcServices="Both"` -- generate client and server stubs.
- `Link` -- makes the file appear in the Solution Explorer under `Protos/` without copying it.

### What Gets Generated

After building `RevitLiveSync.Shared`, the `obj/` directory contains:

| File | Contains |
|---|---|
| `LiveSync.cs` | All message classes: `ModelRequest`, `ModelSnapshot`, `ElementData`, `ModelDelta`, etc. |
| `LiveSyncGrpc.cs` | `RevitSync.RevitSyncBase` (server) and `RevitSync.RevitSyncClient` (client) |

---

## Phase 11: Advanced Topics

### Deadlines and Timeouts

Always set deadlines on client calls in production:

```csharp
var reply = await client.GetFullModelAsync(
    new ModelRequest { IncludeParameters = true },
    deadline: DateTime.UtcNow.AddSeconds(30)
);
```

If the server does not respond in time, the client gets `StatusCode.DeadlineExceeded`.

### Metadata (Headers)

gRPC metadata is like HTTP headers -- key-value pairs sent with requests/responses:

```csharp
// Client: send metadata
var headers = new Metadata { { "x-revit-version", "2026" } };
var reply = await client.GetFullModelAsync(request, headers);

// Server: read metadata
var version = context.RequestHeaders.GetValue("x-revit-version");
```

### Interceptors (Middleware)

Interceptors let you add cross-cutting concerns (logging, metrics, auth) without modifying service code:

```csharp
public class LoggingInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        Console.WriteLine($"Call: {context.Method}");
        var response = await continuation(request, context);
        Console.WriteLine($"Done: {context.Method}");
        return response;
    }
}

// Register:
builder.Services.AddGrpc(options => options.Interceptors.Add<LoggingInterceptor>());
```

### Health Checks

gRPC has a standard health checking protocol (`grpc.health.v1.Health`):

```csharp
builder.Services.AddGrpcHealthChecks();  // Server
// Client can call the standard health endpoint to check if server is alive
```

### Reflection

gRPC server reflection lets tools like `grpcurl` discover your services without the `.proto` file:

```csharp
builder.Services.AddGrpcReflection();
app.MapGrpcReflectionService();
```

Then test from the command line:
```bash
grpcurl -plaintext localhost:50051 list
grpcurl -plaintext localhost:50051 livesync.RevitSync/GetFullModel
```

### Channel Options

```csharp
var channel = GrpcChannel.ForAddress("http://localhost:50051", new GrpcChannelOptions
{
    HttpHandler = new SocketsHttpHandler
    {
        EnableMultipleHttp2Connections = true,  // Allow multiple HTTP/2 connections
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
    },
    MaxReceiveMessageSize = 16 * 1024 * 1024,  // 16 MB (default is 4 MB)
});
```

### Large Messages

Default max message size is 4 MB. For large Revit models, increase it:

```csharp
// Server
builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = 16 * 1024 * 1024;
    options.MaxSendMessageSize = 16 * 1024 * 1024;
});

// Client
var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
{
    MaxReceiveMessageSize = 16 * 1024 * 1024
});
```

---

## Phase 12: Exercises -- Build It Yourself

### Exercise 1: Hello gRPC (Beginner)

Build the minimal greeter service from Phase 3. Verify it works by running the server and client.

### Exercise 2: Add a Second Method (Beginner)

Add a `GetServerTime` unary RPC that returns the current server timestamp. Practice the full cycle: edit `.proto`, rebuild, implement server method, call from client.

### Exercise 3: Server-Streaming Clock (Intermediate)

Add a `SubscribeToClock` server-streaming RPC that sends the current time every second. Practice:
- Writing the `stream` return type in proto
- Using `IServerStreamWriter<T>` on the server
- Using `ReadAllAsync()` on the client
- Cancelling with `CancellationToken`

### Exercise 4: Embedded Server (Intermediate)

Instead of using `WebApplication.Run()`, host the gRPC server on a background thread (like `GrpcHostService.cs`). Practice:
- Creating a `WebApplication` manually
- Configuring Kestrel for HTTP/2
- Running on a background thread
- Graceful shutdown with `CancellationTokenSource`

### Exercise 5: Thread Marshalling Simulation (Advanced)

Simulate the Revit pattern without Revit:
1. Create a "main thread" that processes commands from a `ConcurrentQueue`.
2. Create a gRPC service that enqueues commands with `TaskCompletionSource`.
3. The main thread dequeues and completes them.
4. Verify that gRPC calls wait for the main thread to process them.

This teaches the exact pattern used in `LiveSyncEventHandler.cs`.

### Exercise 6: Change Streaming (Advanced)

Build a mini version of RevitLiveSync:
1. A server that maintains a list of items.
2. A unary `GetAllItems` method.
3. A unary `AddItem` method.
4. A server-streaming `SubscribeToChanges` method that pushes change notifications.
5. Use `System.Threading.Channels.Channel` to bridge between the mutation methods and the stream.

### Exercise 7: Full Revit-Style Architecture (Expert)

Combine everything into a complete system:
1. Shared proto project with messages and service definition.
2. Server embedded on a background thread.
3. Command queue with `TaskCompletionSource` for thread marshalling.
4. Change accumulation with debouncing.
5. Server-streaming for live updates.
6. A WPF or console client that subscribes and displays changes.

---

## Appendix: Key Files in RevitLiveSync

Reference these files as you learn. Each one demonstrates specific gRPC concepts:

| File | Demonstrates |
|---|---|
| `proto/live_sync.proto` | Proto schema design: services, messages, enums, repeated fields |
| `src/RevitLiveSync.Shared/RevitLiveSync.Shared.csproj` | Shared proto codegen configuration |
| `src/RevitLiveSync.Plugin/Services/GrpcHostService.cs` | Embedding Kestrel/gRPC server in a host process |
| `src/RevitLiveSync.Plugin/Services/RevitSyncServiceImpl.cs` | Server-side service implementation (unary + streaming) |
| `src/RevitLiveSync.Plugin/Handlers/LiveSyncEventHandler.cs` | Command queue + TaskCompletionSource thread marshalling |
| `src/RevitLiveSync.Plugin/Updaters/ChangeAccumulator.cs` | Debounced change coalescing with Channel-based streaming |
| `src/RevitLiveSync.Host/Services/RevitSyncClient.cs` | Client-side channel management, call patterns, stream consumption |
| `src/RevitLiveSync.Host/Services/ModelCache.cs` | Applying streamed deltas to local state |

---

## Appendix: Recommended Resources

### Official Documentation
- **gRPC official site**: https://grpc.io/docs/
- **Protocol Buffers language guide**: https://protobuf.dev/programming-guides/proto3/
- **gRPC for .NET**: https://learn.microsoft.com/en-us/aspnet/core/grpc/

### Tools
- **grpcurl**: command-line gRPC client (like curl for gRPC) -- https://github.com/fullstorydev/grpcurl
- **Postman**: has gRPC support for testing services
- **BloomRPC** (or **Evans**): GUI clients for testing gRPC services

### Books and Courses
- *gRPC: Up and Running* by Kasun Indrasiri and Danesh Kuruppu (O'Reilly)
- Microsoft Learn modules on gRPC with .NET

### Suggested Learning Order

1. Read Phases 1-3 and build the greeter service (Day 1)
2. Read Phases 4-5 and do Exercises 1-2 (Day 2)
3. Read Phase 6-7 and do Exercise 3 (Day 3)
4. Read Phase 8 and do Exercise 4 (Day 4)
5. Read Phase 9 and do Exercise 5 (Day 5)
6. Read Phase 10-11, do Exercise 6 (Day 6-7)
7. Do Exercise 7 and then read through all the RevitLiveSync source files (Day 8-10)
