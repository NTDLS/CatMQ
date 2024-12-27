# NTDLS.PrudentMessageQueueServer

📦 Be sure to check out the NuGet package: https://www.nuget.org/packages/NTDLS.PrudentMessageQueue

In memory persistent message queue intended for inter-process-communication,
    queuing, load-balancing and buffering over TCP/IP.

## Running the server:

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
See documentation of [NTDLS.PrudentMessageQueue](https://www.nuget.org/packages/NTDLS.PrudentMessageQueue) for client and server interaction examples.


## License
[MIT](https://choosealicense.com/licenses/mit/)
