namespace Abo.Models
{
    public class User
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Language { get; set; } = "de-de";
        public int Score { get; set; }
        public bool IsSubscribedToQuiz { get; set; } = false;
    }
}
