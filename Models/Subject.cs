namespace EMGFeedbackSystem.Models
{
    public class Subject
    {
        public int Id { get; set; }
        public string SubjectId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public int Age { get; set; }
        public string? Notes { get; set; }
        public double UpperLimit { get; set; }
        public double LeftLegMaxA { get; set; }
        public double LeftLegMaxB { get; set; }
        public double LeftLegMaxC { get; set; }
        public double RightLegMaxA { get; set; }
        public double RightLegMaxB { get; set; }
        public double RightLegMaxC { get; set; }
        public string LeftLegSide { get; set; } = string.Empty;
        public string RightLegSide { get; set; } = string.Empty;
    }
}
