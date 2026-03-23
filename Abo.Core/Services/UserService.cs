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

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public UserService(ILogger<UserService> logger)
        {
            _logger = logger;
            if (!File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, "{}");
            }
        }

        private Dictionary<string, User> ReadAll()
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<string, User>>(json) ?? new Dictionary<string, User>();
        }

        private void WriteAll(Dictionary<string, User> users)
        {
            var json = JsonSerializer.Serialize(users, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }

        public List<User> GetAllUsers()
        {
            lock (_lock)
            {
                try
                {
                    return ReadAll().Values.ToList();
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
                    var dict = users.ToDictionary(u => u.Username, u => u);
                    WriteAll(dict);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error writing to users database.");
                }
            }
        }

        public User GetOrCreateUser(string userId, string username = "")
        {
            lock (_lock)
            {
                try
                {
                    var users = ReadAll();
                    var user = users.Values.FirstOrDefault(u => u.MattermostId == userId);

                    if (user == null)
                    {
                        var name = string.IsNullOrWhiteSpace(username) ? userId : username;
                        user = new User { MattermostId = userId, Username = name };
                        users[name] = user;
                        WriteAll(users);
                    }
                    else if (!string.IsNullOrWhiteSpace(username) && user.Username != username)
                    {
                        users.Remove(user.Username);
                        user.Username = username;
                        users[username] = user;
                        WriteAll(users);
                    }

                    return user;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in GetOrCreateUser.");
                    return new User { MattermostId = userId, Username = string.IsNullOrWhiteSpace(username) ? userId : username };
                }
            }
        }

        public void UpdateUser(User updatedUser)
        {
            lock (_lock)
            {
                try
                {
                    var users = ReadAll();
                    users[updatedUser.Username] = updatedUser;
                    WriteAll(users);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating user.");
                }
            }
        }

        /// <summary>
        /// Checks whether the user identified by their Mattermost ID has a specific role.
        /// </summary>
        public bool HasRole(string mattermostId, string role)
        {
            lock (_lock)
            {
                try
                {
                    var users = ReadAll();
                    var user = users.Values.FirstOrDefault(u => u.MattermostId == mattermostId);
                    return user?.Roles.Contains(role, StringComparer.OrdinalIgnoreCase) ?? false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking role for user {MattermostId}.", mattermostId);
                    return false;
                }
            }
        }

        /// <summary>
        /// Adds a role to the user identified by MattermostId. Creates the user if not found.
        /// </summary>
        public void AddRole(string mattermostId, string role)
        {
            lock (_lock)
            {
                try
                {
                    var users = ReadAll();
                    var user = users.Values.FirstOrDefault(u => u.MattermostId == mattermostId);
                    if (user == null) return;
                    if (!user.Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                    {
                        user.Roles.Add(role);
                        WriteAll(users);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error adding role '{Role}' for user {MattermostId}.", role, mattermostId);
                }
            }
        }

        /// <summary>
        /// Removes a role from the user identified by MattermostId.
        /// </summary>
        public void RemoveRole(string mattermostId, string role)
        {
            lock (_lock)
            {
                try
                {
                    var users = ReadAll();
                    var user = users.Values.FirstOrDefault(u => u.MattermostId == mattermostId);
                    if (user == null) return;
                    var removed = user.Roles.RemoveAll(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
                    if (removed > 0) WriteAll(users);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error removing role '{Role}' for user {MattermostId}.", role, mattermostId);
                }
            }
        }
    }
}
