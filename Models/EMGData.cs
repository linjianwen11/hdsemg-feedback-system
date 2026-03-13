namespace EMGFeedbackSystem.Models
{
    public class EMGData
    {
        public ushort SequenceNumber { get; set; }
        public ushort BatteryVoltage { get; set; }
        public double[] ChannelValues { get; set; } = new double[64];
        public double[] AbsMeanValues { get; set; } = new double[64];
    }

    public class ElectrodeData
    {
        public double CurrentValueA { get; set; }
        public double CurrentValueB { get; set; }
        public double CurrentValueC { get; set; }
        public double MaxValueA { get; set; }
        public double MaxValueB { get; set; }
        public double MaxValueC { get; set; }
    }
}
