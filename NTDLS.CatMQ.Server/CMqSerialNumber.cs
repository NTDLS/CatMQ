namespace NTDLS.CatMQ.Server
{
    internal static class CMqSerialNumber
    {
        public static byte[] ToKey(ulong serialNumber)
        {
            var keyBytes = BitConverter.GetBytes(serialNumber);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(keyBytes); // Convert to big-endian.
            }
            return keyBytes;
        }
    }
}
