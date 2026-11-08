using CatMQ.Tests.Messages;
using NTDLS.CatMQ.Client;
using NTDLS.CatMQ.Server;
using NTDLS.CatMQ.Shared;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CatMQ.Tests.Unit
{
    /// <summary>
    /// The frame compression that CatMQ clients and servers use.
    /// </summary>
    public class Compression
    {
        private static byte[] Json(int bytes)
        {
            var builder = new StringBuilder("[");
            for (int i = 0; builder.Length < bytes; i++)
            {
                builder.Append($"{{\"orderId\":{i},\"customer\":\"customer {i % 50}\",\"status\":\"pending\",\"total\":{i * 3.5}}},");
            }
            builder[^1] = ']';
            return Encoding.UTF8.GetBytes(builder.ToString());
        }

        [Theory(DisplayName = "Compression: frames of every size come back unchanged.")]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(1023)]
        [InlineData(1024)]
        [InlineData(50_000)]
        public void TestRoundTrip(int size)
        {
            var provider = new CMqCompressionProvider();
            var payload = Json(size).Take(size).ToArray();

            Assert.Equal(payload, provider.Decompress(null!, provider.Compress(null!, payload)));
        }

        [Fact(DisplayName = "Compression: small frames are sent as they are, and large ones are compressed.")]
        public void TestThreshold()
        {
            var provider = new CMqCompressionProvider(CompressionLevel.Fastest, 1024);

            var small = Json(1023).Take(1023).ToArray();
            var compressedSmall = provider.Compress(null!, small);
            Assert.Equal(0, compressedSmall[0]);
            Assert.Equal(small.Length + 1, compressedSmall.Length);

            var large = Json(20_000);
            var compressedLarge = provider.Compress(null!, large);
            Assert.Equal(1, compressedLarge[0]);
            Assert.True(compressedLarge.Length < large.Length / 3, $"{large.Length} bytes compressed to {compressedLarge.Length}.");
        }

        [Fact(DisplayName = "Compression: data that would get larger is sent as it is.")]
        public void TestIncompressibleData()
        {
            var provider = new CMqCompressionProvider();
            var random = new byte[10_000];
            new Random(1).NextBytes(random);

            var compressed = provider.Compress(null!, random);
            Assert.Equal(0, compressed[0]);
            Assert.Equal(random, provider.Decompress(null!, compressed));
        }

        [Fact(DisplayName = "Compression: either side reads frames whatever its own settings are.")]
        public void TestSettingsCanDiffer()
        {
            var compressing = new CMqCompressionProvider(CompressionLevel.SmallestSize, 0);
            var notCompressing = new CMqCompressionProvider(CompressionLevel.NoCompression);
            var payload = Json(5000);

            Assert.Equal(payload, notCompressing.Decompress(null!, compressing.Compress(null!, payload)));
            Assert.Equal(payload, compressing.Decompress(null!, notCompressing.Compress(null!, payload)));
        }

        [Fact(DisplayName = "Compression: a frame from an incompatible version is refused clearly.")]
        public void TestUnknownMarker()
        {
            var provider = new CMqCompressionProvider();
            Assert.Throws<InvalidDataException>(() => provider.Decompress(null!, [7, 1, 2, 3]));
            Assert.Throws<InvalidDataException>(() => provider.Decompress(null!, []));
        }

        [Fact(DisplayName = "Compression: a client and server with different settings exchange large and small messages.")]
        public void TestEndToEnd()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            var server = new CMqServer(new CMqServerConfiguration { CompressionLevel = CompressionLevel.SmallestSize, CompressionThreshold = 0 });
            server.Start(port);
            try
            {
                using var client = new CMqClient(new CMqClientConfiguration { CompressionLevel = CompressionLevel.NoCompression });
                client.Connect(Constants.HOST_NAME, port);
                client.CreateQueue("compression");

                var large = new string('x', 200_000);
                var received = new ConcurrentBag<string>();
                client.Subscribe("compression", (_, m) => { received.Add(m.Deserialize<KeyValueMessage>()!.Value); return CMqConsumeResult.Consumed(); });

                client.Enqueue("compression", new KeyValueMessage(1, "small"));
                client.Enqueue("compression", new KeyValueMessage(2, large));

                Assert.True(Wait.Until(() => received.Count == 2));
                Assert.Contains("small", received);
                Assert.Contains(large, received);
            }
            finally
            {
                server.Stop();
            }
        }
    }
}
