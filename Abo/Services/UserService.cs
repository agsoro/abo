using System.Text.Json;
using Abo.Models;
using Microsoft.Extensions.Logging;

namespace Abo.Services
{
    public class UserService
    {
        private readonly string _filePath = "Data/users.json";
        private readonly ILogger<UserService> _logger;
        private readonly object _lock = new();

        public UserService(ILogger<UserService> logger)
        {
            _logger = logger;
            if (!File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, "[]");
            }
        }

        public List<User> GetAllUsers()
        {
            lock (_lock)
            {
                try
                {
                    var json = File.ReadAllText(_filePath);
                    return JsonSerializer.Deserialize<List<User>>(json) ?? new List<User>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading users database.");
                    return new List<User>();
                }
            }
        }

        public void SaveUsers(List<User> users)
        {
            lock (_lock)
            {
                try
                {
                    var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_filePath, json);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error writing to users database.");
                }
            }
        }

        public User GetOrCreateUser(string userId, string username = "")
        {
            var users = GetAllUsers();
            var user = users.FirstOrDefault(u => u.Id == userId);
            
            if (user == null)
            {
                user = new User { Id = userId, Username = string.IsNullOrWhiteSpace(username) ? userId : username };
                users.Add(user);
                SaveUsers(users);
            }
            else if (!string.IsNullOrWhiteSpace(username) && user.Username != username)
            {
                user.Username = username;
                SaveUsers(users);
            }

            return user;
        }

        public void UpdateUser(User updatedUser)
        {
            var users = GetAllUsers();
            var index = users.FindIndex(u => u.Id == updatedUser.Id);
            
            if (index != -1)
            {
                users[index] = updatedUser;
                SaveUsers(users);
            }
        }
    }
}
