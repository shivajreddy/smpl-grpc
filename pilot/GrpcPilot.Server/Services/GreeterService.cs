using Grpc.Core;
using GrpcPilot.Shared;

namespace GrpcPilot.Server.Services;

public class GreeterService : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Received request: Name = {request.Name}");

            return Task.FromResult(new HelloReply
            {
                Message = $"Hello, {request.Name}!"
            });
        }
}
