namespace Abo.Models
{
    public class User
    {
        public string Username { get; set; } = string.Empty;
        public string MattermostId { get; set; } = string.Empty;
        public string Language { get; set; } = "de-de";
        public bool IsSubscribedToQuiz { get; set; } = false;
        public List<string> Roles { get; set; } = new();
    }
}
