using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ES.SFTP.Host.SSH.Configuration
{
    public class MatchBlock
    {
        public enum MatchCriteria
        {
            All,
            User,
            Group
        }

        public MatchCriteria Criteria { get; set; } = MatchCriteria.All;

        public List<string> Match { get; set; } = new List<string>();
        public List<string> Except { get; set; } = new List<string>();
        public List<string> Declarations { get; set; } = new List<string>();

        private string GetPatternLine()
        {
            var builder = new StringBuilder();
            builder.Append($"Match {Criteria} ");
            var patternList = (Match ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => $"{s.Trim()}").Distinct().ToList();
            patternList.AddRange((Except ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => $"!{s.Trim()}").Distinct().ToList());
            var exceptList = string.Join(",", patternList);
            if (!string.IsNullOrWhiteSpace(exceptList)) builder.Append($"\"{exceptList}\"");
            return builder.ToString();
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendLine(GetPatternLine());
            foreach (var declaration in (Declarations ?? new List<string>()).Where(declaration =>
                !string.IsNullOrWhiteSpace(declaration)))
                builder.AppendLine(declaration?.Trim());
            return builder.ToString();
        }
    }
}