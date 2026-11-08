using NTDLS.ReliableMessaging;
using System.IO.Compression;

namespace NTDLS.CatMQ.Shared
{
    /// <summary>
    /// Compresses the frames that CatMQ clients and servers exchange. Small frames are sent as they are, because compressing
    /// them costs more time than the bytes it saves, and large ones are compressed with Deflate. Each frame starts with a byte
    /// that says which it is, so the receiving side can read either, whatever its own settings are.
    /// </summary>
    public class CMqCompressionProvider : IRmCompressionProvider
    {
        private const byte Uncompressed = 0;
        private const byte Deflated = 1;

        /// <summary>
        /// How hard to compress. Fastest compresses JSON nearly as well as Optimal for much less time.
        /// </summary>
        public CompressionLevel CompressionLevel { get; }

        /// <summary>
        /// Frames smaller than this many bytes are not compressed. 0 compresses everything.
        /// </summary>
        public int Threshold { get; }

        /// <summary>
        /// Creates a compression provider.
        /// </summary>
        /// <param name="compressionLevel">How hard to compress.</param>
        /// <param name="threshold">Frames smaller than this many bytes are not compressed.</param>
        public CMqCompressionProvider(CompressionLevel compressionLevel = CMqDefaults.DEFAULT_COMPRESSION_LEVEL,
            int threshold = CMqDefaults.DEFAULT_COMPRESSION_THRESHOLD)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(threshold);
            CompressionLevel = compressionLevel;
            Threshold = threshold;
        }

        /// <summary>
        /// Compresses a frame, unless it is too small to be worth it or would not get smaller.
        /// </summary>
        public byte[] Compress(RmContext context, byte[] payload)
        {
            if (payload.Length >= Threshold && CompressionLevel != CompressionLevel.NoCompression)
            {
                using var output = new MemoryStream(payload.Length / 2 + 16);
                output.WriteByte(Deflated);
                using (var deflate = new DeflateStream(output, CompressionLevel, leaveOpen: true))
                {
                    deflate.Write(payload, 0, payload.Length);
                }

                //Data that is already compressed, or random, can come out larger. Sending it as it is costs less.
                if (output.Length < payload.Length + 1)
                {
                    return output.ToArray();
                }
            }

            var frame = new byte[payload.Length + 1];
            frame[0] = Uncompressed;
            Buffer.BlockCopy(payload, 0, frame, 1, payload.Length);
            return frame;
        }

        /// <summary>
        /// Restores a frame that was written by Compress.
        /// </summary>
        public byte[] Decompress(RmContext context, byte[] compressedPayload)
        {
            if (compressedPayload.Length == 0)
            {
                throw new InvalidDataException("The frame is empty, so it has no compression marker.");
            }

            switch (compressedPayload[0])
            {
                case Uncompressed:
                    return compressedPayload.AsSpan(1).ToArray();

                case Deflated:
                    using (var input = new MemoryStream(compressedPayload, 1, compressedPayload.Length - 1))
                    using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream(compressedPayload.Length * 4))
                    {
                        deflate.CopyTo(output);
                        return output.ToArray();
                    }

                default:
                    throw new InvalidDataException($"Unknown compression marker [{compressedPayload[0]}]. The other side may be running a version of CatMQ that frames messages differently.");
            }
        }
    }
}
