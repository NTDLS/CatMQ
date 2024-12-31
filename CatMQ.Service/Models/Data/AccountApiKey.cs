namespace CatMQ.Service.Models.Data
{
    public class AccountApiKey
    {
        public Guid? Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string? Description { get; set; } = string.Empty;
    }
}

