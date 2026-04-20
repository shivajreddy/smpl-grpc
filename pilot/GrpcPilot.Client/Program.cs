using Grpc.Net.Client;
using GrpcPilot.Shared;

var channel = GrpcChannel.ForAddress("http://localhost:5050");
var client = new Greeter.GreeterClient(channel);
var reply = await client.SayHelloAsync(new HelloRequest { Name = "smpl" });

Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {reply.Message}");
