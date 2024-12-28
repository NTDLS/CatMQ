# CatMQ

![64](https://github.com/user-attachments/assets/b857b3c0-a3f0-4d49-94c6-cb884ad0400b)

📦 Be sure to check out the NuGet package: https://www.nuget.org/packages/NTDLS.CatMQ

CatMQ is a high-performance and reliable persistent message queue designed for efficient inter-process communication, task queuing, load balancing, and data buffering over TCP/IP.

## Running the server
The server can either be run in-process using the nuget package NTDLS.CatMQServer or by downloading
and installing the dedicated CatMQService, which is a platform independent service that includes a
web management interface.

## Running in-process server
Running the server can literally be done with two lines of code and can be run in the same process as the client.
The server does not have to be dedicated either, it can be one of the process that is involved in inner-process-communication.

```csharp
internal class Program
{
    static void Main()
    {
        var server = new MqQueuingService();

        //Listen for queue clients on port 45784, listen for web UI requests on port 8080.
        server.StartAsync(45784, 8080);

        Console.WriteLine("Press [enter] to shutdown.");
        Console.ReadLine();

        server.StopAsync();
    }
}
```
See documentation of [NTDLS.CatMQ](https://www.nuget.org/packages/NTDLS.CatMQ) for client and server interaction examples.

## License
[MIT](https://choosealicense.com/licenses/mit/)
