namespace SysLoja.PrinterTool.Models
{
    public enum HealthStatus { Healthy, Warning, Problem, Unknown }

    public sealed class HealthItem
    {
        public string Name { get; init; } = string.Empty;
        public HealthStatus Status { get; set; } = HealthStatus.Unknown;
        public string Evidence { get; set; } = string.Empty;
        public string ProbableCause { get; set; } = string.Empty;
        public string SuggestedFix { get; set; } = string.Empty;
    }
}
