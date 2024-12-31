namespace CatMQ.Service.Models.Data
{
    public class Account
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? PasswordHash { get; set; } = string.Empty;
        public string? Description { get; set; } = string.Empty;
        public string? TimeZone { get; set; }
        public List<AccountApiKey> ApiKeys { get; set; } = new();
    }
}
