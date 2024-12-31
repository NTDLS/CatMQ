using System.Security.Cryptography;

namespace CatMQ.Service
{
    public class KeyGen
    {
        public static string CreateApiKey()
        {
            int length = 30;
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var result = new char[length];

            using (var rng = RandomNumberGenerator.Create())
            {
                var buffer = new byte[length];
                rng.GetBytes(buffer);

                for (int i = 0; i < length; i++)
                {
                    var index = buffer[i] % chars.Length;
                    result[i] = chars[index];
                }
            }

            return new string(result);
        }
    }
}
