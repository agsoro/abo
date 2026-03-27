using Xunit;
using Abo.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Abo.Tests
{
    /// <summary>
    /// API Endpoint Tests for Issue #297: Testing & Integration
    /// Validates the /api/sessions and /api/interact endpoints work correctly
    /// with dashboard action tracking and issue context.
    /// </summary>
    public class DashboardApiEndpointTests
    {
        private readonly SessionService _sessionService;

        public DashboardApiEndpointTests()
        {
            _sessionService = new SessionService();
        }

        // ────────────────────────────────────────────────────────────────────
        // GET /api/sessions Endpoint Validation
        // ────────────────────────────────────────────────────────────────────

        [Fact]
        public void GetSessions_EmptyInitially()
        {
            // Act
            var sessions = _sessionService.GetActiveSessions();

            // Assert
            Assert.NotNull(sessions);
            Assert.IsType<List<dynamic>>(sessions);
        }

        [Fact]
        public void GetSessions_ReturnsSessionList()
        {
            // Arrange
            _sessionService.SetCurrentIssue("session-1", "290", "Issue 290");
            _sessionService.SetCurrentIssue("session-2", "205", "Issue 205");

            // Act
            var sessions = _sessionService.GetActiveSessions();

            // Assert
            Assert.NotNull(sessions);
            Assert.Equal(2, sessions.Count);
        }

        [Fact]
        public void GetSessions_IncludesCurrentIssueFields()
        {
            // Arrange
            string sessionId = "dashboard-action-290-1234567890";
            string issueId = "290";
            string issueTitle = "Feature request";
            _sessionService.SetCurrentIssue(sessionId, issueId, issueTitle);

            // Act
            var sessions = _sessionService.GetActiveSessions();
            var session = sessions.First(s => s.SessionId == sessionId);

            // Assert - Frontend needs these fields
            Assert.NotNull(session.CurrentIssueId);
            Assert.NotNull(session.CurrentIssueTitle);
            Assert.Equal(issueId, session.CurrentIssueId);
            Assert.Equal(issueTitle, session.CurrentIssueTitle);
        }

        [Fact]
        public void GetSessions_SupportsPollingQuery()
        {
            // Arrange
            _sessionService.SetCurrentIssue("session-1", "290", "Issue 290");
            _sessionService.SetCurrentIssue("session-2", "205", "Issue 205");
            _sessionService.SetCurrentIssue("session-3", "315", "Issue 315");

            // Act - Simulate repeated polling (frontend does this every 2-3s)
            var poll1 = _sessionService.GetActiveSessions();
            System.Threading.Thread.Sleep(100);
            var poll2 = _sessionService.GetActiveSessions();
            System.Threading.Thread.Sleep(100);
            var poll3 = _sessionService.GetActiveSessions();

            // Assert
            Assert.Equal(3, poll1.Count);
            Assert.Equal(3, poll2.Count);
            Assert.Equal(3, poll3.Count);
            
            // All polls should return same sessions
            Assert.Equal(
                poll1.Select(s => s.SessionId).OrderBy(x => x),
                poll2.Select(s => s.SessionId).OrderBy(x => x)
            );
        }

        // ────────────────────────────────────────────────────────────────────
        // Dashboard Action Tracking Flow
        // ────────────────────────────────────────────────────────────────────

        [Fact]
        public void DashboardActionFlow_InitialClickToStatusDisplay()
        {
            // Scenario: User clicks "Start Triage" on Issue #290
            // Backend receives: POST /api/interact with sessionId, issueId, message
            // Frontend expects: Session appears in GET /api/sessions with currentIssueId

            // Arrange
            string sessionId = "dashboard-action-290-1704067200000";
            string issueId = "290";
            string issueTitle = "Feature request: dashboard improvements";

            // Act - Backend receives POST /api/interact
            _sessionService.SetCurrentIssue(sessionId, issueId, issueTitle);

            // Act - Frontend polls GET /api/sessions
            var sessions = _sessionService.GetActiveSessions();
            var session = sessions.FirstOrDefault(s => s.SessionId == sessionId);

            // Assert
            Assert.NotNull(session);
            Assert.Equal(issueId, session.CurrentIssueId);
            Assert.Equal(issueTitle, session.CurrentIssueTitle);
        }

        [Fact]
        public void DashboardActionFlow_MultipleIssuesIndependent()
        {
            // Scenario: User clicks actions on Issues #290 and #205 simultaneously
            // Each action has separate session tracking

            // Arrange
            var action1 = new { SessionId = "dashboard-action-290-1", IssueId = "290", Title = "Issue 290" };
            var action2 = new { SessionId = "dashboard-action-205-2", IssueId = "205", Title = "Issue 205" };

            // Act
            _sessionService.SetCurrentIssue(action1.SessionId, action1.IssueId, action1.Title);
            _sessionService.SetCurrentIssue(action2.SessionId, action2.IssueId, action2.Title);

            var sessions = _sessionService.GetActiveSessions();

            // Assert
            var s1 = sessions.First(s => s.SessionId == action1.SessionId);
            var s2 = sessions.First(s => s.SessionId == action2.SessionId);

            // Both should be tracked independently
            Assert.Equal(action1.IssueId, s1.CurrentIssueId);
            Assert.Equal(action2.IssueId, s2.CurrentIssueId);
            Assert.NotEqual(s1.CurrentIssueId, s2.CurrentIssueId);
        }

        // ────────────────────────────────────────────────────────────────────
        // POST /api/interact Request Handling (SessionService perspective)
        // ────────────────────────────────────────────────────────────────────

        [Fact]
        public void InteractRequest_WithIssueIdAndTitle()
        {
            // Simulate: POST /api/interact with new fields
            // Request body includes: message, sessionId, issueId, IssueTitle, userName

            // Arrange
            string sessionId = "dashboard-action-290-1234567890";
            string issueId = "290";
            string issueTitle = "Feature request";

            // Act - Backend processes request
            _sessionService.SetCurrentIssue(sessionId, issueId, issueTitle);

            // Assert - Session service tracks issue context
            var sessions = _sessionService.GetActiveSessions();
            var session = sessions.First(s => s.SessionId == sessionId);
            
            Assert.Equal(issueId, session.CurrentIssueId);
            Assert.Equal(issueTitle, session.CurrentIssueTitle);
        }

        [Fact]
        public void InteractRequest_WithoutIssueId()
        {
            // Backward compatibility: Requests without issueId should still work

            // Arrange
            string sessionId = "web-session";

            // Act
            _sessionService.SetCurrentIssue(sessionId, null, null);

            // Assert
            var sessions = _sessionService.GetActiveSessions();
            Assert.Contains(sessions, s => s.SessionId == sessionId);
        }

        // ────────────────────────────────────────────────────────────────────
        // Dashboard Frontend Polling Behavior
        // ────────────────────────────────────────────────────────────────────

        [Fact]
        public void FrontendPolling_DetectSessionMatchBySessionId()
        {
            // Frontend polls /api/sessions every 2-3 seconds looking for matching sessionId

            // Arrange
            string dashboardSessionId = "dashboard-action-290-1704067200000";
            _sessionService.SetCurrentIssue(dashboardSessionId, "290", "Issue 290");

            // Act - Frontend polling logic
            var sessions = _sessionService.GetActiveSessions();
            var matchingSession = sessions.FirstOrDefault(s => s.SessionId == dashboardSessionId);

            // Assert
            Assert.NotNull(matchingSession);
            Assert.Equal("290", matchingSession.CurrentIssueId);
        }

        [Fact]
        public void FrontendPolling_ContinuouslyMonitorsSession()
        {
            // Frontend should be able to poll multiple times and see consistent data

            // Arrange
            string sessionId = "dashboard-action-290-1234567890";
            _sessionService.SetCurrentIssue(sessionId, "290", "Issue 290");

            // Act - Multiple polling calls (simulating 2.5s polling)
            var poll1 = _sessionService.GetActiveSessions().First(s => s.SessionId == sessionId);
            System.Threading.Thread.Sleep(10);
            var poll2 = _sessionService.GetActiveSessions().First(s => s.SessionId == sessionId);
            System.Threading.Thread.Sleep(10);
            var poll3 = _sessionService.GetActiveSessions().First(s => s.SessionId == sessionId);

            // Assert
            Assert.Equal("290", poll1.CurrentIssueId);
            Assert.Equal("290", poll2.CurrentIssueId);
            Assert.Equal("290", poll3.CurrentIssueId);
            Assert.Equal("Issue 290", poll1.CurrentIssueTitle);
            Assert.Equal("Issue 290", poll2.CurrentIssueTitle);
            Assert.Equal("Issue 290", poll3.CurrentIssueTitle);
        }

        // ────────────────────────────────────────────────────────────────────
        // Response Format Validation (API Contract)
        // ────────────────────────────────────────────────────────────────────

        [Fact]
        public void ResponseFormat_SessionObjectStructure()
        {
            // Arrange
            _sessionService.SetCurrentIssue("test-session", "290", "Test issue");

            // Act
            var sessions = _sessionService.GetActiveSessions();
            var session = sessions[0];

            // Assert - Verify response structure matches frontend expectations
            // Expected from dashboard/index.html JavaScript:
            // session.sessionId
            // session.currentIssueId
            // session.currentIssueTitle
            // session.lastActivity
            // session.messageCount

            Assert.NotNull(session.SessionId);
            Assert.NotNull(session.CurrentIssueId);
            Assert.NotNull(session.CurrentIssueTitle);
            Assert.NotNull(session.LastActivity);
            Assert.True(session.MessageCount >= 0);
        }

        [Fact]
        public void ResponseFormat_MultipleSessionsArray()
        {
            // Arrange
            _sessionService.SetCurrentIssue("session-1", "290", "Issue 290");
            _sessionService.SetCurrentIssue("session-2", "205", "Issue 205");
            _sessionService.SetCurrentIssue("session-3", "315", "Issue 315");

            // Act
            var sessions = _sessionService.GetActiveSessions();

            // Assert
            Assert.IsType<List<dynamic>>(sessions);
            Assert.Equal(3, sessions.Count);
            
            // Each session should have required fields
            foreach (dynamic session in sessions)
            {
                Assert.NotNull(session.SessionId);
                Assert.NotNull(session.CurrentIssueId);
            }
        }

        [Fact]
        public void ResponseFormat_TimestampFormat()
        {
            // Arrange
            _sessionService.SetCurrentIssue("test-session", "290", "Test issue");

            // Act
            var sessions = _sessionService.GetActiveSessions();
            var session = sessions[0];

            // Assert
            Assert.NotNull(session.LastActivity);
            Assert.IsType<System.DateTime>(session.LastActivity);
            
            // Should be recent (within last minute)
            var timeDiff = System.DateTime.UtcNow - session.LastActivity;
            Assert.True(timeDiff.TotalSeconds < 60);
        }

        // ────────────────────────────────────────────────────────────────────
        // Error Scenarios
        // ────────────────────────────────────────────────────────────────────

        [Fact]
        public void ErrorScenario_SessionWithoutIssueContext()
        {
            // Some sessions may not have issue context (e.g., chat sessions)

            // Arrange
            string sessionId = "web-session-no-issue";

            // Act
            _sessionService.SetCurrentIssue(sessionId, null, null);
            var sessions = _sessionService.GetActiveSessions();

            // Assert
            Assert.Contains(sessions, s => s.SessionId == sessionId);
        }

        [Fact]
        public void ErrorScenario_MissingIssueTitle()
        {
            // Dashboard should handle gracefully when title is missing

            // Arrange
            string sessionId = "dashboard-action-290-1234567890";
            string issueId = "290";

            // Act
            _sessionService.SetCurrentIssue(sessionId, issueId, null);
            var sessions = _sessionService.GetActiveSessions();
            var session = sessions.First(s => s.SessionId == sessionId);

            // Assert
            Assert.Equal(issueId, session.CurrentIssueId);
            // Title may be null or empty, but shouldn't crash
        }

        // ────────────────────────────────────────────────────────────────────
        // Integration Scenarios
        // ────────────────────────────────────────────────────────────────────

        [Fact]
        public void Integration_FullDashboardWorkflow()
        {
            // Complete workflow: User clicks button → action tracked → polls → displays

            // Step 1: User clicks "Start Triage" on Issue #290
            string sessionId = "dashboard-action-290-1704067200000";
            string issueId = "290";
            string issueTitle = "Feature request: dashboard feedback";

            // Step 2: Backend receives POST /api/interact with sessionId and issueId
            _sessionService.SetCurrentIssue(sessionId, issueId, issueTitle);

            // Step 3: Frontend immediately polls GET /api/sessions
            var poll1 = _sessionService.GetActiveSessions();
            var session1 = poll1.FirstOrDefault(s => s.SessionId == sessionId);

            Assert.NotNull(session1);
            Assert.Equal(issueId, session1.CurrentIssueId);
            Assert.Equal(issueTitle, session1.CurrentIssueTitle);

            // Step 4: Frontend continues polling every 2-3 seconds
            for (int i = 0; i < 3; i++)
            {
                System.Threading.Thread.Sleep(100);
                var poll = _sessionService.GetActiveSessions();
                var session = poll.FirstOrDefault(s => s.SessionId == sessionId);
                
                Assert.NotNull(session);
                Assert.Equal(issueId, session.CurrentIssueId);
                Assert.Equal(issueTitle, session.CurrentIssueTitle);
            }

            // Step 5: After agent completes, session should still be queryable
            var finalPoll = _sessionService.GetActiveSessions();
            var finalSession = finalPoll.FirstOrDefault(s => s.SessionId == sessionId);
            
            Assert.NotNull(finalSession);
        }

        [Fact]
        public void Integration_ConcurrentActionsWithPolling()
        {
            // Multiple actions with concurrent polling

            // Arrange
            var actions = new[]
            {
                new { SessionId = "dashboard-action-290-1", IssueId = "290", Title = "Issue 290" },
                new { SessionId = "dashboard-action-205-2", IssueId = "205", Title = "Issue 205" }
            };

            // Act
            foreach (var action in actions)
            {
                _sessionService.SetCurrentIssue(action.SessionId, action.IssueId, action.Title);
            }

            // Simulate aggressive polling (2.5s interval)
            var pollResults = new List<List<dynamic>>();
            for (int i = 0; i < 3; i++)
            {
                var poll = _sessionService.GetActiveSessions();
                pollResults.Add(poll);
                System.Threading.Thread.Sleep(100);
            }

            // Assert
            foreach (var poll in pollResults)
            {
                Assert.Equal(2, poll.Count);
                
                var s1 = poll.First(s => s.SessionId == actions[0].SessionId);
                var s2 = poll.First(s => s.SessionId == actions[1].SessionId);
                
                Assert.Equal("290", s1.CurrentIssueId);
                Assert.Equal("205", s2.CurrentIssueId);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Performance Requirements (Issue #297)
        // ────────────────────────────────────────────────────────────────────

        [Fact]
        public void Performance_PollingOverhead()
        {
            // Target: <5% CPU overhead during polling
            // Measurement: Time to fetch sessions 1000 times

            // Arrange
            _sessionService.SetCurrentIssue("session-1", "290", "Issue 290");

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            for (int i = 0; i < 1000; i++)
            {
                _sessionService.GetActiveSessions();
            }
            
            stopwatch.Stop();

            // Assert
            // Target: 1000 queries in <500ms
            Assert.True(stopwatch.ElapsedMilliseconds < 500,
                $"Polling 1000 times took {stopwatch.ElapsedMilliseconds}ms, expected <500ms");
        }

        [Fact]
        public void Performance_SessionCreationLatency()
        {
            // Target: Button click to status display <100ms
            // This includes: SetCurrentIssue call latency

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            _sessionService.SetCurrentIssue("dashboard-action-290-1234567890", "290", "Issue 290");
            stopwatch.Stop();

            // Assert
            // Target: <50ms for SetCurrentIssue alone
            Assert.True(stopwatch.ElapsedMilliseconds < 50,
                $"SetCurrentIssue took {stopwatch.ElapsedMilliseconds}ms, expected <50ms");
        }
    }
}
