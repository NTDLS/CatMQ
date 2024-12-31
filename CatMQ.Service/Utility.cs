using System.Security.Cryptography;

namespace CatMQ.Service
{
    public class Utility
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

        public static bool IsPasswordComplex(string password, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrEmpty(password))
            {
                errorMessage = "Password cannot be empty.";
                return false;
            }

            if (password.Length < 8)
            {
                errorMessage = "Password must be at least 8 characters long.";
                return false;
            }

            if (!password.Any(char.IsUpper))
            {
                errorMessage = "Password must contain at least one uppercase letter.";
                return false;
            }

            if (!password.Any(char.IsLower))
            {
                errorMessage = "Password must contain at least one lowercase letter.";
                return false;
            }

            if (!password.Any(char.IsDigit))
            {
                errorMessage = "Password must contain at least one digit.";
                return false;
            }

            if (password.Any(char.IsWhiteSpace))
            {
                errorMessage = "Password cannot contain spaces.";
                return false;
            }

            return true;
        }
    }
}
