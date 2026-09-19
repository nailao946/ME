using System;
using System.Collections.Generic;
using System.Linq;
using ME.Models;

namespace ME.Data
{
    public class FocusSessionRepository
    {
        private const string FileName = "focus_sessions";

        public List<FocusSession> GetAllSessions()
        {
            return JsonStore.Load<FocusSession>(FileName)
                .OrderByDescending(s => s.StartTime).ToList();
        }

        public List<FocusSession> GetSessionsByGoalId(int goalId)
        {
            return JsonStore.Load<FocusSession>(FileName)
                .Where(s => s.GoalId == goalId)
                .OrderByDescending(s => s.StartTime).ToList();
        }

        public int InsertSession(FocusSession session)
        {
            var sessions = JsonStore.Load<FocusSession>(FileName);
            var maxId = sessions.Count > 0 ? sessions.Max(s => s.Id) : 0;
            session.Id = maxId + 1;
            session.Uid = string.IsNullOrEmpty(session.Uid) ? Guid.NewGuid().ToString("N") : session.Uid;
            sessions.Add(session);
            JsonStore.Save(FileName, sessions);
            return session.Id;
        }

        public TimeSpan GetTotalFocusTimeByGoalId(int goalId)
        {
            var sessions = JsonStore.Load<FocusSession>(FileName)
                .Where(s => s.GoalId == goalId);
            var totalSeconds = sessions.Sum(s => (long)s.Duration.TotalSeconds);
            return TimeSpan.FromSeconds(totalSeconds);
        }
    }
}
